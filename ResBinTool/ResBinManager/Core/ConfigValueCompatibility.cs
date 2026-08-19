using System;
using System.Collections.Generic;
using System.Linq;
using ResBinManager.Models;

namespace ResBinManager.Core
{
    /// <summary>
    /// 项目识别器 - 通过分析bin文件中的字符串资源自动识别项目类型
    /// </summary>
    public static class ProjectIdentifier
    {
        /// <summary>
        /// 通过分析字符串表识别项目类型
        /// </summary>
        public static ProjectType IdentifyProject(byte[] stringTableData, int stringCount)
        {
            if (stringTableData == null || stringTableData.Length == 0)
            {
                return ProjectType.Unknown;
            }

            // 检查特征字符串ID是否存在
            bool has2160P = HasStringId(stringTableData, 0x0188);
            bool has2160PShort = HasStringId(stringTableData, 0x018D);
            bool has1440PShort = HasStringId(stringTableData, 0x018C);
            bool has100M = HasStringId(stringTableData, 0x0192);
            bool has56M = HasStringId(stringTableData, 0x0193);
            bool has4M = HasStringId(stringTableData, 0x01A1);
            bool hasRomanian = HasStringId(stringTableData, 0x0112);
            bool hasPhotoPuzzle = HasStringId(stringTableData, (uint)(GetLastStringIdOffset(stringCount) + 1));
            bool hasTipsIntSdc = HasStringId(stringTableData, (uint)(GetLastStringIdOffset(stringCount) + 2));
            bool hasTipsSdcMov = HasStringId(stringTableData, (uint)(GetLastStringIdOffset(stringCount) + 3));

            // 根据特征组合判断项目类型
            if (has2160P && has2160PShort && has1440PShort && has100M && has56M)
            {
                if (hasRomanian)
                {
                    return ProjectType.DC508J;
                }
                return ProjectType.JT529X;
            }

            if (hasTipsIntSdc && hasTipsSdcMov)
            {
                return ProjectType.HM020F;
            }

            if (has4M)
            {
                return ProjectType.GX_T317BV200;
            }

            // 检查是否有COM_WAITING（MKL_DM15和JRX_AX329X独有）
            bool hasWaiting = HasStringId(stringTableData, 0x0035); // R_ID_STR_COM_WAITING
            if (hasWaiting)
            {
                // 检查是否有CFG_SET_USBPHOTO（MKL_DM15独有）
                bool hasUsbPhoto = HasStringId(stringTableData, 0x00C5); // CFG_SET_USBPHOTO
                if (hasUsbPhoto)
                {
                    return ProjectType.MKL_DM15;
                }
                return ProjectType.JRX_AX329X;
            }

            // 检查是否有GAME相关字符串（JRX_JT529X独有）
            bool hasGame = HasStringId(stringTableData, 0x01C0); // CFG_GAME_TIPS_GAME_OVER
            if (hasGame && has2160P)
            {
                return ProjectType.JRX_JT529X;
            }

            // 默认返回MKL_CM5（最基础的项目）
            return ProjectType.MKL_CM5;
        }

