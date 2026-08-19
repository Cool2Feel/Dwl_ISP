using UpgradeTool.Core.Utilities;

namespace UpgradeTool.Core.Protocol;

/// <summary>待刷写的固件镜像（原始 bin）。</summary>
public sealed class FirmwareImage
{
    public string FilePath { get; }
    public byte[] Data { get; }
    public long Length => Data.Length;
    public uint Crc32 { get; }

    public FirmwareImage(string filePath, byte[] data)
    {
        FilePath = filePath;
        Data = data;
        Crc32 = Utilities.Crc32.Compute(data);
    }

    public static FirmwareImage Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length == 0)
            throw new InvalidDataException($"固件文件为空: {path}");
        return new FirmwareImage(path, data);
    }
}
