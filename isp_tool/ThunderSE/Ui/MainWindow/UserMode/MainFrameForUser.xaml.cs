using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ThunderSE.Uvc;

namespace ThunderSE.Ui.MainWindow.UserMode
{
    /// <summary>
    /// MainFrameForUser 的交互逻辑
    /// </summary>
    public partial class MainFrameForUser : Window
    {
        private MainFrameForUserViewModel _viewModel;

        private int _videoWidth;
        private int _videoHeight;

        private WriteableBitmap _bitmap;

        public MainFrameForUser()
        {
            InitializeComponent();

            CommonPage.DataContext = null;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel = (MainFrameForUserViewModel)DataContext;
            _viewModel.DeviceChange += OnDeviceConnect;

            _viewModel.ScanOnlineDevice();
        }

        public void OnDeviceConnect(object sender, EventArgs e)
        {
            var devChangeArgs = (MainFrameForUserViewModel.DeviceChangeEventArgs)e;
            if (devChangeArgs.IsConnect)
            {
                CommonPage.DataContext = new CommonTabViewModel(_viewModel.Config.IspProcessor.IspCommonConfig);
                EffectPage.DataContext = new EffectTabViewModel(_viewModel.Config.IspProcessor);
                LcdPage.DataContext = new LcdTabViewModel(_viewModel.Config.LcdSetting);

                UvcReceiver.Instance.DataReceive += OnUvcDataReceive;
                UvcReceiver.Instance.StatusChange += OnPlayStateChange;

                _videoWidth = UvcReceiver.Instance.VideoWidth;
                _videoHeight = UvcReceiver.Instance.VideoHeight;

                bool isRawBayer = UvcReceiver.Instance.IsRawBayer;

                // 根据数据类型创建对应格式的 WriteableBitmap
                if (isRawBayer)
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

                //_bitmap = new WriteableBitmap(_videoWidth,
                //    _videoHeight, 96, 96, PixelFormats.Rgb24, null);
                this.UvcImage.Source = _bitmap;
            }
            else
            {
                CommonPage.DataContext = null;
                EffectPage.DataContext = null;
                LcdPage.DataContext = null;

                UvcReceiver.Instance.DataReceive -= OnUvcDataReceive;
                UvcReceiver.Instance.StatusChange -= OnPlayStateChange;
            }
        }

        private void OnUvcDataReceive(byte[] dataBuffer)
        {
            bool isRawBayer = UvcReceiver.Instance.IsRawBayer;
            bool isGray8 = isRawBayer;

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

                // 根据像素格式计算正确的 stride
                // Gray8: 每像素 1 字节，stride = width * 1
                // Rgb24: 每像素 3 字节，stride = width * 3
                int bytesPerPixel = isGray8 ? 1 : 3;
                int stride = _videoWidth * bytesPerPixel;

                //Console.WriteLine($"[UI线程] 更新图像，格式: {(isGray8 ? "Gray8" : "Rgb24")}, stride: {stride}, 数据长度: {dataBuffer.Length}");
                //var sampleBytes = dataBuffer.Take(32).Select(b => b.ToString("X2")).ToArray();
                //Console.WriteLine($"UvcReceiver Data Sample : [{string.Join(", ", sampleBytes)}]");
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

            }), DispatcherPriority.Normal);
        }

        private int OnPlayStateChange(bool isPlaying)
        {
            if (!isPlaying)
            {
                UvcReceiver.Instance.Connect(_viewModel.Config.UvcInterface,
                    _viewModel.Config.IspProcessor.IspCommonConfig.ResolutionWidth,
                    _viewModel.Config.IspProcessor.IspCommonConfig.ResolutionHeight);
            }
            return 0;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UvcReceiver.Instance.DataReceive -= OnUvcDataReceive;

            _videoWidth = 0;
            _videoHeight = 0;
        }

        private void OnOpenDeviceConfigFile(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "isp配置文件(*.isp) | *.isp";
            if (!(bool)openFileDialog.ShowDialog())
            {
                return;
            }

            _viewModel.OpenOfflineConfigFileCommand.Execute(openFileDialog.FileName);
        }

        /// <summary>
        /// 查看崩溃日志
        /// </summary>
        private void OnViewCrashLog(object sender, RoutedEventArgs e)
        {
            try
            {
                var crashLogWindow = new CrashLogWindow();
                crashLogWindow.Owner = this;
                crashLogWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开崩溃日志窗口失败：\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 退出程序
        /// </summary>
        private void OnExit(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        /// <summary>
        /// 关于对话框
        /// </summary>
        private void OnAbout(object sender, RoutedEventArgs e)
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            MessageBox.Show(
                $"ThunderSE - ISP 调试工具\n\n" +
                $"版本：{version}\n" +
                $".NET Framework: {Environment.Version}\n\n" +
                "© 2026 All Rights Reserved",
                "关于",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
