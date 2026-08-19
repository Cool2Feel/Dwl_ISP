using UpgradeTool.Core.Abstractions;

namespace UpgradeTool.Core.Transport.Simulated;

/// <summary>
/// 模拟传输层：无需硬件即可端到端验证全流程。
/// 将 SCSI CDB 转交给 ISimulatedDevice 处理，模拟"主机 SCSI 栈 + 固件 get_cbw"。
/// 兼容应用态（SimulatedMscDevice，0xCD）与 Loader 态（SimulatedLoaderDevice，0xCB）。
/// </summary>
public sealed class SimulatedMscTransport : IFlashTransport
{
    private readonly ISimulatedDevice _device;
    private bool _isOpen;

    public SimulatedMscTransport(ISimulatedDevice device)
    {
        _device = device;
    }

    public ISimulatedDevice Device => _device;

    public bool IsOpen => _isOpen;
    public string DeviceLabel => "模拟设备 (Simulated MSC)";

    public void Open() => _isOpen = true;

    public void Close() => _isOpen = false;

    public ScsiCommandResult SendCommand(byte[] cdb)
    {
        bool ok = _device.Handle(cdb, null, 0, out byte[] response);
        return new ScsiCommandResult(ok, ok ? 0 : 1, response.Length > 0 ? response : null);
    }

    public ScsiCommandResult SendDataOut(byte[] cdb, ReadOnlySpan<byte> payload)
    {
        bool ok = _device.Handle(cdb, payload.ToArray(), 0, out byte[] response);
        return new ScsiCommandResult(ok, ok ? 0 : 1, response.Length > 0 ? response : null);
    }

    public ScsiCommandResult SendDataIn(byte[] cdb, int expectedLength)
    {
        bool ok = _device.Handle(cdb, null, expectedLength, out byte[] response);
        return new ScsiCommandResult(ok, ok ? 0 : 1, ok ? response : null);
    }

    public void Dispose() => Close();
}
