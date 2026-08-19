# UpgradeTool — DC503J 固件刷写工具

产线用 Windows 工具：通过 **SCSI Pass-Through** 向 USB MSC 设备下发厂商命令完成固件刷写。
**生产主通道为 Loader(0xCB)**：上传 `ThunderSE.elf` 驱动到 RAM，经其 SPI 驱动完成固件下载与回读校验，
最后 `0xDA` 收尾复位。`0xCD` 应用态通道（SDRAM SPI stub）保留为备选/无 Loader 环境（`Dc503RomProtocol`）。
**对齐参考项目 MPTool：Loader 态是生产主通道**——应用态设备（产品串不含 "loader"）在连接时直接下发 `0xDA`
切换至 Loader 模式，工具不再尝试 0xCD 应用态通道；只有 Loader 态设备才建立实际连接（0xCB）。
自动检测/连接目标设备，并读取设备端真实 Flash 容量。

## 为什么用 SCSI Pass-Through（而不是 libusb）

- 使用标准 Windows 存储驱动（usbstor.sys），**无需替换驱动**、不破坏 U 盘功能，产线部署零风险。
- 仓库内已有同方案的生产验证参考：`HM020F_SVN300/HM020F/firmware/tools/ResBinManager/Core/UsbMscService.cs`。
- `0xCD` 数据面以 `cb_mem_rwex` 为统一入口，`DataAddr=0xffffffff` 哨兵让固件走 `scsi.ptxbuf/prxbuf`（SRAM，免缓存问题）。

## 固件侧协议（`hal/dusb/hal_usb_msc.c`）

`scsi_cmd_analysis()` 从 CBW 解析命令（`get_cbw` 中 CBW 字节 15=OpCode、16=SubOpCode、17-20=Address、21=SubEx、22-23=Length，
即对应 SCSI CDB[0..]）：

| OpCode | 功能 | 数据阶段 | 说明 |
|--------|------|---------|------|
| `0xDA` | 进入升级模式 | 无 | 回 CSW 后关中断、复位 USB、跳转 bootloader（`cbw_update()`） |
| `0xCB`/`0xF0` | RTC 校时 | 无 | 时间戳在 CDB[4..7] |
| `0xCB`/`0xF1` | 写传感器 I2C | 无 | 参考命令 |
| `0xCD` | 固件/资源升级 | 无 | `mscCmd_ufmod()` |

> 主机 MSC 栈把 SCSI CDB 放进 CBW 的字节 15..30，因此 `CDB[0]=0xCD` 即被固件解析为 `OpCode=0xCD`。
> 16 字节 CDB 字段按固件 `get_cbw` 映射：`OpCode=CDB[0]`、`Func1=LE(CDB[1..4])`、`DataAddr=LE(CDB[5..8])`、
> `Func2=LE(CDB[9..12])`、`Param=CDB[13..15]`（24 位小端）。`mscCmd_ufmod` 以 `Func1` 为 L1 函数、
> `Func2` 为 L2 函数；L1 无数据回传，需要回数据的操作必须走 L2（经 `cb_mem_read`/`cb_mem_write`）。

## 架构

```
UpgradeTool.slnx
├─ src/UpgradeTool.Core     核心库（无 UI 依赖，可独立测试）
│  ├─ Abstractions/        IFlashTransport / IFlashProtocol / FlashProgress / ProtocolResult
│  ├─ Interop/             Win32 SCSI Pass-Through P/Invoke
│  ├─ Transport/           MscScsiTransport（真实） / SimulatedMscTransport（模拟）
│  ├─ Protocol/            LoaderRomProtocol（生产：0xCB + ThunderSE 驱动） / Dc503RomProtocol（备选：0xCD + stub）
│  │                        / Dc503RomCommands（0xCB/0xCD CDB 编解码） / FlashInfo / StubImage / FirmwareSymbols
│  ├─ Devices/             MscDeviceEnumerator（SetupDi 枚举磁盘）
│  │                        / DeviceConnection（连接 + Flash 信息） / DeviceWatcher（自动检测/连接）
│  ├─ FlashService.cs      刷写会话编排（复用连接→下载→校验→0xDA 收尾）
│  └─ Utilities/           Crc32
├─ src/UpgradeTool.App      WPF 界面
├─ tests/UpgradeTool.Core.Tests  单元测试（26 个）
└─ samples/                示例固件
```

## 使用

1. `dotnet build UpgradeTool.slnx`（需 .NET 9 SDK）。
2. 启动 `src/UpgradeTool.App`。工具自动检测并连接 USB MSC 设备（每 2 秒扫描，
   无需 VID/PID，目标设备由连接握手自动识别），连接后自动读取设备端 Flash 容量并在列表中显示 `[Flash 4 MB]`。
3. 点击「刷新设备」立即重新扫描。
4. 勾选「包含模拟设备」可无硬件验证全流程。
5. 选择固件 bin（或勾选「仅进入升级模式」）→「开始刷写」。

