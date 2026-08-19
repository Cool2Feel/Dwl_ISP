using UpgradeTool.Core.Abstractions;
using UpgradeTool.Core.Utilities;

namespace UpgradeTool.Core.Protocol;

/// <summary>
/// DC503J 真实固件升级协议：通过固件应用态的 0xCD 厂商命令通道驱动 SDRAM 中的
/// AX329X SPI0 flash stub，完成固件下载与回读校验。
///
/// 关键架构事实（来自固件 hal_usb_msc.c）：
///   1) 0xCD 通道只在应用态可用；0xDA（EnterUpdateModeAsync）是收尾的复位/重启步骤，
///      不是下载的前置步骤。因此 DownloadFirmwareAsync 自包含，可在未进升级模式时调用。
///   2) 数据面统一路由到 cb_mem_rwex（Func1），按方向执行：
///        data-in : cb_mem_read  -> L2(Func2)(DataAddr,Residue,Param) 然后 cb_mem2FIFO 回传。
///        data-out: cb_mem_write -> cb_FIFO2mem(DataAddr,Residue) 然后 L2(Func2)。
///      DataAddr=0xffffffff 哨兵会让固件改用 scsi.ptxbuf/prxbuf（SRAM，免缓存问题）。
///   3) stub 上传到缓存 SDRAM 0x020ccec0（超过 0x44000 D-cache 守卫线）：
///      cb_FIFO2mem 的 memcpy 只写入 D-cache，必须先经 ax32xx_sysDcacheFlush 写回 DRAM，
///      再用 ax32xx_sysIcacheInit 全组失效 I-cache，之后才能安全跳转执行 stub。
///      D-cache flush / I-cache init 与固件 L2 签名 (u32,u32,u32) 兼容，作为 L2 注入。
/// </summary>
public sealed class Dc503RomProtocol : IFlashProtocol, IFlashCapacityInfo
{
    /// <summary>SPI NOR flash 页大小（hal_spi.h SF_PAGE_SIZE）。</summary>
    public const int PageSize = 256;

    /// <summary>扇区大小（SF_SECTOR_SIZE）。</summary>
    public const int SectorSize = 4096;

    /// <summary>块大小（SF_BLOCK_SIZE）。</summary>
    public const int BlockSize = 65536;

    /// <summary>引导扇区大小（CODE_BANK_SIZE），最后写入以防止烧录中断时设备意外启动。</summary>
    public const int BootSectorSize = 512;

    /// <summary>回读校验的分块大小（cb_mem2FIFO 按 512 分块，取整对齐）。</summary>
    private const int ReadChunk = 512;

    /// <summary>状态寄存器 BUSY 位轮询的最大重试次数（每次间隔 10ms，最长约 3 秒）。</summary>
    private const int StatusPollMaxRetries = 300;

    /// <summary>
    /// 0xCD 通道探针的 data-in 长度。发送一个已知无副作用的小命令（如 Func2=NoL2, DataAddr=NoDataAddr），
    /// 期望返回 4 字节垃圾数据。如果设备不支持 0xCD 通道，此命令会快速失败（错误码 121 或 SCSI 状态非 0），
    /// 避免上传大块 stub 后才超时。
    /// </summary>
    private const int ProbeDataInLength = 4;

    /// <summary>stub 上传块大小：首块用较小值（512B）探测通道，成功后恢复为 1024B 以提高吞吐。</summary>
    private const int StubUploadChunkSize = 1024;

    /// <summary>stub 上传首块大小（较小值，用于 0xCD 通道探针 + 验证通道可用）。</summary>
    private const int StubFirstChunkSize = 512;

    /// <summary>stub 回读校验块大小（对齐 MPTool MAX_DATA_TRANS_SIZE=0x200）。</summary>
    private const int StubVerifyChunkSize = 512;

    /// <summary>设备端实际 Flash 容量（字节），GetFlashInfoAsync 后有效；0 = 未知。</summary>
    private uint _deviceCapacity;

    /// <summary>设备端 RDID（最近一次查询）。</summary>
    private byte[] _deviceId = Array.Empty<byte>();

