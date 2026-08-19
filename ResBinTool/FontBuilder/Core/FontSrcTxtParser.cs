using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FontBuilder.Models;

namespace FontBuilder.Core
{
    /// <summary>
    /// 字符串源文件解析器
    /// 格式：每行一个带双引号的字符串
    ///   "English"
    ///   "简体中文"
    ///   "Upgrade, please wait. . . . . ."
    /// 转义：\" -> ", \\ -> \
    /// </summary>
    public static class FontSrcTxtParser
    {
        /// <summary>
        /// 解析单个语言源文件
        /// </summary>
        public static List<string> ParseFile(string filePath)
        {
            var result = new List<string>();
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Source file not found: {filePath}");

            // 自动检测编码：UTF-8 (含 BOM) 或 UTF-8 无 BOM
            var content = File.ReadAllText(filePath, DetectEncoding(filePath));

            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r').Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("//") || line.StartsWith("#")) continue;

                // 必须以双引号开头
                if (!line.StartsWith("\""))
                {
                    // 可能是无引号的纯文本行（如 "+E193:P197" 这种异常行）
                    result.Add(line);
                    continue;
                }

                var str = ParseQuotedString(line);
                if (str != null) result.Add(str);
            }
            return result;
        }

        /// <summary>
        /// 解析带双引号的字符串（支持转义）
        /// </summary>
        public static string ParseQuotedString(string line)
        {
            // 查找首尾引号
            if (!line.StartsWith("\"")) return line;
            int end = -1;
            var sb = new StringBuilder();
            bool escape = false;
            for (int i = 1; i < line.Length; i++)
            {
                char c = line[i];
                if (escape)
                {
                    switch (c)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '0': sb.Append('\0'); break;
                        default: sb.Append(c); break;
                    }
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    end = i;
                    break;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return end > 0 ? sb.ToString() : sb.ToString();
        }

        /// <summary>
        /// 加载语言源数据（解析每语言字符串列表）
        /// </summary>
        public static void LoadLanguageStrings(FontBuildConfig config)
        {
            foreach (var lang in config.Languages)
            {
                lang.Strings = ParseFile(lang.FilePath);
            }
        }

        private static Encoding DetectEncoding(string filePath)
        {
            // 默认 UTF-8（含 BOM 自动识别）
            // fontSrc/*.txt 均为 UTF-8 BOM
            var utf8WithBom = new UTF8Encoding(true);
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                if (fs.Length >= 3)
                {
                    var bom = new byte[3];
                    int n = fs.Read(bom, 0, 3);
                    if (n == 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                        return utf8WithBom;
                }
            }
            catch { }

            // 回退到 UTF-8 无 BOM
            return new UTF8Encoding(false);
        }
    }
}
