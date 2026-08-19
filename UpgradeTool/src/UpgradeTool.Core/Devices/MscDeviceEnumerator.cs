using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace UpgradeTool.Core.Devices;

/// <summary>
/// 单个磁盘接口的枚举结果（含被过滤的磁盘与跳过原因），用于诊断"设备为什么没被识别"。
/// </summary>
public sealed record MscDiskProbe(
    string DevicePath,
    ushort Vid,
    ushort Pid,
    string[] HardwareIds,
    StorageDeviceDescriptorInfo? Descriptor,
    string? SkipReason,
    string? VidStr = null,
    string? PidStr = null)
{
    /// <summary>该磁盘是否会被 Enumerate() 收录（SkipReason 为 null 时收录）。</summary>
    public bool IsIncluded => SkipReason == null;

    public MscDeviceInfo ToDeviceInfo()
    {
        string identity = Descriptor is { Identity.Length: > 0 }
            ? Descriptor.Identity
            : "USB 磁盘";
        DeviceSignature.DeviceRecognition rec =
            DeviceSignature.Recognize(Descriptor?.VendorId, Descriptor?.ProductId, Descriptor?.ProductRevision);
        return new MscDeviceInfo(DevicePath, Vid, Pid, identity,
            VendorId: Descriptor?.VendorId, ProductId: Descriptor?.ProductId,
            IsTarget: rec.IsTarget, MatchedEntry: rec.Entry,
            VidStr: VidStr, PidStr: PidStr);
    }
}

/// <summary>
/// 枚举 USB 磁盘设备。
/// 对齐参考项目 MPTool 的 SearchAllDevice：同时枚举 GUID_DEVINTERFACE_DISK
/// （{53F56307-B6BF-11D0-94F2-00A0C91EFB8B}）与 GUID_DEVINTERFACE_CDROM
/// （{53F56308-B6BF-11D0-94F2-00A0C91EFB8B}）两类 USBSTOR 接口，
/// 返回的接口路径可直接用 CreateFile 打开并支持 SCSI Pass-Through。
/// 对每个磁盘读取 STORAGE_DEVICE_DESCRIPTOR（IOCTL_STORAGE_QUERY_PROPERTY）：
/// 非 USB 总线、或描述符查询失败的磁盘被过滤（EnumerateProbes 保留原因供诊断）。
/// IsTarget 由描述符厂商/产品串匹配（DeviceSignature，参考 TimeUpdate），
/// 命中后连接握手（stub 上传 + Flash 查询成功）才真正识别设备。
/// 同一物理设备若同时注册两类接口，按 Vendor+Product+Revision 身份串去重，避免重复连接。
/// </summary>
public static class MscDeviceEnumerator
{
    private static readonly Guid DiskInterfaceGuid = new("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");
    private static readonly Guid CdromInterfaceGuid = new("53F56308-B6BF-11D0-94F2-00A0C91EFB8B");

    /// <summary>参与枚举的 USBSTOR 接口 GUID 列表（DISK + CDROM），对齐 MPTool SearchAllDevice。</summary>
    private static readonly Guid[] InterfaceGuids = { DiskInterfaceGuid, CdromInterfaceGuid };

    /// <summary>GUID_DEVINTERFACE_DISK，供外部（如 MainWindow）注册设备变更通知。</summary>
    public static Guid DiskClassGuid => DiskInterfaceGuid;

    /// <summary>GUID_DEVINTERFACE_CDROM，供外部（如 MainWindow）注册设备变更通知。</summary>
    public static Guid CdromClassGuid => CdromInterfaceGuid;

    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint SPDRP_HARDWAREID = 0x00000001;

    public static IReadOnlyList<MscDeviceInfo> Enumerate(Action<string>? log = null)
        => EnumerateProbes(log).Where(p => p.IsIncluded).Select(p => p.ToDeviceInfo()).ToList();

