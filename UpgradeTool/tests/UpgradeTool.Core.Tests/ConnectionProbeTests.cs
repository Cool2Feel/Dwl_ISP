using System.Text;
using UpgradeTool.Core.Abstractions;
using UpgradeTool.Core.Devices;
using UpgradeTool.Core.Transport.Simulated;

namespace UpgradeTool.Core.Tests;

/// <summary>
/// 连接探针（ConnectionProbe）与 SCSI 结果描述相关测试：
///   - 模拟设备（标准 MSC 磁盘语义）上探针应全部通过。
///   - 无数据 / 数据输入通道单独失败时，报告应正确反映并携带 SCSI 状态/sense。
///   - ScsiCommandResult.DescribeError 的格式化。
/// </summary>
public class ConnectionProbeTests
{
    [Fact]
    public void Run_OnSimulatedDevice_ReportsChannelsOk()
    {
        var device = new SimulatedMscDevice();
        var transport = new SimulatedMscTransport(device);

        ProbeReport report = ConnectionProbe.Run(transport);

        Assert.True(report.NoDataChannelOk);
        Assert.True(report.DataInChannelOk);
        Assert.True(report.TransportOk);
    }

    [Fact]
    public void Run_OnSimulatedDevice_ParsesInquiryIdentity()
    {
        // 模拟设备 INQUIRY 在字节 8 返回 "SIMU  MSC"（9 字符，跨厂商字段边界）：
        // 按 SCSI 规范厂商字段为字节 8..15（8 字符），应解析出 "SIMU  MS"，产品字段首字符 "C"。
        var device = new SimulatedMscDevice();
        var transport = new SimulatedMscTransport(device);

        ProbeReport report = ConnectionProbe.Run(transport);

        Assert.NotNull(report.InquiryIdentity);
        Assert.Equal("SIMU  MS", report.InquiryIdentity!.VendorId);
        Assert.Equal("C", report.InquiryIdentity.ProductId);
    }

    [Fact]
    public void Parse_InquiryIdentity_ExtractsFields()
    {
        // 标准 SCSI INQUIRY 36 字节响应：厂商(8..15)/产品(16..31)/版本(32..35)，空格填充 ASCII
        byte[] data = new byte[36];
        Encoding.ASCII.GetBytes("ACME  ".PadRight(8)).CopyTo(data, 8);       // 厂商，含前导/尾随空格
        Encoding.ASCII.GetBytes("Widget 123".PadRight(16)).CopyTo(data, 16);  // 产品
        Encoding.ASCII.GetBytes("1.00".PadRight(4)).CopyTo(data, 32);         // 版本

        InquiryIdentity identity = InquiryIdentity.Parse(data)!;

        Assert.NotNull(identity);
        Assert.Equal("ACME", identity.VendorId);
        Assert.Equal("Widget 123", identity.ProductId);
        Assert.Equal("1.00", identity.ProductRevision);
    }

    [Fact]
    public void Parse_InquiryIdentity_ShortOrEmptyResponse_ReturnsNull()
    {
        Assert.Null(InquiryIdentity.Parse(null));
        Assert.Null(InquiryIdentity.Parse([]));
        Assert.Null(InquiryIdentity.Parse(new byte[15])); // 不足 16 字节无法解析
    }

    [Fact]
    public void Parse_InquiryIdentity_AllSpaces_ReturnsNull()
    {
        byte[] data = new byte[36];
        Array.Fill(data, (byte)0x20); // 全空格填充

        InquiryIdentity identity = InquiryIdentity.Parse(data)!;

        Assert.NotNull(identity);
        Assert.Null(identity.VendorId);
        Assert.Null(identity.ProductId);
        Assert.Null(identity.ProductRevision);
    }

    [Fact]
    public void Run_WhenNoDataChannelFails_TransportStillOk()
    {
        // 相机等厂商命令通道设备无真实介质时 TUR 返回 NOT READY 属正常，不阻断连接
        var transport = new FailingProbeTransport(failTur: true, failInquiry: false);

        ProbeReport report = ConnectionProbe.Run(transport);

        Assert.False(report.NoDataChannelOk);
        Assert.True(report.TransportOk);  // TUR 失败不阻断
        Assert.True(report.DataInChannelOk);
        Assert.Equal(0x02, report.TestUnitReady!.ScsiStatus);
        Assert.NotNull(report.TestUnitReady.Sense);
    }

    [Fact]
    public void Run_WhenDataInChannelFails_TransportNotOk()
    {
        var transport = new FailingProbeTransport(failTur: false, failInquiry: true);

        ProbeReport report = ConnectionProbe.Run(transport);

        Assert.True(report.NoDataChannelOk);
        Assert.False(report.DataInChannelOk);
        Assert.False(report.TransportOk);
    }

    [Fact]
    public void Run_LogsEachStep()
    {
        var transport = new FailingProbeTransport(failTur: true, failInquiry: true);
        var logs = new List<string>();

        ConnectionProbe.Run(transport, logs.Add);

        Assert.Contains(logs, l => l.Contains("TEST UNIT READY") && l.Contains("FAIL"));
        Assert.Contains(logs, l => l.Contains("INQUIRY") && l.Contains("FAIL"));
        Assert.Contains(logs, l => l.Contains("SCSI 状态 0x02"));
    }

    [Fact]
    public void DescribeError_PrefersScsiStatusOverWin32Error()
    {
        var scsiFail = new ScsiCommandResult(false, 0, null, ScsiStatus: 0x02, Sense: new byte[] { 0x70, 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x0A });
        Assert.Contains("0x02", scsiFail.DescribeError());
        Assert.Contains("700005000000000A", scsiFail.DescribeError());

        var win32Fail = new ScsiCommandResult(false, 55, null);
        Assert.Contains("55", win32Fail.DescribeError());

        Assert.Equal("OK", new ScsiCommandResult(true, 0, null).DescribeError());
    }

    /// <summary>可编程失败注入的探针传输层（TUR=0x00，INQUIRY=0x12）。</summary>
    private sealed class FailingProbeTransport : IFlashTransport
    {
        private readonly bool _failTur;
        private readonly bool _failInquiry;

        public FailingProbeTransport(bool failTur, bool failInquiry)
        {
            _failTur = failTur;
            _failInquiry = failInquiry;
        }

        public bool IsOpen => true;
        public string DeviceLabel => "fake";

        public void Open() { }

        public void Close() { }

        public ScsiCommandResult SendCommand(byte[] cdb)
        {
            if (_failTur && cdb.Length >= 1 && cdb[0] == 0x00)
                return Fail();
            return new ScsiCommandResult(true, 0, null);
        }

        public ScsiCommandResult SendDataOut(byte[] cdb, ReadOnlySpan<byte> payload) =>
            new(true, 0, null);

        public ScsiCommandResult SendDataIn(byte[] cdb, int expectedLength)
        {
            if (_failInquiry && cdb.Length >= 1 && cdb[0] == 0x12)
                return Fail();
            return new ScsiCommandResult(true, 0, new byte[expectedLength]);
        }

        public void Dispose() { }

        private static ScsiCommandResult Fail() =>
            new(false, 0, null, ScsiStatus: 0x02, Sense: new byte[] { 0x70, 0x00, 0x05, 0x00, 0x00, 0x00 });
    }
}
