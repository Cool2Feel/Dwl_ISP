using UpgradeTool.Core.Abstractions;

namespace UpgradeTool.Core.Protocol;

/// <summary>子设备类型（MPTool BerrySdio.ProbeDev：bootSgmt_driver_check 返回 0x01=EEPROM、0x02=Flash）。</summary>
public enum ChildDeviceType
{
    None,
    Eeprom,
    Flash,
}

/// <summary>
/// 子设备检测结果（对齐 MPTool AX2005Adapter→BerrySdio 两阶段识别）。
/// AdapterDetected = 适配器驱动上传/校验/初始化成功（适配器在线）；
/// ChildPresent    = probe_port + probe_dev 确认存在 Berry 子设备；
/// ChildType       = 子设备类型（EEPROM / Flash / None）。
/// </summary>
public sealed record ChildDeviceInfo(
    bool AdapterDetected,
    bool ChildPresent,
    ChildDeviceType ChildType,
    byte[]? FlashId,
    string? AdapterDriverName,
    string Message);

/// <summary>
/// AX2005 适配器 → Berry 子设备的两阶段检测器（对齐 MPTool AX2005Adapter.ProbeDev/MatchDev +
/// BerrySdio.LoadDriver/InitDev/ProbeDev）。
///
/// 阶段一（适配器，0xCB L3 通道，对齐 MPTool UFRunCode）：
///   1) 上传并校验适配器驱动 AXIDEsdspi.elf（Func1=MemReadWrite，加载基址=PT_LOAD vaddr+Code2xDataOffset）；
///   2) 适配器初始化（Func1=Init）；
///   3) probe_port 轮询（Func1=probe_port，data-in 16B，循环最多 0x20 次）：
///      返回 >5 连续 5 次 → 检测到子设备；返回 0 连续 0x10 次 → 无子设备；
///   4) probe_dev 确认（Func1=probe_dev，data-in 16B）：前 4 字节 == 0xAAAAAAAA → Berry 子设备。
///
/// 阶段二（子设备，0xCB 上传 + 0xCD L2 通道，对齐 MPTool UFRunCode/UFRunCode1）：
///   5) 经适配器 tgt_rw（0xCB L3 Func1=tgt_rw）上传子设备固件 AX3233AXIDE_A2.elf；
///   6) 子设备初始化（0xCD L2：Func1=mem_rw, DataAddr=NoDataAddr, Func2=eeprom_init）；
///   7) bootSgmt_driver_check（0xCD L2：Func2=bootSgmt_driver_check，data-in 4B）：
///      [0]=0x01 → EEPROM；[0]=0x02 → Flash（字节 1..3 为 ID）。
///
/// 符号地址全部从驱动 ELF 符号表解析（DriverImage），对齐 MPTool pof_read_symbol，无硬编码魔数。
/// 探测过程中的预期失败不抛异常，返回带状态的结果；仅取消（OperationCanceledException）与
/// 无法解析必需符号（InvalidDataException）向上抛出。
/// </summary>
public sealed class BerryChildDetector
{
    // 对齐 MPTool 常量
    private const int MaxDrvDataTransSize = 0x400;   // 适配器驱动上传块大小（MAX_DRV_DATA_TRANS_SIZE）
    private const int MaxDataTransSize = 0x200;      // 适配器驱动校验块大小（MAX_DATA_TRANS_SIZE）
    private const int ProbePortMaxCount = 0x20;      // probe_port 最大循环次数
    private const int ProbeOkThreshold = 5;          // probe_port 连续成功阈值 → 检测到子设备
    private const int ProbeNoDeviceThreshold = 0x10; // probe_port 连续无响应阈值 → 无子设备
    private const int ProbeDelayMs = 25;             // probe_port 轮询间隔
    private const uint BerryMagic = 0xAAAAAAAA;      // probe_dev 确认 Berry 的魔数
    private const int ProbeReadLength = 16;          // probe_port/probe_dev 读取长度

    private const string AdapterDriverFile = "AXIDEsdspi.elf"; // 适配器驱动资源名（仅用于结果报告）

