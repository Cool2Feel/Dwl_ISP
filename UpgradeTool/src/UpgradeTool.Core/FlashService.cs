using System.Diagnostics;
using UpgradeTool.Core.Abstractions;
using UpgradeTool.Core.Devices;
using UpgradeTool.Core.Protocol;
using UpgradeTool.Core.Transport;
using UpgradeTool.Core.Utilities;

namespace UpgradeTool.Core;

public sealed record FlashRunOptions(
    MscDeviceInfo Device,
    FirmwareImage? Firmware,
    bool VerifyAfterDownload = true,
    DeviceConnection? Connected = null,
    bool EraseAll = false,
    bool RunCapacityPatternTest = true,
    bool PatchBootChecksum = true,
    bool AutoReset = true);

public sealed record FlashSessionResult(bool Success, string Summary, FlashStage FinalStage);

/// <summary>
/// 刷写会话编排：
/// 复用已连接设备（Loader 态 0xCB 通道 + ThunderSE 驱动）→ 下载固件 → 回读校验 → 0xDA 收尾复位。
/// 架构事实：连接阶段（DeviceConnection.Connect）已把应用态设备 0xDA 切换至 Loader 模式，
/// 0xDA（EnterUpdateMode）是下载完成后的复位/重启步骤，不是下载的前置步骤。
/// 传输层与协议层均可插拔：运行流程只连接真实设备（SCSI Pass-Through），
/// 会话由 DeviceWatcher 建好后以 Connected 复用传入。
/// </summary>
public sealed class FlashService
{
    private readonly FlashRunOptions _options;
    private readonly IProgress<FlashProgress>? _progress;
    private readonly Action<string>? _log;

    /// <summary>上次报告的阶段，用于在阶段切换时打印阶段日志。</summary>
    private FlashStage _lastStage = FlashStage.Idle;

    public FlashService(FlashRunOptions options, IProgress<FlashProgress>? progress = null, Action<string>? log = null)
    {
        _options = options;
        _progress = progress;
        _log = log;
    }

    public static Task<FlashSessionResult> RunAsync(
        FlashRunOptions options,
        IProgress<FlashProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken ct = default)
        => new FlashService(options, progress, log).RunCoreAsync(ct);

    private void Report(Action<string> combinedLog, FlashStage stage, int percent, string message)
        => ReportCore(combinedLog, stage, percent, message);

    /// <summary>进度报告统一入口：阶段切换打印 [阶段] 行，随后打印消息并转发进度到 UI。</summary>
    private void ReportCore(Action<string>? logger, FlashStage stage, int percent, string message)
    {
        if (stage != _lastStage)
        {
            logger?.Invoke($"[阶段] {stage} ({percent}%)");
            _lastStage = stage;
        }
        logger?.Invoke(message);
        _progress?.Report(new FlashProgress(stage, percent, message));
    }

