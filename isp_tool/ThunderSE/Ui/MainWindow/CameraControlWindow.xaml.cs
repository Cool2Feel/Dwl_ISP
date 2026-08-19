using System;
using System.Windows;
using System.Windows.Media;
using ThunderSE.Common;
using ThunderSE.Uvc;

namespace ThunderSE.Ui.MainWindow
{
    /// <summary>
    /// CameraControlWindow.xaml 的交互逻辑
    /// 以独立窗口形式展示 USB 摄像头的相机控制调节面板 (Camera Control)。
    /// </summary>
    public partial class CameraControlWindow : Window
    {
        public CameraControlWindow()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                CameraControlPanel.DataContext = CameraControlController.Instance;

                string descriptor = UvcReceiver.Instance.CurrentDeviceDescriptor;
                if (!string.IsNullOrEmpty(descriptor))
                {
                    // 统一初始化两套控制（图像属性 + 相机控制），保证缓存一致
                    ProcAmpController.Instance.Initialize(descriptor);
                    CameraControlController.Instance.Initialize(descriptor);
                }
                else
                {
                    ProcAmpController.Instance.Release();
                    CameraControlController.Instance.Release();
                }
                CameraControlPanel.RefreshStatus();
            }
            catch (Exception ex)
            {
                Logger.Error($"CameraControlWindow initialize failed: {ex.Message}");
            }
        }

        private void ShowStatus(string message, bool isError = false)
        {
            TxtActionStatus.Text = message;
            TxtActionStatus.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33))
                : new SolidColorBrush(Color.FromRgb(0x33, 0x88, 0x33));
            TxtActionStatus.Visibility = Visibility.Visible;

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
                if (!CameraControlController.Instance.IsAvailable)
                { ShowStatus("相机控制不可用，无法保存"); return; }
                CameraControlController.Instance.Save();
                CameraControlPanel.RefreshStatus();
                ShowStatus("✓ 相机参数已保存");
            }
            catch (Exception ex)
            {
                ShowStatus($"✗ 保存失败: {ex.Message}", true);
                Logger.Error($"CameraControl Save failed: {ex.Message}");
            }
        }

        private void OnLoadClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var controller = CameraControlController.Instance;
                if (!controller.IsAvailable)
                { ShowStatus("相机控制不可用，无法恢复"); return; }
                controller.LoadFromFile();
                CameraControlPanel.RefreshStatus();
                ShowStatus("✓ 相机参数已恢复");
            }
            catch (Exception ex)
            {
                ShowStatus($"✗ 恢复失败: {ex.Message}", true);
                Logger.Error($"CameraControl Load failed: {ex.Message}");
            }
        }
    }
}
