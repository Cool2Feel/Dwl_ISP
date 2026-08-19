using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ThunderSE.DeviceConfig;

namespace ThunderSE.DeviceConfig.Isp
{
    class EE : ProcessStep, INotifyPropertyChanged
    {
        private struct EEParams
        {
            public byte ee_class;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] ee_dn_slope;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] ee_sharp_slope;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] ee_th_adp;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] ee_dn_th; 
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] sharp_class;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] dn_class;
        }

        private byte _ee_class;
        private byte[] _ee_dn_slope = new byte[8];
        private byte[] _ee_sharp_slope = new byte[8];
        private byte[] _ee_th_adp = new byte[8];
        private byte[] _ee_dn_th = new byte[8];
        private byte[] _sharp_class = new byte[8];
        private byte[] _dn_class = new byte[8];

        public EE()
        {
            DeviceModulePos = 11;
        }

        public byte ee_class
        {
            get { return _ee_class; }
            set
            {
                _ee_class = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ee_class"));
                }
            }
        }

        public byte[] ee_dn_slope
        {
            get { return _ee_dn_slope; }
            set
            {
                _ee_dn_slope = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ee_dn_slope"));
                }
            }
        }

        public byte[] ee_sharp_slope
        {
            get { return _ee_sharp_slope; }
            set
            {
                _ee_sharp_slope = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ee_sharp_slope"));
                }
            }
        }

        public byte[] ee_th_adp
        {
            get { return _ee_th_adp; }
            set
            {
                _ee_th_adp = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ee_th_adp"));
                }
            }
        }

        public byte[] ee_dn_th
        {
            get { return _ee_dn_th; }
            set
            {
                _ee_dn_th = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ee_dn_th"));
                }
            }
        }

        public byte[] sharp_class
        {
            get { return _sharp_class; }
            set
            {
                _sharp_class = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("sharp_class"));
                }
            }
        }

        public byte[] dn_class
        {
            get { return _dn_class; }
            set
            {
                _dn_class = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("dn_class"));
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
                EEParams eeParams = new EEParams()
                {
                    ee_class = ee_class,
                    ee_dn_slope = ee_dn_slope,
                    ee_sharp_slope = ee_sharp_slope,
                    ee_th_adp = ee_th_adp,
                    ee_dn_th = ee_dn_th,
                    sharp_class = sharp_class,
                    dn_class = dn_class
                };

                int size = Marshal.SizeOf(eeParams);
                byte[] arr = new byte[size];

                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(eeParams, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                Marshal.FreeHGlobal(ptr);

                return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
            }
            set
            {
                EEParams eeParams = new EEParams();

                int size = Marshal.SizeOf(eeParams);
                IntPtr ptr = Marshal.AllocHGlobal(size);

                Marshal.Copy(value[DeviceModulePos], 0, ptr, size);

                eeParams = (EEParams)Marshal.PtrToStructure(ptr, eeParams.GetType());
                Marshal.FreeHGlobal(ptr);

                ee_class = eeParams.ee_class;
                ee_dn_slope = eeParams.ee_dn_slope;
                ee_sharp_slope = eeParams.ee_sharp_slope;
                ee_th_adp = eeParams.ee_th_adp;
                ee_dn_th = eeParams.ee_dn_th;
                sharp_class = eeParams.sharp_class;
                dn_class = eeParams.dn_class;
            }
        }

        public override XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("EE");

            XmlElement eeClassNode = xmlDoc.CreateElement("ee_class");
            eeClassNode.AppendChild(xmlDoc.CreateTextNode(ee_class.ToString()));
            xmlElement.AppendChild(eeClassNode);

            // 添加遗漏字段的序列化
            XmlElement eeDnSlopeNode = xmlDoc.CreateElement("ee_dn_slope");
            string eeDnSlopeStr = string.Join(",", ee_dn_slope.Select(x => x.ToString()).ToArray());
            eeDnSlopeNode.AppendChild(xmlDoc.CreateTextNode(eeDnSlopeStr));
            xmlElement.AppendChild(eeDnSlopeNode);

            XmlElement eeSharpSlopeNode = xmlDoc.CreateElement("ee_sharp_slope");
            string eeSharpSlopeStr = string.Join(",", ee_sharp_slope.Select(x => x.ToString()).ToArray());
            eeSharpSlopeNode.AppendChild(xmlDoc.CreateTextNode(eeSharpSlopeStr));
            xmlElement.AppendChild(eeSharpSlopeNode);

            XmlElement eeThAdpNode = xmlDoc.CreateElement("ee_th_adp");
            string eeThAdpStr = string.Join(",", ee_th_adp.Select(x => x.ToString()).ToArray());
            eeThAdpNode.AppendChild(xmlDoc.CreateTextNode(eeThAdpStr));
            xmlElement.AppendChild(eeThAdpNode);

            XmlElement eeDnThNode = xmlDoc.CreateElement("ee_dn_th");
            string eeDnThStr = string.Join(",", ee_dn_th.Select(x => x.ToString()).ToArray());
            eeDnThNode.AppendChild(xmlDoc.CreateTextNode(eeDnThStr));
            xmlElement.AppendChild(eeDnThNode);

            XmlElement sharpClassNode = xmlDoc.CreateElement("sharp_class");
            string sharpClassStr = string.Join(",", sharp_class.Select(x => x.ToString()).ToArray());
            sharpClassNode.AppendChild(xmlDoc.CreateTextNode(sharpClassStr));
            xmlElement.AppendChild(sharpClassNode);

            XmlElement dnClassNode = xmlDoc.CreateElement("dn_class");
            string dnClassStr = string.Join(",", dn_class.Select(x => x.ToString()).ToArray());
            dnClassNode.AppendChild(xmlDoc.CreateTextNode(dnClassStr));
            xmlElement.AppendChild(dnClassNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
        {
            var eeNode = ispToolDataNode["EE"];

            ee_class = (byte)XmlHelper.GetNodeInt(eeNode, "ee_class", 0);

            // 添加遗漏字段的反序列化
            var tmpEeDnSlopeStr = XmlHelper.GetNodeValue(eeNode, "ee_dn_slope");
            if (tmpEeDnSlopeStr != null)
            {
                ee_dn_slope = tmpEeDnSlopeStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }

            var tmpEeSharpSlopeStr = XmlHelper.GetNodeValue(eeNode, "ee_sharp_slope");
            if (tmpEeSharpSlopeStr != null)
            {
                ee_sharp_slope = tmpEeSharpSlopeStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }

            var tmpEeThAdpStr = XmlHelper.GetNodeValue(eeNode, "ee_th_adp");
            if (tmpEeThAdpStr != null)
            {
                ee_th_adp = tmpEeThAdpStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }

            var tmpEeDnThStr = XmlHelper.GetNodeValue(eeNode, "ee_dn_th");
            if (tmpEeDnThStr != null)
            {
                ee_dn_th = tmpEeDnThStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }

            var tmpSharpClassStr = XmlHelper.GetNodeValue(eeNode, "sharp_class");
            if (tmpSharpClassStr != null)
            {
                sharp_class = tmpSharpClassStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }

            var tmpDnClassStr = XmlHelper.GetNodeValue(eeNode, "dn_class");
            if (tmpDnClassStr != null)
            {
                dn_class = tmpDnClassStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
