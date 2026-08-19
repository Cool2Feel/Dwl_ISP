# AWB (自动白平衡) 模块详细需求规格说明

## 文档信息

| 项目 | 内容 |
|------|------|
| **模块名称** | AWB (Auto White Balance / 自动白平衡) |
| **DeviceModulePos** | 4 |
| **IspModule 枚举值** | `IspModule.Awb` |
| **文档版本** | v1.0 |
| **创建日期** | 2026年4月8日 |
| **分析依据** | 项目源码完整阅读与交叉引用 |

---

## 一、模块概述

### 1.1 功能描述

AWB (Auto White Balance) 模块是 ISP 图像处理管线中的**自动白平衡校正模块**，负责根据光源色温自动调整图像的 R/B 增益，使白色物体在不同光照条件下仍然呈现白色。

**核心功能**:
- 支持 RAW 域和 YUV 域两种白点统计模式
- 通过统计图像的色温信息计算 R/G/B 三个通道的增益值
- 支持分段增益表 (3x8=24 段) 适应不同亮度区间
- 支持高红/高蓝区域检测和保护
- 提供色块选取工具进行手动标定
- 提供贝塞尔曲线编辑工具定义白点判定边界
- 提供 IQ 质量评估功能

### 1.2 物理原理

**白平衡问题成因**:
1. **光源色温差异**: 不同光源 (日光/白炽灯/荧光灯) 的光谱分布不同
2. **传感器光谱响应**: 传感器 R/G/B 滤光片的光谱响应不理想
3. **色彩偏移**: 同一白色物体在不同光源下呈现不同颜色 (偏蓝/偏黄/偏红)

**校正原理**:
- 通过检测图像中的白色/灰色区域，计算 R/G 和 B/G 的比率
- 以 G 通道为基准，调整 R 和 B 通道的增益，使白色区域恢复中性
- 增益公式: `R_gain = G_avg / R_avg * 256`, `B_gain = G_avg / B_avg * 256` (Q8 格式)

### 1.3 在 ISP 管线中的位置

```
Raw Bayer → BLC → LSC → AWB → Demosaic → CCM → YGamma → EE → CH → 输出
   (0)       (1)    (2)    (4)               (5)    (7)    (11)  (9)
                        ↑ AWB 在第 4 步
```

**前置依赖**: BLC (黑电平校正)、LSC (镜头阴影校正)

**处理类型**: RAW 域处理 (ProcessRawBuffer)

---

## 二、参数完整定义

### 2.1 参数总表

| 参数 | 类型 | 默认值 | 取值范围 | 说明 |
|------|------|--------|----------|------|
| Seg_Mode | int | 3 | 0-7 | 分段模式，决定统计区间的分段数量 |
| RGainStart | int | 170 | 0-1024 | 统计曲线 X 轴起始 R 增益值 |
| RGainMin | int | 170 | 0-1024 | R 增益最小限制值（下限钳位） |
| RGainMax | int | 440 | 0-1024 | R 增益最大限制值（上限钳位） |
| Awb_Weight_In | int | 7 | 0-15 | 区间内权重系数 |
| Awb_Weight_Out | int | 3 | 0-15 | 区间外权重系数 |
| Awb_YMin | int | 16 | 0-255 | 有效亮度最小值（排除暗部） |
| Awb_YMax | int | 192 | 0-255 | 有效亮度最大值（排除过曝） |
| Awb_Yuv_Mod_En | int | 0 | 0/1 | YUV 模式使能：0=RAW域，1=YUV域 |
| Awb_Ycbcr_Th | byte | 10 | 0-255 | YCbCr 联合阈值 |
| Awb_De_High_Red_Class | int | 3 | 0-15 | 高红区域分类等级 |
| Awb_De_High_Blue_Class | int | 3 | 0-15 | 高蓝区域分类等级 |
| Awb_De_High_Red_Rate | int | 0 | 0-15 | 高红区域比例阈值 |
| Awb_De_High_Blue_Rate | int | 0 | 0-15 | 高蓝区域比例阈值 |

### 2.2 阈值数组参数

#### Awb_Cb_Th (Cb 阈值表, 8 元素)

| 索引 | 默认值 | 含义 |
|------|--------|------|
| 0 | 8 | 第 1 段 Cb 阈值 |
| 1 | 16 | 第 2 段 Cb 阈值 |
| 2 | 24 | 第 3 段 Cb 阈值 |
| 3 | 32 | 第 4 段 Cb 阈值 |
| 4 | 40 | 第 5 段 Cb 阈值 |
| 5-7 | 48 | 第 6-8 段 Cb 阈值 |

#### Awb_Cr_Th (Cr 阈值表, 8 元素)

| 索引 | 默认值 | 含义 |
|------|--------|------|
| 0 | 8 | 第 1 段 Cr 阈值 |
| 1 | 16 | 第 2 段 Cr 阈值 |
| 2 | 24 | 第 3 段 Cr 阈值 |
| 3 | 32 | 第 4 段 Cr 阈值 |
| 4 | 40 | 第 5 段 Cr 阈值 |
| 5-7 | 48 | 第 6-8 段 Cr 阈值 |

#### Awb_Cbcr_Th (CbCr 联合阈值表, 8 元素)

| 索引 | 默认值 | 含义 |
|------|--------|------|
| 0 | 12 | 第 1 段 CbCr 阈值 |
| 1 | 24 | 第 2 段 CbCr 阈值 |
| 2 | 36 | 第 3 段 CbCr 阈值 |
| 3 | 48 | 第 4 段 CbCr 阈值 |
| 4 | 60 | 第 5 段 CbCr 阈值 |
| 5-7 | 72 | 第 6-8 段 CbCr 阈值 |

### 2.3 数据表参数

#### Awb_Stat_Tab (统计表, 128 字节)

- **格式**: `byte[128]`
- **用途**: 定义 128 个 R 增益位置上的统计权重曲线
- **索引方式**: `tab[segment_index * 32 + gain_bin_index]`，共 4 段 x 32 级 = 128
- **默认值**: 硬编码的 128 字节数组，表示典型的白平衡统计曲线
- **布局**:
  ```
  偏移 0-31:   外部上边界 (BG out high)
  偏移 32-63:  内部上边界 (BG in high)
  偏移 64-95:  内部下边界 (BG in low)
  偏移 96-127: 外部下边界 (BG out low)
  ```

#### seg_gain (分段增益表, 24 元素)

- **格式**: `short[24]`，注释说明原为 `seg_gain[8][3]` 扁平化
- **用途**: 存储 8 个分段 x 3 通道 (R/G/B) 的硬件增益值
- **索引方式**: `seg_gain[segment * 3 + channel]`，channel: 0=R, 1=G, 2=B

### 2.4 StatisticData 数据结构

**类型定义**:
```csharp
using WhiteBalanceStatCollection = ObservableCollection<KeyValuePair<double, double>>;
// StatisticData 类型: ObservableCollection<WhiteBalanceStatCollection>
```

**结构**:
- 外层: 最多 4 个 `WhiteBalanceStatCollection`（对应最多 4 段）
- 内层: 每个 `WhiteBalanceStatCollection` 包含 32 个 `KeyValuePair<double, double>`
  - **Key**: R 增益值 = `RGainStart + GainStep * bin_index` (其中 `GainStep = 16`)
  - **Value**: 该增益 bin 上的统计计数值（整数，存储为 double）

**总计**: 4 x 32 = **128 个数据点**，恰好对应 `Awb_Stat_Tab[128]`

### 2.5 GainData 数据结构

**类型**: `Dictionary<string, KeyValuePair<int, int>>`

- **Key**: 字符串路径（如配置文件路径或光源类型标识）
- **Value**: `KeyValuePair<int, int>` — 第一个 int 为 R 增益，第二个 int 为 B 增益

**用途**: 存储不同场景/光源下已标定好的 AWB 增益值对。

---

## 三、关键机制详解

### 3.1 Seg_Mode (分段模式)

`Seg_Mode` 是一个核心控制参数，取值 0-7，影响以下方面:

1. **统计区间数量**: `Seg_Mode` 决定了白点统计被分为多少个 R 增益区间段。模式值越大，分段越细。段数 = `1 << seg_mode` (如 seg_mode=3 → 8 段)。
2. **`Awb_Stat_Tab` 的有效数据量**: 统计表总共 128 字节，实际有效段数 = `Seg_Mode` 对应的段数（最多 4 段，每段 32 字节）。
3. **`AWB_Gain_Soft_Cal` 的增益计算**: 该函数根据 `Seg_Mode` 决定如何从 `wp_output` 数组中提取和插值最终的 R/G/B 增益。
4. **图表显示**: 在 AwbWindow 中，`StatisticData` 包含的曲线数量对应 `Seg_Mode` 的段数（最多 4 条曲线）。

### 3.2 RGainStart, RGainMin, RGainMax 的作用与关系

