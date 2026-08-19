using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ResBinManager.Core
{
    /// <summary>
    /// config.h 文件解析器
    /// 解析 CONFIG_ID_* 枚举定义，提取配置项索引
    /// </summary>
    public class ConfigHParser
    {
        /// <summary>
        /// 解析结果
        /// </summary>
        public class ParseResult
        {
            /// <summary>
            /// 配置项名称到索引的映射
            /// </summary>
            public Dictionary<string, int> ConfigIndexes { get; set; } = new();

            /// <summary>
            /// 解析是否成功
            /// </summary>
            public bool Success { get; set; }

            /// <summary>
            /// 错误信息
            /// </summary>
            public string ErrorMessage { get; set; } = string.Empty;

            /// <summary>
            /// 最大配置项索引
            /// </summary>
            public int MaxIndex { get; set; }
        }

        /// <summary>
        /// 解析 config.h 文件
        /// </summary>
        /// <param name="configHPath">config.h 文件路径</param>
        /// <returns>解析结果</returns>
        public ParseResult Parse(string configHPath)
        {
            var result = new ParseResult();

            try
            {
                if (!File.Exists(configHPath))
                {
                    result.ErrorMessage = $"文件不存在: {configHPath}";
                    return result;
                }

                string content = File.ReadAllText(configHPath);

                // 匹配枚举定义
                // 格式: CONFIG_ID_XXX = 0, CONFIG_ID_YYY = 1, ...
                // 或者: CONFIG_ID_XXX, CONFIG_ID_YYY, ... (自动递增)
                
                // 先尝试匹配带显式赋值的枚举（忽略行尾注释和逗号）
                var explicitPattern = new Regex(@"^\s*(CONFIG_ID_\w+)\s*=\s*(\d+)\s*,?\s*(//.*)?$", RegexOptions.Multiline);
                var explicitMatches = explicitPattern.Matches(content);

                // 再尝试匹配不带赋值的枚举（自动递增，忽略行尾注释）
                var implicitPattern = new Regex(@"^\s*(CONFIG_ID_\w+)\s*,?\s*(//.*)?$", RegexOptions.Multiline);
                var implicitMatches = implicitPattern.Matches(content);

                int currentIndex = 0;

                // 处理显式赋值
                foreach (Match match in explicitMatches)
                {
                    string configName = match.Groups[1].Value;
                    int index = int.Parse(match.Groups[2].Value);
                    
                    result.ConfigIndexes[configName] = index;
                    currentIndex = index + 1;
                    
                    if (index > result.MaxIndex)
                        result.MaxIndex = index;
                }

                // 处理隐式递增
                foreach (Match match in implicitMatches)
                {
                    string configName = match.Groups[1].Value;
                    
                    // 跳过已处理的
                    if (result.ConfigIndexes.ContainsKey(configName))
                        continue;

                    result.ConfigIndexes[configName] = currentIndex;
                    
                    if (currentIndex > result.MaxIndex)
                        result.MaxIndex = currentIndex;
                    
                    currentIndex++;
                }

                result.Success = result.ConfigIndexes.Count > 0;
                
                if (!result.Success)
                {
                    result.ErrorMessage = "未找到任何 CONFIG_ID_* 定义";
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigHParser] Parsed {result.ConfigIndexes.Count} config items, max index: {result.MaxIndex}");
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"解析失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[ConfigHParser] Error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 从项目目录自动查找 config.h
        /// </summary>
        /// <param name="projectPath">项目根目录</param>
        /// <returns>config.h 文件路径，未找到返回 null</returns>
        public static string? FindConfigH(string projectPath)
        {
            // 常见的 config.h 位置
            string[] searchPaths = new[]
            {
                Path.Combine(projectPath, "config.h"),
                Path.Combine(projectPath, "include", "config.h"),
                Path.Combine(projectPath, "src", "config.h"),
                Path.Combine(projectPath, "firmware", "config.h"),
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            // 递归搜索
            try
            {
                var files = Directory.GetFiles(projectPath, "config.h", SearchOption.AllDirectories);
                if (files.Length > 0)
                    return files[0];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigHParser] Error searching config.h: {ex.Message}");
            }

            return null;
        }
    }
}
