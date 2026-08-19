using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using ThunderSE.Common;

namespace ThunderSE.DeviceConfig.Isp
{
    using System.Runtime.InteropServices;
    using System.Xml;
    using ThunderSE.DeviceConfig;
    using WhiteBalanceStatCollection = ObservableCollection<KeyValuePair<double, double>>;

    public class AutoWhiteBalance : ProcessStep, INotifyPropertyChanged
    {
        private Dictionary<string, KeyValuePair<int, int>> _gainData = new Dictionary<string, KeyValuePair<int, int>>();
        private ObservableCollection<WhiteBalanceStatCollection> _statisticData = new ObservableCollection<WhiteBalanceStatCollection>();

        private ObservableCollection<string> _curveLabels = new ObservableCollection<string>();

        // 智能插值模式：关键点数据（用户只需编辑8个点）
        private ObservableCollection<ObservableCollection<KeyValuePair<double, double>>> _keyPointData =
            new ObservableCollection<ObservableCollection<KeyValuePair<double, double>>>();
        private bool _smartInterpolationEnabled = true;
        private int _keyPointCountPerCurve = 8;

        private int _awb_de_high_red_class = 3;
        private int _awb_de_high_blue_class = 3;
        private int _awb_de_high_red_rate = 0;
        private int _awb_de_high_blue_rate = 0;

        private int _mixPixSum = 256;
        private int _seg_mode = 3;
        private int _awb_weight_in = 7;
        private int _awb_weight_out = 3;

        private int _rgainStart = 170;
        private int _rgainMin = 170;
        private int _rgainMax = 440;

        private int _awb_ymin = 16;
        private int _awb_ymax = 192;

        private byte[] _awb_stat_tab = new byte[128] {
            154,154,154,155,155,155,154,154,153,153,152,151,150,149,148,146,145,143,141,138,136,133,130,127,124,120,115,111,106,100,93,87,
            153,144,137,130,125,120,116,112,109,105,103,100,98,95,93,92,90,88,87,86,85,84,83,83,82,82,82,82,83,83,85,86,
            154,158,161,164,166,167,168,169,169,169,169,168,167,167,165,164,163,161,159,157,154,152,149,146,142,138,134,129,123,116,107,89,
            151,129,119,111,105,100,95,92,88,85,82,80,78,76,74,72,71,70,69,68,68,68,67,68,68,69,70,71,73,76,80,86
        };

        //YUVʽ
        private int _awb_yuv_mod_en = 0;
        private int[] _awb_cb_th = new int[8] { 8, 16, 24, 32, 40, 48, 48, 48 };
        private int[] _awb_cr_th = new int[8] { 8, 16, 24, 32, 40, 48, 48, 48 };
        private int[] _awb_cbcr_th = new int[8] { 12, 24, 36, 48, 60, 72, 72, 72 };
        private byte _awb_ycbcr_th = 10;

        private int _gainStep = 16;

        public event PropertyChangedEventHandler PropertyChanged;

        // 不同设备类型的AWB参数结构
        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
        private struct AwbParamsAX327X
        {
            public int seg_mode;
            public int rg_start;
            public int rgmin;
            public int rgmax;
            public int weight_in;
            public int weight_mid;
            public int ymin;
            public int ymax;
            public int hb_rate;
            public int hb_class;
            public int hr_rate;
            public int hr_class;
            public int awb_scene_mod; //NotUse
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public int[] manu_awb_gain;//= new int[5]; // NotUse
            public int yuv_mod_en;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public int[] cb_th; //= new int[8];
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public int[] cr_th; //= new int[8];
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public int[] cbcr_th; //= new int[8];
            public int ycbcr_th;
            public int manu_rgain;
            public int manu_ggain;
            public int manu_bgain;
            public int rgain;
            public int ggain;
            public int bgain;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
            public short[] seg_gain; // 原来是[8][3]二维数组，现在写成一维
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public byte[] awb_tab; //= new byte[128];
        };

        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
        private struct AwbParamsAX32XX
        {
            public ushort mixPixSum;
            public ushort rg_start;
            public ushort rgmin;
            public ushort rgmax;
            public byte weight_in;
            public byte weight_mid;
            public byte ymin;
            public byte ymax;

            public byte hb_rate;
            public byte hb_class;
            public byte hr_rate;
            public byte hr_class;
            public byte yuv_mod_en;
            public byte ycbcr_th;
            public ushort _padding;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public byte[] awb_tab; //= new byte[128];
        };

        public override Dictionary<int, byte[]> ParamsDataCollection
        {
            get
            {
                // 根据当前设备类型选择对应的参数结构
                if (_commonConfig != null && _commonConfig.DeviceType == "AX327X")
                {
                    return GetAX327XParams();
                }
                else
                {
                    // 默认使用AX32XX结构
                    return GetAX32XXParams();
                }
            }
            set
            {
                // 根据当前设备类型选择对应的解析方法
                if (_commonConfig != null && _commonConfig.DeviceType == "AX327X")
                {
                    SetAX327XParams(value);
                }
                else
                {
                    // 默认使用AX32XX结构
                    SetAX32XXParams(value);
                }
            }
        }

