using System.Text;

namespace UpgradeTool.Core.Protocol;

/// <summary>
/// FlashLib.ini 的一个读 ID 方法定义（[COMMON] 段 Read-ID-* 键）。
/// 字节序列语义（来自 FlashLib.ini 注释）：首个字节是后续序列长度（命令 + 哑元字节），
/// 之后是命令与哑元字节，再之后读取到的才是 ID。例如：
///   Read-ID-9F=0x01,0x9F              → 1 字节后续（仅命令 0x9F）
///   Read-ID-AB=0x04,0xAB,0x00,0x00,0x00 → 4 字节后续（0xAB + 3 个哑元）
/// </summary>
public sealed record FlashReadIdMethod(string Method, byte[] Sequence)
{
    /// <summary>命令字节（Sequence 首个非长度字节）。</summary>
    public byte Command => Sequence.Length > 1 ? Sequence[1] : (byte)0;
}

/// <summary>
/// Flash 的一个 ID 匹配条件（如 ID-9F=0x85601685 + ID-9F-MASK=0xFFFFFFFF）。
/// ID 值把响应字节按大端打包到 32 位，与 MASK 相与后比较。
/// 4 字节响应（如 85 60 16 85）打包得 0x85601685，精确匹配 FlashLib.ini [14]。
/// 3 字节响应（如 EF 40 16）打包得 0xEF401600，精确匹配 FlashLib.ini [12]。
/// </summary>
public sealed record FlashDeviceId(string Method, uint Value, uint Mask);

/// <summary>
/// FlashLib.ini 中的一个 Flash 器件条目（[1]..[N] 段），含容量、页大小与完整指令集。
/// 与 FlashInfo（协议运行期从设备读到的真实信息）不同，这里是静态配置表，
/// 用于在无设备时推断容量/指令码，或对协议内置常量做交叉校验。
/// </summary>
public sealed class FlashDeviceSpec
{
    public string Name { get; }
    public uint Capacity { get; }
    public string SectorType { get; }
    public uint MinSectorSize { get; }
    public uint PageSize { get; }
    public IReadOnlyList<FlashDeviceId> Ids { get; }
    public uint? WriteEnable { get; }
    public uint? WriteDisable { get; }
    public uint? ReadStatusRegister { get; }
    public uint? WriteStatusRegister { get; }
    public uint? Read { get; }
    public uint? FastRead { get; }
    public uint? PageProgram { get; }
    public uint? Erase4K { get; }
    public uint? Erase64K { get; }
    public uint? EraseChip { get; }

    public FlashDeviceSpec(
        string name, uint capacity, string sectorType, uint minSectorSize, uint pageSize,
        IReadOnlyList<FlashDeviceId> ids,
        uint? writeEnable, uint? writeDisable, uint? readStatusRegister, uint? writeStatusRegister,
        uint? read, uint? fastRead, uint? pageProgram, uint? erase4K, uint? erase64K, uint? eraseChip)
    {
        Name = name;
        Capacity = capacity;
        SectorType = sectorType;
        MinSectorSize = minSectorSize;
        PageSize = pageSize;
        Ids = ids;
        WriteEnable = writeEnable;
        WriteDisable = writeDisable;
        ReadStatusRegister = readStatusRegister;
        WriteStatusRegister = writeStatusRegister;
        Read = read;
        FastRead = fastRead;
        PageProgram = pageProgram;
        Erase4K = erase4K;
        Erase64K = erase64K;
        EraseChip = eraseChip;
    }

    /// <summary>判断 RDID 响应字节是否匹配指定方法的 ID 条件（打包大端，与 MASK 相与比较）。</summary>
    public bool Matches(string method, byte[] rdid)
    {
        uint packed = PackRdid(rdid);
        foreach (FlashDeviceId id in Ids)
        {
            if (!id.Method.Equals(method, StringComparison.OrdinalIgnoreCase))
                continue;
            if ((packed & id.Mask) == (id.Value & id.Mask))
                return true;
        }
        return false;
    }

    /// <summary>判断 RDID 响应字节是否匹配 JEDEC（0x9F）ID 条件。</summary>
    public bool Matches9F(byte[] rdid) => Matches("9F", rdid);

