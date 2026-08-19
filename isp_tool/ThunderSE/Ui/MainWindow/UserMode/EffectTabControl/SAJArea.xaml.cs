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

namespace ThunderSE.Ui.MainWindow.UserMode.EffectTabControl
{
    /// <summary>
    /// SAJArea.xaml µÄ½»»¥Âß¼­
    /// </summary>
    /// 
    public partial class SAJArea : UserControl
    {
        private SAJAreaViewModel _viewModel = null;

        public SAJArea()
        {
            InitializeComponent();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue as SAJAreaViewModel != null)
            {
                _viewModel = (SAJAreaViewModel)e.NewValue;
            }
            else
            {
                _viewModel = null;
            }
        }
    }
}
