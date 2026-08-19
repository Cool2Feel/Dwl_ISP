using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Model;

namespace ThunderSE.Ui.SettingWindow.Lsc
{
    /// <summary>
    /// LscIQWindow - LSC IQ参数分析窗口
    /// 用于显示和分析LSC镜头阴影校正的12个关键IQ参数
    /// </summary>
    /// 

    class ValueRange
    {
        public double Min { get; set; }
        public double Max { get; set; }

        public ValueRange(double min, double max)
        {
            Min = min;
            Max = max;
        }
    }

    public class IQData
    {
        public string Group { get; set; }
        public string Name { get; set; }
        public double Value { get; set; }
        public string ValueRange { get; set; }
        public bool? IsGoodValue { get; set; }

        public IQData(string group, string name, double value, string valueRange, bool? isGoodValue)
        {
            Group = group;
            Name = name;
            Value = value;
            ValueRange = valueRange;
            IsGoodValue = isGoodValue;
        }
    }

    public partial class LscIQWindow : Window
    {
        private Dictionary<string, ValueRange> _iQRangeDictionary = new Dictionary<string, ValueRange>()
        {
            {"cr_tl", new ValueRange(0.85,1.20)},
            {"cr_tr", new ValueRange(0.85,1.20)},
            {"cr_bl", new ValueRange(0.85,1.20)},
            {"cr_br", new ValueRange(0.85,1.20)},
            {"cb_tl", new ValueRange(0.85,1.20)},
            {"cb_tr", new ValueRange(0.85,1.20)},
            {"cb_bl", new ValueRange(0.85,1.20)},
            {"cb_br", new ValueRange(0.85,1.20)},

            {"ly_tl", new ValueRange(0.80,1.10)},
            {"ly_tr", new ValueRange(0.80,1.10)},
            {"ly_bl", new ValueRange(0.80,1.10)},
            {"ly_br", new ValueRange(0.80,1.10)}
        };

        private ObservableCollection<IQData> _colorShadingIQ = new ObservableCollection<IQData>();
        private ICollectionView _view;

        public ObservableCollection<IQData> ColorShadingIQ
        {
            get { return _colorShadingIQ; }
        }

        public ICollectionView View
        {
            get { return _view; }
        }

        public LscIQWindow(LensShading lensShading, byte[] processedFileBuffer)
        {
            var lscStep = lensShading;

            ColorShadingIQResult colorShadingIQResult = new ColorShadingIQResult();
            LensShadingIQResult lensShadingIQResult = new LensShadingIQResult();

            lscStep.CalcIQ(processedFileBuffer, ref colorShadingIQResult, ref lensShadingIQResult);

            InitializeComponent();

            SetupWindowEvents();
            SetupKeyboardShortcuts();

            foreach (var member in typeof(ColorShadingIQResult).GetFields(BindingFlags.Instance |
                                                 BindingFlags.NonPublic |
                                                 BindingFlags.Public))
            {
                double value = (double)member.GetValue(colorShadingIQResult);
                if (_iQRangeDictionary.ContainsKey(member.Name))
                {
                    _colorShadingIQ.Add(new IQData("ColorShadingIQ", member.Name, (double)member.GetValue(colorShadingIQResult),
                    _iQRangeDictionary[member.Name].Min.ToString() + "-" + _iQRangeDictionary[member.Name].Max.ToString(),
                    value >= _iQRangeDictionary[member.Name].Min && value <= _iQRangeDictionary[member.Name].Max));
                }
                else
                {
                    _colorShadingIQ.Add(new IQData("ColorShadingIQ", member.Name, (double)member.GetValue(colorShadingIQResult),
                    "",
                    null));
                }
            }

            foreach (var member in typeof(LensShadingIQResult).GetFields(BindingFlags.Instance |
                                                 BindingFlags.NonPublic |
                                                 BindingFlags.Public))
            {
                if (_iQRangeDictionary.ContainsKey(member.Name))
                {
                    double value = (double)member.GetValue(lensShadingIQResult);
                    _colorShadingIQ.Add(new IQData("LensShadingIQ", member.Name, (double)member.GetValue(lensShadingIQResult),
                        _iQRangeDictionary[member.Name].Min.ToString() + "-" + _iQRangeDictionary[member.Name].Max.ToString(),
                        value >= _iQRangeDictionary[member.Name].Min && value <= _iQRangeDictionary[member.Name].Max));
                }
                else
                {
                    _colorShadingIQ.Add(new IQData("LensShadingIQ", member.Name, (double)member.GetValue(lensShadingIQResult),
                        "",
                        null));
                }
            }

            _view = CollectionViewSource.GetDefaultView(_colorShadingIQ);
            _view.GroupDescriptions.Add(new PropertyGroupDescription("Group")); 

            InitializeUI();
        }