        private Dictionary<int, byte[]> GetAX327XParams()
        {
            // AX327X设备的参数结构
            AwbParamsAX327X awbParams = new AwbParamsAX327X()
            {
                seg_mode = Seg_Mode,
                rg_start = RGainStart,
                rgmin = RGainMin,
                rgmax = RGainMax,
                weight_in = Awb_Weight_In,
                weight_mid = Awb_Weight_Out,
                ymin = Awb_YMin,
                ymax = Awb_YMax,
                hb_rate = Awb_De_High_Blue_Rate,
                hb_class = Awb_De_High_Blue_Class,
                hr_rate = Awb_De_High_Red_Rate,
                hr_class = Awb_De_High_Red_Class,
                awb_scene_mod = 0,
                manu_awb_gain = new int[5] { 0, 0, 0, 0, 0 },
                yuv_mod_en = Awb_Yuv_Mod_En,
                cb_th = Awb_Cb_Th,
                cr_th = Awb_Cr_Th,
                cbcr_th = Awb_Cbcr_Th,
                ycbcr_th = Awb_Ycbcr_Th,
                manu_rgain = 0, // 暂设为0，如有需要可替换为实际属性
                manu_ggain = 0,
                manu_bgain = 0,
                rgain = 0,
                ggain = 0,
                bgain = 0,
                seg_gain = new short[24], // 暂设为空数组，如有需要可替换为实际属性
                awb_tab = Awb_Stat_Tab
            };

            int size = Marshal.SizeOf(awbParams);
            byte[] arr = new byte[size];

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(awbParams, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);

            return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
        }

        private Dictionary<int, byte[]> GetAX32XXParams()
        {
            // AX32XX设备的参数结构
            AwbParamsAX32XX awbParams = new AwbParamsAX32XX()
            {
                mixPixSum = (ushort)MixPixSum,
                rg_start = (ushort)RGainStart,
                rgmin = (ushort)RGainMin,
                rgmax = (ushort)RGainMax,
                weight_in = (byte)Awb_Weight_In,
                weight_mid = (byte)Awb_Weight_Out,
                ymin = (byte)Awb_YMin,
                ymax = (byte)Awb_YMax,
                hb_rate = (byte)Awb_De_High_Blue_Rate,
                hb_class = (byte)Awb_De_High_Blue_Class,
                hr_rate = (byte)Awb_De_High_Red_Rate,
                hr_class = (byte)Awb_De_High_Red_Class,
                yuv_mod_en = (byte)Awb_Yuv_Mod_En,
                ycbcr_th = Awb_Ycbcr_Th,
                awb_tab = Awb_Stat_Tab
            };

            int size = Marshal.SizeOf(awbParams);
            byte[] arr = new byte[size];

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(awbParams, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);

            return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
        }

        private void SetAX327XParams(Dictionary<int, byte[]> value)
        {
            // 处理AX327X设备的参数
            IntPtr ptr = Marshal.AllocHGlobal(value[DeviceModulePos].Length);
            Marshal.Copy(value[DeviceModulePos], 0, ptr, value[DeviceModulePos].Length);

            AwbParamsAX327X ax327xParams = (AwbParamsAX327X)Marshal.PtrToStructure(ptr, typeof(AwbParamsAX327X));

            Seg_Mode = ax327xParams.seg_mode;
            RGainStart = ax327xParams.rg_start;
            RGainMin = ax327xParams.rgmin;
            RGainMax = ax327xParams.rgmax;
            Awb_Weight_In = ax327xParams.weight_in;
            Awb_Weight_Out = ax327xParams.weight_mid;
            Awb_YMin = ax327xParams.ymin;
            Awb_YMax = ax327xParams.ymax;
            Awb_De_High_Blue_Rate = ax327xParams.hb_rate;
            Awb_De_High_Blue_Class = ax327xParams.hb_class;
            Awb_De_High_Red_Rate = ax327xParams.hr_rate;
            Awb_De_High_Red_Class = ax327xParams.hr_class;
            Awb_Yuv_Mod_En = ax327xParams.yuv_mod_en;
            Awb_Cb_Th = ax327xParams.cb_th;
            Awb_Cr_Th = ax327xParams.cr_th;
            Awb_Cbcr_Th = ax327xParams.cbcr_th;
            Awb_Ycbcr_Th = (byte)ax327xParams.ycbcr_th;
            Awb_Stat_Tab = ax327xParams.awb_tab;

            Marshal.FreeHGlobal(ptr);
        }

