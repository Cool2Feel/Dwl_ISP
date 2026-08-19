using System;
using System.Collections.Generic;

namespace ResBinManager.Models
{
    /// <summary>
    /// 配置项类型枚举
    /// </summary>
    public enum ConfigItemType
    {
        /// <summary>
        /// 开关类型（开启/关闭）
        /// </summary>
        OnOff,

        /// <summary>
        /// 语言选择
        /// </summary>
        Language,

        /// <summary>
        /// 时间类型（年/月/日/时/分/秒）
        /// </summary>
        Time,

        /// <summary>
        /// 数值类型（直接数值）
        /// </summary>
        Numeric,

        /// <summary>
        /// 分辨率选择
        /// </summary>
        Resolution,

        /// <summary>
        /// 曝光补偿
        /// </summary>
        ExposureValue,

        /// <summary>
        /// 白平衡
        /// </summary>
        WhiteBalance,

        /// <summary>
        /// 自动开关（关闭/开启/自动）
        /// </summary>
        AutoOnOff,

        /// <summary>
        /// 频率选择（50Hz/60Hz）
        /// </summary>
        Frequency,

        /// <summary>
        /// 自动关机时间
        /// </summary>
        AutoOffTime,

        /// <summary>
        /// 屏保时间
        /// </summary>
        ScreenSaveTime,

        /// <summary>
        /// 循环录像时间
        /// </summary>
        LoopTime,

        /// <summary>
        /// 等级/级别（0~9）
        /// </summary>
        Level,

        /// <summary>
        /// 灵敏度（高/中/低）
        /// </summary>
        Sensitivity,

        /// <summary>
        /// 星期
        /// </summary>
        WeekDay,

        /// <summary>
        /// 录像速度（正常/慢速/快速）
        /// </summary>
        VideoSpeed,

        /// <summary>
        /// 原始十六进制（未知类型）
        /// </summary>
        RawHex
    }

    /// <summary>
    /// 配置项元数据
    /// </summary>
    public class ConfigItemMetadata
    {
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
    }

    /// <summary>
    /// 配置项元数据注册表（优化版）
    /// 使用 ConfigItemDescriptor 统一管理元数据、选项列表和显示格式
    /// </summary>
    public static class ConfigItemRegistry
    {
        // 保留旧的元数据字典（向后兼容）
        private static Dictionary<string, ConfigItemMetadata> _metadata = new();
        
        // 新增：描述符字典（优化后的核心数据结构）
        private static Dictionary<string, ConfigItemDescriptor> _descriptors = new();
        private static bool _descriptorsInitialized = false;

