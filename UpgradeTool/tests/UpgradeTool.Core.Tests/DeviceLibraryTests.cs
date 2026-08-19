using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Tests;

/// <summary>
/// DeviceLib.ini 解析与设备匹配测试（对齐 MPTool 设备库配置：INQUIRY 产品串 → 驱动 ELF 文件名）。
/// 直接从 UpgradeTool.Core 内嵌资源读取 DeviceLib.ini。
/// </summary>
public class DeviceLibraryTests
{
    private static DeviceLibrary LoadEmbedded() => DeviceLibrary.LoadEmbedded();

    [Fact]
    public void Parse_EmbeddedDeviceLib_LoadsAllEntries()
    {
        DeviceLibrary lib = LoadEmbedded();

        Assert.Equal(12, lib.ItemSum);
        Assert.Equal(12, lib.Entries.Count);
    }

    [Fact]
    public void Embedded_Cached_ReturnsSameInstance()
    {
        // 识别走缓存实例：首次访问后不再重新解析 INI（线程安全懒加载），
        // 保证同一进程内多次识别结论一致，避免解析瞬时失败造成识别抖动。
        Assert.Same(DeviceLibrary.Embedded, DeviceLibrary.Embedded);
    }

    [Fact]
    public void Parse_EntryFields_Extracted()
    {
        DeviceLibrary lib = LoadEmbedded();

        DeviceEntry se050 = lib.Entries.First(e => e.Index == 10);
        Assert.Equal("BuildWinVideo050Loader  1.00", se050.InquiryInfo);
        Assert.Equal("AX326X", se050.ClassInfo);
        Assert.Equal("ThunderSE.elf", se050.SpiDriverPath);
        Assert.True(se050.IsLoader);
        Assert.True(se050.HasSpiDriver);
    }

    [Fact]
    public void Kind_ResolvesPerClassInfo()
    {
        // 对齐 MPTool SearchDev 按 ClassInfo 派发处理类：每种设备类型解析为对应 DeviceKind
        DeviceLibrary lib = LoadEmbedded();
        DeviceEntry Entry(int idx) => lib.Entries.First(e => e.Index == idx);

        Assert.Equal(DeviceKind.DirectSpi, Entry(1).Kind);   // [1] AX326X（应用态直连 SPI）
        Assert.Equal(DeviceKind.Isp, Entry(3).Kind);         // [3] AXISP
        Assert.Equal(DeviceKind.LegacyRp, Entry(5).Kind);    // [5] AX3233RP
        Assert.Equal(DeviceKind.LegacyRp, Entry(6).Kind);    // [6] AX3233RP
        Assert.Equal(DeviceKind.Adapter, Entry(7).Kind);     // [7] AX2005Adapter
        Assert.Equal(DeviceKind.Loader, Entry(8).Kind);      // [8] Video030Loader
        Assert.Equal(DeviceKind.Loader, Entry(12).Kind);     // [12] Video070Loader
        Assert.Equal(DeviceKind.Unknown, Entry(4).Kind);     // [4] 无 ClassInfo
    }

    [Fact]
    public void Kind_AllLoaderEntries_ResolveAsLoader()
    {
        DeviceLibrary lib = LoadEmbedded();

        foreach (DeviceEntry e in lib.Entries.Where(e => e.IsLoader))
            Assert.Equal(DeviceKind.Loader, e.Kind);
    }

    [Fact]
    public void KindLabel_ProvidesReadableTypeText()
    {
        DeviceLibrary lib = LoadEmbedded();

        Assert.Equal("AXISP(ISP)", lib.Entries.First(e => e.Index == 3).KindLabel);
        Assert.Equal("AX2005Adapter(适配器)", lib.Entries.First(e => e.Index == 7).KindLabel);
        Assert.Equal("AX3233RP(量产)", lib.Entries.First(e => e.Index == 5).KindLabel);
    }

