using ResBinManager.Models;

namespace ResBinManager.Core.ResourceDetection
{
    public interface IResourceTypeDetector
    {
        ResourceType ResourceType { get; }
        int Order { get; }
        ResourceType? Detect(ResourceDetectionContext context);
    }
}
