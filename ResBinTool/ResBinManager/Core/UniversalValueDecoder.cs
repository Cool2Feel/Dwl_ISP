using System;
using System.Collections.Generic;
using ResBinManager.Models;

namespace ResBinManager.Core
{
    /// <summary>
    /// 通用值解码器
    /// 根据固件中的 uint 值自动推断其类型和显示文本，不依赖 configName。
    /// 
    /// 核心原理：所有 R_ID_STR_* 枚举值 = R_ID_TYPE_STR (0x81000000) + 偏移
    /// 通过值范围可以反向推断出该值属于哪类枚举。
    /// 
    /// 用途：
    /// 1. 解析未知配置项时提供智能提示
    /// 2. 自动探测固件中的配置项类型
    /// 3. 为未知项目生成初始映射提供辅助信息
    /// </summary>
    public static class UniversalValueDecoder
    {
        /// <summary>
        /// 值解码结果
        /// </summary>
        public class DecodeResult
        {
            /// <summary>
            /// 推断的配置项类型
            /// </summary>
            public ConfigItemType InferredType { get; set; } = ConfigItemType.RawHex;

            /// <summary>
            /// 显示文本
            /// </summary>
            public string DisplayText { get; set; } = string.Empty;

            /// <summary>
            /// 置信度 (0.0 ~ 1.0)
            /// 1.0 = 精确匹配已知枚举值
            /// 0.5 = 范围匹配（如在等级范围内）
            /// 0.0 = 无法识别，显示原始值
            /// </summary>
            public double Confidence { get; set; } = 0.0;

            /// <summary>
            /// 匹配到的枚举名称（如 "R_STR_COM_OFF"）
            /// </summary>
            public string? MatchedEnumName { get; set; }

            /// <summary>
            /// 是否为 R_ID_TYPE_STR 格式的值
            /// </summary>
            public bool IsStringType => (RawValue & 0xFF000000) == FirmwareConstants.R_ID_TYPE_STR;

            /// <summary>
            /// 原始值
            /// </summary>
            public uint RawValue { get; set; }

            /// <summary>
            /// 偏移量（值 - R_ID_TYPE_STR），仅当 IsStringType 为 true 时有效
            /// </summary>
            public uint Offset => IsStringType ? RawValue - FirmwareConstants.R_ID_TYPE_STR : 0;
        }

        // 优化：使用字典查找替代 if-else 链，提升性能
        private static Dictionary<uint, (ConfigItemType type, string name, string display)> _offsetMap = new();
        private static bool _initialized = false;

        static UniversalValueDecoder()
        {
            InitializeOffsetMap();
        }

