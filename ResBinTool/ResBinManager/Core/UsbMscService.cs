using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ResBinManager.Core.Devices;

namespace ResBinManager.Core
{
    /// <summary>
    /// USB MSC (Mass Storage Class) 通信服务
    /// 使用 SCSI Pass-Through 方式与 USB MSC 设备通信
    /// 参考 isptool 项目的 ScsiAcc.cpp / DeviceManager.cpp 实现
    /// 设备识别（枚举 + VID/PID + 描述符签名匹配）由 <see cref="MscDeviceEnumerator"/> 负责，
    /// 本类仅负责目标设备路径的打开与 SCSI 厂商命令传输（时间同步）。
    /// </summary>
    public class UsbMscService : IDisposable
    {
        #region Win32 P/Invoke 声明

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint GetLastError();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        #endregion

        #region Win32 结构体

        /// <summary>
        /// SCSI_PASS_THROUGH_DIRECT 结构
        /// 必须与 Windows SDK NTDDSCSI.H 定义完全一致 (包含 ScsiStatus 字段)
        /// 32位: sizeof = 44 bytes
        /// 64位: sizeof = 52 bytes (DataBuffer 指针有 4 字节对齐填充)
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct SCSI_PASS_THROUGH_DIRECT
        {
            public short Length;
            public byte ScsiStatus;
            public byte PathId;
            public byte TargetId;
            public byte Lun;
            public byte CdbLength;
            public byte SenseInfoLength;
            public byte DataIn;
            public uint DataTransferLength;
            public uint TimeOutValue;
            // 64位: 此处有 4 字节隐式填充使 DataBuffer 8字节对齐
            public IntPtr DataBuffer;
            public int SenseInfoOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] Cdb;
        }

        /// <summary>
        /// SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER 结构
        /// 包含 SCSI_PASS_THROUGH_DIRECT + Filler + 32字节 Sense 缓冲区
        /// 32位: 44 + 4 + 32 = 80 字节
        /// 64位: 52 + 4 + 32 = 88 字节
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER
        {
            public SCSI_PASS_THROUGH_DIRECT sptd;
            public uint Filler;               // 4 字节对齐
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSenseBuf;         // 32 字节 Sense 缓冲区
        }

        #endregion

        #region 常量

        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 0x00000001;
        const uint FILE_SHARE_WRITE = 0x00000002;
        const uint OPEN_EXISTING = 3;
        const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        /// <summary>
        /// SCSI IOCTL
        /// </summary>
        const uint IOCTL_SCSI_PASS_THROUGH_DIRECT = 0x0004D014;

        // SCSI 常量
        const byte SCSI_IOCTL_DATA_IN = 1;
        const byte SCSI_IOCTL_DATA_OUT = 0;
        const byte SCSI_IOCTL_DATA_UNSPECIFIED = 2;

        // Vendor 命令
        const byte VENDOR_OPCODE = 0xCB;
        const byte SET_TIME_SUBOPCODE = 0xF0;

        // 重试参数 (参考 ScsiAcc.cpp)
        const int MAX_RETRIES = 10;
        const int RETRY_DELAY_MS = 20;

        // 连接探针 CDB
        static readonly byte[] ProbeTestUnitReady = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        static readonly byte[] ProbeInquiry = { 0x12, 0x00, 0x00, 0x00, 0x24, 0x00 };
        const int InquiryAllocationLength = 36;

        // 打开设备的重试参数
        const int OpenRetryCount = 3;
        const int OpenRetryDelayMs = 100;

        #endregion

        #region 私有字段

        private SafeFileHandle? _deviceHandle;
        private bool _isConnected;
        private bool _disposed;
        private string? _devicePath;

        #endregion

        #region 公共属性

        public bool IsConnected => _isConnected;

        public string DeviceInfo { get; private set; } = string.Empty;

        public string? DeviceName { get; private set; }

        public ushort VendorId { get; private set; }

        public ushort ProductId { get; private set; }

        public string ProductDescription { get; private set; } = string.Empty;

        /// <summary>
        /// 单条 SCSI 命令的设备执行超时（秒）。
        /// 连接握手阶段由 DeviceConnection 调短（3s）以便在设备不响应时快速失败；
        /// 时间同步等正常命令保持默认值。
        /// </summary>
        public int CommandTimeoutSeconds { get; set; } = 200;

