using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TimeUpdater.Services;

namespace TimeUpdater
{
    /// <summary>
    /// Main window for the TimeUpdater application.
    /// Displays current system time and updates the time on Buildwin/AX3231MP USB devices.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int WM_DEVICECHANGE = 0x0219;

        private readonly DispatcherTimer _timer;
        private readonly DeviceService _deviceService;
        private bool _isScanning;

        public MainWindow()
        {
            Logger.Info("============================================");
            Logger.Info("[MainWindow] Application starting ...");
            Logger.Info("[MainWindow] OS: {0}, .NET: {1}",
                Environment.OSVersion,
                Environment.Version);
            Logger.Info("[MainWindow] Process architecture: {0}",
                Environment.Is64BitProcess ? "x64" : "x86");
            Logger.Info("============================================");

            InitializeComponent();
            _deviceService = new DeviceService();

            // Set up a 1-second timer to refresh the time display (same as original)
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();
            Logger.Info("[MainWindow] Timer started (1-second interval).");

            // Display current time immediately
            DisplayTime();

            // Register for window messages to handle WM_DEVICECHANGE
            SourceInitialized += OnSourceInitialized;

            Logger.Info("[MainWindow] Initialization complete, triggering initial device scan ...");

            // Scan for devices on startup (same as original OnInitDialog)
            _ = UpdateDeviceTimeAsync();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
            {
                hwndSource.AddHook(WindowProc);
                Logger.Info("[MainWindow] HwndSource hook registered for WM_DEVICECHANGE.");
            }
            else
            {
                Logger.Warn("[MainWindow] Failed to get HwndSource - WM_DEVICECHANGE will NOT be monitored!");
            }
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                uint wParamVal = (uint)wParam.ToInt64();
                Logger.Info("[MainWindow] WM_DEVICECHANGE: wParam=0x{0:X8} ({1})",
                    wParamVal, DescribeDeviceChangeEvent(wParamVal));

                // Only trigger on device arrival or removal
                if (wParamVal == 0x0001 || wParamVal == 0x0005 || wParamVal == 0x8000 || wParamVal == 0x0007)
                {
                    _ = UpdateDeviceTimeAsync();
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static string DescribeDeviceChangeEvent(uint wParam)
        {
            return wParam switch
            {
                0x0001 => "DBT_DEVICEARRIVAL",
                0x0002 => "DBT_DEVICEQUERYREMOVE",
                0x0003 => "DBT_DEVICEQUERYREMOVEFAILED",
                0x0004 => "DBT_DEVICEREMOVEPENDING",
                0x0005 => "DBT_DEVICEREMOVECOMPLETE",
                0x0006 => "DBT_DEVICETYPESPECIFIC",
                0x0007 => "DBT_CONFIGCHANGED",
                0x8000 => "DBT_DEVNODES_CHANGED",
                _ => $"0x{wParam:X8}"
            };
        }

        private void DisplayTime()
        {
            DateTime now = DateTime.Now;
            TimeDisplay.Text = now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            DisplayTime();
        }

        /// <summary>
        /// Updates status display with color-coded indicator and text.
        /// </summary>
        private void UpdateStatus(string text, string dotColorKey)
        {
            StatusDisplay.Text = text;
            StatusDot.Fill = (SolidColorBrush)FindResource(dotColorKey);
            StatusDot.ToolTip = text;
        }

        /// <summary>
        /// Sets the scanning UI state (progress bar + button states).
        /// </summary>
        private void SetScanningState(bool scanning)
        {
            _isScanning = scanning;
            ScanProgressBar.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
            ScanButton.IsEnabled = !scanning;
            AboutButton.IsEnabled = !scanning;
            ExitButton.IsEnabled = !scanning;
        }

        /// <summary>
        /// Updates the last scan timestamp display.
        /// </summary>
        private void UpdateLastScanTime()
        {
            LastScanDisplay.Text = $"Last scan: {DateTime.Now:HH:mm:ss}";
        }

        /// <summary>
        /// Scans for matching USB devices and updates their internal time.
        /// </summary>
        private async Task UpdateDeviceTimeAsync()
        {
            // Prevent re-entry while already scanning
            if (_isScanning)
            {
                Logger.Info("[MainWindow] Scan already in progress, skipping duplicate request.");
                return;
            }

            SetScanningState(true);
            UpdateStatus("Scanning for devices...", "StatusAmber");

            try
            {
                Logger.Info("[MainWindow] UpdateDeviceTime triggered.");
                var result = await _deviceService.UpdateDeviceTimeAsync();

                switch (result)
                {
                    case DeviceService.UpdateResult.NoDevice:
                        UpdateStatus("No device online...", "StatusGray");
                        Logger.Info("[MainWindow] Result: No matching device found.");
                        break;
                    case DeviceService.UpdateResult.Success:
                        UpdateStatus("Update device's time successful.", "StatusGreen");
                        Logger.Info("[MainWindow] Result: Device time update SUCCESSFUL.");
                        break;
                    case DeviceService.UpdateResult.Failed:
                        UpdateStatus("Update device's time fail.", "StatusRed");
                        Logger.Warn("[MainWindow] Result: Device time update FAILED.");
                        break;
                }

                UpdateLastScanTime();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}", "StatusRed");
                Logger.Error("[MainWindow] Exception during scan: {0}", ex.Message);
            }
            finally
            {
                SetScanningState(false);
            }
        }

        /// <summary>
        /// Manual scan button handler.
        /// </summary>
        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("[MainWindow] Scan Now button clicked by user.");
            await UpdateDeviceTimeAsync();
        }

        /// <summary>
        /// About button handler.
        /// </summary>
        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("[MainWindow] About button clicked.");
            var about = new AboutWindow();
            about.Owner = this;
            about.ShowDialog();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("[MainWindow] Exit button clicked, closing application.");
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            Logger.Info("[MainWindow] Application shutting down ...");
            _timer.Stop();
            _deviceService.Dispose();
            base.OnClosed(e);
            Logger.Info("[MainWindow] Application closed.");
        }
    }
}