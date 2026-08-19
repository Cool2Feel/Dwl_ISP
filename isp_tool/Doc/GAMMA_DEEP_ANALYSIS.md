# Gamma 模块深入分析报告

## 1. 概述

Gamma 模块在项目中分为两大独立部分：

### 1.1 ISP YGamma (亮度 Gamma)
- **位置**: `IspModule.YGamma`，枚举索引 7
- **功能**: 对图像进行全局亮度 Gamma 校正，调整图像的非线性亮度映射
- **特点**: 支持 256 点 Gamma 查找表 (LUT)，支持软件图像处理

### 1.2 LCD Gamma (显示 Gamma)
- **位置**: `LcdSection.LcdGamma`
- **功能**: LCD 显示模组的 RGB 三通道 Gamma 调节
- **特点**: 仅提供红、绿、蓝三个通道的 Gamma 索引值

### 核心区别

| 特性 | YGamma (ISP) | LcdGamma (LCD) |
|------|--------------|----------------|
| 作用对象 | 图像亮度通道 | LCD 显示器 RGB 通道 |
| 参数形式 | 256 点查找表 | 3 个索引值 (0-11) |
| 数据处理 | 支持软件处理 (`ProcessRgbBuffer`) | 仅参数配置 |
| UI 调试 | 完整图表调试 + IQ 计算 | 简单滑块调节 |
| 复杂度 | 高 (含曲线编辑、IQ 分析) | 低 (仅参数调节) |

---

## 2. YGamma (ISP 亮度 Gamma) 架构

### 2.1 数据流架构

```
┌─────────────────────────────────────────────────────────────────┐
│                        UI 层                                     │
│  ┌──────────────────────┐    ┌──────────────────────────────┐   │
│  │ YGammaWindow         │    │ LcdGammaArea (用户模式)      │   │
│  │ - 曲线图表 (Chart)   │    │ - 三个滑块 (R/G/B)          │   │
│  │ - 关键点编辑         │    │ - LcdGammaAreaViewModel     │   │
│  │ - 导入/导出          │    │                              │   │
│  │ - 在线/离线 IQ 调试  │    │                              │   │
│  └──────────┬───────────┘    └──────────────┬───────────────┘   │
│             │                                │                   │
│             └────────────────┬───────────────┘                   │
│                              ▼                                   │
│                    YGammaWindowViewModel                         │
│                    LcdGammaAreaViewModel                         │
└──────────────────────────────┬──────────────────────────────────┘
                               ▼
┌──────────────────────────────────────────────────────────────────┐
│                     业务逻辑层                                    │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ YGamma.cs (ProcessStep 子类)                             │   │
│  │ - YGammaTable[256] (Gamma 查找表)                        │   │
│  │ - PadNum (填充参数)                                      │   │
│  │ - LoadYGammaTableFromFile / SaveYGammaTableToFile        │   │
│  │ - ProcessRgbBuffer (软件处理)                            │   │
│  │ - ParamsDataCollection (内存编组)                        │   │
│  └──────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ LcdGamma.cs (LcdSettingSection 子类)                     │   │
│  │ - gamma_red, gamma_green, gamma_blue                     │   │
│  │ - contra_index (对比度索引)                              │   │
│  └──────────────────────────────────────────────────────────┘   │
└──────────────────────────────┬──────────────────────────────────┘
                               ▼
┌──────────────────────────────────────────────────────────────────┐
│                     数据传输层                                    │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ YGammaParams (内部结构体)                                │   │
│  │ - br_mod, gma_num[8], contra_num, bofst, lofst           │   │
│  │ - lcpr_* 参数组                                          │   │
│  │ - pad_num, using_ygama[256]                              │   │
│  │                                                          │   │
│  │ Marshal.StructureToPtr / PtrToStructure                  │   │
│  └──────────────────────────────────────────────────────────┘   │
└──────────────────────────────┬──────────────────────────────────┘
                               ▼
┌──────────────────────────────────────────────────────────────────┐
│                     C++ DLL 层 (IspApi.dll)                      │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ YGammaImg(w, h, pad_num, gamma_table[256], in[], out[]) │   │
│  │ YGAMMA_IQ(gr_avg, gg_avg, gb_avg, num, ...)             │   │
│  └──────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```

### 2.2 类层次结构

```
ProcessStep (抽象基类)
    │
    └── YGamma
         ├── YGammaTable : short[256] (Gamma 查找表)
         ├── PadNum : byte (填充参数)
         ├── 方法:
         │    ├── LoadYGammaTableFromFile(string)
         │    ├── SaveYGammaTableToFile(string)
         │    ├── ProcessRgbBuffer(ref byte[]) (软件处理)
         │    ├── ParamsDataCollection (内存编组)
         │    ├── SerializeToXmlElement
         │    └── DeserializeFromXmlElement
         └── 依赖前置步骤:
              ├── Blc (黑电平校正)
              ├── Lsc (镜头阴影校正)
              └── Awb (自动白平衡)

LcdSettingSection (抽象基类)
    │
    └── LcdGamma
         ├── contra_index : int
         ├── gamma_red : int
         ├── gamma_green : int
         ├── gamma_blue : int
         └── 方法:
              ├── ParamsData (内存编组)
              ├── SerializeToXmlElement
              └── DeserializeFromXmlElement
```

---

## 3. YGamma 核心代码分析

### 3.1 YGamma.cs - 主数据模型类

