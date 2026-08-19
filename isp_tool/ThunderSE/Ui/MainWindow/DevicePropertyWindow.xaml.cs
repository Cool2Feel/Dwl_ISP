using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ThunderSE.Common;
using ThunderSE.Uvc;

namespace ThunderSE.Ui.MainWindow
{
    /// <summary>
    /// DevicePropertyWindow.xaml 的交互逻辑
    /// 设备属性设置窗口：在一个窗口中提供图像属性(ProcAmpPanel)与相机控制(CameraControlPanel)两套面板。
    /// 打开时统一初始化两套控制，关闭时统一释放，避免重复初始化与资源泄漏。
    /// </summary>
    public partial class DevicePropertyWindow : Window
    {
        private readonly string _deviceDescriptor;

        public DevicePropertyWindow(string deviceDescriptor)
        {
            InitializeComponent();
            _deviceDescriptor = deviceDescriptor ?? "";
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtDevice.Text = string.IsNullOrEmpty(_deviceDescriptor)
                    ? "未指定设备"
                    : _deviceDescriptor;

                ProcAmpPanel.DataContext = ProcAmpController.Instance;
                CameraControlPanel.DataContext = CameraControlController.Instance;

                InitializeControls();
                ProcAmpPanel.RefreshStatus();
                CameraControlPanel.RefreshStatus();
                LoadResolutions();
            }
            catch (Exception ex)
            {
                Logger.Error($"DevicePropertyWindow initialize failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 统一初始化图像属性 + 相机控制两套控制器。
        /// 原生层 InitProcAmp 会同时初始化两套，此处两个控制器均调用以确保托管缓存同步刷新。
        /// </summary>
        private void InitializeControls()
        {
            if (string.IsNullOrEmpty(_deviceDescriptor))
            {
                ProcAmpController.Instance.Release();
                CameraControlController.Instance.Release();
                return;
            }

            ProcAmpController.Instance.Initialize(_deviceDescriptor);
            CameraControlController.Instance.Initialize(_deviceDescriptor);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            try
            {
                // 窗口关闭统一释放，防止资源泄漏
                ProcAmpController.Instance.Release();
                CameraControlController.Instance.Release();
            }
            catch (Exception ex)
            {
                Logger.Error($"DevicePropertyWindow release failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 枚举设备支持的分辨率/像素格式（原生层 IAMStreamConfig 探测，工作线程执行），
        /// 填充"分辨率 (Resolution)"页签并更新当前分辨率显示。
        /// 当前版本原生层经安全通道（GetFormat）返回设备当前格式（通常 1 条）。
        /// 失败时给出提示而不抛出（不影响其他页签）。
        /// </summary>
        private void LoadResolutions()
        {
            try
            {
                var formats = new UvcApi.VideoFormatInfo[64];
                int count = UvcApi.EnumVideoFormats(_deviceDescriptor, formats, formats.Length);
                if (count <= 0)
                {
                    TxtCurrentResolution.Text = "无法获取分辨率信息";
                    ListResolutions.ItemsSource = null;
                    return;
                }

                var items = new List<UvcApi.VideoFormatInfo>();
                for (int i = 0; i < count && i < formats.Length; i++)
                    items.Add(formats[i]);
                ListResolutions.ItemsSource = items;
                if (items.Count > 0)
                {
                    ListResolutions.SelectedIndex = 0;
                    TxtCurrentResolution.Text = items[0].ToString();
                }
                TxtResolutionHint.Text = $"共 {items.Count} 种格式（来自 DirectShow IAMStreamConfig）。";
            }
            catch (Exception ex)
            {
                Logger.Error($"LoadResolutions failed: {ex.Message}");
                TxtCurrentResolution.Text = "无法获取分辨率信息";
            }
        }

        private void ShowStatus(string message, bool isError = false)
        {
            TxtActionStatus.Text = message;
            TxtActionStatus.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33))
                : new SolidColorBrush(Color.FromRgb(0x33, 0x88, 0x33));
            TxtActionStatus.Visibility = Visibility.Visible;

            // 3 秒后自动隐藏
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                TxtActionStatus.Visibility = Visibility.Collapsed;
            };
            timer.Start();
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isProcAmp = TabControl.SelectedIndex == 0;
                if (isProcAmp)
                {
                    if (!ProcAmpController.Instance.IsAvailable)
                    { ShowStatus("图像属性不可用，无法保存"); return; }
                    ProcAmpController.Instance.Save();
                }
                else
                {
                    if (!CameraControlController.Instance.IsAvailable)
                    { ShowStatus("相机控制不可用，无法保存"); return; }
                    CameraControlController.Instance.Save();
                }
                ProcAmpPanel.RefreshStatus();
                CameraControlPanel.RefreshStatus();
                ShowStatus($"✓ 参数已保存");
            }
            catch (Exception ex)
            {
                ShowStatus($"✗ 保存失败: {ex.Message}", true);
                Logger.Error($"Save failed: {ex.Message}");
            }
        }

        private void OnLoadClick(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isProcAmp = TabControl.SelectedIndex == 0;

                if (isProcAmp)
                {
                    if (!ProcAmpController.Instance.IsAvailable)
                    { ShowStatus("图像属性不可用，无法恢复"); return; }
                    ProcAmpController.Instance.LoadFromFile();
                }
                else
                {
                    if (!CameraControlController.Instance.IsAvailable)
                    { ShowStatus("相机控制不可用，无法恢复"); return; }
                    CameraControlController.Instance.LoadFromFile();
                }
                ProcAmpPanel.RefreshStatus();
                CameraControlPanel.RefreshStatus();
                ShowStatus($"✓ 参数已恢复");
            }
            catch (Exception ex)
            {
                ShowStatus($"✗ 恢复失败: {ex.Message}", true);
                Logger.Error($"Load failed: {ex.Message}");
            }
        }
    }
}
