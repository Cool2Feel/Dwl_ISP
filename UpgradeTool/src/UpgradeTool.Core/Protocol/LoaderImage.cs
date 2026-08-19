namespace UpgradeTool.Core.Protocol;

/// <summary>
/// 泛化解析 Loader 模式下载器驱动符号表的 ELF32（如 MPTool 的 ThunderSE.elf，OpenRISC）。
/// Loader 固件内置 SPI 驱动，本类用于把驱动导出符号换算成 Loader 固件内的绝对地址。
///
/// 符号分为两类，语义不同：
///   1) SHN_ABS 绝对符号（如 RBC_mem_rwex_CPU=0x00100008）——Loader RAM API 表，
///      不随基址变化，直接作为 0xCB 通道的 Func1/Func2 目标；
///   2) 段内相对符号（如 l1_func_spi_init=0x24）——固件内偏移，绝对地址 = 基址 + 偏移。
/// 基址由协议层决定（ThunderSE.elf PT_LOAD p_vaddr=0，即符号值即绝对地址），本类保持中立：
/// 只按符号语义返回绝对地址或偏移，由 Resolve(loadBase) 统一换算。
/// </summary>
public sealed class LoaderImage
{
    /// <summary>ELF SHN_ABS：符号值为绝对地址。</summary>
    public const ushort ShnAbs = 0xFFF1;

    /// <summary>PT_LOAD 段文件字节（上传内容）。</summary>
    public byte[] Segment { get; }

    /// <summary>PT_LOAD p_vaddr。ET_REL 通常为 0；ET_EXEC 即链接基址。</summary>
    public uint SegmentVaddr { get; }

    /// <summary>是否可重定位（ET_REL）。true 时段内符号值为偏移，需加上传基址；false 时符号值已是绝对地址。</summary>
    public bool Relocatable { get; }

    /// <summary>SHN_ABS 绝对符号（名称 → 绝对地址），即 Loader RAM API 表（RBC_* / sdram_init 等）。</summary>
    public IReadOnlyDictionary<string, uint> AbsoluteSymbols { get; }

    /// <summary>段内相对符号（名称 → 段内偏移），即镜像内的 L1/L2 入口（l1_func_spi_init 等）。</summary>
    public IReadOnlyDictionary<string, uint> SegmentSymbols { get; }

    private LoaderImage(
        byte[] segment,
        uint segmentVaddr,
        bool relocatable,
        IReadOnlyDictionary<string, uint> absoluteSymbols,
        IReadOnlyDictionary<string, uint> segmentSymbols)
    {
        Segment = segment;
        SegmentVaddr = segmentVaddr;
        Relocatable = relocatable;
        AbsoluteSymbols = absoluteSymbols;
        SegmentSymbols = segmentSymbols;
    }

    public static LoaderImage Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>从 UpgradeTool.Core 内嵌资源读取默认 Loader 驱动（EmbeddedResource: Resources\ThunderSE.elf）。</summary>
    public static LoaderImage LoadEmbedded()
        => LoadEmbedded("ThunderSE.elf");

    /// <summary>
    /// 从 UpgradeTool.Core 内嵌资源读取指定驱动 ELF（对齐 MPTool DeviceLib.ini 的 SpiDriverPath，
    /// 如 AX326X.elf / ThunderSE.elf / ThunderBD.elf / ThunderBDPlus.elf）。
    /// 驱动/固件函数地址（RBC_mem_rwex_DMA / RBC_mem_rwex_buf / l1_* / l2_*）从此 ELF 符号表解析。
    /// </summary>
    public static LoaderImage LoadEmbedded(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"非法的驱动 ELF 文件名: {fileName ?? "(null)"}", nameof(fileName));

