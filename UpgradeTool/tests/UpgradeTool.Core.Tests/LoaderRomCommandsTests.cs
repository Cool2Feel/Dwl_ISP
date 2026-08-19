using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Tests;

/// <summary>
/// Loader 模式 0xCB 命令通道的 CDB 编解码测试。
/// 字节布局与 0xCD 相同（同一 get_cbw 解析器），因此用 Dc503RomCommands.DecodeCdb 交叉验证。
/// </summary>
public class LoaderRomCommandsTests
{
    [Fact]
    public void BuildCdb_OpCodeIs0xCb()
    {
        byte[] cdb = LoaderRomCommands.BuildCdb(0, 0, 0, 0);

        Assert.Equal(16, cdb.Length);
        Assert.Equal(LoaderRomCommands.OpCode, cdb[0]);
    }

    [Fact]
    public void BuildCdb_RoundTripsThroughDecodeCdb()
    {
        const uint func1 = 0x00100008;      // RBC_mem_rwex_CPU
        const uint dataAddr = 0x020ccec0;   // SDRAM 上传基址
        const uint func2 = 0xFFFFFFFF;      // 无 L2（原始 RAM 写）
        const uint param = 0x123456;        // 24 位

        byte[] cdb = LoaderRomCommands.BuildCdb(func1, dataAddr, func2, param);
        Dc503RomCommands.CdbFields f = Dc503RomCommands.DecodeCdb(cdb, 0, 0x80);

        Assert.Equal(func1, f.Func1);
        Assert.Equal(dataAddr, f.DataAddr);
        Assert.Equal(func2, f.Func2);
        Assert.Equal(param, f.Param);
    }

    [Fact]
    public void BuildCdb_ParamIsLimitedTo24Bits()
    {
        // Param 超过 24 位时应截断高位（固件按 CDB[13..15] 重组）
        byte[] cdb = LoaderRomCommands.BuildCdb(0, 0, 0, 0xFF_00_00_00);
        Dc503RomCommands.CdbFields f = Dc503RomCommands.DecodeCdb(cdb, 0, 0);

        Assert.Equal(0u, f.Param);
    }

    [Fact]
    public void BuildCdb_PageProgramCommand_CarriesEntryAndFlashOffset()
    {
        // 与 LoaderRomProtocol 页写入命令一致：Func1=RBC_mem_rwex，DataAddr=哨兵，Func2=L2 页编程，Param=偏移
        byte[] cdb = LoaderRomCommands.BuildCdb(
            LoaderConfig.DefaultRbcMemRwex,
            LoaderRomCommands.NoDataAddr,
            0x020cd0c8, // l2_func_spi_page_program(0x208) + UploadBase(0x020ccec0)
            0x1000);
        Dc503RomCommands.CdbFields f = Dc503RomCommands.DecodeCdb(cdb, 256, 0);

        Assert.Equal(LoaderConfig.DefaultRbcMemRwex, f.Func1);
        Assert.Equal(LoaderRomCommands.NoDataAddr, f.DataAddr);
        Assert.Equal(0x020cd0c8u, f.Func2);
        Assert.Equal(0x1000u, f.Param);
    }
}
