# LSC (镜头阴影校正) 模块详细需求规格说明

## 文档信息

| 项目 | 内容 |
|------|------|
| **模块名称** | LSC (Lens Shading Correction / 镜头阴影校正) |
| **DeviceModulePos** | 2 |
| **IspModule 枚举值** | `IspModule.Lsc` |
| **文档版本** | v1.0 |
| **创建日期** | 2026年4月8日 |
| **分析依据** | 项目源码完整阅读与交叉引用 |

---

## 一、模块概述

### 1.1 功能描述

LSC (Lens Shading Correction) 模块是 ISP 图像处理管线中的**镜头阴影校正模块**，用于补偿镜头光学系统造成的图像边缘亮度衰减和色彩偏移问题。

**核心功能**:
- 校正镜头暗角（图像中心亮、四角暗的现象）
- 校正不同波长光线折射率差异导致的四角色彩偏差
- 通过网格化的增益补偿表对每个 Bayer 通道独立校正
- 支持 Y 亮度模式和 RGB 四通道模式

### 1.2 物理原理

**镜头暗角成因**:
1. **光学暗角**: 镜头边缘进光量减少（余弦四次方定律）
2. **渐晕效应**: 镜头机械结构遮挡边缘光线
3. **传感器角度响应**: 边缘光线入射角大，量子效率降低

**色彩阴影成因**:
- 不同波长 (R/G/B) 光线在镜头中的折射率不同
- 导致四角相对于中心产生色彩偏移

### 1.3 在 ISP 管线中的位置

```
Raw Bayer → BLC → LSC → AWB → Demosaic → CCM → YGamma → EE → CH → 输出
   (0)       (1)    (2)    (4)               (5)    (7)    (11)  (9)
                      ↑ LSC 在第 2 步
```

**前置依赖**: BLC (黑电平校正) — LSC 需要在校正黑电平后的数据上计算权重

**处理类型**: RAW 域处理 (ProcessRawBuffer)

---

## 二、参数完整定义

### 2.1 参数总表

| 参数 | 类型 | 默认值 | 取值范围 | 说明 |
|------|------|--------|----------|------|
| CorrectionData | short[] | 256 (全) | 0 ~ 1023 | 网格校正系数数组 |
| LscMode | enum | Rgb (1) | Y(0) / Rgb(1) | 校正模式 |

### 2.2 CorrectionData 数组详解

**数组大小计算公式**:
```csharp
const int blockSizeX = 16;  // X 方向网格步长 (Bayer 半分辨率)
const int blockSizeY = 32;  // Y 方向网格步长

blockW = (ResolutionWidth / 2 + blockSizeX - 1) / blockSizeX + 1;
blockH = (ResolutionHeight / 2 + blockSizeY - 1) / blockSizeY + 1;
totalSize = 4 * blockH * blockW;  // 4 个 Bayer 通道
```

**公式逐行解读**:

| 步骤 | 计算 | 说明 |
|------|------|------|
| `ResolutionWidth / 2` | 宽度除以 2 | Bayer 格式每 2 列包含 R/Gr/Gb/B 各一个采样 |
| `+ blockSizeX - 1` | 加步长减 1 | 向上取整的准备 |
| `/ blockSizeX` | 整数除法 | 得到横向网格数 (不含边缘) |
| `+ 1` | 加 1 | 边缘需要额外的网格节点来覆盖 |
| `* 4` | 乘以 4 | 4 个 Bayer 通道 (R, Gr, Gb, B) 各自一张增益表 |

**示例计算** (1280x720 分辨率):
```
blockW = (1280/2 + 16 - 1) / 16 + 1 = (640 + 15) / 16 + 1 = 40 + 1 = 41
blockH = (720/2 + 32 - 1) / 32 + 1   = (360 + 31) / 32 + 1 = 12 + 1 = 13
totalSize = 4 * 13 * 41 = 2132 个 short
```

**数据布局**:
```
[通道0: blockH x blockW 网格] [通道1: blockH x blockW 网格] 
[通道2: blockH x blockW 网格] [通道3: blockH x blockW 网格]
```

通道映射取决于 Bayer 格式: `s = (i%2)*2 + (j%2)` (0=R/Gr/Gb/B 之一)

**增益值物理意义**:
| 增益值 | 含义 | 校正效果 |
|--------|------|---------|
| 256 | 1.0x 增益 | 不校正 |
| > 256 | > 1.0x 增益 | 提亮 (该区域比中心暗) |
| < 256 | < 1.0x 增益 | 压暗 (该区域比中心亮) |
| 0 | 0.0x 增益 | 完全抑制 (极端情况) |
| 1023 | ~4.0x 增益 | 最大提亮 (10-bit 上限) |

**默认值**: 256 (表示 1.0x 增益，无校正)

### 2.3 LscMode 枚举

```csharp
public enum LscMode
{
    Y,    // 0 - 基于亮度 (Y) 模式
    Rgb   // 1 - 基于 Bayer RGB 模式
}
```

**模式对比**:

| 特性 | Y 模式 (0) | Rgb 模式 (1) |
|------|-----------|-------------|
| **工作原理** | 先将 Bayer 转换为亮度图 (Y)，然后以亮度为基准计算增益 | 将 Bayer 按 4 个通道分离，分别计算各通道的中心参考亮度和网格增益 |
| **校正网格** | 单通道 (所有 Bayer 通道共用同一权重表) | 4 通道独立 (R, Gr, Gb, B 各自一张增益表) |
| **数组大小** | `blockH * blockW` | `4 * blockH * blockW` |
| **计算复杂度** | 低 (单次采样) | 高 (4 倍网格，4 倍采样) |
| **校正精度** | 中 (无法校正色差) | **高** (能精确校正不同颜色的色差) |
| **适用场景** | 灰度传感器、追求效率 | **推荐**，彩色传感器，精确校正 |
| **中心参考区域** | 17x17 = 289 点 | 9x9 = 81 点 (每通道) |
| **网格采样区域** | 9x9 = 81 点 | 5x5 = 25 点 (每通道) |