```
RGainStart → 统计曲线的 X 轴起始点（映射到图表最左侧）
RGainEnd   → 统计曲线的 X 轴终点 = RGainStart + 16 * 31 = RGainStart + 496
RGainMin   → 最终 R 增益的下限钳位值
RGainMax   → 最终 R 增益的上限钳位值
```

**关系链**:
1. `RGainStart` 定义了统计表在 R 增益空间中的起始偏移
2. 图表 X 轴范围为 `[0, 1024]`，`RGainStart` 通过 `RGainStatRangeLineBindingConverter` 映射到画布位置
3. 统计结果经过 `AWB_Gain_Soft_Cal` 计算后，最终 R 增益会被钳位到 `[RGainMin, RGainMax]`
4. `RGainMin` 和 `RGainMax` 在 UI 上以绿色虚线显示在图表上

### 3.3 Weight_In / Weight_Out 权重计算

这两个参数仅在 **RAW 域模式** (`Yuv_Mod_En = 0`) 下有效：

- `Weight_In` (默认 7): 落在统计曲线定义的"区间内"的像素权重
- `Weight_Out` (默认 3): 落在统计曲线定义的"区间外"的像素权重

权重比 = `Weight_In : Weight_Out` = `7 : 3`，即区间内像素权重是区间外的 2.33 倍。

权重机制确保曲线附近的像素对白平衡统计有更大贡献，远离曲线的像素贡献较小。

### 3.4 YMin / YMax 亮度窗口

- `YMin` (默认 16): 只统计亮度 >= 16 的像素，排除极暗区域（噪声大，白平衡不可靠）
- `YMax` (默认 192): 只统计亮度 <= 192 的像素，排除过曝区域（高光可能截断，色度信息丢失）

**适用范围**: RAW 域和 YUV 域模式均使用此亮度窗口。

### 3.5 Yuv_Mod_En 对统计模式的影响

```
Yuv_Mod_En = 0 → RAW 域统计模式 (默认)
   调用: IspApi.AWBStatistic_Yuv(raw_img, ..., ymin, ymax, cb_th, cr_th, cbcr_th, ycbcr_th, wp_output)
   特点: 使用 Cb/Cr 阈值表进行色度白点筛选，不使用统计表和权重系数

Yuv_Mod_En != 0 → YUV 域统计模式
   调用: IspApi.AWBStatistic(raw_img, ..., seg_mode, awb_stat_tab, weight_in, weight_out,
                            rg_start, rg_min, rg_max, ymin, ymax, wp_output)
   特点: 使用统计表 awb_stat_tab 和权重系数进行白点筛选
```

**注意**: 代码中 `Yuv_Mod_En != 0` 时调用的是 `AWBStatistic`，而 `Yuv_Mod_En == 0` 时调用的是 `AWBStatistic_Yuv`。命名与使用场景存在逻辑反转。

### 3.6 Cb_Th, Cr_Th, Cbcr_Th, Ycbcr_Th 阈值用途 (YUV 域模式)

这些阈值用于 YUV 域白点检测：

- **Cb_Th[8]**：每个分段中，像素 Cb 值与中性灰 Cb 的偏差阈值。`|Cb - Cb_gray| < Cb_Th[seg]` 的像素可能为白点。
- **Cr_Th[8]**：同上，针对 Cr 通道。
- **Cbcr_Th[8]**：CbCr 联合偏差阈值。`sqrt((Cb-Cb_g)^2 + (Cr-Cr_g)^2) < Cbcr_Th[seg]`。
- **Ycbcr_Th**：Y 与 CbCr 的联合约束，进一步排除非白点。

8 个阈值分别对应 8 个亮度/色度分段，允许不同亮度区域使用不同的白点判定标准。

---

## 四、C++ 算法实现

### 4.1 AWBCal (色块区域白平衡增益计算)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 724-782)

**函数签名**:
```cpp
ISP_API void AWBCal(
    const void *img_buffer,      // 输入: RAW 图像 (short*, 10bit Bayer)
    int img_width,                // 图像宽度
    int img_height,               // 图像高度
    int polarity,                 // Bayer 排列: 0=RG/GB, 1=GR/BG, 2=BG/GR, 3=GB/RG
    unsigned int *x,              // 输入: 最多6个ROI区域的X坐标
    unsigned int *y,              // 输入: 最多6个ROI区域的Y坐标
    unsigned int *width,          // 输入: 最多6个ROI区域的宽度
    unsigned int *height,         // 输入: 最多6个ROI区域的高度
    int &bgain,                   // 输出: B 通道增益 (Q8 格式)
    int &rgain                    // 输出: R 通道增益 (Q8 格式)
);
```

**算法原理**:

1. **遍历所有有效区域**: 遍历最多 6 个 ROI 区域, 累加区域内各颜色通道的像素和
2. **Bayer 通道分离**: 根据 `polarity` 区分 R/G/B 像素位置
   - polarity 0/2: tmp==0 为 R, tmp==1/2 为 G (Gr/Gb), tmp==3 为 B
   - polarity 1/3: tmp==0/3 为 G, tmp==1 为 R, tmp==2 为 B
3. **计算平均值**:
   - `avg_r = sum_r / (num / 4)` — R 像素占总像素 1/4
   - `avg_g = sum_g / (num / 2)` — G 像素占总像素 1/2
   - `avg_b = sum_b / (num / 4)` — B 像素占总像素 1/4
4. **极性补偿**: polarity 2/3 时交换 R 和 B 平均值
5. **增益计算** (以 G 为基准):
   - `rgain = clip(avg_g / avg_r * 256, 0, 1023)`
   - `bgain = clip(avg_b / avg_g * 256, 0, 1023)`

**关键特点**:
- 这是最简单的 AWB 计算方法, 直接对指定区域求平均
- 不涉及白点筛选, 适合已知灰色/白色区域的校准场景
- 增益以 Q8 定点数表示 (256 = 1.0x, 范围 0-1023)

### 4.2 AWBStatistic (RAW 域白点统计)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 831-928)

**函数签名**:
```cpp
ISP_API void AWBStatistic(
    void *raw_img,                // 输入: RAW 图像 (10bit Bayer)
    int polarity_mode,            // Bayer 排列模式 (0-3)
    int w, int h,                 // 图像宽高
    int seg_mode,                 // Y 值分段模式, 段数=1<<seg_mode (通常 3=8 段)
    unsigned char * awb_stat_tab, // 输入: 白点判定表 (4 行 x32 列, 128 字节)
    int weight_in,                // 输入: 内部区域权重
    int weight_out,               // 输入: 外部区域权重
    int rg_start,                 // 输入: RG 增益起始偏移值
    int rgmin, int rgmax,         // 输入: RG 增益有效范围
    int ymin, int ymax,           // 输入: Y 亮度有效范围
    int *wp_output                // 输出: 白点统计结果 (8 段 x 4 项)
);
```

**算法原理**:

#### 步骤 1: 2x2 块采样与 RGB 提取
```cpp
for (n = 0; n < h; n += 2)
    for (m = 0; m < w; m += 2)
        // 从 Bayer 的 2x2 块中提取 R/G/B
        r = pixel[R_channel] >> 2;     // 10bit -> 8bit
        g = (pixel[Gr] + pixel[Gb]) >> 3; // 两个 G 平均, 10bit -> 8bit
        b = pixel[B_channel] >> 2;     // 10bit -> 8bit
```

#### 步骤 2: Y 亮度计算 (BT.601 系数)
```cpp
y = (r * 77 + g * 150 + b * 29) / 256;  // 等同于 0.299R + 0.587G + 0.114B
```

#### 步骤 3: Y 值分段
```cpp
segk = y >> (8 - seg_mode);  // 例如 seg_mode=3 时, y/32, 将 0-255 分为 8 段
```

#### 步骤 4: RG 增益计算与范围筛选
```cpp
rgain = clip(g * 256 / r, 0, 1023);  // Q8 格式
if (rgain < rgmin || rgain > rgmax) continue;  // 超出范围不是白点
```

#### 步骤 5: 分段线性插值获取边界 (核心算法)
```cpp
rgain_num = (rgain - rg_start) / 16;   // 查表索引 (0-31)
rgain_mod = (rgain - rg_start) % 16;   // 插值权重 (0-15)

// 四条边界曲线 (每段 32 个点, 共 128 字节)
bgain_out_high = (tab[rgain_num] * (16-mod) + tab[rgain_num+1] * mod) / 4;
bgain_in_high  = (tab[32+rgain_num] * (16-mod) + tab[32+rgain_num+1] * mod) / 4;
bgain_in_low   = (tab[64+rgain_num] * (16-mod) + tab[64+rgain_num+1] * mod) / 4;
bgain_out_low  = (tab[96+rgain_num] * (16-mod) + tab[96+rgain_num+1] * mod) / 4;
```

#### 步骤 6: 权重判定
```cpp
bound_out_low  = bgain_out_low  * b / 256;
bound_out_high = bgain_out_high * b / 256;
bound_in_low   = bgain_in_low   * b / 256;
bound_in_high  = bgain_in_high  * b / 256;

if (g >= bound_out_low && g <= bound_out_high) weight = weight_out + 1;
if (g >= bound_in_low  && g <= bound_in_high)  weight = weight_in + 1;
```

