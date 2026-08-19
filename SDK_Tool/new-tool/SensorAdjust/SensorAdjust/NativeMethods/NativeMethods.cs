using System.Runtime.InteropServices;

namespace SensorAdjust.NativeMethods
{
    internal static class NativeMethods
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint INVALID_HANDLE_VALUE = 0xFFFFFFFF;

        // SCSI IOCTL
        private const uint FILE_DEVICE_CONTROLLER = 0x00000004;
        private const uint FILE_READ_ACCESS = 0x0001;
        private const uint FILE_WRITE_ACCESS = 0x0002;
        private const uint METHOD_BUFFERED = 0;

        // Storage IOCTL
        private const uint FILE_DEVICE_MASS_STORAGE = 0x0000002d;
        private const uint FILE_ANY_ACCESS = 0;

        private static uint CTL_CODE(uint deviceType, uint function, uint method, uint access)
        {
            return (deviceType << 16) | (access << 14) | (function << 2) | method;
        }

        public static uint IOCTL_SCSI_PASS_THROUGH_DIRECT =>
            CTL_CODE(FILE_DEVICE_CONTROLLER, 0x0405, METHOD_BUFFERED, FILE_READ_ACCESS | FILE_WRITE_ACCESS);

        public static uint IOCTL_STORAGE_QUERY_PROPERTY =>
            CTL_CODE(FILE_DEVICE_MASS_STORAGE, 0x0500, METHOD_BUFFERED, FILE_ANY_ACCESS);

        // SCSI data direction
        public const byte SCSI_IOCTL_DATA_IN = 1;
        public const byte SCSI_IOCTL_DATA_OUT = 0;
        public const byte SCSI_IOCTL_DATA_UNSPECIFIED = 2;

        // Storage bus types
        public const byte BusTypeUsb = 0x07;

        // Storage property query
        public const int StorageDeviceProperty = 0;
        public const int PropertyStandardQuery = 0;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            byte[] lpInBuffer,
            uint nInBufferSize,
            byte[] lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [StructLayout(LayoutKind.Sequential)]
        public struct SCSI_PASS_THROUGH_DIRECT
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

        [StructLayout(LayoutKind.Sequential)]
        public struct SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER
        {
            public SCSI_PASS_THROUGH_DIRECT sptd;
            public uint Filler;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSenseBuf;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct STORAGE_PROPERTY_QUERY
        {
            public int PropertyId;
            public int QueryType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public byte[] AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct STORAGE_DEVICE_DESCRIPTOR
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
            public byte BusType;
            public uint RawPropertiesLength;
        }
    }
}