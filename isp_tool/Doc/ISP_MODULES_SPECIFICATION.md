# ThunderSE ISP 模块详细需求规格说明

## 文档信息

| 项目 | 内容 |
|------|------|
| **文档版本** | v1.0 |
| **创建日期** | 2026年4月8日 |
| **适用范围** | ThunderSE 项目 ISP 参数调试与烧录模块 |
| **分析依据** | 项目源码完整阅读与交叉引用 |

---

## 一、ISP 模块总体架构

### 1.1 模块枚举定义

**文件**: `ThunderSE/DeviceConfig/Isp/Processor.cs`

```csharp
public enum IspModule
{
    AE       = 0,   // 自动曝光
    Blc      = 1,   // 黑电平校正
    Lsc      = 2,   // 镜头阴影校正
    Ddc      = 3,   // 缺陷像素校正
    Awb      = 4,   // 自动白平衡
    Ccm      = 5,   // 颜色校正矩阵
    Dgain    = 6,   // 数字增益
    YGamma   = 7,   // 亮度 Gamma
    RgbGamma = 8,   // RGB Gamma
    Ch       = 9,   // 色彩增强
    Vde      = 10,  // 视频动态增强
    Ee       = 11,  // 边缘增强
    Cfd      = 12,  // (未实例化)
    Saj      = 13,  // 抗锯齿
}
```

### 1.2 基类 ProcessStep 接口

**文件**: `ThunderSE/DeviceConfig/Isp/ProcessStep.cs`

所有 ISP 模块继承自 `ProcessStep` 抽象类，必须实现以下抽象成员：

| 成员 | 类型 | 说明 |
|------|------|------|
| `DeviceModulePos` | `int` | 模块在设备寄存器中的位置索引 |
| `HasChangedParams` | `bool` | 标记参数是否已变更（用于增量写入） |
| `PreviousStepsEnables` | `Dictionary<IspModule, bool>` | 前置模块的启用状态 |
| `SetCommonConfig(CommonConfig)` | `void` | 设置公共配置引用 |
| `ProcessRawBuffer(ref byte[])` | `void` | 处理 Raw Bayer 图像缓冲 |
| `ProcessRgbBuffer(ref byte[])` | `void` | 处理 RGB 图像缓冲 |
| `ParamsDataCollection` | `Dictionary<int, byte[]>` | 参数序列化/反序列化（设备烧录用） |
| `SerializeToXmlElement(XmlDocument)` | `XmlElement` | XML 序列化（配置文件用） |
| `DeserializeFromXmlElement(XmlElement)` | `void` | XML 反序列化 |

### 1.3 ISP 处理管线顺序

```
Raw Bayer 输入
    │
    ▼
┌─────┐    ┌─────┐    ┌─────┐    ┌─────┐    ┌─────┐
│ AE  │ -> │ BLC │ -> │ LSC │ -> │ DDC │ -> │ AWB │
│ (0) │    │ (1) │    │ (2) │    │ (3) │    │ (4) │
└─────┘    └─────┘    └─────┘    └─────┘    └─────┘
 配置      黑电平      镜头阴影    坏点校正    自动白平衡
 参数      校正        校正                   校正
                                      │
                                      ▼
                            Demosaic (去马赛克)
                                      │
                                      ▼
┌─────┐    ┌─────┐    ┌─────┐    ┌─────┐    ┌─────┐
│ SAJ │ -> │ EE  │ -> │ VDE │ -> │ CH  │ -> │ CCM │
│(13) │    │(11) │    │(10) │    │ (9) │    │ (5) │
└─────┘    └─────┘    └─────┘    └─────┘    └─────┘
 抗锯齿    边缘增强    动态增强    色彩增强    颜色矩阵
                                      │
                                      ▼
┌────────┐    ┌────────┐
│YGamma  │ -> │RGBGamma│ -> ... 输出
│  (7)   │    │  (8)   │
└────────┘    └────────┘
Gamma校正
```

**注意**: 实际处理顺序由 `Processor.RawFileProcessSteps` 和 `Processor.RgbFileProcessSteps` 定义，可能与模块枚举值顺序不同。

### 1.4 模块依赖关系

| 模块 | 前置依赖模块 | 依赖原因 |
|------|------------|---------|
| BLC | 无 | 基础校正，无前置依赖 |
| LSC | BLC | 需要在黑电平校正后的数据上计算镜头阴影 |
| DDC | 无 | 可独立运行 |
| AWB | BLC, LSC | 需要在 Raw 数据校正后统计白平衡 |
| CCM | 无 | RGB 域矩阵，可独立运行 |
| YGamma | BLC, LSC, AWB | 需要在亮度数据准确的基础上做 Gamma 映射 |
| CH | 无 | 色彩增强可独立运行 |
| VDE | 无 | 视频增强可独立运行 |
| EE | 无 | 边缘增强可独立运行 |
| SAJ | 无 | 抗锯齿可独立运行 |
| AE | 无 | 仅参数配置，不处理图像 |

---

## 二、核心模块详细规格

### 2.1 BLC (黑电平校正)

**文件**: `ThunderSE/DeviceConfig/Isp/BlackLevel.cs`  
**DeviceModulePos**: 1

#### 2.1.1 参数定义

| 参数 | 类型 | 默认值 | 取值范围 | 说明 |
|------|------|--------|----------|------|
| R | short | 0 | -512 ~ 511 | R 通道校正值 |
| Gr | short | 0 | -512 ~ 511 | Gr 通道校正值 |
| Gb | short | 0 | -512 ~ 511 | Gb 通道校正值 |
| B | short | 0 | -512 ~ 511 | B 通道校正值 |
| CorrectValuesArray | short[4] | {0,0,0,0} | 同上 | 四通道数组 (只读) |

**内部结构**:
```csharp
struct BlcParams {
    short blkl_r;   // offset 0
    short blkl_gr;  // offset 2
    short blkl_gb;  // offset 4
    short blkl_b;   // offset 6
};  // 总计 8 字节
```

#### 2.1.2 核心方法

**`CalBlackLevelData(byte[] rawBuffer, Dictionary<BlackLevelPixelType, short[]> channels)`**

业务流程:
1. 分配 5 个非托管内存指针（4 个用于 R/Gr/Gb/B，1 个预留）
2. 每个指针分配 `Width * Height / 4 * sizeof(short)` 字节
3. 调用 `IspApi.BlcCal(rawBuffer, W, H, (int)Bayer, ptrArray)`
4. C++ 端按 Bayer 模式分离四通道数据
5. `Marshal.Copy` 回托管数组
6. `finally` 块释放非托管内存

**`ApplyBlackLevelCorrection(short[] values, bool isMinus = true)`**

业务流程:
1. 校验 `values` 非空且长度为 4
2. 若 `isMinus == true`: 对每个元素取负后存入内部数组
3. 若 `isMinus == false`: 直接克隆数组
4. 设置 `HasChangedParams = true`，触发 `PropertyChanged`

**`ProcessRawBuffer(ref byte[] imgBuffer)`**

