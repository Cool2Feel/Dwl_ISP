using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace ThunderSE.Ui.MainWindow.UserMode
{
    class DataArrayToControlCollectionConverter : IValueConverter
    {
        public byte[] DataArray
        {
            get;
            set;
        }

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            // 1. 安全解析索引 (修复 XAML 传 string 导致的崩溃)
            int index = -1;
            if (parameter != null)
            {
                int.TryParse(parameter.ToString(), out index);
            }

            if (index < 0 || value == null)
                return DependencyProperty.UnsetValue;

            // 2. 安全赋值给全局变量 (修复你最开始提问的 int[] 转 byte[] 崩溃)
            if (value is byte[] byteArray)
            {
                DataArray = byteArray;
            }
            else if (value is int[] intArray)
            {
                // 如果外部传入的是 int[]，将其转换拷贝为 byte[] 以适配你的全局变量
                DataArray = new byte[intArray.Length];
                for (int i = 0; i < intArray.Length; i++)
                {
                    DataArray[i] = (byte)intArray[i];
                }
            }
            else
            {
                return DependencyProperty.UnsetValue;
            }

            // 3. 越界检查 (防止索引超出数组长度导致闪退)
            if (index >= DataArray.Length)
                return DependencyProperty.UnsetValue;

            // 4. 正常的类型转换
            if (targetType == typeof(double))
            {
                return (double)DataArray[index];
            }
            else if (targetType == typeof(bool?))
            {
                return System.Convert.ToBoolean(DataArray[index]);
            }
            else if (targetType == typeof(string))
            {
                return DataArray[index].ToString();
            }

            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            int index = -1;
            if (parameter != null)
            {
                int.TryParse(parameter.ToString(), out index);
            }

            // 安全检查：如果全局变量没赋值，或者索引越界，直接中断，不执行写回
            if (index < 0 || DataArray == null || index >= DataArray.Length)
                return DependencyProperty.UnsetValue;

            // 1. 安全提取 UI 传回来的值
            double sliderValue = 0;
            if (value is double d)
            {
                sliderValue = d;
            }
            else if (value is bool b)
            {
                sliderValue = b ? 1 : 0;
            }
            else if (value is string s)
            {
                double.TryParse(s, out sliderValue); // 去掉 try-catch，提高性能
            }
            else if (value != null)
            {
                sliderValue = (double)value;
            }

            // 2. 兼容老版本 .NET 的防溢出处理 (代替 Math.Clamp)
            // 确保值在 0 - 255 之间，防止 Slider 拖出范围变成乱码
            byte byteValue = (byte)Math.Min(Math.Max(sliderValue, byte.MinValue), byte.MaxValue);

            // 3. 写入全局变量并返回
            DataArray[index] = byteValue;
            return DataArray;
        }

        /*
        public object Convert(object value, Type targetType, object parameter, 
            System.Globalization.CultureInfo culture)
        {
            DataArray = (byte[])value;

            if (targetType == typeof(double))
            {
                return (double)DataArray[(int)parameter];
            }
            else if (targetType == typeof(bool?))
            {
                return System.Convert.ToBoolean(DataArray[(int)parameter]);
            }
            else if (targetType == typeof(string))
            {
                return DataArray[(int)parameter].ToString();
            }
            else
            {
                throw new Exception();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, 
            System.Globalization.CultureInfo culture)
        {
            double sliderValue;
            if (value as string != null)
            {
                try
                {
                    sliderValue = System.Convert.ToDouble((string)value);
                }
                catch (Exception)
                {
                    sliderValue = 0;
                }
            }
            else if (value as bool? != null)
            {
                sliderValue = System.Convert.ToInt32((bool)value);
            }
            else
            {
                sliderValue = (double)value;
            }
            DataArray[(int)parameter] = (byte)sliderValue;

            return DataArray;
        }

        */
    }
}
