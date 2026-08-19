using System;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using ResBinManager.Core;

namespace ResBinManager.Views
{
    /// <summary>
    /// Font 替换对话框
    /// </summary>
    public partial class FontReplaceDialog : Window
    {
        private byte[]? _currentFontData;
        private byte[]? _currentFontIndex;
        private FontInfo? _currentFontInfo;
        
        private byte[]? _newFontData;
        private byte[]? _newFontIndex;
        private FontValidationResult? _validationResult;

        public byte[]? NewFontData => _newFontData;
        public byte[]? NewFontIndex => _newFontIndex;

        public FontReplaceDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 设置当前字体信息
        /// </summary>
        public void SetCurrentFontInfo(byte[]? fontData, byte[]? fontIndex, FontInfo? fontInfo)
        {
            _currentFontData = fontData;
            _currentFontIndex = fontIndex;
            _currentFontInfo = fontInfo;

            // 显示当前字体信息
            if (fontData != null && fontInfo != null)
            {
                CurrentFontDataInfo.Text = $"resfont.bin: {fontData.Length:N0} bytes, {fontInfo.CharCount} characters";
            }
            else
            {
                CurrentFontDataInfo.Text = "resfont.bin: Not available";
            }

            if (fontIndex != null && fontInfo != null)
            {
                CurrentFontIndexInfo.Text = $"resfontidx.bin: {fontIndex.Length:N0} bytes, {fontInfo.LanguageCount} languages";
            }
            else
            {
                CurrentFontIndexInfo.Text = "resfontidx.bin: Not available";
            }
        }

        /// <summary>
        /// 浏览 resfont.bin 文件
        /// </summary>
        private void BrowseFontData_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select resfont.bin",
                Filter = "Font data files|*.bin|All files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                NewFontDataPath.Text = dialog.FileName;
                _newFontData = File.ReadAllBytes(dialog.FileName);
                
                ValidateFiles();
            }
        }

        /// <summary>
        /// 浏览 resfontidx.bin 文件
        /// </summary>
        private void BrowseFontIndex_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select resfontidx.bin",
                Filter = "Font index files|*.bin|All files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                NewFontIndexPath.Text = dialog.FileName;
                _newFontIndex = File.ReadAllBytes(dialog.FileName);
                
                ValidateFiles();
            }
        }

        /// <summary>
        /// 验证选择的文件
        /// </summary>
        private void ValidateFiles()
        {
            if (_newFontData == null || _newFontIndex == null)
            {
                ValidationResult.Text = "Please select both font files.";
                ReplaceButton.IsEnabled = false;
                PreviewButton.IsEnabled = false;
                return;
            }

            // 执行验证
            _validationResult = FontValidator.Validate(_newFontData, _newFontIndex);

            // 显示验证结果
            var sb = new StringBuilder();
            sb.AppendLine(_validationResult.GetDisplayText());

            // 如果有原始字体信息，显示对比
            if (_currentFontInfo != null && _validationResult.Info != null)
            {
                sb.AppendLine();
                sb.AppendLine(FontValidator.CompareFontInfo(_currentFontInfo, _validationResult.Info));
            }

            ValidationResult.Text = sb.ToString();

            // 启用按钮
            ReplaceButton.IsEnabled = _validationResult.IsValid;
            PreviewButton.IsEnabled = _validationResult.IsValid;
        }

        /// <summary>
        /// 预览字体
        /// </summary>
        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (_validationResult?.Info == null || _newFontData == null || _newFontIndex == null)
                return;

            // TODO: 打开预览窗口显示新字体
            MessageBox.Show(
                $"Font Preview\n\n" +
                $"Characters: {_validationResult.Info.CharCount}\n" +
                $"Languages: {_validationResult.Info.LanguageCount}\n\n" +
                $"Preview feature coming soon!",
                "Preview",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>
        /// 执行替换
        /// </summary>
        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            if (!_validationResult?.IsValid ?? true)
            {
                MessageBox.Show(
                    "Cannot replace: Font files are not valid.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // 确认替换
            var message = new StringBuilder();
            message.AppendLine("Confirm Font Replacement");
            message.AppendLine();
            message.AppendLine("This will replace both resfont.bin and resfontidx.bin.");
            message.AppendLine();
            
            if (_validationResult != null && _validationResult.Warnings.Count > 0)
            {
                message.AppendLine("Warnings:");
                foreach (var warning in _validationResult.Warnings)
                {
                    message.AppendLine($"  • {warning}");
                }
                message.AppendLine();
            }

            message.AppendLine("Continue with replacement?");

            var result = MessageBox.Show(
                message.ToString(),
                "Confirm Replacement",
                MessageBoxButton.YesNo,
                _validationResult != null && _validationResult.Warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                DialogResult = true;
                Close();
            }
        }

        /// <summary>
        /// 取消
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
