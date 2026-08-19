using System.Text;
using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Transport.Simulated;

/// <summary>
/// 模拟的 Loader（下载器）态设备：复刻 Loader bootloader 的 0xCB 厂商通道语义 + 一块虚拟 SPI NOR flash。
///
/// 命令语义对齐真机抓包（777.txt 设备识别 + 6666.txt 固件烧录 + mptool-810-1.txt）：
///   - 驱动上传：data-out，Func1=RBC_mem_rwex，Func2=NoL2，DataAddr=RAM 偏移（1KB/块）；
///   - SPI0 init：无数据命令，Func1=l1_func_spi_init(0x24)，Func2=0；
///   - NOP 预备：无数据命令，Func1=l1_func_signal_drive(0x74)，CDB[8]=SPI 命令码=0x00；
///   - RDID/RES/ManufacturerID：data-in，Func1=l1_func_signal_drive(0x74)，CDB[8]=SPI 命令码（0x9F/0xAB/0x90）；
///   - flash 读：data-in，Func1=l1_func_signal_drive(0x74)，CDB[8]=SPI Read(0x03)，CDB[9..11]=24 位大端 flash 地址；
///   - flash 写：data-out，Func1=RBC_mem_rwex(0x00100008)，DataAddr=RBC_mem_rwex_buf(0x4a00)，
///     Func2=l2_func_spi_page_program(0x208)，Param=flash 偏移，data-out 每块 0x100；
///     或绕过 L2 Param=0 bug 的 signal-drive data-out：Func1=l1_func_signal_drive(0x74)，
///     CDB[8]=SPI 0x02（页编程），CDB[9..11]=24 位大端 flash 地址，data-out 写入 flash；
///   - 擦除：无数据命令，Func1=l1_func_signal_drive(0x74)，CDB[8]=擦除命令码（0x20/0xD8），CDB[9..11]=24 位大端 flash 地址。
/// </summary>
public sealed class SimulatedLoaderDevice : ISimulatedDevice
{
    public const int MaxFlashSize = 16 * 1024 * 1024; // 匹配 FlashLib.ini [20] EF 40 18 → 16MB

    /// <summary>虚拟 SPI NOR flash（未擦除时字节为 0xFF）。</summary>
    public byte[] Flash { get; private set; } = Enumerable.Repeat((byte)0xFF, MaxFlashSize).ToArray();

    /// <summary>设备 RAM 中的驱动镜像（模拟上传后回读校验）。</summary>
    public byte[] DriverRam { get; } = new byte[16 * 1024];

    public bool SpiInitialized { get; private set; }

    /// <summary>是否已收到驱动上传（模拟设备接受但不实际处理）。</summary>
    public bool DriverUploaded { get; private set; }

    /// <summary>是否收到收尾复位（0xCB 或 0xDA）。</summary>
    public bool ResetRequested { get; private set; }

    /// <summary>状态寄存器（RDSR 0x05 返回值），bit0=0 表示空闲。测试可注入非零值验证 SR=XX 诊断。</summary>
    public byte StatusRegister { get; set; }

    /// <summary>置位后 SPI 读（0x03）返回逐字节 XOR 0xAA 的数据，模拟"物理容量不足"的灰片，用于测试容量 pattern 检测。</summary>
    public bool FailCapacityPatternTest { get; set; }

    /// <summary>置位后地址 0 的 flash 读（0x03）首字节被翻转，模拟引导扇区写入失效，用于测试"回读不一致擦除 block 0"路径。</summary>
    public bool CorruptBootSectorRead { get; set; }

    /// <summary>每次 flash 页编程（l2_func_spi_page_program）的 data-out 字节数记录，验证末页 0xFF 补齐。</summary>
    public List<int> PageProgramSizes { get; } = new();

    private readonly uint _rbcMemRwex;
    private readonly uint _l1SpiInit;
    private readonly uint _l1SignalDrive;
    private readonly uint _l2PageProgram;
    private readonly uint _l2FuncReset;

    public SimulatedLoaderDevice(LoaderImage? image = null)
    {
        LoaderImage img = image ?? LoaderImage.LoadEmbedded();
        _rbcMemRwex = LoaderConfig.DefaultRbcMemRwex;
        _l1SpiInit = img.Resolve("l1_func_spi_init", LoaderConfig.DefaultUploadBase);
        _l1SignalDrive = img.Resolve("l1_func_signal_drive", LoaderConfig.DefaultUploadBase);
        _l2PageProgram = img.Resolve("l2_func_spi_page_program", LoaderConfig.DefaultUploadBase);
        _l2FuncReset = img.Resolve("l2_func_reset", LoaderConfig.DefaultUploadBase);
    }

