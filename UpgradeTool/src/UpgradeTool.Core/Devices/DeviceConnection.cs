using UpgradeTool.Core.Abstractions;
using UpgradeTool.Core.Protocol;
using UpgradeTool.Core.Transport;

namespace UpgradeTool.Core.Devices;

/// <summary>
/// 一个已建立连接（含传输层与协议层）的设备实例。
/// 由 DeviceWatcher 在检测到匹配设备时自动创建，连接已就绪（Loader 驱动上传 + SPI0 初始化 + RDID/容量已查询）。
/// </summary>
public sealed class DeviceConnection : IDisposable
{
    private readonly IFlashTransport _transport;
    private readonly IFlashProtocol _protocol;
    private bool _disposed;

    internal DeviceConnection(MscDeviceInfo info, IFlashTransport transport, IFlashProtocol protocol, FlashInfo? flash)
    {
        Info = info;
        _transport = transport;
        _protocol = protocol;
        Flash = flash;
    }

    public MscDeviceInfo Info { get; }

    public IFlashTransport Transport => _transport;

    public IFlashProtocol Protocol => _protocol;

    /// <summary>设备端 Flash 信息（自动连接时已查询；可能为 null 表示查询失败/未知）。</summary>
    public FlashInfo? Flash { get; private set; }

    public bool IsFlashKnown => Flash is { CapacityBytes: > 0 };

    /// <summary>识别出的设备类型（来自设备库条目 ClassInfo，对齐 MPTool SearchDev 的处理类）。</summary>
    public DeviceKind Kind => Info.MatchedEntry?.Kind ?? DeviceKind.Unknown;

    /// <summary>设备类型可读标签（供界面/日志显示）。</summary>
    public string KindLabel => Info.MatchedEntry?.KindLabel ?? "";

    /// <summary>用于设备列表显示的文本（含设备类型标签）。</summary>
    public string DisplayName
    {
        get
        {
            string baseName = IsFlashKnown
                ? $"{Info.DisplayName} [Flash {Flash!.CapacityText}]"
                : Info.DisplayName;
            return Kind == DeviceKind.Unknown ? baseName : $"[{KindLabel}] {baseName}";
        }
    }

