using System;
using System.Collections.Generic;
using ResBinManager.Core;

namespace ResBinManager.Models
{
    /// <summary>
    /// 配置项选项列表缓存
    /// 统一管理所有类型的选项列表，避免重复创建
    /// </summary>
    public static class ConfigOptionsCache
    {
        private static readonly Dictionary<ConfigItemType, List<ConfigOption>> _cache = new();
        private static bool _initialized = false;
        private static DynamicFirmwareConstants _dynamicConstants = null;

        /// <summary>
        /// 设置动态固件常量
        /// </summary>
        public static void SetDynamicConstants(DynamicFirmwareConstants dynamicConstants)
        {
            _dynamicConstants = dynamicConstants;
            _initialized = false;
        }

        /// <summary>
        /// 获取指定类型的选项列表
        /// </summary>
        public static List<ConfigOption> GetOptions(ConfigItemType type)
        {
            EnsureInitialized();
            return _cache.TryGetValue(type, out var options) ? options : new List<ConfigOption>();
        }

        /// <summary>
        /// 确保缓存已初始化
        /// </summary>
        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            BuildOptionsCache();
            _initialized = true;
        }

        /// <summary>
        /// 获取常量值（优先使用动态常量）
        /// </summary>
        private static uint GetConstant(string name, uint defaultValue)
        {
            if (_dynamicConstants != null && _dynamicConstants.TryGetValue(name, out uint value))
                return value;
            return defaultValue;
        }

