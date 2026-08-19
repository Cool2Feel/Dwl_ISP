using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Ui.SettingWindow.IspSteps;

namespace ThunderSE.Ui.SettingWindow.YGamma
{
    class YGammaWindowViewModel : ViewModelBase
    {
        private IspStepsWindow _ispStepsWindow;

        private RelayCommand _loadYGammaTableFromFileCommand;
        private RelayCommand _saveYGammaTableToFileCommand;

        private RelayCommand _loadYGammaTableFromDeviceCommand;
        private RelayCommand _saveYGammaTableToDeviceCommand;

        private RelayCommand _viewPreviousIspStep;

        private ThunderSE.DeviceConfig.Isp.YGamma _yGamma;
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

        public ObservableCollection<KeyValuePair<int, short>> BezierControlPoints => _bezierControlPoints;


        // 节流机制相关字段
        private DispatcherTimer _throttleTimer;
        private bool _pendingUpdate = false;
        private const int ThrottleIntervalMs = 100; // 节流间隔：50毫秒

        public YGammaWindowViewModel(Processor ispProcessor)
        {
            IspProcessor = ispProcessor;
            _yGamma = (ThunderSE.DeviceConfig.Isp.YGamma)ispProcessor.RgbFileProcessSteps[IspModule.YGamma];

            _loadYGammaTableFromFileCommand = new RelayCommand(LoadYGammaTableFromFile);
            _saveYGammaTableToFileCommand = new RelayCommand(SaveYGammaTableToFile);

            _loadYGammaTableFromDeviceCommand = new RelayCommand(LoadYGammaTableFromDevice);
            _saveYGammaTableToDeviceCommand = new RelayCommand(SaveYGammaTableToDevice);

            _viewPreviousIspStep = new RelayCommand(ViewPreviousIspStep);

            FullGammaCurve = new ObservableCollection<KeyValuePair<int, short>>();
            _yGamma.PropertyChanged += YGammaPropertyChanged;

            // 初始化节流定时器
            _throttleTimer = new DispatcherTimer();
            _throttleTimer.Interval = TimeSpan.FromMilliseconds(ThrottleIntervalMs);
            _throttleTimer.Tick += ThrottleTimer_Tick;

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
            // 初始化贝塞尔控制点
            InitializeBezierControlPoints();

            _bezierControlPoints.CollectionChanged += BezierControlPoints_CollectionChanged;

            _yGammaTable.CollectionChanged += YGammaTable_CollectionChanged;

        }