#### 步骤 7: 累加统计结果
```cpp
wp_output[segk * 4 + 0] += weight;       // 白点计数
wp_output[segk * 4 + 1] += r * weight;   // R 加权和
wp_output[segk * 4 + 2] += g * weight;   // G 加权和
wp_output[segk * 4 + 3] += b * weight;   // B 加权和
```

**关键特点**:
- 使用 2x2 步长采样, 减少计算量
- 基于 RG-BG 平面的多区域白点判定, 支持复杂光源
- 分段插值提供精细的白点筛选边界控制
- 输出结果供 `AWB_Gain_Soft_Cal` 使用

### 4.3 AWBStatistic_Yuv (YUV 域白点统计)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 929-992)

**函数签名**:
```cpp
ISP_API void AWBStatistic_Yuv(
    void *raw_img,                // 输入: RAW 图像
    int polarity_mode,            // Bayer 排列模式
    int w, int h,                 // 图像宽高
    int seg_mode,                 // Y 值分段模式
    int ymin, int ymax,           // Y 亮度有效范围
    int* awb_cb_th,               // Cb 阈值表 (8 元素)
    int* awb_cr_th,               // Cr 阈值表 (8 元素)
    int* awb_cbcr_th,             // CbCr 联合阈值表 (8 元素)
    int awb_ycbcr_th,             // Y-CbCr 联合阈值
    int *wp_output                // 输出: 白点统计结果
);
```

**算法原理**:

与 `AWBStatistic` 前半部分相同 (2x2 采样, Y 计算, 分段), 但白点判定改为 **YUV 色彩空间**:

#### RGB 转 YCbCr
```cpp
// Y 分量 (已在前面计算)
y = (r * 77 + g * 150 + b * 29) / 256;

// Cb 分量 (蓝色色差)
cb = (-r * 43 - g * 85 + b * 128) / 256;   // 等同于 -0.169R - 0.331G + 0.500B

// Cr 分量 (红色色差)
cr = (r * 128 - g * 107 - b * 21) / 256;   // 等同于 0.500R - 0.419G - 0.081B
```

#### 白点判定条件 (三重过滤)
```cpp
// 条件 1: Cb 绝对值 < 阈值 (排除彩色)
// 条件 2: Cr 绝对值 < 阈值 (排除彩色)
if (abs(cb) < awb_cb_th[segk] && abs(cr) < awb_cr_th[segk]) {
    // 条件 3: Cb+Cr < 联合阈值 (更严格的白点筛选)
    // 条件 4: Y > Cb+Cr + 余量 (确保足够亮度)
    if (abs(cb) + abs(cr) < awb_cbcr_th[segk] && y > abs(cb) + abs(cr) + awb_ycbcr_th) {
        weight = 1;
    }
}
```

**关键特点**:
- 更适合中性色温检测, Cb/Cr 接近 0 即为白色
- 阈值支持分段 (每段独立阈值), 适应不同亮度区间
- 计算简单, 适合嵌入式硬件实现

### 4.4 AWB_Gain_Soft_Cal (软件增益计算)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 993-1019)

**函数签名**:
```cpp
ISP_API void AWB_Gain_Soft_Cal(
    int *wp_input,                // 输入: 白点统计结果 (8 段 x 4 项)
    int awb_seg_mode,             // 输入: 分段模式
    int* r_gain,                  // 输出: R 通道增益
    int* b_gain,                  // 输出: B 通道增益
    int* g_gain                   // 输出: G 通道增益
);
```

**算法原理**:

#### 步骤 1: 分段权重分配
```cpp
unsigned int seg_k_weight[8] = { 24, 32, 36, 36, 36, 36, 36, 24 };
// 中间段权重高 (中间亮度更可靠), 两端权重低
```

#### 步骤 2: 有效段筛选与加权累加
```cpp
for (i = 0; i < segs; i++) {
    if (wp_input[i*4] < (2048 * 8 / segs)) {
        k_weight = 0;  // 白点数不足, 忽略此段
    } else {
        k_weight = seg_k_weight[i];
        // R 增益 = G 和 / R 和 * 256 (Q8 格式)
        rgain += (wp_input[i*4+2]) * k_weight * 256 / wp_input[i*4+1];
        // B 增益 = G 和 / B 和 * 256 (Q8 格式)
        bgain += (wp_input[i*4+2]) * k_weight * 256 / wp_input[i*4+3];
    }
    k_weight_all += k_weight;
}
```

#### 步骤 3: 归一化输出
```cpp
if (k_weight_all == 0) {
    *r_gain = 256;  // 默认 1.0x
    *b_gain = 256;
} else {
    *r_gain = clip(rgain / k_weight_all, 0, 1023);
    *b_gain = clip(bgain / k_weight_all, 0, 1023);
}
*g_gain = 256;  // G 始终为基准 1.0x
```

**关键特点**:
- 使用中间亮度区域为主 (权重 36), 极暗/极亮区域为辅 (权重 24)
- 白点数量不足的段被自动忽略
- 采用 `(G/R)` 和 `(G/B)` 比率, 以 G 通道为白平衡基准

### 4.5 AWBImg (图像白平衡校正)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 1020-1072)

**函数签名**:
```cpp
ISP_API void AWBImg(
    void *awb_in_img,             // 输入: RAW 图像
    int polarity_mode,            // Bayer 排列模式
    int image_width,              // 图像宽度
    int image_height,             // 图像高度
    int* gain_values,             // 输入: 增益值 [R, G, B]
    int awb_de_high_red_class,    // 输入: 高红区域分类等级
    int awb_de_high_blue_class,   // 输入: 高蓝区域分类等级
    int awb_de_high_red_rate,     // 输入: 高红区域衰减速率
    int awb_de_high_blue_rate,    // 输入: 高蓝区域衰减速率
    void *awb_out_img             // 输出: 校正后图像
);
```

**算法原理**:

#### 步骤 1: 增益提取
```cpp
r_gain = gain_values[0];  // Q8 格式
g_gain = gain_values[1];
b_gain = gain_values[2];
```

#### 步骤 2: 高红/高蓝阈值计算
```cpp
awb_de_high_red_th  = 1023 - (1 << (6 + awb_de_high_red_class));
awb_de_high_blue_th = 1023 - (1 << (6 + awb_de_high_blue_class));
```
例如: `class=2` 时, `threshold = 1023 - 256 = 767`

#### 步骤 3: B 通道处理 (含高红保护)
```cpp
if (chanel_num == chanel_num_b) {
    gain = b_gain;
    // 当 B 增益 < 1.0x 且像素值很高 (接近饱和) 时, 启用高红保护
    if (b_gain < 256 && awb_de_high_red_class > 0 && pixel > awb_de_high_red_th) {
        // 计算混合比率: 像素值越高, rate 越小, 增益越接近 1.0x
        rate = ((1023 - pixel) * 256 + (pixel - awb_de_high_red_th) * awb_de_high_red_rate)
               >> (8 + awb_de_high_red_class);
        rate = clip(rate, 0, 255);
        // 混合增益: gain * rate/256 + 256 * (1 - rate/256)
        gain = (b_gain * rate + 256 * (256 - rate)) >> 8;
    }
}
```

#### 步骤 4: R 通道处理 (含高蓝保护)
逻辑与 B 通道对称, 防止 R 增益过小导致高蓝区域过曝。

#### 步骤 5: 增益应用
```cpp
out_pixel = clip(in_pixel * gain / 256, 0, 1023);
```

**关键特点**:
- **高红保护**: 当 B 增益 < 1.0x 时, 对高亮度 B 像素降低校正强度, 防止红/橙色区域偏色
- **高蓝保护**: 当 R 增益 < 1.0x 时, 对高亮度 R 像素降低校正强度, 防止蓝色区域偏色
- 混合比率通过 `class` 和 `rate` 参数精细控制保护范围和强度

### 4.6 AWB_IQ (白平衡质量评估)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 784-830)

**函数签名**:
```cpp
ISP_API void AWB_IQ(
    short **img_buffer,           // 输入: Demosaic 后的 RGB 图像 [3][w*h]
    int img_width,                // 图像宽度
    int img_height,               // 图像高度
    int polarity,                 // Bayer 排列
    unsigned int *x,              // 输入: 最多 6 个 ROI 区域的 X 坐标
    unsigned int *y,              // 输入: 最多 6 个 ROI 区域的 Y 坐标
    unsigned int *width,          // 输入: 最多 6 个 ROI 区域的宽度
    unsigned int *height,         // 输入: 最多 6 个 ROI 区域的高度
    double* rg_iq,                // 输出: R/G 比值
    double* bg_iq                 // 输出: B/G 比值
);
```

**算法原理**:

1. **在指定 ROI 区域累加 RGB 值** (处理的是 Demosaic 后的 RGB 图像, 非 Bayer RAW)
2. **计算平均值**:
   - `avg_r = sum_r / num`
   - `avg_g = sum_g / num`
   - `avg_b = sum_b / num`
