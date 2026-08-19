# BLC 模块深度代码分析与优化建议

## 概述

本文档对 BLC (Black Level Correction) 模块进行了全面深入的代码审查，涵盖 C# 数据模型层、UI 层、ViewModel 层以及 C++ 算法层。分析发现了多个需要优化的问题，包括**严重 bug、内存泄漏、性能瓶颈、代码质量缺陷**等。

---

## 一、严重问题 (Critical - 必须修复)

### 问题 1: ApplyBlackLevelCorrection 方法存在严重的逻辑错误

**文件**: `ThunderSE\DeviceConfig\Isp\BlackLevel.cs:116-128`

**问题描述**:

```csharp
public void ApplyBlackLevelCorrection(short[] correctValues, bool isMinus = true)
{
    short[] outputBuffer = new short[_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight];
    Array.Clear(outputBuffer, 0, outputBuffer.Length);  // ❌ 分配但从未使用

    byte[] outputByteBuffer = new byte[Buffer.ByteLength(outputBuffer)];  // ❌ 分配但从未使用

    _correctValuesArray = correctValues;  // ❌ 直接引用外部数组，存在被修改风险
    if (isMinus)
    {
        _correctValuesArray = correctValues.Select(x => x = (short)-x).ToArray();  // ❌ x = (short)-x 是错误的赋值
    }

    if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs("CorrectValuesArray"));
}
```

**严重问题**:

1. **内存浪费**：`outputBuffer` 和 `outputByteBuffer` 被分配但从未使用，纯粹浪费内存
2. **赋值错误**：`x => x = (short)-x` 这个 LINQ 表达式语义混乱
   - `x = (short)-x` 是赋值表达式，返回值是赋值后的结果
   - 但 `x` 是 lambda 参数，赋值给它没有意义
   - 正确写法应该是 `x => (short)-x`
3. **数组引用问题**：`_correctValuesArray = correctValues` 直接引用外部数组，如果外部修改数组，会影响内部状态

**影响**：
- 每次调用泄漏约 `2 * width * height` 字节内存
- 取负操作虽然最终结果正确（因为 `ToArray()` 创建了副本），但代码语义错误，容易误导维护者

**建议修复**:

```csharp
public void ApplyBlackLevelCorrection(short[] correctValues, bool isMinus = true)
{
    if (correctValues == null || correctValues.Length != 4)
        throw new ArgumentException("校正值数组必须包含4个元素");

    if (isMinus)
    {
        // 正确写法：直接取负
        _correctValuesArray = new short[4];
        for (int i = 0; i < 4; i++)
        {
            _correctValuesArray[i] = (short)-correctValues[i];
        }
    }
    else
    {
        // 创建副本，避免外部修改影响内部状态
        _correctValuesArray = (short[])correctValues.Clone();
    }

    HasChangedParams = true;
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CorrectValuesArray"));
}
```

---

### 问题 2: ProcessRawBuffer 和 ApplyBlackLevelCorrection 存在内存泄漏

**文件**: `ThunderSE\DeviceConfig\Isp\BlackLevel.cs:130-145`

**问题描述**:

```csharp
public override void ProcessRawBuffer(ref byte[] imgBuffer)
{
    short[] outputBuffer = new short[_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight];
    Array.Clear(outputBuffer, 0, outputBuffer.Length);  // ❌ 多余

    byte[] outputByteBuffer = new byte[Buffer.ByteLength(outputBuffer)];

    IspApi.BlcImg(imgBuffer, _correctValuesArray, (int)_commonConfig.Bayer,
        _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, outputBuffer);
    Buffer.BlockCopy(outputBuffer, 0, outputByteBuffer, 0, outputByteBuffer.Length);

    imgBuffer = outputByteBuffer;
}
```

**问题**：
1. `Array.Clear()` 是多余的，`BlcImg()` 会覆盖所有输出像素
2. 与 `ApplyBlackLevelCorrection()` 一样，分配了两个缓冲区但只返回一个

**建议修复**:

