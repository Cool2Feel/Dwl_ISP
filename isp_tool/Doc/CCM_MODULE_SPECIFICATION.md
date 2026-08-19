# CCM (颜色校正矩阵) 模块详细需求规格说明

## 文档信息

| 项目 | 内容 |
|------|------|
| **模块名称** | CCM (Color Correction Matrix / 颜色校正矩阵) |
| **DeviceModulePos** | 5 |
| **IspModule 枚举值** | `IspModule.Ccm` |
| **文档版本** | v1.0 |
| **创建日期** | 2026年4月8日 |
| **分析依据** | 项目源码完整阅读与交叉引用 |

---

## 一、模块概述

### 1.1 功能描述

CCM (Color Correction Matrix) 模块是 ISP 图像处理管线中的**色彩校正模块**，用于校正传感器捕获的 RGB 色彩值，使其更接近人眼感知或标准色彩空间（如 sRGB）。

**核心功能**:
- 通过 3x3 矩阵对 RGB 三通道进行线性变换
- 校正传感器感光片和镜头的光谱响应偏差
- 将传感器 RGB 空间转换到标准 sRGB 空间
- 支持预设值快速切换 (R/G/B/Y/C/M 六种颜色倾向)

### 1.2 在 ISP 管线中的位置

```
Raw Bayer → BLC → LSC → AWB → Demosaic → CCM → YGamma → EE → CH → 输出
   (0)       (1)    (2)    (4)               (5)    (7)    (11)  (9)
                                         ↑ CCM 在第 5 步
```

**注意**: CCM 在 `RgbFileProcessSteps` 中，作用于**去马赛克后的 RGB 图像**，而非 Raw Bayer 数据。

### 1.3 与其他模块的区别

| 特性 | CCM | AWB |
|------|-----|-----|
| 作用域 | RGB 三通道线性变换 | R/G/B 通道独立增益 |
| 处理阶段 | 去马赛克后 | Raw Bayer 域 |
| 功能 | 校正色彩空间偏差 | 校正光源色温偏差 |
| 算法 | 3x3 矩阵乘法 | 统计 + 增益计算 |

---

## 二、参数完整定义

### 2.1 参数总表

| 参数 | 类型 | 默认值 | 取值范围 | UI 范围 | 说明 |
|------|------|--------|----------|---------|------|
| ccm | short[9] | {0,0,0,0,0,0,0,0,0} | short.MinValue ~ short.MaxValue | -512 ~ 511 | 3x3 颜色校正矩阵 (行优先) |
| s41 | short | 0 | short 范围 | - | 扩展参数 41 (未使用) |
| s42 | short | 0 | short 范围 | - | 扩展参数 42 (未使用) |
| s43 | short | 0 | short 范围 | - | 扩展参数 43 (未使用) |

### 2.2 ccm 数组的详细定义

ccm 是一个 3x3 颜色校正矩阵，**行优先 (row-major)** 排列：

```
| ccm[0]  ccm[1]  ccm[2] |   | R_in |
| ccm[3]  ccm[4]  ccm[5] | * | G_in |
| ccm[6]  ccm[7]  ccm[8] |   | B_in |
```

**各元素物理含义**:

| 索引 | 矩阵位置 | 物理含义 | 默认值 | 说明 |
|------|----------|----------|--------|------|
| `ccm[0]` | M[0,0] | R 输入 → R 输出的增益 | 256 (1.0x) | 对角线，主增益 |
| `ccm[1]` | M[0,1] | G 输入 → R 输出的串扰 | 0 | 非对角线，色彩混合 |
| `ccm[2]` | M[0,2] | B 输入 → R 输出的串扰 | 0 | 非对角线，色彩混合 |
| `ccm[3]` | M[1,0] | R 输入 → G 输出的串扰 | 0 | 非对角线，色彩混合 |
| `ccm[4]` | M[1,1] | G 输入 → G 输出的增益 | 256 (1.0x) | 对角线，主增益 |
| `ccm[5]` | M[1,2] | B 输入 → G 输出的串扰 | 0 | 非对角线，色彩混合 |
| `ccm[6]` | M[2,0] | R 输入 → B 输出的串扰 | 0 | 非对角线，色彩混合 |
| `ccm[7]` | M[2,1] | G 输入 → B 输出的串扰 | 0 | 非对角线，色彩混合 |
| `ccm[8]` | M[2,2] | B 输入 → B 输出的增益 | 256 (1.0x) | 对角线，主增益 |

**数值格式**: 采用定点数表示法 (Q8 格式):
- `0x100` (256) = 1.0 (单位增益)
- `0x110` (272) = 1.0625 (增强 6.25%)
- `-0x18` (-24) = -0.09375 (抑制 9.375%)
- `0x08` (8) = 0.03125 (微弱串扰)

**有效范围**: -512 ~ 511 (有符号 10-bit)
- 正数: 0 ~ 511 (0x000 ~ 0x1FF)
- 负数: 在设备端使用补码表示 (1024 为模)

### 2.3 预设矩阵值

**6 种预设颜色倾向**:

| 预设名 | 显示文本 | 矩阵值 (十六进制) | 矩阵值 (十进制) | 用途 |
|--------|---------|-------------------|-----------------|------|
| R | R | {0x110, 0x08, -0x18, 0x00, 0x100, 0x00, 0x00, 0x00, 0x100} | {272, 8, -24, 0, 256, 0, 0, 0, 256} | 增强红色 |
| G | G | {0x100, 0x00, 0x00, -0x08, 0x110, -0x08, 0x00, 0x00, 0x100} | {256, 0, 0, -8, 272, -8, 0, 0, 256} | 增强绿色 |
| B | B | {0x100, 0x00, 0x00, 0x00, 0x100, 0x00, -0x18, 0x08, 0x110} | {256, 0, 0, 0, 256, 0, -24, 8, 272} | 增强蓝色 |
| Y | Y | {0x110, 0x08, -0x18, -0x08, 0x110, -0x08, 0x00, 0x00, 0x100} | {272, 8, -24, -8, 272, -8, 0, 0, 256} | 增强黄色 (R+G) |
| C | C | {0x100, 0x00, 0x00, -0x08, 0x110, -0x08, -0x18, 0x08, 0x110} | {256, 0, 0, -8, 272, -8, -24, 8, 272} | 增强青色 (G+B) |
| M | M | {0x110, 0x08, -0x18, 0x00, 0x100, 0x00, -0x18, 0x08, 0x110} | {272, 8, -24, 0, 256, 0, -24, 8, 272} | 增强品红 (R+B) |

