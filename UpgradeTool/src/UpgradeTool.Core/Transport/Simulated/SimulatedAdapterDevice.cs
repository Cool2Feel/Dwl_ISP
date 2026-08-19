using UpgradeTool.Core.Protocol;

namespace UpgradeTool.Core.Transport.Simulated;

/// <summary>
/// 模拟 AX2005 适配器 + Berry 子设备（测试替身，实现 <see cref="ISimulatedDevice"/>）。
/// 复刻 MPTool AX2005Adapter→BerrySdio 语义：
///   - 适配器驱动经 0xCB L3 上传（Func1=MemReadWrite → 写入适配器内存）、Init/probe_port/probe_dev/tgt_rw
///     各函数按真实符号地址派发（probe_port 返回 &gt;5 表示子设备在位，probe_dev 返回 0xAAAAAAAA 确认 Berry）；
///   - 子设备经 0xCB L3 tgt_rw 上传固件，经 0xCD L2 eeprom_init / bootSgmt_driver_check 识别类型
///     （0x01=EEPROM、0x02=Flash + 字节 1..3 为 ID）。
/// 符号地址从真实驱动 ELF（AXIDEsdspi.elf / AX3233AXIDE_A2.elf）解析，保证检测器下发的就是
/// MPTool 使用的函数地址，测试可端到端验证两阶段子设备检测。
/// </summary>
public sealed class SimulatedAdapterDevice : ISimulatedDevice
{
    private readonly DriverImage _adapterDriver = DriverImage.LoadEmbedded("AXIDEsdspi.elf");
    private readonly DriverImage _childDriver = DriverImage.LoadEmbedded("AX3233AXIDE_A2.elf");
    private readonly byte[] _adapterRam = new byte[0x20000];

    /// <summary>子设备是否连接在适配器上（probe_port/probe_dev 结果来源）。</summary>
    public bool ChildPresent { get; set; }

    /// <summary>子设备类型（bootSgmt_driver_check 结果来源）。</summary>
    public ChildDeviceType ChildKind { get; set; } = ChildDeviceType.Flash;

    /// <summary>子设备 Flash 的 JEDEC ID（bootSgmt_driver_check 返回字节 1..3）。</summary>
    public byte[] FlashId { get; set; } = { 0x85, 0x60, 0x16 };

    /// <summary>最近一次适配器驱动上传的字节数。</summary>
    public int AdapterUploadLength { get; private set; }

    /// <summary>最近一次子设备固件上传的字节数。</summary>
    public int ChildUploadLength { get; private set; }

    public bool AdapterInitialized { get; private set; }

    public bool ChildInitialized { get; private set; }

    private uint MemReadWrite => _adapterDriver.Resolve("MemReadWrite");
    private uint Init => _adapterDriver.Resolve("Init");
    private uint ProbePort => _adapterDriver.Resolve("probe_port");
    private uint ProbeDev => _adapterDriver.Resolve("probe_dev");
    private uint TgtRw => _adapterDriver.Resolve("tgt_rw");
    private uint EepromInit => _childDriver.Resolve("eeprom_init");
    private uint BootSgmtDriverCheck => _childDriver.Resolve("bootSgmt_driver_check");

    public bool Handle(byte[] cdb, byte[]? dataOut, int dataInLength, out byte[] response)
    {
        response = Array.Empty<byte>();
        if (cdb.Length < 1)
            return false;
        byte op = cdb[0];

        if (op == AdapterCommands.OpCode)
        {
            (uint func1, uint dataAddr, _, _) = AdapterCommands.DecodeCdb(cdb);

            if (func1 == MemReadWrite)
            {
                if (dataOut is { Length: > 0 })
                {
                    WriteRam(dataAddr, dataOut);
                    AdapterUploadLength += dataOut.Length;
                    return true;
                }
                if (dataOut == null) // data-in：回读校验
                {
                    response = ReadRam(dataAddr, dataInLength);
                    return response.Length == dataInLength;
                }
                return true; // data-out 0 长度：无操作
            }
            if (func1 == Init && dataOut is null or { Length: 0 })
            {
                AdapterInitialized = true;
                return true;
            }
            if (func1 == ProbePort && dataOut == null)
            {
                response = new byte[Math.Max(dataInLength, 1)];
                response[0] = ChildPresent ? (byte)0x06 : (byte)0x00;
                return true;
            }
            if (func1 == ProbeDev && dataOut == null)
            {
                response = new byte[Math.Max(dataInLength, 4)];
                if (ChildPresent)
                {
                    response[0] = 0xAA;
                    response[1] = 0xAA;
                    response[2] = 0xAA;
                    response[3] = 0xAA;
                }
                return true;
            }
            if (func1 == TgtRw && dataOut is { Length: > 0 })
            {
                ChildUploadLength = dataOut.Length; // 子设备固件上传（到子设备地址 0）
                return true;
            }
            return false;
        }

        if (op == Dc503RomCommands.OpCode)
        {
            // 子设备通道：0xCD L2（经适配器桥接到子设备固件）
            uint trx = (uint)(dataOut?.Length ?? dataInLength);
            Dc503RomCommands.CdbFields f = Dc503RomCommands.DecodeCdb(cdb, trx, dataOut != null ? 0u : 0x80u);

            if (f.Func2 == EepromInit && dataOut == null)
            {
                ChildInitialized = true;
                return true;
            }
            if (f.Func2 == BootSgmtDriverCheck && dataOut == null)
            {
                response = new byte[4];
                response[0] = ChildKind switch
                {
                    ChildDeviceType.Eeprom => 0x01,
                    ChildDeviceType.Flash => 0x02,
                    _ => 0x00,
                };
                if (ChildKind == ChildDeviceType.Flash && FlashId is { Length: >= 3 })
                {
                    response[1] = FlashId[0];
                    response[2] = FlashId[1];
                    response[3] = FlashId[2];
                }
                return true;
            }
            return false;
        }

        return false;
    }

    private void WriteRam(uint addr, byte[] data)
    {
        int off = (int)addr;
        if (off < 0 || off + data.Length > _adapterRam.Length)
            return;
        data.CopyTo(_adapterRam, off);
    }

    private byte[] ReadRam(uint addr, int length)
    {
        int off = (int)addr;
        if (off < 0)
            return Array.Empty<byte>();
        int n = Math.Min(length, _adapterRam.Length - Math.Min(off, _adapterRam.Length));
        if (n <= 0)
            return Array.Empty<byte>();
        var buf = new byte[n];
        Array.Copy(_adapterRam, off, buf, 0, n);
        return buf;
    }
}
