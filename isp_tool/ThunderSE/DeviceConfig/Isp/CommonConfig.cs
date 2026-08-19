using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;
using ThunderSE.Uvc;

namespace ThunderSE.DeviceConfig.Isp
{
    public enum BayerMode
    {
        RGRG,
        GRGR,
        BGBG,
        GBGB
    }

    /// <summary>
    /// 输出格式模式
    /// </summary>
    public enum SetMode
    {
        RAW10 = 0,   // RAW10 格式
        MJPG = 1,  // MJPEG 压缩格式
        RAW8 = 2,    // RAW8 格式
        YUV = 3,    // YUV 格式
    }


    /// <summary>
    /// 输出格式模式
    /// </summary>
    public enum FlipMode
    {
        None = 0,   // 无翻转
        Horizontal = 1,  // 水平翻转
        Vertical = 2,    // 垂直翻转
        Both = 3,    // 水平+垂直翻转
    }

    public struct Hvb_Adapt
    {
        public int pclk;
        public int v_len;
        public int step_val;
        public int step_max;
        public int down_fps_mode;//0,1,hvb down_fps; 2: exp down_fps,0xff: turn off down_fps
        public char fps;
        public char frequency;
    }

    public struct CommonData
    {
        public int exp_gain;
        //public int gain_max;
        //public int id;
        public int turn_gain;
        public int turn_exp;
        public int mclk;
        public Hvb_Adapt hvb;
        public short pixelw;
        public short pixelh;
        public char type;
        public char hsyn;
        public char vsyn;
        public char rduline;
        public char colrarray;
        public char pclk_fir_en;
        public char pclk_inv_en;
        public char csi_tun;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public char[] name;//最后一个字母是 ^
        public char exp_gain_en;
        public char blk_en;
        public char lsc_en;
        public char ddc_en;
        public char awb_en;
        public char ccm_en;
        public char dgain_en;
        public char ygama_en;
        public char rgb_gama_en;
        public char ch_en;
        public char vde_en;
        public char ee_en;
        public char cfd_en;
        public char saj_en;
        public char pclk_fir_class;
        public char avdd;
        public char dvdd;
        public char vddio;
        public char rotate;
        public char set_mode;  // 输出格式模式：0=RAW, 1=MJPG, 2=YUV
        public char gainLevel;
        public char iicInfor;
        public char mipiRxClkDiv;
        public short mipiDRperlane;
        public int mipiHbpTime;
        public int mipiHsaTime;
        public int icVer;
        public int ispVer;
        public int curGain;
        public byte rawScaleDown;  //RowScaleDown: bits 0-3; ColScaleDown: bits 4-7
    }

    public class CommonConfig : INotifyPropertyChanged
    {
        private BayerMode _bayer;
        private int _resolutionWidth = 1280;
        private int _resolutionHeight = 720;

