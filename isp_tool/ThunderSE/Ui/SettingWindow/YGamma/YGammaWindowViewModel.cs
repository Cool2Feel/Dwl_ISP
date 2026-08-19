using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Ui.CommonCustomControl;
using ThunderSE.Ui.SettingWindow.IspSteps;

namespace ThunderSE.Ui.SettingWindow.YGamma
{
    class YGammaWindowViewModel : ViewModelBase
    {
        private IspStepsWindow _ispStepsWindow;

        private BezierCurveEditor _curveEditor;

        private RelayCommand _loadYGammaTableFromFileCommand;
        private RelayCommand _saveYGammaTableToFileCommand;

        private RelayCommand _loadYGammaTableFromDeviceCommand;
        private RelayCommand _saveYGammaTableToDeviceCommand;
        private RelayCommand _resetYGammaTableToDeviceCommand;

        private RelayCommand _viewPreviousIspStep;
        private string _coordinatesText;

        private ThunderSE.DeviceConfig.Isp.YGamma _yGamma;

        private ThunderSE.DeviceConfig.Isp.GammaTable _gammaTable;

        // === 新增：用于 UI 平滑渲染的完整 256 点数据源 ===
        public ObservableCollection<KeyValuePair<int, short>> FullGammaCurve { get; set; }

        private ObservableCollection<KeyValuePair<int, short>> _yGammaTable =
            new ObservableCollection<KeyValuePair<int, short>>();

        private int[] _yGammaKeyPointXValues = new int[]{
            0,1,3,6,10,16,26,39,55,71,87,103,119,135,151,167,191,223,239,255
        };
        private bool _isUpdatingFromInterpolation = false;
        private bool _enableSmoothProcessing = false;

        private bool _enableAnchorPointSmoothing = false;

        // 贝塞尔控制点集合（4个点：P0=起点, P1/P2=可拖动控制点, P3=终点）
        private ObservableCollection<KeyValuePair<int, short>> _bezierControlPoints =
            new ObservableCollection<KeyValuePair<int, short>>();
        private bool _isUpdatingBezierPoints = false;

        private bool _isSaveYGammaTable =  false;

        public ObservableCollection<KeyValuePair<int, short>> BezierControlPoints => _bezierControlPoints;

        private bool isbezierLoad = false;
        // 节流机制相关字段
        private DispatcherTimer _throttleTimer;
        private bool _pendingUpdate = false;
        private const int ThrottleIntervalMs = 100; // 节流间隔：50毫秒

        public YGammaWindowViewModel(Processor ispProcessor)
        {
            IspProcessor = ispProcessor;
            _yGamma = (ThunderSE.DeviceConfig.Isp.YGamma)ispProcessor.RgbFileProcessSteps[IspModule.YGamma];
            _gammaTable = (ThunderSE.DeviceConfig.Isp.GammaTable)ispProcessor.RgbFileProcessSteps[IspModule.GammaTable];

            _loadYGammaTableFromFileCommand = new RelayCommand(LoadYGammaTableFromFile);
            _saveYGammaTableToFileCommand = new RelayCommand(SaveYGammaTableToFile);

            _loadYGammaTableFromDeviceCommand = new RelayCommand(LoadYGammaTableFromDevice);
            _saveYGammaTableToDeviceCommand = new RelayCommand(SaveYGammaTableToDevice);
            _resetYGammaTableToDeviceCommand = new RelayCommand(OnReset);

            _viewPreviousIspStep = new RelayCommand(ViewPreviousIspStep);

            FullGammaCurve = new ObservableCollection<KeyValuePair<int, short>>();
            _yGamma.PropertyChanged += YGammaPropertyChanged;
            _gammaTable.PropertyChanged += YGammaTablePropertyChanged;
            // 初始化节流定时器
            //_throttleTimer = new DispatcherTimer();
            //_throttleTimer.Interval = TimeSpan.FromMilliseconds(ThrottleIntervalMs);
            //_throttleTimer.Tick += ThrottleTimer_Tick;

            //foreach (var xValue in _yGammaKeyPointXValues)
            //{
            //    KeyValuePair<int, short>? item = _yGammaTable.FirstOrDefault(pair => pair.Key == xValue);
            //    if (item != null)
            //    {
            //        _yGammaTable.Remove(item.Value);
            //    }
            //    _yGammaTable.Add(new KeyValuePair<int, short>(xValue, _yGamma.YGammaTable[xValue]));
            //}

            // ========== 修复：正确初始化关键点集合 ==========
            System.Diagnostics.Debug.WriteLine($"=== ViewModel 初始化 ===");
            System.Diagnostics.Debug.WriteLine($"底层数组长度: {_yGamma.YGammaTable.Length}");
            System.Diagnostics.Debug.WriteLine($"关键点X值数量: {_yGammaKeyPointXValues.Length}");
            System.Diagnostics.Debug.WriteLine($"关键点X值: [{string.Join(", ", _yGammaKeyPointXValues)}]");

            // 清空现有集合，确保从干净状态开始
            _yGammaTable.Clear();

            // 逐个添加关键点
            int addedCount = 0;
            foreach (var xValue in _yGammaKeyPointXValues)
            {
                if (xValue >= 0 && xValue < _yGamma.YGammaTable.Length)
                {
                    _yGammaTable.Add(new KeyValuePair<int, short>(xValue, _yGamma.YGammaTable[xValue]));
                    addedCount++;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 跳过 X={xValue}: 超出有效范围");
                }
            }

            System.Diagnostics.Debug.WriteLine(
                $"✅ 初始化完成: 成功添加 {addedCount}/{_yGammaKeyPointXValues.Length} 个关键点");
            System.Diagnostics.Debug.WriteLine(
                $"最终集合大小: {_yGammaTable.Count}");
            // ========== 初始化结束 ==========

            //_bezierControlPoints.CollectionChanged += BezierControlPoints_CollectionChanged;

            //_yGammaTable.CollectionChanged += YGammaTable_CollectionChanged;

        }


        void YGammaTable_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            //switch (e.Action)
            //{
            //    case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
            //        break;
            //    case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
            //        break;
            //    case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
            //        break;
            //    case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
            //        {
            //            _yGamma.YGammaTable[_yGammaKeyPointXValues[e.NewStartingIndex]] = _yGammaTable[e.NewStartingIndex].Value;

            //            if (e.NewStartingIndex > 0)
            //            {
            //                int previousYGammaKeyPointX = _yGammaKeyPointXValues[e.NewStartingIndex - 1];
            //                int gammaPointCountBetweenTwoKeyPoints = _yGammaKeyPointXValues[e.NewStartingIndex] - previousYGammaKeyPointX;

            //                float partitionalValueBetweenKeyPoints = 
            //                    (_yGammaTable[e.NewStartingIndex].Value - _yGammaTable[e.NewStartingIndex - 1].Value) / (float)gammaPointCountBetweenTwoKeyPoints;

            //                for (int i = 1; i < gammaPointCountBetweenTwoKeyPoints; i++)
            //                {
            //                    _yGamma.YGammaTable[previousYGammaKeyPointX + i] = 
            //                        (short)(_yGamma.YGammaTable[previousYGammaKeyPointX] 
            //                            + (short)Math.Floor(partitionalValueBetweenKeyPoints * i));
            //                }
            //            }

            //            if (e.NewStartingIndex < _yGammaKeyPointXValues.Length - 1)
            //            {
            //                int frontYGammaKeyPointX = _yGammaKeyPointXValues[e.NewStartingIndex + 1];
            //                int gammaPointCountBetweenTwoKeyPoints = frontYGammaKeyPointX - _yGammaKeyPointXValues[e.NewStartingIndex];

            //                float partitionalValueBetweenKeyPoints =
            //                    (_yGammaTable[e.NewStartingIndex + 1].Value - _yGammaTable[e.NewStartingIndex].Value) / (float)gammaPointCountBetweenTwoKeyPoints;

            //                for (int i = 1; i < gammaPointCountBetweenTwoKeyPoints; i++)
            //                {
            //                    _yGamma.YGammaTable[_yGammaTable[e.NewStartingIndex].Key + i] =
            //                        (short)(_yGamma.YGammaTable[_yGammaTable[e.NewStartingIndex].Key] 
            //                            + (short)Math.Floor(partitionalValueBetweenKeyPoints * i));
            //                }
            //            }
            //        }
            //        break;
            //    case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
            //        break;
            //    default:
            //        break;
            //}
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
                return;

            // 边界检查
            if (e.NewStartingIndex < 0 || e.NewStartingIndex >= _yGammaKeyPointXValues.Length)
                return;

            // ✅ 关键防护：如果是平滑算法内部更新关键点触发的变更，跳过不处理
            // 防止 RefitSmoothCurve/UpdateKeyPointsFromUnderlying 修改 _yGammaTable
            // 触发 CollectionChanged -> 重启定时器 -> 无限循环导致 OOM
            if (_isUpdatingFromInterpolation)
                return;

