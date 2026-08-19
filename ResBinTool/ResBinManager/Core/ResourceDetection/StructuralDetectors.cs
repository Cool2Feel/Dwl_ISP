using System;
using ResBinManager.Models;

namespace ResBinManager.Core.ResourceDetection
{
    public sealed class PaletteDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.Palette;
        public int Order => 40;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            if (context.Length == PaletteParser.PaletteSize && context.Data != null && PaletteParser.ValidatePalette(context.Data))
                return ResourceType.Palette;
            return null;
        }
    }

    public sealed class OsdSourceDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.OsdSource;
        public int Order => 41;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            if (context.Data != null && OsdSourceParser.ValidateOsdSource(context.Data))
                return ResourceType.OsdSource;
            return null;
        }
    }

    public sealed class FontDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.Font;
        public int Order => 42;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            if (context.Data == null || context.Length == 0)
                return null;

            try
            {
                var data = context.Data;
                var length = context.Length;

                // Method 1: resfontidx.bin magic (0x584D = "MX")
                if (length >= 2)
                {
                    ushort magic = BitConverter.ToUInt16(data, 0);
                    if (magic == 0x584D)
                        return ResourceType.Font;
                }

                // Method 2: Adjacent resource has resfontidx.bin magic
                if (context.AdjacentData != null && context.AdjacentData.Length >= 2)
                {
                    ushort adjacentMagic = BitConverter.ToUInt16(context.AdjacentData, 0);
                    if (adjacentMagic == 0x584D)
                        return ResourceType.Font;
                }

                // Method 3: Name contains FONT
                if (!string.IsNullOrEmpty(context.ResourceName))
                {
                    var name = context.ResourceName;
                    if (name.IndexOf("FONT", StringComparison.OrdinalIgnoreCase) >= 0)
                        return ResourceType.Font;
                }

                // Method 4: Character count heuristic
                if (length > 1024 && length >= 4)
                {
                    uint charCount = BitConverter.ToUInt32(data, 0);
                    if (charCount >= 4096 && charCount <= 1007616)
                        return ResourceType.Font;
                }
            }
            catch
            {
                // Ignore
            }

            return null;
        }
    }

    public sealed class TextDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.Text;
        public int Order => 43;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            var data = context.Data;
            var length = context.Length;
            if (data == null || length == 0)
                return null;

            int printableCount = 0;
            int zeroCount = 0;
            int checkLength = (int)Math.Min(length, 200);

            for (int i = 0; i < checkLength; i++)
            {
                byte b = data[i];
                if (b == 0)
                    zeroCount++;
                else if (b >= 0x20 && b <= 0x7E)
                    printableCount++;
            }

            double printableRatio = (double)printableCount / checkLength;
            double zeroRatio = (double)zeroCount / checkLength;

            if (length <= 200 && printableRatio > 0.6 && zeroRatio < 0.3)
                return ResourceType.Text;

            return null;
        }
    }
}
