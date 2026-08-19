namespace UpgradeTool.Core.Protocol;

/// <summary>
/// Loader 模式的 Flash 指令集（可配置，替代 Dc503RomProtocol 的硬编码常量）。
/// 默认值 = W25Q32 指令集（与 Dc503RomCommands / FlashLib.ini [12] 一致）。
/// 可从 FlashDeviceSpec 推导（FlashLib.Match9F），也可显式覆盖。
/// </summary>
public sealed record FlashCommandSet(
    uint Read = 0x03,
    uint PageProgram = 0x02,
    uint Erase4K = 0x20,
    uint Erase64K = 0xD8,
    uint EraseChip = 0xC7,
    uint ReadId = 0x9F,
    uint ReadStatus = 0x05)
{
    /// <summary>从 FlashLib 器件表推导指令集；无匹配时用默认（W25Q32）。</summary>
    public static FlashCommandSet FromDevice(FlashDeviceSpec? device) => device is null
        ? new FlashCommandSet()
        : new FlashCommandSet(
            Read: device.Read ?? 0x03,
            PageProgram: device.PageProgram ?? 0x02,
            Erase4K: device.Erase4K ?? 0x20,
            Erase64K: device.Erase64K ?? 0xD8,
            EraseChip: device.EraseChip ?? 0xC7,
            ReadId: 0x9F,
            ReadStatus: device.ReadStatusRegister ?? 0x05);
}

/// <summary>
/// Loader 模式协议的配置：驱动 ELF 镜像（经 DeviceLib.ini 选中）、Loader RAM API 地址、Flash 指令集。
///
/// 驱动/固件函数地址遵循 MPTool"从 ELF 符号表解析、内置常量仅兜底"：
///   RbcMemRwex     = 符号 RBC_mem_rwex_DMA（SHN_ABS，Loader RAM 数据搬运入口，即 0xCB 写 flash 的 Func1）
///   RbcMemRwexBuf  = 符号 RBC_mem_rwex_buf（SHN_ABS，Loader RAM 数据缓冲；不同 loader 版本不同：
///                    ThunderSE=0x4A00 / ThunderBD=0xB200 / ThunderBDPlus=0x15200 / AX327X=0x1A00）
///   l1/l2 入口     = 段内符号（l1_func_spi_init / l1_func_signal_drive / l2_func_spi_page_program ...）
/// 符号缺失或无效（0 / 0xFFFFFFFF 哨兵）时才回退到内置常量。
///
/// 已从 MPTool 逆向 / ThunderSE.elf 符号表确认的事实：
///   - RBC_mem_rwex_CPU/DMA = 0x00100008（Loader 的 mem 读写入口，写 flash 命令的 Func1）；
///   - sigdrv_buf = 0x00004800、param = 0x00004f80（Loader RAM 数据/命令区）；
///   - ThunderSE.elf 符号：l1_func_spi_init=0x24、l1_func_signal_drive=0x74、
///     l2_func_spi_page_program=0x208、l2_func_reset=0x3A0。
///
/// MPTool 真机抓包确认（777.txt 完整设备识别流程）：
///   - 主机需上传 ThunderSE.elf PT_LOAD 段到设备 RAM（4KB，基址 0x0000，每次 1KB）；
///   - SPI init 使用 Func2=0（非 NoL2），使设备从已上传驱动表加载配置；
///   - 依次尝试 0x9F / 0xAB / 0x90 / 0x15 多种 SPI ID 命令。
///   DataAddr 与 ID 读取结果无关（loader 的 L2 SPI 读驱动总是把结果落到 FlashReadBuf(0x04070000)，
///   主机 DataAddr 不影响读回数据）。`1f ff ff ff` 是设备侧真实状态而非工具/协议错误——MPTool 真机抓包
///   中同一命令序列在部分机器（321/555/888.txt）同样返回 `1f ff ff ff` 且 MPTool 放弃烧写；
///   正常机器（123/777.txt）0x9F 返回 `85 60 16 85`（4MB flash），但对 0xAB/0x90 也返回 `1f ff ff xx`。
///
/// 真机抓包确认（6666.txt 固件烧录流程）：
///   - Loader 态 0xCB 命令为 16 字节 CDB（[0]=0xcb, [1..4]=Func1, [5..8]=DataAddr, [9..12]=Func2, [13..15]=Param）；
///   - 写 flash：Func1=RBC_mem_rwex_CPU，DataAddr=RBC_mem_rwex_buf(0x4a00)，Func2=l2_func_spi_page_program(0x208)，
///     Param=flash 偏移，data-out 每块 0x100；
///   - 读 flash / RDID / 擦除：Func1=l1_func_signal_drive(0x74)，DataAddr=FlashReadBuf(0x04070000)，
///     CDB[8]=SPI 命令码（0x03 读 / 0x9F RDID / 0x20 扇区擦 / 0xD8 块擦），CDB[9..11]=24 位大端 flash 地址（signal-drive 布局）。
///     （"Func2=SPI 命令码、DataAddr=flash 地址"是 DC503 应用态 0xCD 通道的布局约定，勿与 Loader 混用。）
///
/// UploadBase 用于把符号（ET_REL 段内偏移）换算成 Loader 固件内的绝对地址。
/// Thunder* 系列 PT_LOAD p_vaddr=0，符号值即绝对地址，UploadBase 保持 0。
/// </summary>
public sealed record LoaderConfig
{
    /// <summary>Loader 固件内符号基址（ThunderSE.elf PT_LOAD p_vaddr=0，符号值即绝对地址）。</summary>
    public const uint DefaultUploadBase = 0x00000000;

