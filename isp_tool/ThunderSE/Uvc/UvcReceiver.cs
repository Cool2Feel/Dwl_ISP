using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ThunderSE.Common;
using ThunderSE.Device;

namespace ThunderSE.Uvc
{
    /// <summary>
    /// UVC视频接收器,负责接收和处理UVC摄像头视频流
    /// 实现线程安全的单例模式,确保资源正确管理
    /// </summary>
    public sealed partial class UvcReceiver : IDisposable
    {
        #region 委托定义
        public delegate void VideoDataHandler(byte[] dataBuffer);
        public delegate void RawDataHandler(IntPtr data, int dataSize, int pixelFormat, int width, int height);

        // 线程安全的单例实例
        private static readonly Lazy<UvcReceiver> _instance = new Lazy<UvcReceiver>(() => new UvcReceiver());

        // 回调委托必须保存,防止被GC
        private static readonly VideoDataCallbackFunc VideoDataCb;
        private static readonly YuvDataCallbackFunc YuvDataCb;
        private static readonly PlayStateChangeCallbackFunc PlayableChangeDataCb;
        private static readonly RawDataCallbackFunc RawDataCb;

        //private readonly ReaderWriterLockSlim _dataReceiveLock = new ReaderWriterLockSlim();
        private event VideoDataHandler _dataReceive;

        //private readonly ReaderWriterLockSlim _yuvDataReceiveLock = new ReaderWriterLockSlim();
        private event YuvDataCallbackFunc _yuvDataReceive;

        //private readonly ReaderWriterLockSlim _rawDataReceiveLock = new ReaderWriterLockSlim();
        private event RawDataHandler _rawDataReceive;

        //private readonly ReaderWriterLockSlim _statusChangeLock = new ReaderWriterLockSlim();
        private event PlayStateChangeCallbackFunc _statusChange;

        #endregion

        #region 字段定义
        private static readonly object _connectionLock = new object();

        // 使用volatile确保线程可见性
        private long _receivePacketCount = 0;
        private const int MaxPacketCount = 10;

        private volatile bool _isConnected = false;
        private volatile bool _isReconnecting = false;  // 新增：标记是否正在重连
        private volatile bool _disposed = false;

        // 新增：标记应用是否正在退出（防止退出时触发自动重连）
        private static volatile bool _isApplicationExiting = false;

        // 新增：用于同步断开和回调的锁
        //private readonly object _disconnectLock = new object();

        // 新增：标记是否正在执行断开操作（防止并发 Connect/Disconnect）
        private volatile bool _isDisconnecting = false;

        // 视频尺寸使用Interlocked确保线程安全
        private int _videoWidth = 0;//3840;
        private int _videoHeight = 0;//2160;

        private bool _isRawBayer = false;

        // 新增：输出格式模式（与界面 SetMode 同步）
        private volatile DeviceConfig.Isp.SetMode _setMode = DeviceConfig.Isp.SetMode.MJPG;

        // 新增：Bayer 模式（与界面 Bayer 配置同步，默认 RGGB）
        private volatile DeviceConfig.Isp.BayerMode _bayerMode = DeviceConfig.Isp.BayerMode.BGBG;

        public int VideoWidth
        {
            get { return _videoWidth; }
            set { Interlocked.Exchange(ref _videoWidth, value);}
        }

        public int VideoHeight
        {
            get { return _videoHeight; }
            set { Interlocked.Exchange(ref _videoHeight, value);}
        }

        public bool IsConnected => _isConnected;
        public bool IsReconnecting => _isReconnecting;  // 新增：公开属性，供 ConfigManager 检查

        // 当前已连接设备的描述符（如 "video=USB Camera"），供 Proc Amp 控制使用
        public string CurrentDeviceDescriptor { get; private set; } = "";
        public static bool IsApplicationExiting => _isApplicationExiting;  // 新增：公开属性，供外部检查

        public bool IsRawBayer => _isRawBayer;

        public int FlipImage = 0; // 0: 不翻转, 1: 垂直翻转, 2: 水平翻转, 3: 水平+垂直翻转

        public double UvcFps { get; set; } = 0.0;

        /// <summary>
        /// 获取或设置当前输出格式模式（与界面 SetMode 同步）
        /// </summary>
        public DeviceConfig.Isp.SetMode SetMode
        {
            get => _setMode;
            set
            {
                _setMode = value;
                if (value == DeviceConfig.Isp.SetMode.RAW8 || value == DeviceConfig.Isp.SetMode.RAW10)
                {
                    _isRawBayer = true;
                }
                else
                {
                    _isRawBayer = false;
                }
                UvcApi.SetRawFrameMode((int)value);
            }
        }

        /// <summary>
        /// 获取或设置当前 Bayer 排列模式（与界面 Bayer 配置同步）
        /// </summary>
        public DeviceConfig.Isp.BayerMode BayerMode
        {
            get => _bayerMode;
            set => _bayerMode = value;
        }

        #endregion

        /// <summary>
        /// 获取单例实例(线程安全)
        /// </summary>
        public static UvcReceiver Instance => _instance.Value;

        // 静态构造函数注册回调(只执行一次)
        static UvcReceiver()
        {
            VideoDataCb = OnReceiveDataStatic;
            PlayableChangeDataCb = OnPlayStateChangeStatic;
            YuvDataCb = OnReceiveYuvDataStatic;
            RawDataCb = OnReceiveRawDataStatic;
        }

        private UvcReceiver()
        {
            try
            {
                // 注册全局回调(只注册一次)
                UvcApi.SetVideoDataCallback(VideoDataCb, IntPtr.Zero);
                UvcApi.SetYuvDataCallback(YuvDataCb);
                UvcApi.SetPlayStateChangeCallback(PlayableChangeDataCb);
                UvcApi.SetRawDataCallback(RawDataCb, IntPtr.Zero);
                Logger.Debug("UvcReceiver callbacks registered.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to register UvcReceiver callbacks.", ex);
            }
        }

        #region 事件管理(线程安全)

        public event VideoDataHandler DataReceive
        {
            add { _dataReceive += value; }
            remove { _dataReceive -= value; }
            //add
            //{
            //    _dataReceiveLock.EnterWriteLock();
            //    try { _dataReceive += value; }
            //    finally { _dataReceiveLock.ExitWriteLock(); }
            //}
            //remove
            //{
            //    _dataReceiveLock.EnterWriteLock();
            //    try { _dataReceive -= value; }
            //    finally { _dataReceiveLock.ExitWriteLock(); }
            //}
        }

        public event YuvDataCallbackFunc YuvDataReceive
        {
            add { _yuvDataReceive += value; }
            remove { _yuvDataReceive -= value; }
            //add
            //{
            //    _yuvDataReceiveLock.EnterWriteLock();
            //    try { _yuvDataReceive += value; }
            //    finally { _yuvDataReceiveLock.ExitWriteLock(); }
            //}
            //remove
            //{
            //    _yuvDataReceiveLock.EnterWriteLock();
            //    try { _yuvDataReceive -= value; }
            //    finally { _yuvDataReceiveLock.ExitWriteLock(); }
            //}
        }

        public event RawDataHandler RawDataReceive
        {
            add { _rawDataReceive += value; }
            remove { _rawDataReceive -= value; }
            //add
            //{
            //    _rawDataReceiveLock.EnterWriteLock();
            //    try { _rawDataReceive += value; }
            //    finally { _rawDataReceiveLock.ExitWriteLock(); }
            //}
            //remove
            //{
            //    _rawDataReceiveLock.EnterWriteLock();
            //    try { _rawDataReceive -= value; }
            //    finally { _rawDataReceiveLock.ExitWriteLock(); }
            //}
        }

        public event PlayStateChangeCallbackFunc StatusChange
        {
            add { _statusChange += value; }
            remove { _statusChange -= value; }
            //add
            //{
            //    _statusChangeLock.EnterWriteLock();
            //    try { _statusChange += value; }
            //    finally { _statusChangeLock.ExitWriteLock(); }
            //}
            //remove
            //{
            //    _statusChangeLock.EnterWriteLock();
            //    try { _statusChange -= value; }
            //    finally { _statusChangeLock.ExitWriteLock(); }
            //}
        }

        #endregion

        #region 连接管理

        /// <summary>
        /// 连接到UVC摄像头（使用当前存储的分辨率）
        /// </summary>
        /// <param name="cameraDescriptor">设备描述符(设备名称或RTSP地址)</param>
        /// <returns>连接是否成功</returns>
        public bool Connect(string cameraDescriptor)
        {
            Logger.Info($"Connect {cameraDescriptor} from {_videoWidth}x{_videoHeight}");
            return Connect(cameraDescriptor, _videoWidth, _videoHeight);
        }

        /// <summary>
        /// 连接到UVC摄像头（指定分辨率）
        /// </summary>
        /// <param name="cameraDescriptor">设备描述符(设备名称或RTSP地址)</param>
        /// <param name="videoWidth">请求的视频宽度（<=0表示使用设备默认值）</param>
        /// <param name="videoHeight">请求的视频高度（<=0表示使用设备默认值）</param>
        /// <returns>连接是否成功</returns>
        public bool Connect(string cameraDescriptor, int videoWidth, int videoHeight)
        {
            lock (_connectionLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(UvcReceiver));

                if (_isDisconnecting)
                {
                    Logger.Warn("Connect blocked: Disconnect is in progress");
                    return false;
                }

                if (_isConnected)
                {
                    Logger.Warn("Already connected, disconnecting first...");
                    _isConnected = false;
                    Interlocked.Exchange(ref _receivePacketCount, 0);
                    
                    try
                    {
                        Logger.Debug("Closing existing UVC input before reconnect...");
                        int _ret = UvcApi.CloseInput();
                        if (_ret < 0)
                        {
                            Logger.Warn($"CloseInput returned: {_ret}");
                        }
                        else
                        {
                            Logger.Debug("Existing UVC input closed.");
                        }
                        
                        Thread.Sleep(500);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Exception while closing existing connection: {ex.Message}");
                    }
                }

                string inputPath = cameraDescriptor.StartsWith("video=") ? cameraDescriptor : "video=" + cameraDescriptor;

                try
                {
                    Logger.Info($"Connecting to UVC device: {inputPath}");
                }
                catch
                {
                }

                int width = videoWidth;
                int height = videoHeight;

                Logger.Debug($"Calling UvcApi.OpenInputSafe for: {inputPath}, width={width}, height={height}");

                int ret = UvcApi.OpenInputSafe(inputPath, ref width, ref height);
                if (ret < 0)
                {
                    Logger.Error($"Failed to open UVC input: {ret}, descriptor: {inputPath}");
                    return false;
                }

                Logger.Debug($"UvcApi.OpenInputSafe returned: {ret}, width={width}, height={height}");

                Interlocked.Exchange(ref _videoWidth, width);
                Interlocked.Exchange(ref _videoHeight, height);
                Interlocked.Exchange(ref _receivePacketCount, 0);
                _isConnected = true;
                CurrentDeviceDescriptor = inputPath;

                //ResolutionChanged?.Invoke(width, height);

                Logger.Info($"UVC connected successfully: {width}x{height}");
                return true;
            }
        }

        /// <summary>
        /// 重新配置分辨率（在保持连接的情况下切换分辨率）
        /// </summary>
        /// <param name="videoWidth">请求的视频宽度</param>
        /// <param name="videoHeight">请求的视频高度</param>
        /// <returns>是否成功</returns>
        public bool ReconfigureResolution(int videoWidth, int videoHeight)
        {
            lock (_connectionLock)
            {
                if (!_isConnected)
                {
                    Logger.Warn("ReconfigureResolution called but not connected.");
                    return false;
                }

                Logger.Info($"Reconfiguring resolution from {_videoWidth}x{_videoHeight} to {videoWidth}x{videoHeight}");

                int width = videoWidth;
                int height = videoHeight;

                int ret = UvcApi.ReconfigureResolution(ref width, ref height);
                if (ret < 0)
                {
                    Logger.Error($"Failed to reconfigure resolution: {ret}");
                    return false;
                }

                Interlocked.Exchange(ref _videoWidth, width);
                Interlocked.Exchange(ref _videoHeight, height);

                //ResolutionChanged?.Invoke(width, height);

                Logger.Info($"Resolution reconfigured successfully: {width}x{height}");
                return true;
            }
        }

        /// <summary>
        /// 断开UVC连接并释放资源
        /// 增强版：确保等待所有 pending 回调完成，防止空指针访问
        /// </summary>
        public void Disconnect()
        {
            // 使用锁保护，防止并发 Disconnect/Connect
            lock (_connectionLock)
            {
                // 标记正在断开，阻止新连接
                _isDisconnecting = true;

                try
                {
                    if (!_isConnected)
                    {
                        Logger.Debug("Disconnect called but not connected.");
                        return;
                    }

                    Logger.Info("Disconnecting from UVC device...");

                    // 步骤1: 先标记断开，阻止新回调进入
                    _isConnected = false;
                    CurrentDeviceDescriptor = "";

                    // 步骤2: 等待正在执行的回调完成（最多等待 5 秒）
                    int waitCount = 0;
                    const int maxWaitCount = 100; // 500 * 10ms = 5000ms

                    Logger.Debug("Waiting for pending callbacks to complete...");
                    while (Interlocked.Read(ref _receivePacketCount) > 0 && waitCount < maxWaitCount)
                    {
                        Thread.Sleep(10);
                        waitCount++;

                        // 关键改进: 检查 Dispatcher 是否已关闭,如果是则不需要等待
                        var dispatcher = Application.Current?.Dispatcher;
                        bool dispatcherShutdown = dispatcher == null ||
                                                 dispatcher.HasShutdownStarted ||
                                                 dispatcher.HasShutdownFinished;

                        if (dispatcherShutdown && waitCount > 10) // 给一点时间(100ms)让 pending 回调完成
                        {
                            Logger.Info($"Dispatcher shutdown detected, skipping callback wait. Remaining count={Interlocked.Read(ref _receivePacketCount)}");
                            break;
                        }

                        // 每等待 500ms 输出一次日志
                        if (waitCount % 50 == 0)
                        {
                            Logger.Debug($"Still waiting for callbacks... count={Interlocked.Read(ref _receivePacketCount)}, wait={waitCount * 10}ms");
                        }
                    }

                    if (waitCount >= maxWaitCount)
                    {
                        Logger.Warn("Disconnect timeout: some callbacks may still be running. Force closing.");
                    }
                    else
                    {
                        Logger.Info($"Callback wait completed. Remaining count={Interlocked.Read(ref _receivePacketCount)}");
                    }

                    // 步骤3: 关闭 C++ 层输入（带异常保护）
                    // 关键改进：先等待一小段时间，确保 C++ 层回调完全退出
                    Logger.Debug("Waiting 100ms for C++ callbacks to exit...");
                    Thread.Sleep(100);

                    try
                    {
                        Logger.Debug("Calling C++ CloseInput...");
                        int ret = UvcApi.CloseInput();
                        if (ret < 0)
                        {
                            Logger.Warn($"CloseInput returned: {ret}. FFmpeg context may already be closed.");
                        }
                        else
                        {
                            Logger.Info("UVC input closed successfully.");
                        }
                    }
                    catch (AccessViolationException ex)
                    {
                        // 捕获空指针访问异常
                        Logger.Error($"AccessViolationException during CloseInput: {ex.Message}. This indicates FFmpeg context issue.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Exception while closing UVC input: {ex.Message}");
                    }

                    // 步骤4: 重置计数器
                    Interlocked.Exchange(ref _receivePacketCount, 0);

                    // 步骤5: 额外等待，确保 C++ 层资源完全释放
                    Logger.Debug("Waiting for C++ resource cleanup...");
                    Thread.Sleep(200);  // 300ms 确保 avformat_close_input 完成
                    _setMode = DeviceConfig.Isp.SetMode.MJPG;
                    Logger.Info("UVC disconnected.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Disconnect exception: {ex.Message}", ex);
                }
                finally
                {
                    // 确保即使异常也重置标志
                    _isDisconnecting = false;
                }
            }
        }

        /// <summary>
        /// 重新连接UVC设备（带重试机制和异常处理）
        /// 改进版：防止与其他 Connect 调用产生竞态条件
        /// </summary>
        /// <param name="cameraDescriptor">设备描述符</param>
        /// <param name="retryCount">重试次数，默认2次</param>
        /// <param name="retryDelayMs">重试间隔（毫秒），默认1500ms</param>
        /// <returns>重连是否成功</returns>
        public async Task<bool> Reconnect(string cameraDescriptor, int retryCount = 2, int retryDelayMs = 100)
        {
            if (string.IsNullOrEmpty(cameraDescriptor))
            {
                Logger.Error("Reconnect failed: cameraDescriptor is null or empty");
                return false;
            }

            // 防止并发重连
            if (_isReconnecting)
            {
                Logger.Warn("Reconnect already in progress, skipping...");
                return false;
            }

            _isReconnecting = true;

            try
            {
                Logger.Info($"Starting reconnect process for: {cameraDescriptor} (max attempts: {retryCount + 1})");

                // 步骤1: 断开当前连接（带异常保护）
                try
                {
                    Disconnect();
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Exception during disconnect: {ex.Message}");
                }

                // 步骤2: 等待资源释放（异步，不阻塞调用线程）
                // 增加等待时间，确保 C++ 层资源完全释放（FFmpeg 需要更长时间）
                int totalWaitMs = retryDelayMs + 100;  // 额外等待 1000ms（原 500ms）
                Logger.Debug($"Waiting {totalWaitMs}ms for complete resource cleanup...");
                await Task.Delay(totalWaitMs);

                // 关键改进：等待期间检查是否被其他线程连接
                // 如果等待期间已经有连接建立，说明其他代码路径已经处理了连接
                if (_isConnected)
                {
                    Logger.Info("Connection established during wait, skipping explicit reconnect");
                    return true;  // 直接返回成功，避免重复断开/连接
                }

                // 步骤3: 尝试重新连接（带重试机制）
                for (int attempt = 1; attempt <= retryCount + 1; attempt++)
                {
                    try
                    {
                        Logger.Info($"Connection attempt {attempt}/{retryCount + 1} for: {cameraDescriptor}");
                        bool success = Connect(cameraDescriptor);

                        if (success)
                        {
                            Logger.Info($"Reconnect successful on attempt {attempt}");
                            return true;
                        }

                        Logger.Warn($"Connection attempt {attempt} failed");
                    }
                    catch (AccessViolationException ex)
                    {
                        // 捕获空指针访问异常
                        Logger.Error($"Connection attempt {attempt} AccessViolationException: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Connection attempt {attempt} threw exception: {ex.GetType().Name} - {ex.Message}");
                    }

                    // 如果还有重试机会，等待后再试
                    if (attempt < retryCount + 1)
                    {
                        Logger.Debug($"Waiting {retryDelayMs}ms before next attempt...");
                        await Task.Delay(retryDelayMs);
                    }
                }

                Logger.Error($"Reconnect failed after {retryCount + 1} attempts for: {cameraDescriptor}");
                return false;
            }
            finally
            {
                _isReconnecting = false;
            }
        }