```csharp
public override void ProcessRawBuffer(ref byte[] imgBuffer)
{
    if (imgBuffer == null)
        throw new ArgumentNullException(nameof(imgBuffer));
    if (_commonConfig == null)
        throw new InvalidOperationException("CommonConfig not initialized");

    int pixelCount = _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight;
    short[] outputBuffer = new short[pixelCount];  // 移除 Array.Clear

    IspApi.BlcImg(imgBuffer, _correctValuesArray, (int)_commonConfig.Bayer,
        _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, outputBuffer);

    // 直接转换，避免中间变量
    imgBuffer = new byte[pixelCount * sizeof(short)];
    Buffer.BlockCopy(outputBuffer, 0, imgBuffer, 0, imgBuffer.Length);
}
```

---

### 问题 3: CalBlackLevelData 使用 Marshal.AllocHGlobal 但应该用 MemoryManager

**文件**: `ThunderSE\DeviceConfig\Isp\BlackLevel.cs:147-177`

**问题描述**:

```csharp
public void CalBlackLevelData(byte[] nativeRawFileBuffer, Dictionary<BlackLevelPixelType, short[]> blackLevelDataArrays)
{
    IntPtr[] ptrArray = null;
    try
    {
        ptrArray = new IntPtr[5];
        var arrayLength = _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight / 4;

        for (int i = 0; i < ptrArray.Length; i++)
        {
            ptrArray[i] = Marshal.AllocHGlobal(arrayLength * sizeof(short));  // ❌ 直接 AllocHGlobal
            Marshal.Copy(new byte[arrayLength * sizeof(short)], 0, ptrArray[i], arrayLength * sizeof(short));  // ❌ 多余清零
        }

        IspApi.BlcCal(nativeRawFileBuffer, _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight,
            (int)_commonConfig.Bayer, ptrArray);

        // ... 复制数据

    }
    finally
    {
        if (ptrArray != null)
            for (int i = 0; i < ptrArray.Length; i++)
            {
                Marshal.FreeHGlobal(ptrArray[i]);  // ✅ 有清理，但与项目其他模块不一致
            }
    }
}
```

**问题**：
1. 项目其他模块（如 LSC）已统一使用 `MemoryManager` 管理非托管内存
2. `Marshal.Copy(new byte[...], ...)` 清零是多余的，`BlcCal()` 会覆盖所有输出
3. 分配 5 个指针但只用了 4 个（`ptrArray[4]` 未使用）

**建议修复**:

```csharp
public void CalBlackLevelData(byte[] nativeRawFileBuffer, Dictionary<BlackLevelPixelType, short[]> blackLevelDataArrays)
{
    if (nativeRawFileBuffer == null)
        throw new ArgumentNullException(nameof(nativeRawFileBuffer));
    if (blackLevelDataArrays == null)
        throw new ArgumentNullException(nameof(blackLevelDataArrays));
    if (_commonConfig == null)
        throw new InvalidOperationException("CommonConfig not initialized");

    using (var memoryManager = new MemoryManager())  // 使用 MemoryManager 统一管理
    {
        IntPtr[] ptrArray = new IntPtr[4];  // 只需 4 个
        int arrayLength = _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight / 4;
        int bufferSize = arrayLength * sizeof(short);

        for (int i = 0; i < ptrArray.Length; i++)
        {
            ptrArray[i] = memoryManager.AllocateMemory(bufferSize);
            // 移除 Marshal.Copy - BlcCal 会覆盖输出
        }

        IspApi.BlcCal(nativeRawFileBuffer, _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight,
            (int)_commonConfig.Bayer, ptrArray);

        Marshal.Copy(ptrArray[(int)BlackLevelPixelType.R], blackLevelDataArrays[BlackLevelPixelType.R], 0, arrayLength);
        Marshal.Copy(ptrArray[(int)BlackLevelPixelType.Gr], blackLevelDataArrays[BlackLevelPixelType.Gr], 0, arrayLength);
        Marshal.Copy(ptrArray[(int)BlackLevelPixelType.Gb], blackLevelDataArrays[BlackLevelPixelType.Gb], 0, arrayLength);
        Marshal.Copy(ptrArray[(int)BlackLevelPixelType.B], blackLevelDataArrays[BlackLevelPixelType.B], 0, arrayLength);
    }  // using 结束，自动释放所有内存
}
```

