using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FontBuilder.Models;

namespace FontBuilder.Core
{
    /// <summary>
    /// 字符收集器：扫描所有语言字符串，去重并生成 charCode 集合
    /// 同时建立 charCode → 字符串索引映射，供后续构建 resfontidx.bin 使用
    /// </summary>
    public static class CharCollector
    {
        /// <summary>
        /// 始终包含的特殊字符：空格 (0x20)
        /// </summary>
        public const uint SpaceCharCode = 0x20;

        /// <summary>
        /// 收集结果
        /// </summary>
        public sealed class CollectResult
        {
            /// <summary>
            /// 排序后的 charCode 列表（升序，用于 font.bin 字符表）
            /// </summary>
            public List<uint> CharCodes { get; set; } = new();

            /// <summary>
            /// charCode → 字符表索引（0-based，按 CharCodes 顺序）
            /// </summary>
            public Dictionary<uint, int> CharCodeToIndex { get; set; } = new();

            /// <summary>
            /// 每语言的字符串→字符索引数组（用于 resfontidx.bin 写入）
            /// 每个字符串索引数组中，0 表示分隔符（字符串末尾）
            /// </summary>
            public List<List<int[]>> LanguageStringCharIndices { get; set; } = new();
        }

        /// <summary>
        /// 收集所有字符并建立索引映射
        /// </summary>
        /// <param name="config">已加载字符串的配置</param>
        public static CollectResult Collect(FontBuildConfig config)
        {
            var result = new CollectResult();
            var charSet = new SortedSet<uint> { SpaceCharCode };

            // 第一轮：收集所有字符码点
            foreach (var lang in config.Languages)
            {
                foreach (var str in lang.Strings)
                {
                    foreach (uint cc in EnumerateCharCodes(str))
                    {
                        charSet.Add(cc);
                    }
                }
            }

            // 建立排序后的列表与索引映射
            result.CharCodes = charSet.ToList();
            result.CharCodeToIndex = new Dictionary<uint, int>(result.CharCodes.Count);
            for (int i = 0; i < result.CharCodes.Count; i++)
            {
                result.CharCodeToIndex[result.CharCodes[i]] = i;
            }

            // 第二轮：建立每语言、每字符串的字符索引数组
            foreach (var lang in config.Languages)
            {
                var strList = new List<int[]>();
                foreach (var str in lang.Strings)
                {
                    strList.Add(BuildCharIndices(str, result.CharCodeToIndex));
                }
                result.LanguageStringCharIndices.Add(strList);
            }

            return result;
        }

        /// <summary>
        /// 枚举字符串中所有字符的 Unicode 码点
        /// 注意：原 fontSrc.exe 似乎按 UTF-16 码元处理，对 BMP 外字符会拆成代理对
        /// 这里采用相同策略以保持兼容性
        /// </summary>
        public static IEnumerable<uint> EnumerateCharCodes(string str)
        {
            if (string.IsNullOrEmpty(str)) yield break;
            for (int i = 0; i < str.Length; i++)
            {
                yield return (uint)str[i];
            }
        }

        /// <summary>
        /// 构建字符串的字符索引数组
        /// 末尾追加 0 作为分隔符（与 resfontidx.bin 字符串布局一致）
        /// </summary>
        private static int[] BuildCharIndices(string str, Dictionary<uint, int> charToIdx)
        {
            if (string.IsNullOrEmpty(str))
            {
                return new int[] { 0 }; // 仅分隔符
            }

            var indices = new List<int>(str.Length + 1);
            foreach (var cc in EnumerateCharCodes(str))
            {
                // 空格映射为索引 0？实际 resfontidx 中 0 作为分隔符，
                // 空格应在字符表中有索引（首字符 0x20 即为索引 0）
                // 但字符串间用 0 分隔，需检查：原工具是否对空格作特殊处理？
                // 实测 font.bin 索引 0 即空格；字符串中空格的引用是 0
                // 但 resfontidx 用 0 分隔，故空格的引用值与分隔符相同 —— 这会导致歧义
                // 实际从 AnalyzeFontBin.ps1 输出看，String #0 的 [5] Index=0x0000 标注为 "null terminator"
                // 这说明原工具确实用 0 作空格也是分隔符
                if (charToIdx.TryGetValue(cc, out int idx))
                    indices.Add(idx);
                else
                    indices.Add(0); // 未找到字符退化为 0（空格）
            }
            // 追加分隔符 0
            indices.Add(0);
            return indices.ToArray();
        }
    }
}
