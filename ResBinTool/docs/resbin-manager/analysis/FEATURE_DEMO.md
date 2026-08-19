# ResBinManager v1.1 - 固件打包功能演示

## 🎬 功能概览

ResBinManager v1.1 新增了完整的固件打包功能，实现了从资源修改到固件生成的一站式工作流。

---

## 📋 使用流程演示

### 场景：替换开机 Logo 并生成新固件

#### Step 1: 启动工具

```batch
cd tools
RunResBinManager.bat
```

**界面显示**:
- 左侧：资源列表（94个资源）
- 右侧：预览面板（默认显示）
- 工具栏：Open, Replace, Export, Save, Preview, **Build Firmware**, **⚙️ Config**

---

#### Step 2: 打开 RES.BIN

1. 点击 **📂 Open** 按钮
2. 选择 `ax32_platform_demo/resource/RES.BIN`
3. 等待解析完成（约1-2秒）

**状态栏显示**: "Loaded 94 resources from RES.BIN"

---

#### Step 3: 替换开机 Logo

1. 在列表中找到 ID=78 (RES_POWER_ON)
2. 点击选中该行
3. 点击 **🔄 Replace** 按钮
4. 选择新的 `power_on_new.jpg`
5. 确认替换

**预览面板**: 自动显示新 Logo 预览

---

#### Step 4: 保存修改

1. 点击 **💿 Save** 按钮
2. 选择保存位置（建议另存为 `RES_modified.bin`）
3. 自动创建备份文件

**提示框**: "RES.BIN saved successfully!"

---

#### Step 5: 切换到固件打包面板

1. 点击工具栏的 **⚙️ Config** 按钮（ToggleButton）
2. 右侧面板切换为固件打包配置界面

**界面变化**:
- 隐藏：预览面板
- 显示：固件打包配置面板

---

#### Step 6: 配置打包参数（首次需要）

##### 6.1 选择 ELF 文件

点击 **ELF File** 旁边的 **Browse** 按钮：

```
路径: ax32_platform_demo/Debug/ax329x_sdk.elf
```

**智能推断**: 自动设置输出目录为 `ax32_platform_demo/output/`

##### 6.2 确认 RES.BIN 路径

自动填充为当前打开的文件：

```
路径: ax32_platform_demo/resource/RES.BIN
```

**提示**: 如果已保存为新文件，可手动修改路径

##### 6.3 选择 MakeSPIBin.exe

点击 **MakeSPIBin.exe** 旁边的 **Browse** 按钮：

```
路径: ax32_platform_demo/output/MakeSPIBin.exe
```

##### 6.4 确认输出目录

自动推断的路径：

```
路径: ax32_platform_demo/output/
```

##### 6.5 设置选项

- ☑️ Auto backup original files（推荐启用）
- ☑️ Open output folder after build（可选）

---

#### Step 7: 开始打包

点击工具栏的 **🔨 Build Firmware** 按钮

**打包流程**（实时显示在日志窗口）:

```
[0%] 开始固件打包流程...
[10%] 备份原文件...
[15%] 已备份 DestBin.bin -> DestBin.bin.backup_20260518_143022
[20%] 准备输出目录...
[30%] 复制资源文件...
[40%] 已复制 RES.BIN 到输出目录
[50%] 调用 MakeSPIBin.exe 进行合并...
[55%] 执行命令: MakeSPIBin.exe "ax329x_sdk.bin" "Res.bin"
[60%] [OUT] Merging ELF and RES.BIN...
[65%] [OUT] ELF size: 256 KB
[70%] [OUT] RES.BIN size: 128 KB
[75%] [OUT] Total firmware size: 384 KB
[80%] [OUT] Writing DestBin.bin...
[95%] MakeSPIBin.exe 退出码: 0
[98%] 生成 DestBin.bin (393 KB)
[100%] 打包完成！
```

**进度条**: 从 0% 逐步增长到 100%（绿色）

---

#### Step 8: 查看结果

**成功对话框**:

```
✅ 固件打包成功！

输出文件: D:\...\ax32_platform_demo\output\DestBin.bin
文件大小: 393 KB
耗时: 2.35 秒
```

**状态栏**: "Firmware built successfully: DestBin.bin"

**自动操作**: 如果启用了该选项，会自动打开输出文件夹

---

#### Step 9: 烧录测试

1. 将 `DestBin.bin` 烧录到设备
2. 重启设备
3. 验证新 Logo 显示正常

---

## 🎯 核心特性展示

### 1. 一键打包

**操作**: 只需点击一个按钮  
**效果**: 自动完成所有打包步骤

### 2. 可视化配置

**界面元素**:
- 4个文本框显示路径
- 4个 Browse 按钮选择文件/目录
- 2个复选框设置选项
- 1个进度条显示进度
- 1个日志窗口显示详细信息

### 3. 实时进度

**进度阶段**:
- 0-10%: 验证配置
- 10-20%: 备份原文件
- 20-30%: 准备输出目录
- 30-40%: 复制 RES.BIN
- 50-95%: 调用 MakeSPIBin.exe
- 95-100%: 验证结果

### 4. 详细日志

**日志内容**:
- 每个阶段的操作描述
- MakeSPIBin.exe 的标准输出
- 错误信息（如果有）
- 最终结果统计

**日志样式**: Consolas 字体，灰色背景，便于阅读

### 5. 安全保护

