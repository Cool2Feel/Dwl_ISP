using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ThunderSE.Common;
using ThunderSE.Ui.CommonCustomControl;
using ThunderSE.Uvc;

namespace ThunderSE.Ui.MainWindow
{
    /// <summary>
    /// ExpGainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ExpGainWindow : Window
    {
        private static ExpGainWindow ExpGainWindowObj;

        private int _videoWidth = 0;
        private int _videoHeight = 0;
        private WriteableBitmap _bitmap;

        public event PlayStateChangeCallbackFunc PlayStateChange;

        private ObservableCollection<RubberBandData> _rubberBandData = new ObservableCollection<RubberBandData>();

        public int AvgR
        {
            get { return (int)GetValue(AvgRProperty); }
            set { SetValue(AvgRProperty, value); }
        }
        public int AvgG
        {
            get { return (int)GetValue(AvgGProperty); }
            set { SetValue(AvgGProperty, value); }
        }


        public int AvgB
        {
            get { return (int)GetValue(AvgBProperty); }
            set { SetValue(AvgBProperty, value); }
        }

        public int AvgY
        {
            get { return (int)GetValue(AvgYProperty); }
            set { SetValue(AvgYProperty, value); }
        }

        public bool IsConnected
        {
            get { return (bool)GetValue(IsConnectedProperty); }
            set { SetValue(IsConnectedProperty, value); }
        }

        public static readonly DependencyProperty AvgRProperty = DependencyProperty.Register("AvgR",
            typeof(int),
            typeof(ExpGainWindow),
            new PropertyMetadata(0));

        public static readonly DependencyProperty AvgGProperty = DependencyProperty.Register("AvgG",
            typeof(int),
            typeof(ExpGainWindow),
            new PropertyMetadata(0));

        public static readonly DependencyProperty AvgBProperty = DependencyProperty.Register("AvgB",
            typeof(int),
            typeof(ExpGainWindow),
            new PropertyMetadata(0));

        public static readonly DependencyProperty AvgYProperty = DependencyProperty.Register("AvgY",
            typeof(int),
            typeof(ExpGainWindow),
            new PropertyMetadata(0));

        public static readonly DependencyProperty IsConnectedProperty = DependencyProperty.Register("IsConnected",
            typeof(bool),
            typeof(ExpGainWindow),
            new PropertyMetadata(false));


        public ExpGainWindow()
        {
            InitializeComponent();

            ExpGainWindowObj = this;
            this.KeyDown += Window_KeyDown;
            this.SizeChanged += Window_SizeChanged;

            UvcReceiver.Instance.DataReceive += OnUvcDataReceive;
            //UvcReceiver.Instance.YuvDataReceive += OnUvcYuvDataReceive;
            UvcReceiver.Instance.StatusChange += OnPlayStateChange;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            DisplayControl.DataContext = _rubberBandData;
            DisplayControl.MaxBands = 1;

            InitializeUIState();

            _videoWidth = UvcReceiver.Instance.VideoWidth;
            _videoHeight = UvcReceiver.Instance.VideoHeight;

            _bitmap = new WriteableBitmap(_videoWidth,
                _videoHeight, 96, 96, System.Windows.Media.PixelFormats.Rgb24, null);
            this.DisplayControl.DisplayImageSource = _bitmap;
            IsConnected = true;

            UpdateVideoStatus("● 已连接", true);
            UpdateModuleStatus("摄像头已连接");
            UpdateStatusBar("UVC摄像头已连接 - 可以开始框选区域计算RGB均值");
        }

        private void InitializeUIState()
        {
            UpdateStatusBar("就绪 - 曝光增益(ExpGain)计算工具已就绪");
            UpdateProcessingStatus("等待操作...");
            TxtWindowSize.Text = $"窗口尺寸: {this.ActualWidth:F0} x {this.ActualHeight:F0}";
            UpdateOperationStatus("就绪");
            UpdateVideoStatus("● 未连接", false);
            UpdateModuleStatus("等待连接...");
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
                    case Key.Z:
                        UndoButton_Click(null, null);
                        e.Handled = true;
                        break;
                    case Key.Enter:
                        ApplyButton_Click(null, null);
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

        private void UpdateVideoStatus(string status, bool isConnected)
        {
            if (TxtVideoStatus != null)
            {
                TxtVideoStatus.Text = status;
                TxtVideoStatus.Foreground = isConnected
                    ? new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x00))  // 绿色
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35)); // 橙色
            }
        }

        private void UpdateModuleStatus(string status)
        {
            if (TxtModuleStatus != null)
            {
                TxtModuleStatus.Text = status;
            }
        }

        private void UpdateSelectionInfo(string info)
        {
            if (TxtSelectionInfo != null)
            {
                TxtSelectionInfo.Text = info;
            }
        }

        private void OnUvcDataReceive(byte[] dataBuffer)
        {
            //ExpGainWindowObj._bitmap.Lock();
            //ExpGainWindowObj._bitmap.WritePixels(new Int32Rect(0, 0, (int)_bitmap.Width, (int)_bitmap.Height),
            //    dataBuffer, (int)_bitmap.Width * 3, 0);
            //ExpGainWindowObj._bitmap.AddDirtyRect(new Int32Rect(0, 0, _videoWidth, _videoHeight));
            //ExpGainWindowObj._bitmap.Unlock();

            if (!IsLoaded)
            {
                return;
            }

            try
            {
                bool isRawBayer = UvcReceiver.Instance.IsRawBayer;

                // 根据数据大小判断实际格式
                // Gray8: width * height * 1
                // Rgb24: width * height * 3
                int expectedGray8Size = _videoWidth * _videoHeight;
                int expectedRgb24Size = _videoWidth * _videoHeight * 3;

                bool isGray8 = false;//isRawBayer || dataBuffer.Length == expectedGray8Size;

                // 【关键修复】只在首次或格式变化时创建新Bitmap，避免每帧创建导致内存泄漏
                PixelFormat targetFormat = isGray8 ? PixelFormats.Gray8 : PixelFormats.Rgb24;
                bool needNewBitmap = _bitmap == null ||
                                     _bitmap.Format != targetFormat ||
                                     _bitmap.PixelWidth != _videoWidth ||
                                     _bitmap.PixelHeight != _videoHeight;

                if (needNewBitmap)
                {
                    // 释放旧Bitmap引用（让GC可以回收）
                    _bitmap = null;
                    // 根据数据类型创建对应格式的 WriteableBitmap
                    if (isGray8)
                    {
                        // 灰度图数据（RAW Bayer 或 YUYV422 转换后），使用 Gray8 格式
                        _bitmap = new WriteableBitmap(
                            _videoWidth,
                            _videoHeight,
                            96, 96,
                            PixelFormats.Gray8,
                            null
                        );
                    }
                    else
                    {
                        // 标准 RGB 数据（MJPEG 解码后等），使用 Rgb24 格式
                        _bitmap = new WriteableBitmap(
                            _videoWidth,
                            _videoHeight,
                            96, 96,
                            PixelFormats.Rgb24,
                            null
                        );
                    }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        this.DisplayControl.DisplayImageSource = _bitmap;
                    }), DispatcherPriority.Render);
                }
                // 使用Dispatcher.BeginInvoke确保在UI线程中更新，防止跨线程操作异常
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 根据像素格式计算正确的 stride
                    // Gray8: 每像素 1 字节，stride = width * 1
                    // Rgb24: 每像素 3 字节，stride = width * 3
                    int bytesPerPixel = isGray8 ? 1 : 3;
                    int stride = _videoWidth * bytesPerPixel;

                    // 验证缓冲区大小是否足够
                    int requiredBufferSize = stride * _videoHeight;
