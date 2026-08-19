using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ThunderSE.Ui.Converter
{
    public class UvcViewVisibilityConverter : IValueConverter
    {

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            bool boolValue = (bool)value;
            if ((string)parameter == "HideButton")
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }

            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            Visibility visiBilityValue = (Visibility)value;
            if ((string)parameter == "HideButton")
            {
                return visiBilityValue == Visibility.Visible ? true : false;
            }

            return visiBilityValue == Visibility.Visible ? false : true;
        }
    }

    /// <summary>
    /// 根据 SetMode 控制 RAW Scale 控件的可见性
    /// RAW8 或 RAW10 时显示，否则隐藏
    /// </summary>
    public class SetModeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is DeviceConfig.Isp.SetMode mode)
            {
                return (mode == DeviceConfig.Isp.SetMode.RAW8 || mode == DeviceConfig.Isp.SetMode.RAW10)
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PlayNavigateAnimationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var navigatingModule = (DeviceConfig.Isp.IspModule?)value;
            var stringParam = (string)parameter;

            DeviceConfig.Isp.IspModule? module = null;

            switch (stringParam)
            {
                case "DeviceConfig.Isp.IspModule.AE":
                    module = DeviceConfig.Isp.IspModule.AE;
                    break;
                case "DeviceConfig.Isp.IspModule.Blc":
                    module = DeviceConfig.Isp.IspModule.Blc;
                    break;

                case "DeviceConfig.Isp.IspModule.Lsc":
                    module = DeviceConfig.Isp.IspModule.Lsc;
                    break;

                case "DeviceConfig.Isp.IspModule.Ddc":
                    module = DeviceConfig.Isp.IspModule.Ddc;
                    break;

                case "DeviceConfig.Isp.IspModule.Awb":
                    module = DeviceConfig.Isp.IspModule.Awb;
                    break;

                case "DeviceConfig.Isp.IspModule.Ccm":
                    module = DeviceConfig.Isp.IspModule.Ccm;
                    break;

                case "DeviceConfig.Isp.IspModule.YGamma":
                    module = DeviceConfig.Isp.IspModule.YGamma;
                    break;

                case "DeviceConfig.Isp.IspModule.Ch":
                    module = DeviceConfig.Isp.IspModule.Ch;
                    break;

                case "DeviceConfig.Isp.IspModule.Ee":
                    module = DeviceConfig.Isp.IspModule.Ee;
                    break;

                case "DeviceConfig.Isp.IspModule.Saj":
                    module = DeviceConfig.Isp.IspModule.Saj;
                    break;

                default:
                    break;
            }

            return navigatingModule == module;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 枚举值到布尔值的转换器，用于 RadioButton 与枚举属性的双向绑定
    /// 用法：将 RadioButton 的 IsChecked 绑定到枚举属性，并通过 ConverterParameter 指定目标枚举值
    /// 示例：IsChecked="{Binding SetMode, Converter={StaticResource enumToBooleanConverter}, ConverterParameter=RAW}"
    /// </summary>
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            // 比较当前值和目标值是否相等
            return value.ToString().Equals(parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Binding.DoNothing;

            // 如果 RadioButton 被选中，则返回目标枚举值
            bool isChecked = (bool)value;
            if (isChecked)
            {
                // 将参数转换为枚举值
                if (targetType.IsEnum)
                {
                    try
                    {
                        return Enum.Parse(targetType, parameter.ToString(), true);
                    }
                    catch
                    {
                        return Binding.DoNothing;
                    }
                }
            }

            return Binding.DoNothing;
        }
    }

    public class IndexToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int selectedIndex && parameter is string param)
            {
                if (int.TryParse(param, out int currentIndex))
                {
                    // 如果当前索引等于选中索引，则返回绿色边框，否则返回默认边框
                    return selectedIndex == currentIndex ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Gray);
                }
            }

            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DecimalToHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "0";

            if (int.TryParse(value.ToString(), out int intValue))
            {
                string format = "X";
                if (parameter != null)
                {
                    format = parameter.ToString();
                }
                return "0x" + intValue.ToString(format);
            }

            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return 0;

            string hexString = value.ToString().Trim();
            if (hexString.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hexString = hexString.Substring(2);
            }

            if (int.TryParse(hexString, NumberStyles.HexNumber, culture, out int intValue))
            {
                return intValue;
            }

            // 如果解析失败，尝试按十进制解析
            if (int.TryParse(value.ToString(), out int decValue))
            {
                return decValue;
            }

            return 0;
        }
    }

    public class HexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ushort ushortValue)
            {
                return "0x" + ushortValue.ToString("X2");
            }
            else if (value is int intValue)
            {
                return "0x" + intValue.ToString("X2");
            }
            else if (value is uint uintValue)
            {
                return "0x" + uintValue.ToString("X2");
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue)
            {
                string trimmed = stringValue.Trim();

                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(2);
                }

                if (trimmed.StartsWith("0X", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("&h", StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(2);
                }

                if (ushort.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, culture, out ushort result))
                {
                    if (targetType == typeof(int)) return (int)result;
                    if (targetType == typeof(uint)) return (uint)result;
                    return result;
                }
            }

            return value;
        }
    }

    public class ModuleEnableStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ThunderSE.DeviceConfig.Isp.CommonConfig config && parameter is string moduleStr)
            {
                if (!Enum.TryParse<ThunderSE.DeviceConfig.Isp.IspModule>(moduleStr, out var module))
                    return "N/A";

                char actualStatus = GetModuleActualStatus(config, module);
                switch (actualStatus)
                {
                    case (char)0x00: return "(OFF)";
                    case (char)0x01: return "(ON)";
                    case (char)0x02: return "(AUTO)";
                    default: return "(OFF)";
                }
            }
            return "N/A";
        }

        private static char GetModuleActualStatus(ThunderSE.DeviceConfig.Isp.CommonConfig config, ThunderSE.DeviceConfig.Isp.IspModule module)
        {
            if ((int)module >= 0 && (int)module < config.ProcessorStepsEnables.Count && config.ProcessorStepsEnables[(int)module].Value == false)
                return (char)0x00;

            if (config.ProcessorStepsEnablesActualValueMap.TryGetValue(module, out char actualValue))
                return actualValue;

            return (char)0x00;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ModuleEnableStatusColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush OnBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly SolidColorBrush AutoBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
        private static readonly SolidColorBrush OffBrush = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ThunderSE.DeviceConfig.Isp.CommonConfig config && parameter is string moduleStr)
            {
                if (!Enum.TryParse<ThunderSE.DeviceConfig.Isp.IspModule>(moduleStr, out var module))
                    return OffBrush;

                if ((int)module >= 0 && (int)module < config.ProcessorStepsEnables.Count && config.ProcessorStepsEnables[(int)module].Value == false)
                    return OffBrush;

                if (config.ProcessorStepsEnablesActualValueMap.TryGetValue(module, out char actualValue))
                {
                    if (actualValue == 0x00) return OffBrush;
                    if (actualValue == 0x02) return AutoBrush;
                    return OnBrush;
                }

                return OffBrush;
            }
            return OffBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
