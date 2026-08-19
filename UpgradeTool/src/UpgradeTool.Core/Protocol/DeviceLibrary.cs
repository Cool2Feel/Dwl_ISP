namespace UpgradeTool.Core.Protocol;

/// <summary>
/// 设备类型（对齐 MPTool SearchDev 按 DeviceLib.ini 的 ClassInfo 派发的处理类）。
/// 每种类型对应不同的识别/连接路径：
///   Loader   —— Loader/Bootloader 态，0xCB 生产通道（Video030~070Loader，驱动经 SpiDriverPath 选择）
///   Isp      —— AXISP：ISP 设备，经 0xDA(UFIsp) 进入 ISP/升级模式
///   DirectSpi—— AX326X：直连 SPI 设备（应用态，0xDA 进入升级模式后重新枚举）
///   LegacyRp —— AX3233RP / AX3233Efuse：量产（RP）型设备
///   Adapter  —— AX2005Adapter：适配器设备，可检测子设备（BerrySdio，两阶段识别）
///   Unknown  —— 无 ClassInfo / 未识别（如 DeviceLib [4] BuildWinUSBoot Protocol 2.00）
/// </summary>
public enum DeviceKind
{
    Unknown,
    Loader,
    Isp,
    DirectSpi,
    LegacyRp,
    Adapter,
}

/// <summary>
/// DeviceLib.ini 的一个设备条目（对齐 MPTool 的 DeviceLib.ini 设备库配置）。
/// 按设备 INQUIRY 产品串（InquiryInfo）描述对应芯片类别与 SPI 驱动文件：
///   ClassInfo     = 处理类（AX326X / AXISP / AX3233RP / AX2005Adapter）
///   SpiDriverPath = SPI 驱动 ELF 文件名（内嵌资源，如 AX326X.elf / ThunderSE.elf）
///   FuncListPath  = 固件符号表文件名（order.ini，老版本 AX3233RP 路径使用）
///   Isp           = 是否 ISP 类设备
/// </summary>
public sealed record DeviceEntry(
    int Index,
    string InquiryInfo,
    string ClassInfo,
    string SpiDriverPath,
    string FuncListPath,
    bool IsIsp,
    bool IsAdapter,
    bool IsLoader)
{
    /// <summary>是否有可用的 SPI 驱动文件。</summary>
    public bool HasSpiDriver => !string.IsNullOrWhiteSpace(SpiDriverPath);

    /// <summary>
    /// 设备类型（由 ClassInfo / IsIsp / IsLoader 解析，对齐 MPTool SearchDev 的 ClassInfo 派发）。
    /// 判定顺序：Adapter &gt; Isp &gt; Loader &gt; AX3233*（量产RP）&gt; AX326X（直连SPI）。
    /// </summary>
    public DeviceKind Kind
    {
        get
        {
            if (ClassInfo.Contains("Adapter", StringComparison.OrdinalIgnoreCase))
                return DeviceKind.Adapter;
            if (IsIsp)
                return DeviceKind.Isp;
            if (IsLoader)
                return DeviceKind.Loader;
            if (ClassInfo.Contains("AX3233", StringComparison.OrdinalIgnoreCase))
                return DeviceKind.LegacyRp;
            if (ClassInfo.Equals("AX326X", StringComparison.OrdinalIgnoreCase))
                return DeviceKind.DirectSpi;
            return DeviceKind.Unknown;
        }
    }

    /// <summary>设备类型的可读标签（用于日志与界面显示）。</summary>
    public string KindLabel => Kind switch
    {
        DeviceKind.Loader => "Loader",
        DeviceKind.Isp => "AXISP(ISP)",
        DeviceKind.DirectSpi => "AX326X(直连SPI)",
        DeviceKind.LegacyRp => "AX3233RP(量产)",
        DeviceKind.Adapter => "AX2005Adapter(适配器)",
        _ => "未知",
    };
}

/// <summary>
/// DeviceLib.ini 解析器（对齐 MPTool 的 DeviceLib.ini）。
/// 结构：
///   [COMMON]        ItemSum = 条目总数
///   [1]..[N]        设备条目（InquiryInfo / ClassInfo / SpiDriverPath / FuncListPath / Isp）
///
/// 作用：按设备当前 INQUIRY 产品串选中驱动 ELF（SpiDriverPath），驱动/固件函数地址再从该 ELF
/// 符号表解析（见 LoaderImage / LoaderConfig），内置常量仅兜底——对齐 MPTool 的
/// "DeviceLib.ini 选驱动 + pof 解析 ELF 符号"流程。
/// </summary>
public sealed class DeviceLibrary
{
    /// <summary>[COMMON] 段名。</summary>
    public const string CommonSection = "COMMON";

