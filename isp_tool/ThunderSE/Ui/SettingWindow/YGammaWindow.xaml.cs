using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.DataVisualization.Charting;
using System.Windows.Input;
using System.Windows.Media;
using ThunderSE.Model;

namespace ThunderSE.Ui.SettingWindow.YGamma
{
    /// <summary>
    /// YGammaWindow - Y-Gamma曲线调试窗口
    /// 用于编辑和调整Y-Gamma亮度非线性校正曲线（20个关键控制点）
    /// </summary>
    public partial class YGammaWindow : Window
    {
        private Point _panAnchor;
        private int? _currentSelectedChartLinePointIndex = null;
        private int? _currentSelectedBezierCtrlIndex = null;
        private YGammaWindowViewModel _viewModel;

        private int _defaultMaxX = 255;
        private int _defaultMinX = 0;
        private int _defaultMaxY = 1023;
        private int _defaultMinY = 0;

        public YGammaWindow()
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
            // Ctrl+O: 导入Gamma表
            InputBindings.Add(new KeyBinding(new RelayCommand(() =>
                _viewModel?.LoadYGammaTableFromFileCommand.Execute(null)),
                Key.O, ModifierKeys.Control));

            // Ctrl+S: 导出Gamma表
            InputBindings.Add(new KeyBinding(new RelayCommand(() =>
                _viewModel?.SaveYGammaTableToFileCommand.Execute(null)),
                Key.S, ModifierKeys.Control));

            // Home: 复位比例
            InputBindings.Add(new KeyBinding(new RelayCommand(() => OnResetChartAxes(null, null)),
                Key.Home, ModifierKeys.Control));

            // F7: 计算IQ菜单
            InputBindings.Add(new KeyBinding(new RelayCommand(() => OnClickCalcIQButton(null, null)),
                Key.F7, ModifierKeys.Control));

            // Esc: 关闭窗口
            //InputBindings.Add(new KeyBinding(new RelayCommand(() => Close()),
            //    Key.Escape, ModifierKeys.Control));
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateStatus($"窗口尺寸已调整: {this.ActualWidth:F0}×{this.ActualHeight:F0}");
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_viewModel != null && _viewModel.YGammaTable.Count > 0)
            {
                var result = MessageBox.Show(this,
                    "确定要关闭Y-Gamma调试窗口吗？\n\n当前编辑的曲线数据将不会自动保存。",
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

            // 清理 ViewModel 资源
            if (_viewModel != null)
            {
                _viewModel.Cleanup();
            }

            UpdateStatus("正在关闭Y-Gamma调试窗口...");
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

        private void UpdateChartStatus(string status)
        {
            if (TxtChartStatus != null)
            {
                TxtChartStatus.Text = status;
            }
        }

        private void UpdateOperationStatus(string status)
        {
            if (TxtOperationStatus != null)
            {
                TxtOperationStatus.Text = status;
            }
        }

        private void UpdatePointCountDisplay()
        {
            if (_viewModel != null && TxtPointCount != null)
            {
                TxtPointCount.Text = $"控制点数: {_viewModel.YGammaTable.Count}";
            }
        }

        private void UpdateSelectedPointDisplay()
        {
            if (TxtSelectedPoint != null)
            {
                if (_currentSelectedChartLinePointIndex.HasValue)
                {
                    int index = _currentSelectedChartLinePointIndex.Value;
                    if (_viewModel != null && index >= 0 && index < _viewModel.YGammaTable.Count)
                    {
                        var point = _viewModel.YGammaTable[index];
                        TxtSelectedPoint.Text = $"选中: X={point.Key}, Y={point.Value}";
                        TxtSelectedPoint.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7)); // 蓝色
                    }
                }
                else if (_currentSelectedBezierCtrlIndex.HasValue)
                {
                    int idx = _currentSelectedBezierCtrlIndex.Value;
                    if (_viewModel != null && idx >= 0 && idx < _viewModel.BezierControlPoints.Count)
                    {
                        var point = _viewModel.BezierControlPoints[idx];
                        string fixedMark = (idx == 0 || idx == 3) ? " [固定]" : "";
                        TxtSelectedPoint.Text = $"控制点 P{idx}: X={point.Key}, Y={point.Value}{fixedMark}";
                        TxtSelectedPoint.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // 绿色
                    }
                }
                else
                {
                    TxtSelectedPoint.Text = "选中: --";
                    TxtSelectedPoint.Foreground = Brushes.Gray;
                }
            }
        }