    private readonly IFlashTransport _transport;
    private readonly Action<string>? _log;
    private readonly DriverImage _adapterDriver;
    private readonly DriverImage _childDriver;

    public BerryChildDetector(
        IFlashTransport transport,
        Action<string>? log = null,
        DriverImage? adapterDriver = null,
        DriverImage? childDriver = null)
    {
        _transport = transport;
        _log = log;
        _adapterDriver = adapterDriver ?? DriverImage.LoadEmbedded(AdapterDriverFile);
        _childDriver = childDriver ?? DriverImage.LoadEmbedded("AX3233AXIDE_A2.elf");
    }

    /// <summary>执行完整两阶段子设备检测。预期失败返回失败结果；取消/符号缺失异常向上抛出。</summary>
    public ChildDeviceInfo Probe(CancellationToken ct)
    {
        try
        {
            // ---- 阶段一：适配器 ----
            if (!LoadAdapterDriver(ct))
                return new ChildDeviceInfo(false, false, ChildDeviceType.None, null, AdapterDriverFile, "适配器驱动上传/校验失败。");
            if (!AdapterInit(ct))
                return new ChildDeviceInfo(false, false, ChildDeviceType.None, null, AdapterDriverFile, "适配器初始化失败。");

            bool childPresent = ProbePort(ct);
            if (!childPresent)
                return new ChildDeviceInfo(true, false, ChildDeviceType.None, null, AdapterDriverFile, "适配器在线但未检测到子设备。");
            if (!MatchDev(ct))
                return new ChildDeviceInfo(true, true, ChildDeviceType.None, null, AdapterDriverFile, "适配器检测到子设备但 probe_dev 未确认为 Berry。");

            // ---- 阶段二：子设备 ----
            if (!LoadChildDriver(ct))
                return new ChildDeviceInfo(true, true, ChildDeviceType.None, null, AdapterDriverFile, "子设备固件上传失败。");
            if (!ChildInit(ct))
                return new ChildDeviceInfo(true, true, ChildDeviceType.None, null, AdapterDriverFile, "子设备初始化失败。");
            return ProbeChild(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"子设备检测异常: {ex.Message}");
            return new ChildDeviceInfo(false, false, ChildDeviceType.None, null, AdapterDriverFile, $"子设备检测异常: {ex.Message}");
        }
    }

    // ---------- 阶段一：适配器（0xCB L3） ----------

    /// <summary>上传并回读校验适配器驱动到适配器内存（对齐 MPTool InstallDriver/CheckDriver）。</summary>
    private bool LoadAdapterDriver(CancellationToken ct)
    {
        uint code2xData = _adapterDriver.Resolve("Code2xDataOffset");
        uint loadBase = _adapterDriver.LoadAddr + code2xData;
        uint memReadWrite = _adapterDriver.ResolveOrThrow("MemReadWrite");
        byte[] segment = _adapterDriver.Segment;
        _log?.Invoke($"[Adapter] 上传适配器驱动 {segment.Length} 字节 → 0x{loadBase:X4}（LoadAddr=0x{_adapterDriver.LoadAddr:X4} Code2xDataOffset=0x{code2xData:X4}）...");

        // InstallDriver：0xCB L3 Func1=MemReadWrite, DataAddr=loadBase+i, Func2=NoL2, data-out 分块
        for (int i = 0; i < segment.Length; i += MaxDrvDataTransSize)
        {
            ct.ThrowIfCancellationRequested();
            int len = Math.Min(MaxDrvDataTransSize, segment.Length - i);
            ScsiCommandResult r = _transport.SendDataOut(
                AdapterCommands.BuildCdb(memReadWrite, loadBase + (uint)i, AdapterCommands.NoL2, 0),
                segment.AsSpan(i, len).ToArray());
            if (!r.Success)
            {
                _log?.Invoke($"[Adapter]   驱动上传失败 @0x{i:X}: {r.DescribeError()}");
                return false;
            }
        }

        // CheckDriver：data-in 分块回读校验
        for (int i = 0; i < segment.Length; i += MaxDataTransSize)
        {
            ct.ThrowIfCancellationRequested();
            int len = Math.Min(MaxDataTransSize, segment.Length - i);
            ScsiCommandResult r = _transport.SendDataIn(
                AdapterCommands.BuildCdb(memReadWrite, loadBase + (uint)i, AdapterCommands.NoL2, 0), len);
            if (!r.Success || r.Response == null || r.Response.Length < len ||
                !r.Response.AsSpan(0, len).SequenceEqual(segment.AsSpan(i, len)))
            {
                _log?.Invoke($"[Adapter]   驱动校验失败 @0x{i:X}（{r.Success} / {r.Response?.Length ?? 0}/{len} 字节）。");
                return false;
            }
        }

        _log?.Invoke("[Adapter] 适配器驱动上传并校验通过。");
        return true;
    }