**文件路径**: `ThunderSE/DeviceConfig/Isp/Gamma.cs`

#### 3.1.1 内部参数结构体

```csharp
private struct YGammaParams
{
    public int br_mod;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public int[] gma_num;          // 8 个 Gamma 参数
    public int contra_num;
    public int bofst;
    public int lofst;
    public int lcpr_low;
    public int lcpr_high;
    public int lcpr_llimt;
    public int lcpr_hlimt;
    public int pad_num;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public short[] using_ygama;    // 256 点 Gamma 查找表
};
```

**参数说明**:
- `br_mod`: 亮度模式
- `gma_num[8]`: 8 个 Gamma 曲线参数
- `contra_num`, `bofst`, `lofst`: 对比度偏移参数
- `lcpr_*`: 局部对比度参数 (low/high/llimt/hlimt)
- `pad_num`: 填充参数
- `using_ygama[256]`: 完整的 Gamma 查找表 (256 点)

**⚠️ 注意**: 结构体名称 `using_ygama` 拼写错误，应为 `using_ygamma`

#### 3.1.2 默认 Gamma 表

```csharp
private short[] _yGammaTable = new short[]
{
    0x0, 0x8d, 0xb5, 0xd1, 0xe8, 0xfb, 0x10c, 0x11b, ...
    // 共 256 个值 (0x000 ~ 0x3FF)
};
```

**特点**:
- 默认值从 `0x000` 到 `0x3FF` (0~1023)
- 符合 Gamma 曲线的非线性特性 (暗部增长慢，亮部增长快)
- 输出范围为 10-bit (0-1023)

#### 3.1.3 构造函数

```csharp
public YGamma()
{
    DeviceModulePos = 7;

    SetPreviousStepEnable(IspModule.Blc, true);
    SetPreviousStepEnable(IspModule.Lsc, true);
    SetPreviousStepEnable(IspModule.Awb, true);
}
```

**设计说明**:
- 模块位置索引为 7
- 强制要求前置步骤 Blc、Lsc、Awb 必须启用
- 确保 Gamma 校正前图像已经过基础校正

---

### 3.2 文件导入/导出功能

#### 3.2.1 加载 Gamma 表

```csharp
public void LoadYGammaTableFromFile(string tableFile)
{
    string fileContent = File.ReadAllText(tableFile);

    short[] yGammaTable;

    if (fileContent.StartsWith("0x"))
    {
        // 十六进制格式: 每行一个值，如 "0x0\n0x8d\n0xb5..."
        yGammaTable = fileContent.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => short.Parse(s.Substring(2).ToUpper(), System.Globalization.NumberStyles.HexNumber))
            .ToArray();
    }
    else
    {
        // 十进制格式: 逗号分隔，如 "0,141,181,209..."
        yGammaTable = fileContent.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Convert.ToInt16(s))
            .ToArray();
    }

    if (yGammaTable.Length < 256)
    {
        System.Windows.MessageBox.Show("数据格式不正确", "", 
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        return;
    }

    _yGammaTable = yGammaTable;
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("YGammaTable"));
}
```

**支持的格式**:
1. **十六进制格式**: 每行一个 `0x` 开头的值
2. **十进制逗号分隔**: 单行或多行，逗号分隔

**⚠️ 问题**:
- 错误提示为中文乱码（代码页问题）
- 缺少异常处理，`short.Parse` 和 `Convert.ToInt16` 可能崩溃
- 仅检查 `Length < 256`，未检查上限

#### 3.2.2 保存 Gamma 表

```csharp
public void SaveYGammaTableToFile(string tableFile)
{
    string fileContent = String.Join(",", 
        new List<short>(_yGammaTable).ConvertAll(i => i.ToString()).ToArray());

    fileContent = fileContent.Substring(0, fileContent.Length); // ⚠️ 冗余操作

    File.WriteAllText(tableFile, fileContent);
}
```

**⚠️ 代码质量问题**:
- `fileContent.Substring(0, fileContent.Length)` 是无意义的冗余操作
- 应使用更简洁的 `string.Join(",", _yGammaTable)`

---

### 3.3 软件图像处理

```csharp
public override void ProcessRgbBuffer(ref byte[] imgBuffer)
{
    int tmpReadPos = 0;
    IntPtr[] inBuffer = new IntPtr[3];
    
    // 分配输入缓冲区 (RGB 三通道)
    for (int i = 0; i < inBuffer.Length; i++)
    {
        inBuffer[i] = Marshal.AllocHGlobal(
            _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
        Marshal.Copy(imgBuffer, tmpReadPos, inBuffer[i], 
            _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
        tmpReadPos += _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short);
    }

    // 分配输出缓冲区
    IntPtr[] outBuffer = new IntPtr[3];
    for (int i = 0; i < outBuffer.Length; i++)
    {
        outBuffer[i] = Marshal.AllocHGlobal(
            _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
        Marshal.Copy(new byte[_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short)],
            0, outBuffer[i], 
            _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
    }

    // 调用 C++ DLL 处理
    IspApi.YGammaImg(_commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, 
        PadNum, YGammaTable, inBuffer, outBuffer);

    // 释放输入缓冲区
    for (int i = 0; i < outBuffer.Length; i++)
    {
        Marshal.FreeHGlobal(inBuffer[i]);  // ⚠️ 应为 inBuffer[i]
    }

    // 复制结果并释放输出缓冲区
    tmpReadPos = 0;
    for (int i = 0; i < outBuffer.Length; i++)
    {
        Marshal.Copy(outBuffer[i], imgBuffer, tmpReadPos, 
            _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
        tmpReadPos += _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short);
        Marshal.FreeHGlobal(outBuffer[i]);
    }
}
```

