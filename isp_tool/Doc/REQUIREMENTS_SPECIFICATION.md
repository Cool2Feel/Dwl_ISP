# ThunderSE ISP 调试工具 - 需求开发文档

## 文档信息

| 项目 | 内容 |
|------|------|
| **项目名称** | ThunderSE (ISP 调试与配置工具) |
| **文档版本** | v1.0 |
| **创建日期** | 2026年4月8日 |
| **目标平台** | Windows 桌面 (x86/Win32) |
| **技术栈** | C# WPF (.NET 4.8) + C++ DLL |

---

## 一、项目概述

### 1.1 项目背景

ThunderSE 是一款专业的 **ISP (Image Signal Processor) 调试与配置工具**，主要用于相机/镜头模组 ISP 参数的调试、校准和烧录。该工具服务于两个核心场景：

1. **研发调试阶段**：ISP 算法工程师通过图形化工具调试 ISP 参数，基于 RAW 图像数据计算和校准各项参数
2. **产线烧录阶段**：产线/测试人员通过简化的界面将调试好的参数烧录到设备或保存为配置文件

### 1.2 目标用户

| 用户角色 | 使用场景 | 对应模式 |
|---------|---------|---------|
| **ISP 算法工程师** | 参数校准、IQ 分析、RAW 数据处理 | 开发者模式 |
| **产线测试人员** | 参数验证、设备烧录、配置管理 | 用户模式 |

### 1.3 系统架构

```
┌─────────────────────────────────────────────────────────────────┐
│                     C# WPF 应用层 (ThunderSE)                    │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐  │
│  │  开发者模式   │  │  用户模式     │  │  配置管理层           │  │
│  │ (调试/校准)   │  │ (验证/烧录)   │  │ (在线/离线配置管理)   │  │
│  └──────┬───────┘  └──────┬───────┘  └──────────┬───────────┘  │
└─────────┼─────────────────┼──────────────────────┼──────────────┘
          │                 │                      │
          ▼                 ▼                      ▼
┌─────────────────────────────────────────────────────────────────┐
│                      C++ DLL 层                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐  │
│  │  Device.dll  │  │  IspApi.dll  │  │  Uvc.dll (FFmpeg)    │  │
│  │ (设备通信)    │  │ (ISP算法)    │  │ (视频采集/处理)       │  │
│  └──────┬───────┘  └──────┬───────┘  └──────────┬───────────┘  │
└─────────┼─────────────────┼──────────────────────┼──────────────┘
          │                 │                      │
          ▼                 ▼                      ▼
┌─────────────────────────────────────────────────────────────────┐
│                     硬件/外部接口层                               │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐  │
│  │  AX327X 设备 │  │  RAW 文件     │  │  UVC 视频设备         │  │
│  │ (USB 通信)    │  │ (离线数据)    │  │ (实时预览)           │  │
│  └──────────────┘  └──────────────┘  └──────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 二、功能需求

### 2.1 设备管理

#### 2.1.1 设备发现和连接

**需求编号**: DM-001  
**优先级**: 高

**功能描述**:
- 系统自动检测 AX327X 设备的插入和拔出（热插拔）
- 支持主动扫描已连接设备
- 自动枚举设备位置和 UVC 视频接口

**业务流程**:
```
1. 应用启动 → 初始化设备管理器
2. 注册 Windows WM_DEVICECHANGE 消息监听
3. 监听两类设备:
   - KSCATEGORY_CAPTURE (视频捕获设备)
   - GUID_DEVINTERFACE_DISK (磁盘设备 - AX327X)
4. 设备插入时:
   - 检查硬件 ID 是否匹配支持的设备列表
   - 创建设备实例 (AX327X)
   - 打开 USB 设备句柄
   - 初始化调试参数
   - 触发设备连接事件
5. 设备拔出时:
   - 释放设备资源
   - 断开 UVC 连接
   - 从配置列表移除
```

**支持的设备**:
| 设备标识 | 说明 |
|---------|------|
| VID_4A54 | AX327X 主设备 |
| VID_05AC&PID_12A8 | 测试设备 |

**技术实现**:
- C++ 层: `USBDeviceMonitor` 类通过隐藏窗口接收 Windows 消息
- C# 层: `DeviceManger` 单例接收事件并转发到 UI

#### 2.1.2 多设备管理

**需求编号**: DM-002  
**优先级**: 中

**功能描述**:
- 支持同时管理多个 AX327X 设备
- 每个设备有独立的配置和 UVC 连接
- 通过设备位置 (devLocation) 区分不同设备

---

### 2.2 配置管理

#### 2.2.1 配置文件管理

**需求编号**: CM-001  
**优先级**: 高

**功能描述**:
- 支持创建、打开、保存 ISP 配置文件（.isp 格式，XML）
- 配置文件包含 ISP 参数、LCD 参数和公共配置
- 支持在线配置（设备）和离线配置（文件）两种模式

**配置文件结构**:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<IspToolData>
  <Isp>
    <IspCommonConfig>
      <!-- 公共配置: 分辨率、Bayer模式、模块使能等 -->
    </IspCommonConfig>
    <AE><!-- 自动曝光 --></AE>
    <Blc><!-- 黑电平校正 --></Blc>
    <Lsc><!-- 镜头阴影校正 --></Lsc>
    <!-- ... 其他 ISP 模块 -->
  </Isp>
  <Lcd>
    <!-- LCD 配置 -->
  </Lcd>
</IspToolData>
```

**操作权限**:
| 操作 | 开发者模式 | 用户模式 |
|------|-----------|---------|
| 新建配置 | ✅ | ❌ |
| 打开文件 | ✅ | ✅ |
| 保存到文件 | ✅ | ✅ |
| 另存为 | ✅ | ✅ |

