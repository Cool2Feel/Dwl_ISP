namespace ResBinManager.Core.ResourceDetection
{
    public class ResourceDetectionContext
    {
        public byte[]? Data { get; set; }
        public uint Length { get; set; }
        public int ResourceIndex { get; set; } = -1;
        public string? ResourceName { get; set; }
        public byte[]? AdjacentData { get; set; }
        public bool UseNameHeuristics { get; set; }
    }
}