**Y 模式亮度计算公式**:
```cpp
// 以 polarity=0 (RGGB) 为例
Y = (R*77 + (G1+G2)/2*150 + B*29) / 256
```

**Rgb 模式推荐场景**: 对色彩精度要求高的场景，如手机相机、安防监控、车载摄像头。

---

## 三、C++ 算法实现

### 3.1 LscCal (权重计算)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 166-429)

**函数签名**:
```cpp
ISP_API void LscCal(
    const void *img_buffer,      // 输入: RAW Bayer 图像数据 (short*)
    int img_width,                // 图像宽度
    int img_height,               // 图像高度
    int block_size_x,             // X 方向分块大小 (16)
    int block_size_y,             // Y 方向分块大小 (32)
    int lsc_mode,                 // 0=Y 模式, 1=RGB 模式
    int polarity,                 // Bayer 排列: 0=RGGB, 1=GRBG, 2=BGGR, 3=GBRG
    unsigned int *lsc_table,      // 输出: 增益表 (4 * blockH * blockW)
    int ref_x, int ref_y          // 输入: 中心参考点坐标
);
```

#### 3.1.1 Y 模式算法流程

**步骤 1: 构建 Y 亮度图**

将 2x2 Bayer 块转换为单点亮度值:
```cpp
// 以 polarity=0 (RGGB) 为例
for (i=0; i<h/2; i+=2) {
    for (j=0; j<w/2; j+=2) {
        R = raw_img[i*w + j];
        G1 = raw_img[i*w + (j+1)];
        G2 = raw_img[(i+1)*w + j];
        B = raw_img[(i+1)*w + (j+1)];
        
        Y = (R*77 + (G1+G2)/2*150 + B*29) / 256;
        
        // 复制到 2x2 块的 4 个像素
        y_array[i*w + j] = Y;
        y_array[i*w + (j+1)] = Y;
        y_array[(i+1)*w + j] = Y;
        y_array[(i+1)*w + (j+1)] = Y;
    }
}
```

**四个极性排列的 R/G/B 位置映射**:

| Polarity | 名称 | R 位置 | G 位置 | B 位置 |
|----------|------|--------|--------|--------|
| 0 | RGGB | (0,0) | (0,1)/(1,0) | (1,1) |
| 1 | GRBG | (0,1) | (0,0)/(1,1) | (1,0) |
| 2 | BGGR | (1,1) | (0,1)/(1,0) | (0,0) |
| 3 | GBRG | (1,0) | (0,0)/(1,1) | (0,1) |

**步骤 2: 计算中心参考亮度 y_max**

在参考点 `(ref_x, ref_y)` 周围取 17x17 区域 (289 个点):
```cpp
// 1. 采样 17x17 区域
for (i=0; i<17; i++) {
    for (j=0; j<17; j++) {
        tmp_array[i*17 + j] = y_array[(ref_y-8+i)*w + (ref_x-8+j)];
    }
}

// 2. 冒泡排序取中位数
BubbleSort(tmp_array, 289);
mean_val = tmp_array[144];  // 中位数 (289/2 = 144)

// 3. 去噪: 将偏离中位数 > 50 的像素替换为中位数
y_max = 0;
for (i=0; i<289; i++) {
    y_max += (abs(tmp_array[i] - mean_val) < 50 ? tmp_array[i] : mean_val);
}
y_max /= 289;  // 去噪后的平均亮度作为参考亮度
```

**步骤 3: 计算网格增益**

遍历图像，对每个网格交叉点采样 9x9 区域:

**9 种位置情况**:

| tmp_case | 位置 | 采样区域偏移 |
|----------|------|-------------|
| 0 | 左上角 | `[i..i+8][j..j+8]` |
| 1 | 右上角 | `[i..i+8][j-8..j]` |
| 2 | 左下角 | `[i-8..i][j..j+8]` |
| 3 | 右下角 | `[i-8..i][j-8..j]` |
| 4 | 上边 | `[i..i+8][j-4..j+4]` |
| 5 | 左边 | `[i-4..i+4][j..j+8]` |
| 6 | 下边 | `[i-8..i][j-4..j+4]` |
| 7 | 右边 | `[i-4..i+4][j-8..j]` |
| 8 | 内部 | `[i-4..i+4][j-4..j+4]` |

**中位数与去噪** (9x9=81 点):
```cpp
// 1. 采样 9x9 区域
// 2. 冒泡排序 81 个元素
// 3. 取中位值 tmp_array[40]
// 4. 阈值去噪 (val_th=50) 后求平均得到 y_tmp

// 5. 计算增益
gain = CLIP_PIXEL((unsigned int)(y_max / y_tmp * 256), 0, 1023);

// 6. 四个通道填充相同值 (Y 模式)
for (k=0; k<4; k++) {
    lsc_table[block_y*block_w + block_x + k*block_h*block_w] = gain;
}
```

#### 3.1.2 Rgb 模式算法流程

**步骤 1: Bayer 通道分离**

将 Bayer 图像按位置分离为 4 个独立通道数组 (每个为原图 1/4 大小):
```cpp
for (i=0; i<h; i++) {
    for (j=0; j<w; j++) {
        bformat = (i%2)*2 + (j%2);  // 0,1,2,3 对应 4 个通道
        tmp_array[bformat][(i/2)*w/2 + (j/2)] = raw_img[i*w + j];
    }
}
```

**注意**: 这里没有考虑极性差异，固定按位置分通道。

**步骤 2: 计算中心参考值 mid_val[4]**

