namespace UpgradeTool.Core.Protocol;

/// <summary>
/// AX2005 适配器 0xCB 厂商命令通道的 CDB 编码（对齐 MPTool AX2005Adapter 经 UFRunCode 下发，
/// USB_CMD 结构 L3 布局）。
///
/// L3 布局（16 字节 union，主机 memcpy 前 15 字节到 CDB[1..15]，对齐 MPTool ScsiCBW_Buf[16]）：
///   offset 0  Func1   (WORD)  → CDB[1..2]，大端（对齐 MPTool ENDIAN_HALF 字节交换后按小端落内存）
///   offset 2  DataAddr(WORD)  → CDB[3..4]，大端
///   offset 4  Param1  (WORD)  → CDB[5..6]
///   offset 6  Param2  (WORD)  → CDB[7..8]
///   offset 8  Func2   (DWORD) → CDB[9..12]，小端（0xffffffff 为"无 L2"哨兵）
///   offset 12 Param   (DWORD) → CDB[13..15]（仅低 3 字节进入 CDB）
///
/// 用途（对齐 MPTool）：
///   - 适配器驱动上传/校验：Func1=MemReadWrite，DataAddr=加载地址+偏移，Func2=0xffffffff；
///   - 适配器功能调用：Func1=Init / probe_port / probe_dev / tgt_rw 等符号地址。
/// 注意：L3 的 Func1/DataAddr 是 16 位（ENDIAN_HALF），与固件 0xCD 通道（Dc503RomCommands 32 位 L2）不同，
/// 二者不可混用。
/// </summary>
public static class AdapterCommands
{
    /// <summary>0xCB 厂商命令 OpCode（对齐 MPTool UF_MODE_CODE）。</summary>
    public const byte OpCode = 0xCB;

    /// <summary>Func2 哨兵：无 L2（对齐 MPTool UsbCmd.L3.Func2 = 0xffffffff）。</summary>
    public const uint NoL2 = 0xFFFFFFFF;

    /// <summary>
    /// 构建 0xCB L3 命令的 16 字节 CDB。
    /// Func1/DataAddr 按 MPTool ENDIAN_HALF 语义以大端写入 CDB[1..4]（WORD），
    /// Func2 按小端写入 CDB[9..12]，Param 仅低 3 字节写入 CDB[13..15]。
    /// </summary>
    public static byte[] BuildCdb(uint func1, uint dataAddr, uint func2 = NoL2, uint param = 0)
    {
        byte[] cdb = new byte[16];
        cdb[0] = OpCode;

        // Func1 / DataAddr：16 位字段大端（对齐 ENDIAN_HALF 的字节交换结果）
        cdb[1] = (byte)((func1 >> 8) & 0xFF);
        cdb[2] = (byte)(func1 & 0xFF);
        cdb[3] = (byte)((dataAddr >> 8) & 0xFF);
        cdb[4] = (byte)(dataAddr & 0xFF);

        // Param1 / Param2：未使用，置 0
        cdb[5] = 0;
        cdb[6] = 0;
        cdb[7] = 0;
        cdb[8] = 0;

        // Func2：小端 4 字节
        cdb[9] = (byte)(func2 & 0xFF);
        cdb[10] = (byte)((func2 >> 8) & 0xFF);
        cdb[11] = (byte)((func2 >> 16) & 0xFF);
        cdb[12] = (byte)((func2 >> 24) & 0xFF);

        // Param：仅低 3 字节进入 CDB[13..15]
        cdb[13] = (byte)(param & 0xFF);
        cdb[14] = (byte)((param >> 8) & 0xFF);
        cdb[15] = (byte)((param >> 16) & 0xFF);

        return cdb;
    }

    /// <summary>0xCB L3 命令解码（模拟适配器设备侧解析用，与 <see cref="BuildCdb"/> 布局对应）。</summary>
    public static (uint Func1, uint DataAddr, uint Func2, uint Param) DecodeCdb(byte[] cdb)
    {
        uint func1 = ((uint)cdb[1] << 8) | cdb[2];        // WORD 大端
        uint dataAddr = ((uint)cdb[3] << 8) | cdb[4];     // WORD 大端
        uint func2 = cdb[9] | ((uint)cdb[10] << 8) | ((uint)cdb[11] << 16) | ((uint)cdb[12] << 24);
        uint param = ((uint)cdb[13]) | ((uint)cdb[14] << 8) | ((uint)cdb[15] << 16);
        return (func1, dataAddr, func2, param);
    }
}
