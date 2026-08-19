using System;
using System.Collections.Generic;
using System.Text;
using ResBinManager.Models;

namespace ResBinManager.Core
{
    public class ResourceValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<string> Warnings { get; set; } = new List<string>();
        public string Info { get; set; } = string.Empty;
        public object? ExtendedData { get; set; }

        public string GetDisplayText()
        {
            var sb = new StringBuilder();
            
            if (IsValid)
            {
                sb.AppendLine("✓ Valid Resource");
                if (!string.IsNullOrEmpty(Info))
                    sb.AppendLine(Info);
                
                if (Warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠ Warnings:");
                    foreach (var warning in Warnings)
                    {
                        sb.AppendLine($"  - {warning}");
                    }
                }
            }
            else
            {
                sb.AppendLine("✗ Invalid Resource");
                sb.AppendLine($"Error: {ErrorMessage}");
            }

            return sb.ToString();
        }
    }

    public interface IResourceValidator
    {
        ResourceValidationResult Validate(byte[] data, uint expectedSize = 0);
        ResourceValidationResult Validate(byte[] data, uint expectedSize, int expectedWidth, int expectedHeight);
        ResourceType ResourceType { get; }
    }

    public static class ResourceValidatorFactory
    {
        private static readonly Dictionary<ResourceType, IResourceValidator> _validators = new();

        static ResourceValidatorFactory()
        {
            _validators[ResourceType.Wav] = new WavResourceValidator();
            _validators[ResourceType.Palette] = new PaletteResourceValidator();
            _validators[ResourceType.GameMap] = new GameMapResourceValidator();
            _validators[ResourceType.EncodingTable] = new EncodingTableResourceValidator();
            _validators[ResourceType.Font] = new FontResourceValidator();
            _validators[ResourceType.Bitmap] = new BitmapResourceValidator();
            _validators[ResourceType.Jpeg] = new ImageResourceValidator();
            _validators[ResourceType.Png] = new ImageResourceValidator();
            _validators[ResourceType.OsdSource] = new OsdSourceResourceValidator();
        }

        public static IResourceValidator? GetValidator(ResourceType type)
        {
            _validators.TryGetValue(type, out var validator);
            return validator;
        }

        public static ResourceValidationResult Validate(ResourceType type, byte[] data, uint expectedSize = 0)
        {
            if (GetValidator(type) is { } validator)
            {
                return validator.Validate(data, expectedSize);
            }
            
            var result = new ResourceValidationResult();
            result.IsValid = true;
            result.Info = $"Generic validation passed: {data.Length} bytes";
            return result;
        }

        public static ResourceValidationResult Validate(ResourceType type, byte[] data, uint expectedSize, int expectedWidth, int expectedHeight)
        {
            if (GetValidator(type) is { } validator)
            {
                return validator.Validate(data, expectedSize, expectedWidth, expectedHeight);
            }
            
            return Validate(type, data, expectedSize);
        }
    }

    public class WavResourceValidator : IResourceValidator
    {
        public ResourceType ResourceType => ResourceType.Wav;

        public ResourceValidationResult Validate(byte[] data, uint expectedSize = 0)
        {
            var result = new ResourceValidationResult();

            if (data == null || data.Length == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "File is empty";
                return result;
            }

            if (data.Length < 44)
            {
                result.IsValid = false;
                result.ErrorMessage = $"File too small ({data.Length} bytes). Minimum WAV size is 44 bytes.";
                return result;
            }

            string riff = Encoding.ASCII.GetString(data, 0, 4);
            if (riff != "RIFF")
            {
                result.IsValid = false;
                result.ErrorMessage = $"Invalid file format. Expected 'RIFF', got '{riff}'";
                return result;
            }

            string wave = Encoding.ASCII.GetString(data, 8, 4);
            if (wave != "WAVE")
            {
                result.IsValid = false;
                result.ErrorMessage = $"Not a WAV file. Expected 'WAVE', got '{wave}'";
                return result;
            }

            try
            {
                var info = WavInfoParser.Parse(data);
                result.ExtendedData = info;
                result.IsValid = true;
                result.Info = $"Format: {info.Format}, {info.SampleRate} Hz, {info.ChannelsDisplay}, {info.BitsPerSample}-bit, {info.DurationDisplay}";

                if (info.SampleRate < 8000)
                    result.Warnings.Add($"Very low sample rate ({info.SampleRate} Hz)");
                else if (info.SampleRate > 48000)
                    result.Warnings.Add($"High sample rate ({info.SampleRate} Hz). Ensure device supports it");

                if (info.BitsPerSample == 8)
                    result.Warnings.Add("8-bit audio has limited dynamic range");

                if (info.Channels > 2)
                    result.Warnings.Add($"Multi-channel audio ({info.Channels} channels)");

                if (info.DataSize > 100 * 1024)
                    result.Warnings.Add($"Large file ({info.DataSize / 1024} KB). May increase firmware size");
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Failed to parse WAV header: {ex.Message}";
            }

            return result;
        }

        public ResourceValidationResult Validate(byte[] data, uint expectedSize, int expectedWidth, int expectedHeight)
        {
            return Validate(data, expectedSize);
        }
    }

    public class PaletteResourceValidator : IResourceValidator
    {
        public ResourceType ResourceType => ResourceType.Palette;

        public ResourceValidationResult Validate(byte[] data, uint expectedSize = 0)
        {
            var result = new ResourceValidationResult();

            int expectedLength = expectedSize > 0 ? (int)expectedSize : 1024;

            if (data.Length != expectedLength)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Invalid palette size: expected {expectedLength} bytes, got {data.Length}";
                return result;
            }

            int nonZeroColors = 0;
            int validRgb565Colors = 0;

            for (int i = 0; i < data.Length; i += 4)
            {
                uint color = BitConverter.ToUInt32(data, i);
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

            if (nonZeroColors == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "Palette contains all zero colors, likely invalid";
                return result;
            }

            int colorCount = data.Length / 4;
            double validRatio = (double)validRgb565Colors / colorCount * 100;

            result.IsValid = true;
            result.Info = $"RGB565 palette: {colorCount} colors ({nonZeroColors} non-zero), {validRatio:F1}% valid RGB565 values";

            if (validRatio < 90)
            {
                result.Warnings.Add($"Only {validRatio:F0}% of entries appear to be valid RGB565 values");
            }

            if (expectedSize > 0 && data.Length != expectedSize)
            {
                result.Warnings.Add($"Size mismatch: {data.Length} bytes vs expected {expectedSize} bytes");
            }

            return result;
        }

        public ResourceValidationResult Validate(byte[] data, uint expectedSize, int expectedWidth, int expectedHeight)
        {
            return Validate(data, expectedSize);
        }

        private bool IsValidRgb565(ushort rgb565)
        {
            int r = (rgb565 >> 11) & 0x1F;
            int g = (rgb565 >> 5) & 0x3F;
            int b = rgb565 & 0x1F;

            return r <= 31 && g <= 63 && b <= 31;
        }
    }

    public class GameMapResourceValidator : IResourceValidator
    {
        public ResourceType ResourceType => ResourceType.GameMap;

        public ResourceValidationResult Validate(byte[] data, uint expectedSize = 0)
        {
            var result = new ResourceValidationResult();

            if (data == null || data.Length == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "Game map data is empty";
                return result;
            }

            if (data.Length % 2 != 0)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Game map size ({data.Length} bytes) must be even";
                return result;
            }

            int tileCount = data.Length / 2;
            result.IsValid = true;
            result.Info = $"Game map: {tileCount} tiles, {data.Length} bytes";

            return result;
        }

        public ResourceValidationResult Validate(byte[] data, uint expectedSize, int expectedWidth, int expectedHeight)
        {
            return Validate(data, expectedSize);
        }
    }

    public class EncodingTableResourceValidator : IResourceValidator
    {
        public ResourceType ResourceType => ResourceType.EncodingTable;

        public ResourceValidationResult Validate(byte[] data, uint expectedSize = 0)
        {
            var result = new ResourceValidationResult();

            if (data == null || data.Length == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "Encoding table data is empty";
                return result;
            }

            if (data.Length % 4 != 0)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Encoding table size ({data.Length} bytes) must be divisible by 4";
                return result;
            }

            int entryCount = data.Length / 4;
            result.IsValid = true;
            result.Info = $"Encoding table: {entryCount} entries, {data.Length} bytes";

            return result;
        }

        public ResourceValidationResult Validate(byte[] data, uint expectedSize, int expectedWidth, int expectedHeight)
        {
            return Validate(data, expectedSize);
        }
    }

    public class FontResourceValidator : IResourceValidator
    {
        public ResourceType ResourceType => ResourceType.Font;

        public ResourceValidationResult Validate(byte[] data, uint expectedSize = 0)
        {
            var result = new ResourceValidationResult();

            if (data == null || data.Length == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "Font data is empty";
                return result;
            }

            result.IsValid = true;
            result.Info = $"Font data: {data.Length} bytes";

            return result;
        }

        public ResourceValidationResult Validate(byte[] data, uint expectedSize, int expectedWidth, int expectedHeight)
        {
            return Validate(data, expectedSize);
        }
    }

    public class BitmapResourceValidator : IResourceValidator
    {
        public ResourceType ResourceType => ResourceType.Bitmap;

        public ResourceValidationResult Validate(byte[] data, uint expectedSize = 0)
        {
            return Validate(data, expectedSize, 0, 0);
        }

        public ResourceValidationResult Validate(byte[] data, uint expectedSize, int expectedWidth, int expectedHeight)
        {
            var result = new ResourceValidationResult();

            if (data == null || data.Length == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "Bitmap data is empty";
                return result;
            }

            if (data.Length < 54)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Bitmap too small ({data.Length} bytes). Minimum BMP header is 54 bytes";
                return result;
            }

            string magic = Encoding.ASCII.GetString(data, 0, 2);
            if (magic != "BM")
            {
                result.IsValid = false;
                result.ErrorMessage = $"Invalid BMP magic: expected 'BM', got '{magic}'";
                return result;
            }

            int width = BitConverter.ToInt32(data, 18);
            int height = Math.Abs(BitConverter.ToInt32(data, 22));

            if (width <= 0 || height <= 0)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Invalid bitmap dimensions: {width} × {height}";
                return result;
            }

            result.IsValid = true;
            result.Info = $"Valid BMP file: {width} × {height}, {data.Length} bytes";

            if (expectedWidth > 0 && expectedHeight > 0)
            {
                if (width != expectedWidth || height != expectedHeight)
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"Resolution mismatch: expected {expectedWidth} × {expectedHeight}, got {width} × {height}";
                }
            }

            return result;
        }
    }

    public class ImageResourceValidator : IResourceValidator
    {
        public ResourceType ResourceType => ResourceType.Jpeg;

        public ResourceValidationResult Validate(byte[] data, uint expectedSize = 0)
        {
            return Validate(data, expectedSize, 0, 0);
        }

        public ResourceValidationResult Validate(byte[] data, uint expectedSize, int expectedWidth, int expectedHeight)
        {
            var result = new ResourceValidationResult();

            if (data == null || data.Length == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "Image data is empty";
                return result;
            }

            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
            {
                (int width, int height) = ParseJpegResolution(data);
                if (width > 0 && height > 0)
                {
                    result.IsValid = true;
                    result.Info = $"Valid JPEG: {width} × {height}, {data.Length} bytes";

                    if (expectedWidth > 0 && expectedHeight > 0)
                    {
                        if (width != expectedWidth || height != expectedHeight)
                        {
                            result.IsValid = false;
                            result.ErrorMessage = $"Resolution mismatch: expected {expectedWidth} × {expectedHeight}, got {width} × {height}";
                        }
                    }
                }
                else
                {
                    result.IsValid = true;
                    result.Info = $"Valid JPEG: {data.Length} bytes (resolution unknown)";
                }
                return result;
            }

            if (data.Length >= 8 && 
                data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            {
                (int width, int height) = ParsePngResolution(data);
                if (width > 0 && height > 0)
                {
                    result.IsValid = true;
                    result.Info = $"Valid PNG: {width} × {height}, {data.Length} bytes";

                    if (expectedWidth > 0 && expectedHeight > 0)
                    {
                        if (width != expectedWidth || height != expectedHeight)
                        {
                            result.IsValid = false;
                            result.ErrorMessage = $"Resolution mismatch: expected {expectedWidth} × {expectedHeight}, got {width} × {height}";
                        }
                    }
                }
                else
                {
                    result.IsValid = true;
                    result.Info = $"Valid PNG: {data.Length} bytes (resolution unknown)";
                }
                return result;
            }

            result.IsValid = false;
            result.ErrorMessage = "Invalid image format. Expected JPEG or PNG";
            return result;
        }

        private (int width, int height) ParseJpegResolution(byte[] data)
        {
            try
            {
                int pos = 2;
                while (pos + 4 < data.Length)
                {
                    if (data[pos] == 0xFF && (data[pos + 1] >= 0xC0 && data[pos + 1] <= 0xC3))
                    {
                        int length = (data[pos + 2] << 8) | data[pos + 3];
                        if (pos + length <= data.Length)
                        {
                            int height = (data[pos + 5] << 8) | data[pos + 6];
                            int width = (data[pos + 7] << 8) | data[pos + 8];
                            return (width, height);
                        }
                    }
                    int segmentLength = (data[pos + 2] << 8) | data[pos + 3];
                    pos += segmentLength + 2;
                }
            }
            catch { }
            return (0, 0);
        }

        private (int width, int height) ParsePngResolution(byte[] data)
        {
            try
            {
                if (data.Length >= 24)
                {
                    string ihdr = Encoding.ASCII.GetString(data, 12, 4);
                    if (ihdr == "IHDR")
                    {
                        int width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
                        int height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
                        return (width, height);
                    }
                }
            }
            catch { }
            return (0, 0);
        }
    }

    public class OsdSourceResourceValidator : IResourceValidator
    {
        private const int IconEntrySize = 12;

        public ResourceType ResourceType => ResourceType.OsdSource;

        public ResourceValidationResult Validate(byte[] data, uint expectedSize = 0)
        {
            return Validate(data, expectedSize, 0, 0);
        }

        public ResourceValidationResult Validate(byte[] data, uint expectedSize, int expectedWidth, int expectedHeight)
        {
            var result = new ResourceValidationResult();

            if (data == null || data.Length == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "OSD source data is empty";
                return result;
            }

            if (data.Length < IconEntrySize)
            {
                result.IsValid = false;
                result.ErrorMessage = $"OSD source too small ({data.Length} bytes). Minimum size is {IconEntrySize} bytes for one icon entry";
                return result;
            }

            uint firstDataOffset = BitConverter.ToUInt32(data, 8);

            if (firstDataOffset % IconEntrySize != 0)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Invalid OSD header: first data offset ({firstDataOffset}) is not a multiple of {IconEntrySize}";
                return result;
            }

            if (firstDataOffset > data.Length)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Invalid OSD header: first data offset ({firstDataOffset}) exceeds data length ({data.Length})";
                return result;
            }

            int headerSize = (int)firstDataOffset;
            int iconCount = headerSize / IconEntrySize;

            if (iconCount == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "OSD source contains no icons";
                return result;
            }

            int totalPixelDataSize = 0;
            int maxIconIndex = -1;

            for (int i = 0; i < iconCount; i++)
            {
                int offset = i * IconEntrySize;
                int width = BitConverter.ToInt32(data, offset);
                int height = BitConverter.ToInt32(data, offset + 4);
                uint dataOffset = BitConverter.ToUInt32(data, offset + 8);

                if (width <= 0 || height <= 0)
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"Icon {i} has invalid dimensions: {width} × {height}";
                    return result;
                }

                if (width % 4 != 0)
                {
                    result.Warnings.Add($"Icon {i}: width ({width}) is not 4-pixel aligned");
                }

                if (dataOffset != headerSize + totalPixelDataSize)
                {
                    result.Warnings.Add($"Icon {i}: data offset ({dataOffset}) is not contiguous with previous icons");
                }

                if (dataOffset + (long)width * height > data.Length)
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"Icon {i}: pixel data exceeds bounds (offset={dataOffset}, size={width * height}, data length={data.Length})";
                    return result;
                }

                totalPixelDataSize += width * height;

                int pixelDataStart = (int)dataOffset;
                for (int p = pixelDataStart; p < pixelDataStart + width * height; p++)
                {
                    byte pixelIndex = data[p];
                    if (pixelIndex > 255)
                    {
                        result.Warnings.Add($"Icon {i}: contains pixel index > 255 at offset {p}");
                        break;
                    }
                }

                maxIconIndex = i;
            }

            int expectedDataSize = headerSize + totalPixelDataSize;
            if (data.Length > expectedDataSize)
            {
                result.Warnings.Add($"Extra data at end: {data.Length - expectedDataSize} bytes");
            }
            else if (data.Length < expectedDataSize)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Data truncated: expected {expectedDataSize} bytes, got {data.Length} bytes";
                return result;
            }

            result.IsValid = true;
            result.Info = $"OSD source: {iconCount} icons, {totalPixelDataSize} pixels, {data.Length} bytes total";

            if (iconCount > 0)
            {
                int firstWidth = BitConverter.ToInt32(data, 0);
                int firstHeight = BitConverter.ToInt32(data, 4);
                int lastWidth = BitConverter.ToInt32(data, maxIconIndex * IconEntrySize);
                int lastHeight = BitConverter.ToInt32(data, maxIconIndex * IconEntrySize + 4);
                result.Info += $" (first: {firstWidth}×{firstHeight}, last: {lastWidth}×{lastHeight})";
            }

            if (expectedSize > 0 && data.Length != expectedSize)
            {
                result.Warnings.Add($"Size mismatch: {data.Length} bytes vs expected {expectedSize} bytes");
            }

            return result;
        }
    }
}