using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Devices;

/// <summary>一个可被刷写的 USB MSC 磁盘设备。</summary>
public sealed record MscDeviceInfo(
    string DevicePath,
    ushort Vid,
    ushort Pid,
    string Description,
    string? VendorId = null,
    string? ProductId = null,
    bool IsTarget = false,
    string? VidStr = null,
    string? PidStr = null,
    DeviceEntry? MatchedEntry = null)
{
    /// <summary>
    /// 显示名称。设备使用十六进制 VID/PID（如 VID_1234&PID_5678）时显示十六进制；
    /// 否则（如 BuildWin 设备使用字符串标识 VEN_BUILDWIN/PROD_VIDEO050LOADER）显示字符串型标识。
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (Vid != 0 || Pid != 0)
                return $"{Description} (VID={Vid:X4} PID={Pid:X4})";
            if (!string.IsNullOrEmpty(VidStr) || !string.IsNullOrEmpty(PidStr))
                return $"{Description} (VID={VidStr} PID={PidStr})";
            return $"{Description} (VID=? PID=?)";
        }
    }
}
