using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace ThunderSE.DeviceConfig.Lcd
{
    class LcdCcm : LcdSettingSection, INotifyPropertyChanged
    {
        private int[] _de_ccm = new int[12];

        public int[] de_ccm
        {
            get
            {
                return _de_ccm;
            }
            set
            {
                _de_ccm = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("de_ccm"));
                }
            }
        }

        public override byte[] ParamsData
        {
            get
            {
                byte[] result = new byte[de_ccm.Length * sizeof(int)];
                Buffer.BlockCopy(de_ccm, 0, result, 0, result.Length);

                return result;
            }
            set
            {
                var tmpArray = new int[value.Length / sizeof(int)];
                Buffer.BlockCopy(value, 0, tmpArray, 0, value.Length);
                de_ccm = tmpArray;
            }
        }

        public override System.Xml.XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("Ccm");

            XmlElement deCcmNode = xmlDoc.CreateElement("de_ccm");
            string deCcmNodeStr = string.Join(",", de_ccm.Select(x => x.ToString()).ToArray());
            deCcmNode.AppendChild(xmlDoc.CreateTextNode(deCcmNodeStr));
            xmlElement.AppendChild(deCcmNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(System.Xml.XmlElement LcdNode)
        {
            var LcdCCMNode = LcdNode["Ccm"];

            de_ccm = XmlHelper.GetNodeIntArray(LcdCCMNode, "de_ccm");
            //PropertyChanged(this, new PropertyChangedEventArgs("de_ccm"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ExpAdapt." + de_ccm));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