        /// <summary>
        /// 构建选项列表缓存
        /// </summary>
        private static void BuildOptionsCache()
        {
            _cache.Clear();

            // 开关类型
            _cache[ConfigItemType.OnOff] = new List<ConfigOption>
            {
                new ConfigOption(0, "关闭"),
                new ConfigOption(GetConstant("R_ID_STR_COM_OFF", FirmwareConstants.R_STR_COM_OFF), "关闭"),
                new ConfigOption(GetConstant("R_ID_STR_COM_ON", FirmwareConstants.R_STR_COM_ON), "开启")
            };

            // 语言类型
            _cache[ConfigItemType.Language] = new List<ConfigOption>
            {
                new ConfigOption(FirmwareConstants.R_STR_LAN_ENGLISH, "English"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_SCHINESE, "简体中文"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_TCHINESE, "繁体中文"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_JAPANESE, "日本语"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_GERMAN, "Deutsch"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_FRECH, "Français"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_RUSSIAN, "Русский"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_ITALIAN, "Italiano"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_KOERA, "한국어"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_TAI, "ภาษาไทย"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_HEBREW, "العربية"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_DUTCH, "Nederlands"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_UKRAINIAN, "Українська"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_SPANISH, "Español"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_PORTUGUESE, "Português"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_POLISH, "Polski"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_CZECH, "Čeština"),
                new ConfigOption(FirmwareConstants.R_STR_LAN_TURKEY, "Türkçe")
            };

            // 时间类型（年/月/日/时/分/秒）
            var timeOptions = new List<ConfigOption>();
            for (uint i = 0; i <= 99; i++)
            {
                timeOptions.Add(new ConfigOption(i, i.ToString()));
            }
            _cache[ConfigItemType.Time] = timeOptions;

            // 数值类型
            var numericOptions = new List<ConfigOption>();
            for (uint i = 0; i <= 100; i++)
            {
                numericOptions.Add(new ConfigOption(i, i.ToString()));
            }
            _cache[ConfigItemType.Numeric] = numericOptions;

            // 分辨率类型（视频和拍照共用）
            _cache[ConfigItemType.Resolution] = new List<ConfigOption>
            {
                new ConfigOption(FirmwareConstants.R_STR_RES_240P, "240P"),
                new ConfigOption(FirmwareConstants.R_STR_RES_480P, "480P"),
                new ConfigOption(FirmwareConstants.R_STR_RES_480FHD, "480FHD"),
                new ConfigOption(FirmwareConstants.R_STR_RES_720P, "720P"),
                new ConfigOption(FirmwareConstants.R_STR_RES_1024P, "1024P"),
                new ConfigOption(FirmwareConstants.R_STR_RES_1080P, "1080P"),
                new ConfigOption(FirmwareConstants.R_STR_RES_1080FHD, "1080FHD"),
                new ConfigOption(FirmwareConstants.R_STR_RES_1440P, "1440P"),
                new ConfigOption(FirmwareConstants.R_STR_RES_3024P, "3024P"),
                new ConfigOption(FirmwareConstants.R_STR_RES_720P_SHORT, "720P_SHORT"),
                new ConfigOption(FirmwareConstants.R_STR_RES_1080P_SHORT, "1080P_SHORT"),
                new ConfigOption(FirmwareConstants.R_STR_RES_QVGA, "QVGA"),
                new ConfigOption(FirmwareConstants.R_STR_RES_VGA, "VGA"),
                new ConfigOption(FirmwareConstants.R_STR_RES_HD, "HD"),
                new ConfigOption(FirmwareConstants.R_STR_RES_FHD, "FHD"),
                new ConfigOption(FirmwareConstants.R_STR_RES_48M, "48M"),
                new ConfigOption(FirmwareConstants.R_STR_RES_40M, "40M"),
                new ConfigOption(FirmwareConstants.R_STR_RES_24M, "24M"),
                new ConfigOption(FirmwareConstants.R_STR_RES_20M, "20M"),
                new ConfigOption(FirmwareConstants.R_STR_RES_18M, "18M"),
                new ConfigOption(FirmwareConstants.R_STR_RES_16M, "16M"),
                new ConfigOption(FirmwareConstants.R_STR_RES_12M, "12M"),
                new ConfigOption(FirmwareConstants.R_STR_RES_10M, "10M"),
                new ConfigOption(FirmwareConstants.R_STR_RES_8M, "8M"),
                new ConfigOption(FirmwareConstants.R_STR_RES_5M, "5M"),
                new ConfigOption(FirmwareConstants.R_STR_RES_3M, "3M"),
                new ConfigOption(FirmwareConstants.R_STR_RES_2M, "2M"),
                new ConfigOption(FirmwareConstants.R_STR_RES_1M, "1M")
            };

            // 曝光补偿类型
            _cache[ConfigItemType.ExposureValue] = new List<ConfigOption>
            {
                new ConfigOption(FirmwareConstants.R_STR_COM_P4_0, "+4.0"),
                new ConfigOption(FirmwareConstants.R_STR_COM_P3_0, "+3.0"),
                new ConfigOption(FirmwareConstants.R_STR_COM_P2_0, "+2.0"),
                new ConfigOption(FirmwareConstants.R_STR_COM_P1_0, "+1.0"),
                new ConfigOption(FirmwareConstants.R_STR_COM_P0_0, "0.0"),
                new ConfigOption(FirmwareConstants.R_STR_COM_N1_0, "-1.0"),
                new ConfigOption(FirmwareConstants.R_STR_COM_N2_0, "-2.0")
            };

            // 白平衡类型
            _cache[ConfigItemType.WhiteBalance] = new List<ConfigOption>
            {
                new ConfigOption(FirmwareConstants.R_STR_ISP_AUTO, "自动"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_SUNLIGHT, "晴天"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_CLOUDY, "阴天"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_TUNGSTEN, "办公室"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_FLUORESCENT, "荧光灯"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_WHITEBL, "白炽灯"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_SOFT, "柔和"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_STRONG, "强烈"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_BLACKWHITE, "黑白"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_SEPIA, "复古"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_ISO100, "ISO100"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_ISO200, "ISO200"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_ISO400, "ISO400"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_WDR, "WDR"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_EXPOSURE, "曝光"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_ISO, "ISO"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_ANTISHANK, "防抖")
            };

            // 自动开关类型（关闭/开启/自动）
            _cache[ConfigItemType.AutoOnOff] = new List<ConfigOption>
            {
                new ConfigOption(FirmwareConstants.R_STR_COM_OFF, "关闭"),
                new ConfigOption(FirmwareConstants.R_STR_COM_ON, "开启"),
                new ConfigOption(FirmwareConstants.R_STR_ISP_AUTO, "自动")
            };

            // 频率类型
            _cache[ConfigItemType.Frequency] = new List<ConfigOption>
            {
                new ConfigOption(FirmwareConstants.R_STR_COM_50HZ, "50Hz"),
                new ConfigOption(FirmwareConstants.R_STR_COM_60HZ, "60Hz")
            };

            // 自动关机时间类型
            _cache[ConfigItemType.AutoOffTime] = new List<ConfigOption>
            {
                new ConfigOption(FirmwareConstants.R_STR_COM_OFF, "关闭"),
                new ConfigOption(FirmwareConstants.R_STR_COM_ON, "开启"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_1MIN, "1分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_2MIN, "2分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_3MIN, "3分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_5MIN, "5分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_10MIN, "10分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_2SEC, "2秒"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_3SEC, "3秒"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_5SEC, "5秒"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_10SEC, "10秒"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_30SEC, "30秒")
            };

            // 屏保时间类型
            _cache[ConfigItemType.ScreenSaveTime] = new List<ConfigOption>
            {
                new ConfigOption(FirmwareConstants.R_STR_COM_OFF, "关闭"),
                new ConfigOption(FirmwareConstants.R_STR_COM_ON, "开启"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_1MIN, "1分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_2MIN, "2分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_3MIN, "3分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_5MIN, "5分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_10MIN, "10分钟")
            };

            // 循环录像时间类型
            _cache[ConfigItemType.LoopTime] = new List<ConfigOption>
            {
                new ConfigOption(FirmwareConstants.R_STR_TIM_1MIN, "1分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_2MIN, "2分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_3MIN, "3分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_5MIN, "5分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_10MIN, "10分钟"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_2SEC, "2秒"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_3SEC, "3秒"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_5SEC, "5秒"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_10SEC, "10秒"),
                new ConfigOption(FirmwareConstants.R_STR_TIM_30SEC, "30秒")
            };

            // 等级类型
            var levelOptions = new List<ConfigOption>();
            for (uint i = 0; i <= 9; i++)
            {
                levelOptions.Add(new ConfigOption(FirmwareConstants.R_STR_COM_LEVEL_0 + i, $"级别 {i}"));
            }
            _cache[ConfigItemType.Level] = levelOptions;

            // 灵敏度类型（高/中/低）
            _cache[ConfigItemType.Sensitivity] = new List<ConfigOption>
            {
                new ConfigOption(FirmwareConstants.R_STR_COM_LOW, "低"),
                new ConfigOption(FirmwareConstants.R_STR_COM_MIDDLE, "中"),
                new ConfigOption(FirmwareConstants.R_STR_COM_HIGH, "高")
            };

            // 星期类型
            _cache[ConfigItemType.WeekDay] = new List<ConfigOption>
            {
                new ConfigOption(0, "星期日"),
                new ConfigOption(1, "星期一"),
                new ConfigOption(2, "星期二"),
                new ConfigOption(3, "星期三"),
                new ConfigOption(4, "星期四"),
                new ConfigOption(5, "星期五"),
                new ConfigOption(6, "星期六")
            };

            // 录像速度类型
            _cache[ConfigItemType.VideoSpeed] = new List<ConfigOption>
            {
                new ConfigOption(GetConstant("R_ID_STR_COM_VIDEOREC_NORMAL", FirmwareConstants.R_ID_TYPE_STR + 0xDC), "正常"),
                new ConfigOption(GetConstant("R_ID_STR_COM_VIDEOREC_SLOW", FirmwareConstants.R_ID_TYPE_STR + 0xDD), "慢速"),
                new ConfigOption(GetConstant("R_ID_STR_COM_VIDEOREC_FAST", FirmwareConstants.R_ID_TYPE_STR + 0xDE), "快速")
            };

            // 原始十六进制类型（无选项）
            _cache[ConfigItemType.RawHex] = new List<ConfigOption>();

            System.Diagnostics.Debug.WriteLine($"[ConfigOptionsCache] Built {_cache.Count} option lists");
        }

        /// <summary>
        /// 清除缓存（主要用于测试）
        /// </summary>
        public static void Clear()
        {
            _cache.Clear();
            _initialized = false;
        }
    }
}
