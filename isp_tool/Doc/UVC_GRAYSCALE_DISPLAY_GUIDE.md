# UVC RAWVIDEO 灰度图像直接显示指南

## 📊 RAWVIDEO数据格式分析

### UVC设备输出的常见格式

| 格式 | 像素布局 | 字节/像素 | 灰度提取方式 |
|------|---------|----------|-------------|
| **GRAY8** | 纯灰度 | 1 | 直接使用 ✅ |
| **YUYV422** | Y0-U0-Y1-V0 | 2 | 提取Y分量 |
| **UYVY422** | U0-Y0-V0-Y1 | 2 | 提取Y分量 |
| **YUV420P** | Y平面 + U平面 + V平面 | 1.5 | 使用Y平面 |
| **NV12** | Y平面 + UV交错平面 | 1.5 | 使用Y平面 |
| **NV21** | Y平面 + VU交错平面 | 1.5 | 使用Y平面 |

### Y分量即灰度图

**关键原理**：YUV色彩空间中，**Y分量就是亮度（灰度）信息**，U和V是色度信息。

```
YUYV422格式示例（1280x720）：
内存布局：[Y0, U0, Y1, V0, Y2, U1, Y3, V1, ...]
          └──── 4字节表示2个像素 ────┘

提取Y分量：[Y0, Y1, Y2, Y3, ...] → 921,600字节的灰度图
```

---

## 🎯 方案1：C++端自动提取Y分量（已实现）

### C++端实现（uvc.cpp）

代码已自动处理：当设备输出YUV格式时，会自动提取Y分量并通过特殊格式码 `1000` 回调。

```cpp
// 特殊格式码：表示从YUV提取的Y分量灰度图
const int AV_PIX_FMT_GRAY8_FROM_YUV = 1000;

// YUYV422提取Y分量示例
if (inputFmt == AV_PIX_FMT_YUYV422)
{
    for (int y = 0; y < height; y++)
    {
        uint8_t* srcLine = pFrame->data[0] + y * pFrame->linesize[0];
        uint8_t* dstLine = yBuffer + y * width;
        
        for (int x = 0; x < width; x += 2)
        {
            dstLine[x]     = srcLine[x * 2];         // Y0
            dstLine[x + 1] = srcLine[x * 2 + 2];     // Y1
        }
    }
    
    // 回调灰度数据
    rawDataCallbackFunc(yBuffer, width*height, AV_PIX_FMT_GRAY8_FROM_YUV, ...);
}
```

### C#端接收并显示

```csharp
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;

public class UvcGrayDisplay
{
    private Image imageControl; // WPF Image控件
    
    public void Initialize()
    {
        var uvcReceiver = UvcReceiver.Instance;
        
        // 订阅RAW数据回调
        uvcReceiver.RawDataReceive += OnRawDataReceived;
    }
    
    private void OnRawDataReceived(IntPtr data, int dataSize, int pixelFormat, int width, int height)
    {
        // pixelFormat == 1000 表示从YUV提取的Y分量
        // pixelFormat == 8 表示原生GRAY8格式
        if (pixelFormat == 1000 || pixelFormat == 8)
        {
            DisplayGrayImage(data, dataSize, width, height);
        }
    }
    
    private void DisplayGrayImage(IntPtr grayData, int dataSize, int width, int height)
    {
        // 方法1：使用WriteableBitmap（推荐，性能好）
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
        
        // 直接复制内存（零拷贝）
        bitmap.Lock();
        CopyMemory(grayData, bitmap.BackBuffer, dataSize);
        bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        bitmap.Unlock();
        
        // 更新UI（需要从UI线程调用）
        Application.Current.Dispatcher.Invoke(() =>
        {
            imageControl.Source = bitmap;
        });
    }
    
    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void CopyMemory(IntPtr Destination, IntPtr Source, int Length);
}
```

---

## 🎯 方案2：C#端手动提取Y分量

如果需要更灵活的控制，可以在C#端手动提取：

