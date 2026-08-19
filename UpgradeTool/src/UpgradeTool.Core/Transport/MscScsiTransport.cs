using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UpgradeTool.Core.Abstractions;
using UpgradeTool.Core.Interop;
using UpgradeTool.Core.Utilities;

namespace UpgradeTool.Core.Transport;

/// <summary>
/// 真实 USB MSC 传输层：通过 SCSI Pass-Through 向磁盘设备下发厂商 CDB。
/// 使用标准 Windows 存储驱动，无需替换驱动（对比 libusb 方案的产线优势）。
/// CDB 布局与固件 hal_usb_msc.c 的 get_cbw 解析对齐：CDB[0]=OpCode(=CBW 字节 15)...
/// </summary>
public sealed class MscScsiTransport : IFlashTransport
{
    /// <summary>
    /// SCSI 命令最大重试次数（对齐 MPTool：除设备断开（55）外几乎所有错误都重试 10 次）。
    /// </summary>
    private const int MaxRetries = 10;
    private const int RetryDelayMs = 20;
    private const int SenseInfoLength = 26;

    /// <summary>
    /// 同步（非 OVERLAPPED）IOCTL 回退：当 OVERLAPPED 模式连续失败超过此阈值时，
    /// 回退到同步 IO 重试最后一次命令。对齐 MPTool 使用同步 DeviceIoControl 的方式。
    /// </summary>
    private const int MaxOverlappedFailures = 3;
    private int _overlappedFailureCount;

    /// <summary>成功命令日志节流阈值：每此数量的成功命令才记录一条摘要（失败命令始终记录）。</summary>
    private const int SuccessLogThrottle = 50;
    private int _successLogCount;

    /// <summary>
    /// 单条 SCSI 命令的设备执行超时。设备不响应（如处于 Bootloader/Loader 模式，
    /// 不支持应用态 0xCD 通道）时命令会阻塞到超时才返回，因此超时不能过长。
    /// 连接握手阶段由 DeviceConnection.Connect 调短（3s 快速失败）；
    /// 刷写阶段由 FlashService 调长（SPI 块擦除等慢操作需要余量）。
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 刷写阶段关联的取消令牌：取消时会用 CancelIoEx 中断正在执行的 SCSI 命令，
    /// 避免 In-flight 命令阻塞到超时（否则取消按钮无法及时中止）。
    /// 由 FlashService 在刷写前设置，连接握手阶段保持 null（命令超时已调短为 3s）。
    /// </summary>
    public CancellationToken? CancellationToken { get; set; }

    /// <summary>
    /// SCSI 命令日志回调：每条命令执行后回调（方向 / CDB / 长度 / 耗时 / 结果/状态/Sense/错误码）。
    /// 默认 null 不记录；排查问题时可接日志文件，避免刷屏 UI。
    /// </summary>
    public Action<string>? Log { get; set; }

    private readonly object _sync = new();
    private readonly string _devicePath;
    private IntPtr _handle;
    private MscScsiNative.BusAddress _busAddress = MscScsiNative.BusAddress.Default;
    private bool _disposed;

    public MscScsiTransport(string devicePath)
    {
        _devicePath = devicePath;
    }

    public bool IsOpen => _handle != IntPtr.Zero;
    public string DeviceLabel => _devicePath;

    private const int OpenRetryCount = 3;
    private const int OpenRetryDelayMs = 100;

    // 瞬时错误码：设备刚插入未就绪或短暂被占用，值得重试。
    // 121(ERROR_SEM_TIMEOUT) = USB 总线级超时，设备可能因上一命令未处理完而短暂无响应，
    // 31(ERROR_GEN_FAILURE) = 设备 STALL bulk-IN 端点（真机导出曾于固定偏移偶发 STALL，重试可恢复），
    // 重试后通常能恢复（对齐 MPTool：几乎所有错误都重试 10 次，仅 55=设备断开 不重试）。
    private static readonly HashSet<int> TransientErrors = [5, 21, 31, 32, 121, 170];