        #endregion

        #region 公共方法

        /// <summary>
        /// 扫描并连接目标 USB MSC 设备（VID=1908，PID=3319/3283）。
        /// 使用 <see cref="MscDeviceEnumerator"/> 枚举磁盘设备并匹配目标签名。
        /// 当 VID/PID 不匹配时，通过 SCSI INQUIRY 产品串（如 BuildwinMedia-Player）兜底识别。
        /// </summary>
        public bool Connect()
        {
            Disconnect();

            try
            {
                DebugWrite($"[UsbMscService] Scanning for target device VID={DeviceSignature.TargetVid:X4}, PIDs=[{string.Join(", ", DeviceSignature.TargetPids.Select(p => p.ToString("X4")))}]");

                List<MscDeviceInfo> devices = MscDeviceEnumerator.Enumerate(DebugWrite).ToList();
                DebugWrite($"[UsbMscService] 枚举到 {devices.Count} 个磁盘设备");

                MscDeviceInfo? target = null;
                foreach (MscDeviceInfo dev in devices)
                {
                    DebugWrite($"[UsbMscService]   设备: path={dev.DevicePath}, VID={dev.Vid:X4}, PID={dev.Pid:X4}, isTarget={dev.IsTarget}");
                    if (dev.IsTarget)
                    {
                        target = dev;
                        break;
                    }
                }

                // 兜底识别：VID/PID 不匹配时，对每个 USB MSC 设备发送 SCSI INQUIRY，
                // 检查产品串是否包含目标关键字（如 "buildwin"），覆盖 VID/PID 变体的设备。
                if (target == null)
                {
                    DebugWrite("[UsbMscService] VID/PID 未匹配，尝试通过 SCSI INQUIRY 产品串识别...");
                    foreach (MscDeviceInfo dev in devices)
                    {
                        DebugWrite($"[UsbMscService]   INQUIRY 探测: {dev.DevicePath}");
                        (string? vendor, string? product) = TryInquiryDevice(dev.DevicePath);
                        if (vendor == null && product == null)
                            continue;

                        if (DeviceSignature.IsTargetVendorProduct(vendor, product))
                        {
                            DebugWrite($"[UsbMscService] INQUIRY 匹配成功: vendor='{vendor}', product='{product}'");
                            target = dev;
                            break;
                        }
                        DebugWrite($"[UsbMscService]   INQUIRY 未匹配: vendor='{vendor}', product='{product}'");
                    }
                }

                if (target == null)
                {
                    DeviceInfo = $"未找到目标设备 (VID={DeviceSignature.TargetVid:X4})。请检查设备连接";
                    DebugWrite(DeviceInfo);
                    return false;
                }

                return Connect(target);
            }
            catch (Exception ex)
            {
                DeviceInfo = $"连接错误: {ex.Message}";
                DebugWrite($"[UsbMscService] Connect error: {ex}");
                CleanupDevice();
                return false;
            }
        }

