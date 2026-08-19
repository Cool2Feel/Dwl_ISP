using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ThunderSE.Common;
using ThunderSE.DeviceConfig;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Uvc;

namespace ThunderSE.Ui.MainWindow
{
    public class IICRegisterItem : ViewModelBase
    {
        private ushort _address = 0;
        private ushort _data = 0;

        public int Index { get; set; }

        public ushort Address
        {
            get { return _address; }
            set
            {
                if (value > 0xFFFF) throw new ArgumentOutOfRangeException("地址范围: 0x0000-0xFFFF");
                _address = value;
                RaisePropertyChanged("Address");
                RaisePropertyChanged("AddressHex");
                RaisePropertyChanged("PackedBytes");
                RaisePropertyChanged("PackedBytesCount");
                RaisePropertyChanged("PackedHex");
            }
        }

        public ushort Data
        {
            get { return _data; }
            set
            {
                if (value > 0xFFFF) throw new ArgumentOutOfRangeException("数据范围: 0x0000-0xFFFF");
                _data = value;
                RaisePropertyChanged("Data");
                RaisePropertyChanged("DataHex");
                RaisePropertyChanged("PackedBytes");
                RaisePropertyChanged("PackedBytesCount");
                RaisePropertyChanged("PackedHex");
            }
        }

        public string AddressHex
        {
            get
            {
                if (_address <= 0xFF)
                    return "0x" + _address.ToString("X2");
                else
                    return "0x" + _address.ToString("X4");
            }
        }

        public string DataHex
        {
            get { return "0x" + _data.ToString("X2"); }
        }

        public byte[] PackedBytes
        {
            get
            {
                List<byte> bytes = new List<byte>();

                if (_address <= 0xFF)
                {
                    bytes.Add((byte)(_address & 0xFF));
                }
                else
                {
                    bytes.Add((byte)((_address >> 8) & 0xFF));
                    bytes.Add((byte)(_address & 0xFF));
                }

                bytes.Add((byte)(_data & 0xFF));

                return bytes.ToArray();
            }
        }

        public int PackedBytesCount
        {
            get { return PackedBytes.Length; }
        }

        public string PackedHex
        {
            get
            {
                var hexStrings = PackedBytes.Select(b => "0x" + b.ToString("X2"));
                return string.Join(", ", hexStrings);
            }
        }
    }
    public class NavigateToMsg
    {
        private IspModule _module;

        public NavigateToMsg(IspModule Module)
        {
            _module = Module;
        }

        public IspModule Module
        {
            get { return _module; }
        }
    }

    public partial class DeviceConfigPageViewModel : ViewModelBase, ICleanup
    {
        #region fields
        private IspModule? _currentNavigatingModule = null;
        private Processor _ispProcessor = null;
        private CommonConfig _commonConfig = null;
        private AE _aeStep = null;
        private BlackLevel _blackLevelStep = null;
        private LensShading _lenShadingStep = null;
        private DDC _ddcStep = null;
        private AutoWhiteBalance _awbStep = null;
        private CCM _ccmStep = null;
        private YGamma _yGammaStep = null;
        private CH _chStep = null;
        private EE _eeStep = null;
        private SAJ _sajStep = null;
        private GainLevel _gainLevelStep = null;

        private bool _isflip = true;
        private FlipMode _flipMode = FlipMode.None;

        private RelayCommand _writeConfigCommand = null;
        private RelayCommand _reloadConfigCommand = null;
        private RelayCommand _saveConfigAsCommand = null;
        private RelayCommand _loadConfigCommand = null;
        private RelayCommand _setRawScaleCommand = null;

        private Dictionary<string, int> _bayerPatterns = new Dictionary<string, int>() {
            {"RGRG", 0},
            {"GRGR", 1},
            {"BGBG", 2},
            {"GBGB", 3}
        };

        private DispatcherTimer _realTimeUpdateTimer; // 统一的实时更新定时器
        private HashSet<IspModule> _pendingModulesToUpdate = new HashSet<IspModule>(); // 待更新的模块集合
        private DispatcherTimer _curGainUpdateTimer; // CurGain定时更新定时器
        private DateTime _lastRawScaleApplyTime = DateTime.MinValue; // 上次应用降采样的时间戳（用于节流）

        private Uvc.UvcDeviceInstance _uvcDevice;
        private CancellationTokenSource _reconnectCts; // 用于取消之前的重连任�?
        private readonly object _reconnectLock = new object(); // 重连操作�?

        private double _uvcFps = 0.0;
        private string defaultFileName = "";
        #endregion

        public DeviceConfigPageViewModel(Config deviceConfig)
        {
            DeviceConfig = deviceConfig;
            _ispProcessor = DeviceConfig.IspProcessor;

            _commonConfig = _ispProcessor.IspCommonConfig;
            _commonConfig.PropertyChanged += OnCommonConfigChange;
            _commonConfig.ProcessorStepsEnables.CollectionChanged += OnStepsEnablesChanged;


            _aeStep = ((AE)_ispProcessor.AllProcessSteps[IspModule.AE]);
            _aeStep.PropertyChanged += OnAEConfigChange;

            _blackLevelStep = ((BlackLevel)_ispProcessor.AllProcessSteps[IspModule.Blc]);
            _blackLevelStep.PropertyChanged += OnBlcConfigChange;

            _lenShadingStep = ((LensShading)_ispProcessor.AllProcessSteps[IspModule.Lsc]);
            _lenShadingStep.PropertyChanged += OnLscConfigChange;

            _ddcStep = ((DDC)_ispProcessor.AllProcessSteps[IspModule.Ddc]);
            _ddcStep.PropertyChanged += OnDdcConfigChange;

            _awbStep = ((AutoWhiteBalance)_ispProcessor.AllProcessSteps[IspModule.Awb]);
            _awbStep.PropertyChanged += OnAwbPropertyChanged;

            _ccmStep = ((CCM)_ispProcessor.AllProcessSteps[IspModule.Ccm]);
            _ccmStep.PropertyChanged += OnCcmPropertyChanged;

            _yGammaStep = ((YGamma)_ispProcessor.AllProcessSteps[IspModule.YGamma]);
            _yGammaStep.PropertyChanged += OnYGammaStepPropertyChanged;

            _eeStep = ((EE)_ispProcessor.AllProcessSteps[IspModule.Ee]);
            _eeStep.PropertyChanged += OnEEPropertyChanged;

            _chStep = ((CH)_ispProcessor.AllProcessSteps[IspModule.Ch]);
            _chStep.PropertyChanged += OnCHPropertyChanged;

            _sajStep = ((SAJ)_ispProcessor.AllProcessSteps[IspModule.Saj]);
            _sajStep.PropertyChanged += OnSAJPropertyChanged;

            _gainLevelStep = ((GainLevel)_ispProcessor.AllProcessSteps[IspModule.GainLevel]);
            _gainLevelStep.PropertyChanged += OnGainLevelPropertyChanged;

            _writeConfigCommand = new RelayCommand(WriteConfig);
            _reloadConfigCommand = new RelayCommand(ReloadConfig);
            _saveConfigAsCommand = new RelayCommand(SaveConfigAs);
            _loadConfigCommand = new RelayCommand(LoadConfigFromFile);
            _setRawScaleCommand = new RelayCommand(SetRawScale);

            MessengerInstance.Register<NavigateToMsg>(this, DeviceConfig.Name, OnNavigateTo);

            string configDir = Path.Combine(Directory.GetCurrentDirectory(), "Configs");
            Directory.CreateDirectory(configDir);

            // 使用设备名称生成文件�?
            string deviceName = DeviceConfig.Name ?? "OnlineDevice";
            string safeDeviceName = string.Join("_", deviceName.Split(Path.GetInvalidFileNameChars()));
            string timestamp = DateTime.Now.ToString("yyyyMMdd");
            defaultFileName = Path.Combine(configDir, $"{safeDeviceName}_{timestamp}.xml");

            SyncScaleDownFromConfig();

            // 初始化统一的实时更新定时器
            _realTimeUpdateTimer = new DispatcherTimer();
            _realTimeUpdateTimer.Interval = TimeSpan.FromMilliseconds(200); // 200ms 防抖延迟
            _realTimeUpdateTimer.Tick += OnRealTimeUpdateTimerTick;

            // 初始化CurGain定时更新定时�?
            _curGainUpdateTimer = new DispatcherTimer();
            _curGainUpdateTimer.Interval = TimeSpan.FromSeconds(5);
            _curGainUpdateTimer.Tick += OnCurGainUpdateTimerTick;
            _curGainUpdateTimer.Start();

            //BindToDevice();
        }

        private void BindToDevice()
        {
            if (DeviceConfig.Type == Config.ConfigType.Online)
            {
                string deviceKey = $"{DeviceConfig.Name}_{DeviceConfig.DeviceLocation}";
                _uvcDevice = Uvc.MultiUvcManager.Instance.GetDevice(deviceKey);
                DeviceName = _uvcDevice != null ? _uvcDevice.DeviceKey : "设备连接失败";
            }
        }

        public bool IsAwbSetWindow = false;

        public string DeviceName
        {
            get;
            set;
        }

        public Config DeviceConfig
        {
            get;
            set;
        }

        public RelayCommand SaveConfigAsCommand
        {
            get { return _saveConfigAsCommand; }
            set { _saveConfigAsCommand = value; }
        }

        public RelayCommand LoadConfigCommand
        {
            get { return _loadConfigCommand; }
            set { _loadConfigCommand = value; }
        }

        public RelayCommand WriteConfigCommand
        {
            get { return _writeConfigCommand; }
            set { _writeConfigCommand = value; }
        }

        public RelayCommand SetRawScaleCommand
        {
            get { return _setRawScaleCommand; }
            set { _setRawScaleCommand = value; }
        }

        //public RelayCommand ReloadConfigCommand
        //{
        //    get { return _reloadConfigCommand; }
        //    set { _reloadConfigCommand = value; }
        //}

        // 修改ReloadConfigCommand以支持异步操作和禁用状�?
        public RelayCommand ReloadConfigCommand
        {
            get
            {
                return _reloadConfigCommand ?? (_reloadConfigCommand = new RelayCommand(
                    ReloadConfig, () => !IsReloadingConfig && DeviceConfig != null)); // 当正在重载或设备未连接时禁用命令
            }
        }

        #region Properties for UI Feedback

        private bool _isReloadingConfig = false;
        public bool IsReloadingConfig
        {
            get { return _isReloadingConfig; }
            set
            {
                _isReloadingConfig = value;
                RaisePropertyChanged("IsReloadingConfig");
                // 更新命令状�?
                if (_reloadConfigCommand != null)
                    _reloadConfigCommand.RaiseCanExecuteChanged();
            }
        }

        private string _reloadStatusMessage = "";
        public string ReloadStatusMessage
        {
            get { return _reloadStatusMessage; }
            set
            {
                _reloadStatusMessage = value;
                RaisePropertyChanged("ReloadStatusMessage");
            }
        }
        #endregion

        #region misc
        public IspModule? CurrentNavigatingModule
        {
            get { return _currentNavigatingModule; }
            set
            {
                _currentNavigatingModule = value;
                RaisePropertyChanged("CurrentNavigatingModule");
            }
        }

        private void OnNavigateTo(NavigateToMsg msg)
        {
            CurrentNavigatingModule = msg.Module;
        }
        #endregion

        #region Common

        public bool IsOnline
        {
            get { return DeviceConfig.Type == Config.ConfigType.Online; }
        }

        public bool IsOffline
        {
            get { return DeviceConfig.Type == Config.ConfigType.Offline; }
        }

        public Dictionary<string, int> BayerPatterns
        {
            get { return _bayerPatterns; }
        }

        public int SelectedBayerPattern
        {
            get { return (int)_ispProcessor.IspCommonConfig.Bayer; }
            set { _ispProcessor.IspCommonConfig.Bayer = (DeviceConfig.Isp.BayerMode)value; }
        }

