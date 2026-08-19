using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ResBinManager.Models;

namespace ResBinManager.Core
{
    public class OsdIconInfo
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public uint DataOffset { get; set; }
        public int PixelCount => Width * Height;
    }

    public class OsdSourceParser
    {
        private const int IconEntrySize = 12;
        private const byte TransparentIndex = 0xF9;

        private static readonly string[] Hm020fIconNames = {
            "iconGameSnakeWall",
            "iconMenuMusicPause",
            "iconMenuMusicPlay",
            "iconMTBattery0",
            "iconMTBattery1",
            "iconMTBattery2",
            "iconMTBattery3",
            "iconMTBattery4",
            "iconMTBattery5",
            "iconMTMicroscope",
            "iconMTNULL",
            "iconMTPause",
            "iconMTPhoto",
            "iconMTPhoto3",
            "iconMTPhotoFocusRed",
            "iconMTPhotoFocusYellow",
            "iconMTPlay",
            "iconMTRecord",
            "iconMTRecord1080P",
            "iconMTRecord720P",
            "iconMTRecording",
            "iconMTRecordVGA"
        };

        public static int DetectHeaderSize(byte[] osdData)
        {
            if (osdData.Length < IconEntrySize)
                return 0;

            uint firstDataOffset = BitConverter.ToUInt32(osdData, 8);

            if (firstDataOffset % IconEntrySize != 0)
                return 0;

            if (firstDataOffset > osdData.Length)
                return 0;

            return (int)firstDataOffset;
        }

        public static int DetectIconCount(byte[] osdData)
        {
            int headerSize = DetectHeaderSize(osdData);
            
            if (headerSize == 0)
                return 0;

            return headerSize / IconEntrySize;
        }

        public static List<OsdIconInfo> ParseHeader(byte[] osdData, string[]? customIconNames = null)
        {
            var icons = new List<OsdIconInfo>();

            int iconCount = DetectIconCount(osdData);
            if (iconCount == 0)
                return icons;

            string[] iconNames = customIconNames ?? GenerateGenericNames(iconCount);

            for (int i = 0; i < iconCount; i++)
            {
                int offset = i * IconEntrySize;
                int width = BitConverter.ToInt32(osdData, offset);
                int height = BitConverter.ToInt32(osdData, offset + 4);
                uint dataOffset = BitConverter.ToUInt32(osdData, offset + 8);

                string name = i < iconNames.Length ? iconNames[i] : $"icon_{i}";

                icons.Add(new OsdIconInfo
                {
                    Index = i,
                    Name = name,
                    Width = width,
                    Height = height,
                    DataOffset = dataOffset
                });
            }

            return icons;
        }

        public static string[] GenerateGenericNames(int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = $"icon_{i}";
            }
            return names;
        }

        public static Dictionary<int, int> GetOsdIndexFrequency(byte[] osdData, OsdIconInfo iconInfo)
        {
            var freq = new Dictionary<int, int>();
            int width = iconInfo.Width;
            int height = iconInfo.Height;
            uint dataOffset = iconInfo.DataOffset;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = osdData[dataOffset + y * width + x];
                    if (freq.ContainsKey(idx))
                        freq[idx]++;
                    else
                        freq[idx] = 1;
                }
            }
            return freq;
        }

        private static byte[] BuildPaletteMapping(List<(byte r, byte g, byte b, byte a)> sourcePalette, List<(byte r, byte g, byte b, byte a)> targetPalette)
        {
            byte[] mapping = new byte[256];

            int transparentIndex = -1;
            for (int i = 0; i < targetPalette.Count; i++)
            {
                if (targetPalette[i].a == 0 && targetPalette[i].r == 0 && targetPalette[i].g == 0 && targetPalette[i].b == 0)
                {
                    transparentIndex = i;
                    break;
                }
            }

            for (int i = 0; i < Math.Min(sourcePalette.Count, 256); i++)
            {
                var srcColor = sourcePalette[i];

                if (srcColor.a == 0)
                {
                    mapping[i] = transparentIndex >= 0 ? (byte)transparentIndex : (byte)0;
                    continue;
                }

                bool exactMatchFound = false;
                for (int j = 0; j < targetPalette.Count; j++)
                {
                    var tgtColor = targetPalette[j];
                    if (srcColor.r == tgtColor.r && srcColor.g == tgtColor.g && 
                        srcColor.b == tgtColor.b && srcColor.a == tgtColor.a)
                    {
                        mapping[i] = (byte)j;
                        exactMatchFound = true;
                        break;
                    }
                }

                if (!exactMatchFound)
                {
                    byte bestIndex = 0;
                    int bestDiff = int.MaxValue;

                    for (int j = 0; j < targetPalette.Count; j++)
                    {
                        var tgtColor = targetPalette[j];
                        int diff = Math.Abs(srcColor.r - tgtColor.r) + Math.Abs(srcColor.g - tgtColor.g) + 
                                   Math.Abs(srcColor.b - tgtColor.b) + Math.Abs(srcColor.a - tgtColor.a);
                        if (diff < bestDiff)
                        {
                            bestDiff = diff;
                            bestIndex = (byte)j;
                        }
                    }

                    mapping[i] = bestIndex;
                }
            }

            for (int i = sourcePalette.Count; i < 256; i++)
            {
                mapping[i] = 0;
            }

            return mapping;
        }

        private static byte[] BuildRgbaToPaletteLookup(List<(byte r, byte g, byte b, byte a)> palette)
        {
            byte[] lookup = new byte[palette.Count * 4];
            for (int i = 0; i < palette.Count; i++)
            {
                lookup[i * 4] = palette[i].r;
                lookup[i * 4 + 1] = palette[i].g;
                lookup[i * 4 + 2] = palette[i].b;
                lookup[i * 4 + 3] = palette[i].a;
            }
            return lookup;
        }

        private static byte FindClosestColor(byte r, byte g, byte b, byte a, 
            List<(byte r, byte g, byte b, byte a)> palette, byte[] lookupTable)
        {
            byte bestIndex = 0;
            int bestDiff = int.MaxValue;

            for (int i = 0; i < palette.Count; i++)
            {
                int diff = Math.Abs(r - lookupTable[i * 4]) + Math.Abs(g - lookupTable[i * 4 + 1]) +
                           Math.Abs(b - lookupTable[i * 4 + 2]) + Math.Abs(a - lookupTable[i * 4 + 3]);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestIndex = (byte)i;
                }
            }

            return bestIndex;
        }

        private static byte FindClosestColorWithAlpha(byte r, byte g, byte b, byte a, List<(byte r, byte g, byte b, byte a)> palette)
        {
            int transparentIndex = -1;
            for (int i = 0; i < palette.Count; i++)
            {
                if (palette[i].a == 0)
                {
                    transparentIndex = i;
                    break;
                }
            }

            if (a == 0)
            {
                return transparentIndex >= 0 ? (byte)transparentIndex : (byte)0;
            }

            byte bestIndex = 0;
            int bestDiff = int.MaxValue;

            for (int i = 0; i < palette.Count; i++)
            {
                var tgt = palette[i];
                
                int alphaDiff = Math.Abs(a - tgt.a);
                
                if (alphaDiff > 64)
                {
                    continue;
                }

                int rgbDiff = Math.Abs(r - tgt.r) + Math.Abs(g - tgt.g) + Math.Abs(b - tgt.b);
                int diff = rgbDiff + alphaDiff;

                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestIndex = (byte)i;
                }
            }

            return bestIndex;
        }

        public static bool IsValidOsdBmp(byte[] bmpData, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (bmpData.Length < 54)
            {
                errorMessage = "Invalid BMP file: file too small";
                return false;
            }

            if (bmpData[0] != 0x42 || bmpData[1] != 0x4D)
            {
                errorMessage = "Invalid BMP file: not a valid BMP format";
                return false;
            }

            int width = BitConverter.ToInt32(bmpData, 18);
            int height = BitConverter.ToInt32(bmpData, 22);
            short bitsPerPixel = BitConverter.ToInt16(bmpData, 28);

            if (bitsPerPixel != 8)
            {
                errorMessage = $"OSD icon must be 8-bit BMP format. Current format: {bitsPerPixel}-bit";
                return false;
            }

            if (width % 4 != 0)
            {
                errorMessage = $"OSD icon width must be 4-pixel aligned. Current width: {width}";
                return false;
            }

            if (width <= 0 || height <= 0)
            {
                errorMessage = $"Invalid icon size: {width} × {height}";
                return false;
            }

            return true;
        }

        public static byte[] ConvertBmpToOsdIndexData(byte[] bmpData, List<(byte r, byte g, byte b, byte a)> palette, int targetWidth, int targetHeight)
        {
            int dataOffset = BitConverter.ToInt32(bmpData, 10);
            int width = BitConverter.ToInt32(bmpData, 18);
            int height = BitConverter.ToInt32(bmpData, 22);
            short bitsPerPixel = BitConverter.ToInt16(bmpData, 28);

            byte[] indexData = new byte[targetWidth * targetHeight];

            byte[]? paletteMapping = null;
            if (bitsPerPixel == 8)
            {
                var bmpPalette = new List<(byte, byte, byte, byte)>();
                for (int i = 0; i < 256; i++)
                {
                    int palOffset = 54 + i * 4;
                    if (palOffset + 3 < bmpData.Length)
                    {
                        byte b = bmpData[palOffset];
                        byte g = bmpData[palOffset + 1];
                        byte r = bmpData[palOffset + 2];
                        byte a = bmpData[palOffset + 3];
                        bmpPalette.Add((r, g, b, a));
                    }
                    else
                    {
                        bmpPalette.Add((0, 0, 0, 0));
                    }
                }
                paletteMapping = BuildPaletteMapping(bmpPalette, palette);
            }

            for (int y = 0; y < targetHeight; y++)
            {
                for (int x = 0; x < targetWidth; x++)
                {
                    int srcX = x * width / targetWidth;
                    int srcY = y * height / targetHeight;

                    if (bitsPerPixel == 8 && paletteMapping != null)
                    {
                        int rowSize = ((width + 3) / 4) * 4;
                        int offset = dataOffset + (height - 1 - srcY) * rowSize + srcX;
                        byte bmpIndex = bmpData[offset];
                        indexData[y * targetWidth + x] = paletteMapping[bmpIndex];
                    }
                    else
                    {
                        byte r, g, b, a = 255;

                        if (bitsPerPixel == 32)
                        {
                            int rowSize = ((width * 4 + 3) / 4) * 4;
                            int offset = dataOffset + (height - 1 - srcY) * rowSize + srcX * 4;
                            b = bmpData[offset];
                            g = bmpData[offset + 1];
                            r = bmpData[offset + 2];
                            a = bmpData[offset + 3];
                        }
                        else if (bitsPerPixel == 24)
                        {
                            int rowSize = ((width * 3 + 3) / 4) * 4;
                            int offset = dataOffset + (height - 1 - srcY) * rowSize + srcX * 3;
                            b = bmpData[offset];
                            g = bmpData[offset + 1];
                            r = bmpData[offset + 2];
                        }
                        else
                        {
                            r = g = b = a = 0;
                        }

                        byte bestIndex = FindClosestColorWithAlpha(r, g, b, a, palette);
                        indexData[y * targetWidth + x] = bestIndex;
                    }
                }
            }

            return indexData;
        }

        public static Dictionary<int, int> GetBmpIndexFrequency(string bmpPath)
        {
            byte[] data = File.ReadAllBytes(bmpPath);
            int width = BitConverter.ToInt32(data, 18);
            int height = BitConverter.ToInt32(data, 22);
            int dataOffset = BitConverter.ToInt32(data, 10);

            var freq = new Dictionary<int, int>();
            for (int row = 0; row < height; row++)
            {
                int rowOffset = dataOffset + row * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = data[rowOffset + x];
                    if (freq.ContainsKey(idx))
                        freq[idx]++;
                    else
                        freq[idx] = 1;
                }
            }
            return freq;
        }

        public static Dictionary<int, int> BuildIndexMapping(Dictionary<int, int> osdFreq, Dictionary<int, int> bmpFreq)
        {
            var osdIndices = osdFreq.Keys.OrderByDescending(k => osdFreq[k]).ToList();
            var bmpIndices = bmpFreq.Keys.OrderByDescending(k => bmpFreq[k]).ToList();

            var mapping = new Dictionary<int, int>();
            for (int i = 0; i < osdIndices.Count && i < bmpIndices.Count; i++)
            {
                mapping[osdIndices[i]] = bmpIndices[i];
            }
            return mapping;
        }

        public static byte[] DecodeIconToRgba32(byte[] osdData, OsdIconInfo iconInfo, byte[] paletteData)
        {
            return DecodeIconToRgba32(osdData, iconInfo, paletteData, null, null);
        }

        public static byte[] DecodeIconToRgba32(byte[] osdData, OsdIconInfo iconInfo, byte[] paletteData, 
            Dictionary<int, int>? indexMapping = null, List<(byte r, byte g, byte b, byte a)>? targetPalette = null)
        {
            var palette = targetPalette ?? ParsePalette(paletteData);
            int width = iconInfo.Width;
            int height = iconInfo.Height;
            uint dataOffset = iconInfo.DataOffset;

            // 边界检查: 验证图标数据在 osdData 范围内
            int pixelCount = width * height;
            if (dataOffset + pixelCount > (uint)osdData.Length)
                return new byte[0];

            byte[] rgbaData = new byte[pixelCount * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int osdY = height - 1 - y;
                    int pixelIndex = osdData[dataOffset + osdY * width + x];
                    
                    if (indexMapping != null && indexMapping.ContainsKey(pixelIndex))
                    {
                        pixelIndex = indexMapping[pixelIndex];
                    }
                    
                    byte r, g, b, a;
                    
                    if (pixelIndex == TransparentIndex)
                    {
                        r = g = b = a = 0;
                    }
                    else
                    {
                        if (pixelIndex < palette.Count)
                        {
                            (r, g, b, a) = palette[pixelIndex];
                        }
                        else
                        {
                            r = g = b = 0;
                            a = 255;
                        }
                    }

                    int rgbaOffset = (y * width + x) * 4;
                    rgbaData[rgbaOffset] = b;
                    rgbaData[rgbaOffset + 1] = g;
                    rgbaData[rgbaOffset + 2] = r;
                    rgbaData[rgbaOffset + 3] = a;
                }
            }

            return rgbaData;
        }

        public static byte[] ConvertRgba32ToBmp(int width, int height, byte[] rgbaData)
        {
            using var ms = new MemoryStream();
            
            int rawDataSize = width * height * 4;
            int fileSize = 54 + rawDataSize;
            
            byte[] fileHeader = new byte[14];
            fileHeader[0] = 0x42;
            fileHeader[1] = 0x4D;
            BitConverter.GetBytes(fileSize).CopyTo(fileHeader, 2);
            BitConverter.GetBytes(54).CopyTo(fileHeader, 10);
            ms.Write(fileHeader, 0, 14);

            byte[] infoHeader = new byte[40];
            BitConverter.GetBytes(40).CopyTo(infoHeader, 0);
            BitConverter.GetBytes(width).CopyTo(infoHeader, 4);
            BitConverter.GetBytes(height).CopyTo(infoHeader, 8);
            BitConverter.GetBytes((short)1).CopyTo(infoHeader, 12);
            BitConverter.GetBytes((short)32).CopyTo(infoHeader, 14);
            BitConverter.GetBytes(2835).CopyTo(infoHeader, 24);
            BitConverter.GetBytes(2835).CopyTo(infoHeader, 28);
            ms.Write(infoHeader, 0, 40);

            ms.Write(rgbaData, 0, rawDataSize);

            return ms.ToArray();
        }

        public static byte[] ConvertRgba32ToIndexedBmp(int width, int height, byte[] rgbaData, List<(byte r, byte g, byte b, byte a)> palette)
        {
            using var ms = new MemoryStream();

            int paletteSize = Math.Min(palette.Count, 256) * 4;
            int rowSize = ((width + 3) / 4) * 4;
            int rawDataSize = rowSize * height;
            int fileSize = 54 + paletteSize + rawDataSize;

            byte[] fileHeader = new byte[14];
            fileHeader[0] = 0x42;
            fileHeader[1] = 0x4D;
            BitConverter.GetBytes(fileSize).CopyTo(fileHeader, 2);
            BitConverter.GetBytes(54 + paletteSize).CopyTo(fileHeader, 10);
            ms.Write(fileHeader, 0, 14);

            byte[] infoHeader = new byte[40];
            BitConverter.GetBytes(40).CopyTo(infoHeader, 0);
            BitConverter.GetBytes(width).CopyTo(infoHeader, 4);
            BitConverter.GetBytes(height).CopyTo(infoHeader, 8);
            BitConverter.GetBytes((short)1).CopyTo(infoHeader, 12);
            BitConverter.GetBytes((short)8).CopyTo(infoHeader, 14);
            BitConverter.GetBytes(2835).CopyTo(infoHeader, 24);
            BitConverter.GetBytes(2835).CopyTo(infoHeader, 28);
            ms.Write(infoHeader, 0, 40);

            for (int i = 0; i < Math.Min(palette.Count, 256); i++)
            {
                ms.WriteByte(palette[i].b);
                ms.WriteByte(palette[i].g);
                ms.WriteByte(palette[i].r);
                ms.WriteByte(palette[i].a);
            }

            byte[] colorLookupTable = BuildRgbaToPaletteLookup(palette);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int rgbaOffset = (y * width + x) * 4;
                    byte r = rgbaData[rgbaOffset + 2];
                    byte g = rgbaData[rgbaOffset + 1];
                    byte b = rgbaData[rgbaOffset];
                    byte a = rgbaData[rgbaOffset + 3];

                    byte bestIndex = FindClosestColor(r, g, b, a, palette, colorLookupTable);
                    ms.WriteByte(bestIndex);
                }

                int padding = rowSize - width;
                for (int p = 0; p < padding; p++)
                {
                    ms.WriteByte(0);
                }
            }

            return ms.ToArray();
        }

        public static byte[] ConvertRawIndexToIndexedBmp(int width, int height, byte[] rawIndexData, List<(byte r, byte g, byte b, byte a)> palette)
        {
            using var ms = new MemoryStream();

            int paletteSize = Math.Min(palette.Count, 256) * 4;
            int rowSize = ((width + 3) / 4) * 4;
            int rawDataSize = rowSize * height;
            int fileSize = 54 + paletteSize + rawDataSize;

            byte[] fileHeader = new byte[14];
            fileHeader[0] = 0x42;
            fileHeader[1] = 0x4D;
            BitConverter.GetBytes(fileSize).CopyTo(fileHeader, 2);
            BitConverter.GetBytes(54 + paletteSize).CopyTo(fileHeader, 10);
            ms.Write(fileHeader, 0, 14);

            byte[] infoHeader = new byte[40];
            BitConverter.GetBytes(40).CopyTo(infoHeader, 0);
            BitConverter.GetBytes(width).CopyTo(infoHeader, 4);
            BitConverter.GetBytes(height).CopyTo(infoHeader, 8);
            BitConverter.GetBytes((short)1).CopyTo(infoHeader, 12);
            BitConverter.GetBytes((short)8).CopyTo(infoHeader, 14);
            BitConverter.GetBytes(2835).CopyTo(infoHeader, 24);
            BitConverter.GetBytes(2835).CopyTo(infoHeader, 28);
            ms.Write(infoHeader, 0, 40);

            for (int i = 0; i < Math.Min(palette.Count, 256); i++)
            {
                ms.WriteByte(palette[i].b);
                ms.WriteByte(palette[i].g);
                ms.WriteByte(palette[i].r);
                ms.WriteByte(palette[i].a);
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int indexOffset = (height - 1 - y) * width + x;
                    byte pixelIndex = rawIndexData[indexOffset];
                    ms.WriteByte(pixelIndex);
                }

                int padding = rowSize - width;
                for (int p = 0; p < padding; p++)
                {
                    ms.WriteByte(0);
                }
            }

            return ms.ToArray();
        }

        private static byte ConvertRgb5ToRgb8(int rgb5)
        {
            return (byte)((rgb5 * 255 + 15) / 31);
        }

        private static byte ConvertRgb6ToRgb8(int rgb6)
        {
            return (byte)((rgb6 * 255 + 31) / 63);
        }

        public static List<(byte r, byte g, byte b, byte a)> ParsePalette(byte[] paletteData)
        {
            var palette = new List<(byte, byte, byte, byte)>();

            int colorCount = Math.Min(paletteData.Length / 4, 256);

            for (int i = 0; i < colorCount; i++)
            {
                int offset = i * 4;
                uint color = BitConverter.ToUInt32(paletteData, offset);
                ushort rgb565Val = (ushort)(color & 0xFFFF);
                
                byte r = ConvertRgb5ToRgb8((rgb565Val >> 11) & 0x1F);
                byte g = ConvertRgb6ToRgb8((rgb565Val >> 5) & 0x3F);
                byte b = ConvertRgb5ToRgb8(rgb565Val & 0x1F);
                
                // 参考 Platte_Tiga2Vison string2pixel() 格式:
                // Byte2 bits[4:0] = 5位Alpha, Byte3 = 0x00 (未使用)
                byte tagByte = (byte)((color >> 16) & 0xFF);
                byte alpha5 = (byte)(tagByte & 0x1F);
                byte a = ConvertRgb5ToRgb8(alpha5);

                if (i >= 0xF0 && PaletteColor.StandardColorValues.TryGetValue(i, out var argb))
                {
                    a = (byte)((argb >> 24) & 0xFF);
                    r = (byte)((argb >> 16) & 0xFF);
                    g = (byte)((argb >> 8) & 0xFF);
                    b = (byte)(argb & 0xFF);
                }
                
                palette.Add((r, g, b, a));
            }

            return palette;
        }

        public static List<(byte r, byte g, byte b, byte a)> ExtractPaletteFromBmp(string bmpPath)
        {
            byte[] data = File.ReadAllBytes(bmpPath);
            var palette = new List<(byte, byte, byte, byte)>();

            for (int i = 0; i < 256; i++)
            {
                int palOffset = 54 + i * 4;
                if (palOffset + 3 < data.Length)
                {
                    byte b = data[palOffset];
                    byte g = data[palOffset + 1];
                    byte r = data[palOffset + 2];
                    byte a = data[palOffset + 3];
                    palette.Add((r, g, b, a));
                }
                else
                {
                    palette.Add((0, 0, 0, 0));
                }
            }

            return palette;
        }

        public static void ExportOsdIcons(byte[] osdData, byte[] paletteData, string outputDirectory, string[]? customIconNames = null)
        {
            ExportOsdIcons(osdData, paletteData, outputDirectory, null, customIconNames);
        }

        public static void ExportOsdIcons(byte[] osdData, byte[] paletteData, string outputDirectory, 
            string? originalIconDirectory = null, string[]? customIconNames = null)
        {
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var icons = ParseHeader(osdData, customIconNames);

            foreach (var icon in icons)
            {
                if (icon.Width <= 0 || icon.Height <= 0)
                    continue;

                Dictionary<int, int>? indexMapping = null;
                List<(byte r, byte g, byte b, byte a)>? targetPalette = null;

                if (!string.IsNullOrEmpty(originalIconDirectory))
                {
                    string origBmpPath = Path.Combine(originalIconDirectory, $"{icon.Name}.bmp");
                    if (File.Exists(origBmpPath))
                    {
                        var osdFreq = GetOsdIndexFrequency(osdData, icon);
                        var bmpFreq = GetBmpIndexFrequency(origBmpPath);
                        indexMapping = BuildIndexMapping(osdFreq, bmpFreq);
                        targetPalette = ExtractPaletteFromBmp(origBmpPath);
                    }
                }

                byte[] rgbaData = DecodeIconToRgba32(osdData, icon, paletteData, indexMapping, targetPalette);
                
                var palette = targetPalette ?? ParsePalette(paletteData);
                byte[] indexedBmpData = ConvertRgba32ToIndexedBmp(icon.Width, icon.Height, rgbaData, palette);

                string outputPath = Path.Combine(outputDirectory, $"{icon.Name}.bmp");
                File.WriteAllBytes(outputPath, indexedBmpData);
            }
        }

        public static bool ValidateOsdSource(byte[] data)
        {
            int iconCount = DetectIconCount(data);
            if (iconCount == 0 || iconCount > 256)
                return false;

            int headerSize = iconCount * IconEntrySize;

            for (int i = 0; i < iconCount; i++)
            {
                int offset = i * IconEntrySize;
                int width = BitConverter.ToInt32(data, offset);
                int height = BitConverter.ToInt32(data, offset + 4);
                uint dataOffset = BitConverter.ToUInt32(data, offset + 8);

                if (width <= 0 || width > 1024 || height <= 0 || height > 1024)
                    return false;

                if (dataOffset < headerSize)
                    return false;

                uint expectedEndOffset = dataOffset + (uint)(width * height);
                if (expectedEndOffset > data.Length)
                    return false;
            }

            return true;
        }

        public static string GetOsdSourceInfo(byte[] data)
        {
            if (!ValidateOsdSource(data))
                return "Invalid OSD source data";

            var icons = ParseHeader(data);
            int totalPixels = icons.Sum(i => i.PixelCount);
            int headerSize = DetectHeaderSize(data);

            return $"OSD Source: {icons.Count} icons, header={headerSize} bytes, {totalPixels} total pixels, {data.Length} bytes total";
        }

        public static string[] GetHm020fIconNames()
        {
            return (string[])Hm020fIconNames.Clone();
        }

        public static OsdInfo ParseOsdInfo(byte[] osdData)
        {
            return ParseOsdInfo(osdData, null, null);
        }

        public static OsdInfo ParseOsdInfo(byte[] osdData, byte[]? paletteData = null, string? originalIconDirectory = null)
        {
            var info = new OsdInfo();

            int iconCount = DetectIconCount(osdData);
            int headerSize = DetectHeaderSize(osdData);

            info.IconCount = iconCount;
            info.HeaderSize = headerSize;
            info.TotalSize = osdData.Length;

            var icons = ParseHeader(osdData);
            info.TotalPixels = icons.Sum(i => i.PixelCount);

            byte[] paletteForDecoding = paletteData ?? osdData;
            var defaultPalette = ParsePalette(paletteForDecoding);

            foreach (var icon in icons)
            {
                if (icon.Width <= 0 || icon.Height <= 0)
                    continue;

                int pixelCount = icon.Width * icon.Height;

                // 边界检查: 验证图标数据在 osdData 范围内
                if (icon.DataOffset + pixelCount > (uint)osdData.Length)
                    continue;

                byte[] rawIndexData = new byte[pixelCount];
                Array.Copy(osdData, (int)icon.DataOffset, rawIndexData, 0, pixelCount);

                Dictionary<int, int>? indexMapping = null;
                List<(byte r, byte g, byte b, byte a)>? targetPalette = null;

                if (!string.IsNullOrEmpty(originalIconDirectory))
                {
                    string origBmpPath = Path.Combine(originalIconDirectory, $"{icon.Name}.bmp");
                    if (File.Exists(origBmpPath))
                    {
                        var osdFreq = GetOsdIndexFrequency(osdData, icon);
                        var bmpFreq = GetBmpIndexFrequency(origBmpPath);
                        indexMapping = BuildIndexMapping(osdFreq, bmpFreq);
                        targetPalette = ExtractPaletteFromBmp(origBmpPath);
                    }
                }

                byte[] indexedBmpData;

                if (indexMapping == null && targetPalette == null)
                {
                    // 直接索引路径: 无需RGBA往返转换, 保留原始调色板索引
                    indexedBmpData = ConvertRawIndexToIndexedBmp(icon.Width, icon.Height, rawIndexData, defaultPalette);
                }
                else
                {
                    // 需要调色板重映射: 通过RGBA中间格式转换
                    byte[] rgbaData = DecodeIconToRgba32(osdData, icon, paletteForDecoding, indexMapping, targetPalette);
                    var palette = targetPalette ?? defaultPalette;
                    indexedBmpData = ConvertRgba32ToIndexedBmp(icon.Width, icon.Height, rgbaData, palette);
                }

                info.Icons.Add(new OsdIconPreviewItem
                {
                    Index = icon.Index,
                    Name = icon.Name,
                    Width = icon.Width,
                    Height = icon.Height,
                    DataOffset = icon.DataOffset,
                    PixelCount = pixelCount,
                    DataSize = pixelCount,
                    IconData = indexedBmpData,
                    RawIndexData = rawIndexData
                });
            }

            return info;
        }
    }
}