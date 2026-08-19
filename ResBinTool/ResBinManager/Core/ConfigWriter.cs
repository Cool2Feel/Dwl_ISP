using ResBinManager.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace ResBinManager.Core
{
    public class ConfigWriter
    {

        public static bool SaveConfigToDestBin(string destBinPath, FirmwareConfigData configData, string outputPath = null)
        {
            return SaveConfigToDestBin(destBinPath, configData, null, outputPath);
        }

        public static bool SaveConfigToDestBin(string destBinPath, FirmwareConfigData configData, byte[]? firmwareData, string outputPath = null)
        {
            try
            {
                string output = outputPath ?? destBinPath;

                if (firmwareData == null)
                {
                    firmwareData = File.ReadAllBytes(destBinPath);
                }

                System.Diagnostics.Debug.WriteLine($"[ConfigWriter] File size: {firmwareData.Length} (0x{firmwareData.Length})");
                System.Diagnostics.Debug.WriteLine($"[ConfigWriter] ConfigAddress: 0x{configData.ConfigAddress:X}");
                int configSize = ConfigParser.CONFIG_SYSTEM_SIZE;
                int flagsCount = ConfigParser.CONFIG_FLAGS_COUNT;
                System.Diagnostics.Debug.WriteLine($"[ConfigWriter] CONFIG_SYSTEM_SIZE: {configSize}");

                if (configData.ConfigAddress + configSize > firmwareData.Length)
                {
                    throw new InvalidOperationException($"配置区地址 0x{configData.ConfigAddress:X} 超出固件大小 {firmwareData.Length}");
                }

                byte[] configBuffer = new byte[configSize];

                for (int i = 0; i < flagsCount; i++)
                {
                    byte[] flagBytes = BitConverter.GetBytes(configData.Flags[i]);
                    Array.Copy(flagBytes, 0, configBuffer, i * 4, 4);
                }

                uint calculatedCheckSum = configData.CalculateCheckSum();
                configData.CheckSum = calculatedCheckSum;
                byte[] checkSumBytes = BitConverter.GetBytes(configData.CheckSum);
                Array.Copy(checkSumBytes, 0, configBuffer, flagsCount * 4, 4);

                System.Diagnostics.Debug.WriteLine($"[ConfigWriter] Calculated CheckSum: 0x{calculatedCheckSum}");
                System.Diagnostics.Debug.WriteLine($"[ConfigWriter] Writing {configSize} bytes to offset 0x{configData.ConfigAddress:X}");

                Array.Copy(configBuffer, 0, firmwareData, configData.ConfigAddress, configSize);

                File.WriteAllBytes(output, firmwareData);

                System.Diagnostics.Debug.WriteLine($"[ConfigWriter] File written successfully to: {output}");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigWriter] Exception: {ex.Message}");
                throw new InvalidOperationException($"保存配置失败: {ex.Message}", ex);
            }
        }

        public static bool UpdateConfigValue(FirmwareConfigData configData, ConfigId configId, uint newValue)
        {
            try
            {
                int index = (int)configId;
                if (index < 0 || index >= ConfigParser.CONFIG_FLAGS_COUNT)
                {
                    return false;
                }

                configData.Flags[index] = newValue;

                configData.CheckSum = configData.CalculateCheckSum();
                configData.IsValid = true;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool BatchUpdateConfigValues(FirmwareConfigData configData, Dictionary<ConfigId, uint> updates)
        {
            try
            {
                foreach (var kvp in updates)
                {
                    UpdateConfigValue(configData, kvp.Key, kvp.Value);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool ResetToDefaults(FirmwareConfigData configData)
        {
            try
            {
                // 清零所有flag
                Array.Clear(configData.Flags, 0, ConfigParser.CONFIG_FLAGS_COUNT);

                // 填充已知配置项的默认值（使用当前模板）
                var defaultValues = ConfigTemplateManager.CurrentTemplate.DefaultValues;
                foreach (var kvp in defaultValues)
                {
                    int index = (int)kvp.Key;
                    if (index >= 0 && index < configData.Flags.Length)
                    {
                        configData.Flags[index] = kvp.Value;
                    }
                }

                // 重新计算校验和
                configData.CheckSum = configData.CalculateCheckSum();
                configData.IsValid = true;
                
                int activeCount = 0;
                if (configData.Mapping != null)
                {
                    activeCount = configData.Mapping.ConfigItemCount;
                }
                else if (configData.ProjectType != ProjectType.Unknown)
                {
                    var mapping = ProjectConfigMappingDatabase.GetMapping(configData.ProjectType);
                    if (mapping != null)
                    {
                        activeCount = mapping.ConfigItemCount;
                    }
                }
                if (activeCount == 0)
                {
                    activeCount = ConfigParser.DetectActiveConfigCount(configData.Flags);
                }
                configData.ActiveConfigCount = activeCount;
                configData.ConfigVersion = $"V1.3 ({ConfigTemplateManager.CurrentTemplate.Name})";

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool ResetToDefaults(FirmwareConfigData configData, ConfigTemplateId templateId)
        {
            try
            {
                // 清零所有flag
                Array.Clear(configData.Flags, 0, ConfigParser.CONFIG_FLAGS_COUNT);

                // 填充指定模板的配置项默认值
                var defaultValues = ConfigTemplateManager.GetDefaultValues(templateId);
                foreach (var kvp in defaultValues)
                {
                    int index = (int)kvp.Key;
                    if (index >= 0 && index < configData.Flags.Length)
                    {
                        configData.Flags[index] = kvp.Value;
                    }
                }

                // 重新计算校验和
                configData.CheckSum = configData.CalculateCheckSum();
                configData.IsValid = true;
                
                int activeCount = 0;
                if (configData.Mapping != null)
                {
                    activeCount = configData.Mapping.ConfigItemCount;
                }
                else if (configData.ProjectType != ProjectType.Unknown)
                {
                    var mapping = ProjectConfigMappingDatabase.GetMapping(configData.ProjectType);
                    if (mapping != null)
                    {
                        activeCount = mapping.ConfigItemCount;
                    }
                }
                if (activeCount == 0)
                {
                    activeCount = ConfigParser.DetectActiveConfigCount(configData.Flags);
                }
                configData.ActiveConfigCount = activeCount;
                
                var template = ConfigTemplateManager.GetTemplate(templateId.ToString());
                configData.ConfigVersion = $"V1.3 ({template.Name})";

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 根据项目类型重置配置为默认值
        /// </summary>
        /// <param name="configData">配置数据</param>
        /// <param name="projectType">项目类型</param>
        /// <returns>是否成功</returns>
        public static bool ResetToDefaults(FirmwareConfigData configData, ProjectType projectType)
        {
            try
            {
                // 清零所有flag
                Array.Clear(configData.Flags, 0, ConfigParser.CONFIG_FLAGS_COUNT);

                // 获取项目映射
                var mapping = ProjectConfigMappingDatabase.GetMapping(projectType);
                if (mapping == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigWriter] No mapping found for project type: {projectType}");
                    return false;
                }

                // 根据映射填充默认值
                foreach (var kvp in mapping.DefaultValues)
                {
                    string configName = kvp.Key;
                    uint defaultValue = kvp.Value;

                    // 尝试解析配置名为 ConfigId
                    if (Enum.TryParse<ConfigId>(configName, out var configId))
                    {
                        int index = (int)configId;
                        if (index >= 0 && index < configData.Flags.Length)
                        {
                            configData.Flags[index] = defaultValue;
                        }
                    }
                }

                // 重新计算校验和
                configData.CheckSum = configData.CalculateCheckSum();
                configData.IsValid = true;
                configData.ActiveConfigCount = mapping.ConfigItemCount;
                configData.ConfigVersion = $"V1.3 ({mapping.ProjectName})";
                configData.ProjectType = projectType;
                configData.Mapping = mapping;

                System.Diagnostics.Debug.WriteLine($"[ConfigWriter] Reset to defaults for project: {mapping.ProjectName}, config count: {mapping.ConfigItemCount}");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigWriter] ResetToDefaults failed: {ex.Message}");
                return false;
            }
        }

        public static bool ResetFromCParsedItems(FirmwareConfigData configData, List<ConfigCParsedItem> parsedItems)
        {
            try
            {
                Array.Clear(configData.Flags, 0, ConfigParser.CONFIG_FLAGS_COUNT);

                foreach (var item in parsedItems)
                {
                    if (Enum.TryParse<ConfigId>(item.ConfigName, out var configId))
                    {
                        int index = (int)configId;
                        if (index >= 0 && index < configData.Flags.Length)
                        {
                            configData.Flags[index] = item.Value;
                            System.Diagnostics.Debug.WriteLine($"[ConfigWriter] Set {item.ConfigName} (index={index}) = 0x{item.Value:X8}");
                        }
                    }
                }

                configData.CheckSum = configData.CalculateCheckSum();
                configData.IsValid = true;
                
                int maxIndex = -1;
                foreach (var item in parsedItems)
                {
                    if (Enum.TryParse<ConfigId>(item.ConfigName, out var configId))
                    {
                        int index = (int)configId;
                        if (index > maxIndex)
                        {
                            maxIndex = index;
                        }
                    }
                }
                configData.ActiveConfigCount = maxIndex >= 0 ? maxIndex + 1 : parsedItems.Count;
                configData.ConfigVersion = "V1.3 (from config.c)";

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigWriter] ResetFromCParsedItems failed: {ex.Message}");
                return false;
            }
        }

        public static bool ResetFromXmlParsedItems(FirmwareConfigData configData, List<ConfigXmlParsedItem> parsedItems)
        {
            try
            {
                if (configData == null || parsedItems == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ConfigWriter] ResetFromXmlParsedItems failed: null parameters");
                    return false;
                }

                Array.Clear(configData.Flags, 0, ConfigParser.CONFIG_FLAGS_COUNT);

                var writtenIndexes = new HashSet<int>();

                foreach (var item in parsedItems)
                {
                    int targetIndex = -1;
                    bool isByName = false;

                    if (Enum.TryParse<ConfigId>(item.ConfigName, out var configId))
                    {
                        targetIndex = (int)configId;
                        isByName = true;
                    }
                    else if (item.Index >= 0)
                    {
                        targetIndex = item.Index;
                    }

                    if (targetIndex < 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConfigWriter] Skip item {item.ConfigName}: invalid index");
                        continue;
                    }

                    if (targetIndex >= configData.Flags.Length)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConfigWriter] Skip item {item.ConfigName}: index {targetIndex} out of range (max={configData.Flags.Length - 1})");
                        continue;
                    }

                    if (writtenIndexes.Contains(targetIndex))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConfigWriter] Warning: Overwriting index {targetIndex} for {item.ConfigName}");
                    }

                    configData.Flags[targetIndex] = item.Value;
                    writtenIndexes.Add(targetIndex);

                    string logSource = isByName ? $"by name {item.ConfigName}" : "by index";
                    System.Diagnostics.Debug.WriteLine($"[ConfigWriter] Set index={targetIndex} = 0x{item.Value:X8} ({logSource})");
                }

                configData.CheckSum = configData.CalculateCheckSum();
                configData.IsValid = true;
                
                int maxIndex = -1;
                foreach (var item in parsedItems)
                {
                    int index = item.Index;
                    if (index < 0 && Enum.TryParse<ConfigId>(item.ConfigName, out var configId))
                    {
                        index = (int)configId;
                    }
                    if (index >= 0 && index < configData.Flags.Length && index > maxIndex)
                    {
                        maxIndex = index;
                    }
                }
                configData.ActiveConfigCount = maxIndex >= 0 ? maxIndex + 1 : parsedItems.Count;
                configData.ConfigVersion = "V1.3 (from XML)";

                configData.XmlParsedItems = new List<ConfigXmlParsedItem>(parsedItems);

                System.Diagnostics.Debug.WriteLine($"[ConfigWriter] ResetFromXmlParsedItems completed: {writtenIndexes.Count} indexes written, max index={maxIndex}, saved {parsedItems.Count} XML parsed items");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigWriter] ResetFromXmlParsedItems failed: {ex.Message}");
                return false;
            }
        }

        public static string ExportConfigAsText(FirmwareConfigData configData, List<FirmwareConfigItem> configItems)
        {
            var lines = new List<string>
            {
                "========================================",
                "固件配置导出",
                $"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"配置区地址: 0x{configData.ConfigAddress:X}",
                $"校验和: 0x{configData.CheckSum:X}",
                $"状态: {(configData.IsValid ? "有效" : "无效")}",
                "========================================",
                ""
            };

            string lastCategory = null;
            foreach (var item in configItems)
            {
                if (item.Category != lastCategory)
                {
                    lines.Add($"【{item.Category}】");
                    lastCategory = item.Category;
                }

                lines.Add($"  {item.Name}: {item.ValueDisplay} (0x{item.Value:X4})");
            }

            lines.Add("");
            lines.Add("========================================");
            lines.Add("原始数据 (HEX)");
            lines.Add("========================================");

            for (int i = 0; i < ConfigParser.CONFIG_FLAGS_COUNT; i++)
            {
                if (configData.Flags[i] != 0)
                {
                    lines.Add($"  Flag[{i:00}] = 0x{configData.Flags[i]}");
                }
            }

            return string.Join("\r\n", lines);
        }
    }
}