**处理流程**:
1. 将输入的 RGB 缓冲区拆分为 R、G、B 三个独立通道
2. 每个通道转换为 `short` 数组 (16-bit)
3. 调用 `IspApi.YGammaImg` 对三通道分别应用 Gamma 查找表
4. 将处理后的三通道合并回 `imgBuffer`
5. 释放所有非托管内存

**⚠️ 严重 Bug**:
```csharp
// 错误代码
for (int i = 0; i < outBuffer.Length; i++)  // ⚠️ 使用了 outBuffer.Length
{
    Marshal.FreeHGlobal(inBuffer[i]);       // ⚠️ 但释放的是 inBuffer
}

// 应该改为
for (int i = 0; i < inBuffer.Length; i++)
{
    Marshal.FreeHGlobal(inBuffer[i]);
}
```

虽然当前代码碰巧能工作（因为 `inBuffer.Length == outBuffer.Length`），但逻辑错误易引发混淆。

**⚠️ 内存泄漏风险**:
- 如果 `IspApi.YGammaImg` 抛出异常，`FreeHGlobal` 不会执行
- 应使用 `try-finally` 确保内存释放

---

### 3.4 参数数据集合 (与 C++ 交互)

```csharp
public override Dictionary<int, byte[]> ParamsDataCollection
{
    get
    {
        YGammaParams yGammaParams = new YGammaParams()
        {
            br_mod = 0,           // ⚠️ 硬编码为 0
            gma_num = new int[8], // ⚠️ 全部初始化为 0
            contra_num = 0,
            bofst = 0,
            lofst = 0,
            lcpr_low = 0,
            lcpr_high = 0,
            lcpr_llimt = 0,
            lcpr_hlimt = 0,
            pad_num = PadNum,
            using_ygama = YGammaTable
        };

        int size = Marshal.SizeOf(yGammaParams);
        byte[] arr = new byte[size];

        IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(yGammaParams, ptr, true);  // ⚠️ 应为 false
        Marshal.Copy(ptr, arr, 0, size);
        Marshal.FreeHGlobal(ptr);

        return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
    }
    set
    {
        // 反向操作：从字节数组还原
        YGammaParams yGammaParams = new YGammaParams();
        int size = Marshal.SizeOf(yGammaParams);
        IntPtr ptr = Marshal.AllocHGlobal(size);

        Marshal.Copy(value[DeviceModulePos], 0, ptr, size);

        yGammaParams = (YGammaParams)Marshal.PtrToStructure(ptr, yGammaParams.GetType());
        Marshal.FreeHGlobal(ptr);

        YGammaTable = yGammaParams.using_ygama;
        PadNum = (byte)yGammaParams.pad_num;
    }
}
```

**问题清单**:

| 问题 | 严重性 | 描述 |
|------|--------|------|
| **参数硬编码** | 🔴 严重 | `br_mod`、`gma_num`、`contra_num` 等全部硬编码为 0，丢失配置 |
| **Marshal 参数错误** | 🟡 中等 | `StructureToPtr` 第三参数应为 `false` |
| **缺少异常保护** | 🟡 中等 | `value[DeviceModulePos]` 可能不存在 |
| **内存泄漏** | 🟡 中等 | 缺少 `try-finally` 保护 |

---

### 3.5 XML 序列化/反序列化

#### 3.5.1 序列化

```csharp
public override XmlElement SerializeToXmlElement(XmlDocument xmlDoc)
{
    var xmlElement = xmlDoc.CreateElement("YGamma");

    // 序列化完整的 256 点 Gamma 表
    XmlElement globalGammaTableNode = xmlDoc.CreateElement("Global_Gamma_Table");
    string yGammaTable = string.Join(",", YGammaTable.Select(x => x.ToString()).ToArray());
    globalGammaTableNode.AppendChild(xmlDoc.CreateTextNode(yGammaTable));
    xmlElement.AppendChild(globalGammaTableNode);

    // 序列化 PadNum
    XmlElement padNumNode = xmlDoc.CreateElement("Pad_Num");
    padNumNode.AppendChild(xmlDoc.CreateTextNode(PadNum.ToString()));
    xmlElement.AppendChild(padNumNode);

    return xmlElement;
}
```

**✅ 优点**: 
- 序列化了核心的 `YGammaTable` 和 `PadNum`
- 相比 AE 模块更加完整

**⚠️ 问题**:
- 未序列化 `YGammaParams` 中的其他参数（虽然当前硬编码为 0）

#### 3.5.2 反序列化

```csharp
public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
{
    var yGammaNode = ispToolDataNode["YGamma"];

    var tmpYGammaTableStr = XmlHelper.GetNodeValue(yGammaNode, "Global_Gamma_Table");
    if (tmpYGammaTableStr != null)
    {
        YGammaTable = tmpYGammaTableStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Convert.ToInt16(s))
            .ToArray();
    }

    PadNum = Convert.ToByte(XmlHelper.GetNodeValue(yGammaNode, "Pad_Num"));  // ⚠️ 缺少空检查
}
```

