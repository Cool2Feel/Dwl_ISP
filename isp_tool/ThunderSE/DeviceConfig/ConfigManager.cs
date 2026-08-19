using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ThunderSE.Common;
using ThunderSE.Device;
using ThunderSE.Uvc;

namespace ThunderSE.DeviceConfig
{
    /// <summary>
    /// 配置管理器,负责管理所有设备配置的增删改查
    /// 实现线程安全的单例模式和并发安全的配置字典
    /// </summary>
    class ConfigManager : IDisposable
    {
        // 线程安全的单例实现
        private static readonly Lazy<ConfigManager> _instance = new Lazy<ConfigManager>(() => new ConfigManager());

        // 使用并发字典避免锁
        private readonly ConcurrentDictionary<string, Config> _configs = new ConcurrentDictionary<string, Config>();
        private volatile bool _disposed = false;

        public enum ChangeEvent
        {
            Add,
            Remove
        }

        public delegate void OnConfigListChangeHandler(ChangeEvent changeEvent, Config.ConfigType configType, string name);
        public event OnConfigListChangeHandler OnConfigListChange;

        public static ConfigManager Instance => _instance.Value;

        /// <summary>
        /// 保持向后兼容的静态方法
        /// </summary>
        public static ConfigManager GetInstance() => Instance;

        private ConfigManager()
        {
            try
            {
                Logger.Debug("Initializing ConfigManager...");
                DeviceManger.Instance.DeviceChange += OnDeviceChange;
                Logger.Info("ConfigManager initialized successfully.");
            }
            catch (Exception ex)
            {
                Logger.Error("ConfigManager init error.", ex);
            }
        }

