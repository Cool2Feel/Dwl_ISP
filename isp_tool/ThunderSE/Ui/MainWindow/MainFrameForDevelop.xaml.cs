using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ThunderSE.Common;
using ThunderSE.DeviceConfig;

namespace ThunderSE.Ui.MainWindow
{
    class ConfigPageToTreeViewItemConverter : IValueConverter
    {
        private TreeView _configTreeView = null;
        private Dictionary<string, DeviceConfigPage> _devConfigPageCollection = null;

        public ConfigPageToTreeViewItemConverter(TreeView configTreeView, Dictionary<string, DeviceConfigPage> devConfigPageCollection)
        {
            _configTreeView = configTreeView;
            _devConfigPageCollection = devConfigPageCollection;
        }

        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
            {
                return null;
            }


            var selectedItem = (TreeViewItem)value;
            var key = ((KeyValuePair<string, Config>)selectedItem.Header).Key;
            if (!_devConfigPageCollection.ContainsKey(key))
            {
                return null;
            }

            return _devConfigPageCollection[key];
        }
    }

    /// <summary>
    /// MainFrameForDevelop.xaml 的交互逻辑
    /// </summary>
    public partial class MainFrameForDevelop : Window
    {
        private TreeViewItem _configTreeViewOnlineItem = null;
        private TreeViewItem _configTreeViewOfflineItem = null;

        private MainFrameForDevelopViewModel _viewModel = null;

        private Dictionary<string, DeviceConfigPage> _devConfigPageCollection = new Dictionary<string, DeviceConfigPage>();

        public static readonly DependencyProperty CurrentDevConfigPageProperty = DependencyProperty.Register(
            "CurrentDevConfigPage",
            typeof(DeviceConfigPage),
            typeof(MainFrameForDevelop),
            new FrameworkPropertyMetadata(null));

        public DeviceConfigPage CurrentDevConfigPage
        {
            get { return (DeviceConfigPage)GetValue(CurrentDevConfigPageProperty); }
            set
            {
                SetValue(CurrentDevConfigPageProperty, value);
                UpdateCurrentPageInfo();
            }
        }

        public static readonly DependencyProperty CurrentSelectedConfigTreeViewItemProperty = DependencyProperty.Register(
            "CurrentSelectedConfigTreeViewItem",
            typeof(TreeViewItem),
            typeof(MainFrameForDevelop),
            new FrameworkPropertyMetadata(null));


        public TreeViewItem CurrentSelectedConfigTreeViewItem
        {
            get { return (TreeViewItem)GetValue(CurrentSelectedConfigTreeViewItemProperty); }
            set
            {
                SetValue(CurrentSelectedConfigTreeViewItemProperty, value);
            }
        }


        public MainFrameForDevelop()
        {
            InitializeComponent();

            var curSelConfigTreeViewItemBinding = new Binding("CurrentDevConfigPage")
            {
                Source = this,
                Converter = new ConfigPageToTreeViewItemConverter(this.DevConfigListTreeView, this._devConfigPageCollection),
                Mode = BindingMode.OneWayToSource
            };
            this.SetBinding(CurrentSelectedConfigTreeViewItemProperty, curSelConfigTreeViewItemBinding);

            this.KeyDown += Window_KeyDown;
            this.SizeChanged += Window_SizeChanged;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel = (MainFrameForDevelopViewModel)DataContext;

            _configTreeViewOnlineItem = this.DevConfigListTreeView.
                ItemContainerGenerator.ContainerFromItem(_viewModel.DeviceConfigs[0]) as TreeViewItem;
            _configTreeViewOfflineItem = this.DevConfigListTreeView.
                ItemContainerGenerator.ContainerFromItem(_viewModel.DeviceConfigs[1]) as TreeViewItem;

            _viewModel.OnlineConfigs.CollectionChanged += OnDeviceListChange;
            _viewModel.OfflineConfigs.CollectionChanged += OnDeviceListChange;

            _viewModel.ScanOnlineDevice();

            InitializeUIState();

            UpdateStatusBar("就绪 - ThunderSE ISP调试工具已启动");
            UpdateMenuStatus("● 已连接");
        }

        private void InitializeUIState()
        {
            TxtWindowSize.Text = $"窗口尺寸: {this.ActualWidth:F0} x {this.ActualHeight:F0}";
            TxtCurrentPageInfo.Text = "未选择配置";
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
            if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.O:
                        OnOpenDeviceConfigFile(null, null);
                        e.Handled = true;
                        break;
                    case Key.N:
                        OnCreateDeviceConfigFile(null, null);
                        e.Handled = true;
                        break;
                }
            }
            else
            {
                switch (e.Key)
                {
                    case Key.F5:
                        _viewModel?.ScanOnlineDevice();
                        UpdateStatusBar("正在扫描在线设备...");
                        UpdateMenuStatus("🔍 扫描中...");
                        e.Handled = true;
                        break;
                }
            }
        }

        private void UpdateStatusBar(string message)
        {
            if (StatusBarText != null)
            {
                StatusBarText.Text = message;
            }
        }

        private void UpdateMenuStatus(string status)
        {
            if (TxtMenuStatus != null)
            {
                TxtMenuStatus.Text = status;
            }
        }

        private void UpdateCurrentPageInfo()
        {
            if (TxtCurrentPageInfo != null && CurrentDevConfigPage != null)
            {
                var viewModel = CurrentDevConfigPage.DataContext as DeviceConfigPageViewModel;
                if (viewModel != null)
                {
                    TxtCurrentPageInfo.Text = viewModel.DeviceConfig.Name ?? "配置页面";
                }
            }
        }

        void OnDeviceListChange(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                    var newPair = (KeyValuePair<string, Config>)e.NewItems[0];
                    if (_devConfigPageCollection.ContainsKey(newPair.Key))
                    {
                        Logger.Warn($"Device already exists: {newPair.Key}, skipping duplicate");
                        UpdateStatusBar($"设备已存在: {newPair.Key}");
                        return;
                    }

                    var devConfigPage = new DeviceConfigPage();
                    devConfigPage.DataContext = new DeviceConfigPageViewModel(newPair.Value);
                    _devConfigPageCollection.Add(newPair.Key, devConfigPage);
                    CurrentDevConfigPage = devConfigPage;
                    var item = _configTreeViewOfflineItem.ItemContainerGenerator.ContainerFromItem(newPair) as TreeViewItem;
                    if (item == null)
                    {
                        item = _configTreeViewOnlineItem.ItemContainerGenerator.ContainerFromItem(newPair) as TreeViewItem;
                    }
                    if (item != null)
                    {
                        item.IsSelected = true;
                    }

                    UpdateStatusBar($"已添加设备配置: {newPair.Key}");
                    UpdateMenuStatus("✅ 已更新");
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                    // TODO:新的index应该为旧的index + 1, 如果没有这个index，则取下一个index
                    var oldPair = (KeyValuePair<string, Config>)e.OldItems[0];
                    KeyValuePair<string, Config> toSelectPair;
                    if (_devConfigPageCollection.Count > 1)
                    {
                        var devConfigKeysArray = _devConfigPageCollection.Keys.ToArray();
                        int oldindex = Array.IndexOf(devConfigKeysArray, oldPair.Key);
                        // 当选择新的删除旧的
                        if (devConfigKeysArray.Count() - 1 > oldindex)
                        {
                            CurrentDevConfigPage = _devConfigPageCollection[devConfigKeysArray[oldindex + 1]];
                            toSelectPair = new KeyValuePair<string, Config>(devConfigKeysArray[oldindex + 1],
                                ConfigManager.GetInstance().GetConfig(devConfigKeysArray[oldindex + 1]));
                        }
                        else
                        {
                            CurrentDevConfigPage = _devConfigPageCollection[devConfigKeysArray[oldindex - 1]];
                            toSelectPair = new KeyValuePair<string, Config>(devConfigKeysArray[oldindex - 1],
                                ConfigManager.GetInstance().GetConfig(devConfigKeysArray[oldindex - 1]));
                        }

                        var item3 = _configTreeViewOfflineItem.ItemContainerGenerator.ContainerFromItem(toSelectPair) as TreeViewItem;
                        if (item3 == null)
                        {
                            item3 = _configTreeViewOnlineItem.ItemContainerGenerator.ContainerFromItem(toSelectPair) as TreeViewItem;
                        }

                        if (item3 != null)
                        {
                            CurrentSelectedConfigTreeViewItem = item3;
                            CurrentSelectedConfigTreeViewItem.IsSelected = true;
                        }
                    }
                    else
                    {
                        CurrentDevConfigPage = null;
                        TxtCurrentPageInfo.Text = "未选择配置";
                    }
                    _devConfigPageCollection.Remove(oldPair.Key);

                    UpdateStatusBar($"已移除设备配置: {oldPair.Key}");
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                    break;
                default:
                    break;
            }
        }

        private void OnCreateDeviceConfigFile(object sender, RoutedEventArgs e)
        {
            var createConfigWindow = new CreateConfigWindow();
            createConfigWindow.Owner = this;
            createConfigWindow.ShowDialog();
            if (createConfigWindow.DialogResult.Value == true)
            {
                var vm = (MainFrameForDevelopViewModel)DataContext;
                vm.CreateOfflineConfigCommand.Execute(createConfigWindow.ConfigName);
            }
        }

        private void OnSelectedDeviceConfigChange(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var item = _configTreeViewOfflineItem.ItemContainerGenerator.ContainerFromItem(e.NewValue) as TreeViewItem;
            if (item == null)
            {
                item = _configTreeViewOnlineItem.ItemContainerGenerator.ContainerFromItem(e.NewValue) as TreeViewItem;
            }

            if (item != null)
            {
                CurrentSelectedConfigTreeViewItem = item;
            }
        }

        private void OnOpenDeviceConfigFile(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "isp配置文件(*.isp) | *.isp";
            if (!(bool)openFileDialog.ShowDialog())
            {
                return;
            }

            _viewModel.OpenOfflineConfigFileCommand.Execute(openFileDialog.FileName);

            UpdateStatusBar($"正在打开配置文件: {System.IO.Path.GetFileName(openFileDialog.FileName)}");
            UpdateMenuStatus("📂 打开中...");
        }

        /// <summary>
        /// 查看崩溃日志
        /// </summary>
        private void OnViewCrashLog(object sender, RoutedEventArgs e)
        {
            try
            {
                var crashLogWindow = new CrashLogWindow();
                crashLogWindow.Owner = this;
                crashLogWindow.ShowDialog();

                UpdateStatusBar("崩溃日志查看器已打开");
                UpdateMenuStatus("📋 日志");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开崩溃日志窗口失败：\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

                UpdateStatusBar("打开崩溃日志失败");
            }
        }

        /// <summary>
        /// 退出程序
        /// </summary>
        private void OnExit(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        /// <summary>
        /// 关于对话框
        /// </summary>
        private void OnAbout(object sender, RoutedEventArgs e)
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            MessageBox.Show(
                $"ThunderSE - ISP 调试工具\n\n" +
                $"版本：{version}\n" +
                $".NET Framework: {Environment.Version}\n\n" +
                "© 2026 All Rights Reserved\n\n" +
                "功能特性:\n" +
                "• USB摄像头实时预览与控制\n" +
                "• ISP参数在线/离线调试\n" +
                "• 设备配置管理与保存\n" +
                "• 多模块协同工作流程",
                "关于",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            UpdateStatusBar("关于对话框已显示");
        }
    }
}
