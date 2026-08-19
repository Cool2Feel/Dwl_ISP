using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ThunderSE.Common;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Model;

namespace ThunderSE.Ui.SettingWindow.Lsc
{
    /*
    public class RawBufferToBitmapImageConverter : IValueConverter, IDisposable
    {
        public CommonConfig ProcessorCommonConfig
        {
            get;
            set;
        }

        private MemoryManager _memoryManager = new MemoryManager(); 
        private static ImageProcessingCache _imageCache = new ImageProcessingCache();


        // TODO:这里应该统一过程，使用IspProcessor来处理图片
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var rawImgBuffer = (byte[])value;
            if (rawImgBuffer == null)
            {
                return null;
            }

            // 生成缓存键
            string cacheKey = _imageCache.GetCacheKey(rawImgBuffer,
                ProcessorCommonConfig.ResolutionWidth,
                ProcessorCommonConfig.ResolutionHeight,
                (int)ProcessorCommonConfig.Bayer);

            // 尝试从缓存获取
            if (_imageCache.TryGetCachedImage(cacheKey, out byte[] cachedBuffer))
            {
                // 使用缓存的图像数据
                var image = new BitmapImage();
                using (MemoryStream memStream = new MemoryStream(cachedBuffer))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = memStream;
                    image.EndInit();
                    image.Freeze();
                }
                return image;
            }

            IntPtr[] ptrArray = new IntPtr[3];
            try
            {
                for (int i = 0; i < ptrArray.Length; i++)
                {
                    ptrArray[i] = _memoryManager.AllocateMemory(ProcessorCommonConfig.ResolutionWidth
                        * ProcessorCommonConfig.ResolutionHeight * sizeof(short));
                    Marshal.Copy(new byte[ProcessorCommonConfig.ResolutionWidth * ProcessorCommonConfig.ResolutionHeight * sizeof(short)],
                        0, ptrArray[i], ProcessorCommonConfig.ResolutionWidth * ProcessorCommonConfig.ResolutionHeight * sizeof(short));
                }

                // 使用Stopwatch测量处理时间
                var stopwatch = Stopwatch.StartNew();

                IspApi.DemosaicImg(rawImgBuffer, (int)ProcessorCommonConfig.Bayer, ProcessorCommonConfig.ResolutionWidth,
                    ProcessorCommonConfig.ResolutionHeight, ptrArray);
                int size = 0;
                IspApi.EncoderImgBuffer(ptrArray, ProcessorCommonConfig.ResolutionWidth,
                    ProcessorCommonConfig.ResolutionHeight, 2, null, ref size);
                byte[] buffer = new byte[size];
                IspApi.EncoderImgBuffer(ptrArray, ProcessorCommonConfig.ResolutionWidth,
                    ProcessorCommonConfig.ResolutionHeight, 2, buffer, ref size);

                stopwatch.Stop();
                Debug.WriteLine($"图像处理耗时: {stopwatch.ElapsedMilliseconds}ms");

                // 添加到缓存
                _imageCache.AddToCache(cacheKey, buffer);

                //for (int i = 0; i < ptrArray.Length; i++)
                //{
                //    Marshal.FreeHGlobal(ptrArray[i]);
                //}
                var image = new BitmapImage();
                using (MemoryStream memStream = new MemoryStream(buffer))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = memStream;
                    image.EndInit();
                    image.Freeze(); // <--- 关键：冻结以允许跨线程使用
                }
                return image;
            }
            catch (Exception ex) {
                Console.WriteLine(ex.ToString());
                return null;
            }
            
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
        public static void ClearCache()
        {
            _imageCache.ClearCache();
        }

        public void Dispose()
        {
            _memoryManager.Dispose();
        }
    }

    */
    /// <summary>
    /// LscWindow.xaml 的交互逻辑
    /// </summary>
    public partial class LscWindow : Window
    {
        private LscWindowViewModel _vm = null;
        private Point _dotPos;
        private ushort[] _raw16Buffer; // 10-bit原始数据（16-bit容器），保留完整精度用于LSC分析

        private double _maxX = 1.0d;
        private double _minX = 1.0d;
        private double _maxY = 1.0d;
        private double _minY = 1.0d;

        private double _horizontalScale = 1.0d;
        private double _verticalScale = 1.0d;

        // RawImg 控件内部的缩放比（Border 内 Canvas 坐标到 Source 坐标的转换）
        // 即 1 个 Border 内 Canvas 像素 = _rawImgInnerScaleX 个 Source 像素
        private double _rawImgInnerScaleX = 1.0d;
        private double _rawImgInnerScaleY = 1.0d;

        private TextBlock _rawImgColorDisplayBlock;
        private TextBlock _processedImgColorDisplayBlock;

        private const int LSC_SAFE_MARGIN = 10;

        // 鼠标移动节流控制
        private DateTime _lastMouseMoveTime = DateTime.MinValue;
        private const int MOUSE_MOVE_THROTTLE_MS = 33; // ~30fps

        // 自动描点状态
        private bool _isAutoDotPending = false;
        private Point? _pendingAutoDotPos = null;
        private double _pendingAutoDotBrightness = 0;

        public LscWindow()
        {
            InitializeComponent();

            SetupWindowEvents();
            SetupKeyboardShortcuts();
        }

        #region 窗口初始化与事件设置

        private void SetupWindowEvents()
        {
            this.SizeChanged += OnWindowSizeChanged;
            this.Closing += OnWindowClosing;
        }

        private void SetupKeyboardShortcuts()
        {
            // Ctrl+O: 加载RAW文件
            InputBindings.Add(new KeyBinding(new RelayCommand(() =>
                _vm?.LoadRawFileCommand.Execute(null)),
                Key.O, ModifierKeys.Control));

            // Ctrl+Enter: 计算LSC
            InputBindings.Add(new KeyBinding(new RelayCommand(() =>
            {
                if (_vm != null && _vm.HasLoadedRawFile)
                    ClickCalc(null, null);
            }),
                Key.Enter, ModifierKeys.Control));

            // Ctrl+Q: 查看IQ
            InputBindings.Add(new KeyBinding(new RelayCommand(() =>
                _vm?.ViewIQCommand?.Execute(null)),
                Key.Q, ModifierKeys.Control));

            // Ctrl+D: 自动检测最亮区域并描点
            InputBindings.Add(new KeyBinding(new RelayCommand(() =>
                AutoDetectBrightestPoint()),
                Key.D, ModifierKeys.Control));
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateStatus($"窗口尺寸已调整: {this.ActualWidth:F1}×{this.ActualHeight:F1}");
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_vm != null && _vm.HasLoadedRawFile)
            {
                var result = MessageBox.Show(this,
                    "确定要关闭LSC镜头阴影校正窗口吗？\n\n当前加载的图像和计算数据将不会自动保存。",
                    "确认关闭",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    UpdateStatus("已取消关闭操作");
                    return;
                }
            }

            UpdateStatus("正在关闭LSC镜头阴影校正窗口...");
        }

        #endregion

        #region UI状态更新方法

        private void UpdateStatus(string message)
        {
            if (StatusBarText != null)
            {
                StatusBarText.Text = message;
            }
        }

        private void UpdateProgressInfo(string info)
        {
            if (TxtProgressInfo != null)
            {
                TxtProgressInfo.Text = info;
            }
        }

        private void UpdateImageStatus(string status)
        {
            if (TxtImageStatus != null)
            {
                TxtImageStatus.Text = status;
            }
        }