        /// <summary>
        /// 初始化偏移量查找表
        /// </summary>
        private static void InitializeOffsetMap()
        {
            if (_initialized) return;

            _offsetMap = new Dictionary<uint, (ConfigItemType, string, string)>();

            // 语言: 0x00 ~ 0x11
            _offsetMap[0x00] = (ConfigItemType.Language, "R_STR_LAN_ENGLISH", "English");
            _offsetMap[0x01] = (ConfigItemType.Language, "R_STR_LAN_SCHINESE", "简体中文");
            _offsetMap[0x02] = (ConfigItemType.Language, "R_STR_LAN_TCHINESE", "繁体中文");
            _offsetMap[0x03] = (ConfigItemType.Language, "R_STR_LAN_JAPANESE", "日本语");
            _offsetMap[0x04] = (ConfigItemType.Language, "R_STR_LAN_GERMAN", "Deutsch");
            _offsetMap[0x05] = (ConfigItemType.Language, "R_STR_LAN_FRECH", "Français");
            _offsetMap[0x06] = (ConfigItemType.Language, "R_STR_LAN_RUSSIAN", "Русский");
            _offsetMap[0x07] = (ConfigItemType.Language, "R_STR_LAN_ITALIAN", "Italiano");
            _offsetMap[0x08] = (ConfigItemType.Language, "R_STR_LAN_KOERA", "한국어");
            _offsetMap[0x09] = (ConfigItemType.Language, "R_STR_LAN_TAI", "ภาษาไทย");
            _offsetMap[0x0A] = (ConfigItemType.Language, "R_STR_LAN_HEBREW", "العربية");
            _offsetMap[0x0B] = (ConfigItemType.Language, "R_STR_LAN_DUTCH", "Nederlands");
            _offsetMap[0x0C] = (ConfigItemType.Language, "R_STR_LAN_UKRAINIAN", "Українська");
            _offsetMap[0x0D] = (ConfigItemType.Language, "R_STR_LAN_SPANISH", "Español");
            _offsetMap[0x0E] = (ConfigItemType.Language, "R_STR_LAN_PORTUGUESE", "Português");
            _offsetMap[0x0F] = (ConfigItemType.Language, "R_STR_LAN_POLISH", "Polski");
            _offsetMap[0x10] = (ConfigItemType.Language, "R_STR_LAN_CZECH", "Čeština");
            _offsetMap[0x11] = (ConfigItemType.Language, "R_STR_LAN_TURKEY", "Türkçe");

            // 通用开关: 0x14 ~ 0x19
            _offsetMap[0x14] = (ConfigItemType.OnOff, "R_STR_COM_OFF", "关闭");
            _offsetMap[0x15] = (ConfigItemType.OnOff, "R_STR_COM_ON", "开启");
            _offsetMap[0x16] = (ConfigItemType.OnOff, "R_STR_COM_OK", "确定");
            _offsetMap[0x17] = (ConfigItemType.OnOff, "R_STR_COM_CANCEL", "取消");
            _offsetMap[0x18] = (ConfigItemType.OnOff, "R_STR_COM_YES", "是");
            _offsetMap[0x19] = (ConfigItemType.OnOff, "R_STR_COM_NO", "否");

            // 灵敏度/频率: 0x1A ~ 0x1E
            _offsetMap[0x1A] = (ConfigItemType.Sensitivity, "R_STR_COM_LOW", "低");
            _offsetMap[0x1B] = (ConfigItemType.Sensitivity, "R_STR_COM_MIDDLE", "中");
            _offsetMap[0x1C] = (ConfigItemType.Sensitivity, "R_STR_COM_HIGH", "高");
            _offsetMap[0x1D] = (ConfigItemType.Frequency, "R_STR_COM_50HZ", "50Hz");
            _offsetMap[0x1E] = (ConfigItemType.Frequency, "R_STR_COM_60HZ", "60Hz");

            // 等级 LEVEL_0 ~ LEVEL_9: 0x1F ~ 0x28
            for (uint i = 0; i <= 9; i++)
            {
                _offsetMap[0x1F + i] = (ConfigItemType.Level, $"R_STR_COM_LEVEL_{i}", $"级别 {i}");
            }

            // 曝光补偿: 0x29 ~ 0x2F (与user_str.c一致)
            _offsetMap[0x29] = (ConfigItemType.ExposureValue, "R_ID_STR_COM_P4_0", "+4.0");
            _offsetMap[0x2A] = (ConfigItemType.ExposureValue, "R_ID_STR_COM_P3_0", "+3.0");
            _offsetMap[0x2B] = (ConfigItemType.ExposureValue, "R_ID_STR_COM_P2_0", "+2.0");
            _offsetMap[0x2C] = (ConfigItemType.ExposureValue, "R_ID_STR_COM_P1_0", "+1.0");
            _offsetMap[0x2D] = (ConfigItemType.ExposureValue, "R_ID_STR_COM_P0_0", "0.0");
            _offsetMap[0x2E] = (ConfigItemType.ExposureValue, "R_ID_STR_COM_N1_0", "-1.0");
            _offsetMap[0x2F] = (ConfigItemType.ExposureValue, "R_ID_STR_COM_N2_0", "-2.0");

            // 附加选项: 0x30 ~ 0x33
            _offsetMap[0x30] = (ConfigItemType.OnOff, "R_ID_STR_COM_ALWAYSON", "常亮");
            _offsetMap[0x31] = (ConfigItemType.Level, "R_ID_STR_COM_ECONOMIC", "经济");
            _offsetMap[0x32] = (ConfigItemType.Level, "R_ID_STR_COM_NORMAL", "标准");
            _offsetMap[0x33] = (ConfigItemType.Level, "R_ID_STR_COM_FINE", "精细");

            // 时间选项: 0x34 ~ 0x3D (与user_str.c一致)
            _offsetMap[0x34] = (ConfigItemType.AutoOffTime, "R_ID_STR_TIM_1MIN", "1分钟");
            _offsetMap[0x35] = (ConfigItemType.AutoOffTime, "R_ID_STR_TIM_2MIN", "2分钟");
            _offsetMap[0x36] = (ConfigItemType.AutoOffTime, "R_ID_STR_TIM_3MIN", "3分钟");
            _offsetMap[0x37] = (ConfigItemType.AutoOffTime, "R_ID_STR_TIM_5MIN", "5分钟");
            _offsetMap[0x38] = (ConfigItemType.AutoOffTime, "R_ID_STR_TIM_10MIN", "10分钟");
            _offsetMap[0x39] = (ConfigItemType.AutoOffTime, "R_ID_STR_TIM_2SEC", "2秒");
            _offsetMap[0x3A] = (ConfigItemType.AutoOffTime, "R_ID_STR_TIM_3SEC", "3秒");
            _offsetMap[0x3B] = (ConfigItemType.AutoOffTime, "R_ID_STR_TIM_5SEC", "5秒");
            _offsetMap[0x3C] = (ConfigItemType.AutoOffTime, "R_ID_STR_TIM_10SEC", "10秒");
            _offsetMap[0x3D] = (ConfigItemType.AutoOffTime, "R_ID_STR_TIM_30SEC", "30秒");

            // 拍照张数: 0x3E ~ 0x3F (与user_str.c一致)
            _offsetMap[0x3E] = (ConfigItemType.Level, "R_ID_STR_PHOTO_NUM_3", "3张");
            _offsetMap[0x3F] = (ConfigItemType.Level, "R_ID_STR_PHOTO_NUM_5", "5张");

            // 视频分辨率: 0x81 ~ 0x8B (与user_str.c一致)
            _offsetMap[0x81] = (ConfigItemType.Resolution, "R_ID_STR_RES_240P", "240P");
            _offsetMap[0x82] = (ConfigItemType.Resolution, "R_ID_STR_RES_480P", "480P");
            _offsetMap[0x83] = (ConfigItemType.Resolution, "R_ID_STR_RES_480FHD", "480FHD");
            _offsetMap[0x84] = (ConfigItemType.Resolution, "R_ID_STR_RES_720P", "720P");
            _offsetMap[0x85] = (ConfigItemType.Resolution, "R_ID_STR_RES_1024P", "1024P");
            _offsetMap[0x86] = (ConfigItemType.Resolution, "R_ID_STR_RES_1080P", "1080P");
            _offsetMap[0x87] = (ConfigItemType.Resolution, "R_ID_STR_RES_1080FHD", "1080FHD");
            _offsetMap[0x88] = (ConfigItemType.Resolution, "R_ID_STR_RES_1440P", "1440P");
            _offsetMap[0x89] = (ConfigItemType.Resolution, "R_ID_STR_RES_3024P", "3024P");
            _offsetMap[0x8A] = (ConfigItemType.Resolution, "R_ID_STR_RES_720P_SHORT", "720P_SHORT");
            _offsetMap[0x8B] = (ConfigItemType.Resolution, "R_ID_STR_RES_1080P_SHORT", "1080P_SHORT");

            // 拍照分辨率: 0x8C ~ 0x9C (与user_str.c一致)
            _offsetMap[0x8C] = (ConfigItemType.Resolution, "R_ID_STR_RES_QVGA", "QVGA");
            _offsetMap[0x8D] = (ConfigItemType.Resolution, "R_ID_STR_RES_VGA", "VGA");
            _offsetMap[0x8E] = (ConfigItemType.Resolution, "R_ID_STR_RES_HD", "HD");
            _offsetMap[0x8F] = (ConfigItemType.Resolution, "R_ID_STR_RES_FHD", "FHD");
            _offsetMap[0x90] = (ConfigItemType.Resolution, "R_ID_STR_RES_48M", "48M");
            _offsetMap[0x91] = (ConfigItemType.Resolution, "R_ID_STR_RES_40M", "40M");
            _offsetMap[0x92] = (ConfigItemType.Resolution, "R_ID_STR_RES_24M", "24M");
            _offsetMap[0x93] = (ConfigItemType.Resolution, "R_ID_STR_RES_20M", "20M");
            _offsetMap[0x94] = (ConfigItemType.Resolution, "R_ID_STR_RES_18M", "18M");
            _offsetMap[0x95] = (ConfigItemType.Resolution, "R_ID_STR_RES_16M", "16M");
            _offsetMap[0x96] = (ConfigItemType.Resolution, "R_ID_STR_RES_12M", "12M");
            _offsetMap[0x97] = (ConfigItemType.Resolution, "R_ID_STR_RES_10M", "10M");
            _offsetMap[0x98] = (ConfigItemType.Resolution, "R_ID_STR_RES_8M", "8M");
            _offsetMap[0x99] = (ConfigItemType.Resolution, "R_ID_STR_RES_5M", "5M");
            _offsetMap[0x9A] = (ConfigItemType.Resolution, "R_ID_STR_RES_3M", "3M");
            _offsetMap[0x9B] = (ConfigItemType.Resolution, "R_ID_STR_RES_2M", "2M");
            _offsetMap[0x9C] = (ConfigItemType.Resolution, "R_ID_STR_RES_1M", "1M");



            // ISP/白平衡: 0xA7 ~ 0xB7 (与user_str.c一致)
            _offsetMap[0xA7] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_WHITEBL", "白炽灯");
            _offsetMap[0xA8] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_ISO", "ISO");
            _offsetMap[0xA9] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_ANTISHANK", "防抖");
            _offsetMap[0xAA] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_AUTO", "自动");
            _offsetMap[0xAB] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_SOFT", "柔和");
            _offsetMap[0xAC] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_STRONG", "强烈");
            _offsetMap[0xAD] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_SUNLIGHT", "晴天");
            _offsetMap[0xAE] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_CLOUDY", "阴天");
            _offsetMap[0xAF] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_TUNGSTEN", "办公室");
            _offsetMap[0xB0] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_FLUORESCENT", "荧光灯");
            _offsetMap[0xB1] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_BLACKWHITE", "黑白");
            _offsetMap[0xB2] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_SEPIA", "复古");
            _offsetMap[0xB3] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_ISO100", "ISO100");
            _offsetMap[0xB4] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_ISO200", "ISO200");
            _offsetMap[0xB5] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_ISO400", "ISO400");
            _offsetMap[0xB6] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_WDR", "WDR");
            _offsetMap[0xB7] = (ConfigItemType.WhiteBalance, "R_ID_STR_ISP_EXPOSURE", "曝光");

            _initialized = true;
            System.Diagnostics.Debug.WriteLine($"[UniversalValueDecoder] Initialized offset map with {_offsetMap.Count} entries");
        }

