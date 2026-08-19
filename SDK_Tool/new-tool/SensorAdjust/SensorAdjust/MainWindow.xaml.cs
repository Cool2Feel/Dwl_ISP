using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SensorAdjust.Models;
using SensorAdjust.Services;

namespace SensorAdjust
{
    public partial class MainWindow : Window
    {
        // ===== Routed Commands for Keyboard Shortcuts =====
        public static readonly RoutedCommand ReadCommand = new("Read", typeof(MainWindow));
        public static readonly RoutedCommand WriteCommand = new("Write", typeof(MainWindow));
        public static readonly RoutedCommand AddCommand = new("Add", typeof(MainWindow));
        public static readonly RoutedCommand SaveCommand = new("Save", typeof(MainWindow));
        public static readonly RoutedCommand DeleteCommand = new("Delete", typeof(MainWindow));
        public static readonly RoutedCommand UpdateCommand = new("Update", typeof(MainWindow));
        public static readonly RoutedCommand ConnectCommand = new("Connect", typeof(MainWindow));

        private readonly DeviceService _deviceService;
        private RegisterService? _registerService;
        private IntPtr _deviceHandle = IntPtr.Zero;
        private bool _isOperationInProgress;
        private bool _isConnecting;
        private CancellationTokenSource? _scanCts;
        private DeviceScanResult? _lastScanResult;
        private System.Windows.Threading.DispatcherTimer? _healthTimer;

        // ================================================================
        // Constructor
        // ================================================================

        public MainWindow()
        {
            Logger.Info("============================================");
            Logger.Info("[MainWindow] SensorAdjust starting ...");
            Logger.Info("============================================");

            InitializeComponent();
            _deviceService = new DeviceService();

            // Bind routed commands
            CommandBindings.Add(new CommandBinding(ReadCommand, (_, _) => _ = ExecuteReadAsync()));
            CommandBindings.Add(new CommandBinding(WriteCommand, (_, _) => _ = ExecuteWriteAsync()));
            CommandBindings.Add(new CommandBinding(AddCommand, (_, _) => ExecuteAdd()));
            CommandBindings.Add(new CommandBinding(SaveCommand, (_, _) => ExecuteSave()));
            CommandBindings.Add(new CommandBinding(DeleteCommand, (_, _) => ExecuteDeleteSelected()));
            CommandBindings.Add(new CommandBinding(UpdateCommand, (_, _) => ExecuteUpdate()));
            CommandBindings.Add(new CommandBinding(ConnectCommand, (_, _) => _ = ExecuteConnectAsync()));

            // Subscribe to WMI hot-plug events
            _deviceService.DeviceArrived += OnDeviceArrived;
            _deviceService.DeviceRemoved += OnDeviceRemoved;
            _deviceService.ScanProgress += OnScanProgress;

            // Initialize UI
            AddrTextBox.Text = "00";
            ValueTextBox.Text = "00";
            UpdateStatus("No device connected", "StatusGray");
            UpdateItemCount();
            UpdateDeviceInfo(null);
            ScanProgressBar.Visibility = Visibility.Collapsed;
            HealthIndicator.Visibility = Visibility.Collapsed;

            // Auto-connect on startup with a short delay
            _ = AutoConnectAsync();
        }

        // ================================================================
        // UI Update Helpers
        // ================================================================

        private void UpdateStatus(string text, string colorKey)
        {
            StatusDisplay.Text = text;
            StatusDot.Fill = (SolidColorBrush)FindResource(colorKey);
            StatusDot.ToolTip = text;
            StatusBarText.Text = text;
        }

        private void SetStatusBar(string text)
        {
            StatusBarText.Text = text;
        }

        private void UpdateItemCount()
        {
            ItemCountText.Text = $"{RegisterListBox.Items.Count} item{(RegisterListBox.Items.Count != 1 ? "s" : "")}";
        }

        private void UpdateDeviceInfo(DeviceScanResult? result)
        {
            if (result?.IsConnected == true)
            {
                DeviceInfoPanel.Visibility = Visibility.Visible;
                DeviceInfoText.Text = $"Drive: {result.DevicePath}  |  Vendor: {result.VendorId}  |  Product: {result.ProductId}";
                DeviceInfoText.ToolTip = $"Connected to: {result.DevicePath}\nVendor: {result.VendorId}\nProduct: {result.ProductId}";
            }
            else
            {
                DeviceInfoPanel.Visibility = Visibility.Collapsed;
                DeviceInfoText.Text = "";
                DeviceInfoText.ToolTip = null;
            }
        }

