using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace ThunderSE.DeviceConfig.Lcd
{
    public abstract class LcdSettingSection
    {
        public LcdSettingSection()
        {
            HasChangedParams = false;
        }

        public bool HasChangedParams
        {
            get;
            set;
        }

        abstract public byte[] ParamsData
        {
            get;
            set;
        }

        abstract public XmlElement SerializeToXmlElement(XmlDocument xmlDoc);

        abstract public void DeserializeFromXmlElement(XmlElement ispToolDataNode);
    }
}
