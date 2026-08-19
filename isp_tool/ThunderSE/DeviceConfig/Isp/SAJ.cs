using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;

namespace ThunderSE.DeviceConfig.Isp
{
    class SAJ : ProcessStep, INotifyPropertyChanged
    {
        public struct SAJParam
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public byte[] sat;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] sat_rate;
            //public byte saj_step;
        }


        private byte[] _sat = new byte[17];
        private byte[] _sat_rate = new byte[8];
        private byte _saj_step;


        public SAJ()
        {
            DeviceModulePos = 13;
        }


        public byte[] sat
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

        public byte[] sat_rate
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

        public byte saj_step
        {
            get { return _saj_step; }
            set
            {
                _saj_step = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("saj_step"));
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
                SAJParam sajParams = new SAJParam()
                {
                    sat = sat,
                    sat_rate = sat_rate,
                    //saj_step = saj_step,
                };

                int size = Marshal.SizeOf(sajParams);
                byte[] arr = new byte[size];

                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(sajParams, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                Marshal.FreeHGlobal(ptr);

                return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
            }
            set
            {
                SAJParam sajParams = new SAJParam();

                int size = Marshal.SizeOf(sajParams);
                IntPtr ptr = Marshal.AllocHGlobal(size);

                Marshal.Copy(value[DeviceModulePos], 0, ptr, size);

                sajParams = (SAJParam)Marshal.PtrToStructure(ptr, sajParams.GetType());
                Marshal.FreeHGlobal(ptr);

                sat = sajParams.sat;
                sat_rate = sajParams.sat_rate;
                //saj_step = sajParams.saj_step;
            }
        }

        public override System.Xml.XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("SAJ");

            XmlElement satRateNode = xmlDoc.CreateElement("sat_rate");
            string satRateStr = string.Join(",", sat_rate.Select(x => x.ToString()).ToArray());
            satRateNode.AppendChild(xmlDoc.CreateTextNode(satRateStr));
            xmlElement.AppendChild(satRateNode);

            // 添加遗漏字段的序列化
            XmlElement satNode = xmlDoc.CreateElement("sat");
            string satStr = string.Join(",", sat.Select(x => x.ToString()).ToArray());
            satNode.AppendChild(xmlDoc.CreateTextNode(satStr));
            xmlElement.AppendChild(satNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(System.Xml.XmlElement ispToolDataNode)
        {
            var SAJNode = ispToolDataNode["SAJ"];

            var tmpSatRateStr = XmlHelper.GetNodeValue(SAJNode, "sat_rate");
            if (tmpSatRateStr != null)
            {
                sat_rate = tmpSatRateStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }
            // 添加遗漏字段的反序列化
            var tmpSatStr = XmlHelper.GetNodeValue(SAJNode, "sat");
            if (tmpSatStr != null)
            {
                sat = tmpSatStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }

        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
