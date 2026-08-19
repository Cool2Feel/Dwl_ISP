using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace ThunderSE.Ui.MainWindow
{
    /*
    public class ArrayToTextConverter : IValueConverter
    {
        private int _parts = 1;
        private int _numberPerLine = 1;


        public ArrayToTextConverter(int parts = 1, int numberPerLine = 1)
        {
            _parts = parts;
            _numberPerLine = numberPerLine;
        }

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return "";
            
            var dataArray = (int[])value;
            if (dataArray.Length == 0) return "";

            string result = "";

            // 计算每部分的元素数量
            int elementsPerPart = dataArray.Length / _parts;
            // 如果不能整除，则最后一部分可能包含更多元素
            int remainderParts = dataArray.Length % _parts;

            int pos = 0;
            for (int i = 0; i < _parts; i++)
            {
                // 计算当前部分应该有多少个元素
                int currentPartSize = elementsPerPart;
                if (i < remainderParts) // 前几个部分需要多一个元素来分配余数
                {
                    currentPartSize++;
                }

                if (pos >= dataArray.Length) break; // 防止越界

                var part = new int[Math.Min(currentPartSize, dataArray.Length - pos)];
                Array.Copy(dataArray, pos, part, 0, part.Length);
                pos += part.Length;

                // 计算每行的元素数量
                int elementsPerLine = part.Length / _numberPerLine;
                if (part.Length % _numberPerLine != 0) elementsPerLine++; // 处理不能整除的情况

                int pos2 = 0;
                while (pos2 < part.Length)
                {
                    // 确保不会超出part数组边界
                    int copyLength = Math.Min(part.Length / _numberPerLine, part.Length - pos2);
                    if (copyLength <= 0) copyLength = Math.Min(1, part.Length - pos2);

                    var part2 = new int[copyLength];
                    Array.Copy(part, pos2, part2, 0, copyLength);
                    result += String.Join(",", new List<int>(part2).ConvertAll(_i => _i.ToString()).ToArray());
                    result += ",\r\n";

                    pos2 += copyLength;
                }

                result += "\r\n";
            }

            result = result.TrimEnd(new char[] { ',', '\r', '\n', ' ', '\r', '\n' });
            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var dataString = (string)value;

            dataString = dataString.Replace("\r", string.Empty).Replace("\n", string.Empty);
            var result = dataString.Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).Select(n => System.Convert.ToInt32(n)).ToArray();

            return result;
        }
    }
    */

    //public class ArrayToTextConverter : IValueConverter
    //{
    //    private int _parts = 1;
    //    private int _numberPerLine = 1;
    //    private bool _showAsHex = false;

    //    public ArrayToTextConverter(int parts = 1, int numberPerLine = 1, bool showAsHex = false)
    //    {
    //        _parts = parts;
    //        _numberPerLine = numberPerLine;
    //        _showAsHex = showAsHex;
    //    }

    //    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    //    {
    //        if (value == null) return "";

    //        var dataArray = (int[])value;
    //        if (dataArray.Length == 0) return "";

    //        string result = "";

    //        // 计算每部分的元素数量
    //        int elementsPerPart = dataArray.Length / _parts;
    //        // 如果不能整除，则最后一部分可能包含更多元素
    //        int remainderParts = dataArray.Length % _parts;

    //        int pos = 0;
    //        for (int i = 0; i < _parts; i++)
    //        {
    //            // 计算当前部分应该有多少个元素
    //            int currentPartSize = elementsPerPart;
    //            if (i < remainderParts) // 前几个部分需要多一个元素来分配余数
    //            {
    //                currentPartSize++;
    //            }

    //            if (pos >= dataArray.Length) break; // 防止越界

    //            var part = new int[Math.Min(currentPartSize, dataArray.Length - pos)];
    //            Array.Copy(dataArray, pos, part, 0, part.Length);
    //            pos += part.Length;

    //            // 计算每行的元素数量
    //            int elementsPerLine = part.Length / _numberPerLine;
    //            if (part.Length % _numberPerLine != 0) elementsPerLine++; // 处理不能整除的情况

    //            int pos2 = 0;
    //            while (pos2 < part.Length)
    //            {
    //                // 确保不会超出part数组边界
    //                int copyLength = Math.Min(part.Length / _numberPerLine, part.Length - pos2);
    //                if (copyLength <= 0) copyLength = Math.Min(1, part.Length - pos2);

    //                var part2 = new int[copyLength];
    //                Array.Copy(part, pos2, part2, 0, copyLength);

    //                // 根据_showAsHex标志决定显示格式
    //                string[] formattedValues = new string[part2.Length];
    //                for (int j = 0; j < part2.Length; j++)
    //                {
    //                    formattedValues[j] = _showAsHex ? "0x" + ((uint)part2[j]).ToString("X") : part2[j].ToString();
    //                }

    //                result += String.Join(",", new List<string>(formattedValues).ToArray());
    //                result += ",\r\n";

    //                pos2 += copyLength;
    //            }

    //            result += "\r\n";
    //        }

    //        result = result.TrimEnd(new char[] { ',', '\r', '\n', ' ', '\r', '\n' });
    //        return result;
    //    }

    //    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    //    {
    //        var dataString = (string)value;

    //        dataString = dataString.Replace("\r", string.Empty).Replace("\n", string.Empty);

    //        // 使用正则表达式处理十六进制和十进制数字
    //        var regex = new System.Text.RegularExpressions.Regex(@"0x[0-9A-Fa-f]+|-?\d+");
    //        var matches = regex.Matches(dataString);

    //        var result = new int[matches.Count];
    //        for (int i = 0; i < matches.Count; i++)
    //        {
    //            string valueStr = matches[i].Value;
    //            if (valueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    //            {
    //                result[i] = unchecked((int)System.Convert.ToUInt32(valueStr.Substring(2), 16));
    //            }
    //            else
    //            {
    //                // 十进制数
    //                result[i] = System.Convert.ToInt32(valueStr);
    //            }
    //        }

    //        return result;
    //    }
    //}

    public class ArrayToTextConverter : IValueConverter
    {
        private int _parts = 1;
        private int _numberPerLine = 1;
        private bool _showAsHex = false;
        private bool _separateParts = false;

        public ArrayToTextConverter(int parts = 1, int numberPerLine = 1, bool showAsHex = false, bool separateParts = false)
        {
            _parts = parts;
            _numberPerLine = numberPerLine;
            _showAsHex = showAsHex;
            _separateParts = separateParts;
        }

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return "";

            var dataArray = (int[])value;
            if (dataArray.Length == 0) return "";

            string result = "";

            int elementsPerPart = dataArray.Length / _parts;
            int remainderParts = dataArray.Length % _parts;

            int pos = 0;

            // 收集每个 part 的格式化行，用 null 标记 part 分隔符
            var allFormattedLines = new System.Collections.Generic.List<string[]>();
            int maxColumns = 0;

            for (int i = 0; i < _parts; i++)
            {
                int currentPartSize = elementsPerPart;
                if (i < remainderParts)
                    currentPartSize++;

                if (pos >= dataArray.Length) break;

                var part = new int[Math.Min(currentPartSize, dataArray.Length - pos)];
                Array.Copy(dataArray, pos, part, 0, part.Length);
                pos += part.Length;

                int elementsPerLine = part.Length / _numberPerLine;
                if (part.Length % _numberPerLine != 0) elementsPerLine++;

                int pos2 = 0;
                while (pos2 < part.Length)
                {
                    int copyLength = Math.Min(part.Length / _numberPerLine, part.Length - pos2);
                    if (copyLength <= 0) copyLength = Math.Min(1, part.Length - pos2);

                    var part2 = new int[copyLength];
                    Array.Copy(part, pos2, part2, 0, copyLength);

                    string[] formattedValues = new string[part2.Length];
                    for (int j = 0; j < part2.Length; j++)
                    {
                        if (_showAsHex)
                            formattedValues[j] = "0x" + ((uint)part2[j]).ToString("X");
                        else
                            formattedValues[j] = part2[j].ToString();
                    }

                    allFormattedLines.Add(formattedValues);
                    if (formattedValues.Length > maxColumns)
                        maxColumns = formattedValues.Length;

                    pos2 += copyLength;
                }

                // 在 part 之间插入分隔标记（最后一个 part 之后不插入）
                if (_separateParts && i < _parts - 1 && pos < dataArray.Length)
                {
                    allFormattedLines.Add(null); // null 标记空行
                }
            }

            int[] colWidths = new int[maxColumns];
            for (int c = 0; c < maxColumns; c++)
            {
                int maxWidth = 0;
                foreach (var line in allFormattedLines)
                {
                    if (line != null && c < line.Length && line[c].Length > maxWidth)
                        maxWidth = line[c].Length;
                }
                colWidths[c] = maxWidth;
            }

            foreach (var line in allFormattedLines)
            {
                if (line == null)
                {
                    // 空行分隔
                    result += "\r\n";
                    continue;
                }

                for (int j = 0; j < line.Length; j++)
                {
                    if (j > 0) result += ",";
                    result += line[j].PadLeft(colWidths[j]);
                }
                result += ",\r\n";
            }

            result = result.TrimEnd(new char[] { ',', '\r', '\n', ' ' });
            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var dataString = (string)value;

            dataString = dataString.Replace("\r", string.Empty).Replace("\n", string.Empty);

            // 使用正则表达式处理十六进制和十进制数字
            var regex = new System.Text.RegularExpressions.Regex(@"0x[0-9A-Fa-f]+|-?\d+");
            var matches = regex.Matches(dataString);

            var result = new int[matches.Count];
            for (int i = 0; i < matches.Count; i++)
            {
                string valueStr = matches[i].Value;
                if (valueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    result[i] = unchecked((int)System.Convert.ToUInt32(valueStr.Substring(2), 16));
                }
                else
                {
                    // 十进制数
                    result[i] = System.Convert.ToInt32(valueStr);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// ArrayDataWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ArrayDataWindow : Window
    {
        private int _parts = 1;
        private int _numberPerLine = 1;
        private bool _showAsHex = false;
        private bool _separateParts = false;

        public ArrayDataWindow(int[] arrayData, int parts = 1, int numberPerLine = 1, bool showAsHex = false, bool separateParts = false)
        {
            _parts = parts;
            _numberPerLine = numberPerLine;
            _showAsHex = showAsHex;
            _separateParts = separateParts;

            ArrayData = arrayData;
            InitializeComponent();

            SetupKeyboardShortcuts();
        }

        public int[] ArrayData
        {
            get;
            set;
        }

        private void OnClickConfirm(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnClickCancel(object sender, RoutedEventArgs e)
        {
            UpdateStatus("操作已取消");
            DialogResult = false;
            Close();
        }

        private bool ValidateData()
        {
            try
            {
                if (ArrayData == null || ArrayData.Length == 0) return true;

                foreach (var item in ArrayData)
                {
                    if (item < int.MinValue || item > int.MaxValue) return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SetupKeyboardShortcuts()
        {
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
                {
                    OnClickConfirm(null, null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    OnClickCancel(null, null);
                    e.Handled = true;
                }
            };
        }

        private void UpdateStatus(string message)
        {
            if (StatusText != null)
            {
                StatusText.Text = message;
            }
        }

        private void UpdateDataInfo()
        {
            if (DataInfoText != null && ArrayData != null)
            {
                DataInfoText.Text = $"共 {ArrayData.Length} 个元素";
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var binding = new Binding("ArrayData")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1),
                Converter = new ArrayToTextConverter(_parts, _numberPerLine, _showAsHex, _separateParts),
                Mode = BindingMode.TwoWay
            };
            contentBox.SetBinding(TextBox.TextProperty, binding);

            UpdateDataInfo();
            UpdateStatus("数据加载完成");
        }
    }
}