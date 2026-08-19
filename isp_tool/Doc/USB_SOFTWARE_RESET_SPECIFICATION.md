# USB设备软件复位功能实现文档

## 📋 功能概述

通过Windows SetupAPI实现USB设备的软件级复位，模拟物理重新插拔设备的效果，无需手动插拔即可触发系统重新枚举USB设备。

---

## 🎯 应用场景

### 1. **模式切换后的设备重置**
- 当ISP设备切换工作模式（RAW/MJPG/YUV）时
- 自动触发USB设备重新枚举
- 确保设备在新模式下正常工作

### 2. **设备异常恢复**
- 设备无响应时尝试软件复位
- 避免用户手动插拔设备

### 3. **自动化测试**
- 测试设备的重新枚举流程
- 模拟设备断开/连接事件

---

## 🏗️ 架构设计

### 整体流程

```
┌─────────────────────────────────────────────────────────────┐
│                   Config.cs (C#)                             │
│  mode属性变化 → OnCommonConfigChange                         │
│       ↓                                                      │
│  检测 "set_mode" → 选择复位模式                              │
│       ↓                                                      │
│  ┌─────────────────────┐  ┌────────────────────────────┐   │
│  │ 软件USB复位(默认)   │  │ 普通UVC重连(可选)          │   │
│  │ SoftwareResetDevice │  │ Reconnect                  │   │
│  └─────────┬───────────┘  └────────────┬────────────────┘   │
└────────────┼───────────────────────────┼────────────────────┘
             │                           │
             ▼                           ▼
┌────────────────────────┐   ┌────────────────────────┐
│  UvcReceiver.cs (C#)   │   │  UvcReceiver.cs (C#)   │
│                        │   │                        │
│ 1. Disconnect UVC      │   │ 1. Disconnect()        │
│ 2. SoftwareResetUsb    │   │ 2. Task.Delay()        │
│    DeviceEx()          │   │ 3. Connect()           │
│ 3. Wait device ready   │   │                        │
│ 4. Connect UVC         │   │                        │
└──────────┬─────────────┘   └────────────────────────┘
           │
           ▼
┌────────────────────────────────────────────────┐
│  Export.cpp (Device.dll - C++)                  │
│                                                │
│  SoftwareResetUsbDeviceEx()                    │
│       ↓                                         │
│  调用 → UsbDeviceReset.cpp                     │
└──────────┬─────────────────────────────────────┘
           │
           ▼
┌────────────────────────────────────────────────┐
│  UsbDeviceReset.cpp (Device.dll - C++)          │
│                                                │
│  1. ParseDeviceLink()                           │
│     - 解析符号链接提取VID/PID                   │
│       ↓                                         │
│  2. FindDeviceInstance()                        │
│     - SetupDiGetClassDevs() 枚举USB设备         │
│     - SetupDiEnumDeviceInfo() 查找匹配          │
│     - 获取 DEVINST 句柄                         │
│       ↓                                         │
│  3. DisableDeviceInstance()                     │
│     - SetupDiSetClassInstallParams()            │
│     - SetupDiCallClassInstaller(DICS_DISABLE)   │
│       ↓                                         │
│  4. Sleep(waitDisconnectMs)                     │
│     - 等待设备完全断开（默认2000ms）            │
│       ↓                                         │
│  5. EnableDeviceInstance()                      │
│     - SetupDiSetClassInstallParams()            │
│     - SetupDiCallClassInstaller(DICS_ENABLE)    │
│       ↓                                         │
│  6. Sleep(waitConnectMs)                        │
│     - 等待设备重新枚举（默认3000ms）            │
│       ↓                                         │
│  7. CM_Get_DevNode_Status() 验证设备状态        │
└────────────────────────────────────────────────┘
```

---

## 📁 修改文件清单

### 1. **新增文件**

| 文件 | 说明 | 行数 |
|------|------|------|
| `Device/DeviceManager/UsbDeviceReset.cpp` | USB设备复位核心实现 | ~380行 |
| `Device/DeviceManager/UsbDeviceReset.h` | USB设备复位接口声明 | ~60行 |

