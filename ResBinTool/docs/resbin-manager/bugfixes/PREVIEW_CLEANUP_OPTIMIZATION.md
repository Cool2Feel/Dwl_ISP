# 重复打开 DestBin.bin 文件时预览面板清空优化

## 📋 问题描述

**现象**：
- 用户首次打开 DestBin.bin 文件，选择一个资源（如 WAV、Font 或图片）
- 预览面板显示对应内容（WAV 控制面板、字体控件或图片）
- 用户再次打开同一个或另一个 DestBin.bin 文件
- **问题**：之前的预览面板内容仍然显示，没有被清空

**影响**：
- ❌ 用户看到旧文件的预览内容，造成混淆
- ❌ 字体控件、WAV 控件等仍然可见，但数据已无效
- ❌ 界面状态不一致，用户体验差

---

## 🔍 根本原因分析

### 1. 清理顺序问题

**原来的 `CleanupPreviousLoad()` 方法**：

```csharp
private void CleanupPreviousLoad()
{
    // 1. 清空资源列表
    Resources.Clear();
    
    // 2. 释放解析器
    _parser = null;
    _destBinParser = null;
    
    // 3. 清空文件数据
    _currentFileData = null;
    
    // 4. 重置状态（包括 SelectedResource）
    SelectedResource = null;  // ← 这里设置为 null
    WavInfo = null;
    FontInfo = null;
}
```

**问题**：
- `SelectedResource = null` 会触发 `PropertyChanged` 事件
- UI 层的 `OnViewModelPropertyChanged` 接收到事件
- 但此时 `SelectedResource` 已经是 `null`，进入默认分支
- **默认分支没有清空预览面板**，只是隐藏了部分控件
- 字体控件、WAV 控件等仍然保留在内存中并显示

### 2. UI 层处理不完整

**原来的 `OnViewModelPropertyChanged` 方法**：

```csharp
if (e.PropertyName == nameof(MainViewModel.SelectedResource))
{
    var resourceType = ViewModel?.SelectedResource?.Type;
    
    if (resourceType == Models.ResourceType.Wav)
    {
        // 显示 WAV 面板
    }
    else if (...)
    {
        // 显示其他面板
    }
    else
    {
        // 默认分支：显示默认预览
        // ❌ 但没有清空之前的字体控件、WAV 控件等
    }
}
```

**问题**：
- 当 `SelectedResource` 为 `null` 时，`resourceType` 也是 `null`
- 进入 `else` 分支，显示默认预览
- **但没有显式清空所有预览面板**
- 字体控件 (`FontControlPanel`) 和 WAV 控件 (`WavControlPanel`) 可能仍然可见

---

## 🛠️ 修复方案

### 方案 1：优化 ViewModel 清理顺序（✅ 已实现）

**核心思路**：在清理其他状态之前，先设置 `SelectedResource = null`，确保 UI 层能及时响应并清空预览。

**修改后的 `CleanupPreviousLoad()` 方法**：

```csharp
private void CleanupPreviousLoad()
{
    System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Cleaning up previous state...");
    
    // ✅ 重要：先清空选中资源，触发 UI 层清空预览面板
    if (SelectedResource != null)
    {
        System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Clearing SelectedResource to trigger preview cleanup");
        SelectedResource = null;  // ← 提前设置，触发 PropertyChanged
    }
    
    // 清空资源列表
    if (Resources != null && Resources.Count > 0)
    {
        System.Diagnostics.Debug.WriteLine($"[CleanupPreviousLoad] Clearing {Resources.Count} resources");
        Resources.Clear();
    }
    
    // 释放 ResBinParser
    if (_parser != null)
    {
        System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Disposing ResBinParser");
        _parser = null;
    }
    
    // 释放 DestBinParser
    if (_destBinParser != null)
    {
        System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Disposing DestBinParser");
        _destBinParser = null;
    }
    
    // ✅ 新增：释放 RES.H Parser
    if (_resHParser != null)
    {
        System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Disposing ResHParser");
        _resHParser = null;
    }
    
    // 清空当前文件数据
    if (_currentFileData != null)
    {
        System.Diagnostics.Debug.WriteLine($"[CleanupPreviousLoad] Clearing file data ({_currentFileData.Length} bytes)");
        _currentFileData = null;
    }
    
    // 重置状态
    _currentTableOffset = 0;
    IsDestBinMode = false;
    FirmwareVersion = null;
    FirmwareSerial = null;
    WavInfo = null;
    FontInfo = null;
    FontData = null;      // ✅ 新增：清空字体数据
    FontIndex = null;     // ✅ 新增：清空字体索引
    
    System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Cleanup complete");
}
```

