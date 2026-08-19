using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using UpgradeTool.Core;
using UpgradeTool.Core.Abstractions;
using UpgradeTool.Core.Devices;
using UpgradeTool.Core.Protocol;
using UpgradeTool.Core.Utilities;
using Microsoft.Win32;

namespace UpgradeTool.App;

/// <summary>DC503J 固件刷写工具主窗口。</summary>
public partial class MainWindow : Window
{
    // ---- 设备变更通知（RegisterDeviceNotification，WM_DEVICECHANGE）----
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;        // 设备已插入
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004; // 设备已移除
    private const int DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
    private IntPtr _deviceNotificationHandle;
    private HwndSource? _hwndSource;

    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _batchMode; // true = 批量烧录中，此时 StartButton 禁用
    private DeviceWatcher? _watcher;
    private int _batchTotal;
    private int _batchCompleted;

    // 自动开始（对齐 MPTool AUTOSTART=1）防抖：设备可能逐台枚举，延迟合并为一次批量烧录
    private readonly DispatcherTimer _autoStartDebounce;

    // 本地设置（最近固件路径 + 选项勾选状态），启动时加载、关闭时保存
    private readonly ToolSettings _settings = ToolSettings.Load();
    private bool _restoringSettings; // 恢复设置期间抑制事件日志，避免启动即刷屏

    /// <summary>应用级持久日志写入器：从启动即记录，涵盖设备检测与刷写会话。</summary>
    private readonly LogFileWriter _appLog;

    public MainWindow()
    {
        // 应用级日志：从启动开始持续记录（含设备自动检测日志），供排查完整问题链路
        // 注意：必须在 InitializeComponent 之前创建，但 Log 调用必须在 InitializeComponent 之后
        // （Log 方法依赖 LogBox，而 LogBox 由 XAML 初始化）
        _appLog = new LogFileWriter(fileNamePrefix: "mptool");

        // 自动开始防抖定时器：设备接入后等待 600ms 枚举稳定，再触发一次批量烧录
        _autoStartDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _autoStartDebounce.Tick += AutoStartDebounce_Tick;

        InitializeComponent();

        // 支持将固件文件拖拽到路径框快速选择
        AllowDrop = true;
        DragOver += OnWindowDragOver;
        Drop += OnWindowDrop;

        // 还原上次的最近固件路径与选项勾选状态（须在事件绑定后、设备检测启动前）
        RestoreSettings();

        // 窗口句柄就绪后注册设备变更通知（WM_DEVICECHANGE），设备插入/拔出时立即触发扫描
        SourceInitialized += OnSourceInitialized;

        // 以下 Log 调用均在 InitializeComponent 之后，LogBox 已就绪
        if (_appLog.IsFileLoggingActive)
            Log($"日志文件: {_appLog.FilePath}");
        Log("工具已就绪。");
        WarnIfNotElevated();
        StartWatcher();

        // 事件绑定
        BatchButton.Click += Batch_Click;
    }

