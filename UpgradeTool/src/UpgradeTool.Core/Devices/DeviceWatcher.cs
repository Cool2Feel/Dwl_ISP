namespace UpgradeTool.Core.Devices;

/// <summary>设备连接状态变化事件参数。</summary>
public sealed record DeviceStateChanged(
    DeviceConnection Connection,
    bool Connected,
    string Reason);

/// <summary>
/// 设备自动检测/连接器：
/// 周期性枚举 USB MSC 磁盘设备，新出现的目标设备自动建立连接
/// （打开传输层 + 探针 + 查询 Flash 信息），消失的设备自动断开并释放。
/// 只在枚举标识为目标设备（IsTarget，描述符厂商/产品串匹配）时尝试连接，
/// 非目标设备跳过；连接失败的设备进入冷却期，避免每个轮询周期重复重试。
/// 每个决策点输出调试日志，便于排查"设备没识别到/连接不上"的问题。
/// 事件在检测线程触发，调用方需自行调度到 UI 线程。
///
/// 多线程与并发控制（对齐参考项目 MPTool 多线程多设备架构）：
///   设备连接握手（打开传输层 + SCSI 探针 + 驱动上传 + Flash 查询）是耗时阻塞操作，
///   单台接入无所谓，但多台设备同时接入时若串行连接，后续设备会长时间排队等待。
///   因此对每台设备启动独立连接任务（等价 MPTool 的 AfxBeginThread(DownloadThread)），
///   用 SemaphoreSlim 做有界并发控制（默认上限 8，对齐 MPTool MAX_THREAD=8，可配置），
///   多台设备并行握手，显著缩短整体识别时间；单台设备连接失败/异常不影响同批其他设备。
///   日志输出经 _logLock 串行化，保证并发连接下日志不交错、线程安全。
///
/// 轮询策略（插拔及时性）：
///   空闲时指数退避 2s → 5s → 10s，减少 CPU 空转；
///   存在"已出现但尚未连接"的目标设备（含刚插入描述符暂不可用的设备）时
///   保持 2s 快扫，尽快建连/重试；
///   设备插入/拔出或调用 ScanNowAsync()/ResetBackoff() 时立即唤醒休眠中的
///   轮询循环（不再等最长 10s 的退避间隔）并重置为 2s。
/// </summary>
public sealed class DeviceWatcher : IDisposable
{
    /// <summary>并发连接上限默认值：对齐 MPTool 的 MAX_THREAD=8。</summary>
    public const int DefaultMaxConcurrentConnections = 8;

    private readonly object _sync = new();
    private readonly Action<string>? _log;
    private readonly object _logLock = new(); // 并发连接时多个后台线程同时输出日志，串行化日志调用保证线程安全
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _failedCooldown;
    private readonly Func<IReadOnlyList<MscDeviceInfo>> _enumerate;
    private readonly Func<MscDeviceInfo, Action<string>?, TimeSpan?, DeviceConnection?>? _connect;
    private readonly IUvcUpdater? _uvcUpdater; // 可选：UVC 扩展单元升级命令通道（与 USB-MSC 并行的另一条升级触发路径）
    private readonly int _uvcPollInterval;     // 每 N 轮轮询执行一次 UVC 探测（对齐 MPTool wait_cnt=5）
    private readonly int _maxConcurrentConnections; // 并发连接上限（对齐 MPTool MAX_THREAD=8）
    private int _uvcCounter;                   // UVC 节流计数器
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly SemaphoreSlim _scanTrigger = new(0, 1);
    private CancellationTokenSource? _cts; // 非 readonly：Stop 后重新 Start 时重建，支持暂停/恢复检测
    private readonly Dictionary<string, DeviceConnection> _connections = new();
    private readonly Dictionary<string, DateTime> _failedCooldowns = new();
    private readonly HashSet<string> _ignoredLogged = new();
    private readonly object _backoffLock = new();
    private Task? _pollLoop;
    private int _lastDeviceCount = -1;
    private int _idleScanCount;
    private bool _backoffResetRequested;
    private bool _pendingTarget; // 存在已出现但尚未连接的目标设备 → 保持快扫
    private bool _transientDescriptorPending; // 存在描述符暂不可查询的设备（刚插入仍在初始化）→ 保持快扫
    private bool _disposed;