业务流程:
1. 计算 `pixelCount = ResolutionWidth * ResolutionHeight`
2. 分配 `short[pixelCount]` 输出缓冲区
3. 调用 `IspApi.BlcImg(imgBuffer, _correctValuesArray, (int)Bayer, W, H, outputBuffer)`
4. C++ 端:
   - 校正值符号扩展: `val >= 512 ? val - 1024 : val`
   - 对每个像素: 根据坐标+Bayer 模式选择对应通道校正值
   - `output = CLIP_PIXEL(pixel + correction, 0, 1023)`
5. 将 `short[]` 输出转换为 `byte[]` 覆盖原 `imgBuffer`

#### 2.1.3 IspApi 交互

| 函数 | 参数 | 功能 |
|------|------|------|
| `BlcCal(img, W, H, polarity, outData[5])` | 输入 Raw Bayer | 分离四通道数据 |
| `BlcImg(img, correction[4], polarity, W, H, outImg)` | 输入 Raw + 校正值 | 应用黑电平校正 |

**注意**: `BlcCal` 仅做通道分离，统计值（平均值/中值）在 C# 端计算。

#### 2.1.4 序列化格式

**XML 格式**:
```xml
<Blc>
    <BlcR>-50</BlcR>
    <BlcGr>-48</BlcGr>
    <BlcGb>-49</BlcGb>
    <BlcB>-52</BlcB>
</Blc>
```

**二进制格式**: 8 字节 (`BlcParams` 结构体)，键为 `DeviceModulePos = 1`

#### 2.1.5 调试工具 (BlcWindow)

**统计数据计算**:
- **平均值**: LINQ `.Average()` 计算算术平均
- **中值**: 直方图法 (1024 个桶)，O(n) 时间复杂度

**图表数据生成**:
```csharp
Dictionary<int, int> BuildPixelData(BlackLevelPixelType type) {
    // Key = 像素值 (0-1023), Value = 出现频次
    return _blackLevelDataArrays[type].GroupBy(i => i)
        .ToDictionary(g => g.Key, g => g.Count());
}
```

**校正方式选择**:
- **中值 (SelectedCorrection = 0)**: 使用四通道中值构建校正数组
- **平均值 (SelectedCorrection = 1)**: 使用四通道平均值构建校正数组

#### 2.1.6 与 CommonConfig 的依赖

| 依赖项 | 用途 |
|--------|------|
| `ResolutionWidth` | 计算像素总数 |
| `ResolutionHeight` | 计算像素总数 |
| `Bayer` | Bayer 模式转 polarity 参数 (RGRG=0, GRGR=1, BGBG=2, GBGB=3) |

---

### 2.2 LSC (镜头阴影校正)

**文件**: `ThunderSE/DeviceConfig/Isp/LensShading.cs`  
**DeviceModulePos**: 2

#### 2.2.1 参数定义

| 参数 | 类型 | 默认值 | 取值范围 | 说明 |
|------|------|--------|----------|------|
| CorrectionData | short[] | 256 (全) | 0 ~ 1023 | 网格校正系数数组 |
| LscMode | enum | Rgb | Y / Rgb | 校正模式 |

**网格尺寸计算**:
```csharp
const int blockSizeX = 16;  // 横向步长 (Bayer 半分辨率)
const int blockSizeY = 32;  // 纵向步长

blockW = (ResolutionWidth / 2 + blockSizeX - 1) / blockSizeX + 1;
blockH = (ResolutionHeight / 2 + blockSizeY - 1) / blockSizeY + 1;
totalSize = 4 * blockH * blockW;  // 4 通道
```

**示例** (1280x720):
```
blockW = (640 + 15) / 16 + 1 = 42
blockH = (360 + 31) / 32 + 1 = 13
totalSize = 4 * 13 * 42 = 2184
```

**数据布局**:
```
[通道0: blockH x blockW] [通道1: blockH x blockW] 
[通道2: blockH x blockW] [通道3: blockH x blockW]
```

通道映射取决于 Bayer 格式: `s = (i%2)*2 + (j%2)` (0=R/Gr/Gb/B 之一)

#### 2.2.2 LscMode 模式区别

| 模式 | 取值 | 工作原理 | 适用场景 |
|------|------|---------|---------|
| **Y 模式** | 0 | 先将 Bayer 转换为亮度图 (Y = R*77 + G*150 + B*29)，所有通道共用同一权重表 | 效率优先，色彩精度要求不高 |
| **Rgb 模式** | 1 | 四通道分离，分别计算各通道的中心参考亮度和网格增益 | **推荐**，色彩精度高 |

#### 2.2.3 核心方法

**`CalWeight(byte[] rawBuffer, LscMode mode, int pointX, int pointY)`**

Y 模式算法流程:
1. 构建 Y 亮度图: 每 2x2 Bayer 块计算一个 Y 值
   ```
   Y = (R*77 + (G1+G2)/2*150 + B*29) / 256
   ```
2. 计算中心参考亮度 `y_max`:
   - 取 `(pointX, pointY)` 周围 17x17=289 个像素
   - 排序取中位数 `mean_val`
   - 去噪: 将偏离中位数 > 50 的像素替换为中位数
   - `y_max` = 去噪后的平均值
3. 遍历所有网格边界点:
   - 取 9x9=81 个像素
   - 排序取中位数，去噪计算 `y_tmp`
   - `增益 = y_max / y_tmp * 256`
   - 该增益同时写入 4 个通道

Rgb 模式算法流程:
1. 分离 Bayer 为 4 个通道 (半分辨率图)
2. 计算中心参考值 `mid_val[4]` (每通道 9x9 中位数)
3. 遍历所有网格边界点:
   - 取 5x5=25 个像素 (半分辨率)
   - 各通道增益 = `mid_val[k] / tmp_val[k] * 256`
   - 写入对应通道

**`ProcessRawBuffer(ref byte[] imgBuffer)`**

C++ `LscImg` 双线性插值算法:
```cpp
for each pixel (i, j):
    // 确定 Bayer 通道
    xs = j % 2;
    ys = i % 2;
    s = ys * 2 + xs;  // 0-3

    // 确定增益网格位置
    block_y = (i / 2) / block_size_y;
    block_x = (j / 2) / block_size_x;
    weight_y = (i / 2) % block_size_y;
    weight_x = (j / 2) % block_size_x;

    // 双线性插值 (4 个相邻节点)
    tmp1 = w[by,   bx  ] * (bsx-wx) * (bsy-wy);
    tmp2 = w[by+1, bx  ] * wy * (bsx-wx);
    tmp3 = w[by,   bx+1] * (bsy-wy) * wx;
    tmp4 = w[by+1, bx+1] * wy * wx;

    t = (tmp1 + tmp2 + tmp3 + tmp4) / bsy / bsx;

    // 应用增益
    output = CLIP_PIXEL(t * input / 256, 0, 1023);
```

**`CalcIQ(byte[] buffer, ref ColorShadingIQResult cs, ref LensShadingIQResult ls)`**

采样位置 (5 个 5x5 区域):
| 索引 | 位置 | 坐标 |
|------|------|------|
| 0 | 左上角 | (2, 2) |
| 1 | 右上角 | (2, w-3) |
| 2 | 左下角 | (h-3, 2) |
| 3 | 右下角 | (h-3, w-3) |
| 4 | 中心 | (h/2-1, w/2-1) |

评估指标:

**ColorShadingIQResult** (16 个 double):
| 指标 | 含义 | 理想值 | 合格范围 |
|------|------|--------|---------|
| `cr_tl ~ cr_br` (4) | 四角 R/G 比率相对于中心的比值 | 1.0 | 0.85 ~ 1.20 |
| `cb_tl ~ cb_br` (4) | 四角 B/G 比率相对于中心的比值 | 1.0 | 0.85 ~ 1.20 |
| `rg_tl_rate ~ rg_br_rate` (4) | R/G 偏差百分比 | 0% | - |
| `bg_tl_rate ~ bg_br_rate` (4) | B/G 偏差百分比 | 0% | - |

**LensShadingIQResult** (8 个 double):
| 指标 | 含义 | 理想值 | 合格范围 |
|------|------|--------|---------|
| `ly_tl ~ ly_br` (4) | 四角 Y 亮度相对于中心的比值 | 1.0 | 0.80 ~ 1.10 |
| `y_tl_rate ~ y_br_rate` (4) | Y 亮度偏差百分比 | 0% | - |

#### 2.2.4 中心点选择交互

坐标转换公式:
```csharp
const int LSC_SAFE_MARGIN = 10;

rawX = dotPos.X * horizontalScale + LSC_SAFE_MARGIN;
rawY = dotPos.Y * verticalScale + LSC_SAFE_MARGIN;

// 合法边界钳位
maxX = ResolutionWidth - LSC_SAFE_MARGIN;
maxY = ResolutionHeight - LSC_SAFE_MARGIN;
paramX = Clamp(rawX, LSC_SAFE_MARGIN, maxX);
paramY = Clamp(rawY, LSC_SAFE_MARGIN, maxY);
```

#### 2.2.5 分辨率变化重新初始化

```
CommonConfig.ResolutionWidth/Height 变化
    │
    ▼
LensShading.OnCommonConfigPropertyChanged()
    │
    ▼
_currentExpectedSize = 0  (标记失效)
    │
    ▼
下次访问 CorrectionData → EnsureCorrectionDataInitialized()
    │
    ▼
CalculateRequiredLscSize() → 新大小
    │
    ▼
分配新数组 + 初始化 256 (1.0x 增益)
```

#### 2.2.6 序列化格式

**XML 格式**:
```xml
<Lsc>
    <Lsc_Weight>256,256,256,...,256</Lsc_Weight>
</Lsc>
```

**二进制格式**: `CorrectionData.Length * 2` 字节，键为 `DeviceModulePos = 2`

---

### 2.3 AWB (自动白平衡)

**文件**: `ThunderSE/DeviceConfig/Isp/AutoWhiteBalance.cs`  
**DeviceModulePos**: 4

#### 2.3.1 参数完整定义

**核心参数**:

| 参数 | 类型 | 默认值 | 范围 | 说明 |
|------|------|--------|------|------|
| Seg_Mode | int | 3 | 0-7 | 分段模式，决定统计区间分段数量 |
| Awb_Weight_In | int | 7 | 0-15 | 区间内权重系数 |
| Awb_Weight_Out | int | 3 | 0-15 | 区间外权重系数 |
| RGainStart | int | 170 | 0-1024 | 统计曲线 X 轴起始 R 增益值 |
| RGainMin | int | 170 | 0-1024 | R 增益下限钳位 |
| RGainMax | int | 440 | 0-1024 | R 增益上限钳位 |
| Awb_YMin | int | 16 | 0-255 | 有效亮度最小值 |
| Awb_YMax | int | 192 | 0-255 | 有效亮度最大值 |
| Awb_Yuv_Mod_En | int | 0 | 0/1 | 0=RAW域统计, 1=YUV域统计 |
| Awb_Ycbcr_Th | byte | 10 | 0-255 | YCbCr 联合阈值 |
| Awb_De_High_Red_Class | int | 3 | 0-15 | 高红区域分类等级 |
| Awb_De_High_Blue_Class | int | 3 | 0-15 | 高蓝区域分类等级 |
| Awb_De_High_Red_Rate | int | 0 | 0-15 | 高红区域比例阈值 |
| Awb_De_High_Blue_Rate | int | 0 | 0-15 | 高蓝区域比例阈值 |

**阈值数组**:

| 参数 | 大小 | 默认值 | 说明 |
|------|------|--------|------|
| Awb_Cb_Th | byte[8] | {8,16,24,32,40,48,48,48} | Cb 阈值表 |
| Awb_Cr_Th | byte[8] | {8,16,24,32,40,48,48,48} | Cr 阈值表 |
| Awb_Cbcr_Th | byte[8] | {12,24,36,48,60,72,72,72} | CbCr 联合阈值表 |

**数据表**:

| 参数 | 大小 | 说明 |
|------|------|------|
| Awb_Stat_Tab | byte[128] | 统计表 (4段 x 32点) |
| seg_gain | short[24] | 分段增益表 (8段 x 3通道) |
| rgain / ggain / bgain | int | 自动计算的 RGB 增益 |

#### 2.3.2 关键机制

**Seg_Mode (分段模式)**:
- 决定白点统计被分为多少个 R 增益区间段 (最多 4 段)
- 影响 `Awb_Stat_Tab` 的有效数据量
- 影响 `AWB_Gain_Soft_Cal` 的增益计算方式

**RGainStart / RGainMin / RGainMax 关系**:
```
RGainStart → 统计曲线 X 轴起始点
RGainEnd   → X 轴终点 = RGainStart + 16 * 31 = RGainStart + 496
RGainMin   → 最终 R 增益下限钳位
RGainMax   → 最终 R 增益上限钳位
```

**Weight_In / Weight_Out 权重**:
- 仅在 RAW 域模式 (`Yuv_Mod_En = 0`) 下有效
- 权重比 = `7 : 3`，区间内像素权重是区间外的 2.33 倍

**YMin / YMax 亮度窗口**:
- 排除极暗区域 (噪声大) 和过曝区域 (色度信息丢失)
- RAW 域和 YUV 域模式均使用

#### 2.3.3 统计模式区别

**RAW 域模式 (Yuv_Mod_En = 0)**:
```
调用: IspApi.AWBStatistic(raw, Bayer, W, H, seg_mode, awb_stat_tab, 
                          weight_in, weight_out, rg_start, rg_min, rg_max, ymin, ymax, wp_output)

特点: 使用统计表 awb_stat_tab 和权重系数进行白点筛选
```

**YUV 域模式 (Yuv_Mod_En = 1)**:
```
调用: IspApi.AWBStatistic_Yuv(raw, Bayer, W, H, seg_mode, ymin, ymax, 
                              cb_th[8], cr_th[8], cbcr_th[8], ycbcr_th, wp_output)

特点: 使用 Cb/Cr 阈值表进行色度白点筛选，不使用统计表和权重系数
```

#### 2.3.4 核心方法

**`CalcGainValue(byte[] rawImg)`**:

```
1. 初始化 returnData[3] 和 wp_output[128]

2. IF Yuv_Mod_En != 0 (YUV域):
       调用 UpdateAwbStatTab() 更新统计表
       调用 AWBStatistic() → wp_output[128]
   ELSE (RAW域):
       调用 AWBStatistic_Yuv() → wp_output[128]

3. 调用 AWB_Gain_Soft_Cal(wp_output, seg_mode)
   → returnData[0]=R_gain, returnData[1]=G_gain, returnData[2]=B_gain

4. 返回 returnData
```