---

### 问题 4: BlcImg 中循环内重复计算 data_adj[4] 是性能浪费

**文件**: `IspApi\source\IQ.cpp:97-100`

**问题描述**:

```cpp
for (unsigned int i = 0; i < h; i++) {
    for (unsigned int j = 0; j < w; j++) {
        // ❌ 每次循环都计算 4 个 data_adj，但只使用其中 1 个
        data_adj[0] = raw_img[i*w + j] + blackl_r;
        data_adj[1] = raw_img[i*w + j] + blackl_gr;
        data_adj[2] = raw_img[i*w + j] + blackl_gb;
        data_adj[3] = raw_img[i*w + j] + blackl_b;

        switch (polarity) {
        case 0:
            if ((i & 1) == 0 && (j & 1) == 0)
                blc_img[i*w + j] = CLIP_PIXEL(data_adj[0], 0, HIGH_VAL_10BIT);  // 只用 data_adj[0]
            else if ((i & 1) == 0 && (j & 1) == 1)
                blc_img[i*w + j] = CLIP_PIXEL(data_adj[1], 0, HIGH_VAL_10BIT);  // 只用 data_adj[1]
            // ...
        }
    }
}
```

**性能问题**：
- 每个像素计算 4 次加法，但只使用 1 次
- 对于 1920x1080 图像，浪费约 **620 万次**加法运算

**优化建议**:

```cpp
for (unsigned int i = 0; i < h; i++) {
    // 提前判断行奇偶
    bool is_even_row = (i & 1) == 0;
    
    for (unsigned int j = 0; j < w; j++) {
        short pixel = raw_img[i*w + j];
        bool is_even_col = (j & 1) == 0;
        
        // 根据极性和位置直接计算对应的校正值
        short corrected;
        switch (polarity) {
        case 0:  // RG/GB
            if (is_even_row && is_even_col)
                corrected = pixel + blackl_r;
            else if (is_even_row && !is_even_col)
                corrected = pixel + blackl_gr;
            else if (!is_even_row && is_even_col)
                corrected = pixel + blackl_gb;
            else
                corrected = pixel + blackl_b;
            break;
        case 1:  // GR/BG
            if (is_even_row && is_even_col)
                corrected = pixel + blackl_gr;
            else if (is_even_row && !is_even_col)
                corrected = pixel + blackl_r;
            else if (!is_even_row && is_even_col)
                corrected = pixel + blackl_b;
            else
                corrected = pixel + blackl_gb;
            break;
        case 2:  // BG/GR
            if (is_even_row && is_even_col)
                corrected = pixel + blackl_b;
            else if (is_even_row && !is_even_col)
                corrected = pixel + blackl_gb;
            else if (!is_even_row && is_even_col)
                corrected = pixel + blackl_gr;
            else
                corrected = pixel + blackl_r;
            break;
        case 3:  // GB/RG
            if (is_even_row && is_even_col)
                corrected = pixel + blackl_gb;
            else if (is_even_row && !is_even_col)
                corrected = pixel + blackl_b;
            else if (!is_even_row && is_even_col)
                corrected = pixel + blackl_r;
            else
                corrected = pixel + blackl_gr;
            break;
        default:
            printf("Unknown BLC error!");
            return;
        }
        
        blc_img[i*w + j] = CLIP_PIXEL(corrected, 0, HIGH_VAL_10BIT);
    }
}
```

**性能提升**：减少 **75%** 的加法运算（从 4 次/像素降至 1 次/像素）

---

## 二、代码质量问题 (Code Quality - 强烈建议改进)

### 问题 5: BlcWindowViewModel 存在大量重复代码

**文件**: `ThunderSE\Ui\SettingWindow\Blc\BlcWindowViewModel.cs:94-143, 147-205`

**问题描述**:

四个通道的 PixelData 属性几乎完全相同：

