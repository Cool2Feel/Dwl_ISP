using UpgradeTool.Core.Utilities;

namespace UpgradeTool.Core.Protocol;

/// <summary>
/// 启动扇区（BLDR 头）解析与校验和计算。
///
/// 对齐固件 ax32xx\BLDRX32.S 与 HM020F ResBinManager 的 DestBin 结构：
///   偏移 0x00-0x03  BLDR_VER         版本号
///   偏移 0x04-0x07  "BLDR"           签名
///   偏移 0x08       CheckSum         "Should be Check Sum of the sector"
///   偏移 0x09       boot_sector_num  flash_param 偏移 / 16
///   偏移 0x0A       boot_flagbyte    CFG_FUNC=0x01 | INVALID_KEY=0x02 | NO_CHKSUM=0x04
///   偏移 0x0B       对齐填充
///
/// MPTool 真机抓包（6666.txt L3112-3115）证实 MPTool 会计算并写入 byte 8（值 0x9c），
/// 即使 boot_flagbyte=0x05 已置 NO_CHKSUM bit。本工具按"扇区求和归零"计算：
///   byte8 = (0x100 - sum(其余 511 字节) mod 256) & 0xFF，使整个 512 字节扇区累加 ≡ 0 (mod 256)。
/// 注：当前产品固件 spi_boot_cfg.h 为 NO_CHKSUM=1，bootloader 不校验该字节；
/// 写入仅为 MPTool 流程对等，不改变任何启动行为。
/// </summary>
public static class BootSector
{
    /// <summary>引导扇区大小（CODE_BANK_SIZE），校验和的计算范围。</summary>
    public const int SectorSize = 512;

    /// <summary>"BLDR" 签名偏移。</summary>
    public const int MagicOffset = 4;

    /// <summary>校验和字节偏移（0x08）。</summary>
    public const int ChecksumOffset = 8;

    /// <summary>boot_sector_num 偏移（0x09）。</summary>
    public const int BootSectorNumOffset = 9;

    /// <summary>boot_flagbyte 偏移（0x0A）。</summary>
    public const int BootFlagOffset = 10;

    /// <summary>boot_flagbyte 的 NO_CHKSUM bit：置位表示 bootloader 跳过 byte 8 校验。</summary>
    public const byte NoChecksumFlag = 0x04;

    /// <summary>boot_flagbyte 的 ENCRYPTION bit：置位表示固件需要硬件辅助加密写入。</summary>
    /// <remarks>
    /// 对齐 MPTool SpiDriver::SetEncryptAddr 的检测逻辑：
    ///   srcData[10] &amp; (1&lt;&lt;4) 时触发加密流程。
    /// 当前 DC503J 固件未启用此特性（spi_boot_cfg.h NO_CHKSUM=1 仅 bit2 置位），
    /// 此常量作为扩展点预留，供后续固件升级时启用。
    /// </remarks>
    public const byte EncryptionFlag = 0x10;

    /// <summary>flash_param 内数据起始扇区字段偏移（相对 flash_param，扇区号，×512 = 字节偏移）。</summary>
    public const int CrcStartSectorOffset = 0x14;

    /// <summary>flash_param 内数据长度扇区字段偏移（相对 flash_param，扇区号，×512 = 字节数）。</summary>
    public const int CrcLengthSectorOffset = 0x18;

    /// <summary>flash_param 内 CRC16 存储偏移（相对 flash_param）。</summary>
    public const int CrcStoreOffset = 0x20;

    /// <summary>
    /// 判断镜像是否含合法 BLDR 启动扇区。对齐 MPTool <c>SpiDriver.SetEncryptAddr</c> 的判定：
    /// 版本字段 <c>data[0..1]==0</c>、魔数 <c>"BLDR"</c>、尾标 <c>data[0x1fe..0x1ff]==0x55AA</c>。
    /// </summary>
    public static bool HasBootSector(ReadOnlySpan<byte> data)
        => data.Length >= SectorSize
           && data[0] == 0
           && data[1] == 0
           && data[MagicOffset] == (byte)'B'
           && data[MagicOffset + 1] == (byte)'L'
           && data[MagicOffset + 2] == (byte)'D'
           && data[MagicOffset + 3] == (byte)'R'
           && data[0x1fe] == 0x55
           && data[0x1ff] == 0xAA;

    /// <summary>当前固件的启动扇区校验和字节（偏移 8）；无 BLDR 签名返回 0。</summary>
    public static byte CurrentChecksum(ReadOnlySpan<byte> data) => HasBootSector(data) ? data[ChecksumOffset] : (byte)0;

    /// <summary>boot_sector_num（偏移 9，flash_param 偏移 / 16）；无 BLDR 签名返回 0。</summary>
    public static byte BootSectorNum(ReadOnlySpan<byte> data) => HasBootSector(data) ? data[BootSectorNumOffset] : (byte)0;

    /// <summary>boot_flagbyte（偏移 10）；无 BLDR 签名返回 0。</summary>
    public static byte BootFlagByte(ReadOnlySpan<byte> data) => HasBootSector(data) ? data[BootFlagOffset] : (byte)0;

    /// <summary>是否置 NO_CHKSUM（boot_flagbyte bit2）：置位表示 bootloader 不校验 byte 8。</summary>
    public static bool NoChecksum(ReadOnlySpan<byte> data) => (BootFlagByte(data) & NoChecksumFlag) != 0;

