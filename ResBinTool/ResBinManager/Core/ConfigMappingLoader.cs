using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ResBinManager.Models;

namespace ResBinManager.Core
{
    public class ConfigMappingLoader
    {
        private readonly string _mappingsDirectory;
        private Dictionary<string, ProjectConfigMapping> _mappingsCache;

        public ConfigMappingLoader(string mappingsDirectory)
        {
            _mappingsDirectory = mappingsDirectory;
            _mappingsCache = new Dictionary<string, ProjectConfigMapping>();
        }

        public ProjectConfigMapping? LoadMapping(string projectType)
        {
            if (_mappingsCache.TryGetValue(projectType, out var cachedMapping))
            {
                return cachedMapping;
            }

            string[] searchPatterns = {
                $"{projectType}.json",
                $"{projectType.ToLower()}.json",
                $"{projectType.ToUpper()}.json"
            };

            foreach (var pattern in searchPatterns)
            {
                string filePath = Path.Combine(_mappingsDirectory, pattern);
                if (File.Exists(filePath))
                {
                    try
                    {
                        string jsonContent = File.ReadAllText(filePath);
                        var mapping = JsonSerializer.Deserialize<ProjectConfigMapping>(jsonContent);
                        
                        if (mapping != null)
                        {
                            _mappingsCache[projectType] = mapping;
                            System.Diagnostics.Debug.WriteLine($"[ConfigMappingLoader] Loaded mapping for {projectType} from {filePath}");
                            return mapping;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConfigMappingLoader] Failed to load {filePath}: {ex.Message}");
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[ConfigMappingLoader] No mapping found for {projectType}");
            return null;
        }

        public List<string> GetAvailableProjects()
        {
            List<string> projects = new List<string>();

            if (!Directory.Exists(_mappingsDirectory))
                return projects;

            var jsonFiles = Directory.GetFiles(_mappingsDirectory, "*.json");
            foreach (var filePath in jsonFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (!string.IsNullOrEmpty(fileName))
                {
                    projects.Add(fileName);
                }
            }

            return projects;
        }

        public bool ApplyMappingToConfig(ProjectConfigMapping mapping, ConfigManager configManager)
        {
            try
            {
                foreach (var item in mapping.Mappings)
                {
                    uint defaultValue = ParseDefaultValue(item.DefaultValue);
                    configManager.SetConfigValue(item.Index, defaultValue);
                }

                configManager.SetMapping(mapping);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigMappingLoader] Apply mapping error: {ex.Message}");
                return false;
            }
        }

        private uint ParseDefaultValue(object? value)
        {
            if (value == null)
                return 0;

            if (value is uint uintValue)
                return uintValue;

            if (value is int intValue)
                return (uint)intValue;

            if (value is string stringValue)
            {
                if (stringValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (uint.TryParse(stringValue.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint hexValue))
                    {
                        return hexValue;
                    }
                }
                else if (uint.TryParse(stringValue, out uint decValue))
                {
                    return decValue;
                }
            }

            return 0;
        }

        public List<FirmwareConfigItem> BuildConfigItemList(ConfigManager configManager)
        {
            List<FirmwareConfigItem> items = new List<FirmwareConfigItem>();
            var mapping = configManager.Mapping;

            for (int i = 0; i < 127; i++)
            {
                ConfigId configId = (ConfigId)i;
                string configName = configId.ToString();
                uint value = configManager.GetConfigValue(configId);

                var mappingItem = mapping?.Mappings?.FirstOrDefault(m => m.Index == i);
                string description = mappingItem?.Description ?? string.Empty;

                FirmwareConfigItem item = new FirmwareConfigItem
                {
                    Id = configId,
                    Name = configName,
                    Value = value,
                    ValueDisplay = configManager.BuildConfigItemList().FirstOrDefault(c => c.Id == configId)?.ValueDisplay ?? $"0x{value:X8}",
                    Category = configManager.BuildConfigItemList().FirstOrDefault(c => c.Id == configId)?.Category ?? "Other",
                    Options = configManager.BuildConfigItemList().FirstOrDefault(c => c.Id == configId)?.Options ?? new List<ConfigOption>()
                };

                if (!string.IsNullOrEmpty(description))
                {
                    item.Name = description;
                }

                items.Add(item);
            }

            return items;
        }
    }
}