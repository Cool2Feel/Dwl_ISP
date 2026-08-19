using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Ui.CommonCustomControl;

namespace ThunderSE.Ui.SettingWindow.YGamma
{
    /// <summary>
    /// YGammaOfflineIQWindow.xaml 的交互逻辑
    /// </summary>
    public partial class YGammaOfflineIQWindow : Window
    {
        private Processor _ispProcessor;
        private byte[] _rgbBuffer;
        private byte[] _processedRgbBuffer;
        private List<RubberBandData> _rubberBandData = new List<RubberBandData>();
        private ProcessStep _yGammaStep;

        public bool IsLoadImage
        {
            get { return (bool)GetValue(IsLoadImageProperty); }
            set { SetValue(IsLoadImageProperty, value); }
        }

        public static readonly DependencyProperty IsLoadImageProperty = DependencyProperty.Register(
            "IsLoadImage",
            typeof(bool),
            typeof(YGammaOfflineIQWindow),
            new FrameworkPropertyMetadata(false));

        public YGammaOfflineIQWindow(Processor ispProcessor)
        {
            _ispProcessor = ispProcessor;
            _yGammaStep = _ispProcessor.RgbFileProcessSteps[IspModule.YGamma];

            InitializeComponent();

            this.KeyDown += Window_KeyDown;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("就绪 - YGamma离线IQ调试工具已就绪");
            UpdateProcessingStatus("等待加载图像...");
            UpdateSelectionCount(0);
            TxtWindowSize.Text = $"窗口尺寸: {this.ActualWidth:F0} x {this.ActualHeight:F0}";

            this.SizeChanged += Window_SizeChanged;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (TxtWindowSize != null)
            {
                TxtWindowSize.Text = $"窗口尺寸: {e.NewSize.Width:F0} x {e.NewSize.Height:F0}";
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.O:
                        OnLoadRgbButtonClick(null, null);
                        e.Handled = true;
                        break;
                    case Key.Z:
                        OnUndoClick(null, null);
                        e.Handled = true;
                        break;
                    case Key.Enter:
                        OnCalcIQClick(null, null);
                        e.Handled = true;
                        break;
                    case Key.R:
                        OnResetViewClick(null, null);
                        e.Handled = true;
                        break;
                }
            }
        }

        private void UpdateStatusBar(string message)
        {
            if (StatusBarText != null)
            {
                StatusBarText.Text = message;
            }
        }

        private void UpdateProcessingStatus(string status)
        {
            if (TxtProcessingStatus != null)
            {
                TxtProcessingStatus.Text = status;
            }
        }

        private void UpdateOperationStatus(string status)
        {
            if (TxtOperationStatus != null)
            {
                TxtOperationStatus.Text = status;
            }
        }

        private void UpdateSelectionCount(int count)
        {
            if (TxtSelectionCount != null)
            {
                TxtSelectionCount.Text = $"选区数量: {count}";
            }
        }

        private void UpdateImageInfo(string info)
        {
            if (TxtImageInfo != null)
            {
                TxtImageInfo.Text = info;
            }
        }

        private void UpdateImageHint(string hint)
        {
            if (TxtImageHint != null)
            {
                TxtImageHint.Text = hint;
            }
        }

        private void OnProcessedImageUpdated(object sender, DataTransferEventArgs e)
        {
            UpdateStatusBar("YGamma处理完成 - 结果已显示在'YGamma效果'标签页");
            UpdateProcessingStatus("处理完成 ✓");
            UpdateOperationStatus("处理完成");
        }

        private unsafe void ShowProcessedImage(IntPtr[] outputPtr, int width, int height)
        {
            byte[] rgb24Buffer = new byte[width * height * 3];

            fixed (byte* rgb24Ptr = rgb24Buffer)
            {
                for (int i = 0; i < width * height; i++)
                {
                    rgb24Ptr[i * 3 + 2] = (byte)(((short*)outputPtr[0])[i] >> 2);
                    rgb24Ptr[i * 3 + 1] = (byte)(((short*)outputPtr[1])[i] >> 2);
                    rgb24Ptr[i * 3 + 0] = (byte)(((short*)outputPtr[2])[i] >> 2);
                }
            }

            var bitmap = BitmapSource.Create(
                width, height,
                96, 96,
                PixelFormats.Rgb24,
                null,
                rgb24Buffer,
                width * 3);

            ProcessedImage.Source = bitmap;
        }

