using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using ThunderSE.Uvc;
using ThunderSE.Common;

namespace ThunderSE.Ui.MainWindow
{
    /// <summary>
    /// ProcAmpPanel.xaml 的交互逻辑
    /// 展示当前已连接 USB 摄像头的图像属性（亮度/对比度/饱和度/色调/增益等）控制面板。
    /// DataContext 应设为 ProcAmpController.Instance。
    /// </summary>
    public partial class ProcAmpPanel : UserControl
    {
        /// <summary>内置 Boolean→Visibility 转换器（供 XAML 通过 x:Static 引用）</summary>
        public static readonly IValueConverter BoolToVisibility = new BooleanToVisibilityConverter();

        public ProcAmpPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 刷新面板状态文本（由宿主窗口在连接/断开时调用）
        /// </summary>
        public void RefreshStatus()
        {
            var controller = DataContext as ProcAmpController;
            if (controller == null)
            {
                TxtStatus.Text = "未初始化";
                return;
            }

            if (controller.IsAvailable)
            {
                TxtStatus.Text = $"已识别 {controller.Parameters.Count} 项可调图像属性";
            }
            else
            {
                TxtStatus.Text = string.IsNullOrEmpty(controller.LastError)
                    ? "设备未连接或不支持图像属性调节"
                    : controller.LastError;
            }
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            var button = e.OriginalSource as Button;
            var vm = button?.DataContext as ProcAmpParamViewModel;
            if (vm == null || !vm.SupportsManual) return;

            // 恢复为默认值：先切回手动模式，再写入默认值
            if (vm.IsAuto) vm.IsAuto = false;
            vm.SetDragging(true);  // 防止 Value setter 立即写入
            vm.Value = vm.Default;
            vm.SetDragging(false); // 立刻写入默认值

            // 不再自动持久化，需用户点击"保存"按钮才写入文件。
            RefreshStatus();
        }

        /// <summary>
        /// 滑块开始拖动：进入拖动状态，拖动期间不写入设备（避免高频调用导致 ksproxy E_INVALIDARG）。
        /// </summary>
        private void OnSliderDragStarted(object sender, DragStartedEventArgs e)
        {
            var vm = (sender as Slider)?.DataContext as ProcAmpParamViewModel;
            vm?.SetDragging(true);
        }

        /// <summary>
        /// 滑块拖动结束（鼠标释放）：一次性写入最终值到设备，避免拖动过程中高频调用设备。
        /// </summary>
        private void OnSliderDragCompleted(object sender, DragCompletedEventArgs e)
        {
            var vm = (sender as Slider)?.DataContext as ProcAmpParamViewModel;
            vm?.SetDragging(false);
        }

        private void OnResetAllClick(object sender, RoutedEventArgs e)
        {
            var controller = DataContext as ProcAmpController;
            if (controller == null) return;

            try
            {
                controller.ResetAllToDefault();
                RefreshStatus();
            }
            catch (Exception ex)
            {
                Logger.Error($"ResetAll ProcAmp failed: {ex.Message}");
            }
        }
    }
}
