using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ResBinManager.Core
{
    public class PaletteColor
    {
        public int Index { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte A { get; set; }

        public byte OriginalR { get; set; }
        public byte OriginalG { get; set; }
        public byte OriginalB { get; set; }
        public byte OriginalA { get; set; }

        public byte RawByte0 { get; set; }
        public byte RawByte1 { get; set; }
        public byte RawByte2 { get; set; }
        public byte RawByte3 { get; set; }

        public ushort OriginalRgb565 { get; set; }
        public byte OriginalTagByte { get; set; }
        public byte OriginalAlpha5 { get; set; }

        /// <summary>
        /// RGB565原始值的十六进制字符串 (如 "0xF800")
        /// </summary>
        public string Rgb565String => $"0x{OriginalRgb565:X4}";

        /// <summary>
        /// 5位Alpha值的显示字符串 (如 "A5=31 (0x1F) -> A8=255")
        /// </summary>
        public string Alpha5BitString => $"A5={OriginalAlpha5} (0x{OriginalAlpha5:X2}) -> A8={A}";

        /// <summary>
        /// 完整的原始数据字符串
        /// </summary>
        public string RawDataString => $"Idx=0x{Index:X2} RGB565={Rgb565String} A5=0x{OriginalAlpha5:X2} Raw=[{RawByte0:X2},{RawByte1:X2},{RawByte2:X2},{RawByte3:X2}]";

        private static readonly Dictionary<int, string> CustomColorNames = new Dictionary<int, string>
        {
            { 0xF0, "RESERVED" },
            { 0xF1, "GARY3" },
            { 0xF2, "GARY2" },
            { 0xF3, "BLUE2" },
            { 0xF4, "DBLUE" },
            { 0xF5, "BLUE1" },
            { 0xF6, "GARY1" },
            { 0xF7, "YELLOW" },
            { 0xF8, "TBLACK" },
            { 0xF9, "TRANSFER" },
            { 0xFA, "BLACK" },
            { 0xFB, "WHITE" },
            { 0xFC, "BLUE" },
            { 0xFD, "GREEN" },
            { 0xFE, "RED" },
            { 0xFF, "ERROR" }
        };

        public static readonly Dictionary<int, uint> StandardColorValues = new Dictionary<int, uint>
        {
            { 0xFF, 0x00000000 },
            { 0xFE, 0xFFFF0000 },
            { 0xFD, 0xFF00FF00 },
            { 0xFC, 0xFF0000FF },
            { 0xFB, 0xFFFFFFFF },
            { 0xFA, 0xFF000000 },
            { 0xF9, 0x00000001 },
            { 0xF8, 0x80000000 },
            { 0xF7, 0xFFEBAC14 },
            { 0xF6, 0xFF757575 },
            { 0xF5, 0xFF0080E0 },
            { 0xF4, 0xFF022959 },
            { 0xF3, 0xFF60C0C0 },
            { 0xF2, 0xFF303030 },
            { 0xF1, 0xFF505050 }
        };

        public string Name => CustomColorNames.TryGetValue(Index, out var name) ? name : string.Empty;
        public string HexValue => $"0x{R:X2}{G:X2}{B:X2}{A:X2}";
        public string RgbaString => $"RGBA({R}, {G}, {B}, {A})";
        public string OriginalHexValue => $"0x{RawByte0:X2}{RawByte1:X2}{RawByte2:X2}{RawByte3:X2}";
        public string OriginalRgbaString => $"RGBA({OriginalR}, {OriginalG}, {OriginalB}, {OriginalA})";
        public string DisplayString => !string.IsNullOrEmpty(Name) 
            ? $"0x{Index:X2} {Name}" 
            : $"0x{Index:X2}";

        public bool IsTransparent => Index == 0xF9 || (R == 0 && G == 0 && B == 0 && A == 0);

        public bool HasStandardColor => StandardColorValues.ContainsKey(Index);

        public System.Windows.Media.SolidColorBrush Brush
        {
            get
            {
                byte displayAlpha = A;
                if (A == 0 && !(R == 0 && G == 0 && B == 0))
                {
                    displayAlpha = 255;
                }
                return new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(displayAlpha, R, G, B));
            }
        }

        public System.Windows.Media.SolidColorBrush StandardBrush
        {
            get
            {
                if (StandardColorValues.TryGetValue(Index, out var argb))
                {
                    byte a = (byte)((argb >> 24) & 0xFF);
                    byte r = (byte)((argb >> 16) & 0xFF);
                    byte g = (byte)((argb >> 8) & 0xFF);
                    byte b = (byte)(argb & 0xFF);
                    if (a == 0 && !(r == 0 && g == 0 && b == 0))
                        a = 255;
                    return new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(a, r, g, b));
                }
                return Brush;
            }
        }
    }

    public class PaletteInfo
    {
        public int TotalColors { get; set; }
        public int NonZeroColors { get; set; }
        public int TransparentColors { get; set; }
        public int GrayscaleColors { get; set; }
        public PaletteColor BackgroundColor { get; set; } = new PaletteColor();
        public PaletteColor TransparentColor { get; set; } = new PaletteColor();
        public List<PaletteColor> CustomColors { get; set; } = new List<PaletteColor>();
        public List<PaletteColor> AllColors { get; set; } = new List<PaletteColor>();

        public string DisplayName => $"{NonZeroColors}/{TotalColors} colors";
        public string StatsDisplay => $"Non-zero: {NonZeroColors}, Transparent: {TransparentColors}, Grayscale: {GrayscaleColors}";
    }

    public static class PaletteParser
    {
        public const int PaletteSize = 1024;
        public const int ColorCount = 256;
        public const int BytesPerColor = 4;

        private static byte ConvertRgb5ToRgb8(int rgb5)
        {
            return (byte)((rgb5 * 255 + 15) / 31);
        }

        private static byte ConvertRgb6ToRgb8(int rgb6)
        {
            return (byte)((rgb6 * 255 + 31) / 63);
        }

        public static List<PaletteColor> ParsePalette(byte[] paletteData)
        {
            var colors = new List<PaletteColor>();

            int count = Math.Min(ColorCount, paletteData.Length / BytesPerColor);

            for (int i = 0; i < count; i++)
            {
                int offset = i * BytesPerColor;
                byte raw0 = paletteData[offset];       // RGB565 low byte
                byte raw1 = paletteData[offset + 1];   // RGB565 high byte
                byte raw2 = paletteData[offset + 2];   // tagByte
                byte raw3 = paletteData[offset + 3];   // alpha

                uint colorVal = BitConverter.ToUInt32(paletteData, offset);
                ushort rgb565Val = (ushort)(colorVal & 0xFFFF);
                
                byte r = ConvertRgb5ToRgb8((rgb565Val >> 11) & 0x1F);
                byte g = ConvertRgb6ToRgb8((rgb565Val >> 5) & 0x3F);
                byte b = ConvertRgb5ToRgb8(rgb565Val & 0x1F);
                
                // 参考 Platte_Tiga2Vison string2pixel() 的位域布局:
                // d = (A5<<16) | (R5<<11) | (G6<<5) | B5
                // Byte2 bits[4:0] = 5位Alpha (A[7:3]), Byte3 = 0x00 (未使用)
                byte tagByte = (byte)((colorVal >> 16) & 0xFF);
                byte alpha5 = (byte)(tagByte & 0x1F);
                byte a = ConvertRgb5ToRgb8(alpha5);

                byte origR = r;
                byte origG = g;
                byte origB = b;
                byte origA = a;

                var color = new PaletteColor
                {
                    Index = i,
                    RawByte0 = raw0,
                    RawByte1 = raw1,
                    RawByte2 = raw2,
                    RawByte3 = raw3,
                    OriginalRgb565 = rgb565Val,
                    OriginalTagByte = tagByte,
                    OriginalAlpha5 = alpha5,
                    OriginalR = origR,
                    OriginalG = origG,
                    OriginalB = origB,
                    OriginalA = origA,
                    R = r,
                    G = g,
                    B = b,
                    A = a
                };

                if (color.Index >= 0xF0)
                {
                    ApplyStandardColor(color);
                }

                colors.Add(color);
            }

            return colors;
        }

        private static void ApplyStandardColor(PaletteColor color)
        {
            if (PaletteColor.StandardColorValues.TryGetValue(color.Index, out var argb))
            {
                color.A = (byte)((argb >> 24) & 0xFF);
                color.R = (byte)((argb >> 16) & 0xFF);
                color.G = (byte)((argb >> 8) & 0xFF);
                color.B = (byte)(argb & 0xFF);
            }
        }

        /// <summary>
        /// 将8位ARGB转换为压缩格式 (参考 Platte_Tiga2Vison string2pixel 的逆运算)
        /// 输入: A8/R8/G8/B8 -> 输出: 4字节 [RGB565_lo, RGB565_hi, A5, 0x00]
        /// </summary>
        public static uint ConvertArgb8888ToPacked(byte a, byte r, byte g, byte b)
        {
            // 与 string2pixel 相同的位域提取: 取各通道高5/6位
            // s = 0xAABBGGRR, d = (A5<<16)|(R5<<11)|(G6<<5)|B5
            uint a5 = (uint)((a >> 3) & 0x1F);   // A[7:3]
            uint r5 = (uint)((r >> 3) & 0x1F);   // R[7:3]
            uint g6 = (uint)((g >> 2) & 0x3F);   // G[7:2]
            uint b5 = (uint)((b >> 3) & 0x1F);   // B[7:3]

            uint packed = (a5 << 16) | (r5 << 11) | (g6 << 5) | b5;
            return packed;
        }

        /// <summary>
        /// 将 PaletteColor 转换回4字节二进制数据 (用于导出/保存)
        /// </summary>
        public static byte[] ColorToBytes(PaletteColor color)
        {
            uint packed = ConvertArgb8888ToPacked(color.A, color.R, color.G, color.B);
            return BitConverter.GetBytes(packed);
        }

        public static PaletteInfo ParsePaletteInfo(byte[] paletteData)
        {
            var info = new PaletteInfo();
            var colors = ParsePalette(paletteData);

            info.AllColors = colors;
            info.TotalColors = colors.Count;

            foreach (var color in colors)
            {
                if (color.R != 0 || color.G != 0 || color.B != 0)
                    info.NonZeroColors++;

                if (color.IsTransparent)
                    info.TransparentColors++;

                if (color.R == color.G && color.G == color.B)
                    info.GrayscaleColors++;
            }

            if (colors.Count > 0)
                info.BackgroundColor = colors[0];

            if (colors.Count > 0xF9)
                info.TransparentColor = colors[0xF9];

            info.CustomColors = colors.Where(c => c.Index >= 0xF0 && c.Index <= 0xFF).ToList();

            return info;
        }

        public static byte[] CreatePaletteImage(byte[] paletteData, int colorsPerRow = 16)
        {
            var colors = ParsePalette(paletteData);
            int rows = (colors.Count + colorsPerRow - 1) / colorsPerRow;
            int width = colorsPerRow * 16;
            int height = rows * 16;

            byte[] rgbaData = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int colorIndex = (y / 16) * colorsPerRow + (x / 16);
                    
                    byte r, g, b, a;
                    
                    if (colorIndex < colors.Count)
                    {
                        var c = colors[colorIndex];
                        r = c.R;
                        g = c.G;
                        b = c.B;
                        a = c.A;
                        if (a == 0 && !(r == 0 && g == 0 && b == 0))
                            a = 255;
                    }
                    else
                    {
                        r = g = b = a = 0;
                    }

                    int rgbaOffset = (y * width + x) * 4;
                    rgbaData[rgbaOffset] = b;
                    rgbaData[rgbaOffset + 1] = g;
                    rgbaData[rgbaOffset + 2] = r;
                    rgbaData[rgbaOffset + 3] = a;
                }
            }

            return OsdSourceParser.ConvertRgba32ToBmp(width, height, rgbaData);
        }

        public static void ExportPaletteAsImage(byte[] paletteData, string outputPath)
        {
            byte[] bmpData = CreatePaletteImage(paletteData);
            File.WriteAllBytes(outputPath, bmpData);
        }

        public static void ExportPaletteAsText(byte[] paletteData, string outputPath)
        {
            var colors = ParsePalette(paletteData);
            using var writer = new StreamWriter(outputPath);

            writer.WriteLine("u32 Tab[256] = ");
            writer.WriteLine("{");

            for (int i = 0; i < colors.Count; i++)
            {
                var color = colors[i];
                uint value = ((uint)color.A << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;

                string prefix = "";
                string suffix = i == 255 ? "" : ",";

                writer.Write($"{prefix}0x{value:x8}{suffix}");

                if ((i + 1) % 8 == 0 && i != 255)
                    writer.WriteLine();
                else if (i != 255)
                    writer.Write("");
            }

            writer.WriteLine();
            writer.WriteLine("};");
        }

        public static void ExportPaletteAsTextRaw(byte[] paletteData, string outputPath)
        {
            using var writer = new StreamWriter(outputPath);
            writer.WriteLine("u32 Tab[256] = ");
            writer.WriteLine("{");

            for (int i = 0; i < 256; i++)
            {
                int offset = i * 4;
                uint value = BitConverter.ToUInt32(paletteData, offset);
                string prefix = i % 8 == 0 ? "" : "";
                string suffix = i == 255 ? "" : ",";
                writer.Write($"{prefix}0x{value:x8}{suffix}");
                if ((i + 1) % 8 == 0 && i != 255) writer.WriteLine();
                else if (i != 255) writer.Write("");
            }

            writer.WriteLine();
            writer.WriteLine("};");
        }

        public static void ExportPaletteAsGpl(byte[] paletteData, string outputPath)
        {
            var colors = ParsePalette(paletteData);
            using var writer = new StreamWriter(outputPath);

            writer.WriteLine("GIMP Palette");
            writer.WriteLine("Name: AX329x OSD Palette");
            writer.WriteLine("Columns: 16");
            writer.WriteLine("#");

            foreach (var color in colors)
            {
                writer.WriteLine($"{color.R} {color.G} {color.B} Index_{color.Index:X2}");
            }
        }

        public static string GetPaletteInfo(byte[] paletteData)
        {
            if (paletteData.Length != PaletteSize)
                return $"Invalid palette size: {paletteData.Length} bytes (expected {PaletteSize})";

            var colors = ParsePalette(paletteData);
            int nonZeroColors = colors.Count(c => c.R != 0 || c.G != 0 || c.B != 0 || c.A != 0);

            return $"Palette: {colors.Count} colors ({nonZeroColors} non-zero)";
        }

        public static List<PaletteColor> GetCustomColors(byte[] paletteData)
        {
            var colors = ParsePalette(paletteData);
            return colors.Where(c => c.Index >= 0xF0 && c.Index <= 0xFF).ToList();
        }

        public static string GetCustomColorsInfo(byte[] paletteData)
        {
            var customColors = GetCustomColors(paletteData);
            var builder = new System.Text.StringBuilder();
            
            builder.AppendLine("Custom colors (0xF0-0xFF):");
            
            foreach (var color in customColors)
            {
                builder.AppendLine($"  [{color.Index:X2}] {color.RgbaString}");
            }
            
            return builder.ToString();
        }

        public static bool ValidatePalette(byte[] paletteData)
        {
            if (paletteData.Length != PaletteSize)
                return false;

            var colors = ParsePalette(paletteData);
            
            int nonZeroColors = colors.Count(c => c.R != 0 || c.G != 0 || c.B != 0);
            if (nonZeroColors < 2)
                return false;

            return true;
        }
    }
}