3. **计算 RG/BG 比率**:
   - `*rg_iq = avg_g / avg_r`
   - `*bg_iq = avg_g / avg_b`
4. **质量判定**:
   ```cpp
   if (rg_iq > 0.92 && rg_iq < 1.08 && bg_iq > 0.92 && bg_iq < 1.08)
       printf("AWB is perfect!\n");
   else
       printf("AWB needs correction!\n");
   ```

**关键特点**:
- 输入为 RGB 三分立图像 (Demosaic 后), 非 Bayer RAW
- 评估标准: R/G/B 比率在 0.92~1.08 (即偏差<8%) 视为优秀

---

## 五、数据模型实现

### 5.1 核心属性

**文件**: `d:\jrx\zl\isptool\ThunderSE\DeviceConfig\Isp\AutoWhiteBalance.cs`

| 属性 | 类型 | 访问 | 说明 |
|------|------|------|------|
| Seg_Mode | int | get/set | 分段模式 (0-7) |
| RGainStart | int | get/set | R 增益起始值 |
| RGainMin | int | get/set | R 增益最小值 |
| RGainMax | int | get/set | R 增益最大值 |
| Awb_Weight_In | int | get/set | 区间内权重 |
| Awb_Weight_Out | int | get/set | 区间外权重 |
| Awb_YMin | int | get/set | Y 最小值 |
| Awb_YMax | int | get/set | Y 最大值 |
| Awb_Yuv_Mod_En | int | get/set | YUV 模式使能 |
| Awb_Cb_Th | int[8] | get/set | Cb 阈值表 |
| Awb_Cr_Th | int[8] | get/set | Cr 阈值表 |
| Awb_Cbcr_Th | int[8] | get/set | CbCr 联合阈值表 |
| Awb_Ycbcr_Th | byte | get/set | YCbCr 综合阈值 |
| Awb_Stat_Tab | byte[128] | get/set | 统计查找表 |
| Awb_De_High_Red_Class | int | get/set | 高红分类等级 |
| Awb_De_High_Blue_Class | int | get/set | 高蓝分类等级 |
| Awb_De_High_Red_Rate | int | get/set | 高红比率 |
| Awb_De_High_Blue_Rate | int | get/set | 高蓝比率 |
| StatisticData | ObservableCollection<ObservableCollection<KeyValuePair<double, double>>> | get | 统计曲线数据 |
| GainData | Dictionary<string, KeyValuePair<int, int>> | get/set | 增益数据字典 |

### 5.2 CalcGainValue 方法完整流程

```csharp
public int[] CalcGainValue(byte[] raw_img)
{
    int[] returnData = new int[3];   // [R_gain, G_gain, B_gain]
    int[] wp_output = new int[128];  // 白点统计输出缓冲区

    if (_awb_yuv_mod_en != 0)
    {
        // 路径 1: YUV 模式
        UpdateAwbStatTab();  // 先将 StatisticData 同步到 Awb_Stat_Tab

        IspApi.AWBStatistic(raw_img, (int)_commonConfig.Bayer,
            _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight,
            _seg_mode, _awb_stat_tab, _awb_weight_in, _awb_weight_out,
            _rgainStart, _rgainMin, _rgainMax, _awb_ymin, _awb_ymax, wp_output);
    }
    else
    {
        // 路径 2: RAW 模式 (默认)
        IspApi.AWBStatistic_Yuv(raw_img, (int)_commonConfig.Bayer,
            _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight,
            _seg_mode, _awb_ymin, _awb_ymax,
            _awb_cb_th, _awb_cr_th, _awb_cbcr_th, _awb_ycbcr_th, wp_output);
    }

    // 步骤 3: 从白点统计结果中计算 R/G/B 增益
    IspApi.AWB_Gain_Soft_Cal(wp_output, _seg_mode,
        ref returnData[0], ref returnData[1], ref returnData[2]);

    return returnData;  // [R_gain, G_gain, B_gain]
}
```

### 5.3 UpdateAwbStatTab 方法

```csharp
public void UpdateAwbStatTab()
{
    byte[] tmpAwbStatTab = new byte[Awb_Stat_Tab.Length];  // 128 bytes
    Array.Clear(tmpAwbStatTab, 0, tmpAwbStatTab.Length);
    int i = 0;
    foreach (var lineStat in StatisticData)          // 外层: 4 条曲线
    {
        foreach (var item in lineStat)                // 内层: 每条 32 个点
        {
            tmpAwbStatTab[i] = (byte)item.Value;      // 取 Y 轴值
            i++;
        }
    }
    Awb_Stat_Tab = tmpAwbStatTab;  // 触发 PropertyChanged
}
```

**输入**: `StatisticData` (4 条曲线 x 32 点)  
**输出**: `Awb_Stat_Tab` (128 字节数组)

### 5.4 ProcessRawBuffer 图像处理流程

```csharp
public override void ProcessRawBuffer(ref byte[] imgBuffer)
{
    // 1. 分配输出缓冲区 (short 类型，每个像素 16bit)
    short[] outputBuffer = new short[_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight];
    
    // 2. 计算 AWB 增益
    int[] gainValues = CalcGainValue(imgBuffer);

    // 3. 调用 C++ AWBImg 执行白平衡校正
    IspApi.AWBImg(imgBuffer, (int)_commonConfig.Bayer,
        _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight,
        gainValues,
        _awb_de_high_red_class, _awb_de_high_blue_class,
        _awb_de_high_red_rate, _awb_de_high_blue_rate,
        outputBuffer);

    // 4. 将 short[] 转回 byte[] 并替换原缓冲区
    byte[] outputByteBuffer = new byte[Buffer.ByteLength(outputBuffer)];
    Buffer.BlockCopy(outputBuffer, 0, outputByteBuffer, 0, outputByteBuffer.Length);
    imgBuffer = outputByteBuffer;
}
```

### 5.5 CalcIQ 方法

```csharp
public void CalcIQ(byte[] fileBuffer, int[] x, int[] y, int[] width, int[] height,
    ref double rgIq, ref double bgIq)
{
    // 1. 分配 3 个通道的内存 (每个通道 width*height*sizeof(short))
    IntPtr[] ptrArray = new IntPtr[3];
    for (int i = 0; i < ptrArray.Length; i++)
    {
        ptrArray[i] = Marshal.AllocHGlobal(
            _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
    }

    // 2. 去马赛克: RAW Bayer → RGB 三通道
    IspApi.DemosaicImg(fileBuffer, (int)_commonConfig.Bayer,
        _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, ptrArray);

    // 3. 在指定 ROI 区域上计算 R/G 和 B/G 比值
    IspApi.AWB_IQ(ptrArray, _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight,
        (int)_commonConfig.Bayer, x, y, width, height, ref rgIq, ref bgIq);

    // 4. 释放非托管内存
    for (int i = 0; i < ptrArray.Length; i++)
    {
        Marshal.FreeHGlobal(ptrArray[i]);
    }
}
```

---

## 六、UI 实现 (AwbWindow)

### 6.1 窗口整体布局

**文件**: `d:\jrx\zl\isptool\ThunderSE\Ui\SettingWindow\Awb\AwbWindow.xaml`

```
Window (Title="AwbWindow", 1024x768, NoResize, Background="#FFE5E5E5")
  └── Grid (2 列)
        ├── Column 0 (Width="*"): 图表区域
        │     └── DockPanel
        │           ├── Bottom (30px): 范围输入控件
        │           │     └── StackPanel (水平排列)
        │           │           ├── Label "rgain__min:" + TextBox (绑定 RGainMin)
        │           │           └── Label "rgain__max:" + TextBox (绑定 RGainMax)
        │           └── Chart (DataChart) — 主图表控件
        │                 ├── X 轴: LinearAxis (0-1024)
        │                 ├── Y 轴: LinearAxis (0-256)
        │                 ├── Legend: 隐藏
        │                 └── ScatterSeries (GainDataSeries) — 散点系列
        │                       └── ItemsSource 绑定 GainData
        │
        └── Column 1 (Width="130"): 右侧按钮面板
              └── TabControl (ButtonGroupTabs)
                    ├── TabItem 0 (功能操作)
                    │     └── StackPanel
                    │           ├── Button "输入作图数据" — 绑定 LoadChartDataFileCommand
                    │           ├── Button "输出作图数据" — 绑定 SaveChartDataFileCommand
                    │           ├── Button "加载Raw" — 绑定 LoadRawFileCommand
                    │           ├── Button "画线" — Click="OnBeginDrawBezierLine"
                    │           ├── Button "查看IQ" — 绑定 ViewIQCommand
                    │           └── Button "更新StatTab" — 绑定 UpdateStatTabCommand
                    │
                    └── TabItem 1 (贝塞尔操作)
                          └── StackPanel
                                ├── Button "添加" — Click="OnAddBezierLine"
                                │     └── IsEnabled 绑定 CanAddBezierLine DP
                                ├── Button "删除" — Click="OnRemoveBezierLine"
                                │     └── IsEnabled 绑定 HasSelectedBezierLine DP
                                ├── Button "确定" — Click="OnDrawBezierLineOk"
                                └── Button "取消" — Click="OnDrawBezierLineCancel"
```

