using System.Collections.Generic;
using ResBinManager.Core;

namespace ResBinManager.Models
{
    /// <summary>
    /// 项目配置映射定义
    /// 用于处理不同项目CONFIG_ID枚举顺序不一致的问题
    /// </summary>
    public class ProjectConfigMapping
    {
        /// <summary>
        /// 项目类型
        /// </summary>
        public ProjectType ProjectType { get; set; }

        /// <summary>
        /// 项目名称
        /// </summary>
        public string ProjectName { get; set; } = string.Empty;

        /// <summary>
        /// 索引到配置名的映射
        /// Key: 配置项在固件中的索引位置
        /// Value: 配置项名称（对应ConfigId枚举名称）
        /// </summary>
        public Dictionary<int, string> IndexToConfigName { get; set; } = new();

        /// <summary>
        /// 配置名到索引的映射（反向映射，用于快速查找）
        /// Key: 配置项名称
        /// Value: 配置项在固件中的索引位置
        /// </summary>
        public Dictionary<string, int> ConfigNameToIndex { get; set; } = new();

        /// <summary>
        /// 该项目的默认配置值
        /// Key: 配置项名称
        /// Value: 默认值
        /// </summary>
        public Dictionary<string, uint> DefaultValues { get; set; } = new();

        /// <summary>
        /// 配置项元数据覆盖（从 JSON 加载）
        /// Key: 配置项名称
        /// Value: 元数据覆盖信息
        /// </summary>
        public Dictionary<string, ConfigItemMetadataOverride> MetadataOverrides { get; set; } = new();

        /// <summary>
        /// 配置项映射列表（从 JSON 加载）
        /// </summary>
        public List<ConfigMappingItem> Mappings { get; set; } = new();

        /// <summary>
        /// 配置项总数
        /// </summary>
        public int ConfigItemCount => IndexToConfigName.Count;

        /// <summary>
        /// 根据索引获取配置项名称
        /// </summary>
        public string? GetConfigNameByIndex(int index)
        {
            return IndexToConfigName.TryGetValue(index, out var name) ? name : null;
        }

        /// <summary>
        /// 根据配置名获取索引
        /// </summary>
        public int GetIndexByConfigName(string configName)
        {
            return ConfigNameToIndex.TryGetValue(configName, out var index) ? index : -1;
        }

        /// <summary>
        /// 获取默认值
        /// </summary>
        public uint GetDefaultValue(string configName, uint fallbackValue = 0)
        {
            return DefaultValues.TryGetValue(configName, out var value) ? value : fallbackValue;
        }

        /// <summary>
        /// 添加配置项映射
        /// </summary>
        public void AddMapping(int index, string configName, uint defaultValue = 0)
        {
            IndexToConfigName[index] = configName;
            ConfigNameToIndex[configName] = index;
            DefaultValues[configName] = defaultValue;
        }

        /// <summary>
        /// 清除所有映射
        /// </summary>
        public void Clear()
        {
            IndexToConfigName.Clear();
            ConfigNameToIndex.Clear();
            DefaultValues.Clear();
        }

        /// <summary>
        /// 从 JSON 文件加载映射配置
        /// </summary>
        public static ProjectConfigMapping? LoadFromJsonFile(string filePath)
        {
            var config = ProjectMappingConfigLoader.LoadFromFile(filePath);
            if (config == null)
                return null;

            return ProjectMappingConfigLoader.ConvertToMapping(config);
        }

        /// <summary>
        /// 保存映射配置到 JSON 文件
        /// </summary>
        public bool SaveToJsonFile(string filePath)
        {
            var config = ProjectMappingConfigLoader.ConvertToJsonConfig(this);
            return ProjectMappingConfigLoader.SaveToFile(config, filePath);
        }
    }

    /// <summary>
    /// 项目配置映射数据库
    /// 包含所有已知项目的CONFIG_ID映射
    /// </summary>
    public static class ProjectConfigMappingDatabase
    {
        private static Dictionary<ProjectType, ProjectConfigMapping>? _mappings;
        private static bool _jsonMappingsLoaded = false;

