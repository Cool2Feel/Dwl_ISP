using System;
using System.Collections.Generic;
using System.Linq;
using ResBinManager.Models;

namespace ResBinManager.Core
{
    /// <summary>
    /// 项目自动探测器
    /// 通过分析固件配置数据的特征，自动推测可能的项目类型
    /// </summary>
    public static class ProjectDetector
    {
        /// <summary>
        /// 探测结果
        /// </summary>
        public class DetectionResult
        {
            /// <summary>
            /// 推测的项目类型
            /// </summary>
            public ProjectType ProjectType { get; set; }

            /// <summary>
            /// 匹配度 (0.0 ~ 1.0)
            /// </summary>
            public double MatchScore { get; set; }

            /// <summary>
            /// 匹配依据说明
            /// </summary>
            public string Reason { get; set; } = string.Empty;

            /// <summary>
            /// 固件特征摘要
            /// </summary>
            public FirmwareFeatureSummary Features { get; set; } = new();
        }

        /// <summary>
        /// 固件特征摘要
        /// </summary>
        public class FirmwareFeatureSummary
        {
            /// <summary>
            /// 配置项总数
            /// </summary>
            public int ConfigItemCount { get; set; }

            /// <summary>
            /// R_ID_TYPE_STR 格式的值数量
            /// </summary>
            public int StringTypeCount { get; set; }

            /// <summary>
            /// 数值类型数量
            /// </summary>
            public int NumericTypeCount { get; set; }

            /// <summary>
            /// 类型分布统计
            /// </summary>
            public Dictionary<ConfigItemType, int> TypeDistribution { get; set; } = new();

            /// <summary>
            /// 特征配置项（如是否有 LCD_BRIGHT、TIMEPHOTO 等）
            /// </summary>
            public HashSet<string> FeatureConfigs { get; set; } = new();

            /// <summary>
            /// 配置值模式指纹（用于相似度匹配）
            /// </summary>
            public string ValuePattern { get; set; } = string.Empty;
        }

        /// <summary>
        /// 探测固件可能的项目类型
        /// </summary>
        /// <param name="configData">固件配置数据</param>
        /// <returns>探测结果列表（按匹配度降序）</returns>
        public static List<DetectionResult> DetectProject(FirmwareConfigData configData)
        {
            var results = new List<DetectionResult>();

            // 1. 提取固件特征
            var features = ExtractFirmwareFeatures(configData);

            // 2. 与所有已知项目模板进行匹配
            var allMappings = ProjectConfigMappingDatabase.GetAllMappings();
            foreach (var kvp in allMappings)
            {
                var projectType = kvp.Key;
                var mapping = kvp.Value;

                var matchResult = MatchWithTemplate(features, mapping);
                if (matchResult != null)
                {
                    results.Add(matchResult);
                }
            }

            // 3. 按匹配度降序排序
            results.Sort((a, b) => b.MatchScore.CompareTo(a.MatchScore));

            return results;
        }

        /// <summary>
        /// 提取固件特征摘要
        /// </summary>
        private static FirmwareFeatureSummary ExtractFirmwareFeatures(FirmwareConfigData configData)
        {
            var features = new FirmwareFeatureSummary();

            if (configData == null || configData.Flags == null)
                return features;

            // 统计配置项数量
            features.ConfigItemCount = configData.ActiveConfigCount;

            // 使用 UniversalValueDecoder 分析每个配置值
            for (int i = 0; i < configData.ActiveConfigCount && i < configData.Flags.Length; i++)
            {
                uint value = configData.Flags[i];
                if (value == 0 || value == 0xFFFFFFFF)
                    continue;

                var decodeResult = UniversalValueDecoder.Decode(value);

                // 统计类型分布
                if (!features.TypeDistribution.ContainsKey(decodeResult.InferredType))
                    features.TypeDistribution[decodeResult.InferredType] = 0;
                features.TypeDistribution[decodeResult.InferredType]++;

                // 统计字符串类型和数值类型
                if (decodeResult.IsStringType)
                    features.StringTypeCount++;
                else if (decodeResult.InferredType == ConfigItemType.Numeric)
                    features.NumericTypeCount++;
            }

            // 识别特征配置项
            // 通过分析特定索引位置的值模式来推断
            if (configData.Mapping != null)
            {
                foreach (var configName in configData.Mapping.IndexToConfigName.Values)
                {
                    features.FeatureConfigs.Add(configName);
                }
            }

            // 生成值模式指纹
            features.ValuePattern = GenerateValuePattern(configData);

            return features;
        }

        /// <summary>
        /// 生成配置值模式指纹
        /// 用于快速比较两个固件的相似性
        /// </summary>
        private static string GenerateValuePattern(FirmwareConfigData configData)
        {
            if (configData == null || configData.Flags == null)
                return string.Empty;

            var pattern = new List<string>();

            // 取前 20 个配置项作为指纹
            int count = Math.Min(20, configData.ActiveConfigCount);
            for (int i = 0; i < count && i < configData.Flags.Length; i++)
            {
                uint value = configData.Flags[i];
                if (value == 0 || value == 0xFFFFFFFF)
                {
                    pattern.Add("X"); // 空白
                    continue;
                }

                var decodeResult = UniversalValueDecoder.Decode(value);
                
                // 使用类型缩写 + 偏移量作为指纹元素
                if (decodeResult.IsStringType)
                {
                    string typeAbbr = decodeResult.InferredType.ToString().Substring(0, 1).ToUpper();
                    pattern.Add($"{typeAbbr}{decodeResult.Offset:X2}");
                }
                else
                {
                    pattern.Add($"N{value:X4}");
                }
            }

            return string.Join("-", pattern);
        }

