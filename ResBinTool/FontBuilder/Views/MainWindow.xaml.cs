using System.Windows;
using FontBuilder.ViewModels;

namespace FontBuilder.Views
{
    /// <summary>
    /// MainWindow 交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
    }
}
