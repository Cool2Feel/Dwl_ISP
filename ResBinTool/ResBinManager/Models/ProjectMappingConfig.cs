using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResBinManager.Models
{
    /// <summary>
    /// 项目配置映射的 JSON 配置文件结构
    /// 用于从外部 JSON 文件加载项目配置映射，支持动态扩展新项目
    /// </summary>
    public class ProjectMappingJsonConfig
    {
        /// <summary>
        /// 配置文件版本
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// 项目类型名称（对应 ProjectType 枚举）
        /// </summary>
        [JsonPropertyName("projectType")]
        public string ProjectType { get; set; } = string.Empty;

        /// <summary>
        /// 项目显示名称
        /// </summary>
        [JsonPropertyName("projectName")]
        public string ProjectName { get; set; } = string.Empty;

        /// <summary>
        /// 项目描述
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 配置项映射列表
        /// </summary>
        [JsonPropertyName("mappings")]
        public List<MappingEntry> Mappings { get; set; } = new();

        /// <summary>
        /// 项目特征信息
        /// </summary>
        [JsonPropertyName("features")]
        public ProjectFeaturesInfo Features { get; set; } = new();
    }

    /// <summary>
    /// 单个配置项映射条目
    /// </summary>
    public class MappingEntry
    {
        /// <summary>
        /// 配置项在固件中的索引位置
        /// </summary>
        [JsonPropertyName("index")]
        public int Index { get; set; }

        /// <summary>
        /// 配置项名称（对应 ConfigId 枚举名称）
        /// </summary>
        [JsonPropertyName("configName")]
        public string ConfigName { get; set; } = string.Empty;

        /// <summary>
        /// 默认值（支持十进制和十六进制字符串，如 "0x0106"）
        /// </summary>
        [JsonPropertyName("defaultValue")]
        public JsonElement DefaultValue { get; set; }

        /// <summary>
        /// 配置项描述（可选）
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 配置项元数据覆盖（可选）
        /// 用于覆盖默认的显示名称、类型、分类等信息
        /// </summary>
        [JsonPropertyName("metadata")]
        public ConfigMetadataOverride? Metadata { get; set; }
    }

    /// <summary>
    /// 配置项元数据覆盖
    /// 允许 JSON 配置文件覆盖配置项的默认元数据
    /// </summary>
    public class ConfigMetadataOverride
    {
        /// <summary>
        /// 显示名称（中文名称，用于 UI 显示）
        /// </summary>
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        /// <summary>
        /// 配置项类型（如 "OnOff", "Language", "Resolution" 等）
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>
        /// 配置项分类（如 "系统设置", "录像设置" 等）
        /// </summary>
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        /// <summary>
        /// 详细描述信息
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    /// <summary>
    /// 配置项元数据覆盖（内部使用，已解析类型）
    /// </summary>
    public class ConfigItemMetadataOverride
    {
        /// <summary>
        /// 显示名称
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// 配置项类型（已解析为枚举）
        /// </summary>
        public ConfigItemType? Type { get; set; }

        /// <summary>
        /// 配置项分类
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// 详细描述信息
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 从 ConfigMetadataOverride 转换
        /// </summary>
        public static ConfigItemMetadataOverride? FromOverride(ConfigMetadataOverride? override_)
        {
            if (override_ == null)
                return null;

            ConfigItemType? type = null;
            if (!string.IsNullOrEmpty(override_.Type))
            {
                if (Enum.TryParse<ConfigItemType>(override_.Type, true, out var parsedType))
                {
                    type = parsedType;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigItemMetadataOverride] Unknown type: {override_.Type}");
                }
            }

            return new ConfigItemMetadataOverride
            {
                DisplayName = override_.DisplayName,
                Type = type,
                Category = override_.Category,
                Description = override_.Description
            };
        }
    }

    /// <summary>
    /// 项目特征信息
    /// </summary>
    public class ProjectFeaturesInfo
    {
        /// <summary>
        /// 是否有 LCD 亮度配置
        /// </summary>
        [JsonPropertyName("hasLcdBright")]
        public bool HasLcdBright { get; set; } = true;

        /// <summary>
        /// 是否有定时拍照配置
        /// </summary>
        [JsonPropertyName("hasTimePhoto")]
        public bool HasTimePhoto { get; set; } = true;

        /// <summary>
        /// 是否有停车模式配置
        /// </summary>
        [JsonPropertyName("hasParkMode")]
        public bool HasParkMode { get; set; } = true;

        /// <summary>
        /// 是否有 G-Sensor 配置
        /// </summary>
        [JsonPropertyName("hasGSensor")]
        public bool HasGSensor { get; set; } = true;

        /// <summary>
        /// 是否有红外灯配置
        /// </summary>
        [JsonPropertyName("hasIrLed")]
        public bool HasIrLed { get; set; } = true;

        /// <summary>
        /// 是否有补光灯配置
        /// </summary>
        [JsonPropertyName("hasFillLight")]
        public bool HasFillLight { get; set; } = true;

        /// <summary>
        /// 是否有旋转配置
        /// </summary>
        [JsonPropertyName("hasRotate")]
        public bool HasRotate { get; set; } = true;

        /// <summary>
        /// 配置项总数
        /// </summary>
        [JsonPropertyName("configItemCount")]
        public int ConfigItemCount { get; set; } = 0;
    }

    /// <summary>
    /// 项目映射配置文件的加载和保存
    /// </summary>
    public static class ProjectMappingConfigLoader
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// 配置文件目录（程序目录下的 mappings 文件夹）
        /// </summary>
        public static string MappingsDirectory
        {
            get
            {
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mappings");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                return dir;
            }
        }

        /// <summary>
        /// 从 JSON 文件加载项目映射配置
        /// </summary>
        public static ProjectMappingJsonConfig? LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectMappingConfigLoader] File not found: {filePath}");
                return null;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                return LoadFromJson(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectMappingConfigLoader] Load error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从 JSON 字符串加载项目映射配置
        /// </summary>
        public static ProjectMappingJsonConfig? LoadFromJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<ProjectMappingJsonConfig>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectMappingConfigLoader] Parse error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 将项目映射配置保存为 JSON 文件
        /// </summary>
        public static bool SaveToFile(ProjectMappingJsonConfig config, string filePath)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, _jsonOptions);
                File.WriteAllText(filePath, json);
                System.Diagnostics.Debug.WriteLine($"[ProjectMappingConfigLoader] Saved to: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectMappingConfigLoader] Save error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将 ProjectConfigMapping 转换为 JSON 配置
        /// </summary>
        public static ProjectMappingJsonConfig ConvertToJsonConfig(ProjectConfigMapping mapping)
        {
            var config = new ProjectMappingJsonConfig
            {
                Version = "1.0",
                ProjectType = mapping.ProjectType.ToString(),
                ProjectName = mapping.ProjectName,
                Description = $"项目 {mapping.ProjectName} 的配置映射"
            };

            // 转换映射列表
            foreach (var kvp in mapping.IndexToConfigName)
            {
                var entry = new MappingEntry
                {
                    Index = kvp.Key,
                    ConfigName = kvp.Value,
                    DefaultValue = JsonDocument.Parse(
                        mapping.DefaultValues.TryGetValue(kvp.Value, out var val) ? val.ToString() : "0"
                    ).RootElement
                };
                config.Mappings.Add(entry);
            }

            // 转换特征信息
            config.Features.HasLcdBright = mapping.IndexToConfigName.ContainsValue("CONFIG_ID_LCD_BRIGHT");
            config.Features.HasTimePhoto = mapping.IndexToConfigName.ContainsValue("CONFIG_ID_TIMEPHOTO");
            config.Features.HasParkMode = mapping.IndexToConfigName.ContainsValue("CONFIG_ID_PARKMODE");
            config.Features.HasGSensor = mapping.IndexToConfigName.ContainsValue("CONFIG_ID_GSENSOR");
            config.Features.HasIrLed = mapping.IndexToConfigName.ContainsValue("CONFIG_ID_IR_LED");
            config.Features.HasFillLight = mapping.IndexToConfigName.ContainsValue("CONFIG_ID_FILLIGHT");
            config.Features.HasRotate = mapping.IndexToConfigName.ContainsValue("CONFIG_ID_ROTATE");
            config.Features.ConfigItemCount = mapping.ConfigItemCount;

            return config;
        }

        /// <summary>
        /// 将 JSON 配置转换为 ProjectConfigMapping
        /// </summary>
        public static ProjectConfigMapping? ConvertToMapping(ProjectMappingJsonConfig config)
        {
            if (!Enum.TryParse<ProjectType>(config.ProjectType, out var projectType))
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectMappingConfigLoader] Unknown project type: {config.ProjectType}");
                return null;
            }

            var mapping = new ProjectConfigMapping
            {
                ProjectType = projectType,
                ProjectName = config.ProjectName
            };

            foreach (var entry in config.Mappings)
            {
                uint defaultValue = ParseDefaultValue(entry.DefaultValue);
                mapping.AddMapping(entry.Index, entry.ConfigName, defaultValue);

                // 处理元数据覆盖
                if (entry.Metadata != null)
                {
                    var metadataOverride = ConfigItemMetadataOverride.FromOverride(entry.Metadata);
                    if (metadataOverride != null)
                    {
                        mapping.MetadataOverrides[entry.ConfigName] = metadataOverride;
                        System.Diagnostics.Debug.WriteLine($"[ProjectMappingConfigLoader] Added metadata override for {entry.ConfigName}");
                    }
                }
            }

            return mapping;
        }

        /// <summary>
        /// 解析默认值（支持十进制和十六进制）
        /// </summary>
        private static uint ParseDefaultValue(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetUInt32();
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                var str = element.GetString();
                if (string.IsNullOrEmpty(str))
                    return 0;

                if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (uint.TryParse(str.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out var hexVal))
                        return hexVal;
                }

                if (uint.TryParse(str, out var decVal))
                    return decVal;
            }

            return 0;
        }

        /// <summary>
        /// 扫描 mappings 目录加载所有配置文件
        /// </summary>
        public static List<ProjectConfigMapping> LoadAllFromDirectory(string? directoryPath = null)
        {
            var mappings = new List<ProjectConfigMapping>();
            var dir = directoryPath ?? MappingsDirectory;

            if (!Directory.Exists(dir))
            {
                return mappings;
            }

            var jsonFiles = Directory.GetFiles(dir, "*.json");
            System.Diagnostics.Debug.WriteLine($"[ProjectMappingConfigLoader] Found {jsonFiles.Length} mapping files in {dir}");

            foreach (var file in jsonFiles)
            {
                var config = LoadFromFile(file);
                if (config != null)
                {
                    var mapping = ConvertToMapping(config);
                    if (mapping != null)
                    {
                        mappings.Add(mapping);
                        System.Diagnostics.Debug.WriteLine($"[ProjectMappingConfigLoader] Loaded mapping: {mapping.ProjectName} ({mapping.ConfigItemCount} items)");
                    }
                }
            }

            return mappings;
        }

        /// <summary>
        /// 为指定项目生成示例配置文件
        /// </summary>
        public static bool GenerateSampleConfig(ProjectType projectType, string? outputPath = null)
        {
            var mapping = ProjectConfigMappingDatabase.GetMapping(projectType);
            if (mapping == null)
                return false;

            var config = ConvertToJsonConfig(mapping);
            config.Description = $"项目 {mapping.ProjectName} 的配置映射（示例文件，可编辑后重新加载）";

            var filePath = outputPath ?? Path.Combine(MappingsDirectory, $"{projectType}.json");
            return SaveToFile(config, filePath);
        }
    }
}
