using UpgradeTool.Core.Devices;

namespace UpgradeTool.Core.Tests;

/// <summary>
/// DeviceWatcher 连接门槛（ShouldAttemptConnect）测试：
/// 只有命中目标签名（IsTarget）的真实设备才进入连接握手，普通 U 盘等被跳过。
/// </summary>
public class DeviceWatcherGateTests
{
    private static MscDeviceInfo RealDevice(bool isTarget) =>
        new("\\\\?\\GLOBALROOT\\Device\\Fake", 0x1234, 0x5678, "Buildwin Media-Player",
            VendorId: "Buildwin", ProductId: "Media-Player", IsTarget: isTarget);

    [Fact]
    public void ShouldAttemptConnect_TargetDevice_True()
    {
        Assert.True(DeviceWatcher.ShouldAttemptConnect(RealDevice(isTarget: true)));
    }

    [Fact]
    public void ShouldAttemptConnect_NonTargetDevice_False()
    {
        Assert.False(DeviceWatcher.ShouldAttemptConnect(RealDevice(isTarget: false)));
    }
}
