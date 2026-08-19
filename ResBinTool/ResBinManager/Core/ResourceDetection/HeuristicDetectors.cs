using ResBinManager.Models;

namespace ResBinManager.Core.ResourceDetection
{
    public sealed class EncodingTableDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.EncodingTable;
        public int Order => 60;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            if (context.Length >= 85000 && context.Length <= 90000)
                return ResourceType.EncodingTable;
            return null;
        }
    }

    public sealed class OsdSourceSizeDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.OsdSource;
        public int Order => 61;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            if (context.Length >= 90000 && context.Length <= 100000)
                return ResourceType.OsdSource;
            return null;
        }
    }

    public sealed class GameMapDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.GameMap;
        public int Order => 62;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            if (context.Length >= 200 && context.Length < 90000)
            {
                if (context.ResourceIndex < 5)
                    return ResourceType.Font;
                return ResourceType.GameMap;
            }
            return null;
        }
    }

    public sealed class IconSelectionDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.IconSelection;
        public int Order => 63;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            if (context.Length >= 10000 && context.Length < 100000)
                return ResourceType.IconSelection;
            return null;
        }
    }

    public sealed class BinaryFallbackDetector : IResourceTypeDetector
    {
        public ResourceType ResourceType => ResourceType.Binary;
        public int Order => 99;

        public ResourceType? Detect(ResourceDetectionContext context)
        {
            return ResourceType.Binary;
        }
    }
}