**⚠️ 问题**:
- `Convert.ToByte(null)` 会抛出 `ArgumentNullException`
- 应使用 `XmlHelper.ParseInt` 或添加空检查

---

## 4. YGamma UI 层分析

### 4.1 YGammaWindow (调试窗口)

**文件路径**: 
- `ThunderSE/Ui/SettingWindow/YGamma/YGammaWindow.xaml`
- `ThunderSE/Ui/SettingWindow/YGamma/YGammaWindowViewModel.cs`

#### 4.1.1 界面布局

```xml
<Window Title="YGammaWindow" Height="768" Width="1024">
    <Grid>
        <!-- 左侧: Gamma 曲线图表 -->
        <chartingToolkit:Chart Name="YGammaChart">
            <chartingToolkit:Chart.Axes>
                <LinearAxis Orientation="X" Minimum="0" Maximum="255" />
                <LinearAxis Orientation="Y" Minimum="0" Maximum="1023" />
            </chartingToolkit:Chart.Axes>
            <chartingToolkit:LineSeries ItemsSource="{Binding YGammaTable}" />
        </chartingToolkit:Chart>
        
        <!-- 右侧: 功能按钮 -->
        <StackPanel>
            <Button Command="{Binding LoadYGammaTableFromFileCommand}">导入</Button>
            <Button Command="{Binding SaveYGammaTableToFileCommand}">导出</Button>
            <Button>在线调试读</Button>
            <Button>在线调试写</Button>
            <Button Click="OnResetChartAxes">复位比例</Button>
            <Button Click="OnClickCalcIQButton">计算IQ
                <ContextMenu>
                    <MenuItem Header="在线IQ" Click="ShowOnlineYGammaIQ"/>
                    <MenuItem Header="离线IQ" Click="ShowOfflineYGammaIQ"/>
                </ContextMenu>
            </Button>
        </StackPanel>
    </Grid>
</Window>
```

**功能特点**:
- 使用 WPF Toolkit 的 Chart 控件显示 Gamma 曲线
- X 轴: 0-255 (输入亮度)
- Y 轴: 0-1023 (输出亮度，10-bit)
- 支持鼠标滚轮缩放、右键拖拽平移
- 支持导入/导出 Gamma 表
- 支持在线/离线 IQ 质量分析

#### 4.1.2 ViewModel 关键点编辑机制

```csharp
// 19 个关键点 X 坐标
private int[] _yGammaKeyPointXValues = new int[]{
    0, 1, 3, 6, 10, 16, 26, 39, 55, 71, 87, 103, 119, 135, 151, 167, 191, 223, 239, 255
};
```

**关键点选择策略**:
- 暗部区域 (0-63): 密集分布 (19 个点中的前 10 个)
- 中间调 (64-191): 中等密度
- 高光 (192-255): 稀疏分布

这符合 Gamma 曲线在暗部变化更快、需要更精细调节的特性。

#### 4.1.3 曲线插值算法

```csharp
void YGammaTable_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
{
    if (e.Action == NotifyCollectionChangedAction.Replace)
    {
        // 更新当前关键点
        _yGamma.YGammaTable[_yGammaKeyPointXValues[e.NewStartingIndex]] = 
            _yGammaTable[e.NewStartingIndex].Value;

        // 向前插值 (与前一个关键点之间)
        if (e.NewStartingIndex > 0)
        {
            int previousYGammaKeyPointX = _yGammaKeyPointXValues[e.NewStartingIndex - 1];
            int gammaPointCountBetweenTwoKeyPoints = 
                _yGammaKeyPointXValues[e.NewStartingIndex] - previousYGammaKeyPointX;

            float partitionalValueBetweenKeyPoints =
                (_yGammaTable[e.NewStartingIndex].Value - _yGammaTable[e.NewStartingIndex - 1].Value) 
                / (float)gammaPointCountBetweenTwoKeyPoints;

            for (int i = 1; i < gammaPointCountBetweenTwoKeyPoints; i++)
            {
                _yGamma.YGammaTable[previousYGammaKeyPointX + i] =
                    (short)(_yGamma.YGammaTable[previousYGammaKeyPointX]
                        + (short)Math.Floor(partitionalValueBetweenKeyPoints * i));
            }
        }

        // 向后插值 (与后一个关键点之间)
        if (e.NewStartingIndex < _yGammaKeyPointXValues.Length - 1)
        {
            // 类似逻辑...
        }
    }
}
```

**设计说明**:
- 用户修改关键点时，自动在相邻关键点之间进行线性插值
- 保证 Gamma 曲线连续平滑
- 使用 `Math.Floor` 向下取整避免溢出

**⚠️ 问题**:
- 使用线性插值，而非 Gamma 曲线（应为幂函数）
- 可能导致曲线不够平滑

---

### 4.2 在线 IQ 调试窗口 (YGammaOnlineIQWindow)

**文件路径**: 
- `ThunderSE/Ui/SettingWindow/YGamma/YGammaOnlineIQWindow.xaml`
- `ThunderSE/Ui/SettingWindow/YGamma/YGammaOnlineIQWindow.xaml.cs`

#### 4.2.1 功能概述

```
┌──────────────────────────────────────────────┐
│          UVC 视频实时显示区域                  │
│     (支持橡皮筋框选 ROI 区域)                  │
├──────────────────────────────────────────────┤
│      IQ 计算结果显示 (DataGrid)               │
│  - ref_count                                 │
│  - l_val_array                               │
│  - delta_l_array                             │
│  - yMax, yAvg, out_gamma                     │
├──────────────────────────────────────────────┤
│ [计算IQ] [停止] [撤销选框] 色卡:[6阶|13阶]   │
└──────────────────────────────────────────────┘
```

