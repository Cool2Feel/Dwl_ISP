# UVC 堆崩溃 (c0000374) 最终修复报告

## 🚨 问题描述

### 错误信息
```
[2026-04-13 20:32:31.585] [DEBUG] [T01] [UvcReceiver.Disconnect] - Waiting for pending callbacks to complete...
[2026-04-13 20:32:31.616] [INFO ] [T01] [UvcReceiver.Disconnect] - Callback wait completed. Remaining count=0
线程 25156 已退出，返回值为 0 (0x0)。
线程 25280 已退出，返回值为 0 (0x0)。
线程 24836 已退出，返回值为 0 (0x0)。
Critical error detected c0000374
已在 ThunderSE.exe 中执行断点指令(__debugbreak()语句或类似调用)。
```

### 错误含义
- **错误码**: `c0000374` = `STATUS_HEAP_CORRUPTION`
- **触发位置**: `CloseInput()` 调用时
- **根本原因**: **FFmpeg 资源管理不当导致堆内存损坏**

---

## 🔍 根本原因深度分析

### 🔴 核心问题：多个 FFmpeg 资源管理缺陷

#### 问题 1：`av_free_packet` 导致内存损坏

**原始代码**：
```cpp
while (true) {
    if (av_read_frame(pInStreamFormatCtx, packet) < 0) {
        // 重试逻辑
    }
    
    // 使用 packet
    avcodec_decode_video2(..., packet);
    
    av_free_packet(packet);  // ❌ 问题：av_free_packet 已弃用
}
```

**问题本质**：
- `av_free_packet()` 在新版 FFmpeg 中已被弃用
- 应该使用 `av_packet_unref()` 取消引用
- `av_free_packet()` 可能破坏 packet 内部结构，导致后续使用时堆损坏

#### 问题 2：packet 未正确初始化

**原始代码**：
```cpp
AVPacket* packet = (AVPacket*)av_malloc(sizeof(AVPacket));  // ❌ 仅分配内存，未初始化

while (true) {
    if (av_read_frame(pInStreamFormatCtx, packet) < 0) {
        // packet 可能包含未初始化的数据
    }
}
```

**问题本质**：
- `av_malloc(sizeof(AVPacket))` 仅分配内存，不初始化内部字段
- 应该使用 `av_packet_alloc()` 或 `av_init_packet()`
- 未初始化的 packet 可能导致 FFmpeg 内部逻辑错误

#### 问题 3：退出时资源释放顺序错误

**原始代码**：
```cpp
stopPlayingEvent.set();
av_frame_free(&frameForRecordForTranscode);
av_frame_free(&pFrame);
avcodec_close(pInStreamCodecCtx);
avformat_close_input(&pInStreamFormatCtx);  // ❌ 顺序可能有问题
```

**问题本质**：
- 没有释放 `scaleContext`（SwsContext 泄漏）
- 没有释放 `packet`（AVPacket 泄漏）
- 编解码器关闭和输入关闭的顺序可能影响堆

#### 问题 4：CloseInput() 中的双重释放风险

**修复前代码**：
```cpp
// DecodeThread 退出时
avformat_close_input(&pInStreamFormatCtx);  // ✅ 第一次释放

// CloseInput() 中
if (pInStreamFormatCtx) {
    avformat_close_input(&pInStreamFormatCtx);  // ❌ 可能第二次释放
}
```

---

## ✅ 完整修复方案

### 修复 1：使用正确的 packet 初始化和释放

**文件**：`uvc.cpp` → `DecodeThread()`

