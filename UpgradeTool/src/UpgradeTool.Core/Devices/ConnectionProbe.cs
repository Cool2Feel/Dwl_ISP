using System.Text;
using UpgradeTool.Core.Abstractions;

namespace UpgradeTool.Core.Devices;

/// <summary>
/// SCSI INQUIRY 响应中解析出的设备身份（标准 36 字节响应数据：
/// 字节 8..15 厂商、16..31 产品、32..35 产品版本，均为空格填充的 ASCII）。
/// 这是来自实时 SCSI 通道的权威身份，供连接时对设备做二次识别/校验。
/// </summary>
public sealed record InquiryIdentity(string? VendorId, string? ProductId, string? ProductRevision)
{
    /// <summary>从 SCSI INQUIRY 响应字节解析身份字段；响应为空/过短时返回 null。</summary>
    public static InquiryIdentity? Parse(byte[]? data)
    {
        if (data == null || data.Length < 16)
            return null;
        return new InquiryIdentity(
            ReadField(data, 8, 8),
            ReadField(data, 16, 16),
            data.Length >= 36 ? ReadField(data, 32, 4) : null);
    }

    /// <summary>读取定长空格填充字段：去首尾空白与 0 字节，全空白/全 0 时返回 null。</summary>
    private static string? ReadField(byte[] data, int offset, int length)
    {
        int end = Math.Min(offset + length, data.Length);
        int start = offset;
        while (start < end && (data[start] == 0 || data[start] == 0x20))
            start++;
        int stop = end;
        while (stop > start && (data[stop - 1] == 0 || data[stop - 1] == 0x20))
            stop--;
        if (stop <= start)
            return null;
        return Encoding.ASCII.GetString(data, start, stop - start);
    }
}

/// <summary>
/// 真实设备连接前的传输层自检探针。
/// 用一组标准 SCSI 命令确认 "SCSI Pass-Through 通道可用" 与 "数据输入阶段可用"，
/// 在进入 0xCB/0xCD 厂商命令阶段之前把失败点分离出来——若探针失败说明 SCSI 通道本身不可用
/// （句柄权限/锁定/非 MSC 磁盘），探针通过而厂商命令失败才指向固件厂商通道或 CDB 问题。
/// 同时从 INQUIRY 响应解析实时设备身份（厂商/产品/版本），供连接层做权威识别。
/// </summary>
public static class ConnectionProbe
{
    /// <summary>TEST UNIT READY (0x00)：6 字节 CDB，无数据阶段。</summary>
    public static readonly byte[] TestUnitReadyCdb = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

    /// <summary>INQUIRY (0x12)：6 字节 CDB，数据输入阶段，分配长度 36 字节。</summary>
    public static readonly byte[] InquiryCdb = { 0x12, 0x00, 0x00, 0x00, 0x24, 0x00 };

    /// <summary>INQUIRY 分配长度（CDB[4]=0x24=36）。</summary>
    public const int InquiryAllocationLength = 36;

    /// <summary>依次执行 TUR / INQUIRY，并逐条输出结果。不会抛出异常。</summary>
    public static ProbeReport Run(IFlashTransport transport, Action<string>? log = null)
    {
        ScsiCommandResult? tur = Step(transport, "TEST UNIT READY (0x00)", TestUnitReadyCdb, dataInLength: 0, log);
        ScsiCommandResult? inquiry = Step(transport, "INQUIRY (0x12)", InquiryCdb, dataInLength: InquiryAllocationLength, log);
        return new ProbeReport
        {
            TestUnitReady = tur,
            Inquiry = inquiry,
            // 实时 SCSI INQUIRY 身份：供连接层以权威数据做识别/校验（区别于枚举阶段的驱动描述符）
            InquiryIdentity = InquiryIdentity.Parse(inquiry?.Response),
        };
    }

    private static ScsiCommandResult? Step(
        IFlashTransport transport, string name, byte[] cdb, int dataInLength, Action<string>? log)
    {
        ScsiCommandResult result;
        try
        {
            result = dataInLength > 0
                ? transport.SendDataIn(cdb, dataInLength)
                : transport.SendCommand(cdb);
        }
        catch (Exception ex)
        {
            result = new ScsiCommandResult(false, 0, null);
            log?.Invoke($"[Probe] {name}: 异常 {ex.Message}");
            return result;
        }

        log?.Invoke(result.Success
            ? $"[Probe] {name}: OK"
            : $"[Probe] {name}: FAIL（{result.DescribeError()}）");
        return result;
    }
}

/// <summary>探针结果：逐条 SCSI 命令的状态 + 从 INQUIRY 解析的实时设备身份。</summary>
public sealed class ProbeReport
{
    public ScsiCommandResult? TestUnitReady { get; init; }

    public ScsiCommandResult? Inquiry { get; init; }

    /// <summary>实时 SCSI INQUIRY 解析出的设备身份（厂商/产品/版本）；INQUIRY 失败或响应过短时为 null。</summary>
    public InquiryIdentity? InquiryIdentity { get; init; }

    /// <summary>无数据通道是否可用（TUR 成功）。</summary>
    public bool NoDataChannelOk => TestUnitReady?.Success == true;

    /// <summary>数据输入通道是否可用（INQUIRY 成功）。</summary>
    public bool DataInChannelOk => Inquiry?.Success == true;

    /// <summary>
    /// 传输层整体可用。
    /// 注：只要求 INQUIRY 成功（数据通道可用），TUR 失败不阻断连接。
    /// 相机等厂商命令通道设备没有真实介质，TUR 返回 NOT_READY / MEDIUM NOT PRESENT 属正常行为。
    /// </summary>
    public bool TransportOk => DataInChannelOk;

    public string Summary =>
        $"TUR={(TestUnitReady is null ? "?" : TestUnitReady.Success ? "OK" : "FAIL")}, " +
        $"INQUIRY={(Inquiry is null ? "?" : Inquiry.Success ? "OK" : "FAIL")}" +
        (TestUnitReady?.Success == false ? $"（TUR: {TestUnitReady.DescribeError()}）" : "") +
        (Inquiry?.Success == false ? $"（INQUIRY: {Inquiry.DescribeError()}）" : "");
}
