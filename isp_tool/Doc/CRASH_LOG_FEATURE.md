# 崩溃日志功能说明

## 功能概述

ThunderSE 现已集成完整的崩溃日志记录功能，可以捕获和记录程序运行过程中发生的各种异常，帮助开发人员快速定位和修复问题。

## 主要特性

### 1. 全局异常捕获

程序注册了以下四种全局异常处理器：

| 异常类型 | 说明 | 是否阻止崩溃 |
|---------|------|------------|
| **DispatcherUnhandledException** | UI线程未处理的异常 | ✅ 可阻止（Release模式） |
| **AppDomain.UnhandledException** | 非UI线程未处理的异常 | ❌ 仅记录 |
| **TaskScheduler.UnobservedTaskException** | Task未观察到的异常 | ✅ 可阻止 |
| **AppDomain.ProcessExit** | 进程退出事件 | ❌ 仅记录 |

### 2. 详细的崩溃报告

当发生崩溃时，系统会记录以下信息：

- ✅ **系统信息**：操作系统版本、.NET版本、内存使用等
- ✅ **异常详情**：异常类型、消息、来源、目标方法、HResult
- ✅ **完整堆栈**：异常的完整堆栈跟踪
- ✅ **内部异常链**：所有嵌套的内部异常
- ✅ **加载的程序集**：当前加载的所有程序集信息
- ✅ **附加信息**：时间戳、线程ID等诊断信息

### 3. 日志文件管理

- **位置**：`程序目录/logs/ThunderSE_YYYY-MM-DD.log`
- **格式**：按日期自动分割，每天一个文件
- **编码**：UTF-8
- **清理**：自动清理30天前的日志文件

## 使用方法

### 查看崩溃日志

#### 方式一：通过菜单查看

1. 打开程序
2. 点击菜单栏：**文件 → 查看崩溃日志**
3. 在弹出的窗口中查看最近的崩溃报告

#### 方式二：直接查看日志文件

1. 打开程序目录下的 `logs` 文件夹
2. 找到最新的 `ThunderSE_YYYY-MM-DD.log` 文件
3. 用任意文本编辑器打开查看

### 崩溃日志示例

```
================================================================================
[CRASH REPORT] 2026-04-14 15:30:45.123
================================================================================
[2026-04-14 15:30:45.123] [FATAL] [T01] [IspToolApp.OnUnhandledException] - [CRASH] Unhandled Exception (Non-UI Thread) - Application Terminating
[2026-04-14 15:30:45.123] [FATAL] [T01] [IspToolApp.OnUnhandledException] - [SYSTEM] OS: Microsoft Windows NT 10.0.19045.0
[2026-04-14 15:30:45.123] [FATAL] [T01] [IspToolApp.OnUnhandledException] - [SYSTEM] .NET Version: 4.0.30319.42000
[2026-04-14 15:30:45.123] [FATAL] [T01] [IspToolApp.OnUnhandledException] - [SYSTEM] 64-bit OS: True
[2026-04-14 15:30:45.123] [FATAL] [T01] [IspToolApp.OnUnhandledException] - [SYSTEM] 64-bit Process: False
[2026-04-14 15:30:45.123] [FATAL] [T01] [IspToolApp.OnUnhandledException] - [EXCEPTION] Type: System.NullReferenceException
[2026-04-14 15:30:45.123] [FATAL] [T01] [IspToolApp.OnUnhandledException] - [EXCEPTION] Message: 未将对象引用设置到对象的实例。
[2026-04-14 15:30:45.123] [FATAL] [T01] [IspToolApp.OnUnhandledException] - [EXCEPTION] Source: ThunderSE
[2026-04-14 15:30:45.123] [FATAL] [T01] [IspToolApp.OnUnhandledException] - [STACK TRACE]
   在 ThunderSE.SomeClass.SomeMethod() 位置 D:\project\SomeFile.cs:行号 123
   ...
================================================================================
```

## 实现细节

### 修改的文件

1. **IspToolApp.xaml.cs**
   - 添加 `RegisterGlobalExceptionHandlers()` 方法
   - 增强 `OnUnhandledException()` 方法
   - 启动时记录系统和版本信息

2. **Logger.cs**
   - 新增 `LogCrashReport()` 方法，专门记录崩溃详情
   - 包含完整的系统信息、异常链、程序集列表

3. **CrashLogWindow.xaml / CrashLogWindow.xaml.cs** (新增)
   - 崩溃日志查看器窗口
   - 支持复制日志、打开日志目录

4. **MainFrameForDevelop.xaml / MainFrameForDevelop.xaml.cs**
   - 添加"查看崩溃日志"菜单项
   - 添加"关于"菜单项

5. **MainFrameForUser.xaml / MainFrameForUser.xaml.cs**
   - 添加菜单栏
   - 添加"查看崩溃日志"菜单项
   - 添加"关于"菜单项

## Debug vs Release 模式

### Debug 模式
- UI异常不会阻止程序崩溃（便于调试）
- 异常详情会显示原始异常对话框

### Release 模式
- UI异常会被捕获并阻止程序崩溃
- 显示友好的错误提示
- 引导用户查看日志文件

## 注意事项

1. **日志文件权限**：确保程序目录有写入权限
2. **磁盘空间**：日志会占用磁盘空间，建议定期清理
3. **敏感信息**：日志中可能包含路径名等信息，发送给开发者前请确认
4. **自动清理**：程序启动时会自动清理30天前的日志

## 故障排查

### 日志文件未生成

检查以下项：
1. 程序目录是否有写入权限
2. `logs` 文件夹是否创建成功
3. 查看Debug输出窗口（Visual Studio）

### 崩溃日志窗口打不开

可能原因：
1. CrashLogWindow.xaml 未添加到项目中
2. 编译错误
3. 日志目录不存在或无权限

### 如何提供日志给开发者

1. 打开程序菜单：**文件 → 查看崩溃日志**
2. 点击"📋 复制日志"或"📂 打开日志目录"
3. 将日志内容或文件发送给开发者

## 技术要点

### 线程安全

- 所有日志写入都使用 `lock` 保护
- 使用 `volatile` 关键字标记共享标志
- 异步写入不阻塞主线程

### 性能优化

- 日志写入使用 `AutoFlush = false`，手动控制刷新
- 崩溃报告使用批处理，一次性写入
- 避免在热路径中记录过多日志

### 异常安全

- 日志记录本身有异常保护
- 即使日志失败也不会影响主程序
- 崩溃时确保尽可能多的信息被记录

## 总结

崩溃日志功能为程序提供了：
- ✅ 全面的异常捕获
- ✅ 详细的诊断信息
- ✅ 友好的查看界面
- ✅ 便捷的反馈机制

这将大大提高问题的排查效率和修复质量。