    public void Open()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsOpen)
                return;

            int lastError = 0;
            for (int attempt = 0; attempt <= OpenRetryCount; attempt++)
            {
                IntPtr handle = MscScsiNative.CreateFile(
                    _devicePath,
                    MscScsiNative.GENERIC_READ | MscScsiNative.GENERIC_WRITE,
                    MscScsiNative.FILE_SHARE_READ | MscScsiNative.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    MscScsiNative.OPEN_EXISTING,
                    MscScsiNative.FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);

                if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                {
                    _handle = handle;
                    // 打开成功后查询真实 SCSI 总线地址：某些 USB 控制器（尤其 xHCI 多 LUN）
                    // 的 TargetId/PathId 不固定为 0/1，硬编码会导致 SCSI 命令失败。
                    // 查询失败时回退到默认值 (0,1,0)，保持向后兼容。
                    _busAddress = MscScsiNative.TryGetBusAddress(handle);
                    return;
                }

                lastError = Marshal.GetLastWin32Error();
                _handle = IntPtr.Zero;

                // 非瞬时错误不重试
                if (!TransientErrors.Contains(lastError) || attempt >= OpenRetryCount)
                    break;

                Thread.Sleep(OpenRetryDelayMs);
            }

            string hint = lastError switch
            {
                5 => ErrorMessages.GetMessage(5),
                21 => ErrorMessages.GetMessage(21),
                _ => ErrorMessages.GetMessage(lastError),
            };
            throw new InvalidOperationException($"无法打开设备 {_devicePath}（错误码: {lastError}）。{hint}");
        }
    }

    public void Close()
    {
        lock (_sync)
        {
            if (_handle != IntPtr.Zero)
            {
                MscScsiNative.CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    public ScsiCommandResult SendCommand(byte[] cdb) =>
        SendScsi(cdb, MscScsiNative.SCSI_IOCTL_DATA_UNSPECIFIED, null);

    public ScsiCommandResult SendDataOut(byte[] cdb, ReadOnlySpan<byte> payload) =>
        SendScsi(cdb, MscScsiNative.SCSI_IOCTL_DATA_OUT, payload.ToArray());

    public ScsiCommandResult SendDataIn(byte[] cdb, int expectedLength) =>
        SendScsi(cdb, MscScsiNative.SCSI_IOCTL_DATA_IN, new byte[expectedLength]);

    private ScsiCommandResult SendScsi(byte[] cdb, byte dataIn, byte[]? data)
    {
        var sw = Stopwatch.StartNew();
        ScsiCommandResult result = SendScsiCore(cdb, dataIn, data);
        sw.Stop();
        // 失败命令始终记录（诊断关键）；成功命令节流——刷写一个固件有数千条命令，
        // 逐条落盘会让会话日志膨胀到数十 MB，每 SuccessLogThrottle 条成功记录一条摘要。
        if (!result.Success)
            Log?.Invoke(DescribeScsiCommand(cdb, dataIn, data, result, sw.ElapsedMilliseconds));
        else if (++_successLogCount >= SuccessLogThrottle)
        {
            _successLogCount = 0;
            Log?.Invoke(DescribeScsiCommand(cdb, dataIn, data, result, sw.ElapsedMilliseconds));
        }
        return result;
    }

    /// <summary>把一条 SCSI 命令汇总为单行日志（方向 / CDB / 长度 / 耗时 / 结果 / 状态 / Sense / 错误码）。</summary>
    private static string DescribeScsiCommand(byte[] cdb, byte dataIn, byte[]? data, ScsiCommandResult r, long elapsedMs)
    {
        string dir = dataIn switch
        {
            MscScsiNative.SCSI_IOCTL_DATA_IN => "IN ",
            MscScsiNative.SCSI_IOCTL_DATA_OUT => "OUT",
            _ => "NONE",
        };
        var sb = new StringBuilder($"[SCSI] {dir} cdb={Convert.ToHexString(cdb, 0, Math.Min(cdb.Length, 16))}");
        if (data != null)
            sb.Append($" len={data.Length}");
        sb.Append($" {elapsedMs}ms");

        if (r.Success)
        {
            if (r.Response != null && r.Response.Length != (data?.Length ?? -1))
                sb.Append($" got={r.Response.Length}"); // 回读字节数与期望不一致（residue）
            sb.Append(" OK");
        }
        else
        {
            sb.Append(" FAIL");
            if (r.ScsiStatus != 0)
            {
                sb.Append($" status=0x{r.ScsiStatus:X2}");
                if (r.Sense is { Length: > 0 })
                    sb.Append($" sense={Convert.ToHexString(r.Sense)}");
            }
            else if (r.ErrorCode != 0)
            {
                sb.Append($" err={r.ErrorCode}({ErrorMessages.GetTitle(r.ErrorCode)})");
            }
            else
            {
                sb.Append(" (unknown)");
            }
        }
        return sb.ToString();
    }

    private ScsiCommandResult SendScsiCore(byte[] cdb, byte dataIn, byte[]? data)
    {
        lock (_sync)
        {
            if (_handle == IntPtr.Zero)
                return new ScsiCommandResult(false, -1, null);

            // 执行前检查取消，避免在锁内再发起新命令
            CancellationToken? ct = CancellationToken;
            ct?.ThrowIfCancellationRequested();

            int cdbLen = Math.Min(cdb.Length, 16);
            int dataLength = data?.Length ?? 0;
            int sptdSize = Marshal.SizeOf<MscScsiNative.ScsiPassThroughDirect>();
            int structureSize = sptdSize + 4 + 32;          // + Filler(4) + SenseBuf(32)
            int senseInfoOffset = sptdSize + 4;
            int dataOffset = structureSize;                  // 数据区在 Sense 缓冲区之后
            int totalSize = structureSize + dataLength;

            int offLength = FieldOffset(nameof(MscScsiNative.ScsiPassThroughDirect.Length));
            int offScsiStatus = FieldOffset(nameof(MscScsiNative.ScsiPassThroughDirect.ScsiStatus));
            int offPathId = FieldOffset(nameof(MscScsiNative.ScsiPassThroughDirect.PathId));
            int offTargetId = FieldOffset(nameof(MscScsiNative.ScsiPassThroughDirect.TargetId));
            int offLun = FieldOffset(nameof(MscScsiNative.ScsiPassThroughDirect.Lun));
            int offCdbLength = FieldOffset(nameof(MscScsiNative.ScsiPassThroughDirect.CdbLength));
            int offSenseInfoLength = FieldOffset(nameof(MscScsiNative.ScsiPassThroughDirect.SenseInfoLength));
            int offDataIn = FieldOffset(nameof(MscScsiNative.ScsiPassThroughDirect.DataIn));
            int offDataTransferLength = FieldOffset(nameof(MscScsiNative.ScsiPassThroughDirect.DataTransferLength));
            int offTimeOutValue = FieldOffset(nameof(MscScsiNative.ScsiPassThroughDirect.TimeOutValue));
            int offDataBuffer = FieldOffset(nameof(MscScsiNative.ScsiPassThroughDirect.DataBuffer));
            int offSenseInfoOffset = FieldOffset(nameof(MscScsiNative.ScsiPassThroughDirect.SenseInfoOffset));
            int offCdb = FieldOffset(nameof(MscScsiNative.ScsiPassThroughDirect.Cdb));

            int retries = MaxRetries;
            bool useSyncFallback = _overlappedFailureCount >= MaxOverlappedFailures;
            do
            {
                IntPtr buffer = Marshal.AllocHGlobal(totalSize);

                try
                {
                    // 只需清零 SPTD 头部 + Filler + Sense 区（structureSize 内字段均须置 0）。
                    // 数据区随后由 Marshal.Copy 填充（DATA_OUT）或由设备回读覆盖（DATA_IN），
                    // 无需逐字节清零，避免刷写数千条命令时反复执行数百万次非托管边界写入。
                    for (int i = 0; i < structureSize; i++)
                        Marshal.WriteByte(buffer, i, 0);

                    Marshal.WriteInt16(buffer, offLength, (short)sptdSize);
                    Marshal.WriteByte(buffer, offScsiStatus, 0);
                    Marshal.WriteByte(buffer, offPathId, _busAddress.PathId);
                    Marshal.WriteByte(buffer, offTargetId, _busAddress.TargetId);
                    Marshal.WriteByte(buffer, offLun, _busAddress.Lun);
                    Marshal.WriteByte(buffer, offCdbLength, (byte)cdbLen);
                    Marshal.WriteByte(buffer, offSenseInfoLength, SenseInfoLength);
                    Marshal.WriteByte(buffer, offDataIn, dataIn);
                    Marshal.WriteInt32(buffer, offDataTransferLength, dataLength);
                    Marshal.WriteInt32(buffer, offTimeOutValue, (int)CommandTimeout.TotalSeconds);

                    if (dataLength > 0)
                        Marshal.WriteIntPtr(buffer, offDataBuffer, IntPtr.Add(buffer, dataOffset));
                    else
                        Marshal.WriteIntPtr(buffer, offDataBuffer, IntPtr.Zero);

                    Marshal.WriteInt32(buffer, offSenseInfoOffset, senseInfoOffset);

                    for (int i = 0; i < cdbLen; i++)
                        Marshal.WriteByte(buffer, offCdb + i, cdb[i]);

                    if (dataLength > 0 && data != null)
                        Marshal.Copy(data, 0, IntPtr.Add(buffer, dataOffset), dataLength);

                    // 当 OVERLAPPED 连续失败超过阈值时，回退到同步 IO（对齐 MPTool 方式）
                    int ioErr;
                    if (useSyncFallback)
                    {
                        bool syncOk = MscScsiNative.DeviceIoControl(
                            _handle,
                            MscScsiNative.IOCTL_SCSI_PASS_THROUGH_DIRECT,
                            buffer, (uint)structureSize,   // 与 OVERLAPPED 分支保持一致：DIRECT 数据经指针，ioctl 缓冲仅需头部
                            buffer, (uint)structureSize,
                            out _,
                            IntPtr.Zero);
                        if (!syncOk)
                        {
                            ioErr = Marshal.GetLastWin32Error();
                            if (ioErr == 55)
                                return new ScsiCommandResult(false, ioErr, null);
                            if (TransientErrors.Contains(ioErr) && retries-- > 0)
                            {
                                Thread.Sleep(RetryDelayMs);
                                continue;
                            }
                            return new ScsiCommandResult(false, ioErr, null);
                        }
                        // 同步成功：继续读取结果
                        _overlappedFailureCount = 0; // 重置计数器
                    }
                    else
                    {
                        // 用 OVERLAPPED + 事件句柄，同步等待但允许 CancelIoEx 中断
                        var overlapped = new MscScsiNative.Overlapped
                        {
                            EventHandle = IntPtr.Zero
                        };
                        overlapped.EventHandle = MscScsiNative.CreateEvent(IntPtr.Zero, true, false, null);
                        try
                        {
                            bool ioInit = MscScsiNative.DeviceIoControl(
                                _handle,
                                MscScsiNative.IOCTL_SCSI_PASS_THROUGH_DIRECT,
                                buffer, (uint)structureSize,
                                buffer, (uint)structureSize,
                                out _,
                                ref overlapped);

                            // 该命令被取消（外层调用方已请求中止）
                            bool cancelled = false;

                            if (!ioInit)
                            {
                                ioErr = Marshal.GetLastWin32Error();
                                if (ioErr == MscScsiNative.ERROR_IO_PENDING)
                                {
                                    // 命令已提交（异步），等待完成或取消
                                    for (;;)
                                    {
                                        uint waitResult = MscScsiNative.WaitForSingleObject(
                                            overlapped.EventHandle, 200);

                                        if (waitResult == MscScsiNative.WAIT_OBJECT_0)
                                        {
                                            break; // 命令完成
                                        }

                                        // 每次超时轮询取消令牌：非阻塞等待，可被取消中断
                                        if (ct?.IsCancellationRequested == true)
                                        {
                                            MscScsiNative.CancelIoEx(_handle, ref overlapped);
                                            // 等待设备栈真正完成（CancelIoEx 后事件会很快被置位）
                                            MscScsiNative.WaitForSingleObject(overlapped.EventHandle, 3000);
                                            cancelled = true;
                                            break;
                                        }
                                    }

                                    // 获取结果（如果已取消则 GetOverlappedResult 返回 ERROR_OPERATION_ABORTED）
                                    uint xfer = 0;
                                    bool gotResult = MscScsiNative.GetOverlappedResult(
                                        _handle, ref overlapped, out xfer, false);

                                    if (cancelled)
                                    {
                                        // 取消是明确的用户意图：抛出 OperationCanceledException，
                                        // 使 FlashService 的 catch(OperationCanceledException) 能正确识别为"已取消"，
                                        // 而非误报为"命令失败（错误码 -1）"。未提供取消令牌时退化为 995(ERROR_OPERATION_ABORTED)。
                                        if (ct != null)
                                            throw new OperationCanceledException("SCSI 命令已被取消。", ct.Value);
                                        return new ScsiCommandResult(false, (int)MscScsiNative.ERROR_OPERATION_ABORTED, null);
                                    }

                                    // 同步失败（DeviceIoControl 返回 false）：转化为等效同步错误码处理
                                    if (!gotResult)
                                    {
                                        ioErr = Marshal.GetLastWin32Error();
                                        // 错误 55 = 设备未连接
                                        if (ioErr == 55)
                                            return new ScsiCommandResult(false, ioErr, null);

                                        // 瞬时错误重试，含 121(ERROR_SEM_TIMEOUT)
                                        if (TransientErrors.Contains(ioErr) && retries-- > 0)
                                        {
                                            _overlappedFailureCount++;
                                            Thread.Sleep(RetryDelayMs);
                                            continue;
                                        }

                                        return new ScsiCommandResult(false, ioErr, null);
                                    }
                                    // 异步成功：继续读取结果
                                    _overlappedFailureCount = 0;
                                }
                                else
                                {
                                    // 同步失败（非 ERROR_IO_PENDING）
                                    // 错误 55 = 设备未连接
                                    if (ioErr == 55)
                                        return new ScsiCommandResult(false, ioErr, null);

                                    // 瞬时错误重试，含 121(ERROR_SEM_TIMEOUT)
                                    if (TransientErrors.Contains(ioErr) && retries-- > 0)
                                    {
                                        _overlappedFailureCount++;
                                        Thread.Sleep(RetryDelayMs);
                                        continue;
                                    }

                                    return new ScsiCommandResult(false, ioErr, null);
                                }
                            }
                            else
                            {
                                // 同步成功（DeviceIoControl 返回 true）
                                _overlappedFailureCount = 0;
                            }
                        }
                        finally
                        {
                            if (overlapped.EventHandle != IntPtr.Zero)
                                MscScsiNative.CloseHandle(overlapped.EventHandle);
                        }
                    }

                    // 读回驱动填充的 SCSI 状态与 sense（区分设备端拒绝与传输层错误）
                    byte scsiStatus = Marshal.ReadByte(buffer, offScsiStatus);
                    int senseLen = Marshal.ReadByte(buffer, offSenseInfoLength);
                    byte[]? sense = null;
                    if (senseLen > 0)
                    {
                        senseLen = Math.Min(senseLen, SenseInfoLength);
                        sense = new byte[senseLen];
                        Marshal.Copy(IntPtr.Add(buffer, senseInfoOffset), sense, 0, senseLen);
                    }

                    // 设备端返回 CHECK CONDITION 等非 GOOD 状态：立即返回并携带状态/sense，
                    // 不重试——诊断构建需要精确暴露第一次失败，避免掩盖真实原因。
                    if (scsiStatus != 0)
                        return new ScsiCommandResult(false, 0, null, scsiStatus, sense);

                    byte[]? response = data;
                    if (dataIn == MscScsiNative.SCSI_IOCTL_DATA_IN && data != null)
                    {
                        // 按实际传输字节数裁剪（residue 检测：设备少传时 Response 缩短）
                        uint actual = (uint)Marshal.ReadInt32(buffer, offDataTransferLength);
                        int copyLen = (int)Math.Min((uint)data.Length, actual);
                        Marshal.Copy(IntPtr.Add(buffer, dataOffset), data, 0, copyLen);
                        response = copyLen == data.Length ? data : data.AsSpan(0, copyLen).ToArray();
                    }

                    return new ScsiCommandResult(true, 0, response);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            } while (true);
        }
    }

    private static int FieldOffset(string fieldName) =>
        Marshal.OffsetOf(typeof(MscScsiNative.ScsiPassThroughDirect), fieldName).ToInt32();

    public void Dispose()
    {
        if (_disposed)
            return;
        Close();
        _disposed = true;
    }
}
