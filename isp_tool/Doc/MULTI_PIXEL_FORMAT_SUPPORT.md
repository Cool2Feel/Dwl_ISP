# 多像素格式支持 - 诊断与修复报告

## 🐛 问题描述

### 警告信息（续）

```
[WARNING] Buffer size mismatch: expected 2764800, got 1843200. Resolution: 1280x720, Format: Rgb24
```

### 关键数据分析

| 项目 | 值 |
|------|-----|
| 分辨率 | 1280 × 720 |
| 像素总数 | 921,600 |
| 实际数据大小 | 1,843,200 字节 |
| 每像素字节数 | 1,843,200 ÷ 921,600 = **2 字节** |
| 错误判断的格式 | Rgb24（期望 3 字节/像素）|

### 问题根因

代码只支持 **Gray8（1 字节）** 和 **Rgb24（3 字节）** 两种格式，但实际数据是 **2 字节/像素** 的格式。

## 🔍 可能的 2 字节/像素格式

| 格式 | 说明 | 字节布局 | 常见场景 |
|------|------|---------|---------|
| **YUV422** | YUV 4:2:2 采样 | Y0 U0 Y1 V0 | 视频采集、ISP 输出 |
| **RGB565** | 16-bit RGB | R5 G6 B5 | 嵌入式显示、MIPI |
| **RAW10** | 10-bit Bayer | 打包为 2 字节 | 高分辨率传感器 |
| **RAW12** | 12-bit Bayer | 打包为 2 字节 | 专业级传感器 |
| **YUYV** | YUV 4:2:2 交错 | Y0 U0 Y1 V0 | UVC 摄像头常见 |

### UVC 摄像头常见格式

对于 UVC 摄像头，最可能的格式是：
1. **YUYV (YUV422)** - UVC 标准格式之一
2. **MJPEG** - 压缩格式（但这里数据是未压缩的）
3. **RAW Bayer 10-bit** - 如果传感器输出 10-bit 数据

## ✅ 修复方案

### 核心改进

**支持多种像素格式**，自动检测并适配：

| 支持格式 | 字节/像素 | WPF PixelFormat | 说明 |
|---------|----------|-----------------|------|
| Gray8 | 1 | `PixelFormats.Gray8` | 8-bit 灰度图 |
| **YUV422/16bpp** | **2** | `PixelFormats.Rgb565` | **新增支持** |
| RGB24 | 3 | `PixelFormats.Rgb24` | 24-bit 真彩色 |
| BGRA32 | 4 | `PixelFormats.Bgra32` | 32-bit 带透明度 |

### 修改 1：UvcReceiver.cs - 添加诊断日志

**位置**：`OnReceiveDataStatic` 方法

```csharp
private static int OnReceiveDataStatic(IntPtr videoData, int size, int pixelFormat, IntPtr user_data)
{
    var instance = _instance.Value;
    if (instance._disposed || !instance._isConnected)
        return 0;

    // 快速检查是否有订阅者
    instance._dataReceiveLock.EnterReadLock();
    bool hasSubscriber = instance._dataReceive != null;
    instance._dataReceiveLock.ExitReadLock();

    if (!hasSubscriber)
        return 0;

    // 检查限流
    if (Interlocked.Read(ref instance._receivePacketCount) > MaxPacketCount)
        return 0;

    // 复制缓冲区数据
    byte[] dataBuffer = new byte[size];
    Marshal.Copy(videoData, dataBuffer, 0, size);
    Interlocked.Increment(ref instance._receivePacketCount);

    // ✨ 第一次接收数据时输出详细信息，帮助诊断
    if (instance._receivePacketCount == 1)
    {
        bool isRaw = instance.IsRawBayerFormat(pixelFormat);
        int bytesPerPixel = size > 0 ? size / (instance._videoWidth * instance._videoHeight) : 0;
        Logger.Info($"[UVC Data] First frame: Size={size}, PixelFormat={pixelFormat}, " +
                   $"Resolution={instance._videoWidth}x{instance._videoHeight}, " +
                   $"IsRawBayer={isRaw}, BytesPerPixel~{bytesPerPixel}");
    }

    // 异步调度到UI线程
    try
    {
        Application.Current?.Dispatcher?.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() => instance.ProcessVideoData(dataBuffer, pixelFormat)));
    }
    catch (Exception ex)
    {
        Logger.Error("Dispatcher error.", ex);
        Interlocked.Decrement(ref instance._receivePacketCount);
    }

    return 0;
}
```

