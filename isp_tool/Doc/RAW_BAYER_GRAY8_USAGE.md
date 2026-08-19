# RAW Bayer 灰度图显示使用示例

## 概述

`UvcReceiver` 现已支持 RAW Bayer 数据以灰度图形式显示。当 C++ 端传递的 `pixelFormat` 为 100-103 时，会自动识别为 RAW Bayer 数据并触发专用事件。

## 像素格式定义

```csharp
public const int PIXEL_FORMAT_RAW8 = 100;   // 8-bit RAW
public const int PIXEL_FORMAT_RAW10 = 101;  // 10-bit RAW
public const int PIXEL_FORMAT_RAW12 = 102;  // 12-bit RAW
public const int PIXEL_FORMAT_RAW16 = 103;  // 16-bit RAW
```

## 使用方式

### 1. 订阅 RawBayerReceive 事件

```csharp
using ThunderSE.Uvc;

// 在初始化代码中
var receiver = UvcReceiver.Instance;
receiver.RawBayerReceive += OnRawBayerDataReceive;
```

### 2. 处理 RAW Bayer 数据

```csharp
private WriteableBitmap _bitmap;

private void OnRawBayerDataReceive(byte[] dataBuffer, int width, int height, int bitDepth)
{
    if (_bitmap == null)
    {
        // 创建 Gray8 格式的 WriteableBitmap
        _bitmap = new WriteableBitmap(
            width,
            height,
            96, 96,
            PixelFormats.Gray8,
            null);
        
        MyImageControl.Source = _bitmap;
    }

    try
    {
        _bitmap.Lock();
        _bitmap.WritePixels(
            new Int32Rect(0, 0, width, height),
            dataBuffer,
            width,  // stride = width * 1 byte (Gray8)
            0);
        _bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        _bitmap.Unlock();
    }
    catch (Exception ex)
    {
        Logger.Error($"Display error: {ex.Message}");
        try { _bitmap.Unlock(); } catch { }
    }
}
```

### 3. 完整的控件示例

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThunderSE.Uvc;

namespace ThunderSE.Ui.MainWindow
{
    public partial class RawBayerViewControl : UserControl
    {
        private WriteableBitmap _bitmap;
        private volatile bool _isReceiving = false;

        public RawBayerViewControl()
        {
            InitializeComponent();
            Loaded += RawBayerViewControl_Loaded;
            Unloaded += RawBayerViewControl_Unloaded;
        }

        private void RawBayerViewControl_Loaded(object sender, RoutedEventArgs e)
        {
            var receiver = UvcReceiver.Instance;
            
            // 初始化显示
            InitializeDisplay(receiver.VideoWidth, receiver.VideoHeight);
            
            // 订阅事件
            receiver.RawBayerReceive += OnRawBayerDataReceive;
            _isReceiving = true;
        }

        private void RawBayerViewControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _isReceiving = false;
            var receiver = UvcReceiver.Instance;
            receiver.RawBayerReceive -= OnRawBayerDataReceive;
        }

        private void InitializeDisplay(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                width = 1280;
                height = 720;
            }

            _bitmap = new WriteableBitmap(
                width,
                height,
                96, 96,
                PixelFormats.Gray8,
                null);

            RawBayerImage.Source = _bitmap;
        }

        private void OnRawBayerDataReceive(byte[] dataBuffer, int width, int height, int bitDepth)
        {
            if (!_isReceiving || _bitmap == null) return;

            try
            {
                _bitmap.Lock();
                _bitmap.WritePixels(
                    new Int32Rect(0, 0, width, height),
                    dataBuffer,
                    width,  // stride for Gray8
                    0);
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                _bitmap.Unlock();
            }
            catch (Exception ex)
            {
                Logger.Error($"RawBayer display error: {ex.Message}");
                try { _bitmap.Unlock(); } catch { }
            }
        }
    }
}
```

## XAML 定义

```xml
<UserControl x:Class="ThunderSE.Ui.MainWindow.RawBayerViewControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <Image x:Name="RawBayerImage" 
               Stretch="Uniform" 
               RenderOptions.BitmapScalingMode="NearestNeighbor" />
    </Grid>
</UserControl>
```

## 数据流说明

```
C++ UVC 回调 (OnReceiveDataStatic)
    ↓
检测 pixelFormat (100-103)
    ↓
ProcessRawBayerData
    ├─ 检查订阅者
    ├─ 限流检查
    ├─ 复制数据
    └─ 位深转换 (>8-bit → 8-bit)
    ↓
Dispatcher.BeginInvoke (UI 线程)
    ↓
ProcessRawBayerFrame
    ↓
RawBayerReceive 事件
    ↓
订阅者处理 (WriteableBitmap.WritePixels)
    ↓
Image 控件显示
```

## 注意事项

1. **C++ 端配合**：需要 C++ 端在 `VideoDataCallback` 中传递正确的 `pixelFormat` 值（100-103）
2. **数据尺寸**：
   - 8-bit: `width * height` 字节
   - 10/12/16-bit: `width * height * 2` 字节（short 数组）
3. **性能优化**：
   - 避免在回调中进行耗时操作
   - 使用 `WriteableBitmap` 的 `Lock/Unlock` 减少内存分配
   - 考虑使用对象池复用缓冲区
4. **线程安全**：事件已在 UI 线程触发，可直接更新 UI

## 与现有代码的兼容

- `DataReceive` 事件仍然正常工作（RGB24 数据）
- `RawDataReceive` 事件仍然正常工作（通用原始数据）
- 新增 `RawBayerReceive` 事件专门处理 RAW Bayer 灰度显示
- 三种事件互不干扰，可同时订阅

## 切换模式

```csharp
var receiver = UvcReceiver.Instance;

// 检查当前模式
if (receiver.IsRawBayerMode)
{
    // 当前为 RAW Bayer 模式
    Console.WriteLine("RAW Bayer mode active");
}
else
{
    // 当前为 RGB24 模式
    Console.WriteLine("RGB24 mode active");
}
```

## 调试技巧

```csharp
private void OnRawBayerDataReceive(byte[] dataBuffer, int width, int height, int bitDepth)
{
    Logger.Debug($"RAW Bayer received: {width}x{height}, {bitDepth}-bit, size={dataBuffer.Length}");
    
    // 可选：保存原始数据用于分析
    // System.IO.File.WriteAllBytes($"raw_{DateTime.Now:HHmmssfff}.bin", dataBuffer);
    
    // ... 显示代码 ...
}
```