```csharp
public Dictionary<int, int> RPixelData
{
    get
    {
        var pixelDictionary = new Dictionary<int, int>();
        foreach (var group in _blackLevelDataArrays[BlackLevelPixelType.R].GroupBy(i => i))
        {
            pixelDictionary[group.Key] = group.Count();
        }
        return pixelDictionary;
    }
}

public Dictionary<int, int> GRPixelData
{
    get
    {
        var pixelDictionary = new Dictionary<int, int>();
        foreach (var group in _blackLevelDataArrays[BlackLevelPixelType.Gr].GroupBy(i => i))
        {
            pixelDictionary[group.Key] = group.Count();
        }
        return pixelDictionary;
    }
}
// ... GBPixelData 和 BPixelData 类似
```

平均值和中值属性也存在同样问题（各 4 个几乎相同的属性）。

**建议重构**:

```csharp
// 使用统一的泛型方法
private Dictionary<int, int> GetPixelData(BlackLevelPixelType type)
{
    var pixelDictionary = new Dictionary<int, int>();
    foreach (var group in _blackLevelDataArrays[type].GroupBy(i => i))
    {
        pixelDictionary[group.Key] = group.Count();
    }
    return pixelDictionary;
}

public Dictionary<int, int> RPixelData => GetPixelData(BlackLevelPixelType.R);
public Dictionary<int, int> GRPixelData => GetPixelData(BlackLevelPixelType.Gr);
public Dictionary<int, int> GBPixelData => GetPixelData(BlackLevelPixelType.Gb);
public Dictionary<int, int> BPixelData => GetPixelData(BlackLevelPixelType.B);

// 平均值和中值也可以使用类似模式
private int GetAvgValue(string key)
{
    int val = 0;
    _avgValues.TryGetValue(key, out val);
    return val;
}

public int AvgBlackLevelR => GetAvgValue("AvgBlackLevelR");
public int AvgBlackLevelGR => GetAvgValue("AvgBlackLevelGR");
// ...
```

---

### 问题 6: ApplyCorrection 方法在 Task.Run 中修改局部变量

**文件**: `ThunderSE\Ui\SettingWindow\Blc\BlcWindowViewModel.cs:276-290`

**问题描述**:

```csharp
private void ApplyCorrection()
{
    // ... 设置校正值

    byte[] correctingRawBuffer = new byte[_nativeRawFileBuffer.Length];
    Buffer.BlockCopy(_nativeRawFileBuffer, 0, correctingRawBuffer, 0, _nativeRawFileBuffer.Length);
    Task.Run(() => {
        _blackLevelData.ProcessRawBuffer(ref correctingRawBuffer);  // ❌ ref 参数在 lambda 中
        CalBlackLevelData(correctingRawBuffer);  // ❌ 处理后的数据没有通知 UI 更新
    });
}
```

**问题**：
1. `ProcessRawBuffer(ref correctingRawBuffer)` 修改了 `correctingRawBuffer` 的引用（指向新分配的数组）
2. 但这个修改在 `Task.Run` 内部，外部无法感知
3. `CalBlackLevelData(correctingRawBuffer)` 使用的是**校正后的数据**，但没有触发 UI 重新绑定图表数据

**建议修复**:

```csharp
private void ApplyCorrection()
{
    if (_nativeRawFileBuffer == null)
    {
        MessageBox.Show("请先加载 RAW 文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    short[] correctionValueArray;
    switch (_correctionForm)
    {
        case CorrectionForm.Median:
            correctionValueArray = new short[] { 
                (short)MedianBlackLevelR, (short)MedianBlackLevelGR,
                (short)MedianBlackLevelGB, (short)MedianBlackLevelB 
            };
            break;
        case CorrectionForm.Average:
            correctionValueArray = new short[] { 
                (short)AvgBlackLevelR, (short)AvgBlackLevelGR,
                (short)AvgBlackLevelGB, (short)AvgBlackLevelB 
            };
            break;
        default:
            return;
    }

    // 应用校正值
    _blackLevelData.ApplyBlackLevelCorrection(correctionValueArray);

    // 异步处理并更新 UI
    Task.Run(() => {
        byte[] correctingRawBuffer = (byte[])_nativeRawFileBuffer.Clone();
        _blackLevelData.ProcessRawBuffer(ref correctingRawBuffer);
        
        // 在 UI 线程更新图表
        Application.Current.Dispatcher.Invoke(() => {
            CalBlackLevelData(correctingRawBuffer);
        });
    });
}
```

