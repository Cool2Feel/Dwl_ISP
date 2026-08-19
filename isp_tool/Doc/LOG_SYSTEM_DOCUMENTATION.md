# ThunderSE 调试日志系统使用文档

## 概述

本项目已集成完整的调试日志系统，支持分级日志记录、文件输出和线程安全写入。

## 日志系统特性

### 1. 日志级别

系统支持5个日志级别，按严重程度递增：

- **Debug** - 详细调试信息（默认启用）
- **Info** - 一般信息，操作状态
- **Warn** - 警告信息，非致命问题
- **Error** - 错误信息，异常和失败
- **Fatal** - 致命错误，可能导致程序崩溃

### 2. 日志输出

日志同时输出到两个目标：

- **文件日志** - 保存在 `logs/` 目录下，按日期分割文件
- **Debug窗口** - Visual Studio输出窗口（仅Debug模式）

### 3. 日志格式

```
[时间戳] [级别] [线程ID] [类名.方法] - 消息
```

示例：
```
[2026-04-10 14:23:45.123] [INFO ] [T01] [IspToolApp.OnStartup] - ========== ThunderSE Application Starting ==========
[2026-04-10 14:23:45.456] [DEBUG] [T01] [DeviceManger..ctor] - Initializing DeviceManger...
[2026-04-10 14:23:46.789] [ERROR] [T03] [UvcReceiver.Connect] - Failed to open UVC input: -1, descriptor: video=USB Camera
```

### 4. 日志文件

- **目录**: `logs/` （相对于应用程序执行目录）
- **命名格式**: `ThunderSE_YYYY-MM-DD.log`
- **编码**: UTF-8
- **分割**: 按日期自动分割

示例文件结构：
```
项目根目录/
├── Debug/
│   └── logs/
│       ├── ThunderSE_2026-04-10.log
│       ├── ThunderSE_2026-04-11.log
│       └── ...
└── Release/
    └── logs/
        └── ThunderSE_2026-04-10.log
```

## 使用方式

### 在代码中使用

Logger是静态类，可直接调用：

```csharp
using ThunderSE.Common;

// 记录Debug日志
Logger.Debug("详细调试信息");

// 记录Info日志
Logger.Info("操作成功");

// 记录Warn日志
Logger.Warn("配置项不存在，使用默认值");

// 记录Error日志（不带异常）
Logger.Error("操作失败");

// 记录Error日志（带异常）
Logger.Error("操作失败", exception);

// 记录Fatal日志
Logger.Fatal("致命错误，程序即将退出", exception);
```

### 自动上下文信息

Logger使用CallerMemberName和CallerFilePath自动捕获：
- 调用方法的名称
- 调用所在的源文件

无需手动传递这些信息。

## 已集成的模块

### 1. 应用程序生命周期
- **文件**: `IspToolApp.xaml.cs`
- **日志内容**:
  - 应用启动/退出
  - 互斥锁获取
  - 运行模式选择（Develop/User）
  - 未处理异常捕获

### 2. 设备管理
- **文件**: `Device/DeviceManger.cs`
- **日志内容**:
  - 设备管理器初始化
  - 设备插入/拔出事件
  - 设备扫描
  - 资源释放

### 3. UVC视频流
- **文件**: `Uvc/UvcReceiver.cs`
- **日志内容**:
  - 回调注册
  - 连接/断开
  - 视频尺寸获取
  - 原始图像捕获
  - 播放状态变化
  - 调度器错误

### 4. 配置管理
- **文件**: `DeviceConfig/ConfigManager.cs`
- **日志内容**:
  - 配置管理器初始化
  - 配置添加/删除
  - 设备变更事件处理
  - 在线/离线配置读取
  - 资源释放

### 5. ISP处理管线
- **文件**: `DeviceConfig/Isp/Processor.cs`
- **日志内容**:
  - 处理器初始化
  - RAW/RGB文件处理
  - 模块依赖处理
  - 位图生成
  - 文件读写

### 6. ISP模块（BLC/LSC/AWB）
- **文件**: 
  - `DeviceConfig/Isp/BlackLevel.cs`
  - `DeviceConfig/Isp/LensShading.cs`
  - `DeviceConfig/Isp/AutoWhiteBalance.cs`
- **日志内容**:
  - 处理开始/完成
  - 输入输出缓冲区大小
  - 关键参数（分辨率、Bayer模式、校正值等）
  - C++ API调用
  - 异常处理

## 配置日志级别

### 修改最小日志级别

在 `IspToolApp.xaml.cs` 的 `OnStartup` 方法中：

```csharp
// 当前设置为Debug级别（最详细）
Logger.Initialize("logs", LogLevel.Debug);

// 可调整为Info级别（减少日志量）
Logger.Initialize("logs", LogLevel.Info);
```

### 日志级别建议

| 环境 | 推荐级别 | 说明 |
|------|----------|------|
| 开发调试 | Debug | 记录所有详细信息 |
| 测试环境 | Info | 记录关键操作和状态 |
| 生产环境 | Warn | 仅记录警告和错误 |

## 日志文件管理

### 自动清理

系统提供旧日志文件清理功能：

```csharp
// 清理30天前的日志
int deletedCount = Logger.CleanOldLogs(30);
Logger.Info($"Cleaned {deletedCount} old log files");
```

建议在应用启动时调用：

```csharp
Logger.Initialize("logs", LogLevel.Debug);
Logger.CleanOldLogs(30); // 清理旧日志
```

### 手动查看

