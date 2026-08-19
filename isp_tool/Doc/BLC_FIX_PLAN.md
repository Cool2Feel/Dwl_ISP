# BLC 模块问题修复方案

## 文档信息

| 项目 | 内容 |
|------|------|
| **模块名称** | BLC (Black Level Correction) |
| **文档版本** | v1.0 |
| **创建日期** | 2026年4月8日 |
| **参考文档** | [BLC_MODULE_SPECIFICATION.md](BLC_MODULE_SPECIFICATION.md) |

---

## 问题修复总览

| 编号 | 严重程度 | 问题描述 | 修复状态 |
|------|---------|---------|---------|
| B1 | 🔴 高 | BlcImg 宽高参数颠倒 | ⬜ 待修复 |
| B2 | 🔴 高 | DeserializeFromXmlElement 缺少 null 检查 | ⬜ 待修复 |
| B3 | 🔴 高 | CorrectValuesArray 返回直接引用 | ⬜ 待修复 |
| B4 | 🟡 中 | PropertyChanged 属性名不一致 | ⬜ 待修复 |
| B5 | 🟡 中 | CalBlackLevelData 多余零初始化 | ⬜ 待修复 |
| B6 | 🟡 中 | 窗口关闭未调用 Cleanup | ⬜ 待修复 |
| B9 | 🟢 低 | GetMedianPixelValue 跳过负值 | ⬜ 待修复 |

---

## 详细修复方案

### B1: BlcImg 宽高参数颠倒 🔴

#### 问题分析

**位置**: `IspApi.cs` 第 59 行

**当前 P/Invoke 签名**:
```csharp
[DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
public static extern void BlcImg(byte[] imgBuffer, short[] correctionValues, int polarity,
    int imgHeight, int imgWidth,  short[] outImg);
//                                                   ^^^^^^^^^  ^^^^^^^^^
//                                                   参数顺序: 高度, 宽度
```

**调用处**: `BlackLevel.cs` 第 156 行
```csharp
IspApi.BlcImg(imgBuffer, _correctValuesArray, (int)_commonConfig.Bayer,
    _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, outputBuffer);
//  ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
//  传入顺序: Width, Height (与 P/Invoke 签名相反!)
```

**C++ 端签名** (`IQ.cpp` 第 74 行):
```cpp
ISP_API void BlcImg(
    const void* img_buffer,
    short *correction_val,
    int polarity_mode,
    int image_width,     // C++ 端期望: 宽度在前
    int image_height,    // C++ 端期望: 高度在后
    short *blc_img
);
```

**问题影响**:
- 当 `Width != Height` 时 (如 1920x1080)，图像处理会按错误的行列数遍历
- 导致像素位置计算错误，Bayer 通道匹配出错
- 校正结果完全错误

#### 修复方案

**方案 A: 修正 P/Invoke 签名 (推荐)** ✅

修改 `IspApi.cs`，使参数顺序与 C++ 端一致:

```csharp
[DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
public static extern void BlcImg(byte[] imgBuffer, short[] correctionValues, int polarity,
    int imgWidth, int imgHeight, short[] outImg);
//  ^^^^^^^^^^  ^^^^^^^^^^^
//  修正为: 宽度在前，高度在后 (与 C++ 端一致)
```

**优点**: 
- 调用代码无需修改
- 与 C++ 端签名完全对应
- 符合常规习惯 (Width × Height)

**方案 B: 修正调用顺序**

修改 `BlackLevel.cs` 调用:

```csharp
IspApi.BlcImg(imgBuffer, _correctValuesArray, (int)_commonConfig.Bayer,
    _commonConfig.ResolutionHeight, _commonConfig.ResolutionWidth, outputBuffer);
//  ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
//  修正为: Height, Width (匹配当前 P/Invoke 签名)
```

**缺点**: 
- 违反直觉，容易再次出错
- 与 C++ 端签名不对应

#### 推荐实施

采用 **方案 A**，修改 `IspApi.cs`:

```csharp
// 修改前 (第 59 行)
[DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
public static extern void BlcImg(byte[] imgBuffer, short[] correctionValues, int polarity,
    int imgHeight, int imgWidth,  short[] outImg);

// 修改后
[DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
public static extern void BlcImg(byte[] imgBuffer, short[] correctionValues, int polarity,
    int imgWidth, int imgHeight, short[] outImg);
```