        #region 窗口初始化与事件设置

        private void SetupWindowEvents()
        {
            this.SizeChanged += OnWindowSizeChanged;
            this.Loaded += OnWindowLoaded;
        }

        private void SetupKeyboardShortcuts()
        {
            // Ctrl+S: 导出CSV
            InputBindings.Add(new KeyBinding(new RelayCommand(() => ExportCSV_Click(null, null)), 
                Key.S, ModifierKeys.Control));
            
            // Ctrl+C: 复制数据
            InputBindings.Add(new KeyBinding(new RelayCommand(() => CopyData_Click(null, null)), 
                Key.C, ModifierKeys.Control));
            
            // F5: 刷新视图
            InputBindings.Add(new KeyBinding(new RelayCommand(() => Refresh_Click(null, null)), 
                Key.F5, ModifierKeys.Control));
            
            // Esc: 关闭窗口
            //InputBindings.Add(new KeyBinding(new RelayCommand(() => Close()), 
            //    Key.Escape, ModifierKeys.Control));
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateStatus($"窗口尺寸: {this.ActualWidth:F0}×{this.ActualHeight:F0}");
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            CalculateStatistics();
            UpdateStatus("✓ LSC IQ数据加载完成");
        }

        #endregion

        #region UI状态更新方法

        private void UpdateStatus(string message)
        {
            if (StatusBarText != null)
            {
                StatusBarText.Text = message;
            }
        }

        private void UpdateProgressInfo(string info)
        {
            if (TxtProgressInfo != null)
            {
                TxtProgressInfo.Text = info;
            }
        }

        private void UpdateDataStatus(string status)
        {
            if (TxtDataStatus != null)
            {
                TxtDataStatus.Text = status;
            }
        }

        #endregion

        #region 初始化与统计计算

        private void InitializeUI()
        {
            UpdateStatus("正在初始化LSC IQ分析窗口...");
            UpdateDataStatus("");
            UpdateProgressInfo("加载12个IQ参数数据...");
        }

        private void CalculateStatistics()
        {
            int totalCount = _colorShadingIQ.Count;
            int passCount = _colorShadingIQ.Count(item => item.IsGoodValue == true);
            int failCount = _colorShadingIQ.Count(item => item.IsGoodValue == false);
            int noRangeCount = _colorShadingIQ.Count(item => item.IsGoodValue == null);

            if (TxtTotalParams != null)
            {
                TxtTotalParams.Text = $"总参数: {totalCount}";
            }

            if (TxtPassRate != null)
            {
                double rate = totalCount > 0 ? (passCount / (double)(totalCount - noRangeCount)) * 100 : 0;
                TxtPassRate.Text = $"通过率: {rate:F1}% ({passCount}/{totalCount - noRangeCount})";
                
                // 根据通过率改变颜色
                if (rate >= 90)
                {
                    TxtPassRate.Foreground = Brushes.Green;
                }
                else if (rate >= 70)
                {
                    TxtPassRate.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x00)); // 橙色
                }
                else
                {
                    TxtPassRate.Foreground = Brushes.Red;
                }
            }

            if (TxtQualityAssessment != null)
            {
                if (failCount == 0 && noRangeCount == 0)
                {
                    TxtQualityAssessment.Text = "✅ 所有参数均合格！质量优秀";
                    TxtQualityAssessment.Foreground = Brushes.Green;
                }
                else if (failCount > 0)
                {
                    TxtQualityAssessment.Text = $"⚠️ 有{failCount}个参数超限，需要调整";
                    TxtQualityAssessment.Foreground = Brushes.Red;
                }
                else
                {
                    TxtQualityAssessment.Text = "📊 数据已加载完成";
                    TxtQualityAssessment.Foreground = Brushes.Gray;
                }
            }

