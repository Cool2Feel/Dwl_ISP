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
    /// LcdCcmArea.xaml µÄ½»»¥Âß¼­
    /// </summary>
    public partial class LcdCcmArea : UserControl
    {
        private LcdCcmAreaViewModel _viewModel = null;

        public LcdCcmArea()
        {
            InitializeComponent();
        }

        private void OnSelectPresetCcmVal(object sender, RoutedEventArgs e)
        {
            var button = sender as RadioButton;
            if (button != null)
            {
                switch (button.Name)
                {
                    case "PresetCCMValRButton":
                        _viewModel.SetPresetCcmData("R");
                        break;

                    case "PresetCCMValGButton":
                        _viewModel.SetPresetCcmData("G");
                        break;

                    case "PresetCCMValBButton":
                        _viewModel.SetPresetCcmData("B");
                        break;

                    case "PresetCCMValYButton":
                        _viewModel.SetPresetCcmData("Y");
                        break;

                    case "PresetCCMValCButton":
                        _viewModel.SetPresetCcmData("C");
                        break;

                    case "PresetCCMValMButton":
                        _viewModel.SetPresetCcmData("M");
                        break;

                    default:
                        break;
                }
            }
        }

        public void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue as LcdCcmAreaViewModel != null)
            {
                _viewModel = (LcdCcmAreaViewModel)e.NewValue;
            }
            else
            {
                _viewModel = null;
            }
        }

        private void OnCCMDataTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            PresetCCMValRButton.IsChecked = false;
            PresetCCMValGButton.IsChecked = false;
            PresetCCMValBButton.IsChecked = false;
            PresetCCMValYButton.IsChecked = false;
            PresetCCMValCButton.IsChecked = false;
            PresetCCMValMButton.IsChecked = false;
        }
    }
}
