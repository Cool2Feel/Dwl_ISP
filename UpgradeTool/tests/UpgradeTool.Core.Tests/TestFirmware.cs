using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Tests;

/// <summary>测试固件镜像构造辅助（BLDR 头对齐 DestBin.bin / ax32xx\BLDRX32.S）。</summary>
public static class TestFirmware
{
    /// <summary>随机内容固件（无 BLDR 签名）。</summary>
    public static byte[] Random(int length, int seed = 42)
    {
        var random = new Random(seed);
        var data = new byte[length];
        random.NextBytes(data);
        return data;
    }

    /// <summary>
    /// 构造带合法 BLDR 启动扇区的固件：
    ///   偏移 0x00-0x03 = BLDR_VER 0x00020000
    ///   偏移 0x04-0x07 = "BLDR"
    ///   偏移 0x08     = 校验和字节（默认 0x00，未计算）
    ///   偏移 0x09     = boot_sector_num = 1
    ///   偏移 0x0A     = boot_flagbyte（默认 0x05 = CFG_FUNC|NO_CHKSUM）
    ///   偏移 0x0B     = VERSION 0x12
    /// </summary>
    public static byte[] WithBootSector(int totalLength, byte flagByte = 0x05, byte checksumByte = 0x00)
    {
        if (totalLength < BootSector.SectorSize)
            throw new ArgumentOutOfRangeException(nameof(totalLength));

        byte[] data = Random(totalLength, seed: 7);
        data[0] = 0x00; data[1] = 0x00; data[2] = 0x02; data[3] = 0x00;
        data[4] = (byte)'B'; data[5] = (byte)'L'; data[6] = (byte)'D'; data[7] = (byte)'R';
        data[8] = checksumByte;
        data[9] = 0x01;
        data[10] = flagByte;
        data[11] = 0x12;
        // 启动扇区尾标 0x55AA（MPTool SetEncryptAddr 据此前置判定合法 BLDR 头）
        data[0x1fe] = 0x55;
        data[0x1ff] = 0xAA;
        return data;
    }

    public static FirmwareImage Image(byte[] data, string path = "test.bin") => new(path, data);
}