**验证**: 无需修改 `BlackLevel.cs` 调用处，因为传入顺序本身就是正确的 (Width, Height)。

---

### B2: DeserializeFromXmlElement 缺少 null 检查 🔴

#### 问题分析

**位置**: `BlackLevel.cs` 第 290-296 行

**当前代码**:
```csharp
public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
{
    var blcNode = ispToolDataNode["Blc"];

    R = XmlHelper.GetNodeShort(blcNode, "BlcR");  // ⚠️ 如果 blcNode 为 null，会抛 NullReferenceException
    Gr = XmlHelper.GetNodeShort(blcNode, "BlcGr");
    Gb = XmlHelper.GetNodeShort(blcNode, "BlcGb");
    B = XmlHelper.GetNodeShort(blcNode, "BlcB");
}
```

**问题场景**:
1. XML 配置文件中缺少 `<Blc>` 节点
2. XML 格式不匹配 (如旧版本配置文件)
3. 配置文件损坏或被手动编辑错误

**影响**: 程序崩溃，无法加载配置

#### 修复方案

**方案: 添加 null 检查并使用安全解析**

```csharp
public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
{
    if (ispToolDataNode == null)
    {
        // 可选: 记录日志
        return; // 保持默认值 (0, 0, 0, 0)
    }

    var blcNode = ispToolDataNode["Blc"];
    if (blcNode == null)
    {
        // 可选: 记录日志
        return; // 保持默认值
    }

    // 使用 XmlHelper 安全解析 (已支持 null 节点和解析失败)
    R = XmlHelper.GetNodeShort(blcNode, "BlcR", 0);
    Gr = XmlHelper.GetNodeShort(blcNode, "BlcGr", 0);
    Gb = XmlHelper.GetNodeShort(blcNode, "BlcGb", 0);
    B = XmlHelper.GetNodeShort(blcNode, "BlcB", 0);
}
```

**XmlHelper.GetNodeShort 已处理的情况**:
```csharp
// XmlHelper.cs 第 45-53 行
public static short GetNodeShort(XmlNode parentNode, string childName, short defaultValue = 0)
{
    var value = GetNodeValue(parentNode, childName);
    if (value == null)  // 节点不存在时返回 defaultValue
    {
        return defaultValue;
    }
    return short.TryParse(value, out short result) ? result : defaultValue;
}
```

**修复后行为**:
| 场景 | 修复前 | 修复后 |
|------|--------|--------|
| `<Blc>` 节点缺失 | NullReferenceException | 保持默认值 0 |
| `<BlcR>` 子节点缺失 | 返回 0 (正确) | 返回 0 (正确) |
| `<BlcR>` 内容非数字 | 返回 0 (正确) | 返回 0 (正确) |
| `ispToolDataNode` 为 null | NullReferenceException | 直接返回 |

#### 推荐实施

修改 `BlackLevel.cs` 第 290-296 行:

```csharp
// 修改前
public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
{
    var blcNode = ispToolDataNode["Blc"];

    R = XmlHelper.GetNodeShort(blcNode, "BlcR");
    Gr = XmlHelper.GetNodeShort(blcNode, "BlcGr");
    Gb = XmlHelper.GetNodeShort(blcNode, "BlcGb");
    B = XmlHelper.GetNodeShort(blcNode, "BlcB");
}

// 修改后
public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
{
    if (ispToolDataNode == null)
        return;

    var blcNode = ispToolDataNode["Blc"];
    if (blcNode == null)
        return;

    R = XmlHelper.GetNodeShort(blcNode, "BlcR", 0);
    Gr = XmlHelper.GetNodeShort(blcNode, "BlcGr", 0);
    Gb = XmlHelper.GetNodeShort(blcNode, "BlcGb", 0);
    B = XmlHelper.GetNodeShort(blcNode, "BlcB", 0);
}
```

---

### B3: CorrectValuesArray 返回直接引用 🔴

#### 问题分析

**位置**: `BlackLevel.cs` 第 120-123 行