### 6.2 图表数据绑定

#### 6.2.1 GainData 散点图

**数据流**:
```
AutoWhiteBalance.GainData (Dictionary<string, KeyValuePair<int, int>>)
    → AwbWindowViewModel.GainData (直接代理)
    → AwbWindow.xaml 绑定 (经 GainDataBindingConverter)
    → ScatterSeries.ItemsSource (Collection<KeyValuePair<int, int>>)
```

**GainDataBindingConverter 转换逻辑**:
```csharp
// 输入: Dictionary<string, KeyValuePair<int, int>>
// 输出: Collection<KeyValuePair<int, int>>
// 过滤规则: 仅保留 Value.Key != -1 的项 (-1 表示无效数据)
foreach (var item in inputCollection)
{
    if (item.Value.Key != -1)
    {
        outputCollection.Add(new KeyValuePair<int,int>(item.Value.Key, item.Value.Value));
    }
}
```

**数据语义**:
- Key (IndependentValue) = RGain 值 (X 轴)
- Value (DependentValue) = BGain 值 (Y 轴)
- 字典的 string Key = Raw 文件路径（用于标识光源类型）

#### 6.2.2 StatisticData 折线图

**动态添加/删除机制** — 通过 `StatisticDataCollectionChanged` 事件监听:

```csharp
void StatisticDataCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
{
    switch (e.Action)
    {
        case Add:     → AddStatLine(newItem);     // 创建新的 LineSeries 加入 DataChart.Series
        case Remove:  → RemoveStatLine(oldItem);  // 从 DataChart.Series 移除
        case Replace: → 先 Remove 再 Add
        case Reset:   → ClearStatLine() + HasCorrectionData = true
    }
}
```

### 6.3 范围线的显示和拖拽

#### 6.3.1 三类范围线

| 线类型 | 颜色 | 绑定属性 | 用途 |
|--------|------|----------|------|
| RGainStart | 红色虚线 | `RGainStart` | 统计范围起始点 (**可拖拽**) |
| RGainEnd | 红色虚线 | `RGainEnd` | 统计范围终点 (固定 = RGainStart + 496) |
| RGainMin | 绿色虚线 | `RGainMin` | Gain 最小值参考线 (**可拖拽**) |
| RGainMax | 绿色虚线 | `RGainMax` | Gain 最大值参考线 (**可拖拽**) |

#### 6.3.2 RGainStatRangeLineBindingConverter 坐标转换

```csharp
// Canvas 坐标 = ViewModel 属性值 * (画布宽度 / 图表 X 轴最大值)
public object Convert(object value, Type targetType, object parameter, ...)
{
    int doubleVal = (int)value;
    double matrixTranslateValue = (double)parameter;  // = _rangeLinesDrawingArea.ActualWidth / _maxChartX
    return doubleVal * matrixTranslateValue;
}

// 反向转换 (拖拽后回写属性值)
public object ConvertBack(object value, Type targetType, object parameter, ...)
{
    int intVal = (int)(double)value;
    double matrixTranslateValue = (double)parameter;
    return ((int)(intVal / matrixTranslateValue)).ToString();
}
```

#### 6.3.3 RGainStart 拖拽实现

**构造方式** (代码动态创建 ControlTemplate):
```csharp
// 1. 创建 Thumb 控件
_lineStartThumb = new Thumb();

// 2. 动态构建 Template: Grid { 红色虚线 Line + 透明粗线 (用于捕获拖拽) }
FrameworkElementFactory lineFactory = new FrameworkElementFactory(typeof(Line), "Line");
lineFactory.SetValue(Line.StrokeProperty, Brushes.Red);
lineFactory.SetValue(Line.StrokeDashArrayProperty, new DoubleCollection(new double[] { 4, 2 }));
lineFactory.SetValue(Line.Y2Property, _maxCanvasY);  // 贯穿整个画布高度

FrameworkElementFactory lineForDragFactory = new FrameworkElementFactory(typeof(Line), "lineForDrag");
lineForDragFactory.SetValue(Line.StrokeProperty, Brushes.Transparent);
lineForDragFactory.SetValue(Line.StrokeThicknessProperty, 20d);  // 20 像素宽的可拖拽区域

// 3. 绑定水平位置 (双向绑定)
_lineStartThumb.SetBinding(Canvas.LeftProperty, new Binding("RGainStart")
{
    Source = DataContext,
    Converter = new RGainStatRangeLineBindingConverter(),
    ConverterParameter = _rangeLinesDrawingArea.ActualWidth / _maxChartX,
    Mode = BindingMode.TwoWay
});

// 4. 注册拖拽事件
_lineStartThumb.DragDelta += OnStatRangeStartLineDrag;
```

**OnStatRangeStartLineDrag 拖拽逻辑**:
```csharp
void OnStatRangeStartLineDrag(object sender, DragDeltaEventArgs e)
{
    var thumb = sender as Thumb;
    
    // 直接移动 Thumb 位置
    Canvas.SetLeft(thumb, Canvas.GetLeft(thumb) + e.HorizontalChange);
    
    // 计算图表坐标变化量
    var lineChangeDelta = e.HorizontalChange * _maxChartX / _rangeLinesDrawingArea.ActualWidth;
    
    // 同步移动所有贝塞尔曲线的所有控制点
    foreach (var bezierLine in _bezierLineList)
    {
        bezierLine.StartPoint.X += e.HorizontalChange;
        bezierLine.EndPoint.X += e.HorizontalChange;
        bezierLine.StartBezierPoint.X += e.HorizontalChange;
        bezierLine.EndBezierPoint.X += e.HorizontalChange;
    }
    
    // 同步移动所有统计折线图的数据点
    foreach (var line in _chartLineList)
    {
        var tmpDataContext = (ObservableCollection<KeyValuePair<double, double>>)line.DataContext;
        for (int i = 0; i < tmpDataContext.Count; i++)
        {
            tmpDataContext[i] = new KeyValuePair<double, double>(
                tmpDataContext[i].Key + lineChangeDelta,  // X 坐标偏移
                tmpDataContext[i].Value);
        }
    }
}
```

**⚠️ 潜在问题**: 拖拽时直接操作了 `Canvas.SetLeft` 移动 Thumb，但 **没有通过双向绑定回写 ViewModel 的 RGainStart 属性**。这意味着拖拽后 TextBox 中的数值不会更新。

### 6.4 贝塞尔曲线的完整操作流程

#### 6.4.1 贝塞尔曲线控件结构 (BezierFigure)

**四个控制点** (DependencyProperty):
- `StartPoint` — 曲线起点
- `EndPoint` — 曲线终点
- `StartBezierPoint` — 起点控制手柄
- `EndBezierPoint` — 终点控制手柄

**四个锁定标志**:
- `LockStartPointX` / `LockStartPointY` — 锁定起点 X/Y 方向拖拽
- `LockEndPointX` / `LockEndPointY` — 锁定终点 X/Y 方向拖拽

#### 6.4.2 添加贝塞尔曲线 (OnAddBezierLine)

```csharp
private void OnAddBezierLine(object sender, RoutedEventArgs e)
{
    var bezierLine = new BezierFigure();
    
    // 确定起点和终点位置
    if (_bezierLineList.Count > 0)
    {
        // 复用上一条曲线的起点和终点
        startPoint = _bezierLineList.Last().StartPoint;
        endPoint = _bezierLineList.Last().EndPoint;
    }
    else
    {
        // 第一条曲线: 根据 ViewModel 的 RGainStart/RGainEnd 初始化
        startPoint.X = _viewModel.RGainStart / _maxChartX * _maxCanvasX;
        startPoint.Y = _maxCanvasY / 2;  // 垂直居中
        endPoint.X = _viewModel.RGainEnd / _maxChartX * _maxCanvasX;
        endPoint.Y = _maxCanvasY / 2;
    }
    
    // 设置控制手柄 (在起点/终点基础上偏移)
    bezierLine.StartBezierPoint = new Point(startPoint.X + 100, startPoint.Y - 100);
    bezierLine.EndBezierPoint = new Point(endPoint.X - 100, endPoint.Y - 100);
    
    // 锁定 X 轴拖拽 (保证起点和终点的 X 坐标不变)
    bezierLine.LockStartPointX = true;
    bezierLine.LockEndPointX = true;
    
    // 注册选中事件
    bezierLine.SelectStateChange += OnBezierLineSelected;
    bezierLine.SetSelected();  // 设为选中状态 (蓝色)
    
    _bezierLineDrawingArea.Children.Add(bezierLine);
    _bezierLineList.Add(bezierLine);
    
    // 最多 4 条曲线
    if (_bezierLineList.Count >= 4)
    {
        CanAddBezierLine = false;
    }
}
```