            try
            {
                int currentKeyPointIndex = e.NewStartingIndex;
                int currentKeyPointX = _yGammaKeyPointXValues[currentKeyPointIndex];
                short currentKeyPointY = _yGammaTable[currentKeyPointIndex].Value;

                // 更新当前关键点
                _yGamma.YGammaTable[currentKeyPointX] = currentKeyPointY;

                // 使用节流机制延迟执行插值计算
                _pendingUpdate = true;

                // 重启定时器：如果在节流间隔内有新的事件，会重置计时
                _throttleTimer.Stop();
                _throttleTimer.Start();

                /*
                // 根据平滑选项选择不同的插值策略
                if (_enableSmoothProcessing)
                {
                    // 使用三次样条插值重新计算完整曲线
                    RecalculateGammaTableWithSpline();
                }
                else
                {
                    // 向前插值
                    if (currentKeyPointIndex > 0)
                    {
                        InterpolateBetweenKeyPoints(
                            currentKeyPointIndex - 1,
                            currentKeyPointIndex
                        );
                    }

                    // 向后插值
                    if (currentKeyPointIndex < _yGammaKeyPointXValues.Length - 1)
                    {
                        InterpolateBetweenKeyPoints(
                            currentKeyPointIndex,
                            currentKeyPointIndex + 1
                        );
                    }
                }
                */
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gamma插值错误: {ex.Message}");
            }
        }


        /// <summary>
        /// 节流定时器回调：在拖动停止后执行插值计算
        /// </summary>
        private void ThrottleTimer_Tick(object sender, EventArgs e)
        {
            _throttleTimer.Stop();

            if (!_pendingUpdate)
                return;

            _pendingUpdate = false;

            try
            {
                // 根据平滑选项选择不同的插值策略
                if (_enableSmoothProcessing)
                {
                    // ✅ 设置标志，防止插值过程中的 CollectionChanged 触发递归
                    _isUpdatingFromInterpolation = true;
                    try
                    {
                        RecalculateGammaTableWithSpline();
                    }
                    finally
                    {
                        // ✅ 确保标志被重置
                        _isUpdatingFromInterpolation = false;
                    }
                }
                else if (_enableAnchorPointSmoothing)
                {
                    // ✅ 设置标志，防止插值过程中的 CollectionChanged 触发递归
                    _isUpdatingFromInterpolation = true;
                    try
                    {
                    }
                    finally
                    {
                        // ✅ 确保标志被重置
                        _isUpdatingFromInterpolation = false;
                    }
                }
                else
                {
                    // 线性插值：对所有段进行插值
                    //for (int i = 0; i < _yGammaKeyPointXValues.Length - 1; i++)
                    for (int i = 0; i < _yGammaTable.Count - 1; i++)
                    {
                        InterpolateBetweenKeyPoints(i, i + 1);
                    }
                }
                UpdateFullGammaCurve();
                // 通知 UI 更新
                RaisePropertyChanged("YGammaTable");
                RaisePropertyChanged("FullGammaCurve");

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gamma节流插值错误: {ex.Message}");
                _isUpdatingFromInterpolation = false;  // 确保异常时也重置
            }
        }

        /// <summary>
        /// 在两个关键点之间进行线性插值
        /// </summary>
        private void InterpolateBetweenKeyPoints(int startIndex, int endIndex)
        {
            // 修改校验逻辑：以关键点数组长度为准
            if (startIndex < 0 || startIndex >= _yGammaKeyPointXValues.Length ||
                endIndex < 0 || endIndex >= _yGammaKeyPointXValues.Length ||
                startIndex >= _yGammaTable.Count || endIndex >= _yGammaTable.Count)
            {
                System.Diagnostics.Debug.WriteLine($"警告: 插值索引超出范围 - startIndex={startIndex}, endIndex={endIndex}");
                return;
            }

            int startX = _yGammaKeyPointXValues[startIndex];
            int endX = _yGammaKeyPointXValues[endIndex];
            short startY = _yGammaTable[startIndex].Value;
            short endY = _yGammaTable[endIndex].Value;

            int pointCount = endX - startX;
            if (pointCount <= 1)
                return;

            float step = (endY - startY) / (float)pointCount;

            for (int i = 1; i < pointCount; i++)
            {
                _yGamma.YGammaTable[startX + i] = (short)Math.Round(startY + step * i);
            }
        }


        /// <summary>
        /// 使用三次样条插值重新计算完整Gamma表
        /// </summary>
        private void RecalculateGammaTableWithSpline()
        {
            // 修复1：使用预期的关键点数量
            int n = _yGammaKeyPointXValues.Length;

            // 边界检查：确保关键点集合大小匹配
            if (_yGammaTable.Count != n)
            {
                System.Diagnostics.Debug.WriteLine($"警告: 关键点集合大小不匹配 - 期望={n}, 实际={_yGammaTable.Count}");
                return;
            }

            double[] x = new double[n];
            double[] y = new double[n];

            for (int i = 0; i < n; i++)
            {
                x[i] = _yGammaKeyPointXValues[i];
                y[i] = _yGammaTable[i].Value;
            }

            // 计算样条系数（自然边界条件）
            double[] h = new double[n - 1];
            double[] alpha = new double[n - 1];
            double[] l = new double[n];
            double[] mu = new double[n];
            double[] z = new double[n];
            double[] c = new double[n];
            double[] b = new double[n - 1];
            double[] d = new double[n - 1];

            for (int i = 0; i < n - 1; i++)
            {
                h[i] = x[i + 1] - x[i];

                // 修复3：除零保护
                if (h[i] <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"警告: 关键点X坐标无效 - x[{i}]={x[i]}, x[{i + 1}]={x[i + 1]}");
                    return;
                }
            }

            for (int i = 1; i < n - 1; i++)
                alpha[i] = (3.0 / h[i]) * (y[i + 1] - y[i]) - (3.0 / h[i - 1]) * (y[i] - y[i - 1]);

            l[0] = 1; mu[0] = 0; z[0] = 0;

            for (int i = 1; i < n - 1; i++)
            {
                l[i] = 2 * (x[i + 1] - x[i - 1]) - h[i - 1] * mu[i - 1];

                // 除零保护
                if (Math.Abs(l[i]) < 1e-10)
                {
                    System.Diagnostics.Debug.WriteLine($"警告: 样条系数计算异常 - l[{i}]={l[i]}");
                    return;
                }

                mu[i] = h[i] / l[i];
                z[i] = (alpha[i] - h[i - 1] * z[i - 1]) / l[i];
            }

            l[n - 1] = 1; z[n - 1] = 0; c[n - 1] = 0;

            for (int j = n - 2; j >= 0; j--)
            {
                c[j] = z[j] - mu[j] * c[j + 1];
                b[j] = (y[j + 1] - y[j]) / h[j] - h[j] * (c[j + 1] + 2 * c[j]) / 3;
                d[j] = (c[j + 1] - c[j]) / (3 * h[j]);
            }

