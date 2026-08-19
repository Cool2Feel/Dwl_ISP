using System;

namespace ResBinManager.Core
{
    /// <summary>
    /// 调色板资源验证结果
    /// </summary>
    public class PaletteValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Info { get; set; }
    }

    /// <summary>
    /// AX329x 调色板资源验证器
    /// palette.bin 和 palette_game.bin 是固定1024字节的颜色查找表
    /// </summary>
    public static class PaletteValidator
    {
        /// <summary>
        /// 验证调色板资源
        /// </summary>
        /// <param name="paletteData">调色板数据</param>
        /// <returns>验证结果</returns>
        public static PaletteValidationResult Validate(byte[] paletteData)
        {
            var result = new PaletteValidationResult();

            // 1. 检查大小是否为1024字节
            if (paletteData.Length != 1024)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Invalid palette size: expected 1024 bytes, got {paletteData.Length}";
                return result;
            }

            // 2. 检查数据合理性（格式: RGB565[2字节] + tagByte[1字节] + alpha[1字节]）
            int nonZeroColors = 0;
            int validRgb565Colors = 0;

            for (int i = 0; i < paletteData.Length; i += 4)
            {
                uint color = BitConverter.ToUInt32(paletteData, i);
                ushort rgb565Val = (ushort)(color & 0xFFFF);
                byte tagByte = (byte)((color >> 16) & 0xFF);
                byte a = (byte)((color >> 24) & 0xFF);

                if (rgb565Val != 0 || tagByte != 0 || a != 0)
                {
                    nonZeroColors++;
                }

                if (IsValidRgb565(rgb565Val))
                {
                    validRgb565Colors++;
                }
            }

            // 如果所有颜色都是零，可能不是有效的调色板
            if (nonZeroColors == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "Palette contains all zero colors, likely invalid";
                return result;
            }

            int colorCount = paletteData.Length / 4;
            double validRatio = (double)validRgb565Colors / colorCount * 100;

            result.IsValid = true;
            result.Info = $"RGB565 palette: {colorCount} colors ({nonZeroColors} non-zero), {validRatio:F1}% valid RGB565 values";

            if (validRatio < 90)
            {
                result.Info += " [Warning: low valid ratio]";
            }

            return result;
        }

        private static bool IsValidRgb565(ushort rgb565)
        {
            int r = (rgb565 >> 11) & 0x1F;
            int g = (rgb565 >> 5) & 0x3F;
            int b = rgb565 & 0x1F;

            return r <= 31 && g <= 63 && b <= 31;
        }

        /// <summary>
        /// 获取调色板的显示文本
        /// </summary>
        public static string GetDisplayText(PaletteValidationResult result)
        {
            if (!result.IsValid)
            {
                return $"Invalid Palette\n{result.ErrorMessage}";
            }

            return $"Valid Palette\n{result.Info}";
        }
    }
}
