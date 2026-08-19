using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    /// UvcViewControl.xaml 的交互逻辑
    /// </summary>
    public partial class UvcViewControl : UserControl
    {
        private const int MaxSize = 2048;
        private const int MinSize = 50;

        private static UvcViewControl uvcViewObj;

        private int _videoWidth = 0;
        private int _videoHeight = 0;
        private WriteableBitmap _bitmap;

        private int _frameCount = 0;
        private DateTime _lastFrameTime = DateTime.Now;

        public delegate void ClickCutImageHanlder();
        public event ClickCutImageHanlder ClickCutRawImage;

        public event PlayStateChangeCallbackFunc PlayStateChange;
        // 【新增】帧计数器用于降频更新
        private int _frameUpdateCounter = 0;
        // 【新增】保存Lambda事件处理器引用
        private Action<int> _continuousRawFrameLimitReachedHandler;

        // HGRM 画线相关
        private HGRM _hgrmData;
        private bool _showHgrmLines = false;

        public UvcViewControl()
        {
            InitializeComponent();

            uvcViewObj = this;

            this.KeyDown += UserControl_KeyDown;
            this.SizeChanged += UserControl_SizeChanged;

            UvcReceiver.Instance.DataReceive += OnUvcDataReceive;
            UvcReceiver.Instance.StatusChange += OnPlayStateChange;
            UvcReceiver.Instance.ContinuousRawFrameLimitReached += _continuousRawFrameLimitReachedHandler;

            // 初始化Lambda事件处理器
            _continuousRawFrameLimitReachedHandler = (maxFrames) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ChkContinuousRaw.IsChecked = false;
                    ChkContinuousRaw.Content = "🔲 连续截取";
                    UpdateOperationStatus($"连续截取已停止（达到上限: {maxFrames}）");
                    Logger.Warn($"连续截取已停止（达到上限: {maxFrames}）");
                }));
            };
        }

        private void ThumbDragDelta(object sender, DragDeltaEventArgs e)
        {
            Thumb t = sender as Thumb;

            if (t.Cursor == Cursors.SizeWE
              || t.Cursor == Cursors.SizeNESW)
            {
                this.Width = Math.Min(MaxSize,
                  Math.Max(this.Width - e.HorizontalChange,
                  MinSize));
            }

            if (t.Cursor == Cursors.SizeNS
              || t.Cursor == Cursors.SizeNESW)
            {
                this.Height = Math.Min(MaxSize,
                  Math.Max(this.Height + e.VerticalChange,
                  MinSize));
            }

            UpdateSizeInfo();
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSizeInfo();
            // 【修复】控件尺寸变化时触发 HGRM 画线更新
            // 延迟到 Loaded 优先级执行，确保 UvcImage 布局完成后再读取 ActualWidth/ActualHeight
            //Dispatcher.BeginInvoke(new Action(() =>
            //{
            //    UpdateHgrmLines();
            //}), DispatcherPriority.Loaded);
        }

        private void UserControl_KeyDown(object sender, KeyEventArgs e)
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
                }
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

        private void UpdateDeviceInfo(string info)
        {
            if (TxtDeviceInfo != null)
            {
                TxtDeviceInfo.Text = info;
            }
        }

        private void UpdateOperationStatus(string status)
        {
            if (TxtOperationStatus != null)
            {
                TxtOperationStatus.Text = status;
            }
        }

        private void UpdateSizeInfo()
        {
            if (TxtSizeInfo != null && _videoWidth > 0 && _videoHeight > 0)
            {
                TxtSizeInfo.Text = $"尺寸: {_videoWidth}×{_videoHeight}";
            }
        }

        private void UpdateFrameRate()
        {
            _frameCount++;
            DateTime now = DateTime.Now;
            TimeSpan elapsed = now - _lastFrameTime;

            if (elapsed.TotalSeconds >= 1.0)
            {
                double fps = _frameCount / elapsed.TotalSeconds;
                if (TxtFrameRate != null)
                {
                    TxtFrameRate.Text = $"FPS: {(fps * 10):F1}";
                }
                UvcReceiver.Instance.UvcFps = fps * 10;
                _frameCount = 0;
                _lastFrameTime = now;
            }
        }

        public void Initialize()
        {
            _videoWidth = UvcReceiver.Instance.VideoWidth;
            _videoHeight = UvcReceiver.Instance.VideoHeight;

            _bitmap = new WriteableBitmap(_videoWidth,
                _videoHeight, 96, 96, System.Windows.Media.PixelFormats.Rgb24, null);
            this.UvcImage.Source = _bitmap;

            // 订阅图像尺寸变化事件，确保画线位置同步调整
            //UvcImage.SizeChanged += (s, args) => UpdateHgrmLines();

            UpdateVideoStatus("● 正在连接...", false);
            UpdateOperationStatus("初始化中...");
            UpdateSizeInfo();

            // 初始显示画线（如果已有HGRM数据）
            //UpdateHgrmLines();
        }

        private void OnUvcDataReceive(byte[] dataBuffer)
        {
            if (uvcViewObj == null || dataBuffer == null)
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
                        this.UvcImage.Source = _bitmap;
                    }), DispatcherPriority.Render);
                }

                // 使用Dispatcher.BeginInvoke确保在UI线程中更新，防止跨线程操作异常
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_bitmap == null) return;
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
                        uvcViewObj._bitmap.Lock();
                        uvcViewObj._bitmap.WritePixels(
                            new Int32Rect(0, 0, (int)_bitmap.Width, (int)_bitmap.Height),
                            dataBuffer,
                            stride,  // 使用动态计算的 stride
                            0
                        );
                        uvcViewObj._bitmap.AddDirtyRect(new Int32Rect(0, 0, _videoWidth, _videoHeight));

                    }
                    finally
                    {
                        uvcViewObj._bitmap.Unlock();
                    }

                    // 【性能优化】降低UI更新频率：每10帧更新一次状态
                    if (Interlocked.Increment(ref _frameUpdateCounter) % 10 == 0)
                    {
                        UpdateVideoStatus("● 已连接", true);
                        UpdateDeviceInfo($"{_videoWidth}×{_videoHeight}");
                        UpdateOperationStatus("预览中");
                        UpdateFrameRate();
                    }
                }), DispatcherPriority.Normal);
            }
            catch
            {
                // 捕获并忽略所有异常，防止因单帧数据问题导致整个预览崩溃
                // 可以在此处添加日志记录以便调试
            }
        }

        private int OnPlayStateChange(bool isPlaying)
        {
            if (isPlaying)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateVideoStatus("● 已连接", true);
                    UpdateOperationStatus("已连接");
                }));
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateVideoStatus("● 已停止", false);
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
            UvcReceiver.Instance.DataReceive -= OnUvcDataReceive;
            UvcReceiver.Instance.StatusChange -= OnPlayStateChange;

            // 取消Lambda事件订阅
            if (_continuousRawFrameLimitReachedHandler != null)
            {
                UvcReceiver.Instance.ContinuousRawFrameLimitReached -= _continuousRawFrameLimitReachedHandler;
                _continuousRawFrameLimitReachedHandler = null;
            }

            _videoWidth = 0;
            _videoHeight = 0;

            uvcViewObj = null;

            UpdateVideoStatus("● 未连接", false);
            UpdateOperationStatus("已卸载");
        }

        private void OnCutRawClick(object sender, RoutedEventArgs e)
        {
            if (ClickCutRawImage != null)
            {
                UpdateOperationStatus("截取RAW...");
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
                    {
                        MessageBox.Show($"截帧RAW图像已保存\n文件路径：{path}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);

                        UpdateOperationStatus("RAW已保存");
                    }
                    else
                    {
                        MessageBox.Show("截取RAW图像失败：请确认处于RAW模式或有效状态", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                        UpdateOperationStatus("截取失败");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"截取RAW图像失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

                    UpdateOperationStatus("错误");
                }
            }
        }

        private void OnCutRgbClick(object sender, RoutedEventArgs e)
        {
            UpdateOperationStatus("截取RGB...");

            try
            {
                var pixels = new byte[_bitmap.PixelWidth * _bitmap.PixelHeight * _bitmap.Format.BitsPerPixel / 8];
                _bitmap.CopyPixels(pixels, _bitmap.PixelWidth * 3, 0);

                short[] rArray = new short[_bitmap.PixelWidth * _bitmap.PixelHeight];
                short[] gArray = new short[_bitmap.PixelWidth * _bitmap.PixelHeight];
                short[] bArray = new short[_bitmap.PixelWidth * _bitmap.PixelHeight];
                int len = pixels.Length / 3;
                for (int h = 0; h < _bitmap.PixelHeight; h++)
                    for (int w = 0; w < _bitmap.PixelWidth; w++)
                    {
                        rArray[(_bitmap.PixelHeight - 1 - h) * _bitmap.PixelWidth + w] = pixels[h * _bitmap.PixelWidth * 3 + w * 3 + 0];
                        gArray[(_bitmap.PixelHeight - 1 - h) * _bitmap.PixelWidth + w] = pixels[h * _bitmap.PixelWidth * 3 + w * 3 + 1];
                        bArray[(_bitmap.PixelHeight - 1 - h) * _bitmap.PixelWidth + w] = pixels[h * _bitmap.PixelWidth * 3 + w * 3 + 2];

                    }

                Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
                saveFileDialog.Title = "选择保存位置";
                saveFileDialog.CheckFileExists = false;
                saveFileDialog.CheckPathExists = false;
                saveFileDialog.Filter = "rgb文件(*.rgb) | *.rgb";
                if (!(bool)saveFileDialog.ShowDialog())
                {
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

                //#if DEBUG
                //TODO:release版本删除此代码,想法fix
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

                Microsoft.Win32.SaveFileDialog bmpSaveDialog = new Microsoft.Win32.SaveFileDialog();
                bmpSaveDialog.Title = "选择BMP保存路径";
                bmpSaveDialog.CheckFileExists = false;
                bmpSaveDialog.CheckPathExists = false;
                bmpSaveDialog.Filter = "bmp文件(*.bmp) | *.bmp";
                if (!(bool)bmpSaveDialog.ShowDialog())
                {
                    UpdateOperationStatus("RGB已保存");
                    return;
                }

                using (var fileStream = new System.IO.FileStream(bmpSaveDialog.FileName, System.IO.FileMode.Create))
                {
                    encoder.Save(fileStream);
                }

                UpdateOperationStatus("RGB已保存");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"截取RGB图像失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

                UpdateOperationStatus("错误");
            }
        }

        private void StartContinuousRawCapture()
        {
            UpdateOperationStatus("开始截取RAW图像...");

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

                    UpdateOperationStatus("图像截取中...");
                }
                else
                {
                    MessageBox.Show("连续截帧RAW图像失败：请确认处于RAW模式或有效状态", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                    UpdateOperationStatus("截取RAW图像失败");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"连续截取RAW图像失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

                UpdateOperationStatus("截取RAW图像失败");
            }
        }

        private async Task StopContinuousRawCapture()
        {
            UpdateOperationStatus("停止截取RAW图像...");

            try
            {

                bool ok = await UvcReceiver.Instance.StopContinuousRawFrameCaptureAsync();
                if (ok)
                {
                    MessageBox.Show($"连续截帧RAW图像已关闭。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);

                    UpdateOperationStatus("连续截取已停止");
                }
                else
                {
                    MessageBox.Show("连续截取RAW图像失败：请确认处于RAW模式或有效状态", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                    UpdateOperationStatus("截取RAW图像失败");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"连续截取RAW图像失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

                UpdateOperationStatus("连续截取RAW图像失败");
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
            _showHgrmLines = true;
            //UpdateHgrmLines();
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
        /// 更新HGRM画线位置
        /// 计算逻辑：Image 使用 Stretch="Uniform" 时，图像会保持宽高比居中显示。
        /// 当控件宽高比与视频宽高比不一致时，会出现黑边（letterbox/pillarbox）。
        /// 本方法计算图像在控件内的实际渲染位置，确保 HGRM 线只画在图像区域内部。
        /// </summary>
        private void UpdateHgrmLines()
        {
            if (!_showHgrmLines || _hgrmData == null || _videoWidth <= 0 || _videoHeight <= 0)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                HgrmCanvas.Children.Clear();

                // 【修复】使用 HgrmCanvas 的尺寸作为画线坐标系基准
                // HgrmCanvas 与 UvcImage 在同一个 Grid 中，尺寸应一致
                // 使用 Canvas 自身尺寸在语义上更准确，避免潜在的坐标系偏移
                double displayWidth = HgrmCanvas.ActualWidth;
                double displayHeight = HgrmCanvas.ActualHeight;

                if (displayWidth <= 0 || displayHeight <= 0)
                {
                    return;
                }

                // 计算Uniform缩放比例和偏移（处理黑边）
                double scaleX = displayWidth / _videoWidth;
                double scaleY = displayHeight / _videoHeight;
                double scale = Math.Min(scaleX, scaleY);

                double renderedWidth = _videoWidth * scale;
                double renderedHeight = _videoHeight * scale;

                // 黑边偏移：当渲染区域小于控件区域时，图像居中显示
                double offsetX = (displayWidth - renderedWidth) / 2;
                double offsetY = (displayHeight - renderedHeight) / 2;

                // 创建画笔 - X位置用红色，Y位置用绿色
                var redPen = new Pen(new SolidColorBrush(Colors.Red), 1.5);
                redPen.Freeze();
                var greenPen = new Pen(new SolidColorBrush(Colors.LimeGreen), 1.5);
                greenPen.Freeze();

                // 绘制垂直线 (ae_win_x0~x3) - 红色
                DrawVerticalLine(_hgrmData.ae_win_x0, scale, offsetX, offsetY, renderedHeight, redPen);
                DrawVerticalLine(_hgrmData.ae_win_x1, scale, offsetX, offsetY, renderedHeight, redPen);
                DrawVerticalLine(_hgrmData.ae_win_x2, scale, offsetX, offsetY, renderedHeight, redPen);
                DrawVerticalLine(_hgrmData.ae_win_x3, scale, offsetX, offsetY, renderedHeight, redPen);

                // 绘制水平线 (ae_win_y0~y3) - 绿色
                DrawHorizontalLine(_hgrmData.ae_win_y0, scale, offsetX, offsetY, renderedWidth, greenPen);
                DrawHorizontalLine(_hgrmData.ae_win_y1, scale, offsetX, offsetY, renderedWidth, greenPen);
                DrawHorizontalLine(_hgrmData.ae_win_y2, scale, offsetX, offsetY, renderedWidth, greenPen);
                DrawHorizontalLine(_hgrmData.ae_win_y3, scale, offsetX, offsetY, renderedWidth, greenPen);
            }), DispatcherPriority.Loaded);
        }

        private void DrawVerticalLine(short pixelPos, double scale, double offsetX, double offsetY, double renderedHeight, Pen pen)
        {
            if (pixelPos < 0 || pixelPos >= _videoWidth)
            {
                return;
            }

            double displayX = offsetX + pixelPos * scale;
            var line = new Line
            {
                X1 = displayX,
                Y1 = offsetY,
                X2 = displayX,
                Y2 = offsetY + renderedHeight
            };
            line.Stroke = pen.Brush;
            line.StrokeThickness = pen.Thickness;
            HgrmCanvas.Children.Add(line);
        }

        private void DrawHorizontalLine(short pixelPos, double scale, double offsetX, double offsetY, double renderedWidth, Pen pen)
        {
            if (pixelPos < 0 || pixelPos >= _videoHeight)
            {
                return;
            }

            double displayY = offsetY + pixelPos * scale;
            var line = new Line
            {
                X1 = offsetX,
                Y1 = displayY,
                X2 = offsetX + renderedWidth,
                Y2 = displayY
            };
            line.Stroke = pen.Brush;
            line.StrokeThickness = pen.Thickness;
            HgrmCanvas.Children.Add(line);
        }
    }
}
