using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace ThunderSE.Ui.SettingWindow.Awb.CustomControls
{
    /// <summary>
    /// Inherit Thumb control to be able to update a Point dependency property while the thumb
    /// is being dragged.
    /// </summary>
    /// 
    public class ThumbPoint : Thumb
    {
        #region Point
        /// <summary>
        /// Point Dependency Property
        /// </summary>
        public static readonly DependencyProperty PointProperty = DependencyProperty.Register(
            "Point", 
            typeof(Point), 
            typeof(ThumbPoint),
            new FrameworkPropertyMetadata(new Point()));

        /// <summary>
        /// Gets or sets the Point property
        /// </summary>
        public Point Point
        {
            get { return (Point)GetValue(PointProperty); }
            set { SetValue(PointProperty, value); }
        }

        public static readonly DependencyProperty LockDragHorizontallyProperty = DependencyProperty.Register(
            "LockDragHorizontally",
            typeof(bool),
            typeof(ThumbPoint),
            new FrameworkPropertyMetadata(false));

        public bool LockDragHorizontally
        {
            get { return (bool)GetValue(LockDragHorizontallyProperty); }
            set { SetValue(LockDragHorizontallyProperty, value); }
        }

        public static readonly DependencyProperty LockDragVerticallyProperty = DependencyProperty.Register(
            "LockDragVertically",
            typeof(bool),
            typeof(ThumbPoint),
            new FrameworkPropertyMetadata(null));

        public bool LockDragVertically
        {
            get { return (bool)GetValue(LockDragVerticallyProperty); }
            set { SetValue(LockDragVerticallyProperty, value); }
        }

        public static readonly DependencyProperty MaxXProperty = DependencyProperty.Register(
            "MaxX",
            typeof(int?),
            typeof(ThumbPoint),
            new FrameworkPropertyMetadata(null, MaxMinPropertyChangedCallback, CoerceMaxMinValueCallback));

        public int? MaxX
        {
            get { return (int?)GetValue(MaxXProperty); }
            set 
            {
                if (GetValue(MaxXProperty) == null)
                {
                    SetValue(MaxXProperty, value);
                }
            }
        }

        public static readonly DependencyProperty MaxYProperty = DependencyProperty.Register(
            "MaxY",
            typeof(int?),
            typeof(ThumbPoint),
            new FrameworkPropertyMetadata(null, MaxMinPropertyChangedCallback, CoerceMaxMinValueCallback));

        public int? MaxY
        {
            get { return (int?)GetValue(MaxYProperty); }
            set
            {
                if (GetValue(MaxYProperty) == null)
                {
                    SetValue(MaxYProperty, value);
                }
            }
        }

        public static readonly DependencyProperty MinXProperty = DependencyProperty.Register(
            "MinX",
            typeof(int?),
            typeof(ThumbPoint),
            new FrameworkPropertyMetadata(null, MaxMinPropertyChangedCallback, CoerceMaxMinValueCallback));

        public int? MinX
        {
            get { return (int?)GetValue(MinXProperty); }
            set
            {
                if (GetValue(MinXProperty) == null)
                {
                    SetValue(MinXProperty, value);
                }
            }
        }

        public static readonly DependencyProperty MinYProperty = DependencyProperty.Register(
            "MinY",
            typeof(int?),
            typeof(ThumbPoint),
            new FrameworkPropertyMetadata(null, MaxMinPropertyChangedCallback, CoerceMaxMinValueCallback));

        public int? MinY
        {
            get { return (int?)GetValue(MinYProperty); }
            set
            {
                if (GetValue(MinYProperty) == null)
                {
                    SetValue(MinYProperty, value);
                }
            }
        }

        private static object CoerceMaxMinValueCallback(DependencyObject d, object value)
        {
            return value;
        }

        private static void MaxMinPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            d.SetValue(e.Property, e.NewValue);
        }

        #endregion


        static ThumbPoint()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ThumbPoint), new FrameworkPropertyMetadata(typeof(ThumbPoint)));
        }

        public ThumbPoint()
        {
            this.DragDelta += new DragDeltaEventHandler(this.OnDragDelta);
        }

        private void OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            bool lockDragHorizontally = (bool)GetValue(LockDragHorizontallyProperty);
            bool lockDragVertically = (bool)GetValue(LockDragVerticallyProperty);

            var x = lockDragHorizontally ? this.Point.X : this.Point.X + e.HorizontalChange;
            var y = lockDragVertically ? this.Point.Y : this.Point.Y + e.VerticalChange;

            x = MaxX != null && x > MaxX ? (int)MaxX : x;
            y = MaxY != null && y > MaxY ? (int)MaxY : y;

            x = MinX != null && x < MinX ? (int)MinX : x;
            y = MinY != null && y < MinY ? (int)MinY : y;

            this.Point = new Point(x, y);
        }
    }
}
