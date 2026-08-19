using System;
using System.Drawing;

namespace FontBuilder.Models
{
    /// <summary>
    /// 单个字符的字形信息（含位图）
    /// </summary>
    public sealed class CharGlyph
    {
        /// <summary>
        /// Unicode 码点（如 0x20=空格, 0x4E2D='中'）
        /// </summary>
        public uint CharCode { get; set; }

        /// <summary>
        /// 字符宽度（像素）
        /// </summary>
        public ushort Width { get; set; }

        /// <summary>
        /// 字符高度（像素）
        /// </summary>
        public ushort Height { get; set; }

        /// <summary>
        /// 单色位图数据（每字节8个像素，MSB 优先，按行存储）
        /// 已按 16 字节对齐
        /// </summary>
        public byte[] Bitmap { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// 位图数据在 font.bin 中的绝对偏移（写入时填充）
        /// </summary>
        public uint BitmapOffset { get; set; }

        /// <summary>
        /// 在字符表中的索引（写入时填充，0-based）
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 字符显示文本
        /// </summary>
        public string DisplayText
        {
            get
            {
                if (CharCode == 0) return "\\0";
                if (CharCode == 0x20) return "SP";
                try
                {
                    var ch = (char)CharCode;
                    return char.IsControl(ch) ? $"0x{CharCode:X4}" : ch.ToString();
                }
                catch
                {
                    return $"0x{CharCode:X4}";
                }
            }
        }

        /// <summary>
        /// 数据大小（未对齐）：((width+7)/8) * height
        /// </summary>
        public int RawDataSize => ((Width + 7) / 8) * Height;

        /// <summary>
        /// 对齐后大小（16字节对齐）
        /// </summary>
        public int AlignedSize => (RawDataSize + 15) & ~15;
    }
}
