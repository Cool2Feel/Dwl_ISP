using System.Runtime.InteropServices;
using System.Text;
using TimeUpdater.NativeMethods;
using static TimeUpdater.NativeMethods.NativeMethods;

namespace TimeUpdater.Services
{
    /// <summary>
    /// Service for detecting Buildwin/AX3231MP USB devices and updating their internal time
    /// via SCSI pass-through commands.
    /// Ported from the original MFC C++ timeUpdater project.
    /// </summary>
    internal class DeviceService : IDisposable
    {
        private const int MaxDriveNumber = 126;
        private const int MinDriveNumber = 1;

        /// <summary>
        /// Result of a device time update operation.
        /// </summary>
        public enum UpdateResult
        {
            NoDevice,
            Success,
            Failed
        }

        /// <summary>
        /// Scans all physical drives to find a matching Buildwin/AX3231MP USB device.
        /// Returns the device handle if found, otherwise IntPtr.Zero.
        /// </summary>
        private IntPtr OpenMatchingDevice(int driveNumber)
        {
            string deviceName = $@"\\.\PHYSICALDRIVE{driveNumber}";
            Logger.Info("[OpenMatchingDevice] Attempting to open {0} ...", deviceName);

            IntPtr handle = CreateFile(
                deviceName,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (handle == new IntPtr(INVALID_HANDLE_VALUE) || handle == IntPtr.Zero)
            {
                int lastError = Marshal.GetLastWin32Error();
                Logger.Warn("[OpenMatchingDevice] Failed to open {0}, Win32Error={1}", deviceName, lastError);
                return IntPtr.Zero;
            }

            Logger.Info("[OpenMatchingDevice] Successfully opened {0}, Handle=0x{1:X8}", deviceName, handle.ToInt64());

            try
            {
                // Allocate buffer for storage device descriptor
                uint descSize = (uint)Marshal.SizeOf<STORAGE_DEVICE_DESCRIPTOR>() + 512;
                byte[] descBuffer = new byte[descSize];

                // Initialize STORAGE_PROPERTY_QUERY (all zeros = PropertyId=StorageDeviceProperty, QueryType=PropertyStandardQuery)
                byte[] queryBuffer = new byte[Marshal.SizeOf<STORAGE_PROPERTY_QUERY>()];

                uint bytesReturned;
                bool success = DeviceIoControl(
                    handle,
                    IOCTL_STORAGE_QUERY_PROPERTY,
                    queryBuffer,
                    (uint)queryBuffer.Length,
                    descBuffer,
                    descSize,
                    out bytesReturned,
                    IntPtr.Zero);

                if (!success)
                {
                    int lastError = Marshal.GetLastWin32Error();
                    Logger.Warn("[OpenMatchingDevice] IOCTL_STORAGE_QUERY_PROPERTY failed for {0}, Win32Error={1}",
                        deviceName, lastError);
                    CloseHandle(handle);
                    return IntPtr.Zero;
                }

                Logger.Info("[OpenMatchingDevice] IOCTL_STORAGE_QUERY_PROPERTY succeeded, bytesReturned={0}", bytesReturned);

                // Parse the STORAGE_DEVICE_DESCRIPTOR
                STORAGE_DEVICE_DESCRIPTOR descriptor = Marshal.PtrToStructure<STORAGE_DEVICE_DESCRIPTOR>(
                    Marshal.UnsafeAddrOfPinnedArrayElement(descBuffer, 0));

                Logger.Info("[OpenMatchingDevice] DeviceType={0}, BusType={1}, VendorIdOffset={2}, ProductIdOffset={3}",
                    descriptor.DeviceType, descriptor.BusType, descriptor.VendorIdOffset, descriptor.ProductIdOffset);

                // Check if it's a USB device
                if (descriptor.BusType != BusTypeUsb)
                {
                    Logger.Info("[OpenMatchingDevice] {0} is BusType={1} (not USB), skipping.",
                        deviceName, descriptor.BusType);
                    CloseHandle(handle);
                    return IntPtr.Zero;
                }

                Logger.Info("[OpenMatchingDevice] {0} is USB device, checking vendor/product identifiers ...", deviceName);

                // Read the vendor ID string
                string vendorId = ReadDescriptorString(descBuffer, descriptor.VendorIdOffset);
                Logger.Info("[OpenMatchingDevice] VendorId string: \"{0}\" (offset={1})", vendorId, descriptor.VendorIdOffset);

                if (string.IsNullOrEmpty(vendorId))
                {
                    Logger.Warn("[OpenMatchingDevice] VendorId is empty, skipping.");
                    CloseHandle(handle);
                    return IntPtr.Zero;
                }

                string productId = ReadDescriptorString(descBuffer, descriptor.ProductIdOffset);
                Logger.Info("[OpenMatchingDevice] ProductId string: \"{0}\" (offset={1})", productId, descriptor.ProductIdOffset);

                // Build the combined string for matching
                string combined = vendorId;
                if (!string.IsNullOrEmpty(productId))
                {
                    combined += productId;
                }

                combined = combined.ToLowerInvariant();
                Logger.Info("[OpenMatchingDevice] Combined identifier string (lowercase): \"{0}\"", combined);

                // Check against known vendor/product identifiers
                bool matchBuildwinMinidv = combined.Contains("buildwin minidv");
                bool matchAx3231mptool = combined.Contains("ax3231mptool");
                bool matchBuildwinMediaPlayer = combined.Contains("buildwinmedia-player");
                bool matchGeneric = combined.Contains("generic");

                Logger.Info("[OpenMatchingDevice] Matching results: " +
                    "buildwin_minidv={0}, ax3231mptool={1}, buildwinmedia-player={2}, generic={3}",
                    matchBuildwinMinidv, matchAx3231mptool, matchBuildwinMediaPlayer, matchGeneric);

                bool isMatch = matchBuildwinMinidv || matchAx3231mptool ||
                               matchBuildwinMediaPlayer || matchGeneric;

                if (!isMatch)
                {
                    Logger.Info("[OpenMatchingDevice] No matching identifier found for {0}, skipping.", deviceName);
                    CloseHandle(handle);
                    return IntPtr.Zero;
                }

                Logger.Info("[OpenMatchingDevice] *** MATCH FOUND! Device {0} matches target identifier. ***", deviceName);
                return handle;
            }
            catch (Exception ex)
            {
                Logger.Error("[OpenMatchingDevice] Exception while querying {0}: {1}", deviceName, ex.Message);
                Logger.Error("[OpenMatchingDevice] StackTrace: {0}", ex.StackTrace ?? "(null)");
                CloseHandle(handle);
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Reads a null-terminated ASCII string from the descriptor buffer at the specified offset.
        /// </summary>
        private static string ReadDescriptorString(byte[] buffer, uint offset)
        {
            if (offset == 0 || offset >= buffer.Length)
                return string.Empty;

            int end = (int)offset;
            while (end < buffer.Length && buffer[end] != 0)
                end++;

            int length = end - (int)offset;
            if (length <= 0)
                return string.Empty;

            return Encoding.ASCII.GetString(buffer, (int)offset, length);
        }

        /// <summary>
        /// Sends a SCSI command to update the device's internal time.
        /// Uses the 0xCB SCSI operation code with time data in big-endian format.
        /// </summary>
        private bool SendTimeUpdateCommand(IntPtr deviceHandle, uint secondsSince2000)
        {
            Logger.Info("[SendTimeUpdateCommand] Preparing SCSI pass-through command, secondsSince2000={0} (0x{0:X8})", secondsSince2000);

            // Build the SCSI_PASS_THROUGH_DIRECT structure
            int sptdSize = Marshal.SizeOf<SCSI_PASS_THROUGH_DIRECT>();
            int sptdwbSize = Marshal.SizeOf<SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER>();
            Logger.Info("[SendTimeUpdateCommand] SCSI_PASS_THROUGH_DIRECT size={0}, WITH_BUFFER size={1}",
                sptdSize, sptdwbSize);

            SCSI_PASS_THROUGH_DIRECT sptd = new SCSI_PASS_THROUGH_DIRECT
            {
                Length = (ushort)sptdSize,
                PathId = 0,
                TargetId = 1,
                Lun = 0,
                CdbLength = 16,
                SenseInfoLength = 26,
                DataIn = SCSI_IOCTL_DATA_UNSPECIFIED,
                DataTransferLength = 0,
                TimeOutValue = 200,
                DataBuffer = IntPtr.Zero,
                SenseInfoOffset = (uint)(sptdSize + sizeof(uint)),
                Cdb = new byte[16]
            };

            // Build the CDB command (16 bytes)
            byte[] cdb = sptd.Cdb;
            cdb[0] = 0xCB;
            cdb[1] = 0xF0;
            cdb[4] = (byte)((secondsSince2000 >> 24) & 0xFF);
            cdb[5] = (byte)((secondsSince2000 >> 16) & 0xFF);
            cdb[6] = (byte)((secondsSince2000 >> 8) & 0xFF);
            cdb[7] = (byte)(secondsSince2000 & 0xFF);
            // bytes 2,3,8-15 remain 0x00

            Logger.HexDump("[SendTimeUpdateCommand] CDB (16 bytes)", cdb);
            Logger.Info("[SendTimeUpdateCommand] CDB fields: OpCode=0xCB, Param1=0xF0, " +
                "TimeBytes=[0x{0:X2}, 0x{1:X2}, 0x{2:X2}, 0x{3:X2}]",
                cdb[4], cdb[5], cdb[6], cdb[7]);

            sptd.Cdb = cdb;

            // Build the SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER
            SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER sptdwb = new SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER
            {
                sptd = sptd,
                Filler = 0,
                ucSenseBuf = new byte[32]
            };

            // Marshal the structure to a byte buffer
            byte[] buffer = new byte[sptdwbSize];
            IntPtr ptr = Marshal.AllocHGlobal(sptdwbSize);
            try
            {
                Marshal.StructureToPtr(sptdwb, ptr, false);
                Marshal.Copy(ptr, buffer, 0, sptdwbSize);

                Logger.Info("[SendTimeUpdateCommand] Calling DeviceIoControl with IOCTL_SCSI_PASS_THROUGH_DIRECT (0x{0:X8})...",
                    IOCTL_SCSI_PASS_THROUGH_DIRECT);
                Logger.HexDump("[SendTimeUpdateCommand] Full SCSI pass-through buffer (before call)", buffer, 48);

                uint bytesReturned;
                bool result = DeviceIoControl(
                    deviceHandle,
                    IOCTL_SCSI_PASS_THROUGH_DIRECT,
                    buffer,
                    (uint)sptdwbSize,
                    buffer,
                    (uint)sptdwbSize,
                    out bytesReturned,
                    IntPtr.Zero);

                if (result)
                {
                    Logger.Info("[SendTimeUpdateCommand] SCSI command SUCCEEDED, bytesReturned={0}", bytesReturned);
                }
                else
                {
                    int lastError = Marshal.GetLastWin32Error();
                    Logger.Error("[SendTimeUpdateCommand] SCSI command FAILED, Win32Error={0} (0x{0:X8})", lastError);

                    // Map common Win32 error codes to readable messages
                    string errorDesc = lastError switch
                    {
                        0 => "ERROR_SUCCESS",
                        2 => "ERROR_FILE_NOT_FOUND (device not found?)",
                        3 => "ERROR_PATH_NOT_FOUND",
                        5 => "ERROR_ACCESS_DENIED (run as admin?)",
                        6 => "ERROR_INVALID_HANDLE",
                        87 => "ERROR_INVALID_PARAMETER",
                        50 => "ERROR_NOT_SUPPORTED (SCSI not supported?)",
                        1 => "ERROR_INVALID_FUNCTION",
                        31 => "ERROR_GEN_FAILURE (device not responding?)",
                        998 => "ERROR_NOACCESS (invalid buffer?)",
                        _ => "Unknown error"
                    };
                    Logger.Error("[SendTimeUpdateCommand] Win32Error description: {0}", errorDesc);
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error("[SendTimeUpdateCommand] Exception during DeviceIoControl: {0}", ex.Message);
                Logger.Error("[SendTimeUpdateCommand] StackTrace: {0}", ex.StackTrace ?? "(null)");
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// Updates the time on the first matching Buildwin/AX3231MP USB device found.
        /// </summary>
        public UpdateResult UpdateDeviceTime()
        {
            Logger.Info("============================================");
            Logger.Info("[UpdateDeviceTime] Starting device scan. Scanning PHYSICALDRIVE{0} to PHYSICALDRIVE{1}",
                MinDriveNumber, MaxDriveNumber - 1);
            Logger.Info("============================================");

            for (int i = MinDriveNumber; i < MaxDriveNumber; i++)
            {
                // Log progress every 10 drives to avoid excessive output
                if (i % 10 == 0)
                {
                    Logger.Info("[UpdateDeviceTime] Scan progress: checked {0}/{1} drives ...", i, MaxDriveNumber - 1);
                }

                IntPtr deviceHandle = OpenMatchingDevice(i);
                if (deviceHandle != IntPtr.Zero && deviceHandle != new IntPtr(INVALID_HANDLE_VALUE))
                {
                    Logger.Info("[UpdateDeviceTime] === Matched device found at PHYSICALDRIVE{0}, Handle=0x{1:X8} ===",
                        i, deviceHandle.ToInt64());

                    try
                    {
                        Logger.Info("[UpdateDeviceTime] Calculating current time in seconds since 2000 ...");
                        uint seconds = TimeCalculator.GetSecondsSince2000();
                        Logger.Info("[UpdateDeviceTime] Computed secondsSince2000 = {0} (0x{0:X8})", seconds);

                        Logger.Info("[UpdateDeviceTime] Sending time update command to device ...");
                        bool success = SendTimeUpdateCommand(deviceHandle, seconds);

                        if (success)
                        {
                            Logger.Info("[UpdateDeviceTime] >>> Time update SUCCESSFUL on PHYSICALDRIVE{0} <<<", i);
                            return UpdateResult.Success;
                        }
                        else
                        {
                            Logger.Error("[UpdateDeviceTime] >>> Time update FAILED on PHYSICALDRIVE{0} <<<", i);
                            return UpdateResult.Failed;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("[UpdateDeviceTime] Exception during time update on PHYSICALDRIVE{0}: {1}",
                            i, ex.Message);
                        Logger.Error("[UpdateDeviceTime] StackTrace: {0}", ex.StackTrace ?? "(null)");
                        return UpdateResult.Failed;
                    }
                    finally
                    {
                        CloseHandle(deviceHandle);
                        Logger.Info("[UpdateDeviceTime] Device handle closed for PHYSICALDRIVE{0}", i);
                    }
                }
            }

            Logger.Info("[UpdateDeviceTime] Scan complete. No matching device found among {0} drives.",
                MaxDriveNumber - MinDriveNumber);
            Logger.Info("============================================");
            return UpdateResult.NoDevice;
        }

        /// <summary>
        /// Asynchronously updates the time on the first matching device.
        /// </summary>
        public Task<UpdateResult> UpdateDeviceTimeAsync()
        {
            Logger.Info("[UpdateDeviceTimeAsync] Starting async device time update task ...");
            return Task.Run(() => UpdateDeviceTime());
        }

        public void Dispose()
        {
            Logger.Info("[DeviceService] Disposed.");
        }
    }
}