**预设矩阵可视化 (以 R 为例)**:

```
R 预设矩阵:
[ 1.0625   0.03125  -0.09375 ]   [ R_in ]   [ R_out ]
[ 0        1.0       0        ] * [ G_in ] = [ G_out ]
[ 0        0         1.0      ]   [ B_in ]   [ B_out ]
```

解读:
- R_out = 1.0625 * R_in + 0.03125 * G_in - 0.09375 * B_in
- G_out = 1.0 * G_in (不变)
- B_out = 1.0 * B_in (不变)
- **效果**: R 通道增强 6.25%，同时从 G 通道吸收微弱成分 (3.125%)，抑制 B 通道成分 (9.375%)

### 2.4 s41/s42/s43 参数说明

**结论**: 这三个参数在当前代码中**未被实际使用**。

**证据**:
1. 在 `CCM.cs` 中：仅有 getter/setter 属性定义，没有任何业务逻辑引用
2. 在 `SerializeToXmlElement` 中：只序列化 `ccm` 数组，**不序列化** s41/s42/s43
3. 在 `DeserializeFromXmlElement` 中：只反序列化 `ccm` 数组
4. 在 `CCMAreaViewModel.cs` 中：没有任何对 s41/s42/s43 的引用
5. 在 UI 中：没有任何控件用于编辑这三个参数

**推测用途**: 预留字段，可能用于未来的 CCM 扩展参数（如温度补偿、多光源切换阈值、偏移量等），或者在底层 C++ ISP 固件中使用但 C# 端尚未接入。

---

## 三、数据结构与内存布局

### 3.1 CCMParams 结构体 (设备通信用)

```csharp
[StructLayout(LayoutKind.Sequential)]
private struct CCMParams
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
    public short[] ccm;    // 9 * 2 = 18 字节 (偏移 0x00 - 0x11)
    public short s41;       // 2 字节 (偏移 0x12 - 0x13)
    public short s42;       // 2 字节 (偏移 0x14 - 0x15)
    public short s43;       // 2 字节 (偏移 0x16 - 0x17)
};
```

**总大小**: 24 字节

**内存偏移布局**:

| 偏移 (字节) | 字段 | 大小 | 说明 |
|-------------|------|------|------|
| 0x00 - 0x01 | `ccm[0]` | 2 字节 | R→R 增益 |
| 0x02 - 0x03 | `ccm[1]` | 2 字节 | G→R 串扰 |
| 0x04 - 0x05 | `ccm[2]` | 2 字节 | B→R 串扰 |
| 0x06 - 0x07 | `ccm[3]` | 2 字节 | R→G 串扰 |
| 0x08 - 0x09 | `ccm[4]` | 2 字节 | G→G 增益 |
| 0x0A - 0x0B | `ccm[5]` | 2 字节 | B→G 串扰 |
| 0x0C - 0x0D | `ccm[6]` | 2 字节 | R→B 串扰 |
| 0x0E - 0x0F | `ccm[7]` | 2 字节 | G→B 串扰 |
| 0x10 - 0x11 | `ccm[8]` | 2 字节 | B→B 增益 |
| 0x12 - 0x13 | `s41` | 2 字节 | 扩展参数 |
| 0x14 - 0x15 | `s42` | 2 字节 | 扩展参数 |
| 0x16 - 0x17 | `s43` | 2 字节 | 扩展参数 |

**对齐**: 所有字段均为 `short` (2 字节)，无结构体填充。

---

## 四、C++ 算法实现

### 4.1 CCM_Cal (矩阵自动校准)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 1363-1441)

**函数签名**:
```cpp
ISP_API void CCM_Cal(
    int *cr_avg,       // 输入: 24 色块 R 通道平均值 [24]
    int *cg_avg,       // 输入: 24 色块 G 通道平均值 [24]
    int *cb_avg,       // 输入: 24 色块 B 通道平均值 [24]
    int delta_C_th,    // 输入: 色度误差阈值 (典型值 20)
    int delta_S_th,    // 输入: 饱和度误差阈值 (典型值 10)
    int cmatrix_th,    // 输入: 矩阵元素搜索范围 (典型值 6)
    int step,          // 输入: 搜索步长 (典型值 2)
    int **cmatrix_out, // 输出: 最优 3x3 矩阵 [3][3]
    int light_source   // 输入: 光源类型 (0=理想, 1=D65)
);
```

**算法原理**: 暴力搜索 + 约束优化

#### 4.1.1 搜索空间定义

搜索 6 个自由变量 (3x3 矩阵的非对角元素)，第 3 个元素由**行和为 256** 约束确定:

```cpp
// 第 1 行: 搜索 ccm[0][1] 和 ccm[0][2]，ccm[0][0] 由行和约束确定
for (cmatrix[0][1] = -cmatrix_th; cmatrix[0][1] < cmatrix_th; cmatrix[0][1] += step)
for (cmatrix[0][2] = -cmatrix_th; cmatrix[0][2] < cmatrix_th; cmatrix[0][2] += step)
    cmatrix[0][0] = 256 - cmatrix[0][1] - cmatrix[0][2];  // 行和 = 256

// 第 2 行: 搜索 ccm[1][0] 和 ccm[1][2]
for (cmatrix[1][0] = -cmatrix_th; cmatrix[1][0] < cmatrix_th; cmatrix[1][0] += step)
for (cmatrix[1][2] = -cmatrix_th; cmatrix[1][2] < cmatrix_th; cmatrix[1][2] += step)
    cmatrix[1][1] = 256 - cmatrix[1][0] - cmatrix[1][2];

// 第 3 行: 搜索 ccm[2][0] 和 ccm[2][1]
for (cmatrix[2][0] = -cmatrix_th; cmatrix[2][0] < cmatrix_th; cmatrix[2][0] += step)
for (cmatrix[2][1] = -cmatrix_th; cmatrix[2][1] < cmatrix_th; cmatrix[2][1] += step)
    cmatrix[2][2] = 256 - cmatrix[2][0] - cmatrix[2][1];
```

