# YGamma (亮度 Gamma 校正) 模块详细需求规格说明

## 文档信息

| 项目 | 内容 |
|------|------|
| **模块名称** | YGamma (Luma Gamma Correction / 亮度 Gamma 校正) |
| **DeviceModulePos** | 7 |
| **IspModule 枚举值** | `IspModule.YGamma` |
| **文档版本** | v1.0 |
| **创建日期** | 2026年4月8日 |
| **分析依据** | 项目源码完整阅读与交叉引用 |

---

## 一、模块概述

### 1.1 功能描述

YGamma 模块是 ISP 图像处理管线中的**全局亮度 Gamma 校正模块**，负责对图像的亮度通道应用 Gamma 校正曲线（256 点查找表），调整图像的对比度和亮度响应，使图像符合人眼的感知特性或显示器的电光转换特性。

**核心功能**:
- 通过 256 点查找表 (LUT) 进行亮度映射
- 支持图形化拖拽调整 20 个关键控制点
- 自动线性插值生成完整的 256 点 Gamma 曲线
- 支持导入/导出 Gamma 表文件（十六进制/十进制格式）
- 提供在线 IQ（视频流）和离线 IQ（静态文件）质量评估
- 支持 6 阶和 13 阶灰度卡分析

### 1.2 物理原理

**Gamma 校正的必要性**:
1. **显示器非线性**: CRT/LCD/OLED 显示器的亮度响应不是线性的，需要预校正
2. **人眼感知非线性**: 人眼对暗部变化更敏感（韦伯-费希纳定律），需要压缩高光、扩展暗部
3. **数据传输优化**: Gamma 编码可以更高效地利用有限的位深（8-bit/10-bit）

**校正原理**:
- 计算图像的亮度值：`Y = (R*77 + G*150 + B*29) / 256`（BT.601 权重）
- 通过 Gamma 查找表获取目标亮度：`out_y = gamma_table[Y / 4]`
- 计算亮度比率增益：`gain = (out_y + pad_num) / (Y + pad_num)`
- 将增益应用到 RGB 三通道：`R_out = R_in * gain`，`G_out = G_in * gain`，`B_out = B_in * gain`
- **关键**：通过亮度比率增益应用校正，而非独立查表，确保色彩不变性（色度不受 Gamma 影响）

### 1.3 在 ISP 管线中的位置

```
Raw Bayer → BLC → LSC → AWB → Demosaic → CCM → YGamma → EE → CH → 输出
   (0)       (1)    (2)    (4)               (5)    (7)    (11)  (9)
                                              ↑ YGamma 在第 7 步
```

**前置依赖**: BLC (黑电平校正)、LSC (镜头阴影校正)、AWB (自动白平衡)

**处理类型**: RGB 域处理 (ProcessRgbBuffer) — 作用于去马赛克后的 RGB 图像

---

## 二、参数完整定义

### 2.1 参数总表

| 参数 | 类型 | 默认值 | 取值范围 | 说明 |
|------|------|--------|----------|------|
| using_ygama | short[256] | Gamma 2.2 曲线 | 0-1023 | Gamma 校正查找表 |
| Pad_Num | byte | 1 | 0-255 | 防除零保护参数 |

### 2.2 历史参数（硬编码为 0，保留用于结构体对齐）

| 参数 | 类型 | 默认值 | 原始用途推测 |
|------|------|--------|------------|
| br_mod | int | 0 | 亮度模式选择（线性/对数/自定义） |
| gma_num[8] | int[8] | 全 0 | Gamma 曲线分段参数（8 段多项式拟合系数） |
| contra_num | int | 0 | 对比度增强系数 |
| bofst | int | 0 | 全局亮度偏移（Black Offset） |
| lofst | int | 0 | 亮度通道偏移（Light Offset） |
| lcpr_low | int | 0 | 局部对比度低阈值 |
| lcpr_high | int | 0 | 局部对比度高阈值 |
| lcpr_llimt | int | 0 | 局部对比度低限幅 |
| lcpr_hlimt | int | 0 | 局部对比度高限幅 |

**保留原因**: 结构体大小需要与硬件/固件的内存布局保持一致。`ParamsDataCollection` 用于将参数序列化/反序列化为字节数组，通过 `Marshal.StructureToPtr` 在 C# 和 C++ 之间传递。

### 2.3 using_ygama[256] Gamma 表详解

**数据结构**:
```csharp
private short[] _yGammaTable = new short[256];  // 索引 0-255
```

| 维度 | 范围 | 位宽 | 说明 |
|------|------|------|------|
| 索引 (输入) | 0-255 | 8-bit | 归一化后的亮度值（10-bit Y / 4） |
| 值 (输出) | 0x000-0x3FF (0-1023) | 10-bit | 校正后的亮度值 |

**默认值** (标准 Gamma ~2.2 曲线，前 8 个和最后一个):

| 索引 | 十进制值 | 十六进制 | 说明 |
|------|----------|----------|------|
| 0 | 0 | 0x000 | 纯黑输入 → 纯黑输出 |
| 1 | 141 | 0x08D | 极暗部 |
| 2 | 181 | 0x0B5 | 暗部 |
| 3 | 209 | 0x0D1 | 暗部 |
| 4 | 232 | 0x0E8 | 暗部 |
| 5 | 251 | 0x0FB | 暗-中过渡 |
| 6 | 268 | 0x10C | 暗-中过渡 |
| 7 | 283 | 0x11B | 暗-中过渡 |
| ... | ... | ... | ... |
| 127 | 706 | 0x2C2 | 中间调（约 69% 输出） |
| ... | ... | ... | ... |
| 255 | 1023 | 0x3FF | 纯白输入 → 最大输出 |

**曲线特征**:
- 暗部 (0-63): 增长缓慢，值从 0 升至约 0x290 (656)，约占总输出的 64%
- 中间调 (64-191): 从约 0x290 (656) 升至约 0x360 (864)，约 84%
- 高光 (192-255): 从约 0x360 (864) 升至 0x3FF (1023)，缓慢逼近最大值

这符合 Gamma 2.2 曲线 `output = 255 * (input/255)^(1/2.2)` 的近似形状，但被映射到 10-bit 输出空间。

### 2.4 20 个关键点完整 X 值列表

ViewModel 中定义的关键点 X 坐标:
```csharp
private int[] _yGammaKeyPointXValues = new int[]{
    0, 1, 3, 6, 10, 16, 26, 39, 55, 71, 87, 103, 119, 135, 151, 167, 191, 223, 239, 255
};
```

