# BLC (黑电平校正) 模块详细需求规格说明

## 文档信息

| 项目 | 内容 |
|------|------|
| **模块名称** | BLC (Black Level Correction / 黑电平校正) |
| **DeviceModulePos** | 1 |
| **IspModule 枚举值** | `IspModule.Blc` |
| **文档版本** | v1.0 |
| **创建日期** | 2026年4月8日 |
| **分析依据** | 项目源码完整阅读与交叉引用 |

---

## 一、模块概述

### 1.1 功能描述

BLC (Black Level Correction) 模块是 ISP 图像处理管线中的**黑电平校正模块**，用于校正图像传感器固有的暗电流偏移。

**核心功能**:
- 校正 CMOS/CCD 传感器在无光照条件下的暗电流偏移
- 每个 Bayer 通道 (R, Gr, Gb, B) 有独立的校正值
- 支持从 RAW 文件自动计算黑电平值
- 提供中值和平均值两种校正方式
- 可视化展示四通道像素分布直方图

### 1.2 物理原理

**黑电平偏移成因**:
1. **暗电流**: 传感器在无光照条件下仍会产生少量电子-空穴对
2. **读出电路偏移**: 传感器读出电路存在固有偏置电压
3. **温度影响**: 暗电流随温度升高而增加 (约每 6-8°C 翻倍)

**校正原理**:
- 在无光照条件下拍摄 "光学黑" 图像，测量各通道的平均输出值
- 从正常图像的每个像素中减去对应的黑电平偏置
- 确保黑色区域真正归零，为后续 ISP 处理提供干净的基准

### 1.3 在 ISP 管线中的位置

```
Raw Bayer → BLC → LSC → AWB → Demosaic → CCM → YGamma → EE → CH → 输出
   (0)       (1)    (2)    (4)               (5)    (7)    (11)  (9)
             ↑ BLC 在第 1 步 (首个图像处理模块)
```

**前置依赖**: 无 — BLC 是 ISP 管线中第一个图像处理模块

**处理类型**: RAW 域处理 (ProcessRawBuffer)

---

## 二、参数完整定义

### 2.1 参数总表

| 参数 | 类型 | 默认值 | 取值范围 | 说明 |
|------|------|--------|----------|------|
| R | short | 0 | -512 ~ 511 | R 通道黑电平校正值 |
| Gr | short | 0 | -512 ~ 511 | Gr 通道黑电平校正值 |
| Gb | short | 0 | -512 ~ 511 | Gb 通道黑电平校正值 |
| B | short | 0 | -512 ~ 511 | B 通道黑电平校正值 |
| CorrectValuesArray | short[4] | {0, 0, 0, 0} | 同上 | 四通道校正值数组 (只读) |

### 2.2 BlackLevelPixelType 枚举

```csharp
public enum BlackLevelPixelType
{
    R,   // 0 - 红色通道
    Gr,  // 1 - 绿色R行通道
    Gb,  // 2 - 绿色B行通道
    B    // 3 - 蓝色通道
}
```

**Bayer 格式中的通道分布**:

| Bayer 模式 | R 位置 | Gr 位置 | Gb 位置 | B 位置 |
|-----------|--------|---------|---------|--------|
| RGRG (RGGB) | (偶, 偶) | (偶, 奇) | (奇, 偶) | (奇, 奇) |
| GRGR (GRBG) | (偶, 奇) | (偶, 偶) | (奇, 奇) | (奇, 偶) |
| BGBG (BGGR) | (奇, 奇) | (奇, 偶) | (偶, 奇) | (偶, 偶) |
| GBGB (GBRG) | (奇, 偶) | (偶, 偶) | (奇, 奇) | (偶, 奇) |

### 2.3 BlcParams 结构体 (设备通信用)

```csharp
private struct BlcParams
{
    public short blkl_r;   // 偏移 0-1: R 通道校正值
    public short blkl_gr;  // 偏移 2-3: Gr 通道校正值
    public short blkl_gb;  // 偏移 4-5: Gb 通道校正值
    public short blkl_b;   // 偏移 6-7: B 通道校正值
}
```

**总大小**: 8 字节