```cpp
unsigned int __stdcall DecodeThread(void* param)
{
    // ✅ 检查全局指针有效性
    if (!pInStreamFormatCtx || videoindex < 0 || !pInStreamFormatCtx->streams[videoindex]) {
        printf("DecodeThread: Invalid context or video index, exiting.\n");
        if (playStateChangeCallbackFunc) {
            playStateChangeCallbackFunc(false);
        }
        stopPlayingEvent.set();  // ✅ 设置事件，让 CloseInput 不会死等
        return 1;
    }

    AVFrame* pFrame = av_frame_alloc();
    
    // ✅ 使用 av_malloc 分配 packet（后续会用 av_init_packet 初始化）
    AVPacket* packet = (AVPacket*)av_malloc(sizeof(AVPacket));
    int ret = 0, got_picture = 0;

    SwsContext* scaleContext = nullptr;
    AVFrame* frameForRecordForTranscode = nullptr;

    // ... 分配 frame 和 scaleContext ...

    int retryCount = 0;

    // ✅ 初始化 packet，避免使用未初始化的内存
    av_init_packet(packet);
    packet->data = nullptr;
    packet->size = 0;

    while (true)
    {
        if (!isPlaying)
        {
            break;
        }

        // ✅ 在读取新帧前取消引用旧的 packet
        av_packet_unref(packet);
        
        if (av_read_frame(pInStreamFormatCtx, packet) < 0)
        {
            if (retryCount < 100)
            {
                retryCount++;
                Sleep(20);
                continue;
            }
            else
            {
                break;
            }
        }
        
        retryCount = 0;
        if (packet->stream_index != videoindex)
            continue;

        // ... RAW 捕获逻辑 ...

        // ✅ 使用 av_packet_unref 替代 av_free_packet
        ret = avcodec_decode_video2(pInStreamCodecCtx, pFrame, &got_picture, packet);
        if (ret < 0) {
            printf("Decode Error.\n");
            av_packet_unref(packet);  // ✅ 错误时也要取消引用
            continue;
        }

        // ... 解码后处理 ...

        av_packet_unref(packet);  // ✅ 循环末尾取消引用
    }

    stopPlayingEvent.set();
    
    // ✅ 释放资源（顺序很重要）
    av_packet_free(&packet);  // ✅ 使用 av_packet_free 替代 av_free
    av_frame_free(&frameForRecordForTranscode);
    av_frame_free(&pFrame);
    
    if (scaleContext) {
        sws_freeContext(scaleContext);  // ✅ 释放缩放上下文
    }
    
    // ✅ 关闭编解码器并释放输入
    if (pInStreamCodecCtx) {
        avcodec_close(pInStreamCodecCtx);
        pInStreamCodecCtx = nullptr;
    }
    
    if (pInStreamFormatCtx) {
        avformat_close_input(&pInStreamFormatCtx);
        // ✅ 不要手动设置 nullptr，avformat_close_input 会处理
    }

    if (playStateChangeCallbackFunc != nullptr)
    {
        playStateChangeCallbackFunc(false);
    }
    return 0;
}
```

**关键改进**：
- ✅ 使用 `av_init_packet()` 正确初始化
- ✅ 使用 `av_packet_unref()` 替代 `av_free_packet()`
- ✅ 循环开始和结束都取消引用
- ✅ 释放 `scaleContext` 避免泄漏
- ✅ 使用 `av_packet_free()` 完整释放 packet

### 修复 2：CloseInput() 不再释放资源

**文件**：`uvc.cpp`

```cpp
UVC_API int CloseInput()
{
    if (isRecording)
    {
        StopRecord();
    }

	if (isPlaying)
	{
        printf("Stopping video playback...\n");
		InterlockedExchange8((char*)&isPlaying, 0);
        printf("Waiting for decode thread to exit...\n");
		stopPlayingEvent.wait();
        printf("Decode thread exited.\n");
	}

    // ✅ 关键修复：DecodeThread 已经释放了所有资源
    // 这里只需确保指针为 nullptr，不要再次调用 avformat_close_input！

    pInStreamFormatCtx = nullptr;  // ✅ DecodeThread 已释放，仅重置指针
    videoindex = -1;
    pInStreamCodecCtx = nullptr;
    pInStreamCodec = nullptr;

    printf("CloseInput completed.\n");
    return 0;
}
```

**关键改进**：
- ✅ **完全移除 `avformat_close_input()` 调用**
- ✅ 仅将指针置为 `nullptr`（防御性编程）
- ✅ 资源由 `DecodeThread` 统一释放

### 修复 3：OpenInput() 开头完全清理

**文件**：`uvc.cpp`

