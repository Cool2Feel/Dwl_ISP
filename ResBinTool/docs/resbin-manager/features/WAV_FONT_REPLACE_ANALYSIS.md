# WAV 和 Font 资源替换功能分析与实现方案

## 📋 目录
- [1. 现有替换功能分析](#1-现有替换功能分析)
- [2. WAV 资源替换方案](#2-wav-资源替换方案)
- [3. Font 资源替换方案](#3-font-资源替换方案)
- [4. 实现计划](#4-实现计划)
- [5. 技术挑战与解决方案](#5-技术挑战与解决方案)

---

## 1. 现有替换功能分析

### 1.1 当前实现状态

**已实现的功能**：
- ✅ JPEG/BMP 图片资源替换
- ✅ WAV 音频资源替换（基础支持）
- ❌ Font 字体资源替换（未实现）

**核心代码位置**：
- `ViewModels/MainViewModel.cs` - ExecuteReplace() 方法
- `Core/ResBinWriter.cs` - ReplaceResource() 方法
- `Models/ResourceItem.cs` - 资源数据模型

### 1.2 替换流程

```
用户操作                    系统处理
─────────                  ────────
1. 选中资源              →  SelectedResource 设置
2. 点击 Replace          →  ExecuteReplace() 调用
3. 选择新文件            →  OpenFileDialog 打开
4. 读取新文件数据        →  File.ReadAllBytes()
5. 验证文件大小          →  大小比较 + 警告提示
6. 执行替换              →  ResBinWriter.ReplaceResource()
   ├─ 小文件: 直接覆盖    →  填充 0xFF
   └─ 大文件: 移动数据    →  更新后续资源偏移
7. 更新 UI               →  标记 IsModified = true
8. 保存文件              →  Save 按钮触发
```

### 1.3 关键代码分析

**MainViewModel.ExecuteReplace()**:
```csharp
private void ExecuteReplace(object? parameter)
{
    // 1. 打开文件对话框
    var dialog = new OpenFileDialog
    {
        Filter = GetFilterByType(SelectedResource.Type)
    };
    
    // 2. 读取新文件
    var newData = File.ReadAllBytes(dialog.FileName);
    
    // 3. 验证大小
    if (newData.Length > SelectedResource.Size * 2)
    {
        // 显示警告
    }
    
    // 4. 执行替换
    var writer = new ResBinWriter(_currentFileData!, ...);
    writer.ReplaceResource(SelectedResource.Id, newData);
    
    // 5. 更新状态
    SelectedResource.IsModified = true;
    SelectedResource.Size = (uint)newData.Length;
}
```

**ResBinWriter.ReplaceResource()**:
```csharp
public bool ReplaceResource(uint resourceId, byte[] newData)
{
    // 1. 查找资源在索引表中的位置
    int entryIndex = FindEntryIndex(resourceId);
    
    // 2. 获取原资源信息
    uint oldOffset = entries[entryIndex].Address;
    uint oldSize = entries[entryIndex].Length;
    
    // 3. 根据大小决定策略
    if (newData.Length <= oldSize)
    {
        // 策略 A: 直接覆盖
        Array.Copy(newData, 0, _fileData, oldOffset, newData.Length);
        // 填充剩余空间
        for (int i = newData.Length; i < oldSize; i++)
            _fileData[oldOffset + i] = 0xFF;
    }
    else
    {
        // 策略 B: 扩展并移动
        uint delta = (uint)(newData.Length - oldSize);
        ExpandAndShift(oldOffset + oldSize, delta);
        Array.Copy(newData, 0, _fileData, oldOffset, newData.Length);
    }
    
    // 4. 更新索引表
    entries[entryIndex].Length = (uint)newData.Length;
    
    return true;
}
```

---

## 2. WAV 资源替换方案

### 2.1 WAV 资源特点

**文件格式**：
- 标准 WAV 格式（RIFF/WAVE）
- 包含文件头（44 字节）+ PCM 数据
- 采样率、位深度、声道数等元数据

**在 RES.BIN 中的存储**：
- 作为 Binary 类型资源存储
- 完整的 WAV 文件内容（包括头部）
- 通过 ID 或名称识别（如 RES_BEEP, RES_ALARM 等）

### 2.2 替换流程设计

```
┌─────────────────────────────────────────┐
│  1. 用户选中 WAV 资源                     │
│     (例如: ID=5, Name=RES_BEEP)          │
└──────────────┬──────────────────────────┘
               ↓
┌─────────────────────────────────────────┐
│  2. 点击 Replace 按钮                     │
│     打开文件过滤器: *.wav                │
└──────────────┬──────────────────────────┘
               ↓
┌─────────────────────────────────────────┐
│  3. 用户选择新的 WAV 文件                 │
│     例如: new_beep.wav (5KB)             │
└──────────────┬──────────────────────────┘
               ↓
┌─────────────────────────────────────────┐
│  4. 验证 WAV 文件格式                     │
│     ✓ 检查 RIFF 魔数                      │
│     ✓ 检查 WAVE 标识                      │
│     ✗ 如果无效，拒绝替换                   │
└──────────────┬──────────────────────────┘
               ↓
┌─────────────────────────────────────────┐
│  5. 显示 WAV 信息对比                     │
│     原始: 44.1kHz, 16bit, Mono, 3KB      │
│     新文件: 22.05kHz, 8bit, Mono, 5KB    │
│     ⚠ 采样率变化可能导致音质差异           │
└──────────────┬──────────────────────────┘
               ↓
┌─────────────────────────────────────────┐
│  6. 执行替换（复用现有逻辑）               │
│     ResBinWriter.ReplaceResource()       │
└──────────────┬──────────────────────────┘
               ↓
┌─────────────────────────────────────────┐
│  7. 更新预览                              │
│     LoadWavForPreview()                  │
│     显示新的波形和时长                     │
└─────────────────────────────────────────┘
```

### 2.3 需要增强的功能

#### 2.3.1 WAV 格式验证

```csharp
/// <summary>
/// 验证 WAV 文件格式
/// </summary>
private bool ValidateWavFormat(byte[] wavData, out string errorMessage)
{
    errorMessage = null;
    
    if (wavData.Length < 44)
    {
        errorMessage = "File too small to be a valid WAV";
        return false;
    }
    
    // 检查 RIFF 魔数
    string riff = System.Text.Encoding.ASCII.GetString(wavData, 0, 4);
    if (riff != "RIFF")
    {
        errorMessage = "Invalid WAV file: missing RIFF header";
        return false;
    }
    
    // 检查 WAVE 标识
    string wave = System.Text.Encoding.ASCII.GetString(wavData, 8, 4);
    if (wave != "WAVE")
    {
        errorMessage = "Invalid WAV file: missing WAVE identifier";
        return false;
    }
    
    return true;
}
```

#### 2.3.2 WAV 信息显示

在替换前显示详细对比：

```csharp
// 解析原始 WAV 信息
var oldInfo = WavInfoParser.Parse(ExtractData(SelectedResource));

// 解析新 WAV 信息
var newInfo = WavInfoParser.Parse(newData);

// 显示对比对话框
var message = $"WAV Resource Replacement\n\n" +
              $"Original:\n" +
              $"  Duration: {oldInfo.Duration:F2}s\n" +
              $"  Sample Rate: {oldInfo.SampleRate}Hz\n" +
              $"  Bits: {oldInfo.BitsPerSample}\n" +
              $"  Channels: {oldInfo.Channels}\n" +
              $"  Size: {oldInfo.DataSize:N0} bytes\n\n" +
              $"New:\n" +
              $"  Duration: {newInfo.Duration:F2}s\n" +
              $"  Sample Rate: {newInfo.SampleRate}Hz\n" +
              $"  Bits: {newInfo.BitsPerSample}\n" +
              $"  Channels: {newInfo.Channels}\n" +
              $"  Size: {newInfo.DataSize:N0} bytes\n\n" +
              $"Continue with replacement?";

var result = MessageBox.Show(message, "Confirm Replacement", 
                            MessageBoxButton.YesNo, MessageBoxImage.Question);
```

### 2.4 实现步骤

**Phase 1: 基础验证** (优先级: 高)
1. ✅ 添加 WAV 格式验证函数
2. ✅ 在 ExecuteReplace() 中调用验证
3. ✅ 显示简单的确认对话框

**Phase 2: 详细信息** (优先级: 中)
1. 解析并显示 WAV 参数对比
2. 添加采样率/位深度警告
3. 提供播放预览确认

**Phase 3: 高级功能** (优先级: 低)
1. 自动转换格式（重采样）
2. 批量替换多个 WAV
3. WAV 库管理

---

## 3. Font 资源替换方案

### 3.1 Font 资源特殊性

**双文件结构**：
```
Font 资源 = resfont.bin (字体数据) + resfontidx.bin (索引数据)

resfont.bin:
├─ CharCount (4 bytes) - 字符总数
├─ Char[0]: Offset(4) + Width(2) + Height(2)
├─ Char[1]: Offset(4) + Width(2) + Height(2)
├─ ...
└─ Bitmap Data - 点阵位图数据

resfontidx.bin:
├─ Magic (2 bytes) - 0x584D
├─ InvalidCharWidth (1 byte)
├─ LanguageCount (1 byte)
├─ Lang[0]: Index(4) + Offset(4)
├─ Lang[1]: Index(4) + Offset(4)
├─ ...
└─ String Info - 字符串元数据
```

**在 RES.BIN 中的存储**：
- ID 79: RES_RESFONT (字体数据)
- ID 80: RES_RESFONTIDX (字体索引)
- 两个资源必须**同时替换**，保持一致性

### 3.2 替换挑战

| 挑战 | 说明 | 解决方案 |
|------|------|----------|
| **双文件同步** | 必须同时替换两个资源 | 提供联合替换界面 |
| **格式复杂性** | 自定义二进制格式 | 使用 FontInfoParser 验证 |
| **字符数量变化** | 新字体可能有不同字符数 | 重新生成索引表 |
| **语言支持** | 多语言索引结构复杂 | 保持原有语言数量 |
| **向后兼容** | 确保固件能正确加载 | 严格遵循 AX329x 格式 |

### 3.3 替换方案设计

#### 方案 A: 联合替换（推荐）⭐

**用户界面**：
```
┌──────────────────────────────────────────────┐
│  Font Resource Replacement                    │
├──────────────────────────────────────────────┤
│                                               │
│  Current Font:                                │
│  ├── resfont.bin:     84,528 bytes (899 chars)│
│  └── resfontidx.bin:  76,766 bytes (15 langs) │
│                                               │
│  New Font Files:                              │
│  ├── resfont.bin:     [Browse...]  ✓ Loaded   │
│  └── resfontidx.bin:  [Browse...]  ✓ Loaded   │
│                                               │
│  Validation Results:                          │
│  ✓ Format valid                               │
│  ✓ Character count: 1024                      │
│  ✓ Language count: 15                         │
│  ⚠ Character count changed (899→1024)         │
│                                               │
│  [ Preview Font ]  [ Replace Both ]  [Cancel] │
└──────────────────────────────────────────────┘
```

**实现流程**：
```
1. 用户选中任一 Font 资源 (ID 79 或 80)
   ↓
2. 点击 "Replace Font" 按钮（新增）
   ↓
3. 打开自定义对话框 FontReplaceDialog
   ├─ 显示当前字体信息
   ├─ 允许选择新的 resfont.bin
   ├─ 允许选择新的 resfontidx.bin
   └─ 实时验证格式
   ↓
4. 用户选择两个文件
   ↓
5. 验证两个文件
   ├─ 解析 resfont.bin
   ├─ 解析 resfontidx.bin
   ├─ 检查一致性
   └─ 显示预览
   ↓
6. 用户点击 "Replace Both"
   ↓
7. 执行双重替换
   ├─ ReplaceResource(79, fontData)
   └─ ReplaceResource(80, fontIndex)
   ↓
8. 更新 UI 和预览
```

#### 方案 B: 单独替换（备选）

允许分别替换两个文件，但增加警告：

```csharp
if (resourceId == 79 || resourceId == 80)
{
    var result = MessageBox.Show(
        "Warning: Font resources consist of two files.\n" +
        "You should replace both resfont.bin and resfontidx.bin together.\n\n" +
        "Continue with single file replacement?",
        "Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
    
    if (result != MessageBoxResult.Yes)
        return;
}
```

### 3.4 实现细节

#### 3.4.1 Font 格式验证

```csharp
/// <summary>
/// 验证字体文件格式
/// </summary>
public class FontValidator
{
    public static ValidationResult Validate(byte[] fontData, byte[] fontIndex)
    {
        var result = new ValidationResult();
        
        // 1. 验证 resfont.bin
        try
        {
            var info = FontInfoParser.Parse(fontData, fontIndex);
            result.FontDataValid = true;
            result.CharCount = info.CharCount;
            result.LanguageCount = info.LanguageCount;
        }
        catch (Exception ex)
        {
            result.FontDataValid = false;
            result.ErrorMessage = $"Font data error: {ex.Message}";
            return result;
        }
        
        // 2. 验证 resfontidx.bin
        if (fontIndex.Length < 4)
        {
            result.FontIndexValid = false;
            result.ErrorMessage = "Font index file too small";
            return result;
        }
        
        uint header = BitConverter.ToUInt32(fontIndex, 0);
        ushort magic = (ushort)(header & 0x0000FFFF);
        
        if (magic != 0x584D)
        {
            result.FontIndexValid = false;
            result.ErrorMessage = $"Invalid font index magic: 0x{magic:X4}";
            return result;
        }
        
        result.FontIndexValid = true;
        
        // 3. 检查一致性
        byte langCount = (byte)((header >> 24) & 0xFF);
        if (langCount != result.LanguageCount)
        {
            result.ConsistencyWarning = 
                $"Language count mismatch: data={result.LanguageCount}, index={langCount}";
        }
        
        result.IsValid = result.FontDataValid && result.FontIndexValid;
        return result;
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public bool FontDataValid { get; set; }
    public bool FontIndexValid { get; set; }
    public uint CharCount { get; set; }
    public byte LanguageCount { get; set; }
    public string ErrorMessage { get; set; }
    public string ConsistencyWarning { get; set; }
}
```

#### 3.4.2 Font 替换对话框

创建新的 WPF 窗口 `FontReplaceDialog.xaml`:

```xml
<Window x:Class="ResBinManager.Views.FontReplaceDialog"
        Title="Replace Font Resources"
        Width="600" Height="500">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        
        <!-- 当前字体信息 -->
        <GroupBox Header="Current Font" Grid.Row="0" Margin="0,0,0,10">
            <StackPanel Margin="10">
                <TextBlock x:Name="CurrentFontDataInfo"/>
                <TextBlock x:Name="CurrentFontIndexInfo"/>
            </StackPanel>
        </GroupBox>
        
        <!-- 新文件选择 -->
        <GroupBox Header="New Font Files" Grid.Row="1" Margin="0,0,0,10">
            <StackPanel Margin="10">
                <StackPanel Orientation="Horizontal" Margin="0,5">
                    <TextBlock Text="resfont.bin:" Width="100"/>
                    <TextBox x:Name="NewFontDataPath" Width="350" IsReadOnly="True"/>
                    <Button Content="Browse..." Click="BrowseFontData_Click" Margin="5,0,0,0"/>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,5">
                    <TextBlock Text="resfontidx.bin:" Width="100"/>
                    <TextBox x:Name="NewFontIndexPath" Width="350" IsReadOnly="True"/>
                    <Button Content="Browse..." Click="BrowseFontIndex_Click" Margin="5,0,0,0"/>
                </StackPanel>
            </StackPanel>
        </GroupBox>
        
        <!-- 验证结果 -->
        <GroupBox Header="Validation" Grid.Row="2" Margin="0,0,0,10">
            <ScrollViewer>
                <TextBlock x:Name="ValidationResult" TextWrapping="Wrap"/>
            </ScrollViewer>
        </GroupBox>
        
        <!-- 按钮 -->
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Grid.Row="3">
            <Button Content="Preview Font" Click="Preview_Click" Margin="0,0,10,0"/>
            <Button Content="Replace Both" Click="Replace_Click" IsEnabled="False" x:Name="ReplaceButton"/>
            <Button Content="Cancel" Click="Cancel_Click" Margin="10,0,0,0"/>
        </StackPanel>
    </Grid>
</Window>
```

#### 3.4.3 ViewModel 集成

在 `MainViewModel.cs` 中添加：

```csharp
/// <summary>
/// 执行字体资源替换
/// </summary>
private void ExecuteReplaceFont(object? parameter)
{
    if (SelectedResource == null)
        return;
    
    // 确保选中的是字体资源
    if (!IsFontResource(SelectedResource))
    {
        MessageBox.Show("Please select a font resource first.", 
                       "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
    }
    
    // 打开字体替换对话框
    var dialog = new FontReplaceDialog();
    dialog.SetCurrentFontInfo(FontData, FontIndex, FontInfo);
    
    if (dialog.ShowDialog() != true)
        return;
    
    // 获取新文件数据
    var newFontData = dialog.NewFontData;
    var newFontIndex = dialog.NewFontIndex;
    
    StatusMessage = "Replacing font resources...";
    
    try
    {
        var writer = new ResBinWriter(_currentFileData!, _currentTableOffset, 
                                    _parser!.GetResourceTable());
        
        // 替换 resfont.bin (ID 79)
        if (!writer.ReplaceResource(79, newFontData))
        {
            throw new Exception($"Failed to replace resfont.bin: {writer.ErrorMessage}");
        }
        
        // 替换 resfontidx.bin (ID 80)
        if (!writer.ReplaceResource(80, newFontIndex))
        {
            throw new Exception($"Failed to replace resfontidx.bin: {writer.ErrorMessage}");
        }
        
        // 更新数据
        _currentFileData = writer.GetData();
        
        // 更新 ViewModel 状态
        FontData = newFontData;
        FontIndex = newFontIndex;
        LoadFontForPreview(); // 重新加载预览
        
        // 标记两个资源为已修改
        var resfont = Resources.FirstOrDefault(r => r.Id == 79);
        var resfontidx = Resources.FirstOrDefault(r => r.Id == 80);
        
        if (resfont != null) resfont.IsModified = true;
        if (resfontidx != null) resfontidx.IsModified = true;
        
        StatusMessage = "✓ Font resources replaced successfully";
        
        MessageBox.Show(
            "Font resources replaced successfully!\n\n" +
            "Both resfont.bin and resfontidx.bin have been updated.\n" +
            "Don't forget to save the modified file.",
            "Success",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Error: {ex.Message}", "Error", 
                       MessageBoxButton.OK, MessageBoxImage.Error);
        StatusMessage = "Font replacement failed";
    }
}
```

### 3.5 实现步骤

**Phase 1: 基础框架** (优先级: 高)
1. ✅ 创建 FontValidator 类
2. ✅ 创建 FontReplaceDialog 窗口
3. ✅ 添加 ExecuteReplaceFont() 方法
4. ✅ 在 UI 中添加 "Replace Font" 按钮

**Phase 2: 格式验证** (优先级: 高)
1. ✅ 实现完整的字体格式验证
2. ✅ 显示验证结果和警告
3. ✅ 阻止无效文件的替换

**Phase 3: 预览功能** (优先级: 中)
1. 在对话框中嵌入 FontPreviewControl
2. 实时预览新字体效果
3. 字符对比显示

**Phase 4: 高级功能** (优先级: 低)
1. 支持从 TTF 字体转换
2. 自动生成索引文件
3. 字体优化工具

---

## 4. 实现计划

### 4.1 总体时间表

| 阶段 | 任务 | 预计时间 | 优先级 |
|------|------|----------|--------|
| **Week 1** | WAV 基础验证 | 2天 | 🔴 高 |
| **Week 1** | Font 验证框架 | 3天 | 🔴 高 |
| **Week 2** | Font 替换对话框 | 3天 | 🔴 高 |
| **Week 2** | 集成测试 | 2天 | 🟡 中 |
| **Week 3** | 详细信息显示 | 2天 | 🟡 中 |
| **Week 3** | 预览功能增强 | 2天 | 🟢 低 |
| **Week 4** | 文档和完善 | 1天 | 🟢 低 |

### 4.2 任务分解

#### Task 1: WAV 格式验证 (2天)
- [ ] 创建 WavValidator 类
- [ ] 实现 RIFF/WAVE 魔数检查
- [ ] 解析采样率、位深度等参数
- [ ] 集成到 ExecuteReplace()
- [ ] 添加确认对话框

#### Task 2: Font 验证框架 (3天)
- [ ] 创建 FontValidator 类
- [ ] 实现 resfont.bin 验证
- [ ] 实现 resfontidx.bin 验证
- [ ] 添加一致性检查
- [ ] 编写单元测试

#### Task 3: Font 替换对话框 (3天)
- [ ] 设计 XAML 界面
- [ ] 实现文件浏览功能
- [ ] 集成验证逻辑
- [ ] 添加预览控件
- [ ] 实现替换按钮逻辑

#### Task 4: ViewModel 集成 (2天)
- [ ] 添加 ExecuteReplaceFont() 方法
- [ ] 更新 UI 命令绑定
- [ ] 处理双资源同步更新
- [ ] 添加错误处理

#### Task 5: 测试和优化 (2天)
- [ ] 准备测试用例
- [ ] 测试各种场景
- [ ] 修复发现的问题
- [ ] 性能优化

---

## 5. 技术挑战与解决方案

### 5.1 挑战 1: Font 双文件同步

**问题**：
- resfont.bin 和 resfontidx.bin 必须同时替换
- 如果只替换一个，会导致字体无法正常使用

**解决方案**：
- 提供联合替换界面，强制用户同时选择两个文件
- 在替换前验证两个文件的一致性
- 原子操作：要么都成功，要么都失败

```csharp
// 原子替换示例
try
{
    writer.BeginTransaction(); // 开始事务
    
    writer.ReplaceResource(79, fontData);
    writer.ReplaceResource(80, fontIndex);
    
    writer.Commit(); // 提交事务
}
catch
{
    writer.Rollback(); // 回滚
    throw;
}
```

### 5.2 挑战 2: 字体格式兼容性

**问题**：
- 不同工具生成的字体文件格式可能略有差异
- 需要确保生成的字体能被 AX329x 固件正确加载

**解决方案**：
- 严格遵循 SDK 中的 font.c 解析逻辑
- 提供格式验证和警告
- 保留原始的字段顺序和字节序

### 5.3 挑战 3: 大文件替换性能

**问题**：
- 字体文件可能很大（几百 KB）
- 移动大量数据可能导致 UI 卡顿

**解决方案**：
- 使用异步操作
- 显示进度条
- 优化内存拷贝算法

```csharp
// 异步替换示例
private async Task ExecuteReplaceAsync(byte[] newData)
{
    IsLoading = true;
    StatusMessage = "Replacing...";
    
    await Task.Run(() =>
    {
        var writer = new ResBinWriter(...);
        writer.ReplaceResource(SelectedResource.Id, newData);
        _currentFileData = writer.GetData();
    });
    
    IsLoading = false;
    StatusMessage = "✓ Replaced";
}
```

### 5.4 挑战 4: 用户友好性

**问题**：
- 普通用户可能不理解字体文件的复杂性
- 需要提供清晰的指导和反馈

**解决方案**：
- 提供详细的帮助文档
- 显示直观的验证结果
- 给出明确的错误提示和建议

---

## 6. 总结

### 6.1 当前状态

✅ **已完成**：
- 基础的资源替换框架
- JPEG/BMP 图片替换
- WAV 基础替换（无验证）

❌ **待实现**：
- WAV 格式验证和详细信息
- Font 双文件联合替换
- 字体格式验证
- 替换预览功能

### 6.2 下一步行动

**立即开始**：
1. 实现 WAV 格式验证（Task 1）
2. 创建 Font 验证框架（Task 2）

**本周目标**：
- 完成 WAV 和 Font 的基础替换功能
- 能够替换并验证文件格式

**下周目标**：
- 完善用户界面
- 添加预览功能
- 进行全面测试

### 6.3 预期成果

完成后，ResBinManager 将支持：
- ✅ JPEG/BMP 图片预览和替换
- ✅ WAV 音频预览、验证和替换
- ✅ Font 字体预览、验证和替换
- ✅ 完整的资源管理解决方案

这将大大简化固件开发过程中的资源管理工作！🎉
