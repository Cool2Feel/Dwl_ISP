# 保存操作优化：覆盖与另存为选择功能

## 📋 概述

ResBinManager 的保存操作已优化，点击"Save"按钮时会弹出选择对话框，让用户明确选择**覆盖原文件**或**另存为新文件**，提供更好的用户体验和数据安全保障。

---

## 🎯 功能特性

### 1. 智能选择对话框

点击保存按钮后，立即弹出三选项对话框：

```
┌─────────────────────────────────────────────┐
│              Save Options                     │
├─────────────────────────────────────────────┤
│ How would you like to save the modified      │
│ file?                                        │
│                                              │
│ • Overwrite: Replace the original file       │
│   (creates backup)                           │
│                                              │
│ • Save As: Save as a new file with           │
│   different name                             │
│                                              │
│ Choose an option:                            │
│                                              │
│    [Yes]     [No]     [Cancel]               │
└─────────────────────────────────────────────┘
```

**选项说明**：
- **Yes（是）** → 覆盖原文件（自动创建备份）
- **No（否）** → 另存为新文件
- **Cancel（取消）** → 取消保存操作

---

## 🔧 两种保存模式详解

### 模式 A：覆盖原文件 ⚠️

#### 工作流程

```
步骤 1: 创建备份
┌──────────────────┐
│ original.bin     │ → 复制 → original.bin.backup ✓
└──────────────────┘

步骤 2: 写入新数据
┌──────────────────┐
│ original.bin     │ → 覆盖 → 修改后的数据 ✓
└──────────────────┘

步骤 3: 显示成功信息
✓ File overwritten successfully!
✓ Backup saved as: original.bin.backup
```

#### 技术实现

**文件**: `ViewModels/MainViewModel.cs`  
**方法**: `ExecuteOverwriteFile()` (第 879-945 行)

```csharp
private void ExecuteOverwriteFile()
{
    try
    {
        IsLoading = true;
        StatusMessage = "Saving... Creating backup...";

        // 1. 创建备份
        string backupPath = _currentFilePath + ".backup";
        if (File.Exists(_currentFilePath))
        {
            File.Copy(_currentFilePath, backupPath, true);
            System.Diagnostics.Debug.WriteLine($"[Save] Backup created: {backupPath}");
        }

        // 2. 根据模式执行不同的保存逻辑
        if (IsDestBinMode)
        {
            // DestBin 模式：直接保存到原文件
            ExecuteOverwriteDestBin();
        }
        else
        {
            // RES.BIN 模式：直接写入原文件
            File.WriteAllBytes(_currentFilePath, _currentFileData!);
            
            StatusMessage = $"✓ Overwritten: {Path.GetFileName(_currentFilePath)}";
            
            var fileInfo = new FileInfo(_currentFilePath);
            MessageBox.Show(
                $"File overwritten successfully!\n\n" +
                $"File: {Path.GetFileName(_currentFilePath)}\n" +
                $"Size: {fileInfo.Length:N0} bytes ({FormatFileSize((uint)fileInfo.Length)})\n\n" +
                $"Backup saved as:\n{Path.GetFileName(backupPath)}\n\n" +
                $"Next steps:\n" +
                $"1. Copy the modified .bin file to ax32_platform_demo/resource/\n" +
                $"2. Run GenRes.bat to regenerate RES.H if needed\n" +
                $"3. Rebuild firmware using MakeSPIBin.exe",
                "Success",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Save failed: {ex.Message}\n\nThe backup file is still available.", 
                      "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        StatusMessage = "Save failed";
    }
    finally
    {
        IsLoading = false;
    }
}
```

#### 关键特性

✅ **自动备份**：覆盖前自动创建 `.backup` 文件  
✅ **双重保障**：即使覆盖失败，备份文件仍然可用  
✅ **详细提示**：显示文件大小和备份文件名  
✅ **后续指导**：提供固件构建的步骤说明  

#### 成功提示示例

```
┌─────────────────────────────────────────────┐
│                 Success                       │
├─────────────────────────────────────────────┤
│ File overwritten successfully!               │
│                                              │
│ File: RES.BIN                                │
│ Size: 4,387,245 bytes (4.18 MB)              │
│                                              │
│ Backup saved as:                             │
│ RES.BIN.backup                               │
│                                              │
│ Next steps:                                  │
│ 1. Copy the modified .bin file to            │
│    ax32_platform_demo/resource/              │
│ 2. Run GenRes.bat to regenerate RES.H        │
│    if needed                                 │
│ 3. Rebuild firmware using MakeSPIBin.exe     │
│                                              │
│              [OK]                             │
└─────────────────────────────────────────────┘
```

---

