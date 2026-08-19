using System.Text;
using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Transport.Simulated;

/// <summary>
/// 模拟的设备侧：复刻固件 hal_usb_msc.c 的 0xCD 通道语义 + 一块虚拟 SPI NOR flash。
/// 配合 Dc503RomProtocol 使用，可在无硬件时端到端验证真实协议逻辑
/// （stub 上传、cache 维护、RDID、擦除、页写入、回读校验、0xDA 复位）。
///
/// 命令字段与固件 get_cbw 对齐（见 Dc503RomCommands.DecodeCdb）：
///   CbwTrxLength==0            -> 无数据：Func1 直接作为 L1 调用。
///   CbwTrxLength&gt;0 且 CbwFlag==0x80 -> data-in：Func1=cb_mem_rwex 时按 L2(Func2) 路由。
///   CbwTrxLength&gt;0 且 CbwFlag!=0x80 -> data-out：Func1=cb_mem_rwex 时先收数据再调 L2(Func2)。
/// </summary>
public sealed class SimulatedMscDevice : ISimulatedDevice
{
    public const int MaxFlashSize = 4 * 1024 * 1024; // 与 customer.h FLASH_CAPACITY 一致

    public enum DeviceMode
    {
        /// <summary>应用态：0xCD 厂商通道可用。</summary>
        Storage,

        /// <summary>0xDA 复位后（模拟重新枚举/跳 bootloader）。</summary>
        Bootloader,
    }

    public DeviceMode Mode { get; private set; } = DeviceMode.Storage;

    /// <summary>虚拟 SPI NOR flash（未擦除时字节为 0xFF）。</summary>
    public byte[] Flash { get; private set; } = CreateBlankFlash();

    public bool SpiInitialized { get; private set; }

    /// <summary>状态寄存器（stub L2ReadStatus 返回值），bit0=0 表示空闲。测试可注入非零值验证 SR=XX 诊断。</summary>
    public byte StatusRegister { get; set; }

    /// <summary>置位后 flash 读（L2Read）返回逐字节 XOR 0xAA 的数据，模拟"物理容量不足"的灰片，用于测试容量 pattern 检测。</summary>
    public bool FailCapacityPatternTest { get; set; }

    /// <summary>置位后地址 0 的 flash 读（L2Read）首字节被翻转，模拟引导扇区写入失效，用于测试"回读不一致擦除 block 0"路径。</summary>
    public bool CorruptBootSectorRead { get; set; }

    /// <summary>每次 flash 页编程（L2PageProgram）的 data-out 字节数记录，验证末页 0xFF 补齐。</summary>
    public List<int> PageProgramSizes { get; } = new();

    /// <summary>stub 累积上传缓冲区（每次 1KB 分块追加，D-cache 回读时返回完整段）。</summary>
    public byte[]? UploadedStub { get; private set; }

    public byte[] StubCache { get; private set; } = Array.Empty<byte>();

    private readonly StubImage _stub;
    private readonly FirmwareSymbols _symbols;

    public SimulatedMscDevice(StubImage? stub = null, FirmwareSymbols? symbols = null)
    {
        _stub = stub ?? StubImage.LoadEmbedded();
        // 与 Dc503RomProtocol 默认构造同源（LoadDefault 自动发现 order.ini），保证模拟侧与协议侧符号一致
        _symbols = symbols ?? FirmwareSymbols.LoadDefault();
    }