### 2. **修改文件**

| 文件 | 修改内容 | 行数变化 |
|------|---------|---------|
| `Device/Misc/Export.cpp` | 添加导出函数 | +35行 |
| `ThunderSE/Device/DeviceApi.cs` | 添加P/Invoke声明 | +28行 |
| `ThunderSE/Uvc/UvcReceiver.cs` | 添加SoftwareResetDevice方法 | +95行 |
| `ThunderSE/DeviceConfig/Config.cs` | 修改mode变化处理逻辑 | +60行 |

**总计**：新增 ~540行，修改 ~220行

---

## 🔧 核心API

### C++ 层

#### `SoftwareResetUsbDevice`
```cpp
/**
 * 通过软件方式复位USB设备（模拟重新插拔）
 * 
 * @param deviceSymbolicLink 设备符号链接
 *                           例如：L"\\\\?\\USB#VID_1234&PID_5678#ABC123#{GUID}"
 * @param waitDisconnectMs 断开等待时间（毫秒），默认2000ms
 * @param waitConnectMs 连接等待时间（毫秒），默认3000ms
 * @return 是否成功
 */
extern "C" __declspec(dllexport) bool SoftwareResetUsbDevice(
    const wchar_t* deviceSymbolicLink,
    int waitDisconnectMs = 2000,
    int waitConnectMs = 3000
);
```

#### 简化版
```cpp
extern "C" __declspec(dllexport) bool SoftwareResetUsbDevice(
    const wchar_t* deviceSymbolicLink
);
```

### C# 层

#### `DeviceApi.SoftwareResetUsbDeviceEx`
```csharp
/// <summary>
/// 通过软件方式复位USB设备（模拟重新插拔）
/// 带自定义等待时间
/// </summary>
[DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
public static extern bool SoftwareResetUsbDeviceEx(
    [MarshalAs(UnmanagedType.LPWStr)] string deviceSymbolicLink,
    int waitDisconnectMs = 2000,
    int waitConnectMs = 3000);
```

#### `UvcReceiver.SoftwareResetDevice`
```csharp
/// <summary>
/// 通过软件方式复位USB设备（模拟重新插拔）
/// 比Disconnect/Connect更彻底，会触发系统级的设备重新枚举
/// </summary>
public bool SoftwareResetDevice(
    string deviceSymbolicLink, 
    int waitDisconnectMs = 2000, 
    int waitConnectMs = 3000)
```

---

## 💡 使用示例

### 示例1：在Config.cs中自动触发（已实现）

```csharp
// Config.cs: OnCommonConfigChange
if (commonDataMemberName.StartsWith("set_mode"))
{
    bool useSoftwareReset = true;  // 使用软件复位
    
    _ = Task.Run(async () =>
    {
        string deviceLink = UvcInterface;
        bool success = UvcReceiver.Instance.SoftwareResetDevice(
            deviceLink, 
            waitDisconnectMs: 2000, 
            waitConnectMs: 3000);
        
        if (!success)
        {
            // 显示错误提示
        }
    });
}
```

### 示例2：手动调用软件复位

```csharp
// 在ViewModel或其他地方手动调用
string deviceLink = @"\\?\USB#VID_1234&PID_5678#ABC123#{a5dcbf10-6530-11d2-901f-00c04fb951ed}";

bool success = UvcReceiver.Instance.SoftwareResetDevice(
    deviceLink,
    waitDisconnectMs: 2000,
    waitConnectMs: 3000);

if (success)
{
    Logger.Info("USB device reset completed!");
}
else
{
    Logger.Error("USB device reset failed!");
}
```

### 示例3：切换到普通UVC重连（更快）

```csharp
// Config.cs中修改
bool useSoftwareReset = false;  // 使用普通重连

// 或者动态选择
bool useSoftwareReset = (retryCount > 2);  // 普通重连失败3次后使用软件复位
```

---

## ⚙️ 工作原理

### 1. **设备符号链接解析**

