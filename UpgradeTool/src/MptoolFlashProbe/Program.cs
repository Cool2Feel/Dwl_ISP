using UpgradeTool.Core.Devices;
using UpgradeTool.Core.Transport;
using UpgradeTool.Core.Protocol;
using static System.Console;

WriteLine("=" .PadRight(70, '='));
WriteLine("  MPTool Flash ID 探测器模拟器");
WriteLine("  精确模拟 MPTool 的 0x9F RDID 读取序列");
WriteLine("=" .PadRight(70, '='));
WriteLine();

// ── 1. 枚举 USB MSC 设备 ──
WriteLine(">>> [1/6] 枚举 USB MSC 设备...");
var devices = MscDeviceEnumerator.Enumerate(WriteLine);
if (devices.Count == 0)
{
    WriteLine("! 未发现任何 USB 磁盘设备。请确认设备已连接且程序以管理员权限运行。");
    return 1;
}

WriteLine();
for (int i = 0; i < devices.Count; i++)
{
    var d = devices[i];
    WriteLine($"  [{i}] {d.DisplayName}");
    WriteLine($"      路径: {d.DevicePath}");
}
WriteLine();

// ── 2. 用户选择设备 ──
Write(">>> [2/6] 请选择设备编号 [0]: ");
string? input = ReadLine();
int index = 0;
if (!string.IsNullOrWhiteSpace(input))
{
    int.TryParse(input, out index);
    if (index < 0 || index >= devices.Count)
    {
        WriteLine($"! 无效编号，默认使用 [0]。");
        index = 0;
    }
}

var device = devices[index];
WriteLine($"  已选择: {device.DisplayName}");
WriteLine($"  路径:   {device.DevicePath}");
WriteLine();

// ── 3. 打开设备 ──
WriteLine(">>> [3/6] 打开设备 SCSI 通道...");
using var transport = new MscScsiTransport(device.DevicePath);
transport.CommandTimeout = TimeSpan.FromSeconds(5);

try
{
    transport.Open();
}
catch (Exception ex)
{
    WriteLine($"! 打开设备失败: {ex.Message}");
    WriteLine("  提示：请以管理员身份运行本程序。");
    return 1;
}
WriteLine("  OK");
WriteLine();

// ── 4. SCSI 通道自检 ──
WriteLine(">>> [4/6] SCSI 通道自检 (TUR + INQUIRY)...");
var probe = ConnectionProbe.Run(transport, WriteLine);
if (!probe.TransportOk)
{
    WriteLine("! SCSI 通道不可用，这很可能不是目标设备。");
    return 1;
}
WriteLine("  SCSI 通道正常。");
WriteLine();

// ── 5. 加载 ThunderSE.elf 驱动 ──
WriteLine(">>> [5/6] 加载 ThunderSE.elf 驱动...");
LoaderImage image;
try
{
    image = LoaderImage.LoadEmbedded();
}
catch (Exception ex)
{
    WriteLine($"! 加载驱动镜像失败: {ex.Message}");
    return 1;
}
WriteLine($"  驱动大小: {image.Segment.Length} 字节");
WriteLine($"  符号: l1_func_spi_init=0x{image.Resolve("l1_func_spi_init", 0):X}");
WriteLine($"  符号: l1_func_signal_drive=0x{image.Resolve("l1_func_signal_drive", 0):X}");
WriteLine();

// ── 6. 上传驱动到设备 RAM ──
WriteLine(">>> 上传驱动到设备 RAM (基址 0x0000, 分块 1KB)...");
byte[] segment = image.Segment;
const int chunkSize = 1024;
uint uploadBase = 0x00000000;
uint rbcMemRwex = 0x00100008;
uint noL2 = 0xFFFFFFFF;
uint l1SpiInit = image.Resolve("l1_func_spi_init", uploadBase);
uint l1SignalDrive = image.Resolve("l1_func_signal_drive", uploadBase);

for (int off = 0; off < segment.Length; off += chunkSize)
{
    int len = Math.Min(chunkSize, segment.Length - off);
    byte[] chunk = new byte[len];
    Array.Copy(segment, off, chunk, 0, len);

    byte[] cdb = BuildCdb(rbcMemRwex, (uint)off, noL2, 0);
    WriteLine($"    @0x{off:X4}: 上传 {len} 字节...");

    var result = transport.SendDataOut(cdb, chunk);
    if (!result.Success)
    {
        WriteLine($"  ! 上传失败 @0x{off:X}: {result.DescribeError()}");
        return 1;
    }
}
WriteLine("  驱动上传完成。");
WriteLine();