        private void SetInputsEnabled(bool enabled)
        {
            AddrTextBox.IsEnabled = enabled;
            ValueTextBox.IsEnabled = enabled;
            Addr16BitCheckBox.IsEnabled = enabled;
            Value16BitCheckBox.IsEnabled = enabled;
            ReadButton.IsEnabled = enabled;
            WriteButton.IsEnabled = enabled;
            AddButton.IsEnabled = enabled;
        }

        // ================================================================
        // Device Connection (Async with Progress)
        // ================================================================

        private async Task AutoConnectAsync()
        {
            try
            {
                await Task.Delay(300);
                if (!Dispatcher.HasShutdownStarted)
                    await Dispatcher.InvokeAsync(() => _ = ExecuteConnectAsync());
            }
            catch (Exception ex)
            {
                Logger.Warn("[MainWindow] Auto-connect delay interrupted: {0}", ex.Message);
            }
        }

        private async Task ExecuteConnectAsync()
        {
            if (_isConnecting) return;
            _isConnecting = true;

            // Cancel any previous scan
            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();

            // Close previous handle
            CloseDeviceHandle();

            // Show progress bar
            ScanProgressBar.Visibility = Visibility.Visible;
            ScanProgressBar.Value = 0;
            ConnectButton.IsEnabled = false;
            ConnectButton.Content = "Scanning...";
            SetInputsEnabled(false);
            UpdateStatus("Scanning for USB device ...", "StatusAmber");
            DeviceInfoPanel.Visibility = Visibility.Collapsed;

            try
            {
                var result = await _deviceService.FindFirstMatchingDeviceAsync(_scanCts.Token);
                _lastScanResult = result;

                if (result.IsConnected && result.Handle != IntPtr.Zero)
                {
                    _deviceHandle = result.Handle;
                    _registerService = new RegisterService(_deviceHandle);
                    UpdateDeviceInfo(result);
                    UpdateStatus("Device connected", "StatusGreen");
                    SetStatusBar($"Device connected on {result.DevicePath}. Ready to read/write registers.");
                    SetInputsEnabled(true);
                    StartHealthCheck();
                }
                else
                {
                    string errorMsg = !string.IsNullOrEmpty(result.ErrorMessage)
                        ? result.ErrorMessage
                        : "No matching Buildwin/AX3231MP device found.";
                    UpdateStatus("No device connected", "StatusGray");
                    SetStatusBar($"{errorMsg} Click Connect or plug in device to retry.");
                    SetInputsEnabled(false);
                    StopHealthCheck();
                }
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Scan cancelled", "StatusGray");
                SetStatusBar("Device scan was cancelled.");
            }
            catch (Exception ex)
            {
                Logger.Error("[MainWindow] Connection error: {0}", ex.Message);
                UpdateStatus("Connection error", "Warning");
                SetStatusBar($"Connection error: {ex.Message}");
                SetInputsEnabled(false);
            }
            finally
            {
                ScanProgressBar.Visibility = Visibility.Collapsed;
                ConnectButton.IsEnabled = true;
                ConnectButton.Content = "Connect";
                _isConnecting = false;
            }
        }