#### 4.2.2 视频流接收与 ROI 分析

```csharp
private void OnUvcDataReceive(byte[] dataBuffer)
{
    // 更新显示图像
    _bitmap.Lock();
    _bitmap.WritePixels(new Int32Rect(0, 0, (int)_bitmap.Width, (int)_bitmap.Height),
        dataBuffer, (int)_bitmap.Width * 3, 0);
    _bitmap.AddDirtyRect(new Int32Rect(0, 0, _videoWidth, _videoHeight));
    _bitmap.Unlock();

    // 计算每个 ROI 区域的平均 RGB 值
    if (_rubberBandData.Count > 0)
    {
        for (int i = 0; i < _rubberBandData.Count; i++)
        {
            if (SelectedCalcMode == 0)  // 6 阶模式
            {
                var croppedBitmap = new CroppedBitmap(_bitmap,
                    new Int32Rect(_rubberBandData[i].x, _rubberBandData[i].y, 
                        _rubberBandData[i].width, _rubberBandData[i].height));

                var pixels = new byte[_rubberBandData[i].width * _rubberBandData[i].height * 3];
                croppedBitmap.CopyPixels(pixels, _rubberBandData[i].width * 3, 0);

                int rSum = 0, gSum = 0, bSum = 0;
                for (int j = 0; j < pixels.Length / 3; j++)
                {
                    rSum += pixels[j * 3 + 2];
                    gSum += pixels[j * 3 + 1];
                    bSum += pixels[j * 3 + 0];
                }

                _avgRArray[i] = rSum / (pixels.Length / 3);
                _avgGArray[i] = gSum / (pixels.Length / 3);
                _avgBArray[i] = bSum / (pixels.Length / 3);
            }
            else  // 13 阶模式
            {
                // 每个 ROI 分为 3 个子区域，共 39 个采样点
            }
        }
    }
}
```

**工作原理**:
1. 接收 UVC 视频流 (RGB24 格式)
2. 用户在画面上框选 6 或 13 个 ROI 区域（对应色卡）
3. 对每个 ROI 区域计算平均 RGB 值
4. 调用 `IspApi.YGAMMA_IQ` 计算 Gamma 质量指标

#### 4.2.3 IQ 计算

```csharp
void OnCalcIQ(object sender, EventArgs e)
{
    new Thread(() =>
    {
        double[] diff_l = new double[6] { 10, 10, 10, 10, 10, 10 };
        int ref_count = 0;
        double[] l_val_array;
        double[] delta_l_array;
        double yMax = 0.0;
        double[] yAvg;
        double out_gamma = 0.0;

        // 调用 C++ DLL 计算 IQ
        IspApi.YGAMMA_IQ(_avgRArray, _avgGArray, _avgBArray, 6, 
            diff_l, ref ref_count, l_val_array, delta_l_array, 
            ref yMax, yAvg, ref out_gamma);

        // 更新 UI 显示
        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
        {
            DataContext = new ObservableCollection<KeyValuePair<string, string>>(){
                new KeyValuePair<string, string>("ref_count", ref_count.ToString()),
                new KeyValuePair<string, string>("l_val_array", 
                    string.Join(",", l_val_array.Select(x => x.ToString("0.00")))),
                // ...
            };
        }));
    }).Start();
}
```

**输出参数**:
- `ref_count`: 参考计数
- `l_val_array`: 亮度值数组
- `delta_l_array`: 亮度差值数组
- `yMax`: 最大亮度
- `yAvg`: 平均亮度 (按通道分组)
- `out_gamma`: 计算出的 Gamma 值

**⚠️ 性能问题**:
- 每 2 秒 (`TimeSpan(20000000)`) 计算一次，可能不够实时
- 使用 `Thread` 而非 `Task.Run`，不符合现代异步模式

---

### 4.3 离线 IQ 调试窗口 (YGammaOfflineIQWindow)

**文件路径**: 
- `ThunderSE/Ui/SettingWindow/YGamma/YGammaOfflineIQWindow.xaml.cs`

#### 功能特点

```csharp
private void OnLoadRgbButtonClick(object sender, RoutedEventArgs e)
{
    // 加载 .rgb 文件
    _rgbBuffer = File.ReadAllBytes(openFileDialog.FileName);
    OriginImg.DisplayImageSource = _ispProcessor.GenerateBitmapUsingRgb(_rgbBuffer);
    IsLoadImage = true;
}

private void OnCalcIQClick(object sender, RoutedEventArgs e)
{
    // 获取 6 个 ROI 区域的坐标和尺寸
    int[] XArray = new int[6];
    int[] YArray = new int[6];
    int[] HeightArray = new int[6];
    int[] WidthArray = new int[6];

    for (int j = 0; j < _rubberBandData.Count; j++)
    {
        XArray[j] = _rubberBandData[j].x;
        YArray[j] = _rubberBandData[j].y;
        HeightArray[j] = _rubberBandData[j].height;
        WidthArray[j] = _rubberBandData[j].width;
    }

    // ⚠️ 未完成：处理逻辑被注释掉
    //_gammaStep.ProcessRgbBuffer(ref ptrArray);
    //IspApi.EncoderImgBuffer(...);
}
```