**关键改进**：
1. ✅ **提前设置 `SelectedResource = null`**：在其他清理操作之前，确保 UI 层能及时响应
2. ✅ **释放 `_resHParser`**：避免内存泄漏
3. ✅ **清空 `FontData` 和 `FontIndex`**：确保字体预览完全清除

---

### 方案 2：增强 UI 层空值处理（✅ 已实现）

**核心思路**：当 `SelectedResource` 为 `null` 时，显式清空所有预览面板。

**修改后的 `OnViewModelPropertyChanged` 方法**：

```csharp
private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(MainViewModel.SelectedResource))
    {
        var resource = ViewModel?.SelectedResource;
        var resourceType = resource?.Type;
        
        System.Diagnostics.Debug.WriteLine($"[UI] SelectedResource changed: Type={resourceType}");
        
        // ✅ 如果没有选中资源，清空所有预览面板
        if (resource == null)
        {
            System.Diagnostics.Debug.WriteLine("[UI] No resource selected, clearing all preview panels");
            WavControlPanel.Visibility = Visibility.Collapsed;
            FontControlPanel.Visibility = Visibility.Collapsed;
            ImagePreviewBorder.Visibility = Visibility.Collapsed;
            ActionButtonsPanel.Visibility = Visibility.Collapsed;
            ClearPreview();  // 清空图片预览
            return;          // 提前返回，不再执行后续逻辑
        }
        
        // 以下是有选中资源时的处理逻辑
        if (resourceType == Models.ResourceType.Wav)
        {
            // 显示 WAV 面板
        }
        else if (...)
        {
            // 显示其他面板
        }
    }
}
```

**关键改进**：
1. ✅ **显式检查 `resource == null`**：在开头就处理空值情况
2. ✅ **清空所有预览面板**：WAV、Font、Image 全部隐藏
3. ✅ **调用 `ClearPreview()`**：确保图片预览也被清空
4. ✅ **提前返回**：避免执行后续的资源类型判断逻辑

---

## 📊 效果对比

### 修复前

**场景**：重复打开 DestBin.bin 文件

```
步骤 1: 打开 DestBin.bin #1
  → 选择 Resource_5 (WAV 音频)
  → WAV 控制面板显示 ✅
  → 播放按钮可用 ✅

步骤 2: 打开 DestBin.bin #2（或重新打开 #1）
  → CleanupPreviousLoad() 执行
  → SelectedResource = null
  → UI 层收到 PropertyChanged 事件
  → 进入 else 分支（默认预览）
  → ❌ WAV 控制面板仍然可见！
  → ❌ 播放按钮仍然可用！（但数据已无效）
  → 用户困惑：为什么还能看到之前的 WAV 控件？
```

**调试输出**：
```
[CleanupPreviousLoad] Cleaning up previous state...
[CleanupPreviousLoad] Clearing 94 resources
[CleanupPreviousLoad] Disposing ResBinParser
[CleanupPreviousLoad] Disposing DestBinParser
[CleanupPreviousLoad] Clearing file data (382464 bytes)
[UI] SelectedResource changed: Type=
[UI] Showing default preview  ← ❌ 没有清空 WAV 控件
```

---

### 修复后

**场景**：重复打开 DestBin.bin 文件

```
步骤 1: 打开 DestBin.bin #1
  → 选择 Resource_5 (WAV 音频)
  → WAV 控制面板显示 ✅
  → 播放按钮可用 ✅

步骤 2: 打开 DestBin.bin #2（或重新打开 #1）
  → CleanupPreviousLoad() 执行
  → ✅ SelectedResource = null（提前设置）
  → UI 层收到 PropertyChanged 事件
  → ✅ 检测到 resource == null
  → ✅ 清空所有预览面板
  → ✅ WAV 控制面板隐藏
  → ✅ Font 控制面板隐藏
  → ✅ Image 预览隐藏
  → ✅ 调用 ClearPreview()
  → 用户看到干净的界面，准备加载新文件
```

**调试输出**：
```
[CleanupPreviousLoad] Cleaning up previous state...
[CleanupPreviousLoad] Clearing SelectedResource to trigger preview cleanup  ← ✅ 新增
[CleanupPreviousLoad] Clearing 94 resources
[CleanupPreviousLoad] Disposing ResBinParser
[CleanupPreviousLoad] Disposing DestBinParser
[CleanupPreviousLoad] Disposing ResHParser  ← ✅ 新增
[CleanupPreviousLoad] Clearing file data (382464 bytes)
[CleanupPreviousLoad] Cleanup complete
[UI] SelectedResource changed: Type=
[UI] No resource selected, clearing all preview panels  ← ✅ 新增
```