    /// <summary>是否置加密标志（boot_flagbyte bit4）：置位表示固件需要硬件辅助加密写入。</summary>
    /// <remarks>当前 DC503J 固件未启用，此方法为扩展点预留。</remarks>
    public static bool NeedsEncryption(ReadOnlySpan<byte> data) => (BootFlagByte(data) & EncryptionFlag) != 0;

    /// <summary>
    /// 计算使整个 512 字节扇区累加 ≡ 0 (mod 256) 的校验和字节。
    /// 计算时排除校验和字节本身（0x08），其余 511 字节求和后取两补数。
    /// </summary>
    public static byte ComputeChecksum(ReadOnlySpan<byte> sector)
    {
        int sum = 0;
        int end = Math.Min(SectorSize, sector.Length);
        for (int i = 0; i < end; i++)
        {
            if (i == ChecksumOffset)
                continue; // 校验和字节本身不参与求和
            sum += sector[i];
        }
        return (byte)((0x100 - (sum & 0xFF)) & 0xFF);
    }

    /// <summary>
    /// 计算引导扇区数据区的 CRC16（CRC-16/CCITT-FALSE），对齐 MPTool <c>AX326X::SetCRC</c>：
    ///   从 flash_param 读「数据起始扇区」(+0x14) 与「数据长度扇区」(+0x18)，均 ×512 得字节范围；
    ///   对该范围取字节（不足用 0xFF 填充）求 CRC16。字段越界或无有效范围时返回 0xFFFF（空数据区 CRC=初值）。
    /// </summary>
    public static ushort ComputeCrc16(ReadOnlySpan<byte> data)
    {
        if (!HasBootSector(data))
            return Crc16.Init;

        int param = BootSectorNum(data) * 16;
        if (param + CrcLengthSectorOffset + 4 > data.Length)
            return Crc16.Init;

        int start = (int)(ReadU32(data, param + CrcStartSectorOffset) << 9);
        int len = (int)(ReadU32(data, param + CrcLengthSectorOffset) << 9);
        if (start < 0 || len <= 0 || start >= data.Length)
            return Crc16.Init;

        ushort crc = Crc16.Init;
        for (int i = 0; i < len; i++)
        {
            int src = start + i;
            byte b = (uint)src < (uint)data.Length ? data[src] : (byte)0xFF;
            Crc16.Update(ref crc, b);
        }
        return crc;
    }

    /// <summary>
    /// 打补丁：返回字节 8 已按"求和归零"更新、且数据区 CRC16 已按 MPTool SetCRC 写入的新镜像；
    /// 无 BLDR 签名或均已为期望值时原样返回。返回新 FirmwareImage（Crc32 同步重算），
    /// 供刷写与校验共用同一份字节。CRC16 先写、校验和 byte8 后算，确保扇区求和归零包含 CRC 字节。
    /// </summary>
    public static FirmwareImage Patch(FirmwareImage image)
    {
        if (!HasBootSector(image.Data))
            return image;

        byte[] patched = (byte[])image.Data.Clone();
        bool changed = false;

        // 1) 数据区 CRC16（写入 flash_param + 0x20，4 字节小端 DWORD，高位 2 字节清零）。
        //    对齐 MPTool SetCRC 的 *(DWORD*)(srcData + 0x20) = crc：按 DWORD 覆盖出厂占位符
        //    （0x01234567）的残留高位，使 CRC16 槽位与本工具重算值完全一致。位置须落在引导扇区内
        int param = BootSectorNum(image.Data) * 16;
        int storeOff = param + CrcStoreOffset;
        if (storeOff + 4 <= SectorSize)
        {
            ushort crc = ComputeCrc16(image.Data);
            byte lo = (byte)(crc & 0xFF), hi = (byte)(crc >> 8);
            if (patched[storeOff] != lo || patched[storeOff + 1] != hi
                || patched[storeOff + 2] != 0 || patched[storeOff + 3] != 0)
            {
                patched[storeOff] = lo;
                patched[storeOff + 1] = hi;
                patched[storeOff + 2] = 0;
                patched[storeOff + 3] = 0;
                changed = true;
            }
        }

        // 2) byte8 校验和（在 CRC 写入之后计算，使扇区求和归零包含 CRC 字节）
        byte computed = ComputeChecksum(patched);
        if (patched[ChecksumOffset] != computed)
        {
            patched[ChecksumOffset] = computed;
            changed = true;
        }

        if (!changed)
            return image;
        return new FirmwareImage(image.FilePath, patched);
    }

    private static uint ReadU32(ReadOnlySpan<byte> b, int off) =>
        (uint)b[off] | ((uint)b[off + 1] << 8) | ((uint)b[off + 2] << 16) | ((uint)b[off + 3] << 24);

    /// <summary>校验扇区求和归零不变式（测试用）：sum(512B) ≡ 0 (mod 256)。</summary>
    public static bool SectorSumsToZero(ReadOnlySpan<byte> sector)
    {
        if (sector.Length < SectorSize)
            return false;
        int sum = 0;
        for (int i = 0; i < SectorSize; i++)
            sum += sector[i];
        return (sum & 0xFF) == 0;
    }
}
