using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using ThunderSE.DeviceConfig;

namespace ThunderSE.DeviceConfig.Isp
{
    class CH : ProcessStep, INotifyPropertyChanged
    {
        /*
        private struct CHParams
        {
            public int stage0_en;//enable r g b
	        public int stage1_en;//enable y c m
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
	        public int[] enhence;//enhance channel  r b g y c m
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public int[] th1;//you can set hue width
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public int[] th0;
            //m_x r_x y_x b_x g_r r_x
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
	        public int[] r_rate;//combining with sat[],you can enhance or weaken
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
	        public int[] g_rate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
	        public int[] b_rate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
	        public int[] sat;//16Ϊ1
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
	        public int[] rate;
        }
        */

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CHParams
        {
            public byte stage0_en;//enable r g b
            public byte stage1_en;//enable y c m
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] enhence;//enhance channel  r b g y c m
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] r_rate;//combining with sat[],you can enhance or weaken
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] g_rate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] b_rate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public byte[] sat;//16Ϊ1
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] rate;
        }

        private int _stage0_en;
        private int _stage1_en;
        private int[] _enhence = new int[6];
        private int[] _th1 = new int[6];
        private int[] _th0 = new int[6];
        private int[] _r_rate = new int[6];
        private int[] _g_rate = new int[6];
        private int[] _b_rate = new int[6];
        private int[] _sat = new int[17];
        private int[] _rate = new int[8];

        public CH()
        {
            DeviceModulePos = 9;
        }

        public int stage0_en
        {
            get { return _stage0_en; }
            set
            {
                _stage0_en = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("stage0_en"));
                }
            }
        }

	    public int stage1_en
        {
            get { return _stage1_en; }
            set
            {
                _stage1_en = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("stage1_en"));
                }
            }
        }

	    public int[] enhence
        {
            get { return _enhence; }
            set
            {
                _enhence = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("enhence"));
                }
            }
        }

	    public int[] th1
        {
            get { return _th1; }
            set
            {
                _th1 = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("th1"));
                }
            }
        }

	    public int[] th0
        {
            get { return _th0; }
            set
            {
                _th0 = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("_th0"));
                }
            }
        }

        #region r_rate
        public void SetRRate(int pos, int value)
        {
            _r_rate[pos] = value;
            HasChangedParams = true;
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs("r_rate"));
            }
        }

        public int[] r_rate{ get { return _r_rate; } }
        #endregion

        #region g_rate
        public void SetGRate(int pos, int value)
        {
            _g_rate[pos] = value;
            HasChangedParams = true;
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs("g_rate"));
            }
        }
	    public int[] g_rate { get { return _g_rate; } }
        #endregion

        #region b_rate
        public void SetBRate(int pos, int value)
        {
            _b_rate[pos] = value;
            HasChangedParams = true;
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs("b_rate"));
            }
        }
        public int[] b_rate { get { return _b_rate; } }
        #endregion


	    public int[] sat
        {
            get { return _sat; }
            set
            {
                _sat = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("sat"));
                }
            }
        }

	    public int[] rate
        {
            get { return _rate; }
            set
            {
                _rate = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("rate"));
                }
            }
        }

        public override void ProcessRawBuffer(ref byte[] imgBuffer)
        {
            throw new NotImplementedException();
        }

        public override void ProcessRgbBuffer(ref byte[] imgBuffer)
        {
            throw new NotImplementedException();
        }

        public override Dictionary<int, byte[]> ParamsDataCollection
        {
            get
            {
                CHParams chParams = new CHParams()
                {
                    stage0_en = (byte)stage0_en, 
                    stage1_en = (byte)stage1_en, 
                    enhence = new byte[6],
                    //th1 = th1,
                    //th0 = th0,
                    r_rate = new byte[6],
                    g_rate = new byte[6],
                    b_rate = new byte[6],
                    sat = new byte[17],
                    rate = new byte[8],
                };

                for (int i = 0; i < 17; i++)
                {
                    if (i < 6)
                    {
                        chParams.enhence[i] = (byte)enhence[i];
                        chParams.r_rate[i] = (byte)r_rate[i];
                        chParams.g_rate[i] = (byte)g_rate[i];
                        chParams.b_rate[i] = (byte)b_rate[i];
                        //chParams.th1[i] = (byte)th1[i];
                        //chParams.th0[i] = (byte)th0[i];
                    }
                    if (i < 17)
                    {
                        chParams.sat[i] = (byte)sat[i];
                    }
                    if (i < 8)
                    {
                        chParams.rate[i] = (byte)rate[i];
                    }
                }
                int size = Marshal.SizeOf(chParams);
                byte[] arr = new byte[size];

                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(chParams, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                Marshal.FreeHGlobal(ptr);

                return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
            }
            set
            {
                CHParams chParams = new CHParams();

                int size = Marshal.SizeOf(chParams);
                IntPtr ptr = Marshal.AllocHGlobal(size);

                Marshal.Copy(value[DeviceModulePos], 0, ptr, size);

                chParams = (CHParams)Marshal.PtrToStructure(ptr, chParams.GetType());
                Marshal.FreeHGlobal(ptr);

                stage0_en = chParams.stage0_en; 
                stage1_en = chParams.stage1_en;
                //enhence = chParams.enhence;
                //th1 = chParams.th1;
                //th0 = chParams.th0;

                for (int i = 0; i < 17; i++)
                {
                    if (i < 6)
                    {
                        enhence[i] = chParams.enhence[i];
                        _r_rate[i] = chParams.r_rate[i];
                        _g_rate[i] = chParams.g_rate[i];
                        _b_rate[i] = chParams.b_rate[i]    ;
                    }
                    if (i < 17)
                        {
                            sat[i] = chParams.sat[i];
                    }
                    if (i < 8)
                        rate[i] = chParams.rate[i];
                }

                //_r_rate = chParams.r_rate;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("r_rate"));
                }
                //_g_rate = chParams.g_rate;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("g_rate"));
                }
                //_b_rate = chParams.b_rate;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("b_rate"));
                }
                //sat = chParams.sat;
                //rate = chParams.rate;
            }
        }

        public override System.Xml.XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("CH");

            XmlElement rRateNode = xmlDoc.CreateElement("R_Rate");
            string rRateNodeStr = string.Join(",", r_rate.Select(x => x.ToString()).ToArray());
            rRateNode.AppendChild(xmlDoc.CreateTextNode(rRateNodeStr));
            xmlElement.AppendChild(rRateNode);

            XmlElement gRateNode = xmlDoc.CreateElement("G_Rate");
            string gRateNodeStr = string.Join(",", g_rate.Select(x => x.ToString()).ToArray());
            gRateNode.AppendChild(xmlDoc.CreateTextNode(gRateNodeStr));
            xmlElement.AppendChild(gRateNode);

            XmlElement bRateNode = xmlDoc.CreateElement("B_Rate");
            string bRateNodeStr = string.Join(",", b_rate.Select(x => x.ToString()).ToArray());
            bRateNode.AppendChild(xmlDoc.CreateTextNode(bRateNodeStr));
            xmlElement.AppendChild(bRateNode);

            XmlElement enhenceNode = xmlDoc.CreateElement("enhence");
            string enhenceStr = string.Join(",", enhence.Select(x => x.ToString()).ToArray());
            enhenceNode.AppendChild(xmlDoc.CreateTextNode(enhenceStr));
            xmlElement.AppendChild(enhenceNode);

            // 添加遗漏字段的序列化
            XmlElement stage0EnNode = xmlDoc.CreateElement("stage0_en");
            stage0EnNode.AppendChild(xmlDoc.CreateTextNode(stage0_en.ToString()));
            xmlElement.AppendChild(stage0EnNode);

            XmlElement stage1EnNode = xmlDoc.CreateElement("stage1_en");
            stage1EnNode.AppendChild(xmlDoc.CreateTextNode(stage1_en.ToString()));
            xmlElement.AppendChild(stage1EnNode);

            //XmlElement th1Node = xmlDoc.CreateElement("th1");
            //string th1Str = string.Join(",", th1.Select(x => x.ToString()).ToArray());
            //th1Node.AppendChild(xmlDoc.CreateTextNode(th1Str));
            //xmlElement.AppendChild(th1Node);

            //XmlElement th0Node = xmlDoc.CreateElement("th0");
            //string th0Str = string.Join(",", th0.Select(x => x.ToString()).ToArray());
            //th0Node.AppendChild(xmlDoc.CreateTextNode(th0Str));
            //xmlElement.AppendChild(th0Node);

            XmlElement satNode = xmlDoc.CreateElement("sat");
            string satStr = string.Join(",", sat.Select(x => x.ToString()).ToArray());
            satNode.AppendChild(xmlDoc.CreateTextNode(satStr));
            xmlElement.AppendChild(satNode);

            XmlElement rateNode = xmlDoc.CreateElement("rate");
            string rateStr = string.Join(",", rate.Select(x => x.ToString()).ToArray());
            rateNode.AppendChild(xmlDoc.CreateTextNode(rateStr));
            xmlElement.AppendChild(rateNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(System.Xml.XmlElement ispToolDataNode)
        {
            var CHNode = ispToolDataNode["CH"];

            var tmpRRateStr = XmlHelper.GetNodeValue(CHNode, "R_Rate");
            if (tmpRRateStr != null)
            {
                _r_rate = tmpRRateStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();
            }
            PropertyChanged(this, new PropertyChangedEventArgs("r_rate"));

            var tmpGRateStr = XmlHelper.GetNodeValue(CHNode, "G_Rate");
            if (tmpGRateStr != null)
            {
                _g_rate = tmpGRateStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();
            }
            PropertyChanged(this, new PropertyChangedEventArgs("g_rate"));

            var tmpBRateStr = XmlHelper.GetNodeValue(CHNode, "B_Rate");
            if (tmpBRateStr != null)
            {
                _b_rate = tmpBRateStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();
            }
            PropertyChanged(this, new PropertyChangedEventArgs("b_rate"));

            var tmpEnhenceStr = XmlHelper.GetNodeValue(CHNode, "enhence");
            if (tmpEnhenceStr != null)
            {
                enhence = tmpEnhenceStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();
            }
            // 添加遗漏字段的反序列化
            stage0_en = XmlHelper.GetNodeInt(CHNode, "stage0_en", 0);
            stage1_en = XmlHelper.GetNodeInt(CHNode, "stage1_en", 0);

            //var tmpTh1Str = XmlHelper.GetNodeValue(CHNode, "th1");
            //if (tmpTh1Str != null)
            //{
            //    th1 = tmpTh1Str.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
            //        .Select(s => Convert.ToInt32(s))
            //        .ToArray();
            //}

            //var tmpTh0Str = XmlHelper.GetNodeValue(CHNode, "th0");
            //if (tmpTh0Str != null)
            //{
            //    th0 = tmpTh0Str.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
            //        .Select(s => Convert.ToInt32(s))
            //        .ToArray();
            //}

            var tmpSatStr = XmlHelper.GetNodeValue(CHNode, "sat");
            if (tmpSatStr != null)
            {
                sat = tmpSatStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();
            }

            var tmpRateStr = XmlHelper.GetNodeValue(CHNode, "rate");
            if (tmpRateStr != null)
            {
                rate = tmpRateStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
