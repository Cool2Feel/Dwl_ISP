using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ThunderSE.DeviceConfig.Lcd
{
    class LcdCommon : LcdSettingSection, INotifyPropertyChanged
    {
        private string _name = "";
        private short _screen_w = 0;
        private short _screen_h = 0;

        public string name
        {
            get
            {
                return _name;
            }
            set
            {
                _name = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("name"));
                }
            }
        }

        public short screen_w
        {
            get
            {
                return _screen_w;
            }
            set
            {
                _screen_w = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("screen_w"));
                }
            }
        }

        public short screen_h
        {
            get
            {
                return _screen_h;
            }
            set
            {
                _screen_h = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("screen_h"));
                }
            }
        }


        public override byte[] ParamsData
        {
            get
            {
                lcd_common_t lcdCommonParams = new lcd_common_t()
                {
                    name = name.ToCharArray(),
                    screen_w = screen_w,
                    screen_h = screen_h,
                };

                int size = Marshal.SizeOf(lcdCommonParams);
                byte[] arr = new byte[size];

                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(lcdCommonParams, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                Marshal.FreeHGlobal(ptr);

                return arr;
            }
            set
            {
                lcd_common_t lcdCommonParams = new lcd_common_t();
                int size = Marshal.SizeOf(lcdCommonParams);
                IntPtr ptr = Marshal.AllocHGlobal(size);

                Marshal.Copy(value, 0, ptr, size);

                lcdCommonParams = (lcd_common_t)Marshal.PtrToStructure(ptr, lcdCommonParams.GetType());
                Marshal.FreeHGlobal(ptr);

                name = new string(lcdCommonParams.name);
                name = name.Replace("\0", string.Empty);
                screen_w = lcdCommonParams.screen_w;
                screen_h = lcdCommonParams.screen_h;
            }
        }

        public override System.Xml.XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            throw new NotImplementedException();
        }

        public override void DeserializeFromXmlElement(System.Xml.XmlElement ispToolDataNode)
        {
            throw new NotImplementedException();
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
