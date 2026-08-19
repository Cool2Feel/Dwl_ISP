# ResBinManager 功能开发需求文档

## 1. 文档概述

### 1.1 文档目的
本文档详细描述 ResBinManager 工具的功能开发需求，包括资源解析、资源替换、配置管理等核心功能，为开发、测试及维护提供明确指导。

### 1.2 适用范围
- 开发人员：理解功能实现细节
- 测试人员：制定测试用例
- 维护人员：快速定位问题

### 1.3 术语定义

| 术语 | 定义 |
|------|------|
| DestBin.bin | 固件最终输出文件，包含程序代码段和资源段 |
| RES.BIN | 资源文件，包含图片、音频、字体等资源数据 |
| BLDR | Bootloader 签名标识，位于 DestBin.bin 偏移 0x04-0x07 |
| MAGICKEY | 固件魔数常量，标准值为 0x01234567 |
| flash_param | Flash 参数结构，包含资源区地址等关键信息 |
| ResourceType | 资源类型枚举（Jpeg、Bitmap、Wav、Font、Palette 等） |
| ConfigId | 配置项 ID 枚举（CONFIG_ID_YEAR、CONFIG_ID_RESOLUTION 等） |
| CheckSum | 配置校验和，所有配置项值之和 |

---

## 2. 功能目标

### 2.1 核心目标
ResBinManager 是一款用于嵌入式固件资源和配置管理的桌面工具，核心目标如下：

1. **资源解析**：解析 DestBin.bin 或独立 RES.BIN 文件，提取资源列表和元数据
2. **资源预览**：支持多种资源类型的可视化预览
3. **资源替换**：允许用户替换固件中的资源文件，支持大小变化处理
4. **配置管理**：解析、编辑、保存固件配置数据
5. **项目兼容**：支持多种项目类型的配置解析和映射

### 2.2 功能模块划分

| 模块 | 功能描述 | 关键文件 |
|------|----------|----------|
| 固件解析模块 | 解析 DestBin.bin 结构，提取 RES.BIN | DestBinParser.cs |
| 资源解析模块 | 解析 RES.BIN 索引表，提取资源元数据 | ResBinParser.cs |
| 资源替换模块 | 替换资源数据，更新索引表 | ResBinWriter.cs |
| 配置解析模块 | 解析固件配置区数据 | ConfigParser.cs |
| 配置管理模块 | 配置项展示、编辑、保存 | ConfigWriter.cs |
| UI 交互模块 | 界面展示和用户交互 | MainWindow.xaml, MainViewModel.cs |

---

## 3. 核心流程

### 3.1 固件加载流程

```
用户选择文件 → 验证文件存在 → 读取文件数据 → 验证文件头签名(BLDR)
    → 解析版本信息 → 解析启动扇区(flash_param) → 提取RES.BIN
    → 验证RES.BIN有效性 → 解析资源索引表 → 提取资源元数据 → 展示资源列表
```

**关键步骤说明：**

1. **文件验证**：检查文件大小（至少大于 PROGRAM_CODE_SIZE + 1024）
2. **签名验证**：检查偏移 0x04-0x07 是否为 "BLDR"
3. **启动扇区解析**：
   - 读取 bootSectorNum（偏移 0x09）
   - 计算 flash_param 偏移：`bootSectorNum × 16`
   - 解析 flash_param 结构获取资源区信息
4. **资源区定位**：
   - 资源区起始偏移：`resSectorNum × 512`（偏移 flash_param + 0x08）
   - 资源区大小：`resSizeSectors × 512`（偏移 flash_param + 0x0C）

### 3.2 资源替换流程

```
用户选择资源 → 检查资源有效性(Size > 0) → 弹出文件选择对话框
    → 保存原始数据(首次修改) → 类型特殊验证(WAV/Palette/GameMap/EncodingTable)
    → 检查大小差异 → 大小变化确认 → 执行替换 → 更新资源偏移 → 刷新UI
```

**大小差异处理规则：**

| 情况 | 处理方式 | 用户提示 |
|------|----------|----------|
| 新文件更小 | 直接覆盖，剩余空间填充 0xFF | 提示"剩余空间将以0xFF填充" |
| 新文件更大 | 扩展数组，移动后续数据 | 警告"将移动后续所有资源" |