    /// <summary>Loader RAM 数据搬运入口（RBC_mem_rwex_CPU，对应应用态 cb_mem_rwex）。</summary>
    public const uint DefaultRbcMemRwex = 0x00100008;

    /// <summary>Loader RAM 数据缓冲（RBC_mem_rwex_buf）。</summary>
    public const uint DefaultRbcMemRwexBuf = 0x00004a00;

    /// <summary>SPI 信号驱动预备命令缓冲（777.txt: NOP 预备命令 DataAddr=0x01030000）。
    /// 仅用于 NOP/无数据命令；与 ID 读取无关（DataAddr 不影响结果）。</summary>
    public const uint DefaultSigdrvBuf = 0x01030000;

    /// <summary>驱动上传分块大小（777.txt: 每次 0x400=1024 字节）。</summary>
    public const int DriverUploadChunkSize = 1024;

    /// <summary>Flash 数据读缓冲（6666.txt: DataAddr=0x04070000）。
    /// loader 的 L2 SPI 读驱动固定使用此地址，主机 DataAddr 仅为约定、不影响结果。</summary>
    public const uint FlashReadBuf = 0x04070000;

    /// <summary>驱动 ELF 文件名（DeviceLib.ini SpiDriverPath；仅用于日志/诊断）。</summary>
    public string DriverName { get; init; } = "ThunderSE.elf";

    /// <summary>驱动镜像（所选驱动 ELF 的解析结果，符号表已含 L1/L2 与 RBC_* 地址）。</summary>
    public required LoaderImage Image { get; init; }

    /// <summary>驱动上传基址（ET_REL 段内偏移 + 此基址 = 入口绝对地址）。</summary>
    public uint UploadBase { get; init; } = DefaultUploadBase;

    /// <summary>数据搬运的 Func1（Loader 的 mem 读写入口，来自 ELF 符号 RBC_mem_rwex_DMA/CPU）。</summary>
    public uint RbcMemRwex { get; init; } = DefaultRbcMemRwex;

    /// <summary>Loader RAM 数据缓冲（页面编程数据落点，来自 ELF 符号 RBC_mem_rwex_buf）。</summary>
    public uint RbcMemRwexBuf { get; init; } = DefaultRbcMemRwexBuf;

    /// <summary>SPI 信号驱动预备命令缓冲（仅 NOP/无数据命令）。</summary>
    public uint SigdrvBuf { get; init; } = DefaultSigdrvBuf;

    /// <summary>FlashLib.ini 器件表（用于 RDID 匹配得到容量与指令集；null 时用内置默认）。</summary>
    public FlashLib? FlashLib { get; init; }

    /// <summary>
    /// 由驱动镜像推导配置：RbcMemRwex/RbcMemRwexBuf 从 ELF 符号表解析（RBC_mem_rwex_DMA/CPU、RBC_mem_rwex_buf），
    /// 缺失或无效（0 / 0xFFFFFFFF）时回退内置常量。
    /// </summary>
    public static LoaderConfig Create(
        LoaderImage image, FlashLib? flashLib = null, uint uploadBase = DefaultUploadBase, string driverName = "ThunderSE.elf")
        => new()
        {
            Image = image,
            FlashLib = flashLib,
            UploadBase = uploadBase,
            DriverName = driverName,
            RbcMemRwex = ResolveRbc(image, "RBC_mem_rwex_DMA", "RBC_mem_rwex_CPU", DefaultRbcMemRwex, uploadBase),
            RbcMemRwexBuf = ResolveRbc(image, "RBC_mem_rwex_buf", null, DefaultRbcMemRwexBuf, uploadBase),
        };

    /// <summary>
    /// 按设备产品串选择驱动 ELF（对齐 MPTool DeviceLib.ini）：查 DeviceLib 匹配 Loader 条目 →
    /// 取 SpiDriverPath → 内嵌加载对应 ELF 并解析符号。无匹配时回退 ThunderSE.elf。
    /// </summary>
    public static LoaderConfig ForProduct(string? productId, FlashLib? flashLib = null)
        => ForProduct(entry: null, productId, flashLib);

    /// <summary>
    /// 按枚举阶段识别出的 DeviceEntry 选择驱动 ELF（对齐 MPTool SearchDeviceID 回填的 SpiDriverPath）。
    /// 优先使用 entry.SpiDriverPath 作为单一数据源；entry 为空（如模拟测试仅传入产品串）时回退到
    /// 按产品串匹配 Loader 条目。无匹配时回退 ThunderSE.elf。
    /// </summary>
    public static LoaderConfig ForProduct(DeviceEntry? entry, string? productId, FlashLib? flashLib = null)
    {
        string driver = !string.IsNullOrWhiteSpace(entry?.SpiDriverPath)
            ? entry!.SpiDriverPath
            : (DeviceLibrary.Embedded.MatchLoader(productId)?.SpiDriverPath ?? "ThunderSE.elf");
        return Create(LoaderImage.LoadEmbedded(driver), flashLib ?? FlashLib.LoadEmbedded(), driverName: driver);
    }

    /// <summary>从 ELF 符号表解析 RBC 地址；符号缺失或值为 0 / 0xFFFFFFFF（无效哨兵）时回退常量。</summary>
    private static uint ResolveRbc(LoaderImage image, string primary, string? alternate, uint fallback, uint loadBase)
    {
        foreach (string name in alternate is null ? new[] { primary } : new[] { primary, alternate })
        {
            if (image.TryResolve(name, loadBase, out uint v) && v != 0 && v != 0xFFFFFFFF)
                return v;
        }
        return fallback;
    }
}