#### 2.2.2 在线配置（设备读写）

**需求编号**: CM-002  
**优先级**: 高

**功能描述**:
- 从设备读取当前所有 ISP 参数
- 将修改后的参数写入设备
- 支持实时写入（属性变更时立即写入）

**读写流程**:
```
读取流程:
1. 遍历所有 IspModule (AE, BLC, LSC, AWB, CCM, YGamma, CH, VDE, EE, SAJ, DDC)
2. 对每个模块:
   - 计算参数编码: parameter = (readPos << 8) | (DeviceModulePos * IspBitWidth)
   - 分块读取（每块最大 512 字节）
   - 调用 DeviceApi.ReadAx327XIspProperty()
   - 反序列化为模块参数对象
3. 读取 CommonConfig (分辨率、Bayer、使能状态等)

写入流程:
1. 遍历所有 IspModule
2. 检查 HasChangedParams 标志（只写入修改过的参数）
3. 分块写入（每块最大 512 字节）
4. 调用 DeviceApi.WriteAx327XIspProperty()
5. 清除 HasChangedParams 标志
```

#### 2.2.3 配置列表管理

**需求编号**: CM-003  
**优先级**: 高

**功能描述**:
- ConfigManager 单例管理所有配置（在线+离线）
- 使用并发字典存储（线程安全）
- 配置变更时触发 OnConfigListChange 事件

**配置类型**:
| 类型 | 创建方式 | 数据来源 |
|------|---------|---------|
| Online (在线) | 设备插入/扫描 | 从设备读取 |
| Offline (离线) | 手动新建/打开文件 | 从文件读取 |

---

### 2.3 ISP 参数调试

#### 2.3.1 ISP 处理管线

**需求编号**: ISP-001  
**优先级**: 高

**功能描述**:
- ISP 处理按模块顺序执行，形成管线（Pipeline）
- 每个模块可独立使能/禁用
- 模块之间有依赖关系（如 LSC 依赖 BLC）

**ISP 模块列表和处理顺序**:

```
RAW 域处理管线:
┌─────┐    ┌─────┐    ┌─────┐    ┌─────┐
│ BLC │ -> │ LSC │ -> │ AWB │ -> │ ... │
└─────┘    └─────┘    └─────┘    └─────┘
黑电平     镜头阴影    自动白平衡
校正       校正

RGB/YUV 域处理管线:
┌────────┐    ┌────┐    ┌────┐    ┌────┐    ┌────┐
│YGamma  │ -> │CCM │ -> │ CH │ -> │ VDE│ -> │ EE │
└────────┘    └────┘    └────┘    └────┘    └────┘
Gamma校正   颜色矩阵   色彩增强  动态增强  边缘增强
```

**模块依赖关系**:

| 模块 | 前置依赖 | 可处理 RAW | 可处理 RGB | 说明 |
|------|---------|-----------|-----------|------|
| AE | 无 | ❌ | ❌ | 仅参数配置 |
| BLC | 无 | ✅ | ❌ | RAW 域校正 |
| LSC | BLC | ✅ | ❌ | 依赖 BLC 校正后数据 |
| DDC | 无 | ❌ | ❌ | 仅参数配置 |
| AWB | BLC, LSC | ✅ | ❌ | 统计 RAW 域数据 |
| CCM | 无 | ❌ | ❌ | RGB 域矩阵 |
| YGamma | BLC, LSC, AWB | ❌ | ✅ | 亮度校正 |
| CH | 无 | ❌ | ❌ | 色彩增强 |
| VDE | 无 | ❌ | ❌ | 视频动态增强 |
| EE | 无 | ❌ | ❌ | 边缘增强 |
| SAJ | 无 | ❌ | ❌ | 抗锯齿 |

#### 2.3.2 公共配置 (CommonConfig)

**需求编号**: ISP-002  
**优先级**: 高

**功能描述**:
- 管理所有 ISP 模块共享的硬件参数
- 包含设备信息、分辨率、Bayer 模式、时钟、电压等
- 属性变更时实时写入设备

**参数列表**:

| 参数类别 | 参数名 | 类型 | 说明 |
|---------|--------|------|------|
| **设备信息** | Name | string | 设备名称 (20字符) |
| | Id | int | 设备ID |
| | Type | byte | 类型 |
| **分辨率** | ResolutionWidth | int | 宽度 (默认1280) |
| | ResolutionHeight | int | 高度 (默认720) |
| **图像格式** | Bayer | BayerMode | Bayer排列 (RGRG/GRGR/BGBG/GBGB) |
| | Rotate | byte | 图像旋转角度 |
| **曝光增益** | ExpGain | int | 曝光增益值 |
| | GainMax | int | 最大增益值 |
| | IsExpGainEnable | bool | 曝光增益使能 |
| **时钟** | Mclk | int | 主时钟频率 |
| | Pclk | int | 像素时钟 |
| | IsPclkFirEn | int | PCLK FIR 滤波使能 |
| | PclkFirClass | byte | PCLK FIR 滤波级别 |
| | IsPclkInvEn | bool | PCLK 反转使能 |
| | Fps | byte | 帧率 |
| | Frequency | byte | 频率 |
| | DownFpsMode | int | 降帧率模式 |
| **电压** | AVDD | byte | 模拟电压 |
| | DVDD | byte | 数字电压 |
| | VDDIO | byte | IO电压 |
| **接口** | CsiTun | byte | CSI通道 |
| | Hsyn | byte | 水平同步 |
| | Vsyn | byte | 垂直同步 |
| | Vlen | int | 垂直长度 |
| **模块使能** | ProcessorStepsEnables | ObservableCollection | 14个ISP模块使能状态 |