### 3.3 配置加载流程

```
解析配置区地址 → 读取配置数据 → 验证校验和 → 构建配置项列表
    → 配置无效时提供选项(加载默认/从config.c加载) → 展示配置列表
```

**配置区地址计算：**
- 配置区位于资源区之后：`configAddress = resAddress + resSize`
- 4KB 对齐：`configAddress = (configAddress & 0xFFFFF000) + 0x1000`

### 3.4 配置保存流程

```
同步UI修改到配置数据 → 重新计算校验和 → 创建备份文件
    → 应用资源修改到固件数据 → 写入配置到固件 → 更新修改状态
```

---

## 4. 界面交互规范

### 4.1 主界面布局

**界面分区结构：**

| 区域 | 功能 | 控件类型 |
|------|------|----------|
| 顶部工具栏 | 文件操作、保存、配置管理 | Button、ComboBox |
| 左侧资源列表 | 展示资源 ID、名称、类型、偏移、大小、状态 | DataGrid |
| 右侧预览面板 | 资源可视化预览（图片/WAV/字体/十六进制） | TabControl |
| 底部状态栏 | 显示当前状态、文件信息 | TextBlock |

### 4.2 资源列表交互

**交互规范：**

1. **双击资源**：在预览面板显示资源内容
2. **右键菜单**：
   - 替换资源：打开文件选择对话框
   - 导出资源：保存资源到指定路径
   - 预览资源：在预览面板显示
3. **排序**：支持按 ID、名称、大小、偏移排序
4. **筛选**：支持按类型筛选资源

### 4.3 资源预览交互

**预览格式支持：**

| 资源类型 | 预览方式 | 控件 |
|----------|----------|------|
| Jpeg/Bitmap/Png | 图片显示 | Image |
| Wav/Mp3 | 音频播放 | MediaElement |
| Font | 字体预览（显示示例文字） | TextBlock |
| Binary/Palette | 十六进制编辑器 | DataGrid |
| Text | 文本内容 | TextBox |

### 4.4 配置编辑交互

**配置项显示规范：**

1. **名称显示**：使用 CONFIG_ID_* 枚举名称（如 CONFIG_ID_RESOLUTION）
2. **值显示**：根据配置类型格式化显示（如 OnOff 显示"开启"/"关闭"）
3. **下拉选项**：根据配置类型生成选项列表
4. **编辑方式**：ComboBox 下拉选择或直接输入

**配置类型与显示规则：**

| 类型 | 值范围 | 显示格式 | 示例 |
|------|--------|----------|------|
| OnOff | 0/R_STR_COM_OFF/R_STR_COM_ON | 关闭/开启 | 开启 |
| Level | 0-9 | 数字 | 5 |
| Sensitivity | 高/中/低对应值 | 中文描述 | 中 |
| Resolution | 分辨率偏移值 | 分辨率字符串 | 1920×1080 |
| LoopTime | 时间偏移值 | 时间描述 | 5分钟 |
| Numeric | 任意数值 | 数字 | 100 |
| RawHex | 任意数值 | 十六进制 | 0x8100008A |

### 4.5 对话框交互

**标准对话框类型：**

| 对话框 | 触发条件 | 按钮选项 |
|--------|----------|----------|
| 资源替换确认 | 新资源大小与原资源不同 | Yes/No |
| WAV验证 | WAV资源大小超出限制 | Yes/No |
| 配置空白处理 | 配置区无效 | Yes(默认)/No(config.c)/Cancel |
| 保存成功提示 | 配置保存成功 | OK |
| 错误提示 | 操作失败 | OK |

---

## 5. 数据处理规则

### 5.1 资源数据结构

**ResInfoEntry（资源索引表条目）：**

| 字段 | 类型 | 偏移 | 说明 |
|------|------|------|------|
| Offset | uint | 0 | 资源在文件中的偏移地址 |
| Length | uint | 4 | 资源长度（字节） |