#### 6.4.3 选中/取消选中

```csharp
void OnBezierLineSelected(BezierFigure bezierLine, bool isSelected)
{
    if (!isSelected)
    {
        _currentSelectBezierLine = null;
        HasSelectedBezierLine = false;
    }
    else
    {
        // 取消上一个选中
        if (_currentSelectBezierLine != null)
        {
            _currentSelectBezierLine.SetUnSelected();  // 变红色
        }
        _currentSelectBezierLine = bezierLine;
        HasSelectedBezierLine = true;  // 启用"删除"按钮
    }
}
```

#### 6.4.4 GetBezierLinePoints 曲线采样算法

```csharp
private Point[] GetBezierLinePoints(BezierFigure bezierLine)
{
    // 1. 获取贝塞尔曲线的边界矩形
    Rect bezierLineBounds = bezierLine.BezierPathGeometry.Bounds;
    double bezierLineBoundsWidth = bezierLineBounds.Width;
    
    // 2. 将 X 轴等分为 31 段 (共 32 个采样点)
    double bezierLineXAxisDivisionValue = bezierLineBoundsWidth / 31;
    
    Point[] bezierLinePoints = new Point[32];
    
    // 3. 加宽 PathGeometry 用于相交检测
    Geometry og1 = bezierLine.BezierPathGeometry.GetWidenedPathGeometry(
        new Pen(Brushes.Black, 1.0));
    
    // 4. 对每个 X 位置，构造垂直线并与贝塞尔曲线求交
    for (int i = 0; i < 32; i++)
    {
        var line = new LineGeometry(
            new Point(bezierLineBounds.Left + bezierLineXAxisDivisionValue * i, 0),
            new Point(bezierLineBounds.Left + bezierLineXAxisDivisionValue * i, 3000));
        
        Geometry og2 = line.GetWidenedPathGeometry(new Pen(Brushes.Black, 1.0));
        
        // 求交集
        CombinedGeometry cg = new CombinedGeometry(
            GeometryCombineMode.Intersect, og1, og2);
        
        // 展平为 PathGeometry 获取交点
        PathGeometry pg = cg.GetFlattenedPathGeometry();
        Point[] IntersectionPoints = new Point[pg.Figures.Count];
        
        for (int j = 0; j < pg.Figures.Count; j++)
        {
            Rect fig = new PathGeometry(new PathFigure[] { pg.Figures[j] }).Bounds;
            IntersectionPoints[j] = new Point(
                bezierLineBounds.Left + bezierLineXAxisDivisionValue * i,
                fig.Top + fig.Height / 2.0);
        }
        
        bezierLinePoints[i] = IntersectionPoints[0];
    }
    
    return bezierLinePoints;
}
```

**算法本质**:
1. 将贝塞尔曲线的 X 范围等分为 31 段
2. 在每个 X 位置构造一条垂直线
3. 通过 `CombinedGeometry.Intersect` 计算垂直线与贝塞尔曲线的交点
4. 取交点的 Y 坐标作为该 X 位置的采样值

#### 6.4.5 ProjectionBezierLinesToChart 坐标映射

```csharp
private void ProjectionBezierLinesToChart()
{
    for (int i = 0; i < _bezierLineList.Count; i++)
    {
        // 1. 采样贝塞尔曲线 (32 个点)
        var points = GetBezierLinePoints(_bezierLineList[i]);
        
        ObservableCollection<KeyValuePair<double, double>> tmpDataContext = 
            new ObservableCollection<KeyValuePair<double, double>>();
        
        foreach (var item in points)
        {
            // 2. Canvas 坐标 → 图表坐标 的转换
            //    Y 轴翻转: Canvas 原点在左上角，图表原点在左下角
            int ActualChartY = (int)((_maxCanvasY - item.Y) / _maxCanvasY * (double)_maxChartY);
            int ActualChartX = (int)(item.X / _maxCanvasX * (double)_maxChartX);
            
            tmpDataContext.Add(new KeyValuePair<double, double>(ActualChartX, ActualChartY));
        }
        
        // 3. 添加到 ViewModel 的 StatisticData
        _viewModel.StatisticData.Add(tmpDataContext);
    }
}
```

### 6.5 完整操作流程

```
[窗口加载]
    │
    ▼
Window_Loaded
    │
    ├── 获取 Chart.Template 中的关键元素
    │     ├── "BezierLineDrawingArea" → _bezierLineDrawingArea (Canvas)
    │     ├── "RangeLinesDrawingArea" → _rangeLinesDrawingArea (Canvas)
    │     └── "ChartArea" → _chartArea (EdgePanel)
    │
    ├── 注册 ChartArea 鼠标事件 (拖拽曲线点)
    ├── 注册 Chart 鼠标事件 (滚轮缩放、右键平移)
    │
    ├── DrawStatRangeLines() — 绘制 RGainStart/RGainEnd 红色范围线
    └── DrawGainValueRangeLines() — 绘制 RGainMin/RGainMax 绿色范围线


[用户点击"加载Raw"按钮]
    │
    ▼
LoadRawFileCommand
    │
    ├── OpenFileDialog (*.raw, 多选)
    │
    └── 创建 ColorblockPickingWindow
            │
            ├── 对每个 Raw 文件:
            │     ├── 读取文件字节
            │     ├── 调用 _ispProcessor.GenerateBitmapUsingRaw() 生成 Bitmap
            │     ├── 创建 ImageWithRubberBandControl (MaxBands=6)
            │     └── 添加到 TabControl
            │
            └── ShowDialog() 模态等待
                    │
                    ▼
                用户交互:
                    ├── 在图像上拖拽选择色块 (最多 6 个)
                    ├── "撤销" — 撤回最后一个色块
                    └── "确定" — OkButton_Click
                            │
                            ▼
                        对每个 TabItem:
                            ├── 提取色块坐标 (XArray, YArray, WidthArray, HeightArray)
                            ├── 调用 IspApi.AWBCal() 计算 bgain 和 rgain
                            └── correctionData[文件名] = KeyValuePair(rgain, bgain/4)
                                │
                                ▼
                            合并新数据到 _awb.GainData
                            │
                            ▼
                        散点图更新显示


[用户点击"画线"按钮]
    │
    ▼
OnBeginDrawBezierLine
    │
    ├── 显示 _bezierLineDrawingArea (Visibility=Visible)
    └── 切换到 TabItem 1 (贝塞尔操作)
            │
            ▼
        用户点击"添加" → OnAddBezierLine
            │
            ├── 创建 BezierFigure
            ├── 设置起点/终点/控制手柄
            ├── 注册选中事件
            └── 添加到 _bezierLineDrawingArea
                    │
                    ▼
                用户拖拽控制点调整曲线形状
                    │
                    ▼
                用户点击"确定" → OnDrawBezierLineOk
                    │
                    ├── ProjectionBezierLinesToChart()
                    │     ├── GetBezierLinePoints() — 采样 32 个点
                    │     └── Canvas 坐标 → 图表坐标转换
                    │
                    ├── 添加到 _viewModel.StatisticData
                    │     └─ StatisticDataCollectionChanged → AddStatLine()
                    │           └─ 创建 LineSeries 加入 DataChart
                    │
                    └── 隐藏 _bezierLineDrawingArea，切回 TabItem 0


[用户点击"更新StatTab"按钮]
    │
    ▼
UpdateStatTabCommand
    │
    └── _viewModel.UpdateStatTab()
            │
            ├── 将 StatisticData (4 条曲线 x 32 点) 展平为 byte[128]
            └── Awb_Stat_Tab = tmpAwbStatTab
                └─ 触发 PropertyChanged → HasChangedParams = true


[用户点击"查看IQ"按钮]
    │
    ▼
ViewIQCommand
    │
    └── 创建 AwbIQWindow
            │
            ├── 加载 Raw 文件
            ├── 选取色块 (最多 6 个)
            └── 点击"计算" → OnCalcIQClick
                    │
                    ├── 调用 _awbStep.CalcIQ(...)
                    │     ├── DemosaicImg (Bayer → RGB)
                    │     └── AWB_IQ (计算 rgIq, bgIq)
                    │
                    └── DataGrid 显示结果
                          ├── r_gain 值及其范围 [0.92, 1.08]
                          └── b_gain 值及其范围 [0.92, 1.08]
```

---

## 七、色块选取窗口 (ColorblockPickingWindow)

### 7.1 窗口结构

```
ColorblockPickingWindow (734x526)
  └── Grid (2 行)
        ├── Row 0 (Height="*")
        │     └── TabControl (RawImgsTab, TabStripPlacement=Left)
        │           └── 每个 TabItem 对应一个 Raw 文件
        │                 └── ImageWithRubberBandControl — 带橡皮筋选择的图像控件
        │                       ├── MaxBands = 6 (最多 6 个色块)
        │                       └── DataContext = List<RubberBandData>
        └── Row 1 (Height="35")
              └── DockPanel
                    └── StackPanel (右对齐)
                          ├── Button "撤销" — UndoButton_Click
                          ├── Button "<上一张" — BeforePicButton_Click
                          ├── Button "下一张>" — NextPicButton_Click
                          └── Button "确定" — OkButton_Click
```

