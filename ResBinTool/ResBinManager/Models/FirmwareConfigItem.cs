using System.Collections.Generic;
using ResBinManager.Core;

namespace ResBinManager.Models
{
    public enum ConfigId
    {
        CONFIG_ID_YEAR = 0,
        CONFIG_ID_MONTH,
        CONFIG_ID_MDAY,
        CONFIG_ID_WDAY,
        CONFIG_ID_HOUR,
        CONFIG_ID_MIN,
        CONFIG_ID_SEC,
        CONFIG_ID_LANGUAGE,
        CONFIG_ID_LCD_BRIGHT,
        CONFIG_ID_AUTOOFF,
        CONFIG_ID_SCREENSAVE,
        CONFIG_ID_FREQUNCY,
        CONFIG_ID_ROTATE,
        CONFIG_ID_FILLIGHT,
        CONFIG_ID_RESOLUTION,
        CONFIG_ID_TIMEPHOTO,
        CONFIG_ID_TIMESTAMP,
        CONFIG_ID_MOTIONDECTION,
        CONFIG_ID_PARKMODE,
        CONFIG_ID_GSENSOR,
        CONFIG_ID_KEYSOUND,
        CONFIG_ID_IR_LED,
        CONFIG_ID_LOOPTIME,
        CONFIG_ID_AUDIOREC,
        CONFIG_ID_EV,
        CONFIG_ID_WBLANCE,
        CONFIG_ID_PRESLUTION,
        CONFIG_ID_PFASTVIEW,
        CONFIG_ID_PTIMESTRAMP,
        CONFIG_ID_PEV,
        CONFIG_ID_VOLUME,
        CONFIG_ID_THUMBNAIL,
        CONFIG_ID_GSENSORMODE,
        CONFIG_ID_FORMAT,
        CONFIG_ID_DEFUALT,
        CONFIG_ID_VIDEORECEFFECT,
        CONFIG_ID_REINIT,
        CONFIG_ID_MOREPHOTO,
        CONFIG_ID_PRINTER_EN,
        CONFIG_ID_COLOR_PRINT,
        CONFIG_ID_PRINTER_DENSITY,
        CONFIG_ID_PRINTER_MODE,
        CONFIG_ID_PRINTER_NEARFAR,
        CONFIG_ID_PRINTER_DELAY,
        CONFIG_ID_BAT_OLD,
        CONFIG_ID_BAT_CHECK_FLAG,
        CONFIG_ID_DEVICE_ID1,
        CONFIG_ID_DEVICE_ID2,
        CONFIG_ID_DEVICE_ID3,
        CONFIG_ID_DEVICE_ID4,
        CONFIG_ID_DEVICE_ID5,
        CONFIG_ID_DEVICE_ID6,
        CONFIG_ID_PRINTER_DENSITY_H,
        CONFIG_ID_PRINTER_DENSITY_L,
        CONFIG_ID_PRINTER_MOTE_SPEED,
        CONFIG_ID_BT_LED,
        CONFIG_ID_ISP_FILTER,
        CONFIG_ID_VIDEO_RESOLUTION,
        CONFIG_ID_NETWORK_SPEED,
        CONFIG_ID_VIDEOSPEED,
        CONFIG_ID_LINEASSIST,
        CONFIG_ID_MAX
    }

    public class FirmwareConfigItem
    {
        public ConfigId Id { get; set; }
        public string Name { get; set; }
        public uint Value { get; set; }
        public string ValueDisplay { get; set; }
        public string Category { get; set; }
        public List<ConfigOption> Options { get; set; }
        public bool Enabled { get; set; } = false;

        public FirmwareConfigItem()
        {
            Options = new List<ConfigOption>();
        }
    }

    public class ConfigOption
    {
        public uint Value { get; set; }
        public string DisplayName { get; set; }

        public ConfigOption(uint value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }
    }

    public class FirmwareConfigData
    {
        public uint[] Flags { get; set; }
        public uint CheckSum { get; set; }
        public uint ConfigAddress { get; set; }
        public bool IsValid { get; set; }
        public string StatusMessage { get; set; }
        
        public int ActiveConfigCount { get; set; }
        
        public string ConfigVersion { get; set; }

        public ProjectType ProjectType { get; set; }

        public ProjectConfigMapping? Mapping { get; set; }

        public List<ConfigXmlParsedItem>? XmlParsedItems { get; set; }

        public FirmwareConfigData()
        {
            Flags = new uint[127];
            CheckSum = 0;
            ConfigAddress = 0;
            IsValid = false;
            StatusMessage = string.Empty;
            ActiveConfigCount = 0;
            ConfigVersion = "Unknown";
            ProjectType = ProjectType.Unknown;
            Mapping = null;
        }

        public uint CalculateCheckSum()
        {
            uint checkSum = 0;
            for (int i = 0; i < Flags.Length; i++)
            {
                checkSum += Flags[i];
            }
            if (checkSum == 0)
            {
                checkSum = 0xAA55AA55;
            }
            return checkSum;
        }
    }
}