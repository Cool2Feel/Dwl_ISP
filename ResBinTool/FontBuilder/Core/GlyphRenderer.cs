using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using FontBuilder.Models;

namespace FontBuilder.Core
{
    /// <summary>
    /// 字符渲染器：将 Unicode 字符渲染为单色位图（MSB 优先，16字节对齐）
    /// 等价于 fontSrc.exe 的字符位图生成逻辑
    /// </summary>
    public sealed class GlyphRenderer : IDisposable
    {
        private readonly FontSelectConfig _fontConfig;
        private readonly Font _font;
        private readonly Graphics _measureGraphics;
        private readonly StringFormat _stringFormat;

        /// <summary>
        /// 二值化阈值（0-255，alpha 通道像素 > 阈值视为前景）
        /// </summary>
        public byte BinarizationThreshold { get; set; } = 128;

        public GlyphRenderer(FontSelectConfig fontConfig)
        {
            _fontConfig = fontConfig ?? throw new ArgumentNullException(nameof(fontConfig));

            // 尝试加载字体族；若不存在回退到 Microsoft Sans Serif / Arial
            var fontFamily = ResolveFontFamily(_fontConfig.FontFamily);
            _font = new Font(fontFamily, _fontConfig.SizeInPoints, _fontConfig.FontStyle, GraphicsUnit.Point);

            _measureGraphics = Graphics.FromImage(new Bitmap(1, 1));
            _measureGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _measureGraphics.TextContrast = 0;

            _stringFormat = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces)
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.None
            };
        }

        /// <summary>
        /// 渲染单个字符到 CharGlyph
        /// </summary>
        public CharGlyph RenderChar(uint charCode)
        {
            var glyph = new CharGlyph { CharCode = charCode };

            if (charCode == 0)
            {
                // 空字符：返回 8x16 空位图（与原工具保持兼容）
                glyph.Width = 8;
                glyph.Height = 16;
                glyph.Bitmap = new byte[16]; // 16字节对齐
                return glyph;
            }

            char c = (char)charCode;
            string text = new string(c, 1);

            // 测量字符尺寸
            SizeF measured = _measureGraphics.MeasureString(text, _font, int.MaxValue, _stringFormat);

            int width = Math.Max(1, (int)Math.Ceiling(measured.Width));
            int height = Math.Max(1, (int)Math.Ceiling(measured.Height));

            // 用更大画布渲染后裁剪到实际边界
            int padW = width + 2;
            int padH = height + 2;

            using var bmp = new Bitmap(padW, padH, PixelFormat.Format32bppArgb);
            bmp.SetResolution(96, 96);

            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.TextContrast = 0;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

                using var brush = new SolidBrush(Color.Black);
                g.DrawString(text, _font, brush, 0, 0, _stringFormat);
            }

            // 找到字符的实际边界（非透明像素的包围盒）
            var bounds = FindContentBounds(bmp, padW, padH);
            if (bounds.IsEmpty)
            {
                // 空字符（如空格）使用测量尺寸
                bounds = new Rectangle(0, 0, Math.Max(1, width / 2), height);
            }

            glyph.Width = (ushort)bounds.Width;
            glyph.Height = (ushort)bounds.Height;

            // 转换为单色位图（1bpp，每字节8像素，MSB 优先）
            int bytesPerRow = (glyph.Width + 7) / 8;
            int rawDataSize = bytesPerRow * glyph.Height;
            int alignedSize = (rawDataSize + 15) & ~15;

            var bitmap = new byte[alignedSize];
            var rect = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                IntPtr scan0 = data.Scan0;
                int bytesPerPixel = 4;

                for (int y = 0; y < glyph.Height; y++)
                {
                    int rowBase = y * stride;
                    for (int x = 0; x < glyph.Width; x++)
                    {
                        int idx = rowBase + x * bytesPerPixel;
                        byte alpha = Marshal.ReadByte(scan0, idx + 3);
                        // alpha < 阈值 视为背景；>= 阈值视为前景
                        if (alpha >= BinarizationThreshold)
                        {
                            int byteIdx = y * bytesPerRow + (x >> 3);
                            int bitIdx = 7 - (x & 7);
                            bitmap[byteIdx] |= (byte)(1 << bitIdx);
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            glyph.Bitmap = bitmap;
            return glyph;
        }

        /// <summary>
        /// 批量渲染所有字符
        /// </summary>
        public List<CharGlyph> RenderAll(IEnumerable<uint> charCodes, IProgress<(int done, int total, uint current)> progress = null)
        {
            var codes = new List<uint>(charCodes);
            var result = new List<CharGlyph>(codes.Count);
            for (int i = 0; i < codes.Count; i++)
            {
                var g = RenderChar(codes[i]);
                g.Index = i;
                result.Add(g);
                progress?.Report((i + 1, codes.Count, codes[i]));
            }
            return result;
        }

        private static Rectangle FindContentBounds(Bitmap bmp, int w, int h)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                IntPtr scan0 = data.Scan0;
                for (int y = 0; y < h; y++)
                {
                    int rowBase = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int idx = rowBase + x * 4;
                        byte alpha = Marshal.ReadByte(scan0, idx + 3);
                        if (alpha > 0)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            if (maxX < 0) return Rectangle.Empty;
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static FontFamily ResolveFontFamily(string familyName)
        {
            if (IsFontInstalled(familyName, out var found))
                return found;

            // 回退字体族
            string[] fallbacks = { "Microsoft Sans Serif", "Arial", "Microsoft YaHei", "SimSun" };
            foreach (var fb in fallbacks)
            {
                if (IsFontInstalled(fb, out found))
                    return found;
            }
            return FontFamily.GenericSansSerif;
        }

        /// <summary>
        /// 检查字体族是否已安装（兼容 net48 与 net6，不依赖 FontFamily.IsAvailable）
        /// </summary>
        private static bool IsFontInstalled(string name, out FontFamily family)
        {
            family = null;
            try
            {
                using var fonts = new InstalledFontCollection();
                foreach (var ff in fonts.Families)
                {
                    if (string.Equals(ff.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        family = ff;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public void Dispose()
        {
            _font?.Dispose();
            _measureGraphics?.Dispose();
            _stringFormat?.Dispose();
        }
    }
}
