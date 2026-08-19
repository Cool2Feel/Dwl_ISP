using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Tests;

/// <summary>
/// LoaderConfig 驱动选择与符号解析测试：
/// 对齐 MPTool"DeviceLib.ini 选驱动 + ELF 符号表解析地址"，验证 RbcMemRwex/RbcMemRwexBuf
/// 随驱动 ELF 变化（而非写死），以及无效符号（0 / 0xFFFFFFFF）回退内置常量。
/// </summary>
public class LoaderConfigTests
{
    [Fact]
    public void ForProduct_SelectsThunderSe_ForVideo050Loader()
    {
        LoaderConfig config = LoaderConfig.ForProduct("BuildWinVideo050Loader");

        Assert.Equal("ThunderSE.elf", config.DriverName);
        Assert.Equal(0x00100008u, config.RbcMemRwex);    // 来自 ELF RBC_mem_rwex_DMA
        Assert.Equal(0x00004A00u, config.RbcMemRwexBuf); // 来自 ELF RBC_mem_rwex_buf
        Assert.Equal(0x24u, config.Image.Resolve("l1_func_spi_init", config.UploadBase));
        Assert.Equal(0x208u, config.Image.Resolve("l2_func_spi_page_program", config.UploadBase));
    }

    [Fact]
    public void ForProduct_SelectsThunderBd_ForVideo060Loader()
    {
        // 不同 loader 版本的 RBC_mem_rwex_buf 不同（0xB200），必须从 ELF 符号表解析而非写死
        LoaderConfig config = LoaderConfig.ForProduct("BuildWinVideo060Loader");

        Assert.Equal("ThunderBD.elf", config.DriverName);
        Assert.Equal(0x00100008u, config.RbcMemRwex);
        Assert.Equal(0x0000B200u, config.RbcMemRwexBuf);
    }

    [Fact]
    public void ForProduct_SelectsThunderBdPlus_ForVideo070Loader()
    {
        LoaderConfig config = LoaderConfig.ForProduct("BuildWinVideo070Loader");

        Assert.Equal("ThunderBDPlus.elf", config.DriverName);
        Assert.Equal(0x00100008u, config.RbcMemRwex);
        Assert.Equal(0x00015200u, config.RbcMemRwexBuf);
    }

    [Fact]
    public void ForProduct_NoMatch_FallsBackToThunderSe()
    {
        LoaderConfig config = LoaderConfig.ForProduct(null);

        Assert.Equal("ThunderSE.elf", config.DriverName);
        Assert.Equal(0x00004A00u, config.RbcMemRwexBuf);

        // 非 loader 设备产品串也不应命中 loader 驱动选择
        LoaderConfig generic = LoaderConfig.ForProduct("Generic Mass-Storage");
        Assert.Equal("ThunderSE.elf", generic.DriverName);
    }

    [Fact]
    public void Create_DerivesRbcFromElfSymbols()
    {
        // AX326X（应用态驱动 ELF）：RBC_mem_rwex_DMA=0x10008 / RBC_mem_rwex_buf=0x41A00
        LoaderConfig config = LoaderConfig.Create(LoaderImage.LoadEmbedded("AX326X.elf"), driverName: "AX326X.elf");

        Assert.Equal(0x00010008u, config.RbcMemRwex);
        Assert.Equal(0x00041A00u, config.RbcMemRwexBuf);
    }

    [Fact]
    public void Create_InvalidRbcBufSymbol_FallsBackToDefault()
    {
        // AX3233 的 RBC_mem_rwex_buf = 0xFFFFFFFF（无效哨兵）→ 回退内置常量 0x4A00；
        // RBC_mem_rwex_DMA = 0x113858 有效 → 采用符号值
        LoaderConfig config = LoaderConfig.Create(LoaderImage.LoadEmbedded("AX3233.elf"), driverName: "AX3233.elf");

        Assert.Equal(0x00113858u, config.RbcMemRwex);
        Assert.Equal(LoaderConfig.DefaultRbcMemRwexBuf, config.RbcMemRwexBuf);
    }
}
