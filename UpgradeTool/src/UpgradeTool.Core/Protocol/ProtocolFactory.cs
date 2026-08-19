using UpgradeTool.Core.Abstractions;
using UpgradeTool.Core.Devices;

namespace UpgradeTool.Core.Protocol;

/// <summary>
/// 协议工厂：按设备当前所处状态与设备库识别结论选择固件升级协议。
///
/// 对齐参考项目 MPTool：Loader 态是生产主通道——
///   Loader 态设备（产品串含 "loader"）-> 0xCB 下载器通道（LoaderRomProtocol，ThunderSE 驱动，生产主通道）
///   应用态设备（产品串不含 "loader"）-> 0xCD 应用态通道（Dc503RomProtocol，SPI0 stub）
///
/// 适配器类别（AX326X / AXISP / AX3233RP / AX2005Adapter / AX3233Efuse）由 DeviceLib.ini 的
/// ClassInfo 决定，驱动 ELF 由 SpiDriverPath 决定——与 MPTool SearchDev 以 ClassInfo 字符串派发
/// 具体适配器、以 SearchDeviceID 回填的 SpiDriverPath 选驱动一致。识别结论（命中的 DeviceEntry）
/// 由枚举阶段一次性算出并随 MscDeviceInfo 透传，本工厂不再独立重匹配设备库，保证单一数据源。
///
/// 注意：真实连接流程（DeviceConnection.Connect）对应用态设备直接下发 0xDA 切换至 Loader 模式，
/// 不会把应用态设备带到本工厂——因此真实刷写始终走 Loader 通道；本工厂的 0xCD 分支仅服务于
/// 模拟设备测试（SimulatedMscDevice 复刻 0xCD 通道）与无 Loader 环境下的备选路径。
///
/// 0xDA（EnterUpdateModeAsync）在烧录完成后由协议层发送，作为收尾复位，使设备重启加载新固件
/// （对齐 MPTool auto_reset 时调用 DeviceReset）。
/// </summary>
public static class ProtocolFactory
{
    /// <summary>
    /// 按设备信息（含枚举阶段识别出的 DeviceEntry）选择协议。优先使用 DeviceEntry 的
    /// ClassInfo / SpiDriverPath 派发适配器与驱动（对齐 MPTool SearchDev）。
    /// </summary>
    public static IFlashProtocol CreateForDevice(
        IFlashTransport transport,
        MscDeviceInfo info,
        Action<string>? log = null)
        => Create(transport, info.VendorId, info.ProductId, info.MatchedEntry, log);

    public static IFlashProtocol Create(
        IFlashTransport transport,
        string? vendorId,
        string? productId,
        Action<string>? log = null)
        => Create(transport, vendorId, productId, entry: null, log);

    private static IFlashProtocol Create(
        IFlashTransport transport,
        string? vendorId,
        string? productId,
        DeviceEntry? entry,
        Action<string>? log)
    {
        // 设备类型由设备库 ClassInfo 解析（对齐 MPTool SearchDev 按 ClassInfo 派发处理类），
        // 与 DeviceConnection.Connect 的 0xDA/0xCB 路由保持一致；未命中条目时回退产品串子串判断。
        DeviceKind kind = entry?.Kind ?? (DeviceSignature.IsLoader(vendorId, productId) ? DeviceKind.Loader : DeviceKind.Unknown);
        bool loader = kind == DeviceKind.Loader;

        if (loader)
        {
            // 对齐 MPTool：Loader 驱动经 DeviceLib.ini 按设备产品串选中（SpiDriverPath），
            // 驱动/固件函数地址从该 ELF 符号表解析（LoaderConfig.ForProduct）。
            LoaderConfig config = LoaderConfig.ForProduct(entry, productId);
            log?.Invoke($"设备类型 {entry?.KindLabel ?? "Loader"}（产品串 \"{productId ?? "未知"}\"，类别 {entry?.ClassInfo ?? "未知"}）处于 Loader 模式，使用 0xCB 下载通道，驱动: {config.DriverName}（RBC_mem_rwex=0x{config.RbcMemRwex:X8}, RBC_mem_rwex_buf=0x{config.RbcMemRwexBuf:X8}）。");
            return new LoaderRomProtocol(transport, config, log);
        }

        log?.Invoke($"设备类型 {entry?.KindLabel ?? "应用态"}（产品串 \"{productId ?? "未知"}\"，类别 {entry?.ClassInfo ?? "未知"}）处于应用态，使用 0xCD 应用态通道。");
        return new Dc503RomProtocol(transport, log: log);
    }
}