```csharp
private void OnRawDataReceived(IntPtr data, int dataSize, int pixelFormat, int width, int height)
{
    byte[] grayData = null;
    
    switch (pixelFormat)
    {
        case 8: // AV_PIX_FMT_GRAY8
            grayData = new byte[dataSize];
            Marshal.Copy(data, grayData, 0, dataSize);
            break;
            
        case 1: // AV_PIX_FMT_YUYV422
            grayData = ExtractYFromYUYV422(data, dataSize, width, height);
            break;
            
        case 24: // AV_PIX_FMT_UYVY422
            grayData = ExtractYFromUYVY422(data, dataSize, width, height);
            break;
            
        case 0: // AV_PIX_FMT_YUV420P
            grayData = ExtractYFromYUV420P(data, dataSize, width, height);
            break;
    }
    
    if (grayData != null)
    {
        DisplayGrayImage(grayData, width, height);
    }
}

/// <summary>
/// 从YUYV422提取Y分量
/// 内存布局：[Y0, U0, Y1, V0, Y2, U1, Y3, V1, ...]
/// </summary>
private byte[] ExtractYFromYUYV422(IntPtr data, int dataSize, int width, int height)
{
    byte[] yuvData = new byte[dataSize];
    byte[] yData = new byte[width * height];
    
    Marshal.Copy(data, yuvData, 0, dataSize);
    
    int yIndex = 0;
    for (int i = 0; i < dataSize; i += 4)
    {
        yData[yIndex++] = yuvData[i];         // Y0
        yData[yIndex++] = yuvData[i + 2];     // Y1
    }
    
    return yData;
}

/// <summary>
/// 从UYVY422提取Y分量
/// 内存布局：[U0, Y0, V0, Y1, U1, Y2, V1, Y3, ...]
/// </summary>
private byte[] ExtractYFromUYVY422(IntPtr data, int dataSize, int width, int height)
{
    byte[] yuvData = new byte[dataSize];
    byte[] yData = new byte[width * height];
    
    Marshal.Copy(data, yuvData, 0, dataSize);
    
    int yIndex = 0;
    for (int i = 0; i < dataSize; i += 4)
    {
        yData[yIndex++] = yuvData[i + 1];     // Y0
        yData[yIndex++] = yuvData[i + 3];     // Y1
    }
    
    return yData;
}

/// <summary>
/// 从YUV420P提取Y分量
/// 内存布局：[Y平面 (width*height字节)] [U平面] [V平面]
/// </summary>
private byte[] ExtractYFromYUV420P(IntPtr data, int dataSize, int width, int height)
{
    int ySize = width * height;
    byte[] yData = new byte[ySize];
    
    Marshal.Copy(data, yData, 0, ySize);
    
    return yData;
}

private void DisplayGrayImage(byte[] grayData, int width, int height)
{
    Application.Current.Dispatcher.Invoke(() =>
    {
        // 创建灰度Bitmap
        var bitmap = BitmapSource.Create(
            width, height,
            96, 96,
            PixelFormats.Gray8,
            null,
            grayData,
            width); // stride = width * 1 byte
        
        imageControl.Source = bitmap;
    });
}
```

---

## 🎯 方案3：使用WriteableBitmap高性能显示

```csharp
public class HighPerformanceGrayDisplay
{
    private WriteableBitmap _bitmap;
    private readonly object _lock = new object();
    
    public void Initialize(int width, int height)
    {
        _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
        imageControl.Source = _bitmap;
        
        // 订阅RAW数据
        UvcReceiver.Instance.RawDataReceive += OnRawDataReceived;
    }
    
    private void OnRawDataReceived(IntPtr data, int dataSize, int pixelFormat, int width, int height)
    {
        if (pixelFormat != 1000 && pixelFormat != 8)
            return;
        
        lock (_lock)
        {
            _bitmap.Lock();
            
            // 直接内存拷贝（零拷贝，最快）
            CopyMemory(data, _bitmap.BackBuffer, dataSize);
            
            _bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            _bitmap.Unlock();
        }
    }
    
    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void CopyMemory(IntPtr dest, IntPtr src, int size);
}
```

