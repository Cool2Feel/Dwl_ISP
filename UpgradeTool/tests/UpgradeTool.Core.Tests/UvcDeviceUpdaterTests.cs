using System.Runtime.Versioning;
using UpgradeTool.Core.Devices;
using UpgradeTool.Core.Interop;

namespace UpgradeTool.Core.Tests;

[SupportedOSPlatform("windows")]
public class UvcDeviceUpdaterTests
{
/// <summary>
/// UVC 升级命令通道相关测试：
///   1) 关键常量（扩展单元 GUID / 属性 ID / 数据长度 / 节点类型）与 MPTool uvc_dev_if.cpp 严格对齐。
///   2) DeviceWatcher 的 UVC 节流轮询（对齐 MPTool WM_TIMER 的 wait_cnt=5）——
///      每 N 轮轮询才探测一次视频输入设备的 UVC 扩展节点，找到即下发升级命令。
/// </summary>
    // ---- 常量对齐 MPTool ----

    [Fact]
    public void BdGuid_MatchesMPToolUvcDevIf()
    {
        // MPTool: BD_Guid = { 0x9e9590a3, 0xfe3f, 0x4a82, { 0x8c, 0xe8, 0xf7, 0xb0, 0x43, 0xf6, 0x43, 0x67 } }
        Assert.Equal(new Guid("9e9590a3-fe3f-4a82-8ce8-f7b043f64367"), UvcInterop.BdGuid);
    }

    [Fact]
    public void ExtensionNodeType_MatchesKsNodeTypeDevSpecific()
    {
        Assert.Equal(new Guid("a19df336-b3a4-4cf7-a770-33b58866779a"), UvcInterop.KsNodeTypeDevSpecific);
    }

    [Fact]
    public void UpdateCommand_Contract_Is_XuSet_Property0x4_TwoZeroBytes()
    {
        Assert.Equal(0x4u, UvcDeviceUpdater.UpdatePropertyId);
        Assert.Equal(2, UvcDeviceUpdater.UpdateCommandLength);
    }

    // ---- DeviceWatcher 节流（对齐 MPTool wait_cnt）----

    private sealed class FakeUvcUpdater : IUvcUpdater
    {
        public int FindCallCount;
        public int SendCallCount;
        public int NodeToReturn = 3;

        public int FindExtensionNode()
        {
            FindCallCount++;
            return NodeToReturn;
        }

        public bool SendUpdateCommand(int nodeIndex)
        {
            SendCallCount++;
            return true;
        }
    }

    [Fact]
    public async Task DeviceWatcher_UvcPoll_ThrottledByInterval()
    {
        // 间隔 2：第 1 轮立即探测（counter=2），第 2/3 轮跳过，第 4 轮再探测。
        var fake = new FakeUvcUpdater();
        using var watcher = new DeviceWatcher(
            enumerate: () => new List<MscDeviceInfo>(),
            uvcUpdater: fake,
            uvcPollInterval: 2);

        for (int i = 0; i < 4; i++)
            await watcher.ScanNowAsync();

        Assert.Equal(2, fake.FindCallCount);
        Assert.Equal(2, fake.SendCallCount);
    }

    [Fact]
    public async Task DeviceWatcher_UvcPoll_NoNodeFound_DoesNotSend()
    {
        var fake = new FakeUvcUpdater { NodeToReturn = -1 };
        using var watcher = new DeviceWatcher(
            enumerate: () => new List<MscDeviceInfo>(),
            uvcUpdater: fake,
            uvcPollInterval: 1);

        for (int i = 0; i < 3; i++)
            await watcher.ScanNowAsync();

        Assert.Equal(3, fake.FindCallCount); // 无节点时仍按间隔节流探测
        Assert.Equal(0, fake.SendCallCount);
    }

    [Fact]
    public async Task DeviceWatcher_WithoutUvcUpdater_DoesNotThrow()
    {
        using var watcher = new DeviceWatcher(enumerate: () => new List<MscDeviceInfo>());
        await watcher.ScanNowAsync();
        // 不注入 UVC updater 时静默跳过，不影响设备检测主流程
    }
}