// ── 7. SPI 初始化 ──
WriteLine(">>> SPI 初始化 (Func1=l1_func_spi_init, Func2=0)...");
var spiInitCdb = BuildCdb(l1SpiInit, 0, 0, 0);
WriteLine($"    CDB: {BitConverter.ToString(spiInitCdb)}");
var spiResult = transport.SendCommand(spiInitCdb);
if (!spiResult.Success)
{
    WriteLine($"  ! SPI 初始化失败: {spiResult.DescribeError()}");
    return 1;
}
WriteLine("  SPI 初始化成功。");
Thread.Sleep(10);
WriteLine();

// ── 8. 发送 0x9F RDID ──
const uint sigdrvBuf = 0x01030000;

// 8a. NOP 预备命令
WriteLine(">>> NOP 预备命令 (Func1=l1_func_signal_drive, Func2=0x00)...");
var nopCdb = BuildCdb(l1SignalDrive, sigdrvBuf, 0x00, 0);
WriteLine($"    CDB: {BitConverter.ToString(nopCdb)}");
transport.SendCommand(nopCdb);
WriteLine("  NOP 完成。");
WriteLine();

// 8b. 0x9F RDID
WriteLine(">>> 0x9F JEDEC RDID (读取 4 字节)...");
var rdidCdb = BuildFlashReadCdb(l1SignalDrive, sigdrvBuf, 0x9F, 0);
WriteLine($"    CDB: {BitConverter.ToString(rdidCdb)}");
var rdidResult = transport.SendDataIn(rdidCdb, 4);
if (!rdidResult.Success || rdidResult.Response == null || rdidResult.Response.Length < 3)
{
    WriteLine($"  ! 0x9F 读取失败: {rdidResult.DescribeError()}");
}
else
{
    byte[] id = rdidResult.Response;
    string hex = id.Length switch
    {
        4 => $"{id[0]:X2} {id[1]:X2} {id[2]:X2} {id[3]:X2}",
        3 => $"{id[0]:X2} {id[1]:X2} {id[2]:X2}",
        _ => BitConverter.ToString(id)
    };
    uint packed = id.Length switch
    {
        4 => ((uint)id[0] << 24) | ((uint)id[1] << 16) | ((uint)id[2] << 8) | id[3],
        3 => ((uint)id[0] << 24) | ((uint)id[1] << 16) | ((uint)id[2] << 8),
        _ => 0
    };
    WriteLine($"  >>> 原始响应 ({id.Length} 字节): {hex}");
    WriteLine($"  >>> 大端打包: 0x{packed:X8}");
    WriteLine();

    // 判断是否可疑
    if (id[0] == 0x1F && id[1] == 0xFF && id[2] == 0xFF)
    {
        WriteLine("  [警告] 返回值模式为 1F FF FF — 疑似 SPI 时钟异常！");
        WriteLine("  建议: 下面将尝试 NoL2 模式重试 SPI init 后重新读取。");
        WriteLine();

        // ── 9. 重试：NoL2 SPI init + 0x9F ──
        WriteLine(">>> [重试] NoL2 模式 SPI 初始化...");
        var retryInitCdb = BuildCdb(l1SpiInit, 0, noL2, 0);
        WriteLine($"    CDB: {BitConverter.ToString(retryInitCdb)}");
        var retrySpi = transport.SendCommand(retryInitCdb);
        if (!retrySpi.Success)
        {
            WriteLine($"  ! 重试 SPI 初始化失败: {retrySpi.DescribeError()}");
        }
        else
        {
            WriteLine("  NoL2 SPI 初始化成功。");
            Thread.Sleep(10);

            // NOP
            WriteLine(">>> [重试] NOP 预备命令...");
            transport.SendCommand(nopCdb);
            WriteLine("  NOP 完成。");

            // 0x9F
            WriteLine(">>> [重试] 0x9F JEDEC RDID...");
            var retryRdid = transport.SendDataIn(rdidCdb, 4);
            if (!retryRdid.Success || retryRdid.Response == null)
            {
                WriteLine($"  ! 重试 0x9F 失败: {retryRdid.DescribeError()}");
            }
            else
            {
                byte[] id2 = retryRdid.Response;
                string hex2 = id2.Length switch
                {
                    4 => $"{id2[0]:X2} {id2[1]:X2} {id2[2]:X2} {id2[3]:X2}",
                    _ => BitConverter.ToString(id2)
                };
                uint packed2 = id2.Length switch
                {
                    4 => ((uint)id2[0] << 24) | ((uint)id2[1] << 16) | ((uint)id2[2] << 8) | id2[3],
                    _ => 0
                };
                WriteLine($"  >>> 原始响应 ({id2.Length} 字节): {hex2}");
                WriteLine($"  >>> 大端打包: 0x{packed2:X8}");
            }
        }
    }
    else if (id[0] == 0xFF && id[1] == 0xFF && id[2] == 0xFF)
    {
        WriteLine("  [警告] 返回值全 0xFF — Flash 未响应！");
    }
    else if (id[0] == 0x00 && id[1] == 0x00 && id[2] == 0x00)
    {
        WriteLine("  [警告] 返回值全 0x00 — Flash 未响应！");
    }
    else
    {
        WriteLine("  [正常] 返回值有效，可尝试 FlashLib 匹配。");
    }
}

