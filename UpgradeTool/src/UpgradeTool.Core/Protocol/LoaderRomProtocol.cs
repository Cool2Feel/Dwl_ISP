using System.Diagnostics;
using UpgradeTool.Core.Abstractions;
using UpgradeTool.Core.Utilities;

namespace UpgradeTool.Core.Protocol;

/// <summary>
/// Loader（下载器）模式固件升级协议：走 0xCB 厂商命令通道。
/// 先上传 ThunderSE.elf 驱动段到设备 RAM，再经 l1_func_signal_drive 派发 SPI 命令，
/// 完成固件下载与回读校验。
///
/// MPTool 真机抓包确认（777.txt 设备识别 + 6666.txt 固件烧录）：
///   - 0xCB 命令为 16 字节 CDB：Func1=CDB[1..4]（LE）、DataAddr=CDB[5..8]（LE）、Func2=CDB[9..12]（LE）；
///   - 驱动上传 = Func1=RBC_mem_rwex，DataAddr=RAM 偏移，Func2=NoL2，data-out 1KB/块（777.txt L115-178）；
///   - SPI init = Func1=l1_func_spi_init(0x24)，Func2=0（777.txt L307）；
    ///   - ID 探测 = Func1=l1_func_signal_drive(0x74)，DataAddr=0x04070000，CDB[9]=SPI 命令码，data-in 4B（333.txt）；
    ///     DataAddr 不影响结果（loader 的 L2 SPI 读驱动固定落到 0x04070000）；`1f ff ff ff` 是设备侧状态
    ///     （MPTool 抓包 321/555/888 同样出现且放弃烧写），正常机器 0x9F 返回 85 60 16 85。
///   - 写 flash = Func1=RBC_mem_rwex_CPU，DataAddr=RBC_mem_rwex_buf(0x4a00)，Func2=l2_func_spi_page_program(0x208)；
///   - 读 flash = Func1=l1_func_signal_drive(0x74)，DataAddr=0x04070000，flash 地址大端编码在 Func2；
///   - RDID / 擦除 = Func1=l1_func_signal_drive(0x74)，Func2=SPI 命令码，DataAddr=flash 地址。
/// </summary>
public sealed class LoaderRomProtocol : IFlashProtocol, IFlashCapacityInfo
{
    /// <summary>回读校验的分块大小。</summary>
    private const int ReadChunk = 512;

    /// <summary>SPI NOR flash 页大小。</summary>
    private const int PageSize = 256;

    /// <summary>引导扇区大小（CODE_BANK_SIZE），最后写入以防止烧录中断时设备意外启动。</summary>
    private const int BootSectorSize = 512;

    /// <summary>状态寄存器 BUSY 位轮询的最大重试次数（每次间隔 10ms，最长约 3 秒）。</summary>
    private const int StatusPollMaxRetries = 300;

    private readonly IFlashTransport _transport;
    private readonly LoaderConfig _config;
    private readonly Action<string>? _log;

    private readonly uint _l1SpiInit;
    private readonly uint _l1SignalDrive;
    private readonly uint _l2PageProgram;
    private readonly uint _l2FuncReset;

    private uint _deviceCapacity;
    private byte[] _deviceId = Array.Empty<byte>();
    private FlashCommandSet _flashSet = new();

    public LoaderRomProtocol(
        IFlashTransport transport,
        LoaderConfig? config = null,
        Action<string>? log = null)
    {
        _transport = transport;
        _config = config ?? LoaderConfig.Create(LoaderImage.LoadEmbedded(), FlashLib.LoadEmbedded());
        _log = log;

        // ET_REL 段内偏移 + 上传基址 = 入口绝对地址（LoaderImage.Resolve）
        _l1SpiInit = _config.Image.Resolve("l1_func_spi_init", _config.UploadBase);
        _l1SignalDrive = _config.Image.Resolve("l1_func_signal_drive", _config.UploadBase);
        _l2PageProgram = _config.Image.Resolve("l2_func_spi_page_program", _config.UploadBase);
        _l2FuncReset = _config.Image.Resolve("l2_func_reset", _config.UploadBase);

        // 驱动/固件函数地址来自所选驱动 ELF 符号表（对齐 MPTool pof 解析 ELF 符号）
        _log?.Invoke($"Loader 驱动: {_config.DriverName}（RBC_mem_rwex=0x{_config.RbcMemRwex:X8}, RBC_mem_rwex_buf=0x{_config.RbcMemRwexBuf:X8}, " +
                     $"l1_func_spi_init=0x{_l1SpiInit:X8}, l1_func_signal_drive=0x{_l1SignalDrive:X8}, " +
                     $"l2_func_spi_page_program=0x{_l2PageProgram:X8}, l2_func_reset=0x{_l2FuncReset:X8}）");
    }

    public string Name => "Loader 模式协议 (0xCB + ThunderSE 驱动)";

    public uint DeviceCapacity => _deviceCapacity;

    /// <summary>当前有效 Flash 容量：优先设备端报告值，未知时回退 4MB。</summary>
    public uint EffectiveCapacity() => _deviceCapacity != 0 ? _deviceCapacity : 4 * 1024 * 1024;

    public Task<ProtocolResult<FlashInfo>> GetFlashInfoAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            Report(null, FlashStage.OpeningDevice, 0, "上传 ThunderSE 驱动并查询 Flash 信息...");

            ProtocolResult<FlashInfo> prepare = PrepareDriverAndQuery(ct);
            if (!prepare.Success)
                return Task.FromResult(prepare);
            if (prepare.Value is null)
                return Task.FromResult(ProtocolResult<FlashInfo>.Fail("Flash 信息读取失败。"));

