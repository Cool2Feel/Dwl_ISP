using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ThunderSE.Common;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Uvc;

namespace ThunderSE.Ui.MainWindow
{
    /// <summary>
    /// UvcWindow.xaml 的交互逻辑
    /// </summary>
    public partial class UvcWindow : Window
    {
        private int _videoWidth = 0;
        private int _videoHeight = 0;
        private WriteableBitmap _bitmap;

        public delegate void ClickCutImageHanlder();
        public event ClickCutImageHanlder ClickCutRawImage;

        public event PlayStateChangeCallbackFunc PlayStateChange;

        private int _frameCount = 0;
        private long _totalFrameCount = 0; // 新增：累计总帧数
        private DateTime _lastFrameTime = DateTime.Now;
        private int _lastTotalFrameCount = 0; // 新增：记录上次更新时的总帧数

        // 【新增】帧计数器用于降频更新
        private int _frameUpdateCounter = 0;

        // 【新增】保存Lambda事件处理器引用
        private Action<int> _continuousRawFrameLimitReachedHandler;

        // HGRM 画线相关
        private HGRM _hgrmData;
        private bool _showHgrmLines = false;
        
        // 窗口大小变化节流控制
        private DispatcherTimer _resizeThrottleTimer;
        private bool _pendingResizeUpdate = false;

        public UvcWindow()
        {
            InitializeComponent();

            this.KeyDown += Window_KeyDown;
            this.SizeChanged += Window_SizeChanged;
            this.Closed += UvcWindow_Closed;

            UvcReceiver.Instance.DataReceive += OnUvcDataReceive;
            UvcReceiver.Instance.StatusChange += OnPlayStateChange;
            UvcReceiver.Instance.ContinuousRawFrameLimitReached += _continuousRawFrameLimitReachedHandler;

            // 初始化窗口大小变化节流定时器
            // 100ms 间隔：既能保证拖动停止后快速更新，又避免拖动过程中频繁重绘
            _resizeThrottleTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(100),
                DispatcherPriority.Background,
                (s, e) =>
                {
                    _resizeThrottleTimer.Stop();
                    if (_pendingResizeUpdate)
                    {
                        _pendingResizeUpdate = false;
                        UpdateHgrmLines();
                    }
                },
                Dispatcher);

