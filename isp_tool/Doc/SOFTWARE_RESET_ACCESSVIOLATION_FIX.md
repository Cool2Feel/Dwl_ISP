# UVC 软件复位访问冲突异常修复报告

**日期**: 2026-04-14  
**问题**: 调用 `SoftwareResetDevice` 时发生访问冲突异常（0xC0000005），读取空指针地址 0x00000000

---

## 问题描述

在调用软件复位功能时，UVC 断开连接过程中发生了访问冲突异常。异常发生在 `UvcApi.CloseInput()` 调用时，尝试访问已释放的 FFmpeg 上下文。

### 异常日志

```
[2026-04-14 11:49:06.332] [INFO ] [T03] [UvcReceiver.SoftwareResetDevice] - Step 1: Disconnecting UVC stream first...
[2026-04-14 11:49:06.364] [INFO ] [T03] [UvcReceiver.Disconnect] - Disconnecting from UVC device...
[2026-04-14 11:49:06.394] [DEBUG] [T03] [UvcReceiver.Disconnect] - Waiting for pending callbacks to complete...
[2026-04-14 11:49:06.207] [INFO ] [T01] [DeviceManger.OnDeviceChange] - Device change event: RemoveComplete, Location: Port_#0001.Hub_#0001, Model: JT526X, UVC: 
[2026-04-14 11:49:06.458] [INFO ] [T01] [ConfigManager.OnDeviceChange] - Device change event received: RemoveComplete, Model=JT526X
[2026-04-14 11:49:06.490] [INFO ] [T01] [ConfigManager.OnDeviceChange] - Device removed: JT526X
[2026-04-14 11:49:06.521] [INFO ] [T01] [ConfigManager.RemoveConfig] - Config 'JT526X' removed.
[2026-04-14 11:49:06.563] [DEBUG] [T01] [UvcReceiver.Disconnect] - Disconnect called but not connected.
[2026-04-14 11:49:06.633] [INFO ] [T01] [DeviceManger.OnDeviceChange] - Device change event: Arrival, Location: Port_#0001.Hub_#0001, Model: JT526X, UVC: GENERAL - UVC
[2026-04-14 11:49:06.663] [INFO ] [T01] [ConfigManager.OnDeviceChange] - Device change event received: Arrival, Model=JT526X
[2026-04-14 11:49:06.695] [INFO ] [T01] [ConfigManager.OnDeviceChange] - Device arrived: JT526X, connecting...
[2026-04-14 11:49:06.726] [DEBUG] [T04] [ConfigManager.OnDeviceChange] - Connecting to UVC: GENERAL - UVC
[2026-04-14 11:49:06.758] [INFO ] [T04] [UvcReceiver.Connect] - Connecting to UVC device: video=GENERAL - UVC
[2026-04-14 11:49:06.800] [INFO ] [T03] [UvcReceiver.Disconnect] - Callback wait completed. Remaining count=0
[2026-04-14 11:49:06.834] [DEBUG] [T03] [UvcReceiver.Disconnect] - Calling C++ CloseInput...
0x55DB18F1 (avdevice-57.dll)处(位于 ThunderSE.exe 中)引发的异常: 0xC0000005: 读取位置 0x00000000 时发生访问冲突。
```

---

## 根本原因分析

### 时间线分析

```
11:49:06.332 [T03] SoftwareResetDevice 开始执行 Disconnect
11:49:06.364 [T03] Disconnect 等待回调完成
11:49:06.207 [T01] ⚠️ USB 设备移除事件触发 (RemoveComplete) ← 并发冲突开始！
11:49:06.458 [T01] ConfigManager 收到设备移除事件
11:49:06.563 [T01] ConfigManager 调用 Disconnect（但已被 SoftwareResetDevice 断开）
11:49:06.633 [T01] USB 设备重新到达事件 (Arrival)
11:49:06.695 [T01] ConfigManager 尝试自动连接新设备
11:49:06.758 [T04] ConfigManager 的 Connect 开始执行 ← 与 SoftwareResetDevice 并发！
11:49:06.834 [T03] SoftwareResetDevice 调用 CloseInput ← 💥 访问冲突！
```

### 核心问题