| 序号 | X 值 (输入) | 区间宽度 | 区间名称 | 设计意图 |
|------|------------|---------|---------|---------|
| 0 | 0 | - | 黑点 | 确保纯黑映射到纯黑 |
| 1 | 1 | 1 | 极暗 | 超密集采样 |
| 2 | 3 | 2 | 极暗 | 密集采样 |
| 3 | 6 | 3 | 暗部 | 密集采样 |
| 4 | 10 | 4 | 暗部 | 密集采样 |
| 5 | 16 | 6 | 暗部 | 密集采样 |
| 6 | 26 | 10 | 暗-中过渡 | 过渡区采样 |
| 7 | 39 | 13 | 暗-中过渡 | 过渡区采样 |
| 8 | 55 | 16 | 中间调 | 均匀采样 |
| 9 | 71 | 16 | 中间调 | 均匀采样 |
| 10 | 87 | 16 | 中间调 | 均匀采样 |
| 11 | 103 | 16 | 中间调 | 均匀采样 |
| 12 | 119 | 16 | 中间调 | 均匀采样 |
| 13 | 135 | 16 | 中间调 | 均匀采样 |
| 14 | 151 | 16 | 中间调 | 均匀采样 |
| 15 | 167 | 16 | 中-亮过渡 | 过渡区采样 |
| 16 | 191 | 24 | 中-亮过渡 | 较宽区间 |
| 17 | 223 | 32 | 高光 | 最宽区间 |
| 18 | 239 | 16 | 高光 | 密集采样 |
| 19 | 255 | 16 | 白点 | 确保纯白映射到最大输出 |

**设计原理**:
- **暗部 (0-39)**: 7 个关键点覆盖 40 个输入值，平均每 5.7 个输入值一个关键点
- **中间调 (39-191)**: 9 个关键点，间距固定 16
- **高光 (191-255)**: 4 个关键点

这符合**韦伯-费希纳定律**（人眼对暗部变化更敏感），暗部密集采样确保精细控制，高光稀疏采样减少 UI 交互复杂度。

### 2.5 Pad_Num 参数的作用

**定义**:
```csharp
private byte _pad_num = 1;  // 默认值为 1

public byte PadNum
{
    get { return _pad_num; }
    set { _pad_num = value; HasChangedParams = true; /* ... */ }
}
```

**在 C++ 端的作用**:
```cpp
output = input * (out_y + pad_num) / (img_y + pad_num)
```

| pad_num 值 | 效果 | 说明 |
|-----------|------|------|
| 0 | 当 img_y = 0 时除零溢出 | **不可用** |
| 1 (默认) | 黑像素增益 = 1，最小保护 | **推荐**，适合正常亮度范围 |
| 10 | 整体增益对比度被压缩 | 降低暗部增益，适合高动态范围 |

**功能**:
1. **防除零保护**: 当输入亮度 `img_y` 接近 0 时，防止除零溢出
2. **增益调节**: 增大 `pad_num` 会降低整体增益对比度，特别是暗部区域
3. **黑点保护**: 默认值 1 确保黑像素（img_y=0）的增益 = `(out_y + 1) / 1`，对于默认 Gamma 表 out_y=0，增益 = 1（不变）

---

## 三、C++ 算法实现

### 3.1 YGammaImg (图像处理)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 1517-1545)

**函数签名**:
```cpp
ISP_API void YGammaImg(
    int w,                           // 图像宽度
    int h,                           // 图像高度
    int pad_num,                     // 防除零保护参数
    unsigned int* global_gamma_table, // 256 元素 Gamma 查找表
    short **input_img,                // 输入 RGB 三通道图像 [3][w*h]
    short **output_img                // 输出 RGB 三通道图像 [3][w*h]
);
```

#### 3.1.1 算法流程

**步骤 1: 计算亮度图 (Y Image)**

```cpp
// 分配亮度图内存
Pix *img_y = (Pix *)malloc(sizeof(Pix)*w*h);

for (int i = 0; i < h; i++) {
    for (int j = 0; j < w; j++) {
        img_y[i*w + j] = CLIP_PIXEL(
            (input_img[0][i*w + j] * 77 + input_img[1][i*w + j] * 150 + input_img[2][i*w + j] * 29) / 256,
            0, HIGH_VAL_10BIT);  // 10-bit Y, 使用 BT.601 权重
    }
}
```

**亮度计算公式** (BT.601 权重):
```
Y = (R * 77 + G * 150 + B * 29) / 256
```

这等价于 ITU-R BT.601 标准：
```
Y = 0.299 * R + 0.587 * G + 0.114 * B
```

其中系数通过整数近似：`77/256 ≈ 0.3008`、`150/256 ≈ 0.5859`、`29/256 ≈ 0.1133`

**步骤 2: Gamma 查表 + 4 级线性插值**

```cpp
Pix out_y, out_y_plus;

for (int i = 0; i < h; i++) {
    for (int j = 0; j < w; j++) {
        // 查表 (10-bit / 4 = 8-bit 索引)
        out_y = global_gamma_table[img_y[i*w + j] / 4];

        // 4 级线性插值 (当索引 != 255 时)
        if (img_y[i*w + j] / 4 != 255) {
            out_y_plus = global_gamma_table[img_y[i*w + j] / 4 + 1];
            out_y = out_y + (out_y_plus - out_y) * (img_y[i*w + j] & 3) / 4;
        }
```

**查表算法逻辑**:
- 输入 Y 为 10-bit (0-1023)
- 索引计算：`index = Y / 4`（整除），范围 0-255
- 小数部分：`fraction = Y & 3`（取低 2 位），范围 0-3
- **4 级线性插值**：当 `index != 255` 时，在 `table[index]` 和 `table[index+1]` 之间进行线性插值
  ```
  out_y = table[index] + (table[index+1] - table[index]) * fraction / 4
  ```
- 当 `index == 255` 时（Y=1020-1023），直接使用 `table[255]`，不做插值避免越界

**插值示例**:
假设 `Y = 100`:
- `index = 100 / 4 = 25`
- `fraction = 100 & 3 = 0`
- `out_y = table[25] + (table[26] - table[25]) * 0 / 4 = table[25]`

假设 `Y = 101`:
- `index = 101 / 4 = 25`
- `fraction = 101 & 3 = 1`
- `out_y = table[25] + (table[26] - table[25]) * 1 / 4`（25% 插值）

**步骤 3: RGB 三通道亮度比率增益应用**