        /// <summary>
        /// 通过软件方式复位USB设备（模拟重新插拔）
        /// 比Disconnect/Connect更彻底，会触发系统级的设备重新枚举
        /// </summary>
        /// <param name="deviceSymbolicLink">设备符号链接（例如："\\\\?\\USB#VID_1234&PID_5678#..."）</param>
        /// <param name="waitDisconnectMs">断开等待时间（毫秒），默认2000ms</param>
        /// <param name="waitConnectMs">连接等待时间（毫秒），默认3000ms</param>
        /// <returns>是否成功</returns>
        public bool SoftwareResetDevice(string deviceSymbolicLink, int waitDisconnectMs = 2000, int waitConnectMs = 3000)
        {
            if (string.IsNullOrEmpty(deviceSymbolicLink))
            {
                Logger.Error("SoftwareResetDevice failed: deviceSymbolicLink is null or empty");
                return false;
            }

            if (_disposed)
            {
                Logger.Error("SoftwareResetDevice failed: UvcReceiver is disposed");
                return false;
            }

            // 防止并发重连
            if (_isReconnecting)
            {
                Logger.Warn("SoftwareResetDevice blocked: Reconnect already in progress");
                return false;
            }

            Logger.Info($"========================================");
            Logger.Info($"Starting USB device software reset...");
            Logger.Info($"Device: {deviceSymbolicLink}");
            Logger.Info($"Wait times: disconnect={waitDisconnectMs}ms, connect={waitConnectMs}ms");
            Logger.Info($"========================================");

            // 标记正在重连，阻止设备变化事件的自动连接
            _isReconnecting = true;

            try
            {
                // 步骤1: 先断开UVC连接（释放视频流资源）
                if (_isConnected)
                {
                    Logger.Info("Step 1: Disconnecting UVC stream first...");
                    try
                    {
                        Disconnect();
                        Logger.Info("UVC stream disconnected successfully.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Exception during UVC disconnect: {ex.Message}");
                    }

                    // 等待资源释放
                    Logger.Debug($"Waiting 500ms for UVC resource cleanup...");
                    Thread.Sleep(500);
                }

                // 步骤2: 调用C++层复位USB设备
                Logger.Info("Step 2: Calling C++ SoftwareResetUsbDevice...");
                bool resetSuccess = DeviceApi.SoftwareResetUsbDeviceEx(deviceSymbolicLink, waitDisconnectMs, waitConnectMs);

                if (!resetSuccess)
                {
                    Logger.Error("✗ SoftwareResetUsbDevice failed!");
                    return false;
                }

                Logger.Info("✓ USB device reset completed successfully.");

                /*
                // 步骤3: 等待设备完全就绪
                Logger.Debug($"Waiting 1000ms for device to fully ready...");
                Thread.Sleep(1000);

                // 步骤4: 重新连接UVC
                Logger.Info("Step 3: Reconnecting UVC stream...");
                bool reconnectSuccess = Connect(deviceSymbolicLink);

                if (reconnectSuccess)
                {
                    Logger.Info($"✓ Software reset completed! Device reconnected: {VideoWidth}x{VideoHeight}");
                    Logger.Info($"========================================");
                    return true;
                }
                else
                {
                    Logger.Error("✗ Software reset completed but UVC reconnect failed!");
                    Logger.Info($"========================================");
                    return false;
                }
                */
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"SoftwareResetDevice exception: {ex.GetType().Name} - {ex.Message}", ex);
                Logger.Info($"========================================");
                return false;
            }
            finally
            {
                // 确保重连标志被重置
                _isReconnecting = false;
            }
        }

        #endregion

        #region RAW帧捕获

        // 添加一个标志位来指示是否正在捕获RAW帧
        private volatile bool _isCapturingRawFrame = false;

        // 添加一个临时变量来存储RAW文件保存路径
        private string _rawCaptureSavePath = string.Empty;

        // 添加一个新的标志用于连续保存模式
        private bool _isContinuouslyCapturingRawFrames = false;
        // 添加异步处理相关的字段
        private readonly SemaphoreSlim _asyncSaveSemaphore = new SemaphoreSlim(4, 4); // 控制并发数
        private readonly Queue<Task> _pendingSaveTasks = new Queue<Task>();
        private readonly object _taskQueueLock = new object();

        public bool IsCapturingRawFrames = false;
        // 计数器现在使用Interlocked保证线程安全
        private volatile int _continuousRawFrameCount = 0;

        private int _continuousRawFramesMax = 10000;

        private string _continuousRawCaptureSavePath = null;
        
        // 添加事件用于通知UI界面已达到最大连续保存帧数
        public event Action<int> ContinuousRawFrameLimitReached;

        /// <summary>
        /// 触发RAW帧捕获（在ProcessVideoData中实现）
        /// </summary>
        /// <param name="savePath">保存路径</param>
        /// <returns>是否成功触发</returns>
        private bool TriggerRawFrameCapture(string savePath)
        {
            if (!_isConnected || string.IsNullOrEmpty(savePath))
                return false;

            try
            {
                // 检查当前模式是否支持RAW捕获
                if (_setMode == DeviceConfig.Isp.SetMode.RAW8 || _setMode == DeviceConfig.Isp.SetMode.RAW10) // 检查是否为Bayer格式
                {
                    // 确保目录存在
                    string directory = Path.GetDirectoryName(savePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // 设置保存路径和捕获标志
                    _rawCaptureSavePath = savePath;
                    _isCapturingRawFrame = true;

                    Logger.Info($"RAW frame capture triggered, will save to: {savePath}");
                    return true;
                }
                else
                {
                    Logger.Warn($"RAW capture skipped: Current SetMode is {_setMode}, not RAW compatible");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"TriggerRawFrameCapture failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 捕获一帧RAW图像
        /// </summary>
        /// <param name="path">保存路径</param>
        /// <returns>是否成功</returns>
        public bool CaptureRawImage(string path)
        {
            if (!_isConnected || string.IsNullOrEmpty(path))
                return false;

            try
            {
                if (_setMode == DeviceConfig.Isp.SetMode.RAW8 || _setMode == DeviceConfig.Isp.SetMode.RAW10)
                {
                    Logger.Debug($"Capturing raw image to: {path}");
                    //UvcApi.CaptureOneRawFrame(path);

                    // 使用新的实现方式，通过ProcessVideoData保存
                    bool result = TriggerRawFrameCapture(path);
                    if (result)
                    {
                        Logger.Info($"Raw image capture triggered successfully: {path}");
                    }
                    else
                    {
                        Logger.Warn($"Raw image capture failed: {path}");
                    }
                    return result;
                }
                else
                {
                    Logger.Warn($"CaptureRawImage skipped: Current SetMode is {_setMode}, not RAW");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"CaptureRawImage failed: {ex.Message}");
                return false;
            }
        }

        public bool StartCaptureRawImage(string path, int maxFrames = 10000)
        {
            if (!_isConnected || string.IsNullOrEmpty(path))
                return false;

            try
            {
                if (_setMode == DeviceConfig.Isp.SetMode.RAW8 || _setMode == DeviceConfig.Isp.SetMode.RAW10 || _setMode == DeviceConfig.Isp.SetMode.MJPG)
                {
                    Logger.Debug($"Capturing raw image to: {path}");

                    // 使用新的实现方式，通过ProcessVideoData保存
                    bool result = StartContinuousRawFrameCapture(path, maxFrames);
                    if (result)
                    {
                        Logger.Info($"Raw image capture triggered successfully: {path}");
                    }
                    else
                    {
                        Logger.Warn($"Raw image capture failed: {path}");
                    }
                    return result;
                }
                else
                {
                    Logger.Warn($"CaptureRawImage skipped: Current SetMode is {_setMode}, not RAW");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"CaptureRawImage failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 开始连续保存RAW帧到指定目录
        /// </summary>
        /// <param name="directoryPath">保存目录路径</param>
        /// <returns>是否成功启动连续保存</returns>
        private bool StartContinuousRawFrameCapture(string directoryPath, int maxFrames)
        {
            if (!_isConnected || string.IsNullOrEmpty(directoryPath))
                return false;

            try
            {
                // 检查当前模式是否支持RAW捕获
                if (_setMode == DeviceConfig.Isp.SetMode.RAW8 || _setMode == DeviceConfig.Isp.SetMode.RAW10 || _setMode == DeviceConfig.Isp.SetMode.MJPG)
                {
                    // 确保目录存在
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    // 设置连续保存参数
                    _continuousRawCaptureSavePath = directoryPath;
                    _isContinuouslyCapturingRawFrames = true;
                    _continuousRawFrameCount = 0;
                    _continuousRawFramesMax = maxFrames;
                    IsCapturingRawFrames = true;

                    Logger.Info($"Continuous RAW frame capture started, saving to: {directoryPath}");
                    return true;
                }
                else
                {
                    Logger.Warn($"Continuous RAW capture skipped: Current SetMode is {_setMode}, not RAW compatible");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"StartContinuousRawFrameCapture failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止连续保存RAW帧
        /// </summary>
        /// <returns>是否成功停止连续保存</returns>
        public bool StopContinuousRawFrameCapture()
        {
            if (_isContinuouslyCapturingRawFrames)
            {
                _isContinuouslyCapturingRawFrames = false;
                Logger.Info($"Continuous RAW frame capture stopped, total frames saved: {_continuousRawFrameCount}");

                _continuousRawCaptureSavePath = null;
                _continuousRawFrameCount = 0;
                IsCapturingRawFrames = false;

                return true;
            }

            return false;
        }

        /// <summary>
        /// 等待所有待处理的异步保存任务完成
        /// </summary>
        private async Task WaitForPendingSaveTasksAsync()
        {
            Task[] tasksToWait;

            lock (_taskQueueLock)
            {
                tasksToWait = _pendingSaveTasks.ToArray();
            }

            if (tasksToWait.Length > 0)
            {
                Logger.Info($"Waiting for {tasksToWait.Length} pending save tasks to complete...");
                await Task.WhenAll(tasksToWait);

                lock (_taskQueueLock)
                {
                    _pendingSaveTasks.Clear();
                }

                Logger.Info("All pending save tasks completed.");
            }
        }

        /// <summary>
        /// 停止连续保存RAW帧
        /// </summary>
        /// <returns>是否成功停止连续保存</returns>
        public async Task<bool> StopContinuousRawFrameCaptureAsync()
        {
            if (_isContinuouslyCapturingRawFrames)
            {
                // 首先停止接受新的保存请求
                _isContinuouslyCapturingRawFrames = false;

                // 等待所有待处理的异步保存任务完成
                await WaitForPendingSaveTasksAsync();

                Logger.Info($"Continuous RAW frame capture stopped, total frames saved: {_continuousRawFrameCount}");

                _continuousRawCaptureSavePath = null;
                _continuousRawFrameCount = 0;

                IsCapturingRawFrames = false;

                return true;
            }

            return false;
        }

        /// <summary>
        /// 保存RAW帧到文件
        /// </summary>
        /// <param name="rawData">原始数据</param>
        /// <param name="pixelFormat">像素格式</param>
        private void SaveRawFrameToFile(byte[] rawData, int pixelFormat)
        {
            if (string.IsNullOrEmpty(_rawCaptureSavePath))
            {
                Logger.Warn("RAW capture path is empty, skipping save");
                return;
            }

            SaveRawFrameToFile(rawData, pixelFormat, _rawCaptureSavePath);
        }

        private void SaveRawFrameToFile(byte[] rawData, int pixelFormat, string savePath)
        {
            try
            {
                if (string.IsNullOrEmpty(savePath))
                {
                    Logger.Warn("RAW capture path is empty, skipping save");
                    return;
                }

                byte[] dataToSave = rawData;

                // 如果需要将每像素1字节转换为每像素2字节
                if (_setMode == DeviceConfig.Isp.SetMode.RAW8)
                {
                    dataToSave = ConvertRaw8ToRaw8ExtendedLittleEndian(rawData, _videoWidth, _videoHeight);
                }
                else if (_setMode == DeviceConfig.Isp.SetMode.MJPG)
                {
                    // 确保文件扩展名为.jpg/.jpeg
                    if (!savePath.ToLower().EndsWith(".jpg") && !savePath.ToLower().EndsWith(".jpeg"))
                    {
                        savePath = Path.ChangeExtension(savePath, ".jpg");
                    }
                    dataToSave = EnsureValidJpegFormat(rawData);
                }

                // 创建文件头信息（便于后续识别格式）
                using (var fs = new FileStream(_rawCaptureSavePath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(fs))
                {
                    // 写入RAW文件头信息
                    //writer.Write(Encoding.ASCII.GetBytes("RAW")); // 标识符
                    //writer.Write(pixelFormat); // 像素格式
                    //writer.Write(_videoWidth); // 宽度
                    //writer.Write(_videoHeight); // 高度
                    //writer.Write(DateTime.UtcNow.ToBinary()); // 时间戳

                    // 写入实际的RAW数据
                    writer.Write(dataToSave);
                }

                //Logger.Info($"RAW frame saved successfully: {_rawCaptureSavePath}, " +
                //           $"Size: {dataToSave.Length} bytes, Format: {pixelFormat}, " +
                //           $"Resolution: {_videoWidth}x{_videoHeight}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save RAW frame to file: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 异步保存RAW帧到文件
        /// </summary>
        /// <param name="rawData">原始数据</param>
        /// <param name="pixelFormat">像素格式</param>
        /// <param name="savePath">保存路径</param>
        private async Task SaveRawFrameToFileAsync(byte[] rawData, int pixelFormat, string savePath)
        {
            // 获取信号量许可，限制并发数
            await _asyncSaveSemaphore.WaitAsync();

            try
            {
                await Task.Run(() =>
                {
                    if (string.IsNullOrEmpty(savePath))
                    {
                        Logger.Warn("RAW capture path is empty, skipping save");
                        return;
                    }

                    byte[] dataToSave = rawData;

                    // 如果需要将每像素1字节转换为每像素2字节
                    if (_setMode == DeviceConfig.Isp.SetMode.RAW8)
                    {
                        dataToSave = ConvertRaw8ToRaw8ExtendedLittleEndian(rawData, _videoWidth, _videoHeight);
                    }
                    else if (_setMode == DeviceConfig.Isp.SetMode.MJPG)
                    {
                        // 确保文件扩展名为.jpg/.jpeg
                        //if (!savePath.ToLower().EndsWith(".jpg") && !savePath.ToLower().EndsWith(".jpeg"))
                        //{
                        //    savePath = Path.ChangeExtension(savePath, ".jpg");
                        //}
                        dataToSave = ConvertRgb24ToJpeg(rawData, _videoWidth, _videoHeight);
                    }

                    // 创建文件头信息（便于后续识别格式）
                    using (var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                    using (var writer = new BinaryWriter(fs))
                    {
                        // 写入RAW文件头信息
                        //writer.Write(Encoding.ASCII.GetBytes("RAW")); // 标识符
                        //writer.Write(pixelFormat); // 像素格式
                        //writer.Write(_videoWidth); // 宽度
                        //writer.Write(_videoHeight); // 高度
                        //writer.Write(DateTime.UtcNow.ToBinary()); // 时间戳

                        // 写入实际的RAW数据
                        writer.Write(dataToSave);
                    }

                    //Logger.Info($"RAW frame saved successfully: {savePath}, " +
                    //           $"Size: {dataToSave.Length} bytes, Format: {pixelFormat}, " +
                    //           $"Resolution: {_videoWidth}x{_videoHeight}");
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save RAW frame to file asynchronously: {ex.Message}", ex);
            }
            finally
            {
                // 释放信号量许可
                _asyncSaveSemaphore.Release();
            }
        }

        /// <summary>
        /// 确保数据是有效的JPEG格式（添加SOI和EOI标记）
        /// </summary>
        /// <param name="jpegData">原始JPEG数据</param>
        /// <returns>确保格式正确的JPEG数据</returns>
        private byte[] EnsureValidJpegFormat(byte[] jpegData)
        {
            if (jpegData == null || jpegData.Length < 2)
            {
                Logger.Error("Invalid JPEG data: too short");
                return jpegData;
            }

            // 检查是否已经包含完整的JPEG头和尾，避免不必要的数据复制
            bool hasSOI = jpegData[0] == 0xFF && jpegData[1] == 0xD8;
            bool hasEOI = jpegData.Length >= 2 &&
                         jpegData[jpegData.Length - 2] == 0xFF &&
                         jpegData[jpegData.Length - 1] == 0xD9;

            // 如果数据已经具有正确的SOI和EOI标记，则直接返回原数据
            if (hasSOI && hasEOI)
            {
                return jpegData;
            }

            // 计算结果数组大小
            int resultSize = jpegData.Length;
            if (!hasSOI) resultSize += 2; // 需要添加SOI标记
            if (!hasEOI) resultSize += 2; // 需要添加EOI标记

            byte[] result = new byte[resultSize];
            int offset = 0;

            // 添加SOI标记 (0xFFD8) 如果缺失
            if (!hasSOI)
            {
                result[offset++] = 0xFF;
                result[offset++] = 0xD8;
            }

            // 复制原始数据
            Array.Copy(jpegData, 0, result, offset, jpegData.Length);
            offset += jpegData.Length;

            // 添加EOI标记 (0xFFD9) 如果缺失
            if (!hasEOI)
            {
                result[offset++] = 0xFF;
                result[offset] = 0xD9;
            }

            return result;
        }

        #endregion

        #region 静态回调(从C++调用)

        private static int OnPlayStateChangeStatic(bool isPlaying)
        {
            var instance = _instance.Value;
            if (instance._disposed) return 0;

            try
            {
                Logger.Debug($"Play state changed: {(isPlaying ? "Playing" : "Stopped")}");

                instance._statusChange?.Invoke(isPlaying);
            }
            catch (Exception ex)
            {
                Logger.Error("StatusChange callback error.", ex);
            }
            return 0;
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        private static int OnReceiveDataStatic(IntPtr videoData, int size, int pixelFormat, IntPtr user_data)
        {
            var instance = _instance.Value;

            // 关键保护：在 disposed 或未连接时立即返回，不访问任何资源
            if (instance._disposed || !instance._isConnected || instance._isReconnecting)
                return 0;

            // 检查视频数据指针有效性
            if (videoData == IntPtr.Zero || size <= 0)
            {
                Logger.Warn($"Invalid video data: ptr={videoData}, size={size}");
                return -1;
            }

            try
            {
                // 快速检查是否有订阅者
                //instance._dataReceiveLock.EnterReadLock();
                bool hasSubscriber = instance._dataReceive != null;
                //instance._dataReceiveLock.ExitReadLock();

                if (!hasSubscriber)
                    return 0;

                // 检查限流
                if (Interlocked.Read(ref instance._receivePacketCount) > MaxPacketCount)
                    return 0;

                // 复制缓冲区数据（这是可能崩溃的地方，需要 try-catch）
                byte[] dataBuffer;
                try
                {
                    dataBuffer = new byte[size];
                    if (size > 50 * 1024 * 1024) // 设置为 50MB 安全阈值，可按需调整
                    {
                        Logger.Error($"Marshal.Copy aborted: Size [{size}] is abnormally large, possibly corrupted packet.");
                        return -1;
                    }
                    //Console.WriteLine($"Size[{ size}] is abnormally large");
                    Marshal.Copy(videoData, dataBuffer, 0, size);
                }
                catch (AccessViolationException ex)
                {
                    // 捕获空指针访问 - 这说明 FFmpeg 的 pInStreamFormatCtx 已被释放
                    Logger.Error($"1.AccessViolationException in OnReceiveDataStatic: {ex.Message} -- Size [{size}]. FFmpeg context may be invalid.");
                    return -1;
                }
                catch (OutOfMemoryException)
                {
                    Logger.Error($"OOM when allocating {size} bytes for video frame.");
                    return -1;
                }
                catch (Exception ex)
                {
                    // 可能是 AccessViolationException，说明 FFmpeg 上下文已释放
                    Logger.Error($"Marshal.Copy failed (possible FFmpeg context issue): {ex.Message}");
                    return -1;
                }

                Interlocked.Increment(ref instance._receivePacketCount);

                // 关键修复: 应用退出时,Dispatcher 可能已关闭,直接跳过调度
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                {
                    Logger.Debug("Dispatcher is shutting down, skipping video frame processing.");
                    Interlocked.Decrement(ref instance._receivePacketCount);
                    return 0;
                }

                // 异步调度到UI线程(使用BeginInvoke避免阻塞)
                try
                {
                    if (Instance.IsRawBayerFormat(pixelFormat))
                    {
                        int pixel = 1;
                        if (Instance.SetMode == DeviceConfig.Isp.SetMode.RAW10)
                            pixel = 2; // 每像素2字节
                        if (Instance.FlipImage == 1)
                            dataBuffer = FlipImageVertically(dataBuffer, Instance.VideoWidth, Instance.VideoHeight, pixel);
                        else if (Instance.FlipImage == 2)
                            dataBuffer = FlipImageHorizontally(dataBuffer, Instance.VideoWidth, Instance.VideoHeight, pixel);
                        else if (Instance.FlipImage == 3)
                            dataBuffer = RotateImage180(dataBuffer, Instance.VideoWidth, Instance.VideoHeight, pixel);
                    }

                    dispatcher.BeginInvoke(
                        DispatcherPriority.Normal,
                        new Action(() => instance.ProcessVideoData(dataBuffer, pixelFormat)));
                }
                catch (Exception ex)
                {
                    Logger.Error($"Dispatcher error: {ex.Message}", ex);
                    Interlocked.Decrement(ref instance._receivePacketCount);
                }

                return 0;
            }
            catch (AccessViolationException ex)
            {
                // 捕获空指针访问 - 这说明 FFmpeg 的 pInStreamFormatCtx 已被释放
                Logger.Error($"2.AccessViolationException in OnReceiveDataStatic: {ex.Message}. FFmpeg context may be invalid.");
                return -1;
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected error in OnReceiveDataStatic: {ex.GetType().Name} - {ex.Message}");
                return -1;
            }
        }

        private static int OnReceiveYuvDataStatic(IntPtr yuvData)
        {
            var instance = _instance.Value;
            if (instance._disposed) return 0;

            try
            {
                //instance._yuvDataReceiveLock.EnterReadLock();
                try
                {
                    instance._yuvDataReceive?.Invoke(yuvData);
                }
                finally
                {
                    //instance._yuvDataReceiveLock.ExitReadLock();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("YuvDataReceive callback error.", ex);
            }
            return 0;
        }

        private static int OnReceiveRawDataStatic(IntPtr rawData, int dataSize, int pixelFormat, int width, int height, IntPtr user_data)
        {
            var instance = _instance.Value;
            if (instance._disposed) return 0;

            try
            {
                instance._rawDataReceive?.Invoke(rawData, dataSize, pixelFormat, width, height);
            }
            catch (AccessViolationException ex)
            {
                // 捕获非托管内存访问异常，防止进程崩溃
                Logger.Error($"AccessViolationException in RawDataReceive callback: {ex.Message}. Data may be corrupted.");
            }
            catch (Exception ex)
            {
                Logger.Error("RawDataReceive callback error.", ex);
            }
            return 0;
        }

        /// <summary>
        /// 判断是否为 RAW Bayer 格式
        /// </summary>
        private bool IsRawBayerFormat(int pixelFormat)
        {
            // RAW Bayer 格式判断：
            // C++ 端 UvcDataType 枚举值：
            //   UVC_DATA_YUYV422 = 3
            //   UVC_DATA_UYVY422 = 4
            //   UVC_DATA_YUV420P = 5
            //   UVC_DATA_NV12    = 6
            //   UVC_DATA_RGB24   = 7
            //   UVC_DATA_GRAY8   = 8
            //   UVC_DATA_MJPEG   = 1 (compressed, not raw)
            //   UVC_DATA_H264    = 2 (compressed, not raw)
            //
            // 旧的 RAW Bayer 判断（pixelFormat 100-103）保留以兼容旧代码
            // 新的判断：UVC_DATA_YUV420P 及以下都是未压缩 RAW 格式
            //bool isRaw = (pixelFormat >= 100 && pixelFormat <= 103) ||  // 旧格式：10-bit RAW
            //             (pixelFormat >= 3 && pixelFormat <= 8);        // 新格式：UvcDataType 枚举

            //_isRawBayer = isRaw;
            if (_setMode == DeviceConfig.Isp.SetMode.RAW8 || _setMode == DeviceConfig.Isp.SetMode.RAW10)
            {
                _isRawBayer = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 判断是否为 YUYV422 格式
        /// 根据界面 SetMode 的选择项进行处理：SetMode.YUV 时为 YUV 格式
        /// </summary>
        private bool IsYuyv422Format(int pixelFormat)
        {
            // 根据界面 SetMode 设置判断
            if (_setMode == DeviceConfig.Isp.SetMode.YUV)
            {
                return true;
            }

            // 兼容旧代码：直接通过 pixelFormat 判断
            // YUYV422: 旧代码 pixelFormat = 1，新代码 UVC_DATA_YUYV422 = 3
            return false; //pixelFormat == 1 || pixelFormat == 3;
        }

        /// <summary>
        /// 在后台线程处理视频数据，避免阻塞UI线程
        /// CPU密集型操作（Bayer转换）和I/O操作在后台完成
        /// 仅UI更新通过Dispatcher回到UI线程
        /// </summary>
        private void ProcessVideoData(byte[] dataBuffer, int pixelFormat)
        {
            // 获取UI线程Dispatcher，用于后续回调
            var dispatcher = Application.Current?.Dispatcher;

            // 使用线程池处理CPU密集型和I/O操作
            ThreadPool.QueueUserWorkItem(state =>
            {
                //_dataReceiveLock.EnterReadLock();
                try
                {
                    // 检查是否需要保存当前帧为RAW数据（单帧捕获）
                    if (_isCapturingRawFrame)
                    {
                        // 在后台线程保存原始数据到文件
                        SaveRawFrameToFile(dataBuffer, pixelFormat);
                        _isCapturingRawFrame = false; // 重置标志
                    }

                    // 检查是否需要连续保存当前帧为RAW数据
                    if (_isContinuouslyCapturingRawFrames && !string.IsNullOrEmpty(_continuousRawCaptureSavePath))
                    {
                        // 检查是否已达到最大连续保存帧数
                        if (_continuousRawFrameCount >= _continuousRawFramesMax)
                        {
                            // 已达到最大帧数，停止连续保存
                            _isContinuouslyCapturingRawFrames = false;

                            // 可选：调用停止方法来清理资源
                            _continuousRawCaptureSavePath = null;
                            IsCapturingRawFrames = false;

                            // 触发事件通知UI界面已达到最大连续保存帧数（需要在UI线程）
                            if (dispatcher != null && !dispatcher.HasShutdownStarted)
                            {
                                dispatcher.BeginInvoke(new Action(() =>
                                    ContinuousRawFrameLimitReached?.Invoke(_continuousRawFramesMax)));
                            }

                            // 跳出本次处理
                            goto SkipContinuousCapture;
                        }

                        // 创建数据副本以避免异步处理时数据被修改
                        byte[] bufferCopy = new byte[dataBuffer.Length];
                        Buffer.BlockCopy(dataBuffer, 0, bufferCopy, 0, dataBuffer.Length);

                        // 生成8位零填充的纯数字文件名
                        int currentFrameNumber = Interlocked.Increment(ref _continuousRawFrameCount) - 1;
                        string fileName;
                        if (_setMode == DeviceConfig.Isp.SetMode.MJPG)
                        {
                            fileName = $"{currentFrameNumber:D8}.JPG";
                        }
                        else
                        {
                            fileName = $"{currentFrameNumber:D8}.RAW";
                        }
                        string fullPath = Path.Combine(_continuousRawCaptureSavePath, fileName);

                        // 异步保存原始数据到文件
                        Task saveTask = SaveRawFrameToFileAsync(bufferCopy, pixelFormat, fullPath);

                        // 添加到待处理任务队列
                        lock (_taskQueueLock)
                        {
                            _pendingSaveTasks.Enqueue(saveTask);

                            // 清理已完成的任务
                            while (_pendingSaveTasks.Count > 0 && _pendingSaveTasks.Peek().IsCompleted)
                            {
                                _pendingSaveTasks.Dequeue();
                            }
                        }
                    }

                SkipContinuousCapture:
                    // 判断数据类型并进行处理
                    byte[] dataToDisplay;

                    if (IsRawBayerFormat(pixelFormat))
                    {
                        // 在后台线程执行CPU密集型的Bayer → RGB转换
                        dataToDisplay = ConvertBayerToRgb(dataBuffer, _videoWidth, _videoHeight);
                    }
                    else
                    {
                        _isRawBayer = false;
                        // 标准 RGB 数据（MJPEG 解码后等），直接使用
                        dataToDisplay = dataBuffer;
                    }

                    // 在UI线程触发数据接收事件，更新显示
                    if (dispatcher != null && !dispatcher.HasShutdownStarted)
                    {
                        dispatcher.BeginInvoke(
                            DispatcherPriority.Send,
                            new Action(() => _dataReceive?.Invoke(dataToDisplay)));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("VideoDataHandler background processing error.", ex);
                }
                finally
                {
                    //_dataReceiveLock.ExitReadLock();
                    Interlocked.Decrement(ref _receivePacketCount);
                }
            });
        }

        /// <summary>
        /// Gamma 2.0 校正查找表 (LUT)
        /// 用于提升灰度图亮度，改善暗部细节
        /// </summary>
        private static readonly byte[] Gamma20Lut = new byte[256]
        {
            0x00,0x10,0x16,0x1b,0x20,0x23,0x27,0x2a,0x2d,0x30,0x32,0x35,0x37,0x39,0x3b,0x3e,0x40,0x42,0x44,0x45,0x47,0x49,0x4b,0x4c,0x4e,0x50,0x51,0x53,0x54,0x56,0x57,0x59,
            0x5a,0x5c,0x5d,0x5e,0x60,0x61,0x62,0x64,0x65,0x66,0x67,0x69,0x6a,0x6b,0x6c,0x6d,0x6e,0x70,0x71,0x72,0x73,0x74,0x75,0x76,0x77,0x78,0x79,0x7b,0x7c,0x7d,0x7e,0x7f,
            0x80,0x81,0x82,0x83,0x84,0x85,0x85,0x86,0x87,0x88,0x89,0x8a,0x8b,0x8c,0x8d,0x8e,0x8f,0x90,0x91,0x91,0x92,0x93,0x94,0x95,0x96,0x97,0x97,0x98,0x99,0x9a,0x9b,0x9c,
            0x9c,0x9d,0x9e,0x9f,0xa0,0xa0,0xa1,0xa2,0xa3,0xa4,0xa4,0xa5,0xa6,0xa7,0xa7,0xa8,0xa9,0xaa,0xaa,0xab,0xac,0xad,0xad,0xae,0xaf,0xb0,0xb0,0xb1,0xb2,0xb3,0xb3,0xb4,
            0xb5,0xb5,0xb6,0xb7,0xb7,0xb8,0xb9,0xba,0xba,0xbb,0xbc,0xbc,0xbd,0xbe,0xbe,0xbf,0xc0,0xc0,0xc1,0xc2,0xc2,0xc3,0xc4,0xc4,0xc5,0xc6,0xc6,0xc7,0xc7,0xc8,0xc9,0xc9,
            0xca,0xcb,0xcb,0xcc,0xcd,0xcd,0xce,0xce,0xcf,0xd0,0xd0,0xd1,0xd1,0xd2,0xd3,0xd3,0xd4,0xd4,0xd5,0xd6,0xd6,0xd7,0xd7,0xd8,0xd9,0xd9,0xda,0xda,0xdb,0xdc,0xdc,0xdd,
            0xdd,0xde,0xde,0xdf,0xe0,0xe0,0xe1,0xe1,0xe2,0xe2,0xe3,0xe4,0xe4,0xe5,0xe5,0xe6,0xe6,0xe7,0xe7,0xe8,0xe9,0xe9,0xea,0xea,0xeb,0xeb,0xec,0xec,0xed,0xed,0xee,0xef,
            0xef,0xf0,0xf0,0xf1,0xf1,0xf2,0xf2,0xf3,0xf3,0xf4,0xf4,0xf5,0xf5,0xf6,0xf6,0xf7,0xf7,0xf8,0xf9,0xf9,0xfa,0xfa,0xfb,0xfb,0xfc,0xfc,0xfd,0xfd,0xfe,0xfe,0xff,0xff
        };

        /// <summary>
        /// 将 RAW Bayer 数据转换为灰度图
        /// </summary>
        /// <param name="bayerData">RAW Bayer 数据</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <param name="pixelFormat">像素格式 (100=RGGB, 101=GRBG, 102=BGGR, 103=GBRG, 1=16-bit RAW)</param>
        /// <returns>灰度图数据 (Gray8 格式)</returns>
        private byte[] ConvertBayerToGray(byte[] bayerData, int width, int height)
        {
            if (bayerData == null)
                throw new ArgumentNullException(nameof(bayerData));
            if (width <= 0 || height <= 0)
                throw new ArgumentException($"无效的图像尺寸: {width}x{height}");

            byte[] grayData = new byte[width * height];

            try
            {
                if (_setMode == DeviceConfig.Isp.SetMode.RAW10)
                {
                    // ── 10-bit RAW → Gray8 (应用 Gamma 2.0 校正) ──
                    // 假设每像素 2 字节存储（16-bit，低 10 位有效），小端序
                    int expectedLen = width * height;
                    if (bayerData.Length < expectedLen * 2)
                    {
                        expectedLen = bayerData.Length / 2;  // 字节数转像素�?
                        //throw new ArgumentException($"10-bit 数据长度不足: 期望 {expectedLen}, 实际 {bayerData.Length}");
                    }

                    for (int i = 0; i < expectedLen; i++)
                    {
                        int srcIdx = i * 2;
                        // 小端读取 16-bit 原始值，取低 10 位，右移 2 位映射到 8-bit
                        ushort raw10 = (ushort)(bayerData[srcIdx] | (bayerData[srcIdx + 1] << 8));
                        byte grayValue = (byte)(raw10 >> 2);
                        // 应用 Gamma 2.0 校正提升亮度
                        grayData[i] = Gamma20Lut[grayValue];
                    }
                }
                else if (_setMode == DeviceConfig.Isp.SetMode.RAW8)
                {
                    // ── 8-bit Bayer → Gray8 (应用 Gamma 2.0 校正) ──
                    // Bayer 每个像素位置只有一个颜色通道（R/G/B），直接取该值作为灰度。
                    // 不同 Bayer 模式（RGGB/GRBG/BGGR/GBRG）只影响各位置对应哪个通道，
                    // 但灰度输出无需区分模式，直接透传原始值即可。
                    int expectedLen = width * height;
                    if (bayerData.Length < expectedLen)
                    {
                        expectedLen = bayerData.Length; // 保护性调整，避免越界
                        //throw new ArgumentException($"10-bit 数据长度不足: 期望 {expectedLen}, 实际 {bayerData.Length}");
                    }
                    //int copyLen = Math.Min(bayerData.Length, expectedLen);

                    for (int i = 0; i < expectedLen; i++)
                    {
                        // 应用 Gamma 2.0 校正提升亮度
                        grayData[i] = Gamma20Lut[bayerData[i]];
                    }

                }

                /*
                else
                {
                    // 8-bit RAW 数据：进行均值滤波去马赛克

                    // 边缘像素直接赋值（避免数组越界）
                    for (int x = 0; x < width; x++)
                    {
                        grayData[x] = bayerData[x]; // 第一行
                        grayData[(height - 1) * width + x] = bayerData[(height - 1) * width + x]; // 最后一行
                    }
                    for (int y = 0; y < height; y++)
                    {
                        grayData[y * width] = bayerData[y * width]; // 第一列
                        grayData[y * width + width - 1] = bayerData[y * width + width - 1]; // 最后一列
                    }

                    // 核心区域：取周围 4 个像素的平均值（快速消除马赛克和偏绿）
                    for (int y = 1; y < height - 1; y++)
                    {
                        for (int x = 1; x < width - 1; x++)
                        {
                            int idx = y * width + x;

                            // 取当前像素 + 上下左右四个像素的平均值
                            // 这种简单的均值滤波能极大地抹平 RGGB 带来的绿色突兀感
                            int sum = bayerData[idx]
                                    + bayerData[idx - 1]
                                    + bayerData[idx + 1]
                                    + bayerData[idx - width]
                                    + bayerData[idx + width];

                            grayData[idx] = (byte)(sum / 5);
                        }
                    }
                }

                // 判断是否为 16-bit RAW 数据
                bool is16Bit = IsRawBayerFormat(pixelFormat);// (pixelFormat == 1);
                
                if (is16Bit)
                {
                    // 16-bit RAW 数据：每像素2字节，需要转换为8-bit
                    // 简单方案：取高8位（右移8位）或根据实际位深调整
                    int pixelCount = width * height;
                    int srcStride = 2; // 每像素2字节
                    
                    for (int i = 0; i < pixelCount; i++)
                    {
                        // 取高8位（假设数据是 8-16 bit 有效范围）
                        // 如果是 10-bit 数据，可能需要 (high << 2) | (low >> 6)
                        int highByte = bayerData[i * srcStride + 1];
                        int lowByte = bayerData[i * srcStride + 0];
                        
                        // 方案1：直接使用高8位（适用于 8-bit 有效数据存储在高位）
                        grayData[i] = (byte)highByte;
                        
                        // 方案2：如果是完整的 16-bit 数据，需要缩放
                        // ushort pixel16 = (ushort)((highByte << 8) | lowByte);
                        // grayData[i] = (byte)(pixel16 >> 8); // 或者根据实际位深调整
                    }
                }
                else
                {
                    // 8-bit RAW 数据：进行均值滤波去马赛克
                    
                    // 边缘像素直接赋值（避免数组越界）
                    for (int x = 0; x < width; x++)
                    {
                        grayData[x] = bayerData[x]; // 第一行
                        grayData[(height - 1) * width + x] = bayerData[(height - 1) * width + x]; // 最后一行
                    }
                    for (int y = 0; y < height; y++)
                    {
                        grayData[y * width] = bayerData[y * width]; // 第一列
                        grayData[y * width + width - 1] = bayerData[y * width + width - 1]; // 最后一列
                    }

                    // 核心区域：取周围 4 个像素的平均值（快速消除马赛克和偏绿）
                    for (int y = 1; y < height - 1; y++)
                    {
                        for (int x = 1; x < width - 1; x++)
                        {
                            int idx = y * width + x;

                            // 取当前像素 + 上下左右四个像素的平均值
                            // 这种简单的均值滤波能极大地抹平 RGGB 带来的绿色突兀感
                            int sum = bayerData[idx]
                                    + bayerData[idx - 1]
                                    + bayerData[idx + 1]
                                    + bayerData[idx - width]
                                    + bayerData[idx + width];

                            grayData[idx] = (byte)(sum / 5);
                        }
                    }
                }
                */

            }
            catch (Exception ex)
            {
                Logger.Error($"ConvertBayerToGray error: {ex.Message}");
                // 出错时返回原始数据
                return bayerData;
            }

            return grayData;
        }

        /// <summary>
        /// 梯度感知 G 通道插值（参考 C++ DemosaicGainG）
        /// 根据水平/垂直方向的梯度差异自适应选择插值方向，提升边缘质量
        /// </summary>
        /// <param name="matrix">5x5 窗口数据（扁平化数组）</param>
        /// <param name="y">中心像素 Y 坐标（在矩阵中）</param>
        /// <param name="x">中心像素 X 坐标（在矩阵中）</param>
        /// <param name="size">矩阵宽度（5）</param>
        /// <returns>插值后的 G 通道值</returns>
        private static int DemosaicGainG(int[] matrix, int y, int x, int size)
        {
            // 边界钳位，确保访问不越界
            int ym1 = y > 0 ? y - 1 : y;      // y-1 钳位
            int yp1 = y < size - 1 ? y + 1 : y;  // y+1 钳位
            int xm1 = x > 0 ? x - 1 : x;      // x-1 钳位
            int xp1 = x < size - 1 ? x + 1 : x;  // x+1 钳位

            // 计算水平方向梯度（左右差异）
            int dh_tmp1 = Math.Abs(matrix[ym1 * size + xm1] - matrix[ym1 * size + xp1]);
            int dh_tmp2 = Math.Abs(matrix[y * size + xm1] - matrix[y * size + xp1]);
            int dh_tmp3 = Math.Abs(matrix[yp1 * size + xm1] - matrix[yp1 * size + xp1]);
            int dh = (2 * dh_tmp2 + dh_tmp1 + dh_tmp3) / 4;

            // 计算垂直方向梯度（上下差异）
            int dv_tmp1 = Math.Abs(matrix[ym1 * size + xm1] - matrix[yp1 * size + xm1]);
            int dv_tmp2 = Math.Abs(matrix[ym1 * size + x] - matrix[yp1 * size + x]);
            int dv_tmp3 = Math.Abs(matrix[ym1 * size + xp1] - matrix[yp1 * size + xp1]);
            int dv = (2 * dv_tmp2 + dv_tmp1 + dv_tmp3) / 4;

            // 根据梯度比选择权重（沿边缘方向插值更准确）
            int wh, wv;
            if (dv > 4 * dh) { wh = 8; wv = 0; }      // 水平边缘，用水平插值
            else if (dv > 3 * dh) { wh = 7; wv = 1; }
            else if (dv > 2 * dh) { wh = 6; wv = 2; }
            else if (dv > dh) { wh = 5; wv = 3; }
            else if (dh > 4 * dv) { wh = 0; wv = 8; }  // 垂直边缘，用垂直插值
            else if (dh > 3 * dv) { wh = 1; wv = 7; }
            else if (dh > 2 * dv) { wh = 2; wv = 6; }
            else if (dh > dv) { wh = 3; wv = 5; }
            else { wh = 4; wv = 4; }  // 平滑区域，平均

            // 水平插值（左右像素平均）
            int gh = (matrix[y * size + xm1] + matrix[y * size + xp1]) / 2;
            // 垂直插值（上下像素平均）
            int gv = (matrix[ym1 * size + x] + matrix[yp1 * size + x]) / 2;

            // 加权融合
            return (gh * wh + gv * wv) / 8;
        }

        /// <summary>
        /// 快速 G 通道插值（简化版梯度感知，内联优化）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FastInterpolateG(int left, int right, int top, int bottom)
        {
            // 简化梯度判断：只用中心行/列的梯度
            int dh = Math.Abs(left - right);
            int dv = Math.Abs(top - bottom);

            // 放宽阈值，更倾向于四向平均（保持 G 通道亮度）
            if (dv > 4 * dh)
                return (left + right) >> 1;  // 强水平边缘，用水平插值
            else if (dh > 4 * dv)
                return (top + bottom) >> 1;  // 强垂直边缘，用垂直插值
            else
                return (left + right + top + bottom) >> 2;  // 平滑/弱边缘区域，四向平均
        }

        /// <summary>
        /// 将 RAW Bayer 数据转换为 RGB24 格式
        /// 使用快速梯度感知 G 通道插值，优化性能
        /// </summary>
        /// <param name="bayerData">RAW Bayer 数据</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <returns>RGB24 格式数据 (每像素3字节: R,G,B)</returns>
        private unsafe byte[] ConvertBayerToRgb(byte[] bayerData, int width, int height)
        {
            if (bayerData == null)
                throw new ArgumentNullException(nameof(bayerData));
            if (width <= 0 || height <= 0)
                throw new ArgumentException($"无效的图像尺寸: {width}x{height}");
            try
            {
                int pixelCount = width * height;
                byte[] rgbData = new byte[pixelCount * 3];

                // 处理RAW10转8位
                byte[] bayer8Data = _setMode == DeviceConfig.Isp.SetMode.RAW10
                    ? Convert10BitTo8Bit(bayerData, width, height)
                    : bayerData;

                fixed (byte* pBayer = bayer8Data)
                fixed (byte* pRgb = rgbData)
                {
                    int rgbStride = width * 3; // 每行RGB字节数
                    DeviceConfig.Isp.BayerMode bayerMode = _bayerMode;

                    // 以 2x2 宏块为单位遍历，减少函数调用
                    int blockHeight = height - 1;
                    int blockWidth = width - 1;

                    for (int by = 0; by < blockHeight; by += 2)
                    {
                        for (int bx = 0; bx < blockWidth; bx += 2)
                        {
                            // 预计算邻居索引（复用原来的3x3窗口）
                            int x0 = bx > 0 ? bx - 1 : 0;
                            int x2 = bx + 1;
                            int x3 = bx + 2 < width ? bx + 2 : width - 1;
                            int y0 = by > 0 ? by - 1 : 0;
                            int y2 = by + 1;
                            int y3 = by + 2 < height ? by + 2 : height - 1;

                            // 读取 4x3 窗口内的所有像素
                            int p00 = pBayer[y0 * width + x0];
                            int p01 = pBayer[y0 * width + bx];
                            int p02 = pBayer[y0 * width + x2];
                            int p03 = pBayer[y0 * width + x3];

                            int p10 = pBayer[by * width + x0];
                            int p11 = pBayer[by * width + bx];
                            int p12 = pBayer[by * width + x2];
                            int p13 = pBayer[by * width + x3];

                            int p20 = pBayer[y2 * width + x0];
                            int p21 = pBayer[y2 * width + bx];
                            int p22 = pBayer[y2 * width + x2];
                            int p23 = pBayer[y2 * width + x3];

                            int p30 = pBayer[y3 * width + x0];
                            int p31 = pBayer[y3 * width + bx];
                            int p32 = pBayer[y3 * width + x2];
                            int p33 = pBayer[y3 * width + x3];

                            int r00, g00, b00, r01, g01, b01, r10, g10, b10, r11, g11, b11;

                            bool isEvenRow = (by & 1) == 0;

                            switch (bayerMode)
                            {
                                case DeviceConfig.Isp.BayerMode.RGRG:
                                    if (isEvenRow)
                                    {
                                        // (bx, by) - 偶行偶列 = R
                                        r00 = p11;
                                        g00 = FastInterpolateG(p10, p12, p01, p21);
                                        b00 = (p01 + p21) >> 1;
                                        // (bx+1, by) - 偶行奇列 = G
                                        g01 = p12;
                                        r01 = (p11 + p13) >> 1;
                                        b01 = (p02 + p22) >> 1;
                                        // (bx, by+1) - 奇行偶列 = G
                                        g10 = p21;
                                        r10 = (p11 + p31) >> 1;
                                        b10 = (p20 + p22) >> 1;
                                        // (bx+1, by+1) - 奇行奇列 = R
                                        r11 = p22;
                                        g11 = FastInterpolateG(p21, p23, p12, p32);
                                        b11 = (p12 + p32) >> 1;
                                    }
                                    else
                                    {
                                        r00 = p11;
                                        g00 = FastInterpolateG(p10, p12, p01, p21);
                                        b00 = (p01 + p21) >> 1;
                                        g01 = p12;
                                        r01 = (p11 + p13) >> 1;
                                        b01 = (p02 + p22) >> 1;
                                        g10 = p21;
                                        r10 = (p11 + p31) >> 1;
                                        b10 = (p20 + p22) >> 1;
                                        r11 = p22;
                                        g11 = FastInterpolateG(p21, p23, p12, p32);
                                        b11 = (p12 + p32) >> 1;
                                    }
                                    break;

                                case DeviceConfig.Isp.BayerMode.GRGR:
                                    if (isEvenRow)
                                    {
                                        // (bx, by) - 偶行偶列 = G
                                        g00 = p11;
                                        r00 = (p01 + p21) >> 1;
                                        b00 = (p00 + p02 + p20 + p22) >> 2;
                                        // (bx+1, by) - 偶行奇列 = R
                                        r01 = p12;
                                        g01 = FastInterpolateG(p11, p13, p02, p22);
                                        b01 = (p01 + p03 + p21 + p23) >> 2;
                                        // (bx, by+1) - 奇行偶列 = R
                                        r10 = p21;
                                        g10 = FastInterpolateG(p20, p22, p11, p31);
                                        b10 = (p20 + p22) >> 1;
                                        // (bx+1, by+1) - 奇行奇列 = G
                                        g11 = p22;
                                        r11 = (p12 + p32) >> 1;
                                        b11 = (p21 + p23) >> 1;
                                    }
                                    else
                                    {
                                        g00 = p11;
                                        r00 = (p01 + p21) >> 1;
                                        b00 = (p00 + p02 + p20 + p22) >> 2;
                                        r01 = p12;
                                        g01 = FastInterpolateG(p11, p13, p02, p22);
                                        b01 = (p01 + p03 + p21 + p23) >> 2;
                                        r10 = p21;
                                        g10 = FastInterpolateG(p20, p22, p11, p31);
                                        b10 = (p20 + p22) >> 1;
                                        g11 = p22;
                                        r11 = (p12 + p32) >> 1;
                                        b11 = (p21 + p23) >> 1;
                                    }
                                    break;

                                case DeviceConfig.Isp.BayerMode.BGBG:
                                    if (isEvenRow)
                                    {
                                        // (bx, by) - 偶行偶列 = B
                                        b00 = p11;
                                        g00 = FastInterpolateG(p10, p12, p01, p21);
                                        r00 = (p00 + p02 + p20 + p22) >> 2;  // 对角插值
                                        // (bx+1, by) - 偶行奇列 = G
                                        g01 = p12;
                                        b01 = (p11 + p13) >> 1;
                                        r01 = (p01 + p21) >> 1;  // 上下插值
                                        // (bx, by+1) - 奇行偶列 = G
                                        g10 = p21;
                                        b10 = (p11 + p31) >> 1;
                                        r10 = (p20 + p22) >> 1;  // 左右插值
                                        // (bx+1, by+1) - 奇行奇列 = B
                                        b11 = p22;
                                        g11 = FastInterpolateG(p21, p23, p12, p32);
                                        r11 = (p11 + p13 + p31 + p33) >> 2;  // 对角插值
                                    }
                                    else
                                    {
                                        b00 = p11;
                                        g00 = FastInterpolateG(p10, p12, p01, p21);
                                        r00 = (p00 + p02 + p20 + p22) >> 2;  // 对角插值
                                        g01 = p12;
                                        b01 = (p11 + p13) >> 1;
                                        r01 = (p01 + p21) >> 1;  // 上下插值
                                        g10 = p21;
                                        b10 = (p11 + p31) >> 1;
                                        r10 = (p20 + p22) >> 1;  // 左右插值
                                        b11 = p22;
                                        g11 = FastInterpolateG(p21, p23, p12, p32);
                                        r11 = (p11 + p13 + p31 + p33) >> 2;  // 对角插值
                                    }
                                    break;

                                case DeviceConfig.Isp.BayerMode.GBGB:
                                    if (isEvenRow)
                                    {
                                        // (bx, by) - 偶行偶列 = G
                                        g00 = p11;
                                        b00 = (p01 + p21) >> 1;
                                        r00 = (p00 + p02 + p20 + p22) >> 2;
                                        // (bx+1, by) - 偶行奇列 = B
                                        b01 = p12;
                                        g01 = FastInterpolateG(p11, p13, p02, p22);
                                        r01 = (p01 + p03 + p21 + p23) >> 2;
                                        // (bx, by+1) - 奇行偶列 = B
                                        b10 = p21;
                                        g10 = FastInterpolateG(p20, p22, p11, p31);
                                        r10 = (p20 + p22) >> 1;
                                        // (bx+1, by+1) - 奇行奇列 = G
                                        g11 = p22;
                                        b11 = (p12 + p32) >> 1;
                                        r11 = (p21 + p23) >> 1;
                                    }
                                    else
                                    {
                                        g00 = p11;
                                        b00 = (p01 + p21) >> 1;
                                        r00 = (p00 + p02 + p20 + p22) >> 2;
                                        b01 = p12;
                                        g01 = FastInterpolateG(p11, p13, p02, p22);
                                        r01 = (p01 + p03 + p21 + p23) >> 2;
                                        b10 = p21;
                                        g10 = FastInterpolateG(p20, p22, p11, p31);
                                        r10 = (p20 + p22) >> 1;
                                        g11 = p22;
                                        b11 = (p12 + p32) >> 1;
                                        r11 = (p21 + p23) >> 1;
                                    }
                                    break;

                                default:
                                    // 默认使用 RGRG
                                    r00 = p11;
                                    g00 = FastInterpolateG(p10, p12, p01, p21);
                                    b00 = (p01 + p21) >> 1;
                                    g01 = p12;
                                    r01 = (p11 + p13) >> 1;
                                    b01 = (p02 + p22) >> 1;
                                    g10 = p21;
                                    r10 = (p11 + p31) >> 1;
                                    b10 = (p20 + p22) >> 1;
                                    r11 = p22;
                                    g11 = FastInterpolateG(p21, p23, p12, p32);
                                    b11 = (p12 + p32) >> 1;
                                    break;
                            }

                            // 写入 RGB 数据
                            int row0 = by * rgbStride;
                            int row1 = (by + 1) * rgbStride;
                            int col0 = bx * 3;
                            int col1 = col0 + 3;

                            pRgb[row0 + col0] = (byte)r00;
                            pRgb[row0 + col0 + 1] = (byte)g00;
                            pRgb[row0 + col0 + 2] = (byte)b00;

                            pRgb[row0 + col1] = (byte)r01;
                            pRgb[row0 + col1 + 1] = (byte)g01;
                            pRgb[row0 + col1 + 2] = (byte)b01;

                            pRgb[row1 + col0] = (byte)r10;
                            pRgb[row1 + col0 + 1] = (byte)g10;
                            pRgb[row1 + col0 + 2] = (byte)b10;

                            pRgb[row1 + col1] = (byte)r11;
                            pRgb[row1 + col1 + 1] = (byte)g11;
                            pRgb[row1 + col1 + 2] = (byte)b11;
                        }
                    }

                    // 处理边界行（最后一行，如果高度为奇数）
                    if ((height & 1) != 0)
                    {
                        int y = height - 1;
                        int rowOffset = y * rgbStride;
                        for (int x = 0; x < width; x++)
                        {
                            int r, g, b;
                            GetRgbFromBayer(pBayer, width, height, x, y, out r, out g, out b, bayerMode);
                            int idx = rowOffset + x * 3;
                            pRgb[idx] = (byte)r;
                            pRgb[idx + 1] = (byte)g;
                            pRgb[idx + 2] = (byte)b;
                        }
                    }

                    // 处理边界列（最后一列，如果宽度为奇数）
                    if ((width & 1) != 0)
                    {
                        int x = width - 1;
                        for (int y = 0; y < height - 1; y += 2)
                        {
                            int r, g, b;
                            GetRgbFromBayer(pBayer, width, height, x, y, out r, out g, out b, bayerMode);
                            int idx = (y * rgbStride) + x * 3;
                            pRgb[idx] = (byte)r;
                            pRgb[idx + 1] = (byte)g;
                            pRgb[idx + 2] = (byte)b;

                            GetRgbFromBayer(pBayer, width, height, x, y + 1, out r, out g, out b, bayerMode);
                            idx = ((y + 1) * rgbStride) + x * 3;
                            pRgb[idx] = (byte)r;
                            pRgb[idx + 1] = (byte)g;
                            pRgb[idx + 2] = (byte)b;
                        }
                    }
                }

                return rgbData;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 将 RAW Bayer 数据转换为 RGB24 格式（高质量 Adams-Hamilton 去马赛克）
        /// 参考 eric612/BayerToRGB 的 Adams 插值方法，使用二阶梯度校正和色差域插值
        /// 相比 ConvertBayerToRgb 提供更优的边缘质量和更少的伪色
        /// </summary>
        /// <param name="bayerData">RAW Bayer 数据</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <returns>RGB24 格式数据 (每像素3字节: R,G,B)</returns>
        private unsafe byte[] NewHqConvertBayerToRgb(byte[] bayerData, int width, int height)
        {
            if (bayerData == null)
                throw new ArgumentNullException(nameof(bayerData));
            if (width <= 0 || height <= 0)
                throw new ArgumentException($"无效的图像尺寸: {width}x{height}");

            try
            {
                int pixelCount = width * height;
                byte[] rgbData = new byte[pixelCount * 3];

                // 处理 RAW10 转 8 位
                byte[] bayer8Data = _setMode == DeviceConfig.Isp.SetMode.RAW10
                    ? Convert10BitTo8Bit(bayerData, width, height)
                    : bayerData;

                fixed (byte* pBayer = bayer8Data)
                fixed (byte* pRgb = rgbData)
                {
                    int rgbStride = width * 3;
                    DeviceConfig.Isp.BayerMode bayerMode = _bayerMode;

                    // ── 第一遍：在每个像素位置插值完整的 G 通道 ──
                    // 对原始 Bayer 中 R/B 位置的像素，使用 Adams 二阶梯度校正法插值 G
                    // 对原始 Bayer 中 G 位置的像素，直接取原始值
                    byte[] gChannel = new byte[pixelCount];

                    fixed (byte* pG = gChannel)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            bool isEvenRow = (y & 1) == 0;
                            int rowBase = y * width;

                            for (int x = 0; x < width; x++)
                            {
                                bool isEvenCol = (x & 1) == 0;
                                bool isGreen = IsGreenPixel(bayerMode, isEvenRow, isEvenCol);

                                if (isGreen)
                                {
                                    // G 像素直接取原始值
                                    pG[rowBase + x] = pBayer[rowBase + x];
                                }
                                else
                                {
                                    // R 或 B 像素：使用 Adams 插值
                                    pG[rowBase + x] = (byte)ClampByte(
                                        AdamsInterpolateG(pBayer, width, height, x, y, bayerMode));
                                }
                            }
                        }

                        // ── 第二遍：用插值后的 G 重建完整 RGB ──
                        for (int y = 0; y < height; y++)
                        {
                            bool isEvenRow = (y & 1) == 0;
                            int rowBase = y * width;
                            int rgbRowBase = y * rgbStride;

                            for (int x = 0; x < width; x++)
                            {
                                bool isEvenCol = (x & 1) == 0;
                                int bayerIdx = rowBase + x;
                                int gVal = pG[bayerIdx];
                                int rVal, bVal;

                                BayerColor color = GetBayerColor(bayerMode, isEvenRow, isEvenCol);

                                switch (color)
                                {
                                    case BayerColor.Red:
                                        rVal = pBayer[bayerIdx];
                                        // B 在 G 位置对角，使用色差插值
                                        bVal = InterpolateBAtRed(pBayer, pG, width, height, x, y);
                                        break;

                                    case BayerColor.Green:
                                        // 在 G 像素上，R 和 B 都需要插值
                                        rVal = InterpolateRAtGreen(pBayer, pG, width, height, x, y, bayerMode);
                                        bVal = InterpolateBAtGreen(pBayer, pG, width, height, x, y, bayerMode);
                                        break;

                                    case BayerColor.Blue:
                                        bVal = pBayer[bayerIdx];
                                        rVal = InterpolateRAtBlue(pBayer, pG, width, height, x, y);
                                        break;

                                    default:
                                        rVal = gVal; bVal = gVal;
                                        break;
                                }

                                int rgbIdx = rgbRowBase + x * 3;
                                pRgb[rgbIdx] = (byte)ClampByte(rVal);
                                pRgb[rgbIdx + 1] = (byte)ClampByte(gVal);
                                pRgb[rgbIdx + 2] = (byte)ClampByte(bVal);
                            }
                        }
                    }
                }

                return rgbData;
            }
            catch
            {
                return null;
            }
        }

        // ── Bayer 颜色判断辅助 ──
        private enum BayerColor { Red, Green, Blue };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsGreenPixel(DeviceConfig.Isp.BayerMode mode, bool isEvenRow, bool isEvenCol)
        {
            switch (mode)
            {
                case DeviceConfig.Isp.BayerMode.RGRG:
                    return isEvenRow != isEvenCol; // R G / G R — G 在奇行偶列和偶行奇列
                case DeviceConfig.Isp.BayerMode.GRGR:
                    return isEvenRow == isEvenCol; // G R / R G — G 在偶行偶列和奇行奇列
                case DeviceConfig.Isp.BayerMode.BGBG:
                    return isEvenRow != isEvenCol; // B G / G B — G 在奇行偶列和偶行奇列
                case DeviceConfig.Isp.BayerMode.GBGB:
                    return isEvenRow == isEvenCol; // G B / B G — G 在偶行偶列和奇行奇列
                default:
                    return isEvenRow != isEvenCol;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BayerColor GetBayerColor(DeviceConfig.Isp.BayerMode mode, bool isEvenRow, bool isEvenCol)
        {
            switch (mode)
            {
                case DeviceConfig.Isp.BayerMode.RGRG:
                    // R G / G R
                    if (isEvenRow) return isEvenCol ? BayerColor.Red : BayerColor.Green;
                    else           return isEvenCol ? BayerColor.Green : BayerColor.Red;
                case DeviceConfig.Isp.BayerMode.GRGR:
                    // G R / R G
                    if (isEvenRow) return isEvenCol ? BayerColor.Green : BayerColor.Red;
                    else           return isEvenCol ? BayerColor.Red : BayerColor.Green;
                case DeviceConfig.Isp.BayerMode.BGBG:
                    // B G / G B
                    if (isEvenRow) return isEvenCol ? BayerColor.Blue : BayerColor.Green;
                    else           return isEvenCol ? BayerColor.Green : BayerColor.Blue;
                case DeviceConfig.Isp.BayerMode.GBGB:
                    // G B / B G
                    if (isEvenRow) return isEvenCol ? BayerColor.Green : BayerColor.Blue;
                    else           return isEvenCol ? BayerColor.Blue : BayerColor.Green;
                default:
                    if (isEvenRow) return isEvenCol ? BayerColor.Red : BayerColor.Green;
                    else           return isEvenCol ? BayerColor.Green : BayerColor.Red;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ClampByte(int val)
        {
            return (val < 0) ? 0 : (val > 255) ? 255 : val;
        }

        /// <summary>
        /// Adams-Hamilton 风格 G 通道插值
        /// 在 R/B 像素位置使用二阶梯度校正来插值 G
        /// 理论基础：色差 (G-R 或 G-B) 在局部区域平滑变化
        /// 因此 G = G_avg + Laplacian(R) / 2，其中 Laplacian(R) 是 R 的二阶梯度
        /// </summary>
        private static unsafe int AdamsInterpolateG(byte* pBayer, int width, int height, int x, int y, DeviceConfig.Isp.BayerMode mode)
        {
            // 边界钳位
            int xm2 = Math.Max(x - 2, 0);
            int xm1 = Math.Max(x - 1, 0);
            int xp1 = Math.Min(x + 1, width - 1);
            int xp2 = Math.Min(x + 2, width - 1);
            int ym2 = Math.Max(y - 2, 0);
            int ym1 = Math.Max(y - 1, 0);
            int yp1 = Math.Min(y + 1, height - 1);
            int yp2 = Math.Min(y + 2, height - 1);

            int center = pBayer[y * width + x];

            // 计算垂直和水平方向的二阶梯度（Laplacian）
            int laplacianV = center * 2 - pBayer[yp2 * width + x] - pBayer[ym2 * width + x];
            int laplacianH = center * 2 - pBayer[y * width + xp2] - pBayer[y * width + xm2];

            // G 邻居值
            int gTop = pBayer[ym1 * width + x];
            int gBottom = pBayer[yp1 * width + x];
            int gLeft = pBayer[y * width + xm1];
            int gRight = pBayer[y * width + xp1];

            const float maxCut = 30.0f;  // 二阶梯度截断阈值，防止过冲

            // 根据梯度方向选择插值方向：梯度小的方向更可能是边缘方向
            if (Math.Abs(laplacianH) < Math.Abs(laplacianV))
            {
                // 水平方向梯度更小 → 沿边缘方向（水平）插值
                // G = (G_left + G_right) / 2 + Laplacian_H * 0.25
                float highPass = Math.Max(-maxCut, Math.Min(maxCut, laplacianH));
                float gInterp = (gLeft + gRight) * 0.5f + highPass * 0.25f;
                return (int)(gInterp + 0.5f);
            }
            else
            {
                // 垂直方向梯度更小 → 沿边缘方向（垂直）插值
                float highPass = Math.Max(-maxCut, Math.Min(maxCut, laplacianV));
                float gInterp = (gTop + gBottom) * 0.5f + highPass * 0.25f;
                return (int)(gInterp + 0.5f);
            }
        }

        /// <summary>
        /// 在 R 像素位置插值 B 分量
        /// 利用色差平滑性：B-G 在局部区域变化平缓
        /// 先计算周围 B 位置的 B-G 差值，加权平均后加上当前 G 值
        /// </summary>
        private static unsafe int InterpolateBAtRed(byte* pBayer, byte* pG, int width, int height, int x, int y)
        {
            // 在 RGRG 模式中，B 在 (x-1,y-1), (x+1,y-1), (x-1,y+1), (x+1,y+1) 四个对角位置
            int xm1 = Math.Max(x - 1, 0);
            int xp1 = Math.Min(x + 1, width - 1);
            int ym1 = Math.Max(y - 1, 0);
            int yp1 = Math.Min(y + 1, height - 1);

            // 收集四个对角 B 位置的色差 (B - G)
            int b00 = pBayer[ym1 * width + xm1];
            int b01 = pBayer[ym1 * width + xp1];
            int b10 = pBayer[yp1 * width + xm1];
            int b11 = pBayer[yp1 * width + xp1];
            int g00 = pG[ym1 * width + xm1];
            int g01 = pG[ym1 * width + xp1];
            int g10 = pG[yp1 * width + xm1];
            int g11 = pG[yp1 * width + xp1];

            // 梯度加权：色差梯度大的方向权重小
            int diffH = Math.Abs((b01 - g01) - (b00 - g00)) + Math.Abs((b11 - g11) - (b10 - g10));
            int diffV = Math.Abs((b10 - g10) - (b00 - g00)) + Math.Abs((b11 - g11) - (b01 - g01));

            // 计算加权色差
            int wH = diffV + 1; // 水平梯度小的方向权重高
            int wV = diffH + 1;

            int colorDiff = ((b00 - g00) * wH + (b01 - g01) * wV + (b10 - g10) * wV + (b11 - g11) * wH)
                          / (2 * (wH + wV));

            return pG[y * width + x] + colorDiff;
        }

        /// <summary>
        /// 在 B 像素位置插值 R 分量
        /// 同 InterpolateBAtRed 对称处理
        /// </summary>
        private static unsafe int InterpolateRAtBlue(byte* pBayer, byte* pG, int width, int height, int x, int y)
        {
            int xm1 = Math.Max(x - 1, 0);
            int xp1 = Math.Min(x + 1, width - 1);
            int ym1 = Math.Max(y - 1, 0);
            int yp1 = Math.Min(y + 1, height - 1);

            // 四个对角位置的 R 色差 (R - G)
            int r00 = pBayer[ym1 * width + xm1];
            int r01 = pBayer[ym1 * width + xp1];
            int r10 = pBayer[yp1 * width + xm1];
            int r11 = pBayer[yp1 * width + xp1];
            int g00 = pG[ym1 * width + xm1];
            int g01 = pG[ym1 * width + xp1];
            int g10 = pG[yp1 * width + xm1];
            int g11 = pG[yp1 * width + xp1];

            int diffH = Math.Abs((r01 - g01) - (r00 - g00)) + Math.Abs((r11 - g11) - (r10 - g10));
            int diffV = Math.Abs((r10 - g10) - (r00 - g00)) + Math.Abs((r11 - g11) - (r01 - g01));

            int wH = diffV + 1;
            int wV = diffH + 1;

            int colorDiff = ((r00 - g00) * wH + (r01 - g01) * wV + (r10 - g10) * wV + (r11 - g11) * wH)
                          / (2 * (wH + wV));

            return pG[y * width + x] + colorDiff;
        }

        /// <summary>
        /// 在 G 像素位置插值 R 分量
        /// 根据 Bayer 模式，R 在上下或左右位置
        /// 使用色差梯度加权的方向性插值
        /// </summary>
        private static unsafe int InterpolateRAtGreen(byte* pBayer, byte* pG, int width, int height, int x, int y, DeviceConfig.Isp.BayerMode mode)
        {
            int xm1 = Math.Max(x - 1, 0);
            int xp1 = Math.Min(x + 1, width - 1);
            int ym1 = Math.Max(y - 1, 0);
            int yp1 = Math.Min(y + 1, height - 1);

            bool isEvenRow = (y & 1) == 0;
            bool isEvenCol = (x & 1) == 0;

            int rNeighbor1, rNeighbor2, gNeighbor1, gNeighbor2;

            // RGRG: G at (even,odd) → R at left/right; G at (odd,even) → R at top/bottom
            // GRGR: G at (even,even) → R at left/right; G at (odd,odd) → R at top/bottom
            // BGBG: G at (even,odd) → R at top/bottom; G at (odd,even) → R at left/right
            // GBGB: G at (even,even) → R at top/bottom; G at (odd,odd) → R at left/right
            bool rIsHorizontal;
            switch (mode)
            {
                case DeviceConfig.Isp.BayerMode.RGRG:
                    rIsHorizontal = isEvenRow; // 偶行 G 在奇列，R 在左右
                    break;
                case DeviceConfig.Isp.BayerMode.GRGR:
                    rIsHorizontal = !isEvenRow; // 奇行 G 在奇列，R 在左右
                    break;
                case DeviceConfig.Isp.BayerMode.BGBG:
                    rIsHorizontal = !isEvenRow; // 奇行 G 在奇列，R 在上下
                    break;
                case DeviceConfig.Isp.BayerMode.GBGB:
                    rIsHorizontal = isEvenRow; // 偶行 G 在偶列，R 在上下  
                    break;
                default:
                    rIsHorizontal = isEvenRow;
                    break;
            }

            if (rIsHorizontal)
            {
                rNeighbor1 = pBayer[y * width + xm1];
                rNeighbor2 = pBayer[y * width + xp1];
                gNeighbor1 = pG[y * width + xm1];
                gNeighbor2 = pG[y * width + xp1];
            }
            else
            {
                rNeighbor1 = pBayer[ym1 * width + x];
                rNeighbor2 = pBayer[yp1 * width + x];
                gNeighbor1 = pG[ym1 * width + x];
                gNeighbor2 = pG[yp1 * width + x];
            }

            // 色差加权插值
            int diff1 = rNeighbor1 - gNeighbor1;
            int diff2 = rNeighbor2 - gNeighbor2;
            int weight = Math.Abs(diff1 - diff2) + 1; // 色差变化大时降低权重
            int colorDiff = (diff1 * (256 - weight) + diff2 * weight) / 256;

            return pG[y * width + x] + colorDiff;
        }

        /// <summary>
        /// 在 G 像素位置插值 B 分量
        /// 同 InterpolateRAtGreen 对称处理
        /// </summary>
        private static unsafe int InterpolateBAtGreen(byte* pBayer, byte* pG, int width, int height, int x, int y, DeviceConfig.Isp.BayerMode mode)
        {
            int xm1 = Math.Max(x - 1, 0);
            int xp1 = Math.Min(x + 1, width - 1);
            int ym1 = Math.Max(y - 1, 0);
            int yp1 = Math.Min(y + 1, height - 1);

            bool isEvenRow = (y & 1) == 0;
            bool isEvenCol = (x & 1) == 0;

            int bNeighbor1, bNeighbor2, gNeighbor1, gNeighbor2;

            // RGRG: G at (even,odd) → B at top/bottom; G at (odd,even) → B at left/right
            // GRGR: G at (even,even) → B at diagonal; G at (odd,odd) → B at diagonal
            // BGBG: G at (even,odd) → B at left/right; G at (odd,even) → B at top/bottom
            // GBGB: G at (even,even) → B at left/right; G at (odd,odd) → B at top/bottom
            bool bIsHorizontal;
            switch (mode)
            {
                case DeviceConfig.Isp.BayerMode.RGRG:
                    bIsHorizontal = !isEvenRow; // 奇行 G 在偶列，B 在左右
                    break;
                case DeviceConfig.Isp.BayerMode.GRGR:
                    bIsHorizontal = isEvenRow; // 偶行 G 在偶列，B 在上下
                    break;
                case DeviceConfig.Isp.BayerMode.BGBG:
                    bIsHorizontal = isEvenRow; // 偶行 G 在奇列，B 在左右
                    break;
                case DeviceConfig.Isp.BayerMode.GBGB:
                    bIsHorizontal = !isEvenRow; // 奇行 G 在奇列，B 在上下
                    break;
                default:
                    bIsHorizontal = !isEvenRow;
                    break;
            }

            if (bIsHorizontal)
            {
                bNeighbor1 = pBayer[y * width + xm1];
                bNeighbor2 = pBayer[y * width + xp1];
                gNeighbor1 = pG[y * width + xm1];
                gNeighbor2 = pG[y * width + xp1];
            }
            else
            {
                bNeighbor1 = pBayer[ym1 * width + x];
                bNeighbor2 = pBayer[yp1 * width + x];
                gNeighbor1 = pG[ym1 * width + x];
                gNeighbor2 = pG[yp1 * width + x];
            }

            int diff1 = bNeighbor1 - gNeighbor1;
            int diff2 = bNeighbor2 - gNeighbor2;
            int weight = Math.Abs(diff1 - diff2) + 1;
            int colorDiff = (diff1 * (256 - weight) + diff2 * weight) / 256;

            return pG[y * width + x] + colorDiff;
        }

        // ══════════════════════════════════════════════════════════════════
        //  NewConvertBayerToRgb — 参考 eric612/BayerToRGB 项目的
        //  修正 Adams-Hamilton 去马赛克算法（色差域 + 统计方向加权 + 中值滤波）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 将 RAW Bayer 数据转换为 RGB24 格式（修正 Adams-Hamilton 去马赛克）
        /// 参考 https://github.com/eric612/BayerToRGB 项目的 Interpolation_G_Only 算法
        /// 核心：在 R/B 像素位置使用 9 元素色差数组 + 标准差立方加权 + 中值滤波插值 G，
        /// 然后在色差域 (R-G)/(B-G) 中进行双线性插值重建完整 RGB。
        /// 相比 ConvertBayerToRgb 提供更优的边缘方向感知和更少的伪色/拉链效应。
        /// </summary>
        /// <param name="bayerData">RAW Bayer 数据</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <returns>RGB24 格式数据 (每像素3字节: R,G,B)</returns>
        private unsafe byte[] NewConvertBayerToRgb(byte[] bayerData, int width, int height)
        {
            if (bayerData == null)
                throw new ArgumentNullException(nameof(bayerData));
            if (width <= 0 || height <= 0)
                throw new ArgumentException($"无效的图像尺寸: {width}x{height}");

            try
            {
                int pixelCount = width * height;
                byte[] rgbData = new byte[pixelCount * 3];

                // 处理 RAW10 转 8 位
                byte[] bayer8Data = _setMode == DeviceConfig.Isp.SetMode.RAW10
                    ? Convert10BitTo8Bit(bayerData, width, height)
                    : bayerData;

                fixed (byte* pBayer = bayer8Data)
                fixed (byte* pRgb = rgbData)
                {
                    int rgbStride = width * 3;
                    DeviceConfig.Isp.BayerMode bayerMode = _bayerMode;

                    // ── 第一遍：在每个像素位置插值完整的 G 通道 ──
                    // G 像素直接取原始值；R/B 像素使用 Interpolation_G_Only 插值 G
                    // Interpolation_G_Only 访问 ±4 像素范围，边界 4 行/列使用简单双线性
                    byte[] gChannel = new byte[pixelCount];
                    // 色差通道：(R-G)/2+127 和 (B-G)/2+127，存储在 R/B 像素所在位置
                    // 全分辨率存储，方便后续插值
                    byte[] diffRG = new byte[pixelCount];  // R-G 色差（编码后）
                    byte[] diffBG = new byte[pixelCount];  // B-G 色差（编码后）
                    byte[] hasRG = new byte[pixelCount];   // 标记该位置是否有 R-G 直接采样 (0/1)
                    byte[] hasBG = new byte[pixelCount];   // 标记该位置是否有 B-G 直接采样 (0/1)

                    fixed (byte* pG = gChannel)
                    fixed (byte* pDiffRG = diffRG)
                    fixed (byte* pDiffBG = diffBG)
                    fixed (byte* pHasRG = hasRG)
                    fixed (byte* pHasBG = hasBG)
                    {
                        // ── Pass 1: 插值 G 通道 + 计算色差 ──
                        for (int y = 0; y < height; y++)
                        {
                            bool isEvenRow = (y & 1) == 0;
                            int rowBase = y * width;

                            for (int x = 0; x < width; x++)
                            {
                                bool isEvenCol = (x & 1) == 0;
                                int idx = rowBase + x;
                                BayerColor color = GetBayerColor(bayerMode, isEvenRow, isEvenCol);

                                if (color == BayerColor.Green)
                                {
                                    // G 像素直接取原始值
                                    pG[idx] = pBayer[idx];
                                }
                                else
                                {
                                    // R 或 B 像素：使用 Interpolation_G_Only 插值 G
                                    // 边界安全：需要访问 ±4 像素
                                    int gVal;
                                    if (x >= 4 && x < width - 4 && y >= 4 && y < height - 4)
                                    {
                                        gVal = BayerAdamsInterpolationG(pBayer, width, x, y);
                                    }
                                    else
                                    {
                                        // 边界区域使用简单 Adams 插值
                                        gVal = AdamsInterpolateG(pBayer, width, height, x, y, bayerMode);
                                    }
                                    gVal = ClampByte(gVal);
                                    pG[idx] = (byte)gVal;

                                    // 计算并存储色差
                                    int centerVal = pBayer[idx];
                                    if (color == BayerColor.Red)
                                    {
                                        int diffEncoded = ClampByte((centerVal - gVal + 255) / 2);  // (R-G)/2 + 127.5 ≈ (R-G+255)/2
                                        pDiffRG[idx] = (byte)diffEncoded;
                                        pHasRG[idx] = 1;
                                    }
                                    else // BayerColor.Blue
                                    {
                                        int diffEncoded = ClampByte((centerVal - gVal + 255) / 2);  // (B-G)/2 + 127.5
                                        pDiffBG[idx] = (byte)diffEncoded;
                                        pHasBG[idx] = 1;
                                    }
                                }
                            }
                        }

                        // ── Pass 2: 在 G 像素位置插值色差，重建完整 RGB ──
                        for (int y = 0; y < height; y++)
                        {
                            bool isEvenRow = (y & 1) == 0;
                            int rowBase = y * width;
                            int rgbRowBase = y * rgbStride;

                            for (int x = 0; x < width; x++)
                            {
                                bool isEvenCol = (x & 1) == 0;
                                int idx = rowBase + x;
                                int gVal = pG[idx];
                                BayerColor color = GetBayerColor(bayerMode, isEvenRow, isEvenCol);
                                int rVal, bVal;

                                switch (color)
                                {
                                    case BayerColor.Red:
                                        {
                                            rVal = pBayer[idx];
                                            // B-G 色差需要在 R 位置插值
                                            bVal = gVal + BayerInterpolateDiff(pDiffBG, pHasBG, width, height, x, y);
                                            break;
                                        }
                                    case BayerColor.Green:
                                        {
                                            // R-G 和 B-G 色差都需要在 G 位置插值
                                            int rDiff = BayerInterpolateDiff(pDiffRG, pHasRG, width, height, x, y);
                                            int bDiff = BayerInterpolateDiff(pDiffBG, pHasBG, width, height, x, y);
                                            rVal = gVal + rDiff;
                                            bVal = gVal + bDiff;
                                            break;
                                        }
                                    case BayerColor.Blue:
                                        {
                                            bVal = pBayer[idx];
                                            // R-G 色差需要在 B 位置插值
                                            rVal = gVal + BayerInterpolateDiff(pDiffRG, pHasRG, width, height, x, y);
                                            break;
                                        }
                                    default:
                                        rVal = gVal;
                                        bVal = gVal;
                                        break;
                                }

                                int rgbIdx = rgbRowBase + x * 3;
                                pRgb[rgbIdx] = (byte)ClampByte(rVal);
                                pRgb[rgbIdx + 1] = (byte)ClampByte(gVal);
                                pRgb[rgbIdx + 2] = (byte)ClampByte(bVal);
                            }
                        }
                    }
                }

                return rgbData;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Adams 方向性插值（指定方向）
        /// 参考 BayerToRGB 项目的 AdamsInterpolation(in, x, y, width, direction, max_cut)
        /// direction=0: 垂直方向；direction=1: 水平方向
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe float BayerAdamsDirectional(byte* pBayer, int width, int x, int y, int direction, float maxCut)
        {
            if (direction == 0)
            {
                // 垂直方向：G = (G_top + G_bottom) / 2 + BOUND(center*2 - top2 - bottom2, -maxCut, maxCut) / 4
                int center = pBayer[y * width + x];
                int gTop = pBayer[(y - 1) * width + x];
                int gBottom = pBayer[(y + 1) * width + x];
                int laplacianV = center * 2 - pBayer[(y + 2) * width + x] - pBayer[(y - 2) * width + x];
                float highPass = laplacianV < -maxCut ? -maxCut : (laplacianV > maxCut ? maxCut : laplacianV);
                return (gTop + gBottom) * 0.5f + highPass * 0.25f;
            }
            else
            {
                // 水平方向：G = (G_left + G_right) / 2 + BOUND(center*2 - left2 - right2, -maxCut, maxCut) / 4
                int center = pBayer[y * width + x];
                int gLeft = pBayer[y * width + x - 1];
                int gRight = pBayer[y * width + x + 1];
                int laplacianH = center * 2 - pBayer[y * width + x + 2] - pBayer[y * width + x - 2];
                float highPass = laplacianH < -maxCut ? -maxCut : (laplacianH > maxCut ? maxCut : laplacianH);
                return (gLeft + gRight) * 0.5f + highPass * 0.25f;
            }
        }

        /// <summary>
        /// BayerToRGB 项目的核心 G 通道插值算法 Interpolation_G_Only
        /// 在 R/B 像素位置使用修正 Adams-Hamilton 方法插值 G：
        /// 1. 在水平和垂直方向分别构建 9 元素色差数组 (G-R)
        /// 2. 计算两个方向的色差标准差，用立方加权确定方向权重
        /// 3. 用中值滤波增强鲁棒性，加权混合两个方向的 G 估计
        ///
        /// 注意：调用者需确保 x ∈ [4, width-4) 且 y ∈ [4, height-4)
        /// </summary>
        private static unsafe int BayerAdamsInterpolationG(byte* pBayer, int width, int x, int y)
        {
            const float maxCut = 64.0f;     // Interpolation_G_Only 使用更大的 maxCut
            const int kernelSize = 5;        // 标准差计算使用的元素数

            // ── 计算各位置的 Adams 水平/垂直方向 G 估计 ──
            // 中心位置
            float g0h = BayerAdamsDirectional(pBayer, width, x, y, 1, maxCut);     // 水平 Adams at (x,y)
            float g0v = BayerAdamsDirectional(pBayer, width, x, y, 0, maxCut);     // 垂直 Adams at (x,y)

            // 水平 Adams 在 ±2 像素偏移位置
            float g1h = BayerAdamsDirectional(pBayer, width, x + 2, y, 1, maxCut); // (x+2, y)
            float g2h = BayerAdamsDirectional(pBayer, width, x - 2, y, 1, maxCut); // (x-2, y)

            // 垂直 Adams 在 ±2 像素偏移位置
            float g1v = BayerAdamsDirectional(pBayer, width, x, y + 2, 0, maxCut); // (x, y+2)
            float g2v = BayerAdamsDirectional(pBayer, width, x, y - 2, 0, maxCut); // (x, y-2)

            // 对角位置的 Adams 水平估计
            float g3h = BayerAdamsDirectional(pBayer, width, x - 1, y - 1, 1, maxCut);
            float g4h = BayerAdamsDirectional(pBayer, width, x - 1, y + 1, 1, maxCut);
            float g5h = BayerAdamsDirectional(pBayer, width, x + 1, y - 1, 1, maxCut);
            float g6h = BayerAdamsDirectional(pBayer, width, x + 1, y + 1, 1, maxCut);

            // 对角位置的 Adams 垂直估计
            float g3v = BayerAdamsDirectional(pBayer, width, x - 1, y - 1, 0, maxCut);
            float g4v = BayerAdamsDirectional(pBayer, width, x - 1, y + 1, 0, maxCut);
            float g5v = BayerAdamsDirectional(pBayer, width, x + 1, y - 1, 0, maxCut);
            float g6v = BayerAdamsDirectional(pBayer, width, x + 1, y + 1, 0, maxCut);

            // 邻居 R/B 位置的 Adams 估计（用于计算邻居处的 G-R 色差）
            float r0h = BayerAdamsDirectional(pBayer, width, x + 1, y, 1, maxCut);  // 水平 Adams at (x+1, y)
            float r1h = BayerAdamsDirectional(pBayer, width, x - 1, y, 1, maxCut);  // 水平 Adams at (x-1, y)
            float r0v = BayerAdamsDirectional(pBayer, width, x, y + 1, 0, maxCut);  // 垂直 Adams at (x, y+1)
            float r1v = BayerAdamsDirectional(pBayer, width, x, y - 1, 0, maxCut);  // 垂直 Adams at (x, y-1)

            // 中心像素的实际值（R 或 B）
            int centerVal = pBayer[y * width + x];

            // ── 构建水平方向 9 元素 G-R 色差数组 grh ──
            float[] grh = new float[9];
            grh[0] = g0h - centerVal;                           // 中心水平 Adams-G 减去实际值
            grh[1] = pBayer[y * width + x + 1] - r0h;          // 右邻 G 减去右邻水平 Adams-G
            grh[2] = pBayer[y * width + x - 1] - r1h;          // 左邻 G 减去左邻水平 Adams-G
            grh[3] = g1h - pBayer[y * width + x + 2];          // x+2 位置 Adams-G 减去实际值
            grh[4] = g2h - pBayer[y * width + x - 2];          // x-2 位置 Adams-G 减去实际值
            grh[5] = g3h;                                        // 对角 (x-1,y-1) 水平 Adams-G
            grh[6] = g4h;                                        // 对角 (x-1,y+1) 水平 Adams-G
            grh[7] = g5h;                                        // 对角 (x+1,y-1) 水平 Adams-G
            grh[8] = g6h;                                        // 对角 (x+1,y+1) 水平 Adams-G

            // ── 构建垂直方向 9 元素 G-R 色差数组 grv ──
            float[] grv = new float[9];
            grv[0] = g0v - centerVal;                           // 中心垂直 Adams-G 减去实际值
            grv[1] = pBayer[(y + 1) * width + x] - r0v;        // 下邻 G 减去下邻垂直 Adams-G
            grv[2] = pBayer[(y - 1) * width + x] - r1v;        // 上邻 G 减去上邻垂直 Adams-G
            grv[3] = g1v - pBayer[(y + 2) * width + x];        // y+2 位置 Adams-G 减去实际值
            grv[4] = g2v - pBayer[(y - 2) * width + x];        // y-2 位置 Adams-G 减去实际值
            grv[5] = g3v;                                        // 对角 (x-1,y-1) 垂直 Adams-G
            grv[6] = g4v;                                        // 对角 (x-1,y+1) 垂直 Adams-G
            grv[7] = g5v;                                        // 对角 (x+1,y-1) 垂直 Adams-G
            grv[8] = g6v;                                        // 对角 (x+1,y+1) 垂直 Adams-G

            // ── 计算两个方向的色差标准差 ──
            float grStdH = BayerGetStd(grh, kernelSize);
            float grStdV = BayerGetStd(grv, kernelSize);

            // ── 立方加权：标准差越小（色差越平滑）的方向权重越高 ──
            float alpha1 = 0.5f;  // 垂直方向权重
            float alpha2 = 0.5f;  // 水平方向权重
            if (grStdH + grStdV != 0)
            {
                float stdCubedH = grStdH * grStdH * grStdH;  // pow(grStdH, 3)
                float stdCubedV = grStdV * grStdV * grStdV;  // pow(grStdV, 3)
                if (stdCubedH + stdCubedV > 0)
                {
                    alpha1 = stdCubedH / (stdCubedH + stdCubedV);  // 水平标准差大 → 更信任垂直
                    alpha2 = stdCubedV / (stdCubedH + stdCubedV);  // 垂直标准差大 → 更信任水平
                }
            }

            // ── 使用中值滤波增强鲁棒性 ──
            // gEstH = 实际值 + median(grh[0:3])  → 水平方向的 G 估计
            // gEstV = 实际值 + median(grv[0:3])  → 垂直方向的 G 估计
            float gEstH = centerVal + BayerGetMedian(grh, 3);
            float gEstV = centerVal + BayerGetMedian(grv, 3);

            // 钳位到 [0, 255]
            gEstH = gEstH < 0 ? 0 : (gEstH > 255 ? 255 : gEstH);
            gEstV = gEstV < 0 ? 0 : (gEstV > 255 ? 255 : gEstV);

            // 加权混合
            float gResult = gEstH * alpha2 + gEstV * alpha1;
            if (gResult > 255) gResult = 255;
            else if (gResult < 0) gResult = 0;

            return (int)(gResult + 0.5f);
        }

        /// <summary>
        /// 计算数组前 num 个元素的均值
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float BayerGetMean(float[] arr, int num)
        {
            float sum = 0;
            for (int i = 0; i < num; i++)
                sum += arr[i];
            return sum / num;
        }

        /// <summary>
        /// 计算数组前 num 个元素的标准差
        /// 参考 BayerToRGB 项目 util.cpp 中的 GetStd
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float BayerGetStd(float[] arr, int num)
        {
            float mean = BayerGetMean(arr, num);
            float sum = 0;
            for (int i = 0; i < num; i++)
            {
                float diff = arr[i] - mean;
                sum += diff * diff;
            }
            return (float)Math.Sqrt(sum) / num;
        }

        /// <summary>
        /// 计算数组前 num 个元素的中值（冒泡排序取中值）
        /// 参考 BayerToRGB 项目 util.cpp 中的 GetMedian
        /// 注意：此方法会修改输入数组（原地排序）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float BayerGetMedian(float[] arr, int num)
        {
            // 冒泡排序前 num 个元素
            for (int i = 0; i < num - 1; i++)
            {
                for (int j = i + 1; j < num; j++)
                {
                    if (arr[j] < arr[i])
                    {
                        float temp = arr[i];
                        arr[i] = arr[j];
                        arr[j] = temp;
                    }
                }
            }
            if (num % 2 == 0)
                return (arr[num / 2] + arr[num / 2 - 1]) * 0.5f;
            else
                return arr[num / 2];
        }

        /// <summary>
        /// 在色差通道中插值缺失位置的色差值
        /// 对于 G 像素位置：R-G 和 B-G 都需要插值
        /// 对于 R 像素位置：B-G 需要插值
        /// 对于 B 像素位置：R-G 需要插值
        ///
        /// 使用色差域双线性插值：从相邻 4 个有直接采样的位置取加权平均
        /// 插值公式：decoded_diff = encoded - 127，然后 R = G + decoded_diff * 2
        /// </summary>
        private static unsafe int BayerInterpolateDiff(byte* pDiff, byte* hasSample, int width, int height, int x, int y)
        {
            // 在当前位置的 3x3 邻域内收集有直接采样的色差值
            // R/B 像素在 Bayer 阵列中每隔 2 像素出现一次，
            // 对于 G 位置，最近的 R/B 在上下左右 ±1 位置
            // 对于 R/B 位置，同色的最近邻在对角 ±1 位置

            int y0 = y > 0 ? y - 1 : 0;
            int y1 = y < height - 1 ? y + 1 : height - 1;
            int x0 = x > 0 ? x - 1 : 0;
            int x1 = x < width - 1 ? x + 1 : width - 1;

            // 收集邻域内的色差采样（解码为实际色差值 * 2）
            int sumDiff = 0;
            int totalWeight = 0;

            // 4 个直接邻域（上下左右）
            int[] offsets = {
                y0 * width + x,   // 上
                y1 * width + x,   // 下
                y * width + x0,   // 左
                y * width + x1    // 右
            };
            int[] weights = { 4, 4, 4, 4 };  // 直接邻居权重更高

            for (int i = 0; i < 4; i++)
            {
                if (hasSample[offsets[i]] != 0)
                {
                    int decodedDiff = (pDiff[offsets[i]] - 127) * 2;
                    sumDiff += decodedDiff * weights[i];
                    totalWeight += weights[i];
                }
            }

            // 4 个对角邻域
            int[] diagOffsets = {
                y0 * width + x0,  // 左上
                y0 * width + x1,  // 右上
                y1 * width + x0,  // 左下
                y1 * width + x1   // 右下
            };

            for (int i = 0; i < 4; i++)
            {
                if (hasSample[diagOffsets[i]] != 0)
                {
                    int decodedDiff = (pDiff[diagOffsets[i]] - 127) * 2;
                    sumDiff += decodedDiff * 2;  // 对角邻居权重较低
                    totalWeight += 2;
                }
            }

            if (totalWeight > 0)
                return sumDiff / totalWeight;
            else
                return 0;  // 无采样可用时返回 0（G=R=G）
        }

        /// <summary>
        /// 将YUV420P数据转换为JPG格式
        /// </summary>
        /// <param name="yuvData">YUV420P数据</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <returns>JPEG格式字节数组</returns>
        private byte[] ConvertYuvToJpeg(byte[] yuvData, int width, int height)
        {
            try
            {
                // YUV420P格式转换为Bitmap
                using (var bitmap = ConvertYuv420pToBitmap(yuvData, width, height))
                {
                    using (var ms = new MemoryStream())
                    {
                        // 以JPEG格式保存到内存流
                        bitmap.Save(ms, ImageFormat.Jpeg);
                        return ms.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to convert YUV to JPEG: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 将YUV420P格式数据转换为Bitmap
        /// </summary>
        /// <param name="yuvData">YUV420P数据</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <returns>Bitmap对象</returns>
        private unsafe Bitmap ConvertYuv420pToBitmap(byte[] yuvData, int width, int height)
        {
            // YUV420P 总大小 = W*H + (W*H)/4 + (W*H)/4 = W*H*1.5
            int ySize = width * height;
            int uOffset = ySize;
            int vOffset = ySize + ySize / 4;

            // 创建RGB数组
            byte[] rgbData = new byte[width * height * 3];

            // 执行YUV到RGB的转换
            fixed (byte* pYuv = yuvData)
            {
                byte* pY = pYuv;      // Y平面
                byte* pU = pYuv + uOffset;  // U平面
                byte* pV = pYuv + vOffset;  // V平面

                fixed (byte* pRgbFixed = rgbData)
                {
                    byte* pRgb = pRgbFixed;

                    // 对每个像素进行YUV到RGB转换
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int yIdx = y * width + x;
                            int uIdx = (y / 2) * (width / 2) + (x / 2);
                            int vIdx = (y / 2) * (width / 2) + (x / 2);

                            // 获取YUV值
                            int Y = pY[yIdx];
                            int U = pU[uIdx] - 128;
                            int V = pV[vIdx] - 128;

                            // YUV转RGB公式 (ITU-R BT.601标准)
                            int R = Math.Max(0, Math.Min(255, (int)(Y + 1.402 * V)));
                            int G = Math.Max(0, Math.Min(255, (int)(Y - 0.344 * U - 0.714 * V)));
                            int B = Math.Max(0, Math.Min(255, (int)(Y + 1.772 * U)));

                            // 存储RGB值
                            int rgbIdx = yIdx * 3;
                            pRgb[rgbIdx] = (byte)B;     // B
                            pRgb[rgbIdx + 1] = (byte)G; // G
                            pRgb[rgbIdx + 2] = (byte)R; // R
                        }
                    }
                }
            }

            // 创建Bitmap
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var rect = new Rectangle(0, 0, width, height);
            var bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            Marshal.Copy(rgbData, 0, bitmapData.Scan0, rgbData.Length);
            bitmap.UnlockBits(bitmapData);

            return bitmap;
        }

        /// <summary>
        /// 将RGB24格式数据转换为JPG格式
        /// </summary>
        /// <param name="rgbData">RGB24数据</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <returns>JPEG格式字节数组</returns>
        private byte[] ConvertRgb24ToJpeg(byte[] rgbData, int width, int height)
        {
            try
            {
                // 使用高质量转换
                return ConvertRgb24ToHighQualityJpeg(rgbData, width, height, 90);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to convert RGB24 to JPEG: {ex.Message}");

                // 降级到基本转换
                try
                {
                    using (var bitmap = ConvertRgb24ToBitmap(rgbData, width, height))
                    {
                        using (var ms = new MemoryStream())
                        {
                            // 以JPEG格式保存到内存流
                            bitmap.Save(ms, ImageFormat.Jpeg);
                            return ms.ToArray();
                        }
                    }
                }
                catch (Exception fallbackEx)
                {
                    Logger.Error($"Fallback conversion also failed: {fallbackEx.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// 将RGB24格式数据转换为Bitmap
        /// </summary>
        /// <param name="rgbData">RGB24数据（每像素3字节，BGR顺序）</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <returns>Bitmap对象</returns>
        private unsafe Bitmap ConvertRgb24ToBitmap(byte[] rgbData, int width, int height)
        {
            // 验证输入数据长度
            int expectedSize = width * height * 3; // RGB24: 每像素3字节
            if (rgbData.Length < expectedSize)
            {
                Logger.Error($"RGB24 data length mismatch: expected {expectedSize}, actual {rgbData.Length}");
                throw new ArgumentException($"RGB24数据长度不匹配: 期望 {expectedSize}, 实际 {rgbData.Length}");
            }

            // 创建一个临时的BGR数组，因为Bitmap的Format24bppRgb实际上是BGR存储
            byte[] bgrData = new byte[rgbData.Length];

            // 将RGB数据转换为BGR数据（交换R和B通道）
            for (int i = 0; i < rgbData.Length; i += 3)
            {
                bgrData[i] = rgbData[i + 2];     // B = R
                bgrData[i + 1] = rgbData[i + 1]; // G = G
                bgrData[i + 2] = rgbData[i];     // R = B
            }

            // 创建Bitmap
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var rect = new Rectangle(0, 0, width, height);
            var bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            // 复制已转换为BGR顺序的数据
            Marshal.Copy(bgrData, 0, bitmapData.Scan0, bgrData.Length);
            bitmap.UnlockBits(bitmapData);

            return bitmap;
        }

        /// <summary>
        /// 将RGB24格式数据转换为高质量JPG格式
        /// </summary>
        /// <param name="rgbData">RGB24数据</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <param name="quality">JPG质量 (1-100)</param>
        /// <returns>JPEG格式字节数组</returns>
        private byte[] ConvertRgb24ToHighQualityJpeg(byte[] rgbData, int width, int height, long quality = 90)
        {
            try
            {
                using (var bitmap = ConvertRgb24ToBitmap(rgbData, width, height))
                using (var ms = new MemoryStream())
                {
                    // 设置JPG编码质量
                    ImageCodecInfo jpgEncoder = GetEncoder(ImageFormat.Jpeg);
                    EncoderParameters encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

                    bitmap.Save(ms, jpgEncoder, encoderParams);
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to convert RGB24 to high quality JPEG: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取指定格式的编码器
        /// </summary>
        /// <param name="format">图像格式</param>
        /// <returns>编码器信息</returns>
        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        // --- 底层辅助算法 (BT.601 整数位移优化) ---
        private static int RgbToY(int r, int g, int b) => (19595 * r + 38470 * g + 7471 * b) >> 16;
        private static int RgbToU(int r, int g, int b) => ((-11059 * r - 21709 * g + 32768 * b) >> 16) + 128;
        private static int RgbToV(int r, int g, int b) => ((32768 * r - 27439 * g - 5329 * b) >> 16) + 128;
        private static int ClampY(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);
        private static int ClampUV(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);

        /// <summary>
        /// 双线性插值获取指定像素的 RGB（支持多种 Bayer 模式）
        /// </summary>
        /// <param name="bayer">Bayer 数据指针</param>
        /// <param name="w">图像宽度</param>
        /// <param name="h">图像高度</param>
        /// <param name="x">像素 X 坐标</param>
        /// <param name="y">像素 Y 坐标</param>
        /// <param name="r">输出 R 分量</param>
        /// <param name="g">输出 G 分量</param>
        /// <param name="b">输出 B 分量</param>
        /// <param name="bayerMode">Bayer 排列模式（RGRG/GRGR/BGBG/GBGB）</param>
        private static unsafe void GetRgbFromBayer(byte* bayer, int w, int h, int x, int y, out int r, out int g, out int b, DeviceConfig.Isp.BayerMode bayerMode = DeviceConfig.Isp.BayerMode.RGRG)
        {
            // 边界钳位（避免越界，使用三元运算符比 Math.Max/Min 更快）
            int x0 = x > 0 ? x - 1 : 0;
            int x1 = x < w - 1 ? x + 1 : w - 1;
            int y0 = y > 0 ? y - 1 : 0;
            int y1 = y < h - 1 ? y + 1 : h - 1;

            int center = bayer[y * w + x];
            int top = bayer[y0 * w + x];
            int bottom = bayer[y1 * w + x];
            int left = bayer[y * w + x0];
            int right = bayer[y * w + x1];
            int tl = bayer[y0 * w + x0];
            int tr = bayer[y0 * w + x1];
            int bl = bayer[y1 * w + x0];
            int br = bayer[y1 * w + x1];

            bool isEvenRow = (y & 1) == 0;
            bool isEvenCol = (x & 1) == 0;

            // 根据 Bayer 模式排列判断
            // Bayer 模式定义每行的颜色排列（2行交替）
            // RGRG: 偶行=RGRG... 奇行=GRGR... (R/G 交替，B 从相邻行获取)
            // GRGR: 偶行=GRGR... 奇行=RGRG... (G/R 交替)
            // BGBG: 偶行=BGBG... 奇行=GBGB... (B/G 交替)
            // GBGB: 偶行=GBGB... 奇行=BGBG... (G/B 交替)
            switch (bayerMode)
            {
                case DeviceConfig.Isp.BayerMode.RGRG:
                    // RGRG: 偶行偶列=R, 偶行奇列=G, 奇行偶列=G, 奇行奇列=R
                    if (isEvenRow && isEvenCol) { r = center; g = (left + right) >> 1; b = (top + bottom) >> 1; }
                    else if (isEvenRow && !isEvenCol) { g = center; r = (left + right) >> 1; b = (top + bottom) >> 1; }
                    else if (!isEvenRow && isEvenCol) { g = center; r = (top + bottom) >> 1; b = (left + right) >> 1; }
                    else { r = center; g = (left + right) >> 1; b = (top + bottom) >> 1; }
                    break;

                case DeviceConfig.Isp.BayerMode.GRGR:
                    // GRGR: 偶行偶列=G, 偶行奇列=R, 奇行偶列=R, 奇行奇列=G
                    if (isEvenRow && isEvenCol) { g = center; r = (top + bottom) >> 1; b = (tl + tr + bl + br) >> 2; }
                    else if (isEvenRow && !isEvenCol) { r = center; g = (top + bottom + left + right) >> 2; b = (tl + tr + bl + br) >> 2; }
                    else if (!isEvenRow && isEvenCol) { r = center; g = (left + right + tl + tr + bl + br) / 5; b = (left + right) >> 1; }
                    else { g = center; r = (top + bottom) >> 1; b = (left + right) >> 1; }
                    break;

                case DeviceConfig.Isp.BayerMode.BGBG:
                    // BGBG: 偶行偶列=B, 偶行奇列=G, 奇行偶列=G, 奇行奇列=B
                    if (isEvenRow && isEvenCol) { b = center; g = (left + right) >> 1; r = (top + bottom) >> 1; }
                    else if (isEvenRow && !isEvenCol) { g = center; b = (left + right) >> 1; r = (top + bottom) >> 1; }
                    else if (!isEvenRow && isEvenCol) { g = center; b = (top + bottom) >> 1; r = (left + right) >> 1; }
                    else { b = center; g = (left + right) >> 1; r = (top + bottom) >> 1; }
                    break;

                case DeviceConfig.Isp.BayerMode.GBGB:
                    // GBGB: 偶行偶列=G, 偶行奇列=B, 奇行偶列=B, 奇行奇列=G
                    if (isEvenRow && isEvenCol) { g = center; b = (top + bottom) >> 1; r = (tl + tr + bl + br) >> 2; }
                    else if (isEvenRow && !isEvenCol) { b = center; g = (top + bottom + left + right) >> 2; r = (tl + tr + bl + br) >> 2; }
                    else if (!isEvenRow && isEvenCol) { b = center; g = (left + right + tl + tr + bl + br) / 5; r = (left + right) >> 1; }
                    else { g = center; b = (top + bottom) >> 1; r = (left + right) >> 1; }
                    break;

                default:
                    // 默认使用 RGRG
                    if (isEvenRow && isEvenCol) { r = center; g = (left + right) >> 1; b = (top + bottom) >> 1; }
                    else if (isEvenRow && !isEvenCol) { g = center; r = (left + right) >> 1; b = (top + bottom) >> 1; }
                    else if (!isEvenRow && isEvenCol) { g = center; r = (top + bottom) >> 1; b = (left + right) >> 1; }
                    else { r = center; g = (left + right) >> 1; b = (top + bottom) >> 1; }
                    break;
            }
        }

        /// <summary>
        /// 将 RAW Bayer 数据转换为 YUV420P (I420) 格式
        /// 支持多种 Bayer 模式（RGRG/GRGR/BGBG/GBGB）和 8-bit/10-bit RAW 输入
        /// </summary>
        /// <param name="bayerData">Bayer RAW 数据（8-bit 或 10-bit  packed 格式）</param>
        /// <param name="width">图像宽度（如为奇数会自动对齐到偶数）</param>
        /// <param name="height">图像高度（如为奇数会自动对齐到偶数）</param>
        /// <returns>YUV420P (I420) 格式数据，大小为 W×H×1.5 字节</returns>
        /// <exception cref="ArgumentException">当输入数据无效时抛出</exception>
        private unsafe byte[] ConvertBayerToYuv(byte[] bayerData, int width, int height, int pixelFormat)
        {
            if (bayerData == null)
                throw new ArgumentNullException(nameof(bayerData), "Bayer数据不能为null");
            if (width <= 0 || height <= 0)
                throw new ArgumentException($"无效的图像尺寸: {width}x{height}");

            // 检查数据长度是否足够
            int expectedSize8 = width * height;
            int expectedSize10 = width * height * 2;  // 10-bit 每像素 2 字节
            bool is10Bit = (pixelFormat >= 100 && pixelFormat <= 103);  // 10-bit RAW 格式

            if (is10Bit && bayerData.Length < expectedSize10)
                throw new ArgumentException($"10-bit Bayer数据长度不足: 期望 {expectedSize10}, 实际 {bayerData.Length}");
            if (!is10Bit && bayerData.Length < expectedSize8)
                throw new ArgumentException($"8-bit Bayer数据长度不足: 期望 {expectedSize8}, 实际 {bayerData.Length}");

            // 奇数尺寸对齐到偶数（YUV420 要求 2 的倍数）
            int alignedWidth = (width + 1) & ~1;   // 向上对齐到偶数
            int alignedHeight = (height + 1) & ~1;
            bool needsAlignment = (alignedWidth != width || alignedHeight != height);

            if (needsAlignment)
            {
                Logger.Debug($"ConvertBayerToYuv: 尺寸对齐 {width}x{height} → {alignedWidth}x{alignedHeight}");
            }

            int ySize = alignedWidth * alignedHeight;
            int uvWidth = alignedWidth / 2;
            int uvHeight = alignedHeight / 2;
            int uvSize = uvWidth * uvHeight;

            // I420 布局：Y平面 + U平面 + V平面，总大小为 W×H×1.5
            byte[] yuvI420 = new byte[ySize + uvSize * 2];

            // 获取当前 Bayer 模式
            DeviceConfig.Isp.BayerMode bayerMode = _bayerMode;

            // 如果是 10-bit，先转换为 8-bit 临时缓冲区
            byte[] bayer8Data = is10Bit ? Convert10BitTo8Bit(bayerData, width, height) : bayerData;
            int bayer8Width = width;
            int bayer8Height = height;

            fixed (byte* pBayer = bayer8Data)
            fixed (byte* pYuv = yuvI420)
            {
                byte* yPtr = pYuv;
                byte* uPtr = pYuv + ySize;
                byte* vPtr = uPtr + uvSize;

                // 以 2x2 宏块为单位遍历，直接生成 4个Y，1个U，1个V
                for (int blockY = 0; blockY < uvHeight; blockY++)
                {
                    for (int blockX = 0; blockX < uvWidth; blockX++)
                    {
                        int px = blockX * 2;
                        int py = blockY * 2;

                        // 边界检查：如果超出原始尺寸，使用边缘像素复制
                        if (px >= width || py >= height)
                        {
                            // 超出原始尺寸的区域填充边缘值（黑色）
                            int yIdx00 = py * alignedWidth + px;
                            int yIdx01 = yIdx00 + 1;
                            int yIdx10 = yIdx00 + alignedWidth;
                            int yIdx11 = yIdx00 + alignedWidth + 1;

                            yPtr[yIdx00] = 16;   // Y=16 表示黑色（ITU-R BT.601）
                            yPtr[yIdx01] = 16;
                            yPtr[yIdx10] = 16;
                            yPtr[yIdx11] = 16;

                            int _uvIdx = blockY * uvWidth + blockX;
                            uPtr[_uvIdx] = 128;  // U=128 表示无色度
                            vPtr[_uvIdx] = 128;  // V=128 表示无色度
                            continue;
                        }

                        // 1. 获取 2x2 块内 4 个像素的 RGB 值（双线性插值去马赛克）
                        // 注意：边界像素需要特殊处理

                        // 声明所有变量
                        int r00, g00, b00, r01, g01, b01, r10, g10, b10, r11, g11, b11;

                        // 像素 (px, py)
                        GetRgbFromBayer(pBayer, bayer8Width, bayer8Height, px, py, out r00, out g00, out b00, bayerMode);

                        // 像素 (px+1, py)
                        if (px + 1 < width)
                            GetRgbFromBayer(pBayer, bayer8Width, bayer8Height, px + 1, py, out r01, out g01, out b01, bayerMode);
                        else
                        { r01 = r00; g01 = g00; b01 = b00; }  // 边界复制

                        // 像素 (px, py+1)
                        if (py + 1 < height)
                            GetRgbFromBayer(pBayer, bayer8Width, bayer8Height, px, py + 1, out r10, out g10, out b10, bayerMode);
                        else
                        { r10 = r00; g10 = g00; b10 = b00; }  // 边界复制

                        // 像素 (px+1, py+1)
                        if (px + 1 < width && py + 1 < height)
                            GetRgbFromBayer(pBayer, bayer8Width, bayer8Height, px + 1, py + 1, out r11, out g11, out b11, bayerMode);
                        else
                        { r11 = r00; g11 = g00; b11 = b00; }  // 边界复制

                        // 2. 写入 4 个 Y 分量
                        yPtr[py * alignedWidth + px] = (byte)ClampY(RgbToY(r00, g00, b00));
                        yPtr[py * alignedWidth + px + 1] = (byte)ClampY(RgbToY(r01, g01, b01));
                        yPtr[(py + 1) * alignedWidth + px] = (byte)ClampY(RgbToY(r10, g10, b10));
                        yPtr[(py + 1) * alignedWidth + px + 1] = (byte)ClampY(RgbToY(r11, g11, b11));

                        // 3. 计算 1 个 U 和 1 个 V（取 4 个像素 RGB 的平均值）
                        int avgR = (r00 + r01 + r10 + r11) >> 2;
                        int avgG = (g00 + g01 + g10 + g11) >> 2;
                        int avgB = (b00 + b01 + b10 + b11) >> 2;

                        int uvIdx = blockY * uvWidth + blockX;
                        uPtr[uvIdx] = (byte)ClampUV(RgbToU(avgR, avgG, avgB));
                        vPtr[uvIdx] = (byte)ClampUV(RgbToV(avgR, avgG, avgB));
                    }
                }
            }

            return yuvI420;
        }

        /// <summary>
        /// 将 10-bit Bayer 数据转换为 8-bit（右移 2 位）
        /// </summary>
        /// <param name="bayer10Data">10-bit Bayer 数据（每像素 2 字节，小端序）</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <returns>8-bit Bayer 数据</returns>
        private unsafe byte[] Convert10BitTo8Bit(byte[] bayer10Data, int width, int height)
        {
            int pixelCount = width * height;
            byte[] bayer8Data = new byte[pixelCount];
            if(bayer10Data.Length >= pixelCount*2)
            {
                fixed (byte* pSrc = bayer10Data)
                fixed (byte* pDst = bayer8Data)
                {
                    ushort* pSrc16 = (ushort*)pSrc;
                    for (int i = 0; i < pixelCount; i++)
                    {
                        // 10-bit → 8-bit：右移 2 位
                        pDst[i] = (byte)(pSrc16[i] >> 2);
                    }
                }
            }

            return bayer8Data;
        }

        /// <summary>
        /// 将RAW8数据（每像素1字节）转换为RAW8扩展数据（每像素2字节）
        /// </summary>
        /// <param name="raw8Data">原始RAW8数据（每像素1字节）</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <returns>扩展的RAW数据（每像素2字节）</returns>
        private byte[] ConvertRaw8ToRaw8Extended(byte[] raw8Data, int width, int height)
        {
            int pixelCount = width * height;

            // 检查输入数据长度是否符合预期
            if (raw8Data.Length < pixelCount)
            {
                Logger.Warn($"Input data length ({raw8Data.Length}) is less than expected pixel count ({pixelCount})");
                // 调整实际处理的像素数
                pixelCount = Math.Min(raw8Data.Length, pixelCount);
            }

            // 创建新的字节数组，每像素2字节
            byte[] extendedRawData = new byte[pixelCount * 2];

            // 将每像素1字节扩展为每像素2字节
            // 方案1：将8位数据扩展为16位（左移8位，低位补0）
            for (int i = 0; i < pixelCount; i++)
            {
                byte originalValue = raw8Data[i];
                // 高字节存储原数据，低字节补0
                extendedRawData[i * 2] = originalValue;       // 高字节
                extendedRawData[i * 2 + 1] = 0;              // 低字节
            }

            Logger.Info($"Converted RAW8 data: {raw8Data.Length} bytes -> {extendedRawData.Length} bytes, " +
                       $"{width}x{height}, {pixelCount} pixels converted");

            return extendedRawData;
        }

        /// <summary>
        /// 将RAW8数据（每像素1字节）转换为RAW8扩展数据（每像素2字节）- 小端序版本
        /// </summary>
        /// <param name="raw8Data">原始RAW8数据（每像素1字节）</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <returns>扩展的RAW数据（每像素2字节，小端序）</returns>
        private byte[] ConvertRaw8ToRaw8ExtendedLittleEndian(byte[] raw8Data, int width, int height)
        {
            int pixelCount = width * height;

            // 检查输入数据长度是否符合预期
            if (raw8Data.Length < pixelCount)
            {
                Logger.Warn($"Input data length ({raw8Data.Length}) is less than expected pixel count ({pixelCount})");
                // 调整实际处理的像素数
                pixelCount = Math.Min(raw8Data.Length, pixelCount);
            }

            // 创建新的字节数组，每像素2字节
            byte[] extendedRawData = new byte[pixelCount * 2];

            // 将每像素1字节扩展为每像素2字节（小端序：低字节在前，高字节在后）
            for (int i = 0; i < pixelCount; i++)
            {
                byte originalValue = raw8Data[i];
                //// 低字节存储原数据，高字节补0（小端序）
                //extendedRawData[i * 2] = originalValue;       // 低字节
                //extendedRawData[i * 2 + 1] = 0            ;  // 高字节

                // 正确方法：比例映射到16位范围
                // 8位 0-255 → 10位 0-1023
                ushort extendedValue = (ushort)(originalValue * 1023 / 255);

                // 小端序存储
                extendedRawData[i * 2] = (byte)(extendedValue & 0xFF);        // 低字节
                extendedRawData[i * 2 + 1] = (byte)(extendedValue >> 8);      // 高字节
            }

            Logger.Info($"Converted RAW8 data to little-endian format: {raw8Data.Length} bytes -> {extendedRawData.Length} bytes, " +
                       $"{width}x{height}, {pixelCount} pixels converted");

            return extendedRawData;
        }

        /// <summary>
        /// 将MJPEG数据解码为RGB格式
        /// </summary>
        /// <param name="mjpegData">MJPEG格式数据</param>
        /// <returns>解码后的RGB数据</returns>
        private byte[] DecodeMjpegToRgb(byte[] mjpegData)
        {
            try
            {
                using (var ms = new MemoryStream(mjpegData))
                using (var bitmap = new Bitmap(ms))
                {
                    // 将Bitmap转换为RGB字节数组
                    return ConvertBitmapToRgbArray(bitmap);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"MJPEG decode error: {ex.Message}");
                // 如果解码失败，返回原始数据
                return mjpegData;
            }
        }

        /// <summary>
        /// 将Bitmap转换为RGB字节数组
        /// </summary>
        /// <param name="bitmap">输入Bitmap</param>
        /// <returns>RGB字节数组（每像素3字节，BGR顺序）</returns>
        private unsafe byte[] ConvertBitmapToRgbArray(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            int stride = width * 3; // 每像素3字节 (RGB)
            int totalSize = stride * height;

            byte[] rgbData = new byte[totalSize];

            var rect = new Rectangle(0, 0, width, height);
            var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                // 使用Marshal.Copy复制数据
                Marshal.Copy(bitmapData.Scan0, rgbData, 0, rgbData.Length);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return rgbData;
        }

        /// <summary>
        /// 垂直翻转图像数据（上下翻转）
        /// </summary>
        /// <param name="imageData">原始图像数据</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <param name="bytesPerPixel">每像素字节数</param>
        /// <returns>翻转后的图像数据</returns>
        private static byte[] FlipImageVertically(byte[] imageData, int width, int height, int bytesPerPixel = 1)
        {
            int stride = width * bytesPerPixel; // 每行字节数
            byte[] flippedData = new byte[imageData.Length];

            // 逐行翻转：将第i行复制到(height-1-i)行的位置
            for (int row = 0; row < height; row++)
            {
                int sourceIndex = row * stride;
                int destIndex = (height - 1 - row) * stride;

                // 复制整行数据
                Array.Copy(imageData, sourceIndex, flippedData, destIndex, stride);
            }

            return flippedData;
        }

        /// <summary>
        /// 水平翻转图像数据（镜像翻转）
        /// </summary>
        /// <param name="imageData">原始图像数据</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <param name="bytesPerPixel">每像素字节数</param>
        /// <returns>翻转后的图像数据</returns>
        private static byte[] FlipImageHorizontally(byte[] imageData, int width, int height, int bytesPerPixel = 1)
        {
            int stride = width * bytesPerPixel; // 每行字节数
            byte[] flippedData = new byte[imageData.Length];

            for (int row = 0; row < height; row++)
            {
                int rowIndex = row * stride;

                // 对每一行进行水平翻转
                for (int col = 0; col < width; col++)
                {
                    int srcPixelIndex = rowIndex + col * bytesPerPixel;
                    int dstPixelIndex = rowIndex + (width - 1 - col) * bytesPerPixel;

                    // 复制每个像素的所有字节
                    for (int b = 0; b < bytesPerPixel; b++)
                    {
                        flippedData[dstPixelIndex + b] = imageData[srcPixelIndex + b];
                    }
                }
            }

            return flippedData;
        }

        /// <summary>
        /// 同时进行水平和垂直翻转（旋转180度）
        /// </summary>
        /// <param name="imageData">原始图像数据</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <param name="bytesPerPixel">每像素字节数</param>
        /// <returns>翻转后的图像数据</returns>
        private static byte[] RotateImage180(byte[] imageData, int width, int height, int bytesPerPixel = 1)
        {
            int stride = width * bytesPerPixel; // 每行字节数
            byte[] rotatedData = new byte[imageData.Length];

            for (int row = 0; row < height; row++)
            {
                int srcRowIndex = row * stride;
                int dstRowIndex = (height - 1 - row) * stride;

                // 对每一行进行水平翻转
                for (int col = 0; col < width; col++)
                {
                    int srcPixelIndex = srcRowIndex + col * bytesPerPixel;
                    int dstPixelIndex = dstRowIndex + (width - 1 - col) * bytesPerPixel;

                    // 复制每个像素的所有字节
                    for (int b = 0; b < bytesPerPixel; b++)
                    {
                        rotatedData[dstPixelIndex + b] = imageData[srcPixelIndex + b];
                    }
                }
            }

            return rotatedData;
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            Logger.Debug("Disposing UvcReceiver...");
            Disconnect();

            // 清理事件订阅
            _dataReceive = null;
            _yuvDataReceive = null;
            _rawDataReceive = null;
            _statusChange = null;

            // 释放异步资源
            _asyncSaveSemaphore?.Dispose();

            _disposed = true;
            Logger.Debug("UvcReceiver disposed.");
        }

        #endregion
    }
}
