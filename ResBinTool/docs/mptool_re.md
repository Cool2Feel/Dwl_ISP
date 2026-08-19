# MPTool 逆向分析报告（mptool.exe）

目标：`D:\jrx\project\tools\MPTool(1)\MPTool\mptool.exe`
工具：pefile 2024.8.26 + capstone 4.0.2（通过 `py` 启动器），PowerShell 辅助。
日期：2026-08-11

## 1. 文件总览

| 项 | 值 |
|---|---|
| 大小 | 486,912 字节 |
| 格式 | PE32 x86，原生（非 .NET） |
| Machine | 0x014C (i386) |
| Magic | 0x010B (PE32) |
| ImageBase | 0x400000 |
| EP RVA/VA | 0x3253D / 0x43253D |
| TimeDateStamp | 2025-08-26 |
| 运行时 | MFC 9.0（VS2008/VC90），引用 mfcm90u.dll |
| 提权 | requireAdministrator（manifest） |

节区：

| 节 | RVA | VSize | 文件偏移 |
|---|---|---|---|
| .text | 0x1000 | 0x56400 | 0x400 |
| .rdata | 0x58000 | 0x17E00 | 0x56800 |
| .data | 0x70000 | 0x2AD08 | 0x6E600 |
| .rsrc | 0x9B000 | 0x4C00 | 0x72200 |

资源目录类型：1(图标) 2(位图) 3(光标) 4(菜单) 5(对话框) 6(字符串) 12(?) 14(图标组) 16(版本) 24(Manifest) 240(?)——与 MFC 应用等资源结构吻合。

## 2. 类结构（MFC RTTI 线索）

`CAX2210MPToolApp`、`CAX2210MPToolDlg`、`CAX2210Static` 以及各设备驱动类：
`AX2210DownMsg`、`AX3233Efuse`(CEfuse)、`AX3233RP`、`AX326X`、`AXISP`(Isp=1)、`AX2005Adapter`、`AXAccessible`。
设备支持由 `setting/DeviceLib.ini` 决定（SCSI INQUIRY 字符串 → 驱动类 + SpiDriver ELF）。

## 3. USB 传输协议（已确认）

### 3.1 两层调用结构

```
高层: AXxxxDriver 成员（this=esi，ecx+0xc4c8..）
  -> 0x40A810  Builder：封装厂商 CDB（opcode 0xCB）
  -> 0x40A290  SCSIPassThru：DeviceIoControl(IOCTL_SCSI_PASS_THROUGH_DIRECT=0x4D014)
  -> KERNEL32!DeviceIoControl / CreateFileW / GetLastError / Sleep
```

### 3.2 CDB 构建（0x40A810）

- `CDB[0] = 0xCB`（厂商自定义 opcode，非标准 SCSI 命令）
- 上送参数结构体 [esp+4]（15 字节）→ `CDB[1..0xF]`（4+4+4+2+1 段复制）
- `CDB[16]` 为 16 字节（`push 0x10` 传长度）
- 调用 0x40A290 传递：CDB、数据缓冲、长度、方向标志（[esp+0x10] 非零才发 USB）
- 函数 `ret 0x10`：共 4 个参数（结构体指针、数据缓冲、长度、标志）

### 3.3 SCSI Pass-Through（0x40A290）

- 使用 **IOCTL 0x4D014** = `IOCTL_SCSI_PASS_THROUGH_DIRECT`
- 构建 `SCSI_PASS_THROUGH_DIRECT`（`Length=0x2C`=sizeof，`SenseInfoLength=0x1A`，`DataIn=0/1`）
- `DataTransferLength` 取自参数；`TimeOutValue = 0xC8`（200ms）
- 一次握手占满 0x50 字节缓冲（in/out 同址，nIn=nOut=0x50）
- 失败重试：`GetLastError()!=0x37` 时 `Sleep(0x14)`=20ms 后重建重发，直至成功或重试耗尽（计数 [esp+0x14]）
- 句柄由 CreateFileW 打开（对象位于 `[esi+0xc454]`，即 "this"[0]+0xc454）

### 3.4 设备发现/句柄

- 字符串：`\\?\USBSTOR`、`\\.\PHYSICALDRIVE%i`、`\\.\%c:`（磁盘枚举/卷）
- 注册表：`SYSTEM\ControlSet001\Control\UsbFlags`（USB 设备覆盖标志）
- `IgnoreHWSerNum1908`：忽略 VID 0x1908 设备的硬件序列号一致检查
- 依赖 SETUPAPI：`SetupDiGetClassDevsW / SetupDiEnumDeviceInterfaces / SetupDiGetDeviceInterfaceDetailW`（间接枚举 USBSTOR）

