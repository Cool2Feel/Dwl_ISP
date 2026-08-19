using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ResBinManager.Models;

namespace ResBinManager.Core
{
    /// <summary>
    /// JSON 配置文件生成器
    /// 从解析结果生成标准的 JSON 配置文件
    /// </summary>
    public class ConfigJsonGenerator
    {
        /// <summary>
        /// 生成 JSON 配置文件
        /// </summary>
        /// <param name="parseResult">解析结果</param>
        /// <param name="outputPath">输出路径</param>
        /// <param name="projectType">项目类型（可选，默认从解析结果推断）</param>
        /// <returns>生成是否成功</returns>
        public bool Generate(ConfigSourceParser.ParseResult parseResult, string outputPath, string? projectType = null)
        {
            try
            {
                if (!parseResult.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigJsonGenerator] Parse result is not successful: {parseResult.ErrorMessage}");
                    return false;
                }

                // 创建 JSON 配置对象
                var jsonConfig = new ProjectMappingJsonConfig
                {
                    Version = "1.0",
                    ProjectType = projectType ?? parseResult.ProjectName,
                    ProjectName = FormatProjectName(parseResult.ProjectName),
                    Description = $"从 {Path.GetFileName(parseResult.SourceFilePath)} 自动生成的配置映射",
                    Mappings = new List<MappingEntry>(),
                    Features = new ProjectFeaturesInfo()
                };

                // 转换配置项
                foreach (var item in parseResult.ConfigItems)
                {
                    // 跳过被注释掉的配置项
                    if (item.IsCommented)
                        continue;

                    var entry = new MappingEntry
                    {
                        Index = item.Index,
                        ConfigName = item.Name,
                        DefaultValue = CreateJsonElement(item.DefaultValue),
                        Description = ExtractDescription(item)
                    };

                    jsonConfig.Mappings.Add(entry);
                }

                // 推断项目特性
                jsonConfig.Features = InferFeatures(parseResult.ConfigItems);

                // 序列化为 JSON
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                string jsonContent = JsonSerializer.Serialize(jsonConfig, options);

                // 写入文件
                File.WriteAllText(outputPath, jsonContent);

                System.Diagnostics.Debug.WriteLine($"[ConfigJsonGenerator] Generated JSON config: {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigJsonGenerator] Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从 config.h 和 config.c 自动生成 JSON 配置
        /// </summary>
        /// <param name="projectPath">项目目录路径</param>
        /// <param name="outputPath">输出 JSON 文件路径</param>
        /// <returns>生成是否成功</returns>
        public bool GenerateFromProject(string projectPath, string outputPath)
        {
            try
            {
                // 查找 config.h
                string? configHPath = ConfigHParser.FindConfigH(projectPath);
                if (string.IsNullOrEmpty(configHPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigJsonGenerator] config.h not found in {projectPath}");
                }

                // 查找 config.c
                string? configCPath = ConfigSourceParser.FindConfigC(projectPath);
                if (string.IsNullOrEmpty(configCPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigJsonGenerator] config.c not found in {projectPath}");
                    return false;
                }

                // 解析
                var parser = new ConfigSourceParser();
                var parseResult = parser.Parse(configCPath, configHPath);

                if (!parseResult.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigJsonGenerator] Parse failed: {parseResult.ErrorMessage}");
                    return false;
                }

                // 生成 JSON
                return Generate(parseResult, outputPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigJsonGenerator] Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 创建 JSON 元素
        /// </summary>
        private JsonElement CreateJsonElement(uint value)
        {
            // 如果是 R_ID_TYPE_STR 格式，使用十六进制字符串
            if ((value & 0xFF000000) == FirmwareConstants.R_ID_TYPE_STR)
            {
                string hexValue = $"0x{value:X8}";
                return JsonSerializer.SerializeToElement(hexValue);
            }
            // 否则使用数值
            else
            {
                return JsonSerializer.SerializeToElement(value);
            }
        }

        /// <summary>
        /// 从配置项提取描述信息
        /// </summary>
        private string ExtractDescription(ConfigSourceParser.ConfigItemInfo item)
        {
            // 如果有注释，使用注释作为描述
            if (!string.IsNullOrEmpty(item.Comment))
            {
                // 清理注释
                string comment = item.Comment.Trim();
                if (comment.StartsWith("//"))
                    comment = comment.Substring(2).Trim();
                return comment;
            }

            // 否则从配置项名称推断
            return InferDescriptionFromName(item.Name);
        }

        /// <summary>
        /// 从配置项名称推断描述
        /// </summary>
        private string InferDescriptionFromName(string configName)
        {
            // 移除 CONFIG_ID_ 前缀
            string name = configName.Replace("CONFIG_ID_", "");

            // 常见配置项的中文描述
            var descriptions = new Dictionary<string, string>
            {
                { "YEAR", "年份" },
                { "MONTH", "月份" },
                { "MDAY", "日期" },
                { "WDAY", "星期" },
                { "HOUR", "小时" },
                { "MIN", "分钟" },
                { "SEC", "秒" },
                { "LANGUAGE", "语言" },
                { "AUTOOFF", "自动关机" },
                { "SCREENSAVE", "屏保" },
                { "FREQUNCY", "电源频率" },
                { "ROTATE", "旋转" },
                { "FILLIGHT", "补光灯" },
                { "RESOLUTION", "视频分辨率" },
                { "TIMESTAMP", "时间水印" },
                { "MOTIONDECTION", "移动侦测" },
                { "PARKMODE", "停车监控" },
                { "GSENSOR", "重力感应" },
                { "GSENSORMODE", "重力感应模式" },
                { "KEYSOUND", "按键音" },
                { "IR_LED", "红外灯" },
                { "LOOPTIME", "循环录像" },
                { "AUDIOREC", "录音" },
                { "EV", "曝光补偿" },
                { "WBLANCE", "白平衡" },
                { "PRESLUTION", "拍照分辨率" },
                { "PFASTVIEW", "快速预览" },
                { "PTIMESTRAMP", "拍照时间戳" },
                { "PEV", "拍照曝光补偿" },
                { "THUMBNAIL", "缩略图" },
                { "TIMEPHOTO", "定时拍照" },
                { "MOREPHOTO", "连拍" },
                { "FORMAT", "格式化" },
                { "DEFUALT", "恢复默认" },
                { "BAT_OLD", "电池电量" },
                { "BAT_CHECK_FLAG", "电池检测标志" },
                { "DEVICE_ID1", "设备ID" },
                { "PRINTER_EN", "打印机使能" },
                { "PRINTER_DENSITY", "打印浓度" },
                { "PRINTER_MODE", "打印模式" },
                { "PRINTER_NEARFAR", "打印距离" },
                { "VOLUME", "音量" },
                { "ISP_FILTER", "ISP滤镜" },
            };

            if (descriptions.ContainsKey(name))
                return descriptions[name];

            // 返回原始名称
            return name;
        }

        /// <summary>
        /// 格式化项目名称
        /// </summary>
        private string FormatProjectName(string projectName)
        {
            // 将下划线转换为空格，首字母大写
            var parts = projectName.Split('_');
            return string.Join(" ", parts.Select(p => 
                p.Length > 0 ? char.ToUpper(p[0]) + p.Substring(1).ToLower() : p));
        }

        /// <summary>
        /// 推断项目特性
        /// </summary>
        private ProjectFeaturesInfo InferFeatures(List<ConfigSourceParser.ConfigItemInfo> items)
        {
            var features = new ProjectFeaturesInfo();

            var configNames = items.Select(x => x.Name).ToHashSet();

            // 只设置 ProjectFeaturesInfo 中存在的属性
            features.HasLcdBright = configNames.Contains("CONFIG_ID_LCD_BRIGHT");
            features.HasTimePhoto = configNames.Contains("CONFIG_ID_TIMEPHOTO");
            features.HasParkMode = configNames.Contains("CONFIG_ID_PARKMODE");
            features.HasGSensor = configNames.Any(n => n.Contains("GSENSOR"));
            features.HasIrLed = configNames.Contains("CONFIG_ID_IR_LED");
            features.HasFillLight = configNames.Contains("CONFIG_ID_FILLIGHT");
            features.HasRotate = configNames.Contains("CONFIG_ID_ROTATE");
            features.ConfigItemCount = items.Count;

            return features;
        }
    }
}
