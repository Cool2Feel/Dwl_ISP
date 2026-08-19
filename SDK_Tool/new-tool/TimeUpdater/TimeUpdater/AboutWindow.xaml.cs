using System.Windows;

namespace TimeUpdater
{
    /// <summary>
    /// About dialog for the TimeUpdater application.
    /// Corresponds to the original CAboutDlg in the MFC project.
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}