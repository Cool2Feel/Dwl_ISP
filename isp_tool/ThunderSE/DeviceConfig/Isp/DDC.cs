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
    class DDC : ProcessStep, INotifyPropertyChanged
    {
        /*
        private struct DDCParams
        {
	        public int hot_num;
	        public int dead_num;
	        public int hot_th;
	        public int dead_th;
	        public int avg_th;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
	        public int[] d_th_rate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
	        public int[] h_th_rate;
	        public int dpc_dn_en;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
	        public int[] indx_table;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
	        public int[] indx_adapt;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
	        public int[] std_th;
            public int std_th_rate;
            public int ddc_step;
            public int ddc_class;
        }
        */

        private struct DDCParams
        {
            public byte hot_num;
            public byte dead_num;
            public ushort hot_th;
            public ushort dead_th;
            public byte avg_th;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] d_th_rate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] h_th_rate;
            public byte dpc_dn_en;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public uint[] indx_table;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] indx_adapt;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            public ushort[] std_th;
            public byte std_th_rate;
            //public int ddc_step;
            //public int ddc_class;
        }

        public DDC()
        {
            DeviceModulePos = 3;
        }

        private int _hot_num;
        private int _dead_num;
        private int _hot_th;
        private int _dead_th;
        private int _avg_th;

        private int[] _d_th_rate = new int[8];
        private int[] _h_th_rate = new int[8];
        private int _dpc_dn_en;
        private uint[] _indx_table = new uint[8];
        private int[] _indx_adapt = new int[8];
        private int[] _std_th = new int[7];
        private int _std_th_rate;
        private int _ddc_step;
        private int _ddc_class;

        public int hot_num
        {
            get { return _hot_num; }
            set
            {
                _hot_num = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("hot_num"));
                }
            }
        }

        public int dead_num
        {
            get { return _dead_num; }
            set
            {
                _dead_num = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("dead_num"));
                }
            }
        }

        public int hot_th
        {
            get { return _hot_th; }
            set
            {
                _hot_th = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("hot_th"));
                }
            }
        }

        public int dead_th
        {
            get { return _dead_th; }
            set
            {
                _dead_th = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("dead_th"));
                }
            }
        }
        public int avg_th
        {
            get { return _avg_th; }
            set
            {
                _avg_th = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("avg_th"));
                }
            }
        }

        public int[] d_th_rate
        {
            get { return _d_th_rate; }
            set
            {
                _d_th_rate = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("d_th_rate"));
                }
            }
        }
        public int[] h_th_rate
        {
            get { return _h_th_rate; }
            set
            {
                _h_th_rate = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("h_th_rate"));
                }
            }
        }
        public int dpc_dn_en
        {
            get { return _dpc_dn_en; }
            set
            {
                _dpc_dn_en = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("dpc_dn_en"));
                }
            }
        }

        public uint[] indx_table
        {
            get { return _indx_table; }
            set
            {
                _indx_table = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("indx_table"));
                }
            }
        }

        public int[] indx_adapt
        {
            get { return _indx_adapt; }
            set
            {
                _indx_adapt = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("indx_adapt"));
                }
            }
        }

        public int[] std_th
        {
            get { return _std_th; }
            set
            {
                _std_th = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("std_th"));
                }
            }
        }

        public int std_th_rate
        {
            get { return _std_th_rate; }
            set
            {
                _std_th_rate = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("std_th_rate"));
                }
            }
        }

        public int ddc_step
        {
            get { return _ddc_step; }
            set
            {
                _ddc_step = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ddc_step"));
                }
            }
        }

        public int ddc_class
        {
            get { return _ddc_class; }
            set
            {
                _ddc_class = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ddc_class"));
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
                DDCParams ddcParams = new DDCParams()
                {
                    hot_num = (byte)hot_num,
                    dead_num = (byte)dead_num,
                    hot_th = (ushort)hot_th,
                    dead_th = (ushort)dead_th,
                    avg_th = (byte)avg_th,
                    d_th_rate = new byte[8],
                    h_th_rate = new byte[8],
                    //d_th_rate = d_th_rate,
                    //h_th_rate = h_th_rate,
                    dpc_dn_en = (byte)dpc_dn_en,
                    indx_table = new uint[8],
                    indx_adapt = new byte[8],
                    std_th = new ushort[7],
                    //indx_table = indx_table,
                    //indx_adapt = indx_adapt,
                    //std_th = std_th,
                    std_th_rate = (byte)std_th_rate
                    //ddc_step = ddc_step,
                    //ddc_class = ddc_class
                };
                for (int i = 0; i < 8; i++)
                {
                    ddcParams.d_th_rate[i] = (byte)(d_th_rate[i]);
                    ddcParams.h_th_rate[i] = (byte)(h_th_rate[i]);
                    ddcParams.indx_table[i] = (uint)(indx_table[i]);
                    ddcParams.indx_adapt[i] = (byte)(indx_adapt[i]);
                }
                for (int i = 0; i < 7; i++)
                {
                    ddcParams.std_th[i] = (byte)(std_th[i]);
                }

                int size = Marshal.SizeOf(ddcParams);
                byte[] arr = new byte[size];

                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(ddcParams, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                Marshal.FreeHGlobal(ptr);

                return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
            }
            set
            {
                DDCParams ddcParams = new DDCParams();

                int size = Marshal.SizeOf(ddcParams);
                IntPtr ptr = Marshal.AllocHGlobal(size);

                Marshal.Copy(value[DeviceModulePos], 0, ptr, size);

                ddcParams = (DDCParams)Marshal.PtrToStructure(ptr, ddcParams.GetType());
                Marshal.FreeHGlobal(ptr);

                hot_num = ddcParams.hot_num;
                dead_num = ddcParams.dead_num;
                hot_th = ddcParams.hot_th;
                dead_th = ddcParams.dead_th;
                avg_th = ddcParams.avg_th;

                for (int i = 0; i < 8; i++)
                {
                    d_th_rate[i] = ddcParams.d_th_rate[i];
                    h_th_rate[i] = ddcParams.h_th_rate[i];
                    indx_table[i] = ddcParams.indx_table[i];
                    indx_adapt[i] = ddcParams.indx_adapt[i];
                }
                for (int i = 0; i < 7; i++)
                {
                    std_th[i] = ddcParams.std_th[i];
                }
                dpc_dn_en = ddcParams.dpc_dn_en;
                std_th_rate = ddcParams.std_th_rate;
            }
        }

        public override System.Xml.XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("DDC");
            XmlElement hotNumNode = xmlDoc.CreateElement("hot_num");
            hotNumNode.AppendChild(xmlDoc.CreateTextNode(hot_num.ToString()));
            xmlElement.AppendChild(hotNumNode);

            XmlElement deadNumNode = xmlDoc.CreateElement("dead_num");
            deadNumNode.AppendChild(xmlDoc.CreateTextNode(dead_num.ToString()));
            xmlElement.AppendChild(deadNumNode);

            XmlElement hotThNode = xmlDoc.CreateElement("hot_th");
            hotThNode.AppendChild(xmlDoc.CreateTextNode(hot_th.ToString()));
            xmlElement.AppendChild(hotThNode);

            XmlElement deadThNode = xmlDoc.CreateElement("dead_th");
            deadThNode.AppendChild(xmlDoc.CreateTextNode(dead_th.ToString()));
            xmlElement.AppendChild(deadThNode);

            XmlElement avgThNode = xmlDoc.CreateElement("avg_th");
            avgThNode.AppendChild(xmlDoc.CreateTextNode(avg_th.ToString()));
            xmlElement.AppendChild(avgThNode);

            XmlElement dThRateNode = xmlDoc.CreateElement("d_th_rate");
            string dThRateStr = string.Join(",", d_th_rate.Select(x => x.ToString()).ToArray());
            dThRateNode.AppendChild(xmlDoc.CreateTextNode(dThRateStr));
            xmlElement.AppendChild(dThRateNode);

            XmlElement hThRateNode = xmlDoc.CreateElement("h_th_rate");
            string hThRateStr = string.Join(",", h_th_rate.Select(x => x.ToString()).ToArray());
            hThRateNode.AppendChild(xmlDoc.CreateTextNode(hThRateStr));
            xmlElement.AppendChild(hThRateNode);

            XmlElement dpcDnEnNode = xmlDoc.CreateElement("dpc_dn_en");
            dpcDnEnNode.AppendChild(xmlDoc.CreateTextNode(dpc_dn_en.ToString()));
            xmlElement.AppendChild(dpcDnEnNode);

            XmlElement indxTableNode = xmlDoc.CreateElement("indx_table");
            string indxTableStr = string.Join(",", indx_table.Select(x => x.ToString()).ToArray());
            indxTableNode.AppendChild(xmlDoc.CreateTextNode(indxTableStr));
            xmlElement.AppendChild(indxTableNode);

            XmlElement indxAdaptNode = xmlDoc.CreateElement("indx_adapt");
            string indxAdaptStr = string.Join(",", indx_adapt.Select(x => x.ToString()).ToArray());
            indxAdaptNode.AppendChild(xmlDoc.CreateTextNode(indxAdaptStr));
            xmlElement.AppendChild(indxAdaptNode);

            XmlElement stdThNode = xmlDoc.CreateElement("std_th");
            string stdThStr = string.Join(",", std_th.Select(x => x.ToString()).ToArray());
            stdThNode.AppendChild(xmlDoc.CreateTextNode(stdThStr));
            xmlElement.AppendChild(stdThNode);

            XmlElement stdThRateNode = xmlDoc.CreateElement("std_th_rate");
            stdThRateNode.AppendChild(xmlDoc.CreateTextNode(std_th_rate.ToString()));
            xmlElement.AppendChild(stdThRateNode);

            //XmlElement ddcStepNode = xmlDoc.CreateElement("ddc_step");
            //ddcStepNode.AppendChild(xmlDoc.CreateTextNode(ddc_step.ToString()));
            //xmlElement.AppendChild(ddcStepNode);

            //XmlElement ddcClassNode = xmlDoc.CreateElement("ddc_class");
            //ddcClassNode.AppendChild(xmlDoc.CreateTextNode(ddc_class.ToString()));
            //xmlElement.AppendChild(ddcClassNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(System.Xml.XmlElement ispToolDataNode)
        {
            var DDCNode = ispToolDataNode["DDC"];

            hot_num = XmlHelper.GetNodeInt(DDCNode, "hot_num", 0);
            dead_num = XmlHelper.GetNodeInt(DDCNode, "dead_num", 0);
            hot_th = XmlHelper.GetNodeInt(DDCNode, "hot_th", 0);
            dead_th = XmlHelper.GetNodeInt(DDCNode, "dead_th", 0);
            avg_th = XmlHelper.GetNodeInt(DDCNode, "avg_th", 0);

            var tmpDThRateStr = XmlHelper.GetNodeValue(DDCNode, "d_th_rate");
            if (tmpDThRateStr != null)
            {
                d_th_rate = tmpDThRateStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();
            }

            var tmpHThRateStr = XmlHelper.GetNodeValue(DDCNode, "h_th_rate");
            if (tmpHThRateStr != null)
            {
                h_th_rate = tmpHThRateStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();
            }

            dpc_dn_en = XmlHelper.GetNodeInt(DDCNode, "dpc_dn_en", 0);

            var tmpIndxTableStr = XmlHelper.GetNodeValue(DDCNode, "indx_table");
            if (tmpIndxTableStr != null)
            {
                indx_table = tmpIndxTableStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToUInt32(s))
                    .ToArray();
            }

            var tmpIndxAdaptStr = XmlHelper.GetNodeValue(DDCNode, "indx_adapt");
            if (tmpIndxAdaptStr != null)
            {
                indx_adapt = tmpIndxAdaptStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();
            }

            var tmpStdThStr = XmlHelper.GetNodeValue(DDCNode, "std_th");
            if (tmpStdThStr != null)
            {
                std_th = tmpStdThStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();
            }

            std_th_rate = XmlHelper.GetNodeInt(DDCNode, "std_th_rate", 0);

        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