    /// <summary>
    /// 枚举所有磁盘接口（不做过滤），逐个读取描述符并标记跳过原因。
    /// SkipReason 可能的取值：
    ///   描述符查询失败（Win32 错误码 N）——打开/ioctl 失败，常见于非管理员权限或设备被占用；
    ///   非 USB 总线（BusType=X）——系统盘/内置盘，不是相机目标。
    /// </summary>
    public static IReadOnlyList<MscDiskProbe> EnumerateProbes(Action<string>? log = null)
    {
        var result = new List<MscDiskProbe>();
        int totalInterfaces = 0;
        int pathNull = 0;

        // 对齐 MPTool SearchAllDevice：遍历 DISK + CDROM 两类 USBSTOR 接口，覆盖全部可能的设备形态。
        foreach (Guid classGuid in InterfaceGuids)
        {
            Guid guid = classGuid;
            IntPtr deviceInfoSet = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
                continue;

            try
            {
                uint index = 0;
                while (true)
                {
                    var ifaceData = new SpDeviceInterfaceData { cbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>() };
                    if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref guid, index, ref ifaceData))
                        break;

                    var devInfoData = new SpDevInfoData { cbSize = (uint)Marshal.SizeOf<SpDevInfoData>() };
                    string? path = GetDeviceInterfacePath(deviceInfoSet, ref ifaceData, ref devInfoData, log);
                    if (path != null)
                    {
                        string[] hwIds = GetHardwareIds(deviceInfoSet, ref devInfoData);
                        var (vid, pid, vidStr, pidStr) = ParseVidPid(hwIds, path);

                        // 优先使用 USB 设备树中上层的真实十六进制 VID/PID（如 VID_1234&PID_5678），
                        // 替代磁盘节点可能返回的字符串标识（如 VEN_BUILDWIN）。
                        var treeVidPid = GetNumericVidPidFromDeviceTree(devInfoData.DevInst);
                        if (treeVidPid != null)
                        {
                            vid = treeVidPid.Value.Vid;
                            pid = treeVidPid.Value.Pid;
                            vidStr = null;
                            pidStr = null;
                        }

                        StorageDescriptorQueryResult query = StorageDescriptor.Query(path);
                        string? skipReason;
                        if (!query.Ok)
                        {
                            skipReason = query.FailureReason;
                        }
                        else if (!query.Info!.IsUsb)
                        {
                            skipReason = $"非 USB 总线（BusType={query.Info.BusTypeName}）";
                        }
                        else
                        {
                            skipReason = null;
                        }

                        result.Add(new MscDiskProbe(path, vid, pid, hwIds, query.Info, skipReason, vidStr, pidStr));
                    }
                    else
                    {
                        pathNull++;
                    }

                    index++;
                    totalInterfaces++;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }

        // 同一物理设备的 DISK/CDROM 两类接口可能解析出相同 Vendor+Product+Revision，
        // 按身份串去重，保留首个（通常 DISK），避免 DeviceWatcher 对其建立重复连接。
        result = DeduplicateByIdentity(result);

        log?.Invoke($"设备枚举: 共 {totalInterfaces} 个接口（DISK+CDROM），候选 {result.Count(p => p.IsIncluded)}，路径获取失败 {pathNull}。");
        return result;
    }

    /// <summary>按 Vendor+Product+Revision 身份串去重；身份相同的多条记录仅保留第一条。</summary>
    private static List<MscDiskProbe> DeduplicateByIdentity(List<MscDiskProbe> probes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<MscDiskProbe>(probes.Count);
        foreach (MscDiskProbe probe in probes)
        {
            string identity = probe.Descriptor is { VendorId: { }, ProductId: { } }
                ? $"{probe.Descriptor.VendorId}|{probe.Descriptor.ProductId}|{probe.Descriptor.ProductRevision ?? ""}"
                : probe.DevicePath;
            if (seen.Add(identity))
                deduped.Add(probe);
        }
        return deduped;
    }

    private static string? GetDeviceInterfacePath(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData ifaceData,
        ref SpDevInfoData devInfoData,
        Action<string>? log = null)
    {
        // 第一步：询问所需缓冲区大小。
        // 标准用法：传入 NULL/0，函数返回 false + ERROR_INSUFFICIENT_BUFFER，同时填充 requiredSize。
        // 因此 !firstOk 是预期行为，不能作为失败判断；仅当 requiredSize == 0 时才真正失败。
        SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref ifaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);
        if (requiredSize == 0)
        {
            log?.Invoke($"获取磁盘接口路径失败(第一步): requiredSize=0, err={Marshal.GetLastWin32Error()}");
            return null;
        }

