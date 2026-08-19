# UVC 重连 AccessViolationException 修复报告

**日期**: 2026-04-14  
**问题**: UVC 设备重连时发生 `0xC0000005: 访问冲突` 导致程序崩溃

---

## 🔴 问题描述

### 错误日志

```
[2026-04-14 13:44:29.556] [INFO ] [T04] [Config.OnCommonConfigChange] - Mode changed detected!
[2026-04-14 13:44:29.740] [INFO ] [T04] [Config.OnCommonConfigChange] - Reset mode: UVC Reconnect
0x6D4C33DE (vcruntime140_clr0400.dll)处(位于 ThunderSE.exe 中)引发的异常: 0xC0000005: 读取位置 0x12035000 时发生访问冲突。
公共语言运行时无法在此异常处停止。通常的原因包括: COM 互操作封送错误和内存损坏。
引发的异常:"System.AccessViolationException"(位于 mscorlib.dll 中)
"System.AccessViolationException"类型的未经处理的异常在 mscorlib.dll 中发生 
尝试读取或写入受保护的内存。这通常指示其他内存已损坏。
```

### 触发场景

在用户模式效果标签页中切换 ISP 输出模式（RAW/MJPG/YUV）时，系统需要重新连接 UVC 视频流，但在重连过程中发生内存访问冲突。

---

## 🔍 根本原因分析

### 1. **async void 导致异常无法捕获** ⚠️

`OnCommonConfigChange` 是 `async void` 事件处理器，当内部 Task.Run 发生 AccessViolationException 时：

```csharp
async void OnCommonConfigChange(object sender, PropertyChangedEventArgs e)
{
    // ...
    _ = Task.Run(async () => {
        await UvcReceiver.Instance.Reconnect(...);  // 这里可能抛出 AccessViolationException
    });
}
```

**问题**：`async void` 方法的异常无法被调用方捕获，会直接逃逸到 CLR 级别，导致程序崩溃。

### 2. **P/Invoke 字符串封送问题** ⚠️

UvcApi.OpenInput 使用自动封送：

```csharp
[DllImport("uvc.dll", CallingConvention = CallingConvention.Cdecl)]
public static extern int OpenInput(
    [MarshalAs(UnmanagedType.LPStr)] string filePath,
    ref int videoWidth, ref int videoHeight);
```

**问题**：在 `Task.Run` 异步执行时，C# 的 `string` 对象可能被 GC 移动或回收，导致 C++ 层访问到无效内存地址 `0x12035000`。

### 3. **并发重入竞争** ⚠️

`OnCommonConfigChange` 是事件处理器，可能在短时间内被多次触发（例如用户快速切换模式），导致：

- 多个 Task.Run 并发执行 Reconnect
- C++ 层的 FFmpeg 资源被多次释放或访问已释放的内存

### 4. **C++ 层资源未完全释放** ⚠️

`Disconnect()` 后立即调用 `Connect()`，C++ 层的 FFmpeg 资源（解码器、缓冲区）可能还未完全释放，导致访问已释放的内存。

原有等待时间：`retryDelayMs + 500ms`（仅 1500ms）

---

## ✅ 修复方案

### 修复 1: 添加最外层异常保护

**文件**: `ThunderSE/DeviceConfig/Config.cs`

```csharp
async void OnCommonConfigChange(object sender, PropertyChangedEventArgs e)
{
    try  // ← 新增：最外层异常捕获
    {
        // ... 原有逻辑 ...
        
        _ = Task.Run(async () =>
        {
            try
            {
                // ... Reconnect 逻辑 ...
            }
            catch (AccessViolationException avEx)  // ← 新增：专门捕获内存访问异常
            {
                Logger.Error($"Mode change handler AccessViolationException: {avEx.Message}", avEx);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("UVC 设备内存访问异常...", "UVC 内存访问异常", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Mode change handler exception: {ex.GetType().Name} - {ex.Message}", ex);
            }
        });
    }
    catch (Exception outerEx)  // ← 新增：async void 最外层保护
    {
        Logger.Error($"OnCommonConfigChange outer exception: {outerEx.GetType().Name} - {outerEx.Message}", outerEx);
    }
}
```

**效果**：即使发生 AccessViolationException，也不会直接崩溃到 CLR，而是被捕获并记录日志。

### 修复 2: 防抖机制（Debounce）

**文件**: `ThunderSE/DeviceConfig/Config.cs`

```csharp
// 类成员
private DateTime _lastModeChangeTime = DateTime.MinValue;
private const int ModeChangeDebounceMs = 2000;  // 2秒内只允许一次模式切换
private readonly object _modeChangeLock = new object();

// 在 OnCommonConfigChange 中
if (commonDataMemberName.StartsWith("set_mode"))
{
    // 防抖检查
    lock (_modeChangeLock)
    {
        var now = DateTime.Now;
        var timeSinceLastChange = (now - _lastModeChangeTime).TotalMilliseconds;
        if (timeSinceLastChange < ModeChangeDebounceMs)
        {
            Logger.Warn($"Mode change debounced: {(ModeChangeDebounceMs - timeSinceLastChange):F0}ms remaining");
            return;  // 忽略此次触发
        }
        _lastModeChangeTime = now;
    }
    
    // ... 继续执行 ...
}
```

**效果**：防止用户快速切换模式导致多次并发重连。

### 修复 3: UVC 重连状态检查

**文件**: `ThunderSE/DeviceConfig/Config.cs`

```csharp
if (commonDataMemberName.StartsWith("set_mode"))
{
    // 防抖检查...
    
    // 新增：检查 UVC 是否正在重连
    if (UvcReceiver.Instance.IsReconnecting)
    {
        Logger.Warn("Skipping mode change: UVC is already reconnecting");
        return;
    }
    
    // ... 继续执行 ...
}
```