    private async Task<FlashSessionResult> RunCoreAsync(CancellationToken ct)
    {
        // 创建日志文件写入器，自动保存到 logs/ 目录
        using var logFile = new LogFileWriter();
        Action<string> combinedLog = logFile.CombineWith(_log);
        var sw = Stopwatch.StartNew();
        combinedLog($"==== 开始刷写会话: {_options.Device.DisplayName} ====");
        combinedLog($"时间戳: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        _log?.Invoke($"日志文件: {logFile.FilePath}");

        IFlashTransport? transport = null;
        IFlashProtocol? protocol = null;
        try
        {
            // 优先复用 DeviceWatcher 已建立的连接（stub 已上传、Flash 信息已查询）
            if (_options.Connected is not null)
            {
                transport = _options.Connected.Transport;
                protocol = _options.Connected.Protocol;
                Report(combinedLog, FlashStage.OpeningDevice, 5, $"复用已连接设备: {_options.Connected.DisplayName}");
            }
            else
            {
                transport = CreateTransport();
                transport.Open();
                Report(combinedLog, FlashStage.OpeningDevice, 5, "设备连接成功。");
                protocol = CreateProtocol(transport);
            }
            // 握手阶段用短命令超时（快速失败），刷写阶段恢复较长超时（SPI 块擦除较慢）
            if (transport is MscScsiTransport msc)
            {
                msc.CommandTimeout = TimeSpan.FromSeconds(30);
                // 挂接会话取消令牌：取消时用 CancelIoEx 中断 in-flight SCSI 命令，避免阻塞到超时
                msc.CancellationToken = ct;
                // SCSI 命令级日志写入会话日志文件（逐条命令方向/CDB/耗时/结果，方便排查，不刷 UI）
                msc.Log = logFile.Write;
            }
            combinedLog($"使用协议: {protocol.Name}");
            combinedLog($"会话配置: 设备路径={_options.Device.DevicePath}, " +
                        $"固件={(_options.Firmware?.FilePath ?? "(仅进入升级模式)")}, " +
                        $"固件大小={(_options.Firmware?.Length ?? 0)}B, " +
                        $"固件CRC32={(_options.Firmware?.Crc32 ?? 0):X8}, " +
                        $"回读校验={_options.VerifyAfterDownload}, 整片擦除={_options.EraseAll}, " +
                        $"容量检测={_options.RunCapacityPatternTest}, 补启动校验和={_options.PatchBootChecksum}, " +
                        $"自动复位={_options.AutoReset}");

            // 1) 下载固件（0xCD 应用态通道，无需先进入升级模式）
            if (_options.Firmware != null)
            {
                FirmwareImage firmware = _options.Firmware;

                // 1a) 启动扇区校验和（对齐 MPTool 会重算 BLDR 头 byte 8）
                //     打补丁后的镜像同时用于下载与回读校验，保证两阶段字节一致。
                if (_options.PatchBootChecksum)
                {
                    FirmwareImage patched = BootSector.Patch(firmware);
                    if (!ReferenceEquals(patched, firmware))
                    {
                        string flagNote = BootSector.NoChecksum(firmware.Data)
                            ? "（当前固件 NO_CHKSUM=1，bootloader 不校验该字节，仅为流程对等）"
                            : "";
                        combinedLog($"启动扇区校验和已更新: byte8=0x{firmware.Data[8]:X2} -> 0x{patched.Data[8]:X2} {flagNote}");
                        firmware = patched;
                    }
                    else if (BootSector.HasBootSector(firmware.Data))
                    {
                        combinedLog($"启动扇区校验和无需更新（byte8=0x{firmware.Data[8]:X2} 已是期望值）。");
                    }
                }
                combinedLog($"固件: {firmware.FilePath} ({firmware.Length} 字节, CRC32={firmware.Crc32:X8})");

                if (protocol is IFlashCapacityInfo cap && cap.EffectiveCapacity() > 0)
                    combinedLog($"设备 Flash 容量: {new FlashInfo(Array.Empty<byte>(), cap.EffectiveCapacity()).CapacityText}");

                IProgress<FlashProgress> downloadProgress = new ProgressAdapter(_progress, FlashStage.Downloading, from: 5, to: 85, logFile.Write);
                var stDownload = Stopwatch.StartNew();
                ProtocolResult download = await protocol.DownloadFirmwareAsync(
                    firmware, downloadProgress, ct,
                    new FlashDownloadOptions(
                        EraseAll: _options.EraseAll,
                        RunCapacityPatternTest: _options.RunCapacityPatternTest)).ConfigureAwait(false);
                stDownload.Stop();
                combinedLog($"[耗时] 下载阶段完成: {stDownload.ElapsedMilliseconds}ms");
                if (!download.Success)
                {
                    Report(combinedLog, FlashStage.Failed, 100, download.Message);
                    return new FlashSessionResult(false, download.Message, FlashStage.Failed);
                }
                Report(combinedLog, FlashStage.Downloading, 85, download.Message);

                // 2) 回读校验
                if (_options.VerifyAfterDownload)
                {
                    IProgress<FlashProgress> verifyProgress = new ProgressAdapter(_progress, FlashStage.Verifying, from: 85, to: 100, logFile.Write);
                    var stVerify = Stopwatch.StartNew();
                    ProtocolResult verify = await protocol.VerifyFirmwareAsync(firmware, verifyProgress, ct).ConfigureAwait(false);
                    stVerify.Stop();
                    combinedLog($"[耗时] 校验阶段完成: {stVerify.ElapsedMilliseconds}ms");
                    if (!verify.Success)
                    {
                        Report(combinedLog, FlashStage.Failed, 100, verify.Message);
                        return new FlashSessionResult(false, verify.Message, FlashStage.Failed);
                    }
                    Report(combinedLog, FlashStage.Verifying, 100, verify.Message);
                }
            }

            // 3) 0xDA 收尾复位（跳 bootloader / 重启，应用新固件）
            //    对齐 MPTool AUTORESET=1：默认开启；关闭时跳过复位，便于调试保持设备在线
            if (_options.AutoReset)
            {
                Report(combinedLog, FlashStage.EnteringUpdateMode, 100, "下发 0xDA 复位设备...");
                var stReset = Stopwatch.StartNew();
                ProtocolResult reset = await protocol.EnterUpdateModeAsync(ct).ConfigureAwait(false);
                stReset.Stop();
                combinedLog($"[耗时] 复位阶段完成: {stReset.ElapsedMilliseconds}ms");
                if (!reset.Success)
                {
                    Report(combinedLog, FlashStage.Failed, 100, reset.Message);
                    return new FlashSessionResult(false, reset.Message, FlashStage.Failed);
                }
                Report(combinedLog, FlashStage.EnteringUpdateMode, 100, reset.Message);
            }
            else
            {
                Report(combinedLog, FlashStage.Completed, 100, "已跳过复位（自动复位已关闭）。");
            }

            Report(combinedLog, FlashStage.Completed, 100, "刷写完成。");
            combinedLog($"[完成] 刷写成功，总耗时 {sw.Elapsed.TotalSeconds:0.0}s");
            return new FlashSessionResult(true, "刷写完成。", FlashStage.Completed);
        }
        catch (OperationCanceledException)
        {
            Report(combinedLog, FlashStage.Cancelled, 100, "已取消。");
            combinedLog($"[完成] 刷写已取消，耗时 {sw.Elapsed.TotalSeconds:0.0}s");
            return new FlashSessionResult(false, "已取消。", FlashStage.Cancelled);
        }
        catch (Exception ex)
        {
            Report(combinedLog, FlashStage.Failed, 100, $"刷写失败: {ex.Message}");
            combinedLog($"[完成] 刷写失败，耗时 {sw.Elapsed.TotalSeconds:0.0}s: {ex.Message}");
            return new FlashSessionResult(false, ex.Message, FlashStage.Failed);
        }
        finally
        {
            // 仅关闭自建连接；复用的连接由 DeviceWatcher 管理
            if (transport != null && _options.Connected is null)
            {
                transport.Close();
                transport.Dispose();
            }
        }
    }

    private IFlashTransport CreateTransport() => new MscScsiTransport(_options.Device.DevicePath);

    private IFlashProtocol CreateProtocol(IFlashTransport transport)
        => ProtocolFactory.CreateForDevice(transport, _options.Device);

    /// <summary>
    /// 将协议层 0-100 的进度映射到会话总体百分比区间，并向 UI 节流转发。
    /// 下载/校验按 256B 页/512B 块逐条上报：若每条都经 Progress&lt;T&gt;/Dispatcher 投递到 UI 线程，
    /// 4MB 固件会累积约 2.5 万条消息洪泛界面导致卡死不实时（对齐导出路径已有注释的同类问题）。
    /// 因此仅在整数百分比前进时转发一次，末条强制上报收尾；日志文件同样按 5% 节流。
    /// </summary>
    private sealed class ProgressAdapter(
        IProgress<FlashProgress>? inner, FlashStage stage, int from, int to, Action<string>? fileLog = null)
        : IProgress<FlashProgress>
    {
        private int _lastLoggedPercent = -10;
        private int _lastReportedPercent = -1;

        public void Report(FlashProgress value)
        {
            int percent = from + (value.Percent * (to - from)) / 100;

            // 进度节流：百分比每跨越 5% 才写一行日志文件，避免逐页/逐块刷屏
            if (fileLog != null && (percent - _lastLoggedPercent >= 5 || percent <= _lastLoggedPercent - 5))
            {
                fileLog($"[进度] {stage} {percent}%: {value.Message}");
                _lastLoggedPercent = percent;
            }

            // UI 节流：仅整数百分比前进时转发到 UI（末条强制上报），避免 Dispatcher 消息洪泛卡死界面。
            // 同时要求单调递增：擦除/异常回退路径（如整片擦除失败回退逐块）上报的百分比低于已报值
            // 时丢弃，避免界面进度条倒退（末条 100% 强制上报除外）。
            if (percent > _lastReportedPercent || value.Percent >= 100)
            {
                _lastReportedPercent = percent;
                inner?.Report(new FlashProgress(stage, percent, value.Message));
            }
        }
    }
}