在 `(ref_x/2, ref_y/2)` 周围对 4 个通道各取 9x9 区域:
```cpp
for (k=0; k<4; k++) {
    // 1. 采样 9x9 区域
    // 2. 冒泡排序 81 个元素
    // 3. 取中位值 mean_val[k] = block_array[k][40]
    // 4. 阈值去噪 (val_th=50) 后求平均
    mid_val[k] = 去噪后的平均值;
}
```

**步骤 3: 计算网格增益 (各通道独立)**

在降采样坐标系 (w/2 x h/2) 上遍历，对每个网格交叉点采样 5x5 区域:

```cpp
for (k=0; k<4; k++) {
    // 1. 采样 5x5 区域 (25 点)
    // 2. 冒泡排序 25 个元素
    // 3. 取中位值 mean_val[k] = block_array[k][12]
    // 4. 阈值去噪 (val_th=50) 后求平均得到 tmp_val[k]
    
    // 5. 计算各通道增益
    lsc_table[block_y*block_w + block_x + k*block_h*block_w] = 
        CLIP_PIXEL((unsigned int)(mid_val[k] / tmp_val[k] * 256), 0, 1023);
}
```

#### 3.1.3 算法参数汇总

| 参数 | Y 模式 | Rgb 模式 | 说明 |
|------|--------|---------|------|
| 中心参考区域 | 17x17 = 289 点 | 9x9 = 81 点 (每通道) | 参考点亮度/值采样 |
| 网格采样区域 | 9x9 = 81 点 | 5x5 = 25 点 (每通道) | 网格点亮度/值采样 |
| 去噪阈值 | 50 | 50 | 偏离中位数超过此值的像素被替换 |
| 排序算法 | 冒泡排序 | 冒泡排序 | 用于计算中位数 |
| 增益基准值 | 256 | 256 | 1.0x 增益对应 256 |
| 增益范围 | [0, 1023] | [0, 1023] | 10-bit 存储 |

### 3.2 LscImg (图像处理)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 687-720)

**函数签名**:
```cpp
ISP_API void LscImg(
    void *raw_img,            // 输入: RAW Bayer 图像 (BLC 校正后)
    int image_width,           // 图像宽度
    int image_height,          // 图像高度
    int block_size_x,          // X 方向分块大小 (16)
    int block_size_y,          // Y 方向分块大小 (32)
    unsigned int* lsc_weight,  // 输入: LSC 增益表 (来自 LscCal)
    void *lsc_img              // 输出: 校正后图像 (short*)
);
```

#### 3.2.1 双线性插值算法

对每个像素 `(i, j)` 执行双线性插值:

**步骤 1: 确定所在网格和通道**

```cpp
xs = j % 2;              // 像素在 2x2 Bayer 块内的 x 偏移 (0 或 1)
ys = i % 2;              // 像素在 2x2 Bayer 块内的 y 偏移 (0 或 1)
s = ys * 2 + xs;         // 通道索引 0,1,2,3 (对应 R/Gr/Gb/B)

block_y = (i/2) / block_size_y;   // Y 方向网格索引
block_x = (j/2) / block_size_x;   // X 方向网格索引
weight_y = (i/2) % block_size_y;  // 在网格内的 y 偏移
weight_x = (j/2) % block_size_x;  // 在网格内的 x 偏移
```

**步骤 2: 获取四角网格点增益**

```cpp
// 左上角
tmp1 = lsc_weight[block_h*block_w*s + block_y*block_w + block_x] 
       * (block_size_x - weight_x) * (block_size_y - weight_y);

// 左下角
tmp2 = lsc_weight[block_h*block_w*s + (block_y+1)*block_w + block_x] 
       * weight_y * (block_size_x - weight_x);

// 右上角
tmp3 = lsc_weight[block_h*block_w*s + block_y*block_w + (block_x+1)] 
       * (block_size_y - weight_y) * weight_x;

// 右下角
tmp4 = lsc_weight[block_h*block_w*s + (block_y+1)*block_w + block_x+1] 
       * weight_y * weight_x;
```

**步骤 3: 插值与增益应用**

```cpp
// 归一化 (标准双线性插值公式)
t = (tmp1 + tmp2 + tmp3 + tmp4) / block_size_y / block_size_x;

// 应用增益: 原始像素值 * 插值增益 / 256
lscimg[i*w + j] = CLIP_PIXEL(t * rawimg[i*w + j] / 256, 0, 1023);
```

**双线性插值示意图**:

```
网格节点:  (block_x, block_y) ─────── (block_x+1, block_y)
          权重: (bsx-wx)*(bsy-wy)        (bsy-wy)*wx
                              │               │
            (block_x, block_y+1) ─────── (block_x+1, block_y+1)
          权重: wy*(bsx-wx)               wy*wx

像素位置:  weight_x 距离左边, weight_y 距离上边
```

**算法原理**: 标准双线性插值公式:
```
f(x,y) = f(Q11)*(x2-x)*(y2-y) + f(Q21)*(x-x1)*(y2-y) + f(Q12)*(x2-x)*(y-y1) + f(Q22)*(x-x1)*(y-y1)
```

这里 `block_size_x` 和 `block_size_y` 充当 `(x2-x1)` 和 `(y2-y1)`，最后除以 `block_size_y * block_size_x` 归一化。

#### 3.2.2 边界处理

代码**未做显式边界检查**。由于 `block_h` 和 `block_w` 计算时加了 1:
```cpp
block_h = (h/2 + block_size_y - 1) / block_size_y + 1;
block_w = (w/2 + block_size_x - 1) / block_size_x + 1;
```

这确保 `block_y+1` 和 `block_x+1` 不会越界访问增益表。

### 3.3 LscIQ (质量评估)

**文件**: `d:\jrx\zl\isptool\IspApi\source\IQ.cpp` (行 430-686)