**保护措施**:
- ✅ 打包前自动备份
- ✅ 时间戳命名避免覆盖
- ✅ 文件存在性验证
- ✅ 超时保护（60秒）
- ✅ 退出码检查

### 6. 智能推断

**自动设置**:
- 选择 ELF 后 → 自动推断输出目录
- 打开 RES.BIN 后 → 自动填充资源路径
- 常用路径记忆（未来版本）

---

## 💡 高级技巧

### 技巧 1: 快速切换面板

**操作**: 点击 **⚙️ Config** 按钮  
**效果**: 在预览面板和配置面板之间快速切换

### 技巧 2: 批量修改后统一打包

**流程**:
1. 依次替换多个资源（Logo、图标、音效等）
2. 每次替换后预览确认
3. 全部完成后保存一次
4. 执行一次固件打包

**优势**: 减少打包次数，提高效率

### 技巧 3: 查看历史备份

**位置**: `ax32_platform_demo/output/`  
**文件名**: `DestBin.bin.backup_YYYYMMDD_HHMMSS`  
**用途**: 恢复到之前的版本

### 技巧 4: 日志分析

**成功标志**:
```
MakeSPIBin.exe 退出码: 0
生成 DestBin.bin (XXX KB)
```

**失败标志**:
```
[ERR] Error: ...
MakeSPIBin.exe 退出码: 1
```

---

## 🔍 界面布局详解

### 工具栏（顶部）

```
┌─────────────────────────────────────────────────────┐
│ 📂 Open | 🔄 Replace | 💾 Export | 💿 Save | 👁 Preview │
│ ─────────────────────────────────────────────────── │
│ 🔨 Build Firmware | ⚙️ Config                        │
└─────────────────────────────────────────────────────┘
```

### 主内容区（中部）

```
┌──────────────────┬───┬──────────────────────────┐
│                  │   │  Preview & Properties    │
│  Resources       │   │  (或 Firmware Packaging) │
│  (DataGrid)      │   │                          │
│                  │   │  - Image Preview         │
│  - ID            │   │  - Properties            │
│  - Name          │   │  - Build Config          │
│  - Type          │   │  - Progress Bar          │
│  - Offset        │   │  - Build Log             │
│  - Size          │   │                          │
│  - Status        │   │                          │
│                  │   │                          │
└──────────────────┴───┴──────────────────────────┘
```

### 状态栏（底部）

```
┌─────────────────────────────────────────────────────┐
│ Status Message                    Total: 94 resources│
└─────────────────────────────────────────────────────┘
```

---

## 📊 性能指标

### 打包速度

| 项目 | 时间 |
|------|------|
| RES.BIN 解析 | 1-2 秒 |
| 资源替换 | < 1 秒 |
| 固件打包 | 2-5 秒 |
| **总计** | **5-10 秒** |

### 文件大小

| 文件 | 大小 |
|------|------|
| ax329x_sdk.elf | ~256 KB |
| RES.BIN | ~128 KB |
| **DestBin.bin** | **~384 KB** |

---

## 🆚 对比传统方式

### 传统方式（命令行）

```batch
REM 1. 手动复制 RES.BIN
copy RES_modified.bin ax32_platform_demo\output\Res.bin

REM 2. 进入输出目录
cd ax32_platform_demo\output

REM 3. 运行打包工具
MakeSPIBin.exe ax329x_sdk.bin Res.bin

REM 4. 检查结果
dir DestBin.bin
```

**缺点**:
- ❌ 需要手动输入命令
- ❌ 容易出错（路径、文件名）
- ❌ 无进度提示
- ❌ 无自动备份
- ❌ 需要多次切换目录

### 新方式（图形界面）

```
1. 点击 Open → 选择 RES.BIN
2. 替换资源 → 预览确认
3. 点击 Save → 保存修改
4. 点击 ⚙️ Config → 配置参数（首次）
5. 点击 🔨 Build Firmware → 一键打包
```

**优点**:
- ✅ 图形界面直观易用
- ✅ 自动处理路径和文件名
- ✅ 实时进度显示
- ✅ 自动备份保护
- ✅ 一站式完成所有操作

---

## 🎓 学习资源

- 📖 [完整文档](README.md)
- 🚀 [快速入门](QUICKSTART.md)
- 🔨 [固件打包指南](FIRMWARE_BUILD_GUIDE.md)
- 📝 [使用示例](USAGE_EXAMPLES.md)
- 📊 [项目总结](PROJECT_SUMMARY.md)
- 📋 [更新日志](CHANGELOG.md)

---

## ✨ 总结

ResBinManager v1.1 通过集成 MakeSPIBin.exe，实现了真正的**一站式固件开发工作流**：

```
资源修改 → 预览确认 → 保存备份 → 固件打包 → 烧录测试
   ↑                                                        ↓
   └────────────────────────────────────────────────────────┘
                        循环迭代优化
```

**核心价值**:
- 🎯 **效率提升**: 从 10+ 步简化到 5 步
- 🛡️ **安全保障**: 自动备份 + 多重验证
- 📊 **可视化**: 实时进度 + 详细日志
- 🔄 **易迭代**: 快速修改 → 快速打包 → 快速测试

**适用场景**:
- UI 设计师调整 Logo 和图标
- 工程师调试资源文件
- 测试人员验证修改效果
- 产品经理演示功能变更

---

**版本**: v1.1.0  
**更新日期**: 2026-05-18  
**作者**: AX329x SDK Team
