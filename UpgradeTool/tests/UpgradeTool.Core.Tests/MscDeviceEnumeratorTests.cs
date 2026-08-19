using UpgradeTool.Core.Devices;

namespace UpgradeTool.Core.Tests;

public class MscDeviceEnumeratorTests
{
    [Theory]
    [InlineData("USBSTOR\\DISK&VEN_ABCD&PROD_1234&REV_0100", 0xABCD, 0x1234)]
    [InlineData("USB\\VID_0451&PID_8142&REV_0100", 0x0451, 0x8142)]
    [InlineData("USB\\VID_1FC9&PID_0028", 0x1FC9, 0x0028)]
    public void ParseVidPid_ExtractsVidPid(string hwId, ushort expectedVid, ushort expectedPid)
    {
        var (vid, pid, vidStr, pidStr) = MscDeviceEnumerator.ParseVidPid(new[] { hwId });
        Assert.Equal(expectedVid, vid);
        Assert.Equal(expectedPid, pid);
        Assert.Null(vidStr);
        Assert.Null(pidStr);
    }

    [Fact]
    public void ParseVidPid_NoMatch_ReturnsZero()
    {
        var (vid, pid, vidStr, pidStr) = MscDeviceEnumerator.ParseVidPid(new[] { "ACPI\\PNP0A08" });
        Assert.Equal(0, vid);
        Assert.Equal(0, pid);
        Assert.Null(vidStr);
        Assert.Null(pidStr);
    }

    [Fact]
    public void ParseVidPid_Empty_ReturnsZero()
    {
        var (vid, pid, vidStr, pidStr) = MscDeviceEnumerator.ParseVidPid(Array.Empty<string>());
        Assert.Equal(0, vid);
        Assert.Equal(0, pid);
        Assert.Null(vidStr);
        Assert.Null(pidStr);
    }

    [Fact]
    public void ParseVidPid_StringIdentifiers_ReturnsStrings()
    {
        // BuildWin 设备使用字符串型标识（非十六进制），应通过 VidStr/PidStr 返回。
        var (vid, pid, vidStr, pidStr) =
            MscDeviceEnumerator.ParseVidPid(new[] { "USBSTOR\\DISK&VEN_BUILDWIN&PROD_VIDEO050LOADER&REV_1.00" });
        Assert.Equal(0, vid);
        Assert.Equal(0, pid);
        Assert.Equal("BUILDWIN", vidStr);
        Assert.Equal("VIDEO050LOADER", pidStr);
    }
}