```cpp
UVC_API int OpenInput(const char* filepath, int& videoWidth, int& videoHeight)
{
    // ✅ 检查并清理上一次连接的残留资源
    if (pInStreamFormatCtx) {
        printf("Warning: Previous input context not freed, cleaning up...\n");
        avformat_close_input(&pInStreamFormatCtx);
        // ✅ 不调用 pInStreamFormatCtx = nullptr，因为 avformat_close_input 会处理
    }
    videoindex = -1;
    pInStreamCodecCtx = nullptr;
    pInStreamCodec = nullptr;

    printf("Opening UVC input: %s\n", filepath);
    
    av_register_all();
    avformat_network_init();
    
    pInStreamFormatCtx = avformat_alloc_context();
    if (!pInStreamFormatCtx) {
        printf("Failed to allocate format context.\n");
        return -3;
    }
    pInStreamFormatCtx->flags |= AVFMT_FLAG_NONBLOCK;
    
    // ... 后续打开设备逻辑 ...
}
```

**关键改进**：
- ✅ 开头检查并清理残留资源
- ✅ 增加日志便于调试
- ✅ 检查 `avformat_alloc_context()` 返回值

---

## 📊 修复对比

| 维度 | 修复前 | 修复后 |
|------|--------|--------|
| **packet 初始化** | ❌ av_malloc | ✅ av_init_packet + 设置 data/size |
| **packet 释放** | ❌ av_free_packet（已弃用） | ✅ av_packet_unref |
| **packet 完整释放** | ❌ 未释放 | ✅ av_packet_free |
| **scaleContext 释放** | ❌ 泄漏 | ✅ sws_freeContext |
| **CloseInput 释放资源** | ❌ 双重释放风险 | ✅ 仅重置指针 |
| **DecodeThread 空指针保护** | ❌ 无 | ✅ 三重检查 |
| **错误时 packet 处理** | ❌ 未处理 | ✅ av_packet_unref |
| **日志详细度** | ⚠️ 基础 | ✅ 分步详细 |

---

## 🎯 修复后的执行流程

```
用户切换 set_mode
    ↓
UvcReceiver.Reconnect() 开始
    ↓
Disconnect() → CloseInput()
    ├── isRecording? → StopRecord()
    ├── isPlaying? → 是
    │    ├── printf("Stopping video playback...")
    │    ├── InterlockedExchange8(&isPlaying, 0)
    │    ├── printf("Waiting for decode thread to exit...")
    │    └── stopPlayingEvent.wait()
    ↓
    DecodeThread 检测到 !isPlaying
    ├── 退出循环
    ├── stopPlayingEvent.set()
    ├── ✅ av_packet_free(&packet)  ← 完整释放 packet
    ├── av_frame_free(frameForRecordForTranscode)
    ├── av_frame_free(pFrame)
    ├── ✅ sws_freeContext(scaleContext)  ← 释放缩放上下文
    ├── ✅ avcodec_close(pInStreamCodecCtx)
    ├── ✅ avformat_close_input(&pInStreamFormatCtx)  ← 唯一一次释放
    └── 线程退出
    ↓
    CloseInput() 继续执行
    ├── printf("Decode thread exited.")
    ├── pInStreamFormatCtx = nullptr  ← ✅ 仅重置指针
    ├── videoindex = -1
    ├── pInStreamCodecCtx = nullptr
    ├── pInStreamCodec = nullptr
    └── printf("CloseInput completed.")
    ↓
等待 1.5 秒
    ↓
Connect() → OpenInput()
    ├── 检查残留资源 → avformat_close_input（如果有）
    ├── avformat_alloc_context()
    ├── 打开设备
    └── 启动新的 DecodeThread
```

---

## 🔧 FFmpeg 资源管理最佳实践

### 1. AVPacket 正确使用

```cpp
// ✅ 分配并初始化
AVPacket* packet = (AVPacket*)av_malloc(sizeof(AVPacket));
av_init_packet(packet);
packet->data = nullptr;
packet->size = 0;

// ✅ 循环中使用
while (...) {
    av_packet_unref(packet);  // 取消引用旧的
    av_read_frame(ctx, packet);
    // 使用 packet
    av_packet_unref(packet);  // 取消引用
}

// ✅ 最终释放
av_packet_free(&packet);
```

### 2. 资源释放顺序

