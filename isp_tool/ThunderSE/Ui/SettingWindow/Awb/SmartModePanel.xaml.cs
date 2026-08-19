using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ThunderSE.Ui.SettingWindow.Awb
{
    /// <summary>
    /// SmartModePanel.xaml 的交互逻辑
    /// 智能模式独立控制面板 - 完全封装，不影响原有UI组件
    /// </summary>
    public partial class SmartModePanel : UserControl
    {
        public SmartModePanel()
        {
            InitializeComponent();
            
            // 注册键盘快捷键（不与原有快捷键冲突）
            this.KeyDown += SmartModePanel_KeyDown;
            
            // 初始化状态文本
            UpdateStatusText("就绪 - 智能插值模式已启用");
        }

        private void SmartModePanel_KeyDown(object sender, KeyEventArgs e)
        {
            // 智能模式专用快捷键
            // Ctrl+R: 重置关键点
            if (Keyboard.IsKeyDown(Key.LeftCtrl) && e.Key == Key.R)
            {
                // 通过DataContext调用ViewModel命令
                var viewModel = DataContext as AwbWindowViewModel;
                if (viewModel?.InitializeDefaultKeyPointsCommand != null)
                {
                    viewModel.InitializeDefaultKeyPointsCommand.Execute(null);
                    UpdateStatusText("已重置关键点为默认值");
                    e.Handled = true;
                }
            }
            
            // Ctrl+G: 生成曲线
            if (Keyboard.IsKeyDown(Key.LeftCtrl) && e.Key == Key.G)
            {
                var viewModel = DataContext as AwbWindowViewModel;
                if (viewModel?.GenerateCurveFromKeyPointsCommand != null)
                {
                    viewModel.GenerateCurveFromKeyPointsCommand.Execute(null);
                    UpdateStatusText("已从关键点生成完整曲线");
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// 更新底部状态栏文本
        /// </summary>
        private void UpdateStatusText(string message)
        {
            if (TxtSmartStatus != null)
            {
                TxtSmartStatus.Text = message;
            }
        }

        /// <summary>
        /// 当关键点数量改变时更新提示信息
        /// </summary>
        public void OnKeyPointCountChanged(int newCount)
        {
            string efficiencyHint = newCount <= 8 ? "高效模式" : 
                                   newCount <= 12 ? "平衡模式" : 
                                   "精细模式";
            
            UpdateStatusText($"当前使用{newCount}个关键点 ({efficiencyHint})");
        }

        /// <summary>
        /// 当曲线生成完成时显示成功消息
        /// </summary>
        public void OnCurveGenerated()
        {
            UpdateStatusText("✅ 曲线生成成功 - 可预览或应用");
        }

        /// <summary>
        /// 当应用到设备时显示进度
        /// </summary>
        public void OnApplyToDevice()
        {
            UpdateStatusText("⏳ 正在应用配置到设备...");
        }

        /// <summary>
        /// 显示错误信息
        /// </summary>
        public void ShowError(string errorMessage)
        {
            UpdateStatusText($"❌ 错误: {errorMessage}");
            TxtSmartStatus.Foreground = Brushes.Red;
            
            // 3秒后恢复默认颜色
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += (s, args) =>
            {
                TxtSmartStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
                timer.Stop();
            };
            timer.Start();
        }
    }
}