        public int ResolutionWidth
        {
            get
            {
                int _width = _ispProcessor.IspCommonConfig.ResolutionWidth;
                UvcReceiver.Instance.VideoWidth = _width;
                return _width;
            }
            set { _ispProcessor.IspCommonConfig.ResolutionWidth = value; }
        }

        public int ResolutionHeight
        {
            get
            {
                int _height = _ispProcessor.IspCommonConfig.ResolutionHeight;
                UvcReceiver.Instance.VideoHeight = _height;
                return _height;
            }
            set { _ispProcessor.IspCommonConfig.ResolutionHeight = value; }
        }

        public int ExpGain
        {
            get { return _ispProcessor.IspCommonConfig.ExpGain; }
            set
            {
                _ispProcessor.IspCommonConfig.ExpGain = value;

            }
        }

        public int IsPclkFirEn
        {
            get { return _ispProcessor.IspCommonConfig.IsPclkFirEn; }
            set { _ispProcessor.IspCommonConfig.IsPclkFirEn = value; }
        }

        public byte PclkFirClass
        {
            get { return _ispProcessor.IspCommonConfig.PclkFirClass; }
            set { _ispProcessor.IspCommonConfig.PclkFirClass = value; }
        }

        public bool IsPclkInvEn
        {
            get { return _ispProcessor.IspCommonConfig.IsPclkInvEn; }
            set { _ispProcessor.IspCommonConfig.IsPclkInvEn = value; }
        }

        public byte CsiTun
        {
            get { return _ispProcessor.IspCommonConfig.CsiTun; }
            set { _ispProcessor.IspCommonConfig.CsiTun = value; }
        }

        public byte Hsyn
        {
            get { return _ispProcessor.IspCommonConfig.Hsyn; }
            set { _ispProcessor.IspCommonConfig.Hsyn = value; }
        }

        public byte Vsyn
        {
            get { return _ispProcessor.IspCommonConfig.Vsyn; }
            set { _ispProcessor.IspCommonConfig.Vsyn = value; }
        }

        public int Mclk
        {
            get { return _ispProcessor.IspCommonConfig.Mclk; }
            set { _ispProcessor.IspCommonConfig.Mclk = value; }
        }

        public byte Rotate
        {
            get { return _ispProcessor.IspCommonConfig.Rotate; }
            set { _ispProcessor.IspCommonConfig.Rotate = value; }
        }

        public byte AVDD
        {
            get { return _ispProcessor.IspCommonConfig.AVDD; }
            set { _ispProcessor.IspCommonConfig.AVDD = value; }
        }

        public byte DVDD
        {
            get { return _ispProcessor.IspCommonConfig.DVDD; }
            set { _ispProcessor.IspCommonConfig.DVDD = value; }
        }

        public byte VDDIO
        {
            get { return _ispProcessor.IspCommonConfig.VDDIO; }
            set { _ispProcessor.IspCommonConfig.VDDIO = value; }
        }

        public int Vlen
        {
            get { return _ispProcessor.IspCommonConfig.Vlen; }
            set { _ispProcessor.IspCommonConfig.Vlen = value; }
        }

        public int DownFpsMode
        {
            get { return _ispProcessor.IspCommonConfig.DownFpsMode; }
            set { _ispProcessor.IspCommonConfig.DownFpsMode = value; }
        }

        public byte Fps
        {
            get { return _ispProcessor.IspCommonConfig.Fps; }
            set { _ispProcessor.IspCommonConfig.Fps = value; }
        }

        public byte Frequency
        {
            get { return _ispProcessor.IspCommonConfig.Frequency; }
            set { _ispProcessor.IspCommonConfig.Frequency = value; }
        }

        public int Pclk
        {
            get { return _ispProcessor.IspCommonConfig.Pclk; }
            set { _ispProcessor.IspCommonConfig.Pclk = value; }
        }

        public string Name
        {
            get { return _ispProcessor.IspCommonConfig.Name; }
            set { _ispProcessor.IspCommonConfig.Name = value; }
        }

        public int Id
        {
            get { return _ispProcessor.IspCommonConfig.Id; }
            set { _ispProcessor.IspCommonConfig.Id = value; }
        }

        public byte Type
        {
            get { return _ispProcessor.IspCommonConfig.Type; }
            set { _ispProcessor.IspCommonConfig.Type = value; }
        }

        /// <summary>
        /// 输出格式模式（RAW/MJPG/YUV�?
        /// </summary>
        public DeviceConfig.Isp.SetMode SetMode
        {
            get { return _ispProcessor.IspCommonConfig.SetMode; }
            set
            {
                _ispProcessor.IspCommonConfig.SetMode = value;
                if (value == SetMode.MJPG)
                {
                    IsFlip = false;
                    FlipImage = FlipMode.None;
                }
                else
                    IsFlip = true;
                //if (SelectedRawScaleValue > 0 && IsFlip)
                //{
                //    Task.Run(async () => {
                //        await UvcReceiver.Instance.Reconnect(DeviceConfig.UvcInterface);
                //    });
                //}
                bool isset = false;
                if (value == SetMode.RAW8 || value == SetMode.RAW10)
                {
                    var minScale = GetMinRawScaleValue(value, ResolutionHeight);
                    if (SelectedRawScaleValue < minScale)
                    {
                        SelectedRawScaleValue = minScale;
                        //SetRawScale();
                        isset = true;
                    }
                }
                UvcApi.SetRawFrameMode((int)value);
                Thread.Sleep(200);
                if (isset)
                    SetRawScale();
                RaisePropertyChanged("SetMode");
            }
        }

        public bool IsFlip
        {
            get { return _isflip; }
            set { _isflip = value; RaisePropertyChanged("IsFlip"); }
        }

        public FlipMode FlipImage
        {
            get { return _flipMode; }
            set
            {
                _flipMode = value;
                UvcReceiver.Instance.FlipImage = (int)value;
                RaisePropertyChanged("FlipImage");
            }
        }

        public int TurnGain
        {
            get { return _ispProcessor.IspCommonConfig.TurnGain; }
            set { _ispProcessor.IspCommonConfig.TurnGain = value; }
        }

        public int TurnExp
        {
            get { return _ispProcessor.IspCommonConfig.TurnExp; }
            set { _ispProcessor.IspCommonConfig.TurnExp = value; }
        }

        public bool IsExpGainEnable
        {
            get { return _ispProcessor.IspCommonConfig.IsExpGainEnable; }
            set
            {
                _ispProcessor.IspCommonConfig.IsExpGainEnable = value;
                RaisePropertyChanged("IsExpGainEnable");
            }
        }

        public byte IICInfor
        {
            get { return _ispProcessor.IspCommonConfig.IICInfor; }
            //set { _ispProcessor.IspCommonConfig.IICInfor = value; }
        }

        public CommonConfig CommonConfig
        {
            get { return _ispProcessor.IspCommonConfig; }
        }

        // IIC properties moved to DeviceConfigPageViewModel.I2C.cs

        public byte MipiRxClkDiv
        {
            get { return _ispProcessor.IspCommonConfig.MipiRxClkDiv; }
            set { _ispProcessor.IspCommonConfig.MipiRxClkDiv = value; }
        }
        public short MipiDRperlane
        {
            get { return _ispProcessor.IspCommonConfig.MipiDRperlane; }
            set { _ispProcessor.IspCommonConfig.MipiDRperlane = value; }
        }
        public int MipiHbpTime
        {
            get { return _ispProcessor.IspCommonConfig.MipiHbpTime; }
            set { _ispProcessor.IspCommonConfig.MipiHbpTime = value; }
        }
        public int MipiHsaTime
        {
            get { return _ispProcessor.IspCommonConfig.MipiHsaTime; }
            set { _ispProcessor.IspCommonConfig.MipiHsaTime = value; }
        }

        public int IcVer
        {
            get { return _ispProcessor.IspCommonConfig.IcVer; }
            //set { _ispProcessor.IspCommonConfig.IcVersion = value; }
        }

        public int IspVer
        {
            get { return _ispProcessor.IspCommonConfig.ISPVer; }
            //set { _ispProcessor.IspCommonConfig.ISPVer = value; }
        }

        public bool IsIcVerValid
        {
            get
            {
                return false;
                //return _ispProcessor.IspCommonConfig.IcVer > 0x526; 
            }
        }


        public int CurGain
        {
            get { return _ispProcessor.IspCommonConfig.CurGain; }
            //set { _ispProcessor.IspCommonConfig.CurGain = value; }
        }

        public byte RawScaleDown
        {
            get
            {
                return _ispProcessor.IspCommonConfig.RawScaleDown;
            }
            set { _ispProcessor.IspCommonConfig.RawScaleDown = value; }
        }

        public byte RawColScaleDown
        {
            get
            {
                return _ispProcessor.IspCommonConfig.RawColScaleDown;
            }
            set { _ispProcessor.IspCommonConfig.RawColScaleDown = value; }
        }

        private byte[] _rawScaleDown = { 0, 1, 2, 3, 4, 5, 6 };

        public ObservableCollection<string> RawScaleValues
        {
            get
            {
                var collection = new ObservableCollection<string>();
                if (_rawScaleDown != null)
                {
                    foreach (int value in _rawScaleDown)
                    {
                        collection.Add(value.ToString());
                    }
                }
                return collection;
            }
        }

        private int _selectedRawScaleValue = 0;
        public int SelectedRawScaleValue
        {
            get { return _selectedRawScaleValue; }
            set
            {
                if (_selectedRawScaleValue != value)
                {
                    _selectedRawScaleValue = value;
                    //Logger.Info($"SelectedRawScaleValue changed to: {_selectedRawScaleValue}");
                    RaisePropertyChanged("SelectedRawScaleValue");
                }
            }
        }

        private int _selectedColRawScaleValue = 0;
        public int SelectedColRawScaleValue
        {
            get { return _selectedColRawScaleValue; }
            set
            {
                if (_selectedColRawScaleValue != value)
                {
                    _selectedColRawScaleValue = value;
                    //Logger.Info($"SelectedColRawScaleValue changed to {_selectedColRawScaleValue}");
                    RaisePropertyChanged("SelectedColRawScaleValue");
                }
            }
        }

        private void SyncScaleDownFromConfig()
        {
            if (_rawScaleDown == null)
            {
                return;
            }

            byte rowScale = (byte)(RawScaleDown & 0x0F);
            byte colScale = (byte)((RawScaleDown >> 4) & 0x0F);

            for (int i = 0; i < _rawScaleDown.Length; i++)
            {
                if (_rawScaleDown[i] == rowScale)
                {
                    SelectedRawScaleValue = i;
                }
                if (_rawScaleDown[i] == colScale)
                {
                    SelectedColRawScaleValue = i;
                }
            }
        }

        /// <summary>
        /// 根据分辨率和模式获取 RawScale 最小值
        /// </summary>
        private int GetMinRawScaleValue(DeviceConfig.Isp.SetMode mode, int resolutionHeight)
        {
            if (resolutionHeight == 720)
            {
                return mode == SetMode.RAW8 ? 1 : 2;
            }
            if (resolutionHeight == 1080)
            {
                if (_uvcFps > 0 && _uvcFps <= 15)
                    return mode == SetMode.RAW8 ? 1 : 2;
                else if (_uvcFps > 15 && _uvcFps <= 20)
                    return mode == SetMode.RAW8 ? 2 : 4;
                else
                    return mode == SetMode.RAW8 ? 3 : 6;
            }
            if (resolutionHeight > 1080)
            {
                return mode == SetMode.RAW8 ? 3 : 6;
            }
            return 0;
        }