        #endregion

        #region 窗口事件处理

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel = (YGammaWindowViewModel)DataContext;

            InitializeUI();

            // 订阅数据变化事件以更新UI
            if (_viewModel != null)
            {
                _viewModel.YGammaTable.CollectionChanged += (s, args) =>
                {
                    UpdatePointCountDisplay();
                    UpdateOperationStatus("✓ 曲线数据已更新");
                };
            }
        }

        private void InitializeUI()
        {
            UpdateStatus("✓ Y-Gamma曲线调试工具初始化完成");
            UpdateOperationStatus("就绪");
            UpdateChartStatus("");
            UpdateProgressInfo("请导入Gamma表或开始编辑曲线");
            UpdatePointCountDisplay();
            UpdateSelectedPointDisplay();
        }

        #endregion

        #region 图表交互事件（保持原有逻辑+增强状态反馈）

        private void Chart_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _panAnchor = e.GetPosition((Chart)sender);
        }

        private void Chart_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var axisX = (LinearAxis)((Chart)sender).Axes[0];
            var axisY = (LinearAxis)((Chart)sender).Axes[1];

            axisX.Maximum = axisX.Maximum * (100 - e.Delta / 10) / 100;
            axisX.Minimum = axisX.Minimum * (100 - e.Delta / 10) / 100;

            axisY.Minimum = axisY.Minimum * (100 - e.Delta / 10) / 100;
            axisY.Maximum = axisY.Maximum * (100 - e.Delta / 10) / 100;

            UpdateOperationStatus("🔍 视图已缩放");
        }

        private void Chart_MouseMove(object sender, MouseEventArgs e)
        {
            var axisX = (LinearAxis)((Chart)sender).Axes[0];
            var axisY = (LinearAxis)((Chart)sender).Axes[1];

            if (e.RightButton == MouseButtonState.Pressed && _panAnchor != null)
            {
                axisX.Minimum += _panAnchor.X - e.GetPosition((Chart)sender).X;
                axisX.Maximum += _panAnchor.X - e.GetPosition((Chart)sender).X;

                axisY.Maximum += e.GetPosition((Chart)sender).Y - _panAnchor.Y;
                axisY.Minimum += e.GetPosition((Chart)sender).Y - _panAnchor.Y;

                _panAnchor = e.GetPosition((Chart)sender);

                UpdateOperationStatus("🖱️ 正在平移视图...");
            }

            if (_currentSelectedChartLinePointIndex != null)
            {
                var pos = e.GetPosition(YGammaTableLine);

                double ActualChartY = (axisY.ActualHeight - pos.Y) / axisY.ActualHeight * (double)axisY.ActualMaximum;

                _viewModel.YGammaTable[_currentSelectedChartLinePointIndex.Value] =
                    new KeyValuePair<int, short>(_viewModel.YGammaTable[_currentSelectedChartLinePointIndex.Value].Key, (short)ActualChartY);

                //System.Diagnostics.Debug.WriteLine(
                //    $"[MouseMove] 修改关键点 Index={_currentSelectedChartLinePointIndex.Value}, " +
                //    $"新Y值={ActualChartY}, 集合大小={_viewModel.YGammaTable.Count}");

                // 实时更新选中点显示
                UpdateSelectedPointDisplay();
                UpdateOperationStatus("✏️ 正在编辑数据点...");
            }


            if (_currentSelectedBezierCtrlIndex != null)
            {
                int idx = _currentSelectedBezierCtrlIndex.Value;
                if (idx == 1 || idx == 2) // P1/P2 可拖动
                {
                    var pos = e.GetPosition(BezierCtrlLine);
                    double chartWidth = BezierCtrlLine.ActualWidth;
                    double chartHeight = BezierCtrlLine.ActualHeight;

                    if (chartWidth > 0 && chartHeight > 0)
                    {
                        double dataX = pos.X / chartWidth * (double)axisX.ActualMaximum;
                        double dataY = (chartHeight - pos.Y) / chartHeight * (double)axisY.ActualMaximum;

                        dataX = Math.Max(1, Math.Min(254, dataX));
                        dataY = Math.Max(0, Math.Min(1023, dataY));

                        _viewModel.BezierControlPoints[idx] = new KeyValuePair<int, short>((int)dataX, (short)dataY);

                        UpdateSelectedPointDisplay();
                        UpdateOperationStatus($"✏️ 正在编辑控制点 P{idx}...");
                    }
                }
            }
        }

        private void Chart_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            YGammaTableLine.SelectedItem = null;
            BezierCtrlLine.SelectedItem = null;
            _currentSelectedChartLinePointIndex = null;
            _currentSelectedBezierCtrlIndex = null;

            UpdateSelectedPointDisplay();
            UpdateOperationStatus("已取消选择");
        }

        private void LineSeries_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0)
            {
                return;
            }

            var dataCollection = _viewModel.YGammaTable;
            KeyValuePair<int, short> valPair = (KeyValuePair<int, short>)e.AddedItems[0];

            for (int i = 0; i < dataCollection.Count; i++)
            {
                if (dataCollection[i].Key == valPair.Key)
                {
                    var tmpVal = new KeyValuePair<int, short>(dataCollection[i].Key, dataCollection[i].Value);
                    dataCollection[i] = tmpVal;
                    _currentSelectedChartLinePointIndex = i;

                    // 更新选中状态显示
                    UpdateSelectedPointDisplay();
                    UpdateOperationStatus($"✓ 已选择数据点 ({valPair.Key}, {valPair.Value})");

                    break;
                }
            }
        }

        /// <summary>
        /// 贝塞尔控制点选中事件：点击 P1/P2 时记录选中索引
        /// </summary>
        private void BezierCtrlLine_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;

            var dataCollection = _viewModel.BezierControlPoints;
            KeyValuePair<int, short> valPair = (KeyValuePair<int, short>)e.AddedItems[0];

            for (int i = 0; i < dataCollection.Count; i++)
            {
                if (dataCollection[i].Key == valPair.Key)
                {
                    dataCollection[i] = dataCollection[i]; // 触发 Replace 事件
                    _currentSelectedBezierCtrlIndex = i;

                    UpdateSelectedPointDisplay();
                    string fixedMark = (i == 0 || i == 3) ? " [固定不可拖动]" : "";
                    UpdateOperationStatus($"✓ 已选择控制点 P{i} ({valPair.Key}, {valPair.Value}){fixedMark}");
                    break;
                }
            }
        }

        private void OnResetChartAxes(object sender, RoutedEventArgs e)
        {
            var axisX = (LinearAxis)(YGammaChart).Axes[0];
            var axisY = (LinearAxis)(YGammaChart).Axes[1];

            axisX.Maximum = _defaultMaxX;
            axisX.Minimum = _defaultMinX;

            axisY.Maximum = _defaultMaxY;
            axisY.Minimum = _defaultMinY;

            UpdateStatus("🔄 坐标轴已复位到默认范围");
            UpdateOperationStatus("✓ 复位完成 (X:0-255, Y:0-1023)");
            UpdateProgressInfo("默认坐标轴范围已恢复");
        }

        #endregion

        #region IQ计算功能（增强版）

        private void ShowOfflineYGammaIQ(object sender, RoutedEventArgs e)
        {
            try
            {
                YGammaOfflineIQWindow IQWindow = new YGammaOfflineIQWindow(_viewModel.IspProcessor);
                IQWindow.Show();

                UpdateStatus("📊 已打开离线IQ计算窗口");
                UpdateProgressInfo("使用本地数据进行IQ分析");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"打开离线IQ窗口失败:\n{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                UpdateStatus("❌ 打开离线IQ窗口失败");
            }
        }

        private void ShowOnlineYGammaIQ(object sender, RoutedEventArgs e)
        {
            try
            {
                YGammaOnlineIQWindow IQWindow = new YGammaOnlineIQWindow();
                IQWindow.Show();

                UpdateStatus("🌐 已打开在线IQ计算窗口");
                UpdateProgressInfo("实时设备交互模式已启动");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"打开在线IQ窗口失败:\n{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                UpdateStatus("❌ 打开在线IQ窗口失败");
            }
        }

        private void OnClickCalcIQButton(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.ContextMenu != null)
            {
                button.ContextMenu.IsEnabled = true;
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;

                UpdateStatus("🧮 IQ计算选项菜单已展开");
                UpdateOperationStatus("请选择在线或离线模式");
            }
            else
            {
                // ContextMenu为空时显示错误信息或尝试延迟加载
                UpdateStatus("⚠️ 无法打开IQ计算菜单，请稍后重试");
                UpdateOperationStatus("ContextMenu未初始化");
            }
        }

        #endregion

    }
}