**搜索空间大小**: 当 `cmatrix_th=6, step=2` 时:
- 每行: 6 × 6 = 36 种组合
- 总计: 36³ = **46,656** 种矩阵

#### 4.1.2 矩阵应用与色彩校正

对每个候选矩阵，执行色彩校正:

```cpp
for (unsigned int i = 0; i < 24; i++) {
    // 注意: 使用转置形式 cmatrix[col][row]
    r_avg[i] = (cmatrix[0][0] * cr_avg[i] + cmatrix[1][0] * cg_avg[i] + cmatrix[2][0] * cb_avg[i]) / 256;
    r_avg[i] = CLIP_PIXEL(r_avg[i], 0, 1023);
    
    g_avg[i] = (cmatrix[0][1] * cr_avg[i] + cmatrix[1][1] * cg_avg[i] + cmatrix[2][1] * cb_avg[i]) / 256;
    g_avg[i] = CLIP_PIXEL(g_avg[i], 0, 1023);
    
    b_avg[i] = (cmatrix[0][2] * cr_avg[i] + cmatrix[1][2] * cg_avg[i] + cmatrix[2][2] * cb_avg[i]) / 256;
    b_avg[i] = CLIP_PIXEL(b_avg[i], 0, 1023);
}
```

**矩阵应用公式**:
```
[R_out]   [M[0][0]  M[1][0]  M[2][0]] [R_in]
[G_out] = [M[0][1]  M[1][1]  M[2][1]] [G_in]
[B_out]   [M[0][2]  M[1][2]  M[2][2]] [B_in]
```

**注意**: 这里使用的是**转置形式** (M[col][row] 而非 M[row][col])。

#### 4.1.3 色彩空间转换 (RGB → Lab)

```cpp
// 1. sRGB 伽马线性化
r_var = gamma(r_avg[i] / 1024.0);
g_var = gamma(g_avg[i] / 1024.0);
b_var = gamma(b_avg[i] / 1024.0);

// gamma 函数 (sRGB 标准)
double gamma(double x) {
    return x > 0.04045 ? pow((x + 0.055) / 1.055, 2.4) : x / 12.92;
}

// 2. RGB → XYZ (sRGB D65 标准矩阵)
x_var = r_var * 0.4124 + g_var * 0.3576 + b_var * 0.1805;
y_var = r_var * 0.2126 + g_var * 0.7152 + b_var * 0.0722;
z_var = r_var * 0.0193 + g_var * 0.1192 + b_var * 0.9505;

// 3. XYZ → Lab
a_val[i] = 500.0 * (XYZ2LAB(x_var / 95.047) - XYZ2LAB(y_var / 100.0));
b_val[i] = 200.0 * (XYZ2LAB(y_var / 100.0) - XYZ2LAB(z_var / 108.883));

// XYZ2LAB 函数
double XYZ2LAB(double x) {
    if (x > pow(6.0 / 29.0, 3.0))  // 约 0.008856
        return pow(x, 1.0 / 3.0);
    else
        return 1.0 / 3.0 * pow(29.0 / 6.0, 2.0) * x + 4.0 / 29.0;
}
```

#### 4.1.4 约束筛选与最优解

```cpp
// 计算饱和度
saturation[i] = 100.0 * sqrt(a_val[i]² + b_val[i]²);
saturation_sum += saturation[i];

// 计算色差 Delta C (与理想值比较)
if (light_source == 0)
    delta_C[i] = sqrt((a_val[i] - a_Ideal[i])² + (b_val[i] - b_Ideal[i])²);
else
    delta_C[i] = sqrt((a_val[i] - a_D65[i])² + (b_val[i] - b_D65[i])²);
delta_C_sum += delta_C[i];

// 约束 1: 平均饱和度偏差 < delta_S_th
if (abs(saturation_sum / 24.0 - 100.0) < delta_S_th) {
    // 约束 2: Delta C 最小化
    if (delta_C_sum / 24.0 < delta_C_min) {
        delta_C_min = delta_C_sum / 24.0;
        // 保存当前最优矩阵
        for (k1, k2) cmatrix_out[k1][k2] = cmatrix[k1][k2];
    }
}
```

**输出验证**:
```cpp
if (delta_C_min < delta_C_th) {
    printf("saturation: %f\n", saturation_out);
    printf("delta_C: %f\n", delta_C_min);
} else {
    printf("The cmatrix_th is not enough!\n");  // 搜索范围不够
}
```

#### 4.1.5 理想色度值参考

**理想光源 (a_Ideal, b_Ideal)** — 标准实验室环境 (24 色块):
```
索引 0-5:  a={12.75, 13.54, -1.58, -16.05, 11.22, -31.83}
           b={14.85, 17.20, -21.29, 21.95, -25.04, 1.48}
索引 6-11: a={31.37, 15.50, 45.39, 23.49, -26.83, 15.03}
           b={58.34, -42.49, 14.49, -22.34, 58.56, 67.04}
索引 12-17: a={26.88, -41.03, 56.41, -1.25, 49.69, -23.74}
            b={-52.69, 34.93, 28.65, 79.40, -15.70, -26.27}
索引 18-23: a={-0.64, -0.03, -0.10, -0.05, -0.12, 0.35}
            b={2.58, 0.27, 0.06, 0.66, -0.14, -0.20}
```

**D65 光源 (a_D65, b_D65)** — 日光/标准照明 (24 色块):
```
索引 0-5:  a={13.90, 14.02, -0.86, -32.40, 14.39, -29.10}
           b={29.09, 21.01, -35.52, 41.57, -31.58, -2.38}
索引 6-11: a={21.45, 36.25, 50.61, 36.26, -35.57, -6.44}
           b={79.09, -75.31, 33.13, -32.64, 62.59, 85.35}
索引 12-17: a={57.13, -61.13, 71.72, -18.57, 66.47, -5.67}
            b={-87.45, 52.31, 62.46, 80.34, -20.92, -49.07}
索引 18-23: a={-0.05, -3.86, -5.70, -5.70, -3.74, 0.09}
            b={-0.02, -0.14, 1.22, 1.89, -0.94, 0.26}
```

