using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Tests;

public class UpdateModeCommandTests
{
    [Fact]
    public void BuildCdb_Returns16ByteCdb_WithOpCodeDaFirst()
    {
        byte[] cdb = UpdateModeCommand.BuildCdb();

        Assert.Equal(16, cdb.Length);
        Assert.Equal(0xDA, cdb[0]);
        Assert.All(cdb.Skip(1), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Cdb_EmbedsIntoCbw_SoFirmwareReadsOpCodeAtByte15()
    {
        // 固件 hal_usb_msc.c get_cbw 从 CBW 字节 15 读取 OpCode（即 CDB[0]）。
        // 主机 MSC 栈把 SCSI CDB 放在 CBW 字节 15..30。
        byte[] cdb = UpdateModeCommand.BuildCdb();

        var cbw = new byte[31];
        cdb.CopyTo(cbw, 15);

        Assert.Equal(0xDA, cbw[15]);
    }

    [Fact]
    public void OpCode_MatchesFirmwareHandler()
    {
        // 固件 scsi_cmd_analysis: MscCmd.OpCode == 0xDA -> cbw_update()
        Assert.Equal(0xDA, UpdateModeCommand.OpCode);
    }
}