**模块使能映射**:

| 模块 | 使能时写入值 |
|------|:---:|
| BLC | 0x01 |
| LSC | 0x01 |
| DDC | 0x02 |
| AWB | 0x02 |
| CCM | 0x02 |
| YGamma | 0x02 |
| CH | 0x02 |
| VDE | 0x02 |
| EE | 0x02 |
| SAJ | 0x02 |

#### 2.3.3 BLC (黑电平校正)

**需求编号**: ISP-003  
**优先级**: 高

**功能描述**:
- 校正传感器固有的暗电流偏移
- 每个 Bayer 通道（R、Gr、Gb、B）有独立的校正值
- 支持从 RAW 文件自动计算黑电平值

**参数**:

| 参数 | 类型 | 范围 | 说明 |
|------|------|------|------|
| BlcR | short | 0-1023 | R通道校正值 |
| BlcGr | short | 0-1023 | Gr通道校正值 |
| BlcGb | short | 0-1023 | Gb通道校正值 |
| BlcB | short | 0-1023 | B通道校正值 |

**操作流程**:
```
1. 开发者打开 BlcWindow
2. 选择 RAW 文件（.raw 格式）
3. 系统异步读取 RAW 文件
4. 调用 IspApi.BlcCal() 计算各通道黑电平值
5. 显示4通道的像素分布图（面积图）
6. 显示平均值和中值统计
7. 选择校正方式（中值/平均值）
8. 点击"应用"，将计算结果写入配置
9. (可选) 使用校正后的 buffer 重新计算并验证
```

**C++ 算法接口**:
| 函数 | 功能 |
|------|------|
| BlcCal() | 从 RAW 图像计算黑电平校正值 |
| BlcImg() | 对 RAW 图像应用黑电平校正 |

#### 2.3.4 LSC (镜头阴影校正)

**需求编号**: ISP-004  
**优先级**: 高

**功能描述**:
- 补偿镜头光学特性导致的图像四角亮度衰减
- 将图像划分为网格，每个网格点为 Bayer 四通道分别计算增益系数
- 支持自动计算和 IQ 质量评估

**参数**:

| 参数 | 类型 | 说明 |
|------|------|------|
| CorrectionData | short[] | 网格校正系数数组 |
| LscMode | enum | 校正模式 (Y通道 / RGB) |

**网格尺寸计算**:
```
blockSizeX = 16, blockSizeY = 32
blockW = (width/2 + blockSizeX - 1) / blockSizeX + 1
blockH = (height/2 + blockSizeY - 1) / blockSizeY + 1
数组大小 = 4 * blockH * blockW
默认值 = 256 (表示 1.0x 增益，无校正)
```

**操作流程**:
```
1. 开发者打开 LscWindow
2. 加载 RAW 文件
3. 在原图上点击选择 LSC 中心点坐标
4. 选择 LSC 模式 (Y 或 RGB)
5. 点击"计算Lsc"，调用 IspApi.LscCal()
6. 处理后的图像自动显示在"lsc效果" Tab
7. 点击"查看IQ"，打开 LscIQWindow 查看质量指标
8. (可选) 点击"查看先行步骤"，查看 BLC 处理结果
```

**C++ 算法接口**:
| 函数 | 功能 |
|------|------|
| LscCal() | 从 RAW 图像计算网格校正值 |
| LscImg() | 对 RAW 图像应用镜头阴影校正 |
| LscIQ() | IQ 质量评估，返回 ColorShadingIQResult 和 LensShadingIQResult |

#### 2.3.5 AWB (自动白平衡)

**需求编号**: ISP-005  
**优先级**: 高

**功能描述**:
- 根据光源色温自动调整图像的 R/B 增益
- 支持 RAW 域和 YUV 域两种统计模式
- 支持分段增益表（3x8=24段）

**参数**:

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| seg_mode | int | 3 | 分段模式 (0-3) |
| rg_start | int | 170 | R/G 起始值 |
| rgmin | int | 170 | R/G 最小值 |
| rgmax | int | 440 | R/G 最大值 |
| weight_in | int | 7 | 内部权重 |
| weight_out | int | 3 | 外部权重 |
| ymin | int | 16 | Y最小值 |
| ymax | int | 192 | Y最大值 |
| yuv_mod_en | int | 0 | YUV模式使能 (0=RAW域, 1=YUV域) |
| rgain | int | - | R通道增益 |
| ggain | int | - | G通道增益 |
| bgain | int | - | B通道增益 |
| seg_gain | short[24] | - | 分段增益表 (3x8) |
| awb_tab | byte[128] | - | 白平衡统计表 |

**操作流程**:
```
1. 开发者打开 AwbWindow
2. 点击"加载Raw"，弹出 ColorblockPickingWindow
3. 在图像上选取色块区域
4. 系统计算 R/G/B gain 数据并显示在散点图
5. 可导入/导出 .ispawb 格式的作图数据文件
6. 使用贝塞尔曲线拟合 gain 数据
7. 调整 rgmin/rgmax 等参数
8. 点击"查看IQ"，分析白平衡质量
```

**C++ 算法接口**:
| 函数 | 功能 |
|------|------|
| AWBCal() | 计算白平衡增益 |
| AWBStatistic() | RAW 域白平衡统计 |
| AWBStatistic_Yuv() | YUV 域白平衡统计 |
| AWB_Gain_Soft_Cal() | 软件计算白平衡增益 |
| AWBImg() | 应用白平衡校正 |
| AWB_IQ() | 白平衡 IQ 评估 |

#### 2.3.6 CCM (颜色校正矩阵)