### 7.2 数据流

```
1. AwbWindowViewModel.LoadRawFiles()
   ├─ 打开 OpenFileDialog (*.raw, 多选)
   ├─ 创建 ColorblockPickingWindow
   ├─ 创建 Dictionary<string, KeyValuePair<int, int>> correctionData 作为 DataContext
   └─ ShowDialog() 模态等待

2. ColorblockPickingWindow.Window_Loaded
   └─ 对每个 Raw 文件:
       ├─ 读取文件字节
       ├─ 调用 _ispProcessor.GenerateBitmapUsingRaw() 生成 Bitmap
       ├─ 创建 ImageWithRubberBandControl (MaxBands=6)
       ├─ 创建 List<RubberBandData> 作为 DataContext
       └─ 添加到 TabControl

3. 用户交互:
   ├─ 在图像上拖拽选择色块 (最多 6 个)
   ├─ "撤销" — 调用 imgControl.UndoDrawRubberBand()
   ├─ "上一张/下一张" — 切换 TabItem
   └─ "确定" — OkButton_Click

4. OkButton_Click:
   ├─ 对每个 TabItem:
   │   ├─ 提取色块坐标 (XArray, YArray, WidthArray, HeightArray)
   │   ├─ 调用 IspApi.AWBCal() 计算 bgain 和 rgain
   │   └─ correctionData[文件名] = KeyValuePair(rgain, bgain/4)
   ├─ DialogResult = true
   └─ Close()

5. 回到 LoadRawFiles():
   └─ 合并新数据到 _awb.GainData
```

### 7.3 色块选取关键代码

```csharp
private void OkButton_Click(object sender, RoutedEventArgs e)
{
    foreach (var item in _rawImageBufferList)
    {
        int[] XArray = new int[6];
        int[] YArray = new int[6];
        int[] HeightArray = new int[6];
        int[] WidthArray = new int[6];
        
        var correctionData = (Dictionary<string, KeyValuePair<int, int>>)DataContext;
        var dataItem = _rubberBandDataList[item.Key];
        
        if (dataItem.Count > 0)
        {
            for (int j = 0; j < dataItem.Count; j++)
            {
                XArray[j] = dataItem[j].x;
                YArray[j] = dataItem[j].y;
                HeightArray[j] = dataItem[j].height;
                WidthArray[j] = dataItem[j].width;
            }
            
            int bgain = 0, rgain = 0;
            
            // 调用 C++ ISP API 计算色块的平均 RGain/BGain
            IspApi.AWBCal(tmpBuffer, width, height, bayerFormat,
                XArray, YArray, WidthArray, HeightArray, ref bgain, ref rgain);
            
            correctionData[item.Key] = new KeyValuePair<int, int>(rgain, bgain / 4);
        }
        else
        {
            // 未选取色块时记录 (-1, -1)，前端会过滤掉
            correctionData[item.Key] = new KeyValuePair<int, int>(-1, -1);
        }
    }
    
    DialogResult = true;
    Close();
}
```

**注意**: `bgain / 4` 的除法操作是硬编码的，可能是因为 C++ 端返回的 bgain 是 4 倍精度。

---

## 八、IQ 窗口实现 (AwbIQWindow)

### 8.1 窗口结构

```
AwbIQWindow (800x600)
  └── Grid (3 行)
        ├── Row 0 (Height="*")
        │     └── ImageWithRubberBandControl (RawImg)
        │           └── 用于加载 Raw 并选取色块
        ├── Row 1 (Height="140")
        │     └── DataGrid
        │           └── ItemsSource 绑定 View (ICollectionView)
        │                 ├── 列: 项 (Name)
        │                 ├── 列: 值 (Value)
        │                 ├── 列: 范围 (ValueRange)
        │                 └── 列: 是否在范围内 (IsGoodValue)
        └── Row 2 (Height="35")
              └── DockPanel
                    └── StackPanel (右对齐)
                          ├── Button "加载Raw" — OnLoadRawButtonClick
                          ├── Button "撤销" — OnUndoClick
                          └── Button "计算" — OnCalcIQClick
```

### 8.2 IQ 计算流程

```csharp
private void OnCalcIQClick(object sender, RoutedEventArgs e)
{
    double r_gain = 0, b_gain = 0;
    
    // 准备色块坐标数组
    int[] XArray = new int[6];
    int[] YArray = new int[6];
    int[] HeightArray = new int[6];
    int[] WidthArray = new int[6];
    
    if (_rubberBandData.Count > 0)
    {
        for (int j = 0; j < _rubberBandData.Count; j++)
        {
            XArray[j] = _rubberBandData[j].x;
            YArray[j] = _rubberBandData[j].y;
            HeightArray[j] = _rubberBandData[j].height;
            WidthArray[j] = _rubberBandData[j].width;
        }
    }
    
    // 调用 C++ IQ 计算
    _awbStep.CalcIQ(_rawFileBuffer, XArray, YArray, WidthArray, HeightArray, ref r_gain, ref b_gain);
    
    // 构建 IQ 结果 (含合格判定)
    _awbIQ[0] = new IQData("r_gain", r_gain,
        _iQRangeDictionary["r_gain"].Min + "-" + _iQRangeDictionary["r_gain"].Max,
        r_gain >= _iQRangeDictionary["r_gain"].Min && r_gain <= _iQRangeDictionary["r_gain"].Max);
    
    _awbIQ[1] = new IQData("b_gain", b_gain,
        _iQRangeDictionary["b_gain"].Min + "-" + _iQRangeDictionary["b_gain"].Max,
        b_gain >= _iQRangeDictionary["b_gain"].Min && b_gain <= _iQRangeDictionary["b_gain"].Max);
    
    View = CollectionViewSource.GetDefaultView(_awbIQ);
}
```

### 8.3 IQ 合格范围

```csharp
private Dictionary<string, ValueRange> _iQRangeDictionary = new Dictionary<string, ValueRange>()
{
    {"r_gain", new ValueRange(0.92, 1.08)},
    {"b_gain", new ValueRange(0.92, 1.08)}
};
```

合格范围是硬编码的 R/B Gain 在 **0.92 ~ 1.08** 之间 (偏差 < 8%)。

---

## 九、数据序列化

### 9.1 XML 序列化 (配置文件)

```csharp
public override XmlElement SerializeToXmlElement(XmlDocument xmlDoc)
{
    var xmlElement = xmlDoc.CreateElement("Awb");

    // 标量参数
    XmlElement segModeNode = xmlDoc.CreateElement("Awb_Seg_Mode");
    segModeNode.AppendChild(xmlDoc.CreateTextNode(Seg_Mode.ToString()));
    xmlElement.AppendChild(segModeNode);

    // ... 其他标量参数

    // 数组参数: 逗号分隔字符串
    XmlElement statTabNode = xmlDoc.CreateElement("Awb_Stat_Tab");
    string statTabStr = string.Join(",", Awb_Stat_Tab.Select(x => x.ToString()).ToArray());
    statTabNode.AppendChild(xmlDoc.CreateTextNode(statTabStr));
    xmlElement.AppendChild(statTabNode);

    return xmlElement;
}
```

**XML 格式示例**:
```xml
<Awb>
    <Awb_Seg_Mode>3</Awb_Seg_Mode>
    <Awb_Weight_In>7</Awb_Weight_In>
    <Awb_Weight_Out>3</Awb_Weight_Out>
    <Awb_Rg_Start>170</Awb_Rg_Start>
    <Awb_Rgain_Min>170</Awb_Rgain_Min>
    <Awb_Rgain_Max>440</Awb_Rgain_Max>
    <Awb_YMin>16</Awb_YMin>
    <Awb_YMax>192</Awb_YMax>
    <Awb_Stat_Tab>154,154,154,...,86</Awb_Stat_Tab>
    <Awb_Yuv_En>0</Awb_Yuv_En>
    <Awb_Cb_Th>8,16,24,32,40,48,48,48</Awb_Cb_Th>
    <Awb_Cr_Th>8,16,24,32,40,48,48,48</Awb_Cr_Th>
    <Awb_Cbcr_Th>12,24,36,48,60,72,72,72</Awb_Cbcr_Th>
    <Awb_Ycbcr_Th>10</Awb_Ycbcr_Th>
    <Awb_De_High_Red_Class>3</Awb_De_High_Red_Class>
    <Awb_De_High_Blue_Class>3</Awb_De_High_Blue_Class>
    <Awb_De_High_Red_Rate>0</Awb_De_High_Red_Rate>
    <Awb_De_High_Blue_Rate>0</Awb_De_High_Blue_Rate>
</Awb>
```

### 9.2 XML 反序列化