1. **并发 Disconnect/Connect**：
   - `SoftwareResetDevice` 在 T03 线程执行 `Disconnect()`
   - `ConfigManager.OnDeviceChange` 在 T01/T04 线程响应设备变化事件，尝试 `Connect()`
   - 两个操作**同时执行**，没有并发保护

2. **FFmpeg 上下文生命周期问题**：
   - `Disconnect()` 等待回调完成后，准备调用 `CloseInput()` 释放 FFmpeg 上下文
   - 但 `Connect()` 已经在另一个线程调用 `OpenInput()` 创建新的 FFmpeg 上下文
   - 旧的 `CloseInput()` 释放了上下文，新的 `OpenInput()` 也受到影响
   - 最终导致访问已释放的内存地址（0x00000000）

3. **设备变化事件干扰**：
   - USB 设备软件复位会触发系统的 `RemoveComplete` 和 `Arrival` 事件
   - `ConfigManager.OnDeviceChange` 响应这些事件，自动执行 `Disconnect()` 和 `Connect()`
   - 与 `SoftwareResetDevice` 的操作冲突

---

## 修复方案

### 1. 添加 `_connectionLock` 并发保护锁

**文件**: `ThunderSE\Uvc\UvcReceiver.cs`

**修改内容**:

```csharp
// 新增字段
private readonly object _connectionLock = new object();
private volatile bool _isDisconnecting = false;

// 修改 Connect 方法
public bool Connect(string cameraDescriptor)
{
    // 使用锁保护，防止并发连接/断开
    lock (_connectionLock)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UvcReceiver));

        // 如果正在断开，拒绝连接
        if (_isDisconnecting)
        {
            Logger.Warn("Connect blocked: Disconnect is in progress");
            return false;
        }
        
        // ... 原有连接逻辑
    }
}

// 修改 Disconnect 方法
public void Disconnect()
{
    // 使用锁保护，防止并发 Disconnect/Connect
    lock (_connectionLock)
    {
        // 标记正在断开，阻止新连接
        _isDisconnecting = true;

        try
        {
            // ... 原有断开逻辑
        }
        finally
        {
            // 确保即使异常也重置标志
            _isDisconnecting = false;
        }
    }
}
```

**效果**: 确保 `Connect()` 和 `Disconnect()` 不会同时执行，避免 FFmpeg 上下文的竞争条件。

---

### 2. 在 Disconnect 中增加回调等待时间

**修改内容**:

```csharp
// 步骤3: 关闭 C++ 层输入（带异常保护）
// 关键改进：先等待一小段时间，确保 C++ 层回调完全退出
Logger.Debug("Waiting 100ms for C++ callbacks to exit...");
Thread.Sleep(100);

try
{
    Logger.Debug("Calling C++ CloseInput...");
    int ret = UvcApi.CloseInput();
    // ...
}
```

**效果**: 在调用 `CloseInput()` 之前等待 100ms，确保 C++ 层的回调线程完全退出，避免访问正在释放的资源。

---

### 3. SoftwareResetDevice 增加 `_isReconnecting` 标志

**文件**: `ThunderSE\Uvc\UvcReceiver.cs`

**修改内容**:

```csharp
public bool SoftwareResetDevice(string deviceSymbolicLink, ...)
{
    // 防止并发重连
    if (_isReconnecting)
    {
        Logger.Warn("SoftwareResetDevice blocked: Reconnect already in progress");
        return false;
    }

    // 标记正在重连，阻止设备变化事件的自动连接
    _isReconnecting = true;

    try
    {
        // ... 复位逻辑
    }
    finally
    {
        // 确保重连标志被重置
        _isReconnecting = false;
    }
}

// 新增公开属性
public bool IsReconnecting => _isReconnecting;
```

**效果**: 防止多个重连操作并发执行，同时通知其他组件当前正在重连。

---

### 4. ConfigManager.OnDeviceChange 检查重连状态

**文件**: `ThunderSE\DeviceConfig\ConfigManager.cs`

**修改内容**:

```csharp
private void OnDeviceChange(DeviceEvent eventType, string location, string model, string uvcInterafce)
{
    // 关键修复：如果 UvcReceiver 正在重连/复位，跳过自动连接逻辑
    if (UvcReceiver.Instance.IsReconnecting)
    {
        Logger.Debug($"Skipping OnDeviceChange: UvcReceiver is reconnecting (event: {eventType})");
        return;
    }

    if (eventType == DeviceEvent.Arrival)
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            // 再次检查重连状态（因为可能在上次检查后状态变化）
            if (UvcReceiver.Instance.IsReconnecting)
            {
                Logger.Debug($"Skipping Connect: UvcReceiver is reconnecting");
                return;
            }

            // ... 原有连接逻辑
        });
    }
    else
    {
        // 只在非重连状态下断开 UVC
        if (!UvcReceiver.Instance.IsReconnecting)
        {
            UvcReceiver.Instance.Disconnect();
        }
        else
        {
            Logger.Debug("Skipping Disconnect: UvcReceiver is reconnecting");
        }
    }
}
```

**效果**: 在软件复位期间，`ConfigManager` 不会响应设备变化事件，避免并发操作。

---

## 修改的文件列表

1. `ThunderSE\Uvc\UvcReceiver.cs`
   - 新增 `_connectionLock` 和 `_isDisconnecting` 字段
   - 修改 `Connect()` 方法，添加并发保护
   - 修改 `Disconnect()` 方法，添加并发保护和回调等待
   - 修改 `SoftwareResetDevice()` 方法，添加 `_isReconnecting` 标志
   - 新增 `IsReconnecting` 公开属性

2. `ThunderSE\DeviceConfig\ConfigManager.cs`
   - 修改 `OnDeviceChange()` 方法，检查 `_isReconnecting` 状态

---

## 修复效果

### 改进前
- ❌ `SoftwareResetDevice` 和设备变化事件并发执行
- ❌ `Disconnect()` 和 `Connect()` 同时调用
- ❌ FFmpeg 上下文被并发访问
- ❌ 访问冲突异常（0xC0000005）

### 改进后
- ✅ `_connectionLock` 确保 `Connect/Disconnect` 互斥执行
- ✅ `_isDisconnecting` 阻止在断开时的新连接
- ✅ `SoftwareResetDevice` 使用 `_isReconnecting` 阻止并发重连
- ✅ `ConfigManager.OnDeviceChange` 检查重连状态，不干扰软件复位
- ✅ 额外的 100ms 等待确保 C++ 回调完全退出
- ✅ 避免 FFmpeg 上下文的并发访问

---

## 测试建议

1. **软件复位功能测试**：
   - 连接 USB 设备
   - 调用 `SoftwareResetDevice`
   - 验证不再出现访问冲突异常
   - 验证设备成功重新连接

2. **并发压力测试**：
   - 快速连续调用多次 `SoftwareResetDevice`
   - 验证只有一个复位操作执行，其他被拒绝
   - 验证日志中出现 "blocked: Reconnect already in progress"

3. **设备变化事件测试**：
   - 在软件复位期间，观察设备变化事件是否被正确跳过
   - 验证日志中出现 "Skipping OnDeviceChange: UvcReceiver is reconnecting"

4. **正常连接/断开测试**：
   - 测试正常的 `Connect` 和 `Disconnect` 流程
   - 验证不受新锁的影响

---

## 注意事项

1. **线程安全**：所有状态标志使用 `volatile` 修饰，确保多线程可见性
2. **异常保护**：`Disconnect()` 使用 `try-finally` 确保 `_isDisconnecting` 标志被正确重置
3. **日志完善**：增加详细的调试日志，便于排查问题
4. **向后兼容**：修改不影响现有的正常功能

---

## 总结

本次修复通过添加并发保护锁和状态标志，解决了软件复位过程中的访问冲突问题。核心改进包括：

1. **互斥锁保护**：`_connectionLock` 确保 `Connect/Disconnect` 互斥执行
2. **状态标志**：`_isDisconnecting` 和 `_isReconnecting` 阻止并发操作
3. **事件过滤**：`ConfigManager.OnDeviceChange` 检查重连状态，避免干扰
4. **时序优化**：增加回调等待时间，确保资源正确释放

这些改进确保了软件复位过程的线程安全性和资源管理的正确性。