---

### 问题 7: GetMedianPixelValue 使用排序算法效率低

**文件**: `ThunderSE\Ui\SettingWindow\Blc\BlcWindowViewModel.cs:292-310`

**问题描述**:

```csharp
private short GetMedianPixelValue(IEnumerable<short> PixelValueArray)
{
    short[] temp = PixelValueArray.ToArray();
    Array.Sort(temp);  // ❌ O(n log n) 排序，但只需要中位数

    int count = temp.Length;
    if (count == 0)
        throw new InvalidOperationException("Empty collection");
    else if (count % 2 == 0)
        return (short)((temp[count / 2 - 1] + temp[count / 2]) / 2);
    else
        return temp[count / 2];
}
```

**性能问题**：
- 对于 1920x1080 图像，每个通道约 50 万像素
- `Array.Sort()` 需要 O(n log n) ≈ 1000 万次比较
- 实际上只需要中位数，可以使用 QuickSelect 算法 O(n)

**优化建议**:

对于实际应用场景，BLC 校准图像通常是镜头盖住的全黑图像，像素值分布集中，可以使用**计数排序**思想：

```csharp
private short GetMedianPixelValue(short[] pixelValueArray)
{
    // 对于 10-bit 图像，像素范围 0-1023
    // 使用计数数组（桶）来快速找到中位数
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
```

**性能提升**：
- 从 O(n log n) 降至 O(n + k)，其中 k=1024 是像素值范围
- 对于 50 万像素，约 **10-20 倍加速**

---

### 问题 8: RPixelData 等属性每次都创建新 Dictionary

**文件**: `ThunderSE\Ui\SettingWindow\Blc\BlcWindowViewModel.cs:94-143`

**问题描述**:

```csharp
public Dictionary<int, int> RPixelData
{
    get
    {
        var pixelDictionary = new Dictionary<int, int>();  // ❌ 每次 getter 调用都创建新对象
        foreach (var group in _blackLevelDataArrays[BlackLevelPixelType.R].GroupBy(i => i))
        {
            pixelDictionary[group.Key] = group.Count();
        }
        return pixelDictionary;
    }
}
```

**性能问题**：
- WPF 数据绑定可能多次调用 getter
- 每次调用都遍历整个数组（50 万+ 元素）并创建新 Dictionary
- 应该在数据变化时计算一次，然后缓存结果

**建议修复**:

```csharp
private Dictionary<int, int> _rPixelData;
private Dictionary<int, int> _grPixelData;
private Dictionary<int, int> _gbPixelData;
private Dictionary<int, int> _bPixelData;

public Dictionary<int, int> RPixelData => _rPixelData;
public Dictionary<int, int> GRPixelData => _grPixelData;
public Dictionary<int, int> GBPixelData => _gbPixelData;
public Dictionary<int, int> BPixelData => _bPixelData;

private void UpdatePixelData()
{
    _rPixelData = BuildPixelData(BlackLevelPixelType.R);
    _grPixelData = BuildPixelData(BlackLevelPixelType.Gr);
    _gbPixelData = BuildPixelData(BlackLevelPixelType.Gb);
    _bPixelData = BuildPixelData(BlackLevelPixelType.B);

    RaisePropertyChanged("RPixelData");
    RaisePropertyChanged("GRPixelData");
    RaisePropertyChanged("GBPixelData");
    RaisePropertyChanged("BPixelData");
}

private Dictionary<int, int> BuildPixelData(BlackLevelPixelType type)
{
    var pixelDictionary = new Dictionary<int, int>();
    foreach (var group in _blackLevelDataArrays[type].GroupBy(i => i))
    {
        pixelDictionary[group.Key] = group.Count();
    }
    return pixelDictionary;
}
```

