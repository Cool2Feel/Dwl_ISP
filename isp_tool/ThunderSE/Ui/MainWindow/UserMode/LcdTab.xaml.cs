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
using ThunderSE.DeviceConfig.Lcd;
using ThunderSE.Ui.MainWindow.UserMode.LcdTabControl;

namespace ThunderSE.Ui.MainWindow.UserMode
{
    /// <summary>
    /// LcdTab.xaml µÄ½»»¥Âß¼­
    /// </summary>
    public partial class LcdTab : UserControl
    {
        private LcdTabViewModel _viewModel = null;

        public LcdTab()
        {
            InitializeComponent();

            CommonPart.DataContext = null;
            VdePart.DataContext = null;
            GammaPart.DataContext = null;
            CcmPart.DataContext = null;
            SajPart.DataContext = null;
            LsawtoothPart.DataContext = null;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {

        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue as LcdTabViewModel != null)
            {
                _viewModel = (LcdTabViewModel)DataContext;

                CommonPart.DataContext = new LcdCommonAreaViewModel((LcdCommon)_viewModel.LcdSetting.SettingSections[LcdSection.LcdCommon]);
                VdePart.DataContext = new LcdVdeAreaViewModel((LcdVde)_viewModel.LcdSetting.SettingSections[LcdSection.LcdVde]);
                GammaPart.DataContext = new LcdGammaAreaViewModel((LcdGamma)_viewModel.LcdSetting.SettingSections[LcdSection.LcdGamma]);
                CcmPart.DataContext = new LcdCcmAreaViewModel((LcdCcm)_viewModel.LcdSetting.SettingSections[LcdSection.LcdCcm]);
                SajPart.DataContext = new LcdSajAreaViewModel((LcdSaj)_viewModel.LcdSetting.SettingSections[LcdSection.LcdSaj]);
                LsawtoothPart.DataContext = new LcdLsawtoothViewModel((LcdLsawtooth)_viewModel.LcdSetting.SettingSections[LcdSection.LcdLsawtooth]);
            }
            else
            {
                CommonPart.DataContext = null;
                VdePart.DataContext = null;
                GammaPart.DataContext = null;
                CcmPart.DataContext = null;
                SajPart.DataContext = null;
                LsawtoothPart.DataContext = null;
                //CHPart.DataContext = null;
                //SAJPart.DataContext = null;
                //CCMPart.DataContext = null;
            }
        }
    }
}
