using System;
using ThunderSE.Common;

namespace ThunderSE.Device
{
    /// <summary>
    /// 设备管理器,负责设备检测和事件通知
    /// 实现线程安全的单例模式和正确的资源管理
    /// </summary>
    class DeviceManger : IDisposable
    {
        private bool _disposed = false;
        private DeviceChangeHandler _deviceChangeHandler;

        // 线程安全的单例实现
        private static readonly Lazy<DeviceManger> _instance = new Lazy<DeviceManger>(() => new DeviceManger());

        public event DeviceChangeHandler DeviceChange;

        public static DeviceManger Instance => _instance.Value;

        private DeviceManger()
        {
            try
            {
                _deviceChangeHandler = OnDeviceChange;

                Logger.Debug("Initializing DeviceManger...");
                DeviceApi.Initialize();
                DeviceApi.RegDeviceChangeCallback(_deviceChangeHandler);
                Logger.Info("DeviceManger initialized successfully.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize DeviceManger.", ex);
                throw;
            }
        }

        /// <summary>
        /// 保持向后兼容的静态方法
        /// </summary>
        public static DeviceManger GetInstance() => Instance;

        private void OnDeviceChange(DeviceEvent eventType, string location, string model, string uvcInterafce)
        {
            if (_disposed) return;

            try
            {
                Logger.Info($"Device change event: {eventType}, Location: {location}, Model: {model}, UVC: {uvcInterafce}");
                DeviceChange?.Invoke(eventType, location, model, uvcInterafce);
            }
            catch (Exception ex)
            {
                Logger.Error("DeviceChange callback error.", ex);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 托管资源释放
                    DeviceChange = null;
                }

                // 非托管资源释放
                try
                {
                    Logger.Debug("Releasing DeviceManger resources...");
                    DeviceApi.UnRegDeviceChangeCallback();
                    DeviceApi.UnInitialize();
                    Logger.Info("DeviceManger resources released.");
                }
                catch (Exception ex)
                {
                    Logger.Error("DeviceManger dispose error.", ex);
                }

                _disposed = true;
            }
        }

        public void ScanDevice()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DeviceManger));

            try
            {
                Logger.Debug("Scanning for devices...");
                DeviceApi.ScanDevice();
                Logger.Debug("Device scan completed.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to scan devices.", ex);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~DeviceManger()
        {
            Dispose(false);
        }
    }
}
