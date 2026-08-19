using UpgradeTool.Core.Abstractions;
using UpgradeTool.Core.Devices;
using UpgradeTool.Core.Protocol;
using UpgradeTool.Core.Transport.Simulated;

namespace UpgradeTool.Core.Tests;

/// <summary>
/// 设备连接流程相关测试：
///   - 真实协议 + 模拟设备（测试替身）验证 Flash 查询/下载。
///   - DeviceConnection.Connect 对不可打开设备的优雅失败。
///   - DeviceWatcher 自动检测/自动连接/自动断开（连接工厂注入，避免真实硬件）。
///   - 下载容量校验使用设备端实际容量。
/// </summary>
public class DeviceConnectionTests
{
    private static FirmwareImage CreateFirmware(int length)
    {
        var random = new Random(42);
        var data = new byte[length];
        random.NextBytes(data);
        return new FirmwareImage("simulated.bin", data);
    }

    private static (SimulatedMscDevice Device, Dc503RomProtocol Protocol) CreateProtocol()
    {
        var device = new SimulatedMscDevice();
        var transport = new SimulatedMscTransport(device);
        return (device, new Dc503RomProtocol(transport));
    }

    /// <summary>用模拟设备（测试替身）构造一个已就绪的设备连接。</summary>
    private static DeviceConnection CreateSimulatedConnection(MscDeviceInfo info)
    {
        var device = new SimulatedMscDevice();
        var transport = new SimulatedMscTransport(device);
        transport.Open();
        var protocol = new Dc503RomProtocol(transport);
        return new DeviceConnection(info, transport, protocol,
            new FlashInfo(new byte[] { 0xEF, 0x40, 0x16 }, SimulatedMscDevice.MaxFlashSize));
    }

    private static MscDeviceInfo TargetDevice(string path = "\\\\?\\GLOBALROOT\\Device\\FakeTarget") =>
        new(path, 0x1234, 0x5678, "Buildwin Media-Player",
            VendorId: "Buildwin", ProductId: "Media-Player", IsTarget: true);

    [Fact]
    public async Task GetFlashInfo_ReportsDeviceCapacity()
    {
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();

        ProtocolResult<FlashInfo> result = await protocol.GetFlashInfoAsync(CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);
        Assert.Equal((uint)SimulatedMscDevice.MaxFlashSize, result.Value!.CapacityBytes);
        Assert.Equal("EF 40 16", result.Value.IdText);
        Assert.Equal("4 MB", result.Value.CapacityText);
        Assert.True(device.SpiInitialized);
        Assert.NotNull(device.UploadedStub);
    }

    [Fact]
    public async Task GetFlashInfo_ThenDownload_Works()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();

        ProtocolResult<FlashInfo> info = await protocol.GetFlashInfoAsync(CancellationToken.None);
        Assert.True(info.Success);
        Assert.Equal((uint)(4 * 1024 * 1024), protocol.EffectiveCapacity());

