using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ThunderSE.Ui.MainWindow.UserMode.LcdTabControl
{
    /// <summary>
    /// LcdSajArea.xaml µÄ½»»¥Âß¼­
    /// </summary>
    public partial class LcdSajArea : UserControl
    {
        private LcdSajAreaViewModel _viewModel = null;

        public LcdSajArea()
        {
            InitializeComponent();
        }

        public void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue as LcdSajAreaViewModel != null)
            {
                _viewModel = (LcdSajAreaViewModel)e.NewValue;
            }
            else
            {
                _viewModel = null;
            }
        }
    }
}