**需求编号**: ISP-006  
**优先级**: 高

**功能描述**:
- 3x3 颜色校正矩阵，修正传感器感光片和镜头的光谱响应偏差
- 将传感器 RGB 空间转换到标准 sRGB 空间
- 支持预设值快速切换

**参数**:

| 参数 | 类型 | 范围 | 说明 |
|------|------|------|------|
| ccm | short[9] | -512~511 | 3x3颜色校正矩阵 (行优先) |
| s41 | short | - | 偏移量参数1 |
| s42 | short | - | 偏移量参数2 |
| s43 | short | - | 偏移量参数3 |

**矩阵布局**:
```
ccm[0] ccm[1] ccm[2]    R->R  R->G  R->B
ccm[3] ccm[4] ccm[5] =  G->R  G->G  G->B
ccm[6] ccm[7] ccm[8]    B->R  B->G  B->B
```

**预设值**:
| 预设名 | 矩阵值 |
|--------|--------|
| R | [1024, 0, 0, 0, 1024, 0, 0, 0, 1024] |
| G | 同上 |
| B | 同上 |
| Y | 同上 |
| C | 同上 |
| M | 同上 |

#### 2.3.7 YGamma (亮度 Gamma 校正)

**需求编号**: ISP-007  
**优先级**: 高

**功能描述**:
- 对图像的亮度通道应用 Gamma 校正曲线（256点查找表）
- 支持图形化拖拽调整关键点，自动线性插值
- 支持导入/导出 Gamma 表文件

**参数**:

| 参数 | 类型 | 范围 | 说明 |
|------|------|------|------|
| using_ygama | short[256] | 0-1023 | Gamma校正查找表 |
| Pad_Num | int | - | 填充数 (默认1) |

**默认 Gamma 表**: 标准 Gamma 2.2 曲线（256个点，从 0x0 到 0x3ff）

**关键点 X 值** (20个):
```
{0, 1, 3, 6, 10, 16, 26, 39, 55, 71, 87, 103, 119, 135, 151, 167, 191, 223, 239, 255}
```

**操作流程**:
```
1. 开发者打开 YGammaWindow
2. 显示折线图 (X: 0-255, Y: 0-1023)
3. 拖拽关键点调整 Gamma 曲线
4. 系统自动线性插值完整的 256 点 YGammaTable
5. CollectionChanged 事件触发后更新数据
6. 可导入/导出 .txt 格式的 Gamma 表
7. 点击"计算IQ"，选择"在线IQ"或"离线IQ"
8. 在 IQ 窗口中框选区域计算质量指标
9. 选择色卡类型 (6阶/13阶)
10. 点击"显示图表"查看 Gamma 分析图表
```

**C++ 算法接口**:
| 函数 | 功能 |
|------|------|
| YGammaImg() | 对 RGB 图像应用 Gamma 校正 |
| YGAMMA_IQ() | Gamma IQ 评估 |

#### 2.3.8 其他 ISP 模块

**需求编号**: ISP-008  
**优先级**: 中

| 模块 | 名称 | 功能 | 参数数量 |
|------|------|------|---------|
| AE | 自动曝光 | 曝光控制，自适应亮度 | 14个参数 |
| DDC | 缺陷像素校正 | 检测并校正坏点 | 17个参数 |
| CH | 色彩增强 | RGB/YCM通道自适应增强 | 42个参数 |
| VDE | 视频动态增强 | 对比度/亮度/饱和度调节 | 13个参数 |
| EE | 边缘增强 | 图像锐化 | 57个参数 |
| SAJ | 抗锯齿 | 消除斜线锯齿效应 | 26个参数 |

---

### 2.4 视频流管理

#### 2.4.1 UVC 视频预览

**需求编号**: UVC-001  
**优先级**: 高

**功能描述**:
- 实时显示设备输出的视频流
- 支持 DirectShow 设备采集、RTSP 网络流、本地文件回放
- 视频数据格式为 RGB24

**支持的视频源**:
| 类型 | 路径格式 | 说明 |
|------|---------|------|
| DirectShow 设备 | "video=设备名称" | 主要模式 |
| RTSP 网络流 | "rtsp://192.168.1.1:7070/webcam" | 备用模式，5秒超时 |
| 本地文件回放 | "d:\capture.mp4" | 视频文件回放 |

**视频处理流程**:
```
C++ 层 (uvc.dll + FFmpeg):
1. OpenInput() → 打开视频源
   - avformat_open_input() (dshow/rtsp/file)
   - avformat_find_stream_info()
   - avcodec_find_decoder()
   - avcodec_open2()

2. DecodeThread → 独立解码线程
   - av_read_frame() → 读取数据包
   - avcodec_decode_video2() → 解码
   - sws_scale() → 格式转换为 RGB24
   - videoDataCallbackFunc() → 回调 C# 端

C# 层 (ThunderSE):
3. OnReceiveDataStatic() → 静态回调
   - Marshal.Copy → 复制到托管 byte[]
   - 限流检查 (MaxPacketCount = 10)
   - Dispatcher.BeginInvoke → 切换到 UI 线程

4. ProcessVideoData() → UI 线程处理
   - _dataReceive?.Invoke() → 通知所有订阅者

5. WriteableBitmap 渲染
   - Lock() → WritePixels() → AddDirtyRect() → Unlock()
   - 显示在 Image 控件
```

**视频回调类型**:
| 回调 | 功能 | 数据格式 |
|------|------|---------|
| VideoDataCallbackFunc | 每帧视频数据 | RGB24 |
| YuvDataCallbackFunc | YUV 原始数据 | YUV (当前未使用) |
| PlayStateChangeCallbackFunc | 播放状态变化 | bool isPlaying |