    /// <summary>清空日志显示区。</summary>
    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogBox.Clear();
    }

    /// <summary>拖拽悬停：仅当拖入 .bin 文件时显示可放置光标。</summary>
    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasCompatibleFirmwareFile(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>拖放：将首个 .bin 固件文件路径填入路径框，并记入最近使用列表。</summary>
    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (TryGetFirmwareFile(e.Data, out string? path))
        {
            RememberFirmwarePath(path!, updateBox: true);
            Log($"已通过拖拽选择固件: {path}");
        }
    }

    private static bool HasCompatibleFirmwareFile(IDataObject data)
        => TryGetFirmwareFile(data, out _);

    private static bool TryGetFirmwareFile(IDataObject data, out string? path)
    {
        path = null;
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            string? first = files.FirstOrDefault(f => f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));
            if (first != null)
            {
                path = first;
                return true;
            }
        }
        return false;
    }

    // ================================================================
    //  设置持久化：最近固件路径 + 选项勾选状态
    // ================================================================

    /// <summary>启动时还原最近固件路径与选项勾选状态。</summary>
    private void RestoreSettings()
    {
        _restoringSettings = true;
        try
        {
            // 过滤掉已不存在的固件文件（如被移动/删除），避免下拉列表残留无效记录
            _settings.RecentFirmwarePaths.RemoveAll(static p => !File.Exists(p));

            RefreshRecentPaths();
            FirmwarePathBox.Text = _settings.RecentFirmwarePaths.FirstOrDefault() ?? string.Empty;

            // 先恢复会联动禁用其他选项的开关，再恢复其余勾选状态
            QuickDebugBox.IsChecked = _settings.QuickDebug;
            VerifyBox.IsChecked = _settings.Verify;
            CapacityCheckBox.IsChecked = _settings.CapacityCheck;
            AutoStartBox.IsChecked = _settings.AutoStart;
            AutoResetBox.IsChecked = _settings.AutoReset;
            EraseAllBox.IsChecked = _settings.EraseAll;
            BootChecksumBox.IsChecked = _settings.BootChecksum;
            EnterOnlyBox.IsChecked = _settings.EnterOnly;

            // 高级选项面板展开状态
            bool show = _settings.AdvancedOptionsVisible;
            AdvancedOptionsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            AdvancedToggleButton.Content = show ? "高级选项 ▴" : "高级选项 ▾";

            if (_settings.RecentFirmwarePaths.Count > 0)
                Log($"已恢复上次设置：最近固件 {_settings.RecentFirmwarePaths.Count} 条 + 选项勾选状态。");
        }
        finally
        {
            _restoringSettings = false;
        }
    }

    /// <summary>把当前界面状态保存到设置（关闭窗口时调用）。</summary>
    private void SaveSettings()
    {
        // 手动键入但未点"开始"的路径也纳入记录；路径来源以内存列表为准（RememberFirmwarePath 已维护）
        string current = FirmwarePathBox.Text?.Trim() ?? string.Empty;
        if (current.Length > 0 && File.Exists(current))
            _settings.AddRecentPath(current);

        _settings.AutoStart = AutoStartBox.IsChecked == true;
        _settings.QuickDebug = QuickDebugBox.IsChecked == true;
        _settings.AutoReset = AutoResetBox.IsChecked == true;
        _settings.EraseAll = EraseAllBox.IsChecked == true;
        _settings.Verify = VerifyBox.IsChecked == true;
        _settings.CapacityCheck = CapacityCheckBox.IsChecked == true;
        _settings.BootChecksum = BootChecksumBox.IsChecked == true;
        _settings.EnterOnly = EnterOnlyBox.IsChecked == true;
        _settings.AdvancedOptionsVisible = AdvancedOptionsPanel.Visibility == Visibility.Visible;

        _settings.Save();
    }

    /// <summary>记录一条固件路径到最近列表并立即落盘（浏览/拖拽/开始刷写时调用）。</summary>
    private void RememberFirmwarePath(string path, bool updateBox = false)
    {
        if (updateBox)
            FirmwarePathBox.Text = path;
        _settings.AddRecentPath(path);
        RefreshRecentPaths();
        _settings.Save(); // 立即落盘，避免崩溃丢失最近记录
    }

    /// <summary>以下拉列表只读副本刷新最近路径（ComboBox 可编辑模式下 ItemsSource 换新副本以触发布局更新）。</summary>
    private void RefreshRecentPaths()
    {
        FirmwarePathBox.ItemsSource = _settings.RecentFirmwarePaths.ToList();
    }

    /// <summary>向磁盘下发 SCSI Pass-Through 需要管理员权限，非管理员启动时提示。</summary>
    private void WarnIfNotElevated()
    {
        bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
        if (!elevated)
            Log("警告：当前非管理员运行，设备检测不受影响，但连接/刷写需要管理员权限。");
    }

    /// <summary>窗口关闭时停止自动检测并释放设备句柄，避免后台连接挂起拖住关闭。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        // 烧录进行中：先取消当前会话，并请用户确认是否退出，避免设备停在半烧状态
        // （协议层采用"引导扇区最后写"设计，中断后重烧即可恢复，但需用户知情）。
        if (_busy)
        {
            _cts?.Cancel();
            MessageBoxResult confirm = MessageBox.Show(
                this,
                "当前正在烧录，确定要退出吗？退出会中断进行中的烧录会话。",
                "UpgradeTool",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        _autoStartDebounce.Stop();
        _autoStartDebounce.Tick -= AutoStartDebounce_Tick;
        StopWatcher();
        UnregisterDeviceNotifications();
        SourceInitialized -= OnSourceInitialized;
        SaveSettings(); // 退出前保存最近路径与选项勾选状态，供下次启动还原
        _appLog.Dispose();
        base.OnClosing(e);
    }

    private void Log(string message)
    {
        // 同步写入应用级日志文件（线程安全），再更新 UI
        _appLog?.Write(message);
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        LogBox.ScrollToEnd();
    }

    /// <summary>异步投递日志到 UI 线程（BeginInvoke）：后台线程不被 UI 渲染阻塞，避免日志量大时相互牵制；窗口关闭期间静默丢弃。</summary>
    private void DispatchLog(string message)
    {
        try
        {
            Dispatcher.BeginInvoke(() => Log(message));
        }
        catch (Exception)
        {
            // 关闭期间 Dispatcher 停止，丢弃日志
        }
    }

    private void StartWatcher()
    {
        StopWatcher();
        _watcher = new DeviceWatcher(
            log: msg =>
            {
                // 后台线程写日志：窗口关闭期间 Dispatcher 停止时直接丢弃，避免挂起后台任务。
                // 设备检测日志先写应用级日志文件，再投递到 UI 线程。
                _appLog?.Write(msg);
                try { Dispatcher.BeginInvoke(() => LogBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n")); }
                catch (Exception) { /* 关闭期间忽略 */ }
            },
            uvcUpdater: new UvcDeviceUpdater(msg => _appLog?.Write(msg)),
            uvcPollInterval: 5);
        _watcher.DeviceChanged += OnDeviceChanged;
        _watcher.Start();
        Log($"已启动设备自动检测（空闲时指数退避 2s→5s→10s，插拔事件即时唤醒扫描，待连接设备保持快扫；多台设备并行连接，并发上限 {DeviceWatcher.DefaultMaxConcurrentConnections}，对齐 MPTool MAX_THREAD=8）。");
    }

    private void StopWatcher()
    {
        if (_watcher != null)
        {
            _watcher.DeviceChanged -= OnDeviceChanged;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    // ================================================================
    //  RegisterDeviceNotification — 设备插入/拔出即时通知
    // ================================================================

    /// <summary>窗口句柄就绪后，注册磁盘设备变更通知。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);

        // DEV_BROADCAST_DEVICEINTERFACE 结构体，指定监听磁盘接口
        var filter = new DevBroadcastDeviceInterface
        {
            DeviceType = DBT_DEVTYP_DEVICEINTERFACE,
            Reserved = 0,
            ClassGuid = MscDeviceEnumerator.DiskClassGuid,
            Size = Marshal.SizeOf<DevBroadcastDeviceInterface>(),
        };
        _deviceNotificationHandle = RegisterDeviceNotification(
            handle, ref filter, DEVICE_NOTIFY_WINDOW_HANDLE);
        if (_deviceNotificationHandle == IntPtr.Zero)
            Log($"注册设备变更通知失败：Win32 错误码 {Marshal.GetLastWin32Error()}");
        else
            Log("已注册设备变更通知（WM_DEVICECHANGE），设备插入/拔出自动触发扫描。");
    }

    /// <summary>窗口消息处理：捕获 WM_DEVICECHANGE 后立即触发 DeviceWatcher 扫描。</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE)
        {
            int evt = (int)wParam;
            if (evt == DBT_DEVICEARRIVAL || evt == DBT_DEVICEREMOVECOMPLETE)
            {
                _watcher?.ResetBackoff();
                _watcher?.ScanNowAsync();
            }
        }
        return IntPtr.Zero;
    }

    private void UnregisterDeviceNotifications()
    {
        if (_deviceNotificationHandle != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_deviceNotificationHandle);
            _deviceNotificationHandle = IntPtr.Zero;
        }
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    // ---- P/Invoke ----

    [StructLayout(LayoutKind.Sequential)]
    private struct DevBroadcastDeviceInterface
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(
        IntPtr hRecipient,
        ref DevBroadcastDeviceInterface notificationFilter,
        int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterDeviceNotification(IntPtr hHandle);

    /// <summary>设备接入/断开：同步到 UI 列表并自动选中第一个可用设备。</summary>
    private void OnDeviceChanged(DeviceStateChanged e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.Connected)
            {
                DeviceList.Items.Add(new FlashDeviceItem(e.Connection));
                Log($"检测到设备: {e.Connection.DisplayName}");
                if (DeviceList.SelectedItem == null && DeviceList.Items.Count > 0)
                    DeviceList.SelectedIndex = 0;

                // 自动开始（对齐 MPTool AUTOSTART=1）：设备接入且勾选时，防抖合并触发批量烧录
                if (AutoStartBox.IsChecked == true && !_busy)
                {
                    _autoStartDebounce.Stop();
                    _autoStartDebounce.Start();
                }
            }
            else
            {
                FlashDeviceItem? item = DeviceList.Items
                    .OfType<FlashDeviceItem>()
                    .FirstOrDefault(x => x.Connection == e.Connection);
                if (item != null)
                    DeviceList.Items.Remove(item);
                Log($"设备已断开: {e.Connection.DisplayName}");
                if (DeviceList.SelectedItem == null && DeviceList.Items.Count > 0)
                    DeviceList.SelectedIndex = 0;
            }
            StatusText.Text = $"已自动连接 {DeviceList.Items.Count} 个设备";
        });
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DeviceList.Items.Clear();
            StartWatcher(); // 重启 watcher 重新扫描
            Log("已重新扫描设备。");
        }
        catch (Exception ex)
        {
            Log($"设备扫描失败: {ex.Message}");
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "固件文件 (*.bin)|*.bin|所有文件 (*.*)|*.*",
            Title = "选择固件文件",
        };
        if (dialog.ShowDialog(this) == true)
        {
            RememberFirmwarePath(dialog.FileName, updateBox: true);
            Log($"已选择固件: {dialog.FileName}");
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (DeviceList.SelectedItem is not FlashDeviceItem selected)
        {
            Log("请先选择目标设备（自动检测中，稍候...）。");
            return;
        }
        DeviceConnection connection = selected.Connection;

        FirmwareImage? firmware = null;
        if (EnterOnlyBox.IsChecked != true)
        {
            if (string.IsNullOrWhiteSpace(FirmwarePathBox.Text))
            {
                Log("请选择固件文件。");
                return;
            }

            try
            {
                firmware = FirmwareImage.Load(FirmwarePathBox.Text.Trim());
                RememberFirmwarePath(FirmwarePathBox.Text.Trim());
            }
            catch (Exception ex)
            {
                Log($"固件加载失败: {ex.Message}");
                return;
            }
        }

        // 固件大小与 Flash 容量比较：固件超出设备 Flash 容量时无法正常烧录，提前阻断。
        // 仅当 EnterOnly（不进固件）模式跳过此检查；Flash 信息未知时仅记录警告，不阻断。
        if (firmware != null)
        {
            if (connection.Flash is { CapacityBytes: > 0 } flashInfo)
            {
                if (firmware.Length > flashInfo.CapacityBytes)
                {
                    Log($"固件大小 ({firmware.Length} 字节) 超出设备 Flash 容量 ({flashInfo.CapacityBytes} 字节)，无法烧录。");
                    return;
                }
                Log($"固件大小 ({firmware.Length} 字节) 在设备 Flash 容量 ({flashInfo.CapacityBytes} 字节) 范围内。");
            }
            else
            {
                Log("警告：无法获取设备 Flash 容量信息，跳过固件大小检查。");
            }
        }

        // 容量 pattern 检测仅在"整片擦除"时执行（LoaderRomProtocol 依赖 EraseAll），提前提示避免误解
        if (CapacityCheckBox.IsChecked == true && EraseAllBox.IsChecked != true)
            Log("提示：容量 pattern 检测需同时勾选「整片擦除」才会执行。");

        SetBusy(true);
        var options = new FlashRunOptions(
            connection.Info,
            firmware,
            VerifyBox.IsChecked == true && QuickDebugBox.IsChecked != true,
            Connected: connection,
            EraseAll: EraseAllBox.IsChecked == true,
            RunCapacityPatternTest: CapacityCheckBox.IsChecked != false && QuickDebugBox.IsChecked != true,
            PatchBootChecksum: BootChecksumBox.IsChecked != false,
            AutoReset: AutoResetBox.IsChecked != false);
        var progress = new Progress<FlashProgress>(OnProgress);

        // 每次刷写创建独立的取消源：取消只作用于当前这次操作，
        // 不会像固定一次的 CTS 那样导致后续所有刷写被永久取消。
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            Log($"--- 开始刷写: {connection.DisplayName} ---");
            selected.SetStatus("开始烧录...", FlashDeviceItem.FlashingBrush, flashing: true);
            // 协议层为同步 SCSI 命令，须在后台线程执行，避免阻塞 UI 线程导致界面卡死。
            // Progress<FlashProgress> 已捕获 UI 同步上下文，进度/日志仍会安全回到 UI 线程。
            FlashSessionResult result = await Task.Run(
                () => FlashService.RunAsync(
                    options,
                    progress,
                    DispatchLog,
                    _cts.Token),
                _cts.Token);

            if (result.Success)
            {
                // 对齐参考项目 MPTool：刷写后终态由「复位」勾选（AutoReset）决定，工具就此放手，
                // 不主动切换设备模式（连接阶段已按设备当前状态选协议）。
                selected.SetStatus("✓ 成功", FlashDeviceItem.SuccessBrush, flashing: false);
                StatusText.Text = "完成";
                Log(result.Summary);
                HideError();
            }
            else
            {
                selected.SetStatus("✕ 失败", FlashDeviceItem.FailedBrush, flashing: false);
                StatusText.Text = "失败";
                Log(result.Summary);
                ShowError("刷写失败", result.Summary, canRetry: true);
            }
        }
        catch (OperationCanceledException)
        {
            selected.SetStatus("✕ 已取消", FlashDeviceItem.IdleBrush, flashing: false);
            Log("已取消。");
            HideError();
        }
        catch (Exception ex)
        {
            Log($"刷写异常: {ex.Message}");
            ShowError(
                ErrorMessages.GetTitle(0),
                $"{ex.Message}",
                canRetry: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>显示错误卡片（标题 + 详情 + 可重试按钮）。</summary>
    private void ShowError(string title, string detail, bool canRetry)
    {
        ErrorTitle.Text = title;
        ErrorDetail.Text = detail;
        RetryButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        ErrorCard.Visibility = Visibility.Visible;
        StatusDot.Fill = (Brush)FindResource("ErrorBrush");
    }

    /// <summary>隐藏错误卡片。</summary>
    private void HideError()
    {
        ErrorCard.Visibility = Visibility.Collapsed;
    }

    private void DismissError_Click(object sender, RoutedEventArgs e) => HideError();

    /// <summary>快速调试模式：跳过回读校验与容量检测，并禁用对应选项卡片防止误用。</summary>
    private void QuickDebugBox_Checked(object sender, RoutedEventArgs e)
    {
        VerifyBox.IsEnabled = false;
        CapacityCheckBox.IsEnabled = false;
        if (!_restoringSettings)
            Log("已启用快速调试：跳过回读校验与容量检测（仅供开发迭代）。");
    }

    private void QuickDebugBox_Unchecked(object sender, RoutedEventArgs e)
    {
        VerifyBox.IsEnabled = true;
        CapacityCheckBox.IsEnabled = true;
        if (!_restoringSettings)
            Log("已关闭快速调试：恢复回读校验与容量检测。");
    }

    /// <summary>高级选项展开/收起：切换独立容器 Visibility，避免 UniformGrid 计入折叠项导致空白。</summary>
    private void AdvancedToggle_Click(object sender, RoutedEventArgs e)
    {
        bool show = AdvancedOptionsPanel.Visibility != Visibility.Visible;
        AdvancedOptionsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AdvancedToggleButton.Content = show ? "高级选项 ▴" : "高级选项 ▾";
        if (show)
            Log("已展开高级选项（回读校验 / 容量检测 / 校验和 / 仅进入升级模式）。");
    }

    /// <summary>自动开始防抖触发：设备枚举稳定后，批量烧录所有已连接设备。</summary>
    private void AutoStartDebounce_Tick(object? sender, EventArgs e)
    {
        _autoStartDebounce.Stop();
        if (_busy || AutoStartBox.IsChecked != true)
            return;
        Log("自动开始已触发（检测到设备接入）。");
        Batch_Click(this, new RoutedEventArgs());
    }

    /// <summary>滚动日志区到末尾，便于用户查看错误详情。</summary>
    private void ScrollToLog(object sender, RoutedEventArgs e)
    {
        LogBox.CaretIndex = LogBox.Text.Length;
        LogBox.ScrollToEnd();
        LogBox.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    /// <summary>
    /// 导出固件（对齐 MPTool ExportSpiCodeToBin）：把选中设备的整片 Flash 读回为 .bin 文件。
    /// 与刷写共享连接（复用 Loader 0xCB 通道），因此同样需要设备已连接；导出不修改 Flash 内容。
    /// </summary>
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (DeviceList.SelectedItem is not FlashDeviceItem selected)
        {
            Log("请先选择目标设备（自动检测中，稍候...）。");
            return;
        }
        DeviceConnection connection = selected.Connection;

        var dialog = new SaveFileDialog
        {
            Filter = "固件导出 (*.bin)|*.bin|所有文件 (*.*)|*.*",
            Title = "导出固件",
            FileName = $"DestBin_export_{DateTime.Now:yyyyMMdd}.bin",
            DefaultExt = ".bin",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        SetBusy(true);
        var options = new ExportRunOptions(connection.Info, dialog.FileName, Connected: connection);
        var progress = new Progress<FlashProgress>(OnProgress);

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            Log($"--- 开始导出固件: {connection.DisplayName} ---");
            selected.SetStatus("开始导出...", FlashDeviceItem.FlashingBrush, flashing: true);
            ExportSessionResult result = await Task.Run(
                () => ExportService.RunAsync(options, progress, DispatchLog, _cts.Token),
                _cts.Token);

            if (result.Success)
            {
                selected.SetStatus("✓ 导出完成", FlashDeviceItem.SuccessBrush, flashing: false);
                StatusText.Text = "完成";
                Log(result.Summary);
                HideError();
            }
            else
            {
                selected.SetStatus("✕ 导出失败", FlashDeviceItem.FailedBrush, flashing: false);
                StatusText.Text = "失败";
                Log(result.Summary);
                ShowError("导出失败", result.Summary, canRetry: true);
            }
        }
        catch (OperationCanceledException)
        {
            selected.SetStatus("✕ 已取消", FlashDeviceItem.IdleBrush, flashing: false);
            Log("已取消。");
            HideError();
        }
        catch (Exception ex)
        {
            Log($"导出异常: {ex.Message}");
            ShowError("导出失败", ex.Message, canRetry: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>进度回调：确保在 UI 线程上更新进度条与状态文本。</summary>
    private void OnProgress(FlashProgress p)
    {
        // Progress<T> 可能因 SynchronizationContext 捕获异常而未正确投递到 UI 线程，
        // 此处使用 Dispatcher.CheckAccess() 安全守卫，确保更新始终在 UI 线程执行。
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnProgress(p));
            return;
        }

        // 单设备模式：同步进度到列表中选中的设备条目，让其进度条/状态随操作实时变化
        if (DeviceList.SelectedItem is FlashDeviceItem sel)
            sel.SetStatus($"{p.Stage}: {p.Percent}%", FlashDeviceItem.FlashingBrush, flashing: true);

        Progress.Value = p.Percent;
        SyncProgressFill(p.Percent);
        PercentText.Text = $"{p.Percent}%";
        StatusText.Text = p.Message;
    }

    /// <summary>将隐藏 ProgressBar 的当前值同步到可见的进度条填充框。</summary>
    private void SyncProgressFill(int percent)
    {
        if (ProgressFill?.RenderTransform is ScaleTransform scale)
            scale.ScaleX = percent / 100d;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        StartButton.IsEnabled = !busy && !_batchMode;
        BatchButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        RefreshButton.IsEnabled = !busy;
        DeviceList.IsEnabled = !busy;

        // 导出同样需要在设备空闲时进行，忙碌期间禁用（导出依赖已连接设备，连接本身不可复用状态）
        ExportButton.IsEnabled = !busy && !_batchMode;

        // 刷写/烧录进行中禁用固件选项，防止操作与进行中的会话冲突（对齐 MPTool 烧录期间锁定界面）
        bool quickDebug = QuickDebugBox.IsChecked == true;
        FirmwarePathBox.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        AdvancedToggleButton.IsEnabled = !busy;
        AutoStartBox.IsEnabled = !busy;
        QuickDebugBox.IsEnabled = !busy;
        AutoResetBox.IsEnabled = !busy;
        EraseAllBox.IsEnabled = !busy;
        EnterOnlyBox.IsEnabled = !busy;
        BootChecksumBox.IsEnabled = !busy;
        // 快速调试开启时回读校验/容量检测恒为禁用，恢复时按勾选状态还原
        VerifyBox.IsEnabled = !busy && !quickDebug;
        CapacityCheckBox.IsEnabled = !busy && !quickDebug;

        if (busy)
        {
            HideError(); // 新操作开始时隐藏之前的错误卡片
            Progress.Value = 0;
            SyncProgressFill(0);
            PercentText.Text = "0%";
            StatusDot.Fill = (Brush)FindResource("WarningBrush");
        }
        else
        {
            StatusDot.Fill = (Brush)FindResource("SuccessBrush");
        }
    }

    // ================================================================
    //  批量并行烧录（对齐 MPTool 多线程多设备架构）
    // ================================================================

    /// <summary>批量烧录所有已连接设备：每台设备独立 Task 并行执行，错误相互隔离。</summary>
    private async void Batch_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        FlashDeviceItem[] devices = DeviceList.Items.OfType<FlashDeviceItem>().ToArray();
        if (devices.Length == 0)
        {
            Log("没有已连接的目标设备，无法批量烧录。");
            return;
        }

        // 仅进入升级模式且未选固件时不支持批量（每台设备都需要固件）
        FirmwareImage? firmware = null;
        if (EnterOnlyBox.IsChecked != true)
        {
            if (string.IsNullOrWhiteSpace(FirmwarePathBox.Text))
            {
                Log("请选择固件文件。");
                return;
            }
            try
            {
                firmware = FirmwareImage.Load(FirmwarePathBox.Text.Trim());
                RememberFirmwarePath(FirmwarePathBox.Text.Trim());
            }
            catch (Exception ex)
            {
                Log($"固件加载失败: {ex.Message}");
                return;
            }
        }

        _batchMode = true;
        _batchTotal = devices.Length;
        _batchCompleted = 0;
        SetBusy(true);

        // 每次批量烧录创建独立取消源：取消会传递给所有设备（LinkedTokenSource）
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken globalCt = _cts.Token;

        foreach (FlashDeviceItem dev in devices)
            dev.SetStatus("等待中...", FlashDeviceItem.IdleBrush, flashing: true);
        Log($"--- 开始批量烧录 {devices.Length} 个设备（并行执行）---");

        // 每台设备独立异步任务，互不阻塞；Task.WhenAll 等待全部结束
        Task<FlashSessionResult>[] tasks = devices
            .Select(dev => FlashOneDeviceAsync(dev, firmware, globalCt))
            .ToArray();
        FlashSessionResult[] results = await Task.WhenAll(tasks);

        int success = results.Count(r => r.Success);
        int failed = results.Count(r => !r.Success);
        Log($"--- 批量烧录完成: {success} 成功 / {failed} 失败 ---");
        StatusText.Text = $"批量烧录完成: {success} 成功 / {failed} 失败";

        _batchMode = false;
        SetBusy(false);

        if (failed > 0)
            ShowError("部分设备烧录失败", $"{failed} 台设备烧录失败，{success} 台成功。请查看日志了解失败详情。", canRetry: true);
        else
            HideError();
    }

    /// <summary>单台设备烧录任务：独立进度 + 独立取消令牌（链接全局取消），结果不影响其他设备。</summary>
    private async Task<FlashSessionResult> FlashOneDeviceAsync(
        FlashDeviceItem dev, FirmwareImage? firmware, CancellationToken globalCt)
    {
        dev.SetStatus("烧录中...", FlashDeviceItem.FlashingBrush, flashing: true);

        using var devCts = CancellationTokenSource.CreateLinkedTokenSource(globalCt);
        var options = new FlashRunOptions(
            dev.Connection.Info,
            firmware,
            VerifyBox.IsChecked == true && QuickDebugBox.IsChecked != true,
            Connected: dev.Connection,
            EraseAll: EraseAllBox.IsChecked == true,
            RunCapacityPatternTest: CapacityCheckBox.IsChecked != false && QuickDebugBox.IsChecked != true,
            PatchBootChecksum: BootChecksumBox.IsChecked != false,
            AutoReset: AutoResetBox.IsChecked != false);

        // 注意：即使 Progress<T> 在 UI 线程创建，其 SynchronizationContext 投递在极端情况下可能失效，
        // 因此回调内部使用 Dispatcher.BeginInvoke 确保 UI 更新始终在 UI 线程执行。
        var progress = new Progress<FlashProgress>(p =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                dev.SetStatus($"{p.Stage}: {p.Percent}%", FlashDeviceItem.FlashingBrush, flashing: true);
                UpdateAggregateProgress();
            });
        });

        FlashSessionResult result;
        try
        {
            // 协议层为同步 SCSI 命令，须在后台线程执行，避免阻塞 UI 线程导致界面卡死。
            result = await Task.Run(
                () => FlashService.RunAsync(
                    options,
                    progress,
                    msg =>
                    {
                        // 文件写在后台线程直接做；UI 更新异步投递，不阻塞烧录线程
                        if (_appLog != null)
                            _appLog.Write(msg);
                        try
                        {
                            Dispatcher.BeginInvoke(() =>
                            {
                                LogBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] [{dev.DisplayName}] {msg}\n");
                                LogBox.ScrollToEnd();
                            });
                        }
                        catch (Exception) { /* 关闭期间忽略 */ }
                    },
                    devCts.Token),
                devCts.Token);
        }
        catch (OperationCanceledException)
        {
            result = new FlashSessionResult(false, "已取消。", FlashStage.Cancelled);
        }
        catch (Exception ex)
        {
            result = new FlashSessionResult(false, ex.Message, FlashStage.Failed);
        }

        Dispatcher.Invoke(() =>
        {
            if (result.Success)
            {
                // 对齐参考项目 MPTool：刷写后终态由「复位」勾选（AutoReset）决定，工具就此放手。
                dev.SetStatus("✓ 成功", FlashDeviceItem.SuccessBrush, flashing: false);
            }
            else if (result.FinalStage == FlashStage.Cancelled)
                dev.SetStatus("✕ 已取消", FlashDeviceItem.IdleBrush, flashing: false);
            else
                dev.SetStatus("✕ 失败", FlashDeviceItem.FailedBrush, flashing: false);
            _batchCompleted++;
            UpdateAggregateProgress();
        });
        return result;
    }

    /// <summary>批量模式聚合进度：所有设备百分比的平均值。</summary>
    private void UpdateAggregateProgress()
    {
        FlashDeviceItem[] items = DeviceList.Items.OfType<FlashDeviceItem>().ToArray();
        if (items.Length == 0)
            return;
        int avg = (int)Math.Round(items.Average(i => i.Percent));
        Progress.Value = avg;
        SyncProgressFill(avg);
        PercentText.Text = $"{avg}%";
        StatusText.Text = $"批量烧录中: {_batchCompleted}/{_batchTotal} 完成";
    }
}