        private void SetAX32XXParams(Dictionary<int, byte[]> value)
        {
            // 处理AX32XX设备的参数
            IntPtr ptr = Marshal.AllocHGlobal(value[DeviceModulePos].Length);
            Marshal.Copy(value[DeviceModulePos], 0, ptr, value[DeviceModulePos].Length);

            AwbParamsAX32XX ax32xxParams = (AwbParamsAX32XX)Marshal.PtrToStructure(ptr, typeof(AwbParamsAX32XX));

            MixPixSum = ax32xxParams.mixPixSum;
            RGainStart = ax32xxParams.rg_start;
            RGainMin = ax32xxParams.rgmin;
            RGainMax = ax32xxParams.rgmax;
            Awb_Weight_In = ax32xxParams.weight_in;
            Awb_Weight_Out = ax32xxParams.weight_mid;
            Awb_YMin = ax32xxParams.ymin;
            Awb_YMax = ax32xxParams.ymax;
            Awb_De_High_Blue_Rate = ax32xxParams.hb_rate;
            Awb_De_High_Blue_Class = ax32xxParams.hb_class;
            Awb_De_High_Red_Rate = ax32xxParams.hr_rate;
            Awb_De_High_Red_Class = ax32xxParams.hr_class;
            Awb_Yuv_Mod_En = ax32xxParams.yuv_mod_en;
            Awb_Ycbcr_Th = ax32xxParams.ycbcr_th;
            Awb_Stat_Tab = ax32xxParams.awb_tab;

            Marshal.FreeHGlobal(ptr);
        }

        public AutoWhiteBalance()
        {
            DeviceModulePos = 4;

            //SetPreviousStepEnable(IspModule.Blc, true);
            //SetPreviousStepEnable(IspModule.Lsc, true);
        }

        public void SetPreviousStep(Processor ispProcessor)
        {
            ObservableCollection<KeyValuePair<IspModule, bool>> _ispProccesStepsEnables = ispProcessor.IspCommonConfig.ProcessorStepsEnables;
            if (_ispProccesStepsEnables == null)
            {
                return;
            }

            int previousStepPos = _ispProccesStepsEnables.IndexOf(_ispProccesStepsEnables.First(item => item.Key == IspModule.Blc));

            if (previousStepPos >= 0)
            {
                bool set = _ispProccesStepsEnables[previousStepPos].Value;
                SetPreviousStepEnable(IspModule.Blc, set);
            }
            previousStepPos = _ispProccesStepsEnables.IndexOf(_ispProccesStepsEnables.First(item => item.Key == IspModule.Lsc));

            if (previousStepPos >= 0)
            {
                bool set = _ispProccesStepsEnables[previousStepPos].Value;
                SetPreviousStepEnable(IspModule.Lsc, set);
            }
        }

