using System.Collections.Generic;

namespace FontBuilder.Models
{
    /// <summary>
    /// 单语言源文件信息
    /// </summary>
    public sealed class LanguageSource
    {
        /// <summary>
        /// 语言名称（如 english, schinese）
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 源文件路径（如 .\fontSrc\english.txt）
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 字符串列表（按源文件顺序）
        /// </summary>
        public List<string> Strings { get; set; } = new();

        /// <summary>
        /// 语言索引（在 font.ini -f 列表中的位置，0-based）
        /// </summary>
        public int Index { get; set; }
    }

    /// <summary>
    /// font.ini 配置（-t 段 + -f 段）
    /// </summary>
    public sealed class FontBuildConfig
    {
        /// <summary>
        /// -t 段：输出文件路径
        /// </summary>
        public string FontTabPath { get; set; } = @".\font.tab";
        public string UserStrCPath { get; set; } = @".\user_str.c";
        public string UserStrHPath { get; set; } = @".\user_str.h";

        /// <summary>
        /// -f 段：语言源文件列表（按顺序，已剔除 # 注释行）
        /// </summary>
        public List<LanguageSource> Languages { get; set; } = new();

        /// <summary>
        /// font.bin 路径（输出）
        /// </summary>
        public string FontBinPath { get; set; } = @".\font.bin";

        /// <summary>
        /// resfont.bin 路径（输出）
        /// </summary>
        public string ResFontBinPath { get; set; } = @".\resfont.bin";

        /// <summary>
        /// resfontidx.bin 路径（输出）
        /// </summary>
        public string ResFontIdxPath { get; set; } = @".\resfontidx.bin";

        /// <summary>
        /// fontSelect.txt 路径
        /// </summary>
        public string FontSelectPath { get; set; } = @".\fontSelect.txt";

        /// <summary>
        /// 无效字符宽度（默认 8）
        /// </summary>
        public byte InvalidCharWidth { get; set; } = 8;

        /// <summary>
        /// R_ID_TYPE_STR 起始值（user_str.h 中的字符串 ID 基址）
        /// </summary>
        public uint RIdTypeStrBase { get; set; } = 0x0000E000;

        /// <summary>
        /// 语言 ID 起始值（resfontidx.bin 中）
        /// </summary>
        public uint LangIdBase { get; set; } = 0xD000;
    }
}