**函数签名**:
```cpp
ISP_API void LscIQ(
    short **img_buffer,                    // 输入: Demosaic 后的 RGB 图像 [3][w*h]
    int img_width,                          // 图像宽度
    int img_height,                         // 图像高度
    lsc_cs_iq_result* colorShadingIQ,      // 输出: 色彩阴影评估结果
    lsc_ls_iq_result* lensShadingIQ        // 输出: 镜头阴影评估结果
);
```

#### 3.3.1 5 个采样区域位置

| 区域索引 | 位置 | 采样区域中心坐标 | 采样范围 |
|---------|------|-----------------|---------|
| 0 | 左上 (TL) | `(2, 2)` | `[0..4][0..4]` |
| 1 | 右上 (TR) | `(2, w-3)` | `[0..4][w-5..w-1]` |
| 2 | 左下 (BL) | `(h-3, 2)` | `[h-5..h-1][0..4]` |
| 3 | 右下 (BR) | `(h-3, w-3)` | `[h-5..h-1][w-5..w-1]` |
| 4 | 中心 (MID) | `(h/2-1, w/2-1)` | `[h/2-3..h/2+1][w/2-3..w/2+1]` |

每个采样区域取 5x5 = 25 个像素。

#### 3.3.2 中位数计算和去噪

对每个区域的每个通道 (R/G/B 共 3 通道):

```cpp
// 1. 采集 25 个像素值
// 2. 冒泡排序 25 个元素
// 3. 取中位值: mean = sorted[12]
// 4. 去噪: sum += (abs(val[n] - mean) < 10 ? val[n] : mean)
// 5. 平均: result = sum / 25
```

#### 3.3.3 ColorShadingIQResult 计算

**色彩空间**: 使用 R/G 和 B/G 比值评估色彩均匀性

**R/G 比值** (四角相对于中心):
```cpp
cr_tl = (r_tl / g_tl) / (r_mid / g_mid);   // 左上角 R/G 相对于中心
cr_tr = (r_tr / g_tr) / (r_mid / g_mid);   // 右上角
cr_bl = (r_bl / g_bl) / (r_mid / g_mid);   // 左下角
cr_br = (r_br / g_br) / (r_mid / g_mid);   // 右下角
```

**B/G 比值** (四角相对于中心):
```cpp
cb_tl = (b_tl / g_tl) / (b_mid / g_mid);
cb_tr = (b_tr / g_tr) / (b_mid / g_mid);
cb_bl = (b_bl / g_bl) / (b_mid / g_mid);
cb_br = (b_br / g_br) / (b_mid / g_mid);
```

**偏差百分比**:
```cpp
rg_tl_rate = ((r_tl/g_tl - r_mid/g_mid) / (r_mid/g_mid)) * 100;
bg_tl_rate = ((b_tl/g_tl - b_mid/g_mid) / (b_mid/g_mid)) * 100;
// ... 其余 6 个角同理
```

**物理意义**:
| 指标 | 理想值 | 含义 |
|------|--------|------|
| `cr_tl = 1.0` | 无色偏 | 左上角 R/G 比与中心一致 |
| `cr_tl > 1.0` | 偏红 | 左上角相对中心偏红 |
| `cr_tl < 1.0` | 偏绿 | 左上角相对中心偏绿 |
| `cb_tl = 1.0` | 无色偏 | 左上角 B/G 比与中心一致 |
| `cb_tl > 1.0` | 偏蓝 | 左上角相对中心偏蓝 |
| `cb_tl < 1.0` | 偏绿 | 左上角相对中心偏绿 |

#### 3.3.4 LensShadingIQResult 计算

**亮度计算** (BT.601 权重，省略 /256):
```cpp
y_tl  = 77 * r_tl  + 150 * g_tl  + 29 * b_tl;
y_tr  = 77 * r_tr  + 150 * g_tr  + 29 * b_tr;
y_bl  = 77 * r_bl  + 150 * g_bl  + 29 * b_bl;
y_br  = 77 * r_br  + 150 * g_br  + 29 * b_br;
y_mid = 77 * r_mid + 150 * g_mid + 29 * b_mid;
```

注意: 这里省略了 `/256`，因为比值计算中分子分母的系数会抵消。

**亮度比值**:
```cpp
ly_tl = y_tl / y_mid;   // 左上角亮度相对于中心
ly_tr = y_tr / y_mid;
ly_bl = y_bl / y_mid;
ly_br = y_br / y_mid;
```

**亮度偏差百分比**:
```cpp
y_tl_rate = (y_tl - y_mid) / y_mid * 100;
// ... 其余 3 个角同理
```

**物理意义**:
| 指标 | 理想值 | 含义 |
|------|--------|------|
| `ly_tl = 1.0` | 无暗角 | 左上角与中心亮度一致 |
| `ly_tl < 1.0` | 有暗角 | 左上角比中心暗 (常见) |
| `ly_tl > 1.0` | 过亮 | 左上角比中心亮 (不常见) |

#### 3.3.5 合格范围标准

| 指标类型 | 字段 | 合格范围 | 说明 |
|---------|------|---------|------|
| **Color Shading (Cr)** | cr_tl, cr_tr, cr_bl, cr_br | **0.85 - 1.20** | 四角 R/G 比率相对于中心 ±15% |
| **Color Shading (Cb)** | cb_tl, cb_tr, cb_bl, cb_br | **0.85 - 1.20** | 四角 B/G 比率相对于中心 ±15% |
| **Lens Shading (Ly)** | ly_tl, ly_tr, ly_bl, ly_br | **0.80 - 1.10** | 四角 Y 亮度相对于中心 -20% ~ +10% |

**注意**: `rg_*_rate`, `bg_*_rate`, `y_*_rate` 字段**没有**合格范围定义，仅用于参考。

#### 3.3.6 典型值解读