在 `CalBlackLevelData()` 中调用 `UpdatePixelData()`。

---

### 问题 9: DeserializeFromXmlElement 缺少空值检查

**文件**: `ThunderSE\DeviceConfig\Isp\BlackLevel.cs:238-244`

**问题描述**:

```csharp
public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
{
    var blcNode = ispToolDataNode["Blc"];  // ❌ 可能为 null

    R = XmlHelper.GetNodeShort(blcNode, "BlcR");  // ❌ blcNode 为 null 时会崩溃
    Gr = XmlHelper.GetNodeShort(blcNode, "BlcGr");
    Gb = XmlHelper.GetNodeShort(blcNode, "BlcGb");
    B = XmlHelper.GetNodeShort(blcNode, "BlcB");
}
```

**风险**：如果 XML 中不存在 `<Blc>` 节点，会抛出 `NullReferenceException`

**建议修复**:

```csharp
public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
{
    var blcNode = ispToolDataNode?["Blc"];
    if (blcNode == null)
    {
        Console.WriteLine("[BLC] 警告: XML 中不存在 <Blc> 节点，使用默认值 0");
        _correctValuesArray = new short[4];  // 默认全 0
        return;
    }

    R = XmlHelper.GetNodeShort(blcNode, "BlcR");
    Gr = XmlHelper.GetNodeShort(blcNode, "BlcGr");
    Gb = XmlHelper.GetNodeShort(blcNode, "BlcGb");
    B = XmlHelper.GetNodeShort(blcNode, "BlcB");
}
```

---

### 问题 10: ParamsDataCollection 使用 Marshal 但应简化

**文件**: `ThunderSE\DeviceConfig\Isp\BlackLevel.cs:179-214`

**问题描述**:

```csharp
public override Dictionary<int, byte[]> ParamsDataCollection
{
    get
    {
        BlcParams blcParams = new BlcParams()
        {
            blkl_r = R,
            blkl_gr = Gr,
            blkl_gb = Gb,
            blkl_b = B
        };

        int size = Marshal.SizeOf(blcParams);
        byte[] arr = new byte[size];

        IntPtr ptr = Marshal.AllocHGlobal(size);  // ❌ 手动 Alloc/Free
        Marshal.StructureToPtr(blcParams, ptr, true);
        Marshal.Copy(ptr, arr, 0, size);
        Marshal.FreeHGlobal(ptr);

        return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
    }
    set
    {
        BlcParams blcParams = new BlcParams();
        int size = Marshal.SizeOf(blcParams);
        IntPtr ptr = Marshal.AllocHGlobal(size);  // ❌ 手动 Alloc/Free

        Marshal.Copy(value[DeviceModulePos], 0, ptr, size);
        blcParams = (BlcParams)Marshal.PtrToStructure(ptr, blcParams.GetType());
        Marshal.FreeHGlobal(ptr);

        R = (short)blcParams.blkl_r;
        // ...
    }
}
```

**问题**：
1. 使用 `Marshal.AllocHGlobal` / `FreeHGlobal` 手动管理内存，容易泄漏
2. `BlcParams` 结构体使用 `int` 但外部使用 `short`，类型不一致
3. 如果 `Marshal.StructureToPtr` 抛出异常，`FreeHGlobal` 不会被调用

**建议修复**:

