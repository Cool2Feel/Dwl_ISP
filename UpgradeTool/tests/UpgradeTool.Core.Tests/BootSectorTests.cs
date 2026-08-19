using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Tests;

/// <summary>启动扇区校验和（BLDR 头 byte 8）计算与打补丁测试。</summary>
public class BootSectorTests
{
    [Fact]
    public void HasBootSector_DetectsBldrSignature()
    {
        byte[] data = TestFirmware.WithBootSector(1024);

        Assert.True(BootSector.HasBootSector(data));
        Assert.False(BootSector.HasBootSector(TestFirmware.Random(1024)));
        Assert.False(BootSector.HasBootSector(new byte[400])); // 短于 512
    }

    [Fact]
    public void HasBootSector_RequiresVersionBytesAndTail()
    {
        byte[] good = TestFirmware.WithBootSector(512);
        Assert.True(BootSector.HasBootSector(good));

        // 缺失尾标 0x55AA -> 非合法 BLDR 头（对齐 MPTool SetEncryptAddr）
        byte[] noTail = (byte[])good.Clone();
        noTail[0x1fe] = 0x00; noTail[0x1ff] = 0x00;
        Assert.False(BootSector.HasBootSector(noTail));

        // 版本字段 data[0..1] 非 0 -> 非合法 BLDR 头
        byte[] badVer = (byte[])good.Clone();
        badVer[0] = 0x12;
        Assert.False(BootSector.HasBootSector(badVer));
    }

    [Fact]
    public void BootSectorNum_And_BootFlag_Parsed()
    {
        byte[] data = TestFirmware.WithBootSector(1024, flagByte: 0x05);

        Assert.Equal(0x05, BootSector.BootFlagByte(data));
        Assert.True(BootSector.NoChecksum(data)); // bit2 置位
        Assert.Equal(1, BootSector.BootSectorNum(data));
    }

    [Fact]
    public void NoChecksum_ReflectsFlagByte()
    {
        Assert.True(BootSector.NoChecksum(TestFirmware.WithBootSector(512, flagByte: 0x05)));
        Assert.False(BootSector.NoChecksum(TestFirmware.WithBootSector(512, flagByte: 0x01)));
    }

    [Fact]
    public void Patch_UpdatesByte8_AndSectorSumsToZero()
    {
        FirmwareImage image = TestFirmware.Image(TestFirmware.WithBootSector(1024, checksumByte: 0x00));

        FirmwareImage patched = BootSector.Patch(image);

        Assert.NotSame(image, patched);
        Assert.Equal(0x00, image.Data[BootSector.ChecksumOffset]); // 原镜像未被修改
        // byte8 = 使整扇区（含已写入的 CRC16）求和归零的校验和
        Assert.Equal(BootSector.ComputeChecksum(patched.Data), patched.Data[BootSector.ChecksumOffset]);
        Assert.True(BootSector.SectorSumsToZero(patched.Data.AsSpan(0, BootSector.SectorSize)));
        // CRC32 随补丁重算
        Assert.NotEqual(image.Crc32, patched.Crc32);
    }

    [Fact]
    public void Patch_IsIdempotent()
    {
        FirmwareImage image = TestFirmware.Image(TestFirmware.WithBootSector(1024, checksumByte: 0x00));
        FirmwareImage once = BootSector.Patch(image);
        FirmwareImage twice = BootSector.Patch(once);

        Assert.Same(once, twice); // byte8 已为期望值，原样返回
    }

    [Fact]
    public void Patch_NoOp_WithoutBldrSignature()
    {
        FirmwareImage image = TestFirmware.Image(TestFirmware.Random(1024));

        Assert.Same(image, BootSector.Patch(image));
    }

    [Fact]
    public void Patch_NoOp_ForShortFirmware()
    {
        FirmwareImage image = TestFirmware.Image(TestFirmware.Random(300));

        Assert.Same(image, BootSector.Patch(image));
    }

    [Fact]
    public void ComputeChecksum_IsTwoComplementOfSectorSum()
    {
        // 校验和 = 其余 511 字节求和的两补数，使整扇区累加 ≡ 0 (mod 256)
        byte[] data = TestFirmware.WithBootSector(512, checksumByte: 0x00);
        int sumWithoutChecksum = 0;
        for (int i = 0; i < 512; i++)
        {
            if (i == BootSector.ChecksumOffset)
                continue;
            sumWithoutChecksum += data[i];
        }
        byte expected = (byte)((0x100 - (sumWithoutChecksum & 0xFF)) & 0xFF);

        Assert.Equal(expected, BootSector.ComputeChecksum(data));
    }

    [Fact]
    public void Patch_WritesCrc16_AndSectorSumsToZero()
    {
        byte[] data = TestFirmware.WithBootSector(1024, checksumByte: 0x00);
        // boot_sector_num=1 -> flash_param @0x10；写入数据区起始扇区/长度扇区字段（对齐 MPTool SetCRC）
        int param = BootSector.BootSectorNum(data) * 16;
        const int startByte = 512;
        const int lenBytes = 512;
        WriteU32(data, param + BootSector.CrcStartSectorOffset, (uint)(startByte / 512));
        WriteU32(data, param + BootSector.CrcLengthSectorOffset, (uint)(lenBytes / 512));

        FirmwareImage patched = BootSector.Patch(TestFirmware.Image(data));

        int storeOff = param + BootSector.CrcStoreOffset;
        ushort expected = UpgradeTool.Core.Utilities.Crc16.Compute(data.AsSpan(startByte, lenBytes));
        ushort actual = (ushort)(patched.Data[storeOff] | (patched.Data[storeOff + 1] << 8));
        Assert.Equal(expected, actual);
        // 对齐 MPTool SetCRC 的 *(DWORD*)=crc：高位 2 字节清零（覆盖出厂占位符 0x01234567 的残留高位）
        Assert.Equal(0x00, patched.Data[storeOff + 2]);
        Assert.Equal(0x00, patched.Data[storeOff + 3]);
        Assert.True(BootSector.SectorSumsToZero(patched.Data.AsSpan(0, BootSector.SectorSize)));
    }

    [Fact]
    public void ComputeCrc16_NoValidRange_ReturnsInit()
    {
        // 数据长度扇区字段为 0 -> 无有效范围，返回初值 0xFFFF
        byte[] data = TestFirmware.WithBootSector(512, checksumByte: 0x00);
        Assert.Equal(UpgradeTool.Core.Utilities.Crc16.Init, BootSector.ComputeCrc16(data));
    }

    private static void WriteU32(byte[] b, int off, uint value)
    {
        b[off] = (byte)(value & 0xFF);
        b[off + 1] = (byte)((value >> 8) & 0xFF);
        b[off + 2] = (byte)((value >> 16) & 0xFF);
        b[off + 3] = (byte)((value >> 24) & 0xFF);
    }
}