    private readonly IFlashTransport _transport;
    private readonly StubImage _stub;
    private readonly FirmwareSymbols _symbols;
    private readonly Action<string>? _log;

    public Dc503RomProtocol(
        IFlashTransport transport,
        StubImage? stub = null,
        FirmwareSymbols? symbols = null,
        Action<string>? log = null)
    {
        _transport = transport;
        _stub = stub ?? StubImage.LoadEmbedded();
        // 默认自动发现固件符号表（order.ini：环境变量 / exe 目录 / exe 目录 setting\），
        // 找不到才回退内置常量（对齐 MPTool 从产物/配置解析、常量仅兜底）。
        _symbols = symbols ?? FirmwareSymbols.LoadDefault();
        _log = log;
        _log?.Invoke($"固件符号来源: {_symbols.Source}（cb_mem_rwex=0x{_symbols.CbMemRwex:X8}, DcacheFlush=0x{_symbols.DcacheFlush:X8}, IcacheInit=0x{_symbols.IcacheInit:X8}）");
    }

    public string Name => "DC503J ROM 协议 (0xCD + SPI0 stub)";

    /// <summary>
    /// 查询设备端 Flash 信息（RDID + 容量）。会完成 stub 上传、cache 维护与 SPI0 初始化，
    /// 之后可直接继续 DownloadFirmwareAsync / VerifyFirmwareAsync（stub 已驻留 SDRAM）。
    /// </summary>
    public Task<ProtocolResult<FlashInfo>> GetFlashInfoAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            Report(null, FlashStage.OpeningDevice, 0, "准备 stub 并查询 Flash 信息...");

            ProtocolResult<FlashInfo> prepare = PrepareStubAndQuery(ct);
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
    /// 上传 stub 到 SDRAM + cache 维护 + SPI0 初始化 + 查询 RDID/容量。
    /// 供 GetFlashInfoAsync / DownloadFirmwareAsync 共用。
    /// </summary>
    private ProtocolResult<FlashInfo> PrepareStubAndQuery(CancellationToken ct)
    {
        // 0) 0xCD 通道可用性探针：发送一个已知无副作用的小命令，验证设备是否支持 0xCD 厂商通道。
        //    对齐 MPTool 思路：先发小命令探路，避免大块 stub 上传超时后才发现通道不可用。
        //    命令格式：cb_mem_rwex(哨兵 DataAddr=NoDataAddr, Func2=NoL2, Param=0)，期望 data-in 4 字节。
        ct.ThrowIfCancellationRequested();
        _log?.Invoke("[PrepareStub] 发送 0xCD 通道探针（Func1=cb_mem_rwex, DataAddr=NoDataAddr, Func2=NoL2, data-in 4B）...");
        ScsiCommandResult probe = _transport.SendDataIn(
            Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, Dc503RomCommands.NoDataAddr, Dc503RomCommands.NoL2, 0),
            ProbeDataInLength);
        if (!probe.Success)
        {
            string probeErr = probe.DescribeError();
            _log?.Invoke($"[PrepareStub] 0xCD 通道探针失败: {probeErr}");
            return ProtocolResult<FlashInfo>.Fail(
                $"0xCD 厂商通道不可用（探针失败: {probeErr}）。" +
                $"设备可能处于 Bootloader/Loader 模式，或固件符号地址（cb_mem_rwex=0x{_symbols.CbMemRwex:X8}）与当前固件不匹配。");
        }
        _log?.Invoke($"[PrepareStub] 0xCD 通道探针通过（返回 {probe.Response?.Length ?? 0} 字节）。");

