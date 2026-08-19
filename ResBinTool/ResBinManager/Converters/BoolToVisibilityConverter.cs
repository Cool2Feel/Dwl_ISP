using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ResBinManager.Converters
{
    /// <summary>
    /// 布尔值到可见性转换器
    /// true → Visible, false → Collapsed (或 Hidden)
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// 当值为 false 时，是否使用 Hidden 而不是 Collapsed
        /// </summary>
        public bool UseHidden { get; set; } = false;

        /// <summary>
        /// 是否反转逻辑（true → Collapsed, false → Visible）
        /// </summary>
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // 检查参数是否需要反转
                bool invert = Invert;
                if (parameter is string param && param.Equals("Invert", StringComparison.OrdinalIgnoreCase))
                    invert = !invert;

                // 如果需要反转，则取反
                if (invert)
                    boolValue = !boolValue;

                // 根据布尔值返回可见性
                if (boolValue)
                    return Visibility.Visible;
                else
                    return UseHidden ? Visibility.Hidden : Visibility.Collapsed;
            }

            // 默认返回 Collapsed
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool isVisible = (visibility == Visibility.Visible);
                return Invert ? !isVisible : isVisible;
            }

            return false;
        }
    }
}
