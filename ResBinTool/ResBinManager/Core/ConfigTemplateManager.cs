using ResBinManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ResBinManager.Core
{
    /// <summary>
    /// 配置模板管理器（混合架构：基础模板 + 特征规则 + JSON配置）
    /// </summary>
    public static class ConfigTemplateManager
    {
        private static ConfigTemplate _currentTemplate;
        private static readonly Dictionary<string, ConfigTemplate> _templates = new();
        private static string _currentProjectType;
        private static ProjectConfigFeatures _currentFeatures;
        private static bool _isInitialized = false;

        /// <summary>
        /// 当前模板ID（向后兼容）
        /// </summary>
        public static ConfigTemplateId CurrentTemplateId
        {
            get
            {
                if (string.IsNullOrEmpty(_currentProjectType))
                    return ConfigTemplateId.Default;
                return Enum.TryParse<ConfigTemplateId>(_currentProjectType, out var id) ? id : ConfigTemplateId.Default;
            }
            set
            {
                _currentProjectType = value.ToString();
                UpdateCurrentTemplate();
            }
        }

        /// <summary>
        /// 当前项目类型
        /// </summary>
        public static string CurrentProjectType
        {
            get => _currentProjectType;
            set
            {
                _currentProjectType = value;
                UpdateCurrentTemplate();
            }
        }

        /// <summary>
        /// 当前项目特征
        /// </summary>
        public static ProjectConfigFeatures CurrentFeatures
        {
            get => _currentFeatures;
            set
            {
                _currentFeatures = value;
                UpdateCurrentTemplate();
            }
        }

        /// <summary>
        /// 当前模板
        /// </summary>
        public static ConfigTemplate CurrentTemplate
        {
            get
            {
                EnsureInitialized();
                return _currentTemplate;
            }
        }

        /// <summary>
        /// 当前项目的完整配置值
        /// </summary>
        public static Dictionary<string, uint> CurrentValues
        {
            get
            {
                if (_currentTemplate == null)
                {
                    EnsureInitialized();
                }

                if (_currentTemplate != null && !string.IsNullOrEmpty(_currentProjectType))
                {
                    return _currentTemplate.GetValuesForProject(_currentProjectType, _currentFeatures);
                }

                return _currentTemplate?.BaseValues ?? new Dictionary<string, uint>();
            }
        }

        /// <summary>
        /// 所有已注册的模板（向后兼容版本）
        /// </summary>
        [Obsolete("使用 AllTemplates 属性获取字符串键版本")]
        public static Dictionary<ConfigTemplateId, ConfigTemplate> AllTemplatesLegacy
        {
            get
            {
                var result = new Dictionary<ConfigTemplateId, ConfigTemplate>();
                foreach (var kvp in _templates)
                {
                    if (Enum.TryParse<ConfigTemplateId>(kvp.Key, out var id))
                    {
                        result[id] = kvp.Value;
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// 所有已注册的模板
        /// </summary>
        public static IDictionary<string, ConfigTemplate> AllTemplates => _templates;

        /// <summary>
        /// 所有模板名称
        /// </summary>
        public static IEnumerable<string> TemplateNames
        {
            get
            {
                EnsureInitialized();
                return _templates.Values.Select(t => t.Name);
            }
        }

        /// <summary>
        /// 所有模板ID
        /// </summary>
        public static IEnumerable<string> TemplateIds
        {
            get
            {
                EnsureInitialized();
                return _templates.Keys;
            }
        }

        private static void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                InitializeTemplates();
                _isInitialized = true;
            }
        }

        /// <summary>
        /// 初始化模板系统
        /// </summary>
        public static void InitializeTemplates()
        {
            _templates.Clear();

            // 1. 创建基础模板
            var baseTemplate = CreateBaseTemplate();
            _templates[baseTemplate.Id] = baseTemplate;
            _currentTemplate = baseTemplate;

            // 2. 注册预定义规则（暂时禁用，ConfigRuleEngine尚未实现）
            // RegisterPredefinedRules(baseTemplate);

            // 3. 添加项目差异覆盖
            AddProjectOverrides(baseTemplate);

            // 4. 尝试从JSON文件加载额外模板
            LoadJsonTemplates();

            System.Diagnostics.Debug.WriteLine($"[ConfigTemplateManager] Initialized with {_templates.Count} template(s)");
        }

        /// <summary>
        /// 创建基础模板（包含所有项目通用的默认值）
        /// </summary>
        private static ConfigTemplate CreateBaseTemplate()
        {
            var template = new ConfigTemplate
            {
                Id = "BaseTemplate",
                Name = "通用基础模板",
                Description = "所有项目通用的基础配置值"
            };

            // 基础配置值（90%+项目通用）
            // 使用 FirmwareConstants 统一值格式 (0x81000000 + 偏移)
            template.BaseValues = new Dictionary<string, uint>
            {
                // 日期时间
                { "CONFIG_ID_YEAR", 2026 },
                { "CONFIG_ID_MONTH", 1 },
                { "CONFIG_ID_MDAY", 1 },
                { "CONFIG_ID_WDAY", 4 },
                { "CONFIG_ID_HOUR", 0 },
                { "CONFIG_ID_MIN", 0 },
                { "CONFIG_ID_SEC", 0 },
                // 基础设置
                { "CONFIG_ID_LANGUAGE", FirmwareConstants.R_STR_LAN_SCHINESE },      // 简体中文 0x81000001
                { "CONFIG_ID_SCREENSAVE", FirmwareConstants.R_STR_COM_OFF },        // 关闭 0x81000014
                { "CONFIG_ID_FREQUNCY", FirmwareConstants.R_STR_COM_50HZ },         // 50Hz 0x8100001D
                { "CONFIG_ID_ROTATE", FirmwareConstants.R_STR_COM_OFF },            // 关闭 0x81000014
                { "CONFIG_ID_FILLIGHT", FirmwareConstants.R_STR_COM_OFF },          // 关闭 0x81000014
                { "CONFIG_ID_TIMESTAMP", FirmwareConstants.R_STR_COM_ON },          // 开启 0x81000015
                { "CONFIG_ID_MOTIONDECTION", FirmwareConstants.R_STR_COM_OFF },     // 关闭 0x81000014
                { "CONFIG_ID_TIMEPHOTO", FirmwareConstants.R_STR_COM_OFF },         // 关闭 0x81000014
                { "CONFIG_ID_PARKMODE", FirmwareConstants.R_STR_COM_OFF },          // 关闭 0x81000014
                { "CONFIG_ID_KEYSOUND", FirmwareConstants.R_STR_COM_ON },           // 开启 0x81000015
                { "CONFIG_ID_IR_LED", FirmwareConstants.R_STR_COM_OFF },            // 关闭 0x81000014
                { "CONFIG_ID_LOOPTIME", FirmwareConstants.R_STR_COM_OFF },          // 关闭 0x81000014
                { "CONFIG_ID_AUDIOREC", FirmwareConstants.R_STR_COM_ON },           // 开启 0x81000015
                { "CONFIG_ID_EV", FirmwareConstants.R_STR_COM_P0_0 },               // 0.0 0x81000033
                { "CONFIG_ID_WBLANCE", FirmwareConstants.R_STR_ISP_AUTO },          // 自动 0x8100009C
                { "CONFIG_ID_PFASTVIEW", FirmwareConstants.R_STR_COM_OFF },         // 关闭 0x81000014
                { "CONFIG_ID_PTIMESTRAMP", FirmwareConstants.R_STR_COM_ON },        // 开启 0x81000015
                { "CONFIG_ID_PEV", FirmwareConstants.R_STR_COM_P0_0 },              // 0.0 0x81000033
                { "CONFIG_ID_THUMBNAIL", FirmwareConstants.R_STR_COM_ON },          // 开启 0x81000015
                { "CONFIG_ID_GSENSORMODE", FirmwareConstants.R_STR_COM_ON },        // 开启 0x81000015
                // 其他通用配置
                { "CONFIG_ID_FORMAT", FirmwareConstants.R_STR_COM_OFF },            // 关闭 0x81000014
                { "CONFIG_ID_DEFUALT", FirmwareConstants.R_STR_COM_OFF },           // 关闭 0x81000014
                { "CONFIG_ID_VIDEORECEFFECT", FirmwareConstants.R_STR_COM_ON },     // 开启 0x81000015
            };

            return template;
        }

        /// <summary>
        /// 注册预定义规则到模板（暂时禁用，ConfigRuleEngine尚未实现）
        /// </summary>
        private static void RegisterPredefinedRules(ConfigTemplate template)
        {
            // var rules = ConfigRuleEngine.GetAllRules();
            // foreach (KeyValuePair<string, ConfigRuleEngine.RuleDefinition> kvp in rules)
            // {
            //     template.AddFeatureRule(kvp.Key, kvp.Value.Evaluator, kvp.Value.Description);
            // }
        }

        /// <summary>
        /// 添加项目差异覆盖（使用 FirmwareConstants 统一值格式）
        /// </summary>
        private static void AddProjectOverrides(ConfigTemplate template)
        {
            // JT529X项目差异
            template.SetProjectOverride("JT529X", new Dictionary<string, uint>
            {
                { "CONFIG_ID_YEAR", 2025 },
                { "CONFIG_ID_RESOLUTION", FirmwareConstants.R_STR_RES_HD },       // HD 0x8100007B
                { "CONFIG_ID_GSENSOR", FirmwareConstants.R_STR_COM_OFF },         // 关闭 0x81000014
                { "CONFIG_ID_GSENSORMODE", FirmwareConstants.R_STR_COM_OFF },     // 关闭 0x81000014
            });

            // DC508J项目差异
            template.SetProjectOverride("DC508J", new Dictionary<string, uint>
            {
                { "CONFIG_ID_RESOLUTION", FirmwareConstants.R_STR_RES_1080P_SHORT }, // 1080P_SHORT 0x81000078
                { "CONFIG_ID_PRESLUTION", FirmwareConstants.R_STR_RES_20M },         // 20M 0x8100007E
                { "CONFIG_ID_VOLUME", 10 },                                           // 音量10（原始数值）
            });

            // GX_T317BV200项目差异
            template.SetProjectOverride("GX_T317BV200", new Dictionary<string, uint>
            {
                { "CONFIG_ID_RESOLUTION", FirmwareConstants.R_STR_RES_1080P_SHORT }, // 1080P_SHORT 0x81000078
            });

            // HM020F项目差异
            template.SetProjectOverride("HM020F", new Dictionary<string, uint>
            {
                { "CONFIG_ID_RESOLUTION", FirmwareConstants.R_STR_RES_1080P_SHORT }, // 1080P_SHORT 0x81000078
            });

            // MKL_CM5项目差异
            template.SetProjectOverride("MKL_CM5", new Dictionary<string, uint>
            {
                { "CONFIG_ID_RESOLUTION", FirmwareConstants.R_STR_RES_1080P_SHORT }, // 1080P_SHORT 0x81000078
            });

            // MKL_DM15项目差异
            template.SetProjectOverride("MKL_DM15", new Dictionary<string, uint>
            {
                { "CONFIG_ID_RESOLUTION", FirmwareConstants.R_STR_RES_1080P_SHORT }, // 1080P_SHORT 0x81000078
            });

            // JRX_JT529X项目差异
            template.SetProjectOverride("JRX_JT529X", new Dictionary<string, uint>
            {
                { "CONFIG_ID_RESOLUTION", FirmwareConstants.R_STR_RES_HD },          // HD 0x8100007B
            });

            // JRX_AX329X项目差异
            template.SetProjectOverride("JRX_AX329X", new Dictionary<string, uint>
            {
                { "CONFIG_ID_RESOLUTION", FirmwareConstants.R_STR_RES_1080P_SHORT }, // 1080P_SHORT 0x81000078
            });
        }

        /// <summary>
        /// 从JSON文件加载额外模板
        /// </summary>
        private static void LoadJsonTemplates()
        {
            // 尝试从多个位置加载JSON模板
            var searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "Templates"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ResBinManager", "Templates")
            };

            foreach (var path in searchPaths)
            {
                var templates = JsonTemplateLoader.LoadAllFromDirectory(path);
                foreach (var template in templates)
                {
                    if (!_templates.ContainsKey(template.Id))
                    {
                        _templates[template.Id] = template;
                        System.Diagnostics.Debug.WriteLine($"[ConfigTemplateManager] Loaded JSON template: {template.Name}");
                    }
                }
            }
        }

        /// <summary>
        /// 更新当前模板（当项目类型或特征变化时）
        /// </summary>
        private static void UpdateCurrentTemplate()
        {
            if (_currentTemplate != null && !string.IsNullOrEmpty(_currentProjectType))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ConfigTemplateManager] Updated template for project: {_currentProjectType}");
            }
        }

        /// <summary>
        /// 获取指定模板的默认值（向后兼容）
        /// </summary>
        [Obsolete("使用 GetValuesForProject 方法替代")]
        public static Dictionary<ConfigId, uint> GetDefaultValues(ConfigTemplateId templateId)
        {
            EnsureInitialized();
            var projectType = templateId.ToString();
            var stringValues = GetValuesForProject(projectType);
            var result = new Dictionary<ConfigId, uint>();
            foreach (var kvp in stringValues)
            {
                if (Enum.TryParse<ConfigId>(kvp.Key, out var configId))
                {
                    result[configId] = kvp.Value;
                }
            }
            return result;
        }

        /// <summary>
        /// 获取指定项目的完整配置值
        /// </summary>
        public static Dictionary<string, uint> GetValuesForProject(string projectType, ProjectConfigFeatures features = null)
        {
            EnsureInitialized();

            var effectiveFeatures = features ?? _currentFeatures ?? ProjectFeatureDatabase.GetFeatures(ParseProjectType(projectType));

            if (_templates.TryGetValue("BaseTemplate", out var template))
            {
                return template.GetValuesForProject(projectType, effectiveFeatures);
            }

            return new Dictionary<string, uint>();
        }

        /// <summary>
        /// 获取指定项目的完整配置值（ConfigId版本）
        /// </summary>
        public static Dictionary<ConfigId, uint> GetValuesForProject(ProjectType projectType, ProjectConfigFeatures features = null)
        {
            EnsureInitialized();

            var effectiveFeatures = features ?? _currentFeatures ?? ProjectFeatureDatabase.GetFeatures(projectType);

            if (_templates.TryGetValue("BaseTemplate", out var template))
            {
                return template.GetValuesForProject(projectType, effectiveFeatures);
            }

            return new Dictionary<ConfigId, uint>();
        }

        /// <summary>
        /// 注册自定义模板
        /// </summary>
        public static void RegisterTemplate(ConfigTemplate template)
        {
            EnsureInitialized();
            _templates[template.Id] = template;
            System.Diagnostics.Debug.WriteLine($"[ConfigTemplateManager] Registered template: {template.Name}");
        }

        /// <summary>
        /// 获取指定ID的模板
        /// </summary>
        public static ConfigTemplate GetTemplate(string templateId)
        {
            EnsureInitialized();
            return _templates.TryGetValue(templateId, out var template) ? template : null;
        }

        /// <summary>
        /// 检查模板是否存在
        /// </summary>
        public static bool HasTemplate(string templateId)
        {
            EnsureInitialized();
            return _templates.ContainsKey(templateId);
        }

        /// <summary>
        /// 从JSON文件加载并注册模板
        /// </summary>
        public static bool LoadTemplateFromFile(string filePath)
        {
            var template = JsonTemplateLoader.LoadFromFile(filePath);
            if (template != null)
            {
                RegisterTemplate(template);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 保存模板为JSON文件
        /// </summary>
        public static bool SaveTemplateToFile(string templateId, string filePath)
        {
            if (_templates.TryGetValue(templateId, out var template))
            {
                return JsonTemplateLoader.SaveToFile(template, filePath);
            }
            return false;
        }

        /// <summary>
        /// 创建新项目模板（基于基础模板添加差异覆盖）
        /// </summary>
        public static ConfigTemplate CreateProjectTemplate(string projectType, string name, string description, Dictionary<string, uint> overrides)
        {
            var template = new ConfigTemplate
            {
                Id = $"Project_{projectType}",
                Name = name,
                Description = description
            };

            // 复制基础值
            if (_templates.TryGetValue("BaseTemplate", out var baseTemplate))
            {
                template.BaseValues = new Dictionary<string, uint>(baseTemplate.BaseValues);
            }

            // 设置差异覆盖
            template.SetProjectOverride(projectType, overrides);

            return template;
        }

        /// <summary>
        /// 解析项目类型字符串为枚举
        /// </summary>
        private static ProjectType ParseProjectType(string projectType)
        {
            if (Enum.TryParse<ProjectType>(projectType, out var type))
            {
                return type;
            }
            return ProjectType.Unknown;
        }

        /// <summary>
        /// 导出当前配置为JSON
        /// </summary>
        public static string ExportCurrentConfigAsJson()
        {
            EnsureInitialized();
            if (_currentTemplate != null)
            {
                return JsonTemplateLoader.ToJson(_currentTemplate);
            }
            return "{}";
        }

        /// <summary>
        /// 获取模板统计信息
        /// </summary>
        public static string GetTemplateStats()
        {
            EnsureInitialized();

            var stats = $"模板总数: {_templates.Count}\n";

            if (_templates.TryGetValue("BaseTemplate", out var baseTemplate))
            {
                stats += $"基础配置项: {baseTemplate.BaseValues.Count}\n";
                stats += $"特征规则数: {baseTemplate.FeatureRules.Count}\n";
                stats += $"项目差异覆盖: {baseTemplate.ProjectOverrides.Count} 个项目\n";
            }

            return stats;
        }
    }
}