/// <summary>
/// 设备列表项包装：显示设备名 + 烧录状态指示 + 每台设备各自的进度条。
/// 批量烧录时每台设备的进度/状态通过 INotifyPropertyChanged 自动刷新 UI，
/// 进度条填充（PercentScale）+ 动态流光背景（GlowOpacity）直观显示设备工作状态。
/// </summary>
public sealed class FlashDeviceItem : INotifyPropertyChanged
{
    public static readonly Brush IdleBrush = Brushes.Gray;
    public static readonly Brush FlashingBrush = Brushes.Orange;
    public static readonly Brush SuccessBrush = Brushes.Green;
    public static readonly Brush FailedBrush = Brushes.Red;

    private string _statusText = "";
    private Brush _statusBrush = IdleBrush;
    private int _percent;
    private double _percentScale;
    private double _glowOpacity;

    public FlashDeviceItem(DeviceConnection connection) => Connection = connection;

    public DeviceConnection Connection { get; }

    public string DisplayName => Connection.DisplayName;

    /// <summary>设备烧录状态文本（如"Downloading: 45%"、"✓ 成功"）。</summary>
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    /// <summary>状态指示灯颜色。</summary>
    public Brush StatusBrush
    {
        get => _statusBrush;
        private set
        {
            if (!ReferenceEquals(_statusBrush, value))
            {
                _statusBrush = value;
                OnPropertyChanged(nameof(StatusBrush));
            }
        }
    }

