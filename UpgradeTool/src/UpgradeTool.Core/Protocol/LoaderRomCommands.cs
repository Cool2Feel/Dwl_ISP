namespace UpgradeTool.Core.Protocol;

/// <summary>
/// Loader 模式厂商命令通道（0xCB）的 CDB 构建。
///
/// 0xCB 是下载器（Loader/Bootloader 态）的厂商命令通道，字节布局与固件应用态的
/// 0xCD 完全一致（同一条 SCSI 通道、同一个 get_cbw 解析器）：
///   CDB[0]      = OpCode = 0xCB
///   CDB[1..4]   = Func1（LE）
///   CDB[5..8]   = DataAddr（LE）
///   CDB[9..12]  = Func2（LE）
///   CDB[13..15] = Param（24 位小端）
///
/// 字段语义参考 MPTool 逆向 / 真机抓包（ThunderSE.elf 符号表 + 0x40a8xx 发送器）：
///   - Func1 = Loader 内置驱动入口（l1_func_*，无数据命令 / SPI 命令派发）或 RBC_mem_rwex（写 flash 数据搬运）；
///   - DataAddr = flash 地址 / RBC_mem_rwex_buf 数据暂存缓冲 / 0xffffffff 哨兵；
///   - Func2 = 驱动入口（l2_func_spi_page_program=0x208）/ SPI 命令码 / 大端 flash 地址（读命令）；
///   - Param = 24 位 flash 偏移。
/// 设备侧字段解析可复用 Dc503RomCommands.DecodeCdb（不校验 OpCode）。
/// </summary>
public static class LoaderRomCommands
{
    /// <summary>Loader 模式厂商命令 OpCode（MPTool Send0xCB）。</summary>
    public const byte OpCode = 0xCB;

    /// <summary>Func2 哨兵：无 L2 调用（原始 RAM 写入 = 驱动上传）。</summary>
    public const uint NoL2 = Dc503RomCommands.NoL2;

    /// <summary>DataAddr 哨兵：数据走 Loader 的 prxbuf/ptxbuf（免回传地址解析）。</summary>
    public const uint NoDataAddr = Dc503RomCommands.NoDataAddr;

    /// <summary>构建 0xCB 命令的 16 字节 CDB（字节布局与 0xCD 相同）。</summary>
    public static byte[] BuildCdb(uint func1, uint dataAddr, uint func2, uint param)
    {
        byte[] cdb = new byte[16];
        cdb[0] = OpCode;
        cdb[1] = (byte)(func1 & 0xFF);
        cdb[2] = (byte)((func1 >> 8) & 0xFF);
        cdb[3] = (byte)((func1 >> 16) & 0xFF);
        cdb[4] = (byte)((func1 >> 24) & 0xFF);
        cdb[5] = (byte)(dataAddr & 0xFF);
        cdb[6] = (byte)((dataAddr >> 8) & 0xFF);
        cdb[7] = (byte)((dataAddr >> 16) & 0xFF);
        cdb[8] = (byte)((dataAddr >> 24) & 0xFF);
        cdb[9] = (byte)(func2 & 0xFF);
        cdb[10] = (byte)((func2 >> 8) & 0xFF);
        cdb[11] = (byte)((func2 >> 16) & 0xFF);
        cdb[12] = (byte)((func2 >> 24) & 0xFF);
        cdb[13] = (byte)(param & 0xFF);
        cdb[14] = (byte)((param >> 8) & 0xFF);
        cdb[15] = (byte)((param >> 16) & 0xFF);
        return cdb;
    }