| 指标组合 | 物理含义 | 评价 |
|----------|---------|------|
| `ly_* ≈ 0.95` | 校正后四角亮度约为中心的 95%，残余暗角 5% | **优秀** |
| `ly_* ≈ 0.85` | 校正后四角亮度约为中心的 85%，残余暗角 15% | **良好** |
| `ly_* < 0.80` | 校正不足，四角仍有明显暗角 | **需调整** |
| `ly_* > 1.10` | 校正过度，四角过亮 | **需调整** |
| `cr_* ≈ 1.05` | 四角 R/G 比中心高 5%，轻微偏红 | **可接受** |
| `cb_* ≈ 0.92` | 四角 B/G 比中心低 8%，轻微偏绿 | **可接受** |
| `cr_* > 1.20` | 四角明显偏红 | **需调整** |
| `cb_* < 0.85` | 四角明显偏蓝 | **需调整** |

---

## 四、数据模型实现

### 4.1 核心属性

**文件**: `d:\jrx\zl\isptool\ThunderSE\DeviceConfig\Isp\LensShading.cs`

| 属性 | 类型 | 访问 | 说明 |
|------|------|------|------|
| `CorrectionData` | short[] | get/set | 网格校正系数数组 |
| `LscMode` | LscMode | get/set | 校正模式 (Y/Rgb) |
| `DeviceModulePos` | int | get | 固定值 2 |
| `HasChangedParams` | bool | get/set | 参数变更标志 |

### 4.2 延迟初始化 (Lazy Initialization)

```csharp
private void EnsureCorrectionDataInitialized()
{
    int requiredSize = CalculateRequiredLscSize();
    if (requiredSize == 0) return;  // 配置未初始化或无效

    if (_correctionData == null || _correctionData.Length != requiredSize)
    {
        _currentExpectedSize = requiredSize;
        _correctionData = new short[requiredSize];
        for (int i = 0; i < requiredSize; i++)
            _correctionData[i] = 256; // 默认 1.0x 增益
        
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CorrectionData"));
    }
}
```

**关键设计**: 数组不在构造函数中创建，而是在首次访问时按需创建，确保此时 `_commonConfig.ResolutionWidth/Height` 已经设置。

### 4.3 分辨率变化重新初始化

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

**关键设计**: 采用**懒失效 (Lazy Invalidation)** 模式。不立即重建数组，而是标记为无效，延迟到实际访问时才重建。这避免了分辨率连续多次变化时不必要的重复分配。

### 4.4 CalWeight 方法完整流程

```csharp
public void CalWeight(byte[] rawFileBuffer, LscMode lscMode, int pointX, int pointY)
{
    // 1. 确保校正数据已初始化
    EnsureCorrectionDataInitialized();

    // 2. 创建输出缓冲区
    var lscWeightBuffer = new int[_currentExpectedSize];

    try
    {
        // 3. 调用 C++ 算法
        IspApi.LscCal(rawFileBuffer, 
            _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight,
            _blockSizeX, _blockSizeY, 
            (int)lscMode, (int)_commonConfig.Bayer, 
            lscWeightBuffer, pointX, pointY);
    }
    catch (Exception ex)
    {
        Console.WriteLine("LscCal exception: " + ex.Message);
        return;
    }

    // 4. 安全截断，防止 short 溢出
    _correctionData = lscWeightBuffer
        .Select(x => (short)Math.Max(0, Math.Min(x, short.MaxValue)))
        .ToArray();

    // 5. 通知变更
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CorrectionData"));
}
```

### 4.5 ProcessRawBuffer 图像处理流程

```csharp
public override void ProcessRawBuffer(ref byte[] imgBuffer)
{
    // 1. 分配 short 输出缓冲区
    short[] outputBuffer = new short[_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight];
    byte[] outputByteBuffer = new byte[Buffer.ByteLength(outputBuffer)];

    // 2. 转换权重数据 short[] → int[]
    var lscWeightBuffer = CorrectionData.Select(x => Convert.ToInt32(x)).ToArray();

    // 3. 调用 C++ 校正
    IspApi.LscImg(imgBuffer, 
        _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, 
        _blockSizeX, _blockSizeY, lscWeightBuffer, outputBuffer);

    // 4. 拷贝数据
    Buffer.BlockCopy(outputBuffer, 0, outputByteBuffer, 0, outputByteBuffer.Length);

    // 5. 替换引用
    imgBuffer = outputByteBuffer;
}
```

### 4.6 CalcIQ 方法

```csharp
public void CalcIQ(byte[] fileBuffer, 
    ref ColorShadingIQResult colorShadingIQResult, 
    ref LensShadingIQResult lensShadingIQResult)
{
    using (var memoryManager = new MemoryManager())
    {
        // 1. 分配 3 个 IntPtr (R/G/B 三通道)
        IntPtr[] ptrArray = new IntPtr[3];
        for (int i = 0; i < ptrArray.Length; i++)
        {
            ptrArray[i] = memoryManager.AllocateMemory(
                _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
        }

        // 2. Demosaic: Bayer → RGB
        IspApi.DemosaicImg(fileBuffer, (int)_commonConfig.Bayer, 
            _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, ptrArray);

        // 3. 计算 IQ 指标
        IspApi.LscIQ(ptrArray, _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, 
            ref colorShadingIQResult, ref lensShadingIQResult);
    }
}
```

---

## 五、UI 实现 (LscWindow)

### 5.1 窗口整体布局

**文件**: `d:\jrx\zl\isptool\ThunderSE\Ui\SettingWindow\Lsc\LscWindow.xaml`

