using System.Runtime.InteropServices;
using UpgradeTool.Core.Interop;
using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Devices;

/// <summary>
/// 磁盘设备描述符信息（由 IOCTL_STORAGE_QUERY_PROPERTY 查询得到）。
/// VendorId/ProductId 为描述符中的字符串（来自 SCSI INQUIRY 的厂商/产品字段，已去除尾部空格）。
/// BusTypeCode 为 STORAGE_BUS_TYPE 原始值（如 USB=0x07）。
/// </summary>
public sealed record StorageDeviceDescriptorInfo(
    bool IsUsb,
    int BusTypeCode,
    bool RemovableMedia,
    string? VendorId,
    string? ProductId,
    string? ProductRevision)
{
    public string BusTypeName => BusTypeCode switch
    {
        0x00 => "Unknown",
        0x01 => "SCSI",
        0x02 => "ATAPI",
        0x03 => "ATA",
        0x04 => "1394",
        0x05 => "SSA",
        0x06 => "Fibre",
        0x07 => "USB",
        0x08 => "RAID",
        0x09 => "iSCSI",
        0x0A => "SAS",
        0x0B => "SATA",
        0x0C => "SD",
        0x0D => "MMC",
        0x0E => "MAX",
        0x0F => "Virtual",
        0x10 => "FileBackedVirtual",
        _ => $"{BusTypeCode}",
    };

    public string Identity => string.Join(" ", new[] { VendorId, ProductId, ProductRevision }
        .Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>描述符查询结果（携带失败原因，供诊断）。</summary>
public sealed record StorageDescriptorQueryResult(bool Ok, int Win32Error, StorageDeviceDescriptorInfo? Info)
{
    public string? FailureReason => Ok
        ? null
        : $"描述符查询失败（Win32 错误码 {Win32Error}）";
}

/// <summary>
/// 设备签名匹配：判断磁盘描述符是否属于本工具的目标相机设备。
/// 识别方式对齐参考项目 MPTool 的 SearchDeviceID（DeviceLib.ini InquiryInfo 匹配）：
///   1) DeviceLib.ini（设备库）按 Vendor+Product+Revision 拼接身份串前缀匹配（config-driven，新增产品只需改 ini）；
///   2) 未列入设备库的旧产品回退到内置 pattern 匹配（参考 TimeUpdate 的 INQUIRY 厂商/产品串，不区分大小写）。
/// </summary>
public static class DeviceSignature
{
    private static readonly string[] TargetPatterns = { "buildwin", "ax3231mp", "minidv" };

    /// <summary>设备识别结果：是否目标设备 + 命中的 DeviceLib.ini 设备库条目（无则 null）。</summary>
    public sealed record DeviceRecognition(bool IsTarget, DeviceEntry? Entry);

    /// <summary>
    /// 识别设备身份（对齐 MPTool SearchDeviceID）：
    ///   1) 内置 pattern（buildwin/ax3231mp/minidv）命中即判为目标；
    ///   2) 否则按 DeviceLib.ini 设备库（Vendor+Product+Revision 拼接身份串前缀匹配）识别，
    ///      命中的条目带回 ClassInfo/SpiDriverPath/Isp 等配置。
    /// 返回结果同时给出 IsTarget 与命中的 DeviceEntry，供上层（适配器工厂 / 驱动选择）
    /// 复用同一份识别结论，避免重复匹配（MPTool 中 SearchDeviceID 一次性回填 SpiDriverPath/ClassInfo）。
    /// </summary>
    public static DeviceRecognition Recognize(string? vendorId, string? productId, string? productRevision = null)
    {
        string combined = string.Join(" ", new[] { vendorId, productId }
            .Where(s => !string.IsNullOrWhiteSpace(s)))
            .Trim()
            .ToLowerInvariant();
        if (TargetPatterns.Any(pattern => combined.Contains(pattern, StringComparison.Ordinal)))
            return new DeviceRecognition(true, TryMatchLibrary(vendorId, productId, productRevision));

        DeviceEntry? entry = TryMatchLibrary(vendorId, productId, productRevision);
        return new DeviceRecognition(entry != null, entry);
    }

    /// <summary>按 DeviceLib.ini 设备库匹配；内嵌资源缺失或异常时返回 null（回退为纯 pattern 匹配）。
    /// 使用缓存的 Embedded 实例，避免每台设备每次扫描都重新解析 INI。</summary>
    private static DeviceEntry? TryMatchLibrary(string? vendorId, string? productId, string? productRevision)
    {
        try
        {
            return DeviceLibrary.Embedded.MatchIdentity(vendorId, productId, productRevision);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>匹配 DC503J 等 Buildwin 相机（描述符厂商/产品串）。</summary>
    public static bool IsTarget(string? vendorId, string? productId, string? productRevision = null)
        => Recognize(vendorId, productId, productRevision).IsTarget;

    /// <summary>检测 Loader/Bootloader 模式设备（描述符厂商/产品串含 "loader"）。</summary>
    public static bool IsLoader(string? vendorId, string? productId)
    {
        string combined = string.Join(" ", new[] { vendorId, productId }
            .Where(s => !string.IsNullOrWhiteSpace(s)))
            .Trim();
        return combined.Contains("loader", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 通过 IOCTL_STORAGE_QUERY_PROPERTY 读取磁盘设备描述符。
/// 打开磁盘接口路径（dwDesiredAccess=0，无访问权也能执行 FILE_ANY_ACCESS 的缓冲 ioctl，
/// 非管理员下 GENERIC_READ/WRITE 打开磁盘会 ACCESS_DENIED），
/// 下发 StorageDeviceProperty 查询，从返回的 STORAGE_DEVICE_DESCRIPTOR 提取
/// BusType / VendorId / ProductId / ProductRevision。失败时携带 Win32 错误码。
/// </summary>
public static class StorageDescriptor
{
    private const int DescriptorBufferSize = 1024;

    public static StorageDescriptorQueryResult Query(string devicePath)
    {
        IntPtr handle = MscScsiNative.CreateFile(
            devicePath,
            0, // 无访问权即可执行 METHOD_BUFFERED 的 STORAGE_QUERY_PROPERTY（FILE_ANY_ACCESS）；
               // 非管理员下 GENERIC_READ/WRITE 打开磁盘会 ERROR_ACCESS_DENIED(5)
            MscScsiNative.FILE_SHARE_READ | MscScsiNative.FILE_SHARE_WRITE,
            IntPtr.Zero,
            MscScsiNative.OPEN_EXISTING,
            MscScsiNative.FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            int error = Marshal.GetLastWin32Error();
            return new StorageDescriptorQueryResult(false, error, null);
        }

        try
        {
            var query = new StoragePropertyNative.StoragePropertyQuery
            {
                PropertyId = StoragePropertyNative.StorageDeviceProperty,
                QueryType = StoragePropertyNative.PropertyStandardQuery,
            };

            IntPtr queryPtr = Marshal.AllocHGlobal(Marshal.SizeOf<StoragePropertyNative.StoragePropertyQuery>());
            uint bufferSize = DescriptorBufferSize;
            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
            try
            {
                Marshal.StructureToPtr(query, queryPtr, false);

                if (!MscScsiNative.DeviceIoControl(
                        handle,
                        StoragePropertyNative.IOCTL_STORAGE_QUERY_PROPERTY,
                        queryPtr,
                        (uint)Marshal.SizeOf<StoragePropertyNative.StoragePropertyQuery>(),
                        buffer,
                        bufferSize,
                        out _,
                        IntPtr.Zero))
                {
                    int error = Marshal.GetLastWin32Error();
                    return new StorageDescriptorQueryResult(false, error, null);
                }

                var desc = Marshal.PtrToStructure<StoragePropertyNative.StorageDeviceDescriptor>(buffer);
                if (desc.Size == 0)
                    return new StorageDescriptorQueryResult(false, 0, null);

                // 若驱动返回的描述符（含厂商/产品字符串）大于首查缓冲，按实际 Size 扩容重查一次，避免字符串被截断
                if (desc.Size > bufferSize)
                {
                    Marshal.FreeHGlobal(buffer);
                    bufferSize = desc.Size;
                    buffer = Marshal.AllocHGlobal((int)bufferSize);
                    if (!MscScsiNative.DeviceIoControl(
                            handle,
                            StoragePropertyNative.IOCTL_STORAGE_QUERY_PROPERTY,
                            queryPtr,
                            (uint)Marshal.SizeOf<StoragePropertyNative.StoragePropertyQuery>(),
                            buffer,
                            bufferSize,
                            out _,
                            IntPtr.Zero))
                    {
                        int error = Marshal.GetLastWin32Error();
                        return new StorageDescriptorQueryResult(false, error, null);
                    }
                    desc = Marshal.PtrToStructure<StoragePropertyNative.StorageDeviceDescriptor>(buffer);
                }

                // BusType 恒有值（据此判断是否 USB）；厂商/产品串可能为空（系统盘常缺 INQUIRY 字符串）
                return new StorageDescriptorQueryResult(
                    true,
                    0,
                    new StorageDeviceDescriptorInfo(
                        IsUsb: desc.BusType == StoragePropertyNative.StorageBusType.BusTypeUsb,
                        BusTypeCode: (int)desc.BusType,
                        RemovableMedia: desc.RemovableMedia,
                        VendorId: ReadAnsiString(buffer, bufferSize, desc.VendorIdOffset),
                        ProductId: ReadAnsiString(buffer, bufferSize, desc.ProductIdOffset),
                        ProductRevision: ReadAnsiString(buffer, bufferSize, desc.ProductRevisionOffset)));
            }
            finally
            {
                Marshal.FreeHGlobal(queryPtr);
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            MscScsiNative.CloseHandle(handle);
        }
    }

    private static string? ReadAnsiString(IntPtr buffer, uint bufferSize, uint offset)
    {
        // offset 越界（如描述符被截断）时返回 null，避免读取未分配内存
        if (offset == 0 || offset >= bufferSize)
            return null;
        IntPtr p = IntPtr.Add(buffer, (int)offset);
        string? s = Marshal.PtrToStringAnsi(p);
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