#### 2.4.2 视频截图

**需求编号**: UVC-002  
**优先级**: 中

**功能描述**:
- 支持截取 RAW 格式帧（设备原始数据）
- 支持截取 RGB 格式帧（当前显示帧）

**RAW 截图**:
```
1. 用户点击"截取RAW"按钮
2. 创建路径: "TestRaw/yyyy-MM-dd_HH-mm-ss-fff.RAW"
3. 调用 UvcApi.CaptureOneRawFrame(path)
4. C++ 层设置捕获标志 (InterlockedExchange)
5. DecodeThread 检测到标志，写入当前帧
6. 自动清除标志，完成截图
```

**RGB 截图**:
```
1. 用户点击"截取RGB"按钮
2. WriteableBitmap.CopyPixels() → 复制当前显示帧
3. 分离 R/G/B 通道到 short 数组
4. 用户选择保存路径 (.rgb 文件)
5. 写入 .rgb 文件: R平面 + G平面 + B平面
6. (DEBUG模式) 编码为 BMP 预览图
```

#### 2.4.3 视频录制

**需求编号**: UVC-003  
**优先级**: 低

**功能描述**:
- 支持录制视频流为 H.264 MP4 文件
- 使用 FFmpeg 编码，preset=slow, tune=zerolatency, crf=20

**录制流程**:
```
1. 播放状态变为 isPlaying=true
2. 调用 StartRecord("d:\capture.mp4")
3. C++ 层:
   - avformat_alloc_output_context2()
   - avcodec_find_encoder(H264)
   - 创建 RecordThread 编码线程
4. DecodeThread 将帧入队 (recordingFrameQueue)
5. RecordThread 从队列取帧，编码并写入文件
6. 调用 StopRecord() 结束录制
7. C++ 层写文件尾，清理资源
```

**注意**: 当前代码中录制功能接口已定义但未在业务逻辑中使用。

---

### 2.5 用户界面

#### 2.5.1 开发者模式

**需求编号**: UI-001  
**优先级**: 高

**功能描述**:
- 面向 ISP 算法工程师，提供完整的参数调试能力
- 左侧 TreeView 管理在线/离线配置
- 右侧动态展示选中的配置编辑页面

**布局结构**:
```
+------------------------------------------------------------------+
| 菜单栏: [文件] -> [打开] [新建] [退出]                              |
+------------------------------------------------------------------+
| TreeView (200px)  |  GridSplitter  |  DeviceConfigPage (动态内容)   |
| - 在线设备列表     |               |  (左侧选择的配置详情)            |
| - 离线配置文件     |               |                               |
+------------------------------------------------------------------+
```

**功能清单**:
| 功能 | 说明 |
|------|------|
| 打开配置 | 从文件加载 .isp 配置 |
| 新建配置 | 创建空白配置 |
| 配置切换 | TreeView 点击切换当前配置 |
| 参数编辑 | 全量参数，直接编辑每个字段 |
| 图形化调试 | 各模块专用窗口 (BlcWindow, LscWindow 等) |
| IQ 分析 | 完整的 IQ 窗口 (AwbIQ, LscIQ, YGammaIQ) |
| 数组编辑 | 专用 ArrayDataWindow 弹窗 |

#### 2.5.2 用户模式

**需求编号**: UI-002  
**优先级**: 高

**功能描述**:
- 面向产线测试人员，提供简化的参数验证界面
- 右侧固定 UVC 视频预览
- 三大 Tab 页：模组属性、整体效果、屏幕效果

**布局结构**:
```
+--------------------------------------------------+------------------+
| TabControl (3:2 比例)                              | UVC 预览窗口     |
| [模组属性] [整体效果] [屏幕效果]                     | (RGB24 实时画面) |
| + 对应 Tab 内容                                    |                  |
+--------------------------------------------------+------------------+
| [重新读取] [写入] [从文件读取] [保存到文件]                           |
+--------------------------------------------------+------------------+
```

**底部操作按钮**:
| 按钮 | 功能 |
|------|------|
| 重新读取 | 从设备刷新当前配置 |
| 写入 | 将当前配置写入设备 |
| 从文件读取 | 打开 .isp 文件加载 |
| 保存到文件 | 保存当前配置到文件 |

**EffectTab (整体效果)** 包含 7 个子区域:

| 子区域 | 功能 | 可调参数 |
|--------|------|---------|
| VDEArea | 整体调节 | 色饱和度(8级)、对比度、亮度系数、亮度偏移、色相 |
| AEArea | 亮度调节 | 目标亮度(8级)、最大增益 |
| EEArea | 锐化 | 锐化强度 |
| CHArea | 颜色增强 | R/G/B/Y/C/M 六通道强度 |
| SAJArea | 降色噪 | 强度(8级) |
| CCMArea | 颜色矩阵 | 3x3 CCM 矩阵、预设值 |
| DDCArea | 降噪 | 强度(8级) |

**LcdTab (屏幕效果)** 包含 6 个子区域:

| 子区域 | 功能 | 可调参数 |
|--------|------|---------|
| LcdCommonArea | 屏幕属性 | 型号、宽高 (只读) |
| LcdVdeArea | 屏幕整体调节 | 亮度、饱和度、对比度 |
| LcdGammaArea | 屏幕Gamma | 红色/绿色/蓝色 Gamma |
| LcdCcmArea | 屏幕颜色矩阵 | 3x3 CCM + 3偏移 |
| LcdSajArea | 屏幕饱和度 | 饱和度(5级) |
| LcdLsawtoothArea | 屏幕抗锯齿 | 平滑级别 |

#### 2.5.3 模式差异

**需求编号**: UI-003  
**优先级**: 高

