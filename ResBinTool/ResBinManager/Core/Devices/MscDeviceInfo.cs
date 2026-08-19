using System;

namespace ResBinManager.Core.Devices
{
    /// <summary>
    /// 一个可被识别的 USB MSC 磁盘设备。
    /// 由 <see cref="MscDeviceEnumerator"/> 枚举生成，用于设备识别与连接。
    /// </summary>
    public sealed class MscDeviceInfo
    {
        public string DevicePath { get; }

        public ushort Vid { get; }

        public ushort Pid { get; }

        public string Description { get; }

        /// <summary>SCSI INQUIRY 描述符厂商串（可能为空）。</summary>
        public string? VendorId { get; }

        /// <summary>SCSI INQUIRY 描述符产品串（可能为空）。</summary>
        public string? ProductId { get; }

        /// <summary>是否为本工具的目标设备（由 DeviceSignature 判定）。</summary>
        public bool IsTarget { get; }

        /// <summary>字符串型 VID（设备使用字符串标识时，如 VEN_BUILDWIN）。</summary>
        public string? VidStr { get; }

        /// <summary>字符串型 PID（设备使用字符串标识时，如 PROD_VIDEO050LOADER）。</summary>
        public string? PidStr { get; }

        public MscDeviceInfo(
            string devicePath,
            ushort vid,
            ushort pid,
            string description,
            string? vendorId = null,
            string? productId = null,
            bool isTarget = false,
            string? vidStr = null,
            string? pidStr = null)
        {
            DevicePath = devicePath ?? throw new ArgumentNullException(nameof(devicePath));
            Vid = vid;
            Pid = pid;
            Description = description ?? string.Empty;
            VendorId = vendorId;
            ProductId = productId;
            IsTarget = isTarget;
            VidStr = vidStr;
            PidStr = pidStr;
        }

        /// <summary>
        /// 显示名称。设备使用十六进制 VID/PID 时显示十六进制；
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
}