### 4.2 ColorCorrection (矩阵应用)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 1443-1473)

**函数签名**:
```cpp
void ColorCorrection(iq_config* iq_cfg, Pix **input_img, Pix **output_img);
```

**处理流程**:

```cpp
// 1. 将无符号配置值转换为有符号矩阵系数
for (i = 0; i < 3; i++) {
    for (j = 0; j < 3; j++) {
        // 阈值 512: >=512 表示负数 (补码形式, 1024 为模)
        cc_matrix_c[i][j] = (iq_cfg->ccm_par_c[i][j] >= 512) 
            ? iq_cfg->ccm_par_c[i][j] - 1024 
            : iq_cfg->ccm_par_c[i][j];
    }
}

// 2. 转换偏移参数 (10-bit 时阈值为 64)
for (i = 0; i < 3; i++) {
    cc_matrix_s[i] = (iq_cfg->ccm_par_s[i] >= 64) 
        ? iq_cfg->ccm_par_s[i] - 128 
        : iq_cfg->ccm_par_s[i];
}

// 3. 逐像素应用 CCM 矩阵
for (i = 0; i < h; i++) {
    for (j = 0; j < w; j++) {
        int pos = i*w + j;
        for (k = 0; k < 3; k++) {
            // 3x3 矩阵乘法 + 偏移
            tmp = input_img[0][pos] * cc_matrix_c[0][k] 
                + input_img[1][pos] * cc_matrix_c[1][k] 
                + input_img[2][pos] * cc_matrix_c[2][k];
            tmp = tmp / 256;           // Q8 反归一化
            tmp += cc_matrix_s[k];     // 添加偏移
            output_img[k][pos] = CLIP_PIXEL(tmp, 0, 1023);
        }
    }
}
```

**矩阵应用公式**:
```
[R_out]   [M[0][0]  M[1][0]  M[2][0]] [R_in]   [S[0]]
[G_out] = [M[0][1]  M[1][1]  M[2][1]] [G_in] + [S[1]]
[B_out]   [M[0][2]  M[1][2]  M[2][2]] [B_in]   [S[2]]
```

### 4.3 Rgb2Lab_CCM_IQ (色彩质量评估)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 1475-1513)

**函数签名**:
```cpp
ISP_API void Rgb2Lab_CCM_IQ(int *r_avg, int *g_avg, int *b_avg);
```

**功能**: 评估 CCM 校正后的色彩质量，计算 24 色块的 Delta E (总色差) 和 Delta Eab (色度差)。

**计算公式**:
```cpp
// Delta E (包含亮度)
delta_E = Σ sqrt((L_ideal[i] - l_val[i])² + (a_ideal[i] - a_val[i])² + (b_ideal[i] - b_val[i])²) / 24

// Delta Eab (仅色度)
delta_Eab = Σ sqrt((a_ideal[i] - a_val[i])² + (b_ideal[i] - b_val[i])²) / 24
```

**合格标准**:
- Delta E < 5: 优秀
- Delta E < 10: 良好
- Delta E < 20: 可接受
- Delta E > 20: 需要调整

---

## 五、数据序列化

### 5.1 XML 序列化 (配置文件)

**SerializeToXmlElement 实现**:

```csharp
public override XmlElement SerializeToXmlElement(XmlDocument xmlDoc)
{
    var xmlElement = xmlDoc.CreateElement("CCM");
    XmlElement CcmNode = xmlDoc.CreateElement("ccm");
    
    // 将 9 个 short 值用逗号连接成字符串
    string CcmStr = string.Join(",", ccm.Select(x => x.ToString()).ToArray());
    CcmNode.AppendChild(xmlDoc.CreateTextNode(CcmStr));
    xmlElement.AppendChild(CcmNode);
    
    return xmlElement;
}
```

**生成的 XML 格式**:
```xml
<CCM>
    <ccm>256,0,0,0,256,0,0,0,256</ccm>
</CCM>
```

**注意**: `s41/s42/s43` **不参与 XML 序列化**。

### 5.2 XML 反序列化

**DeserializeFromXmlElement 实现**:

```csharp
public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
{
    var CcmNode = ispToolDataNode["CCM"];
    var tmpCcmStr = XmlHelper.GetNodeValue(CcmNode, "ccm");
    
    if (tmpCcmStr != null)
    {
        ccm = tmpCcmStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Convert.ToInt16(s))
            .ToArray();
    }
}
```

**注意事项**:
- 使用 `XmlHelper.GetNodeValue` 安全获取 (节点不存在时返回 null)
- 如果 XML 中 `<ccm>` 节点不存在，`ccm` 数组不会被修改 (保持当前值)
- **不反序列化 s41/s42/s43**
- 如果值超出 `Int16` 范围，`Convert.ToInt16` 会抛出 `OverflowException`

### 5.3 二进制序列化 (设备烧录用)

**ParamsDataCollection getter 实现**:

```csharp
public override Dictionary<int, byte[]> ParamsDataCollection
{
    get
    {
        CCMParams ccmParams = new CCMParams()
        {
            ccm = ccm,
            s41 = s41,
            s42 = s42,
            s43 = s43
        };

        int size = Marshal.SizeOf(ccmParams);  // 24 字节
        byte[] arr = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);

        Marshal.StructureToPtr(ccmParams, ptr, true);
        Marshal.Copy(ptr, arr, 0, size);
        Marshal.FreeHGlobal(ptr);

        return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
    }
}
```

**输出**: 单元素字典 `{5, byte[24]}`，键为 `DeviceModulePos = 5`

**内存泄漏风险**: 如果 `StructureToPtr` 抛出异常，`FreeHGlobal` 不会被执行 (缺少 try-finally 保护)。

**ParamsDataCollection setter 实现**:

```csharp
set
{
    CCMParams ccmParams = new CCMParams();
    int size = Marshal.SizeOf(ccmParams);
    IntPtr ptr = Marshal.AllocHGlobal(size);

    Marshal.Copy(value[DeviceModulePos], 0, ptr, size);
    ccmParams = (CCMParams)Marshal.PtrToStructure(ptr, ccmParams.GetType());
    Marshal.FreeHGlobal(ptr);

    ccm = ccmParams.ccm;
    s41 = ccmParams.s41;
    s42 = ccmParams.s42;
    s43 = ccmParams.s43;
}
```

---

## 六、UI 实现 (用户模式 CCMArea)

### 6.1 整体布局

**文件**: `d:\jrx\zl\isptool\ThunderSE\Ui\MainWindow\UserMode\EffectTabControl\CCMArea.xaml`

```
GroupBox (Header="颜色矩阵")
  └── StackPanel (Vertical)
        ├── StackPanel (Horizontal) — 使能行
        │     ├── Label → "使能 :"
        │     └── CheckBox → IsChecked 绑定到 IsCcmEnable
        │
        └── StackPanel (Horizontal) — 矩阵行
              ├── Label → "矩阵 :"
              └── StackPanel (垂直排列)
                    ├── StackPanel (Horizontal) — 3x3 矩阵区域
                    │     ├── StackPanel — 第 1 列 (索引 0, 1, 2)
                    │     │     ├── TextBox [ccm[0]] — 40x20px
                    │     │     ├── TextBox [ccm[1]] — 40x20px
                    │     │     └── TextBox [ccm[2]] — 40x20px
                    │     ├── StackPanel — 第 2 列 (索引 3, 4, 5)
                    │     │     ├── TextBox [ccm[3]]
                    │     │     ├── TextBox [ccm[4]]
                    │     │     └── TextBox [ccm[5]]
                    │     └── StackPanel — 第 3 列 (索引 6, 7, 8)
                    │           ├── TextBox [ccm[6]]
                    │           ├── TextBox [ccm[7]]
                    │           └── TextBox [ccm[8]]
                    │
                    └── StackPanel (Horizontal) — 预设值行
                          ├── Label → "预设值 :"
                          └── 6 个 RadioButton (GroupName="PresetCcmVal")
                                ├── R (PresetCCMValRButton)
                                ├── G (PresetCCMValGButton)
                                ├── B (PresetCCMValBButton)
                                ├── Y (PresetCCMValYButton)
                                ├── C (PresetCCMValCButton)
                                └── M (PresetCCMValMButton)
```

### 6.2 9 个 TextBox 的数据绑定

**绑定方式**:
```xml
<TextBox Text="{Binding Path=ccm, Converter={StaticResource ccmConverter}}">
    <Binding.ConverterParameter>
        <sys:Int32>0</sys:Int32>  <!-- 索引 0~8 -->
    </Binding.ConverterParameter>
    <Binding.ValidationRules>
        <customControl:NumberRange ValidatesOnTargetUpdated="True">
            <customControl:NumberRange.Wrapper>
                <customControl:Wrapper
                    MinValue="{Binding Data.MinCcmValue, Source={StaticResource proxy}}"
                    MaxValue="{Binding Data.MaxCcmValue, Source={StaticResource proxy}}"/>
            </customControl:NumberRange.Wrapper>
        </customControl:NumberRange>
    </Binding.ValidationRules>
</TextBox>
```

**DataArrayToControlCollectionConverter 转换器**:

```csharp
class DataArrayToControlCollectionConverter : IValueConverter
{
    public int[] DataArray { get; set; }

    // Model → UI: 从数组中取出指定索引的值
    public object Convert(object value, Type targetType, object parameter, ...)
    {
        DataArray = (int[])value;
        if (targetType == typeof(string))
            return DataArray[(int)parameter].ToString();
        // ...
    }

    // UI → Model: 将 TextBox 值写回数组的指定索引
    public object ConvertBack(object value, Type targetType, object parameter, ...)
    {
        sliderValue = Convert.ToDouble((string)value);
        DataArray[(int)parameter] = (int)sliderValue;
        return DataArray;  // 返回整个数组触发 UI 更新
    }
}
```

**关键设计**: `ConvertBack` 返回整个数组 (而非单个值)，这会触发 WPF 重新绑定所有 9 个 TextBox。

### 6.3 输入验证机制

**NumberRange 验证规则**:
```csharp
class NumberRange : ValidationRule
{
    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        if (value is string && ((string)value).Length == 0)
            return ValidationResult.ValidResult;

        int numVal;
        if (!int.TryParse(value.ToString(), out numVal))
            return new ValidationResult(false, "输入有效值");

        if (numVal > Wrapper.MaxValue || numVal < Wrapper.MinValue)
            return new ValidationResult(false, 
                string.Format("值范围:{0}~{1}", Wrapper.MinValue, Wrapper.MaxValue));

        return ValidationResult.ValidResult;
    }
}
```

**验证范围**: `MinCcmValue = -512`, `MaxCcmValue = 511`

**NumberOnlyBehaviour 附加属性**:
- 只允许数字和 `-` 字符输入
- 屏蔽空格键
- 粘贴时检查内容是否全为数字 (注意: **不允许负号粘贴**)

**IME 禁用**:
```xml
input:InputMethod.IsInputMethodEnabled="False"
```

### 6.4 预设值 RadioButtons 交互

**点击处理** (CCMArea.xaml.cs):
```csharp
private void OnSelectPresetCcmVal(object sender, RoutedEventArgs e)
{
    var button = sender as RadioButton;
    switch (button.Name)
    {
        case "PresetCCMValRButton":  _viewModel.SetPresetCcmData("R"); break;
        case "PresetCCMValGButton":  _viewModel.SetPresetCcmData("G"); break;
        case "PresetCCMValBButton":  _viewModel.SetPresetCcmData("B"); break;
        case "PresetCCMValYButton":  _viewModel.SetPresetCcmData("Y"); break;
        case "PresetCCMValCButton":  _viewModel.SetPresetCcmData("C"); break;
        case "PresetCCMValMButton":  _viewModel.SetPresetCcmData("M"); break;
    }
}
```

