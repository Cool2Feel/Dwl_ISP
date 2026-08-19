using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using ThunderSE.Common;

namespace ThunderSE.Uvc
{
    /// <summary>
    /// 多设备 UVC 管理器，支持同时管理多个摄像头
    /// </summary>
    public class UvcDeviceInstance : IDisposable
    {
        public delegate void VideoDataHandler(byte[] dataBuffer);

        private readonly string _deviceKey;
        private readonly string _uvcInterface;
        private volatile bool _isConnected = false;
        private volatile bool _disposed = false;

        // 每个实例有自己的回调
        //private UvcApi.VideoDataCallbackFunc _videoDataCb;
        //private UvcApi.RawDataCallbackFunc _rawDataCb;

        private readonly VideoDataCallbackFunc _videoDataCb;
        private readonly RawDataCallbackFunc _rawDataCb;

        public event Action<byte[], int, int, int> VideoFrameReceived;
        public event Action<bool> ConnectionStateChanged;

        public string DeviceKey => _deviceKey;
        public string UvcInterface => _uvcInterface;
        public bool IsConnected => _isConnected;
        public int VideoWidth { get; private set; }
        public int VideoHeight { get; private set; }

        public UvcDeviceInstance(string deviceKey, string uvcInterface)
        {
            _deviceKey = deviceKey;
            _uvcInterface = uvcInterface;

            _videoDataCb = OnVideoDataCallback;
            _rawDataCb = OnRawDataCallback;
        }

        public async Task<bool> ConnectAsync()
        {
            if (_disposed || _isConnected)
                return false;

            try
            {
                Logger.Info($"[UVC-{_deviceKey}] Connecting to: {_uvcInterface}");

                int width = 0, height = 0;
                int ret = UvcApi.OpenInputSafe(_uvcInterface, ref width, ref height);

                if (ret < 0)
                {
                    Logger.Error($"[UVC-{_deviceKey}] Failed to connect: {ret}");
                    return false;
                }

                VideoWidth = width;
                VideoHeight = height;
                _isConnected = true;

                // 注册回调（注意：这里需要 C++ 层支持多实例回调）
                UvcApi.SetVideoDataCallback(_videoDataCb, IntPtr.Zero);

                Logger.Info($"[UVC-{_deviceKey}] Connected successfully: {width}x{height}");
                ConnectionStateChanged?.Invoke(true);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[UVC-{_deviceKey}] Connect exception: {ex.Message}", ex);
                return false;
            }
        }

        public void Disconnect()
        {
            if (!_isConnected)
                return;

            try
            {
                Logger.Info($"[UVC-{_deviceKey}] Disconnecting...");
                _isConnected = false;

                UvcApi.CloseInput();

                Logger.Info($"[UVC-{_deviceKey}] Disconnected");
                ConnectionStateChanged?.Invoke(false);
            }
            catch (Exception ex)
            {
                Logger.Error($"[UVC-{_deviceKey}] Disconnect exception: {ex.Message}", ex);
            }
        }

        private int OnVideoDataCallback(IntPtr videoData, int size, int pixelFormat, IntPtr userData)
        {
            if (!_isConnected || videoData == IntPtr.Zero || size <= 0)
                return 0;

            try
            {
                byte[] buffer = new byte[size];
                System.Runtime.InteropServices.Marshal.Copy(videoData, buffer, 0, size);

                VideoFrameReceived?.Invoke(buffer, pixelFormat, VideoWidth, VideoHeight);
            }
            catch (Exception ex)
            {
                Logger.Error($"[UVC-{_deviceKey}] Callback error: {ex.Message}");
            }

            return 0;
        }

        private int OnRawDataCallback(IntPtr rawData, int dataSize, int pixelFormat, int width, int height, IntPtr userData)
        {
            return 0;
        }

        public void Dispose()
        {
            if (_disposed) return;

            Disconnect();
            _disposed = true;
        }
    }

    /// <summary>
    /// 多设备 UVC 管理器（单例）
    /// </summary>
    public sealed class MultiUvcManager : IDisposable
    {
        private static readonly Lazy<MultiUvcManager> _instance =
            new Lazy<MultiUvcManager>(() => new MultiUvcManager());

        private readonly ConcurrentDictionary<string, UvcDeviceInstance> _devices =
            new ConcurrentDictionary<string, UvcDeviceInstance>();

        private volatile bool _disposed = false;

        public static MultiUvcManager Instance => _instance.Value;

        private MultiUvcManager() { }

        /// <summary>
        /// 添加并连接设备
        /// </summary>
        public async Task<UvcDeviceInstance> AddDeviceAsync(string deviceKey, string uvcInterface)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MultiUvcManager));

            if (_devices.ContainsKey(deviceKey))
            {
                Logger.Warn($"Device already exists: {deviceKey}");
                return _devices[deviceKey];
            }

            var device = new UvcDeviceInstance(deviceKey, uvcInterface);
            _devices[deviceKey] = device;

            bool success = await device.ConnectAsync();
            if (!success)
            {
                _devices.TryRemove(deviceKey, out _);
                device.Dispose();
                return null;
            }

            return device;
        }

        /// <summary>
        /// 移除设备
        /// </summary>
        public void RemoveDevice(string deviceKey)
        {
            if (_devices.TryRemove(deviceKey, out var device))
            {
                device.Dispose();
                Logger.Info($"Device removed: {deviceKey}");
            }
        }

        /// <summary>
        /// 获取设备实例
        /// </summary>
        public UvcDeviceInstance GetDevice(string deviceKey)
        {
            _devices.TryGetValue(deviceKey, out var device);
            return device;
        }

        /// <summary>
        /// 获取所有设备
        /// </summary>
        public UvcDeviceInstance[] GetAllDevices()
        {
            return _devices.Values.ToArray();
        }

        public void Dispose()
        {
            if (_disposed) return;

            foreach (var device in _devices.Values)
            {
                device.Dispose();
            }
            _devices.Clear();
            _disposed = true;
        }
    }
}
