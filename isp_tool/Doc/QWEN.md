# ThunderSE / isptool 项目上下文

## 项目概述

这是一个基于 **Windows 桌面平台** 的 ISP (Image Signal Processor) 调试与配置工具，项目名称为 **ThunderSE**。它主要用于相机/镜头模组 ISP 参数的调试、校准和烧录。

### 技术栈

- **主界面 (UI)**: C# / WPF (.NET Framework 4.8)
- **底层库**: C++ (Win32 DLL)
- **IDE**: Visual Studio 2013+
- **架构模式**: MVVM (使用 MvvmLight 框架)
- **目标平台**: x86 (Win32)

### 项目优化状态

**2026年4月已完成全面优化**,包括:
- ✅ 修复严重Bug (CH.cs反序列化错误、分辨率硬编码)
- ✅ 修复P/Invoke调用约定不匹配 (UvcApi、DeviceApi)
- ✅ 消除30+处空引用风险 (XmlHelper安全解析)
- ✅ 修复内存泄漏 (MemoryManager、UvcReceiver、DeviceManger)
- ✅ UVC视频性能优化 (异步调度、限流机制)
- ✅ 线程安全改进 (Lazy单例、异常保护)
- ✅ ConfigManager重构 (并发字典、异步操作)

详见 [OPTIMIZATION_REPORT.md](OPTIMIZATION_REPORT.md)

### 项目结构

解决方案 (`ThunderSE.sln`) 包含以下子项目：

| 项目 | 类型 | 说明 |
|------|------|------|
| `ThunderSE` | C# WPF 应用 | 主程序，包含 UI 和业务逻辑 |
| `Device` | C++ DLL | 设备通信层（目前项目文件较空，可能通过其他方式管理） |
| `Uvc` | C++ DLL | UVC 视频采集模块，基于 FFmpeg，支持 RTSP 流预览和录制 |
| `IspApi` | C++ DLL | ISP 算法库，包含 BLC、LSC、AWB、YGamma、Demosaic 等图像处理函数 |
| `LcdApi` | C++ DLL | LCD 相关算法库，主要提供 Anti-Sawtooth (抗锯齿) 功能 |
| `DeviceModuelTest` | Win32 应用 | 设备模块测试工具 |

### 主要功能模块

主程序 (`ThunderSE`) 按功能分为：

- **DeviceConfig/Isp/** — ISP 配置数据模型，包括：
  - AE (自动曝光)
  - AWB (自动白平衡)
  - BLC (黑电平校正)
  - LSC (镜头阴影校正)
  - Gamma
  - CCM (颜色校正矩阵)
  - EE (边缘增强)
  - CH (色彩增强)
  - DDC
  - VDE
  - SAJ (Anti-Sawtooth 抗锯齿)

- **DeviceConfig/Lcd/** — LCD 配置模块，包含 LcdSetting、LcdGamma、LcdCcm、LcdSaj 等

- **Ui/MainWindow/** — 主界面，分两种模式：
  - `MainFrameForDevelop` — 开发者模式
  - `MainFrameForUser` — 用户模式（含 EffectTab、LcdTab、CommonTab）

- **Ui/SettingWindow/** — 各类 ISP 参数调试窗口 (AwbWindow, BlcWindow, LscWindow, YGammaWindow, CcmWindow 等)

- **Uvc/** — C# 端 UVC 视频流封装 (`UvcApi.cs`, `UvcReceiver.cs`)

## 构建与运行

### 环境要求

- **Visual Studio 2013 或更高版本**（项目格式为 VS2013）
- **.NET Framework 4.8 SDK**
- **NuGet** (用于还原 packages)

### 依赖库

项目通过 NuGet 管理以下包 (`ThunderSE/packages.config`)：

- `MvvmLightLibs 5.3.0.0` — MVVM 框架
- `CommonServiceLocator 1.0` — 服务定位器
- `WPFToolkit 3.5.50211.1` — WPF 扩展控件
- `WPFToolkit.DataVisualization 3.5.50211.1` — 数据可视化控件

第三方 C++ 依赖放在 `3rd/` 目录下：
- `3rd/include/` — 头文件
- `3rd/lib/` — 导入库
- `3rd/dll/` — 运行时 DLL

### 构建方式

1. 打开 `ThunderSE.sln`
2. 还原 NuGet 包：`nuget restore ThunderSE.sln`
3. 在 Visual Studio 中选择 **Debug** 或 **Release** 配置，平台选择 **Win32** / **Mixed Platforms**
4. 生成解决方案 (Ctrl+Shift+B)

输出目录：
- Debug → `Debug/`
- Release → `Release/`

### 运行

直接按 F5 启动 `ThunderSE` 项目即可运行主程序 `IspToolApp.xaml`。

## 架构说明

### C++ 与 C# 互操作

C++ DLL 通过 `__declspec(dllexport/dllimport)` 和 `extern "C"` 导出 C 风格函数，C# 端使用 `[DllImport]` 进行 P/Invoke 调用：

```cpp
// IspApi.dll (C++)
UVC_API int OpenInput(const char* filepath, int& videoWidth, int& videoHeight);
```

```csharp
// C# 端
[DllImport("IspApi.dll", CallingConvention = CallingConvention.Cdecl)]
public static extern void DemosaicImg(...);
```

### MVVM 模式

项目使用 **MvvmLight** 实现 MVVM：

- `ViewModel/MainViewModel.cs` — 主视图模型
- `ViewModel/ViewModelLocator.cs` — 视图模型定位器
- 各 UI 页面有对应的 ViewModel (如 `MainFrameForUserViewModel`, `EffectTabViewModel` 等)
- 使用 `AsyncRelayCommand` 处理异步命令

### UVC 视频流

`Uvc.dll` 封装了视频采集功能：
- 支持 RTSP 流输入（默认 `rtsp://192.168.1.1:7070/webcam`）
- 支持本地视频文件回放
- 提供三个回调：`PlayStateChangeCallbackFunc`、`VideoDataCallbackFunc`、`YuvDataCallbackFunc`
- 视频数据格式为 RGB24

## 开发约定

- C++ 代码使用预编译头 (`stdafx.h`)
- C# 代码遵循 .NET Framework 命名规范
- UI 与逻辑分离，使用 MVVM 模式
- P/Invoke 调用约定统一使用 `CallingConvention.Cdecl`

## 注意事项

- 项目较老（VS2013 格式），部分 C++ 项目（如 `Device`、`IspApi`）源码可能未完全纳入版本控制
- 运行时需确保 `3rd/dll/` 下的第三方 DLL 与项目生成的 DLL 一起输出到执行目录
- 目标框架为 .NET 4.8，但 NuGet 包目标框架为 net35，兼容性由 Visual Studio 处理
