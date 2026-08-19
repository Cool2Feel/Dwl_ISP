using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Xml;
using ThunderSE.Common;
using ThunderSE.Device;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.DeviceConfig.Lcd;

namespace ThunderSE.DeviceConfig
{
     public class Config : IDisposable
    {
        // 设备写入互斥锁，与 DeviceConfigPageViewModel 共享
        public readonly object DeviceWriteLock = new object();
        // #define _ISP_BIT_WIDTH_ 2
        public const int IspBitWidth = 2;

        public enum ConfigType
        {
            Online,
            Offline
        }

        private ConfigType _type = ConfigType.Offline;
        private Isp.Processor _ispProcessor = new Isp.Processor();
        private Lcd.LcdSetting _lcdSetting = new Lcd.LcdSetting();
        private List<Isp.IspModule> _serializeIspModuleListForUserMode = new List<IspModule>()
        {
            IspModule.AE,
            IspModule.Ddc,
            IspModule.Ccm,
            IspModule.Ch,
            IspModule.Vde,
            IspModule.Ee,
            IspModule.Saj
        };

        // 防抖机制：防止 OnCommonConfigChange 频繁触发导致 UVC 重入
        //private DateTime _lastModeChangeTime = DateTime.MinValue;
        //private const int ModeChangeDebounceMs = 2000;  // 2秒内只允许一次模式切换
        //private readonly object _modeChangeLock = new object();

        public Isp.Processor IspProcessor
        {
            get { return _ispProcessor; }
        }

        public Lcd.LcdSetting LcdSetting
        {
            get { return _lcdSetting; }
        }

        public string Name
        {
            get;
            set;
        }

        public ConfigType Type
        {
            get { return _type; }
            set { _type = value; }
        }

        public string FilePath
        {
            get;
            set;
        }

        public string DeviceLocation
        {
            get;
            set;
        }

        public string UvcInterface
        {
            get;
            set;
        }

        public Config(ConfigType type, string name)
        {
            Type = type;
            Name = name;
            _ispProcessor.ConfigName = Name;
        }

