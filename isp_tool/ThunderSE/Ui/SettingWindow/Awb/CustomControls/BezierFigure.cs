using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ThunderSE.Ui.SettingWindow.Awb.CustomControls
{
    public delegate void SelectStateChangeHandler(BezierFigure bezierLine, bool isSelected);
    public class BezierFigure : Control
    {
        public event SelectStateChangeHandler SelectStateChange;

        // 新增：缓存模板中的 Path 对象
        private Path _bodyPath;
        private Path _pathForSelect;
        private Path _startVectorPath;
        private Path _endVectorPath;

        #region StartPoint
        public static readonly DependencyProperty StartPointProperty = DependencyProperty.Register(
            "StartPoint", typeof(Point), typeof(BezierFigure),
            new FrameworkPropertyMetadata(new Point(), OnPointChanged)); // 增加回调

        public Point StartPoint
        {
            get { return (Point)GetValue(StartPointProperty); }
            set { SetValue(StartPointProperty, value); }
        }
        #endregion

        #region EndPoint
        public static readonly DependencyProperty EndPointProperty = DependencyProperty.Register(
            "EndPoint", typeof(Point), typeof(BezierFigure),
            new FrameworkPropertyMetadata(new Point(), OnPointChanged)); // 增加回调

        public Point EndPoint
        {
            get { return (Point)GetValue(EndPointProperty); }
            set { SetValue(EndPointProperty, value); }
        }
        #endregion

        #region StartBezierPoint
        public static readonly DependencyProperty StartBezierPointProperty = DependencyProperty.Register(
            "StartBezierPoint", typeof(Point), typeof(BezierFigure),
            new FrameworkPropertyMetadata(new Point(), OnPointChanged)); // 增加回调

        public Point StartBezierPoint
        {
            get { return (Point)GetValue(StartBezierPointProperty); }
            set { SetValue(StartBezierPointProperty, value); }
        }
        #endregion

        #region EndBezierPoint
        public static readonly DependencyProperty EndBezierPointProperty = DependencyProperty.Register(
            "EndBezierPoint", typeof(Point), typeof(BezierFigure),
            new FrameworkPropertyMetadata(new Point(), OnPointChanged)); // 增加回调

        public Point EndBezierPoint
        {
            get { return (Point)GetValue(EndBezierPointProperty); }
            set { SetValue(EndBezierPointProperty, value); }
        }
        #endregion

        #region LockPoints
        public static readonly DependencyProperty LockStartPointXProperty = DependencyProperty.Register("LockStartPointX", typeof(bool), typeof(BezierFigure), new FrameworkPropertyMetadata(false));
        public bool LockStartPointX { get { return (bool)GetValue(LockStartPointXProperty); } set { SetValue(LockStartPointXProperty, value); } }

        public static readonly DependencyProperty LockStartPointYProperty = DependencyProperty.Register("LockStartPointY", typeof(bool), typeof(BezierFigure), new FrameworkPropertyMetadata(false));
        public bool LockStartPointY { get { return (bool)GetValue(LockStartPointYProperty); } set { SetValue(LockStartPointYProperty, value); } }

        public static readonly DependencyProperty LockEndPointXProperty = DependencyProperty.Register("LockEndPointX", typeof(bool), typeof(BezierFigure), new FrameworkPropertyMetadata(false));
        public bool LockEndPointX { get { return (bool)GetValue(LockEndPointXProperty); } set { SetValue(LockEndPointXProperty, value); } }

        public static readonly DependencyProperty LockEndPointYProperty = DependencyProperty.Register("LockEndPointY", typeof(bool), typeof(BezierFigure), new FrameworkPropertyMetadata(false));
        public bool LockEndPointY { get { return (bool)GetValue(LockEndPointYProperty); } set { SetValue(LockEndPointYProperty, value); } }
        #endregion

        #region Points
        // 修改：直接返回缓存的字段，避免每次 FindName
        public Path PathForSelect => _pathForSelect;

        // 修改：直接从缓存的 BodyPath 获取 Data
        public PathGeometry BezierPathGeometry => _bodyPath?.Data as PathGeometry;
        #endregion

        #region misc
        public static readonly DependencyProperty BodyColorProperty = DependencyProperty.Register("BodyColor", typeof(Brush), typeof(BezierFigure), new FrameworkPropertyMetadata(Brushes.Red));
        public Brush BodyColor { get { return (Brush)GetValue(BodyColorProperty); } set { SetValue(BodyColorProperty, value); } }

        public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register("IsSelected", typeof(bool), typeof(BezierFigure), new FrameworkPropertyMetadata(true));
        public bool IsSelected { get { return (bool)GetValue(IsSelectedProperty); } set { SetValue(IsSelectedProperty, value); } }

        public static readonly DependencyProperty CurveIndexProperty = DependencyProperty.Register("CurveIndex", typeof(int), typeof(BezierFigure), new FrameworkPropertyMetadata(-1));
        public int CurveIndex { get { return (int)GetValue(CurveIndexProperty); } set { SetValue(CurveIndexProperty, value); } }

        public static readonly DependencyProperty CurveLabelProperty = DependencyProperty.Register("CurveLabel", typeof(string), typeof(BezierFigure), new FrameworkPropertyMetadata(string.Empty));
        public string CurveLabel { get { return (string)GetValue(CurveLabelProperty); } set { SetValue(CurveLabelProperty, value); } }

        private Brush _originalColor;
        public Brush OriginalColor { get { return _originalColor ?? Brushes.Red; } set { _originalColor = value; } }
        #endregion

        #region Label
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register("Label", typeof(string), typeof(BezierFigure), new FrameworkPropertyMetadata(string.Empty));
        public string Label { get { return (string)GetValue(LabelProperty); } set { SetValue(LabelProperty, value); } }
        #endregion

        public BezierFigure()
        {
            Loaded += BezierFigure_Loaded;
        }

        // 新增：重写 OnApplyTemplate 获取模板内部的 Path 控件
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _bodyPath = GetTemplateChild("BodyPath") as Path;
            _pathForSelect = GetTemplateChild("PathForSelect") as Path;
            _startVectorPath = GetTemplateChild("StartVectorPath") as Path;
            _endVectorPath = GetTemplateChild("EndVectorPath") as Path;
            UpdatePathData();
        }

        // 新增：坐标点属性变更时的回调
        private static void OnPointChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var figure = d as BezierFigure;
            figure?.UpdatePathData();
        }

        // 新增：核心方法，使用代码构建 PathGeometry
        private void UpdatePathData()
        {
            if (_bodyPath == null) return;

            // 1. 构建/更新主贝塞尔曲线
            var bezierGeo = new PathGeometry();
            bezierGeo.Figures.Add(new PathFigure
            {
                StartPoint = StartPoint,
                Segments = new PathSegmentCollection
                {
                    new BezierSegment(StartBezierPoint, EndBezierPoint, EndPoint, true)
                }
            });
            _bodyPath.Data = bezierGeo;

            // 2. 构建/更新透明点击/选中区域
            if (_pathForSelect != null)
            {
                var selectGeo = new PathGeometry();
                selectGeo.Figures.Add(new PathFigure
                {
                    StartPoint = StartPoint,
                    Segments = new PathSegmentCollection
                    {
                        new BezierSegment(StartBezierPoint, EndBezierPoint, EndPoint, true)
                    }
                });
                _pathForSelect.Data = selectGeo;
            }

            // 3. 构建/更新起点控制向量线
            if (_startVectorPath != null)
            {
                var startVecGeo = new PathGeometry();
                startVecGeo.Figures.Add(new PathFigure
                {
                    StartPoint = StartPoint,
                    Segments = new PathSegmentCollection
                    {
                        new LineSegment(StartBezierPoint, true)
                    }
                });
                _startVectorPath.Data = startVecGeo;
            }

            // 4. 构建/更新终点控制向量线
            if (_endVectorPath != null)
            {
                var endVecGeo = new PathGeometry();
                endVecGeo.Figures.Add(new PathFigure
                {
                    StartPoint = EndPoint,
                    Segments = new PathSegmentCollection
                    {
                        new LineSegment(EndBezierPoint, true)
                    }
                });
                _endVectorPath.Data = endVecGeo;
            }
        }

        private void BezierFigure_Loaded(object sender, RoutedEventArgs e)
        {
            this.MouseLeftButtonDown += OnMouseLeftButtonDown;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsSelected) SetSelected();
        }

        public void SetUnSelected() { BodyColor = OriginalColor; IsSelected = false; SelectStateChange?.Invoke(this, false); }
        public void SetSelected() { BodyColor = Brushes.Blue; IsSelected = true; SelectStateChange?.Invoke(this, true); }
        public void SetSelectedWithColor() { OriginalColor = BodyColor; BodyColor = Brushes.Blue; IsSelected = true; SelectStateChange?.Invoke(this, true); }

        static BezierFigure()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(BezierFigure), new FrameworkPropertyMetadata(typeof(BezierFigure)));
        }
    }
}