**效果**：避免在 UVC 已经在重连时再次触发重连。

### 修复 4: 安全的字符串封送

**文件**: `ThunderSE/Uvc/UvcApi.cs`

```csharp
// 原始声明改为私有
[DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
private static extern int OpenInput(
    IntPtr filePath,  // ← 改为 IntPtr
    ref int videoWidth, ref int videoHeight);

/// <summary>
/// 打开UVC输入流（安全版本，固定字符串防止GC移动）
/// </summary>
public static int OpenInputSafe(string filePath, ref int videoWidth, ref int videoHeight)
{
    IntPtr hGlobal = IntPtr.Zero;
    try
    {
        // 将字符串转换为 ANSI 并固定到非托管内存
        hGlobal = Marshal.StringToHGlobalAnsi(filePath);
        return OpenInput(hGlobal, ref videoWidth, ref videoHeight);
    }
    finally
    {
        // 释放非托管内存
        if (hGlobal != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(hGlobal);
        }
    }
}
```

**文件**: `ThunderSE/Uvc/UvcReceiver.cs`

```csharp
// 修改 Connect 方法
int ret = UvcApi.OpenInputSafe(inputPath, ref width, ref height);  // ← 使用安全版本
```

**效果**：字符串在调用期间被固定到非托管内存，GC 无法移动，避免 C++ 层访问无效地址。

### 修复 5: 增加 C++ 资源等待时间

**文件**: `ThunderSE/Uvc/UvcReceiver.cs`

```csharp
// 步骤2: 等待资源释放（异步，不阻塞调用线程）
// 增加等待时间，确保 C++ 层资源完全释放（FFmpeg 需要更长时间）
int totalWaitMs = retryDelayMs + 1000;  // ← 从 500ms 增加到 1000ms
Logger.Debug($"Waiting {totalWaitMs}ms for complete resource cleanup...");
await Task.Delay(totalWaitMs);
```

**效果**：给 FFmpeg 更长时间来释放资源，避免访问已释放的内存。

### 修复 6: 增加重连延迟

**文件**: `ThunderSE/DeviceConfig/Config.cs`

```csharp
success = await UvcReceiver.Instance.Reconnect(UvcInterface,
    retryCount: 2,
    retryDelayMs: 1500);  // ← 从 1000ms 增加到 1500ms
```

**效果**：每次重试之间等待更长时间，降低并发压力。

---

## 📊 修复效果对比

| 指标 | 修复前 | 修复后 |
|------|--------|--------|
| 异常捕获 | ❌ 直接崩溃到 CLR | ✅ 完整捕获并记录日志 |
| 并发重连 | ❌ 无限制 | ✅ 防抖 2 秒 + IsReconnecting 检查 |
| 字符串封送 | ❌ GC 可能移动 | ✅ 固定到非托管内存 |
| 资源等待 | ⚠️ 1500ms | ✅ 2500ms |
| 重连延迟 | ⚠️ 1000ms | ✅ 1500ms |

---

## 🎯 验证建议

### 1. 快速切换模式测试

```
步骤：
1. 打开应用，连接设备
2. 在效果标签页中快速切换输出模式（RAW → MJPG → YUV）
3. 每 0.5 秒切换一次，连续 10 次

预期：
- 只执行 1-2 次重连（防抖生效）
- 无 AccessViolationException
- 日志显示 "Mode change debounced"
```

### 2. 长时间稳定性测试

```
步骤：
1. 连接设备，正常运行
2. 每 5 秒切换一次模式，持续 30 分钟

预期：
- 无内存泄漏
- 无崩溃
- 视频流正常恢复
```

### 3. 边界情况测试

```
步骤：
1. 连接设备
2. 在 UVC 正在重连时再次切换模式

预期：
- 日志显示 "Skipping mode change: UVC is already reconnecting"
- 不触发新的重连
```

---

## 📝 修改文件清单

| 文件 | 修改内容 |
|------|---------|
| `ThunderSE/DeviceConfig/Config.cs` | 添加防抖字段、异常保护、UVC 状态检查 |
| `ThunderSE/Uvc/UvcApi.cs` | 添加 OpenInputSafe 安全封送方法 |
| `ThunderSE/Uvc/UvcReceiver.cs` | 使用 OpenInputSafe，增加资源等待时间 |

---

## ⚠️ 注意事项

1. **不要移除 AccessViolationException 捕获**  
   虽然这不是最佳实践，但在 P/Invoke 场景下是必要的安全网。

2. **防抖时间可根据实际情况调整**  
   当前设置为 2 秒，如果用户反馈切换太慢，可以降低到 1 秒。

3. **监控日志**  
   观察是否还有 AccessViolationException，如果有，可能需要检查 C++ 层代码。

4. **OpenInputSafe 的性能**  
   每次调用都会分配/释放非托管内存，但频率很低（只在模式切换时），性能影响可忽略。

---

## 🔧 后续优化建议

1. **C++ 层代码审查**  
   检查 `uvc.dll` 的 `OpenInput` 实现，确保：
   - 不保存 `filePath` 指针
   - 完全复制字符串后再使用
   - 正确释放所有资源

2. **考虑使用 CancellationToken**  
   在 Reconnect 时传入 CancellationToken，允许用户取消重连操作。

3. **添加重连超时**  
   如果 Reconnect 超过 30 秒，主动放弃并提示用户检查设备。

4. **单元测试**  
   为 UvcApi.OpenInputSafe 编写单元测试，验证字符串封送的正确性。

---

**修复完成时间**: 2026-04-14  
**修复人员**: AI Assistant  
**审核状态**: 待验证