        async void OnCommonConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try
            {
                // CommonConfig里的东西需要即时写进去
                byte[] bytesToWrite;
                if (IspProcessor.IspCommonConfig.PropertyNameToStructMemberMap.Keys.Contains(e.PropertyName))
                {
                    var typeOfCommonConfig = IspProcessor.IspCommonConfig.GetType();
                    var propInfo = typeOfCommonConfig.GetProperty(e.PropertyName);
                    if (propInfo != null)
                    {
                        object valueToWrite = propInfo.GetValue(IspProcessor.IspCommonConfig, null);
                        if (valueToWrite.GetType() == typeof(byte))
                        {
                            bytesToWrite = new byte[] { (byte)valueToWrite };
                        }
                        else if (valueToWrite.GetType() == typeof(SetMode))
                        {
                            // SetMode 枚举值转换为 byte：RAW=0, MJPG=1, YUV=2
                            bytesToWrite = new byte[] { (byte)(SetMode)valueToWrite };
                        }
                        else
                        {
                            bytesToWrite = (byte[])typeof(BitConverter).GetMethod("GetBytes",
                            new Type[] { valueToWrite.GetType() }).Invoke(null, new object[] { valueToWrite });
                        }
                    }
                    else
                    {
                        string ispModuleEnumName = e.PropertyName.Substring(2).Replace("Enable", "");
                        IspModule ispModule = (IspModule)Enum.Parse(typeof(IspModule), ispModuleEnumName);
                        bool isEnablesOpen = IspProcessor.IspCommonConfig.ProcessorStepsEnables[(int)ispModule].Value;
                        byte actualValue = isEnablesOpen ? (byte)IspProcessor.IspCommonConfig.ProcessorStepsEnablesActualValueMap[ispModule] :(byte)0;
                        if (actualValue == 0 && isEnablesOpen)
                            actualValue = 1;
                        bytesToWrite = new byte[] { actualValue };
                    }

                    int writePos = 0;
                    string commonDataMemberName = IspProcessor.IspCommonConfig.PropertyNameToStructMemberMap[e.PropertyName];

                    if (commonDataMemberName.StartsWith("hvb."))
                    {
                        var splitParts = commonDataMemberName.Split(new string[] { "." }, StringSplitOptions.RemoveEmptyEntries);

                        writePos = (int)Marshal.OffsetOf(typeof(CommonData), splitParts[0]) +
                            (int)Marshal.OffsetOf(typeof(Hvb_Adapt), splitParts[1]);
                    }
                    else
                    {
                        writePos = (int)Marshal.OffsetOf(typeof(CommonData), commonDataMemberName);
                    }

                    DeviceApi.WriteAx327XSensorProperty(DeviceLocation,
                            writePos,
                            bytesToWrite,
                            bytesToWrite.Length);
                    /*
                    if (commonDataMemberName.Equals("set_mode"))
                    {
                        // 防抖检查：2秒内只允许一次模式切换
                        lock (_modeChangeLock)
                        {
                            var now = DateTime.Now;
                            var timeSinceLastChange = (now - _lastModeChangeTime).TotalMilliseconds;
                            if (timeSinceLastChange < ModeChangeDebounceMs)
                            {
                                Logger.Warn($"Mode change debounced: {(ModeChangeDebounceMs - timeSinceLastChange):F0}ms remaining");
                                return;  // 忽略此次触发
                            }
                            _lastModeChangeTime = now;
                        }

                        // 额外检查：如果 UVC 正在重连，跳过此次操作
                        if (UvcReceiver.Instance.IsReconnecting)
                        {
                            Logger.Warn("Skipping mode change: UVC is already reconnecting");
                            return;
                        }
                        
                        // 额外检查：如果 UVC 正在断开，跳过此次操作
                        if (!UvcReceiver.Instance.IsConnected)
                        {
                            Logger.Warn("Skipping mode change: UVC is not connected, waiting for reconnect");
                            return;
                        }

                        // set_mode 变化需要重新连接 UVC 设备
                        // 提供两种模式：
                        //   1. 软件复位USB设备（更彻底，模拟重新插拔）
                        //   2. 普通UVC重连（较快，只重置视频流）

                        // 可通过配置切换模式（默认使用软件复位）
                        bool useSoftwareReset = false;  // 设置为false使用普通重连

                        // 使用后台任务执行，避免阻塞 UI 线程
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                Logger.Info($"========================================");
                                Logger.Info($"Mode changed detected!");
                                Logger.Info($"Mode member: {commonDataMemberName}");
                                Logger.Info($"UVC interface: {UvcInterface}");
                                Logger.Info($"Reset mode: {(useSoftwareReset ? "Software USB Reset" : "UVC Reconnect")}");
                                Logger.Info($"========================================");

                                bool success = false;

                                if (useSoftwareReset)
                                {
                                    // 方案1: 软件复位USB设备（模拟重新插拔）
                                    Logger.Info("Starting software USB reset...");

                                    string deviceLink = @"\\?\\usbstor#disk&ven_buildwin&prod_media-player&rev_1.00#7&3602a9d&0&20250708v1.000&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}";

                                    if (!deviceLink.StartsWith(@"\\?\"))
                                    {
                                        Logger.Warn($"UvcInterface is not a full device link, trying to use as-is: {deviceLink}");
                                    }

                                    // 调用软件复位（在后台线程中同步执行）
                                    success = UvcReceiver.Instance.SoftwareResetDevice(deviceLink,
                                        waitDisconnectMs: 2000,
                                        waitConnectMs: 3000);
                                }
                                else
                                {
                                    // 方案2: 普通UVC重连（较快）
                                    Logger.Info("Starting UVC reconnect...");
                                    success = await UvcReceiver.Instance.Reconnect(UvcInterface,
                                        retryCount: 2,
                                        retryDelayMs: 300);  // 增加延迟时间到 1500ms
                                }

                                if (!success)
                                {
                                    Logger.Error("Device reconnect/reset failed. Video stream may be unavailable.");

                                    // 在 UI 线程显示错误提示
                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        string errorMsg = useSoftwareReset
                                            ? $"USB 设备软件复位失败。\n\n设备接口: {UvcInterface}\n\n请尝试物理重新插拔设备。"
                                            : $"UVC 设备重新连接失败。\n\n设备接口: {UvcInterface}\n\n视频流可能不可用，请检查设备连接后手动重连。";

                                        MessageBox.Show(
                                            errorMsg,
                                            useSoftwareReset ? "USB 复位失败" : "UVC 连接失败",
                                            MessageBoxButton.OK,
                                            MessageBoxImage.Warning);
                                    });
                                }
                                else
                                {
                                    string successMsg = useSoftwareReset
                                        ? "USB device software reset completed successfully!"
                                        : "UVC device reconnected successfully.";
                                    Logger.Info($"✓ {successMsg}");
                                }
                            }
                            catch (AccessViolationException avEx)
                            {
                                // 捕获内存访问异常，防止 CLR 崩溃
                                Logger.Error($"Mode change handler AccessViolationException: {avEx.Message}", avEx);

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    MessageBox.Show(
                                        $"UVC 设备内存访问异常。\n\n错误信息: {avEx.Message}\n\n请检查设备连接后重试。",
                                        "UVC 内存访问异常",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Error);
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Mode change handler exception: {ex.GetType().Name} - {ex.Message}", ex);

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    MessageBox.Show(
                                        $"设备模式切换异常。\n\n错误信息: {ex.Message}\n\n请检查设备连接后重试。",
                                        "模式切换异常",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Error);
                                });
                            }
                        });
                    }
                    
                    */
                }
            }
            catch (Exception outerEx)
            {
                // 最外层异常捕获，防止 async void 导致的 CLR 崩溃
                Logger.Error($"OnCommonConfigChange outer exception: {outerEx.GetType().Name} - {outerEx.Message}", outerEx);
            }

            //switch (e.PropertyName)
            //{
            //    case "ExpGain":
            //        DeviceApi.WriteAx327XSensorProperty(DeviceLocation,
            //            Marshal.OffsetOf(typeof(CommonData), "exp_gain").ToInt32(),
            //            BitConverter.GetBytes(IspProcessor.IspCommonConfig.ExpGain),
            //            Marshal.SizeOf(typeof(int)));
            //        break;

            //    case "GainMax":
            //        DeviceApi.WriteAx327XSensorProperty(DeviceLocation,
            //            Marshal.OffsetOf(typeof(CommonData), "gain_max").ToInt32(),
            //            BitConverter.GetBytes(IspProcessor.IspCommonConfig.GainMax),
            //            Marshal.SizeOf(typeof(int)));
            //        break;