        /// <summary>
        /// 检查字符串表中是否存在指定的字符串ID
        /// </summary>
        private static bool HasStringId(byte[] stringTableData, uint stringId)
        {
            // 字符串表格式：每个字符串条目包含ID和长度信息
            // 这里简化处理，实际需要根据具体格式解析
            try
            {
                // 假设字符串表是连续的，每个条目4字节（ID + 长度）
                int entrySize = 4;
                int offset = (int)(stringId * entrySize);
                return offset + entrySize <= stringTableData.Length;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取最后一个字符串ID的偏移量
        /// </summary>
        private static int GetLastStringIdOffset(int stringCount)
        {
            return stringCount - 1;
        }

        /// <summary>
        /// 通过配置文件特征识别项目类型
        /// </summary>
        public static ProjectType IdentifyProjectByConfig(FirmwareConfigData configData)
        {
            if (configData == null || !configData.IsValid)
            {
                return ProjectType.Unknown;
            }

            // 检查配置项数量
            int configCount = configData.ActiveConfigCount;

            // 检查特定配置项的值
            uint resolution = configData.Flags[(int)ConfigId.CONFIG_ID_RESOLUTION];
            uint photoResolution = configData.Flags[(int)ConfigId.CONFIG_ID_PRESLUTION];

            // 根据分辨率判断
            bool has2160P = (resolution == 0x0188);
            bool has2160PShort = (resolution == 0x018D);
            bool has1440PShort = (resolution == 0x018C);
            bool has100M = (photoResolution == 0x0192);
            bool has56M = (photoResolution == 0x0193);

            if (has2160P || has2160PShort || has1440PShort)
            {
                if (has100M || has56M)
                {
                    return ProjectType.DC508J;  // 默认返回DC508J（需要更多特征区分）
                }
                return ProjectType.JT529X;
            }

            return ProjectType.MKL_CM5;  // 默认
        }

        /// <summary>
        /// 获取项目类型的显示名称
        /// </summary>
        public static string GetProjectDisplayName(ProjectType projectType)
        {
            return projectType switch
            {
                ProjectType.JT529X => "JT529X",
                ProjectType.DC508J => "DC508J",
                ProjectType.GX_T317BV200 => "GX-T317BV200",
                ProjectType.HM020F => "HM020F",
                ProjectType.MKL_CM5 => "MKL_CM5",
                ProjectType.MKL_DM15 => "MKL_DM15",
                ProjectType.JRX_JT529X => "JRX_SDK JT529X",
                ProjectType.JRX_AX329X => "JRX_SDK AX329X",
                _ => "未知项目"
            };
        }

        /// <summary>
        /// 获取项目类型的描述信息
        /// </summary>
        public static string GetProjectDescription(ProjectType projectType)
        {
            var features = ProjectFeatureDatabase.GetFeatures(projectType);
            return features.Description;
        }
    }

    /// <summary>
    /// 配置值兼容层 - 处理不同项目间配置值的差异
    /// </summary>
    public static class ConfigValueCompatibility
    {
        /// <summary>
        /// 检查配置值在指定项目中是否有效
        /// </summary>
        public static bool IsConfigValueValid(ProjectType projectType, ConfigId configId, uint value)
        {
            var features = ProjectFeatureDatabase.GetFeatures(projectType);

            switch (configId)
            {
                case ConfigId.CONFIG_ID_RESOLUTION:
                    return IsVideoResolutionValid(features, value);

                case ConfigId.CONFIG_ID_PRESLUTION:
                    return IsPhotoResolutionValid(features, value);

                case ConfigId.CONFIG_ID_LANGUAGE:
                    return IsLanguageValid(features, value);

                default:
                    return true;  // 其他配置项默认有效
            }
        }

        /// <summary>
        /// 检查视频分辨率在项目中是否有效
        /// </summary>
        private static bool IsVideoResolutionValid(ProjectConfigFeatures features, uint value)
        {
            return value switch
            {
                0x0180 => true,  // 240P
                0x0181 => true,  // 480P
                0x0182 => true,  // 480FHD
                0x0183 => true,  // 720P
                0x0184 => true,  // 1024P
                0x0185 => true,  // 1080P
                0x0186 => true,  // 1080FHD
                0x0187 => true,  // 1440P
                0x0188 => features.Has2160P,  // 2160P
                0x0189 => true,  // 3024P
                0x018A => true,  // 720P_SHORT
                0x018B => true,  // 1080P_SHORT
                0x018C => features.Has1440PShort,  // 1440P_SHORT
                0x018D => features.Has2160PShort,  // 2160P_SHORT
                _ => false
            };
        }

        /// <summary>
        /// 检查拍照分辨率在项目中是否有效
        /// </summary>
        private static bool IsPhotoResolutionValid(ProjectConfigFeatures features, uint value)
        {
            return value switch
            {
                0x0192 => features.Has100M,  // 100M
                0x0193 => features.Has56M,   // 56M
                0x0194 => true,  // 48M
                0x0195 => true,  // 40M
                0x0196 => true,  // 24M
                0x0197 => true,  // 20M
                0x0198 => true,  // 18M
                0x0199 => true,  // 16M
                0x019A => true,  // 12M
                0x019B => true,  // 10M
                0x019C => true,  // 8M
                0x019D => true,  // 5M
                0x019E => true,  // 3M
                0x019F => true,  // 2M
                0x01A0 => true,  // 1M
                0x01A1 => features.Has4M,  // 4M (GX-T317BV200独有)
                _ => false
            };
        }

        /// <summary>
        /// 检查语言在项目中是否有效
        /// </summary>
        private static bool IsLanguageValid(ProjectConfigFeatures features, uint value)
        {
            return value switch
            {
                >= 0x0100 and <= 0x0111 => true,  // 基础语言
                0x0112 => features.HasRomanian,    // 罗马尼亚语
                _ => false
            };
        }

        /// <summary>
        /// 获取配置值的显示文本（考虑项目差异）
        /// </summary>
        public static string GetConfigValueDisplay(ProjectType projectType, ConfigId configId, uint value)
        {
            var features = ProjectFeatureDatabase.GetFeatures(projectType);

            switch (configId)
            {
                case ConfigId.CONFIG_ID_RESOLUTION:
                    return GetVideoResolutionDisplay(features, value);

                case ConfigId.CONFIG_ID_PRESLUTION:
                    return GetPhotoResolutionDisplay(features, value);

                case ConfigId.CONFIG_ID_LANGUAGE:
                    return GetLanguageDisplay(features, value);

                case ConfigId.CONFIG_ID_KEYSOUND:
                case ConfigId.CONFIG_ID_TIMESTAMP:
                case ConfigId.CONFIG_ID_AUDIOREC:
                case ConfigId.CONFIG_ID_AUTOOFF:
                case ConfigId.CONFIG_ID_SCREENSAVE:
                    return GetOnOffDisplay(value);

                case ConfigId.CONFIG_ID_VOLUME:
                case ConfigId.CONFIG_ID_LCD_BRIGHT:
                    return GetLevelDisplay(value);

                case ConfigId.CONFIG_ID_LOOPTIME:
                    return GetLoopTimeDisplay(value);

                case ConfigId.CONFIG_ID_FREQUNCY:
                    return GetFrequencyDisplay(value);

                default:
                    return $"0x{value:X4}";
            }
        }

        /// <summary>
        /// 获取视频分辨率显示文本
        /// </summary>
        private static string GetVideoResolutionDisplay(ProjectConfigFeatures features, uint value)
        {
            return value switch
            {
                0x0180 => "240P",
                0x0181 => "480P",
                0x0182 => "480FHD",
                0x0183 => "720P",
                0x0184 => "1024P",
                0x0185 => "1080P",
                0x0186 => "1080FHD",
                0x0187 => "1440P",
                0x0188 => features.Has2160P ? "2160P" : "未知",
                0x0189 => "3024P",
                0x018A => "720P_SHORT",
                0x018B => "1080P_SHORT",
                0x018C => features.Has1440PShort ? "1440P_SHORT" : "未知",
                0x018D => features.Has2160PShort ? "2160P_SHORT" : "未知",
                _ => $"未知 (0x{value})"
            };
        }

        /// <summary>
        /// 获取拍照分辨率显示文本
        /// </summary>
        private static string GetPhotoResolutionDisplay(ProjectConfigFeatures features, uint value)
        {
            return value switch
            {
                0x0192 => features.Has100M ? "100M" : "未知",
                0x0193 => features.Has56M ? "56M" : "未知",
                0x0194 => "48M",
                0x0195 => "40M",
                0x0196 => "24M",
                0x0197 => "20M",
                0x0198 => "18M",
                0x0199 => "16M",
                0x019A => "12M",
                0x019B => "10M",
                0x019C => "8M",
                0x019D => "5M",
                0x019E => "3M",
                0x019F => "2M",
                0x01A0 => "1M",
                0x01A1 => features.Has4M ? "4M" : "未知",
                _ => $"未知 (0x{value})"
            };
        }

        /// <summary>
        /// 获取语言显示文本
        /// </summary>
        private static string GetLanguageDisplay(ProjectConfigFeatures features, uint value)
        {
            return value switch
            {
                0x0100 => "English",
                0x0101 => "简体中文",
                0x0102 => "繁体中文",
                0x0103 => "日本语",
                0x0104 => "Deutsch",
                0x0105 => "Français",
                0x0106 => "Русский",
                0x0107 => "Italiano",
                0x0108 => "한국어",
                0x0109 => "ภาษาไทย",
                0x010A => "العربية",
                0x010B => "Nederlands",
                0x010C => "Українська",
                0x010D => "Español",
                0x010E => "Português",
                0x010F => "Polski",
                0x0110 => "Čeština",
                0x0111 => "Türkçe",
                0x0112 => features.HasRomanian ? "Română" : "未知",
                _ => $"未知 (0x{value})"
            };
        }

        /// <summary>
        /// 获取开关显示文本
        /// </summary>
        private static string GetOnOffDisplay(uint value) =>
            value switch { 0x0114 => "关闭", 0x0115 => "开启", _ => $"未知 (0x{value:X4})" };

        /// <summary>
        /// 获取级别显示文本
        /// </summary>
        private static string GetLevelDisplay(uint value) =>
            (value >= 0x011F && value <= 0x0128) ? $"级别 {value - 0x011F}" : $"未知 (0x{value})";

        /// <summary>
        /// 获取循环时间显示文本
        /// </summary>
        private static string GetLoopTimeDisplay(uint value) =>
            value switch
            {
                0x0134 => "1 分钟",
                0x0135 => "2 分钟",
                0x0136 => "3 分钟",
                0x0137 => "5 分钟",
                0x0138 => "10 分钟",
                _ => $"未知 (0x{value:X4})"
            };

        /// <summary>
        /// 获取频率显示文本
        /// </summary>
        private static string GetFrequencyDisplay(uint value) =>
            value switch { 0x011D => "50Hz", 0x011E => "60Hz", _ => $"未知 (0x{value:X4})" };
    }
}
