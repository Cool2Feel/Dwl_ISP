using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace ThunderSE.DeviceConfig.Lcd
{
    class LcdSaj : LcdSettingSection, INotifyPropertyChanged
    {
        private int[] _de_saj = new int[5];

        public int[] de_saj
        {
            get
            {
                return _de_saj;
            }
            set
            {
                _de_saj = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("de_saj"));
                }
            }
        }

        public override byte[] ParamsData
        {
            get
            {
                byte[] result = new byte[de_saj.Length * sizeof(int)];
                Buffer.BlockCopy(de_saj, 0, result, 0, result.Length);

                return result;
            }
            set
            {
                var tmpArray = new int[value.Length / sizeof(int)];
                Buffer.BlockCopy(value, 0, tmpArray, 0, value.Length);
                de_saj = tmpArray;
            }
        }

        public override System.Xml.XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("Saj");

            XmlElement deSajNode = xmlDoc.CreateElement("de_saj");
            string rRateNodeStr = string.Join(",", de_saj.Select(x => x.ToString()).ToArray());
            deSajNode.AppendChild(xmlDoc.CreateTextNode(rRateNodeStr));
            xmlElement.AppendChild(deSajNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(System.Xml.XmlElement LcdNode)
        {
            var SajNode = LcdNode["Saj"];

            de_saj = XmlHelper.GetNodeIntArray(SajNode, "de_saj");
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