    /// <summary>当前进度百分比（0~100，批量聚合进度与单设备进度条共用）。</summary>
    public int Percent
    {
        get => _percent;
        private set
        {
            if (_percent != value)
            {
                _percent = value;
                OnPropertyChanged(nameof(Percent));
                PercentScale = Math.Clamp(value / 100d, 0d, 1d);
            }
        }
    }

    /// <summary>进度条填充比例（0~1），驱动每台设备进度条 ScaleTransform，免去转换器。</summary>
    public double PercentScale
    {
        get => _percentScale;
        private set
        {
            if (Math.Abs(_percentScale - value) > 1e-9)
            {
                _percentScale = value;
                OnPropertyChanged(nameof(PercentScale));
            }
        }
    }

    /// <summary>动态流光背景透明度：设备正在烧录/导出/等待时置 1，空闲或终态置 0。</summary>
    public double GlowOpacity
    {
        get => _glowOpacity;
        private set
        {
            if (Math.Abs(_glowOpacity - value) > 1e-9)
            {
                _glowOpacity = value;
                OnPropertyChanged(nameof(GlowOpacity));
            }
        }
    }

    /// <summary>
    /// 更新设备条目状态。
    /// flashing=true 表示设备正处于烧录/导出/排队等工作状态：激活流光动画背景，
    /// 并尝试从文本 "阶段: NN%" 解析进度百分比（解析失败时保持原值，如"等待中..."）；
    /// flashing=false 为终态：关闭流光，成功时进度直接拉满。
    /// </summary>
    public void SetStatus(string text, Brush brush, bool flashing)
    {
        if (flashing)
        {
            int idx = text.IndexOf(": ", StringComparison.Ordinal);
            if (idx >= 0 && int.TryParse(text.AsSpan(idx + 2).TrimEnd('%'), out int p))
                Percent = p;
            GlowOpacity = 1d;
        }
        else
        {
            GlowOpacity = 0d;
            if (ReferenceEquals(brush, SuccessBrush))
                Percent = 100;
        }
        StatusText = text;
        StatusBrush = brush;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
