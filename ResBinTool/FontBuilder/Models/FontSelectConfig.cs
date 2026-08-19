using System;
using System.IO;
using System.Text.RegularExpressions;

namespace FontBuilder.Models
{
    /// <summary>
    /// fontSelect.txt 配置：字体族 + 样式 + 字号
    /// 格式示例: "1. Arial Unicode MS + 常规 + 四号;"
    /// </summary>
    public sealed class FontSelectConfig
    {
        /// <summary>
        /// 字体族名称（如 Arial Unicode MS / Microsoft Sans Serif）
        /// </summary>
        public string FontFamily { get; set; } = "Arial Unicode MS";

        /// <summary>
        /// 字体样式名称（"常规"=Regular, "粗体"=Bold, "斜体"=Italic）
        /// </summary>
        public string StyleName { get; set; } = "常规";

        /// <summary>
        /// 中文字号名称（如 "四号"、"五号"、"小四"）
        /// </summary>
        public string SizeName { get; set; } = "四号";

        /// <summary>
        /// 解析后的 FontStyle
        /// </summary>
        public System.Drawing.FontStyle FontStyle
        {
            get
            {
                if (StyleName.Contains("粗") || StyleName.ToLower().Contains("bold"))
                    return System.Drawing.FontStyle.Bold;
                if (StyleName.Contains("斜") || StyleName.ToLower().Contains("italic"))
                    return System.Drawing.FontStyle.Italic;
                return System.Drawing.FontStyle.Regular;
            }
        }

        /// <summary>
        /// 字号转磅值（pt）
        /// 中文字号映射表
        /// </summary>
        public float SizeInPoints => SizeName switch
        {
            "初号" => 42f,
            "小初" => 36f,
            "一号" => 26f,
            "小一" => 24f,
            "二号" => 22f,
            "小二" => 18f,
            "三号" => 16f,
            "小三" => 15f,
            "四号" => 14f,
            "小四" => 12f,
            "五号" => 10.5f,
            "小五" => 9f,
            "六号" => 7.5f,
            "小六" => 6.5f,
            "七号" => 5.5f,
            "八号" => 5f,
            _ => float.TryParse(SizeName, out var v) ? v : 14f
        };

        /// <summary>
        /// 从 fontSelect.txt 解析配置
        /// </summary>
        public static FontSelectConfig Parse(string filePath)
        {
            var config = new FontSelectConfig();
            if (!File.Exists(filePath)) return config;

            foreach (var line in File.ReadAllLines(filePath))
            {
                // "1. Arial Unicode MS + 常规 + 四号;"
                var m = Regex.Match(line, @"^\s*\d+\.\s*([^+]+)\+\s*([^+]+)\+\s*([^;]+);?\s*$");
                if (m.Success)
                {
                    config.FontFamily = m.Groups[1].Value.Trim();
                    config.StyleName = m.Groups[2].Value.Trim();
                    config.SizeName = m.Groups[3].Value.Trim();
                    return config;
                }
            }
            return config;
        }

        public override string ToString() => $"{FontFamily} / {StyleName} / {SizeName}({SizeInPoints}pt)";
    }
}