        /*
        private void OnCalcIQClick(object sender, RoutedEventArgs e)
        {
            // 1. ������֤
            if (_rubberBandData.Count == 0)
            {
                MessageBox.Show("���ȿ�ѡ��������");
                return;
            }

            // 2. ��ȡ ROI ����
            int[] XArray = new int[6];
            int[] YArray = new int[6];
            int[] HeightArray = new int[6];
            int[] WidthArray = new int[6];

            for (int j = 0; j < _rubberBandData.Count; j++)
            {
                XArray[j] = _rubberBandData[j].x;
                YArray[j] = _rubberBandData[j].y;
                HeightArray[j] = _rubberBandData[j].height;
                WidthArray[j] = _rubberBandData[j].width;
            }

            // 3. ��ȡͼ��ߴ�
            int imgWidth = (int)OriginImg.DisplayImageSource.Width;
            int imgHeight = (int)OriginImg.DisplayImageSource.Height;

            // 4. ׼���������������
            IntPtr[] inputPtr = new IntPtr[3];
            IntPtr[] outputPtr = new IntPtr[3];

            try
            {
                // �����ڴ�
                for (int i = 0; i < 3; i++)
                {
                    inputPtr[i] = MemoryManager.AllocateMemory(imgWidth * imgHeight * sizeof(short));
                    outputPtr[i] = MemoryManager.AllocateMemory(imgWidth * imgHeight * sizeof(short));
                }

                // 5. �� RGB24 ת��Ϊ RGB16 �����Ƶ����뻺����
                unsafe
                {
                    fixed (byte* rgb24Ptr = _rgbBuffer)
                    {
                        for (int i = 0; i < imgWidth * imgHeight; i++)
                        {
                            short* rPtr = (short*)inputPtr[0];
                            short* gPtr = (short*)inputPtr[1];
                            short* bPtr = (short*)inputPtr[2];

                            rPtr[i] = (short)(rgb24Ptr[i * 3 + 2] << 2);  // R
                            gPtr[i] = (short)(rgb24Ptr[i * 3 + 1] << 2);  // G
                            bPtr[i] = (short)(rgb24Ptr[i * 3 + 0] << 2);  // B
                        }
                    }
                }

                // 6. Ӧ�� Gamma У��
                IspApi.YGammaImg(imgWidth, imgHeight, 1, _yGamma.using_ygama, inputPtr, outputPtr);

                // 7. ���� ROI ���� RGB ��ֵ
                double[] avgR = new double[6];
                double[] avgG = new double[6];
                double[] avgB = new double[6];

                unsafe
                {
                    for (int i = 0; i < _rubberBandData.Count; i++)
                    {
                        int startX = XArray[i];
                        int startY = YArray[i];
                        int width = WidthArray[i];
                        int height = HeightArray[i];

                        long sumR = 0, sumG = 0, sumB = 0;
                        int pixelCount = width * height;

                        for (int y = startY; y < startY + height; y++)
                        {
                            for (int x = startX; x < startX + width; x++)
                            {
                                int offset = y * imgWidth + x;
                                sumR += ((short*)outputPtr[0])[offset];
                                sumG += ((short*)outputPtr[1])[offset];
                                sumB += ((short*)outputPtr[2])[offset];
                            }
                        }

                        avgR[i] = (double)sumR / pixelCount;
                        avgG[i] = (double)sumG / pixelCount;
                        avgB[i] = (double)sumB / pixelCount;
                    }
                }

                // 8. ���� IQ ����
                double[] diff_l = new double[6] { 10, 10, 10, 10, 10, 10 };
                int ref_count = 0;
                double[] l_val_array = new double[6];
                double[] delta_l_array = new double[6];
                double yMax = 0.0;
                double[] yAvg = new double[18];
                double out_gamma = 0.0;

                IspApi.YGAMMA_IQ(avgR, avgG, avgB, 6,
                    diff_l, ref ref_count, l_val_array, delta_l_array,
                    ref yMax, yAvg, ref out_gamma);

                // 9. ���½����ʾ
                var results = new ObservableCollection<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("ref_count", ref_count.ToString()),
            new KeyValuePair<string, string>("l_val_array",
                string.Join(",", l_val_array.Select(x => x.ToString("0.00")))),
            new KeyValuePair<string, string>("delta_l_array",
                string.Join(",", delta_l_array.Select(x => x.ToString("0.00")))),
            new KeyValuePair<string, string>("yMax", yMax.ToString("0.00")),
            new KeyValuePair<string, string>("out_gamma", out_gamma.ToString("0.00"))
        };

                DataContext = results;

                // 10. ��ʾ�������ͼ��
                ShowProcessedImage(outputPtr, imgWidth, imgHeight);
            }
            finally
            {
                // �ͷ��ڴ�
                for (int i = 0; i < 3; i++)
                {
                    if (inputPtr[i] != IntPtr.Zero)
                        MemoryManager.FreeMemory(inputPtr[i]);
                    if (outputPtr[i] != IntPtr.Zero)
                        MemoryManager.FreeMemory(outputPtr[i]);
                }
            }
        }

        */

