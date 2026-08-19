using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace ResBinManager.Core
{
    public class ConfigXmlGenerator
    {
        public class XmlConfigItem
        {
            public int Index { get; set; }
            public string ConfigName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public uint DefaultValue { get; set; }
            public string DefaultValueStr { get; set; } = string.Empty;
            public List<XmlOption> Options { get; set; } = new List<XmlOption>();
            public bool Enabled { get; set; } = false;
        }

        public class XmlOption
        {
            public uint Value { get; set; }
            public string ValueStr { get; set; } = string.Empty;
            public string Display { get; set; } = string.Empty;
        }

        public bool Generate(ConfigSourceParser.ParseResult parseResult,
                            MenuParser.ParseResult? menuResult,
                            string outputFilePath)
        {
            try
            {
                var xmlItems = BuildXmlConfigItems(parseResult, menuResult);
                string xmlContent = BuildXmlContent(parseResult.ProjectName, xmlItems, parseResult.StringConstants, parseResult.RIdTypeStrBase, parseResult.ResourceDefinitions);

                string outputDir = Path.GetDirectoryName(outputFilePath);
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                File.WriteAllText(outputFilePath, xmlContent, Encoding.UTF8);

                System.Diagnostics.Debug.WriteLine($"[ConfigXmlGenerator] Generated XML file: {outputFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigXmlGenerator] Error: {ex.Message}");
                return false;
            }
        }

        private List<XmlConfigItem> BuildXmlConfigItems(
            ConfigSourceParser.ParseResult parseResult,
            MenuParser.ParseResult? menuResult)
        {
            var xmlItems = new List<XmlConfigItem>();
            var menuOptionMap = new Dictionary<string, List<string>>();
            var enabledConfigMap = new Dictionary<string, bool>();
            var existingConfigNames = new HashSet<string>();

            if (menuResult != null && menuResult.Success)
            {
                foreach (var menu in menuResult.MenuOptions)
                {
                    menuOptionMap[menu.MenuName] = menu.Options;
                }

                if (menuResult.EnabledConfigIds != null)
                {
                    foreach (var kvp in menuResult.EnabledConfigIds)
                    {
                        enabledConfigMap[kvp.Key] = kvp.Value;
                    }
                }
            }

            int nextIndex = parseResult.ConfigItems.Count > 0 
                ? parseResult.ConfigItems.Max(c => c.Index) + 1 
                : 0;

            foreach (var item in parseResult.ConfigItems)
            {
                existingConfigNames.Add(item.Name);

                bool isEnabled = false;
                if (enabledConfigMap.ContainsKey(item.Name))
                {
                    isEnabled = enabledConfigMap[item.Name];
                }

                var xmlItem = new XmlConfigItem
                {
                    Index = item.Index,
                    ConfigName = item.Name,
                    DefaultValue = item.DefaultValue,
                    DefaultValueStr = FormatValue(item.DefaultValue),
                    Description = item.Comment,
                    Category = GetCategory(item.Name),
                    Type = GetConfigType(item.Name),
                    DisplayName = GetDisplayName(item.Name),
                    Enabled = isEnabled
                };

                if (string.IsNullOrEmpty(xmlItem.DisplayName))
                {
                    xmlItem.DisplayName = item.Name;
                }

                if (string.IsNullOrEmpty(xmlItem.Category))
                {
                    xmlItem.Category = "其他设置";
                }

                if (string.IsNullOrEmpty(xmlItem.Type))
                {
                    xmlItem.Type = "OnOff";
                }

                string menuName = GetMenuName(item.Name);
                if (isEnabled && menuOptionMap.ContainsKey(menuName) && menuOptionMap[menuName].Count > 0)
                {
                    foreach (var optionStr in menuOptionMap[menuName])
                    {
                        uint optionValue = ResolveStringConstant(parseResult.StringConstants, optionStr);
                        xmlItem.Options.Add(new XmlOption
                        {
                            Value = optionValue,
                            ValueStr = FormatValue(optionValue),
                            Display = GetOptionDisplay(optionStr)
                        });
                    }
                }
                else
                {
                    xmlItem.Options = GenerateDefaultOptions(item.Name, item.DefaultValue, parseResult.StringConstants);
                }

                if (xmlItem.Options == null || xmlItem.Options.Count == 0)
                {
                    xmlItem.Options = new List<XmlOption>
                    {
                        new XmlOption { Value = item.DefaultValue, ValueStr = FormatValue(item.DefaultValue), Display = "默认值" }
                    };
                }

                xmlItems.Add(xmlItem);
            }

            // 补充 menuMovieRec.c 中有但 config.c 中没有的配置项
            //if (menuResult != null && menuResult.Success)
            //{
            //    foreach (var kvp in enabledConfigMap)
            //    {
            //        string configName = kvp.Key;
            //        bool isEnabled = kvp.Value;

            //        if (!existingConfigNames.Contains(configName))
            //        {
            //            System.Diagnostics.Debug.WriteLine($"[ConfigXmlGenerator] Adding missing config item from menu: {configName}");

            //            var xmlItem = new XmlConfigItem
            //            {
            //                Index = nextIndex++,
            //                ConfigName = configName,
            //                DefaultValue = 0,
            //                DefaultValueStr = "0",
            //                Description = "从菜单配置补充",
            //                Category = GetCategory(configName),
            //                Type = GetConfigType(configName),
            //                DisplayName = GetDisplayName(configName),
            //                Enabled = isEnabled
            //            };

            //            if (string.IsNullOrEmpty(xmlItem.DisplayName))
            //            {
            //                xmlItem.DisplayName = configName;
            //            }

            //            if (string.IsNullOrEmpty(xmlItem.Category))
            //            {
            //                xmlItem.Category = "其他设置";
            //            }

            //            if (string.IsNullOrEmpty(xmlItem.Type))
            //            {
            //                xmlItem.Type = "OnOff";
            //            }

            //            string menuName = GetMenuName(configName);
            //            if (isEnabled && menuOptionMap.ContainsKey(menuName) && menuOptionMap[menuName].Count > 0)
            //            {
            //                foreach (var optionStr in menuOptionMap[menuName])
            //                {
            //                    uint optionValue = ResolveStringConstant(parseResult.StringConstants, optionStr);
            //                    xmlItem.Options.Add(new XmlOption
            //                    {
            //                        Value = optionValue,
            //                        ValueStr = FormatValue(optionValue),
            //                        Display = GetOptionDisplay(optionStr)
            //                    });
            //                }
            //            }
            //            else
            //            {
            //                xmlItem.Options = GenerateDefaultOptions(configName, 0, parseResult.StringConstants);
            //            }

            //            if (xmlItem.Options == null || xmlItem.Options.Count == 0)
            //            {
            //                xmlItem.Options = new List<XmlOption>
            //                {
            //                    new XmlOption { Value = 0, ValueStr = "0", Display = "默认值" }
            //                };
            //            }

            //            xmlItems.Add(xmlItem);
            //        }
            //    }
            //}

            var finalItems = xmlItems.OrderBy(x => x.Index).ToList();

            var seenIndexes = new HashSet<int>();
            for (int i = finalItems.Count - 1; i >= 0; i--)
            {
                if (seenIndexes.Contains(finalItems[i].Index))
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigXmlGenerator] Warning: Removing duplicate index {finalItems[i].Index} for {finalItems[i].ConfigName}");
                    finalItems.RemoveAt(i);
                }
                else
                {
                    seenIndexes.Add(finalItems[i].Index);
                }
            }

            return finalItems;
        }

        private string BuildXmlContent(string projectName, List<XmlConfigItem> items,
                                       Dictionary<string, uint> stringConstants, uint rIdTypeStrBase,
                                       Dictionary<uint, string> resourceDefinitions)
        {
            XNamespace ns = "";

            var configItemsElement = new XElement(ns + "ConfigItems");
            foreach (var item in items)
            {
                var optionsElement = new XElement(ns + "Options");
                foreach (var opt in item.Options)
                {
                    var optionElement = new XElement(ns + "Option", opt.Display);
                    optionElement.SetAttributeValue("value", opt.ValueStr);
                    optionsElement.Add(optionElement);
                }

                var configItemElement = new XElement(ns + "ConfigItem",
                    new XElement(ns + "Index", item.Index),
                    new XElement(ns + "ConfigName", item.ConfigName),
                    new XElement(ns + "DisplayName", item.DisplayName),
                    new XElement(ns + "Category", item.Category),
                    new XElement(ns + "Type", item.Type),
                    new XElement(ns + "Description", item.Description),
                    new XElement(ns + "DefaultValue", item.DefaultValueStr),
                    new XElement(ns + "Enabled", item.Enabled ? "true" : "false"),
                    optionsElement
                );
                configItemsElement.Add(configItemElement);
            }

            var featuresElement = new XElement(ns + "Features");
            string[] featureNames = { "hasBattery", "hasIrLed", "hasGsensor", "hasMotionDetection",
                                      "hasParkMode", "hasAudioRec", "hasTimestamp", "hasVideoResolution",
                                      "hasPhotoResolution", "hasLoopRecord" };
            foreach (var name in featureNames)
            {
                var featureElement = new XElement(ns + "Feature", "true");
                featureElement.SetAttributeValue("name", name);
                featuresElement.Add(featureElement);
            }

            var stringConstantsElement = new XElement(ns + "StringConstants");
            stringConstantsElement.SetAttributeValue("base", $"0x{rIdTypeStrBase:X8}");
            foreach (var kvp in stringConstants.OrderBy(kvp => kvp.Value))
            {
                var constantElement = new XElement(ns + "Constant", $"0x{kvp.Value:X8}");
                constantElement.SetAttributeValue("name", kvp.Key);
                stringConstantsElement.Add(constantElement);
            }

            // 写入 RES.H 资源定义映射（资源 Id -> 资源名称）
            var resourceDefinitionsElement = new XElement(ns + "ResourceDefinitions");
            foreach (var kvp in resourceDefinitions.OrderBy(kvp => kvp.Key))
            {
                var resElement = new XElement(ns + "Resource", kvp.Value);
                resElement.SetAttributeValue("id", kvp.Key.ToString());
                resourceDefinitionsElement.Add(resElement);
            }

            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(ns + "ConfigDefinition",
                    new XElement(ns + "ProjectInfo",
                        new XElement(ns + "Id", projectName),
                        new XElement(ns + "Name", projectName),
                        new XElement(ns + "Description", $"基于config.c userConfigReset()默认值自动生成"),
                        new XElement(ns + "Version", "1.0")
                    ),
                    stringConstantsElement,
                    resourceDefinitionsElement,
                    configItemsElement,
                    featuresElement
                )
            );

            return doc.ToString();
        }

        private string FormatValue(uint value)
        {
            if (value >= 0x81000000)
            {
                return $"0x{value:X8}";
            }
            return value.ToString();
        }

        private string GetCategory(string configName)
        {
            if (configName.Contains("YEAR") || configName.Contains("MONTH") || configName.Contains("DAY") ||
                configName.Contains("HOUR") || configName.Contains("MIN") || configName.Contains("SEC") ||
                configName.Contains("WDAY"))
                return "时间设置";

            if (configName.Contains("LANGUAGE") || configName.Contains("AUTOOFF") || configName.Contains("FREQUNCY") ||
                configName.Contains("PARKMODE") || configName.Contains("GSENSOR") || configName.Contains("KEYSOUND") ||
                configName.Contains("THUMBNAIL") || configName.Contains("GSENSORMODE") || configName.Contains("FORMAT") ||
                configName.Contains("DEFUALT") || configName.Contains("REINIT"))
                return "系统设置";

            if (configName.Contains("LCD_BRIGHT") || configName.Contains("SCREENSAVE") || configName.Contains("ROTATE") ||
                configName.Contains("FILLIGHT") || configName.Contains("IR_LED"))
                return "显示设置";

            if (configName.Contains("RESOLUTION") || configName.Contains("TIMESTAMP") || configName.Contains("MOTIONDECTION") ||
                configName.Contains("LOOPTIME") || configName.Contains("AUDIOREC") || configName.Contains("EV") ||
                configName.Contains("WBLANCE") || configName.Contains("VIDEORECEFFECT") || configName.Contains("VIDEOSPEED"))
                return "录像设置";

            if (configName.Contains("TIMEPHOTO") || configName.Contains("PRESLUTION") || configName.Contains("PFASTVIEW") ||
                configName.Contains("PTIMESTAMP") || configName.Contains("PEV") || configName.Contains("LINEASSIST"))
                return "拍照设置";

            if (configName.Contains("VOLUME"))
                return "声音设置";

            return "其他设置";
        }

        private string GetConfigType(string configName)
        {
            if (configName.Contains("YEAR") || configName.Contains("MONTH") || configName.Contains("DAY") ||
                configName.Contains("HOUR") || configName.Contains("MIN") || configName.Contains("SEC"))
                return "Time";

            if (configName.Contains("WDAY"))
                return "WeekDay";

            if (configName.Contains("LANGUAGE"))
                return "Language";

            if (configName.Contains("LCD_BRIGHT") || configName.Contains("VOLUME"))
                return "Level";

            if (configName.Contains("AUTOOFF") || configName.Contains("SCREENSAVE") ||
                configName.Contains("TIMEPHOTO") || configName.Contains("PFASTVIEW"))
                return "AutoOffTime";

            if (configName.Contains("FREQUNCY"))
                return "Frequency";

            if (configName.Contains("RESOLUTION") || configName.Contains("PRESLUTION"))
                return "Resolution";

            if (configName.Contains("GSENSOR"))
                return "Sensitivity";

            if (configName.Contains("LOOPTIME"))
                return "LoopTime";

            if (configName.Contains("ISP_FILTER"))
                return "WhiteBalance";

            if (configName.Contains("DEVICE_ID"))
                return "Numeric";

            if (configName.Contains("PRINTER_DENSITY_H") || configName.Contains("PRINTER_DENSITY_L") || configName.Contains("PRINTER_MOTE_SPEED"))
                return "Numeric";

            if (configName.Contains("EV") || configName.Contains("PEV"))
                return "ExposureValue";

            if (configName.Contains("WBLANCE"))
                return "WhiteBalance";

            if (configName.Contains("VOLUME"))
                return "Level";

            if (configName.Contains("PRINTER_DENSITY") && !configName.Contains("_H") && !configName.Contains("_L"))
                return "Level";

            if (configName.Contains("PRINTER_MODE"))
                return "OnOff";

            if (configName.Contains("PRINTER_NEARFAR"))
                return "OnOff";

            if (configName.Contains("BAT_OLD"))
                return "Level";

            if (configName.Contains("VIDEOSPEED"))
                return "VideoSpeed";

            return "OnOff";
        }

        private string GetDisplayName(string configName)
        {
            return configName switch
            {
                "CONFIG_ID_YEAR" => "年",
                "CONFIG_ID_MONTH" => "月",
                "CONFIG_ID_MDAY" => "日",
                "CONFIG_ID_WDAY" => "星期",
                "CONFIG_ID_HOUR" => "时",
                "CONFIG_ID_MIN" => "分",
                "CONFIG_ID_SEC" => "秒",
                "CONFIG_ID_LANGUAGE" => "默认语言",
                "CONFIG_ID_LCD_BRIGHT" => "LCD 亮度",
                "CONFIG_ID_AUTOOFF" => "自动关机",
                "CONFIG_ID_SCREENSAVE" => "屏幕保护",
                "CONFIG_ID_FREQUNCY" => "光源频率",
                "CONFIG_ID_ROTATE" => "图片旋转",
                "CONFIG_ID_FILLIGHT" => "补光灯",
                "CONFIG_ID_RESOLUTION" => "视频分辨率",
                "CONFIG_ID_TIMEPHOTO" => "定时拍照",
                "CONFIG_ID_MOREPHOTO" => "连拍",
                "CONFIG_ID_TIMESTAMP" => "时间标志",
                "CONFIG_ID_MOTIONDECTION" => "移动侦测",
                "CONFIG_ID_PARKMODE" => "停车模式",
                "CONFIG_ID_GSENSOR" => "G-Sensor",
                "CONFIG_ID_KEYSOUND" => "按键声音",
                "CONFIG_ID_IR_LED" => "红外灯",
                "CONFIG_ID_LOOPTIME" => "循环录像时间",
                "CONFIG_ID_AUDIOREC" => "录音",
                "CONFIG_ID_EV" => "曝光补偿",
                "CONFIG_ID_WBLANCE" => "白平衡",
                "CONFIG_ID_ISP_FILTER" => "ISP滤镜",
                "CONFIG_ID_PRINTER_EN" => "打印机使能",
                "CONFIG_ID_PRINTER_DENSITY" => "打印浓度",
                "CONFIG_ID_PRINTER_MODE" => "打印模式",
                "CONFIG_ID_PRINTER_NEARFAR" => "远近模式",
                "CONFIG_ID_PRINTER_DELAY" => "打印延迟",
                "CONFIG_ID_PRINTER_DENSITY_H" => "打印浓度高",
                "CONFIG_ID_PRINTER_DENSITY_L" => "打印浓度低",
                "CONFIG_ID_PRINTER_MOTE_SPEED" => "打印速度",
                "CONFIG_ID_BAT_OLD" => "电池电量",
                "CONFIG_ID_BAT_CHECK_FLAG" => "电池检测",
                "CONFIG_ID_DEVICE_ID1" => "设备ID1",
                "CONFIG_ID_DEVICE_ID2" => "设备ID2",
                "CONFIG_ID_DEVICE_ID3" => "设备ID3",
                "CONFIG_ID_DEVICE_ID4" => "设备ID4",
                "CONFIG_ID_DEVICE_ID5" => "设备ID5",
                "CONFIG_ID_DEVICE_ID6" => "设备ID6",
                "CONFIG_ID_PRESLUTION" => "图片质量",
                "CONFIG_ID_PFASTVIEW" => "快速预览",
                "CONFIG_ID_PTIMESTRAMP" => "照片时间戳",
                "CONFIG_ID_PEV" => "照片曝光补偿",
                "CONFIG_ID_VOLUME" => "音量",
                "CONFIG_ID_THUMBNAIL" => "缩略图",
                "CONFIG_ID_GSENSORMODE" => "G-Sensor 模式",
                "CONFIG_ID_FORMAT" => "格式化",
                "CONFIG_ID_DEFUALT" => "恢复默认",
                "CONFIG_ID_VIDEORECEFFECT" => "录像特效",
                "CONFIG_ID_VIDEOSPEED" => "录像速度",
                "CONFIG_ID_LINEASSIST" => "辅助线",
                "CONFIG_ID_REINIT" => "重新初始化",
                _ => configName
            };
        }

        private string GetMenuName(string configName)
        {
            return configName switch
            {
                "CONFIG_ID_RESOLUTION" => "vidResolution",
                "CONFIG_ID_PRESLUTION" => "photoResolution",
                "CONFIG_ID_PFASTVIEW" => "pfastview",
                "CONFIG_ID_LOOPTIME" => "loopRecord",
                "CONFIG_ID_WBLANCE" => "awb",
                "CONFIG_ID_EV" => "ev",
                "CONFIG_ID_PEV" => "ev",
                "CONFIG_ID_MOTIONDECTION" => "md",
                "CONFIG_ID_AUDIOREC" => "audio",
                "CONFIG_ID_PARKMODE" => "parking",
                "CONFIG_ID_TIMESTAMP" => "timeStamp",
                "CONFIG_ID_GSENSOR" => "gsensor",
                "CONFIG_ID_KEYSOUND" => "keySound",
                "CONFIG_ID_AUTOOFF" => "autoPowerOff",
                "CONFIG_ID_LANGUAGE" => "language",
                "CONFIG_ID_FREQUNCY" => "frequency",
                "CONFIG_ID_IR_LED" => "irLed",
                "CONFIG_ID_FORMAT" => "format",
                "CONFIG_ID_DEFUALT" => "defaul",
                "CONFIG_ID_SCREENSAVE" => "screenSave",
                "CONFIG_ID_LCD_BRIGHT" => "lcdbright",
                "CONFIG_ID_VOLUME" => "volume",
                "CONFIG_ID_TIMEPHOTO" => "timephoto",
                "CONFIG_ID_ISP_FILTER" => "ispFilter",
                "CONFIG_ID_PRINTER_DENSITY" => "printdensity",
                "CONFIG_ID_VIDEOSPEED" => "vidSpeed",
                "CONFIG_ID_LINEASSIST" => "lineAssist",
                _ => ""
            };
        }

        private string GetOptionDisplay(string optionName)
        {
            return optionName switch
            {
                "R_ID_STR_COM_OFF" => "关闭",
                "R_ID_STR_COM_ON" => "开启",
                "R_ID_STR_COM_LOW" => "低",
                "R_ID_STR_COM_MIDDLE" => "中",
                "R_ID_STR_COM_HIGH" => "高",
                "R_ID_STR_COM_50HZ" => "50Hz",
                "R_ID_STR_COM_60HZ" => "60Hz",
                "R_ID_STR_COM_OK" => "确认",
                "R_ID_STR_COM_CANCEL" => "取消",
                "R_ID_STR_COM_LEVEL_0" => "级别 0",
                "R_ID_STR_COM_LEVEL_1" => "级别 1",
                "R_ID_STR_COM_LEVEL_2" => "级别 2",
                "R_ID_STR_COM_LEVEL_3" => "级别 3",
                "R_ID_STR_COM_LEVEL_4" => "级别 4",
                "R_ID_STR_COM_LEVEL_5" => "级别 5",
                "R_ID_STR_COM_LEVEL_6" => "级别 6",
                "R_ID_STR_COM_LEVEL_7" => "级别 7",
                "R_ID_STR_COM_LEVEL_8" => "级别 8",
                "R_ID_STR_COM_LEVEL_9" => "级别 9",
                "R_ID_STR_COM_BRIGHT_LEVEL_1" => "亮度 1",
                "R_ID_STR_COM_BRIGHT_LEVEL_2" => "亮度 2",
                "R_ID_STR_COM_BRIGHT_LEVEL_3" => "亮度 3",
                "R_ID_STR_COM_BRIGHT_LEVEL_4" => "亮度 4",
                "R_ID_STR_COM_BRIGHT_LEVEL_5" => "亮度 5",
                "R_ID_STR_COM_BRIGHT_LEVEL_6" => "亮度 6",
                "R_ID_STR_COM_BRIGHT_LEVEL_7" => "亮度 7",
                "R_ID_STR_COM_BRIGHT_LEVEL_8" => "亮度 8",
                "R_ID_STR_COM_BRIGHT_LEVEL_9" => "亮度 9",
                "R_ID_STR_COM_P4_0" => "+4.0",
                "R_ID_STR_COM_P3_0" => "+3.0",
                "R_ID_STR_COM_P2_0" => "+2.0",
                "R_ID_STR_COM_P1_0" => "+1.0",
                "R_ID_STR_COM_P0_0" => "0.0",
                "R_ID_STR_COM_N1_0" => "-1.0",
                "R_ID_STR_COM_N2_0" => "-2.0",
                "R_ID_STR_RES_240P" => "240P",
                "R_ID_STR_RES_480P" => "480P",
                "R_ID_STR_RES_720P" => "720P",
                "R_ID_STR_RES_720HD" => "720HD",
                "R_ID_STR_RES_720P_SHORT" => "720P_SHORT",
                "R_ID_STR_RES_1080P" => "1080P",
                "R_ID_STR_RES_1080FHD" => "1080FHD",
                "R_ID_STR_RES_1080P_SHORT" => "1080P_SHORT",
                "R_ID_STR_RES_1440P_SHORT" => "1440P_SHORT",
                "R_ID_STR_RES_2160P_SHORT" => "2160P_SHORT",
                "R_ID_STR_RES_1440P" => "1440P",
                "R_ID_STR_RES_HD" => "HD",
                "R_ID_STR_RES_FHD" => "FHD",
                "R_ID_STR_RES_48M" => "48M",
                "R_ID_STR_RES_40M" => "40M",
                "R_ID_STR_RES_24M" => "24M",
                "R_ID_STR_RES_20M" => "20M",
                "R_ID_STR_RES_18M" => "18M",
                "R_ID_STR_RES_16M" => "16M",
                "R_ID_STR_RES_12M" => "12M",
                "R_ID_STR_RES_10M" => "10M",
                "R_ID_STR_RES_8M" => "8M",
                "R_ID_STR_RES_5M" => "5M",
                "R_ID_STR_RES_4M" => "4M",
                "R_ID_STR_RES_3M" => "3M",
                "R_ID_STR_RES_2M" => "2M",
                "R_ID_STR_RES_1M" => "1M",
                "R_ID_STR_RES_VGA" => "VGA",
                "R_ID_STR_TIM_1MIN" => "1分钟",
                "R_ID_STR_TIM_2MIN" => "2分钟",
                "R_ID_STR_TIM_3MIN" => "3分钟",
                "R_ID_STR_TIM_5MIN" => "5分钟",
                "R_ID_STR_TIM_10MIN" => "10分钟",
                "R_ID_STR_TIM_2SEC" => "2秒",
                "R_ID_STR_TIM_3SEC" => "3秒",
                "R_ID_STR_TIM_5SEC" => "5秒",
                "R_ID_STR_TIM_10SEC" => "10秒",
                "R_ID_STR_ISP_AUTO" => "自动",
                "R_ID_STR_ISP_SUNLIGHT" => "晴天",
                "R_ID_STR_ISP_CLOUDY" => "阴天",
                "R_ID_STR_ISP_TUNGSTEN" => "办公室",
                "R_ID_STR_ISP_FLUORESCENT" => "荧光灯",
                "R_ID_STR_ISP_RETRO" => "复古",
                "R_ID_STR_IR_AUTO" => "自动",
                "R_ID_STR_COM_VIDEOREC_NORMAL" => "正常",
                "R_ID_STR_COM_VIDEOREC_SLOW" => "慢速",
                "R_ID_STR_COM_VIDEOREC_FAST" => "快速",
                "R_ID_STR_LAN_ENGLISH" => "English",
                "R_ID_STR_LAN_SCHINESE" => "简体中文",
                "R_ID_STR_LAN_TCHINESE" => "繁体中文",
                "R_ID_STR_LAN_JAPANESE" => "日本语",
                "R_ID_STR_LAN_GERMAN" => "Deutsch",
                "R_ID_STR_LAN_FRECH" => "Français",
                "R_ID_STR_LAN_HEBREW" => "עברית",
                "R_ID_STR_LAN_RUSSIAN" => "Русский",
                "R_ID_STR_LAN_ITALIAN" => "Italiano",
                "R_ID_STR_LAN_KOERA" => "한국어",
                "R_ID_STR_LAN_TAI" => "ภาษาไทย",
                "R_ID_STR_LAN_DUTCH" => "Nederlands",
                "R_ID_STR_LAN_SPANISH" => "Español",
                "R_ID_STR_LAN_PORTUGUESE" => "Português",
                "R_ID_STR_LAN_POLISH" => "Polski",
                "R_ID_STR_LAN_TURKEY" => "Türkçe",
                "R_ID_STR_LAN_CZECH" => "Čeština",
                "R_ID_STR_LAN_ROMANIAN" => "Română",
                _ => optionName
            };
        }

        private List<XmlOption> GenerateDefaultOptions(string configName, uint defaultValue, Dictionary<string, uint> stringConstants)
        {
            var options = new List<XmlOption>();

            if (configName.Contains("YEAR"))
            {
                for (int year = 2020; year <= 2040; year++)
                {
                    options.Add(new XmlOption { Value = (uint)year, ValueStr = year.ToString(), Display = year.ToString() });
                }
            }
            else if (configName.Contains("MONTH"))
            {
                for (int month = 1; month <= 12; month++)
                {
                    options.Add(new XmlOption { Value = (uint)month, ValueStr = month.ToString(), Display = month.ToString() });
                }
            }
            else if (configName.Contains("MDAY"))
            {
                for (int day = 1; day <= 31; day++)
                {
                    options.Add(new XmlOption { Value = (uint)day, ValueStr = day.ToString(), Display = day.ToString() });
                }
            }
            else if (configName.Contains("WDAY"))
            {
                string[] weekDays = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
                for (int i = 0; i < 7; i++)
                {
                    options.Add(new XmlOption { Value = (uint)i, ValueStr = i.ToString(), Display = weekDays[i] });
                }
            }
            else if (configName.Contains("HOUR"))
            {
                for (int hour = 0; hour < 24; hour++)
                {
                    options.Add(new XmlOption { Value = (uint)hour, ValueStr = hour.ToString(), Display = hour.ToString() });
                }
            }
            else if (configName.Contains("MIN") || configName.Contains("SEC"))
            {
                for (int i = 0; i < 60; i++)
                {
                    options.Add(new XmlOption { Value = (uint)i, ValueStr = i.ToString(), Display = i.ToString() });
                }
            }
            else if (configName.Contains("VOLUME"))
            {
                string defaultValueName = stringConstants.FirstOrDefault(kvp => kvp.Value == defaultValue).Key;

                if (!string.IsNullOrEmpty(defaultValueName))
                {
                    if (defaultValueName.Contains("LOW") || defaultValueName.Contains("MIDDLE") || defaultValueName.Contains("HIGH"))
                    {
                        string[] densityConstants = { "R_ID_STR_COM_LOW", "R_ID_STR_COM_MIDDLE", "R_ID_STR_COM_HIGH" };
                        string[] densityDisplays = { "低", "中", "高" };
                        for (int i = 0; i < densityConstants.Length; i++)
                        {
                            uint value = ResolveStringConstant(stringConstants, densityConstants[i]);
                            options.Add(new XmlOption { Value = value, ValueStr = FormatValue(value), Display = densityDisplays[i] });
                        }
                    }
                    else if (defaultValueName.Contains("LEVEL"))
                    {
                        for (int i = 0; i <= 10; i++)
                        {
                            string constantName = $"R_ID_STR_COM_LEVEL_{i}";
                            uint value = ResolveStringConstant(stringConstants, constantName);
                            options.Add(new XmlOption { Value = value, ValueStr = FormatValue(value), Display = $"等级 {i}" });
                        }
                    }
                    else
                    {
                        for (int i = 0; i <= 10; i++)
                        {
                            options.Add(new XmlOption { Value = (uint)i, ValueStr = i.ToString(), Display = i.ToString() });
                        }
                    }
                }
                else
                {
                    for (int i = 0; i <= 10; i++)
                    {
                        options.Add(new XmlOption { Value = (uint)i, ValueStr = i.ToString(), Display = i.ToString() });
                    }
                }
            }
            else if (configName.Contains("LCD_BRIGHT"))
            {
                string defaultValueName = stringConstants.FirstOrDefault(kvp => kvp.Value == defaultValue).Key;

                if (!string.IsNullOrEmpty(defaultValueName) && defaultValueName.Contains("BRIGHT_LEVEL"))
                {
                    for (int i = 1; i <= 9; i++)
                    {
                        string constantName = $"R_ID_STR_COM_BRIGHT_LEVEL_{i}";
                        uint value = ResolveStringConstant(stringConstants, constantName);
                        options.Add(new XmlOption { Value = value, ValueStr = FormatValue(value), Display = $"亮度 {i}" });
                    }
                }
                else
                {
                    for (int i = 0; i <= 9; i++)
                    {
                        string constantName = $"R_ID_STR_COM_LEVEL_{i}";
                        uint value = ResolveStringConstant(stringConstants, constantName);
                        options.Add(new XmlOption { Value = value, ValueStr = FormatValue(value), Display = $"级别 {i}" });
                    }
                }
            }
            else if (configName.Contains("DEVICE_ID"))
            {
                options.Add(new XmlOption { Value = defaultValue, ValueStr = FormatValue(defaultValue), Display = FormatValue(defaultValue) });
            }
            else if (configName.Contains("PRINTER_DENSITY_H") || configName.Contains("PRINTER_DENSITY_L") || configName.Contains("PRINTER_MOTE_SPEED"))
            {
                options.Add(new XmlOption { Value = defaultValue, ValueStr = FormatValue(defaultValue), Display = FormatValue(defaultValue) });
            }
            else if (configName.Contains("EV") || configName.Contains("PEV"))
            {
                var evConstantNames = new List<string> { "R_ID_STR_COM_N3_0", "R_ID_STR_COM_N2_0", "R_ID_STR_COM_N1_0",
                                                          "R_ID_STR_COM_P0_0", "R_ID_STR_COM_P1_0", "R_ID_STR_COM_P2_0",
                                                          "R_ID_STR_COM_P3_0", "R_ID_STR_COM_P4_0" };
                var evDisplays = new Dictionary<string, string>
                {
                    { "R_ID_STR_COM_N3_0", "-3.0" },
                    { "R_ID_STR_COM_N2_0", "-2.0" },
                    { "R_ID_STR_COM_N1_0", "-1.0" },
                    { "R_ID_STR_COM_P0_0", "0.0" },
                    { "R_ID_STR_COM_P1_0", "+1.0" },
                    { "R_ID_STR_COM_P2_0", "+2.0" },
                    { "R_ID_STR_COM_P3_0", "+3.0" },
                    { "R_ID_STR_COM_P4_0", "+4.0" }
                };

                var evItems = new List<Tuple<uint, string, string>>();
                foreach (var constantName in evConstantNames)
                {
                    if (stringConstants != null && stringConstants.TryGetValue(constantName, out uint value))
                    {
                        evItems.Add(Tuple.Create(value, FormatValue(value), evDisplays[constantName]));
                    }
                }

                evItems.Sort((a, b) => a.Item1.CompareTo(b.Item1));

                foreach (var item in evItems)
                {
                    options.Add(new XmlOption { Value = item.Item1, ValueStr = item.Item2, Display = item.Item3 });
                }

                if (options.Count == 0)
                {
                    for (int i = 0; i < evConstantNames.Count; i++)
                    {
                        string constantName = evConstantNames[i];
                        uint value = ResolveStringConstant(stringConstants, constantName);
                        options.Add(new XmlOption { Value = value, ValueStr = FormatValue(value), Display = evDisplays[constantName] });
                    }
                }
            }
            else if (configName.Contains("PRINTER_DENSITY") && !configName.Contains("_H") && !configName.Contains("_L"))
            {
                string defaultValueName = stringConstants.FirstOrDefault(kvp => kvp.Value == defaultValue).Key;

                if (!string.IsNullOrEmpty(defaultValueName) && (defaultValueName.Contains("COM_MIDDLE") ||
                    defaultValueName.Contains("COM_LOW") || defaultValueName.Contains("COM_HIGH")))
                {
                    string[] densityConstants = { "R_ID_STR_COM_LOW", "R_ID_STR_COM_MIDDLE", "R_ID_STR_COM_HIGH" };
                    string[] densityDisplays = { "低", "中", "高" };
                    for (int i = 0; i < densityConstants.Length; i++)
                    {
                        uint value = ResolveStringConstant(stringConstants, densityConstants[i]);
                        options.Add(new XmlOption { Value = value, ValueStr = FormatValue(value), Display = densityDisplays[i] });
                    }
                }
                else
                {
                    string[] levelConstants = { "R_ID_STR_COM_LEVEL_1", "R_ID_STR_COM_LEVEL_2", "R_ID_STR_COM_LEVEL_3",
                                                "R_ID_STR_COM_LEVEL_4", "R_ID_STR_COM_LEVEL_5" };
                    for (int i = 0; i < levelConstants.Length; i++)
                    {
                        uint value = ResolveStringConstant(stringConstants, levelConstants[i]);
                        options.Add(new XmlOption { Value = value, ValueStr = FormatValue(value), Display = $"级别 {i + 1}" });
                    }
                }
            }
            else if (configName.Contains("PRINTER_MODE"))
            {
                uint dotValue = ResolveStringConstant(stringConstants, "R_ID_STR_SET_PRINT_DOT");
                uint grayValue = ResolveStringConstant(stringConstants, "R_ID_STR_SET_PRINT_GRAY");
                options.Add(new XmlOption { Value = dotValue, ValueStr = FormatValue(dotValue), Display = "点阵" });
                options.Add(new XmlOption { Value = grayValue, ValueStr = FormatValue(grayValue), Display = "灰度" });
            }
            else if (configName.Contains("PRINTER_NEARFAR"))
            {
                uint nearValue = ResolveStringConstant(stringConstants, "R_ID_STR_TIP_NEAR");
                uint middleValue = ResolveStringConstant(stringConstants, "R_ID_STR_TIP_MIDDLE");
                uint farValue = ResolveStringConstant(stringConstants, "R_ID_STR_TIP_FAR");
                options.Add(new XmlOption { Value = nearValue, ValueStr = FormatValue(nearValue), Display = "近景" });
                options.Add(new XmlOption { Value = middleValue, ValueStr = FormatValue(middleValue), Display = "中景" });
                options.Add(new XmlOption { Value = farValue, ValueStr = FormatValue(farValue), Display = "远景" });
            }
            else if (configName.Contains("BAT_OLD"))
            {
                string[] levelConstants = { "R_ID_STR_COM_LEVEL_1", "R_ID_STR_COM_LEVEL_2", "R_ID_STR_COM_LEVEL_3", 
                                            "R_ID_STR_COM_LEVEL_4", "R_ID_STR_COM_LEVEL_5", "R_ID_STR_COM_LEVEL_6" };
                for (int i = 0; i < levelConstants.Length; i++)
                {
                    uint value = ResolveStringConstant(stringConstants, levelConstants[i]);
                    options.Add(new XmlOption { Value = value, ValueStr = FormatValue(value), Display = $"级别 {i + 1}" });
                }
            }
            else if (configName.Contains("WBLANCE"))
            {
                uint autoValue = ResolveStringConstant(stringConstants, "R_ID_STR_ISP_AUTO");
                uint sunlightValue = ResolveStringConstant(stringConstants, "R_ID_STR_ISP_SUNLIGHT");
                uint cloudyValue = ResolveStringConstant(stringConstants, "R_ID_STR_ISP_CLOUDY");
                uint tungstenValue = ResolveStringConstant(stringConstants, "R_ID_STR_ISP_TUNGSTEN");
                uint fluorescentValue = ResolveStringConstant(stringConstants, "R_ID_STR_ISP_FLUORESCENT");
                options.Add(new XmlOption { Value = autoValue, ValueStr = FormatValue(autoValue), Display = "自动" });
                options.Add(new XmlOption { Value = sunlightValue, ValueStr = FormatValue(sunlightValue), Display = "晴天" });
                options.Add(new XmlOption { Value = cloudyValue, ValueStr = FormatValue(cloudyValue), Display = "阴天" });
                options.Add(new XmlOption { Value = tungstenValue, ValueStr = FormatValue(tungstenValue), Display = "办公室" });
                options.Add(new XmlOption { Value = fluorescentValue, ValueStr = FormatValue(fluorescentValue), Display = "荧光灯" });
            }
            else if (configName.Contains("MOTIONDECTION") || configName.Contains("TIMEPHOTO") || configName.Contains("PARKMODE") || configName.Contains("GSENSORMODE") || configName.Contains("IR_LED") || configName.Contains("AUDIOREC"))
            {
                uint offValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_OFF");
                uint onValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_ON");
                options.Add(new XmlOption { Value = offValue, ValueStr = FormatValue(offValue), Display = "关闭" });
                options.Add(new XmlOption { Value = onValue, ValueStr = FormatValue(onValue), Display = "开启" });
            }
            else if (configName.Contains("GSENSOR"))
            {
                // 灵敏度类型：关闭、低、中、高
                uint offValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_OFF");
                uint lowValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_LOW");
                uint middleValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_MIDDLE");
                uint highValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_HIGH");
                options.Add(new XmlOption { Value = offValue, ValueStr = FormatValue(offValue), Display = "关闭" });
                options.Add(new XmlOption { Value = lowValue, ValueStr = FormatValue(lowValue), Display = "低" });
                options.Add(new XmlOption { Value = middleValue, ValueStr = FormatValue(middleValue), Display = "中" });
                options.Add(new XmlOption { Value = highValue, ValueStr = FormatValue(highValue), Display = "高" });
            }
            else if (configName.Contains("VIDEOSPEED"))
            {
                uint normalValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_VIDEOREC_NORMAL");
                uint slowValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_VIDEOREC_SLOW");
                uint fastValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_VIDEOREC_FAST");
                options.Add(new XmlOption { Value = normalValue, ValueStr = FormatValue(normalValue), Display = "正常" });
                options.Add(new XmlOption { Value = slowValue, ValueStr = FormatValue(slowValue), Display = "慢速" });
                options.Add(new XmlOption { Value = fastValue, ValueStr = FormatValue(fastValue), Display = "快速" });
            }
            else if (configName.Contains("RESOLUTION") || configName.Contains("PRESLUTION"))
            {
                if (configName.Contains("PRES"))
                {
                    string[] presConstants = { "R_ID_STR_RES_VGA", "R_ID_STR_RES_HD", "R_ID_STR_RES_FHD", "R_ID_STR_RES_1M", "R_ID_STR_RES_2M", "R_ID_STR_RES_3M",
                                              "R_ID_STR_RES_5M", "R_ID_STR_RES_8M", "R_ID_STR_RES_10M",
                                              "R_ID_STR_RES_12M", "R_ID_STR_RES_16M", "R_ID_STR_RES_18M",
                                              "R_ID_STR_RES_20M", "R_ID_STR_RES_24M", "R_ID_STR_RES_40M",
                                              "R_ID_STR_RES_48M" };
                    for (int i = 0; i < presConstants.Length; i++)
                    {
                        uint value = ResolveStringConstant(stringConstants, presConstants[i]);
                        string display = presConstants[i].Replace("R_ID_STR_RES_", "");
                        options.Add(new XmlOption { Value = value, ValueStr = FormatValue(value), Display = display });
                    }
                }
                else
                {
                    string[] videoConstants = { "R_ID_STR_RES_240P", "R_ID_STR_RES_480P", "R_ID_STR_RES_480FHD",
                                                "R_ID_STR_RES_720P", "R_ID_STR_RES_1024P", "R_ID_STR_RES_1080P",
                                                "R_ID_STR_RES_1080FHD", "R_ID_STR_RES_1440P", "R_ID_STR_RES_3024P",
                                                "R_ID_STR_RES_720P_SHORT", "R_ID_STR_RES_1080P_SHORT",
                                                "R_ID_STR_RES_QVGA", "R_ID_STR_RES_VGA", "R_ID_STR_RES_HD",
                                                "R_ID_STR_RES_FHD" };
                    for (int i = 0; i < videoConstants.Length; i++)
                    {
                        uint value = ResolveStringConstant(stringConstants, videoConstants[i]);
                        string display = videoConstants[i].Replace("R_ID_STR_RES_", "");
                        options.Add(new XmlOption { Value = value, ValueStr = FormatValue(value), Display = display });
                    }
                }
            }
            else if (configName.Contains("FREQUNCY"))
            {
                uint hz50Value = ResolveStringConstant(stringConstants, "R_ID_STR_COM_50HZ");
                uint hz60Value = ResolveStringConstant(stringConstants, "R_ID_STR_COM_60HZ");
                options.Add(new XmlOption { Value = hz50Value, ValueStr = FormatValue(hz50Value), Display = "50Hz" });
                options.Add(new XmlOption { Value = hz60Value, ValueStr = FormatValue(hz60Value), Display = "60Hz" });
            }
            else if (configName.Contains("DEFUALT") || configName.Contains("FORMAT"))
            {
                uint okValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_OK");
                uint cancelValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_CANCEL");
                options.Add(new XmlOption { Value = okValue, ValueStr = FormatValue(okValue), Display = "OK" });
                options.Add(new XmlOption { Value = cancelValue, ValueStr = FormatValue(cancelValue), Display = "CANCEL" });
            }
            else if (configName.EndsWith("_ON") || configName.EndsWith("_OFF"))
            {
                uint offValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_OFF");
                uint onValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_ON");
                options.Add(new XmlOption { Value = offValue, ValueStr = FormatValue(offValue), Display = "关闭" });
                options.Add(new XmlOption { Value = onValue, ValueStr = FormatValue(onValue), Display = "开启" });
            }
            else
            {
                uint offValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_OFF");
                uint onValue = ResolveStringConstant(stringConstants, "R_ID_STR_COM_ON");
                options.Add(new XmlOption { Value = offValue, ValueStr = FormatValue(offValue), Display = "关闭" });
                options.Add(new XmlOption { Value = onValue, ValueStr = FormatValue(onValue), Display = "开启" });
            }

            return options;
        }

        private uint ResolveStringConstant(Dictionary<string, uint> stringConstants, string constantName)
        {
            if (stringConstants != null && stringConstants.TryGetValue(constantName, out uint value))
            {
                return value;
            }
            return ConfigSourceParser.ParseRIdStrConstantStatic(constantName);
        }
    }
}