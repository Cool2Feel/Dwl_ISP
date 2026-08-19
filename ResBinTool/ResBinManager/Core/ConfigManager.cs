using System;
using System.Collections.Generic;
using System.Linq;
using ResBinManager.Models;

namespace ResBinManager.Core
{
    public class ConfigManager
    {
        private const int CONFIG_FLAGS_COUNT = 127;
        private const int CONFIG_STRUCT_SIZE = (CONFIG_FLAGS_COUNT + 1) * 4;
        private const uint CHECKSUM_MAGIC_VALUE = 0xAA55AA55;

        private uint[] _configFlags;
        private uint _checkSum;
        private uint _configAddress;
        private bool _isValid;
        private bool _isModified;
        private ProjectConfigMapping? _mapping;

        public uint[] ConfigFlags => _configFlags;
        public uint CheckSum => _checkSum;
        public uint ConfigAddress => _configAddress;
        public bool IsValid => _isValid;
        public bool IsModified => _isModified;
        public ProjectConfigMapping? Mapping => _mapping;

        public ConfigManager()
        {
            _configFlags = new uint[CONFIG_FLAGS_COUNT];
            _checkSum = 0;
            _configAddress = 0;
            _isValid = false;
            _isModified = false;
        }

        public void SetMapping(ProjectConfigMapping mapping)
        {
            _mapping = mapping;
        }

        public uint CalculateCheckSum()
        {
            uint checkSum = 0;
            for (int i = 0; i < CONFIG_FLAGS_COUNT; i++)
            {
                checkSum += _configFlags[i];
            }
            if (checkSum == 0)
            {
                checkSum = CHECKSUM_MAGIC_VALUE;
            }
            return checkSum;
        }

        public bool ValidateCheckSum()
        {
            uint calculatedSum = CalculateCheckSum();
            bool isValid = calculatedSum == _checkSum;
            
            if (!isValid && _checkSum == CHECKSUM_MAGIC_VALUE && calculatedSum != CHECKSUM_MAGIC_VALUE)
            {
                isValid = false;
            }
            
            _isValid = isValid;
            return isValid;
        }

        public uint GetConfigValue(ConfigId configId)
        {
            int index = (int)configId;
            if (index >= 0 && index < CONFIG_FLAGS_COUNT)
            {
                return _configFlags[index];
            }
            return 0;
        }

        public void SetConfigValue(ConfigId configId, uint value)
        {
            int index = (int)configId;
            if (index >= 0 && index < CONFIG_FLAGS_COUNT)
            {
                if (_configFlags[index] != value)
                {
                    _configFlags[index] = value;
                    _isModified = true;
                }
            }
        }

        public void SetConfigValue(int index, uint value)
        {
            if (index >= 0 && index < CONFIG_FLAGS_COUNT)
            {
                if (_configFlags[index] != value)
                {
                    _configFlags[index] = value;
                    _isModified = true;
                }
            }
        }

