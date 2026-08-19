/*
 * AX329X SPI0 flash 操作 stub（运行在 SDRAM 0x020ccec0）。
 *
 * 用途：UpgradeTool 主机工具通过固件 0xCD 应用态通道调用本 stub，
 * 实现对 SPI NOR flash 的读写/擦除，完成固件刷写。
 *
 * 调用 ABI（与固件 hal_usb_msc.c 对齐）：
 *   L1: l1_func(u32 *p1, u32 *p2)   p1 = MscCmd, p2 = SCSI
 *   L2: l2_func(u32 DataAddr, u32 Residue, u32 Param)
 *
 * 数据面使用固件 0xCD 通道的标准路由（Func1=cb_mem_rwex）：
 *   - data-in：cb_mem_read 先调 L2(DataAddr,Residue,Param) 再 cb_mem2FIFO 回传。
 *              主机侧将 DataAddr 设为 0xffffffff 哨兵 -> 固件改用 scsi.ptxbuf，
 *              L2 把读到的字节写进 ptxbuf，由固件发回主机。
 *   - data-out：cb_mem_write 先 cb_FIFO2mem(DataAddr,Residue) 再调 L2。
 *              主机侧将 DataAddr 设为 0xffffffff -> 固件改用 scsi.prxbuf，
 *              L2 从 prxbuf 读主机发来的 256B 页数据。
 *   无数据命令（WE/SE/BE/CE）直接走 L1（Func1=l1_func_signal_drive）。
 *
 * 本 stub 由 or1k-elf-gcc 编译，单 PT_LOAD 段链接到 0x020ccec0。
 */

typedef unsigned long u32;
typedef unsigned char u8;
typedef volatile __sfr unsigned long sfr_t;

#define SFR(n) (*(volatile __sfr unsigned long*)(n))

/* SPI0 寄存器（ax329x.h，SPRGROUP_SFR2=0x9000） */
#define SPIFGCON  SFR(0x9000u)
#define SPIFFACON SFR(0x9004u)
#define SPIFPACON SFR(0x9008u)
#define SPIFDCON  SFR(0x900Cu)
#define SPIFACT   SFR(0x9244u)
#define SPIFSTA   SFR(0x9248u)
#define SPIFBAUD  SFR(0x924Cu)
#define SPIFDBUF  SFR(0x9250u)

/* 系统时钟使能（PCON0，bit20=SYS_CLK_SPI0） */
#define PCON0     SFR(0x8840u)

#define SPIFGCON_CS  (1u<<14)   /* SPI0 CS 电平（1=高/空闲，0=低/选中） */
#define SPIFGCON_DIR (1u<<7)    /* 传输方向（0=发送，1=接收） */

/* SPI NOR flash 命令 */
#define FLASH_WRITE_ENABLE 0x06
#define FLASH_READ_STATUS  0x05
#define FLASH_READ_DATA    0x03
#define FLASH_PAGE_PROGRAM 0x02
#define FLASH_SECTOR_ERASE 0x20
#define FLASH_BLOCK_ERASE  0xD8
#define FLASH_READ_ID      0x9F
#define FLASH_CHIP_ERASE   0xC7

/* MSC_CMD 布局，须与固件 hal_usb_msc.c 的 MSC_CMD 完全一致（16 个 u32） */
typedef struct {
    u32 CbwTag;
    u32 CbwTrxLength;
    u32 CbwFlag;
    u32 CbwLun;
    u32 OpCode;
    u32 SubOpCode;
    u32 Address;
    u32 SubEx;
    u32 Length;
    u32 Residue;
    u32 SubEx1;
    u32 SubEx2;
    u32 SubEx3;
    u32 Func1;
    u32 DataAddr;
    u32 Func2;
    u32 Param;
} MSC_CMD;

static void spi0_send(u8 b)
{
    SPIFGCON &= ~SPIFGCON_DIR;
    SPIFDBUF = b;
    SPIFACT = 1;
    while ((SPIFSTA & 1u) == 0u) { }
}

static u8 spi0_recv(void)
{
    SPIFGCON |= SPIFGCON_DIR;
    SPIFDBUF = 0xff;
    SPIFACT = 1;
    while ((SPIFSTA & 1u) == 0u) { }
    return (u8)SPIFDBUF;
}

