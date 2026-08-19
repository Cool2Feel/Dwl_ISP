using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ThunderSE.Ui.CommonCustomControl
{
    public class BezierCurveEditor : Control
    {
        #region Nested Types

        private Canvas _canvas;
        private Path _curvePath;
        private Path _gammaTablePath;
        private readonly List<Ellipse> _controlPointMarkers = new List<Ellipse>();
        private readonly List<Line> _controlLines = new List<Line>();
        private readonly List<TextBlock> _controlPointLabels = new List<TextBlock>();
        private readonly List<FrameworkElement> _gridElements = new List<FrameworkElement>();
        private readonly List<TextBlock> _axisLabels = new List<TextBlock>();
        private readonly List<FrameworkElement> _dataPointElements = new List<FrameworkElement>();
        private int _draggingPointIndex = -1;
        private bool _isInitialized = false;
        private const double ChartPadding = 40;

        private static readonly int[] KeyPointXValues = new int[]
        {
            0, 1, 3, 6, 10, 16, 26, 39, 55, 71,
            87, 103, 119, 135, 151, 167, 191, 223, 239, 255
        };

        private short[] _yGammaTable = new short[]
        {
            0x0, 0x8d, 0xb5, 0xd1, 0xe8, 0xfb, 0x10c, 0x11b, 0x129, 0x136,
            0x142, 0x14d, 0x157, 0x161, 0x16b, 0x174, 0x17c, 0x185, 0x18d, 0x194,
            0x19c, 0x1a3, 0x1aa, 0x1b1, 0x1b8, 0x1be, 0x1c4, 0x1cb, 0x1d1, 0x1d6,
            0x1dc, 0x1e2, 0x1e7, 0x1ed, 0x1f2, 0x1f7, 0x1fc, 0x201, 0x206, 0x20b,
            0x210, 0x214, 0x219, 0x21d, 0x222, 0x226, 0x22b, 0x22f, 0x233, 0x237,
            0x23b, 0x240, 0x244, 0x247, 0x24b, 0x24f, 0x253, 0x257, 0x25b, 0x25e,
            0x262, 0x266, 0x269, 0x26d, 0x270, 0x274, 0x277, 0x27a, 0x27e, 0x281,
            0x284, 0x288, 0x28b, 0x28e, 0x291, 0x295, 0x298, 0x29b, 0x29e, 0x2a1,
            0x2a4, 0x2a7, 0x2aa, 0x2ad, 0x2b0, 0x2b3, 0x2b6, 0x2b8, 0x2bb, 0x2be,
            0x2c1, 0x2c4, 0x2c7, 0x2c9, 0x2cc, 0x2cf, 0x2d1, 0x2d4, 0x2d7, 0x2d9,
            0x2dc, 0x2df, 0x2e1, 0x2e4, 0x2e6, 0x2e9, 0x2eb, 0x2ee, 0x2f0, 0x2f3,
            0x2f5, 0x2f8, 0x2fa, 0x2fd, 0x2ff, 0x301, 0x304, 0x306, 0x309, 0x30b,
            0x30d, 0x310, 0x312, 0x314, 0x316, 0x319, 0x31b, 0x31d, 0x31f, 0x322,
            0x324, 0x326, 0x328, 0x32a, 0x32d, 0x32f, 0x331, 0x333, 0x335, 0x337,
            0x339, 0x33c, 0x33e, 0x340, 0x342, 0x344, 0x346, 0x348, 0x34a, 0x34c,
            0x34e, 0x350, 0x352, 0x354, 0x356, 0x358, 0x35a, 0x35c, 0x35e, 0x360,
            0x362, 0x364, 0x366, 0x368, 0x369, 0x36b, 0x36d, 0x36f, 0x371, 0x373,
            0x375, 0x377, 0x378, 0x37a, 0x37c, 0x37e, 0x380, 0x382, 0x383, 0x385,
            0x387, 0x389, 0x38b, 0x38c, 0x38e, 0x390, 0x392, 0x393, 0x395, 0x397,
            0x399, 0x39a, 0x39c, 0x39e, 0x39f, 0x3a1, 0x3a3, 0x3a5, 0x3a6, 0x3a8,
            0x3aa, 0x3ab, 0x3ad, 0x3af, 0x3b0, 0x3b2, 0x3b4, 0x3b5, 0x3b7, 0x3b8,
            0x3ba, 0x3bc, 0x3bd, 0x3bf, 0x3c1, 0x3c2, 0x3c4, 0x3c5, 0x3c7, 0x3c8,
            0x3ca, 0x3cc, 0x3cd, 0x3cf, 0x3d0, 0x3d2, 0x3d3, 0x3d5, 0x3d7, 0x3d8,
            0x3da, 0x3db, 0x3dd, 0x3de, 0x3e0, 0x3e1, 0x3e3, 0x3e4, 0x3e6, 0x3e7,
            0x3e9, 0x3ea, 0x3ec, 0x3ed, 0x3ef, 0x3f0, 0x3f2, 0x3f3, 0x3f4, 0x3f6,
            0x3f7, 0x3f9, 0x3fa, 0x3fc, 0x3fd, 0x3ff
        };

        private short[] _originalGammaTable = new short[]
        {
            0x0, 0x8d, 0xb5, 0xd1, 0xe8, 0xfb, 0x10c, 0x11b, 0x129, 0x136,
            0x142, 0x14d, 0x157, 0x161, 0x16b, 0x174, 0x17c, 0x185, 0x18d, 0x194,
            0x19c, 0x1a3, 0x1aa, 0x1b1, 0x1b8, 0x1be, 0x1c4, 0x1cb, 0x1d1, 0x1d6,
            0x1dc, 0x1e2, 0x1e7, 0x1ed, 0x1f2, 0x1f7, 0x1fc, 0x201, 0x206, 0x20b,
            0x210, 0x214, 0x219, 0x21d, 0x222, 0x226, 0x22b, 0x22f, 0x233, 0x237,
            0x23b, 0x240, 0x244, 0x247, 0x24b, 0x24f, 0x253, 0x257, 0x25b, 0x25e,
            0x262, 0x266, 0x269, 0x26d, 0x270, 0x274, 0x277, 0x27a, 0x27e, 0x281,
            0x284, 0x288, 0x28b, 0x28e, 0x291, 0x295, 0x298, 0x29b, 0x29e, 0x2a1,
            0x2a4, 0x2a7, 0x2aa, 0x2ad, 0x2b0, 0x2b3, 0x2b6, 0x2b8, 0x2bb, 0x2be,
            0x2c1, 0x2c4, 0x2c7, 0x2c9, 0x2cc, 0x2cf, 0x2d1, 0x2d4, 0x2d7, 0x2d9,
            0x2dc, 0x2df, 0x2e1, 0x2e4, 0x2e6, 0x2e9, 0x2eb, 0x2ee, 0x2f0, 0x2f3,
            0x2f5, 0x2f8, 0x2fa, 0x2fd, 0x2ff, 0x301, 0x304, 0x306, 0x309, 0x30b,
            0x30d, 0x310, 0x312, 0x314, 0x316, 0x319, 0x31b, 0x31d, 0x31f, 0x322,
            0x324, 0x326, 0x328, 0x32a, 0x32d, 0x32f, 0x331, 0x333, 0x335, 0x337,
            0x339, 0x33c, 0x33e, 0x340, 0x342, 0x344, 0x346, 0x348, 0x34a, 0x34c,
            0x34e, 0x350, 0x352, 0x354, 0x356, 0x358, 0x35a, 0x35c, 0x35e, 0x360,
            0x362, 0x364, 0x366, 0x368, 0x369, 0x36b, 0x36d, 0x36f, 0x371, 0x373,
            0x375, 0x377, 0x378, 0x37a, 0x37c, 0x37e, 0x380, 0x382, 0x383, 0x385,
            0x387, 0x389, 0x38b, 0x38c, 0x38e, 0x390, 0x392, 0x393, 0x395, 0x397,
            0x399, 0x39a, 0x39c, 0x39e, 0x39f, 0x3a1, 0x3a3, 0x3a5, 0x3a6, 0x3a8,
            0x3aa, 0x3ab, 0x3ad, 0x3af, 0x3b0, 0x3b2, 0x3b4, 0x3b5, 0x3b7, 0x3b8,
            0x3ba, 0x3bc, 0x3bd, 0x3bf, 0x3c1, 0x3c2, 0x3c4, 0x3c5, 0x3c7, 0x3c8,
            0x3ca, 0x3cc, 0x3cd, 0x3cf, 0x3d0, 0x3d2, 0x3d3, 0x3d5, 0x3d7, 0x3d8,
            0x3da, 0x3db, 0x3dd, 0x3de, 0x3e0, 0x3e1, 0x3e3, 0x3e4, 0x3e6, 0x3e7,
            0x3e9, 0x3ea, 0x3ec, 0x3ed, 0x3ef, 0x3f0, 0x3f2, 0x3f3, 0x3f4, 0x3f6,
            0x3f7, 0x3f9, 0x3fa, 0x3fc, 0x3fd, 0x3ff
        };

        private Point _initialP1;
        private Point _initialP2;
        private FitStatistics _cachedFitStats;
        private bool _fitStatsCacheValid;
        private double _zoomOffsetX;
        private double _zoomOffsetY;
        private bool _hasImportedData;
        private short[] _defaultGammaTable;
        private bool _suppressLayoutUpdate;
        private short[] _referenceDataForStats;

        #endregion

        #region Dependency Properties
        public static readonly DependencyProperty P1XProperty = DependencyProperty.Register(
            "P1X", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(85.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnControlPointChanged));

        public static readonly DependencyProperty P1YProperty = DependencyProperty.Register(
            "P1Y", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(682.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnControlPointChanged));

        public static readonly DependencyProperty P2XProperty = DependencyProperty.Register(
            "P2X", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(170.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnControlPointChanged));

        public static readonly DependencyProperty P2YProperty = DependencyProperty.Register(
            "P2Y", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(341.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnControlPointChanged));

        public static readonly DependencyProperty P0YProperty = DependencyProperty.Register(
            "P0Y", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnP0P3Changed));

        public static readonly DependencyProperty P3YProperty = DependencyProperty.Register(
            "P3Y", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(1023.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnP0P3Changed));

        public static readonly DependencyProperty MaxXProperty = DependencyProperty.Register(
            "MaxX", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(255.0, OnLayoutChanged));

        public static readonly DependencyProperty MaxYProperty = DependencyProperty.Register(
            "MaxY", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(1023.0, OnLayoutChanged));

        public static readonly DependencyProperty ZoomLevelProperty = DependencyProperty.Register(
            "ZoomLevel", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnZoomLevelChanged));

        private static readonly DependencyPropertyKey CurveYMinPropertyKey =
            DependencyProperty.RegisterReadOnly("CurveYMin", typeof(double), typeof(BezierCurveEditor),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.None));

        public static readonly DependencyProperty CurveYMinProperty = CurveYMinPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CurveYMaxPropertyKey =
            DependencyProperty.RegisterReadOnly("CurveYMax", typeof(double), typeof(BezierCurveEditor),
                new FrameworkPropertyMetadata(1023.0, FrameworkPropertyMetadataOptions.None));

        public static readonly DependencyProperty CurveYMaxProperty = CurveYMaxPropertyKey.DependencyProperty;

        public event EventHandler CurveChanged;
        public event EventHandler GammaTableChanged;

        public double P1X
        {
            get { return (double)GetValue(P1XProperty); }
            set { SetValue(P1XProperty, value); }
        }

        public double P1Y
        {
            get { return (double)GetValue(P1YProperty); }
            set { SetValue(P1YProperty, value); }
        }

        public double P2X
        {
            get { return (double)GetValue(P2XProperty); }
            set { SetValue(P2XProperty, value); }
        }

        public double P2Y
        {
            get { return (double)GetValue(P2YProperty); }
            set { SetValue(P2YProperty, value); }
        }

        public double P0Y
        {
            get { return (double)GetValue(P0YProperty); }
            set { SetValue(P0YProperty, value); }
        }

        public double P3Y
        {
            get { return (double)GetValue(P3YProperty); }
            set { SetValue(P3YProperty, value); }
        }

        public double MaxX
        {
            get { return (double)GetValue(MaxXProperty); }
            set { SetValue(MaxXProperty, value); }
        }

        public double MaxY
        {
            get { return (double)GetValue(MaxYProperty); }
            set { SetValue(MaxYProperty, value); }
        }

        public double ZoomLevel
        {
            get { return (double)GetValue(ZoomLevelProperty); }
            set { SetValue(ZoomLevelProperty, value); }
        }

        public double CurveYMin
        {
            get { return (double)GetValue(CurveYMinProperty); }
        }

        public double CurveYMax
        {
            get { return (double)GetValue(CurveYMaxProperty); }
        }

        public short[] GammaTable
        {
            get { return _yGammaTable; }
        }

        #endregion

        static BezierCurveEditor()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(BezierCurveEditor),
                new FrameworkPropertyMetadata(typeof(BezierCurveEditor)));
        }

        public BezierCurveEditor()
        {
            Loaded += OnLoaded;
            SizeChanged += OnSizeChanged;
            //MouseWheel += OnControlMouseWheel;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isInitialized = true;
            _defaultGammaTable = new short[_yGammaTable.Length];
            Array.Copy(_yGammaTable, _defaultGammaTable, _yGammaTable.Length);
            _referenceDataForStats = new short[_yGammaTable.Length];
            Array.Copy(_yGammaTable, _referenceDataForStats, _yGammaTable.Length);
            MaxY = ComputeMaxYFromData();
            P0Y = 0;
            P3Y = MaxY;
            FitControlPointsFromData(_yGammaTable);
            _initialP1 = new Point(P1X, P1Y);
            _initialP2 = new Point(P2X, P2Y);
            RedrawAll();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isInitialized)
                RedrawAll();
        }

        private static void OnControlPointChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            BezierCurveEditor editor = (BezierCurveEditor)d;
            editor._fitStatsCacheValid = false;
            if (editor._suppressLayoutUpdate) return;
            if (editor._isInitialized)
            {
                editor.Dispatcher?.Invoke(() =>
                {
                    editor.UpdateGammaTable();
                    editor.RedrawAll();
                });
            }
        }

        private static void OnP0P3Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            BezierCurveEditor editor = (BezierCurveEditor)d;
            editor._fitStatsCacheValid = false;
            if (editor._suppressLayoutUpdate) return;
            if (editor._isInitialized)
            {
                editor.Dispatcher?.Invoke(() =>
                {
                    editor.UpdateGammaTable();
                    editor.RedrawAll();
                });
            }
        }

        private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            BezierCurveEditor editor = (BezierCurveEditor)d;
            if (editor._suppressLayoutUpdate) return;
            editor._fitStatsCacheValid = false;
            if (editor._isInitialized)
            {
                editor.Dispatcher?.Invoke(() =>
                {
                    editor.UpdateGammaTable();
                    editor.RedrawAll();
                });
            }
        }

        private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            BezierCurveEditor editor = (BezierCurveEditor)d;
            editor._zoomOffsetX = 0;
            editor._zoomOffsetY = 0;
            if (editor._isInitialized)
            {
                editor.Dispatcher?.Invoke(() => editor.RedrawAll());
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _canvas = GetTemplateChild("PART_Canvas") as Canvas;
            if (_canvas == null) return;

            _curvePath = new Path
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0x3B)),
                StrokeThickness = 3
            };
            _canvas.Children.Add(_curvePath);

            _gammaTablePath = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x50, 0x00, 0xBC, 0xD4)),
                Stroke = null
            };
            _canvas.Children.Add(_gammaTablePath);

            for (int i = 0; i < 3; i++)
            {
                Line line = new Line
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 4 }
                };
                _canvas.Children.Add(line);
                _controlLines.Add(line);
            }

            string[] labels = { "P0", "P1", "P2", "P3" };
            for (int i = 0; i < 4; i++)
            {
                Ellipse marker = new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Stroke = Brushes.White,
                    StrokeThickness = 2,
                    Cursor = Cursors.SizeAll,
                    Tag = i
                };

                if (i == 0 || i == 3)
                {
                    marker.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x57, 0x22));
                }
                else
                {
                    marker.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                }

                marker.MouseLeftButtonDown += OnControlPointMouseDown;
                marker.MouseMove += OnControlPointMouseMove;
                marker.MouseLeftButtonUp += OnControlPointMouseUp;

                _canvas.Children.Add(marker);
                _controlPointMarkers.Add(marker);

                TextBlock label = new TextBlock
                {
                    Text = labels[i],
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11
                };
                _canvas.Children.Add(label);
                _controlPointLabels.Add(label);
            }

            _canvas.MouseMove += OnCanvasMouseMove;
            _canvas.MouseLeftButtonUp += OnCanvasMouseUp;
            _canvas.MouseWheel += OnCanvasMouseWheel;
        }

        private Point[] GetControlPoints()
        {
            return new Point[]
            {
                new Point(0, P0Y),
                new Point(P1X, P1Y),
                new Point(P2X, P2Y),
                new Point(MaxX, P3Y)
            };
        }

        private double GetDrawWidth()
        {
            return _canvas.ActualWidth - 2 * ChartPadding;
        }

        private double GetDrawHeight()
        {
            return _canvas.ActualHeight - 2 * ChartPadding;
        }

        private double GetVisibleMaxX()
        {
            return MaxX / ZoomLevel;
        }

        private double GetVisibleMaxY()
        {
            return MaxY / ZoomLevel;
        }

        private Point DataToCanvas(double dataX, double dataY)
        {
            double visibleMaxX = GetVisibleMaxX();
            double visibleMaxY = GetVisibleMaxY();
            double offsetX = _zoomOffsetX;
            double offsetY = _zoomOffsetY;

            double drawW = GetDrawWidth();
            double drawH = GetDrawHeight();

            double normX = (dataX - offsetX) / visibleMaxX;
            double normY = 1.0 - (dataY - offsetY) / visibleMaxY;

            return new Point(
                ChartPadding + normX * drawW,
                ChartPadding + normY * drawH
            );
        }

        private Point DataToCanvasNoZoom(double dataX, double dataY)
        {
            double drawW = GetDrawWidth();
            double drawH = GetDrawHeight();

            double normX = dataX / MaxX;
            double normY = 1.0 - dataY / MaxY;

            return new Point(
                ChartPadding + normX * drawW,
                ChartPadding + normY * drawH
            );
        }

        private Point CanvasToData(double canvasX, double canvasY)
        {
            double visibleMaxX = GetVisibleMaxX();
            double visibleMaxY = GetVisibleMaxY();
            double offsetX = _zoomOffsetX;
            double offsetY = _zoomOffsetY;

            double drawW = GetDrawWidth();
            double drawH = GetDrawHeight();

            double normX = (canvasX - ChartPadding) / drawW;
            double normY = 1.0 - (canvasY - ChartPadding) / drawH;

            return new Point(
                Math.Max(0, Math.Min(MaxX, normX * visibleMaxX + offsetX)),
                Math.Max(0, Math.Min(MaxY, normY * visibleMaxY + offsetY))
            );
        }

        private void RedrawAll()
        {
            if (_canvas == null || !_isInitialized) return;

            DrawGrid();
            DrawCurve();
            DrawGammaTablePoints();
            DrawDataPoints();
            DrawControlPoints();
        }

        private void DrawGrid()
        {
            foreach (FrameworkElement elem in _gridElements)
                _canvas.Children.Remove(elem);
            _gridElements.Clear();

            foreach (TextBlock label in _axisLabels)
                _canvas.Children.Remove(label);
            _axisLabels.Clear();

            SolidColorBrush gridBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            SolidColorBrush axisBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            int xGridCount = 8;
            int yGridCount = 8;

            for (int i = 0; i <= xGridCount; i++)
            {
                double ratio = i / (double)xGridCount;

                Line vLine = new Line
                {
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                    X1 = ChartPadding + ratio * GetDrawWidth(),
                    Y1 = ChartPadding,
                    X2 = ChartPadding + ratio * GetDrawWidth(),
                    Y2 = ChartPadding + GetDrawHeight()
                };
                _canvas.Children.Add(vLine);
                _gridElements.Add(vLine);

                int xLabelVal = (int)Math.Round(ratio * MaxX);
                TextBlock xLabel = new TextBlock
                {
                    Text = xLabelVal.ToString(),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                    FontSize = 10
                };
                Point xPos = DataToCanvasNoZoom(ratio * MaxX, 0);
                Canvas.SetLeft(xLabel, xPos.X - 10);
                Canvas.SetTop(xLabel, xPos.Y + 4);
                _canvas.Children.Add(xLabel);
                _axisLabels.Add(xLabel);
            }

            for (int i = 0; i <= yGridCount; i++)
            {
                double ratio = i / (double)yGridCount;

                Line hLine = new Line
                {
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                    X1 = ChartPadding,
                    Y1 = ChartPadding + ratio * GetDrawHeight(),
                    X2 = ChartPadding + GetDrawWidth(),
                    Y2 = ChartPadding + ratio * GetDrawHeight()
                };
                _canvas.Children.Add(hLine);
                _gridElements.Add(hLine);

                int yLabelVal = (int)Math.Round(CurveYMin + (1.0 - ratio) * (CurveYMax - CurveYMin));
                TextBlock yLabel = new TextBlock
                {
                    Text = yLabelVal.ToString(),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                    FontSize = 10
                };
                double yCanvasNorm = 1.0 - (yLabelVal - CurveYMin) / (CurveYMax - CurveYMin);
                double yCanvasY = ChartPadding + yCanvasNorm * GetDrawHeight();
                Canvas.SetLeft(yLabel, ChartPadding - 32);
                Canvas.SetTop(yLabel, yCanvasY - 6);
                _canvas.Children.Add(yLabel);
                _axisLabels.Add(yLabel);
            }

            Rectangle borderRect = new Rectangle
            {
                Stroke = axisBrush,
                StrokeThickness = 1,
                Width = GetDrawWidth(),
                Height = GetDrawHeight()
            };
            Canvas.SetLeft(borderRect, ChartPadding);
            Canvas.SetTop(borderRect, ChartPadding);
            _canvas.Children.Add(borderRect);
            _gridElements.Add(borderRect);

            Line diagonalLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 6, 4 },
                X1 = ChartPadding,
                Y1 = ChartPadding + GetDrawHeight(),
                X2 = ChartPadding + GetDrawWidth(),
                Y2 = ChartPadding
            };
            _canvas.Children.Add(diagonalLine);
            _gridElements.Add(diagonalLine);
        }

        private void DrawCurve()
        {
            if (_curvePath == null) return;

            Point[] points = GetControlPoints();
            Point[] canvasPoints = new Point[4];
            for (int i = 0; i < 4; i++)
            {
                canvasPoints[i] = DataToCanvas(points[i].X, points[i].Y);
            }

            PathGeometry geometry = new PathGeometry();
            PathFigure figure = new PathFigure();
            figure.StartPoint = canvasPoints[0];
            BezierSegment bezier = new BezierSegment(canvasPoints[1], canvasPoints[2], canvasPoints[3], true);
            figure.Segments.Add(bezier);
            geometry.Figures.Add(figure);

            _curvePath.Data = geometry;
        }

        private void DrawGammaTablePoints()
        {
            if (_gammaTablePath == null || _yGammaTable == null) return;

            PathGeometry geometry = new PathGeometry();

            for (int i = 0; i < _yGammaTable.Length; i++)
            {
                double yVal = _yGammaTable[i];
                Point canvasPos = DataToCanvas(i, yVal);

                double left = canvasPos.X - 1.5;
                double top = canvasPos.Y - 1.5;
                double right = canvasPos.X + 1.5;
                double bottom = canvasPos.Y + 1.5;

                PathFigure figure = new PathFigure();
                figure.StartPoint = new Point(left, top);
                figure.IsClosed = true;
                figure.IsFilled = true;

                figure.Segments.Add(new LineSegment(new Point(right, top), true));
                figure.Segments.Add(new LineSegment(new Point(right, bottom), true));
                figure.Segments.Add(new LineSegment(new Point(left, bottom), true));

                geometry.Figures.Add(figure);
            }

            _gammaTablePath.Data = geometry;
        }

        private void DrawDataPoints()
        {
            foreach (FrameworkElement elem in _dataPointElements)
                _canvas.Children.Remove(elem);
            _dataPointElements.Clear();

            Point[] cp = GetControlPoints();
            SolidColorBrush vLineBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xBC, 0xD4));

            for (int i = 0; i < KeyPointXValues.Length; i++)
            {
                int keyX = KeyPointXValues[i];

                if (keyX == 0 || keyX == (int)MaxX)
                    continue;

                double t = FindTForX(keyX, cp, MaxX);
                double y = CubicBezierY(t, cp);
                y = Math.Max(0, Math.Min(MaxY, y));

                Point canvasPos = DataToCanvas(keyX, y);
                Point bottomPos = DataToCanvas(keyX, 0);

                Line vLine = new Line
                {
                    Stroke = vLineBrush,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 3 },
                    X1 = canvasPos.X,
                    Y1 = bottomPos.Y,
                    X2 = canvasPos.X,
                    Y2 = canvasPos.Y
                };
                _canvas.Children.Add(vLine);
                _dataPointElements.Add(vLine);

                int yRounded = (int)Math.Round(y);

                Border tipBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x1A, 0x1A, 0x2E)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 3, 6, 3),
                    Child = new TextBlock
                    {
                        Text = string.Format("X: {0}  Y: {1}", keyX, yRounded),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)),
                        FontSize = 11,
                        FontFamily = new FontFamily("Consolas")
                    }
                };

                Ellipse dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)),
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    Cursor = Cursors.Cross,
                    ToolTip = tipBorder
                };
                Canvas.SetLeft(dot, canvasPos.X - dot.Width / 2);
                Canvas.SetTop(dot, canvasPos.Y - dot.Height / 2);
                _canvas.Children.Add(dot);
                _dataPointElements.Add(dot);
            }
        }

        private void DrawControlPoints()
        {
            Point[] points = GetControlPoints();

            for (int i = 0; i < 4; i++)
            {
                Point canvasPos = DataToCanvas(points[i].X, points[i].Y);

                if (i < _controlPointMarkers.Count)
                {
                    Ellipse marker = _controlPointMarkers[i];
                    Canvas.SetLeft(marker, canvasPos.X - marker.Width / 2);
                    Canvas.SetTop(marker, canvasPos.Y - marker.Height / 2);
                }

                if (i < _controlPointLabels.Count)
                {
                    TextBlock label = _controlPointLabels[i];
                    Canvas.SetLeft(label, canvasPos.X - 8);
                    Canvas.SetTop(label, canvasPos.Y - 20);
                }
            }

            for (int i = 0; i < _controlLines.Count && i < 3; i++)
            {
                Point from = DataToCanvas(points[i].X, points[i].Y);
                Point to = DataToCanvas(points[i + 1].X, points[i + 1].Y);
                _controlLines[i].X1 = from.X;
                _controlLines[i].Y1 = from.Y;
                _controlLines[i].X2 = to.X;
                _controlLines[i].Y2 = to.Y;
            }
        }

        #region Event Handlers
        private void OnControlPointMouseDown(object sender, MouseButtonEventArgs e)
        {
            Ellipse marker = sender as Ellipse;
            if (marker == null) return;

            int index = (int)marker.Tag;
            _draggingPointIndex = index;
            marker.CaptureMouse();
            e.Handled = true;
        }

        private void OnControlPointMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingPointIndex < 0) return;

            Point pos = e.GetPosition(_canvas);
            Point dataPos = CanvasToData(pos.X, pos.Y);

            if (_draggingPointIndex == 0)
            {
                P0Y = Math.Round(Math.Max(0, Math.Min(MaxY, dataPos.Y)));
            }
            else if (_draggingPointIndex == 1)
            {
                P1X = Math.Round(Math.Max(0, Math.Min(MaxX, dataPos.X)));
                P1Y = Math.Round(Math.Max(0, Math.Min(MaxY, dataPos.Y)));
            }
            else if (_draggingPointIndex == 2)
            {
                P2X = Math.Round(Math.Max(0, Math.Min(MaxX, dataPos.X)));
                P2Y = Math.Round(Math.Max(0, Math.Min(MaxY, dataPos.Y)));
            }
            else if (_draggingPointIndex == 3)
            {
                P3Y = Math.Round(Math.Max(0, Math.Min(MaxY, dataPos.Y)));
            }

            UpdateCurveYRangeFromCurrentControlPoints();

            UpdateGammaTable();
            RedrawAll();
            FireCurveChanged();
            e.Handled = true;
        }

        private void OnControlPointMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingPointIndex >= 0)
            {
                Ellipse marker = sender as Ellipse;
                if (marker != null)
                    marker.ReleaseMouseCapture();
                _draggingPointIndex = -1;
                e.Handled = true;
            }
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingPointIndex < 0) return;

            Point pos = e.GetPosition(_canvas);
            Point dataPos = CanvasToData(pos.X, pos.Y);

            if (_draggingPointIndex == 1)
            {
                P1X = Math.Round(dataPos.X);
                P1Y = Math.Round(dataPos.Y);
            }
            else if (_draggingPointIndex == 2)
            {
                P2X = Math.Round(dataPos.X);
                P2Y = Math.Round(dataPos.Y);
            }
            else if (_draggingPointIndex == 3)
            {
                P3Y = Math.Round(Math.Max(0, Math.Min(MaxY, dataPos.Y)));
            }

            UpdateCurveYRangeFromCurrentControlPoints();

            UpdateGammaTable();
            RedrawAll();
            FireCurveChanged();
        }

        private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            _draggingPointIndex = -1;
        }

        private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_canvas == null) return;

            Point mousePos = e.GetPosition(_canvas);
            ApplyZoomAt(mousePos, e.Delta);
            e.Handled = true;
        }

        private void OnControlMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_canvas != null && _canvas.IsMouseOver)
            {
                Point mousePos = e.GetPosition(_canvas);
                ApplyZoomAt(mousePos, e.Delta);
                e.Handled = true;
            }
        }

        #endregion

        #region Helper Methods

        // 新增结构体用于存储拟合结果
        private struct FitResult
        {
            public double P1X, P1Y, P2X, P2Y;
            public double RSquared;
            public double MaxError;
            public bool IsValid;
        }

        private void ApplyZoomAt(Point mousePos, int delta)
        {
            double factor = delta > 0 ? 1.1 : 0.9;
            double newZoom = ZoomLevel * factor;
            newZoom = Math.Max(0.3, Math.Min(5.0, newZoom));

            if (Math.Abs(newZoom - ZoomLevel) < 0.001)
                return;

            _zoomOffsetX = 0;
            _zoomOffsetY = 0;

            ZoomLevel = newZoom;
        }

        private void FireCurveChanged()
        {
            EventHandler handler = CurveChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);

            EventHandler handler2 = GammaTableChanged;
            if (handler2 != null)
                handler2(this, EventArgs.Empty);
        }

        public void UpdateGammaTable()
        {
            Point[] cp = GetControlPoints();

            for (int x = 0; x < _yGammaTable.Length; x++)
            {
                double t = FindTForX(x, cp, MaxX);
                double y = CubicBezierY(t, cp);
                _yGammaTable[x] = (short)Math.Max(0, Math.Min((int)MaxY, Math.Round(y)));
            }
        }

        public void SetDefaultGammaTable(byte[] table)
        {
            //LoadGammaTable(_defaultGammaTable, preserveMaxY: false);
            try
            {
                int n = Math.Min(table.Length, _yGammaTable.Length);
                for (int i = 0; i < n; i++)
                {
                    _yGammaTable[i] = table[i];
                    _originalGammaTable[i] = table[i];
                }
                _defaultGammaTable = new short[_yGammaTable.Length];
                Array.Copy(_yGammaTable, _defaultGammaTable, _yGammaTable.Length);
                _referenceDataForStats = new short[_yGammaTable.Length];
                Array.Copy(_yGammaTable, _referenceDataForStats, _yGammaTable.Length);
                MaxY = ComputeMaxYFromData();
                P0Y = 0;
                P3Y = MaxY;
                FitControlPointsFromData(_yGammaTable);
                _initialP1 = new Point(P1X, P1Y);
                _initialP2 = new Point(P2X, P2Y);
                RedrawAll();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error applying default gamma table data ," + ex.Message);
            }
        }

        public short[] GenerateGammaTable(int pointCount)
        {
            short[] result = new short[pointCount];
            Point[] cp = GetControlPoints();

            for (int x = 0; x < pointCount; x++)
            {
                double t = FindTForX(x, cp, MaxX);
                double y = CubicBezierY(t, cp);
                result[x] = (short)Math.Max(0, Math.Min((int)MaxY, Math.Round(y)));
            }

            return result;
        }

        public void LoadGammaTable(short[] data, bool preserveMaxY = false)
        {
            if (data == null || data.Length == 0) return;

            _suppressLayoutUpdate = true;
            try
            {
                _hasImportedData = true;
                int n = Math.Min(data.Length, _yGammaTable.Length);
                for (int i = 0; i < n; i++)
                {
                    _yGammaTable[i] = data[i];
                    _originalGammaTable[i] = data[i];
                }

                if (!preserveMaxY)
                {
                    MaxY = ComputeMaxYFromData();
                }
                for (int i = n; i < _yGammaTable.Length; i++)
                {
                    _yGammaTable[i] = (short)MaxY;
                    _originalGammaTable[i] = (short)MaxY;
                }
                if (_referenceDataForStats == null || _referenceDataForStats.Length != _yGammaTable.Length)
                    _referenceDataForStats = new short[_yGammaTable.Length];
                Array.Copy(_yGammaTable, _referenceDataForStats, _yGammaTable.Length);
                P0Y = 0;
                P3Y = MaxY;
                _initialP1 = new Point(MaxX * 0.25, MaxY * 0.4);
                _initialP2 = new Point(MaxX * 0.70, MaxY * 0.85);
                FitControlPointsFromData(_yGammaTable);
            }
            finally
            {
                _suppressLayoutUpdate = false;
            }
            RedrawAll();
            FireCurveChanged();
        }

        private double CubicBezierDerivativeX(double t, Point[] cp)
        {
            double u = 1 - t;
            return 3 * u * u * (cp[1].X - cp[0].X) +
                   6 * u * t * (cp[2].X - cp[1].X) +
                   3 * t * t * (cp[3].X - cp[2].X);
        }

        private double CubicBezierDerivativeY(double t, Point[] cp)
        {
            double u = 1 - t;
            return 3 * u * u * (cp[1].Y - cp[0].Y) +
                   6 * u * t * (cp[2].Y - cp[1].Y) +
                   3 * t * t * (cp[3].Y - cp[2].Y);
        }

        private void ComputeSlopeBasedWeights(Point[] cp, double[] slopeWeights, double minSlope, double maxX)
        {
            int n = slopeWeights.Length;
            for (int i = 0; i < n; i++)
            {
                double dataX = (maxX * i) / (n - 1);
                double t = FindTForX(dataX, cp, maxX);
                double dxdt = CubicBezierDerivativeX(t, cp);
                double dydt = CubicBezierDerivativeY(t, cp);
                if (Math.Abs(dxdt) < 1e-12)
                {
                    slopeWeights[i] = 10.0;
                    continue;
                }
                double slope = dydt / dxdt;
                double absSlope = Math.Max(Math.Abs(slope), minSlope);
                slopeWeights[i] = minSlope / absSlope;
            }
            double wMin = double.MaxValue, wMax = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (slopeWeights[i] < wMin) wMin = slopeWeights[i];
                if (slopeWeights[i] > wMax) wMax = slopeWeights[i];
            }
            if (wMax > wMin)
            {
                for (int i = 0; i < n; i++)
                    slopeWeights[i] = 1.0 + 9.0 * (slopeWeights[i] - wMin) / (wMax - wMin);
            }
            else
            {
                for (int i = 0; i < n; i++)
                    slopeWeights[i] = 5.0;
            }
        }


        private void FitControlPointsFromData(short[] yValues)
        {
            int n = yValues.Length;

            double p1x = Math.Max(1, MaxX * 0.25);
            double p1y = Math.Max(1, MaxY * 0.40);
            double p2x = Math.Max(1, MaxX * 0.70);
            double p2y = Math.Max(1, MaxY * 0.85);
            //double p1x = Math.Max(1, MaxX * 0.33);
            //double p1y = Math.Max(1, MaxY * 0.67);
            //double p2x = Math.Max(1, MaxX * 0.67);
            //double p2y = Math.Max(1, MaxY * 0.33);

            double[] tArr = new double[n];
            double[] weights = new double[n];
            double minSlope = 0.5;
            double lambda = 0.001;
            double prevError = double.MaxValue;

            for (int iter = 0; iter < 100; iter++)
            {
                Point[] cpCurrent = new Point[]
                {
                    new Point(0, P0Y),
                    new Point(p1x, p1y),
                    new Point(p2x, p2y),
                    new Point(MaxX, P3Y)
                };
                for (int i = 0; i < n; i++)
                {
                    double dataX = (MaxX * i) / (n - 1);
                    tArr[i] = FindTForX(dataX, cpCurrent, MaxX);
                }
                ComputeSlopeBasedWeights(cpCurrent, weights, minSlope, MaxX);
                double[] residuals = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double dataX = (MaxX * i) / (n - 1);
                    double t = tArr[i];
                    residuals[i] = (CubicBezierY(t, cpCurrent) - yValues[i]) * weights[i];
                }

                double error = 0;
                for (int i = 0; i < n; i++)
                    error += residuals[i] * residuals[i];

                if (error < 1e-8 || Math.Abs(prevError - error) < 1e-8)
                    break;
                prevError = error;

                const int M = 4;
                double[,] J = new double[n, M];
                double eps = 1e-5;

                for (int j = 0; j < M; j++)
                {
                    double[] pTest = new double[] { p1x, p1y, p2x, p2y };
                    pTest[j] += eps;

                    double[] rPlus = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        double dataX = (MaxX * i) / (n - 1);
                        Point[] cp = new Point[]
                        {
                            new Point(0, P0Y),
                            new Point(pTest[0], pTest[1]),
                            new Point(pTest[2], pTest[3]),
                            new Point(MaxX, P3Y)
                        };
                        double t = FindTForX(dataX, cp, MaxX);
                        tArr[i] = t;
                        rPlus[i] = (CubicBezierY(t, cp) - yValues[i]) * weights[i];
                    }

                    for (int i = 0; i < n; i++)
                        J[i, j] = (rPlus[i] - residuals[i]) / eps;
                }

                double[,] JtJ = new double[M, M];
                double[] JtR = new double[M];
                for (int i = 0; i < M; i++)
                {
                    for (int j = 0; j < M; j++)
                    {
                        double sum = 0;
                        for (int k = 0; k < n; k++)
                            sum += J[k, i] * J[k, j];
                        JtJ[i, j] = sum;
                    }
                    double sumR = 0;
                    for (int k = 0; k < n; k++)
                        sumR += J[k, i] * residuals[k];
                    JtR[i] = sumR;
                }

                double[] delta = new double[M];
                for (int i = 0; i < M; i++)
                    delta[i] = JtR[i];

                for (int i = 0; i < M; i++)
                    JtJ[i, i] *= (1.0 + lambda);

                SolveLinearSystem(JtJ, delta, M);

                double newP1x = Math.Max(0, Math.Min(MaxX, p1x - delta[0]));
                double newP1y = Math.Max(0, Math.Min(MaxY, p1y - delta[1]));
                double newP2x = Math.Max(0, Math.Min(MaxX, p2x - delta[2]));
                double newP2y = Math.Max(0, Math.Min(MaxY, p2y - delta[3]));

                Point[] cpNew = new Point[]
                {
                    new Point(0, P0Y),
                    new Point(newP1x, newP1y),
                    new Point(newP2x, newP2y),
                    new Point(MaxX, P3Y)
                };
                double[] newWeights = new double[n];
                ComputeSlopeBasedWeights(cpNew, newWeights, minSlope, MaxX);

                double[] newResiduals = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double dataX = (MaxX * i) / (n - 1);
                    double t = FindTForX(dataX, cpNew, MaxX);
                    newResiduals[i] = (CubicBezierY(t, cpNew) - yValues[i]) * newWeights[i];
                }

                double newError = 0;
                for (int i = 0; i < n; i++)
                    newError += newResiduals[i] * newResiduals[i];

                if (newError < error)
                {
                    p1x = newP1x; p1y = newP1y;
                    p2x = newP2x; p2y = newP2y;
                    lambda *= 0.1;
                    if (lambda < 1e-8) lambda = 1e-8;
                }
                else
                {
                    lambda *= 10;
                    if (lambda < 1e-6) lambda = 1e-6;
                }
            }

            double finalP1x = p1x, finalP1y = p1y, finalP2x = p2x, finalP2y = p2y;

            Point[] cpFinal = new Point[]
            {
                new Point(0, P0Y),
                new Point(finalP1x, finalP1y),
                new Point(finalP2x, finalP2y),
                new Point(MaxX, P3Y)
            };

            double meanY = 0;
            for (int i = 0; i < n; i++)
                meanY += _referenceDataForStats[i];
            meanY /= n;

            double ssTot = 0, ssRes = 0, maxAbsError = 0;
            double[] finalResiduals = new double[n];
            for (int i = 0; i < n; i++)
            {
                double dataX = (MaxX * i) / (n - 1);
                double t = FindTForX(dataX, cpFinal, MaxX);
                double predY = CubicBezierY(t, cpFinal);
                double actual = _referenceDataForStats[i];
                double diff = actual - predY;
                finalResiduals[i] = diff;
                ssRes += diff * diff;
                double diffMean = actual - meanY;
                ssTot += diffMean * diffMean;
                double absErr = Math.Abs(diff);
                if (absErr > maxAbsError) maxAbsError = absErr;
            }

            _cachedFitStats = new FitStatistics
            {
                RSquared = ssTot > 0 ? 1.0 - (ssRes / ssTot) : 1.0,
                MaxAbsoluteError = maxAbsError
            };
            _fitStatsCacheValid = true;

            P1X = Math.Round(finalP1x);
            P1Y = Math.Round(finalP1y);
            P2X = Math.Round(finalP2x);
            P2Y = Math.Round(finalP2y);

            double curveYMin = double.MaxValue, curveYMax = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                double dataX = (MaxX * i) / (n - 1);
                double t = FindTForX(dataX, cpFinal, MaxX);
                double yVal = CubicBezierY(t, cpFinal);
                if (yVal < curveYMin) curveYMin = yVal;
                if (yVal > curveYMax) curveYMax = yVal;
            }
            curveYMin = Math.Max(0, curveYMin);
            curveYMax = Math.Min(MaxY, curveYMax);
            double yMargin = (curveYMax - curveYMin) * 0.05;
            SetValue(CurveYMinPropertyKey, Math.Max(0, curveYMin - yMargin));
            SetValue(CurveYMaxPropertyKey, Math.Min(MaxY, curveYMax + yMargin));

            UpdateGammaTable();
        }


        /*
        // =================================================================================
        // 优化后的拟合逻辑入口
        // =================================================================================
        private void FitControlPointsFromData(short[] yValues)
        {
            if (yValues == null || yValues.Length < 4) return;

            // 1. 准备数据
            int n = yValues.Length;
            double[] yData = new double[n];
            for (int i = 0; i < n; i++)
            {
                yData[i] = yValues[i];
            }

            // 2. 执行多初始值全局搜索
            // 使用 5x5 网格，共 25 个初始点，兼顾速度与覆盖率
            FitResult bestResult = RunGlobalOptimization(yData, n, 5);

            // 3. 应用最优结果
            if (bestResult.IsValid)
            {
                // ★ 关键：直接赋值 double 值，不要 Round
                P1X = bestResult.P1X;
                P1Y = bestResult.P1Y;
                P2X = bestResult.P2X;
                P2Y = bestResult.P2Y;

                // 更新统计信息
                UpdateFitStatistics(yData, n, bestResult);
            }
            else
            {
                // 兜底：如果全局搜索失败，使用默认值
                P1X = MaxX * 0.25; P1Y = MaxY * 0.4;
                P2X = MaxX * 0.70; P2Y = MaxY * 0.85;
            }

            UpdateGammaTable();
            RedrawAll();
        }

        // =================================================================================
        // 全局搜索主逻辑 (并行)
        // =================================================================================
        private FitResult RunGlobalOptimization(double[] yData, int n, int gridDivisions)
        {
            double step = 1.0 / (gridDivisions + 1);
            var results = new List<FitResult>();
            object lockObj = new object();

            // 在并行循环之前读取依赖属性值，避免跨线程访问UI对象
            double maxX = MaxX;
            double maxY = MaxY;
            double p0y = P0Y;
            double p3y = P3Y;

            // 并行处理每个网格点
            Parallel.For(1, gridDivisions + 1, (int i) =>
            {
                Parallel.For(1, gridDivisions + 1, (int j) =>
                {
                    // 生成初始猜测 (P1, P2)
                    double guessP1X = maxX * (i * step);
                    double guessP1Y = maxY * (j * step);

                    // P2 的初始猜测基于 P1，保证曲线单调性概率更高
                    double guessP2X = maxX * (1 - i * step);
                    double guessP2Y = maxY * (1 - j * step);

                    // 执行单次 LM 拟合
                    FitResult result = LevenbergMarquardt(yData, n, guessP1X, guessP1Y, guessP2X, guessP2Y, p0y, p3y, maxX, maxY);

                    if (result.IsValid)
                    {
                        lock (lockObj)
                        {
                            results.Add(result);
                        }
                    }
                });
            });

            // 找出最优结果 (R^2 最大)
            FitResult best = new FitResult { IsValid = false };
            double bestRSquared = -1;
            foreach (var res in results)
            {
                if (res.RSquared > bestRSquared)
                {
                    bestRSquared = res.RSquared;
                    best = res;
                }
            }

            return best;
        }

        // =================================================================================
        // Levenberg-Marquardt 拟合核心 (全程 double)
        // =================================================================================
        private FitResult LevenbergMarquardt(double[] yData, int n, double p1x, double p1y, double p2x, double p2y, double p0y, double p3y, double maxX, double maxY)
        {
            FitResult result = new FitResult { IsValid = false };

            // 参数边界检查
            if (double.IsNaN(p1x) || double.IsInfinity(p1x)) p1x = maxX * 0.25;
            if (double.IsNaN(p1y) || double.IsInfinity(p1y)) p1y = maxY * 0.4;
            if (double.IsNaN(p2x) || double.IsInfinity(p2x)) p2x = maxX * 0.7;
            if (double.IsNaN(p2y) || double.IsInfinity(p2y)) p2y = maxY * 0.85;

            // LM 算法参数
            double lambda = 1e-3;
            double lambdaFactor = 10.0;
            int maxIter = 50;
            double eps = 1e-6;

            // 预分配数组以减少 GC
            double[] tArr = new double[n];
            double[] weights = new double[n];
            double[] residuals = new double[n];
            double[] paramsVec = new double[4] { p1x, p1y, p2x, p2y };

            for (int iter = 0; iter < maxIter; iter++)
            {
                Point[] cp = new Point[]
                {
            new Point(0, p0y),
            new Point(paramsVec[0], paramsVec[1]),
            new Point(paramsVec[2], paramsVec[3]),
            new Point(maxX, p3y)
                };

                // 1. 计算 t 参数和权重
                for (int i = 0; i < n; i++)
                {
                    double dataX = (maxX * i) / (n - 1);
                    tArr[i] = FindTForX(dataX, cp, maxX);
                }
                ComputeSlopeBasedWeights(cp, weights, 0.5, maxX);

                // 2. 计算残差
                double currentError = 0;
                for (int i = 0; i < n; i++)
                {
                    double predY = CubicBezierY(tArr[i], cp);
                    residuals[i] = (predY - yData[i]) * weights[i];
                    currentError += residuals[i] * residuals[i];
                }

                // 3. 雅可比矩阵 (数值差分，中心差分法更稳定)
                double[,] J = new double[n, 4];
                double h = 1e-7; // 微小扰动
                for (int j = 0; j < 4; j++)
                {
                    paramsVec[j] += h;
                    Point[] cpPlus = new Point[]
                    {
                new Point(0, p0y),
                new Point(paramsVec[0], paramsVec[1]),
                new Point(paramsVec[2], paramsVec[3]),
                new Point(maxX, p3y)
                    };
                    double errorPlus = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double tPlus = FindTForX((maxX * i) / (n - 1), cpPlus, maxX);
                        double predYPlus = CubicBezierY(tPlus, cpPlus);
                        double rPlus = (predYPlus - yData[i]) * weights[i];
                        errorPlus += rPlus * rPlus;
                    }

                    paramsVec[j] -= 2 * h;
                    Point[] cpMinus = new Point[]
                    {
                new Point(0, p0y),
                new Point(paramsVec[0], paramsVec[1]),
                new Point(paramsVec[2], paramsVec[3]),
                new Point(maxX, p3y)
                    };
                    double errorMinus = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double tMinus = FindTForX((maxX * i) / (n - 1), cpMinus, maxX);
                        double predYMinus = CubicBezierY(tMinus, cpMinus);
                        double rMinus = (predYMinus - yData[i]) * weights[i];
                        errorMinus += rMinus * rMinus;
                    }

                    paramsVec[j] += h; // 恢复

                    // 中心差分计算雅可比列
                    for (int i = 0; i < n; i++)
                    {
                        J[i, j] = (errorPlus - errorMinus) / (4 * h * residuals[i]); // 简化近似，实际应计算残差梯度
                                                                                     // 这里为了效率使用了标量近似，工业级应用建议计算完整的残差向量差分
                    }
                }

                // 4. 求解增量 (J^T J + lambda * I) * delta = -J^T * r
                // 构建正规方程
                double[,] JtJ = new double[4, 4];
                double[] Jtr = new double[4];

                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        double sum = 0;
                        for (int k = 0; k < n; k++)
                        {
                            sum += J[k, i] * J[k, j];
                        }
                        JtJ[i, j] = sum;
                    }
                    double sumR = 0;
                    for (int k = 0; k < n; k++)
                    {
                        sumR += J[k, i] * residuals[k];
                    }
                    Jtr[i] = -sumR;
                }

                // 添加阻尼
                for (int i = 0; i < 4; i++)
                {
                    JtJ[i, i] += lambda;
                }

                // 求解线性方程组
                double[] delta = Solve4xUp4(JtJ, Jtr);
                if (delta == null) continue;

                // 5. 更新参数
                double[] newParams = new double[4];
                for (int i = 0; i < 4; i++)
                {
                    newParams[i] = paramsVec[i] + delta[i];
                    // 边界限制
                    if (i % 2 == 0) // X 坐标
                        newParams[i] = Math.Max(0, Math.Min(maxX, newParams[i]));
                    else // Y 坐标
                        newParams[i] = Math.Max(0, Math.Min(maxY, newParams[i]));
                }

                // 6. 检查收敛
                if (Math.Abs(delta[0]) < eps && Math.Abs(delta[1]) < eps &&
                    Math.Abs(delta[2]) < eps && Math.Abs(delta[3]) < eps)
                {
                    // 成功收敛
                    result.IsValid = true;
                    result.P1X = newParams[0];
                    result.P1Y = newParams[1];
                    result.P2X = newParams[2];
                    result.P2Y = newParams[3];
                    break;
                }

                // 更新参数向量
                paramsVec = newParams;

                // 调整 lambda
                if (iter % 10 == 0) lambda *= 0.1; // 假设下降成功，减小阻尼
                if (lambda < 1e-10) lambda = 1e-10;
            }

            return result;
        }

        // 4x4 矩阵求解器 (高斯消元法)
        private double[] Solve4xUp4(double[,] A, double[] b)
        {
            // 使用高斯-约旦消元法求解 Ax = b
            // 增广矩阵
            double[,] M = new double[4, 5];
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    M[i, j] = A[i, j];
                }
                M[i, 4] = b[i];
            }

            // 消元
            for (int col = 0; col < 4; col++)
            {
                // 寻找主元
                int pivot = col;
                for (int row = col + 1; row < 4; row++)
                {
                    if (Math.Abs(M[row, col]) > Math.Abs(M[pivot, col]))
                    {
                        pivot = row;
                    }
                }

                // 交换行
                if (pivot != col)
                {
                    for (int j = 0; j < 5; j++)
                    {
                        double temp = M[col, j];
                        M[col, j] = M[pivot, j];
                        M[pivot, j] = temp;
                    }
                }

                // 如果主元为 0，矩阵奇异
                if (Math.Abs(M[col, col]) < 1e-12) return null;

                // 归一化当前行
                double divisor = M[col, col];
                for (int j = col; j < 5; j++)
                {
                    M[col, j] /= divisor;
                }

                // 消去其他行
                for (int row = 0; row < 4; row++)
                {
                    if (row != col)
                    {
                        double factor = M[row, col];
                        for (int j = col; j < 5; j++)
                        {
                            M[row, j] -= factor * M[col, j];
                        }
                    }
                }
            }

            // 提取解向量
            double[] x = new double[4];
            for (int i = 0; i < 4; i++)
            {
                x[i] = M[i, 4];
            }
            return x;
        }


        /// <summary>
        /// 更新拟合统计信息 (R² 和 最大绝对误差)
        /// </summary>
        /// <param name="yData">原始数据数组</param>
        /// <param name="n">数据点数量</param>
        /// <param name="result">包含当前最优控制点的结构体</param>
        private void UpdateFitStatistics(double[] yData, int n, FitResult result)
        {
            // 计算 R² (决定系数) 和 最大误差
            double ssRes = 0; // 残差平方和
            double ssTot = 0; // 总平方和
            double maxAbsError = 0;

            // 计算原始数据的均值，用于 SS_tot
            double meanY = 0;
            for (int i = 0; i < n; i++)
            {
                meanY += yData[i];
            }
            meanY /= n;

            // 构建当前控制点的曲线
            Point[] cp = new Point[]
            {
        new Point(0, P0Y),
        new Point(result.P1X, result.P1Y),
        new Point(result.P2X, result.P2Y),
        new Point(MaxX, P3Y)
            };

            // 计算误差
            for (int i = 0; i < n; i++)
            {
                double dataX = (MaxX * i) / (n - 1);
                double t = FindTForX(dataX, cp, MaxX);
                double predY = CubicBezierY(t, cp);

                double actual = yData[i];
                double diff = actual - predY;
                ssRes += diff * diff;

                double diffMean = actual - meanY;
                ssTot += diffMean * diffMean;

                double absError = Math.Abs(diff);
                if (absError > maxAbsError)
                {
                    maxAbsError = absError;
                }
            }

            // 计算 R²
            double rSquared = 1.0;
            if (ssTot > 1e-10) // 防止除以0
            {
                rSquared = 1.0 - (ssRes / ssTot);
            }

            // 限制 R² 范围在 [0, 1] 之间，防止数值误差导致的微小负数或大于1
            rSquared = Math.Max(0, Math.Min(1, rSquared));

            // 更新结果结构体
            result.RSquared = rSquared;
            result.MaxError = maxAbsError;
            result.IsValid = true;

            // 更新 UI 绑定属性 (如果需要显示在界面上)
            // 注意：如果类中没有定义 CurveRSquaredProperty，则需要先定义，或者忽略这行
            // SetValue(CurveRSquaredPropertyKey, rSquared); 

            // 更新缓存，供外部调用 (如 ComputeFitStatistics 方法)
            _cachedFitStats = new FitStatistics
            {
                RSquared = rSquared,
                MaxAbsoluteError = maxAbsError
            };
            _fitStatsCacheValid = true;
        }

        */

        private void UpdateCurveYRangeFromCurrentControlPoints()
        {
            Point[] cp = GetControlPoints();
            double curveYMin = double.MaxValue;
            double curveYMax = double.MinValue;
            int steps = 100;
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double yVal = CubicBezierY(t, cp);
                if (yVal < curveYMin) curveYMin = yVal;
                if (yVal > curveYMax) curveYMax = yVal;
            }
            curveYMin = Math.Max(0, curveYMin);
            curveYMax = Math.Min(MaxY, curveYMax);
            double yMargin = (curveYMax - curveYMin) * 0.05;
            SetValue(CurveYMinPropertyKey, Math.Max(0, curveYMin - yMargin));
            SetValue(CurveYMaxPropertyKey, Math.Min(MaxY, curveYMax + yMargin));
        }

        private void SolveLinearSystem(double[,] A, double[] b, int n)
        {
            for (int col = 0; col < n; col++)
            {
                int maxRow = col;
                double maxVal = Math.Abs(A[col, col]);
                for (int row = col + 1; row < n; row++)
                {
                    if (Math.Abs(A[row, col]) > maxVal)
                    {
                        maxVal = Math.Abs(A[row, col]);
                        maxRow = row;
                    }
                }

                if (maxVal < 1e-12)
                    continue;

                if (maxRow != col)
                {
                    for (int j = col; j < n; j++)
                    {
                        double tmp = A[col, j];
                        A[col, j] = A[maxRow, j];
                        A[maxRow, j] = tmp;
                    }
                    double tmpB = b[col];
                    b[col] = b[maxRow];
                    b[maxRow] = tmpB;
                }

                double pivot = A[col, col];
                for (int j = col + 1; j < n; j++)
                    A[col, j] /= pivot;
                b[col] /= pivot;
                A[col, col] = 1.0;

                for (int row = 0; row < n; row++)
                {
                    if (row != col)
                    {
                        double factor = A[row, col];
                        if (Math.Abs(factor) > 1e-12)
                        {
                            for (int j = col + 1; j < n; j++)
                                A[row, j] -= factor * A[col, j];
                            b[row] -= factor * b[col];
                            A[row, col] = 0;
                        }
                    }
                }
            }
        }

        private double FindTForX(double x, Point[] cp, double maxX)
        {

            double low = 0, high = 1, t = 0.5;
            double epsilon = 0.0001;

            for (int i = 0; i < 30; i++)
            {
                double testX = CubicBezierX(t, cp);
                if (Math.Abs(testX - x) < epsilon) return t;

                if (testX < x)
                    low = t;
                else
                    high = t;

                t = (low + high) / 2;
            }

            return t;

            /*
            double t = x / maxX; // 初始估计
            for (int i = 0; i < 10; i++) // 牛顿法通常只需几次迭代
            {
                double currentX = CubicBezierX(t, cp) - x;
                double derivative = CubicBezierDerivativeX(t, cp);
                if (Math.Abs(derivative) < 1e-12) break;
                double delta = currentX / derivative;
                t -= delta;
                t = Math.Max(0, Math.Min(1, t)); // 限制在[0,1]内
                if (Math.Abs(delta) < 1e-8) break; // 收敛
            }
            return t;
            */
        }

        private double CubicBezierX(double t, Point[] cp)
        {
            double u = 1 - t;
            return u * u * u * cp[0].X +
                   3 * u * u * t * cp[1].X +
                   3 * u * t * t * cp[2].X +
                   t * t * t * cp[3].X;
        }

        private double CubicBezierY(double t, Point[] cp)
        {
            double u = 1 - t;
            return u * u * u * cp[0].Y +
                   3 * u * u * t * cp[1].Y +
                   3 * u * t * t * cp[2].Y +
                   t * t * t * cp[3].Y;
        }

        public void ResetControlPoints()
        {
            _suppressLayoutUpdate = true;
            try
            {
                if (_hasImportedData)
                {
                    Array.Copy(_originalGammaTable, _yGammaTable, _yGammaTable.Length);
                }
                else
                {
                    Array.Copy(_defaultGammaTable, _yGammaTable, _yGammaTable.Length);
                    MaxY = ComputeMaxYFromData();
                }

                Array.Copy(_yGammaTable, _referenceDataForStats, _yGammaTable.Length);
                P0Y = 0;
                P3Y = MaxY;
                FitControlPointsFromData(_yGammaTable);
            }
            finally
            {
                _suppressLayoutUpdate = false;
            }

            RedrawAll();
            FireCurveChanged();
        }

        private double ComputeMaxYFromData()
        {
            short maxVal = 0;
            foreach (short v in _yGammaTable)
            {
                if (v > maxVal) maxVal = v;
            }
            double computed = maxVal;
            if (computed > 1023) computed = 1023;
            if (computed < 1) computed = 255;
            return computed;
        }

        public FitStatistics ComputeFitStatistics()
        {
            if (_fitStatsCacheValid)
                return _cachedFitStats;

            Point[] cp = GetControlPoints();
            short[] refData = _referenceDataForStats ?? _yGammaTable;
            int n = refData.Length;

            double meanY = 0;
            for (int i = 0; i < n; i++)
                meanY += refData[i];
            meanY /= n;

            double ssTot = 0;
            double ssRes = 0;
            double maxAbsError = 0;

            for (int i = 0; i < n; i++)
            {
                double dataX = (MaxX * i) / (n - 1);
                double t = FindTForX(dataX, cp, MaxX);
                double predY = CubicBezierY(t, cp);
                double actual = refData[i];

                double diff = actual - predY;
                ssRes += diff * diff;
                double diffMean = actual - meanY;
                ssTot += diffMean * diffMean;

                double absError = Math.Abs(diff);
                if (absError > maxAbsError)
                    maxAbsError = absError;
            }

            double rSquared = ssTot > 0 ? 1.0 - (ssRes / ssTot) : 1.0;
            _cachedFitStats = new FitStatistics { RSquared = rSquared, MaxAbsoluteError = maxAbsError };
            _fitStatsCacheValid = true;
            return _cachedFitStats;
        }

        #endregion
    }

    public struct FitStatistics
    {
        public double RSquared { get; set; }
        public double MaxAbsoluteError { get; set; }
    }
}