```csharp
[StructLayout(LayoutKind.Sequential)]
private struct BlcParams
{
    public short blkl_r;    // 改为 short 与外部一致
    public short blkl_gr;
    public short blkl_gb;
    public short blkl_b;
}

public override Dictionary<int, byte[]> ParamsDataCollection
{
    get
    {
        BlcParams blcParams = new BlcParams
        {
            blkl_r = R,
            blkl_gr = Gr,
            blkl_gb = Gb,
            blkl_b = B
        };

        int size = Marshal.SizeOf(blcParams);
        byte[] arr = new byte[size];
        
        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(blcParams, ptr, false);
            Marshal.Copy(ptr, arr, 0, size);
        }
        finally
        {
            if (ptr != IntPtr.Zero)
                Marshal.FreeHGlobal(ptr);
        }

        return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
    }
    set
    {
        if (value == null || !value.ContainsKey(DeviceModulePos))
            throw new ArgumentException("ParamsDataCollection 数据缺失");

        byte[] data = value[DeviceModulePos];
        int expectedSize = Marshal.SizeOf(typeof(BlcParams));
        
        if (data.Length != expectedSize)
            throw new ArgumentException($"数据尺寸不匹配: 期望 {expectedSize}，实际 {data.Length}");

        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.AllocHGlobal(expectedSize);
            Marshal.Copy(data, 0, ptr, expectedSize);
            BlcParams blcParams = (BlcParams)Marshal.PtrToStructure(ptr, typeof(BlcParams));
            
            R = blcParams.blkl_r;
            Gr = blcParams.blkl_gr;
            Gb = blcParams.blkl_gb;
            B = blcParams.blkl_b;
        }
        finally
        {
            if (ptr != IntPtr.Zero)
                Marshal.FreeHGlobal(ptr);
        }
    }
}
```

---

## 三、潜在风险 (Potential Risks - 需要注意)

### 问题 11: BlcWindow 加载时自动弹出文件对话框

**文件**: `ThunderSE\Ui\SettingWindow\Blc\BlcWindow.xaml.cs:22-26`

**问题描述**:

```csharp
private void Window_Loaded(object sender, RoutedEventArgs e)
{
    var viewModel = (BlcWindowViewModel)DataContext;
    viewModel.OpenRawFileCommand.Execute(null);  // ❌ 窗口加载就弹窗
}
```

**问题**：
- 窗口一打开就弹出文件对话框，用户无法选择取消
- 如果用户关闭对话框，窗口会保持空白状态
- 用户体验不佳

**建议修复**:

```csharp
private void Window_Loaded(object sender, RoutedEventArgs e)
{
    // 移除自动弹窗，让用户手动点击按钮打开文件
    // 或者至少检查是否有已加载的文件
    var viewModel = (BlcWindowViewModel)DataContext;
    if (!viewModel.HasLoadedRawFile)  // 需要添加这个属性
    {
        // 可选：显示提示消息
        MessageBox.Show("请加载镜头盖住的 RAW 文件以进行黑电平校准。", 
                       "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
```

---

### 问题 12: _nativeRawFileBuffer 可能为 null 时使用

**文件**: `ThunderSE\Ui\SettingWindow\Blc\BlcWindowViewModel.cs:276`

**问题描述**:

```csharp
private void ApplyCorrection()
{
    // ...
    byte[] correctingRawBuffer = new byte[_nativeRawFileBuffer.Length];  // ❌ 可能为 null
    Buffer.BlockCopy(_nativeRawFileBuffer, 0, correctingRawBuffer, 0, _nativeRawFileBuffer.Length);
    // ...
}
```

如果用户先点击"应用"但没有加载过 RAW 文件，会抛出 `NullReferenceException`

**建议修复**:

```csharp
private void ApplyCorrection()
{
    if (_nativeRawFileBuffer == null)
    {
        MessageBox.Show("请先加载 RAW 文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    // ... 其余代码
}
```

---

### 问题 13: BlcWindowViewModel 没有实现 ICleanup

**文件**: `ThunderSE\Ui\SettingWindow\Blc\BlcWindowViewModel.cs`

**问题描述**:

ViewModel 持有 `_ispProcessor` 和 `_blackLevelData` 的引用，但没有实现 `ICleanup` 接口。如果 Window 关闭时没有正确清理，可能导致内存泄漏。

**建议添加**:

```csharp
class BlcWindowViewModel : ViewModelBase, ICleanup
{
    private bool _isCleanedUp = false;

    public void Cleanup()
    {
        if (_isCleanedUp) return;

        _ispProcessor = null;
        _blackLevelData = null;
        _blackLevelDataArrays = null;
        _nativeRawFileBuffer = null;
        _medianValues.Clear();
        _avgValues.Clear();

        _isCleanedUp = true;
    }
}
```

