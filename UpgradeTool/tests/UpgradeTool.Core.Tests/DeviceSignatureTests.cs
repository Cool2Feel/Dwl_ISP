using UpgradeTool.Core.Devices;

namespace UpgradeTool.Core.Tests;

/// <summary>
/// 设备签名匹配（DeviceSignature）与描述符信息格式化测试。
/// 匹配规则参考 TimeUpdate 参考项目：INQUIRY 厂商/产品串不区分大小写匹配。
/// </summary>
public class DeviceSignatureTests
{
    [Theory]
    [InlineData("BuildwinMedia-", "Player")]
    [InlineData("Buildwin", "Media-Player")]
    [InlineData("Buildwin", "Minidv")]
    [InlineData("buildwin", "MINIDV")]
    [InlineData("AX3231MP", "Tool")]
    [InlineData("ax3231mp", null)]
    [InlineData(null, "minidv")]
    public void IsTarget_MatchesKnownCameraSignatures(string? vendor, string? product)
    {
        Assert.True(DeviceSignature.IsTarget(vendor, product));
    }

    [Theory]
    [InlineData("SanDisk", "USB 3.2Gen1")]
    [InlineData("Kingston", "DataTraveler")]
    [InlineData("Generic", "Flash Disk")]
    [InlineData("", "")]
    public void IsTarget_RejectsNonCameraDevices(string? vendor, string? product)
    {
        Assert.False(DeviceSignature.IsTarget(vendor, product));
    }

    /// <summary>
    /// DeviceLib.ini 驱动识别（对齐 MPTool SearchDeviceID）：产品未命中内置 pattern 时，
    /// 按设备库 InquiryInfo 匹配识别。Generic Mass-Storage 1.11/1.12（AX3233RP）即此类设备。
    /// </summary>
    [Theory]
    [InlineData("Generic", "Mass-Storage", "1.11")]
    [InlineData("Generic", "Mass-Storage", "1.12")]
    [InlineData("Generic", "Mass-Storage", null)]
    public void IsTarget_DeviceLibEntry_HitsConfigDrivenTarget(string? vendor, string? product, string? revision)
    {
        Assert.True(DeviceSignature.IsTarget(vendor, product, revision));
    }

    [Fact]
    public void IsTarget_AppModeIdentity_MatchViaDeviceLib()
    {
        // 应用态设备（Buildwin Media-Player 1.00，对齐固件 device_inquiry_data）：
        // "buildwin" pattern 已覆盖，且 DeviceLib [3]（AXISP）也能命中。
        Assert.True(DeviceSignature.IsTarget("Buildwin", "Media-Player", "1.00"));
    }

    [Fact]
    public void IsTarget_LoaderIdentity_MatchViaDeviceLib()
    {
        Assert.True(DeviceSignature.IsTarget("BuildWin", "Video050Loader", "1.00"));
        Assert.True(DeviceSignature.IsTarget("BuildWin", "Video060Loader", "1.00"));
    }

    [Fact]
    public void IsTarget_GenericStorage_RevisionMismatch_Rejects()
    {
        // Generic Mass-Storage 需匹配 1.11/1.12 版本（AX3233RP）；其他版本不是本工具目标
        Assert.False(DeviceSignature.IsTarget("Generic", "Mass-Storage", "2.00"));
    }

    [Fact]
    public void IsTarget_CaseInsensitive()
    {
        Assert.True(DeviceSignature.IsTarget("BuIlDwIn", "mEdIa-pLaYeR"));
    }

    [Fact]
    public void Identity_JoinsNonEmptyFields()
    {
        var desc = new StorageDeviceDescriptorInfo(
            IsUsb: true, BusTypeCode: 0x07, RemovableMedia: true,
            VendorId: "Buildwin", ProductId: "Media-Player", ProductRevision: null);
        Assert.Equal("Buildwin Media-Player", desc.Identity);
    }
}