**TextBox 编辑时取消预设选择**:
```csharp
private void OnCCMDataTextBoxKeyDown(object sender, KeyEventArgs e)
{
    PresetCCMValRButton.IsChecked = false;
    PresetCCMValGButton.IsChecked = false;
    // ... 清除所有 RadioButton
}
```

**交互逻辑**: 用户在任意 TextBox 中按键时，所有 RadioButton 的选中状态都被清除，表示当前矩阵值不再匹配任何预设。

### 6.5 IsCcmEnable 使能绑定

**ViewModel 属性**:
```csharp
public bool IsCcmEnable
{
    get { return _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Ccm].Value; }
    set
    {
        _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Ccm]
            = new KeyValuePair<IspModule, bool>(IspModule.Ccm, value);
    }
}
```

**不直接存储状态**，而是直接读写 `Processor.IspCommonConfig.ProcessorStepsEnables` 集合中 `IspModule.Ccm` 对应的值。

**双向同步**:
```csharp
// 构造函数中注册事件监听
_ccmStep.PropertyChanged += OnCCMConfigChange;
_ispProcessor.IspCommonConfig.PropertyChanged += OnCommonConfigChange;

// CommonConfig 变化时更新 UI
private void OnCommonConfigChange(object sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == "IsCcmEnable")
        RaisePropertyChanged("IsCcmEnable");
}
```

### 6.6 CCMAreaViewModel 属性汇总

| 属性 | 类型 | 访问 | 数据绑定 | 说明 |
|------|------|------|---------|------|
| `IsCcmEnable` | bool | get/set | CheckBox.IsChecked | CCM 模块使能开关 |
| `ccm` | int[] | get/set | TextBox.Text (9 个) | 3x3 矩阵数组 (short[] → int[]) |
| `MinCcmValue` | int | get only | 验证规则最小值 | 固定返回 -512 |
| `MaxCcmValue` | int | get only | 验证规则最大值 | 固定返回 511 |

---

## 七、UI 实现 (开发者模式 CcmOnlineIQWindow)

### 7.1 窗口概述

**文件**: `d:\jrx\zl\isptool\ThunderSE\Ui\SettingWindow\Ccm\CcmWindow.xaml`

**注意**: `CcmWindow.xaml` 文件的 `x:Class` 属性声明为 `CcmOnlineIQWindow`，而非 `CcmWindow`。这意味着:
- 文件名是 `CcmWindow.xaml`，但类名是 `CcmOnlineIQWindow`
- 在 `DeviceConfigPage.xaml.cs` 中，CCM 模块的 `OpenSetttingWindow` 分支 **不存在**

### 7.2 布局结构

```
Window (Title="CcmOnlineIQWindow", 800x600)
  └── Grid (3 行)
        ├── Row 0 (70% 高度): ImageWithRubberBandControl (视频显示 + 橡皮筋选框)
        ├── Row 1 (23% 高度): DataGrid (显示计算结果，单列"值")
        └── Row 2 (7% 高度): StackPanel (3 个 Button)
              ├── "加载图片" → OnClickCalcIQ
              ├── "计算RGB均值" → OnClickCalcIQ (共用同一事件)
              └── "撤销选框" → OnClickUndoRubberBand
```

### 7.3 功能状态: **未完成**

| 功能 | 状态 | 说明 |
|------|------|------|
| 视频显示 | ❌ 未完成 | `OnUvcDataReceive` 为空实现，不更新画面 |
| 选框绘制 | ⚠️ 依赖控件 | `ImageWithRubberBandControl` 支持，但无数据驱动 |
| 加载图片 | ❌ 空实现 | `OnClickCalcIQ` 仅设置状态 |
| RGB 均值计算 | ❌ 空实现 | 无计算逻辑 |
| 结果显示 | ❌ 空集合 | DataGrid 绑定到空的 `ObservableCollection` |
| UVC 事件订阅 | ❌ 未执行 | `Onloaded` 中未订阅事件 |

### 7.4 预留的数据结构

```csharp
private double[] _avgRArray = new double[6];  // 对应 R/G/B/Y/C/M 6 种颜色的 R 均值
private double[] _avgGArray = new double[6];
private double[] _avgBArray = new double[6];
private int _selectedCalcMode = 0;
private List<RubberBandData> _rubberBandData = new List<RubberBandData>();
```

---

## 八、完整数据流

### 8.1 用户编辑参数 → 设备烧录

```
[用户操作] 编辑 CCMArea 中的 TextBox
    │
    ▼
[WPF Binding ConvertBack]
    DataArray[index] = newValue
    返回整个数组 → 触发 UI 刷新
    │
    ▼
[CCMAreaViewModel.ccm setter]
    _ccmStep.ccm = value.Select(x => (short)x).ToArray()
    │
    ▼
[CCM.ccm setter]
    _ccm = value
    HasChangedParams = true
    PropertyChanged("ccm")
    │
    ▼
[CCMAreaViewModel.OnCCMConfigChange]
    RaisePropertyChanged("ccm")
    通知 UI 更新其他 8 个 TextBox
    │
    ▼
[用户点击"写入配置"]
    │
    ▼
[Config.WriteToDevice()]
    遍历所有 IspModule
    检查 HasChangedParams (true → 写入)
    │
    ▼
[CCM.ParamsDataCollection getter]
    构建 CCMParams 结构体
    Marshal.StructureToPtr → 24 字节数组
    返回 {5, byte[24]}
    │
    ▼
[DeviceApi.WriteAx327XIspProperty(location, parameter, buffer, size)]
    分块写入 (每块最大 512 字节)
    参数编码: parameter = (sentPos << 8) | (5 * 2)
    │
    ▼
[设备固件] 接收 24 字节，更新 CCM 硬件寄存器
```

### 8.2 预设值应用流程

```
[用户操作] 点击 "R" 预设 RadioButton
    │
    ▼
[CCMArea.OnSelectPresetCcmVal]
    switch(button.Name) → _viewModel.SetPresetCcmData("R")
    │
    ▼
[CCMAreaViewModel.SetPresetCcmData("R")]
    _ccmStep.ccm = _presetCcmData["R"]
    → {272, 8, -24, 0, 256, 0, 0, 0, 256}
    │
    ▼
[CCM.ccm setter]
    HasChangedParams = true
    PropertyChanged("ccm")
    │
    ▼
[CCMAreaViewModel.OnCCMConfigChange]
    RaisePropertyChanged("ccm")
    │
    ▼
[WPF Binding 更新]
    9 个 TextBox 显示新值
    (通过 Converter.Convert 提取各索引值)
```

