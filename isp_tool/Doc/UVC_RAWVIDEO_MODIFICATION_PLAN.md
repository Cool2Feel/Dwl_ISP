# UVC 视频处理修改说明

## 修改目标
根据输入格式自动选择处理方式：
- **MJPG/H264等压缩格式** → sws_scale转换为RGB24 → videoDataCallbackFunc
- **RAWVIDEO** → 直接回调原始数据 → videoDataCallbackFunc（不做格式转换）

## 需要修改的文件

### 1. Uvc/uvc.cpp (C++端)

#### 修改位置：第512-530行（videoDataCallbackFunc回调部分）

**原代码：**
```cpp
if (videoDataCallbackFunc != nullptr)
{
    // Yuv回调暂时不使用，注释掉
    //yuvDataCallbackFunc((void**)pFrame->data);

    // 使用老的ffmpeg的转换方法，临时使用
    int ret = sws_scale(scaleContext,
        (const uint8_t * const *)pFrame->data,
        pFrame->linesize,
        0,
        pFrame->height,
        frameForRecordForTranscode->data,
        frameForRecordForTranscode->linesize);
    videoDataCallbackFunc((void*)frameForRecordForTranscode->data[0],
        frameForRecordForTranscode->linesize[0] * pFrame->height, user_data_ptr);
}
```

**新代码：**
```cpp
if (videoDataCallbackFunc != nullptr)
{
    // Yuv回调暂时不使用，注释掉
    //yuvDataCallbackFunc((void**)pFrame->data);

    // ============================================================
    // 自动判断输入格式，选择处理方式
    // - MJPG/H264等压缩格式：需要sws_scale转换为RGB24
    // - RAWVIDEO：直接回调原始数据，不做转换
    // ============================================================
    AVCodecID codecId = pInStreamFormatCtx->streams[videoindex]->codec->codec_id;
    AVPixelFormat inputFmt = pInStreamCodecCtx->pix_fmt;

    if (codecId == AV_CODEC_ID_RAWVIDEO)
    {
        // --------------------------------------------------------
        // RAWVIDEO模式：直接回调原始数据（不做格式转换）
        // --------------------------------------------------------
        
        // 计算实际数据大小
        int dataSize = 0;
        if (inputFmt == AV_PIX_FMT_YUYV422 || inputFmt == AV_PIX_FMT_UYVY422) {
            dataSize = pFrame->width * pFrame->height * 2; // YUV422: 2字节/像素
        } else if (inputFmt == AV_PIX_FMT_YUV420P || inputFmt == AV_PIX_FMT_NV12 || inputFmt == AV_PIX_FMT_NV21) {
            dataSize = pFrame->width * pFrame->height * 3 / 2; // YUV420: 1.5字节/像素
        } else if (inputFmt == AV_PIX_FMT_RGB24 || inputFmt == AV_PIX_FMT_BGR24) {
            dataSize = pFrame->width * pFrame->height * 3; // RGB: 3字节/像素
        } else if (inputFmt == AV_PIX_FMT_GRAY8) {
            dataSize = pFrame->width * pFrame->height; // GRAY8: 1字节/像素
        } else {
            // 默认使用linesize计算
            dataSize = pFrame->linesize[0] * pFrame->height;
        }

        // 回调原始数据（包含格式信息）
        videoDataCallbackFunc(
            (void*)pFrame->data[0],
            dataSize,
            user_data_ptr);

        // 调试输出（可选）
        // printf("RAWVIDEO callback: fmt=%d, size=%d, %dx%d\n", 
        //        inputFmt, dataSize, pFrame->width, pFrame->height);
    }
    else
    {
        // --------------------------------------------------------
        // MJPG/H264等压缩格式：需要sws_scale转换为RGB24
        // --------------------------------------------------------
        int ret = sws_scale(scaleContext,
            (const uint8_t * const *)pFrame->data,
            pFrame->linesize,
            0,
            pFrame->height,
            frameForRecordForTranscode->data,
            frameForRecordForTranscode->linesize);

        videoDataCallbackFunc(
            (void*)frameForRecordForTranscode->data[0],
            frameForRecordForTranscode->linesize[0] * pFrame->height,
            user_data_ptr);
    }
}
```

#### 关键修改说明：

1. **格式判断**：使用 `AVCodecID` 判断是否为 `AV_CODEC_ID_RAWVIDEO`
2. **RAWVIDEO路径**：
   - 跳过 `sws_scale` 转换
   - 直接回调 `pFrame->data[0]`
   - 根据像素格式计算准确的数据大小
3. **MJPG路径**：保持原有逻辑，进行RGB24转换

### 2. ThunderSE/Uvc/UvcApi.cs (C#端)

**无需修改** - 现有的回调委托已经支持原始数据传递：
```csharp
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
public delegate int RawDataCallbackFunc(IntPtr rawData, int dataSize, int pixelFormat, int width, int height, IntPtr user_data);
```

### 3. ThunderSE/Uvc/UvcReceiver.cs (C#端)

**无需修改** - 现有的RawData回调已经完整：
```csharp
private static int OnReceiveRawDataStatic(IntPtr rawData, int dataSize, int pixelFormat, int width, int height, IntPtr user_data)
{
    // 已经实现了完整的原始数据接收逻辑
}
```

## 实现逻辑

### 数据流

```
[UVC设备输入]
    |
    | 判断 AVCodecID
    |
    ├─ AV_CODEC_ID_RAWVIDEO
    |   ├─ 计算数据大小（基于像素格式）
    |   ├─ 直接回调 videoDataCallbackFunc(pFrame->data[0], dataSize, user_data)
    |   └─ C#端接收原始数据（YUYV422/YUV420P/RGB24等）
    |
    └─ AV_CODEC_ID_MJPEG / AV_CODEC_ID_H264 等
        ├─ sws_scale 转换为 RGB24
        ├─ 回调 videoDataCallbackFunc(RGB24_data, size, user_data)
        └─ C#端接收RGB24格式数据
```

### 像素格式数据大小计算

| 像素格式 | 字节/像素 | 数据大小公式 |
|---------|----------|-------------|
| YUYV422 | 2 | width × height × 2 |
| UYVY422 | 2 | width × height × 2 |
| YUV420P | 1.5 | width × height × 3/2 |
| NV12 | 1.5 | width × height × 3/2 |
| NV21 | 1.5 | width × height × 3/2 |
| RGB24 | 3 | width × height × 3 |
| BGR24 | 3 | width × height × 3 |
| GRAY8 | 1 | width × height |

## 测试建议

1. **MJPG设备测试**：
   - 连接MJPEG格式的UVC摄像头
   - 验证是否正常显示（应走RGB24转换路径）
   - 可添加printf确认走了MJPG路径

2. **RAWVIDEO设备测试**：
   - 连接RAWVIDEO格式的UVC摄像头
   - 验证是否正常显示（应走原始数据路径）
   - 检查数据格式是否正确传递

3. **格式识别验证**：
   - 在C#端的RawDataReceive回调中添加日志
   - 确认pixelFormat、width、height参数正确

## 注意事项

1. **C#端显示适配**：
   - 当前UI控件使用 `WriteableBitmap(PixelFormats.Rgb24)`
   - 如果RAWVIDEO传递的不是RGB24格式，需要在C#端进行格式转换
   - 或者修改WriteableBitmap的PixelFormat以匹配原始数据格式

2. **性能优化**：
   - RAWVIDEO模式省去了sws_scale转换，性能更好
   - 但需要确保C#端能正确处理原始数据格式

3. **调试输出**：
   - 建议初期取消printf注释，确认格式判断正确
   - 验证通过后可注释掉printf以提高性能
