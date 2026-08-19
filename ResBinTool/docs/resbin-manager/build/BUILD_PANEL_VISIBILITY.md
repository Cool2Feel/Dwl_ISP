# BuildPanel 条件显示功能

## 📋 概述

ResBinManager 的固件构建面板（BuildPanelBorder）已优化为**条件显示**：
- **打开 RES.BIN** → 显示构建按钮和配置面板
- **打开 DestBin.bin** → 隐藏构建按钮和配置面板

这样设计的原因是：DestBin.bin 已经是完整的固件文件，不需要再次打包；而 RES.BIN 需要打包成固件才能使用。

---

## 🎯 功能特性

### 1. 智能显示逻辑

```
用户打开文件
    ↓
判断文件类型
    ↓
┌─────────────┬──────────────┐
│  RES.BIN    │ DestBin.bin  │
│             │              │
│ 显示面板 ✓  │ 隐藏面板 ✗   │
└─────────────┴──────────────┘
```

### 2. 用户体验优化

**RES.BIN 模式**：
- ✅ 显示 "🔨 Build Firmware" 按钮
- ✅ 显示 "⚙️ Config" 切换按钮
- ✅ 用户可以配置并打包固件

**DestBin.bin 模式**：
- ✅ 隐藏构建相关按钮（不需要）
- ✅ 界面更简洁
- ✅ 避免用户误操作

---

## 🔧 技术实现

### 1. 创建 BoolToVisibilityConverter

**文件**: `Converters/BoolToVisibilityConverter.cs`

```csharp
public class BoolToVisibilityConverter : IValueConverter
{
    public bool UseHidden { get; set; } = false;
    public bool Invert { get; set; } = false;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            // 检查参数是否需要反转
            bool invert = Invert;
            if (parameter is string param && param.Equals("Invert", StringComparison.OrdinalIgnoreCase))
                invert = !invert;

            // 如果需要反转，则取反
            if (invert)
                boolValue = !boolValue;

            // 根据布尔值返回可见性
            if (boolValue)
                return Visibility.Visible;
            else
                return UseHidden ? Visibility.Hidden : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            bool isVisible = (visibility == Visibility.Visible);
            return Invert ? !isVisible : isVisible;
        }

        return false;
    }
}
```

**特性**：
- ✅ 支持 `Invert` 属性反转逻辑
- ✅ 支持 `ConverterParameter="Invert"` 动态反转
- ✅ 支持 `UseHidden` 选择 Hidden 或 Collapsed
- ✅ 双向转换支持

---

### 2. XAML 资源声明

**文件**: `Views/MainWindow.xaml` (第 1-17 行)

```xml
<Window x:Class="ResBinManager.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:ResBinManager.ViewModels"
        xmlns:converters="clr-namespace:ResBinManager.Converters"
        Title="RES.BIN Resource Manager - AX329x SDK" 
        Height="750" Width="1200"
        WindowStartupLocation="CenterScreen">
    
    <Window.Resources>
        <!-- 布尔值到可见性转换器 -->
        <converters:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter"/>
    </Window.Resources>
    
    <Window.DataContext>
        <vm:MainViewModel/>
    </Window.DataContext>
```

**关键点**：
- 添加 `xmlns:converters` 命名空间
- 在 `Window.Resources` 中声明转换器实例

---

### 3. BuildPanelBorder Visibility 绑定

**文件**: `Views/MainWindow.xaml` (第 69-70 行)

```xml
<Border Background="Transparent" x:Name="BuildPanelBorder"
        Visibility="{Binding IsDestBinMode, Converter={StaticResource BoolToVisibilityConverter}, ConverterParameter=Invert}">
    <StackPanel Orientation="Horizontal">
        <Button Command="{Binding BuildFirmwareCommand}" ...>
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="🔨" FontSize="16"/>
                <TextBlock Text="Build Firmware" .../>
            </StackPanel>
        </Button>

        <Separator/>

        <ToggleButton x:Name="ToggleBuildPanelBtn" Content="⚙️ Config" .../>
    </StackPanel>
</Border>
```