        /// <summary>
        /// 解码固件配置值（优化版本 - 使用字典查找）
        /// </summary>
        /// <param name="value">固件中的 uint 值</param>
        /// <returns>解码结果</returns>
        public static DecodeResult Decode(uint value)
        {
            var result = new DecodeResult { RawValue = value };

            // 1. 检查是否为 R_ID_TYPE_STR 格式
            if ((value & 0xFF000000) != FirmwareConstants.R_ID_TYPE_STR)
            {
                // 不是 R_ID_STR_* 格式，可能是原始数值
                result.InferredType = ConfigItemType.Numeric;
                result.DisplayText = value.ToString();
                result.Confidence = 0.3; // 低置信度，因为不确定
                return result;
            }

            uint offset = value - FirmwareConstants.R_ID_TYPE_STR;

            // 2. 使用字典查找（O(1) 复杂度）
            if (_offsetMap.TryGetValue(offset, out var entry))
            {
                result.InferredType = entry.type;
                result.DisplayText = entry.display;
                result.Confidence = 1.0;
                result.MatchedEnumName = entry.name;
                return result;
            }

            // 3. 已识别为 R_ID_TYPE_STR 格式但无法精确匹配
            result.InferredType = ConfigItemType.RawHex;
            result.DisplayText = $"0x{value:X8} (偏移: 0x{offset:X2})";
            result.Confidence = 0.2;
            return result;
        }

