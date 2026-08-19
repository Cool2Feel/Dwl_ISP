# 资源替换大小差异确认功能

## 📋 概述

在 ResBinManager 中，当用户执行资源替换操作时，如果新文件与原文件大小不同，系统会自动弹出确认对话框，显示详细的大小对比信息，让用户明确知晓替换的影响。

---

## 🎯 功能特性

### 1. 智能检测

- ✅ **任何大小差异**都会触发确认对话框（不仅仅是 2 倍）
- ✅ 区分**变大**和**变小**两种情况
- ✅ 显示详细的字节数和百分比变化
- ✅ 提供人类可读的文件大小格式（B/KB/MB/GB）

### 2. 差异化提示

#### 情况 A：新文件更大 ⚠️

```
┌─────────────────────────────────────────────┐
│         Confirm Replacement                  │
├─────────────────────────────────────────────┤
│ New file is LARGER than original:            │
│                                              │
│ Original: 102,400 bytes (100.00 KB)          │
│ New:      153,600 bytes (150.00 KB)          │
│ Difference: +51,200 bytes (+50.0%)           │
│                                              │
│ ⚠️ This will shift all subsequent resources  │
│    in the file.                              │
│ The file size will increase by 50.00 KB.     │
│                                              │
│ Continue with replacement?                   │
│                                              │
│        [Yes]          [No]                   │
└─────────────────────────────────────────────┘
```

**关键信息**：
- ⚠️ 警告图标
- 显示增加的具体大小
- 明确说明会影响后续所有资源
- 告知文件总大小会增加多少

#### 情况 B：新文件更小 ✓

```
┌─────────────────────────────────────────────┐
│         Confirm Replacement                  │
├─────────────────────────────────────────────┤
│ New file is SMALLER than original:           │
│                                              │
│ Original: 204,800 bytes (200.00 KB)          │
│ New:      153,600 bytes (150.00 KB)          │
│ Difference: -51,200 bytes (-25.0%)           │
│                                              │
│ ✓ The remaining space will be filled with    │
│   0xFF padding.                              │
│ No other resources will be affected.         │
│                                              │
│ Continue with replacement?                   │
│                                              │
│        [Yes]          [No]                   │
└─────────────────────────────────────────────┘
```

**关键信息**：
- ✓ 问号图标（非警告）
- 显示减少的具体大小
- 说明会用 0xFF 填充剩余空间
- 明确不会影响其他资源

---

## 🔧 技术实现

### 核心代码位置

**文件**: `ViewModels/MainViewModel.cs`

**方法**: `ExecuteReplace()` (第 608-667 行)

### 实现逻辑

```csharp
// 1. 计算大小差异
long sizeDiff = newData.Length - (long)SelectedResource.Size;
double sizeDiffPercent = SelectedResource.Size > 0 
    ? (double)sizeDiff / SelectedResource.Size * 100 
    : 0;

// 2. 判断是否需要确认（任何差异都需要）
bool needsConfirmation = sizeDiff != 0;

if (needsConfirmation)
{
    // 3. 根据差异方向构建不同的消息
    if (sizeDiff > 0)
    {
        // 新文件更大 - 警告消息
        message = $"New file is LARGER than original:\n\n" +
                 $"Original: {SelectedResource.Size:N0} bytes ({FormatFileSize(SelectedResource.Size)})\n" +
                 $"New:      {newData.Length:N0} bytes ({FormatFileSize((uint)newData.Length)})\n" +
                 $"Difference: +{sizeDiff:N0} bytes (+{sizeDiffPercent:F1}%)\n\n" +
                 $"⚠️ This will shift all subsequent resources in the file.\n" +
                 $"The file size will increase by {FormatFileSize((uint)sizeDiff)}.\n\n" +
                 $"Continue with replacement?";
        icon = MessageBoxImage.Warning;
    }
    else
    {
        // 新文件更小 - 普通消息
        message = $"New file is SMALLER than original:\n\n" +
                 $"Original: {SelectedResource.Size:N0} bytes ({FormatFileSize(SelectedResource.Size)})\n" +
                 $"New:      {newData.Length:N0} bytes ({FormatFileSize((uint)newData.Length)})\n" +
                 $"Difference: {sizeDiff:N0} bytes ({sizeDiffPercent:F1}%)\n\n" +
                 $"✓ The remaining space will be filled with 0xFF padding.\n" +
                 $"No other resources will be affected.\n\n" +
                 $"Continue with replacement?";
        icon = MessageBoxImage.Question;
    }
    
    // 4. 显示确认对话框
    var result = MessageBox.Show(message, "Confirm Replacement", 
                                 MessageBoxButton.YesNo, icon);
    
    if (result != MessageBoxResult.Yes)
    {
        StatusMessage = "Replace cancelled by user";
        return;
    }
}
```

