# SetMode 功能完善报告

## 修复日期
2026年4月13日

## 问题概述

在 `DeviceConfigPage.xaml` 第 184-189 行中，三个 RadioButton（RAW、MJPG、YUV）都错误地绑定到了不存在的 `SetModeAuto` 属性，导致：
1. **绑定失败**：ViewModel 中不存在 `SetModeAuto` 属性
2. **逻辑错误**：三个 RadioButton 应该互斥，但都绑定到同一属性无法实现单选功能
3. **数据转换错误**：`Config.cs` 中将 `SetMode` 枚举错误地转换为 `bool`

## 修复内容

### 1. 添加 SetMode 枚举定义
**文件**: `ThunderSE\DeviceConfig\Isp\CommonConfig.cs`

```csharp
/// <summary>
/// 输出格式模式
/// </summary>
public enum SetMode
{
    RAW = 0,   // RAW 格式
    MJPG = 1,  // MJPEG 压缩格式
    YUV = 2    // YUV 格式
}
```

### 2. 在 CommonData 结构体中添加字段
**文件**: `ThunderSE\DeviceConfig\Isp\CommonConfig.cs`

```csharp
public struct CommonData
{
    // ... 其他字段 ...
    public char set_mode;  // 输出格式模式：0=RAW, 1=MJPG, 2=YUV
}
```

### 3. 在 CommonConfig 类中添加 SetMode 属性
**文件**: `ThunderSE\DeviceConfig\Isp\CommonConfig.cs`

```csharp
private SetMode _setMode = SetMode.RAW;

/// <summary>
/// 输出格式模式（RAW/MJPG/YUV）
/// </summary>
public SetMode SetMode
{
    get { return _setMode; }
    set
    {
        _setMode = value;
        if (PropertyChanged != null)
            PropertyChanged(this, new PropertyChangedEventArgs("SetMode"));
    }
}
```

### 4. 完善 ParamsDataCollection 的序列化/反序列化
**文件**: `ThunderSE\DeviceConfig\Isp\CommonConfig.cs`

**序列化（getter）**:
```csharp
CommonData commonDataParams = new CommonData()
{
    // ... 其他字段 ...
    set_mode = (char)SetMode,
};
```

**反序列化（setter）**:
```csharp
SetMode = (SetMode)commonDataParams.set_mode;
```

**属性映射**:
```csharp
PropertyNameToStructMemberMap = new Dictionary<string, string>()
{
    // ... 其他映射 ...
    {"SetMode","set_mode"},
};
```

### 5. 在 ViewModel 中添加 SetMode 属性
**文件**: `ThunderSE\Ui\MainWindow\DeviceConfigPageViewModel.cs`

```csharp
/// <summary>
/// 输出格式模式（RAW/MJPG/YUV）
/// </summary>
public DeviceConfig.Isp.SetMode SetMode
{
    get { return _ispProcessor.IspCommonConfig.SetMode; }
    set { _ispProcessor.IspCommonConfig.SetMode = value; }
}
```

### 6. 创建 EnumToBooleanConverter 转换器
**文件**: `ThunderSE\Ui\Converter\Converter.cs`

```csharp
/// <summary>
/// 枚举值到布尔值的转换器，用于 RadioButton 与枚举属性的双向绑定
/// </summary>
public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;
        
        return value.ToString().Equals(parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return Binding.DoNothing;

        bool isChecked = (bool)value;
        if (isChecked)
        {
            if (targetType.IsEnum)
            {
                try
                {
                    return Enum.Parse(targetType, parameter.ToString(), true);
                }
                catch
                {
                    return Binding.DoNothing;
                }
            }
        }

        return Binding.DoNothing;
    }
}
```

### 7. 修正 XAML 绑定
**文件**: `ThunderSE\Ui\MainWindow\DeviceConfigPage.xaml`

**添加转换器资源**:
```xml
<UserControl.Resources>
    <!-- ... 其他资源 ... -->
    <CV:EnumToBooleanConverter x:Key="enumToBooleanConverter"/>
</UserControl.Resources>
```

**修正 RadioButton 绑定**:
```xml
<StackPanel Orientation="Horizontal" Style="{StaticResource ParamRowStyle}">
    <Label Content="set mode :" Style="{StaticResource ParamLabelStyle}"/>
    <RadioButton Content="RAW" GroupName="SetMode" Margin="8,4,20,0" VerticalAlignment="Center" 
                 IsChecked="{Binding SetMode, Mode=TwoWay, Converter={StaticResource enumToBooleanConverter}, ConverterParameter=RAW}"/>
    <RadioButton Content="MJPG" GroupName="SetMode" Margin="8,4,20,0" VerticalAlignment="Center" 
                 IsChecked="{Binding SetMode, Mode=TwoWay, Converter={StaticResource enumToBooleanConverter}, ConverterParameter=MJPG}"/>
    <RadioButton Content="YUV" GroupName="SetMode" Margin="8,4,10,0" VerticalAlignment="Center" 
                 IsChecked="{Binding SetMode, Mode=TwoWay, Converter={StaticResource enumToBooleanConverter}, ConverterParameter=YUV}"/>
</StackPanel>
```