| 维度 | 开发者模式 | 用户模式 |
|------|-----------|---------|
| **目标用户** | ISP 算法工程师 | 产线测试人员 |
| **配置管理** | TreeView 多配置管理 (在线+离线) | 单一在线设备配置 |
| **参数编辑** | 全量参数，直接编辑每个字段 | 精简参数，Slider 交互 |
| **图形化调试** | 各模块专用窗口 | 无 |
| **实时预览** | UvcView 嵌入或弹出窗口 | 右侧固定 UVC 预览 |
| **IQ 分析** | 完整的 IQ 窗口 | 无 |
| **LCD 调试** | 无 | 有 LcdTab |
| **数据可视化** | 图表 (面积图、散点图、折线图) | 仅 Slider + TextBox |
| **序列化模块** | 所有 15 个 IspModule | 子集: AE, Ddc, Ccm, Ch, Vde, Ee, Saj |

---

### 2.6 IQ 质量分析

#### 2.6.1 IQ 分析窗口

**需求编号**: IQ-001  
**优先级**: 高

**功能描述**:
- 提供图像质量评估的专用窗口
- 支持在线（实时视频）和离线（静态 RAW 文件）两种模式
- 支持框选分析区域

**IQ 窗口列表**:

| 窗口 | 模式 | 功能 |
|------|------|------|
| AwbIQWindow | 离线 | AWB 质量分析 |
| LscIQWindow | 离线 | LSC 质量分析 |
| YGammaOnlineIQWindow | 在线 | Gamma 在线质量分析 |
| YGammaOfflineIQWindow | 离线 | Gamma 离线质量分析 |
| YGammaIQChartWindow | 离线 | Gamma 图表可视化 |

**通用功能**:
- ImageWithRubberBandControl: 可框选区域的图像控件
- DataGrid: 显示分析结果 [项] [值] [范围] [是否在范围内]
- 加载 RAW 文件 / 撤销选框 / 计算

**YGamma 在线 IQ 特有功能**:
| 功能 | 说明 |
|------|------|
| 色卡选择 | 6阶/13阶灰度卡 |
| 停止计算 | 中断正在进行的计算 |
| 显示图表 | 打开 YGammaIQChartWindow 可视化结果 |

---

### 2.7 LCD 配置

#### 2.7.1 LCD 参数管理

**需求编号**: LCD-001  
**优先级**: 中

**功能描述**:
- 管理 LCD 屏幕的显示参数
- 与 ISP 参数独立，但有类似的调试流程
- 支持通过设备通信写入 LCD 参数

**LCD 模块列表**:

| 模块 | 名称 | 功能 |
|------|------|------|
| LcdCommon | 屏幕属性 | 型号、分辨率 (只读) |
| LcdVde | 整体调节 | 亮度、饱和度、对比度 |
| LcdGamma | Gamma | RGB 三通道 Gamma |
| LcdCcm | 颜色矩阵 | 3x3 CCM + 偏移 |
| LcdSaj | 饱和度 | 多级饱和度调节 |
| LcdLsawtooth | 抗锯齿 | 平滑级别 |

**写入流程**:
```
1. 遍历 LCD 模块 (VDE, Gamma, CCM, SAJ, Lsawtooth)
2. 对每个模块:
   - 计算字段偏移量 (Marshal.OffsetOf)
   - 调用 DeviceApi.WriteAx327XLcdProperty()
```

---

## 三、非功能需求

### 3.1 性能需求

| 指标 | 要求 |
|------|------|
| 视频帧率 | 支持 30fps 实时预览 |
| 参数写入延迟 | < 100ms |
| RAW 文件加载 | < 2s (1280x720) |
| UI 响应 | 操作后立即反馈，无卡顿 |

### 3.2 可靠性需求

| 场景 | 要求 |
|------|------|
| 设备断开 | 自动检测并提示，不崩溃 |
| 配置文件损坏 | 友好错误提示，不抛出未处理异常 |
| 参数越界 | 输入时限制范围 |
| 并发操作 | 配置管理线程安全 |

### 3.3 兼容性需求

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows 7/8/10/11 (x86) |
| .NET 版本 | .NET Framework 4.8 |
| 设备驱动 | USBSTOR (系统自带) |
| 配置文件 | XML 格式，可跨版本兼容 |

---

## 四、技术实现

### 4.1 项目结构

```
ThunderSE.sln
├── ThunderSE/                  # C# WPF 主程序
│   ├── IspToolApp.xaml.cs      # 应用入口
│   ├── Ui/                     # 用户界面
│   │   ├── MainWindow/         # 主窗口 (开发者/用户模式)
│   │   ├── SettingWindow/      # 设置窗口 (Blc/Lsc/Awb/Ccm/YGamma)
│   │   └── CommonCustomControl/# 自定义控件
│   ├── ViewModel/              # 视图模型
│   ├── DeviceConfig/           # 配置数据
│   │   ├── Isp/               # ISP 模块
│   │   ├── Lcd/               # LCD 模块
│   │   ├── Config.cs          # 配置读写
│   │   └── ConfigManager.cs   # 配置管理
│   ├── Device/                 # 设备通信
│   │   ├── DeviceApi.cs       # P/Invoke 声明
│   │   └── DeviceManger.cs    # 设备管理
│   └── Uvc/                    # 视频流
│       ├── UvcApi.cs          # P/Invoke 声明
│       └── UvcReceiver.cs     # 视频接收器
│
├── Device/                     # C++ 设备通信 DLL
│   ├── DeviceManager/         # 设备管理器
│   ├── Data/                  # 设备数据结构
│   ├── Usb/                   # USB 命令
│   └── Misc/                  # 导出函数
│
├── IspApi/                     # C++ ISP 算法 DLL
│   ├── source/                # 算法实现
│   └── include/               # 头文件
│
├── Uvc/                        # C++ 视频采集 DLL
│   ├── uvc.h                  # API 声明
│   └── uvc.cpp                # FFmpeg 实现
│
└── 3rd/                        # 第三方依赖
    ├── include/               # 头文件
    ├── lib/                   # 导入库
    └── dll/                   # 运行时 DLL
```