**绑定逻辑**：
```
IsDestBinMode = false (RES.BIN) 
    → Converter 反转 
    → Visibility.Visible ✓

IsDestBinMode = true (DestBin.bin) 
    → Converter 反转 
    → Visibility.Collapsed ✗
```

---

## 📊 工作流程

### 场景 1：打开 RES.BIN

```
1. 用户点击 Open → 选择 RES.BIN
2. LoadFileSmart() 检测文件名
3. IsDestBinMode = false
4. BoolToVisibilityConverter 反转逻辑
5. BuildPanelBorder.Visibility = Visible ✓

界面显示：
┌──────────────────────────────────────┐
│ [Open] [Save] | [🔨 Build Firmware] [⚙️ Config] │
└──────────────────────────────────────┘
         ↑ 构建面板可见
```

### 场景 2：打开 DestBin.bin

```
1. 用户点击 Open → 选择 DestBin.bin
2. LoadFileSmart() 检测文件名包含 "destbin"
3. IsDestBinMode = true
4. BoolToVisibilityConverter 反转逻辑
5. BuildPanelBorder.Visibility = Collapsed ✗

界面显示：
┌──────────────────────────────────────┐
│ [Open] [Save] |                      │
└──────────────────────────────────────┘
         ↑ 构建面板隐藏
```

---

## 🎨 UI 效果对比

### RES.BIN 模式（显示）

```
┌────────────────────────────────────────────────────────┐
│ ToolBar                                                │
├────────────────────────────────────────────────────────┤
│ [📂 Open] [💿 Save] | [🔨 Build Firmware] [⚙️ Config] │
│                              ↑ 可见                    │
├────────────────────────────────────────────────────────┤
│ DataGrid (资源列表)                                     │
└────────────────────────────────────────────────────────┘
```

### DestBin.bin 模式（隐藏）

```
┌────────────────────────────────────────────────────────┐
│ ToolBar                                                │
├────────────────────────────────────────────────────────┤
│ [📂 Open] [💿 Save] |                                  │
│                              ↑ 隐藏                    │
├────────────────────────────────────────────────────────┤
│ DataGrid (资源列表)                                     │
└────────────────────────────────────────────────────────┘
```

---

## 🔍 技术细节

### 1. 数据绑定机制

```csharp
// MainViewModel.cs
private bool _isDestBinMode;
public bool IsDestBinMode
{
    get => _isDestBinMode;
    set
    {
        _isDestBinMode = value;
        OnPropertyChanged(nameof(IsDestBinMode));  // 通知 UI 更新
    }
}
```

**关键点**：
- ✅ 实现 `INotifyPropertyChanged`
- ✅ 设置时调用 `OnPropertyChanged`
- ✅ UI 自动响应变化

### 2. 转换器参数传递

```xml
ConverterParameter="Invert"
```

**作用**：
- 在 XAML 中动态指定反转逻辑
- 无需创建多个转换器实例
- 灵活控制显示/隐藏行为

### 3. Visibility 枚举

```csharp
public enum Visibility
{
    Visible,    // 可见且占用空间
    Hidden,     // 不可见但占用空间
    Collapsed   // 不可见且不占用空间
}
```

**当前使用**：`Collapsed`
- 完全隐藏，不占用布局空间
- 工具栏自动调整宽度

---

## 💡 设计理由

### 为什么 DestBin.bin 不需要构建面板？

**DestBin.bin 结构**：
```
┌─────────────────────────────────────┐
│ 程序代码段 (BLDR + APP)              │
├─────────────────────────────────────┤
│ RES.BIN 资源段                       │
├─────────────────────────────────────┤
│ 尾部填充（可选）                      │
└─────────────────────────────────────┘
```

**特点**：
- ✅ 已经是完整的固件文件
- ✅ 包含引导加载程序和应用程序
- ✅ 可以直接烧录到设备

**RES.BIN 结构**：
```
┌─────────────────────────────────────┐
│ 资源索引表                            │
├─────────────────────────────────────┤
│ 图片资源 (JPEG/BMP)                  │
│ 音频资源 (WAV)                       │
│ 字体资源                             │
│ ...                                  │
└─────────────────────────────────────┘
```

**特点**：
- ⚠️ 只是资源文件，不是完整固件
- ⚠️ 需要与程序代码合并
- ⚠️ 需要使用 MakeSPIBin.exe 打包

