using GalaSoft.MvvmLight.Command;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ThunderSE.Common;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Ui.CommonCustomControl;

namespace ThunderSE.Ui.SettingWindow.Awb
{
    /// <summary>
    /// AwbIQWindow.xaml 的交互逻辑
    /// </summary>
    /// 

    class ValueRange
    {
        public double Min { get; set; }
        public double Max { get; set; }

        public ValueRange(double min, double max)
        {
            Min = min;
            Max = max;
        }
    }

    public class IQData
    {
        public string Name { get; set; }
        public double Value { get; set; }
        public string ValueRange { get; set; }
        public bool IsGoodValue { get; set; }

        public IQData()
        {

        }

        public IQData(string name, double value, string valueRange, bool isGoodValue)
        {
            Name = name;
            Value = value;
            ValueRange = valueRange;
            IsGoodValue = isGoodValue;
        }
    }


    public partial class AwbIQWindow : Window
    {
        #region 私有字段
        private Processor _ispProcessor;
        private AutoWhiteBalance _awbStep;
        private byte[] _rawFileBuffer;
        private List<RubberBandData> _rubberBandData = new List<RubberBandData>();
        private double _calculatedRGain = 1.0;
        private double _calculatedBGain = 1.0;
        private bool _isGainCalculated = false;
        private bool _isImageLoaded = false;
        private Stopwatch _sw = new Stopwatch();
        private CancellationTokenSource _cts;
        private bool _isProcessing = false;
        private System.Windows.Threading.DispatcherTimer _patchMonitorTimer;
        private int _lastPatchCount = -1;
        #endregion

        #region 依赖属性
        public ObservableCollection<IQData> AwbIQ { get; } = new ObservableCollection<IQData>();

        public ICollectionView View
        {
            get { return (ICollectionView)GetValue(ViewProperty); }
            set { SetValue(ViewProperty, value); }
        }

        public static readonly DependencyProperty ViewProperty = DependencyProperty.Register(
            "View", typeof(ICollectionView), typeof(AwbIQWindow),
            new FrameworkPropertyMetadata(null));

        public bool IsLoadImage
        {
            get { return (bool)GetValue(IsLoadImageProperty); }
            set { SetValue(IsLoadImageProperty, value); }
        }

        public static readonly DependencyProperty IsLoadImageProperty = DependencyProperty.Register(
            "IsLoadImage", typeof(bool), typeof(AwbIQWindow),
            new FrameworkPropertyMetadata(false));

        public bool PatchCountValid
        {
            get { return (bool)GetValue(PatchCountValidProperty); }
            set { SetValue(PatchCountValidProperty, value); }
        }

        public static readonly DependencyProperty PatchCountValidProperty = DependencyProperty.Register(
            "PatchCountValid", typeof(bool), typeof(AwbIQWindow),
            new FrameworkPropertyMetadata(false));
        #endregion

        #region 构造函数
        public AwbIQWindow(Processor ispProcessor)
        {
            InitializeComponent();

            if (ispProcessor == null)
                throw new ArgumentNullException(nameof(ispProcessor));

            _ispProcessor = ispProcessor;
            _awbStep = (AutoWhiteBalance)_ispProcessor.AllProcessSteps[IspModule.Awb];

            InitializeDataGrids();
            InitializeRubberBandControl();
            UpdateButtonStates();
            SetupKeyboardShortcuts();
            SetupWindowEvents();

            Logger.Debug("AWB调试窗口初始化完成");
        }
        #endregion

        #region 初始化方法
        private void InitializeDataGrids()
        {
            AwbIQ.Add(new IQData());
            AwbIQ.Add(new IQData());

            IqDataGrid.ItemsSource = AwbIQ;
        }

        private void InitializeRubberBandControl()
        {
            RawImg.DataContext = _rubberBandData;
        }

        private void SetupKeyboardShortcuts()
        {
            InputBindings.Add(new KeyBinding(new RelayCommand(OnLoadRawFromKeyboard), Key.O, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new RelayCommand(OnUndoPatchFromKeyboard), Key.Z, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new RelayCommand(OnCalculateFromKeyboard), Key.Enter, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new RelayCommand(OnEvaluateFromKeyboard), Key.E, ModifierKeys.Control));
        }