        public Dictionary<string, KeyValuePair<int, int>> GainData
        {
            get { return _gainData; }
            set
            {
                _gainData = value;
                //HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("GainData");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        // �����Observable��������ί�У����Բ��õ���PropertyChanged
        public ObservableCollection<WhiteBalanceStatCollection> StatisticData
        {
            get { return _statisticData; }
            set { _statisticData = value; }
        }

        public ObservableCollection<string> CurveLabels
        {
            get => _curveLabels;
            set
            {
                _curveLabels = value;
                // 通知 UI 更新
                //HasChangedParams = true;
                //PropertyChangedEventArgs args = new PropertyChangedEventArgs("CurveLabels");
                //if (PropertyChanged != null)
                //    PropertyChanged(this, args);
            }
        }

        // 智能插值模式的关键点数据
        public ObservableCollection<ObservableCollection<KeyValuePair<double, double>>> KeyPointData
        {
            get { return _keyPointData; }
            set
            {
                _keyPointData = value;
                //HasChangedParams = true;
                //PropertyChangedEventArgs args = new PropertyChangedEventArgs("KeyPointData");
                //if (PropertyChanged != null)
                //    PropertyChanged(this, args);
            }
        }

        public bool SmartInterpolationEnabled
        {
            get { return _smartInterpolationEnabled; }
            set
            {
                _smartInterpolationEnabled = value;
                //HasChangedParams = true;
                //PropertyChangedEventArgs args = new PropertyChangedEventArgs("SmartInterpolationEnabled");
                //if (PropertyChanged != null)
                //    PropertyChanged(this, args);
            }
        }

        public int KeyPointCountPerCurve
        {
            get { return _keyPointCountPerCurve; }
            set
            {
                if (value >= 4 && value <= 16)
                {
                    _keyPointCountPerCurve = value;
                    //HasChangedParams = true;
                    //PropertyChangedEventArgs args = new PropertyChangedEventArgs("KeyPointCountPerCurve");
                    //if (PropertyChanged != null)
                    //    PropertyChanged(this, args);
                }
            }
        }

        /// <summary>
        /// 从关键点生成完整曲线（智能插值核心方法）
        /// </summary>
        public void GenerateFullCurveFromKeyPoints()
        {
            if (!_smartInterpolationEnabled || _keyPointData.Count == 0) return;

            _statisticData.Clear();
            foreach (var keyPoints in _keyPointData)
            {
                if (keyPoints != null && keyPoints.Count >= 2)
                {
                    var fullCurve = AwbSmartInterpolator.GenerateFullCurveFromKeyPoints(keyPoints, 32);
                    _statisticData.Add(fullCurve);
                }
            }

            //PropertyChangedEventArgs args = new PropertyChangedEventArgs("StatisticData");
            //if (PropertyChanged != null)
            //    PropertyChanged(this, args);
        }

        /// <summary>
        /// 初始化默认关键点（8个均匀分布的点）
        /// </summary>
        public void InitializeDefaultKeyPoints(int curveIndex = 0)
        {
            while (_keyPointData.Count <= curveIndex)
            {
                _keyPointData.Add(new ObservableCollection<KeyValuePair<double, double>>());
            }

            var defaultPoints = AwbSmartInterpolator.GenerateDefaultKeyPoints(
                RGainStart, _gainStep, _keyPointCountPerCurve);

            _keyPointData[curveIndex] = defaultPoints;
            //PropertyChangedEventArgs args = new PropertyChangedEventArgs("KeyPointData");
            //if (PropertyChanged != null)
            //    PropertyChanged(this, args);
        }

        /// <summary>
        /// 智能更新StatTab：自动从关键点插值生成128字节表
        /// </summary>
        public void SmartUpdateAwbStatTab()
        {
            if (_smartInterpolationEnabled && _keyPointData.Count > 0)
            {
                GenerateFullCurveFromKeyPoints();
                UpdateAwbStatTab();
            }
            else
            {
                UpdateAwbStatTab();
            }
        }

        public int Awb_De_High_Red_Class
        {
            get { return _awb_de_high_red_class; }
            set
            {
                _awb_de_high_red_class = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_De_High_Red_Class");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int Awb_De_High_Blue_Class
        {
            get { return _awb_de_high_blue_class; }
            set
            {
                _awb_de_high_blue_class = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_De_High_Blue_Class");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int Awb_De_High_Red_Rate
        {
            get { return _awb_de_high_red_rate; }
            set
            {
                _awb_de_high_red_rate = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_De_High_Red_Rate");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int Awb_De_High_Blue_Rate
        {
            get { return _awb_de_high_blue_rate; }
            set
            {
                _awb_de_high_blue_rate = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_De_High_Blue_Rate");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int Seg_Mode
        {
            get { return _seg_mode; }
            set
            {
                _seg_mode = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Seg_Mode");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int MixPixSum
        {
            get { return _mixPixSum; }
            set
            {
                _mixPixSum = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("MixPixSum");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int Awb_Weight_In
        {
            get { return _awb_weight_in; }
            set
            {
                _awb_weight_in = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_Weight_In");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int Awb_Weight_Out
        {
            get { return _awb_weight_out; }
            set
            {
                _awb_weight_out = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_Weight_Out");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int RGainStart
        {
            get { return _rgainStart; }
            set
            {
                _rgainStart = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("RGainStart");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int RGainMin
        {
            get { return _rgainMin; }
            set
            {
                _rgainMin = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("RGainMin");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int RGainMax
        {
            get { return _rgainMax; }
            set
            {
                _rgainMax = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("RGainMax");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int Awb_YMin
        {
            get { return _awb_ymin; }
            set
            {
                _awb_ymin = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_YMin");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int Awb_YMax
        {
            get { return _awb_ymax; }
            set
            {
                _awb_ymax = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_YMax");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public byte[] Awb_Stat_Tab
        {
            get { return _awb_stat_tab; }
            set
            {
                _awb_stat_tab = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_Stat_Tab");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int Awb_Yuv_Mod_En
        {
            get { return _awb_yuv_mod_en; }
            set
            {
                _awb_yuv_mod_en = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_Yuv_Mod_En");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int[] Awb_Cb_Th
        {
            get { return _awb_cb_th; }
            set
            {
                _awb_cb_th = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_Cb_Th");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int[] Awb_Cr_Th
        {
            get { return _awb_cr_th; }
            set
            {
                _awb_cr_th = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_Cr_Th");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int[] Awb_Cbcr_Th
        {
            get { return _awb_cbcr_th; }
            set
            {
                _awb_cbcr_th = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_Cbcr_Th");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public byte Awb_Ycbcr_Th
        {
            get { return _awb_ycbcr_th; }
            set
            {
                _awb_ycbcr_th = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Awb_Ycbcr_Th");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public void LoadChartDataFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                    throw new ArgumentNullException(nameof(path));
                if (!File.Exists(path))
                    throw new FileNotFoundException("文件不存在", path);

                string xmlFileText = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(xmlFileText))
                    throw new InvalidDataException("文件内容为空");

                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlFileText);

                var rootNode = doc["AwbChartData"];
                if (rootNode == null)
                    throw new InvalidDataException("无效的Awb作图数据文件格式：缺少AwbChartData根节点");

                // 读取基本参数
                var rGainStartNode = rootNode["RGainStart"];
                if (rGainStartNode?.FirstChild != null)
                    RGainStart = Convert.ToInt32(rGainStartNode.FirstChild.Value);

                var rGainMinNode = rootNode["RGainMin"];
                if (rGainMinNode?.FirstChild != null)
                    RGainMin = Convert.ToInt32(rGainMinNode.FirstChild.Value);

                var rGainMaxNode = rootNode["RGainMax"];
                if (rGainMaxNode?.FirstChild != null)
                    RGainMax = Convert.ToInt32(rGainMaxNode.FirstChild.Value);

                // 验证参数合理性
                if (RGainMin > RGainMax)
                    throw new InvalidDataException($"数据无效：RGainMin({RGainMin}) > RGainMax({RGainMax})");

                // 读取StatData
                var statDataNode = rootNode["StatData"];
                if (statDataNode != null && statDataNode.HasChildNodes)
                {
                    // 暂时移除事件监听，避免在填充数据时触发多次UI更新
                    _statisticData.Clear();

                    // 检查是否为新格式（包含Curve子元素）
                    var curveNodes = statDataNode.SelectNodes("Curve");
                    if (curveNodes != null && curveNodes.Count > 0)
                    {
                        // 新格式：读取完整的X、Y坐标
                        foreach (XmlNode curveNode in curveNodes)
                        {
                            WhiteBalanceStatCollection tmpCollection = new WhiteBalanceStatCollection();
                            var pointNodes = curveNode.SelectNodes("Point");
                            if (pointNodes != null)
                            {
                                foreach (XmlNode pointNode in pointNodes)
                                {
                                    var xNode = pointNode["X"];
                                    var yNode = pointNode["Y"];
                                    if (xNode?.FirstChild != null && yNode?.FirstChild != null)
                                    {
                                        double x = Convert.ToDouble(xNode.FirstChild.Value);
                                        double y = Convert.ToDouble(yNode.FirstChild.Value);
                                        tmpCollection.Add(new KeyValuePair<double, double>(x, y));
                                    }
                                }
                            }
                            if (tmpCollection.Count > 0)
                                _statisticData.Add(tmpCollection);
                        }
                    }
                    else if (statDataNode.FirstChild != null && statDataNode.FirstChild.Value != null)
                    {
                        // 旧格式兼容：只有Y值，X坐标用等间距计算
                        var statDataText = statDataNode.FirstChild.Value;
                        var statDataList = statDataText.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).ToList();

                        if (statDataList.Count == 0)
                            throw new InvalidDataException("StatData数据为空");

                        // 动态计算曲线数量和点数
                        int totalPoints = statDataList.Count;
                        int pointsPerCurve = 32; // 默认每条曲线32个点
                        int curveCount = (totalPoints + pointsPerCurve - 1) / pointsPerCurve;

                        int dataIndex = 0;
                        for (int i = 0; i < curveCount && dataIndex < totalPoints; i++)
                        {
                            WhiteBalanceStatCollection tmpCollection = new WhiteBalanceStatCollection();
                            int currentCurvePoints = Math.Min(pointsPerCurve, totalPoints - dataIndex);

                            for (int j = 0; j < currentCurvePoints; j++)
                            {
                                double x = _rgainStart + _gainStep * j;
                                double y = Convert.ToDouble(statDataList[dataIndex]);
                                tmpCollection.Add(new KeyValuePair<double, double>(x, y));
                                dataIndex++;
                            }
                            _statisticData.Add(tmpCollection);
                        }
                    }
                }

                // 读取GainValueData
                var gainValueDataNode = rootNode["GainValueData"];
                if (gainValueDataNode != null && gainValueDataNode.HasChildNodes)
                {
                    Dictionary<string, KeyValuePair<int, int>> tmpCollection = new Dictionary<string, KeyValuePair<int, int>>();
                    for (int i = 0; i < gainValueDataNode.ChildNodes.Count; i++)
                    {
                        var node = gainValueDataNode.ChildNodes[i];
                        var pathAttr = node.Attributes?["Path"];
                        if (pathAttr == null || node.FirstChild == null)
                            continue;

                        var gainValText = node.FirstChild.Value;
                        string[] keyValue = gainValText.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries);
                        if (keyValue.Length >= 2)
                        {
                            tmpCollection[pathAttr.Value] = new KeyValuePair<int, int>(
                                Convert.ToInt32(keyValue[0]),
                                Convert.ToInt32(keyValue[1]));
                        }
                    }
                    GainData = tmpCollection;
                }
                // 数据加载完成后，触发PropertyChanged事件通知UI重建图表
                if (_statisticData.Count != 0)
                {
                    PropertyChangedEventArgs args = new PropertyChangedEventArgs("StatisticData");
                    if (PropertyChanged != null)
                        PropertyChanged(this, args);
                }
            }
            catch (Exception ex) when (!(ex is InvalidDataException || ex is FileNotFoundException || ex is ArgumentNullException))
            {
                throw new InvalidDataException($"读取Awb作图数据失败：{ex.Message}", ex);
            }
        }

        public void SaveChartDataFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                    throw new ArgumentNullException(nameof(path));

                XmlDocument doc = new XmlDocument();
                XmlDeclaration dec = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
                doc.AppendChild(dec);

                XmlElement root = doc.CreateElement("AwbChartData");
                doc.AppendChild(root);

                // 保存基本参数
                XmlElement rGainStartNode = doc.CreateElement("RGainStart");
                rGainStartNode.AppendChild(doc.CreateTextNode(RGainStart.ToString()));
                root.AppendChild(rGainStartNode);

                XmlElement rGainMinNode = doc.CreateElement("RGainMin");
                rGainMinNode.AppendChild(doc.CreateTextNode(RGainMin.ToString()));
                root.AppendChild(rGainMinNode);

                XmlElement rGainMaxNode = doc.CreateElement("RGainMax");
                rGainMaxNode.AppendChild(doc.CreateTextNode(RGainMax.ToString()));
                root.AppendChild(rGainMaxNode);

                // 保存StatData（新格式：包含完整的X、Y坐标）
                XmlElement statDataNode = doc.CreateElement("StatData");
                foreach (var collection in _statisticData)
                {
                    XmlElement curveNode = doc.CreateElement("Curve");
                    foreach (var item in collection)
                    {
                        XmlElement pointNode = doc.CreateElement("Point");

                        XmlElement xNode = doc.CreateElement("X");
                        xNode.AppendChild(doc.CreateTextNode(item.Key.ToString("F2")));
                        pointNode.AppendChild(xNode);

                        XmlElement yNode = doc.CreateElement("Y");
                        yNode.AppendChild(doc.CreateTextNode(item.Value.ToString("F2")));
                        pointNode.AppendChild(yNode);

                        curveNode.AppendChild(pointNode);
                    }
                    statDataNode.AppendChild(curveNode);
                }
                root.AppendChild(statDataNode);

                // 保存GainValueData
                XmlElement gainValueData = doc.CreateElement("GainValueData");
                foreach (var item in _gainData)
                {
                    XmlElement gainValueDataItem = doc.CreateElement("Value");
                    gainValueDataItem.SetAttribute("Path", item.Key);

                    string gainValueDataContent = string.Format("{0},{1}", (int)item.Value.Key, (int)item.Value.Value);
                    gainValueDataItem.AppendChild(doc.CreateTextNode(gainValueDataContent));

                    gainValueData.AppendChild(gainValueDataItem);
                }
                root.AppendChild(gainValueData);

                doc.Save(path);
            }
            catch (Exception ex) when (!(ex is ArgumentNullException))
            {
                throw new IOException($"保存Awb作图数据失败：{ex.Message}", ex);
            }
        }

        public override void ProcessRawBuffer(ref byte[] imgBuffer)
        {
            try
            {
                if (imgBuffer == null)
                    throw new ArgumentNullException(nameof(imgBuffer));
                if (_commonConfig == null)
                    throw new InvalidOperationException("CommonConfig not initialized");

                Logger.Debug($"[AWB] Processing - Buffer: {imgBuffer.Length} bytes, Resolution: {_commonConfig.ResolutionWidth}x{_commonConfig.ResolutionHeight}, Bayer: {_commonConfig.Bayer}");
                Logger.Debug($"[AWB] Params: RGainStart={RGainStart}, RGainMin={RGainMin}, RGainMax={RGainMax}, YMin={Awb_YMin}, YMax={Awb_YMax}");

                int pixelCount = _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight;
                short[] outputBuffer = new short[pixelCount];

                Logger.Debug($"[AWB] Calculating gain values...");
                int[] gainValues = CalcGainValue(imgBuffer);
                Logger.Debug($"[AWB] Gain values calculated: R={gainValues[0]}, G={gainValues[1]}, B={gainValues[2]}");

                Logger.Debug($"[AWB] Calling IspApi.AWBImg");
                IspApi.AWBImg(imgBuffer, (int)_commonConfig.Bayer, _commonConfig.ResolutionWidth,
                              _commonConfig.ResolutionHeight, gainValues,
                              _awb_de_high_red_class, _awb_de_high_blue_class,
                              _awb_de_high_red_rate, _awb_de_high_blue_rate, outputBuffer);
                Logger.Debug($"[AWB] AWBImg completed");

                // 直接转换，避免中间变量
                imgBuffer = new byte[pixelCount * sizeof(short)];
                Buffer.BlockCopy(outputBuffer, 0, imgBuffer, 0, imgBuffer.Length);

                Logger.Debug($"[AWB] Processing completed, output buffer: {imgBuffer.Length} bytes");
            }
            catch (Exception ex)
            {
                Logger.Error("[AWB] ProcessRawBuffer failed.", ex);
                throw;
            }
        }

        public int[] CalcGainValue(byte[] raw_img)
        {
            int[] returnData = new int[3];
            int[] wp_output = new int[128];

            if (_awb_yuv_mod_en != 0)
            {
                UpdateAwbStatTab();

                IspApi.AWBStatistic(raw_img, (int)_commonConfig.Bayer, _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight,
                    _seg_mode, _awb_stat_tab, _awb_weight_in, _awb_weight_out, _rgainStart, _rgainMin, _rgainMax, _awb_ymin, _awb_ymax, wp_output);
            }
            else
            {
                IspApi.AWBStatistic_Yuv(raw_img, (int)_commonConfig.Bayer, _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight,
                    _seg_mode, _awb_ymin, _awb_ymax, _awb_cb_th, _awb_cr_th, _awb_cbcr_th, _awb_ycbcr_th, wp_output);
            }

            IspApi.AWB_Gain_Soft_Cal(wp_output, _seg_mode, ref returnData[0], ref returnData[1], ref returnData[2]);

            return returnData;
        }

        public void UpdateAwbStatTab()
        {
            byte[] tmpAwbStatTab = new byte[Awb_Stat_Tab.Length];
            Array.Clear(tmpAwbStatTab, 0, tmpAwbStatTab.Length);
            Array.Copy(Awb_Stat_Tab, tmpAwbStatTab, Awb_Stat_Tab.Length);
            int i = 0;
            foreach (var lineStat in StatisticData)
            {
                foreach (var item in lineStat)
                {
                    if (item.Value <= 0)
                        continue;
                    tmpAwbStatTab[i] = (byte)item.Value;
                    i++;
                }
            }

            Awb_Stat_Tab = tmpAwbStatTab;
        }

        // 添加曲线时统一维护标签
        public void AddStatisticCurve(ObservableCollection<KeyValuePair<double, double>> points, string label = null)
        {
            StatisticData.Add(points);
            CurveLabels.Add(label ?? $"曲线 {CurveLabels.Count + 1}");
        }

        // 清空时同步
        public void ClearAllCurves()
        {
            StatisticData.Clear();
            CurveLabels.Clear();
        }

        public void CalcIQ(byte[] fileBuffer, int[] x, int[] y, int[] width, int[] height, ref double rgIq, ref double bgIq)
        {
            IntPtr[] ptrArray = new IntPtr[3];
            for (int i = 0; i < ptrArray.Length; i++)
            {
                ptrArray[i] = Marshal.AllocHGlobal(_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
                Marshal.Copy(new byte[_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short)],
                    0, ptrArray[i], _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
            }

            IspApi.DemosaicImg(fileBuffer, (int)_commonConfig.Bayer, _commonConfig.ResolutionWidth,
                _commonConfig.ResolutionHeight, ptrArray);

            IspApi.AWB_IQ(ptrArray, _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, (int)_commonConfig.Bayer,
                    x, y, width, height, ref rgIq, ref bgIq);

            for (int i = 0; i < ptrArray.Length; i++)
            {
                Marshal.FreeHGlobal(ptrArray[i]);
            }
        }

        public override XmlElement SerializeToXmlElement(XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("Awb");

            XmlElement segModeNode = xmlDoc.CreateElement("MixPixSum");
            segModeNode.AppendChild(xmlDoc.CreateTextNode(MixPixSum.ToString()));
            xmlElement.AppendChild(segModeNode);

            XmlElement AwbWeightInNode = xmlDoc.CreateElement("Awb_Weight_In");
            AwbWeightInNode.AppendChild(xmlDoc.CreateTextNode(Awb_Weight_In.ToString()));
            xmlElement.AppendChild(AwbWeightInNode);

            XmlElement AwbWeightOutNode = xmlDoc.CreateElement("Awb_Weight_Out");
            AwbWeightOutNode.AppendChild(xmlDoc.CreateTextNode(Awb_Weight_Out.ToString()));
            xmlElement.AppendChild(AwbWeightOutNode);

            XmlElement AwbRgStartNode = xmlDoc.CreateElement("Awb_Rg_Start");
            AwbRgStartNode.AppendChild(xmlDoc.CreateTextNode(RGainStart.ToString()));
            xmlElement.AppendChild(AwbRgStartNode);

            XmlElement AwbRgainMinNode = xmlDoc.CreateElement("Awb_Rgain_Min");
            AwbRgainMinNode.AppendChild(xmlDoc.CreateTextNode(RGainMin.ToString()));
            xmlElement.AppendChild(AwbRgainMinNode);

            XmlElement AwbRgainMaxNode = xmlDoc.CreateElement("Awb_Rgain_Max");
            AwbRgainMaxNode.AppendChild(xmlDoc.CreateTextNode(RGainMax.ToString()));
            xmlElement.AppendChild(AwbRgainMaxNode);

            XmlElement AwbYMinNode = xmlDoc.CreateElement("Awb_YMin");
            AwbYMinNode.AppendChild(xmlDoc.CreateTextNode(Awb_YMin.ToString()));
            xmlElement.AppendChild(AwbYMinNode);

            XmlElement AwbYMaxNode = xmlDoc.CreateElement("Awb_YMax");
            AwbYMaxNode.AppendChild(xmlDoc.CreateTextNode(Awb_YMax.ToString()));
            xmlElement.AppendChild(AwbYMaxNode);

            XmlElement AwbStatTabNode = xmlDoc.CreateElement("Awb_Stat_Tab");
            string statTabStr = string.Join(",", Awb_Stat_Tab.Select(x => x.ToString()).ToArray());
            AwbStatTabNode.AppendChild(xmlDoc.CreateTextNode(statTabStr));
            xmlElement.AppendChild(AwbStatTabNode);

            XmlElement AwbYuvModeEnNode = xmlDoc.CreateElement("Awb_Yuv_Mod_En");
            AwbYuvModeEnNode.AppendChild(xmlDoc.CreateTextNode(Awb_Yuv_Mod_En.ToString()));
            xmlElement.AppendChild(AwbYuvModeEnNode);

            //XmlElement AwbCbThNode = xmlDoc.CreateElement("Awb_Cb_Th");
            //string cbThStr = string.Join(",", Awb_Cb_Th.Select(x => x.ToString()).ToArray());
            //AwbCbThNode.AppendChild(xmlDoc.CreateTextNode(cbThStr.ToString()));
            //xmlElement.AppendChild(AwbCbThNode);

            //XmlElement AwbCrThNode = xmlDoc.CreateElement("Awb_Cr_Th");
            //string crThStr = string.Join(",", Awb_Cr_Th.Select(x => x.ToString()).ToArray());
            //AwbCrThNode.AppendChild(xmlDoc.CreateTextNode(crThStr));
            //xmlElement.AppendChild(AwbCrThNode);

            //XmlElement AwbCbcrThNode = xmlDoc.CreateElement("Awb_Cbcr_Th");
            //string cbCrThStr = string.Join(",", Awb_Cbcr_Th.Select(x => x.ToString()).ToArray());
            //AwbCbcrThNode.AppendChild(xmlDoc.CreateTextNode(cbCrThStr));
            //xmlElement.AppendChild(AwbCbcrThNode);

            XmlElement AwbYcbcrThNode = xmlDoc.CreateElement("Awb_Ycbcr_Th");
            AwbYcbcrThNode.AppendChild(xmlDoc.CreateTextNode(Awb_Ycbcr_Th.ToString()));
            xmlElement.AppendChild(AwbYcbcrThNode);

            XmlElement AwbDeHighRedClassNode = xmlDoc.CreateElement("Awb_De_High_Red_Class");
            AwbDeHighRedClassNode.AppendChild(xmlDoc.CreateTextNode(Awb_De_High_Red_Class.ToString()));
            xmlElement.AppendChild(AwbDeHighRedClassNode);

            XmlElement AwbDeHighBlueClassNode = xmlDoc.CreateElement("Awb_De_High_Blue_Class");
            AwbDeHighBlueClassNode.AppendChild(xmlDoc.CreateTextNode(Awb_De_High_Blue_Class.ToString()));
            xmlElement.AppendChild(AwbDeHighBlueClassNode);

            XmlElement AwbDeHighRedRateNode = xmlDoc.CreateElement("Awb_De_High_Red_Rate");
            AwbDeHighRedRateNode.AppendChild(xmlDoc.CreateTextNode(Awb_De_High_Red_Rate.ToString()));
            xmlElement.AppendChild(AwbDeHighRedRateNode);

            XmlElement AwbDeHighBlueRateNode = xmlDoc.CreateElement("Awb_De_High_Blue_Rate");
            AwbDeHighBlueRateNode.AppendChild(xmlDoc.CreateTextNode(Awb_De_High_Blue_Rate.ToString()));
            xmlElement.AppendChild(AwbDeHighBlueRateNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
        {
            var awbNode = ispToolDataNode["Awb"];

            MixPixSum = XmlHelper.GetNodeInt(awbNode, "MixPixSum");
            Awb_Weight_In = XmlHelper.GetNodeInt(awbNode, "Awb_Weight_In");
            Awb_Weight_Out = XmlHelper.GetNodeInt(awbNode, "Awb_Weight_Out");
            RGainStart = XmlHelper.GetNodeInt(awbNode, "Awb_Rg_Start");
            RGainMin = XmlHelper.GetNodeInt(awbNode, "Awb_Rgain_Min");
            RGainMax = XmlHelper.GetNodeInt(awbNode, "Awb_Rgain_Max");
            Awb_YMin = XmlHelper.GetNodeInt(awbNode, "Awb_YMin");
            Awb_YMax = XmlHelper.GetNodeInt(awbNode, "Awb_YMax");

            var tmpStatTabStr = XmlHelper.GetNodeValue(awbNode, "Awb_Stat_Tab");
            if (tmpStatTabStr != null)
            {
                Awb_Stat_Tab = tmpStatTabStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }

            Awb_Yuv_Mod_En = XmlHelper.GetNodeInt(awbNode, "Awb_Yuv_Mod_En");

            //var tmpAwbCbThStr = XmlHelper.GetNodeValue(awbNode, "Awb_Cb_Th");
            //if (tmpAwbCbThStr != null)
            //{
            //    Awb_Cb_Th = tmpAwbCbThStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
            //        .Select(s => Convert.ToInt32(s))
            //        .ToArray();
            //}

            //var tmpAwbCrThStr = XmlHelper.GetNodeValue(awbNode, "Awb_Cr_Th");
            //if (tmpAwbCrThStr != null)
            //{
            //    Awb_Cr_Th = tmpAwbCrThStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
            //        .Select(s => Convert.ToInt32(s))
            //        .ToArray();
            //}

            //var tmpAwbCbcrThStr = XmlHelper.GetNodeValue(awbNode, "Awb_Cbcr_Th");
            //if (tmpAwbCbcrThStr != null)
            //{
            //    Awb_Cbcr_Th = tmpAwbCbcrThStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
            //        .Select(s => Convert.ToInt32(s))
            //        .ToArray();
            //}

            Awb_Ycbcr_Th = Convert.ToByte(XmlHelper.GetNodeValue(awbNode, "Awb_Ycbcr_Th"));
            Awb_De_High_Red_Class = XmlHelper.GetNodeInt(awbNode, "Awb_De_High_Red_Class");
            Awb_De_High_Blue_Class = XmlHelper.GetNodeInt(awbNode, "Awb_De_High_Blue_Class");
            Awb_De_High_Red_Rate = XmlHelper.GetNodeInt(awbNode, "Awb_De_High_Red_Rate");
            Awb_De_High_Blue_Rate = XmlHelper.GetNodeInt(awbNode, "Awb_De_High_Blue_Rate");
        }

        public override void ProcessRgbBuffer(ref byte[] imgBuffer)
        {
            throw new NotImplementedException();
        }
    }
}