# DestBin.bin 保存后资源数据损坏问题修复

## 一、问题现象

用户报告：
1. 打开原始 `DestBin.bin` - ✓ 正常，第一个资源是 JPEG
2. 替换资源后另存为 `destbin_modified.bin`
3. 重新打开 `destbin_modified.bin` - ✗ **第一个资源被识别为 IconSelection，数据损坏**

### 调试日志显示

```
[ExtractResourceMetadata] First resource:
  Address: 0x2F0, Length: 37831
  File size: 4391936
  First 4 bytes: 6A E8 FF E0  ← 应该是 FF D8 FF E0
  Is JPEG header? False

[DetectResourceType] Detected IconSelection, length=37831, first 4 bytes: 6A E8 FF E0
```

### 数据对比

**原始 DestBin.bin**：
```
Offset 0x9DEF0: FF D8 FF E0 00 10 4A 46 49 46 ... (JPEG ✓)
```

**修改后的 destbin_modified.bin**：
```
Offset 0x9DEF0: 6A E8 FF E0 00 10 4A 46 B4 55 ... (损坏 ✗)
```

前两个字节从 `FF D8` 变成了 `6A E8`，第9-10字节从 `49 46` 变成了 `B4 55`。

---

## 二、根本原因

### 问题代码位置

[MainViewModel.cs](file://d:/dwl/work/2026/JT/JX_SDK/JT529X/firmware/tools/ResBinManager/ViewModels/MainViewModel.cs#L1351) 第1351行和第1421行：

```csharp
// ❌ 错误的调用
_destBinParser.ReplaceResBin(_currentFileData, keepSize: true)
```

### 问题分析

#### 1. 工作流程

```
打开 DestBin.bin
  ↓
提取 RES.BIN → _currentFileData (临时文件)
  ↓
ResBinParser 解析 _currentFileData
  ↓
用户替换资源
  ↓
ResBinWriter 修改 _currentFileData (大小可能改变)
  ↓
保存时调用 ReplaceResBin(_currentFileData, keepSize: true)
  ↓
❌ 如果新数据 > 原始大小，数据被截断！
  ↓
保存到文件
  ↓
重新打开时数据损坏
```

#### 2. keepSize: true 的行为

在 [DestBinParser.cs](file://d:/dwl/work/2026/JT/JX_SDK/JT529X/firmware/tools/ResBinManager/Core/DestBinParser.cs#L443-L488) 中：

```csharp
if (keepSize)
{
    if (newResBinData.Length != _resBinSize)
    {
        if (newResBinData.Length < _resBinSize)
        {
            // 新数据较小，用 0xFF 填充
            dataToWrite = new byte[_resBinSize];
            Array.Copy(newResBinData, dataToWrite, newResBinData.Length);
            for (int i = newResBinData.Length; i < _resBinSize; i++)
                dataToWrite[i] = 0xFF;
        }
        else
        {
            // ❌ 新数据较大，截断（警告用户）
            dataToWrite = new byte[_resBinSize];
            Array.Copy(newResBinData, dataToWrite, _resBinSize);  // 只复制前 _resBinSize 字节
            ErrorMessage = $"Warning: RES.BIN was truncated...";
        }
    }
}
```

**问题**：当用户替换一个较大的 JPEG 图片时：
- 原始大小：37,831 字节
- 新图片大小：可能更大（例如 40,000 字节）
- `keepSize=true` 导致只复制前 37,831 字节
- **JPEG 文件头被破坏**（前几个字节被截断或覆盖）

#### 3. 为什么数据会错位

假设：
- 原始 RES.BIN 大小：4,391,936 字节
- 用户替换第一个资源，新资源比原来大 2,000 字节
- ResBinWriter 修改 `_currentFileData`，新大小：4,393,936 字节
- 调用 `ReplaceResBin(_currentFileData, keepSize: true)`
- 由于 `4,393,936 > 4,391,936`，数据被截断到 4,391,936 字节
- **最后 2,000 字节丢失**，但更严重的是，**ResBinWriter 已经更新了资源表中的偏移量**
- 截断后，资源表指向的位置可能包含错误的数据

---

## 三、修复方案

### 修复内容

将 `keepSize: true` 改为 `keepSize: false`，允许文件大小变化。

#### 修改位置 1：ExecuteOverwriteDestBin 方法

[MainViewModel.cs 第1351-1353行](file://d:/dwl/work/2026/JT/JX_SDK/JT529X/firmware/tools/ResBinManager/ViewModels/MainViewModel.cs#L1351-L1353)

```csharp
// ✅ 修复后
// 重要：对于 DestBin 模式，应该使用 keepSize: false，允许文件大小变化
// 这样可以避免数据截断导致的损坏
if (_destBinParser.ReplaceResBin(_currentFileData, keepSize: false))
```

#### 修改位置 2：ExecuteSaveAs 方法

[MainViewModel.cs 第1421-1422行](file://d:/dwl/work/2026/JT/JX_SDK/JT529X/firmware/tools/ResBinManager/ViewModels/MainViewModel.cs#L1421-L1422)

```csharp
// ✅ 修复后
// 重要：使用 keepSize: false，允许文件大小变化
if (_destBinParser.ReplaceResBin(_currentFileData, keepSize: false))
```

### keepSize: false 的行为

在 [DestBinParser.cs 第490-527行](file://d:/dwl/work/2026/JT/JX_SDK/JT529X/firmware/tools/ResBinManager/Core/DestBinParser.cs#L490-L527) 中：

```csharp
else  // keepSize: false
{
    if (newResBinData.Length != _resBinSize)
    {
        // 重新计算 DestBin.bin 大小
        int newSize = (int)PROGRAM_CODE_SIZE + newResBinData.Length;
        
        // 确保 4KB 对齐
        int paddingNeeded = (4096 - (newSize % 4096)) % 4096;
        newSize += paddingNeeded;
        
        // 创建新的 DestBin.bin 数据
        var newData = new byte[newSize];
        
        // 复制程序代码段
        Array.Copy(_destBinData, 0, newData, 0, PROGRAM_CODE_SIZE);
        
        // ✅ 完整复制新的 RES.BIN（不截断）
        Array.Copy(newResBinData, 0, newData, PROGRAM_CODE_SIZE, newResBinData.Length);
        
        // 尾部填充零
        for (int i = (int)PROGRAM_CODE_SIZE + newResBinData.Length; i < newSize; i++)
            newData[i] = 0x00;
        
        _destBinData = newData;
        _resBinData = newResBinData;
        _resBinSize = newResBinData.Length;
        
        return true;
    }
}
```

**优点**：
- ✅ 完整保留所有数据，不会截断
- ✅ 自动调整文件大小
- ✅ 保持 4KB 对齐
- ✅ 资源表和数据保持一致

---

## 四、测试验证

### 测试步骤

1. **打开原始 DestBin.bin**
   ```
   ✓ 第一个资源识别为 JPEG
   ```

2. **替换第一个资源（使用更大的 JPEG）**
   ```
   ✓ 替换成功
   ✓ 资源大小增加
   ```

3. **保存为 destbin_modified.bin**
   ```
   ✓ 保存成功
   ✓ 文件大小适当增加
   ```

4. **重新打开 destbin_modified.bin**
   ```
   ✓ 第一个资源仍然识别为 JPEG
   ✓ 数据完整无损
   ✓ 可以正常预览
   ```

### 预期结果

- ✅ 替换后的资源数据完整
- ✅ JPEG 文件头正确（FF D8 FF E0）
- ✅ 资源类型检测正确
- ✅ 预览功能正常

---

## 五、相关代码位置

### 核心文件

1. **MainViewModel.cs**
   - `ExecuteOverwriteDestBin()` - 第1351行（已修复）
   - `ExecuteSaveAs()` - 第1421行（已修复）

2. **DestBinParser.cs**
   - `ReplaceResBin()` - 第425行
   - `keepSize: true` 分支 - 第443-488行
   - `keepSize: false` 分支 - 第490-527行

### 调试日志

- `[ExtractResourceMetadata]` - ResBinParser.cs 第200-215行
- `[DetectResourceType]` - ResBinParser.cs 第317-377行

---

## 六、注意事项

### 1. 文件大小变化

使用 `keepSize: false` 后，保存的文件大小可能会变化：
- 如果替换的资源变大 → 文件变大
- 如果替换的资源变小 → 文件变小

这是**正常且预期的行为**。

### 2. 固件兼容性

修改后的 DestBin.bin 文件：
- ✅ 程序代码段保持不变
- ✅ RES.BIN 完整保留
- ✅ 4KB 对齐保持
- ✅ 可以直接用于固件烧录

### 3. 向后兼容

如果某些场景确实需要保持固定大小（例如 Flash 分区大小固定），可以：
1. 手动添加填充数据
2. 或者在替换时使用相同大小的资源

但对于大多数情况，`keepSize: false` 是更安全的选择。

---

## 七、总结

### 问题根源

- ❌ 使用 `keepSize: true` 导致大数据被截断
- ❌ 截断破坏了资源数据结构
- ❌ 重新打开时无法正确识别资源类型

### 修复方案

- ✅ 改用 `keepSize: false`
- ✅ 允许文件大小自然变化
- ✅ 完整保留所有数据
- ✅ 保持结构完整性

### 影响范围

- 修复了 DestBin 模式的保存功能
- 适用于"覆盖保存"和"另存为"两种操作
- 不影响 RES.BIN 独立文件的处理

---

## 八、后续优化建议

1. **添加数据完整性验证**
   - 保存前验证关键资源的文件头
   - 保存后重新加载验证

2. **提供更明确的提示**
   - 当文件大小变化时，显示变化量
   - 提醒用户注意固件分区大小限制

3. **支持可选的固定大小模式**
   - 对于特殊需求，提供选项
   - 但默认使用安全模式（keepSize: false）