```
Window (Title="LscWindow", 1000x800, NoResize)
  └── Grid (Margin: 10)
        ├── Row 0 (600px): TabControl (ImgDisplayTab)
        │     ├── TabItem (OriginTab, Header="原图")
        │     │     └── Canvas (OriginImgCanvas)
        │     │           ├── Image (RawImg) — 绑定 OriginRawFileBuffer
        │     │           └── Border (DrawingDotAreaBorder) — 合法描点区域
        │     │                 └── Grid (dot, 10x10) — 红色十字准心
        │     │
        │     └── TabItem (Header="lsc效果")
        │           └── Canvas (ProcessedImgCanvas)
        │                 └── Image (ProcessedImg) — 绑定 ProcessedRawFileBuffer
        │
        └── Row 1 (*): StackPanel (垂直排列)
              ├── [Button: "加载Raw文件"] — 绑定 LoadRawFileCommand
              ├── [Label: "lsc模式:"] + [ComboBox: Y/RGB] — 绑定 SelectedLscMode
              ├── [Button: "计算Lsc"] — Click="ClickCalc"
              ├── [Button: "查看IQ"] — 绑定 ViewIQCommand
              └── [Hyperlink: "查看先行步骤"] — 绑定 ViewPreviousIspStepCommand
```

### 5.2 中心点选择交互

**LSC_SAFE_MARGIN = 10 像素**

**坐标转换公式**:
```
步骤 1: 用户点击 OriginImgCanvas → e.GetPosition(DrawingDotArea)
        得到 _dotPos (相对于 DrawingDotArea 内部的坐标)

步骤 2: _dotPos.X * _horizontalScale
        将 DrawingDotArea 内部坐标缩放到原始图像坐标
        其中 _horizontalScale = RawImg.Source.Width / (_maxX - _minX)

步骤 3: + LSC_SAFE_MARGIN (10)
        加上安全边距偏移

步骤 4: Math.Max(10, Math.Min(rawX, ResolutionWidth - 10))
        钳位到合法范围 [10, ResolutionWidth - 10]

最终公式:
  rawX = clamp(_dotPos.X * (Source.Width / displayWidth) + 10, 10, ResolutionWidth - 10)
  rawY = clamp(_dotPos.Y * (Source.Height / displayHeight) + 10, 10, ResolutionHeight - 10)
```

**合法描点区域**:
```csharp
// 从图像区域的每边各减去 10 像素
Canvas.SetLeft(DrawingDotAreaBorder, _minX + 10 / _horizontalScale);
Canvas.SetTop(DrawingDotAreaBorder, _minY + 10 / _verticalScale);
DrawingDotAreaBorder.Width = _maxX - _minX - 20 / _horizontalScale;
DrawingDotAreaBorder.Height = _maxY - _minY - 20 / _verticalScale;
```

### 5.3 鼠标移动显示颜色值

```csharp
private void OnCanvasMouseMove(object sender, MouseEventArgs e)
{
    // 1. 判断是哪个 Canvas 触发的
    // 2. 限制坐标在有效范围内 [_minX, _maxX] 和 [_minY, _maxY]
    // 3. 将 Canvas 坐标转换为图像像素坐标
    // 4. 裁剪 1x1 像素
    var croppedBitmap = new CroppedBitmap((BitmapSource)imgSource,
        new Int32Rect((int)AbsoluteXValue, (int)AbsoluteYValue, 1, 1));
    
    // 5. 读取像素值 (BGRA 格式)
    var pixels = new byte[4];
    croppedBitmap.CopyPixels(pixels, 4, 0);
    
    // 6. 计算 Y 亮度值 (BT.601)
    int Y = (pixels[2] * 77 + pixels[1] * 150 + pixels[0] * 29) / 256;
    
    // 7. 更新 TextBlock 显示
    _colorDisplayBlock.Text = String.Format("R:{0},G:{1},B:{2},Y:{3}",
        pixels[2], pixels[1], pixels[0], Y);
}
```

### 5.4 完整操作流程

```
[用户点击"加载Raw文件"]
    │
    ▼
LoadRawFileAsync()
    │
    ├── OpenFileDialog (*.raw)
    │
    └── Task.Run(File.ReadAllBytes)
            │
            ▼
        OriginRawFileBuffer = byte[]
            │
            ▼
        RawBufferToBitmapImageConverter.Convert()
            │
            ├── Demosaic (Bayer → RGB)
            │
            ├── EncoderImgBuffer (RGB → JPEG)
            │
            └── BitmapImage (JPEG 解码显示)
                    │
                    ▼
                Image.Source 更新 → 显示 RAW 图像


[用户在原图 Tab 上点击选择中心点]
    │
    ▼
OnMouseLeftDown()
    │
    ├── _dotPos = e.GetPosition(DrawingDotArea)
    │
    ├── 边界检查 (必须在 DrawingDotAreaBorder 内)
    │
    └── dot.Visibility = Visible
         Canvas.SetLeft(dot, _dotPos.X - 5)
         Canvas.SetTop(dot, _dotPos.Y - 5)


[用户点击"计算Lsc"按钮]
    │
    ▼
ClickCalc()
    │
    ├── 检查 dot.Visibility == Visible?
    │    └── 否 → MessageBox("请先在图上描点！")
    │
    ├── 计算原始分辨率坐标:
    │    rawX = _dotPos.X * _horizontalScale + 10
    │    rawY = _dotPos.Y * _verticalScale + 10
    │
    ├── 钳位到 [10, Resolution-10]
    │
    └── CalcLscWeightCommand.Execute([rawX, rawY])
            │
            ▼
        LensShading.CalWeight(rawBuffer, mode, pointX, pointY)
            │
            ├── EnsureCorrectionDataInitialized()
            │
            ├── IspApi.LscCal(...) → int[] 权重
            │
            ├── 安全截断: int[] → short[] [0, 32767]
            │
            └── PropertyChanged("CorrectionData")
                    │
                    ▼
                LscConfigsChange()
                    │
                    ├── ClearCache()
                    │
                    ├── 复制 OriginRawFileBuffer → ProcessedRawFileBuffer
                    │
                    ├── Processor.ProcessRawFile(ref buffer, Lsc)
                    │    ├── BLC (如果启用)
                    │    └── LSC: IspApi.LscImg()
                    │
                    └── RaisePropertyChanged("ProcessedRawFileBuffer")
                            │
                            ▼
                        Image.Source 更新 → 显示处理后图像
                            │
                            ▼
                        OnProcessedImageUpdated()
                            └── ImgDisplayTab.SelectedIndex = 1


[用户点击"查看IQ"按钮]
    │
    ▼
ViewIQCommand → ViewIQ()
    │
    └── new LscIQWindow(_lensShading, ProcessedRawFileBuffer)
            │
            ├── CalcIQ() → ColorShadingIQResult + LensShadingIQResult
            │
            ├── 反射遍历结果字段
            │
            ├── 对比预设范围 _iQRangeDictionary
            │
            └── 显示 DataGrid (项, 值, 范围, 是否合格)
```