日志文件是纯文本格式，可使用任何文本编辑器打开。推荐使用：
- Notepad++
- VS Code
- Baretail（实时查看）

## 日志示例

### 应用启动日志

```
[2026-04-10 14:23:45.123] [INFO ] [T01] [IspToolApp.OnStartup] - ========== ThunderSE Application Starting ==========
[2026-04-10 14:23:45.125] [INFO ] [T01] [IspToolApp.OnStartup] - Logger initialized. Log directory: D:\jrx\zl\isptool\Debug\logs
[2026-04-10 14:23:45.126] [INFO ] [T01] [IspToolApp.OnStartup] - Minimum log level: Debug
[2026-04-10 14:23:45.130] [INFO ] [T01] [IspToolApp.OnStartup] - Application instance lock acquired.
[2026-04-10 14:23:45.135] [INFO ] [T01] [IspToolApp.OnStartup] - Loading MainFrameForDevelop UI.
```

### 设备连接日志

```
[2026-04-10 14:23:46.200] [DEBUG] [T01] [DeviceManger..ctor] - Initializing DeviceManger...
[2026-04-10 14:23:46.210] [INFO ] [T01] [DeviceManger..ctor] - DeviceManger initialized successfully.
[2026-04-10 14:23:46.215] [DEBUG] [T01] [ConfigManager..ctor] - Initializing ConfigManager...
[2026-04-10 14:23:46.220] [INFO ] [T01] [ConfigManager..ctor] - ConfigManager initialized successfully.
[2026-04-10 14:23:47.500] [INFO ] [T03] [DeviceManger.OnDeviceChange] - Device change event: Arrival, Location: USB\VID_XXXX, Model: AX327X, UVC: video=USB Camera
[2026-04-10 14:23:47.505] [INFO ] [T03] [ConfigManager.OnDeviceChange] - Device change event received: Arrival, Model=AX327X
[2026-04-10 14:23:47.510] [INFO ] [T03] [ConfigManager.OnDeviceChange] - Device arrived: AX327X, connecting...
[2026-04-10 14:23:47.515] [DEBUG] [T04] [ConfigManager.OnDeviceChange] - Connecting to UVC: video=USB Camera
[2026-04-10 14:23:47.520] [INFO ] [T04] [UvcReceiver.Connect] - Connecting to UVC device: video=USB Camera
[2026-04-10 14:23:48.100] [INFO ] [T04] [UvcReceiver.Connect] - UVC connected successfully: 1920x1080
```

### ISP处理日志

```
[2026-04-10 14:24:00.100] [DEBUG] [T01] [Processor..ctor] - Processor initialized with ISP modules.
[2026-04-10 14:24:01.200] [DEBUG] [T01] [Processor.ProcessRawFile] - Processing RAW file, final step: Blc, useFinalStep: True
[2026-04-10 14:24:01.205] [DEBUG] [T01] [BlackLevel.ProcessRawBuffer] - [BLC] Processing - Buffer: 4147200 bytes, Resolution: 1920x1080, Bayer: RGGB
[2026-04-10 14:24:01.210] [DEBUG] [T01] [BlackLevel.ProcessRawBuffer] - [BLC] Correction values: R=64, Gr=64, Gb=64, B=64
[2026-04-10 14:24:01.215] [DEBUG] [T01] [BlackLevel.ProcessRawBuffer] - [BLC] Calling IspApi.BlcImg with 2073600 pixels
[2026-04-10 14:24:01.350] [DEBUG] [T01] [BlackLevel.ProcessRawBuffer] - [BLC] BlcImg completed, output: 2073600 shorts
[2026-04-10 14:24:01.355] [DEBUG] [T01] [BlackLevel.ProcessRawBuffer] - [BLC] Processing completed, output buffer: 4147200 bytes
```

## 故障排查

### 日志文件未生成

检查项：
1. Logger是否已初始化（查看`IspToolApp.xaml.cs`）
2. `logs/`目录是否有写入权限
3. 磁盘空间是否充足

### 日志输出不完整

检查项：
1. 日志级别是否合适（可能需要调整为Debug）
2. 是否有异常被捕获但未重新抛出
3. 查看Debug输出窗口是否有额外信息

### 性能问题

如果日志影响性能：
1. 提高日志级别（如Info代替Debug）
2. 减少不必要的日志调用
3. 考虑异步写入（当前是同步刷新）

## 最佳实践

1. **关键操作必加日志**：
   - 设备连接/断开
   - 文件读写
   - 网络请求
   - 用户关键操作

2. **异常必须记录**：
   - 所有catch块都应该记录异常
   - 使用`Logger.Error("message", ex)`包含异常堆栈

3. **循环内谨慎使用**：
   - 避免在高频循环中记录Debug日志
   - 可使用计数器限制日志频率

4. **敏感信息处理**：
   - 不要记录密码、密钥等敏感信息
   - 路径信息注意隐私问题

## 技术实现

Logger核心实现要点：

- **线程安全**: 使用`lock`保护写入操作
- **异步写入**: 使用`async void`避免阻塞调用线程
- **日志轮转**: 按日期自动创建新文件
- **自动初始化**: 首次调用时自动初始化
- **异常保护**: 日志系统自身异常不会影响主程序

详见 `Common/Logger.cs` 源码。

## 扩展建议

1. **性能优化**：可考虑使用异步队列批量写入
2. **日志压缩**：定期压缩旧日志文件节省空间
3. **远程日志**：集成日志服务器实现集中管理
4. **日志分析**：开发日志分析工具辅助问题定位
