using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using ThunderSE.DeviceConfig;
using ThunderSE.DeviceConfig.Isp;

namespace ThunderSE.Ui.MainWindow.UserMode
{
    class MainFrameForUserViewModel : ViewModelBase
    {
        public event EventHandler<DeviceChangeEventArgs> DeviceChange;

        private RelayCommand _writeConfigCommand = null;
        private RelayCommand _reloadConfigCommand = null;
        private RelayCommand _saveConfigAsCommand = null;
        private RelayCommand<string> _openOfflineConfigFileCommand = null;

        public class DeviceChangeEventArgs : EventArgs
        {
            public DeviceChangeEventArgs(bool isConnect) { IsConnect = isConnect; }
            public bool IsConnect = false;
        }

        public MainFrameForUserViewModel()
        {
#if DEBUG
            if (IsInDesignMode)
            {
                return;
            }
#endif
            ConfigManager.GetInstance().OnConfigListChange += OnConfigListChange;

            _writeConfigCommand = new RelayCommand(WriteConfig);
            _reloadConfigCommand = new RelayCommand(ReloadConfig);
            _saveConfigAsCommand = new RelayCommand(SaveConfigAs);

            _openOfflineConfigFileCommand = new RelayCommand<string>(OpenOfflineConfigFile);
        }

        public Config Config
        {
            get;
            set;
        }

        public RelayCommand ReloadConfigCommand
        {
            get { return _reloadConfigCommand; }
            set { _reloadConfigCommand = value; }
        }

        public RelayCommand SaveConfigAsCommand
        {
            get { return _saveConfigAsCommand; }
            set { _saveConfigAsCommand = value; }
        }

        public RelayCommand WriteConfigCommand
        {
            get { return _writeConfigCommand; }
            set { _writeConfigCommand = value; }
        }
        public RelayCommand<string> OpenOfflineConfigFileCommand
        {
            get { return _openOfflineConfigFileCommand; }
            set { _openOfflineConfigFileCommand = value; }
        }

        ~MainFrameForUserViewModel()
        {
            ConfigManager.GetInstance().OnConfigListChange -= OnConfigListChange;
        }

        private void OnConfigListChange(ConfigManager.ChangeEvent changeEvent, 
            Config.ConfigType configType, string name)
        {
            if (configType == Config.ConfigType.Online)
            {
                switch (changeEvent)
                {
                    case ConfigManager.ChangeEvent.Add:
                        {
                            Config = ConfigManager.GetInstance().GetConfig(name);
                            DeviceChange(this, new DeviceChangeEventArgs(true));
                        }
                        break;
                    case ConfigManager.ChangeEvent.Remove:
                        {
                            DeviceChange(this, new DeviceChangeEventArgs(false));
                            Config = null;
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        public void ScanOnlineDevice()
        {
            ConfigManager.GetInstance().ScanDevice();
        }

        private void ReloadConfig()
        {
            if (Config == null )
            {
                MessageBox.Show("设备已离线", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Config.RefreshDataFromDevice();
        }

        private void SaveConfigAs()
        {
            if (Config == null)
            {
                MessageBox.Show("设备已离线", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
            saveFileDialog.CheckFileExists = false;
            saveFileDialog.CheckPathExists = false;
            saveFileDialog.Filter = "isp配置文件(*.isp) | *.isp";
            if (!(bool)saveFileDialog.ShowDialog())
            {
                return;
            }

            Config.WriteToFile(saveFileDialog.FileName);
            System.Windows.MessageBox.Show("已保存为:" + saveFileDialog.FileName, "", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void WriteConfig()
        {
            if (Config == null)
            {
                MessageBox.Show("设备已离线", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Config.WriteToDevice();
        }

        private void OpenOfflineConfigFile(string configFilePath)
        {
            if (Config == null)
            {
                MessageBox.Show("设备已离线", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Config.ReadFromFile(configFilePath);
        }
    }
}