```cpp
        // 亮度比率增益应用到 RGB 三通道
        output_img[0][i*w + j] = CLIP_PIXEL(
            input_img[0][i*w + j] * (out_y + pad_num) / (img_y[i*w + j] + pad_num), 0, HIGH_VAL_10BIT);
        output_img[1][i*w + j] = CLIP_PIXEL(
            input_img[1][i*w + j] * (out_y + pad_num) / (img_y[i*w + j] + pad_num), 0, HIGH_VAL_10BIT);
        output_img[2][i*w + j] = CLIP_PIXEL(
            input_img[2][i*w + j] * (out_y + pad_num) / (img_y[i*w + j] + pad_num), 0, HIGH_VAL_10BIT);
    }
}
```

**亮度比率增益公式**:
```
gain = (out_y + pad_num) / (img_y + pad_num)

R_out = CLIP_PIXEL(R_in * gain, 0, 1023)
G_out = CLIP_PIXEL(G_in * gain, 0, 1023)
B_out = CLIP_PIXEL(B_in * gain, 0, 1023)
```

**关键设计**: Gamma 校正通过**亮度比率增益**应用到 RGB 三通道，而非独立查表。这确保了**色彩不变性**（色度不受 Gamma 影响），因为 R/G/B 通道都乘以相同的增益因子。

**增益示例**:
假设 `img_y = 100`, `out_y = 300`, `pad_num = 1`:
- `gain = (300 + 1) / (100 + 1) = 301 / 101 ≈ 2.98`
- 如果 `R_in = 200`: `R_out = 200 * 2.98 = 596`（裁剪到 1023 以内）

#### 3.1.2 CLIP_PIXEL 裁剪宏

```cpp
#define HIGH_VAL_10BIT   1023  // (1<<10) - 1
#define CLIP_PIXEL(val, low, high) (((val) < (low)) ? (low) : (((val) >= (high)) ? (high) : (val)))
```

**裁剪区间**: `[low, high)` — `low` 是闭区间，`high` 是开区间。

### 3.2 YGAMMA_IQ (质量评估)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 1551-1611)

**函数签名**:
```cpp
ISP_API void YGAMMA_IQ(
    double *gr_avg,     // 输入: R 通道平均值数组 (6 或 13 元素)
    double *gg_avg,     // 输入: G 通道平均值数组
    double *gb_avg,     // 输入: B 通道平均值数组
    int num,            // 输入: 阶数 (6 或 13)
    double *diff_l,     // 输入: 可分辨亮度差异阈值 (默认全 10)
    int *count,         // 输出: 可分辨阶数
    double *l_var,      // 输出: L* 值数组
    double *delta_l,    // 输出: 相邻阶 L* 差值
    double *y_max,      // 输出: 最大亮度
    double *y_avg,      // 输出: 平均亮度数组 (13 阶时为 39 元素)
    double *out_gamma   // 输出: 计算出的 Gamma 值
);
```

#### 3.2.1 6 阶灰度卡分析

**目的**: 评估灰度卡的可分辨阶数，基于 CIE L*a*b* 色彩空间的亮度感知均匀性。

**步骤 1: sRGB 反伽马校正 (线性化)**

```cpp
for (int i = 0; i < num; i++) {
    r_var = gamma(gr_avg[i] / 255.0);  // 归一化到 [0,1]
    g_var = gamma(gg_avg[i] / 255.0);
    b_var = gamma(gb_avg[i] / 255.0);
```

**sRGB 反伽马函数**:
```cpp
double gamma(double x) {
    return x > 0.04045 ? pow((x + 0.055) / 1.055, 2.4) : x / 12.92;
}
```

分段函数:
- 当 `x > 0.04045`: `linear = ((x + 0.055) / 1.055) ^ 2.4`
- 当 `x <= 0.04045`: `linear = x / 12.92`

**步骤 2: RGB → XYZ → L* 转换**

```cpp
y_var = r_var * 0.2126 + g_var * 0.7152 + b_var * 0.0722;  // BT.709 权重计算 Y
l_var[i] = 116.0 * XYZ2LAB(y_var / 1.0) - 16;
```

**XYZ2LAB 函数**:
```cpp
double XYZ2LAB(double x) {
    if (x > pow(6.0 / 29.0, 3.0))  // 阈值 ≈ 0.008856
        out_val = pow(x, 1.0 / 3.0);  // 立方根
    else
        out_val = 1.0 / 3.0 * pow((29.0 / 6.0), 2.0) * x + 4.0 / 29.0;  // 线性段
    return out_val;
}
```

完整 L* 计算: `L* = 116 * f(Y/Yn) - 16`，其中 `Yn = 1.0`（参考白点归一化）

**步骤 3: 可分辨阶数计算**

```cpp
*count = 0;
for (int i = 0; i < num - 1; i++) {
    delta_l[i] = abs(l_var[i] - l_var[i + 1]);
    if (delta_l[i] > diff_l[i]) {
        *count = *count + 1;
    }
}
```

- 计算相邻阶 L* 差值: `delta_l[i] = |L*[i] - L*[i+1]|`
- 与阈值 `diff_l[i]` 比较，超过阈值则认为该阶"可分辨"
- `diff_l` 数组从 C# 端传入，默认值为 `{6, 10, 10, 10, 10}`（第一个阈值较宽松）

**diff_l 阈值的作用**:
- 定义人眼可感知的最小 L* 差异
- 只有当相邻灰阶的 L* 差异超过阈值时，才计为"可分辨"
- `count` = 可分辨的阶数（理想值为 5，即 6 阶全部可分辨）

#### 3.2.2 13 阶灰度卡分析

**目的**: 计算显示设备的 Gamma 值和动态范围。

**步骤 1: BT.601 亮度计算**

```cpp
for (int i = 0; i < 3 * num; i++) {
    y_avg[i] = 77 * gr_avg[i] + 150 * gg_avg[i] + 29 * gb_avg[i];
    y_avg[i] = ((double)y_avg[i]) / 256;  // 归一化
}
```

输入数组按 `[R0, G0, B0, R1, G1, B1, ...]` 交错排列，共 `3 * 13 = 39` 个值。
每阶的亮度在索引 `i * 3 + 1` 处（G 通道值）。

**步骤 2: 动态范围检查**

```cpp
*y_max = 0;
for (int i = 0; i < num; i++) {
    if (y_avg[i * 3 + 1] > *y_max)
        *y_max = y_avg[i * 3 + 1];
}

if (*y_max < 0.98 * 256)
    printf("Dynamic range warning: Maximum = %f (should be >= 0.98)\n", *y_max);
```

- 找出所有阶中的最大亮度值
- 如果最大值 < `0.98 * 256 = 250.88`，发出动态范围不足警告
- 理论上，最亮阶（白色）的归一化亮度应接近 1.0（即 256）