**结论**：
- DestBin.bin → 直接保存即可使用
- RES.BIN → 需要打包成固件才能使用

---

## 🛠️ 扩展性

### 未来可能的增强

#### 1. 动画过渡

```xml
<Border.Triggers>
    <EventTrigger RoutedEvent="Border.Loaded">
        <BeginStoryboard>
            <Storyboard>
                <DoubleAnimation Storyboard.TargetProperty="Opacity"
                               From="0" To="1" Duration="0:0:0.3"/>
            </Storyboard>
        </BeginStoryboard>
    </EventTrigger>
</Border.Triggers>
```

#### 2. 提示信息

```xml
<Border.ToolTip>
    <ToolTip Visibility="{Binding IsDestBinMode, Converter={StaticResource BoolToVisibilityConverter}}">
        <TextBlock Text="Firmware build tools are not available for DestBin.bin files."/>
    </ToolTip>
</Border.ToolTip>
```

#### 3. 禁用而非隐藏

如果希望保留按钮位置但禁用功能：

```xml
<Button IsEnabled="{Binding IsDestBinMode, Converter={StaticResource BoolToVisibilityConverter}, ConverterParameter=Invert}">
    ...
</Button>
```

---

## 📝 测试用例

### 测试 1：打开 RES.BIN

```
步骤：
1. 启动 ResBinManager
2. 点击 Open → 选择 RES.BIN
3. 观察工具栏

预期结果：
✓ Build Firmware 按钮可见
✓ Config 切换按钮可见
✓ 可以点击进行固件打包
```

### 测试 2：打开 DestBin.bin

```
步骤：
1. 启动 ResBinManager
2. 点击 Open → 选择 DestBin.bin
3. 观察工具栏

预期结果：
✓ Build Firmware 按钮隐藏
✓ Config 切换按钮隐藏
✓ 工具栏宽度自动调整
```

### 测试 3：切换文件类型

```
步骤：
1. 打开 RES.BIN（显示构建面板）
2. 关闭文件
3. 打开 DestBin.bin
4. 观察工具栏变化

预期结果：
✓ 构建面板立即隐藏
✓ 无闪烁或延迟
✓ UI 响应流畅
```

---

## 🔗 相关文件

### 新增文件

1. **[BoolToVisibilityConverter.cs](file://d:/dwl/work/2026/JT/JX_SDK/JT529X/firmware/tools/ResBinManager/Converters/BoolToVisibilityConverter.cs)**
   - 布尔值到可见性转换器
   - 支持反转逻辑
   - 支持参数化配置

### 修改文件

2. **[MainWindow.xaml](file://d:/dwl/work/2026/JT/JX_SDK/JT529X/firmware/tools/ResBinManager/Views/MainWindow.xaml)**
   - 添加 converters 命名空间
   - 声明转换器资源
   - BuildPanelBorder 添加 Visibility 绑定

3. **[MainViewModel.cs](file://d:/dwl/work/2026/JT/JX_SDK/JT529X/firmware/tools/ResBinManager/ViewModels/MainViewModel.cs)**
   - 已有 `IsDestBinMode` 属性
   - 无需修改

---

## ✅ 总结

### 核心改进

1. ✅ **智能显示**：根据文件类型自动显示/隐藏构建面板
2. ✅ **用户体验**：简化 DestBin 模式的界面
3. ✅ **逻辑清晰**：避免用户对 DestBin 进行不必要的打包操作
4. ✅ **可扩展**：转换器可复用于其他场景

### 技术亮点

- 🎯 **数据绑定**：WPF MVVM 模式的最佳实践
- 🔄 **值转换器**：灵活的布尔值到可见性转换
- 📦 **参数化**：支持动态反转逻辑
- 🚀 **性能**：零额外开销，纯声明式实现

### 适用场景

- ✅ RES.BIN 资源编辑后需要打包
- ✅ DestBin.bin 固件直接保存使用
- ✅ 多文件类型混合管理
- ✅ 界面元素条件显示

---

**版本**: v1.0  
**更新日期**: 2026-05-19  
**作者**: ResBinManager Team
