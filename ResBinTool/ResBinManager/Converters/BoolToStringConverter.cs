using System;
using System.Globalization;
using System.Windows.Data;

namespace ResBinManager.Converters
{
    /// <summary>
    /// 布尔值到字符串的转换器，用于显示模式等信息
    /// </summary>
    public class BoolToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is string str && str.IndexOf('|') >= 0)
            {
                var parts = str.Split('|');
                if (parts.Length == 2)
                {
                    return (bool)value ? parts[0] : parts[1];
                }
            }
            
            return (bool)value ? "True" : "False";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// 布尔值到颜色的转换器
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is string str && str.IndexOf('|') >= 0)
            {
                var parts = str.Split('|');
                if (parts.Length == 2)
                {
                    // 简单的颜色名称解析
                    var colorStr = (bool)value ? parts[0] : parts[1];
                    
                    // 返回画笔
                    switch (colorStr.ToLower())
                    {
                        case "green":
                            return System.Windows.Media.Brushes.Green;
                        case "blue":
                            return System.Windows.Media.Brushes.Blue;
                        case "red":
                            return System.Windows.Media.Brushes.Red;
                        case "orange":
                            return System.Windows.Media.Brushes.Orange;
                        default:
                            return System.Windows.Media.Brushes.Black;
                    }
                }
            }
            
            return System.Windows.Media.Brushes.Black;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
