using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace ThunderSE.DeviceConfig.Lcd
{
    class LcdGamma : LcdSettingSection, INotifyPropertyChanged
    {
        private int _contra_index;
        private int _gamma_red;
        private int _gamma_green;
        private int _gamma_blue;

        public int contra_index
        {
            get
            {
                return _contra_index;
            }
            set
            {
                _contra_index = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("contra_index"));
                }
            }
        }

        public int gamma_red
        {
            get
            {
                return _gamma_red;
            }
            set
            {
                _gamma_red = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("gamma_red"));
                }
            }
        }

        public int gamma_green
        {
            get
            {
                return _gamma_green;
            }
            set
            {
                _gamma_green = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("gamma_green"));
                }
            }
        }

        public int gamma_blue
        {
            get
            {
                return _gamma_blue;
            }
            set
            {
                _gamma_blue = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("gamma_blue"));
                }
            }
        }


        public override byte[] ParamsData
        {
            get
            {
                lcd_gamma_t lcdGammaParams = new lcd_gamma_t()
                {
                     contra_index = contra_index,
                     gamma_red = gamma_red,
                     gamma_green = gamma_green,
                     gamma_blue = gamma_blue,
                };

                int size = Marshal.SizeOf(lcdGammaParams);
                byte[] arr = new byte[size];

                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(lcdGammaParams, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                Marshal.FreeHGlobal(ptr);

                return arr;
            }
            set
            {
                lcd_gamma_t lcdGammaParams = new lcd_gamma_t();
                int size = Marshal.SizeOf(lcdGammaParams);
                IntPtr ptr = Marshal.AllocHGlobal(size);

                Marshal.Copy(value, 0, ptr, size);

                lcdGammaParams = (lcd_gamma_t)Marshal.PtrToStructure(ptr, lcdGammaParams.GetType());
                Marshal.FreeHGlobal(ptr);

                contra_index = lcdGammaParams.contra_index;
                gamma_red = lcdGammaParams.gamma_red;
                gamma_green = lcdGammaParams.gamma_green;
                gamma_blue = lcdGammaParams.gamma_blue;
            }
        }

        public override XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("Gamma");

            XmlElement gammaRedNode = xmlDoc.CreateElement("gamma_red");
            gammaRedNode.AppendChild(xmlDoc.CreateTextNode(gamma_red.ToString()));
            xmlElement.AppendChild(gammaRedNode);

            XmlElement gammaGreenNode = xmlDoc.CreateElement("gamma_green");
            gammaGreenNode.AppendChild(xmlDoc.CreateTextNode(gamma_green.ToString()));
            xmlElement.AppendChild(gammaGreenNode);

            XmlElement gammaBlueNode = xmlDoc.CreateElement("gamma_blue");
            gammaBlueNode.AppendChild(xmlDoc.CreateTextNode(gamma_blue.ToString()));
            xmlElement.AppendChild(gammaBlueNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(XmlElement LcdNode)
        {
            var blcNode = LcdNode["Gamma"];

            gamma_red = XmlHelper.GetNodeShort(blcNode, "gamma_red");
            gamma_green = XmlHelper.GetNodeShort(blcNode, "gamma_green");
            gamma_blue = XmlHelper.GetNodeShort(blcNode, "gamma_blue");
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