**步骤 3: 相邻阶灰度差值分析**

```cpp
int count = 0;
for (int i = 0; i < num; i++) {
    if (i > 0) {
        delta1 = abs(y_avg[i * 3 + 1] - y_avg[(i + 1) * 3 + 1]);
        if (delta1 > 8)
            count++;
    }
}
```

- 计算相邻阶 G 通道亮度差值
- 差值 > 8 才计为"可分辨"
- 该阈值固定为 8，不如 6 阶模式可配置

**步骤 4: Gamma 值计算**

```cpp
*out_gamma = log(0.5) / log(y_avg[6 * 3 + 1] / 256.0);
```

**计算公式**:
```
Gamma = log(0.5) / log(V_in / V_out)
```

其中:
- `V_out = 0.5`（目标输出亮度比，即 50% 灰度对应的归一化输出）
- `V_in = y_avg[6 * 3 + 1] / 256.0`（第 7 阶灰度卡的实测输入亮度比）

**原理**: 基于 Gamma 曲线的定义 `V_out = V_in ^ Gamma`，当 `V_out = 0.5` 时：
```
0.5 = V_in ^ Gamma
Gamma = log(0.5) / log(V_in)
```

第 7 阶（索引 6）通常对应 50% 灰度卡，因此用它来估算整个系统的 Gamma 值。

#### 3.2.3 6 阶 vs 13 阶对比

| 特性 | 6 阶灰度卡 | 13 阶灰度卡 |
|------|-----------|------------|
| **ROI 数量** | 6 个 | 13 个 |
| **每 ROI 子区域** | 1 个 | 3 个 (上/中/下) |
| **总采样点** | 6 | 39 |
| **色彩空间** | CIE L*a*b* (L*) | BT.601 Y 亮度 |
| **反伽马函数** | sRGB gamma() | 无 |
| **可分辨阈值** | 10 (JND) | 8 |
| **输出指标** | count, l_var, delta_l | count, y_max, y_avg, out_gamma |
| **主要用途** | 色卡颜色准确性评估 | 动态范围 + Gamma 准确性评估 |

---

## 四、数据模型实现

### 4.1 核心属性

**文件**: `d:\jrx\zl\isptool\ThunderSE\DeviceConfig\Isp\Gamma.cs`

| 属性 | 类型 | 访问 | 说明 |
|------|------|------|------|
| YGammaTable | short[256] | get/set | Gamma 校正查找表 |
| PadNum | byte | get/set | 防除零保护参数 |
| DeviceModulePos | int | get | 固定值 7 |
| HasChangedParams | bool | get/set | 参数变更标志 |

### 4.2 YGammaParams 结构体（设备通信用）

```csharp
private struct YGammaParams
{
    public int br_mod;              // 偏移 0x00: 4 字节，亮度模式 (未使用)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public int[] gma_num;           // 偏移 0x04: 8 * 4 = 32 字节，Gamma 段数参数 (未使用)
    public int contra_num;          // 偏移 0x24: 4 字节，对比度参数 (未使用)
    public int bofst;               // 偏移 0x28: 4 字节，黑色偏移 (未使用)
    public int lofst;               // 偏移 0x2C: 4 字节，亮色偏移 (未使用)
    public int lcpr_low;            // 偏移 0x30: 4 字节，局部对比度下限 (未使用)
    public int lcpr_high;           // 偏移 0x34: 4 字节，局部对比度上限 (未使用)
    public int lcpr_llimt;          // 偏移 0x38: 4 字节，局部对比度低限幅 (未使用)
    public int lcpr_hlimt;          // 偏移 0x3C: 4 字节，局部对比度高限幅 (未使用)
    public int pad_num;             // 偏移 0x40: 4 字节，填充参数 (实际使用)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public short[] using_ygama;     // 偏移 0x44: 256 * 2 = 512 字节，Gamma 查找表
};
```

**总大小**: 4 + 32 + 4*8 + 4 + 512 = **576 字节**

### 4.3 ProcessRgbBuffer 图像处理流程

```csharp
public override void ProcessRgbBuffer(ref byte[] imgBuffer)
{
    // 1. 分配输入缓冲区 (3 个通道 R/G/B)
    int tmpReadPos = 0;
    IntPtr[] inBuffer = new IntPtr[3];
    for (int i = 0; i < inBuffer.Length; i++)
    {
        inBuffer[i] = Marshal.AllocHGlobal(
            _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
        Marshal.Copy(imgBuffer, tmpReadPos, inBuffer[i], 
            _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
        tmpReadPos += _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short);
    }

    // 2. 分配输出缓冲区 (3 个通道)
    IntPtr[] outBuffer = new IntPtr[3];
    for (int i = 0; i < outBuffer.Length; i++)
    {
        outBuffer[i] = Marshal.AllocHGlobal(
            _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
        Marshal.Copy(new byte[_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short)],
            0, outBuffer[i], _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
    }

    // 3. 调用 C++ Gamma 校正
    IspApi.YGammaImg(_commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, 
        PadNum, YGammaTable, inBuffer, outBuffer);

    // 4. 释放输入缓冲区
    for (int i = 0; i < outBuffer.Length; i++)  // ⚠️ Bug: 应该是 inBuffer.Length
    {
        Marshal.FreeHGlobal(inBuffer[i]);
    }

    // 5. 将输出数据拷贝回 imgBuffer 并释放输出缓冲区
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

**数据流**:
```
imgBuffer (byte[])
    │
    ├── [通道 0: R 平面，W*H*2 字节，short 类型]
    ├── [通道 1: G 平面，W*H*2 字节，short 类型]
    └── [通道 2: B 平面，W*H*2 字节，short 类型]
         │
         ▼
    Marshal.Copy 到非托管内存 inBuffer[0..2]
         │
         ▼
    IspApi.YGammaImg() — 对每个像素应用 Gamma 校正
         │
         ▼
    Marshal.Copy 从 outBuffer[0..2] 回 imgBuffer
         │
         ▼
    imgBuffer 已被 Gamma 校正后的数据覆盖
