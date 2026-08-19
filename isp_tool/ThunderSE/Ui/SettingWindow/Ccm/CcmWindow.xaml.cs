using GalaSoft.MvvmLight.Command;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThunderSE.Common;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Ui.CommonCustomControl;


namespace ThunderSE.Ui.SettingWindow.Ccm
{
    public class CcmIQResult : INotifyPropertyChanged
    {
        private int _index;
        private double _rAvg;
        private double _gAvg;
        private double _bAvg;
        private double _deltaE;

        public int Index
        {
            get => _index;
            set { _index = value; OnPropertyChanged(nameof(Index)); }
        }

        public double RAvg
        {
            get => _rAvg;
            set { _rAvg = value; OnPropertyChanged(nameof(RAvg)); }
        }

        public double GAvg
        {
            get => _gAvg;
            set { _gAvg = value; OnPropertyChanged(nameof(GAvg)); }
        }

        public double BAvg
        {
            get => _bAvg;
            set { _bAvg = value; OnPropertyChanged(nameof(BAvg)); }
        }

        public double DeltaE
        {
            get => _deltaE;
            set { _deltaE = value; OnPropertyChanged(nameof(DeltaE)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class MatrixRow : INotifyPropertyChanged
    {
        private string _rowLabel;
        private int _col0;
        private int _col1;
        private int _col2;

        public string RowLabel
        {
            get => _rowLabel;
            set { _rowLabel = value; OnPropertyChanged(nameof(RowLabel)); }
        }

        public int Col0
        {
            get => _col0;
            set { _col0 = value; OnPropertyChanged(nameof(Col0)); }
        }

        public int Col1
        {
            get => _col1;
            set { _col1 = value; OnPropertyChanged(nameof(Col1)); }
        }

        public int Col2
        {
            get => _col2;
            set { _col2 = value; OnPropertyChanged(nameof(Col2)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// CCM色彩校正调试窗口 - 完整的5步工作流：加载→选区→计算→应用→评估
    /// </summary>
    public partial class CcmOnlineIQWindow : Window
    {
        #region 私有字段
        private Processor _processor;
        private CCM _ccmStep;
        private byte[] _rawFileBuffer;
        private ObservableCollection<RubberBandData> _colorPatchRegions = new ObservableCollection<RubberBandData>();
        private int[,] _calculatedMatrix;
        private bool _isMatrixCalculated = false;
        private bool _isImageLoaded = false;
        private bool _showingCorrectedImage = false;
        private IntPtr[] _originalImgPtrs = new IntPtr[3];
        private IntPtr[] _correctedImgPtrs = new IntPtr[3];
        private IntPtr[] _demosaicImgPtrs = new IntPtr[3];  // Demosaic后的3通道数据
        private int _imageWidth, _imageHeight;
        private Stopwatch _sw = new Stopwatch();
        private CancellationTokenSource _cts;
        private bool _isProcessing = false;
        private System.Windows.Threading.DispatcherTimer _patchMonitorTimer;
        private int _lastPatchCount = -1;

        // 新增：自动识别相关字段
        private bool _isAutoDetectMode = false;
        #endregion

        #region 依赖属性（用于UI数据绑定）
        public static readonly DependencyProperty IsLoadImageProperty =
            DependencyProperty.Register("IsLoadImage", typeof(bool), typeof(CcmOnlineIQWindow), new PropertyMetadata(false));

        public static readonly DependencyProperty PatchCountValidProperty =
            DependencyProperty.Register("PatchCountValid", typeof(bool), typeof(CcmOnlineIQWindow), new PropertyMetadata(false));

        public bool IsLoadImage
        {
            get => (bool)GetValue(IsLoadImageProperty);
            set => SetValue(IsLoadImageProperty, value);
        }

        public bool PatchCountValid
        {
            get => (bool)GetValue(PatchCountValidProperty);
            set => SetValue(PatchCountValidProperty, value);
        }
        #endregion

        #region 构造函数
        public CcmOnlineIQWindow(Processor processor)
        {
            InitializeComponent();

            if (processor == null)
                throw new ArgumentNullException(nameof(processor));

            _processor = processor;
            _ccmStep = (CCM)_processor.AllProcessSteps[IspModule.Ccm];

            InitializeDataGrids();
            InitializeRubberBandControl();
            //StartPatchMonitoring();
            UpdateButtonStates();
            SetupKeyboardShortcuts();
            SetupWindowEvents();

            Logger.Debug("CCM调试窗口初始化完成");
        }
        #endregion

        #region 初始化方法
        private void InitializeDataGrids()
        {
            var matrixData = new ObservableCollection<MatrixRow>();

            // 确保_ccmStep不为空并且ccm数组长度正确
            if (_ccmStep != null && _ccmStep.ccm != null && _ccmStep.ccm.Length >= 9)
            {
                // 将一维数组转换为3x3矩阵形式显示
                string[] labels = { "R'=", "G'=", "B'=" };

                for (int i = 0; i < 3; i++)
                {
                    matrixData.Add(new MatrixRow
                    {
                        RowLabel = labels[i],
                        Col0 = _ccmStep.ccm[i * 3 + 0],  // 第0, 3, 6个元素
                        Col1 = _ccmStep.ccm[i * 3 + 1],  // 第1, 4, 7个元素
                        Col2 = _ccmStep.ccm[i * 3 + 2]   // 第2, 5, 8个元素
                    });
                }

                // 同时设置偏移量文本框
                TxtOffsetR.Text = _ccmStep.s41.ToString();
                TxtOffsetG.Text = _ccmStep.s42.ToString();
                TxtOffsetB.Text = _ccmStep.s43.ToString();
            }
            else
            {
                // 如果没有有效的CCM数据，则使用默认值
                matrixData = new ObservableCollection<MatrixRow>()
                {
                    new MatrixRow { RowLabel="R'=", Col0=256, Col1=0, Col2=0 },
                    new MatrixRow { RowLabel="G'=", Col0=0, Col1=256, Col2=0 },
                    new MatrixRow { RowLabel="B'=", Col0=0, Col1=0, Col2=256 }
                };
            }
            MatrixDataGrid.ItemsSource = matrixData;

            ColorPatchDataGrid.ItemsSource = new ObservableCollection<CcmIQResult>();
            for (int i = 0; i < 24; i++)
            {
                ((ObservableCollection<CcmIQResult>)ColorPatchDataGrid.ItemsSource).Add(
                    new CcmIQResult { Index = i + 1, RAvg = 0, GAvg = 0, BAvg = 0, DeltaE = 0 });
            }
        }

        private void InitializeRubberBandControl()
        {
            RubberBandCtrl.DataContext = _colorPatchRegions;
            RubberBandCtrl.CurrentCategory = "非白点";
            RubberBandCtrl.SetMaxBandsForCategory("非白点", 24);

            // 监听集合变化，替代 Timer 轮询
            _colorPatchRegions.CollectionChanged += (s, e) =>
            {
                UpdatePatchSelectionUI();
                UpdateButtonStates();
            };
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
            this.StateChanged += OnWindowStateChanged;
            this.SizeChanged += OnWindowSizeChanged;
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
            Logger.Info("CCM调试窗口已关闭");
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                Logger.Debug("CCM窗口最大化");
            }
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isImageLoaded)
            {
                Logger.Debug($"CCM窗口尺寸变更: {e.NewSize.Width}×{e.NewSize.Height}");
            }
        }

        private void CleanupResources()
        {
            try
            {
                //StopPatchMonitoring();
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                for (int i = 0; i < 3; i++)
                {
                    if (_originalImgPtrs[i] != IntPtr.Zero)
                        Marshal.FreeHGlobal(_originalImgPtrs[i]);
                    if (_correctedImgPtrs[i] != IntPtr.Zero)
                        Marshal.FreeHGlobal(_correctedImgPtrs[i]);
                    _originalImgPtrs[i] = IntPtr.Zero;
                    _correctedImgPtrs[i] = IntPtr.Zero;
                }

                FreeDemosaicBuffers();
                FreeOriginalImageBuffers();

                _rawFileBuffer = null;
                _colorPatchRegions?.Clear();
                _calculatedMatrix = null;
                _isMatrixCalculated = false;
                _showingCorrectedImage = false;

                RubberBandCtrl.DisplayImageSource = null;

                Logger.Debug("CCM窗口资源清理完成");
            }
            catch (Exception ex)
            {
                Logger.Error($"资源清理异常: {ex.Message}");
            }
        }

        private void UpdateButtonStates()
        {
            BtnUndoPatch.IsEnabled = (_colorPatchRegions?.Count > 0) && IsLoadImage;
            BtnAutoDetect.IsEnabled = IsLoadImage; // 新增：加载图像后即可使用
            BtnCalculate.IsEnabled = IsLoadImage && (_colorPatchRegions?.Count == 24);
            BtnApply.IsEnabled = _isMatrixCalculated;
            BtnEvaluate.IsEnabled = IsLoadImage || _isMatrixCalculated;
            BtnExport.IsEnabled = TxtDeltaE.Text != "--.--";
        }
        #endregion

        #region Demosaic处理
        private unsafe void PerformDemosaic()
        {
            int totalPixels = _imageWidth * _imageHeight;

            FreeDemosaicBuffers();
            FreeOriginalImageBuffers();

            for (int i = 0; i < 3; i++)
            {
                _demosaicImgPtrs[i] = Marshal.AllocHGlobal(totalPixels * sizeof(short));
                _originalImgPtrs[i] = Marshal.AllocHGlobal(totalPixels * sizeof(short));
            }

            try
            {
                IspApi.DemosaicImg(
                    _rawFileBuffer,
                    (int)_processor.IspCommonConfig.Bayer,
                    _imageWidth, _imageHeight,
                    _demosaicImgPtrs);

                Logger.Debug($"Demosaic完成: {_imageWidth}×{_imageHeight}, 3通道");

                int byteCount = totalPixels * sizeof(short);
                for (int ch = 0; ch < 3; ch++)
                {
                    //Marshal.Copy(_demosaicImgPtrs[ch], new byte[byteCount], 0, _originalImgPtrs[ch], 0, byteCount);
                    //CopyMemory(_originalImgPtrs[ch], _demosaicImgPtrs[ch], byteCount);

                    //// Step 1: 非托管内存 → 托管byte数组 (使用重载#1)
                    //byte[] tempBuffer = new byte[byteCount];
                    //Marshal.Copy(_demosaicImgPtrs[ch], tempBuffer, 0, byteCount);

                    //// Step 2: 托管byte数组 → 非托管内存 (使用重载#2)
                    //Marshal.Copy(tempBuffer, 0, _originalImgPtrs[ch], byteCount);

                    // 直接内存拷贝，避免托管数组分配
                    Buffer.MemoryCopy(
                        _demosaicImgPtrs[ch].ToPointer(),
                        _originalImgPtrs[ch].ToPointer(),
                        byteCount,
                        byteCount
                    );
                }

                Logger.Debug("原始图像缓冲区已初始化（从Demosaic数据复制）");
            }
            catch (Exception ex)
            {
                Logger.Error($"Demosaic失败: {ex.Message}");
                FreeDemosaicBuffers();
                FreeOriginalImageBuffers();
                throw;
            }
        }

        private void FreeDemosaicBuffers()
        {
            for (int i = 0; i < 3; i++)
            {
                if (_demosaicImgPtrs[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_demosaicImgPtrs[i]);
                    _demosaicImgPtrs[i] = IntPtr.Zero;
                }
            }
        }

        private void FreeOriginalImageBuffers()
        {
            for (int i = 0; i < 3; i++)
            {
                if (_originalImgPtrs[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_originalImgPtrs[i]);
                    _originalImgPtrs[i] = IntPtr.Zero;
                }
            }
        }

        private bool IsOriginalImageReady()
        {
            return _originalImgPtrs.All(p => p != IntPtr.Zero);
        }

        private bool IsCorrectedImageReady()
        {
            return _correctedImgPtrs.All(p => p != IntPtr.Zero);
        }

        private short ReadDemosaicPixel(int channel, int x, int y)
        {
            if (_demosaicImgPtrs[channel] == IntPtr.Zero)
                return 0;

            int idx = y * _imageWidth + x;
            if (idx < 0 || idx >= _imageWidth * _imageHeight)
                return 0;

            return Marshal.ReadInt16(_demosaicImgPtrs[channel], idx * 2);
        }

        private void ResetApplyPreviewState()
        {
            _showingCorrectedImage = false;
            FreeCorrectedBuffers();

            Application.Current.Dispatcher.Invoke(() =>
            {
                BtnApply.Content = "👁️ 应用预览";
            });

            Logger.Debug("应用预览状态已重置");
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
                    var bitmap = _processor.GenerateBitmapUsingRaw(
                        _rawFileBuffer, IspModule.Ccm, false);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        RubberBandCtrl.DisplayImageSource = bitmap;
                    });

                    var config = _processor.IspCommonConfig;
                    _imageWidth = config.ResolutionWidth;
                    _imageHeight = config.ResolutionHeight;

                    UpdateProgress(50, "Demosaic去马赛克...");
                    PerformDemosaic();  // 新增：生成3通道数据供色卡提取使用

                    UpdateProgress(70, "渲染预览图...");
                }, _cts.Token);

                _cts.Token.ThrowIfCancellationRequested();
                UpdateProgress(100, "完成");

                _sw.Stop();
                _isImageLoaded = true;
                IsLoadImage = true;

                ResetApplyPreviewState();

                SetStatus($"✓ RAW加载完成 ({_rawFileBuffer.Length:N0} 字节, {_imageWidth}×{_imageHeight}, 耗时: {_sw.ElapsedMilliseconds}ms)");
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

        #region Step 2: 色卡选区管理
        private void OnUndoPatch_Click(object sender, RoutedEventArgs e)
        {
            if (_colorPatchRegions?.Count > 0)
            {
                RubberBandCtrl.UndoDrawRubberBand();
                UpdatePatchSelectionUI();
                Logger.Debug($"撤销选区，剩余: {_colorPatchRegions.Count}");
            }
        }
        /// <summary>
        /// 进入自动识别模式
        /// </summary>
        private void OnAutoDetect_Click(object sender, RoutedEventArgs e)
        {
            if (!_isImageLoaded)
            {
                MessageBox.Show(this, "请先加载图像！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isAutoDetectMode = true;
            SetStatus("请在预览图上框选整个24色卡区域...");

            // 暂时禁用普通选区添加逻辑，可以通过修改 RubberBandCtrl 的行为或在此处处理
            // 由于 RubberBandCtrl 是自定义控件，我们假设它会在绘制完成后触发 DataContext 的变化
            // 或者我们可以订阅它的某个事件。这里采用一种更通用的方式：
            // 监听 _colorPatchRegions 的变化，如果是在自动模式下且新增了选区，则将其视为“种子”

            // 如果是自动识别模式，且刚刚产生了一个选区（即种子）
            if (_isAutoDetectMode && _colorPatchRegions.Count == 1)
            {
                var seed = _colorPatchRegions[0];
                // 验证种子大小，避免误触
                if (seed.width > 100 && seed.height > 100)
                {
                    GenerateGridFromSeed(seed);
                    return; // 直接返回，不再执行下面的常规更新
                }
                else
                {
                    // 如果太小，取消自动模式并提示
                    _isAutoDetectMode = false;
                    _colorPatchRegions.Clear();
                    SetStatus("选区过小，已取消自动识别");
                    return;
                }
            }
        }

        private void StartPatchMonitoring()
        {
            _patchMonitorTimer = new System.Windows.Threading.DispatcherTimer();
            _patchMonitorTimer.Interval = TimeSpan.FromMilliseconds(100);
            _patchMonitorTimer.Tick += (s, args) =>
            {
                if (_colorPatchRegions.Count != _lastPatchCount)
                {
                    _lastPatchCount = _colorPatchRegions.Count;
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
            int count = _colorPatchRegions.Count;

            TxtPatchCount.Text = $"{count}/24";
            PatchCountValid = (count == 24);

            if (count < 24)
            {
                TxtPatchStatus.Text = count == 0 ? "⏳ 请选择24个色卡区域" :
                                         $"⚠️ 还需选择 {24 - count} 个色块";
            }
            else if (count == 24)
            {
                TxtPatchStatus.Text = "✓ 已选满24个色块，可开始计算";
            }
            else
            {
                TxtPatchStatus.Text = $"⚠️ 选区过多({count})，请撤销多余选区";
            }

            UpdateButtonStates();
        }

        /// <summary>
        /// 根据种子矩形生成 4x6 网格选区
        /// </summary>
        private void GenerateGridFromSeed(RubberBandData seed)
        {
            _colorPatchRegions.Clear(); // 清除之前的选区

            double totalX = seed.x;
            double totalY = seed.y;
            double totalW = seed.width;
            double totalH = seed.height;

            double cellW = totalW / 6.0;
            double cellH = totalH / 4.0;

            // 参考 ROI.m: w = deltaX * 0.6, h = deltaY * 0.6
            double patchW = cellW * 0.6;
            double patchH = cellH * 0.6;

            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 6; col++)
                {
                    // 计算每个色块的中心位置
                    double centerX = totalX + cellW * col + cellW / 2.0;
                    double centerY = totalY + cellH * row + cellH / 2.0;

                    // 计算左上角坐标
                    double patchX = centerX - patchW / 2.0;
                    double patchY = centerY - patchH / 2.0;

                    // 边界检查
                    if (patchX < 0) patchX = 0;
                    if (patchY < 0) patchY = 0;
                    if (patchX + patchW > _imageWidth) patchW = _imageWidth - patchX;
                    if (patchY + patchH > _imageHeight) patchH = _imageHeight - patchY;

                    var patch = new RubberBandData
                    {
                        x = (int)patchX,
                        y = (int)patchY,
                        width = (int)patchW,
                        height = (int)patchH
                    };

                    _colorPatchRegions.Add(patch);
                }
            }

            _isAutoDetectMode = false;
            SetStatus($"已自动生成 {_colorPatchRegions.Count} 个色块选区");
            UpdatePatchSelectionUI();
        }

        #endregion

        #region Step 3: 计算CCM矩阵
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
                SetStatus("正在从24色卡区域提取RGB数据...");
                UpdateProgress(0, "提取色卡数据...");
                _sw.Restart();

                bool success = await Task.Run(() =>
                {
                    return CalculateCCMMatrixInternal(_cts.Token);
                }, _cts.Token);

                _sw.Stop();
                ProgressCalc.Visibility = Visibility.Collapsed;
                UpdateProgress(100, "完成");

                if (success)
                {
                    _isMatrixCalculated = true;
                    UpdateMatrixDisplay(_calculatedMatrix);

                    SetStatus($"✓ CCM矩阵计算完成！耗时: {_sw.ElapsedMilliseconds}ms");

                    UpdateColorPatchDataGrid();
                    UpdateButtonStates();

                    Logger.Info($"CCM矩阵计算成功，耗时: {_sw.ElapsedMilliseconds}ms");
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("⚠️ 计算已取消");
                Logger.Info("用户取消了CCM矩阵计算");
                ProgressCalc.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Logger.Error($"CCM计算异常: {ex.Message}");
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

            if (_colorPatchRegions == null || _colorPatchRegions.Count != 24)
            {
                MessageBox.Show(this,
                    $"请选择完整的24个色卡区域（当前: {_colorPatchRegions?.Count ?? 0}/24）\n\n" +
                    "提示：在预览图上用鼠标框选每个色块的中心区域。\n" +
                    "建议从左到右、从上到下依次选择24个色块。",
                    "选区不完整",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            var smallPatches = _colorPatchRegions.Where(r => r.width < 10 || r.height < 10).ToList();
            if (smallPatches.Any())
            {
                MessageBox.Show(this,
                    $"{smallPatches.Count} 个选区尺寸过小（最小要求 10×10 像素），\n请重新选择这些色块。\n\n" +
                    "提示：框选时应包含色块的完整区域，避免只选择边缘。",
                    "选区尺寸无效",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            var outOfBounds = _colorPatchRegions.Where(r =>
                r.x < 0 || r.y < 0 ||
                r.x + r.width > _imageWidth ||
                r.y + r.height > _imageHeight).ToList();

            if (outOfBounds.Any())
            {
                MessageBox.Show(this,
                    $"{outOfBounds.Count} 个选区超出图像边界！\n图像尺寸: {_imageWidth}×{_imageHeight}\n\n" +
                    "请重新选择这些越界的色块。",
                    "选区超出边界",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            var overlapping = CheckOverlappingPatches();
            if (overlapping > 0)
            {
                var result = MessageBox.Show(this,
                    $"检测到 {overlapping} 对重叠选区。\n\n" +
                    "重叠的选区可能导致计算结果不准确。\n是否继续？",
                    "选区重叠警告",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                    return false;
            }

            return true;
        }

        private int CheckOverlappingPatches()
        {
            int overlapCount = 0;

            for (int i = 0; i < _colorPatchRegions.Count; i++)
            {
                for (int j = i + 1; j < _colorPatchRegions.Count; j++)
                {
                    var r1 = _colorPatchRegions[i];
                    var r2 = _colorPatchRegions[j];

                    bool overlaps = !(r1.x + r1.width < r2.x ||
                                      r2.x + r2.width < r1.x ||
                                      r1.y + r1.height < r2.y ||
                                      r2.y + r2.height < r1.y);

                    if (overlaps) overlapCount++;
                }
            }

            return overlapCount;
        }

        private bool CalculateCCMMatrixInternal(CancellationToken ct)
        {
            int[] crAvg = new int[24], cgAvg = new int[24], cbAvg = new int[24];

            UpdateProgress(20, "提取RGB均值...");
            ExtractColorPatchAverages(crAvg, cgAvg, cbAvg);
            ct.ThrowIfCancellationRequested();

            _calculatedMatrix = new int[3, 3];
            int[] calculatedOffsets = new int[3];

            UpdateProgress(50, "调用CCM_Cal算法...");
            IntPtr rawPtr = Marshal.AllocHGlobal(_rawFileBuffer.Length);
            try
            {
                Marshal.Copy(_rawFileBuffer, 0, rawPtr, _rawFileBuffer.Length);

                int result = IspApi.CCM_New_Cal(
                    rawPtr,
                    _imageWidth, _imageHeight,
                    crAvg, cgAvg, cbAvg,
                    20.0f, 10.0f,
                    6, 2,
                    1,
                    _calculatedMatrix,
                    null//calculatedOffsets
                );
                //int result = IspApi.CCM_Cal(
                //    crAvg, cgAvg, cbAvg,
                //    20.0f, 10.0f,
                //    6, 2,
                //    _calculatedMatrix,
                //    1
                //);

                if (result != CcmErrorCode.CCM_SUCCESS)
                {
                    throw new InvalidOperationException($"CCM_Cal返回错误码: {result} ({GetErrorMessage(result)})");
                }

                UpdateProgress(90, "验证结果...");

                //_ccmStep.s41 = (short)calculatedOffsets[0];
                //_ccmStep.s42 = (short)calculatedOffsets[1];
                //_ccmStep.s43 = (short)calculatedOffsets[2];

                Logger.Info($"CCM偏移量计算完成: R={calculatedOffsets[0]}, G={calculatedOffsets[1]}, B={calculatedOffsets[2]}");

                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(rawPtr);
            }
        }

        private void ExtractColorPatchAverages(int[] rAvg, int[] gAvg, int[] bAvg)
        {
            for (int i = 0; i < Math.Min(24, _colorPatchRegions.Count); i++)
            {
                var region = _colorPatchRegions[i];
                int x = (int)Math.Max(0, region.x);
                int y = (int)Math.Max(0, region.y);
                int w = (int)Math.Min(region.width, _imageWidth - x);
                int h = (int)Math.Min(region.height, _imageHeight - y);

                long sumR = 0, sumG = 0, sumB = 0;
                int count = 0;

                for (int row = y; row < y + h; row++)
                {
                    for (int col = x; col < x + w; col++)
                    {
                        sumR += ReadDemosaicPixel(0, col, row);  // 通道0 = R
                        sumG += ReadDemosaicPixel(1, col, row);  // 通道1 = G
                        sumB += ReadDemosaicPixel(2, col, row);  // 通道2 = B
                        count++;
                    }
                }

                if (count > 0)
                {
                    rAvg[i] = (int)(sumR / count);
                    gAvg[i] = (int)(sumG / count);
                    bAvg[i] = (int)(sumB / count);
                }
            }

            Logger.Debug("色卡RGB均值提取完成（从Demosaic数据）");
        }

        private void UpdateMatrixDisplay(int[,] matrix)
        {
            var items = (ObservableCollection<MatrixRow>)MatrixDataGrid.ItemsSource;
            items.Clear();
            string[] labels = { "R'=", "G'=", "B'=" };

            for (int i = 0; i < 3; i++)
            {
                items.Add(new MatrixRow
                {
                    RowLabel = labels[i],
                    Col0 = matrix[i, 0],
                    Col1 = matrix[i, 1],
                    Col2 = matrix[i, 2]
                });
            }

            TxtOffsetR.Text = _ccmStep.s41.ToString();
            TxtOffsetG.Text = _ccmStep.s42.ToString();
            TxtOffsetB.Text = _ccmStep.s43.ToString();
        }

        private void UpdateColorPatchDataGrid()
        {
            var items = (ObservableCollection<CcmIQResult>)ColorPatchDataGrid.ItemsSource;
            // 建议：在大量更新前，如果担心闪烁，可以考虑暂时 suspend layout，但在 WPF 中通常不需要
            for (int i = 0; i < items.Count && i < _colorPatchRegions.Count; i++)
            {
                var region = _colorPatchRegions[i];

                // 直接赋值，INotifyPropertyChanged 会自动通知 UI 刷新对应单元格
                items[i].RAvg = GetChannelAverage(region, 0);
                items[i].GAvg = GetChannelAverage(region, 1);
                items[i].BAvg = GetChannelAverage(region, 2);
            }

            // 强制刷新一下 DataGrid 的布局（如果依然有延迟感可以加上这句）
            ColorPatchDataGrid.Items.Refresh();
        }

        private double GetChannelAverage(RubberBandData region, int channel)
        {
            int x = Math.Max(0, (int)region.x);
            int y = Math.Max(0, (int)region.y);
            int w = Math.Min((int)region.width, _imageWidth - x);
            int h = Math.Min((int)region.height, _imageHeight - y);
            long sum = 0;
            int count = 0;

            for (int row = y; row < y + h; row++)
            {
                for (int col = x; col < x + w; col++)
                {
                    sum += ReadDemosaicPixel(channel, col, row);
                    count++;
                }
            }
            return count > 0 ? (double)sum / count : 0;
        }
        #endregion

        #region Step 4: 应用矩阵到预览图
        private async void OnApply_Click(object sender, RoutedEventArgs e)
        {
            if (!_isMatrixCalculated || _calculatedMatrix == null) return;

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
                SetStatus("正在应用CCM矩阵到预览图...");
                UpdateProgress(0, "准备数据...");
                _sw.Restart();

                await Task.Run(() =>
                {
                    ApplyMatrixToPreviewInternal(_cts.Token);
                }, _cts.Token);

                _sw.Stop();
                _showingCorrectedImage = !_showingCorrectedImage;
                BtnApply.Content = _showingCorrectedImage ? "🔄 显示原图" : "👁️ 应用预览";
                UpdateProgress(100, "完成");

                SetStatus($"✓ 矩阵{'已' + (_showingCorrectedImage ? "" : "取消")}应用，耗时: {_sw.ElapsedMilliseconds}ms");
                Logger.Info($"CCM矩阵应用完成，当前显示: {(_showingCorrectedImage ? "校正图" : "原图")}");
            }
            catch (OperationCanceledException)
            {
                SetStatus("⚠️ 应用已取消");
                Logger.Info("用户取消了矩阵应用");
            }
            catch (Exception ex)
            {
                Logger.Error($"CCM应用失败: {ex.Message}");
                ShowErrorDialog("应用失败", $"无法应用CCM矩阵:\n{ex.Message}");
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

        private void ApplyMatrixToPreviewInternal(CancellationToken ct)
        {
            if (!IsOriginalImageReady())
            {
                throw new InvalidOperationException("原始图像数据未初始化，请先加载RAW图像");
            }

            int totalPixels = _imageWidth * _imageHeight;

            if (_showingCorrectedImage)
            {
                if (IsCorrectedImageReady())
                {
                    var bitmap = CreateBitmapFromPointers(_originalImgPtrs);
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        RubberBandCtrl.DisplayImageSource = bitmap;
                    }));
                }
                else
                {
                    Logger.Warn("校正图像数据不可用，重新应用CCM矩阵");
                    ApplyCCMTransform(totalPixels, ct);
                }
                return;
            }
            else
            {
                ApplyCCMTransform(totalPixels, ct);
            }
        }

        private void ApplyCCMTransform(int totalPixels, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            FreeCorrectedBuffers();
            AllocateOutputBuffers(totalPixels);

            int[][] matrixJagged = new int[3][];
            for (int i = 0; i < 3; i++)
            {
                matrixJagged[i] = new int[] { _calculatedMatrix[i, 0], _calculatedMatrix[i, 1], _calculatedMatrix[i, 2] };
            }
            int[] offsets = { _ccmStep.s41, _ccmStep.s42, _ccmStep.s43 };

            int result = IspApi.CCM_Img(
                _originalImgPtrs,
                _correctedImgPtrs,
                _imageWidth, _imageHeight,
                 //ConvertToIntJagged(matrixJagged),
                 _calculatedMatrix,
                offsets
            );

            if (result != CcmErrorCode.CCM_SUCCESS)
            {
                throw new InvalidOperationException($"CCM_Img返回错误码: {result} ({GetErrorMessage(result)})");
            }

            var correctedBitmap = CreateBitmapFromPointers(_correctedImgPtrs);
            Application.Current.Dispatcher.Invoke(() =>
            {
                RubberBandCtrl.DisplayImageSource = correctedBitmap;
            });
        }

        private void AllocateOutputBuffers(int size)
        {
            for (int i = 0; i < 3; i++)
            {
                if (_correctedImgPtrs[i] == IntPtr.Zero)
                    _correctedImgPtrs[i] = Marshal.AllocHGlobal(size * sizeof(short));
            }
        }

        private void FreeCorrectedBuffers()
        {
            for (int i = 0; i < 3; i++)
            {
                if (_correctedImgPtrs[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_correctedImgPtrs[i]);
                    _correctedImgPtrs[i] = IntPtr.Zero;
                }
            }
        }

        private BitmapSource CreateBitmapFromPointers(IntPtr[] ptrs)
        {
            if (ptrs == null || ptrs.Length < 3 || ptrs.Any(p => p == IntPtr.Zero))
            {
                Logger.Warn("CreateBitmapFromPointers: 输入指针无效");
                return null;
            }
            int size = 0;
            IspApi.EncoderImgBuffer(ptrs, _imageWidth, _imageHeight, 2, null, ref size);

            byte[] buffer = new byte[size];

            IspApi.EncoderImgBuffer(ptrs, _imageWidth, _imageHeight, 2, buffer, ref size);

            try
            {
                var bitmapImage = new BitmapImage();
                using (MemoryStream memStream = new MemoryStream(buffer))
                {
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = memStream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                }

                //var flipTransform = new ScaleTransform(1, -1);
                //var flippedImage = new TransformedBitmap(bitmapImage, flipTransform);
                //flippedImage.Freeze();

                //return flippedImage;
                return bitmapImage;
            }
            catch (Exception ex)
            {
                Logger.Error($"创建Bitmap失败: {ex.Message}");

                return BitmapSource.Create(_imageWidth, _imageHeight, 96, 96, PixelFormats.Bgr24, null, buffer, _imageWidth * 3);
            }
        }

        private static int[][] ConvertToIntJagged(int[,] matrix)
        {
            int[][] jagged = new int[matrix.GetLength(0)][];
            for (int i = 0; i < matrix.GetLength(0); i++)
            {
                jagged[i] = new int[matrix.GetLength(1)];
                for (int j = 0; j < matrix.GetLength(1); j++)
                    jagged[i][j] = matrix[i, j];
            }
            return jagged;
        }
        #endregion

        #region Step 5: 质量评估
        private async void OnEvaluate_Click(object sender, RoutedEventArgs e)
        {
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
                SetStatus("正在评估色彩准确性...");
                UpdateProgress(0, "准备数据...");
                _sw.Restart();

                float deltaE = 0, deltaEab = 0;
                float[] perPatchDelta = new float[24];
                int[] rAvg = new int[24], gAvg = new int[24], bAvg = new int[24];

                await Task.Run(() =>
                {
                    UpdateProgress(20, "提取RGB数据...");

                    for (int i = 0; i < 24; i++)
                    {
                        rAvg[i] = (int)((ObservableCollection<CcmIQResult>)ColorPatchDataGrid.ItemsSource)[i].RAvg;
                        gAvg[i] = (int)((ObservableCollection<CcmIQResult>)ColorPatchDataGrid.ItemsSource)[i].GAvg;
                        bAvg[i] = (int)((ObservableCollection<CcmIQResult>)ColorPatchDataGrid.ItemsSource)[i].BAvg;
                    }

                    _cts.Token.ThrowIfCancellationRequested();
                    UpdateProgress(50, "计算Delta E...");

                    int result = IspApi.CCM_IQ(rAvg, gAvg, bAvg, out deltaE, out deltaEab, perPatchDelta);

                    if (result != CcmErrorCode.CCM_SUCCESS)
                    {
                        throw new InvalidOperationException($"CCM_IQ返回错误码: {result}");
                    }

                    UpdateProgress(90, "生成报告...");
                }, _cts.Token);

                _sw.Stop();
                UpdateProgress(100, "完成");

                UpdateEvaluationResults(deltaE, deltaEab, perPatchDelta);

                SetStatus($"✓ 色彩评估完成 - ΔE={deltaE:F2}, ΔEab={deltaEab:F2}, 耗时: {_sw.ElapsedMilliseconds}ms");
                UpdateButtonStates();

                Logger.Info($"CCM评估完成: Delta E={deltaE:F2}, Delta Eab={deltaEab:F2}");
            }
            catch (OperationCanceledException)
            {
                SetStatus("⚠️ 评估已取消");
                Logger.Info("用户取消了色彩评估");
            }
            catch (Exception ex)
            {
                Logger.Error($"CCM评估失败: {ex.Message}");
                ShowErrorDialog("评估失败", $"无法评估色彩质量:\n{ex.Message}");
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

        private void UpdateEvaluationResults(float deltaE, float deltaEab, float[] perPatchDelta)
        {
            TxtDeltaE.Text = $"{deltaE:F2}";
            TxtDeltaEab.Text = $"{deltaEab:F2}";
            TxtRatingStars.Text = GetRatingString(deltaE);
            TxtRatingStars.Foreground = GetRatingColor(deltaE);

            double accuracy = Math.Max(0, 100 - deltaE * 10);
            ProgressAccuracy.Value = accuracy;

            var items = (ObservableCollection<CcmIQResult>)ColorPatchDataGrid.ItemsSource;
            for (int i = 0; i < Math.Min(24, perPatchDelta.Length); i++)
            {
                items[i].DeltaE = perPatchDelta[i];
            }

            BtnShowDetail.IsEnabled = true;
            BtnExport.IsEnabled = true;

            TxtStatusMessage.Text = GetQualityDescription(deltaE);
        }

        private static string GetRatingString(double de)
        {
            if (de < 3.0) return "⭐⭐⭐ 优秀";
            if (de < 6.0) return "⭐⭐ 良好";
            if (de < 10.0) return "⭐ 可接受";
            return "❌ 需优化";
        }

        private static SolidColorBrush GetRatingColor(double de)
        {
            if (de < 3.0) return new SolidColorBrush(Color.FromRgb(76, 175, 80));
            if (de < 6.0) return new SolidColorBrush(Color.FromRgb(255, 193, 7));
            if (de < 10.0) return new SolidColorBrush(Color.FromRgb(255, 152, 0));
            return new SolidColorBrush(Color.FromRgb(244, 67, 54));
        }

        private static string GetQualityDescription(double de)
        {
            if (de < 3.0) return "色彩还原优秀！校正后的颜色与标准值高度一致，适合专业摄影工作流。";
            if (de < 6.0) return "色彩质量良好。大多数场景下肉眼难以察觉偏差，可满足一般应用需求。";
            if (de < 10.0) return "色彩基本准确但在某些色调下可能存在可见偏差。建议针对特定场景微调参数。";
            return "色彩偏差较大，建议重新校准或检查光源条件、色卡放置位置是否正确。";
        }
        #endregion

        #region 辅助功能
        private void UpdateProgress(int percent, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ProgressCalc.Value = percent;
                TxtProgress.Text = message;
            });
        }

        private static void ShowWarningDialog(string title, string message)
        {
            MessageBox.Show(null, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OnShowDetail_Click(object sender, RoutedEventArgs e)
        {
            var detailWindow = new Window
            {
                Title = "24色块详细Delta E偏差报告",
                Width = 600,
                Height = 500,
                Owner = this
            };

            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                IsReadOnly = true,
                ItemsSource = ColorPatchDataGrid.ItemsSource,
                Margin = new Thickness(10)
            };
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new System.Windows.Data.Binding("Index"), Width = 40 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "R均值", Binding = new System.Windows.Data.Binding("RAvg"), Width = 70 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "G均值", Binding = new System.Windows.Data.Binding("GAvg"), Width = 70 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "B均值", Binding = new System.Windows.Data.Binding("BAvg"), Width = 70 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "ΔE", Binding = new System.Windows.Data.Binding("DeltaE"), Width = 60 });

            detailWindow.Content = dataGrid;
            detailWindow.ShowDialog();
        }

        private void OnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                    Title = "导出CCM评估报告"
                };

                if (dialog.ShowDialog() != true) return;

                using (var writer = new StreamWriter(dialog.FileName))
                {
                    writer.WriteLine("#,R均值,G均值,B均值,Delta_E");
                    var items = (ObservableCollection<CcmIQResult>)ColorPatchDataGrid.ItemsSource;
                    foreach (var item in items)
                    {
                        writer.WriteLine($"{item.Index},{item.RAvg:F0},{item.GAvg:F0},{item.BAvg:F0},{item.DeltaE:F2}");
                    }
                    writer.WriteLine();
                    writer.WriteLine($"# CCM评估摘要");
                    writer.WriteLine($"# Delta E: {TxtDeltaE.Text}");
                    writer.WriteLine($"# Delta Eab: {TxtDeltaEab.Text}");
                    writer.WriteLine($"# 评级: {TxtRatingStars.Text}");
                }

                MessageBox.Show(this, $"报告已导出到:\n{dialog.FileName}", "导出成功",
                               MessageBoxButton.OK, MessageBoxImage.Information);
                Logger.Info($"CCM评估报告已导出: {dialog.FileName}");
            }
            catch (Exception ex)
            {
                Logger.Error($"导出失败: {ex.Message}");
                ShowErrorDialog("导出失败", $"无法保存文件:\n{ex.Message}");
            }
        }

        private void SetStatus(string message)
        {
            StatusBarText.Text = message;
            TxtElapsedTime.Text = $"耗时: {_sw.ElapsedMilliseconds}ms";
            Logger.Debug(message);
        }

        private static void ShowErrorDialog(string title, string message)
        {
            MessageBox.Show(null, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {

        }

        private void Onloaded(object sender, RoutedEventArgs e)
        {

        }

        private static string GetErrorMessage(int errorCode)
        {
            switch (errorCode)
            {
                case CcmErrorCode.CCM_ERR_NULL_POINTER: return "空指针输入";
                case CcmErrorCode.CCM_ERR_INVALID_PARAM: return "参数超出有效范围";
                case CcmErrorCode.CCM_ERR_MEMORY_ALLOC: return "内存分配失败";
                case CcmErrorCode.CCM_ERR_NO_CONVERGENCE: return "搜索未收敛";
                default: return $"未知错误 ({errorCode})";
            }
        }
        #endregion
    }

    /*
    public partial class CcmOnlineIQWindow : Window
    {
        private int _imageWidth = 0;
        private int _imageHeight = 0;
        private WriteableBitmap _bitmap;

        private double _horizontalScale = 1.0;
        private double _verticalScale = 1.0;

        private double[] _avgRArray = new double[6];
        private double[] _avgGArray = new double[6];
        private double[] _avgBArray = new double[6];
        private int _selectedCalcMode = 0;

        private List<RubberBandData> _rubberBandData = new List<RubberBandData>();

        public static readonly DependencyProperty IsDrawingProperty = DependencyProperty.Register("IsDrawing",
            typeof(bool),
            typeof(CcmOnlineIQWindow),
            new PropertyMetadata(true));

        public bool IsDrawing
        {
            get { return (bool)GetValue(IsDrawingProperty); }
            set { SetValue(IsDrawingProperty, value); }
        }

        public static readonly DependencyProperty IsCalculatingProperty = DependencyProperty.Register("IsCalculating",
            typeof(bool),
            typeof(CcmOnlineIQWindow),
            new PropertyMetadata(false));


        public bool IsCalculating
        {
            get { return (bool)GetValue(IsCalculatingProperty); }
            set { SetValue(IsCalculatingProperty, value); }
        }

        public CcmOnlineIQWindow()
        {
            DataContext = new ObservableCollection<KeyValuePair<string, string>>();
            InitializeComponent();
        }

        public void Onloaded(object sender, RoutedEventArgs e)
        {
            DisplayControl.DataContext = _rubberBandData;
            DisplayControl.MaxBands = int.MaxValue;


            _imageWidth = UvcReceiver.Instance.VideoWidth;
            _imageHeight = UvcReceiver.Instance.VideoHeight;

            _bitmap = new WriteableBitmap(_imageWidth,
                _imageHeight, 96, 96, System.Windows.Media.PixelFormats.Rgb24, null);
            DisplayControl.DisplayImageSource = _bitmap;
        }

        private void OnUvcDataReceive(byte[] dataBuffer)
        {
            
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

            _imageWidth = 0;
            _imageHeight = 0;

            IsCalculating = false;
            IsDrawing = true;
        }


        private void OnClickCalcIQ(object sender, RoutedEventArgs e)
        {
            IsCalculating = true;
            IsDrawing = false;
        }

        private void OnClickUndoRubberBand(object sender, RoutedEventArgs e)
        {
            DisplayControl.UndoDrawRubberBand();
        }

        private void OnClickLoadImage(object sender, RoutedEventArgs e)
        {

        }
    }

    */
}