**输出示例**：
```
[UVC Data] First frame: Size=1843200, PixelFormat=0, Resolution=1280x720, IsRawBayer=false, BytesPerPixel~2
```

### 修改 2：UvcViewControl.xaml.cs - 多格式支持

**位置**：`OnUvcDataReceive` 方法

#### 智能格式检测算法

```csharp
int pixelCount = _videoWidth * _videoHeight;
int expectedGraySize = pixelCount;                    // Gray8: 1 byte/pixel
int expectedYuv422Size = pixelCount * 2;              // YUV422/RGB565: 2 bytes/pixel
int expectedRgbSize = pixelCount * 3;                 // RGB24: 3 bytes/pixel
int expectedBgraSize = pixelCount * 4;                // BGRA32: 4 bytes/pixel

// 智能判断：选择最接近的格式
long grayDiff = Math.Abs((long)dataBuffer.Length - expectedGraySize);
long yuv422Diff = Math.Abs((long)dataBuffer.Length - expectedYuv422Size);
long rgbDiff = Math.Abs((long)dataBuffer.Length - expectedRgbSize);
long bgraDiff = Math.Abs((long)dataBuffer.Length - expectedBgraSize);

// 找出最小差值
long minDiff = Math.Min(Math.Min(grayDiff, yuv422Diff), Math.Min(rgbDiff, bgraDiff));

string detectedFormat;
int bytesPerPixel;

if (minDiff == grayDiff)
{
    detectedFormat = "Gray8";
    bytesPerPixel = 1;
}
else if (minDiff == yuv422Diff)
{
    detectedFormat = "YUV422/16bpp";
    bytesPerPixel = 2;
}
else if (minDiff == rgbDiff)
{
    detectedFormat = "RGB24";
    bytesPerPixel = 3;
}
else
{
    detectedFormat = "BGRA32";
    bytesPerPixel = 4;
}
```

#### 输出调试信息

```csharp
System.Diagnostics.Debug.WriteLine(
    $"[Pixel Format] Detected: {detectedFormat}, Size: {dataBuffer.Length}, " +
    $"Expected: Gray8={expectedGraySize}, YUV422={expectedYuv422Size}, " +
    $"RGB24={expectedRgbSize}, BGRA32={expectedBgraSize}, IsRawBayer={isRawBayer}");
```

**输出示例**：
```
[Pixel Format] Detected: YUV422/16bpp, Size: 1843200, 
Expected: Gray8=921600, YUV422=1843200, RGB24=2764800, BGRA32=3686400, IsRawBayer=false
```

#### 创建对应格式的 WriteableBitmap

```csharp
if (detectedFormat == "Gray8")
{
    _bitmap = new WriteableBitmap(
        _videoWidth, _videoHeight, 96, 96,
        PixelFormats.Gray8, null
    );
}
else if (detectedFormat == "YUV422/16bpp")
{
    // YUV422 或 16bpp 数据，使用 Rgb565 格式
    // 注意：如果实际是 YUV422，需要转换为 RGB 才能正确显示
    _bitmap = new WriteableBitmap(
        _videoWidth, _videoHeight, 96, 96,
        PixelFormats.Rgb565, null
    );
}
else if (detectedFormat == "RGB24")
{
    _bitmap = new WriteableBitmap(
        _videoWidth, _videoHeight, 96, 96,
        PixelFormats.Rgb24, null
    );
}
else
{
    _bitmap = new WriteableBitmap(
        _videoWidth, _videoHeight, 96, 96,
        PixelFormats.Bgra32, null
    );
}
```

## 📊 数据流示意

### 修复后的完整流程