---

## 六、LSC IQ 窗口实现

### 6.1 布局结构

**文件**: `d:\jrx\zl\isptool\ThunderSE\Ui\SettingWindow\Lsc\LscIQWindow.xaml`

```
Window (Title="LscIQWindow", 400x300, NoResize)
  └── DataGrid (WPFToolkit)
        ├── CellStyle (居中对齐)
        ├── GroupStyle (Expander 分组容器)
        └── Columns (4 列)
              ├── 项 (100px) — 绑定 Name
              ├── 值 (60px) — 绑定 Value
              ├── 范围 (140px) — 绑定 ValueRange
              └── 是否在范围内 (90px) — 绑定 IsGoodValue
```

### 6.2 数据模型

**IQData 类**:
```csharp
public class IQData
{
    public string Group { get; set; }          // 分组名: "ColorShadingIQ" / "LensShadingIQ"
    public string Name { get; set; }           // 字段名: "cr_tl", "ly_br" 等
    public double Value { get; set; }          // 实际计算值
    public string ValueRange { get; set; }     // 范围字符串: "0.85-1.20"
    public bool? IsGoodValue { get; set; }     // 是否合格 (null=无范围)
}
```

### 6.3 合格范围定义

```csharp
private Dictionary<string, ValueRange> _iQRangeDictionary = new Dictionary<string, ValueRange>()
{
    // Cr 通道四角: 0.85 - 1.20
    {"cr_tl", new ValueRange(0.85, 1.20)},
    {"cr_tr", new ValueRange(0.85, 1.20)},
    {"cr_bl", new ValueRange(0.85, 1.20)},
    {"cr_br", new ValueRange(0.85, 1.20)},

    // Cb 通道四角: 0.85 - 1.20
    {"cb_tl", new ValueRange(0.85, 1.20)},
    {"cb_tr", new ValueRange(0.85, 1.20)},
    {"cb_bl", new ValueRange(0.85, 1.20)},
    {"cb_br", new ValueRange(0.85, 1.20)},

    // Luma Y 四角: 0.80 - 1.10
    {"ly_tl", new ValueRange(0.80, 1.10)},
    {"ly_tr", new ValueRange(0.80, 1.10)},
    {"ly_bl", new ValueRange(0.80, 1.10)},
    {"ly_br", new ValueRange(0.80, 1.10)}
};
```

### 6.4 数据流

```
RAW 图像 → Demosaic → LscIQ (C++ DLL)
    │
    ├── ColorShadingIQResult (16 个 double)
    │     cr_tl, cr_tr, cr_bl, cr_br
    │     cb_tl, cb_tr, cb_bl, cb_br
    │     rg_tl_rate, rg_tr_rate, rg_bl_rate, rg_br_rate
    │     bg_tl_rate, bg_tr_rate, bg_bl_rate, bg_br_rate
    │
    └── LensShadingIQResult (8 个 double)
          ly_tl, ly_tr, ly_bl, ly_br
          y_tl_rate, y_tr_rate, y_bl_rate, y_br_rate
              │
              ▼ (反射遍历字段)
          ObservableCollection<IQData>
              │
              ▼ (CollectionViewSource.GetDefaultView)
          ICollectionView (按 Group 分组)
              │
              ▼ (XAML Binding)
          DataGrid (Expander 分组显示)
```

---

## 七、数据序列化

### 7.1 XML 序列化 (配置文件)

```csharp
public override XmlElement SerializeToXmlElement(XmlDocument xmlDoc)
{
    EnsureCorrectionDataInitialized();
    var xmlElement = xmlDoc.CreateElement("Lsc");

    XmlElement lscWeight = xmlDoc.CreateElement("Lsc_Weight");
    string lscWeightStr = string.Join(",", CorrectionData.Select(x => x.ToString()).ToArray());
    lscWeight.AppendChild(xmlDoc.CreateTextNode(lscWeightStr));
    xmlElement.AppendChild(lscWeight);

    return xmlElement;
}
```

**XML 格式**:
```xml
<Lsc>
    <Lsc_Weight>256,256,256,258,255,257,...</Lsc_Weight>
</Lsc>
```

### 7.2 XML 反序列化

```csharp
public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
{
    var lscNode = ispToolDataNode["Lsc"];
    var tmpLscWeightStr = lscNode["Lsc_Weight"].FirstChild.Value;
    CorrectionData = tmpLscWeightStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
        .Select(s => Convert.ToInt16(s))
        .ToArray();
}
```

**⚠️ 风险**: 缺少 null 检查，如果节点不存在会抛出 `NullReferenceException`。

### 7.3 二进制序列化 (设备烧录用)

**Getter**:
```csharp
int byteCount = CorrectionData.Length * sizeof(short);
byte[] arr = new byte[byteCount];
Buffer.BlockCopy(CorrectionData, 0, arr, 0, byteCount);
return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
```

**Setter**:
```csharp
var tmpData = new short[CorrectionData.Length];
Buffer.BlockCopy(value[DeviceModulePos], 0, tmpData, 0, tmpData.Length * sizeof(short));
CorrectionData = tmpData;
```