**⚠️ 未完成功能**:
- IQ 计算逻辑被注释，功能不完整
- 需要补充图像处理和数据导出逻辑

---

## 5. LcdGamma (LCD 显示 Gamma) 分析

### 5.1 LcdGamma.cs - 数据模型

**文件路径**: `ThunderSE/DeviceConfig/Lcd/LcdGamma.cs`

#### 参数说明

```csharp
class LcdGamma : LcdSettingSection, INotifyPropertyChanged
{
    private int _contra_index;      // 对比度索引 (0-12)
    private int _gamma_red;         // 红色 Gamma 值 (0-11)
    private int _gamma_green;       // 绿色 Gamma 值 (0-11)
    private int _gamma_blue;        // 蓝色 Gamma 值 (0-11)
}
```

**与 YGamma 的区别**:
- 不是 256 点查找表，而是 3 个索引值
- 范围更小 (0-11)，可能是硬件查找表的索引
- 支持独立的 RGB 通道调节

#### 序列化/反序列化

```csharp
public override XmlElement SerializeToXmlElement(XmlDocument xmlDoc)
{
    var xmlElement = xmlDoc.CreateElement("Gamma");

    XmlElement gammaRedNode = xmlDoc.CreateElement("gamma_red");
    gammaRedNode.AppendChild(xmlDoc.CreateTextNode(gamma_red.ToString()));
    xmlElement.AppendChild(gammaRedNode);

    // gamma_green, gamma_blue 类似...

    return xmlElement;
}

public override void DeserializeFromXmlElement(XmlElement LcdNode)
{
    var blcNode = LcdNode["Gamma"];  // ⚠️ 变量名错误，应为 gammaNode

    gamma_red = XmlHelper.GetNodeShort(blcNode, "gamma_red");
    gamma_green = XmlHelper.GetNodeShort(blcNode, "gamma_green");
    gamma_blue = XmlHelper.GetNodeShort(blcNode, "gamma_blue");
}
```

**⚠️ 问题**:
- 变量命名错误 (`blcNode` 应为 `gammaNode`)
- 缺少空检查，`XmlHelper.GetNodeShort` 返回可能为 null

---

### 5.2 LcdGamma UI (用户模式)

**文件路径**: 
- `ThunderSE/Ui/MainWindow/UserMode/LcdTabControl/LcdGammaArea.xaml`
- `ThunderSE/Ui/MainWindow/UserMode/LcdTabControl/LcdGammaAreaViewModel.cs`

#### 界面布局

```xml
<UserControl>
    <GroupBox Header="Gamma">
        <StackPanel>
            <StackPanel Orientation="Horizontal">
                <Label>红色 :</Label>
                <Slider Value="{Binding gamma_red}" 
                        Maximum="{Binding GammaMaxValue}"  <!-- 11 -->
                        Minimum="{Binding GammaMinValue}"/> <!-- 0 -->
                <TextBox Text="{Binding gamma_red}"/>
            </StackPanel>
            <!-- 绿色、蓝色 类似 -->
        </StackPanel>
    </GroupBox>
</UserControl>
```

**ViewModel**:
```csharp
class LcdGammaAreaViewModel : ViewModelBase
{
    public int GammaMaxValue => 11;  // ⚠️ 硬编码
    public int GammaMinValue => 0;
    public int ContraIndexMaxValue => 12;
    public int ContraIndexMinValue => 0;
}
```

**⚠️ 问题**:
- 最大值硬编码为 11，可能与硬件规格不符
- 缺少从硬件/配置读取的机制

---

## 6. IspApi C++ DLL 接口

### 6.1 YGammaImg - 图像处理

```csharp
[DllImport("IspApi.dll", CallingConvention = CallingConvention.Cdecl)]
public static extern void YGammaImg(
    int w, 
    int h, 
    int pad_num, 
    short[] global_gamma_table,  // 256 点 Gamma 查找表
    IntPtr[] input_img,          // RGB 三通道输入
    IntPtr[] output_img          // RGB 三通道输出
);
```

**C++ 端实现** (推测):
```cpp
extern "C" UVC_API void YGammaImg(int w, int h, int pad_num, 
                                   short* gamma_table, 
                                   IntPtr* in_buffers, 
                                   IntPtr* out_buffers)
{
    // 对每个通道的每个像素应用 Gamma 查找表
    for (int channel = 0; channel < 3; channel++)
    {
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                short input = in_buffers[channel][y * w + x];
                out_buffers[channel][y * w + x] = gamma_table[input];
            }
        }
    }
}
```

### 6.2 YGAMMA_IQ - 质量计算

```csharp
[DllImport("IspApi.dll", CallingConvention = CallingConvention.Cdecl)]
public static extern void YGAMMA_IQ(
    double[] gr_avg,     // 绿色通道 R 分量平均值
    double[] gg_avg,     // 绿色通道 G 分量平均值
    double[] gb_avg,     // 绿色通道 B 分量平均值
    int num,             // 采样点数量 (6 或 13)
    double[] diff_l,     // 亮度差阈值
    ref int count,       // 参考计数 (输出)
    double[] l_var,      // 亮度值数组 (输出)
    double[] delta_l,    // 亮度差值数组 (输出)
    ref double y_max,    // 最大亮度 (输出)
    double[] y_avg,      // 平均亮度数组 (输出)
    ref double out_gamma // 计算的 Gamma 值 (输出)
);
```