Windows USB设备符号链接格式：
```
\\?\USB#VID_XXXX&PID_YYYY#SERIAL#{GUID}
```

例如：
```
\\?\USB#VID_046D&PID_082D#ABC123#{a5dcbf10-6530-11d2-901f-00c04fb951ed}
```

- `VID_046D`: 供应商ID（Logitech）
- `PID_082D`: 产品ID
- `ABC123`: 序列号
- `{GUID}`: 设备接口GUID

### 2. **SetupAPI调用流程**

```
SetupDiGetClassDevs(USB)
    ↓
SetupDiEnumDeviceInfo() [遍历所有USB设备]
    ↓
SetupDiGetDeviceInstanceId() [获取设备ID]
    ↓
匹配目标设备 → 获取 DEVINST 句柄
    ↓
┌─────────────────────────────────┐
│ 禁用设备                         │
│  SetupDiSetClassInstallParams()  │
│  SetupDiCallClassInstaller(      │
│      DIF_PROPERTYCHANGE,         │
│      DICS_DISABLE)               │
└──────────────┬──────────────────┘
               ↓
          Sleep(2000ms) [等待断开]
               ↓
┌─────────────────────────────────┐
│ 启用设备                         │
│  SetupDiSetClassInstallParams()  │
│  SetupDiCallClassInstaller(      │
│      DIF_PROPERTYCHANGE,         │
│      DICS_ENABLE)                │
└──────────────┬──────────────────┘
               ↓
          Sleep(3000ms) [等待重新枚举]
               ↓
    CM_Get_DevNode_Status() [验证状态]
```

### 3. **系统事件触发**

软件复位会触发以下系统事件：

```
1. DBT_DEVICEREMOVECOMPLETE [设备移除]
   ↓
2. 系统清理设备资源
   ↓
3. DBT_DEVICEARRIVAL [设备到达]
   ↓
4. 系统重新枚举设备
   ↓
5. DeviceManager 收到设备变更通知
```

---

## ⚠️ 注意事项

### 1. **权限要求**
- ✅ **需要管理员权限**
- SetupAPI的设备禁用/启用操作需要提升的权限
- 如果权限不足，函数会返回false

### 2. **等待时间**
| 阶段 | 默认时间 | 说明 |
|------|---------|------|
| 断开等待 | 2000ms | 确保设备完全断开 |
| 连接等待 | 3000ms | 确保设备重新枚举完成 |
| **总计** | **~10秒** | 包括额外的验证时间 |

### 3. **设备不可用期间**
- 软件复位期间设备会**短暂不可用**（约5-10秒）
- 视频流会中断
- 不适合频繁调用

### 4. **与普通重连的区别**

| 特性 | 软件USB复位 | 普通UVC重连 |
|------|------------|------------|
| **彻底性** | ★★★★★ 系统级重置 | ★★★ 应用层重置 |
| **速度** | 慢（~10秒） | 快（~3秒） |
| **触发系统事件** | ✅ 是 | ❌ 否 |
| **解决驱动问题** | ✅ 能 | ❌ 不能 |
| **适用场景** | 严重异常、模式切换 | 普通重连、视频流问题 |

### 5. **失败处理**

如果软件复位失败：
1. 检查管理员权限
2. 验证设备符号链接格式
3. 查看系统日志（事件查看器）
4. 尝试物理重新插拔设备

---

## 🧪 测试建议

### 1. **基本功能测试**
- [ ] 正常调用软件复位
- [ ] 验证设备重新枚举
- [ ] 检查视频流恢复

### 2. **模式切换测试**
- [ ] RAW → MJPG 切换
- [ ] MJPG → YUV 切换
- [ ] YUV → RAW 切换

### 3. **压力测试**
- [ ] 连续5次软件复位
- [ ] 在视频流播放中复位
- [ ] 在录制过程中复位

### 4. **异常场景测试**
- [ ] 无管理员权限
- [ ] 无效设备链接
- [ ] 设备已拔出

---

## 📊 日志输出示例

### 成功的软件复位

