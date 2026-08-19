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
using ThunderSE.Ui.MainWindow.UserMode.EffectTabControl;

namespace ThunderSE.Ui.MainWindow.UserMode
{
    /// <summary>
    /// EffectTab.xaml µÄ½»»¥Âß¼­
    /// </summary>
    public partial class EffectTab : UserControl
    {
        private EffectTabViewModel _viewModel = null;

        public EffectTab()
        {
            InitializeComponent();

            AEPart.DataContext = null;
            VDEPart.DataContext = null;
            EEPart.DataContext = null;
            CHPart.DataContext = null;
            SAJPart.DataContext = null;
            CCMPart.DataContext = null;
            DDCPart.DataContext = null;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {

        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue as EffectTabViewModel != null)
            {
                _viewModel = (EffectTabViewModel)DataContext;

                AEPart.DataContext = new AEAreaViewModel(_viewModel.IspProcessor);
                VDEPart.DataContext = new VDEAreaViewModel(_viewModel.IspProcessor);
                EEPart.DataContext = new EEAreaViewModel(_viewModel.IspProcessor);
                CHPart.DataContext = new CHAreaViewModel(_viewModel.IspProcessor);
                SAJPart.DataContext = new SAJAreaViewModel(_viewModel.IspProcessor);
                CCMPart.DataContext = new CCMAreaViewModel(_viewModel.IspProcessor);
                DDCPart.DataContext = new DDCAreaViewModel(_viewModel.IspProcessor);
            }
            else
            {
                AEPart.DataContext = null;
                VDEPart.DataContext = null;
                EEPart.DataContext = null;
                CHPart.DataContext = null;
                SAJPart.DataContext = null;
                CCMPart.DataContext = null;
                DDCPart.DataContext = null;
            }
        }
    }
}