```

**⚠️ 已知 Bug**: 第 4 步循环条件使用 `outBuffer.Length` 但释放 `inBuffer`，虽然两者长度相同（都为 3），但语义不正确。如果 `YGammaImg` 抛出异常，会导致内存泄漏（缺少 try-finally 保护）。

---

## 五、UI 实现 (YGammaWindow)

### 5.1 窗口整体布局

**文件**: `d:\jrx\zl\isptool\ThunderSE\Ui\SettingWindow\YGamma\YGammaWindow.xaml`

```
Window (Title="YGammaWindow", 1024x768, NoResize, Background="#FFE5E5E5")
  └── Grid (2 列)
        ├── Column 0 (Width="5*"): 图表区域
        │     └── DockPanel
        │           └── Chart (YGammaChart)
        │                 ├── X 轴: LinearAxis (0-255)
        │                 ├── Y 轴: LinearAxis (0-1023)
        │                 ├── Legend: 隐藏
        │                 └── LineSeries (YGammaTableLine) — 绑定 YGammaTable
        │                       ├── IsSelectionEnabled: True
        │                       ├── DependentValuePath: "Value"
        │                       ├── IndependentValuePath: "Key"
        │                       └── TransitionDuration: 0 (禁用动画)
        │
        └── Column 1 (Width="*"): 右侧按钮面板
              └── StackPanel (Margin: 30,60,30,0)
                    ├── Button "导入" — 绑定 LoadYGammaTableFromFileCommand
                    ├── Button "导出" — 绑定 SaveYGammaTableToFileCommand
                    ├── Button "在线调试读" — **无 Command 绑定 (空壳)**
                    ├── Button "在线调试写" — **无 Command 绑定 (空壳)**
                    ├── Button "复位比例" — Click="OnResetChartAxes"
                    ├── Button "计算IQ" — Click="OnClickCalcIQButton"
                    │     └── ContextMenu (右键菜单)
                    │           ├── MenuItem "在线IQ" — ShowOnlineYGammaIQ
                    │           └── MenuItem "离线IQ" — ShowOfflineYGammaIQ
                    └── Hyperlink "查看先行步骤" — 绑定 ViewPreviousIspStepCommand
```

### 5.2 折线图数据绑定

**ViewModel 数据流**:
```
YGamma.YGammaTable (short[256])
    │
    ▼ (提取 20 个关键点)
YGammaWindowViewModel.YGammaTable (ObservableCollection<KeyValuePair<int, short>>)
    │
    ▼ (WPF Binding)
LineSeries.ItemsSource
```

**YGammaTable 集合**:
- 类型：`ObservableCollection<KeyValuePair<int, short>>`
- 大小：固定 20 个元素（对应 20 个关键 X 坐标）
- Key：X 坐标（0-255 范围的 20 个关键点）
- Value：Y 坐标（0-1023 范围的 Gamma 输出值）

### 5.3 完整操作流程

```
[窗口加载]
    │
    ▼
OnWindowLoaded
    │
    └── 获取 _viewModel 引用 (从 DataContext)


[用户拖拽关键点]
    │
    ▼
Chart_MouseMove (鼠标左键按下选中点)
    │
    ├── LineSeries_SelectionChanged → 设置 _currentSelectedChartLinePointIndex
    │
    ▼
鼠标移动
    │
    ├── 计算新 Y 值:
    │     pos = e.GetPosition(YGammaTableLine)
    │     ActualChartY = (axisY.ActualHeight - pos.Y) / axisY.ActualHeight * axisY.ActualMaximum
    │
    ▼
更新 ViewModel:
    │
    └── _viewModel.YGammaTable[index] = new KeyValuePair<Key, (short)ActualChartY>
            │
            ▼
        触发 CollectionChanged (Replace 事件)
            │
            ▼
        YGammaTable_CollectionChanged
            │
            ├── 更新底层 _yGamma.YGammaTable[对应 X 坐标] = 新 Y 值
            │
            ├── 向前插值 (当前关键点与前一个关键点之间):
            │     slope = (currentY - previousY) / (currentX - previousX)
            │     for i = 1 to 间隔点数:
            │         _yGamma.YGammaTable[previousX + i] = previousY + Floor(slope * i)
            │
            └── 向后插值 (当前关键点与后一个关键点之间):
                  slope = (nextY - currentY) / (nextX - currentX)
                  for i = 1 to 间隔点数:
                      _yGamma.YGammaTable[currentX + i] = currentY + Floor(slope * i)
            │
            ▼
        UI 图表自动刷新 (256 点完整曲线)


[用户点击"导入"按钮]
    │
    ▼
LoadYGammaTableFromFileCommand
    │
    ├── OpenFileDialog (*.txt)
    │
    └── 读取文件内容
            │
            ├── 如果以 "0x" 开头 → 按十六进制解析:
            │     按 "\r\n" 分割 → 每行去掉 "0x" 前缀 → short.Parse(HexNumber)
            │
            └── 否则 → 按十进制逗号分隔解析:
                  按 "," 分割 → Convert.ToInt16
            │
            ▼
        检查长度 >= 256 (否则弹出错误提示)
            │
            ▼
        替换 _yGammaTable → 触发 PropertyChanged("YGammaTable")
            │
            ▼
        ViewModel 重建 20 个关键点 → 图表更新


[用户点击"导出"按钮]
    │
    ▼
SaveYGammaTableToFileCommand
    │
    ├── SaveFileDialog (*.txt)
    │
    └── 将 256 个 short 值用逗号连接 → 写入文件
          格式: "0,141,181,209,232,..."


[用户点击"计算IQ"按钮]
    │
    ▼
OnClickCalcIQButton → 弹出 ContextMenu
    │
    ├── 选择"在线IQ" → ShowOnlineYGammaIQ
    │     │
    │     └── 创建 YGammaOnlineIQWindow 并 Show()
    │
    └── 选择"离线IQ" → ShowOfflineYGammaIQ
          │
          └── 创建 YGammaOfflineIQWindow(_viewModel.IspProcessor) 并 Show()


[用户点击"复位比例"按钮]
    │
    ▼
OnResetChartAxes
    │
    └── 将 X 轴重置为 [0, 255]，Y 轴重置为 [0, 1023]


[用户右键拖拽平移图表]
    │
    ▼
Chart_MouseRightButtonDown → 记录锚点 _panAnchor
    │
    ▼
Chart_MouseMove (右键按下)
    │
    ├── axisX.Minimum += _panAnchor.X - current.X
    ├── axisX.Maximum += _panAnchor.X - current.X
    ├── axisY.Minimum += current.Y - _panAnchor.Y
    ├── axisY.Maximum += current.Y - _panAnchor.Y
    └── 更新 _panAnchor = current


[用户滚轮缩放图表]
    │
    ▼
Chart_MouseWheel
    │
    ├── Delta > 0 (向上滚动) → 缩小范围 (放大视图)
    │     axisX.Maximum *= (100 - Delta/10) / 100
    │     axisX.Minimum *= (100 - Delta/10) / 100
    │     axisY.Minimum *= (100 - Delta/10) / 100
    │     axisY.Maximum *= (100 - Delta/10) / 100
    │
    └── Delta < 0 (向下滚动) → 扩大范围 (缩小视图)