```
[2026-04-14 15:30:01.123] [INFO] [T03] ========================================
[2026-04-14 15:30:01.124] [INFO] [T03] Mode changed detected!
[2026-04-14 15:30:01.124] [INFO] [T03] Mode member: set_mode
[2026-04-14 15:30:01.124] [INFO] [T03] UVC interface: \\?\USB#VID_1234&PID_5678#ABC123#{...}
[2026-04-14 15:30:01.124] [INFO] [T03] Reset mode: Software USB Reset
[2026-04-14 15:30:01.124] [INFO] [T03] ========================================
[2026-04-14 15:30:01.125] [INFO] [T03] Starting software USB reset...
[2026-04-14 15:30:01.126] [INFO] [T03] Step 1: Disconnecting UVC stream first...
[2026-04-14 15:30:01.500] [INFO] [T03] UVC stream disconnected successfully.
[2026-04-14 15:30:02.001] [INFO] [T03] Step 2: Calling C++ SoftwareResetUsbDevice...
[2026-04-14 15:30:02.002] [INFO] [T03] [USB-Reset] Starting USB device software reset...
[2026-04-14 15:30:02.003] [INFO] [T03] [USB-Reset] Device: \\?\USB#VID_1234...
[2026-04-14 15:30:02.100] [INFO] [T03] [USB-Reset] Step 1: Disabling device...
[2026-04-14 15:30:02.150] [INFO] [T03] [USB-Reset] Device disabled successfully
[2026-04-14 15:30:02.151] [INFO] [T03] [USB-Reset] Step 2: Waiting 2000 ms...
[2026-04-14 15:30:04.152] [INFO] [T03] [USB-Reset] Step 3: Re-enabling device...
[2026-04-14 15:30:04.200] [INFO] [T03] [USB-Reset] Device enabled successfully
[2026-04-14 15:30:04.201] [INFO] [T03] [USB-Reset] Step 4: Waiting 3000 ms...
[2026-04-14 15:30:07.202] [INFO] [T03] [USB-Reset] Device reset successfully!
[2026-04-14 15:30:07.203] [INFO] [T03] ✓ USB device reset completed successfully.
[2026-04-14 15:30:08.204] [INFO] [T03] Step 3: Reconnecting UVC stream...
[2026-04-14 15:30:08.500] [INFO] [T03] UVC connected successfully: 1920x1080
[2026-04-14 15:30:08.501] [INFO] [T03] ✓ Software reset completed! Device reconnected: 1920x1080
[2026-04-14 15:30:08.501] [INFO] [T03] ========================================
```

---

## 🚀 下一步优化建议

### 1. **自动降级策略**
```csharp
// 先尝试普通重连，失败后使用软件复位
bool success = await UvcReceiver.Instance.Reconnect(...);
if (!success)
{
    Logger.Warn("UVC reconnect failed, trying software reset...");
    success = UvcReceiver.Instance.SoftwareResetDevice(...);
}
```

### 2. **异步非阻塞版本**
```csharp
public async Task<bool> SoftwareResetDeviceAsync(...)
{
    // 使用 Task.Run 包装C++调用
    return await Task.Run(() => SoftwareResetDevice(...));
}
```

### 3. **设备链接自动获取**
```csharp
// 从 DeviceLocation 自动构造完整链接
string deviceLink = DeviceApi.GetDeviceSymbolicLink(DeviceLocation);
```

### 4. **进度回调**
```csharp
// 提供进度反馈
SoftwareResetDevice(..., progress => {
    Console.WriteLine($"Progress: {progress}%");
});
```

---

## 📚 参考资料

- [Windows SetupAPI Documentation](https://docs.microsoft.com/en-us/windows-hardware/drivers/install/setupapi)
- [CM_Get_DevNode_Status Function](https://docs.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_devnode_status)
- [SetupDiCallClassInstaller Function](https://docs.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdicallclassinstaller)
- [USB Device Interface GUIDs](https://docs.microsoft.com/en-us/windows-hardware/drivers/install/guid-devinterface-usb-device)

---

**实现日期**：2026-04-14  
**版本**：1.0  
**状态**：待测试