**内存偏移布局**:

| 偏移 (字节) | 字段 | 大小 | 说明 |
|-------------|------|------|------|
| 0x00 - 0x01 | `blkl_r` | 2 字节 | R 通道校正值 |
| 0x02 - 0x03 | `blkl_gr` | 2 字节 | Gr 通道校正值 |
| 0x04 - 0x05 | `blkl_gb` | 2 字节 | Gb 通道校正值 |
| 0x06 - 0x07 | `blkl_b` | 2 字节 | B 通道校正值 |

---

## 三、C++ 算法实现

### 3.1 BlcCal (通道分离)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 27-72)

**函数签名**:
```cpp
ISP_API void BlcCal(
    const void *img_buffer,      // 输入: RAW Bayer 图像数据 (short*)
    int img_width,                // 图像宽度
    int img_height,               // 图像高度
    int polarity_mode,            // Bayer 排列: 0=RGGB, 1=GRBG, 2=BGGR, 3=GBRG
    short **out_data              // 输出: 4 个通道指针 [R, Gr, Gb, B]
);
```

#### 3.1.1 算法原理

**功能**: 将 Bayer 格式的 RAW 图像按像素位置分离为 R、Gr、Gb、B 四个独立通道。

**Bayer 通道分离算法**:

```cpp
// 对每个像素位置 (i, j):
for (i=0; i<h; i++) {
    for (j=0; j<w; j++) {
        tmp = (i % 2) * 2 + (j % 2);  // 计算 2x2 Bayer 块内的位置索引 (0-3)
        
        // 根据 tmp 值和 polarity 分配到对应通道
        switch (tmp) {
            case 0:  // 左上角 (偶行偶列)
                if (polarity == 0) r_array[...] = raw_img[i*w + j];
                else if (polarity == 1) gr_array[...] = raw_img[i*w + j];
                else if (polarity == 2) b_array[...] = raw_img[i*w + j];
                else if (polarity == 3) gb_array[...] = raw_img[i*w + j];
                break;
            // ... 其他 case
        }
    }
}
```

**不同 Polarity 的像素映射**:

| tmp 位置 | 物理位置 | polarity 0 (RGGB) | polarity 1 (GRBG) | polarity 2 (BGGR) | polarity 3 (GBRG) |
|----------|---------|-------------------|-------------------|-------------------|-------------------|
| 0 | 偶行偶列 | R | Gr | B | Gb |
| 1 | 偶行奇列 | Gr | R | Gb | R |
| 2 | 奇行偶列 | Gb | B | Gb | B |
| 3 | 奇行奇列 | B | Gb | R | Gr |

**Polarity 2/3 的通道交换逻辑**:

```cpp
if (polarity == 2 || polarity == 3) {
    // R <-> B 交换
    tmp_array = r_array; r_array = b_array; b_array = tmp_array;
    // Gr <-> Gb 交换
    tmp_array = gr_array; gr_array = gb_array; gb_array = tmp_array;
}
```

这个逻辑处理了 Bayer 模式的翻转情况，确保输出数组始终按 R/Gr/Gb/B 顺序排列。

#### 3.1.2 输出数据格式

| 输出索引 | 通道 | 数组大小 | 说明 |
|---------|------|---------|------|
| `out_data[0]` | R | (w/2) × (h/2) | R 通道像素值 |
| `out_data[1]` | Gr | (w/2) × (h/2) | Gr 通道像素值 |
| `out_data[2]` | Gb | (w/2) × (h/2) | Gb 通道像素值 |
| `out_data[3]` | B | (w/2) × (h/2) | B 通道像素值 |

**注意**: 每个通道输出为原图 1/4 大小，因为 Bayer 格式中每种颜色通道各占 1/4 像素。

**重要**: `BlcCal` **仅做通道分离，不做任何统计计算**。校正值 (平均值/中值) 的计算在 C# 端完成。

### 3.2 BlcImg (图像处理)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 74-164)