    /// <summary>
    /// 建立连接：创建传输层并打开，识别设备状态并建立协议。
    ///
    /// 策略（对齐参考项目 MPTool）：Loader 态是生产主通道。
    ///   应用态设备 -> 直接下发 0xDA 切换至 Loader 模式（不使用 0xCD 应用态通道 + SPI0 stub），
    ///                  返回 null，设备断开 USB 并重新枚举为 Loader 态设备，后续轮询周期走 0xCB 生产通道；
    ///   Loader/Bootloader 态设备 -> 走 0xCB 生产通道（ThunderSE 驱动）查询 Flash 信息。
    /// </summary>
    public static DeviceConnection? Connect(
        MscDeviceInfo info, Action<string>? log = null, TimeSpan? flashQueryTimeout = null)
    {
        // 握手阶段用短命令超时：设备不响应（如处于 Bootloader/Loader 模式）时快速失败，
        // 不阻塞到 200s；刷写阶段由 FlashService 恢复较长超时。
        var transport = new MscScsiTransport(info.DevicePath)
        {
            CommandTimeout = TimeSpan.FromSeconds(3),
            // 连接/握手阶段的 SCSI 命令级日志（探针/stub 上传/Flash 查询），便于排查"设备识别不上/连接失败"
            Log = log,
        };

        try
        {
            log?.Invoke($"正在打开设备: {info.DevicePath}");
            transport.Open();
            log?.Invoke($"设备已打开: {transport.DeviceLabel}");

            // 真实设备先跑传输层探针，分离"SCSI 通道不可用"与"0xCD 厂商通道失败"两类原因
            ProbeReport probe = ConnectionProbe.Run(transport, log);
            if (!probe.TransportOk)
            {
                log?.Invoke($"设备 {info.DisplayName} 传输层探针失败，忽略: {probe.Summary}");
                transport.Close();
                transport.Dispose();
                return null;
            }
            log?.Invoke($"传输层探针通过: {probe.Summary}");

            // 探针通过后恢复较长超时：Loader 驱动上传（整个段一次性 SCSI Data-Out）需要比 3s 更多的时间，
            // 否则会触发错误码 121 (ERROR_SEM_TIMEOUT)。
            transport.CommandTimeout = TimeSpan.FromSeconds(10);

            // 权威识别：以实时 SCSI INQUIRY 数据为准重新识别设备（枚举阶段用的是驱动缓存的
            // STORAGE 描述符，两者可能因缓存/截断/填充差异不一致）。以 INQUIRY 识别出的
            // 设备库条目（对齐 MPTool SearchDeviceID→ClassInfo/SpiDriverPath）作为单一数据源，
            // 未命中时回退到枚举阶段条目；两者都为空时回退到内置 pattern。
            InquiryIdentity? inquiry = probe.InquiryIdentity;
            string? vendor = inquiry?.VendorId ?? info.VendorId;
            string? product = inquiry?.ProductId ?? info.ProductId;
            string? revision = inquiry?.ProductRevision;
            DeviceSignature.DeviceRecognition rec = DeviceSignature.Recognize(vendor, product, revision);
            DeviceEntry? entry = rec.Entry ?? info.MatchedEntry;
            if (inquiry != null)
                log?.Invoke($"实时 INQUIRY 身份: 厂商=\"{vendor}\" 产品=\"{product}\" 版本=\"{revision}\" → 目标={rec.IsTarget}，设备库条目={entry?.ClassInfo ?? "无"}");

            // 以权威身份构建 refinedInfo，供协议选择与 UI 显示使用（厂商/产品/设备库条目来自 INQUIRY）。
            // IsTarget 保持枚举接入时的判定，不因 INQUIRY 的差异而降级（设备已被识别为目标才走到这里）。
            MscDeviceInfo refinedInfo = info with
            {
                VendorId = vendor,
                ProductId = product,
                IsTarget = info.IsTarget || rec.IsTarget,
                MatchedEntry = entry,
            };

            // 对齐参考项目 MPTool：Loader 态是生产主通道。
            // 设备类型由设备库 ClassInfo 解析（对齐 MPTool SearchDev 按 ClassInfo 派发处理类）：
            //   Loader 态设备 → 0xCB 生产通道；
            //   其余类型（AXISP / AX326X 直连SPI / AX3233RP 量产 / AX2005Adapter 适配器等应用态设备）
            //   → 直接下发 0xDA 进入升级模式后重新枚举为 Loader，不使用 0xCD 应用态通道。
            DeviceKind kind = entry?.Kind ?? (DeviceSignature.IsLoader(vendor, product) ? DeviceKind.Loader : DeviceKind.Unknown);
            bool loader = kind == DeviceKind.Loader;
            string kindLabel = kind switch
            {
                DeviceKind.Loader => "Loader",
                _ => entry?.KindLabel ?? "未知",
            };
            log?.Invoke($"检测设备类型: {kindLabel}（ClassInfo={entry?.ClassInfo ?? "无"}）→ 进入{(loader ? "0xCB Loader 通道" : "0xDA 升级模式")}。");
            if (!loader)
            {
                // AX2005Adapter 适配器：不做 0xDA 升级，而是执行两阶段子设备检测
                // （对齐 MPTool AX2005Adapter→BerrySdio：上传驱动 → probe_port → probe_dev →
                //  经 tgt_rw 上传子设备固件 → bootSgmt_driver_check 识别 EEPROM/Flash）。
                // 检测结果完整记入日志（含子设备类型/Flash ID）。当前工具以 Loader 态为生产主通道，
                // 适配器+子设备的刷写通道尚未接入，故不建立可刷写连接，等待后续扩展。
                if (kind == DeviceKind.Adapter)
                {
                    log?.Invoke($"AX2005Adapter 适配器设备：执行子设备（BerrySdio）两阶段检测...");
                    ChildDeviceInfo child = new BerryChildDetector(transport, log).Probe(CancellationToken.None);
                    log?.Invoke($"子设备检测结果: {child.Message}");
                    transport.Close();
                    transport.Dispose();
                    return null;
                }

                string enterReason = kind switch
                {
                    DeviceKind.Isp => "AXISP ISP 设备",
                    DeviceKind.DirectSpi => "AX326X 直连 SPI 设备",
                    DeviceKind.LegacyRp => "AX3233RP 量产设备",
                    _ => "应用态设备",
                };
                log?.Invoke($"{enterReason}：下发 0xDA 进入升级模式后重新枚举为 Loader（对齐 MPTool UFIsp/模式切换，不使用 0xCD 应用态通道）...");
                // 对齐 MPTool ReadFromScsi：使用 SCSI_IOCTL_DATA_IN 方向（bmCBWFlags=0x80），
                // 而非 SCSI_IOCTL_DATA_UNSPECIFIED（bmCBWFlags=0x00）。设备固件检查
                // bmCBWFlags 方向位，方向错误时返回 CHECK CONDITION / NOT READY 拒绝命令。
                // 此处 dataLength=0 无实际数据传输，仅方向位影响固件判定。
                ScsiCommandResult daResult = transport.SendDataIn(UpdateModeCommand.BuildCdb(), 0);
                // 0xDA 下发后设备立即复位 USB，SendDataIn 可能返回 55（设备未连接）或 31（设备未就绪），
                // 都视为命令已送达，设备正在重新枚举为 Loader 模式。
                // 也可能返回 SCSI CHECK CONDITION（0x02），仍视为成功——设备已进入复位流程。
                if (daResult.Success || daResult.ErrorCode is 55 or 31 || daResult.ScsiStatus == 0x02)
                    log?.Invoke($"0xDA 已下发，设备将断开并重新枚举为 Loader 模式，等待设备重新识别...");
                else
                    log?.Invoke($"0xDA 下发失败: {daResult.DescribeError()}。");
                transport.Close();
                transport.Dispose();
                return null;
            }

            // Loader/Bootloader 态设备：走 0xCB 生产通道（ThunderSE 驱动），查询 Flash 信息。
            // 传入 refinedInfo（含权威识别出的 DeviceEntry），使适配器类别（ClassInfo）与
            // 驱动（SpiDriverPath）由设备库单一数据源决定，对齐 MPTool SearchDev。
            IFlashProtocol protocol = ProtocolFactory.CreateForDevice(transport, refinedInfo, log);
            log?.Invoke($"使用协议: {protocol.Name}");
            using var cts = flashQueryTimeout.HasValue ? new CancellationTokenSource(flashQueryTimeout.Value) : new CancellationTokenSource();
            ProtocolResult<FlashInfo> result = protocol.GetFlashInfoAsync(cts.Token).GetAwaiter().GetResult();
            if (!result.Success)
            {
                log?.Invoke($"设备 {info.DisplayName} Flash 查询失败，忽略: {result.Message}");
                transport.Close();
                transport.Dispose();
                return null;
            }
            FlashInfo flash = result.Value!;
            log?.Invoke($"设备 {info.DisplayName} 连接就绪: {flash.IdText} / {flash.CapacityText}");
            return new DeviceConnection(refinedInfo, transport, protocol, flash);
        }
        catch (OperationCanceledException)
        {
            log?.Invoke($"设备 {info.DisplayName} Flash 查询超时，忽略。");
            transport.Close();
            transport.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            log?.Invoke($"设备 {info.DisplayName} 连接失败: {ex.Message}");
            transport.Close();
            transport.Dispose();
            return null;
        }
    }

    /// <summary>重新查询设备端 Flash 信息（设备可能被替换/重插）。</summary>
    public FlashInfo? RefreshFlash(Action<string>? log = null)
    {
        if (_disposed)
            return null;
        ProtocolResult<FlashInfo> result = _protocol.GetFlashInfoAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (result.Success)
            Flash = result.Value;
        else
            log?.Invoke($"Flash 信息刷新失败: {result.Message}");
        return Flash;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _transport.Close();
        _transport.Dispose();
    }
}
