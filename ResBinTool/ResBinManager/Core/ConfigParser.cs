using ResBinManager.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ResBinManager.Core
{
    public class ConfigParser
    {
        private const uint BOOT_SECTOR_MAGIC = 0x52444C42;
        
        // SDK 当前定义的最大配置项数量
        // 基于503项目 config.h 的 CONFIG_ID_MAX = 52（索引0-51）
        // 但不同项目可能有不同数量的配置项，最大支持 127（Flags 数组大小）
        public const int SDK_CONFIG_ID_MAX = 127;
        public const int CONFIG_FLAGS_COUNT = 127;
        public const int CONFIG_SYSTEM_SIZE = 512; // (127 + 1) * 4

        /// <summary>
        /// 精确读取指定字节数，不足时抛出异常
        /// </summary>
        private static void ReadExact(FileStream fs, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = fs.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                    throw new EndOfStreamException($"Expected {count} bytes, only read {totalRead}");
                totalRead += read;
            }
        }
        
        // 已知配置项的默认值表（用于填充空白配置项）
        // 使用 ConfigTemplateManager 统一管理，保持向后兼容
        private static Dictionary<ConfigId, uint> KnownDefaultValues
        {
            get => ConfigTemplateManager.CurrentTemplate.DefaultValues;
        }

        private const uint CONFIG_ALIGNMENT = 0x1000;

        /// <summary>
        /// 从 DestBin 文件解析配置数据
        /// </summary>
        /// <param name="destBinPath">DestBin 文件路径</param>
        /// <param name="projectType">项目类型（可选，用于确定配置项映射）</param>
        /// <returns>固件配置数据</returns>
        public static FirmwareConfigData ParseConfigFromDestBin(
            string destBinPath, 
            ProjectType projectType = ProjectType.Unknown)
        {
            FirmwareConfigData CreateErrorData(string message)
            {
                var data = new FirmwareConfigData();
                data.ProjectType = projectType;
                data.StatusMessage = message;
                return data;
            }

            try
            {
                using (var fs = new FileStream(destBinPath, FileMode.Open, FileAccess.Read))
                {
                    uint resAddress;
                    uint resSize;

                    var bootSector = ParseBootSector(fs);
                    if (bootSector == null)
                        return CreateErrorData("无法解析启动扇区");

                    var flashParam = ParseFlashParam(fs, bootSector.SectorNumber);
                    if (flashParam == null)
                        return CreateErrorData("无法解析 Flash 参数");

                    resAddress = flashParam.ResAddress;
                    resSize = flashParam.ResSize;

                    if (resAddress == 0)
                        return CreateErrorData("资源区地址无效");

                    if (resSize > uint.MaxValue - resAddress)
                        return CreateErrorData("资源地址计算溢出");

                    uint configAddress = resAddress + resSize;
                    uint configAddressBeforeAlign = configAddress;
                    
                    configAddress = (configAddress + CONFIG_ALIGNMENT - 1) & ~(CONFIG_ALIGNMENT - 1);

                    // 检查对齐后是否溢出（进位导致回绕）
                    if (configAddress < configAddressBeforeAlign)
                        return CreateErrorData("配置区地址计算溢出（对齐后）");

                    if (configAddress < resAddress + resSize)
                        return CreateErrorData($"配置区地址 0x{configAddress:X} 位于资源区内部");

                    System.Diagnostics.Debug.WriteLine($"[ConfigParser] Config address calculation:");
                    System.Diagnostics.Debug.WriteLine($"  resAddress: 0x{resAddress:X}");
                    System.Diagnostics.Debug.WriteLine($"  resSize: 0x{resSize:X}");
                    System.Diagnostics.Debug.WriteLine($"  configAddress (before align): 0x{configAddressBeforeAlign:X}");
                    System.Diagnostics.Debug.WriteLine($"  configAddress (after align): 0x{configAddress:X}");
                    System.Diagnostics.Debug.WriteLine($"  File size: 0x{fs.Length:X} ({fs.Length} bytes)");
                    System.Diagnostics.Debug.WriteLine($"  Project type: {projectType}");

                    if (configAddress >= fs.Length)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConfigParser] Config address exceeds file size");
                        return CreateErrorData($"配置区地址 0x{configAddress:X} 超出固件大小 (0x{fs.Length:X})");
                    }

                    var result = ParseConfigData(fs, configAddress, projectType);
                    System.Diagnostics.Debug.WriteLine($"[ConfigParser] ParseConfigData result: IsValid={result.IsValid}, Status={result.StatusMessage}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                return CreateErrorData($"解析配置失败: {ex.Message}");
            }
        }

        private static BootSectorInfo ParseBootSector(FileStream fs)
        {
            if (fs.Length < 16)
                return null;

            fs.Position = 0;
            byte[] buffer = new byte[16];
            ReadExact(fs, buffer, 0, 16);

            uint magic = BitConverter.ToUInt32(buffer, 4);
            if (magic != BOOT_SECTOR_MAGIC)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigParser] Invalid magic: 0x{magic:X8} (expected 0x{BOOT_SECTOR_MAGIC:X8})");
                return null;
            }

            byte sectorNumber = buffer[9];
            System.Diagnostics.Debug.WriteLine($"[ConfigParser] Boot sector parsed: magic=0x{magic:X8}, sectorNumber={sectorNumber}");

            return new BootSectorInfo
            {
                Magic = magic,
                SectorNumber = sectorNumber
            };
        }

        private static FlashParamInfo ParseFlashParam(FileStream fs, byte sectorNumber)
        {
            uint flashParamOffset = (uint)(sectorNumber << 4);

            if (flashParamOffset + 64 > fs.Length)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigParser] FlashParam offset out of range: 0x{flashParamOffset:X}");
                return null;
            }

            fs.Position = flashParamOffset;
            byte[] buffer = new byte[64];
            ReadExact(fs, buffer, 0, 64);

            uint resAddress = BitConverter.ToUInt32(buffer, 0x08) << 9;
            uint resSize = BitConverter.ToUInt32(buffer, 0x0C) << 9;

            System.Diagnostics.Debug.WriteLine($"[ConfigParser] FlashParam parsed: resAddress=0x{resAddress:X}, resSize=0x{resSize:X}");

            return new FlashParamInfo
            {
                ResAddress = resAddress,
                ResSize = resSize
            };
        }

        private static FirmwareConfigData ParseConfigData(FileStream fs, uint configAddress, ProjectType projectType = ProjectType.Unknown)
        {
            var configData = new FirmwareConfigData();
            configData.ConfigAddress = configAddress;
            configData.ProjectType = projectType;

            // 根据项目类型获取配置映射
            if (projectType != ProjectType.Unknown)
            {
                configData.Mapping = ProjectConfigMappingDatabase.GetMapping(projectType);
                System.Diagnostics.Debug.WriteLine($"[ConfigParser] Using mapping for project: {projectType}, config count: {configData.Mapping.ConfigItemCount}");
            }

            if (configAddress + CONFIG_SYSTEM_SIZE > fs.Length)
            {
                configData.StatusMessage = $"配置区数据不完整，需要 {CONFIG_SYSTEM_SIZE} 字节，但固件剩余 {fs.Length - configAddress} 字节";
                return configData;
            }

            fs.Position = configAddress;
            byte[] buffer = new byte[CONFIG_SYSTEM_SIZE];
            ReadExact(fs, buffer, 0, CONFIG_SYSTEM_SIZE);

            for (int i = 0; i < CONFIG_FLAGS_COUNT; i++)
            {
                configData.Flags[i] = BitConverter.ToUInt32(buffer, i * 4);
            }

            configData.CheckSum = BitConverter.ToUInt32(buffer, CONFIG_FLAGS_COUNT * 4);

            uint calculatedCheckSum = configData.CalculateCheckSum();
            
            bool isBlank = true;
            for (int i = 0; i < CONFIG_FLAGS_COUNT; i++)
            {
                if (configData.Flags[i] != 0xFFFFFFFF && configData.Flags[i] != 0x00000000)
                {
                    isBlank = false;
                    break;
                }
            }

            if (isBlank)
            {
                configData.IsValid = false;
                configData.StatusMessage = "配置区为空白（未初始化），可以加载默认配置";
                configData.ActiveConfigCount = 0;
                configData.ConfigVersion = "Blank";
                System.Diagnostics.Debug.WriteLine($"[ConfigParser] Config area is blank (uninitialized)");
            }
            else if (calculatedCheckSum == configData.CheckSum)
            {
                configData.IsValid = true;
                configData.StatusMessage = $"配置数据有效 (校验和: 0x{configData.CheckSum:X8})";
                
                configData.ActiveConfigCount = DetectActiveConfigCount(configData.Flags, projectType);
                configData.ConfigVersion = InferConfigVersion(configData.ActiveConfigCount);
                
                System.Diagnostics.Debug.WriteLine($"[ConfigParser] Config valid, active count: {configData.ActiveConfigCount}, version: {configData.ConfigVersion}");
            }
            else
            {
                configData.IsValid = false;
                configData.StatusMessage = $"配置数据校验失败 (期望: 0x{calculatedCheckSum:X8}, 实际: 0x{configData.CheckSum:X8})";
                
                configData.ActiveConfigCount = DetectActiveConfigCount(configData.Flags, projectType);
                configData.ConfigVersion = InferConfigVersion(configData.ActiveConfigCount);
            }

            return configData;
        }

        // ============================================================
        // R_ID_TYPE_STR 基址常量
        // 来自 max.h: #define R_ID_TYPE_STR 0x81000000
        // 所有 R_ID_STR_* 枚举值 = R_ID_TYPE_STR + 枚举偏移
        // 统一使用 FirmwareConstants 中的定义
        // ============================================================
        private const uint R_ID_TYPE_STR = FirmwareConstants.R_ID_TYPE_STR;

        // 语言选项值 - 引用 FirmwareConstants
        private const uint R_STR_LAN_ENGLISH    = FirmwareConstants.R_STR_LAN_ENGLISH;
        private const uint R_STR_LAN_SCHINESE   = FirmwareConstants.R_STR_LAN_SCHINESE;
        private const uint R_STR_LAN_TCHINESE   = FirmwareConstants.R_STR_LAN_TCHINESE;
        private const uint R_STR_LAN_JAPANESE   = FirmwareConstants.R_STR_LAN_JAPANESE;
        private const uint R_STR_LAN_GERMAN     = FirmwareConstants.R_STR_LAN_GERMAN;
        private const uint R_STR_LAN_FRECH      = FirmwareConstants.R_STR_LAN_FRECH;
        private const uint R_STR_LAN_RUSSIAN    = FirmwareConstants.R_STR_LAN_RUSSIAN;
        private const uint R_STR_LAN_ITALIAN    = FirmwareConstants.R_STR_LAN_ITALIAN;
        private const uint R_STR_LAN_KOERA      = FirmwareConstants.R_STR_LAN_KOERA;
        private const uint R_STR_LAN_TAI        = FirmwareConstants.R_STR_LAN_TAI;
        private const uint R_STR_LAN_HEBREW     = FirmwareConstants.R_STR_LAN_HEBREW;
        private const uint R_STR_LAN_DUTCH      = FirmwareConstants.R_STR_LAN_DUTCH;
        private const uint R_STR_LAN_UKRAINIAN  = FirmwareConstants.R_STR_LAN_UKRAINIAN;
        private const uint R_STR_LAN_SPANISH    = FirmwareConstants.R_STR_LAN_SPANISH;
        private const uint R_STR_LAN_PORTUGUESE = FirmwareConstants.R_STR_LAN_PORTUGUESE;
        private const uint R_STR_LAN_POLISH     = FirmwareConstants.R_STR_LAN_POLISH;
        private const uint R_STR_LAN_CZECH      = FirmwareConstants.R_STR_LAN_CZECH;
        private const uint R_STR_LAN_TURKEY     = FirmwareConstants.R_STR_LAN_TURKEY;

        // 通用开关/选项值 - 引用 FirmwareConstants
        private const uint R_STR_COM_OFF        = FirmwareConstants.R_STR_COM_OFF;
        private const uint R_STR_COM_ON         = FirmwareConstants.R_STR_COM_ON;
        private const uint R_STR_COM_LOW        = FirmwareConstants.R_STR_COM_LOW;
        private const uint R_STR_COM_MIDDLE     = FirmwareConstants.R_STR_COM_MIDDLE;
        private const uint R_STR_COM_HIGH       = FirmwareConstants.R_STR_COM_HIGH;
        private const uint R_STR_COM_50HZ       = FirmwareConstants.R_STR_COM_50HZ;
        private const uint R_STR_COM_60HZ       = FirmwareConstants.R_STR_COM_60HZ;

        // 等级值 LEVEL_0 ~ LEVEL_9 - 引用 FirmwareConstants
        private const uint R_STR_COM_LEVEL_0    = FirmwareConstants.R_STR_COM_LEVEL_0;

        // 曝光补偿值 - 引用 FirmwareConstants
        private const uint R_STR_COM_P4_0       = FirmwareConstants.R_STR_COM_P4_0;
        private const uint R_STR_COM_P3_0       = FirmwareConstants.R_STR_COM_P3_0;
        private const uint R_STR_COM_P2_0       = FirmwareConstants.R_STR_COM_P2_0;
        private const uint R_STR_COM_P1_0       = FirmwareConstants.R_STR_COM_P1_0;
        private const uint R_STR_COM_P0_0       = FirmwareConstants.R_STR_COM_P0_0;
        private const uint R_STR_COM_N1_0       = FirmwareConstants.R_STR_COM_N1_0;
        private const uint R_STR_COM_N2_0       = FirmwareConstants.R_STR_COM_N2_0;

        // 时间值 - 引用 FirmwareConstants
        private const uint R_STR_TIM_1MIN       = FirmwareConstants.R_STR_TIM_1MIN;
        private const uint R_STR_TIM_2MIN       = FirmwareConstants.R_STR_TIM_2MIN;
        private const uint R_STR_TIM_3MIN       = FirmwareConstants.R_STR_TIM_3MIN;
        private const uint R_STR_TIM_5MIN       = FirmwareConstants.R_STR_TIM_5MIN;
        private const uint R_STR_TIM_10MIN      = FirmwareConstants.R_STR_TIM_10MIN;
        private const uint R_STR_TIM_2SEC       = FirmwareConstants.R_STR_TIM_2SEC;
        private const uint R_STR_TIM_3SEC       = FirmwareConstants.R_STR_TIM_3SEC;
        private const uint R_STR_TIM_5SEC       = FirmwareConstants.R_STR_TIM_5SEC;
        private const uint R_STR_TIM_10SEC      = FirmwareConstants.R_STR_TIM_10SEC;
        private const uint R_STR_TIM_30SEC      = FirmwareConstants.R_STR_TIM_30SEC;

        // 视频分辨率值 - 引用 FirmwareConstants
        private const uint R_STR_RES_240P        = FirmwareConstants.R_STR_RES_240P;
        private const uint R_STR_RES_480P        = FirmwareConstants.R_STR_RES_480P;
        private const uint R_STR_RES_480FHD      = FirmwareConstants.R_STR_RES_480FHD;
        private const uint R_STR_RES_720P        = FirmwareConstants.R_STR_RES_720P;
        private const uint R_STR_RES_1024P       = FirmwareConstants.R_STR_RES_1024P;
        private const uint R_STR_RES_1080P       = FirmwareConstants.R_STR_RES_1080P;
        private const uint R_STR_RES_1080FHD     = FirmwareConstants.R_STR_RES_1080FHD;
        private const uint R_STR_RES_1440P       = FirmwareConstants.R_STR_RES_1440P;
        private const uint R_STR_RES_2160P       = FirmwareConstants.R_STR_RES_2160P;
        private const uint R_STR_RES_3024P       = FirmwareConstants.R_STR_RES_3024P;
        private const uint R_STR_RES_720P_SHORT  = FirmwareConstants.R_STR_RES_720P_SHORT;
        private const uint R_STR_RES_1080P_SHORT = FirmwareConstants.R_STR_RES_1080P_SHORT;
        private const uint R_STR_RES_1440P_SHORT = FirmwareConstants.R_STR_RES_1440P_SHORT;
        private const uint R_STR_RES_2160P_SHORT = FirmwareConstants.R_STR_RES_2160P_SHORT;
        private const uint R_STR_RES_QVGA        = FirmwareConstants.R_STR_RES_QVGA;
        private const uint R_STR_RES_VGA         = FirmwareConstants.R_STR_RES_VGA;
        private const uint R_STR_RES_HD          = FirmwareConstants.R_STR_RES_HD;
        private const uint R_STR_RES_FHD         = FirmwareConstants.R_STR_RES_FHD;
        private const uint R_STR_RES_48M         = FirmwareConstants.R_STR_RES_48M;
        private const uint R_STR_RES_40M         = FirmwareConstants.R_STR_RES_40M;
        private const uint R_STR_RES_24M         = FirmwareConstants.R_STR_RES_24M;
        private const uint R_STR_RES_20M         = FirmwareConstants.R_STR_RES_20M;
        private const uint R_STR_RES_18M         = FirmwareConstants.R_STR_RES_18M;
        private const uint R_STR_RES_16M         = FirmwareConstants.R_STR_RES_16M;
        private const uint R_STR_RES_12M         = FirmwareConstants.R_STR_RES_12M;
        private const uint R_STR_RES_10M         = FirmwareConstants.R_STR_RES_10M;
        private const uint R_STR_RES_8M          = FirmwareConstants.R_STR_RES_8M;
        private const uint R_STR_RES_5M          = FirmwareConstants.R_STR_RES_5M;
        private const uint R_STR_RES_4M          = FirmwareConstants.R_STR_RES_4M;
        private const uint R_STR_RES_3M          = FirmwareConstants.R_STR_RES_3M;
        private const uint R_STR_RES_2M          = FirmwareConstants.R_STR_RES_2M;
        private const uint R_STR_RES_1M          = FirmwareConstants.R_STR_RES_1M;

        // ISP/白平衡值 - 引用 FirmwareConstants
        private const uint R_STR_ISP_WHITEBL     = FirmwareConstants.R_STR_ISP_WHITEBL;
        private const uint R_STR_ISP_ISO         = FirmwareConstants.R_STR_ISP_ISO;
        private const uint R_STR_ISP_ANTISHANK   = FirmwareConstants.R_STR_ISP_ANTISHANK;
        private const uint R_STR_ISP_AUTO        = FirmwareConstants.R_STR_ISP_AUTO;
        private const uint R_STR_ISP_SOFT        = FirmwareConstants.R_STR_ISP_SOFT;
        private const uint R_STR_ISP_STRONG      = FirmwareConstants.R_STR_ISP_STRONG;
        private const uint R_STR_ISP_SUNLIGHT    = FirmwareConstants.R_STR_ISP_SUNLIGHT;
        private const uint R_STR_ISP_CLOUDY      = FirmwareConstants.R_STR_ISP_CLOUDY;
        private const uint R_STR_ISP_TUNGSTEN    = FirmwareConstants.R_STR_ISP_TUNGSTEN;
        private const uint R_STR_ISP_FLUORESCENT = FirmwareConstants.R_STR_ISP_FLUORESCENT;
        private const uint R_STR_ISP_BLACKWHITE  = FirmwareConstants.R_STR_ISP_BLACKWHITE;
        private const uint R_STR_ISP_SEPIA       = FirmwareConstants.R_STR_ISP_SEPIA;
        private const uint R_STR_ISP_ISO100      = FirmwareConstants.R_STR_ISP_ISO100;
        private const uint R_STR_ISP_ISO200      = FirmwareConstants.R_STR_ISP_ISO200;
        private const uint R_STR_ISP_ISO400      = FirmwareConstants.R_STR_ISP_ISO400;
        private const uint R_STR_ISP_WDR         = FirmwareConstants.R_STR_ISP_WDR;
        private const uint R_STR_ISP_EXPOSURE    = FirmwareConstants.R_STR_ISP_EXPOSURE;

        private static string GetLanguageDisplay(uint value)
        {
            return value switch
            {
                R_STR_LAN_ENGLISH    => "English",
                R_STR_LAN_SCHINESE   => "简体中文",
                R_STR_LAN_TCHINESE   => "繁体中文",
                R_STR_LAN_JAPANESE   => "日本语",
                R_STR_LAN_GERMAN     => "Deutsch",
                R_STR_LAN_FRECH      => "Français",
                R_STR_LAN_RUSSIAN    => "Русский",
                R_STR_LAN_ITALIAN    => "Italiano",
                R_STR_LAN_KOERA      => "한국어",
                R_STR_LAN_TAI        => "ภาษาไทย",
                R_STR_LAN_HEBREW     => "العربية",
                R_STR_LAN_DUTCH      => "Nederlands",
                R_STR_LAN_UKRAINIAN  => "Українська",
                R_STR_LAN_SPANISH    => "Español",
                R_STR_LAN_PORTUGUESE => "Português",
                R_STR_LAN_POLISH     => "Polski",
                R_STR_LAN_CZECH      => "Čeština",
                R_STR_LAN_TURKEY     => "Türkçe",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string GetVideoResolutionDisplay(uint value)
        {
            return value switch
            {
                R_STR_RES_240P        => "240P",
                R_STR_RES_480P        => "480P",
                R_STR_RES_480FHD      => "480FHD",
                R_STR_RES_720P        => "720P",
                R_STR_RES_1024P       => "1024P",
                R_STR_RES_1080P       => "1080P",
                R_STR_RES_1080FHD     => "1080FHD",
                R_STR_RES_1440P       => "1440P",
                R_STR_RES_3024P       => "3024P",
                R_STR_RES_720P_SHORT  => "720P_SHORT",
                R_STR_RES_1080P_SHORT => "1080P_SHORT",
                R_STR_RES_HD          => "HD",
                R_STR_RES_FHD         => "FHD",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string GetPhotoResolutionDisplay(uint value)
        {
            return value switch
            {
                R_STR_RES_48M  => "48M",
                R_STR_RES_20M  => "20M",
                R_STR_RES_12M  => "12M",
                R_STR_RES_10M  => "10M",
                R_STR_RES_8M   => "8M",
                R_STR_RES_5M   => "5M",
                R_STR_RES_3M   => "3M",
                R_STR_RES_2M   => "2M",
                R_STR_RES_1M   => "1M",
                R_STR_RES_QVGA => "QVGA",
                R_STR_RES_VGA  => "VGA",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string GetOnOffDisplay(uint value)
        {
            return value switch
            {
                0             => "关闭",
                R_STR_COM_OFF => "关闭",
                R_STR_COM_ON  => "开启",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string GetLevelDisplay(uint value)
        {
            // LEVEL_0 = R_ID_TYPE_STR + 0x1F, LEVEL_9 = R_ID_TYPE_STR + 0x28
            if (value >= R_STR_COM_LEVEL_0 && value <= R_STR_COM_LEVEL_0 + 9)
            {
                return $"级别 {value - R_STR_COM_LEVEL_0}";
            }
            return $"未知 (0x{value:X8})";
        }

        private static string GetSensitivityDisplay(uint value)
        {
            return value switch
            {
                R_STR_COM_LOW => "低",
                R_STR_COM_MIDDLE => "中",
                R_STR_COM_HIGH => "高",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static List<ConfigOption> BuildLevelOptions()
        {
            var options = new List<ConfigOption>();
            for (uint i = 0; i <= 9; i++)
            {
                options.Add(new ConfigOption(R_STR_COM_LEVEL_0 + i, $"级别 {i}"));
            }
            return options;
        }

        private static List<ConfigOption> BuildSensitivityOptions()
        {
            return new List<ConfigOption>
            {
                new ConfigOption(R_STR_COM_OFF, "关闭"),
                new ConfigOption(R_STR_COM_LOW, "低"),
                new ConfigOption(R_STR_COM_MIDDLE, "中"),
                new ConfigOption(R_STR_COM_HIGH, "高")
            };
        }

        private static List<ConfigOption> BuildOnOffOptions()
        {
            return new List<ConfigOption>
            {
                new ConfigOption(R_STR_COM_OFF, "关闭"),
                new ConfigOption(R_STR_COM_ON, "开启")
            };
        }

        private static List<ConfigOption> BuildVideoResolutionOptions()
        {
            return new List<ConfigOption>
            {
                new ConfigOption(R_STR_RES_240P, "240P"),
                new ConfigOption(R_STR_RES_480P, "480P"),
                new ConfigOption(R_STR_RES_480FHD, "480FHD"),
                new ConfigOption(R_STR_RES_720P, "720P"),
                new ConfigOption(R_STR_RES_1024P, "1024P"),
                new ConfigOption(R_STR_RES_1080P, "1080P"),
                new ConfigOption(R_STR_RES_1080FHD, "1080FHD"),
                new ConfigOption(R_STR_RES_1440P, "1440P"),
                new ConfigOption(R_STR_RES_3024P, "3024P"),
                new ConfigOption(R_STR_RES_720P_SHORT, "720P_SHORT"),
                new ConfigOption(R_STR_RES_1080P_SHORT, "1080P_SHORT"),
                new ConfigOption(R_STR_RES_QVGA, "QVGA"),
                new ConfigOption(R_STR_RES_VGA, "VGA"),
                new ConfigOption(R_STR_RES_HD, "HD"),
                new ConfigOption(R_STR_RES_FHD, "FHD")
            };
        }

        private static List<ConfigOption> BuildPhotoResolutionOptions()
        {
            return new List<ConfigOption>
            {
                new ConfigOption(R_STR_RES_48M, "48M"),
                new ConfigOption(R_STR_RES_40M, "40M"),
                new ConfigOption(R_STR_RES_24M, "24M"),
                new ConfigOption(R_STR_RES_20M, "20M"),
                new ConfigOption(R_STR_RES_18M, "18M"),
                new ConfigOption(R_STR_RES_16M, "16M"),
                new ConfigOption(R_STR_RES_12M, "12M"),
                new ConfigOption(R_STR_RES_10M, "10M"),
                new ConfigOption(R_STR_RES_8M, "8M"),
                new ConfigOption(R_STR_RES_5M, "5M"),
                new ConfigOption(R_STR_RES_3M, "3M"),
                new ConfigOption(R_STR_RES_2M, "2M"),
                new ConfigOption(R_STR_RES_1M, "1M"),
                new ConfigOption(R_STR_RES_QVGA, "QVGA"),
                new ConfigOption(R_STR_RES_VGA, "VGA")
            };
        }

        private static List<ConfigOption> BuildResolutionOptions(int index = -1)
        {
            if (index == (int)ConfigId.CONFIG_ID_PRESLUTION)
            {
                return BuildPhotoResolutionOptions();
            }

            if (index == (int)ConfigId.CONFIG_ID_RESOLUTION || index == (int)ConfigId.CONFIG_ID_VIDEO_RESOLUTION)
            {
                return BuildVideoResolutionOptions();
            }

            var options = new List<ConfigOption>(BuildVideoResolutionOptions());
            options.AddRange(BuildPhotoResolutionOptions().Where(o => !options.Any(existing => existing.Value == o.Value)));
            return options;
        }

        private static List<ConfigOption> BuildLoopTimeOptions()
        {
            return new List<ConfigOption>
            {
                new ConfigOption(R_STR_TIM_1MIN, "1分钟"),
                new ConfigOption(R_STR_TIM_2MIN, "2分钟"),
                new ConfigOption(R_STR_TIM_3MIN, "3分钟"),
                new ConfigOption(R_STR_TIM_5MIN, "5分钟"),
                new ConfigOption(R_STR_TIM_10MIN, "10分钟"),
                new ConfigOption(R_STR_TIM_2SEC, "2秒"),
                new ConfigOption(R_STR_TIM_3SEC, "3秒"),
                new ConfigOption(R_STR_TIM_5SEC, "5秒"),
                new ConfigOption(R_STR_TIM_10SEC, "10秒"),
                new ConfigOption(R_STR_TIM_30SEC, "30秒")
            };
        }

        private static List<ConfigOption> BuildLanguageOptions()
        {
            return new List<ConfigOption>
            {
                new ConfigOption(R_STR_LAN_ENGLISH, "English"),
                new ConfigOption(R_STR_LAN_SCHINESE, "简体中文"),
                new ConfigOption(R_STR_LAN_TCHINESE, "繁体中文"),
                new ConfigOption(R_STR_LAN_JAPANESE, "日本语"),
                new ConfigOption(R_STR_LAN_GERMAN, "Deutsch"),
                new ConfigOption(R_STR_LAN_FRECH, "Français"),
                new ConfigOption(R_STR_LAN_RUSSIAN, "Русский"),
                new ConfigOption(R_STR_LAN_ITALIAN, "Italiano"),
                new ConfigOption(R_STR_LAN_KOERA, "한국어"),
                new ConfigOption(R_STR_LAN_TAI, "ภาษาไทย"),
                new ConfigOption(R_STR_LAN_HEBREW, "العربية"),
                new ConfigOption(R_STR_LAN_DUTCH, "Nederlands"),
                new ConfigOption(R_STR_LAN_UKRAINIAN, "Українська"),
                new ConfigOption(R_STR_LAN_SPANISH, "Español"),
                new ConfigOption(R_STR_LAN_PORTUGUESE, "Português"),
                new ConfigOption(R_STR_LAN_POLISH, "Polski"),
                new ConfigOption(R_STR_LAN_CZECH, "Čeština"),
                new ConfigOption(R_STR_LAN_TURKEY, "Türkçe")
            };
        }

        private static List<ConfigOption> BuildAutoOffOptions()
        {
            return new List<ConfigOption>
            {
                new ConfigOption(R_STR_COM_OFF, "关闭"),
                new ConfigOption(R_STR_COM_ON, "开启"),
                new ConfigOption(R_STR_TIM_1MIN, "1分钟"),
                new ConfigOption(R_STR_TIM_2MIN, "2分钟"),
                new ConfigOption(R_STR_TIM_3MIN, "3分钟"),
                new ConfigOption(R_STR_TIM_5MIN, "5分钟"),
                new ConfigOption(R_STR_TIM_10MIN, "10分钟"),
                new ConfigOption(R_STR_TIM_2SEC, "2秒"),
                new ConfigOption(R_STR_TIM_3SEC, "3秒"),
                new ConfigOption(R_STR_TIM_5SEC, "5秒"),
                new ConfigOption(R_STR_TIM_10SEC, "10秒"),
                new ConfigOption(R_STR_TIM_30SEC, "30秒")
            };
        }

        private static List<ConfigOption> BuildScreenSaveOptions()
        {
            return new List<ConfigOption>
            {
                new ConfigOption(R_STR_COM_OFF, "关闭"),
                new ConfigOption(R_STR_COM_ON, "开启"),
                new ConfigOption(R_STR_TIM_1MIN, "1分钟"),
                new ConfigOption(R_STR_TIM_2MIN, "2分钟"),
                new ConfigOption(R_STR_TIM_3MIN, "3分钟"),
                new ConfigOption(R_STR_TIM_5MIN, "5分钟"),
                new ConfigOption(R_STR_TIM_10MIN, "10分钟")
            };
        }

        private static List<ConfigOption> BuildFrequencyOptions()
        {
            return new List<ConfigOption>
            {
                new ConfigOption(R_STR_COM_50HZ, "50Hz"),
                new ConfigOption(R_STR_COM_60HZ, "60Hz")
            };
        }

        private static List<ConfigOption> BuildEvOptions()
        {
            return new List<ConfigOption>
            {
                new ConfigOption(R_STR_COM_P4_0, "+4.0"),
                new ConfigOption(R_STR_COM_P3_0, "+3.0"),
                new ConfigOption(R_STR_COM_P2_0, "+2.0"),
                new ConfigOption(R_STR_COM_P1_0, "+1.0"),
                new ConfigOption(R_STR_COM_P0_0, "0.0"),
                new ConfigOption(R_STR_COM_N1_0, "-1.0"),
                new ConfigOption(R_STR_COM_N2_0, "-2.0")
            };
        }

        private static List<ConfigOption> BuildWbalanceOptions()
        {
            return new List<ConfigOption>
            {
                new ConfigOption(R_STR_ISP_AUTO, "自动"),
                new ConfigOption(R_STR_ISP_SUNLIGHT, "晴天"),
                new ConfigOption(R_STR_ISP_CLOUDY, "阴天"),
                new ConfigOption(R_STR_ISP_TUNGSTEN, "办公室"),
                new ConfigOption(R_STR_ISP_FLUORESCENT, "荧光灯"),
                //new ConfigOption(R_STR_ISP_WHITEBL, "白炽灯"),
                //new ConfigOption(R_STR_ISP_SOFT, "柔和"),
                //new ConfigOption(R_STR_ISP_STRONG, "强烈"),
                //new ConfigOption(R_STR_ISP_BLACKWHITE, "黑白"),
                //new ConfigOption(R_STR_ISP_SEPIA, "复古"),
                //new ConfigOption(R_STR_ISP_ISO100, "ISO100"),
                //new ConfigOption(R_STR_ISP_ISO200, "ISO200"),
                //new ConfigOption(R_STR_ISP_ISO400, "ISO400"),
                //new ConfigOption(R_STR_ISP_WDR, "WDR"),
                //new ConfigOption(R_STR_ISP_EXPOSURE, "曝光"),
                //new ConfigOption(R_STR_ISP_ISO, "ISO"),
                //new ConfigOption(R_STR_ISP_ANTISHANK, "防抖")
            };
        }

        private static string GetAutoOffDisplay(uint value)
        {
            return value switch
            {
                var v when v == R_STR_COM_OFF   => "关闭",
                var v when v == R_STR_COM_ON    => "开启",
                var v when v == R_STR_TIM_1MIN  => "1 分钟",
                var v when v == R_STR_TIM_2MIN  => "2 分钟",
                var v when v == R_STR_TIM_3MIN  => "3 分钟",
                var v when v == R_STR_TIM_5MIN  => "5 分钟",
                var v when v == R_STR_TIM_10MIN => "10 分钟",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string GetScreenSaveDisplay(uint value)
        {
            return value switch
            {
                var v when v == R_STR_COM_OFF   => "关闭",
                var v when v == R_STR_COM_ON    => "开启",
                var v when v == R_STR_TIM_1MIN  => "1 分钟",
                var v when v == R_STR_TIM_2MIN  => "2 分钟",
                var v when v == R_STR_TIM_3MIN  => "3 分钟",
                var v when v == R_STR_TIM_5MIN  => "5 分钟",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string GetEvDisplay(uint value)
        {
            return value switch
            {
                var v when v == R_STR_COM_P4_0 => "+4.0",
                var v when v == R_STR_COM_P3_0 => "+3.0",
                var v when v == R_STR_COM_P2_0 => "+2.0",
                var v when v == R_STR_COM_P1_0 => "+1.0",
                var v when v == R_STR_COM_P0_0 => "0.0",
                var v when v == R_STR_COM_N1_0 => "-1.0",
                var v when v == R_STR_COM_N2_0 => "-2.0",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string GetWbalanceDisplay(uint value)
        {
            return value switch
            {
                var v when v == R_STR_ISP_AUTO        => "自动",
                var v when v == R_STR_ISP_SUNLIGHT    => "晴天",
                var v when v == R_STR_ISP_CLOUDY      => "阴天",
                var v when v == R_STR_ISP_TUNGSTEN    => "办公室",
                var v when v == R_STR_ISP_FLUORESCENT => "荧光灯",
                var v when v == R_STR_ISP_WHITEBL     => "白炽灯",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string GetLoopTimeDisplay(uint value)
        {
            return value switch
            {
                var v when v == R_STR_TIM_1MIN  => "1 分钟",
                var v when v == R_STR_TIM_2MIN  => "2 分钟",
                var v when v == R_STR_TIM_3MIN  => "3 分钟",
                var v when v == R_STR_TIM_5MIN  => "5 分钟",
                var v when v == R_STR_TIM_10MIN => "10 分钟",
                _ => $"未知 (0x{value:X8})"
            };
        }

        private static string GetFrequencyDisplay(uint value)
        {
            return value switch
            {
                var v when v == R_STR_COM_50HZ => "50Hz",
                var v when v == R_STR_COM_60HZ => "60Hz",
                _ => $"未知 (0x{value:X8})"
            };
        }

        /// <summary>
        /// 动态检测实际使用的配置项数量
        /// 直接从配置数据中检测，不依赖映射文件，确保数据准确
        /// </summary>
        /// <param name="flags">配置项数组</param>
        /// <returns>实际使用的配置项数量</returns>
        internal static int DetectActiveConfigCount(uint[] flags, ProjectType projectType = ProjectType.Unknown)
        {
            if (flags == null || flags.Length == 0)
                return 0;

            int lastActiveIndex = 0;
            for (int i = 0; i < flags.Length; i++)
            {
                if (flags[i] != 0 && flags[i] != 0xFFFFFFFF)
                {
                    lastActiveIndex = i + 1;
                }
            }

            int mappingBasedCount = 0;
            if (projectType != ProjectType.Unknown)
            {
                var mapping = ProjectConfigMappingDatabase.GetMapping(projectType);
                if (mapping != null)
                {
                    mappingBasedCount = mapping.ConfigItemCount;
                }
            }

            int activeCount = Math.Max(lastActiveIndex, mappingBasedCount);
            activeCount = Math.Min(activeCount, flags.Length);

            System.Diagnostics.Debug.WriteLine($"[ConfigParser] DetectActiveConfigCount: lastActiveIndex={lastActiveIndex}, mappingBasedCount={mappingBasedCount}, result={activeCount}");

            return activeCount;
        }

        /// <summary>
        /// 根据检测到的配置项数量推断配置版本
        /// </summary>
        /// <param name="activeCount">实际使用的配置项数量</param>
        /// <returns>配置版本字符串</returns>
        private static string InferConfigVersion(int activeCount)
        {
            if (activeCount == 0)
                return "Blank";

            if (activeCount <= 30)
                return "V1.0 (Legacy)";
            else if (activeCount <= 35)
                return "V1.1";
            else if (activeCount <= 40)
                return "V1.2";
            else if (activeCount <= 55)
                return $"V1.3 (Current, {activeCount} items)";
            else if (activeCount <= 100)
                return $"V2.0 (Extended, {activeCount} items)";
            else
                return $"V? ({activeCount} items)";
        }

        /// <summary>
        /// 构建配置项列表（支持动态检测）
        /// 优先使用直接解析方法，不依赖映射文件，确保数据准确
        /// 当项目类型已知时，使用项目映射优化类型推断
        /// </summary>
        public static List<FirmwareConfigItem> BuildConfigItemList(FirmwareConfigData configData)
        {
            // 如果有项目映射且项目类型已知，使用映射版本
            if (configData.Mapping != null && configData.ProjectType != ProjectType.Unknown)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigParser] BuildConfigItemList: using project mapping for {configData.ProjectType}");
                return BuildConfigItemListWithMapping(configData, configData.Mapping);
            }

            // 尝试获取项目映射（即使项目类型已知但没有映射，尝试从数据库获取）
            ProjectConfigMapping? fallbackMapping = null;
            if (configData.ProjectType != ProjectType.Unknown)
            {
                fallbackMapping = ProjectConfigMappingDatabase.GetMapping(configData.ProjectType);
                if (fallbackMapping != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigParser] BuildConfigItemList: using fallback mapping for {configData.ProjectType}");
                }
            }

            // 使用直接解析方法，从配置数据中直接推断类型和显示信息
            // 如果有项目映射，传递给直接解析方法进行类型推断优化
            System.Diagnostics.Debug.WriteLine($"[ConfigParser] BuildConfigItemList: using direct parsing (projectType={configData.ProjectType}, fallbackMapping={fallbackMapping?.ProjectName ?? "null"})");
            return BuildConfigItemListDirect(configData, fallbackMapping);
        }

        /// <summary>
        /// 直接解析配置项列表（不依赖映射文件）
        /// 根据 UniversalValueDecoder 直接从值推断类型和显示文本
        /// 支持项目映射优化：当提供项目映射时，优先使用映射进行类型推断
        /// </summary>
        /// <param name="configData">配置数据</param>
        /// <param name="mapping">项目配置映射（可选，用于优化类型推断）</param>
        public static List<FirmwareConfigItem> BuildConfigItemListDirect(FirmwareConfigData configData, ProjectConfigMapping? mapping = null)
        {
            if (configData == null)
                throw new ArgumentNullException(nameof(configData));
            if (configData.Flags == null)
                throw new ArgumentException("Flags array cannot be null", nameof(configData));

            var items = new List<FirmwareConfigItem>();

            System.Diagnostics.Debug.WriteLine($"[ConfigParser] BuildConfigItemListDirect: activeCount={configData.ActiveConfigCount}, mapping={mapping?.ProjectName ?? "null"}");

            int maxCount = Math.Min(configData.ActiveConfigCount, CONFIG_FLAGS_COUNT);
            maxCount = Math.Min(maxCount, configData.Flags.Length);

            for (int index = 0; index < maxCount; index++)
            {
                uint value = configData.Flags[index];

                // 使用 UniversalValueDecoder 推断值类型
                var decodeResult = UniversalValueDecoder.Decode(value);

                // 记录调试信息：索引-值-解码类型-显示文本
                System.Diagnostics.Debug.WriteLine($"[ConfigParser] Direct parse: index={index}, value=0x{value:X8}, type={decodeResult.InferredType}, display={decodeResult.DisplayText}, confidence={decodeResult.Confidence:F2}");

                // 获取配置项名称（优先从映射获取）
                string configName = mapping?.GetConfigNameByIndex(index) ?? $"CONFIG_ID_{index}";

                ConfigItemType effectiveType = decodeResult.InferredType;

                if (mapping != null && !string.IsNullOrEmpty(configName) && configName != $"CONFIG_ID_{index}")
                {
                    var descriptor = ConfigItemRegistry.GetDescriptor(configName);
                    if (descriptor != null)
                    {
                        effectiveType = descriptor.Type;
                        System.Diagnostics.Debug.WriteLine($"[ConfigParser] Mapping-based type inference: index={index}, name={configName}, type={effectiveType}");
                    }
                }

                if (effectiveType == decodeResult.InferredType)
                {
                    if (IsTimeConfigIndex(index))
                    {
                        effectiveType = ConfigItemType.Time;
                        System.Diagnostics.Debug.WriteLine($"[ConfigParser] Time config detected: index={index}, overriding type to Time");
                    }
                    else if (IsOnOffConfigIndex(index))
                    {
                        effectiveType = ConfigItemType.OnOff;
                        System.Diagnostics.Debug.WriteLine($"[ConfigParser] OnOff config detected: index={index}, overriding type to OnOff");
                    }
                    else if (IsSensitivityConfigIndex(index))
                    {
                        effectiveType = ConfigItemType.Sensitivity;
                        System.Diagnostics.Debug.WriteLine($"[ConfigParser] Sensitivity config detected: index={index}, overriding type to Sensitivity");
                    }
                    else if (IsLevelConfigIndex(index))
                    {
                        effectiveType = ConfigItemType.Level;
                        System.Diagnostics.Debug.WriteLine($"[ConfigParser] Level config detected: index={index}, overriding type to Level");
                    }
                    else if (IsAutoOffTimeConfigIndex(index))
                    {
                        effectiveType = ConfigItemType.AutoOffTime;
                        System.Diagnostics.Debug.WriteLine($"[ConfigParser] AutoOffTime config detected: index={index}, overriding type to AutoOffTime");
                    }
                }

                // 获取该类型的选项列表和格式化函数（传递索引以支持时间选项的精确生成）
                // 优先从XML配置文件获取选项，否则回退到类型推断
                var inferredOptions = GetOptionsFromXmlOrType(index, configName, effectiveType);
                var inferredFormatter = ConfigDisplayFormatters.GetFormatter(effectiveType);

                // 显示名称：优先使用映射中的配置名称，否则使用枚举名称或默认格式
                string displayName = GetDisplayNameWithMapping(index, configName, effectiveType);
                string category = GetCategoryWithMapping(configName, effectiveType);

                uint displayValue = value;
                if (value == 0)
                {
                    switch (effectiveType)
                    {
                        case ConfigItemType.OnOff:
                        case ConfigItemType.AutoOffTime:
                        case ConfigItemType.ScreenSaveTime:
                        case ConfigItemType.Sensitivity:
                            displayValue = FirmwareConstants.R_STR_COM_OFF;
                            break;
                        case ConfigItemType.Level:
                            displayValue = FirmwareConstants.R_STR_COM_LEVEL_0;
                            break;
                    }
                }

                string displayText = inferredFormatter(displayValue);

                var finalOptions = new List<ConfigOption>(inferredOptions);
                if (!finalOptions.Any(o => o.Value == value))
                {
                    finalOptions.Add(new ConfigOption(value, displayText));
                }
                if (displayValue != value && !finalOptions.Any(o => o.Value == displayValue))
                {
                    finalOptions.Add(new ConfigOption(displayValue, inferredFormatter(displayValue)));
                }

                items.Add(new FirmwareConfigItem
                {
                    Id = (ConfigId)index,
                    Name = displayName,
                    Value = displayValue,
                    ValueDisplay = displayText,
                    Category = category,
                    Options = finalOptions,
                    Enabled = true
                });
            }

            System.Diagnostics.Debug.WriteLine($"[ConfigParser] BuildConfigItemListDirect: built {items.Count} items");
            return items;
        }

        /// <summary>
        /// 根据索引、配置名称和类型获取显示名称
        /// 优先从注册表获取中文显示名，保持与XML路径一致的行为
        /// </summary>
        private static string GetDisplayNameWithMapping(int index, string configName, ConfigItemType type)
        {
            // 优先从注册表获取中文显示名
            var metadata = ConfigItemRegistry.GetMetadata(configName);
            if (metadata != null && !string.IsNullOrEmpty(metadata.DisplayName)
                && metadata.DisplayName != configName)
            {
                return metadata.DisplayName;
            }

            // 如果配置名称不是默认格式（即来自映射），直接使用
            if (configName != $"CONFIG_ID_{index}")
            {
                return configName;
            }

            // 回退到枚举名称
            string enumName = Enum.GetName(typeof(ConfigId), index);
            if (!string.IsNullOrEmpty(enumName))
            {
                // 再次尝试从注册表获取枚举名对应的中文显示名
                var enumMetadata = ConfigItemRegistry.GetMetadata(enumName);
                if (enumMetadata != null && !string.IsNullOrEmpty(enumMetadata.DisplayName)
                    && enumMetadata.DisplayName != enumName)
                {
                    return enumMetadata.DisplayName;
                }
                return enumName;
            }

            return configName;
        }

        /// <summary>
        /// 根据配置名称和类型获取分类
        /// 优先使用注册表中的分类定义
        /// </summary>
        private static string GetCategoryWithMapping(string configName, ConfigItemType type)
        {
            // 尝试从注册表获取分类
            var descriptor = ConfigItemRegistry.GetDescriptor(configName);
            if (descriptor != null && !string.IsNullOrEmpty(descriptor.Category))
            {
                return descriptor.Category;
            }

            // 回退到基于类型的分类
            return GetTypeBasedCategory(type);
        }

        /// <summary>
        /// 判断索引是否为时间配置项（年/月/日/星期/时/分/秒）
        /// </summary>
        private static bool IsTimeConfigIndex(int index)
        {
            return index is 
                (int)ConfigId.CONFIG_ID_YEAR or 
                (int)ConfigId.CONFIG_ID_MONTH or 
                (int)ConfigId.CONFIG_ID_MDAY or 
                (int)ConfigId.CONFIG_ID_WDAY or 
                (int)ConfigId.CONFIG_ID_HOUR or 
                (int)ConfigId.CONFIG_ID_MIN or 
                (int)ConfigId.CONFIG_ID_SEC;
        }

        /// <summary>
        /// 判断索引是否为开关配置项（即使值为0也应该识别为OnOff类型）
        /// 这些配置项在固件中可能没有设置初始值（值为0），但实际是开关类型
        /// </summary>
        private static bool IsOnOffConfigIndex(int index)
        {
            return index is 
                (int)ConfigId.CONFIG_ID_FORMAT or 
                (int)ConfigId.CONFIG_ID_DEFUALT or 
                (int)ConfigId.CONFIG_ID_VIDEORECEFFECT or
                (int)ConfigId.CONFIG_ID_REINIT;
        }

        /// <summary>
        /// 判断索引是否为灵敏度配置项（使用高/中/低选项）
        /// 这些配置项的值范围是 R_STR_COM_LOW/MIDDLE/HIGH (0x1A/0x1B/0x1C)
        /// </summary>
        private static bool IsSensitivityConfigIndex(int index)
        {
            return index is 
                (int)ConfigId.CONFIG_ID_GSENSOR;
        }

        /// <summary>
        /// 判断索引是否为级别配置项（使用级别0~9选项）
        /// 这些配置项的值范围是 R_STR_COM_LEVEL_0~9 (0x1F~0x28)
        /// </summary>
        private static bool IsLevelConfigIndex(int index)
        {
            return index is 
                (int)ConfigId.CONFIG_ID_LCD_BRIGHT;
        }

        /// <summary>
        /// 判断索引是否为自动关闭时间配置项（使用关闭+时间选项）
        /// 这些配置项的值范围是 R_STR_COM_OFF/ON/TIM_*
        /// </summary>
        private static bool IsAutoOffTimeConfigIndex(int index)
        {
            return index is 
                (int)ConfigId.CONFIG_ID_PFASTVIEW;
        }

        /// <summary>
        /// 根据值类型和索引获取显示名称
        /// 使用CONFIG_ID_*枚举名称形式显示
        /// </summary>
        private static string GetTypeBasedDisplayName(int index, ConfigItemType type)
        {
            string enumName = Enum.GetName(typeof(ConfigId), index);
            if (!string.IsNullOrEmpty(enumName))
            {
                return enumName;
            }
            return $"CONFIG_ID_{index}";
        }

        /// <summary>
        /// 根据值类型获取分类（不依赖索引，纯值驱动）
        /// </summary>
        private static string GetTypeBasedCategory(ConfigItemType type)
        {
            return type switch
            {
                ConfigItemType.Language => "系统设置",
                ConfigItemType.Resolution => "影像设置",
                ConfigItemType.OnOff => "系统设置",
                ConfigItemType.Level => "系统设置",
                ConfigItemType.Sensitivity => "系统设置",
                ConfigItemType.ExposureValue => "影像设置",
                ConfigItemType.WhiteBalance => "影像设置",
                ConfigItemType.Frequency => "系统设置",
                ConfigItemType.AutoOffTime => "系统设置",
                ConfigItemType.ScreenSaveTime => "显示设置",
                ConfigItemType.LoopTime => "录像设置",
                ConfigItemType.Time => "时间设置",
                ConfigItemType.WeekDay => "时间设置",
                ConfigItemType.Numeric => "数值设置",
                ConfigItemType.RawHex => "其他",
                _ => "其他"
            };
        }

        /// <summary>
        /// 使用项目映射构建配置项列表
        /// </summary>
        public static List<FirmwareConfigItem> BuildConfigItemListWithMapping(FirmwareConfigData configData, ProjectConfigMapping mapping)
        {
            var items = new List<FirmwareConfigItem>();

            System.Diagnostics.Debug.WriteLine($"[ConfigParser] BuildConfigItemListWithMapping: project={mapping.ProjectName}, configCount={mapping.ConfigItemCount}");

            // 遍历映射中的每个配置项
            foreach (var kvp in mapping.IndexToConfigName)
            {
                int index = kvp.Key;
                string configName = kvp.Value;

                // 边界检查：确保索引在有效范围内（0 到 126）
                if (index < 0 || index >= CONFIG_FLAGS_COUNT)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigParser] Config index {index} out of range (max: {CONFIG_FLAGS_COUNT - 1})");
                    continue;
                }

                uint value = configData.Flags[index];

                // 获取元数据覆盖（如果有）
                mapping.MetadataOverrides.TryGetValue(configName, out var metadataOverride);

                var item = BuildConfigItemFromMapping(configName, index, value, metadataOverride);
                if (item != null)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        /// <summary>
        /// 根据映射信息构建单个配置项（优化版本 - 使用 ConfigItemDescriptor）
        /// </summary>
        private static FirmwareConfigItem? BuildConfigItemFromMapping(string configName, int index, uint value, ConfigItemMetadataOverride? metadataOverride = null)
        {
            // 优化 1: 直接从注册表获取描述符（包含元数据、选项列表、格式化函数）
            var descriptor = ConfigItemRegistry.GetDescriptor(configName);
            
            if (descriptor == null)
            {
                // 配置项未注册，创建默认描述符
                descriptor = new ConfigItemDescriptor
                {
                    Id = ConfigId.CONFIG_ID_MAX,
                    ConfigName = configName,
                    DisplayName = configName,
                    Category = "未知配置项",
                    Type = ConfigItemType.RawHex,
                    Description = "未知配置项",
                    Options = new List<ConfigOption>(),
                    DisplayFormatter = (v) => $"0x{v:X8}"
                };
            }

            // 优化 2: 应用元数据覆盖（如果有）
            if (metadataOverride != null)
            {
                descriptor = descriptor.ApplyOverride(metadataOverride);
            }

            // 优化 3: 值归一化 - 将值为0的配置项转换为R_STR_COM_OFF（关闭）
            // 适用于所有具有"关闭"选项的类型：OnOff、AutoOffTime、ScreenSaveTime
            uint displayValue = value;
            if (value == 0 && (descriptor.Type == ConfigItemType.OnOff || 
                               descriptor.Type == ConfigItemType.AutoOffTime || 
                               descriptor.Type == ConfigItemType.ScreenSaveTime))
            {
                displayValue = FirmwareConstants.R_STR_COM_OFF;
            }

            // 优化 4: 使用描述符的格式化函数和选项列表
            string displayText = descriptor.DisplayFormatter(value);
            
            // 优化 5: 对于 RawHex 类型，尝试使用 UniversalValueDecoder 推断
            if (descriptor.Type == ConfigItemType.RawHex)
            {
                var decodeResult = UniversalValueDecoder.Decode(value);
                if (decodeResult.Confidence > 0.5)
                {
                    var inferredOptions = new List<ConfigOption>(ConfigOptionsCache.GetOptions(decodeResult.InferredType));
                    var inferredFormatter = ConfigDisplayFormatters.GetFormatter(decodeResult.InferredType);
                    string inferredDisplayText = inferredFormatter(value);
                    
                    if (!inferredOptions.Any(o => o.Value == value))
                    {
                        inferredOptions.Add(new ConfigOption(value, inferredDisplayText));
                    }
                    
                    return new FirmwareConfigItem
                    {
                        Id = descriptor.Id,
                        Name = descriptor.DisplayName,
                        Value = value,
                        ValueDisplay = inferredDisplayText,
                        Category = descriptor.Category,
                        Options = inferredOptions,
                        Enabled = true
                    };
                }
            }

            var finalOptions = descriptor.Options != null ? new List<ConfigOption>(descriptor.Options) : new List<ConfigOption>();
            if (!finalOptions.Any(o => o.Value == displayValue))
            {
                finalOptions.Add(new ConfigOption(displayValue, displayText));
            }

            return new FirmwareConfigItem
            {
                Id = descriptor.Id,
                Name = descriptor.DisplayName,
                Value = displayValue,
                ValueDisplay = displayText,
                Category = descriptor.Category,
                Options = finalOptions,
                Enabled = true
            };
        }

        /// <summary>
        /// 根据配置项类型构建配置项
        /// </summary>
        private static FirmwareConfigItem BuildConfigItemByType(ConfigId configId, ConfigItemMetadata metadata, uint value)
        {
            var options = new List<ConfigOption>(BuildOptionsByType(metadata.Type, (int)configId));
            var display = GetDisplayByType(value, metadata.Type, (int)configId);

            if (!options.Any(o => o.Value == value))
            {
                options.Add(new ConfigOption(value, display));
            }

            return new FirmwareConfigItem
            {
                Id = configId,
                Name = metadata.DisplayName,
                Value = value,
                ValueDisplay = display,
                Category = metadata.Category,
                Options = options,
                Enabled = true
            };
        }

        // 缓存选项列表，避免重复创建（线程安全）
        // 使用 Tuple<ConfigItemType, int> 作为键，支持不同索引的时间选项缓存
        private static readonly ConcurrentDictionary<Tuple<ConfigItemType, int>, List<ConfigOption>> _optionsCache = new();

        /// <summary>
        /// 根据配置项类型生成选项列表（使用缓存，线程安全）
        /// </summary>
        private static List<ConfigOption> GetCachedOptionsByType(ConfigItemType type, int index = -1)
        {
            var key = Tuple.Create(type, index);
            return _optionsCache.GetOrAdd(key, k => BuildOptionsByType(type, index));
        }

        private static List<ConfigXmlParsedItem>? _xmlConfigCache;
        private static string? _xmlConfigCachePath;

        /// <summary>
        /// 优先从XML配置文件获取选项，否则回退到类型推断
        /// </summary>
        private static List<ConfigOption> GetOptionsFromXmlOrType(int index, string configName, ConfigItemType effectiveType)
        {
            var xmlOptions = GetOptionsFromXml(index, configName);
            if (xmlOptions != null && xmlOptions.Count > 0)
            {
                return xmlOptions;
            }

            return BuildOptionsByType(effectiveType, index);
        }

        /// <summary>
        /// 从XML配置文件获取选项
        /// </summary>
        private static List<ConfigOption>? GetOptionsFromXml(int index, string configName)
        {
            try
            {
                string xmlPath = GetXmlConfigPath();
                if (string.IsNullOrEmpty(xmlPath))
                    return null;

                if (_xmlConfigCache == null || _xmlConfigCachePath != xmlPath)
                {
                    _xmlConfigCache = ConfigXmlParser.ParseFromFile(xmlPath);
                    _xmlConfigCachePath = xmlPath;
                    System.Diagnostics.Debug.WriteLine($"[ConfigParser] Loaded XML config: {xmlPath}, {_xmlConfigCache.Count} items");
                }

                var xmlItem = _xmlConfigCache.FirstOrDefault(
                    x => x.Index == index || x.ConfigName == configName);

                if (xmlItem != null && xmlItem.Options != null && xmlItem.Options.Count > 0)
                {
                    return xmlItem.Options;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigParser] Error reading XML options: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 获取XML配置文件路径
        /// </summary>
        private static string? GetXmlConfigPath()
        {
            string configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            if (!Directory.Exists(configDir))
                return null;

            string[] xmlFiles = Directory.GetFiles(configDir, "*_Config.xml");
            if (xmlFiles.Length > 0)
                return xmlFiles[0];

            string[] altXmlFiles = Directory.GetFiles(configDir, "*.xml");
            if (altXmlFiles.Length > 0)
                return altXmlFiles[0];

            return null;
        }

        /// <summary>
        /// 根据配置项类型生成选项列表
        /// </summary>
        private static List<ConfigOption> BuildOptionsByType(ConfigItemType type, int index = -1)
        {
            return type switch
            {
                ConfigItemType.OnOff => BuildOnOffOptions(),
                ConfigItemType.Language => BuildLanguageOptions(),
                ConfigItemType.Time => BuildTimeOptions(index),
                ConfigItemType.Numeric => BuildNumericOptions(),
                ConfigItemType.Resolution => BuildResolutionOptions(index),
                ConfigItemType.ExposureValue => BuildEvOptions(),
                ConfigItemType.WhiteBalance => BuildWbalanceOptions(),
                ConfigItemType.Frequency => BuildFrequencyOptions(),
                ConfigItemType.AutoOffTime => BuildAutoOffOptions(),
                ConfigItemType.ScreenSaveTime => BuildScreenSaveOptions(),
                ConfigItemType.LoopTime => BuildLoopTimeOptions(),
                ConfigItemType.Level => BuildLevelOptions(),
                ConfigItemType.Sensitivity => BuildSensitivityOptions(),
                ConfigItemType.WeekDay => BuildWeekDayOptions(),
                ConfigItemType.RawHex => new List<ConfigOption>(),
                _ => new List<ConfigOption>()
            };
        }

        /// <summary>
        /// 根据配置项类型获取显示文本
        /// </summary>
        private static string GetDisplayByType(uint value, ConfigItemType type, int index = -1)
        {
            return type switch
            {
                ConfigItemType.OnOff => GetOnOffDisplay(value),
                ConfigItemType.Language => GetLanguageDisplay(value),
                ConfigItemType.Time => GetTimeDisplay(value),
                ConfigItemType.Numeric => GetNumericDisplay(value),
                ConfigItemType.Resolution => GetResolutionDisplay(value, index),
                ConfigItemType.ExposureValue => GetEvDisplay(value),
                ConfigItemType.WhiteBalance => GetWbalanceDisplay(value),
                ConfigItemType.Frequency => GetFrequencyDisplay(value),
                ConfigItemType.AutoOffTime => GetAutoOffDisplay(value),
                ConfigItemType.ScreenSaveTime => GetScreenSaveDisplay(value),
                ConfigItemType.LoopTime => GetLoopTimeDisplay(value),
                ConfigItemType.Level => GetLevelDisplay(value),
                ConfigItemType.Sensitivity => GetSensitivityDisplay(value),
                ConfigItemType.WeekDay => GetWeekDayDisplay(value),
                ConfigItemType.RawHex => $"0x{value:X8}",
                _ => $"0x{value:X8}"
            };
        }

        private static string GetResolutionDisplay(uint value, int index)
        {
            if (index == (int)ConfigId.CONFIG_ID_PRESLUTION)
            {
                return GetPhotoResolutionDisplay(value);
            }

            return GetVideoResolutionDisplay(value);
        }

        /// <summary>
        /// 生成时间选项（根据配置项索引生成对应的时间范围）
        /// </summary>
        private static List<ConfigOption> BuildTimeOptions(int index)
        {
            return index switch
            {
                (int)ConfigId.CONFIG_ID_YEAR => BuildYearOptions(),
                (int)ConfigId.CONFIG_ID_MONTH => BuildMonthOptions(),
                (int)ConfigId.CONFIG_ID_MDAY => BuildDayOptions(),
                (int)ConfigId.CONFIG_ID_WDAY => BuildWeekDayOptions(),
                (int)ConfigId.CONFIG_ID_HOUR => BuildHourOptions(),
                (int)ConfigId.CONFIG_ID_MIN => BuildMinOptions(),
                (int)ConfigId.CONFIG_ID_SEC => BuildSecOptions(),
                _ => BuildNumericOptions()
            };
        }

        /// <summary>
        /// 生成星期选项（0-6）
        /// </summary>
        private static List<ConfigOption> BuildWeekDayOptions()
        {
            return new List<ConfigOption>
            {
                new ConfigOption(0, "星期日"),
                new ConfigOption(1, "星期一"),
                new ConfigOption(2, "星期二"),
                new ConfigOption(3, "星期三"),
                new ConfigOption(4, "星期四"),
                new ConfigOption(5, "星期五"),
                new ConfigOption(6, "星期六")
            };
        }

        /// <summary>
        /// 生成年份选项（2020-2035）
        /// </summary>
        private static List<ConfigOption> BuildYearOptions()
        {
            var options = new List<ConfigOption>();
            for (uint year = 2020; year <= 2035; year++)
            {
                options.Add(new ConfigOption(year, $"{year}"));
            }
            return options;
        }

        /// <summary>
        /// 生成月份选项（1-12）
        /// </summary>
        private static List<ConfigOption> BuildMonthOptions()
        {
            var options = new List<ConfigOption>();
            for (uint month = 1; month <= 12; month++)
            {
                options.Add(new ConfigOption(month, $"{month}"));
            }
            return options;
        }

        /// <summary>
        /// 生成日期选项（1-31）
        /// </summary>
        private static List<ConfigOption> BuildDayOptions()
        {
            var options = new List<ConfigOption>();
            for (uint day = 1; day <= 31; day++)
            {
                options.Add(new ConfigOption(day, $"{day}"));
            }
            return options;
        }

        /// <summary>
        /// 生成小时选项（0-23）
        /// </summary>
        private static List<ConfigOption> BuildHourOptions()
        {
            var options = new List<ConfigOption>();
            for (uint hour = 0; hour <= 23; hour++)
            {
                options.Add(new ConfigOption(hour, $"{hour:D2}"));
            }
            return options;
        }

        /// <summary>
        /// 生成分钟选项（0-59）
        /// </summary>
        private static List<ConfigOption> BuildMinOptions()
        {
            var options = new List<ConfigOption>();
            for (uint min = 0; min <= 59; min++)
            {
                options.Add(new ConfigOption(min, $"{min:D2}"));
            }
            return options;
        }

        /// <summary>
        /// 生成秒选项（0-59）
        /// </summary>
        private static List<ConfigOption> BuildSecOptions()
        {
            var options = new List<ConfigOption>();
            for (uint sec = 0; sec <= 59; sec++)
            {
                options.Add(new ConfigOption(sec, $"{sec:D2}"));
            }
            return options;
        }

        /// <summary>
        /// 生成数值选项
        /// </summary>
        private static List<ConfigOption> BuildNumericOptions()
        {
            var options = new List<ConfigOption>();
            for (uint i = 0; i <= 10; i++)
            {
                options.Add(new ConfigOption(i, i.ToString()));
            }
            return options;
        }

        /// <summary>
        /// 获取时间显示文本
        /// </summary>
        private static string GetTimeDisplay(uint value)
        {
            return value.ToString();
        }

        /// <summary>
        /// 获取数值显示文本
        /// </summary>
        private static string GetNumericDisplay(uint value)
        {
            return value.ToString();
        }

        
        private static string GetWeekDayDisplay(uint value)
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



        public class BootSectorInfo
        {
            public uint Magic { get; set; }
            public byte SectorNumber { get; set; }
        }

        public class FlashParamInfo
        {
            public uint ResAddress { get; set; }
            public uint ResSize { get; set; }
        }
    }
}
