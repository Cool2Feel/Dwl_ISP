# UVC 退出时回调超时问题分析报告

## 问题描述

软件退出时,在 `UvcReceiver.Disconnect()` 中等待 C++ 回调完成时超时 5 秒,严重影响退出体验。

### 日志证据

```
[2026-04-14 15:05:03.767] [DEBUG] [T01] [UvcReceiver.Disconnect] - Waiting for pending callbacks to complete...
[2026-04-14 15:05:04.328] [DEBUG] [T01] [UvcReceiver.Disconnect] - Still waiting for callbacks... count=1, wait=500ms
[2026-04-14 15:05:04.888] [DEBUG] [T01] [UvcReceiver.Disconnect] - Still waiting for callbacks... count=1, wait=1000ms
...
[2026-04-14 15:05:09.402] [DEBUG] [T01] [UvcReceiver.Disconnect] - Still waiting for callbacks... count=1, wait=5000ms
[2026-04-14 15:05:09.432] [WARN ] [T01] [UvcReceiver.Disconnect] - Disconnect timeout: some callbacks may still be running. Force closing.
```

**关键观察**: `_receivePacketCount` 始终为 1,说明有一个视频帧回调卡住了,没有完成。

---

## 问题根因分析

### 1. 回调执行流程

```
C++ 层回调 (T09 线程)
    ↓
OnReceiveDataStatic() 
    ↓
Interlocked.Increment(ref _receivePacketCount)  // 计数器 +1
    ↓
Application.Current.Dispatcher.BeginInvoke()    // 异步调度到 UI 线程
    ↓
ProcessVideoData() → finally { Interlocked.Decrement(ref _receivePacketCount) }  // 计数器 -1
```

### 2. 问题场景

**应用退出时的时序问题**:

1. 用户关闭窗口,触发 `IspToolApp.OnExit`
2. WPF 开始关闭 `Dispatcher`
3. `UvcReceiver.Disconnect()` 被调用
4. 此时 C++ 层的视频回调可能还在运行
5. `OnReceiveDataStatic` 执行了 `Interlocked.Increment(ref _receivePacketCount)`,计数器变为 1
6. 尝试 `Dispatcher.BeginInvoke()` 调度 `ProcessVideoData`
7. **问题**: 如果 `Dispatcher` 已经关闭或正在关闭:
   - `BeginInvoke` 可能不会执行委托
   - 或者执行被取消
   - 但 `ProcessVideoData` 的 `finally` 块永远不会执行
   - `_receivePacketCount` 永远不会递减回 0
8. `Disconnect()` 中的 `while (_receivePacketCount > 0)` 循环永远等待,直到 5 秒超时

### 3. 日志时间线解读

```
15:05:03.767 - 开始等待回调 (count=1)
15:05:03.767 ~ 15:05:09.402 - 持续等待 5.6 秒, count 始终为 1
15:05:09.432 - 超时,强制关闭
15:05:09.596 - 收到 "Play state changed: Stopped" (来自 T09 线程)
```

**注意**: "Play state changed: Stopped" 在调用 `CloseInput()` 之后才收到,说明 C++ 层在 `CloseInput()` 被调用后才发出停止事件,而在这之前回调可能还在运行。

---

## 修复方案

### 修复 1: 在回调中检测 Dispatcher 关闭状态

**文件**: `UvcReceiver.cs` - `OnReceiveDataStatic` 方法

**修改前**:
```csharp
Interlocked.Increment(ref instance._receivePacketCount);

// 异步调度到UI线程
Application.Current?.Dispatcher?.BeginInvoke(
    DispatcherPriority.Normal,
    new Action(() => instance.ProcessVideoData(dataBuffer, pixelFormat)));
```

**修改后**:
```csharp
Interlocked.Increment(ref instance._receivePacketCount);

// 关键修复: 应用退出时,Dispatcher 可能已关闭,直接跳过调度
var dispatcher = Application.Current?.Dispatcher;
if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
{
    Logger.Debug("Dispatcher is shutting down, skipping video frame processing.");
    Interlocked.Decrement(ref instance._receivePacketCount);  // 立即递减,防止卡住
    return 0;
}

// 异步调度到UI线程
dispatcher.BeginInvoke(
    DispatcherPriority.Normal,
    new Action(() => instance.ProcessVideoData(dataBuffer, pixelFormat)));
```