**`UpdateAwbStatTab(StatisticData)`**:

```
1. 创建 128 字节临时数组，清零
2. 遍历 StatisticData 中的每条曲线 (最多 4 条)
3. 遍历每条曲线中的 32 个点
4. 将 item.Value 转为 byte 填入 tmpAwbStatTab[i]
5. 赋值给 Awb_Stat_Tab
```

**`CalcIQ(byte[] buffer, int[] x, y, w, h, out rgIq, out bgIq)`**:

```
1. 分配 3 个 short[W*H] 非托管内存 (R/G/B 三通道)
2. 调用 DemosaicImg() 去马赛克
3. 调用 AWB_IQ() 在选取区域上计算 IQ 值
4. 释放内存
5. 合格范围: r_gain 和 b_gain 均在 [0.92, 1.08]
```

#### 2.3.5 数据结构

**StatisticData**:
```csharp
// 外层: 最多 4 个 WhiteBalanceStatCollection (对应最多 4 段)
// 内层: 每个 WhiteBalanceStatCollection 包含 32 个键值对
//   Key   = R 增益值 = RGainStart + 16 * bin_index
//   Value = 该增益 bin 上的统计计数值

using WhiteBalanceStatCollection = ObservableCollection<KeyValuePair<double, double>>;
ObservableCollection<WhiteBalanceStatCollection> StatisticData;
```

**GainData**:
```csharp
// Key: 文件名 (不含扩展名), Value: (Rgain, Bgain/4)
Dictionary<string, KeyValuePair<int, int>> GainData;
```

#### 2.3.6 色块选取交互 (ColorblockPickingWindow)

```
1. 用户选择多个 .raw 文件
2. 每个文件一个 Tab 页
3. 在每个 Tab 图片上框选最多 6 个灰色色块区域
4. 点击 "确定":
   - 提取所有选框坐标 (x, y, width, height)
   - 调用 AWBCal() 计算该文件的 R/B 增益
   - 存储 correctionData[文件名] = (rgain, bgain/4)
5. 返回 AwbWindow，合并到 GainData
```

**注意**: `bgain` 存储时做了 `/4` 缩放 (可能是精度转换)。

#### 2.3.7 贝塞尔曲线拟合

**四个控制点**:
- `StartPoint`: 曲线起点 (X 锁定在 RGainStart)
- `EndPoint`: 曲线终点 (X 锁定在 RGainEnd)
- `StartBezierPoint`: 起点控制点
- `EndBezierPoint`: 终点控制点

**曲线采样 (GetBezierLinePoints)**:
1. 获取贝塞尔曲线的 `PathGeometry`
2. 沿 X 轴均分为 31 份 (取 32 个点)
3. 对每个 X 位置，创建垂直线与贝塞尔曲线求交
4. 交点的 Y 坐标即为该 X 位置的统计值

**最多 4 条曲线**: 当 `_bezierLineList.Count >= 4` 时禁止添加。

#### 2.3.8 .ispawb 文件格式

```xml
<?xml version="1.0" encoding="UTF-8"?>
<AwbChartData>
    <RGainStart>170</RGainStart>
    <RGainMin>170</RGainMin>
    <RGainMax>440</RGainMax>
    <StatData>154,154,154,...,86</StatData>
    <GainValueData>
        <Value Path="D65_001">256,128</Value>
        <Value Path="TL84_001">280,140</Value>
    </GainValueData>
</AwbChartData>
```

**StatData 编码**: 所有曲线 (最多 4 条) 的 Value 值按顺序拼接，每条曲线 32 个点。

#### 2.3.9 序列化格式

**XML 格式** (部分字段):
```xml
<Awb>
    <Awb_Seg_Mode>3</Awb_Seg_Mode>
    <Awb_Weight_In>7</Awb_Weight_In>
    <Awb_Weight_Out>3</Awb_Weight_Out>
    <Awb_Rg_Start>170</Awb_Rg_Start>
    <Awb_RgainMin>170</Awb_RgainMin>
    <Awb_RgainMax>440</Awb_RgainMax>
    <Awb_Ymin>16</Awb_Ymin>
    <Awb_Ymax>192</Awb_Ymax>
    <Awb_Yuv_En>0</Awb_Yuv_En>
    <Awb_Stat_Tab>154,154,...,86</Awb_Stat_Tab>
</Awb>
```

**二进制格式**: `AwbParams` 结构体 (约 300 字节)，键为 `DeviceModulePos = 4`

---

### 2.4 CCM (颜色校正矩阵)

**文件**: `ThunderSE/DeviceConfig/Isp/CCM.cs`  
**DeviceModulePos**: 5

#### 2.4.1 参数定义

| 参数 | 类型 | 默认值 | 范围 | UI 范围 | 说明 |
|------|------|--------|------|---------|------|
| ccm | short[9] | 未初始化 | short 范围 | -512 ~ 511 | 3x3 颜色校正矩阵 (行优先) |
| s41 | short | 0 | short 范围 | - | 扩展参数 41 |
| s42 | short | 0 | short 范围 | - | 扩展参数 42 |
| s43 | short | 0 | short 范围 | - | 扩展参数 43 |

**矩阵布局**:
```
ccm[0] ccm[1] ccm[2]    R->R  R->G  R->B
ccm[3] ccm[4] ccm[5] =  G->R  G->G  G->B
ccm[6] ccm[7] ccm[8]    B->R  B->G  B->B
```

#### 2.4.2 预设值

| 预设名 | 矩阵值 (十进制) |
|--------|----------------|
| R | {272, 8, -24, 0, 256, 0, 0, 0, 256} |
| G | {256, 0, 0, -8, 272, -8, 0, 0, 256} |
| B | {256, 0, 0, 0, 256, 0, -24, 8, 272} |
| Y | {272, 8, -24, -8, 272, -8, 0, 0, 256} |
| C | {256, 0, 0, -8, 272, -8, -24, 8, 272} |
| M | {272, 8, -24, 0, 256, 0, -24, 8, 272} |

#### 2.4.3 序列化

**XML 格式**:
```xml
<CCM>
    <ccm>272,8,-24,0,256,0,0,0,256</ccm>
</CCM>
```

**注意**: s41/s42/s43 不参与 XML 序列化。

**二进制格式**: `CCMParams` 结构体 (9*2 + 3*2 = 24 字节)，键为 `DeviceModulePos = 5`

---

### 2.5 YGamma (亮度 Gamma 校正)

**文件**: `ThunderSE/DeviceConfig/Isp/Gamma.cs`  
**DeviceModulePos**: 7

#### 2.5.1 参数定义

| 参数 | 类型 | 默认值 | 范围 | 说明 |
|------|------|--------|------|------|
| using_ygama | short[256] | Gamma 2.2 曲线 | 0-1023 | Gamma 校正查找表 |
| Pad_Num | byte | 1 | byte | 防除零保护参数 |