        /// <summary>
        /// 获取所有项目映射
        /// </summary>
        public static Dictionary<ProjectType, ProjectConfigMapping> GetAllMappings()
        {
            if (_mappings == null)
            {
                InitializeMappings();
            }
            return _mappings!;
        }

        /// <summary>
        /// 获取指定项目的配置映射
        /// </summary>
        public static ProjectConfigMapping GetMapping(ProjectType projectType)
        {
            if (_mappings == null)
            {
                InitializeMappings();
            }
            return _mappings!.TryGetValue(projectType, out var mapping) ? mapping : GetDefaultMapping();
        }

        /// <summary>
        /// 获取默认映射（当项目类型未知时使用）
        /// </summary>
        public static ProjectConfigMapping GetDefaultMapping()
        {
            return GetMapping(ProjectType.DC508J);
        }

        /// <summary>
        /// 从 JSON 配置文件目录加载所有映射配置
        /// </summary>
        public static void LoadMappingsFromDirectory(string? directoryPath = null)
        {
            if (_mappings == null)
            {
                InitializeMappings();
            }

            var jsonMappings = ProjectMappingConfigLoader.LoadAllFromDirectory(directoryPath);
            
            foreach (var mapping in jsonMappings)
            {
                // 如果已存在相同项目类型的映射，则覆盖；否则添加新映射
                _mappings![mapping.ProjectType] = mapping;
                System.Diagnostics.Debug.WriteLine($"[ProjectConfigMappingDatabase] Loaded/Updated mapping for {mapping.ProjectName}");
            }

            _jsonMappingsLoaded = true;
            System.Diagnostics.Debug.WriteLine($"[ProjectConfigMappingDatabase] Loaded {jsonMappings.Count} JSON mapping(s)");
        }

        /// <summary>
        /// 添加或更新项目映射配置
        /// </summary>
        public static void AddOrUpdateMapping(ProjectConfigMapping mapping)
        {
            if (_mappings == null)
            {
                InitializeMappings();
            }

            _mappings![mapping.ProjectType] = mapping;
            System.Diagnostics.Debug.WriteLine($"[ProjectConfigMappingDatabase] Added/Updated mapping for {mapping.ProjectName}");
        }

        /// <summary>
        /// 重新加载所有映射（包括内置和 JSON 配置）
        /// </summary>
        public static void ReloadMappings()
        {
            _mappings = null;
            _jsonMappingsLoaded = false;
            InitializeMappings();
        }