**效果**: 
- 如果 Dispatcher 已关闭,立即递减 `_receivePacketCount`
- 避免回调卡住,让 `Disconnect()` 快速完成

---

### 修复 2: 在 Disconnect 中检测 Dispatcher 关闭

**文件**: `UvcReceiver.cs` - `Disconnect` 方法

**修改前**:
```csharp
while (Interlocked.Read(ref _receivePacketCount) > 0 && waitCount < maxWaitCount)
{
    Thread.Sleep(10);
    waitCount++;
    
    if (waitCount % 50 == 0)
    {
        Logger.Debug($"Still waiting for callbacks... count={...}, wait={...}ms");
    }
}
```

**修改后**:
```csharp
while (Interlocked.Read(ref _receivePacketCount) > 0 && waitCount < maxWaitCount)
{
    Thread.Sleep(10);
    waitCount++;

    // 关键改进: 检查 Dispatcher 是否已关闭,如果是则不需要等待
    var dispatcher = Application.Current?.Dispatcher;
    bool dispatcherShutdown = dispatcher == null || 
                             dispatcher.HasShutdownStarted || 
                             dispatcher.HasShutdownFinished;
    
    if (dispatcherShutdown && waitCount > 10) // 给一点时间(100ms)让 pending 回调完成
    {
        Logger.Info($"Dispatcher shutdown detected, skipping callback wait. Remaining count={...}");
        break;  // 提前退出等待循环
    }

    if (waitCount % 50 == 0)
    {
        Logger.Debug($"Still waiting for callbacks... count={...}, wait={...}ms");
    }
}
```

**效果**:
- 如果检测到 Dispatcher 已关闭,等待 100ms 后提前退出
- 避免无意义的 5 秒超时等待

---

## 预期效果

修复后,软件退出时的行为应该是:

```
[INFO] Application exiting, cleaning up...
[INFO] Disconnecting from UVC device...
[DEBUG] Waiting for pending callbacks to complete...
[DEBUG] Dispatcher shutdown detected, skipping callback wait. Remaining count=1
[DEBUG] Waiting 100ms for C++ callbacks to exit...
[DEBUG] Calling C++ CloseInput...
[DEBUG] Play state changed: Stopped
[INFO] UVC disconnected.
```

**退出时间**: 从原来的 ~6 秒缩短到 ~200ms

---

## 技术要点

### 1. Dispatcher 关闭检测

WPF 的 `Dispatcher` 提供了两个属性用于检测关闭状态:
- `HasShutdownStarted`: Dispatcher 开始关闭,但可能还在处理最后的消息
- `HasShutdownFinished`: Dispatcher 完全关闭,不能再调用 `BeginInvoke`

### 2. 为什么需要等待 100ms?

即使 Dispatcher 已关闭,也应该给已调度的回调一点时间完成:
- 已经 `BeginInvoke` 的委托可能还在队列中
- 给 100ms 让已调度的回调有机会执行完成
- 避免过于激进的关闭导致资源泄漏

### 3. 线程安全

- `_receivePacketCount` 使用 `Interlocked` 操作,确保线程安全
- `dispatcherShutdown` 检测在主线程执行,不需要额外锁保护

---

## 测试建议

1. **正常退出**: 打开 UVC 预览 → 关闭应用 → 验证退出时间 < 500ms
2. **录制中退出**: 开始录制 → 关闭应用 → 验证录制文件完整且快速退出
3. **重连中退出**: 触发设备重连 → 关闭应用 → 验证无异常
4. **压力测试**: 快速打开/关闭应用 10 次 → 验证无内存泄漏和崩溃

---

## 相关文件

- `ThunderSE/Uvc/UvcReceiver.cs` - UVC 接收器实现
- `ThunderSE/IspToolApp.xaml.cs` - 应用退出入口 (`OnExit` 方法)
- `ThunderSE/Uvc/UvcApi.cs` - UVC API 封装

---

## 后续优化建议