            //    case "IsExpGainEnable":
            //        DeviceApi.WriteAx327XSensorProperty(DeviceLocation,
            //            Marshal.OffsetOf(typeof(CommonData), "exp_gain_en").ToInt32(),
            //            BitConverter.GetBytes(IspProcessor.IspCommonConfig.IsExpGainEnable),
            //            Marshal.SizeOf(typeof(char)));
            //        break;

            //    case "IsBlcEnable":
            //        DeviceApi.WriteAx327XSensorProperty(DeviceLocation,
            //            Marshal.OffsetOf(typeof(CommonData), "blk_en").ToInt32(),
            //            BitConverter.GetBytes(IspProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Blc].Value ?
            //            0x01 : 0x00),
            //            Marshal.SizeOf(typeof(char)));
            //        break;

            //    case "IsLscEnable":
            //        DeviceApi.WriteAx327XSensorProperty(DeviceLocation,
            //            Marshal.OffsetOf(typeof(CommonData), "lsc_en").ToInt32(),
            //            BitConverter.GetBytes(IspProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Lsc].Value ?
            //            0x01 : 0x00),
            //            Marshal.SizeOf(typeof(char)));
            //        break;

            //    case "IsAwbEnable":
            //        DeviceApi.WriteAx327XSensorProperty(DeviceLocation,
            //            Marshal.OffsetOf(typeof(CommonData), "awb_en").ToInt32(),
            //            BitConverter.GetBytes(IspProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Awb].Value ?
            //            0x02 : 0x00),
            //            Marshal.SizeOf(typeof(char)));
            //        break;
            //    default:
            //        break;
            //}
        }

        public void WriteToFile(string filePath)
        {
            XmlDocument xmlDoc = new XmlDocument();
            XmlDeclaration dec = xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", null);
            xmlDoc.AppendChild(dec);

            XmlElement root = xmlDoc.CreateElement("IspToolData");
            XmlElement ispRoot = xmlDoc.CreateElement("Isp");
            XmlElement LcdRoot = xmlDoc.CreateElement("Lcd");

            root.AppendChild(ispRoot);
            root.AppendChild(LcdRoot);
            xmlDoc.AppendChild(root);

            #region Isp

            List<Isp.IspModule> SerializeIspModuleList;
            if (((IspToolApp)Application.Current).IsDevelopMode)
            {

                SerializeIspModuleList = Enum.GetValues(typeof(IspModule)).Cast<IspModule>().ToList();
            }
            else
            {
                SerializeIspModuleList = _serializeIspModuleListForUserMode;
            }

            var commonConfigNode = _ispProcessor.IspCommonConfig.SerializeCommonConfigToXmlNode(xmlDoc, SerializeIspModuleList);
            ispRoot.AppendChild(commonConfigNode);
            foreach (IspModule ispModule in SerializeIspModuleList)
            {
                if (!_ispProcessor.AllProcessSteps.Keys.Contains(ispModule))
                {
                    continue;
                }

                var xmlElement = _ispProcessor.AllProcessSteps[ispModule].SerializeToXmlElement(xmlDoc);
                ispRoot.AppendChild(xmlElement);
            }
            #endregion

            #region Lcd
            foreach (var item in LcdSetting.SettingSections)
            {
                if (item.Key == LcdSection.LcdCommon)
                {
                    continue;
                }

                var xmlElement = item.Value.SerializeToXmlElement(xmlDoc);
                LcdRoot.AppendChild(xmlElement);
            }

            #endregion

            xmlDoc.Save(filePath);
        }

        public void WriteToFile()
        {
            WriteToFile(FilePath);
        }

        public void ReadFromFile()
        {
            ReadFromFile(FilePath);
        }

