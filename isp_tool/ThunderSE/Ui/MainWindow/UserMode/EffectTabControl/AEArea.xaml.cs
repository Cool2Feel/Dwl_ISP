using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ThunderSE.Ui.MainWindow.UserMode.EffectTabControl
{
    /// <summary>
    /// AEArea.xaml µÄ½»»¥Âß¼­
    /// </summary>
    /// 
    public class AEGainMaxValueConverter : IValueConverter
    {

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return ((int)value) / 256;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return ((int)value) * 256;
        }
    }

    public partial class AEArea : UserControl
    {
        private AEAreaViewModel _viewModel = null;
        private DataArrayToControlCollectionConverter _expTagConverter = null;

        public AEArea()
        {
            InitializeComponent();

            _expTagConverter = (DataArrayToControlCollectionConverter)TryFindResource("ExpTagConverter");
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _expTagConverter.DataArray = _viewModel.ExpTag;
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue as AEAreaViewModel != null)
            {
                _viewModel = (AEAreaViewModel)e.NewValue;
                _expTagConverter.DataArray = _viewModel.ExpTag;
            }
            else
            {
                _expTagConverter.DataArray = null;
                _viewModel = null;
            }
        }
    }
}
