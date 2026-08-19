using System;
using System.Collections.Generic;
using System.Linq;

namespace ResBinManager.Models
{
    /// <summary>
    /// 配置项描述符
    /// 将元数据、选项列表、显示格式合并到单一数据结构，简化配置项构建流程
    /// </summary>
    public class ConfigItemDescriptor
    {
        /// <summary>
        /// 配置项 ID
        /// </summary>
        public ConfigId Id { get; set; }

        /// <summary>
        /// 配置项名称（英文标识）
        /// </summary>
        public string ConfigName { get; set; } = string.Empty;

        /// <summary>
        /// 显示名称（中文）
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 配置项分类
        /// </summary>
        public string Category { get; set; } = "其他";

        /// <summary>
        /// 配置项类型
        /// </summary>
        public ConfigItemType Type { get; set; } = ConfigItemType.RawHex;

        /// <summary>
        /// 描述信息
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 可选值列表
        /// </summary>
        public List<ConfigOption> Options { get; set; } = new List<ConfigOption>();

        /// <summary>
        /// 显示文本格式化函数
        /// </summary>
        public Func<uint, string> DisplayFormatter { get; set; } = (value) => $"0x{value:X8}";

        /// <summary>
        /// 根据值创建配置项
        /// </summary>
        public FirmwareConfigItem CreateConfigItem(uint value)
        {
            string displayText = DisplayFormatter(value);
            var finalOptions = Options != null ? new List<ConfigOption>(Options) : new List<ConfigOption>();
            
            if (!finalOptions.Any(o => o.Value == value))
            {
                finalOptions.Add(new ConfigOption(value, displayText));
            }

            return new FirmwareConfigItem
            {
                Id = Id,
                Name = DisplayName,
                Value = value,
                ValueDisplay = displayText,
                Category = Category,
                Options = finalOptions
            };
        }

        /// <summary>
        /// 应用元数据覆盖
        /// </summary>
        public ConfigItemDescriptor ApplyOverride(ConfigItemMetadataOverride? metadataOverride)
        {
            if (metadataOverride == null)
                return this;

            // 创建副本避免修改原始描述符
            var descriptor = new ConfigItemDescriptor
            {
                Id = Id,
                ConfigName = ConfigName,
                DisplayName = DisplayName,
                Category = Category,
                Type = Type,
                Description = Description,
                Options = Options,
                DisplayFormatter = DisplayFormatter
            };

            // 应用覆盖
            if (!string.IsNullOrEmpty(metadataOverride.DisplayName))
                descriptor.DisplayName = metadataOverride.DisplayName;
            if (!string.IsNullOrEmpty(metadataOverride.Category))
                descriptor.Category = metadataOverride.Category;
            if (metadataOverride.Type.HasValue)
                descriptor.Type = metadataOverride.Type.Value;
            if (!string.IsNullOrEmpty(metadataOverride.Description))
                descriptor.Description = metadataOverride.Description;

            return descriptor;
        }
    }
}