        private void OnCurGainUpdateTimerTick(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                if (IsOnline)
                {
                    try
                    {
                        DeviceConfig.ReadIspCommonConfigFromDevice();

                        DeviceConfig.WriteToFile(defaultFileName);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to auto-save config to file: {ex.Message}");
                    }
                }
            });
            //Console.WriteLine("CurGain updated: " + CurGain);
            //Debug.WriteLine("UvcFps updated: " + UvcReceiver.Instance.UvcFps);
            _uvcFps = UvcReceiver.Instance.UvcFps;
            RaisePropertyChanged(nameof(CurGain));
        }

        #endregion

        #region AE
        public EXP ExpAdapt
        {
            get { return _aeStep.ExpAdapt; }
            set
            {
                _aeStep.ExpAdapt = value;
            }
        }

        public HGRM HgrmAdapt
        {
            get { return _aeStep.HgrmAdapt; }
            set
            {
                _aeStep.HgrmAdapt = value;
            }
        }

        public int ExpTarget
        {
            get
            {
                var tmp = ExpAdapt.exp_tag[_selectedGainLevelValue];//Select(x => Convert.ToInt32(x)).ToArray();
                return Convert.ToInt32(tmp);
            }
            set
            {
                //byte tmp = Convert.ToByte(value);
                byte[] tmp = ExpAdapt.exp_tag.Select(x => Convert.ToByte(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToByte(value);
                ExpAdapt.exp_tag = tmp;
            }
        }


        /// <summary>
        /// 将AE数据格式化为C结构体格�?
        /// </summary>
        public string FormatAEDataToC()
        {
            // 格式化EXP数据
            string expTagStr = "{" + string.Join(",", ExpAdapt.exp_tag.Select(b => b.ToString()).ToArray()) + "}";
            // 按照要求的格式，需要将某些值转换为十六进制
            string expAdaptStr = $"{{{expTagStr},{ExpAdapt.exp_adj},{ConvertToHex(ExpAdapt.dark_weight)},{ConvertToHex(ExpAdapt.light_weight)},{ExpAdapt.exp_min},{ConvertToHex(ExpAdapt.gain_max)},{ConvertToHex(ExpAdapt.exp_nums)}}}";

            // 格式化HGRM数据
            string hgrmCentreWeightStr = "{" + string.Join(",", HgrmAdapt.hgrm_centre_weight.Select(i => i.ToString()).ToArray()) + "}";
            string hgrmGrayWeightStr = "{" + string.Join(",", HgrmAdapt.hgrm_gray_weight.Select(i => i.ToString()).ToArray()) + "}";
            // 根据示例，HGRM中的一些值应该用十六进制表示
            string hgrmAdaptStr = $"{{{HgrmAdapt.ae_win_x0},{HgrmAdapt.ae_win_x1},{HgrmAdapt.ae_win_x2},{HgrmAdapt.ae_win_x3},{HgrmAdapt.ae_win_y0},{HgrmAdapt.ae_win_y1},{HgrmAdapt.ae_win_y2},{HgrmAdapt.ae_win_y3},{ConvertToHexOrExpression(HgrmAdapt.weight_0_7)},{ConvertToHexOrExpression(HgrmAdapt.weight_8_15)},{ConvertToHexOrExpression(HgrmAdapt.weight_16_23)},{ConvertToHexOrExpression(HgrmAdapt.weight_24)}}}";

            // 合并整个AE结构
            return $".ae_adapt = {{.exp_adapt = {expAdaptStr},.hgrm_adapt = {hgrmAdaptStr}}},";
        }

        /// <summary>
        /// 将EXP数据格式化为C结构体格�?
        /// </summary>
        public string FormatEXPDataToC()
        {
            string expTagStr = "{" + string.Join(",", ExpAdapt.exp_tag.Select(b => b.ToString()).ToArray()) + "}";
            return $"{{ {expTagStr},{ExpAdapt.ylog_cal_fnum},{ExpAdapt.exp_ext_mod},{ExpAdapt.exp_adj},{ExpAdapt.dark_weight},{ExpAdapt.light_weight},{ExpAdapt.exp_min},{ExpAdapt.gain_max},{ExpAdapt.exp_nums} }}";
        }

        /// <summary>
        /// 将HGRM数据格式化为C结构体格�?
        /// </summary>
        public string FormatHGRMDataToC()
        {
            string hgrmCentreWeightStr = "{" + string.Join(",", HgrmAdapt.hgrm_centre_weight.Select(i => i.ToString()).ToArray()) + "}";
            string hgrmGrayWeightStr = "{" + string.Join(",", HgrmAdapt.hgrm_gray_weight.Select(i => i.ToString()).ToArray()) + "}";
            return $"{{ {HgrmAdapt.allow_miss_dots},{HgrmAdapt.ae_win_x0},{HgrmAdapt.ae_win_x1},{HgrmAdapt.ae_win_x2},{HgrmAdapt.ae_win_x3},{HgrmAdapt.ae_win_y0},{HgrmAdapt.ae_win_y1},{HgrmAdapt.ae_win_y2},{HgrmAdapt.ae_win_y3},{HgrmAdapt.weight_0_7},{HgrmAdapt.weight_8_15},{HgrmAdapt.weight_16_23},{HgrmAdapt.weight_24},{hgrmCentreWeightStr},{hgrmGrayWeightStr} }}";
        }

        /// <summary>
        /// 将数值转换为十六进制或其他特殊格�?
        /// </summary>
        /// <param name="value">输入�?/param>
        /// <returns>格式化后的字符串</returns>
        private string ConvertToHexOrExpression(int value)
        {
            // 检查是否为常见�?024的倍数，如果是则输出乘法表达式
            if (value % 1024 == 0 && value != 0)
            {
                int factor = value / 1024;
                return $"{factor}*1024";
            }
            // 特定值转换为十六进制表示
            if (value > 255) // 大于255的值通常用十六进制表�?
            {
                return $"0x{value:X}";
            }
            return value.ToString();
        }

        /// <summary>
        /// 将数值转换为十六进制格式
        /// </summary>
        /// <param name="value">输入�?/param>
        /// <returns>十六进制字符�?/returns>
        private string ConvertToHex(int value)
        {
            return $"0x{value:X}";
        }

        #endregion

        #region Blc
        public short BlcR
        {
            get { return _blackLevelStep.R; }
            set { _blackLevelStep.R = value; }
        }

        public short BlcGr
        {
            get { return _blackLevelStep.Gr; }
            set { _blackLevelStep.Gr = value; }
        }

        public short BlcGb
        {
            get { return _blackLevelStep.Gb; }
            set { _blackLevelStep.Gb = value; }
        }

        public short BlcB
        {
            get { return _blackLevelStep.B; }
            set { _blackLevelStep.B = value; }
        }

        public byte[] Blc_rate
        {
            get { return _blackLevelStep.Blk_Rate; }
            set
            {
                _blackLevelStep.Blk_Rate = value;
                Blc_Target = _blackLevelStep.Blk_Rate[_selectedGainLevelValue];

                RaisePropertyChanged("Blc_Target");
            }
        }

        public int Blc_Target
        {
            get
            {
                var tmp = _blackLevelStep.Blk_Rate[_selectedGainLevelValue];
                return Convert.ToInt32(tmp);
            }
            set
            {
                //int tmp = Convert.ToInt32(value);
                //_ddcStep.d_th_rate[_selectedGainLevelValue] = tmp;
                byte[] tmp = _blackLevelStep.Blk_Rate.Select(x => Convert.ToByte(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToByte(value);
                _blackLevelStep.Blk_Rate = tmp;
                RaisePropertyChanged("Blc_Target");
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Blc);
                //}
            }
        }

        /// <summary>
        /// 将BLC数据格式化为C结构体格�?
        /// </summary>
        public string FormatBLCDataToC()
        {
            string blcValuesStr = "{" + string.Join(",", new[] { BlcR, BlcGr, BlcGb, BlcB }.Select(v => v.ToString()).ToArray());
            string blcRateStr = "{" + string.Join(",", Blc_rate.Select(b => b.ToString()).ToArray()) + "}}";
            return $".blc_adapt = {blcValuesStr},{blcRateStr},";
        }

        #endregion

        #region Lsc
        public short[] LscCorrectionData
        {
            get { return _lenShadingStep.CorrectionData; }
            set { _lenShadingStep.CorrectionData = value; }
        }
        #endregion

        #region Ddc
        public int hot_num
        {
            get { return _ddcStep.hot_num; }
            set { _ddcStep.hot_num = value; }
        }

        public int dead_num
        {
            get { return _ddcStep.dead_num; }
            set { _ddcStep.dead_num = value; }
        }

        public int hot_th
        {
            get { return _ddcStep.hot_th; }
            set { _ddcStep.hot_th = value; }
        }

        public int dead_th
        {
            get { return _ddcStep.dead_th; }
            set { _ddcStep.dead_th = value; }
        }
        public int avg_th
        {
            get { return _ddcStep.avg_th; }
            set { _ddcStep.avg_th = value; }
        }

        public int[] d_th_rate
        {
            get { return _ddcStep.d_th_rate; }
            set
            {
                _ddcStep.d_th_rate = value;
                D_th_Target = _ddcStep.d_th_rate[_selectedGainLevelValue];

                RaisePropertyChanged("D_th_Target");
            }
        }

        public int[] h_th_rate
        {
            get { return _ddcStep.h_th_rate; }
            set
            {
                _ddcStep.h_th_rate = value;
                H_th_Target = _ddcStep.h_th_rate[_selectedGainLevelValue];

                RaisePropertyChanged("H_th_Target");
            }
        }

        public int dpc_dn_en
        {
            get { return _ddcStep.dpc_dn_en; }
            set { _ddcStep.dpc_dn_en = value; }
        }

        public uint[] indx_table
        {
            get { return _ddcStep.indx_table; }
            set
            {
                _ddcStep.indx_table = value;
                Indx_Target = _ddcStep.indx_table[_selectedGainLevelValue];
                RaisePropertyChanged("Indx_Target");
            }
        }

        public int[] indx_adapt
        {
            get { return _ddcStep.indx_adapt; }
            set
            {
                _ddcStep.indx_adapt = value;
                Weight_Target = _ddcStep.indx_adapt[_selectedGainLevelValue];
                RaisePropertyChanged("Weight_Target");
            }
        }

        public int[] std_th
        {
            get { return _ddcStep.std_th; }
            set { _ddcStep.std_th = value; }
        }

        public int std_th_rate
        {
            get { return _ddcStep.std_th_rate; }
            set { _ddcStep.std_th_rate = value; }
        }

        public int ddc_step
        {
            get { return _ddcStep.ddc_step; }
            set { _ddcStep.ddc_step = value; }
        }

        public int D_th_Target
        {
            get
            {
                var tmp = _ddcStep.d_th_rate[_selectedGainLevelValue];
                return Convert.ToInt32(tmp);
            }
            set
            {
                //int tmp = Convert.ToInt32(value);
                //_ddcStep.d_th_rate[_selectedGainLevelValue] = tmp;
                int[] tmp = _ddcStep.d_th_rate.Select(x => Convert.ToInt32(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToInt32(value);
                _ddcStep.d_th_rate = tmp;
                RaisePropertyChanged("D_th_Target");
                // 触发SAJ参数的延迟发�?
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Ddc);
                //}
            }
        }

        public int H_th_Target
        {
            get
            {
                var tmp = _ddcStep.h_th_rate[_selectedGainLevelValue];
                return Convert.ToInt32(tmp);
            }
            set
            {
                int[] tmp = _ddcStep.h_th_rate.Select(x => Convert.ToInt32(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToInt32(value);
                _ddcStep.h_th_rate = tmp;
                RaisePropertyChanged("H_th_Target");
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Ddc);
                //}
            }
        }

        public uint Indx_Target
        {
            get
            {
                var tmp = _ddcStep.indx_table[_selectedGainLevelValue];
                return Convert.ToUInt32(tmp);
            }
            set
            {
                uint[] tmp = _ddcStep.indx_table.Select(x => Convert.ToUInt32(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToUInt32(value);
                _ddcStep.indx_table = tmp;
                RaisePropertyChanged("Indx_Target");
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Ddc);
                //}
            }
        }

        public int Weight_Target
        {
            get
            {
                var tmp = _ddcStep.indx_adapt[_selectedGainLevelValue];
                return Convert.ToInt32(tmp);
            }
            set
            {
                int[] tmp = _ddcStep.indx_adapt.Select(x => Convert.ToInt32(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToInt32(value);
                _ddcStep.indx_adapt = tmp;
                RaisePropertyChanged("Weight_Target");
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Ddc);
                //}
            }
        }


        /// <summary>
        /// 将DDC数据格式化为C结构体格�?
        /// </summary>
        public string FormatDDCDataToC()
        {
            string dThRateStr = "{" + string.Join(",", _ddcStep.d_th_rate.Select(i => i.ToString()).ToArray()) + "}";
            string hThRateStr = "{" + string.Join(",", _ddcStep.h_th_rate.Select(i => i.ToString()).ToArray()) + "}";
            string indxTableStr = "{" + string.Join(",", _ddcStep.indx_table.Select(i => "DNIDX(" + string.Join(",", GetEightBytesFromUInt32(i)) + ")").ToArray()) + "}";
            string indxAdaptStr = "{" + string.Join(",", _ddcStep.indx_adapt.Select(i => i.ToString()).ToArray()) + "}";
            string stdThStr = "{" + string.Join(",", _ddcStep.std_th.Select(i => i.ToString()).ToArray()) + "}";

            return $".ddc_adapt = {{{_ddcStep.hot_num},{_ddcStep.dead_num},{_ddcStep.hot_th},{_ddcStep.dead_th},{_ddcStep.avg_th},{dThRateStr},{hThRateStr},{_ddcStep.dpc_dn_en},{indxTableStr},{indxAdaptStr},{stdThStr},{_ddcStep.std_th_rate}}},";
        }

        /// 从uint中提�?字节信息用于DNIDX�?
        /// </summary>
        /// <param name="value">要解析的uint�?/param>
        /// <returns>包含8个字节的数组</returns>
        private byte[] GetEightBytesFromUInt32(uint value)
        {
            // 这里需要根据实际的DNIDX宏定义来决定如何分解uint�?
            // 假设DNIDX宏需�?个参数，可能是通过某种方式将uint值拆分成8个字�?
            // 由于没有具体的DNIDX定义，我们暂时按位操作来模拟
            byte[] bytes = new byte[8];
            // 将uint值拆分成8个部分，每个部分代表一个字节或计算�?
            bytes[7] = (byte)((value >> 28) & 0xF); // �?�?
            bytes[6] = (byte)((value >> 24) & 0xF); // 接下�?�?
            bytes[5] = (byte)((value >> 20) & 0xF); // 接下�?�?
            bytes[4] = (byte)((value >> 16) & 0xF); // 接下�?�?
            bytes[3] = (byte)((value >> 12) & 0xF); // 接下�?�?
            bytes[2] = (byte)((value >> 8) & 0xF);  // 接下�?�?
            bytes[1] = (byte)((value >> 4) & 0xF);  // 接下�?�?
            bytes[0] = (byte)(value & 0xF);         // �?�?

            return bytes;
        }

        #endregion

        #region Awb
        public int Awb_De_High_Red_Class
        {
            get { return _awbStep.Awb_De_High_Red_Class; }
            set { _awbStep.Awb_De_High_Red_Class = value; }
        }

        public int Awb_De_High_Blue_Class
        {
            get { return _awbStep.Awb_De_High_Blue_Class; }
            set { _awbStep.Awb_De_High_Blue_Class = value; }
        }

        public int Awb_De_High_Red_Rate
        {
            get { return _awbStep.Awb_De_High_Red_Rate; }
            set { _awbStep.Awb_De_High_Red_Rate = value; }
        }

        public int Awb_De_High_Blue_Rate
        {
            get { return _awbStep.Awb_De_High_Blue_Rate; }
            set { _awbStep.Awb_De_High_Blue_Rate = value; }
        }

        public int Seg_Mode
        {
            get { return _awbStep.Seg_Mode; }
            set { _awbStep.Seg_Mode = value; }
        }

        public int MixPixSum
        {
            get { return _awbStep.MixPixSum; }
            set { _awbStep.MixPixSum = value; }
        }

        public int Awb_Weight_In
        {
            get { return _awbStep.Awb_Weight_In; }
            set { _awbStep.Awb_Weight_In = value; }
        }

        public int Awb_Weight_Out
        {
            get { return _awbStep.Awb_Weight_Out; }
            set { _awbStep.Awb_Weight_Out = value; }
        }

        public int RGainStart
        {
            get { return _awbStep.RGainStart; }
            set
            {
                _awbStep.RGainStart = value;
                RaisePropertyChanged("RGainEnd");
            }
        }

        public int RGainEnd
        {
            get { return _awbStep.RGainStart + 16 * 31; }
        }

        public Dictionary<string, KeyValuePair<int, int>> GainData
        {
            get { return _awbStep.GainData; }
            set { _awbStep.GainData = value; }
        }

        public int RGainMin
        {
            get { return _awbStep.RGainMin; }
            set { _awbStep.RGainMin = value; }
        }

        public int RGainMax
        {
            get { return _awbStep.RGainMax; }
            set { _awbStep.RGainMax = value; }
        }

        public int Awb_YMin
        {
            get { return _awbStep.Awb_YMin; }
            set { _awbStep.Awb_YMin = value; }
        }

        public int Awb_YMax
        {
            get { return _awbStep.Awb_YMax; }
            set { _awbStep.Awb_YMax = value; }
        }

        public byte[] Awb_Stat_Tab
        {
            get { return _awbStep.Awb_Stat_Tab; }
            set { _awbStep.Awb_Stat_Tab = value; }
        }

        public int Awb_Yuv_Mod_En
        {
            get { return _awbStep.Awb_Yuv_Mod_En; }
            set { _awbStep.Awb_Yuv_Mod_En = value; }
        }

        public int[] Awb_Cb_Th
        {
            get { return _awbStep.Awb_Cb_Th; }
            set { _awbStep.Awb_Cb_Th = value; }
        }

        public int[] Awb_Cr_Th
        {
            get { return _awbStep.Awb_Cr_Th; }
            set { _awbStep.Awb_Cr_Th = value; }
        }

        public int[] Awb_Cbcr_Th
        {
            get { return _awbStep.Awb_Cbcr_Th; }
            set { _awbStep.Awb_Cbcr_Th = value; }
        }

        public byte Awb_Ycbcr_Th
        {
            get { return _awbStep.Awb_Ycbcr_Th; }
            set { _awbStep.Awb_Ycbcr_Th = value; }
        }

        /// <summary>
        /// 将AWB数据格式化为C结构体格�?
        /// </summary>
        public string FormatAWBDataToC()
        {
            string awbStatTabStr = "{" + string.Join(",", _awbStep.Awb_Stat_Tab.Take(128).Select(b => b.ToString()).ToArray()) + "}"; // AWB统计表通常�?28字节

            // 根据不同设备类型返回不同的格�?
            if (_commonConfig != null && _commonConfig.DeviceType == "AX327X")
            {
                return $".awb_adapt = {{{_awbStep.Seg_Mode},{_awbStep.RGainStart},{_awbStep.RGainMin},{_awbStep.RGainMax},{_awbStep.Awb_Weight_In},{_awbStep.Awb_Weight_Out},{_awbStep.Awb_YMin},{_awbStep.Awb_YMax},{_awbStep.Awb_De_High_Blue_Rate},{_awbStep.Awb_De_High_Blue_Class},{_awbStep.Awb_De_High_Red_Rate},{_awbStep.Awb_De_High_Red_Class},0,{_awbStep.Awb_Ycbcr_Th},{awbStatTabStr}}},";
            }
            else
            {
                // AX32XX设备格式
                return $".awb_adapt = {{{_awbStep.MixPixSum},{_awbStep.RGainStart},{_awbStep.RGainMin},{_awbStep.RGainMax},{_awbStep.Awb_Weight_In},{_awbStep.Awb_Weight_Out},{ConvertToHex(_awbStep.Awb_YMin)},{ConvertToHex(_awbStep.Awb_YMax)},{ConvertToHex(_awbStep.Awb_De_High_Blue_Rate)},{ConvertToHex(_awbStep.Awb_De_High_Blue_Class)},{ConvertToHex(_awbStep.Awb_De_High_Red_Rate)},{ConvertToHex(_awbStep.Awb_De_High_Red_Class)},{ConvertToHex(_awbStep.Awb_Yuv_Mod_En)},{ConvertToHex(_awbStep.Awb_Ycbcr_Th)},{awbStatTabStr}}},";
            }
        }

        #endregion

        #region CCM
        public short[] ccm
        {
            get { return _ccmStep.ccm; }
            set { _ccmStep.ccm = value; }
        }

        public short s41
        {
            get { return _ccmStep.s41; }
            set { _ccmStep.s41 = value; }
        }

        public short s42
        {
            get { return _ccmStep.s42; }
            set { _ccmStep.s42 = value; }
        }

        public short s43
        {
            get { return _ccmStep.s43; }
            set { _ccmStep.s43 = value; }
        }

        /// <summary>
        /// 将CCM数据格式化为C结构体格�?
        /// </summary>
        public string FormatCCMDataToC()
        {
            // 将ccm数组格式化为3x3矩阵形式，以更清晰地展示矩阵结构
            StringBuilder ccmMatrixStr = new StringBuilder("{");
            for (int i = 0; i < 9; i++)
            {
                if (i > 0) ccmMatrixStr.Append(",");
                // 直接使用十进制格式表示数值，包括负数
                ccmMatrixStr.Append(_ccmStep.ccm[i].ToString());
            }
            ccmMatrixStr.Append("}");

            return $".ccm_adapt = {{{ccmMatrixStr},{ConvertToHex(_ccmStep.s41)},{ConvertToHex(_ccmStep.s42)},{ConvertToHex(_ccmStep.s43)}}},";
        }
        #endregion

        #region YGamma

        public short[] YGammaTable
        {
            get { return _yGammaStep.YGammaTable; }
            set { _yGammaStep.YGammaTable = value; }
        }

        public byte[] YGammaNum
        {
            get { return _yGammaStep.Gma_Num; }
            set
            {
                _yGammaStep.Gma_Num = value;
                YGamma_Num_Target = _yGammaStep.Gma_Num[_selectedGainLevelValue];
                RaisePropertyChanged("YGamma_Num_Target");
            }
        }

        public byte[] YGammaYLowRate
        {
            get { return _yGammaStep.YLowRate; }
            set
            {
                _yGammaStep.YLowRate = value;
                YLowRate_Target = _yGammaStep.YLowRate[_selectedGainLevelValue];
                RaisePropertyChanged("YLowRate_Target");
            }
        }
        public byte[] YGammaYHighRate
        {
            get { return _yGammaStep.YHighRate; }
            set
            {
                _yGammaStep.YHighRate = value;
                YHighRate_Target = _yGammaStep.YHighRate[_selectedGainLevelValue];
                RaisePropertyChanged("YHighRate_Target");
            }
        }
        public byte[] YGammaRate
        {
            get { return _yGammaStep.Rate; }
            set
            {
                _yGammaStep.Rate = value;
                YGamma_Rate_Target = _yGammaStep.Rate[_selectedGainLevelValue];
                RaisePropertyChanged("YGamma_Rate_Target");
            }
        }

        public int YGammaPadNum
        {
            get { return _yGammaStep.PadNum; }
            set { _yGammaStep.PadNum = (byte)value; }
        }

        public byte YLowRate_Target
        {
            get { return _yGammaStep.YLowRate[_selectedGainLevelValue]; }
            set
            {
                //byte tmp = Convert.ToByte(value);
                //_yGammaStep.YLowRate[_selectedGainLevelValue] = tmp;

                byte[] tmp = _yGammaStep.YLowRate.Select(x => Convert.ToByte(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToByte(value);
                _yGammaStep.YLowRate = tmp;
                RaisePropertyChanged("YLowRate_Target");
                if (IsOnline)
                {
                    TriggerModuleRealTimeUpdate(IspModule.YGamma);
                }
            }
        }

        public byte YHighRate_Target
        {
            get { return _yGammaStep.YHighRate[_selectedGainLevelValue]; }
            set
            {
                byte[] tmp = _yGammaStep.YHighRate.Select(x => Convert.ToByte(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToByte(value);
                _yGammaStep.YHighRate = tmp;
                RaisePropertyChanged("YHighRate_Target");
                if (IsOnline)
                {
                    TriggerModuleRealTimeUpdate(IspModule.YGamma);
                }
            }
        }

        public byte YGamma_Rate_Target
        {
            get { return _yGammaStep.Rate[_selectedGainLevelValue]; }
            set
            {
                byte[] tmp = _yGammaStep.Rate.Select(x => Convert.ToByte(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToByte(value);
                _yGammaStep.Rate = tmp;
                RaisePropertyChanged("YGamma_Rate_Target");
                if (IsOnline)
                {
                    TriggerModuleRealTimeUpdate(IspModule.YGamma);
                }
            }
        }

        public byte YGamma_Num_Target
        {
            get { return _yGammaStep.Gma_Num[_selectedGainLevelValue]; }
            set
            {
                byte[] tmp = _yGammaStep.Gma_Num.Select(x => Convert.ToByte(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToByte(value);
                _yGammaStep.Gma_Num = tmp;
                RaisePropertyChanged("YGamma_Num_Target");
                if (IsOnline)
                {
                    TriggerModuleRealTimeUpdate(IspModule.YGamma);
                }
            }
        }


        public byte YLowLimit
        {
            get { return _yGammaStep.YLowLimit; }
            set { _yGammaStep.YLowLimit = value; }
        }

        public byte YHighLimit
        {
            get { return _yGammaStep.YHighLimit; }
            set { _yGammaStep.YHighLimit = value; }
        }

        public byte FogPersent
        {
            get { return _yGammaStep.FogPersent; }
            set { _yGammaStep.FogPersent = value; }
        }

        /// <summary>
        /// 将YGAMMA数据格式化为C结构体格�?
        /// </summary>
        public string FormatYGAMMADataToC()
        {
            string gmaNumStr = "{" + string.Join(",", _yGammaStep.Gma_Num.Select(b => b.ToString()).ToArray()) + "}";
            string yLowRateStr = "{" + string.Join(",", _yGammaStep.YLowRate.Select(b => b.ToString()).ToArray()) + "}";
            string yHighRateStr = "{" + string.Join(",", _yGammaStep.YHighRate.Select(b => b.ToString()).ToArray()) + "}";
            string rateStr = "{" + string.Join(",", _yGammaStep.Rate.Select(b => b.ToString()).ToArray()) + "}";

            return $".ygama_adapt = {{{gmaNumStr},{yLowRateStr},{yHighRateStr},{rateStr},{_yGammaStep.YLowLimit},{_yGammaStep.YHighLimit},{_yGammaStep.FogPersent}}},";
        }
        #endregion

        #region CH
        public int stage0_en
        {
            get { return _chStep.stage0_en; }
            set { _chStep.stage0_en = value; }
        }

        public int stage1_en
        {
            get { return _chStep.stage1_en; }
            set { _chStep.stage1_en = value; }
        }

        public int[] enhence
        {
            get { return _chStep.enhence; }
            set { _chStep.enhence = value; }
        }

        public int[] th1
        {
            get { return _chStep.th1; }
            set { _chStep.th1 = value; }
        }

        public int[] th0
        {
            get { return _chStep.th0; }
            set { _chStep.th0 = value; }
        }

        public int[] r_rate
        {
            get { return _chStep.r_rate; }
            set
            {
                for (int i = 0; i < value.Length; i++)
                {
                    _chStep.SetRRate(i, value[i]);
                }
            }
        }

        public int[] g_rate
        {
            get { return _chStep.g_rate; }
            set
            {
                for (int i = 0; i < value.Length; i++)
                {
                    _chStep.SetGRate(i, value[i]);
                }
            }
        }

        public int[] b_rate
        {
            get { return _chStep.b_rate; }
            set
            {
                for (int i = 0; i < value.Length; i++)
                {
                    _chStep.SetBRate(i, value[i]);
                }
            }
        }

        public int[] Ch_sat
        {
            get { return _chStep.sat; }
            set { _chStep.sat = value; }
        }

        public int[] rate
        {
            get { return _chStep.rate; }
            set
            {
                _chStep.rate = value;
                Rate_Target = _chStep.rate[_selectedGainLevelValue];

                RaisePropertyChanged("Rate_Target");
            }
        }

        public int Rate_Target
        {
            get
            {
                var tmp = _chStep.rate[_selectedGainLevelValue];
                return Convert.ToInt32(tmp);
            }
            set
            {
                //int tmp = Convert.ToInt32(value);
                //_chStep.rate[_selectedGainLevelValue] = tmp;
                int[] tmp = _chStep.rate.Select(x => Convert.ToInt32(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToInt32(value);
                _chStep.rate = tmp;
                RaisePropertyChanged("Rate_Target");
                if (IsOnline)
                {
                    TriggerModuleRealTimeUpdate(IspModule.Ch);
                }
            }
        }

        /// <summary>
        /// 将CH数据格式化为C结构体格�?
        /// </summary>
        public string FormatCHDataToC()
        {
            string enhenceStr = "{" + string.Join(",", _chStep.enhence.Select(i => i.ToString()).ToArray()) + "}";
            //string th1Str = "{" + string.Join(",", _chStep.th1.Select(i => i.ToString()).ToArray()) + "}";
            //string th0Str = "{" + string.Join(",", _chStep.th0.Select(i => i.ToString()).ToArray()) + "}";
            string rRateStr = "{" + string.Join(",", _chStep.r_rate.Select(i => i.ToString()).ToArray()) + "}";
            string gRateStr = "{" + string.Join(",", _chStep.g_rate.Select(i => i.ToString()).ToArray()) + "}";
            string bRateStr = "{" + string.Join(",", _chStep.b_rate.Select(i => i.ToString()).ToArray()) + "}";
            string satStr = "{" + string.Join(",", _chStep.sat.Select(i => i.ToString()).ToArray()) + "}";
            string rateStr = "{" + string.Join(",", _chStep.rate.Select(i => i.ToString()).ToArray()) + "}";

            return $".ch_adapt = {{{_chStep.stage0_en},{_chStep.stage1_en},{enhenceStr},{rRateStr},{gRateStr},{bRateStr},{satStr},{rateStr}}},";
        }

        #endregion

        #region EE

        public byte ee_class
        {
            get { return _eeStep.ee_class; }
            set
            {
                _eeStep.ee_class = value;
                RaisePropertyChanged("ee_class");
                if (IsOnline)
                {
                    TriggerModuleRealTimeUpdate(IspModule.Ee);
                }
            }
        }

        public byte[] ee_dn_slope
        {
            get { return _eeStep.ee_dn_slope; }
            set
            {
                _eeStep.ee_dn_slope = value;
                ee_dn_slope_Target = _eeStep.ee_dn_slope[_selectedGainLevelValue];
                RaisePropertyChanged("ee_dn_slope_Target");
            }
        }

        public byte[] ee_sharp_slope
        {
            get { return _eeStep.ee_sharp_slope; }
            set
            {
                _eeStep.ee_sharp_slope = value;
                ee_sharp_slope_Target = _eeStep.ee_sharp_slope[_selectedGainLevelValue];
                RaisePropertyChanged("ee_sharp_slope_Target");
            }
        }

        public byte[] ee_th_adp
        {
            get { return _eeStep.ee_th_adp; }
            set
            {
                _eeStep.ee_th_adp = value;
                ee_th_adp_Target = _eeStep.ee_th_adp[_selectedGainLevelValue];
                RaisePropertyChanged("ee_th_adp_Target");
            }
        }

        public byte[] ee_dn_th
        {
            get { return _eeStep.ee_dn_th; }
            set
            {
                _eeStep.ee_dn_th = value;
                ee_dn_th_Target = _eeStep.ee_dn_th[_selectedGainLevelValue];
                RaisePropertyChanged("ee_dn_th_Target");
            }
        }

        public byte[] sharp_class
        {
            get { return _eeStep.sharp_class; }
            set
            {
                _eeStep.sharp_class = value;
                sharp_class_Target = _eeStep.sharp_class[_selectedGainLevelValue];
                RaisePropertyChanged("sharp_class_Target");
            }
        }

        public byte[] dn_class
        {
            get { return _eeStep.dn_class; }
            set
            {
                _eeStep.dn_class = value;
                dn_class_Target = _eeStep.dn_class[_selectedGainLevelValue];
                RaisePropertyChanged("dn_class_Target");
            }
        }

        public byte ee_dn_slope_Target
        {
            get { return _eeStep.ee_dn_slope[_selectedGainLevelValue]; }
            set
            {
                byte[] tmp = _eeStep.ee_dn_slope.Select(x => Convert.ToByte(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToByte(value);
                _eeStep.ee_dn_slope = tmp;
                RaisePropertyChanged("ee_dn_slope_Target");
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Ee);
                //}
            }
        }

        public byte ee_sharp_slope_Target
        {
            get { return _eeStep.ee_sharp_slope[_selectedGainLevelValue]; }
            set
            {
                byte[] tmp = _eeStep.ee_sharp_slope.Select(x => Convert.ToByte(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToByte(value);
                _eeStep.ee_sharp_slope = tmp;
                RaisePropertyChanged("ee_sharp_slope_Target");
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Ee);
                //}
            }
        }

        public byte ee_th_adp_Target
        {
            get { return _eeStep.ee_th_adp[_selectedGainLevelValue]; }
            set
            {
                byte[] tmp = _eeStep.ee_th_adp.Select(x => Convert.ToByte(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToByte(value);
                _eeStep.ee_th_adp = tmp;
                RaisePropertyChanged("ee_th_adp_Target");
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Ee);
                //}
            }
        }

        public byte ee_dn_th_Target
        {
            get { return _eeStep.ee_dn_th[_selectedGainLevelValue]; }
            set
            {
                byte[] tmp = _eeStep.ee_dn_th.Select(x => Convert.ToByte(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToByte(value);
                _eeStep.ee_dn_th = tmp;
                RaisePropertyChanged("ee_dn_th_Target");
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Ee);
                //}
            }
        }

        public byte sharp_class_Target
        {
            get { return _eeStep.sharp_class[_selectedGainLevelValue]; }
            set
            {
                byte[] tmp = _eeStep.sharp_class.Select(x => Convert.ToByte(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToByte(value);
                _eeStep.sharp_class = tmp;
                RaisePropertyChanged("sharp_class_Target");
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Ee);
                //}
            }
        }

        public byte dn_class_Target
        {
            get { return _eeStep.dn_class[_selectedGainLevelValue]; }
            set
            {
                byte[] tmp = _eeStep.dn_class.Select(x => Convert.ToByte(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToByte(value);
                _eeStep.dn_class = tmp;
                RaisePropertyChanged("dn_class_Target");
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Ee);
                //}
            }
        }

        /// <summary>
        /// 将EE数据格式化为C结构体格�?
        /// </summary>
        public string FormatEEDataToC()
        {
            string eeDnSlopeStr = "{" + string.Join(",", _eeStep.ee_dn_slope.Select(b => b.ToString()).ToArray()) + "}";
            string eeSharpSlopeStr = "{" + string.Join(",", _eeStep.ee_sharp_slope.Select(b => b.ToString()).ToArray()) + "}";
            string eeThAdpStr = "{" + string.Join(",", _eeStep.ee_th_adp.Select(b => b.ToString()).ToArray()) + "}";
            string eeDnThStr = "{" + string.Join(",", _eeStep.ee_dn_th.Select(b => b.ToString()).ToArray()) + "}";
            string sharpClassStr = "{" + string.Join(",", _eeStep.sharp_class.Select(b => b.ToString()).ToArray()) + "}";
            string dnClassStr = "{" + string.Join(",", _eeStep.dn_class.Select(b => b.ToString()).ToArray()) + "}";

            return $".ee_adapt = {{{_eeStep.ee_class},{eeDnSlopeStr},{eeSharpSlopeStr},{eeThAdpStr},{eeDnThStr},{sharpClassStr},{dnClassStr}}},";
        }

        #endregion

        #region SAJ
        public byte[] Saj_sat
        {
            get { return _sajStep.sat; }
            set
            {
                _sajStep.sat = value;
                RaisePropertyChanged("Saj_sat");
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Saj);
                //}
            }
        }
        public byte[] Saj_rate
        {
            get { return _sajStep.sat_rate; }
            set
            {
                _sajStep.sat_rate = value;
                SAJ_Rate_Target = _sajStep.sat_rate[_selectedGainLevelValue];
                RaisePropertyChanged("SAJ_Rate_Target");
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Saj);
                //}
            }
        }

        public byte saj_step
        {
            get { return _sajStep.saj_step; }
            set { _sajStep.saj_step = value; }
        }

        public byte SAJ_Rate_Target
        {
            get
            {
                var tmp = _sajStep.sat_rate[_selectedGainLevelValue];
                return Convert.ToByte(tmp);
            }
            set
            {
                byte[] tmp = _sajStep.sat_rate.Select(x => Convert.ToByte(x)).ToArray();
                tmp[_selectedGainLevelValue] = Convert.ToByte(value);
                _sajStep.sat_rate = tmp;
                RaisePropertyChanged("SAJ_Rate_Target");
                //if (IsOnline)
                //{
                //    TriggerModuleRealTimeUpdate(IspModule.Saj);
                //}
            }
        }

        /// <summary>
        /// 将SAJ数据格式化为C结构体格�?
        /// </summary>
        public string FormatSAJDataToC()
        {
            string satStr = "{" + string.Join(",", _sajStep.sat.Select(b => b.ToString()).ToArray()) + "}";
            string satRateStr = "{" + string.Join(",", _sajStep.sat_rate.Select(b => b.ToString()).ToArray()) + "}";

            return $".saj_adapt = {{{satStr},{satRateStr}}},";
        }

        #endregion

        #region GainLevel
        public int[] Gain_level
        {
            get { return _gainLevelStep.Gain_Level; }
            set { _gainLevelStep.Gain_Level = value; }
        }


        // 为数组中的每个元素提供单独的属性以供界面绑�?
        public int GainLevel0
        {
            get { return _gainLevelStep.Gain_Level.Length > 0 ? _gainLevelStep.Gain_Level[0] : 0; }
            set
            {
                if (_gainLevelStep.Gain_Level.Length > 0)
                {
                    //_gainLevelStep.Gain_Level[0] = value;
                    var tmp = _gainLevelStep.Gain_Level;
                    tmp[0] = value;
                    _gainLevelStep.Gain_Level = tmp;
                    RaisePropertyChanged("GainLevel0");
                    if (IsOnline)
                    {
                        TriggerModuleRealTimeUpdate(IspModule.GainLevel);
                    }
                }
            }
        }

        public int GainLevel1
        {
            get { return _gainLevelStep.Gain_Level.Length > 1 ? _gainLevelStep.Gain_Level[1] : 0; }
            set
            {
                if (_gainLevelStep.Gain_Level.Length > 1)
                {
                    var tmp = _gainLevelStep.Gain_Level;
                    tmp[1] = value;
                    _gainLevelStep.Gain_Level = tmp;
                    RaisePropertyChanged("GainLevel1");
                    if (IsOnline)
                    {
                        TriggerModuleRealTimeUpdate(IspModule.GainLevel);
                    }
                }
            }
        }

        public int GainLevel2
        {
            get { return _gainLevelStep.Gain_Level.Length > 2 ? _gainLevelStep.Gain_Level[2] : 0; }
            set
            {
                if (_gainLevelStep.Gain_Level.Length > 2)
                {
                    var tmp = _gainLevelStep.Gain_Level;
                    tmp[2] = value;
                    _gainLevelStep.Gain_Level = tmp;
                    RaisePropertyChanged("GainLevel2");
                    if (IsOnline)
                    {
                        TriggerModuleRealTimeUpdate(IspModule.GainLevel);
                    }
                }
            }
        }

        public int GainLevel3
        {
            get { return _gainLevelStep.Gain_Level.Length > 3 ? _gainLevelStep.Gain_Level[3] : 0; }
            set
            {
                if (_gainLevelStep.Gain_Level.Length > 3)
                {
                    var tmp = _gainLevelStep.Gain_Level;
                    tmp[3] = value;
                    _gainLevelStep.Gain_Level = tmp;
                    RaisePropertyChanged("GainLevel3");
                    if (IsOnline)
                    {
                        TriggerModuleRealTimeUpdate(IspModule.GainLevel);
                    }
                }
            }
        }

        public int GainLevel4
        {
            get { return _gainLevelStep.Gain_Level.Length > 4 ? _gainLevelStep.Gain_Level[4] : 0; }
            set
            {
                if (_gainLevelStep.Gain_Level.Length > 4)
                {
                    var tmp = _gainLevelStep.Gain_Level;
                    tmp[4] = value;
                    _gainLevelStep.Gain_Level = tmp;
                    RaisePropertyChanged("GainLevel4");
                    if (IsOnline)
                    {
                        TriggerModuleRealTimeUpdate(IspModule.GainLevel);
                    }
                }
            }
        }

        public int GainLevel5
        {
            get { return _gainLevelStep.Gain_Level.Length > 5 ? _gainLevelStep.Gain_Level[5] : 0; }
            set
            {
                if (_gainLevelStep.Gain_Level.Length > 5)
                {
                    var tmp = _gainLevelStep.Gain_Level;
                    tmp[5] = value;
                    _gainLevelStep.Gain_Level = tmp;
                    RaisePropertyChanged("GainLevel5");
                    if (IsOnline)
                    {
                        TriggerModuleRealTimeUpdate(IspModule.GainLevel);
                    }
                }
            }
        }

        public int GainLevel6
        {
            get { return _gainLevelStep.Gain_Level.Length > 6 ? _gainLevelStep.Gain_Level[6] : 0; }
            set
            {
                if (_gainLevelStep.Gain_Level.Length > 6)
                {
                    var tmp = _gainLevelStep.Gain_Level;
                    tmp[6] = value;
                    _gainLevelStep.Gain_Level = tmp;
                    RaisePropertyChanged("GainLevel6");
                    if (IsOnline)
                    {
                        TriggerModuleRealTimeUpdate(IspModule.GainLevel);
                    }
                }
            }
        }

        public int GainLevel7
        {
            get { return _gainLevelStep.Gain_Level.Length > 7 ? _gainLevelStep.Gain_Level[7] : 0; }
            set
            {
                if (_gainLevelStep.Gain_Level.Length > 7)
                {
                    var tmp = _gainLevelStep.Gain_Level;
                    tmp[7] = value;
                    _gainLevelStep.Gain_Level = tmp;
                    RaisePropertyChanged("GainLevel7");
                    if (IsOnline)
                    {
                        TriggerModuleRealTimeUpdate(IspModule.GainLevel);
                    }
                }
            }
        }

        /// <summary>
        /// 将GainLevel数据格式化为C结构体格�?
        /// </summary>
        public string FormatGainLevelDataToC()
        {
            // 检查是否可以表示为递减�?024倍数格式，否则直接输出原始�?
            bool canRepresentAsMultiples = true;
            List<string> elements = new List<string>();

            for (int i = 0; i < _gainLevelStep.Gain_Level.Length; i++)
            {
                int expectedValue = (_gainLevelStep.Gain_Level.Length - i) * 1024;
                if (_gainLevelStep.Gain_Level[i] == expectedValue)
                {
                    elements.Add($"{_gainLevelStep.Gain_Level.Length - i}*1024");
                }
                else
                {
                    canRepresentAsMultiples = false;
                    break;
                }
            }

            // 如果不能表示为递减�?024倍数，则输出原始�?
            if (!canRepresentAsMultiples)
            {
                elements.Clear();
                foreach (int val in _gainLevelStep.Gain_Level)
                {
                    elements.Add(val.ToString());
                }
            }

            string gainLevelStr = "{" + string.Join(",", elements) + "}";

            return $".gain_levl = {gainLevelStr},";
        }

        /// <summary>
        /// 将所有ISP数据格式化为完整的C结构体格式（_Sensor_Adpt�?
        /// </summary>
        public string FormatAllISPDataToC()
        {
            var sb = new StringBuilder();

            // 获取设备名称
            string sensorName = _commonConfig.Name.Replace(" ", "_");
            if (string.IsNullOrEmpty(sensorName))
                sensorName = "sensor";

            sb.AppendLine($"const _Sensor_Adpt {sensorName}_cmd =");
            sb.AppendLine("{");

            // .itf - 接口类型（根据Type字段判断�?
            byte typ = _commonConfig.Type;
            string itfType = (typ == 0 || typ == 1) ? "CSI_IF_DVP" : "CSI_IF_MIPI";
            sb.AppendLine($"    .itf = {itfType},");

            // .typ - 数据类型
            string csiType;
            switch (_commonConfig.SetMode)
            {
                case SetMode.RAW10:
                    csiType = "CSI_TYPE_RAW10";
                    break;
                case SetMode.RAW8:
                    csiType = "CSI_TYPE_RAW8";
                    break;
                case SetMode.YUV:
                    csiType = "CSI_TYPE_YUV";
                    break;
                default:
                    csiType = "CSI_TYPE_RAW10";
                    break;
            }
            sb.AppendLine($"    .typ = {csiType},");

            // .pixelw / .pixelh
            sb.AppendLine($"    .pixelw = {_commonConfig.ResolutionWidth},");
            sb.AppendLine($"    .pixelh= {_commonConfig.ResolutionHeight},");

            // .hsyn / .vsyn
            sb.AppendLine($"    .hsyn = {_commonConfig.Hsyn},");
            sb.AppendLine($"    .vsyn = {_commonConfig.Vsyn},");

            // .colrarray - Bayer模式: 0:RGRG 1:GRGR 2:BGBG 3:GBGB
            int bayerIdx = (int)_commonConfig.Bayer;
            sb.AppendLine($"    .colrarray = {bayerIdx},//  RAW is 0:_RGRG_ 1:_GRGR_,2:_BGBG_,3:_GBGB_, YUV is 0: CBYCRY,1: CRYCBY ,2: YCBYCR , 3: YCRYCB");

            // .AVDD / .DVDD / .VDDIO
            sb.AppendLine($"    .AVDD = SYS_VOL_V{_commonConfig.AVDD},");
            sb.AppendLine($"    .DVDD = SYS_VOL_V{_commonConfig.DVDD},");
            sb.AppendLine($"    .VDDIO = SYS_VOL_V{_commonConfig.VDDIO},");

            // .rotate_adapt
            sb.AppendLine($"    .rotate_adapt = {{{_commonConfig.Rotate}}},");

            // .hvb_adapt
            var hvb = _commonConfig;
            sb.AppendLine($"    .hvb_adapt = {{{hvb.Pclk},{hvb.Vlen},0,0,0x{hvb.DownFpsMode:X},{hvb.Fps},{hvb.Frequency}}},");

            // .mclk
            sb.AppendLine($"    .mclk = {_commonConfig.Mclk},");

            // .pclk_fir_en / .pclk_fir_class / .pclk_inv_en / .csi_tun
            sb.AppendLine($"    .pclk_fir_en = {_commonConfig.IsPclkFirEn}, \t\t // digital fir");
            sb.AppendLine($"    .pclk_fir_class = {_commonConfig.PclkFirClass}, \t // analog fir  setting:1~7");
            sb.AppendLine($"    .pclk_inv_en = {(_commonConfig.IsPclkInvEn ? 1 : 0)},");
            sb.AppendLine($"    .csi_tun = {_commonConfig.CsiTun}, \t // csi clk tun delay step  setting:0~15");

            // .isp_all_mod - 根据各模块使能状态构�?
            string ispAllMod = BuildIspAllMod();
            sb.AppendLine($"    .isp_all_mod = \t {ispAllMod},");

            // .gain_levl
            sb.AppendLine($"    {FormatGainLevelDataToC()}");

            // .af_adapt - 使用默认值（ViewModel中没有对应字段）
            sb.AppendLine($"    .af_adapt = {{32,{_commonConfig.ResolutionWidth - 32},32,{_commonConfig.ResolutionHeight - 32},30}},");

            // .ae_adapt
            sb.AppendLine($"    {FormatAEDataToC()}");

            // .blc_adapt
            sb.AppendLine($"    {FormatBLCDataToC()}");

            // .ddc_adapt
            sb.AppendLine($"    {FormatDDCDataToC()}");

            // .awb_adapt
            sb.AppendLine($"    {FormatAWBDataToC()}");

            // .ccm_adapt
            sb.AppendLine($"    {FormatCCMDataToC()}");

            // .rgbdgain_adapt - 使用默认值
            sb.AppendLine($"    .rgbdgain_adapt = {{{{64,64,64,64,64,64,64,64,64}},{{64,64,64,64,64,64,64,64}}}},");

            // .ygama_adapt
            sb.AppendLine($"    {FormatYGAMMADataToC()}");

            // .ch_adapt
            sb.AppendLine($"    {FormatCHDataToC()}");

            // .vde_adapt - 使用默认值（ViewModel中没有对应字段）
            sb.AppendLine($"    .vde_adapt = {{0x80,0x80,0x80,0x80,{{32,64,64,64,64,64,64,64,8}},{{16,16,16,16,16,16,16,16}}}},");

            // .ee_adapt
            sb.AppendLine($"    {FormatEEDataToC()}");

            // .cfd_adapt - 使用默认值
            sb.AppendLine($"    .cfd_adapt = {{4,0xe0,0x20,1,1,0xff,1,0,1}},");

            // .saj_adapt
            sb.AppendLine($"    {FormatSAJDataToC()}");

            // .p_fun_adapt
            sb.AppendLine($"    .p_fun_adapt = {{/*{sensorName}_rotate*/NULL,/*{sensorName}_hvblank*/NULL,sensor_{sensorName.ToLower()}_exp_gain_wr}},");

            sb.AppendLine("};");

            return sb.ToString();
        }

        /// <summary>
        /// 构建isp_all_mod字段，根据各ISP模块的使能状态
        /// </summary>
        private string BuildIspAllMod()
        {
            var enables = _commonConfig.ProcessorStepsEnables;

            // 获取各模块的使能状�?
            bool blcEn = enables[(int)IspModule.Blc].Value;
            bool lscEn = enables[(int)IspModule.Lsc].Value;
            bool ddcEn = enables[(int)IspModule.Ddc].Value;
            bool awbEn = enables[(int)IspModule.Awb].Value;
            bool ccmEn = enables[(int)IspModule.Ccm].Value;
            bool dgainEn = enables[(int)IspModule.Dgain].Value;
            bool ygammaEn = enables[(int)IspModule.YGamma].Value;
            bool rgbGammaEn = enables[(int)IspModule.RgbGamma].Value;
            bool chEn = enables[(int)IspModule.Ch].Value;
            bool vdeEn = enables[(int)IspModule.Vde].Value;
            bool eeEn = enables[(int)IspModule.Ee].Value;
            bool cfdEn = enables[(int)IspModule.Cfd].Value;
            bool sajEn = enables[(int)IspModule.Saj].Value;

            // 构建isp_all_mod表达�?
            // 格式: (_ISP_FREE_<<_AE_POS_ | _ISP_EN_<<_BLC_POS_ | ...)
            var parts = new List<string>();
            parts.Add("_ISP_FREE_<<_AE_POS_");
            parts.Add(blcEn ? "_ISP_EN_<<_BLC_POS_" : "_ISP_DIS_<<_BLC_POS_");
            parts.Add(lscEn ? "_ISP_EN_<<_LSC_POS_" : "_ISP_DIS_<<_LSC_POS_");
            parts.Add(ddcEn ? "_ISP_AUTO_<<_DDC_POS_" : "_ISP_DIS_<<_DDC_POS_");
            parts.Add(awbEn ? "_ISP_AUTO_<<_AWB_POS_" : "_ISP_DIS_<<_AWB_POS_");
            parts.Add(ccmEn ? "_ISP_EN_<<_CCM_POS_" : "_ISP_DIS_<<_CCM_POS_");
            parts.Add(dgainEn ? "_ISP_EN_<<_DGAIN_POS_" : "_ISP_DIS_<<_DGAIN_POS_");
            parts.Add(ygammaEn ? "_ISP_AUTO_<<_YGAMA_POS_" : "_ISP_DIS_<<_YGAMA_POS_");
            parts.Add(rgbGammaEn ? "_ISP_AUTO_<<_RGB_GAMA_POS_" : "_ISP_DIS_<<_RGB_GAMA_POS_");
            parts.Add(chEn ? "_ISP_AUTO_<<_CH_POS_" : "_ISP_DIS_<<_CH_POS_");
            parts.Add(vdeEn ? "_ISP_EN_<<_VDE_POS_" : "_ISP_DIS_<<_VDE_POS_");
            parts.Add(eeEn ? "_ISP_EN_<<_EE_POS_" : "_ISP_DIS_<<_EE_POS_");
            parts.Add(cfdEn ? "_ISP_EN_<<_CFD_POS_" : "_ISP_DIS_<<_CFD_POS_");
            parts.Add(sajEn ? "_ISP_AUTO_<<_SAJ_POS_" : "_ISP_DIS_<<_SAJ_POS_");

            // 分行显示，每�?个模�?
            string result = "(";
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                {
                    result += " \\ \n    \t \t \t |";
                }
                result += parts[i];
            }
            result += ")";

            return result;
        }


        // 提供一个集合用于ComboBox显示 - 直接使用Gain_Level数组的数据
        public ObservableCollection<string> GainLevelValues
        {
            get
            {
                var collection = new ObservableCollection<string>();
                if (_gainLevelStep.Gain_Level != null)
                {
                    foreach (int value in _gainLevelStep.Gain_Level)
                    {
                        collection.Add(value.ToString());
                    }
                }
                collection.Add("Auto");
                return collection;
            }
        }

        // 用于跟踪选择的值
        private int _selectedGainLevelValue = 0;
        public int SelectedGainLevelValue
        {
            get
            {
                if (_selectedGainLevelValue == _commonConfig.GainLevel)
                {
                    return _selectedGainLevelValue;
                }
                SelectedGainLevelValue = _commonConfig.GainLevel;
                return _commonConfig.GainLevel;
            }
            set
            {
                if (_selectedGainLevelValue != value || _commonConfig.GainLevel != Convert.ToByte(value))
                {
                    byte byteValue = Convert.ToByte(value);

                    // 只有当值确实发生改变时才进行更�?
                    if (_commonConfig.GainLevel != byteValue)
                    {
                        _commonConfig.GainLevel = byteValue;

                        Task.Run(() =>
                        {
                            if (IsOnline)
                            {
                                DeviceConfig.ReadIspModuleFromDevice(IspModule.GammaTable);
                            }
                        });
                    }

                    if (_selectedGainLevelValue != value)
                    {
                        if (value < 8)
                        {
                            _selectedGainLevelValue = value;
                        }
                        else
                        {
                            _selectedGainLevelValue = 0; // 或者设置为默认�?
                        }
                    }
                    if (value < 8)
                    {
                        _selectedGainLevelValue = value;
                        RaisePropertyChanged("SelectedGainLevelValue");
                        RaisePropertyChanged("ExpTarget");
                        RaisePropertyChanged("Blc_Target");
                        RaisePropertyChanged("D_th_Target");
                        RaisePropertyChanged("H_th_Target");
                        RaisePropertyChanged("Indx_Target");
                        RaisePropertyChanged("Weight_Target");
                        RaisePropertyChanged("Rate_Target");
                        RaisePropertyChanged("YLowRate_Target");
                        RaisePropertyChanged("YHighRate_Target");
                        RaisePropertyChanged("YLowRate_Target");
                        RaisePropertyChanged("YGamma_Rate_Target");
                        RaisePropertyChanged("YGamma_Num_Target");
                        RaisePropertyChanged("ee_dn_slope_Target");
                        RaisePropertyChanged("ee_sharp_slope_Target");
                        RaisePropertyChanged("ee_th_adp_Target");
                        RaisePropertyChanged("ee_dn_th_Target");
                        RaisePropertyChanged("sharp_class_Target");
                        RaisePropertyChanged("dn_class_Target");
                        RaisePropertyChanged("SAJ_Rate_Target");
                    }
                    RaisePropertyChanged("GainLevel");
                }
            }
        }

        #endregion

        #region Methods-- Config Change
        void OnCommonConfigChange(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "Bayer":
                    RaisePropertyChanged("SelectedBayerPattern");
                    break;
                case "ResolutionHeight":
                    RaisePropertyChanged("ResolutionHeight");
                    break;
                case "ResolutionWidth":
                    RaisePropertyChanged("ResolutionWidth");
                    break;

                case "ExpGain":
                    RaisePropertyChanged("ExpGain");
                    break;
                case "RawScaleDown":
                    break;
                default: break;
            }
        }

        void OnStepsEnablesChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            /*
            if (e.NewItems != null)
            {
                foreach (KeyValuePair<ThunderSE.DeviceConfig.Isp.IspModule, bool> item in e.NewItems)
                {
                    var module = item.Key;
                    bool isSelected = item.Value;
                    if (isSelected)
                    {
                        _commonConfig.ProcessorStepsEnablesActualValueMap[module] = (char)0x00;
                    }
                    else
                    {
                        if (!_commonConfig.ProcessorStepsEnablesActualValueMap.ContainsKey(module) ||
                            _commonConfig.ProcessorStepsEnablesActualValueMap[module] == (char)0x00)
                        {
                            _commonConfig.ProcessorStepsEnablesActualValueMap[module] = module == ThunderSE.DeviceConfig.Isp.IspModule.Blc || module == ThunderSE.DeviceConfig.Isp.IspModule.Lsc
                                ? (char)0x01
                                : (char)0x02;
                        }
                    }
                }
            }
            */
            RaisePropertyChanged("CommonConfig");
        }

        private void OnAEConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
            if (e.PropertyName == "ExpAdapt.exp_tag")
            {
                RaisePropertyChanged("ExpTarget");
            }
            if (IsOnline)
            {
                TriggerModuleRealTimeUpdate(IspModule.AE);
            }
        }

        private void OnDdcConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
            if (IsOnline)
            {
                TriggerModuleRealTimeUpdate(IspModule.Ddc);
            }
        }

        private void OnBlcConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "CorrectValuesArray")
            {
                RaisePropertyChanged("BlcR");
                RaisePropertyChanged("BlcGr");
                RaisePropertyChanged("BlcGb");
                RaisePropertyChanged("BlcB");
            }
            if (IsOnline)
            {
                TriggerModuleRealTimeUpdate(IspModule.Blc);
            }
        }

        private void OnLscConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
            if (IsOnline)
            {
                Console.WriteLine($"LscConfig property changed: {e.PropertyName}");
                TriggerModuleRealTimeUpdate(IspModule.Lsc);
            }
        }

        private void OnAwbPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
            if (IsOnline && e.PropertyName != "GainData" && !IsAwbSetWindow) // Replace "SomePropertyName" with the actual property name to check
            {
                TriggerModuleRealTimeUpdate(IspModule.Awb);
            }
            if (IsAwbSetWindow && (e.PropertyName == "Awb_Stat_Tab" || e.PropertyName == "RGainMin" || e.PropertyName == "RGainMax" || e.PropertyName == "RGainStart"))
            {
                TriggerModuleRealTimeUpdate(IspModule.Awb);
            }
        }

        private void OnCcmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
            if (IsOnline)
            {
                TriggerModuleRealTimeUpdate(IspModule.Ccm);
            }
        }

        void OnYGammaStepPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "YLowLimit" || e.PropertyName == "YHighLimit" || e.PropertyName == "FogPersent")
            {
                RaisePropertyChanged(e.PropertyName);
                if (IsOnline)
                {
                    TriggerModuleRealTimeUpdate(IspModule.YGamma);
                }
            }
            else if (e.PropertyName == "LoadYGammaTable")
            {
                Console.WriteLine("LoadYGammaTable property changed");

                Task.Run(() =>
                {
                    if (IsOnline)
                    {
                        DeviceConfig.ReadIspModuleFromDevice(IspModule.GammaTable);
                    }
                });
            }
            else if (e.PropertyName == "SaveYGammaTable")
            {
                Console.WriteLine("SaveYGammaTable property changed");
                Task.Run(() =>
                {
                    if (IsOnline)
                    {
                        DeviceConfig.WriteIspModuleToDevice(IspModule.GammaTable);
                    }
                });
            }
        }

        void OnEEPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
            if (IsOnline)
            {
                TriggerModuleRealTimeUpdate(IspModule.Ee);
            }
        }

        void OnCHPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
            if (IsOnline)
            {
                TriggerModuleRealTimeUpdate(IspModule.Ch);
            }
        }

        void OnSAJPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
            if (IsOnline)
            {
                TriggerModuleRealTimeUpdate(IspModule.Saj);
            }
        }

        void OnGainLevelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 当GainLevel数组整体改变时，更新所有子属�?
            if (e.PropertyName == "Gain_Level")
            {
                RaisePropertyChanged("GainLevel0");
                RaisePropertyChanged("GainLevel1");
                RaisePropertyChanged("GainLevel2");
                RaisePropertyChanged("GainLevel3");
                RaisePropertyChanged("GainLevel4");
                RaisePropertyChanged("GainLevel5");
                RaisePropertyChanged("GainLevel6");
                RaisePropertyChanged("GainLevel7");
                RaisePropertyChanged("GainLevelValues");  // 刷新ComboBox数据
            }
            else
            {
                RaisePropertyChanged(e.PropertyName);
            }
        }

        #endregion

        #region Methods-- Config Write (moved to DeviceConfigPageViewModel.Writer.cs)

        private void SetRawScale()
        {
            if (IsOnline)
            {
                DateTime now = DateTime.Now;
                if ((now - _lastRawScaleApplyTime).TotalMilliseconds < 500)
                {
                    return;
                }
                _lastRawScaleApplyTime = now;

                try
                {
                    Logger.Info($"Applying RAW ScaleDown: Row Scale Index = {_selectedRawScaleValue}, Col Scale Index = {_selectedColRawScaleValue}");
                    byte rowScale = _rawScaleDown[_selectedRawScaleValue];
                    byte colScale = _rawScaleDown[_selectedColRawScaleValue];
                    byte currentScale = (byte)((colScale << 4) | rowScale);
                    _ispProcessor.IspCommonConfig.RawScaleDown = currentScale;
                    Logger.Info($"RAW ScaleDown applied: 0x{currentScale:X2} (row={rowScale}, col={colScale})");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error applying RAW ScaleDown: {ex.Message}", ex);
                }
            }
        }

        public void WriteConfig()
        {
            if (IsOnline)
            {
                // 添加确认提示对话框
                MessageBoxResult result = System.Windows.MessageBox.Show(
                    "您确定要将配置写入设备吗？此操作将覆盖设备上的当前配置。",
                    "确认写入设备",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    DeviceConfig.WriteToDevice();
                }
                //DeviceConfig.WriteToDevice();
            }
            else
            {
                if (DeviceConfig.FilePath.Length == 0)
                {
                    Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
                    saveFileDialog.CheckFileExists = false;
                    saveFileDialog.CheckPathExists = false;
                    saveFileDialog.Filter = "isp配置文件(*.isp) | *.isp";
                    if (!(bool)saveFileDialog.ShowDialog())
                    {
                        return;
                    }

                    DeviceConfig.FilePath = saveFileDialog.FileName;
                }
                DeviceConfig.WriteToFile();
                System.Windows.MessageBox.Show("成功写入配置", "", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }

        #endregion

        public void SaveConfigAs()
        {
            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
            saveFileDialog.CheckFileExists = false;
            saveFileDialog.CheckPathExists = false;
            saveFileDialog.Filter = "isp配置文件(*.isp) | *.isp";
            if (!(bool)saveFileDialog.ShowDialog())
            {
                return;
            }

            DeviceConfig.WriteToFile(saveFileDialog.FileName);
            System.Windows.MessageBox.Show("已保存为:" + saveFileDialog.FileName, "", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        public async void LoadConfigFromFile()
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.CheckFileExists = true;
            openFileDialog.CheckPathExists = true;
            openFileDialog.Filter = "isp配置文件(*.isp;*.xml)|*.isp;*.xml|所有文�?*.*)|*.*";
            if (!(bool)openFileDialog.ShowDialog())
            {
                return;
            }

            try
            {
                // 读取文件前暂时取消事件订阅，避免反序列化过程中频繁触�?
                _ispProcessor.IspCommonConfig.PropertyChanged -= OnCommonConfigChange;
                _aeStep.PropertyChanged -= OnAEConfigChange;
                _blackLevelStep.PropertyChanged -= OnBlcConfigChange;
                _lenShadingStep.PropertyChanged -= OnLscConfigChange;
                _ddcStep.PropertyChanged -= OnDdcConfigChange;
                _awbStep.PropertyChanged -= OnAwbPropertyChanged;
                _ccmStep.PropertyChanged -= OnCcmPropertyChanged;
                _yGammaStep.PropertyChanged -= OnYGammaStepPropertyChanged;
                _eeStep.PropertyChanged -= OnEEPropertyChanged;
                _chStep.PropertyChanged -= OnCHPropertyChanged;
                _sajStep.PropertyChanged -= OnSAJPropertyChanged;
                _gainLevelStep.PropertyChanged -= OnGainLevelPropertyChanged;

                string fileName = openFileDialog.FileName;

                // 在后台线程执行文件读�?
                await Task.Run(() =>
                {
                    DeviceConfig.FilePath = fileName;
                    DeviceConfig.ReadFromFile(fileName);
                });

                // 回到 UI 线程重新订阅事件并刷�?UI
                // 重新订阅事件
                _ispProcessor.IspCommonConfig.PropertyChanged += OnCommonConfigChange;
                _aeStep.PropertyChanged += OnAEConfigChange;
                _blackLevelStep.PropertyChanged += OnBlcConfigChange;
                _lenShadingStep.PropertyChanged += OnLscConfigChange;
                _ddcStep.PropertyChanged += OnDdcConfigChange;
                _awbStep.PropertyChanged += OnAwbPropertyChanged;
                _ccmStep.PropertyChanged += OnCcmPropertyChanged;
                _yGammaStep.PropertyChanged += OnYGammaStepPropertyChanged;
                _eeStep.PropertyChanged += OnEEPropertyChanged;
                _chStep.PropertyChanged += OnCHPropertyChanged;
                _sajStep.PropertyChanged += OnSAJPropertyChanged;
                _gainLevelStep.PropertyChanged += OnGainLevelPropertyChanged;

                // 通知 UI 刷新所有绑定属�?
                RaisePropertyChanged("CommonConfig");
                RaisePropertyChanged("GainLevel0");
                RaisePropertyChanged("GainLevel1");
                RaisePropertyChanged("GainLevel2");
                RaisePropertyChanged("GainLevel3");
                RaisePropertyChanged("GainLevel4");
                RaisePropertyChanged("GainLevel5");
                RaisePropertyChanged("GainLevel6");
                RaisePropertyChanged("GainLevel7");
                RaisePropertyChanged("GainLevelValues");

                Logger.Info($"Configuration loaded from file: {fileName}");
                System.Windows.MessageBox.Show("已从文件加载配置: " + fileName, "",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // 确保事件重新订阅（即使发生异常也要恢复）
                _ispProcessor.IspCommonConfig.PropertyChanged += OnCommonConfigChange;
                _aeStep.PropertyChanged += OnAEConfigChange;
                _blackLevelStep.PropertyChanged += OnBlcConfigChange;
                _lenShadingStep.PropertyChanged += OnLscConfigChange;
                _ddcStep.PropertyChanged += OnDdcConfigChange;
                _awbStep.PropertyChanged += OnAwbPropertyChanged;
                _ccmStep.PropertyChanged += OnCcmPropertyChanged;
                _yGammaStep.PropertyChanged += OnYGammaStepPropertyChanged;
                _eeStep.PropertyChanged += OnEEPropertyChanged;
                _chStep.PropertyChanged += OnCHPropertyChanged;
                _sajStep.PropertyChanged += OnSAJPropertyChanged;
                _gainLevelStep.PropertyChanged += OnGainLevelPropertyChanged;

                Logger.Error($"Failed to load config from file: {ex.Message}");
                System.Windows.MessageBox.Show("加载配置文件失败: " + ex.Message, "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /*
        public void ReloadConfig()
        {
            if (IsOnline)
            {
                DeviceConfig.RefreshDataFromDevice();
            }
            else
            {
                DeviceConfig.ReadFromFile();
            }
        }
        */

        private async void ReloadConfig()
        {
            if (DeviceConfig == null)
            {
                MessageBox.Show("设备未连接", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (IsReloadingConfig) return; // 防止重复执行

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsReloadingConfig = true;
                ReloadStatusMessage = "正在重新加载配置...";
            });

            try
            {
                await Task.Run(() =>
                {
                    if (IsOnline)
                    {
                        DeviceConfig.RefreshDataFromDevice();
                    }
                    else
                    {
                        DeviceConfig.ReadFromFile();
                    }
                });

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ReloadStatusMessage = "配置重载成功";
                });

                // 延迟清除状态消息
                await Task.Delay(2000);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ReloadStatusMessage = "";
                    IsReloadingConfig = false;
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ReloadStatusMessage = $"配置重载失败: {ex.Message}";
                    IsReloadingConfig = false;
                    MessageBox.Show($"配置重载失败: {ex.Message}\n\n详细信息: {ex.InnerException?.Message}",
                                  "错误",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Error);
                });
            }
        }

        public override void Cleanup()
        {
            if (_ispProcessor?.IspCommonConfig != null)
            {
                _ispProcessor.IspCommonConfig.PropertyChanged -= OnCommonConfigChange;
            }
            _aeStep.PropertyChanged -= OnAEConfigChange;
            _blackLevelStep.PropertyChanged -= OnBlcConfigChange;
            _lenShadingStep.PropertyChanged -= OnLscConfigChange;
            _ddcStep.PropertyChanged -= OnDdcConfigChange;
            _awbStep.PropertyChanged -= OnAwbPropertyChanged;
            _ccmStep.PropertyChanged -= OnCcmPropertyChanged;
            _yGammaStep.PropertyChanged -= OnYGammaStepPropertyChanged;
            _eeStep.PropertyChanged -= OnEEPropertyChanged;
            _chStep.PropertyChanged -= OnCHPropertyChanged;
            _sajStep.PropertyChanged -= OnSAJPropertyChanged;
            _gainLevelStep.PropertyChanged -= OnGainLevelPropertyChanged;

            _curGainUpdateTimer?.Stop();
            _curGainUpdateTimer.Tick -= OnCurGainUpdateTimerTick;

            // 清理重连任务
            lock (_reconnectLock)
            {
                _reconnectCts?.Cancel();
                _reconnectCts?.Dispose();
                _reconnectCts = null;
            }

            MessengerInstance.Unregister(this);
        }
    }
}