**历史参数** (硬编码为 0，保留用于结构体对齐):
| 参数 | 类型 | 原始用途推测 |
|------|------|------------|
| br_mod | int | 亮度模式选择 |
| gma_num[8] | int | Gamma 曲线分段参数 |
| contra_num | int | 对比度增强系数 |
| bofst | int | 全局亮度偏移 |
| lofst | int | 亮度通道偏移 |
| lcpr_low/high/llimt/hlimt | int | 局部对比度增强 |

#### 2.5.2 Gamma 表定义

**取值范围**:
- 索引 (输入): 0-255 (8-bit 查表索引)
- 值 (输出): 0x000-0x3FF (0-1023, 10-bit 亮度值)

**默认值** (标准 Gamma ~2.2 曲线，前 8 个):
```
索引 0: 0x000 (0)
索引 1: 0x08D (141)
索引 2: 0x0B5 (181)
索引 3: 0x0D1 (209)
索引 4: 0x0E8 (232)
索引 5: 0x0FB (251)
索引 6: 0x10C (268)
索引 7: 0x11B (283)
...
索引 255: 0x3FF (1023)
```

#### 2.5.3 亮度映射链路

```
输入 RGB (10-bit, 0-1023)
    │
    ▼
亮度计算: Y = (R*77 + G*150 + B*29) >> 8   // BT.601 权重
    │
    ▼
查表索引: idx = Y / 4                      // 0-255
插值权重: frac = Y & 3                     // 0-3
    │
    ▼
线性插值: out = table[idx] + (table[idx+1] - table[idx]) * frac / 4
    │
    ▼
增益计算: gain = (out_y + pad_num) / (Y + pad_num)
    │
    ▼
输出: R_out = clip(R * gain, 0, 1023)
      G_out = clip(G * gain, 0, 1023)
      B_out = clip(B * gain, 0, 1023)
```

**关键设计**: Gamma 校正通过**亮度比率增益**应用到 RGB 三通道，确保色彩不变性。

#### 2.5.4 20 个关键点

| 序号 | X 值 | 区间宽度 | 区间名称 |
|------|------|---------|---------|
| 0 | 0 | - | 黑点 |
| 1 | 1 | 1 | 极暗 |
| 2 | 3 | 2 | 极暗 |
| 3 | 6 | 3 | 暗部 |
| 4 | 10 | 4 | 暗部 |
| 5 | 16 | 6 | 暗部 |
| 6 | 26 | 10 | 暗-中过渡 |
| 7 | 39 | 13 | 暗-中过渡 |
| 8 | 55 | 16 | 中间调 |
| 9 | 71 | 16 | 中间调 |
| 10 | 87 | 16 | 中间调 |
| 11 | 103 | 16 | 中间调 |
| 12 | 119 | 16 | 中间调 |
| 13 | 135 | 16 | 中间调 |
| 14 | 151 | 16 | 中间调 |
| 15 | 167 | 16 | 中-亮过渡 |
| 16 | 191 | 24 | 中-亮过渡 |
| 17 | 223 | 32 | 高光 |
| 18 | 239 | 16 | 高光 |
| 19 | 255 | 16 | 白点 |

**设计原理**:
- 暗部 (0-39): 7 个关键点覆盖 40 个输入值，平均 5.7/点
- 中间调 (39-191): 9 个关键点，间距固定 16
- 高光 (191-255): 4 个关键点

符合韦伯-费希纳定律 (人眼对暗部变化更敏感)。

#### 2.5.5 线性插值算法

当拖拽关键点时触发 `CollectionChanged` 事件:

```csharp
// 更新底层 256 点表中该关键点的值
_yGamma.YGammaTable[_yGammaKeyPointXValues[e.NewStartingIndex]] = 
    _yGammaTable[e.NewStartingIndex].Value;

// 向前插值 (与前一个关键点之间的区间)
if (e.NewStartingIndex > 0) {
    prevX = _yGammaKeyPointXValues[e.NewStartingIndex - 1];
    currX = _yGammaKeyPointXValues[e.NewStartingIndex];
    span = currX - prevX;
    slope = (currY - prevY) / (float)span;
    for (i = 1; i < span; i++)
        _yGamma.YGammaTable[prevX + i] = prevY + Floor(slope * i);
}

// 向后插值 (与后一个关键点之间的区间)
// 逻辑同上
```

**特性**:
- 插值类型: **线性插值** (非 Gamma 幂函数)
- 取整方式: `Math.Floor` 向下取整
- 范围: 仅影响被修改关键点与**直接相邻**关键点之间的区间
- 连续性: C0 连续 (关键点处连续，但斜率可能突变)

#### 2.5.6 文件导入/导出

**支持格式 1: 十六进制 (每行一个值)**
```
0x0
0x8d
0xb5
...
0x3ff
```

**支持格式 2: 十进制逗号分隔**
```
0,141,181,209,232,...,1023
```

**校验规则**:
- 仅检查 `yGammaTable.Length >= 256`
- **不检查**值的范围 (应为 0-1023)
- 超过 256 个值时取前 256 个

**导出格式**: 纯十进制逗号分隔

#### 2.5.7 Pad_Num 参数作用

```cpp
output = input * (out_y + pad_num) / (img_y + pad_num)
```

| pad_num 值 | 效果 |
|-----------|------|
| 0 | 当 img_y = 0 时除零溢出 |
| 1 (默认) | 黑像素增益 = 1，最小保护 |
| 10 | 整体增益对比度被压缩 |

#### 2.5.8 YGAMMA_IQ 评估

**6 阶灰度卡分析**:
```
1. RGB -> sRGB 反伽马 -> CIE XYZ Y -> CIE L*a*b* L*
2. 计算相邻阶 L* 差值
3. count = 可分辨阶数 (理想 5，阈值 > 10 JND)
```

**13 阶灰度卡分析**:
```
1. 每 ROI 分 3 个子区域，共 39 个采样点
2. 计算亮度 Y (BT.601 权重)
3. count = 可分辨阶数 (阈值 > 8)
4. y_max = 最大亮度 (应 >= 0.98 * 256)
5. out_gamma = log(0.5) / log(y_avg[7] / 256.0)
```

**6 阶 vs 13 阶对比**:

| 特性 | 6 阶 | 13 阶 |
|------|------|-------|
| ROI 数量 | 6 | 13 |
| 每 ROI 子区域 | 1 | 3 (上/中/下) |
| 总采样点 | 6 | 39 |
| 色彩空间 | CIE L*a*b* (L*) | BT.601 Y |
| 可分辨阈值 | 10 (JND) | 8 |
| 输出指标 | count, l_var, delta_l | count, y_max, y_avg, out_gamma |

#### 2.5.9 序列化格式

**XML 格式**:
```xml
<YGamma>
    <Global_Gamma_Table>0,141,181,...,1023</Global_Gamma_Table>
    <Pad_Num>1</Pad_Num>
</YGamma>
```

**二进制格式**: `YGammaParams` 结构体 (约 530 字节)，键为 `DeviceModulePos = 7`

---

## 三、简化模块详细规格

### 3.1 VDE (视频动态增强)

**文件**: `ThunderSE/DeviceConfig/Isp/VDE.cs`  
**DeviceModulePos**: 10