**函数签名**:
```cpp
ISP_API void BlcImg(
    const void* img_buffer,      // 输入: RAW Bayer 图像
    short *correction_val,       // 输入: 校正值 [R, Gr, Gb, B]
    int polarity_mode,           // Bayer 排列模式
    int image_width,             // 图像宽度
    int image_height,            // 图像高度
    short *blc_img               // 输出: 校正后图像
);
```

#### 3.2.1 校正值符号扩展

校正值以 10-bit 无符号数形式传入 (0-1023)，但实际校正值可能为负数。采用**偏移编码**方式:

```cpp
range_val = 1024;

blackl_r  = (blackl_r  >= range_val / 2) ? blackl_r  - range_val : blackl_r;
blackl_gr = (blackl_gr >= range_val / 2) ? blackl_gr - range_val : blackl_gr;
blackl_gb = (blackl_gb >= range_val / 2) ? blackl_gb - range_val : blackl_gb;
blackl_b  = (blackl_b  >= range_val / 2) ? blackl_b  - range_val : blackl_b;
```

**编码规则**:

| 输入值 | 实际校正值 | 含义 |
|--------|-----------|------|
| 0 | 0 | 无校正 |
| 1 | 1 | +1 校正 (提亮) |
| 511 | 511 | +511 校正 |
| 512 | 512 - 1024 = **-512** | -512 校正 (压暗) |
| 513 | 513 - 1024 = **-511** | -511 校正 |
| 1023 | 1023 - 1024 = **-1** | -1 校正 |

**可表示范围**: **[-512, +511]**，覆盖 10-bit 像素的全范围。

#### 3.2.2 像素级校正算法

对每个像素 `(i, j)`:

```cpp
// 1. 预先计算四通道校正值
data_adj[0] = raw_img[i*w + j] + blackl_r;
data_adj[1] = raw_img[i*w + j] + blackl_gr;
data_adj[2] = raw_img[i*w + j] + blackl_gb;
data_adj[3] = raw_img[i*w + j] + blackl_b;

// 2. 根据像素位置和 polarity 选择对应校正值
switch (polarity) {
case 0:  // RGGB 排列
    if (i偶 && j偶)  -> blc_img[i*w + j] = CLIP_PIXEL(data_adj[0], 0, 1023);  // R
    if (i偶 && j奇)  -> blc_img[i*w + j] = CLIP_PIXEL(data_adj[1], 0, 1023);  // Gr
    if (i奇 && j偶)  -> blc_img[i*w + j] = CLIP_PIXEL(data_adj[2], 0, 1023);  // Gb
    if (i奇 && j奇)  -> blc_img[i*w + j] = CLIP_PIXEL(data_adj[3], 0, 1023);  // B
    break;
// ... 其他 polarity 同理
}
```

**核心校正公式**:
```
输出像素 = CLIP_PIXEL(输入像素 + 校正值, 0, 1023)
```

**注意**: C++ 端做的是**加法**。因此如果要从像素中减去黑电平偏置，传入的校正值应为**负数**。

#### 3.2.3 CLIP_PIXEL 裁剪宏

```cpp
#define HIGH_VAL_10BIT   1023  // (1<<10) - 1
#define CLIP_PIXEL(val, low, high) (((val) < (low)) ? (low) : (((val) >= (high)) ? (high) : (val)))
```

**裁剪区间**: `[low, high)` — `low` 是闭区间，`high` 是开区间。

**校正示例**:

假设 `blackl_r = 512` (即 -512)，某 R 像素原始值为 600:
```
校正后 = 600 + (-512) = 88
裁剪后 = CLIP_PIXEL(88, 0, 1023) = 88
```

假设 `blackl_r = 10` (即 +10)，某 R 像素原始值为 1020:
```
校正后 = 1020 + 10 = 1030
裁剪后 = CLIP_PIXEL(1030, 0, 1023) = 1023  // 被裁剪到上限
```

---

## 四、数据模型实现

### 4.1 核心属性

**文件**: `d:\jrx\zl\isptool\ThunderSE\DeviceConfig\Isp\BlackLevel.cs`

