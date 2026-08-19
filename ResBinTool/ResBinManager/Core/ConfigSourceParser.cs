using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ResBinManager.Core
{
    /// <summary>
    /// config.c 源码解析器
    /// 从 C 源码中提取配置项信息，自动生成 JSON 配置文件
    /// </summary>
    public class ConfigSourceParser
    {
        /// <summary>
        /// 解析结果
        /// </summary>
        public class ParseResult
        {
            /// <summary>
            /// 提取的配置项列表
            /// </summary>
            public List<ConfigItemInfo> ConfigItems { get; set; } = new();

            /// <summary>
            /// 解析是否成功
            /// </summary>
            public bool Success { get; set; }

            /// <summary>
            /// 错误信息
            /// </summary>
            public string ErrorMessage { get; set; } = string.Empty;

            /// <summary>
            /// 项目名称（从目录名推断）
            /// </summary>
            public string ProjectName { get; set; } = string.Empty;

            /// <summary>
            /// 源文件路径
            /// </summary>
            public string SourceFilePath { get; set; } = string.Empty;

            /// <summary>
            /// 从 user_str.h 解析的字符串常量映射
            /// </summary>
            public Dictionary<string, uint> StringConstants { get; set; } = new();

            /// <summary>
            /// R_ID_TYPE_STR 基址值（从 user_str.h 解析）
            /// </summary>
            public uint RIdTypeStrBase { get; set; } = FirmwareConstants.R_ID_TYPE_STR;

            /// <summary>
            /// 从 RES.H 解析的资源定义映射（资源 Id -> 资源名称）
            /// </summary>
            public Dictionary<uint, string> ResourceDefinitions { get; set; } = new();

            /// <summary>
            /// RES.H 源文件路径（若已解析）
            /// </summary>
            public string? ResHFilePath { get; set; }
        }

        /// <summary>
        /// 配置项信息
        /// </summary>
        public class ConfigItemInfo
        {
            /// <summary>
            /// 配置项名称（如 CONFIG_ID_LANGUAGE）
            /// </summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>
            /// 配置项索引
            /// </summary>
            public int Index { get; set; }

            /// <summary>
            /// 默认值表达式（如 R_ID_STR_LAN_ENGLISH）
            /// </summary>
            public string DefaultValueExpression { get; set; } = string.Empty;

            /// <summary>
            /// 默认值（解析后的数值）
            /// </summary>
            public uint DefaultValue { get; set; }

            /// <summary>
            /// 注释信息
            /// </summary>
            public string Comment { get; set; } = string.Empty;

            /// <summary>
            /// 是否被注释掉
            /// </summary>
            public bool IsCommented { get; set; }
        }

        /// <summary>
        /// 从 config.c 文件解析配置项
        /// </summary>
        /// <param name="configCPath">config.c 文件路径</param>
        /// <param name="configHPath">config.h 文件路径（可选，用于获取索引）</param>
        /// <param name="userStrHPath">user_str.h 文件路径（可选，用于解析字符串常量）</param>
        /// <param name="customerHPath">customer.h 文件路径（可选，用于解析条件编译宏）</param>
        /// <param name="versionHPath">version.h 文件路径（可选，用于解析版本宏）</param>
        /// <param name="resHPath">RES.H 文件路径（可选，用于解析资源名称映射）</param>
        /// <returns>解析结果</returns>
        public ParseResult Parse(string configCPath, string? configHPath = null, string? userStrHPath = null, string? customerHPath = null, string? versionHPath = null, string? resHPath = null)
        {
            var result = new ParseResult
            {
                SourceFilePath = configCPath,
                ProjectName = Path.GetFileName(Path.GetDirectoryName(configCPath)) ?? "Unknown"
            };

            try
            {
                if (!File.Exists(configCPath))
                {
                    result.ErrorMessage = $"文件不存在: {configCPath}";
                    return result;
                }

                string content = File.ReadAllText(configCPath);

                // 解析 config.h 获取索引映射
                Dictionary<string, int> configIndexes = new();
                if (!string.IsNullOrEmpty(configHPath) && File.Exists(configHPath))
                {
                    var hParser = new ConfigHParser();
                    var hResult = hParser.Parse(configHPath);
                    if (hResult.Success)
                    {
                        configIndexes = hResult.ConfigIndexes;
                    }
                }

                // 解析 user_str.h 获取字符串常量映射
                Dictionary<string, uint> stringConstants = new();
                uint rIdTypeStrBase = FirmwareConstants.R_ID_TYPE_STR;
                if (!string.IsNullOrEmpty(userStrHPath) && File.Exists(userStrHPath))
                {
                    var strParser = new UserStrParser();
                    var strResult = strParser.Parse(userStrHPath);
                    if (strResult.Success)
                    {
                        stringConstants = strResult.StringConstants;
                        rIdTypeStrBase = strResult.RIdTypeStrBase;
                        result.StringConstants = stringConstants;
                        result.RIdTypeStrBase = rIdTypeStrBase;
                        System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Loaded {stringConstants.Count} string constants from {userStrHPath}");
                    }
                }

                // 解析 customer.h 获取宏定义（用于条件编译判断）
                Dictionary<string, bool> definedMacros = ParseCustomerH(customerHPath);

                // 解析 version.h 获取宏定义（用于 atoi(VERSION_*) 解析）
                Dictionary<string, string> versionMacros = ParseVersionH(versionHPath);

                // 解析 RES.H 获取资源名称映射（资源 Id -> 资源名称）
                if (!string.IsNullOrEmpty(resHPath) && File.Exists(resHPath))
                {
                    var resourceDefs = ParseResHFile(resHPath);
                    if (resourceDefs.Count > 0)
                    {
                        result.ResourceDefinitions = resourceDefs;
                        result.ResHFilePath = resHPath;
                        System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Loaded {resourceDefs.Count} resource definitions from {resHPath}");
                    }
                }

                // 提取 userConfigReset 函数中的配置项
                var defineMacros = new Dictionary<string, string>();
                CollectDefineMacros(defineMacros, configCPath);
                CollectDefineMacros(defineMacros, configHPath);
                CollectDefineMacros(defineMacros, userStrHPath);
                CollectDefineMacros(defineMacros, customerHPath);
                CollectDefineMacros(defineMacros, versionHPath);

                var items = ExtractConfigItems(content, configIndexes, stringConstants, definedMacros, versionMacros, defineMacros);

                result.ConfigItems = items;
                result.Success = items.Count > 0;

                if (!result.Success)
                {
                    result.ErrorMessage = "未找到任何配置项";
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Parsed {items.Count} config items from {configCPath}");
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"解析失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 解析 customer.h 文件，提取宏定义
        /// </summary>
        /// <param name="customerHPath">customer.h 文件路径</param>
        /// <returns>宏定义字典，key 为宏名，value 为是否被定义</returns>
        private Dictionary<string, bool> ParseCustomerH(string? customerHPath)
        {
            var macros = new Dictionary<string, bool>();

            if (string.IsNullOrEmpty(customerHPath) || !File.Exists(customerHPath))
            {
                return macros;
            }

            try
            {
                string content = File.ReadAllText(customerHPath);
                var lines = content.Split('\n');

                var definePattern = new Regex(@"^\s*#define\s+(\w+)(?:\s+|$)");

                foreach (var line in lines)
                {
                    var trimmedLine = line.TrimStart();
                    if (trimmedLine.StartsWith("//") || trimmedLine.StartsWith("/*"))
                    {
                        continue;
                    }

                    var match = definePattern.Match(line);
                    if (match.Success)
                    {
                        string macroName = match.Groups[1].Value;
                        if (!macros.ContainsKey(macroName))
                        {
                            macros[macroName] = true;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Loaded {macros.Count} macros from {customerHPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Error parsing customer.h: {ex.Message}");
            }

            return macros;
        }

        /// <summary>
        /// 解析 version.h 文件，提取宏定义的值
        /// </summary>
        /// <param name="versionHPath">version.h 文件路径</param>
        /// <returns>宏定义字典，key 为宏名，value 为宏的值</returns>
        private Dictionary<string, string> ParseVersionH(string? versionHPath)
        {
            var macros = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(versionHPath) || !File.Exists(versionHPath))
            {
                return macros;
            }

            try
            {
                string content = File.ReadAllText(versionHPath);
                var lines = content.Split('\n');

                var stringDefinePattern = new Regex(@"^\s*#define\s+(\w+)\s+""([^""]+)""");
                var numericDefinePattern = new Regex(@"^\s*#define\s+(\w+)\s+(\d+)");

                foreach (var line in lines)
                {
                    var trimmedLine = line.TrimStart();
                    if (trimmedLine.StartsWith("//") || trimmedLine.StartsWith("/*"))
                    {
                        continue;
                    }

                    var stringMatch = stringDefinePattern.Match(line);
                    if (stringMatch.Success)
                    {
                        string macroName = stringMatch.Groups[1].Value;
                        string macroValue = stringMatch.Groups[2].Value;
                        if (!macros.ContainsKey(macroName))
                        {
                            macros[macroName] = macroValue;
                        }
                        continue;
                    }

                    var numericMatch = numericDefinePattern.Match(line);
                    if (numericMatch.Success)
                    {
                        string macroName = numericMatch.Groups[1].Value;
                        string macroValue = numericMatch.Groups[2].Value;
                        if (!macros.ContainsKey(macroName))
                        {
                            macros[macroName] = macroValue;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Loaded {macros.Count} version macros from {versionHPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Error parsing version.h: {ex.Message}");
            }

            return macros;
        }

        /// <summary>
        /// 解析 RES.H 文件，提取资源定义映射（资源 Id -> 资源名称）
        /// 匹配形如: #define RES_POWER_ON  78
        /// </summary>
        /// <param name="resHPath">RES.H 文件路径</param>
        /// <returns>资源 Id 到资源名称的映射字典</returns>
        private Dictionary<uint, string> ParseResHFile(string resHPath)
        {
            var map = new Dictionary<uint, string>();

            if (string.IsNullOrEmpty(resHPath) || !File.Exists(resHPath))
            {
                return map;
            }

            try
            {
                var lines = File.ReadAllLines(resHPath);
                var definePattern = new Regex(@"^\s*#\s*define\s+(RES_\w+)\s+(\d+)", RegexOptions.Compiled);

                foreach (var line in lines)
                {
                    var match = definePattern.Match(line);
                    if (match.Success)
                    {
                        string resourceName = match.Groups[1].Value;
                        if (uint.TryParse(match.Groups[2].Value, out uint index))
                        {
                            map[index] = resourceName;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Loaded {map.Count} resource definitions from {resHPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Error parsing RES.H: {ex.Message}");
            }

            return map;
        }

        /// <summary>
        /// 从文件中收集 #define 宏定义到字典中
        /// 仅收集值为简单标识符或常量的宏，跳过函数式宏和复杂宏
        /// </summary>
        private void CollectDefineMacros(Dictionary<string, string> defineMacros, string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;

            try
            {
                string content = File.ReadAllText(filePath);
                var lines = content.Split('\n');

                // 匹配: #define MACRO_NAME value  (value为非空，不含括号的简单表达式)
                var definePattern = new Regex(@"^\s*#define\s+(\w+)\s+(.+?)\s*$");

                foreach (var line in lines)
                {
                    var trimmedLine = line.TrimStart();
                    if (trimmedLine.StartsWith("//") || trimmedLine.StartsWith("/*") || trimmedLine.StartsWith("#if") || trimmedLine.StartsWith("#else") || trimmedLine.StartsWith("#endif"))
                        continue;

                    var match = definePattern.Match(line);
                    if (!match.Success)
                        continue;

                    string macroName = match.Groups[1].Value;
                    string macroValue = match.Groups[2].Value.Trim();

                    // 跳过 CONFIG_ID_* 宏（这些是配置索引，不是值）
                    if (macroName.StartsWith("CONFIG_ID_"))
                        continue;

                    // 跳过函数式宏（值中包含 (）
                    if (macroValue.Contains('('))
                        continue;

                    // 跳过空值
                    if (string.IsNullOrEmpty(macroValue))
                        continue;

                    // 移除行尾注释
                    macroValue = Regex.Replace(macroValue, @"//.*$", "").Trim();
                    macroValue = Regex.Replace(macroValue, @"/\*.*?\*/", "", RegexOptions.Singleline).Trim();

                    if (string.IsNullOrEmpty(macroValue))
                        continue;

                    // 只保留第一个值（逗号前的部分），防止多值定义
                    int commaIdx = macroValue.IndexOf(',');
                    if (commaIdx > 0)
                        macroValue = macroValue.Substring(0, commaIdx).Trim();

                    if (!defineMacros.ContainsKey(macroName))
                    {
                        defineMacros[macroName] = macroValue;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Error collecting defines from {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// 递归解析宏引用链，支持多级宏替换
        /// </summary>
        private static string ResolveMacroChain(string expr, Dictionary<string, string> defineMacros, int depth = 0)
        {
            if (depth >= 10)
                return expr;

            // 如果表达式是一个已知宏，替换为其值
            if (defineMacros.TryGetValue(expr, out string? macroValue))
            {
                // 递归解析宏值（支持宏嵌套）
                return ResolveMacroChain(macroValue, defineMacros, depth + 1);
            }

            // 尝试解析表达式中的每个标识符
            // 用空格和运算符分割，逐个替换已知宏
            var tokens = Regex.Matches(expr, @"[A-Za-z_]\w*|[0-9]+|[^\s\w]");
            if (tokens.Count <= 1)
                return expr;

            bool changed = false;
            var result = new System.Text.StringBuilder();

            foreach (Match token in tokens)
            {
                string tokenValue = token.Value;
                if (defineMacros.TryGetValue(tokenValue, out string? replacement))
                {
                    result.Append(replacement);
                    changed = true;
                }
                else
                {
                    result.Append(tokenValue);
                }
            }

            if (!changed)
                return expr;

            // 如果有变化，检查结果是否还可以进一步解析
            string resolved = result.ToString();
            return ResolveMacroChain(resolved, defineMacros, depth + 1);
        }

        /// <summary>
        /// 从源码内容中提取配置项（支持条件编译）
        /// </summary>
        private List<ConfigItemInfo> ExtractConfigItems(string content, Dictionary<string, int> configIndexes, Dictionary<string, uint>? stringConstants = null, Dictionary<string, bool>? definedMacros = null, Dictionary<string, string>? versionMacros = null, Dictionary<string, string>? defineMacros = null)
        {
            var items = new List<ConfigItemInfo>();
            var lines = content.Split('\n');

            var configSetPattern = new Regex(
                @"^\s*(//\s*)?configSet\s*\(\s*(CONFIG_ID_\w+)\s*,",
                RegexOptions.Multiline);

            var ifPattern = new Regex(@"^\s*#if\s+(?:defined\s*\(\s*(\w+)\s*\)|(\w+))");
            var elifPattern = new Regex(@"^\s*#elif\s+(?:defined\s*\(\s*(\w+)\s*\)|(\w+))");
            var elsePattern = new Regex(@"^\s*#else");
            var endifPattern = new Regex(@"^\s*#endif");

            definedMacros ??= new Dictionary<string, bool>();

            Stack<Tuple<bool, bool>> conditionStack = new();
            bool isActive = true;
            bool branchTaken = false;
            bool inAmbiguousBranch = false;

            int currentIndex = 0;
            var usedIndexes = new HashSet<int>();
            var nameIndexMap = new Dictionary<string, int>();

            foreach (var line in lines)
            {
                var trimmedLine = line.TrimStart();

                if (trimmedLine.StartsWith("#"))
                {
                    if (ifPattern.Match(line).Success)
                    {
                        var ifMatch = ifPattern.Match(line);
                        string macroName = ifMatch.Groups[1].Success ? ifMatch.Groups[1].Value : ifMatch.Groups[2].Value;

                        bool conditionResult;
                        if (definedMacros.TryGetValue(macroName, out bool isDefined))
                        {
                            conditionResult = isDefined;
                            inAmbiguousBranch = false;
                        }
                        else
                        {
                            conditionResult = true;
                            inAmbiguousBranch = true;
                        }

                        bool newBranchTaken = isActive && conditionResult;
                        conditionStack.Push(Tuple.Create(isActive, branchTaken));
                        isActive = newBranchTaken;
                        branchTaken = newBranchTaken;
                    }
                    else if (elifPattern.Match(line).Success)
                    {
                        if (conditionStack.Count > 0)
                        {
                            var top = conditionStack.Peek();
                            bool parentActive = top.Item1;

                            var elifMatch = elifPattern.Match(line);
                            string macroName = elifMatch.Groups[1].Success ? elifMatch.Groups[1].Value : elifMatch.Groups[2].Value;

                            bool conditionResult;
                            if (definedMacros.TryGetValue(macroName, out bool isDefined))
                            {
                                conditionResult = isDefined;
                                inAmbiguousBranch = false;
                            }
                            else
                            {
                                conditionResult = false;
                                inAmbiguousBranch = true;
                            }

                            bool newBranchTaken = parentActive && !branchTaken && conditionResult;
                            isActive = newBranchTaken;
                            branchTaken = newBranchTaken || branchTaken;
                        }
                    }
                    else if (elsePattern.Match(line).Success)
                    {
                        if (conditionStack.Count > 0)
                        {
                            var top = conditionStack.Peek();
                            bool parentActive = top.Item1;

                            bool newBranchTaken = parentActive && !branchTaken;
                            isActive = newBranchTaken;
                            branchTaken = newBranchTaken || branchTaken;
                        }
                    }
                    else if (endifPattern.Match(line).Success)
                    {
                        if (conditionStack.Count > 0)
                        {
                            var top = conditionStack.Pop();
                            isActive = top.Item1;
                            branchTaken = top.Item2;
                            inAmbiguousBranch = false;
                        }
                    }
                    continue;
                }

                var match = configSetPattern.Match(line);
                if (!match.Success)
                    continue;

                if (!isActive)
                    continue;

                bool isCommented = match.Groups[1].Success;
                string configName = match.Groups[2].Value;
                
                // 从逗号后开始提取值表达式
                int valueStartIndex = match.Index + match.Length;
                string valueExpr = ExtractConfigSetValue(line, valueStartIndex);
                
                // 提取注释（在分号后）
                string comment = string.Empty;
                int semiIndex = valueExpr.IndexOf(';');
                if (semiIndex >= 0)
                {
                    comment = valueExpr.Substring(semiIndex + 1).Trim();
                    valueExpr = valueExpr.Substring(0, semiIndex).Trim();
                }

                if (nameIndexMap.ContainsKey(configName) && inAmbiguousBranch)
                {
                    continue;
                }

                uint defaultValue = ParseDefaultValue(valueExpr, stringConstants, versionMacros, defineMacros);

                int index;
                if (configIndexes.ContainsKey(configName))
                {
                    index = configIndexes[configName];
                }
                else if (nameIndexMap.ContainsKey(configName))
                {
                    index = nameIndexMap[configName];
                }
                else
                {
                    while (usedIndexes.Contains(currentIndex))
                    {
                        currentIndex++;
                    }
                    index = currentIndex++;
                }

                usedIndexes.Add(index);
                nameIndexMap[configName] = index;

                var item = new ConfigItemInfo
                {
                    Name = configName,
                    Index = index,
                    DefaultValueExpression = valueExpr,
                    DefaultValue = defaultValue,
                    Comment = comment,
                    IsCommented = isCommented
                };

                items.Add(item);
            }

            items = RemoveDuplicateConfigItems(items);

            return items.OrderBy(x => x.Index).ToList();
        }

        /// <summary>
        /// 从configSet行中提取值表达式，正确处理嵌套括号
        /// </summary>
        private string ExtractConfigSetValue(string line, int startIndex)
        {
            // 找到 configSet( 的位置
            int configSetStart = line.IndexOf("configSet", StringComparison.OrdinalIgnoreCase);
            if (configSetStart < 0)
            {
                return string.Empty;
            }

            int configSetParenIdx = line.IndexOf('(', configSetStart);
            if (configSetParenIdx < 0)
            {
                return string.Empty;
            }

            // 找到第一个参数的逗号位置（在括号深度为1的层级中）
            int bracketDepth = 0;
            int commaIdx = -1;
            
            for (int i = configSetParenIdx; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '(')
                {
                    bracketDepth++;
                }
                else if (c == ')')
                {
                    bracketDepth--;
                }
                else if (c == ',' && bracketDepth == 1)
                {
                    commaIdx = i;
                    break;
                }
            }

            if (commaIdx < 0)
            {
                return string.Empty;
            }

            // 从逗号后开始追踪，找到 configSet 的闭合括号
            bracketDepth = 0;
            int endIndex = line.Length;
            bool found = false;

            for (int i = configSetParenIdx; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '(')
                {
                    bracketDepth++;
                }
                else if (c == ')')
                {
                    bracketDepth--;
                    if (bracketDepth == 0)
                    {
                        endIndex = i;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                endIndex = line.Length;
            }

            // 提取值表达式（逗号后到闭合括号前）
            int valueStart = commaIdx + 1;
            string result = line.Substring(valueStart, endIndex - valueStart).Trim();
            return result;
        }

        /// <summary>
        /// 移除重复的配置项，保留最后一个（模拟C语言赋值覆盖行为）
        /// 同时处理重复索引的情况
        /// </summary>
        private List<ConfigItemInfo> RemoveDuplicateConfigItems(List<ConfigItemInfo> items)
        {
            var seenNames = new Dictionary<string, int>();
            var seenIndexes = new Dictionary<int, int>();
            var result = new List<ConfigItemInfo>();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];

                if (seenNames.ContainsKey(item.Name))
                {
                    int prevIndex = seenNames[item.Name];
                    var prevItem = result[prevIndex];

                    System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Warning: Duplicate config item '{item.Name}' (index={item.Index}). " +
                        $"Previous value: {prevItem.DefaultValueExpression} (0x{prevItem.DefaultValue:X8}), " +
                        $"New value: {item.DefaultValueExpression} (0x{item.DefaultValue:X8}). " +
                        $"Keeping new value.");

                    result[prevIndex] = item;
                }
                else if (seenIndexes.ContainsKey(item.Index))
                {
                    int prevIndex = seenIndexes[item.Index];
                    var prevItem = result[prevIndex];

                    System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Warning: Duplicate index {item.Index} for '{item.Name}' and '{prevItem.Name}'. " +
                        $"Keeping '{item.Name}'.");

                    result[prevIndex] = item;
                    seenNames[item.Name] = prevIndex;
                }
                else
                {
                    seenNames[item.Name] = result.Count;
                    seenIndexes[item.Index] = result.Count;
                    result.Add(item);
                }
            }

            return result;
        }

        /// <summary>
        /// 解析默认值表达式
        /// </summary>
        private uint ParseDefaultValue(string expr, Dictionary<string, uint>? stringConstants = null, Dictionary<string, string>? versionMacros = null, Dictionary<string, string>? defineMacros = null)
        {
            // 移除 C 风格注释: 块注释 /* ... */ 和行注释 // ...
            expr = Regex.Replace(expr, @"/\*.*?\*/", "", RegexOptions.Singleline);
            expr = Regex.Replace(expr, @"//.*$", "").Trim();

            // 处理宏定义引用: 如 DEAULT_LANG → R_ID_STR_LAN_SCHINESE
            if (defineMacros != null && defineMacros.Count > 0 && !string.IsNullOrEmpty(expr))
            {
                var resolved = ResolveMacroChain(expr, defineMacros);
                if (resolved != expr)
                {
                    return ParseDefaultValue(resolved, stringConstants, versionMacros, defineMacros);
                }
            }

            // 处理 R_ID_STR_* 常量
            if (expr.StartsWith("R_ID_STR_"))
            {
                return ResolveRIdStrConstant(expr, stringConstants);
            }

            // 处理十六进制数
            if (expr.StartsWith("0x") || expr.StartsWith("0X"))
            {
                if (uint.TryParse(expr.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint hexValue))
                {
                    return hexValue;
                }
            }

            // 处理十进制数
            if (uint.TryParse(expr, out uint decValue))
            {
                return decValue;
            }

            // 处理 atoi("数字") 调用
            var atoiLiteralMatch = Regex.Match(expr, @"atoi\s*\(\s*""(\d+)""\s*\)");
            if (atoiLiteralMatch.Success)
            {
                if (uint.TryParse(atoiLiteralMatch.Groups[1].Value, out uint atoiValue))
                {
                    return atoiValue;
                }
            }

            // 处理 atoi(MACRO_NAME) 调用（如 atoi(VERSION_YEAR)）
            var atoiMacroMatch = Regex.Match(expr, @"atoi\s*\(\s*(\w+)\s*\)");
            if (atoiMacroMatch.Success)
            {
                string macroName = atoiMacroMatch.Groups[1].Value;
                
                // 首先尝试直接从 versionMacros 查找
                if (versionMacros != null && versionMacros.TryGetValue(macroName, out string macroValue))
                {
                    if (uint.TryParse(macroValue, out uint macroIntValue))
                    {
                        return macroIntValue;
                    }
                }
                
                // 尝试从 VERSION_TIME 字符串中解析年月日时分秒
                uint timeValue = TryResolveFromVersionTime(macroName, versionMacros);
                if (timeValue != 0 || IsZeroTimeValueValid(macroName))
                {
                    return timeValue;
                }
                
                System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Warning: Macro '{macroName}' not found in version.h, cannot resolve atoi({macroName})");
            }

            // 未知表达式，返回 0
            System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Unknown value expression: {expr}");
            return 0;
        }

        /// <summary>
        /// 从 VERSION_TIME 字符串中解析年月日时分秒
        /// 支持格式: "2026/07/24 17:43:19" 或 "07/28/2026 14:30:00"
        /// </summary>
        private uint TryResolveFromVersionTime(string macroName, Dictionary<string, string>? versionMacros)
        {
            if (versionMacros == null || !versionMacros.TryGetValue("VERSION_TIME", out string? timeStr))
            {
                return 0;
            }

            if (string.IsNullOrEmpty(timeStr))
            {
                return 0;
            }

            var timeParts = timeStr.Split(new[] { ' ', '/', ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (timeParts.Length < 6)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] VERSION_TIME format unexpected: '{timeStr}'");
                return 0;
            }

            // 判断日期格式: YYYY/MM/DD 或 MM/DD/YYYY
            int year, month, day, hour, minute, second;
            
            if (timeParts[0].Length == 4)
            {
                // 格式: YYYY/MM/DD HH:MM:SS
                year = int.Parse(timeParts[0]);
                month = int.Parse(timeParts[1]);
                day = int.Parse(timeParts[2]);
            }
            else
            {
                // 格式: MM/DD/YYYY HH:MM:SS
                month = int.Parse(timeParts[0]);
                day = int.Parse(timeParts[1]);
                year = int.Parse(timeParts[2]);
            }
            
            hour = int.Parse(timeParts[3]);
            minute = int.Parse(timeParts[4]);
            second = int.Parse(timeParts[5]);

            return macroName switch
            {
                "VERSION_YEAR" => (uint)year,
                "VERSION_MONTH" => (uint)month,
                "VERSION_DAY" => (uint)day,
                "VERSION_HOUR" => (uint)hour,
                "VERSION_MINUTE" => (uint)minute,
                "VERSION_SECOND" => (uint)second,
                _ => 0
            };
        }

        /// <summary>
        /// 判断零值是否有效（对于秒、分钟等可以为0的值）
        /// </summary>
        private bool IsZeroTimeValueValid(string macroName)
        {
            return macroName == "VERSION_SECOND" || 
                   macroName == "VERSION_MINUTE" ||
                   macroName == "VERSION_HOUR";
        }

        /// <summary>
        /// 解析 R_ID_STR_* 常量
        /// 优先从动态解析的字符串常量中查找，其次从 FirmwareConstants 中查找
        /// </summary>
        private uint ResolveRIdStrConstant(string constantName, Dictionary<string, uint>? stringConstants = null)
        {
            // 优先从动态解析的字符串常量中查找
            if (stringConstants != null && stringConstants.TryGetValue(constantName, out uint dynamicValue))
            {
                return dynamicValue;
            }

            // 从 FirmwareConstants 中查找对应的值
            var field = typeof(FirmwareConstants).GetField(constantName);
            if (field != null)
            {
                var value = field.GetValue(null);
                if (value is uint uintValue)
                {
                    return uintValue;
                }
            }

            // 尝试从常量名推断值
            // 例如: R_ID_STR_LAN_ENGLISH -> 语言枚举
            if (constantName.Contains("_LAN_"))
            {
                return ResolveLanguageConstant(constantName);
            }
            else if (constantName.Contains("_COM_"))
            {
                return ResolveCommonConstant(constantName);
            }
            else if (constantName.Contains("_RES_"))
            {
                return ResolveResolutionConstant(constantName);
            }
            else if (constantName.Contains("_TIM_"))
            {
                return ResolveTimeConstant(constantName);
            }
            else if (constantName.Contains("_ISP_"))
            {
                return ResolveIspConstant(constantName);
            }

            System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Unknown R_ID_STR constant: {constantName}");
            return 0;
        }

        private uint ResolveLanguageConstant(string name)
        {
            // 从名称中提取语言类型
            if (name.Contains("ENGLISH")) return FirmwareConstants.R_STR_LAN_ENGLISH;
            if (name.Contains("SCHINESE") || name.Contains("SIMPLIFIED")) return FirmwareConstants.R_STR_LAN_SCHINESE;
            if (name.Contains("TCHINESE") || name.Contains("TRADITIONAL")) return FirmwareConstants.R_STR_LAN_TCHINESE;
            if (name.Contains("JAPANESE")) return FirmwareConstants.R_STR_LAN_JAPANESE;
            if (name.Contains("GERMAN")) return FirmwareConstants.R_STR_LAN_GERMAN;
            if (name.Contains("FRENCH") || name.Contains("FRECH")) return FirmwareConstants.R_STR_LAN_FRECH;
            if (name.Contains("RUSSIAN")) return FirmwareConstants.R_STR_LAN_RUSSIAN;
            if (name.Contains("ITALIAN")) return FirmwareConstants.R_STR_LAN_ITALIAN;
            if (name.Contains("KOREAN")) return FirmwareConstants.R_STR_LAN_KOERA;
            if (name.Contains("THAI")) return FirmwareConstants.R_STR_LAN_TAI;
            if (name.Contains("HEBREW")) return FirmwareConstants.R_STR_LAN_HEBREW;
            if (name.Contains("DUTCH")) return FirmwareConstants.R_STR_LAN_DUTCH;
            if (name.Contains("UKRAINIAN")) return FirmwareConstants.R_STR_LAN_UKRAINIAN;
            if (name.Contains("SPANISH")) return FirmwareConstants.R_STR_LAN_SPANISH;
            if (name.Contains("PORTUGUESE")) return FirmwareConstants.R_STR_LAN_PORTUGUESE;
            if (name.Contains("POLISH")) return FirmwareConstants.R_STR_LAN_POLISH;
            if (name.Contains("CZECH")) return FirmwareConstants.R_STR_LAN_CZECH;
            if (name.Contains("TURKISH") || name.Contains("TURKEY")) return FirmwareConstants.R_STR_LAN_TURKEY;
            if (name.Contains("ROMANIAN")) return FirmwareConstants.R_STR_LAN_ROMANIAN;
            
            return FirmwareConstants.R_ID_TYPE_STR; // 默认英语
        }

        private uint ResolveCommonConstant(string name)
        {
            if (name.Contains("_OFF")) return FirmwareConstants.R_STR_COM_OFF;
            if (name.Contains("_ON")) return FirmwareConstants.R_STR_COM_ON;
            if (name.Contains("_LOW")) return FirmwareConstants.R_STR_COM_LOW;
            if (name.Contains("_MIDDLE") || name.Contains("_MED")) return FirmwareConstants.R_STR_COM_MIDDLE;
            if (name.Contains("_HIGH")) return FirmwareConstants.R_STR_COM_HIGH;
            if (name.Contains("_50HZ")) return FirmwareConstants.R_STR_COM_50HZ;
            if (name.Contains("_60HZ")) return FirmwareConstants.R_STR_COM_60HZ;
            
            // 处理 BRIGHT_LEVEL 值
            var brightLevelMatch = Regex.Match(name, @"BRIGHT_LEVEL_(\d+)");
            if (brightLevelMatch.Success)
            {
                int level = int.Parse(brightLevelMatch.Groups[1].Value);
                if (level >= 1 && level <= 9)
                {
                    return FirmwareConstants.R_ID_TYPE_STR + (uint)(0x60 + level - 1);
                }
            }

            // 处理 LEVEL 值
            var levelMatch = Regex.Match(name, @"LEVEL_(\d+)");
            if (levelMatch.Success)
            {
                int level = int.Parse(levelMatch.Groups[1].Value);
                if (level >= 0 && level <= 9)
                {
                    return FirmwareConstants.R_ID_TYPE_STR + (uint)(0x1F + level);
                }
            }

            // 处理 EV 值
            if (name.Contains("_P4_0")) return FirmwareConstants.R_STR_COM_P4_0;
            if (name.Contains("_P3_0")) return FirmwareConstants.R_STR_COM_P3_0;
            if (name.Contains("_P2_0")) return FirmwareConstants.R_STR_COM_P2_0;
            if (name.Contains("_P1_0")) return FirmwareConstants.R_STR_COM_P1_0;
            if (name.Contains("_P0_0")) return FirmwareConstants.R_STR_COM_P0_0;
            if (name.Contains("_N1_0")) return FirmwareConstants.R_STR_COM_N1_0;
            if (name.Contains("_N2_0")) return FirmwareConstants.R_STR_COM_N2_0;

            return FirmwareConstants.R_ID_TYPE_STR;
        }

        private uint ResolveResolutionConstant(string name)
        {
            if (name.Contains("_240P")) return FirmwareConstants.R_STR_RES_240P;
            if (name.Contains("_480P")) return FirmwareConstants.R_STR_RES_480P;
            if (name.Contains("_720HD")) return FirmwareConstants.R_STR_RES_720HD;
            if (name.Contains("_720P")) return FirmwareConstants.R_STR_RES_720P;
            if (name.Contains("_720P_SHORT")) return FirmwareConstants.R_STR_RES_720P_SHORT;
            if (name.Contains("_1080FHD")) return FirmwareConstants.R_STR_RES_1080FHD;
            if (name.Contains("_1080P")) return FirmwareConstants.R_STR_RES_1080P;
            if (name.Contains("_1080P_SHORT")) return FirmwareConstants.R_STR_RES_1080P_SHORT;
            if (name.Contains("_1440P_SHORT")) return FirmwareConstants.R_STR_RES_1440P_SHORT;
            if (name.Contains("_1440P")) return FirmwareConstants.R_STR_RES_1440P;
            if (name.Contains("_HD")) return FirmwareConstants.R_STR_RES_HD;
            if (name.Contains("_FHD")) return FirmwareConstants.R_STR_RES_FHD;
            if (name.Contains("_12M")) return FirmwareConstants.R_STR_RES_12M;
            if (name.Contains("_8M")) return FirmwareConstants.R_STR_RES_8M;
            if (name.Contains("_5M")) return FirmwareConstants.R_STR_RES_5M;
            if (name.Contains("_4M")) return FirmwareConstants.R_STR_RES_4M;
            
            return FirmwareConstants.R_ID_TYPE_STR;
        }

        private uint ResolveTimeConstant(string name)
        {
            if (name.Contains("_1MIN")) return FirmwareConstants.R_STR_TIM_1MIN;
            if (name.Contains("_2MIN")) return FirmwareConstants.R_STR_TIM_2MIN;
            if (name.Contains("_3MIN")) return FirmwareConstants.R_STR_TIM_3MIN;
            if (name.Contains("_5MIN")) return FirmwareConstants.R_STR_TIM_5MIN;
            if (name.Contains("_10MIN")) return FirmwareConstants.R_STR_TIM_10MIN;
            
            return FirmwareConstants.R_ID_TYPE_STR;
        }

        private uint ResolveIspConstant(string name)
        {
            if (name.Contains("_AUTO")) return FirmwareConstants.R_STR_ISP_AUTO;
            if (name.Contains("_SUNLIGHT")) return FirmwareConstants.R_STR_ISP_SUNLIGHT;
            if (name.Contains("_CLOUDY")) return FirmwareConstants.R_STR_ISP_CLOUDY;
            if (name.Contains("_TUNGSTEN")) return FirmwareConstants.R_STR_ISP_TUNGSTEN;
            if (name.Contains("_FLUORESCENT")) return FirmwareConstants.R_STR_ISP_FLUORESCENT;
            
            return FirmwareConstants.R_ID_TYPE_STR;
        }

        /// <summary>
        /// 静态方法：解析 R_ID_STR_* 常量
        /// </summary>
        public static uint ParseRIdStrConstantStatic(string constantName)
        {
            var field = typeof(FirmwareConstants).GetField(constantName);
            if (field != null)
            {
                var value = field.GetValue(null);
                if (value is uint uintValue)
                {
                    return uintValue;
                }
            }

            if (constantName.Contains("_LAN_"))
            {
                if (constantName.Contains("ENGLISH")) return FirmwareConstants.R_STR_LAN_ENGLISH;
                if (constantName.Contains("SCHINESE") || constantName.Contains("SIMPLIFIED")) return FirmwareConstants.R_STR_LAN_SCHINESE;
                if (constantName.Contains("TCHINESE") || constantName.Contains("TRADITIONAL")) return FirmwareConstants.R_STR_LAN_TCHINESE;
                if (constantName.Contains("JAPANESE")) return FirmwareConstants.R_STR_LAN_JAPANESE;
                if (constantName.Contains("GERMAN")) return FirmwareConstants.R_STR_LAN_GERMAN;
                if (constantName.Contains("FRENCH") || constantName.Contains("FRECH")) return FirmwareConstants.R_STR_LAN_FRECH;
                if (constantName.Contains("RUSSIAN")) return FirmwareConstants.R_STR_LAN_RUSSIAN;
                if (constantName.Contains("ITALIAN")) return FirmwareConstants.R_STR_LAN_ITALIAN;
                if (constantName.Contains("KOREAN")) return FirmwareConstants.R_STR_LAN_KOERA;
                if (constantName.Contains("THAI")) return FirmwareConstants.R_STR_LAN_TAI;
                if (constantName.Contains("HEBREW")) return FirmwareConstants.R_STR_LAN_HEBREW;
                if (constantName.Contains("DUTCH")) return FirmwareConstants.R_STR_LAN_DUTCH;
                if (constantName.Contains("UKRAINIAN")) return FirmwareConstants.R_STR_LAN_UKRAINIAN;
                if (constantName.Contains("SPANISH")) return FirmwareConstants.R_STR_LAN_SPANISH;
                if (constantName.Contains("PORTUGUESE")) return FirmwareConstants.R_STR_LAN_PORTUGUESE;
                if (constantName.Contains("POLISH")) return FirmwareConstants.R_STR_LAN_POLISH;
                if (constantName.Contains("CZECH")) return FirmwareConstants.R_STR_LAN_CZECH;
                if (constantName.Contains("TURKISH") || constantName.Contains("TURKEY")) return FirmwareConstants.R_STR_LAN_TURKEY;
            }
            else if (constantName.Contains("_COM_"))
            {
                if (constantName.Contains("_OFF")) return FirmwareConstants.R_STR_COM_OFF;
                if (constantName.Contains("_ON")) return FirmwareConstants.R_STR_COM_ON;
                if (constantName.Contains("_LOW")) return FirmwareConstants.R_STR_COM_LOW;
                if (constantName.Contains("_MIDDLE") || constantName.Contains("_MED")) return FirmwareConstants.R_STR_COM_MIDDLE;
                if (constantName.Contains("_HIGH")) return FirmwareConstants.R_STR_COM_HIGH;
                if (constantName.Contains("_50HZ")) return FirmwareConstants.R_STR_COM_50HZ;
                if (constantName.Contains("_60HZ")) return FirmwareConstants.R_STR_COM_60HZ;

                var brightLevelMatch = Regex.Match(constantName, @"BRIGHT_LEVEL_(\d+)");
                if (brightLevelMatch.Success)
                {
                    int level = int.Parse(brightLevelMatch.Groups[1].Value);
                    if (level >= 1 && level <= 9)
                    {
                        return FirmwareConstants.R_ID_TYPE_STR + (uint)(0x60 + level - 1);
                    }
                }

                var levelMatch = Regex.Match(constantName, @"LEVEL_(\d+)");
                if (levelMatch.Success)
                {
                    int level = int.Parse(levelMatch.Groups[1].Value);
                    if (level >= 0 && level <= 9)
                    {
                        return FirmwareConstants.R_ID_TYPE_STR + (uint)(0x1F + level);
                    }
                }

                if (constantName.Contains("_P4_0")) return FirmwareConstants.R_STR_COM_P4_0;
                if (constantName.Contains("_P3_0")) return FirmwareConstants.R_STR_COM_P3_0;
                if (constantName.Contains("_P2_0")) return FirmwareConstants.R_STR_COM_P2_0;
                if (constantName.Contains("_P1_0")) return FirmwareConstants.R_STR_COM_P1_0;
                if (constantName.Contains("_P0_0")) return FirmwareConstants.R_STR_COM_P0_0;
                if (constantName.Contains("_N1_0")) return FirmwareConstants.R_STR_COM_N1_0;
                if (constantName.Contains("_N2_0")) return FirmwareConstants.R_STR_COM_N2_0;
            }
            else if (constantName.Contains("_RES_"))
            {
                if (constantName.Contains("_240P")) return FirmwareConstants.R_STR_RES_240P;
                if (constantName.Contains("_480P")) return FirmwareConstants.R_STR_RES_480P;
                if (constantName.Contains("_720P")) return FirmwareConstants.R_STR_RES_720P;
                if (constantName.Contains("_1080P")) return FirmwareConstants.R_STR_RES_1080P;
                if (constantName.Contains("_1440P")) return FirmwareConstants.R_STR_RES_1440P;
                if (constantName.Contains("_2160P")) return FirmwareConstants.R_STR_RES_2160P;
                if (constantName.Contains("_3024P")) return FirmwareConstants.R_STR_RES_3024P;
                if (constantName.Contains("_720P_SHORT")) return FirmwareConstants.R_STR_RES_720P_SHORT;
                if (constantName.Contains("_1080P_SHORT")) return FirmwareConstants.R_STR_RES_1080P_SHORT;
                if (constantName.Contains("_1440P_SHORT")) return FirmwareConstants.R_STR_RES_1440P_SHORT;
                if (constantName.Contains("_2160P_SHORT")) return FirmwareConstants.R_STR_RES_2160P_SHORT;
                if (constantName.Contains("_HD")) return FirmwareConstants.R_STR_RES_HD;
                if (constantName.Contains("_FHD")) return FirmwareConstants.R_STR_RES_FHD;
                if (constantName.Contains("_48M")) return FirmwareConstants.R_STR_RES_48M;
                if (constantName.Contains("_24M")) return FirmwareConstants.R_STR_RES_24M;
                if (constantName.Contains("_20M")) return FirmwareConstants.R_STR_RES_20M;
                if (constantName.Contains("_18M")) return FirmwareConstants.R_STR_RES_18M;
                if (constantName.Contains("_16M")) return FirmwareConstants.R_STR_RES_16M;
                if (constantName.Contains("_12M")) return FirmwareConstants.R_STR_RES_12M;
                if (constantName.Contains("_10M")) return FirmwareConstants.R_STR_RES_10M;
                if (constantName.Contains("_8M")) return FirmwareConstants.R_STR_RES_8M;
                if (constantName.Contains("_5M")) return FirmwareConstants.R_STR_RES_5M;
                if (constantName.Contains("_4M")) return FirmwareConstants.R_STR_RES_4M;
                if (constantName.Contains("_3M")) return FirmwareConstants.R_STR_RES_3M;
                if (constantName.Contains("_2M")) return FirmwareConstants.R_STR_RES_2M;
                if (constantName.Contains("_1M")) return FirmwareConstants.R_STR_RES_1M;
                if (constantName.Contains("_VGA")) return FirmwareConstants.R_STR_RES_VGA;
            }
            else if (constantName.Contains("_TIM_"))
            {
                if (constantName.Contains("_1MIN")) return FirmwareConstants.R_STR_TIM_1MIN;
                if (constantName.Contains("_2MIN")) return FirmwareConstants.R_STR_TIM_2MIN;
                if (constantName.Contains("_3MIN")) return FirmwareConstants.R_STR_TIM_3MIN;
                if (constantName.Contains("_5MIN")) return FirmwareConstants.R_STR_TIM_5MIN;
                if (constantName.Contains("_10MIN")) return FirmwareConstants.R_STR_TIM_10MIN;
                if (constantName.Contains("_2SEC")) return FirmwareConstants.R_STR_TIM_2SEC;
                if (constantName.Contains("_3SEC")) return FirmwareConstants.R_STR_TIM_3SEC;
                if (constantName.Contains("_5SEC")) return FirmwareConstants.R_STR_TIM_5SEC;
                if (constantName.Contains("_10SEC")) return FirmwareConstants.R_STR_TIM_10SEC;
            }
            else if (constantName.Contains("_ISP_"))
            {
                if (constantName.Contains("_AUTO")) return FirmwareConstants.R_STR_ISP_AUTO;
                if (constantName.Contains("_SUNLIGHT")) return FirmwareConstants.R_STR_ISP_SUNLIGHT;
                if (constantName.Contains("_CLOUDY")) return FirmwareConstants.R_STR_ISP_CLOUDY;
                if (constantName.Contains("_TUNGSTEN")) return FirmwareConstants.R_STR_ISP_TUNGSTEN;
                if (constantName.Contains("_FLUORESCENT")) return FirmwareConstants.R_STR_ISP_FLUORESCENT;
            }
            else if (constantName.Contains("_IR_"))
            {
                if (constantName.Contains("_AUTO")) return FirmwareConstants.R_ID_TYPE_STR;
            }

            return FirmwareConstants.R_ID_TYPE_STR;
        }

        /// <summary>
        /// 从项目目录自动查找 config.c
        /// </summary>
        public static string? FindConfigC(string projectPath)
        {
            string[] searchPaths = new[]
            {
                Path.Combine(projectPath, "config.c"),
                Path.Combine(projectPath, "src", "config.c"),
                Path.Combine(projectPath, "firmware", "config.c"),
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            // 递归搜索
            try
            {
                var files = Directory.GetFiles(projectPath, "config.c", SearchOption.AllDirectories);
                if (files.Length > 0)
                    return files[0];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigSourceParser] Error searching config.c: {ex.Message}");
            }

            return null;
        }
    }
}
