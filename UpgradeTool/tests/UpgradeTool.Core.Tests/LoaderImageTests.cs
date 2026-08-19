using System.Text;
using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Tests;

public class LoaderImageTests
{
    private static byte[] LoadEmbedded(string resourceName)
    {
        var asm = typeof(LoaderImageTests).Assembly;
        using Stream? stream = asm.GetManifestResourceStream($"UpgradeTool.Core.Tests.Resources.{resourceName}")
            ?? throw new InvalidOperationException($"缺少内嵌资源 {resourceName}。");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    // ---------- 合成 ELF ----------

    [Fact]
    public void Parse_SyntheticRelocatableElf_ClassifiesSymbolsCorrectly()
    {
        byte[] elf = BuildElf(
            eType: 2,
            segment: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 },
            segmentVaddr: 0,
            symbols: new (string Name, uint Value, ushort Shndx)[]
            {
                ("RBC_mem_rwex_CPU", 0x00100008, LoaderImage.ShnAbs),
                ("l1_func_spi_init", 0x24, 1),
            });

        LoaderImage image = LoaderImage.Parse(elf);

        Assert.True(image.Relocatable);
        Assert.Equal(0u, image.SegmentVaddr);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, image.Segment);

        Assert.Single(image.AbsoluteSymbols);
        Assert.Equal(0x00100008u, image.AbsoluteSymbols["RBC_mem_rwex_CPU"]);

        Assert.Single(image.SegmentSymbols);
        Assert.Equal(0x24u, image.SegmentSymbols["l1_func_spi_init"]);

