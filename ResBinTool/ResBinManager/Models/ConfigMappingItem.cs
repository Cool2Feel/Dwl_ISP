using System;

namespace ResBinManager.Models
{
    public class ConfigMappingItem
    {
        public int Index { get; set; }
        public string ConfigName { get; set; } = string.Empty;
        public object? DefaultValue { get; set; }
        public string Description { get; set; } = string.Empty;

        public uint GetDefaultValueAsUInt()
        {
            if (DefaultValue == null)
                return 0;

            if (DefaultValue is uint uintValue)
                return uintValue;

            if (DefaultValue is int intValue)
                return (uint)intValue;

            if (DefaultValue is string stringValue)
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
    }

    public class ProjectMappingConfig
    {
        public string Version { get; set; } = "1.0";
        public string ProjectType { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public System.Collections.Generic.List<ConfigMappingItem> Mappings { get; set; } = new();
        public ProjectFeatures Features { get; set; } = new();
    }

    public class ProjectFeatures
    {
        public bool HasPrinter { get; set; }
        public bool HasBattery { get; set; }
        public bool HasIrLed { get; set; }
        public bool HasGsensor { get; set; }
        public bool HasMotionDetection { get; set; }
        public bool HasParkMode { get; set; }
    }
}