        public void InitializeDefaults()
        {
            Array.Clear(_configFlags, 0, CONFIG_FLAGS_COUNT);
            
            SetConfigValue(ConfigId.CONFIG_ID_YEAR, 2026);
            SetConfigValue(ConfigId.CONFIG_ID_MONTH, 1);
            SetConfigValue(ConfigId.CONFIG_ID_MDAY, 1);
            SetConfigValue(ConfigId.CONFIG_ID_WDAY, 1);
            SetConfigValue(ConfigId.CONFIG_ID_HOUR, 0);
            SetConfigValue(ConfigId.CONFIG_ID_MIN, 0);
            SetConfigValue(ConfigId.CONFIG_ID_SEC, 0);
            SetConfigValue(ConfigId.CONFIG_ID_LANGUAGE, FirmwareConstants.R_STR_LAN_ENGLISH);
            SetConfigValue(ConfigId.CONFIG_ID_AUTOOFF, FirmwareConstants.R_STR_COM_OFF);
            SetConfigValue(ConfigId.CONFIG_ID_SCREENSAVE, FirmwareConstants.R_STR_COM_OFF);
            SetConfigValue(ConfigId.CONFIG_ID_FREQUNCY, FirmwareConstants.R_STR_COM_50HZ);
            SetConfigValue(ConfigId.CONFIG_ID_ROTATE, FirmwareConstants.R_STR_COM_OFF);
            SetConfigValue(ConfigId.CONFIG_ID_FILLIGHT, FirmwareConstants.R_STR_COM_OFF);
            SetConfigValue(ConfigId.CONFIG_ID_RESOLUTION, FirmwareConstants.R_STR_RES_720P_SHORT);
            SetConfigValue(ConfigId.CONFIG_ID_TIMESTAMP, FirmwareConstants.R_STR_COM_ON);
            SetConfigValue(ConfigId.CONFIG_ID_MOTIONDECTION, FirmwareConstants.R_STR_COM_OFF);
            SetConfigValue(ConfigId.CONFIG_ID_TIMEPHOTO, FirmwareConstants.R_STR_COM_OFF);
            SetConfigValue(ConfigId.CONFIG_ID_PARKMODE, FirmwareConstants.R_STR_COM_OFF);
            SetConfigValue(ConfigId.CONFIG_ID_GSENSOR, FirmwareConstants.R_STR_COM_MIDDLE);
            SetConfigValue(ConfigId.CONFIG_ID_KEYSOUND, FirmwareConstants.R_STR_COM_ON);
            SetConfigValue(ConfigId.CONFIG_ID_IR_LED, FirmwareConstants.R_STR_COM_OFF);
            SetConfigValue(ConfigId.CONFIG_ID_LOOPTIME, FirmwareConstants.R_STR_TIM_3MIN);
            SetConfigValue(ConfigId.CONFIG_ID_AUDIOREC, FirmwareConstants.R_STR_COM_ON);
            SetConfigValue(ConfigId.CONFIG_ID_EV, FirmwareConstants.R_STR_COM_P0_0);
            SetConfigValue(ConfigId.CONFIG_ID_WBLANCE, FirmwareConstants.R_STR_ISP_AUTO);
            SetConfigValue(ConfigId.CONFIG_ID_PRESLUTION, FirmwareConstants.R_STR_RES_VGA);
            SetConfigValue(ConfigId.CONFIG_ID_PFASTVIEW, FirmwareConstants.R_STR_COM_OFF);
            SetConfigValue(ConfigId.CONFIG_ID_PTIMESTRAMP, FirmwareConstants.R_STR_COM_ON);
            SetConfigValue(ConfigId.CONFIG_ID_PEV, FirmwareConstants.R_STR_COM_P0_0);
            SetConfigValue(ConfigId.CONFIG_ID_VOLUME, FirmwareConstants.R_STR_COM_LEVEL_7);
            SetConfigValue(ConfigId.CONFIG_ID_LCD_BRIGHT, FirmwareConstants.R_STR_COM_LEVEL_8);
            SetConfigValue(ConfigId.CONFIG_ID_THUMBNAIL, FirmwareConstants.R_STR_COM_ON);
            SetConfigValue(ConfigId.CONFIG_ID_GSENSORMODE, FirmwareConstants.R_STR_COM_ON);
            SetConfigValue(ConfigId.CONFIG_ID_REINIT, FirmwareConstants.R_STR_COM_ON);

            _checkSum = CalculateCheckSum();
            _isValid = true;
            _isModified = true;
        }

        public byte[] Serialize()
        {
            byte[] data = new byte[CONFIG_STRUCT_SIZE];
            
            for (int i = 0; i < CONFIG_FLAGS_COUNT; i++)
            {
                byte[] flagBytes = BitConverter.GetBytes(_configFlags[i]);
                Array.Copy(flagBytes, 0, data, i * 4, 4);
            }
            
            byte[] checkSumBytes = BitConverter.GetBytes(CalculateCheckSum());
            Array.Copy(checkSumBytes, 0, data, CONFIG_FLAGS_COUNT * 4, 4);
            
            return data;
        }

        public bool Deserialize(byte[] data)
        {
            if (data == null || data.Length < CONFIG_STRUCT_SIZE)
            {
                return false;
            }

            try
            {
                for (int i = 0; i < CONFIG_FLAGS_COUNT; i++)
                {
                    _configFlags[i] = BitConverter.ToUInt32(data, i * 4);
                }
                
                _checkSum = BitConverter.ToUInt32(data, CONFIG_FLAGS_COUNT * 4);
                _isModified = false;
                
                return ValidateCheckSum();
            }
            catch
            {
                return false;
            }
        }

        public uint GetConfigAddress(uint resBinOffset, uint resBinSize)
        {
            uint addr = resBinOffset + resBinSize;
            
            if ((addr & 0xFFF) != 0)
            {
                addr = (addr & 0xFFFFF000) + 0x1000;
            }
            
            _configAddress = addr;
            return addr;
        }