| 属性 | 类型 | 访问 | 说明 |
|------|------|------|------|
| R | short | get/set | R 通道校正值 |
| Gr | short | get/set | Gr 通道校正值 |
| Gb | short | get/set | Gb 通道校正值 |
| B | short | get/set | B 通道校正值 |
| CorrectValuesArray | short[4] | get only | 四通道数组 (返回直接引用) |
| DeviceModulePos | int | get | 固定值 1 |
| HasChangedParams | bool | get/set | 参数变更标志 |

### 4.2 属性 Setter 统一实现

```csharp
public short R
{
    get { return _correctValuesArray[(int)BlackLevelPixelType.R]; }
    set { SetCorrectValue(BlackLevelPixelType.R, value); }
}

private void SetCorrectValue(BlackLevelPixelType pixelType, short value)
{
    _correctValuesArray[(int)pixelType] = value;
    HasChangedParams = true;
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CorrectValuesArray"));
}
```

**关键行为**:
1. 更新内部数组对应索引的值
2. 设置 `HasChangedParams = true`
3. 触发 `"CorrectValuesArray"` 属性变更通知 (注意: 不是 `"R"` 等具体属性名)

### 4.3 CalBlackLevelData 方法完整流程

```csharp
public void CalBlackLevelData(byte[] nativeRawFileBuffer, 
    Dictionary<BlackLevelPixelType, short[]> blackLevelDataArrays)
{
    // 1. 参数校验
    if (nativeRawFileBuffer == null) throw new ArgumentNullException(...);
    if (blackLevelDataArrays == null) throw new ArgumentNullException(...);
    if (_commonConfig == null) throw new InvalidOperationException(...);

    IntPtr[] ptrArray = null;
    try
    {
        // 2. 分配 5 个非托管内存指针
        ptrArray = new IntPtr[5];
        var arrayLength = _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight / 4;

        for (int i = 0; i < ptrArray.Length; i++)
        {
            ptrArray[i] = Marshal.AllocHGlobal(arrayLength * sizeof(short));
            // 零初始化 (实际上不必要，BlcCal 会覆盖)
            Marshal.Copy(new byte[arrayLength * sizeof(short)], 0, ptrArray[i], ...);
        }

        // 3. 调用 C++ 通道分离
        IspApi.BlcCal(nativeRawFileBuffer, 
            _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight,
            (int)_commonConfig.Bayer, ptrArray);

        // 4. 拷回托管数组
        Marshal.Copy(ptrArray[(int)BlackLevelPixelType.R], 
            blackLevelDataArrays[BlackLevelPixelType.R], 0, arrayLength);
        // ... 其他 3 个通道同理
    }
    finally
    {
        // 5. 释放非托管内存
        if (ptrArray != null)
            for (int i = 0; i < ptrArray.Length; i++)
                Marshal.FreeHGlobal(ptrArray[i]);
    }
}
```

### 4.4 ApplyBlackLevelCorrection 方法

```csharp
public void ApplyBlackLevelCorrection(short[] correctValues, bool isMinus = true)
{
    if (correctValues == null || correctValues.Length != 4)
        throw new ArgumentException("校正值数组必须包含4个元素");

    if (isMinus)
    {
        _correctValuesArray = new short[4];
        for (int i = 0; i < 4; i++)
        {
            _correctValuesArray[i] = (short)-correctValues[i];  // 取负
        }
    }
    else
    {
        _correctValuesArray = (short[])correctValues.Clone();
    }

    HasChangedParams = true;
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CorrectValuesArray"));
}
```

**`isMinus` 参数的作用**:
- **`true` (默认)**: 将输入值取负。因为黑电平校正的本质是**减去**黑电平偏置。
- **`false`**: 直接使用输入值，不做变换。

### 4.5 ProcessRawBuffer 图像处理流程

```csharp
public override void ProcessRawBuffer(ref byte[] imgBuffer)
{
    // 1. 参数校验
    if (imgBuffer == null) throw new ArgumentNullException(...);
    if (_commonConfig == null) throw new InvalidOperationException(...);

    // 2. 分配输出缓冲区
    int pixelCount = _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight;
    short[] outputBuffer = new short[pixelCount];

    // 3. 调用 C++ 校正
    IspApi.BlcImg(imgBuffer, _correctValuesArray, (int)_commonConfig.Bayer,
        _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, outputBuffer);

    // 4. 回写到 imgBuffer
    imgBuffer = new byte[pixelCount * sizeof(short)];
    Buffer.BlockCopy(outputBuffer, 0, imgBuffer, 0, imgBuffer.Length);
}
```

