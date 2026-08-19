using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FontBuilder.Models;

namespace FontBuilder.Core
{
    /// <summary>
    /// resfontidx.bin 写入器（等价于 userStr.exe 的索引输出阶段）
    ///
    /// 文件格式（通过二进制分析还原）:
    ///   [0x00] header    : u32
    ///          ├─ magic        : 16 bits = 0x584D ('X','M')
    ///          ├─ invalidWidth : 8 bits  (无效字符宽度，默认 8)
    ///          └─ langCount    : 8 bits  (语言数量)
    ///   [0x04] fileSize  : u32      (文件总大小)
    ///   [0x08] langTable[] : 8 bytes × langCount
    ///          ├─ langId        : u32   (0xD000~0xD00D)
    ///          └─ strBlockOffset: u32   (字符串块绝对偏移)
    ///   [N]    stringBlocks[] : 每语言一块
    ///          块头: [0x0000:u16][blockSize:u16][0:u32]
    ///          字符串条目: [width:u16][height:u16][charCount:u16][dataOffset:u16] × N
    ///          字符索引数据: u16 × Σ(charCount)  (引用 resfont.bin 索引，0=分隔符)
    /// </summary>
    public static class ResFontIdxWriter
    {
        /// <summary>
        /// 字符串索引文件魔数
        /// </summary>
        public const ushort Magic = 0x584D;

        /// <summary>
        /// 字符串块头部标记
        /// </summary>
        public const ushort BlockMarker = 0x0000;

        /// <summary>
        /// 语言 ID 步长
        /// </summary>
        public const uint LangIdStep = 1;

        /// <summary>
        /// 写入 resfontidx.bin
        /// </summary>
        /// <param name="config">配置（含 InvalidCharWidth, LangIdBase）</param>
        /// <param name="collectResult">字符收集结果（含 LanguageStringCharIndices）</param>
        /// <param name="glyphs">已渲染字形列表（用于查 width/height）</param>
        /// <param name="outputPath">输出文件路径</param>
        public static void Write(
            FontBuildConfig config,
            CharCollector.CollectResult collectResult,
            List<CharGlyph> glyphs,
            string outputPath)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (collectResult == null) throw new ArgumentNullException(nameof(collectResult));
            if (glyphs == null) throw new ArgumentNullException(nameof(glyphs));

            byte[] bytes = BuildBytes(config, collectResult, glyphs);
            File.WriteAllBytes(outputPath, bytes);
        }

        /// <summary>
        /// 在内存中构建 resfontidx.bin
        /// </summary>
        public static byte[] BuildBytes(
            FontBuildConfig config,
            CharCollector.CollectResult collectResult,
            List<CharGlyph> glyphs)
        {
            int langCount = config.Languages.Count;
            if (langCount == 0) throw new InvalidOperationException("No languages configured");
            if (collectResult.LanguageStringCharIndices.Count != langCount)
                throw new InvalidOperationException("Language count mismatch");

            // 验证各语言字符串数一致（原 user_str.h 中 R_ID_STR_LAN_* 等都是同步的）
            int strCountPerLang = collectResult.LanguageStringCharIndices[0].Count;
            for (int i = 1; i < langCount; i++)
            {
                if (collectResult.LanguageStringCharIndices[i].Count != strCountPerLang)
                    throw new InvalidOperationException(
                        $"Language {i} string count mismatch: {collectResult.LanguageStringCharIndices[i].Count} vs {strCountPerLang}");
            }

            // 计算字符串块布局
            var blockLayouts = new BlockLayout[langCount];
            int langTableEnd = 8 + langCount * 8;
            uint currentBlockOffset = (uint)langTableEnd;

            for (int li = 0; li < langCount; li++)
            {
                var strIndices = collectResult.LanguageStringCharIndices[li];
                int strEntriesSize = strIndices.Count * 8;  // 每字符串条目 8 字节
                int blockHeaderSize = 8; // [0x0000][blockSize][0]

                // 字符索引数据大小：Σ(charCount) * 2
                int charIndexDataSize = 0;
                for (int si = 0; si < strIndices.Count; si++)
                {
                    charIndexDataSize += strIndices[si].Length * 2;
                }

                int blockSize = blockHeaderSize + strEntriesSize + charIndexDataSize;
                blockLayouts[li] = new BlockLayout
                {
                    BlockOffset = currentBlockOffset,
                    BlockSize = (ushort)blockSize,
                    StrEntriesSize = strEntriesSize,
                    CharIndexDataSize = charIndexDataSize
                };
                currentBlockOffset += (uint)blockSize;
            }

            uint totalSize = currentBlockOffset;

            // 写入字节流
            using var ms = new MemoryStream((int)totalSize);
            using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

            // 头部: magic | invalidWidth | langCount
            uint header = (uint)Magic | ((uint)config.InvalidCharWidth << 16) | ((uint)langCount << 24);
            bw.Write(header);

            // 文件总大小
            bw.Write(totalSize);

            // 语言表 [langId:u32][strBlockOffset:u32]
            for (int li = 0; li < langCount; li++)
            {
                uint langId = config.LangIdBase + (uint)li * LangIdStep;
                bw.Write(langId);
                bw.Write(blockLayouts[li].BlockOffset);
            }

            // 字符串块
            for (int li = 0; li < langCount; li++)
            {
                var strIndices = collectResult.LanguageStringCharIndices[li];
                var layout = blockLayouts[li];

                // 块头: [0x0000:u16][blockSize:u16][0:u32]
                bw.Write(BlockMarker);
                bw.Write(layout.BlockSize);
                bw.Write(0u);

                // 字符串条目区域: [width:u16][height:u16][charCount:u16][dataOffset:u16] × N
                // dataOffset 是相对于块起始的偏移，包含 8 字节块头 + strCount*8 字节条目区 + 累计字符数据偏移
                // 已通过 AnalyzeFontBin.ps1 验证：第一字符串的 dataOffset = 8 + 208*8 = 1672
                // (据此可反推字符串数：(1672 - 8) / 8 = 208)
                int relOffset = 8 + strIndices.Count * 8; // 跳过块头 + 全部条目区
                for (int si = 0; si < strIndices.Count; si++)
                {
                    var indices = strIndices[si];
                    int charCount = indices.Length; // 含末尾 0 分隔符

                    // 计算字符串的 width/height（基于字符最大宽度与最大高度）
                    ushort strWidth = 0;
                    ushort strHeight = 0;
                    foreach (var idx in indices)
                    {
                        if (idx == 0) continue; // 跳过分隔符
                        if (idx >= 0 && idx < glyphs.Count)
                        {
                            strWidth += glyphs[idx].Width;
                            if (glyphs[idx].Height > strHeight)
                                strHeight = glyphs[idx].Height;
                        }
                    }

                    bw.Write(strWidth);
                    bw.Write(strHeight);
                    bw.Write((ushort)charCount);
                    bw.Write((ushort)relOffset);

                    relOffset += charCount * 2;
                }

                // 字符索引数据 u16 × Σ(charCount)
                for (int si = 0; si < strIndices.Count; si++)
                {
                    foreach (var idx in strIndices[si])
                    {
                        bw.Write((ushort)idx);
                    }
                }
            }

            return ms.ToArray();
        }

        private sealed class BlockLayout
        {
            public uint BlockOffset;
            public ushort BlockSize;
            public int StrEntriesSize;
            public int CharIndexDataSize;
        }
    }
}
