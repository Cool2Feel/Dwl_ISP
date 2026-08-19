using ResBinManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ResBinManager.Core
{
    public class ConfigXmlParser
    {
        public class ParseResult
        {
            public List<ConfigXmlParsedItem> Items { get; set; } = new();
            public Dictionary<string, uint> StringConstants { get; set; } = new();
            public uint RIdTypeStrBase { get; set; } = FirmwareConstants.R_ID_TYPE_STR;

            /// <summary>
            /// 从 RES.H 解析的资源定义映射（资源 Id -> 资源名称）
            /// </summary>
            public Dictionary<uint, string> ResourceDefinitions { get; set; } = new();
        }

        public static ParseResult ParseFromFileWithConstants(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("XML配置文件不存在", filePath);
            }

            var content = File.ReadAllText(filePath);
            return ParseWithConstants(content);
        }

        public static ParseResult ParseFromStreamWithConstants(Stream stream)
        {
            using (var reader = new StreamReader(stream))
            {
                var content = reader.ReadToEnd();
                return ParseWithConstants(content);
            }
        }

        public static List<ConfigXmlParsedItem> ParseFromFile(string filePath)
        {
            return ParseFromFileWithConstants(filePath).Items;
        }

        public static List<ConfigXmlParsedItem> Parse(string content)
        {
            return ParseWithConstants(content).Items;
        }

        public static ParseResult ParseWithConstants(string content)
        {
            var result = new ParseResult();

            try
            {
                XDocument doc = XDocument.Parse(content);
                XNamespace ns = "";

                var stringConstantsElement = doc.Descendants(ns + "StringConstants").FirstOrDefault();
                if (stringConstantsElement != null)
                {
                    string baseAttr = stringConstantsElement.Attribute("base")?.Value ?? "";
                    if (!string.IsNullOrEmpty(baseAttr))
                    {
                        result.RIdTypeStrBase = ParseDefaultValue(baseAttr);
                    }

                    foreach (var constantElement in stringConstantsElement.Descendants(ns + "Constant"))
                    {
                        string name = constantElement.Attribute("name")?.Value ?? "";
                        string valueStr = constantElement.Value ?? "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            result.StringConstants[name] = ParseDefaultValue(valueStr);
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[ConfigXmlParser] Parsed {result.StringConstants.Count} string constants");
                }

                // 解析 RES.H 资源定义映射（资源 Id -> 资源名称）
                var resourceDefinitionsElement = doc.Descendants(ns + "ResourceDefinitions").FirstOrDefault();
                if (resourceDefinitionsElement != null)
                {
                    foreach (var resElement in resourceDefinitionsElement.Descendants(ns + "Resource"))
                    {
                        string idStr = resElement.Attribute("id")?.Value ?? "";
                        string name = resElement.Value?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(idStr) && !string.IsNullOrEmpty(name) && uint.TryParse(idStr, out uint id))
                        {
                            result.ResourceDefinitions[id] = name;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[ConfigXmlParser] Parsed {result.ResourceDefinitions.Count} resource definitions");
                }

                var configItems = doc.Descendants(ns + "ConfigItem");
                if (!configItems.Any())
                {
                    System.Diagnostics.Debug.WriteLine("[ConfigXmlParser] Warning: No ConfigItem elements found");
                }

                foreach (var itemElement in configItems)
                {
                    try
                    {
                        var item = new ConfigXmlParsedItem();

                        item.Index = (int?)itemElement.Element(ns + "Index") ?? -1;
                        item.ConfigName = itemElement.Element(ns + "ConfigName")?.Value ?? string.Empty;
                        item.DisplayName = itemElement.Element(ns + "DisplayName")?.Value ?? string.Empty;
                        item.Category = itemElement.Element(ns + "Category")?.Value ?? string.Empty;
                        item.Type = itemElement.Element(ns + "Type")?.Value ?? string.Empty;
                        item.Description = itemElement.Element(ns + "Description")?.Value ?? string.Empty;

                        string defaultValueStr = itemElement.Element(ns + "DefaultValue")?.Value ?? "0";
                        item.Value = ParseDefaultValue(defaultValueStr);
                        item.DefaultValueStr = defaultValueStr;

                        string enabledStr = itemElement.Element(ns + "Enabled")?.Value ?? "true";
                        item.Enabled = !string.Equals(enabledStr, "false", StringComparison.OrdinalIgnoreCase);

                        var optionsElements = itemElement.Descendants(ns + "Option");
                        foreach (var optElement in optionsElements)
                        {
                            string valueAttr = optElement.Attribute("value")?.Value ?? optElement.Value;
                            string displayName = optElement.Value.Trim();
                            if (string.IsNullOrEmpty(displayName))
                                displayName = valueAttr;

                            item.Options.Add(new ConfigOption(ParseDefaultValue(valueAttr), displayName));
                        }

                        if (!string.IsNullOrEmpty(item.ConfigName) && item.Index >= 0)
                        {
                            if (item.Index >= ConfigParser.SDK_CONFIG_ID_MAX)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ConfigXmlParser] Warning: ConfigItem {item.ConfigName} (index={item.Index}) exceeds max index {ConfigParser.SDK_CONFIG_ID_MAX - 1}");
                            }

                            if (!Enum.IsDefined(typeof(ConfigId), item.ConfigName))
                            {
                                System.Diagnostics.Debug.WriteLine($"[ConfigXmlParser] Warning: ConfigItem {item.ConfigName} is not defined in ConfigId enum");
                            }

                            result.Items.Add(item);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[ConfigXmlParser] Warning: Skipping ConfigItem - Name='{item.ConfigName}', Index={item.Index}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConfigXmlParser] Parse item error: {ex.Message}");
                    }
                }

                result.Items = result.Items.OrderBy(x => x.Index).ToList();

                var duplicateIndexes = result.Items.GroupBy(x => x.Index).Where(g => g.Count() > 1).ToList();
                if (duplicateIndexes.Any())
                {
                    foreach (var group in duplicateIndexes)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConfigXmlParser] Warning: Duplicate index {group.Key} found for: {string.Join(", ", group.Select(x => x.ConfigName))}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[ConfigXmlParser] Parsed {result.Items.Count} config items");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigXmlParser] Parse error: {ex.Message}");
                throw;
            }

            return result;
        }

        public static uint ParseDefaultValue(string valueStr)
        {
            if (string.IsNullOrEmpty(valueStr))
                return 0;

            valueStr = valueStr.Trim();

            if (uint.TryParse(valueStr, out uint decValue))
            {
                return decValue;
            }

            if (valueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (uint.TryParse(valueStr.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint hexValue))
                {
                    return hexValue;
                }
            }

            return 0;
        }

        public static Dictionary<string, uint> ToDictionary(List<ConfigXmlParsedItem> items)
        {
            return items.ToDictionary(item => item.ConfigName, item => item.Value);
        }

        public static Dictionary<ConfigId, uint> ToConfigIdDictionary(List<ConfigXmlParsedItem> items)
        {
            var result = new Dictionary<ConfigId, uint>();
            foreach (var item in items)
            {
                if (Enum.TryParse<ConfigId>(item.ConfigName, out var configId))
                {
                    result[configId] = item.Value;
                }
            }
            return result;
        }

        public static bool SaveXmlToFile(string filePath, List<ConfigXmlParsedItem> updatedItems, Dictionary<string, uint>? stringConstants = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("XML配置文件不存在", filePath);

            XDocument doc = XDocument.Load(filePath);
            XNamespace ns = "";

            if (stringConstants != null)
            {
                var stringConstantsElement = doc.Descendants(ns + "StringConstants").FirstOrDefault();
                if (stringConstantsElement != null)
                {
                    stringConstantsElement.RemoveAll();
                    foreach (var kvp in stringConstants.OrderBy(k => k.Key))
                    {
                        var constantElement = new XElement(ns + "Constant",
                            new XAttribute("name", kvp.Key),
                            $"0x{kvp.Value:X8}");
                        stringConstantsElement.Add(constantElement);
                    }
                }
            }

            var configItemsElements = doc.Descendants(ns + "ConfigItem").ToList();
            var configItemDict = updatedItems.ToDictionary(x => x.ConfigName, x => x);

            foreach (var itemElement in configItemsElements)
            {
                var configNameElement = itemElement.Element(ns + "ConfigName");
                if (configNameElement == null) continue;

                string configName = configNameElement.Value;
                if (!configItemDict.TryGetValue(configName, out var updatedItem)) continue;

                var defaultValElement = itemElement.Element(ns + "DefaultValue");
                if (defaultValElement != null)
                {
                    defaultValElement.Value = $"0x{updatedItem.Value:X8}";
                }

                var enabledElement = itemElement.Element(ns + "Enabled");
                if (enabledElement != null)
                {
                    enabledElement.Value = updatedItem.Enabled ? "true" : "false";
                }

                var optionsElement = itemElement.Element(ns + "Options");
                if (optionsElement != null && updatedItem.Options != null && updatedItem.Options.Count > 0)
                {
                    optionsElement.RemoveAll();
                    foreach (var opt in updatedItem.Options)
                    {
                        var optionElement = new XElement(ns + "Option",
                            new XAttribute("value", $"0x{opt.Value:X8}"),
                            opt.DisplayName);
                        optionsElement.Add(optionElement);
                    }
                }
            }

            doc.Save(filePath);
            System.Diagnostics.Debug.WriteLine($"[ConfigXmlParser] Saved XML config to: {filePath}");
            return true;
        }
    }

    public class ConfigXmlParsedItem
    {
        public int Index { get; set; } = -1;
        public string ConfigName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public uint Value { get; set; }
        public string DefaultValueStr { get; set; } = string.Empty;
        public List<ConfigOption> Options { get; set; } = new List<ConfigOption>();
        public bool Enabled { get; set; } = false;
    }
}