    /// <summary>
/// 构建 Loader 模式 signal_drive 命令（L1 布局），对齐 MPTool USB_CMD.L1 结构：
///   CDB[0]      = OpCode = 0xCB
///   CDB[1..4]   = func1（LE）
///   CDB[5]      = param（保留，0）
///   CDB[6]      = ctrl（控制位：0x03=仅命令+读数据，0x07=命令+地址+读数据）
///   CDB[7]      = siLen（Si 有效字节数：1=仅命令码，4=命令码+3字节地址）
///   CDB[8]      = si[0]（SPI 命令码）
///   CDB[9..11]  = si[1..3]（24 位大端 flash 地址）
///   CDB[12..15] = 0（保留/填充）
/// </summary>
public static byte[] BuildSignalDriveCdb(uint func1, byte ctrl, byte siLen, byte spiCmd, uint flashAddr)
{
    byte[] cdb = new byte[16];
    cdb[0] = OpCode;
    cdb[1] = (byte)(func1 & 0xFF);
    cdb[2] = (byte)((func1 >> 8) & 0xFF);
    cdb[3] = (byte)((func1 >> 16) & 0xFF);
    cdb[4] = (byte)((func1 >> 24) & 0xFF);
    cdb[5] = 0; // param
    cdb[6] = ctrl;
    cdb[7] = siLen;
    cdb[8] = spiCmd;
    cdb[9] = (byte)((flashAddr >> 16) & 0xFF);
    cdb[10] = (byte)((flashAddr >> 8) & 0xFF);
    cdb[11] = (byte)(flashAddr & 0xFF);
    return cdb;
}

/// <summary>
/// 构建 Loader flash 读命令（兼容旧版，建议改用 BuildSignalDriveCdb）。
/// 真机抓包 mptool-810-101.txt 确认 SPI 读布局：
///   CDB = cb 74 00 00 00 00 07 04 03 XX YY ZZ 00 00 00 00
/// 其中 Ctrl=0x07（命令+地址+读数据），SiLen=4（1 字节命令码 + 3 字节地址）。
/// </summary>
public static byte[] BuildFlashReadCdb(uint func1, uint spiCmd, uint flashAddr)
    => BuildFlashReadCdb(func1, LoaderConfig.DefaultSigdrvBuf, spiCmd, flashAddr);

/// <summary>
/// 构建 SPI 读命令 CDB（可指定 DataAddr）。
/// 6666.txt flash 数据读取：DataAddr=0x04070000（大缓冲，512B/块）。
/// 注意：DataAddr 只是主机侧约定——loader 的 L2 SPI 读驱动固定把结果落到 FlashReadBuf(0x04070000)，
/// 换任意 DataAddr（如 0x01030000）读回内容不变（真机 123/777 正常、321/555/888 均 1F FF FF 已证实）。
/// 从 dataAddr 间接提取 Ctrl（byte 2）和 SiLen（byte 3）：
///   dataAddr=0x04070000 → Ctrl=0x07, SiLen=4（SPI 读/擦除）
///   dataAddr=0x01030000 → Ctrl=0x03, SiLen=1（NOP/ID 探测/WREN）
/// </summary>
public static byte[] BuildFlashReadCdb(uint func1, uint dataAddr, uint spiCmd, uint flashAddr)
{
    byte ctrl = (byte)((dataAddr >> 16) & 0xFF);
    byte siLen = (byte)((dataAddr >> 24) & 0xFF);
    return BuildSignalDriveCdb(func1, ctrl, siLen, (byte)spiCmd, flashAddr);
}

/// <summary>
/// 构建 0xCB 设备复位命令 CDB，对齐 MPTool AX326X::DeviceReset()：
///   CDB[0]      = 0xCB（OpCode）
///   CDB[1..4]   = Func1 = 0（LE）
///   CDB[5..8]   = DataAddr = 0（LE，未使用）
///   CDB[9..12]  = Func2 = l2_func_reset 地址（LE）
///   CDB[13..15] = Param = 0x4（24 位，与 LoaderRomCommands.BuildCdb 的 Param 编码一致）
///
/// 参考 MPTool 代码（AX326X.cpp DeviceReset，SpiDriver.cpp SpiReset）：
///   1. memset(&UsbCmd, 0, 0x10)
///   2. UsbCmd.L2.Func1 = 0
///   3. UsbCmd.L2.Param = 0x4
///   4. UsbCmd.L2.Func2 = func_Reset（来自 ELF 符号 l2_func_reset）
///   5. UFRunCode(&UsbCmd, 0, NULL, USB_WRITE) → WriteToScsi(SCSI_IOCTL_DATA_OUT, cdbLen=16, dataLen=0, data=NULL)
///
/// 固件端 scsi_cmd_analysis 在 CbwTrxLength==0 分支中匹配 OpCode=0xCB，
/// 但 SubOpCode=0（Func1 字节 0）不匹配 0xf0/0xf1 子命令，实际落入 INVALID_FIELD_IN_COMMAND。
/// 此命令在 MPTool 中同样因 DeviceReset() 始终返回 0 而忽略错误。
/// 对齐 MPTool 发送方式：DATA_OUT 方向，0 数据长度，NULL 数据缓冲。
/// </summary>
public static byte[] BuildDeviceResetCdb(uint func2Reset)
{
    return BuildCdb(func1: 0, dataAddr: 0, func2: func2Reset, param: 0x4);
}
}
