using System.Runtime.InteropServices;

namespace ResBinManager.Core
{
    /// <summary>
    /// RES.BIN 文件头结构
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ResBinHeader
    {
        public uint Magic;              // 魔数标识 (可选)
        public uint Version;            // 版本号
        public uint ResourceCount;      // 资源数量
        public uint TableOffset;        // 索引表偏移
        public uint DataOffset;         // 数据区偏移
        public uint Checksum;           // 校验和
        
        public const uint DEFAULT_MAGIC = 0x52455342; // "RESB"
    }

    /// <summary>
    /// 资源信息表条目 (8 字节)
    /// 对应 C 代码中的 Res_Info_T
    /// 注意: Address字段在SDK中存储的是相对偏移(relative offset)，不是绝对地址
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ResInfoEntry
    {
        public uint Offset;     // 资源数据相对于资源区基地址的偏移量 (SDK: address field)
        public uint Length;     // 资源文件大小
        
        /// <summary>
        /// 计算资源的绝对地址(需要加上资源区基地址)
        /// 对应SDK: return (nvInfo.lastRes.address + nvInfo.resAddress);
        /// </summary>
        /// <param name="baseAddress">资源区基地址(对于DestBin模式是_resBinOffset)</param>
        /// <returns>资源的绝对地址</returns>
        public uint GetAbsoluteAddress(uint baseAddress)
        {
            return baseAddress + Offset;
        }
        
        public override string ToString()
        {
            return $"Offset: 0x{Offset:X8}, Len: {Length}";
        }
    }

    /// <summary>
    /// DestBin.bin 启动扇区头部结构 (偏移0x00-0x0F)
    /// 对应 SDK: BLDRX32.S 第54-62行
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BootSectorHeader
    {
        public uint BldrVer;              // 0x00-0x03: BLDR_VER (固件版本 0x00020000)
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] Magic;              // 0x04-0x07: "BLDR" 签名 (0x52444C42)
        
        public byte CheckSum;             // 0x08: 校验和 (通常为0x00)
        public byte BootSectorNum;        // 0x09: 启动扇区号 (flash_param相对偏移/16)
        public byte BootFlagByte;         // 0x0A: 启动标志位
        public byte Reserved;             // 0x0B: 保留字节
        
        // 注意: flash_param 位于 (BootSectorNum << 4) 偏移处
    }
    
    /// <summary>
    /// Flash参数结构 (flash_param)
    /// 对应 SDK: BLDRX32.S 第64-83行
    /// 位置: 启动扇区号 × 16 字节偏移处
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FlashParam
    {
        // ===== hex表 (用于调试输出) =====
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] HexTable;           // 0x00-0x0F: "0123456789ABCDEF"
        
        // ===== 代码段信息 =====
        public uint TextStart;            // 0x10-0x13: _text_start (代码段起始地址)
        public uint TextSec;              // 0x14-0x17: _text_sec (代码段起始扇区号)
        public uint TextLen;              // 0x18-0x1B: _text_len (代码段长度)
        public uint ExceptionVma;         // 0x1C-0x1F: _exception_vma (异常向量地址)
        
        // ===== 魔数与校验 =====
        public uint Checksum;             // 0x20-0x23: CHECKSUM (校验和)
        public uint MagicKey;             // 0x24-0x27: MAGICKEY (魔数常量 0x01234567)
        
        // ===== SPI配置 =====
        public uint SpiDmaShift;          // 0x28-0x2B: SPI_DMA_SHIFT (DMA配置)
        public uint SpinandCmd;           // 0x2C-0x2F: SPINAND_CMD (SPI NAND命令)
        public uint SpiBaud;              // 0x30-0x33: SPI波特率
        
        // ===== PSRAM配置 =====
        public uint PsramCfg;             // 0x34-0x37: PSRAM配置
        public uint PsramCmd;             // 0x38-0x3B: PSRAM命令
        
        // ===== 资源区信息 (在boot_sector偏移处的+0x08和+0x0C) =====
        // 注意: 这两个字段不在flash_param结构中，而是在boot_sector的特定偏移处
        // +0x08: res_sector (资源区起始扇区号)
        // +0x0C: res_size_sectors (资源区大小扇区数)
    }
}
