using System.Windows;

namespace ThunderSE.Ui.SettingWindow.Awb
{
    /// <summary>
    /// LabelInputDialog.xaml 的交互逻辑
    /// </summary>
    public partial class LabelInputDialog : Window
    {
        public string Label { get; set; }
        public LabelInputDialog(string prompt, string defaultLabel = "")
        {
            InitializeComponent();
            Title = prompt;
            Label = defaultLabel;
            DataContext = this;
            TxtLabel.Focus();
        }
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