            _log?.Invoke($"设备 Flash: {prepare.Value.IdText} / {prepare.Value.CapacityText}");
            return Task.FromResult(prepare);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(ProtocolResult<FlashInfo>.Fail($"Flash 信息查询失败: {ex.Message}"));
        }
    }

    /// <summary>
    /// 上传 ThunderSE 驱动 -> SPI 初始化 -> 多命令探测 Flash ID -> 解析容量。
    /// 供 GetFlashInfoAsync / DownloadFirmwareAsync 共用。
    /// 对齐 MPTool 777.txt 流程：驱动上传 -> SPI init(Func2=0) -> 0x9F/0xAB/0x90 多命令探测。
    /// </summary>
    private ProtocolResult<FlashInfo> PrepareDriverAndQuery(CancellationToken ct)
    {
        // 1) 上传 ThunderSE.elf PT_LOAD 段到设备 RAM（777.txt L115-178）
        ct.ThrowIfCancellationRequested();
        _log?.Invoke("[PrepareDriver] 开始上传 ThunderSE 驱动到设备 RAM...");
        ProtocolResult upload = UploadDriver(ct);
        if (!upload.Success)
            return ProtocolResult<FlashInfo>.Fail(upload.Message);
        _log?.Invoke("[PrepareDriver] ThunderSE 驱动上传完成。");

        // 2) SPI0 初始化（Func2=0，使设备从已上传驱动表加载配置；777.txt L307）
        ct.ThrowIfCancellationRequested();
        _log?.Invoke("[PrepareDriver] SPI0 初始化 (Func1=l1_func_spi_init, Func2=0)...");
        ScsiCommandResult init = _transport.SendCommand(
            LoaderRomCommands.BuildCdb(_l1SpiInit, 0, 0, 0));
        if (!init.Success)
            return ProtocolResult<FlashInfo>.Fail($"SPI0 初始化失败: {init.DescribeError()}。");
        _log?.Invoke("[PrepareDriver] SPI0 初始化成功。");

        // 2a) SPI init 后等待 10ms，确保 SPI 控制器时钟稳定
        _log?.Invoke("[PrepareDriver] SPI0 初始化后等待 10ms 使时钟稳定...");
        Thread.Sleep(10);
        _log?.Invoke("[PrepareDriver] 等待完成。");

        // 3) 多种 SPI 命令探测 Flash ID（777.txt L316-364: 0x9F / 0xAB / 0x90，各读 4 字节）
        //    每次探测前发送 0x00 NOP 预备命令同步 SPI 总线；读命令 DataAddr=FlashReadBuf(0x04070000)。
        //    DataAddr 只是约定（L2 SPI 读驱动固定落到 0x04070000），不影响结果。
        ct.ThrowIfCancellationRequested();
        byte[]? bestId = null;
        FlashDeviceSpec? device = null;

        // 3a) 首次 0x9F 探测与重试逻辑（复刻 MPTool 321.txt：init Func2=0 → NoL2 → 0 三连重试）
        //     如果 0x9F 返回 1F FF FF FF，按 MPTool 相同顺序重试 SPI init + 0x9F。
        //     注意：1F FF FF 是设备侧真实状态（MPTool 抓包 321/555/888 同样出现且最终放弃烧写），
        //     不是 SPI 时钟或 DataAddr 问题；保留重试仅为了与 MPTool 行为一致。
        for (int retry = 0; retry < 3; retry++)
        {
            // NOP 预备命令（signal_drive 格式：SPI 命令 0x00 在 CDB[8]）
            _transport.SendCommand(
                LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, 0x00, 0));

            uint func2 = retry switch
            {
                0 => 0u,                       // 第 1 次：Func2=0（原始配置）
                1 => LoaderRomCommands.NoL2,   // 第 2 次：Func2=NoL2（重试模式）
                _ => 0u,                        // 第 3 次：Func2=0（回退原始）
            };
            string func2desc = func2 == LoaderRomCommands.NoL2 ? "NoL2" : "0";

            _log?.Invoke($"[PrepareDriver] 探测 Flash ID: SPI 命令 0x9F（9F），DataAddr=0x{LoaderConfig.FlashReadBuf:X8}，读取 4 字节（尝试 {retry + 1}/3，SPI init Func2={func2desc}）...");

            // 如果需要重试，先重新初始化 SPI（改变 Func2 模式）
            if (retry > 0)
            {
                _log?.Invoke($"[PrepareDriver]   重试 SPI 初始化 (Func2={func2desc})...");
                ScsiCommandResult retryInit = _transport.SendCommand(
                    LoaderRomCommands.BuildCdb(_l1SpiInit, 0, func2, 0));
                if (!retryInit.Success)
                {
                    _log?.Invoke($"[PrepareDriver]   重试 SPI 初始化失败: {retryInit.DescribeError()}，跳过。");
                    continue;
                }
                _log?.Invoke($"[PrepareDriver]   重试 SPI 初始化成功，等待 10ms 稳定...");
                Thread.Sleep(10);
                _log?.Invoke($"[PrepareDriver]   等待完成。");

                // 重试前再发一次 NOP
                _transport.SendCommand(
                    LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, 0x00, 0));
            }

            ScsiCommandResult r = _transport.SendDataIn(
                LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, 0x9F, 0), 4);
            if (!r.Success || r.Response == null || r.Response.Length < 3)
            {
                _log?.Invoke($"[PrepareDriver]   命令 0x9F 失败（{r.DescribeError()}），跳过。");
                continue;
            }

            byte[] id = r.Response;
            string idHex = $"{id[0]:X2} {id[1]:X2} {id[2]:X2}" + (id.Length >= 4 ? $" {id[3]:X2}" : "");
            _log?.Invoke($"[PrepareDriver]   返回值: {idHex}");

            if (id[0] == 0xFF && id[1] == 0xFF && id[2] == 0xFF)
            {
                _log?.Invoke($"[PrepareDriver]   全 0xFF，Flash 未响应，跳过。");
                continue;
            }
            if (id[0] == 0x00 && id[1] == 0x00 && id[2] == 0x00)
            {
                _log?.Invoke($"[PrepareDriver]   全 0x00，Flash 未响应，跳过。");
                continue;
            }

            // 检查是否 1F FF FF（疑似 SPI 时钟异常）
            if (id[0] == 0x1F && id[1] == 0xFF && id[2] == 0xFF)
            {
                _log?.Invoke($"[PrepareDriver]   返回值模式为 1F FF FF（设备未响应有效 ID，与 MPTool 抓包 321/555/888 一致），第 4 字节={(id.Length >= 4 ? $"{id[3]:X2}" : "N/A")}。");
                if (retry < 2)
                {
                    _log?.Invoke($"[PrepareDriver]   将尝试重试 SPI init (Func2={(retry == 0 ? "NoL2" : "0")}) 后重新读取...");
                    continue;
                }
                _log?.Invoke($"[PrepareDriver]   3 次重试均返回 1F FF FF，中止烧写（对齐 MPTool 抓包 321/555/888 行为）。");
                // 1F FF FF 表示 SPI 时钟异常/设备未有效响应，MPTool 在此情形放弃烧写。
                // 直接中止，避免用默认 W25Q32 指令集向未知/坏 Flash 盲目擦写（仅靠容量 pattern 兜底不充分）。
                return ProtocolResult<FlashInfo>.Fail(
                    "SPI Flash ID 读取异常：3 次均返回 1F FF FF（疑似 SPI 时钟异常或设备未进入 Loader 模式）。请检查连接后重试。");
            }

            // 尝试 FlashLib 匹配
            _log?.Invoke($"[PrepareDriver]   尝试 FlashLib 匹配 (method=9F)...");
            FlashDeviceSpec? match = _config.FlashLib?.Match("9F", id);
            if (match != null)
            {
                _log?.Invoke($"[PrepareDriver]   匹配成功: {match.Name} (容量=0x{match.Capacity:X})");
                device = match;
                bestId = id;
                break;
            }

            _log?.Invoke($"[PrepareDriver]   FlashLib 无匹配，保留为候选 ID。");
            bestId ??= id;
            break;
        }

        // 3b) 继续用 0xAB/0x90 补充探测（仅在 0x9F 未匹配时执行）
        if (device == null)
        {
            foreach ((string method, byte spiCmd) in new[] { ("AB", (byte)0xAB), ("90", (byte)0x90), ("15", (byte)0x15) })
            {
                // NOP 预备命令（signal_drive 格式：SPI 命令 0x00 在 CDB[8]）
                _transport.SendCommand(
                    LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, 0x00, 0));

                _log?.Invoke($"[PrepareDriver] 探测 Flash ID: SPI 命令 0x{spiCmd:X2}（{method}），读取 4 字节...");
                ScsiCommandResult r = _transport.SendDataIn(
                    LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, spiCmd, 0), 4);
                if (!r.Success || r.Response == null || r.Response.Length < 3)
                {
                    _log?.Invoke($"[PrepareDriver]   命令 0x{spiCmd:X2} 失败（{r.DescribeError()}），跳过。");
                    continue;
                }

                byte[] id = r.Response;
                string idHex = $"{id[0]:X2} {id[1]:X2} {id[2]:X2}" + (id.Length >= 4 ? $" {id[3]:X2}" : "");
                _log?.Invoke($"[PrepareDriver]   返回值: {idHex}");

                if (id[0] == 0xFF && id[1] == 0xFF && id[2] == 0xFF)
                {
                    _log?.Invoke($"[PrepareDriver]   全 0xFF，Flash 未响应，跳过。");
                    continue;
                }
                if (id[0] == 0x00 && id[1] == 0x00 && id[2] == 0x00)
                {
                    _log?.Invoke($"[PrepareDriver]   全 0x00，Flash 未响应，跳过。");
                    continue;
                }

                _log?.Invoke($"[PrepareDriver]   尝试 FlashLib 匹配 (method={method})...");
                FlashDeviceSpec? match = _config.FlashLib?.Match(method, id);
                if (match != null)
                {
                    _log?.Invoke($"[PrepareDriver]   匹配成功: {match.Name} (容量=0x{match.Capacity:X})");
                    device = match;
                    bestId = id;
                    break;
                }

                _log?.Invoke($"[PrepareDriver]   FlashLib 无匹配，保留为候选 ID。");
                bestId ??= id;
            }
        }

        if (bestId == null)
            return ProtocolResult<FlashInfo>.Fail("Flash 未响应任何 ID 读取命令（0x9F/0xAB/0x90 均无有效数据）。请检查连接。");

        // 4) 容量与指令集
        _deviceId = bestId;
        string finalId = $"{bestId[0]:X2} {bestId[1]:X2} {bestId[2]:X2}";
        if (device != null)
        {
            _deviceCapacity = device.Capacity;
            _log?.Invoke($"[PrepareDriver] Flash ID={finalId}，匹配设备 {device.Name}，容量={_deviceCapacity} 字节。");
        }
        else
        {
            // 对齐 MPTool AutoAddFlashType：FlashLib 未匹配时，从 JEDEC 密度字段推导容量，
            // 使未知但有效的 Flash 能以正确容量烧写（而不是一律回退默认 4MB）。
            uint? derived = FlashLib.DeriveCapacityFromRdid(bestId);
            if (derived is > 0)
            {
                _deviceCapacity = derived.Value;
                _log?.Invoke($"FlashLib 未匹配该 RDID ({finalId})，按 JEDEC 密度推导容量 {_deviceCapacity} 字节（对齐 MPTool AutoAddFlashType），指令集使用 W25Q32 默认。");
            }
            else
            {
                _log?.Invoke($"警告: FlashLib 未匹配到该 RDID ({finalId}) 且无法从 JEDEC 密度推导容量（ID 无效），容量未知（回退默认），指令集使用 W25Q32 默认。");
            }
        }
        _flashSet = FlashCommandSet.FromDevice(device);

        return ProtocolResult<FlashInfo>.Ok(new FlashInfo(_deviceId, _deviceCapacity), "Flash 信息读取成功。");
    }

    /// <summary>
    /// 上传 ThunderSE.elf PT_LOAD 段到设备 RAM（对齐 MPTool 777.txt L115-178）。
    /// 分块写入（1KB/块），Func1=RBC_mem_rwex，Func2=NoL2（纯 RAM 写入，不调用 L2）。
    /// 上传后回读校验（对齐 777.txt L179-306），确保驱动正确写入 RAM。
    /// </summary>
    private ProtocolResult UploadDriver(CancellationToken ct)
    {
        byte[] segment = _config.Image.Segment;
        int chunkSize = LoaderConfig.DriverUploadChunkSize;
        int totalChunks = (segment.Length + chunkSize - 1) / chunkSize;
        _log?.Invoke($"[UploadDriver] 上传驱动 {segment.Length} 字节，分 {totalChunks} 块，每块 {chunkSize} 字节，基址=0x{_config.UploadBase:X8}。");

        for (int off = 0; off < segment.Length; off += chunkSize)
        {
            ct.ThrowIfCancellationRequested();
            int len = Math.Min(chunkSize, segment.Length - off);
            int chunkIndex = off / chunkSize + 1;
            byte[] chunk = new byte[len];
            Array.Copy(segment, off, chunk, 0, len);

            // _log?.Invoke($"[UploadDriver]   块 {chunkIndex}/{totalChunks} @0x{off:X4}，大小 {len} 字节...");
            ScsiCommandResult r = _transport.SendDataOut(
                LoaderRomCommands.BuildCdb(_config.RbcMemRwex, _config.UploadBase + (uint)off, LoaderRomCommands.NoL2, 0),
                chunk);
            if (!r.Success)
                return ProtocolResult.Fail($"驱动上传失败 @0x{off:X}: {r.DescribeError()}");
        }
        _log?.Invoke($"[UploadDriver] 驱动上传完成（{segment.Length} 字节，{totalChunks} 块）。");

        // 回读校验（对齐 777.txt L179-306：8 × 512B 回读对比）
        const int verifyChunkSize = 512;
        int verifyChunks = (segment.Length + verifyChunkSize - 1) / verifyChunkSize;
        _log?.Invoke($"[UploadDriver] 开始回读校验: {segment.Length} 字节，{verifyChunks} 块，每块 {verifyChunkSize} 字节。");
        for (int off = 0; off < segment.Length; off += verifyChunkSize)
        {
            ct.ThrowIfCancellationRequested();
            int len = Math.Min(verifyChunkSize, segment.Length - off);
            int chunkIndex = off / verifyChunkSize + 1;

            ScsiCommandResult r = _transport.SendDataIn(
                LoaderRomCommands.BuildCdb(_config.RbcMemRwex, _config.UploadBase + (uint)off, LoaderRomCommands.NoL2, 0),
                len);
            if (!r.Success || r.Response is null)
                return ProtocolResult.Fail($"驱动回读失败 @0x{off:X}: {r.DescribeError()}");

            // 逐字节对比
            for (int i = 0; i < len; i++)
            {
                if (r.Response[i] != segment[off + i])
                {
                    return ProtocolResult.Fail(
                        $"驱动校验失败 @0x{off + i:X}: 期望 0x{segment[off + i]:X2}，实际 0x{r.Response[i]:X2}。");
                }
            }
            // _log?.Invoke($"[UploadDriver]   校验块 {chunkIndex}/{verifyChunks} @0x{off:X4} 一致（{len} 字节）。");
        }
        _log?.Invoke($"[UploadDriver] 驱动回读校验通过（{segment.Length} 字节，{verifyChunks} 块）。");
        return ProtocolResult.Ok("驱动上传完成。");
    }

    /// <summary>
    /// 导出整片 Flash 到文件（对齐 MPTool ExportSpiCodeToBin）：
    ///   上传驱动 -> SPI0 init -> RDID/容量 -> 按 512B 分块读回整片 -> 写入 .bin 文件。
    /// 读命令与回读校验相同（l1_func_signal_drive + 0x03），不改写任何字节（不做启动扇区补丁/CRC 重算），
    /// 导出的镜像用于备份/比对，如需可回烧需另跑 BootSector.Patch。
    /// </summary>
    public Task<ProtocolResult<ExportInfo>> ExportFirmwareAsync(
        string outputPath, IProgress<FlashProgress>? progress, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            _log?.Invoke($"[Export] 开始导出整片 Flash: {outputPath}。");

            Report(progress, FlashStage.Exporting, 2, "上传 Loader SPI 驱动并初始化...");
            ProtocolResult<FlashInfo> prepare = PrepareDriverAndQuery(ct);
            if (!prepare.Success)
                return Task.FromResult(ProtocolResult<ExportInfo>.Fail(prepare.Message));

            uint capacity = EffectiveCapacity();
            if (capacity == 0)
                return Task.FromResult(ProtocolResult<ExportInfo>.Fail("Flash 容量未知，无法导出。"));
            _log?.Invoke($"[Export] Flash 容量: {capacity} 字节（{capacity / 1024 / 1024} MB），整片导出。");

            int total = (int)capacity;
            int chunks = (total + ReadChunk - 1) / ReadChunk;
            _log?.Invoke($"[Export] 分 {chunks} 块回读，每块 {ReadChunk} 字节。");
            using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            uint crc = 0xFFFFFFFF;
            // 进度/日志按百分比节流：每块上报（16MB=32768 次）会洪泛 UI 线程（每块都经 Progress<T>/Dispatcher
            // 投递），导致进度条卡住不实时。仅在整数百分比前进时上报，末块强制收尾。
            int lastPercent = -1;
            for (int off = 0; off < total; off += ReadChunk)
            {
                ct.ThrowIfCancellationRequested();

                int chunkLen = Math.Min(ReadChunk, total - off);
                int chunkIdx = off / ReadChunk + 1;
                ScsiCommandResult r = _transport.SendDataIn(
                    LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, LoaderConfig.FlashReadBuf, _flashSet.Read, (uint)off), chunkLen);
                if (!r.Success || r.Response == null || r.Response.Length < chunkLen)
                    return Task.FromResult(ProtocolResult<ExportInfo>.Fail($"导出回读失败 @0x{off:X8}。"));
                stream.Write(r.Response, 0, chunkLen);
                crc = Crc32.Update(crc, r.Response.AsSpan(0, chunkLen));

                int percent = 2 + (off + chunkLen) * 98 / Math.Max(total, 1);
                if (percent == lastPercent && off + chunkLen < total)
                    continue;
                lastPercent = percent;
                _log?.Invoke($"[Export]   块 {chunkIdx}/{chunks} @0x{off:X8}（{chunkLen} 字节）已读回。");
                Report(progress, FlashStage.Exporting, percent, $"导出 Flash {off + chunkLen}/{total} 字节");
            }
            crc = Crc32.Finalize(crc);
            stream.Flush();

            _log?.Invoke($"[Export] 导出完成: {outputPath}（{total} 字节）。");
            _log?.Invoke($"[Export] 导出文件 CRC32=0x{crc:X8}。");
            return Task.FromResult(ProtocolResult<ExportInfo>.Ok(
                new ExportInfo(outputPath, total, crc),
                $"导出完成（{total} 字节，CRC32=0x{crc:X8}）。"));
        }
        catch (OperationCanceledException)
        {
            _log?.Invoke("[Export] 导出已取消。");
            throw;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Export] 导出异常: {ex.Message}");
            return Task.FromResult(ProtocolResult<ExportInfo>.Fail($"导出失败: {ex.Message}"));
        }
    }

    /// <summary>
    /// 0xCB 收尾复位（对齐 MPTool AX326X::DeviceReset / SpiDriver::SpiReset）。
    /// 发送 0xCB 复位命令（Func1=0, DataAddr=0, Func2=l2_func_reset, Param=0x4），
    /// 触发 Loader 设备复位/重新枚举。
    ///
    /// 参考 MPTool 流程：
    ///   AX2210MPToolDlg.cpp:2058-2066  if (auto_reset) dt->DeviceReset()
    ///   AX326X.cpp:1131-1135          UFRunCode(&UsbCmd, 0, NULL, USB_WRITE)
    ///   UsbFunction.cpp:36-38          WriteToScsi(SCSI_IOCTL_DATA_OUT, cdbLen=16, dataLen=0, data=NULL)
    ///
    /// 注意：此命令是下载完成后的收尾复位，与 DeviceConnection.Connect 中用于切换
    /// 应用态→Loader 态的 0xDA 命令用途不同。0xDA 保留用于初始模式切换。
    /// </summary>
    public Task<ProtocolResult> EnterUpdateModeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        byte[] cdb = LoaderRomCommands.BuildDeviceResetCdb(_l2FuncReset);
        _log?.Invoke($"下发 0xCB 复位设备（Func2=0x{_l2FuncReset:X8}）...");

        // 对齐 MPTool WriteToScsi（SCSI_IOCTL_DATA_OUT）：
        // 使用 DATA_OUT 方向，0 数据长度，NULL 数据缓冲。
        // 固件检查 CbwTrxLength==0 后进入零数据分支，方向位不影响命令路由。
        ScsiCommandResult result = _transport.SendDataOut(cdb, ReadOnlySpan<byte>.Empty);

        if (result.Success)
            return Task.FromResult(ProtocolResult.Ok("0xCB 已下发，设备正在复位并重新枚举。"));

        // 错误码 55（设备未连接）或 31（设备未就绪）= 设备已断开 USB 正在重启，视为成功
        if (result.ErrorCode is 55 or 31)
            return Task.FromResult(ProtocolResult.Ok("0xCB 已下发，设备已断开（正在重启）。"));

        // SCSI CHECK CONDITION（0x02）：设备端返回错误，但 MPTool 同样忽略 DeviceReset 的返回值
        // 此时设备可能已进入复位流程，仍视为成功
        if (result.ScsiStatus == 0x02)
        {
            string senseInfo = result.Sense is { Length: > 0 }
                ? $"，Sense: {Convert.ToHexString(result.Sense)}"
                : "，无 Sense 数据";
            _log?.Invoke($"0xCB 复位返回 CHECK CONDITION{senseInfo}，设备可能已进入复位流程。");
            return Task.FromResult(ProtocolResult.Ok("0xCB 已下发（SCSI CHECK CONDITION），设备正在复位。"));
        }

        return Task.FromResult(ProtocolResult.Fail($"0xCB 复位失败: {result.DescribeError()}。请检查设备连接。"));
    }

    /// <summary>
    /// 下载固件到 SPI NOR flash 起始区域：
    ///   上传驱动 -> SPI0 init -> RDID -> 容量校验 -> 容量 pattern 检测（可选）->
    ///   擦除（可选整片）-> 按 256B 页写入（末页 0xFF 补齐）-> 完成。
    /// </summary>
    public Task<ProtocolResult> DownloadFirmwareAsync(
        FirmwareImage firmware, IProgress<FlashProgress>? progress, CancellationToken ct, FlashDownloadOptions? options = null)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            _log?.Invoke($"[Download] 开始固件下载: {firmware.Length} 字节。");
            Report(progress, FlashStage.Downloading, 2, "上传 Loader SPI 驱动并初始化...");
            ProtocolResult<FlashInfo> prepare = PrepareDriverAndQuery(ct);
            if (!prepare.Success)
                return Task.FromResult(ProtocolResult.Fail(prepare.Message));
            ReadOnlySpan<byte> id = _deviceId;

            uint capacity = EffectiveCapacity();
            if (firmware.Length > capacity)
                return Task.FromResult(ProtocolResult.Fail($"固件大小 {firmware.Length} 字节超过 Flash 容量 {capacity} 字节。"));
            _log?.Invoke($"[Download] 容量校验通过: 固件 {firmware.Length} 字节 <= Flash {capacity} 字节。");

            // 容量 pattern 检测（对齐 MPTool CheckCapacity）：写读比对识别灰片/坏片，
            // 避免"报告容量 > 物理容量"的 Flash 烧录后设备不可用。
            // 仅整片擦除时运行——该测试会在容量中点擦写扇区，局部重刷会破坏固件区外的资源/参数分区。
            if (options?.EraseAll == true && options?.RunCapacityPatternTest != false)
            {
                _log?.Invoke("[Download] 容量 pattern 检测（0x5a@0、0xa5@容量中点，写读比对）...");
                Report(progress, FlashStage.Downloading, 8, "容量 pattern 检测...");
                ProtocolResult pattern = RunCapacityPatternTest((int)firmware.Length, ct);
                if (!pattern.Success)
                    return Task.FromResult(pattern);
            }

            // 擦除 [0, regionEnd) 区域（优先 64KB 块，尾部 4KB 扇区）
            //    每次擦除前发送 WREN，擦除后轮询 BUSY 位等待完成
            const int sectorSize = 4096;
            const int blockSize = 65536;
            int total = (int)firmware.Length;
            uint regionEnd = (uint)((total + sectorSize - 1) / sectorSize) * (uint)sectorSize;
            if (options?.EraseAll == true)
            {
                regionEnd = capacity;
                _log?.Invoke($"[Download] 已开启整片擦除（ERASEALL），擦除区域扩展到全片 0x{capacity:X8}。");
            }
            uint blockEnd = regionEnd - regionEnd % blockSize;
            int blockCount = (int)(blockEnd / blockSize);
            int sectorCount = (int)((regionEnd - blockEnd) / sectorSize);

            // ★ 禁用 Chip Erase（0xC7）整片擦除：真机验证该 loader 固件的 l1_func_signal_drive 对 0xC7
            //   3ms 即返回"成功且就绪"（16MB 整片擦除物理上不可能这么快），擦除状态不可靠——
            //   实测出现"命令成功但 flash 实际未擦除"，残留旧数据后页编程无法置 1 导致固件损坏。
            //   因此 ERASEALL 也统一走真机验证可靠的逐块擦除（64KB 块 0xD8 + 尾部 4KB 扇区 0x20）。
            _log?.Invoke($"[Download] 擦除区域: 0x00000000 ~ 0x{regionEnd:X8}（{blockCount} 个 64KB 块 + {sectorCount} 个 4KB 扇区）...");

            // ★ 进度优化：擦除是全流程最慢阶段之一，原先仅开始时报一次 10%，进度条在擦除的
            //   10-20 秒内纹丝不动、界面看似卡死。现按块/扇区完成数逐项上报（映射 1%-10%），
            //   进度随擦除推进实时增长。
            int ops = blockCount + sectorCount;
            int done = 0;
            for (uint addr = 0; addr < blockEnd; addr += blockSize)
            {
                ct.ThrowIfCancellationRequested();
                int blockIdx = (int)(addr / blockSize);
                // _log?.Invoke($"[Download]   块擦除 {blockIdx + 1}/{blockCount} @0x{addr:X8}（64KB）...");
                ProtocolResult we = SendWriteEnable();
                if (!we.Success)
                    return Task.FromResult(we);
                ScsiCommandResult r = _transport.SendCommand(
                    LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, LoaderConfig.FlashReadBuf, _flashSet.Erase64K, addr));
                if (!r.Success)
                    return Task.FromResult(ProtocolResult.Fail($"块擦除失败 @0x{addr:X8}: {r.DescribeError()}。"));
                ProtocolResult wait = WaitForFlashReady();
                if (!wait.Success)
                    return Task.FromResult(wait);
                done++;
                Report(progress, FlashStage.Downloading, 1 + 9 * done / Math.Max(ops, 1), $"擦除固件 {done}/{ops} 区域");
            }
            for (uint addr = blockEnd; addr < regionEnd; addr += sectorSize)
            {
                ct.ThrowIfCancellationRequested();
                int sectorIdx = (int)((addr - blockEnd) / sectorSize);
                // _log?.Invoke($"[Download]   扇区擦除 {sectorIdx + 1}/{sectorCount} @0x{addr:X8}（4KB）...");
                ProtocolResult we = SendWriteEnable();
                if (!we.Success)
                    return Task.FromResult(we);
                ScsiCommandResult r = _transport.SendCommand(
                    LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, LoaderConfig.FlashReadBuf, _flashSet.Erase4K, addr));
                if (!r.Success)
                    return Task.FromResult(ProtocolResult.Fail($"扇区擦除失败 @0x{addr:X8}: {r.DescribeError()}。"));
                ProtocolResult wait = WaitForFlashReady();
                if (!wait.Success)
                    return Task.FromResult(wait);
                done++;
                Report(progress, FlashStage.Downloading, 1 + 9 * done / Math.Max(ops, 1), $"擦除固件 {done}/{ops} 区域");
            }
            _log?.Invoke($"[Download] 擦除完成。");

            // 按 PageSize 页写入固件，对齐 MPTool DownBinCode 写入顺序：
            //   先跳过前 BootSectorSize 字节（引导扇区），写入 0x200 至末尾的非引导数据；
            //   最后单独写入引导扇区 0x000~0x1FF。
            //   末页不足 256B 时以 0xFF 补齐整页（对齐 MPTool：每次 data-out 均 0x100）。
            int pages = (total + PageSize - 1) / PageSize;
            int firstDataPage = BootSectorSize / PageSize; // 跳过前 2 页（512 字节引导扇区）
            _log?.Invoke($"[Download] 开始写入固件: {total} 字节，{pages} 页，跳过前 {firstDataPage} 页引导扇区，每页 {PageSize} 字节。");
            for (int i = firstDataPage; i < pages; i++)
            {
                ct.ThrowIfCancellationRequested();

                int off = i * PageSize;
                byte[] page = MakePaddedPage(firmware.Data, off, total);

                var sw = Stopwatch.StartNew();
                // _log?.Invoke($"[Download]   写入页 {i + 1}/{pages} @0x{off:X8}（{page.Length} 字节）...");

                // ★ 注意：Param 必须使用页索引（byteOffset >> 8），而非字节偏移。
                // MPTool 参考：SpiWrite 中 WritePageAddr = ((WriteAddr + i)>>8) | enc，
                // 固件 l2_func_spi_page_program 内部将 Param 左移 8 位得到字节地址（flash_addr = Param << 8）。
                // 如果传字节偏移（如 off=0x100），固件会将其解释为 0x10000，导致所有页写入地址错位 256 倍。
                ScsiCommandResult r = _transport.SendDataOut(
                    LoaderRomCommands.BuildCdb(_config.RbcMemRwex, _config.RbcMemRwexBuf, _l2PageProgram, (uint)(off >> 8)),
                    page);
                sw.Stop();

                if (!r.Success)
                    return Task.FromResult(ProtocolResult.Fail($"页写入失败 @0x{off:X8}: {r.DescribeError()}。"));
                // 写入耗时 > 500ms 时记录警告日志（正常页写入应 < 50ms；> 500ms 表明 flash 或 USB 链路异常）
                // if (sw.ElapsedMilliseconds > 500)
                //     _log?.Invoke($"[Download]     页 {i + 1}/{pages} 写入成功（耗时 {sw.ElapsedMilliseconds}ms，超过 500ms 阈值）。");
                // else
                //     _log?.Invoke($"[Download]     页 {i + 1}/{pages} 写入成功。");

                // 注意：不在此处调用 WaitForFlashReady()。L2 页编程函数内部已等待 flash 就绪，
                // 额外 RDSR 轮询每页增加 ~10ms 开销，5312 页累计 ~53 秒（对齐 MPTool 参考：
                // SpiWrite 每页仅调用 UFRunCode，无额外轮询，总下载时间 ~6 秒）。
                // 引导扇区（关键页）写入后仍保留 WaitForFlashReady() 作为安全网。

                int percent = 12 + (i - firstDataPage + 1) * 80 / Math.Max(pages - firstDataPage, 1);
                Report(progress, FlashStage.Downloading, percent, $"写入固件 {off + Math.Min(PageSize, total - off)}/{total} 字节");
            }

            // 非引导数据写入完成后，最后写入引导扇区（对齐 MPTool DownBinCode：Step 6）
            int bootPages = Math.Min(firstDataPage, pages);
            if (bootPages > 0)
            {
                _log?.Invoke($"[Download] 写入引导扇区（前 {BootSectorSize} 字节）...");
                for (int i = 0; i < bootPages; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    int off = i * PageSize;
                    byte[] page = MakePaddedPage(firmware.Data, off, total);

                    var sw = Stopwatch.StartNew();
                    // _log?.Invoke($"[Download]   写入引导扇区页 {i + 1}/{bootPages} @0x{off:X8}（{page.Length} 字节）...");

                    // 先发送 WREN（对齐 MPTool SpiWrite：每次页编程前 WREN）
                    ProtocolResult we = SendWriteEnable();
                    if (!we.Success)
                        return Task.FromResult(ProtocolResult.Fail($"WREN 失败 @0x{off:X8}: {we.Message}。"));

                    // 使用 L2 页编程写入（对齐 MPTool：Param = (WriteAddr >> 8) | enc）
                    // 注意：地址 0 处 Param=0，固件 l2_func_spi_page_program 在 Param=0
                    // 时可能返回成功但不编程（SR=0x02）。此处对齐 MPTool 行为用 L2 方式，
                    // 写入后立即回读校验以检测此问题。
                    ScsiCommandResult r = _transport.SendDataOut(
                        LoaderRomCommands.BuildCdb(_config.RbcMemRwex, _config.RbcMemRwexBuf, _l2PageProgram, (uint)(off >> 8)),
                        page);
                    sw.Stop();

                    if (!r.Success)
                        return Task.FromResult(ProtocolResult.Fail($"引导扇区页写入失败 @0x{off:X8}: {r.DescribeError()}。"));
                    // _log?.Invoke($"[Download]     引导扇区页 {i + 1}/{bootPages} 写入成功（耗时 {sw.ElapsedMilliseconds}ms）。");

                    // 等待 flash 就绪
                    ProtocolResult writeReady = WaitForFlashReady();
                    if (!writeReady.Success)
                    {
                        _log?.Invoke($"[Download]     引导扇区页 {i + 1}/{bootPages} 写入后 flash 未就绪: {writeReady.Message}");
                        return Task.FromResult(writeReady);
                    }
                }
            }

            // 引导扇区写后立即回读校验（对齐 MPTool DownBinCode：boot sector 是最关键的入栈，
            // 回读不一致则擦除 block 0 防止设备启动到不完整固件，并抛 ERR_SPI_VERIFY）
            // 期望内容 = 固件前 512 字节按 0xFF 补齐（与页写入补齐纪律一致；短固件不足 512 字节时
            // flash 中超出固件长度的字节为 0xFF，对齐 MPTool LoadCodeIntoBuffer 对短文件的 0xFF 填充）。
            _log?.Invoke($"[Download] 引导扇区写入后立即回读校验（前 {BootSectorSize} 字节 @0x00000000）...");
            ScsiCommandResult verifyRead = _transport.SendDataIn(
                LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, LoaderConfig.FlashReadBuf, _flashSet.Read, 0), BootSectorSize);
            byte[] expectedBootSector = MakeExpectedBootSector(firmware.Data, total);
            int firstBad = -1;
            if (verifyRead.Success && verifyRead.Response != null && verifyRead.Response.Length >= BootSectorSize)
            {
                int i = 0;
                while (i < BootSectorSize && verifyRead.Response[i] == expectedBootSector[i])
                    i++;
                firstBad = i == BootSectorSize ? BootSectorSize : i;
            }
            if (firstBad < BootSectorSize)
            {
                if (firstBad < 0)
                    _log?.Invoke($"[Download]   引导扇区回读失败！");
                else
                {
                    _log?.Invoke($"[Download]   引导扇区回读不一致（首条 @0x{firstBad:X8}）！");
                    _log?.Invoke($"[Download]     期望: {Convert.ToHexString(expectedBootSector.AsSpan(0, 16).ToArray())}");
                    _log?.Invoke($"[Download]     实际: {Convert.ToHexString(verifyRead.Response.AsSpan(0, 16).ToArray())}");
                }
                // 对齐 MPTool：读回状态寄存器辅助定位原因，并擦除 block 0 防止启动到不完整固件
                byte sr = ReadStatusRegister();
                _log?.Invoke($"[Download]     状态寄存器: SR=0x{sr:X2}，擦除 block 0（0x00000000）...");
                SendWriteEnable();
                _transport.SendCommand(
                    LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, LoaderConfig.FlashReadBuf, _flashSet.Erase64K, 0));
                WaitForFlashReady();
                return Task.FromResult(ProtocolResult.Fail($"引导扇区校验失败（SR={sr:X2}），已擦除 block 0 防止启动到不完整固件，请重试。"));
            }
            _log?.Invoke($"[Download]   引导扇区回读一致（写入成功）。");

            _log?.Invoke($"[Download] 固件下载完成（{total} 字节，{pages} 页）。");
            Report(progress, FlashStage.Downloading, 92, $"固件下载完成（{total} 字节，{pages} 页）。");
            return Task.FromResult(ProtocolResult.Ok($"固件下载完成（{total} 字节，flash ID {id[0]:X2}{id[1]:X2}{id[2]:X2}）。"));
        }
        catch (OperationCanceledException)
        {
            _log?.Invoke("[Download] 固件下载已取消。");
            throw;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Download] 固件下载异常: {ex.Message}");
            return Task.FromResult(ProtocolResult.Fail($"固件下载失败: {ex.Message}"));
        }
    }

    /// <summary>回读校验：按 512B 分块经 l1_func_signal_drive 派发 0x03 读取并逐字节比较。</summary>
    public Task<ProtocolResult> VerifyFirmwareAsync(
        FirmwareImage firmware, IProgress<FlashProgress>? progress, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            _log?.Invoke($"[Verify] 开始回读校验: {firmware.Length} 字节。");

            _log?.Invoke("[Verify] SPI0 初始化...");
            ScsiCommandResult init = _transport.SendCommand(
                LoaderRomCommands.BuildCdb(_l1SpiInit, 0, 0, 0));
            if (!init.Success)
                return Task.FromResult(ProtocolResult.Fail($"SPI0 初始化失败: {init.DescribeError()}。"));
            _log?.Invoke("[Verify] SPI0 初始化成功。");

            int total = (int)firmware.Length;
            int chunks = (total + ReadChunk - 1) / ReadChunk;
            _log?.Invoke($"[Verify] 分 {chunks} 块回读，每块 {ReadChunk} 字节。");
            // 块级日志按整数百分比节流：4MB=8192 块若逐块经协议日志回调（复用连接时直达 UI/日志区）
            // 会在校验阶段洪泛卡死界面；仅在百分比前进或末块时输出（对齐导出路径的同款节流策略）。
            int lastPercent = -1;
            for (int off = 0; off < total; off += ReadChunk)
            {
                ct.ThrowIfCancellationRequested();

                int chunkLen = Math.Min(ReadChunk, total - off);
                int chunkIdx = off / ReadChunk + 1;
                int percent = (off + chunkLen) * 100 / total;
                bool reportStep = percent != lastPercent || off + chunkLen >= total;
                if (reportStep)
                {
                    lastPercent = percent;
                    // _log?.Invoke($"[Verify]   回读块 {chunkIdx}/{chunks} @0x{off:X8}（{chunkLen} 字节）...");
                }
                // SPI 读（0x03）带 24 位地址，必须 Ctrl=0x07, SiLen=4（FlashReadBuf=0x04070000），
                // 与 MPTool 抓包一致（cb 74 00 00 00 00 07 04 03 XX YY ZZ）。
                // 读命令发送地址是必需的，否则 flash 从错误地址读回导致校验不一致。
                ScsiCommandResult r = _transport.SendDataIn(
                    LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, LoaderConfig.FlashReadBuf, _flashSet.Read, (uint)off), chunkLen);
                if (!r.Success || r.Response == null || r.Response.Length < chunkLen)
                    return Task.FromResult(ProtocolResult.Fail($"回读失败 @0x{off:X8}。"));

                if (!r.Response.AsSpan(0, chunkLen).SequenceEqual(firmware.Data.AsSpan(off, chunkLen)))
                {
                    // 诊断：定位第一条不匹配字节，打印实际读回 vs 期望（前 16 字节）
                    int firstBad = 0;
                    ReadOnlySpan<byte> actual = r.Response;
                    ReadOnlySpan<byte> expect = firmware.Data.AsSpan(off, chunkLen);
                    while (firstBad < chunkLen && actual[firstBad] == expect[firstBad])
                        firstBad++;
                    _log?.Invoke($"[Verify]   校验不一致 @0x{off:X8}（首条 @0x{off + firstBad:X8}）！");
                    int ctxStart = Math.Max(0, firstBad - 4);
                    int ctxLen = Math.Min(16, chunkLen - ctxStart);
                    _log?.Invoke($"[Verify]     期望: {Convert.ToHexString(expect.Slice(ctxStart, ctxLen).ToArray())}");
                    _log?.Invoke($"[Verify]     实际: {Convert.ToHexString(actual.Slice(ctxStart, ctxLen).ToArray())}");
                    // 对齐 MPTool "Code Verify Error!(SR=%02X)"：回读状态寄存器辅助定位原因
                    byte sr = ReadStatusRegister();
                    _log?.Invoke($"[Verify]     状态寄存器: SR=0x{sr:X2}");
                    return Task.FromResult(ProtocolResult.Fail($"校验不一致 @0x{off:X8}（SR={sr:X2}）。"));
                }
                // if (reportStep)
                //     _log?.Invoke($"[Verify]     块 {chunkIdx}/{chunks} 校验通过。");

                if (reportStep)
                    Report(progress, FlashStage.Verifying, percent, $"校验固件 {off + chunkLen}/{total} 字节");
            }

            _log?.Invoke($"[Verify] 固件校验通过（{total} 字节，{chunks} 块）。");
            return Task.FromResult(ProtocolResult.Ok($"固件校验通过（{total} 字节）。"));
        }
        catch (OperationCanceledException)
        {
            _log?.Invoke("[Verify] 回读校验已取消。");
            throw;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Verify] 回读校验异常: {ex.Message}");
            return Task.FromResult(ProtocolResult.Fail($"固件校验失败: {ex.Message}"));
        }
    }

    /// <summary>
    /// 发送 Write Enable (WREN, 0x06) 命令。SPI NOR Flash 标准要求在每次擦除前使能 WEL。
    /// l1_func_signal_drive 是通用 SPI 命令派发器，不会自动添加 WREN，需主机显式发送。
    /// </summary>
    private ProtocolResult SendWriteEnable()
    {
        ScsiCommandResult r = _transport.SendCommand(
            LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, 0x06, 0));
        return r.Success
            ? ProtocolResult.Ok("Write Enable 已发送。")
            : ProtocolResult.Fail($"Write Enable 失败: {r.DescribeError()}。");
    }

    /// <summary>
    /// 轮询 Flash 状态寄存器直到 BUSY 位清零（擦除/写入完成）。
    /// 通过 l1_func_signal_drive 派发 0x05 (Read Status Register) 命令读取状态，检查 bit0 (BUSY)。
    ///
    /// 优化策略（快慢路径分离）：
    ///   1. 快路径：无延迟检查一次 RDSR。L2 页编程函数内部已等待 flash 就绪，
    ///      大多数情况下返回时 flash 已就绪，1 次 RDSR 即可通过（~1ms）。
    ///   2. 慢路径：仅当 flash 未就绪时，回退到 10ms 间隔轮询（最多 3 秒）。
    ///   这避免了 5310 页写入时每页都额外等待 10ms 的累积开销。
    /// </summary>
    private ProtocolResult WaitForFlashReady()
    {
        // 快路径：先无延迟检查一次（大多数情况 flash 已就绪，~1ms 返回）
        ScsiCommandResult first = _transport.SendDataIn(
            LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, _flashSet.ReadStatus, 0), 4);
        if (first.Success && first.Response is { Length: >= 1 } && (first.Response[0] & 0x01) == 0)
            return ProtocolResult.Ok("Flash 就绪。");

        // 慢路径：仅当快路径未就绪时，进入 10ms 间隔轮询
        byte lastStatus = first.Success && first.Response is { Length: >= 1 } ? first.Response[0] : (byte)0;
        for (int i = 0; i < StatusPollMaxRetries; i++)
        {
            // 实际 loader 固件的 l1_func_signal_drive 对 RDSR 使用 Ctrl=0x03, SiLen=1（仅命令码，无地址）。
            // 注意：MPTool 参考对 RDSR 用 Ctrl=0x07，但那是不同固件版本；本设备改 0x07 会导致
            // 状态寄存器读回错误、BUSY 位误判为 0、轮询提前退出，进而引发下一页 WREN 错误码 31。
            ScsiCommandResult r = _transport.SendDataIn(
                LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, _flashSet.ReadStatus, 0), 4);
            if (!r.Success || r.Response == null || r.Response.Length < 1)
                return ProtocolResult.Fail($"读取状态寄存器失败: {r.DescribeError()}。");
            lastStatus = r.Response[0];
            if ((lastStatus & 0x01) == 0)
                return ProtocolResult.Ok("Flash 就绪。");  // BUSY 清零
            Thread.Sleep(10);
        }
        return ProtocolResult.Fail($"Flash 等待超时（{StatusPollMaxRetries * 10}ms，SR={lastStatus:X2}），设备可能异常。");
    }

    /// <summary>读取 Flash 状态寄存器（0x05），失败时返回 0。供校验失败/超时诊断（对齐 MPTool SR=XX）。</summary>
    private byte ReadStatusRegister()
    {
        ScsiCommandResult r = _transport.SendDataIn(
            LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, _flashSet.ReadStatus, 0), 4);
        return r.Success && r.Response != null && r.Response.Length >= 1 ? r.Response[0] : (byte)0;
    }

    /// <summary>构造一页写入数据：取 [off, min(off+PageSize, total)) 的固件字节，不足整页处以 0xFF 补齐。
    /// 对齐 MPTool 每页固定 data-out 0x100 的写块纪律。</summary>
    private static byte[] MakePaddedPage(byte[] data, int off, int total)
    {
        int pageLen = Math.Min(PageSize, total - off);
        var page = new byte[PageSize];
        Array.Fill(page, (byte)0xFF);
        Array.Copy(data, off, page, 0, pageLen);
        return page;
    }

    /// <summary>构造引导扇区的期望内容：固件前 <see cref="BootSectorSize"/> 字节，不足处以 0xFF 补齐。
    /// 引导扇区页按整页写入（末字节 0xFF 补齐），故 flash 中超出固件长度的字节为 0xFF；
    /// 对齐 MPTool LoadCodeIntoBuffer 对短文件（&lt; 512B）的 0xFF 填充语义，避免短固件越界读取。</summary>
    private static byte[] MakeExpectedBootSector(byte[] data, int total)
    {
        byte[] expected = new byte[BootSectorSize];
        Array.Fill(expected, (byte)0xFF);
        int n = Math.Min(BootSectorSize, total);
        Array.Copy(data, 0, expected, 0, n);
        return expected;
    }

    /// <summary>构造固定值 pattern 缓冲区（容量 pattern 检测用）。</summary>
    private static byte[] MakePattern(byte value, int length)
    {
        var buf = new byte[length];
        Array.Fill(buf, value);
        return buf;
    }

    /// <summary>
    /// 容量 pattern 测试（对齐 MPTool CheckCapacity）：在地址 0 写 0x5a×512、容量中点写 0xa5×512 后回读比对。
    /// NOR 必须先擦后写（MPTool CheckCapacity 写 pattern 前先擦两端扇区）；回读顺序为先读地址 0 再读容量中点，
    /// 以检出"报告容量 > 物理容量"的回环灰片/坏片，避免烧录后设备不可用。
    /// 守卫对齐 MPTool：仅当容量中点（flashsize）&gt; 固件长度 时才运行，否则中点落在固件区内，测试无意义。
    /// </summary>
    private ProtocolResult RunCapacityPatternTest(int totalLength, CancellationToken ct)
    {
        uint capacity = EffectiveCapacity();
        uint flashsize = capacity / 2;
        if (flashsize <= (uint)totalLength)
        {
            _log?.Invoke($"[Download]   容量中点 0x{flashsize:X8} <= 固件长度 {totalLength}，跳过容量 pattern 检测（对齐 MPTool CheckCapacity 守卫）。");
            return ProtocolResult.Ok("容量 pattern 检测跳过（容量中点落在固件区内，对齐 MPTool 守卫）。");
        }

        byte[] patternLo = MakePattern(0x5A, BootSectorSize);
        byte[] patternHi = MakePattern(0xA5, BootSectorSize);
        (uint Addr, byte[] Pattern)[] writes = { (0u, patternLo), (flashsize, patternHi) };
        foreach ((uint addr, byte[] pattern) in writes)
        {
            if (addr >= capacity)
                continue;
            ct.ThrowIfCancellationRequested();

            // NOR 必须先擦后写（MPTool CheckCapacity 写 pattern 前先擦两端扇区）
            ProtocolResult erase = EraseSector(addr, ct);
            if (!erase.Success)
                return erase;

            // 512B pattern 分 2 个 0x100 页写入（对齐 MPTool 每次 data-out 均 0x100）
            _log?.Invoke($"[Download]   写入 pattern 0x{pattern[0]:X2}×512 @0x{addr:X8}...");
            for (int off = 0; off < pattern.Length; off += PageSize)
            {
                ProtocolResult we = SendWriteEnable();
                if (!we.Success)
                    return we;
                ScsiCommandResult w = _transport.SendDataOut(
                    LoaderRomCommands.BuildCdb(_config.RbcMemRwex, _config.RbcMemRwexBuf, _l2PageProgram, (addr + (uint)off) >> 8),
                    pattern.AsSpan(off, PageSize).ToArray());
                if (!w.Success)
                    return ProtocolResult.Fail($"容量 pattern 写入失败 @0x{addr + off:X8}: {w.DescribeError()}。");
                ProtocolResult wait = WaitForFlashReady();
                if (!wait.Success)
                    return wait;
            }
        }

        // 回读：先地址 0、再容量中点（MPTool CheckCapacity 读顺序）
        (uint Addr, byte[] Pattern)[] reads = { (0u, patternLo), (flashsize, patternHi) };
        foreach ((uint addr, byte[] pattern) in reads)
        {
            if (addr >= capacity)
                continue;
            ct.ThrowIfCancellationRequested();
            ScsiCommandResult r = _transport.SendDataIn(
                LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, LoaderConfig.FlashReadBuf, _flashSet.Read, addr), pattern.Length);
            if (!r.Success || r.Response == null || r.Response.Length < pattern.Length)
                return ProtocolResult.Fail($"容量 pattern 回读失败 @0x{addr:X8}。");
            int firstBad = 0;
            while (firstBad < pattern.Length && r.Response[firstBad] == pattern[firstBad])
                firstBad++;
            if (firstBad < pattern.Length)
                return ProtocolResult.Fail(
                    $"容量 pattern 校验失败 @0x{addr:X8}: 期望 0x{pattern[0]:X2}，首条不一致 @0x{addr + firstBad:X8} " +
                    $"(读回 0x{r.Response[firstBad]:X2})。Flash 容量异常（报告 {capacity} 字节但物理容量不足），请更换 Flash。");
            _log?.Invoke($"[Download]   回读 @0x{addr:X8} 一致（{pattern.Length} 字节）。");
        }
        _log?.Invoke("[Download] 容量 pattern 检测通过。");
        return ProtocolResult.Ok("容量 pattern 检测通过。");
    }

    private ProtocolResult EraseSector(uint addr, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ProtocolResult we = SendWriteEnable();
        if (!we.Success)
            return we;
        ScsiCommandResult r = _transport.SendCommand(
            LoaderRomCommands.BuildFlashReadCdb(_l1SignalDrive, LoaderConfig.FlashReadBuf, _flashSet.Erase4K, addr));
        if (!r.Success)
            return ProtocolResult.Fail($"容量 pattern 扇区擦除失败 @0x{addr:X8}: {r.DescribeError()}。");
        return WaitForFlashReady();
    }

    private static void Report(IProgress<FlashProgress>? progress, FlashStage stage, int percent, string message)
        => progress?.Report(new FlashProgress(stage, percent, message));
}