    /// <summary>指数退避阶梯：空闲时逐级递增，设备变化时立即复位。</summary>
    internal static readonly TimeSpan[] BackoffSteps =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
    ];

    public DeviceWatcher(
        Action<string>? log = null,
        TimeSpan? connectTimeout = null,
        TimeSpan? failedCooldown = null,
        Func<IReadOnlyList<MscDeviceInfo>>? enumerate = null,
        Func<MscDeviceInfo, Action<string>?, TimeSpan?, DeviceConnection?>? connect = null,
        IUvcUpdater? uvcUpdater = null,
        int uvcPollInterval = 5,
        int maxConcurrentConnections = DefaultMaxConcurrentConnections)
    {
        _log = log;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(10);
        _failedCooldown = failedCooldown ?? TimeSpan.FromSeconds(30);
        _enumerate = enumerate ?? EnumerateDevices;
        _connect = connect;
        _uvcUpdater = uvcUpdater;
        _uvcPollInterval = uvcPollInterval < 1 ? 1 : uvcPollInterval;
        _maxConcurrentConnections = Math.Max(1, maxConcurrentConnections);
    }

    /// <summary>设备连接状态变化（新设备接入或设备断开）。</summary>
    public event Action<DeviceStateChanged>? DeviceChanged;

    /// <summary>当前已连接的设备快照。</summary>
    public IReadOnlyList<DeviceConnection> Connections
    {
        get
        {
            lock (_sync)
                return _connections.Values.ToArray();
        }
    }

    public bool IsRunning => _pollLoop is { IsCompleted: false };

    /// <summary>开始周期检测。已运行时无操作；Stop 后再次调用可恢复检测（重建取消令牌）。</summary>
    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pollLoop is { IsCompleted: false })
                return;
            // Stop() 已取消旧令牌；重新 Start 时重建，避免取消状态阻塞新一轮检测循环
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;
            _pollLoop = Task.Run(() => PollLoopAsync(ct));
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _cts?.Cancel();
            _pollLoop = null;
        }
    }

    /// <summary>立即执行一次设备扫描（不阻塞）。会复位退避计时，并唤醒休眠中的轮询循环。</summary>
    public Task ScanNowAsync()
    {
        lock (_backoffLock)
            _backoffResetRequested = true;
        SignalWake();
        CancellationToken ct = _cts?.Token ?? CancellationToken.None;
        return Task.Run(() => PollOnceAsync(ct));
    }

    /// <summary>外部设备变更事件（WMI/RegisterDeviceNotification）触发时复位退避，并唤醒轮询循环立即扫描。</summary>
    public void ResetBackoff()
    {
        lock (_backoffLock)
        {
            _idleScanCount = 0;
            _backoffResetRequested = true;
        }
        SignalWake();
    }

    /// <summary>唤醒休眠中的轮询循环（使其跳过当前退避等待立即进入下一轮扫描）。</summary>
    private void SignalWake()
    {
        try
        {
            if (_scanTrigger.CurrentCount == 0)
                _scanTrigger.Release();
        }
        catch (SemaphoreFullException) { /* 已有一个待消费信号，忽略 */ }
    }

    /// <summary>线程安全日志：并发连接时多个后台线程同时输出日志，串行化调用避免交错。</summary>
    private void Log(string message)
    {
        lock (_logLock)
            _log?.Invoke(message);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break; // 主动停止（Stop/Dispose），正常退出轮询循环
            }
            catch (Exception ex)
            {
                Log($"设备检测异常: {ex.Message}");
            }

            // 本轮无变化 → 退避升级；有变化或外部请求复位 → 回到最短间隔；
            // 有待连接的目标设备 → 保持最短间隔（尽快建连/重试）。
            bool reset;
            TimeSpan delay;
            lock (_backoffLock)
            {
                reset = _backoffResetRequested;
                if (reset)
                {
                    _idleScanCount = 0;
                    _backoffResetRequested = false;
                }
                else if (!_pendingTarget && _idleScanCount < BackoffSteps.Length - 1)
                {
                    _idleScanCount++;
                }
                delay = BackoffSteps[_idleScanCount];
            }

            Log(_pendingTarget
                ? $"存在待连接设备，保持 {delay.TotalSeconds:0} 秒快扫间隔。"
                : $"设备无变化，下次扫描等待 {delay.TotalSeconds:0} 秒。");

            // 休眠期间可被设备变更事件（SignalWake）打断，插拔即时响应
            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task delayTask = Task.Delay(delay, delayCts.Token);
            Task wakeTask = _scanTrigger.WaitAsync(delayCts.Token);
            Task completed = await Task.WhenAny(delayTask, wakeTask).ConfigureAwait(false);
            delayCts.Cancel(); // 取消未完成的那一个等待
            // 取消会在败方任务上触发 OperationCanceledException，必须消费，避免成为未观察异常
            Task loser = ReferenceEquals(completed, wakeTask) ? delayTask : wakeTask;
            try { await loser.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            if (completed == wakeTask)
                Log("收到设备变更唤醒信号，立即重新扫描。");
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        // 同一时刻只允许一次扫描，避免 ScanNowAsync 与轮询循环并发导致重复连接
        await _pollGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await PollCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _pollGate.Release();
        }
    }

    private async Task PollCoreAsync(CancellationToken ct)
    {
        _transientDescriptorPending = false; // 每轮重置；由枚举期间发现描述符暂不可用的设备置位
        IReadOnlyList<MscDeviceInfo> present = _enumerate();
        Dictionary<string, MscDeviceInfo> byPath = present.ToDictionary(d => d.DevicePath);

        // 设备数量变化时提示（避免每个轮询周期重复打印相同清单）
        int presentCount;
        lock (_sync)
            presentCount = present.Count;
        if (presentCount != _lastDeviceCount)
        {
            _lastDeviceCount = presentCount;
            lock (_backoffLock)
                _backoffResetRequested = true; // 设备数量变化 → 复位退避（立即再扫，尽快建连）
            Log($"扫描到 {presentCount} 个磁盘设备。");
        }

        // 清理已消失设备的"已忽略"记录，下次出现时重新打印
        _ignoredLogged.RemoveWhere(p => !byPath.ContainsKey(p));

        // 清理已消失设备的冷却记录，避免路径复用后仍被冷却拦截
        lock (_sync)
        {
            string[] staleCooldowns = _failedCooldowns.Keys.Where(k => !byPath.ContainsKey(k)).ToArray();
            foreach (string stale in staleCooldowns)
                _failedCooldowns.Remove(stale);
        }

        // 1) 断开已消失的设备
        string[] removed;
        lock (_sync)
            removed = _connections.Keys.Where(k => !byPath.ContainsKey(k)).ToArray();
        foreach (string path in removed)
        {
            DeviceConnection? conn = null;
            lock (_sync)
            {
                if (_connections.Remove(path, out DeviceConnection? existing))
                    conn = existing;
            }
            if (conn != null)
            {
                Log($"设备已断开: {conn.DisplayName}");
                // 先通知订阅者再释放连接：让 UI 等订阅方仍能读取 DisplayName/Flash 等连接信息
                DeviceChanged?.Invoke(new DeviceStateChanged(conn, false, "设备已移除"));
                conn.Dispose();
            }
        }

        // 2) 并发自动连接新出现的目标设备
        //    对齐 MPTool 多线程多设备架构（每台设备一个线程，MAX_THREAD=8）：
        //    连接握手（打开传输层 + SCSI 探针 + 驱动上传 + Flash 查询）是耗时阻塞操作，
        //    多台设备同时接入时若串行连接，后续设备会长时间排队；这里把每台设备的连接
        //    交给独立 Task，用 SemaphoreSlim 做有界并发控制（默认上限 8），并行握手，
        //    显著缩短整体识别时间。扫描期间 PollOnceAsync 仍持有 _pollGate，
        //    同一时刻只有一轮扫描，同一路径不会被重复连接。
        var candidates = new List<MscDeviceInfo>();
        foreach (MscDeviceInfo info in present)
        {
            lock (_sync)
            {
                if (_connections.ContainsKey(info.DevicePath))
                    continue;
            }

            // 只对目标设备做连接握手
            if (!ShouldAttemptConnect(info))
            {
                if (_ignoredLogged.Add(info.DevicePath))
                    Log($"跳过非目标设备: {info.DisplayName}（未命中设备签名）");
                continue;
            }

            if (InCooldown(info.DevicePath))
            {
                Log($"设备 {info.DisplayName} 处于连接冷却期，本轮跳过。");
                continue;
            }

            candidates.Add(info);
        }

        if (candidates.Count > 0)
        {
            Log($"本轮待连接目标设备 {candidates.Count} 台（并发上限 {_maxConcurrentConnections}），开始并行握手...");
            using var connectGate = new SemaphoreSlim(_maxConcurrentConnections, _maxConcurrentConnections);
            Task[] connectTasks = candidates.Select(info => ConnectOneAsync(info, connectGate, ct)).ToArray();
            await Task.WhenAll(connectTasks).ConfigureAwait(false);
        }

        // 3) 存在"已出现但尚未连接"的目标设备（未连接且未冷却）→ 保持快扫，尽快建连/重试。
        //    描述符暂不可用的设备（刚插入仍在初始化）同样视为待连接，保持快扫直至其就绪。
        lock (_sync)
        {
            _pendingTarget = _transientDescriptorPending || present.Any(p =>
                p.IsTarget && !_connections.ContainsKey(p.DevicePath) && !_failedCooldowns.ContainsKey(p.DevicePath));
        }

        // 4) UVC 扩展单元升级命令通道（与 USB-MSC 并行）：
        //    对齐 MPTool WM_TIMER——每轮轮询以节流计数探测一次视频输入设备的 UVC 扩展节点，
        //    找到即下发 XU SET 升级触发命令（使相机进入 Loader 模式）。无 UVC 设备时也按间隔节流，
        //    避免每轮空轮询（相对 MPTool 的"仅找到时重置计数"是一处优化）。任何异常均被吞掉，
        //    绝不中断热插拔主流程。
        if (_uvcUpdater != null)
        {
            try
            {
                if (_uvcCounter > 0)
                {
                    _uvcCounter--;
                }
                else
                {
                    int node = _uvcUpdater.FindExtensionNode();
                    if (node >= 0)
                    {
                        if (_uvcUpdater.SendUpdateCommand(node))
                            Log($"[UVC] 已通过扩展单元下发升级命令（节点 {node}），相机将进入升级模式。");
                        // 对齐 MPTool：仅在找到扩展单元（并成功下发）后才节流，
                        // 避免每轮重复探测；未找到节点时保持计数=0，下一轮继续探测。
                        _uvcCounter = _uvcPollInterval;
                    }
                    else
                    {
                        Log("[UVC] 未发现 UVC 扩展单元节点，跳过升级命令下发。");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[UVC] 升级命令轮询异常（已忽略，不影响设备检测）：{ex.Message}");
                _uvcCounter = _uvcPollInterval;
            }
        }
    }

    /// <summary>
    /// 并发连接单台设备（由 SemaphoreSlim 限制并发度，等价 MPTool 每设备一个 DownloadThread）。
    /// 单台设备连接失败/异常只影响自身（Loader 失败进入冷却期），不中断同批其他设备的并行连接。
    /// </summary>
    private async Task ConnectOneAsync(MscDeviceInfo info, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            try
            {
                Log($"正在连接设备: {info.DisplayName}");
                DeviceConnection? conn = await Task.Run(() =>
                    (_connect ?? DeviceConnection.Connect)(info, Log, _connectTimeout), ct).ConfigureAwait(false);
                if (conn == null)
                {
                    // Loader 设备连接失败 → 进入冷却期（避免每个轮询周期重复重试）；
                    // 应用态设备 Connect 返回 null = 0xDA 已下发、设备正切换至 Loader 模式，
                    // 属于预期模式切换而非失败，不记冷却，等待重新枚举后按 Loader 态接入。
                    if (DeviceSignature.IsLoader(info.VendorId, info.ProductId))
                    {
                        Log($"设备 {info.DisplayName} 连接失败，进入冷却期。");
                        RecordFailed(info.DevicePath);
                    }
                    else
                    {
                        Log($"设备 {info.DisplayName} 已下发 0xDA 切换至 Loader 模式，等待重新识别...");
                    }
                    return;
                }

                lock (_sync)
                {
                    // 并发连接下另一任务可能已建立同一路径连接，则释放本次连接避免重复
                    if (_connections.ContainsKey(info.DevicePath))
                    {
                        conn.Dispose();
                        return;
                    }
                    _connections[info.DevicePath] = conn;
                }
                Log($"设备已连接: {conn.DisplayName}");
                DeviceChanged?.Invoke(new DeviceStateChanged(conn, true, "设备已识别并连接"));
            }
            catch (OperationCanceledException)
            {
                throw; // 取消（Stop/Dispose）继续传播，终止剩余连接
            }
            catch (Exception ex)
            {
                // 单台设备连接异常不中断同批其他设备（对齐 MPTool 每设备线程的错误隔离）
                Log($"设备 {info.DisplayName} 连接异常: {ex.Message}");
                if (DeviceSignature.IsLoader(info.VendorId, info.ProductId))
                    RecordFailed(info.DevicePath);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>是否值得尝试对该设备建立连接（命中目标签名才连接）。</summary>
    internal static bool ShouldAttemptConnect(MscDeviceInfo info) => info.IsTarget;

    private bool InCooldown(string path)
    {
        lock (_sync)
        {
            if (!_failedCooldowns.TryGetValue(path, out DateTime lastFail))
                return false;
            if (DateTime.UtcNow - lastFail >= _failedCooldown)
            {
                _failedCooldowns.Remove(path);
                return false;
            }
            return true;
        }
    }

    private void RecordFailed(string path)
    {
        lock (_sync)
            _failedCooldowns[path] = DateTime.UtcNow;
    }

    private IReadOnlyList<MscDeviceInfo> EnumerateDevices()
    {
        var list = new List<MscDeviceInfo>();
        try
        {
            IReadOnlyList<MscDiskProbe> probes = MscDeviceEnumerator.EnumerateProbes(_log);
            foreach (MscDiskProbe probe in probes)
            {
                if (probe.SkipReason != null)
                {
                    // 每个磁盘接口只打印一次跳过原因，避免每轮轮询刷屏
                    // if (_ignoredLogged.Add(probe.DevicePath))
                    //     _log?.Invoke($"  忽略磁盘 {probe.DevicePath}: {probe.SkipReason}");
                    // 描述符查询失败通常是设备刚插入仍在初始化（暂不可查询），
                    // 标记为待连接目标，让轮询保持快扫直至其就绪。
                    if (probe.SkipReason.StartsWith("描述符查询失败", StringComparison.Ordinal))
                        _transientDescriptorPending = true;
                    continue;
                }
                list.Add(probe.ToDeviceInfo());
            }
        }
        catch (Exception ex)
        {
            Log($"设备枚举失败: {ex.Message}");
        }
        return list;
    }

    public void Dispose()
    {
        Stop();
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (DeviceConnection conn in _connections.Values)
                conn.Dispose();
            _connections.Clear();
            _cts?.Dispose();
            _pollGate.Dispose();
            _scanTrigger.Dispose();
        }
    }
}
