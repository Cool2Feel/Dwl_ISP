using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FontBuilder.Models;

namespace FontBuilder.Core
{
    /// <summary>
    /// font.bin 写入器（等价于 fontSrc.exe 的二进制输出阶段）
    ///
    /// 文件格式:
    ///   [0x00] charCount  : u32      (字符总数)
    ///   [0x04] entries[]  : 8 bytes × charCount
    ///          ├─ charCode     : u32   (Unicode 码点)
    ///          └─ bitmapOffset : u32   (位图数据绝对偏移)
    ///   [0x04 + charCount*8] bitmapData : 每字符按 16 字节对齐，MSB 优先位序
    ///
    /// 注意：字符按 charCode 升序排列（实测 0x20, 0x21, 0x26, 0x27, 0x2C, 0x2D...）
    /// </summary>
    public static class FontBinWriter
    {
        /// <summary>
        /// 写入 font.bin
        /// </summary>
        /// <param name="glyphs">已渲染的字形列表（需按 charCode 升序）</param>
        /// <param name="outputPath">输出文件路径</param>
        public static void Write(List<CharGlyph> glyphs, string outputPath)
        {
            if (glyphs == null) throw new ArgumentNullException(nameof(glyphs));

            // 确保 charCode 升序排列（fontSrc.exe 输出顺序）
            glyphs.Sort((a, b) => a.CharCode.CompareTo(b.CharCode));

            // 重新分配索引
            for (int i = 0; i < glyphs.Count; i++)
                glyphs[i].Index = i;

            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: true);

            // 头部：字符总数
            bw.Write((uint)glyphs.Count);

            // 计算位图数据起始偏移
            uint bitmapDataStart = (uint)(4 + glyphs.Count * 8);

            // 第一遍：计算每字符位图偏移并写入索引表
            uint currentOffset = bitmapDataStart;
            foreach (var g in glyphs)
            {
                g.BitmapOffset = currentOffset;
                // 写入 [charCode:u32][bitmapOffset:u32]
                bw.Write((uint)g.CharCode);
                bw.Write((uint)g.BitmapOffset);
                currentOffset += (uint)g.AlignedSize;
            }

            // 第二遍：写入位图数据（已 16 字节对齐）
            foreach (var g in glyphs)
            {
                if (g.Bitmap == null || g.Bitmap.Length == 0)
                {
                    // 写入空白占位
                    var empty = new byte[g.AlignedSize];
                    bw.Write(empty, 0, empty.Length);
                }
                else
                {
                    // 确保对齐到 16 字节
                    int aligned = g.AlignedSize;
                    if (g.Bitmap.Length >= aligned)
                    {
                        bw.Write(g.Bitmap, 0, aligned);
                    }
                    else
                    {
                        bw.Write(g.Bitmap, 0, g.Bitmap.Length);
                        // 补齐对齐字节
                        int pad = aligned - g.Bitmap.Length;
                        if (pad > 0)
                        {
                            var padding = new byte[pad];
                            bw.Write(padding, 0, pad);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 在内存中构建 font.bin（用于验证与对比）
        /// </summary>
        public static byte[] BuildBytes(List<CharGlyph> glyphs)
        {
            if (glyphs == null) throw new ArgumentNullException(nameof(glyphs));
            glyphs.Sort((a, b) => a.CharCode.CompareTo(b.CharCode));
            for (int i = 0; i < glyphs.Count; i++) glyphs[i].Index = i;

            uint bitmapDataStart = (uint)(4 + glyphs.Count * 8);
            uint totalSize = bitmapDataStart;
            foreach (var g in glyphs)
                totalSize += (uint)g.AlignedSize;

            var ms = new MemoryStream((int)totalSize);
            using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
            {
                bw.Write((uint)glyphs.Count);

                uint currentOffset = bitmapDataStart;
                foreach (var g in glyphs)
                {
                    g.BitmapOffset = currentOffset;
                    bw.Write((uint)g.CharCode);
                    bw.Write((uint)g.BitmapOffset);
                    currentOffset += (uint)g.AlignedSize;
                }

                foreach (var g in glyphs)
                {
                    int aligned = g.AlignedSize;
                    int toWrite = Math.Min(g.Bitmap?.Length ?? 0, aligned);
                    if (toWrite > 0) bw.Write(g.Bitmap, 0, toWrite);
                    int pad = aligned - toWrite;
                    if (pad > 0) bw.Write(new byte[pad], 0, pad);
                }
            }
            return ms.ToArray();
        }
    }
}