    /// <summary>
    /// 把 RDID 响应字节按大端打包到 32 位，最多 4 字节。
    /// 与 FlashLib.ini 的 ID-xx 值（通常 4 字节，含第 4 字节回显）对齐。
    /// </summary>
    private static uint PackRdid(byte[] rdid) => rdid.Length switch
    {
        >= 4 => ((uint)rdid[0] << 24) | ((uint)rdid[1] << 16) | ((uint)rdid[2] << 8) | rdid[3],
        >= 3 => ((uint)rdid[0] << 24) | ((uint)rdid[1] << 16) | ((uint)rdid[2] << 8),
        >= 2 => ((uint)rdid[0] << 24) | ((uint)rdid[1] << 16),
        >= 1 => (uint)rdid[0] << 24,
        _ => 0
    };
}

/// <summary>
/// FlashLib.ini 解析器（对齐 MPTool 的 FlashLib.ini）。
/// 文件是 GBK 编码（注释为中文，值均为 ASCII hex）；本类按字节透明解码（Latin-1），
/// 只消费键/值 token，注释（; 起头或行内 ;）全部丢弃。
///
/// 结构：
///   [COMMON]          全局配置（Loader-Version / Firmware / Address / Read-ID-*）
///   [1]..[N]          器件表（Name / Capacity / Sector-Type / Min-Sector-Size / Page-Size /
///                      ID-xx + ID-xx-MASK / 指令集 Write-Enable 等）
///
/// W25Q32（EF 40 16）对应条目 [12]：Capacity=0x400000、Erase-4K=0x20、Erase-64K=0xD8、
/// Read=0x03、Page-Program=0x02、Write-Enable=0x06，与 Dc503RomProtocol 内置常量一致。
/// </summary>
public sealed class FlashLib
{
    /// <summary>[COMMON] 段名。</summary>
    public const string CommonSection = "COMMON";

    public string LoaderVersion { get; }
    public string Firmware { get; }
    public uint Address { get; }
    public IReadOnlyList<FlashReadIdMethod> ReadIdMethods { get; }
    public IReadOnlyList<FlashDeviceSpec> Devices { get; }

    private FlashLib(
        string loaderVersion, string firmware, uint address,
        IReadOnlyList<FlashReadIdMethod> readIdMethods, IReadOnlyList<FlashDeviceSpec> devices)
    {
        LoaderVersion = loaderVersion;
        Firmware = firmware;
        Address = address;
        ReadIdMethods = readIdMethods;
        Devices = devices;
    }

    public static FlashLib Load(string path) => Parse(File.ReadAllText(path, Encoding.Latin1));

    /// <summary>从 UpgradeTool.Core 内嵌资源读取 FlashLib.ini（EmbeddedResource: Resources\FlashLib.ini）。</summary>
    public static FlashLib LoadEmbedded()
    {
        var asm = typeof(FlashLib).Assembly;
        using Stream? stream = asm.GetManifestResourceStream("UpgradeTool.Core.Resources.FlashLib.ini")
            ?? throw new InvalidOperationException("缺少内嵌资源 FlashLib.ini（EmbeddedResource 未配置）。");
        using var reader = new StreamReader(stream, Encoding.Latin1);
        return Parse(reader.ReadToEnd());
    }

    public static FlashLib Parse(string iniText)
    {
        string loaderVersion = "";
        string firmware = "";
        uint address = 0;
        var readIds = new List<FlashReadIdMethod>();
        var devices = new List<FlashDeviceSpec>();

        string? section = null;
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void FlushDevice()
        {
            if (section == null || section.Equals(CommonSection, StringComparison.OrdinalIgnoreCase))
                return;
            FlashDeviceSpec? spec = BuildDevice(current);
            if (spec != null)
                devices.Add(spec);
            current.Clear();
        }

        foreach (string rawLine in SplitLines(iniText))
        {
            string line = rawLine;
            int semi = line.IndexOf(';');
            if (semi >= 0)
                line = line[..semi];
            line = line.Trim();
            if (line.Length == 0)
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                FlushDevice();
                section = line[1..^1].Trim();
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0 || section == null)
                continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();

            if (section.Equals(CommonSection, StringComparison.OrdinalIgnoreCase))
            {
                switch (key)
                {
                    case "Loader-Version": loaderVersion = value; break;
                    case "Firmware": firmware = value; break;
                    case "Address": address = ParseHex(value); break;
                    default:
                        if (key.StartsWith("Read-ID-", StringComparison.OrdinalIgnoreCase))
                            readIds.Add(new FlashReadIdMethod(key[8..], ParseByteList(value)));
                        break;
                }
            }
            else
            {
                current[key] = value;
            }
        }
        FlushDevice();