        var asm = typeof(LoaderImage).Assembly;
        using Stream? stream = asm.GetManifestResourceStream($"UpgradeTool.Core.Resources.{fileName}")
            ?? throw new InvalidOperationException($"缺少内嵌资源 {fileName}（EmbeddedResource 未配置或驱动未纳入仓库）。");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    public static LoaderImage Parse(byte[] elf)
    {
        if (elf.Length < 52)
            throw new InvalidDataException("Loader ELF 文件过短。");
        if (elf[0] != 0x7f || elf[1] != 'E' || elf[2] != 'L' || elf[3] != 'F')
            throw new InvalidDataException("Loader 文件不是 ELF 格式。");
        byte eiClass = elf[4];
        byte eiData = elf[5];
        if (eiClass != 1 || eiData != 1)
            throw new InvalidDataException($"仅支持 32 位小端 ELF（当前 class={eiClass}, data={eiData}）。");

        ushort eType = ReadU16(elf, 16);
        if (eType is not (1 or 2)) // ET_EXEC / ET_REL
            throw new InvalidDataException($"不支持的 ELF 类型 {eType}（期望 ET_EXEC 或 ET_REL）。");
        bool relocatable = eType == 2;

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
                throw new InvalidDataException("Loader ELF 应为单个非空 PT_LOAD 段。");
            if (pFilesz != pMemsz)
                throw new InvalidDataException($"Loader ELF PT_LOAD 存在未初始化 bss（FileSiz=0x{pFilesz:X} MemSiz=0x{pMemsz:X}）。");
            loadVaddr = pVaddr;
            loadOffset = pOffset;
            loadSize = pFilesz;
        }

        if (loadVaddr == null || loadOffset == null || loadSize == null)
            throw new InvalidDataException("Loader ELF 缺少 PT_LOAD 段。");

        if (loadOffset.Value > (uint)elf.Length || loadSize.Value > (uint)elf.Length - loadOffset.Value)
            throw new InvalidDataException("Loader ELF PT_LOAD 段超出文件长度。");
        byte[] segment = new byte[loadSize.Value];
        Array.Copy(elf, loadOffset.Value, segment, 0, loadSize.Value);

        // ---- 符号表 ----
        uint? symOff = null;
        uint? symSize = null;
        uint? strOff = null;
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
                }
            }
        }

        if (symOff == null || strOff == null)
            throw new InvalidDataException("Loader ELF 缺少符号表。");

        var abs = new Dictionary<string, uint>(StringComparer.Ordinal);
        var seg = new Dictionary<string, uint>(StringComparer.Ordinal);
        const int symEntSize = 16;
        for (uint off = symOff.Value; off + symEntSize <= symOff.Value + symSize!.Value; off += symEntSize)
        {
            uint nameOff = ReadU32(elf, off);
            uint value = ReadU32(elf, off + 4);
            ushort shndx = ReadU16(elf, off + 14);
            if (nameOff == 0 || shndx == 0) // 无名符号 / SHN_UNDEF
                continue;
            string name = ReadString(elf, strOff!.Value, nameOff);
            if (shndx == ShnAbs)
                abs[name] = value;
            else if (value != 0)
                seg[name] = value;
        }

        if (abs.Count == 0 && seg.Count == 0)
            throw new InvalidDataException("Loader ELF 符号表为空。");

        return new LoaderImage(segment, loadVaddr.Value, relocatable, abs, seg);
    }

    /// <summary>
    /// 解析符号的绝对地址：SHN_ABS 符号直接返回值；段内符号在可重定位镜像中加上传基址，
    /// 在固定链接镜像中符号值已是绝对地址。
    /// </summary>
    public bool TryResolve(string name, uint loadBase, out uint address)
    {
        if (AbsoluteSymbols.TryGetValue(name, out uint abs))
        {
            address = abs;
            return true;
        }
        if (SegmentSymbols.TryGetValue(name, out uint offset))
        {
            address = Relocatable ? loadBase + offset : offset;
            return true;
        }
        address = 0;
        return false;
    }

    public uint Resolve(string name, uint loadBase) =>
        TryResolve(name, loadBase, out uint address)
            ? address
            : throw new InvalidOperationException($"Loader ELF 缺少符号 {name}。");

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
