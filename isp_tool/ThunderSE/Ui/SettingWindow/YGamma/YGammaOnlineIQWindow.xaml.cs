using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Model;
using ThunderSE.Ui.CommonCustomControl;
using ThunderSE.Uvc;


namespace ThunderSE.Ui.SettingWindow.YGamma
{
    /// <summary>
    /// YGammaOnlineIQWindow.xaml 的交互逻辑
    /// </summary>
    public partial class YGammaOnlineIQWindow : Window
    {
        private int _videoWidth = 0;
        private int _videoHeight = 0;
        private WriteableBitmap _bitmap;

        private double _horizontalScale = 1.0;
        private double _verticalScale = 1.0;

        private double[] _avgRArray = new double[6];
        private double[] _avgGArray = new double[6];
        private double[] _avgBArray = new double[6];
        private int _selectedCalcMode = 0;

        private ChartData _chartData = new ChartData();

        private DispatcherTimer timerForCalcIQ = new DispatcherTimer();

        private List<RubberBandData> _rubberBandData = new List<RubberBandData>();

        public static readonly DependencyProperty IsDrawingProperty = DependencyProperty.Register("IsDrawing",
            typeof(bool),
            typeof(YGammaOnlineIQWindow),
            new PropertyMetadata(true));

        public bool IsDrawing
        {
            get { return (bool)GetValue(IsDrawingProperty); }
            set { SetValue(IsDrawingProperty, value); }
        }

        public static readonly DependencyProperty IsCalculatingProperty = DependencyProperty.Register("IsCalculating",
            typeof(bool),
            typeof(YGammaOnlineIQWindow),
            new PropertyMetadata(false));


        public bool IsCalculating
        {
            get { return (bool)GetValue(IsCalculatingProperty); }
            set { SetValue(IsCalculatingProperty, value); }
        }


        public int SelectedCalcMode
        {
            get { return _selectedCalcMode; }
            set
            {
                switch (value)
                {
                    case 0:
                        DisplayControl.MaxBands = 6;
                        Array.Resize(ref _avgRArray, 6);
                        Array.Resize(ref _avgGArray, 6);
                        Array.Resize(ref _avgBArray, 6);
                        break;

                    case 1:
                        DisplayControl.MaxBands = 13;
                        Array.Resize(ref _avgRArray, 39);
                        Array.Resize(ref _avgGArray, 39);
                        Array.Resize(ref _avgBArray, 39);
                        break;

                    default:
                        break;
                }
                _selectedCalcMode = value;
                DisplayControl.ClearRubberBands();
            }
        }


        public YGammaOnlineIQWindow()
        {
            DataContext = new ObservableCollection<KeyValuePair<string, string>>();
            InitializeComponent();

            UvcReceiver.Instance.DataReceive += OnUvcDataReceive;
            UvcReceiver.Instance.StatusChange += OnPlayStateChange;
        }

        public void Onloaded(object sender, RoutedEventArgs e)
        {
            DisplayControl.DataContext = _rubberBandData;
            DisplayControl.MaxBands = 6;

            _videoWidth = UvcReceiver.Instance.VideoWidth;
            _videoHeight = UvcReceiver.Instance.VideoHeight;

            _bitmap = new WriteableBitmap(_videoWidth,
                _videoHeight, 96, 96, System.Windows.Media.PixelFormats.Rgb24, null);
            DisplayControl.DisplayImageSource = _bitmap;

            timerForCalcIQ.Tick += OnCalcIQ;
            timerForCalcIQ.Interval = new TimeSpan(20000000);

            // 初始化UI状态
            InitializeUI();
        }

        private void InitializeUI()
        {
            UpdateStatus("✓ Y-Gamma在线IQ分析工具初始化完成");
            UpdateVideoStatus($"(分辨率: {_videoWidth}×{_videoHeight})");
            UpdateCalcStatus("就绪 - 等待框选色块区域");
            UpdateProgressInfo("请连接摄像头并开始框选色块进行IQ分析");
        }

