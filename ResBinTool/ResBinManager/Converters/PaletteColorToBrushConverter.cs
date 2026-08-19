using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ResBinManager.Core;

namespace ResBinManager.Converters
{
    public class PaletteColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PaletteColor color)
            {
                return color.Brush;
            }
            
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