        // 1) 上传 stub 到缓存 SDRAM（cb_mem_write：FIFO->SDRAM，经 D-cache）
        //    分块上传，对齐参考项目 MAX_DRV_DATA_TRANS_SIZE(0x400=1024) 和 LoaderRomProtocol。
        //    首块用 512B 较小值（探针验证后的首块通常较慢，小尺寸成功率更高），
        //    后续块恢复 1024B 以提高吞吐。
        //    设备端 SCSI FIFO 缓冲区有限，整段一次性发送会导致命令挂起超时（错误码 121）。
        ct.ThrowIfCancellationRequested();
        int totalLen = _stub.Segment.Length;
        int uploaded = 0;
        while (uploaded < totalLen)
        {
            ct.ThrowIfCancellationRequested();
            // 首块用较小尺寸，后续恢复标准尺寸
            int chunkSize = (uploaded == 0) ? StubFirstChunkSize : StubUploadChunkSize;
            int len = Math.Min(chunkSize, totalLen - uploaded);
            byte[] chunk = new byte[len];
            Array.Copy(_stub.Segment, uploaded, chunk, 0, len);

            _log?.Invoke($"[PrepareStub]   上传块 @0x{uploaded:X}，大小 {len} 字节...");
            ScsiCommandResult r = _transport.SendDataOut(
                Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, _stub.LoadBase + (uint)uploaded, Dc503RomCommands.NoL2, 0),
                chunk);
            if (!r.Success)
                return ProtocolResult<FlashInfo>.Fail(
                    $"stub 上传失败 @0x{uploaded:X}: {r.DescribeError()}。" +
                    $"设备可能处于 Bootloader/Loader 模式（0xCD 厂商通道不可用），请确认相机已进入应用模式。");
            uploaded += len;
        }

        // 2) D-cache 写回 + 回读校验：把 memcpy 留在 D-cache 的 stub 字节刷到 DRAM。
        //    首块调用 L2(DcacheFlush) 刷新整个 D-cache，后续块用 NoL2 直接从 DRAM 读取。
        //    分块回读（512B/块），对齐参考项目 MAX_DATA_TRANS_SIZE(0x200)。
        ct.ThrowIfCancellationRequested();
        for (int off = 0; off < totalLen; off += StubVerifyChunkSize)
        {
            ct.ThrowIfCancellationRequested();
            int len = Math.Min(StubVerifyChunkSize, totalLen - off);
            uint func2 = (off == 0) ? _symbols.DcacheFlush : Dc503RomCommands.NoL2;

            ScsiCommandResult r = _transport.SendDataIn(
                Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, _stub.LoadBase + (uint)off, func2, 0),
                len);
            if (!r.Success)
                return ProtocolResult<FlashInfo>.Fail($"D-cache 回读失败 @0x{off:X}: {r.DescribeError()}。");
            if (r.Response == null || r.Response.Length < len)
                return ProtocolResult<FlashInfo>.Fail($"stub 回读数据不足 @0x{off:X}（{r.Response?.Length ?? 0}/{len} 字节）。");
            if (!r.Response.AsSpan(0, len).SequenceEqual(_stub.Segment.AsSpan(off, len)))
                return ProtocolResult<FlashInfo>.Fail($"stub 上传校验失败 @0x{off:X}（回读内容与原始数据不匹配）。");
        }

        // 3) I-cache 全组失效（防止旧 stub 指令驻留），L2 注入 ax32xx_sysIcacheInit。
        //    DataAddr 用哨兵 -> ptxbuf，返回 4 字节垃圾数据，丢弃。
        ct.ThrowIfCancellationRequested();
        ScsiCommandResult icache = _transport.SendDataIn(
            Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, Dc503RomCommands.NoDataAddr, _symbols.IcacheInit, 0), 4);
        if (!icache.Success)
            return ProtocolResult<FlashInfo>.Fail($"I-cache 失效命令失败: {icache.DescribeError()}。");

        // 4) 初始化 SPI0（stub l1_func_spi_init，无数据）
        ct.ThrowIfCancellationRequested();
        ScsiCommandResult init = _transport.SendCommand(
            Dc503RomCommands.BuildCdb(_stub.L1SpiInit, 0, Dc503RomCommands.NoL2, 0));
        if (!init.Success)
            return ProtocolResult<FlashInfo>.Fail($"SPI0 初始化失败: {init.DescribeError()}。");

        // 5) RDID 确认 flash 在线（l2_func_spi_read_id，data-in 3 字节）
        ct.ThrowIfCancellationRequested();
        ScsiCommandResult rdid = _transport.SendDataIn(
            Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, Dc503RomCommands.NoDataAddr, _stub.L2ReadId, 0), 3);
        if (!rdid.Success)
            return ProtocolResult<FlashInfo>.Fail($"读取 flash ID 失败: {rdid.DescribeError()}。");
        if (rdid.Response == null || rdid.Response.Length < 3)
            return ProtocolResult<FlashInfo>.Fail($"读取 flash ID 数据不足（{rdid.Response?.Length ?? 0}/3 字节）。");
        ReadOnlySpan<byte> id = rdid.Response;
        if (id[0] == 0xFF && id[1] == 0xFF && id[2] == 0xFF)
            return ProtocolResult<FlashInfo>.Fail("flash 未响应（ID 全为 0xFF）。请检查连接。");
        if (id[0] == 0x00 && id[1] == 0x00 && id[2] == 0x00)
            return ProtocolResult<FlashInfo>.Fail("flash 未响应（ID 全为 0x00）。");

        // 6) 读取设备端 Flash 容量（l2_func_spi_read_capacity，4 字节 LE）
        ct.ThrowIfCancellationRequested();
        ScsiCommandResult cap = _transport.SendDataIn(
            Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, Dc503RomCommands.NoDataAddr, _stub.L2ReadCapacity, 0), 4);
        if (!cap.Success)
            return ProtocolResult<FlashInfo>.Fail($"读取 flash 容量失败: {cap.DescribeError()}。");
        if (cap.Response == null || cap.Response.Length < 4)
            return ProtocolResult<FlashInfo>.Fail($"读取 flash 容量数据不足（{cap.Response?.Length ?? 0}/4 字节）。");

        _deviceId = id.ToArray();
        _deviceCapacity = cap.Response[0]
            | ((uint)cap.Response[1] << 8)
            | ((uint)cap.Response[2] << 16)
            | ((uint)cap.Response[3] << 24);
        if (_deviceCapacity == 0)
            _log?.Invoke("警告: 设备未返回 Flash 容量（回退到默认值）。");

        return ProtocolResult<FlashInfo>.Ok(new FlashInfo(_deviceId, _deviceCapacity), "Flash 信息读取成功。");
    }

    /// <summary>当前已知的设备端 Flash 容量（0 = 未知）。</summary>
    public uint DeviceCapacity => _deviceCapacity;

    /// <summary>
    /// 0xDA 收尾复位。固件 cbw_update 会回 CSW、关中断并复位 USB 重新枚举（跳 bootloader）。
    /// 必须在下载/校验完成之后调用。与 Loader 0xCB 通道共用 UpdateModeCommand.SendAsync。
    /// </summary>
    public Task<ProtocolResult> EnterUpdateModeAsync(CancellationToken ct)
        => UpdateModeCommand.SendAsync(_transport, ct);

    /// <summary>
    /// 下载固件到 SPI NOR flash 起始区域：
    ///   上传 stub → D-cache flush → I-cache invalidate → SPI0 init → RDID 确认 flash
    ///   → 读取设备容量 → 容量 pattern 检测（可选）→ 擦除（可选整片）→
    ///   按 256B 页写入（末页 0xFF 补齐）→ 完成。
    /// </summary>
    public Task<ProtocolResult> DownloadFirmwareAsync(
        FirmwareImage firmware, IProgress<FlashProgress>? progress, CancellationToken ct, FlashDownloadOptions? options = null)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            // 1) 上传 stub + cache 维护 + SPI0 初始化 + 查询 RDID/容量（同时获得实时 Flash 容量）
            Report(progress, FlashStage.Downloading, 2, "上传 SPI stub 并初始化...");
            ProtocolResult<FlashInfo> prepare = PrepareStubAndQuery(ct);
            if (!prepare.Success)
                return Task.FromResult(ProtocolResult.Fail(prepare.Message));
            ReadOnlySpan<byte> id = _deviceId;

            // 2) 用实时查询到的容量校验固件大小（避免在 PrepareStubAndQuery 之前使用默认回退值）
            uint capacity = EffectiveCapacity();
            if (firmware.Length > capacity)
                return Task.FromResult(ProtocolResult.Fail($"固件大小 {firmware.Length} 字节超过 Flash 容量 {capacity} 字节。"));

            // 2a) 容量 pattern 检测（对齐 MPTool CheckCapacity）：写读比对识别灰片/坏片。
            //     仅整片擦除时运行——该测试会在容量中点擦写扇区，局部重刷会破坏固件区外的资源/参数分区。
            if (options?.EraseAll == true && options?.RunCapacityPatternTest != false)
            {
                Report(progress, FlashStage.Downloading, 8, "容量 pattern 检测...");
                ProtocolResult pattern = RunCapacityPatternTest((int)firmware.Length, ct);
                if (!pattern.Success)
                    return Task.FromResult(pattern);
            }

            // 3) 擦除 [0, alignedFirmwareLen) 区域（优先 64KB 块，尾部 4KB 扇区）
            //    每次擦除前发送 WREN，擦除后轮询 BUSY 位等待完成
            int total = (int)firmware.Length;
            uint regionEnd = (uint)((total + SectorSize - 1) / SectorSize) * (uint)SectorSize;
            if (options?.EraseAll == true)
            {
                regionEnd = capacity;
                _log?.Invoke($"[Download] 已开启整片擦除（ERASEALL），擦除区域扩展到全片 0x{capacity:X8}。");
            }
            uint blockEnd = regionEnd - regionEnd % BlockSize;

            // ★ 进度优化：擦除是全流程最慢阶段之一，原先仅开始时报一次 10%，进度条在擦除的
            //   10-20 秒内纹丝不动、界面看似卡死。现按块/扇区完成数逐项上报（映射 1%-10%）；
            //   FlashService 已做单调去重，UI 进度不会倒退。
            int ops = (int)(blockEnd / BlockSize) + (int)((regionEnd - blockEnd) / SectorSize);
            int done = 0;
            for (uint addr = 0; addr < blockEnd; addr += BlockSize)
            {
                ct.ThrowIfCancellationRequested();
                ProtocolResult we = SendWriteEnable();
                if (!we.Success)
                    return Task.FromResult(we);
                ScsiCommandResult r = _transport.SendCommand(
                    Dc503RomCommands.BuildCdb(_stub.L1SignalDrive, addr, Dc503RomCommands.FlashBlockErase, 0));
                if (!r.Success)
                    return Task.FromResult(ProtocolResult.Fail($"块擦除失败 @0x{addr:X8}: {r.DescribeError()}。"));
                ProtocolResult wait = WaitForFlashReady();
                if (!wait.Success)
                    return Task.FromResult(wait);
                done++;
                Report(progress, FlashStage.Downloading, 1 + 9 * done / Math.Max(ops, 1), $"擦除固件 {done}/{ops} 区域");
            }
            for (uint addr = blockEnd; addr < regionEnd; addr += SectorSize)
            {
                ct.ThrowIfCancellationRequested();
                ProtocolResult we = SendWriteEnable();
                if (!we.Success)
                    return Task.FromResult(we);
                ScsiCommandResult r = _transport.SendCommand(
                    Dc503RomCommands.BuildCdb(_stub.L1SignalDrive, addr, Dc503RomCommands.FlashSectorErase, 0));
                if (!r.Success)
                    return Task.FromResult(ProtocolResult.Fail($"扇区擦除失败 @0x{addr:X8}: {r.DescribeError()}。"));
                ProtocolResult wait = WaitForFlashReady();
                if (!wait.Success)
                    return Task.FromResult(wait);
                done++;
                Report(progress, FlashStage.Downloading, 1 + 9 * done / Math.Max(ops, 1), $"擦除固件 {done}/{ops} 区域");
            }

            // 4) 按 256B 页写入固件，引导扇区（前 512 字节）最后写入
            //    防止烧录中断时设备启动到不完整的固件
            //    末页不足 256B 时以 0xFF 补齐整页（对齐 MPTool 每次 data-out 均 0x100）。
            int pages = (total + PageSize - 1) / PageSize;
            int bootPages = Math.Min(BootSectorSize / PageSize, pages);  // 引导扇区页数（通常 2 页）
            int dataPages = pages - bootPages;  // 非引导扇区页数

            // 4a) 先写入非引导扇区页（从偏移 BootSectorSize 开始）
            for (int i = bootPages; i < pages; i++)
            {
                ct.ThrowIfCancellationRequested();

                int off = i * PageSize;
                byte[] page = MakePaddedPage(firmware.Data, off, total);

                ScsiCommandResult r = _transport.SendDataOut(
                    Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, Dc503RomCommands.NoDataAddr, _stub.L2PageProgram, (uint)off),
                    page);
                if (!r.Success)
                    return Task.FromResult(ProtocolResult.Fail($"页写入失败 @0x{off:X8}: {r.DescribeError()}。"));

                int percent = 12 + (i - bootPages + 1) * 76 / Math.Max(dataPages, 1);
                Report(progress, FlashStage.Downloading, percent, $"写入固件 {off + page.Length}/{total} 字节");
            }

            // 4b) 最后写入引导扇区（前 512 字节）
            for (int i = 0; i < bootPages; i++)
            {
                ct.ThrowIfCancellationRequested();

                int off = i * PageSize;
                byte[] page = MakePaddedPage(firmware.Data, off, total);

                ScsiCommandResult r = _transport.SendDataOut(
                    Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, Dc503RomCommands.NoDataAddr, _stub.L2PageProgram, (uint)off),
                    page);
                if (!r.Success)
                    return Task.FromResult(ProtocolResult.Fail($"引导扇区写入失败 @0x{off:X8}: {r.DescribeError()}。"));
            }

            // 引导扇区写后立即回读校验（对齐 MPTool DownBinCode：回读不一致则擦除 block 0，
            // 防止设备启动到不完整固件，并抛 ERR_SPI_VERIFY）
            // 期望内容 = 固件前 512 字节按 0xFF 补齐（与页写入补齐纪律一致；短固件不足 512 字节时
            // flash 中超出固件长度的字节为 0xFF，对齐 MPTool LoadCodeIntoBuffer 对短文件的 0xFF 填充）。
            _log?.Invoke($"[Download] 引导扇区写入后立即回读校验（前 {BootSectorSize} 字节 @0x00000000）...");
            ScsiCommandResult verifyRead = _transport.SendDataIn(
                Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, Dc503RomCommands.NoDataAddr, _stub.L2Read, 0), BootSectorSize);
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
                    Dc503RomCommands.BuildCdb(_stub.L1SignalDrive, 0, Dc503RomCommands.FlashBlockErase, 0));
                WaitForFlashReady();
                return Task.FromResult(ProtocolResult.Fail($"引导扇区校验失败（SR={sr:X2}），已擦除 block 0 防止启动到不完整固件，请重试。"));
            }
            _log?.Invoke($"[Download]   引导扇区回读一致（写入成功）。");

            Report(progress, FlashStage.Downloading, 92, $"固件下载完成（{total} 字节，{pages} 页）。");
            return Task.FromResult(ProtocolResult.Ok($"固件下载完成（{total} 字节，flash ID {id[0]:X2}{id[1]:X2}{id[2]:X2}）。"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(ProtocolResult.Fail($"固件下载失败: {ex.Message}"));
        }
    }

    /// <summary>
    /// 当前有效 Flash 容量：优先设备端报告值，未知时回退到内置默认（customer.h FLASH_CAPACITY = 4MB）。
    /// </summary>
    public uint EffectiveCapacity() => _deviceCapacity != 0 ? _deviceCapacity : 4 * 1024 * 1024;

    /// <summary>
    /// 回读校验：按 512B 分块从 flash 读取并逐字节比较。
    /// 可独立调用（无需先下载）。
    /// </summary>
    public Task<ProtocolResult> VerifyFirmwareAsync(
        FirmwareImage firmware, IProgress<FlashProgress>? progress, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            // 确保 SPI0 已初始化（下载后或设备重枚举后均可安全调用）
            ScsiCommandResult init = _transport.SendCommand(
                Dc503RomCommands.BuildCdb(_stub.L1SpiInit, 0, Dc503RomCommands.NoL2, 0));
            if (!init.Success)
                return Task.FromResult(ProtocolResult.Fail($"SPI0 初始化失败: {init.DescribeError()}。"));

            int total = (int)firmware.Length;
            for (int off = 0; off < total; off += ReadChunk)
            {
                ct.ThrowIfCancellationRequested();

                int chunkLen = Math.Min(ReadChunk, total - off);
                ScsiCommandResult r = _transport.SendDataIn(
                    Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, Dc503RomCommands.NoDataAddr, _stub.L2Read, (uint)off),
                    chunkLen);
                if (!r.Success || r.Response == null || r.Response.Length < chunkLen)
                    return Task.FromResult(ProtocolResult.Fail($"回读失败 @0x{off:X8}。"));

                if (!r.Response.AsSpan(0, chunkLen).SequenceEqual(firmware.Data.AsSpan(off, chunkLen)))
                {
                    // 对齐 MPTool "Code Verify Error!(SR=%02X)"：回读状态寄存器辅助定位原因
                    byte sr = ReadStatusRegister();
                    _log?.Invoke($"[Verify]     状态寄存器: SR=0x{sr:X2}");
                    return Task.FromResult(ProtocolResult.Fail($"校验不一致 @0x{off:X8}（SR={sr:X2}）。"));
                }

                Report(progress, FlashStage.Verifying, (off + chunkLen) * 100 / total, $"校验固件 {off + chunkLen}/{total} 字节");
            }

            return Task.FromResult(ProtocolResult.Ok($"固件校验通过（{total} 字节）。"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(ProtocolResult.Fail($"固件校验失败: {ex.Message}"));
        }
    }

    /// <summary>
    /// 导出整片 Flash 到文件（对齐 MPTool ExportSpiCodeToBin）：
    ///   上传 stub -> cache 维护 -> SPI0 init -> RDID/容量 -> 按 512B 分块读回整片 -> 写入 .bin 文件。
    /// 读命令与回读校验相同（cb_mem_rwex + L2Read），不改写任何字节（不做启动扇区补丁/CRC 重算），
    /// 导出的镜像用于备份/比对，如需可回烧需另跑 BootSector.Patch。
    /// </summary>
    public Task<ProtocolResult<ExportInfo>> ExportFirmwareAsync(
        string outputPath, IProgress<FlashProgress>? progress, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            _log?.Invoke($"[Export] 开始导出整片 Flash: {outputPath}。");

            Report(progress, FlashStage.Exporting, 2, "上传 SPI stub 并初始化...");
            ProtocolResult<FlashInfo> prepare = PrepareStubAndQuery(ct);
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
                    Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, Dc503RomCommands.NoDataAddr, _stub.L2Read, (uint)off), chunkLen);
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
    /// 发送 Write Enable (WREN, 0x06) 命令。SPI NOR Flash 标准要求在每次擦除/写入前使能 WEL。
    /// l1_func_signal_drive 是通用 SPI 命令派发器，不会自动添加 WREN，需主机显式发送。
    /// </summary>
    private ProtocolResult SendWriteEnable()
    {
        ScsiCommandResult r = _transport.SendCommand(
            Dc503RomCommands.BuildCdb(_stub.L1SignalDrive, 0, Dc503RomCommands.FlashWriteEnable, 0));
        return r.Success
            ? ProtocolResult.Ok("Write Enable 已发送。")
            : ProtocolResult.Fail($"Write Enable 失败: {r.DescribeError()}。");
    }

    /// <summary>
    /// 轮询 Flash 状态寄存器直到 BUSY 位清零（擦除/写入完成）。
    /// 通过 L2 函数 l2_func_spi_read_status 读取状态寄存器，检查 bit0 (BUSY)。
    /// </summary>
    private ProtocolResult WaitForFlashReady()
    {
        byte lastStatus = 0;
        for (int i = 0; i < StatusPollMaxRetries; i++)
        {
            ScsiCommandResult r = _transport.SendDataIn(
                Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, Dc503RomCommands.NoDataAddr, _stub.L2ReadStatus, 0), 1);
            if (!r.Success || r.Response == null || r.Response.Length < 1)
                return ProtocolResult.Fail($"读取状态寄存器失败: {r.DescribeError()}。");
            lastStatus = r.Response[0];
            if ((lastStatus & 0x01) == 0)
                return ProtocolResult.Ok("Flash 就绪。");  // BUSY 清零
            Thread.Sleep(10);
        }
        return ProtocolResult.Fail($"Flash 等待超时（{StatusPollMaxRetries * 10}ms，SR={lastStatus:X2}），设备可能异常。");
    }

    /// <summary>读取 Flash 状态寄存器（stub l2_func_spi_read_status），失败时返回 0。供校验失败/超时诊断。</summary>
    private byte ReadStatusRegister()
    {
        ScsiCommandResult r = _transport.SendDataIn(
            Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, Dc503RomCommands.NoDataAddr, _stub.L2ReadStatus, 0), 1);
        return r.Success && r.Response != null && r.Response.Length >= 1 ? r.Response[0] : (byte)0;
    }

    /// <summary>
    /// 构造一页写入数据：取 [off, min(off+PageSize, total)) 的固件字节，不足整页处以 0xFF 补齐。
    /// 对齐 MPTool 每页固定 data-out 0x100 的写块纪律。
    /// </summary>
    private static byte[] MakePaddedPage(byte[] data, int off, int total)
    {
        int pageLen = Math.Min(PageSize, total - off);
        var page = new byte[PageSize];
        Array.Fill(page, (byte)0xFF);
        Array.Copy(data, off, page, 0, pageLen);
        return page;
    }

    /// <summary>
    /// 构造引导扇区的期望内容：固件前 <see cref="BootSectorSize"/> 字节，不足处以 0xFF 补齐。
    /// 引导扇区页按整页写入（末字节 0xFF 补齐），故 flash 中超出固件长度的字节为 0xFF；
    /// 对齐 MPTool LoadCodeIntoBuffer 对短文件（&lt; 512B）的 0xFF 填充语义，避免短固件越界读取。
    /// </summary>
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
            _log?.Invoke($"[Download]   擦除扇区 @0x{addr:X8}...");
            ProtocolResult we = SendWriteEnable();
            if (!we.Success)
                return we;
            ScsiCommandResult e = _transport.SendCommand(
                Dc503RomCommands.BuildCdb(_stub.L1SignalDrive, addr, Dc503RomCommands.FlashSectorErase, 0));
            if (!e.Success)
                return ProtocolResult.Fail($"容量 pattern 扇区擦除失败 @0x{addr:X8}: {e.DescribeError()}。");
            ProtocolResult waitErase = WaitForFlashReady();
            if (!waitErase.Success)
                return waitErase;

            // 512B pattern 分 2 个 0x100 页写入（对齐 MPTool 每次 data-out 均 0x100）
            _log?.Invoke($"[Download]   写入 pattern 0x{pattern[0]:X2}×512 @0x{addr:X8}...");
            for (int off = 0; off < pattern.Length; off += PageSize)
            {
                ProtocolResult we2 = SendWriteEnable();
                if (!we2.Success)
                    return we2;
                ScsiCommandResult w = _transport.SendDataOut(
                    Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, Dc503RomCommands.NoDataAddr, _stub.L2PageProgram, addr + (uint)off),
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
                Dc503RomCommands.BuildCdb(_symbols.CbMemRwex, Dc503RomCommands.NoDataAddr, _stub.L2Read, addr), pattern.Length);
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

    private static void Report(IProgress<FlashProgress>? progress, FlashStage stage, int percent, string message)
        => progress?.Report(new FlashProgress(stage, percent, message));
}