| 参数 | 类型 | 默认值 | UI 范围 | 说明 |
|------|------|--------|---------|------|
| contra | int | 0 | 0-255 | 对比度 |
| bright_k | int | 80 | - | 亮度系数 (80 = 1x 增益) |
| bright_oft | int | 0 | - | 亮度偏移 (最终 = bright_oft * bright_k) |
| hue | int | 0 | - | 色调 |
| sat | int[9] | 全0 | - | 饱和度数组 (**未序列化**) |
| sat_rate | int[8] | 全0 | 0-32 | 饱和度比率 (8 级，夜晚→白天) |
| vde_step | int | 0 | - | VDE 步进 (**未序列化**) |

**序列化**: 仅序列化 `sat_rate`, `contra`, `bright_k`, `bright_oft`, `hue`。

**UI**: 8 个 Slider (sat_rate) + 4 个 Slider (contra/bright_k/bright_oft/hue)。

---

### 3.2 EE (边缘增强/锐化)

**文件**: `ThunderSE/DeviceConfig/Isp/EE.cs`  
**DeviceModulePos**: 11

| 参数 | 类型 | 默认值 | UI 范围 | 说明 |
|------|------|--------|---------|------|
| ee_class | byte | 0 | 0-15 | 锐化强度等级 |
| ee_dn_slope | byte[8] | null | - | 降噪斜率 (**未序列化**) |
| ee_sharp_slope | byte[8] | null | - | 锐化斜率 (**未序列化**) |
| ee_th_adp | byte[8] | null | - | 阈值自适应 (**未序列化**) |
| ee_dn_th | byte[8] | null | - | 降噪阈值 (**未序列化**) |
| sharp_class | byte[8] | null | - | 锐化等级 (**未序列化**) |
| dn_class | byte[8] | null | - | 降噪等级 (**未序列化**) |

**严重问题**:
- 7 个数组参数中仅 `ee_class` 参与 XML 序列化 (丢失率 85.7%)
- UI 仅暴露 `ee_class` 一个参数
- 数组未初始化，首次访问可能引发 `NullReferenceException`

---

### 3.3 CH (色彩增强)

**文件**: `ThunderSE/DeviceConfig/Isp/CH.cs`  
**DeviceModulePos**: 9

| 参数 | 类型 | 默认值 | UI 范围 | 说明 |
|------|------|--------|---------|------|
| stage0_en | int | 0 | - | Stage0 使能 (RGB 通道) (**未序列化**) |
| stage1_en | int | 0 | - | Stage1 使能 (YCM 通道) (**未序列化**) |
| enhence | int[6] | 全0 | - | 增强通道开关 (R/B/G/Y/C/M) |
| th1 | int[6] | 全0 | - | Hue 宽度上界 (**未序列化**) |
| th0 | int[6] | 全0 | - | Hue 宽度下界 (**未序列化**) |
| r_rate | int[6] | 全0 | 0-31 | R 通道增强率 |
| g_rate | int[6] | 全0 | - | G 通道增强率 |
| b_rate | int[6] | 全0 | - | B 通道增强率 |
| sat | int[17] | 全0 | - | 饱和度表 (**未序列化**) |
| rate | int[8] | 全0 | - | 速率数组 (**未序列化**) |

**UI 映射关系**:

| UI 通道 | 实际修改的数据 |
|---------|---------------|
| R | `r_rate[0]` |
| G | `g_rate[1]` |
| B | `b_rate[2]` |
| Y | `r_rate[3]` + `g_rate[3]` (同时设置) |
| C | `g_rate[4]` + `b_rate[4]` (同时设置) |
| M | `r_rate[5]` + `b_rate[5]` (同时设置) |

**Bug**: `th0` 的 `PropertyChanged` 事件使用了错误的属性名 `"_th0"` (下划线前缀导致 WPF 绑定失效)。

---

### 3.4 SAJ (抗锯齿/降色噪)

**文件**: `ThunderSE/DeviceConfig/Isp/SAJ.cs`  
**DeviceModulePos**: 13

| 参数 | 类型 | 默认值 | UI 范围 | 说明 |
|------|------|--------|---------|------|
| sat | byte[17] | 全0 | - | 饱和度数组 (**未序列化**) |
| sat_rate | byte[8] | 全0 | 0-16 (反相) | 饱和度比率 (8 级) |
| saj_step | byte | 0 | - | SAJ 步进 (**未序列化**) |

**UI 值反相映射**:
```
UI 显示值 = 16 - 实际存储值
实际存储值 = 16 - UI 显示值
// UI 左端 (0) = 夜晚强降噪, 右端 (16) = 白天弱降噪
```

---

### 3.5 DDC (缺陷像素校正/降噪)

**文件**: `ThunderSE/DeviceConfig/Isp/DDC.cs`  
**DeviceModulePos**: 3

| 参数 | 类型 | 默认值 | UI 范围 | 说明 |
|------|------|--------|---------|------|
| hot_num | int | 0 | - | 热像素数量 (**未序列化**) |
| dead_num | int | 0 | - | 死像素数量 (**未序列化**) |
| hot_th | int | 0 | - | 热像素阈值 (**未序列化**) |
| dead_th | int | 0 | - | 死像素阈值 (**未序列化**) |
| avg_th | int | 0 | - | 平均阈值 (**未序列化**) |
| d_th_rate | int[8] | 全0 | - | 死像素阈值率 (**未序列化**) |
| h_th_rate | int[8] | 全0 | - | 热像素阈值率 (**未序列化**) |
| dpc_dn_en | int | 0 | - | DPC 降噪使能 (**未序列化**) |
| indx_table | int[8] | 全0 | - | 索引表 (**未序列化**) |
| indx_adapt | int[8] | 全0 | 0-14 (UI) | 索引自适应 (UI 偏移 +7) |
| std_th | int[7] | 全0 | - | 标准阈值 (**未序列化**) |
| std_th_rate | int | 0 | - | 标准阈值率 (**未序列化**) |
| ddc_step | int | 0 | - | DDC 步进 (**未序列化**) |
| ddc_class | int | 0 | 0-7 | DDC 等级 (**ViewModel 中被注释**) |

**UI 值偏移映射**:
```
UI 显示值 = 实际存储值 + 7
实际存储值 = UI 显示值 - 7
// UI 范围 0-14, 实际存储 -7 ~ 7
```

**严重问题**: 16 个参数中仅 1 个 (`indx_adapt`) 参与 XML 序列化 (丢失率 93.75%)。

---

### 3.6 AE (自动曝光)

**文件**: `ThunderSE/DeviceConfig/Isp/AE/AE.cs`, `AEData.cs`  
**DeviceModulePos**: 0

**EXP 参数**:

| 参数 | 类型 | 默认值 | UI 范围 | 说明 |
|------|------|--------|---------|------|
| ylog_cal_fnum | int | 0 | - | Y log 计算帧数 (**未序列化**) |
| exp_tag | int[8] | 全0 | 0-255 | 目标曝光标签 (8 级，夜晚→白天) |
| exp_ext_mod | int | 0 | - | 曝光扩展模式 (**未序列化**) |
| exp_gain | int | 0 | - | 曝光增益 (**未序列化**) |
| k_br | int | 0 | - | 亮度系数 (**未序列化**) |
| exp_min | int | 0 | - | 最小曝光 (**未序列化**) |
| gain_max | int | 0 | 0-gain_max_save | 最大增益 |
| exp_nums | int | 0 | - | 曝光次数 (**未序列化**) |
| gain_max_save | int | 0 | - | 最大增益上限 (**未序列化**) |