**⚠️ 潜在 Bug**: `BlcImg` 的 P/Invoke 签名中参数顺序为 `(imgHeight, imgWidth)`，但调用时传入 `(ResolutionWidth, ResolutionHeight)`，即**宽高颠倒**。这可能导致在宽高不一致的分辨率下图像处理出错。

---

## 五、UI 实现 (BlcWindow)

### 5.1 窗口整体布局

**文件**: `d:\jrx\zl\isptool\ThunderSE\Ui\SettingWindow\Blc\BlcWindow.xaml`

```
Window (Title="BlcWindow", 1000x811, NoResize, Background="#FFE5E5E5")
  └── Grid (2 行)
        ├── Row 0 (2.5* ≈ 71%): 数据分布区域
        │     ├── Row 0 (26px): Label "数据分布:"
        │     ├── Row 1 (1*): 上半图表行
        │     │     ├── Col 0: Chart "R" — AreaSeries 绑定 RPixelData
        │     │     └── Col 1: Chart "GR" — AreaSeries 绑定 GRPixelData
        │     └── Row 2 (1*): 下半图表行
        │           ├── Col 0: Chart "GB" — AreaSeries 绑定 GBPixelData
        │           └── Col 1: Chart "B" — AreaSeries 绑定 BPixelData
        │
        └── Row 1 (1* ≈ 29%): 统计+校正+文件路径区域
              ├── StackPanel (垂直排列)
              │     ├── Label "统计数据:"
              │     ├── 平均值行: "avg__BLC__R : {AvgBlackLevelR}" ... (4 个)
              │     ├── 中值行: "median__BLC__R : {MedianBlackLevelR}" ... (4 个)
              │     ├── Label "减去"
              │     └── 校正操作行:
              │           ├── ComboBox (中值/平均值) — 绑定 SelectedCorrection
              │           └── Button "应用" — 绑定 ApplyCorrectionCommand
              │
              └── StackPanel (Grid.Row=1)
                    ├── Label "Raw文件路径:"
                    ├── TextBox (只读, 灰色背景) — 绑定 RawFile
                    └── Button "打开..." — 绑定 OpenRawFileCommand
```

### 5.2 图表配置

**四个面积图 (AreaSeries Chart)**:

| 图表 | 标题 | 绑定属性 | X 轴范围 |
|------|------|---------|---------|
| R | R | `RPixelData` | 0-1024 |
| GR | GR | `GRPixelData` | 0-1024 |
| GB | GB | `GBPixelData` | 0-1024 |
| B | B | `BPixelData` | 0-1024 |

**每个 Chart 的配置**:
- `BorderThickness="0"` (无边框)
- `LegendStyle.Width=0` (隐藏图例)
- X 轴: `LinearAxis`，固定范围 0-1024 (对应 10-bit 像素值)
- `AreaSeries.ItemsSource` 绑定到 `Dictionary<int, int>`
  - `IndependentValuePath="Key"` (X 轴 = 像素值 0-1023)
  - `DependentValuePath="Value"` (Y 轴 = 像素计数)

### 5.3 完整操作流程

