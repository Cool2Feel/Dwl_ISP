using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Tests;

/// <summary>
/// 固件符号表（order.ini 覆盖 + 内置回退常量）测试。
/// 回退常量必须对齐当前固件 build（ax32_platform_demo/output/order.ini / ax329x_sdk.map），
/// 否则 0xCD 应用态通道会调用错误的固件函数地址。
/// </summary>
public class FirmwareSymbolsTests
{
    [Fact]
    public void DefaultSymbols_MatchCurrentFirmwareBuild()
    {
        // 与 ax32_platform_demo/output/order.ini（cb_mem_rwex = 0x020446B8）和
        // ax329x_sdk.map（ax32xx_sysDcacheFlush=0x02024A60 / ax32xx_sysIcacheInit=0x020249A0）对齐
        FirmwareSymbols s = FirmwareSymbols.LoadOrDefault(null);

        Assert.Equal(0x020446B8u, s.CbMemRwex);
        Assert.Equal(0x02024A60u, s.DcacheFlush);
        Assert.Equal(0x020249A0u, s.IcacheInit);
    }

    [Fact]
    public void LoadOrDefault_PicksValuesFromOrderIni()
    {
        using var temp = TempFile.Create("[ORDER]\ncb_mem_rwex = 0x11111111\nax32xx_sysDcacheFlush = 0x22222222\nax32xx_sysIcacheInit = 0x33333333\n");

        FirmwareSymbols s = FirmwareSymbols.LoadOrDefault(temp.Path);

        Assert.Equal(0x11111111u, s.CbMemRwex);
        Assert.Equal(0x22222222u, s.DcacheFlush);
        Assert.Equal(0x33333333u, s.IcacheInit);
    }

    [Fact]
    public void LoadOrDefault_ZeroOrMissingKeys_FallBackToDefaults()
    {
        // 值为 0（无效/未解析）或缺键 -> 回退内置常量
        using var temp = TempFile.Create("[ORDER]\ncb_mem_rwex = 0x0\nax32xx_sysDcacheFlush = 0x0\n");

        FirmwareSymbols s = FirmwareSymbols.LoadOrDefault(temp.Path);

        Assert.Equal(FirmwareSymbols.DefaultCbMemRwex, s.CbMemRwex);
        Assert.Equal(FirmwareSymbols.DefaultDcacheFlush, s.DcacheFlush);
        Assert.Equal(FirmwareSymbols.DefaultIcacheInit, s.IcacheInit);
    }

    // ---------- 自动发现（对齐 MPTool 从产物/配置解析、内置常量仅兜底） ----------

    [Fact]
    public void LoadFromCandidates_NoCandidates_FallsBackToDefaultsAndTracksSource()
    {
        FirmwareSymbols s = FirmwareSymbols.LoadFromCandidates(Array.Empty<string>());

        Assert.Equal(FirmwareSymbols.DefaultCbMemRwex, s.CbMemRwex);
        Assert.Equal(FirmwareSymbols.DefaultDcacheFlush, s.DcacheFlush);
        Assert.Equal(FirmwareSymbols.DefaultIcacheInit, s.IcacheInit);
        Assert.Equal(FirmwareSymbols.FallbackSource, s.Source);
    }

    [Fact]
    public void LoadFromCandidates_FirstExistingCandidateWins()
    {
        using var temp = TempFile.Create("[ORDER]\ncb_mem_rwex = 0x12345678\n");

        FirmwareSymbols s = FirmwareSymbols.LoadFromCandidates(new[] { temp.Path, "nonexistent-order.ini" });

        Assert.Equal(0x12345678u, s.CbMemRwex);
        Assert.Equal(FirmwareSymbols.DefaultDcacheFlush, s.DcacheFlush); // 缺键回退
        Assert.Equal(Path.GetFullPath(temp.Path), s.Source);
    }

    [Fact]
    public void LoadDefault_DiscoveredOrderIni_UsedAsSource()
    {
        // 显式注入 exe 目录候选：验证 LoadDefault 路径解析逻辑（不依赖真实环境）
        using var temp = TempFile.Create("[ORDER]\ncb_mem_rwex = 0xABCDEF00\nax32xx_sysDcacheFlush = 0x12340000\nax32xx_sysIcacheInit = 0x56780000\n");

        FirmwareSymbols s = FirmwareSymbols.LoadFromCandidates(new[] { Path.Combine(Path.GetTempPath(), "order.ini"), temp.Path });

        // 第一个候选不存在时继续尝试后续候选
        Assert.Equal(0xABCDEF00u, s.CbMemRwex);
        Assert.Equal(0x12340000u, s.DcacheFlush);
        Assert.Equal(0x56780000u, s.IcacheInit);
        Assert.Equal(Path.GetFullPath(temp.Path), s.Source);
    }

    [Fact]
    public void DefaultCandidates_EnvVarFirst_ThenExeDir()
    {
        const string envKey = "UPGRADETOOL_ORDER_INI";
        string? previous = Environment.GetEnvironmentVariable(envKey);
        try
        {
            Environment.SetEnvironmentVariable(envKey, @"D:\virtual\order.ini");
            string[] candidates = FirmwareSymbols.DefaultCandidates().ToArray();

            Assert.Equal(@"D:\virtual\order.ini", candidates[0]);
            Assert.Contains(Path.Combine(AppContext.BaseDirectory, "order.ini"), candidates);
            Assert.Contains(Path.Combine(AppContext.BaseDirectory, "setting", "order.ini"), candidates);
        }
        finally
        {
            if (previous is null)
                Environment.SetEnvironmentVariable(envKey, null);
            else
                Environment.SetEnvironmentVariable(envKey, previous);
        }
    }

    /// <summary>临时 order.ini 文件辅助（自动清理）。</summary>
    private sealed class TempFile : IDisposable
    {
        public string Path { get; }

        private TempFile(string path) => Path = path;

        public static TempFile Create(string content)
        {
            string path = System.IO.Path.GetTempFileName();
            File.WriteAllText(path, content);
            return new TempFile(path);
        }

        public void Dispose()
        {
            try { File.Delete(Path); } catch { /* 清理失败忽略 */ }
        }
    }
}
