namespace UpgradeTool.Core.Transport.Simulated;

/// <summary>
/// 模拟设备侧语义（测试替身）：处理一条 SCSI 命令并返回响应。
/// 真实传输层把 CDB 下发给固件（get_cbw），模拟层把 CDB 转交给实现该接口的设备。
/// SimulatedMscDevice（应用态 0xCD）与 SimulatedLoaderDevice（Loader 态 0xCB）都实现此接口，
/// 因此 SimulatedMscTransport 可复用，无需为 Loader 态新建传输层。
/// </summary>
public interface ISimulatedDevice
{
    /// <summary>
    /// 处理一条设备侧命令。
    /// dataOut：data-out 阶段负载（无则 null）；dataInLength：请求的数据输入长度。
    /// 返回 true 表示命令成功；成功且有数据输入时通过 response 回传。
    /// </summary>
    bool Handle(byte[] cdb, byte[]? dataOut, int dataInLength, out byte[] response);
}
