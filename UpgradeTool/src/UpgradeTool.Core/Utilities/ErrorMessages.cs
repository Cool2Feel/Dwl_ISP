namespace UpgradeTool.Core.Utilities;

/// <summary>Win32 错误码 → 中文可操作消息映射。</summary>
public static class ErrorMessages
{
    private static readonly Dictionary<int, ErrorEntry> _map = new()
    {
        [2]   = new("系统找不到文件", "设备句柄无效或设备未连接。", "请检查设备是否已插入，然后点击「刷新设备」重试。"),
        [5]   = new("拒绝访问", "SCSI 直通命令需要管理员权限。", "请以管理员身份重新运行本工具。"),
        [21]  = new("设备未就绪", "设备正在初始化或未正确响应。", "请等待设备就绪后重试，或重新插拔 USB 线缆。"),
        [32]  = new("文件共享冲突", "设备句柄被其他进程占用。", "请关闭其他可能占用磁盘设备的程序（如磁盘管理器、分区工具）。"),
        [55]  = new("设备不存在", "设备已断开连接或已被移除。", "请检查 USB 连接是否牢固，重新插拔设备。"),
        [87]  = new("参数错误", "SCSI 命令参数不正确，可能是 CDB 布局与固件不匹配。", "请确认固件版本与工具兼容，联系技术支持。"),
        [121] = new("信号量超时", "设备响应 USB 事务超时（USB 总线级超时约 5 秒），通常是因为 0xCD 厂商通道不可用或固件符号地址不匹配导致设备无响应。", "检查设备是否卡死，确认固件符号表（order.ini）与当前固件版本匹配，重新插拔后重试。"),
        [170] = new("资源忙", "设备正在被其他进程占用。", "请关闭其他可能占用设备的程序（如磁盘管理器、资源管理器）。"),
        [995] = new("操作已取消", "SCSI 命令被用户取消。", "无需操作，这是正常取消行为。"),
        [997] = new("IO 操作挂起", "SCSI 命令超时未返回，设备可能已卡死。", "请重新插拔设备后重试。"),
        [1117] = new("IO 设备错误", "SCSI 命令因设备错误而失败。", "设备可能已损坏，请重新插拔或更换 USB 线缆。"),
    };

    /// <summary>获取完整的错误卡片文本（标题 + 详情 + 建议）。</summary>
    public static string GetMessage(int errorCode)
    {
        if (_map.TryGetValue(errorCode, out var entry))
            return $"⚠️ {entry.Title}\n  详情：{entry.Detail}\n  建议：{entry.Action}";
        return $"⚠️ 未知错误 (0x{errorCode:X8})\n  详情：Win32 错误码 {errorCode}\n  建议：请查看日志文件后联系技术支持。";
    }

    /// <summary>获取错误标题（简短，适合 UI 卡片标题）。</summary>
    public static string GetTitle(int errorCode) =>
        _map.TryGetValue(errorCode, out var entry) ? entry.Title : $"未知错误 ({errorCode})";

    /// <summary>获取错误详情（适合 UI 卡片详情）。</summary>
    public static string GetDetail(int errorCode) =>
        _map.TryGetValue(errorCode, out var entry) ? entry.Detail : "未预期的 Win32 错误码。";

    /// <summary>获取操作建议（适合 UI 卡片建议）。</summary>
    public static string GetAction(int errorCode) =>
        _map.TryGetValue(errorCode, out var entry) ? entry.Action : "请查看日志文件后联系技术支持。";

    /// <summary>判断错误码是否表示设备断开（55 = 设备不存在）。</summary>
    public static bool IsDeviceDisconnected(int errorCode) => errorCode == 55;

    /// <summary>判断错误码是否允许重试（非断开、非取消的错误码可重试）。</summary>
    public static bool CanRetry(int errorCode) => errorCode is not (55 or 995);

    private readonly record struct ErrorEntry(string Title, string Detail, string Action);
}