```

---

## 六、在线 IQ 窗口 (YGammaOnlineIQWindow)

### 6.1 窗口布局

```
Window (800x600, Background="#FFE5E5E5")
  └── Grid (3 行)
        ├── Row 0 (Height="7*"): 视频显示区
        │     └── ImageWithRubberBandControl (DisplayControl)
        │           ├── IsEnabled: 绑定 IsDrawing (Window DP)
        │           └── SizeChanged → OnDisplayControlSizeChange
        │
        ├── Row 1 (Height="2.3*"): 结果数据表格
        │     └── DataGrid
        │           ├── AutoGenerateColumns: False
        │           ├── ItemsSource: 绑定 DataContext
        │           └── Columns:
        │                 ├── "项" (Key) - Width 200
        │                 └── "值" (Value) - Width 200
        │
        └── Row 2 (Height="0.7*"): 控制按钮栏
              └── StackPanel (水平排列)
                    ├── Button "计算IQ" — Visibility 绑定 IsDrawing
                    ├── Button "停止计算" — Visibility 绑定 IsCalculating
                    ├── Button "撤销选框" — IsEnabled 绑定 IsDrawing
                    ├── Label "色卡:"
                    ├── ComboBox "6 阶/13 阶" — SelectedIndex 绑定 SelectedCalcMode
                    └── Button "显示图表" — OnClickShowGammaChart
```

### 6.2 视频流处理

```
Onloaded (Loaded 事件)
    │
    ├── 注册 UvcReceiver 事件:
    │     ├── DataReceive → OnUvcDataReceive
    │     └── StatusChange → OnPlayStateChange
    │
    ├── 获取视频尺寸: UvcReceiver.Instance.VideoWidth/Height
    │
    ├── 创建 WriteableBitmap (RGB24 格式)
    │
    ├── DisplayControl.DisplayImageSource = _bitmap
    │
    └── 配置 timerForCalcIQ:
          ├── Tick → OnCalcIQ
          └── Interval = 2 秒 (20000000 ticks = 2000ms)


OnUvcDataReceive (每帧视频数据)
    │
    ├── 更新 WriteableBitmap:
    │     ├── Lock()
    │     ├── WritePixels(全部像素)
    │     ├── AddDirtyRect()
    │     └── Unlock()
    │
    └── 如果存在橡皮筋选框 (_rubberBandData.Count > 0):
          │
          ├── 6 阶模式 (SelectedCalcMode == 0):
          │     遍历每个选框:
          │         ├── CroppedBitmap 裁剪选框区域
          │         ├── CopyPixels 获取像素数据
          │         ├── 计算 R/G/B 均值 (遍历所有像素)
          │         └── 存入 _avgRArray[i], _avgGArray[i], _avgBArray[i]
          │
          └── 13 阶模式 (SelectedCalcMode == 1):
                每个选框垂直分为 3 个子区域:
                    对每个子区域 j (0,1,2):
                        ├── 计算子区域的 Y 坐标和高度
                        ├── CroppedBitmap 裁剪子区域
                        ├── 计算 R/G/B 均值
                        └── 存入 _avgRArray[i*3+j], _avgGArray[i*3+j], _avgBArray[i*3+j]


OnClickCalcIQ (点击"计算IQ"按钮)
    │
    ├── IsCalculating = true
    ├── IsDrawing = false
    └── timerForCalcIQ.Start() — 每 2 秒调用一次 OnCalcIQ


OnCalcIQ (定时器触发)
    │
    ├── 创建新线程执行计算 (new Thread().Start())
    │
    ├── 初始化参数:
    │     ├── diff_l = [10, 10, 10, 10, 10, 10] (固定亮度差阈值)
    │     ├── ref_count = 0 (输出: 参考点数量)
    │     ├── l_val_array (输出: L* 值数组)
    │     ├── delta_l_array (输出: Delta L* 数组)
    │     ├── yMax = 0.0 (输出: 最大亮度)
    │     ├── yAvg (输出: 平均亮度数组)
    │     └── out_gamma = 0.0 (输出: 计算出的 Gamma 值)
    │
    ├── 调用 IspApi.YGAMMA_IQ:
    │     ├── 6 阶: num=6, yAvg 大小=18 (6*3)
    │     └── 13 阶: num=13, yAvg 大小=39 (13*3)
    │
    ├── 格式化输出值:
    │     ├── 6 阶: 显示 ref_count, l_val_array, delta_l_array
    │     └── 13 阶: 显示 yMax, yAvg (取中间值), out_gamma
    │
    ├── 更新图表数据 (仅 13 阶):
    │     ├── 遍历 yAvg 数组
    │     ├── yAvgDict[i/3.0] = yAvg[i] / 255.0
    │     ├── _chartData.yAvg = tmpYAvgDict
    │     └── _chartData.OutGamma = out_gamma
    │
    └── 通过 Dispatcher 更新 DataGrid DataContext


OnClickStopCalcIQ (点击"停止计算"按钮)
    │
    ├── timerForCalcIQ.Stop()
    ├── IsCalculating = false
    └── IsDrawing = true


OnUnloaded (Unloaded 事件)
    │
    ├── 注销 UvcReceiver 事件
    ├── 清除视频尺寸
    ├── timerForCalcIQ.Stop()
    ├── IsCalculating = false
    └── IsDrawing = true
```

### 6.3 13 阶模式的分区逻辑

13 阶灰度卡有 13 个灰度级，每个选框对应一个灰度级，选框内部垂直切分为 3 行 (R/G/B 通道分别采样)：

| 选框索引 | 子区域 0 | 子区域 1 | 子区域 2 |
|---------|---------|---------|---------|
| 0 | y, height/3 | y+height/3, height/3 | y+2*height/3, height/3 |
| 1 | ... | ... | ... |
| ... | ... | ... | ... |
| 12 | ... | ... | ... |

**总数据点**: 13 选框 × 3 子区域 = 39 个数据点

---

## 七、离线 IQ 窗口 (YGammaOfflineIQWindow)

### 7.1 窗口布局

```
Window (800x600, Background="#FFE5E5E5")
  └── Grid (2 行)
        ├── Row 0 (Height="*"): 图片显示区
        │     └── TabControl (ImgDisplayTab)
        │           ├── TabItem "原图":
        │           │     └── ImageWithRubberBandControl (OriginImg)
        │           │           └── IsEnabled: 绑定 IsLoadImage (Window DP)
        │           │
        │           └── TabItem "YGamma效果":
        │                 └── Canvas (白色背景)
        │                       └── Image (未绑定 Source)
        │
        └── Row 1 (Height="35"): 控制按钮栏
              └── StackPanel (水平排列)
                    ├── Button "选择Rgb" — OnLoadRgbButtonClick
                    ├── Button "撤销选框" — OnUndoClick, IsEnabled 绑定 IsLoadImage
                    └── Button "计算" — OnCalcIQClick, IsEnabled 绑定 IsLoadImage
