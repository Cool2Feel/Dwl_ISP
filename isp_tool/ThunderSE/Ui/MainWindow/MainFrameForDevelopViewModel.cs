using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using ThunderSE.Common;
using ThunderSE.DeviceConfig;

namespace ThunderSE.Ui.MainWindow
{
    class MainFrameForDevelopViewModel : ViewModelBase
    {
        private ObservableCollection<KeyValuePair<string, ObservableCollection<KeyValuePair<string, Config>>>> _deviceConfigs
            = new ObservableCollection<KeyValuePair<string, ObservableCollection<KeyValuePair<string, Config>>>>()
        {
            new KeyValuePair<string, ObservableCollection<KeyValuePair<string, Config>>>("在线",
                 new ObservableCollection<KeyValuePair<string, Config>>()),
            new KeyValuePair<string, ObservableCollection<KeyValuePair<string, Config>>>("离线",
                 new ObservableCollection<KeyValuePair<string, Config>>())
        };

        public RelayCommand<string> CreateOfflineConfigCommand
        {
            get;
            set;
        }

        public RelayCommand<string> OpenOfflineConfigFileCommand
        {
            get;
            set;
        }

        public MainFrameForDevelopViewModel()
        {
#if DEBUG
            if(IsInDesignMode)
            {
                return;
            }
#endif
            CreateOfflineConfigCommand = new RelayCommand<string>(CreateOfflineConfigFile);
            OpenOfflineConfigFileCommand = new RelayCommand<string>(OpenOfflineConfigFile);
            ConfigManager.GetInstance().OnConfigListChange += OnConfigListChange;
        }

        ~MainFrameForDevelopViewModel()
        {
            ConfigManager.GetInstance().OnConfigListChange -= OnConfigListChange;
        }

        public ObservableCollection<KeyValuePair<string, ObservableCollection<KeyValuePair<string, Config>>>> DeviceConfigs
        {
            get { return _deviceConfigs; }
            set
            {
                _deviceConfigs = value;
                RaisePropertyChanged("DeviceConfigs");
            }
        }

        public ObservableCollection<KeyValuePair<string, Config>> OnlineConfigs
        {
            get { return DeviceConfigs[0].Value; }
        }

        public ObservableCollection<KeyValuePair<string, Config>> OfflineConfigs
        {
            get { return DeviceConfigs[1].Value; }
        }

        private void CreateOfflineConfigFile(string configName)
        {
            Logger.Debug($"CreateOfflineConfigFile configName: {configName}");
            ConfigManager.GetInstance().AddConfig(Config.ConfigType.Offline, "", configName, "");
        }

        private void OpenOfflineConfigFile(string configFilePath)
        {
            Logger.Debug($"OpenOfflineConfigFile configFilePath: {configFilePath}");
            ConfigManager.GetInstance().AddConfig(Config.ConfigType.Offline, "", Path.GetFileNameWithoutExtension(configFilePath), "");
            ConfigManager.GetInstance().GetConfig(Path.GetFileNameWithoutExtension(configFilePath)).FilePath = configFilePath;
        }

        // 处理model传过来的消息
        private void OnConfigListChange(ConfigManager.ChangeEvent changeEvent, Config.ConfigType type, string configName)
        {
            var targetConfigList = OfflineConfigs;
            if (type == Config.ConfigType.Online)
            {
                targetConfigList = OnlineConfigs;
            }

            switch (changeEvent)
            {
                case ConfigManager.ChangeEvent.Add:
                    targetConfigList.Add(new KeyValuePair<string, Config>(configName, ConfigManager.GetInstance().GetConfig(configName)));
                    break;
                case ConfigManager.ChangeEvent.Remove:
                    int foundIndex = -1;
                    for (int i = 0; i < targetConfigList.Count; i++)
                    {
                        if (targetConfigList[i].Key == configName)
                        {
                            foundIndex = i;
                            break;
                        }
                    }
                    if (foundIndex != -1)
                    {
                        targetConfigList.RemoveAt(foundIndex);
                    }
                    break;
                default:
                    break;
            }
        }

        public void ScanOnlineDevice()
        {
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.WorkerSupportsCancellation = true;

            worker.DoWork += (s, args) =>
            {
                try
                {
                    ConfigManager.GetInstance().ScanDevice();
                }
                catch (Exception ex)
                {
                    args.Result = ex;
                }
            };

            worker.RunWorkerCompleted += (s, args) =>
            {
                if (args.Error != null)
                {
                    Logger.Error("Device scan failed.", args.Error);
                }
                else
                {
#if DEBUG
                    Thread.Sleep(2500);
                     //检查是否有配置，如果没有则自动创建模拟设备用于调试
                    var configs = ConfigManager.GetInstance().GetAllConfigs();
                    if (configs.Length == 0)
                    {
                        Logger.Info("Auto-creating mock device for debugging...");
                        ConfigManager.GetInstance().CreateMockDevice("Debug_Device");
                    }
#endif
                }
            };

            worker.RunWorkerAsync();

        }
    }
}
