using System;
using System.Windows;
using System.Windows.Input;
using ThunderSE.Model;

namespace ThunderSE.Ui.SettingWindow.Blc
{
    /// <summary>
    /// BlcWindow - BLC黑电平校正窗口
    /// 用于分析RAW图像的像素数据分布并计算黑电平校正值
    /// </summary>
    public partial class BlcWindow : Window
    {
        private BlcWindowViewModel _viewModel;

        public BlcWindow()
        {
            InitializeComponent();

            SetupWindowEvents();
            SetupKeyboardShortcuts();
        }

        #region 窗口初始化与事件设置

        private void SetupWindowEvents()
        {
            this.SizeChanged += OnWindowSizeChanged;
            this.Closing += OnWindowClosing;
        }

        private void SetupKeyboardShortcuts()
        {
            // Ctrl+O: 打开RAW文件
            InputBindings.Add(new KeyBinding(new RelayCommand(() =>
                _viewModel?.OpenRawFileCommand.Execute(null)),
                Key.O, ModifierKeys.Control));

            // Ctrl+Enter: 应用校正
            InputBindings.Add(new KeyBinding(new RelayCommand(() =>
                _viewModel?.ApplyCorrectionCommand.Execute(null)),
                Key.Enter, ModifierKeys.Control));

            // F5: 刷新统计
            InputBindings.Add(new KeyBinding(new RelayCommand(() => RefreshStats_Click(null, null)),
                Key.F5, ModifierKeys.Control));
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateStatus($"窗口尺寸已调整: {this.ActualWidth:F0}×{this.ActualHeight:F0}");
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_viewModel != null && !string.IsNullOrEmpty(_viewModel.RawFile))
            {
                var result = MessageBox.Show(this,
                    "确定要关闭BLC黑电平校正窗口吗？\n\n当前加载的数据将不会自动保存。",
                    "确认关闭",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    UpdateStatus("已取消关闭操作");
                    return;
                }
            }

            UpdateStatus("正在关闭BLC黑电平校正窗口...");
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

        private void UpdateRawFileStatus(string status)
        {
            if (TxtRawFileStatus != null)
            {
                TxtRawFileStatus.Text = status;

                // 根据状态改变颜色
                if (status.Contains("未加载"))
                {
                    TxtRawFileStatus.Foreground = System.Windows.Media.Brushes.Gray;
                }
                else if (status.Contains("已加载") || status.Contains("就绪"))
                {
                    TxtRawFileStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD7)); // 蓝色
                }
                else if (status.Contains("处理中") || status.Contains("计算中"))
                {
                    TxtRawFileStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xFF, 0x99, 0x00)); // 橙色
                }
                else
                {
                    TxtRawFileStatus.Foreground = System.Windows.Media.Brushes.Green;
                }
            }
        }

        private void UpdateChartStatus(string status)
        {
            if (TxtChartStatus != null)
            {
                TxtChartStatus.Text = status;
            }
        }

        #endregion

        #region 窗口事件处理

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel = DataContext as BlcWindowViewModel;

            InitializeUI();

            UpdateStatus("✓ BLC黑电平校正工具初始化完成");
            UpdateRawFileStatus("未加载文件");
            UpdateChartStatus("");
            UpdateProgressInfo("请打开RAW图像文件开始分析");
        }

        private void InitializeUI()
        {
            UpdateStatus("BLC黑电平校正工具正在初始化...");
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (_viewModel != null)
            {
                try
                {
                    _viewModel.Cleanup();
                    UpdateStatus("✓ BLC资源清理完成");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"⚠ 清理过程中出现警告: {ex.Message}");
                }
            }
        }

        #endregion

        #region 操作按钮事件处理

        private void RefreshStats_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                MessageBox.Show(this,
                    "ViewModel尚未初始化，无法刷新统计数据。",
                    "操作错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_viewModel.RawFile))
            {
                MessageBox.Show(this,
                    "尚未加载RAW图像文件。\n\n请先点击'📂 打开RAW文件'按钮选择RAW图像。",
                    "无数据",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            UpdateStatus("🔄 正在刷新统计数据...");
            UpdateProgressInfo("重新计算四通道像素数据统计信息...");
            UpdateRawFileStatus("处理中...");

            try
            {
                // 触发ViewModel重新计算（通过重新执行OpenRawFileCommand）
                string currentRawFile = _viewModel.RawFile;
                if (!string.IsNullOrEmpty(currentRawFile))
                {
                    _viewModel.OpenRawFileCommand.Execute(currentRawFile);

                    UpdateStatus($"✓ 统计数据刷新完成 - 文件: {System.IO.Path.GetFileName(currentRawFile)}");
                    UpdateRawFileStatus($"已加载: {System.IO.Path.GetFileName(currentRawFile)}");
                    UpdateChartStatus($"(共 {_viewModel.RPixelData.Count} 个采样点)");
                    UpdateProgressInfo($"R:{_viewModel.AvgBlackLevelR} | GR:{_viewModel.AvgBlackLevelGR} | GB:{_viewModel.AvgBlackLevelGB} | B:{_viewModel.AvgBlackLevelB}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"刷新统计数据时发生错误:\n{ex.Message}\n\n请检查RAW文件是否有效。",
                    "刷新错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                UpdateStatus("❌ 刷新统计数据失败");
                UpdateProgressInfo($"错误: {ex.Message}");
                UpdateRawFileStatus("刷新失败");
            }
        }

        #endregion
    }


    /*
    public partial class BlcWindow : Window
    {
        public BlcWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var viewModel = (BlcWindowViewModel)DataContext;
            viewModel.OpenRawFileCommand.Execute(null);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            var viewModel = (BlcWindowViewModel)DataContext;
            viewModel?.Cleanup();
        }
    }*/
}