**用途**: 根据色卡的 RGB 采样值计算 Gamma 曲线质量指标

---

## 7. 数据流生命周期

### 7.1 配置加载流程

```
1. 用户打开配置文件
   ↓
2. ConfigManager.DeserializeIspConfig()
   ↓
3. YGamma.DeserializeFromXmlElement(xmlNode)
   - 恢复 YGammaTable[256]
   - 恢复 PadNum
   ↓
4. UI 绑定刷新图表显示
```

### 7.2 参数下发流程

```
1. 用户在 YGammaWindow 修改曲线
   ↓
2. ViewModel 更新关键点
   ↓
3. 插值算法填充完整 256 点表
   ↓
4. YGamma.HasChangedParams = true
   ↓
5. Processor.CollectParams()
   ↓
6. YGamma.ParamsDataCollection (getter)
   - 转换为 YGammaParams 结构体
   - Marshal 编组为字节数组
   ↓
7. 发送到 C++ DLL / 硬件
```

### 7.3 软件处理流程

```
1. 用户加载测试图片 (.rgb 文件)
   ↓
2. Processor.ProcessRgbFile()
   ↓
3. YGamma.ProcessRgbBuffer(ref imgBuffer)
   - 拆分 RGB 三通道
   - 调用 IspApi.YGammaImg 应用 Gamma 表
   - 合并 RGB 三通道
   ↓
4. 显示处理后的图像
```

---

## 8. 已知问题与风险

### 8.1 严重问题

| 问题 | 位置 | 严重性 | 描述 |
|------|------|--------|------|
| **参数硬编码** | `YGamma.cs:ParamsDataCollection` | 🔴 严重 | `br_mod`、`gma_num`、`lcpr_*` 等参数硬编码为 0 |
| **内存释放逻辑错误** | `YGamma.cs:ProcessRgbBuffer` | 🟡 中等 | 循环条件使用 `outBuffer.Length` 但释放 `inBuffer` |
| **反序列化空引用** | `YGamma.cs:DeserializeFromXmlElement` | 🟡 中等 | `Convert.ToByte(null)` 会崩溃 |

### 8.2 代码质量问题

| 问题 | 位置 | 建议 |
|------|------|------|
| 冗余代码 | `YGamma.cs:SaveYGammaTableToFile` | 删除 `Substring` 操作 |
| 命名错误 | `LcdGamma.cs` 变量 `blcNode` | 改为 `gammaNode` |
| 拼写错误 | `YGammaParams.using_ygama` | 改为 `using_ygamma` |
| 硬编码最大值 | `LcdGammaAreaViewModel.cs` | 从配置读取 |
| 异步模式过时 | `YGammaOnlineIQWindow.cs` | 使用 `Task.Run` 替代 `Thread` |

### 8.3 功能不完整

| 功能 | 状态 | 描述 |
|------|------|------|
| 离线 IQ 计算 | ⚠️ 未完成 | 处理逻辑被注释 |
| 在线调试读/写 | ⚠️ 空实现 | ViewModel 中方法为空 |
| 设备参数读取 | ⚠️ 空实现 | `LoadYGammaTableFromDevice` 为空 |

---

## 9. 改进建议

### 9.1 紧急修复

#### 修复参数硬编码

```csharp
// 方案 1: 添加对应的属性
public int BrMod { get; set; }
public int[] GmaNum { get; set; } = new int[8];
public int ContraNum { get; set; }
// ... 其他参数

// 修改 ParamsDataCollection
YGammaParams yGammaParams = new YGammaParams()
{
    br_mod = BrMod,          // 使用属性值
    gma_num = GmaNum,        // 使用属性值
    contra_num = ContraNum,
    // ...
    using_ygama = YGammaTable
};
```

#### 修复内存释放错误

```csharp
public override void ProcessRgbBuffer(ref byte[] imgBuffer)
{
    IntPtr[] inBuffer = new IntPtr[3];
    IntPtr[] outBuffer = new IntPtr[3];

    try
    {
        // 分配和初始化...
        
        IspApi.YGammaImg(/* ... */);

        // 复制结果...
    }
    finally
    {
        // 确保始终释放内存
        for (int i = 0; i < inBuffer.Length; i++)
        {
            if (inBuffer[i] != IntPtr.Zero)
                Marshal.FreeHGlobal(inBuffer[i]);
        }
        for (int i = 0; i < outBuffer.Length; i++)
        {
            if (outBuffer[i] != IntPtr.Zero)
                Marshal.FreeHGlobal(outBuffer[i]);
        }
    }
}
```

#### 修复反序列化空引用

```csharp
public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
{
    var yGammaNode = ispToolDataNode["YGamma"];
    if (yGammaNode == null) return;

    var tmpYGammaTableStr = XmlHelper.GetNodeValue(yGammaNode, "Global_Gamma_Table");
    if (tmpYGammaTableStr != null)
    {
        YGammaTable = tmpYGammaTableStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => 
            {
                short.TryParse(s, out short val);
                return val;
            })
            .ToArray();
    }

    var padNumStr = XmlHelper.GetNodeValue(yGammaNode, "Pad_Num");
    if (padNumStr != null)
    {
        PadNum = byte.TryParse(padNumStr, out byte padNum) ? padNum : PadNum;
    }
}
```

### 9.2 代码质量改进

#### 简化文件保存