### 4.2 数据流

```
用户操作 (UI)
    │
    ▼
ViewModel (命令/属性)
    │
    ▼
Config (配置对象)
    │
    ├── 文件操作 → XML 序列化/反序列化
    │
    └── 设备操作 → DeviceApi → Device.dll
                      │
                      ▼
                  USB 命令 → AX327X 设备
```

### 4.3 关键设计模式

| 模式 | 应用位置 |
|------|---------|
| 单例 | ConfigManager, DeviceManger, UvcReceiver |
| MVVM | 所有 UI 和 ViewModel 分离 |
| 观察者 | PropertyChanged, 设备事件, 视频回调 |
| 策略 | ISP 模块处理管线 |
| 工厂 | 配置创建 |

### 4.4 P/Invoke 调用约定

| DLL | 调用约定 | 字符集 |
|-----|---------|--------|
| Device.dll | Cdecl | Unicode |
| IspApi.dll | Cdecl | - |
| Uvc.dll | Cdecl | ANSI (LPStr) |
| 回调委托 | StdCall | - |

---

## 五、典型用户操作流程

### 5.1 场景：开发者调试 LSC

```
1. 启动应用，进入开发者模式
2. TreeView 选择要调试的配置
3. 在 DeviceConfigPage 找到 LSC 模块
4. 点击"使用图示进行设置..."超链接
5. 打开 LscWindow
6. 点击"加载Raw文件"，选择 .raw 文件
7. 等待文件加载完成
8. 在"原图" Tab 上点击选择 LSC 中心点
9. 下拉选择 LSC 模式 (RGB)
10. 点击"计算Lsc"
11. 切换到"lsc效果" Tab 查看处理结果
12. 点击"查看IQ"，打开 LscIQWindow 分析质量
13. 如需查看 BLC 结果，点击"查看先行步骤"
14. 确认结果后，返回 DeviceConfigPage
15. 点击"保存"或"写入"应用配置
```

### 5.2 场景：用户模式验证 ISP 效果

```
1. 启动应用，进入用户模式
2. 系统自动扫描并连接在线设备
3. UVC 预览窗口显示实时视频流
4. 在 EffectTab 调整各模块参数:
   - VDE: 调整饱和度、对比度
   - AE: 调整目标亮度
   - EE: 调整锐化强度
   - CH: 调整颜色增强
5. 右侧 UVC 预览实时观察效果变化
6. 在 LcdTab 调整屏幕效果参数
7. 点击"写入"将配置烧录到设备
8. 点击"保存到文件"备份配置
```

### 5.3 场景：开发者调试 YGamma

```
1. 进入开发者模式，选择配置
2. 点击 YGamma 模块"使用图示进行设置..."
3. 打开 YGammaWindow
4. 在折线图上拖拽关键点调整 Gamma 曲线
5. 系统自动线性插值完整 256 点表
6. 可导入/导出 .txt 格式的 Gamma 表
7. 点击"计算IQ" -> 选择"在线IQ"
8. 在 IQ 窗口中框选区域计算质量指标
9. 选择色卡类型 (6阶/13阶)
10. 点击"显示图表"查看 Gamma 分析图表
11. 确认满意后，返回主界面保存配置
```

---

## 六、配置数据规范

### 6.1 配置文件格式 (.isp)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<IspToolData>
  <Isp>
    <IspCommonConfig>
      <Bayer>RGRG</Bayer>
      <ResolutionWidth>1280</ResolutionWidth>
      <ResolutionHeight>720</ResolutionHeight>
      <ProcessorStepsEnables>
        <Blc>true</Blc>
        <Lsc>true</Lsc>
        <Awb>true</Awb>
        <Ccm>true</Ccm>
        <YGamma>true</YGamma>
        <Ch>true</Ch>
        <Vde>true</Vde>
        <Ee>true</Ee>
        <Saj>true</Saj>
      </ProcessorStepsEnables>
    </IspCommonConfig>

    <Blc>
      <BlcR>64</BlcR>
      <BlcGr>64</BlcGr>
      <BlcGb>64</BlcGb>
      <BlcB>64</BlcB>
    </Blc>

    <Lsc>
      <Lsc_Weight>256,256,256,...,256</Lsc_Weight>
    </Lsc>

    <Awb>
      <Awb_Seg_Mode>3</Awb_Seg_Mode>
      <Awb_Weight_In>7</Awb_Weight_In>
      <Awb_Rg_Start>170</Awb_Rg_Start>
      <Awb_RgainMin>170</Awb_RgainMin>
      <Awb_RgainMax>440</Awb_RgainMax>
      <Awb_Ymin>16</Awb_Ymin>
      <Awb_Ymax>192</Awb_Ymax>
      <Awb_Yuv_En>0</Awb_Yuv_En>
      <Awb_Stat_Tab>154,154,...,86</Awb_Stat_Tab>
    </Awb>

    <CCM>
      <ccm>256,0,0,0,256,0,0,0,256</ccm>
    </CCM>

    <YGamma>
      <Global_Gamma_Table>0,141,181,...,1023</Global_Gamma_Table>
      <Pad_Num>1</Pad_Num>
    </YGamma>
  </Isp>
  <Lcd>
    <!-- LCD 配置 -->
  </Lcd>