static void spi0_cs(u8 level)
{
    if (level)
        SPIFGCON |= SPIFGCON_CS;
    else
        SPIFGCON &= ~SPIFGCON_CS;
}

static void spi0_send_addr(u32 addr)
{
    spi0_send((u8)(addr >> 16));
    spi0_send((u8)(addr >> 8));
    spi0_send((u8)addr);
}

static u8 spi0_wait(void);

/* WE + 等待 WIP 清零 */
static void spi0_write_enable(void)
{
    spi0_cs(0);
    spi0_send(FLASH_WRITE_ENABLE);
    spi0_cs(1);
    (void)spi0_wait();
}

/* 轮询 RDSR 直到 WIP 清零（超时返回非 0） */
static u8 spi0_wait(void)
{
    volatile u32 cnt = 0;
    spi0_cs(0);
    spi0_send(FLASH_READ_STATUS);
    while (spi0_recv() & 1u) {
        if (cnt++ > 2000000u) {
            spi0_cs(1);
            return 1;
        }
    }
    spi0_cs(1);
    return 0;
}

/*-------------------------------------------------------------------*/
/* L1: SPI0 初始化（无数据）。只重配 SPIFGCON 为手动字节模式，       */
/*     引脚/时钟沿用 bootloader 与固件已配置的 SPI0 2W1D 连接。       */
/*-------------------------------------------------------------------*/
void l1_func_spi_init(u32 *p1, u32 *p2)
{
    PCON0 |= (1u<<20);      /* SYS_CLK_SPI0 on */
    SPIFGCON  = (1u<<24)|(1u<<20)|(1u<<16)|(1u<<14)|(1u<<12)|(1u<<2)|(1u<<0);
    SPIFFACON = 0;
    SPIFPACON = 0;
    SPIFDCON  = 0;
    SPIFACT   = (1u<<25)|(1u<<24)|(1u<<23)|(1u<<22)|(1u<<21)|(1u<<20)|
                (1u<<19)|(1u<<18)|(1u<<17)|(1u<<16);
    SPIFSTA   = 0;
    spi0_cs(1);
}

/*-------------------------------------------------------------------*/
/* L1: SPI 信号驱动（无数据命令）。由 MscCmd 决定行为：               */
/*   Func2 & 0xFF : SPI opcode（WE/SE/BE/CE）                         */
/*   DataAddr     : flash 地址（SE/BE 用，低 24 位）                   */
/* 有数据的命令（RDID/RDSR/READ）必须走 data-in 路径的 L2 函数，      */
/* 因为 L1 无数据阶段，无法把数据回传主机。                            */
/*-------------------------------------------------------------------*/
void l1_func_signal_drive(u32 *p1, u32 *p2)
{
    MSC_CMD *cmd = (MSC_CMD *)p1;
    u8 op = (u8)(cmd->Func2 & 0xffu);
    u32 addr = cmd->DataAddr & 0x00ffffffu;

    switch (op) {
    case FLASH_WRITE_ENABLE:
        spi0_write_enable();
        break;

    case FLASH_SECTOR_ERASE:
    case FLASH_BLOCK_ERASE:
        spi0_write_enable();
        spi0_cs(0);
        spi0_send(op);
        spi0_send_addr(addr);
        spi0_cs(1);
        (void)spi0_wait();
        break;

    case FLASH_CHIP_ERASE:
        spi0_write_enable();
        spi0_cs(0);
        spi0_send(FLASH_CHIP_ERASE);
        spi0_cs(1);
        (void)spi0_wait();
        break;

    default:
        break;
    }
}