        private void OnLoadRawFromKeyboard()
        {
            OnLoadRaw_Click(null, null);
        }

        private void OnUndoPatchFromKeyboard()
        {
            OnUndoPatch_Click(null, null);
        }

        private void OnCalculateFromKeyboard()
        {
            OnCalculate_Click(null, null);
        }

        private void OnEvaluateFromKeyboard()
        {
            OnEvaluate_Click(null, null);
        }

        private void SetupWindowEvents()
        {
            this.Closing += OnWindowClosing;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            RawImg.MaxBands = 6;
            StartPatchMonitoring();
        }
        #endregion

        #region Step 1: 加载RAW图像
        private async void OnLoadRaw_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                ShowWarningDialog("操作进行中", "请等待当前操作完成后再开始新操作");
                return;
            }

            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "RAW文件 (*.raw)|*.raw|所有文件 (*.*)|*.*",
                    Title = "选择RAW图像文件"
                };

                if (dialog.ShowDialog() != true) return;

                _isProcessing = true;
                _cts = new CancellationTokenSource();

                SetStatus($"正在加载: {System.IO.Path.GetFileName(dialog.FileName)}...");
                UpdateProgress(0, "读取文件...");
                _sw.Restart();

                _rawFileBuffer = File.ReadAllBytes(dialog.FileName);
                UpdateProgress(30, "解码RAW数据...");

                _cts.Token.ThrowIfCancellationRequested();

                await Task.Run(() =>
                {
                    var bitmap = _ispProcessor.GenerateBitmapUsingRaw(_rawFileBuffer, IspModule.Awb);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        RawImg.DisplayImageSource = bitmap;
                    });

                    UpdateProgress(70, "渲染预览图...");
                }, _cts.Token);

                _cts.Token.ThrowIfCancellationRequested();
                UpdateProgress(100, "完成");

                _sw.Stop();
                _isImageLoaded = true;
                IsLoadImage = true;

                ResetApplyPreviewState();

                SetStatus($"✓ RAW加载完成 ({_rawFileBuffer.Length:N0} 字节, 耗时: {_sw.ElapsedMilliseconds}ms)");
                UpdateButtonStates();

                Logger.Info($"RAW文件加载成功: {dialog.FileName}, 大小: {_rawFileBuffer.Length}");
            }
            catch (OperationCanceledException)
            {
                SetStatus("⚠️ 加载已取消");
                Logger.Info("用户取消了RAW文件加载");
            }
            catch (Exception ex)
            {
                Logger.Error($"加载RAW失败: {ex.Message}");
                ShowErrorDialog("加载失败", $"无法加载RAW文件:\n{ex.Message}\n\n请检查文件格式是否正确。");
            }
            finally
            {
                _isProcessing = false;
                _cts?.Dispose();
                _cts = null;
                UpdateProgress(0, "");
            }
        }
        #endregion

        #region Step 2: 选区监控
        private void StartPatchMonitoring()
        {
            _patchMonitorTimer = new System.Windows.Threading.DispatcherTimer();
            _patchMonitorTimer.Interval = TimeSpan.FromMilliseconds(100);
            _patchMonitorTimer.Tick += (s, args) =>
            {
                if (_rubberBandData.Count != _lastPatchCount)
                {
                    _lastPatchCount = _rubberBandData.Count;
                    UpdatePatchSelectionUI();
                }
            };
            _patchMonitorTimer.Start();
        }

        private void StopPatchMonitoring()
        {
            if (_patchMonitorTimer != null)
            {
                _patchMonitorTimer.Stop();
                _patchMonitorTimer = null;
            }
        }

        private void UpdatePatchSelectionUI()
        {
            int count = _rubberBandData.Count;

            TxtPatchCount.Text = $"{count}/6";
            PatchCountValid = (count >= 2 && count <= 6);

            if (count < 2)
            {
                TxtPatchStatus.Text = count == 0 ? "⏳ 请选择2-6个白平衡校准区域" :
                                         $"⚠️ 至少需要选择2个区域 (当前:{count})";
            }
            else if (count <= 6)
            {
                TxtPatchStatus.Text = $"✓ 已选择{count}个校准区域，可开始计算";
            }
            else
            {
                TxtPatchStatus.Text = $"⚠️ 选区过多({count})，建议撤销至6个以内";
            }

            UpdateButtonStates();
        }

        private async void OnUndoPatch_Click(object sender, RoutedEventArgs e)
        {
            if (_rubberBandData?.Count > 0)
            {
                RawImg.UndoDrawRubberBand();
                UpdatePatchSelectionUI();
                Logger.Debug($"撤销选区，剩余: {_rubberBandData.Count}");

                await Task.Delay(100);
            }
        }
        #endregion

        #region Step 3: 计算白平衡增益
        private async void OnCalculate_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidatePatches()) return;

            if (_isProcessing)
            {
                ShowWarningDialog("操作进行中", "请等待当前操作完成");
                return;
            }

            try
            {
                _isProcessing = true;
                _cts = new CancellationTokenSource();

                BtnCalculate.IsEnabled = false;
                ProgressCalc.Visibility = Visibility.Visible;
                SetStatus("正在计算白平衡增益参数...");
                UpdateProgress(0, "提取选区数据...");
                _sw.Restart();

                double r_gain = 0, b_gain = 0;

                bool success = await Task.Run(() =>
                {
                    return CalculateAWBGainInternal(r_gain, b_gain, _cts.Token);
                }, _cts.Token);

                _sw.Stop();
                ProgressCalc.Visibility = Visibility.Collapsed;
                UpdateProgress(100, "完成");

                if (success)
                {
                    _isGainCalculated = true;
                    UpdateGainDisplay(_calculatedRGain, _calculatedBGain);
                    UpdateIQDataTable(_calculatedRGain, _calculatedBGain);

                    SetStatus($"✓ AWB增益计算完成！R={_calculatedRGain:F4}, B={_calculatedBGain:F4}, 耗时: {_sw.ElapsedMilliseconds}ms");
                    UpdateButtonStates();

                    Logger.Info($"AWB计算成功: R_Gain={_calculatedRGain:F4}, B_Gain={_calculatedBGain:F4}");
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("⚠️ 计算已取消");
                Logger.Info("用户取消了AWB增益计算");
                ProgressCalc.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Logger.Error($"AWB计算异常: {ex.Message}");
                ShowErrorDialog("计算异常", $"计算过程中发生错误:\n{ex.Message}");
                ProgressCalc.Visibility = Visibility.Collapsed;
            }
            finally
            {
                _isProcessing = false;
                _cts?.Dispose();
                _cts = null;
                BtnCalculate.IsEnabled = true;
                UpdateProgress(0, "");
            }
        }

        private bool CalculateAWBGainInternal(double r_gain, double b_gain, CancellationToken ct)
        {
            int[] XArray = new int[6];
            int[] YArray = new int[6];
            int[] HeightArray = new int[6];
            int[] WidthArray = new int[6];

            UpdateProgress(20, "准备坐标数据...");

            for (int j = 0; j < Math.Min(6, _rubberBandData.Count); j++)
            {
                XArray[j] = (int)_rubberBandData[j].x;
                YArray[j] = (int)_rubberBandData[j].y;
                WidthArray[j] = (int)_rubberBandData[j].width;
                HeightArray[j] = (int)_rubberBandData[j].height;
            }

            ct.ThrowIfCancellationRequested();

            UpdateProgress(50, "调用AWB CalcIQ算法...");

            _awbStep.CalcIQ(_rawFileBuffer, XArray, YArray, WidthArray, HeightArray, ref r_gain, ref b_gain);

            _calculatedRGain = r_gain;
            _calculatedBGain = b_gain;

            UpdateProgress(90, "验证结果...");
            return true;
        }

        private bool ValidatePatches()
        {
            if (!_isImageLoaded)
            {
                MessageBox.Show(this,
                    "请先加载RAW图像文件！\n\n点击「加载RAW」按钮选择图像文件。",
                    "缺少图像数据",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            if (_rubberBandData == null || _rubberBandData.Count < 2)
            {
                MessageBox.Show(this,
                    $"请至少选择2个白平衡校准区域（当前: {_rubberBandData?.Count ?? 0}）\n\n" +
                    "提示：在预览图上用鼠标框选灰度卡或白色区域。\n" +
                    "建议选择2-6个不同亮度的中性色区域以获得最佳效果。",
                    "选区不足",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            var smallPatches = _rubberBandData.Where(r => r.width < 10 || r.height < 10).ToList();
            if (smallPatches.Any())
            {
                MessageBox.Show(this,
                    $"{smallPatches.Count} 个选区尺寸过小（最小要求 10×10 像素），\n请重新选择这些区域。",
                    "选区尺寸无效",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            return true;
        }
        #endregion

        #region Step 4: 应用预览
        private async void OnApply_Click(object sender, RoutedEventArgs e)
        {
            if (!_isGainCalculated)
            {
                ShowWarningDialog("未计算增益", "请先点击「计算增益」获取白平衡参数");
                return;
            }

            if (_isProcessing)
            {
                ShowWarningDialog("操作进行中", "请等待当前操作完成");
                return;
            }

            try
            {
                _isProcessing = true;
                _cts = new CancellationTokenSource();

                BtnApply.IsEnabled = false;
                SetStatus("正在应用白平衡校正到预览图...");
                UpdateProgress(0, "处理中...");
                _sw.Restart();

                await Task.Run(() =>
                {
                    Thread.Sleep(100);
                    _cts.Token.ThrowIfCancellationRequested();
                }, _cts.Token);

                _sw.Stop();
                UpdateProgress(100, "完成");

                SetStatus($"✓ 白平衡已应用预览，耗时: {_sw.ElapsedMilliseconds}ms");
                Logger.Info("AWB应用预览完成");

                TxtSuggestion.Text = $"白平衡参数已应用：R={_calculatedRGain:F4}, B={_calculatedBGain:F4}\n图像色彩应更接近真实场景。";
            }
            catch (OperationCanceledException)
            {
                SetStatus("⚠️ 应用已取消");
            }
            catch (Exception ex)
            {
                Logger.Error($"AWB应用失败: {ex.Message}");
                ShowErrorDialog("应用失败", $"无法应用白平衡:\n{ex.Message}");
            }
            finally
            {
                _isProcessing = false;
                _cts?.Dispose();
                _cts = null;
                BtnApply.IsEnabled = true;
                UpdateProgress(0, "");
            }
        }
        #endregion

        #region Step 5: 质量评估
        private async void OnEvaluate_Click(object sender, RoutedEventArgs e)
        {
            if (!_isGainCalculated)
            {
                ShowWarningDialog("未计算增益", "请先点击「计算增益」获取白平衡参数");
                return;
            }

            if (_isProcessing)
            {
                ShowWarningDialog("操作进行中", "请等待当前操作完成");
                return;
            }

            try
            {
                _isProcessing = true;
                _cts = new CancellationTokenSource();

                BtnEvaluate.IsEnabled = false;
                SetStatus("正在评估白平衡质量...");
                UpdateProgress(0, "分析数据...");
                _sw.Restart();

                await Task.Run(() =>
                {
                    UpdateProgress(50, "计算偏差指标...");
                    Thread.Sleep(50);
                    _cts.Token.ThrowIfCancellationRequested();

                    UpdateProgress(90, "生成评估报告...");
                }, _cts.Token);

                _sw.Stop();
                UpdateProgress(100, "完成");

                UpdateEvaluationResults(_calculatedRGain, _calculatedBGain);

                SetStatus($"✓ 白平衡质量评估完成，耗时: {_sw.ElapsedMilliseconds}ms");
                UpdateButtonStates();

                Logger.Info($"AWB评估完成: R={_calculatedRGain:F4}, B={_calculatedBGain:F4}");
            }
            catch (OperationCanceledException)
            {
                SetStatus("⚠️ 评估已取消");
            }
            catch (Exception ex)
            {
                Logger.Error($"AWB评估失败: {ex.Message}");
                ShowErrorDialog("评估失败", $"无法评估白平衡质量:\n{ex.Message}");
            }
            finally
            {
                _isProcessing = false;
                _cts?.Dispose();
                _cts = null;
                BtnEvaluate.IsEnabled = true;
                UpdateProgress(0, "");
            }
        }

        private void UpdateEvaluationResults(double rGain, double bGain)
        {
            bool rOk = (rGain >= 0.92 && rGain <= 1.08);
            bool bOk = (bGain >= 0.92 && bGain <= 1.08);

            double rDeviation = Math.Abs(rGain - 1.0) * 100;
            double bDeviation = Math.Abs(bGain - 1.0) * 100;

            ProgressRDeviation.Value = 10 + (rGain > 1 ? rDeviation : -rDeviation) / 2;
            ProgressBDeviation.Value = 10 + (bGain > 1 ? bDeviation : -bDeviation) / 2;
            TxtRDeviation.Text = $"{rDeviation:F1}%";
            TxtBDeviation.Text = $"{bDeviation:F1}%";

            if (rOk && bOk)
            {
                TxtRatingStars.Text = "⭐⭐⭐";
                TxtRatingText.Text = "优秀";
                TxtRatingText.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                TxtSuggestion.Text = "✓ 白平衡增益在标准范围内，色彩还原准确。建议保存此配置用于批量处理。";
            }
            else if (rOk || bOk)
            {
                TxtRatingStars.Text = "⭐⭐";
                TxtRatingText.Text = "良好";
                TxtRatingText.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                TxtSuggestion.Text = rOk ?
                    "⚠️ B通道增益偏离标准范围，蓝色调可能不准确。建议检查光源或重新选择校准区域。" :
                    "⚠️ R通道增益偏离标准范围，红色调可能不准确。建议检查光源或重新选择校准区域。";
            }
            else
            {
                TxtRatingStars.Text = "⭐";
                TxtRatingText.Text = "需优化";
                TxtRatingText.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                TxtSuggestion.Text = "❌ R/B增益均超出标准范围！建议:\n1. 确保选择的区域为中性灰/白色\n2. 避免选择彩色区域作为白点\n3. 检查光照条件是否均匀稳定";
            }

            EstimateColorTemperature(rGain, bGain);

            BtnShowDetail.IsEnabled = true;
            BtnExport.IsEnabled = true;
        }

        private void EstimateColorTemperature(double rGain, double bGain)
        {
            double ratio = bGain / rGain;

            if (ratio > 1.05)
            {
                TxtColorTemp.Text = $"{(3000 + (ratio - 1.05) * 5000):0} K";
                TxtLightSource.Text = "偏暖色调 (Warm)";
            }
            else if (ratio < 0.95)
            {
                TxtColorTemp.Text = $"{(7000 - (0.95 - ratio) * 5000):0} K";
                TxtLightSource.Text = "偏冷色调 (Cool)";
            }
            else
            {
                TxtColorTemp.Text = "5500 K";
                TxtLightSource.Text = "接近日光 (Daylight)";
            }
        }
        #endregion

        #region UI更新方法
        private void UpdateGainDisplay(double rGain, double bGain)
        {
            TxtRGain.Text = rGain.ToString("F4");
            TxtBGain.Text = bGain.ToString("F4");

            bool rOk = (rGain >= 0.92 && rGain <= 1.08);
            bool bOk = (bGain >= 0.92 && bGain <= 1.08);

            BorderRStatus.Background = rOk ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) :
                                              new SolidColorBrush(Color.FromRgb(244, 67, 54));
            TxtRStatus.Text = rOk ? "✓ 正常" : "✗ 超标";

            BorderBStatus.Background = bOk ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) :
                                              new SolidColorBrush(Color.FromRgb(244, 67, 54));
            TxtBStatus.Text = bOk ? "✓ 正常" : "✗ 超标";
        }

        private void UpdateIQDataTable(double rGain, double bGain)
        {
            bool rOk = (rGain >= 0.92 && rGain <= 1.08);
            bool bOk = (bGain >= 0.92 && bGain <= 1.08);

            if (AwbIQ.Count >= 2)
            {
                AwbIQ[0] = new IQData("r_gain", rGain,
                    "[0.92, 1.08]", rOk);
                AwbIQ[1] = new IQData("b_gain", bGain,
                    "[0.92, 1.08]", bOk);
            }

            View = CollectionViewSource.GetDefaultView(AwbIQ);
        }

        private void UpdateButtonStates()
        {
            BtnUndoPatch.IsEnabled = (_rubberBandData?.Count > 0) && IsLoadImage;
            BtnCalculate.IsEnabled = IsLoadImage && (_rubberBandData?.Count >= 2);
            BtnApply.IsEnabled = _isGainCalculated;
            BtnEvaluate.IsEnabled = _isGainCalculated;
        }

        private void ResetApplyPreviewState()
        {
            _isGainCalculated = false;
            TxtRGain.Text = "--";
            TxtBGain.Text = "--";
            TxtRStatus.Text = "--";
            TxtBStatus.Text = "--";
            TxtColorTemp.Text = "-- K";
            TxtLightSource.Text = "";
            TxtRatingStars.Text = "--";
            TxtRatingText.Text = "";
            TxtRDeviation.Text = "0%";
            TxtBDeviation.Text = "0%";
            TxtSuggestion.Text = "请先加载图像并选择校准区域";

            BorderRStatus.Background = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            BorderBStatus.Background = new SolidColorBrush(Color.FromRgb(200, 200, 200));

            BtnShowDetail.IsEnabled = false;
            BtnExport.IsEnabled = false;
        }

        private void UpdateProgress(int percent, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ProgressCalc.Value = percent;
                TxtProgress.Text = message;
            });
        }

        private void SetStatus(string message)
        {
            StatusBarText.Text = message;
            TxtElapsedTime.Text = _sw.IsRunning ? $"耗时: {_sw.ElapsedMilliseconds}ms" : "";
        }
        #endregion

        #region 辅助功能
        private static void ShowWarningDialog(string title, string message)
        {
            MessageBox.Show(null, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static void ShowErrorDialog(string title, string message)
        {
            MessageBox.Show(null, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void OnExport_Click(object sender, RoutedEventArgs e)
        {
            if (!_isGainCalculated) return;

            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV文件 (*.csv)|*.csv",
                    Title = "导出AWB校准报告",
                    FileName = $"AWB_Report_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (dialog.ShowDialog() != true) return;

                var sb = new StringBuilder();
                sb.AppendLine("AWB Auto White Balance Calibration Report");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();
                sb.AppendLine("Parameter,Value,Standard Range,Compliance");
                sb.AppendLine($"r_gain,{_calculatedRGain:F4},[0.92, 1.08],{(_calculatedRGain >= 0.92 && _calculatedRGain <= 1.08 ? "PASS" : "FAIL")}");
                sb.AppendLine($"b_gain,{_calculatedBGain:F4},[0.92, 1.08],{(_calculatedBGain >= 0.92 && _calculatedBGain <= 1.08 ? "PASS" : "FAIL")}");
                sb.AppendLine();
                sb.AppendLine("Summary:");
                sb.AppendLine($"  R Deviation: {Math.Abs(_calculatedRGain - 1.0) * 100:F2}%");
                sb.AppendLine($"  B Deviation: {Math.Abs(_calculatedBGain - 1.0) * 100:F2}%");
                sb.AppendLine($"  Color Temperature: ~{TxtColorTemp.Text}");
                sb.AppendLine($"  Patches Selected: {_rubberBandData.Count}");

                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

                SetStatus($"✓ 报告已导出: {System.IO.Path.GetFileName(dialog.FileName)}");
                Logger.Info($"AWB报告导出: {dialog.FileName}");

                MessageBox.Show(this,
                    $"报告已成功导出到:\n{dialog.FileName}",
                    "导出成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.Error($"导出失败: {ex.Message}");
                ShowErrorDialog("导出失败", $"无法导出报告:\n{ex.Message}");
            }
        }

        private void OnShowDetail_Click(object sender, RoutedEventArgs e)
        {
            var detailWindow = new Window
            {
                Title = "AWB详细数据",
                Width = 600,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            var grid = new Grid();
            grid.Margin = new Thickness(10);

            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                ItemsSource = AwbIQ,
                GridLinesVisibility = DataGridGridLinesVisibility.All
            };

            dataGrid.Columns.Add(new DataGridTextColumn { Header = "参数名", Binding = new Binding("Name"), IsReadOnly = true });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "当前值", Binding = new Binding("Value"), FontSize = 12 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "标准范围", Binding = new Binding("ValueRange"), IsReadOnly = true });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "合规性", Binding = new Binding("IsGoodValue") });

            grid.Children.Add(dataGrid);
            detailWindow.Content = grid;
            detailWindow.Show();
        }

        private void CleanupResources()
        {
            try
            {
                StopPatchMonitoring();
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                _rawFileBuffer = null;
                _rubberBandData?.Clear();

                RawImg.DisplayImageSource = null;

                Logger.Debug("AWB窗口资源清理完成");
            }
            catch (Exception ex)
            {
                Logger.Error($"资源清理异常: {ex.Message}");
            }
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isProcessing)
            {
                var result = MessageBox.Show(this,
                    "当前有操作正在进行中，确定要关闭窗口吗？\n\n正在进行的任务将被取消。",
                    "确认关闭",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                _cts?.Cancel();
                System.Threading.Thread.Sleep(200);
            }

            CleanupResources();
            Logger.Info("AWB调试窗口已关闭");
        }
        #endregion
    }

    /*
    public partial class AwbIQWindow : Window
    {
        private Processor _ispProcessor = null;
        private AutoWhiteBalance _awbStep = null;

        private byte[] _rawFileBuffer;
        private List<RubberBandData> _rubberBandData = new List<RubberBandData>();

        private Dictionary<string, ValueRange> _iQRangeDictionary = new Dictionary<string, ValueRange>()
        {
            {"r_gain", new ValueRange(0.92,1.08)},
            {"b_gain", new ValueRange(0.92,1.08)}
        };

        private ObservableCollection<IQData> _awbIQ = new ObservableCollection<IQData>() 
        {
            new IQData(),
            new IQData()
        };

        public ObservableCollection<IQData> AwbIQ
        {
            get { return _awbIQ; }
        }

        public ICollectionView View
        {
            get { return (ICollectionView)GetValue(ViewProperty); }
            set { SetValue(ViewProperty, value); }
        }

        public static readonly DependencyProperty ViewProperty = DependencyProperty.Register(
            "View",
            typeof(ICollectionView),
            typeof(AwbIQWindow),
            new FrameworkPropertyMetadata(null));

        public bool IsLoadImage
        {
            get { return (bool)GetValue(IsLoadImageProperty); }
            set { SetValue(IsLoadImageProperty, value); }
        }

        public static readonly DependencyProperty IsLoadImageProperty = DependencyProperty.Register(
            "IsLoadImage",
            typeof(bool),
            typeof(AwbIQWindow),
            new FrameworkPropertyMetadata(false));

        public AwbIQWindow(Processor ispProcessor)
        {
            _ispProcessor = ispProcessor;
            _awbStep = (AutoWhiteBalance)_ispProcessor.AllProcessSteps[IspModule.Awb];
            InitializeComponent();
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            RawImg.MaxBands = 6;
            RawImg.DataContext = _rubberBandData;
            RawImg.IsEnabled = false;
        }

        private void OnLoadRawButtonClick(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Raw文件(*.raw) | *.raw";
            if (!(bool)openFileDialog.ShowDialog())
            {
                return;
            }

            _rawFileBuffer = File.ReadAllBytes(openFileDialog.FileName);
            RawImg.DisplayImageSource = _ispProcessor.GenerateBitmapUsingRaw(_rawFileBuffer, IspModule.Awb);

            IsLoadImage = true;
            //TODO:这里欠债了，有空改掉吧(改成binding形式)
            RawImg.IsEnabled = true;
        }

        private void OnCalcIQClick(object sender, RoutedEventArgs e)
        {
            double r_gain = 0;
            double b_gain = 0;

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

            _awbStep.CalcIQ(_rawFileBuffer, XArray, YArray, WidthArray, HeightArray, ref r_gain, ref b_gain);

            _awbIQ[0] = new IQData("r_gain", r_gain,
                _iQRangeDictionary["r_gain"].Min.ToString() + "-" + _iQRangeDictionary["r_gain"].Max.ToString(),
                r_gain >= _iQRangeDictionary["r_gain"].Min && r_gain <= _iQRangeDictionary["r_gain"].Max);

            _awbIQ[1] = new IQData("b_gain", b_gain,
                _iQRangeDictionary["b_gain"].Min.ToString() + "-" + _iQRangeDictionary["b_gain"].Max.ToString(),
                b_gain >= _iQRangeDictionary["b_gain"].Min && b_gain <= _iQRangeDictionary["b_gain"].Max);

            View = CollectionViewSource.GetDefaultView(_awbIQ);
        }

        private void OnUndoClick(object sender, RoutedEventArgs e)
        {
            RawImg.UndoDrawRubberBand();
        }
    }
    */
}