/*
 //* 2024-06-15: 优化拟合算法，提升性能和拟合质量
 //* - 引入多初始值全局搜索，增强鲁棒性，减少局部最优风险
 //* - 使用数值差分计算雅可比矩阵，提升稳定性
 //* - 全程使用 double 类型计算，避免精度损失
 //* - 添加拟合统计信息 (R² 和最大绝对误差)，提供拟合质量反馈
 //* - 优化控制点更新逻辑，确保曲线单调性和边界约束
 //* - 代码结构调整，增强可读性和维护性

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ThunderSE.Ui.CommonCustomControl
{
    public class BezierCurveEditor : Control
    {
        #region Nested Types

        private Canvas _canvas;
        private Path _curvePath;
        private Path _gammaTablePath;
        private readonly List<Ellipse> _controlPointMarkers = new List<Ellipse>();
        private readonly List<Line> _controlLines = new List<Line>();
        private readonly List<TextBlock> _controlPointLabels = new List<TextBlock>();
        private readonly List<FrameworkElement> _gridElements = new List<FrameworkElement>();
        private readonly List<TextBlock> _axisLabels = new List<TextBlock>();
        private readonly List<FrameworkElement> _dataPointElements = new List<FrameworkElement>();
        private int _draggingPointIndex = -1;
        private bool _isInitialized = false;
        private const double ChartPadding = 40;

        private static readonly int[] KeyPointXValues = new int[]
        {
            0, 1, 3, 6, 10, 16, 26, 39, 55, 71,
            87, 103, 119, 135, 151, 167, 191, 223, 239, 255
        };

        private short[] _yGammaTable = new short[]
        {
            0x0, 0x8d, 0xb5, 0xd1, 0xe8, 0xfb, 0x10c, 0x11b, 0x129, 0x136,
            0x142, 0x14d, 0x157, 0x161, 0x16b, 0x174, 0x17c, 0x185, 0x18d, 0x194,
            0x19c, 0x1a3, 0x1aa, 0x1b1, 0x1b8, 0x1be, 0x1c4, 0x1cb, 0x1d1, 0x1d6,
            0x1dc, 0x1e2, 0x1e7, 0x1ed, 0x1f2, 0x1f7, 0x1fc, 0x201, 0x206, 0x20b,
            0x210, 0x214, 0x219, 0x21d, 0x222, 0x226, 0x22b, 0x22f, 0x233, 0x237,
            0x23b, 0x240, 0x244, 0x247, 0x24b, 0x24f, 0x253, 0x257, 0x25b, 0x25e,
            0x262, 0x266, 0x269, 0x26d, 0x270, 0x274, 0x277, 0x27a, 0x27e, 0x281,
            0x284, 0x288, 0x28b, 0x28e, 0x291, 0x295, 0x298, 0x29b, 0x29e, 0x2a1,
            0x2a4, 0x2a7, 0x2aa, 0x2ad, 0x2b0, 0x2b3, 0x2b6, 0x2b8, 0x2bb, 0x2be,
            0x2c1, 0x2c4, 0x2c7, 0x2c9, 0x2cc, 0x2cf, 0x2d1, 0x2d4, 0x2d7, 0x2d9,
            0x2dc, 0x2df, 0x2e1, 0x2e4, 0x2e6, 0x2e9, 0x2eb, 0x2ee, 0x2f0, 0x2f3,
            0x2f5, 0x2f8, 0x2fa, 0x2fd, 0x2ff, 0x301, 0x304, 0x306, 0x309, 0x30b,
            0x30d, 0x310, 0x312, 0x314, 0x316, 0x319, 0x31b, 0x31d, 0x31f, 0x322,
            0x324, 0x326, 0x328, 0x32a, 0x32d, 0x32f, 0x331, 0x333, 0x335, 0x337,
            0x339, 0x33c, 0x33e, 0x340, 0x342, 0x344, 0x346, 0x348, 0x34a, 0x34c,
            0x34e, 0x350, 0x352, 0x354, 0x356, 0x358, 0x35a, 0x35c, 0x35e, 0x360,
            0x362, 0x364, 0x366, 0x368, 0x369, 0x36b, 0x36d, 0x36f, 0x371, 0x373,
            0x375, 0x377, 0x378, 0x37a, 0x37c, 0x37e, 0x380, 0x382, 0x383, 0x385,
            0x387, 0x389, 0x38b, 0x38c, 0x38e, 0x390, 0x392, 0x393, 0x395, 0x397,
            0x399, 0x39a, 0x39c, 0x39e, 0x39f, 0x3a1, 0x3a3, 0x3a5, 0x3a6, 0x3a8,
            0x3aa, 0x3ab, 0x3ad, 0x3af, 0x3b0, 0x3b2, 0x3b4, 0x3b5, 0x3b7, 0x3b8,
            0x3ba, 0x3bc, 0x3bd, 0x3bf, 0x3c1, 0x3c2, 0x3c4, 0x3c5, 0x3c7, 0x3c8,
            0x3ca, 0x3cc, 0x3cd, 0x3cf, 0x3d0, 0x3d2, 0x3d3, 0x3d5, 0x3d7, 0x3d8,
            0x3da, 0x3db, 0x3dd, 0x3de, 0x3e0, 0x3e1, 0x3e3, 0x3e4, 0x3e6, 0x3e7,
            0x3e9, 0x3ea, 0x3ec, 0x3ed, 0x3ef, 0x3f0, 0x3f2, 0x3f3, 0x3f4, 0x3f6,
            0x3f7, 0x3f9, 0x3fa, 0x3fc, 0x3fd, 0x3ff
        };

        private short[] _originalGammaTable = new short[]
        {
            0x0, 0x8d, 0xb5, 0xd1, 0xe8, 0xfb, 0x10c, 0x11b, 0x129, 0x136,
            0x142, 0x14d, 0x157, 0x161, 0x16b, 0x174, 0x17c, 0x185, 0x18d, 0x194,
            0x19c, 0x1a3, 0x1aa, 0x1b1, 0x1b8, 0x1be, 0x1c4, 0x1cb, 0x1d1, 0x1d6,
            0x1dc, 0x1e2, 0x1e7, 0x1ed, 0x1f2, 0x1f7, 0x1fc, 0x201, 0x206, 0x20b,
            0x210, 0x214, 0x219, 0x21d, 0x222, 0x226, 0x22b, 0x22f, 0x233, 0x237,
            0x23b, 0x240, 0x244, 0x247, 0x24b, 0x24f, 0x253, 0x257, 0x25b, 0x25e,
            0x262, 0x266, 0x269, 0x26d, 0x270, 0x274, 0x277, 0x27a, 0x27e, 0x281,
            0x284, 0x288, 0x28b, 0x28e, 0x291, 0x295, 0x298, 0x29b, 0x29e, 0x2a1,
            0x2a4, 0x2a7, 0x2aa, 0x2ad, 0x2b0, 0x2b3, 0x2b6, 0x2b8, 0x2bb, 0x2be,
            0x2c1, 0x2c4, 0x2c7, 0x2c9, 0x2cc, 0x2cf, 0x2d1, 0x2d4, 0x2d7, 0x2d9,
            0x2dc, 0x2df, 0x2e1, 0x2e4, 0x2e6, 0x2e9, 0x2eb, 0x2ee, 0x2f0, 0x2f3,
            0x2f5, 0x2f8, 0x2fa, 0x2fd, 0x2ff, 0x301, 0x304, 0x306, 0x309, 0x30b,
            0x30d, 0x310, 0x312, 0x314, 0x316, 0x319, 0x31b, 0x31d, 0x31f, 0x322,
            0x324, 0x326, 0x328, 0x32a, 0x32d, 0x32f, 0x331, 0x333, 0x335, 0x337,
            0x339, 0x33c, 0x33e, 0x340, 0x342, 0x344, 0x346, 0x348, 0x34a, 0x34c,
            0x34e, 0x350, 0x352, 0x354, 0x356, 0x358, 0x35a, 0x35c, 0x35e, 0x360,
            0x362, 0x364, 0x366, 0x368, 0x369, 0x36b, 0x36d, 0x36f, 0x371, 0x373,
            0x375, 0x377, 0x378, 0x37a, 0x37c, 0x37e, 0x380, 0x382, 0x383, 0x385,
            0x387, 0x389, 0x38b, 0x38c, 0x38e, 0x390, 0x392, 0x393, 0x395, 0x397,
            0x399, 0x39a, 0x39c, 0x39e, 0x39f, 0x3a1, 0x3a3, 0x3a5, 0x3a6, 0x3a8,
            0x3aa, 0x3ab, 0x3ad, 0x3af, 0x3b0, 0x3b2, 0x3b4, 0x3b5, 0x3b7, 0x3b8,
            0x3ba, 0x3bc, 0x3bd, 0x3bf, 0x3c1, 0x3c2, 0x3c4, 0x3c5, 0x3c7, 0x3c8,
            0x3ca, 0x3cc, 0x3cd, 0x3cf, 0x3d0, 0x3d2, 0x3d3, 0x3d5, 0x3d7, 0x3d8,
            0x3da, 0x3db, 0x3dd, 0x3de, 0x3e0, 0x3e1, 0x3e3, 0x3e4, 0x3e6, 0x3e7,
            0x3e9, 0x3ea, 0x3ec, 0x3ed, 0x3ef, 0x3f0, 0x3f2, 0x3f3, 0x3f4, 0x3f6,
            0x3f7, 0x3f9, 0x3fa, 0x3fc, 0x3fd, 0x3ff
        };

        private Point _initialP1;
        private Point _initialP2;
        private FitStatistics _cachedFitStats;
        private bool _fitStatsCacheValid;
        private double _zoomOffsetX;
        private double _zoomOffsetY;
        private bool _hasImportedData;
        private short[] _defaultGammaTable;
        private bool _suppressLayoutUpdate;
        private short[] _referenceDataForStats;

        #endregion

        #region Dependency Properties
        public static readonly DependencyProperty P1XProperty = DependencyProperty.Register(
            "P1X", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(85.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnControlPointChanged));

        public static readonly DependencyProperty P1YProperty = DependencyProperty.Register(
            "P1Y", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(682.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnControlPointChanged));

        public static readonly DependencyProperty P2XProperty = DependencyProperty.Register(
            "P2X", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(170.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnControlPointChanged));

        public static readonly DependencyProperty P2YProperty = DependencyProperty.Register(
            "P2Y", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(341.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnControlPointChanged));

        public static readonly DependencyProperty P0YProperty = DependencyProperty.Register(
            "P0Y", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnP0P3Changed));

        public static readonly DependencyProperty P3YProperty = DependencyProperty.Register(
            "P3Y", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(1023.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnP0P3Changed));

        public static readonly DependencyProperty MaxXProperty = DependencyProperty.Register(
            "MaxX", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(255.0, OnLayoutChanged));

        public static readonly DependencyProperty MaxYProperty = DependencyProperty.Register(
            "MaxY", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(1023.0, OnLayoutChanged));

        public static readonly DependencyProperty ZoomLevelProperty = DependencyProperty.Register(
            "ZoomLevel", typeof(double), typeof(BezierCurveEditor),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnZoomLevelChanged));

        private static readonly DependencyPropertyKey CurveYMinPropertyKey =
            DependencyProperty.RegisterReadOnly("CurveYMin", typeof(double), typeof(BezierCurveEditor),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.None));

        public static readonly DependencyProperty CurveYMinProperty = CurveYMinPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CurveYMaxPropertyKey =
            DependencyProperty.RegisterReadOnly("CurveYMax", typeof(double), typeof(BezierCurveEditor),
                new FrameworkPropertyMetadata(1023.0, FrameworkPropertyMetadataOptions.None));

        public static readonly DependencyProperty CurveYMaxProperty = CurveYMaxPropertyKey.DependencyProperty;

        public event EventHandler CurveChanged;
        public event EventHandler GammaTableChanged;

        public double P1X
        {
            get { return (double)GetValue(P1XProperty); }
            set { SetValue(P1XProperty, value); }
        }

        public double P1Y
        {
            get { return (double)GetValue(P1YProperty); }
            set { SetValue(P1YProperty, value); }
        }

        public double P2X
        {
            get { return (double)GetValue(P2XProperty); }
            set { SetValue(P2XProperty, value); }
        }

        public double P2Y
        {
            get { return (double)GetValue(P2YProperty); }
            set { SetValue(P2YProperty, value); }
        }

        public double P0Y
        {
            get { return (double)GetValue(P0YProperty); }
            set { SetValue(P0YProperty, value); }
        }

        public double P3Y
        {
            get { return (double)GetValue(P3YProperty); }
            set { SetValue(P3YProperty, value); }
        }

        public double MaxX
        {
            get { return (double)GetValue(MaxXProperty); }
            set { SetValue(MaxXProperty, value); }
        }

        public double MaxY
        {
            get { return (double)GetValue(MaxYProperty); }
            set { SetValue(MaxYProperty, value); }
        }

        public double ZoomLevel
        {
            get { return (double)GetValue(ZoomLevelProperty); }
            set { SetValue(ZoomLevelProperty, value); }
        }

        public double CurveYMin
        {
            get { return (double)GetValue(CurveYMinProperty); }
        }

        public double CurveYMax
        {
            get { return (double)GetValue(CurveYMaxProperty); }
        }

        public short[] GammaTable
        {
            get { return _yGammaTable; }
        }

        #endregion

        static BezierCurveEditor()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(BezierCurveEditor),
                new FrameworkPropertyMetadata(typeof(BezierCurveEditor)));
        }

        public BezierCurveEditor()
        {
            Loaded += OnLoaded;
            SizeChanged += OnSizeChanged;
            //MouseWheel += OnControlMouseWheel;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isInitialized = true;
            _defaultGammaTable = new short[_yGammaTable.Length];
            Array.Copy(_yGammaTable, _defaultGammaTable, _yGammaTable.Length);
            _referenceDataForStats = new short[_yGammaTable.Length];
            Array.Copy(_yGammaTable, _referenceDataForStats, _yGammaTable.Length);
            MaxY = ComputeMaxYFromData();
            P0Y = 0;
            P3Y = MaxY;
            FitControlPointsFromData(_yGammaTable);
            _initialP1 = new Point(P1X, P1Y);
            _initialP2 = new Point(P2X, P2Y);
            RedrawAll();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isInitialized)
                RedrawAll();
        }

        private static void OnControlPointChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            BezierCurveEditor editor = (BezierCurveEditor)d;
            editor._fitStatsCacheValid = false;
            if (editor._suppressLayoutUpdate) return;
            if (editor._isInitialized)
            {
                editor.Dispatcher?.Invoke(() =>
                {
                    editor.UpdateGammaTable();
                    editor.RedrawAll();
                });
            }
        }

        private static void OnP0P3Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            BezierCurveEditor editor = (BezierCurveEditor)d;
            editor._fitStatsCacheValid = false;
            if (editor._suppressLayoutUpdate) return;
            if (editor._isInitialized)
            {
                editor.Dispatcher?.Invoke(() =>
                {
                    editor.UpdateGammaTable();
                    editor.RedrawAll();
                });
            }
        }

        private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            BezierCurveEditor editor = (BezierCurveEditor)d;
            if (editor._suppressLayoutUpdate) return;
            editor._fitStatsCacheValid = false;
            if (editor._isInitialized)
            {
                editor.Dispatcher?.Invoke(() =>
                {
                    editor.UpdateGammaTable();
                    editor.RedrawAll();
                });
            }
        }

        private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            BezierCurveEditor editor = (BezierCurveEditor)d;
            editor._zoomOffsetX = 0;
            editor._zoomOffsetY = 0;
            if (editor._isInitialized)
            {
                editor.Dispatcher?.Invoke(() => editor.RedrawAll());
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _canvas = GetTemplateChild("PART_Canvas") as Canvas;
            if (_canvas == null) return;

            _curvePath = new Path
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0x3B)),
                StrokeThickness = 3
            };
            _canvas.Children.Add(_curvePath);

            _gammaTablePath = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x50, 0x00, 0xBC, 0xD4)),
                Stroke = null
            };
            _canvas.Children.Add(_gammaTablePath);

            for (int i = 0; i < 3; i++)
            {
                Line line = new Line
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 4 }
                };
                _canvas.Children.Add(line);
                _controlLines.Add(line);
            }

            string[] labels = { "P0", "P1", "P2", "P3" };
            for (int i = 0; i < 4; i++)
            {
                Ellipse marker = new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Stroke = Brushes.White,
                    StrokeThickness = 2,
                    Cursor = Cursors.SizeAll,
                    Tag = i
                };

                if (i == 0 || i == 3)
                {
                    marker.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x57, 0x22));
                }
                else
                {
                    marker.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                }

                marker.MouseLeftButtonDown += OnControlPointMouseDown;
                marker.MouseMove += OnControlPointMouseMove;
                marker.MouseLeftButtonUp += OnControlPointMouseUp;

                _canvas.Children.Add(marker);
                _controlPointMarkers.Add(marker);

                TextBlock label = new TextBlock
                {
                    Text = labels[i],
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11
                };
                _canvas.Children.Add(label);
                _controlPointLabels.Add(label);
            }

            _canvas.MouseMove += OnCanvasMouseMove;
            _canvas.MouseLeftButtonUp += OnCanvasMouseUp;
            _canvas.MouseWheel += OnCanvasMouseWheel;
        }

        private Point[] GetControlPoints()
        {
            return new Point[]
            {
                new Point(0, P0Y),
                new Point(P1X, P1Y),
                new Point(P2X, P2Y),
                new Point(MaxX, P3Y)
            };
        }

        private double GetDrawWidth()
        {
            return _canvas.ActualWidth - 2 * ChartPadding;
        }

        private double GetDrawHeight()
        {
            return _canvas.ActualHeight - 2 * ChartPadding;
        }

        private double GetVisibleMaxX()
        {
            return MaxX / ZoomLevel;
        }

        private double GetVisibleMaxY()
        {
            return MaxY / ZoomLevel;
        }

        private Point DataToCanvas(double dataX, double dataY)
        {
            double visibleMaxX = GetVisibleMaxX();
            double visibleMaxY = GetVisibleMaxY();
            double offsetX = _zoomOffsetX;
            double offsetY = _zoomOffsetY;

            double drawW = GetDrawWidth();
            double drawH = GetDrawHeight();

            double normX = (dataX - offsetX) / visibleMaxX;
            double normY = 1.0 - (dataY - offsetY) / visibleMaxY;

            return new Point(
                ChartPadding + normX * drawW,
                ChartPadding + normY * drawH
            );
        }

        private Point DataToCanvasNoZoom(double dataX, double dataY)
        {
            double drawW = GetDrawWidth();
            double drawH = GetDrawHeight();

            double normX = dataX / MaxX;
            double normY = 1.0 - dataY / MaxY;

            return new Point(
                ChartPadding + normX * drawW,
                ChartPadding + normY * drawH
            );
        }

        private Point CanvasToData(double canvasX, double canvasY)
        {
            double visibleMaxX = GetVisibleMaxX();
            double visibleMaxY = GetVisibleMaxY();
            double offsetX = _zoomOffsetX;
            double offsetY = _zoomOffsetY;

            double drawW = GetDrawWidth();
            double drawH = GetDrawHeight();

            double normX = (canvasX - ChartPadding) / drawW;
            double normY = 1.0 - (canvasY - ChartPadding) / drawH;

            return new Point(
                Math.Max(0, Math.Min(MaxX, normX * visibleMaxX + offsetX)),
                Math.Max(0, Math.Min(MaxY, normY * visibleMaxY + offsetY))
            );
        }

        private void RedrawAll()
        {
            if (_canvas == null || !_isInitialized) return;

            DrawGrid();
            DrawCurve();
            DrawGammaTablePoints();
            DrawDataPoints();
            DrawControlPoints();
        }

        private void DrawGrid()
        {
            foreach (FrameworkElement elem in _gridElements)
                _canvas.Children.Remove(elem);
            _gridElements.Clear();

            foreach (TextBlock label in _axisLabels)
                _canvas.Children.Remove(label);
            _axisLabels.Clear();

            SolidColorBrush gridBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            SolidColorBrush axisBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            int xGridCount = 8;
            int yGridCount = 8;

            for (int i = 0; i <= xGridCount; i++)
            {
                double ratio = i / (double)xGridCount;

                Line vLine = new Line
                {
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                    X1 = ChartPadding + ratio * GetDrawWidth(),
                    Y1 = ChartPadding,
                    X2 = ChartPadding + ratio * GetDrawWidth(),
                    Y2 = ChartPadding + GetDrawHeight()
                };
                _canvas.Children.Add(vLine);
                _gridElements.Add(vLine);

                int xLabelVal = (int)Math.Round(ratio * MaxX);
                TextBlock xLabel = new TextBlock
                {
                    Text = xLabelVal.ToString(),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                    FontSize = 10
                };
                Point xPos = DataToCanvasNoZoom(ratio * MaxX, 0);
                Canvas.SetLeft(xLabel, xPos.X - 10);
                Canvas.SetTop(xLabel, xPos.Y + 4);
                _canvas.Children.Add(xLabel);
                _axisLabels.Add(xLabel);
            }

            for (int i = 0; i <= yGridCount; i++)
            {
                double ratio = i / (double)yGridCount;

                Line hLine = new Line
                {
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                    X1 = ChartPadding,
                    Y1 = ChartPadding + ratio * GetDrawHeight(),
                    X2 = ChartPadding + GetDrawWidth(),
                    Y2 = ChartPadding + ratio * GetDrawHeight()
                };
                _canvas.Children.Add(hLine);
                _gridElements.Add(hLine);

                int yLabelVal = (int)Math.Round(CurveYMin + (1.0 - ratio) * (CurveYMax - CurveYMin));
                TextBlock yLabel = new TextBlock
                {
                    Text = yLabelVal.ToString(),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                    FontSize = 10
                };
                double yCanvasNorm = 1.0 - (yLabelVal - CurveYMin) / (CurveYMax - CurveYMin);
                double yCanvasY = ChartPadding + yCanvasNorm * GetDrawHeight();
                Canvas.SetLeft(yLabel, ChartPadding - 32);
                Canvas.SetTop(yLabel, yCanvasY - 6);
                _canvas.Children.Add(yLabel);
                _axisLabels.Add(yLabel);
            }

            Rectangle borderRect = new Rectangle
            {
                Stroke = axisBrush,
                StrokeThickness = 1,
                Width = GetDrawWidth(),
                Height = GetDrawHeight()
            };
            Canvas.SetLeft(borderRect, ChartPadding);
            Canvas.SetTop(borderRect, ChartPadding);
            _canvas.Children.Add(borderRect);
            _gridElements.Add(borderRect);

            Line diagonalLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 6, 4 },
                X1 = ChartPadding,
                Y1 = ChartPadding + GetDrawHeight(),
                X2 = ChartPadding + GetDrawWidth(),
                Y2 = ChartPadding
            };
            _canvas.Children.Add(diagonalLine);
            _gridElements.Add(diagonalLine);
        }

        private void DrawCurve()
        {
            if (_curvePath == null) return;

            Point[] points = GetControlPoints();
            Point[] canvasPoints = new Point[4];
            for (int i = 0; i < 4; i++)
            {
                canvasPoints[i] = DataToCanvas(points[i].X, points[i].Y);
            }

            PathGeometry geometry = new PathGeometry();
            PathFigure figure = new PathFigure();
            figure.StartPoint = canvasPoints[0];
            BezierSegment bezier = new BezierSegment(canvasPoints[1], canvasPoints[2], canvasPoints[3], true);
            figure.Segments.Add(bezier);
            geometry.Figures.Add(figure);

            _curvePath.Data = geometry;
        }

        private void DrawGammaTablePoints()
        {
            if (_gammaTablePath == null || _yGammaTable == null) return;

            PathGeometry geometry = new PathGeometry();

            for (int i = 0; i < _yGammaTable.Length; i++)
            {
                double yVal = _yGammaTable[i];
                Point canvasPos = DataToCanvas(i, yVal);

                double left = canvasPos.X - 1.5;
                double top = canvasPos.Y - 1.5;
                double right = canvasPos.X + 1.5;
                double bottom = canvasPos.Y + 1.5;

                PathFigure figure = new PathFigure();
                figure.StartPoint = new Point(left, top);
                figure.IsClosed = true;
                figure.IsFilled = true;

                figure.Segments.Add(new LineSegment(new Point(right, top), true));
                figure.Segments.Add(new LineSegment(new Point(right, bottom), true));
                figure.Segments.Add(new LineSegment(new Point(left, bottom), true));

                geometry.Figures.Add(figure);
            }

            _gammaTablePath.Data = geometry;
        }

        private void DrawDataPoints()
        {
            foreach (FrameworkElement elem in _dataPointElements)
                _canvas.Children.Remove(elem);
            _dataPointElements.Clear();

            Point[] cp = GetControlPoints();
            SolidColorBrush vLineBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xBC, 0xD4));

            for (int i = 0; i < KeyPointXValues.Length; i++)
            {
                int keyX = KeyPointXValues[i];

                if (keyX == 0 || keyX == (int)MaxX)
                    continue;

                double t = FindTForX(keyX, cp, MaxX);
                double y = CubicBezierY(t, cp);
                y = Math.Max(0, Math.Min(MaxY, y));

                Point canvasPos = DataToCanvas(keyX, y);
                Point bottomPos = DataToCanvas(keyX, 0);

                Line vLine = new Line
                {
                    Stroke = vLineBrush,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 3 },
                    X1 = canvasPos.X,
                    Y1 = bottomPos.Y,
                    X2 = canvasPos.X,
                    Y2 = canvasPos.Y
                };
                _canvas.Children.Add(vLine);
                _dataPointElements.Add(vLine);

                int yRounded = (int)Math.Round(y);

                Border tipBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x1A, 0x1A, 0x2E)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 3, 6, 3),
                    Child = new TextBlock
                    {
                        Text = string.Format("X: {0}  Y: {1}", keyX, yRounded),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)),
                        FontSize = 11,
                        FontFamily = new FontFamily("Consolas")
                    }
                };

                Ellipse dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)),
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    Cursor = Cursors.Cross,
                    ToolTip = tipBorder
                };
                Canvas.SetLeft(dot, canvasPos.X - dot.Width / 2);
                Canvas.SetTop(dot, canvasPos.Y - dot.Height / 2);
                _canvas.Children.Add(dot);
                _dataPointElements.Add(dot);
            }
        }

        private void DrawControlPoints()
        {
            Point[] points = GetControlPoints();

            for (int i = 0; i < 4; i++)
            {
                Point canvasPos = DataToCanvas(points[i].X, points[i].Y);

                if (i < _controlPointMarkers.Count)
                {
                    Ellipse marker = _controlPointMarkers[i];
                    Canvas.SetLeft(marker, canvasPos.X - marker.Width / 2);
                    Canvas.SetTop(marker, canvasPos.Y - marker.Height / 2);
                }

                if (i < _controlPointLabels.Count)
                {
                    TextBlock label = _controlPointLabels[i];
                    Canvas.SetLeft(label, canvasPos.X - 8);
                    Canvas.SetTop(label, canvasPos.Y - 20);
                }
            }

            for (int i = 0; i < _controlLines.Count && i < 3; i++)
            {
                Point from = DataToCanvas(points[i].X, points[i].Y);
                Point to = DataToCanvas(points[i + 1].X, points[i + 1].Y);
                _controlLines[i].X1 = from.X;
                _controlLines[i].Y1 = from.Y;
                _controlLines[i].X2 = to.X;
                _controlLines[i].Y2 = to.Y;
            }
        }

        #region Event Handlers
        private void OnControlPointMouseDown(object sender, MouseButtonEventArgs e)
        {
            Ellipse marker = sender as Ellipse;
            if (marker == null) return;

            int index = (int)marker.Tag;
            _draggingPointIndex = index;
            marker.CaptureMouse();
            e.Handled = true;
        }

        private void OnControlPointMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingPointIndex < 0) return;

            Point pos = e.GetPosition(_canvas);
            Point dataPos = CanvasToData(pos.X, pos.Y);

            if (_draggingPointIndex == 0)
            {
                P0Y = Math.Round(Math.Max(0, Math.Min(MaxY, dataPos.Y)));
            }
            else if (_draggingPointIndex == 1)
            {
                P1X = Math.Round(Math.Max(0, Math.Min(MaxX, dataPos.X)));
                P1Y = Math.Round(Math.Max(0, Math.Min(MaxY, dataPos.Y)));
            }
            else if (_draggingPointIndex == 2)
            {
                P2X = Math.Round(Math.Max(0, Math.Min(MaxX, dataPos.X)));
                P2Y = Math.Round(Math.Max(0, Math.Min(MaxY, dataPos.Y)));
            }
            else if (_draggingPointIndex == 3)
            {
                P3Y = Math.Round(Math.Max(0, Math.Min(MaxY, dataPos.Y)));
            }

            UpdateCurveYRangeFromCurrentControlPoints();

            UpdateGammaTable();
            RedrawAll();
            FireCurveChanged();
            e.Handled = true;
        }

        private void OnControlPointMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingPointIndex >= 0)
            {
                Ellipse marker = sender as Ellipse;
                if (marker != null)
                    marker.ReleaseMouseCapture();
                _draggingPointIndex = -1;
                e.Handled = true;
            }
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingPointIndex < 0) return;

            Point pos = e.GetPosition(_canvas);
            Point dataPos = CanvasToData(pos.X, pos.Y);

            if (_draggingPointIndex == 1)
            {
                P1X = Math.Round(dataPos.X);
                P1Y = Math.Round(dataPos.Y);
            }
            else if (_draggingPointIndex == 2)
            {
                P2X = Math.Round(dataPos.X);
                P2Y = Math.Round(dataPos.Y);
            }
            else if (_draggingPointIndex == 3)
            {
                P3Y = Math.Round(Math.Max(0, Math.Min(MaxY, dataPos.Y)));
            }

            UpdateCurveYRangeFromCurrentControlPoints();

            UpdateGammaTable();
            RedrawAll();
            FireCurveChanged();
        }

        private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            _draggingPointIndex = -1;
        }

        private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_canvas == null) return;

            Point mousePos = e.GetPosition(_canvas);
            ApplyZoomAt(mousePos, e.Delta);
            e.Handled = true;
        }

        private void OnControlMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_canvas != null && _canvas.IsMouseOver)
            {
                Point mousePos = e.GetPosition(_canvas);
                ApplyZoomAt(mousePos, e.Delta);
                e.Handled = true;
            }
        }

        #endregion

        #region Helper Methods

        private void ApplyZoomAt(Point mousePos, int delta)
        {
            double factor = delta > 0 ? 1.1 : 0.9;
            double newZoom = ZoomLevel * factor;
            newZoom = Math.Max(0.3, Math.Min(5.0, newZoom));

            if (Math.Abs(newZoom - ZoomLevel) < 0.001)
                return;

            _zoomOffsetX = 0;
            _zoomOffsetY = 0;

            ZoomLevel = newZoom;
        }

        private void FireCurveChanged()
        {
            EventHandler handler = CurveChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);

            EventHandler handler2 = GammaTableChanged;
            if (handler2 != null)
                handler2(this, EventArgs.Empty);
        }


        // 优化后的查找t值方法 - 使用牛顿法结合二分法
        private double FindTForX(double x, Point[] cp, double maxX)
        {
            // 先使用二分法获得初值
            double low = 0, high = 1, t = 0.5;
            double epsilon = 0.00001; // 提高精度

            // 二分法快速定位
            for (int i = 0; i < 30; i++)
            {
                double testX = CubicBezierX(t, cp);
                if (Math.Abs(testX - x) < epsilon) return t;

                if (testX < x)
                    low = t;
                else
                    high = t;

                t = (low + high) / 2;
            }

            // 牛顿法精炼
            for (int i = 0; i < 20; i++)
            {
                double currentX = CubicBezierX(t, cp) - x;
                double derivative = CubicBezierDerivativeX(t, cp);

                if (Math.Abs(derivative) < 1e-12) break;

                double delta = currentX / derivative;
                t -= delta;

                // 保持t在有效范围内
                t = Math.Max(0, Math.Min(1, t));

                if (Math.Abs(delta) < 1e-10) break; // 更高精度
            }

            return t;
        }

        // 优化的权重计算 - 基于曲率
        private void ComputeCurvatureBasedWeights(Point[] cp, double[] curvatureWeights, double maxX)
        {
            int n = curvatureWeights.Length;

            // 计算贝塞尔曲线的二阶导数以估算曲率
            for (int i = 0; i < n; i++)
            {
                double dataX = (maxX * i) / (n - 1);
                double t = FindTForX(dataX, cp, maxX);

                // 计算一阶导数
                double dxdt = CubicBezierDerivativeX(t, cp);
                double dydt = CubicBezierDerivativeY(t, cp);

                // 计算二阶导数
                double d2xdt2 = 6 * (1 - t) * (cp[2].X - 2 * cp[1].X + cp[0].X) +
                               6 * t * (cp[3].X - 2 * cp[2].X + cp[1].X);
                double d2ydt2 = 6 * (1 - t) * (cp[2].Y - 2 * cp[1].Y + cp[0].Y) +
                               6 * t * (cp[3].Y - 2 * cp[2].Y + cp[1].Y);

                // 曲率公式
                double numerator = Math.Abs(dxdt * d2ydt2 - dydt * d2xdt2);
                double denominator = Math.Pow(dxdt * dxdt + dydt * dydt, 1.5);

                double curvature = denominator > 1e-10 ? numerator / denominator : 0;

                // 使用曲率作为权重 - 曲率大的地方给予更高权重
                curvatureWeights[i] = 1.0 + 10.0 * curvature; // 调整系数以平衡权重
            }
        }

        // 优化的拟合方法 - 结合多阶段优化和曲率权重
        private void FitControlPointsFromData(short[] yValues)
        {
            int n = yValues.Length;

            // 初始估计值
            double p1x = Math.Max(1, MaxX * 0.25);
            double p1y = Math.Max(1, MaxY * 0.40);
            double p2x = Math.Max(1, MaxX * 0.70);
            double p2y = Math.Max(1, MaxY * 0.85);

            double[] tArr = new double[n];
            double[] weights = new double[n];
            double lambda = 0.001;
            double prevError = double.MaxValue;

            // 多阶段优化 - 逐步提高精度
            double[] tolerances = { 1e-4, 1e-6, 1e-8 }; // 不同阶段的容差

            for (int stage = 0; stage < tolerances.Length; stage++)
            {
                double tolerance = tolerances[stage];
                int maxIter = stage == 0 ? 50 : (stage == 1 ? 75 : 100); // 后续阶段需要更多迭代

                for (int iter = 0; iter < maxIter; iter++)
                {
                    Point[] cpCurrent = new Point[]
                    {
                        new Point(0, P0Y),
                        new Point(p1x, p1y),
                        new Point(p2x, p2y),
                        new Point(MaxX, P3Y)
                    };

                    // 更新t值数组
                    for (int i = 0; i < n; i++)
                    {
                        double dataX = (MaxX * i) / (n - 1);
                        tArr[i] = FindTForX(dataX, cpCurrent, MaxX);
                    }

                    // 使用曲率权重而非斜率权重
                    ComputeCurvatureBasedWeights(cpCurrent, weights, MaxX);

                    // 计算残差
                    double[] residuals = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        double dataX = (MaxX * i) / (n - 1);
                        double t = tArr[i];
                        residuals[i] = (CubicBezierY(t, cpCurrent) - yValues[i]) * weights[i];
                    }

                    double error = 0;
                    for (int i = 0; i < n; i++)
                        error += residuals[i] * residuals[i];

                    // 检查收敛
                    if (error < tolerance || Math.Abs(prevError - error) < tolerance)
                        break;
                    prevError = error;

                    const int M = 4; // 参数数量
                    double[,] J = new double[n, M];
                    double eps = 1e-7; // 更小的步长用于梯度计算

                    // 计算雅可比矩阵
                    for (int j = 0; j < M; j++)
                    {
                        double[] pTest = new double[] { p1x, p1y, p2x, p2y };
                        pTest[j] += eps;

                        double[] rPlus = new double[n];
                        for (int i = 0; i < n; i++)
                        {
                            double dataX = (MaxX * i) / (n - 1);
                            Point[] cp = new Point[]
                            {
                                new Point(0, P0Y),
                                new Point(pTest[0], pTest[1]),
                                new Point(pTest[2], pTest[3]),
                                new Point(MaxX, P3Y)
                            };
                            double t = FindTForX(dataX, cp, MaxX);
                            tArr[i] = t;
                            rPlus[i] = (CubicBezierY(t, cp) - yValues[i]) * weights[i];
                        }

                        for (int i = 0; i < n; i++)
                            J[i, j] = (rPlus[i] - residuals[i]) / eps;
                    }

                    // 构建正规方程 J^T*J 和 J^T*r
                    double[,] JtJ = new double[M, M];
                    double[] JtR = new double[M];
                    for (int i = 0; i < M; i++)
                    {
                        for (int j = 0; j < M; j++)
                        {
                            double sum = 0;
                            for (int k = 0; k < n; k++)
                                sum += J[k, i] * J[k, j];
                            JtJ[i, j] = sum;
                        }
                        double sumR = 0;
                        for (int k = 0; k < n; k++)
                            sumR += J[k, i] * residuals[k];
                        JtR[i] = -sumR; // 注意这里是负号
                    }

                    // Levenberg-Marquardt 步骤
                    for (int i = 0; i < M; i++)
                        JtJ[i, i] *= (1.0 + lambda);

                    // 求解线性系统
                    double[] delta = new double[M];
                    Array.Copy(JtR, delta, M);

                    if (!SolveLinearSystem(JtJ, delta, M))
                    {
                        // 如果求解失败，减小lambda并继续
                        lambda *= 0.1;
                        if (lambda < 1e-8) lambda = 1e-8;
                        continue;
                    }

                    // 尝试更新参数
                    double newP1x = Math.Max(0, Math.Min(MaxX, p1x - delta[0]));
                    double newP1y = Math.Max(0, Math.Min(MaxY, p1y - delta[1]));
                    double newP2x = Math.Max(0, Math.Min(MaxX, p2x - delta[2]));
                    double newP2y = Math.Max(0, Math.Min(MaxY, p2y - delta[3]));

                    Point[] cpNew = new Point[]
                    {
                        new Point(0, P0Y),
                        new Point(newP1x, newP1y),
                        new Point(newP2x, newP2y),
                        new Point(MaxX, P3Y)
                    };

                    // 重新计算权重和残差
                    double[] newWeights = new double[n];
                    ComputeCurvatureBasedWeights(cpNew, newWeights, MaxX);

                    double[] newResiduals = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        double dataX = (MaxX * i) / (n - 1);
                        double t = FindTForX(dataX, cpNew, MaxX);
                        newResiduals[i] = (CubicBezierY(t, cpNew) - yValues[i]) * newWeights[i];
                    }

                    double newError = 0;
                    for (int i = 0; i < n; i++)
                        newError += newResiduals[i] * newResiduals[i];

                    if (newError < error)
                    {
                        // 接受更新
                        p1x = newP1x; p1y = newP1y;
                        p2x = newP2x; p2y = newP2y;
                        lambda *= 0.1; // 减小正则化参数
                        if (lambda < 1e-8) lambda = 1e-8;
                    }
                    else
                    {
                        // 拒绝更新，增加正则化参数
                        lambda *= 10;
                        if (lambda > 1e2) lambda = 1e2;
                    }
                }
            }

            // 设置最终结果
            double finalP1x = p1x, finalP1y = p1y, finalP2x = p2x, finalP2y = p2y;

            Point[] cpFinal = new Point[]
            {
                new Point(0, P0Y),
                new Point(finalP1x, finalP1y),
                new Point(finalP2x, finalP2y),
                new Point(MaxX, P3Y)
            };

            // 计算统计信息
            double meanY = 0;
            for (int i = 0; i < n; i++)
                meanY += _referenceDataForStats[i];
            meanY /= n;

            double ssTot = 0, ssRes = 0, maxAbsError = 0;
            for (int i = 0; i < n; i++)
            {
                double dataX = (MaxX * i) / (n - 1);
                double t = FindTForX(dataX, cpFinal, MaxX);
                double predY = CubicBezierY(t, cpFinal);
                double actual = _referenceDataForStats[i];
                double diff = actual - predY;
                ssRes += diff * diff;
                double diffMean = actual - meanY;
                ssTot += diffMean * diffMean;
                double absErr = Math.Abs(diff);
                if (absErr > maxAbsError) maxAbsError = absErr;
            }

            _cachedFitStats = new FitStatistics
            {
                RSquared = ssTot > 0 ? 1.0 - (ssRes / ssTot) : 1.0,
                MaxAbsoluteError = maxAbsError
            };
            _fitStatsCacheValid = true;

            P1X = Math.Round(finalP1x);
            P1Y = Math.Round(finalP1y);
            P2X = Math.Round(finalP2x);
            P2Y = Math.Round(finalP2y);

            // 更新曲线范围
            double curveYMin = double.MaxValue, curveYMax = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                double dataX = (MaxX * i) / (n - 1);
                double t = FindTForX(dataX, cpFinal, MaxX);
                double yVal = CubicBezierY(t, cpFinal);
                if (yVal < curveYMin) curveYMin = yVal;
                if (yVal > curveYMax) curveYMax = yVal;
            }
            curveYMin = Math.Max(0, curveYMin);
            curveYMax = Math.Min(MaxY, curveYMax);
            double yMargin = (curveYMax - curveYMin) * 0.05;
            SetValue(CurveYMinPropertyKey, Math.Max(0, curveYMin - yMargin));
            SetValue(CurveYMaxPropertyKey, Math.Min(MaxY, curveYMax + yMargin));

            UpdateGammaTable();
        }

        // 改进的线性系统求解器，增加了数值稳定性检查
        private bool SolveLinearSystem(double[,] A, double[] b, int n)
        {
            // 创建副本以避免修改原矩阵
            double[,] Ac = new double[n, n];
            double[] bc = new double[n];

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                    Ac[i, j] = A[i, j];
                bc[i] = b[i];
            }

            // 高斯消元法配合部分主元选择
            for (int col = 0; col < n; col++)
            {
                // 寻找主元（绝对值最大的元素）
                int maxRow = col;
                for (int row = col + 1; row < n; row++)
                {
                    if (Math.Abs(Ac[row, col]) > Math.Abs(Ac[maxRow, col]))
                        maxRow = row;
                }

                // 交换行
                if (maxRow != col)
                {
                    for (int j = col; j < n; j++)
                    {
                        double temp = Ac[col, j];
                        Ac[col, j] = Ac[maxRow, j];
                        Ac[maxRow, j] = temp;
                    }
                    double tempB = bc[col];
                    bc[col] = bc[maxRow];
                    bc[maxRow] = tempB;
                }

                // 检查是否为奇异矩阵
                if (Math.Abs(Ac[col, col]) < 1e-12)
                    return false; // 矩阵接近奇异，无法求解

                // 将主元归一化
                double pivot = Ac[col, col];
                for (int j = col; j < n; j++)
                    Ac[col, j] /= pivot;
                bc[col] /= pivot;

                // 消元
                for (int row = 0; row < n; row++)
                {
                    if (row != col)
                    {
                        double factor = Ac[row, col];
                        for (int j = col; j < n; j++)
                            Ac[row, j] -= factor * Ac[col, j];
                        bc[row] -= factor * bc[col];
                    }
                }
            }

            // 将结果复制回原数组
            for (int i = 0; i < n; i++)
                b[i] = bc[i];

            return true;
        }

        private double CubicBezierDerivativeX(double t, Point[] cp)
        {
            double u = 1 - t;
            return 3 * u * u * (cp[1].X - cp[0].X) +
                   6 * u * t * (cp[2].X - cp[1].X) +
                   3 * t * t * (cp[3].X - cp[2].X);
        }

        private double CubicBezierDerivativeY(double t, Point[] cp)
        {
            double u = 1 - t;
            return 3 * u * u * (cp[1].Y - cp[0].Y) +
                   6 * u * t * (cp[2].Y - cp[1].Y) +
                   3 * t * t * (cp[3].Y - cp[2].Y);
        }

        private double CubicBezierX(double t, Point[] cp)
        {
            double u = 1 - t;
            return u * u * u * cp[0].X +
                   3 * u * u * t * cp[1].X +
                   3 * u * t * t * cp[2].X +
                   t * t * t * cp[3].X;
        }

        private double CubicBezierY(double t, Point[] cp)
        {
            double u = 1 - t;
            return u * u * u * cp[0].Y +
                   3 * u * u * t * cp[1].Y +
                   3 * u * t * t * cp[2].Y +
                   t * t * t * cp[3].Y;
        }

        public void UpdateGammaTable()
        {
            Point[] cp = GetControlPoints();

            for (int x = 0; x < _yGammaTable.Length; x++)
            {
                double t = FindTForX(x, cp, MaxX);
                double y = CubicBezierY(t, cp);
                _yGammaTable[x] = (short)Math.Max(0, Math.Min((int)MaxY, Math.Round(y)));
            }
        }

        public void SetDefaultGammaTable(byte[] table)
        {
            //LoadGammaTable(_defaultGammaTable, preserveMaxY: false);
            try
            {
                int n = Math.Min(table.Length, _yGammaTable.Length);
                for (int i = 0; i < n; i++)
                {
                    _yGammaTable[i] = table[i];
                    _originalGammaTable[i] = table[i];
                }
                _defaultGammaTable = new short[_yGammaTable.Length];
                Array.Copy(_yGammaTable, _defaultGammaTable, _yGammaTable.Length);
                _referenceDataForStats = new short[_yGammaTable.Length];
                Array.Copy(_yGammaTable, _referenceDataForStats, _yGammaTable.Length);
                MaxY = ComputeMaxYFromData();
                P0Y = 0;
                P3Y = MaxY;
                FitControlPointsFromData(_yGammaTable);
                _initialP1 = new Point(P1X, P1Y);
                _initialP2 = new Point(P2X, P2Y);
                RedrawAll();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error applying default gamma table data ," + ex.Message);
            }
        }

        public short[] GenerateGammaTable(int pointCount)
        {
            short[] result = new short[pointCount];
            Point[] cp = GetControlPoints();

            for (int x = 0; x < pointCount; x++)
            {
                double t = FindTForX(x, cp, MaxX);
                double y = CubicBezierY(t, cp);
                result[x] = (short)Math.Max(0, Math.Min((int)MaxY, Math.Round(y)));
            }

            return result;
        }

        public void LoadGammaTable(short[] data, bool preserveMaxY = false)
        {
            if (data == null || data.Length == 0) return;

            _suppressLayoutUpdate = true;
            try
            {
                _hasImportedData = true;
                int n = Math.Min(data.Length, _yGammaTable.Length);
                for (int i = 0; i < n; i++)
                {
                    _yGammaTable[i] = data[i];
                    _originalGammaTable[i] = data[i];
                }

                if (!preserveMaxY)
                {
                    MaxY = ComputeMaxYFromData();
                }
                for (int i = n; i < _yGammaTable.Length; i++)
                {
                    _yGammaTable[i] = (short)MaxY;
                    _originalGammaTable[i] = (short)MaxY;
                }
                if (_referenceDataForStats == null || _referenceDataForStats.Length != _yGammaTable.Length)
                    _referenceDataForStats = new short[_yGammaTable.Length];
                Array.Copy(_yGammaTable, _referenceDataForStats, _yGammaTable.Length);
                P0Y = 0;
                P3Y = MaxY;
                _initialP1 = new Point(MaxX * 0.25, MaxY * 0.4);
                _initialP2 = new Point(MaxX * 0.70, MaxY * 0.85);
                FitControlPointsFromData(_yGammaTable);
            }
            finally
            {
                _suppressLayoutUpdate = false;
            }
            RedrawAll();
            FireCurveChanged();
        }

        private void UpdateCurveYRangeFromCurrentControlPoints()
        {
            Point[] cp = GetControlPoints();
            double curveYMin = double.MaxValue;
            double curveYMax = double.MinValue;
            int steps = 100;
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double yVal = CubicBezierY(t, cp);
                if (yVal < curveYMin) curveYMin = yVal;
                if (yVal > curveYMax) curveYMax = yVal;
            }
            curveYMin = Math.Max(0, curveYMin);
            curveYMax = Math.Min(MaxY, curveYMax);
            double yMargin = (curveYMax - curveYMin) * 0.05;
            SetValue(CurveYMinPropertyKey, Math.Max(0, curveYMin - yMargin));
            SetValue(CurveYMaxPropertyKey, Math.Min(MaxY, curveYMax + yMargin));
        }

        public void ResetControlPoints()
        {
            _suppressLayoutUpdate = true;
            try
            {
                if (_hasImportedData)
                {
                    Array.Copy(_originalGammaTable, _yGammaTable, _yGammaTable.Length);
                }
                else
                {
                    Array.Copy(_defaultGammaTable, _yGammaTable, _yGammaTable.Length);
                    MaxY = ComputeMaxYFromData();
                }

                Array.Copy(_yGammaTable, _referenceDataForStats, _yGammaTable.Length);
                P0Y = 0;
                P3Y = MaxY;
                FitControlPointsFromData(_yGammaTable);
            }
            finally
            {
                _suppressLayoutUpdate = false;
            }

            RedrawAll();
            FireCurveChanged();
        }

        private double ComputeMaxYFromData()
        {
            short maxVal = 0;
            foreach (short v in _yGammaTable)
            {
                if (v > maxVal) maxVal = v;
            }
            double computed = maxVal;
            if (computed > 1023) computed = 1023;
            if (computed < 1) computed = 255;
            return computed;
        }

        public FitStatistics ComputeFitStatistics()
        {
            if (_fitStatsCacheValid)
                return _cachedFitStats;

            Point[] cp = GetControlPoints();
            short[] refData = _referenceDataForStats ?? _yGammaTable;
            int n = refData.Length;

            double meanY = 0;
            for (int i = 0; i < n; i++)
                meanY += refData[i];
            meanY /= n;

            double ssTot = 0;
            double ssRes = 0;
            double maxAbsError = 0;

            for (int i = 0; i < n; i++)
            {
                double dataX = (MaxX * i) / (n - 1);
                double t = FindTForX(dataX, cp, MaxX);
                double predY = CubicBezierY(t, cp);
                double actual = refData[i];

                double diff = actual - predY;
                ssRes += diff * diff;
                double diffMean = actual - meanY;
                ssTot += diffMean * diffMean;

                double absError = Math.Abs(diff);
                if (absError > maxAbsError)
                    maxAbsError = absError;
            }

            double rSquared = ssTot > 0 ? 1.0 - (ssRes / ssTot) : 1.0;
            _cachedFitStats = new FitStatistics { RSquared = rSquared, MaxAbsoluteError = maxAbsError };
            _fitStatsCacheValid = true;
            return _cachedFitStats;
        }

        #endregion
    }

    public struct FitStatistics
    {
        public double RSquared { get; set; }
        public double MaxAbsoluteError { get; set; }
    }
}
*/