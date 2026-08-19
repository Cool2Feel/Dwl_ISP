namespace UpgradeTool.Core.Protocol;

/// <summary>
/// 解析 AX329X SPI0 flash stub 的 ELF32（自建 stub_flash.elf）。
/// 只关心两类信息：
///   1. 单 PT_LOAD 段的文件字节与加载基址（上传到 SDRAM 0x020ccec0）。
///   2. 符号表中 L1/L2 入口的绝对地址（l1_func_spi_init 等）。
/// </summary>
public sealed class StubImage
{
    public uint LoadBase { get; }
    public byte[] Segment { get; }

    /// <summary>L1: SPI0 初始化（无数据）。</summary>
    public uint L1SpiInit { get; }

    /// <summary>L1: SPI 信号驱动（WE/RDSR/RDID/SE/BE/CE/READ）。</summary>
    public uint L1SignalDrive { get; }

    /// <summary>L2: 页编程（数据输出阶段）。</summary>
    public uint L2PageProgram { get; }

    /// <summary>L2: 读 flash（数据输入阶段）。</summary>
    public uint L2Read { get; }

    /// <summary>L2: 读 flash 厂商 ID（RDID，数据输入阶段）。</summary>
    public uint L2ReadId { get; }

    /// <summary>L2: 读 flash 状态寄存器（RDSR，数据输入阶段）。</summary>
    public uint L2ReadStatus { get; }

    /// <summary>L2: 读 flash 容量（RDID 密度解码，4 字节 LE，数据输入阶段）。</summary>
    public uint L2ReadCapacity { get; }

    private StubImage(uint loadBase, byte[] segment, uint l1SpiInit, uint l1SignalDrive, uint l2PageProgram, uint l2Read, uint l2ReadId, uint l2ReadStatus, uint l2ReadCapacity)
    {
        LoadBase = loadBase;
        Segment = segment;
        L1SpiInit = l1SpiInit;
        L1SignalDrive = l1SignalDrive;
        L2PageProgram = l2PageProgram;
        L2Read = l2Read;
        L2ReadId = l2ReadId;
        L2ReadStatus = l2ReadStatus;
        L2ReadCapacity = l2ReadCapacity;
    }