        IntPtr detailPtr = Marshal.AllocHGlobal((int)requiredSize);
        IntPtr devInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SpDevInfoData>());
        try
        {
            // cbSize 必须等于 sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W)。
            // C 结构 { DWORD cbSize; WCHAR DevicePath[ANYSIZE_ARRAY]; } 按 4 字节对齐后为 8 字节
            // （cbSize 4 + 2 字节 DevicePath 填充到 8），传 4 会触发 ERROR_INVALID_USER_BUFFER(1784)。
            Marshal.WriteInt32(detailPtr, 8);
            // DeviceInfoData 参数非空时由驱动回填 SP_DEVINFO_DATA（DevInst），
            // 供后续 SetupDiGetDeviceRegistryProperty 读取硬件 ID（VID/PID）。
            Marshal.WriteInt32(devInfoPtr, Marshal.SizeOf<SpDevInfoData>());

            if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref ifaceData, detailPtr, requiredSize, out _, devInfoPtr))
            {
                log?.Invoke($"获取磁盘接口路径失败(第二步): requiredSize={requiredSize}, err={Marshal.GetLastWin32Error()}");
                return null;
            }

            string path = Marshal.PtrToStringUni(IntPtr.Add(detailPtr, 4)) ?? string.Empty;
            if (string.IsNullOrEmpty(path))
                log?.Invoke($"获取磁盘接口路径成功但返回空字符串。");
            devInfoData = Marshal.PtrToStructure<SpDevInfoData>(devInfoPtr);
            return string.IsNullOrEmpty(path) ? null : path;
        }
        finally
        {
            Marshal.FreeHGlobal(detailPtr);
            Marshal.FreeHGlobal(devInfoPtr);
        }
    }

    private static string[] GetHardwareIds(IntPtr deviceInfoSet, ref SpDevInfoData devInfoData)
    {
        SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref devInfoData, SPDRP_HARDWAREID, out _, IntPtr.Zero, 0, out uint requiredSize);
        if (requiredSize == 0)
            return Array.Empty<string>();

        IntPtr buffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            if (!SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref devInfoData, SPDRP_HARDWAREID, out _, buffer, requiredSize, out _))
                return Array.Empty<string>();

            var ids = new List<string>();
            int offset = 0;
            while (offset < requiredSize)
            {
                string? s = Marshal.PtrToStringUni(IntPtr.Add(buffer, offset));
                if (string.IsNullOrEmpty(s))
                    break;
                ids.Add(s);
                offset += (s.Length + 1) * 2;
            }
            return ids.ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// 从硬件 ID 解析 VID/PID。
    /// 优先解析标准十六进制 VID/PID（例：USB\VID_1234&amp;PID_5678 或 USBSTOR\DISK&amp;VEN_1234&amp;PROD_5678），
    /// 返回 ushort 值；若设备使用字符串型标识（如 BuildWin 的 VEN_BUILDWIN/PROD_VIDEO050LOADER），
    /// 则提取字符串并通过 VidStr/PidStr 返回，供显示使用。
    /// devicePath 作为备用解析源（设备接口路径中可能包含 ven_xxx&prod_yyy 标识）。
    /// </summary>
    public static (ushort Vid, ushort Pid, string? VidStr, string? PidStr) ParseVidPid(IEnumerable<string> hardwareIds, string? devicePath = null)
    {
        foreach (string hwId in hardwareIds)
        {
            int vidIdx = hwId.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
            int pidIdx = hwId.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
            int venIdx = hwId.IndexOf("VEN_", StringComparison.OrdinalIgnoreCase);
            int prodIdx = hwId.IndexOf("PROD_", StringComparison.OrdinalIgnoreCase);

            // 标准 USB 硬件 ID：VID_XXXX&PID_XXXX（十六进制）
            if (vidIdx >= 0 && pidIdx >= 0 &&
                TryParseHex4(hwId, vidIdx + 4, out ushort vid) && TryParseHex4(hwId, pidIdx + 4, out ushort pid))
                return (vid, pid, null, null);

            // USBSTOR 硬件 ID：VEN_XXXX&PROD_YYYY。优先尝试十六进制（标准 USB 设备），
            // 失败则回退为字符串标识（自定义设备，如 BuildWin）。
            if (venIdx >= 0 && prodIdx >= 0)
            {
                string ven = ExtractToken(hwId, venIdx + 4);
                string prod = ExtractToken(hwId, prodIdx + 5);
                if (TryParseHex4(hwId, venIdx + 4, out ushort venHex) && TryParseHex4(hwId, prodIdx + 5, out ushort prodHex))
                    return (venHex, prodHex, null, null);
                if (ven.Length > 0 || prod.Length > 0)
                    return (0, 0, ven, prod);
            }
        }

        // 备用：从设备接口路径解析（如 \?\usbstor#disk&ven_buildwin&prod_video050loader&rev_1.00#...）
        // 硬件 ID 可能因驱动或注册表问题不包含 VEN_/PROD_，但路径中一定包含。
        if (devicePath != null)
        {
            var (vidFromPath, pidFromPath) = ParseVidPidFromPath(devicePath);
            if (vidFromPath != null || pidFromPath != null)
                return (0, 0, vidFromPath, pidFromPath);
        }

        return (0, 0, null, null);
    }

    /// <summary>从设备接口路径中提取 ven_xxx&prod_yyy 标识（如 usbstor#disk&ven_buildwin&prod_video050loader）。</summary>
    private static (string? Vid, string? Pid) ParseVidPidFromPath(string devicePath)
    {
        // 路径格式：\?\usbstor#disk&ven_xxx&prod_yyy&rev_zzz#...
        // 提取 & 分隔的键值对，查找 ven_ 和 prod_ 前缀
        int hashIdx = devicePath.IndexOf('#');
        if (hashIdx < 0) return (null, null);

        // 从第一个 # 之后查找 ven_ 和 prod_
        string segment = devicePath.Substring(hashIdx + 1);
        string[] parts = segment.Split('&', '#');
        string? ven = null, prod = null;
        foreach (string part in parts)
        {
            if (part.StartsWith("ven_", StringComparison.OrdinalIgnoreCase))
                ven = part.Substring(4);
            else if (part.StartsWith("prod_", StringComparison.OrdinalIgnoreCase))
                prod = part.Substring(5);
        }
        return (ven, prod);
    }

    /// <summary>提取从 start 开始到下一个 '&' 或字符串结束的标识符。</summary>
    private static string ExtractToken(string s, int start)
    {
        if (start < 0 || start >= s.Length)
            return string.Empty;
        int end = s.IndexOf('&', start);
        int len = end < 0 ? s.Length - start : end - start;
        return s.Substring(start, len);
    }

    private static bool TryParseHex4(string s, int start, out ushort value)
    {
        value = 0;
        if (start < 0 || start + 4 > s.Length)
            return false;
        return ushort.TryParse(s.AsSpan(start, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    #region P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr reserved,
        uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        uint property,
        out uint regDataType,
        IntPtr buffer,
        uint bufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    #endregion

    #region cfgmgr32 (device tree walking)

    [DllImport("cfgmgr32.dll", SetLastError = true)]
    private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, int ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int CM_Get_Device_ID(uint dnDevInst, StringBuilder buffer, int bufferLen, int ulFlags);

    /// <summary>从设备树向上遍历，查找根 USB 设备节点的真实十六进制 VID/PID。</summary>
    private static (ushort Vid, ushort Pid)? GetNumericVidPidFromDeviceTree(uint devInst)
    {
        uint current = devInst;
        for (int i = 0; i < 10; i++)
        {
            if (CM_Get_Parent(out uint parent, current, 0) != 0)
                break;
            current = parent;
            var sb = new StringBuilder(256);
            if (CM_Get_Device_ID(current, sb, sb.Capacity, 0) != 0)
                continue;
            string id = sb.ToString();
            int vidIdx = id.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
            int pidIdx = id.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
            if (vidIdx >= 0 && pidIdx >= 0 &&
                TryParseHex4(id, vidIdx + 4, out ushort vid) && TryParseHex4(id, pidIdx + 4, out ushort pid))
                return (vid, pid);
        }
        return null;
    }

    #endregion
}
