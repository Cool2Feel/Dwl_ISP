# WAV 格式验证功能测试指南

## 📋 功能概述

WAV 资源替换功能现在包含完整的格式验证和参数对比，确保只有有效的 WAV 文件才能被替换到 RES.BIN 中。

## ✨ 新增功能

### 1. WAV 格式验证

**验证内容**：
- ✅ RIFF 魔数检查
- ✅ WAVE 标识验证
- ✅ 文件大小检查（最小 44 字节）
- ✅ 音频格式解析（PCM/其他）
- ✅ 采样率合理性检查（8kHz - 192kHz）
- ✅ 声道数验证（1-8）
- ✅ 位深度支持检查（8/16/24/32-bit）
- ✅ Data chunk 查找

### 2. 参数对比显示

当替换已有 WAV 资源时，会显示详细对比：
- 采样率变化
- 声道数变化
- 位深度变化
- 时长差异
- 文件大小差异

### 3. 智能警告系统

根据以下情况发出警告：
- ⚠️ 极低采样率（< 8kHz）
- ⚠️ 高采样率（> 48kHz）
- ⚠️ 8-bit 低动态范围
- ⚠️ >16-bit 可能不被完全支持
- ⚠️ 多声道（>2）可能被混音
- ⚠️ 长音频（>10秒）影响性能
- ⚠️ 大文件（>100KB）增加固件体积

## 🧪 测试步骤

### 测试 1: 有效 WAV 文件替换

**准备**：
1. 打开 ResBinManager
2. 加载包含 WAV 资源的 RES.BIN 文件
3. 选中一个 WAV 资源（例如 ID=5, RES_BEEP）

**操作**：
1. 点击 "Replace" 按钮
2. 选择一个有效的 WAV 文件
3. 查看验证对话框

**预期结果**：
```
WAV Resource Replacement

New File Information:
✓ Valid WAV file
  Format: PCM
  Sample Rate: 44100 Hz
  Channels: Mono
  Bits: 16-bit
  Duration: 0.50s
  Size: 44,144 bytes

Parameter Comparison:
✓ Sample Rate: 44100 Hz
✓ Channels: Mono
✓ Bits: 16-bit
ℹ Duration: 0.45s → 0.50s (+0.05s)
ℹ File Size: 39,734 → 44,144 bytes (+4,410)

Continue with replacement?
```

### 测试 2: 无效文件格式

**准备**：
1. 准备一个非 WAV 文件（例如 .mp3 或 .txt）

**操作**：
1. 选中 WAV 资源
2. 点击 "Replace"
3. 选择非 WAV 文件

**预期结果**：
```
Invalid WAV file:

Invalid file format. Expected 'RIFF', got 'ID3 '
```
对话框标题："Validation Failed"，图标为错误标志。

### 测试 3: 损坏的 WAV 文件

**准备**：
1. 创建一个截断的 WAV 文件（小于 44 字节）

**操作**：
1. 尝试替换

**预期结果**：
```
Invalid WAV file:

File too small (20 bytes). Minimum WAV size is 44 bytes.
```

### 测试 4: 参数变化警告

**准备**：
1. 原始 WAV: 44.1kHz, 16-bit, Mono
2. 新 WAV: 22.05kHz, 8-bit, Stereo

**操作**：
1. 执行替换

**预期结果**：
```
WAV Resource Replacement

New File Information:
✓ Valid WAV file
  Format: PCM
  Sample Rate: 22050 Hz
  Channels: Stereo
  Bits: 8-bit
  Duration: 0.50s
  Size: 22,094 bytes

⚠ Warnings:
  - 8-bit audio has limited dynamic range.

Parameter Comparison:
⚠ Sample Rate: 44100 Hz → 22050 Hz
⚠ Channels: Mono → Stereo
⚠ Bits: 16-bit → 8-bit
ℹ Duration: 0.45s → 0.50s (+0.05s)
ℹ File Size: 39,734 → 22,094 bytes (-17,640)

Please review the warnings above.

Continue with replacement?
```
对话框图标为警告标志（黄色三角形）。

### 测试 5: 大文件警告

