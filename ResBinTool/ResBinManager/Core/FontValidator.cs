using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ResBinManager.Core
{
    /// <summary>
    /// Font 文件验证结果
    /// </summary>
    public class FontValidationResult
    {
        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 错误消息（如果无效）
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 警告消息列表
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// 字体数据文件 (resfont.bin) 是否有效
        /// </summary>
        public bool FontDataValid { get; set; }

        /// <summary>
        /// 字体索引文件 (resfontidx.bin) 是否有效
        /// </summary>
        public bool FontIndexValid { get; set; }

        /// <summary>
        /// 字体信息（如果解析成功）
        /// </summary>
        public FontInfo? Info { get; set; }

        /// <summary>
        /// 字符数量
        /// </summary>
        public uint CharCount { get; set; }

        /// <summary>
        /// 语言数量
        /// </summary>
        public byte LanguageCount { get; set; }

        /// <summary>
        /// 一致性警告
        /// </summary>
        public string ConsistencyWarning { get; set; } = string.Empty;

        /// <summary>
        /// 获取验证结果的显示文本
        /// </summary>
        public string GetDisplayText()
        {
            var sb = new StringBuilder();

            if (IsValid && Info != null)
            {
                sb.AppendLine("✓ Valid font files");
                sb.AppendLine();
                sb.AppendLine("resfont.bin:");
                sb.AppendLine($"  Character Count: {CharCount}");
                sb.AppendLine($"  Data Size: {Info.Characters.Count} entries");
                
                sb.AppendLine();
                sb.AppendLine("resfontidx.bin:");
                sb.AppendLine($"  Magic: 0x584D ✓");
                sb.AppendLine($"  Language Count: {LanguageCount}");
                sb.AppendLine($"  Invalid Char Width: {Info.InvalidCharWidth}");

                if (!string.IsNullOrEmpty(ConsistencyWarning))
                {
                    sb.AppendLine();
                    sb.AppendLine($"⚠ {ConsistencyWarning}");
                }

                if (Warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠ Warnings:");
                    foreach (var warning in Warnings)
                    {
                        sb.AppendLine($"  - {warning}");
                    }
                }
            }
            else
            {
                sb.AppendLine("✗ Invalid font files");
                sb.AppendLine($"  Error: {ErrorMessage}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Font 文件验证器
    /// </summary>
    public static class FontValidator
    {
        /// <summary>
        /// 验证字体文件格式
        /// </summary>
        /// <param name="fontData">resfont.bin 数据</param>
        /// <param name="fontIndex">resfontidx.bin 数据</param>
        /// <returns>验证结果</returns>
        public static FontValidationResult Validate(byte[] fontData, byte[] fontIndex)
        {
            var result = new FontValidationResult();

            // 1. 基本检查
            if (fontData == null || fontData.Length == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "Font data file (resfont.bin) is empty";
                return result;
            }

            if (fontIndex == null || fontIndex.Length == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "Font index file (resfontidx.bin) is empty";
                return result;
            }

            if (fontData.Length < 4)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Font data file too small ({fontData.Length} bytes). Minimum size is 4 bytes.";
                return result;
            }

            if (fontIndex.Length < 4)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Font index file too small ({fontIndex.Length} bytes). Minimum size is 4 bytes.";
                return result;
            }

            // 2. 尝试解析字体信息
            try
            {
                var info = FontInfoParser.Parse(fontData, fontIndex);
                result.Info = info;
                result.FontDataValid = true;
                result.FontIndexValid = true;
                result.CharCount = info.CharCount;
                result.LanguageCount = info.LanguageCount;
                result.IsValid = true;

                // 3. 添加警告
                AddWarnings(info, fontData, fontIndex, result);
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Failed to parse font files: {ex.Message}";
                
                // 判断是哪个文件出错
                if (ex.Message.Contains("index") || ex.Message.Contains("magic"))
                {
                    result.FontIndexValid = false;
                    result.FontDataValid = true; // 假设数据文件可能没问题
                }
                else
                {
                    result.FontDataValid = false;
                }
            }

            return result;
        }

        /// <summary>
        /// 比较两个字体的参数差异
        /// </summary>
        /// <param name="oldInfo">原始字体信息</param>
        /// <param name="newInfo">新字体信息</param>
        /// <returns>差异描述</returns>
        public static string CompareFontInfo(FontInfo oldInfo, FontInfo newInfo)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Parameter Comparison:");
            sb.AppendLine();

            // 字符数量
            if (oldInfo.CharCount != newInfo.CharCount)
            {
                long diff = (long)newInfo.CharCount - oldInfo.CharCount;
                string sign = diff > 0 ? "+" : "";
                sb.AppendLine($"⚠ Character Count: {oldInfo.CharCount} → {newInfo.CharCount} ({sign}{diff})");
            }
            else
            {
                sb.AppendLine($"✓ Character Count: {oldInfo.CharCount}");
            }

            // 语言数量
            if (oldInfo.LanguageCount != newInfo.LanguageCount)
            {
                sb.AppendLine($"⚠ Language Count: {oldInfo.LanguageCount} → {newInfo.LanguageCount}");
            }
            else
            {
                sb.AppendLine($"✓ Language Count: {oldInfo.LanguageCount}");
            }

            // 无效字符宽度
            if (oldInfo.InvalidCharWidth != newInfo.InvalidCharWidth)
            {
                sb.AppendLine($"ℹ Invalid Char Width: {oldInfo.InvalidCharWidth} → {newInfo.InvalidCharWidth}");
            }
            else
            {
                sb.AppendLine($"✓ Invalid Char Width: {oldInfo.InvalidCharWidth}");
            }

            // 字符串数量
            int oldStrCount = oldInfo.Languages.FirstOrDefault()?.StringCount ?? 0;
            int newStrCount = newInfo.Languages.FirstOrDefault()?.StringCount ?? 0;
            if (oldStrCount != newStrCount)
            {
                sb.AppendLine($"ℹ String Count: {oldStrCount} → {newStrCount}");
            }
            else
            {
                sb.AppendLine($"✓ String Count: {oldStrCount}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 添加警告信息
        /// </summary>
        private static void AddWarnings(FontInfo info, byte[] fontData, byte[] fontIndex, FontValidationResult result)
        {
            // 字符数量警告
            if (info.CharCount > 2000)
            {
                result.Warnings.Add($"Large character set ({info.CharCount} chars). May increase firmware size.");
            }

            // 语言数量警告
            if (info.LanguageCount > 10)
            {
                result.Warnings.Add($"Many languages ({info.LanguageCount}). Ensure device supports all.");
            }

            // 文件大小警告
            int totalSize = fontData.Length + fontIndex.Length;
            if (totalSize > 200 * 1024) // 200KB
            {
                result.Warnings.Add($"Large font files ({totalSize / 1024} KB total). Consider optimizing.");
            }

            // 字符平均大小
            if (info.Characters.Count > 0)
            {
                double avgSize = (double)fontData.Length / info.Characters.Count;
                if (avgSize > 500)
                {
                    result.Warnings.Add($"Large average character size ({avgSize:F0} bytes/char).");
                }
            }

            // 检查一致性
            if (info.Characters.Count != info.CharCount)
            {
                result.ConsistencyWarning = 
                    $"Character count mismatch: header={info.CharCount}, parsed={info.Characters.Count}";
            }
        }
    }
}
