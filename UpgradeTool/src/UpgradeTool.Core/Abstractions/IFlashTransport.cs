namespace UpgradeTool.Core.Abstractions;

using UpgradeTool.Core.Utilities;

/// <summary>
/// SCSI 数据阶段方向（与 ntddscsi.h 的 SCSI_IOCTL_DATA_* 对齐）。
/// </summary>
public enum ScsiDataDirection : byte
{
    DataOut = 0,
    DataIn = 1,
    Unspecified = 2,
}

/// <summary>
/// 一条 SCSI 命令的执行结果。
/// Success = DeviceIoControl 成功 且设备返回 GOOD（ScsiStatus==0）。
/// ErrorCode 为 Win32 错误码（ioctl 失败时非 0）；ScsiStatus 为设备返回的 SCSI 状态
/// （CHECK CONDITION 等非 GOOD 时非 0，通常伴随 Sense 数据）。
/// </summary>
public sealed record ScsiCommandResult(
    bool Success,
    int ErrorCode,
    byte[]? Response,
    byte ScsiStatus = 0,
    byte[]? Sense = null)
{
    /// <summary>将失败原因格式化为可读文本（SCSI 状态优先于 Win32 错误码）。</summary>
    public string DescribeError() =>
        Success
            ? "OK"
            : ScsiStatus != 0
                ? $"SCSI 状态 0x{ScsiStatus:X2}" +
                  (Sense is { Length: > 0 } ? $"，Sense: {Convert.ToHexString(Sense)}" : "")
                : $"错误码 {ErrorCode}：{ErrorMessages.GetTitle(ErrorCode)}（{ErrorMessages.GetAction(ErrorCode)}）";
}

/// <summary>
/// USB MSC 传输层抽象。
/// 真实实现走 SCSI Pass-Through（标准 Windows 驱动，无需替换驱动）；
/// 模拟实现走 SimulatedMscTransport，用于无硬件时端到端验证全流程。
/// </summary>
public interface IFlashTransport : IDisposable
{
    bool IsOpen { get; }
    string DeviceLabel { get; }

    /// <summary>打开设备句柄。</summary>
    void Open();

    /// <summary>发送一条无数据阶段的 SCSI CDB（如 0xDA 进入升级模式）。</summary>
    ScsiCommandResult SendCommand(byte[] cdb);

    /// <summary>发送一条带数据输出阶段的 SCSI CDB（向设备写数据）。</summary>
    ScsiCommandResult SendDataOut(byte[] cdb, ReadOnlySpan<byte> payload);

    /// <summary>发送一条带数据输入阶段的 SCSI CDB（从设备读数据）。</summary>
    ScsiCommandResult SendDataIn(byte[] cdb, int expectedLength);

    void Close();
}
