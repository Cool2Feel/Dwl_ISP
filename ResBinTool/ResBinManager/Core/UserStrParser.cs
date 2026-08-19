using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ResBinManager.Core
{
    public class UserStrParser
    {
        public class ParseResult
        {
            public Dictionary<string, uint> StringConstants { get; set; } = new();
            public Dictionary<string, uint> CfgMappings { get; set; } = new();
            public bool Success { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
            public uint RIdTypeStrBase { get; set; } = FirmwareConstants.R_ID_TYPE_STR;
        }

        public ParseResult Parse(string userStrHPath)
        {
            var result = new ParseResult();

            try
            {
                if (!File.Exists(userStrHPath))
                {
                    result.ErrorMessage = $"文件不存在: {userStrHPath}";
                    return result;
                }

                string content = File.ReadAllText(userStrHPath);

                result.RIdTypeStrBase = ParseRIdTypeStrBase(content);
                ParseRIdStrEnums(content, result);
                ParseCfgEnums(content, result);

                result.Success = result.StringConstants.Count > 0;

                if (!result.Success)
                {
                    result.ErrorMessage = "未找到任何 R_ID_STR_* 定义";
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[UserStrParser] Parsed {result.StringConstants.Count} string constants");
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"解析失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[UserStrParser] Error: {ex.Message}");
            }

            return result;
        }

        private uint ParseRIdTypeStrBase(string content)
        {
            var definePattern = new Regex(@"#define\s+R_ID_TYPE_STR\s+([0-9a-fA-FxX]+)");
            var match = definePattern.Match(content);
            if (match.Success)
            {
                string valueStr = match.Groups[1].Value;
                if (valueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (uint.TryParse(valueStr.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint hexValue))
                        return hexValue;
                }
                else if (uint.TryParse(valueStr, out uint decValue))
                {
                    return decValue;
                }
            }
            return FirmwareConstants.R_ID_TYPE_STR;
        }

        private void ParseRIdStrEnums(string content, ParseResult result)
        {
            var enumPattern = new Regex(@"enum\s*\{([^}]+)\}", RegexOptions.Singleline);
            var matches = enumPattern.Matches(content);

            foreach (Match enumMatch in matches)
            {
                string enumContent = enumMatch.Groups[1].Value;
                if (!enumContent.Contains("R_ID_STR_"))
                    continue;

                var itemPattern = new Regex(@"(R_ID_STR_\w+)\s*(=\s*([^,]+))?,?");
                var itemMatches = itemPattern.Matches(enumContent);

                uint currentValue = result.RIdTypeStrBase;
                uint? lastExplicitValue = null;

                foreach (Match itemMatch in itemMatches)
                {
                    string name = itemMatch.Groups[1].Value.Trim();
                    if (string.IsNullOrEmpty(name))
                        continue;

                    string valueExpr = itemMatch.Groups[3].Value.Trim();

                    if (!string.IsNullOrEmpty(valueExpr))
                    {
                        if (valueExpr.Equals("R_ID_TYPE_STR", StringComparison.OrdinalIgnoreCase))
                        {
                            currentValue = result.RIdTypeStrBase;
                        }
                        else if (valueExpr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            if (uint.TryParse(valueExpr.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint hexValue))
                                currentValue = hexValue;
                        }
                        else if (uint.TryParse(valueExpr, out uint decValue))
                        {
                            currentValue = decValue;
                        }
                        else if (result.StringConstants.TryGetValue(valueExpr, out uint referencedValue))
                        {
                            currentValue = referencedValue;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[UserStrParser] Warning: Cannot resolve {name} = {valueExpr}");
                            continue;
                        }
                        lastExplicitValue = currentValue;
                    }
                    else if (lastExplicitValue.HasValue)
                    {
                        currentValue = lastExplicitValue.Value + 1;
                        lastExplicitValue = currentValue;
                    }

                    result.StringConstants[name] = currentValue;
                }
            }
        }

        private void ParseCfgEnums(string content, ParseResult result)
        {
            var enumPattern = new Regex(@"enum\s*//?\s*configure\s*id\s*table[^}]*\{([^}]+)\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var match = enumPattern.Match(content);

            if (match.Success)
            {
                string enumContent = match.Groups[1].Value;
                var itemPattern = new Regex(@"(CFG_\w+)\s*(=\s*(\d+))?,?");
                var itemMatches = itemPattern.Matches(enumContent);

                uint currentValue = 0;

                foreach (Match itemMatch in itemMatches)
                {
                    string name = itemMatch.Groups[1].Value.Trim();
                    if (string.IsNullOrEmpty(name))
                        continue;

                    string valueStr = itemMatch.Groups[3].Value.Trim();
                    if (!string.IsNullOrEmpty(valueStr))
                    {
                        if (uint.TryParse(valueStr, out uint decValue))
                            currentValue = decValue;
                    }

                    result.CfgMappings[name] = currentValue;
                    currentValue++;
                }
            }
        }

        public static string? FindUserStrH(string projectPath)
        {
            string[] searchPaths = new[]
            {
                Path.Combine(projectPath, "user_str.h"),
                Path.Combine(projectPath, "resource", "user_str.h"),
                Path.Combine(projectPath, "res", "user_str.h"),
                Path.Combine(projectPath, "include", "user_str.h"),
                Path.Combine(projectPath, "src", "user_str.h"),
                Path.Combine(projectPath, "firmware", "user_str.h"),
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            try
            {
                var files = Directory.GetFiles(projectPath, "user_str.h", SearchOption.AllDirectories);
                if (files.Length > 0)
                    return files[0];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserStrParser] Error searching user_str.h: {ex.Message}");
            }

            return null;
        }

        public static uint ResolveStringConstant(Dictionary<string, uint> constants, string constantName)
        {
            if (constants.TryGetValue(constantName, out uint value))
                return value;

            return ConfigSourceParser.ParseRIdStrConstantStatic(constantName);
        }
    }
}