### 8. 修复 Config.cs 中的数据转换错误 ⚠️
**文件**: `ThunderSE\DeviceConfig\Config.cs` (第 104-108 行)

**修复前（错误）**:
```csharp
else if(valueToWrite.GetType() == typeof(SetMode))
{
    bytesToWrite = new byte[] { (bool)valueToWrite ? (byte)0x01 : (byte)0x00 };
}
```

**修复后（正确）**:
```csharp
else if (valueToWrite.GetType() == typeof(SetMode))
{
    // SetMode 枚举值转换为 byte：RAW=0, MJPG=1, YUV=2
    bytesToWrite = new byte[] { (byte)(SetMode)valueToWrite };
}
```

**错误原因**:
- 尝试将 `SetMode` 枚举转换为 `bool` 会导致 `InvalidCastException`
- 应该直接将枚举值转换为 `byte`，因为 `SetMode` 枚举的定义值就是 0、1、2

## 技术要点

### RadioButton 与枚举的双向绑定
WPF 的 RadioButton 的 `IsChecked` 属性是 `bool?` 类型，不能直接绑定到枚举属性。需要使用 `IValueConverter` 进行转换：

1. **Convert**: 枚举 → 布尔值（用于显示）
   - 比较当前枚举值与目标值是否相等
   - 相等返回 `true`（选中），否则返回 `false`

2. **ConvertBack**: 布尔值 → 枚举（用于用户交互）
   - 如果 `IsChecked == true`，返回目标枚举值
   - 如果 `IsChecked == false`，返回 `Binding.DoNothing`（不改变绑定源）

### 枚举与字节数组的转换
在设备通信中，需要将各种类型的数据转换为字节数组：

```csharp
// byte 类型
bytesToWrite = new byte[] { (byte)valueToWrite };

// 枚举类型（如 SetMode、BayerMode）
bytesToWrite = new byte[] { (byte)(SetMode)valueToWrite };

// 其他数值类型（int、short 等）
bytesToWrite = BitConverter.GetBytes(valueToWrite);
```

## 测试建议

1. **UI 测试**:
   - 打开 DeviceConfigPage，检查三个 RadioButton 是否正常显示
   - 点击不同的 RadioButton，验证互斥行为是否正常
   - 修改 SetMode 后，检查是否正确触发 PropertyChanged 事件

2. **数据序列化测试**:
   - 保存配置文件，验证 set_mode 字段是否正确写入
   - 加载配置文件，验证 set_mode 字段是否正确读取
   - 分别测试 RAW、MJPG、YUV 三种模式的保存和加载

3. **设备通信测试**（如果在线）:
   - 切换到不同 SetMode，验证是否正确发送字节到设备
   - 使用字节分析工具验证发送的数据是否正确

## 影响范围

### 修改的文件
1. `ThunderSE\DeviceConfig\Isp\CommonConfig.cs` - 添加枚举、属性、序列化逻辑
2. `ThunderSE\Ui\MainWindow\DeviceConfigPageViewModel.cs` - 添加 ViewModel 属性
3. `ThunderSE\Ui\Converter\Converter.cs` - 添加转换器
4. `ThunderSE\Ui\MainWindow\DeviceConfigPage.xaml` - 修正绑定
5. `ThunderSE\DeviceConfig\Config.cs` - 修复数据转换错误

### 兼容性
- ✅ 向后兼容：如果配置文件没有 set_mode 字段，默认使用 RAW 模式
- ✅ 不影响其他 ISP 模块
- ✅ 转换器可复用于其他枚举类型的 RadioButton 绑定

## 后续优化建议

1. **添加验证**: 在 SetMode 属性 setter 中添加范围验证
2. **日志记录**: 在 SetMode 变化时记录日志，便于调试
3. **单元测试**: 为 EnumToBooleanConverter 添加单元测试
4. **文档完善**: 在用户手册中说明三种 SetMode 的区别和使用场景

## 总结

本次修复解决了 SetMode 功能的完整实现链中的多个问题：
- ✅ 数据模型层（CommonConfig）
- ✅ ViewModel 层（DeviceConfigPageViewModel）
- ✅ View 层（DeviceConfigPage.xaml）
- ✅ 转换器（EnumToBooleanConverter）
- ✅ 设备通信层（Config.cs）

所有修改遵循 MVVM 架构原则，保持了代码的一致性和可维护性。
