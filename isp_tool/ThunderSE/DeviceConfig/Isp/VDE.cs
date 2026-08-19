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
    class VDE : ProcessStep, INotifyPropertyChanged
    {
        public struct VDEParam
        {
            public byte contra;
            public byte bright_k; // 80 -> 1 gain
            public byte bright_oft; // bright_oft * bright_K
            public byte hue;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
            public byte[] sat;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] sat_rate;
            //public int vde_step;
        }

        private int _contra;
        private int _bright_k; // 80 -> 1 gain
        private int _bright_oft; // bright_oft * bright_K
        private int _hue;
        private int[] _sat = new int[9];
        private int[] _sat_rate = new int[8];
        private int _vde_step;

        public VDE()
        {
            DeviceModulePos = 10;
        }

        public int contra
        {
            get { return _contra; }
            set
            {
                _contra = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("contra"));
                }
            }
        }

        public int bright_k
        {
            get { return _bright_k; }
            set
            {
                _bright_k = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("bright_k"));
                }
            }
        }

        public int bright_oft
        {
            get { return _bright_oft; }
            set
            {
                _bright_oft = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("bright_oft"));
                }
            }
        }

        public int hue
        {
            get { return _hue; }
            set
            {
                _hue = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("hue"));
                }
            }
        }

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

        public int[] sat_rate
        {
            get { return _sat_rate; }
            set
            {
                _sat_rate = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("sat_rate"));
                }
            }
        }

        public int vde_step
        {
            get { return _vde_step; }
            set
            {
                _vde_step = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("vde_step"));
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
                VDEParam vdeParams = new VDEParam()
                {
                    contra = (byte)contra,
                    bright_k = (byte)bright_k,
                    bright_oft = (byte)bright_oft,
                    hue = (byte)hue,
                    sat = new byte[9],
                    sat_rate = new byte[8],
                    //vde_step = vde_step
                };

                for (int i = 0; i < 9; i++)
                {
                    if (i < 8)
                    {
                        vdeParams.sat_rate[i] = (byte)sat_rate[i];
                    }
                    vdeParams.sat[i] = (byte)sat[i];
                }

                int size = Marshal.SizeOf(vdeParams);
                byte[] arr = new byte[size];

                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(vdeParams, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                Marshal.FreeHGlobal(ptr);

                return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
            }
            set
            {
                VDEParam vdeParams = new VDEParam();

                int size = Marshal.SizeOf(vdeParams);
                IntPtr ptr = Marshal.AllocHGlobal(size);

                Marshal.Copy(value[DeviceModulePos], 0, ptr, size);

                vdeParams = (VDEParam)Marshal.PtrToStructure(ptr, vdeParams.GetType());
                Marshal.FreeHGlobal(ptr);

                contra = vdeParams.contra;
                bright_k = vdeParams.bright_k;
                bright_oft = vdeParams.bright_oft;
                hue = vdeParams.hue;
                //sat = vdeParams.sat;
                //sat_rate = vdeParams.sat_rate;
                //vde_step = vdeParams.vde_step;

                for (int i = 0; i < 9; i++)
                {
                    if (i < 8)
                    {
                        sat_rate[i] = vdeParams.sat_rate[i];
                    }
                    sat[i] = vdeParams.sat[i];
                }
            }
        }

        public override System.Xml.XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("VDE");

            XmlElement satRateNode = xmlDoc.CreateElement("sat_rate");
            string satRateStr = string.Join(",", sat_rate.Select(x => x.ToString()).ToArray());
            satRateNode.AppendChild(xmlDoc.CreateTextNode(satRateStr));
            xmlElement.AppendChild(satRateNode);

            XmlElement contraNode = xmlDoc.CreateElement("contra");
            contraNode.AppendChild(xmlDoc.CreateTextNode(contra.ToString()));
            xmlElement.AppendChild(contraNode);

            XmlElement brightKNode = xmlDoc.CreateElement("bright_k");
            brightKNode.AppendChild(xmlDoc.CreateTextNode(bright_k.ToString()));
            xmlElement.AppendChild(brightKNode);

            XmlElement brightOftNode = xmlDoc.CreateElement("bright_oft");
            brightOftNode.AppendChild(xmlDoc.CreateTextNode(bright_oft.ToString()));
            xmlElement.AppendChild(brightOftNode);

            XmlElement hueNode = xmlDoc.CreateElement("hue");
            hueNode.AppendChild(xmlDoc.CreateTextNode(hue.ToString()));
            xmlElement.AppendChild(hueNode);

            // 添加遗漏字段的序列化
            XmlElement satNode = xmlDoc.CreateElement("sat");
            string satStr = string.Join(",", sat.Select(x => x.ToString()).ToArray());
            satNode.AppendChild(xmlDoc.CreateTextNode(satStr));
            xmlElement.AppendChild(satNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(System.Xml.XmlElement ispToolDataNode)
        {
            var VDENode = ispToolDataNode["VDE"];

            var tmpSatRateStr = XmlHelper.GetNodeValue(VDENode, "sat_rate");
            if (tmpSatRateStr != null)
            {
                sat_rate = tmpSatRateStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();
            }

            contra = XmlHelper.GetNodeInt(VDENode, "contra");
            bright_k = XmlHelper.GetNodeInt(VDENode, "bright_k");
            bright_oft = XmlHelper.GetNodeInt(VDENode, "bright_oft");
            hue = XmlHelper.GetNodeInt(VDENode, "hue");
            // 添加遗漏字段的反序列化
            var tmpSatStr = XmlHelper.GetNodeValue(VDENode, "sat");
            if (tmpSatStr != null)
            {
                sat = tmpSatStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
