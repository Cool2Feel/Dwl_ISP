using System.Text;
using UpgradeTool.Core.Abstractions;
using UpgradeTool.Core.Devices;
using UpgradeTool.Core.Protocol;
using UpgradeTool.Core.Transport.Simulated;
using UpgradeTool.Core.Utilities;

namespace UpgradeTool.Core.Tests;

/// <summary>
/// Loader（下载器）态协议端到端验证：用 SimulatedLoaderDevice（测试替身，复刻 0xCB 通道）
/// 驱动真实协议 LoaderRomProtocol 与 FlashService 编排（上传驱动 → RDID/容量 → 擦除 →
/// 页写入 → 回读校验 → 0xDA 复位）。
/// </summary>
public class LoaderProtocolFlowTests
{
    private static FlashLib LoadFlashLib()
    {
        var asm = typeof(LoaderProtocolFlowTests).Assembly;
        using Stream? stream = asm.GetManifestResourceStream("UpgradeTool.Core.Tests.Resources.FlashLib.ini")
            ?? throw new InvalidOperationException("缺少内嵌资源 FlashLib.ini。");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return FlashLib.Parse(Encoding.Latin1.GetString(ms.ToArray()));
    }

    private static LoaderConfig Config() =>
        LoaderConfig.Create(LoaderImage.LoadEmbedded(), LoadFlashLib());

    private static FirmwareImage CreateFirmware(int length)
    {
        var random = new Random(42);
        var data = new byte[length];
        random.NextBytes(data);
        return new FirmwareImage("simulated.bin", data);
    }

    private static (SimulatedLoaderDevice Device, LoaderRomProtocol Protocol) CreateProtocol()
    {
        var device = new SimulatedLoaderDevice();
        var transport = new SimulatedMscTransport(device);
        return (device, new LoaderRomProtocol(transport, Config()));
    }

    private static MscDeviceInfo LoaderDevice(string path = "\\\\?\\GLOBALROOT\\Device\\FakeLoader") =>
        new(path, 0x1234, 0x5678, "Buildwin Loader",
            VendorId: "Buildwin", ProductId: "BuildWinVideo050Loader", IsTarget: true);

    private static DeviceConnection CreateConnection()
    {
        var device = new SimulatedLoaderDevice();
        var transport = new SimulatedMscTransport(device);
        transport.Open();
        var protocol = new LoaderRomProtocol(transport, Config());
        return new DeviceConnection(LoaderDevice(), transport, protocol, null);
    }

    // ---------- LoaderRomProtocol ----------

    [Fact]
    public async Task GetFlashInfo_InitializesSpiAndReportsCapacity()
    {
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();

        ProtocolResult<FlashInfo> result = await protocol.GetFlashInfoAsync(CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);
        Assert.Equal("EF 40 18 00", result.Value!.IdText);
        Assert.Equal((uint)SimulatedLoaderDevice.MaxFlashSize, result.Value.CapacityBytes);
        Assert.True(device.SpiInitialized);
    }

    [Fact]
    public void IdProbe_ReturnsSameId_RegardlessOfDataAddr()
    {
        // loader 的 L2 SPI 读驱动固定把结果落到 FlashReadBuf(0x04070000)，主机 DataAddr 只是约定。
        // 真机证实：0x01030000 与 0x04070000 读回内容一致（123/777 正常，321/555/888 均 1F FF FF）。
        var img = LoaderImage.LoadEmbedded();
        uint l1SignalDrive = img.Resolve("l1_func_signal_drive", LoaderConfig.DefaultUploadBase);
        var device = new SimulatedLoaderDevice();

        byte[] cdbSig = LoaderRomCommands.BuildFlashReadCdb(l1SignalDrive, LoaderConfig.DefaultSigdrvBuf, 0x9F, 0);
        byte[] cdbBuf = LoaderRomCommands.BuildFlashReadCdb(l1SignalDrive, LoaderConfig.FlashReadBuf, 0x9F, 0);

        Assert.True(device.Handle(cdbSig, null, 4, out byte[] fromSig));
        Assert.True(device.Handle(cdbBuf, null, 4, out byte[] fromBuf));
        Assert.Equal(fromSig, fromBuf);
        Assert.True(fromSig.Length >= 3 && fromSig[0] != 0x1F, "ID 不应为 1F FF FF");
    }

    [Fact]
    public async Task Download_WritesFirmwareToFlash()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();

