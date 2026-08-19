using System;
using ResBinManager.Core;

namespace ResBinManager.Models
{
    /// <summary>
    /// 配置项显示格式化工具类
    /// 统一管理所有类型的显示文本格式化函数
    /// </summary>
    public static class ConfigDisplayFormatters
    {
        /// <summary>
        /// 获取指定类型的显示格式化函数
        /// </summary>
        public static Func<uint, string> GetFormatter(ConfigItemType type)
        {
            return type switch
            {
                ConfigItemType.OnOff => FormatOnOff,
                ConfigItemType.Language => FormatLanguage,
                ConfigItemType.Time => FormatTime,
                ConfigItemType.Numeric => FormatNumeric,
                ConfigItemType.Resolution => FormatResolution,
                ConfigItemType.ExposureValue => FormatExposureValue,
                ConfigItemType.WhiteBalance => FormatWhiteBalance,
                ConfigItemType.Frequency => FormatFrequency,
                ConfigItemType.AutoOffTime => FormatAutoOffTime,
                ConfigItemType.ScreenSaveTime => FormatScreenSaveTime,
                ConfigItemType.LoopTime => FormatLoopTime,
                ConfigItemType.Level => FormatLevel,
                ConfigItemType.Sensitivity => FormatSensitivity,
                ConfigItemType.WeekDay => FormatWeekDay,
                ConfigItemType.VideoSpeed => FormatVideoSpeed,
                ConfigItemType.RawHex => FormatRawHex,
                _ => FormatRawHex
            };
        }

        // ============================================================
        // 格式化函数实现
        // ============================================================

