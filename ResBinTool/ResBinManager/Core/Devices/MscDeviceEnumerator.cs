using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ResBinManager.Core.Devices
{
    /// <summary>
    /// 单个磁盘接口的枚举结果（含被过滤的磁盘与跳过原因），用于诊断"设备为什么没被识别"。
    /// </summary>
    public sealed class MscDiskProbe
    {
        public string DevicePath { get; }

        public ushort Vid { get; }

        public ushort Pid { get; }

        public string[] HardwareIds { get; }

        public StorageDeviceDescriptorInfo? Descriptor { get; }

        /// <summary>该磁盘未收录的原因；为 null 时会被 <see cref="MscDeviceEnumerator.Enumerate"/> 收录。</summary>
        public string? SkipReason { get; }

        public string? VidStr { get; }

        public string? PidStr { get; }

        public MscDiskProbe(
            string devicePath,
            ushort vid,
            ushort pid,
            string[] hardwareIds,
            StorageDeviceDescriptorInfo? descriptor,
            string? skipReason,
            string? vidStr = null,
            string? pidStr = null)
        {
            DevicePath = devicePath;
            Vid = vid;
            Pid = pid;
            HardwareIds = hardwareIds;
            Descriptor = descriptor;
            SkipReason = skipReason;
            VidStr = vidStr;
            PidStr = pidStr;
        }

        /// <summary>该磁盘是否会被 <see cref="MscDeviceEnumerator.Enumerate"/> 收录（SkipReason 为 null 时收录）。</summary>
        public bool IsIncluded => SkipReason == null;

        public MscDeviceInfo ToDeviceInfo()
        {
            string identity = Descriptor != null && Descriptor.Identity.Length > 0
                ? Descriptor.Identity
                : "USB 磁盘";
            return new MscDeviceInfo(DevicePath, Vid, Pid, identity,
                vendorId: Descriptor?.VendorId,
                productId: Descriptor?.ProductId,
                isTarget: DeviceSignature.IsTargetVidPid(Vid, Pid)
                          || DeviceSignature.IsTargetVendorProduct(Descriptor?.VendorId, Descriptor?.ProductId),
                vidStr: VidStr,
                pidStr: PidStr);
        }
    }

    /// <summary>
    /// 枚举 USB 磁盘设备。
    /// 使用 GUID_DEVINTERFACE_DISK（{53F56307-B6BF-11D0-94F2-00A0C91EFB8B}）接口枚举，
    /// 返回的接口路径可直接用 CreateFile 打开并支持 SCSI Pass-Through。
    /// 对每个磁盘读取 STORAGE_DEVICE_DESCRIPTOR（IOCTL_STORAGE_QUERY_PROPERTY）：
    /// 非 USB 总线、或描述符查询失败的磁盘被过滤（EnumerateProbes 保留原因供诊断）。
    /// IsTarget 由 VID/PID + 描述符厂商/产品串匹配（DeviceSignature，参考 UpgradeTool 项目），
    /// 命中后连接握手（SCSI 探针通过）才真正识别设备。
    /// </summary>
    public static class MscDeviceEnumerator
    {
        private static readonly Guid DiskInterfaceGuid = new Guid("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");

        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
        private const uint SPDRP_HARDWAREID = 0x00000001;

        public static IReadOnlyList<MscDeviceInfo> Enumerate(Action<string>? log = null)
        {
            // 主枚举：SetupAPI GUID_DEVINTERFACE_DISK（不依赖管理员权限，推荐的现代方法）
            // 保留描述符查询失败的设备（SkipReason 含"描述符查询失败"），其设备路径仍可用于 SCSI INQUIRY 探测。
            // 仅排除"非 USB 总线"的设备（系统盘/内置盘）。
            List<MscDeviceInfo> devices = EnumerateProbes(log)
                .Where(p => p.IsIncluded || (p.SkipReason != null && !p.SkipReason.StartsWith("非 USB")))
                .Select(p => p.ToDeviceInfo())
                .ToList();

            // 回退枚举：SetupAPI 未找到目标设备时，尝试 \\.\PHYSICALDRIVE{0..126}
            // （与参考项目 TimeUpdate.OpenTheDrv 的枚举方式一致，覆盖某些驱动未注册为磁盘接口的设备）
            if (devices.Count == 0 || !devices.Any(d => d.IsTarget))
            {
                var fallback = EnumeratePhysicalDrives(log);
                // 仅合并 fallback 中新增的设备（按路径去重）
                foreach (MscDeviceInfo dev in fallback)
                {
                    if (!devices.Any(d => string.Equals(d.DevicePath, dev.DevicePath, StringComparison.OrdinalIgnoreCase)))
                        devices.Add(dev);
                }
            }

            return devices;
        }

        /// <summary>
        /// 回退枚举：扫描 \\.\PHYSICALDRIVE{0..126} 物理磁盘（与参考项目 TimeUpdate.OpenTheDrv 一致）。
        /// 每个磁盘通过 IOCTL_STORAGE_QUERY_PROPERTY 读取描述符，
        /// 检查 BusType==USB 且厂商/产品串匹配目标设备。
        /// 当 SetupAPI 枚举因驱动/权限原因未找到目标设备时作为兜底。
        /// </summary>
        private static List<MscDeviceInfo> EnumeratePhysicalDrives(Action<string>? log = null)
        {
            var result = new List<MscDeviceInfo>();
            string pattern = "\\\\.\\PHYSICALDRIVE{0}";

            for (int i = 0; i < 127; i++)
            {
                string path = string.Format(pattern, i);
                StorageDescriptorQueryResult query = StorageDescriptor.Query(path);

                // 查询失败且错误码为 2(FILE_NOT_FOUND)/3(PATH_NOT_FOUND) 表示路径不存在，跳过
                if (!query.Ok && (query.Win32Error == 2 || query.Win32Error == 3))
                    continue;

                // 查询成功但非 USB 总线，跳过
                if (query.Ok && !query.Info!.IsUsb)
                    continue;

                // 查询成功：从描述符提取厂商/产品串
                // 查询失败但路径存在：保留设备路径供 SCSI INQUIRY 兜底识别
                string? vendorId = query.Ok ? query.Info!.VendorId : null;
                string? productId = query.Ok ? query.Info!.ProductId : null;

                string hwId = $"USBSTOR\\DISK&VEN_{vendorId ?? "?"}&PROD_{productId ?? "?"}";
                (ushort vid, ushort pid, _, _) = MscDeviceEnumerator.ParseVidPid(new[] { hwId });

                bool isTarget = DeviceSignature.IsTargetVidPid(vid, pid)
                                || DeviceSignature.IsTargetVendorProduct(vendorId, productId);

                string identity = query.Ok && query.Info!.Identity.Length > 0
                    ? query.Info.Identity
                    : $"PhysicalDrive{i}";

                result.Add(new MscDeviceInfo(
                    path, vid, pid, identity,
                    vendorId: vendorId,
                    productId: productId,
                    isTarget: isTarget));
            }

            if (result.Count > 0)
                log?.Invoke($"PHYSICALDRIVE 回退枚举: 共 {result.Count} 个磁盘，{result.Count(d => d.IsTarget)} 个匹配目标");

            return result;
        }

        /// <summary>
        /// 枚举所有磁盘接口（不做过滤），逐个读取描述符并标记跳过原因。
        /// SkipReason 可能的取值：
        ///   描述符查询失败（Win32 错误码 N）——打开/ioctl 失败，常见于非管理员权限或设备被占用；
        ///   非 USB 总线（BusType=X）——系统盘/内置盘，不是相机目标。
        /// </summary>
        public static IReadOnlyList<MscDiskProbe> EnumerateProbes(Action<string>? log = null)
        {
            var result = new List<MscDiskProbe>();

            Guid classGuid = DiskInterfaceGuid;
            IntPtr deviceInfoSet = SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
            {
                log?.Invoke("设备枚举: SetupDiGetClassDevs 失败。");
                return result;
            }

            try
            {
                uint index = 0;
                int pathNull = 0;
                while (true)
                {
                    var ifaceData = new SpDeviceInterfaceData { cbSize = (uint)Marshal.SizeOf(typeof(SpDeviceInterfaceData)) };
                    if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref classGuid, index, ref ifaceData))
                        break;

                    var devInfoData = new SpDevInfoData { cbSize = (uint)Marshal.SizeOf(typeof(SpDevInfoData)) };
                    string? path = GetDeviceInterfacePath(deviceInfoSet, ref ifaceData, ref devInfoData, log);
                    if (path != null)
                    {
                        string[] hwIds = GetHardwareIds(deviceInfoSet, ref devInfoData);
                        (ushort vid, ushort pid, string? vidStr, string? pidStr) = ParseVidPid(hwIds, path);

                        // 优先使用 USB 设备树中上层的真实十六进制 VID/PID（如 VID_1908&PID_3319），
                        // 替代磁盘节点可能返回的字符串标识（如 VEN_BUILDWIN）。
                        (ushort Vid, ushort Pid)? treeVidPid = GetNumericVidPidFromDeviceTree(devInfoData.DevInst);
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
                }
                log?.Invoke($"设备枚举: 共 {index} 个磁盘接口，候选 {result.Count(p => p.IsIncluded)}，路径获取失败 {pathNull}。");
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }

            return result;
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
            IntPtr devInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SpDevInfoData)));
            try
            {
                // cbSize 必须等于 sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W)。
                // C 结构 { DWORD cbSize; WCHAR DevicePath[ANYSIZE_ARRAY]; } 按 4 字节对齐后为 8 字节
                // （cbSize 4 + 2 字节 DevicePath 填充到 8），传 4 会触发 ERROR_INVALID_USER_BUFFER(1784)。
                int detailCbSize = IntPtr.Size == 8 ? 8 : 6;
                Marshal.WriteInt32(detailPtr, detailCbSize);
                // DeviceInfoData 参数非空时由驱动回填 SP_DEVINFO_DATA（DevInst），
                // 供后续 SetupDiGetDeviceRegistryProperty 读取硬件 ID（VID/PID）。
                Marshal.WriteInt32(devInfoPtr, Marshal.SizeOf(typeof(SpDevInfoData)));

                if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref ifaceData, detailPtr, requiredSize, out _, devInfoPtr))
                {
                    log?.Invoke($"获取磁盘接口路径失败(第二步): requiredSize={requiredSize}, err={Marshal.GetLastWin32Error()}");
                    return null;
                }

                string path = Marshal.PtrToStringUni(IntPtr.Add(detailPtr, 4)) ?? string.Empty;
                if (string.IsNullOrEmpty(path))
                    log?.Invoke("获取磁盘接口路径成功但返回空字符串。");
                devInfoData = (SpDevInfoData)Marshal.PtrToStructure(devInfoPtr, typeof(SpDevInfoData));
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
        /// 优先解析标准十六进制 VID/PID（例：USB\VID_1908&PID_3319 或 USBSTOR\DISK&VEN_1908&PROD_3319），
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
                (string? vidFromPath, string? pidFromPath) = ParseVidPidFromPath(devicePath);
                if (vidFromPath != null || pidFromPath != null)
                    return (0, 0, vidFromPath, pidFromPath);
            }

            return (0, 0, null, null);
        }

        /// <summary>从设备接口路径中提取 ven_xxx&amp;prod_yyy 标识（如 usbstor#disk&amp;ven_buildwin&amp;prod_video050loader）。</summary>
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

        /// <summary>提取从 start 开始到下一个 '&amp;' 或字符串结束的标识符。</summary>
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
            return ushort.TryParse(s.Substring(start, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
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
}