**准备**：
1. 准备一个 >100KB 的 WAV 文件

**操作**：
1. 执行替换

**预期结果**：
```
⚠ Warnings:
  - Large file size (150 KB). May increase firmware size significantly.
```

### 测试 6: 取消替换

**操作**：
1. 在验证对话框中点击 "No"

**预期结果**：
- 替换被取消
- StatusMessage 显示 "WAV replacement cancelled"
- 资源未被修改

## 📊 验证逻辑流程图

```
用户选择 WAV 文件
    ↓
检查文件大小 (>= 44 bytes?)
    ├─ No → 错误: "File too small"
    └─ Yes ↓
检查 RIFF 魔数
    ├─ Invalid → 错误: "Invalid file format"
    └─ Valid ↓
检查 WAVE 标识
    ├─ Invalid → 错误: "Not a WAV file"
    └─ Valid ↓
解析 WAV 头部信息
    ├─ Failed → 错误: "Failed to parse WAV header"
    └─ Success ↓
检查参数合理性
    ├─ SampleRate < 8kHz or > 192kHz → 警告
    ├─ Channels < 1 or > 8 → 错误
    ├─ BitsPerSample not in {8,16,24,32} → 错误
    └─ All valid ↓
生成警告列表
    ├─ Low/High sample rate → 警告
    ├─ 8-bit audio → 警告
    ├─ Multi-channel → 警告
    ├─ Long duration → 警告
    └─ Large file → 警告
    ↓
显示验证结果和对比
    ↓
用户确认?
    ├─ Yes → 执行替换
    └─ No → 取消
```

## 🔍 代码位置

### 核心文件

1. **WavValidator.cs** - 验证器类
   - `Validate()` - 主验证函数
   - `CompareWavInfo()` - 参数对比
   - `AddWarnings()` - 生成警告

2. **MainViewModel.cs** - 集成到替换流程
   - `ExecuteReplace()` - 调用验证
   - `ValidateAndConfirmWavReplacement()` - 验证和确认对话框

3. **WavInfoParser.cs** - 基础解析（已存在）
   - `Parse()` - 解析 WAV 头部
   - `IsValidWav()` - 快速验证

## 💡 使用建议

### 最佳实践

1. **保持参数一致**
   - 尽量使用与原始文件相同的采样率和位深度
   - 避免不必要的格式转换

2. **控制文件大小**
   - 短音效：< 50KB
   - 背景音乐：< 200KB
   - 过大的文件会影响固件更新速度

3. **选择合适的采样率**
   - 语音提示：8kHz - 16kHz
   - 一般音效：22.05kHz - 44.1kHz
   - 高质量音乐：44.1kHz - 48kHz

4. **注意声道数**
   - 单声道（Mono）适合大多数嵌入式设备
   - 立体声（Stereo）需要设备支持

### 常见问题

**Q: 为什么我的 WAV 文件被拒绝？**
A: 检查以下几点：
- 文件是否以 RIFF 开头
- 是否包含 WAVE 标识
- 文件大小是否 >= 44 字节
- 采样率是否在 8kHz-192kHz 范围内
- 位深度是否为 8/16/24/32-bit

**Q: 警告会影响替换吗？**
A: 不会。警告只是提醒，您仍然可以点击 "Yes" 继续替换。

**Q: 如何消除警告？**
A: 
- 降低采样率到 48kHz 以下
- 使用 16-bit 位深度
- 使用单声道
- 缩短音频时长
- 压缩文件大小

## 🎯 下一步改进

可能的增强方向：
1. 添加波形可视化预览
2. 支持自动格式转换（重采样）
3. 批量替换多个 WAV 文件
4. WAV 文件库管理
5. 导出验证报告

---

**测试完成检查清单**：
- [ ] 测试 1: 有效 WAV 替换 ✓
- [ ] 测试 2: 无效文件格式 ✓
- [ ] 测试 3: 损坏的 WAV 文件 ✓
- [ ] 测试 4: 参数变化警告 ✓
- [ ] 测试 5: 大文件警告 ✓
- [ ] 测试 6: 取消替换 ✓

祝测试顺利！🚀
