using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ResBinManager.Core
{
    /// <summary>
    /// 单个字符的信息
    /// </summary>
    public class CharInfo
    {
        /// <summary>
        /// 字符在 resfont.bin 中的序号
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 字符宽度（像素）
        /// </summary>
        public ushort Width { get; set; }

        /// <summary>
        /// 字符高度（像素）
        /// </summary>
        public ushort Height { get; set; }

        /// <summary>
        /// 位图数据在文件中的偏移
        /// </summary>
        public uint Offset { get; set; }

        /// <summary>
        /// 从 font.bin 解析出的 charCode（unicode 码点或 GBK 复合值）
        /// </summary>
        public uint CharCode { get; set; }

        /// <summary>
        /// 是否已通过 font.bin 解析出 CharCode
        /// </summary>
        public bool HasCharCode { get; set; }

        /// <summary>
        /// 位图数据大小（字节）
        /// </summary>
        public int DataSize => ((Width + 7) / 8) * Height;

        /// <summary>
        /// 对齐后的大小（16字节对齐）
        /// </summary>
        public int AlignedSize
        {
            get
            {
                int size = DataSize;
                return (size + 15) & ~15; // 16字节对齐
            }
        }

        /// <summary>
        /// 获取显示用 Unicode 字符
        /// </summary>
        public string GetDisplayChar()
        {
            if (!HasCharCode || CharCode > 0xFFFF)
                return "?";

            if (CharCode >= 0x81 && CharCode <= 0xFE)
            {
                // GBK 双字节
                byte b0 = (byte)(CharCode >> 8);
                byte b1 = (byte)(CharCode & 0xFF);
                if (b0 >= 0x81 && b1 >= 0x40)
                {
                    try
                    {
                        return Encoding.GetEncoding("GBK").GetString(new byte[] { b0, b1 });
                    }
                    catch { }
                }
            }
            return char.ConvertFromUtf32((int)CharCode);
        }
    }

    /// <summary>
    /// 字符串信息
    /// </summary>
    public class StringInfo
    {
        /// <summary>
        /// 字符串总宽度
        /// </summary>
        public ushort Width { get; set; }

        /// <summary>
        /// 字符串高度
        /// </summary>
        public ushort Height { get; set; }

        /// <summary>
        /// 字符数量
        /// </summary>
        public ushort Number { get; set; }

        /// <summary>
        /// 字符串数据在索引文件中的绝对偏移
        /// </summary>
        public int DataOffset { get; set; }

        /// <summary>
        /// 字符索引数组
        /// </summary>
        public ushort[] CharIndices { get; set; } = new ushort[0];
    }

    /// <summary>
    /// 语言信息
    /// </summary>
    public class LanguageInfo
    {
        /// <summary>
        /// 语言ID (0xD000~0xD00D)
        /// </summary>
        public uint LanguageId { get; set; }

        /// <summary>
        /// 该语言字符串块在文件中的偏移
        /// </summary>
        public uint StringBlockOffset { get; set; }

        /// <summary>
        /// 该语言的字符串数量
        /// </summary>
        public int StringCount { get; set; }
    }

    public static class StringDecoder
    {
        /// <summary>
        /// 解码字符串：通过 font.bin 查找 charCode
        /// </summary>
        /// <param name="fontIndex">resfontidx.bin 数据</param>
        /// <param name="fontBinData">font.bin 数据（含 charCode→index 映射）</param>
        /// <param name="strInfo">字符串信息</param>
        public static string DecodeString(byte[] fontIndex, byte[]? fontBinData, StringInfo strInfo)
        {
            if (fontIndex == null || strInfo.Number == 0)
                return string.Empty;

            int dataOffset = strInfo.DataOffset;

            if (dataOffset < 0 || dataOffset >= fontIndex.Length)
            {
                System.Diagnostics.Debug.WriteLine($"[StringDecoder] Invalid offset: {dataOffset}, max: {fontIndex.Length}");
                return string.Empty;
            }

            if (strInfo.Number > 1000)
            {
                System.Diagnostics.Debug.WriteLine($"[StringDecoder] Number too large: {strInfo.Number}");
                return string.Empty;
            }

            if (dataOffset + strInfo.Number * 2 > fontIndex.Length)
            {
                System.Diagnostics.Debug.WriteLine($"[StringDecoder] Out of range: offset={dataOffset}, count={strInfo.Number}, max={fontIndex.Length}");
                return string.Empty;
            }

            // 没有 font.bin 时无法解析 charCode，降级为显示 charIndex 十六进制
            if (fontBinData == null || fontBinData.Length < 4)
                return DecodeStringHex(fontIndex, strInfo);

            strInfo.CharIndices = new ushort[strInfo.Number];
            StringBuilder sb = new StringBuilder();

            // font.bin 结构: header(4) + entries[charCode:4][bitmapOffset:4]
            int charEntrySize = 8;

            for (int i = 0; i < strInfo.Number; i++)
            {
                ushort charIndex = BitConverter.ToUInt16(fontIndex, dataOffset + i * 2);
                strInfo.CharIndices[i] = charIndex;

                // charIndex=0 对应空格 (charCode=0x20)
                if (charIndex == 0)
                {
                    sb.Append(' ');
                    continue;
                }

                // 从 font.bin 查找 charCode: offset = 4 + charIndex * 8
                int charEntryOffset = 4 + charIndex * charEntrySize;
                if (charEntryOffset + 4 > fontBinData.Length)
                {
                    sb.Append('?');
                    continue;
                }

                uint charCode = BitConverter.ToUInt32(fontBinData, charEntryOffset);

                // 合法性校验：真实 charCode 在 0x20~0xFFFF（GBK 双字节 0x8140~0xFEFE）。
                // 若越界（明显是位图偏移被误当 charCode），说明传入的不是 font.bin → 降级为 hex 显示
                if (charCode > 0xFFFF)
                    return DecodeStringHex(fontIndex, strInfo);

                sb.Append(CharCodeToDisplayChar(charCode));
            }

            return sb.ToString().TrimEnd('\0');
        }

        /// <summary>
        /// 无 font.bin 时的降级解码：将每个 charIndex 显示为十六进制占位符
        /// </summary>
        private static string DecodeStringHex(byte[] fontIndex, StringInfo strInfo)
        {
            int dataOffset = strInfo.DataOffset;
            strInfo.CharIndices = new ushort[strInfo.Number];
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < strInfo.Number; i++)
            {
                ushort charIndex = BitConverter.ToUInt16(fontIndex, dataOffset + i * 2);
                strInfo.CharIndices[i] = charIndex;

                if (charIndex == 0)
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append('[').Append(charIndex.ToString("X2")).Append(']');
                }
            }

            return sb.ToString().TrimEnd('\0');
        }

        /// <summary>
        /// 使用预先构建的 CharCodeMap 解码字符串（避免重复解析 font.bin）
        /// </summary>
        /// <param name="fontIndex">resfontidx.bin 数据</param>
        /// <param name="charCodeMap">charIndex→charCode 映射表</param>
        /// <param name="strInfo">字符串信息</param>
        /// <param name="charCount">font.bin 字符总数（用于校验）</param>
        public static string DecodeString(byte[] fontIndex, uint[] charCodeMap, StringInfo strInfo, int charCount)
        {
            if (fontIndex == null || charCodeMap == null || strInfo.Number == 0)
                return string.Empty;

            int dataOffset = strInfo.DataOffset;

            if (dataOffset < 0 || dataOffset >= fontIndex.Length)
                return string.Empty;

            if (strInfo.Number > 1000)
                return string.Empty;

            if (dataOffset + strInfo.Number * 2 > fontIndex.Length)
                return string.Empty;

            strInfo.CharIndices = new ushort[strInfo.Number];
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < strInfo.Number; i++)
            {
                ushort charIndex = BitConverter.ToUInt16(fontIndex, dataOffset + i * 2);
                strInfo.CharIndices[i] = charIndex;

                if (charIndex == 0)
                {
                    sb.Append(' ');
                    continue;
                }

                if (charIndex >= charCodeMap.Length)
                {
                    sb.Append('?');
                    continue;
                }

                uint charCode = charCodeMap[charIndex];
                sb.Append(CharCodeToDisplayChar(charCode));
            }

            return sb.ToString().TrimEnd('\0');
        }

        /// <summary>
        /// 将 charCode 转换为可显示的字符（支持 GBK 双字节）
        /// </summary>
        private static string CharCodeToDisplayChar(uint charCode)
        {
            if (charCode > 0xFFFF)
                return "?";

            if (charCode >= 0x81 && charCode <= 0xFE)
            {
                // GBK 双字节字符: charCode 高字节=GBK lead byte
                byte b0 = (byte)(charCode >> 8);
                byte b1 = (byte)(charCode & 0xFF);
                if (b1 >= 0x40)
                {
                    try
                    {
                        byte[] gbkBytes = new byte[] { b0, b1 };
                        return Encoding.GetEncoding("GBK").GetString(gbkBytes);
                    }
                    catch
                    {
                        return "?";
                    }
                }
                else
                {
                    return ((char)charCode).ToString();
                }
            }
            else
            {
                return ((char)charCode).ToString();
            }
        }
    }

    /// <summary>
    /// 字体文件信息
    /// </summary>
    public class FontInfo
    {
        /// <summary>
        /// 字符总数
        /// </summary>
        public uint CharCount { get; set; }

        /// <summary>
        /// 语言数量
        /// </summary>
        public byte LanguageCount { get; set; }

        /// <summary>
        /// 无效字符宽度
        /// </summary>
        public byte InvalidCharWidth { get; set; }

        /// <summary>
        /// 语言列表
        /// </summary>
        public List<LanguageInfo> Languages { get; set; } = new List<LanguageInfo>();

        /// <summary>
        /// 所有字符的元数据
        /// </summary>
        public List<CharInfo> Characters { get; set; } = new List<CharInfo>();

        /// <summary>
        /// charCode→CharInfo 映射（通过 font.bin 构建），key=charCode, value=CharInfo
        /// </summary>
        public Dictionary<uint, CharInfo> CharCodeMap { get; set; } = new Dictionary<uint, CharInfo>();

        /// <summary>
        /// charIndex→charCode 映射数组（通过 font.bin 构建），下标即 charIndex
        /// </summary>
        public uint[] CharCodeIndexMap { get; set; } = Array.Empty<uint>();

        /// <summary>
        /// 字体数据显示名称
        /// </summary>
        public string DisplayName => $"{CharCount} chars, {LanguageCount} languages";
    }

    /// <summary>
    /// AX329x 字体文件解析器
    /// </summary>
    public static class FontInfoParser
    {
        /// <summary>
        /// 解析字体文件
        /// </summary>
        /// <param name="fontData">resfont.bin 的数据</param>
        /// <param name="fontIndex">resfontidx.bin 的数据</param>
        /// <returns>字体信息对象</returns>
        /// <exception cref="InvalidDataException">当文件格式无效时抛出</exception>
        public static FontInfo Parse(byte[] fontData, byte[] fontIndex)
        {
            return Parse(fontData, fontIndex, null);
        }

        /// <summary>
        /// 解析字体文件（高级版，同时构建 charCode 映射）
        /// </summary>
        /// <param name="fontData">resfont.bin 的数据</param>
        /// <param name="fontIndex">resfontidx.bin 的数据</param>
        /// <param name="fontBinData">font.bin 的数据（可选），用于构建 charCode→CharInfo 映射</param>
        /// <returns>字体信息对象</returns>
        public static FontInfo Parse(byte[] fontData, byte[] fontIndex, byte[]? fontBinData)
        {
            if (fontData == null || fontData.Length < 4)
                throw new InvalidDataException("Font data file too small");

            if (fontIndex == null || fontIndex.Length < 4)
                throw new InvalidDataException("Font index file too small");

            var info = new FontInfo();

            try
            {
                uint rawCharCount = BitConverter.ToUInt32(fontData, 0);

                System.Diagnostics.Debug.WriteLine($"[FontParser] Raw char count: {rawCharCount} (0x{rawCharCount:X8})");
                System.Diagnostics.Debug.WriteLine($"[FontParser] Font data size: {fontData.Length} bytes");

                info.CharCount = rawCharCount;

                if (info.CharCount == 0 || info.CharCount > 100000)
                {
                    throw new InvalidDataException($"Invalid character count: {info.CharCount}");
                }

                // resfontidx.bin 头部: [magic:2=0x584D][invalidCharWidth:1][languageCount:1]
                // 字节序检测：只做一次
                bool needsSwap = false;
                uint header = BitConverter.ToUInt32(fontIndex, 0);

                ushort magic = (ushort)(header & 0x0000FFFF);
                if (magic != 0x584D)
                {
                    // 尝试小端/大端交换
                    byte[] swappedHeader = new byte[4];
                    swappedHeader[0] = fontIndex[3];
                    swappedHeader[1] = fontIndex[2];
                    swappedHeader[2] = fontIndex[1];
                    swappedHeader[3] = fontIndex[0];
                    uint swappedValue = BitConverter.ToUInt32(swappedHeader, 0);
                    ushort swappedMagic = (ushort)(swappedValue & 0x0000FFFF);

                    if (swappedMagic == 0x584D)
                    {
                        header = swappedValue;
                        magic = swappedMagic;
                        needsSwap = true;
                    }
                    else
                    {
                        throw new InvalidDataException($"Invalid font index magic: 0x{magic:X4}");
                    }
                }

                info.InvalidCharWidth = (byte)((header >> 16) & 0xFF);
                info.LanguageCount = (byte)((header >> 24) & 0xFF);

                if (info.LanguageCount == 0)
                {
                    throw new InvalidDataException("Language count is zero");
                }

                // 语言表: 偏移8开始, 每条 [langId:4][strOff:4]
                int langTableOffset = 8;
                int langEntrySize = 8;

                for (int i = 0; i < info.LanguageCount; i++)
                {
                    int entryOffset = langTableOffset + i * langEntrySize;
                    if (entryOffset + langEntrySize > fontIndex.Length)
                        break;

                    uint langId = BitConverter.ToUInt32(fontIndex, entryOffset);
                    uint strBlockOff = BitConverter.ToUInt32(fontIndex, entryOffset + 4);

                    // 从字符串块头部推导字符串数量
                    // 块结构: [0x0000:2][blockSize:2][0:4] + string entries + string data
                    // 第一个字符串条目的 relOffset 可推算字符串数量
                    int stringCount = 0;
                    int strEntryStart = (int)strBlockOff + 8; // 跳过8字节块头
                    if (strEntryStart + 8 <= fontIndex.Length)
                    {
                        ushort firstRelOff = BitConverter.ToUInt16(fontIndex, strEntryStart + 6);
                        stringCount = (firstRelOff - 8) / 8;
                    }

                    var langInfo = new LanguageInfo
                    {
                        LanguageId = langId,
                        StringBlockOffset = strBlockOff,
                        StringCount = stringCount
                    };

                    info.Languages.Add(langInfo);
                    System.Diagnostics.Debug.WriteLine($"[FontParser] Lang[{i}] id=0x{langId:X4} strOff={strBlockOff} count={stringCount}");
                }

                // 解析字符条目 (resfont.bin: [bitmapOffset:4][width:2][height:2])
                int charEntrySize = 8;

                for (uint i = 0; i < info.CharCount; i++)
                {
                    int offset = 4 + (int)(i * charEntrySize);

                    if (offset + charEntrySize > fontData.Length)
                        break;

                    uint charOffset = BitConverter.ToUInt32(fontData, offset);
                    ushort width = BitConverter.ToUInt16(fontData, offset + 4);
                    ushort height = BitConverter.ToUInt16(fontData, offset + 6);

                    var charInfo = new CharInfo
                    {
                        Index = (int)i,
                        Width = width,
                        Height = height,
                        Offset = charOffset
                    };

                    if (charInfo.Width > 0 && charInfo.Width <= 256 &&
                        charInfo.Height > 0 && charInfo.Height <= 256)
                    {
                        info.Characters.Add(charInfo);
                    }
                }

                // 如果提供了 font.bin，构建 charCode 映射
                if (fontBinData != null && fontBinData.Length >= 4)
                {
                    BuildCharCodeMap(info, fontBinData);
                }
            }
            catch (Exception ex) when (!(ex is InvalidDataException))
            {
                throw new InvalidDataException($"Failed to parse font files: {ex.Message}", ex);
            }

            return info;
        }

        /// <summary>
        /// 从 font.bin 构建 charCode 映射到 CharInfo
        /// </summary>
        /// <param name="info">FontInfo 对象</param>
        /// <param name="fontBinData">font.bin 数据</param>
        public static void BuildCharCodeMap(FontInfo info, byte[] fontBinData)
        {
            if (fontBinData == null || fontBinData.Length < 4)
                return;

            // font.bin 结构: header(4) + entries[charCode:4][bitmapOffset:4] × N
            int charEntrySize = 8;
            // 从文件大小推断实际字符数，或使用 font.bin 的 header[0] 字段
            uint fontBinCharCount = Math.Min(
                BitConverter.ToUInt32(fontBinData, 0),
                (uint)((fontBinData.Length - 4) / charEntrySize));

            if (fontBinCharCount == 0 || fontBinCharCount > 100000)
                return;

            uint[] indexMap = new uint[fontBinCharCount];

            for (uint i = 0; i < fontBinCharCount; i++)
            {
                int entryOffset = 4 + (int)(i * charEntrySize);
                if (entryOffset + 4 > fontBinData.Length)
                    break;

                uint charCode = BitConverter.ToUInt32(fontBinData, entryOffset);
                indexMap[i] = charCode;

                // 在 Characters 中查找匹配的 CharInfo（通过 Index 字段）
                // 注：charIndex 不一定等于 i，因为 Characters 可能过滤了无效条目
                // 但我们仍为每个 charIndex 保存映射，供 StringDecoder 使用
                int charInfoIdx = info.Characters.FindIndex(c => c.Index == (int)i);
                if (charInfoIdx >= 0)
                {
                    info.Characters[charInfoIdx].CharCode = charCode;
                    info.Characters[charInfoIdx].HasCharCode = true;

                    // 以 charCode 为 key 建立映射（用于 unicode 搜索）
                    if (!info.CharCodeMap.ContainsKey(charCode))
                    {
                        info.CharCodeMap[charCode] = info.Characters[charInfoIdx];
                    }
                }
            }

            info.CharCodeIndexMap = indexMap;
            System.Diagnostics.Debug.WriteLine($"[FontParser] Built CharCodeMap: {indexMap.Length} entries, {info.CharCodeMap.Count} unique charCodes");
        }

        /// <summary>
        /// 合成字符串的完整位图数据（用于预览）
        /// </summary>
        /// <param name="fontData">resfont.bin 数据</param>
        /// <param name="fontInfo">FontInfo（含 Characters 列表）</param>
        /// <param name="strInfo">字符串信息</param>
        /// <param name="charSpacing">字符间距（像素，默认2）</param>
        /// <returns>合成位图的像素数组，null 表示失败</returns>
        public static bool[,]? ComposeStringPixels(byte[] fontData, FontInfo fontInfo, StringInfo strInfo, int charSpacing = 2)
        {
            if (fontData == null || fontInfo == null || strInfo == null || strInfo.CharIndices.Length == 0)
                return null;

            // 计算总宽度和高度
            int totalWidth = 0;
            int maxHeight = 0;
            var charBitmaps = new List<(bool[,] pixels, int width, int height)>();

            for (int i = 0; i < strInfo.CharIndices.Length; i++)
            {
                ushort charIndex = strInfo.CharIndices[i];
                if (charIndex == 0)
                {
                    // 空格：用 InvalidCharWidth 或固定宽度
                    int spaceWidth = fontInfo.InvalidCharWidth > 0 ? fontInfo.InvalidCharWidth : 8;
                    totalWidth += spaceWidth + charSpacing;
                    charBitmaps.Add((null, spaceWidth, 0));
                    continue;
                }

                // 通过 Index 查找 CharInfo
                var charInfo = fontInfo.Characters.Find(c => c.Index == charIndex);
                if (charInfo == null)
                {
                    totalWidth += fontInfo.InvalidCharWidth > 0 ? fontInfo.InvalidCharWidth + charSpacing : 10;
                    charBitmaps.Add((null, fontInfo.InvalidCharWidth > 0 ? fontInfo.InvalidCharWidth : 8, 0));
                    continue;
                }

                try
                {
                    var bitmap = ExtractCharBitmap(fontData, charInfo);
                    var pixels = BitmapToPixels(bitmap, charInfo.Width, charInfo.Height);
                    totalWidth += charInfo.Width + charSpacing;
                    if (charInfo.Height > maxHeight)
                        maxHeight = charInfo.Height;
                    charBitmaps.Add((pixels, charInfo.Width, charInfo.Height));
                }
                catch
                {
                    totalWidth += fontInfo.InvalidCharWidth > 0 ? fontInfo.InvalidCharWidth + charSpacing : 10;
                    charBitmaps.Add((null, fontInfo.InvalidCharWidth > 0 ? fontInfo.InvalidCharWidth : 8, 0));
                }
            }

            // 使用实际字符串高度（如果为0则取最大字符高度）
            if (maxHeight == 0)
                maxHeight = strInfo.Height > 0 ? strInfo.Height : 24;

            if (totalWidth <= charSpacing)
                return null;

            totalWidth -= charSpacing; // 去掉最后一个字符后面的间距

            bool[,] result = new bool[maxHeight, totalWidth];
            int xOffset = 0;

            for (int i = 0; i < charBitmaps.Count; i++)
            {
                var (pixels, width, height) = charBitmaps[i];
                if (pixels == null)
                {
                    xOffset += width + charSpacing;
                    continue;
                }

                for (int y = 0; y < height && y < maxHeight; y++)
                {
                    for (int x = 0; x < width && (xOffset + x) < totalWidth; x++)
                    {
                        result[y, xOffset + x] = pixels[y, x];
                    }
                }

                xOffset += width + charSpacing;
            }

            return result;
        }

        /// <summary>
        /// 提取单个字符的位图数据
        /// </summary>
        /// <param name="fontData">字体数据</param>
        /// <param name="charInfo">字符信息</param>
        /// <returns>位图数据（已对齐）</returns>
        public static byte[] ExtractCharBitmap(byte[] fontData, CharInfo charInfo)
        {
            if (fontData == null || charInfo == null)
                throw new ArgumentNullException();

            int dataSize = charInfo.DataSize;
            int alignedSize = charInfo.AlignedSize;

            if (charInfo.Offset + alignedSize > fontData.Length)
                throw new InvalidDataException("Character data out of bounds");

            // 复制数据并对齐到16字节
            byte[] bitmap = new byte[alignedSize];
            Array.Copy(fontData, (int)charInfo.Offset, bitmap, 0, dataSize);

            return bitmap;
        }

        /// <summary>
        /// 将位图数据转换为像素数组（用于 WPF 渲染）
        /// </summary>
        /// <param name="bitmap">位图数据（每字节8个像素，MSB优先）</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        /// <returns>像素数组（true=前景色，false=背景色）</returns>
        public static bool[,] BitmapToPixels(byte[] bitmap, int width, int height)
        {
            bool[,] pixels = new bool[height, width];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int byteIndex = (y * ((width + 7) / 8)) + (x / 8);
                    int bitIndex = 7 - (x % 8); // MSB 优先

                    if (byteIndex < bitmap.Length)
                    {
                        pixels[y, x] = ((bitmap[byteIndex] >> bitIndex) & 1) == 1;
                    }
                    else
                    {
                        pixels[y, x] = false;
                    }
                }
            }

            return pixels;
        }

        /// <summary>
        /// 验证字体文件是否有效
        /// </summary>
        public static bool IsValidFont(byte[] fontData, byte[] fontIndex)
        {
            try
            {
                Parse(fontData, fontIndex);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}