        public List<FirmwareConfigItem> BuildConfigItemList()
        {
            List<FirmwareConfigItem> items = new List<FirmwareConfigItem>();
            
            for (int i = 0; i < CONFIG_FLAGS_COUNT; i++)
            {
                ConfigId configId = (ConfigId)i;
                string configName = configId.ToString();
                uint value = _configFlags[i];
                
                var mappingItem = _mapping?.Mappings?.FirstOrDefault(m => m.Index == i);
                string description = mappingItem?.Description ?? string.Empty;
                
                string displayText = FormatConfigValue(configId, value);
                var finalOptions = new List<ConfigOption>(GetConfigOptions(configId));
                
                if (!finalOptions.Any(o => o.Value == value))
                {
                    finalOptions.Add(new ConfigOption(value, displayText));
                }

                FirmwareConfigItem item = new FirmwareConfigItem
                {
                    Id = configId,
                    Name = configName,
                    Value = value,
                    ValueDisplay = displayText,
                    Category = GetConfigCategory(configId),
                    Options = finalOptions
                };
                
                items.Add(item);
            }
            
            return items;
        }

        private string FormatConfigValue(ConfigId configId, uint value)
        {
            switch (configId)
            {
                case ConfigId.CONFIG_ID_YEAR:
                case ConfigId.CONFIG_ID_MONTH:
                case ConfigId.CONFIG_ID_MDAY:
                case ConfigId.CONFIG_ID_WDAY:
                case ConfigId.CONFIG_ID_HOUR:
                case ConfigId.CONFIG_ID_MIN:
                case ConfigId.CONFIG_ID_SEC:
                    return value.ToString();
                
                case ConfigId.CONFIG_ID_LANGUAGE:
                    return GetLanguageName(value);
                
                case ConfigId.CONFIG_ID_AUTOOFF:
                case ConfigId.CONFIG_ID_SCREENSAVE:
                case ConfigId.CONFIG_ID_TIMESTAMP:
                case ConfigId.CONFIG_ID_KEYSOUND:
                case ConfigId.CONFIG_ID_AUDIOREC:
                case ConfigId.CONFIG_ID_THUMBNAIL:
                    return value == FirmwareConstants.R_STR_COM_ON ? "ON" : "OFF";
                
                case ConfigId.CONFIG_ID_RESOLUTION:
                    return GetResolutionName(value);
                
                case ConfigId.CONFIG_ID_LOOPTIME:
                    return GetLoopTimeName(value);
                
                case ConfigId.CONFIG_ID_EV:
                    return GetEvName(value);
                
                case ConfigId.CONFIG_ID_WBLANCE:
                    return GetWhiteBalanceName(value);
                
                case ConfigId.CONFIG_ID_VOLUME:
                case ConfigId.CONFIG_ID_LCD_BRIGHT:
                    return $"Level {value & 0xFF}";
                
                case ConfigId.CONFIG_ID_GSENSOR:
                    return GetGsensorLevelName(value);
                
                default:
                    return $"0x{value:X8}";
            }
        }

        private string GetLanguageName(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_LAN_ENGLISH => "English",
                FirmwareConstants.R_STR_LAN_SCHINESE => "Simplified Chinese",
                FirmwareConstants.R_STR_LAN_TCHINESE => "Traditional Chinese",
                FirmwareConstants.R_STR_LAN_JAPANESE => "Japanese",
                FirmwareConstants.R_STR_LAN_GERMAN => "German",
                FirmwareConstants.R_STR_LAN_FRECH => "French",
                FirmwareConstants.R_STR_LAN_RUSSIAN => "Russian",
                FirmwareConstants.R_STR_LAN_ITALIAN => "Italian",
                FirmwareConstants.R_STR_LAN_KOERA => "Korean",
                FirmwareConstants.R_STR_LAN_TAI => "Taiwanese",
                FirmwareConstants.R_STR_LAN_DUTCH => "Dutch",
                FirmwareConstants.R_STR_LAN_SPANISH => "Spanish",
                FirmwareConstants.R_STR_LAN_PORTUGUESE => "Portuguese",
                FirmwareConstants.R_STR_LAN_POLISH => "Polish",
                _ => $"Unknown (0x{value:X8})"
            };
        }

