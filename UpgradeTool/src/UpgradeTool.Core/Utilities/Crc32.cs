using System.Runtime.InteropServices;

namespace UpgradeTool.Core.Utilities;

/// <summary>CRC-32（IEEE 802.3，多项式 0xEDB88320），用于固件校验。</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => Finalize(Update(0xFFFFFFFF, data));

    /// <summary>增量计算：对 <paramref name="data"/> 更新运行态 CRC，返回中间值（尚未异或 0xFFFFFFFF）。</summary>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    /// <summary>结束增量计算：把中间值异或 0xFFFFFFFF 得到最终 CRC-32。</summary>
    public static uint Finalize(uint crc) => crc ^ 0xFFFFFFFF;

    public static byte[] ComputeBytes(ReadOnlySpan<byte> data)
    {
        uint value = Compute(data);
        return new[]
        {
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value,
        };
    }
}