        private void OnScanProgress(object? sender, DeviceScanProgress progress)
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.Invoke(() =>
                {
                    ScanProgressBar.Value = progress.Percentage;
                    SetStatusBar($"Scanning... {progress.CurrentDrive}/{progress.TotalDrives} ({progress.Status})");
                });
            }
        }

        // ================================================================
        // Hot-Plug Event Handling
        // ================================================================

        private void OnDeviceArrived(object? sender, EventArgs e)
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.Invoke(async () =>
                {
                    // If we're already connected, no need to re-scan
                    if (_deviceHandle != IntPtr.Zero)
                    {
                        // Quick check if it's actually our device that arrived
                        Logger.Info("[MainWindow] Device arrived, but already connected.");
                        return;
                    }

                    Logger.Info("[MainWindow] Device arrival detected. Auto-connecting...");
                    await ExecuteConnectAsync();
                });
            }
        }

        private void OnDeviceRemoved(object? sender, EventArgs e)
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.Invoke(() =>
                {
                    // Only react if we had a device connected
                    if (_deviceHandle == IntPtr.Zero) return;

                    Logger.Info("[MainWindow] Device removal detected. Disconnecting...");
                    CloseDeviceHandle();
                    SetInputsEnabled(false);
                    UpdateStatus("Device disconnected", "Warning");
                    SetStatusBar("Device was removed. Plug it back in for auto-reconnect, or click Connect.");
                    StopHealthCheck();
                });
            }
        }

        // ================================================================
        // Connection Health Check
        // ================================================================

        private void StartHealthCheck()
        {
            StopHealthCheck();
            _healthTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _healthTimer.Tick += (_, _) => PerformHealthCheck();
            _healthTimer.Start();
            HealthIndicator.Visibility = Visibility.Visible;
            HealthIndicatorText.Text = "Monitoring...";
            HealthIndicatorColor.Fill = (SolidColorBrush)FindResource("StatusGreen");
            Logger.Info("[MainWindow] Health check started (interval: 5s).");
        }

        private void StopHealthCheck()
        {
            if (_healthTimer != null)
            {
                _healthTimer.Stop();
                _healthTimer = null;
            }
            HealthIndicator.Visibility = Visibility.Collapsed;
        }

        private void PerformHealthCheck()
        {
            if (_deviceHandle == IntPtr.Zero || _registerService == null)
            {
                SetInputsEnabled(false);
                UpdateStatus("Device disconnected", "Warning");
                HealthIndicatorColor.Fill = (SolidColorBrush)FindResource("StatusGray");
                HealthIndicatorText.Text = "Disconnected";
                StopHealthCheck();
                return;
            }

            bool handleValid = DeviceService.IsHandleValid(_deviceHandle);
            if (handleValid)
            {
                HealthIndicatorColor.Fill = (SolidColorBrush)FindResource("StatusGreen");
                HealthIndicatorText.Text = "OK";
            }
            else
            {
                Logger.Warn("[MainWindow] Health check: device handle is invalid.");
                CloseDeviceHandle();
                SetInputsEnabled(false);
                UpdateStatus("Device disconnected", "Warning");
                SetStatusBar("Device connection lost. Click Connect to reconnect.");
                HealthIndicatorColor.Fill = (SolidColorBrush)FindResource("StatusRed");
                HealthIndicatorText.Text = "Lost";
                StopHealthCheck();
            }
        }

        // ================================================================
        // Device Handle Management
        // ================================================================

        private void CloseDeviceHandle()
        {
            if (_deviceHandle != IntPtr.Zero)
            {
                NativeMethods.NativeMethods.CloseHandle(_deviceHandle);
                _deviceHandle = IntPtr.Zero;
                _registerService = null;
            }
            UpdateDeviceInfo(null);
            StopHealthCheck();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ExecuteConnectAsync();
        }

        // ================================================================
        // Hex Input Validation
        // ================================================================

        private void AddrTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ValidateHexInput(AddrTextBox, Addr16BitCheckBox.IsChecked == true ? 4 : 2);
        }

        private void ValueTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ValidateHexInput(ValueTextBox, Value16BitCheckBox.IsChecked == true ? 4 : 2);
        }

        private void ValidateHexInput(System.Windows.Controls.TextBox textBox, int maxLength)
        {
            string text = textBox.Text.ToUpper();

            if (text.Length > maxLength)
            {
                text = text.Substring(0, maxLength);
                textBox.Text = text;
                textBox.CaretIndex = text.Length;
                return;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))
                {
                    text = text.Remove(i, 1);
                    textBox.Text = text;
                    textBox.CaretIndex = text.Length;
                    return;
                }
            }
        }

        private void ClearInputButton_Click(object sender, RoutedEventArgs e)
        {
            AddrTextBox.Text = Addr16BitCheckBox.IsChecked == true ? "0000" : "00";
            ValueTextBox.Text = Value16BitCheckBox.IsChecked == true ? "0000" : "00";
            AddrTextBox.Focus();
        }

        // ================================================================
        // Bit-width Checkboxes
        // ================================================================

        private void Addr16BitCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool is16Bit = Addr16BitCheckBox.IsChecked == true;
            string addr = AddrTextBox.Text;

            if (!is16Bit)
            {
                if (addr.Length > 2)
                    addr = addr.Substring(addr.Length - 2, 2);
            }
            else
            {
                addr = "00" + addr;
                if (addr.Length > 4)
                    addr = addr.Substring(0, 4);
            }

            AddrTextBox.Text = addr;
            AddrTextBox.MaxLength = is16Bit ? 4 : 2;
            UpdateListBoxAddrWidth(is16Bit);
        }

        private void Value16BitCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool is16Bit = Value16BitCheckBox.IsChecked == true;
            string value = ValueTextBox.Text;

            if (!is16Bit)
            {
                if (value.Length > 2)
                    value = value.Substring(value.Length - 2, 2);
            }
            else
            {
                value = "00" + value;
                if (value.Length > 4)
                    value = value.Substring(0, 4);
            }

            ValueTextBox.Text = value;
            ValueTextBox.MaxLength = is16Bit ? 4 : 2;
            UpdateListBoxValueWidth(is16Bit);
        }

        private void UpdateListBoxAddrWidth(bool is16Bit)
        {
            foreach (RegisterEntry entry in RegisterListBox.Items)
            {
                if (is16Bit)
                {
                    if (entry.Address.Length < 4)
                        entry.Address = entry.Address.PadLeft(4, '0');
                }
                else
                {
                    if (entry.Address.Length > 2)
                        entry.Address = entry.Address.Substring(entry.Address.Length - 2, 2);
                }
            }
        }

        private void UpdateListBoxValueWidth(bool is16Bit)
        {
            foreach (RegisterEntry entry in RegisterListBox.Items)
            {
                if (is16Bit)
                {
                    if (entry.Value.Length < 4)
                        entry.Value = entry.Value.PadLeft(4, '0');
                }
                else
                {
                    if (entry.Value.Length > 2)
                        entry.Value = entry.Value.Substring(entry.Value.Length - 2, 2);
                }
            }
        }

        // ================================================================
        // ListBox Selection & Interaction
        // ================================================================

        private void RegisterListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (RegisterListBox.SelectedItem is RegisterEntry selectedEntry &&
                RegisterListBox.SelectedItems.Count == 1)
            {
                AddrTextBox.Text = selectedEntry.Address;
                ValueTextBox.Text = selectedEntry.Value;
            }
        }

        private void RegisterListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RegisterListBox.SelectedItem != null)
            {
                AddrTextBox.Focus();
                AddrTextBox.SelectAll();
            }
        }

        // ================================================================
        // Read Operation
        // ================================================================

        private async Task ExecuteReadAsync()
        {
            if (_isOperationInProgress) return;

            if (string.IsNullOrEmpty(AddrTextBox.Text))
            {
                SetStatusBar("Cannot read: address field is empty.");
                return;
            }

            if (_registerService == null)
            {
                SetStatusBar("Cannot read: device not connected. Click Connect first.");
                return;
            }

            uint addr = RegisterService.HexStringToValue(AddrTextBox.Text);
            if (addr == 0xFFFFFFFF)
            {
                SetStatusBar("Cannot read: invalid address format.");
                return;
            }

            _isOperationInProgress = true;
            SetStatusBar($"Reading register at 0x{AddrTextBox.Text} ...");

            try
            {
                // Single selection
                if (RegisterListBox.SelectedItems.Count == 1 && RegisterListBox.SelectedItem is RegisterEntry singleEntry)
                {
                    uint readValue = 0;
                    bool result = await Task.Run(() => _registerService.ReadRegister(addr, out readValue));
                    if (result)
                    {
                        bool is16BitValue = Value16BitCheckBox.IsChecked == true;
                        ValueTextBox.Text = is16BitValue ? readValue.ToString("X4") : readValue.ToString("X2");
                        singleEntry.Value = ValueTextBox.Text;
                        SetStatusBar($"Read: addr=0x{AddrTextBox.Text} �?value=0x{ValueTextBox.Text}");
                    }
                    else
                    {
                        SetStatusBar("Read failed: device not responding.");
                        await HandleDeviceErrorAsync();
                    }
                }
                // Multi-selection
                else if (RegisterListBox.SelectedItems.Count > 1)
                {
                    int successCount = 0;
                    foreach (RegisterEntry entry in RegisterListBox.SelectedItems)
                    {
                        uint entryAddr = RegisterService.HexStringToValue(entry.Address);
                        if (entryAddr == 0xFFFFFFFF) continue;

                        uint readValue = 0;
                        bool result = await Task.Run(() => _registerService.ReadRegister(entryAddr, out readValue));
                        if (result)
                        {
                            bool is16BitValue = Value16BitCheckBox.IsChecked == true;
                            entry.Value = is16BitValue ? readValue.ToString("X4") : readValue.ToString("X2");
                            successCount++;
                        }
                        else
                        {
                            SetStatusBar("Read failed: device not responding during batch read.");
                            await HandleDeviceErrorAsync();
                            break;
                        }
                    }
                    if (successCount > 0)
                        SetStatusBar($"Batch read complete: {successCount} register(s) updated.");
                }
                else
                {
                    SetStatusBar("Select an item in the list to read, or use the address field directly.");
                }
            }
            finally
            {
                _isOperationInProgress = false;
            }
        }

        private void ReadButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ExecuteReadAsync();
        }

        // ================================================================
        // Write Operation
        // ================================================================

        private async Task ExecuteWriteAsync()
        {
            if (_isOperationInProgress) return;

            if (string.IsNullOrEmpty(AddrTextBox.Text) || string.IsNullOrEmpty(ValueTextBox.Text))
            {
                SetStatusBar("Cannot write: address or value field is empty.");
                return;
            }

            if (_registerService == null)
            {
                SetStatusBar("Cannot write: device not connected. Click Connect first.");
                return;
            }

            uint addr = RegisterService.HexStringToValue(AddrTextBox.Text);
            uint value = RegisterService.HexStringToValue(ValueTextBox.Text);

            if (addr == 0xFFFFFFFF || value == 0xFFFFFFFF)
            {
                SetStatusBar("Cannot write: invalid address or value format.");
                return;
            }

            _isOperationInProgress = true;
            SetStatusBar($"Writing register at 0x{AddrTextBox.Text} = 0x{ValueTextBox.Text} ...");

            try
            {
                // Single selection
                if (RegisterListBox.SelectedItems.Count == 1 && RegisterListBox.SelectedItem is RegisterEntry singleEntry)
                {
                    bool result = await Task.Run(() => _registerService.WriteRegister(addr, value));
                    if (result)
                    {
                        bool is16BitAddr = Addr16BitCheckBox.IsChecked == true;
                        bool is16BitValue = Value16BitCheckBox.IsChecked == true;

                        AddrTextBox.Text = is16BitAddr ? addr.ToString("X4") : addr.ToString("X2");
                        ValueTextBox.Text = is16BitValue ? value.ToString("X4") : value.ToString("X2");

                        singleEntry.Address = AddrTextBox.Text;
                        singleEntry.Value = ValueTextBox.Text;

                        SetStatusBar($"Write: addr=0x{AddrTextBox.Text} �?value=0x{ValueTextBox.Text} (success)");
                    }
                    else
                    {
                        SetStatusBar("Write failed: device not responding.");
                        await HandleDeviceErrorAsync();
                    }
                }
                // Multi-selection
                else if (RegisterListBox.SelectedItems.Count > 1)
                {
                    int successCount = 0;
                    foreach (RegisterEntry entry in RegisterListBox.SelectedItems)
                    {
                        uint entryAddr = RegisterService.HexStringToValue(entry.Address);
                        uint entryValue = RegisterService.HexStringToValue(entry.Value);

                        if (entryAddr == 0xFFFFFFFF || entryValue == 0xFFFFFFFF) continue;

                        bool result = await Task.Run(() => _registerService.WriteRegister(entryAddr, entryValue));
                        if (result)
                        {
                            bool is16BitAddr = Addr16BitCheckBox.IsChecked == true;
                            bool is16BitValue = Value16BitCheckBox.IsChecked == true;

                            entry.Address = is16BitAddr ? entryAddr.ToString("X4") : entryAddr.ToString("X2");
                            entry.Value = is16BitValue ? entryValue.ToString("X4") : entryValue.ToString("X2");
                            successCount++;
                        }
                        else
                        {
                            SetStatusBar("Write failed: device not responding during batch write.");
                            await HandleDeviceErrorAsync();
                            break;
                        }
                    }
                    if (successCount > 0)
                        SetStatusBar($"Batch write complete: {successCount} register(s) written.");
                }
                else
                {
                    SetStatusBar("Select an item in the list to write, or use the address field directly.");
                }
            }
            finally
            {
                _isOperationInProgress = false;
            }
        }

        private void WriteButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ExecuteWriteAsync();
        }

        // ================================================================
        // Device Error Handling
        // ================================================================

        private async Task HandleDeviceErrorAsync()
        {
            // Check if device handle is still valid
            bool handleValid = await Task.Run(() => DeviceService.IsHandleValid(_deviceHandle));
            if (!handleValid)
            {
                Logger.Warn("[MainWindow] Device handle invalid after operation error. Reconnecting...");
                UpdateStatus("Device error", "Warning");
                await ExecuteConnectAsync();
            }
        }

        // ================================================================
        // Add Entry
        // ================================================================

        private void ExecuteAdd()
        {
            string addr = AddrTextBox.Text;
            string value = ValueTextBox.Text;

            // Pad address
            if (string.IsNullOrEmpty(addr))
            {
                addr = Addr16BitCheckBox.IsChecked == true ? "0000" : "00";
            }
            else if (addr.Length == 1)
            {
                addr = Addr16BitCheckBox.IsChecked == true ? "000" + addr : "0" + addr;
            }
            else if (addr.Length == 2 && Addr16BitCheckBox.IsChecked == true)
            {
                addr = "00" + addr;
            }
            else if (addr.Length == 3 && Addr16BitCheckBox.IsChecked == true)
            {
                addr = "0" + addr;
            }

            // Pad value
            if (string.IsNullOrEmpty(value))
            {
                value = Value16BitCheckBox.IsChecked == true ? "0000" : "00";
            }
            else if (value.Length == 1)
            {
                value = Value16BitCheckBox.IsChecked == true ? "000" + value : "0" + value;
            }
            else if (value.Length == 2 && Value16BitCheckBox.IsChecked == true)
            {
                value = "00" + value;
            }
            else if (value.Length == 3 && Value16BitCheckBox.IsChecked == true)
            {
                value = "0" + value;
            }

            var entry = new RegisterEntry
            {
                Address = addr,
                Value = value,
                IsSelected = true
            };

            foreach (RegisterEntry item in RegisterListBox.Items)
            {
                item.IsSelected = false;
            }

            RegisterListBox.Items.Add(entry);
            RegisterListBox.SelectedItem = entry;
            RegisterListBox.ScrollIntoView(entry);

            UpdateItemCount();
            SetStatusBar($"Added: addr=0x{addr}, value=0x{value} (total: {RegisterListBox.Items.Count} items)");
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteAdd();
        }

        // ================================================================
        // Update Entry
        // ================================================================

        private void ExecuteUpdate()
        {
            if (RegisterListBox.SelectedItems.Count != 1)
            {
                SetStatusBar("Select exactly one item to update.");
                return;
            }

            if (RegisterListBox.SelectedItem is not RegisterEntry entry)
                return;

            string addr = AddrTextBox.Text;
            string value = ValueTextBox.Text;

            // Pad address
            if (string.IsNullOrEmpty(addr))
            {
                SetStatusBar("Cannot update: address field is empty.");
                return;
            }
            addr = Addr16BitCheckBox.IsChecked == true ? addr.PadLeft(4, '0') : addr.PadLeft(2, '0');

            // Pad value
            if (string.IsNullOrEmpty(value))
            {
                SetStatusBar("Cannot update: value field is empty.");
                return;
            }
            value = Value16BitCheckBox.IsChecked == true ? value.PadLeft(4, '0') : value.PadLeft(2, '0');

            entry.Address = addr;
            entry.Value = value;

            SetStatusBar($"Updated: addr=0x{addr}, value=0x{value}");
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteUpdate();
        }

        // ================================================================
        // Delete Operations
        // ================================================================

        private void ExecuteDeleteSelected()
        {
            if (RegisterListBox.SelectedItems.Count == 0)
            {
                SetStatusBar("No items selected to delete.");
                return;
            }

            int count = RegisterListBox.SelectedItems.Count;
            var itemsToRemove = RegisterListBox.SelectedItems.Cast<RegisterEntry>().ToList();
            foreach (var item in itemsToRemove)
            {
                RegisterListBox.Items.Remove(item);
            }

            UpdateItemCount();
            SetStatusBar($"Deleted {count} item(s). Remaining: {RegisterListBox.Items.Count} items.");
        }

        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDeleteSelected();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (RegisterListBox.Items.Count == 0)
            {
                SetStatusBar("List is already empty.");
                return;
            }

            var result = MessageBox.Show(
                "Are you sure you want to clear all items from the list?",
                "Clear All Items",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                RegisterListBox.Items.Clear();
                UpdateItemCount();
                SetStatusBar("All items cleared.");
            }
        }

        // ================================================================
        // Save to File
        // ================================================================

        private void ExecuteSave()
        {
            if (RegisterListBox.Items.Count == 0)
            {
                SetStatusBar("Nothing to save. List is empty.");
                return;
            }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = "setting",
                DefaultExt = "txt"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using var writer = new System.IO.StreamWriter(saveDialog.FileName, false);
                    foreach (RegisterEntry entry in RegisterListBox.Items)
                    {
                        writer.WriteLine(entry.DisplayText);
                    }
                    SetStatusBar($"Saved {RegisterListBox.Items.Count} entries to {saveDialog.FileName}");
                    Logger.Info("[MainWindow] Saved {0} entries to {1}", RegisterListBox.Items.Count, saveDialog.FileName);
                }
                catch (Exception ex)
                {
                    SetStatusBar($"Save failed: {ex.Message}");
                    MessageBox.Show($"Save failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteSave();
        }

        // ================================================================
        // Load from File
        // ================================================================

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = "txt"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    var lines = System.IO.File.ReadAllLines(openDialog.FileName);
                    int loadedCount = 0;

                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;

                        int addrPos = trimmed.IndexOf("addr: 0x");
                        int valuePos = trimmed.IndexOf("value: 0x");

                        if (addrPos >= 0 && valuePos >= 0)
                        {
                            string addrPart = trimmed.Substring(addrPos + 8, valuePos - addrPos - 9).Trim();
                            string valuePart = trimmed.Substring(valuePos + 9).Trim();

                            if (!string.IsNullOrEmpty(addrPart) && !string.IsNullOrEmpty(valuePart))
                            {
                                var entry = new RegisterEntry
                                {
                                    Address = addrPart.ToUpper(),
                                    Value = valuePart.ToUpper()
                                };
                                RegisterListBox.Items.Add(entry);
                                loadedCount++;
                            }
                        }
                    }

                    UpdateItemCount();
                    SetStatusBar($"Loaded {loadedCount} entries from {openDialog.FileName}");
                }
                catch (Exception ex)
                {
                    SetStatusBar($"Load failed: {ex.Message}");
                    MessageBox.Show($"Load failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ================================================================
        // Context Menu Handlers
        // ================================================================

        private void ContextMenu_ReadSelected(object sender, RoutedEventArgs e) => _ = ExecuteReadAsync();
        private void ContextMenu_WriteSelected(object sender, RoutedEventArgs e) => _ = ExecuteWriteAsync();
        private void ContextMenu_UpdateSelected(object sender, RoutedEventArgs e) => ExecuteUpdate();
        private void ContextMenu_DeleteSelected(object sender, RoutedEventArgs e) => ExecuteDeleteSelected();
        private void ContextMenu_ClearAll(object sender, RoutedEventArgs e) => ClearAllButton_Click(sender, e);
        private void ContextMenu_Save(object sender, RoutedEventArgs e) => ExecuteSave();
        private void ContextMenu_Load(object sender, RoutedEventArgs e) => LoadButton_Click(sender, e);

        // ================================================================
        // About
        // ================================================================

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow();
            about.Owner = this;
            about.ShowDialog();
        }

        // ================================================================
        // Cleanup
        // ================================================================

        protected override void OnClosed(EventArgs e)
        {
            Logger.Info("[MainWindow] Application shutting down ...");
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            StopHealthCheck();
            CloseDeviceHandle();
            _deviceService.Dispose();
            base.OnClosed(e);
            Logger.Info("[MainWindow] Application closed.");
        }
    }
}