        private string GetResolutionName(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_RES_240P => "240P",
                FirmwareConstants.R_STR_RES_480P => "480P",
                FirmwareConstants.R_STR_RES_480FHD => "480FHD",
                FirmwareConstants.R_STR_RES_720P => "720P",
                FirmwareConstants.R_STR_RES_720P_SHORT => "720P",
                FirmwareConstants.R_STR_RES_1080P => "1080P",
                FirmwareConstants.R_STR_RES_1080P_SHORT => "1080P",
                FirmwareConstants.R_STR_RES_QVGA => "QVGA",
                FirmwareConstants.R_STR_RES_VGA => "VGA",
                FirmwareConstants.R_STR_RES_HD => "HD",
                FirmwareConstants.R_STR_RES_FHD => "FHD",
                _ => $"Unknown (0x{value:X8})"
            };
        }

        private string GetLoopTimeName(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_COM_OFF => "OFF",
                FirmwareConstants.R_STR_TIM_1MIN => "1 Min",
                FirmwareConstants.R_STR_TIM_2MIN => "2 Min",
                FirmwareConstants.R_STR_TIM_3MIN => "3 Min",
                FirmwareConstants.R_STR_TIM_5MIN => "5 Min",
                FirmwareConstants.R_STR_TIM_10MIN => "10 Min",
                _ => $"Unknown (0x{value:X8})"
            };
        }

        private string GetEvName(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_COM_N2_0 => "-2.0",
                FirmwareConstants.R_STR_COM_N1_0 => "-1.0",
                FirmwareConstants.R_STR_COM_P0_0 => "0.0",
                FirmwareConstants.R_STR_COM_P1_0 => "+1.0",
                FirmwareConstants.R_STR_COM_P2_0 => "+2.0",
                _ => $"Unknown (0x{value:X8})"
            };
        }

        private string GetWhiteBalanceName(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_ISP_AUTO => "Auto",
                FirmwareConstants.R_STR_ISP_SUNLIGHT => "Sunlight",
                FirmwareConstants.R_STR_ISP_CLOUDY => "Cloudy",
                FirmwareConstants.R_STR_ISP_TUNGSTEN => "Tungsten",
                FirmwareConstants.R_STR_ISP_FLUORESCENT => "Fluorescent",
                _ => $"Unknown (0x{value:X8})"
            };
        }

        private string GetGsensorLevelName(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_COM_LOW => "Low",
                FirmwareConstants.R_STR_COM_MIDDLE => "Middle",
                FirmwareConstants.R_STR_COM_HIGH => "High",
                _ => $"Unknown (0x{value:X8})"
            };
        }

        private string GetConfigCategory(ConfigId configId)
        {
            switch (configId)
            {
                case ConfigId.CONFIG_ID_YEAR:
                case ConfigId.CONFIG_ID_MONTH:
                case ConfigId.CONFIG_ID_MDAY:
                case ConfigId.CONFIG_ID_WDAY:
                case ConfigId.CONFIG_ID_HOUR:
                case ConfigId.CONFIG_ID_MIN:
                case ConfigId.CONFIG_ID_SEC:
                    return "Time";
                
                case ConfigId.CONFIG_ID_LANGUAGE:
                    return "System";
                
                case ConfigId.CONFIG_ID_AUTOOFF:
                case ConfigId.CONFIG_ID_SCREENSAVE:
                case ConfigId.CONFIG_ID_KEYSOUND:
                case ConfigId.CONFIG_ID_LCD_BRIGHT:
                case ConfigId.CONFIG_ID_VOLUME:
                    return "System";
                
                case ConfigId.CONFIG_ID_RESOLUTION:
                case ConfigId.CONFIG_ID_TIMESTAMP:
                case ConfigId.CONFIG_ID_LOOPTIME:
                case ConfigId.CONFIG_ID_AUDIOREC:
                case ConfigId.CONFIG_ID_EV:
                case ConfigId.CONFIG_ID_WBLANCE:
                    return "Video";
                
                case ConfigId.CONFIG_ID_PRESLUTION:
                case ConfigId.CONFIG_ID_PFASTVIEW:
                case ConfigId.CONFIG_ID_PTIMESTRAMP:
                case ConfigId.CONFIG_ID_PEV:
                    return "Photo";
                
                case ConfigId.CONFIG_ID_GSENSOR:
                case ConfigId.CONFIG_ID_GSENSORMODE:
                case ConfigId.CONFIG_ID_MOTIONDECTION:
                case ConfigId.CONFIG_ID_PARKMODE:
                case ConfigId.CONFIG_ID_IR_LED:
                    return "Sensor";
                
                case ConfigId.CONFIG_ID_FREQUNCY:
                case ConfigId.CONFIG_ID_ROTATE:
                case ConfigId.CONFIG_ID_FILLIGHT:
                case ConfigId.CONFIG_ID_TIMEPHOTO:
                case ConfigId.CONFIG_ID_THUMBNAIL:
                case ConfigId.CONFIG_ID_REINIT:
                default:
                    return "Other";
            }
        }

        private List<ConfigOption> GetConfigOptions(ConfigId configId)
        {
            List<ConfigOption> options = new List<ConfigOption>();

            switch (configId)
            {
                case ConfigId.CONFIG_ID_LANGUAGE:
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_LAN_ENGLISH, "English"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_LAN_SCHINESE, "Simplified Chinese"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_LAN_TCHINESE, "Traditional Chinese"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_LAN_JAPANESE, "Japanese"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_LAN_GERMAN, "German"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_LAN_FRECH, "French"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_LAN_RUSSIAN, "Russian"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_LAN_ITALIAN, "Italian"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_LAN_KOERA, "Korean"));
                    break;

                case ConfigId.CONFIG_ID_AUTOOFF:
                case ConfigId.CONFIG_ID_SCREENSAVE:
                case ConfigId.CONFIG_ID_TIMESTAMP:
                case ConfigId.CONFIG_ID_KEYSOUND:
                case ConfigId.CONFIG_ID_AUDIOREC:
                case ConfigId.CONFIG_ID_THUMBNAIL:
                case ConfigId.CONFIG_ID_TIMEPHOTO:
                case ConfigId.CONFIG_ID_MOTIONDECTION:
                case ConfigId.CONFIG_ID_PARKMODE:
                case ConfigId.CONFIG_ID_IR_LED:
                case ConfigId.CONFIG_ID_PFASTVIEW:
                case ConfigId.CONFIG_ID_PTIMESTRAMP:
                case ConfigId.CONFIG_ID_REINIT:
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_OFF, "OFF"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_ON, "ON"));
                    break;

                case ConfigId.CONFIG_ID_RESOLUTION:
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_RES_720P_SHORT, "720P"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_RES_1080P_SHORT, "1080P"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_RES_HD, "HD"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_RES_FHD, "FHD"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_RES_VGA, "VGA"));
                    break;

                case ConfigId.CONFIG_ID_LOOPTIME:
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_OFF, "OFF"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_TIM_1MIN, "1 Min"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_TIM_2MIN, "2 Min"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_TIM_3MIN, "3 Min"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_TIM_5MIN, "5 Min"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_TIM_10MIN, "10 Min"));
                    break;

                case ConfigId.CONFIG_ID_EV:
                case ConfigId.CONFIG_ID_PEV:
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_N2_0, "-2.0"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_N1_0, "-1.0"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_P0_0, "0.0"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_P1_0, "+1.0"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_P2_0, "+2.0"));
                    break;

                case ConfigId.CONFIG_ID_WBLANCE:
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_ISP_AUTO, "Auto"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_ISP_SUNLIGHT, "Sunlight"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_ISP_CLOUDY, "Cloudy"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_ISP_TUNGSTEN, "Tungsten"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_ISP_FLUORESCENT, "Fluorescent"));
                    break;

                case ConfigId.CONFIG_ID_GSENSOR:
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_LOW, "Low"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_MIDDLE, "Middle"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_HIGH, "High"));
                    break;

                case ConfigId.CONFIG_ID_VOLUME:
                case ConfigId.CONFIG_ID_LCD_BRIGHT:
                    for (int i = 0; i <= 9; i++)
                    {
                        options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_LEVEL_0 + (uint)i, $"Level {i}"));
                    }
                    break;

                case ConfigId.CONFIG_ID_FREQUNCY:
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_50HZ, "50Hz"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_COM_60HZ, "60Hz"));
                    break;

                case ConfigId.CONFIG_ID_PRESLUTION:
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_RES_QVGA, "QVGA"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_RES_VGA, "VGA"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_RES_1M, "1M"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_RES_2M, "2M"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_RES_3M, "3M"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_RES_5M, "5M"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_RES_8M, "8M"));
                    options.Add(new ConfigOption(FirmwareConstants.R_STR_RES_12M, "12M"));
                    break;
            }

            return options;
        }
    }
}