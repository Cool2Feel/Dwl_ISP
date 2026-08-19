using System.Text;
using UpgradeTool.Core.Utilities;

namespace UpgradeTool.Core.Tests;

public class Crc32Tests
{
    [Fact]
    public void Compute_MatchesKnownVector()
    {
        // 标准校验向量: CRC32("123456789") = 0xCBF43926
        byte[] data = Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0xCBF43926u, Crc32.Compute(data));
    }

    [Fact]
    public void Compute_EmptyInput_IsZero()
    {
        Assert.Equal(0u, Crc32.Compute(Array.Empty<byte>()));
    }

    [Fact]
    public void ComputeBytes_ReturnsBigEndianFourBytes()
    {
        byte[] bytes = Crc32.ComputeBytes(Encoding.ASCII.GetBytes("123456789"));
        Assert.Equal(new byte[] { 0xCB, 0xF4, 0x39, 0x26 }, bytes);
    }
}
