using System.Text;
using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Tests;

public class FlashLibTests
{
    private const string SyntheticIni = """
        [COMMON]
        Loader-Version=BL999v9.9.9
        Firmware=Firmware\X.bin
        Address=0x800
        Read-ID-9F=0x01,0x9F
        Read-ID-AB=0x04,0xAB,0x00,0x00,0x00
        Read-ID-90=0x04,0x90,0x00,0x00,0x00
        Read-ID-15=0x01,0x15

        ;注释行应被忽略
        [1]
        Name=AutoAdd
        Capacity=0x200000
        Sector-Type=Simple
        Min-Sector-Size=0x1000
        Page-Size=0x100
        ID-9F=0xEF401500
        ID-9F-MASK=0xFFFFFFFF
        Write-Enable=0x06
        Write-Disable=0x04
        Read-Status-Register=0x05
        Write-Status-Register=0x01
        Read=0x03
        Fast-Read=0x0B
        Page-Program=0x02
        Erase-4K=0x20
        Erase-64K=0xD8
        Erase-Chip=0xC7

        [2]
        Name=AutoAdd
        Capacity=0x400000
        ID-9F=0xEF401600 ;行内注释应被剥离
        ID-9F-MASK=0xFFFFFFFF
        """;

    private static byte[] LoadEmbedded(string resourceName)
    {
        var asm = typeof(FlashLibTests).Assembly;
        using Stream? stream = asm.GetManifestResourceStream($"UpgradeTool.Core.Tests.Resources.{resourceName}")
            ?? throw new InvalidOperationException($"缺少内嵌资源 {resourceName}。");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    // ---------- 合成 INI ----------

    [Fact]
    public void Parse_SyntheticIni_ReadsCommonSection()
    {
        FlashLib lib = FlashLib.Parse(SyntheticIni);

        Assert.Equal("BL999v9.9.9", lib.LoaderVersion);
        Assert.Equal("Firmware\\X.bin", lib.Firmware);
        Assert.Equal(0x800u, lib.Address);

        Assert.Equal(4, lib.ReadIdMethods.Count);
        Assert.Equal("9F", lib.ReadIdMethods[0].Method);
        Assert.Equal(0x9Fu, lib.ReadIdMethods[0].Command);
        Assert.Equal(5, lib.ReadIdMethods[1].Sequence.Length); // 0x04 + 0xAB + 3 哑元
    }

    [Fact]
    public void Parse_SyntheticIni_ReadsDevicesAndOpcodes()
    {
        FlashLib lib = FlashLib.Parse(SyntheticIni);

        Assert.Equal(2, lib.Devices.Count);

        FlashDeviceSpec w25q16 = lib.Devices[0];
        Assert.Equal("AutoAdd", w25q16.Name);
        Assert.Equal(0x200000u, w25q16.Capacity);
        Assert.Equal("Simple", w25q16.SectorType);
        Assert.Equal(0x1000u, w25q16.MinSectorSize);
        Assert.Equal(0x100u, w25q16.PageSize);
        Assert.Equal(0x06u, w25q16.WriteEnable);
        Assert.Equal(0x04u, w25q16.WriteDisable);
        Assert.Equal(0x05u, w25q16.ReadStatusRegister);
        Assert.Equal(0x01u, w25q16.WriteStatusRegister);
        Assert.Equal(0x03u, w25q16.Read);
        Assert.Equal(0x0Bu, w25q16.FastRead);
        Assert.Equal(0x02u, w25q16.PageProgram);
        Assert.Equal(0x20u, w25q16.Erase4K);
        Assert.Equal(0xD8u, w25q16.Erase64K);
        Assert.Equal(0xC7u, w25q16.EraseChip);

        // 行内注释被剥离后的设备
        Assert.Equal(0x400000u, lib.Devices[1].Capacity);
    }

    [Fact]
    public void Match9F_FindsByJEdId()
    {
        FlashLib lib = FlashLib.Parse(SyntheticIni);

        FlashDeviceSpec? match = lib.Match9F(new byte[] { 0xEF, 0x40, 0x16 });
        Assert.NotNull(match);
        Assert.Equal(0x400000u, match.Capacity);
        Assert.Equal(lib.Devices[1], match);

        Assert.Same(lib.Devices[0], lib.Match9F(new byte[] { 0xEF, 0x40, 0x15 }));
        Assert.Null(lib.Match9F(new byte[] { 0xFF, 0xFF, 0xFF }));
        Assert.Null(lib.Match9F(new byte[] { 0xEF, 0x40, 0x17 }));
    }

    [Fact]
    public void Match_WithMask_AllowsPartialMatch()
    {
        const string ini = """
            [COMMON]
            [1]
            Capacity=0x400000
            ID-9F=0xEF400000
            ID-9F-MASK=0xFFFF0000
            """;
        FlashLib lib = FlashLib.Parse(ini);

        FlashDeviceSpec? match = lib.Match9F(new byte[] { 0xEF, 0x40, 0x99 });
        Assert.NotNull(match);
        Assert.Equal(0x400000u, match.Capacity);

        // 厂商字节不匹配则整体不匹配
        Assert.Null(lib.Match9F(new byte[] { 0x00, 0x40, 0x99 }));
    }

    [Fact]
    public void DeriveCapacity_FromJedecDensity_DerivesCapacity()
    {
        // 对齐 MPTool AutoAddFlashType：FlashLib 未匹配时按 JEDEC 密度字段推导容量。
        // W25Q32(EF 40 16)→4MB、W25Q64(EF 40 17)→8MB、W25Q128(EF 40 18)→16MB；
        // 4 字节响应 85 60 16 85（MPTool 真机 4MB flash）→4MB。
        Assert.Equal(0x400000u, FlashLib.DeriveCapacityFromRdid(new byte[] { 0xEF, 0x40, 0x16 }));
        Assert.Equal(0x800000u, FlashLib.DeriveCapacityFromRdid(new byte[] { 0xEF, 0x40, 0x17 }));
        Assert.Equal(0x1000000u, FlashLib.DeriveCapacityFromRdid(new byte[] { 0xEF, 0x40, 0x18 }));
        Assert.Equal(0x400000u, FlashLib.DeriveCapacityFromRdid(new byte[] { 0x85, 0x60, 0x16, 0x85 }));
    }

    [Fact]
    public void DeriveCapacity_InvalidOrUnusableId_ReturnsNull()
    {
        // 无效/不可用 ID（对齐 MPTool 放弃烧写情形）：1F FF FF（SPI 时钟异常）、
        // FF FF FF / 00 00 00（未响应）、响应过短、null，均无法推导容量。
        Assert.Null(FlashLib.DeriveCapacityFromRdid(new byte[] { 0x1F, 0xFF, 0xFF }));
        Assert.Null(FlashLib.DeriveCapacityFromRdid(new byte[] { 0xFF, 0xFF, 0xFF }));
        Assert.Null(FlashLib.DeriveCapacityFromRdid(new byte[] { 0x00, 0x00, 0x00 }));
        Assert.Null(FlashLib.DeriveCapacityFromRdid(new byte[] { 0xEF, 0x40 }));
        Assert.Null(FlashLib.DeriveCapacityFromRdid(null));
    }

    [Fact]
    public void Parse_NoCapacityKey_SkipsDevice()
    {
        const string ini = """
            [COMMON]
            [1]
            Name=OnlyName
            ID-9F=0xEF401600
            """;
        FlashLib lib = FlashLib.Parse(ini);
        Assert.Empty(lib.Devices);
    }

    // ---------- 真实 FlashLib.ini ----------

    [Fact]
    public void Parse_RealFlashLibIni_CommonSectionMatchesMptool()
    {
        string text = Encoding.Latin1.GetString(LoadEmbedded("FlashLib.ini"));
        FlashLib lib = FlashLib.Parse(text);

        Assert.Equal("BL206v1.0.0", lib.LoaderVersion);
        Assert.Equal("Firmware\\Spi_Lib.bin", lib.Firmware);
        Assert.Equal(0x800u, lib.Address);
        Assert.Equal(4, lib.ReadIdMethods.Count);
        Assert.Equal(20, lib.Devices.Count);
    }

    [Fact]
    public void Parse_RealFlashLibIni_W25Q32MatchesEntry12()
    {
        string text = Encoding.Latin1.GetString(LoadEmbedded("FlashLib.ini"));
        FlashLib lib = FlashLib.Parse(text);

        FlashDeviceSpec? w25q32 = lib.Match9F(new byte[] { 0xEF, 0x40, 0x16 });
        Assert.NotNull(w25q32);
        Assert.Equal(0x400000u, w25q32.Capacity);
        Assert.Equal(0x100u, w25q32.PageSize);
        Assert.Equal(0x06u, w25q32.WriteEnable);
        Assert.Equal(0x03u, w25q32.Read);
        Assert.Equal(0x02u, w25q32.PageProgram);
        Assert.Equal(0x20u, w25q32.Erase4K);
        Assert.Equal(0xD8u, w25q32.Erase64K);
        Assert.Equal(0xC7u, w25q32.EraseChip);

        // 与 Dc503RomProtocol 内置常量交叉校验
        Assert.Equal(256u, w25q32.PageSize);
        Assert.Equal(4096u, w25q32.MinSectorSize);
        Assert.Null(lib.Match9F(new byte[] { 0xFF, 0xFF, 0xFF }));
    }
}