    /// <summary>适配器初始化（对齐 MPTool InitDev：0xCB L3 Func1=Init，data-out 0）。</summary>
    private bool AdapterInit(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        uint init = _adapterDriver.ResolveOrThrow("Init");
        ScsiCommandResult r = _transport.SendDataOut(AdapterCommands.BuildCdb(init, 0), Array.Empty<byte>());
        if (!r.Success)
        {
            _log?.Invoke($"[Adapter] 适配器 Init 失败: {r.DescribeError()}");
            return false;
        }
        _log?.Invoke("[Adapter] 适配器初始化完成。");
        return true;
    }

    /// <summary>probe_port 轮询检测子设备（对齐 MPTool ProbeDev）。</summary>
    private bool ProbePort(CancellationToken ct)
    {
        uint probe = _adapterDriver.ResolveOrThrow("probe_port");
        int cntOk = 0, cntNot = 0;
        _log?.Invoke("[Adapter] 轮询 probe_port 检测子设备（最多 0x20 次）...");
        for (int n = 0; n < ProbePortMaxCount; n++)
        {
            ct.ThrowIfCancellationRequested();
            ScsiCommandResult r = _transport.SendDataIn(AdapterCommands.BuildCdb(probe, 0), ProbeReadLength);
            if (!r.Success || r.Response == null || r.Response.Length < 1)
            {
                _log?.Invoke($"[Adapter]   probe_port 第 {n + 1} 次失败: {r.DescribeError()}");
                return false; // 通道异常，视为无子设备
            }
            byte val = r.Response[0];
            if (val > 0x05)
                cntOk++;
            else if (val == 0 && n == cntNot)
                cntNot++;
            if (cntOk >= ProbeOkThreshold)
            {
                _log?.Invoke($"[Adapter] probe_port 检测到子设备（连续 {cntOk} 次成功）。");
                return true;
            }
            if (cntNot >= ProbeNoDeviceThreshold)
            {
                _log?.Invoke($"[Adapter] probe_port 未检测到子设备（连续 {cntNot} 次无响应）。");
                return false;
            }
            Thread.Sleep(ProbeDelayMs);
        }
        _log?.Invoke("[Adapter] probe_port 达到最大轮询次数，视为无子设备。");
        return false;
    }

    /// <summary>probe_dev 确认 Berry 子设备（对齐 MPTool MatchDev）。</summary>
    private bool MatchDev(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        uint matchDev = _adapterDriver.ResolveOrThrow("probe_dev");
        ScsiCommandResult r = _transport.SendDataIn(AdapterCommands.BuildCdb(matchDev, 0), ProbeReadLength);
        if (!r.Success || r.Response == null || r.Response.Length < 4)
        {
            _log?.Invoke($"[Adapter] probe_dev 失败: {r.DescribeError()}");
            return false;
        }
        uint magic = r.Response[0] | ((uint)r.Response[1] << 8) | ((uint)r.Response[2] << 16) | ((uint)r.Response[3] << 24);
        bool berry = magic == BerryMagic;
        _log?.Invoke(berry
            ? "[Adapter] probe_dev 确认为 Berry 子设备（0xAAAAAAAA）。"
            : $"[Adapter] probe_dev 返回 0x{magic:X8}，非 Berry 子设备。");
        return berry;
    }

    // ---------- 阶段二：子设备 ----------