---

### 问题 14: C++ BlcCal 中 polarity 2/3 的交换逻辑可能导致混淆

**文件**: `IspApi\source\IQ.cpp:64-71`

**问题描述**:

```cpp
if (polarity == 2 || polarity == 3){
    tmp_array = r_array;
    r_array = b_array;  // ❌ 交换指针
    b_array = tmp_array;
    tmp_array = gr_array;
    gr_array = gb_array;  // ❌ 交换指针
    gb_array = tmp_array;
}
```

**问题**：
- 这里交换的是**局部指针变量**，不影响 `out_data` 数组
- 但代码语义不清晰，容易误解为修改了输出数据
- 实际目的是统一输出顺序，但后续代码已经通过 `polarity` 判断处理了

**建议添加注释说明**:

```cpp
// 对于 BG/GR (2) 和 GB/RG (3) 极性，交换局部指针变量
// 这样后续代码可以统一按照 RG/GB 顺序处理输出
// 注意：这里只交换了局部指针，不影响 out_data 指向的实际内存
if (polarity == 2 || polarity == 3){
    tmp_array = r_array;
    r_array = b_array;
    b_array = tmp_array;
    tmp_array = gr_array;
    gr_array = gb_array;
    gb_array = tmp_array;
}
```

---

## 四、优化总结

| 类别 | 问题数 | 优先级 | 预计影响 |
|------|--------|--------|----------|
| 严重 Bug | 4 | 🔴 Critical | 正确性/内存泄漏 |
| 性能问题 | 3 | 🟡 High | 10-75% 加速 |
| 代码质量 | 5 | 🟢 Medium | 可维护性 |
| 潜在风险 | 2 | 🟡 High | 鲁棒性 |

### 优先修复建议排序

1. **立即修复**: 问题 1 (ApplyBlackLevelCorrection 逻辑错误) → 代码语义错误 + 内存泄漏
2. **立即修复**: 问题 2/3 (内存泄漏) → 统一使用 MemoryManager
3. **高优优化**: 问题 4 (BlcImg 重复计算) → 75% 性能提升
4. **高优优化**: 问题 8 (PixelData 缓存) → 避免重复计算
5. **中优改进**: 问题 5/6 (重复代码) → 代码整洁
6. **中优改进**: 问题 7 (中值算法) → 10-20 倍加速
7. **中优改进**: 问题 9/10 (序列化和 Marshal) → 鲁棒性

---

## 五、长期建议

### 1. 添加单元测试

为 BLC 模块编写单元测试，覆盖：
- `BlcCal()` 在 4 种 Bayer 极性下的输出正确性
- `BlcImg()` 对不同校正值的处理
- 序列化/反序列化的完整性
- 边界情况（空图像、极小图像等）

### 2. 性能基准测试

建立 BLC 性能基准：
```
测试场景:
- 1920x1080 RAW 图像
- BlcCal 耗时
- BlcImg 耗时
- 统计计算耗时（平均值/中值）
- 内存峰值
```

### 3. 考虑 SIMD 优化

`BlcImg()` 是逐像素操作，可以使用 SIMD 加速：
- 一次处理 4/8 个像素
- 预计 **3-4 倍性能提升**

### 4. 统一内存管理

项目中混用了 `Marshal.AllocHGlobal` 和 `MemoryManager`，应该统一为 `MemoryManager`，并在代码审查中强制要求。

---

## 结论

BLC 模块核心功能完整，但存在若干**严重 Bug 和性能瓶颈**需要优先修复。修复后预计：
- ✅ **消除内存泄漏** (ApplyBlackLevelCorrection, ProcessRawBuffer)
- ✅ **提升 10-75% 性能** (BlcImg 优化、PixelData 缓存)
- ✅ **改善代码质量** (消除重复代码、统一内存管理)
- ✅ **增强鲁棒性** (空值检查、边界检查)

建议按照优先级逐步推进优化，每步都配合测试验证。