        return new FlashLib(loaderVersion, firmware, address, readIds, devices);
    }

    /// <summary>按 JEDEC（0x9F）ID 匹配器件，返回第一个匹配项；无匹配返回 null。</summary>
    public FlashDeviceSpec? Match9F(byte[] rdid)
    {
        foreach (FlashDeviceSpec device in Devices)
        {
            if (device.Matches9F(rdid))
                return device;
        }
        return null;
    }

    /// <summary>按指定方法匹配器件，返回第一个匹配项；无匹配返回 null。</summary>
    public FlashDeviceSpec? Match(string method, byte[] rdid)
    {
        foreach (FlashDeviceSpec device in Devices)
        {
            if (device.Matches(method, rdid))
                return device;
        }
        return null;
    }

    /// <summary>
    /// 从 JEDEC RDID 响应推导 Flash 容量（对齐 MPTool AutoAddFlashType）。
    /// 当 FlashLib 未匹配到该 ID 时，用此公式从 JEDEC 密度字段推得容量，使未知但有效的
    /// Flash 也能以正确容量烧写（而不是一律回退默认 4MB）：
    ///   packed = 响应字节大端打包（最多 4 字节）；
    ///   守卫   = bits[15:12]（内存类型字节低半字节）须为 0 或 1；
    ///   密度   = y = bits[11:8] - 1；
    ///   容量   = 2^y * 1024 * 1024 / 8 字节。
    /// 无法推导（响应过短 / 守卫不符 / 密度越界，如 1F FF FF、FF FF FF 等无效 ID）返回 null。
    /// </summary>
    public static uint? DeriveCapacityFromRdid(byte[]? rdid)
    {
        if (rdid == null || rdid.Length < 3)
            return null;
        uint packed = rdid.Length switch
        {
            >= 4 => ((uint)rdid[0] << 24) | ((uint)rdid[1] << 16) | ((uint)rdid[2] << 8) | rdid[3],
            _ => ((uint)rdid[0] << 24) | ((uint)rdid[1] << 16) | ((uint)rdid[2] << 8),
        };
        int guard = (int)((packed >> 12) & 0x0F);
        if (guard != 0 && guard != 1)
            return null;
        int y = (int)((packed >> 8) & 0x0F) - 1;
        if (y < 0 || y >= 0x0E)
            return null;
        return (uint)((ulong)Math.Pow(2, y) * 1024 * 1024 / 8);
    }

    private static FlashDeviceSpec? BuildDevice(IReadOnlyDictionary<string, string> kv)
    {
        if (!kv.TryGetValue("Capacity", out string? capacityText))
            return null;

        var ids = new List<FlashDeviceId>();
        foreach ((string key, string value) in kv)
        {
            if (!key.StartsWith("ID-", StringComparison.OrdinalIgnoreCase) ||
                key.EndsWith("-MASK", StringComparison.OrdinalIgnoreCase))
                continue;
            string method = key[3..];
            uint mask = 0xFFFFFFFF;
            if (kv.TryGetValue($"ID-{method}-MASK", out string? maskText))
                mask = ParseHex(maskText);
            ids.Add(new FlashDeviceId(method, ParseHex(value), mask));
        }

        return new FlashDeviceSpec(
            kv.GetValueOrDefault("Name") ?? "AutoAdd",
            ParseHex(capacityText),
            kv.GetValueOrDefault("Sector-Type") ?? "Simple",
            kv.TryGetValue("Min-Sector-Size", out string? minSectorText) ? ParseHex(minSectorText) : 0x1000,
            kv.TryGetValue("Page-Size", out string? pageSizeText) ? ParseHex(pageSizeText) : 0x100,
            ids,
            OptionalHex(kv, "Write-Enable"),
            OptionalHex(kv, "Write-Disable"),
            OptionalHex(kv, "Read-Status-Register"),
            OptionalHex(kv, "Write-Status-Register"),
            OptionalHex(kv, "Read"),
            OptionalHex(kv, "Fast-Read"),
            OptionalHex(kv, "Page-Program"),
            OptionalHex(kv, "Erase-4K"),
            OptionalHex(kv, "Erase-64K"),
            OptionalHex(kv, "Erase-Chip"));
    }

    private static uint? OptionalHex(IReadOnlyDictionary<string, string> kv, string key)
        => kv.TryGetValue(key, out string? value) ? ParseHex(value) : null;

    private static uint ParseHex(string text)
    {
        string hex = text.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        return Convert.ToUInt32(hex, 16);
    }

    private static byte[] ParseByteList(string text)
    {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
        var bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            bytes[i] = (byte)ParseHex(parts[i]);
        return bytes;
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Split('\n', '\r');
}