**HGRM 参数** (全部未序列化，UI 不可见):

| 参数 | 类型 | 说明 |
|------|------|------|
| allow_miss_dots | int | 允许缺失点数 |
| ae_win_x0~x3 | int[4] | AE 窗口 X 坐标 (4 顶点) |
| ae_win_y0~y3 | int[4] | AE 窗口 Y 坐标 (4 顶点) |
| weight_0_7 | int | 权重分区 0-7 |
| weight_8_15 | int | 权重分区 8-15 |
| weight_16_23 | int | 权重分区 16-23 |
| weight_24 | int | 权重分区 24 |
| hgrm_centre_weight | int[8] | 中心权重 |
| hgrm_gray_weight | int[8] | 灰度权重 |

**严重问题**: 24 个参数中仅 1 个 (`exp_tag`) 参与 XML 序列化 (丢失率 95.8%)。

---

## 四、共性问题分析

### 4.1 XML 序列化不完整汇总

| 模块 | 总参数字段 | 已序列化 | 丢失率 | 严重程度 |
|------|-----------|---------|--------|---------|
| AE | 24 | 1 (exp_tag) | 95.8% | 🔴 极端 |
| DDC | 16 | 1 (indx_adapt) | 93.75% | 🔴 极端 |
| EE | 7 | 1 (ee_class) | 85.7% | 🔴 严重 |
| SAJ | 3 | 1 (sat_rate) | 66.7% | 🟡 中等 |
| CH | 10 | 4 | 60% | 🟡 中等 |
| CCM | 4 | 1 (ccm) | 75% | 🟡 中等 |
| VDE | 7 | 5 | 28.6% | 🟢 轻微 |
| BLC | 4 | 4 | 0% | ✅ 完整 |
| LSC | 1 | 1 | 0% | ✅ 完整 |
| AWB | 约 20 | 约 10 | ~50% | 🟡 中等 |
| YGamma | 2 | 2 | 0% | ✅ 完整 |

### 4.2 数组未初始化风险

| 模块 | 未初始化数组 | 风险 |
|------|------------|------|
| EE | ee_dn_slope, ee_sharp_slope, ee_th_adp, ee_dn_th, sharp_class, dn_class | NullReferenceException |
| CH | th0, th1, sat, rate | NullReferenceException |
| SAJ | sat | NullReferenceException |

### 4.3 UI 简化过度

| 模块 | UI 暴露参数 | 总参数 | 隐藏参数 |
|------|-----------|--------|---------|
| EE | ee_class (1) | 49 byte | 48 byte |
| DDC | indx_adapt (8) | 16 参数 | 15 参数 |
| AE | exp_tag (8), gain_max (1) | 24 参数 | 22 参数 |
| VDE | sat_rate (8), contra, bright_k, bright_oft, hue | 7 参数 | sat[9], vde_step |

### 4.4 无默认值

所有模块参数在构造时均为 0 或 null，没有有意义的默认值。新建配置时所有 ISP 参数都是零值，需要手动配置或从设备读取。

---

## 五、设备通信协议

### 5.1 参数编码规则

```
parameter (32 位整数):
  高 24 位: readPos/sentPos (读/写位置，左移 8 位)
  低 8 位:  DeviceModulePos * IspBitWidth (模块位置 * 2)
```

### 5.2 分块读写

| 操作 | 块大小 | 说明 |
|------|--------|------|
| 读取 | 最大 512 字节/块 | 分块读取模块参数 |
| 写入 | 最大 512 字节/块 | 仅写入 HasChangedParams = true 的模块 |

### 5.3 模块使能写入值

| 模块 | 使能时写入值 |
|------|:---:|
| BLC | 0x01 |
| LSC | 0x01 |
| 其他 (DDC/AWB/CCM/YGamma/CH/VDE/EE/SAJ) | 0x02 |

---

## 六、数据流完整链路

### 6.1 RAW 文件 → 参数计算 → 设备写入 (以 BLC 为例)

```
[用户操作] 打开 BlcWindow → 选择 RAW 文件
    │
    ▼
[BlcWindowViewModel.OpenRawFileAndCalcBlackLevel]
    1. File.ReadAllBytes() → _nativeRawFileBuffer
    2. Task.Run → CalBlackLevelData()
    │
    ▼
[BlackLevel.CalBlackLevelData]
    1. AllocHGlobal 分配 5 个指针 (每个 W*H/4 * 2 字节)
    2. IspApi.BlcCal(rawBuffer, W, H, (int)Bayer, ptrArray)
       C++ 端: 按 Bayer 模式分离 R/Gr/Gb/B 四通道
    3. Marshal.Copy 回托管 short[] 数组
    4. FreeHGlobal 释放非托管内存
    │
    ▼
[ViewModel 统计计算]
    1. GetMedianPixelValue() → 四通道中值 (直方图法)
    2. .Average() → 四通道平均值
    3. BuildPixelData() → 四通道直方图 (图表用)
    4. RaisePropertyChanged 通知 UI 更新
    │
    ▼
[用户选择校正方式] ComboBox: 中值/平均值 → 点击"应用"
    │
    ▼
[BlcWindowViewModel.ApplyCorrection]
    1. 构建 correctionValueArray = [R, Gr, Gb, B]
    2. BlackLevel.ApplyBlackLevelCorrection(array, isMinus=true)
       → _correctValuesArray = [-R, -Gr, -Gb, -B] (取负)
       → HasChangedParams = true
    │
    ▼
[DeviceConfigPageViewModel.OnBlcConfigChange]
    → RaisePropertyChanged("BlcR/Gr/Gb/B")
    → UI TextBox 更新显示
    │
    ▼
[用户点击"写入"/"保存"]
    │
    ▼
[Config.WriteToDevice() / WriteToFile()]
    │
    ▼
[BlackLevel.ParamsDataCollection getter]
    1. 构建 BlcParams 结构 {blkl_r=R, blkl_gr=Gr, ...}
    2. Marshal.StructureToPtr → 8 字节数组
    3. 返回 Dictionary<int, byte[]> { {1, byteArray} }
    │
    ▼
[设备烧录] 将 8 字节 BLC 参数写入设备模块位置 1
```

### 6.2 图像处理管线 (RAW 文件预览)

