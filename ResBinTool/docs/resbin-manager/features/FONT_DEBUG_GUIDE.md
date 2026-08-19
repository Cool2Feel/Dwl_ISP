# Font 面板显示问题排查指南

## 🔍 问题诊断步骤

### 步骤 1: 运行程序并打开输出窗口

1. 在 Visual Studio 或 VS Code 中打开项目
2. 按 `F5` 启动调试
3. 打开 **输出窗口**（View → Output 或 Ctrl+Alt+O）
4. 确保输出窗口的下拉菜单选择 **"Debug"**

### 步骤 2: 加载 RES.BIN 文件

1. 点击 "Open" 按钮
2. 选择您的 RES.BIN 文件
3. 等待加载完成

### 步骤 3: 选中字体资源

1. 在资源列表中找到 **ID 78** 或 **ID 79**
2. **单击**该行选中它
3. 观察输出窗口中的调试信息

---

## 📋 预期的调试输出

如果一切正常，您应该看到类似以下的输出：

```
[VM] SelectedResource changed: ID=78, Type=Binary, Name=resfont.bin
[VM] Loading Font preview
[Font] Loading font for resource: ID=78, Name=resfont.bin
[Font] Found resfont: Size=84480, Offset=123456
[Font] Found resfontidx: Size=76800, Offset=207936
[Font] Extracted data: FontData.Length=84480, FontIndex.Length=76800
[Font] Parsed successfully: 1500 chars, 2 languages
[UI] LoadFontPreview: FontInfo is not null
[UI] Loading font preview: 1500 chars, 2 languages
[UI] Font preview loaded successfully
```

---

## ❌ 常见问题及解决方案

### 问题 1: 没有任何调试输出

**症状**: 点击资源后，输出窗口没有任何 `[VM]` 或 `[Font]` 开头的信息

**可能原因**:
- 程序不是以调试模式运行
- 输出窗口未选择 "Debug" 级别

**解决方案**:
1. 确保按 `F5` 而不是 `Ctrl+F5` 启动
2. 检查输出窗口右下角的下拉菜单，选择 "Debug"
3. 重新编译并运行：`dotnet build` 然后 `dotnet run`

---

### 问题 2: 输出显示 "Missing required data"

**症状**:
```
[VM] SelectedResource changed: ID=78, Type=Binary, Name=resfont.bin
[VM] Loading Font preview
[Font] LoadFontForPreview: Missing required data
```

**可能原因**:
- `_parser` 为 null（文件未正确加载）
- `_currentFileData` 为 null

**解决方案**:
1. 确认 RES.BIN 文件已成功加载（状态栏应显示 "Loaded XX resources..."）
2. 尝试重新打开文件
3. 检查文件是否损坏

---

### 问题 3: 输出显示 "Font resources not found in list"

**症状**:
```
[VM] SelectedResource changed: ID=78, Type=Binary, Name=resfont.bin
[VM] Loading Font preview
[Font] Loading font for resource: ID=78, Name=resfont.bin
[Font] Font resources not found in list
```

**可能原因**:
- Resources 列表中找不到 ID 78 或 79 的资源
- 资源 ID 不匹配

**解决方案**:
1. 检查资源列表中是否真的有 ID 78 和 79
2. 查看 Resources 集合的内容：
   ```csharp
   // 在 Immediate Window 中输入
   ViewModel.Resources.Where(r => r.Id == 78 || r.Id == 79).ToList()
   ```
3. 如果 ID 不同，修改 `IsFontResource()` 方法中的 ID 判断

---

### 问题 4: 解析失败，显示错误消息

**症状**:
```
[Font] Error: Invalid font index magic: 0xXXXX
```
或弹出错误对话框

**可能原因**:
- resfontidx.bin 文件格式不正确
- 魔数验证失败（应该是 0x584D）

**解决方案**:
1. 确认使用的是正确的 RES.BIN 文件
2. 检查 resfontidx.bin 的前 4 个字节：
   ```csharp
   // 在 Immediate Window 中
   BitConverter.ToUInt32(ViewModel.FontIndex, 0).ToString("X8")
   ```
3. 如果魔数不对，说明文件格式有问题

---

### 问题 5: UI 层显示 "FontInfo is null"

