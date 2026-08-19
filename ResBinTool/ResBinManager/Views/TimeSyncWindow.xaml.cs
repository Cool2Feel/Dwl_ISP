using System;
using System.Windows;
using System.Windows.Interop;

namespace ResBinManager.Views
{
    public partial class TimeSyncWindow : Window
    {
        private readonly ViewModels.TimeSyncViewModel _viewModel;
        private const int WM_DEVICECHANGE = 0x0219;

        public TimeSyncWindow()
        {
            InitializeComponent();
            _viewModel = new ViewModels.TimeSyncViewModel();
            DataContext = _viewModel;
            Closed += TimeSyncWindow_Closed;
            SourceInitialized += TimeSyncWindow_SourceInitialized;
        }

        private void TimeSyncWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // 注册 WM_DEVICECHANGE 消息钩子，实现设备热插拔即时响应
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(DeviceChangeHook);
        }

        private IntPtr DeviceChangeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                _viewModel.OnDeviceChange();
            }
            return IntPtr.Zero;
        }

        private void TimeSyncWindow_Closed(object? sender, EventArgs e)
        {
            _viewModel.Dispose();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}