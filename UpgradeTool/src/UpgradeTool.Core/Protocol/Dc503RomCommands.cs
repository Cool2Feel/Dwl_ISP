namespace UpgradeTool.Core.Protocol;

/// <summary>
/// 0xCD 厂商命令通道的 CDB 编解码（对齐固件 hal_usb_msc.c 的 get_cbw 字节映射）。
///
/// USB MSC CBW 的 CBWCB（prxbuf[15..30]）与 SCSI CDB 一一对应：
///   CBWCB[0] = CDB[0], ..., CBWCB[15] = CDB[15]。
/// 固件 get_cbw 从 prxbuf[15..30] 提取字段（LE = 小端，MSB = 大端）：
///   OpCode      = prxbuf[15]            = CDB[0]
///   SubOpCode   = prxbuf[16]            = CDB[1]（0xCD 命令不使用）
///   Func1       = LSB(16,17,18,19)      = CDB[1..4]（小端）
///   DataAddr    = LSB(20,21,22,23)      = CDB[5..8]（小端）
///   Func2       = LSB(24,25,26,27)      = CDB[9..12]（小端）
///   Param       = MSB(0,30,29,28)       = CDB[15]<<16|CDB[14]<<8|CDB[13]（24 位）
/// 注意 DataAddr 与 Func2 都必须写入全部 4 字节（尤其是 Func2=0xffffffff 的"无 L2"
/// 哨兵），因此 0xCD 命令必须使用完整 16 字节 CDB。
/// </summary>
public static class Dc503RomCommands
{
    public const byte OpCode = 0xCD;

    /// <summary>Func2 哨兵：固件 cb_mem_read/write 判断 Func2+1 != 0 才调用 L2，0xffffffff 表示无 L2。</summary>
    public const uint NoL2 = 0xFFFFFFFF;

    /// <summary>DataAddr 哨兵：cb_mem_read/write 将其替换为 scsi.ptxbuf/prxbuf（免上传/免回读缓存问题）。</summary>
    public const uint NoDataAddr = 0xFFFFFFFF;

    /// <summary>SPI NOR flash 命令（与固件 hal_spi.h 一致）。</summary>
    public const byte FlashWriteEnable = 0x06;
    public const byte FlashReadStatus = 0x05;
    public const byte FlashReadData = 0x03;
    public const byte FlashPageProgram = 0x02;
    public const byte FlashSectorErase = 0x20;
    public const byte FlashBlockErase = 0xD8;
    public const byte FlashReadId = 0x9F;
    public const byte FlashChipErase = 0xC7;

    /// <summary>
    /// 构建 0xCD 命令的 16 字节 CDB。func2/dataAddr 字段携带 L1/L2 函数指针，
    /// 其中 dataAddr 既是数据缓冲地址（cb_mem_* 数据阶段）也是无数据命令的参数。
    /// </summary>
    public static byte[] BuildCdb(uint func1, uint dataAddr, uint func2, uint param)
    {
        byte[] cdb = new byte[16];
        cdb[0] = OpCode;

        // Func1：LE，跨 CDB[1..4]（CDB[1] 同时是 SubOpCode，0xCD 不使用）
        cdb[1] = (byte)(func1 & 0xFF);
        cdb[2] = (byte)((func1 >> 8) & 0xFF);
        cdb[3] = (byte)((func1 >> 16) & 0xFF);
        cdb[4] = (byte)((func1 >> 24) & 0xFF);

        // DataAddr：LE，跨 CDB[5..8]
        cdb[5] = (byte)(dataAddr & 0xFF);
        cdb[6] = (byte)((dataAddr >> 8) & 0xFF);
        cdb[7] = (byte)((dataAddr >> 16) & 0xFF);
        cdb[8] = (byte)((dataAddr >> 24) & 0xFF);

        // Func2：LE，跨 CDB[9..12]
        cdb[9] = (byte)(func2 & 0xFF);
        cdb[10] = (byte)((func2 >> 8) & 0xFF);
        cdb[11] = (byte)((func2 >> 16) & 0xFF);
        cdb[12] = (byte)((func2 >> 24) & 0xFF);

        // Param：24 位，CDB[13]=LOW, CDB[14]=MID, CDB[15]=HIGH（固件按 MSB 重组）
        cdb[13] = (byte)(param & 0xFF);
        cdb[14] = (byte)((param >> 8) & 0xFF);
        cdb[15] = (byte)((param >> 16) & 0xFF);

        return cdb;
    }

    /// <summary>固件 get_cbw 提取出的命令字段（模拟设备侧解析用）。</summary>
    public readonly record struct CdbFields(
        uint Func1,
        uint DataAddr,
        uint Func2,
        uint Param,
        uint CbwTrxLength,
        uint CbwFlag);

    /// <summary>
    /// 解码 16 字节 CDB 为固件字段。cbwTrxLength 来自 CBW 的 dCBWDataTransferLength
    /// （传输层传入），cbwFlag 来自 bmCBWFlags（0x80 = data-in）。
    /// </summary>
    public static CdbFields DecodeCdb(byte[] cdb, uint cbwTrxLength, uint cbwFlag)
    {
        uint Func1 = cdb[1] | ((uint)cdb[2] << 8) | ((uint)cdb[3] << 16) | ((uint)cdb[4] << 24);
        uint DataAddr = cdb[5] | ((uint)cdb[6] << 8) | ((uint)cdb[7] << 16) | ((uint)cdb[8] << 24);
        uint Func2 = cdb[9] | ((uint)cdb[10] << 8) | ((uint)cdb[11] << 16) | ((uint)cdb[12] << 24);
        uint Param = ((uint)cdb[13]) | ((uint)cdb[14] << 8) | ((uint)cdb[15] << 16);
        return new CdbFields(Func1, DataAddr, Func2, Param, cbwTrxLength, cbwFlag);
    }
}