//using System;
//using System.Reflection;
//using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Input;
//using System.Windows.Media;
//using System.Windows.Shapes;

//namespace ThunderSE.Ui.SettingWindow.Awb.CustomControls
//{
//    /// <summary>
//    /// The BezierFigure control is very simple: it just has 4 dependency properties of type Point. 
//    /// The interesting part of the BezierFigure control is its template: see Window1.xaml.
//    /// </summary>
//    /// 

//    public delegate void SelectStateChangeHandler(BezierFigure bezierLine, bool isSelected);

//    public class BezierFigure : Control
//    {
//        public event SelectStateChangeHandler SelectStateChange;

//        #region StartPoint
//        /// <summary>
//        /// StartPoint Dependency Property
//        /// </summary>
//        public static readonly DependencyProperty StartPointProperty = DependencyProperty.Register(
//                "StartPoint", 
//                typeof(Point), 
//                typeof(BezierFigure),
//                new FrameworkPropertyMetadata(new Point()));

//        /// <summary>
//        /// Gets or sets the StartPoint property
//        /// </summary>
//        public Point StartPoint
//        {
//            get { return (Point)GetValue(StartPointProperty); }
//            set { SetValue(StartPointProperty, value); }
//        }
//        #endregion

//        #region EndPoint
//        /// <summary>
//        /// EndPoint Dependency Property
//        /// </summary>
//        public static readonly DependencyProperty EndPointProperty = DependencyProperty.Register(
//                "EndPoint",
//                typeof(Point),
//                typeof(BezierFigure),
//                new FrameworkPropertyMetadata(new Point()));