### 辅助方法：FormatFileSize

```csharp
/// <summary>
/// 格式化文件大小显示
/// </summary>
private string FormatFileSize(uint bytes)
{
    if (bytes < 1024)
        return $"{bytes} B";
    else if (bytes < 1024 * 1024)
        return $"{bytes / 1024.0:F2} KB";
    else if (bytes < 1024 * 1024 * 1024)
        return $"{bytes / (1024.0 * 1024):F2} MB";
    else
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
}
```

**功能**：
- 自动选择合适的单位（B/KB/MB/GB）
- 保留两位小数
- 提高可读性

---

## 📊 实际应用场景

### 场景 1：替换小图标（变大）

```
原文件：icon_home.png = 5,120 bytes (5.00 KB)
新文件：icon_home_hd.png = 15,360 bytes (15.00 KB)

对话框显示：
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
New file is LARGER than original:

Original: 5,120 bytes (5.00 KB)
New:      15,360 bytes (15.00 KB)
Difference: +10,240 bytes (+200.0%)

⚠️ This will shift all subsequent resources in the file.
The file size will increase by 10.00 KB.

Continue with replacement?
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

用户选择：
- Yes → 执行替换，移动后续资源
- No → 取消操作
```

### 场景 2：替换背景图（变小）

```
原文件：bg_main.jpg = 512,000 bytes (500.00 KB)
新文件：bg_main_optimized.jpg = 307,200 bytes (300.00 KB)

对话框显示：
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
New file is SMALLER than original:

Original: 512,000 bytes (500.00 KB)
New:      307,200 bytes (300.00 KB)
Difference: -204,800 bytes (-40.0%)

✓ The remaining space will be filled with 0xFF padding.
No other resources will be affected.

Continue with replacement?
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

用户选择：
- Yes → 执行替换，填充空白
- No → 取消操作
```

### 场景 3：替换音频文件（大幅增加）

```
原文件：sound_click.wav = 10,240 bytes (10.00 KB)
新文件：sound_click_hq.wav = 102,400 bytes (100.00 KB)

对话框显示：
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
New file is LARGER than original:

Original: 10,240 bytes (10.00 KB)
New:      102,400 bytes (100.00 KB)
Difference: +92,160 bytes (+900.0%)

⚠️ This will shift all subsequent resources in the file.
The file size will increase by 90.00 KB.

Continue with replacement?
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

注意：+900% 的增长会显著影响文件结构！
```

---

## 🎨 UI 设计要点

### 1. 视觉区分

| 元素 | 变大 | 变小 |
|------|------|------|
| 图标 | ⚠️ Warning | ❓ Question |
| 标题 | LARGER | SMALLER |
| 差异符号 | + | - |
| 影响说明 | 移位所有后续资源 | 仅填充空白 |

### 2. 信息层次

```
第一层：主要变化（LARGER/SMALLER）
第二层：具体数值（字节数 + 人类可读格式）
第三层：变化百分比
第四层：影响说明
第五层：操作确认
```

### 3. 数字格式化

- **千位分隔符**：`102,400` 而非 `102400`
- **百分比精度**：保留 1 位小数 `+50.0%`
- **文件大小**：自动选择合适单位

---

## 🔍 与旧版本的对比

### 旧版本（v1.0）

```csharp
// 仅在超过 2 倍时才警告
if (newData.Length > SelectedResource.Size * 2)
{
    MessageBox.Show("New file is much larger...", ...);
}
```