**ResourceItem（资源项模型）：**

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | uint | 资源 ID（索引表序号） |
| Name | string | 资源名称（从 RES.H 解析） |
| Type | ResourceType | 资源类型枚举 |
| Offset | uint | 文件偏移地址 |
| Size | uint | 资源大小 |
| IsModified | bool | 是否已修改 |
| OriginalData | byte[] | 原始数据（用于恢复） |
| OriginalSize | uint | 原始大小 |

### 5.2 配置数据结构

**FirmwareConfigData（配置数据）：**

| 属性 | 类型 | 说明 |
|------|------|------|
| Flags | uint[] | 配置项数组（最大 127 项） |
| CheckSum | uint | 校验和（所有 Flags 之和） |
| ConfigAddress | uint | 配置区在固件中的地址 |
| IsValid | bool | 配置是否有效 |
| ProjectType | ProjectType | 项目类型 |

**FirmwareConfigItem（配置项）：**

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | ConfigId | 配置项 ID |
| Name | string | 显示名称 |
| Value | uint | 原始值 |
| ValueDisplay | string | 格式化显示值 |
| Options | List\<ConfigOption\> | 可选值列表 |

### 5.3 资源类型识别规则

**类型识别优先级：**

1. **文件头魔数检测**：
   - JPEG：0xFF 0xD8 0xFF
   - PNG：0x89 0x50 0x4E 0x47
   - WAV："RIFF" + "WAVE"
2. **扩展名匹配**：根据文件扩展名确定类型
3. **默认类型**：Binary（二进制数据）

**资源类型枚举：**

```csharp
public enum ResourceType
{
    Unknown = 0,
    Jpeg = 1,
    Bitmap = 2,
    Wav = 3,
    Binary = 4,
    Font = 5,
    Text = 6,
    Palette = 7,
    GameMap = 8,
    IconSelection = 9,
    EncodingTable = 10,
    OsdSource = 11,
    Png = 12,
    Mp3 = 13
}
```

### 5.4 配置类型推断规则

**类型推断流程：**

```
原始值 → 检查是否为R_ID_TYPE_STR格式(0x810000XX) → 提取偏移量
    → 偏移映射查找 → 索引上下文判断 → 确定最终类型
```

**索引上下文覆盖规则：**

| 索引范围 | 强制类型 | 说明 |
|----------|----------|------|
| 0-6 | Time | 日期时间配置 |
| 14 | Resolution | 视频分辨率 |
| 22 | LoopTime | 循环录像时间 |
| 19,31,37 | Sensitivity | 灵敏度配置（高/中/低） |
| 33-35 | OnOff | 开关配置（值为0时显示"关闭"） |

**特殊值处理：**

- **OnOff 类型**：值 0 和 R_STR_COM_OFF 都显示为"关闭"
- **Sensitivity 类型**：
  - 0x8100001A → 低
  - 0x8100001B → 中
  - 0x8100001C → 高

### 5.5 大小变化处理

**替换策略：**

| 场景 | 策略 | 数据处理 |
|------|------|----------|
| 新≤原 | 原地替换 | 直接覆盖，剩余空间填充 0xFF |
| 新>原 | 移位替换 | 扩展数组，从后往前复制后续数据，更新所有后续资源偏移 |

**DestBin 模式特殊处理：**

- RES.BIN 大小变化时，需调整 DestBin.bin 总大小
- 保持 4KB 对齐：`paddingNeeded = (4096 - (newSize % 4096)) % 4096`

---

## 6. 错误处理机制

### 6.1 文件操作错误

| 错误类型 | 触发条件 | 处理方式 | 用户提示 |
|----------|----------|----------|----------|
| 文件不存在 | 用户选择的文件路径无效 | 返回 false，设置 ErrorMessage | "File not found" |
| 文件过小 | 文件大小 < PROGRAM_CODE_SIZE + 1024 | 返回 false，设置 ErrorMessage | "File too small" |
| 签名无效 | 偏移 0x04-0x07 不是 "BLDR" | 返回 false，设置 ErrorMessage | "Invalid header" |
| 启动扇区解析失败 | flash_param 数据无效 | 返回 false，设置 ErrorMessage | "Failed to parse boot sector" |

### 6.2 资源操作错误

