using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using SensorAdjust.NativeMethods;
using static SensorAdjust.NativeMethods.NativeMethods;

namespace SensorAdjust.Services
{
    /// <summary>
    /// Detailed result of a device scan operation.
    /// </summary>
    public class DeviceScanResult
    {
        public IntPtr Handle { get; set; } = IntPtr.Zero;
        public int DriveNumber { get; set; } = -1;
        public string DevicePath { get; set; } = "";
        public string VendorId { get; set; } = "";
        public string ProductId { get; set; } = "";
        public bool IsConnected => Handle != IntPtr.Zero;
        public string ErrorMessage { get; set; } = "";
        public int Win32ErrorCode { get; set; }
    }

    /// <summary>
    /// Progress information reported during device scanning.
    /// </summary>
    public class DeviceScanProgress
    {
        public int CurrentDrive { get; set; }
        public int TotalDrives { get; set; }
        public string Status { get; set; } = "";
        public double Percentage => TotalDrives > 0 ? (double)CurrentDrive / TotalDrives * 100.0 : 0;
    }

    /// <summary>
    /// Service for detecting Buildwin/AX3231MP USB devices with advanced
    /// connection management, hot-plug monitoring, and error reporting.
    /// </summary>
    internal class DeviceService : IDisposable
    {
        private const int MaxDriveNumber = 127;
        private const int RetryCount = 2;
        private const int RetryDelayMs = 200;

        // Known vendor/product identifiers (lowercase)
        private static readonly string[] KnownIdentifiers =
        {
            "buildwin minidv",
            "ax3231mptool",
            "buildwinmedia-player",
            "generic"
        };

        private ManagementEventWatcher? _arrivalWatcher;
        private ManagementEventWatcher? _removalWatcher;
        private bool _isDisposed;

        /// <summary>
        /// Fired when a USB device is plugged in (arrival).
        /// </summary>
        public event EventHandler? DeviceArrived;

        /// <summary>
        /// Fired when a USB device is removed.
        /// </summary>
        public event EventHandler? DeviceRemoved;

        /// <summary>
        /// Fired during scan to report progress.
        /// </summary>
        public event EventHandler<DeviceScanProgress>? ScanProgress;

        public DeviceService()
        {
            StartWmiMonitoring();
        }

        // ================================================================
        // WMI Hot-Plug Monitoring
        // ================================================================

        private void StartWmiMonitoring()
        {
            try
            {
                // Monitor device arrivals
                var arrivalQuery = new WqlEventQuery(
                    "SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2");
                _arrivalWatcher = new ManagementEventWatcher(arrivalQuery);
                _arrivalWatcher.EventArrived += (_, _) =>
                {
                    Logger.Info("[DeviceService] WMI: Device arrival detected.");
                    DeviceArrived?.Invoke(this, EventArgs.Empty);
                };
                _arrivalWatcher.Start();

                // Monitor device removals
                var removalQuery = new WqlEventQuery(
                    "SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");
                _removalWatcher = new ManagementEventWatcher(removalQuery);
                _removalWatcher.EventArrived += (_, _) =>
                {
                    Logger.Info("[DeviceService] WMI: Device removal detected.");
                    DeviceRemoved?.Invoke(this, EventArgs.Empty);
                };
                _removalWatcher.Start();

                Logger.Info("[DeviceService] WMI hot-plug monitoring started.");
            }
            catch (Exception ex)
            {
                Logger.Warn("[DeviceService] WMI monitoring not available: {0}", ex.Message);
            }
        }

        // ================================================================
        // Device Scanning
        // ================================================================

        /// <summary>
        /// Scans all physical drives and returns the first matching device.
        /// Reports progress via ScanProgress event and supports cancellation.
        /// </summary>
        public async Task<DeviceScanResult> FindFirstMatchingDeviceAsync(
            CancellationToken cancellationToken = default)
        {
            Logger.Info("[DeviceService] Starting async scan (0..{0})", MaxDriveNumber - 1);

            for (int driver = 0; driver < MaxDriveNumber; driver++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Report progress
                ScanProgress?.Invoke(this, new DeviceScanProgress
                {
                    CurrentDrive = driver + 1,
                    TotalDrives = MaxDriveNumber,
                    Status = $"Scanning PHYSICALDRIVE{driver}..."
                });

                var result = await Task.Run(() => OpenMatchingDeviceWithRetry(driver), cancellationToken);
                if (result.IsConnected)
                {
                    Logger.Info("[DeviceService] Scan complete: device found at PHYSICALDRIVE{0}",
                        result.DriveNumber);
                    return result;
                }
            }

            Logger.Info("[DeviceService] Scan complete: no matching device found.");
            return new DeviceScanResult
            {
                ErrorMessage = "No matching device found among 127 drives."
            };
        }

        /// <summary>
        /// Synchronous fallback for backward compatibility.
        /// </summary>
        public DeviceScanResult FindFirstMatchingDevice()
        {
            return Task.Run(() => FindFirstMatchingDeviceAsync()).GetAwaiter().GetResult();
        }

        // ================================================================
        // Single Drive Opening with Retry
        // ================================================================

        private DeviceScanResult OpenMatchingDeviceWithRetry(int driveNumber)
        {
            for (int attempt = 0; attempt <= RetryCount; attempt++)
            {
                if (attempt > 0)
                {
                    Logger.Info("[DeviceService] Retry {0}/{1} for PHYSICALDRIVE{2}",
                        attempt, RetryCount, driveNumber);
                    Thread.Sleep(RetryDelayMs);
                }

                var result = OpenMatchingDevice(driveNumber);
                if (result.IsConnected || result.Win32ErrorCode != 0)
                    return result;
            }
            return new DeviceScanResult { DriveNumber = driveNumber };
        }

