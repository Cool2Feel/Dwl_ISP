using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FontBuilder.Models;

namespace FontBuilder.Core
{
    /// <summary>
    /// resfont.bin 写入器（等价于 userStr.exe 的字体数据输出阶段）
    ///
    /// 文件格式:
    ///   [0x00] charCount  : u32      (字符总数)
    ///   [0x04] entries[]  : 8 bytes × charCount
    ///          ├─ bitmapOffset : u32   (位图数据绝对偏移)
    ///          ├─ width        : u16   (字符宽度，像素)
    ///          └─ height       : u16   (字符高度，像素)
    ///   [0x04 + charCount*8] bitmapData : 与 font.bin 相同的位图数据
    ///
    /// 注意：resfont.bin 丢弃了 charCode，仅保留渲染信息
    /// </summary>
    public static class ResFontBinWriter
    {
        /// <summary>
        /// 写入 resfont.bin
        /// </summary>
        /// <param name="glyphs">已渲染并已分配 BitmapOffset 的字形列表（需与 font.bin 相同顺序）</param>
        /// <param name="outputPath">输出文件路径</param>
        public static void Write(List<CharGlyph> glyphs, string outputPath)
        {
            if (glyphs == null) throw new ArgumentNullException(nameof(glyphs));

            // 字符顺序需与 font.bin 一致（已按 charCode 升序）
            // 重新计算 resfont.bin 中的 bitmapOffset（结构不同：8字节/项，无 charCode）
            uint bitmapDataStart = (uint)(4 + glyphs.Count * 8);
            uint currentOffset = bitmapDataStart;

            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: true);

            // 头部：字符总数
            bw.Write((uint)glyphs.Count);

            // 第一遍：写索引表 [bitmapOffset:u32][width:u16][height:u16]
            foreach (var g in glyphs)
            {
                bw.Write((uint)currentOffset);
                bw.Write((ushort)g.Width);
                bw.Write((ushort)g.Height);
                currentOffset += (uint)g.AlignedSize;
            }

            // 第二遍：写位图数据
            foreach (var g in glyphs)
            {
                int aligned = g.AlignedSize;
                int toWrite = Math.Min(g.Bitmap?.Length ?? 0, aligned);
                if (toWrite > 0) bw.Write(g.Bitmap, 0, toWrite);
                int pad = aligned - toWrite;
                if (pad > 0)
                {
                    var padding = new byte[pad];
                    bw.Write(padding, 0, pad);
                }
            }
        }

        /// <summary>
        /// 在内存中构建 resfont.bin
        /// </summary>
        public static byte[] BuildBytes(List<CharGlyph> glyphs)
        {
            if (glyphs == null) throw new ArgumentNullException(nameof(glyphs));

            uint bitmapDataStart = (uint)(4 + glyphs.Count * 8);
            uint totalSize = bitmapDataStart;
            foreach (var g in glyphs) totalSize += (uint)g.AlignedSize;

            var ms = new MemoryStream((int)totalSize);
            using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
            {
                bw.Write((uint)glyphs.Count);

                uint currentOffset = bitmapDataStart;
                foreach (var g in glyphs)
                {
                    bw.Write((uint)currentOffset);
                    bw.Write((ushort)g.Width);
                    bw.Write((ushort)g.Height);
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