        /// <summary>
        /// 将固件特征与项目模板进行匹配
        /// </summary>
        private static DetectionResult? MatchWithTemplate(FirmwareFeatureSummary features, ProjectConfigMapping template)
        {
            if (features == null || template == null)
                return null;

            var result = new DetectionResult
            {
                ProjectType = template.ProjectType,
                Features = features
            };

            double score = 0.0;
            var reasons = new List<string>();

            // 1. 配置项数量匹配度（权重 30%）
            int templateCount = template.ConfigItemCount;
            int firmwareCount = features.ConfigItemCount;
            double countDiff = Math.Abs(templateCount - firmwareCount);
            double countScore = Math.Max(0, 1.0 - countDiff / 10.0); // 每差1项扣0.1分
            score += countScore * 0.3;
            if (countScore > 0.8)
                reasons.Add($"配置项数量匹配 ({firmwareCount} vs {templateCount})");

            // 2. 特征配置项匹配度（权重 40%）
            var templateFeatures = new HashSet<string>(template.IndexToConfigName.Values);
            var firmwareFeatures = features.FeatureConfigs;
            
            int matchCount = templateFeatures.Intersect(firmwareFeatures).Count();
            int totalFeatures = templateFeatures.Union(firmwareFeatures).Count();
            double featureScore = totalFeatures > 0 ? (double)matchCount / totalFeatures : 0;
            score += featureScore * 0.4;
            if (featureScore > 0.8)
                reasons.Add($"特征配置项高度匹配 ({matchCount}/{totalFeatures})");

            // 3. 类型分布匹配度（权重 20%）
            // 比较字符串类型 vs 数值类型的比例
            double templateStringRatio = 0.7; // 典型项目约 70% 是字符串类型
            double firmwareStringRatio = firmwareCount > 0 ? (double)features.StringTypeCount / firmwareCount : 0;
            double ratioDiff = Math.Abs(templateStringRatio - firmwareStringRatio);
            double typeScore = Math.Max(0, 1.0 - ratioDiff * 2); // 比例差越大扣分越多
            score += typeScore * 0.2;
            if (typeScore > 0.8)
                reasons.Add($"类型分布相似 (字符串占比 {firmwareStringRatio:P0})");

            // 4. 值模式指纹相似度（权重 10%）
            // 这里简化处理，实际可以计算编辑距离或 Jaccard 相似度
            double patternScore = 0.5; // 默认中等分数
            score += patternScore * 0.1;

            result.MatchScore = score;
            result.Reason = string.Join("; ", reasons);

            // 只有匹配度超过 0.5 才返回结果
            if (score < 0.5)
                return null;

            return result;
        }

        /// <summary>
        /// 生成固件分析报告
        /// </summary>
        public static string GenerateAnalysisReport(FirmwareConfigData configData)
        {
            if (configData == null)
                return "无配置数据";

            var features = ExtractFirmwareFeatures(configData);
            var detectionResults = DetectProject(configData);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== 固件分析报告 ===");
            sb.AppendLine();
            
            sb.AppendLine("【基本特征】");
            sb.AppendLine($"  配置项总数: {features.ConfigItemCount}");
            sb.AppendLine($"  字符串类型: {features.StringTypeCount}");
            sb.AppendLine($"  数值类型: {features.NumericTypeCount}");
            sb.AppendLine();

            sb.AppendLine("【类型分布】");
            foreach (var kvp in features.TypeDistribution.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value} 项");
            }
            sb.AppendLine();

            sb.AppendLine("【特征配置项】");
            if (features.FeatureConfigs.Count > 0)
            {
                foreach (var config in features.FeatureConfigs.Take(10))
                {
                    sb.AppendLine($"  - {config}");
                }
                if (features.FeatureConfigs.Count > 10)
                    sb.AppendLine($"  ... 还有 {features.FeatureConfigs.Count - 10} 项");
            }
            else
            {
                sb.AppendLine("  (无)");
            }
            sb.AppendLine();

            sb.AppendLine("【项目探测结果】");
            if (detectionResults.Count > 0)
            {
                for (int i = 0; i < Math.Min(3, detectionResults.Count); i++)
                {
                    var result = detectionResults[i];
                    sb.AppendLine($"  {i + 1}. {result.ProjectType} (匹配度: {result.MatchScore:P0})");
                    if (!string.IsNullOrEmpty(result.Reason))
                        sb.AppendLine($"     依据: {result.Reason}");
                }
            }
            else
            {
                sb.AppendLine("  未找到匹配的项目类型");
            }

            return sb.ToString();
        }
    }
}
