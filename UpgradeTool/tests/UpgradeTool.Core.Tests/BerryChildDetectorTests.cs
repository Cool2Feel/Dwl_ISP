using UpgradeTool.Core.Protocol;
using UpgradeTool.Core.Transport.Simulated;

namespace UpgradeTool.Core.Tests;

/// <summary>
/// AX2005 适配器 → Berry 子设备两阶段检测测试（对齐 MPTool AX2005Adapter→BerrySdio）：
///   - 适配器/子设备驱动 ELF 符号解析（probe_port/probe_dev/tgt_rw/bootSgmt_driver_check 等真实符号）；
///   - 0xCB L3 命令编解码（Func1/DataAddr 16 位大端 + Func2 NoL2）；
///   - 端到端检测：模拟适配器上挂 Flash/EEPROM 子设备 → 检测器两阶段识别。
/// 模拟设备与检测器均从真实驱动 ELF 解析符号地址，保证下发的就是 MPTool 使用的函数地址。
/// </summary>
public class BerryChildDetectorTests
{
    [Fact]
    public void AdapterDriver_ResolvesRequiredSymbols()
    {
        // 适配器驱动 AXIDEsdspi.elf：检测所需符号必须能从 ELF 符号表解析（对齐 MPTool pubsym）
        DriverImage img = DriverImage.LoadEmbedded("AXIDEsdspi.elf");

        Assert.NotEmpty(img.Segment);
        Assert.NotEqual(0u, img.Resolve("MemReadWrite"));   // 0x0C0D
        Assert.NotEqual(0u, img.Resolve("probe_port"));     // 0x1BC1
        Assert.NotEqual(0u, img.Resolve("probe_dev"));      // 0x1D73
        Assert.NotEqual(0u, img.Resolve("tgt_rw"));         // 0x1C94
        Assert.NotEqual(0u, img.Resolve("Init"));           // 0x1B8B
        Assert.NotEqual(0u, img.Resolve("Code2xDataOffset")); // 0x8000
    }

    [Fact]
    public void ChildDriver_ResolvesRequiredSymbols()
    {
        // 子设备固件 AX3233AXIDE_A2.elf：bootSgmt_driver_check / mem_rw / eeprom_init 必须可解析
        DriverImage img = DriverImage.LoadEmbedded("AX3233AXIDE_A2.elf");

        Assert.NotEmpty(img.Segment);
        Assert.NotEqual(0u, img.Resolve("mem_rw"));
        Assert.NotEqual(0u, img.Resolve("bootSgmt_driver_check"));
        Assert.NotEqual(0u, img.Resolve("eeprom_init"));
    }

    [Fact]
    public void AdapterCommands_BuildDecode_RoundTrip()
    {
        // 0xCB L3 命令编解码一致性（Func1/DataAddr 16 位大端 + Func2 小端 + Param 24 位）
        byte[] cdb = AdapterCommands.BuildCdb(0x1BC1, 0x8000, AdapterCommands.NoL2, 0x123456);
        (uint func1, uint dataAddr, uint func2, uint param) = AdapterCommands.DecodeCdb(cdb);

        Assert.Equal(0x1BC1u, func1);
        Assert.Equal(0x8000u, dataAddr);
        Assert.Equal(AdapterCommands.NoL2, func2);
        Assert.Equal(0x123456u, param);
    }

    [Fact]
    public void Probe_DetectsFlashChild_EndToEnd()
    {
        // 模拟适配器挂 Flash 子设备：两阶段检测应识别出 Flash + 正确 ID
        var device = new SimulatedAdapterDevice
        {
            ChildPresent = true,
            ChildKind = ChildDeviceType.Flash,
            FlashId = new byte[] { 0x85, 0x60, 0x16 },
        };
        using var transport = new SimulatedMscTransport(device);
        var detector = new BerryChildDetector(transport);

        ChildDeviceInfo result = detector.Probe(CancellationToken.None);

        Assert.True(result.AdapterDetected);
        Assert.True(result.ChildPresent);
        Assert.Equal(ChildDeviceType.Flash, result.ChildType);
        Assert.NotNull(result.FlashId);
        Assert.Equal(new byte[] { 0x85, 0x60, 0x16 }, result.FlashId);

        // 验证完整流程被真实执行：驱动上传/校验 + 初始化 + probe + 子设备固件上传 + 子设备识别
        Assert.True(device.AdapterUploadLength > 0);
        Assert.True(device.ChildUploadLength > 0);
        Assert.True(device.AdapterInitialized);
        Assert.True(device.ChildInitialized);
    }

    [Fact]
    public void Probe_EepromChild_ReturnsEeprom()
    {
        var device = new SimulatedAdapterDevice { ChildPresent = true, ChildKind = ChildDeviceType.Eeprom };
        using var transport = new SimulatedMscTransport(device);
        var detector = new BerryChildDetector(transport);

        ChildDeviceInfo result = detector.Probe(CancellationToken.None);

        Assert.True(result.AdapterDetected);
        Assert.True(result.ChildPresent);
        Assert.Equal(ChildDeviceType.Eeprom, result.ChildType);
    }

    [Fact]
    public void Probe_NoChild_AdapterOnlineButNoChild()
    {
        // 适配器在线但未挂子设备：probe_port 连续无响应 → 报告适配器在线、无子设备
        var device = new SimulatedAdapterDevice { ChildPresent = false };
        using var transport = new SimulatedMscTransport(device);
        var detector = new BerryChildDetector(transport);

        ChildDeviceInfo result = detector.Probe(CancellationToken.None);

        Assert.True(result.AdapterDetected);
        Assert.False(result.ChildPresent);
        Assert.Equal(ChildDeviceType.None, result.ChildType);
        Assert.True(device.AdapterInitialized); // 适配器已初始化后才进入 probe
    }
}
