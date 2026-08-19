# Preview 按钮状态优化说明

## 修改目标
优化 Preview 按钮的显示状态，使其只有在**当前选中的资源被修改后**才可用。

## 实现方案

### 1. 修改 `CanExecutePreview` 方法
**文件**: `ViewModels/MainViewModel.cs`

```csharp
private bool CanExecutePreview(object? parameter) 
{ 
    return SelectedResource != null && SelectedResource.IsModified; 
}
```

**说明**: 
- 原来只检查是否有选中资源 (`SelectedResource != null`)
- 现在额外检查选中资源是否被修改过 (`SelectedResource.IsModified`)
- 只有当两个条件都满足时，Preview 按钮才可用

### 2. 在 `SelectedResource` setter 中触发命令状态更新
**文件**: `ViewModels/MainViewModel.cs`

```csharp
public ResourceItem? SelectedResource
{
    get => _selectedResource;
    set 
    { 
        // ... 原有代码 ...
        
        // 最后通知 UI 更新
        OnPropertyChanged();
        
        // 通知命令状态更新，使 Preview 按钮根据选中资源的 IsModified 状态变化
        (PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
```

**说明**:
- 当用户切换选中的资源时，自动更新 Preview 按钮的状态
- 如果新选中的资源未被修改，按钮会自动置灰
- 如果新选中的资源已被修改，按钮会变为可用

### 3. 在资源替换后触发命令状态更新

#### 3.1 普通资源替换 (`ExecuteReplace`)
**文件**: `ViewModels/MainViewModel.cs`

```csharp
if (writer.ReplaceResource(SelectedResource.Id, newData))
{
    _currentFileData = writer.GetData();
    
    var currentSelected = SelectedResource;
    currentSelected.IsModified = true;
    currentSelected.Size = (uint)newData.Length;
    
    // 通知 Preview 命令状态更新
    (PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
    
    // ... 后续代码 ...
}
```

#### 3.2 字体资源替换 (`ExecuteReplaceFont`)
**文件**: `ViewModels/MainViewModel.cs`

```csharp
// 标记两个资源为已修改
var resfont = Resources.FirstOrDefault(r => r != null && r.Id == 79);
var resfontidx = Resources.FirstOrDefault(r => r != null && r.Id == 80);

if (resfont != null) resfont.IsModified = true;
if (resfontidx != null) resfontidx.IsModified = true;

// 通知 Preview 命令状态更新
(PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
```

## 行为说明

### 场景 1: 初始状态
- 打开文件后，所有资源的 `IsModified` 都是 `false`
- 无论选中哪个资源，Preview 按钮都是**置灰**（不可用）状态

### 场景 2: 替换资源后
- 用户对某个资源执行 Replace 操作
- 该资源的 `IsModified` 被设置为 `true`
- Preview 按钮立即变为**可用**状态

### 场景 3: 切换选中资源
- 用户从已修改的资源切换到未修改的资源
- Preview 按钮自动变为**置灰**状态
- 用户从未修改的资源切换到已修改的资源
- Preview 按钮自动变为**可用**状态

### 场景 4: 保存后
- 用户执行 Save 操作后，资源列表会被刷新
- 所有资源的 `IsModified` 会被重置为 `false`
- Preview 按钮恢复为**置灰**状态

## 优势

1. **精确控制**: 只有当前选中的资源被修改后才能预览，避免误操作
2. **用户体验**: 按钮状态实时反映当前选中资源的状态，直观明了
3. **逻辑清晰**: 通过 WPF 的命令系统自动控制按钮状态，无需手动管理 UI

## 测试建议

1. 打开一个 RES.BIN 或 DestBin.bin 文件
2. 选中任意资源，确认 Preview 按钮是置灰的
3. 对该资源执行 Replace 操作
4. 确认 Preview 按钮变为可用状态
5. 点击 Preview 按钮，确认可以正常预览
6. 切换到另一个未修改的资源
7. 确认 Preview 按钮再次变为置灰状态
8. 对第二个资源执行 Replace 操作
9. 确认 Preview 按钮再次变为可用状态
