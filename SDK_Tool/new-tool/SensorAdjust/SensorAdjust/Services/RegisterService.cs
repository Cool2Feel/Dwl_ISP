using System.Runtime.InteropServices;
using SensorAdjust.NativeMethods;
using static SensorAdjust.NativeMethods.NativeMethods;

namespace SensorAdjust.Services
{
    /// <summary>
    /// Service for reading/writing registers on a Buildwin/AX3231MP USB device
    /// via SCSI pass-through commands.
    /// Ported from the original MFC C++ SensorAdjust project.
    /// </summary>
    internal class RegisterService
    {
        private readonly IntPtr _deviceHandle;

        public RegisterService(IntPtr deviceHandle)
        {
            _deviceHandle = deviceHandle;
        }

        /// <summary>
        /// Reads from SCSI device (CDB[0] = 0xCB).
        /// </summary>
        private bool ReadFromScsi(byte[] cdb, int dataLen, byte[] data)
        {
            cdb[0] = 0xCB;
            var sptdwb = new SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER
            {
                sptd = new SCSI_PASS_THROUGH_DIRECT
                {
                    Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH_DIRECT>(),
                    PathId = 0,
                    TargetId = 1,
                    Lun = 0,
                    CdbLength = (byte)cdb.Length,
                    SenseInfoLength = 26,
                    DataIn = SCSI_IOCTL_DATA_IN,
                    DataTransferLength = (uint)dataLen,
                    TimeOutValue = 200,
                    DataBuffer = Marshal.AllocHGlobal(dataLen),
                    SenseInfoOffset = (uint)(Marshal.SizeOf<SCSI_PASS_THROUGH_DIRECT>() + sizeof(uint)),
                    Cdb = cdb
                },
                Filler = 0,
                ucSenseBuf = new byte[32]
            };

            try
            {
                Marshal.Copy(data, 0, sptdwb.sptd.DataBuffer, dataLen);

                int sptdwbSize = Marshal.SizeOf<SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER>();
                byte[] buffer = new byte[sptdwbSize];
                IntPtr ptr = Marshal.AllocHGlobal(sptdwbSize);

                try
                {
                    Marshal.StructureToPtr(sptdwb, ptr, false);
                    Marshal.Copy(ptr, buffer, 0, sptdwbSize);

                    uint bytesReturned;
                    bool result = DeviceIoControl(
                        _deviceHandle,
                        IOCTL_SCSI_PASS_THROUGH_DIRECT,
                        buffer,
                        (uint)sptdwbSize,
                        buffer,
                        (uint)sptdwbSize,
                        out bytesReturned,
                        IntPtr.Zero);

                    if (result)
                    {
                        Marshal.Copy(ptr, buffer, 0, sptdwbSize);
                        // Re-read the structure to get the response data
                        var response = Marshal.PtrToStructure<SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER>(ptr);
                        Marshal.Copy(response.sptd.DataBuffer, data, 0, dataLen);
                    }

                    return result;
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(sptdwb.sptd.DataBuffer);
            }
        }

        /// <summary>
        /// Writes to SCSI device (CDB[0] = 0xCB).
        /// </summary>
        private bool WriteToScsi(byte[] cdb, int dataLen, byte[] data)
        {
            cdb[0] = 0xCB;
            var sptdwb = new SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER
            {
                sptd = new SCSI_PASS_THROUGH_DIRECT
                {
                    Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH_DIRECT>(),
                    PathId = 0,
                    TargetId = 1,
                    Lun = 0,
                    CdbLength = (byte)cdb.Length,
                    SenseInfoLength = 26,
                    DataIn = SCSI_IOCTL_DATA_OUT,
                    DataTransferLength = (uint)dataLen,
                    TimeOutValue = 200,
                    DataBuffer = dataLen > 0 ? Marshal.AllocHGlobal(dataLen) : IntPtr.Zero,
                    SenseInfoOffset = (uint)(Marshal.SizeOf<SCSI_PASS_THROUGH_DIRECT>() + sizeof(uint)),
                    Cdb = cdb
                },
                Filler = 0,
                ucSenseBuf = new byte[32]
            };

            try
            {
                if (dataLen > 0 && sptdwb.sptd.DataBuffer != IntPtr.Zero)
                {
                    Marshal.Copy(data, 0, sptdwb.sptd.DataBuffer, dataLen);
                }

                int sptdwbSize = Marshal.SizeOf<SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER>();
                byte[] buffer = new byte[sptdwbSize];
                IntPtr ptr = Marshal.AllocHGlobal(sptdwbSize);

                try
                {
                    Marshal.StructureToPtr(sptdwb, ptr, false);
                    Marshal.Copy(ptr, buffer, 0, sptdwbSize);

                    uint bytesReturned;
                    return DeviceIoControl(
                        _deviceHandle,
                        IOCTL_SCSI_PASS_THROUGH_DIRECT,
                        buffer,
                        (uint)sptdwbSize,
                        buffer,
                        (uint)sptdwbSize,
                        out bytesReturned,
                        IntPtr.Zero);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            finally
            {
                if (sptdwb.sptd.DataBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(sptdwb.sptd.DataBuffer);
            }
        }

        /// <summary>
        /// Writes a register value at the specified address.
        /// CDB: [0xCB, 0xF1, addr[3], addr[2], addr[1], addr[0], val[3], val[2], val[1], val[0], ...]
        /// </summary>
        public bool WriteRegister(uint regAddr, uint regValue)
        {
            byte[] cdb = new byte[16];
            cdb[1] = 0xF1;
            cdb[2] = (byte)((regAddr >> 24) & 0xFF);
            cdb[3] = (byte)((regAddr >> 16) & 0xFF);
            cdb[4] = (byte)((regAddr >> 8) & 0xFF);
            cdb[5] = (byte)(regAddr & 0xFF);
            cdb[6] = (byte)((regValue >> 24) & 0xFF);
            cdb[7] = (byte)((regValue >> 16) & 0xFF);
            cdb[8] = (byte)((regValue >> 8) & 0xFF);
            cdb[9] = (byte)(regValue & 0xFF);

            return WriteToScsi(cdb, 0, Array.Empty<byte>());
        }

        /// <summary>
        /// Reads a register value from the specified address.
        /// CDB: [0xCB, 0xF2, addr[3], addr[2], addr[1], addr[0], ...]
        /// Response: data[0]=0xCB, data[1]=0xF2, data[2-5]=value (big-endian)
        /// </summary>
        public bool ReadRegister(uint regAddr, out uint regValue)
        {
            regValue = 0;
            byte[] cdb = new byte[16];
            byte[] data = new byte[16];

            cdb[1] = 0xF2;
            cdb[2] = (byte)((regAddr >> 24) & 0xFF);
            cdb[3] = (byte)((regAddr >> 16) & 0xFF);
            cdb[4] = (byte)((regAddr >> 8) & 0xFF);
            cdb[5] = (byte)(regAddr & 0xFF);

            bool bRet = ReadFromScsi(cdb, 16, data);
            if (bRet && data[0] == 0xCB && data[1] == 0xF2)
            {
                regValue = (uint)(data[2] << 24) |
                          (uint)(data[3] << 16) |
                          (uint)(data[4] << 8) |
                          data[5];
                return true;
            }
            return false;
        }

        /// <summary>
        /// Converts a hex string to a numeric value.
        /// </summary>
        public static uint HexStringToValue(string hexStr)
        {
            uint value = 0;
            int len = hexStr.Length;
            for (int i = 0; i < len; i++)
            {
                char c = hexStr[i];
                uint tmp = 0xFFFFFFFF;
                if (c >= '0' && c <= '9')
                    tmp = (uint)(c - '0');
                else if (c >= 'A' && c <= 'F')
                    tmp = (uint)(c - 'A' + 10);
                else if (c >= 'a' && c <= 'f')
                    tmp = (uint)(c - 'a' + 10);

                if (tmp == 0xFFFFFFFF)
                    return 0xFFFFFFFF;

                value = (value << 4) | tmp;
            }
            return value;
        }
    }
}