    public bool Handle(byte[] cdb, byte[]? dataOut, int dataInLength, out byte[] response)
    {
        response = Array.Empty<byte>();

        byte op = cdb.Length > 0 ? cdb[0] : (byte)0;

        // 0xDA：收尾复位（cbw_update），与 0xCB 通道无关
        if (op == UpdateModeCommand.OpCode)
        {
            ResetRequested = true;
            return true;
        }

        // 标准 SCSI 命令（ConnectionProbe 探针 / 磁盘在线检查）
        if (op == 0x00) // TEST UNIT READY：无数据，在线即成功
            return true;
        if (op == 0x12) // INQUIRY：返回带 Loader 产品串的查询数据
        {
            response = BuildInquiryResponse(dataInLength);
            return true;
        }

        if (op != LoaderRomCommands.OpCode && op != Dc503RomCommands.OpCode)
            return false;

        uint cbwTrxLength = (uint)(dataOut?.Length ?? dataInLength);
        uint cbwFlag = dataOut != null ? 0u : 0x80u;
        Dc503RomCommands.CdbFields f = Dc503RomCommands.DecodeCdb(cdb, cbwTrxLength, cbwFlag);

        // 无数据命令：Func1 直接作为 L1 调用
        if (cbwTrxLength == 0)
        {
            if (f.Func1 == _l1SpiInit)
            {
                SpiInitialized = true;
                return true;
            }
            if (f.Func1 == _l1SignalDrive)
                return HandleSignalDrive(cdb, out response);

            // 0xCB 复位命令（DeviceReset）：Func1=0, Func2=l2_func_reset, Param=0x4
            // 对齐 MPTool AX326X::DeviceReset / LoaderRomCommands.BuildDeviceResetCdb。
            // 该命令无数据阶段，固件侧 CbwTrxLength==0 分支不匹配 0xf0/0xf1 子命令，
            // 模拟层直接按语义处理。
            if (f.Func1 == 0 && f.Func2 == _l2FuncReset)
            {
                ResetRequested = true;
                return true;
            }

            return false;
        }

        // data-out：RBC_mem_rwex（驱动上传 / L2 页编程）或 signal-drive 直接 SPI 页编程
        if (dataOut != null)
        {
            // c) signal-drive data-out：绕过 L2 Param=0 bug 的 SPI 页编程（0x02）
            //    直接解析 CDB[8]=SPI 命令码，CDB[9..11]=24 位大端 flash 地址，data-out 写入 flash。
            if (f.Func1 == _l1SignalDrive)
            {
                byte spiCmd = cdb.Length > 8 ? cdb[8] : (byte)0;
                if (spiCmd == 0x02) // Page Program
                {
                    uint sigAddr = cdb.Length >= 12
                        ? ((uint)cdb[9] << 16) | ((uint)cdb[10] << 8) | cdb[11]
                        : 0u;
                    if (sigAddr + dataOut.Length > Flash.Length)
                        return false;
                    dataOut.AsSpan().CopyTo(Flash.AsSpan((int)sigAddr, dataOut.Length));
                    PageProgramSizes.Add(dataOut.Length);
                    return true;
                }
                // 其他 signal-drive data-out 命令不支持
                return false;
            }

            if (f.Func1 != _rbcMemRwex)
                return false;

            // a) 驱动上传：Func2=NoL2，写入 DriverRam 供回读校验
            if (f.Func2 == LoaderRomCommands.NoL2)
            {
                if (f.DataAddr + dataOut.Length > DriverRam.Length)
                    return false;
                dataOut.AsSpan().CopyTo(DriverRam.AsSpan((int)f.DataAddr, dataOut.Length));
                DriverUploaded = true;
                return true;
            }

            // b) flash 页写入（L2 方式）
            // 注意：固件 l2_func_spi_page_program 内部将 Param 左移 8 位得到字节地址（flash_addr = Param << 8）。
            // 对齐 MPTool SpiWrite：Param = (WriteAddr >> 8) | enc，模拟器需匹配固件行为。
            long flashAddr = (long)f.Param << 8;
            if (flashAddr + dataOut.Length > Flash.Length)
                return false;
            dataOut.AsSpan().CopyTo(Flash.AsSpan((int)flashAddr, dataOut.Length));
            PageProgramSizes.Add(dataOut.Length);
            return true;
        }

        // data-in：两种情况
        //   a) 驱动回读（777.txt L179-306）：Func1=RBC_mem_rwex，Func2=NoL2，DataAddr=RAM 偏移 -> 从 DriverRam 读
        //   b) SPI Flash 读取：Func1=l1_func_signal_drive(0x74)，Func2 字段=SPI 命令码 + flash 地址
        if (f.Func1 == _rbcMemRwex && f.Func2 == LoaderRomCommands.NoL2)
        {
            // 驱动回读
            if (f.DataAddr + cbwTrxLength > DriverRam.Length)
                return false;
            response = new byte[cbwTrxLength];
            Array.Copy(DriverRam, (int)f.DataAddr, response, 0, (int)cbwTrxLength);
            return true;
        }

        // data-in：Func1=l1_func_signal_drive(0x74)
        // MPTool 真机抓包 mptool-810-1.txt 确认：CDB[8]=SPI 命令码，CDB[9..11]=24 位大端 flash 地址。
        if (f.Func1 == _l1SignalDrive)
        {
            byte spiCmd = cdb.Length > 8 ? cdb[8] : (byte)0;
            uint flashAddr = cdb.Length >= 12
                ? ((uint)cdb[9] << 16) | ((uint)cdb[10] << 8) | cdb[11]
                : 0u;
            // RDID：SPI 0x9F / RES 0xAB / Manufacturer ID 0x90（各返回 3 字节 ID + 1 字节填充）
            if (spiCmd == 0x9F || spiCmd == 0xAB || spiCmd == 0x90)
            {
                response = new byte[] { 0xEF, 0x40, 0x18, 0x00 }; // Winbond W25Q128（16MB）
                return true;
            }
            // 读状态寄存器：SPI 0x05（返回状态值，bit0=0 表示空闲）
            if (spiCmd == 0x05)
            {
                response = new byte[] { StatusRegister, 0x00, 0x00, 0x00 };
                return true;
            }
            // flash 读：SPI 0x03（读数据 @ flashAddr，长度 = cbwTrxLength）
            if (spiCmd == 0x03)
            {
                uint len = Math.Min(cbwTrxLength, (uint)(Flash.Length - (long)flashAddr));
                if (flashAddr > Flash.Length || (long)flashAddr + len > Flash.Length)
                    return false;
                response = new byte[len];
                Array.Copy(Flash, (int)flashAddr, response, 0, (int)len);
                if (FailCapacityPatternTest)
                    for (int i = 0; i < response.Length; i++)
                        response[i] ^= 0xAA; // 模拟物理容量异常：读回数据被破坏，容量 pattern 检测应失败
                if (CorruptBootSectorRead && flashAddr == 0 && response.Length > 0)
                    response[0] ^= 0xFF; // 模拟引导扇区写入失效：地址 0 回读首字节不匹配
                return true;
            }
            return false;
        }

        return false;
    }