        /// <summary>
        /// 添加配置
        /// </summary>
        public bool AddConfig(Config.ConfigType configType, string devLocationOrFilePath, string configName, string uvcInterface)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConfigManager));

            try
            {
                Logger.Info($"Adding config: Type={configType}, Name={configName}, Path/Location={devLocationOrFilePath}");

                var config = new Config(configType, configName);
                if (configType == Config.ConfigType.Offline)
                {
                    config.FilePath = devLocationOrFilePath;
                    Logger.Debug($"Reading config from file: {devLocationOrFilePath}");
                    config.ReadFromFile();
                }
                else
                {
                    config.DeviceLocation = devLocationOrFilePath;
                    config.UvcInterface = uvcInterface;
                    Logger.Debug($"Reading config from device: {devLocationOrFilePath}");
                    config.ReadFromDevice();
                }

                // 使用TryAdd避免竞态条件
                if (_configs.TryAdd(configName, config))
                {
                    Logger.Info($"Config '{configName}' added successfully.");
                    // 确保事件在UI线程触发(因为订阅者可能修改ObservableCollection)
                    RaiseOnConfigListChange(ChangeEvent.Add, configType, config.Name);
                    return true;
                }

                Logger.Warn($"Config '{configName}' already exists.");
                return false;
            }
            catch (Exception e)
            {
                Logger.Error($"AddConfig error for '{configName}'.", e);
                return false;
            }
        }

        /// <summary>
        /// 移除配置
        /// </summary>
        public bool RemoveConfig(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (_configs.TryRemove(name, out Config removedConfig))
            {
                Logger.Info($"Config '{name}' removed.");

                // 【新增】释放Config资源
                if (removedConfig is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                        Logger.Debug($"Config '{name}' disposed.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error disposing config '{name}': {ex.Message}");
                    }
                }

                // 确保事件在UI线程触发
                RaiseOnConfigListChange(ChangeEvent.Remove, removedConfig.Type, name);
                return true;
            }

            Logger.Warn($"Config '{name}' not found for removal.");
            return false;
        }

        /// <summary>
        /// 获取配置(线程安全)
        /// </summary>
        public Config GetConfig(string configName)
        {
            if (string.IsNullOrEmpty(configName))
                return null;

            _configs.TryGetValue(configName, out Config config);
            return config;
        }

        /// <summary>
        /// 获取所有配置名称
        /// </summary>
        public string[] GetConfigNames()
        {
            return _configs.Keys.ToArray();
        }

        /// <summary>
        /// 获取所有配置
        /// </summary>
        public Config[] GetAllConfigs()
        {
            return _configs.Values.ToArray();
        }

        public void ScanDevice()
        {
            if (_disposed) return;

            try
            {
                Logger.Debug("Scanning device via ConfigManager...");
                DeviceManger.Instance.ScanDevice();
            }
            catch (Exception ex)
            {
                Logger.Error("ScanDevice error.", ex);
            }
        }

        private void OnDeviceChange(DeviceEvent eventType, string location, string model, string uvcInterafce)
        {
            if (_disposed) return;

            try
            {
                Logger.Info($"Device change event received: {eventType}, Model={model}");

                // 关键修复：如果应用正在退出，跳过所有自动连接/断开逻辑
                if (UvcReceiver.IsApplicationExiting)
                {
                    Logger.Debug($"Skipping OnDeviceChange: Application is exiting (event: {eventType})");
                    return;
                }

                // 关键修复：如果 UvcReceiver 正在重连/复位，跳过自动连接逻辑
                if (UvcReceiver.Instance.IsReconnecting)
                {
                    Logger.Debug($"Skipping OnDeviceChange: UvcReceiver is reconnecting (event: {eventType})");
                    return;
                }

                if (eventType == DeviceEvent.Arrival)
                {
                    Logger.Info($"Device arrived: {model} at {location}, connecting...");

                    string deviceKey = $"{model}_{location}";
                    // 异步连接设备,避免阻塞设备检测线程
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            // 再次检查重连状态（因为可能在上次检查后状态变化）
                            if (UvcReceiver.Instance.IsReconnecting)
                            {
                                Logger.Debug($"Skipping Connect: UvcReceiver is reconnecting");
                                return;
                            }

                            AddConfig(Config.ConfigType.Online, location, model, uvcInterafce);
                            Config config = GetConfig(model);
                            if (config != null)
                            {
                                int _width = config.IspProcessor.IspCommonConfig.ResolutionWidth;
                                int _height = config.IspProcessor.IspCommonConfig.ResolutionHeight;
                                UvcReceiver.Instance.VideoWidth = _width;
                                UvcReceiver.Instance.VideoHeight = _height;
                                Logger.Info($"Config resolution from {_width}x{_height} to {UvcReceiver.Instance.VideoWidth}x{UvcReceiver.Instance.VideoHeight}");
                            }
                            Logger.Debug($"Connecting to UVC: {uvcInterafce}");
                            bool success = UvcReceiver.Instance.Connect(uvcInterafce);

                            if (success)
                            {
                                Logger.Debug($"UVC connected successfully");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error handling device arrival: {model}", ex);
                        }

                    });
                }
                else
                {
                    Logger.Info($"Device removed: {model} at {location}");

                    //string deviceKey = $"{model}_{location}";
                    //Uvc.MultiUvcManager.Instance.RemoveDevice(deviceKey);

                    RemoveConfig(model);

                    // 只在非重连状态下断开 UVC
                    if (!UvcReceiver.Instance.IsReconnecting)
                    {
                        UvcReceiver.Instance.Disconnect();
                    }
                    else
                    {
                        Logger.Debug("Skipping Disconnect: UvcReceiver is reconnecting");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("OnDeviceChange error.", ex);
            }
        }

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                Logger.Debug("Disposing ConfigManager...");
                // 取消事件订阅
                DeviceManger.Instance.DeviceChange -= OnDeviceChange;
                OnConfigListChange = null;

                // 清空配置字典
                _configs.Clear();
                Logger.Info("ConfigManager disposed.");
            }
            catch (Exception ex)
            {
                Logger.Error("ConfigManager dispose error.", ex);
            }

            _disposed = true;
        }

        #endregion

        #region 事件辅助方法

        /// <summary>
        /// 确保OnConfigListChange事件在UI线程触发
        /// 因为订阅者(ViewModel)可能修改ObservableCollection,而WPF集合绑定必须在UI线程
        /// </summary>
        private void RaiseOnConfigListChange(ChangeEvent changeEvent, Config.ConfigType configType, string name)
        {
            var handler = OnConfigListChange;
            if (handler == null) return;

            // 检查当前是否在UI线程
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                // 不在UI线程,调度到UI线程执行
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal,
                    handler,
                    changeEvent,
                    configType,
                    name);
            }
            else
            {
                // 已在UI线程,直接调用
                handler(changeEvent, configType, name);
            }
        }


        /// <summary>
        /// 创建模拟设备进行调试（无需真实硬件）
        /// </summary>
        /// <param name="deviceModel">模拟的设备型号</param>
        /// <param name="configFilePath">可选的配置文件路径，如果为空则创建默认配置</param>
        /// <returns>是否成功创建模拟设备</returns>
        public bool CreateMockDevice(string deviceModel = "Mock_Device_H63P", string configFilePath = null)
        {
            if (_disposed) return false;

            try
            {
                Logger.Info($"Creating mock device: {deviceModel}");

                // 生成一个虚拟的设备位置标识
                string mockLocation = $"MOCK_{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";

                // 检查是否已存在同名配置
                if (_configs.ContainsKey(deviceModel))
                {
                    Logger.Warn($"Mock device '{deviceModel}' already exists.");
                    return false;
                }

                // 创建离线配置
                var config = new Config(Config.ConfigType.Offline, deviceModel);

                if (!string.IsNullOrEmpty(configFilePath) && System.IO.File.Exists(configFilePath))
                {
                    // 如果提供了配置文件路径，从文件加载
                    Logger.Info($"Loading mock device config from file: {configFilePath}");
                    config.FilePath = configFilePath;
                    config.ReadFromFile();
                }
                else
                {
                    // 否则使用默认配置（空配置，可以后续手动设置参数）
                    Logger.Info("Creating mock device with default configuration.");
                    // 这里可以选择性地初始化一些默认值
                }

                // 添加到配置字典
                if (_configs.TryAdd(deviceModel, config))
                {
                    Logger.Info($"Mock device '{deviceModel}' created successfully at location: {mockLocation}");

                    // 触发配置列表变更事件
                    RaiseOnConfigListChange(ChangeEvent.Add, Config.ConfigType.Offline, deviceModel);

                    return true;
                }

                Logger.Error($"Failed to add mock device '{deviceModel}' to config dictionary.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"CreateMockDevice error for '{deviceModel}'.", ex);
                return false;
            }
        }

        /// <summary>
        /// 移除模拟设备
        /// </summary>
        /// <param name="deviceModel">要移除的设备型号</param>
        /// <returns>是否成功移除</returns>
        public bool RemoveMockDevice(string deviceModel)
        {
            if (string.IsNullOrEmpty(deviceModel))
                return false;

            if (_configs.TryRemove(deviceModel, out Config removedConfig))
            {
                Logger.Info($"Mock device '{deviceModel}' removed.");

                // 查找并移除对应的UVC设备

                RaiseOnConfigListChange(ChangeEvent.Remove, removedConfig.Type, deviceModel);
                return true;
            }

            Logger.Warn($"Mock device '{deviceModel}' not found for removal.");
            return false;
        }

        #endregion
    }
}