        private void OnLoadRgbButtonClick(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "rgb文件(*.rgb) | *.rgb";
            if (!(bool)openFileDialog.ShowDialog())
            {
                return;
            }

            try
            {
                _rgbBuffer = File.ReadAllBytes(openFileDialog.FileName);
                OriginImg.DisplayImageSource = _ispProcessor.GenerateBitmapUsingRgb(_rgbBuffer);

                IsLoadImage = true;
                OriginImg.IsEnabled = true;

                UpdateStatusBar($"已加载图像: {openFileDialog.FileName} | 大小: {_rgbBuffer.Length} 字节");
                UpdateProcessingStatus("图像已加载 ✓");
                UpdateOperationStatus("图像已加载");

                if (OriginImg.DisplayImageSource != null)
                {
                    int width = (int)OriginImg.DisplayImageSource.Width;
                    int height = (int)OriginImg.DisplayImageSource.Height;
                    UpdateImageInfo($"尺寸: {width} x {height}\n大小: {_rgbBuffer.Length:N0} 字节\n格式: RGB24");
                    UpdateImageHint("图像已加载，请在原图上框选需要处理的区域");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载图像失败: {ex.Message}", "错误",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar("加载图像失败");
                UpdateProcessingStatus("加载失败 ✗");
            }
        }

        private void OnUndoClick(object sender, RoutedEventArgs e)
        {
            OriginImg.UndoDrawRubberBand();
            UpdateStatusBar("已撤销选区");
            UpdateOperationStatus("已撤销选区");
        }

        private void OnCalcIQClick(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("正在执行YGamma离线IQ计算...");
            UpdateProcessingStatus("正在计算中...");
            UpdateOperationStatus("计算中");

            int[] XArray = new int[6];
            int[] YArray = new int[6];
            int[] HeightArray = new int[6];
            int[] WidthArray = new int[6];

            if (_rubberBandData.Count > 0)
            {
                UpdateSelectionCount(_rubberBandData.Count);

                for (int j = 0; j < _rubberBandData.Count; j++)
                {
                    XArray[j] = _rubberBandData[j].x;
                    YArray[j] = _rubberBandData[j].y;
                    HeightArray[j] = _rubberBandData[j].height;
                    WidthArray[j] = _rubberBandData[j].width;
                }

                UpdateStatusBar($"正在处理 {_rubberBandData.Count} 个选区的YGamma IQ数据...");
            }
            else
            {
                UpdateStatusBar("未检测到选区，请先在原图上选择处理区域");
                UpdateProcessingStatus("无选区数据");
                UpdateSelectionCount(0);
            }

            //_gammaStep.ProcessRgbBuffer(ref ptrArray);

            //IspApi.EncoderImgBuffer(ptrArray, _ispProcessor.IspCommonConfig.ResolutionWidth,
            //    _ispProcessor.IspCommonConfig.ResolutionHeight, null, ref size);

            //byte[] buffer = new byte[size];
            //IspApi.EncoderImgBuffer(ptrArray, _ispProcessor.IspCommonConfig.ResolutionWidth,
            //    _ispProcessor.IspCommonConfig.ResolutionHeight, buffer, ref size);
        }

        private void OnResetViewClick(object sender, RoutedEventArgs e)
        {
            OriginImg.UndoDrawRubberBand();

            ImgDisplayTab.SelectedIndex = 0;

            UpdateStatusBar("视图已重置到初始状态");
            UpdateProcessingStatus("等待操作...");
            UpdateOperationStatus("视图已重置");
            UpdateImageHint("请先加载RGB图像文件，然后在原图上框选处理区域");
        }
    }

