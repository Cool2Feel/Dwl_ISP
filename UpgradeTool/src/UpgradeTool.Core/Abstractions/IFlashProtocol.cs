using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Abstractions;

public sealed record ProtocolResult(bool Success, string Message)
{
    public static ProtocolResult Ok(string message) => new(true, message);
    public static ProtocolResult Fail(string message) => new(false, message);
}

/// <summary>带返回值的协议结果。</summary>
public sealed record ProtocolResult<T>(bool Success, string Message, T? Value)
{
    public static ProtocolResult<T> Ok(T value, string message) => new(true, message, value);
    public static ProtocolResult<T> Fail(string message) => new(false, message, default);
}

/// <summary>提供有效 Flash 容量（供日志/容量校验的公共访问，协议无关）。</summary>
public interface IFlashCapacityInfo
{
    uint EffectiveCapacity();
}

/// <summary>导出结果：导出的 .bin 文件路径、字节数与 CRC32 校验值。</summary>
public sealed record ExportInfo(string FilePath, long Length, uint Crc32);

/// <summary>
/// 固件升级协议抽象（可插拔）。
/// 生产主通道为 Loader 态 0xCB（LoaderRomProtocol + ThunderSE 驱动）；应用态设备在连接时
/// 已由 DeviceConnection 下发 0xDA 切换至 Loader 模式，因此真实刷写始终走 Loader 通道。
/// Dc503RomProtocol（0xCD 应用态通道 + SDRAM SPI stub）保留为备选/无 Loader 环境，以及
/// 模拟设备（SimulatedMscDevice）复刻同一协议用于无硬件测试。
/// 注意顺序：Download/Verify 在 Loader 态执行，EnterUpdateMode（0xDA）是收尾复位。
/// </summary>
public interface IFlashProtocol
{
    string Name { get; }

    /// <summary>查询设备端 Flash 信息（RDID + 容量）。连接后调用以识别设备。</summary>
    Task<ProtocolResult<FlashInfo>> GetFlashInfoAsync(CancellationToken ct);

    /// <summary>下发进入升级模式命令（0xDA）。</summary>
    Task<ProtocolResult> EnterUpdateModeAsync(CancellationToken ct);

    /// <summary>下载固件到设备。options 为空时使用默认行为（容量 pattern 检测开、不整片擦除）。</summary>
    Task<ProtocolResult> DownloadFirmwareAsync(
        FirmwareImage firmware, IProgress<FlashProgress>? progress, CancellationToken ct, FlashDownloadOptions? options = null);

    /// <summary>回读校验固件。</summary>
    Task<ProtocolResult> VerifyFirmwareAsync(FirmwareImage firmware, IProgress<FlashProgress>? progress, CancellationToken ct);

    /// <summary>
    /// 导出整片 Flash 到文件（读回所有容量字节，不进行任何补丁/校验和修改）。
    /// 对齐 MPTool ExportSpiCodeToBin：导出长度 = Flash 容量（整片导出）。
    /// </summary>
    Task<ProtocolResult<ExportInfo>> ExportFirmwareAsync(
        string outputPath, IProgress<FlashProgress>? progress, CancellationToken ct);
}
