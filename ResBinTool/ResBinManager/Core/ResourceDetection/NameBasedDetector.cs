using System;
using ResBinManager.Models;

namespace ResBinManager.Core.ResourceDetection
{
    public sealed class NameBasedDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.Unknown;
        public int Order => 10;

        private readonly IResourceTypeDetector[] _magicDetectors;

        public NameBasedDetector(IResourceTypeDetector[] magicDetectors)
        {
            _magicDetectors = magicDetectors;
        }

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            if (!context.UseNameHeuristics || string.IsNullOrEmpty(context.ResourceName))
                return null;

            var name = context.ResourceName;
            var data = context.Data;
            var length = context.Length;

            // Name → type mapping ordered by specificity
            // Magic bytes are the definitive indicator for image/audio formats.
            // If magic verification fails, return null and let magic-based detectors
            // (JpegDetector, PngDetector, etc.) or structural detectors handle it.
            if (IsNameMatch(name, "_BK", "RES_FRAME", "_ICON", "MAIN_BK", "MENU_BK"))
                return VerifyWithMagic(data, length, ResourceType.Jpeg, ResourceType.Png, ResourceType.Bitmap, ResourceType.Wav, ResourceType.Mp3);

            if (IsNameMatch(name, "_PNG", "PNG_"))
                return VerifyWithMagic(data, length, ResourceType.Png);

            if (IsNameMatch(name, "_BMP", "BMP_"))
                return VerifyWithMagic(data, length, ResourceType.Bitmap);

            if (IsNameMatch(name, "_AUDIO", "MUSIC_", "_SOUND", "KEY_SOUND"))
                return VerifyWithMagic(data, length, ResourceType.Wav, ResourceType.Mp3, ResourceType.Jpeg, ResourceType.Png);

            if (IsNameMatch(name, "FONT", "_FONT"))
            {
                if (data != null && length >= 4)
                {
                    ushort magic = BitConverter.ToUInt16(data, 0);
                    if (magic == 0x584D)
                        return ResourceType.Font;
                }
                return null;
            }

            if (IsNameMatch(name, "_MP3", "MP3_") && !name.Contains("FONT"))
                return VerifyWithMagic(data, length, ResourceType.Mp3);

            if (name.Contains("PALETTE"))
                return ResourceType.Palette;

            if (name.Contains("OSD"))
                return ResourceType.OsdSource;

            if (name.Contains("UNI2OEM") || name.Contains("OEM2UNI"))
                return ResourceType.EncodingTable;

            if (IsNameMatch(name, "_MAP", "MAP_"))
                return ResourceType.GameMap;

            if (IsNameMatch(name, "_STR") || name.Contains("VERSION"))
                return ResourceType.Text;

            return null;
        }

        private static bool IsNameMatch(string name, params string[] patterns)
        {
            foreach (var p in patterns)
            {
                if (name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private ResourceType? VerifyWithMagic(byte[]? data, uint length, params ResourceType[] expectedTypes)
        {
            if (data == null || length < 3)
                return null;

            foreach (var expected in expectedTypes)
            {
                foreach (var detector in _magicDetectors)
                {
                    if (detector.ResourceType == expected)
                    {
                        var result = detector.Detect(new ResourceDetectionContext { Data = data, Length = length });
                        if (result != null)
                            return result;
                        break;
                    }
                }
            }

            return null;
        }
    }
}