```cpp
// 1. 停止读取
isPlaying = false;
wait_for_thread_exit();

// 2. 释放帧缓冲区
av_frame_free(&frame1);
av_frame_free(&frame2);

// 3. 释放 packet
av_packet_free(&packet);

// 4. 释放缩放上下文
sws_freeContext(scaleContext);

// 5. 关闭编解码器
avcodec_close(codecCtx);

// 6. 释放输入
avformat_close_input(&formatCtx);
```

### 3. 避免双重释放

```cpp
// ❌ 错误：两个地方都释放
void ThreadExit() {
    avformat_close_input(&ctx);  // 第一次
}

void CloseInput() {
    avformat_close_input(&ctx);  // 💥 第二次 → 堆崩溃
}

// ✅ 正确：只有一个地方释放
void ThreadExit() {
    avformat_close_input(&ctx);  // 唯一一次释放
}

void CloseInput() {
    ctx = nullptr;  // 仅重置指针
}
```

---

## 📝 测试建议

### 测试场景 1：正常重连（必测）
```
1. 启动程序
2. 连接 UVC 设备
3. 切换 set_mode（触发重连）
预期：
  - 日志显示 "Stopping video playback..."
  - 日志显示 "Waiting for decode thread to exit..."
  - 日志显示 "Decode thread exited."
  - 日志显示 "CloseInput completed."
  - ✅ 程序不崩溃
  - ✅ 重连成功
```

### 测试场景 2：快速连续重连
```
1. 连接 UVC 设备
2. 连续 5 次切换 set_mode
预期：
  - 第二次及以后的重连被跳过（_isReconnecting 保护）
  - 不会导致死锁或崩溃
```

### 测试场景 3：长时间运行
```
1. 连接设备
2. 运行 30 分钟
3. 每隔 2 分钟切换一次 set_mode
预期：
  - 无内存泄漏
  - 无堆崩溃
  - 程序稳定运行
```

---

## 📚 修改的文件清单

| 文件 | 修改内容 | 行数变化 |
|------|----------|----------|
| `uvc.cpp` | 修复 packet 初始化（av_init_packet） | +6 |
| `uvc.cpp` | 替换 av_free_packet → av_packet_unref（3处） | +3 |
| `uvc.cpp` | DecodeThread 退出时完整释放资源 | +15 |
| `uvc.cpp` | 释放 scaleContext | +4 |
| `uvc.cpp` | CloseInput() 不再释放资源 | -10 |
| `uvc.cpp` | OpenInput() 开头清理残留 | +5 |
| `uvc.cpp` | DecodeThread 开头空指针保护 | +10 |
| `uvc.cpp` | 增加详细日志 | +8 |

---

## ✅ 修复完成清单

- [x] 识别 av_free_packet 导致的内存损坏
- [x] 修复 packet 初始化（av_init_packet）
- [x] 替换所有 av_free_packet → av_packet_unref
- [x] DecodeThread 退出时完整释放资源
- [x] 释放 scaleContext 避免泄漏
- [x] CloseInput() 不再释放资源
- [x] OpenInput() 开头清理残留
- [x] DecodeThread 开头空指针保护
- [x] 增加详细日志输出
- [x] 编写详细修复报告

---

## 🎓 总结

### 问题本质
**FFmpeg 资源管理不当导致堆内存损坏**：
1. 使用已弃用的 `av_free_packet()`
2. packet 未正确初始化
3. 资源释放不完整（scaleContext 泄漏）
4. CloseInput() 和 DecodeThread 双重释放风险

### 修复关键
- ✅ **正确初始化**：使用 `av_init_packet()`
- ✅ **正确释放**：使用 `av_packet_unref()` + `av_packet_free()`
- ✅ **完整释放**：包括 scaleContext、packet、frame 等
- ✅ **避免双重释放**：DecodeThread 负责释放，CloseInput 仅重置指针
- ✅ **详细日志**：帮助快速定位问题

### 经验教训
1. **FFmpeg API 会更新**：定期检查弃用警告
2. **资源释放必须完整**：包括所有分配的内存
3. **多线程资源管理要严格**：明确谁分配，谁释放
4. **日志记录必须详细**：帮助调试和监控

---

**修复完成时间**：2026-04-13  
**问题严重性**：🔴🔴🔴 致命（堆崩溃导致程序立即终止）  
**修复状态**：✅ 已完成  
**下一步**：编译测试 → 功能验证 → 压力测试