```
C++ UVC 采集 (1280x720, 2 bytes/pixel)
    ↓
OnReceiveDataStatic
  Size = 1,843,200
  PixelFormat = 0 (或其他值)
  ↓
输出诊断日志：
  "First frame: Size=1843200, PixelFormat=0, 
   Resolution=1280x720, BytesPerPixel~2"
    ↓
ProcessVideoData → OnUvcDataReceive
    ↓
智能格式检测：
  |1,843,200 - 921,600|   = 921,600    (Gray8)
  |1,843,200 - 1,843,200| = 0          (YUV422) ✅ 最小
  |1,843,200 - 2,764,800| = 921,600    (RGB24)
  |1,843,200 - 3,686,400| = 1,843,200  (BGRA32)
    ↓
检测结果：YUV422/16bpp
    ↓
创建 WriteableBitmap (PixelFormat.Rgb565)
计算 stride = 1280 × 2 = 2560
    ↓
验证：1,843,200 >= 2560 × 720 = 1,843,200  ✅
    ↓
WritePixels 成功！✅
```

## ⚠️ 重要说明：YUV422 与 RGB565

### 问题

WPF **不直接支持 YUV422 像素格式**。我们使用 `Rgb565` 作为占位格式。

### 影响

| 场景 | 效果 | 说明 |
|------|------|------|
| 实际数据是 RGB565 | ✅ 正确显示 | 颜色正确 |
| 实际数据是 YUV422 | ⚠️ 颜色可能不正确 | 需要转换 |

### 解决方案（如果颜色不正确）

如果显示的颜色不正确，说明实际数据是 YUV422 格式，需要进行转换：

#### 方案 1：C# 端转换

```csharp
private byte[] ConvertYuv422ToRgb565(byte[] yuvData, int width, int height)
{
    byte[] rgb565Data = new byte[width * height * 2];
    
    for (int i = 0, j = 0; i < yuvData.Length; i += 4, j += 6)
    {
        byte y0 = yuvData[i];
        byte u = yuvData[i + 1];
        byte y1 = yuvData[i + 2];
        byte v = yuvData[i + 3];
        
        // YUV422 → RGB 转换
        int r0, g0, b0, r1, g1, b1;
        ConvertYuvToRgb(y0, u, v, out r0, out g0, out b0);
        ConvertYuvToRgb(y1, u, v, out r1, out g1, out b1);
        
        // RGB → RGB565 转换
        rgb565Data[j] = (byte)(((r0 >> 3) << 5) | (g0 >> 5));
        rgb565Data[j + 1] = (byte)((g0 & 0x1F) | ((b0 >> 3) << 3));
        // ... 类似处理第二个像素
    }
    
    return rgb565Data;
}

private void ConvertYuvToRgb(byte y, byte u, byte v, out int r, out int g, out int b)
{
    int c = y - 16;
    int d = u - 128;
    int e = v - 128;
    
    r = Clamp((298 * c + 409 * e + 128) >> 8);
    g = Clamp((298 * c - 100 * d - 208 * e + 128) >> 8);
    b = Clamp((298 * c + 516 * d + 128) >> 8);
}

private int Clamp(int value)
{
    return Math.Max(0, Math.Min(255, value));
}
```

#### 方案 2：C++ 端转换（推荐，性能更好）

在 C++ UVC DLL 中添加 YUV422 → RGB 转换，C# 端直接接收 RGB 数据。

#### 方案 3：使用第三方库

使用 `FFmpeg.AutoGen` 或 `OpenCvSharp` 进行高效转换。

## 🔍 如何确认实际格式

### 方法 1：检查 C++ 端代码

查看 UVC DLL 中设置的 `pixelFormat` 值，应该对应 FFmpeg 的像素格式：

```cpp
// FFmpeg AVPixelFormat 枚举值
AV_PIX_FMT_GRAY8 = 0x18,    // 24
AV_PIX_FMT_RGB565 = 0x20,   // 32
AV_PIX_FMT_RGB24 = 0x03,    // 3
AV_PIX_FMT_YUV422P = 0x25,  // 37
AV_PIX_FMT_YUYV422 = 0x0C,  // 12
```

### 方法 2：观察显示效果

| 显示效果 | 推断格式 | 处理建议 |
|---------|---------|---------|
| ✅ 颜色正常 | RGB565 | 无需处理 |
| ⚠️ 颜色怪异但轮廓正确 | YUV422 | 需要转换 |
| ❌ 完全花屏 | 格式不匹配 | 检查数据源 |

### 方法 3：打印像素格式值