**当前代码**:
```csharp
public short[] CorrectValuesArray
{
    get { return _correctValuesArray; }  // ⚠️ 返回内部数组的直接引用
}
```

**问题场景**:
```csharp
// 外部代码获取数组
short[] values = blackLevel.CorrectValuesArray;

// 外部修改数组 (不会触发 PropertyChanged)
values[0] = 100;  // ⚠️ 内部 _correctValuesArray 被修改，但 UI 不知道

// 或者更危险的情况
Array.Clear(blackLevel.CorrectValuesArray, 0, 4);  // ⚠️ 内部状态被破坏
```

**影响**:
1. 外部修改不会触发 `PropertyChanged`，UI 不同步
2. 破坏封装性，内部状态可被意外修改
3. 难以追踪 bug 来源

#### 修复方案

**方案 A: 返回副本 (推荐)** ✅

```csharp
public short[] CorrectValuesArray
{
    get 
    { 
        return (short[])_correctValuesArray.Clone();  // 返回副本
    }
}
```

**优点**: 
- 完全隔离内部状态
- 调用者可自由修改返回的数组
- 线程安全 (快照语义)

**缺点**: 
- 每次调用都创建新数组 (性能开销极小，4 个 short)

**方案 B: 返回只读包装**

```csharp
public IReadOnlyList<short> CorrectValuesArray
{
    get { return Array.AsReadOnly(_correctValuesArray); }
}
```

**优点**: 
- 零拷贝，性能更好
- 编译期阻止修改

**缺点**: 
- 改变返回类型，可能影响现有调用代码
- 需要检查所有调用处是否兼容

#### 推荐实施

采用 **方案 A** (兼容性最好):

修改 `BlackLevel.cs` 第 120-123 行:

```csharp
// 修改前
public short[] CorrectValuesArray
{
    get { return _correctValuesArray; }
}

// 修改后
public short[] CorrectValuesArray
{
    get { return (short[])_correctValuesArray.Clone(); }
}
```

**影响范围检查**:
- `BlackLevel.cs` 内部使用 `_correctValuesArray` 字段，不受影响
- 外部调用处获取的是副本，修改不影响内部状态

---

### B4: PropertyChanged 属性名不一致 🟡

#### 问题分析

**位置**: `BlackLevel.cs` 第 107-113 行