```csharp
public void SaveYGammaTableToFile(string tableFile)
{
    string fileContent = string.Join(",", _yGammaTable);
    File.WriteAllText(tableFile, fileContent);
}
```

#### 使用现代异步模式

```csharp
private async void OnCalcIQ(object sender, RoutedEventArgs e)
{
    timerForCalcIQ.Start();
    IsCalculating = true;
    IsDrawing = false;

    await Task.Run(() =>
    {
        // IQ 计算逻辑...
    });

    IsCalculating = false;
    IsDrawing = true;
}
```

### 9.3 架构改进建议

1. **完成离线 IQ 功能**: 补充注释掉的代码逻辑
2. **添加 Gamma 预设**: 提供常用 Gamma 曲线预设（sRGB、Adobe RGB 等）
3. **改进插值算法**: 使用幂函数插值替代线性插值
4. **添加曲线平滑功能**: 支持用户手动平滑 Gamma 曲线
5. **参数范围验证**: 加载文件时检查值是否在合法范围 (0-1023)

---

## 10. 与其他模块的关系

### 10.1 依赖关系

```
YGamma (索引 7)
  ├── 前置依赖:
  │   ├── Blc (黑电平校正) - 强制启用
  │   ├── Lsc (镜头阴影校正) - 强制启用
  │   └── Awb (自动白平衡) - 强制启用
  ├── 后续步骤:
  │   └── Ee (边缘增强)
  └── 被依赖:
      ├── Ccm (颜色校正矩阵) - Gamma 影响亮度后影响 CCM
      └── VDE (视频数据增强)
```

### 10.2 在 ISP 管线中的位置

```
AE → Blc → Lsc → Ddc → Awb → Ccm → YGamma → Ee → Saj → VDE
                            ↑              ↑
                            └── 强制启用 ──┘
```

### 10.3 与 CCM 的协同

- **YGamma 优先**: 先校正亮度 Gamma 曲线
- **CCM 后续**: Gamma 校正后的亮度影响颜色矩阵计算
- **相互影响**: Gamma 过亮/过暗会导致 CCM 色彩还原不准确

---

## 11. 总结

### 11.1 架构评价

| 方面 | YGamma | LcdGamma |
|------|--------|----------|
| 功能完整性 | ⭐⭐⭐⭐ | ⭐⭐⭐ |
| UI 调试工具 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ |
| 序列化完整性 | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| 内存管理 | ⭐⭐ | ⭐⭐ |
| 代码质量 | ⭐⭐⭐ | ⭐⭐⭐ |

### 11.2 关键行动项

1. **🔴 紧急**: 修复 `ParamsDataCollection` 中的参数硬编码问题
2. **🔴 紧急**: 修复 `ProcessRgbBuffer` 中的内存释放逻辑
3. **🟡 重要**: 添加反序列化空引用保护
4. **🟡 重要**: 完成离线 IQ 计算功能
5. **🟢 建议**: 改进曲线插值算法（使用幂函数）
6. **🟢 建议**: 添加 Gamma 预设配置

### 11.3 与 AE 模块的对比

| 特性 | AE 模块 | YGamma 模块 |
|------|---------|-------------|
| 序列化完整性 | ⭐ (仅 exp_tag) | ⭐⭐⭐⭐ (完整) |
| 软件处理能力 | ❌ NotImplementedException | ✅ 完整实现 |
| UI 调试工具 | 简单滑块 | 完整图表+IQ分析 |
| 主要问题 | 配置丢失 | 参数硬编码 |

---

## 附录：文件清单

### YGamma 相关文件

| 文件路径 | 类型 | 说明 |
|---------|------|------|
| `DeviceConfig/Isp/Gamma.cs` | 数据模型 | YGamma 主类 |
| `DeviceConfig/Isp/IspApi.cs` | API 声明 | YGammaImg / YGAMMA_IQ |
| `Ui/SettingWindow/YGamma/YGammaWindow.xaml` | UI | Gamma 曲线调试窗口 |
| `Ui/SettingWindow/YGamma/YGammaWindowViewModel.cs` | ViewModel | 曲线编辑逻辑 |
| `Ui/SettingWindow/YGamma/YGammaOnlineIQWindow.xaml` | UI | 在线 IQ 分析窗口 |
| `Ui/SettingWindow/YGamma/YGammaOnlineIQWindow.xaml.cs` | UI 代码 | 视频流 ROI 分析 |
| `Ui/SettingWindow/YGamma/YGammaOfflineIQWindow.xaml.cs` | UI 代码 | 离线 IQ 分析（未完成） |

### LcdGamma 相关文件

| 文件路径 | 类型 | 说明 |
|---------|------|------|
| `DeviceConfig/Lcd/LcdGamma.cs` | 数据模型 | LcdGamma 主类 |
| `DeviceConfig/Lcd/LcdSetting.cs` | 结构体定义 | lcd_gamma_t 等 |
| `DeviceConfig/Lcd/LcdSettingSection.cs` | 抽象基类 | LcdSettingSection |
| `Ui/MainWindow/UserMode/LcdTabControl/LcdGammaArea.xaml` | UI | LCD Gamma 调节区域 |
| `Ui/MainWindow/UserMode/LcdTabControl/LcdGammaAreaViewModel.cs` | ViewModel | LCD Gamma 视图模型 |

---

**报告生成日期**: 2026 年 4 月 7 日  
**分析工具版本**: Qwen Code Agent