        private async void OnCalcIQ(object sender, EventArgs e)
        {
            /*
            new Thread(() =>
            {
                double[] diff_l = new double[6] { 10, 10, 10, 10, 10, 10 };
                int ref_count;
                double[] l_val_array;
                double[] delta_l_array;
                double yMax;
                double[] yAvg;
                double out_gamma;
                var values = new ObservableCollection<KeyValuePair<string, string>>();

                if (SelectedCalcMode == 0)
                {
                    ref_count = 0;
                    l_val_array = new double[6];
                    delta_l_array = new double[6];
                    yMax = 0.0;
                    yAvg = new double[6 * 3];
                    out_gamma = 0.0;

                    values = new ObservableCollection<KeyValuePair<string, string>>(){
                        new KeyValuePair<string, string>("ref_count", "0"),
                        new KeyValuePair<string, string>("l_val_array", "[0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]"),
                        new KeyValuePair<string, string>("delta_l_array", "[0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]"),
                    };

                    IspApi.YGAMMA_IQ(_avgRArray, _avgGArray, _avgBArray, 6, diff_l, ref ref_count, l_val_array,
                        delta_l_array, ref yMax, yAvg, ref out_gamma);

                }
                else
                {
                    ref_count = 0;
                    l_val_array = new double[13];
                    delta_l_array = new double[13];
                    yMax = 0.0;
                    yAvg = new double[13 * 3];
                    out_gamma = 0.0;

                    IspApi.YGAMMA_IQ(_avgRArray, _avgGArray, _avgBArray, 13, diff_l, ref ref_count, l_val_array,
                        delta_l_array, ref yMax, yAvg, ref out_gamma);

                    values = new ObservableCollection<KeyValuePair<string, string>>(){
                        new KeyValuePair<string, string>("yMax", "0"),
                        new KeyValuePair<string, string>("yAvg", "0"),
                        new KeyValuePair<string, string>("out_gamma", "0"),
                    };

                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        Dictionary<double, double> tmpYAvgDict = new Dictionary<double, double>();
                        for (int i = 0; i < yAvg.Length; i++)
                        {
                            tmpYAvgDict[i / 3.0] = yAvg[i] / 255;
                        }
                        _chartData.yAvg = tmpYAvgDict;
                        _chartData.OutGamma = out_gamma;
                    }));
                }

                for (int i = 0; i < values.Count; i++)
                {
                    switch (values[i].Key)
                    {
                        case "ref_count":
                            values[i] = new KeyValuePair<string, string>("ref_count", ref_count.ToString());
                            break;

                        case "l_val_array":
                            {
                                string arrayStr = string.Join(",", l_val_array.Select(x => x.ToString("0.00")).ToArray());
                                arrayStr = "[" + arrayStr + "]";
                                values[i] = new KeyValuePair<string, string>("l_val_array", arrayStr);
                            }
                            break;

                        case "delta_l_array":
                            {
                                string arrayStr = string.Join(",", delta_l_array.Select(x => x.ToString("0.00")).ToArray());
                                arrayStr = "[" + arrayStr + "]";
                                values[i] = new KeyValuePair<string, string>("delta_l_array", arrayStr);
                            }
                            break;

                        case "yMax":
                            values[i] = new KeyValuePair<string, string>("yMax", yMax.ToString());
                            break;

                        case "yAvg":
                            {
                                var yAvgValueStrArray = yAvg.Select(x => x.ToString("0.00")).ToArray();
                                var yAvgMidValueStrArray = new string[yAvgValueStrArray.Length / 3];

                                for (int j = 0; j < yAvgValueStrArray.Length / 3; j++)
                                {
                                    yAvgMidValueStrArray[j] = yAvgValueStrArray[j * 3 + 1];
                                }

                                string arrayStr = string.Join(",", yAvgMidValueStrArray);
                                arrayStr = "[" + arrayStr + "]";
                                values[i] = new KeyValuePair<string, string>("yAvg", arrayStr);
                            }
                            break;

                        case "out_gamma":
                            values[i] = new KeyValuePair<string, string>("out_gamma", out_gamma.ToString());
                            break;

                        default:
                            break;
                    }
                }

                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    DataContext = values;
                }));
            }).Start();
            */

            timerForCalcIQ.Stop();
            IsCalculating = true;
            IsDrawing = false;

            try
            {
                double[] diff_l = new double[6] { 10, 10, 10, 10, 10, 10 };
                int ref_count;
                double[] l_val_array;
                double[] delta_l_array;
                double yMax;
                double[] yAvg;
                double out_gamma;
                var values = new ObservableCollection<KeyValuePair<string, string>>();

                await Task.Run(() =>
                {
                    if (SelectedCalcMode == 0)
                    {
                        ref_count = 0;
                        l_val_array = new double[6];
                        delta_l_array = new double[6];
                        yMax = 0.0;
                        yAvg = new double[6 * 3];
                        out_gamma = 0.0;

                        values = new ObservableCollection<KeyValuePair<string, string>>(){
                        new KeyValuePair<string, string>("ref_count", "0"),
                        new KeyValuePair<string, string>("l_val_array", "[0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]"),
                        new KeyValuePair<string, string>("delta_l_array", "[0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]"),
                    };

                        IspApi.YGAMMA_IQ(_avgRArray, _avgGArray, _avgBArray, 6, diff_l, ref ref_count, l_val_array,
                            delta_l_array, ref yMax, yAvg, ref out_gamma);

                    }
                    else
                    {
                        ref_count = 0;
                        l_val_array = new double[13];
                        delta_l_array = new double[13];
                        yMax = 0.0;
                        yAvg = new double[13 * 3];
                        out_gamma = 0.0;

                        IspApi.YGAMMA_IQ(_avgRArray, _avgGArray, _avgBArray, 13, diff_l, ref ref_count, l_val_array,
                            delta_l_array, ref yMax, yAvg, ref out_gamma);

                        values = new ObservableCollection<KeyValuePair<string, string>>(){
                        new KeyValuePair<string, string>("yMax", "0"),
                        new KeyValuePair<string, string>("yAvg", "0"),
                        new KeyValuePair<string, string>("out_gamma", "0"),
                    };

                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            Dictionary<double, double> tmpYAvgDict = new Dictionary<double, double>();
                            for (int i = 0; i < yAvg.Length; i++)
                            {
                                tmpYAvgDict[i / 3.0] = yAvg[i] / 255;
                            }
                            _chartData.yAvg = tmpYAvgDict;
                            _chartData.OutGamma = out_gamma;
                        }));
                    }

                    for (int i = 0; i < values.Count; i++)
                    {
                        switch (values[i].Key)
                        {
                            case "ref_count":
                                values[i] = new KeyValuePair<string, string>("ref_count", ref_count.ToString());
                                break;

                            case "l_val_array":
                                {
                                    string arrayStr = string.Join(",", l_val_array.Select(x => x.ToString("0.00")).ToArray());
                                    arrayStr = "[" + arrayStr + "]";
                                    values[i] = new KeyValuePair<string, string>("l_val_array", arrayStr);
                                }
                                break;

                            case "delta_l_array":
                                {
                                    string arrayStr = string.Join(",", delta_l_array.Select(x => x.ToString("0.00")).ToArray());
                                    arrayStr = "[" + arrayStr + "]";
                                    values[i] = new KeyValuePair<string, string>("delta_l_array", arrayStr);
                                }
                                break;

                            case "yMax":
                                values[i] = new KeyValuePair<string, string>("yMax", yMax.ToString());
                                break;

                            case "yAvg":
                                {
                                    var yAvgValueStrArray = yAvg.Select(x => x.ToString("0.00")).ToArray();
                                    var yAvgMidValueStrArray = new string[yAvgValueStrArray.Length / 3];

                                    for (int j = 0; j < yAvgValueStrArray.Length / 3; j++)
                                    {
                                        yAvgMidValueStrArray[j] = yAvgValueStrArray[j * 3 + 1];
                                    }

                                    string arrayStr = string.Join(",", yAvgMidValueStrArray);
                                    arrayStr = "[" + arrayStr + "]";
                                    values[i] = new KeyValuePair<string, string>("yAvg", arrayStr);
                                }
                                break;

                            case "out_gamma":
                                values[i] = new KeyValuePair<string, string>("out_gamma", out_gamma.ToString());
                                break;

                            default:
                                break;
                        }
                    }

                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        DataContext = values;
                    }));
                });
            }
            finally
            {
                IsCalculating = false;
                IsDrawing = true;
                timerForCalcIQ.Start();
            }
        }

        private void OnUvcDataReceive(byte[] dataBuffer)
        {
            //_bitmap.Lock();
            //_bitmap.WritePixels(new Int32Rect(0, 0, (int)_bitmap.Width, (int)_bitmap.Height),
            //    dataBuffer, (int)_bitmap.Width * 3, 0);
            //_bitmap.AddDirtyRect(new Int32Rect(0, 0, _videoWidth, _videoHeight));
            //_bitmap.Unlock();
            try
            {
                bool isRawBayer = UvcReceiver.Instance.IsRawBayer;

                // 根据数据大小判断实际格式
                // Gray8: width * height * 1
                // Rgb24: width * height * 3
                int expectedGray8Size = _videoWidth * _videoHeight;
                int expectedRgb24Size = _videoWidth * _videoHeight * 3;

                bool isGray8 = false; //isRawBayer || dataBuffer.Length == expectedGray8Size;

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
                    }), DispatcherPriority.Normal);
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

                }), DispatcherPriority.Normal);
            }
            catch
            {
                // 捕获并忽略所有异常，防止因单帧数据问题导致程序崩溃
            }

            if (_rubberBandData.Count > 0)
            {
                for (int i = 0; i < _rubberBandData.Count; i++)
                {
                    if (SelectedCalcMode == 0)
                    {
                        var croppedBitmap = new CroppedBitmap(_bitmap,
                            new Int32Rect(_rubberBandData[i].x, _rubberBandData[i].y, _rubberBandData[i].width,
                                _rubberBandData[i].height));

                        var pixels = new byte[_rubberBandData[i].width * _rubberBandData[i].height *
                            croppedBitmap.Format.BitsPerPixel / 8];

                        croppedBitmap.CopyPixels(pixels, _rubberBandData[i].width * 3, 0);

                        int rSum = 0;
                        int gSum = 0;
                        int bSum = 0;

                        for (int j = 0; j < pixels.Length / 3; j++)
                        {
                            rSum += pixels[j * 3 + 2];
                            gSum += pixels[j * 3 + 1];
                            bSum += pixels[j * 3 + 0];
                        }

                        _avgRArray[i] = rSum / (pixels.Length / 3);
                        _avgGArray[i] = gSum / (pixels.Length / 3);
                        _avgBArray[i] = bSum / (pixels.Length / 3);
                    }
                    else
                    {
                        //每一个小框都分三段来做
                        for (int j = 0; j < 3; j++)
                        {
                            int tmpY = _rubberBandData[i].y + _rubberBandData[i].height / 3 * j;
                            int tmpHeight = _rubberBandData[i].height / 3;

                            var croppedBitmap = new CroppedBitmap(_bitmap,
                                new Int32Rect(_rubberBandData[i].x, tmpY, _rubberBandData[i].width, tmpHeight));

                            var pixels = new byte[_rubberBandData[i].width * _rubberBandData[i].height / 3 *
                                croppedBitmap.Format.BitsPerPixel / 8];

                            croppedBitmap.CopyPixels(pixels, _rubberBandData[i].width * 3, 0);

                            int rSum = 0;
                            int gSum = 0;
                            int bSum = 0;

                            for (int k = 0; k < pixels.Length / 3; k++)
                            {
                                rSum += pixels[k * 3 + 2];
                                gSum += pixels[k * 3 + 1];
                                bSum += pixels[k * 3 + 0];
                            }

                            _avgRArray[i * 3 + j] = rSum / (pixels.Length / 3);
                            _avgGArray[i * 3 + j] = gSum / (pixels.Length / 3);
                            _avgBArray[i * 3 + j] = bSum / (pixels.Length / 3);
                        }
                    }
                }
            }
        }

        private void OnDisplayControlSizeChange(object sender, SizeChangedEventArgs e)
        {
            if (DisplayControl.DisplayImageSource != null)
            {
                _horizontalScale = DisplayControl.Width / DisplayControl.DisplayImageSource.Width;
                _verticalScale = DisplayControl.Height / DisplayControl.DisplayImageSource.Height;
            }
        }

        private int OnPlayStateChange(bool isPlaying)
        {
            return 0;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UvcReceiver.Instance.DataReceive -= OnUvcDataReceive;
            UvcReceiver.Instance.StatusChange -= OnPlayStateChange;

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

            timerForCalcIQ.Stop();
            IsCalculating = false;
            IsDrawing = true;
        }


        private void OnClickCalcIQ(object sender, RoutedEventArgs e)
        {
            timerForCalcIQ.Start();
            IsCalculating = true;
            IsDrawing = false;

            UpdateStatus("🧮 开始Y-Gamma在线IQ计算...");
            UpdateCalcStatus("计算中");
            UpdateProgressInfo($"模式: {(SelectedCalcMode == 0 ? "6阶" : "13阶")}色卡 | 正在采集数据并计算...");
        }

        private void OnClickStopCalcIQ(object sender, RoutedEventArgs e)
        {
            timerForCalcIQ.Stop();
            IsCalculating = false;
            IsDrawing = true;

            UpdateStatus("⏹️ IQ计算已停止");
            UpdateCalcStatus("已停止 - 等待重新开始");
            UpdateProgressInfo("可以继续框选色块或查看结果数据");
        }

        private void OnClickUndoRubberBand(object sender, RoutedEventArgs e)
        {
            DisplayControl.UndoDrawRubberBand();

            UpdateStatus("↩️ 已撤销上一次选框操作");
            UpdateProgressInfo($"当前选区数: {_rubberBandData.Count}");
        }

        private void OnClickShowGammaChart(object sender, RoutedEventArgs e)
        {
            YGammaIQChartWindow chartWindow = new YGammaIQChartWindow();
            chartWindow.DataContext = _chartData;
            chartWindow.Show();

            UpdateStatus("📊 已打开Y-Gamma曲线图表窗口");
        }

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

        private void UpdateVideoStatus(string status)
        {
            if (TxtVideoStatus != null)
            {
                TxtVideoStatus.Text = status;
            }
        }

        private void UpdateCalcStatus(string status)
        {
            if (TxtCalcStatus != null)
            {
                TxtCalcStatus.Text = status;

                // 根据计算状态改变颜色
                if (status.Contains("计算中"))
                {
                    TxtCalcStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x00)); // 橙色
                }
                else if (status.Contains("已完成") || status.Contains("就绪"))
                {
                    TxtCalcStatus.Foreground = Brushes.Green;
                }
                else if (status.Contains("停止") || status.Contains("等待"))
                {
                    TxtCalcStatus.Foreground = Brushes.Gray;
                }
                else
                {
                    TxtCalcStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7)); // 蓝色
                }
            }
        }

        #endregion

        #region 窗口事件增强

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // 设置键盘快捷键
            InputBindings.Add(new KeyBinding(new RelayCommand(() => OnClickCalcIQ(null, null)),
                Key.Enter, ModifierKeys.Control));

            InputBindings.Add(new KeyBinding(new RelayCommand(() => OnClickStopCalcIQ(null, null)),
                Key.Escape, ModifierKeys.Control));

            InputBindings.Add(new KeyBinding(new RelayCommand(() => OnClickUndoRubberBand(null, null)),
                Key.Z, ModifierKeys.Control));

            InputBindings.Add(new KeyBinding(new RelayCommand(() => OnClickShowGammaChart(null, null)),
                Key.G, ModifierKeys.Control));

            this.SizeChanged += (s, args) =>
            {
                UpdateProgressInfo($"窗口尺寸: {this.ActualWidth:F0}×{this.ActualHeight:F0}");
            };

            this.Closing += (s, args) =>
            {
                if (IsCalculating)
                {
                    var result = MessageBox.Show(this,
                        "当前正在进行IQ计算，确定要关闭窗口吗？",
                        "确认关闭",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.No)
                    {
                        args.Cancel = true;
                        return;
                    }
                }

                UpdateStatus("正在关闭Y-Gamma在线IQ分析窗口...");
            };
        }

        #endregion

    }
}
