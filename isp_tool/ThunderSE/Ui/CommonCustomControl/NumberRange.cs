using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace ThunderSE.Ui.CommonCustomControl
{
    class Wrapper : DependencyObject
    {
        public static readonly DependencyProperty MaxValueProperty =
             DependencyProperty.Register("MaxValue", typeof(int),
             typeof(Wrapper), new FrameworkPropertyMetadata(int.MaxValue));

        public int MaxValue
        {
            get { return (int)GetValue(MaxValueProperty); }
            set { SetValue(MaxValueProperty, value); }
        }

        public static readonly DependencyProperty MinValueProperty =
             DependencyProperty.Register("MinValue", typeof(int),
             typeof(Wrapper), new FrameworkPropertyMetadata(0));

        public int MinValue
        {
            get { return (int)GetValue(MinValueProperty); }
            set { SetValue(MinValueProperty, value); }
        }
    }

    class BindingProxy : System.Windows.Freezable
    {
        protected override Freezable CreateInstanceCore()
        {
            return new BindingProxy();
        }

        public object Data
        {
            get { return (object)GetValue(DataProperty); }
            set { SetValue(DataProperty, value); }
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register("Data", typeof(object), typeof(BindingProxy), new PropertyMetadata(null));
    }

    class NumberRange : ValidationRule
    {
        public Wrapper Wrapper { get; set; }

        public override ValidationResult Validate(object value, System.Globalization.CultureInfo cultureInfo)
        {
            if (value is string && ((string)value).Length == 0)
            {
                return ValidationResult.ValidResult;
            }

            int numVal = -1;
            if (!int.TryParse(value.ToString(), out numVal))
            {
                return new ValidationResult(false, "不是有效值");
            }
            if (numVal > Wrapper.MaxValue || numVal < Wrapper.MinValue)
            {
                return new ValidationResult(false, string.Format("值范围:{0}到{1}", Wrapper.MinValue, Wrapper.MaxValue));
            }
            return ValidationResult.ValidResult;
        }
    }
}
