using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.DataVisualization.Charting;
using System.Windows.Controls.DataVisualization.Charting.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ThunderSE.Ui.SettingWindow.Awb
{
    using CustomControls;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Windows.Controls.Primitives;
    using System.Windows.Threading;
    using ThunderSE.Model;

    internal class RGainStatRangeLineBindingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null || parameter == null)
                return 0.0;

            int doubleVal = (int)value;
            double matrixTranslateValue = (double)parameter;

            // 防止除零或无效值导致Infinity
            if (matrixTranslateValue <= 0 || double.IsInfinity(matrixTranslateValue) || double.IsNaN(matrixTranslateValue))
                return 0.0;

            return doubleVal * matrixTranslateValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null || parameter == null)
                return "0";

            int intVal = (int)(double)value;
            double matrixTranslateValue = (double)parameter;

            // 防止除零或无效值
            if (matrixTranslateValue <= 0 || double.IsInfinity(matrixTranslateValue) || double.IsNaN(matrixTranslateValue))
                return "0";

            return ((int)(intVal / matrixTranslateValue)).ToString();
        }
    }

    internal class StatisticRangeLineVisibilityConverter : IValueConverter
    {

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var visibility = (Visibility)value;
            return visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    internal class GainDataBindingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var inputCollection = (Dictionary<string, KeyValuePair<int, int>>)value;
            var outputCollection = new Collection<KeyValuePair<int, int>>();
            foreach (var item in inputCollection)
            {
                if (item.Value.Key != -1)
                {
                    outputCollection.Add(new KeyValuePair<int, int>(item.Value.Key, item.Value.Value));
                }
            }

            return outputCollection;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 增益数据点信息（包含完整的上下文）
    /// </summary>
    public class GainDataPoint
    {
        public string ImageName { get; set; }      // 图片名称
        public string Category { get; set; }       // 类别名称
        public int SelectionIndex { get; set; }    // 选区索引（-1表示合并计算）
        public int RGain { get; set; }             // R增益值
        public int BGain { get; set; }             // B增益值
        public string FullKey { get; set; }        // 完整键名

        public GainDataPoint(string fullKey, string imageName, string category, int selectionIndex, int rGain, int bGain)
        {
            FullKey = fullKey;
            ImageName = imageName;
            Category = category;
            SelectionIndex = selectionIndex;
            RGain = rGain;
            BGain = bGain;
        }

        public override string ToString()
        {
            if (SelectionIndex >= 0)
                return $"{ImageName}[#{SelectionIndex + 1}] R={RGain}, B={BGain}";
            else
                return $"{ImageName} R={RGain}, B={BGain}";
        }
    }

    /// <summary>
    /// 可拖拽的控制点可视化对象
    /// </summary>
    public class ControlPointThumb : Thumb
    {
        public int Index { get; set; }
        public Ellipse VisualCircle { get; set; }

        public ControlPointThumb(int index, Point position, Brush color)
        {
            Index = index;

            // 创建圆形视觉元素
            VisualCircle = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = color,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                IsHitTestVisible = false // 让鼠标事件穿透到Thumb
            };

            Canvas.SetLeft(VisualCircle, position.X - 6);
            Canvas.SetTop(VisualCircle, position.Y - 6);

            // Thumb本身不可见，但接收拖动事件
            this.Width = 20;
            this.Height = 20;
            this.Background = Brushes.Transparent;
            Canvas.SetLeft(this, position.X - 10);
            Canvas.SetTop(this, position.Y - 10);
        }

        public void UpdatePosition(Point newPosition)
        {
            Canvas.SetLeft(this, newPosition.X - 10);
            Canvas.SetTop(this, newPosition.Y - 10);
            Canvas.SetLeft(VisualCircle, newPosition.X - 6);
            Canvas.SetTop(VisualCircle, newPosition.Y - 6);
        }
    }

    public partial class AwbWindow : Window
    {
        private AwbWindowViewModel _viewModel;

        private Canvas _bezierLineDrawingArea;
        private Canvas _rangeLinesDrawingArea;

        private EdgePanel _chartArea;

        // 用于在批量投影贝塞尔曲线时，向事件传递颜色的临时队列
        private List<Brush> _pendingCurveColors = new List<Brush>();

        private List<BezierFigure> _bezierLineList = new List<BezierFigure>();
        private BezierFigure _currentSelectBezierLine = null;

        private List<LineSeries> _chartLineList = new List<LineSeries>();
        private LineSeries _currentSelectedChartLine = null;
        private int _currentSelectedChartLinePointIndex = 0;
        private double? _pendingDragValue = null; // 拖动期间的待提交值
        private Ellipse _dragIndicator = null;

        private double _maxChartX;
        private double _maxChartY;

        private double _maxCanvasX;
        private double _maxCanvasY;

        private Point _panAnchor;
        private bool _canPanYAndCanZoomY = true;

        Thumb _lineStartThumb = new Thumb();
        Line _lineEnd = new Line();

        public static readonly DependencyProperty HasSelectedBezierLineProperty = DependencyProperty.Register(
                "HasSelectedBezierLine",
                typeof(bool),
                typeof(AwbWindow),
                new FrameworkPropertyMetadata(false));

        public bool HasSelectedBezierLine
        {
            get { return (bool)GetValue(HasSelectedBezierLineProperty); }
            set { SetValue(HasSelectedBezierLineProperty, value); }
        }

        public static readonly DependencyProperty CanAddBezierLineProperty = DependencyProperty.Register(
                "CanAddBezierLine",
                typeof(bool),
                typeof(AwbWindow),
                new FrameworkPropertyMetadata(true));

        public bool CanAddBezierLine
        {
            get { return (bool)GetValue(CanAddBezierLineProperty); }
            set { SetValue(CanAddBezierLineProperty, value); }
        }

        public static readonly DependencyProperty HasCorrectionDataProperty = DependencyProperty.Register(
            "HasCorrectionData",
            typeof(bool),
            typeof(AwbWindow),
            new FrameworkPropertyMetadata(false));

        public bool HasCorrectionData
        {
            get { return (bool)GetValue(HasCorrectionDataProperty); }
            set { SetValue(HasCorrectionDataProperty, value); }
        }

        private List<Color> _bezierLineColors = new List<Color>
        {
            Colors.DodgerBlue,
            Colors.OrangeRed,
            Colors.ForestGreen,
            Colors.Purple
        };

        public AwbWindow()
        {
            InitializeComponent();
            SetupWindowEvents();
        }

        private void SetupWindowEvents()
        {
            this.SizeChanged += OnWindowSizeChanged;
            this.StateChanged += OnWindowStateChanged;
            this.Closing += OnWindowClosing;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel = (AwbWindowViewModel)DataContext;
            _viewModel.StatisticData.CollectionChanged += StatisticDataCollectionChanged;

            // 订阅GainData变化事件
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            _bezierLineDrawingArea = (Canvas)DataChart.Template.FindName("BezierLineDrawingArea", DataChart);
            _rangeLinesDrawingArea = (Canvas)DataChart.Template.FindName("RangeLinesDrawingArea", DataChart);

            _chartArea = (EdgePanel)DataChart.Template.FindName("ChartArea", DataChart);
            _chartArea.MouseMove += OnDragChartlinePoint;
            _chartArea.MouseLeftButtonUp += OnChartAreaMouseLeftButtonUp;

            DataChart.MouseWheel += OnDataChartMouseWheel;
            DataChart.MouseMove += OnDataChartMouseMove;
            DataChart.MouseRightButtonDown += OnDataChartMouseRightButtonDown;

            var axisX = (LinearAxis)DataChart.Axes[0];
            _maxChartX = (double)axisX.ActualMaximum;

            var axisY = (LinearAxis)DataChart.Axes[1];
            _maxChartY = (double)axisY.ActualMaximum;

            _maxCanvasX = _bezierLineDrawingArea.ActualWidth;
            _maxCanvasY = _bezierLineDrawingArea.ActualHeight;

            DrawStatRangeLines();
            DrawGainValueRangeLines();

            // 如果已经有数据，立即更新图表
            if (_viewModel?.GainData != null && _viewModel.GainData.Count > 0)
            {
                UpdateChartSeriesByCategory();
            }

            // 【关键修改1】：不要订阅内部 Canvas 的 SizeChanged，改为订阅外层 Chart 控件
            // 这样无论内部模板怎么重建，只要图表尺寸变了，都能准确触发重绘
            DataChart.SizeChanged += (s, v) =>
            {
                // 每次尺寸改变时，重新获取最新的画布尺寸用于坐标转换
                var latestBezierCanvas = (Canvas)DataChart.Template.FindName("BezierLineDrawingArea", DataChart);
                if (latestBezierCanvas != null)
                {
                    _maxCanvasX = latestBezierCanvas.ActualWidth;
                    _maxCanvasY = latestBezierCanvas.ActualHeight;
                }
                RedrawRangeLines();
            };

            InitializeUI();
            SetupKeyboardShortcuts();

            // 【新增】初始化时确保拖动是启用的
            SetDragEnabled(false);

            UpdateStatus("✓ AWB调试窗口初始化完成");
        }

        private void InitializeUI()
        {
            UpdateWindowSizeDisplay();
            UpdateCurveCountDisplay();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "GainData")
            {
                UpdateChartSeriesByCategory();
            }
            else if(e.PropertyName == "StatisticData")
            {
                // 导入数据后，使用 RebuildChartFromStatisticData 重新构建图表
                // 这样可以确保所有曲线都正确显示
                RebuildChartFromStatisticData();
            }
            //Console.WriteLine($"ViewModel property changed: {e.PropertyName}");
        }

        /// <summary>
        /// 在视觉树中查找指定类型的子控件
        /// </summary>
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                {
                    return typedChild;
                }

                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        /// <summary>
        /// 根据类别更新图表系列
        /// </summary>
        private void UpdateChartSeriesByCategory()
        {
            if (_viewModel?.GainData == null)
                return;

            // 方法1: 通过名称查找 Chart 控件
            var chart = FindName("DataChart") as Chart;

            // 方法2: 如果方法1失败，尝试从视觉树中查找
            if (chart == null)
            {
                chart = FindVisualChild<Chart>(this);
            }

            if (chart == null)
            {
                System.Diagnostics.Debug.WriteLine("警告: 无法找到 Chart 控件");
                return;
            }

            // 清除现有的所有系列
            chart.Series.Clear();

            // 定义类别颜色映射
            var categoryColors = new Dictionary<string, Brush>
            {
                { "白点", Brushes.Red },
                { "非白点", Brushes.DarkSlateGray },
                { "默认", Brushes.Red }
            };

            // 按类别分组数据（使用新的数据结构保留完整信息）
            var groupedData = new Dictionary<string, List<GainDataPoint>>();

            foreach (var item in _viewModel.GainData)
            {
                if (item.Value.Key == -1) continue; // 跳过无效数据

                string categoryName = "白点";
                string imageName = "";
                int selectionIndex = -1; // -1表示白点合并模式，>=0表示非白点独立选区

                // 解析键名，支持两种格式：
                // 1. "图像名_类别名" （白点类别）
                // 2. "图像名_类别名_#N" （非白点类别，带选区索引）
                string key = item.Key;

                // 首先尝试匹配已知的类别名称
                bool categoryFound = false;
                foreach (var knownCategory in categoryColors.Keys)
                {
                    // 检查键名中是否包含该类别（支持下划线分隔）
                    if (key.Contains($"_{knownCategory}_") || key.EndsWith($"_{knownCategory}"))
                    {
                        categoryName = knownCategory;
                        categoryFound = true;
                        break;
                    }
                }

                // 解析图片名称和选区索引
                if (categoryFound)
                {
                    if (key.Contains($"_{categoryName}_#"))
                    {
                        // 格式: "图像名_类别名_#N"
                        int hashIndex = key.IndexOf("_#");
                        imageName = key.Substring(0, hashIndex);

                        // 提取选区索引
                        string indexStr = key.Substring(hashIndex + 2);
                        if (int.TryParse(indexStr, out int idx))
                        {
                            selectionIndex = idx - 1; // 转换为从0开始的索引
                        }
                    }
                    else if (key.EndsWith($"_{categoryName}"))
                    {
                        // 格式: "图像名_类别名"
                        int lastUnderscore = key.LastIndexOf('_');
                        if (lastUnderscore > 0)
                        {
                            imageName = key.Substring(0, lastUnderscore);
                        }
                    }
                }
                else
                {
                    // 后备解析逻辑
                    int lastUnderscoreIndex = key.LastIndexOf('_');
                    if (lastUnderscoreIndex > 0)
                    {
                        string potentialCategory = key.Substring(lastUnderscoreIndex + 1);

                        if (potentialCategory.StartsWith("#"))
                        {
                            int secondLastUnderscore = key.LastIndexOf('_', lastUnderscoreIndex - 1);
                            if (secondLastUnderscore > 0)
                            {
                                potentialCategory = key.Substring(secondLastUnderscore + 1, lastUnderscoreIndex - secondLastUnderscore - 1);
                                imageName = key.Substring(0, secondLastUnderscore);

                                string indexStr = key.Substring(lastUnderscoreIndex + 2);
                                if (int.TryParse(indexStr, out int idx))
                                {
                                    selectionIndex = idx - 1;
                                }
                            }
                        }
                        else
                        {
                            imageName = key.Substring(0, lastUnderscoreIndex);
                        }

                        if (categoryColors.ContainsKey(potentialCategory))
                        {
                            categoryName = potentialCategory;
                        }
                    }
                }

                if (!groupedData.ContainsKey(categoryName))
                {
                    groupedData[categoryName] = new List<GainDataPoint>();
                }

                // 创建包含完整信息的数据点
                var dataPoint = new GainDataPoint(
                    key,
                    imageName,
                    categoryName,
                    selectionIndex,
                    item.Value.Key,   // RGain
                    item.Value.Value  // BGain
                );

                groupedData[categoryName].Add(dataPoint);
            }

            // 为每个类别创建一个ScatterSeries
            int seriesCount = 0;
            foreach (var group in groupedData.OrderBy(g => g.Key))
            {
                if (group.Value.Count == 0) continue;

                // 转换为简单的 KeyValuePair<int, int> 用于图表显示
                var displayData = group.Value.Select(dp =>
                    new KeyValuePair<int, int>(dp.RGain, dp.BGain)).ToList();

                var series = new ScatterSeries
                {
                    Title = group.Key,
                    ItemsSource = displayData,
                    DependentValuePath = "Value",
                    IndependentValuePath = "Key",
                    IsSelectionEnabled = true
                };

                // 创建带工具提示的数据点样式
                var dataPointStyle = CreateDataPointStyleWithTooltip(
                    categoryColors.ContainsKey(group.Key) ? categoryColors[group.Key] : categoryColors["默认"],
                    group.Key,
                    group.Value); // 传递完整的GainDataPoint列表

                series.DataPointStyle = dataPointStyle;

                chart.Series.Add(series);
                seriesCount++;
            }

            System.Diagnostics.Debug.WriteLine($"UpdateChartSeriesByCategory: 成功创建 {seriesCount} 个系列");
        }


        // 用于存储数据点和类别的映射
        private Dictionary<ScatterDataPoint, string> _dataPointCategoryMap = new Dictionary<ScatterDataPoint, string>();
        private ScatterDataPoint _selectedDataPoint = null;

        /// <summary>
        /// 创建带工具提示的数据点样式
        /// </summary>
        private Style CreateDataPointStyleWithTooltip(Brush color, string categoryName, List<GainDataPoint> allDataPoints = null)
        {
            var style = new Style(typeof(ScatterDataPoint));

            // 基本属性设置
            style.Setters.Add(new Setter(ScatterDataPoint.BackgroundProperty, color));
            style.Setters.Add(new Setter(ScatterDataPoint.IsTabStopProperty, false));
            style.Setters.Add(new Setter(ScatterDataPoint.WidthProperty, 10.0));
            style.Setters.Add(new Setter(ScatterDataPoint.HeightProperty, 10.0));
            style.Setters.Add(new Setter(ScatterDataPoint.BorderBrushProperty, Brushes.White));
            style.Setters.Add(new Setter(ScatterDataPoint.BorderThicknessProperty, new Thickness(2)));

            // 添加 Loaded 事件来设置工具提示
            var loadedEventSetter = new EventSetter();
            loadedEventSetter.Event = FrameworkElement.LoadedEvent;
            loadedEventSetter.Handler = new RoutedEventHandler((sender, e) =>
            {
                var dataPoint = sender as ScatterDataPoint;
                if (dataPoint != null && !_dataPointCategoryMap.ContainsKey(dataPoint))
                {
                    _dataPointCategoryMap[dataPoint] = categoryName;

                    // 创建并设置工具提示
                    var toolTip = CreateToolTipForDataPoint(dataPoint, categoryName, color, allDataPoints);
                    dataPoint.ToolTip = toolTip;

                    // 添加点击事件
                    dataPoint.MouseLeftButtonDown += DataPoint_MouseLeftButtonDown;
                }
            });
            style.Setters.Add(loadedEventSetter);

            // 创建模板
            var template = new ControlTemplate(typeof(ScatterDataPoint));
            var gridFactory = new FrameworkElementFactory(typeof(Grid));
            gridFactory.SetValue(Grid.NameProperty, "Root");

            // 添加椭圆（数据点）
            var ellipse = new FrameworkElementFactory(typeof(Ellipse));
            ellipse.SetBinding(Ellipse.FillProperty, new Binding("Background")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            ellipse.SetBinding(Ellipse.StrokeProperty, new Binding("BorderBrush")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            ellipse.SetValue(Ellipse.StrokeThicknessProperty, 2.0);
            ellipse.SetValue(Ellipse.NameProperty, "DataPointEllipse");
            gridFactory.AppendChild(ellipse);

            // 添加选中高亮效果
            var selectionHighlight = new FrameworkElementFactory(typeof(Ellipse));
            selectionHighlight.SetValue(Ellipse.NameProperty, "SelectionHighlight");
            selectionHighlight.SetValue(Ellipse.FillProperty, Brushes.Transparent);
            selectionHighlight.SetValue(Ellipse.StrokeProperty, Brushes.Gold);
            selectionHighlight.SetValue(Ellipse.StrokeThicknessProperty, 4.0);
            selectionHighlight.SetValue(Ellipse.IsHitTestVisibleProperty, false);
            selectionHighlight.SetValue(Ellipse.OpacityProperty, 0.0);
            gridFactory.AppendChild(selectionHighlight);

            template.VisualTree = gridFactory;
            style.Setters.Add(new Setter(ScatterDataPoint.TemplateProperty, template));

            return style;
        }

        /// <summary>
        /// 为数据点创建工具提示
        /// </summary>
        private ToolTip CreateToolTipForDataPoint(ScatterDataPoint dataPoint, string categoryName, Brush color, List<GainDataPoint> allDataPoints = null)
        {
            var toolTip = new ToolTip
            {
                Placement = PlacementMode.Mouse,
                HasDropShadow = true,
                Background = new SolidColorBrush(
                    Color.FromArgb(230, 205, 205, 205)),
                BorderBrush = new SolidColorBrush(
                    Color.FromRgb(0, 120, 215)),
                BorderThickness = new Thickness(1)
            };

            // 获取数据值
            int rGain = 0;
            int bGain = 0;
            string imageName = "未知";
            int selectionIndex = -1;

            // 尝试从DataContext获取完整信息
            if (dataPoint.DataContext is KeyValuePair<int, int> kvp)
            {
                rGain = kvp.Key;
                bGain = kvp.Value;

                // 如果有完整数据列表，查找对应的详细信息
                if (allDataPoints != null)
                {
                    var matchingPoint = allDataPoints.FirstOrDefault(dp =>
                        dp.RGain == rGain && dp.BGain == bGain);

                    if (matchingPoint != null)
                    {
                        imageName = matchingPoint.ImageName;
                        selectionIndex = matchingPoint.SelectionIndex;
                        categoryName = matchingPoint.Category;
                    }
                }
            }

            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4)
            };

            var stackPanel = new StackPanel();

            // 标题
            var titleTextBlock = new TextBlock
            {
                Text = "📊AWB增益数据点",
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                Margin = new Thickness(0, 0, 0, 3)
            };
            stackPanel.Children.Add(titleTextBlock);

            // 分隔线
            var separator1 = new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                Margin = new Thickness(0, 0, 0, 3)
            };
            stackPanel.Children.Add(separator1);

            // 🆕 图片名称信息行
            var imagePanel = new StackPanel { Orientation = Orientation.Horizontal };
            var imageLabel = new TextBlock
            {
                Text = "🖼️ 图片: ",
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DarkSlateGray
            };
            var imageValue = new TextBlock
            {
                Text = imageName,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                FontSize = 11,
                MaxWidth = 200,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            imagePanel.Children.Add(imageLabel);
            imagePanel.Children.Add(imageValue);
            stackPanel.Children.Add(imagePanel);

            // 🆕 选区索引信息（仅非白点类别显示）
            if (selectionIndex >= 0)
            {
                var selectionPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 3, 0, 0)
                };
                var selectionLabel = new TextBlock
                {
                    Text = "📍 选区: ",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.DarkSlateGray
                };
                var selectionValue = new TextBlock
                {
                    Text = $"#{selectionIndex + 1}",
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                    FontSize = 11
                };
                selectionPanel.Children.Add(selectionLabel);
                selectionPanel.Children.Add(selectionValue);
                stackPanel.Children.Add(selectionPanel);
            }

            // 分隔线
            var separator2 = new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                Margin = new Thickness(0, 0, 0, 3)
            };
            stackPanel.Children.Add(separator2);

            // RGain 信息行
            var rgainPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var rgainLabel = new TextBlock
            {
                Text = "RGain: ",
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DarkSlateGray
            };
            var rgainValue = new TextBlock
            {
                Text = rGain.ToString(),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 0)),
                FontSize = 12
            };
            rgainPanel.Children.Add(rgainLabel);
            rgainPanel.Children.Add(rgainValue);
            stackPanel.Children.Add(rgainPanel);

            // BGain 信息行
            var bgainPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 3, 0, 0)
            };
            var bgainLabel = new TextBlock
            {
                Text = "BGain: ",
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DarkSlateGray
            };
            var bgainValue = new TextBlock
            {
                Text = bGain.ToString(),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 100, 255)),
                FontSize = 12
            };
            bgainPanel.Children.Add(bgainLabel);
            bgainPanel.Children.Add(bgainValue);
            stackPanel.Children.Add(bgainPanel);

            // 分隔线
            var separator3 = new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                Margin = new Thickness(0, 0, 0, 3)
            };
            stackPanel.Children.Add(separator3);

            // 类别信息
            var categoryPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var categoryLabel = new TextBlock
            {
                Text = "🏷️ 类别: ",
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DarkSlateGray
            };
            var categoryValue = new TextBlock
            {
                Text = categoryName,
                FontWeight = FontWeights.Bold,
                Foreground = color,
                FontSize = 11
            };
            categoryPanel.Children.Add(categoryLabel);
            categoryPanel.Children.Add(categoryValue);
            stackPanel.Children.Add(categoryPanel);

            border.Child = stackPanel;
            toolTip.Content = border;

            return toolTip;
        }

        /// <summary>
        /// 鼠标点击数据点时选中
        /// </summary>
        private void DataPoint_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dataPoint = sender as ScatterDataPoint;
            if (dataPoint == null) return;

            // 取消之前的选中
            if (_selectedDataPoint != null && _selectedDataPoint != dataPoint)
            {
                ClearDataPointSelection(_selectedDataPoint);
            }

            // 选中当前数据点
            SelectDataPoint(dataPoint);
            _selectedDataPoint = dataPoint;

            // 阻止事件继续传播
            e.Handled = true;
        }

        /// <summary>
        /// 选中数据点
        /// </summary>
        private void SelectDataPoint(ScatterDataPoint dataPoint)
        {
            // 查找模板中的 SelectionHighlight 元素
            var selectionHighlight = FindChildByName(dataPoint, "SelectionHighlight") as Ellipse;
            if (selectionHighlight != null)
            {
                selectionHighlight.Opacity = 1.0;
            }

            // 改变数据点大小以突出显示
            dataPoint.Width = 14;
            dataPoint.Height = 14;
        }

        /// <summary>
        /// 清除数据点选中状态
        /// </summary>
        private void ClearDataPointSelection(ScatterDataPoint dataPoint)
        {
            var selectionHighlight = FindChildByName(dataPoint, "SelectionHighlight") as Ellipse;
            if (selectionHighlight != null)
            {
                selectionHighlight.Opacity = 0.0;
            }

            // 恢复数据点大小
            dataPoint.Width = 10;
            dataPoint.Height = 10;
        }

        /// <summary>
        /// 在可视化树中查找指定名称的子元素
        /// </summary>
        private DependencyObject FindChildByName(DependencyObject parent, string name)
        {
            if (parent == null) return null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is FrameworkElement fe && fe.Name == name)
                {
                    return child;
                }

                var result = FindChildByName(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }


        private void SetupKeyboardShortcuts()
        {
            InputBindings.Add(new KeyBinding(new RelayCommand(() => OnBeginDrawBezierLine(null, null)),
                Key.D, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new RelayCommand(() => OnDrawBezierLineOk(null, null)),
                Key.Enter, ModifierKeys.Control));
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateWindowSizeDisplay();
            // 当窗口大小改变时，重新绘制范围线以适应新尺寸
            RedrawRangeLines();
        }

        private void RedrawRangeLines()
        {
            // 【关键修改2】：每次重绘前，重新从模板中获取最新的内部控件引用
            var newRangeCanvas = (Canvas)DataChart.Template.FindName("RangeLinesDrawingArea", DataChart);
            var newBezierCanvas = (Canvas)DataChart.Template.FindName("BezierLineDrawingArea", DataChart);
            var newChartArea = (EdgePanel)DataChart.Template.FindName("ChartArea", DataChart);


            // 2. 强制更新引用，避免使用旧对象
            if (newRangeCanvas != null) _rangeLinesDrawingArea = newRangeCanvas;
            if (newBezierCanvas != null) _bezierLineDrawingArea = newBezierCanvas;
            // 检测 EdgePanel (图表绘图区) 是否被重建
            if (newChartArea != _chartArea && newChartArea != null)
            {
                _chartArea = newChartArea;
                // 引用变了，必须重新绑定鼠标拖拽数据点的事件，否则拖拽会失效
                _chartArea.MouseMove += OnDragChartlinePoint;
                _chartArea.MouseLeftButtonUp += OnChartAreaMouseLeftButtonUp;
            }

            // 检测贝塞尔画布是否被重建
            //if (newBezierCanvas != _bezierLineDrawingArea && newBezierCanvas != null)
            //{
            //    _bezierLineDrawingArea = newBezierCanvas;
            //    // 如果当前正在绘图模式，画布重建会导致里面的贝塞尔曲线丢失，必须重新挂载上去
            //    if (_bezierLineDrawingArea.Visibility == Visibility.Visible && _bezierLineList.Count > 0)
            //    {
            //        foreach (var bezier in _bezierLineList)
            //        {
            //            // 【关键修复 3】：强制从可能已废弃的旧画布中移除
            //            if (bezier.Parent != null)
            //            {
            //                ((Panel)bezier.Parent).Children.Remove(bezier);
            //            }
            //            _bezierLineDrawingArea.Children.Add(bezier);
            //        }
            //    }
            //}

            // 3. 安全检查：如果获取失败，延迟重试
            if (_rangeLinesDrawingArea == null || _bezierLineDrawingArea == null)
            {
                Dispatcher.BeginInvoke(new Action(RedrawRangeLines),
                    System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            //// 更新范围线画布引用
            //if (newRangeCanvas != null)
            //{
            //    _rangeLinesDrawingArea = newRangeCanvas;
            //}

            //// 清除现有的范围线
            //if (_rangeLinesDrawingArea == null) 
            //    return;

            // 4. 更新画布尺寸（使用新Canvas的实际尺寸）
            _maxCanvasX = _bezierLineDrawingArea.ActualWidth;
            _maxCanvasY = _bezierLineDrawingArea.ActualHeight;
            // 5. 清除新Canvas的子元素（而不是旧Canvas）
            _rangeLinesDrawingArea.Children.Clear();

            // 重新绘制统计范围线
            DrawStatRangeLines();

            // 重新绘制增益值范围线
            DrawGainValueRangeLines();
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                UpdateStatus("窗口已最大化");
            }
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_viewModel.IsLoadFile || (_chartLineList.Count > 0 || _bezierLineList.Count > 0))
            {
                var result = MessageBox.Show(this,
                    "当前有未保存的曲线数据，确定要关闭窗口吗？",
                    "确认关闭",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            UpdateStatus("AWB调试窗口正在关闭...");
        }

        private void UpdateWindowSizeDisplay()
        {
            if (TxtWindowSize != null)
            {
                TxtWindowSize.Text = $"窗口尺寸: {this.ActualWidth:F0}×{this.ActualHeight:F0}";
            }
        }

        private void UpdateCurveCountDisplay()
        {
            if (TxtCurveCount != null)
            {
                TxtCurveCount.Text = $"曲线数量: {_chartLineList.Count} | 贝塞尔曲线: {_bezierLineList.Count}";
            }
        }

        private void UpdateOperationStatus(string status)
        {
            if (TxtOperationStatus != null)
            {
                TxtOperationStatus.Text = status;
            }
        }

        private void UpdateStatus(string message)
        {
            if (StatusBarText != null)
            {
                StatusBarText.Text = message;
            }
        }

        private void DrawStatRangeLines()
        {
            // 先清除已有的范围线
            var existingChildren = new List<UIElement>();
            foreach (UIElement child in _rangeLinesDrawingArea.Children)
            {
                if (child is Thumb || child is Line)
                {
                    // 仅移除我们添加的范围线元素，保留其他元素
                    existingChildren.Add(child);
                }
            }
            foreach (var child in existingChildren)
            {
                _rangeLinesDrawingArea.Children.Remove(child);
            }

            var thumbTemplate = new ControlTemplate(typeof(Thumb));

            FrameworkElementFactory lineFactory = new FrameworkElementFactory(typeof(Line), "Line");

            lineFactory.SetValue(Line.StrokeProperty, Brushes.Red);
            lineFactory.SetValue(Line.StrokeDashArrayProperty, new DoubleCollection(new double[] { 4, 2 }));
            lineFactory.SetValue(Line.X1Property, 0d);
            lineFactory.SetValue(Line.Y1Property, 0d);
            lineFactory.SetValue(Line.X2Property, 0d);
            lineFactory.SetValue(Line.Y2Property, _maxCanvasY);

            FrameworkElementFactory lineForDragFactory = new FrameworkElementFactory(typeof(Line), "lineForDrag");

            lineForDragFactory.SetValue(Line.StrokeProperty, Brushes.Transparent);
            lineForDragFactory.SetValue(Line.X1Property, 0d);
            lineForDragFactory.SetValue(Line.Y1Property, 0d);
            lineForDragFactory.SetValue(Line.X2Property, 0d);
            lineForDragFactory.SetValue(Line.Y2Property, _maxCanvasY);
            lineForDragFactory.SetValue(Line.StrokeThicknessProperty, 20d);

            FrameworkElementFactory gridFactory = new FrameworkElementFactory(typeof(Grid), "Grid");
            gridFactory.AppendChild(lineFactory);
            gridFactory.AppendChild(lineForDragFactory);

            thumbTemplate.VisualTree = gridFactory;
            _lineStartThumb.Template = thumbTemplate;
            _lineStartThumb.Cursor = Cursors.Hand;
            _lineStartThumb.DragDelta += OnStatRangeStartLineDrag;
            _lineStartThumb.SetBinding(Control.VisibilityProperty, new Binding("HasCorrectionData")
            {
                Source = this,
                Converter = new BooleanToVisibilityConverter()
            });

            _lineStartThumb.SetBinding(Canvas.LeftProperty, new Binding("RGainStart")
            {
                Source = DataContext,
                Converter = new RGainStatRangeLineBindingConverter(),
                ConverterParameter = _rangeLinesDrawingArea.ActualWidth / _maxChartX,
                Mode = BindingMode.TwoWay
            });

            //_lineStartThumb.SetBinding(Line.X1Property, new Binding("RGainStart")
            //{
            //    Source = DataContext,
            //    Converter = new RGainStatRangeLineBindingConverter(),
            //    ConverterParameter = _rangeLinesDrawingArea.ActualWidth / _maxChartX,
            //});

            // 【关键修复 1】：如果 Thumb 还绑在旧模板的画布上，必须先剥离，否则无法加入新画布
            if (_lineStartThumb.Parent != null)
            {
                ((Panel)_lineStartThumb.Parent).Children.Remove(_lineStartThumb);
            }

            _rangeLinesDrawingArea.Children.Add(_lineStartThumb);

            _lineEnd.Stroke = Brushes.Red;
            _lineEnd.StrokeDashArray = new DoubleCollection(new double[] { 4, 2 });

            _lineEnd.SetBinding(Line.X1Property, new Binding("RGainEnd")
            {
                Source = DataContext,
                Converter = new RGainStatRangeLineBindingConverter(),
                ConverterParameter = _rangeLinesDrawingArea.ActualWidth / _maxChartX,
            });
            _lineEnd.Y1 = 0;

            _lineEnd.SetBinding(Line.X2Property, new Binding("RGainEnd")
            {
                Source = DataContext,
                Converter = new RGainStatRangeLineBindingConverter(),
                ConverterParameter = _rangeLinesDrawingArea.ActualWidth / _maxChartX,
            });
            _lineEnd.Y2 = _maxCanvasY;
            _lineEnd.SetBinding(Control.VisibilityProperty, new Binding("HasCorrectionData")
            {
                Source = this,
                Converter = new BooleanToVisibilityConverter()
            });

            // 【关键修复 2】：Line 对象同理，剥离旧父级
            if (_lineEnd.Parent != null)
            {
                ((Panel)_lineEnd.Parent).Children.Remove(_lineEnd);
            }

            _rangeLinesDrawingArea.Children.Add(_lineEnd);
        }


        void OnStatRangeStartLineDrag(object sender, DragDeltaEventArgs e)
        {
            var thumb = sender as Thumb;

            //_viewModel.RGainStart = (int)((Canvas.GetLeft(thumb) + e.HorizontalChange) / _maxCanvasX * _maxChartX);
            Canvas.SetLeft(thumb, Canvas.GetLeft(thumb) + e.HorizontalChange);
            //Canvas.SetRight(thumb, Canvas.GetRight(thumb) + e.HorizontalChange);

            var lineChangeDelta = e.HorizontalChange * _maxChartX / _rangeLinesDrawingArea.ActualWidth;

            //foreach (var bezierLine in _bezierLineList)
            //{
            //    var tmpStartPoint = bezierLine.StartPoint;
            //    tmpStartPoint.X += e.HorizontalChange;
            //    bezierLine.StartPoint = tmpStartPoint;

            //    var tmpEndPoint = bezierLine.EndPoint;
            //    tmpEndPoint.X += e.HorizontalChange;
            //    bezierLine.EndPoint = tmpEndPoint;

            //    var tmpStartBezierPoint = bezierLine.StartBezierPoint;
            //    tmpStartBezierPoint.X += e.HorizontalChange;
            //    bezierLine.StartBezierPoint = tmpStartBezierPoint;

            //    var tmpEndBezierPoint = bezierLine.EndBezierPoint;
            //    tmpEndBezierPoint.X += e.HorizontalChange;
            //    bezierLine.EndBezierPoint = tmpEndBezierPoint;
            //}

            foreach (var line in _chartLineList)
            {
                ObservableCollection<KeyValuePair<double, double>> tmpDataContext =
                    (ObservableCollection<KeyValuePair<double, double>>)line.DataContext;

                for (int i = 0; i < tmpDataContext.Count; i++)
                {
                    tmpDataContext[i] = new KeyValuePair<double, double>(tmpDataContext[i].Key + lineChangeDelta, tmpDataContext[i].Value);
                }
            }
        }

        private void DrawGainValueRangeLines()
        {
            // 找到之前添加的绿色范围线并移除（避免重复添加）
            // 使用一个列表来存储已经添加的范围线Thumb，以便于清理
            var thumbsToKeep = new List<Thumb>(); // 存储非范围线的Thumb

            var childrenToRemove = new List<UIElement>();
            for (int i = _rangeLinesDrawingArea.Children.Count - 1; i >= 0; i--)
            {
                UIElement child = _rangeLinesDrawingArea.Children[i];

                // 检查是否是我们添加的范围线Thumb（除了_lineStartThumb之外的Thumb）
                if (child is Thumb && child != _lineStartThumb)
                {
                    // 检查是否是_lineEnd（Line类型）
                    if (child != _lineEnd)
                    {
                        childrenToRemove.Add(child);
                    }
                }
            }

            foreach (var child in childrenToRemove)
            {
                _rangeLinesDrawingArea.Children.Remove(child);
            }

            for (int i = 0; i < 2; i++)
            {
                Thumb tmpLineThumb = new Thumb();
                tmpLineThumb.SetBinding(Canvas.LeftProperty, new Binding(i == 0 ? "RGainMin" : "RGainMax")
                {
                    Source = DataContext,
                    Converter = new RGainStatRangeLineBindingConverter(),
                    ConverterParameter = _rangeLinesDrawingArea.ActualWidth / _maxChartX,
                    Mode = BindingMode.TwoWay
                });

                tmpLineThumb.SetBinding(Control.VisibilityProperty, new Binding("HasCorrectionData")
                {
                    Source = this,
                    Converter = new BooleanToVisibilityConverter()
                });

                var thumbTemplate = new ControlTemplate(typeof(Thumb));

                FrameworkElementFactory lineFactory = new FrameworkElementFactory(typeof(Line), "Line");

                lineFactory.SetValue(Line.StrokeProperty, Brushes.Green);
                lineFactory.SetValue(Line.StrokeDashArrayProperty, new DoubleCollection(new double[] { 4, 2 }));
                lineFactory.SetValue(Line.X1Property, 0d);
                lineFactory.SetValue(Line.Y1Property, 0d);
                lineFactory.SetValue(Line.X2Property, 0d);
                lineFactory.SetValue(Line.Y2Property, _maxCanvasY);

                FrameworkElementFactory lineForDragFactory = new FrameworkElementFactory(typeof(Line), "lineForDrag");

                lineForDragFactory.SetValue(Line.StrokeProperty, Brushes.Transparent);
                lineForDragFactory.SetValue(Line.X1Property, 0d);
                lineForDragFactory.SetValue(Line.Y1Property, 0d);
                lineForDragFactory.SetValue(Line.X2Property, 0d);
                lineForDragFactory.SetValue(Line.Y2Property, _maxCanvasY);
                lineForDragFactory.SetValue(Line.StrokeThicknessProperty, 20d);

                FrameworkElementFactory gridFactory = new FrameworkElementFactory(typeof(Grid), "Grid");
                gridFactory.AppendChild(lineFactory);
                gridFactory.AppendChild(lineForDragFactory);

                thumbTemplate.VisualTree = gridFactory;
                tmpLineThumb.Template = thumbTemplate;
                tmpLineThumb.Cursor = Cursors.Hand;

                tmpLineThumb.DragDelta += (object sender, DragDeltaEventArgs e) =>
                {
                    Thumb tmpThumb = (Thumb)sender;

                    Canvas.SetLeft(tmpThumb, Canvas.GetLeft(tmpThumb) + e.HorizontalChange);
                    Canvas.SetRight(tmpThumb, Canvas.GetRight(tmpThumb) + e.HorizontalChange);
                };

                _rangeLinesDrawingArea.Children.Add(tmpLineThumb);
            }
        }

        void OnDataChartMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _panAnchor = e.GetPosition(DataChart);
        }

        void OnDataChartMouseMove(object sender, MouseEventArgs e)
        {
            if (!_canPanYAndCanZoomY)
            {
                return;
            }

            if (e.RightButton == MouseButtonState.Pressed && _panAnchor != null)
            {
                var axisX = (LinearAxis)DataChart.Axes[0];
                var axisY = (LinearAxis)DataChart.Axes[1];
                //axisX.Minimum += _panAnchor.X - e.GetPosition(DataChart).X;
                //axisX.Maximum += _panAnchor.X - e.GetPosition(DataChart).X;

                axisY.Maximum += e.GetPosition(DataChart).Y - _panAnchor.Y;
                axisY.Minimum += e.GetPosition(DataChart).Y - _panAnchor.Y;

                _panAnchor = e.GetPosition(DataChart);
            }
        }

        void OnDataChartMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_canPanYAndCanZoomY)
            {
                return;
            }

            var axisX = (LinearAxis)DataChart.Axes[0];
            var axisY = (LinearAxis)DataChart.Axes[1];

            //axisX.Maximum = axisX.Maximum * (100 - e.Delta / 10) / 100;
            //axisX.Minimum = axisX.Minimum * (100 - e.Delta / 10) / 100;

            axisY.Minimum = axisY.Minimum * (100 - e.Delta / 10) / 100;
            axisY.Maximum = axisY.Maximum * (100 - e.Delta / 10) / 100;
        }

        void StatisticDataCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                    {
                        if (e.NewItems == null)
                        {
                            break;
                        }

                        foreach (ObservableCollection<KeyValuePair<double, double>> newItem in e.NewItems)
                        {
                            // 优先从临时队列中获取颜色（用于贝塞尔曲线投影时的精准着色）
                            Brush lineColor = Brushes.Gray;
                            if (_pendingCurveColors.Count > 0)
                            {
                                lineColor = _pendingCurveColors[0];
                                _pendingCurveColors.RemoveAt(0); // 取出后移除
                            }
                            AddStatLine(newItem, lineColor);
                        }
                    }
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                    {
                        if (e.OldItems == null)
                        {
                            break;
                        }

                        foreach (ObservableCollection<KeyValuePair<double, double>> oldItem in e.OldItems)
                        {
                            RemoveStatLine(oldItem);
                        }
                    }
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                    {
                        if (e.NewItems != null)
                        {
                            foreach (ObservableCollection<KeyValuePair<double, double>> newItem in e.NewItems)
                            {
                                AddStatLine(newItem, Brushes.Gray);
                            }
                        }
                        if (e.OldItems != null)
                        {
                            foreach (ObservableCollection<KeyValuePair<double, double>> oldItem in e.OldItems)
                            {
                                RemoveStatLine(oldItem);
                            }
                        }
                    }
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                    {
                        ClearStatLine();
                        HasCorrectionData = true;
                    }
                    break;
                default:
                    break;
            }
        }

        private Point[] GetBezierLinePoints(BezierFigure bezierLine, int numCut = 8)
        {
            Rect bezierLineBounds = bezierLine.BezierPathGeometry.Bounds;
            double bezierLineBoundsWidth = bezierLineBounds.Width;
            double bezierLineXAxisDivisionValue = bezierLineBoundsWidth / (numCut - 1); //31 分割值，用来取32个点

            Point[] bezierLinePoints = new Point[numCut];
            Geometry og1 = bezierLine.BezierPathGeometry.GetWidenedPathGeometry(new Pen(Brushes.Black, 1.0));
            for (int i = 0; i < numCut; i++)
            {
                var line = new LineGeometry(new Point(bezierLineBounds.Left + bezierLineXAxisDivisionValue * i, 0)
                    , new Point(bezierLineBounds.Left + bezierLineXAxisDivisionValue * i, 3000));
                Geometry og2 = line.GetWidenedPathGeometry(new Pen(Brushes.Black, 1.0));

                CombinedGeometry cg = new CombinedGeometry(GeometryCombineMode.Intersect, og1, og2);
                PathGeometry pg = cg.GetFlattenedPathGeometry();
                Point[] IntersectionPoints = new Point[pg.Figures.Count];

                for (int j = 0; j < pg.Figures.Count; j++)
                {
                    Rect fig = new PathGeometry(new PathFigure[] { pg.Figures[j] }).Bounds;
                    IntersectionPoints[j] = new Point(bezierLineBounds.Left + bezierLineXAxisDivisionValue * i, fig.Top + fig.Height / 2.0);
                }
                bezierLinePoints[i] = IntersectionPoints[0];
            }
            return bezierLinePoints;
        }

        private int _nextBezierLabelIndex = 1;

        // 用于控制是否禁用拖动操作的状态标记
        private bool _isDragDisabled = false;

        /// <summary>
        /// 启用或禁用所有拖动操作
        /// </summary>
        /// <param name="disable">true=禁用拖动, false=启用拖动</param>
        private void SetDragEnabled(bool disable)
        {
            _isDragDisabled = disable;

            // 更新统计范围线的 Thumb 拖动状态
            if (_lineStartThumb != null)
            {
                _lineStartThumb.IsEnabled = !disable;
            }

            // 更新增益值范围线的所有 Thumb 控件
            if (_rangeLinesDrawingArea != null)
            {
                foreach (var child in _rangeLinesDrawingArea.Children)
                {
                    if (child is Thumb thumb && thumb != _lineStartThumb)
                    {
                        thumb.IsEnabled = !disable;
                    }
                }
            }

            UpdateStatus(disable ? "🔒 已禁用范围线拖动（曲线编辑中）" : "🔓 已恢复范围线拖动");
        }

        private void OnAddBezierLine(object sender, RoutedEventArgs e)
        {
            var bezierLine = new BezierFigure();

            Point startPoint = new Point();
            Point endPoint = new Point();
            if (_bezierLineList.Count > 0)
            {
                startPoint = _bezierLineList.Last().StartPoint;
                endPoint = _bezierLineList.Last().EndPoint;
            }
            else
            {
                startPoint.X = _viewModel.RGainStart / _maxChartX * _maxCanvasX;
                startPoint.Y = _maxCanvasY / 2;

                endPoint.X = _viewModel.RGainEnd / _maxChartX * _maxCanvasX;
                endPoint.Y = _maxCanvasY / 2;
            }

            int colorIndex = _bezierLineList.Count % _bezierLineColors.Count;
            bezierLine.BodyColor = new SolidColorBrush(_bezierLineColors[colorIndex]);
            // 设置标注
            bezierLine.Label = $"曲线 {_nextBezierLabelIndex++}";
            bezierLine.StartPoint = startPoint;
            bezierLine.EndPoint = endPoint;
            bezierLine.StartBezierPoint = new Point(startPoint.X + 100, startPoint.Y - 100);
            bezierLine.EndBezierPoint = new Point(endPoint.X - 100, endPoint.Y - 100);
            bezierLine.LockStartPointX = true;
            bezierLine.LockEndPointX = true;
            bezierLine.SelectStateChange += OnBezierLineSelected;
            bezierLine.SetSelectedWithColor(); ;

            _bezierLineDrawingArea.Children.Add(bezierLine);
            _bezierLineList.Add(bezierLine);

            // 【新增】添加第一条曲线后，禁用所有拖动操作
            if (_bezierLineList.Count == 1)
            {
                SetDragEnabled(true);
            }

            if (_bezierLineList.Count >= 4)
            {
                CanAddBezierLine = false;
            }

            UpdateCurveCountDisplay();
            UpdateOperationStatus($"已添加第 {_bezierLineList.Count} 条贝塞尔曲线");
            UpdateStatus($"✓ 新增贝塞尔曲线 (总计: {_bezierLineList.Count}/4)");
        }

        private void OnEditBezierLineOk(object sender, RoutedEventArgs e)
        {
            // 【优化1】：状态拦截 - 如果当前不在绘图模式（画布未显示），直接忽略重复点击
            if (_bezierLineDrawingArea == null || _bezierLineDrawingArea.Visibility != Visibility.Visible)
            {
                UpdateOperationStatus("⚠️ 当前不在绘图模式，无法重复应用");
                return;
            }

            // 【优化2】：安全检查 - 如果没有贝塞尔曲线，也直接返回
            if (_bezierLineList == null || _bezierLineList.Count == 0)
            {
                UpdateOperationStatus("⚠️ 没有可应用的曲线数据");
                return;
            }


            // 【关键修改】：清空现有数据，准备重新填充
            _viewModel.StatisticData.Clear();
            _viewModel.IsLoadFile = false;

            // 【关键修改】：对所有贝塞尔曲线进行8点采样
            for (int i = 0; i < _bezierLineList.Count; i++)
            {
                var points = GetBezierLinePoints(_bezierLineList[i], 8);

                ObservableCollection<KeyValuePair<double, double>> tmpDataContext = new ObservableCollection<KeyValuePair<double, double>>();
                foreach (var item in points)
                {
                    int ActualChartY = (int)((_maxCanvasY - item.Y) / _maxCanvasY * (double)_maxChartY);
                    int ActualChartX = (int)(item.X / _maxCanvasX * (double)_maxChartX);

                    tmpDataContext.Add(new KeyValuePair<double, double>(ActualChartX, ActualChartY));
                }

                // 将8个控制点数据添加到StatisticData
                _viewModel.StatisticData.Add(tmpDataContext);
            }

            // 【关键修改】：备份当前StatisticData，用于后续恢复或对比
            _backupStatisticData = DeepCopyStatisticData(_viewModel.StatisticData);

            _bezierLineDrawingArea.Children.Clear();

            UpdateOperationStatus($"✓ 已将所有 {_bezierLineList.Count} 条曲线转换为8点编辑模式");
            UpdateStatus($"正在编辑 {_bezierLineList.Count} 条曲线（每条8个控制点）");

            SetDragEnabled(true);
        }

        // 在类中添加备份字段
        private ObservableCollection<ObservableCollection<KeyValuePair<double, double>>> _backupStatisticData;


        private void OnEditBezierLineCompleted(object sender, RoutedEventArgs e)
        {
            // 【安全检查】：确保有备份数据
            if (_backupStatisticData == null)
            {
                UpdateOperationStatus("⚠️ 没有可完成的编辑操作，请先进行8点调整");
                return;
            }

            // 【关键修改】：检查是否有编辑后的数据
            if (_viewModel.StatisticData == null || _viewModel.StatisticData.Count == 0)
            {
                UpdateOperationStatus("⚠️ 没有可应用的曲线数据");
                return;
            }

            var points = _backupStatisticData;

            if (points.Count > 0)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    Point[] temp_points = points[i].Select(kvp => new Point(kvp.Key, kvp.Value)).ToArray();
                    if (temp_points.Length == 8)
                    {
                        Point[] sampledPoints = GeneratePointsFromControlPoints(temp_points, 32);

                        _viewModel.StatisticData[i].Clear();
                        ObservableCollection<KeyValuePair<double, double>> tmpDataContext = new ObservableCollection<KeyValuePair<double, double>>();
                        foreach (var item in sampledPoints)
                        {
                            tmpDataContext.Add(new KeyValuePair<double, double>(item.X, item.Y));
                        }

                        // 【关键修改】：如果有对应的贝塞尔曲线，提取其颜色
                        if (i < _bezierLineList.Count)
                        {
                            Brush curveColor = _bezierLineList[i].OriginalColor;
                            _pendingCurveColors.Add(curveColor);
                        }

                        // 【修复3】：替换而不是添加
                        _viewModel.StatisticData[i] = tmpDataContext;
                    }
                }

            }
            // 投影完成后清空队列（正常情况下事件同步执行，这里应该已经是空的了）
            _pendingCurveColors.Clear();

            _bezierLineList.Clear();
            HasSelectedBezierLine = false;
            CanAddBezierLine = true;
            _nextBezierLabelIndex = 1; // 重置编号计数器

            // 清理备份
            _backupStatisticData = null;
            _bezierLineDrawingArea.Visibility = System.Windows.Visibility.Collapsed;
            TraditionalModeTabs.SelectedIndex = 0;

            HasCorrectionData = _chartLineList.Count > 0;

            UpdateCurveCountDisplay();
            UpdateOperationStatus($"已应用 {_chartLineList.Count} 条增益曲线");
            UpdateStatus($"✓ 曲线应用完成 (共 {_chartLineList.Count} 条统计线)");
        }

        void OnBezierLineSelected(BezierFigure bezierLine, bool isSelected)
        {
            if (!isSelected)
            {
                _currentSelectBezierLine = null;
                HasSelectedBezierLine = false;
            }
            else
            {
                if (_currentSelectBezierLine != null)
                {
                    _currentSelectBezierLine.SetUnSelected();
                }

                _currentSelectBezierLine = bezierLine;
                HasSelectedBezierLine = true;
            }
        }

        private void ProjectionBezierLinesToChart()
        {
            // 投影前清空颜色队列
            _pendingCurveColors.Clear();

            for (int i = 0; i < _bezierLineList.Count; i++)
            {
                var points = GetBezierLinePoints(_bezierLineList[i], 32);

                if (points.Length == 8)
                {
                    Point[] sampledPoints = GeneratePointsFromControlPoints(points, 32);
                    points = sampledPoints;
                }

                ObservableCollection<KeyValuePair<double, double>> tmpDataContext = new ObservableCollection<KeyValuePair<double, double>>();
                foreach (var item in points)
                {
                    int ActualChartY = (int)((_maxCanvasY - item.Y) / _maxCanvasY * (double)_maxChartY);
                    int ActualChartX = (int)(item.X / _maxCanvasX * (double)_maxChartX);

                    tmpDataContext.Add(new KeyValuePair<double, double>(ActualChartX, ActualChartY));
                }

                // 【关键修改】：提取贝塞尔曲线的原始颜色（注意用 OriginalColor，因为此时可能是选中状态的蓝色）
                Brush curveColor = _bezierLineList[i].OriginalColor;
                _pendingCurveColors.Add(curveColor);

                _viewModel.StatisticData.Add(tmpDataContext);

            }
            // 投影完成后清空队列（正常情况下事件同步执行，这里应该已经是空的了）
            _pendingCurveColors.Clear();
        }

        private void AddStatLine(ObservableCollection<KeyValuePair<double, double>> tmpDataContext, Brush lineColor)
        {
            LineSeries tmpChartLine = new LineSeries();
            tmpChartLine.TransitionDuration = new TimeSpan(0);
            tmpChartLine.DependentValuePath = "Value";
            tmpChartLine.IndependentValuePath = "Key";

            // 设置线条样式（连接数据点的线）
            // WPF Toolkit LineSeries 使用 Polyline 绘制线条
            Style lineStyle = new Style(typeof(Polyline));
            lineStyle.Setters.Add(new Setter(Polyline.StrokeProperty, lineColor));
            lineStyle.Setters.Add(new Setter(Polyline.StrokeThicknessProperty, 3.0));
            tmpChartLine.PolylineStyle = lineStyle;

            // 设置数据点样式
            Style dataPointStyle = new Style(typeof(LineDataPoint));
            dataPointStyle.Setters.Add(new Setter(LineDataPoint.BackgroundProperty, lineColor));
            dataPointStyle.Setters.Add(new Setter(LineDataPoint.WidthProperty, 8.0));
            dataPointStyle.Setters.Add(new Setter(LineDataPoint.HeightProperty, 8.0));
            tmpChartLine.DataPointStyle = dataPointStyle;

            // 直接设置ItemsSource（不需要Binding）
            tmpChartLine.ItemsSource = tmpDataContext;
            tmpChartLine.DataContext = tmpDataContext;

            tmpChartLine.IsSelectionEnabled = true;
            tmpChartLine.SelectionChanged += OnChartLineSelectionChanged;
            tmpChartLine.MouseLeftButtonUp += OnChartLineMouseLeftButtonUp;

            _chartLineList.Add(tmpChartLine);
            HasCorrectionData = _chartLineList.Count > 0;
            DataChart.Series.Add(tmpChartLine);

            // 强制刷新图表布局
            DataChart.UpdateLayout();

            UpdateCurveCountDisplay();
        }

        void OnDragChartlinePoint(object sender, MouseEventArgs e)
        {
            try
            {
                if (_currentSelectedChartLine != null)
                {
                    // 【修复】防止除以零：窗口首次打开时 Canvas 尺寸可能尚未初始化
                    if (_maxCanvasX == 0 || _maxCanvasY == 0)
                        return;

                    var pos = e.GetPosition(_bezierLineDrawingArea);

                    double ActualChartX = pos.X / _maxCanvasX * (double)_maxChartX;
                    double ActualChartY = (_maxCanvasY - pos.Y) / _maxCanvasY * (double)_maxChartY;

                    // 【关键修复】MouseMove 期间不直接修改集合，只记录待提交值
                    // 直接修改 KeyValuePair 会触发 CollectionChanged(Replace)，
                    // WPF Toolkit LineSeries 内部 DataPoint 视觉绑定会失效 → NullReferenceException
                    _pendingDragValue = ActualChartY;
                }
            }
            catch { }
        }

        void OnChartLineSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0)
            {
                return;
            }
            try
            {
                _currentSelectedChartLine = (LineSeries)sender;
                var dataCollection = _currentSelectedChartLine.DataContext as ObservableCollection<KeyValuePair<double, double>>;
                if (dataCollection == null)
                {
                    _currentSelectedChartLine = null;
                    return;
                }

                KeyValuePair<double, double> valPair = (KeyValuePair<double, double>)e.AddedItems[0];

                // 【修复】使用"找到最接近的点"策略，避免浮点精度问题导致匹配失败
                // WPF Toolkit 图表控件在选中时可能返回数据副本，Key 值可能存在微小差异
                // 直接找到 Key 值最接近的点，比使用容差更可靠
                int closestIndex = -1;
                double minDiff = double.MaxValue;
                for (int i = 0; i < dataCollection.Count; i++)
                {
                    double diff = Math.Abs(dataCollection[i].Key - valPair.Key);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        closestIndex = i;
                    }
                }

                // 【修复】只有当最接近的点足够接近时才认为匹配成功
                // 使用相对容差验证，避免误匹配到错误的点
                if (closestIndex >= 0)
                {
                    double maxAbs = Math.Max(Math.Abs(dataCollection[closestIndex].Key), Math.Abs(valPair.Key));
                    double tolerance = Math.Max(maxAbs * 1e-6, 1e-6); // 使用更宽松的容差 1e-6
                    if (minDiff <= tolerance)
                    {
                        // 【关键修复】只记录索引，不修改集合
                        // 之前的实现用相同值重新赋值，虽然值相同但会触发 CollectionChanged 事件
                        // 在 SelectionChanged 处理过程中触发 CollectionChanged 会导致 Toolkit 内部
                        // 正在访问的 DataPoint 视觉对象失效，引发 NullReferenceException
                        _currentSelectedChartLinePointIndex = closestIndex;
                    }
                    else
                    {
                        // 最接近的点仍然差异过大，说明数据不匹配
                        _currentSelectedChartLine = null;
                        Debug.WriteLine($"⚠️ 未找到匹配的数据点，最小差异: {minDiff}, 容差: {tolerance}");
                    }
                }
                else
                {
                    _currentSelectedChartLine = null;
                }
            }
            catch
            {
                //_currentSelectedChartLine = null;
                Debug.WriteLine("⚠️ 选中曲线点时发生异常，可能是数据索引不匹配");
            }
        }

        void OnChartLineMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentSelectedChartLine != null && _pendingDragValue.HasValue)
            {
                var selectedLine = _currentSelectedChartLine;
                var selectedIndex = _currentSelectedChartLinePointIndex;
                var pendingValue = _pendingDragValue.Value;

                // 【关键修复】先清除选中状态，让 Toolkit 内部完全释放对旧 DataPoint 的引用
                // 再通过 Dispatcher 延迟更新数据，避免 CollectionChanged 处理期间 Toolkit 重入访问已失效的引用
                _currentSelectedChartLine = null;
                _pendingDragValue = null;
                selectedLine.SelectedItem = null;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var dataCollection = selectedLine.DataContext as ObservableCollection<KeyValuePair<double, double>>;
                    if (dataCollection != null && selectedIndex >= 0 && selectedIndex < dataCollection.Count)
                    {
                        // 【关键修复】创建全新的 ObservableCollection 替换旧的，而不是修改原集合
                        // KeyValuePair 是值类型，替换元素会触发 CollectionChanged，Toolkit 在处理时
                        // 会尝试更新旧的 DataPoint 视觉对象，但此时其绑定已失效导致 NullReferenceException
                        // 创建新集合并整体替换 ItemsSource，强制 Toolkit 丢弃旧 DataPoint 并重建全新的
                        var newCollection = new ObservableCollection<KeyValuePair<double, double>>(dataCollection);
                        newCollection[selectedIndex] =
                            new KeyValuePair<double, double>(newCollection[selectedIndex].Key, pendingValue);
                        selectedLine.ItemsSource = newCollection;
                        selectedLine.DataContext = newCollection;

                        // 同步回 ViewModel，保持数据一致性（供导出/保存使用）
                        // 临时取消事件监听避免 StatisticDataCollectionChanged 的 Replace 处理
                        // 导致重复添加 LineSeries
                        int lineIdx = _chartLineList.IndexOf(selectedLine);
                        if (lineIdx >= 0 && lineIdx < _viewModel.StatisticData.Count)
                        {
                            _viewModel.StatisticData.CollectionChanged -= StatisticDataCollectionChanged;
                            try
                            {
                                _viewModel.StatisticData[lineIdx] = newCollection;
                            }
                            finally
                            {
                                _viewModel.StatisticData.CollectionChanged += StatisticDataCollectionChanged;
                            }
                        }
                    }
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
            else if (_currentSelectedChartLine != null)
            {
                _currentSelectedChartLine.SelectedItem = null;
                _currentSelectedChartLine = null;
            }
        }

        void OnChartAreaMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentSelectedChartLine != null)
            {
                OnChartLineMouseLeftButtonUp(null, null);
            }
        }

        private void OnDrawBezierLineOk(object sender, RoutedEventArgs e)
        {
            // 【优化1】：状态拦截 - 如果当前不在绘图模式（画布未显示），直接忽略重复点击
            if (_bezierLineDrawingArea == null || _bezierLineDrawingArea.Visibility != Visibility.Visible)
            {
                UpdateOperationStatus("⚠️ 当前不在绘图模式，无法重复应用");
                return;
            }

            // 【优化2】：安全检查 - 如果没有贝塞尔曲线，也直接返回
            if (_bezierLineList == null || _bezierLineList.Count == 0)
            {
                UpdateOperationStatus("⚠️ 没有可应用的曲线数据");
                return;
            }

            ProjectionBezierLinesToChart();

            // 【优化3】：应用成功后，清空画布中的贝塞尔曲线元素和临时列表，释放资源
            _bezierLineDrawingArea.Children.Clear();
            _bezierLineList.Clear();
            _currentSelectBezierLine = null;
            HasSelectedBezierLine = false;
            CanAddBezierLine = true;
            _nextBezierLabelIndex = 1; // 重置编号计数器

            // 【新增】应用曲线后，恢复拖动操作
            SetDragEnabled(true);

            _bezierLineDrawingArea.Visibility = System.Windows.Visibility.Collapsed;
            TraditionalModeTabs.SelectedIndex = 0;

            HasCorrectionData = _chartLineList.Count > 0;

            UpdateCurveCountDisplay();
            UpdateOperationStatus($"已应用 {_chartLineList.Count} 条增益曲线");
            UpdateStatus($"✓ 曲线应用完成 (共 {_chartLineList.Count} 条统计线)");
        }

        private void OnDrawBezierLineCancel(object sender, RoutedEventArgs e)
        {
            // 【新增】取消编辑时，清除所有贝塞尔曲线并恢复拖动
            _bezierLineDrawingArea.Children.Clear();
            _bezierLineList.Clear();
            _currentSelectBezierLine = null;
            HasSelectedBezierLine = false;
            CanAddBezierLine = true;
            _nextBezierLabelIndex = 1;

            SetDragEnabled(false);

            _bezierLineDrawingArea.Visibility = System.Windows.Visibility.Collapsed;
            TraditionalModeTabs.SelectedIndex = 0;

            HasCorrectionData = _chartLineList.Count > 0;

            UpdateOperationStatus("已取消绘图模式");
            UpdateStatus("⚠️ 已取消曲线编辑操作");
        }

        private void OnRemoveBezierLine(object sender, RoutedEventArgs e)
        {
            _bezierLineDrawingArea.Children.Remove(_currentSelectBezierLine);
            _bezierLineList.Remove(_currentSelectBezierLine);

            // 重新分配编号和颜色，确保颜色始终与列表索引绑定
            for (int i = 0; i < _bezierLineList.Count; i++)
            {
                int colorIndex = i % _bezierLineColors.Count;
                var newColor = new SolidColorBrush(_bezierLineColors[colorIndex]);

                _bezierLineList[i].Label = $"曲线 {i + 1}";
                _bezierLineList[i].OriginalColor = newColor; // 更新它的“原始颜色”

                // 如果当前曲线没被选中，直接刷新显示为新分配的颜色
                if (!_bezierLineList[i].IsSelected)
                {
                    _bezierLineList[i].BodyColor = newColor;
                }
            }
            _nextBezierLabelIndex = _bezierLineList.Count + 1;

            // 【新增】删除曲线后，如果没有曲线了，恢复拖动操作
            if (_bezierLineList.Count == 0)
            {
                SetDragEnabled(false);
            }

            _currentSelectBezierLine = null;
            HasSelectedBezierLine = false;
            CanAddBezierLine = true;

            UpdateCurveCountDisplay();
            UpdateOperationStatus("已删除选中的贝塞尔曲线");
            UpdateStatus("✓ 已删除贝塞尔曲线");
        }

        private void OnBeginDrawBezierLine(object sender, RoutedEventArgs e)
        {
            if (_chartLineList.Count != 0)
            {
                var result = MessageBox.Show(this, "切换绘图模式将清除现有数据，是否继续？", "", MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            TraditionalModeTabs.SelectedIndex = 1;
            _bezierLineDrawingArea.Visibility = System.Windows.Visibility.Visible;
            HasCorrectionData = true;

            _viewModel.StatisticData.Clear();

            UpdateOperationStatus("已进入贝塞尔曲线绘图模式");
            UpdateStatus("✏️ 绘图模式 - 请在图表上绘制增益映射曲线");
        }

        private void RemoveStatLine(ObservableCollection<KeyValuePair<double, double>> tmpDataContext)
        {
            var removeLine = _chartLineList.Find(line => line.DataContext.Equals(tmpDataContext));
            DataChart.Series.Remove(removeLine);
            _chartLineList.Remove(removeLine);

            HasCorrectionData = _chartLineList.Count > 0;

            UpdateCurveCountDisplay();
        }

        private void ClearStatLine()
        {
            foreach (var item in _chartLineList)
            {
                item.SelectionChanged -= OnChartLineSelectionChanged;   // 解事件
                item.MouseLeftButtonUp -= OnChartLineMouseLeftButtonUp;
                item.ItemsSource = null;
                DataChart.Series.Remove(item);
            }
            _chartLineList.Clear();
            HasCorrectionData = _chartLineList.Count > 0;

            UpdateCurveCountDisplay();
            UpdateStatus("🗑️ 已清除所有统计数据");
        }

        /// <summary>
        /// 从 StatisticData 重建图表显示（用于导入数据后的显示）
        /// 类似 ProjectionBezierLinesToChart 的处理，但数据已经是图表坐标，不需要Canvas转换
        /// </summary>
        private void RebuildChartFromStatisticData()
        {
            // 先清空现有曲线
            ClearStatLine();

            if (_viewModel == null || _viewModel.StatisticData == null)
                return;

            // 预定义曲线颜色数组（类似专业图表软件）
            Brush[] curveColors = new Brush[]
            {
                Brushes.DodgerBlue,
                Brushes.OrangeRed,
                Brushes.ForestGreen,
                Brushes.Purple,
            };

            // 遍历所有曲线数据，重新创建 LineSeries
            for (int i = 0; i < _viewModel.StatisticData.Count; i++)
            {
                var curveData = _viewModel.StatisticData[i];
                if (curveData == null || curveData.Count == 0)
                    continue;

                // 循环使用预定义颜色
                Brush lineColor = curveColors[i % curveColors.Length];

                // 直接调用 AddStatLine 来创建 LineSeries
                AddStatLine(curveData, lineColor);
            }

            // 【修复】数据导入后刷新画布引用，确保拖动事件订阅正确
            // 如果图表模板在 Window_Loaded 后被重建，_chartArea 引用会失效
            RedrawRangeLines();

            UpdateOperationStatus($"✓ 已导入 {_viewModel.StatisticData.Count} 条增益曲线");
            UpdateStatus($"✓ 数据导入完成 (共 {_chartLineList.Count} 条统计线)");
        }

        // ===== 新增：曲线优化相关事件处理 =====

        /// <summary>
        /// 从8个控制点生成分段贝塞尔曲线并采样32个点
        /// </summary>
        private Point[] GeneratePointsFromControlPoints(Point[] controlPoints, int sampleCount = 32)
        {
            if (controlPoints == null || controlPoints.Length < 2)
                throw new ArgumentException("至少需要2个控制点");

            // 1. 计算分段贝塞尔曲线
            var segments = CalculateBezierSegments(controlPoints);

            // 2. 从分段曲线采样
            return SampleFromBezierSegments(segments, sampleCount);
        }

        /// <summary>
        /// 计算分段三次贝塞尔曲线（保证C1连续性）
        /// </summary>
        private BezierSegment[] CalculateBezierSegments(Point[] controlPoints)
        {
            int segmentCount = controlPoints.Length - 1;
            var segments = new BezierSegment[segmentCount];

            for (int i = 0; i < segmentCount; i++)
            {
                Point p0 = controlPoints[i];
                Point p3 = controlPoints[i + 1];

                // 计算切线向量
                Vector tangent;
                if (i == 0)
                {
                    // 第一段：使用前向差分
                    tangent = p3 - p0;
                }
                else if (i == segmentCount - 1)
                {
                    // 最后一段：使用后向差分
                    tangent = p3 - controlPoints[i - 1];
                }
                else
                {
                    // 中间段：使用中心差分（更平滑）
                    tangent = (controlPoints[i + 1] - controlPoints[i - 1]) / 2.0;
                }

                // 归一化切线
                double tangentLength = tangent.Length;
                if (tangentLength < 1e-6)
                {
                    // 退化情况：两点重合，使用水平切线
                    tangent = new Vector(1, 0);
                    tangentLength = 1;
                }
                tangent /= tangentLength;

                // 控制点距离（取段长的1/3，可根据需要调整张力）
                double segmentLength = (p3 - p0).Length;
                double controlDistance = segmentLength / 3.0;

                // 计算两个中间控制点
                Point p1 = p0 + tangent * controlDistance;
                Point p2 = p3 - tangent * controlDistance;

                segments[i] = new BezierSegment(p0, p1, p2, p3);
            }

            return segments;
        }

        /// <summary>
        /// 从分段贝塞尔曲线等弧长采样
        /// </summary>
        private Point[] SampleFromBezierSegments(BezierSegment[] segments, int sampleCount)
        {
            if (segments == null || segments.Length == 0)
                return new Point[0];

            // 预计算每段的近似长度
            double[] segmentLengths = new double[segments.Length];
            double totalLength = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                segmentLengths[i] = segments[i].ApproximateLength();
                totalLength += segmentLengths[i];
            }

            // 避免除零
            if (totalLength < 1e-6)
            {
                // 所有点重合的特殊情况
                Point fallback = segments[0].P0;
                return Enumerable.Repeat(fallback, sampleCount).ToArray();
            }

            Point[] result = new Point[sampleCount];
            double step = totalLength / (sampleCount - 1);

            int currentSegment = 0;
            double accumulatedLength = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                double targetLength = i * step;

                // 找到目标长度所在的段
                while (currentSegment < segments.Length - 1 &&
                       accumulatedLength + segmentLengths[currentSegment] < targetLength)
                {
                    accumulatedLength += segmentLengths[currentSegment];
                    currentSegment++;
                }

                // 在当前段内的相对参数
                double remainingLength = targetLength - accumulatedLength;
                double t = remainingLength / segmentLengths[currentSegment];
                t = Math.Max(0, Math.Min(1, t)); // 夹紧到[0,1]

                // 使用De Casteljau算法求值
                result[i] = segments[currentSegment].Evaluate(t);
            }

            return result;
        }

        /// <summary>
        /// 三次贝塞尔曲线段
        /// </summary>
        private struct BezierSegment
        {
            public Point P0, P1, P2, P3;

            public BezierSegment(Point p0, Point p1, Point p2, Point p3)
            {
                P0 = p0;
                P1 = p1;
                P2 = p2;
                P3 = p3;
            }

            /// <summary>
            /// De Casteljau算法求值
            /// </summary>
            public Point Evaluate(double t)
            {
                double u = 1 - t;

                // 第一层插值
                Point a = new Point(u * P0.X + t * P1.X, u * P0.Y + t * P1.Y);
                Point b = new Point(u * P1.X + t * P2.X, u * P1.Y + t * P2.Y);
                Point c = new Point(u * P2.X + t * P3.X, u * P2.Y + t * P3.Y);

                // 第二层插值
                Point d = new Point(u * a.X + t * b.X, u * a.Y + t * b.Y);
                Point e = new Point(u * b.X + t * c.X, u * b.Y + t * c.Y);

                // 第三层插值（最终结果）
                return new Point(u * d.X + t * e.X, u * d.Y + t * e.Y);
            }

            /// <summary>
            /// 近似长度计算（控制多边形长度）
            /// </summary>
            public double ApproximateLength()
            {
                return (P0 - P1).Length + (P1 - P2).Length + (P2 - P3).Length;
            }


        }


        /// <summary>
        /// 深拷贝 StatisticData（嵌套的 ObservableCollection）
        /// </summary>
        /// <param name="source">源数据</param>
        /// <returns>深拷贝后的数据</returns>
        private ObservableCollection<ObservableCollection<KeyValuePair<double, double>>> DeepCopyStatisticData(
            ObservableCollection<ObservableCollection<KeyValuePair<double, double>>> source)
        {
            if (source == null)
                return null;

            var copy = new ObservableCollection<ObservableCollection<KeyValuePair<double, double>>>();

            foreach (var innerCollection in source)
            {
                // 对每个内部的 ObservableCollection 进行深拷贝
                var innerCopy = new ObservableCollection<KeyValuePair<double, double>>();

                foreach (var kvp in innerCollection)
                {
                    // KeyValuePair 是值类型，直接添加即可（已经是深拷贝）
                    innerCopy.Add(new KeyValuePair<double, double>(kvp.Key, kvp.Value));
                }

                copy.Add(innerCopy);
            }

            return copy;
        }

    }
}