---

## 九、与 CommonConfig 的交互

### 9.1 使能控制

在 `CommonData` 结构体中:
```csharp
public char ccm_en;  // CCM 使能标志
```

在 `CommonConfig` 中:
```csharp
// 实际值映射表
public Dictionary<IspModule, char> ProcessorStepsEnablesActualValueMap = new Dictionary<IspModule, char>
{
    {IspModule.Ccm, (char)0x02},  // CCM 使能时写入 0x02
};

// 使能状态集合 (索引 5 对应 IspModule.Ccm)
public ObservableCollection<KeyValuePair<IspModule, bool>> ProcessorStepsEnables = 
    new ObservableCollection<KeyValuePair<IspModule, bool>>
{
    new KeyValuePair<IspModule, bool>(IspModule.Ccm, true),  // 默认启用
};
```

### 9.2 反向逻辑注意

```csharp
// ParamsDataCollection getter 中:
ccm_en = ProcessorStepsEnables[(int)(IspModule.Ccm)].Value
    ? (char)0x00                        // 启用 → 写入 0x00
    : ProcessorStepsEnablesActualValueMap[IspModule.Ccm];  // 禁用 → 写入 0x02
```

**注意**: 这里是**反向逻辑**!
- `ProcessorStepsEnables[5].Value == true` (启用) → `ccm_en = 0x00`
- `ProcessorStepsEnables[5].Value == false` (禁用) → `ccm_en = 0x02`

这与直觉相反 (通常 0 表示禁用)，可能是 ISP 固件的硬件寄存器设计为 "0 = 启用模块，非 0 = 禁用模块"。

---

## 十、已知问题清单

### 10.1 高严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B1 | s41/s42/s43 未序列化 | 配置文件加载后参数丢失 | 在 SerializeToXmlElement/DeserializeFromXmlElement 中添加序列化 |
| B2 | CcmWindow.xaml 类名不一致 | 无法通过 `new CcmWindow()` 创建实例 | 统一文件名和类名 |
| B3 | DeviceConfigPage 中无 CCM 窗口打开逻辑 | 开发者模式无法打开 CCM 调试窗口 | 添加 `case "CcmGrid"` 分支 |
| B4 | CcmOnlineIQWindow 功能未完成 | 视频显示、计算逻辑均为空实现 | 补充完整功能或移除入口 |

### 10.2 中严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B5 | 数组默认值为全 0 | 新配置中 CCM 矩阵为单位矩阵的 0 倍 | 初始化为单位矩阵 {256,0,0,0,256,0,0,0,256} |
| B6 | Marshal 缺少 try-finally | 异常时非托管内存泄漏 | 添加 try-finally 保护 |
| B7 | HasChangedParams 永不重置 | 无法区分"已修改"和"已应用" | 添加"已应用后重置"机制 |
| B8 | 使能逻辑反向 | `true → 0x00` 可能引起混淆 | 添加注释说明或修改硬件设计 |

### 10.3 低严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B9 | ccm 数组直接引用赋值 | 外部修改会影响内部状态 | 改为 Clone/Copy |
| B10 | Converter 有副作用 | ConvertBack 修改自身状态 | 改用纯函数 |
| B11 | 两个按钮共用同一事件 | "加载图片"和"计算RGB均值"功能混淆 | 分离为两个事件处理 |
| B12 | 硬编码范围值 | MinCcmValue/MaxCcmValue 硬编码在 ViewModel | 提取为常量或配置 |

---

## 十一、C# 端与 IspApi 的交互

### 11.1 当前状态

**CCM 模块与 IspApi.dll 没有任何直接交互。**

`IspApi.cs` 中导出的函数列表中**没有 CCM 相关的函数**:

| 函数 | 用途 |
|------|------|
| `DemosaicImg` | Bayer 转 RGB |
| `EncoderImgBuffer` | 图像编码 |
| `BlcCal` / `BlcImg` | BLC 校正 |
| `LscCal` / `LscImg` / `LscIQ` | LSC 校正与计算 |
| `AWBCal` / `AWBImg` / `AWBStatistic` / `AWB_Gain_Soft_Cal` / `AWB_IQ` | AWB 计算 |
| `YGammaImg` / `YGAMMA_IQ` | Gamma 校正 |

**CCM 的参数仅通过 `ParamsDataCollection` 二进制序列化后，由 `Device` 项目 (C++ DLL) 通过设备通信接口写入 ISP 硬件寄存器。**

### 11.2 建议的 P/Invoke 声明

如果需要从 C# 调用 `CCM_Cal`，应在 `IspApi.cs` 中添加:

```csharp
[DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
public static extern void CCM_Cal(
    int[] crAvg,        // [24] R 通道平均值
    int[] cgAvg,        // [24] G 通道平均值
    int[] cbAvg,        // [24] B 通道平均值
    int deltaCTh,       // 色差阈值 (典型 20)
    int deltaSTh,       // 饱和度偏差阈值 (典型 10)
    int cmatrixTh,      // 搜索范围 (典型 6)
    int step,           // 搜索步长 (典型 2)
    IntPtr cmatrixOut,  // 输出矩阵 (需分配 3x3 int 数组)
    int lightSource     // 光源类型 (0=理想, 1=D65)
);
```

---

## 十二、典型用户操作流程

### 12.1 场景：用户模式编辑 CCM 矩阵

```
1. 进入用户模式 (MainFrameForUser)
2. 在 EffectTab 找到"颜色矩阵"区域
3. 勾选"使能"CheckBox 启用 CCM 模块
4. 在 3x3 矩阵区域编辑各元素值:
   - 直接输入数值 (范围 -512 ~ 511)
   - 或点击预设值 RadioButton (R/G/B/Y/C/M) 应用预设矩阵
5. 右侧 UVC 预览实时观察效果变化
6. 点击"写入"将配置烧录到设备
7. 点击"保存到文件"备份配置
```

