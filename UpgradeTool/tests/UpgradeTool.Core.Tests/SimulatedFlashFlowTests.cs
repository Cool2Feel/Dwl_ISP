using UpgradeTool.Core.Abstractions;
using UpgradeTool.Core.Devices;
using UpgradeTool.Core.Protocol;
using UpgradeTool.Core.Transport.Simulated;
using UpgradeTool.Core.Utilities;

namespace UpgradeTool.Core.Tests;

/// <summary>
/// 用模拟设备（SimulatedMscDevice 作为测试替身，复刻固件 0xCD 通道）端到端验证真实协议
/// Dc503RomProtocol 与 FlashService 编排。
/// 运行流程只连接真实设备，因此 FlashService 会话通过已建好的 DeviceConnection（Connected 复用）驱动。
/// </summary>
public class SimulatedFlashFlowTests
{
    private static FirmwareImage CreateFirmware(int length)
    {
        var random = new Random(42);
        var data = new byte[length];
        random.NextBytes(data);
        return new FirmwareImage("simulated.bin", data);
    }

    private static FlashRunOptions Options(FirmwareImage firmware, DeviceConnection conn, bool verify = true) => new(
        conn.Info,
        firmware,
        VerifyAfterDownload: verify,
        Connected: conn);

    private static DeviceConnection CreateConnection()
    {
        var transport = new SimulatedMscTransport(new SimulatedMscDevice());
        transport.Open();
        var protocol = new Dc503RomProtocol(transport);
        return new DeviceConnection(
            new MscDeviceInfo("simulated", 0, 0, "模拟"),
            transport,
            protocol,
            null);
    }

    private static (SimulatedMscDevice Device, Dc503RomProtocol Protocol) CreateProtocol()
    {
        var device = new SimulatedMscDevice();
        var transport = new SimulatedMscTransport(device);
        return (device, new Dc503RomProtocol(transport));
    }

    [Fact]
    public async Task FullFlow_DownloadVerifyReset_Succeeds()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        using DeviceConnection conn = CreateConnection();

        var progress = new List<FlashProgress>();
        FlashSessionResult result = await FlashService.RunAsync(
            Options(firmware, conn),
            new Progress<FlashProgress>(progress.Add),
            log: null);

        Assert.True(result.Success, result.Summary);
        Assert.Equal(FlashStage.Completed, result.FinalStage);
        Assert.Equal(100, progress[^1].Percent);

        // 关键阶段都应出现
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
        await FlashService.RunAsync(Options(firmware, conn), new Progress<FlashProgress>(progress.Add), log: null);

