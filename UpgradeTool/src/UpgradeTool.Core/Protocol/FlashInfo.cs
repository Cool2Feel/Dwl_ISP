namespace UpgradeTool.Core.Protocol;

/// <summary>
/// 设备端 Flash 信息（RDID + 容量）。
/// 容量通过 stub 的 l2_func_spi_read_capacity 从设备读取，是设备侧的真实大小。
/// </summary>
public sealed record FlashInfo(byte[] Id, uint CapacityBytes)
{
    /// <summary>RDID 文本（如 "EF 40 16"）。</summary>
    public string IdText => Id.Length == 0 ? "-" : string.Join(" ", Id.Select(b => $"{b:X2}"));

    /// <summary>容量文本（如 "4 MB"）。</summary>
    public string CapacityText
    {
        get
        {
            if (CapacityBytes == 0)
                return "未知";
            if (CapacityBytes >= 1024 * 1024)
                return $"{CapacityBytes / (1024 * 1024)} MB";
            return $"{CapacityBytes / 1024} KB";
        }
    }

    public override string ToString() => $"{IdText} / {CapacityText}";
}