**症状**:
```
[VM] SelectedResource changed: ID=78, Type=Binary, Name=resfont.bin
[VM] Loading Font preview
[Font] ... (各种日志)
[UI] LoadFontPreview: FontInfo is null
```

**可能原因**:
- ViewModel 的 `LoadFontForPreview()` 执行失败
- `FontInfo` 属性未被正确设置

**解决方案**:
1. 检查 ViewModel 层的完整日志
2. 确认没有异常抛出
3. 查看是否有 "Parsed successfully" 的消息

---

### 问题 6: 面板仍然不显示

**症状**: 所有日志都正常，但 FontControlPanel 仍然不可见

**可能原因**:
- XAML 中的 Visibility 绑定问题
- 面板被其他控件遮挡

**解决方案**:
1. 在 XAML 中临时添加背景色测试：
   ```xml
   <StackPanel x:Name="FontControlPanel" 
               Background="Yellow"  <!-- 临时添加 -->
               Visibility="Visible"> <!-- 强制可见 -->
   ```
2. 检查 Grid 的列定义是否正确
3. 确认 PreviewPanel 和 BuildConfigPanel 不会覆盖它

---

## 🛠️ 手动测试代码

如果自动检测不起作用，可以在 Immediate Window 中手动测试：

### 测试 1: 检查资源列表
```csharp
// 在 Immediate Window (Ctrl+Alt+I) 中输入
ViewModel.Resources.Count
ViewModel.Resources.FirstOrDefault(r => r.Id == 78)
ViewModel.Resources.FirstOrDefault(r => r.Id == 79)
```

### 测试 2: 手动触发字体加载
```csharp
// 选中 ID 78 的资源
var fontResource = ViewModel.Resources.FirstOrDefault(r => r.Id == 78);
ViewModel.SelectedResource = fontResource;
```

### 测试 3: 检查属性值
```csharp
ViewModel.FontInfo
ViewModel.FontData?.Length
ViewModel.FontIndex?.Length
```

### 测试 4: 手动显示面板
```csharp
// 在 MainWindow.xaml.cs 的某个方法中
FontControlPanel.Visibility = System.Windows.Visibility.Visible;
```

---

## 📊 快速诊断流程图

```
选中 ID 78/79 资源
    ↓
有调试输出吗？
    ├─ 否 → 检查是否以调试模式运行
    └─ 是 ↓
          
输出包含 "Loading Font preview" 吗？
    ├─ 否 → 检查 IsFontResource() 逻辑
    └─ 是 ↓
          
输出包含 "Parsed successfully" 吗？
    ├─ 否 → 查看错误信息，检查文件格式
    └─ 是 ↓
          
输出包含 "Font preview loaded successfully" 吗？
    ├─ 否 → 检查 UI 层代码和 FontPreviewContainer
    └─ 是 ↓
          
面板显示了吗？
    ├─ 否 → 检查 XAML 布局和 Visibility
    └─ 是 → ✅ 成功！
```

---

## 💡 常见陷阱

### 陷阱 1: 资源类型判断错误

确保资源的 `Type` 属性是 `ResourceType.Binary`：

```csharp
// 在 Immediate Window 中检查
ViewModel.SelectedResource.Type
// 应该输出: Binary
```

如果不是 Binary，可能需要修改资源类型检测逻辑。

### 陷阱 2: ID 不匹配

不同的 RES.BIN 文件可能有不同的 ID 分配。检查您的 RES.H 文件：

```c
#define RES_RESFONT      ??  // 实际 ID
#define RES_RESFONTIDX   ??  // 实际 ID
```

如果 ID 不是 78/79，需要修改代码中的硬编码 ID。

### 陷阱 3: 文件名大小写

`IsFontResource()` 使用 `Contains("resfont", StringComparison.OrdinalIgnoreCase)`，所以大小写不敏感。但如果文件名完全不包含 "resfont"，则会失败。

---

## 📞 获取帮助

如果以上步骤都无法解决问题，请提供以下信息：

1. **完整的调试输出**（从选中资源开始的所有 `[VM]`, `[Font]`, `[UI]` 日志）
2. **RES.H 文件中 RESFONT 和 RESFONTIDX 的定义**
3. **资源列表中 ID 78 和 79 的截图**
4. **状态栏显示的消息**

这样可以更准确地定位问题所在。

---

**最后更新**: 2026-05-18  
**版本**: v1.3.0
