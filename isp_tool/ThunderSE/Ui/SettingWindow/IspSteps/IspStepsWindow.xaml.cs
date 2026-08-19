using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace ThunderSE.Ui.SettingWindow.IspSteps
{
    public class OptionEnableConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var settingModule = (DeviceConfig.Isp.IspModule?)value;
            var stringParam = (string)parameter;

            DeviceConfig.Isp.IspModule? optionModule = null;

            switch (stringParam)
            {
                case "Blc":
                    optionModule = DeviceConfig.Isp.IspModule.Blc;
                    break;

                case "Lsc":
                    optionModule = DeviceConfig.Isp.IspModule.Lsc;
                    break;

                case "Ddc":
                    optionModule = DeviceConfig.Isp.IspModule.Ddc;
                    break;

                case "Awb":
                    optionModule = DeviceConfig.Isp.IspModule.Awb;
                    break;

                case "Ccm":
                    optionModule = DeviceConfig.Isp.IspModule.Ccm;
                    break;

                case "Dgain":
                    optionModule = DeviceConfig.Isp.IspModule.Dgain;
                    break;

                case "YGamma":
                    optionModule = DeviceConfig.Isp.IspModule.YGamma;
                    break;

                case "RgbGamma":
                    optionModule = DeviceConfig.Isp.IspModule.RgbGamma;
                    break;

                case "Ch":
                    optionModule = DeviceConfig.Isp.IspModule.Ch;
                    break;

                case "Vde":
                    optionModule = DeviceConfig.Isp.IspModule.Vde;
                    break;

                case "Ee":
                    optionModule = DeviceConfig.Isp.IspModule.Ee;
                    break;

                case "Cfd":
                    optionModule = DeviceConfig.Isp.IspModule.Cfd;
                    break;

                case "Saj":
                    optionModule = DeviceConfig.Isp.IspModule.Saj;
                    break;

                default:
                    break;
            }

            if (settingModule == null)
            {
                return true;
            }
            return settingModule > optionModule;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NavigateCommandParamConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var settingModule = (DeviceConfig.Isp.IspModule)value;
            var stringParam = (string)parameter;

            DeviceConfig.Isp.IspModule optionModule =  DeviceConfig.Isp.IspModule.Blc;

            switch (stringParam)
            {
                case "DeviceConfig.Isp.IspModule.Blc":
                    optionModule = DeviceConfig.Isp.IspModule.Blc;
                    break;

                case "DeviceConfig.Isp.IspModule.Lsc":
                    optionModule = DeviceConfig.Isp.IspModule.Lsc;
                    break;

                case "DeviceConfig.Isp.IspModule.Awb":
                    optionModule = DeviceConfig.Isp.IspModule.Awb;
                    break;

                default:
                    break;
            }

            return optionModule;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ModuleStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null || !(value is char))
            {
                return "关闭";
            }

            char status = (char)value;

            switch (status)
            {
                case (char)0x01:
                    return "启用";

                case (char)0x02:
                    return "Auto";

                default:
                    return "关闭";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class IspStepsWindow : Window
    {
        private IspStepsWindowViewModel _viewModel;

        public IspStepsWindow()
        {
            InitializeComponent();
            SetupKeyboardShortcuts();
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel = (IspStepsWindowViewModel)DataContext;
            UpdateStatus("窗口加载完成");
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            UpdateStatus("窗口已隐藏");
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.SetAllEnablesCommand.Execute(true);
                UpdateStatus("已选择所有模块");
            }
        }

        private void OnUnSelectAll(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.SetAllEnablesCommand.Execute(false);
                UpdateStatus("已取消所有模块选择");
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Hide();
            UpdateStatus("窗口已关闭");
        }

        private void SetupKeyboardShortcuts()
        {
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    OnSelectAll(null, null);
                    e.Handled = true;
                }
                else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    OnUnSelectAll(null, null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    OnClose(null, null);
                    e.Handled = true;
                }
            };
        }

        private void UpdateStatus(string message)
        {
            if (StatusText != null)
            {
                StatusText.Text = message;
            }
        }

        private void UpdateModuleCount()
        {
            if (ModuleCountText != null && _viewModel != null)
            {
                int selectedCount = 0;
                int totalCount = 13;

                if (_viewModel.IsBlcSelected) selectedCount++;
                if (_viewModel.IsLscSelected) selectedCount++;
                if (_viewModel.IsDdcSelected) selectedCount++;
                if (_viewModel.IsAwbSelected) selectedCount++;
                if (_viewModel.IsCcmSelected) selectedCount++;
                if (_viewModel.IsDgainSelected) selectedCount++;
                if (_viewModel.IsYGammaSelected) selectedCount++;
                if (_viewModel.IsRgbGammaSelected) selectedCount++;
                if (_viewModel.IsChSelected) selectedCount++;
                if (_viewModel.IsVdeSelected) selectedCount++;
                if (_viewModel.IsEeSelected) selectedCount++;
                if (_viewModel.IsCfdSelected) selectedCount++;
                if (_viewModel.IsSajSelected) selectedCount++;

                ModuleCountText.Text = $"已选 {selectedCount}/{totalCount} 个模块";
            }
        }
    }
}