```
[Processor.RawFileProcessSteps] = { Blc, Lsc, Awb } (按顺序)
    │
    ▼
[BlackLevel.ProcessRawBuffer(ref imgBuffer)]
    1. pixelCount = W * H
    2. short[outputBuffer] = new short[pixelCount]
    3. IspApi.BlcImg(imgBuffer, _correctValuesArray, Bayer, W, H, outputBuffer)
       C++ 端: 校正值符号扩展 → 对每个像素加校正值 → CLIP_PIXEL(0, 1023)
    4. imgBuffer = new byte[pixelCount * 2]; Buffer.BlockCopy(outputBuffer, ...)
    │
    ▼
[LensShading.ProcessRawBuffer(ref imgBuffer)]
    1. 确保 CorrectionData 已初始化 (按当前分辨率)
    2. short[outputBuffer] = new short[pixelCount]
    3. IspApi.LscImg(imgBuffer, W, H, blockSizeX, blockSizeY, CorrectionData, outputBuffer)
       C++ 端: 双线性插值增益 → CLIP_PIXEL(0, 1023)
    4. imgBuffer = new byte[pixelCount * 2]; Buffer.BlockCopy(outputBuffer, ...)
    │
    ▼
[AutoWhiteBalance.ProcessRawBuffer(ref imgBuffer)]
    1. CalcGainValue(imgBuffer) → {R,G,B}_gain
    2. IspApi.AWBImg(imgBuffer, Bayer, W, H, gains, ...)
    3. imgBuffer = 校正后的数据
    │
    ▼
[Processor.GenerateBitmap]
    1. IspApi.DemosaicImg() → Bayer 去马赛克为 RGB
    2. IspApi.EncoderImgBuffer() → 编码为 JPEG
    3. 创建 BitmapImage 返回 UI 显示
```

---

## 七、已知 Bug 清单

### 7.1 高严重性

| 编号 | 模块 | 问题 | 影响 |
|------|------|------|------|
| B1 | EE | 6 个数组参数未初始化 | NullReferenceException |
| B2 | CH | th0 的 PropertyChanged 属性名错误 `"_th0"` | WPF 绑定失效 |
| B3 | DDC | 16 参数中仅 1 个序列化 | 配置文件加载后参数丢失 |
| B4 | AE | 24 参数中仅 1 个序列化 | 配置文件加载后参数丢失 |
| B5 | CCM | s41/s42/s43 未序列化 | 配置文件加载后参数丢失 |
| B6 | VDE | sat[9] 和 vde_step 未序列化 | 配置文件加载后参数丢失 |
| B7 | SAJ | sat[17] 和 saj_step 未序列化 | 配置文件加载后参数丢失 |
| B8 | YGamma | 离线 IQ 功能未完成 | 功能不可用 |

### 7.2 中严重性

| 编号 | 模块 | 问题 | 影响 |
|------|------|------|------|
| B9 | BLC | P/Invoke 参数名误导 (height/width 顺序) | 可能导致调用错误 |
| B10 | YGamma | LoadYGammaTableFromFile 不检查值范围 | 可能加载无效 Gamma 表 |
| B11 | YGamma | Convert.ToByte(null) 在缺少 Pad_Num 节点时崩溃 | NullReferenceException |
| B12 | LSC | DeserializeFromXmlElement 缺少空节点检查 | NullReferenceException |
| B13 | AWB | bgain 存储时除以 4 精度损失 | 精度降低 |

### 7.3 低严重性

| 编号 | 模块 | 问题 | 影响 |
|------|------|------|------|
| B14 | YGamma | 错误提示中文乱码 | 用户体验差 |
| B15 | YGamma | Substring(0, Length) 冗余 | 代码整洁度 |
| B16 | 通用 | 所有模块无默认值初始化 | 新建配置参数全为零 |

---

## 八、关键文件清单

### 核心数据模型

| 文件 | 路径 | 模块 |
|------|------|------|
| BlackLevel.cs | `DeviceConfig/Isp/BlackLevel.cs` | BLC |
| LensShading.cs | `DeviceConfig/Isp/LensShading.cs` | LSC |
| AutoWhiteBalance.cs | `DeviceConfig/Isp/AutoWhiteBalance.cs` | AWB |
| CCM.cs | `DeviceConfig/Isp/CCM.cs` | CCM |
| Gamma.cs | `DeviceConfig/Isp/Gamma.cs` | YGamma |
| VDE.cs | `DeviceConfig/Isp/VDE.cs` | VDE |
| EE.cs | `DeviceConfig/Isp/EE.cs` | EE |
| CH.cs | `DeviceConfig/Isp/CH.cs` | CH |
| SAJ.cs | `DeviceConfig/Isp/SAJ.cs` | SAJ |
| DDC.cs | `DeviceConfig/Isp/DDC.cs` | DDC |
| AE.cs | `DeviceConfig/Isp/AE/AE.cs` | AE |
| AEData.cs | `DeviceConfig/Isp/AE/AEData.cs` | AE 数据结构 |
| CommonConfig.cs | `DeviceConfig/Isp/CommonConfig.cs` | 公共配置 |
| Processor.cs | `DeviceConfig/Isp/Processor.cs` | ISP 处理器 |
| ProcessStep.cs | `DeviceConfig/Isp/ProcessStep.cs` | 抽象基类 |
| IspApi.cs | `DeviceConfig/Isp/IspApi.cs` | P/Invoke 声明 |

### UI 调试窗口

| 文件 | 路径 | 模块 |
|------|------|------|
| BlcWindow | `Ui/SettingWindow/Blc/` | BLC |
| LscWindow | `Ui/SettingWindow/Lsc/` | LSC |
| AwbWindow | `Ui/SettingWindow/Awb/` | AWB |
| CcmWindow | `Ui/SettingWindow/Ccm/` | CCM |
| YGammaWindow | `Ui/SettingWindow/YGamma/` | YGamma |
| ColorblockPickingWindow | `Ui/SettingWindow/Awb/` | 色块选取 |
| BezierFigure.cs | `Ui/SettingWindow/Awb/CustomControls/` | 贝塞尔曲线 |

### C++ 算法实现

| 文件 | 路径 | 功能 |
|------|------|------|
| IQ.cpp | `IspApi/source/IQ.cpp` | BLC/LSC/AWB/CCM/YGamma 算法 |
| Export.h | `IspApi/source/Export.h` | C 接口导出 |
| IQ.h | `IspApi/include/IQ.h` | IQ 算法配置 |

---

## 九、附录

### 9.1 Bayer 模式与 Polarity 映射

| Bayer 模式 | Polarity 值 | 像素排列 |
|-----------|:---:|---------|
| RGRG | 0 | RG / GB |
| GRGR | 1 | GR / BG |
| BGBG | 2 | BG / GR |
| GBGB | 3 | GB / RG |

### 9.2 像素值范围

| 位宽 | 范围 | 宏定义 |
|------|------|--------|
| 10-bit | 0-1023 | `HIGH_VAL_10BIT` |
| 8-bit | 0-255 | - |

### 9.3 色彩空间转换权重

| 转换 | 权重公式 |
|------|---------|
| BT.601 亮度 | Y = (R*77 + G*150 + B*29) / 256 |
| BT.709 XYZ | Y = R*0.2126 + G*0.7152 + B*0.0722 |
| sRGB 反伽马 | x > 0.04045 ? pow((x+0.055)/1.055, 2.4) : x/12.92 |
| CIE L* | L* = 116 * f(Y/Yn) - 16 |

---

**文档版本**: v1.0  
**创建日期**: 2026年4月8日  
**分析依据**: 项目源码完整阅读与交叉引用  
**文档状态**: 完整

---

**文档结束**

本文档基于 ThunderSE 项目实际代码深入分析生成，涵盖 11 个 ISP 模块的完整参数定义、算法流程、数据格式、UI 交互、序列化规范和设备通信协议。可作为模块开发、调试、测试和维护的参考文档。