真实刷写：
- 选择设备 → 填固件路径 → 开始。工具经 `0xCB` 上传 ThunderSE 驱动、擦写固件并回读校验，
  最后下发 `0xDA` 收尾复位（跳 bootloader 并重新枚举）。
- 固件大小受设备端 Flash 容量限制，超限会被拒绝。
- 若仅需产线"进入升级模式"，勾选「仅进入升级模式（不下载固件）」。
- **刷写后终态（对齐 MPTool auto_reset）**：勾选「复位」→ 刷写完成后下发 `0xDA` 复位设备（运行新固件），
  工具就此放手；不勾选 → 不下发复位，设备停留在当前（Loader）态。连接阶段不改变设备模式。

## 设备自动连接

- `DeviceWatcher` 周期性枚举 USB MSC 磁盘设备：目标设备自动建立连接，**按设备当前状态选协议**
  （Loader 态→`0xCB` 生产通道，应用态→`0xCD` 备选通道），不主动切换设备模式；消失的设备自动断开释放。
- **多线程与并发控制（对齐 MPTool）**：设备连接握手（打开传输层 + SCSI 探针 + 驱动上传 + Flash 查询）
  为耗时阻塞操作。多台设备同时接入时，`DeviceWatcher` 为每台设备启动独立连接任务并行握手，
  用 `SemaphoreSlim` 做有界并发控制（默认上限 8，对齐 MPTool `MAX_THREAD=8`，可经
  `DeviceWatcher` 构造参数 `maxConcurrentConnections` 配置），单台连接失败/异常不影响同批其他设备；
  日志输出串行化保证并发下不交错。
- `DeviceConnection` 持有传输层与协议层，供 `FlashService` 复用（会话不重复连接、不关闭 watcher 管理的连接）。
- **分类型识别（对齐 MPTool SearchDev 按 ClassInfo 派发）**：设备库条目按 `ClassInfo` 解析为设备类型
  `DeviceKind`（Loader / AXISP / AX326X 直连SPI / AX3233RP 量产 / AX2005Adapter 适配器），
  `DeviceConnection` 暴露 `Kind`/`KindLabel` 并在显示名中标出类型；Loader 态 → 0xCB 生产通道，
  其余类型（应用态）→ 0xDA 进入升级模式后重新枚举为 Loader。
- **子设备检测（对齐 MPTool AX2005Adapter→BerrySdio 两阶段）**：AX2005Adapter 适配器在连接时不再发 0xDA，
  而是由 `BerryChildDetector` 执行两阶段检测——① 经 0xCB L3 上传并校验适配器驱动 AXIDEsdspi.elf、
  初始化、`probe_port` 轮询检测子设备在位、`probe_dev` 以 0xAAAAAAAA 确认 Berry；
  ② 经适配器 `tgt_rw` 上传子设备固件 AX3233AXIDE_A2.elf，经 0xCD L2 `eeprom_init` 初始化、
  `bootSgmt_driver_check` 识别类型（0x01=EEPROM、0x02=Flash+ID）。
  符号地址全部从驱动 ELF 符号表解析（`DriverImage`），用 `SimulatedAdapterDevice` 端到端测试验证。
  适配器+子设备的刷写通道尚未接入（当前以 Loader 态为生产主通道），检测结果记入日志供后续扩展。
- **Flash ID 无法匹配的处理（对齐 MPTool AutoAddFlashType）**：Loader 通道读到有效但 FlashLib
  未收录的 RDID 时，不再一律回退默认 4MB——`FlashLib.DeriveCapacityFromRdid` 按 JEDEC 密度字段
  推导容量（W25Q32→4MB、W25Q64→8MB、W25Q128→16MB），使未知但有效的 Flash 能以正确容量烧写；
  仅当 ID 确实无效（1F FF FF 重试后 / 全 FF / 全 00 / 密度不可推导）才回退默认并告警（对齐 MPTool
  对无效 ID 放弃烧写的行为）。

## 接入真实下载协议

1. 向固件侧确认 bootloader 的下载命令格式（写块 / 读回 / CRC / 结束命令的 OpCode 与字段布局）。
2. 生产通道在 `LoaderRomProtocol.DownloadFirmwareAsync / VerifyFirmwareAsync`（0xCB + ThunderSE 驱动）
   实现；备选的 `Dc503RomProtocol`（0xCD + stub）保留供无 Loader 环境使用。
   传输层已提供 `SendCommand / SendDataOut / SendDataIn`（CDB 布局与 `get_cbw` 对齐）。

## 测试

```powershell
dotnet test tests/UpgradeTool.Core.Tests
```

覆盖：0xCD CDB 布局与固件字节偏移对齐、CRC32 标准向量、VID/PID 解析（仅用于显示）、模拟设备全流程
（下载/校验/篡改检测/进度单调）、Flash 信息查询、容量超限拒绝、DeviceWatcher 自动连接/断开、
连接复用（会话不关闭 watcher 管理的连接）。
