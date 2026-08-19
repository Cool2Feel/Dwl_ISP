using UpgradeTool.Core.Abstractions;
using UpgradeTool.Core.Devices;
using UpgradeTool.Core.Protocol;
using UpgradeTool.Core.Transport.Simulated;

namespace UpgradeTool.Core.Tests;

/// <summary>
/// 协议工厂（ProtocolFactory）选择逻辑测试：
/// INQUIRY 厂商/产品串判定 Loader 态（0xCB）与应用态（0xCD），并统一走 DeviceSignature.IsLoader。
/// </summary>
public class ProtocolFactoryTests
{
    [Fact]
    public void Create_LoaderProductId_UsesLoaderProtocol()
    {
        var transport = new SimulatedMscTransport(new SimulatedLoaderDevice());

        IFlashProtocol protocol = ProtocolFactory.Create(transport, "Buildwin", "BuildWinVideo050Loader");

        Assert.IsType<LoaderRomProtocol>(protocol);
        Assert.True(DeviceSignature.IsLoader("Buildwin", "BuildWinVideo050Loader"));
    }

    [Fact]
    public void Create_LoaderToken_IsCaseInsensitive()
    {
        var transport = new SimulatedMscTransport(new SimulatedLoaderDevice());

        IFlashProtocol protocol = ProtocolFactory.Create(transport, "buildwin", "BUILDWINloader");

        Assert.IsType<LoaderRomProtocol>(protocol);
    }

    [Fact]
    public void Create_AppProductId_UsesDc503Protocol()
    {
        var transport = new SimulatedMscTransport(new SimulatedMscDevice());

        IFlashProtocol protocol = ProtocolFactory.Create(transport, "Buildwin", "Media-Player");

        Assert.IsType<Dc503RomProtocol>(protocol);
        Assert.False(DeviceSignature.IsLoader("Buildwin", "Media-Player"));
    }

    [Fact]
    public void Create_UnknownIdentity_DefaultsToDc503Protocol()
    {
        var transport = new SimulatedMscTransport(new SimulatedMscDevice());

        IFlashProtocol protocol = ProtocolFactory.Create(transport, null, null);

        Assert.IsType<Dc503RomProtocol>(protocol);
    }

    [Fact]
    public void Create_LogsSelectedMode()
    {
        var transport = new SimulatedMscTransport(new SimulatedLoaderDevice());
        var logs = new List<string>();

        ProtocolFactory.Create(transport, "Buildwin", "BuildWinVideo050Loader", logs.Add);

        Assert.Contains(logs, l => l.Contains("Loader 模式"));
    }

    [Fact]
    public void Create_LoaderProduct_SelectsDriverViaDeviceLib()
    {
        // 对齐 MPTool DeviceLib.ini：不同 loader 版本选不同驱动 ELF，RBC_mem_rwex_buf 随 ELF 变化
        var t050 = new SimulatedMscTransport(new SimulatedLoaderDevice());
        var logs050 = new List<string>();
        ProtocolFactory.Create(t050, "Buildwin", "BuildWinVideo050Loader", logs050.Add);
        Assert.Contains(logs050, l => l.Contains("驱动: ThunderSE.elf") && l.Contains("RBC_mem_rwex_buf=0x00004A00"));

        var t060 = new SimulatedMscTransport(new SimulatedLoaderDevice());
        var logs060 = new List<string>();
        ProtocolFactory.Create(t060, "Buildwin", "BuildWinVideo060Loader", logs060.Add);
        Assert.Contains(logs060, l => l.Contains("驱动: ThunderBD.elf") && l.Contains("RBC_mem_rwex_buf=0x0000B200"));
    }

    [Fact]
    public void CreateForDevice_UsesMatchedEntry_AsSingleSourceOfTruth()
    {
        // 对齐 MPTool SearchDev：枚举阶段识别出的 DeviceEntry（ClassInfo/SpiDriverPath）
        // 直接驱动适配器与驱动选择，不再独立重匹配设备库。
        DeviceEntry entry = new(
            Index: 10,
            InquiryInfo: "BuildWinVideo050Loader  1.00",
            ClassInfo: "AX326X",
            SpiDriverPath: "ThunderSE.elf",
            FuncListPath: "",
            IsIsp: false,
            IsAdapter: false,
            IsLoader: true);

        var info = new MscDeviceInfo(
            "\\\\?\\GLOBALROOT\\Device\\FakeLoader", 0, 0, "BuildWin Video050Loader",
            VendorId: "BuildWin", ProductId: "Video050Loader", IsTarget: true, MatchedEntry: entry);

        var transport = new SimulatedMscTransport(new SimulatedLoaderDevice());
        var logs = new List<string>();
        IFlashProtocol protocol = ProtocolFactory.CreateForDevice(transport, info, logs.Add);

        Assert.IsType<LoaderRomProtocol>(protocol);
        Assert.Contains(logs, l => l.Contains("类别 AX326X"));
        Assert.Contains(logs, l => l.Contains("驱动: ThunderSE.elf"));
    }

    [Fact]
    public void CreateForDevice_WithIspEntry_NonLoader_UsesAppChannel()
    {
        // AXISP 条目（IsIsp=true、产品串不含 "loader"）：类型为 Isp → 非 Loader →
        // 应用态 0xCD 备选通道（真实流程经 DeviceConnection 下发 0xDA 进入升级模式）。
        DeviceEntry ispEntry = new(
            Index: 3,
            InquiryInfo: "BuildwinMedia-Player    1.00",
            ClassInfo: "AXISP",
            SpiDriverPath: "",
            FuncListPath: "",
            IsIsp: true,
            IsAdapter: false,
            IsLoader: false);

        var info = new MscDeviceInfo(
            "\\\\?\\GLOBALROOT\\Device\\FakeIsp", 0, 0, "Buildwin Media-Player",
            VendorId: "Buildwin", ProductId: "Media-Player", IsTarget: true, MatchedEntry: ispEntry);

        var transport = new SimulatedMscTransport(new SimulatedMscDevice());
        IFlashProtocol protocol = ProtocolFactory.CreateForDevice(transport, info);

        Assert.Equal(DeviceKind.Isp, ispEntry.Kind);
        Assert.IsType<Dc503RomProtocol>(protocol);
    }

    [Fact]
    public void CreateForDevice_WithAdapterEntry_NonLoader_UsesAppChannel()
    {
        // AX2005Adapter 条目（IsAdapter=true、产品串不含 "loader"）：类型为 Adapter → 非 Loader →
        // 应用态 0xCD 备选通道（真实流程经 DeviceConnection 下发 0xDA 升级）。
        DeviceEntry adapterEntry = new(
            Index: 7,
            InquiryInfo: "BuildwinUSBoot Protocol 1.00",
            ClassInfo: "AX2005Adapter",
            SpiDriverPath: "AXIDEsdspi.elf",
            FuncListPath: "",
            IsIsp: false,
            IsAdapter: true,
            IsLoader: false);

        var info = new MscDeviceInfo(
            "\\\\?\\GLOBALROOT\\Device\\FakeAdapter", 0, 0, "Buildwin USBoot",
            VendorId: "Buildwin", ProductId: "USBoot Protocol", IsTarget: true, MatchedEntry: adapterEntry);

        var transport = new SimulatedMscTransport(new SimulatedMscDevice());
        IFlashProtocol protocol = ProtocolFactory.CreateForDevice(transport, info);

        Assert.Equal(DeviceKind.Adapter, adapterEntry.Kind);
        Assert.IsType<Dc503RomProtocol>(protocol);
    }
}
