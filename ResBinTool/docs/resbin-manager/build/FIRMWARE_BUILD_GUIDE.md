# 固件打包功能使用指南

## 📋 功能概述

ResBinManager 现已集成 MakeSPIBin.exe 固件打包功能，可以实现从资源修改到固件生成的一站式流程。

### 主要特性

- ✅ **可视化配置** - 图形界面配置 ELF、RES.BIN、输出目录等参数
- ✅ **自动备份** - 打包前自动备份原 DestBin.bin 文件
- ✅ **实时进度** - 显示打包进度和详细日志
- ✅ **智能推断** - 自动推断输出目录路径
- ✅ **一键打包** - 点击按钮即可完成整个打包流程
- ✅ **结果提示** - 打包完成后显示文件大小和耗时信息

---

## 🚀 快速开始

### 步骤 1: 打开 RES.BIN 文件

1. 启动 ResBinManager
2. 点击 **📂 Open** 按钮
3. 选择 `ax32_platform_demo/resource/RES.BIN`
4. 等待资源列表加载完成

### 步骤 2: 切换到固件打包面板

1. 点击工具栏右侧的 **⚙️ Config** 按钮
2. 右侧面板切换为固件打包配置界面

### 步骤 3: 配置打包参数

#### 3.1 选择 ELF 文件

点击 **ELF File** 旁边的 **Browse** 按钮，选择编译生成的 ELF 文件：

```
ax32_platform_demo/Debug/ax329x_sdk.elf
```

**提示**: 选择 ELF 后，工具会自动推断输出目录为 `ax32_platform_demo/output/`

#### 3.2 确认 RES.BIN 路径

RES.BIN 路径会自动填充为当前打开的文件。如果需要修改，可以手动编辑（未来版本支持）。

#### 3.3 选择 MakeSPIBin.exe

点击 **MakeSPIBin.exe** 旁边的 **Browse** 按钮，选择打包工具：

```
ax32_platform_demo/output/MakeSPIBin.exe
```

**注意**: 如果该文件不存在，需要先编译项目或从 SDK 中获取。

#### 3.4 确认输出目录

输出目录通常会自动设置为 ELF 所在目录的 `output` 子目录。如果需要修改，点击 **Browse** 选择其他目录。

### 步骤 4: 设置选项

- ☑️ **Auto backup original files** - 打包前自动备份原 DestBin.bin（推荐启用）
- ☑️ **Open output folder after build** - 打包完成后自动打开输出文件夹

### 步骤 5: 开始打包

点击工具栏的 **🔨 Build Firmware** 按钮，开始打包流程。

---

## 📊 打包流程详解

### 阶段 1: 验证配置 (0-10%)

- 检查所有必需文件是否存在
- 验证路径有效性
- 报告错误（如果有）

### 阶段 2: 备份原文件 (10-20%)

- 如果启用了自动备份
- 将现有的 `DestBin.bin` 复制为 `DestBin.bin.backup_YYYYMMDD_HHMMSS`
- 保留时间戳以便追溯

### 阶段 3: 准备输出目录 (20-30%)

- 检查输出目录是否存在
- 如果不存在则创建
- 清理临时文件（如果需要）

### 阶段 4: 复制 RES.BIN (30-40%)

- 将修改后的 RES.BIN 复制到输出目录
- 重命名为 `Res.bin`（MakeSPIBin.exe 要求的名称）

### 阶段 5: 调用 MakeSPIBin.exe (50-95%)

- 执行命令：`MakeSPIBin.exe ax329x_sdk.bin Res.bin`
- 实时捕获标准输出和错误输出
- 显示在日志窗口中

### 阶段 6: 验证结果 (95-100%)

- 检查是否生成了 `DestBin.bin`
- 计算文件大小
- 报告成功或失败

---

## 🔍 日志解读

### 成功示例

```
开始固件打包流程...
备份原文件...
已备份 DestBin.bin -> DestBin.bin.backup_20260518_143022
准备输出目录...
复制资源文件...
已复制 RES.BIN 到输出目录
调用 MakeSPIBin.exe 进行合并...
执行命令: MakeSPIBin.exe "ax329x_sdk.bin" "Res.bin"
[OUT] Merging ELF and RES.BIN...
[OUT] ELF size: 256 KB
[OUT] RES.BIN size: 128 KB
[OUT] Total firmware size: 384 KB
[OUT] Writing DestBin.bin...
MakeSPIBin.exe 退出码: 0
生成 DestBin.bin (393 KB)
打包完成！

✅ 打包成功！
输出文件: D:\...\output\DestBin.bin
文件大小: 393 KB
耗时: 2.35 秒
```

### 失败示例