        ProtocolResult download = await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        Assert.True(download.Success, download.Message);
        Assert.True(device.Flash.AsSpan(0, (int)firmware.Length).SequenceEqual(firmware.Data));
    }

    [Fact]
    public async Task Download_RejectsFirmwareLargerThanDeviceFlash()
    {
        // 模拟设备容量 4MB，固件 4MB+1 应被拒绝
        FirmwareImage oversized = CreateFirmware(4 * 1024 * 1024 + 1);
        (SimulatedMscDevice _, Dc503RomProtocol protocol) = CreateProtocol();

        await protocol.GetFlashInfoAsync(CancellationToken.None);
        ProtocolResult result = await protocol.DownloadFirmwareAsync(oversized, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("超过", result.Message);
    }

    [Fact]
    public void DeviceConnection_ExposesRecognizedDeviceKind()
    {
        // 设备类型来自设备库条目 ClassInfo（对齐 MPTool SearchDev 的处理类），
        // 连接暴露 Kind/KindLabel，并在显示名中标出类型。
        DeviceEntry ispEntry = new(
            Index: 3,
            InquiryInfo: "BuildwinMedia-Player    1.00",
            ClassInfo: "AXISP",
            SpiDriverPath: "",
            FuncListPath: "",
            IsIsp: true,
            IsAdapter: false,
            IsLoader: false);
        var info = new MscDeviceInfo("\\\\?\\GLOBALROOT\\Device\\FakeTarget", 0, 0, "Buildwin Media-Player",
            VendorId: "Buildwin", ProductId: "Media-Player", IsTarget: true, MatchedEntry: ispEntry);

        using DeviceConnection conn = CreateSimulatedConnection(info);

        Assert.Equal(DeviceKind.Isp, conn.Kind);
        Assert.Equal("AXISP(ISP)", conn.KindLabel);
        Assert.StartsWith("[AXISP(ISP)]", conn.DisplayName);
    }

    [Fact]
    public void DeviceConnection_Connect_OnNonexistentPath_ReturnsNullAndLogs()
    {
        var info = TargetDevice("\\\\?\\GLOBALROOT\\Device\\NoSuchDeviceAbc");
        var logs = new List<string>();

        using DeviceConnection? conn = DeviceConnection.Connect(info, logs.Add, TimeSpan.FromSeconds(2));

        Assert.Null(conn);
        Assert.Contains(logs, l => l.Contains("正在打开设备"));
        Assert.Contains(logs, l => l.Contains("连接失败") || l.Contains("无法打开设备"));
    }

    [Fact]
    public async Task DeviceWatcher_AutoConnectsTargetDevice()
    {
        MscDeviceInfo info = TargetDevice();
        var events = new List<DeviceStateChanged>();
        using var watcher = new DeviceWatcher(
            connect: (i, log, timeout) => CreateSimulatedConnection(i),
            enumerate: () => new List<MscDeviceInfo> { info });

        watcher.DeviceChanged += events.Add;
        watcher.Start();
        await watcher.ScanNowAsync();
        await Task.Delay(200);

        Assert.Single(events);
        Assert.True(events[0].Connected);
        Assert.Single(watcher.Connections);
        Assert.Equal((uint)SimulatedMscDevice.MaxFlashSize, watcher.Connections[0].Flash!.CapacityBytes);
        Assert.Equal("EF 40 16", watcher.Connections[0].Flash!.IdText);
    }

    [Fact]
    public async Task DeviceWatcher_SkipsNonTargetDevice()
    {
        var nonTarget = new MscDeviceInfo("\\\\?\\GLOBALROOT\\Device\\UsbStick", 0xABCD, 0x1234, "SanDisk USB",
            VendorId: "SanDisk", ProductId: "USB 3.2Gen1", IsTarget: false);
        var logs = new List<string>();
        using var watcher = new DeviceWatcher(
            log: logs.Add,
            connect: (i, log, timeout) => throw new InvalidOperationException("不应尝试连接非目标设备"),
            enumerate: () => new List<MscDeviceInfo> { nonTarget });

        watcher.Start();
        await watcher.ScanNowAsync();
        await Task.Delay(200);

        Assert.Empty(watcher.Connections);
        Assert.Contains(logs, l => l.Contains("跳过非目标设备"));
    }

    [Fact]
    public async Task DeviceWatcher_AutoDisconnectsRemovedDevice()
    {
        var present = new List<MscDeviceInfo> { TargetDevice() };
        var events = new List<DeviceStateChanged>();
        using var watcher = new DeviceWatcher(
            connect: (i, log, timeout) => CreateSimulatedConnection(i),
            enumerate: () => present);

        watcher.DeviceChanged += events.Add;
        watcher.Start();
        await watcher.ScanNowAsync();
        await Task.Delay(200);
        Assert.Single(watcher.Connections);

        // 设备移除
        present.Clear();
        await watcher.ScanNowAsync();
        await Task.Delay(200);

        Assert.Equal(2, events.Count);
        Assert.False(events[^1].Connected);
        Assert.Empty(watcher.Connections);
    }

    [Fact]
    public async Task DeviceWatcher_AppStateZeroDaHandoff_NoCooldown_RetriesPromptly()
    {
        // 应用态设备 Connect 返回 null = 0xDA 已下发（预期模式切换，非失败）：
        // 不应记冷却，下一轮应立即重试连接（配合 0xDA → Loader 重新枚举）。
        MscDeviceInfo appState = TargetDevice(); // IsTarget=true, ProductId="Media-Player"（应用态）
        var logs = new List<string>();
        int attempts = 0;
        using var watcher = new DeviceWatcher(
            log: logs.Add,
            connect: (i, log, timeout) => { attempts++; return null; }, // 始终模拟 0xDA 后返回 null
            enumerate: () => new List<MscDeviceInfo> { appState });

        await watcher.ScanNowAsync();
        await Task.Delay(100);
        Assert.Equal(1, attempts);

        // 立即再扫：应用态空返回不应被冷却拦截
        await watcher.ScanNowAsync();
        await Task.Delay(100);
        Assert.Equal(2, attempts);
        Assert.Contains(logs, l => l.Contains("切换至 Loader"));
        Assert.DoesNotContain(logs, l => l.Contains("进入冷却期"));
    }

    [Fact]
    public async Task DeviceWatcher_LoaderConnectFailure_EntersCooldown()
    {
        // Loader 态设备连接失败是真失败：应记冷却，冷却期内不再重复重试。
        var loader = new MscDeviceInfo("\\\\?\\GLOBALROOT\\Device\\FakeLoader", 0, 0, "BuildWin Video050Loader",
            VendorId: "BuildWin", ProductId: "Video050Loader", IsTarget: true);
        var logs = new List<string>();
        int attempts = 0;
        using var watcher = new DeviceWatcher(
            log: logs.Add,
            failedCooldown: TimeSpan.FromSeconds(60),
            connect: (i, log, timeout) => { attempts++; return null; },
            enumerate: () => new List<MscDeviceInfo> { loader });

        await watcher.ScanNowAsync();
        await Task.Delay(100);
        Assert.Equal(1, attempts);

        // 冷却期内再扫：不应再次尝试
        await watcher.ScanNowAsync();
        await Task.Delay(100);
        Assert.Equal(1, attempts);
        Assert.Contains(logs, l => l.Contains("进入冷却期"));
    }

    [Fact]
    public async Task DeviceWatcher_StaleCooldown_ClearedWhenDeviceGone()
    {
        // 已消失设备的冷却记录应在下一轮清理：路径复用/重新出现时不被旧冷却拦截。
        MscDeviceInfo info = TargetDevice();
        int attempts = 0;
        var present = new List<MscDeviceInfo> { info };
        using var watcher = new DeviceWatcher(
            failedCooldown: TimeSpan.FromSeconds(60),
            connect: (i, log, timeout) => { attempts++; return null; },
            enumerate: () => present);

        await watcher.ScanNowAsync(); // 失败 → 记冷却
        await Task.Delay(100);
        Assert.Equal(1, attempts);

        present.Clear(); // 设备消失
        await watcher.ScanNowAsync();
        await Task.Delay(100);

        present.Add(info); // 重新出现
        await watcher.ScanNowAsync(); // 冷却已清理 → 应立即重试
        await Task.Delay(100);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task DeviceWatcher_PendingTarget_KeepsFastScan()
    {
        // 存在已出现但尚未连接的目标设备（未连接且未冷却）→ 轮询日志应出现"保持快扫间隔"，
        // 即退避被钉在最短档而不是升级到 5s/10s。
        MscDeviceInfo info = TargetDevice();
        var logs = new List<string>();
        using var watcher = new DeviceWatcher(
            log: logs.Add,
            connect: (i, log, timeout) => null, // 应用态 0xDA 空返回 → 不冷却 → 保持待连接
            enumerate: () => new List<MscDeviceInfo> { info });

        watcher.Start();
        await Task.Delay(300); // 让后台轮询循环跑几轮

        Assert.Contains(logs, l => l.Contains("存在待连接设备"));
        Assert.Contains(logs, l => l.Contains("快扫间隔"));
    }

    [Fact]
    public async Task DeviceWatcher_ConnectsMultipleDevicesConcurrently()
    {
        // 多台设备同时接入：连接应并行执行（记录的最大并发数 >= 2），而非串行逐台排队。
        // 对齐 MPTool 每设备一个线程的多线程架构（MAX_THREAD=8）。
        var devices = new List<MscDeviceInfo>();
        for (int i = 0; i < 4; i++)
            devices.Add(TargetDevice($"\\\\?\\GLOBALROOT\\Device\\FakeTarget{i}"));
        int active = 0, maxActive = 0;
        var gate = new object();
        using var release = new ManualResetEventSlim(false);

        using var watcher = new DeviceWatcher(
            connect: (info, log, timeout) =>
            {
                lock (gate) { active++; maxActive = Math.Max(maxActive, active); }
                release.Wait(400); // 模拟握手耗时，使并发真正重叠
                lock (gate) active--;
                return CreateSimulatedConnection(info);
            },
            enumerate: () => devices);

        watcher.Start();
        await watcher.ScanNowAsync();
        await Task.Delay(300);

        Assert.Equal(4, watcher.Connections.Count);
        Assert.True(maxActive >= 2, $"期望连接并行执行，实际最大并发 {maxActive}");
    }

    [Fact]
    public async Task DeviceWatcher_BoundedConcurrency_LimitsParallelConnections()
    {
        // 并发控制：maxConcurrentConnections=2 时，同一时刻进行中的连接数不得超过 2。
        // 对齐 MPTool MAX_THREAD 的有界并发语义。
        var devices = new List<MscDeviceInfo>();
        for (int i = 0; i < 4; i++)
            devices.Add(TargetDevice($"\\\\?\\GLOBALROOT\\Device\\FakeTarget{i}"));
        int active = 0, maxActive = 0;
        var gate = new object();
        using var release = new ManualResetEventSlim(false);

        using var watcher = new DeviceWatcher(
            maxConcurrentConnections: 2,
            connect: (info, log, timeout) =>
            {
                lock (gate) { active++; maxActive = Math.Max(maxActive, active); }
                release.Wait(400);
                lock (gate) active--;
                return CreateSimulatedConnection(info);
            },
            enumerate: () => devices);

        watcher.Start();
        await watcher.ScanNowAsync();
        await Task.Delay(600);

        Assert.Equal(4, watcher.Connections.Count);
        Assert.InRange(maxActive, 1, 2); // 并发被限制在 2 以内
    }

    [Fact]
    public async Task FlashService_ReusesConnectedTransport()
    {
        FirmwareImage firmware = CreateFirmware(4_096);
        MscDeviceInfo info = TargetDevice();

        using DeviceConnection? conn = CreateSimulatedConnection(info);
        Assert.NotNull(conn);

        var options = new FlashRunOptions(info, firmware, VerifyAfterDownload: true, Connected: conn);
        FlashSessionResult result = await FlashService.RunAsync(options, null, null);

        Assert.True(result.Success, result.Summary);
        Assert.Equal(FlashStage.Completed, result.FinalStage);
    }

    [Fact]
    public async Task FlashService_ReusedTransport_StaysOpen()
    {
        FirmwareImage firmware = CreateFirmware(4_096);
        MscDeviceInfo info = TargetDevice();

        using DeviceConnection? conn = CreateSimulatedConnection(info);
        Assert.NotNull(conn);

        // 会话结束后复用连接不应被 FlashService 关闭（由 DeviceConnection/DeviceWatcher 管理）
        var options = new FlashRunOptions(info, firmware, VerifyAfterDownload: true, Connected: conn);
        FlashSessionResult result = await FlashService.RunAsync(options, null, null);
        Assert.True(result.Success, result.Summary);
        Assert.True(conn.Transport.IsOpen);
    }
}