    /// <summary>
    /// 内嵌 DeviceLib.ini 的缓存实例（线程安全懒加载）。
    /// 对齐 MPTool 启动时读一次设备库：避免每台设备每次扫描都重新解析 INI，
    /// 也保证同一进程内识别结论一致（解析瞬时失败不再造成识别结果抖动）。
    /// </summary>
    private static readonly Lazy<DeviceLibrary> _embedded =
        new(LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>内嵌 DeviceLib.ini 的缓存实例（首次访问时加载并缓存）。</summary>
    public static DeviceLibrary Embedded => _embedded.Value;

    public int ItemSum { get; }

    /// <summary>全部设备条目（按 [1]..[N] 顺序）。</summary>
    public IReadOnlyList<DeviceEntry> Entries { get; }

    private DeviceLibrary(int itemSum, IReadOnlyList<DeviceEntry> entries)
    {
        ItemSum = itemSum;
        Entries = entries;
    }

    /// <summary>从指定路径读取 DeviceLib.ini。</summary>
    public static DeviceLibrary Load(string path) => Parse(File.ReadAllText(path, System.Text.Encoding.Latin1));

    /// <summary>从 UpgradeTool.Core 内嵌资源读取 DeviceLib.ini（EmbeddedResource: Resources\DeviceLib.ini）。</summary>
    public static DeviceLibrary LoadEmbedded()
    {
        var asm = typeof(DeviceLibrary).Assembly;
        using Stream? stream = asm.GetManifestResourceStream("UpgradeTool.Core.Resources.DeviceLib.ini")
            ?? throw new InvalidOperationException("缺少内嵌资源 DeviceLib.ini（EmbeddedResource 未配置）。");
        using var reader = new StreamReader(stream, System.Text.Encoding.Latin1);
        return Parse(reader.ReadToEnd());
    }

    public static DeviceLibrary Parse(string iniText)
    {
        int itemSum = 0;
        var entries = new List<DeviceEntry>();

        string? section = null;
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void FlushEntry()
        {
            if (section == null || section.Equals(CommonSection, StringComparison.OrdinalIgnoreCase))
                return;
            if (current.Count == 0)
                return;
            if (!int.TryParse(section, out int index))
                return;

            entries.Add(new DeviceEntry(
                index,
                current.GetValueOrDefault("InquiryInfo") ?? "",
                current.GetValueOrDefault("ClassInfo") ?? "",
                current.GetValueOrDefault("SpiDriverPath") ?? "",
                current.GetValueOrDefault("FuncListPath") ?? "",
                IsIsp: current.TryGetValue("Isp", out string? isp) && isp.Trim() != "0",
                IsAdapter: (current.GetValueOrDefault("ClassInfo") ?? "").Contains("Adapter", StringComparison.OrdinalIgnoreCase),
                IsLoader: (current.GetValueOrDefault("InquiryInfo") ?? "").Contains("loader", StringComparison.OrdinalIgnoreCase)));
            current.Clear();
        }

        foreach (string rawLine in iniText.Split('\n', '\r'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                FlushEntry();
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
                if (key.Equals("ItemSum", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(value, out itemSum);
            }
            else
            {
                current[key] = value;
            }
        }
        FlushEntry();

        return new DeviceLibrary(itemSum, entries);
    }

    /// <summary>
    /// 按设备产品串匹配设备条目（对齐 MPTool 按 INQUIRY 串选设备）：
    /// 规范化（去空白/折叠空格/小写）后，条目 InquiryInfo 与产品串互为前缀即命中，返回第一个匹配。
    /// 无匹配返回 null。
    /// </summary>
    public DeviceEntry? Match(string? productId)
    {
        if (string.IsNullOrWhiteSpace(productId))
            return null;
        string norm = Normalize(productId);
        foreach (DeviceEntry entry in Entries)
        {
            string inq = Normalize(entry.InquiryInfo);
            if (inq.Length == 0)
                continue;
            if (inq.StartsWith(norm, StringComparison.Ordinal) || norm.StartsWith(inq, StringComparison.Ordinal))
                return entry;
        }
        return null;
    }

    /// <summary>
    /// 匹配 Loader/Bootloader 态设备条目（0xCB 下载通道专用）：
    /// 只考虑含 "loader" 的条目，且要求有可用的 SPI 驱动文件（SpiDriverPath 非空）。
    /// </summary>
    public DeviceEntry? MatchLoader(string? productId)
    {
        if (string.IsNullOrWhiteSpace(productId))
            return null;
        string norm = Normalize(productId);
        foreach (DeviceEntry entry in Entries)
        {
            if (!entry.IsLoader || !entry.HasSpiDriver)
                continue;
            string inq = Normalize(entry.InquiryInfo);
            if (inq.Length == 0)
                continue;
            if (inq.StartsWith(norm, StringComparison.Ordinal) || norm.StartsWith(inq, StringComparison.Ordinal))
                return entry;
        }
        return null;
    }

    /// <summary>
    /// 按设备完整身份串（VendorId+ProductId+ProductRevision 拼接）匹配设备条目，
    /// 对齐 MPTool SearchDeviceID：DeviceLib.ini 的 InquiryInfo 即厂商(8)/产品(16)/版本(4)
    /// 三个 INQUIRY 字段的 28 字节拼接。匹配前去掉所有空白并小写（容忍字段填充/版本缺失差异），
    /// 规范化后互为前缀即命中（与 Match/MatchLoader 同一前缀语义）。无匹配返回 null。
    /// </summary>
    public DeviceEntry? MatchIdentity(string? vendorId, string? productId, string? productRevision = null)
    {
        string identity = Compact(string.Concat(vendorId ?? "", productId ?? "", productRevision ?? ""));
        if (identity.Length == 0)
            return null;
        foreach (DeviceEntry entry in Entries)
        {
            string inq = Compact(entry.InquiryInfo);
            if (inq.Length == 0)
                continue;
            if (inq.StartsWith(identity, StringComparison.Ordinal) || identity.StartsWith(inq, StringComparison.Ordinal))
                return entry;
        }
        return null;
    }

    /// <summary>规范化产品串/INQUIRY 串：去首尾空白、折叠连续空白、小写。</summary>
    private static string Normalize(string text)
    {
        System.Text.StringBuilder sb = new(text.Length);
        bool prevSpace = false;
        foreach (char c in text.Trim())
        {
            bool space = char.IsWhiteSpace(c);
            if (space && prevSpace)
                continue;
            sb.Append(char.ToLowerInvariant(c));
            prevSpace = space;
        }
        return sb.ToString();
    }

    /// <summary>跨字段身份串压缩：去掉所有空白并小写（用于 Vendor+Product+Revision 拼接匹配）。</summary>
    private static string Compact(string text)
    {
        System.Text.StringBuilder sb = new(text.Length);
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
                continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