    [Fact]
    public void Match_ByLoaderProductString()
    {
        DeviceLibrary lib = LoadEmbedded();

        // 设备 INQUIRY 产品串（无版本）应命中对应 Loader 条目
        DeviceEntry? e050 = lib.Match("BuildWinVideo050Loader");
        Assert.NotNull(e050);
        Assert.Equal("ThunderSE.elf", e050!.SpiDriverPath);

        DeviceEntry? e060 = lib.Match("BuildWinVideo060Loader");
        Assert.Equal("ThunderBD.elf", e060!.SpiDriverPath);

        DeviceEntry? e070 = lib.Match("BuildWinVideo070Loader");
        Assert.Equal("ThunderBDPlus.elf", e070!.SpiDriverPath);

        // 大小写不敏感
        Assert.Equal("ThunderSE.elf", lib.Match("buildwinvideo050loader")!.SpiDriverPath);
    }

    [Fact]
    public void Match_NonLoaderProduct()
    {
        DeviceLibrary lib = LoadEmbedded();

        // 应用态 / 通用存储设备（非 loader）也能按产品串匹配
        DeviceEntry? generic = lib.Match("Generic Mass-Storage");
        Assert.NotNull(generic);
        Assert.Equal("AX3233RP", generic!.ClassInfo);
        Assert.Equal("AX3233.elf", generic.SpiDriverPath);

        Assert.Null(lib.Match(null));
        Assert.Null(lib.Match("   "));
    }

    [Fact]
    public void MatchLoader_OnlyReturnsLoaderEntriesWithDriver()
    {
        DeviceLibrary lib = LoadEmbedded();

        // Loader 过滤：只命中含 "loader" 且有驱动文件的条目
        Assert.Equal("ThunderSE.elf", lib.MatchLoader("BuildWinVideo050Loader")!.SpiDriverPath);
        Assert.Equal("ThunderBD.elf", lib.MatchLoader("BuildWinVideo060Loader")!.SpiDriverPath);
        Assert.Equal("ThunderBDPlus.elf", lib.MatchLoader("BuildWinVideo070Loader")!.SpiDriverPath);

        // 非 loader 设备（Generic Mass-Storage）不在 0xCB 下载通道候选内
        Assert.Null(lib.MatchLoader("Generic Mass-Storage"));
        Assert.Null(lib.MatchLoader("BuildWinUSBoot Protocol"));
    }

    [Fact]
    public void MatchIdentity_ByConcatenatedInquiryFields()
    {
        DeviceLibrary lib = LoadEmbedded();

        // 应用态设备：Vendor+Product+Revision 三字段拼接（BuildwinMedia-Player 1.00，对齐固件 device_inquiry_data）
        DeviceEntry? app = lib.MatchIdentity("Buildwin", "Media-Player", "1.00");
        Assert.NotNull(app);
        Assert.Equal("AXISP", app!.ClassInfo);
        Assert.True(app.IsIsp);

        // Loader 设备：BuildWinVideo050Loader 1.00 -> ThunderSE.elf
        DeviceEntry? se050 = lib.MatchIdentity("BuildWin", "Video050Loader", "1.00");
        Assert.NotNull(se050);
        Assert.Equal("ThunderSE.elf", se050!.SpiDriverPath);
        Assert.True(se050.IsLoader);

        // 缺版本（Revision 缺失/未知）也能按 Vendor+Product 前缀命中
        Assert.Equal("ThunderSE.elf", lib.MatchIdentity("BuildWin", "Video050Loader")!.SpiDriverPath);
    }

    [Fact]
    public void MatchIdentity_RejectsNonDeviceIdentities()
    {
        DeviceLibrary lib = LoadEmbedded();

        Assert.Null(lib.MatchIdentity("SanDisk", "USB 3.2Gen1", "1.00"));
        Assert.Null(lib.MatchIdentity("Kingston", "DataTraveler", null));
        Assert.Null(lib.MatchIdentity(null, null, null));
        Assert.Null(lib.MatchIdentity("", "", ""));
    }
}