            UpdateDataStatus($"(共 {totalCount} 个参数 | 合格 {passCount} | 超限 {failCount})");
            UpdateProgressInfo($"统计完成 | ColorShadingIQ: 8项 | LensShadingIQ: 4项");
        }

        #endregion

        #region 操作按钮事件处理

        private void ExportCSV_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV文件 (*.csv)|*.csv",
                    FileName = $"LSC_IQ_Data_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    Title = "导出LSC IQ数据"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    StringBuilder csvContent = new StringBuilder();
                    csvContent.AppendLine("分组,参数项,当前值,正常范围,是否合格");

                    foreach (var item in _colorShadingIQ)
                    {
                        string isGood = item.IsGoodValue.HasValue 
                            ? (item.IsGoodValue.Value ? "✓ 是" : "✗ 否") 
                            : "-";
                        
                        csvContent.AppendLine($"{item.Group},{item.Name},{item.Value:F4},{item.ValueRange},{isGood}");
                    }

                    File.WriteAllText(saveFileDialog.FileName, csvContent.ToString(), Encoding.UTF8);

                    MessageBox.Show(this,
                        $"IQ数据已成功导出到:\n{saveFileDialog.FileName}\n\n共 {_colorShadingIQ.Count} 条记录",
                        "导出成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    UpdateStatus($"✓ 数据已导出: {System.IO.Path.GetFileName(saveFileDialog.FileName)}");
                    UpdateProgressInfo($"文件大小: {new FileInfo(saveFileDialog.FileName).Length / 1024:F1} KB");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"导出CSV时发生错误:\n{ex.Message}",
                    "导出错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                
                UpdateStatus("❌ 导出失败");
                UpdateProgressInfo($"错误: {ex.Message}");
            }
        }

        private void CopyData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IQDataGrid.SelectedItem == null)
                {
                    // 如果没有选中行，复制所有数据
                    StringBuilder allData = new StringBuilder();
                    allData.AppendLine("分组\t参数项\t当前值\t正常范围\t是否合格");

                    foreach (var item in _colorShadingIQ)
                    {
                        string isGood = item.IsGoodValue.HasValue 
                            ? (item.IsGoodValue.Value ? "✓" : "✗") 
                            : "-";
                        
                        allData.AppendLine($"{item.Group}\t{item.Name}\t{item.Value:F4}\t{item.ValueRange}\t{isGood}");
                    }

                    Clipboard.SetText(allData.ToString());
                    
                    UpdateStatus($"✓ 已复制全部 {_colorShadingIQ.Count} 条记录到剪贴板");
                    UpdateProgressInfo("按 Ctrl+V 粘贴到Excel或其他应用");
                }
                else
                {
                    // 复制选中的单行数据
                    var selectedItem = (IQData)IQDataGrid.SelectedItem;
                    string isGood = selectedItem.IsGoodValue.HasValue 
                        ? (selectedItem.IsGoodValue.Value ? "✓ 是" : "✗ 否") 
                        : "-";

                    string rowData = $"{selectedItem.Group}\t{selectedItem.Name}\t{selectedItem.Value:F4}\t{selectedItem.ValueRange}\t{isGood}";
                    
                    Clipboard.SetText(rowData);

                    UpdateStatus($"✓ 已复制 [{selectedItem.Name}] 到剪贴板");
                    UpdateProgressInfo($"值: {selectedItem.Value:F4} | 范围: {selectedItem.ValueRange}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"复制数据时发生错误:\n{ex.Message}",
                    "复制错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                
                UpdateStatus("❌ 复制失败");
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatus("🔄 正在刷新视图...");
            UpdateProgressInfo("重新计算统计数据...");

            CalculateStatistics();

            UpdateStatus("✓ 视图刷新完成");
        }

        #endregion
    }
}
