using System.Text.RegularExpressions;

namespace UpgradeTool.Core.Protocol;

/// <summary>
/// 固件侧符号地址来源（order.ini，随固件 build 由 elfsym.exe 生成）。
/// 下载协议用到三个固件函数：
///   cb_mem_rwex        = L1 内存读写分发（data phase 传输的 Func1）
///   ax32xx_sysDcacheFlush = L2 用作上传后 D-cache 回写（Func2）
///   ax32xx_sysIcacheInit  = L1 用作上传后 I-cache 全组失效
///
/// 对齐参考项目 MPTool 的符号解析原则：地址随"被实际使用的产物"走、运行期解析、内置常量仅兜底——
///   MPTool 的 SPI 驱动/固件函数地址来自驱动 ELF 符号表（pof_read_symbol 解析 pubsym，如
///   RBC_mem_rwex_DMA / l1_func_spi_init / l2_func_spi_page_program），或来自 order.ini/FuncList
///   （GetPrivateProfileInt("ORDER","cb_mem_rwex",...)，路径由 DeviceLib.ini 按设备配置）。
/// 本类对固件侧函数（不在 stub ELF 内）以 order.ini 为默认来源（LoadDefault 自动发现），
/// 找不到时才回退到内置常量（仅作缺省兜底，随 build 会漂移）。
/// </summary>
public sealed class FirmwareSymbols
{
    // 回退常量对齐当前固件 build（ax32_platform_demo/output/order.ini 与 ax329x_sdk.map）。
    // 注意：这些地址随固件编译会漂移，必须以 order.ini/符号表为准；内置值仅作缺省兜底。
    public const uint DefaultCbMemRwex = 0x020446B8;
    public const uint DefaultDcacheFlush = 0x02024A60;
    public const uint DefaultIcacheInit = 0x020249A0;

    /// <summary>未找到 order.ini 时的来源描述。</summary>
    public const string FallbackSource = "内置回退常量";

    public uint CbMemRwex { get; }
    public uint DcacheFlush { get; }
    public uint IcacheInit { get; }

    /// <summary>符号来源（order.ini 绝对路径；未找到时为 <see cref="FallbackSource"/>），用于日志排查"符号过期"。</summary>
    public string Source { get; }

    private FirmwareSymbols(uint cbMemRwex, uint dcacheFlush, uint icacheInit, string source)
    {
        CbMemRwex = cbMemRwex;
        DcacheFlush = dcacheFlush;
        IcacheInit = icacheInit;
        Source = source;
    }

    /// <summary>
    /// 从显式路径解析；路径为 null 或不存在时回退内置常量。
    /// 保持向后兼容：显式注入与测试均走此入口，不受自动发现影响。
    /// </summary>
    public static FirmwareSymbols LoadOrDefault(string? orderIniPath)
        => LoadFromCandidates(orderIniPath is { Length: > 0 } ? new[] { orderIniPath } : Array.Empty<string>());

    /// <summary>
    /// 默认加载：自动发现固件符号表（对齐 MPTool 从产物/配置文件解析、内置常量仅兜底）。
    /// 候选顺序：环境变量 <c>UPGRADETOOL_ORDER_INI</c> → exe 目录 <c>order.ini</c> → exe 目录 <c>setting\order.ini</c>
    /// （setting 子目录对齐 MPTool <c>curpath\setting</c> 的配置布局）。
    /// 全部未找到时回退内置常量，<see cref="Source"/> 记录实际来源。
    /// </summary>
    public static FirmwareSymbols LoadDefault()
        => LoadFromCandidates(DefaultCandidates());

    /// <summary>默认候选路径：环境变量优先，其次 exe 目录（对齐 MPTool curpath\setting 布局）。</summary>
    internal static IEnumerable<string> DefaultCandidates()
    {
        string? env = Environment.GetEnvironmentVariable("UPGRADETOOL_ORDER_INI");
        if (!string.IsNullOrEmpty(env))
            yield return env;
        string baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "order.ini");
        yield return Path.Combine(baseDir, "setting", "order.ini");
    }

    /// <summary>按候选路径逐个尝试，取第一个存在的 order.ini 解析；均不存在时内置常量兜底。</summary>
    internal static FirmwareSymbols LoadFromCandidates(IEnumerable<string> candidates)
    {
        foreach (string candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate) || !File.Exists(candidate))
                continue;
            var symbols = ParseOrderIni(File.ReadAllText(candidate));
            return new FirmwareSymbols(
                PickOrDefault(symbols, "cb_mem_rwex", DefaultCbMemRwex),
                PickOrDefault(symbols, "ax32xx_sysDcacheFlush", DefaultDcacheFlush),
                PickOrDefault(symbols, "ax32xx_sysIcacheInit", DefaultIcacheInit),
                Path.GetFullPath(candidate));
        }
        return new FirmwareSymbols(DefaultCbMemRwex, DefaultDcacheFlush, DefaultIcacheInit, FallbackSource);
    }

    /// <summary>取符号地址；键缺失或值为 0（未解析/无效）时回退到内置常量。</summary>
    private static uint PickOrDefault(IReadOnlyDictionary<string, uint> symbols, string key, uint fallback)
        => symbols.TryGetValue(key, out uint value) && value != 0 ? value : fallback;

    /// <summary>
    /// 解析 order.ini：忽略段头（[RAM]/[ORDER] 等），提取 `key = 0x........`。
    /// 也支持 list.tmp 风格的 `$(name)` 占位（视为未解析，忽略）。
    /// </summary>
    private static Dictionary<string, uint> ParseOrderIni(string text)
    {
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '[' || line[0] == ';' || line[0] == '#')
                continue;

            int eq = line.IndexOf('=');
            if (eq < 0)
                continue;

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();

            if (value.StartsWith("$(", StringComparison.Ordinal))
                continue; // 未解析占位符

            Match m = Regex.Match(value, @"0[xX]([0-9a-fA-F]{1,8})");
            if (m.Success)
                result[key] = Convert.ToUInt32(m.Groups[1].Value, 16);
        }
        return result;
    }
}
