using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ThunderSE.Ui.SettingWindow.YGamma
{
    public class ChartData : INotifyPropertyChanged
    {
        private Dictionary<double, double> _yAvg = new Dictionary<double, double>();
        private double _out_gamma = 0.0;

        public Dictionary<double, double> yAvg
        {
            get { return _yAvg; }
            set
            {
                _yAvg = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("yAvg"));
                }
            }
        }

        public double OutGamma
        {
            get { return _out_gamma; }
            set
            {
                _out_gamma = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("OutGamma"));
                }
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class GammaDataToChartLineConverter : IValueConverter
    {

        public object Convert(object value, Type targetType, object parameter, 
            System.Globalization.CultureInfo culture)
        {
            double out_gamma = (double)value;
            Dictionary<int, double> tmpGammaChartData = new Dictionary<int,double>();
            int maxItemCount = (int)parameter;
            for (int i = 2; i < maxItemCount - 2; i++)
            {
                //tmpGammaChartData[i] = Math.Pow(i, out_gamma);
                tmpGammaChartData[i] = Math.Pow((double)i / maxItemCount, out_gamma);
            }

            return tmpGammaChartData;
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// YGammaIQChart.xaml 的交互逻辑
    /// </summary>
    public partial class YGammaIQChartWindow : Window
    {
        public static readonly DependencyProperty GammaLineDataProperty =
            DependencyProperty.Register("GammaLineData",
                                         typeof(Dictionary<int, double>),
                                         typeof(YGammaIQChartWindow),
                                         new PropertyMetadata(new Dictionary<int, double>()));


        public Dictionary<int, double> GammaLineData
        {
            get { return (Dictionary<int, double>)GetValue(GammaLineDataProperty); }
            set { SetValue(GammaLineDataProperty, value); }
        }

        public int MaxItemCount
        {
            get;
            set;
        }


        public YGammaIQChartWindow()
        {
            InitializeComponent();
            MaxItemCount = 15;

            this.KeyDown += Window_KeyDown;
            this.SizeChanged += Window_SizeChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Binding gammaLineDataBinding = new Binding("OutGamma")
            {
                Converter = new GammaDataToChartLineConverter(),
                ConverterParameter = MaxItemCount
            };
            SetBinding(GammaLineDataProperty, gammaLineDataBinding);

            InitializeUIState();
        }

        private void InitializeUIState()
        {
            UpdateStatusBar("就绪 - YGamma IQ曲线分析工具已就绪");
            UpdateProcessingStatus("等待数据加载...");
            TxtMaxItemCount.Text = MaxItemCount.ToString();
            TxtOutGamma.Text = "--";
            UpdateDataRange();
            TxtWindowSize.Text = $"窗口尺寸: {this.ActualWidth:F0} x {this.ActualHeight:F0}";
            UpdateOperationStatus("就绪");
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (TxtWindowSize != null)
            {
                TxtWindowSize.Text = $"窗口尺寸: {e.NewSize.Width:F0} x {e.NewSize.Height:F0}";
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.F5:
                    OnRefreshDataClick(null, null);
                    e.Handled = true;
                    break;
                case Key.S:
                    if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
                    {
                        OnExportImageClick(null, null);
                        e.Handled = true;
                    }
                    break;
                case Key.R:
                    if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
                    {
                        OnResetViewClick(null, null);
                        e.Handled = true;
                    }
                    break;
                case Key.Add:
                    if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
                    {
                        OnZoomInClick(null, null);
                        e.Handled = true;
                    }
                    break;
                case Key.Subtract:
                    if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
                    {
                        OnZoomOutClick(null, null);
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void UpdateStatusBar(string message)
        {
            if (StatusBarText != null)
            {
                StatusBarText.Text = message;
            }
        }

        private void UpdateProcessingStatus(string status)
        {
            if (TxtProcessingStatus != null)
            {
                TxtProcessingStatus.Text = status;
            }
        }

        private void UpdateOperationStatus(string status)
        {
            if (TxtOperationStatus != null)
            {
                TxtOperationStatus.Text = status;
            }
        }

        private void UpdateDataRange()
        {
            if (TxtDataRange != null)
            {
                TxtDataRange.Text = $"X轴: 0-{MaxItemCount} | Y轴: 0-1.0";
            }
        }

        private void UpdateDataStatistics()
        {
            try
            {
                var chartData = DataContext as ChartData;

                if (chartData != null && chartData.yAvg != null)
                {
                    TxtYAvgCount.Text = chartData.yAvg.Count.ToString();

                    if (GammaLineData != null)
                    {
                        TxtGammaCount.Text = GammaLineData.Count.ToString();
                    }

                    TxtOutGamma.Text = chartData.OutGamma.ToString("F4");

                    UpdateStatusBar($"数据已加载 - YAvg: {chartData.yAvg.Count}点 | OutGamma: {chartData.OutGamma:F4}");
                    UpdateProcessingStatus($"数据已加载 ✓\nYAvg: {chartData.yAvg.Count}个数据点\nGamma值: {chartData.OutGamma:F4}");
                    UpdateOperationStatus("数据已加载");
                }
                else
                {
                    TxtYAvgCount.Text = "0";
                    TxtGammaCount.Text = "0";
                }
            }
            catch (Exception ex)
            {
                UpdateStatusBar($"统计数据更新失败: {ex.Message}");
            }
        }

        private void OnRefreshDataClick(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("正在刷新图表数据...");
            UpdateProcessingStatus("刷新中...");
            UpdateOperationStatus("刷新数据");

            try
            {
                Binding gammaLineDataBinding = new Binding("OutGamma")
                {
                    Converter = new GammaDataToChartLineConverter(),
                    ConverterParameter = MaxItemCount
                };
                SetBinding(GammaLineDataProperty, gammaLineDataBinding);

                UpdateDataStatistics();
                UpdateStatusBar("图表数据已刷新");
                UpdateProcessingStatus("刷新完成 ✓");
                UpdateOperationStatus("就绪");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新数据失败: {ex.Message}", "错误",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar("刷新数据失败");
                UpdateProcessingStatus("刷新失败 ✗");
            }
        }

        private void OnZoomInClick(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("放大图表视图");
            UpdateOperationStatus("视图已放大");

            MessageBox.Show("图表缩放功能需要配合鼠标滚轮使用\n或通过右键拖拽调整视图范围",
                          "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnZoomOutClick(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("缩小图表视图");
            UpdateOperationStatus("视图已缩小");

            MessageBox.Show("图表缩放功能需要配合鼠标滚轮使用\n或通过右键拖拽调整视图范围",
                          "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnResetViewClick(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("重置图表到默认视图");
            UpdateProcessingStatus("视图已重置");
            UpdateOperationStatus("视图已重置");

            InfoTabs.SelectedIndex = 0;

            try
            {
                if (YGammaChart != null)
                {
                    YGammaChart.UpdateLayout();
                }
            }
            catch (Exception)
            {
            }
        }

        private void OnExportImageClick(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("准备导出图表图像...");
            UpdateOperationStatus("导出图像");

            try
            {
                Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
                saveFileDialog.Filter = "PNG图像 (*.png) | *.png|JPEG图像 (*.jpg;*.jpeg) | *.jpg;*.jpeg|所有文件 (*.*) | *.*";
                saveFileDialog.FileName = "YGamma_IQ_Chart";
                saveFileDialog.DefaultExt = ".png";

                if (!(bool)saveFileDialog.ShowDialog())
                {
                    UpdateStatusBar("导出操作已取消");
                    UpdateOperationStatus("就绪");
                    return;
                }

                RenderTargetBitmap renderBitmap = new RenderTargetBitmap(
                    (int)YGammaChart.ActualWidth,
                    (int)YGammaChart.ActualHeight,
                    96d, 96d,
                    PixelFormats.Pbgra32);

                renderBitmap.Render(YGammaChart);

                using (System.IO.Stream outStream = System.IO.File.Create(saveFileDialog.FileName))
                {
                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
                    encoder.Save(outStream);
                }

                UpdateStatusBar($"图表已导出至: {saveFileDialog.FileName}");
                UpdateProcessingStatus("导出成功 ✓");
                UpdateOperationStatus("导出完成");

                MessageBox.Show($"图表图像已成功保存到:\n{saveFileDialog.FileName}",
                              "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出图像失败: {ex.Message}", "错误",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar("导出图像失败");
                UpdateProcessingStatus("导出失败 ✗");
            }
        }
    }

    /*
    public partial class YGammaIQChartWindow : Window
    {
        public static readonly DependencyProperty GammaLineDataProperty =
            DependencyProperty.Register("GammaLineData",
                                         typeof(Dictionary<int, double>),
                                         typeof(YGammaIQChartWindow),
                                         new PropertyMetadata(new Dictionary<int, double>()));


        public Dictionary<int, double> GammaLineData
        {
            get { return (Dictionary<int, double>)GetValue(GammaLineDataProperty); }
            set { SetValue(GammaLineDataProperty, value); }
        }

        public int MaxItemCount
        {
            get;
            set;
        }


        public YGammaIQChartWindow()
        {
            InitializeComponent();
            MaxItemCount = 15;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Binding gammaLineDataBinding = new Binding("OutGamma") 
            {
                Converter = new GammaDataToChartLineConverter(),
                ConverterParameter = MaxItemCount
            };
            SetBinding(GammaLineDataProperty, gammaLineDataBinding);
        }
    }
    */
}