#if DEBUG
                if (dataBuffer.Length < requiredBufferSize)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[WARNING] Buffer size mismatch: expected {requiredBufferSize}, got {dataBuffer.Length}");
                    return;
                }
#endif
                    try
                    {
                        _bitmap.Lock();
                        _bitmap.WritePixels(
                            new Int32Rect(0, 0, (int)_bitmap.Width, (int)_bitmap.Height),
                            dataBuffer,
                            stride,  // 使用动态计算的 stride
                            0
                        );
                        _bitmap.AddDirtyRect(new Int32Rect(0, 0, _videoWidth, _videoHeight));

                    }
                    finally
                    {
                        _bitmap.Unlock();
                    }

                    UpdateVideoStatus("● 已连接", true);

                    if (ExpGainWindowObj._rubberBandData.Count > 0)
                    {
                        var croppedBitmap = new CroppedBitmap(ExpGainWindowObj._bitmap,
                            new Int32Rect(ExpGainWindowObj._rubberBandData[0].x, ExpGainWindowObj._rubberBandData[0].y,
                                ExpGainWindowObj._rubberBandData[0].width, ExpGainWindowObj._rubberBandData[0].height));

                        var pixels = new byte[ExpGainWindowObj._rubberBandData[0].width * ExpGainWindowObj._rubberBandData[0].height * croppedBitmap.Format.BitsPerPixel / 8];
                        //croppedBitmap.CopyPixels(pixels, ExpGainWindowObj._rubberBandData[0].width * 3, 0);
                        // 根据裁剪后的位图格式计算正确的stride
                        int croppedStride = ExpGainWindowObj._rubberBandData[0].width * (croppedBitmap.Format.BitsPerPixel / 8);
                        croppedBitmap.CopyPixels(pixels, croppedStride, 0);

                        int rSum = 0;
                        int gSum = 0;
                        int bSum = 0;

                        for (int i = 0; i < pixels.Length / 3; i++)
                        {
                            rSum += pixels[i * 3 + 2];
                            gSum += pixels[i * 3 + 1];
                            bSum += pixels[i * 3 + 0];
                        }

                        AvgR = rSum / (pixels.Length / 3);
                        AvgG = gSum / (pixels.Length / 3);
                        AvgB = bSum / (pixels.Length / 3);
                        //AvgY = (int)(0.299 * AvgR + 0.587 * AvgG + 0.114 * AvgB);
                        AvgY = (AvgR * 77 + AvgG * 150 + AvgB * 29) >> 8;


                        UpdateSelectionInfo($"选区尺寸: {ExpGainWindowObj._rubberBandData[0].width}×{ExpGainWindowObj._rubberBandData[0].height}\n像素数: {(pixels.Length / 3):N0}");
                        UpdateProcessingStatus($"RGB均值已更新 ✓\nR:{AvgR} G:{AvgG} B:{AvgB} Y:{AvgY}");
                        UpdateOperationStatus("均值计算完成");
                        UpdateStatusBar($"RGB均值已计算 - R:{AvgR} G:{AvgG} B:{AvgB} Y:{AvgY}");
                    }
                    else
                    {
                        UpdateSelectionInfo("未选择区域");
                        UpdateOperationStatus("预览中");
                    }

                }), DispatcherPriority.Normal);
            }
            catch
            {
                // 捕获并忽略所有异常，防止因单帧数据问题导致应用崩溃
            }
        }

        private int OnUvcYuvDataReceive(IntPtr yuvData)
        {
            //this.Dispatcher.Invoke((Action)(() =>
            //    {
            //        int ptr = yuvData.ToInt32();
            //        if (_rubberBandData.Count > 0)
            //        {
            //            int ySum = 0;
            //            for (int y = _rubberBandData[0].y;
            //                y < _rubberBandData[0].y + _rubberBandData[0].height;
            //                y++)
            //            {
            //                for (int x = _rubberBandData[0].x; x < _rubberBandData[0].x + _rubberBandData[0].width; x++)
            //                {
            //                    ySum += Marshal.ReadByte((IntPtr)(ptr + y * _videoWidth + x));
            //                }
            //            }

            //            AvgR = ySum / _rubberBandData[0].width / _rubberBandData[0].height;
            //        }
            //    }));
            return 0;
        }

        private int OnPlayStateChange(bool isPlaying)
        {
            if (PlayStateChange != null)
            {
                PlayStateChange(isPlaying);
            }

            if (isPlaying)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateVideoStatus("● 已连接", true);
                    UpdateModuleStatus("视频已连接");
                    UpdateStatusBar("视频流已连接 - 可以框选区域进行均值计算");
                    UpdateOperationStatus("已连接");
                }));
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateVideoStatus("● 已暂停", false);
                    UpdateModuleStatus("视频已暂停");
                    UpdateStatusBar("视频流已暂停");
                    UpdateOperationStatus("已暂停");
                }));
            }

            //}), new TimeSpan(10000), null);
            return 0;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UvcReceiver.Instance.DataReceive -= OnUvcDataReceive;
            UvcReceiver.Instance.StatusChange -= OnPlayStateChange;
            UvcReceiver.Instance.YuvDataReceive -= OnUvcYuvDataReceive;

            _videoWidth = 0;
            _videoHeight = 0;

            if (_bitmap != null)
            {
                _bitmap = null;
            }

            // 5. 清除UI引用
            if (DisplayControl != null)
            {
                DisplayControl.DisplayImageSource = null;
            }

            ExpGainWindowObj = null;

            UpdateVideoStatus("● 已断开", false);
            UpdateModuleStatus("设备已断开");
            UpdateStatusBar("ExpGain窗口已关闭");
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            DisplayControl.UndoDrawRubberBand();

            UpdateStatusBar("已撤销选区 - 请重新选择计算范围");
            UpdateOperationStatus("已撤销选区");
            UpdateSelectionInfo("未选择区域（已撤销）");
            UpdateProcessingStatus("等待重新选择...");
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // 验证输入
            if (!TryParseInputValues(out int gainValue, out int expValue, out string errorMessage))
            {
                ShowErrorAndReset(errorMessage);
                return;
            }

            // 应用设置
            try
            {
                ApplySettings(gainValue, expValue);

                string summary = $"Gain: {gainValue}, Exp: {expValue}";
                UpdateStatusBar($"设置已应用 - {summary}");
                UpdateProcessingStatus($"设置已应用 ✓\n{summary}");
                UpdateOperationStatus("设置已应用");
            }
            catch (Exception ex)
            {
                ShowErrorAndReset($"应用设置失败: {ex.Message}");
            }
        }

        private bool TryParseInputValues(out int gainValue, out int expValue, out string errorMessage)
        {
            gainValue = 0;
            expValue = 0;
            errorMessage = null;

            string gainText = TxtTurnGain?.Text?.Trim();
            string expText = TxtTurnExp?.Text?.Trim();

            if (string.IsNullOrWhiteSpace(gainText))
            {
                errorMessage = "Turn_Gain值不能为空";
                return false;
            }

            if (string.IsNullOrWhiteSpace(expText))
            {
                errorMessage = "Turn_Exp值不能为空";
                return false;
            }

            if (!int.TryParse(gainText, out gainValue))
            {
                errorMessage = $"Turn_Gain值格式无效: {gainText}";
                return false;
            }

            if (!int.TryParse(expText, out expValue))
            {
                errorMessage = $"Turn_Exp值格式无效: {expText}";
                return false;
            }

            return true;
        }

        private void ApplySettings(int gainValue, int expValue)
        {
            // TODO: 实现实际的设置应用逻辑
            // 例如：调用底层设备接口或保存配置
            // UvcReceiver.Instance.SetExposureGain(gainValue, expValue);
        }

        private void ShowErrorAndReset(string errorMessage)
        {
            MessageBox.Show(errorMessage, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            UpdateStatusBar($"应用失败 - {errorMessage}");
            UpdateProcessingStatus("输入无效 ✗");
            UpdateOperationStatus("等待输入");
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // 转移到下一个控件（模拟 Tab）
                var request = new TraversalRequest(FocusNavigationDirection.Next);
                (sender as UIElement)?.MoveFocus(request);
                e.Handled = true;
            }
        }
    }
}
