using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ThunderSE.Ui.MainWindow
{
    /// <summary>
    /// FormatDataWindow.xaml 的交互逻辑
    /// </summary>
    public partial class FormatDataWindow : Window
    {
        private string _originalData;
        private string _formattedData;
        public FormatDataWindow(string data)
        {
            InitializeComponent();
            _originalData = data;
            _formattedData = FormatWithBraceAlignment(data);

            SetupKeyboardShortcuts();
        }

        #region 窗口事件处理

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            InitializeUIState();
            UpdateStatus("数据加载完成");
        }

        private void InitializeUIState()
        {
            if (!string.IsNullOrEmpty(_originalData))
            {
                contentBox.Text = _formattedData;
                UpdateDataStatistics();
                UpdateDescription("C结构体格式数据预览 - 已加载");
            }
            else
            {
                contentBox.Text = "";
                TxtEmptyHint.Visibility = Visibility.Visible;
                UpdateDescription("暂无数据");
                UpdateStatus("无数据内容", isWarning: true);
            }
            bool ch = Properties.Settings.Default.AutoWrap;
            if (ch)
            {
                contentBox.TextWrapping = TextWrapping.Wrap;
                ChkWordWrap.IsChecked = true;

            }
            else
            {
                contentBox.TextWrapping = TextWrapping.NoWrap;
                ChkWordWrap.IsChecked = false;
            }
        }

        #endregion

        #region 状态更新方法

        private void UpdateStatus(string message, bool isError = false, bool isWarning = false, bool isSuccess = false)
        {
            StatusText.Text = message;

            if (isError)
            {
                StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 0, 0));
                StatusIcon.Text = "●";
            }
            else if (isWarning)
            {
                StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0));
                StatusIcon.Text = "⚠";
            }
            else if (isSuccess)
            {
                StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 0));
                StatusIcon.Text = "✓";
            }
            else
            {
                StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 0));
                StatusIcon.Text = "●";
            }
        }

        private void UpdateDescription(string description)
        {
            TxtDescription.Text = description;
        }

        private void UpdateInfoTip(string tip)
        {
            TxtInfoTip.Text = tip;
        }

        private void UpdateDataStatistics()
        {
            if (contentBox != null && !string.IsNullOrEmpty(contentBox.Text))
            {
                int charCount = contentBox.Text.Length;
                int lineCount = contentBox.Text.Split('\n').Length;

                TxtDataStats.Text = $"字符数: {charCount:N0} | 行数: {lineCount}";
            }
            else
            {
                TxtDataStats.Text = "字符数: 0 | 行数: 0";
            }
        }

        #endregion

        #region 按钮事件处理

        private void OnClickCancel(object sender, RoutedEventArgs e)
        {
            UpdateStatus("操作已取消", isWarning: true);
            DialogResult = false;
            Close();
        }

        private void OnClickConfirm(object sender, RoutedEventArgs e)
        {
            CopyToClipboard();
            UpdateStatus("数据已复制到剪贴板 ✓", isSuccess: true);
            DialogResult = true;
            Close();
        }

        private void OnClickSelectAll(object sender, RoutedEventArgs e)
        {
            if (contentBox != null)
            {
                contentBox.Focus();
                contentBox.SelectAll();
                UpdateStatus("已全选文本");
            }
        }

        private void OnClickCopyQuick(object sender, RoutedEventArgs e)
        {
            CopyToClipboard();
            UpdateStatus("已复制到剪贴板 ✓", isSuccess: true);
        }

        #endregion

        #region 工具栏事件处理

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateDataStatistics();

            if (!string.IsNullOrEmpty(contentBox?.Text))
            {
                TxtEmptyHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtEmptyHint.Visibility = Visibility.Visible;
            }
        }

        private void OnWordWrapChecked(object sender, RoutedEventArgs e)
        {
            if (contentBox != null)
            {
                contentBox.TextWrapping = TextWrapping.Wrap;
                Properties.Settings.Default.AutoWrap = true;
                contentBox.Text = _formattedData;
                UpdateDataStatistics();
                Properties.Settings.Default.Save();
                UpdateStatus("自动换行：开启");
            }
        }

        private void OnWordWrapUnchecked(object sender, RoutedEventArgs e)
        {
            if (contentBox != null)
            {
                contentBox.TextWrapping = TextWrapping.NoWrap;
                Properties.Settings.Default.AutoWrap = false;
                contentBox.Text = _originalData;
                UpdateDataStatistics();
                Properties.Settings.Default.Save();
                UpdateStatus("自动换行：关闭");
            }
        }

        private void OnFontSizeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (contentBox != null && CboFontSize != null)
            {
                int fontSize;
                switch (CboFontSize.SelectedIndex)
                {
                    case 0:
                        fontSize = 11;
                        break;
                    case 1:
                        fontSize = 13;
                        break;
                    case 2:
                        fontSize = 15;
                        break;
                    case 3:
                        fontSize = 18;
                        break;
                    default:
                        fontSize = 13;
                        break;
                }

                contentBox.FontSize = fontSize;
                UpdateStatus($"字体大小已调整为 {fontSize}px");
            }
        }

        #endregion

        #region 辅助方法


        private string FormatWithBraceAlignment(string data)
        {
            if (string.IsNullOrEmpty(data)) return data;

            if (data.StartsWith("const _Sensor_Adpt"))
            {
                return FormatSensorAdpt(data);
            }
            else if (data.StartsWith(".gain_levl") || data.StartsWith(".blc_adapt") || data.StartsWith(".ygama_adapt") || data.StartsWith(".saj_adapt"))
            {
                return data;
            }
            else if (data.StartsWith(".ae_adapt"))
            {
                return FormatAE(data, ".ae_adapt");
            }
            else if (data.StartsWith(".ddc_adapt"))
            {
                return FormatDDC(data, ".ddc_adapt");
            }
            else if (data.StartsWith(".awb_adapt"))
            {
                return FormatAWB(data, ".awb_adapt");
            }
            else if (data.StartsWith(".ccm_adapt"))
            {
                return FormatCCM(data, ".ccm_adapt");
            }
            else if (data.StartsWith(".ch_adapt"))
            {
                return FormatCH(data, ".ch_adapt");
            }
            else if (data.StartsWith(".ee_adapt"))
            {
                return FormatEE(data, ".ee_adapt");
            }

            //if (fieldPrefix.Contains("ae_adapt")) return FormatAE(data, fieldPrefix);
            //if (fieldPrefix.Contains("ddc_adapt")) return FormatDDC(data, fieldPrefix);
            //if (fieldPrefix.Contains("awb_adapt")) return FormatAWB(data, fieldPrefix);
            //if (fieldPrefix.Contains("ccm_adapt")) return FormatCCM(data, fieldPrefix);

            return FormatDefault(data, "");
        }

        /// <summary>
        /// 格式化完整的 _Sensor_Adpt 结构体，对其中的各 _adapt 字段应用对应的模板化显示
        /// </summary>
        private string FormatSensorAdpt(string data)
        {
            var sb = new System.Text.StringBuilder();
            int i = 0;

            while (i < data.Length)
            {
                // 检测各 _adapt 字段起始位置，分派到对应格式化器
                if (i + 10 <= data.Length && data.Substring(i, 10) == ".ae_adapt ")
                {
                    int fieldEnd = FindFieldEnd(data, i);
                    string fieldContent = data.Substring(i, fieldEnd - i);
                    sb.Append(FormatAE(fieldContent, ".ae_adapt"));
                    i = fieldEnd;
                }
                else if (i + 11 <= data.Length && data.Substring(i, 11) == ".ddc_adapt ")
                {
                    int fieldEnd = FindFieldEnd(data, i);
                    string fieldContent = data.Substring(i, fieldEnd - i);
                    sb.Append(FormatDDC(fieldContent, ".ddc_adapt"));
                    i = fieldEnd;
                }
                else if (i + 11 <= data.Length && data.Substring(i, 11) == ".awb_adapt ")
                {
                    int fieldEnd = FindFieldEnd(data, i);
                    string fieldContent = data.Substring(i, fieldEnd - i);
                    sb.Append(FormatAWB(fieldContent, ".awb_adapt"));
                    i = fieldEnd;
                }
                else if (i + 11 <= data.Length && data.Substring(i, 11) == ".ccm_adapt ")
                {
                    int fieldEnd = FindFieldEnd(data, i);
                    string fieldContent = data.Substring(i, fieldEnd - i);
                    sb.Append(FormatCCM(fieldContent, ".ccm_adapt"));
                    i = fieldEnd;
                }
                else if (i + 10 <= data.Length && data.Substring(i, 10) == ".ch_adapt ")
                {
                    int fieldEnd = FindFieldEnd(data, i);
                    string fieldContent = data.Substring(i, fieldEnd - i);
                    sb.Append(FormatCH(fieldContent, ".ch_adapt"));
                    i = fieldEnd;
                }
                else if (i + 10 <= data.Length && data.Substring(i, 10) == ".ee_adapt ")
                {
                    int fieldEnd = FindFieldEnd(data, i);
                    string fieldContent = data.Substring(i, fieldEnd - i);
                    sb.Append(FormatEE(fieldContent, ".ee_adapt"));
                    i = fieldEnd;
                }
                else
                {
                    sb.Append(data[i]);
                    i++;
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 从指定位置开始查找字段结束位置（下一个 .xxx_adapt 或结构体结束之前）
        /// </summary>
        private int FindFieldEnd(string data, int startPos)
        {
            int searchPos = startPos + 1;
            while (searchPos < data.Length - 1)
            {
                if (data[searchPos] == '\n' && searchPos + 5 < data.Length)
                {
                    int afterNewline = searchPos + 1;
                    while (afterNewline < data.Length && (data[afterNewline] == ' ' || data[afterNewline] == '\t'))
                        afterNewline++;
                    if (afterNewline < data.Length && data[afterNewline] == '.')
                    {
                        string rest = data.Substring(afterNewline);
                        if (rest.StartsWith(".ae_adapt") || rest.StartsWith(".ddc_adapt") ||
                            rest.StartsWith(".awb_adapt") || rest.StartsWith(".ccm_adapt") ||
                            rest.StartsWith(".ch_adapt") || rest.StartsWith(".ee_adapt") ||
                            rest.StartsWith(".gain_levl") || rest.StartsWith(".af_adapt") ||
                            rest.StartsWith(".blc_adapt") || rest.StartsWith(".ygama_adapt") ||
                            rest.StartsWith(".rgbdgain_adapt") || rest.StartsWith(".vde_adapt") ||
                            rest.StartsWith(".cfd_adapt") || rest.StartsWith(".saj_adapt") ||
                            rest.StartsWith(".p_fun_adapt") || rest.StartsWith(".isp_all_mod") ||
                            rest.StartsWith(".itf") || rest.StartsWith(".typ") || rest.StartsWith(".pixelw") ||
                            rest.StartsWith(".pixelh") || rest.StartsWith(".hsyn") || rest.StartsWith(".vsyn") ||
                            rest.StartsWith(".colrarray") || rest.StartsWith(".AVDD") || rest.StartsWith(".DVDD") ||
                            rest.StartsWith(".VDDIO") || rest.StartsWith(".rotate_adapt") ||
                            rest.StartsWith(".hvb_adapt") || rest.StartsWith(".mclk") ||
                            rest.StartsWith(".pclk_fir_en") || rest.StartsWith(".pclk_fir_class") ||
                            rest.StartsWith(".pclk_inv_en") || rest.StartsWith(".csi_tun"))
                        {
                            return searchPos;
                        }
                    }
                    if (afterNewline < data.Length && data[afterNewline] == '}')
                    {
                        return searchPos;
                    }
                }
                searchPos++;
            }
            return data.Length;
        }


        private int CalcItemsPerLine(int totalItems)
        {
            if (totalItems == 9) return 3;
            return 32;
        }


        #region AE 模板 — struct紧凑模式
        private string FormatAE(string data, string prefix)
        {
            var sb = new System.Text.StringBuilder();
            int i = 0;
            while (i < data.Length)
            {
                if (data[i] == '.' && data[i + 1] == 'h')
                {
                    sb.Append('\n');
                    sb.Append("\t");
                    AppendIndent(sb, 5);
                    sb.Append(data[i]);
                    i++;
                    continue;
                }
                else
                {
                    sb.Append(data[i]);
                    i++;
                }
            }
            return sb.ToString().TrimEnd();
        }
        #endregion

        #region DDC 模板 — 数据块分行模式
        private string FormatDDC(string data, string prefix)
        {
            var sb = new System.Text.StringBuilder();
            int i = 0;
            int _count = 0;
            while (i < data.Length)
            {
                if (i > 0 && data[i - 1] == '}' && data[i] == ',')
                {
                    if (_count == 1)
                    {
                        sb.Append(data[i]);
                        sb.Append('\n');
                        sb.Append("\t");
                    }
                    else
                    {
                        sb.Append(data[i]);
                    }
                    _count++;
                }
                else if (i > 0 && data[i - 1] == ')' && data[i] == ',')
                {
                    if (_count == 5)
                    {
                        sb.Append(data[i]);
                        sb.Append('\n');
                        sb.Append("\t");
                    }
                    else
                    {
                        sb.Append(data[i]);
                    }
                    _count++;
                }
                else
                {
                    sb.Append(data[i]);
                }
                i++;
            }
            return sb.ToString().TrimEnd();
        }
        #endregion

        #region AWB 模板 — 大数组32/行模式
        private string FormatAWB(string data, string prefix)
        {
            var sb = new System.Text.StringBuilder();
            int i = 0;
            bool _start = false;
            int _count = 0;
            while (i < data.Length)
            {
                if ((i + 1) < data.Length && data[i] == ',' && data[i + 1] == '{')
                {
                    sb.Append(data[i]);
                    sb.Append('\n');
                    sb.Append("\t");
                    AppendIndent(sb, 6);
                }
                else if (i > 0 && data[i - 1] == ',' && data[i] == '{')
                {
                    sb.Append(data[i]);
                    sb.Append('\n');
                    _start = true;
                }
                else if ((i + 1) < data.Length && data[i] == '}' && data[i + 1] == '}')
                {
                    sb.Append('\n');
                    sb.Append("\t");
                    AppendIndent(sb, 6);
                    sb.Append(data[i]);
                }
                else if (i > 0 && data[i] == ',')
                {
                    if (_start)
                        _count++;
                    if (_count == 32)
                    {
                        sb.Append(data[i]);
                        sb.Append('\n');
                        _count = 0;
                    }
                    else
                    {
                        sb.Append(data[i]);
                    }
                }
                else
                {
                    sb.Append(data[i]);
                }
                i++;
                Console.WriteLine(i);
            }
            return sb.ToString().TrimEnd();
        }
        #endregion

        #region CCM 模板 — 3x3矩阵每行3个
        private string FormatCCM(string data, string prefix)
        {
            var sb = new System.Text.StringBuilder();

            int i = 0;
            int _count = 0;
            while (i < data.Length)
            {
                if (i > 0 && data[i - 1] == '}' && data[i] == ',')
                {
                    sb.Append(data[i]);
                    sb.Append('\n');
                    sb.Append("\t\t");
                    _count = 0;
                }
                else if (data[i] == ',' && i < data.Length - 1)
                {
                    _count++;
                    if (_count == 3)
                    {
                        sb.Append(data[i]);
                        sb.Append('\n');
                        sb.Append("\t\t");
                        _count = 0;
                    }
                    else
                    {
                        sb.Append(data[i]);
                    }
                }
                else
                {
                    sb.Append(data[i]);
                }
                i++;
            }
            return sb.ToString().TrimEnd();
        }

        private string FormatCH(string data, string prefix)
        {
            var sb = new System.Text.StringBuilder();

            int i = 0;
            int _count = 0;
            while (i < data.Length)
            {
                if (i > 0 && data[i - 1] == '}' && data[i] == ',')
                {
                    if (_count < 3)
                    {
                        sb.Append(data[i]);
                        sb.Append('\n');
                        sb.Append("\t\t");
                        AppendIndent(sb, 2);
                    }
                    else if (_count == 3)
                    {
                        sb.Append(data[i]);
                        sb.Append('\n');
                        sb.Append("\t");
                        AppendIndent(sb, 5);
                    }
                    else
                        sb.Append(data[i]);
                    _count++;
                }
                else
                {
                    sb.Append(data[i]);
                }
                i++;
            }
            return sb.ToString().TrimEnd();
        }

        private string FormatEE(string data, string prefix)
        {
            var sb = new System.Text.StringBuilder();

            int i = 0;
            int _count = 0;
            while (i < data.Length)
            {
                if (i > 0 && data[i - 1] == '}' && data[i] == ',')
                {
                    if (_count == 3)
                    {
                        sb.Append(data[i]);
                        sb.Append('\n');
                        sb.Append("\t");
                        AppendIndent(sb, 5);
                    }
                    else
                        sb.Append(data[i]);
                    _count++;
                }
                else
                {
                    sb.Append(data[i]);
                }
                i++;
            }
            return sb.ToString().TrimEnd();
        }

        #endregion

        #region 默认模板
        private string FormatDefault(string data, string prefix)
        {
            var sb = new System.Text.StringBuilder();
            int indent = 4;
            int itemCountInLine = 0;
            bool isNewLine = true;
            bool inBraceBlock = false;
            char lastOutputChar = '\0';
            for (int i = 0; i < data.Length; i++)
            {
                char c = data[i];
                if (c == '{')
                {
                    if ((inBraceBlock && !isNewLine) || lastOutputChar == '}')
                    {
                        sb.Append('\n'); AppendIndent(sb, indent);
                    }
                    sb.Append(c); lastOutputChar = c;
                    indent += 4;
                    sb.Append('\n'); AppendIndent(sb, indent);
                    itemCountInLine = 0; isNewLine = true; inBraceBlock = true;
                }
                else if (c == '}')
                {
                    indent = Math.Max(0, indent - 4);
                    if (!isNewLine) sb.Append('\n');
                    AppendIndent(sb, indent);
                    sb.Append(c); lastOutputChar = c;
                    itemCountInLine = 0; isNewLine = true;
                    if (indent == 0) inBraceBlock = false;
                }
                else if (c == ',' && inBraceBlock)
                {
                    sb.Append(c); lastOutputChar = c;
                    itemCountInLine++;
                    int nextNonSpace = i + 1;
                    while (nextNonSpace < data.Length && IsWhitespace(data[nextNonSpace])) nextNonSpace++;
                    bool isFieldSeparator = nextNonSpace < data.Length && data[nextNonSpace] == '.';
                    if (isFieldSeparator || itemCountInLine >= 32)
                    {
                        sb.Append('\n'); AppendIndent(sb, indent);
                        itemCountInLine = 0; isNewLine = true;
                    }
                }
                else if (IsWhitespace(c)) { if (!isNewLine || c != ' ') sb.Append(c); }
                else { sb.Append(c); isNewLine = false; }
            }
            return sb.ToString().TrimEnd();
        }
        #endregion

        private int CountItemsInBrace(string data, int braceStart)
        {
            int depth = 0;
            int count = 0;
            for (int j = braceStart; j < data.Length; j++)
            {
                if (data[j] == '{') depth++;
                else if (data[j] == '}') { depth--; if (depth == 0) break; }
                else if (depth == 1 && data[j] == ',') count++;
            }
            return count + 1;
        }

        private void SkipWhitespace(string data, ref int i)
        {
            while (i < data.Length && IsWhitespace(data[i])) i++;
        }

        private bool IsWhitespace(char c)
        {
            return c == ' ' || c == '\t' || c == '\r' || c == '\n';
        }

        private void AppendIndent(System.Text.StringBuilder sb, int indent)
        {
            for (int i = 0; i < indent; i++)
                sb.Append(' ');
        }

        private void CopyToClipboard()
        {
            try
            {
                if (!string.IsNullOrEmpty(contentBox?.Text))
                {
                    Clipboard.SetText(contentBox.Text);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制失败: {ex.Message}", "错误",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("复制失败", isError: true);
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
                else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    OnClickSelectAll(null, null);
                    e.Handled = true;
                }
                else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    OnClickCopyQuick(null, null);
                    e.Handled = true;
                }
            };
        }

        #endregion
    }
}