WriteLine();
WriteLine("=" .PadRight(70, '='));
WriteLine("  完成。");
WriteLine("  提示：以上结果未经过任何解释/过滤，是设备 SPI 总线的原始响应。");
WriteLine("  如果返回值是 85 60 16 85 (0x85601685)，说明 Flash ID 读取正常、");
WriteLine("  PackRdid 修复后应该能匹配 FlashLib 条目。");
WriteLine("=" .PadRight(70, '='));
return 0;

// ── 辅助方法 ──
static byte[] BuildCdb(uint func1, uint dataAddr, uint func2, uint param)
{
    byte[] cdb = new byte[16];
    cdb[0] = 0xCB;
    cdb[1] = (byte)(func1 & 0xFF);
    cdb[2] = (byte)((func1 >> 8) & 0xFF);
    cdb[3] = (byte)((func1 >> 16) & 0xFF);
    cdb[4] = (byte)((func1 >> 24) & 0xFF);
    cdb[5] = (byte)(dataAddr & 0xFF);
    cdb[6] = (byte)((dataAddr >> 8) & 0xFF);
    cdb[7] = (byte)((dataAddr >> 16) & 0xFF);
    cdb[8] = (byte)((dataAddr >> 24) & 0xFF);
    cdb[9] = (byte)(func2 & 0xFF);
    cdb[10] = (byte)((func2 >> 8) & 0xFF);
    cdb[11] = (byte)((func2 >> 16) & 0xFF);
    cdb[12] = (byte)((func2 >> 24) & 0xFF);
    cdb[13] = (byte)(param & 0xFF);
    cdb[14] = (byte)((param >> 8) & 0xFF);
    cdb[15] = (byte)((param >> 16) & 0xFF);
    return cdb;
}

static byte[] BuildFlashReadCdb(uint func1, uint dataAddr, uint spiCmd, uint flashAddr)
{
    byte[] cdb = new byte[16];
    cdb[0] = 0xCB;
    cdb[1] = (byte)(func1 & 0xFF);
    cdb[2] = (byte)((func1 >> 8) & 0xFF);
    cdb[3] = (byte)((func1 >> 16) & 0xFF);
    cdb[4] = (byte)((func1 >> 24) & 0xFF);
    cdb[5] = (byte)(dataAddr & 0xFF);
    cdb[6] = (byte)((dataAddr >> 8) & 0xFF);
    cdb[7] = (byte)((dataAddr >> 16) & 0xFF);
    cdb[8] = (byte)((dataAddr >> 24) & 0xFF);
    cdb[9] = (byte)(spiCmd & 0xFF);
    cdb[10] = (byte)((flashAddr >> 16) & 0xFF);
    cdb[11] = (byte)((flashAddr >> 8) & 0xFF);
    cdb[12] = (byte)(flashAddr & 0xFF);
    return cdb;
}