        // ================================================================
        // Core Device Matching Logic
        // ================================================================

        public DeviceScanResult OpenMatchingDevice(int driveNumber)
        {
            string devicePath = $@"\\.\PHYSICALDRIVE{driveNumber}";
            var result = new DeviceScanResult
            {
                DriveNumber = driveNumber,
                DevicePath = devicePath
            };

            IntPtr handle = CreateFile(
                devicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (handle == IntPtr.Zero || handle == new IntPtr(INVALID_HANDLE_VALUE))
            {
                int lastError = Marshal.GetLastWin32Error();
                // ERROR_FILE_NOT_FOUND (2) and ERROR_PATH_NOT_FOUND (3) are normal
                // for non-existent drives, don't log them as warnings
                if (lastError != 2 && lastError != 3)
                {
                    Logger.Warn("[DeviceService] CreateFile failed for {0}: Win32Error={1}",
                        devicePath, lastError);
                }
                result.Win32ErrorCode = lastError;
                result.ErrorMessage = GetWin32ErrorDescription(lastError);
                return result;
            }

            try
            {
                return QueryDeviceDescriptor(handle, result);
            }
            catch (Exception ex)
            {
                Logger.Error("[DeviceService] Exception while querying {0}: {1}",
                    devicePath, ex.Message);
                CloseHandle(handle);
                result.ErrorMessage = $"Exception: {ex.Message}";
                return result;
            }
        }

        private DeviceScanResult QueryDeviceDescriptor(IntPtr handle, DeviceScanResult result)
        {
            uint descSize = (uint)Marshal.SizeOf<STORAGE_DEVICE_DESCRIPTOR>() + 512;
            byte[] descBuffer = new byte[descSize];
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
                CloseHandle(handle);
                result.Win32ErrorCode = lastError;
                result.ErrorMessage = $"IOCTL_STORAGE_QUERY_PROPERTY failed: {GetWin32ErrorDescription(lastError)}";
                return result;
            }

            STORAGE_DEVICE_DESCRIPTOR descriptor = Marshal.PtrToStructure<STORAGE_DEVICE_DESCRIPTOR>(
                Marshal.UnsafeAddrOfPinnedArrayElement(descBuffer, 0));

            // Check if it's a USB device
            if (descriptor.BusType != BusTypeUsb)
            {
                CloseHandle(handle);
                return result; // Not a USB device — silent skip
            }

            // Read vendor/product identifiers
            string vendorId = ReadDescriptorString(descBuffer, descriptor.VendorIdOffset);
            string productId = ReadDescriptorString(descBuffer, descriptor.ProductIdOffset);

            result.VendorId = vendorId;
            result.ProductId = productId;

            if (string.IsNullOrEmpty(vendorId))
            {
                CloseHandle(handle);
                result.ErrorMessage = "VendorId is empty";
                return result;
            }

            // Build the combined string for matching (same logic as original MFC code)
            string combined = vendorId + productId;
            combined = combined.ToLowerInvariant();

            // Check against known identifiers
            if (IsKnownDevice(combined))
            {
                result.Handle = handle;
                Logger.Info("[DeviceService] MATCH: {0} vendor='{1}' product='{2}'",
                    result.DevicePath, vendorId, productId);
                return result;
            }

            CloseHandle(handle);
            return result;
        }

        private static bool IsKnownDevice(string combinedLower)
        {
            foreach (var id in KnownIdentifiers)
            {
                if (combinedLower.Contains(id))
                    return true;
            }
            return false;
        }

        // ================================================================
        // Connection Health Check
        // ================================================================

        /// <summary>
        /// Tests whether a previously obtained device handle is still valid
        /// by sending a minimal SCSI command.
        /// </summary>
        public static bool IsHandleValid(IntPtr handle)
        {
            if (handle == IntPtr.Zero || handle == new IntPtr(INVALID_HANDLE_VALUE))
                return false;

            // Try a zero-length IOCTL to check handle validity
            uint bytesReturned;
            bool result = DeviceIoControl(
                handle,
                0, // Invalid IOCTL code — will fail, but we check the error
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                0,
                out bytesReturned,
                IntPtr.Zero);

            // If the handle is invalid, GetLastError returns ERROR_INVALID_HANDLE (6)
            // If it's valid, we get ERROR_INVALID_FUNCTION (1) which is expected
            int lastError = Marshal.GetLastWin32Error();
            return lastError != 6; // Not ERROR_INVALID_HANDLE means handle is still valid
        }

        // ================================================================
        // Helpers
        // ================================================================

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

        private static string GetWin32ErrorDescription(int errorCode)
        {
            return errorCode switch
            {
                0 => "ERROR_SUCCESS",
                1 => "ERROR_INVALID_FUNCTION",
                2 => "ERROR_FILE_NOT_FOUND",
                3 => "ERROR_PATH_NOT_FOUND",
                5 => "ERROR_ACCESS_DENIED (run as administrator?)",
                6 => "ERROR_INVALID_HANDLE",
                50 => "ERROR_NOT_SUPPORTED",
                87 => "ERROR_INVALID_PARAMETER",
                998 => "ERROR_NOACCESS",
                _ => $"Win32 Error {errorCode}"
            };
        }

        // ================================================================
        // Cleanup
        // ================================================================

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                _arrivalWatcher?.Stop();
                _arrivalWatcher?.Dispose();
                _removalWatcher?.Stop();
                _removalWatcher?.Dispose();
            }
            catch { }

            Logger.Info("[DeviceService] Disposed.");
        }
    }
}