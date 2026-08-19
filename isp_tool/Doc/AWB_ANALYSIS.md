# AWB (自动白平衡) 模块功能分析

## 一、模块概述

**AWB (Auto White Balance / 自动白平衡)** 用于校正图像在不同光源下的色偏问题。由于不同光源（日光、白炽灯、荧光灯等）的光谱成分不同，白色物体在非标准光源下会呈现偏色。AWB 模块的作用是自动检测场景光源，并调整 R/B 通道增益，使白色物体在各种光源下都能还原为真正的白色（R=G=B）。

---

## 二、整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                         UI 层 (WPF)                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  │
│  │ AwbWindow    │  │ AwbIQWindow  │  │ColorblockPicking │  │
│  │ (统计曲线调试)│  │ (IQ评估)     │  │Window (色块选取)  │  │
│  └──────┬───────┘  └──────┬───────┘  └────────┬─────────┘  │
│         │                 │                    │             │
│  ┌──────▼────────────────▼────────────────────▼─────────┐  │
│  │              AwbWindowViewModel                       │  │
│  └──────────────────────┬───────────────────────────────┘  │
└─────────────────────────┼──────────────────────────────────┘
                          │
┌─────────────────────────▼──────────────────────────────────┐
│                    数据模型层 (C#)                          │
│  ┌──────────────────────────────────────────────────────┐  │
│  │            AutoWhiteBalance.cs                        │  │
│  │  - 参数属性 (Seg_Mode, RGainStart/Min/Max, 阈值等)    │  │
│  │  - CalcGainValue()   ← 调用底层统计+增益计算          │  │
│  │  - ProcessRawBuffer() ← 调用 AWBImg 应用增益          │  │
│  │  - CalcIQ()           ← 调用 AWB_IQ 评估              │  │
│  │  - 序列化/反序列化 (XML / 二进制)                      │  │
│  └──────────────────────────────────────────────────────┘  │
│                          │                                  │
│  ┌───────────────────────▼─────────────────────────────┐   │
│  │              IspApi.cs (P/Invoke)                    │   │
│  │  [DllImport("IspApi.dll")]                           │   │
│  │  AWBCal, AWBStatistic, AWBImg, AWB_IQ, ...          │   │
│  └───────────────────────┬─────────────────────────────┘   │
└──────────────────────────┼─────────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────────┐
│                   算法层 (C++ IspApi.dll)                   │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  IQ.cpp                                              │   │
│  │  ┌─────────────────────────────────────────────┐    │   │
│  │  │ AWBCal()      - 从色块区域计算 R/B 增益      │    │   │
│  │  │ AWBStatistic() - RAW 域白平衡统计 (RGB阈值)  │    │   │
│  │  │ AWBStatistic_Yuv() - YUV域白平衡统计         │    │   │
│  │  │ AWB_Gain_Soft_Cal() - 软件增益计算           │    │   │
│  │  │ AWBImg()        - 应用增益到图像             │    │   │
│  │  │ AWB_IQ()        - IQ 评估 (RGB域)            │    │   │
│  │  └─────────────────────────────────────────────┘    │   │
│  └─────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  IQ_config.cpp                                       │   │
│  │  - IqConfig(): 初始化 AWB 默认参数                    │   │
│  │  - get_awb_stat_tab(): 生成统计阈值表                 │   │
│  │  - AllocImgBuff/FreeImgBuff: awb_img 内存管理        │   │
│  └─────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  IQ.h / Export.h                                     │   │
│  │  - awb_rect 结构体 (6个色块矩形)                      │   │
│  │  - iq_config 结构体 (含大量 awb_* 字段)               │   │
│  │  - ISP_API 函数声明                                  │   │
│  └─────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
```

---

## 三、核心功能详解

### 1. **数据模型层** (`AutoWhiteBalance.cs`)

#### 1.1 核心参数

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Seg_Mode` | int | 3 | 亮度分段模式 (2^3=8 段) |
| `RGainStart` | int | 170 | R 增益统计起始值 |
| `RGainMin` | int | 170 | R 增益最小阈值 |
| `RGainMax` | int | 440 | R 增益最大阈值 |
| `Awb_YMin` | int | 16 | 亮度统计下限 |
| `Awb_YMax` | int | 192 | 亮度统计上限 |
| `Awb_Weight_In` | int | 7 | 内边界权重 |
| `Awb_Weight_Out` | int | 3 | 外边界权重 |
| `Awb_Stat_Tab` | byte[128] | (预设曲线) | 4条32点阈值曲线 |
| `Awb_Yuv_Mod_En` | int | 0 | YUV 模式开关 |
| `Awb_Cb_Th/Cr_Th` | int[8] | (预设) | YCbCr 色度阈值（每段一个） |
| `Awb_De_High_Red/Blue_Class` | int | 3 | 高光保护级别 |
| `Awb_De_High_Red/Blue_Rate` | int | 0 | 高光保护速率 |

#### 1.2 核心方法

| 方法 | 功能 |
|------|------|
| `CalcGainValue()` | 调用 `AWBStatistic` + `AWB_Gain_Soft_Cal` 计算 R/B 增益 |
| `ProcessRawBuffer()` | 调用 `AWBImg()` 对 RAW 图像应用白平衡校正 |
| `CalcIQ()` | 调用 `AWB_IQ()` 评估白平衡效果 |
| `UpdateAwbStatTab()` | 将 UI 图表数据同步到 `Awb_Stat_Tab` |
| `LoadChartDataFile()` | 从 XML 文件加载统计曲线和增益数据 |
| `SaveChartDataFile()` | 将统计曲线和增益数据保存到 XML 文件 |

---

### 2. **算法层** (`IQ.cpp`)

#### 2.1 `AWBCal()` — 色块增益计算（手动标定模式）

**功能**：在用户框选的白色/灰色色块区域内，统计 R/G/B 平均值，计算 R 增益和 B 增益。

**工作流程**：

```cpp
void AWBCal(..., unsigned int *x, unsigned int *y, 
            unsigned int *width, unsigned int *height, 
            int &bgain, int &rgain) {
    
    // 1. 遍历最多 6 个色块区域
    for (k = 0; k < 6; k++) {
        if (width[k] == 0) break;
        
        // 2. 在色块区域内累加 R/G/B 值
        for (i = 0; i < h; i++) {
            for (j = 0; j < w; j++) {
                if (在色块矩形内) {
                    tmp = (i%2)*2 + (j%2);  // Bayer 位置
                    if (polarity == 0 || 2) {
                        if (tmp==0) sum_r += pixel;      // R
                        if (tmp==1||tmp==2) sum_g += pixel; // G
                        if (tmp==3) sum_b += pixel;      // B
                    } else { /* 相反极性 */ }
                }
            }
        }
    }
    
    // 3. 计算平均值
    num = Σ(色块面积);
    avg_r = sum_r / (num/4);   // R 占 1/4 像素
    avg_g = sum_g / (num/2);   // G 占 1/2 像素
    avg_b = sum_b / (num/4);   // B 占 1/4 像素
    
    // 4. 计算增益 (以 G 为参考)
    rgain = CLIP(avg_g / avg_r * 256, 0, 1023);
    bgain = CLIP(avg_g / avg_b * 256, 0, 1023);
}
```

**关键点**：
- 增益以 **256 为基准**（1.0x 增益 = 256）
- 增益公式：`gain = G_avg / R_avg * 256`（使 R 通道增益后等于 G）
- 支持最多 6 个色块，适用于多点标定
- 对于极性 2/3，交换 R/B 平均值

---

#### 2.2 `AWBStatistic()` — RAW 域白平衡统计（自动统计模式）

**功能**：按亮度分段统计白色像素，使用 RGB 色彩空间判定白色。

**工作流程**：

```cpp
void AWBStatistic(..., int seg_mode, unsigned char *awb_stat_tab, ...) {
    segs = 1 << seg_mode;  // 2^3 = 8 段
    
    for (n = 0; n < h; n += 2) {      // 每 2 行
        for (m = 0; m < w; m += 2) {  // 每 2 列 (2x2 Bayer 块)
            
            // 1. 提取 R/G/B (转为 8-bit)
            r = pixel_R >> 2;  // 10-bit → 8-bit
            g = (pixel_Gr + pixel_Gb) >> 3;
            b = pixel_B >> 2;
            
            // 2. 计算亮度
            y = (r*77 + g*150 + b*29) / 256;
            
            // 3. 亮度过滤
            if (y < ymin || y > ymax) continue;
            
            // 4. 确定亮度分段
            segk = y >> (8 - seg_mode);  // 8-bit / 2^(8-3) = 每段 32 级
            
            // 5. 计算 R 增益
            rgain = g * 256 / r;
            if (rgain < rgmin || rgain > rgmax) continue;
            
            // 6. 从 awb_stat_tab 插值 B 增益边界
            rgain_num = (rgain - rg_start) / 16;
            rgain_mod = (rgain - rg_start) % 16;
            
            // 线性插值 4 条边界曲线 (每条 32 点)
            bgain_out_high = lerp(tab[rgain_num], tab[rgain_num+1], mod) / 4;
            bgain_in_high  = lerp(tab[32+rgain_num], tab[33+rgain_num], mod) / 4;
            bgain_in_low   = lerp(tab[64+rgain_num], tab[65+rgain_num], mod) / 4;
            bgain_out_low  = lerp(tab[96+rgain_num], tab[97+rgain_num], mod) / 4;
            
            // 7. 判定 G 是否在 B 增益边界内
            bound_out_low = bgain_out_low * b / 256;
            bound_out_high = bgain_out_high * b / 256;
            if (g >= bound_out_low && g <= bound_out_high)
                weight = weight_out + 1;  // 外边界权重低
            
            bound_in_low = bgain_in_low * b / 256;
            bound_in_high = bgain_in_high * b / 256;
            if (g >= bound_in_low && g <= bound_in_high)
                weight = weight_in + 1;   // 内边界权重高
            
            // 8. 累加到对应分段
            wp_output[segk*4]   += weight;      // 像素计数
            wp_output[segk*4+1] += r * weight;  // R 累加
            wp_output[segk*4+2] += g * weight;  // G 累加
            wp_output[segk*4+3] += b * weight;  // B 累加
        }
    }
}
```

**关键设计**：
- **awb_stat_tab**：128 字节，定义 4 条 32 点的 B 增益边界曲线
  - 曲线 0 (偏移 0)：外边界高
  - 曲线 1 (偏移 32)：内边界高
  - 曲线 2 (偏移 64)：内边界低
  - 曲线 3 (偏移 96)：外边界低
- **白色判定**：R 增益在 [rgmin, rgmax] 范围内，且 G 在 B 增益边界内的像素视为白色
- **分级加权**：内边界权重 (7) > 外边界权重 (3)

---

#### 2.3 `AWBStatistic_Yuv()` — YUV 域白平衡统计

**功能**：基于 YCbCr 色度空间的白色像素判定。

**工作流程**：

```cpp
void AWBStatistic_Yuv(...) {
    for (n = 0; n < h; n += 2) {
        for (m = 0; m < w; m += 2) {
            // 1. 提取 R/G/B 和亮度 Y
            r, g, b, y = ...;
            
            if (y < ymin || y > ymax) continue;
            segk = y >> (8 - seg_mode);
            
            // 2. 转换到 YCbCr 色度空间
            cb = (-r*43 - g*85 + b*128) / 256;
            cr = (r*128 - g*107 - b*21) / 256;
            
            // 3. 白色判定：Cb/Cr 接近 0（无色度）
            if (abs(cb) < cb_th[segk] && abs(cr) < cr_th[segk]) {
                if (abs(cb)+abs(cr) < cbcr_th[segk] && 
                    y > abs(cb)+abs(cr) + ycbcr_th) {
                    weight = 1;
                }
            }
            
            // 4. 累加
            wp_output[segk*4]   += weight;
            wp_output[segk*4+1] += r * weight;
            // ...
        }
    }
}
```

**与 RGB 模式的区别**：
- 使用 **YCbCr 色度空间**判定白色（无色度 = 白色/灰色）
- 每段有独立的 Cb/Cr 阈值
- 实现更简单，权重固定为 1

---

#### 2.4 `AWB_Gain_Soft_Cal()` — 软件增益计算

**功能**：对各亮度分段的统计结果加权平均，计算最终的 R/B 增益。

**工作流程**：

```cpp
void AWB_Gain_Soft_Cal(int *wp_input, int awb_seg_mode, 
                       int* r_gain, int* b_gain, int* g_gain) {
    segs = 1 << awb_seg_mode;  // 8 段
    seg_k_weight[8] = { 24, 32, 36, 36, 36, 36, 36, 24 };  // 中间权重高
    
    for (i = 0; i < segs; i++) {
        // 像素太少则忽略
        if (wp_input[i*4] < (2048*8/segs)) {
            k_weight = 0;
        } else {
            k_weight = seg_k_weight[i];
            rgain += G_seg * k_weight * 256 / R_seg;  // G/R 增益
            bgain += G_seg * k_weight * 256 / B_seg;  // G/B 增益
        }
        k_weight_all += k_weight;
    }
    
    if (k_weight_all == 0) {
        *r_gain = 256;  // 默认无增益
        *b_gain = 256;
    } else {
        *r_gain = CLIP(rgain / k_weight_all, 0, 1023);
        *b_gain = CLIP(bgain / k_weight_all, 0, 1023);
    }
    *g_gain = 256;  // G 始终为 1.0x
}
```

**关键设计**：
- **中间亮度权重更高**：seg 2-5 权重 36，seg 0/7 权重 24
- **忽略低置信度分段**：像素数 < 2048 的分段不参与计算
- **防止除零**：如果所有分段都被忽略，返回默认增益 256

---

#### 2.5 `AWBImg()` — 应用 AWB 增益到图像

**功能**：对每个像素根据其 Bayer 通道位置应用对应的增益，包含高光保护机制。

**工作流程**：

```cpp
void AWBImg(..., int* gain_values, 
            int awb_de_high_red_class, int awb_de_high_blue_class,
            int awb_de_high_red_rate, int awb_de_high_blue_rate, ...) {
    
    r_gain = gain_values[0];
    g_gain = gain_values[1];
    b_gain = gain_values[2];
    
    // 高光阈值
    awb_de_high_red_th = 1023 - (1 << (6 + awb_de_high_red_class));
    awb_de_high_blue_th = 1023 - (1 << (6 + awb_de_high_blue_class));
    
    for (n = 0; n < h; n++) {
        for (m = 0; m < w; m++) {
            chanel_num = 2*(n%2) + (m%2);  // 0=R, 1=Gr, 2=Gb, 3=B
            
            if (chanel_num == chanel_num_b) {
                gain = b_gain;
                
                // 高光保护：当 B 增益 < 256 且像素值很高时
                if (b_gain < 256 && awb_de_high_red_class > 0 && 
                    pixel > awb_de_high_red_th) {
                    
                    // 计算混合比例
                    rate = (1023 - pixel) * 256 + (pixel - th) * rate_param;
                    rate = rate >> (8 + class_param);
                    rate = CLIP(rate, 0, 255);
                    
                    // 混合：增益 → 256 (无增益)
                    gain = (b_gain * rate + 256 * (256 - rate)) >> 8;
                }
            }
            else if (chanel_num == chanel_num_r) {
                gain = r_gain;
                // 类似的高光保护
            }
            else {
                gain = g_gain;
            }
            
            out_img[n*w + m] = CLIP(pixel * gain / 256, 0, 1023);
        }
    }
}
```

**高光保护机制**：
- 当增益 < 256（需要降低该通道亮度）且像素值接近饱和时
- 渐进式降低增益，避免高光区域过曝或色偏
- 混合公式：`gain = (orig_gain * rate + 256 * (256-rate)) / 256`
  - `rate=255` → 使用原始增益
  - `rate=0` → 使用 256（无增益）

---

#### 2.6 `AWB_IQ()` — IQ 评估

**功能**：在 demosaic 后的 RGB 图像上评估白平衡效果。

**工作流程**：

```cpp
void AWB_IQ(short **rgb_img, ..., double* rg_iq, double* bg_iq) {
    // 1. 在色块区域累加 R/G/B
    for (k = 0; k < 6; k++) {
        for (在色块内) {
            sum_r += rgb_img[0][pixel];
            sum_g += rgb_img[1][pixel];
            sum_b += rgb_img[2][pixel];
        }
    }
    
    // 2. 计算平均值
    avg_r = sum_r / num;
    avg_g = sum_g / num;
    avg_b = sum_b / num;
    
    // 3. 计算 IQ 指标
    *rg_iq = avg_g / avg_r;  // 理想值 1.0
    *bg_iq = avg_g / avg_b;  // 理想值 1.0
    
    // 4. 评估：0.92 ~ 1.08 为合格
    if (*rg_iq > 0.92 && *rg_iq < 1.08 && *bg_iq > 0.92 && *bg_iq < 1.08)
        printf("AWB is perfect!\n");
    else
        printf("AWB needs correction!\n");
}
```

**评估逻辑**：
- 理想白平衡：R=G=B，因此 `rg_iq = bg_iq = 1.0`
- 合格范围：0.92 ~ 1.08（±8% 偏差）

---

### 3. **UI 交互层** (`AwbWindow`)

#### 3.1 工作流程

1. **加载 Raw 文件**：选择一组 RAW 图像（不同场景/光源）
2. **色块选取**：在每张图像上框选白色/灰色区域
3. **计算增益**：调用 `AWBCal()` 计算每张图像的 R/B 增益
4. **调整统计曲线**：使用贝塞尔曲线绘制 4 条 B 增益边界曲线
5. **保存配置**：将曲线数据和增益数据保存为 `.ispawb` 文件

#### 3.2 UI 布局

```
┌──────────────────────────────────────────────────────┐
│ AWB 统计曲线调试                                      │
│                                                       │
│  Y轴: B 增益                                          │
│  ┌──────────────────────────────────────┐             │
│  │  ╱╲    曲线1 (外边界高)               │             │
│  │ ╱  ╲   曲线2 (内边界高)               │             │
│  │╱    ╲  曲线3 (内边界低)               │             │
│  │      ╲ 曲线4 (外边界低)               │             │
│  └──────────────────────────────────────┘             │
│  ←─── R 增益 ───→                                      │
│                                                       │
│  [RGainStart 线]              [RGainEnd 线]           │
│  [RGainMin 线]                [RGainMax 线]           │
│                                                       │
├──────────────────────────────────────────────────────┤
│ 参数: Seg_Mode | Weight_In | Weight_Out               │
│       YMin | YMax | De_High_Red_Class | ...           │
│                                                       │
│ [加载Raw] [加载曲线] [保存曲线] [更新StatTab] [查看IQ] │
└──────────────────────────────────────────────────────┘
```

#### 3.3 贝塞尔曲线交互

- **添加曲线**：最多 4 条贝塞尔曲线，对应 4 条 B 增益边界
- **拖拽控制点**：每条曲线有 4 个控制点（起点、终点、2 个贝塞尔点）
- **平移/缩放**：右键拖拽平移 Y 轴，滚轮缩放 Y 轴
- **拖拽 RGainStart 线**：整体平移所有曲线的 X 轴起始位置
- **投影到图表**：贝塞尔曲线自动转换为 32 点数据，同步到 `StatisticData`

---

### 4. **色块选取窗口** (`ColorblockPickingWindow`)

**功能**：在 Raw 图上框选白色区域，调用 `AWBCal()` 计算增益。

**工作流程**：
1. 用户选择 RAW 文件列表
2. 打开 `ColorblockPickingWindow`
3. 在图像上用鼠标拖拽框选矩形区域
4. 调用 `AWBCal()` 计算该区域的 R/B 增益
5. 将增益值保存到 `GainData` 字典（键为文件路径）

---

### 5. **IQ 评估窗口** (`AwbIQWindow`)

**功能**：在 demosaic 后的 RGB 图像上评估白平衡效果。

**工作流程**：
1. 加载 RAW 文件并 demosaic 为 RGB
2. 框选白色色块区域
3. 调用 `AWB_IQ()` 计算 `rg_iq` 和 `bg_iq`
4. 显示结果并判定是否合格（0.92~1.08）

---

## 四、完整 AWB 增益计算流程

```
┌─────────────────────────────────────────────┐
│ 模式选择                                     │
│ ┌─────────────┐  ┌──────────────────────┐   │
│ │ 手动标定模式 │  │   自动统计模式        │   │
│ └──────┬──────┘  └──────────┬───────────┘   │
│        │                    │                │
│        ▼                    ▼                │
│   AWBCal()          AWBStatistic() 或        │
│   - 框选白色色块      AWBStatistic_Yuv()     │
│   - 计算 G/R, G/B     - 遍历全图 2x2 块      │
│   - 输出 rgain/bgain  - 按亮度分段统计       │
│                          - 白色像素判定       │
│                          - 加权累加 R/G/B     │
│                               │              │
│                               ▼              │
│                        AWB_Gain_Soft_Cal()   │
│                        - 各分段加权平均       │
│                        - 中间亮度权重高       │
│                        - 输出 r_gain/b_gain   │
└───────────────────────────────┬───────────────┘
                                │
                                ▼
                          AWBImg()
                          - 逐像素应用增益
                          - 高光保护
                          - 输出校正后图像
                                │
                                ▼
                          AWB_IQ()
                          - 评估 rg_iq, bg_iq
                          - 判定是否合格
```

---

## 五、关键技术要点

### 1. 两种工作模式

| 模式 | 适用场景 | 优点 | 缺点 |
|------|----------|------|------|
| **手动标定** | 实验室环境，已知白色参考 | 精确、可控 | 需要人工操作 |
| **自动统计** | 实际场景，未知光源 | 自动化 | 可能误判非白色区域 |

### 2. 白色判定方法

| 方法 | 原理 | 适用场景 |
|------|------|----------|
| **RGB 阈值法** | R 增益在范围内，G 在 B 增益边界内 | 通用场景，可精细调参 |
| **YCbCr 色度法** | Cb/Cr 接近 0（无色度） | 简单场景，快速判定 |

### 3. 亮度分段策略

- 将 0-255 亮度范围分为 8 段（每段 32 级）
- 中间亮度（seg 2-5，约 64-191）权重更高
- 边缘亮度（seg 0/1/6/7）权重较低
- 像素数 < 2048 的分段被忽略

### 4. 高光保护机制

- 当增益 < 256（需要降低通道亮度）时
- 如果像素值接近饱和（> 阈值），渐进式降低增益
- 避免高光区域过曝或色偏
- 参数：`class`（控制阈值位置）、`rate`（控制混合速度）

### 5. 贝塞尔曲线与统计阈值表

- UI 上绘制 4 条贝塞尔曲线（外边界高/低、内边界高/低）
- 每条曲线转换为 32 个采样点
- 4 × 32 = 128 字节存储为 `Awb_Stat_Tab`
- 运行时根据 R 增益值线性插值得到 B 增益边界

---

## 六、数据流示意

```
用户框选白色色块
         │
         ▼
   AWBCal()
   ├─ 累加色块内 R/G/B
   ├─ 计算平均值
   └─ 增益 = G_avg / R_avg * 256
         │
         ▼ (或自动统计模式)
   AWBStatistic()
   ├─ 遍历 2x2 Bayer 块
   ├─ 计算 R 增益 = G * 256 / R
   ├─ 从 awb_stat_tab 插值 B 增益边界
   ├─ 判定白色像素并加权
   └─ 按亮度分段累加
         │
         ▼
   AWB_Gain_Soft_Cal()
   ├─ 各分段加权平均
   ├─ 中间亮度权重 36
   └─ 输出 r_gain, b_gain
         │
         ▼
   AWBImg()
   ├─ 根据 Bayer 位置选择增益
   ├─ 高光区域自适应保护
   └─ out = in * gain / 256
         │
         ▼
   校正后的 RAW 图像
         │
         ▼
   AWB_IQ()
   ├─ 在 demosaic 后 RGB 图上评估
   ├─ rg_iq = G_avg / R_avg
   └─ 合格范围: 0.92 ~ 1.08
```

---

## 七、相关文件清单

| 文件 | 层级 | 功能 |
|------|------|------|
| `AutoWhiteBalance.cs` | C# 数据模型 | 核心业务逻辑、参数管理、序列化 |
| `AwbWindow.xaml/.cs` | C# UI | AWB 统计曲线调试窗口（贝塞尔曲线） |
| `AwbWindowViewModel.cs` | C# ViewModel | 参数绑定、文件加载/保存 |
| `AwbIQWindow.xaml/.cs` | C# UI | AWB IQ 评估窗口 |
| `ColorblockPickingWindow.xaml/.cs` | C# UI | 色块选取窗口 |
| `CustomControls/BezierFigure.cs` | C# 控件 | 贝塞尔曲线自定义控件 |
| `CustomControls/ThumbPoint.cs` | C# 控件 | 拖拽控制点控件 |
| `IspApi.cs` | C# P/Invoke | DLL 导入声明 |
| `IQ.cpp` | C++ 算法 | AWBCal, AWBStatistic, AWBImg, AWB_IQ 实现 |
| `IQ_config.cpp` | C++ 配置 | 默认参数初始化 |
| `Export.h` / `IQ.h` | C++ 头文件 | 函数声明、结构体定义 |

---

## 八、总结

AWB 模块是 ISP 处理链中**最复杂的模块之一**，实现了完整的自动白平衡功能：

### 核心特性
✅ **双模式支持**：手动标定 + 自动统计
✅ **两种白色判定**：RGB 阈值法 + YCbCr 色度法
✅ **亮度分段加权**：中间亮度权重更高，提高准确性
✅ **高光保护机制**：防止过曝和色偏
✅ **可视化调试**：贝塞尔曲线绘制统计阈值，直观调整
✅ **IQ 评估**：量化评估白平衡效果

### 设计亮点
- **灵活的统计曲线**：通过 4 条贝塞尔曲线定义复杂的白色判定区域
- **分级加权策略**：内边界权重 > 外边界权重，提高鲁棒性
- **高光自适应**：根据像素值动态调整增益，保护高光细节
- **多点标定支持**：最多 6 个色块，适用于复杂场景

AWB 模块设计精巧，算法完整，是相机 ISP 调优的核心工具。
