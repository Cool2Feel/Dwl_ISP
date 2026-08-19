using System.Collections.Generic;
using System.Linq;
using ResBinManager.Models;

namespace ResBinManager.Core.ResourceDetection
{
    public class ResourceTypeDetectorOrchestrator
    {
        private readonly List<IResourceTypeDetector> _detectors;

        public ResourceTypeDetectorOrchestrator()
        {
            var magicDetectors = new IResourceTypeDetector[]
            {
                new JpegDetector(),
                new PngDetector(),
                new BmpDetector(),
                new WavDetector(),
                new Mp3Detector(),
            };

            _detectors = new List<IResourceTypeDetector>
            {
                new NameBasedDetector(magicDetectors),
            };
            _detectors.AddRange(magicDetectors);
            _detectors.AddRange(new IResourceTypeDetector[]
            {
                new PaletteDetector(),
                new OsdSourceDetector(),
                new FontDetector(),
                new TextDetector(),
                new EncodingTableDetector(),
                new OsdSourceSizeDetector(),
                new GameMapDetector(),
                new IconSelectionDetector(),
                new BinaryFallbackDetector(),
            });
        }

        public ResourceType Detect(ResourceDetectionContext context)
        {
            var ordered = _detectors.OrderBy(d => d.Order);
            foreach (var detector in ordered)
            {
                var result = detector.Detect(context);
                if (result.HasValue)
                    return result.Value;
            }
            return ResourceType.Binary;
        }

        public ResourceType DetectByMagic(byte[]? data, uint length)
        {
            var context = new ResourceDetectionContext
            {
                Data = data,
                Length = length,
                UseNameHeuristics = false,
            };
            return Detect(context);
        }

        public ResourceType DetectByName(string name, byte[]? data, uint length,
            int resourceIndex = -1, byte[]? adjacentData = null)
        {
            var context = new ResourceDetectionContext
            {
                Data = data,
                Length = length,
                ResourceIndex = resourceIndex,
                ResourceName = name,
                AdjacentData = adjacentData,
                UseNameHeuristics = true,
            };
            return Detect(context);
        }
    }
}