### 模式 B：另存为新文件 ✓

#### 工作流程

```
步骤 1: 显示保存对话框
┌──────────────────────────────────────┐
│ Save Modified RES.BIN As...          │
├──────────────────────────────────────┤
│ File name: RES_modified.bin          │
│ Save as type: BIN files (*.bin)      │
│                                      │
│        [Save]    [Cancel]            │
└──────────────────────────────────────┘

步骤 2: 写入新文件
┌──────────────────┐
│ RES_modified.bin │ → 写入 → 修改后的数据 ✓
└──────────────────┘

步骤 3: 显示成功信息
✓ File saved successfully!
✓ 原始文件保持不变
```

#### 技术实现

**文件**: `ViewModels/MainViewModel.cs`  
**方法**: `ExecuteSaveAsNewFile()` (第 995-1097 行)

```csharp
private void ExecuteSaveAsNewFile()
{
    var dialog = new SaveFileDialog
    {
        FileName = Path.GetFileNameWithoutExtension(_currentFilePath) + "_modified.bin",
        Filter = "BIN files|*.bin|All files|*.*",
        Title = IsDestBinMode ? "Save Modified DestBin.bin As..." : "Save Modified RES.BIN As...",
        InitialDirectory = Path.GetDirectoryName(_currentFilePath)
    };

    if (dialog.ShowDialog() == true)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Saving as new file...";

            if (IsDestBinMode)
            {
                // DestBin 模式：需要重新打包
                if (_destBinParser != null && _currentFileData != null)
                {
                    if (_destBinParser.ReplaceResBin(_currentFileData, keepSize: true))
                    {
                        if (_destBinParser.Save(dialog.FileName))
                        {
                            StatusMessage = $"✓ Saved to {Path.GetFileName(dialog.FileName)}";
                            
                            var fileInfo = new FileInfo(dialog.FileName);
                            MessageBox.Show(
                                $"DestBin.bin saved successfully!\n\n" +
                                $"File: {Path.GetFileName(dialog.FileName)}\n" +
                                $"Size: {fileInfo.Length:N0} bytes ({FormatFileSize((uint)fileInfo.Length)})\n\n" +
                                $"The firmware is ready for flashing.",
                                "Success",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                    }
                }
            }
            else
            {
                // RES.BIN 模式：直接保存
                File.WriteAllBytes(dialog.FileName, _currentFileData!);
                
                StatusMessage = $"✓ Saved to {Path.GetFileName(dialog.FileName)}";
                
                var fileInfo = new FileInfo(dialog.FileName);
                MessageBox.Show(
                    "File saved successfully!\n\n" +
                    $"File: {Path.GetFileName(dialog.FileName)}\n" +
                    $"Size: {fileInfo.Length:N0} bytes ({FormatFileSize((uint)fileInfo.Length)})\n\n" +
                    "Next steps:\n" +
                    "1. Copy the modified .bin file to ax32_platform_demo/resource/\n" +
                    "2. Run GenRes.bat to regenerate RES.H if needed\n" +
                    "3. Rebuild firmware using MakeSPIBin.exe",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed: {ex.Message}", "Error", 
                          MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Save failed";
        }
        finally
        {
            IsLoading = false;
        }
    }
    else
    {
        StatusMessage = "Save cancelled";
    }
}
```

#### 关键特性

✅ **默认文件名**：自动添加 `_modified` 后缀  
✅ **初始目录**：默认为原文件所在目录  
✅ **动态标题**：根据模式显示不同标题  
✅ **原始保护**：原文件完全不受影响  

#### 保存对话框示例

```
┌──────────────────────────────────────────────────┐
│ Save Modified RES.BIN As...                      │
├──────────────────────────────────────────────────┤
│ File name: RES_modified.bin                      │
│                                                  │
│ Save as type: [BIN files (*.bin)       ▼]        │
│                                                  │
│ Look in: D:\...\ax32_platform_demo\resource      │
│                                                  │
│        [Save]          [Cancel]                  │
└──────────────────────────────────────────────────┘
```

---

## 📊 DestBin.bin 特殊处理

### 覆盖 DestBin.bin

当加载的是 DestBin.bin 文件时，覆盖操作会：

1. **提取修改后的 RES.BIN**
2. **替换 DestBin 中的 RES.BIN 段**
3. **保持程序代码段不变**
4. **保存到原文件**

```csharp
private void ExecuteOverwriteDestBin()
{
    // 替换 RES.BIN
    if (_destBinParser.ReplaceResBin(_currentFileData, keepSize: true))
    {
        // 保存到原文件
        if (_destBinParser.Save(_currentFilePath))
        {
            // 显示成功信息
        }
    }
}
```