//        /// <summary>
//        /// Gets or sets the EndPoint property
//        /// </summary>
//        public Point EndPoint
//        {
//            get { return (Point)GetValue(EndPointProperty); }
//            set { SetValue(EndPointProperty, value); }
//        }
//        #endregion

//        #region StartBezierPoint
//        /// <summary>
//        /// StartBezierPoint Dependency Property
//        /// </summary>
//        public static readonly DependencyProperty StartBezierPointProperty = DependencyProperty.Register(
//                "StartBezierPoint",
//                typeof(Point),
//                typeof(BezierFigure),
//                new FrameworkPropertyMetadata(new Point()));

//        /// <summary>
//        /// Gets or sets the StartBezierPoint property
//        /// </summary>
//        public Point StartBezierPoint
//        {
//            get { return (Point)GetValue(StartBezierPointProperty); }
//            set { SetValue(StartBezierPointProperty, value); }
//        }
//        #endregion

//        #region EndBezierPoint
//        /// <summary>
//        /// StartBezierPoint Dependency Property
//        /// </summary>
//        public static readonly DependencyProperty EndBezierPointProperty = DependencyProperty.Register(
//                "EndBezierPoint",
//                typeof(Point),
//                typeof(BezierFigure),
//                new FrameworkPropertyMetadata(new Point()));

//        /// <summary>
//        /// Gets or sets the StartBezierPoint property
//        /// </summary>
//        public Point EndBezierPoint
//        {
//            get { return (Point)GetValue(EndBezierPointProperty); }
//            set { SetValue(EndBezierPointProperty, value); }
//        }
//        #endregion

//        #region LockPoints

//        public static readonly DependencyProperty LockStartPointXProperty = DependencyProperty.Register(
//            "LockStartPointX",
//            typeof(bool),
//            typeof(BezierFigure),
//            new FrameworkPropertyMetadata(false));

//        public bool LockStartPointX
//        {
//            get { return (bool)GetValue(LockStartPointXProperty); }
//            set { SetValue(LockStartPointXProperty, value); }
//        }

//        public static readonly DependencyProperty LockStartPointYProperty = DependencyProperty.Register(
//            "LockStartPointY",
//            typeof(bool),
//            typeof(BezierFigure),
//            new FrameworkPropertyMetadata(false));

//        public bool LockStartPointY
//        {
//            get { return (bool)GetValue(LockStartPointYProperty); }
//            set { SetValue(LockStartPointYProperty, value); }
//        }

//        public static readonly DependencyProperty LockEndPointXProperty = DependencyProperty.Register(
//            "LockEndPointX",
//            typeof(bool),
//            typeof(BezierFigure),
//            new FrameworkPropertyMetadata(false));