        /// <summary>
        /// 分辨率宽度
        /// 设置时触发 PropertyChanged 事件，通知 LSC 等模块重新计算 BlockSize
        /// </summary>
        public int ResolutionWidth
        {
            get { return _resolutionWidth; }
            set
            {
                if (_resolutionWidth != value)
                {
                    _resolutionWidth = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResolutionWidth)));
                }
            }
        }

        /// <summary>
        /// 分辨率高度
        /// 设置时触发 PropertyChanged 事件，通知 LSC 等模块重新计算 BlockSize
        /// </summary>
        public int ResolutionHeight
        {
            get { return _resolutionHeight; }
            set
            {
                if (_resolutionHeight != value)
                {
                    _resolutionHeight = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResolutionHeight)));
                }
            }
        }
        private int _expGain = 0;
        //private int _gainMax = 0;
        private int _turnGain = 0;
        private int _turnExp = 0;
        private bool _isExpGainEnable = false;
        private int _isPclkFirEn = 0;
        private bool _isPclkInvEn = false;
        private byte _pclk_fir_class = 0;
        private byte _csi_tun = 0;
        private byte _hsyn = 0;
        private byte _vsyn = 0;
        private byte _type = 0;
        private Hvb_Adapt _hvb_adapt;
        private int _mclk = 0;
        private byte _rotate = 0;
        private byte _AVDD = 0;
        private byte _DVDD = 0;
        private byte _VDDIO = 0;
        private int _id = 0;
        private string _name = "";
        private SetMode _setMode = SetMode.RAW10;
        private string _deviceType = "";
        public byte _gainLevel = 0;
        public byte _iicInfor;
        public byte _mipiRxClkDiv;
        public short _mipiDRperlane;
        public int _mipiHbpTime;
        public int _mipiHsaTime;
        public int _icVer;
        public int _ispVer;
        public int _curGain;
        public byte _rawScaleDown;
        public bool SuppressScaleDownPropertyChanged { get; set; } = false;

        public CommonConfig()
        {
            ProcessorStepsEnables.OrderBy(item => item.Key);
            ProcessorStepsEnables.CollectionChanged += OnStepsEnablesChanged;

            PropertyNameToStructMemberMap = new Dictionary<string, string>()
            {
                {"ExpGain", "exp_gain"},
                {"TurnGain", "turn_gain"},
                {"TurnExp", "turn_exp"},
                {"IsExpGainEnable", "exp_gain_en"},
                {"IsPclkFirEn", "pclk_fir_en"},
                {"PclkFirClass", "pclk_fir_class"},
                {"IsPclkInvEn", "pclk_inv_en"},
                {"Mclk","mclk"},
                {"Rotate","rotate"},
                {"AVDD","avdd"},
                {"DVDD","dvdd"},
                {"VDDIO","vddio"},
                {"Id","id"},
                {"Name","name"},
                {"Pclk","hvb.pclk"},
                {"Vlen","hvb.v_len"},
                {"DownFpsMode","hvb.down_fps_mode"},
                {"Fps","hvb.fps"},
                {"Frequency","hvb.frequency"},
                {"CsiTun", "csi_tun"},
                {"Hsyn", "hsyn"},
                {"Vsyn", "vsyn"},
                {"IsBlcEnable","blk_en"},
                {"IsLscEnable","lsc_en"},
                {"IsDdcEnable","ddc_en"},
                {"IsAwbEnable","awb_en"},
                {"IsCcmEnable","ccm_en"},
                {"IsDgainEnable","dgain_en"},
                {"IsYGammaEnable","ygama_en"},
                {"IsRgbGammaEnable","rgb_gama_en"},
                {"IsChEnable","ch_en"},
                {"IsVdeEnable","vde_en"},
                {"IsEeEnable","ee_en"},
                {"IsCfdEnable","cfd_en"},
                {"IsSajEnable","saj_en"},
                {"SetMode","set_mode"},
                {"GainLevel","gainLevel"},
                {"MipiRxClkDiv","mipiRxClkDiv"},
                {"MipiDRperlane","mipiDRperlane"},
                {"MipiHbpTime","mipiHbpTime"},
                {"MipiHsaTime","mipiHsaTime"},
                {"IcVer","icVer"},
                {"ISPVer","ispVer"},
                {"CurGain","curGain"},
                {"RawScaleDown","rawScaleDown"}
            };
        }

        void OnStepsEnablesChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (PropertyChanged == null)
            {
                return;
            }

            string enablePropertyName = "";
            switch (((KeyValuePair<IspModule, bool>)e.NewItems[0]).Key)
            {
                case IspModule.Blc:
                    enablePropertyName = "IsBlcEnable";
                    break;
                case IspModule.Lsc:
                    enablePropertyName = "IsLscEnable";
                    break;
                case IspModule.Ddc:
                    enablePropertyName = "IsDdcEnable";
                    break;
                case IspModule.Awb:
                    enablePropertyName = "IsAwbEnable";
                    break;
                case IspModule.Ccm:
                    enablePropertyName = "IsCcmEnable";
                    break;
                case IspModule.Dgain:
                    enablePropertyName = "IsDgainEnable";
                    break;
                case IspModule.YGamma:
                    enablePropertyName = "IsYGammaEnable";
                    break;
                case IspModule.RgbGamma:
                    enablePropertyName = "IsRgbGammaEnable";
                    break;
                case IspModule.Ch:
                    enablePropertyName = "IsChEnable";
                    break;
                case IspModule.Vde:
                    enablePropertyName = "IsVdeEnable";
                    break;
                case IspModule.Ee:
                    enablePropertyName = "IsEeEnable";
                    break;
                case IspModule.Cfd:
                    enablePropertyName = "IsCfdEnable";
                    break;
                case IspModule.Saj:
                    enablePropertyName = "IsSajEnable";
                    break;
                case IspModule.GainLevel:
                case IspModule.GammaTable:
                case IspModule.AE:
                    // These modules don't have corresponding IsXxxEnable properties
                    return;
                default:
                    throw new Exception($"Cannot find property name for ISP module: {((KeyValuePair<IspModule, bool>)e.NewItems[0]).Key}");
            }

            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(enablePropertyName));
            }
        }


        public Dictionary<string, string> PropertyNameToStructMemberMap
        {
            get;
            set;
        }

        public string DeviceType
        {
            get { return _deviceType; }
            set
            {
                _deviceType = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("DeviceType"));
            }
        }

        public BayerMode Bayer
        {
            get { return _bayer; }
            set
            {
                _bayer = value;
                
                // 同步到 UvcReceiver
                try
                {
                    Uvc.UvcReceiver.Instance.BayerMode = value;
                }
                catch
                {
                    // 忽略同步失败（UvcReceiver 可能未初始化）
                }
                
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Bayer"));
            }
        }
        public int ExpGain
        {
            get { return _expGain; }
            set
            {
                _expGain = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("ExpGain"));
            }
        }

        public int TurnGain
        {
            get { return _turnGain; }
            set
            {
                _turnGain = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("TurnGain"));
            }
        }


        public int TurnExp
        {
            get { return _turnExp; }
            set
            {
                _turnExp = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("TurnExp"));
            }
        }

        public bool IsExpGainEnable
        {
            get { return _isExpGainEnable; }
            set
            {
                _isExpGainEnable = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("IsExpGainEnable"));
            }
        }
        public int IsPclkFirEn
        {
            get { return _isPclkFirEn; }
            set
            {
                _isPclkFirEn = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("IsPclkFirEn"));
            }
        }

        public byte PclkFirClass
        {
            get { return _pclk_fir_class; }
            set
            {
                _pclk_fir_class = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("PclkFirClass"));
            }
        }

        public bool IsPclkInvEn
        {
            get { return _isPclkInvEn; }
            set
            {
                _isPclkInvEn = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("IsPclkInvEn"));
            }
        }

        public int Mclk
        {
            get { return _mclk; }
            set
            {
                _mclk = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Mclk"));
            }
        }

        public byte Rotate
        {
            get { return _rotate; }
            set
            {
                _rotate = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Rotate"));
            }
        }

        public byte AVDD
        {
            get { return _AVDD; }
            set
            {
                _AVDD = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("AVDD"));
            }
        }

        public byte DVDD
        {
            get { return _DVDD; }
            set
            {
                _DVDD = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("DVDD"));
            }
        }

        public byte VDDIO
        {
            get { return _VDDIO; }
            set
            {
                _VDDIO = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("VDDIO"));
            }
        }

        public int Id
        {
            get { return _id; }
            set
            {
                _id = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Id"));
            }
        }

        public string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Name"));
            }
        }

        /// <summary>
        /// 输出格式模式（RAW/MJPG/YUV）
        /// </summary>
        public SetMode SetMode
        {
            get { return _setMode; }
            set
            {
                _setMode = value;
                
                // 同步到 UvcReceiver
                try
                {
                    Uvc.UvcReceiver.Instance.SetMode = value;
                }
                catch
                {
                    // 忽略同步失败（UvcReceiver 可能未初始化）
                }
                
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("SetMode"));
            }
        }

        public byte GainLevel
        {
            get { return _gainLevel; }
            set
            {
                _gainLevel = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("GainLevel"));
            }
        }

        public byte CsiTun
        {
            get { return _csi_tun; }
            set
            {
                _csi_tun = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("CsiTun"));
            }
        }

        public byte Hsyn
        {
            get { return _hsyn; }
            set
            {
                _hsyn = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Hsyn"));
            }
        }

        public byte Vsyn
        {
            get { return _vsyn; }
            set
            {
                _vsyn = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Vsyn"));
            }
        }

        public int Vlen
        {
            get { return _hvb_adapt.v_len; }
            set
            {
                _hvb_adapt.v_len = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Vlen"));
            }
        }

        public int DownFpsMode
        {
            get { return _hvb_adapt.down_fps_mode; }
            set
            {
                _hvb_adapt.down_fps_mode = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("DownFpsMode"));
            }
        }

        public byte Fps
        {
            get { return Convert.ToByte(_hvb_adapt.fps); }
            set
            {
                _hvb_adapt.fps = Convert.ToChar(value);
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Fps"));
            }
        }

        public byte Frequency
        {
            get { return Convert.ToByte(_hvb_adapt.frequency); }
            set
            {
                _hvb_adapt.frequency = Convert.ToChar(value);
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Frequency"));
            }
        }

        public int Pclk
        {
            get { return _hvb_adapt.pclk; }
            set
            {
                _hvb_adapt.pclk = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Pclk"));
            }
        }

        public byte Type
        {
            get { return _type; }
            set
            {
                _type = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Type"));
            }
        }

        public byte IICInfor
        {
            get { return _iicInfor; }
            set
            {
                _iicInfor = value;
                //if (PropertyChanged != null)
                //    PropertyChanged(this, new PropertyChangedEventArgs("IICInfor"));
            }
        }

        public byte MipiRxClkDiv
        {
            get { return _mipiRxClkDiv; }
            set
            {
                _mipiRxClkDiv = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("MipiRxClkDiv"));
            }
        }

        public short MipiDRperlane
        {
            get { return _mipiDRperlane; }
            set
            {
                _mipiDRperlane = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("MipiDRperlane"));
            }
        }

        public int MipiHbpTime
        {
            get { return _mipiHbpTime; }
            set
            {
                _mipiHbpTime = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("MipiHbpTime"));
            }
        }

        public int MipiHsaTime
        {
            get { return _mipiHsaTime; }
            set
            {
                _mipiHsaTime = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("MipiHsaTime"));
            }
        }

        public int IcVer
        {
            get { return _icVer; }
            set
            {
                _icVer = value;
                //if (PropertyChanged != null)
                //    PropertyChanged(this, new PropertyChangedEventArgs("IcVer"));
            }
        }

        public int ISPVer
        {
            get { return _ispVer; }
            set
            {
                _ispVer = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("ISPVer"));
            }
        }

        public int CurGain
        {
            get { return _curGain; }
            set
            {
                _curGain = value;
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("CurGain"));
            }
        }

        public byte RawScaleDown
        {
            get { return _rawScaleDown; }
            set
            {
                _rawScaleDown = value;
                if (!SuppressScaleDownPropertyChanged)
                {
                    UvcApi.SetRawScaleDown(_rawScaleDown);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("RawScaleDown"));
                    //PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("RawColScaleDown"));
                }
            }
        }

        public byte RawColScaleDown
        {
            get { return (byte)((_rawScaleDown >> 4) & 0x0F); }
            set
            {
                _rawScaleDown = (byte)((_rawScaleDown & 0x0F) | ((value & 0x0F) << 4));
                if (!SuppressScaleDownPropertyChanged)
                {
                    UvcApi.SetRawScaleDown(_rawScaleDown);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("RawScaleDown"));
                    //PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("RawColScaleDown"));
                }
            }
        }

        public Dictionary<IspModule, char> ProcessorStepsEnablesActualValueMap = new Dictionary<IspModule, char>
        {
            {IspModule.Blc, (char)0x01},
            {IspModule.Lsc, (char)0x01},
            {IspModule.Ddc, (char)0x02},
            {IspModule.Awb, (char)0x02},
            {IspModule.Ccm, (char)0x02},
            {IspModule.Dgain, (char)0x02},
            {IspModule.YGamma, (char)0x02},
            {IspModule.RgbGamma, (char)0x02},
            {IspModule.Ch, (char)0x02},
            {IspModule.Vde, (char)0x02},
            {IspModule.Ee, (char)0x02},
            {IspModule.Cfd, (char)0x02},
            {IspModule.Saj, (char)0x02}
        };

        public ObservableCollection<KeyValuePair<IspModule, bool>> ProcessorStepsEnables = new ObservableCollection<KeyValuePair<IspModule, bool>>
        {
            //new KeyValuePair<IspModule, bool>(IspModule.AE, true),
            //new KeyValuePair<IspModule, bool>(IspModule.Blc, true),
            //new KeyValuePair<IspModule, bool>(IspModule.Lsc, true),
            //new KeyValuePair<IspModule, bool>(IspModule.Ddc, true),
            //new KeyValuePair<IspModule, bool>(IspModule.Awb, true),
            //new KeyValuePair<IspModule, bool>(IspModule.Ccm, true),
            //new KeyValuePair<IspModule, bool>(IspModule.Dgain, true),
            //new KeyValuePair<IspModule, bool>(IspModule.YGamma, true),
            //new KeyValuePair<IspModule, bool>(IspModule.RgbGamma, true),
            //new KeyValuePair<IspModule, bool>(IspModule.Ch, true),
            //new KeyValuePair<IspModule, bool>(IspModule.Vde, true),
            //new KeyValuePair<IspModule, bool>(IspModule.Ee, true),
            //new KeyValuePair<IspModule, bool>(IspModule.Cfd, true),
            //new KeyValuePair<IspModule, bool>(IspModule.Saj, true),
            //new KeyValuePair<IspModule, bool>(IspModule.GainLevel, true)
            
            new KeyValuePair<IspModule, bool>(IspModule.AE, false),
            new KeyValuePair<IspModule, bool>(IspModule.Blc, false),
            new KeyValuePair<IspModule, bool>(IspModule.Lsc, false),
            new KeyValuePair<IspModule, bool>(IspModule.Ddc, false),
            new KeyValuePair<IspModule, bool>(IspModule.Awb, false),
            new KeyValuePair<IspModule, bool>(IspModule.Ccm, false),
            new KeyValuePair<IspModule, bool>(IspModule.Dgain, false),
            new KeyValuePair<IspModule, bool>(IspModule.YGamma, false),
            new KeyValuePair<IspModule, bool>(IspModule.RgbGamma, false),
            new KeyValuePair<IspModule, bool>(IspModule.Ch, false),
            new KeyValuePair<IspModule, bool>(IspModule.Vde, false),
            new KeyValuePair<IspModule, bool>(IspModule.Ee, false),
            new KeyValuePair<IspModule, bool>(IspModule.Cfd, false),
            new KeyValuePair<IspModule, bool>(IspModule.Saj, false),
            new KeyValuePair<IspModule, bool>(IspModule.GainLevel, false),
            new KeyValuePair<IspModule, bool>(IspModule.GammaTable, false)
        };

        public Dictionary<int, byte[]> ParamsDataCollection
        {
            get
            {
                CommonData commonDataParams = new CommonData()
                {
                    pixelh = (short)ResolutionHeight,
                    pixelw = (short)ResolutionWidth,
                    colrarray = (char)(int)Bayer,
                    exp_gain = ExpGain,
                    turn_gain = TurnGain,
                    turn_exp = TurnExp,
                    exp_gain_en = Convert.ToChar(Convert.ToInt32(IsExpGainEnable)),
                    pclk_fir_en = Convert.ToChar(IsPclkFirEn),
                    pclk_inv_en = Convert.ToChar(Convert.ToInt32(IsPclkInvEn)),
                    pclk_fir_class = Convert.ToChar(Convert.ToInt32(PclkFirClass)),
                    csi_tun = Convert.ToChar(Convert.ToInt32(CsiTun)),
                    hsyn = Convert.ToChar(Convert.ToInt32(Hsyn)),
                    vsyn = Convert.ToChar(Convert.ToInt32(Vsyn)),
                    hvb = _hvb_adapt,
                    mclk = Mclk,
                    rotate = Convert.ToChar(Convert.ToInt32(Rotate)),
                    avdd = Convert.ToChar(Convert.ToInt32(AVDD)),
                    dvdd = Convert.ToChar(Convert.ToInt32(DVDD)),
                    vddio = Convert.ToChar(Convert.ToInt32(VDDIO)),
                    //id = Id,
                    name = Name.ToCharArray(),
                    blk_en = ProcessorStepsEnables[(int)(IspModule.Blc)].Value ? (char)0x00 : ProcessorStepsEnablesActualValueMap[IspModule.Blc],
                    lsc_en = ProcessorStepsEnables[(int)(IspModule.Lsc)].Value ? (char)0x00 : ProcessorStepsEnablesActualValueMap[IspModule.Lsc],
                    ddc_en = ProcessorStepsEnables[(int)(IspModule.Ddc)].Value ? (char)0x00 : ProcessorStepsEnablesActualValueMap[IspModule.Ddc],
                    awb_en = ProcessorStepsEnables[(int)(IspModule.Awb)].Value ? (char)0x00 : ProcessorStepsEnablesActualValueMap[IspModule.Awb],
                    ccm_en = ProcessorStepsEnables[(int)(IspModule.Ccm)].Value ? (char)0x00 : ProcessorStepsEnablesActualValueMap[IspModule.Ccm],
                    dgain_en = ProcessorStepsEnables[(int)(IspModule.Dgain)].Value ? (char)0x00 : ProcessorStepsEnablesActualValueMap[IspModule.Dgain],
                    ygama_en = ProcessorStepsEnables[(int)(IspModule.YGamma)].Value ? (char)0x00 : ProcessorStepsEnablesActualValueMap[IspModule.YGamma],
                    rgb_gama_en = ProcessorStepsEnables[(int)(IspModule.RgbGamma)].Value ? (char)0x00 : ProcessorStepsEnablesActualValueMap[IspModule.RgbGamma],
                    ch_en = ProcessorStepsEnables[(int)(IspModule.Ch)].Value ? (char)0x00 : ProcessorStepsEnablesActualValueMap[IspModule.Ch],
                    vde_en = ProcessorStepsEnables[(int)(IspModule.Vde)].Value ? (char)0x00 : ProcessorStepsEnablesActualValueMap[IspModule.Vde],
                    ee_en = ProcessorStepsEnables[(int)(IspModule.Ee)].Value ? (char)0x00 : ProcessorStepsEnablesActualValueMap[IspModule.Ee],
                    cfd_en = ProcessorStepsEnables[(int)(IspModule.Cfd)].Value ? (char)0x00 : ProcessorStepsEnablesActualValueMap[IspModule.Cfd],
                    saj_en = ProcessorStepsEnables[(int)(IspModule.Saj)].Value ? (char)0x00 : ProcessorStepsEnablesActualValueMap[IspModule.Saj],
                    set_mode = (char)SetMode,
                    gainLevel = Convert.ToChar(Convert.ToInt32(GainLevel)),
                    iicInfor = Convert.ToChar(IICInfor),
                    mipiRxClkDiv = Convert.ToChar(MipiRxClkDiv),
                    mipiDRperlane = MipiDRperlane,
                    mipiHbpTime = MipiHbpTime,
                    mipiHsaTime = MipiHsaTime,
                    icVer = IcVer,
                    ispVer = ISPVer,
                    curGain = CurGain,
                    rawScaleDown = RawScaleDown
                };

                int size = Marshal.SizeOf(commonDataParams);
                byte[] arr = new byte[size];

                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(commonDataParams, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                Marshal.FreeHGlobal(ptr);

                return new Dictionary<int, byte[]>() { { 0, arr } };
            }
            set
            {
                CommonData commonDataParams = new CommonData();

                int size = Marshal.SizeOf(commonDataParams);
                IntPtr ptr = Marshal.AllocHGlobal(size);

                Marshal.Copy(value[0], 0, ptr, size);

                commonDataParams = (CommonData)Marshal.PtrToStructure(ptr, commonDataParams.GetType());
                Marshal.FreeHGlobal(ptr);

                ResolutionHeight = commonDataParams.pixelh;
                ResolutionWidth = commonDataParams.pixelw;

                _hvb_adapt = commonDataParams.hvb;
                Bayer = (BayerMode)commonDataParams.colrarray;
                ExpGain = commonDataParams.exp_gain;
                TurnGain = commonDataParams.turn_gain;
                TurnExp = commonDataParams.turn_exp;
                IsExpGainEnable = Convert.ToBoolean(Convert.ToInt32(commonDataParams.exp_gain_en));
                IsPclkFirEn = Convert.ToInt32(commonDataParams.pclk_fir_en);
                IsPclkInvEn = Convert.ToBoolean(Convert.ToInt32(commonDataParams.pclk_inv_en));
                PclkFirClass = Convert.ToByte(commonDataParams.pclk_fir_class);
                CsiTun = Convert.ToByte(commonDataParams.csi_tun);
                Hsyn = Convert.ToByte(Convert.ToInt32(commonDataParams.hsyn));
                Vsyn = Convert.ToByte(Convert.ToInt32(commonDataParams.vsyn));
                Mclk = commonDataParams.mclk;
                Rotate = Convert.ToByte(commonDataParams.rotate);
                AVDD = Convert.ToByte(commonDataParams.avdd);
                DVDD = Convert.ToByte(commonDataParams.dvdd);
                VDDIO = Convert.ToByte(commonDataParams.vddio);
                //Id = commonDataParams.id;
                Name = new string(commonDataParams.name);
                Name = Name.Replace("\0", string.Empty);
                SetMode = (SetMode)commonDataParams.set_mode;
                GainLevel = Convert.ToByte(commonDataParams.gainLevel);
                IICInfor = Convert.ToByte(commonDataParams.iicInfor);
                MipiRxClkDiv = Convert.ToByte(commonDataParams.mipiRxClkDiv);
                MipiDRperlane = Convert.ToInt16(commonDataParams.mipiDRperlane);
                MipiHbpTime = Convert.ToInt32(commonDataParams.mipiHbpTime);
                MipiHsaTime = Convert.ToInt32(commonDataParams.mipiHsaTime);
                IcVer = Convert.ToInt32(commonDataParams.icVer);
                ISPVer = Convert.ToInt32(commonDataParams.ispVer);
                CurGain = Convert.ToInt32(commonDataParams.curGain);
                RawScaleDown = commonDataParams.rawScaleDown;

                //Console.WriteLine("Deserialized CommonConfig:"+ string.Join(",", GainLevel));

                bool blcEnabled = Convert.ToBoolean(Convert.ToInt32(commonDataParams.blk_en));
                ProcessorStepsEnables[(int)IspModule.Blc] = new KeyValuePair<IspModule, bool>(IspModule.Blc, blcEnabled);
                if (blcEnabled) ProcessorStepsEnablesActualValueMap[IspModule.Blc] = Convert.ToChar(commonDataParams.blk_en);

                bool lscEnabled = Convert.ToBoolean(Convert.ToInt32(commonDataParams.lsc_en));
                ProcessorStepsEnables[(int)IspModule.Lsc] = new KeyValuePair<IspModule, bool>(IspModule.Lsc, lscEnabled);
                if (lscEnabled) ProcessorStepsEnablesActualValueMap[IspModule.Lsc] = Convert.ToChar(commonDataParams.lsc_en);

                bool ddcEnabled = Convert.ToBoolean(Convert.ToInt32(commonDataParams.ddc_en));
                ProcessorStepsEnables[(int)IspModule.Ddc] = new KeyValuePair<IspModule, bool>(IspModule.Ddc, ddcEnabled);
                if (ddcEnabled) ProcessorStepsEnablesActualValueMap[IspModule.Ddc] = Convert.ToChar(commonDataParams.ddc_en);

                bool awbEnabled = Convert.ToBoolean(Convert.ToInt32(commonDataParams.awb_en));
                ProcessorStepsEnables[(int)IspModule.Awb] = new KeyValuePair<IspModule, bool>(IspModule.Awb, awbEnabled);
                if (awbEnabled) ProcessorStepsEnablesActualValueMap[IspModule.Awb] = Convert.ToChar(commonDataParams.awb_en);

                bool ccmEnabled = Convert.ToBoolean(Convert.ToInt32(commonDataParams.ccm_en));
                ProcessorStepsEnables[(int)IspModule.Ccm] = new KeyValuePair<IspModule, bool>(IspModule.Ccm, ccmEnabled);
                if (ccmEnabled) ProcessorStepsEnablesActualValueMap[IspModule.Ccm] = Convert.ToChar(commonDataParams.ccm_en);

                bool dgainEnabled = Convert.ToBoolean(Convert.ToInt32(commonDataParams.dgain_en));
                ProcessorStepsEnables[(int)IspModule.Dgain] = new KeyValuePair<IspModule, bool>(IspModule.Dgain, dgainEnabled);
                if (dgainEnabled) ProcessorStepsEnablesActualValueMap[IspModule.Dgain] = Convert.ToChar(commonDataParams.dgain_en);

                bool yGammaEnabled = Convert.ToBoolean(Convert.ToInt32(commonDataParams.ygama_en));
                ProcessorStepsEnables[(int)IspModule.YGamma] = new KeyValuePair<IspModule, bool>(IspModule.YGamma, yGammaEnabled);
                if (yGammaEnabled) ProcessorStepsEnablesActualValueMap[IspModule.YGamma] = Convert.ToChar(commonDataParams.ygama_en);

                bool rgbGammaEnabled = Convert.ToBoolean(Convert.ToInt32(commonDataParams.rgb_gama_en));
                ProcessorStepsEnables[(int)IspModule.RgbGamma] = new KeyValuePair<IspModule, bool>(IspModule.RgbGamma, rgbGammaEnabled);
                if (rgbGammaEnabled) ProcessorStepsEnablesActualValueMap[IspModule.RgbGamma] = Convert.ToChar(commonDataParams.rgb_gama_en);

                bool chEnabled = Convert.ToBoolean(Convert.ToInt32(commonDataParams.ch_en));
                ProcessorStepsEnables[(int)IspModule.Ch] = new KeyValuePair<IspModule, bool>(IspModule.Ch, chEnabled);
                if (chEnabled) ProcessorStepsEnablesActualValueMap[IspModule.Ch] = Convert.ToChar(commonDataParams.ch_en);

                bool vdeEnabled = Convert.ToBoolean(Convert.ToInt32(commonDataParams.vde_en));
                ProcessorStepsEnables[(int)IspModule.Vde] = new KeyValuePair<IspModule, bool>(IspModule.Vde, vdeEnabled);
                if (vdeEnabled) ProcessorStepsEnablesActualValueMap[IspModule.Vde] = Convert.ToChar(commonDataParams.vde_en);

                bool eeEnabled = Convert.ToBoolean(Convert.ToInt32(commonDataParams.ee_en));
                ProcessorStepsEnables[(int)IspModule.Ee] = new KeyValuePair<IspModule, bool>(IspModule.Ee, eeEnabled);
                if (eeEnabled) ProcessorStepsEnablesActualValueMap[IspModule.Ee] = Convert.ToChar(commonDataParams.ee_en);

                bool cfdEnabled = Convert.ToBoolean(Convert.ToInt32(commonDataParams.cfd_en));
                ProcessorStepsEnables[(int)IspModule.Cfd] = new KeyValuePair<IspModule, bool>(IspModule.Cfd, cfdEnabled);
                if (cfdEnabled) ProcessorStepsEnablesActualValueMap[IspModule.Cfd] = Convert.ToChar(commonDataParams.cfd_en);

                bool sajEnabled = Convert.ToBoolean(Convert.ToInt32(commonDataParams.saj_en));
                ProcessorStepsEnables[(int)IspModule.Saj] = new KeyValuePair<IspModule, bool>(IspModule.Saj, sajEnabled);
                if (sajEnabled) ProcessorStepsEnablesActualValueMap[IspModule.Saj] = Convert.ToChar(commonDataParams.saj_en);
            }
        }

        public XmlElement SerializeCommonConfigToXmlNode(XmlDocument xmlDoc, List<Isp.IspModule> ispModuleListToSerialize)
        {
            var commonConfigNode = xmlDoc.CreateElement("IspCommonConfig");

            XmlElement bayerNode = xmlDoc.CreateElement("Bayer");
            bayerNode.AppendChild(xmlDoc.CreateTextNode(Bayer.ToString()));
            commonConfigNode.AppendChild(bayerNode);

            XmlElement resolutionWidthNode = xmlDoc.CreateElement("ResolutionWidth");
            resolutionWidthNode.AppendChild(xmlDoc.CreateTextNode(ResolutionWidth.ToString()));
            commonConfigNode.AppendChild(resolutionWidthNode);

            XmlElement resolutionHeightNode = xmlDoc.CreateElement("ResolutionHeight");
            resolutionHeightNode.AppendChild(xmlDoc.CreateTextNode(ResolutionHeight.ToString()));
            commonConfigNode.AppendChild(resolutionHeightNode);

            XmlElement expGainNode = xmlDoc.CreateElement("ExpGain");
            expGainNode.AppendChild(xmlDoc.CreateTextNode(ExpGain.ToString()));
            commonConfigNode.AppendChild(expGainNode);

            XmlElement turnGainNode = xmlDoc.CreateElement("TurnGain");
            turnGainNode.AppendChild(xmlDoc.CreateTextNode(TurnGain.ToString()));
            commonConfigNode.AppendChild(turnGainNode);

            XmlElement turnExpNode = xmlDoc.CreateElement("TurnExp");
            turnExpNode.AppendChild(xmlDoc.CreateTextNode(TurnExp.ToString()));
            commonConfigNode.AppendChild(turnExpNode);

            XmlElement isExpGainEnableNode = xmlDoc.CreateElement("IsExpGainEnable");
            isExpGainEnableNode.AppendChild(xmlDoc.CreateTextNode(IsExpGainEnable.ToString()));
            commonConfigNode.AppendChild(isExpGainEnableNode);

            XmlElement isPclkFirEnNode = xmlDoc.CreateElement("IsPclkFirEn");
            isPclkFirEnNode.AppendChild(xmlDoc.CreateTextNode(IsPclkFirEn.ToString()));
            commonConfigNode.AppendChild(isPclkFirEnNode);

            XmlElement pclkFirClassNode = xmlDoc.CreateElement("PclkFirClass");
            pclkFirClassNode.AppendChild(xmlDoc.CreateTextNode(PclkFirClass.ToString()));
            commonConfigNode.AppendChild(pclkFirClassNode);

            XmlElement isPclkInvEnNode = xmlDoc.CreateElement("IsPclkInvEn");
            isPclkInvEnNode.AppendChild(xmlDoc.CreateTextNode(IsPclkInvEn.ToString()));
            commonConfigNode.AppendChild(isPclkInvEnNode);

            XmlElement mclkNode = xmlDoc.CreateElement("Mclk");
            mclkNode.AppendChild(xmlDoc.CreateTextNode(Mclk.ToString()));
            commonConfigNode.AppendChild(mclkNode);

            XmlElement rotateNode = xmlDoc.CreateElement("Rotate");
            rotateNode.AppendChild(xmlDoc.CreateTextNode(Rotate.ToString()));
            commonConfigNode.AppendChild(rotateNode);

            XmlElement avddNode = xmlDoc.CreateElement("AVDD");
            avddNode.AppendChild(xmlDoc.CreateTextNode(AVDD.ToString()));
            commonConfigNode.AppendChild(avddNode);

            XmlElement dvddNode = xmlDoc.CreateElement("DVDD");
            dvddNode.AppendChild(xmlDoc.CreateTextNode(DVDD.ToString()));
            commonConfigNode.AppendChild(dvddNode);

            XmlElement vddioNode = xmlDoc.CreateElement("VDDIO");
            vddioNode.AppendChild(xmlDoc.CreateTextNode(VDDIO.ToString()));
            commonConfigNode.AppendChild(vddioNode);

            //XmlElement idNode = xmlDoc.CreateElement("Id");
            //idNode.AppendChild(xmlDoc.CreateTextNode(Id.ToString()));
            //commonConfigNode.AppendChild(idNode);

            XmlElement nameNode = xmlDoc.CreateElement("Name");
            nameNode.AppendChild(xmlDoc.CreateTextNode(Name));
            commonConfigNode.AppendChild(nameNode);

            XmlElement setModeNode = xmlDoc.CreateElement("SetMode");
            setModeNode.AppendChild(xmlDoc.CreateTextNode(SetMode.ToString()));
            commonConfigNode.AppendChild(setModeNode);

            XmlElement csiTunNode = xmlDoc.CreateElement("CsiTun");
            csiTunNode.AppendChild(xmlDoc.CreateTextNode(CsiTun.ToString()));
            commonConfigNode.AppendChild(csiTunNode);

            XmlElement hsynNode = xmlDoc.CreateElement("Hsyn");
            hsynNode.AppendChild(xmlDoc.CreateTextNode(Hsyn.ToString()));
            commonConfigNode.AppendChild(hsynNode);

            XmlElement vsynNode = xmlDoc.CreateElement("Vsyn");
            vsynNode.AppendChild(xmlDoc.CreateTextNode(Vsyn.ToString()));
            commonConfigNode.AppendChild(vsynNode);

            XmlElement typeNode = xmlDoc.CreateElement("Type");
            typeNode.AppendChild(xmlDoc.CreateTextNode(Type.ToString()));
            commonConfigNode.AppendChild(typeNode);

            XmlElement pclkNode = xmlDoc.CreateElement("Pclk");
            pclkNode.AppendChild(xmlDoc.CreateTextNode(Pclk.ToString()));
            commonConfigNode.AppendChild(pclkNode);

            XmlElement vlenNode = xmlDoc.CreateElement("Vlen");
            vlenNode.AppendChild(xmlDoc.CreateTextNode(Vlen.ToString()));
            commonConfigNode.AppendChild(vlenNode);

            XmlElement downFpsModeNode = xmlDoc.CreateElement("DownFpsMode");
            downFpsModeNode.AppendChild(xmlDoc.CreateTextNode(DownFpsMode.ToString()));
            commonConfigNode.AppendChild(downFpsModeNode);

            XmlElement fpsNode = xmlDoc.CreateElement("Fps");
            fpsNode.AppendChild(xmlDoc.CreateTextNode(Fps.ToString()));
            commonConfigNode.AppendChild(fpsNode);

            XmlElement frequencyNode = xmlDoc.CreateElement("Frequency");
            frequencyNode.AppendChild(xmlDoc.CreateTextNode(Frequency.ToString()));
            commonConfigNode.AppendChild(frequencyNode);

            XmlElement deviceTypeNode = xmlDoc.CreateElement("DeviceType");
            deviceTypeNode.AppendChild(xmlDoc.CreateTextNode(DeviceType));
            commonConfigNode.AppendChild(deviceTypeNode);


            XmlElement ProcessorStepsEnablesNode = xmlDoc.CreateElement("ProcessorStepsEnables");
            foreach (var ispModule in ispModuleListToSerialize)
            {
                if (ispModule == IspModule.AE)
                {
                    continue;
                }

                var moduleEnableNode = xmlDoc.CreateElement(ispModule.ToString());
                moduleEnableNode.AppendChild(xmlDoc.CreateTextNode(ProcessorStepsEnables.First(item => item.Key == ispModule).Value.ToString()));
                ProcessorStepsEnablesNode.AppendChild(moduleEnableNode);
            }

            commonConfigNode.AppendChild(ProcessorStepsEnablesNode);

            return commonConfigNode;
        }

        public void DeserializeFromXmlElement(XmlElement ispToolDataNode, List<Isp.IspModule> ispModuleListToDeserialize)
        {
            var ispCommonConfigNode = ispToolDataNode["IspCommonConfig"];
            if (ispCommonConfigNode != null)
            {
                Bayer = (BayerMode)Enum.Parse(typeof(BayerMode), ispCommonConfigNode["Bayer"].FirstChild.Value);
                ResolutionWidth = Convert.ToInt32(ispCommonConfigNode["ResolutionWidth"].FirstChild.Value);
                ResolutionHeight = Convert.ToInt16(ispCommonConfigNode["ResolutionHeight"].FirstChild.Value);

                ExpGain = XmlHelper.GetNodeInt(ispCommonConfigNode, "ExpGain", ExpGain);
                TurnGain = XmlHelper.GetNodeInt(ispCommonConfigNode, "TurnGain", TurnGain);
                TurnExp = XmlHelper.GetNodeInt(ispCommonConfigNode, "TurnExp", TurnExp);
                IsExpGainEnable = XmlHelper.GetNodeBool(ispCommonConfigNode, "IsExpGainEnable", IsExpGainEnable);
                IsPclkFirEn = XmlHelper.GetNodeInt(ispCommonConfigNode, "IsPclkFirEn", IsPclkFirEn);
                PclkFirClass = Convert.ToByte(XmlHelper.GetNodeInt(ispCommonConfigNode, "PclkFirClass", PclkFirClass));
                IsPclkInvEn = XmlHelper.GetNodeBool(ispCommonConfigNode, "IsPclkInvEn", IsPclkInvEn);
                Mclk = XmlHelper.GetNodeInt(ispCommonConfigNode, "Mclk", Mclk);
                Rotate = Convert.ToByte(XmlHelper.GetNodeInt(ispCommonConfigNode, "Rotate", Rotate));
                AVDD = Convert.ToByte(XmlHelper.GetNodeInt(ispCommonConfigNode, "AVDD", AVDD));
                DVDD = Convert.ToByte(XmlHelper.GetNodeInt(ispCommonConfigNode, "DVDD", DVDD));
                VDDIO = Convert.ToByte(XmlHelper.GetNodeInt(ispCommonConfigNode, "VDDIO", VDDIO));
                //Id = XmlHelper.GetNodeInt(ispCommonConfigNode, "Id", Id);
                Name = XmlHelper.GetNodeValue(ispCommonConfigNode, "Name", Name);
                SetMode = (SetMode)Enum.Parse(typeof(SetMode), XmlHelper.GetNodeValue(ispCommonConfigNode, "SetMode", SetMode.ToString()));
                CsiTun = Convert.ToByte(XmlHelper.GetNodeInt(ispCommonConfigNode, "CsiTun", CsiTun));
                Hsyn = Convert.ToByte(XmlHelper.GetNodeInt(ispCommonConfigNode, "Hsyn", Hsyn));
                Vsyn = Convert.ToByte(XmlHelper.GetNodeInt(ispCommonConfigNode, "Vsyn", Vsyn));
                Type = Convert.ToByte(XmlHelper.GetNodeInt(ispCommonConfigNode, "Type", Type));
                Pclk = XmlHelper.GetNodeInt(ispCommonConfigNode, "Pclk", Pclk);
                Vlen = XmlHelper.GetNodeInt(ispCommonConfigNode, "Vlen", Vlen);
                DownFpsMode = XmlHelper.GetNodeInt(ispCommonConfigNode, "DownFpsMode", DownFpsMode);
                Fps = Convert.ToByte(XmlHelper.GetNodeInt(ispCommonConfigNode, "Fps", Fps));
                Frequency = Convert.ToByte(XmlHelper.GetNodeInt(ispCommonConfigNode, "Frequency", Frequency));
                DeviceType = XmlHelper.GetNodeValue(ispCommonConfigNode, "DeviceType", DeviceType);

                foreach (var ispModule in ispModuleListToDeserialize)
                {
                    if (ispModule == IspModule.AE)
                    {
                        continue;
                    }

                    ProcessorStepsEnables[(int)ispModule] = new KeyValuePair<IspModule, bool>(ispModule,
                        Convert.ToBoolean(ispCommonConfigNode["ProcessorStepsEnables"][ispModule.ToString()].FirstChild.Value));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