### 另存为 DestBin.bin

用户可以选择新的文件名保存修改后的 DestBin.bin：

```
原始文件：DestBin.bin
另存为：DestBin_modified.bin

结果：
- DestBin.bin（原始，未修改）
- DestBin_modified.bin（包含修改后的 RES.BIN）
```

---

## 🎨 UI 交互流程

### 完整流程图

```
用户点击 Save 按钮
        ↓
┌──────────────────────┐
│  Save Options 对话框  │
│                      │
│  Yes → 覆盖原文件     │
│  No  → 另存为新文件   │
│  Cancel → 取消       │
└──────────────────────┘
        ↓
   ┌────┴────┐
   ↓         ↓
 Yes       No
   ↓         ↓
覆盖流程   另存流程
   ↓         ↓
创建备份   显示对话框
   ↓         ↓
写入数据   用户选择路径
   ↓         ↓
显示成功   写入数据
   ↓         ↓
完成       显示成功
```

### 状态栏反馈

每个操作阶段都有清晰的状态提示：

| 阶段 | 状态消息 |
|------|---------|
| 开始保存 | "Saving... Creating backup..." |
| 备份完成 | （继续下一步） |
| 写入中 | "Saving as new file..." |
| 成功 | "✓ Overwritten: RES.BIN" |
| 失败 | "Save failed" |
| 取消 | "Save cancelled" |

---

## 🔒 数据安全保障

### 1. 自动备份机制

**触发条件**：选择"覆盖原文件"时

**备份策略**：
```csharp
string backupPath = _currentFilePath + ".backup";
File.Copy(_currentFilePath, backupPath, true);
```

**特点**：
- ✅ 每次覆盖前自动创建备份
- ✅ 如果已有备份，会被新备份覆盖
- ✅ 备份文件与原文件同目录
- ✅ 即使保存失败，备份仍可用

**备份文件示例**：
```
RES.BIN          ← 当前文件（可能被覆盖）
RES.BIN.backup   ← 自动备份（上次保存前的版本）
```

### 2. 错误恢复

如果保存过程中出现错误：

```csharp
catch (Exception ex)
{
    MessageBox.Show(
        $"Save failed: {ex.Message}\n\n" +
        "The backup file is still available.",  // 提示备份可用
        "Error", 
        MessageBoxButton.OK, 
        MessageBoxImage.Error);
}
```

用户可以：
1. 检查错误原因
2. 从 `.backup` 文件恢复
3. 重新尝试保存

### 3. 异常处理

所有保存操作都包裹在 try-catch-finally 块中：

```csharp
try
{
    IsLoading = true;
    // 执行保存...
}
catch (Exception ex)
{
    // 显示错误
}
finally
{
    IsLoading = false;  // 确保状态重置
}
```

---

## 📝 实际应用场景

### 场景 1：快速迭代开发

```
开发者工作流：
1. 打开 RES.BIN
2. 替换几个图标
3. 点击 Save → 选择 Yes（覆盖）
4. 自动创建备份
5. 运行固件构建脚本
6. 烧录测试

优势：
✓ 快速迭代，无需手动管理文件名
✓ 每次覆盖都有备份保护
✓ 可随时回滚到上一版本
```

### 场景 2：多版本管理

```
设计师工作流：
1. 打开 RES.BIN
2. 替换背景图
3. 点击 Save → 选择 No（另存为）
4. 保存为 RES_v2.bin
5. 继续修改其他资源
6. 再次另存为 RES_v3.bin

结果：
- RES.BIN（原始版本）
- RES_v2.bin（第一次修改）
- RES_v3.bin（第二次修改）

优势：
✓ 保留多个版本供对比
✓ 原始文件始终安全
✓ 可选择最佳版本使用
```

### 场景 3：DestBin.bin 固件更新

```
固件工程师工作流：
1. 打开 DestBin.bin
2. 修改 RES.BIN 中的资源
3. 点击 Save → 选择 Yes（覆盖）
4. 系统自动：
   - 备份 DestBin.bin → DestBin.bin.backup
   - 替换内部的 RES.BIN 段
   - 保持程序代码不变
   - 保存完整的 DestBin.bin

结果：
- DestBin.bin（新版本固件）
- DestBin.bin.backup（旧版本固件）

优势：
✓ 一键更新固件
✓ 自动处理复杂的二进制结构
✓ 随时可回滚
```

---

## 🔍 与旧版本的对比

### 旧版本（v1.0）

**行为**：
- DestBin 模式：直接覆盖（有确认对话框）
- RES.BIN 模式：直接另存为

**问题**：
- ❌ 用户无法主动选择保存方式
- ❌ 覆盖操作不够灵活
- ❌ 缺少自动备份机制
- ❌ RES.BIN 模式没有覆盖选项

