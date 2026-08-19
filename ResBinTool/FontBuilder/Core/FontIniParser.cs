using System;
using System.IO;
using FontBuilder.Models;

namespace FontBuilder.Core
{
    /// <summary>
    /// font.ini 解析器
    /// 文件格式:
    ///   -t
    ///   .\font.tab
    ///   .\user_str.c
    ///   .\user_str.h
    ///   -f
    ///   .\fontSrc\english.txt
    ///   .\fontSrc\schinese.txt
    ///   #.\fontSrc\czech.txt   (注释行跳过)
    /// </summary>
    public static class FontIniParser
    {
        public static FontBuildConfig Parse(string iniPath)
        {
            var config = new FontBuildConfig();
            if (!File.Exists(iniPath))
                throw new FileNotFoundException($"font.ini not found: {iniPath}");

            // 解析相对路径基准目录为 ini 所在目录
            string baseDir = Path.GetDirectoryName(Path.GetFullPath(iniPath)) ?? string.Empty;

            var lines = File.ReadAllLines(iniPath);
            string section = string.Empty;
            int langIndex = 0;
            var tPaths = new System.Collections.Generic.List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // 注释行
                if (line.StartsWith("#") || line.StartsWith("//")) continue;

                if (line == "-t" || line == "-f")
                {
                    section = line;
                    continue;
                }

                // 路径行
                if (line.Contains("#") && !line.StartsWith("#"))
                {
                    // 行内注释（如 #.\fontSrc\x.txt，但开头非 #，故处理为整体跳过）
                    var trimmed = line.TrimStart('#', ' ', '\t').Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    line = trimmed.StartsWith(".") || trimmed.Contains(":") ? line : trimmed;
                }

                string absPath = ResolvePath(line, baseDir);

                if (section == "-t")
                {
                    tPaths.Add(absPath);
                }
                else if (section == "-f")
                {
                    // 若行以 # 开头已处理；保留路径
                    var langName = Path.GetFileNameWithoutExtension(line);
                    config.Languages.Add(new LanguageSource
                    {
                        Name = langName,
                        FilePath = absPath,
                        Index = langIndex++
                    });
                }
            }

            // -t 段顺序: font.tab, user_str.c, user_str.h
            if (tPaths.Count > 0) config.FontTabPath = tPaths[0];
            if (tPaths.Count > 1) config.UserStrCPath = tPaths[1];
            if (tPaths.Count > 2) config.UserStrHPath = tPaths[2];

            return config;
        }

        /// <summary>
        /// 将 .\xxx 形式的相对路径转换为绝对路径
        /// </summary>
        public static string ResolvePath(string path, string baseDir)
        {
            if (Path.IsPathRooted(path)) return path;

            var normalized = path.Replace('\\', Path.DirectorySeparatorChar)
                                 .Replace('/', Path.DirectorySeparatorChar);
            // 去除开头的 .\
            if (normalized.StartsWith($".{Path.DirectorySeparatorChar}"))
                normalized = normalized.Substring(2);
            if (normalized.StartsWith($".{Path.DirectorySeparatorChar}"))
                normalized = normalized.Substring(2);

            return Path.GetFullPath(Path.Combine(baseDir, normalized));
        }
    }
}