```csharp
public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
{
    var awbNode = ispToolDataNode["Awb"];

    Seg_Mode = XmlHelper.GetNodeInt(awbNode, "Awb_Seg_Mode");
    // ... 所有标量参数通过 XmlHelper 安全解析 ...

    // 数组参数: 先获取字符串，再 Split 后转换
    var tmpStatTabStr = XmlHelper.GetNodeValue(awbNode, "Awb_Stat_Tab");
    if (tmpStatTabStr != null)
    {
        Awb_Stat_Tab = tmpStatTabStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Convert.ToByte(s))
            .ToArray();
    }
    // ... 类似处理 Awb_Cb_Th, Awb_Cr_Th, Awb_Cbcr_Th ...
}
```

### 9.3 二进制序列化 (设备烧录用)

**Getter**:
```csharp
AwbParams awbParams = new AwbParams()
{
    seg_mode = Seg_Mode,
    rg_start = RGainStart,
    // ... 其他字段
    // 注意: seg_gain, manu_rgain/ggain/bgain, rgain/ggain/bgain 未赋值 (默认为 0)
};

int size = Marshal.SizeOf(awbParams);  // 376 bytes
byte[] arr = new byte[size];
IntPtr ptr = Marshal.AllocHGlobal(size);
Marshal.StructureToPtr(awbParams, ptr, true);
Marshal.Copy(ptr, arr, 0, size);
Marshal.FreeHGlobal(ptr);

return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
```

**⚠️ 风险**: `seg_gain[24]`、`manu_rgain/ggain/bgain`、`rgain/ggain/bgain` 在 getter 中**未赋值**，将以零值写入二进制数据。

### 9.4 .ispawb 文件格式 (图表数据)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<AwbChartData>
    <RGainStart>170</RGainStart>
    <RGainMin>170</RGainMin>
    <RGainMax>440</RGainMax>
    <StatData>120,115,110,...  <!-- 所有曲线的所有点的 Value 值，逗号分隔 --></StatData>
    <GainValueData>
        <Value Path="D:\raw\daylight.raw">256,128</Value>
        <Value Path="D:\raw\cloudy.raw">280,140</Value>
        <!-- Path=文件路径, Value="RGain,BGain" -->
    </GainValueData>
</AwbChartData>
```

---

## 十、已知问题清单

### 10.1 高严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B1 | seg_gain[24] 在二进制序列化中始终为零 | 分段增益丢失 | 在 Getter 中赋值 |
| B2 | manu_rgain/ggain/bgain 等 6 个字段未序列化 | 手动增益丢失 | 在 Getter/Setter 中处理 |
| B3 | RGainStart 拖拽不回写 ViewModel | TextBox 与线位置不同步 | 拖拽结束时回写属性 |

### 10.2 中严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B4 | AWBStatistic 与 AWBStatistic_Yuv 命名反转 | 代码混淆 | 重命名函数或调整分支 |
| B5 | 贝塞尔曲线采样性能差 | 频繁刷新时卡顿 | 缓存采样结果 |
| B6 | CalcIQ 缺少 try-finally | 异常时内存泄漏 | 添加 try-finally |

### 10.3 低严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B7 | bgain/4 除法硬编码 | 缺乏注释，C++ 端变化会导致错误 | 添加注释或动态计算 |
| B8 | IQ 合格范围硬编码 | 无法动态配置 | 提取为配置 |
| B9 | 窗口关闭未清理资源 | 可能内存泄漏 | 显式调用 Cleanup |

---

## 十一、关键文件清单

### 数据模型

| 文件 | 路径 | 职责 |
|------|------|------|
| AutoWhiteBalance.cs | `DeviceConfig/Isp/AutoWhiteBalance.cs` | AWB 数据模型、算法封装 |
| ProcessStep.cs | `DeviceConfig/Isp/ProcessStep.cs` | 抽象基类 |
| CommonConfig.cs | `DeviceConfig/Isp/CommonConfig.cs` | 公共配置 (分辨率、Bayer) |
| IspApi.cs | `DeviceConfig/Isp/IspApi.cs` | P/Invoke 声明 |

### UI

| 文件 | 路径 | 职责 |
|------|------|------|
| AwbWindow.xaml | `Ui/SettingWindow/Awb/AwbWindow.xaml` | AWB 调试窗口 XAML |
| AwbWindow.xaml.cs | `Ui/SettingWindow/Awb/AwbWindow.xaml.cs` | AWB 窗口代码隐藏 (711 行) |
| AwbWindowViewModel.cs | `Ui/SettingWindow/Awb/AwbWindowViewModel.cs` | AWB 窗口 ViewModel |
| ColorblockPickingWindow.xaml | `Ui/SettingWindow/Awb/ColorblockPickingWindow.xaml` | 色块选取窗口 |
| ColorblockPickingWindow.xaml.cs | `Ui/SettingWindow/Awb/ColorblockPickingWindow.xaml.cs` | 色块选取代码隐藏 |
| AwbIQWindow.xaml | `Ui/SettingWindow/Awb/AwbIQWindow.xaml` | IQ 分析窗口 |
| AwbIQWindow.xaml.cs | `Ui/SettingWindow/Awb/AwbIQWindow.xaml.cs` | IQ 窗口代码隐藏 |
| BezierFigure.cs | `Ui/SettingWindow/Awb/CustomControls/BezierFigure.cs` | 贝塞尔曲线控件 |
| ThumbPoint.cs | `Ui/SettingWindow/Awb/CustomControls/ThumbPoint.cs` | 拖拽控点控件 |
| Dictionary1.xaml | `Resources/Dictionary1.xaml` | 样式资源 (BezierFigure 模板、Chart 模板) |

### C++ 算法

| 文件 | 路径 | 职责 |
|------|------|------|
| IQ.cpp | `IspApi/source/IQ.cpp` (行 724-1072) | AWBCal, AWBStatistic, AWBStatistic_Yuv, AWB_Gain_Soft_Cal, AWBImg, AWB_IQ |
| Export.h | `IspApi/source/Export.h` | C 接口导出 |
| IQ.h | `IspApi/include/IQ.h` | 结构体定义 (awb_rect, iq_config) |

---

## 十二、附录

### 12.1 Bayer 模式与 Polarity 映射

| Bayer 模式 | Polarity 值 | R 位置 | G 位置 | B 位置 |
|-----------|:---:|---------|--------|--------|
| RGRG (RGGB) | 0 | (偶, 偶) | (偶, 奇)/(奇, 偶) | (奇, 奇) |
| GRGR (GRBG) | 1 | (偶, 奇) | (偶, 偶)/(奇, 奇) | (奇, 偶) |
| BGBG (BGGR) | 2 | (奇, 奇) | (偶, 奇)/(奇, 偶) | (偶, 偶) |
| GBGB (GBRG) | 3 | (奇, 偶) | (偶, 偶)/(奇, 奇) | (偶, 奇) |

### 12.2 增益编码规则

| 输入值 (Q8 格式) | 实际增益倍率 | 物理含义 |
|-----------------|-------------|---------|
| 0 | 0.0x | 完全抑制 |
| 128 | 0.5x | 减半 |
| 256 | 1.0x | 单位增益 (基准) |
| 512 | 2.0x | 加倍 |
| 1023 | ~4.0x | 最大增益 |

### 12.3 色彩空间转换公式

| 转换 | 公式 |
|------|------|
| BT.601 亮度 | `Y = (R*77 + G*150 + B*29) / 256` |
| RGB → YCbCr (Cb) | `Cb = (-R*43 - G*85 + B*128) / 256` |
| RGB → YCbCr (Cr) | `Cr = (R*128 - G*107 - B*21) / 256` |
| sRGB 反伽马 | `x > 0.04045 ? pow((x+0.055)/1.055, 2.4) : x/12.92` |
| RGB → XYZ (D65) | `X = R*0.4124 + G*0.3576 + B*0.1805`<br>`Y = R*0.2126 + G*0.7152 + B*0.0722`<br>`Z = R*0.0193 + G*0.1192 + B*0.9505` |
| XYZ → Lab | `L* = 116 * f(Y/Yn) - 16`<br>`a* = 500 * (f(X/Xn) - f(Y/Yn))`<br>`b* = 200 * (f(Y/Yn) - f(Z/Zn))` |
| XYZ2LAB | `x > 0.008856 ? pow(x, 1/3) : 7.787*x + 0.1379` |

---

**文档版本**: v1.0  
**创建日期**: 2026年4月8日  
**分析依据**: 项目源码完整阅读与交叉引用  
**文档状态**: 完整

---

**文档结束**

本文档基于 ThunderSE 项目实际代码深入分析生成，涵盖 AWB 模块的完整参数定义、C++ 算法原理 (AWBCal/AWBStatistic/AWBStatistic_Yuv/AWB_Gain_Soft_Cal/AWBImg/AWB_IQ)、数据序列化规范、UI 交互设计 (AwbWindow/ColorblockPickingWindow/AwbIQWindow/贝塞尔曲线编辑) 和设备通信协议。可作为 AWB 模块开发、调试、测试和维护的参考文档。
