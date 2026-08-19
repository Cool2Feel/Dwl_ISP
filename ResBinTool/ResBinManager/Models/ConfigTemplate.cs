using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ResBinManager.Models
{
    /// <summary>
    /// 项目类型枚举
    /// </summary>
    public enum ProjectType
    {
        Unknown,
        JT529X,
        DC508J,
        GX_T317BV200,
        HM020F,
        MKL_CM5,
        MKL_DM15,
        JRX_JT529X,
        JRX_AX329X
    }

    /// <summary>
    /// 配置模板ID枚举（保留向后兼容性）
    /// </summary>
    public enum ConfigTemplateId
    {
        Default,
        JT529X,
        DC508J,
        GX_T317BV200,
        HM020F,
        MKL_CM5,
        MKL_DM15,
        JRX_JT529X,
        JRX_AX329X
    }

    /// <summary>
    /// 配置模板（混合架构：基础模板 + 差异覆盖）
    /// </summary>
    public class ConfigTemplate
    {
        /// <summary>
        /// 模板唯一标识
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 模板显示名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 模板描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 基础配置值（所有项目通用的默认值）
        /// </summary>
        public Dictionary<string, uint> BaseValues { get; set; } = new();

        /// <summary>
        /// 默认配置值（向后兼容的别名，指向BaseValues）
        /// </summary>
        [JsonIgnore]
        public Dictionary<ConfigId, uint> DefaultValues
        {
            get
            {
                var result = new Dictionary<ConfigId, uint>();
                foreach (var kvp in BaseValues)
                {
                    var key = kvp.Key;
                    var value = kvp.Value;
                    if (Enum.TryParse<ConfigId>(key, out var configId))
                    {
                        result[configId] = value;
                    }
                }
                return result;
            }
            set
            {
                BaseValues.Clear();
                foreach (var kvp in value)
                {
                    BaseValues[kvp.Key.ToString()] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// 项目差异覆盖配置
        /// Key: 项目类型名称, Value: 该项目的差异配置
        /// </summary>
        public Dictionary<string, Dictionary<string, uint>> ProjectOverrides { get; set; } = new();

        /// <summary>
        /// 特征规则配置
        /// Key: 配置项名称, Value: 特征规则表达式
        /// </summary>
        public Dictionary<string, FeatureRule> FeatureRules { get; set; } = new();

        /// <summary>
        /// 获取指定项目的完整配置值（基础值 + 差异串 + 特征规则）
        /// </summary>
        public Dictionary<string, uint> GetValuesForProject(string projectType, ProjectConfigFeatures features = null)
        {
            // 1. 从基础值开始
            var result = new Dictionary<string, uint>(BaseValues);

            // 2. 应用特征规则（动态计算）
            if (features != null)
            {
                foreach (var kvp in FeatureRules)
                {
                    var configName = kvp.Key;
                    var rule = kvp.Value;
                    result[configName] = rule.Evaluate(features);
                }
            }

            // 3. 应用项目差异覆盖（最高优先级）
            if (ProjectOverrides.TryGetValue(projectType, out var overrides))
                {
                    foreach (var kvp in overrides)
                    {
                        result[kvp.Key] = kvp.Value;
                    }
                }

            return result;
        }

        /// <summary>
        /// 获取指定项目的完整配置值（使用ConfigId枚举）
        /// </summary>
        public Dictionary<ConfigId, uint> GetValuesForProject(ProjectType projectType, ProjectConfigFeatures features = null)
        {
            var stringValues = GetValuesForProject(projectType.ToString(), features);
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
        /// 添加或更新项目差异覆盖项
        /// </summary>
        public void SetProjectOverride(string projectType, Dictionary<string, uint> overrides)
        {
            ProjectOverrides[projectType] = new Dictionary<string, uint>(overrides);
        }

        /// <summary>
        /// 添加特征规则
        /// </summary>
        public void AddFeatureRule(string configName, Func<ProjectConfigFeatures, uint> evaluator, string description = "")
        {
            FeatureRules[configName] = new FeatureRule
            {
                Evaluator = evaluator,
                Description = description
            };
        }
    }

    /// <summary>
    /// 特征规则（用于动态计算配置值）
    /// </summary>
    public class FeatureRule
    {
        /// <summary>
        /// 规则描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 规则求值函数（JSON序列化时忽略）
        /// </summary>
        [JsonIgnore]
        public Func<ProjectConfigFeatures, uint> Evaluator { get; set; } = _ => 0;

        /// <summary>
        /// 规则条件表达式（JSON序列化格式）
        /// 格式: "feature:value" 或 "feature:true/false"
        /// </summary>
        public string ConditionExpression { get; set; }

        /// <summary>
        /// 条件为真时的值
        /// </summary>
        public uint TrueValue { get; set; }

        /// <summary>
        /// 条件为假时的值
        /// </summary>
        public uint FalseValue { get; set; }

        /// <summary>
        /// 从JSON配置求值
        /// </summary>
        public uint Evaluate(ProjectConfigFeatures features)
        {
            // 如果有直接求值函数，优先使用
            if (Evaluator != null)
            {
                return Evaluator(features);
            }

            // 规则从JSON表达式求值
            if (string.IsNullOrEmpty(ConditionExpression))
            {
                return TrueValue;
            }

            var parts = ConditionExpression.Split(':');
            if (parts.Length != 2)
            {
                return TrueValue;
            }

            var featureName = parts[0];
            var expectedValue = parts[1];

            bool conditionMet = CheckFeatureCondition(features, featureName, expectedValue);

            return conditionMet ? TrueValue : FalseValue;
        }

        private static bool CheckFeatureCondition(ProjectConfigFeatures features, string featureName, string expectedValue)
        {
            var featureType = typeof(ProjectConfigFeatures);
            var property = featureType.GetProperty(featureName);

            if (property == null)
            {
                return false;
            }

            var actualValue = property.GetValue(features);

            if (actualValue is bool boolValue)
            {
                return boolValue == (expectedValue.ToLower() == "true");
            }

            if (actualValue is int intValue)
            {
                return intValue.ToString() == expectedValue;
            }

            return actualValue.ToString() == expectedValue;
        }
    }

    /// <summary>
    /// JSON配置文件结构
    /// </summary>
    public class TemplateJsonConfig
    {
        /// <summary>
        /// 模板基本信息
        /// </summary>
        public TemplateInfo Info { get; set; } = new();

        /// <summary>
        /// 基础配置项
        /// </summary>
        public Dictionary<string, uint> BaseValues { get; set; } = new();

        /// <summary>
        /// 项目差异覆盖
        /// </summary>
        public Dictionary<string, Dictionary<string, uint>> ProjectOverrides { get; set; } = new();

        /// <summary>
        /// 特征规则
        /// </summary>
        public Dictionary<string, FeatureRuleJson> FeatureRules { get; set; } = new();
    }

    /// <summary>
    /// 模板基本信息
    /// </summary>
    public class TemplateInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0";
    }

    /// <summary>
    /// JSON格式的特征规则
    /// </summary>
    public class FeatureRuleJson
    {
        /// <summary>
        /// 规则描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 条件表达式，格式: "feature:value"
        /// </summary>
        public string Condition { get; set; } = string.Empty;

        /// <summary>
        /// 条件为真时的值
        /// </summary>
        public uint TrueValue { get; set; }

        /// <summary>
        /// 条件为假时的值
        /// </summary>
        public uint FalseValue { get; set; }
    }
}