        /// <summary>
        /// 初始化内置配置项元数据
        /// </summary>
        public static void Initialize()
        {
            // 时间设置
            Register("CONFIG_ID_YEAR", "年", "时间设置", ConfigItemType.Time, "年份");
            Register("CONFIG_ID_MONTH", "月", "时间设置", ConfigItemType.Time, "月份");
            Register("CONFIG_ID_MDAY", "日", "时间设置", ConfigItemType.Time, "日期");
            Register("CONFIG_ID_WDAY", "星期", "时间设置", ConfigItemType.WeekDay, "星期");
            Register("CONFIG_ID_HOUR", "时", "时间设置", ConfigItemType.Time, "小时");
            Register("CONFIG_ID_MIN", "分", "时间设置", ConfigItemType.Time, "分钟");
            Register("CONFIG_ID_SEC", "秒", "时间设置", ConfigItemType.Time, "秒");

            // 系统设置
            Register("CONFIG_ID_LANGUAGE", "默认语言", "系统设置", ConfigItemType.Language, "系统默认语言");
            Register("CONFIG_ID_AUTOOFF", "自动关机", "系统设置", ConfigItemType.AutoOffTime, "自动关机时间");
            Register("CONFIG_ID_SCREENSAVE", "屏幕保护", "显示设置", ConfigItemType.ScreenSaveTime, "屏幕保护时间");
            Register("CONFIG_ID_FREQUNCY", "电源频率", "系统设置", ConfigItemType.Frequency, "电源频率（50Hz/60Hz）");
            Register("CONFIG_ID_PARKMODE", "停车模式", "系统设置", ConfigItemType.OnOff, "停车监控模式");
            Register("CONFIG_ID_GSENSOR", "G-Sensor", "系统设置", ConfigItemType.Sensitivity, "重力传感器灵敏度");
            Register("CONFIG_ID_KEYSOUND", "按键声音", "声音设置", ConfigItemType.OnOff, "按键音开关");
            Register("CONFIG_ID_THUMBNAIL", "缩略图", "系统设置", ConfigItemType.OnOff, "缩略图显示");
            Register("CONFIG_ID_GSENSORMODE", "G-Sensor 模式", "系统设置", ConfigItemType.OnOff, "G-Sensor工作模式");
            Register("CONFIG_ID_FORMAT", "格式化", "系统设置", ConfigItemType.OnOff, "格式化存储");
            Register("CONFIG_ID_DEFUALT", "恢复默认", "系统设置", ConfigItemType.OnOff, "恢复默认设置");
            Register("CONFIG_ID_REINIT", "重新初始化", "系统设置", ConfigItemType.OnOff, "重新初始化系统");

            // 显示设置
            Register("CONFIG_ID_ROTATE", "旋转", "显示设置", ConfigItemType.OnOff, "画面旋转");
            Register("CONFIG_ID_FILLIGHT", "补光灯", "显示设置", ConfigItemType.OnOff, "补光灯开关");
            Register("CONFIG_ID_IR_LED", "红外灯", "显示设置", ConfigItemType.OnOff, "红外灯开关");

            // 录像设置
            Register("CONFIG_ID_RESOLUTION", "视频分辨率", "录像设置", ConfigItemType.Resolution, "录像分辨率");
            Register("CONFIG_ID_TIMESTAMP", "时间戳", "录像设置", ConfigItemType.OnOff, "录像时间戳");
            Register("CONFIG_ID_MOTIONDECTION", "移动侦测", "录像设置", ConfigItemType.OnOff, "移动侦测功能");
            Register("CONFIG_ID_LOOPTIME", "循环录像时间", "录像设置", ConfigItemType.LoopTime, "循环录像间隔");
            Register("CONFIG_ID_AUDIOREC", "录音", "录像设置", ConfigItemType.OnOff, "录音开关");
            Register("CONFIG_ID_EV", "曝光补偿", "录像设置", ConfigItemType.ExposureValue, "录像曝光补偿");
            Register("CONFIG_ID_WBLANCE", "白平衡", "录像设置", ConfigItemType.WhiteBalance, "录像白平衡");
            Register("CONFIG_ID_VIDEORECEFFECT", "录像特效", "录像设置", ConfigItemType.OnOff, "录像特效开关");
            Register("CONFIG_ID_VIDEOSPEED", "录像速度", "录像设置", ConfigItemType.VideoSpeed, "录像速度（正常/慢速/快速）");
            Register("CONFIG_ID_LINEASSIST", "辅助线", "拍照设置", ConfigItemType.OnOff, "拍照辅助线开关");

            // 拍照设置
            Register("CONFIG_ID_TIMEPHOTO", "定时拍照", "拍照设置", ConfigItemType.OnOff, "定时拍照功能");
            Register("CONFIG_ID_PRESLUTION", "拍照分辨率", "拍照设置", ConfigItemType.Resolution, "拍照分辨率");
            Register("CONFIG_ID_PFASTVIEW", "快速预览", "拍照设置", ConfigItemType.AutoOffTime, "拍照后快速预览");
            Register("CONFIG_ID_PTIMESTRAMP", "照片时间戳", "拍照设置", ConfigItemType.OnOff, "照片时间戳");
            Register("CONFIG_ID_PEV", "照片曝光补偿", "拍照设置", ConfigItemType.ExposureValue, "拍照曝光补偿");

            // 声音设置
            Register("CONFIG_ID_VOLUME", "音量", "声音设置", ConfigItemType.Numeric, "系统音量");

            // ISP设置
            Register("CONFIG_ID_ISP_FILTER", "ISP滤镜", "录像设置", ConfigItemType.WhiteBalance, "ISP滤镜选择");

            // 打印机设置
            Register("CONFIG_ID_PRINTER_EN", "打印机", "打印机设置", ConfigItemType.OnOff, "打印机使能");
            Register("CONFIG_ID_COLOR_PRINT", "彩色打印", "打印机设置", ConfigItemType.OnOff, "彩色打印开关");
            Register("CONFIG_ID_PRINTER_DENSITY", "打印浓度", "打印机设置", ConfigItemType.Level, "打印浓度等级");
            Register("CONFIG_ID_PRINTER_MODE", "打印模式", "打印机设置", ConfigItemType.OnOff, "打印模式选择");
            Register("CONFIG_ID_PRINTER_NEARFAR", "打印远近", "打印机设置", ConfigItemType.Level, "打印头距离");
            Register("CONFIG_ID_PRINTER_DELAY", "打印延迟", "打印机设置", ConfigItemType.Numeric, "打印延迟时间");

            // 电池设置
            Register("CONFIG_ID_BAT_OLD", "电池老化", "系统设置", ConfigItemType.Level, "电池老化等级");
            Register("CONFIG_ID_BAT_CHECK_FLAG", "电池检测标志", "系统设置", ConfigItemType.OnOff, "电池更新标志");

            // 设备ID
            Register("CONFIG_ID_DEVICE_ID1", "设备ID1", "系统设置", ConfigItemType.Numeric, "设备ID第1字节");
            Register("CONFIG_ID_DEVICE_ID2", "设备ID2", "系统设置", ConfigItemType.Numeric, "设备ID第2字节");
            Register("CONFIG_ID_DEVICE_ID3", "设备ID3", "系统设置", ConfigItemType.Numeric, "设备ID第3字节");
            Register("CONFIG_ID_DEVICE_ID4", "设备ID4", "系统设置", ConfigItemType.Numeric, "设备ID第4字节");
            Register("CONFIG_ID_DEVICE_ID5", "设备ID5", "系统设置", ConfigItemType.Numeric, "设备ID第5字节");
            Register("CONFIG_ID_DEVICE_ID6", "设备ID6", "系统设置", ConfigItemType.Numeric, "设备ID第6字节");

            // 打印机高级设置
            Register("CONFIG_ID_PRINTER_DENSITY_H", "打印浓度H", "打印机设置", ConfigItemType.Numeric, "打印浓度高字节");
            Register("CONFIG_ID_PRINTER_DENSITY_L", "打印浓度L", "打印机设置", ConfigItemType.Numeric, "打印浓度低字节");
            Register("CONFIG_ID_PRINTER_MOTE_SPEED", "马达速度", "打印机设置", ConfigItemType.Numeric, "打印马达速度");

            // 蓝牙LED
            Register("CONFIG_ID_BT_LED", "蓝牙LED", "显示设置", ConfigItemType.OnOff, "蓝牙指示灯");

            // 新增配置项
            Register("CONFIG_ID_MOREPHOTO", "连拍", "拍照设置", ConfigItemType.OnOff, "连拍模式");
            Register("CONFIG_ID_LCD_BRIGHT", "LCD 亮度", "显示设置", ConfigItemType.Level, "LCD屏幕亮度");
            Register("CONFIG_ID_VIDEO_RESOLUTION", "视频分辨率", "录像设置", ConfigItemType.Resolution, "视频分辨率");
            Register("CONFIG_ID_NETWORK_SPEED", "网络速度", "系统设置", ConfigItemType.Level, "网络速度等级");

            // 标记描述符需要重建
            _descriptorsInitialized = false;
        }