```

### 7.2 操作流程

```
OnLoadRgbButtonClick
    │
    ├── OpenFileDialog (*.rgb)
    │
    ├── File.ReadAllBytes 读取文件 → _rgbBuffer
    │
    ├── 调用 _ispProcessor.GenerateBitmapUsingRgb(_rgbBuffer) → 创建 BitmapSource
    │
    ├── OriginImg.DisplayImageSource = BitmapSource
    │
    └── IsLoadImage = true


OnCalcIQClick
    │
    ├── 初始化 XArray/YArray/HeightArray/WidthArray (各 6 个元素)
    │
    ├── 如果存在橡皮筋选框:
    │     └── 遍历选框 → 提取 x, y, height, width 到数组
    │
    └── ⚠️ **功能未完成**: 实际计算逻辑被注释
          // _gammaStep.ProcessRgbBuffer(ref ptrArray);
          // IspApi.EncoderImgBuffer(...)
```

**⚠️ 已知问题**: 计算功能**未实现**，仅提取了 ROI 坐标但后续处理被注释。

---

## 八、Gamma 理论曲线窗口 (YGammaIQChartWindow)

### 8.1 窗口布局

```
Window (640x480)
  └── Grid
        └── Chart (YGammaChart)
              ├── X 轴: Minimum=0, Maximum=绑定 MaxItemCount (默认 15)
              ├── Y 轴: Minimum=0, Maximum=1
              ├── Legend: 隐藏
              └── LineSeries:
                    ├── YAvgLine — 绑定 yAvg (实测平均亮度曲线)
                    └── OutGammaLine — 绑定 GammaLineData (Gamma 理论曲线)
```

### 8.2 Gamma 理论曲线生成

```
OnLoaded 事件
    │
    └── 创建 Binding: "OutGamma" (来自 DataContext.ChartData)
          │
          ├── 设置 Converter: new GammaDataToChartLineConverter()
          ├── 设置 ConverterParameter: MaxItemCount (默认 15)
          └── 绑定到 Window.GammaLineDataProperty


GammaDataToChartLineConverter.Convert
    │
    ├── 输入: out_gamma (double), maxItemCount (ConverterParameter)
    │
    └── 对 i 从 2 到 maxItemCount-2:
          └── tmpGammaChartData[i] = Math.Pow(i, out_gamma)
                │
                └── ⚠️ **潜在 Bug**: 应该是 Math.Pow(i / maxItemCount, out_gamma)
```

**⚠️ 已知 Bug**: 代码实际实现为 `Math.Pow(i, out_gamma)`，其中 `i` 是绝对像素值而非归一化值。由于 Y 轴 Maximum=1，这意味着只有当 out_gamma 值使得 `i^gamma <= 1` 时曲线才可见。对于典型 Gamma 值 (2.2)，当 `i > 1` 时 `i^2.2 > 1`，曲线会超出 Y 轴范围。

**正确公式应为**:
```csharp
tmpGammaChartData[i] = Math.Pow((double)i / maxItemCount, out_gamma);
```

---

## 九、数据序列化

### 9.1 XML 序列化 (配置文件)

```csharp
public override XmlElement SerializeToXmlElement(XmlDocument xmlDoc)
{
    var xmlElement = xmlDoc.CreateElement("YGamma");

    XmlElement globalGammaTableNode = xmlDoc.CreateElement("Global_Gamma_Table");
    string yGammaTable = string.Join(",", YGammaTable.Select(x => x.ToString()).ToArray());
    globalGammaTableNode.AppendChild(xmlDoc.CreateTextNode(yGammaTable));
    xmlElement.AppendChild(globalGammaTableNode);

    XmlElement padNumNode = xmlDoc.CreateElement("Pad_Num");
    padNumNode.AppendChild(xmlDoc.CreateTextNode(PadNum.ToString()));
    xmlElement.AppendChild(padNumNode);

    return xmlElement;
}
```

**XML 格式**:
```xml
<YGamma>
    <Global_Gamma_Table>0,141,181,209,...,1023</Global_Gamma_Table>
    <Pad_Num>1</Pad_Num>
</YGamma>
```

### 9.2 XML 反序列化

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

    PadNum = Convert.ToByte(XmlHelper.GetNodeValue(yGammaNode, "Pad_Num"));
}
```

**⚠️ 潜在风险**: `XmlHelper.GetNodeValue(yGammaNode, "Pad_Num")` 如果返回 `null`，`Convert.ToByte(null)` 会抛出 `ArgumentNullException`。缺少 try-catch 保护。

### 9.3 二进制序列化 (设备烧录用)

**Getter**:
```csharp
YGammaParams yGammaParams = new YGammaParams()
{
    br_mod = 0,
    gma_num = new int[8],
    // ... 其他未使用字段清零
    pad_num = PadNum,
    using_ygama = YGammaTable
};

int size = Marshal.SizeOf(yGammaParams);  // 576 字节
byte[] arr = new byte[size];
IntPtr ptr = Marshal.AllocHGlobal(size);
try
{
    Marshal.StructureToPtr(yGammaParams, ptr, false);
    Marshal.Copy(ptr, arr, 0, size);
}
finally
{
    Marshal.FreeHGlobal(ptr);
}
return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
```

**输出**: 单元素字典 `{7, byte[576]}`，键为 `DeviceModulePos = 7`

### 9.4 文件导入/导出格式

**LoadYGammaTableFromFile 支持格式**:

**格式 1: 十六进制 (每行一个值)**
```
0x0
0x8D
0xB5
...
0x3FF
```

解析:
```csharp
if (fileContent.StartsWith("0x"))
{
    yGammaTable = fileContent.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
        .Select(s => short.Parse(s.Substring(2).ToUpper(), System.Globalization.NumberStyles.HexNumber))
        .ToArray();
}
```

**格式 2: 十进制逗号分隔**
```
0,141,181,209,232,251,268,...,1023
```