            // 初始化Lambda事件处理器
            _continuousRawFrameLimitReachedHandler = (maxFrames) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ChkContinuousRaw != null)
                    {
                        ChkContinuousRaw.IsChecked = false;
                        ChkContinuousRaw.Content = "🔲 连续截取";
                        UpdateOperationStatus($"连续截取已停止（达到上限: {maxFrames}）");
                    }
                }));
            };
        }

        public void Onloaded(object sender, RoutedEventArgs e)
        {
            _videoWidth = UvcReceiver.Instance.VideoWidth;
            _videoHeight = UvcReceiver.Instance.VideoHeight;

            _bitmap = new WriteableBitmap(_videoWidth,
                _videoHeight, 96, 96, System.Windows.Media.PixelFormats.Rgb24, null);
            this.UvcImage.Source = _bitmap;

            // 订阅图像尺寸变化事件，使用节流机制避免拖动时卡顿
            UvcImage.SizeChanged += (s, args) =>
            {
                // 标记有待更新，重启定时器
                // 定时器会在停止拖动后 100ms 执行实际更新
                _pendingResizeUpdate = true;
                _resizeThrottleTimer.Stop();
                _resizeThrottleTimer.Start();
            };

            InitializeUIState();
            UpdateDeviceInfo();

            // 初始化 Proc Amp 图像属性控制面板
            ProcAmpPanel.DataContext = ProcAmpController.Instance;
            // 初始化 Camera Control 相机控制面板
            CameraControlPanel.DataContext = CameraControlController.Instance;
            InitializeProcAmp();

            // 【修复】使用 LayoutUpdated 事件确保在布局系统完全完成后执行
            // 这是 WPF 中最可靠的方式来获取正确的 ActualWidth/ActualHeight
            //EventHandler layoutUpdatedHandler = null;
            //layoutUpdatedHandler = (s, args) =>
            //{
            //    // 取消订阅，只执行一次
            //    this.LayoutUpdated -= layoutUpdatedHandler;

            //    System.Diagnostics.Debug.WriteLine($"[UvcWindow] LayoutUpdated triggered - showLines: {_showHgrmLines}, videoSize: {_videoWidth}x{_videoHeight}, imageSize: {UvcImage.ActualWidth}x{UvcImage.ActualHeight}");
            //    UpdateHgrmLines();
            //};
            //this.LayoutUpdated += layoutUpdatedHandler;
        }

        private void InitializeUIState()
        {
            if (UvcReceiver.Instance.IsCapturingRawFrames)
            {
                ChkContinuousRaw.IsChecked = true;
                ChkContinuousRaw.Content = "✅ 连续截取";
                UpdateOperationStatus("连续截取中...");

            }
            else
            {
                ChkContinuousRaw.IsChecked = false;
                ChkContinuousRaw.Content = "🔲 连续截取";
            }
            UpdateStatusBar("就绪 - UVC摄像头预览工具已就绪");
            UpdateDeviceStatus("正在连接摄像头...");
            TxtWindowSize.Text = $"窗口尺寸: {this.ActualWidth:F0} x {this.ActualHeight:F0}";
            UpdateOperationStatus("就绪");
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (TxtWindowSize != null)
            {
                TxtWindowSize.Text = $"窗口尺寸: {e.NewSize.Width:F0} x {e.NewSize.Height:F0}";
            }
            // 使用节流机制，避免频繁更新画线导致卡顿
            _pendingResizeUpdate = true;
            _resizeThrottleTimer.Stop();
            _resizeThrottleTimer.Start();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.R:
                        OnCutRawClick(null, null);
                        e.Handled = true;
                        break;
                    case Key.G:
                        OnCutRgbClick(null, null);
                        e.Handled = true;
                        break;
                    case Key.O:
                        OnOpenFolderClick(null, null);
                        e.Handled = true;
                        break;
                }
            }
        }

        private void UvcWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                Logger.Info("UVC窗口关闭，清理资源...");

                // 停止并清理节流定时器
                if (_resizeThrottleTimer != null)
                {
                    _resizeThrottleTimer.Stop();
                    _resizeThrottleTimer = null;
                }

                // 取消所有事件订阅
                UvcReceiver.Instance.DataReceive -= OnUvcDataReceive;
                UvcReceiver.Instance.StatusChange -= OnPlayStateChange;

                // 释放 Proc Amp / Camera Control 控制资源
                try
                {
                    ProcAmpController.Instance.Release();
                    CameraControlController.Instance.Release();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Release ProcAmp on close failed: {ex.Message}");
                }

                // 取消Lambda事件订阅
                if (_continuousRawFrameLimitReachedHandler != null)
                {
                    UvcReceiver.Instance.ContinuousRawFrameLimitReached -= _continuousRawFrameLimitReachedHandler;
                    _continuousRawFrameLimitReachedHandler = null;
                }

                // 清理事件处理器
                ClickCutRawImage = null;
                PlayStateChange = null;

                // 释放Bitmap
                _bitmap = null;
                if (UvcImage != null)
                {
                    UvcImage.Source = null;
                }

                // 清除UI控件引用
                ChkContinuousRaw = null;
                TxtWindowSize = null;

                Logger.Info("UVC窗口资源清理完成");
            }
            catch (Exception ex)
            {
                Logger.Error($"UVC窗口关闭异常: {ex.Message}");
            }
        }

        private void UpdateStatusBar(string message)
        {
            if (StatusBarText != null)
            {
                StatusBarText.Text = message;
            }
        }

        private void UpdateDeviceStatus(string status)
        {
            if (TxtDeviceStatus != null)
            {
                TxtDeviceStatus.Text = status;
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

        /// <summary>
        /// 根据当前已连接设备初始化 Proc Amp / Camera Control 控制面板
        /// </summary>
        private void InitializeProcAmp()
        {
            try
            {
                string descriptor = UvcReceiver.Instance.CurrentDeviceDescriptor;
                if (!string.IsNullOrEmpty(descriptor))
                {
                    // InitProcAmp 会一并初始化图像属性 + 相机控制两套
                    ProcAmpController.Instance.Initialize(descriptor);
                    CameraControlController.Instance.Initialize(descriptor);
                }
                else
                {
                    ProcAmpController.Instance.Release();
                    CameraControlController.Instance.Release();
                }
                ProcAmpPanel.RefreshStatus();
                CameraControlPanel.RefreshStatus();
            }
            catch (Exception ex)
            {
                Logger.Error($"InitializeProcAmp failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 打开独立的图像参数 (Proc Amp) 调节窗口
        /// </summary>
        private void OnOpenProcAmpWindow(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new ProcAmpWindow();
                win.Owner = this;
                win.Show();
            }
            catch (Exception ex)
            {
                Logger.Error($"Open ProcAmpWindow failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 打开独立的相机控制 (Camera Control) 调节窗口
        /// </summary>
        private void OnOpenCameraControlWindow(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new CameraControlWindow();
                win.Owner = this;
                win.Show();
            }
            catch (Exception ex)
            {
                Logger.Error($"Open CameraControlWindow failed: {ex.Message}");
            }
        }

        private void UpdateDeviceInfo()
        {
            if (TxtVideoWidth != null && _videoWidth > 0)
            {
                TxtVideoWidth.Text = $"{_videoWidth} 像素";
            }

            if (TxtVideoHeight != null && _videoHeight > 0)
            {
                TxtVideoHeight.Text = $"{_videoHeight} 像素";
            }

            if (TxtPixelFormat != null && _bitmap != null)
            {
                TxtPixelFormat.Text = _bitmap.Format.ToString();
            }
        }

        private void AddOperationHistory(string operation)
        {
            if (TxtOperationHistory != null)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string currentHistory = TxtOperationHistory.Text;

                if (currentHistory == "暂无操作记录")
                {
                    TxtOperationHistory.Text = $"[{timestamp}] {operation}";
                }
                else
                {
                    string[] lines = currentHistory.Split('\n');
                    if (lines.Length >= 5)
                    {
                        TxtOperationHistory.Text = $"[{timestamp}] {operation}";
                    }
                    else
                    {
                        TxtOperationHistory.Text = $"[{timestamp}] {operation}\n{currentHistory}";
                    }
                }
            }
        }

        private void UpdateFrameRate()
        {
            /*
            _frameCount++;
            DateTime now = DateTime.Now;
            TimeSpan elapsed = now - _lastFrameTime;

            if (elapsed.TotalSeconds >= 1.0)
            {
                double fps = _frameCount / elapsed.TotalSeconds;
                if (TxtFrameRate != null)
                {
                    TxtFrameRate.Text = $"FPS: {fps:F1} | 帧数: {_frameCount}";
                }

                _frameCount = 0;
                _lastFrameTime = now;
            }
            */
            // 使用原子操作保证多线程下的计数准确性
            Interlocked.Increment(ref _frameCount);
            Interlocked.Increment(ref _totalFrameCount);

            DateTime now = DateTime.Now;
            TimeSpan elapsed = now - _lastFrameTime;

            if (elapsed.TotalSeconds >= 1.0)
            {
                // 获取这一段时间内增加的帧数
                int currentTotal = (int)Interlocked.Read(ref _totalFrameCount);
                int framesInInterval = currentTotal - _lastTotalFrameCount;

                // 使用精确的时间差计算 FPS
                double fps = framesInInterval / elapsed.TotalSeconds;

                if (TxtFrameRate != null)
                {
                    // 建议在 UI 线程更新，如果此方法不在 UI 线程调用：
                    // Application.Current.Dispatcher.Invoke(() => { ... });

                    TxtFrameRate.Text = $"FPS: {(fps*10):F1} | 总帧数: {currentTotal}";
                }

                _lastTotalFrameCount = currentTotal;
                _lastFrameTime = now;

                // 如果需要重置瞬时计数器（可选，用于其他逻辑）
                Interlocked.Exchange(ref _frameCount, 0);
            }
        }

        private void OnUvcDataReceive(byte[] dataBuffer)
        {
            if (!IsLoaded)
            {
                return;
            }
            try
            {
                bool isRawBayer = UvcReceiver.Instance.IsRawBayer;

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

                    if (isGray8)
                    {
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
                        this.UvcImage.Source = _bitmap;
                    }), DispatcherPriority.Render);
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_bitmap == null) return;

                    int bytesPerPixel = isGray8 ? 1 : 3;
                    int stride = _videoWidth * bytesPerPixel;

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
                            stride,
                            0
                        );
                        _bitmap.AddDirtyRect(new Int32Rect(0, 0, _videoWidth, _videoHeight));
                    }
                    finally
                    {
                        _bitmap.Unlock();
                    }
                    // 【性能优化】降低UI更新频率：每10帧更新一次状态
                    if (Interlocked.Increment(ref _frameUpdateCounter) % 10 == 0)
                    {
                        UpdateVideoStatus("● 已连接", true);
                        UpdateDeviceStatus("UVC已连接");
                        UpdateDeviceInfo();
                        UpdateFrameRate();
                    }

                }), DispatcherPriority.Normal);
            }
            catch
            {
                // 捕获并忽略所有异常，防止因单帧数据问题导致预览崩溃

            }
        }

        private int OnPlayStateChange(bool isPlaying)
        {
            if (isPlaying)
            {
                // 视频流启动后，初始化 Proc Amp 控制（此时设备描述符已就绪）。
                // 本回调运行在 uvc.dll 工作线程，DirectShow 设备枚举按 STA 语义设计，
                // 因此切回 UI 线程执行。
                Dispatcher.BeginInvoke(new Action(InitializeProcAmp));

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateVideoStatus("● 已连接", true);
                    UpdateStatusBar("摄像头视频流已启动");
                    UpdateDeviceStatus("视频播放中...");
                    UpdateOperationStatus("已连接");
                }));
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateVideoStatus("● 已停止", false);
                    UpdateStatusBar("摄像头视频流已停止");
                    UpdateDeviceStatus("视频已暂停");
                    UpdateOperationStatus("已停止");
                }));
            }

            if (PlayStateChange != null)
            {
                PlayStateChange(isPlaying);
            }

            return 0;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
        }

        private void OnCutRawClick(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("正在截取RAW图像...");
            UpdateOperationStatus("截取中...");

            try
            {
                string runDir = Directory.GetCurrentDirectory();

                string testFolder = System.IO.Path.Combine(runDir, "TestRaw");

                Directory.CreateDirectory(testFolder);

                string path = "";
                bool isRawBayer = UvcReceiver.Instance.IsRawBayer;
                if (isRawBayer)
                {
                    path = System.IO.Path.Combine(testFolder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".RAW");
                }
                else
                {
                    path = System.IO.Path.Combine(testFolder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".JPEG");
                }

                bool ok = UvcReceiver.Instance.CaptureRawImage(path);
                if (ok)
                {
                    MessageBox.Show($"截帧RAW图像已保存\n文件路径: {path}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);

                    UpdateStatusBar($"RAW图像已保存至: {path}");
                    UpdateOperationStatus("截取完成");
                    UpdateDeviceStatus("RAW图像已保存");
                    AddOperationHistory($"截取RAW → {System.IO.Path.GetFileName(path)}");
                }
                else
                {
                    MessageBox.Show("截取RAW图像失败：请确认处于RAW模式或有效状态", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                    UpdateStatusBar("截取RAW图像失败");
                    UpdateOperationStatus("截取失败");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"截取RAW图像失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

                UpdateStatusBar($"截取失败: {ex.Message}");
                UpdateOperationStatus("错误");
            }
        }

        private void OnCutRgbClick(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("正在截取RGB图像...");
            UpdateOperationStatus("截取中...");

            try
            {
                var pixels = new byte[_bitmap.PixelWidth * _bitmap.PixelHeight * _bitmap.Format.BitsPerPixel / 8];
                _bitmap.CopyPixels(pixels, _bitmap.PixelWidth * 3, 0);

                short[] rArray = new short[_bitmap.PixelWidth * _bitmap.PixelHeight];
                short[] gArray = new short[_bitmap.PixelWidth * _bitmap.PixelHeight];
                short[] bArray = new short[_bitmap.PixelWidth * _bitmap.PixelHeight];

                for (int i = 0; i < pixels.Length / 3; i++)
                {
                    rArray[i] = pixels[i * 3 + 0];
                    gArray[i] = pixels[i * 3 + 1];
                    bArray[i] = pixels[i * 3 + 2];
                }

                Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
                saveFileDialog.Title = "选择保存位置";
                saveFileDialog.CheckFileExists = false;
                saveFileDialog.CheckPathExists = false;
                saveFileDialog.Filter = "rgb文件(*.rgb) | *.rgb";

                if (!(bool)saveFileDialog.ShowDialog())
                {
                    UpdateStatusBar("操作已取消");
                    UpdateOperationStatus("已取消");
                    return;
                }

                byte[] bytesForWrite = new byte[_bitmap.PixelWidth * _bitmap.PixelHeight * sizeof(short)];

                using (FileStream rgbFileStream = new FileStream(saveFileDialog.FileName, FileMode.Create))
                {
                    Buffer.BlockCopy(rArray, 0, bytesForWrite, 0, bytesForWrite.Length);
                    rgbFileStream.Write(bytesForWrite, 0, bytesForWrite.Length);

                    Buffer.BlockCopy(gArray, 0, bytesForWrite, 0, bytesForWrite.Length);
                    rgbFileStream.Write(bytesForWrite, 0, bytesForWrite.Length);

                    Buffer.BlockCopy(bArray, 0, bytesForWrite, 0, bytesForWrite.Length);
                    rgbFileStream.Write(bytesForWrite, 0, bytesForWrite.Length);
                }

#if DEBUG
                IntPtr[] ptrArray = new IntPtr[3];
                ptrArray[0] = Marshal.AllocHGlobal(_bitmap.PixelWidth * _bitmap.PixelHeight * sizeof(short));
                Marshal.Copy(rArray, 0, ptrArray[0], _bitmap.PixelWidth * _bitmap.PixelHeight);

                ptrArray[1] = Marshal.AllocHGlobal(_bitmap.PixelWidth * _bitmap.PixelHeight * sizeof(short));
                Marshal.Copy(gArray, 0, ptrArray[1], _bitmap.PixelWidth * _bitmap.PixelHeight);

                ptrArray[2] = Marshal.AllocHGlobal(_bitmap.PixelWidth * _bitmap.PixelHeight * sizeof(short));
                Marshal.Copy(bArray, 0, ptrArray[2], _bitmap.PixelWidth * _bitmap.PixelHeight);

                int size = 0;
                ThunderSE.DeviceConfig.Isp.IspApi.EncoderImgBuffer(ptrArray, _bitmap.PixelWidth, _bitmap.PixelHeight, 0, null, ref size);
                byte[] buffer = new byte[size];
                ThunderSE.DeviceConfig.Isp.IspApi.EncoderImgBuffer(ptrArray, _bitmap.PixelWidth, _bitmap.PixelHeight, 0, buffer, ref size);

                for (int i = 0; i < ptrArray.Length; i++)
                {
                    Marshal.FreeHGlobal(ptrArray[i]);
                }

                var image = new BitmapImage();
                using (MemoryStream memStream = new MemoryStream(buffer))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = memStream;
                    image.EndInit();
                }


                BitmapEncoder encoder = new BmpBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));

                using (var fileStream = new System.IO.FileStream("d:\\123.bmp", System.IO.FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
#endif

                UpdateStatusBar($"RGB图像已保存至: {saveFileDialog.FileName}");
                UpdateOperationStatus("截取完成");
                UpdateDeviceStatus("RGB图像已保存");
                AddOperationHistory($"截取RGB → {System.IO.Path.GetFileName(saveFileDialog.FileName)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"截取RGB图像失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

                UpdateStatusBar($"截取失败: {ex.Message}");
                UpdateOperationStatus("错误");
            }
        }

        private void OnOpenFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string runDir = Directory.GetCurrentDirectory();
                string testFolder = System.IO.Path.Combine(runDir, "TestRaw");

                if (!Directory.Exists(testFolder))
                {
                    Directory.CreateDirectory(testFolder);
                }

                System.Diagnostics.Process.Start("explorer.exe", testFolder);

                UpdateStatusBar($"已打开截图目录: {testFolder}");
                UpdateOperationStatus("目录已打开");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开目录: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar("打开目录失败");
            }
        }

        private void StartContinuousRawCapture()
        {
            UpdateStatusBar("开始截取RAW图像...");
            UpdateOperationStatus("截取中...");

            try
            {
                string runDir = Directory.GetCurrentDirectory();

                string testFolder = System.IO.Path.Combine(runDir, "MoreRaw");

                Directory.CreateDirectory(testFolder);

                int maxRawImage = TxtContinuousMax.Text != "" ? int.Parse(TxtContinuousMax.Text) : 10000;
                bool ok = UvcReceiver.Instance.StartCaptureRawImage(testFolder, maxRawImage);
                if (ok)
                {
                    MessageBox.Show($"连续截帧RAW图像已保存\n文件路径: {testFolder}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);

                    UpdateStatusBar($"连续截帧RAW图像已保存至: {testFolder}");
                    UpdateOperationStatus("截取中...");
                    AddOperationHistory($"连续截取RAW → {testFolder}");
                }
                else
                {
                    MessageBox.Show("截取RAW图像失败：请确认处于RAW模式或有效状态", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                    UpdateStatusBar("截取RAW图像失败");
                    UpdateOperationStatus("截取失败");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"截取RAW图像失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

                UpdateStatusBar($"截取失败: {ex.Message}");
                UpdateOperationStatus("错误");
            }
        }

        private async Task StopContinuousRawCapture()
        {
            UpdateStatusBar("停止截取RAW图像...");

            try
            {
                bool ok = await UvcReceiver.Instance.StopContinuousRawFrameCaptureAsync();
                if (ok)
                {
                    MessageBox.Show($"连续截帧RAW图像已关闭。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);

                    UpdateStatusBar($"连续截帧RAW图像已关闭");
                    UpdateOperationStatus("连续截取已停止");
                }
                else
                {
                    MessageBox.Show("截取RAW图像失败：请确认处于RAW模式或有效状态", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                    UpdateStatusBar("截取RAW图像失败");
                    UpdateOperationStatus("截取失败");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"截取RAW图像失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

                UpdateStatusBar($"截取失败: {ex.Message}");
                UpdateOperationStatus("错误");
            }
        }

        private async void ChkContinuousRaw_Click(object sender, RoutedEventArgs e)
        {
            if (ChkContinuousRaw.IsChecked == true)
            {
                ChkContinuousRaw.Content = "✅ 连续截取";
                StartContinuousRawCapture();
            }
            else
            {
                ChkContinuousRaw.Content = "🔲 连续截取";
                await StopContinuousRawCapture();
            }
        }

        /// <summary>
        /// 设置HGRM数据并显示画线
        /// </summary>
        public void SetHgrmData(HGRM hgrmData)
        {
            _hgrmData = hgrmData;
            //_showHgrmLines = true;
            
            //System.Diagnostics.Debug.WriteLine($"[SetHgrmData] 设置HGRM数据, IsLoaded={IsLoaded}, videoSize={_videoWidth}x{_videoHeight}");
            
            // 【修复】如果窗口已加载，立即更新画线
            // 这解决了 SetHgrmData 在 LayoutUpdated 之后调用的时序问题
            if (IsLoaded && _videoWidth > 0 && _videoHeight > 0)
            {
                //System.Diagnostics.Debug.WriteLine($"[SetHgrmData] 窗口已加载，立即更新画线");
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateHgrmLines();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// 隐藏HGRM画线
        /// </summary>
        public void HideHgrmLines()
        {
            _showHgrmLines = false;
            HgrmCanvas.Children.Clear();
        }

        /// <summary>
        /// CheckBox 状态变化事件处理：控制画线显示/隐藏
        /// </summary>
        private void OnShowHgrmLinesChanged(object sender, RoutedEventArgs e)
        {
            _showHgrmLines = ChkShowHgrmLines?.IsChecked == true;
            if (!_showHgrmLines)
            {
                HgrmCanvas.Children.Clear();
            }
            else
            {
                UpdateHgrmLines();
            }
        }

        /// <summary>
        /// 更新HGRM画线位置
        /// </summary>
        private void UpdateHgrmLines()
        {
            if (!_showHgrmLines || _hgrmData == null || _videoWidth <= 0 || _videoHeight <= 0)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateHgrmLines] 条件不满足: showLines={_showHgrmLines}, hgrmData={_hgrmData != null}, videoSize={_videoWidth}x{_videoHeight}");
                return;
            }

            HgrmCanvas.Children.Clear();

            // 【修复】使用 HgrmCanvas 的尺寸作为画线坐标系基准
            // HgrmCanvas 与 UvcImage 在同一个 Grid 中，尺寸应一致
            // 使用 Canvas 自身尺寸在语义上更准确，避免潜在的坐标系偏移
            double displayWidth = HgrmCanvas.ActualWidth;
            double displayHeight = HgrmCanvas.ActualHeight;

            //System.Diagnostics.Debug.WriteLine($"[UpdateHgrmLines] Canvas控件尺寸: {displayWidth}x{displayHeight}, 视频尺寸: {_videoWidth}x{_videoHeight}");

            if (displayWidth <= 0 || displayHeight <= 0)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateHgrmLines] Canvas控件尺寸无效，跳过画线");
                return;
            }

            // 【关键修复】计算 Uniform 缩放模式下的实际渲染区域
            // Image 使用 Stretch="Uniform" 时，图像会保持宽高比并居中显示
            // 我们需要计算图像在控件内的实际渲染位置和大小
            
            double scaleX = displayWidth / _videoWidth;
            double scaleY = displayHeight / _videoHeight;
            double scale = Math.Min(scaleX, scaleY);

            // 图像实际渲染尺寸（在控件内，不包含黑边）
            double renderedWidth = _videoWidth * scale;
            double renderedHeight = _videoHeight * scale;

            // 图像在控件内的偏移（居中显示，即黑边的宽度/高度）
            // 当有黑边时，offset 不为 0
            double offsetX = (displayWidth - renderedWidth) / 2;
            double offsetY = (displayHeight - renderedHeight) / 2;

            //System.Diagnostics.Debug.WriteLine($"[UpdateHgrmLines] scale={scale:F4}, renderedSize={renderedWidth:F2}x{renderedHeight:F2}, offset=({offsetX:F2},{offsetY:F2})");
            //System.Diagnostics.Debug.WriteLine($"[UpdateHgrmLines] HGRM坐标: x0={_hgrmData.ae_win_x0}, x1={_hgrmData.ae_win_x1}, x2={_hgrmData.ae_win_x2}, x3={_hgrmData.ae_win_x3}");
            //System.Diagnostics.Debug.WriteLine($"[UpdateHgrmLines] HGRM坐标: y0={_hgrmData.ae_win_y0}, y1={_hgrmData.ae_win_y1}, y2={_hgrmData.ae_win_y2}, y3={_hgrmData.ae_win_y3}");

            // 创建画笔 - X位置用红色，Y位置用绿色
            var redPen = new Pen(new SolidColorBrush(Colors.OrangeRed), 1.5);
            redPen.Freeze();

            // 绘制垂直线 (ae_win_x0~x3) - 红色
            DrawVerticalLine(_hgrmData.ae_win_x0, scale, offsetX, offsetY, renderedHeight, redPen, "ae_win_x0");
            DrawVerticalLine(_hgrmData.ae_win_x1, scale, offsetX, offsetY, renderedHeight, redPen, "ae_win_x1");
            DrawVerticalLine(_hgrmData.ae_win_x2, scale, offsetX, offsetY, renderedHeight, redPen, "ae_win_x2");
            DrawVerticalLine(_hgrmData.ae_win_x3, scale, offsetX, offsetY, renderedHeight, redPen, "ae_win_x3");

            // 绘制水平线 (ae_win_y0~y3) - 绿色
            DrawHorizontalLine(_hgrmData.ae_win_y0, scale, offsetX, offsetY, renderedWidth, redPen, "ae_win_y0");
            DrawHorizontalLine(_hgrmData.ae_win_y1, scale, offsetX, offsetY, renderedWidth, redPen, "ae_win_y1");
            DrawHorizontalLine(_hgrmData.ae_win_y2, scale, offsetX, offsetY, renderedWidth, redPen, "ae_win_y2");
            DrawHorizontalLine(_hgrmData.ae_win_y3, scale, offsetX, offsetY, renderedWidth, redPen, "ae_win_y3");
        }

        private void DrawVerticalLine(short pixelPos, double scale, double offsetX, double offsetY, double renderedHeight, Pen pen, string lineName)
        {
            if (pixelPos < 0 || pixelPos >= _videoWidth)
            {
                return;
            }

            double displayX = offsetX + pixelPos * scale;
            
            // 创建透明的宽线条作为点击区域（增加可交互区域）
            var hitArea = new Line
            {
                X1 = displayX,
                Y1 = offsetY,
                X2 = displayX,
                Y2 = offsetY + renderedHeight,
                Stroke = new SolidColorBrush(Colors.Transparent),
                StrokeThickness = 10,
                Cursor = Cursors.Hand
            };
            
            // 创建可见的细线条
            var line = new Line
            {
                X1 = displayX,
                Y1 = offsetY,
                X2 = displayX,
                Y2 = offsetY + renderedHeight,
                Stroke = pen.Brush,
                StrokeThickness = pen.Thickness,
                IsHitTestVisible = false
            };
            
            // 设置 ToolTip 显示 HGRM 数据
            var toolTip = new ToolTip
            {
                Content = $"{lineName}: {pixelPos}",//像素\n显示位置: {displayX:F1}
                Background = new SolidColorBrush(Color.FromArgb(230, 50, 50, 50)),
                Foreground = Brushes.White,
                Padding = new Thickness(8, 5, 8, 5),
                FontSize = 11,
                BorderBrush = pen.Brush,
                BorderThickness = new Thickness(1)
            };
            hitArea.ToolTip = toolTip;
            
            // 鼠标悬停时高亮显示
            hitArea.MouseEnter += (s, e) =>
            {
                line.StrokeThickness = 2;
            };
            hitArea.MouseLeave += (s, e) =>
            {
                line.StrokeThickness = pen.Thickness;
            };
            
            HgrmCanvas.Children.Add(hitArea);
            HgrmCanvas.Children.Add(line);
        }

        private void DrawHorizontalLine(short pixelPos, double scale, double offsetX, double offsetY, double renderedWidth, Pen pen, string lineName)
        {
            if (pixelPos < 0 || pixelPos >= _videoHeight)
            {
                return;
            }

            double displayY = offsetY + pixelPos * scale;
            
            // 创建透明的宽线条作为点击区域（增加可交互区域）
            var hitArea = new Line
            {
                X1 = offsetX,
                Y1 = displayY,
                X2 = offsetX + renderedWidth,
                Y2 = displayY,
                Stroke = new SolidColorBrush(Colors.Transparent),
                StrokeThickness = 10,
                Cursor = Cursors.Hand
            };
            
            // 创建可见的细线条
            var line = new Line
            {
                X1 = offsetX,
                Y1 = displayY,
                X2 = offsetX + renderedWidth,
                Y2 = displayY,
                Stroke = pen.Brush,
                StrokeThickness = pen.Thickness,
                IsHitTestVisible = false
            };
            
            // 设置 ToolTip 显示 HGRM 数据
            var toolTip = new ToolTip
            {
                Content = $"{lineName}: {pixelPos} ",
                Background = new SolidColorBrush(Color.FromArgb(230, 50, 50, 50)),
                Foreground = Brushes.White,
                Padding = new Thickness(8, 5, 8, 5),
                FontSize = 11,
                BorderBrush = pen.Brush,
                BorderThickness = new Thickness(1)
            };
            hitArea.ToolTip = toolTip;
            
            // 鼠标悬停时高亮显示
            hitArea.MouseEnter += (s, e) =>
            {
                line.StrokeThickness = 2;
            };
            hitArea.MouseLeave += (s, e) =>
            {
                line.StrokeThickness = pen.Thickness;
            };
            
            HgrmCanvas.Children.Add(hitArea);
            HgrmCanvas.Children.Add(line);
        }
    }

    /*
    public partial class UvcWindow : Window
    {
        //private static UvcWindow uvcWindowObj;

        private int _videoWidth = 0;
        private int _videoHeight = 0;
        private WriteableBitmap _bitmap;

        public delegate void ClickCutImageHanlder();
        public event ClickCutImageHanlder ClickCutRawImage;

        public event PlayStateChangeCallbackFunc PlayStateChange;

        public UvcWindow()
        {
            InitializeComponent();
        }

        public void Onloaded(object sender, RoutedEventArgs e)
        {
            //uvcWindowObj = this;
            UvcReceiver.Instance.DataReceive += OnUvcDataReceive;
            UvcReceiver.Instance.StatusChange += OnPlayStateChange;

            _videoWidth = UvcReceiver.Instance.VideoWidth;
            _videoHeight = UvcReceiver.Instance.VideoHeight;

            _bitmap = new WriteableBitmap(_videoWidth,
                _videoHeight, 96, 96, System.Windows.Media.PixelFormats.Rgb24, null);
            this.UvcImage.Source = _bitmap;
        }

        private void OnUvcDataReceive(byte[] dataBuffer)
        {
            if (!IsLoaded || !IsActive)
            {
                return;
            }

            bool isRawBayer = UvcReceiver.Instance.IsRawBayer;

            // 根据数据大小判断实际格式
            // Gray8: width * height * 1
            // Rgb24: width * height * 3
            int expectedGray8Size = _videoWidth * _videoHeight;
            int expectedRgb24Size = _videoWidth * _videoHeight * 3;

            bool isGray8 = isRawBayer || dataBuffer.Length == expectedGray8Size;

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
            // 使用Dispatcher.BeginInvoke确保在UI线程中更新，防止跨线程操作异常
            Dispatcher.BeginInvoke(new Action(() =>
            {
                this.UvcImage.Source = _bitmap;

                // 根据像素格式计算正确的 stride
                // Gray8: 每像素 1 字节，stride = width * 1
                // Rgb24: 每像素 3 字节，stride = width * 3
                int bytesPerPixel = isGray8 ? 1 : 3;
                int stride = _videoWidth * bytesPerPixel;

                // 验证缓冲区大小是否足够
                int requiredBufferSize = stride * _videoHeight;
                if (dataBuffer.Length < requiredBufferSize)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[WARNING] Buffer size mismatch: expected {requiredBufferSize}, got {dataBuffer.Length}");
                    return;
                }

                _bitmap.Lock();
                _bitmap.WritePixels(
                    new Int32Rect(0, 0, (int)_bitmap.Width, (int)_bitmap.Height),
                    dataBuffer,
                    stride,  // 使用动态计算的 stride
                    0
                );
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, _videoWidth, _videoHeight));
                _bitmap.Unlock();
            }), DispatcherPriority.Render);
            //if (uvcWindowObj == null)
            //{
            //    return;
            //}

            //uvcWindowObj._bitmap.Lock();
            //uvcWindowObj._bitmap.WritePixels(new Int32Rect(0, 0, (int)_bitmap.Width, (int)_bitmap.Height),
            //    dataBuffer, (int)_bitmap.Width * 3, 0);
            //uvcWindowObj._bitmap.AddDirtyRect(new Int32Rect(0, 0, _videoWidth, _videoHeight));
            //uvcWindowObj._bitmap.Unlock();
        }

        private int OnPlayStateChange(bool isPlaying)
        {
            if (PlayStateChange != null)
            {
                PlayStateChange(isPlaying);
            }

            return 0;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UvcReceiver.Instance.DataReceive -= OnUvcDataReceive;
            UvcReceiver.Instance.StatusChange -= OnPlayStateChange;

            _videoWidth = 0;
            _videoHeight = 0;

            //uvcWindowObj = null;
        }

        private void OnCutRawClick(object sender, RoutedEventArgs e)
        {
            if (ClickCutRawImage != null)
            {
                try
                {
                    //ClickCutRawImage();
                    //_cutraw = true;
                    // 捕获一帧RAW数据
                    // 1. 获取当前程序运行目录
                    string runDir = Directory.GetCurrentDirectory();

                    // 2. 拼接 test 文件夹完整路径
                    string testFolder = System.IO.Path.Combine(runDir, "TestRaw");

                    // 3. 创建 test 文件夹（如果已存在不会报错，已存在则不操作）
                    Directory.CreateDirectory(testFolder);

                    // 4. 拼接文件完整路径
                    string path = "";
                    bool isRawBayer = UvcReceiver.Instance.IsRawBayer;
                    if (isRawBayer)
                    {
                        path = System.IO.Path.Combine(testFolder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".RAW");
                    }
                    else
                    {
                        path = System.IO.Path.Combine(testFolder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".JPEG");
                    }

                    bool ok = UvcReceiver.Instance.CaptureRawImage(path);
                    if (ok)
                        MessageBox.Show($"单帧RAW数据已保存，文件路径：{path}", "Tips", MessageBoxButton.OK, MessageBoxImage.Information);
                    else
                        MessageBox.Show($"截取RAW数据需在RAW模式下有效", "Tips", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"单帧RAW数据保存失败：{ex.Message}", "Tips", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void OnCutRgbClick(object sender, RoutedEventArgs e)
        {
            var pixels = new byte[_bitmap.PixelWidth * _bitmap.PixelHeight * _bitmap.Format.BitsPerPixel / 8];
            _bitmap.CopyPixels(pixels, _bitmap.PixelWidth * 3, 0);

            short[] rArray = new short[_bitmap.PixelWidth * _bitmap.PixelHeight];
            short[] gArray = new short[_bitmap.PixelWidth * _bitmap.PixelHeight];
            short[] bArray = new short[_bitmap.PixelWidth * _bitmap.PixelHeight];

            for (int i = 0; i < pixels.Length / 3; i++)
            {
                rArray[i] = pixels[i * 3 + 0];
                gArray[i] = pixels[i * 3 + 1];
                bArray[i] = pixels[i * 3 + 2];
            }

            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
            saveFileDialog.Title = "请选择保存位置";
            saveFileDialog.CheckFileExists = false;
            saveFileDialog.CheckPathExists = false;
            saveFileDialog.Filter = "rgb文件(*.rgb) | *.rgb";
            if (!(bool)saveFileDialog.ShowDialog())
            {
                return;
            }

            byte[] bytesForWrite = new byte[_bitmap.PixelWidth * _bitmap.PixelHeight * sizeof(short)];

            using (FileStream rgbFileStream = new FileStream(saveFileDialog.FileName, FileMode.Create))
            {
                Buffer.BlockCopy(rArray, 0, bytesForWrite, 0, bytesForWrite.Length);
                rgbFileStream.Write(bytesForWrite, 0, bytesForWrite.Length);

                Buffer.BlockCopy(gArray, 0, bytesForWrite, 0, bytesForWrite.Length);
                rgbFileStream.Write(bytesForWrite, 0, bytesForWrite.Length);

                Buffer.BlockCopy(bArray, 0, bytesForWrite, 0, bytesForWrite.Length);
                rgbFileStream.Write(bytesForWrite, 0, bytesForWrite.Length);
            }

#if DEBUG
            IntPtr[] ptrArray = new IntPtr[3];
            ptrArray[0] = Marshal.AllocHGlobal(_bitmap.PixelWidth * _bitmap.PixelHeight * sizeof(short));
            Marshal.Copy(rArray, 0, ptrArray[0], _bitmap.PixelWidth * _bitmap.PixelHeight);

            ptrArray[1] = Marshal.AllocHGlobal(_bitmap.PixelWidth * _bitmap.PixelHeight * sizeof(short));
            Marshal.Copy(gArray, 0, ptrArray[1], _bitmap.PixelWidth * _bitmap.PixelHeight);

            ptrArray[2] = Marshal.AllocHGlobal(_bitmap.PixelWidth * _bitmap.PixelHeight * sizeof(short));
            Marshal.Copy(bArray, 0, ptrArray[2], _bitmap.PixelWidth * _bitmap.PixelHeight);

            int size = 0;
            ThunderSE.DeviceConfig.Isp.IspApi.EncoderImgBuffer(ptrArray, _bitmap.PixelWidth, _bitmap.PixelHeight, 0, null, ref size);
            byte[] buffer = new byte[size];
            ThunderSE.DeviceConfig.Isp.IspApi.EncoderImgBuffer(ptrArray, _bitmap.PixelWidth, _bitmap.PixelHeight, 0, buffer, ref size);

            for (int i = 0; i < ptrArray.Length; i++)
            {
                Marshal.FreeHGlobal(ptrArray[i]);
            }

            var image = new BitmapImage();
            using (MemoryStream memStream = new MemoryStream(buffer))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = memStream;
                image.EndInit();
            }


            BitmapEncoder encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using (var fileStream = new System.IO.FileStream("d:\\123.bmp", System.IO.FileMode.Create))
            {
                encoder.Save(fileStream);
            }
#endif
        }

    }
    */
}