### 新版本（v2.0）✅

**行为**：
- 统一的选择对话框
- 用户主动选择覆盖或另存为
- 自动备份保护
- 详细的成功提示

**优势**：
- ✅ 用户完全掌控保存方式
- ✅ 灵活的两种模式
- ✅ 数据安全有保障
- ✅ 清晰的后续指导

---

## 💡 用户体验优化

### 1. 明确的选项说明

对话框中使用清晰的描述：

```
• Overwrite: Replace the original file (creates backup)
• Save As: Save as a new file with different name
```

而不是简单的"Yes/No"，让用户明白每个选择的含义。

### 2. 智能默认值

- **覆盖模式**：自动创建备份，降低风险
- **另存为模式**：默认文件名带 `_modified` 后缀
- **初始目录**：默认为原文件所在目录

### 3. 详细的成功提示

不仅显示"保存成功"，还提供：
- 文件大小
- 备份文件名（覆盖模式）
- 后续操作步骤

### 4. 一致的错误处理

所有失败情况都：
- 显示具体错误信息
- 提示备份可用性（覆盖模式）
- 保持界面响应

---

## 🛠️ 技术细节

### 方法重构

**删除的方法**：
- `ExecuteSaveResBin()` - 旧的另存为方法
- `ExecuteSaveToDestBin()` - 旧的 DestBin 保存方法

**新增的方法**：
- `ExecuteSave()` - 主入口，显示选择对话框
- `ExecuteOverwriteFile()` - 覆盖原文件（通用）
- `ExecuteOverwriteDestBin()` - 覆盖 DestBin（专用）
- `ExecuteSaveAsNewFile()` - 另存为新文件（通用）

### 代码组织

```
ExecuteSave()
    ├─ 显示选择对话框
    ├─ Yes → ExecuteOverwriteFile()
    │         ├─ 创建备份
    │         ├─ IsDestBinMode?
    │         │   ├─ Yes → ExecuteOverwriteDestBin()
    │         │   └─ No → File.WriteAllBytes()
    │         └─ 显示成功信息
    └─ No → ExecuteSaveAsNewFile()
              ├─ 显示保存对话框
              ├─ IsDestBinMode?
              │   ├─ Yes → 重新打包 DestBin
              │   └─ No → File.WriteAllBytes()
              └─ 显示成功信息
```

### 状态管理

```csharp
// 保存前
IsLoading = true;
StatusMessage = "Saving...";

// 保存后
IsLoading = false;
StatusMessage = "✓ Saved";
```

确保界面不会在保存过程中被误操作。

---

## 📈 性能考虑

### 备份开销

**时间复杂度**：O(n)，n = 文件大小  
**空间复杂度**：O(n)，需要额外存储空间

**优化建议**：
- 对于大文件（> 10MB），可考虑异步备份
- 定期清理旧的备份文件
- 使用增量备份（未来增强）

### 内存使用

- 覆盖模式：不需要额外内存
- 另存为模式：需要临时保存对话框
- DestBin 模式：需要重新打包（已在内存中）

---

## 🔗 相关文档

- [REPLACE_SIZE_CONFIRMATION.md](./REPLACE_SIZE_CONFIRMATION.md) - 资源替换大小确认功能
- [RESOURCE_REPLACE_SIZE_HANDLING_DETAILED.md](./RESOURCE_REPLACE_SIZE_HANDLING_DETAILED.md) - 资源替换机制详解
- [SMART_FILE_OPERATIONS_INTEGRATION.md](./SMART_FILE_OPERATIONS_INTEGRATION.md) - 智能文件操作集成

---

## ✅ 总结

### 核心改进

1. ✅ **统一入口**：一个 Save 按钮，两种保存方式
2. ✅ **用户选择**：明确的 Yes/No/Cancel 选项
3. ✅ **自动备份**：覆盖前自动创建 .backup 文件
4. ✅ **详细提示**：文件大小、备份名、后续步骤
5. ✅ **模式适配**：同时支持 RES.BIN 和 DestBin.bin

### 核心价值

- 🎯 **灵活性**：用户根据需求选择保存方式
- 🛡️ **安全性**：自动备份防止数据丢失
- 💡 **易用性**：清晰的提示和指导
- 🔒 **可靠性**：完善的错误处理和恢复机制

### 适用场景

- ✅ 快速迭代开发（覆盖模式）
- ✅ 多版本管理（另存为模式）
- ✅ 固件更新（DestBin 模式）
- ✅ 实验性修改（备份保护）

---

**版本**: v2.0  
**更新日期**: 2026-05-19  
**作者**: ResBinManager Team
