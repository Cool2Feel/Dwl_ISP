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

namespace ThunderSE.Ui.SettingWindow.Awb
{
    /// <summary>
    /// ModeSwitchControl.xaml 的交互逻辑
    /// 模式切换控件 - 管理传统模式与智能模式的切换
    /// </summary>
    public partial class ModeSwitchControl : UserControl
    {
        // 定义模式切换事件
        public static readonly RoutedEvent ModeChangedEvent = 
            EventManager.RegisterRoutedEvent("ModeChanged", 
                RoutingStrategy.Bubble, 
                typeof(RoutedEventHandler), 
                typeof(ModeSwitchControl));

        // CLR包装器
        public event RoutedEventHandler ModeChanged
        {
            add { AddHandler(ModeChangedEvent, value); }
            remove { RemoveHandler(ModeChangedEvent, value); }
        }

        // 当前模式属性
        public bool IsSmartMode
        {
            get { return (bool)GetValue(IsSmartModeProperty); }
            set { SetValue(IsSmartModeProperty, value); }
        }

        // 依赖属性注册
        public static readonly DependencyProperty IsSmartModeProperty =
            DependencyProperty.Register("IsSmartMode", typeof(bool), 
                typeof(ModeSwitchControl), 
                new PropertyMetadata(false, OnIsSmartModeChanged));

        private static void OnIsSmartModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as ModeSwitchControl;
            if (control != null)
            {
                control.UpdateRadioButtonState((bool)e.NewValue);
            }
        }

        public ModeSwitchControl()
        {
            InitializeComponent();
            
            // 初始化状态
            UpdateRadioButtonState(IsSmartMode);
        }

        private void RbTraditionalMode_Checked(object sender, RoutedEventArgs e)
        {
            IsSmartMode = false;
            RaiseModeChangedEvent(false);
        }

        private void RbSmartMode_Checked(object sender, RoutedEventArgs e)
        {
            IsSmartMode = true;
            RaiseModeChangedEvent(true);
        }

        /// <summary>
        /// 更新RadioButton状态（当外部设置IsSmartMode时）
        /// </summary>
        private void UpdateRadioButtonState(bool isSmartMode)
        {
            if (isSmartMode)
            {
                if (!RbSmartMode.IsChecked == true)
                {
                    RbSmartMode.IsChecked = true;
                }
            }
            else
            {
                if (!RbTraditionalMode.IsChecked == true)
                {
                    RbTraditionalMode.IsChecked = true;
                }
            }
        }

        /// <summary>
        /// 触发模式切换事件
        /// </summary>
        private void RaiseModeChangedEvent(bool isSmartMode)
        {
            var args = new RoutedEventArgs(ModeChangedEvent);
            args.Source = this;
            RaiseEvent(args);
            
            // 同时更新Tag用于传递额外信息
            Tag = isSmartMode ? "Smart" : "Traditional";
        }

        /// <summary>
        /// 切换到指定模式（供外部调用）
        /// </summary>
        public void SwitchToMode(bool enableSmartMode)
        {
            IsSmartMode = enableSmartMode;
        }

        /// <summary>
        /// 获取当前模式名称（用于日志/调试）
        /// </summary>
        public string GetCurrentModeName()
        {
            return IsSmartMode ? "智能插值模式" : "传统编辑模式";
        }
    }
}
