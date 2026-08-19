using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ResBinManager.Models;

namespace ResBinManager.Core
{
    /// <summary>
    /// JSON模板配置加载器
    /// </summary>
    public static class JsonTemplateLoader
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// 从JSON文件加载模板配置
        /// </summary>
        public static ConfigTemplate LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"[JsonTemplateLoader] File not found: {filePath}");
                return null;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                return LoadFromJson(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JsonTemplateLoader] Load error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从JSON字符串加载模板配置
        /// </summary>
        public static ConfigTemplate LoadFromJson(string json)
        {
            try
            {
                var jsonConfig = JsonSerializer.Deserialize<TemplateJsonConfig>(json, _jsonOptions);
                if (jsonConfig == null)
                {
                    return null;
                }

                return ConvertToTemplate(jsonConfig);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JsonTemplateLoader] Parse error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 将JSON配置转换为ConfigTemplate
        /// </summary>
        private static ConfigTemplate ConvertToTemplate(TemplateJsonConfig jsonConfig)
        {
            var template = new ConfigTemplate
            {
                Id = jsonConfig.Info.Id,
                Name = jsonConfig.Info.Name,
                Description = jsonConfig.Info.Description,
                BaseValues = new Dictionary<string, uint>(jsonConfig.BaseValues),
                ProjectOverrides = new Dictionary<string, Dictionary<string, uint>>()
            };

            // 转换项目差异覆盖
            foreach (var kvp in jsonConfig.ProjectOverrides)
            {
                template.ProjectOverrides[kvp.Key] = new Dictionary<string, uint>(kvp.Value);
            }

            foreach (var kvp in jsonConfig.FeatureRules)
            {
                template.FeatureRules[kvp.Key] = new FeatureRule
                {
                    Description = kvp.Value.Description,
                    ConditionExpression = kvp.Value.Condition,
                    TrueValue = kvp.Value.TrueValue,
                    FalseValue = kvp.Value.FalseValue
                };
            }

            return template;
        }

        /// <summary>
        /// 将ConfigTemplate转换为JSON配置
        /// </summary>
        public static TemplateJsonConfig ConvertToJsonConfig(ConfigTemplate template)
        {
            var jsonConfig = new TemplateJsonConfig
            {
                Info = new TemplateInfo
                {
                    Id = template.Id,
                    Name = template.Name,
                    Description = template.Description,
                    Version = "1.0"
                },
                BaseValues = new Dictionary<string, uint>(template.BaseValues),
                ProjectOverrides = new Dictionary<string, Dictionary<string, uint>>()
            };

            // 转换项目差异覆盖
            foreach (var kvp in template.ProjectOverrides)
            {
                jsonConfig.ProjectOverrides[kvp.Key] = new Dictionary<string, uint>(kvp.Value);
            }

            foreach (var kvp in template.FeatureRules)
            {
                jsonConfig.FeatureRules[kvp.Key] = new FeatureRuleJson
                {
                    Description = kvp.Value.Description,
                    Condition = kvp.Value.ConditionExpression ?? string.Empty,
                    TrueValue = kvp.Value.TrueValue,
                    FalseValue = kvp.Value.FalseValue
                };
            }

            return jsonConfig;
        }

        /// <summary>
        /// 将模板保存为JSON文件
        /// </summary>
        public static bool SaveToFile(ConfigTemplate template, string filePath)
        {
            try
            {
                var jsonConfig = ConvertToJsonConfig(template);
                var json = JsonSerializer.Serialize(jsonConfig, _jsonOptions);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JsonTemplateLoader] Save error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将模板转换为JSON字符串
        /// </summary>
        public static string ToJson(ConfigTemplate template)
        {
            var jsonConfig = ConvertToJsonConfig(template);
            return JsonSerializer.Serialize(jsonConfig, _jsonOptions);
        }

        /// <summary>
        /// 扫描目录加载所有模板
        /// </summary>
        public static List<ConfigTemplate> LoadAllFromDirectory(string directoryPath)
        {
            var templates = new List<ConfigTemplate>();

            if (!Directory.Exists(directoryPath))
            {
                return templates;
            }

            var jsonFiles = Directory.GetFiles(directoryPath, "*.json");

            foreach (var file in jsonFiles)
            {
                var template = LoadFromFile(file);
                if (template != null)
                {
                    templates.Add(template);
                }
            }

            return templates;
        }
    }
}