### 12.2 场景：应用预设矩阵

```
1. 在 EffectTab 的"颜色矩阵"区域
2. 点击 "R" RadioButton
3. 9 个 TextBox 自动更新为 R 预设值:
   [272] [8] [-24]
   [0]   [256] [0]
   [0]   [0]   [256]
4. 同时 "R" RadioButton 显示选中状态
5. 如果用户随后编辑任意 TextBox，所有 RadioButton 的选中状态被清除
```

### 12.3 场景：开发者模式 (如果 CcmWindow 功能完成)

```
1. 进入开发者模式 (MainFrameForDevelop)
2. 选择要调试的配置
3. 在 DeviceConfigPage 找到 CCM 模块
4. 点击"使用图示进行设置..."超链接
5. 打开 CcmOnlineIQWindow (如果功能完成)
6. 显示 UVC 实时视频流
7. 在画面上框选色块区域
8. 计算选框区域的 RGB 均值
9. 根据 RGB 值推导或验证 CCM 矩阵系数
10. 返回 DeviceConfigPage，保存配置
```

---

## 十三、关键文件清单

### 数据模型

| 文件 | 路径 | 职责 |
|------|------|------|
| CCM.cs | `DeviceConfig/Isp/CCM.cs` | CCM 数据模型、序列化 |
| ProcessStep.cs | `DeviceConfig/Isp/ProcessStep.cs` | 抽象基类 |
| CommonConfig.cs | `DeviceConfig/Isp/CommonConfig.cs` | 公共配置 (使能控制) |
| Processor.cs | `DeviceConfig/Isp/Processor.cs` | ISP 处理器 (模块注册) |
| IspApi.cs | `DeviceConfig/Isp/IspApi.cs` | P/Invoke 声明 (无 CCM) |

### UI (用户模式)

| 文件 | 路径 | 职责 |
|------|------|------|
| CCMArea.xaml | `Ui/MainWindow/UserMode/EffectTabControl/CCMArea.xaml` | UI 布局 |
| CCMArea.xaml.cs | `Ui/MainWindow/UserMode/EffectTabControl/CCMArea.xaml.cs` | 事件处理 |
| CCMAreaViewModel.cs | `Ui/MainWindow/UserMode/EffectTabControl/CCMAreaViewModel.cs` | ViewModel |
| DataArrayToControlCollectionConverter.cs | `Ui/MainWindow/UserMode/` | 数组转换器 |
| NumberRange.cs | `Ui/CommonCustomControl/` | 验证规则 + BindingProxy |
| NumberOnlyBehaviour.cs | `Ui/CommonCustomControl/` | 数字输入限制 |

### UI (开发者模式 - 未完成)

| 文件 | 路径 | 职责 |
|------|------|------|
| CcmWindow.xaml | `Ui/SettingWindow/Ccm/CcmWindow.xaml` | 在线 IQ 窗口 XAML |
| CcmWindow.xaml.cs | `Ui/SettingWindow/Ccm/CcmWindow.xaml.cs` | 在线 IQ 窗口代码 |
| CcmWindowViewModel.cs | `Ui/SettingWindow/Ccm/CcmWindowViewModel.cs` | ViewModel (空类) |

### C++ 算法

| 文件 | 路径 | 职责 |
|------|------|------|
| IQ.cpp | `IspApi/source/IQ.cpp` (行 1363-1513) | CCM_Cal, ColorCorrection, Rgb2Lab_CCM_IQ |
| Export.h | `IspApi/source/Export.h` | C 接口导出 |
| IQ.h | `IspApi/include/IQ.h` | 结构体定义 |
| IQ_config.cpp | `IspApi/source/IQ_config.cpp` | 默认配置 |

---

## 十四、附录

### 14.1 色彩空间转换公式

| 转换 | 公式 |
|------|------|
| sRGB 反伽马 | `x > 0.04045 ? pow((x+0.055)/1.055, 2.4) : x/12.92` |
| RGB → XYZ (D65) | `X = R*0.4124 + G*0.3576 + B*0.1805`<br>`Y = R*0.2126 + G*0.7152 + B*0.0722`<br>`Z = R*0.0193 + G*0.1192 + B*0.9505` |
| XYZ → Lab | `L* = 116 * f(Y/Yn) - 16`<br>`a* = 500 * (f(X/Xn) - f(Y/Yn))`<br>`b* = 200 * (f(Y/Yn) - f(Z/Zn))` |
| XYZ2LAB | `x > 0.008856 ? pow(x, 1/3) : 7.787 * x + 16/116` |
| 饱和度 C* | `C* = sqrt(a*² + b*²)` |
| Delta C | `ΔC = sqrt((a1-a2)² + (b1-b2)²)` |
| Delta E | `ΔE = sqrt(ΔL² + Δa² + Δb²)` |

### 14.2 Q8 定点数格式

| 十进制 | 十六进制 | Q8 表示 | 说明 |
|--------|---------|---------|------|
| 1.0 | 0x100 | 256 | 单位增益 |
| 1.0625 | 0x110 | 272 | 增强 6.25% |
| -0.09375 | -0x18 | -24 | 抑制 9.375% |
| 0.03125 | 0x08 | 8 | 微弱成分 |
| 0.0 | 0x000 | 0 | 无影响 |

**补码表示 (设备端)**:
- 负数: `value < 0` → `1024 + value` (1024 为模)
- 例如: -24 → 1000 (0x3E8)

### 14.3 24 色 ColorChecker (Macbeth 色卡)

标准 24 色卡包含:
- 6 行 × 4 列
- 包含: 肤色、蓝色天空、树叶、橙色等自然色
- 第 19-24 色: 灰度阶梯 (从黑到白)

---

**文档版本**: v1.0  
**创建日期**: 2026年4月8日  
**分析依据**: 项目源码完整阅读与交叉引用  
**文档状态**: 完整

---

**文档结束**

本文档基于 ThunderSE 项目实际代码深入分析生成，涵盖 CCM 模块的完整参数定义、C++ 算法原理、数据序列化规范、UI 交互设计和设备通信协议。可作为 CCM 模块开发、调试、测试和维护的参考文档。
