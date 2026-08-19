using ResBinManager.Models;

namespace ResBinManager.Core.ResourceDetection
{
    public sealed class JpegDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.Jpeg;
        public int Order => 20;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            var data = context.Data;
            if (data == null || data.Length < 3)
                return null;
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return ResourceType.Jpeg;
            return null;
        }
    }

    public sealed class PngDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.Png;
        public int Order => 21;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            var data = context.Data;
            if (data == null || data.Length < 8)
                return null;
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
                return ResourceType.Png;
            return null;
        }
    }

    public sealed class BmpDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.Bitmap;
        public int Order => 22;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            var data = context.Data;
            if (data == null || data.Length < 2)
                return null;
            if (data[0] == 'B' && data[1] == 'M')
                return ResourceType.Bitmap;
            return null;
        }
    }

    public sealed class WavDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.Wav;
        public int Order => 23;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            var data = context.Data;
            if (data == null || data.Length < 12)
                return null;
            if (data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
                data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E')
                return ResourceType.Wav;
            return null;
        }
    }

    public sealed class Mp3Detector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.Mp3;
        public int Order => 24;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            var data = context.Data;
            if (data == null || data.Length < 2)
                return null;

            // ID3v2 tag
            if (data.Length >= 10 && data[0] == 0x49 && data[1] == 0x44 && data[2] == 0x33)
            {
                byte majorVersion = data[3];
                if (majorVersion >= 2 && majorVersion <= 4)
                {
                    bool validSyncsafe = (data[6] & 0x80) == 0 && (data[7] & 0x80) == 0 &&
                                         (data[8] & 0x80) == 0 && (data[9] & 0x80) == 0;
                    if (validSyncsafe)
                        return ResourceType.Mp3;
                }
            }

            // APEv2 tag
            if (data.Length >= 8 &&
                data[0] == 'A' && data[1] == 'P' && data[2] == 'E' &&
                data[3] == 'T' && data[4] == 'A' && data[5] == 'G' &&
                data[6] == 'E' && data[7] == 'X')
                return ResourceType.Mp3;

            return null;
        }
    }
}
