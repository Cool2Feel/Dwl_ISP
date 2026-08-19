using ResBinManager.Core;
using ResBinManager.Views;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ResBinManager.ViewModels
{
    /// <summary>
    /// 时间同步页面 ViewModel
    /// 参考 TimeUpdate 项目的自动检测+自动同步模式优化
    /// </summary>
    public class TimeSyncViewModel : INotifyPropertyChanged, IDisposable
    {
        #region 私有字段

        private readonly UsbMscService _usbService;
        private string _connectionStatus = "未连接";
        private string _deviceName = "未检测到设备";
        private string _deviceTimeDisplay = "--";
        private string _pcTimeDisplay = "--";
        private string _timeDifferenceDisplay = "--";
        private string _logText = "";
        private string _devicePath = string.Empty;
        private string _lastSyncTime = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isConnected;
        private bool _isScanning;
        private bool _isSyncing;
        private bool _autoSyncEnabled;
        private bool _isAutoSyncing;
        private bool _isDisposed;
        private System.Windows.Threading.DispatcherTimer? _pcTimeTimer;
        private System.Windows.Threading.DispatcherTimer? _autoSyncTimer;

        // 定时器间隔常量
        private const int AutoSyncIntervalSec = 60;        // 自动同步间隔（秒）

        #endregion

        #region 公共属性

        /// <summary>
        /// 连接状态文本
        /// </summary>
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set { _connectionStatus = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName
        {
            get => _deviceName;
            set { _deviceName = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 设备时间显示
        /// </summary>
        public string DeviceTimeDisplay
        {
            get => _deviceTimeDisplay;
            set { _deviceTimeDisplay = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// PC时间显示
        /// </summary>
        public string PcTimeDisplay
        {
            get => _pcTimeDisplay;
            set { _pcTimeDisplay = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 时间差显示
        /// </summary>
        public string TimeDifferenceDisplay
        {
            get => _timeDifferenceDisplay;
            set { _timeDifferenceDisplay = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 日志文本
        /// </summary>
        public string LogText
        {
            get => _logText;
            set { _logText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 设备路径
        /// </summary>
        public string DevicePath
        {
            get => _devicePath;
            set { _devicePath = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 最后同步时间
        /// </summary>
        public string LastSyncTime
        {
            get => _lastSyncTime;
            set { _lastSyncTime = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 状态消息（底部显示）
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 设备是否已连接
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
                UpdateCanExecuteStates();
            }
        }

        /// <summary>
        /// 是否正在扫描
        /// </summary>
        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                _isScanning = value;
                OnPropertyChanged();
                UpdateCanExecuteStates();
            }
        }

        /// <summary>
        /// 是否正在同步
        /// </summary>
        public bool IsSyncing
        {
            get => _isSyncing;
            set
            {
                _isSyncing = value;
                OnPropertyChanged();
                UpdateCanExecuteStates();
            }
        }

        /// <summary>
        /// 自动同步是否启用
        /// </summary>
        public bool AutoSyncEnabled
        {
            get => _autoSyncEnabled;
            set
            {
                _autoSyncEnabled = value;
                OnPropertyChanged();
                if (_autoSyncEnabled)
                    StartAutoSyncTimer();
                else
                    StopAutoSyncTimer();
            }
        }

        /// <summary>
        /// 可以连接
        /// </summary>
        public bool CanConnect => !IsConnected && !IsScanning;

        /// <summary>
        /// 可以同步时间
        /// </summary>
        public bool CanSyncTime => IsConnected && !IsSyncing;

        /// <summary>
        /// 可以断开连接
        /// </summary>
        public bool CanDisconnect => IsConnected;

        #endregion

        #region 命令

        /// <summary>
        /// 扫描设备命令
        /// </summary>
        public ICommand ScanDeviceCommand { get; }

        /// <summary>
        /// 连接设备命令
        /// </summary>
        public ICommand ConnectDeviceCommand { get; }

        /// <summary>
        /// 同步时间命令
        /// </summary>
        public ICommand SyncTimeCommand { get; }

        /// <summary>
        /// 手动设置时间命令
        /// </summary>
        public ICommand ManualSetTimeCommand { get; }

        /// <summary>
        /// 断开设备命令
        /// </summary>
        public ICommand DisconnectDeviceCommand { get; }

        #endregion

        #region 构造函数

        public TimeSyncViewModel()
        {
            _usbService = new UsbMscService();

            // 初始化命令
            ScanDeviceCommand = new RelayCommand(ExecuteScanDevice, CanExecuteScanDevice);
            ConnectDeviceCommand = new RelayCommand(ExecuteConnectDevice, CanExecuteConnectDevice);
            SyncTimeCommand = new RelayCommand(ExecuteSyncTime, CanExecuteSyncTime);
            ManualSetTimeCommand = new RelayCommand(ExecuteManualSetTime, CanExecuteManualSetTime);
            DisconnectDeviceCommand = new RelayCommand(ExecuteDisconnectDevice, _ => IsConnected);

            // 初始化显示
            PcTimeDisplay = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            DeviceTimeDisplay = "--";
            DeviceName = "未检测到设备";
            ConnectionStatus = "未连接";
            LogText = "等待用户操作...\n";

            // 启动PC时间更新定时器（1秒间隔，参考TimeUpdate项目）
            StartPcTimeUpdateTimer();

            // 自动检测设备并同步（参考TimeUpdate项目OnInitDialog行为）
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                AddLog("启动自动检测...");
                AutoDetectAndSync();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        #endregion

        #region 命令实现

        /// <summary>
        /// 手动扫描设备
        /// </summary>
        private async void ExecuteScanDevice(object? parameter)
        {
            IsScanning = true;
            AddLog("正在扫描USB设备...");
            ConnectionStatus = "扫描中...";

            try
            {
                bool connected = await Task.Run(() => _usbService.Connect());

                if (connected)
                {
                    IsConnected = true;
                    DevicePath = _usbService.DeviceInfo;
                    DeviceName = _usbService.DeviceName ?? "HM020F Device";
                    ConnectionStatus = "已连接";
                    AddLog($"设备已连接: {DeviceName}");
                    AddLog($"  路径: {DevicePath}");
                }
                else
                {
                    IsConnected = false;
                    DeviceName = "未检测到设备";
                    ConnectionStatus = "未连接";
                    AddLog("未找到MSC设备。请确保设备已连接并处于MSC模式。");
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                DeviceName = "连接失败";
                ConnectionStatus = "错误";
                AddLog($"扫描失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[TimeSync] Scan error: {ex}");
            }
            finally
            {
                IsScanning = false;
            }
        }

        private bool CanExecuteScanDevice(object? parameter) => !IsScanning;

        /// <summary>
        /// 连接设备（委托给扫描命令）
        /// </summary>
        private void ExecuteConnectDevice(object? parameter)
        {
            ExecuteScanDevice(parameter);
        }

        private bool CanExecuteConnectDevice(object? parameter) => !IsConnected && !IsScanning;

        /// <summary>
        /// 手动同步时间
        /// </summary>
        private async void ExecuteSyncTime(object? parameter)
        {
            if (!IsConnected)
            {
                AddLog("请先连接设备");
                return;
            }

            IsSyncing = true;
            AddLog("正在同步时间到设备...");

            try
            {
                bool success = await Task.Run(() => _usbService.SyncPcTimeToDevice());

                if (success)
                {
                    LastSyncTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    DeviceTimeDisplay = LastSyncTime;
                    AddLog($"时间同步成功: {LastSyncTime}");
                }
                else
                {
                    AddLog("时间同步失败。请检查设备连接状态。");
                }
            }
            catch (Exception ex)
            {
                AddLog($"同步异常: {ex.Message}");
            }
            finally
            {
                IsSyncing = false;
            }
        }

        private bool CanExecuteSyncTime(object? parameter) => IsConnected && !IsSyncing;

        /// <summary>
        /// 手动设置时间
        /// </summary>
        private void ExecuteManualSetTime(object? parameter)
        {
            if (!IsConnected)
            {
                AddLog("请先连接设备");
                return;
            }

            var inputDialog = new InputDialog("输入时间戳", "请输入Unix时间戳（秒）：", DateTimeOffset.Now.ToUnixTimeSeconds().ToString());
            inputDialog.Owner = Application.Current.MainWindow;

            if (inputDialog.ShowDialog() == true && long.TryParse(inputDialog.InputText, out long timestamp))
            {
                AddLog($"正在设置时间戳: {timestamp}");

                try
                {
                    bool success = _usbService.SyncTimeByTimestamp(timestamp);
                    if (success)
                    {
                        var time = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                        DeviceTimeDisplay = time.ToString("yyyy-MM-dd HH:mm:ss");
                        LastSyncTime = DeviceTimeDisplay;
                        AddLog($"时间设置成功: {DeviceTimeDisplay}");
                    }
                    else
                    {
                        AddLog("时间设置失败");
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"设置异常: {ex.Message}");
                }
            }
        }

        private bool CanExecuteManualSetTime(object? parameter) => IsConnected && !IsSyncing;

        /// <summary>
        /// 断开设备连接
        /// </summary>
        private void ExecuteDisconnectDevice(object? parameter)
        {
            AddLog("正在断开设备...");
            HandleDeviceDisconnected();
            StatusMessage = "设备已断开";
        }

        #endregion

        #region 自动检测与同步

        /// <summary>
        /// 自动检测设备并同步时间（参考TimeUpdate项目的UpdateDeviceTime）
        /// 已连接时仅同步时间，不重复扫描设备
        /// </summary>
        private async void AutoDetectAndSync()
        {
            if (_isAutoSyncing || _isScanning || _isSyncing)
                return;

            _isAutoSyncing = true;

            try
            {
                // 已连接则直接同步，不重新扫描
                if (!IsConnected)
                {
                    bool connected = await Task.Run(() => _usbService.Connect());
                    if (!connected)
                        return;

                    IsConnected = true;
                    DevicePath = _usbService.DeviceInfo;
                    DeviceName = _usbService.DeviceName ?? "HM020F Device";
                    ConnectionStatus = "已连接";
                    AddLog($"设备已连接: {DeviceName}");
                }

                // 同步时间到设备
                bool success = await Task.Run(() => _usbService.SyncPcTimeToDevice());
                if (success)
                {
                    LastSyncTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    DeviceTimeDisplay = LastSyncTime;
                    AddLog($"时间同步成功: {LastSyncTime}");
                }
                else
                {
                    AddLog("时间同步失败");
                    // 同步失败可能是设备已断开
                    HandleDeviceDisconnected();
                }
            }
            catch (Exception ex)
            {
                AddLog($"自动检测异常: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[TimeSync] AutoDetect error: {ex}");
                // 异常可能是设备断开导致
                if (IsConnected)
                    HandleDeviceDisconnected();
            }
            finally
            {
                _isAutoSyncing = false;
            }
        }

        /// <summary>
        /// 处理设备断开
        /// </summary>
        private void HandleDeviceDisconnected()
        {
            IsConnected = false;
            DeviceName = "设备已断开";
            ConnectionStatus = "未连接";
            DeviceTimeDisplay = "--";
            TimeDifferenceDisplay = "--";
            AddLog("设备已断开连接");
            try { _usbService.Disconnect(); } catch { }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 启动PC时间更新定时器（1秒间隔，参考TimeUpdate项目）
        /// </summary>
        private void StartPcTimeUpdateTimer()
        {
            _pcTimeTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _pcTimeTimer.Tick += (s, e) =>
            {
                PcTimeDisplay = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                UpdateTimeDifference();
            };
            _pcTimeTimer.Start();
        }

        /// <summary>
        /// 响应设备热插拔事件 (WM_DEVICECHANGE)，实现即时检测
        /// 由 TimeSyncWindow 的 HwndSource 钩子 (DeviceChangeHook) 调用
        /// 已连接时先校验设备是否仍可用（处理拔出），否则触发扫描
        /// </summary>
        public void OnDeviceChange()
        {
            if (_isDisposed || _isScanning || _isSyncing || _isAutoSyncing)
                return;

            try
            {
                if (IsConnected)
                {
                    // 直接校验已连接设备的句柄是否有效，避免依赖枚举
                    bool present = Task.Run(() => _usbService.IsConnectedDevicePresent()).Result;
                    if (!present)
                    {
                        AddLog("检测到设备已拔出");
                        HandleDeviceDisconnected();
                    }
                    return;
                }

                // 未连接时，检测目标设备是否插入
                bool available = Task.Run(() => _usbService.IsTargetDeviceAvailable()).Result;
                if (available)
                {
                    AddLog("检测到设备插入，自动连接...");
                    AutoDetectAndSync();
                }
            }
            catch
            {
                // 静默处理监控异常，避免刷屏
            }
        }

        /// <summary>
        /// 启动自动同步定时器（周期性重同步）
        /// </summary>
        private void StartAutoSyncTimer()
        {
            StopAutoSyncTimer();
            _autoSyncTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(AutoSyncIntervalSec)
            };
            _autoSyncTimer.Tick += (s, e) => AutoDetectAndSync();
            _autoSyncTimer.Start();
            AddLog($"已启用自动同步（每{AutoSyncIntervalSec}秒）");

            // 立即执行一次同步
            if (IsConnected)
            {
                AutoDetectAndSync();
            }
        }

        /// <summary>
        /// 停止自动同步定时器
        /// </summary>
        private void StopAutoSyncTimer()
        {
            if (_autoSyncTimer != null)
            {
                _autoSyncTimer.Stop();
                _autoSyncTimer = null;
                AddLog("已停止自动同步");
            }
        }

        /// <summary>
        /// 更新时间差显示
        /// </summary>
        private void UpdateTimeDifference()
        {
            if (!string.IsNullOrEmpty(DeviceTimeDisplay) && DeviceTimeDisplay != "--")
            {
                if (DateTime.TryParse(DeviceTimeDisplay, out DateTime deviceTime))
                {
                    var diff = DateTime.Now - deviceTime;
                    var absDiff = diff.Duration();

                    if (absDiff.TotalSeconds < 1)
                        TimeDifferenceDisplay = "同步";
                    else if (absDiff.TotalSeconds < 60)
                        TimeDifferenceDisplay = $"{(int)absDiff.TotalSeconds}秒 {(diff.TotalSeconds > 0 ? "领先" : "落后")}";
                    else
                        TimeDifferenceDisplay = $"{absDiff.Minutes}分{absDiff.Seconds}秒 {(diff.TotalSeconds > 0 ? "领先" : "落后")}";
                }
            }
        }

        /// <summary>
        /// 添加日志
        /// </summary>
        private void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            LogText += $"[{timestamp}] {message}\n";

            // 限制日志长度
            if (LogText.Length > 5000)
            {
                LogText = LogText.Substring(LogText.Length - 3000);
            }
        }

        /// <summary>
        /// 更新命令可执行状态
        /// </summary>
        private void UpdateCanExecuteStates()
        {
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(CanSyncTime));
            OnPropertyChanged(nameof(CanDisconnect));

            if (ScanDeviceCommand is RelayCommand scanCmd)
                scanCmd.RaiseCanExecuteChanged();
            if (ConnectDeviceCommand is RelayCommand connectCmd)
                connectCmd.RaiseCanExecuteChanged();
            if (SyncTimeCommand is RelayCommand syncCmd)
                syncCmd.RaiseCanExecuteChanged();
            if (ManualSetTimeCommand is RelayCommand manualCmd)
                manualCmd.RaiseCanExecuteChanged();
            if (DisconnectDeviceCommand is RelayCommand disconnectCmd)
                disconnectCmd.RaiseCanExecuteChanged();
        }

        #endregion

        #region INotifyPropertyChanged 实现

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable 实现

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _pcTimeTimer?.Stop();
            _autoSyncTimer?.Stop();

            try { _usbService?.Disconnect(); } catch { }

            _isDisposed = true;
        }

        #endregion
    }
}
