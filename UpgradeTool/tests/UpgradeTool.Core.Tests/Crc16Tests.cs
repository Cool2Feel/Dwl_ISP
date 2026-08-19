using UpgradeTool.Core.Utilities;

namespace UpgradeTool.Core.Tests;

/// <summary>CRC-16/CCITT-FALSE（对齐 MPTool Soft_crc16 / SetCRC）校验测试。</summary>
public class Crc16Tests
{
    [Fact]
    public void Compute_MatchesKnownVector()
    {
        // CRC-16/CCITT-FALSE 标准测试向量："123456789" -> 0x29B1
        ushort crc = Crc16.Compute("123456789"u8);
        Assert.Equal(0x29B1, crc);
    }

    [Fact]
    public void Compute_Empty_ReturnsInit()
    {
        Assert.Equal(Crc16.Init, Crc16.Compute(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Compute_MatchesStandardTableFormulation()
    {
        // 独立的标准 CRC-16/CCITT-FALSE 实现交叉验证（crc ^= byte<<8 后再逐位处理）
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];
        ushort expected = 0xFFFF;
        foreach (byte b in data)
        {
            expected ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
                expected = (expected & 0x8000) != 0
                    ? (ushort)((expected << 1) ^ 0x1021)
                    : (ushort)(expected << 1);
        }
        Assert.Equal(expected, Crc16.Compute(data));
    }
}