    /// <summary>经适配器 tgt_rw 上传子设备固件（对齐 MPTool BerrySdio.LoadDriver，0xCB L3 Func1=tgt_rw）。</summary>
    private bool LoadChildDriver(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        uint tgtRw = _adapterDriver.ResolveOrThrow("tgt_rw");
        byte[] childSeg = _childDriver.Segment;
        _log?.Invoke($"[Child] 经适配器 tgt_rw 上传子设备固件 {childSeg.Length} 字节...");
        ScsiCommandResult r = _transport.SendDataOut(AdapterCommands.BuildCdb(tgtRw, 0), childSeg);
        if (!r.Success)
        {
            _log?.Invoke($"[Child]   子设备固件上传失败: {r.DescribeError()}");
            return false;
        }
        _log?.Invoke("[Child] 子设备固件上传完成。");
        return true;
    }

    /// <summary>子设备初始化（对齐 MPTool BerrySdio.InitDev：0xCD L2 Func1=mem_rw, Func2=eeprom_init）。</summary>
    private bool ChildInit(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        uint memRw = _childDriver.ResolveOrThrow("mem_rw");
        uint eepromInit = _childDriver.ResolveOrThrow("eeprom_init");
        ScsiCommandResult r = _transport.SendDataIn(
            Dc503RomCommands.BuildCdb(memRw, Dc503RomCommands.NoDataAddr, eepromInit, 0), 0);
        if (!r.Success)
        {
            _log?.Invoke($"[Child]   eeprom_init 失败: {r.DescribeError()}");
            return false;
        }
        _log?.Invoke("[Child] 子设备初始化完成。");
        return true;
    }

    /// <summary>bootSgmt_driver_check 识别子设备类型（对齐 MPTool BerrySdio.ProbeDev）。</summary>
    private ChildDeviceInfo ProbeChild(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        uint memRw = _childDriver.ResolveOrThrow("mem_rw");
        uint driverCheck = _childDriver.ResolveOrThrow("bootSgmt_driver_check");
        ScsiCommandResult r = _transport.SendDataIn(
            Dc503RomCommands.BuildCdb(memRw, Dc503RomCommands.NoDataAddr, driverCheck, 0), 4);
        if (!r.Success || r.Response == null || r.Response.Length < 1)
        {
            _log?.Invoke($"[Child]   bootSgmt_driver_check 失败: {r.DescribeError()}");
            return new ChildDeviceInfo(true, true, ChildDeviceType.None, null, AdapterDriverFile, "子设备类型识别失败。");
        }

        byte kind = r.Response[0];
        if (kind == 0x01)
        {
            _log?.Invoke("[Child] 子设备类型: EEPROM（bootSgmt_driver_check=0x01）。");
            return new ChildDeviceInfo(true, true, ChildDeviceType.Eeprom, null, AdapterDriverFile, "已识别子设备: EEPROM。");
        }
        if (kind == 0x02)
        {
            // 对齐 MPTool BerrySdio.ProbeDev：ID 位于响应字节 1..3（字节 0 是类型 0x02），
            // FlashLib 匹配用打包 id = buf[1]<<8 | buf[2]<<16 | buf[3]<<24。
            byte[] id = r.Response.Length >= 4
                ? new[] { r.Response[1], r.Response[2], r.Response[3] }
                : r.Response.Length >= 2 ? r.Response[1..] : Array.Empty<byte>();
            uint packed = id.Length >= 3
                ? ((uint)id[0] << 8) | ((uint)id[1] << 16) | ((uint)id[2] << 24)
                : 0;
            _log?.Invoke($"[Child] 子设备类型: Flash（bootSgmt_driver_check=0x02），ID-9F 打包=0x{packed:X8}（原始 {Convert.ToHexString(id)}）。");
            return new ChildDeviceInfo(true, true, ChildDeviceType.Flash, id, AdapterDriverFile, $"已识别子设备: Flash（ID {Convert.ToHexString(id)}）。");
        }

        _log?.Invoke($"[Child] bootSgmt_driver_check 返回未知类型 0x{kind:X2}，无法识别子设备。");
        return new ChildDeviceInfo(true, true, ChildDeviceType.None, null, AdapterDriverFile, $"子设备类型未知（0x{kind:X2}）。");
    }
}
