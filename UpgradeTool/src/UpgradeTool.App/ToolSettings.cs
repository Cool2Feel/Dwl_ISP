using System.IO;
using System.Text.Json;

namespace UpgradeTool.App;

/// <summary>
/// 工具本地设置：最近使用的固件路径 + 各选项勾选状态。
/// 以 JSON 形式保存到工具目录 settings.json（对齐参考项目 MPTool 同目录 mptool.ini 的做法），
/// 下次启动自动还原。
/// </summary>
public sealed class ToolSettings
{
    /// <summary>最近固件路径的最大保留条数。</summary>
    public const int MaxRecentPaths = 10;

    /// <summary>最近使用的固件路径（最新在前）。</summary>
    public List<string> RecentFirmwarePaths { get; set; } = new();

    // ---- 烧录选项勾选状态（默认值与界面初始勾选保持一致）----
    public bool AutoStart { get; set; }
    public bool QuickDebug { get; set; }
    public bool AutoReset { get; set; } = true;
    public bool EraseAll { get; set; }
    public bool Verify { get; set; } = true;
    public bool CapacityCheck { get; set; }
    public bool BootChecksum { get; set; } = true;
    public bool EnterOnly { get; set; }

    /// <summary>高级选项面板是否展开。</summary>
    public bool AdvancedOptionsVisible { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string DefaultFilePath => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    /// <summary>从磁盘加载设置；文件缺失或损坏时回退默认值，不抛异常。</summary>
    public static ToolSettings Load(string? filePath = null)
    {
        string path = filePath ?? DefaultFilePath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<ToolSettings>(File.ReadAllText(path)) ?? new ToolSettings();
        }
        catch
        {
            // 配置损坏时回退默认
        }
        return new ToolSettings();
    }

    /// <summary>保存设置到磁盘；写入失败时静默忽略，不影响主流程。</summary>
    public void Save(string? filePath = null)
    {
        string path = filePath ?? DefaultFilePath;
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // 保存失败静默忽略（如无写权限）
        }
    }

    /// <summary>记录一条固件路径：去重后插入头部，超出上限时裁剪尾部。</summary>
    public void AddRecentPath(string path)
    {
        string trimmed = path.Trim();
        if (trimmed.Length == 0)
            return;

        RecentFirmwarePaths.RemoveAll(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase));
        RecentFirmwarePaths.Insert(0, trimmed);
        if (RecentFirmwarePaths.Count > MaxRecentPaths)
            RecentFirmwarePaths.RemoveRange(MaxRecentPaths, RecentFirmwarePaths.Count - MaxRecentPaths);
    }
}