---

## 📊 性能对比

| 方案 | CPU使用 | 内存拷贝 | 延迟 | 适用场景 |
|------|---------|---------|------|---------|
| **C++提取Y + WriteableBitmap** | 低 | 1次 | 最低 | 实时预览 ✅ |
| **C#提取Y + BitmapSource** | 中 | 2次 | 中等 | 需要灵活控制 |
| **C++提取Y + Bitmap** | 中低 | 1次+调色板 | 低 | 需要保存文件 |

---

## 🔍 调试技巧

### 1. 确认设备输出格式

连接摄像头后，查看控制台输出：

```
Stream codecpar->format = 24 (yuyv422)
CodecContext pix_fmt = 24 (yuyv422)
```

### 2. 验证数据大小

```csharp
private void OnRawDataReceived(IntPtr data, int dataSize, int pixelFormat, int width, int height)
{
    Console.WriteLine($"Size: {dataSize}, Expected: {width * height}, Format: {pixelFormat}");
    
    // YUYV422: dataSize = width * height * 2
    // Y分量灰度: dataSize = width * height
}
```

### 3. 保存灰度图验证

```csharp
private void SaveGrayImage(string path, byte[] data, int width, int height)
{
    var bitmap = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
    
    // 设置灰度调色板
    var palette = bitmap.Palette;
    for (int i = 0; i < 256; i++)
        palette.Entries[i] = Color.FromArgb(i, i, i);
    bitmap.Palette = palette;
    
    // 复制数据
    var bmpData = bitmap.LockBits(
        new Rectangle(0, 0, width, height),
        ImageLockMode.WriteOnly,
        PixelFormat.Format8bppIndexed);
    
    Marshal.Copy(data, 0, bmpData.Scan0, data.Length);
    bitmap.UnlockBits(bmpData);
    
    bitmap.Save(path, ImageFormat.Png);
}
```

---

## ⚠️ 注意事项

1. **线程安全**：RAW回调在工作线程，更新UI必须用Dispatcher
2. **数据生命周期**：回调返回后数据可能被释放，需立即复制
3. **内存对齐**：YUV的linesize可能大于width，C++端已处理
4. **性能优化**：使用WriteableBitmap + CopyMemory最快
5. **格式码1000**：这是自定义格式码，表示从YUV提取的Y分量

---

## 📝 完整示例

```csharp
public partial class MainWindow : Window
{
    private Image _imageControl;
    
    public MainWindow()
    {
        InitializeComponent();
        
        // 初始化UVC
        InitializeUvc();
    }
    
    private void InitializeUvc()
    {
        var receiver = UvcReceiver.Instance;
        receiver.RawDataReceive += OnRawDataReceived;
        
        // 连接摄像头
        bool connected = receiver.Connect("USB Camera");
        if (connected)
        {
            Logger.Info("UVC connected, waiting for gray frames...");
        }
    }
    
    private void OnRawDataReceived(IntPtr data, int dataSize, int pixelFormat, int width, int height)
    {
        // 只处理灰度数据
        if (pixelFormat != 1000 && pixelFormat != 8)
            return;
        
        // 创建WriteableBitmap
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
        
        bitmap.Lock();
        CopyMemory(data, bitmap.BackBuffer, dataSize);
        bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        bitmap.Unlock();
        
        // 更新UI
        Dispatcher.Invoke(() =>
        {
            _imageControl.Source = bitmap;
        });
    }
    
    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void CopyMemory(IntPtr dest, IntPtr src, int size);
}
```

---

## 🎓 总结

**推荐方案**：使用已实现的C++端自动提取Y分量 + C#端WriteableBitmap显示

- ✅ 零配置，自动识别YUV格式并提取Y分量
- ✅ 高性能，一次内存拷贝
- ✅ 低延迟，跳过sws_scale转码
- ✅ 代码简洁，C#端只需处理Gray8格式