**输出**: 单元素字典 `{2, byte[CorrectionData.Length * 2]}`，键为 `DeviceModulePos = 2`

---

## 八、已知问题清单

### 8.1 高严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B1 | DeserializeFromXmlElement 缺少 null 检查 | NullReferenceException | 添加 XmlHelper 安全解析 |
| B2 | CalWeight 不设置 HasChangedParams | 通过字段赋值绕过属性 setter | 直接调用属性 setter 或手动设置 |
| B3 | ProcessRawBuffer 存在冗余操作 | 性能浪费 | 移除 Array.Clear 和重复缓冲区分配 |

### 8.2 中严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B4 | _cachedRgbPlanes 未使用 | 设计预留但未实现 | 实现或移除 |
| B5 | ParamsDataCollection Setter 缺少尺寸校验 | 可能导致数据损坏 | 添加尺寸检查 |
| B6 | 每次调用重新分配缓冲区 | 性能问题 | 添加缓存机制 |

### 8.3 低严重性

| 编号 | 问题 | 影响 | 修复建议 |
|------|------|------|---------|
| B7 | ProcessRgbBuffer 未实现 | 仅支持 RAW 格式 | 设计决定，可接受 |
| B8 | LscIQWindow 使用反射 | 性能较差 | 改为直接字段访问 |
| B9 | 合格范围硬编码 | 无法动态配置 | 提取为配置文件 |

---

## 九、关键文件清单

### 数据模型

| 文件 | 路径 | 职责 |
|------|------|------|
| LensShading.cs | `DeviceConfig/Isp/LensShading.cs` | LSC 数据模型、算法封装 |
| ProcessStep.cs | `DeviceConfig/Isp/ProcessStep.cs` | 抽象基类 |
| CommonConfig.cs | `DeviceConfig/Isp/CommonConfig.cs` | 公共配置 (分辨率、Bayer) |
| IspApi.cs | `DeviceConfig/Isp/IspApi.cs` | P/Invoke 声明 |

### UI

| 文件 | 路径 | 职责 |
|------|------|------|
| LscWindow.xaml | `Ui/SettingWindow/Lsc/LscWindow.xaml` | LSC 调试窗口 XAML |
| LscWindow.xaml.cs | `Ui/SettingWindow/Lsc/LscWindow.xaml.cs` | LSC 窗口代码隐藏 |
| LscWindowViewModel.cs | `Ui/SettingWindow/Lsc/LscWindowViewModel.cs` | LSC 窗口 ViewModel |
| LscIQWindow.xaml | `Ui/SettingWindow/Lsc/LscIQWindow.xaml` | IQ 分析窗口 XAML |
| LscIQWindow.xaml.cs | `Ui/SettingWindow/Lsc/LscIQWindow.xaml.cs` | IQ 窗口代码隐藏 |
| RawBufferToBitmapImageConverter.cs | `Ui/SettingWindow/Lsc/` | RAW 转图像转换器 |

### C++ 算法

| 文件 | 路径 | 职责 |
|------|------|------|
| IQ.cpp | `IspApi/source/IQ.cpp` (行 166-720) | LscCal, LscImg, LscIQ |
| Export.h | `IspApi/source/Export.h` | C 接口导出 |
| IQ.h | `IspApi/include/IQ.h` | 结构体定义 |

---

## 十、附录

### 10.1 Bayer 模式与 Polarity 映射

| Bayer 模式 | Polarity 值 | R 位置 | G 位置 | B 位置 |
|-----------|:---:|---------|--------|--------|
| RGRG (RGGB) | 0 | (0,0) | (0,1)/(1,0) | (1,1) |
| GRGR (GRBG) | 1 | (0,1) | (0,0)/(1,1) | (1,0) |
| BGBG (BGGR) | 2 | (1,1) | (0,1)/(1,0) | (0,0) |
| GBGB (GBRG) | 3 | (1,0) | (0,0)/(1,1) | (0,1) |

### 10.2 网格尺寸速查表

| 分辨率 | blockW | blockH | totalSize |
|--------|--------|--------|-----------|
| 640x480 (VGA) | 21 | 8 | 672 |
| 1280x720 (720p) | 41 | 13 | 2132 |
| 1920x1080 (1080p) | 61 | 18 | 4392 |
| 2560x1440 (2K) | 81 | 24 | 7776 |
| 3840x2160 (4K) | 121 | 35 | 16940 |

### 10.3 色彩空间转换公式

| 转换 | 公式 |
|------|------|
| BT.601 亮度 | `Y = (R*77 + G*150 + B*29) / 256` |
| sRGB 反伽马 | `x > 0.04045 ? pow((x+0.055)/1.055, 2.4) : x/12.92` |
| RGB → XYZ (D65) | `X = R*0.4124 + G*0.3576 + B*0.1805`<br>`Y = R*0.2126 + G*0.7152 + B*0.0722`<br>`Z = R*0.0193 + G*0.1192 + B*0.9505` |
| 双线性插值 | `f(x,y) = f(Q11)*(x2-x)*(y2-y) + f(Q21)*(x-x1)*(y2-y) + f(Q12)*(x2-x)*(y-y1) + f(Q22)*(x-x1)*(y-y1)` |

---

**文档版本**: v1.0  
**创建日期**: 2026年4月8日  
**分析依据**: 项目源码完整阅读与交叉引用  
**文档状态**: 完整

---

**文档结束**

本文档基于 ThunderSE 项目实际代码深入分析生成，涵盖 LSC 模块的完整参数定义、C++ 算法原理 (LscCal/LscImg/LscIQ)、数据序列化规范、UI 交互设计 (LscWindow/LscIQWindow) 和设备通信协议。可作为 LSC 模块开发、调试、测试和维护的参考文档。