解析:
```csharp
else
{
    yGammaTable = fileContent.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
        .Select(s => Convert.ToInt16(s))
        .ToArray();
}
```

**校验规则**:
- 仅检查下限 (`yGammaTable.Length >= 256`)
- **不检查**值的范围 (应为 0-1023)
- 超过 256 个值时取前 256 个

**SaveYGammaTableToFile 输出格式**:
纯十进制逗号分隔，无换行，无前后缀。

---

## 十、已知问题清单

### 10.1 高严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B1 | DeserializeFromXmlElement 缺少 null 检查 | Pad_Num 为 null 时崩溃 | 添加 XmlHelper 安全解析 |
| B2 | ProcessRgbBuffer 缺少 try-finally | 异常时内存泄漏 | 添加 try-finally 保护 |
| B3 | YGammaIQChartWindow Gamma 理论曲线公式错误 | 曲线超出 Y 轴范围 | 改为 `Math.Pow(i / maxItemCount, out_gamma)` |

### 10.2 中严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B4 | "在线调试读/写" 按钮无功能 | 用户困惑 | 绑定 Command 或移除按钮 |
| B5 | YGammaOfflineIQWindow 计算 IQ 未完成 | 功能不可用 | 完成实现或移除入口 |
| B6 | 线性插值使用 Math.Floor 可能导致负值 | 溢出风险 | 添加范围检查 |

### 10.3 低严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B7 | SaveYGammaTableToFile 中 Substring 冗余 | 代码整洁度 | 移除冗余操作 |
| B8 | ProcessRgbBuffer 循环条件语义错误 | 代码可读性 | 改为 inBuffer.Length |
| B9 | SelectedCalcMode 绑定使用 LostFocus | 用户体验差 | 改为 PropertyChanged |

---

## 十一、关键文件清单

### 数据模型

| 文件 | 路径 | 职责 |
|------|------|------|
| Gamma.cs | `DeviceConfig/Isp/Gamma.cs` | YGamma 数据模型、算法封装 |
| ProcessStep.cs | `DeviceConfig/Isp/ProcessStep.cs` | 抽象基类 |
| CommonConfig.cs | `DeviceConfig/Isp/CommonConfig.cs` | 公共配置 (分辨率、Bayer) |
| IspApi.cs | `DeviceConfig/Isp/IspApi.cs` | P/Invoke 声明 |

### UI

| 文件 | 路径 | 职责 |
|------|------|------|
| YGammaWindow.xaml | `Ui/SettingWindow/YGamma/YGammaWindow.xaml` | 主调试窗口 XAML |
| YGammaWindow.xaml.cs | `Ui/SettingWindow/YGamma/YGammaWindow.xaml.cs` | 主窗口代码隐藏 |
| YGammaWindowViewModel.cs | `Ui/SettingWindow/YGamma/YGammaWindowViewModel.cs` | 主窗口 ViewModel |
| YGammaOnlineIQWindow.xaml | `Ui/SettingWindow/YGamma/YGammaOnlineIQWindow.xaml` | 在线 IQ 窗口 |
| YGammaOnlineIQWindow.xaml.cs | `Ui/SettingWindow/YGamma/YGammaOnlineIQWindow.xaml.cs` | 在线 IQ 窗口代码 |
| YGammaOfflineIQWindow.xaml | `Ui/SettingWindow/YGamma/YGammaOfflineIQWindow.xaml` | 离线 IQ 窗口 |
| YGammaOfflineIQWindow.xaml.cs | `Ui/SettingWindow/YGamma/YGammaOfflineIQWindow.xaml.cs` | 离线 IQ 窗口代码 |
| YGammaIQChartWindow.xaml | `Ui/SettingWindow/YGamma/YGammaIQChartWindow.xaml` | Gamma 图表窗口 |
| YGammaIQChartWindow.xaml.cs | `Ui/SettingWindow/YGamma/YGammaIQChartWindow.xaml.cs` | Gamma 图表窗口代码 |

### C++ 算法

| 文件 | 路径 | 职责 |
|------|------|------|
| IQ.cpp | `IspApi/source/IQ.cpp` (行 1517-1611) | YGammaImg, YGAMMA_IQ |
| Export.h | `IspApi/source/Export.h` | C 接口导出 |
| IQ.h | `IspApi/include/IQ.h` | 结构体定义 (iq_config, gray_iq) |

---

## 十二、附录

### 12.1 色彩空间转换公式

| 转换 | 公式 |
|------|------|
| BT.601 亮度 | `Y = (R*77 + G*150 + B*29) / 256` |
| BT.709 亮度 | `Y = R*0.2126 + G*0.7152 + B*0.0722` |
| sRGB 反伽马 | `x > 0.04045 ? pow((x+0.055)/1.055, 2.4) : x/12.92` |
| RGB → XYZ (D65) | `X = R*0.4124 + G*0.3576 + B*0.1805`<br>`Y = R*0.2126 + G*0.7152 + B*0.0722`<br>`Z = R*0.0193 + G*0.1192 + B*0.9505` |
| XYZ → Lab | `L* = 116 * f(Y/Yn) - 16`<br>`a* = 500 * (f(X/Xn) - f(Y/Yn))`<br>`b* = 200 * (f(Y/Yn) - f(Z/Zn))` |
| XYZ2LAB | `x > 0.008856 ? pow(x, 1/3) : 7.787*x + 0.1379` |
| Gamma 值计算 | `Gamma = log(0.5) / log(V_in / 256)` |

### 12.2 线性插值公式

```
slope = (Y_current - Y_neighbor) / (X_current - X_neighbor)
Y_interpolated[X_neighbor + i] = Y_neighbor + Floor(slope * i)
```

### 12.3 增益编码规则

| Gamma 表输出值 | 含义 |
|---------------|------|
| 0 | 纯黑输出 |
| 256 (0x100) | 1.0x 增益 (输入=输出) |
| 512 (0x200) | 2.0x 增益 |
| 1023 (0x3FF) | ~4.0x 增益 (最大) |

---

**文档版本**: v1.0  
**创建日期**: 2026年4月8日  
**分析依据**: 项目源码完整阅读与交叉引用  
**文档状态**: 完整

---

**文档结束**

本文档基于 ThunderSE 项目实际代码深入分析生成，涵盖 YGamma 模块的完整参数定义、C++ 算法原理 (YGammaImg/YGAMMA_IQ)、数据序列化规范、UI 交互设计 (YGammaWindow/YGammaOnlineIQWindow/YGammaOfflineIQWindow/YGammaIQChartWindow) 和设备通信协议。可作为 YGamma 模块开发、调试、测试和维护的参考文档。