    private bool HandleSignalDrive(byte[] cdb, out byte[] response)
    {
        response = Array.Empty<byte>();
        if (cdb.Length < 12)
            return false;
        // MPTool 真机抓包 mptool-810-1.txt 确认：CDB[8]=SPI 命令码，CDB[9..11]=24 位大端 flash 地址。
        byte spiCmd = cdb[8];
        uint addr = ((uint)cdb[9] << 16) | ((uint)cdb[10] << 8) | cdb[11];
        switch (spiCmd)
        {
            case 0x00: // NOP 预备命令（777.txt: 同步 SPI 总线，无操作）
                return true;

            case 0x06: // WriteEnable：无操作
                return true;

            case 0x01: // WriteStatusRegister：接受但不实际处理（模拟器 SR 始终为 0x00）
                return true;

            case 0x20: // SectorErase（4KB）
                return Erase(addr, 4096);

            case 0xD8: // BlockErase（64KB）
                return Erase(addr, 65536);

            case 0xC7: // ChipErase
                Array.Fill(Flash, (byte)0xFF);
                return true;

            default:
                return false;
        }
    }

    private bool Erase(uint addr, int size)
    {
        if (addr >= Flash.Length)
            return false;
        int len = Math.Min(size, Flash.Length - (int)addr);
        Array.Fill(Flash, (byte)0xFF, (int)addr, len);
        return true;
    }

    /// <summary>最小标准 INQUIRY 数据，产品串带 Loader 标记（供 ProtocolFactory 识别）。</summary>
    private static byte[] BuildInquiryResponse(int allocationLength)
    {
        byte[] data = new byte[Math.Max(allocationLength, 36)];
        data[0] = 0x00; // Peripheral device type: 直接访问块设备
        data[2] = 0x02; // Version
        data[3] = 0x02; // Response data format
        Encoding.ASCII.GetBytes("BuildWinVideo050Loader").CopyTo(data, 8);
        return data;
    }
}