        private static string FormatOnOff(uint value)
        {
            return value switch
            {
                0 => "关闭",
                FirmwareConstants.R_STR_COM_OFF => "关闭",
                FirmwareConstants.R_STR_COM_ON => "开启",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string FormatLanguage(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_LAN_ENGLISH => "English",
                FirmwareConstants.R_STR_LAN_SCHINESE => "简体中文",
                FirmwareConstants.R_STR_LAN_TCHINESE => "繁体中文",
                FirmwareConstants.R_STR_LAN_JAPANESE => "日本语",
                FirmwareConstants.R_STR_LAN_GERMAN => "Deutsch",
                FirmwareConstants.R_STR_LAN_FRECH => "Français",
                FirmwareConstants.R_STR_LAN_RUSSIAN => "Русский",
                FirmwareConstants.R_STR_LAN_ITALIAN => "Italiano",
                FirmwareConstants.R_STR_LAN_KOERA => "한국어",
                FirmwareConstants.R_STR_LAN_TAI => "ภาษาไทย",
                FirmwareConstants.R_STR_LAN_HEBREW => "العربية",
                FirmwareConstants.R_STR_LAN_DUTCH => "Nederlands",
                FirmwareConstants.R_STR_LAN_UKRAINIAN => "Українська",
                FirmwareConstants.R_STR_LAN_SPANISH => "Español",
                FirmwareConstants.R_STR_LAN_PORTUGUESE => "Português",
                FirmwareConstants.R_STR_LAN_POLISH => "Polski",
                FirmwareConstants.R_STR_LAN_CZECH => "Čeština",
                FirmwareConstants.R_STR_LAN_TURKEY => "Türkçe",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string FormatTime(uint value)
        {
            return value.ToString();
        }

        private static string FormatNumeric(uint value)
        {
            return value.ToString();
        }

        private static string FormatResolution(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_RES_240P => "240P",
                FirmwareConstants.R_STR_RES_480P => "480P",
                FirmwareConstants.R_STR_RES_480FHD => "480FHD",
                FirmwareConstants.R_STR_RES_720P => "720P",
                FirmwareConstants.R_STR_RES_1024P => "1024P",
                FirmwareConstants.R_STR_RES_1080P => "1080P",
                FirmwareConstants.R_STR_RES_1080FHD => "1080FHD",
                FirmwareConstants.R_STR_RES_1440P => "1440P",
                FirmwareConstants.R_STR_RES_3024P => "3024P",
                FirmwareConstants.R_STR_RES_720P_SHORT => "720P_SHORT",
                FirmwareConstants.R_STR_RES_1080P_SHORT => "1080P_SHORT",
                FirmwareConstants.R_STR_RES_QVGA => "QVGA",
                FirmwareConstants.R_STR_RES_VGA => "VGA",
                FirmwareConstants.R_STR_RES_HD => "HD",
                FirmwareConstants.R_STR_RES_FHD => "FHD",
                FirmwareConstants.R_STR_RES_48M => "48M",
                FirmwareConstants.R_STR_RES_40M => "40M",
                FirmwareConstants.R_STR_RES_24M => "24M",
                FirmwareConstants.R_STR_RES_20M => "20M",
                FirmwareConstants.R_STR_RES_18M => "18M",
                FirmwareConstants.R_STR_RES_16M => "16M",
                FirmwareConstants.R_STR_RES_12M => "12M",
                FirmwareConstants.R_STR_RES_10M => "10M",
                FirmwareConstants.R_STR_RES_8M => "8M",
                FirmwareConstants.R_STR_RES_5M => "5M",
                FirmwareConstants.R_STR_RES_3M => "3M",
                FirmwareConstants.R_STR_RES_2M => "2M",
                FirmwareConstants.R_STR_RES_1M => "1M",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string FormatExposureValue(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_COM_P4_0 => "+4.0",
                FirmwareConstants.R_STR_COM_P3_0 => "+3.0",
                FirmwareConstants.R_STR_COM_P2_0 => "+2.0",
                FirmwareConstants.R_STR_COM_P1_0 => "+1.0",
                FirmwareConstants.R_STR_COM_P0_0 => "0.0",
                FirmwareConstants.R_STR_COM_N1_0 => "-1.0",
                FirmwareConstants.R_STR_COM_N2_0 => "-2.0",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string FormatWhiteBalance(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_ISP_AUTO => "自动",
                FirmwareConstants.R_STR_ISP_SUNLIGHT => "晴天",
                FirmwareConstants.R_STR_ISP_CLOUDY => "阴天",
                FirmwareConstants.R_STR_ISP_TUNGSTEN => "办公室",
                FirmwareConstants.R_STR_ISP_FLUORESCENT => "荧光灯",
                FirmwareConstants.R_STR_ISP_WHITEBL => "白炽灯",
                FirmwareConstants.R_STR_ISP_SOFT => "柔和",
                FirmwareConstants.R_STR_ISP_STRONG => "强烈",
                FirmwareConstants.R_STR_ISP_BLACKWHITE => "黑白",
                FirmwareConstants.R_STR_ISP_SEPIA => "复古",
                FirmwareConstants.R_STR_ISP_ISO100 => "ISO100",
                FirmwareConstants.R_STR_ISP_ISO200 => "ISO200",
                FirmwareConstants.R_STR_ISP_ISO400 => "ISO400",
                FirmwareConstants.R_STR_ISP_WDR => "WDR",
                FirmwareConstants.R_STR_ISP_EXPOSURE => "曝光",
                FirmwareConstants.R_STR_ISP_ISO => "ISO",
                FirmwareConstants.R_STR_ISP_ANTISHANK => "防抖",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string FormatFrequency(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_COM_50HZ => "50Hz",
                FirmwareConstants.R_STR_COM_60HZ => "60Hz",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string FormatAutoOffTime(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_COM_OFF => "关闭",
                FirmwareConstants.R_STR_COM_ON => "开启",
                FirmwareConstants.R_STR_TIM_1MIN => "1分钟",
                FirmwareConstants.R_STR_TIM_2MIN => "2分钟",
                FirmwareConstants.R_STR_TIM_3MIN => "3分钟",
                FirmwareConstants.R_STR_TIM_5MIN => "5分钟",
                FirmwareConstants.R_STR_TIM_10MIN => "10分钟",
                FirmwareConstants.R_STR_TIM_2SEC => "2秒",
                FirmwareConstants.R_STR_TIM_3SEC => "3秒",
                FirmwareConstants.R_STR_TIM_5SEC => "5秒",
                FirmwareConstants.R_STR_TIM_10SEC => "10秒",
                FirmwareConstants.R_STR_TIM_30SEC => "30秒",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string FormatScreenSaveTime(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_COM_OFF => "关闭",
                FirmwareConstants.R_STR_COM_ON => "开启",
                FirmwareConstants.R_STR_TIM_1MIN => "1分钟",
                FirmwareConstants.R_STR_TIM_2MIN => "2分钟",
                FirmwareConstants.R_STR_TIM_3MIN => "3分钟",
                FirmwareConstants.R_STR_TIM_5MIN => "5分钟",
                FirmwareConstants.R_STR_TIM_10MIN => "10分钟",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string FormatLoopTime(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_TIM_1MIN => "1分钟",
                FirmwareConstants.R_STR_TIM_2MIN => "2分钟",
                FirmwareConstants.R_STR_TIM_3MIN => "3分钟",
                FirmwareConstants.R_STR_TIM_5MIN => "5分钟",
                FirmwareConstants.R_STR_TIM_10MIN => "10分钟",
                FirmwareConstants.R_STR_TIM_2SEC => "2秒",
                FirmwareConstants.R_STR_TIM_3SEC => "3秒",
                FirmwareConstants.R_STR_TIM_5SEC => "5秒",
                FirmwareConstants.R_STR_TIM_10SEC => "10秒",
                FirmwareConstants.R_STR_TIM_30SEC => "30秒",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string FormatLevel(uint value)
        {
            if (value >= FirmwareConstants.R_STR_COM_LEVEL_0 && value <= FirmwareConstants.R_STR_COM_LEVEL_0 + 9)
            {
                return $"级别 {value - FirmwareConstants.R_STR_COM_LEVEL_0}";
            }
            return $"未知 (0x{value:X8})";
        }

        private static string FormatSensitivity(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_STR_COM_LOW => "低",
                FirmwareConstants.R_STR_COM_MIDDLE => "中",
                FirmwareConstants.R_STR_COM_HIGH => "高",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string FormatWeekDay(uint value)
        {
            return value switch
            {
                0 => "星期日",
                1 => "星期一",
                2 => "星期二",
                3 => "星期三",
                4 => "星期四",
                5 => "星期五",
                6 => "星期六",
                _ => $"未知 ({value})"
            };
        }

        private static string FormatVideoSpeed(uint value)
        {
            return value switch
            {
                FirmwareConstants.R_ID_TYPE_STR + 0xC4 => "正常",
                FirmwareConstants.R_ID_TYPE_STR + 0xC5 => "慢速",
                FirmwareConstants.R_ID_TYPE_STR + 0xC6 => "快速",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string FormatRawHex(uint value)
        {
            return $"0x{value:X8}";
        }
    }
}