/*-------------------------------------------------------------------*/
/* L2: 页编程。data_addr 指向固件已从 USB 收取数据的 prxbuf（256B），  */
/*     param 为 flash 地址。                                          */
/*-------------------------------------------------------------------*/
void l2_func_spi_page_program(u32 data_addr, u32 len, u32 param)
{
    u8 *buf = (u8 *)data_addr;
    u32 i;
    if (len > 256u) len = 256u;
    spi0_write_enable();
    spi0_cs(0);
    spi0_send(FLASH_PAGE_PROGRAM);
    spi0_send_addr(param);
    for (i = 0; i < len; i++)
        spi0_send(buf[i]);
    spi0_cs(1);
    (void)spi0_wait();
}/*-------------------------------------------------------------------*/
/* L2: 读 flash。data_addr 指向固件回传用的 ptxbuf，param 为地址。    */
/*     固件在 L2 返回后由 cb_mem2FIFO 把 ptxbuf 数据发回主机。        */
/*-------------------------------------------------------------------*/
void l2_func_spi_read(u32 data_addr, u32 len, u32 param)
{
    u8 *buf = (u8 *)data_addr;
    u32 i;
    spi0_cs(0);
    spi0_send(FLASH_READ_DATA);
    spi0_send_addr(param);
    for (i = 0; i < len; i++)
        buf[i] = spi0_recv();
    spi0_cs(1);
}

/*-------------------------------------------------------------------*/
/* L2: 读 flash 厂商 ID（RDID 0x9F，3 字节）。param 未用。            */
/*-------------------------------------------------------------------*/
void l2_func_spi_read_id(u32 data_addr, u32 len, u32 param)
{
    u8 *buf = (u8 *)data_addr;
    u32 i;
    spi0_cs(0);
    spi0_send(FLASH_READ_ID);
    for (i = 0; i < len && i < 4; i++)
        buf[i] = spi0_recv();
    spi0_cs(1);
}

/*-------------------------------------------------------------------*/
/* L2: 读 flash 状态寄存器（RDSR 0x05，1 字节）。param 未用。         */
/*-------------------------------------------------------------------*/
void l2_func_spi_read_status(u32 data_addr, u32 len, u32 param)
{
    u8 *buf = (u8 *)data_addr;
    spi0_cs(0);
    spi0_send(FLASH_READ_STATUS);
    buf[0] = spi0_recv();
    spi0_cs(1);
}

/*-------------------------------------------------------------------*/
/* RDID 密度字节 -> 容量（字节）。标准 JEDEC 密度编码：              */
/*   0x14=8Mb(1MB) 0x15=16Mb(2MB) 0x16=32Mb(4MB) 0x17=64Mb(8MB)      */
/*   0x18=128Mb(16MB) 0x19=256Mb(32MB) 0x20=512Mb(64MB)              */
/*   0x21=1Gb(128MB) 0x22=2Gb(256MB) 0x23=4Gb(512MB)                 */
/* 未知密度返回 0。                                                   */
/*-------------------------------------------------------------------*/
static u32 flash_capacity_from_density(u8 d)
{
    switch (d) {
    case 0x11: return 0x00020000u; /* 1Mb  */
    case 0x12: return 0x00040000u; /* 2Mb  */
    case 0x13: return 0x00080000u; /* 4Mb  */
    case 0x14: return 0x00100000u; /* 8Mb  */
    case 0x15: return 0x00200000u; /* 16Mb */
    case 0x16: return 0x00400000u; /* 32Mb */
    case 0x17: return 0x00800000u; /* 64Mb */
    case 0x18: return 0x01000000u; /* 128Mb */
    case 0x19: return 0x02000000u; /* 256Mb */
    case 0x20: return 0x04000000u; /* 512Mb */
    case 0x21: return 0x08000000u; /* 1Gb   */
    case 0x22: return 0x10000000u; /* 2Gb   */
    case 0x23: return 0x20000000u; /* 4Gb   */
    default:   return 0u;
    }
}

/*-------------------------------------------------------------------*/
/* L2: 读 flash 容量（字节，4 字节 LE）。param 未用。                 */
/*     通过 RDID 密度字节解码得到设备端真实 Flash 大小，               */
/*     未知密度返回 0（主机侧回退到默认容量）。                        */
/*-------------------------------------------------------------------*/
void l2_func_spi_read_capacity(u32 data_addr, u32 len, u32 param)
{
    u8 *buf = (u8 *)data_addr;
    u8 id[4];
    u32 i;
    u32 cap;
    spi0_cs(0);
    spi0_send(FLASH_READ_ID);
    for (i = 0; i < 4; i++)
        id[i] = spi0_recv();
    spi0_cs(1);
    cap = flash_capacity_from_density(id[2]);
    if (len > 4) len = 4;
    for (i = 0; i < len; i++)
        buf[i] = (u8)(cap >> (8u * i));
}