</IspToolData>
```

### 6.2 RAW 文件格式

- 文件扩展名: `.raw`
- 数据排列: Bayer 模式 (RGRG/GRGR/BGBG/GBGB)
- 位深度: 10-bit (存储为 16-bit)
- 大小: width × height × 2 字节

### 6.3 RGB 截图文件格式 (.rgb)

- 文件扩展名: `.rgb`
- 数据排列: R 平面 + G 平面 + B 平面
- 每平面: width × height × sizeof(short) 字节
- 总大小: width × height × 3 × 2 字节

---

## 七、错误处理

### 7.1 设备通信错误

| 错误类型 | 处理方式 |
|---------|---------|
| SCSI 通信失败 | 重试 10 次，每次间隔 20ms |
| 设备断开 | 检测并提示，自动断开 UVC |
| 参数写入失败 | 弹出 MessageBox 提示错误 |

### 7.2 文件操作错误

| 错误类型 | 处理方式 |
|---------|---------|
| 文件不存在 | ArgumentException + 友好提示 |
| XML 格式错误 | NullReferenceException 防护 + 提示 |
| RAW 文件读取失败 | try-catch + MessageBox |

### 7.3 视频流错误

| 错误类型 | 处理方式 |
|---------|---------|
| 设备断开 | PlayStateChange 回调检测，尝试重连 |
| 解码失败 | 丢弃帧，不阻塞 UI |
| 帧积压 | MaxPacketCount 限流，丢弃超限帧 |

---

## 八、附录

### 8.1 术语表

| 术语 | 全称 | 说明 |
|------|------|------|
| ISP | Image Signal Processor | 图像信号处理器 |
| BLC | Black Level Correction | 黑电平校正 |
| LSC | Lens Shading Correction | 镜头阴影校正 |
| AWB | Auto White Balance | 自动白平衡 |
| CCM | Color Correction Matrix | 颜色校正矩阵 |
| Gamma | Gamma Correction | Gamma 校正 |
| IQ | Image Quality | 图像质量 |
| UVC | USB Video Class | USB 视频设备类 |
| RAW | Raw Image Data | 原始图像数据 |
| Bayer | Bayer Pattern | Bayer 色彩滤镜阵列 |

### 8.2 参考文档

| 文档 | 说明 |
|------|------|
| QWEN.md | 项目上下文文档 |
| OPTIMIZATION_REPORT.md | 项目优化报告 |
| AE_DEEP_ANALYSIS.md | AE 模块深度分析 |
| AWB_DEEP_ANALYSIS.md | AWB 模块深度分析 |
| BLC_DEEP_ANALYSIS.md | BLC 模块深度分析 |
| LSC_DEEP_ANALYSIS.md | LSC 模块深度分析 |
| GAMMA_DEEP_ANALYSIS.md | Gamma 模块深度分析 |
| CCM_DEEP_ANALYSIS.md | CCM 模块深度分析 |

### 8.3 关键文件清单

#### 核心业务逻辑

| 文件 | 路径 | 职责 |
|------|------|------|
| Config.cs | `ThunderSE/DeviceConfig/Config.cs` | 配置总控类 |
| ConfigManager.cs | `ThunderSE/DeviceConfig/ConfigManager.cs` | 配置管理单例 |
| Processor.cs | `ThunderSE/DeviceConfig/Isp/Processor.cs` | ISP 处理器 |
| CommonConfig.cs | `ThunderSE/DeviceConfig/Isp/CommonConfig.cs` | 公共配置 |
| DeviceManger.cs | `ThunderSE/Device/DeviceManger.cs` | 设备管理 |
| UvcReceiver.cs | `ThunderSE/Uvc/UvcReceiver.cs` | 视频接收器 |

#### UI 主框架

| 文件 | 路径 | 职责 |
|------|------|------|
| MainFrameForDevelop | `ThunderSE/Ui/MainWindow/` | 开发者模式主窗口 |
| MainFrameForUser | `ThunderSE/Ui/MainWindow/UserMode/` | 用户模式主窗口 |
| DeviceConfigPage | `ThunderSE/Ui/MainWindow/` | 配置编辑页 |

#### 设置窗口

| 文件 | 路径 | 职责 |
|------|------|------|
| BlcWindow | `ThunderSE/Ui/SettingWindow/Blc/` | BLC 调试窗口 |
| LscWindow | `ThunderSE/Ui/SettingWindow/Lsc/` | LSC 调试窗口 |
| AwbWindow | `ThunderSE/Ui/SettingWindow/Awb/` | AWB 调试窗口 |
| CcmWindow | `ThunderSE/Ui/SettingWindow/Ccm/` | CCM 调试窗口 |
| YGammaWindow | `ThunderSE/Ui/SettingWindow/YGamma/` | Gamma 调试窗口 |

#### C++ DLL

| 文件 | 路径 | 职责 |
|------|------|------|
| DeviceManager | `Device/DeviceManager/` | 设备管理器 |
| AX327X | `Device/Data/AX327X.cpp` | AX327X 设备实现 |
| IspApi Export | `IspApi/source/Export.h` | ISP 算法导出 |
| Uvc | `Uvc/uvc.cpp` | 视频采集 |

---

## 九、版本历史

| 版本 | 日期 | 说明 |
|------|------|------|
| v1.0 | 2026-04-08 | 初始版本，基于项目代码分析生成 |

---

**文档结束**

本文档基于 ThunderSE 项目实际代码分析生成，涵盖设备管理、配置管理、ISP 参数调试、视频流管理、用户界面、IQ 质量分析、LCD 配置等核心功能模块，以及典型用户操作流程和技术实现细节。