    /// <summary>
    /// 处理一条设备侧命令。
    /// dataOut：data-out 阶段负载（无则 null）；dataInLength：请求的数据输入长度。
    /// </summary>
    public bool Handle(byte[] cdb, byte[]? dataOut, int dataInLength, out byte[] response)
    {
        response = Array.Empty<byte>();

        byte op = cdb.Length > 0 ? cdb[0] : (byte)0;

        // 0xDA：收尾复位（cbw_update），与 0xCD 通道无关
        if (op == UpdateModeCommand.OpCode)
        {
            Mode = DeviceMode.Bootloader;
            return true;
        }

        // 标准 SCSI 命令（ConnectionProbe 探针 / 磁盘在线检查）
        if (op == 0x00) // TEST UNIT READY：无数据，在线即成功
            return true;
        if (op == 0x12) // INQUIRY：返回最小标准查询数据
        {
            response = BuildInquiryResponse(dataInLength);
            return true;
        }

        if (op != Dc503RomCommands.OpCode || Mode != DeviceMode.Storage)
            return false;

        uint cbwTrxLength = (uint)(dataOut?.Length ?? dataInLength);
        uint cbwFlag = dataOut != null ? 0u : 0x80u;
        Dc503RomCommands.CdbFields f = Dc503RomCommands.DecodeCdb(cdb, cbwTrxLength, cbwFlag);

        // 无数据命令：Func1 直接作为 L1 调用
        if (cbwTrxLength == 0)
        {
            if (f.Func1 == _stub.L1SpiInit)
            {
                SpiInitialized = true;
                return true;
            }
            if (f.Func1 == _stub.L1SignalDrive)
                return HandleSignalDrive(f, out response);

            return false;
        }

        // data-out：cb_mem_write -> cb_FIFO2mem(DataAddr,Residue) 然后 L2(Func2)
        if (dataOut != null)
        {
            // Func2=0xffffffff（无 L2）：原始内存写入 = stub 分块上传到 SDRAM 0x020ccec0
            if (f.Func2 == Dc503RomCommands.NoL2)
            {
                var buf = UploadedStub;
                if (buf == null)
                {
                    UploadedStub = (byte[])dataOut.Clone();
                }
                else
                {
                    int oldLen = buf.Length;
                    Array.Resize(ref buf, oldLen + dataOut.Length);
                    dataOut.AsSpan().CopyTo(buf.AsSpan(oldLen));
                    UploadedStub = buf;
                }
                StubCache = new byte[Math.Max(UploadedStub.Length, 1)];
                return true;
            }
            uint len = Math.Min(cbwTrxLength, (uint)dataOut.Length);
            if (f.Func2 == _stub.L2PageProgram)
            {
                if ((long)f.Param + len > Flash.Length)
                    return false;
                dataOut.AsSpan(0, (int)len).CopyTo(Flash.AsSpan((int)f.Param, (int)len));
                PageProgramSizes.Add(dataOut.Length);
                return true;
            }
            return false;
        }

        // data-in：cb_mem_read -> L2(Func2)(DataAddr,Residue,Param) 然后 cb_mem2FIFO 回传
        if (f.Func2 == _symbols.DcacheFlush)
        {
            // 回写缓存：模拟上传后的 SDRAM 内容回读（首块，返回完整 stub 段）
            response = UploadedStub ?? Array.Empty<byte>();
            return true;
        }
        if (f.Func2 == Dc503RomCommands.NoL2)
        {
            // 探针：NoDataAddr 探针返回零填充数据，无论是否已上传 stub
            // 对齐 Dc503RomProtocol.PrepareStubAndQuery 的 0xCD 通道可用性探针
            // （Func1=cb_mem_rwex, DataAddr=NoDataAddr, Func2=NoL2, data-in 4B）。
            // 必须在 UploadedStub 检查之前，因为第二次调用 PrepareStubAndQuery 时
            // UploadedStub 已非空，但 NoDataAddr 仍应作为探针处理而非实际内存读取。
            if (f.DataAddr == Dc503RomCommands.NoDataAddr)
            {
                response = new byte[cbwTrxLength];
                return true;
            }

            // 无 L2 原始内存读（stub 回读的后续分块）：从 SDRAM DataAddr 偏移读取
            if (UploadedStub == null)
                return false;
            int memOff = (int)(f.DataAddr - _stub.LoadBase);
            if (memOff < 0 || memOff >= UploadedStub.Length)
                return false;
            uint len = Math.Min(cbwTrxLength, (uint)(UploadedStub.Length - memOff));
            response = new byte[len];
            Array.Copy(UploadedStub, memOff, response, 0, (int)len);
            return true;
        }
        if (f.Func2 == _symbols.IcacheInit)
        {
            response = new byte[Math.Max((int)cbwTrxLength, 0)];
            return true;
        }
        if (f.Func2 == _stub.L2ReadId)
        {
            response = new byte[] { 0xEF, 0x40, 0x16 }; // 模拟 flash：Winbond W25Q32（4MB，密度 0x16）
            return true;
        }
        if (f.Func2 == _stub.L2ReadCapacity)
        {
            response = BitConverter.GetBytes((uint)MaxFlashSize); // 4 字节 LE
            return true;
        }
        if (f.Func2 == _stub.L2ReadStatus)
        {
            response = new byte[] { StatusRegister }; // WIP 位：0 = 空闲
            return true;
        }
        if (f.Func2 == _stub.L2Read)
        {
            uint len = Math.Min(cbwTrxLength, (uint)(Flash.Length - (long)f.Param));
            if ((long)f.Param + len > Flash.Length || f.Param > Flash.Length)
                return false;
            response = new byte[len];
            Array.Copy(Flash, (int)f.Param, response, 0, (int)len);
            if (FailCapacityPatternTest)
                for (int i = 0; i < response.Length; i++)
                    response[i] ^= 0xAA; // 模拟物理容量异常：读回数据被破坏，容量 pattern 检测应失败
            if (CorruptBootSectorRead && f.Param == 0 && response.Length > 0)
                response[0] ^= 0xFF; // 模拟引导扇区写入失效：地址 0 回读首字节不匹配
            return true;
        }

        return false;
    }

    private bool HandleSignalDrive(Dc503RomCommands.CdbFields f, out byte[] response)
    {
        response = Array.Empty<byte>();
        uint addr = f.DataAddr;
        switch (f.Func2 & 0xFF)
        {
            case Dc503RomCommands.FlashWriteEnable:
                return true;

            case Dc503RomCommands.FlashSectorErase:
                return Erase(addr, Dc503RomProtocol.SectorSize);

            case Dc503RomCommands.FlashBlockErase:
                return Erase(addr, Dc503RomProtocol.BlockSize);

            case Dc503RomCommands.FlashChipErase:
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

    private static byte[] CreateBlankFlash() => Enumerable.Repeat((byte)0xFF, MaxFlashSize).ToArray();

    /// <summary>最小标准 INQUIRY 数据（直接访问块设备），不足分配长度时补零。</summary>
    private static byte[] BuildInquiryResponse(int allocationLength)
    {
        byte[] data = new byte[Math.Max(allocationLength, 36)];
        data[0] = 0x00; // Peripheral device type: 直接访问块设备
        data[2] = 0x02; // Version
        data[3] = 0x02; // Response data format
        Encoding.ASCII.GetBytes("SIMU  MSC").CopyTo(data, 8);
        return data;
    }
}