**当前代码**:
```csharp
private void SetCorrectValue(BlackLevelPixelType pixelType, short value,
    [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
{
    _correctValuesArray[(int)pixelType] = value;
    HasChangedParams = true;

    // ⚠️ 问题: 使用 CallerMemberName 但实际通知的是 "CorrectValuesArray"
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

**问题**:
- `CallerMemberName` 会捕获调用者的属性名 (如 "R", "Gr", "Gb", "B")
- 但代码注释和之前实现都通知 `"CorrectValuesArray"`
- 如果 UI 绑定到 `"R"` 等具体属性，将收不到通知

**实际检查**: 当前代码已使用 `CallerMemberName`，这是**改进后的版本**，但仍需确认:
1. UI 是否绑定到具体属性 (R/Gr/Gb/B)
2. 还是绑定到 `CorrectValuesArray`

#### 修复方案

**方案: 同时通知两个属性名 (兼容所有绑定场景)**

```csharp
private void SetCorrectValue(BlackLevelPixelType pixelType, short value,
    [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
{
    _correctValuesArray[(int)pixelType] = value;
    HasChangedParams = true;

    // 通知具体改变的属性 (R/Gr/Gb/B)
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    
    // 同时通知数组属性 (兼容绑定到 CorrectValuesArray 的场景)
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CorrectValuesArray"));
}
```

**优点**: 
- 兼容所有绑定场景
- 性能开销极小 (额外一次事件触发)

#### 推荐实施

修改 `BlackLevel.cs` 第 107-113 行:

```csharp
// 修改前
private void SetCorrectValue(BlackLevelPixelType pixelType, short value,
    [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
{
    _correctValuesArray[(int)pixelType] = value;
    HasChangedParams = true;

    // 通知具体改变的属性
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

// 修改后
private void SetCorrectValue(BlackLevelPixelType pixelType, short value,
    [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
{
    _correctValuesArray[(int)pixelType] = value;
    HasChangedParams = true;

    // 通知具体改变的属性 (R/Gr/Gb/B)
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    
    // 同时通知数组属性 (兼容绑定到 CorrectValuesArray 的场景)
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CorrectValuesArray"));
}
```

---

### B5: CalBlackLevelData 多余零初始化 🟡

#### 问题分析

**位置**: `BlackLevel.cs` 第 183-188 行

**当前代码**:
```csharp
for (int i = 0; i < ptrArray.Length; i++)
{
    ptrArray[i] = Marshal.AllocHGlobal(arrayLength * sizeof(short));
    Marshal.Copy(new byte[arrayLength * sizeof(short)], 0, ptrArray[i], arrayLength * sizeof(short));
    // ⚠️ 零初始化: 分配内存后又写入全零
}
```

**问题**:
1. `AllocHGlobal` 分配的内存本就是未初始化的
2. `BlcCal` 会**完全覆盖**这些内存 (逐像素写入)
3. 零初始化操作完全无用，且浪费时间

**性能影响**:
- 对于 1920x1080 分辨率:
  - `arrayLength = 1920 × 1080 / 4 = 518,400`
  - 5 个指针 × 518,400 × 2 字节 = **5.2 MB** 无用写入
  - 约浪费 10-20ms

#### 修复方案

**直接移除零初始化代码**:

```csharp
for (int i = 0; i < ptrArray.Length; i++)
{
    ptrArray[i] = Marshal.AllocHGlobal(arrayLength * sizeof(short));
    // 移除 Marshal.Copy 零初始化 (BlcCal 会覆盖所有数据)
}
```

#### 推荐实施

修改 `BlackLevel.cs` 第 183-188 行:

```csharp
// 修改前
for (int i = 0; i < ptrArray.Length; i++)
{
    ptrArray[i] = Marshal.AllocHGlobal(arrayLength * sizeof(short));
    Marshal.Copy(new byte[arrayLength * sizeof(short)], 0, ptrArray[i], arrayLength * sizeof(short));
}

// 修改后
for (int i = 0; i < ptrArray.Length; i++)
{
    ptrArray[i] = Marshal.AllocHGlobal(arrayLength * sizeof(short));
}
```

---

### B6: 窗口关闭未调用 Cleanup 🟡

#### 问题分析

**位置**: `BlcWindow.xaml.cs` 第 21-26 行

**当前代码**:
```csharp
public partial class BlcWindow : Window
{
    public BlcWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var viewModel = (BlcWindowViewModel)DataContext;
        viewModel.OpenRawFileCommand.Execute(null);
    }
    // ⚠️ 没有 Window_Closing 或 Window_Closed 事件处理
}
```

**ViewModel 已有 Cleanup 实现** (`BlcWindowViewModel.cs` 第 305-317 行):
```csharp
public override void Cleanup()
{
    if (_isCleanedUp) return;

    _ispProcessor = null;
    _blackLevelData = null;
    _blackLevelDataArrays = null;
    _nativeRawFileBuffer = null;  // 释放 RAW 文件缓冲 (可能很大)
    _medianValues.Clear();
    _avgValues.Clear();

    _isCleanedUp = true;
}
```

**问题**:
- 窗口关闭时 ViewModel 持有大量内存未释放
- `_nativeRawFileBuffer` 可能占用数 MB (RAW 文件)
- `_blackLevelDataArrays` 同样占用数 MB
- 频繁打开/关闭窗口会导致内存累积

#### 修复方案

**添加窗口关闭事件处理**:

```csharp
public partial class BlcWindow : Window
{
    public BlcWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var viewModel = (BlcWindowViewModel)DataContext;
        viewModel.OpenRawFileCommand.Execute(null);
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        var viewModel = (BlcWindowViewModel)DataContext;
        viewModel?.Cleanup();
    }
}
```

**XAML 中添加事件绑定** (`BlcWindow.xaml`):

```xml
<Window x:Class="ThunderSE.Ui.SettingWindow.Blc.BlcWindow"
        ...
        Closed="Window_Closed">
    <!-- 现有内容 -->
</Window>
```

#### 推荐实施

1. 修改 `BlcWindow.xaml.cs`:

```csharp
// 在 Window_Loaded 方法后添加
private void Window_Closed(object sender, EventArgs e)
{
    var viewModel = (BlcWindowViewModel)DataContext;
    viewModel?.Cleanup();
}
```

2. 修改 `BlcWindow.xaml`，在 Window 标签添加 `Closed` 事件:

```xml
<Window ... Closed="Window_Closed">
```

---

### B9: GetMedianPixelValue 跳过负值 🟢

#### 问题分析

**位置**: `BlcWindowViewModel.cs` 第 284-309 行

**当前代码**:
```csharp
private short GetMedianPixelValue(short[] pixelValueArray)
{
    int[] histogram = new int[1024];

    foreach (short val in pixelValueArray)
    {
        if (val >= 0 && val < 1024)  // ⚠️ 跳过负值
            histogram[val]++;
    }
    // ...
}
```

**问题场景**:
1. 原始数据中存在负值 (异常情况)
2. 负值被跳过，导致统计的总像素数减少
3. 中值计算基于不完整的样本集，结果偏差

**正常情况**: BLC 通道分离后的像素值应为 0-1023 (10-bit RAW)
**异常情况**: 如果 C++ 端 bug 或其他原因导致负值，应被记录或处理

#### 修复方案

**方案 A: 包含负值处理 (推荐)** ✅

扩展直方图范围，包含可能的负值:

```csharp
private short GetMedianPixelValue(short[] pixelValueArray)
{
    // 扩展直方图范围: 支持 [-512, 1023] 覆盖校正值范围
    const int minVal = -512;
    const int maxVal = 1023;
    const int range = maxVal - minVal + 1;
    
    int[] histogram = new int[range];
    int validCount = 0;

    foreach (short val in pixelValueArray)
    {
        if (val >= minVal && val <= maxVal)
        {
            histogram[val - minVal]++;  // 偏移索引
            validCount++;
        }
    }

    if (validCount == 0)
        return 0;  // 无有效数据，返回 0

    int medianIndex1 = (validCount - 1) / 2;
    int medianIndex2 = validCount / 2;
    int currentIndex = -1;
    int medianVal1 = 0, medianVal2 = 0;

    for (int i = 0; i < range; i++)
    {
        currentIndex += histogram[i];

        if (medianVal1 == 0 && currentIndex >= medianIndex1)
            medianVal1 = i + minVal;  // 还原实际值

        if (currentIndex >= medianIndex2)
        {
            medianVal2 = i + minVal;
            break;
        }
    }

    return (short)((medianVal1 + medianVal2) / 2);
}
```

**方案 B: 保持现状，添加警告日志**

```csharp
private short GetMedianPixelValue(short[] pixelValueArray)
{
    int[] histogram = new int[1024];
    int skippedCount = 0;

    foreach (short val in pixelValueArray)
    {
        if (val >= 0 && val < 1024)
            histogram[val]++;
        else
            skippedCount++;  // 记录跳过的异常值
    }

    if (skippedCount > 0)
    {
        // 可选: 记录日志或抛出警告
        // Debug.WriteLine($"警告: GetMedianPixelValue 跳过 {skippedCount} 个异常值");
    }

    // ... 后续逻辑不变
}
```

#### 推荐实施

采用 **方案 A** (更健壮):

修改 `BlcWindowViewModel.cs` 第 284-309 行:

```csharp
// 修改前
private short GetMedianPixelValue(short[] pixelValueArray)
{
    int[] histogram = new int[1024];

    foreach (short val in pixelValueArray)
    {
        if (val >= 0 && val < 1024)
            histogram[val]++;
    }

    int count = pixelValueArray.Length;
    int medianIndex1 = (count - 1) / 2;
    int medianIndex2 = count / 2;
    int currentIndex = -1;
    int medianVal1 = 0, medianVal2 = 0;

    for (int i = 0; i < 1024; i++)
    {
        currentIndex += histogram[i];

        if (medianVal1 == 0 && currentIndex >= medianIndex1)
            medianVal1 = i;

        if (currentIndex >= medianIndex2)
        {
            medianVal2 = i;
            break;
        }
    }

    return (short)((medianVal1 + medianVal2) / 2);
}

// 修改后
private short GetMedianPixelValue(short[] pixelValueArray)
{
    // 扩展直方图范围: 支持 [-512, 1023] 覆盖可能的异常值
    const int minVal = -512;
    const int maxVal = 1023;
    const int range = maxVal - minVal + 1;
    
    int[] histogram = new int[range];
    int validCount = 0;

    foreach (short val in pixelValueArray)
    {
        if (val >= minVal && val <= maxVal)
        {
            histogram[val - minVal]++;  // 偏移索引
            validCount++;
        }
    }

    if (validCount == 0)
        return 0;  // 无有效数据

    int medianIndex1 = (validCount - 1) / 2;
    int medianIndex2 = validCount / 2;
    int currentIndex = -1;
    int medianVal1 = 0, medianVal2 = 0;

    for (int i = 0; i < range; i++)
    {
        currentIndex += histogram[i];

        if (medianVal1 == 0 && currentIndex >= medianIndex1)
            medianVal1 = i + minVal;  // 还原实际值

        if (currentIndex >= medianIndex2)
        {
            medianVal2 = i + minVal;
            break;
        }
    }

    return (short)((medianVal1 + medianVal2) / 2);
}
```

---

## 修复优先级

### 第一批 (立即修复 - 高严重性)

| 编号 | 问题 | 预估工作量 | 风险 |
|------|------|-----------|------|
| B1 | BlcImg 宽高参数颠倒 | 5 分钟 | 低 (仅改签名) |
| B2 | DeserializeFromXmlElement null 检查 | 10 分钟 | 低 (仅添加检查) |
| B3 | CorrectValuesArray 返回副本 | 5 分钟 | 低 (仅改 getter) |

### 第二批 (短期修复 - 中严重性)

| 编号 | 问题 | 预估工作量 | 风险 |
|------|------|-----------|------|
| B4 | PropertyChanged 属性名 | 5 分钟 | 低 (添加通知) |
| B5 | 移除零初始化 | 5 分钟 | 极低 (删除代码) |
| B6 | 窗口关闭 Cleanup | 10 分钟 | 低 (添加事件) |

### 第三批 (长期优化 - 低严重性)

| 编号 | 问题 | 预估工作量 | 风险 |
|------|------|-----------|------|
| B9 | 中值计算负值处理 | 15 分钟 | 低 (扩展范围) |

**总预估工作量**: 约 1 小时

---

## 测试建议

### B1 测试

```csharp
// 测试非正方形分辨率
BlackLevel blc = new BlackLevel();
blc.SetCommonConfig(new CommonConfig { ResolutionWidth = 1920, ResolutionHeight = 1080 });

byte[] testBuffer = new byte[1920 * 1080 * 2];
// 填充测试数据...

blc.ProcessRawBuffer(ref testBuffer);
// 验证: 图像不应出现行列错位
```

### B2 测试

```csharp
// 测试缺失节点
BlackLevel blc = new BlackLevel();
XmlDocument doc = new XmlDocument();
XmlElement root = doc.CreateElement("Root");
// 不添加 <Blc> 节点

blc.DeserializeFromXmlElement(root);
// 验证: 不抛异常，值为默认 0
```

### B3 测试

```csharp
BlackLevel blc = new BlackLevel();
blc.R = 64;

short[] values = blc.CorrectValuesArray;
values[0] = 100;  // 修改返回的数组

short[] values2 = blc.CorrectValuesArray;
// 验证: values2[0] 仍为 64 (不受影响)
```

---

## 总结

本文档针对 BLC 模块的 7 个已知问题提供了详细的修复方案和代码示例。所有修复均为**低风险改动**，不涉及算法逻辑变更，主要聚焦于:

1. **接口修正** (B1): 确保 P/Invoke 签名与 C++ 端一致
2. **防御性编程** (B2, B3, B9): 添加 null 检查、返回副本、扩展数据范围
3. **资源管理** (B5, B6): 移除无用操作、及时释放内存
4. **事件通知** (B4): 确保 UI 绑定正确接收通知

建议按优先级分三批实施，总工作量约 1 小时。