| 错误类型 | 触发条件 | 处理方式 | 用户提示 |
|----------|----------|----------|----------|
| 资源不存在 | Size == 0 | 禁止替换，弹出警告 | "Resource does not exist" |
| 资源 ID 无效 | ID >= 资源表长度 | 返回 false，设置 ErrorMessage | "Invalid resource ID" |
| WAV 验证失败 | WAV 文件过大或格式错误 | 取消替换 | "WAV replacement cancelled" |
| Palette 验证失败 | 调色板数据格式错误 | 取消替换 | "Palette replacement cancelled" |

### 6.3 配置操作错误

| 错误类型 | 触发条件 | 处理方式 | 用户提示 |
|----------|----------|----------|----------|
| 配置区无效 | 校验和验证失败或数据为空 | 提供三种处理选项 | "配置区为空白或无效" |
| config.c 解析失败 | 文件格式错误 | 弹出错误提示 | "加载 config.c 文件失败" |
| 配置保存失败 | 文件写入错误 | 弹出错误提示 | "配置保存失败" |

### 6.4 异常处理规范

**统一异常处理模式：**

```csharp
try
{
    // 业务逻辑
}
catch (Exception ex)
{
    ErrorMessage = $"操作失败: {ex.Message}";
    MessageBox.Show($"错误: {ex.Message}\n\n类型: {ex.GetType().Name}", 
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
    StatusMessage = "操作失败";
}
```

**调试日志规范：**

- 使用 `System.Diagnostics.Debug.WriteLine` 输出调试信息
- 日志格式：`[模块名] 操作描述: 详细信息`
- 关键节点必须输出日志（文件加载、资源替换、配置解析）

---

## 7. 性能要求

### 7.1 响应时间

| 操作 | 最大响应时间 | 说明 |
|------|-------------|------|
| 固件加载 | 500ms | 解析 DestBin.bin（≤ 4MB） |
| 资源列表展示 | 200ms | 填充 DataGrid |
| 资源替换 | 100ms/MB | 资源数据写入 |
| 配置解析 | 100ms | 解析 127 项配置 |
| 配置保存 | 300ms | 写入固件文件 |

### 7.2 内存使用

| 场景 | 最大内存占用 | 说明 |
|------|-------------|------|
| 单固件加载 | ≤ 10MB | 固件数据 + 资源列表 |
| 资源替换（大文件） | ≤ 20MB | 原始数据备份 + 新数据 |

### 7.3 兼容性要求

**固件版本兼容：**

- 支持标准 BLDR 格式的 DestBin.bin
- 支持独立 RES.BIN 文件（standalone 模式）
- 支持不同项目类型的配置映射

**项目类型支持：**

| 项目类型 | 配置项数量 | 特殊配置 |
|----------|-----------|----------|
| HM020F | 43 | 分辨率、灵敏度等 |
| JT529X | 不同 | 打印机相关配置 |

---

## 8. 验收标准

### 8.1 功能验收

**固件加载：**

- [ ] 能正确加载 DestBin.bin 文件
- [ ] 能正确加载独立 RES.BIN 文件
- [ ] 能解析并显示固件版本信息
- [ ] 能解析并显示资源列表

**资源替换：**

- [ ] 能替换 JPEG/Bitmap/Png 图片资源
- [ ] 能替换 WAV/Mp3 音频资源
- [ ] 能替换 Font 字体资源
- [ ] 能替换 Binary/Palette/GameMap/EncodingTable 资源
- [ ] 新文件更小时能正确填充 0xFF
- [ ] 新文件更大时能正确移动后续资源
- [ ] 首次替换时能保存原始数据用于恢复

**配置管理：**

- [ ] 能正确解析配置区数据
- [ ] 配置区无效时能提供默认配置选项
- [ ] 能从 config.c 文件加载配置
- [ ] 能正确显示配置项名称（CONFIG_ID_* 格式）
- [ ] 能正确显示配置项值（格式化）
- [ ] 能通过下拉框选择配置值
- [ ] 能保存配置到固件
- [ ] 保存时能自动计算校验和

**项目兼容：**