        /// <summary>
        /// 注册配置项元数据（向后兼容）
        /// </summary>
        public static void Register(string configName, string displayName, string category, ConfigItemType type, string description = "")
        {
            _metadata[configName] = new ConfigItemMetadata
            {
                ConfigName = configName,
                DisplayName = displayName,
                Category = category,
                Type = type,
                Description = description
            };
        }

        /// <summary>
        /// 注册完整的配置项描述符（优化版）
        /// </summary>
        public static void RegisterDescriptor(ConfigItemDescriptor descriptor)
        {
            _descriptors[descriptor.ConfigName] = descriptor;
        }

        /// <summary>
        /// 获取配置项描述符（优化版核心方法）
        /// 懒加载：首次调用时自动构建描述符字典
        /// </summary>
        public static ConfigItemDescriptor? GetDescriptor(string configName)
        {
            EnsureDescriptorsInitialized();
            return _descriptors.TryGetValue(configName, out var descriptor) ? descriptor : null;
        }

        /// <summary>
        /// 尝试获取配置项描述符
        /// </summary>
        public static bool TryGetDescriptor(string configName, out ConfigItemDescriptor? descriptor)
        {
            EnsureDescriptorsInitialized();
            if (_descriptors.TryGetValue(configName, out var desc))
            {
                descriptor = desc;
                return true;
            }
            descriptor = null;
            return false;
        }

