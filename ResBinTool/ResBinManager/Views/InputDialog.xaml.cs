using System.Windows;
using System.Windows.Controls;

namespace ResBinManager.Views
{
    /// <summary>
    /// 简单的输入对话框
    /// </summary>
    public partial class InputDialog : Window
    {
        /// <summary>
        /// 输入的文本
        /// </summary>
        public string InputText { get; private set; } = string.Empty;

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            InitializeComponent();
            
            Title = title;
            PromptTextBlock.Text = prompt;
            InputTextBox.Text = defaultValue;
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            InputText = InputTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
