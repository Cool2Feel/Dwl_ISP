using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace ThunderSE.DeviceConfig.Lcd
{
    class LcdVde : LcdSettingSection, INotifyPropertyChanged
    {
        private int _contrast = 0;
        private int _brightness = 0;
        private int _saturation = 0;

        public int contrast
        {
            get
            {
                return _contrast;
            }
            set
            {
                _contrast = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("contrast"));
                }
            }
        }

        public int brightness
        {
            get
            {
                return _brightness;
            }
            set
            {
                _brightness = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("brightness"));
                }
            }
        }

        public int saturation
        {
            get
            {
                return _saturation;
            }
            set
            {
                _saturation = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("saturation"));
                }
            }
        }


        public override byte[] ParamsData
        {
            get
            {
                lcd_vde_t lcdVdeParams = new lcd_vde_t()
                {
                    contrast = contrast,
                    brightness = brightness,
                    saturation = saturation
                };

                int size = Marshal.SizeOf(lcdVdeParams);
                byte[] arr = new byte[size];

                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(lcdVdeParams, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                Marshal.FreeHGlobal(ptr);

                return arr;
            }
            set
            {
                lcd_vde_t lcdVdeParams = new lcd_vde_t();
                int size = Marshal.SizeOf(lcdVdeParams);
                IntPtr ptr = Marshal.AllocHGlobal(size);

                Marshal.Copy(value, 0, ptr, size);

                lcdVdeParams = (lcd_vde_t)Marshal.PtrToStructure(ptr, lcdVdeParams.GetType());
                Marshal.FreeHGlobal(ptr);

                contrast = lcdVdeParams.contrast;
                brightness = lcdVdeParams.brightness;
                saturation = lcdVdeParams.saturation;
            }
        }

        public override System.Xml.XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("Vde");

            XmlElement contrastNode = xmlDoc.CreateElement("contrast");
            contrastNode.AppendChild(xmlDoc.CreateTextNode(contrast.ToString()));
            xmlElement.AppendChild(contrastNode);

            XmlElement brightnessNode = xmlDoc.CreateElement("brightness");
            brightnessNode.AppendChild(xmlDoc.CreateTextNode(brightness.ToString()));
            xmlElement.AppendChild(brightnessNode);

            XmlElement saturationNode = xmlDoc.CreateElement("saturation");
            saturationNode.AppendChild(xmlDoc.CreateTextNode(saturation.ToString()));
            xmlElement.AppendChild(saturationNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(System.Xml.XmlElement LcdNode)
        {
            var VdeNode = LcdNode["Vde"];

            contrast = XmlHelper.GetNodeShort(VdeNode, "contrast");
            brightness = XmlHelper.GetNodeShort(VdeNode, "brightness");
            saturation = XmlHelper.GetNodeShort(VdeNode, "saturation");
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