        public void ReadFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be empty", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Configuration file not found: {filePath}");
            if (filePath.Length == 0)
            {
                return;
            }

            try
            {
                string xmlFileText = File.ReadAllText(filePath);

                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlFileText);

                var ispToolDataNode = doc["IspToolData"];
                if (ispToolDataNode == null)
                    throw new InvalidDataException($"Missing root node 'IspToolData' in {filePath}");

                #region Isp
                var ispPart = ispToolDataNode["Isp"];
                if (ispPart == null)
                    throw new InvalidDataException($"Missing node 'Isp' in {filePath}");

                List<Isp.IspModule> DeserializeIspModuleList;
                if (((IspToolApp)Application.Current).IsDevelopMode)
                {

                    DeserializeIspModuleList = Enum.GetValues(typeof(IspModule)).Cast<IspModule>().ToList();
                }
                else
                {
                    DeserializeIspModuleList = _serializeIspModuleListForUserMode;
                }

                _ispProcessor.IspCommonConfig.DeserializeFromXmlElement(ispPart, DeserializeIspModuleList);

                foreach (IspModule ispModule in DeserializeIspModuleList)
                {
                    if (!_ispProcessor.AllProcessSteps.Keys.Contains(ispModule))
                    {
                        continue;
                    }
                    _ispProcessor.AllProcessSteps[ispModule].DeserializeFromXmlElement(ispPart);
                }
                #endregion

                #region Lcd
                var lcdPart = ispToolDataNode["Lcd"];
                if (lcdPart != null)
                {
                    foreach (var item in LcdSetting.SettingSections)
                    {
                        if (item.Key == LcdSection.LcdCommon)
                        {
                            continue;
                        }

                        item.Value.DeserializeFromXmlElement(lcdPart);
                    }
                }

                #endregion
            }

            catch (XmlException ex)
            {
                throw new InvalidDataException($"Invalid XML format in {filePath}", ex);
            }
            catch (InvalidDataException)
            {
                throw; // Re-throw InvalidDataException as-is
            }
            catch (Exception ex)
            {
                string innerInfo = ex.InnerException != null ? $" InnerException: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}" : "";
                Logger.Error($"Failed to read configuration from {filePath}: {ex.Message}{innerInfo}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Logger.Error($"Inner stack trace: {ex.InnerException.StackTrace}");
                }
                throw new IOException($"Failed to read configuration from {filePath}: {ex.Message}{innerInfo}", ex);
            }
        }

        public void WriteIspModuleToDevice(IspModule ispModule)
        {
            if (!_ispProcessor.AllProcessSteps.Keys.Contains(ispModule))
            {
                return;
            }

            if (!_ispProcessor.AllProcessSteps[ispModule].HasChangedParams)
            {
                return;
            }

            var paramsDataCollection = _ispProcessor.AllProcessSteps[ispModule].ParamsDataCollection;
            foreach (var item in paramsDataCollection)
            {
                try
                {
                    Logger.Info($"Writing ISP {ispModule} parameters - Key: {item.Key}, Data Length: {item.Value.Length}");

                    int sentPos = 0;
                    while (sentPos < item.Value.Length)
                    {
                        var dataToSend = item.Value.Skip(sentPos).Take(512).ToArray();

                        int parameter = 0;
                        parameter = sentPos << 8 | (item.Key * IspBitWidth);

                        Logger.Info($"ISP Data - Module: {ispModule}, Key: {item.Key}, Position: {sentPos}, Send Length: {dataToSend.Length}, Parameter: 0x{parameter:X}");

                        if (dataToSend.Length > 0)
                        {
                            var sampleBytes = dataToSend.Take(16).Select(b => b.ToString("X2")).ToArray();
                            Logger.Info($"ISP Data Sample (first 16 bytes): [{string.Join(", ", sampleBytes)}]");
                        }

                        lock (DeviceWriteLock)
                        {
                            DeviceApi.WriteAx327XIspProperty(DeviceLocation, parameter, dataToSend, sizeof(byte) * dataToSend.Length);
                        }
                        sentPos += dataToSend.Length;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error writing {ispModule} parameters to device: {ex.Message}", ex);
                    throw new Exception($"写入{ispModule}参数失败: {ex.Message}", ex);
                }
            }

            _ispProcessor.AllProcessSteps[ispModule].HasChangedParams = false;
        }

        public void WriteToDevice()
        {
            #region Isp
            foreach (IspModule ispModule in Enum.GetValues(typeof(IspModule)))
            {
                WriteIspModuleToDevice(ispModule);
            }
            #endregion

            #region Lcd
            //if (LcdSetting.SettingSection[Lcd.LcdSection.LcdCommon].HasChangedParams)
            //{
            //    DeviceApi.WriteAx327XLcdProperty(DeviceLocation, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "lcd_common"),
            //        LcdSetting.SettingSection[Lcd.LcdSection.LcdCommon].ParamsData, LcdSetting.SettingSection[Lcd.LcdSection.LcdCommon].ParamsData.Length);
            //}

            if (LcdSetting.SettingSections[Lcd.LcdSection.LcdVde].HasChangedParams)
            {
                try
                {
                    Logger.Info($"Writing LCD VDE parameters - Data Length: {LcdSetting.SettingSections[Lcd.LcdSection.LcdVde].ParamsData.Length}");

                    // 打印前几个字节的数据内容
                    if (LcdSetting.SettingSections[Lcd.LcdSection.LcdVde].ParamsData.Length > 0)
                    {
                        var sampleBytes = LcdSetting.SettingSections[Lcd.LcdSection.LcdVde].ParamsData.Take(16).Select(b => b.ToString("X2")).ToArray();
                        Logger.Info($"LCD VDE Data Sample (first 16 bytes): [{string.Join(", ", sampleBytes)}]");
                    }

                    DeviceApi.WriteAx327XLcdProperty(DeviceLocation, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "lcd_vde"),
                        LcdSetting.SettingSections[Lcd.LcdSection.LcdVde].ParamsData, LcdSetting.SettingSections[Lcd.LcdSection.LcdVde].ParamsData.Length);
                    LcdSetting.SettingSections[Lcd.LcdSection.LcdVde].HasChangedParams = false;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error writing LCD VDE parameters to device: {ex.Message}", ex);
                    throw new Exception($"写入LCD VDE参数失败: {ex.Message}", ex);
                }
            }

            if (LcdSetting.SettingSections[Lcd.LcdSection.LcdGamma].HasChangedParams)
            {
                try
                {
                    Logger.Info($"Writing LCD Gamma parameters - Data Length: {LcdSetting.SettingSections[Lcd.LcdSection.LcdGamma].ParamsData.Length}");

                    // 打印前几个字节的数据内容
                    if (LcdSetting.SettingSections[Lcd.LcdSection.LcdGamma].ParamsData.Length > 0)
                    {
                        var sampleBytes = LcdSetting.SettingSections[Lcd.LcdSection.LcdGamma].ParamsData.Take(16).Select(b => b.ToString("X2")).ToArray();
                        Logger.Info($"LCD Gamma Data Sample (first 16 bytes): [{string.Join(", ", sampleBytes)}]");
                    }

                    DeviceApi.WriteAx327XLcdProperty(DeviceLocation, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "lcd_gamma"),
                        LcdSetting.SettingSections[Lcd.LcdSection.LcdGamma].ParamsData, LcdSetting.SettingSections[Lcd.LcdSection.LcdGamma].ParamsData.Length);
                    LcdSetting.SettingSections[Lcd.LcdSection.LcdGamma].HasChangedParams = false;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error writing LCD Gamma parameters to device: {ex.Message}", ex);
                    throw new Exception($"写入LCD Gamma参数失败: {ex.Message}", ex);
                }
            }

            if (LcdSetting.SettingSections[Lcd.LcdSection.LcdCcm].HasChangedParams)
            {
                try
                {
                    Logger.Info($"Writing LCD CCM parameters - Data Length: {LcdSetting.SettingSections[Lcd.LcdSection.LcdCcm].ParamsData.Length}");

                    // 打印前几个字节的数据内容
                    if (LcdSetting.SettingSections[Lcd.LcdSection.LcdCcm].ParamsData.Length > 0)
                    {
                        var sampleBytes = LcdSetting.SettingSections[Lcd.LcdSection.LcdCcm].ParamsData.Take(16).Select(b => b.ToString("X2")).ToArray();
                        Logger.Info($"LCD CCM Data Sample (first 16 bytes): [{string.Join(", ", sampleBytes)}]");
                    }

                    DeviceApi.WriteAx327XLcdProperty(DeviceLocation, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "de_ccm"),
                        LcdSetting.SettingSections[Lcd.LcdSection.LcdCcm].ParamsData, LcdSetting.SettingSections[Lcd.LcdSection.LcdCcm].ParamsData.Length);
                    LcdSetting.SettingSections[Lcd.LcdSection.LcdCcm].HasChangedParams = false;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error writing LCD CCM parameters to device: {ex.Message}", ex);
                    throw new Exception($"写入LCD CCM参数失败: {ex.Message}", ex);
                }
            }

            if (LcdSetting.SettingSections[Lcd.LcdSection.LcdSaj].HasChangedParams)
            {
                try
                {
                    Logger.Info($"Writing LCD SAJ parameters - Data Length: {LcdSetting.SettingSections[Lcd.LcdSection.LcdSaj].ParamsData.Length}");

                    // 打印前几个字节的数据内容
                    if (LcdSetting.SettingSections[Lcd.LcdSection.LcdSaj].ParamsData.Length > 0)
                    {
                        var sampleBytes = LcdSetting.SettingSections[Lcd.LcdSection.LcdSaj].ParamsData.Take(16).Select(b => b.ToString("X2")).ToArray();
                        Logger.Info($"LCD SAJ Data Sample (first 16 bytes): [{string.Join(", ", sampleBytes)}]");
                    }

                    DeviceApi.WriteAx327XLcdProperty(DeviceLocation, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "de_saj"),
                        LcdSetting.SettingSections[Lcd.LcdSection.LcdSaj].ParamsData, LcdSetting.SettingSections[Lcd.LcdSection.LcdSaj].ParamsData.Length);
                    LcdSetting.SettingSections[Lcd.LcdSection.LcdSaj].HasChangedParams = false;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error writing LCD SAJ parameters to device: {ex.Message}", ex);
                    throw new Exception($"写入LCD SAJ参数失败: {ex.Message}", ex);
                }
            }

            if (LcdSetting.SettingSections[Lcd.LcdSection.LcdLsawtooth].HasChangedParams)
            {
                try
                {
                    Logger.Info($"Writing LCD Lsawtooth parameters - Data Length: {LcdSetting.SettingSections[Lcd.LcdSection.LcdLsawtooth].ParamsData.Length}");

                    // 打印前几个字节的数据内容
                    if (LcdSetting.SettingSections[Lcd.LcdSection.LcdLsawtooth].ParamsData.Length > 0)
                    {
                        var sampleBytes = LcdSetting.SettingSections[Lcd.LcdSection.LcdLsawtooth].ParamsData.Take(16).Select(b => b.ToString("X2")).ToArray();
                        Logger.Info($"LCD Lsawtooth Data Sample (first 16 bytes): [{string.Join(", ", sampleBytes)}]");
                    }

                    DeviceApi.WriteAx327XLcdProperty(DeviceLocation, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "lcd_lsawtooth"),
                        LcdSetting.SettingSections[Lcd.LcdSection.LcdLsawtooth].ParamsData, LcdSetting.SettingSections[Lcd.LcdSection.LcdLsawtooth].ParamsData.Length);
                    LcdSetting.SettingSections[Lcd.LcdSection.LcdLsawtooth].HasChangedParams = false;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error writing LCD Lsawtooth parameters to device: {ex.Message}", ex);
                    throw new Exception($"写入LCD Lsawtooth参数失败: {ex.Message}", ex);
                }
            }

            #endregion
        }

        public void RefreshDataFromDevice()
        {
            #region Isp
            _ispProcessor.IspCommonConfig.PropertyChanged -= OnCommonConfigChange;
            #endregion

            ReadFromDevice();
        }

        /*
        public void ReadFromDevice()
        {
            if (_type != ConfigType.Online)
                return;

            #region Isp
            foreach (IspModule ispModule in Enum.GetValues(typeof(IspModule)))
            {
                if (!_ispProcessor.AllProcessSteps.Keys.Contains(ispModule))
                {
                    continue;
                }
                Logger.Info($"ReadFromDevice ISP {ispModule} parameters");

                var paramsDataCollection = _ispProcessor.AllProcessSteps[ispModule].ParamsDataCollection;
                int fullSize = sizeof(byte) * paramsDataCollection[_ispProcessor.AllProcessSteps[ispModule].DeviceModulePos].Length;
                byte[] fullBuffer = new byte[paramsDataCollection[_ispProcessor.AllProcessSteps[ispModule].DeviceModulePos].Length];
                try
                {
                    int readPos = 0;
                    while (readPos < fullSize)
                    {
                        int readSize = fullSize - readPos > 512 ? 512 : fullSize - readPos;
                        byte[] partialBuffer = new byte[readSize];
                        int parameter = 0;
                        parameter = readPos << 8 | (_ispProcessor.AllProcessSteps[ispModule].DeviceModulePos * IspBitWidth);

                        DeviceApi.ReadAx327XIspProperty(DeviceLocation, parameter, partialBuffer, readSize);

                        Buffer.BlockCopy(partialBuffer, 0, fullBuffer, readPos, readSize);
                        readPos += readSize;
                    }

                    _ispProcessor.AllProcessSteps[ispModule].ParamsDataCollection = new Dictionary<int, byte[]>(){
                        {_ispProcessor.AllProcessSteps[ispModule].DeviceModulePos, fullBuffer}
                    };

                    _ispProcessor.AllProcessSteps[ispModule].HasChangedParams = false;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error reading {ispModule} parameters from device: {ex.Message}", ex);
                    throw new Exception($"读取{ispModule}参数失败: {ex.Message}", ex);
                }
            }

            var commonConfigDataBuffer = new byte[Marshal.SizeOf(typeof(CommonData))];
            bool ok = DeviceApi.ReadAx327XSensorProperty(DeviceLocation, 0, commonConfigDataBuffer, commonConfigDataBuffer.Length);
            if (!ok)
            {
                Logger.Warn($"Failed to read common config from device: {DeviceLocation}");
            }

            _ispProcessor.IspCommonConfig.ParamsDataCollection = new Dictionary<int, byte[]>() { { 0, commonConfigDataBuffer } };

            _ispProcessor.IspCommonConfig.PropertyChanged += OnCommonConfigChange;
            #endregion

            #region Lcd

            byte[] lcdRecvBuffer = new byte[512];
            DeviceApi.ReadAx327XLcdProperty(DeviceLocation, lcdRecvBuffer);

            byte[] lcdCommonBuffer = new byte[Marshal.SizeOf(typeof(Lcd.lcd_common_t))];
            Buffer.BlockCopy(lcdRecvBuffer, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "lcd_common"), lcdCommonBuffer, 0, lcdCommonBuffer.Length);
            LcdSetting.SettingSections[Lcd.LcdSection.LcdCommon].ParamsData = lcdCommonBuffer;
            LcdSetting.SettingSections[Lcd.LcdSection.LcdCommon].HasChangedParams = false;


            byte[] lcdVdeBuffer = new byte[Marshal.SizeOf(typeof(Lcd.lcd_vde_t))];
            Buffer.BlockCopy(lcdRecvBuffer, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "lcd_vde"), lcdVdeBuffer, 0, lcdVdeBuffer.Length);
            LcdSetting.SettingSections[Lcd.LcdSection.LcdVde].ParamsData = lcdVdeBuffer;
            LcdSetting.SettingSections[Lcd.LcdSection.LcdVde].HasChangedParams = false;

            byte[] lcdGammaBuffer = new byte[Marshal.SizeOf(typeof(Lcd.lcd_gamma_t))];
            Buffer.BlockCopy(lcdRecvBuffer, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "lcd_gamma"), lcdGammaBuffer, 0, lcdGammaBuffer.Length);
            LcdSetting.SettingSections[Lcd.LcdSection.LcdGamma].ParamsData = lcdGammaBuffer;
            LcdSetting.SettingSections[Lcd.LcdSection.LcdGamma].HasChangedParams = false;

            FieldInfo ccmField = typeof(Lcd.usb_lcddev_t).GetField("de_ccm");
            object[] ccmAttributes = ccmField.GetCustomAttributes(typeof(MarshalAsAttribute), false);
            int CcmSizeConst = ((MarshalAsAttribute)ccmAttributes[0]).SizeConst;
            byte[] lcdCcmBuffer = new byte[CcmSizeConst * sizeof(int)];
            Buffer.BlockCopy(lcdRecvBuffer, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "de_ccm"), lcdCcmBuffer, 0, lcdCcmBuffer.Length);
            LcdSetting.SettingSections[Lcd.LcdSection.LcdCcm].ParamsData = lcdCcmBuffer;
            LcdSetting.SettingSections[Lcd.LcdSection.LcdCcm].HasChangedParams = false;

            FieldInfo sajField = typeof(Lcd.usb_lcddev_t).GetField("de_saj");
            object[] sajAttributes = sajField.GetCustomAttributes(typeof(MarshalAsAttribute), false);
            int sajSizeConst = ((MarshalAsAttribute)sajAttributes[0]).SizeConst;
            byte[] lcdSajBuffer = new byte[sajSizeConst * sizeof(int)];
            Buffer.BlockCopy(lcdRecvBuffer, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "de_saj"), lcdSajBuffer, 0, lcdSajBuffer.Length);
            LcdSetting.SettingSections[Lcd.LcdSection.LcdSaj].ParamsData = lcdSajBuffer;
            LcdSetting.SettingSections[Lcd.LcdSection.LcdSaj].HasChangedParams = false;

            var lcdLsawtoothModule = ((LcdLsawtooth)LcdSetting.SettingSections[Lcd.LcdSection.LcdLsawtooth]);

            lcdLsawtoothModule.SensorWidth = _ispProcessor.IspCommonConfig.ResolutionWidth;
            lcdLsawtoothModule.SensorHeight = _ispProcessor.IspCommonConfig.ResolutionHeight;

            lcdLsawtoothModule.LcdWidth = ((LcdCommon)LcdSetting.SettingSections[Lcd.LcdSection.LcdCommon]).screen_w;
            lcdLsawtoothModule.LcdHeight = ((LcdCommon)LcdSetting.SettingSections[Lcd.LcdSection.LcdCommon]).screen_h;
            lcdLsawtoothModule.RefreshAntiLsawtoothPresetData();

            byte[] lcdLsawtoothBuffer = new byte[Marshal.SizeOf(typeof(Lcd.lcd_lsawtooth_t))];
            Buffer.BlockCopy(lcdRecvBuffer, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "lcd_lsawtooth"), lcdLsawtoothBuffer, 0, lcdLsawtoothBuffer.Length);
            lcdLsawtoothModule.ParamsData = lcdLsawtoothBuffer;
            lcdLsawtoothModule.HasChangedParams = false;

            #endregion
        }
        */


        #region IDisposable

        private bool _disposed = false;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _ispProcessor.IspCommonConfig.PropertyChanged -= OnCommonConfigChange;
                Logger.Debug($"Config '{Name}' disposed.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error disposing config '{Name}': {ex.Message}");
            }
        }

        #endregion

        public void ReadIspModuleFromDevice(IspModule ispModule)
        {
            if (_type != ConfigType.Online)
                return;

            if (!_ispProcessor.AllProcessSteps.Keys.Contains(ispModule))
            {
                return;
            }
            Console.WriteLine($"ReadFromDevice ISP {ispModule} parameters");
            var paramsDataCollection = _ispProcessor.AllProcessSteps[ispModule].ParamsDataCollection;
            int fullSize = sizeof(byte) * paramsDataCollection[_ispProcessor.AllProcessSteps[ispModule].DeviceModulePos].Length;
            byte[] fullBuffer = new byte[paramsDataCollection[_ispProcessor.AllProcessSteps[ispModule].DeviceModulePos].Length];
            try
            {
                int readPos = 0;
                while (readPos < fullSize)
                {
                    int readSize = fullSize - readPos > 512 ? 512 : fullSize - readPos;
                    byte[] partialBuffer = new byte[readSize];
                    int parameter = 0;
                    parameter = readPos << 8 | (_ispProcessor.AllProcessSteps[ispModule].DeviceModulePos * IspBitWidth);

                    DeviceApi.ReadAx327XIspProperty(DeviceLocation, parameter, partialBuffer, readSize);

                    Buffer.BlockCopy(partialBuffer, 0, fullBuffer, readPos, readSize);
                    readPos += readSize;
                }

                _ispProcessor.AllProcessSteps[ispModule].ParamsDataCollection = new Dictionary<int, byte[]>(){
                    {_ispProcessor.AllProcessSteps[ispModule].DeviceModulePos, fullBuffer}
                };

                _ispProcessor.AllProcessSteps[ispModule].HasChangedParams = false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error reading {ispModule} parameters from device: {ex.Message}", ex);
                throw new Exception($"读取{ispModule}参数失败: {ex.Message}", ex);
            }
        }


        public void ReadIspCommonConfigFromDevice()
        {
            _ispProcessor.IspCommonConfig.PropertyChanged -= OnCommonConfigChange;

            var commonConfigDataBuffer = new byte[Marshal.SizeOf(typeof(CommonData))];
            bool ok = DeviceApi.ReadAx327XSensorProperty(DeviceLocation, 0, commonConfigDataBuffer, commonConfigDataBuffer.Length);
            if (!ok)
            {
                Logger.Warn($"Failed to read common config from device: {DeviceLocation}");
            }
            //Console.WriteLine("Read common config from device: " + DeviceLocation);
            _ispProcessor.IspCommonConfig.ParamsDataCollection = new Dictionary<int, byte[]>() { { 0, commonConfigDataBuffer } };

            _ispProcessor.IspCommonConfig.PropertyChanged += OnCommonConfigChange;
        }

        public void ReadFromDevice()
        {
            if (_type != ConfigType.Online)
                return;

            #region Isp
            foreach (IspModule ispModule in Enum.GetValues(typeof(IspModule)))
            {
                ReadIspModuleFromDevice(ispModule);
            }

            var commonConfigDataBuffer = new byte[Marshal.SizeOf(typeof(CommonData))];
            bool ok = DeviceApi.ReadAx327XSensorProperty(DeviceLocation, 0, commonConfigDataBuffer, commonConfigDataBuffer.Length);
            if (!ok)
            {
                Logger.Warn($"Failed to read common config from device: {DeviceLocation}");
            }

            _ispProcessor.IspCommonConfig.ParamsDataCollection = new Dictionary<int, byte[]>() { { 0, commonConfigDataBuffer } };

            _ispProcessor.IspCommonConfig.PropertyChanged += OnCommonConfigChange;

            #endregion

            #region Lcd

            byte[] lcdRecvBuffer = new byte[512];
            DeviceApi.ReadAx327XLcdProperty(DeviceLocation, lcdRecvBuffer);

            byte[] lcdCommonBuffer = new byte[Marshal.SizeOf(typeof(Lcd.lcd_common_t))];
            Buffer.BlockCopy(lcdRecvBuffer, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "lcd_common"), lcdCommonBuffer, 0, lcdCommonBuffer.Length);
            LcdSetting.SettingSections[Lcd.LcdSection.LcdCommon].ParamsData = lcdCommonBuffer;
            LcdSetting.SettingSections[Lcd.LcdSection.LcdCommon].HasChangedParams = false;


            byte[] lcdVdeBuffer = new byte[Marshal.SizeOf(typeof(Lcd.lcd_vde_t))];
            Buffer.BlockCopy(lcdRecvBuffer, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "lcd_vde"), lcdVdeBuffer, 0, lcdVdeBuffer.Length);
            LcdSetting.SettingSections[Lcd.LcdSection.LcdVde].ParamsData = lcdVdeBuffer;
            LcdSetting.SettingSections[Lcd.LcdSection.LcdVde].HasChangedParams = false;

            byte[] lcdGammaBuffer = new byte[Marshal.SizeOf(typeof(Lcd.lcd_gamma_t))];
            Buffer.BlockCopy(lcdRecvBuffer, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "lcd_gamma"), lcdGammaBuffer, 0, lcdGammaBuffer.Length);
            LcdSetting.SettingSections[Lcd.LcdSection.LcdGamma].ParamsData = lcdGammaBuffer;
            LcdSetting.SettingSections[Lcd.LcdSection.LcdGamma].HasChangedParams = false;

            FieldInfo ccmField = typeof(Lcd.usb_lcddev_t).GetField("de_ccm");
            object[] ccmAttributes = ccmField.GetCustomAttributes(typeof(MarshalAsAttribute), false);
            int CcmSizeConst = ((MarshalAsAttribute)ccmAttributes[0]).SizeConst;
            byte[] lcdCcmBuffer = new byte[CcmSizeConst * sizeof(int)];
            Buffer.BlockCopy(lcdRecvBuffer, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "de_ccm"), lcdCcmBuffer, 0, lcdCcmBuffer.Length);
            LcdSetting.SettingSections[Lcd.LcdSection.LcdCcm].ParamsData = lcdCcmBuffer;
            LcdSetting.SettingSections[Lcd.LcdSection.LcdCcm].HasChangedParams = false;

            FieldInfo sajField = typeof(Lcd.usb_lcddev_t).GetField("de_saj");
            object[] sajAttributes = sajField.GetCustomAttributes(typeof(MarshalAsAttribute), false);
            int sajSizeConst = ((MarshalAsAttribute)sajAttributes[0]).SizeConst;
            byte[] lcdSajBuffer = new byte[sajSizeConst * sizeof(int)];
            Buffer.BlockCopy(lcdRecvBuffer, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "de_saj"), lcdSajBuffer, 0, lcdSajBuffer.Length);
            LcdSetting.SettingSections[Lcd.LcdSection.LcdSaj].ParamsData = lcdSajBuffer;
            LcdSetting.SettingSections[Lcd.LcdSection.LcdSaj].HasChangedParams = false;

            var lcdLsawtoothModule = ((LcdLsawtooth)LcdSetting.SettingSections[Lcd.LcdSection.LcdLsawtooth]);

            lcdLsawtoothModule.SensorWidth = _ispProcessor.IspCommonConfig.ResolutionWidth;
            lcdLsawtoothModule.SensorHeight = _ispProcessor.IspCommonConfig.ResolutionHeight;

            lcdLsawtoothModule.LcdWidth = ((LcdCommon)LcdSetting.SettingSections[Lcd.LcdSection.LcdCommon]).screen_w;
            lcdLsawtoothModule.LcdHeight = ((LcdCommon)LcdSetting.SettingSections[Lcd.LcdSection.LcdCommon]).screen_h;
            lcdLsawtoothModule.RefreshAntiLsawtoothPresetData();

            byte[] lcdLsawtoothBuffer = new byte[Marshal.SizeOf(typeof(Lcd.lcd_lsawtooth_t))];
            Buffer.BlockCopy(lcdRecvBuffer, (int)Marshal.OffsetOf(typeof(Lcd.usb_lcddev_t), "lcd_lsawtooth"), lcdLsawtoothBuffer, 0, lcdLsawtoothBuffer.Length);
            lcdLsawtoothModule.ParamsData = lcdLsawtoothBuffer;
            lcdLsawtoothModule.HasChangedParams = false;

            #endregion
        }

    }
}