```
[窗口加载]
    │
    ▼
Window_Loaded
    │
    └── viewModel.OpenRawFileCommand.Execute(null)
            │
            ▼
        自动弹出文件选择对话框


[用户选择 RAW 文件]
    │
    ▼
OpenRawFileAndCalcBlackLevel()
    │
    ├── OpenFileDialog (*.raw)
    │    └── 用户取消 → return
    │
    ├── RawFile = openFileDialog.FileName
    │    └── RaisePropertyChanged("RawFile")
    │
    └── _nativeRawFileBuffer = File.ReadAllBytes(FileName)
            │
            ▼
        await Task.Run(() => CalBlackLevelData(_nativeRawFileBuffer))
            │
            ├── BlackLevel.CalBlackLevelData(rawBuffer, _blackLevelDataArrays)
            │    │
            │    ├── 分配 5 个非托管内存指针
            │    │
            │    ├── IspApi.BlcCal(...) → 分离 4 通道
            │    │
            │    └── Marshal.Copy 回托管数组
            │
            └── UpdatePixelData()
                 │
                 ├── RPixelData = BuildPixelData(R)
                 ├── GRPixelData = BuildPixelData(Gr)
                 ├── GBPixelData = BuildPixelData(Gb)
                 └── BPixelData = BuildPixelData(B)
            │
            ▼ (await 后回到 UI 线程)
        计算 4 通道中值 (GetMedianPixelValue)
        计算 4 通道平均值 (LINQ .Average())
            │
            ▼
        触发 8 个统计属性的 RaisePropertyChanged
            │
            ▼
        UI 更新: 4 个图表 + 8 个统计值 Label


[用户选择校正模式并点击"应用"]
    │
    ▼
ApplyCorrection()
    │
    ├── 检查 _nativeRawFileBuffer != null
    │    └── null → MessageBox("请先加载 RAW 文件")
    │
    ├── 根据 SelectedCorrection 选择校正值:
    │    │
    │    ├── 0 (中值):
    │    │    correctionValueArray = [MedianBlackLevelR, MedianBlackLevelGR,
    │    │                            MedianBlackLevelGB, MedianBlackLevelB]
    │    │
    │    └── 1 (平均值):
    │         correctionValueArray = [AvgBlackLevelR, AvgBlackLevelGR,
    │                                 AvgBlackLevelGB, AvgBlackLevelB]
    │
    ├── BlackLevel.ApplyBlackLevelCorrection(correctionValueArray)
    │    └─ 内部将值取负后存入 _correctValuesArray
    │    └─ 触发 PropertyChanged("CorrectValuesArray")
    │
    └── Task.Run(() => {
            // 1. 创建 Raw 缓冲副本
            correctingRawBuffer = _nativeRawFileBuffer 的深拷贝

            // 2. 执行 BLC 处理
            BlackLevel.ProcessRawBuffer(ref correctingRawBuffer)
                │
                └── IspApi.BlcImg(...) → 应用校正到每个像素

            // 3. 切回 UI 线程重新计算图表
            Dispatcher.Invoke(() => {
                CalBlackLevelData(correctingRawBuffer);
            })
        })
            │
            ▼
        图表和统计数据更新 (显示校正后的效果)
```

### 5.4 核心算法详解

#### 5.4.1 BuildPixelData — 直方图生成

