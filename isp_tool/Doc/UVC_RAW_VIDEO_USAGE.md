# UVC RAW数据直接显示功能

## 功能说明

此功能允许直接接收未转码的原始视频数据（RAW格式），适用于需要直接处理原始图像格式的场景，如ISP调试。

## 支持的RAW格式

- **YUYV422** (YUV422 Packed): 2字节/像素
- **YUV420P** (Planar YUV 4:2:0): 1.5字节/像素
- **NV12/NV21** (Semi-Planar YUV 4:2:0): 1.5字节/像素
- **UYVY422** (YUV422 Packed): 2字节/像素
- **GRAY8** (8-bit Grayscale): 1字节/像素
- **RGB24/BGR24**: 3字节/像素

## 使用示例

### 1. 基本使用

```csharp
using ThunderSE.Uvc;

// 获取UVC接收器实例
var uvcReceiver = UvcReceiver.Instance;

// 订阅RAW数据接收事件
uvcReceiver.RawDataReceive += OnRawDataReceived;

// 连接摄像头
uvcReceiver.Connect("USB Camera");

// 处理RAW数据
private void OnRawDataReceived(IntPtr data, int dataSize, int pixelFormat, int width, int height)
{
    // data: 指向原始数据的指针
    // dataSize: 数据大小（字节）
    // pixelFormat: 像素格式（对应AVPixelFormat枚举）
    // width: 图像宽度
    // height: 图像高度
    
    // 示例：将数据复制到托管数组
    byte[] rawData = new byte[dataSize];
    System.Runtime.InteropServices.Marshal.Copy(data, rawData, 0, dataSize);
    
    // 在这里处理RAW数据
    ProcessRawFrame(rawData, pixelFormat, width, height);
}
```

### 2. 像素格式转换

```csharp
// AVPixelFormat枚举值（来自FFmpeg）
public enum AVPixelFormat
{
    AV_PIX_FMT_NONE = -1,
    AV_PIX_FMT_YUV420P = 0,      ///< Planar YUV 4:2:0
    AV_PIX_FMT_YUYV422 = 1,      ///< Packed YUV 4:2:2
    AV_PIX_FMT_RGB24 = 2,        ///< Packed RGB 8:8:8
    AV_PIX_FMT_BGR24 = 3,        ///< Packed BGR 8:8:8
    AV_PIX_FMT_NV12 = 23,        ///< Semi-planar YUV 4:2:0
    AV_PIX_FMT_UYVY422 = 24,     ///< Packed YUV 4:2:2
    AV_PIX_FMT_GRAY8 = 8,        ///< 8-bit grayscale
}

// 判断格式并处理
private void ProcessRawFrame(byte[] data, int pixelFormat, int width, int height)
{
    switch ((AVPixelFormat)pixelFormat)
    {
        case AVPixelFormat.AV_PIX_FMT_YUYV422:
            // YUYV422: 每个宏像素2个像素，4字节
            // Y0 U0 Y1 V0
            ProcessYuyv422(data, width, height);
            break;
            
        case AVPixelFormat.AV_PIX_FMT_YUV420P:
            // Planar YUV420: Y平面 + U平面 + V平面
            ProcessYuv420P(data, width, height);
            break;
            
        case AVPixelFormat.AV_PIX_FMT_NV12:
            // Semi-planar: Y平面 + 交错的UV平面
            ProcessNV12(data, width, height);
            break;
    }
}
```

### 3. YUYV422格式解析

```csharp
private void ProcessYuyv422(byte[] data, int width, int height)
{
    // YUYV422布局：[Y0, U0, Y1, V0, Y2, U1, Y3, V1, ...]
    // 每个宏像素（2个像素）占用4字节
    
    byte[] yChannel = new byte[width * height];
    byte[] uChannel = new byte[width * height / 2];
    byte[] vChannel = new byte[width * height / 2];
    
    int yIndex = 0, uvIndex = 0;
    
    for (int i = 0; i < data.Length; i += 4)
    {
        // 第一个像素
        yChannel[yIndex++] = data[i];     // Y0
        uChannel[uvIndex] = data[i + 1];  // U0
        
        // 第二个像素
        yChannel[yIndex++] = data[i + 2]; // Y1
        vChannel[uvIndex++] = data[i + 3]; // V0
    }
    
    // 现在可以分别处理Y、U、V通道
}
```

### 4. 与RGB显示结合

如果需要显示但不想C++端转码，可以在C#端按需转换：

```csharp
[DllImport("IspApi.dll", CallingConvention = CallingConvention.Cdecl)]
public static extern void ConvertYuvToRgb(
    IntPtr yuvData, 
    IntPtr rgbData, 
    int width, 
    int height, 
    int format);

private void OnRawDataReceived(IntPtr data, int dataSize, int pixelFormat, int width, int height)
{
    // 分配RGB缓冲区
    int rgbSize = width * height * 3;
    byte[] rgbBuffer = new byte[rgbSize];
    
    GCHandle handle = GCHandle.Alloc(rgbBuffer, GCHandleType.Pinned);
    try
    {
        IntPtr rgbPtr = handle.AddrOfPinnedObject();
        
        // 调用ISP库进行转换
        ConvertYuvToRgb(data, rgbPtr, width, height, pixelFormat);
        
        // 现在rgbBuffer包含RGB24数据，可以直接用于Bitmap显示
        DisplayRgbImage(rgbBuffer, width, height);
    }
    finally
    {
        handle.Free();
    }
}
```

## 性能优势

1. **零拷贝**: RAW数据直接从FFmpeg的AVFrame传递给C#，避免额外的转码步骤
2. **降低CPU使用**: 跳过sws_scale转换步骤，减少CPU开销
3. **降低延迟**: 减少处理步骤，降低视频延迟
4. **ISP调试友好**: 可以直接获取传感器原始数据格式

## 注意事项

1. **线程安全**: RAW数据回调在工作线程中调用，如需更新UI，请使用Dispatcher
2. **数据生命周期**: 回调返回后，原始数据可能被释放，如需保留请复制
3. **格式检测**: 连接时会打印实际的像素格式，请检查日志确认
4. **带宽**: RAW数据量较大（YUYV422: 1280x720 = ~1.8MB/帧），确保系统带宽足够

## 调试输出

C++端会输出以下调试信息：

```
Stream codecpar->format = 24 (yuyv422)
CodecContext pix_fmt = 24 (yuyv422)
CodecContext color_range = 2
```

这表明摄像头输出的是YUYV422格式，颜色范围为JPEG（全范围）。

## 与现有VideoData回调的区别

| 特性 | VideoData回调 | RawData回调 |
|------|--------------|-------------|
| 数据格式 | RGB24（已转码） | 原始格式（YUYV422/YUV420P等） |
| 转码开销 | 有（sws_scale） | 无 |
| 数据大小 | 固定（width*height*3） | 根据格式变化 |
| 适用场景 | 直接显示 | ISP调试、自定义处理 |
| 性能 | 中等 | 最优 |

## 完整示例：RAW数据保存为文件

```csharp
public void SaveRawFrame(string filePath)
{
    uvcReceiver.RawDataReceive += (data, dataSize, pixelFormat, width, height) =>
    {
        byte[] buffer = new byte[dataSize];
        Marshal.Copy(data, buffer, 0, dataSize);
        File.WriteAllBytes(filePath, buffer);
        Console.WriteLine($"Saved RAW frame: {filePath} ({dataSize} bytes, {width}x{height}, format={pixelFormat})");
    };
}
```
