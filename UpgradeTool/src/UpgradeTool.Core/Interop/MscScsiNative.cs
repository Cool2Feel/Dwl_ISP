using System.Runtime.InteropServices;

namespace UpgradeTool.Core.Interop;

/// <summary>
/// Win32 原生互操作：SCSI Pass-Through（IOCTL_SCSI_PASS_THROUGH_DIRECT）。
/// 实现方式参考同 SDK 仓库 HM020F ResBinManager 的 UsbMscService.cs（产线已验证）。
/// </summary>
internal static class MscScsiNative
{
    internal const uint GENERIC_READ = 0x80000000;
    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    internal const uint IOCTL_SCSI_PASS_THROUGH_DIRECT = 0x0004D014;
    internal const uint IOCTL_SCSI_GET_ADDRESS = 0x00041004;

    internal const byte SCSI_IOCTL_DATA_OUT = 0;
    internal const byte SCSI_IOCTL_DATA_IN = 1;
    internal const byte SCSI_IOCTL_DATA_UNSPECIFIED = 2;

    internal const uint ERROR_IO_PENDING = 997;
    internal const uint ERROR_OPERATION_ABORTED = 995;
    internal const int WAIT_OBJECT_0 = 0;
    internal const int WAIT_TIMEOUT = 258;

    /// <summary>SCSI_PASS_THROUGH_DIRECT（注意 DataBuffer 为指针，32/64 位布局由 Marshal 处理）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ScsiPassThroughDirect
    {
        public ushort Length;
        public byte ScsiStatus;
        public byte PathId;
        public byte TargetId;
        public byte Lun;
        public byte CdbLength;
        public byte SenseInfoLength;
        public byte DataIn;
        public uint DataTransferLength;
        public uint TimeOutValue;
        public IntPtr DataBuffer;
        public uint SenseInfoOffset;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] Cdb;
    }

    /// <summary>SCSI_ADDRESS：IOCTL_SCSI_GET_ADDRESS 的返回结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ScsiAddress
    {
        public uint Length;
        public byte PortNumber;
        public byte PathId;
        public byte TargetId;
        public byte Lun;
    }

    /// <summary>SCSI 总线地址查询结果（TryGetBusAddress 的输出）。</summary>
    public readonly record struct BusAddress(byte PathId, byte TargetId, byte Lun)
    {
        public static readonly BusAddress Default = new(0, 1, 0);
        public bool IsDefault => PathId == 0 && TargetId == 1 && Lun == 0;
    }

    /// <summary>查询设备 SCSI 总线地址（PathId / TargetId / Lun）。失败时返回默认值 (0,1,0)。</summary>
    public static BusAddress TryGetBusAddress(IntPtr deviceHandle)
    {
        try
        {
            var address = new ScsiAddress();
            int size = Marshal.SizeOf<ScsiAddress>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(address, buffer, false);
                bool ok = DeviceIoControl(
                    deviceHandle,
                    IOCTL_SCSI_GET_ADDRESS,
                    IntPtr.Zero, 0,
                    buffer, (uint)size,
                    out _,
                    IntPtr.Zero);
                if (ok)
                {
                    address = Marshal.PtrToStructure<ScsiAddress>(buffer);
                    return new BusAddress(address.PathId, address.TargetId, address.Lun);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // 忽略任何异常，回退到默认值
        }
        return BusAddress.Default;
    }

    /// <summary>Win32 OVERLAPPED，用于异步 DeviceIoControl 以便通过 CancelIoEx 中断。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Overlapped
    {
        public UIntPtr Internal;
        public UIntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr EventHandle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    /// <summary>异步（OVERLAPPED）版本，用于可中断的 SCSI 命令。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        ref Overlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateEvent(
        IntPtr lpEventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
        [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
        string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CancelIoEx(IntPtr hFile, ref Overlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetOverlappedResult(
        IntPtr hFile,
        ref Overlapped lpOverlapped,
        out uint lpNumberOfBytesTransferred,
        [MarshalAs(UnmanagedType.Bool)] bool bWait);
}