    /*
    public partial class YGammaOfflineIQWindow : Window
    {
        private Processor _ispProcessor;
        private byte[] _rgbBuffer;
        private byte[] _processedRgbBuffer;
        private List<RubberBandData> _rubberBandData = new List<RubberBandData>();
        private ProcessStep _yGammaStep;

        public bool IsLoadImage
        {
            get { return (bool)GetValue(IsLoadImageProperty); }
            set { SetValue(IsLoadImageProperty, value); }
        }

        public static readonly DependencyProperty IsLoadImageProperty = DependencyProperty.Register(
            "IsLoadImage",
            typeof(bool),
            typeof(YGammaOfflineIQWindow),
            new FrameworkPropertyMetadata(false));

        public YGammaOfflineIQWindow(Processor ispProcessor)
        {
            _ispProcessor = ispProcessor;
            _yGammaStep = _ispProcessor.RgbFileProcessSteps[IspModule.YGamma];

            InitializeComponent();
        }

        private void OnProcessedImageUpdated(object sender, DataTransferEventArgs e)
        {

        }

        private void ShowProcessedImage(IntPtr[] outputPtr, int width, int height)
        {
            unsafe
            {
                // 创建 RGB24 缓冲区
                byte[] rgb24Buffer = new byte[width * height * 3];

                fixed (byte* rgb24Ptr = rgb24Buffer)
                {
                    for (int i = 0; i < width * height; i++)
                    {
                        // 将 RGB16 转换回 RGB24
                        rgb24Ptr[i * 3 + 2] = (byte)(((short*)outputPtr[0])[i] >> 2);  // R
                        rgb24Ptr[i * 3 + 1] = (byte)(((short*)outputPtr[1])[i] >> 2);  // G
                        rgb24Ptr[i * 3 + 0] = (byte)(((short*)outputPtr[2])[i] >> 2);  // B
                    }
                }

                // 创建 BitmapSource
                var bitmap = BitmapSource.Create(
                    width, height,
                    96, 96,
                    PixelFormats.Rgb24,
                    null,
                    rgb24Buffer,
                    width * 3);

                // 更新"YGamma效果" Tab 的图像
                ProcessedImage.Source = bitmap;
            }
        }

        private void OnCalcIQClick(object sender, RoutedEventArgs e)
        {
            // 1. 参数验证
            if (_rubberBandData.Count == 0)
            {
                MessageBox.Show("请先框选分析区域！");
                return;
            }

            // 2. 提取 ROI 坐标
            int[] XArray = new int[6];
            int[] YArray = new int[6];
            int[] HeightArray = new int[6];
            int[] WidthArray = new int[6];

            for (int j = 0; j < _rubberBandData.Count; j++)
            {
                XArray[j] = _rubberBandData[j].x;
                YArray[j] = _rubberBandData[j].y;
                HeightArray[j] = _rubberBandData[j].height;
                WidthArray[j] = _rubberBandData[j].width;
            }

            // 3. 获取图像尺寸
            int imgWidth = (int)OriginImg.DisplayImageSource.Width;
            int imgHeight = (int)OriginImg.DisplayImageSource.Height;

            // 4. 准备输入输出缓冲区
            IntPtr[] inputPtr = new IntPtr[3];
            IntPtr[] outputPtr = new IntPtr[3];

            try
            {
                // 分配内存
                for (int i = 0; i < 3; i++)
                {
                    inputPtr[i] = MemoryManager.AllocateMemory(imgWidth * imgHeight * sizeof(short));
                    outputPtr[i] = MemoryManager.AllocateMemory(imgWidth * imgHeight * sizeof(short));
                }

                // 5. 将 RGB24 转换为 RGB16 并复制到输入缓冲区
                unsafe
                {
                    fixed (byte* rgb24Ptr = _rgbBuffer)
                    {
                        for (int i = 0; i < imgWidth * imgHeight; i++)
                        {
                            short* rPtr = (short*)inputPtr[0];
                            short* gPtr = (short*)inputPtr[1];
                            short* bPtr = (short*)inputPtr[2];

                            rPtr[i] = (short)(rgb24Ptr[i * 3 + 2] << 2);  // R
                            gPtr[i] = (short)(rgb24Ptr[i * 3 + 1] << 2);  // G
                            bPtr[i] = (short)(rgb24Ptr[i * 3 + 0] << 2);  // B
                        }
                    }
                }

                // 6. 应用 Gamma 校正
                IspApi.YGammaImg(imgWidth, imgHeight, 1, _yGamma.using_ygama, inputPtr, outputPtr);

                // 7. 计算 ROI 区域 RGB 均值
                double[] avgR = new double[6];
                double[] avgG = new double[6];
                double[] avgB = new double[6];

                unsafe
                {
                    for (int i = 0; i < _rubberBandData.Count; i++)
                    {
                        int startX = XArray[i];
                        int startY = YArray[i];
                        int width = WidthArray[i];
                        int height = HeightArray[i];

                        long sumR = 0, sumG = 0, sumB = 0;
                        int pixelCount = width * height;

                        for (int y = startY; y < startY + height; y++)
                        {
                            for (int x = startX; x < startX + width; x++)
                            {
                                int offset = y * imgWidth + x;
                                sumR += ((short*)outputPtr[0])[offset];
                                sumG += ((short*)outputPtr[1])[offset];
                                sumB += ((short*)outputPtr[2])[offset];
                            }
                        }

                        avgR[i] = (double)sumR / pixelCount;
                        avgG[i] = (double)sumG / pixelCount;
                        avgB[i] = (double)sumB / pixelCount;
                    }
                }

                // 8. 调用 IQ 计算
                double[] diff_l = new double[6] { 10, 10, 10, 10, 10, 10 };
                int ref_count = 0;
                double[] l_val_array = new double[6];
                double[] delta_l_array = new double[6];
                double yMax = 0.0;
                double[] yAvg = new double[18];
                double out_gamma = 0.0;

                IspApi.YGAMMA_IQ(avgR, avgG, avgB, 6,
                    diff_l, ref ref_count, l_val_array, delta_l_array,
                    ref yMax, yAvg, ref out_gamma);

                // 9. 更新结果显示
                var results = new ObservableCollection<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("ref_count", ref_count.ToString()),
            new KeyValuePair<string, string>("l_val_array",
                string.Join(",", l_val_array.Select(x => x.ToString("0.00")))),
            new KeyValuePair<string, string>("delta_l_array",
                string.Join(",", delta_l_array.Select(x => x.ToString("0.00")))),
            new KeyValuePair<string, string>("yMax", yMax.ToString("0.00")),
            new KeyValuePair<string, string>("out_gamma", out_gamma.ToString("0.00"))
        };

                DataContext = results;

                // 10. 显示处理后的图像
                ShowProcessedImage(outputPtr, imgWidth, imgHeight);
            }
            finally
            {
                // 释放内存
                for (int i = 0; i < 3; i++)
                {
                    if (inputPtr[i] != IntPtr.Zero)
                        MemoryManager.FreeMemory(inputPtr[i]);
                    if (outputPtr[i] != IntPtr.Zero)
                        MemoryManager.FreeMemory(outputPtr[i]);
                }
            }
        }


        private void OnLoadRgbButtonClick(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "rgb文件(*.rgb) | *.rgb";
            if (!(bool)openFileDialog.ShowDialog())
            {
                return;
            }

            _rgbBuffer = File.ReadAllBytes(openFileDialog.FileName);
            OriginImg.DisplayImageSource = _ispProcessor.GenerateBitmapUsingRgb(_rgbBuffer);

            IsLoadImage = true;
            //TODO:这里欠债了，有空改掉吧(改成binding形式)
            OriginImg.IsEnabled = true;
        }

        private void OnUndoClick(object sender, RoutedEventArgs e)
        {
            OriginImg.UndoDrawRubberBand();
        }

        private void OnCalcIQClick(object sender, RoutedEventArgs e)
        {
            int[] XArray = new int[6];
            int[] YArray = new int[6];
            int[] HeightArray = new int[6];
            int[] WidthArray = new int[6];

            if (_rubberBandData.Count > 0)
            {
                for (int j = 0; j < _rubberBandData.Count; j++)
                {
                    XArray[j] = _rubberBandData[j].x;
                    YArray[j] = _rubberBandData[j].y;
                    HeightArray[j] = _rubberBandData[j].height;
                    WidthArray[j] = _rubberBandData[j].width;
                }
            }

            //_gammaStep.ProcessRgbBuffer(ref ptrArray);

            //IspApi.EncoderImgBuffer(ptrArray, _ispProcessor.IspCommonConfig.ResolutionWidth,
            //    _ispProcessor.IspCommonConfig.ResolutionHeight, null, ref size);

            //byte[] buffer = new byte[size];
            //IspApi.EncoderImgBuffer(ptrArray, _ispProcessor.IspCommonConfig.ResolutionWidth,
            //    _ispProcessor.IspCommonConfig.ResolutionHeight, buffer, ref size);
        }
    }
        */
}