```csharp
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

**算法**:
1. 对 `_blackLevelDataArrays[type]` 数组 (大小为 `W×H/4`) 按值分组
2. `GroupBy(i => i)` 将相同像素值归为一组
3. 字典 `Key` = 像素值 (0-1023)，`Value` = 该值出现的次数

**性能**: 对于 1920x1080 分辨率，数组大小为 518,400 元素，`GroupBy` 操作约需 100-200ms。

#### 5.4.2 GetMedianPixelValue — 中值计算 (直方图法)

```csharp
private short GetMedianPixelValue(short[] pixelValueArray)
{
    // 1. 构建 1024 个桶的直方图
    int[] histogram = new int[1024];
    foreach (short val in pixelValueArray)
    {
        if (val >= 0 && val < 1024)
            histogram[val]++;
    }

    // 2. 计算中值位置
    int count = pixelValueArray.Length;
    int medianIndex1 = (count - 1) / 2;  // 下中位
    int medianIndex2 = count / 2;         // 上中位

    // 3. 累积查找
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

**算法详解**:

| 步骤 | 操作 | 时间复杂度 |
|------|------|-----------|
| 1 | 构建 1024 桶直方图 | O(N) |
| 2 | 计算中位索引 | O(1) |
| 3 | 累积查找中位值 | O(1024) |
| **总计** | | **O(N + 1024)** |

**对比排序法**: 排序法为 O(N log N)，对于 50 万元素约需 1000 万次操作，直方图法仅需 50 万 + 1024 次操作，**快 20 倍以上**。

**偶数情况处理**: 当元素个数为偶数时，取两个中间位置的均值。

---

## 六、LSC IQ 窗口实现

**注意**: BLC 模块**没有**专用的 IQ 分析窗口。与 LSC/AWB/YGamma 不同，BLC 的质量评估直接通过 BlcWindow 中的图表和统计数据完成。

**评估方式**:
- **直方图分布**: 观察四通道像素值分布是否集中在某个值附近
- **平均值/中值**: 数值越小表示黑电平越接近零
- **校正后对比**: 点击"应用"后查看校正后的直方图是否向零点移动

---

## 七、数据序列化

### 7.1 XML 序列化 (配置文件)

```csharp
public override XmlElement SerializeToXmlElement(XmlDocument xmlDoc)
{
    var xmlElement = xmlDoc.CreateElement("Blc");

    XmlElement blcRNode = xmlDoc.CreateElement("BlcR");
    blcRNode.AppendChild(xmlDoc.CreateTextNode(R.ToString()));
    xmlElement.AppendChild(blcRNode);

    // ... Gr, Gb, B 同理

    return xmlElement;
}
```

**XML 格式**:
```xml
<Blc>
    <BlcR>64</BlcR>
    <BlcGr>64</BlcGr>
    <BlcGb>64</BlcGb>
    <BlcB>64</BlcB>
</Blc>
```

### 7.2 XML 反序列化

```csharp
public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
{
    var blcNode = ispToolDataNode["Blc"];

    R = XmlHelper.GetNodeShort(blcNode, "BlcR");
    Gr = XmlHelper.GetNodeShort(blcNode, "BlcGr");
    Gb = XmlHelper.GetNodeShort(blcNode, "BlcGb");
    B = XmlHelper.GetNodeShort(blcNode, "BlcB");
}
```

**⚠️ 风险**: 如果 `ispToolDataNode["Blc"]` 返回 `null`，`XmlHelper.GetNodeShort(null, "BlcR")` 会抛出 `NullReferenceException`。

### 7.3 二进制序列化 (设备烧录用)

**Getter**:
```csharp
BlcParams blcParams = new BlcParams() {
    blkl_r = R, blkl_gr = Gr, blkl_gb = Gb, blkl_b = B
};
int size = Marshal.SizeOf(blcParams);  // 8 字节
byte[] arr = new byte[size];
IntPtr ptr = Marshal.AllocHGlobal(size);
try {
    Marshal.StructureToPtr(blcParams, ptr, false);
    Marshal.Copy(ptr, arr, 0, size);
} finally {
    Marshal.FreeHGlobal(ptr);
}
return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
```

**Setter**:
```csharp
byte[] data = value[DeviceModulePos];
if (data.Length != 8) throw new ArgumentException(...);

IntPtr ptr = Marshal.AllocHGlobal(8);
try {
    Marshal.Copy(data, 0, ptr, 8);
    BlcParams blcParams = (BlcParams)Marshal.PtrToStructure(ptr, typeof(BlcParams));
    R = blcParams.blkl_r;
    Gr = blcParams.blkl_gr;
    Gb = blcParams.blkl_gb;
    B = blcParams.blkl_b;
} finally {
    Marshal.FreeHGlobal(ptr);
}
```

**输出**: 单元素字典 `{1, byte[8]}`，键为 `DeviceModulePos = 1`

---

## 八、已知问题清单

### 8.1 高严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B1 | BlcImg 宽高参数颠倒 | 宽高不一致时图像处理出错 | 修正 P/Invoke 签名或调用顺序 |
| B2 | DeserializeFromXmlElement 缺少 null 检查 | NullReferenceException | 添加 XmlHelper 安全解析 |
| B3 | CorrectValuesArray 返回直接引用 | 外部修改不触发通知 | 返回副本或只读包装 |

### 8.2 中严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B4 | PropertyChanged 属性名不一致 | UI 绑定 R/Gr/Gb/B 收不到通知 | 改为通知具体属性名 |
| B5 | CalBlackLevelData 多余零初始化 | 性能浪费 | 移除不必要的 Marshal.Copy |
| B6 | 窗口关闭未调用 Cleanup | 可能内存泄漏 | 显式调用 ICleanup.Cleanup |

### 8.3 低严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B7 | ProcessRgbBuffer 未实现 | 仅支持 RAW 格式 | 设计决定，可接受 |
| B8 | 图表 X 轴硬编码 0-1024 | 不支持其他位深度 | 从 CommonConfig 读取位深度 |
| B9 | GetMedianPixelValue 跳过负值 | 异常数据中值偏差 | 处理负值情况 |

---

## 九、关键文件清单

### 数据模型

| 文件 | 路径 | 职责 |
|------|------|------|
| BlackLevel.cs | `DeviceConfig/Isp/BlackLevel.cs` | BLC 数据模型、算法封装 |
| ProcessStep.cs | `DeviceConfig/Isp/ProcessStep.cs` | 抽象基类 |
| CommonConfig.cs | `DeviceConfig/Isp/CommonConfig.cs` | 公共配置 (分辨率、Bayer) |
| IspApi.cs | `DeviceConfig/Isp/IspApi.cs` | P/Invoke 声明 |

### UI

| 文件 | 路径 | 职责 |
|------|------|------|
| BlcWindow.xaml | `Ui/SettingWindow/Blc/BlcWindow.xaml` | BLC 调试窗口 XAML |
| BlcWindow.xaml.cs | `Ui/SettingWindow/Blc/BlcWindow.xaml.cs` | BLC 窗口代码隐藏 |
| BlcWindowViewModel.cs | `Ui/SettingWindow/Blc/BlcWindowViewModel.cs` | BLC 窗口 ViewModel |

### C++ 算法

| 文件 | 路径 | 职责 |
|------|------|------|
| IQ.cpp | `IspApi/source/IQ.cpp` (行 27-164) | BlcCal (45 行), BlcImg (90 行) |
| Export.h | `IspApi/source/Export.h` | C 接口导出 |
| IQ.h | `IspApi/include/IQ.h` | 宏定义 (CLIP_PIXEL, HIGH_VAL_10BIT) |

---

## 十、附录

### 10.1 Bayer 模式与 Polarity 映射

| Bayer 模式 | Polarity 值 | R 位置 | Gr 位置 | Gb 位置 | B 位置 |
|-----------|:---:|---------|---------|---------|--------|
| RGRG (RGGB) | 0 | (偶, 偶) | (偶, 奇) | (奇, 偶) | (奇, 奇) |
| GRGR (GRBG) | 1 | (偶, 奇) | (偶, 偶) | (奇, 奇) | (奇, 偶) |
| BGBG (BGGR) | 2 | (奇, 奇) | (奇, 偶) | (偶, 奇) | (偶, 偶) |
| GBGB (GBRG) | 3 | (奇, 偶) | (偶, 偶) | (奇, 奇) | (奇, 奇) |

### 10.2 校正值编码规则

| 输入值 (short) | 实际校正值 | 物理含义 |
|---------------|-----------|---------|
| 0 | 0 | 无校正 |
| 64 | 64 | +64 (提亮) |
| 511 | 511 | +511 (最大提亮) |
| 512 | -512 | -512 (最大压暗) |
| 960 | -64 | -64 (常见黑电平偏置) |
| 1023 | -1 | -1 (最小压暗) |

### 10.3 色彩空间转换公式

| 转换 | 公式 |
|------|------|
| CLIP_PIXEL | `val < low ? low : (val >= high ? high : val)` |
| 像素值范围 | `[0, 1023]` (10-bit) |
| 校正值范围 | `[-512, +511]` (偏移编码) |

---

**文档版本**: v1.0  
**创建日期**: 2026年4月8日  
**分析依据**: 项目源码完整阅读与交叉引用  
**文档状态**: 完整

---

**文档结束**

本文档基于 ThunderSE 项目实际代码深入分析生成，涵盖 BLC 模块的完整参数定义、C++ 算法原理 (BlcCal/BlcImg)、数据序列化规范、UI 交互设计 (BlcWindow) 和设备通信协议。可作为 BLC 模块开发、调试、测试和维护的参考文档。