        for (int i = 1; i < progress.Count; i++)
            Assert.True(progress[i].Percent >= progress[i - 1].Percent, $"进度回退: {progress[i - 1].Percent} -> {progress[i].Percent}");
    }

    [Fact]
    public async Task Download_WithoutEnteringUpdateMode_Succeeds()
    {
        // 架构事实：0xCD 通道在应用态可用，下载无需先 0xDA。
        FirmwareImage firmware = CreateFirmware(4_096);
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();

        ProtocolResult result = await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(SimulatedMscDevice.DeviceMode.Storage, device.Mode);
        Assert.True(device.SpiInitialized);
    }

    [Fact]
    public async Task Download_WritesFirmwareToFlashAndUploadsStub()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();

        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        // stub 上传到模拟 SDRAM 且内容与内嵌资源一致
        StubImage stub = StubImage.LoadEmbedded();
        Assert.NotNull(device.UploadedStub);
        Assert.Equal(stub.Segment, device.UploadedStub);

        // flash [0, firmwareLen) 与固件一致
        Assert.True(device.Flash.AsSpan(0, (int)firmware.Length).SequenceEqual(firmware.Data));
    }

    [Fact]
    public async Task Verify_DetectsCorruption()
    {
        FirmwareImage firmware = CreateFirmware(8_192);
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();

        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        // 篡改设备虚拟 Flash 中的一个字节
        device.Flash[0] ^= 0xFF;

        ProtocolResult verify = await protocol.VerifyFirmwareAsync(firmware, null, CancellationToken.None);

        Assert.False(verify.Success);
    }

    [Fact]
    public async Task EnterUpdateMode_SwitchesDeviceToBootloader()
    {
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();

        Assert.Equal(SimulatedMscDevice.DeviceMode.Storage, device.Mode);

        ProtocolResult result = await protocol.EnterUpdateModeAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(SimulatedMscDevice.DeviceMode.Bootloader, device.Mode);
    }

    [Fact]
    public async Task Download_EraseDoesNotTouchDataBeyondErasedRegion()
    {
        // 先写 5000B 固件（占用 0..4096 扇区 + 4096..5000 扇区），再写 2000B 固件：
        // 第二次只擦除 [0,4096)，所以 [4096,5000) 的旧数据应原样保留。
        FirmwareImage first = CreateFirmware(5_000);
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();
        await protocol.DownloadFirmwareAsync(first, null, CancellationToken.None);

        FirmwareImage second = CreateFirmware(2_000);
        await protocol.DownloadFirmwareAsync(second, null, CancellationToken.None);

        Assert.True(device.Flash.AsSpan(0, (int)second.Length).SequenceEqual(second.Data));
        // [2000,4096) 已擦除
        for (int i = (int)second.Length; i < 4096; i++)
            Assert.Equal((byte)0xFF, device.Flash[i]);
        // [4096,5000) 保留第一次固件数据
        Assert.True(device.Flash.AsSpan(4096, (int)first.Length - 4096).SequenceEqual(first.Data.AsSpan(4096)));
    }

    // ---------- MPTool 流程对等改进（容量 pattern / 整片擦除 / 页补齐 / SR 诊断） ----------

    [Fact]
    public async Task Download_DoesNotRunCapacityPatternTest_ByDefault()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();

        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        // 默认（非整片擦除）不运行容量 pattern 测试：容量中点不应被写入 0xA5（保持 0xFF）
        Assert.Equal((byte)0xFF, device.Flash[SimulatedMscDevice.MaxFlashSize / 2]);
        Assert.True(device.Flash.AsSpan(0, (int)firmware.Length).SequenceEqual(firmware.Data));
    }

    [Fact]
    public async Task Download_CapacityPatternTest_DetectsFaultyFlash()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();
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
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();
        Array.Fill(device.Flash, (byte)0xAA);

        ProtocolResult result = await protocol.DownloadFirmwareAsync(
            firmware, null, CancellationToken.None, new FlashDownloadOptions(EraseAll: true));

        Assert.True(result.Success, result.Message);
        Assert.True(device.Flash.AsSpan(0, (int)firmware.Length).SequenceEqual(firmware.Data));
        for (int i = (int)firmware.Length; i < SimulatedMscDevice.MaxFlashSize; i++)
            Assert.Equal((byte)0xFF, device.Flash[i]);
    }

    [Fact]
    public async Task Download_PadsFinalPageToFullPageSize()
    {
        FirmwareImage firmware = CreateFirmware(10_000); // 39*256 + 16，末页不足整页
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();

        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        int expectedPages = (10_000 + 255) / 256;
        Assert.Equal(expectedPages, device.PageProgramSizes.Count(s => s == 256));
        Assert.Equal(256, device.PageProgramSizes[^1]);
    }

    [Fact]
    public async Task Verify_ReportsStatusRegisterOnMismatch()
    {
        FirmwareImage firmware = CreateFirmware(8_192);
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();
        device.StatusRegister = 0x42;

        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);
        device.Flash[0] ^= 0xFF;

        ProtocolResult verify = await protocol.VerifyFirmwareAsync(firmware, null, CancellationToken.None);

        Assert.False(verify.Success);
        Assert.Contains("SR=42", verify.Message);
    }

    // ---------- 短固件 / 引导扇区校验路径（对齐 MPTool LoadCodeIntoBuffer 0xFF 填充） ----------

    [Fact]
    public async Task Download_ShortFirmware_BootSectorVerifyDoesNotCrash()
    {
        // 300 字节固件（短于引导扇区 512B）：引导扇区回读期望内容 = 固件 + 0xFF 补齐，
        // 不再对 firmware.Data 越界读取（旧逻辑在 i>=Length 时抛 IndexOutOfRange）。
        FirmwareImage firmware = CreateFirmware(300);
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();

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
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();
        device.CorruptBootSectorRead = true; // 模拟地址 0 回读首字节不匹配

        ProtocolResult result = await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("引导扇区校验失败", result.Message);
        Assert.All(device.Flash.AsSpan(0, Dc503RomProtocol.BlockSize).ToArray(), b => Assert.Equal((byte)0xFF, b));
    }

    // ---------- 导出固件（对齐 MPTool ExportSpiCodeToBin：整片回读） ----------

    [Fact]
    public async Task Export_WritesWholeFlashToFile()
    {
        FirmwareImage firmware = CreateFirmware(10_000);
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();
        await protocol.DownloadFirmwareAsync(firmware, null, CancellationToken.None);

        string output = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid():N}.bin");
        try
        {
            ProtocolResult<ExportInfo> result = await protocol.ExportFirmwareAsync(output, null, CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.Value);
            Assert.Equal(SimulatedMscDevice.MaxFlashSize, result.Value!.Length);
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
        (SimulatedMscDevice device, Dc503RomProtocol protocol) = CreateProtocol();
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
        (SimulatedMscDevice _, Dc503RomProtocol protocol) = CreateProtocol();
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
}