        // ABS 符号不受上传基址影响；段内符号要加上传基址
        Assert.Equal(0x00100008u, image.Resolve("RBC_mem_rwex_CPU", 0x020ccec0));
        Assert.Equal(0x020ccee4u, image.Resolve("l1_func_spi_init", 0x020ccec0));
    }

    [Fact]
    public void Parse_SyntheticFixedLinkedElf_ResolvesWithoutLoadBase()
    {
        byte[] elf = BuildElf(
            eType: 1,
            segment: new byte[] { 1, 2, 3, 4 },
            segmentVaddr: 0x020ccec0,
            symbols: new (string Name, uint Value, ushort Shndx)[]
            {
                ("l1_func_spi_init", 0x020ccee4, 1),
            });

        LoaderImage image = LoaderImage.Parse(elf);

        Assert.False(image.Relocatable);
        Assert.Equal(0x020ccec0u, image.SegmentVaddr);
        // 固定链接镜像：段内符号值已是绝对地址，loadBase 不影响结果
        Assert.Equal(0x020ccee4u, image.Resolve("l1_func_spi_init", 0));
    }

    [Fact]
    public void Parse_MissingSymbol_Throws()
    {
        byte[] elf = BuildElf(
            eType: 2, segment: new byte[] { 1, 2, 3, 4 }, segmentVaddr: 0,
            symbols: new (string, uint, ushort)[] { ("only_symbol", 0x24, 1) });

        LoaderImage image = LoaderImage.Parse(elf);
        Assert.False(image.TryResolve("missing", 0, out _));
        Assert.Throws<InvalidOperationException>(() => image.Resolve("missing", 0));
    }

    // ---------- 真实 ThunderSE.elf ----------

    [Fact]
    public void Parse_ThunderSeElf_RbcApiTableAndEntryOffsets()
    {
        byte[] elf = LoadEmbedded("ThunderSE.elf");
        LoaderImage image = LoaderImage.Parse(elf);

        Assert.True(image.Relocatable);
        Assert.Equal(0u, image.SegmentVaddr);
        Assert.Equal(0xf00u, (uint)image.Segment.Length);

        // Loader RAM API 表（SHN_ABS 绝对地址，不随上传位置变化）
        Assert.Equal(0x00100008u, image.AbsoluteSymbols["RBC_mem_rwex_CPU"]);
        Assert.Equal(0x00100008u, image.AbsoluteSymbols["RBC_mem_rwex_DMA"]);
        Assert.Equal(0x004a00u, image.AbsoluteSymbols["RBC_mem_rwex_buf"]);
        Assert.Equal(0x00100018u, image.AbsoluteSymbols["RBC_process_res"]);
        Assert.Equal(0x00100020u, image.AbsoluteSymbols["RBC_FIFO2mem"]);
        Assert.Equal(0x00100028u, image.AbsoluteSymbols["RBC_mem2FIFO"]);
        Assert.Equal(0x00100030u, image.AbsoluteSymbols["RBC_receive_data"]);
        Assert.Equal(0x00100038u, image.AbsoluteSymbols["RBC_send_data"]);
        Assert.Equal(0x00100040u, image.AbsoluteSymbols["RBC_Set_Chksum2"]);
        Assert.Equal(0x00100048u, image.AbsoluteSymbols["sdram_init"]);
        Assert.Equal(0x00100060u, image.AbsoluteSymbols["encrypt_open"]);
        Assert.Equal(0x00100068u, image.AbsoluteSymbols["encrypt_close"]);

        // 段内相对入口（上传到 SDRAM 0x020ccec0 后加基址）
        Assert.Equal(0x24u, image.SegmentSymbols["l1_func_spi_init"]);
        Assert.Equal(0x74u, image.SegmentSymbols["l1_func_signal_drive"]);
        Assert.Equal(0x208u, image.SegmentSymbols["l2_func_spi_page_program"]);
        Assert.Equal(0x3a0u, image.SegmentSymbols["l2_func_reset"]);
        Assert.Equal(0xad8u, image.SegmentSymbols["spi_sf_send_addr"]);
        Assert.Equal(0xb34u, image.SegmentSymbols["spi_sf_write_enable"]);
        Assert.Equal(0xb90u, image.SegmentSymbols["spi_sf_check_status"]);
        Assert.Equal(0xc20u, image.SegmentSymbols["spi_sf_read_id"]);

        // 入口换算：段内偏移 + 上传基址
        Assert.Equal(0x020ccee4u, image.Resolve("l1_func_spi_init", 0x020ccec0));
        Assert.Equal(0x020cd0c8u, image.Resolve("l2_func_spi_page_program", 0x020ccec0));
        // ABS 不受基址影响
        Assert.Equal(0x00100020u, image.Resolve("RBC_FIFO2mem", 0x020ccec0));
    }

    // ---------- DeviceLib.ini 各驱动 ELF（对齐 MPTool setting\*.elf，从符号表解析地址） ----------

    [Theory]
    [InlineData("AX326X.elf", 0x00040000u, 0x00010008u, 0x00041A00u, 0x00040028u, 0x000400B0u, 0x00040264u)]
    [InlineData("AX3233.elf", 0x0000B1A0u, 0x00113858u, 0xFFFFFFFFu, 0x0000B1E4u, 0x0000B250u, 0x0000B3E4u)]
    [InlineData("AX3233_A2.elf", 0x0000B1E4u, 0x00113ABCu, 0xFFFFFFFFu, 0x0000B21Cu, 0x0000B288u, 0x0000B41Cu)]
    [InlineData("AX3233_mpw.elf", 0x00118000u, 0x00120020u, 0xFFFFFFFFu, 0x00118024u, 0x00118044u, 0x001181D4u)]
    [InlineData("AX327X.elf", 0x00000000u, 0x00100008u, 0x00001A00u, 0x00000024u, 0x00000088u, 0x00000220u)]
    [InlineData("ThunderBD.elf", 0x00000000u, 0x00100008u, 0x0000B200u, 0x00000024u, 0x00000074u, 0x00000208u)]
    [InlineData("ThunderBDPlus.elf", 0x00000000u, 0x00100008u, 0x00015200u, 0x00000024u, 0x00000074u, 0x00000208u)]
    public void Parse_DeviceLibDriverElf_SymbolsFromSymbolTable(
        string fileName, uint vaddr, uint rbcMemRwex, uint rbcMemRwexBuf, uint l1SpiInit, uint l1SignalDrive, uint l2PageProgram)
    {
        LoaderImage image = LoaderImage.LoadEmbedded(fileName);

        Assert.Equal(vaddr, image.SegmentVaddr);
        Assert.Equal(rbcMemRwex, image.Resolve("RBC_mem_rwex_DMA", 0));
        Assert.Equal(rbcMemRwexBuf, image.Resolve("RBC_mem_rwex_buf", 0));
        Assert.Equal(l1SpiInit, image.Resolve("l1_func_spi_init", 0));
        Assert.Equal(l1SignalDrive, image.Resolve("l1_func_signal_drive", 0));
        Assert.Equal(l2PageProgram, image.Resolve("l2_func_spi_page_program", 0));
    }

    [Fact]
    public void LoadEmbedded_UnknownFile_Throws()
        => Assert.Throws<InvalidOperationException>(() => LoaderImage.LoadEmbedded("NotEmbedded.elf"));

    [Fact]
    public void LoadEmbedded_NonElfName_Throws()
        => Assert.Throws<ArgumentException>(() => LoaderImage.LoadEmbedded("order.ini"));

    // ---------- 错误输入 ----------

    [Fact]
    public void Parse_NotElf_Throws() =>
        Assert.Throws<InvalidDataException>(() => LoaderImage.Parse(new byte[] { 0x11, 0x22, 0x33 }));

    [Fact]
    public void Parse_TooShort_Throws() =>
        Assert.Throws<InvalidDataException>(() => LoaderImage.Parse(new byte[16]));

    [Fact]
    public void Parse_MultipleLoadSegments_Throws()
    {
        byte[] elf = BuildElf(
            eType: 2, segment: new byte[] { 1, 2, 3, 4 }, segmentVaddr: 0,
            symbols: new (string, uint, ushort)[] { ("sym", 0x24, 1) },
            loadPhdrCount: 2);
        Assert.Throws<InvalidDataException>(() => LoaderImage.Parse(elf));
    }

    [Fact]
    public void Parse_BssSegment_Throws()
    {
        byte[] elf = BuildElf(
            eType: 2, segment: new byte[] { 1, 2, 3, 4 }, segmentVaddr: 0,
            symbols: new (string, uint, ushort)[] { ("sym", 0x24, 1) },
            bss: true);
        Assert.Throws<InvalidDataException>(() => LoaderImage.Parse(elf));
    }

    [Fact]
    public void Parse_MissingSymbolTable_Throws()
    {
        byte[] elf = BuildElf(
            eType: 2, segment: new byte[] { 1, 2, 3, 4 }, segmentVaddr: 0,
            symbols: new (string, uint, ushort)[] { ("sym", 0x24, 1) },
            omitSymtab: true);
        Assert.Throws<InvalidDataException>(() => LoaderImage.Parse(elf));
    }

    [Fact]
    public void Parse_EmptySymbolTable_Throws()
    {
        byte[] elf = BuildElf(
            eType: 2, segment: new byte[] { 1, 2, 3, 4 }, segmentVaddr: 0,
            symbols: Array.Empty<(string, uint, ushort)>());
        Assert.Throws<InvalidDataException>(() => LoaderImage.Parse(elf));
    }

    // ---------- 合成 ELF32 构造器 ----------

    private static byte[] BuildElf(
        ushort eType, byte[] segment, uint segmentVaddr,
        (string Name, uint Value, ushort Shndx)[] symbols,
        int loadPhdrCount = 1, bool bss = false, bool omitSymtab = false)
    {
        const int ehsize = 52, phentsize = 32, shentsize = 40, symEntSize = 16;

        // 字符串表：先 1 个空字节，再依次存放符号名
        var strtab = new List<byte> { 0 };
        var nameOffsets = new List<uint>();
        foreach ((string name, _, _) in symbols)
        {
            nameOffsets.Add((uint)strtab.Count);
            strtab.AddRange(Encoding.ASCII.GetBytes(name));
            strtab.Add(0);
        }

        int phOff = ehsize;
        int segOff = phOff + phentsize * loadPhdrCount;
        int symtabOff = segOff + segment.Length;
        int symtabSize = symEntSize * (1 + symbols.Length); // 第 0 项为空符号
        int strtabOff = symtabOff + symtabSize;
        int shOff = strtabOff + strtab.Count;
        int shnum = omitSymtab ? 3 : 4;

        var elf = new byte[shOff + shnum * shentsize];
        // 头
        elf[0] = 0x7f; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
        elf[4] = 1; // 32 位
        elf[5] = 1; // 小端
        elf[6] = 1; // 版本
        WriteU16(elf, 16, eType);
        WriteU16(elf, 18, 92); // OpenRISC
        WriteU32(elf, 20, 1);
        WriteU32(elf, 28, (uint)phOff);
        WriteU32(elf, 32, (uint)shOff);
        WriteU16(elf, 40, ehsize);
        WriteU16(elf, 42, phentsize);
        WriteU16(elf, 44, (ushort)loadPhdrCount);
        WriteU16(elf, 46, shentsize);
        WriteU16(elf, 48, (ushort)shnum);

        // 程序头（PT_LOAD）
        for (int i = 0; i < loadPhdrCount; i++)
        {
            int off = phOff + i * phentsize;
            WriteU32(elf, off, 1); // PT_LOAD
            WriteU32(elf, off + 4, (uint)segOff);
            WriteU32(elf, off + 8, segmentVaddr);
            WriteU32(elf, off + 12, segmentVaddr);
            WriteU32(elf, off + 16, (uint)segment.Length);
            WriteU32(elf, off + 20, bss ? (uint)segment.Length + 0x100u : (uint)segment.Length);
            WriteU32(elf, off + 24, 5);
            WriteU32(elf, off + 28, 4);
        }

        // 段数据
        Array.Copy(segment, 0, elf, segOff, segment.Length);

        // 符号表（第 0 项空）
        for (int i = 0; i <= symbols.Length; i++)
        {
            int off = symtabOff + i * symEntSize;
            if (i == 0)
                continue;
            (string name, uint value, ushort shndx) = symbols[i - 1];
            WriteU32(elf, off, nameOffsets[i - 1]);
            WriteU32(elf, off + 4, value);
            WriteU32(elf, off + 8, 0); // st_size
            WriteU16(elf, off + 14, shndx);
        }

        // 字符串表
        Array.Copy(strtab.ToArray(), 0, elf, strtabOff, strtab.Count);

        // 节头
        void WriteSection(int index, uint shType, uint offset, uint size, uint link = 0, uint entsize = 0)
        {
            int off = shOff + index * shentsize;
            WriteU32(elf, off, 0); // sh_name（非必需）
            WriteU32(elf, off + 4, shType);
            WriteU32(elf, off + 8, 0); // flags
            WriteU32(elf, off + 12, 0); // addr
            WriteU32(elf, off + 16, offset);
            WriteU32(elf, off + 20, size);
            WriteU32(elf, off + 24, link);
            WriteU32(elf, off + 28, 0);
            WriteU32(elf, off + 32, 1);
            WriteU32(elf, off + 36, entsize);
        }

        WriteSection(0, 0, 0, 0); // NULL
        WriteSection(1, 1, (uint)segOff, (uint)segment.Length); // .text
        if (!omitSymtab)
        {
            WriteSection(2, 2, (uint)symtabOff, (uint)symtabSize, link: 3, entsize: symEntSize); // .symtab
            WriteSection(3, 3, (uint)strtabOff, (uint)strtab.Count); // .strtab
        }

        return elf;
    }

    private static void WriteU16(byte[] b, int off, ushort v)
    {
        b[off] = (byte)v;
        b[off + 1] = (byte)(v >> 8);
    }

    private static void WriteU32(byte[] b, int off, uint v)
    {
        b[off] = (byte)v;
        b[off + 1] = (byte)(v >> 8);
        b[off + 2] = (byte)(v >> 16);
        b[off + 3] = (byte)(v >> 24);
    }
}