```
开始固件打包流程...
备份原文件...
准备输出目录...
复制资源文件...
已复制 RES.BIN 到输出目录
调用 MakeSPIBin.exe 进行合并...
执行命令: MakeSPIBin.exe "ax329x_sdk.bin" "Res.bin"
[ERR] Error: Cannot open ELF file
MakeSPIBin.exe 退出码: 1
生成失败: Error: Cannot open ELF file

❌ 打包失败: MakeSPIBin.exe 执行失败，请检查输出日志
```

---

## ⚠️ 常见问题

### Q1: MakeSPIBin.exe 找不到？

**原因**: MakeSPIBin.exe 可能不在默认位置。

**解决方法**:
1. 检查 `ax32_platform_demo/output/` 目录
2. 如果不存在，需要重新编译项目
3. 或者从 SDK 的其他位置拷贝

### Q2: ELF 文件不存在？

**原因**: 项目未编译或编译失败。

**解决方法**:
1. 使用 CodeLite 或其他 IDE 编译项目
2. 确保生成了 `ax329x_sdk.elf`
3. 检查编译日志是否有错误

### Q3: 打包后 DestBin.bin 没有更新？

**原因**: MakeSPIBin.exe 执行失败但未被检测到。

**解决方法**:
1. 查看 Build Log 中的错误信息
2. 手动运行 MakeSPIBin.exe 测试
3. 检查文件权限

### Q4: 如何恢复备份文件？

**方法**:
1. 找到备份文件：`DestBin.bin.backup_YYYYMMDD_HHMMSS`
2. 删除当前的 `DestBin.bin`
3. 将备份文件重命名为 `DestBin.bin`

### Q5: 打包后如何烧录？

**方法**:
1. 使用 JTAG 调试器烧录 `DestBin.bin`
2. 或使用专用的 SPI Flash 烧录工具
3. 或通过 USB 升级功能（如果支持）

---

## 💡 最佳实践

### 1. 修改资源后的完整流程

```
1. 在 ResBinManager 中打开 RES.BIN
2. 替换需要的资源（如 Logo）
3. 保存修改后的 RES.BIN
4. 切换到固件打包面板
5. 配置打包参数（首次需要配置）
6. 点击 Build Firmware
7. 等待打包完成
8. 烧录生成的 DestBin.bin
```

### 2. 批量修改多个资源

```
1. 依次替换所有需要的资源
2. 每次替换后预览确认效果
3. 全部替换完成后保存一次
4. 执行固件打包
```

### 3. 版本管理建议

```
- 每次重大修改前备份 RES.BIN
- 使用有意义的文件名：RES_v1.0_logo_updated.bin
- 记录修改内容和日期
- 保留多个版本的 DestBin.bin
```

### 4. 调试技巧

```
- 始终启用 Build Log 查看详细信息
- 关注 MakeSPIBin.exe 的退出码（0=成功）
- 比较打包前后的文件大小
- 使用十六进制编辑器验证关键数据
```

---

## 🔧 高级用法

### 命令行调用（未来版本）

```bash
ResBinManager.exe --build \
  --elf "ax329x_sdk.elf" \
  --res "RES_modified.bin" \
  --tool "MakeSPIBin.exe" \
  --output "./output"
```

### 批处理脚本集成

```batch
@echo off
REM 自动打包脚本

cd /d "%~dp0"

REM 1. 修改 RES.BIN（通过 ResBinManager）
start ResBinManager.exe resource\RES_modified.bin

REM 2. 等待用户确认后打包
pause

REM 3. 调用 MakeSPIBin.exe
cd ax32_platform_demo\output
MakeSPIBin.exe ax329x_sdk.bin Res.bin

REM 4. 烧录固件
echo Please flash DestBin.bin to device
pause
```

---

## 📝 技术细节

### MakeSPIBin.exe 工作原理

```
输入:
  - ax329x_sdk.bin (从 ELF 转换而来)
  - Res.bin (资源文件)

处理:
  1. 读取 ELF 二进制数据
  2. 读取 RES.BIN 数据
  3. 按照特定格式合并
  4. 添加 Boot Sector 和校验和

输出:
  - DestBin.bin (可直接烧录的固件镜像)
```

### 文件结构

```
DestBin.bin = 
  BootSector (512 bytes) +
  ExceptionVectors +
  Code Section (.text) +
  Data Section (.data, .bss) +
  Resource Table +
  Resource Data Area
```

---

## 🆘 获取帮助

如果遇到无法解决的问题：

1. **查看日志**: Build Log 中通常包含详细的错误信息
2. **检查文件**: 确认所有必需文件存在且未损坏
3. **查阅文档**: 参考 SDK 文档了解 MakeSPIBin.exe 的详细用法
4. **联系支持**: 提供完整的日志和错误截图

---

## 📚 相关文档

- [ResBinManager 主文档](README.md)
- [使用示例](USAGE_EXAMPLES.md)
- [快速入门](QUICKSTART.md)
- [编译指南](BUILD_GUIDE.md)

---

**最后更新**: 2026-05-18  
**版本**: ResBinManager v1.0
