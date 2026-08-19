# UVC 重连卡死问题修复报告

**日期**: 2026-04-14  
**问题**: UVC 设备重连时程序卡住，无法继续执行  
**严重程度**: 🔴 高危（导致程序无响应）

---

## 问题现象

从日志可以看出：

```
[14:05:44.885] [T09] 等待2500ms for complete resource cleanup...
[14:05:44.885] [T10] Connect - Connecting to UVC device: video=GENERAL - UVC
[14:05:47.495] [T09] Connection attempt 1/3 for: GENERAL - UVC  ← 卡住
```

**关键时间线**：
1. `14:05:44.885` - T09 开始等待 2500ms
2. `14:05:44.885` - T10 **同时**调用 Connect（同一时刻！）
3. `14:05:47.495` - T09 等待结束后尝试重连 → **卡住不动**

---

## 根本原因

### 问题 1: Connect 内部死锁

**代码位置**: `UvcReceiver.Connect()` (第 178-231 行)

```csharp
public bool Connect(string cameraDescriptor)
{
    lock (_connectionLock)  // ← 获取锁
    {
        if (_isConnected)
        {
            Logger.Warn("Already connected, disconnecting first...");
            Disconnect();  // ← 问题！Disconnect 内部也要获取 _connectionLock
        }
        // ...
    }
}

public void Disconnect()
{
    lock (_connectionLock)  // ← 尝试获取同一把锁
    {
        // ...
    }
}
```

**死锁场景**：
1. T10 调用 `Connect` → 获取 `_connectionLock`
2. T10 发现 `_isConnected = true`（可能由其他线程设置）
3. T10 调用 `Disconnect()` → **尝试再次获取 `_connectionLock`** → **死锁！**

### 问题 2: 竞态条件

从日志时间戳可以看出：
- `14:05:44.885` 同一时刻有两个线程在活动
- T09 执行 `Reconnect`（在 `await Task.Delay` 中）
- T10 执行 `Connect`（来源不明，可能是设备热插拔事件或其他触发源）

两个线程**并发操作 UVC 连接状态**，导致资源竞争和卡死。

### 问题 3: 缺少连接状态检查

`Reconnect` 方法在 `await Task.Delay(2500ms)` 后，**没有检查等待期间是否已有其他线程建立了连接**，直接尝试再次连接。

---

## 修复方案

### 修复 1: 解除 Connect 内部死锁

**文件**: `ThunderSE\Uvc\UvcReceiver.cs`

**改动**: `Connect` 方法中"已连接"处理逻辑

```csharp
// 修复前（死锁）:
if (_isConnected)
{
    Disconnect();  // ← 获取同一把锁 → 死锁
}

// 修复后（无锁）:
if (_isConnected)
{
    // 直接重置状态，不调用 Disconnect()
    _isConnected = false;
    Interlocked.Exchange(ref _receivePacketCount, 0);
    
    // 直接关闭 C++ 层输入（不获取 Disconnect 的锁）
    try
    {
        int ret = UvcApi.CloseInput();
        Thread.Sleep(300);  // 等待资源释放
    }
    catch (Exception ex)
    {
        Logger.Warn($"Exception: {ex.Message}");
    }
}
```

**原理**: 
- 避免在 `lock (_connectionLock)` 内部调用 `Disconnect()`
- 直接操作状态和调用 C++ API，不获取第二层锁
- 保持异常保护，防止崩溃

### 修复 2: Reconnect 等待后二次检查

**文件**: `ThunderSE\Uvc\UvcReceiver.cs`

**改动**: `Reconnect` 方法中 `await Task.Delay` 后增加检查

```csharp
await Task.Delay(totalWaitMs);

// 关键改进：等待期间检查是否被其他线程连接
if (_isConnected)
{
    Logger.Info("Connection established during wait, skipping explicit reconnect");
    return true;  // 直接返回成功，避免重复断开/连接
}
```

**原理**:
- 如果在等待期间已有连接建立，说明其他代码路径已处理
- 避免重复断开/连接的循环

### 修复 3: Config.cs 增加连接状态检查

**文件**: `ThunderSE\DeviceConfig\Config.cs`

**改动**: `OnCommonConfigChange` 方法中增加连接状态检查

```csharp
// 额外检查：如果 UVC 正在断开，跳过此次操作
if (!UvcReceiver.Instance.IsConnected)
{
    Logger.Warn("Skipping mode change: UVC is not connected, waiting for reconnect");
    return;
}
```

**原理**:
- 防止在 UVC 未连接时触发新的重连请求
- 与 `IsReconnecting` 检查配合，形成完整的状态保护

---

## 修复效果

### 修复前

```
T09: Reconnect → Disconnect → await 2500ms
T10: Connect → 获取 _connectionLock → 发现已连接 → 调用 Disconnect → 死锁！
T09: await 结束 → 尝试 Connect → 卡住等待 T10 释放锁
```

**结果**: 程序卡死，无法继续执行

### 修复后

```
T09: Reconnect → Disconnect → await 2500ms
T10: Connect → 获取 _connectionLock → 发现已连接 → 直接关闭（不调用 Disconnect）→ 释放锁
T09: await 结束 → 检查 _isConnected → 发现已连接 → 直接返回成功
```

**结果**: 正常完成，无死锁

---

## 测试建议

### 1. 快速模式切换测试
- **操作**: 连续快速切换多个 `set_mode` 值（5次/秒）
- **预期**: 只有第一次触发重连，后续被防抖机制拦截
- **日志检查**: 应出现 "Mode change debounced" 和 "UVC is already reconnecting"

### 2. 热插拔并发测试
- **操作**: 在 UVC 重连等待期间，物理拔插设备
- **预期**: 不会卡死，能正确识别设备状态变化
- **日志检查**: 应出现 "Connection established during wait, skipping explicit reconnect"

### 3. 长时间稳定性测试
- **操作**: 连续运行 24 小时，每小时触发 10 次模式切换
- **预期**: 无内存泄漏、无死锁、无崩溃
- **日志检查**: 无重复断开/连接循环

---

## 相关文件

| 文件 | 改动 |
|------|------|
| `ThunderSE\Uvc\UvcReceiver.cs` | 修复 Connect 死锁、Reconnect 二次检查 |
| `ThunderSE\DeviceConfig\Config.cs` | 增加连接状态检查 |

---

## 注意事项

1. **C++ 层超时**: 当前 `UvcApi.OpenInputSafe` 是同步阻塞调用，**没有超时机制**。如果 C++ 层 FFmpeg 初始化卡住，会导致 C# 层一直等待。建议后续在 C++ 层添加超时保护。

2. **日志增强**: 修复后增加了更多调试日志（如 `Calling UvcApi.OpenInputSafe`、`OpenInputSafe returned`），便于排查卡住问题。

3. **状态一致性**: 直接操作 `_isConnected` 标志而不通过 `Disconnect()` 可能导致状态不一致（如事件未触发）。需要观察实际运行情况，如有问题需进一步优化。

---

## 后续改进建议

1. **C++ 层超时**: 在 `Uvc.dll` 的 `OpenInput` 函数中添加超时机制（例如 10 秒）
2. **异步 Connect**: 将 `Connect` 改为异步方法，使用 `Task.Run` 包装 C++ 调用
3. **状态机**: 引入 UVC 状态机（Disconnected → Connecting → Connected → Disconnecting），避免状态混乱
4. **超时保护**: 为所有 C++ P/Invoke 调用添加超时包装器

---

**修复完成时间**: 2026-04-14 14:30  
**修复人员**: AI Assistant  
**验证状态**: ⏳ 待用户测试验证
