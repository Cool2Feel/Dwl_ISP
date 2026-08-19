using ResBinManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ResBinManager.Core
{
    public class ConfigCParser
    {
        private static readonly Regex ConfigSetRegex = new Regex(
            @"configSet\s*\(\s*(CONFIG_ID_\w+)\s*,\s*([^)]+)\s*\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex HexValueRegex = new Regex(
            @"0x([0-9A-Fa-f]+)",
            RegexOptions.Compiled);

        private static readonly Dictionary<string, uint> MacroValueMap = new Dictionary<string, uint>
        {
            { "R_ID_STR_LAN_ENGLISH", FirmwareConstants.R_STR_LAN_ENGLISH },
            { "R_ID_STR_LAN_SCHINESE", FirmwareConstants.R_STR_LAN_SCHINESE },
            { "R_ID_STR_LAN_TCHINESE", FirmwareConstants.R_STR_LAN_TCHINESE },
            { "R_ID_STR_LAN_JAPANESE", FirmwareConstants.R_STR_LAN_JAPANESE },
            { "R_ID_STR_LAN_GERMAN", FirmwareConstants.R_STR_LAN_GERMAN },
            { "R_ID_STR_LAN_FRECH", FirmwareConstants.R_STR_LAN_FRECH },
            { "R_ID_STR_LAN_RUSSIAN", FirmwareConstants.R_STR_LAN_RUSSIAN },
            { "R_ID_STR_LAN_ITALIAN", FirmwareConstants.R_STR_LAN_ITALIAN },
            { "R_ID_STR_LAN_KOERA", FirmwareConstants.R_STR_LAN_KOERA },
            { "R_ID_STR_LAN_TAI", FirmwareConstants.R_STR_LAN_TAI },
            { "R_ID_STR_LAN_HEBREW", FirmwareConstants.R_STR_LAN_HEBREW },
            { "R_ID_STR_LAN_DUTCH", FirmwareConstants.R_STR_LAN_DUTCH },
            { "R_ID_STR_LAN_UKRAINIAN", FirmwareConstants.R_STR_LAN_UKRAINIAN },
            { "R_ID_STR_LAN_SPANISH", FirmwareConstants.R_STR_LAN_SPANISH },
            { "R_ID_STR_LAN_PORTUGUESE", FirmwareConstants.R_STR_LAN_PORTUGUESE },
            { "R_ID_STR_LAN_POLISH", FirmwareConstants.R_STR_LAN_POLISH },
            { "R_ID_STR_LAN_CZECH", FirmwareConstants.R_STR_LAN_CZECH },
            { "R_ID_STR_LAN_TURKEY", FirmwareConstants.R_STR_LAN_TURKEY },
            { "R_ID_STR_COM_OFF", FirmwareConstants.R_STR_COM_OFF },
            { "R_ID_STR_COM_ON", FirmwareConstants.R_STR_COM_ON },
            { "R_ID_STR_COM_OK", FirmwareConstants.R_STR_COM_OK },
            { "R_ID_STR_COM_CANCEL", FirmwareConstants.R_STR_COM_CANCEL },
            { "R_ID_STR_COM_YES", FirmwareConstants.R_STR_COM_YES },
            { "R_ID_STR_COM_NO", FirmwareConstants.R_STR_COM_NO },
            { "R_ID_STR_COM_LOW", FirmwareConstants.R_STR_COM_LOW },
            { "R_ID_STR_COM_MIDDLE", FirmwareConstants.R_STR_COM_MIDDLE },
            { "R_ID_STR_COM_HIGH", FirmwareConstants.R_STR_COM_HIGH },
            { "R_ID_STR_COM_50HZ", FirmwareConstants.R_STR_COM_50HZ },
            { "R_ID_STR_COM_60HZ", FirmwareConstants.R_STR_COM_60HZ },
            { "R_ID_STR_COM_LEVEL_0", FirmwareConstants.R_STR_COM_LEVEL_0 },
            { "R_ID_STR_COM_LEVEL_1", FirmwareConstants.R_STR_COM_LEVEL_1 },
            { "R_ID_STR_COM_LEVEL_2", FirmwareConstants.R_STR_COM_LEVEL_2 },
            { "R_ID_STR_COM_LEVEL_3", FirmwareConstants.R_STR_COM_LEVEL_3 },
            { "R_ID_STR_COM_LEVEL_4", FirmwareConstants.R_STR_COM_LEVEL_4 },
            { "R_ID_STR_COM_LEVEL_5", FirmwareConstants.R_STR_COM_LEVEL_5 },
            { "R_ID_STR_COM_LEVEL_6", FirmwareConstants.R_STR_COM_LEVEL_6 },
            { "R_ID_STR_COM_LEVEL_7", FirmwareConstants.R_STR_COM_LEVEL_7 },
            { "R_ID_STR_COM_LEVEL_8", FirmwareConstants.R_STR_COM_LEVEL_8 },
            { "R_ID_STR_COM_LEVEL_9", FirmwareConstants.R_STR_COM_LEVEL_9 },
            { "R_ID_STR_COM_P4_0", FirmwareConstants.R_STR_COM_P4_0 },
            { "R_ID_STR_COM_P3_0", FirmwareConstants.R_STR_COM_P3_0 },
            { "R_ID_STR_COM_P2_0", FirmwareConstants.R_STR_COM_P2_0 },
            { "R_ID_STR_COM_P1_0", FirmwareConstants.R_STR_COM_P1_0 },
            { "R_ID_STR_COM_P0_0", FirmwareConstants.R_STR_COM_P0_0 },
            { "R_ID_STR_COM_N1_0", FirmwareConstants.R_STR_COM_N1_0 },
            { "R_ID_STR_COM_N2_0", FirmwareConstants.R_STR_COM_N2_0 },
            { "R_ID_STR_COM_ALWAYSON", FirmwareConstants.R_STR_COM_ALWAYSON },
            { "R_ID_STR_COM_ECONOMIC", FirmwareConstants.R_STR_COM_ECONOMIC },
            { "R_ID_STR_COM_NORMAL", FirmwareConstants.R_STR_COM_NORMAL },
            { "R_ID_STR_COM_FINE", FirmwareConstants.R_STR_COM_FINE },
            { "R_ID_STR_TIM_1MIN", FirmwareConstants.R_STR_TIM_1MIN },
            { "R_ID_STR_TIM_2MIN", FirmwareConstants.R_STR_TIM_2MIN },
            { "R_ID_STR_TIM_3MIN", FirmwareConstants.R_STR_TIM_3MIN },
            { "R_ID_STR_TIM_5MIN", FirmwareConstants.R_STR_TIM_5MIN },
            { "R_ID_STR_TIM_10MIN", FirmwareConstants.R_STR_TIM_10MIN },
            { "R_ID_STR_TIM_2SEC", FirmwareConstants.R_STR_TIM_2SEC },
            { "R_ID_STR_TIM_3SEC", FirmwareConstants.R_STR_TIM_3SEC },
            { "R_ID_STR_TIM_5SEC", FirmwareConstants.R_STR_TIM_5SEC },
            { "R_ID_STR_TIM_10SEC", FirmwareConstants.R_STR_TIM_10SEC },
            { "R_ID_STR_TIM_30SEC", FirmwareConstants.R_STR_TIM_30SEC },
            { "R_ID_STR_PHOTO_NUM_3", FirmwareConstants.R_STR_PHOTO_NUM_3 },
            { "R_ID_STR_PHOTO_NUM_5", FirmwareConstants.R_STR_PHOTO_NUM_5 },
            { "R_ID_STR_RES_240P", FirmwareConstants.R_STR_RES_240P },
            { "R_ID_STR_RES_480P", FirmwareConstants.R_STR_RES_480P },
            { "R_ID_STR_RES_480FHD", FirmwareConstants.R_STR_RES_480FHD },
            { "R_ID_STR_RES_720P", FirmwareConstants.R_STR_RES_720P },
            { "R_ID_STR_RES_1024P", FirmwareConstants.R_STR_RES_1024P },
            { "R_ID_STR_RES_1080P", FirmwareConstants.R_STR_RES_1080P },
            { "R_ID_STR_RES_1080FHD", FirmwareConstants.R_STR_RES_1080FHD },
            { "R_ID_STR_RES_1440P", FirmwareConstants.R_STR_RES_1440P },
            { "R_ID_STR_RES_3024P", FirmwareConstants.R_STR_RES_3024P },
            { "R_ID_STR_RES_720P_SHORT", FirmwareConstants.R_STR_RES_720P_SHORT },
            { "R_ID_STR_RES_1080P_SHORT", FirmwareConstants.R_STR_RES_1080P_SHORT },
            { "R_ID_STR_RES_QVGA", FirmwareConstants.R_STR_RES_QVGA },
            { "R_ID_STR_RES_VGA", FirmwareConstants.R_STR_RES_VGA },
            { "R_ID_STR_RES_HD", FirmwareConstants.R_STR_RES_HD },
            { "R_ID_STR_RES_FHD", FirmwareConstants.R_STR_RES_FHD },
            { "R_ID_STR_RES_48M", FirmwareConstants.R_STR_RES_48M },
            { "R_ID_STR_RES_40M", FirmwareConstants.R_STR_RES_40M },
            { "R_ID_STR_RES_24M", FirmwareConstants.R_STR_RES_24M },
            { "R_ID_STR_RES_20M", FirmwareConstants.R_STR_RES_20M },
            { "R_ID_STR_RES_18M", FirmwareConstants.R_STR_RES_18M },
            { "R_ID_STR_RES_16M", FirmwareConstants.R_STR_RES_16M },
            { "R_ID_STR_RES_12M", FirmwareConstants.R_STR_RES_12M },
            { "R_ID_STR_RES_10M", FirmwareConstants.R_STR_RES_10M },
            { "R_ID_STR_RES_8M", FirmwareConstants.R_STR_RES_8M },
            { "R_ID_STR_RES_5M", FirmwareConstants.R_STR_RES_5M },
            { "R_ID_STR_RES_3M", FirmwareConstants.R_STR_RES_3M },
            { "R_ID_STR_RES_2M", FirmwareConstants.R_STR_RES_2M },
            { "R_ID_STR_RES_1M", FirmwareConstants.R_STR_RES_1M },
            { "R_ID_STR_ISP_WHITEBL", FirmwareConstants.R_STR_ISP_WHITEBL },
            { "R_ID_STR_ISP_ISO", FirmwareConstants.R_STR_ISP_ISO },
            { "R_ID_STR_ISP_ANTISHANK", FirmwareConstants.R_STR_ISP_ANTISHANK },
            { "R_ID_STR_ISP_AUTO", FirmwareConstants.R_STR_ISP_AUTO },
            { "R_ID_STR_ISP_SOFT", FirmwareConstants.R_STR_ISP_SOFT },
            { "R_ID_STR_ISP_STRONG", FirmwareConstants.R_STR_ISP_STRONG },
            { "R_ID_STR_ISP_SUNLIGHT", FirmwareConstants.R_STR_ISP_SUNLIGHT },
            { "R_ID_STR_ISP_CLOUDY", FirmwareConstants.R_STR_ISP_CLOUDY },
            { "R_ID_STR_ISP_TUNGSTEN", FirmwareConstants.R_STR_ISP_TUNGSTEN },
            { "R_ID_STR_ISP_FLUORESCENT", FirmwareConstants.R_STR_ISP_FLUORESCENT },
            { "R_ID_STR_ISP_BLACKWHITE", FirmwareConstants.R_STR_ISP_BLACKWHITE },
            { "R_ID_STR_ISP_SEPIA", FirmwareConstants.R_STR_ISP_SEPIA },
            { "R_ID_STR_ISP_ISO100", FirmwareConstants.R_STR_ISP_ISO100 },
            { "R_ID_STR_ISP_ISO200", FirmwareConstants.R_STR_ISP_ISO200 },
            { "R_ID_STR_ISP_ISO400", FirmwareConstants.R_STR_ISP_ISO400 },
            { "R_ID_STR_ISP_WDR", FirmwareConstants.R_STR_ISP_WDR },
            { "R_ID_STR_ISP_EXPOSURE", FirmwareConstants.R_STR_ISP_EXPOSURE },
            { "R_ID_STR_SET_PRINT_DENSITY", FirmwareConstants.R_STR_SET_PRINT_DENSITY },
            { "R_ID_STR_SET_PRINT_MODE", FirmwareConstants.R_STR_SET_PRINT_MODE },
            { "R_ID_STR_SET_PRINT_GRAY", FirmwareConstants.R_STR_SET_PRINT_GRAY },
            { "R_ID_STR_SET_PRINT_DOT", FirmwareConstants.R_STR_SET_PRINT_DOT },
            { "R_ID_STR_TIP_NEAR", FirmwareConstants.R_STR_TIP_NEAR },
        };

        public static List<ConfigCParsedItem> ParseFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("config.c 文件不存在", filePath);
            }

            var content = File.ReadAllText(filePath);
            return Parse(content);
        }

        public static List<ConfigCParsedItem> Parse(string content)
        {
            var items = new List<ConfigCParsedItem>();

            var matches = ConfigSetRegex.Matches(content);
            foreach (Match match in matches)
            {
                string configName = match.Groups[1].Value.Trim();
                string valueStr = match.Groups[2].Value.Trim();

                uint value;
                if (TryParseValue(valueStr, out value))
                {
                    items.Add(new ConfigCParsedItem
                    {
                        ConfigName = configName,
                        Value = value,
                        RawValue = valueStr
                    });
                }
            }

            System.Diagnostics.Debug.WriteLine($"[ConfigCParser] Parsed {items.Count} config items");
            return items;
        }

        private static bool TryParseValue(string valueStr, out uint value)
        {
            value = 0;

            if (string.IsNullOrEmpty(valueStr))
                return false;

            valueStr = valueStr.Trim();

            if (uint.TryParse(valueStr, out value))
            {
                return true;
            }

            if (uint.TryParse(valueStr, System.Globalization.NumberStyles.HexNumber, null, out value))
            {
                return true;
            }

            if (MacroValueMap.TryGetValue(valueStr, out value))
            {
                return true;
            }

            var hexMatch = HexValueRegex.Match(valueStr);
            if (hexMatch.Success && uint.TryParse(hexMatch.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out value))
            {
                return true;
            }

            return false;
        }

        public static Dictionary<string, uint> ToDictionary(List<ConfigCParsedItem> items)
        {
            return items.ToDictionary(item => item.ConfigName, item => item.Value);
        }

        public static Dictionary<ConfigId, uint> ToConfigIdDictionary(List<ConfigCParsedItem> items)
        {
            var result = new Dictionary<ConfigId, uint>();
            foreach (var item in items)
            {
                if (Enum.TryParse<ConfigId>(item.ConfigName, out var configId))
                {
                    result[configId] = item.Value;
                }
            }
            return result;
        }
    }

    public class ConfigCParsedItem
    {
        public string ConfigName { get; set; } = string.Empty;
        public uint Value { get; set; }
        public string RawValue { get; set; } = string.Empty;
    }
}