        /// <summary>
        /// 初始化贝塞尔控制点（P0/P3 固定端点，P1/P2 为可拖动控制点）
        /// P1 默认在 X=85 处，P2 默认在 X=170 处，Y 为线性中点
        /// </summary>
        private void InitializeBezierControlPoints()
        {
            _bezierControlPoints.Clear();
            if (_yGamma.YGammaTable == null || _yGamma.YGammaTable.Length < 256)
                return;

            short startY = _yGamma.YGammaTable[0];
            short endY = _yGamma.YGammaTable[255];
            short midY = (short)(startY + (endY - startY) / 2);

            _bezierControlPoints.Add(new KeyValuePair<int, short>(0, startY));    // P0 - 起点（固定）
            _bezierControlPoints.Add(new KeyValuePair<int, short>(85, midY));     // P1 - 左侧控制点
            _bezierControlPoints.Add(new KeyValuePair<int, short>(170, midY));    // P2 - 右侧控制点
            _bezierControlPoints.Add(new KeyValuePair<int, short>(255, endY));    // P3 - 终点（固定）
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

            // 记录被修改的关键点索引
            _lastModifiedIndex = e.NewStartingIndex;


            // ================================================
            // YGammaTable 关键点拖拽 → 映射到 Bezier 控制点
            // 使用三点 Bezier 拟合：P0=(0,startY), P3=(255,endY)
            // 计算 P1/P2 的 Y 值使曲线通过拖拽点
            // ================================================
            if (_bezierControlPoints.Count == 4)
            {
                try
                {
                    _isUpdatingBezierPoints = true;
                    try
                    {
                        // 获取拖拽的关键点坐标
                        int dragX = _yGammaKeyPointXValues[_lastModifiedIndex];
                        short dragY = _yGammaTable[_lastModifiedIndex].Value;
                        short startY = _bezierControlPoints[0].Value;
                        short endY = _bezierControlPoints[3].Value;
                        int p1x = _bezierControlPoints[1].Key;
                        int p2x = _bezierControlPoints[2].Key;

                        // 二分法求参数 t，使 Bezier X(t) ≈ dragX
                        double lo = 0, hi = 1, t = 0.5;
                        double omt = 1 - t;
                        for (int iter = 0; iter < 20; iter++)
                        {
                            double xt = 3 * omt * omt * t * p1x + 3 * omt * t * t * p2x + t * t * t * 255;
                            if (Math.Abs(xt - dragX) < 1e-3) break;
                            if (xt < dragX) lo = t;
                            else hi = t;
                            t = (lo + hi) / 2;
                        }

                        // 三点 Bezier 拟合：
                        // dragY = (1-t)³·startY + 3(1-t)²·t·ctrlY + t³·endY
                        // (此时 P1=P2=ctrlY)
                        omt = 1 - t;
                        double denominator = 3 * omt * t;
                        short ctrlY;
                        if (denominator > 1e-10)
                        {
                            ctrlY = (short)Math.Max(0, Math.Min(1023,
                                Math.Round((dragY - omt * omt * omt * startY - t * t * t * endY) / denominator)));
                        }
                        else
                        {
                            // t≈0 或 t≈1 时，使用线性中点
                            ctrlY = (short)(startY + (endY - startY) / 2);
                        }

                        // 更新 P1 和 P2 的 Y 值（保留各自的 X 位置）
                        _bezierControlPoints[1] = new KeyValuePair<int, short>(p1x, ctrlY);
                        _bezierControlPoints[2] = new KeyValuePair<int, short>(p2x, ctrlY);
                    }
                    finally
                    {
                        _isUpdatingBezierPoints = false;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"YGammaTable→Bezier 映射错误: {ex.Message}");
                }
            }

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
        /// 贝塞尔控制点变更处理：当用户拖拽 P1/P2 时触发节流重算
        /// </summary>
        private void BezierControlPoints_CollectionChanged(object sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_isUpdatingBezierPoints) return;

            // 仅处理 Replace 操作（值修改）
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
                return;

            if (e.NewStartingIndex < 0 || e.NewStartingIndex >= _bezierControlPoints.Count)
                return;

            // P0(起点) 和 P3(终点) 不支持拖拽修改
            if (e.NewStartingIndex == 0 || e.NewStartingIndex == 3)
                return;

            if (_isUpdatingFromInterpolation) return;

            try
            {
                _pendingUpdate = true;
                _throttleTimer.Stop();
                _throttleTimer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bezier控制点变更错误: {ex.Message}");
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
                else if(_enableAnchorPointSmoothing)
                {
                    // ✅ 设置标志，防止插值过程中的 CollectionChanged 触发递归
                    _isUpdatingFromInterpolation = true;
                    try
                    {
                        // 获取最后修改的关键点索引
                        //int draggedIndex = GetLastModifiedKeyPointIndex();

                        int draggedIndex = _lastModifiedIndex >= 0
                            ? _lastModifiedIndex
                            : _yGammaTable.Count / 2;

                        //RefitSmoothCurve(draggedIndex);

                        GenerateFullBezierCurve();
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
        /// 使用 4 控制点三次 Bezier 曲线生成完整 256 点 Gamma 表
        ///   - P0(0,startY)/P3(255,endY) 为固定端点
        ///   - P1(x1,y1)/P2(x2,y2) 从 _bezierControlPoints 中读取
        ///   - 二分法求参数 t 使 X(t) ≈ 目标像素位置
        ///   - 三次 Bezier 公式计算 Y 值
        /// 拖动 P1/P2 控制点即可独立调整曲线形状（不依赖曲线上的点）
        /// </summary>
        private void GenerateFullBezierCurve()
        {
            try
            {
                if (_bezierControlPoints.Count != 4)
                {
                    InitializeBezierControlPoints();
                    if (_bezierControlPoints.Count != 4) return;
                }

                // 读取四个控制点
                short p0y = _bezierControlPoints[0].Value;
                int p1x = _bezierControlPoints[1].Key;
                short p1y = _bezierControlPoints[1].Value;
                int p2x = _bezierControlPoints[2].Key;
                short p2y = _bezierControlPoints[2].Value;
                short p3y = _bezierControlPoints[3].Value;

                // 生成 256 点曲线
                _yGamma.YGammaTable[0] = p0y;
                double prevValue = p0y;

                for (int px = 1; px < 256; px++)
                {
                    // 二分法求参数 t，使 Bezier X(t) ≈ 目标 X 位置
                    double lo = 0, hi = 1;
                    double t = 0.5;
                    double omt = 1 - t;
                    for (int iter = 0; iter < 20; iter++)
                    {
                        double xt = 3 * omt * omt * t * p1x +
                                    3 * omt * t * t * p2x +
                                    t * t * t * 255;

                        if (Math.Abs(xt - px) < 1e-3) break;
                        if (xt < px) lo = t;
                        else hi = t;
                        t = (lo + hi) / 2;
                    }

                    // 三次 Bezier 公式计算 Y
                    omt = 1 - t;
                    double value = omt * omt * omt * p0y +
                                   3 * omt * omt * t * p1y +
                                   3 * omt * t * t * p2y +
                                   t * t * t * p3y;

                    short newValue = (short)Math.Max(0, Math.Min(1023, Math.Round(value)));
                    if (newValue < prevValue) newValue = (short)Math.Round(prevValue);

                    _yGamma.YGammaTable[px] = newValue;
                    prevValue = newValue;
                }

                // 同步 YGammaTable（20 关键点）和 FullGammaCurve（256 点）
                UpdateKeyPointsFromUnderlying();
                UpdateFullGammaCurve();
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GenerateFullBezierCurve 错误: {e}");
            }
        }

        /// <summary>
        /// 拖动关键点后重新拟合平滑曲线
        /// 只修改被拖动的关键点，其他关键点保持不变，使用保单调 Hermite 插值重新生成完整曲线
        /// </summary>
        private void RefitSmoothCurve(int draggedIndex)
        {
            try
            {
                int totalPoints = _yGammaTable.Count;
                if (totalPoints < 2) return;

                if (draggedIndex == 0 || draggedIndex == totalPoints - 1)
                {
                    // 端点值不强制设为 0/1023，保持其当前数值
                    _yGamma.YGammaTable[0] = _yGammaTable[0].Value;
                    _yGamma.YGammaTable[255] = _yGammaTable[totalPoints - 1].Value;
                    UpdateFullGammaCurve();
                    return;
                }


                // 三点锚定全局曲线拟合：以起点、拖动点、终点为锚点
                // 使用保单调 Hermite 插值重新计算整条曲线，实现全局弯曲上移效果
                short startY = _yGammaTable[0].Value;
                int dragX = _yGammaKeyPointXValues[draggedIndex];
                short dragY = _yGammaTable[draggedIndex].Value;
                short endY = _yGammaTable[totalPoints - 1].Value;

                // 确保拖动点值在有效范围内（不低于起点、不高于终点）
                dragY = (short)Math.Max(startY + 1, Math.Min(endY - 1, dragY));
                _yGammaTable[draggedIndex] = new KeyValuePair<int, short>(dragX, dragY);

                //// 基于三点生成保单调 Hermite 曲线并写回底层数组
                //GenerateThreePointHermiteCurve(0, startY, dragX, dragY, 255, endY);

                // 基于三点拟合三次 Bezier 曲线并写回底层数组
                // 参考 gamma-255.html 实现：P0(起点)→P1/P2(控制点)→P3(终点)
                GenerateBezierCurve(startY, dragX, dragY, endY);

                //EnforceKeyPointMonotonicity();
                //GenerateMonotonicHermiteCurve();
                UpdateKeyPointsFromUnderlying();
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"❌ RefitSmoothCurve 错误: {e}");
            }
        }

        /// <summary>
        /// 强制所有关键点严格单调递增
        /// </summary>
        private void EnforceKeyPointMonotonicity()
        {
            int totalPoints = _yGammaTable.Count;
            for (int i = 1; i < totalPoints; i++)
            {
                short prevY = _yGammaTable[i - 1].Value;
                short currY = _yGammaTable[i].Value;

                if (currY <= prevY)
                {
                    currY = (short)(prevY + 1);
                    if (currY > 1023)
                    {
                        for (int j = i; j >= 0; j--)
                        {
                            short val = (short)(1023 - (i - j));
                            _yGammaTable[j] = new KeyValuePair<int, short>(
                                _yGammaKeyPointXValues[j], val);
                        }
                    }
                    else
                    {
                        _yGammaTable[i] = new KeyValuePair<int, short>(
                            _yGammaKeyPointXValues[i], currY);
                    }
                }
            }
        }

        /// <summary>
        /// 从底层 Gamma 表同步关键点 Y 值
        /// </summary>
        private void UpdateKeyPointsFromUnderlying()
        {
            for (int i = 0; i < _yGammaTable.Count; i++)
            {
                int xVal = _yGammaKeyPointXValues[i];
                if (xVal >= 0 && xVal < 256)
                {
                    _yGammaTable[i] = new KeyValuePair<int, short>(
                        xVal, _yGamma.YGammaTable[xVal]);
                }
            }
        }

        /// <summary>
        /// 根据当前关键点，使用 Fritsch-Carlson 保单调 Hermite 插值生成完整曲线
        /// 两步保证单调性：1) 调和平均切线估计 2) α²+β²≤9 钳位约束
        /// </summary>
        private void GenerateMonotonicHermiteCurve()
        {
            int n = _yGammaTable.Count;
            if (n < 2) return;

            int[] x = new int[n];
            double[] y = new double[n];
            for (int i = 0; i < n; i++)
            {
                x[i] = _yGammaKeyPointXValues[i];
                y[i] = _yGammaTable[i].Value;
            }

            double[] h = new double[n - 1];
            double[] delta = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                h[i] = x[i + 1] - x[i];
                delta[i] = (y[i + 1] - y[i]) / h[i];
            }

            double[] m = new double[n];
            m[0] = delta[0];
            m[n - 1] = delta[n - 2];

            for (int i = 1; i < n - 1; i++)
            {
                if (delta[i - 1] * delta[i] <= 0)
                {
                    m[i] = 0.0;
                }
                else
                {
                    double w1 = 2 * h[i] + h[i - 1];
                    double w2 = h[i] + 2 * h[i - 1];
                    m[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]);
                }
            }

            for (int i = 0; i < n - 1; i++)
            {
                if (Math.Abs(delta[i]) < 1e-10) continue;
                double alpha = m[i] / delta[i];
                double beta = m[i + 1] / delta[i];
                if (alpha * alpha + beta * beta > 9.0)
                {
                    double scale = 3.0 / Math.Sqrt(alpha * alpha + beta * beta);
                    m[i] = scale * alpha * delta[i];
                    m[i + 1] = scale * beta * delta[i];
                }
            }

            _yGamma.YGammaTable[0] = (short)Math.Max(0, Math.Min(1023, (int)Math.Round(y[0])));
            short previousValue = _yGamma.YGammaTable[0];

            for (int px = 1; px < 256; px++)
            {
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
                double t2 = t * t;
                double t3 = t2 * t;
                double h00 = 2 * t3 - 3 * t2 + 1;
                double h10 = t3 - 2 * t2 + t;
                double h01 = -2 * t3 + 3 * t2;
                double h11 = t3 - t2;

                double value = h00 * y[seg]
                             + h10 * h[seg] * m[seg]
                             + h01 * y[seg + 1]
                             + h11 * h[seg] * m[seg + 1];

                short newValue = (short)Math.Max(0, Math.Min(1023, Math.Round(value)));
                if (newValue < previousValue) newValue = previousValue;

                _yGamma.YGammaTable[px] = newValue;
                previousValue = newValue;
            }
        }

        /// <summary>
        /// 基于三点（起点、拖动点、终点）拟合三次 Bezier 曲线并写入底层数组
        /// 参考 gamma-255.html 的 Bezier 曲线实现：
        ///   - P0(0,startY) / P3(255,endY) 固定端点
        ///   - P1/P2 控制点共轭计算，使 Bezier 曲线通过拖动点
        ///   - 二分法求参数 t，使 X(t) ≈ 目标像素位置
        ///   - 三次 Bezier 公式计算 Y 值
        /// 拖动中间点上下调整时，整条曲线随之全局弯曲偏移，效果平滑自然
        /// </summary>
        private void GenerateBezierCurve(short startY, int dragX, short dragY, short endY)
        {
            double t_d = dragX / 255.0;

            // P1/P2 控制点 X 坐标（关于拖动点对称分布，接近 gamma-255 默认比例）
            double p1x = Math.Max(1, dragX * 2.0 / 3.0);
            double p2x = Math.Min(254, 255.0 - (255.0 - dragX) * 2.0 / 3.0);

            // 共轭控制点 Y 值
            // 令 P1.y = P2.y = ctrlY，代入三次 Bezier 公式求解：
            //   dragY = (1-t_d)³·startY + 3(1-t_d)²·t_d·ctrlY + 3(1-t_d)·t_d²·ctrlY + t_d³·endY
            //        = (1-t_d)³·startY + 3(1-t_d)·t_d·ctrlY + t_d³·endY
            double w0 = (1 - t_d) * (1 - t_d) * (1 - t_d);
            double w3 = t_d * t_d * t_d;
            double denom = 3.0 * (1 - t_d) * t_d;

            double ctrlY;
            if (Math.Abs(denom) < 1e-10)
            {
                ctrlY = (startY + endY) / 2.0;
            }
            else
            {
                ctrlY = (dragY - w0 * startY - w3 * endY) / denom;
            }
            ctrlY = Math.Max(0, Math.Min(1023, ctrlY));

            // 填充 256 点曲线
            _yGamma.YGammaTable[0] = startY;
            double prevValue = startY;

            for (int px = 1; px < 256; px++)
            {
                // 二分法求参数 t，使 Bezier X(t) ≈ 目标 X 位置
                double lo = 0, hi = 1;
                double t = 0.5;
                double omt = 1 - t;
                for (int iter = 0; iter < 20; iter++)
                {
                    double xt = 3 * omt * omt * t * p1x +
                                3 * omt * t * t * p2x +
                                t * t * t * 255;

                    if (Math.Abs(xt - px) < 1e-3) break;
                    if (xt < px) lo = t;
                    else hi = t;
                    t = (lo + hi) / 2;
                }

                // 三次 Bezier 公式计算 Y
                omt = 1 - t;
                double value = omt * omt * omt * startY +
                               3 * omt * omt * t * ctrlY +
                               3 * omt * t * t * ctrlY +
                               t * t * t * endY;

                short newValue = (short)Math.Max(0, Math.Min(1023, Math.Round(value)));
                if (newValue < prevValue) newValue = (short)Math.Round(prevValue);

                _yGamma.YGammaTable[px] = newValue;
                prevValue = newValue;
            }
        }



        /// <summary>
        /// 基于三点（起点、控制点、终点）生成保单调 Hermite 曲线并写入底层数组
        /// 用于实现拖动中间关键点时的全局弯曲上移/下移效果
        /// 使用 Fritsch-Carlson 切线估计 + α²+β²≤9 钳位保证单调性
        /// </summary>
        private void GenerateThreePointHermiteCurve(int x0, short y0, int x1, short y1, int x2, short y2)
        {
            double h0 = x1 - x0;
            double h1 = x2 - x1;
            if (h0 <= 0 || h1 <= 0) return;

            double delta0 = (y1 - y0) / h0;
            double delta1 = (y2 - y1) / h1;

            // Fritsch-Carlson 切线估计
            double m0 = delta0;
            double m1, m2 = delta1;

            if (delta0 * delta1 <= 0)
            {
                m1 = 0.0;
            }
            else
            {
                double w1 = 2 * h1 + h0;
                double w2 = h1 + 2 * h0;
                m1 = (w1 + w2) / (w1 / delta0 + w2 / delta1);
            }

            double[] m = { m0, m1, m2 };
            double[] delta = { delta0, delta1 };
            double[] h = { h0, h1 };
            double[] x = { (double)x0, (double)x1, (double)x2 };
            double[] y = { (double)y0, (double)y1, (double)y2 };

            // 单调性钳位：α²+β² ≤ 9
            for (int i = 0; i < 2; i++)
            {
                if (Math.Abs(delta[i]) < 1e-10) continue;
                double alpha = m[i] / delta[i];
                double beta = m[i + 1] / delta[i];
                if (alpha * alpha + beta * beta > 9.0)
                {
                    double scale = 3.0 / Math.Sqrt(alpha * alpha + beta * beta);
                    m[i] = scale * alpha * delta[i];
                    m[i + 1] = scale * beta * delta[i];
                }
            }

            // 填充 256 点曲线
            _yGamma.YGammaTable[0] = y0;
            double prevValue = y0;

            for (int px = 1; px < 256; px++)
            {
                int seg = (px <= x1) ? 0 : 1;

                double t = (px - x[seg]) / h[seg];
                double t2 = t * t;
                double t3 = t2 * t;
                double h00 = 2 * t3 - 3 * t2 + 1;
                double h10 = t3 - 2 * t2 + t;
                double h01 = -2 * t3 + 3 * t2;
                double h11 = t3 - t2;

                double value = h00 * y[seg]
                             + h10 * h[seg] * m[seg]
                             + h01 * y[seg + 1]
                             + h11 * h[seg] * m[seg + 1];

                short newValue = (short)Math.Max(0, Math.Min(1023, Math.Round(value)));
                if (newValue < prevValue) newValue = (short)Math.Round(prevValue);

                _yGamma.YGammaTable[px] = newValue;
                prevValue = newValue;
            }
        }


        // 在 ViewModel 中添加属性
        private int _keyPointInfluenceRadius = 10;

        /// <summary>
        /// 关键点影响半径（影响的关键点数量）
        /// </summary>
        public int KeyPointInfluenceRadius
        {
            get { return _keyPointInfluenceRadius; }
            set
            {
                if (_keyPointInfluenceRadius == value) return;
                _keyPointInfluenceRadius = Math.Max(1, Math.Min(10, value));
                RaisePropertyChanged("KeyPointInfluenceRadius");
            }
        }

        /// <summary>
        /// 获取最后修改的关键点索引
        /// </summary>
        private int _lastModifiedIndex = -1;

        private int GetLastModifiedKeyPointIndex()
        {
            if (_lastModifiedIndex >= 0 && _lastModifiedIndex < _yGammaTable.Count)
                return _lastModifiedIndex;

            // 默认返回中间点
            return _yGammaTable.Count / 2;
        }


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

                            int draggedIndex = _lastModifiedIndex >= 0
                                ? _lastModifiedIndex
                                : _yGammaTable.Count / 2;

                            //RecalculateGammaTableWithGammaBlendedSpline(draggedIndex);

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

                    System.Diagnostics.Debug.WriteLine(
                        $"✅ 关键点集合重建完成: {_yGammaTable.Count} 个点");
                }
                finally
                {
                    // 确保定时器状态正确
                    // 注意：不需要重新启动，因为下次拖动时会启动 
                    // 重建后恢复监听
                    _yGammaTable.CollectionChanged += YGammaTable_CollectionChanged;
                }
            }
            RaisePropertyChanged(e.PropertyName);
        }


        public Processor IspProcessor
        {
            get;
            set;
        }

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
        }

        public byte PadNum
        {
            get { return _yGamma.PadNum; }
            set { _yGamma.PadNum = value; }
        }

        public void LoadYGammaTableFromFile()
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Multiselect = true;
            openFileDialog.Filter = "txt文件(*.txt) | *.txt";
            if (!(bool)openFileDialog.ShowDialog())
            {
                return;
            }

            _yGamma.LoadYGammaTableFromFile(openFileDialog.FileName);
            UpdateFullGammaCurve();
        }

        public void SaveYGammaTableToFile()
        {
            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
            saveFileDialog.CheckFileExists = false;
            saveFileDialog.CheckPathExists = false;
            saveFileDialog.Filter = "txt文件(*.txt) | *.txt";
            if (!(bool)saveFileDialog.ShowDialog())
            {
                return;
            }

            _yGamma.SaveYGammaTableToFile(saveFileDialog.FileName);
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


        public void LoadYGammaTableFromDevice()
        {

        }
        public void SaveYGammaTableToDevice()
        {

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