## 4. SPI Flash 编程命令

三个几乎相同的 SPI 下载函数副本（分属不同设备类）：
`0x40BC60` / `0x40D1D0` / `0x40EB...`（含 `USB ERROR SPI[xx]` 错误点 0x40BD89/0x40D2FC/...）。

命令包（15 字节，位于 `[this+0x98..0xA8)`，0x40BDC0 构造示例）：
```
[0x98] dword = 全局计数 ([0x496B9C])   # 可能为 CBW 参数/计数器
[0x9C]       = 0
[0x9D]       = 0x07                    # 命令类型
[0x9E]       = 0x04
[0x9F]       = 0xD8                    # SPI 操作码（与错误串 SPI[D8] 对应）
[0xA0]       = 参数高字节
[0xA1]       = 参数高字节
[0xA2]       = 参数低字节
```

命令包以 chunk 传输：
- 首包/校验回读用 0x200/0x400 长度，`_spifirm_writeadr` 地址自增
- 校验方式：整段按 4 字节 dword 比对读回数据（0x40BC60 内比较循环）
- 错误分类：`USB ERROR SPI[02] [03] [05] [06] [9F] [D8]` 与 `USB ERROR[0] [2] [3] [5] [D8]`，
  通过 `%s!(SR=%02X)` / `%s2!(SR=%02X)` 追加 SPI 状态寄存器值
- 0x40A810/0x40A290 由 `[this+0xc4c8]` 校验区块是否可达后调用

## 5. 固件侧（setting/*.elf）——关键符号

Xtensa (e_machine=92) ELF32，`GNU C 4.9.1 -mno-delay -mnewlib -g -Os`，未剥离：

- 下载交互：`l1_func_preprocess / l1_func_reset / l1_func_spi_init / l1_func_signal_drive`
  `l2_func_reset / l2_func_spi_page_program`
- SPI：`spi_sf_read_id / spi_sf_write_enable / spi_sf_check_status / spi_sf_send_addr`、`SPICSORDER`
- DMA/缓冲：`RBC_mem2FIFO / RBC_FIFO2mem / RBC_send_data / RBC_receive_data / RBC_Set_Chksum2 / RBC_mem_rwex_buf`
- 加密：`encrypt_open / encrypt_close`（ENCRYPT_t）、`getheader2`
- 结构与 CBW：`PARAM_t / SIG_DRV_t / BYTE_PROGRAM_t / cbwcb_usbc / cbwcb_tag / cbwcb_len`
  （`cbwcb_*` = USB MSC BOT CBW 中 CbwCB=厂商 CDB 字段，与 exe 侧 0xCB/0x10 对应）
- 源码工程：`D:/work/project_JRX/project_thunderLT/ThunderLT_verify/tools/MPTool_src/SpiFirm/ThunderSE/scr/main.c`

## 6. 配置文件

- `DeviceLib.ini`（ItemSum=10）：SCSI INQUIRY 字符串匹配。HM020F → `BuildWinVideo050Loader 1.00` → ClassInfo=AX326X，SpiDriverPath=ThunderSE.elf
- `FlashLib.ini`：Loader-Version=BL206v1.0.0，Firmware=Firmware\Spi_Lib.bin，Address=0x800，Read-ID-9F/AB/90/15
- `mptool.ini`：`[COMMON] CodeBinPath ERASEALL=1 AUTORESET AUTOSTART SmartMPEnable`，`[PATH] PATH0-9`（DestBin.bin 系列）
- `log.txt`（UTF-16LE BOM）：量产日志；含 `文件校验值为: SPxxxxxxxx`。（校验 = exe 生成 8 位十六进制，前缀 "SP"）

## 7. 备注

- 字符串含 `BT_Setting.dll / BT_Setting2.dll / BT_Setting3.dll / XLX_Setting.dll`（音频负载设置 UI 插件，可缺失）
- `BackUp.bin`（量产前备份）、`order.ini`（SPI 时序参数，AX3233RP 用）、`licence.lic`
- `eeprom_init/read/write`、`efuse_read/efuse_write` 与 `MemReadWrite`、`Read Symbol Table/String`（Code 调试加载，对应"导入CODE调试文件"）
- 控制台中文乱码：utf-16le 字符串需写临时文件读取；GBK 已单独提取到 `mptool_gbk_rdata.txt`（492 条）