    public static StubImage Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>从 UpgradeTool.Core 内嵌资源读取 stub ELF（EmbeddedResource: Resources\stub_flash.elf）。</summary>
    public static StubImage LoadEmbedded()
    {
        var asm = typeof(StubImage).Assembly;
        using Stream? stream = asm.GetManifestResourceStream("UpgradeTool.Core.Resources.stub_flash.elf")
            ?? throw new InvalidOperationException("缺少内嵌资源 stub_flash.elf（EmbeddedResource 未配置或 stub 未编译）。");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    public static StubImage Parse(byte[] elf)
    {
        if (elf.Length < 52)
            throw new InvalidDataException("stub ELF 文件过短。");
        if (elf[0] != 0x7f || elf[1] != 'E' || elf[2] != 'L' || elf[3] != 'F')
            throw new InvalidDataException("stub 文件不是 ELF 格式。");
        byte eiClass = elf[4];
        byte eiData = elf[5];
        if (eiClass != 1 || eiData != 1)
            throw new InvalidDataException($"仅支持 32 位小端 ELF（当前 class={eiClass}, data={eiData}）。");

        uint phOff = ReadU32(elf, 28);
        ushort phentSize = ReadU16(elf, 42);
        ushort phNum = ReadU16(elf, 44);
        uint shOff = ReadU32(elf, 32);
        ushort shentSize = ReadU16(elf, 46);
        ushort shNum = ReadU16(elf, 48);

        // ---- PT_LOAD 段 ----
        uint? loadVaddr = null;
        uint? loadOffset = null;
        uint? loadSize = null;
        for (int i = 0; i < phNum; i++)
        {
            uint off = phOff + (uint)(i * phentSize);
            uint type = ReadU32(elf, off);
            if (type != 1) // PT_LOAD
                continue;
            uint pOffset = ReadU32(elf, off + 4);
            uint pVaddr = ReadU32(elf, off + 8);
            uint pFilesz = ReadU32(elf, off + 16);
            uint pMemsz = ReadU32(elf, off + 20);
            if (pFilesz == 0 || loadOffset != null)
                throw new InvalidDataException("stub ELF 应为单个非空 PT_LOAD 段。");
            if (pFilesz != pMemsz)
                throw new InvalidDataException($"stub ELF PT_LOAD 存在未初始化 bss（FileSiz=0x{pFilesz:X} MemSiz=0x{pMemsz:X}）。");
            loadVaddr = pVaddr;
            loadOffset = pOffset;
            loadSize = pFilesz;
        }

        if (loadVaddr == null || loadOffset == null || loadSize == null)
            throw new InvalidDataException("stub ELF 缺少 PT_LOAD 段。");

        if (loadOffset.Value > (uint)elf.Length || loadSize.Value > (uint)elf.Length - loadOffset.Value)
            throw new InvalidDataException("stub ELF PT_LOAD 段超出文件长度。");
        byte[] segment = new byte[loadSize.Value];
        Array.Copy(elf, loadOffset.Value, segment, 0, loadSize.Value);

        // ---- 符号表 ----
        uint? symOff = null;
        uint? symSize = null;
        uint? strOff = null;
        uint? strSize = null;
        for (int i = 0; i < shNum; i++)
        {
            uint off = shOff + (uint)(i * shentSize);
            uint type = ReadU32(elf, off + 4);
            uint size = ReadU32(elf, off + 20);
            uint link = ReadU32(elf, off + 24);
            if (type == 2) // SHT_SYMTAB
            {
                symOff = ReadU32(elf, off + 16);
                symSize = size;
                // sh_link 指向符号名字符串表节
                if (link < shNum)
                {
                    uint strShOff = shOff + link * shentSize;
                    strOff = ReadU32(elf, strShOff + 16);
                    strSize = ReadU32(elf, strShOff + 20);
                }
            }
        }

        if (symOff == null || strOff == null)
            throw new InvalidDataException("stub ELF 缺少符号表。");

        uint l1SpiInit = 0, l1SignalDrive = 0, l2PageProgram = 0, l2Read = 0, l2ReadId = 0, l2ReadStatus = 0, l2ReadCapacity = 0;
        const int symEntSize = 16;
        for (uint off = symOff.Value; off + symEntSize <= symOff.Value + symSize!.Value; off += symEntSize)
        {
            uint nameOff = ReadU32(elf, off);
            uint value = ReadU32(elf, off + 4);
            if (value == 0)
                continue;
            string name = ReadString(elf, strOff!.Value, nameOff);
            switch (name)
            {
                case "l1_func_spi_init": l1SpiInit = value; break;
                case "l1_func_signal_drive": l1SignalDrive = value; break;
                case "l2_func_spi_page_program": l2PageProgram = value; break;
                case "l2_func_spi_read": l2Read = value; break;
                case "l2_func_spi_read_id": l2ReadId = value; break;
                case "l2_func_spi_read_status": l2ReadStatus = value; break;
                case "l2_func_spi_read_capacity": l2ReadCapacity = value; break;
            }
        }

        if (l1SpiInit == 0 || l1SignalDrive == 0 || l2PageProgram == 0 || l2Read == 0 || l2ReadId == 0 || l2ReadStatus == 0 || l2ReadCapacity == 0)
            throw new InvalidDataException("stub ELF 缺少所需的入口符号（l1_func_spi_init / l2_func_spi_read_capacity 等）。");

        return new StubImage(loadVaddr.Value, segment, l1SpiInit, l1SignalDrive, l2PageProgram, l2Read, l2ReadId, l2ReadStatus, l2ReadCapacity);
    }

    private static ushort ReadU16(byte[] b, uint off)
    {
        if (off + 2 > (uint)b.Length)
            throw new InvalidDataException($"ELF 读取越界: 偏移 0x{off:X} 超出文件长度 0x{b.Length:X}。");
        return (ushort)(b[off] | (b[off + 1] << 8));
    }

    private static uint ReadU32(byte[] b, uint off)
    {
        if (off + 4 > (uint)b.Length)
            throw new InvalidDataException($"ELF 读取越界: 偏移 0x{off:X} 超出文件长度 0x{b.Length:X}。");
        return (uint)b[off] | ((uint)b[off + 1] << 8) | ((uint)b[off + 2] << 16) | ((uint)b[off + 3] << 24);
    }

    private static string ReadString(byte[] b, uint baseOff, uint off)
    {
        int start = (int)(baseOff + off);
        if (start < 0 || start >= b.Length)
            return string.Empty;
        int end = start;
        while (end < b.Length && b[end] != 0)
            end++;
        return System.Text.Encoding.ASCII.GetString(b, start, end - start);
    }
}