        /// <summary>
        /// 批量解码固件配置数据，返回每个配置项的推断类型
        /// </summary>
        /// <param name="flags">固件配置 flags 数组</param>
        /// <returns>每个索引位置的解码结果</returns>
        public static List<DecodeResult> DecodeAll(uint[] flags)
        {
            var results = new List<DecodeResult>();
            if (flags == null) return results;

            for (int i = 0; i < flags.Length; i++)
            {
                results.Add(Decode(flags[i]));
            }
            return results;
        }

        /// <summary>
        /// 分析固件配置数据，生成配置项类型分布报告
        /// </summary>
        public static string AnalyzeConfigProfile(uint[] flags)
        {
            if (flags == null || flags.Length == 0)
                return "无配置数据";

            var typeCounts = new Dictionary<ConfigItemType, int>();
            int recognizedCount = 0;
            int stringTypeCount = 0;

            for (int i = 0; i < flags.Length; i++)
            {
                uint value = flags[i];
                if (value == 0 || value == 0xFFFFFFFF)
                    continue; // 跳过空白值

                var result = Decode(value);
                
                if (!typeCounts.ContainsKey(result.InferredType))
                    typeCounts[result.InferredType] = 0;
                typeCounts[result.InferredType]++;

                if (result.Confidence >= 0.8)
                    recognizedCount++;
                if (result.IsStringType)
                    stringTypeCount++;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== 固件配置分析报告 ===");
            sb.AppendLine($"配置项总数: {flags.Length}");
            sb.AppendLine($"R_ID_TYPE_STR 格式值: {stringTypeCount}");
            sb.AppendLine($"已识别配置项 (置信度>=0.8): {recognizedCount}");
            sb.AppendLine();
            sb.AppendLine("类型分布:");
            foreach (var kvp in typeCounts)
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value} 项");
            }

            return sb.ToString();
        }
    }
}