        /// <summary>
        /// 连接指定的枚举设备（由 MscDeviceEnumerator 识别出的目标设备）。
        /// </summary>
        public bool Connect(MscDeviceInfo info)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));
            return ConnectByPath(info.DevicePath, info);
        }

        /// <summary>
        /// 断开设备连接
        /// </summary>
        public void Disconnect()
        {
            CleanupDevice();
        }

        /// <summary>
        /// 同步PC时间到设备RTC
        /// </summary>
        public bool SyncPcTimeToDevice()
        {
            if (!_isConnected)
                throw new InvalidOperationException("设备未连接");

            uint secondsFrom2000 = GetSecondsFrom2000();
            DebugWrite($"[UsbMscService] Time to sync: {secondsFrom2000} seconds from 2000");

            bool success = SendTimeCommand(secondsFrom2000);
            DebugWrite(success ? "[UsbMscService] Time sync successful" : "[UsbMscService] Time sync failed");
            return success;
        }

        /// <summary>
        /// 同步指定时间戳到设备RTC
        /// Unix时间戳为UTC基准，需转换为本地时间基准的秒数（与TimeUpdate参考项目一致）
        /// </summary>
        public bool SyncTimeByTimestamp(long unixTimestamp)
        {
            if (!_isConnected)
                throw new InvalidOperationException("设备未连接");

            const long unixEpochTo2000 = 946684800L;
            // Unix时间戳是UTC基准，设备RTC使用本地时间基准，需要加上时区偏移
            long timezoneOffset = (long)TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalSeconds;
            uint secondsFrom2000 = unixTimestamp >= unixEpochTo2000
                ? (uint)(unixTimestamp - unixEpochTo2000 + timezoneOffset)
                : 0;

            DebugWrite($"[UsbMscService] Set time: {secondsFrom2000} seconds from 2000");
            bool success = SendTimeCommand(secondsFrom2000);
            DebugWrite(success ? "[UsbMscService] Time sync successful" : "[UsbMscService] Time sync failed");
            return success;
        }

        /// <summary>
        /// 获取当前时间的可读字符串
        /// </summary>
        public string GetCurrentTimeString() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        /// <summary>
        /// 检查目标设备是否在系统中可用。
        /// 先按 VID/PID 匹配，无匹配时通过 SCSI INQUIRY 产品串兜底识别。
        /// </summary>
        public bool IsTargetDeviceAvailable()
        {
            List<MscDeviceInfo> devices = MscDeviceEnumerator.Enumerate().ToList();
            return devices.Any(d => d.IsTarget);
        }

        /// <summary>
        /// 检查当前已连接设备是否仍在系统中（通过发送 TestUnitReady 命令确认）。
        /// 设备拔出后句柄变为无效，SCSI 命令会失败返回 false。
        /// </summary>
        public bool IsConnectedDevicePresent()
        {
            if (!_isConnected || _deviceHandle == null || _deviceHandle.IsInvalid)
                return false;
            return SendScsiCommand(ProbeTestUnitReady, SCSI_IOCTL_DATA_UNSPECIFIED, 0, null);
        }

        /// <summary>
        /// 获取目标设备的详细信息（用于诊断）
        /// </summary>
        public (string status, string details) GetTargetDeviceStatus()
        {
            IReadOnlyList<MscDiskProbe> probes = MscDeviceEnumerator.EnumerateProbes();
            MscDiskProbe? target = null;
            foreach (MscDiskProbe probe in probes)
            {
                if (probe.IsIncluded && probe.ToDeviceInfo().IsTarget)
                {
                    target = probe;
                    break;
                }
            }

            if (target == null)
                return ("not_found", $"目标设备 (VID={DeviceSignature.TargetVid:X4}) 未检测到");

            return ("found", $"VID={target.Vid:X4}, PID={target.Pid:X4}, 路径={target.DevicePath}");
        }

        /// <summary>
        /// 连接前的传输层自检探针：
        /// 用一组标准 SCSI 命令确认 "SCSI Pass-Through 通道可用" 与 "数据输入阶段可用"，
        /// 在进入 0xCB 厂商命令阶段之前把失败点分离出来——若探针失败说明 SCSI 通道本身不可用
        /// （句柄权限/锁定/非 MSC 磁盘），探针通过而 0xCB 失败才指向固件厂商通道或 CDB 问题。
        /// </summary>
        /// <returns>数据输入通道可用（INQUIRY 成功）即视为传输层可用。
        /// 相机等厂商命令通道设备没有真实介质，TEST UNIT READY 返回 NOT_READY 属正常行为，不阻断连接。</returns>
        public bool TestTransport(Action<string>? log = null)
        {
            if (_deviceHandle == null || _deviceHandle.IsInvalid)
            {
                log?.Invoke("[Probe] 设备句柄不可用");
                return false;
            }

            bool tur = SendScsiCommand(ProbeTestUnitReady, SCSI_IOCTL_DATA_UNSPECIFIED, 0, null);
            byte[] inquiryBuffer = new byte[InquiryAllocationLength];
            bool inquiry = SendScsiCommand(ProbeInquiry, SCSI_IOCTL_DATA_IN, InquiryAllocationLength, inquiryBuffer);

            log?.Invoke($"[Probe] TUR={(tur ? "OK" : "FAIL")}, INQUIRY={(inquiry ? "OK" : "FAIL")}");
            return inquiry;
        }

        #endregion

        #region 私有方法 - 连接

        /// <summary>
        /// 按设备接口路径打开目标设备（带瞬时错误重试）。
        /// 设备识别与签名匹配由调用方（MscDeviceEnumerator / DeviceConnection）负责。
        /// </summary>
        private bool ConnectByPath(string targetPath, MscDeviceInfo? info)
        {
            Disconnect();

            try
            {
                _devicePath = targetPath;
                DebugWrite($"[UsbMscService] Opening device path: {targetPath}");

                int lastError = 0;
                for (int attempt = 0; attempt <= OpenRetryCount; attempt++)
                {
                    _deviceHandle = CreateFile(
                        targetPath,
                        GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        FILE_ATTRIBUTE_NORMAL,
                        IntPtr.Zero);

                    if (_deviceHandle != null && !_deviceHandle.IsInvalid)
                        break;

                    _deviceHandle = null;
                    lastError = Marshal.GetLastWin32Error();

                    // 瞬时错误（设备未就绪/被占用）重试，其余直接失败
                    if (lastError != 21 && lastError != 170 && lastError != 32 && attempt >= OpenRetryCount)
                        break;
                    if (attempt < OpenRetryCount)
                        System.Threading.Thread.Sleep(OpenRetryDelayMs);
                }

                if (_deviceHandle == null || _deviceHandle.IsInvalid)
                {
                    int error = lastError != 0 ? lastError : Marshal.GetLastWin32Error();
                    string hint = error == 5
                        ? "（权限不足：向磁盘下发 SCSI 命令需要管理员权限，请以管理员身份运行本工具）"
                        : "";
                    DeviceInfo = $"无法打开设备 (错误码: {error})。请确保设备已连接 {hint}";
                    DebugWrite(DeviceInfo);
                    return false;
                }

                DebugWrite("[UsbMscService] Device handle opened successfully");

                VendorId = info?.Vid ?? DeviceSignature.TargetVid;
                ProductId = info?.Pid ?? DeviceSignature.TargetPids[0];
                if (ProductId == 0)
                    ProductId = DeviceSignature.TargetPids[0];
                ProductDescription = "HM020F USB MSC Device";

                _isConnected = true;
                DeviceName = info?.DisplayName ?? $"HM020F MSC Device (VID={VendorId:X4} PID={ProductId:X4})";
                DeviceInfo = $"已连接: {DeviceName}";

                DebugWrite("[UsbMscService] Successfully connected to target device");
                return true;
            }
            catch (Exception ex)
            {
                DeviceInfo = $"连接错误: {ex.Message}";
                DebugWrite($"[UsbMscService] Connect error: {ex}");
                CleanupDevice();
                return false;
            }
        }

        #endregion

        #region 私有方法 - SCSI Pass-Through

        /// <summary>
        /// 发送 SCSI 命令 (参考 ScsiAcc.cpp 的 ReadFromScsi/WriteToScsi 实现)
        /// 使用 SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER 结构
        /// 所有字段偏移通过 Marshal.OffsetOf 动态获取, 确保 32/64 位兼容
        /// </summary>
        private bool SendScsiCommand(byte[] cdb, byte dataIn, int dataLength, byte[]? data)
            => SendScsiCommand(_deviceHandle, cdb, dataIn, dataLength, data);

        /// <summary>
        /// 发送 SCSI 命令到指定设备句柄（用于连接前的 INQUIRY 探询）。
        /// </summary>
        private bool SendScsiCommand(SafeFileHandle? handle, byte[] cdb, byte dataIn, int dataLength, byte[]? data)
        {
            if (handle == null || handle.IsInvalid)
                return false;

            int cdbLen = cdb.Length;

            // 使用 Marshal.SizeOf 获取实际结构大小 (包含隐式填充)
            int sptdSize = Marshal.SizeOf(typeof(SCSI_PASS_THROUGH_DIRECT));
            int structureSize = sptdSize + 4 + 32; // SCSI_PASS_THROUGH_DIRECT + Filler(4) + SenseBuf(32)
            int senseInfoOffset = sptdSize + 4;  // SenseInfoOffset = offsetof(WITH_BUFFER, ucSenseBuf)
            int dataOffset = structureSize;        // 数据紧跟在 Sense 缓冲区后面
            int totalSize = structureSize + Math.Max(dataLength, 0);

            // 使用 OffsetOf 精确获取所有字段偏移 (自动处理 32/64 位对齐差异)
            int offLength = Marshal.OffsetOf(typeof(SCSI_PASS_THROUGH_DIRECT), "Length").ToInt32();
            int offScsiStatus = Marshal.OffsetOf(typeof(SCSI_PASS_THROUGH_DIRECT), "ScsiStatus").ToInt32();
            int offPathId = Marshal.OffsetOf(typeof(SCSI_PASS_THROUGH_DIRECT), "PathId").ToInt32();
            int offTargetId = Marshal.OffsetOf(typeof(SCSI_PASS_THROUGH_DIRECT), "TargetId").ToInt32();
            int offLun = Marshal.OffsetOf(typeof(SCSI_PASS_THROUGH_DIRECT), "Lun").ToInt32();
            int offCdbLength = Marshal.OffsetOf(typeof(SCSI_PASS_THROUGH_DIRECT), "CdbLength").ToInt32();
            int offSenseInfoLength = Marshal.OffsetOf(typeof(SCSI_PASS_THROUGH_DIRECT), "SenseInfoLength").ToInt32();
            int offDataIn = Marshal.OffsetOf(typeof(SCSI_PASS_THROUGH_DIRECT), "DataIn").ToInt32();
            int offDataTransferLength = Marshal.OffsetOf(typeof(SCSI_PASS_THROUGH_DIRECT), "DataTransferLength").ToInt32();
            int offTimeOutValue = Marshal.OffsetOf(typeof(SCSI_PASS_THROUGH_DIRECT), "TimeOutValue").ToInt32();
            int offDataBuffer = Marshal.OffsetOf(typeof(SCSI_PASS_THROUGH_DIRECT), "DataBuffer").ToInt32();
            int offSenseInfoOffset = Marshal.OffsetOf(typeof(SCSI_PASS_THROUGH_DIRECT), "SenseInfoOffset").ToInt32();
            int offCdb = Marshal.OffsetOf(typeof(SCSI_PASS_THROUGH_DIRECT), "Cdb").ToInt32();

            DebugWrite($"[UsbMscService] SendScsiCommand: CDBLen={cdbLen}, DataLen={dataLength}");
            DebugWrite($"[UsbMscService]   SPT Size={sptdSize}, Total Struct={structureSize}, SenseOff={senseInfoOffset}");
            DebugWrite($"[UsbMscService]   DataBuffer offset={offDataBuffer}, SenseInfoOffset offset={offSenseInfoOffset}, CDB offset={offCdb}");

            int retryTimes = MAX_RETRIES;

            do
            {
                // 分配连续内存
                IntPtr bufferPtr = Marshal.AllocHGlobal(totalSize);

                try
                {
                    // 清零整个缓冲区
                    for (int i = 0; i < totalSize; i++)
                    {
                        Marshal.WriteByte(bufferPtr, i, 0);
                    }

                    // 填充 SCSI_PASS_THROUGH_DIRECT 头 (全部使用 Marshal.OffsetOf 偏移)

                    // Length (2 bytes)
                    Marshal.WriteInt16(bufferPtr, offLength, (short)sptdSize);

                    // ScsiStatus (1 byte) - 输出字段, 初始为0
                    Marshal.WriteByte(bufferPtr, offScsiStatus, 0);

                    // PathId (1 byte)
                    Marshal.WriteByte(bufferPtr, offPathId, 0);

                    // TargetId (1 byte) - MSC 设备 TargetId 为 1
                    Marshal.WriteByte(bufferPtr, offTargetId, 1);

                    // Lun (1 byte)
                    Marshal.WriteByte(bufferPtr, offLun, 0);

                    // CdbLength (1 byte)
                    Marshal.WriteByte(bufferPtr, offCdbLength, (byte)cdbLen);

                    // SenseInfoLength (1 byte) - 26 = 24字节Sense + 2字节附加头
                    Marshal.WriteByte(bufferPtr, offSenseInfoLength, 26);

                    // DataIn (1 byte)
                    Marshal.WriteByte(bufferPtr, offDataIn, dataIn);

                    // DataTransferLength (4 bytes)
                    Marshal.WriteInt32(bufferPtr, offDataTransferLength, unchecked((int)(uint)Math.Max(dataLength, 0)));

                    // TimeOutValue (4 bytes)
                    Marshal.WriteInt32(bufferPtr, offTimeOutValue, CommandTimeoutSeconds);

                    // DataBuffer (指针)
                    if (dataLength > 0 && data != null)
                    {
                        Marshal.WriteIntPtr(bufferPtr, offDataBuffer, IntPtr.Add(bufferPtr, dataOffset));
                    }
                    else
                    {
                        Marshal.WriteIntPtr(bufferPtr, offDataBuffer, IntPtr.Zero);
                    }

                    // SenseInfoOffset (4 bytes)
                    Marshal.WriteInt32(bufferPtr, offSenseInfoOffset, senseInfoOffset);

                    // CDB (16 bytes)
                    for (int i = 0; i < cdbLen && i < 16; i++)
                    {
                        Marshal.WriteByte(bufferPtr, offCdb + i, cdb[i]);
                    }

                    // Filler (4 bytes) - 已清零

                    // Sense 缓冲区 (32 bytes) - 已清零

                    // 拷贝数据到缓冲区 (在 Sense buffer 之后)
                    if (dataLength > 0 && data != null)
                    {
                        Marshal.Copy(data, 0, IntPtr.Add(bufferPtr, dataOffset), dataLength);
                    }

                    // 发送 DeviceIoControl
                    uint returned;
                    bool status = DeviceIoControl(
                        handle,
                        IOCTL_SCSI_PASS_THROUGH_DIRECT,
                        bufferPtr,
                        (uint)structureSize,
                        bufferPtr,
                        (uint)structureSize,
                        out returned,
                        IntPtr.Zero);

                    if (status)
                    {
                        // 从缓冲区读回数据
                        if (dataLength > 0 && data != null)
                        {
                            Marshal.Copy(IntPtr.Add(bufferPtr, dataOffset), data, 0, dataLength);
                        }

                        DebugWrite($"[UsbMscService] SCSI command succeeded, returned={returned}");
                        return true;
                    }
                    else
                    {
                        int lastErr = Marshal.GetLastWin32Error();
                        DebugWrite($"[UsbMscService] DeviceIoControl failed: Error={lastErr}");

                        // Error 55 = 设备未连接, 直接返回
                        if (lastErr == 55)
                        {
                            return false;
                        }

                        // 重试
                        System.Threading.Thread.Sleep(RETRY_DELAY_MS);
                        if (retryTimes-- > 0)
                        {
                            DebugWrite($"[UsbMscService] Retrying ({retryTimes} attempts left)...");
                            continue;
                        }

                        return false;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(bufferPtr);
                }
            } while (true);
        }

        /// <summary>
        /// 对候选设备执行 SCSI INQUIRY，解析厂商/产品串。
        /// 用于 VID/PID 不匹配时的兜底识别（如设备实际枚举为 0x0219:0x3280）。
        /// INQUIRY 标准响应：byte[8..15]=厂商串, byte[16..31]=产品串, byte[32..35]=修订版本。
        /// </summary>
        private (string? Vendor, string? Product) TryInquiryDevice(string devicePath)
        {
            SafeFileHandle? handle = null;
            try
            {
                handle = CreateFile(
                    devicePath,
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);

                if (handle == null || handle.IsInvalid)
                {
                    DebugWrite($"[UsbMscService] INQUIRY 打开设备失败: {devicePath}, err={Marshal.GetLastWin32Error()}");
                    return (null, null);
                }

                byte[] buffer = new byte[InquiryAllocationLength];
                if (!SendScsiCommand(handle, ProbeInquiry, SCSI_IOCTL_DATA_IN, InquiryAllocationLength, buffer))
                {
                    DebugWrite($"[UsbMscService] INQUIRY 命令失败: {devicePath}");
                    return (null, null);
                }

                // SCSI INQUIRY 标准响应格式
                string vendor = TrimAscii(buffer, 8, 8);
                string product = TrimAscii(buffer, 16, 16);
                DebugWrite($"[UsbMscService] INQUIRY: vendor='{vendor}', product='{product}'");
                return (vendor, product);
            }
            catch (Exception ex)
            {
                DebugWrite($"[UsbMscService] INQUIRY 异常: {ex.Message}");
                return (null, null);
            }
            finally
            {
                handle?.Dispose();
            }
        }

        /// <summary>提取 INQUIRY 响应中的 ASCII 字段并去除首尾空格。</summary>
        private static string TrimAscii(byte[] buffer, int offset, int length)
        {
            if (offset + length > buffer.Length)
                return string.Empty;
            return Encoding.ASCII.GetString(buffer, offset, length).TrimEnd(' ', '\0').Trim();
        }

        /// <summary>
        /// 构建时间同步 CDB 命令
        /// 格式: OpCode(0xCB) + SubOpCode(0xF0) + 时间戳(Big-Endian) + 填充
        /// 设备端读取 prxbuf[19..22] 作为时间戳 (即 CDB bytes 4-7)
        /// </summary>
        private byte[] BuildTimeCdb(uint secondsFrom2000)
        {
            // 使用 16 字节 CDB，与已验证的参考项目 (TimeUpdate) 保持一致
            byte[] cdb = new byte[16];

            cdb[0] = VENDOR_OPCODE;      // 0xCB - Vendor Unique 命令
            cdb[1] = SET_TIME_SUBOPCODE; // 0xF0 - 时间同步子命令

            // 时间数据 (Big-Endian, 设备端读取 prxbuf[19..22] 对应 CDB[4..7])
            cdb[4] = (byte)((secondsFrom2000 >> 24) & 0xFF);
            cdb[5] = (byte)((secondsFrom2000 >> 16) & 0xFF);
            cdb[6] = (byte)((secondsFrom2000 >> 8) & 0xFF);
            cdb[7] = (byte)(secondsFrom2000 & 0xFF);

            return cdb;
        }

        /// <summary>
        /// 发送时间设置命令 (带重试)
        /// </summary>
        private bool SendTimeCommand(uint secondsFrom2000)
        {
            if (_deviceHandle == null || _deviceHandle.IsInvalid)
            {
                DebugWrite("[UsbMscService] Device handle not ready");
                return false;
            }

            byte[] cdb = BuildTimeCdb(secondsFrom2000);
            DebugWrite($"[UsbMscService] Time CDB: {BitConverter.ToString(cdb)}");

            // 时间同步命令不需要数据传输 (dataLength=0)，DataBuffer=NULL
            // 使用 SCSI_IOCTL_DATA_IN (1) 方向与已验证的参考项目一致
            bool success = SendScsiCommand(cdb, SCSI_IOCTL_DATA_IN, 0, null);
            DebugWrite(success ? "[UsbMscService] Time sync command completed successfully" : "[UsbMscService] Time sync command failed");
            return success;
        }

        /// <summary>
        /// 获取相对于2000年的秒数（使用本地时间，与TimeUpdate参考项目一致）
        /// </summary>
        private static uint GetSecondsFrom2000()
        {
            DateTime localNow = DateTime.Now;
            DateTime year2000 = new DateTime(2000, 1, 1, 0, 0, 0);
            TimeSpan diff = localNow - year2000;
            return (uint)diff.TotalSeconds;
        }

        #endregion

        #region 私有方法 - 资源管理

        private void CleanupDevice()
        {
            try
            {
                if (_deviceHandle != null && !_deviceHandle.IsInvalid)
                {
                    _deviceHandle.Dispose();
                    _deviceHandle = null;
                }
            }
            catch (Exception ex)
            {
                DebugWrite($"[UsbMscService] CleanupDevice error: {ex.Message}");
            }
            finally
            {
                _isConnected = false;
                _devicePath = null;
                DeviceInfo = "已断开";
            }
        }

        private static void DebugWrite(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
            Console.WriteLine(message);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                CleanupDevice();
                _disposed = true;
            }
        }

        #endregion
    }
}
