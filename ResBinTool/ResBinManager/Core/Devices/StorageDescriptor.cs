using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace ResBinManager.Core.Devices
{
    /// <summary>
    /// 磁盘设备描述符信息（由 IOCTL_STORAGE_QUERY_PROPERTY 查询得到）。
    /// VendorId/ProductId 为描述符中的字符串（来自 SCSI INQUIRY 的厂商/产品字段，已去除尾部空格）。
    /// BusTypeCode 为 STORAGE_BUS_TYPE 原始值（如 USB=0x07）。
    /// </summary>
    public sealed class StorageDeviceDescriptorInfo
    {
        public bool IsUsb { get; }

        public int BusTypeCode { get; }

        public bool RemovableMedia { get; }

        public string? VendorId { get; }

        public string? ProductId { get; }

        public string? ProductRevision { get; }

        public StorageDeviceDescriptorInfo(
            bool isUsb,
            int busTypeCode,
            bool removableMedia,
            string? vendorId,
            string? productId,
            string? productRevision)
        {
            IsUsb = isUsb;
            BusTypeCode = busTypeCode;
            RemovableMedia = removableMedia;
            VendorId = vendorId;
            ProductId = productId;
            ProductRevision = productRevision;
        }

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
            _ => BusTypeCode.ToString(),
        };

        public string Identity => string.Join(" ", new[] { VendorId, ProductId, ProductRevision }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>描述符查询结果（携带失败原因，供诊断）。</summary>
    public sealed class StorageDescriptorQueryResult
    {
        public bool Ok { get; }

        public int Win32Error { get; }

        public StorageDeviceDescriptorInfo? Info { get; }

        public StorageDescriptorQueryResult(bool ok, int win32Error, StorageDeviceDescriptorInfo? info)
        {
            Ok = ok;
            Win32Error = win32Error;
            Info = info;
        }

        public string? FailureReason => Ok
            ? null
            : $"描述符查询失败（Win32 错误码 {Win32Error}）";
    }

    /// <summary>
    /// 设备签名匹配：判断磁盘描述符是否属于本工具的目标 HM020F 相机设备。
    /// 匹配方式参考 TimeUpdate / UpgradeTool 参考项目的设备识别
    /// （INQUIRY 厂商/产品串匹配 + VID/PID 匹配，不区分大小写）。
    /// </summary>
    public static class DeviceSignature
    {
        /// <summary>目标设备 VID（0x1908）。</summary>
        public const ushort TargetVid = 0x1908;

        /// <summary>目标设备 PID 列表：0x3319=HM020F，0x3283=Buildwin Media-Player。</summary>
        public static readonly ushort[] TargetPids = { 0x3319, 0x3283 };

        /// <summary>
        /// 厂商/产品串匹配关键字（不区分大小写，子串匹配）。
        /// 兼容参考项目 TimeUpdate.OpenTheDrv 的厂商串识别：
        ///   "buildwin minidv" / "ax3231mptool" / "buildwinmedia-player" / "generic"
        /// 其中 "buildwin" 覆盖前两者，"ax329" 覆盖 "ax3231mptool"，"generic" 为参考项目独有关键字。
        /// </summary>
        private static readonly string[] TargetPatterns = { "buildwin", "ax329", "generic" };

        /// <summary>按 VID/PID 精确匹配目标设备。</summary>
        public static bool IsTargetVidPid(ushort vid, ushort pid)
            => vid == TargetVid && TargetPids.Contains(pid);

        /// <summary>匹配 HM020F 等 Buildwin 相机（描述符厂商/产品串）。</summary>
        public static bool IsTargetVendorProduct(string? vendorId, string? productId)
        {
            string combined = string.Join(" ", new[] { vendorId, productId }
                .Where(s => !string.IsNullOrWhiteSpace(s)))
                .Trim()
                .ToLowerInvariant();
            if (combined.Length == 0)
                return false;
            foreach (string pattern in TargetPatterns)
            {
                if (combined.IndexOf(pattern, StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
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
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        public static StorageDescriptorQueryResult Query(string devicePath)
        {
            IntPtr handle = CreateFile(
                devicePath,
                0, // 无访问权即可执行 METHOD_BUFFERED 的 STORAGE_QUERY_PROPERTY（FILE_ANY_ACCESS）；
                   // 非管理员下 GENERIC_READ/WRITE 打开磁盘会 ERROR_ACCESS_DENIED(5)
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                int error = Marshal.GetLastWin32Error();
                return new StorageDescriptorQueryResult(false, error, null);
            }

            try
            {
                var query = new StoragePropertyQuery
                {
                    PropertyId = StorageDeviceProperty,
                    QueryType = PropertyStandardQuery,
                };

                IntPtr queryPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(StoragePropertyQuery)));
                IntPtr buffer = Marshal.AllocHGlobal(DescriptorBufferSize);
                try
                {
                    Marshal.StructureToPtr(query, queryPtr, false);

                    if (!DeviceIoControl(
                            handle,
                            IOCTL_STORAGE_QUERY_PROPERTY,
                            queryPtr,
                            (uint)Marshal.SizeOf(typeof(StoragePropertyQuery)),
                            buffer,
                            DescriptorBufferSize,
                            out _,
                            IntPtr.Zero))
                    {
                        int error = Marshal.GetLastWin32Error();
                        return new StorageDescriptorQueryResult(false, error, null);
                    }

                    var desc = (StorageDeviceDescriptor)Marshal.PtrToStructure(buffer, typeof(StorageDeviceDescriptor));
                    if (desc.Size == 0)
                        return new StorageDescriptorQueryResult(false, 0, null);

                    // BusType 恒有值（据此判断是否 USB）；厂商/产品串可能为空（系统盘常缺 INQUIRY 字符串）
                    return new StorageDescriptorQueryResult(
                        true,
                        0,
                        new StorageDeviceDescriptorInfo(
                            isUsb: desc.BusType == StorageBusType.BusTypeUsb,
                            busTypeCode: (int)desc.BusType,
                            removableMedia: desc.RemovableMedia,
                            vendorId: ReadAnsiString(buffer, desc.VendorIdOffset),
                            productId: ReadAnsiString(buffer, desc.ProductIdOffset),
                            productRevision: ReadAnsiString(buffer, desc.ProductRevisionOffset)));
                }
                finally
                {
                    Marshal.FreeHGlobal(queryPtr);
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static string? ReadAnsiString(IntPtr buffer, uint offset)
        {
            if (offset == 0)
                return null;
            IntPtr p = IntPtr.Add(buffer, (int)offset);
            string? s = Marshal.PtrToStringAnsi(p);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        #region 常量与结构体

        private const byte StorageDeviceProperty = 0;
        private const byte PropertyStandardQuery = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct StoragePropertyQuery
        {
            public byte PropertyId;
            public byte QueryType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public byte[] AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StorageDeviceDescriptor
        {
            public uint Version;
            public uint Size;
            public byte DeviceType;
            public byte DeviceTypeModifier;
            [MarshalAs(UnmanagedType.U1)]
            public bool RemovableMedia;
            [MarshalAs(UnmanagedType.U1)]
            public bool CommandQueueing;
            public uint VendorIdOffset;
            public uint ProductIdOffset;
            public uint ProductRevisionOffset;
            public uint SerialNumberOffset;
            public StorageBusType BusType;
            public uint RawPropertiesLength;
            public byte RawDeviceProperties;
        }

        private enum StorageBusType : int
        {
            BusTypeUnknown = 0x00,
            BusTypeScsi = 0x01,
            BusTypeAtapi = 0x02,
            BusTypeAta = 0x03,
            BusType1394 = 0x04,
            BusTypeSsa = 0x05,
            BusTypeFibre = 0x06,
            BusTypeUsb = 0x07,
            BusTypeRaid = 0x08,
            BusTypeIScsi = 0x09,
            BusTypeSas = 0x0A,
            BusTypeSata = 0x0B,
            BusTypeSd = 0x0C,
            BusTypeMmc = 0x0D,
            BusTypeMax = 0x0E,
            BusTypeVirtual = 0x0F,
            BusTypeFileBackedVirtual = 0x10,
        }

        #endregion

        #region P/Invoke

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        #endregion
    }
}