        /// <summary>
        /// 确保描述符字典已初始化（懒加载）
        /// </summary>
        private static void EnsureDescriptorsInitialized()
        {
            if (_descriptorsInitialized)
                return;

            BuildDescriptors();
            _descriptorsInitialized = true;
        }

        /// <summary>
        /// 构建描述符字典
        /// 将元数据、选项列表、显示格式合并到单一数据结构
        /// </summary>
        private static void BuildDescriptors()
        {
            _descriptors.Clear();

            foreach (var kvp in _metadata)
            {
                var meta = kvp.Value;
                
                // 尝试解析 ConfigId
                if (!Enum.TryParse<ConfigId>(meta.ConfigName, out var configId))
                    configId = ConfigId.CONFIG_ID_MAX;

                var descriptor = new ConfigItemDescriptor
                {
                    Id = configId,
                    ConfigName = meta.ConfigName,
                    DisplayName = meta.DisplayName,
                    Category = meta.Category,
                    Type = meta.Type,
                    Description = meta.Description,
                    Options = ConfigOptionsCache.GetOptions(meta.Type),
                    DisplayFormatter = ConfigDisplayFormatters.GetFormatter(meta.Type)
                };

                _descriptors[meta.ConfigName] = descriptor;
            }

            System.Diagnostics.Debug.WriteLine($"[ConfigItemRegistry] Built {_descriptors.Count} descriptors");
        }

        /// <summary>
        /// 获取配置项元数据（向后兼容）
        /// </summary>
        public static ConfigItemMetadata? GetMetadata(string configName)
        {
            return _metadata.TryGetValue(configName, out var metadata) ? metadata : null;
        }

        /// <summary>
        /// 获取配置项元数据，如果不存在则返回默认元数据（向后兼容）
        /// </summary>
        public static ConfigItemMetadata GetMetadataOrDefault(string configName)
        {
            return GetMetadata(configName) ?? new ConfigItemMetadata
            {
                ConfigName = configName,
                DisplayName = configName,
                Category = "其他",
                Type = ConfigItemType.RawHex,
                Description = "未知配置项"
            };
        }

        /// <summary>
        /// 检查配置项是否已注册
        /// </summary>
        public static bool IsRegistered(string configName)
        {
            return _metadata.ContainsKey(configName);
        }

        /// <summary>
        /// 获取所有已注册的配置项名称
        /// </summary>
        public static IEnumerable<string> GetAllRegisteredNames()
        {
            return _metadata.Keys;
        }

        /// <summary>
        /// 清除所有注册（主要用于测试）
        /// </summary>
        public static void Clear()
        {
            _metadata.Clear();
            _descriptors.Clear();
            _descriptorsInitialized = false;
        }
    }
}
