using System.Diagnostics;
using UpgradeTool.Core.Abstractions;
using UpgradeTool.Core.Devices;
using UpgradeTool.Core.Protocol;
using UpgradeTool.Core.Transport;
using UpgradeTool.Core.Utilities;

namespace UpgradeTool.Core;

public sealed record ExportRunOptions(
    MscDeviceInfo Device,
    string OutputPath,
    DeviceConnection? Connected = null);

public sealed record ExportSessionResult(bool Success, string Summary, FlashStage FinalStage);

/// <summary>
/// 导出固件会话编排（对齐 MPTool ExportSpiCodeToBin）：
/// 复用已连接设备（Loader 态 0xCB 通道 + ThunderSE 驱动）→ 协议层按 512B 分块读回整片 Flash →
/// 写入 .bin 文件 → 报告 CRC32。
/// 传输层与协议层均可插拔：运行流程只连接真实设备（SCSI Pass-Through），
/// 会话由 DeviceWatcher 建好后以 Connected 复用传入。
/// </summary>
public static class ExportService
{
    public static async Task<ExportSessionResult> RunAsync(
        ExportRunOptions options,
        IProgress<FlashProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        using var logFile = new LogFileWriter();
        Action<string> combinedLog = logFile.CombineWith(log);
        var sw = Stopwatch.StartNew();
        combinedLog($"==== 开始导出会话: {options.Device.DisplayName} ====");
        combinedLog($"时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        log?.Invoke($"日志文件: {logFile.FilePath}");

        IFlashTransport? transport = null;
        try
        {
            IFlashProtocol protocol;
            // 优先复用 DeviceWatcher 已建立的连接（stub 已上传、Flash 信息已查询）
            if (options.Connected is not null)
            {
                transport = options.Connected.Transport;
                protocol = options.Connected.Protocol;
                combinedLog($"复用已连接设备: {options.Connected.DisplayName}");
            }
            else
            {
                transport = new MscScsiTransport(options.Device.DevicePath);
                transport.Open();
                combinedLog("设备连接成功。");
                protocol = ProtocolFactory.CreateForDevice(transport, options.Device, log);
            }
            if (transport is MscScsiTransport msc)
            {
                msc.CommandTimeout = TimeSpan.FromSeconds(30);
                msc.CancellationToken = ct;
                msc.Log = logFile.Write;
            }
            combinedLog($"使用协议: {protocol.Name}");
            combinedLog($"导出路径: {options.OutputPath}");

            combinedLog("开始导出整片 Flash...");
            ProtocolResult<ExportInfo> result = await protocol.ExportFirmwareAsync(options.OutputPath, progress, ct).ConfigureAwait(false);
            combinedLog($"[耗时] 导出阶段完成: {sw.ElapsedMilliseconds}ms");
            if (!result.Success)
            {
                combinedLog($"[完成] 导出失败，耗时 {sw.Elapsed.TotalSeconds:0.0}s: {result.Message}");
                return new ExportSessionResult(false, result.Message, FlashStage.Failed);
            }
            combinedLog($"[完成] 导出成功: {result.Value!.Length} 字节，CRC32=0x{result.Value.Crc32:X8}，总耗时 {sw.Elapsed.TotalSeconds:0.0}s");
            return new ExportSessionResult(true, result.Message, FlashStage.Completed);
        }
        catch (OperationCanceledException)
        {
            combinedLog($"[完成] 导出已取消，耗时 {sw.Elapsed.TotalSeconds:0.0}s");
            return new ExportSessionResult(false, "已取消。", FlashStage.Cancelled);
        }
        catch (Exception ex)
        {
            combinedLog($"[完成] 导出失败，耗时 {sw.Elapsed.TotalSeconds:0.0}s: {ex.Message}");
            return new ExportSessionResult(false, ex.Message, FlashStage.Failed);
        }
        finally
        {
            // 仅关闭自建连接；复用的连接由 DeviceWatcher 管理
            if (transport != null && options.Connected is null)
            {
                transport.Close();
                transport.Dispose();
            }
        }
    }
}