            // 使用样条系数填充完整Gamma表
            for (int px = 0; px < 256; px++)
            {
                // 找到所在区间
                int interval = 0;
                for (int i = 0; i < n - 1; i++)
                {
                    if (px >= x[i] && px <= x[i + 1])
                    {
                        interval = i;
                        break;
                    }
                }

                double dx = px - x[interval];
                double value = y[interval] + b[interval] * dx + c[interval] * dx * dx + d[interval] * dx * dx * dx;

                // 限制范围并四舍五入
                _yGamma.YGammaTable[px] = (short)Math.Max(0, Math.Min(1023, Math.Round(value)));

            }
        }


        /*
        /// <summary>
        /// 基于起点、当前拖动点、终点进行完整的曲线平滑处理
        /// </summary>
        /// <param name="draggedIndex">当前拖动的关键点索引</param>
        private void RecalculateGammaTableWithThreePointAnchor(int draggedIndex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"🎯 三点锚定平滑: 拖动点索引={draggedIndex}");

            // 获取三个锚点
            int anchor1_X = 0;
            int anchor1_Y = _yGammaTable[0].Value;  // 起点

            int anchor2_X = _yGammaKeyPointXValues[draggedIndex];
            int anchor2_Y = _yGammaTable[draggedIndex].Value;  // 拖动点

            int anchor3_X = 255;
            int anchor3_Y = _yGammaTable[_yGammaTable.Count - 1].Value;  // 终点

            System.Diagnostics.Debug.WriteLine(
                $"  锚点1 (起点): ({anchor1_X}, {anchor1_Y})");
            System.Diagnostics.Debug.WriteLine(
                $"  锚点2 (拖动): ({anchor2_X}, {anchor2_Y})");
            System.Diagnostics.Debug.WriteLine(
                $"  锚点3 (终点): ({anchor3_X}, {anchor3_Y})");

            // 验证数据有效性
            if (anchor1_X >= anchor2_X || anchor2_X >= anchor3_X)
            {
                System.Diagnostics.Debug.WriteLine("❌ 锚点X坐标顺序错误");
                return;
            }

            // 使用三段样条插值
            // 第一段: [0, anchor2_X]
            // 第二段: [anchor2_X, 255]

            double[] x = { anchor1_X, anchor2_X, anchor3_X };
            double[] y = { anchor1_Y, anchor2_Y, anchor3_Y };

            // 计算样条系数（3个点的简化版本）
            int n = 3;
            double[] h = new double[n - 1];
            double[] alpha = new double[n - 1];
            double[] l = new double[n];
            double[] mu = new double[n];
            double[] z = new double[n];
            double[] c = new double[n];
            double[] b = new double[n - 1];
            double[] d = new double[n - 1];

            h[0] = x[1] - x[0];  // anchor2_X - 0
            h[1] = x[2] - x[1];  // 255 - anchor2_X

            // 边界检查
            if (h[0] <= 0 || h[1] <= 0)
            {
                System.Diagnostics.Debug.WriteLine("❌ 区间宽度无效");
                return;
            }

            // 自然边界条件：二阶导数在端点为0
            l[0] = 1; mu[0] = 0; z[0] = 0;

            // 中间点（只有一个）
            l[1] = 2 * (x[2] - x[0]) - h[0] * mu[0];
            mu[1] = h[1] / l[1];
            alpha[1] = (3.0 / h[1]) * (y[2] - y[1]) - (3.0 / h[0]) * (y[1] - y[0]);
            z[1] = (alpha[1] - h[0] * z[0]) / l[1];

            l[2] = 1; z[2] = 0; c[2] = 0;

            // 回代
            c[1] = z[1] - mu[1] * c[2];
            c[0] = z[0] - mu[0] * c[1];

            b[0] = (y[1] - y[0]) / h[0] - h[0] * (c[1] + 2 * c[0]) / 3;
            b[1] = (y[2] - y[1]) / h[1] - h[1] * (c[2] + 2 * c[1]) / 3;

            d[0] = (c[1] - c[0]) / (3 * h[0]);
            d[1] = (c[2] - c[1]) / (3 * h[1]);

            // 使用样条曲线重新计算完整的 256 点 Gamma 表
            for (int px = 0; px < 256; px++)
            {
                int interval;

                // 确定 px 所在的区间
                if (px <= anchor2_X)
                {
                    interval = 0;  // 第一段 [0, anchor2_X]
                }
                else
                {
                    interval = 1;  // 第二段 [anchor2_X, 255]
                }

                double dx = px - x[interval];
                double value = y[interval] + b[interval] * dx
                                          + c[interval] * dx * dx
                                          + d[interval] * dx * dx * dx;

                // 限制范围
                _yGamma.YGammaTable[px] = (short)Math.Max(0, Math.Min(1023, Math.Round(value)));
            }

            // 根据新的 Gamma 表更新所有关键点
            for (int i = 0; i < _yGammaTable.Count; i++)
            {
                int xValue = _yGammaKeyPointXValues[i];
                if (xValue >= 0 && xValue < 256)
                {
                    _yGammaTable[i] = new KeyValuePair<int, short>(
                        xValue,
                        _yGamma.YGammaTable[xValue]
                    );
                }
            }

            System.Diagnostics.Debug.WriteLine("✅ 三点锚定平滑完成");
        }

        /// <summary>
        /// 带权重的曲线平滑处理
        /// </summary>
        private void RecalculateGammaTableWithWeightedSpline(int draggedIndex)
        {
            int n = _yGammaKeyPointXValues.Length;
            double[] x = new double[n];
            double[] y = new double[n];
            double[] weights = new double[n];  // 权重数组

            int draggedX = _yGammaKeyPointXValues[draggedIndex];

            // 收集关键点并计算权重
            for (int i = 0; i < n; i++)
            {
                x[i] = _yGammaKeyPointXValues[i];

                if (i == 0 || i == n - 1)
                {
                    // 起点和终点：权重 1.0（完全固定）
                    y[i] = _yGammaTable[i].Value;
                    weights[i] = 1.0;
                }
                else if (i == draggedIndex)
                {
                    // 拖动点：权重 1.0（用户指定）
                    y[i] = _yGammaTable[i].Value;
                    weights[i] = 1.0;
                }
                else
                {
                    // 其他点：根据距离计算权重
                    int distance = Math.Abs(_yGammaKeyPointXValues[i] - draggedX);
                    // 高斯衰减：距离越远，权重越小
                    double sigma = 50.0;  // 控制衰减速度
                    weights[i] = Math.Exp(-(distance * distance) / (2 * sigma * sigma));

                    // 原始值（会被样条调整）
                    y[i] = _yGammaTable[i].Value;
                }
            }

            // 使用加权样条插值
            // ... （实现加权版本的样条算法）
        }

        // 在 ViewModel 类中添加字段
        private double _influenceRadius = 20.0;  // 影响半径（像素）

        /// <summary>
        /// 影响半径属性（可在 UI 中绑定滑块）
        /// </summary>
        public double InfluenceRadius
        {
            get { return _influenceRadius; }
            set
            {
                if (Math.Abs(_influenceRadius - value) < 1.0) return;
                _influenceRadius = Math.Max(20, Math.Min(150, value));
                RaisePropertyChanged("InfluenceRadius");
            }
        }

        /// <summary>
        /// 基于可调节影响范围的加权样条平滑
        /// </summary>
        private void RecalculateGammaTableWithAdjustableWeightedSpline(int draggedIndex)
        {
            // ========== 新增：边界检查 ==========
            int totalPoints = _yGammaTable.Count;

            // 不允许拖动起点和终点
            if (draggedIndex == 0 || draggedIndex == totalPoints - 1)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"⚠️ 跳过: 起点(index=0)和终点(index={totalPoints - 1})不可拖动");

                // 恢复起点/终点的原始值
                if (draggedIndex == 0)
                {
                    _yGammaTable[0] = new KeyValuePair<int, short>(0, 0);
                    _yGamma.YGammaTable[0] = 0;
                }
                else
                {
                    _yGammaTable[totalPoints - 1] = new KeyValuePair<int, short>(255, 1023);
                    _yGamma.YGammaTable[255] = 1023;
                }

                UpdateFullGammaCurve();
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"🎯 可调加权样条平滑: 拖动点索引={draggedIndex}, 影响半径={_influenceRadius:F0}");

            // 获取锚点
            int startX = 0;
            short startY = _yGammaTable[0].Value;

            int dragX = _yGammaKeyPointXValues[draggedIndex];
            short dragY = _yGammaTable[draggedIndex].Value;

            int endX = 255;
            short endY = _yGammaTable[_yGammaTable.Count - 1].Value;

            // 确保单调性
            dragY = (short)Math.Max(startY + 1, Math.Min(endY - 1, dragY));
            _yGammaTable[draggedIndex] = new KeyValuePair<int, short>(dragX, dragY);
            _yGamma.YGammaTable[dragX] = dragY;

            // 保存旧曲线
            short[] oldGammaTable = new short[256];
            Array.Copy(_yGamma.YGammaTable, oldGammaTable, 256);

            // 计算距离权重（使用可调节的影响半径）
            double sigma = _influenceRadius / 2.0;
            double[] distanceWeights = new double[256];

            for (int px = 0; px < 256; px++)
            {
                double distance = Math.Abs(px - dragX);
                double weight = Math.Exp(-(distance * distance) / (2 * sigma * sigma));

                // 起点和终点权重为 0
                if (px == startX || px == endX)
                {
                    weight = 0;
                }

                distanceWeights[px] = weight;
            }

            // 计算三点样条目标曲线
            double[] x = { startX, dragX, endX };
            double[] y = { startY, dragY, endY };

            int n = 3;
            double[] h = { x[1] - x[0], x[2] - x[1] };
            double[] l = new double[n];
            double[] mu = new double[n];
            double[] z = new double[n];
            double[] c = new double[n];
            double[] b = new double[n - 1];
            double[] d = new double[n - 1];

            if (h[0] <= 0 || h[1] <= 0) return;

            l[0] = 1; mu[0] = 0; z[0] = 0;
            l[1] = 2 * (x[2] - x[0]) - h[0] * mu[0];
            if (Math.Abs(l[1]) < 1e-10) return;

            mu[1] = h[1] / l[1];
            double alpha1 = (3.0 / h[1]) * (y[2] - y[1]) - (3.0 / h[0]) * (y[1] - y[0]);
            z[1] = (alpha1 - h[0] * z[0]) / l[1];
            l[2] = 1; z[2] = 0; c[2] = 0;

            c[1] = z[1] - mu[1] * c[2];
            c[0] = z[0] - mu[0] * c[1];
            b[0] = (y[1] - y[0]) / h[0] - h[0] * (c[1] + 2 * c[0]) / 3;
            b[1] = (y[2] - y[1]) / h[1] - h[1] * (c[2] + 2 * c[1]) / 3;
            d[0] = (c[1] - c[0]) / (3 * h[0]);
            d[1] = (c[2] - c[1]) / (3 * h[1]);

            short[] targetSpline = new short[256];
            for (int px = 0; px < 256; px++)
            {
                int interval = px <= dragX ? 0 : 1;
                double dx = px - x[interval];
                double value = y[interval] + b[interval] * dx
                                          + c[interval] * dx * dx
                                          + d[interval] * dx * dx * dx;
                targetSpline[px] = (short)Math.Max(0, Math.Min(1023, Math.Round(value)));
            }

            // 混合新旧曲线
            short previousValue = startY;
            for (int px = 0; px < 256; px++)
            {
                double weight = distanceWeights[px];
                double blendedValue = targetSpline[px] * weight + oldGammaTable[px] * (1 - weight);
                short newValue = (short)Math.Max(0, Math.Min(1023, Math.Round(blendedValue)));

                // 强制单调性
                if (newValue < previousValue)
                    newValue = previousValue;

                _yGamma.YGammaTable[px] = newValue;
                previousValue = newValue;
            }

            // 更新关键点
            for (int i = 0; i < _yGammaTable.Count; i++)
            {
                int xValue = _yGammaKeyPointXValues[i];
                if (xValue >= 0 && xValue < 256)
                {
                    _yGammaTable[i] = new KeyValuePair<int, short>(
                        xValue, _yGamma.YGammaTable[xValue]);
                }
            }
        }
        

        private void RecalculateGammaTableWithAdjustableKeyPointWeight(int draggedIndex)
        {
            int totalPoints = _yGammaTable.Count;
            if (draggedIndex == 0 || draggedIndex == totalPoints - 1)
            {
                // 固定端点
                _yGamma.YGammaTable[0] = 0;
                _yGamma.YGammaTable[255] = 1023;
                UpdateKeyPointsFromUnderlying();
                UpdateFullGammaCurve(); // 需实现：基于关键点重新插值全曲线
                return;
            }

            // 1. 保存旧曲线
            short[] oldGamma = new short[256];
            Array.Copy(_yGamma.YGammaTable, oldGamma, 256);

            // 2. 获取当前所有关键点（已包含新拖动的Y值）
            int[] keyX = _yGammaKeyPointXValues;
            short[] keyY = new short[totalPoints];
            for (int i = 0; i < totalPoints; i++)
                keyY[i] = _yGammaTable[i].Value;

            // 3. 使用全局自然样条生成目标曲线（基于所有关键点）
            short[] targetSpline = GlobalCubicSplineInterpolate(keyX, keyY, 256);

            // 4. 计算每个像素的混合权重（基于X轴距离，非索引距离）
            double[] pixelWeights = ComputePixelWeightsByXDistance(draggedIndex, keyX);

            // 5. 混合并保证单调性
            short[] newGamma = new short[256];
            newGamma[0] = 0;
            for (int px = 1; px < 256; px++)
            {
                double weight = pixelWeights[px];
                double blended = targetSpline[px] * weight + oldGamma[px] * (1 - weight);
                short raw = (short)Math.Max(0, Math.Min(1023, Math.Round(blended)));
                // 强制单调且不能低于前一点（避免凹陷）
                newGamma[px] = Math.Max(raw, newGamma[px - 1]);
                // 可选：限制过度凸起（若新值远高于线性插值，则削峰）
                if (px < 255)
                {
                    short linearEst = (short)(newGamma[px - 1] + (newGamma[255] - newGamma[0]) / 255.0);
                    newGamma[px] = (short)Math.Min(newGamma[px], linearEst * 2); // 限制增长速率
                }
            }

            // 6. 写回底层数据
            for (int px = 0; px < 256; px++)
                _yGamma.YGammaTable[px] = newGamma[px];

            // 7. 同步关键点（确保UI显示正确）
            UpdateKeyPointsFromUnderlying();
        }

        /// <summary>
        /// 基于X轴距离计算像素权重（高斯衰减，sigma = 影响半径对应的X范围）
        /// </summary>
        private double[] ComputePixelWeightsByXDistance(int draggedIndex, int[] keyX)
        {
            double[] weights = new double[256];
            int dragX = keyX[draggedIndex];
            double sigma = (keyX[Math.Min(keyX.Length - 1, draggedIndex + _keyPointInfluenceRadius)] -
                            keyX[Math.Max(0, draggedIndex - _keyPointInfluenceRadius)]) / 2.0;
            if (sigma < 1) sigma = 1;

            for (int px = 0; px < 256; px++)
            {
                double dist = Math.Abs(px - dragX);
                if (dist > sigma * 3)
                    weights[px] = 0;
                else
                    weights[px] = Math.Exp(-(dist * dist) / (2 * sigma * sigma));
            }
            // 归一化? 不需要，权重用于线性插值
            return weights;
        }

        /// <summary>
        /// 全局三次自然样条插值（基于所有关键点）
        /// </summary>
        private short[] GlobalCubicSplineInterpolate(int[] x, short[] y, int outputPoints)
        {
            int n = x.Length;
            double[] h = new double[n - 1];
            for (int i = 0; i < n - 1; i++) h[i] = x[i + 1] - x[i];

            double[] alpha = new double[n];
            for (int i = 1; i < n - 1; i++)
                alpha[i] = (3.0 / h[i]) * (y[i + 1] - y[i]) - (3.0 / h[i - 1]) * (y[i] - y[i - 1]);

            double[] l = new double[n];
            double[] mu = new double[n];
            double[] z = new double[n];
            l[0] = 1; mu[0] = 0; z[0] = 0;
            for (int i = 1; i < n - 1; i++)
            {
                l[i] = 2 * (x[i + 1] - x[i - 1]) - h[i - 1] * mu[i - 1];
                mu[i] = h[i] / l[i];
                z[i] = (alpha[i] - h[i - 1] * z[i - 1]) / l[i];
            }
            l[n - 1] = 1; z[n - 1] = 0;
            double[] c = new double[n];
            double[] b = new double[n - 1];
            double[] d = new double[n - 1];
            c[n - 1] = 0;
            for (int j = n - 2; j >= 0; j--)
            {
                c[j] = z[j] - mu[j] * c[j + 1];
                b[j] = (y[j + 1] - y[j]) / h[j] - h[j] * (c[j + 1] + 2 * c[j]) / 3;
                d[j] = (c[j + 1] - c[j]) / (3 * h[j]);
            }

            short[] result = new short[outputPoints];
            for (int px = 0; px < outputPoints; px++)
            {
                int segment = 0;
                for (int i = 0; i < n - 1; i++)
                    if (px >= x[i] && px <= x[i + 1]) { segment = i; break; }
                double dx = px - x[segment];
                double value = y[segment] + b[segment] * dx + c[segment] * dx * dx + d[segment] * dx * dx * dx;
                result[px] = (short)Math.Max(0, Math.Min(1023, Math.Round(value)));
            }
            return result;
        }

        private void UpdateKeyPointsFromUnderlying()
        {
            for (int i = 0; i < _yGammaTable.Count; i++)
            {
                int xVal = _yGammaKeyPointXValues[i];
                _yGammaTable[i] = new KeyValuePair<int, short>(xVal, _yGamma.YGammaTable[xVal]);
            }
        }

        /// <summary>
        /// 使用可调节影响范围的关键点距离加权样条
        /// </summary>
        private void RecalculateGammaTableWithAdjustableKeyPointWeight(int draggedIndex)
        {
            int totalPoints = _yGammaTable.Count;

            // 边界检查
            if (draggedIndex == 0 || draggedIndex == totalPoints - 1)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 起点和终点不可拖动");

                if (draggedIndex == 0)
                {
                    _yGammaTable[0] = new KeyValuePair<int, short>(0, 0);
                    _yGamma.YGammaTable[0] = 0;
                }
                else
                {
                    _yGammaTable[totalPoints - 1] = new KeyValuePair<int, short>(255, 1023);
                    _yGamma.YGammaTable[255] = 1023;
                }

                UpdateFullGammaCurve();
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"🎯 可调关键点加权样条: 拖动点={draggedIndex}, " +
                $"影响半径={_keyPointInfluenceRadius}个关键点");

            int startX = 0;
            short startY = _yGammaTable[0].Value;
            int dragX = _yGammaKeyPointXValues[draggedIndex];
            short dragY = _yGammaTable[draggedIndex].Value;
            int endX = 255;
            short endY = _yGammaTable[totalPoints - 1].Value;

            // 确保单调性
            dragY = (short)Math.Max(startY + 1, Math.Min(endY - 1, dragY));
            _yGammaTable[draggedIndex] = new KeyValuePair<int, short>(dragX, dragY);
            _yGamma.YGammaTable[dragX] = dragY;

            // 保存旧曲线
            short[] oldGammaTable = new short[256];
            Array.Copy(_yGamma.YGammaTable, oldGammaTable, 256);

            // 计算关键点权重
            double[] keyPointWeights = new double[totalPoints];
            double sigma = _keyPointInfluenceRadius / 2.0;

            for (int i = 0; i < totalPoints; i++)
            {
                int indexDistance = Math.Abs(i - draggedIndex);

                if (i == 0 || i == totalPoints - 1)
                {
                    keyPointWeights[i] = 0.0;  // 起点终点固定
                }
                else if (indexDistance == 0)
                {
                    keyPointWeights[i] = 1.0;  // 拖动点完全调整
                }
                else if (indexDistance <= _keyPointInfluenceRadius)
                {
                    // 高斯衰减
                    keyPointWeights[i] = Math.Exp(-(indexDistance * indexDistance) / (2 * sigma * sigma));
                }
                else
                {
                    keyPointWeights[i] = 0.0;  // 超出范围
                }
            }

            // 输出权重分布
            System.Diagnostics.Debug.WriteLine($"  权重分布:");
            for (int i = 0; i < totalPoints; i++)
            {
                string bar = new string('█', (int)(keyPointWeights[i] * 10));
                System.Diagnostics.Debug.WriteLine(
                    $"    [{i:D2}] X={_yGammaKeyPointXValues[i]:D3}: {keyPointWeights[i]:F2} {bar}");
            }

            // 插值像素权重
            double[] pixelWeights = new double[256];
            for (int px = 0; px < 256; px++)
            {
                int leftIndex = 0;
                int rightIndex = totalPoints - 1;

                for (int i = 0; i < totalPoints - 1; i++)
                {
                    if (px >= _yGammaKeyPointXValues[i] && px <= _yGammaKeyPointXValues[i + 1])
                    {
                        leftIndex = i;
                        rightIndex = i + 1;
                        break;
                    }
                }

                int leftX = _yGammaKeyPointXValues[leftIndex];
                int rightX = _yGammaKeyPointXValues[rightIndex];
                double leftWeight = keyPointWeights[leftIndex];
                double rightWeight = keyPointWeights[rightIndex];

                if (rightX == leftX)
                {
                    pixelWeights[px] = leftWeight;
                }
                else
                {
                    double t = (double)(px - leftX) / (rightX - leftX);
                    pixelWeights[px] = leftWeight * (1 - t) + rightWeight * t;
                }
            }

            // 计算三点样条目标曲线
            double[] x = { startX, dragX, endX };
            double[] y = { startY, dragY, endY };

            int n = 3;
            double[] h = { x[1] - x[0], x[2] - x[1] };

            if (h[0] <= 0 || h[1] <= 0) return;

            double[] l = new double[n];
            double[] mu = new double[n];
            double[] z = new double[n];
            double[] c = new double[n];
            double[] b = new double[n - 1];
            double[] d = new double[n - 1];

            l[0] = 1; mu[0] = 0; z[0] = 0;
            l[1] = 2 * (x[2] - x[0]) - h[0] * mu[0];
            if (Math.Abs(l[1]) < 1e-10) return;

            mu[1] = h[1] / l[1];
            double alpha1 = (3.0 / h[1]) * (y[2] - y[1]) - (3.0 / h[0]) * (y[1] - y[0]);
            z[1] = (alpha1 - h[0] * z[0]) / l[1];
            l[2] = 1; z[2] = 0; c[2] = 0;

            c[1] = z[1] - mu[1] * c[2];
            c[0] = z[0] - mu[0] * c[1];
            b[0] = (y[1] - y[0]) / h[0] - h[0] * (c[1] + 2 * c[0]) / 3;
            b[1] = (y[2] - y[1]) / h[1] - h[1] * (c[2] + 2 * c[1]) / 3;
            d[0] = (c[1] - c[0]) / (3 * h[0]);
            d[1] = (c[2] - c[1]) / (3 * h[1]);

            short[] targetSpline = new short[256];
            for (int px = 0; px < 256; px++)
            {
                int interval = px <= dragX ? 0 : 1;
                double dx = px - x[interval];
                double value = y[interval] + b[interval] * dx
                                          + c[interval] * dx * dx
                                          + d[interval] * dx * dx * dx;
                targetSpline[px] = (short)Math.Max(0, Math.Min(1023, Math.Round(value)));
            }

            // 混合新旧曲线
            short previousValue = startY;
            for (int px = 0; px < 256; px++)
            {
                double weight = pixelWeights[px];
                double blendedValue = targetSpline[px] * weight + oldGammaTable[px] * (1 - weight);
                short newValue = (short)Math.Max(0, Math.Min(1023, Math.Round(blendedValue)));

                if (newValue < previousValue)
                    newValue = previousValue;

                _yGamma.YGammaTable[px] = newValue;
                previousValue = newValue;
            }

            // 更新关键点
            for (int i = 0; i < _yGammaTable.Count; i++)
            {
                int xValue = _yGammaKeyPointXValues[i];
                if (xValue >= 0 && xValue < 256)
                {
                    _yGammaTable[i] = new KeyValuePair<int, short>(
                        xValue, _yGamma.YGammaTable[xValue]);
                }
            }
        }
        
        */

        /*
        /// <summary>
        /// 使用可调节影响范围的关键点距离加权（增量扩散 + 单调Hermite插值）
        /// 完美支持上凸下凹等复杂曲线，无平顶死区
        /// </summary>
        private void RecalculateGammaTableWithAdjustableKeyPointWeight(int draggedIndex)
        {
            try
            {
                int totalPoints = _yGammaTable.Count;

                // 1. 边界强制锁定
                if (draggedIndex == 0 || draggedIndex == totalPoints - 1)
                {
                    _yGammaTable[0] = new KeyValuePair<int, short>(0, 0);
                    _yGamma.YGammaTable[0] = 0;
                    _yGammaTable[totalPoints - 1] = new KeyValuePair<int, short>(255, 1023);
                    _yGamma.YGammaTable[255] = 1023;
                    UpdateFullGammaCurve();
                    return;
                }

                int dragX = _yGammaKeyPointXValues[draggedIndex];
                short newDragY = _yGammaTable[draggedIndex].Value;

                // 获取拖动前该点的旧Y值（用于计算增量）
                short oldDragY = _yGamma.YGammaTable[dragX];
                short deltaY = (short)(newDragY - oldDragY);

                // 确保拖动点自身不越界
                short startY = _yGammaTable[0].Value;
                short endY = _yGammaTable[totalPoints - 1].Value;
                newDragY = (short)Math.Max(startY + 1, Math.Min(endY - 1, newDragY));
                _yGammaTable[draggedIndex] = new KeyValuePair<int, short>(dragX, newDragY);

                // 2. 计算X轴物理距离的高斯权重参数
                // 根据关键点平均间距将"影响半径(点数)"转换为"X轴像素距离的Sigma"
                double averageSpacing = 255.0 / Math.Max(1, totalPoints - 1);
                double sigmaX = (_keyPointInfluenceRadius * averageSpacing) / 2.0;
                if (sigmaX < 1.0) sigmaX = 1.0; // 防止除零和权重过窄

                // 3. 关键点级别：增量高斯扩散
                // 保存旧的关键点Y值
                short[] oldKeyPointYs = new short[totalPoints];
                for (int i = 0; i < totalPoints; i++)
                {
                    oldKeyPointYs[i] = _yGammaTable[i].Value;
                }

                // 应用增量到受影响的关键点
                for (int i = 1; i < totalPoints - 1; i++)
                {
                    if (i == draggedIndex) continue; // 自身已经设置过

                    int currentX = _yGammaKeyPointXValues[i];
                    double distanceX = Math.Abs(currentX - dragX);

                    // 使用连续高斯衰减，不硬性截断
                    double weight = Math.Exp(-(distanceX * distanceX) / (2.0 * sigmaX * sigmaX));

                    // 极小权重忽略，提升性能
                    if (weight < 0.001) continue;

                    double appliedDelta = deltaY * weight;
                    short newY = (short)Math.Round(oldKeyPointYs[i] + appliedDelta);

                    // 限幅保护
                    newY = (short)Math.Max(1, Math.Min((ushort)1022, newY));
                    _yGammaTable[i] = new KeyValuePair<int, short>(currentX, newY);
                }

                // 4. 强制所有关键点严格单调递增（解决拖动造成的交叉问题）
                for (int i = 1; i < totalPoints; i++)
                {
                    short prevY = _yGammaTable[i - 1].Value;
                    short currY = _yGammaTable[i].Value;

                    if (currY <= prevY)
                    {
                        //// 强制比前一个点至少大1，保证单调性
                        //currY = (short)(prevY + 1);
                        //// 如果超出上限，需要从后往前回退调整（极端情况）
                        //if (currY > 1023)
                        //{
                        //    for (int j = i; j >= 0; j--)
                        //    {
                        //        short val = (short)(1023 - (i - j));
                        //        _yGammaTable[j] = new KeyValuePair<int, short>(_yGammaKeyPointXValues[j], val);
                        //    }
                        //}
                        //else
                        //{
                        //    _yGammaTable[i] = new KeyValuePair<int, short>(_yGammaKeyPointXValues[i], currY);
                        //}
                        currY = (short)(prevY + 1); // 严格单调，至少差1
                        _yGammaTable[i] = new KeyValuePair<int, short>(_yGammaKeyPointXValues[i], currY);
                    }
                }

                // 反向遍历，防止超出1023上限（如果超出，往前推挤）
                for (int i = totalPoints - 2; i >= 0; i--)
                {
                    short nextY = _yGammaTable[i + 1].Value;
                    short currY = _yGammaTable[i].Value;

                    if (currY >= nextY)
                    {
                        currY = (short)(nextY - 1); // 严格单调
                        _yGammaTable[i] = new KeyValuePair<int, short>(_yGammaKeyPointXValues[i], currY);
                    }
                }

                // 5. 使用单调三次 Hermite 插值生成完整的256点曲线
                GenerateMonotonicHermiteCurve();
            }
            catch (Exception e) { 
                System.Diagnostics.Debug.WriteLine($"❌ RecalculateGammaTableWithAdjustableKeyPointWeight 错误: {e}");
            }
        }

        /// <summary>
        /// 根据当前 _yGammaTable 关键点，使用保单调性的 Hermite 插值生成完整曲线
        /// 彻底解决"平顶死区"和"上凸下凹"变形问题
        /// </summary>
        private void GenerateMonotonicHermiteCurve()
        {
            int n = _yGammaTable.Count;
            if (n < 2) return;

            int[] x = new int[n];
            short[] y = new short[n];
            for (int i = 0; i < n; i++)
            {
                x[i] = _yGammaKeyPointXValues[i];
                y[i] = _yGammaTable[i].Value;
            }

            // 计算相邻点间的斜率 (Delta Y / Delta X)
            double[] h = new double[n - 1];
            double[] delta = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                h[i] = x[i + 1] - x[i];
                delta[i] = (y[i + 1] - y[i]) / h[i];
            }

            // 计算每个关键点的切线 m (使用 Fritsch-Carlson 方法保证单调性)
            double[] m = new double[n];
            m[0] = delta[0];
            m[n - 1] = delta[n - 2];

            for (int i = 1; i < n - 1; i++)
            {
                if (delta[i - 1] * delta[i] <= 0)
                {
                    // 局部极值点（拐点），切线设为0，防止过冲产生非单调
                    m[i] = 0.0;
                }
                else
                {
                    // 使用调和平均数平滑切线，防止拐点处出现平顶
                    double w1 = 2 * h[i] + h[i - 1];
                    double w2 = h[i] + 2 * h[i - 1];
                    m[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]);
                }
            }

            // 插值填充 256 个像素点
            short previousValue = y[0];
            _yGamma.YGammaTable[0] = y[0];

            for (int px = 1; px < 256; px++)
            {
                // 找到当前像素所在的区间
                int seg = 0;
                for (int i = 0; i < n - 1; i++)
                {
                    if (px >= x[i] && px <= x[i + 1])
                    {
                        seg = i;
                        break;
                    }
                }

                double t = (px - x[seg]) / h[seg];

                // Hermite 基函数
                double t2 = t * t;
                double t3 = t2 * t;
                double h00 = 2 * t3 - 3 * t2 + 1;
                double h10 = t3 - 2 * t2 + t;
                double h01 = -2 * t3 + 3 * t2;
                double h11 = t3 - t2;

                // 计算插值
                double value = h00 * y[seg]
                             + h10 * h[seg] * m[seg]
                             + h01 * y[seg + 1]
                             + h11 * h[seg] * m[seg + 1];

                short newValue = (short)Math.Max(0, Math.Min(1023, Math.Round(value)));

                // 理论上由于单调性保证，这里不需要再强制截断，但以防浮点误差加一层安全网
                //if (newValue < previousValue) newValue = previousValue;

                // 【核心防御】强制严格单调递增，绝不允许出现平顶（相等），否则硬件报OSException
                if (newValue <= previousValue)
                {
                    newValue = (short)(previousValue + 1);
                }

                // 上限截断（如果前面强行+1导致超限）
                if (newValue > 1023)
                {
                    newValue = 1023;
                }

                _yGamma.YGammaTable[px] = newValue;
                previousValue = newValue;
            }
        }
        */


        /// <summary>
        /// 是否启用平滑处理（三次样条插值）
        /// </summary>
        public bool EnableSmoothProcessing
        {
            get { return _enableSmoothProcessing; }
            set
            {
                if (_enableSmoothProcessing == value)
                    return;  // 值未改变，直接返回
                _enableSmoothProcessing = value;
                RaisePropertyChanged("EnableSmoothProcessing");

                // 切换时重新计算曲线
                if (_yGammaTable.Count > 0 && _yGammaTable.Count == _yGammaKeyPointXValues.Length)
                {
                    // 暂停定时器，防止在计算过程中触发
                    _throttleTimer.Stop();
                    _pendingUpdate = false;

                    try
                    {
                        if (_enableSmoothProcessing)
                        {
                            RecalculateGammaTableWithSpline();
                        }
                        else
                        {
                            // 恢复线性插值：重新对所有段进行线性插值
                            for (int i = 0; i < _yGammaKeyPointXValues.Length - 1; i++)
                            {
                                InterpolateBetweenKeyPoints(i, i + 1);
                            }
                        }
                    }
                    finally
                    {
                        // 定时器不需要重新启动，下次拖动时会启动
                    }

                    // 通过触发 CollectionChanged 来通知 UI 更新
                    // 创建一个虚拟的替换操作来刷新 UI
                    if (_yGammaTable.Count > 0)
                    {
                        var firstItem = _yGammaTable[0];
                        _yGammaTable[0] = firstItem;  // 这会触发 CollectionChanged
                    }
                }
            }
        }

        public bool EnableAnchorPointSmoothing
        {
            get { return _enableAnchorPointSmoothing; }
            set
            {
                if (_enableAnchorPointSmoothing == value)
                    return;
                _enableAnchorPointSmoothing = value;
                RaisePropertyChanged("EnableAnchorPointSmoothing");

                // 切换时重新计算曲线
                if (_yGammaTable.Count > 0 && _yGammaTable.Count == _yGammaKeyPointXValues.Length)
                {
                    // 暂停定时器，防止在计算过程中触发
                    _throttleTimer.Stop();
                    _pendingUpdate = false;

                    try
                    {
                        if (_enableAnchorPointSmoothing)
                        {
                            // 使用三点锚定平滑
                            //int draggedIndex = GetLastModifiedKeyPointIndex();

                        }
                        else
                        {
                            // 恢复线性插值：重新对所有段进行线性插值
                            for (int i = 0; i < _yGammaKeyPointXValues.Length - 1; i++)
                            {
                                InterpolateBetweenKeyPoints(i, i + 1);
                            }
                        }
                    }
                    finally
                    {
                        // 定时器不需要重新启动，下次拖动时会启动
                    }

                    // 通过触发 CollectionChanged 来通知 UI 更新
                    // 创建一个虚拟的替换操作来刷新 UI
                    if (_yGammaTable.Count > 0)
                    {
                        var firstItem = _yGammaTable[0];
                        _yGammaTable[0] = firstItem;  // 这会触发 CollectionChanged
                    }
                }
            }
        }


        void YGammaPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "YGammaTable")
            {
                System.Diagnostics.Debug.WriteLine($"=== 开始重建关键点集合 ===");

                /*
                // 检查底层数据是否就绪
                if (_yGamma.YGammaTable == null || _yGamma.YGammaTable.Length < 256)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"⏳ 底层数据未就绪，延迟重建 (当前长度: {_yGamma.YGammaTable?.Length ?? 0})");
                    return;  // 等待下次通知
                }
                //foreach (var xValue in _yGammaKeyPointXValues)
                //{
                //    KeyValuePair<int, short>? item = _yGammaTable.FirstOrDefault(pair => pair.Key == xValue);
                //    if (item != null)
                //    {
                //        _yGammaTable.Remove(item.Value);
                //    }
                //    _yGammaTable.Add(new KeyValuePair<int, short>(xValue, _yGamma.YGammaTable[xValue]));
                //}

                // 暂停定时器，防止在重建集合过程中触发
                _throttleTimer.Stop();
                _pendingUpdate = false;

                // 关键点：重建前移除集合变更监听，防止触发插值死循环
                _yGammaTable.CollectionChanged -= YGammaTable_CollectionChanged;
                try
                {
                    System.Diagnostics.Debug.WriteLine($"=== 开始重建关键点集合 ===");
                    System.Diagnostics.Debug.WriteLine($"底层数组长度: {_yGamma.YGammaTable.Length}");

                    // 简化逻辑：直接重建关键点集合
                    _yGammaTable.Clear();

                    int skippedCount = 0;
                    List<int> skippedXValues = new List<int>();

                    foreach (var xValue in _yGammaKeyPointXValues)
                    {
                        //if (xValue >= 0 && xValue < _yGamma.YGammaTable.Length)
                        //{
                        //    _yGammaTable.Add(new KeyValuePair<int, short>(xValue, _yGamma.YGammaTable[xValue]));
                        //}
                        // 防御性检查：如果底层表长度异常，截断或抛出异常，避免静默丢失关键点
                        if (xValue < _yGamma.YGammaTable.Length)
                        {
                            _yGammaTable.Add(new KeyValuePair<int, short>(xValue, _yGamma.YGammaTable[xValue]));
                        }
                        else
                        {
                            skippedCount++;
                            skippedXValues.Add(xValue);
                            System.Diagnostics.Debug.WriteLine(
                                $"⚠️ 跳过关键点 X={xValue} (数组长度: {_yGamma.YGammaTable.Length})");
                        }
                    }
                    // ============ 新增调试代码 开始 ============
                    //System.Diagnostics.Debug.WriteLine($"--- YGammaTable 更新调试 (共 {_yGammaTable.Count} 点) ---");
                    //for (int i = 0; i < _yGammaTable.Count; i++)
                    //{
                    //    var point = _yGammaTable[i];
                    //    System.Diagnostics.Debug.WriteLine($"[Index:{i:D2}] X={point.Key:D3}, Y={point.Value:D4}");
                    //}
                    //System.Diagnostics.Debug.WriteLine("---------------------------------------------");
                    // ============ 新增调试代码 结束 ============
                    // 验证集合完整性
                    if (_yGammaTable.Count != _yGammaKeyPointXValues.Length)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"❌ 关键点集合不完整: 期望={_yGammaKeyPointXValues.Length}, " +
                            $"实际={_yGammaTable.Count}, 跳过={skippedCount}个");

                        if (skippedXValues.Count > 0)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"跳过的X值: [{string.Join(", ", skippedXValues)}]");
                        }

                        // 尝试修复：使用默认值填充缺失的关键点
                        foreach (var xValue in skippedXValues)
                        {
                            short defaultValue = 0;
                            if (xValue >= 0 && xValue < _yGamma.YGammaTable.Length)
                            {
                                defaultValue = _yGamma.YGammaTable[xValue];
                            }
                            else if (_yGammaTable.Count > 0)
                            {
                                // 使用最后一个有效值
                                defaultValue = _yGammaTable[_yGammaTable.Count - 1].Value;
                            }

                            _yGammaTable.Add(new KeyValuePair<int, short>(xValue, defaultValue));
                            System.Diagnostics.Debug.WriteLine(
                                $"🔧 已修复关键点 X={xValue}, Y={defaultValue} (使用默认值)");
                        }
                    }

                    if (_isUpdatingBezierPoints == false)
                    {
                        _curveEditor.LoadGammaTable(_yGamma.YGammaTable);  // 通知编辑器加载新数据
                    }

                    System.Diagnostics.Debug.WriteLine($"✅ 关键点集合重建完成: {_yGammaTable.Count} 个点");
                }
                finally
                {
                    // 确保定时器状态正确
                    // 注意：不需要重新启动，因为下次拖动时会启动 
                    // 重建后恢复监听
                    _yGammaTable.CollectionChanged += YGammaTable_CollectionChanged;
                }

                */
            }
            if (e.PropertyName == "Y_Gamma_Table")
            {
                System.Diagnostics.Debug.WriteLine("🔄 Y_Gamma_Table changed");
                RaisePropertyChanged("Y_Gamma_Table");
            }
            //RaisePropertyChanged(e.PropertyName);
        }

        void YGammaTablePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Y_Gamma_Table")
            {
                System.Diagnostics.Debug.WriteLine("Y_Gamma_Table changed");
                if (_isSaveYGammaTable == false)
                    SetUpgradeYGamma();
                else
                    _isSaveYGammaTable = false;
            }
        }

        public Processor IspProcessor
        {
            get;
            set;
        }

        public string CoordinatesText
        {
            get { return _coordinatesText; }
            private set { _coordinatesText = value; RaisePropertyChanged("CoordinatesText"); }
        }

        public double P0Y { get { return _curveEditor.P0Y; } }
        public double P1X { get { return _curveEditor.P1X; } }
        public double P1Y { get { return _curveEditor.P1Y; } }
        public double P2X { get { return _curveEditor.P2X; } }
        public double P2Y { get { return _curveEditor.P2Y; } }
        public double P3Y { get { return _curveEditor.P3Y; } }
        public double MaxX { get { return _curveEditor.MaxX; } }
        public double MaxY { get { return _curveEditor.MaxY; } }
        public short[] GammaTable { get { return _curveEditor.GammaTable; } }


        public RelayCommand LoadYGammaTableFromFileCommand
        {
            get { return _loadYGammaTableFromFileCommand; }
        }

        public RelayCommand SaveYGammaTableToFileCommand
        {
            get { return _saveYGammaTableToFileCommand; }
        }

        public RelayCommand LoadYGammaTableFromDeviceCommand
        {
            get { return _loadYGammaTableFromDeviceCommand; }
        }

        public RelayCommand SaveYGammaTableToDeviceCommand
        {
            get { return _saveYGammaTableToDeviceCommand; }
        }

        public RelayCommand ResetYGammaTableCommand
        {
            get { return _resetYGammaTableToDeviceCommand; }
        }

        public RelayCommand ViewPreviousIspStepCommand
        {
            get { return _viewPreviousIspStep; }
        }


        // 在初始化或插值更新时，同步刷新此集合
        private void UpdateFullGammaCurve()
        {
            // 方案1：使用临时列表，一次性替换
            var newCurve = new List<KeyValuePair<int, short>>(256);
            for (int i = 0; i < 256; i++)
            {
                newCurve.Add(new KeyValuePair<int, short>(i, _yGamma.YGammaTable[i]));
            }

            // 暂停事件通知
            var tempCurve = FullGammaCurve;
            FullGammaCurve = new ObservableCollection<KeyValuePair<int, short>>(newCurve);
            RaisePropertyChanged("FullGammaCurve");
        }

        public ObservableCollection<KeyValuePair<int, short>> YGammaTable
        {
            get
            {
                return _yGammaTable;
            }
            set
            {
                if (_yGammaTable != value)
                {
                    _yGammaTable = value;
                    for (int i = 1; i < _yGammaTable.Count; i++)
                    {
                        _yGamma.YGammaTable[i] = _yGammaTable[i].Value;
                    }
                }
            }
        }


        public byte PadNum
        {
            get { return _yGamma.PadNum; }
            set { _yGamma.PadNum = value; }
        }

        public void SetBezierCurveEditor(BezierCurveEditor bezier)
        {
            _curveEditor = bezier;

            _curveEditor.SetDefaultGammaTable(_gammaTable.Y_Gamma_Table);
            _curveEditor.Loaded += OnCurveEditorLoaded;
            _curveEditor.CurveChanged += OnCurveChanged;
            isbezierLoad = true;
        }

        public void SetUpgradeYGamma()
        {
            if (isbezierLoad)
            {
                _curveEditor.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _curveEditor.SetDefaultGammaTable(_gammaTable.Y_Gamma_Table);
                }));
            }
        }

        private void OnCurveEditorLoaded(object sender, RoutedEventArgs e)
        {
            UpdateCoordinatesDisplay();
        }


        private void OnCurveChanged(object sender, EventArgs e)
        {
            UpdateCoordinatesDisplay();
            RaisePropertyChanged("P0Y");
            RaisePropertyChanged("P1X");
            RaisePropertyChanged("P1Y");
            RaisePropertyChanged("P2X");
            RaisePropertyChanged("P2Y");
            RaisePropertyChanged("P3Y");
            RaisePropertyChanged("MaxY");
        }

        private void UpdateCoordinatesDisplay()
        {
            var sb = new StringBuilder();

            sb.AppendLine("控制点坐标:");
            sb.AppendLine(string.Format("  P0: (0, {0:F0}) [固定]", P0Y));
            sb.AppendLine(string.Format("  P1: ({0:F0}, {1:F0})", P1X, P1Y));
            sb.AppendLine(string.Format("  P2: ({0:F0}, {1:F0})", P2X, P2Y));
            sb.AppendLine(string.Format("  P3: ({0:F0}, {1:F0}) [固定]", MaxX, P3Y));
            sb.AppendLine();

            short[] table = GammaTable;
            _isUpdatingBezierPoints = true;
            sb.AppendLine("Gamma表数据:");
            for (int i = 0; i < table.Length; i++)
            {
                sb.AppendLine(string.Format("  [{0}] = {1} (0x{1:X})", i, table[i]));
                _yGamma.YGammaTable[i] = table[i];
                _gammaTable.Y_Gamma_Table[i] = (byte)table[i];
            }
            //_yGamma.YGammaTable = table;

            CoordinatesText = sb.ToString();
            _isUpdatingBezierPoints = false;
        }

        public void OnReset()
        {
            _curveEditor.ResetControlPoints();
            UpdateCoordinatesDisplay();
        }

        public void LoadYGammaTableFromFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                DefaultExt = ".txt",
                Title = "导入曲线数据"
            };
            if (dialog.ShowDialog() != true) return;
            //string directory = System.IO.Path.GetDirectoryName(dialog.FileName);
            //string finalFilePath = System.IO.Path.Combine(directory, "gamma_data.txt");
            //_yGamma.LoadYGammaTableFromFile(finalFilePath);
            OnImport(dialog.FileName);
            //UpdateFullGammaCurve();
        }

        public void SaveYGammaTableToFile()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|C头文件 (*.h)|*.h|CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = "gamma_table"
            };
            if (dialog.ShowDialog() != true) return;

            string directory = System.IO.Path.GetDirectoryName(dialog.FileName);
            string finalFilePath = System.IO.Path.Combine(directory, "gamma_data.txt");
            _yGamma.SaveYGammaTableToFile(finalFilePath);
            OnExport(dialog.FileName);
        }

        private void ViewPreviousIspStep()
        {
            if (_ispStepsWindow != null)
            {
                _ispStepsWindow.Show();
                _ispStepsWindow.Activate();
            }
            else
            {
                _ispStepsWindow = new IspStepsWindow();
                _ispStepsWindow.DataContext = new IspStepsWindowViewModel(IspProcessor, DeviceConfig.Isp.IspModule.YGamma);
                _ispStepsWindow.Show();
            }
        }

        private void OnImport(string FileName)
        {
            try
            {
                string[] lines = File.ReadAllLines(FileName, Encoding.UTF8);

                double p1x = double.NaN, p1y = double.NaN, p2x = double.NaN, p2y = double.NaN;
                bool foundControlPoints = false;

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    if (line.StartsWith("P1:") || line.StartsWith("P1："))
                    {
                        string coordPart = line.Substring(3).Trim();
                        double cx, cy;
                        if (TryParseCoord(coordPart, out cx, out cy))
                        {
                            p1x = cx;
                            p1y = cy;
                            foundControlPoints = true;
                        }
                    }
                    else if (line.StartsWith("P2:") || line.StartsWith("P2："))
                    {
                        string coordPart = line.Substring(3).Trim();
                        double cx, cy;
                        if (TryParseCoord(coordPart, out cx, out cy))
                        {
                            p2x = cx;
                            p2y = cy;
                            foundControlPoints = true;
                        }
                    }
                }

                if (foundControlPoints)
                {
                    double maxY = MaxY;
                    if (!double.IsNaN(p1x)) _curveEditor.P1X = Math.Max(0, Math.Min(MaxX, p1x));
                    if (!double.IsNaN(p1y)) _curveEditor.P1Y = Math.Max(0, Math.Min(maxY, p1y));
                    if (!double.IsNaN(p2x)) _curveEditor.P2X = Math.Max(0, Math.Min(MaxX, p2x));
                    if (!double.IsNaN(p2y)) _curveEditor.P2Y = Math.Max(0, Math.Min(maxY, p2y));

                    _curveEditor.UpdateGammaTable();
                    UpdateCoordinatesDisplay();

                    MessageBox.Show(Application.Current.MainWindow,
                        string.Format("已成功导入控制点数据:\nP1: ({0:F0}, {1:F0})\nP2: ({2:F0}, {3:F0})",
                            P1X, P1Y, P2X, P2Y),
                        "导入成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                string allText = File.ReadAllText(FileName, Encoding.UTF8);
                var yValues = new System.Collections.Generic.List<short>();
                bool isSingleLineCsv = false;

                lines = allText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 1)
                {
                    string singleLine = lines[0].Trim();
                    string[] allParts = singleLine.Split(new[] { ',', ' ', '\t', ';' },
                        StringSplitOptions.RemoveEmptyEntries);
                    if (allParts.Length >= 128)
                    {
                        isSingleLineCsv = true;
                        foreach (string part in allParts)
                        {
                            short val;
                            if (TryParseShort(part, out val))
                                yValues.Add(val);
                        }
                    }
                }

                if (!isSingleLineCsv)
                {
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (string.IsNullOrEmpty(line)) continue;
                        if (line.StartsWith("X") || line.StartsWith("x") ||
                            line.StartsWith("控制") || line.StartsWith("P0") ||
                            line.StartsWith("P1") || line.StartsWith("P2") ||
                            line.StartsWith("P3") || line.StartsWith("256") ||
                            line.StartsWith("#"))
                            continue;

                        string[] parts = line.Split(new char[] { ',', ' ', '\t', ';' },
                            StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length >= 2)
                        {
                            short yVal;
                            if (TryParseShort(parts[parts.Length - 1], out yVal))
                                yValues.Add(yVal);
                        }
                        else if (parts.Length == 1)
                        {
                            short yVal;
                            if (TryParseShort(parts[0], out yVal))
                                yValues.Add(yVal);
                        }
                    }
                }

                if (yValues.Count >= 4)
                {
                    string adaptMsg = DetectAndAdaptDataRange(yValues, MaxY);
                    _curveEditor.LoadGammaTable(yValues.ToArray());
                    UpdateCoordinatesDisplay();

                    MessageBox.Show(string.Format("已从 {0} 个数据点拟合控制点:\nP1: ({1:F0}, {2:F0})\nP2: ({3:F0}, {4:F0})\n\n{5}",
                            yValues.Count, P1X, P1Y, P2X, P2Y, adaptMsg),
                        "导入成功（拟合）",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                MessageBox.Show("无法识别文件中的曲线数据格式。\n\n支持的格式:\n1. 包含 P1/P2 控制点坐标\n2. 包含 X,Y 数据点列表",
                    "导入失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("导入失败:\n{0}", ex.Message),
                    "导入错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnExport(string FileName)
        {
            try
            {
                short[] table = GammaTable;
                string ext = System.IO.Path.GetExtension(FileName).ToLower();
                var sb = new StringBuilder();

                if (ext == ".h")
                {
                    sb.AppendLine("/* Gamma Table - Auto Generated */");
                    sb.AppendLine("#ifndef __GAMMA_TABLE_H__");
                    sb.AppendLine("#define __GAMMA_TABLE_H__");
                    sb.AppendLine();
                    sb.AppendLine("short yGammaTable[256] = ");
                    sb.AppendLine("{");

                    for (int i = 0; i < table.Length; i++)
                    {
                        if (i % 10 == 0 && i > 0)
                            sb.AppendLine();
                        sb.Append(string.Format("0x{0:X3}", (ushort)table[i]));
                        if (i < table.Length - 1)
                            sb.Append(", ");
                    }

                    sb.AppendLine();
                    sb.AppendLine("};");
                    sb.AppendLine();
                    sb.AppendLine("#endif /* __GAMMA_TABLE_H__ */");
                }
                else
                {
                    sb.AppendLine("控制点坐标:");
                    sb.AppendLine(string.Format("P0: (0, {0:F0}) [固定]", P0Y));
                    sb.AppendLine(string.Format("P1: ({0:F0}, {1:F0})", P1X, P1Y));
                    sb.AppendLine(string.Format("P2: ({0:F0}, {1:F0})", P2X, P2Y));
                    sb.AppendLine(string.Format("P3: ({0:F0}, {1:F0}) [固定]", MaxX, P3Y));
                    sb.AppendLine();
                    sb.AppendLine("256个数据点:");
                    sb.AppendLine("X, Y");

                    for (int i = 0; i < table.Length; i++)
                    {
                        sb.AppendLine(string.Format("{0}, {1}", i, table[i]));
                    }
                }

                File.WriteAllText(FileName, sb.ToString(), Encoding.UTF8);

                MessageBox.Show(string.Format("数据已成功导出到:\n{0}", FileName),
                    "导出成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("导出失败:\n{0}", ex.Message),
                    "导出错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool TryParseCoord(string text, out double x, out double y)
        {
            x = 0;
            y = 0;

            string s = text.Replace("[固定]", "").Replace("(", "").Replace(")", "").Replace("，", ",").Trim();

            string[] parts = s.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                bool ok1 = double.TryParse(parts[0], out x);
                bool ok2 = double.TryParse(parts[1], out y);
                return ok1 && ok2;
            }

            return false;
        }

        private enum DataRangeType { Unknown, EightBit, TenBit }

        private string DetectAndAdaptDataRange(List<short> yValues, double systemMaxY)
        {
            if (yValues.Count == 0) return "无数据";

            int maxVal = 0;
            foreach (short v in yValues)
                if (v > maxVal) maxVal = v;

            if (maxVal > 1023)
            {
                for (int i = 0; i < yValues.Count; i++)
                    if (yValues[i] > 1023) yValues[i] = 1023;
                return string.Format("警告: 检测到超出10位范围的值 ({0})，已截断到1023", maxVal);
            }

            DataRangeType detectedType = maxVal <= 255 ? DataRangeType.EightBit : DataRangeType.TenBit;

            if (detectedType == DataRangeType.EightBit)
                return string.Format("检测到8位数据 (0xFF)，MaxY将设置为 255");
            else
                return string.Format("检测到10位数据 (0x3FF)，MaxY将设置为 {0}", maxVal);
        }

        private bool TryParseShort(string text, out short result)
        {
            text = text.Trim();

            if (short.TryParse(text, out result))
                return true;

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
            {
                string hexPart = text.Substring(2);
                if (short.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber, null, out result))
                    return true;
            }

            return false;
        }


        public void LoadYGammaTableFromDevice()
        {
            _yGamma.LoadYGammaTable = "LoadYGammaTable";
        }
        public void SaveYGammaTableToDevice()
        {
            _isSaveYGammaTable = true;
            _gammaTable.Y_Gamma_Table_String = "SaveYGammaTableToDevice";
            _yGamma.SaveYGammaTable = "SaveYGammaTable";
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public override void Cleanup()
        {
            if (_throttleTimer != null)
            {
                _throttleTimer.Stop();
                _throttleTimer.Tick -= ThrottleTimer_Tick;
                _throttleTimer = null;
            }
        }
    }
}
