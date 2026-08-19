using System.Text;

namespace UpgradeTool.Core.Protocol;

/// <summary>
/// 宽松 ELF32（小端）驱动镜像解析器，用于适配器/子设备驱动（AXIDEsdspi.elf / AX3233AXIDE_A2.elf）。
/// 与 LoaderImage/StubImage 的严格约束（单个非空 PT_LOAD、FileSiz==MemSiz）不同，这些驱动 ELF
/// 可能有多个 PT_LOAD 段或带 bss（FileSiz &lt; MemSiz）。本解析器只做两件事（对齐 MPTool AnalyzeElf）：
///   1) 按段表顺序拼接所有 PT_LOAD 段的文件字节，首段 p_vaddr 记为加载基址 LoadAddr
///      （对齐 MPTool pof_get_next_seg 顺序拼接 + 首段地址记录），供上传到设备内存；
///   2) 提取符号表中所有具名符号的原始 st_value（SHN_ABS 与段内符号统一按名称解析），
///      供调用 probe_port / probe_dev / bootSgmt_driver_check / mem_rw 等函数时作为命令目标地址。
/// </summary>
public sealed class DriverImage
{
    /// <summary>所有 PT_LOAD 段文件字节的拼接（上传内容，对齐 MPTool DriverBuf）。</summary>
    public byte[] Segment { get; }

    /// <summary>首个 PT_LOAD 段的 p_vaddr（加载基址，对齐 MPTool DriverLoadAddr）。</summary>
    public uint LoadAddr { get; }

    /// <summary>全部具名符号 → 原始 st_value（对齐 MPTool pof_read_symbol 的 SYM_TYPE_VMA 取值）。</summary>
    public IReadOnlyDictionary<string, uint> Symbols { get; }

    private DriverImage(byte[] segment, uint loadAddr, IReadOnlyDictionary<string, uint> symbols)
    {
        Segment = segment;
        LoadAddr = loadAddr;
        Symbols = symbols;
    }

    public static DriverImage Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>从 UpgradeTool.Core 内嵌资源读取驱动 ELF（如 AXIDEsdspi.elf / AX3233AXIDE_A2.elf）。</summary>
    public static DriverImage LoadEmbedded(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"非法的驱动 ELF 文件名: {fileName ?? "(null)"}", nameof(fileName));

        var asm = typeof(DriverImage).Assembly;
        using Stream? stream = asm.GetManifestResourceStream($"UpgradeTool.Core.Resources.{fileName}")
            ?? throw new InvalidOperationException($"缺少内嵌资源 {fileName}（EmbeddedResource 未配置或驱动未纳入仓库）。");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    /// <summary>按名称解析符号地址；缺失时返回 0。</summary>
    public uint Resolve(string name) => Symbols.TryGetValue(name, out uint v) ? v : 0;

    /// <summary>按名称解析符号地址；缺失时抛异常。</summary>
    public uint ResolveOrThrow(string name)
        => Symbols.TryGetValue(name, out uint v) && v != 0
            ? v
            : throw new InvalidDataException($"驱动 ELF 缺少符号 {name}（资源：{(Symbols.Count == 0 ? "符号表为空" : "未找到")}）。");

    public static DriverImage Parse(byte[] elf)
    {
        if (elf.Length < 52)
            throw new InvalidDataException("驱动 ELF 文件过短。");
        if (elf[0] != 0x7f || elf[1] != 'E' || elf[2] != 'L' || elf[3] != 'F')
            throw new InvalidDataException("驱动文件不是 ELF 格式。");
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

        // ---- PT_LOAD 段：按表顺序拼接文件字节，首段 vaddr 为加载基址（对齐 MPTool AnalyzeElf）----
        var segments = new List<(uint Vaddr, byte[] Bytes)>();
        for (int i = 0; i < phNum; i++)
        {
            uint off = phOff + (uint)(i * phentSize);
            uint type = ReadU32(elf, off);
            if (type != 1) // PT_LOAD
                continue;
            uint pOffset = ReadU32(elf, off + 4);
            uint pVaddr = ReadU32(elf, off + 8);
            uint pFilesz = ReadU32(elf, off + 16);
            if (pFilesz == 0)
                continue;
            if (pOffset > (uint)elf.Length || pFilesz > (uint)elf.Length - pOffset)
                throw new InvalidDataException($"驱动 ELF PT_LOAD 段超出文件长度（offset=0x{pOffset:X} size=0x{pFilesz:X}）。");
            byte[] bytes = new byte[pFilesz];
            Array.Copy(elf, pOffset, bytes, 0, pFilesz);
            segments.Add((pVaddr, bytes));
        }
        if (segments.Count == 0)
            throw new InvalidDataException("驱动 ELF 缺少 PT_LOAD 段。");

        uint loadAddr = segments[0].Vaddr;
        int totalLen = segments.Sum(s => s.Bytes.Length);
        byte[] segment = new byte[totalLen];
        int dst = 0;
        foreach ((_, byte[] bytes) in segments)
        {
            Array.Copy(bytes, 0, segment, dst, bytes.Length);
            dst += bytes.Length;
        }

        // ---- 符号表：所有具名符号 → st_value ----
        uint? symOff = null;
        uint? symSize = null;
        uint? strOff = null;
        for (int i = 0; i < shNum; i++)
        {
            uint off = shOff + (uint)(i * shentSize);
            uint type = ReadU32(elf, off + 4);
            if (type != 2) // SHT_SYMTAB
                continue;
            symOff = ReadU32(elf, off + 16);
            symSize = ReadU32(elf, off + 20);
            uint link = ReadU32(elf, off + 24);
            if (link < shNum)
                strOff = ReadU32(elf, shOff + link * shentSize + 16);
        }
        if (symOff == null || strOff == null)
            throw new InvalidDataException("驱动 ELF 缺少符号表。");

        var symbols = new Dictionary<string, uint>(StringComparer.Ordinal);
        const int symEntSize = 16;
        for (uint off = symOff.Value; off + symEntSize <= symOff.Value + symSize!.Value; off += symEntSize)
        {
            uint nameOff = ReadU32(elf, off);
            uint value = ReadU32(elf, off + 4);
            ushort shndx = ReadU16(elf, off + 14);
            if (nameOff == 0 || shndx == 0) // 无名符号 / SHN_UNDEF
                continue;
            string name = ReadString(elf, strOff.Value, nameOff);
            if (name.Length == 0)
                continue;
            symbols.TryAdd(name, value); // 重复符号保留首个
        }

        return new DriverImage(segment, loadAddr, symbols);
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
        return Encoding.ASCII.GetString(b, start, end - start);
    }
}