1. **C++ 层改进**: 在 `CloseInput` 中主动停止回调循环,而不是等待回调自然退出
2. **使用 CancellationToken**: 将超时逻辑改为 CancellationToken,更优雅
3. **回调注册/注销**: 提供更细粒度的控制,允许在 Disconnect 前注销回调

---

## 更新: 退出时 Connect 调用问题 (2026-04-14)

### 问题现象

在 `Disconnect` 完成后,日志显示有 `Connect` 调用:

```
[2026-04-14 15:09:46.199] [INFO ] [T01] [UvcReceiver.Disconnect] - UVC disconnected.
[2026-04-14 15:09:46.199] [INFO ] [T01] [IspToolApp.xaml.OnExit] - UVC connection disconnected.
[2026-04-14 15:09:46.199] [INFO ] [T10] [UvcReceiver.Connect] - Connecting to UVC device: video=GENERAL - UVC
```

### 问题根因

**设备变化事件在应用退出时触发了自动重连**:

1. `OnExit` 调用 `Disconnect()` → `CloseInput()`
2. `CloseInput()` 导致 C++ 层设备断开
3. C++ 层的设备检测回调触发了 `DeviceEvent.Arrival` 事件
4. `ConfigManager.OnDeviceChange(Arrival)` 被调用
5. `Task.Run(() => UvcReceiver.Instance.Connect(...))` 启动异步连接
6. 由于 `Task.Run` 是异步的,在 `Disconnect` 完成后才执行 `Connect`

### 修复方案

#### 修复 1: 添加全局退出标志

**文件**: `UvcReceiver.cs`

```csharp
// 新增静态标志
private static volatile bool _isApplicationExiting = false;

// 提供公开属性供外部检查
public static bool IsApplicationExiting => _isApplicationExiting;

// 提供设置方法
public static void SetApplicationExiting()
{
    _isApplicationExiting = true;
    Logger.Debug("Application exiting flag set.");
}
```

#### 修复 2: 在 OnExit 中设置退出标志

**文件**: `IspToolApp.xaml.cs`

```csharp
private void OnExit(object sender, ExitEventArgs e)
{
    try
    {
        Logger.Info("Application exiting, cleaning up...");
        
        // 关键修复：标记应用正在退出，防止设备变化事件触发自动重连
        UvcReceiver.SetApplicationExiting();
        
        UvcReceiver.Instance.Disconnect();
        Logger.Info("UVC connection disconnected.");
    }
    catch (Exception ex)
    {
        Logger.Error("Error during application exit.", ex);
    }
    finally
    {
        Logger.Cleanup();
    }
}
```

#### 修复 3: 在 OnDeviceChange 中检查退出标志

**文件**: `ConfigManager.cs`

```csharp
private void OnDeviceChange(DeviceEvent eventType, string location, string model, string uvcInterafce)
{
    if (_disposed) return;

    try
    {
        Logger.Info($"Device change event received: {eventType}, Model={model}");

        // 关键修复：如果应用正在退出，跳过所有自动连接/断开逻辑
        if (UvcReceiver.IsApplicationExiting)
        {
            Logger.Debug($"Skipping OnDeviceChange: Application is exiting (event: {eventType})");
            return;
        }

        // 原有逻辑...
    }
    catch (Exception ex)
    {
        Logger.Error("OnDeviceChange error.", ex);
    }
}
```

### 预期效果

修复后,应用退出时的日志应该是:

```
[INFO] Application exiting, cleaning up...
[DEBUG] Application exiting flag set.
[INFO] Disconnecting from UVC device...
[DEBUG] Waiting for pending callbacks to complete...
[DEBUG] Dispatcher shutdown detected, skipping callback wait. Remaining count=1
[DEBUG] Waiting 100ms for C++ callbacks to exit...
[DEBUG] Calling C++ CloseInput...
[DEBUG] Play state changed: Stopped
[DEBUG] Device change event received: Arrival, Model=GENERAL - UVC
[DEBUG] Skipping OnDeviceChange: Application is exiting (event: Arrival)  ← 不再触发 Connect
[INFO] UVC input closed successfully.
[INFO] UVC disconnected.
```

**不会再有退出时的 Connect 调用!**