        private void UpdateDotStatus(string status)
        {
            if (TxtDotStatus != null)
            {
                TxtDotStatus.Text = status;
                Debug.WriteLine($"描点状态更新: {status}");
                // 根据描点状态改变颜色
                if (status.Contains("未描点"))
                {
                    TxtDotStatus.Foreground = System.Windows.Media.Brushes.Gray;
                }
                else if (status.Contains("已描点") || status.Contains("位置:"))
                {
                    TxtDotStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD7)); // 蓝色
                }
                else
                {
                    TxtDotStatus.Foreground = System.Windows.Media.Brushes.Green;
                }
            }
        }

        private void UpdateProcessingStatus(string status)
        {
            if (TxtProcessingStatus != null)
            {
                TxtProcessingStatus.Text = status;

                // 根据处理状态改变颜色
                if (status.Contains("处理中") || status.Contains("计算中"))
                {
                    TxtProcessingStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xFF, 0x99, 0x00)); // 橙色
                }
                else if (status.Contains("完成") || status.Contains("成功"))
                {
                    TxtProcessingStatus.Foreground = System.Windows.Media.Brushes.Green;
                }
                else if (status.Contains("失败") || status.Contains("错误"))
                {
                    TxtProcessingStatus.Foreground = System.Windows.Media.Brushes.Red;
                }
                else
                {
                    TxtProcessingStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD7)); // 蓝色
                }
            }
        }

        #endregion

        #region 窗口事件处理

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            var bufferToBitmapImageConverter = (RawBufferToBitmapImageConverter)FindResource("rawBufferToBitmapImageConverter");
            _vm = (LscWindowViewModel)DataContext;

            bufferToBitmapImageConverter.ProcessorCommonConfig = _vm.IspCommonConfig;

            // 绑定自动描点委托：ViewModel通过此委托回调View层执行描点
            _vm.AutoDetectBrightestAction = () => AutoDetectBrightestPoint();

            InitializeUI();
        }

        private void InitializeUI()
        {
            UpdateStatus("✓ LSC镜头阴影校正工具初始化完成");
            UpdateDotStatus("未描点");
            UpdateProcessingStatus("");
            UpdateImageStatus("");
            UpdateProgressInfo("请加载RAW图像文件开始LSC校正分析");
        }

        #endregion

        #region 图像显示与切换

        private void OnProcessedImageUpdated(object sender, DataTransferEventArgs e)
        {
            Image processedImage = (Image)sender;
            if (processedImage.Source != null)
            {
                ImgDisplayTab.SelectedIndex = 1; // 自动切换到LSC效果Tab

                UpdateStatus("✓ LSC处理完成，已切换到效果预览");
                UpdateImageStatus("(LSC效果已生成)");
                UpdateProcessingStatus("✓ 处理完成");
            }
        }

        #endregion

        #region 鼠标交互（描点与取色）

        /// <summary>
        /// 自动检测RAW图像最亮区域并描点
        /// </summary>
        private void AutoDetectBrightestPoint()
        {
            if (_vm == null || !_vm.HasLoadedRawFile)
            {
                MessageBox.Show(this,
                    "尚未加载RAW图像。\n\n请先点击'📂 加载RAW文件'按钮选择RAW图像。",
                    "无图像",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            UpdateStatus("🔍 正在检测最亮区域...");
            UpdateProcessingStatus("检测中...");

            // 在后台线程执行亮度分析
            Task.Run(() =>
            {
                try
                {
                    int width = _vm.IspCommonConfig.ResolutionWidth;
                    int height = _vm.IspCommonConfig.ResolutionHeight;
                    byte[] rawBuffer = _vm.OriginRawFileBuffer;
                    bool isRaw8 = _vm.IspCommonConfig.SetMode == SetMode.RAW8;

                    // 新增：解码RAW到16位缓冲，保留完整精度
                    DecodeRaw10To16Bit(rawBuffer, width, height, isRaw8);

                    int brightX, brightY;
                    double brightness;
                    FindBrightestRegion(rawBuffer, width, height, out brightX, out brightY, out brightness);

                    // 回到UI线程更新描点
                    Dispatcher.Invoke(() =>
                    {
                        OnBrightestPointFound(brightX, brightY, brightness);
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(this,
                            $"自动检测最亮区域失败:\n{ex.Message}",
                            "检测错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        UpdateStatus("❌ 自动检测失败");
                        UpdateProcessingStatus("❌ 检测失败");
                    });
                }
            });
        }

        /// <summary>
        /// 计算单个像素位置的 Y 亮度（BT.601），与 C++ LscCal 算法一致
        /// 通过读取该像素所在的 2x2 Bayer 块，根据 Bayer 极性计算 Y = (R*77 + G_avg*150 + B*29) / 256
        /// 支持 RAW8（1 byte/pixel）和 RAW10（2 bytes/pixel）两种位深
        /// </summary>
        private int GetPixelYBrightness(byte[] rawBuffer, int width, int height, int x, int y, int bayerPolarity)
        {
            // 确保 2x2 Bayer 块在图像范围内（需要 x+1, y+1 有效）
            if (x < 0 || y < 0 || x + 1 >= width || y + 1 >= height)
                return 0;

            bool isRaw8 = _vm.IspCommonConfig.SetMode == SetMode.RAW8;
            int bytesPerPixel = isRaw8 ? 1 : 2;

            int idx00 = (y * width + x) * bytesPerPixel;
            int idx01 = (y * width + (x + 1)) * bytesPerPixel;
            int idx10 = ((y + 1) * width + x) * bytesPerPixel;
            int idx11 = ((y + 1) * width + (x + 1)) * bytesPerPixel;

            if (idx11 + bytesPerPixel - 1 >= rawBuffer.Length)
                return 0;

            // 读取 2x2 Bayer 块的 4 个像素值，统一转为 8-bit
            int p00, p01, p10, p11;
            if (isRaw8)
            {
                // RAW8：每像素 1 字节，直接就是 8-bit
                p00 = rawBuffer[idx00];
                p01 = rawBuffer[idx01];
                p10 = rawBuffer[idx10];
                p11 = rawBuffer[idx11];
            }
            else
            {
                // RAW10：每像素 2 字节，10-bit 右移 2 位转 8-bit
                p00 = (rawBuffer[idx00] | (rawBuffer[idx00 + 1] << 8)) >> 2;
                p01 = (rawBuffer[idx01] | (rawBuffer[idx01 + 1] << 8)) >> 2;
                p10 = (rawBuffer[idx10] | (rawBuffer[idx10 + 1] << 8)) >> 2;
                p11 = (rawBuffer[idx11] | (rawBuffer[idx11 + 1] << 8)) >> 2;
            }

            int gAvg = (p01 + p10) / 2;

            // 与 C++ IQ.cpp LscCal 完全一致的 BT.601 Y 亮度计算
            switch (bayerPolarity)
            {
                case 0: // RGGB: p00=R, p01=G, p10=G, p11=B
                    return (p00 * 77 + gAvg * 150 + p11 * 29) / 256;
                case 1: // GRGR: p00=G, p01=R, p10=B, p11=G
                    return (p01 * 77 + gAvg * 150 + p10 * 29) / 256;
                case 2: // BGBG: p00=B, p01=G, p10=G, p11=R
                    return (p11 * 77 + gAvg * 150 + p00 * 29) / 256;
                case 3: // GBGB: p00=G, p01=B, p10=R, p11=G
                    return (p10 * 77 + gAvg * 150 + p01 * 29) / 256;
                default:
                    return (p00 * 77 + gAvg * 150 + p11 * 29) / 256;
            }
        }

        /*
        /// <summary>
        /// 查找最亮区域（仅在图像中心 30% 区域内搜索）
        /// 使用积分图(Integral Image)加速：先一次性解码 RAW10 并按 Bayer 极性计算 Y 亮度图，
        /// 再构建积分图实现 O(1) 块求和，最后两阶段搜索定位最亮位置。
        /// 当亮度相同时，优先选择距离图像中心最近的位置。
        /// </summary>
        private void FindBrightestRegion(byte[] rawBuffer, int width, int height,
            out int brightestX, out int brightestY, out double brightness)
        {
            int bayerPolarity = (int)_vm.IspCommonConfig.Bayer;
            int margin = LSC_SAFE_MARGIN;

            // ===== Step 1: 一次性解码 RAW10 → Y 亮度图 =====
            int halfW = width / 2;
            int halfH = height / 2;
            byte[] yBlockMap = new byte[halfW * halfH];

            for (int by = 0; by < height - 1; by += 2)
            {
                int rowOff0 = by * width * 2;
                int rowOff1 = (by + 1) * width * 2;
                int yRow = (by / 2) * halfW;

                for (int bx = 0; bx < width - 1; bx += 2)
                {
                    int idx00 = rowOff0 + bx * 2;
                    int idx01 = idx00 + 2;
                    int idx10 = rowOff1 + bx * 2;
                    int idx11 = idx10 + 2;

                    int p00 = (rawBuffer[idx00] | (rawBuffer[idx00 + 1] << 8)) >> 2;
                    int p01 = (rawBuffer[idx01] | (rawBuffer[idx01 + 1] << 8)) >> 2;
                    int p10 = (rawBuffer[idx10] | (rawBuffer[idx10 + 1] << 8)) >> 2;
                    int p11 = (rawBuffer[idx11] | (rawBuffer[idx11 + 1] << 8)) >> 2;

                    int gAvg = (p01 + p10) >> 1;
                    int yVal;

                    switch (bayerPolarity)
                    {
                        case 0: yVal = (p00 * 77 + gAvg * 150 + p11 * 29) >> 8; break;
                        case 1: yVal = (p01 * 77 + gAvg * 150 + p10 * 29) >> 8; break;
                        case 2: yVal = (p11 * 77 + gAvg * 150 + p00 * 29) >> 8; break;
                        case 3: yVal = (p10 * 77 + gAvg * 150 + p01 * 29) >> 8; break;
                        default: yVal = (p00 * 77 + gAvg * 150 + p11 * 29) >> 8; break;
                    }

                    yBlockMap[yRow + bx / 2] = (byte)yVal;
                }
            }

            // ===== Step 2: 构建积分图 =====
            int iStride = halfW + 1;
            long[] integral = new long[(halfH + 1) * iStride];

            for (int y = 0; y < halfH; y++)
            {
                long rowSum = 0;
                int iRow = (y + 1) * iStride;
                int iPrevRow = y * iStride;
                int yRow = y * halfW;
                for (int x = 0; x < halfW; x++)
                {
                    rowSum += yBlockMap[yRow + x];
                    integral[iRow + x + 1] = integral[iPrevRow + x + 1] + rowSum;
                }
            }

            // ===== Step 3: 计算中心 30% 搜索区域 =====
            // 图像中心（block 坐标空间，每个block=2x2像素）
            // 使用 (halfW - 1) / 2 确保中心点计算准确
            int centerBlockX = (halfW - 1) / 2;
            int centerBlockY = (halfH - 1) / 2;

            // 中心 30% 区域的半宽/半高（block 坐标）
            int searchHalfWBlock = (int)(halfW * 0.15);  // 30%/2 = 15%
            int searchHalfHBlock = (int)(halfH * 0.15);

            int marginBlock = margin / 2;
            int cStartX = Math.Max(marginBlock, centerBlockX - searchHalfWBlock);
            int cStartY = Math.Max(marginBlock, centerBlockY - searchHalfHBlock);
            int cEndX = Math.Min(halfW - marginBlock, centerBlockX + searchHalfWBlock);
            int cEndY = Math.Min(halfH - marginBlock, centerBlockY + searchHalfHBlock);

            // ===== Step 4: 粗搜 - 在中心 30% 区域内找到最大亮度 =====
            int coarseBlock = 10;
            int coarseStep = 3;

            // 计算对称的搜索范围
            // 搜索块中心范围: [cStartX + coarseBlock/2, cEndX - coarseBlock/2]
            // 需要确保搜索范围以 centerBlockX/centerBlockY 为基准对称
            int halfBlock = coarseBlock / 2;
            int coarseStartX = cStartX;
            int coarseStartY = cStartY;
            
            // 计算从起点到中心需要的步数，然后对称扩展
            int stepsToCenterX = (centerBlockX - halfBlock - coarseStartX) / coarseStep;
            int coarseEndX = coarseStartX + stepsToCenterX * 2 * coarseStep;
            if (coarseEndX > cEndX - coarseBlock) coarseEndX = cEndX - coarseBlock;
            
            int stepsToCenterY = (centerBlockY - halfBlock - coarseStartY) / coarseStep;
            int coarseEndY = coarseStartY + stepsToCenterY * 2 * coarseStep;
            if (coarseEndY > cEndY - coarseBlock) coarseEndY = cEndY - coarseBlock;

            // 找到最大亮度
            long maxBrightness = long.MinValue;
            for (int cy = coarseStartY; cy <= coarseEndY; cy += coarseStep)
            {
                int cy0 = cy * iStride;
                int cy1 = (cy + coarseBlock) * iStride;
                for (int cx = coarseStartX; cx <= coarseEndX; cx += coarseStep)
                {
                    long sum = integral[cy1 + cx + coarseBlock]
                             - integral[cy0 + cx + coarseBlock]
                             - integral[cy1 + cx]
                             + integral[cy0 + cx];
                    if (sum > maxBrightness)
                        maxBrightness = sum;
                }
            }

            // 在亮度 >= 最大亮度 * 0.95 的位置中，选择最靠近图像中心的
            long brightnessThreshold = (long)(maxBrightness * 0.95);
            int bestCX = centerBlockX, bestCY = centerBlockY;
            int bestDistSq = int.MaxValue;

            for (int cy = coarseStartY; cy <= coarseEndY; cy += coarseStep)
            {
                int cy0 = cy * iStride;
                int cy1 = (cy + coarseBlock) * iStride;
                for (int cx = coarseStartX; cx <= coarseEndX; cx += coarseStep)
                {
                    long sum = integral[cy1 + cx + coarseBlock]
                             - integral[cy0 + cx + coarseBlock]
                             - integral[cy1 + cx]
                             + integral[cy0 + cx];

                    if (sum >= brightnessThreshold)
                    {
                        int blockCenterX = cx + coarseBlock / 2;
                        int blockCenterY = cy + coarseBlock / 2;
                        int distSq = (blockCenterX - centerBlockX) * (blockCenterX - centerBlockX)
                                   + (blockCenterY - centerBlockY) * (blockCenterY - centerBlockY);

                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            bestCX = blockCenterX;
                            bestCY = blockCenterY;
                        }
                    }
                }
            }

            // ===== Step 5: 精搜 - 在粗搜结果周围精细搜索 =====
            int fineBlock = 2;
            int fineStep = 1;
            // 增大精搜范围，确保能覆盖到真正最亮的位置
            // 粗搜使用 10x10 块，可能错过边缘的最亮点，需要更大范围补偿
            int searchRange = coarseStep * 10;  // 从 6 增大到 30
            
            // 在精搜范围内找到最大亮度
            long fineMaxBrightness = long.MinValue;
            int fStartY = Math.Max(cStartY, bestCY - searchRange);
            int fEndY = Math.Min(cEndY - fineBlock, bestCY + searchRange);
            int fStartX = Math.Max(cStartX, bestCX - searchRange);
            int fEndX = Math.Min(cEndX - fineBlock, bestCX + searchRange);

            for (int cy = fStartY; cy <= fEndY; cy += fineStep)
            {
                int cy0 = cy * iStride;
                int cy1 = (cy + fineBlock) * iStride;
                for (int cx = fStartX; cx <= fEndX; cx += fineStep)
                {
                    long sum = integral[cy1 + cx + fineBlock]
                             - integral[cy0 + cx + fineBlock]
                             - integral[cy1 + cx]
                             + integral[cy0 + cx];
                    if (sum > fineMaxBrightness)
                        fineMaxBrightness = sum;
                }
            }

            // 在亮度 >= 最大亮度 * 0.95 的位置中，选择最靠近图像中心的
            long fineBrightnessThreshold = (long)(fineMaxBrightness * 0.95);
            int fineBestX = centerBlockX, fineBestY = centerBlockY;
            int fineBestDistSq = int.MaxValue;

            for (int cy = fStartY; cy <= fEndY; cy += fineStep)
            {
                int cy0 = cy * iStride;
                int cy1 = (cy + fineBlock) * iStride;
                for (int cx = fStartX; cx <= fEndX; cx += fineStep)
                {
                    long sum = integral[cy1 + cx + fineBlock]
                             - integral[cy0 + cx + fineBlock]
                             - integral[cy1 + cx]
                             + integral[cy0 + cx];

                    if (sum >= fineBrightnessThreshold)
                    {
                        int blockCenterX = cx + fineBlock / 2;
                        int blockCenterY = cy + fineBlock / 2;
                        int distSq = (blockCenterX - centerBlockX) * (blockCenterX - centerBlockX)
                                   + (blockCenterY - centerBlockY) * (blockCenterY - centerBlockY);

                        if (distSq < fineBestDistSq)
                        {
                            fineBestDistSq = distSq;
                            fineBestX = blockCenterX;
                            fineBestY = blockCenterY;
                        }
                    }
                }
            }

            // 转换回原图坐标
            brightestX = (int)(fineBestX * 2);
            brightestY = (int)(fineBestY * 2);
            Console.WriteLine($"最亮点坐标: ({brightestX}, {brightestY})");

            brightness = GetPixelYBrightness(rawBuffer, width, height,
                brightestX, brightestY, bayerPolarity);
        }

        */

        /// <summary>
        /// 量产级LSC最亮点检测算法（融合参考算法核心逻辑）
        /// 优化1：使用16位RAW原始数据，保留完整10-bit精度
        /// 优化2：2x2宏块物理求和，消除通道差异
        /// 优化3：Two-Pass连通域分析，识别最大亮区
        /// 优化4：灰度重心法亚像素细化
        /// 优化5：偏心度验证机制
        /// </summary>
        private void FindBrightestRegion(byte[] rawBuffer, int width, int height,
            out int brightestX, out int brightestY, out double brightness)
        {
            var sw = Stopwatch.StartNew();
            int margin = LSC_SAFE_MARGIN;

            // ===== Step 1: 构建16位宏块求和图 =====
            // 每个2x2宏块包含R,G,G,B四个像素，求和后抵消通道差异
            int macroblockCols = width / 2;
            int macroblockRows = height / 2;
            int[] macroblockSum = new int[macroblockRows * macroblockCols];

            for (int my = 0; my < macroblockRows; my++)
            {
                for (int mx = 0; mx < macroblockCols; mx++)
                {
                    int py = my * 2;
                    int px = mx * 2;

                    // 读取2x2宏块的4个像素（16位原始值）
                    int p00 = _raw16Buffer[py * width + px];
                    int p01 = _raw16Buffer[py * width + px + 1];
                    int p10 = _raw16Buffer[(py + 1) * width + px];
                    int p11 = _raw16Buffer[(py + 1) * width + px + 1];

                    // 物理求和：抵消通道差异，代表真实光照强度
                    macroblockSum[my * macroblockCols + mx] = p00 + p01 + p10 + p11;
                }
            }

            // ===== Step 1.5: 3x3中值滤波（消除热像素/坏点） =====
            int[] filteredSum = new int[macroblockRows * macroblockCols];
            int[] sortArr = new int[9];

            for (int y = 0; y < macroblockRows; y++)
            {
                for (int x = 0; x < macroblockCols; x++)
                {
                    int cnt = 0;
                    // 每次迭代清零sortArr，防止边界宏块残留旧数据
                    Array.Clear(sortArr, 0, 9);
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= macroblockRows) continue;
                        int rowBase = ny * macroblockCols;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            if (nx < 0 || nx >= macroblockCols) continue;
                            sortArr[cnt++] = macroblockSum[rowBase + nx];
                        }
                    }

                    // 9元素Bose-Nelson排序网络（19次compare-swap）
                    SortPair(sortArr, 0, 1); SortPair(sortArr, 3, 4); SortPair(sortArr, 6, 7);
                    SortPair(sortArr, 1, 2); SortPair(sortArr, 4, 5); SortPair(sortArr, 7, 8);
                    SortPair(sortArr, 0, 1); SortPair(sortArr, 3, 4); SortPair(sortArr, 6, 7);
                    SortPair(sortArr, 0, 3); SortPair(sortArr, 3, 6); SortPair(sortArr, 0, 3);
                    SortPair(sortArr, 1, 4); SortPair(sortArr, 4, 7); SortPair(sortArr, 1, 4);
                    SortPair(sortArr, 2, 5); SortPair(sortArr, 5, 8); SortPair(sortArr, 2, 5);
                    SortPair(sortArr, 1, 3); SortPair(sortArr, 5, 7);
                    SortPair(sortArr, 2, 6); SortPair(sortArr, 4, 8); SortPair(sortArr, 2, 4);
                    SortPair(sortArr, 2, 3); SortPair(sortArr, 5, 6);

                    // 中值：有效数据个数为cnt，中值位置为cnt/2
                    filteredSum[y * macroblockCols + x] = sortArr[cnt / 2];
                }
            }
            macroblockSum = filteredSum;

            // ===== Step 2: 构建积分图（long[]防止4K图像溢出） =====
            int iStride = macroblockCols + 1;
            long[] integral = new long[(macroblockRows + 1) * iStride];

            for (int my = 0; my < macroblockRows; my++)
            {
                long rowSum = 0;
                int iRow = (my + 1) * iStride;
                int iPrevRow = my * iStride;
                int mRow = my * macroblockCols;

                for (int mx = 0; mx < macroblockCols; mx++)
                {
                    rowSum += macroblockSum[mRow + mx];
                    integral[iRow + mx + 1] = integral[iPrevRow + mx + 1] + rowSum;
                }
            }

            // ===== Step 3: N×N宏块均值滤波 =====
            int macroblockFilterSize = 4; // 4x4宏块 = 8x8像素
            int blockCols = macroblockCols / macroblockFilterSize;
            int blockRows = macroblockRows / macroblockFilterSize;
            int[] blockSum = new int[blockRows * blockCols];

            int maxBlockSum = 0;
            int maxBlockX = 0, maxBlockY = 0;

            for (int by = 0; by < blockRows; by++)
            {
                for (int bx = 0; bx < blockCols; bx++)
                {
                    int mStartX = bx * macroblockFilterSize;
                    int mStartY = by * macroblockFilterSize;
                    int mEndX = mStartX + macroblockFilterSize;
                    int mEndY = mStartY + macroblockFilterSize;

                    // 积分图O(1)计算区域和（4×4宏块和不会溢出int）
                    long sumLong = integral[mEndY * iStride + mEndX]
                            - integral[mStartY * iStride + mEndX]
                            - integral[mEndY * iStride + mStartX]
                            + integral[mStartY * iStride + mStartX];
                    int sum = (int)sumLong;

                    blockSum[by * blockCols + bx] = sum;

                    if (sum > maxBlockSum)
                    {
                        maxBlockSum = sum;
                        maxBlockX = bx;
                        maxBlockY = by;
                    }
                }
            }

            // ===== Step 4: 阈值分割 + Two-Pass连通域分析 =====
            int threshold = (int)(maxBlockSum * 0.95); // 高原区阈值：最大值的98%

            int[] labels = new int[blockRows * blockCols];
            int[] parent = new int[blockRows * blockCols];

            // 初始化并查集
            for (int i = 0; i < blockRows * blockCols; i++)
            {
                parent[i] = i;
            }

            // Pass 1: 标记临时标签，建立等价关系
            int nextLabel = 1;
            for (int by = 0; by < blockRows; by++)
            {
                for (int bx = 0; bx < blockCols; bx++)
                {
                    int idx = by * blockCols + bx;
                    if (blockSum[idx] < threshold) continue;

                    // 检查4个邻居（8-连通）：左、左上、上、右上
                    int leftLabel = (bx > 0 && blockSum[idx - 1] >= threshold) ? labels[idx - 1] : 0;
                    int topLeftLabel = (bx > 0 && by > 0 && blockSum[idx - blockCols - 1] >= threshold) ? labels[idx - blockCols - 1] : 0;
                    int topLabel = (by > 0 && blockSum[idx - blockCols] >= threshold) ? labels[idx - blockCols] : 0;
                    int topRightLabel = (bx < blockCols - 1 && by > 0 && blockSum[idx - blockCols + 1] >= threshold) ? labels[idx - blockCols + 1] : 0;

                    // 找到最小非零标签
                    int minLabel = 0;
                    if (leftLabel > 0) minLabel = leftLabel;
                    if (topLeftLabel > 0 && (minLabel == 0 || topLeftLabel < minLabel)) minLabel = topLeftLabel;
                    if (topLabel > 0 && (minLabel == 0 || topLabel < minLabel)) minLabel = topLabel;
                    if (topRightLabel > 0 && (minLabel == 0 || topRightLabel < minLabel)) minLabel = topRightLabel;

                    if (minLabel == 0)
                    {
                        labels[idx] = nextLabel++;
                    }
                    else
                    {
                        labels[idx] = minLabel;
                        // 合并等价标签
                        if (leftLabel > 0 && leftLabel != minLabel) Union(parent, minLabel, leftLabel);
                        if (topLeftLabel > 0 && topLeftLabel != minLabel) Union(parent, minLabel, topLeftLabel);
                        if (topLabel > 0 && topLabel != minLabel) Union(parent, minLabel, topLabel);
                        if (topRightLabel > 0 && topRightLabel != minLabel) Union(parent, minLabel, topRightLabel);
                    }
                }
            }

            // Pass 2: 统计每个连通域的面积和质心
            int componentCount = nextLabel - 1;
            int[] componentArea = new int[componentCount + 1];
            long[] componentSumX = new long[componentCount + 1];
            long[] componentSumY = new long[componentCount + 1];

            for (int by = 0; by < blockRows; by++)
            {
                for (int bx = 0; bx < blockCols; bx++)
                {
                    int idx = by * blockCols + bx;
                    if (labels[idx] == 0) continue;

                    int root = Find(parent, labels[idx]);
                    componentArea[root]++;
                    componentSumX[root] += bx;
                    componentSumY[root] += by;
                }
            }

            // 找到面积最大的连通域
            int largestComponentLabel = 0;
            int largestArea = 0;
            for (int i = 1; i <= componentCount; i++)
            {
                if (componentArea[i] > largestArea)
                {
                    largestArea = componentArea[i];
                    largestComponentLabel = i;
                }
            }

            // 计算几何重心（粗定位）
            double centroidBlockX, centroidBlockY;
            if (largestComponentLabel > 0 && largestArea > 0)
            {
                centroidBlockX = (double)componentSumX[largestComponentLabel] / largestArea;
                centroidBlockY = (double)componentSumY[largestComponentLabel] / largestArea;
            }
            else
            {
                // 降级：使用单个最亮块
                centroidBlockX = maxBlockX;
                centroidBlockY = maxBlockY;
            }

            // 计算背景基线（用于置信度评分，在Step 5和Step 7中使用）
            long backgroundBaseline = 0;
            {
                int refineRadius = Math.Max(1, macroblockFilterSize);
                int refineCenterMX = (int)(centroidBlockX * macroblockFilterSize);
                int refineCenterMY = (int)(centroidBlockY * macroblockFilterSize);

                int refineMinMX = Math.Max(0, refineCenterMX - refineRadius);
                int refineMaxMX = Math.Min(macroblockCols - 1, refineCenterMX + refineRadius);
                int refineMinMY = Math.Max(0, refineCenterMY - refineRadius);
                int refineMaxMY = Math.Min(macroblockRows - 1, refineCenterMY + refineRadius);

                // 计算窗口边缘平均亮度作为背景基线
                long edgeSum = 0;
                int edgeCount = 0;
                for (int my = refineMinMY; my <= refineMaxMY; my++)
                {
                    for (int mx = refineMinMX; mx <= refineMaxMX; mx++)
                    {
                        if (mx == refineMinMX || mx == refineMaxMX || my == refineMinMY || my == refineMaxMY)
                        {
                            long macroVal = integral[(my + 1) * iStride + (mx + 1)]
                                         - integral[my * iStride + (mx + 1)]
                                         - integral[(my + 1) * iStride + mx]
                                         + integral[my * iStride + mx];
                            edgeSum += macroVal;
                            edgeCount++;
                        }
                    }
                }
                backgroundBaseline = edgeCount > 0 ? edgeSum / edgeCount : 0;
            }

            // ===== Step 5: 灰度重心法亚像素细化 =====
            {
                int refineRadius = Math.Max(1, macroblockFilterSize);
                int refineCenterMX = (int)(centroidBlockX * macroblockFilterSize);
                int refineCenterMY = (int)(centroidBlockY * macroblockFilterSize);

                long weightedSumX = 0, weightedSumY = 0, totalWeight = 0;
                int refineMinMX = Math.Max(0, refineCenterMX - refineRadius);
                int refineMaxMX = Math.Min(macroblockCols - 1, refineCenterMX + refineRadius);
                int refineMinMY = Math.Max(0, refineCenterMY - refineRadius);
                int refineMaxMY = Math.Min(macroblockRows - 1, refineCenterMY + refineRadius);

                // 灰度重心法：权重 = 亮度 - 背景基线
                for (int my = refineMinMY; my <= refineMaxMY; my++)
                {
                    for (int mx = refineMinMX; mx <= refineMaxMX; mx++)
                    {
                        long macroVal = integral[(my + 1) * iStride + (mx + 1)]
                                     - integral[my * iStride + (mx + 1)]
                                     - integral[(my + 1) * iStride + mx]
                                     + integral[my * iStride + mx];
                        long weight = macroVal - backgroundBaseline;
                        if (weight > 0)
                        {
                            weightedSumX += weight * mx;
                            weightedSumY += weight * my;
                            totalWeight += weight;
                        }
                    }
                }

                if (totalWeight > 0)
                {
                    double refinedMX = (double)weightedSumX / totalWeight;
                    double refinedMY = (double)weightedSumY / totalWeight;
                    double refinedBlockX = refinedMX / macroblockFilterSize;
                    double refinedBlockY = refinedMY / macroblockFilterSize;
                    // 混合：70%灰度重心 + 30%几何重心
                    centroidBlockX = refinedBlockX * 0.7 + centroidBlockX * 0.3;
                    centroidBlockY = refinedBlockY * 0.7 + centroidBlockY * 0.3;
                }
            }

            // ===== Step 6: 最终像素级精细扫描（±2像素） =====
            int pixelScanRadius = 2; // 匹配±4像素容差
            int centerPixelX = (int)((centroidBlockX + 0.5) * macroblockFilterSize * 2);
            int centerPixelY = (int)((centroidBlockY + 0.5) * macroblockFilterSize * 2);

            int bestPixelX = centerPixelX, bestPixelY = centerPixelY;
            int bestPixelBrightness = 0;

            int scanStartX = Math.Max(margin + 1, centerPixelX - pixelScanRadius);
            int scanEndX = Math.Min(width - margin - 1, centerPixelX + pixelScanRadius);
            int scanStartY = Math.Max(margin + 1, centerPixelY - pixelScanRadius);
            int scanEndY = Math.Min(height - margin - 1, centerPixelY + pixelScanRadius);

            for (int py = scanStartY; py <= scanEndY; py++)
            {
                for (int px = scanStartX; px <= scanEndX; px++)
                {
                    // 使用16位宏块亮度
                    int my = py / 2;
                    int mx = px / 2;
                    if (my >= 0 && my < macroblockRows && mx >= 0 && mx < macroblockCols)
                    {
                        int _brightness = macroblockSum[my * macroblockCols + mx];
                        if (_brightness > bestPixelBrightness)
                        {
                            bestPixelBrightness = _brightness;
                            bestPixelX = px;
                            bestPixelY = py;
                        }
                    }
                }
            }

            brightestX = bestPixelX;
            brightestY = bestPixelY;


            // ===== Step 7: 偏心度验证 =====
            double centerBlockX = (blockCols - 1) / 2.0;
            double centerBlockY = (blockRows - 1) / 2.0;

            double distToCenter = Math.Sqrt(
                Math.Pow(centroidBlockX - centerBlockX, 2) +
                Math.Pow(centroidBlockY - centerBlockY, 2)
            );

            double imageDiagonal = Math.Sqrt(Math.Pow(blockCols, 2) + Math.Pow(blockRows, 2));
            double eccentricityPercent = (distToCenter / imageDiagonal) * 100;

            double maxAllowedEccentricity = 15.0; // 15%阈值
            string validationMessage = "";

            if (eccentricityPercent > maxAllowedEccentricity)
            {
                validationMessage = $"⚠️ 警告：光学中心严重偏离，偏心度 {eccentricityPercent:F1}%";
            }
            else
            {
                validationMessage = $"✓ 偏心度 {eccentricityPercent:F1}%（正常）,bestPixelBrightness:{bestPixelBrightness}";
            }

            // 置信度评分（亮度对比度）
            double contrastRatio = (double)maxBlockSum / (backgroundBaseline + 1);
            double confidenceScore = Math.Min(100, contrastRatio * 10);

            // 边界检查
            bool withinSafeMargin = brightestX >= margin && brightestX < width - margin
                                 && brightestY >= margin && brightestY < height - margin;

            if (!withinSafeMargin)
            {
                validationMessage += " | ⚠️ 结果超出安全边距";
            }

            sw.Stop();
            //Debug.WriteLine($"LSC检测完成: ({brightestX}, {brightestY}), {validationMessage}, 置信度: {confidenceScore:F1}%, 耗时: {sw.ElapsedMilliseconds}ms");
            Logger.Info($"LSC检测完成: ({brightestX}, {brightestY}), {validationMessage}, 置信度: {confidenceScore:F1}%, 耗时: {sw.ElapsedMilliseconds}ms");

            // Step 8: 返回亮度值（使用8位Y亮度用于显示）
            int bayerPolarity = (int)_vm.IspCommonConfig.Bayer;
            brightness = GetPixelYBrightness(rawBuffer, width, height,
                brightestX, brightestY, bayerPolarity);
        }

        /// <summary>
        /// 排序网络 compare-swap 原语：保证 a[i] <= a[j]
        /// </summary>
        private static void SortPair(int[] a, int i, int j)
        {
            if (a[i] > a[j])
            {
                int tmp = a[i];
                a[i] = a[j];
                a[j] = tmp;
            }
        }

        // 辅助函数：快速计算块面积和
        private long GetBlockSum(long[] integral, int stride, int x, int y, int blockSize)
        {
            int x1 = x + blockSize;
            int y1 = y + blockSize;
            return integral[y1 * stride + x1]
                 - integral[y * stride + x1]
                 - integral[y1 * stride + x]
                 + integral[y * stride + x];
        }

        /// <summary>
        /// 解码RAW数据到16位容器，保留完整精度用于LSC分析
        /// 支持RAW8（1字节/像素）和RAW10 Unpacked（2字节/像素）两种格式
        /// </summary>
        private void DecodeRaw10To16Bit(byte[] rawBuffer, int width, int height, bool isRaw8)
        {
            int pixelCount = width * height;
            _raw16Buffer = new ushort[pixelCount];

            if (isRaw8)
            {
                // RAW8：左移8位扩展到16位容器
                for (int i = 0; i < pixelCount; i++)
                    _raw16Buffer[i] = (ushort)(rawBuffer[i] << 8);
            }
            else
            {
                // RAW10 Unpacked：每像素2字节，保留完整10-bit精度
                for (int i = 0; i < pixelCount; i++)
                {
                    int idx = i * 2;
                    if (idx + 1 < rawBuffer.Length)
                        _raw16Buffer[i] = (ushort)(rawBuffer[idx] | (rawBuffer[idx + 1] << 8));
                }
            }
        }

        /// <summary>
        /// 并查集：查找根节点（带路径压缩）
        /// </summary>
        private int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]]; // 路径压缩
                x = parent[x];
            }
            return x;
        }

        /// <summary>
        /// 并查集：合并两个集合
        /// </summary>
        private void Union(int[] parent, int x, int y)
        {
            int rootX = Find(parent, x);
            int rootY = Find(parent, y);
            if (rootX != rootY)
                parent[rootX] = rootY;
        }


        /// <summary>
        /// 当找到最亮点时，自动放置描点
        /// </summary>
        private void OnBrightestPointFound(int rawX, int rawY, double brightness)
        {
            // 确保scale已计算
            if (_rawImgInnerScaleX <= 0 || _rawImgInnerScaleY <= 0)
            {
                // 延迟执行，等待OnRawImgSizeChange完成
                _isAutoDotPending = true;
                _pendingAutoDotPos = new Point(rawX, rawY);
                _pendingAutoDotBrightness = brightness;
                return;
            }

            // 坐标系统说明：
            // _dotPos 直接存储 RAW 图像的像素坐标（与原始数据一致）
            // 显示时转换为 Canvas 坐标：canvasCoord = (pixelCoord - margin) / scale
            // 注意：图像垂直翻转已取消，RAW 坐标与显示坐标方向一致
            
            // 存储像素坐标
            _dotPos = new Point(rawX, rawY);

            // 获取实际物理分辨率（用于精准映射）
            int resW = _vm.IspCommonConfig.ResolutionWidth;
            int resH = _vm.IspCommonConfig.ResolutionHeight;

            // 转换为 Canvas 坐标用于显示
            double displayX = (double)(rawX - LSC_SAFE_MARGIN) / (resW - 2 * LSC_SAFE_MARGIN) * DrawingDotAreaBorder.Width;
            double displayY = (double)(rawY - LSC_SAFE_MARGIN) / (resH - 2 * LSC_SAFE_MARGIN) * DrawingDotAreaBorder.Height;


            // 显示描点
            dot.Visibility = System.Windows.Visibility.Visible;
            Canvas.SetLeft(dot, displayX - dot.Width / 2);
            Canvas.SetTop(dot, displayY - dot.Height / 2);

            // 更新状态
            UpdateDotStatus($"✓ 自动描点 (像素: {rawX}, {rawY}) | 亮度: {brightness:F0})");
            UpdateStatus($"✓ 自动检测到最亮区域 - 像素坐标({rawX}, {rawY}) | 亮度: {brightness:F0}/255");
            UpdateProcessingStatus("✓ 自动描点完成");
            UpdateProgressInfo($"自动描点完成 | 可点击'⚙️ 计算LSC权重'按钮");

            // 切换到原图Tab
            ImgDisplayTab.SelectedIndex = 0;
        }

        private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
        {
            if (RawImg.Source == null)
            {
                MessageBox.Show(this,
                    "尚未加载RAW图像。\n\n请先点击'📂 加载RAW文件'按钮选择RAW图像。",
                    "无图像",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // 获取 Canvas 坐标（DrawingDotArea 内的相对坐标）
            var canvasPos = e.GetPosition(DrawingDotArea);
            if (canvasPos.X < 0 || canvasPos.X > DrawingDotAreaBorder.Width
                || canvasPos.Y < 0 || canvasPos.Y > DrawingDotAreaBorder.Height)
            {
                return;
            }

            // 获取实际物理分辨率
            int resW = _vm.IspCommonConfig.ResolutionWidth;
            int resH = _vm.IspCommonConfig.ResolutionHeight;

            // 反向映射：将 [0, Width] 的点击坐标还原为 [10, resW-10] 的物理像素坐标
            int rawX = (int)(canvasPos.X / DrawingDotAreaBorder.Width * (resW - 2 * LSC_SAFE_MARGIN) + LSC_SAFE_MARGIN);
            int rawY = (int)(canvasPos.Y / DrawingDotAreaBorder.Height * (resH - 2 * LSC_SAFE_MARGIN) + LSC_SAFE_MARGIN);

            _dotPos = new Point(rawX, rawY);  // 统一存储原始 RAW 坐标
            Debug.WriteLine($"描点坐标: ({rawX}, {rawY})");
            // 显示描点
            dot.Visibility = System.Windows.Visibility.Visible;
            Canvas.SetLeft(dot, canvasPos.X - dot.Width / 2);
            Canvas.SetTop(dot, canvasPos.Y - dot.Height / 2);

            // 更新描点状态（显示像素坐标）
            UpdateDotStatus($"已描点 (像素: {_dotPos.X}, {_dotPos.Y})");
            UpdateStatus($"✏️ 已在原图上描点 - 像素坐标({rawX}, {rawY}) | 可点击'⚙️ 计算LSC权重'按钮");
        }

        private void ClickCalc(object sender, RoutedEventArgs e)
        {
            if (dot.Visibility != System.Windows.Visibility.Visible)
            {
                MessageBox.Show(this,
                    "请先在'原图'Tab上点击描点！\n\n操作步骤：\n1. 切换到'📷 原图'标签页\n2. 在图像上点击选择校正中心点\n3. 点击此按钮计算LSC权重",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // _dotPos 现在直接存储 RAW 像素坐标，无需转换
            int rawX = (int)_dotPos.X;
            int rawY = (int)_dotPos.Y;

            // 算出合法的边界最大值
            int maxX = _vm.IspCommonConfig.ResolutionWidth - LSC_SAFE_MARGIN;
            int maxY = _vm.IspCommonConfig.ResolutionHeight - LSC_SAFE_MARGIN;

            int[] param = new int[] {
                Math.Max(LSC_SAFE_MARGIN, Math.Min(rawX, maxX)),
                Math.Max(LSC_SAFE_MARGIN, Math.Min(rawY, maxY))
            };

            UpdateStatus("⚙️ 正在计算LSC权重...");
            UpdateProcessingStatus("计算中...");
            UpdateProgressInfo($"使用描点坐标 ({param[0]}, {param[1]}) 计算镜头阴影校正权重");

            try
            {
                _vm.CalcLscWeightCommand.Execute(param);

                UpdateStatus($"✓ LSC权重计算完成 - 校正中心: ({param[0]}, {param[1]})");
                UpdateProcessingStatus("✓ 计算成功");
                UpdateProgressInfo($"LSC权重已生成 | 模式: {(_vm.SelectedLscMode == 0 ? "Y" : "RGB")}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"计算LSC权重时发生错误:\n{ex.Message}\n\n请检查图像数据和描点位置是否有效。",
                    "计算错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                UpdateStatus("❌ LSC权重计算失败");
                UpdateProcessingStatus("❌ 计算失败");
                UpdateProgressInfo($"错误: {ex.Message}");
            }
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            // 1. 节流控制：限制刷新率到 ~30fps
            var now = DateTime.Now;
            if ((now - _lastMouseMoveTime).TotalMilliseconds < MOUSE_MOVE_THROTTLE_MS)
            {
                return;
            }
            _lastMouseMoveTime = now;

            // 2. 判断是哪个 Canvas 触发的
            ImageSource imgSource;
            TextBlock colorDisplayBlock;

            if (sender == OriginImgCanvas)
            {
                imgSource = RawImg.Source;
                colorDisplayBlock = GetOrCreateColorDisplayBlock(ref _rawImgColorDisplayBlock, OriginImgCanvas);
            }
            else
            {
                imgSource = ProcessedImg.Source;
                colorDisplayBlock = GetOrCreateColorDisplayBlock(ref _processedImgColorDisplayBlock, ProcessedImgCanvas);
            }

            if (imgSource == null)
            {
                colorDisplayBlock.Visibility = Visibility.Collapsed;
                return;
            }

            var bitmapSource = imgSource as BitmapSource;
            if (bitmapSource == null)
            {
                colorDisplayBlock.Visibility = Visibility.Collapsed;
                return;
            }
            colorDisplayBlock.Visibility = Visibility.Visible;

            // 3. 获取鼠标位置并限制在有效范围内
            var endPoint = e.GetPosition((Canvas)sender);
            endPoint.X = Clamp(endPoint.X, _minX, _maxX);
            endPoint.Y = Clamp(endPoint.Y, _minY, _maxY);

            // 4. 将 Canvas 坐标转换为图像像素坐标
            double rangeX = _maxX - _minX;
            double rangeY = _maxY - _minY;

            // 防止除零
            if (rangeX <= 0 || rangeY <= 0)
            {
                colorDisplayBlock.Visibility = Visibility.Collapsed;
                return;
            }

            double absoluteX = (endPoint.X - _minX) / rangeX * bitmapSource.PixelWidth;
            double absoluteY = (endPoint.Y - _minY) / rangeY * bitmapSource.PixelHeight;

            // 5. 转换为整数并严格边界钳制
            int pixelX = Clamp((int)absoluteX, 0, bitmapSource.PixelWidth - 1);
            int pixelY = Clamp((int)absoluteY, 0, bitmapSource.PixelHeight - 1);

            try
            {
                // 6. 读取像素值 (BGRA 格式)
                var pixels = new byte[4];
                var sourceRect = new Int32Rect(pixelX, pixelY, 1, 1);
                var croppedBitmap = new CroppedBitmap(bitmapSource, sourceRect);
                croppedBitmap.CopyPixels(pixels, 4, 0);

                // 7. 计算 Y 亮度值 (BT.601)
                int Y = (pixels[2] * 77 + pixels[1] * 150 + pixels[0] * 29) / 256;

                // 8. 更新 TextBlock 显示
                colorDisplayBlock.Text = String.Format("R:{0},G:{1},B:{2},Y:{3}",
                    pixels[2], pixels[1], pixels[0], Y);

                // 9. 智能定位：避免超出边界
                double left = (endPoint.X + colorDisplayBlock.Width > _maxX)
                    ? endPoint.X - colorDisplayBlock.Width
                    : endPoint.X;
                double top = endPoint.Y + 10;

                colorDisplayBlock.Background = Brushes.Black;
                colorDisplayBlock.Foreground = Brushes.White;
                Canvas.SetLeft(colorDisplayBlock, left);
                Canvas.SetTop(colorDisplayBlock, top);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"取色失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 懒初始化颜色显示 TextBlock
        /// </summary>
        private TextBlock GetOrCreateColorDisplayBlock(ref TextBlock field, Canvas parentCanvas)
        {
            if (field == null)
            {
                field = new TextBlock
                {
                    Width = 150,
                    Height = 20,
                    Background = Brushes.Black,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    Padding = new Thickness(2),
                    TextAlignment = TextAlignment.Center
                };
                parentCanvas.Children.Add(field);
            }
            return field;
        }

        /// <summary>
        /// 简化的 Clamp 函数
        /// </summary>
        private static double Clamp(double value, double min, double max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        #endregion

        #region 图像尺寸变化处理

        private void OnRawImgSizeChange(object sender, SizeChangedEventArgs e)
        {

            if (RawImg.Source != null)
            {
                // 移除旧的取色显示块（布局变化后之前的 TextBlock 位置可能失效）
                if (_rawImgColorDisplayBlock != null)
                {
                    OriginImgCanvas.Children.Remove(_rawImgColorDisplayBlock);
                    _rawImgColorDisplayBlock = null;
                }
                // 不再在此处创建新 TextBlock，由 OnCanvasMouseMove 中的
                // GetOrCreateColorDisplayBlock 按需懒初始化

                // 计算 RawImg 在 OriginImgCanvas 中的实际边界（用于坐标转换）
                // 关键修复：使用 TranslatePoint 计算右边界和下边界，而不是简单的 Width-_minX
                _minX = RawImg.TranslatePoint(new Point(0, 0), OriginImgCanvas).X;
                _minY = RawImg.TranslatePoint(new Point(0, 0), OriginImgCanvas).Y;
                _maxX = RawImg.TranslatePoint(new Point(RawImg.ActualWidth, 0), OriginImgCanvas).X;
                _maxY = RawImg.TranslatePoint(new Point(0, RawImg.ActualHeight), OriginImgCanvas).Y;

                _horizontalScale = RawImg.Source.Width / (_maxX - _minX);
                _verticalScale = RawImg.Source.Height / (_maxY - _minY);

                //Debug.WriteLine($"RawImg尺寸变化: RawImg.Source.Width={RawImg.Source.Width}, RawImg.Source.Height={RawImg.Source.Height}, RawImg.Width={RawImg.Width}, RawImg.Height={RawImg.Height}, _horizontalPic={_horizontalPic}, _picScale={_picScale}");
                //Debug.WriteLine($"RawImg尺寸变化: _minX={_minX}, _maxX={_maxX}, _minY={_minY}, _maxY={_maxY}, _horizontalScale={_horizontalScale}, _verticalScale={_verticalScale}");

                Canvas.SetLeft(DrawingDotAreaBorder, _minX + 10);
                Canvas.SetTop(DrawingDotAreaBorder, _minY + 10);

                DrawingDotAreaBorder.Width = _maxX - _minX - 20;
                DrawingDotAreaBorder.Height = _maxY - _minY - 20;

                // 计算 Border 内 Canvas 到 Source 坐标的缩放比
                // Border 内 Canvas 坐标 (0,0) 对应 Source 坐标 (LSC_SAFE_MARGIN, LSC_SAFE_MARGIN)
                // Border 内 Canvas 坐标 (borderInnerWidth, borderInnerHeight) 对应 Source 坐标 (Source.Width - LSC_SAFE_MARGIN, Source.Height - LSC_SAFE_MARGIN)
                // 因此 1 个 Border 内 Canvas 像素 = (Source.Size - 2*LSC_SAFE_MARGIN) / borderInnerSize 个 Source 像素
                double borderInnerWidth = DrawingDotAreaBorder.Width;
                double borderInnerHeight = DrawingDotAreaBorder.Height;
                if (borderInnerWidth > 0 && borderInnerHeight > 0)
                {
                    _rawImgInnerScaleX = (RawImg.Source.Width - 2 * LSC_SAFE_MARGIN) / borderInnerWidth;
                    _rawImgInnerScaleY = (RawImg.Source.Height - 2 * LSC_SAFE_MARGIN) / borderInnerHeight;
                }
                else
                {
                    _rawImgInnerScaleX = _horizontalScale;
                    _rawImgInnerScaleY = _verticalScale;
                }

                //Debug.WriteLine($"RawImg尺寸变化: borderInnerWidth={borderInnerWidth}, borderInnerHeight={borderInnerHeight}, _rawImgInnerScaleX={_rawImgInnerScaleX}, _rawImgInnerScaleY={_rawImgInnerScaleY}");

                // 更新图像状态
                UpdateImageStatus($"(尺寸: {RawImg.Source.Width:F0}×{RawImg.Source.Height:F0})");

                // 处理延迟的自动描点请求
                if (_isAutoDotPending && _pendingAutoDotPos.HasValue)
                {
                    _isAutoDotPending = false;
                    var pendingPos = _pendingAutoDotPos.Value;
                    _pendingAutoDotPos = null;

                    // 延迟执行，确保布局已更新
                    double pendingBrightness = _pendingAutoDotBrightness;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        OnBrightestPointFound((int)pendingPos.X, (int)pendingPos.Y, pendingBrightness);
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }

        private void OnProcessedImgSizeChange(object sender, SizeChangedEventArgs e)
        {
            if (_processedImgColorDisplayBlock != null)
            {
                ProcessedImgCanvas.Children.Remove(_processedImgColorDisplayBlock);
                _processedImgColorDisplayBlock = null;
            }
            _processedImgColorDisplayBlock = new TextBlock();
            ProcessedImgCanvas.Children.Add(_processedImgColorDisplayBlock);
        }

        #endregion
    }

    /*
    public partial class LscWindow : Window
    {
        private LscWindowViewModel _vm = null;
        private Point _dotPos;

        private double _maxX = 1.0d;
        private double _minX = 1.0d;
        private double _maxY = 1.0d;
        private double _minY = 1.0d;

        private double _horizontalScale = 1.0d;
        private double _verticalScale = 1.0d;

        private TextBlock _rawImgColorDisplayBlock;
        private TextBlock _processedImgColorDisplayBlock;

        private const int LSC_SAFE_MARGIN = 10;

        // 鼠标移动节流控制
        private DateTime _lastMouseMoveTime = DateTime.MinValue;
        private const int MOUSE_MOVE_THROTTLE_MS = 33; // ~30fps

        public LscWindow()
        {
            InitializeComponent();
        }

        private void OnProcessedImageUpdated(object sender, DataTransferEventArgs e)
        {
            Image processedImage = (Image)sender;
            if (processedImage.Source != null)
            {
                ImgDisplayTab.SelectedIndex = 1;
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            var bufferToBitmapImageConverter = (RawBufferToBitmapImageConverter)FindResource("rawBufferToBitmapImageConverter");
            _vm = (LscWindowViewModel)DataContext;

            bufferToBitmapImageConverter.ProcessorCommonConfig = _vm.IspCommonConfig;
        }

        private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
        {
            if (RawImg.Source == null)
            {
                return;
            }

            _dotPos = e.GetPosition(DrawingDotArea);
            if (_dotPos.X < 0 || _dotPos.X > DrawingDotAreaBorder.Width
                || _dotPos.Y < 0 || _dotPos.Y > DrawingDotAreaBorder.Height)
            {
                return;
            }

            dot.Visibility = System.Windows.Visibility.Visible;
            Canvas.SetLeft(dot, _dotPos.X - dot.Width / 2);
            Canvas.SetTop(dot, _dotPos.Y - dot.Height / 2);
        }

        private void ClickCalc(object sender, RoutedEventArgs e)
        {
            if (dot.Visibility != System.Windows.Visibility.Visible)
            {
                MessageBox.Show("请先在图上描点！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int rawX = (int)(_dotPos.X * _horizontalScale + LSC_SAFE_MARGIN);
            int rawY = (int)(_dotPos.Y * _verticalScale + LSC_SAFE_MARGIN);

            // 算出合法的边界最大值
            int maxX = _vm.IspCommonConfig.ResolutionWidth - LSC_SAFE_MARGIN;
            int maxY = _vm.IspCommonConfig.ResolutionHeight - LSC_SAFE_MARGIN;

            int[] param = new int[] {
                Math.Max(LSC_SAFE_MARGIN, Math.Min(rawX, maxX)),
                Math.Max(LSC_SAFE_MARGIN, Math.Min(rawY, maxY))
            };
            _vm.CalcLscWeightCommand.Execute(param);
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            // 1. 节流控制：限制刷新率到 ~30fps
            var now = DateTime.Now;
            if ((now - _lastMouseMoveTime).TotalMilliseconds < MOUSE_MOVE_THROTTLE_MS)
            {
                return;
            }
            _lastMouseMoveTime = now;

            // 2. 判断是哪个 Canvas 触发的
            ImageSource imgSource;
            TextBlock colorDisplayBlock;

            if (sender == OriginImgCanvas)
            {
                imgSource = RawImg.Source;
                colorDisplayBlock = GetOrCreateColorDisplayBlock(ref _rawImgColorDisplayBlock, OriginImgCanvas);
            }
            else
            {
                imgSource = ProcessedImg.Source;
                colorDisplayBlock = GetOrCreateColorDisplayBlock(ref _processedImgColorDisplayBlock, ProcessedImgCanvas);
            }

            if (imgSource == null)
            {
                colorDisplayBlock.Visibility = Visibility.Collapsed;
                return;
            }

            var bitmapSource = imgSource as BitmapSource;
            if (bitmapSource == null)
            {
                colorDisplayBlock.Visibility = Visibility.Collapsed;
                return;
            }
            colorDisplayBlock.Visibility = Visibility.Visible;

            // 3. 获取鼠标位置并限制在有效范围内
            var endPoint = e.GetPosition((Canvas)sender);
            endPoint.X = Clamp(endPoint.X, _minX, _maxX);
            endPoint.Y = Clamp(endPoint.Y, _minY, _maxY);

            // 4. 将 Canvas 坐标转换为图像像素坐标
            double rangeX = _maxX - _minX;
            double rangeY = _maxY - _minY;

            // 防止除零
            if (rangeX <= 0 || rangeY <= 0)
            {
                colorDisplayBlock.Visibility = Visibility.Collapsed;
                return;
            }

            double absoluteX = (endPoint.X - _minX) / rangeX * bitmapSource.PixelWidth;
            double absoluteY = (endPoint.Y - _minY) / rangeY * bitmapSource.PixelHeight;

            // 5. 转换为整数并严格边界钳制
            int pixelX = Clamp((int)absoluteX, 0, bitmapSource.PixelWidth - 1);
            int pixelY = Clamp((int)absoluteY, 0, bitmapSource.PixelHeight - 1);

            try
            {
                // 6. 读取像素值 (BGRA 格式)
                var pixels = new byte[4];
                var sourceRect = new Int32Rect(pixelX, pixelY, 1, 1);
                var croppedBitmap = new CroppedBitmap(bitmapSource, sourceRect);
                croppedBitmap.CopyPixels(pixels, 4, 0);

                // 7. 计算 Y 亮度值 (BT.601)
                int Y = (pixels[2] * 77 + pixels[1] * 150 + pixels[0] * 29) / 256;

                // 8. 更新 TextBlock 显示
                colorDisplayBlock.Text = String.Format("R:{0},G:{1},B:{2},Y:{3}",
                    pixels[2], pixels[1], pixels[0], Y);

                // 9. 智能定位：避免超出边界
                double left = (endPoint.X + colorDisplayBlock.Width > _maxX)
                    ? endPoint.X - colorDisplayBlock.Width
                    : endPoint.X;
                double top = endPoint.Y + 10;

                colorDisplayBlock.Background = Brushes.Black;
                colorDisplayBlock.Foreground = Brushes.White;
                Canvas.SetLeft(colorDisplayBlock, left);
                Canvas.SetTop(colorDisplayBlock, top);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"取色失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 懒初始化颜色显示 TextBlock
        /// </summary>
        private TextBlock GetOrCreateColorDisplayBlock(ref TextBlock field, Canvas parentCanvas)
        {
            if (field == null)
            {
                field = new TextBlock
                {
                    Width = 150,
                    Height = 20,
                    Background = Brushes.Black,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    Padding = new Thickness(2),
                    TextAlignment = TextAlignment.Center
                };
                parentCanvas.Children.Add(field);
            }
            return field;
        }

        /// <summary>
        /// 简化的 Clamp 函数
        /// </summary>
        private static double Clamp(double value, double min, double max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private void OnRawImgSizeChange(object sender, SizeChangedEventArgs e)
        {
            
            if (RawImg.Source != null)
            {
                if (_rawImgColorDisplayBlock != null)
                {
                    OriginImgCanvas.Children.Remove(_rawImgColorDisplayBlock);
                    _rawImgColorDisplayBlock = null;
                }

                _rawImgColorDisplayBlock = new TextBlock();
                OriginImgCanvas.Children.Add(_rawImgColorDisplayBlock);

                _maxX = RawImg.Width - RawImg.TranslatePoint(new Point(0, 0), OriginImgCanvas).X;
                _minX = RawImg.TranslatePoint(new Point(0, 0), OriginImgCanvas).X;
                _maxY = RawImg.Height - RawImg.TranslatePoint(new Point(0, 0), OriginImgCanvas).Y;
                _minY = RawImg.TranslatePoint(new Point(0, 0), OriginImgCanvas).Y;

                _horizontalScale = RawImg.Source.Width / (_maxX - _minX);
                _verticalScale = RawImg.Source.Height / (_maxY - _minY);

                Canvas.SetLeft(DrawingDotAreaBorder, _minX + 10 / _horizontalScale);
                Canvas.SetTop(DrawingDotAreaBorder, _minY + 10 / _verticalScale);

                DrawingDotAreaBorder.Width = _maxX - _minX - 20 / _horizontalScale;
                DrawingDotAreaBorder.Height = _maxY - _minY - 20 / _verticalScale;
            }
            
            
            if (RawImg.Source != null)
            {
                // 1. 优化：不要每次 SizeChanged 都 new TextBlock
                // 建议将 _rawImgColorDisplayBlock 的初始化放到窗口构造函数或 Loaded 事件中
                // 如果这里必须放，至少做个判断避免重复添加
                if (_rawImgColorDisplayBlock == null)
                {
                    _rawImgColorDisplayBlock = new TextBlock();
                    OriginImgCanvas.Children.Add(_rawImgColorDisplayBlock);
                }

                // 2. 计算逻辑边界（注意：这是未翻转的布局边界）
                double imageLeft = RawImg.TranslatePoint(new Point(0, 0), OriginImgCanvas).X;
                double imageTop = RawImg.TranslatePoint(new Point(0, 0), OriginImgCanvas).Y;

                _minX = imageLeft;
                _maxX = RawImg.Width - imageLeft; // 如果 Image 完全填满 Canvas，这基本等于 Canvas.Width

                _minY = imageTop;
                _maxY = RawImg.Height - imageTop; // 如果 Image 完全填满 Canvas，这基本等于 Canvas.Height

                double logicWidth = _maxX - _minX;
                double logicHeight = _maxY - _minY;

                // 3. 防御性编程：防止除以 0 崩溃
                if (logicWidth <= 0 || logicHeight <= 0) return;

                _horizontalScale = RawImg.Source.Width / logicWidth;
                _verticalScale = RawImg.Source.Height / logicHeight;

                // ==========================================
                // 4. 核心修复：因为 XAML 中使用了 ScaleY="-1" 翻转
                // 所以在设置 Canvas 的 Top 和 Height 时，必须进行 Y 轴坐标映射转换！
                // 视觉Y坐标 = _maxY - (逻辑Y坐标 - _minY)
                // ==========================================

                double logicMargin = 10 / _horizontalScale;
                double logicBorderLeft = _minX + logicMargin;
                double logicBorderTop = _minY + logicMargin;
                double logicBorderWidth = logicWidth - (20 / _horizontalScale);
                double logicBorderHeight = logicHeight - (20 / _verticalScale);

                // 转换为视觉坐标
                double visualBorderTop = _maxY - (logicBorderTop - _minY) - logicBorderHeight;
                // 简化公式：visualBorderTop = _maxY - logicBorderTop - logicBorderHeight + _minY
                // 由于 _maxY - _minY = logicHeight，所以：
                // visualBorderTop = logicHeight - logicBorderTop - logicBorderHeight

                Canvas.SetLeft(DrawingDotAreaBorder, logicBorderLeft); // X轴不受翻转影响
                Canvas.SetTop(DrawingDotAreaBorder, visualBorderTop);  // 使用转换后的视觉 Y 坐标

                DrawingDotAreaBorder.Width = logicBorderWidth;
                DrawingDotAreaBorder.Height = logicBorderHeight;
            }
        }

        private void OnProcessedImgSizeChange(object sender, SizeChangedEventArgs e)
        {
            if (_processedImgColorDisplayBlock != null)
            {
                ProcessedImgCanvas.Children.Remove(_processedImgColorDisplayBlock);
                _processedImgColorDisplayBlock = null;
            }
            _processedImgColorDisplayBlock = new TextBlock();
            ProcessedImgCanvas.Children.Add(_processedImgColorDisplayBlock);
        }
    }*/
}
