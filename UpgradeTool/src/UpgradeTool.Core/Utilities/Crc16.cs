namespace UpgradeTool.Core.Utilities;

/// <summary>
/// CRC-16/CCITT-FALSE：多项式 0x1021、初值 0xFFFF、MSB 优先、无反射、无最终异或。
/// 与 MPTool 固件烧写用 <c>Soft_crc16</c>（Target.cpp:503 / SpiDriver.cpp:402）逐字节位算法一致，
/// 用于对引导扇区数据区计算并写入 CRC16（对齐 MPTool <c>AX326X::SetCRC</c>）。
/// </summary>
public static class Crc16
{
    /// <summary>CRC-16/CCITT-FALSE 多项式。</summary>
    public const ushort Polynomial = 0x1021;

    /// <summary>初始值（MPTool CalcCRC 初值 0xffff）。</summary>
    public const ushort Init = 0xFFFF;

    /// <summary>计算 CRC-16/CCITT-FALSE（无最终异或）。</summary>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = Init;
        foreach (byte b in data)
            Update(ref crc, b);
        return crc;
    }

    /// <summary>以字节为单位更新 CRC（MSB 优先，对齐 MPTool Soft_crc16 位级循环）。</summary>
    public static void Update(ref ushort crc, byte value)
    {
        for (int i = 0; i < 8; i++)
        {
            bool bit = ((value >> 7) ^ (crc >> 15)) != 0;
            crc <<= 1;
            if (bit)
                crc ^= Polynomial;
            value <<= 1;
        }
    }
}