**问题**：
- ❌ 小幅增长（如 +10%）不会警告
- ❌ 变小也不会提示
- ❌ 信息不够详细
- ❌ 没有说明具体影响

### 新版本（v2.0）✅

```csharp
// 任何大小差异都确认
bool needsConfirmation = sizeDiff != 0;

if (needsConfirmation)
{
    // 详细的对比信息
    // 区分变大/变小
    // 说明具体影响
}
```

**优势**：
- ✅ 所有变化都会提示
- ✅ 信息完整透明
- ✅ 用户完全知情
- ✅ 防止意外操作

---

## 💡 用户体验优化

### 1. 即时反馈

- 选择文件后立即显示对比
- 清晰的操作后果说明
- 明确的 Yes/No 选项

### 2. 信息完整性

- 原始大小（字节 + 可读格式）
- 新文件大小（字节 + 可读格式）
- 差异值（绝对值 + 百分比）
- 影响范围说明

### 3. 安全保护

- 默认需要用户确认
- 用户可以随时取消
- 状态栏显示取消原因

---

## 🛠️ 未来增强建议

### 1. 阈值配置

允许用户设置是否跳过小变化的确认：

```csharp
// 在配置文件中
public class AppSettings
{
    public double ConfirmationThreshold { get; set; } = 0.0; // 0% = 总是确认
    // 或设置为 5.0 = 小于 5% 的变化不确认
}
```

### 2. 批量替换预览

对于批量替换操作，显示汇总信息：

```
Batch Replace Summary:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Total Resources: 5
Size Increase: +256 KB
Affected Resources: 12
Estimated Time: ~2 seconds

[Proceed] [Cancel]
```

### 3. 历史记录

记录用户的替换决策：

```csharp
class ReplacementHistory
{
    DateTime Timestamp { get; set; }
    uint ResourceId { get; set; }
    long SizeDiff { get; set; }
    bool UserConfirmed { get; set; }
}
```

---

## 📝 测试用例

### 测试 1：相同大小

```
输入：原文件 100KB，新文件 100KB
预期：不弹出确认对话框，直接替换
结果：✓ PASS
```

### 测试 2：小幅增长

```
输入：原文件 100KB，新文件 105KB (+5%)
预期：弹出确认对话框，显示详细信息
结果：✓ PASS
```

### 测试 3：大幅增长

```
输入：原文件 10KB，新文件 100KB (+900%)
预期：弹出警告对话框，强调影响
结果：✓ PASS
```

### 测试 4：变小

```
输入：原文件 200KB，新文件 150KB (-25%)
预期：弹出确认对话框，说明填充机制
结果：✓ PASS
```

### 测试 5：用户取消

```
输入：任意大小差异，用户点击 No
预期：操作取消，状态栏显示 "Replace cancelled by user"
结果：✓ PASS
```

---

## 🔗 相关文档

- [RESOURCE_REPLACE_SIZE_HANDLING_DETAILED.md](./RESOURCE_REPLACE_SIZE_HANDLING_DETAILED.md) - 资源替换大小处理机制详解
- [SMART_FILE_OPERATIONS_INTEGRATION.md](./SMART_FILE_OPERATIONS_INTEGRATION.md) - 智能文件操作集成
- [DESTBIN_LOAD_FAILURE_DIAGNOSIS.md](./DESTBIN_LOAD_FAILURE_DIAGNOSIS.md) - DestBin 诊断指南

---

## ✅ 总结

### 改进内容

1. ✅ **全面覆盖**：任何大小差异都会触发确认
2. ✅ **详细对比**：显示字节数、百分比、可读格式
3. ✅ **差异化提示**：变大/变小有不同的说明
4. ✅ **用户友好**：清晰的图标和文案
5. ✅ **安全可靠**：用户完全掌控操作

### 核心价值

- 🎯 **透明度**：用户清楚知道每次替换的影响
- 🛡️ **安全性**：防止意外的大文件替换
- 💡 **易用性**：直观的信息展示和决策支持

---

**版本**: v2.0  
**更新日期**: 2026-05-19  
**作者**: ResBinManager Team