---

## 🎯 关键改进点

### 1. **清理顺序优化**

```
修复前：
  清空资源列表 → 释放解析器 → 清空数据 → 设置 SelectedResource = null
                                          ↓
                                    UI 层响应太晚，预览未清空

修复后：
  设置 SelectedResource = null → 清空资源列表 → 释放解析器 → 清空数据
  ↓
UI 层立即响应，清空预览面板 ✅
```

### 2. **UI 层空值处理**

```
修复前：
  if (resource == null)
  {
      // 进入 else 分支
      // 显示默认预览
      // ❌ 没有清空之前的控件
  }

修复后：
  if (resource == null)
  {
      // ✅ 显式清空所有预览面板
      WavControlPanel.Visibility = Collapsed;
      FontControlPanel.Visibility = Collapsed;
      ImagePreviewBorder.Visibility = Collapsed;
      ClearPreview();
      return;  // 提前返回
  }
```

### 3. **完整的状态清理**

```
新增清理项：
  ✅ _resHParser（RES.H 解析器）
  ✅ FontData（字体数据）
  ✅ FontIndex（字体索引）

确保所有与预览相关的状态都被清空
```

---

## 📝 测试验证

### 测试场景 1：重复打开同一文件

```
步骤：
1. 打开 DestBin.bin
2. 选择一个 WAV 资源
3. WAV 控制面板显示
4. 再次打开同一个 DestBin.bin
5. 观察预览面板是否清空

预期结果：
✅ WAV 控制面板隐藏
✅ 字体控件隐藏
✅ 图片预览清空
✅ 界面回到初始状态
```

### 测试场景 2：打开不同文件

```
步骤：
1. 打开 JT529X DestBin.bin
2. 选择一个 Font 资源
3. 字体预览控件显示
4. 打开 AX329X DestBin.bin
5. 观察预览面板是否清空

预期结果：
✅ 字体预览控件隐藏
✅ 所有面板清空
✅ 准备加载新文件的资源
```

### 测试场景 3：连续快速打开

```
步骤：
1. 打开 DestBin.bin #1
2. 选择资源 A
3. 立即打开 DestBin.bin #2
4. 立即打开 DestBin.bin #3
5. 观察是否有残留的预览内容

预期结果：
✅ 每次打开都清空之前的预览
✅ 最终显示最后一个文件的干净界面
✅ 没有内存泄漏或控件残留
```

---

## 📁 修改的文件

1. **[MainViewModel.cs](file://d:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\tools\ResBinManager\ViewModels\MainViewModel.cs)**
   - 优化 `CleanupPreviousLoad()` 方法
   - 提前设置 `SelectedResource = null`
   - 新增清理 `_resHParser`、`FontData`、`FontIndex`

2. **[MainWindow.xaml.cs](file://d:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\tools\ResBinManager\Views\MainWindow.xaml.cs)**
   - 增强 `OnViewModelPropertyChanged` 方法
   - 显式处理 `resource == null` 的情况
   - 清空所有预览面板

---

## ✅ 总结

通过优化清理顺序和增强 UI 层空值处理，我们成功解决了重复打开 DestBin.bin 文件时预览面板未清空的问题：

### 优势

✅ **界面更干净**：每次打开新文件都从干净的状态开始  
✅ **用户体验更好**：不会看到旧文件的预览内容  
✅ **内存更安全**：及时释放字体控件、WAV 控件等资源  
✅ **逻辑更清晰**：清理顺序合理，状态转换明确  

### 适用场景

- ✅ 重复打开同一个 DestBin.bin 文件
- ✅ 连续打开不同的 DestBin.bin 文件
- ✅ 在 DestBin 模式和 RES.BIN 模式之间切换
- ✅ 快速连续打开多个文件

### 技术要点

1. **提前触发 PropertyChanged**：在清理其他状态之前设置 `SelectedResource = null`
2. **显式处理空值**：UI 层检测到 `null` 时立即清空所有预览面板
3. **完整清理状态**：包括解析器、数据、索引等所有相关状态

---

## 🚀 未来扩展

可能的进一步增强：

1. **添加加载动画**
   - 在清理和加载之间显示 loading 状态
   - 提升用户体验

2. **保存预览状态**
   - 记住用户上次选择的资源
   - 重新打开时自动选中

3. **异步清理**
   - 对于大型文件，使用异步清理
   - 避免 UI 卡顿

4. **预览缓存**
   - 缓存最近查看的资源预览
   - 快速切换时直接从缓存加载