//        public bool LockEndPointX
//        {
//            get { return (bool)GetValue(LockEndPointXProperty); }
//            set { SetValue(LockEndPointXProperty, value); }
//        }

//        public static readonly DependencyProperty LockEndPointYProperty = DependencyProperty.Register(
//            "LockEndPointY",
//            typeof(bool),
//            typeof(BezierFigure),
//            new FrameworkPropertyMetadata(false));

//        public bool LockEndPointY
//        {
//            get { return (bool)GetValue(LockEndPointYProperty); }
//            set { SetValue(LockEndPointYProperty, value); }
//        }

//        #endregion

//        #region Points

//        public Path PathForSelect
//        {
//            get
//            {
//                var pathFigure = (Path)Template.FindName("PathForSelect", this);
//                return pathFigure;
//            }
//        }


//        public System.Windows.Media.PathGeometry BezierPathGeometry
//        {
//            get
//            {
//                var pathFigure = (System.Windows.Media.PathGeometry)Template.FindName("bezierPathGeometry", this);
//                return pathFigure;
//            }
//        }

//        #endregion

//        #region misc
//        public static readonly DependencyProperty BodyColorProperty = DependencyProperty.Register(
//            "BodyColor",
//            typeof(Brush),
//            typeof(BezierFigure),
//            new FrameworkPropertyMetadata(Brushes.Red));

//        public Brush BodyColor
//        {
//            get { return (Brush)GetValue(BodyColorProperty); }
//            set { SetValue(BodyColorProperty, value); }
//        }

//        public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
//            "IsSelected",
//            typeof(bool),
//            typeof(BezierFigure),
//            new FrameworkPropertyMetadata(true));

//        public bool IsSelected
//        {
//            get { return (bool)GetValue(IsSelectedProperty); }
//            set { SetValue(IsSelectedProperty, value); }
//        }

//        public static readonly DependencyProperty CurveIndexProperty = DependencyProperty.Register(
//            "CurveIndex",
//            typeof(int),
//            typeof(BezierFigure),
//            new FrameworkPropertyMetadata(-1));

//        public int CurveIndex
//        {
//            get { return (int)GetValue(CurveIndexProperty); }
//            set { SetValue(CurveIndexProperty, value); }
//        }

//        public static readonly DependencyProperty CurveLabelProperty = DependencyProperty.Register(
//            "CurveLabel",
//            typeof(string),
//            typeof(BezierFigure),
//            new FrameworkPropertyMetadata(string.Empty));

//        public string CurveLabel
//        {
//            get { return (string)GetValue(CurveLabelProperty); }
//            set { SetValue(CurveLabelProperty, value); }
//        }

//        private Brush _originalColor;
//        public Brush OriginalColor
//        {
//            get { return _originalColor ?? Brushes.Red; }
//            set { _originalColor = value; }
//        }
//        #endregion

//        #region Label
//        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
//            "Label",
//            typeof(string),
//            typeof(BezierFigure),
//            new FrameworkPropertyMetadata(string.Empty));

//        public string Label
//        {
//            get { return (string)GetValue(LabelProperty); }
//            set { SetValue(LabelProperty, value); }
//        }
//        #endregion

//        public BezierFigure()
//        {
//            Loaded += BezierFigure_Loaded;
//        }


//        private void BezierFigure_Loaded(object sender, RoutedEventArgs e)
//        {
//            this.MouseLeftButtonDown += OnMouseLeftButtonDown;
//        }

//        private void OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
//        {
//            if (!IsSelected)
//            {
//                SetSelected();
//            }
//        }

//        public void SetUnSelected()
//        {
//            BodyColor = OriginalColor;
//            IsSelected = false;

//            SelectStateChange(this, false);
//        }

//        public void SetSelected()
//        {
//            BodyColor = Brushes.Blue;
//            IsSelected = true;

//            SelectStateChange(this, true);
//        }

//        public void SetSelectedWithColor()
//        {
//            OriginalColor = BodyColor;
//            BodyColor = Brushes.Blue;
//            IsSelected = true;
//            SelectStateChange(this, true);
//        }

//        static BezierFigure()
//        {
//            DefaultStyleKeyProperty.OverrideMetadata(typeof(BezierFigure), new FrameworkPropertyMetadata(typeof(BezierFigure)));
//        }
//    }
//}