        ProtocolResult result = await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.True(device.Flash.AsSpan(0, (int)firmware.Length).SequenceEqual(firmware.Data));
    }

    [Fact]
    public async Task Download_WithDeviceLibSelectedDriver_Succeeds()
    {
        // 对齐 MPTool DeviceLib.ini：BuildWinVideo060Loader -> ThunderBD.elf，
        // RBC_mem_rwex_buf 从 ELF 符号表解析为 0xB200（不同于 ThunderSE 的 0x4A00），端到端下载应成功
        FirmwareImage firmware = CreateFirmware(10_000);
        var device = new SimulatedLoaderDevice();
        var transport = new SimulatedMscTransport(device);
        var protocol = new LoaderRomProtocol(transport, LoaderConfig.ForProduct("BuildWinVideo060Loader"));

        ProtocolResult result = await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.True(device.Flash.AsSpan(0, (int)firmware.Length).SequenceEqual(firmware.Data));
    }

    [Fact]
    public async Task Download_EraseDoesNotTouchDataBeyondErasedRegion()
    {
        FirmwareImage first = CreateFirmware(5_000);
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();
        await protocol.DownloadFirmwareAsync(first, null, CancellationToken.None);

        FirmwareImage second = CreateFirmware(2_000);
        await protocol.DownloadFirmwareAsync(second, null, CancellationToken.None);

        Assert.True(device.Flash.AsSpan(0, (int)second.Length).SequenceEqual(second.Data));
        for (int i = (int)second.Length; i < 4096; i++)
            Assert.Equal((byte)0xFF, device.Flash[i]);
        Assert.True(device.Flash.AsSpan(4096, (int)first.Length - 4096).SequenceEqual(first.Data.AsSpan(4096)));
    }

    [Fact]
    public async Task Verify_DetectsCorruption()
    {
        FirmwareImage firmware = CreateFirmware(8_192);
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();

        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        device.Flash[0] ^= 0xFF;

        ProtocolResult verify = await protocol.VerifyFirmwareAsync(firmware, null, CancellationToken.None);

        Assert.False(verify.Success);
    }

    [Fact]
    public async Task Verify_PassesForIntactFirmware()
    {
        FirmwareImage firmware = CreateFirmware(8_192);
        (SimulatedLoaderDevice _, LoaderRomProtocol protocol) = CreateProtocol();

        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        ProtocolResult verify = await protocol.VerifyFirmwareAsync(firmware, null, CancellationToken.None);

        Assert.True(verify.Success, verify.Message);
    }

    [Fact]
    public async Task Download_RejectsFirmwareLargerThanDeviceFlash()
    {
        FirmwareImage oversized = CreateFirmware(SimulatedLoaderDevice.MaxFlashSize + 1);
        (SimulatedLoaderDevice _, LoaderRomProtocol protocol) = CreateProtocol();

        ProtocolResult result = await protocol.DownloadFirmwareAsync(oversized, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("超过", result.Message);
    }

    // ---------- MPTool 流程对等改进（容量 pattern / 整片擦除 / 页补齐 / SR 诊断） ----------

    [Fact]
    public async Task Download_DoesNotRunCapacityPatternTest_ByDefault()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();

        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        // 默认（非整片擦除）不运行容量 pattern 测试：容量中点不应被写入 0xA5（保持 0xFF）
        Assert.Equal((byte)0xFF, device.Flash[SimulatedLoaderDevice.MaxFlashSize / 2]);
        Assert.True(device.Flash.AsSpan(0, (int)firmware.Length).SequenceEqual(firmware.Data));
    }

    [Fact]
    public async Task Download_CapacityPatternTest_DetectsFaultyFlash()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();
        device.FailCapacityPatternTest = true;

        // 容量 pattern 测试仅在整片擦除时运行
        ProtocolResult result = await protocol.DownloadFirmwareAsync(
            firmware, null, CancellationToken.None,
            new FlashDownloadOptions(EraseAll: true, RunCapacityPatternTest: true));

        Assert.False(result.Success);
        Assert.Contains("容量 pattern 校验失败", result.Message);
    }

    [Fact]
    public async Task Download_EraseAll_WipesEntireFlash()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();
        Array.Fill(device.Flash, (byte)0xAA); // 全片脏数据

        ProtocolResult result = await protocol.DownloadFirmwareAsync(
            firmware, null, CancellationToken.None, new FlashDownloadOptions(EraseAll: true));

        Assert.True(result.Success, result.Message);
        Assert.True(device.Flash.AsSpan(0, (int)firmware.Length).SequenceEqual(firmware.Data));
        for (int i = (int)firmware.Length; i < SimulatedLoaderDevice.MaxFlashSize; i++)
            Assert.Equal((byte)0xFF, device.Flash[i]);
    }

    [Fact]
    public async Task Download_WithoutEraseAll_KeepsDataBeyondFirmwareRegion()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();
        Array.Fill(device.Flash, (byte)0xAA);
        device.Flash[0x100000] = 0x5A; // 固件区域外的脏数据

        ProtocolResult result = await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        // 非整片擦除：固件区域外（含 0x100000 的 0x5A）应保留
        Assert.Equal((byte)0x5A, device.Flash[0x100000]);
    }

    [Fact]
    public async Task Download_PadsFinalPageToFullPageSize()
    {
        FirmwareImage firmware = CreateFirmware(10_000); // 39*256 + 16，末页不足整页
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();

        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        int expectedPages = (10_000 + 255) / 256;
        Assert.Equal(expectedPages, device.PageProgramSizes.Count(s => s == 256));
        Assert.Equal(256, device.PageProgramSizes[^1]); // 末页也补齐整页
    }

    [Fact]
    public async Task Verify_ReportsStatusRegisterOnMismatch()
    {
        FirmwareImage firmware = CreateFirmware(8_192);
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();
        device.StatusRegister = 0x42;

        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);
        device.Flash[0] ^= 0xFF;

        ProtocolResult verify = await protocol.VerifyFirmwareAsync(firmware, null, CancellationToken.None);

        Assert.False(verify.Success);
        Assert.Contains("SR=42", verify.Message);
    }

    [Fact]
    public async Task FullFlow_PatchesBootChecksumBeforeDownload()
    {
        byte[] data = TestFirmware.WithBootSector(10_000, checksumByte: 0x00);
        var device = new SimulatedLoaderDevice();
        var transport = new SimulatedMscTransport(device);
        transport.Open();
        var protocol = new LoaderRomProtocol(transport, Config());
        using DeviceConnection conn = new(LoaderDevice(), transport, protocol, null);

        FlashSessionResult result = await FlashService.RunAsync(
            new FlashRunOptions(conn.Info, TestFirmware.Image(data), VerifyAfterDownload: true, Connected: conn),
            null,
            log: null);

        Assert.True(result.Success, result.Summary);
        // byte8 按"补丁后镜像"（已含 CRC16 写入）计算，刷入 flash 的应为该值
        byte expected = BootSector.Patch(TestFirmware.Image(data)).Data[BootSector.ChecksumOffset];
        Assert.Equal(expected, device.Flash[BootSector.ChecksumOffset]);
        Assert.True(BootSector.SectorSumsToZero(device.Flash.AsSpan(0, BootSector.SectorSize)));
    }

    [Fact]
    public async Task EnterUpdateMode_RequestsLoaderReset()
    {
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();

        ProtocolResult result = await protocol.EnterUpdateModeAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(device.ResetRequested);
    }

    // ---------- 导出固件（对齐 MPTool ExportSpiCodeToBin：整片回读） ----------

    [Fact]
    public async Task Export_WritesWholeFlashToFile()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();
        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        string output = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid():N}.bin");
        try
        {
            ProtocolResult<ExportInfo> result = await protocol.ExportFirmwareAsync(output, null, CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.Value);
            Assert.Equal(SimulatedLoaderDevice.MaxFlashSize, result.Value!.Length);
            // 导出长度 = 整片容量，文件内容与设备 Flash 完全一致
            byte[] exported = File.ReadAllBytes(output);
            Assert.Equal(device.Flash, exported);
            Assert.Equal(Crc32.Compute(device.Flash), result.Value.Crc32);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task Export_DoesNotModifyFlash()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();
        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);
        byte[] before = (byte[])device.Flash.Clone();

        string output = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid():N}.bin");
        try
        {
            ProtocolResult<ExportInfo> result = await protocol.ExportFirmwareAsync(output, null, CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Equal(before, device.Flash);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task Export_Progress_ReportsExportingStage()
    {
        FirmwareImage firmware = CreateFirmware(4_096);
        (SimulatedLoaderDevice _, LoaderRomProtocol protocol) = CreateProtocol();
        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        string output = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid():N}.bin");
        try
        {
            var progress = new List<FlashProgress>();
            ProtocolResult<ExportInfo> result = await protocol.ExportFirmwareAsync(output, new Progress<FlashProgress>(progress.Add), CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Contains(progress, p => p.Stage == FlashStage.Exporting);
            Assert.True(progress[^1].Percent >= 90);
        }
        finally
        {
            File.Delete(output);
        }
    }

    // ---------- 短固件 / 引导扇区校验路径（对齐 MPTool LoadCodeIntoBuffer 0xFF 填充） ----------

    [Fact]
    public async Task Download_ShortFirmware_BootSectorVerifyDoesNotCrash()
    {
        // 300 字节固件（短于引导扇区 512B）：引导扇区回读期望内容 = 固件 + 0xFF 补齐，
        // 不再对 firmware.Data 越界读取（旧逻辑在 i>=Length 时抛 IndexOutOfRange）。
        FirmwareImage firmware = CreateFirmware(300);
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();

        ProtocolResult result = await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.True(device.Flash.AsSpan(0, 300).SequenceEqual(firmware.Data));
        for (int i = 300; i < 512; i++)
            Assert.Equal((byte)0xFF, device.Flash[i]); // 超出固件长度的引导扇区为 0xFF
    }

    [Fact]
    public async Task Download_BootSectorVerifyFailure_ErasesBlock0()
    {
        // 对齐 MPTool DownBinCode：引导扇区回读不一致 -> 读 SR -> 擦除 block 0（64KB）
        // 防止设备启动到不完整固件，并返回 ERR_SPI_VERIFY 语义错误。
        FirmwareImage firmware = CreateFirmware(10_000);
        (SimulatedLoaderDevice device, LoaderRomProtocol protocol) = CreateProtocol();
        device.CorruptBootSectorRead = true; // 模拟地址 0 回读首字节不匹配

        ProtocolResult result = await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("引导扇区校验失败", result.Message);
        Assert.All(device.Flash.AsSpan(0, 65536).ToArray(), b => Assert.Equal((byte)0xFF, b));
    }

    // ---------- FlashService 编排 ----------

    [Fact]
    public async Task FullFlow_DownloadVerifyReset_Succeeds()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        using DeviceConnection conn = CreateConnection();

        var progress = new List<FlashProgress>();
        FlashSessionResult result = await FlashService.RunAsync(
            new FlashRunOptions(conn.Info, firmware, VerifyAfterDownload: true, Connected: conn),
            new Progress<FlashProgress>(progress.Add),
            log: null);

        Assert.True(result.Success, result.Summary);
        Assert.Equal(FlashStage.Completed, result.FinalStage);
        Assert.Contains(progress, p => p.Stage == FlashStage.Downloading);
        Assert.Contains(progress, p => p.Stage == FlashStage.Verifying);
        Assert.Contains(progress, p => p.Stage == FlashStage.EnteringUpdateMode);
    }

    [Fact]
    public async Task FullFlow_Progress_IsMonotonic()
    {
        FirmwareImage firmware = CreateFirmware(5_000);
        using DeviceConnection conn = CreateConnection();

        var progress = new List<FlashProgress>();
        await FlashService.RunAsync(
            new FlashRunOptions(conn.Info, firmware, VerifyAfterDownload: true, Connected: conn),
            new Progress<FlashProgress>(progress.Add),
            log: null);

        for (int i = 1; i < progress.Count; i++)
            Assert.True(progress[i].Percent >= progress[i - 1].Percent, $"进度回退: {progress[i - 1].Percent} -> {progress[i].Percent}");
    }

    // ---------- 导出固件编排（ExportService） ----------

    [Fact]
    public async Task ExportService_RunAsync_ExportsToFile()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        using DeviceConnection conn = CreateConnection();

        string output = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid():N}.bin");
        try
        {
            ExportSessionResult result = await ExportService.RunAsync(
                new ExportRunOptions(conn.Info, output, Connected: conn),
                log: null);

            Assert.True(result.Success, result.Summary);
            Assert.Equal(FlashStage.Completed, result.FinalStage);
            Assert.True(File.Exists(output));
            Assert.Equal(SimulatedLoaderDevice.MaxFlashSize, new FileInfo(output).Length);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task ExportService_RunAsync_ReportsCrc32()
    {
        using DeviceConnection conn = CreateConnection();

        string output = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid():N}.bin");
        try
        {
            var progress = new List<FlashProgress>();
            ExportSessionResult result = await ExportService.RunAsync(
                new ExportRunOptions(conn.Info, output, Connected: conn),
                new Progress<FlashProgress>(progress.Add),
                log: null);

            Assert.True(result.Success, result.Summary);
            Assert.Contains("CRC32=0x", result.Summary);
            Assert.Contains(progress, p => p.Stage == FlashStage.Exporting);
        }
        finally
        {
            File.Delete(output);
        }
    }
}