- [ ] 能自动检测项目类型
- [ ] 能根据项目类型应用配置映射
- [ ] 不同项目类型的配置解析互不干扰

### 8.2 错误处理验收

- [ ] 文件不存在时显示明确错误信息
- [ ] 文件格式错误时显示明确错误信息
- [ ] 资源不存在时禁止替换并显示警告
- [ ] 大小变化时显示确认对话框
- [ ] 所有异常都有对应的错误提示

### 8.3 性能验收

- [ ] 固件加载时间 ≤ 500ms
- [ ] 资源列表展示时间 ≤ 200ms
- [ ] 资源替换时间 ≤ 100ms/MB
- [ ] 配置保存时间 ≤ 300ms

### 8.4 UI 验收

- [ ] 资源列表数据垂直居中显示
- [ ] 配置项下拉框垂直居中显示
- [ ] 状态栏实时显示操作状态
- [ ] 对话框按钮布局合理
- [ ] 右键菜单功能完整

---

## 9. 附录

### 9.1 固件结构

```
DestBin.bin
├── [0x000000 - 0x00000B] 启动扇区头部
│   ├── [0x000000 - 0x000003] BLDR_VER
│   ├── [0x000004 - 0x000007] BLDR签名 ("BLDR")
│   ├── [0x000008] CheckSum
│   ├── [0x000009] BootSectorNum
│   └── [0x00000A - 0x00000B] BootFlag + Reserved
├── [0x000010 - 0x000013] MAGICKEY (0x01234567)
├── [flash_param_offset] flash_param 结构
│   ├── TextStart, TextSec, TextLen...
│   ├── [+0x08] resSectorNum (资源区起始扇区)
│   └── [+0x0C] resSizeSectors (资源区扇区数)
├── [0x000000 - resBinOffset] 程序代码段
├── [resBinOffset - end] RES.BIN 资源段
│   ├── [0 - firstResAddr] 资源索引表
│   │   └── 每个条目: Offset(4B) + Length(4B)
│   └── [firstResAddr - end] 资源数据区
└── [resBinEnd - end] 配置区 (4KB对齐后)
    ├── [0 - 508] SYSTEM_FLAY 结构 (127 × 4B Flags)
    └── [508 - 512] CheckSum (4B)
```

### 9.2 配置类型枚举

```csharp
public enum ConfigItemType
{
    RawHex = 0,
    Numeric = 1,
    OnOff = 2,
    Level = 3,
    Resolution = 4,
    LoopTime = 5,
    Time = 6,
    Sensitivity = 7
}
```

### 9.3 关键常量

| 常量 | 值 | 说明 |
|------|-----|------|
| PROGRAM_CODE_SIZE | 0x9DC00 (646,144) | 程序代码段标准大小 |
| SYSTEM_FLAY_SIZE | 512 | 配置区大小 |
| FLAGS_COUNT | 127 | 最大配置项数量 |
| BLDR_SIGNATURE | "BLDR" | Bootloader 签名 |
| MAGICKEY_STANDARD | 0x01234567 | 标准魔数值 |
| ALIGNMENT_SIZE | 4096 (0x1000) | 配置区对齐大小 |

### 9.4 文件依赖关系

```
ResBinManager.exe
├── Core/
│   ├── DestBinParser.cs      # DestBin.bin 解析
│   ├── ResBinParser.cs       # RES.BIN 解析
│   ├── ResBinWriter.cs       # RES.BIN 写入
│   ├── ConfigParser.cs       # 配置解析
│   ├── ConfigWriter.cs       # 配置写入
│   ├── ConfigCParser.cs      # config.c 解析
│   └── ConfigTemplateManager.cs  # 配置模板管理
├── Models/
│   ├── ResourceItem.cs       # 资源项模型
│   ├── FirmwareConfigItem.cs # 配置项模型
│   ├── ConfigItemDescriptor.cs # 配置描述符
│   ├── ConfigOptionsCache.cs # 配置选项缓存
│   └── ConfigDisplayFormatters.cs # 显示格式化
├── ViewModels/
│   └── MainViewModel.cs      # 主视图模型
└── Views/
    └── MainWindow.xaml       # 主界面
```