        /// <summary>
        /// 初始化所有项目的配置映射
        /// </summary>
        private static void InitializeMappings()
        {
            _mappings = new Dictionary<ProjectType, ProjectConfigMapping>();

            // 503项目映射
            _mappings[ProjectType.JT529X] = CreateJT529XMapping();

            // DC508J项目映射（作为默认映射）
            _mappings[ProjectType.DC508J] = CreateDC508JMapping();

            // GX-T317BV200项目映射
            _mappings[ProjectType.GX_T317BV200] = CreateGXT317Mapping();

            // HM020F项目映射
            _mappings[ProjectType.HM020F] = CreateHM020FMapping();

            // MKL_CM5项目映射
            _mappings[ProjectType.MKL_CM5] = CreateMKLCM5Mapping();

            // MKL_DM15项目映射
            _mappings[ProjectType.MKL_DM15] = CreateMKLDM15Mapping();

            // JRX_JT529X项目映射
            _mappings[ProjectType.JRX_JT529X] = CreateJT529XMapping();
            _mappings[ProjectType.JRX_JT529X].ProjectType = ProjectType.JRX_JT529X;
            _mappings[ProjectType.JRX_JT529X].ProjectName = "JRX_JT529X";

            // JRX_AX329X项目映射
            _mappings[ProjectType.JRX_AX329X] = CreateDC508JMapping();
            _mappings[ProjectType.JRX_AX329X].ProjectType = ProjectType.JRX_AX329X;
            _mappings[ProjectType.JRX_AX329X].ProjectName = "JRX_AX329X";

            // 如果尚未加载 JSON 配置，则自动加载
            if (!_jsonMappingsLoaded)
            {
                try
                {
                    LoadMappingsFromDirectory();
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectConfigMappingDatabase] Error loading JSON mappings: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 创建503项目映射
        /// 特点：没有CONFIG_ID_LCD_BRIGHT，CONFIG_ID_AUTOOFF在索引8，
        /// 包含打印机相关配置项，CONFIG_ID_GSENSOR在索引18
        /// </summary>
        private static ProjectConfigMapping CreateJT529XMapping()
        {
            var mapping = new ProjectConfigMapping
            {
                ProjectType = ProjectType.JT529X,
                ProjectName = "JT529X"
            };

            // 时间配置项（索引0-6）
            mapping.AddMapping(0, "CONFIG_ID_YEAR", 2025);
            mapping.AddMapping(1, "CONFIG_ID_MONTH", 1);
            mapping.AddMapping(2, "CONFIG_ID_MDAY", 1);
            mapping.AddMapping(3, "CONFIG_ID_WDAY", 4);
            mapping.AddMapping(4, "CONFIG_ID_HOUR", 0);
            mapping.AddMapping(5, "CONFIG_ID_MIN", 0);
            mapping.AddMapping(6, "CONFIG_ID_SEC", 0);

            // 系统设置（索引7开始，注意：503项目没有CONFIG_ID_LCD_BRIGHT）
            // 使用 FirmwareConstants 统一值格式 (0x81000000 + 偏移)
            mapping.AddMapping(7, "CONFIG_ID_LANGUAGE", FirmwareConstants.R_STR_LAN_RUSSIAN);    // 俄语 0x81000006
            mapping.AddMapping(8, "CONFIG_ID_AUTOOFF", FirmwareConstants.R_STR_COM_OFF);         // 关闭 0x81000014
            mapping.AddMapping(9, "CONFIG_ID_SCREENSAVE", FirmwareConstants.R_STR_COM_OFF);      // 关闭 0x81000014
            mapping.AddMapping(10, "CONFIG_ID_FREQUNCY", FirmwareConstants.R_STR_COM_60HZ);     // 60Hz 0x8100001E (503项目默认60Hz)
            mapping.AddMapping(11, "CONFIG_ID_ROTATE", FirmwareConstants.R_STR_COM_OFF);         // 关闭 0x81000014
            mapping.AddMapping(12, "CONFIG_ID_FILLIGHT", FirmwareConstants.R_STR_COM_OFF);       // 关闭 0x81000014
            mapping.AddMapping(13, "CONFIG_ID_RESOLUTION", FirmwareConstants.R_STR_RES_HD);      // HD 0x8100007B
            mapping.AddMapping(14, "CONFIG_ID_ISP_FILTER", FirmwareConstants.R_STR_ISP_AUTO);    // 自动 0x8100009C (503项目特有)
            mapping.AddMapping(15, "CONFIG_ID_TIMESTAMP", FirmwareConstants.R_STR_COM_ON);       // 开启 0x81000015
            mapping.AddMapping(16, "CONFIG_ID_MOTIONDECTION", FirmwareConstants.R_STR_COM_OFF);  // 关闭 0x81000014
            mapping.AddMapping(17, "CONFIG_ID_PARKMODE", FirmwareConstants.R_STR_COM_OFF);       // 关闭 0x81000014
            mapping.AddMapping(18, "CONFIG_ID_GSENSOR", FirmwareConstants.R_STR_COM_OFF);        // 关闭 0x81000014 (503项目在索引18)
            mapping.AddMapping(19, "CONFIG_ID_KEYSOUND", FirmwareConstants.R_STR_COM_ON);        // 开启 0x81000015
            mapping.AddMapping(20, "CONFIG_ID_IR_LED", FirmwareConstants.R_STR_COM_OFF);         // 关闭 0x81000014
            mapping.AddMapping(21, "CONFIG_ID_LOOPTIME", FirmwareConstants.R_STR_COM_OFF);       // 关闭 0x81000014
            mapping.AddMapping(22, "CONFIG_ID_AUDIOREC", FirmwareConstants.R_STR_COM_ON);        // 开启 0x81000015
            mapping.AddMapping(23, "CONFIG_ID_EV", FirmwareConstants.R_STR_COM_P0_0);            // 0.0 0x81000033
            mapping.AddMapping(24, "CONFIG_ID_WBLANCE", FirmwareConstants.R_STR_ISP_AUTO);       // 自动 0x8100009C
            mapping.AddMapping(25, "CONFIG_ID_PRESLUTION", FirmwareConstants.R_STR_RES_12M);     // 12M 0x8100007F
            mapping.AddMapping(26, "CONFIG_ID_PFASTVIEW", FirmwareConstants.R_STR_COM_OFF);      // 关闭 0x81000014
            mapping.AddMapping(27, "CONFIG_ID_PTIMESTRAMP", FirmwareConstants.R_STR_COM_ON);     // 开启 0x81000015
            mapping.AddMapping(28, "CONFIG_ID_PEV", FirmwareConstants.R_STR_COM_P0_0);           // 0.0 0x81000033
            mapping.AddMapping(29, "CONFIG_ID_VOLUME", 10);                                       // 音量10（原始数值）
            mapping.AddMapping(30, "CONFIG_ID_THUMBNAIL", FirmwareConstants.R_STR_COM_ON);       // 开启 0x81000015
            mapping.AddMapping(31, "CONFIG_ID_GSENSORMODE", FirmwareConstants.R_STR_COM_OFF);    // 关闭 0x81000014
            mapping.AddMapping(32, "CONFIG_ID_TIMEPHOTO", FirmwareConstants.R_STR_COM_OFF);      // 关闭 0x81000014
            mapping.AddMapping(33, "CONFIG_ID_MOREPHOTO", FirmwareConstants.R_STR_COM_OFF);      // 关闭 0x81000014 (503项目特有)
            mapping.AddMapping(34, "CONFIG_ID_PRINTER_EN", FirmwareConstants.R_STR_COM_OFF);     // 关闭 0x81000014 (503项目特有)
            mapping.AddMapping(35, "CONFIG_ID_COLOR_PRINT", FirmwareConstants.R_STR_COM_OFF);    // 关闭 0x81000014 (503项目特有)
            mapping.AddMapping(36, "CONFIG_ID_PRINTER_DENSITY", FirmwareConstants.R_STR_COM_MIDDLE); // 中等 0x8100001B (503项目特有)
            mapping.AddMapping(37, "CONFIG_ID_PRINTER_MODE", FirmwareConstants.R_STR_SET_PRINT_GRAY); // 灰度打印 (503项目特有)
            mapping.AddMapping(38, "CONFIG_ID_PRINTER_NEARFAR", FirmwareConstants.R_STR_TIP_NEAR); // 近 (503项目特有)
            mapping.AddMapping(39, "CONFIG_ID_PRINTER_DELAY", 0);                                 // 延迟0（原始数值）(503项目特有)
            mapping.AddMapping(40, "CONFIG_ID_BAT_OLD", FirmwareConstants.R_STR_COM_LEVEL_5);    // 级别5 0x81000024 (503项目特有)
            mapping.AddMapping(41, "CONFIG_ID_BAT_CHECK_FLAG", FirmwareConstants.R_STR_COM_OFF); // 关闭 0x81000014 (503项目特有)
            mapping.AddMapping(42, "CONFIG_ID_DEVICE_ID1", 0);                                    // 设备ID1 (503项目特有)
            mapping.AddMapping(43, "CONFIG_ID_DEVICE_ID2", 0);                                    // 设备ID2 (503项目特有)
            mapping.AddMapping(44, "CONFIG_ID_DEVICE_ID3", 0);                                    // 设备ID3 (503项目特有)
            mapping.AddMapping(45, "CONFIG_ID_DEVICE_ID4", 0);                                    // 设备ID4 (503项目特有)
            mapping.AddMapping(46, "CONFIG_ID_DEVICE_ID5", 0);                                    // 设备ID5 (503项目特有)
            mapping.AddMapping(47, "CONFIG_ID_DEVICE_ID6", 0);                                    // 设备ID6 (503项目特有)
            mapping.AddMapping(48, "CONFIG_ID_PRINTER_DENSITY_H", 0);                             // 打印浓度H (503项目特有)
            mapping.AddMapping(49, "CONFIG_ID_PRINTER_DENSITY_L", 75);                            // 打印浓度L (503项目特有)
            mapping.AddMapping(50, "CONFIG_ID_PRINTER_MOTE_SPEED", 23);                            // 马达速度 (503项目特有)
            mapping.AddMapping(51, "CONFIG_ID_BT_LED", FirmwareConstants.R_STR_COM_OFF);          // 关闭 0x81000014 (503项目特有)

            return mapping;
        }

        /// <summary>
        /// 创建DC508J项目映射（作为默认映射）
        /// 特点：有CONFIG_ID_LCD_BRIGHT，CONFIG_ID_AUTOOFF在索引9
        /// </summary>
        private static ProjectConfigMapping CreateDC508JMapping()
        {
            var mapping = new ProjectConfigMapping
            {
                ProjectType = ProjectType.DC508J,
                ProjectName = "DC508J"
            };

            // 时间配置项（索引0-6）
            mapping.AddMapping(0, "CONFIG_ID_YEAR", 2026);
            mapping.AddMapping(1, "CONFIG_ID_MONTH", 1);
            mapping.AddMapping(2, "CONFIG_ID_MDAY", 1);
            mapping.AddMapping(3, "CONFIG_ID_WDAY", 4);
            mapping.AddMapping(4, "CONFIG_ID_HOUR", 0);
            mapping.AddMapping(5, "CONFIG_ID_MIN", 0);
            mapping.AddMapping(6, "CONFIG_ID_SEC", 0);

            // 系统设置（索引7开始，注意：DC508J项目在索引8有CONFIG_ID_LCD_BRIGHT）
            // 使用 FirmwareConstants 统一值格式 (0x81000000 + 偏移)
            mapping.AddMapping(7, "CONFIG_ID_LANGUAGE", FirmwareConstants.R_STR_LAN_ENGLISH);    // English 0x81000000
            mapping.AddMapping(8, "CONFIG_ID_LCD_BRIGHT", FirmwareConstants.R_STR_COM_LEVEL_3);  // 级别3 0x81000022
            mapping.AddMapping(9, "CONFIG_ID_AUTOOFF", FirmwareConstants.R_STR_TIM_5MIN);        // 5分钟 0x81000043
            mapping.AddMapping(10, "CONFIG_ID_SCREENSAVE", FirmwareConstants.R_STR_COM_OFF);     // 关闭 0x81000014
            mapping.AddMapping(11, "CONFIG_ID_FREQUNCY", FirmwareConstants.R_STR_COM_50HZ);      // 50Hz 0x8100001D
            mapping.AddMapping(12, "CONFIG_ID_ROTATE", FirmwareConstants.R_STR_COM_OFF);         // 关闭 0x81000014
            mapping.AddMapping(13, "CONFIG_ID_FILLIGHT", FirmwareConstants.R_STR_COM_OFF);       // 关闭 0x81000014
            mapping.AddMapping(14, "CONFIG_ID_RESOLUTION", FirmwareConstants.R_STR_RES_1080P_SHORT); // 1080P_SHORT 0x81000078
            mapping.AddMapping(15, "CONFIG_ID_TIMEPHOTO", FirmwareConstants.R_STR_COM_OFF);      // 关闭 0x81000014
            mapping.AddMapping(16, "CONFIG_ID_TIMESTAMP", FirmwareConstants.R_STR_COM_ON);       // 开启 0x81000015
            mapping.AddMapping(17, "CONFIG_ID_MOTIONDECTION", FirmwareConstants.R_STR_COM_OFF);  // 关闭 0x81000014
            mapping.AddMapping(18, "CONFIG_ID_PARKMODE", FirmwareConstants.R_STR_COM_OFF);       // 关闭 0x81000014
            mapping.AddMapping(19, "CONFIG_ID_GSENSOR", FirmwareConstants.R_STR_COM_ON);         // 开启 0x81000015
            mapping.AddMapping(20, "CONFIG_ID_KEYSOUND", FirmwareConstants.R_STR_COM_ON);        // 开启 0x81000015
            mapping.AddMapping(21, "CONFIG_ID_IR_LED", FirmwareConstants.R_STR_COM_OFF);         // 关闭 0x81000014
            mapping.AddMapping(22, "CONFIG_ID_LOOPTIME", FirmwareConstants.R_STR_TIM_3MIN);      // 3分钟 0x81000042
            mapping.AddMapping(23, "CONFIG_ID_AUDIOREC", FirmwareConstants.R_STR_COM_ON);        // 开启 0x81000015
            mapping.AddMapping(24, "CONFIG_ID_EV", FirmwareConstants.R_STR_COM_P0_0);            // 0.0 0x81000033
            mapping.AddMapping(25, "CONFIG_ID_WBLANCE", FirmwareConstants.R_STR_ISP_AUTO);       // 自动 0x8100009C
            mapping.AddMapping(26, "CONFIG_ID_PRESLUTION", FirmwareConstants.R_STR_RES_20M);     // 20M 0x8100007E
            mapping.AddMapping(27, "CONFIG_ID_PFASTVIEW", FirmwareConstants.R_STR_COM_OFF);      // 关闭 0x81000014
            mapping.AddMapping(28, "CONFIG_ID_PTIMESTRAMP", FirmwareConstants.R_STR_COM_ON);     // 开启 0x81000015
            mapping.AddMapping(29, "CONFIG_ID_PEV", FirmwareConstants.R_STR_COM_P0_0);           // 0.0 0x81000033
            mapping.AddMapping(30, "CONFIG_ID_VOLUME", 10);                                       // 音量10（原始数值）
            mapping.AddMapping(31, "CONFIG_ID_THUMBNAIL", FirmwareConstants.R_STR_COM_ON);       // 开启 0x81000015
            mapping.AddMapping(32, "CONFIG_ID_GSENSORMODE", FirmwareConstants.R_STR_COM_ON);     // 开启 0x81000015
            mapping.AddMapping(33, "CONFIG_ID_FORMAT", FirmwareConstants.R_STR_COM_OFF);         // 关闭 0x81000014
            mapping.AddMapping(34, "CONFIG_ID_DEFUALT", FirmwareConstants.R_STR_COM_OFF);        // 关闭 0x81000014
            mapping.AddMapping(35, "CONFIG_ID_VIDEORECEFFECT", FirmwareConstants.R_STR_COM_ON);  // 开启 0x81000015

            return mapping;
        }

        /// <summary>
        /// 创建GX-T317BV200项目映射
        /// </summary>
        private static ProjectConfigMapping CreateGXT317Mapping()
        {
            var mapping = CreateDC508JMapping();
            mapping.ProjectType = ProjectType.GX_T317BV200;
            mapping.ProjectName = "GX-T317BV200";

            // 覆盖特定配置（使用 FirmwareConstants）
            mapping.DefaultValues["CONFIG_ID_LANGUAGE"] = FirmwareConstants.R_STR_LAN_ENGLISH;        // English 0x81000000
            mapping.DefaultValues["CONFIG_ID_RESOLUTION"] = FirmwareConstants.R_STR_RES_1080P_SHORT;  // 1080P_SHORT 0x81000078
            mapping.DefaultValues["CONFIG_ID_PRESLUTION"] = FirmwareConstants.R_STR_RES_12M;          // 12M 0x8100007F

            return mapping;
        }

        /// <summary>
        /// 创建HM020F项目映射
        /// </summary>
        private static ProjectConfigMapping CreateHM020FMapping()
        {
            var mapping = CreateDC508JMapping();
            mapping.ProjectType = ProjectType.HM020F;
            mapping.ProjectName = "HM020F";

            // 覆盖特定配置（使用 FirmwareConstants）
            mapping.DefaultValues["CONFIG_ID_LANGUAGE"] = FirmwareConstants.R_STR_LAN_ENGLISH;        // English 0x81000000
            mapping.DefaultValues["CONFIG_ID_RESOLUTION"] = FirmwareConstants.R_STR_RES_720P_SHORT;   // 720P_SHORT 0x81000077
            mapping.DefaultValues["CONFIG_ID_PRESLUTION"] = FirmwareConstants.R_STR_RES_12M;          // 12M 0x8100007F
            mapping.DefaultValues["CONFIG_ID_AUTOOFF"] = FirmwareConstants.R_STR_COM_OFF;             // 关闭 0x81000014

            return mapping;
        }

        /// <summary>
        /// 创建MKL_CM5项目映射
        /// </summary>
        private static ProjectConfigMapping CreateMKLCM5Mapping()
        {
            var mapping = CreateDC508JMapping();
            mapping.ProjectType = ProjectType.MKL_CM5;
            mapping.ProjectName = "MKL_CM5";

            // 覆盖特定配置（使用 FirmwareConstants）
            mapping.DefaultValues["CONFIG_ID_LANGUAGE"] = FirmwareConstants.R_STR_LAN_ENGLISH;        // English 0x81000000
            mapping.DefaultValues["CONFIG_ID_RESOLUTION"] = FirmwareConstants.R_STR_RES_720P_SHORT;   // 720P_SHORT 0x81000077
            mapping.DefaultValues["CONFIG_ID_PRESLUTION"] = FirmwareConstants.R_STR_RES_12M;          // 12M 0x8100007F
            mapping.DefaultValues["CONFIG_ID_AUTOOFF"] = FirmwareConstants.R_STR_COM_OFF;             // 关闭 0x81000014

            return mapping;
        }

        /// <summary>
        /// 创建MKL_DM15项目映射
        /// 特点：没有CONFIG_ID_TIMEPHOTO、CONFIG_ID_PARKMODE、CONFIG_ID_GSENSOR等
        /// </summary>
        private static ProjectConfigMapping CreateMKLDM15Mapping()
        {
            var mapping = new ProjectConfigMapping
            {
                ProjectType = ProjectType.MKL_DM15,
                ProjectName = "MKL_DM15"
            };

            // 时间配置项（索引0-6）
            mapping.AddMapping(0, "CONFIG_ID_YEAR", 2026);
            mapping.AddMapping(1, "CONFIG_ID_MONTH", 1);
            mapping.AddMapping(2, "CONFIG_ID_MDAY", 1);
            mapping.AddMapping(3, "CONFIG_ID_WDAY", 4);
            mapping.AddMapping(4, "CONFIG_ID_HOUR", 0);
            mapping.AddMapping(5, "CONFIG_ID_MIN", 0);
            mapping.AddMapping(6, "CONFIG_ID_SEC", 0);

            // 系统设置（MKL_DM15项目缺少部分配置项，使用 FirmwareConstants 统一值格式）
            mapping.AddMapping(7, "CONFIG_ID_LANGUAGE", FirmwareConstants.R_STR_LAN_ENGLISH);    // English 0x81000000
            mapping.AddMapping(8, "CONFIG_ID_LCD_BRIGHT", FirmwareConstants.R_STR_COM_LEVEL_3);  // 级别3 0x81000022
            mapping.AddMapping(9, "CONFIG_ID_AUTOOFF", FirmwareConstants.R_STR_COM_OFF);         // 关闭 0x81000014
            mapping.AddMapping(10, "CONFIG_ID_SCREENSAVE", FirmwareConstants.R_STR_COM_OFF);     // 关闭 0x81000014
            mapping.AddMapping(11, "CONFIG_ID_FREQUNCY", FirmwareConstants.R_STR_COM_50HZ);      // 50Hz 0x8100001D
            mapping.AddMapping(12, "CONFIG_ID_ROTATE", FirmwareConstants.R_STR_COM_OFF);         // 关闭 0x81000014
            mapping.AddMapping(13, "CONFIG_ID_FILLIGHT", FirmwareConstants.R_STR_COM_OFF);       // 关闭 0x81000014
            mapping.AddMapping(14, "CONFIG_ID_RESOLUTION", FirmwareConstants.R_STR_RES_1080P);   // 1080P 0x81000073
            // 注意：MKL_DM15没有CONFIG_ID_TIMEPHOTO
            mapping.AddMapping(15, "CONFIG_ID_TIMESTAMP", FirmwareConstants.R_STR_COM_ON);       // 开启 0x81000015
            mapping.AddMapping(16, "CONFIG_ID_MOTIONDECTION", FirmwareConstants.R_STR_COM_OFF);  // 关闭 0x81000014
            // 注意：MKL_DM15没有CONFIG_ID_PARKMODE、CONFIG_ID_GSENSOR
            mapping.AddMapping(17, "CONFIG_ID_KEYSOUND", FirmwareConstants.R_STR_COM_ON);        // 开启 0x81000015
            mapping.AddMapping(18, "CONFIG_ID_IR_LED", FirmwareConstants.R_STR_COM_OFF);         // 关闭 0x81000014
            mapping.AddMapping(19, "CONFIG_ID_LOOPTIME", FirmwareConstants.R_STR_TIM_5MIN);      // 5分钟 0x81000043
            mapping.AddMapping(20, "CONFIG_ID_AUDIOREC", FirmwareConstants.R_STR_COM_ON);        // 开启 0x81000015
            mapping.AddMapping(21, "CONFIG_ID_EV", FirmwareConstants.R_STR_COM_P0_0);            // 0.0 0x81000033
            mapping.AddMapping(22, "CONFIG_ID_WBLANCE", FirmwareConstants.R_STR_ISP_AUTO);       // 自动 0x8100009C
            mapping.AddMapping(23, "CONFIG_ID_PRESLUTION", FirmwareConstants.R_STR_RES_12M);     // 12M 0x8100007F
            mapping.AddMapping(24, "CONFIG_ID_PFASTVIEW", FirmwareConstants.R_STR_COM_OFF);      // 关闭 0x81000014
            mapping.AddMapping(25, "CONFIG_ID_PTIMESTRAMP", FirmwareConstants.R_STR_COM_ON);     // 开启 0x81000015
            mapping.AddMapping(26, "CONFIG_ID_PEV", FirmwareConstants.R_STR_COM_P0_0);           // 0.0 0x81000033
            mapping.AddMapping(27, "CONFIG_ID_VOLUME", 10);                                       // 音量10（原始数值）
            mapping.AddMapping(28, "CONFIG_ID_THUMBNAIL", FirmwareConstants.R_STR_COM_ON);       // 开启 0x81000015
            mapping.AddMapping(29, "CONFIG_ID_GSENSORMODE", FirmwareConstants.R_STR_COM_ON);     // 开启 0x81000015
            mapping.AddMapping(30, "CONFIG_ID_FORMAT", FirmwareConstants.R_STR_COM_OFF);         // 关闭 0x81000014
            mapping.AddMapping(31, "CONFIG_ID_DEFUALT", FirmwareConstants.R_STR_COM_OFF);        // 关闭 0x81000014
            mapping.AddMapping(32, "CONFIG_ID_VIDEORECEFFECT", FirmwareConstants.R_STR_COM_ON);  // 开启 0x81000015

            return mapping;
        }
    }
}