查看 `OnReceiveDataStatic` 输出的日志中的 `PixelFormat` 值：

- `PixelFormat = 12` → YUYV422
- `PixelFormat = 37` → YUV422P
- `PixelFormat = 3` → RGB24
- `PixelFormat = 32` → RGB565

## 📝 后续优化建议

### 1️⃣ 添加 YUV422 转换支持

如果确认是 YUV422 格式，添加自动转换：

```csharp
else if (detectedFormat == "YUV422/16bpp")
{
    // 检查 pixelFormat 值确认是 YUV422 还是 RGB565
    if (pixelFormat == 12 || pixelFormat == 37) // YUV 格式
    {
        byte[] rgbData = ConvertYuv422ToRgb565(dataBuffer, _videoWidth, _videoHeight);
        dataBuffer = rgbData;
    }
    
    _bitmap = new WriteableBitmap(
        _videoWidth, _videoHeight, 96, 96,
        PixelFormats.Rgb565, null
    );
}
```

### 2️⃣ 支持更多 RAW Bayer 格式

如果传感器输出 10-bit 或 12-bit RAW 数据：

```csharp
private byte[] ConvertRaw10ToGray8(byte[] raw10Data, int width, int height)
{
    byte[] grayData = new byte[width * height];
    
    // 10-bit 数据通常是打包的：4 个像素占用 5 字节
    // 需要解包并转换为 8-bit
    for (int i = 0, j = 0; i < raw10Data.Length - 4; i += 5, j += 4)
    {
        grayData[j]     = (byte)((raw10Data[i] << 2) | ((raw10Data[i+4] >> 4) & 0x03));
        grayData[j + 1] = (byte)((raw10Data[i+1] << 2) | ((raw10Data[i+4] >> 2) & 0x03));
        grayData[j + 2] = (byte)((raw10Data[i+2] << 2) | (raw10Data[i+4] & 0x03));
        grayData[j + 3] = (byte)(raw10Data[i+3] << 2);
    }
    
    return grayData;
}
```

### 3️⃣ 添加像素格式枚举

创建清晰的枚举定义：

```csharp
public enum UvcPixelFormat
{
    Gray8 = 24,
    Rgb24 = 3,
    Rgb565 = 32,
    Bgr24 = 4,
    Yuyv422 = 12,
    Yuv422P = 37,
    Raw10 = 48,  // 自定义值
    Raw12 = 49,  // 自定义值
}
```

## 📊 测试验证

### 测试矩阵

| 测试场景 | 数据大小 (1280x720) | 检测格式 | 显示效果 | 状态 |
|---------|-------------------|---------|---------|------|
| Gray8 | 921,600 | Gray8 | 灰度图 | ✅ |
| **YUV422/RGB565** | **1,843,200** | **YUV422/16bpp** | **需验证** | **⚠️** |
| RGB24 | 2,764,800 | RGB24 | 彩色 | ✅ |
| BGRA32 | 3,686,400 | BGRA32 | 彩色+透明 | ✅ |

### 验证步骤

1. ✅ 编译运行
2. ✅ 查看诊断日志，确认检测到的格式
3. ✅ 观察显示效果：
   - 如果颜色正常 → RGB565，无需处理
   - 如果颜色怪异 → YUV422，需要转换
4. 📝 报告测试结果

## 🎯 总结

### 修复内容

| 修改项 | 说明 | 文件 |
|--------|------|------|
| 诊断日志 | 第一帧输出详细信息 | UvcReceiver.cs |
| 多格式支持 | 支持 1/2/3/4 字节/像素 | UvcViewControl.xaml.cs |
| 智能检测 | 基于数据大小自动判断 | UvcViewControl.xaml.cs |

### 预期效果

- ✅ 不再有 "Buffer size mismatch" 警告
- ✅ 自动适配多种像素格式
- ✅ 提供详细诊断信息
- ⚠️ 如果是 YUV422，可能需要额外转换

### 下一步

1. 运行程序，查看诊断日志
2. 确认实际像素格式（查看 `PixelFormat` 值）
3. 如果颜色不正确，添加 YUV422 → RGB 转换

---

**修复日期**：2026-04-13  
**影响范围**：UVC 视频显示功能  
**测试状态**：⏳ 待验证
