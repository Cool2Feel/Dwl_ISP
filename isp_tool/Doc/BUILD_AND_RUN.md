# ThunderSE 编译和运行指南

## 快速开始

### 环境要求
- **Visual Studio 2013** 或更高版本
- **.NET Framework 4.8 SDK**
- **NuGet** (Visual Studio通常已集成)
- **Windows x86平台**

### 编译步骤

#### 方法1: Visual Studio (推荐)
1. 打开 `d:\jrx\zl\isptool\ThunderSE.sln`
2. 等待NuGet包自动还原（右下角会显示进度）
3. 在工具栏选择:
   - 配置: `Debug` 或 `Release`
   - 平台: `Mixed Platforms` 或 `Win32`
4. 按 `Ctrl+Shift+B` 生成解决方案

#### 方法2: 命令行
```batch
cd d:\jrx\zl\isptool

:: 1. 还原NuGet包
nuget restore ThunderSE.sln

:: 2. 编译
msbuild ThunderSE.sln ^
  /p:Configuration=Debug ^
  /p:Platform="Mixed Platforms" ^
  /t:Rebuild ^
  /v:minimal
```

### 运行程序

编译成功后:
- **Debug版本**: `d:\jrx\zl\isptool\Debug\ThunderSE.exe`
- **Release版本**: `d:\jrx\zl\isptool\Release\ThunderSE.exe`

直接双击运行，或在Visual Studio中按 `F5` 调试运行。

---

## 已知依赖项

### NuGet包 (自动还原)
- MvvmLightLibs 5.3.0.0
- CommonServiceLocator 1.0
- WPFToolkit 3.5.50211.1
- WPFToolkit.DataVisualization 3.5.50211.1

### 第三方DLL (需手动确认)
以下DLL应存在于 `3rd/dll/` 目录:
- FFmpeg相关DLL (uvc.dll依赖)
- 其他硬件SDK (如有)

### 项目生成DLL
- `Device.dll` - 设备通信层
- `Uvc.dll` - 视频采集
- `IspApi.dll` - ISP算法
- `LcdApi.dll` - LCD算法

**注意**: C++项目需要在C#项目之前编译。

---

## 常见问题排查

### 编译错误

#### 1. "找不到XXXXX.dll"
**原因**: NuGet包未还原或3rd库缺失
**解决**:
```batch
nuget restore ThunderSE.sln
```
检查 `3rd/dll/`, `3rd/lib/`, `3rd/include/` 是否存在

#### 2. "无法识别UvcReceiver"
**原因**: 可能是文件编码问题
**解决**: 在Visual Studio中右键 `Uvc/UvcReceiver.cs` > 高级保存选项 > UTF-8

#### 3. "XmlHelper未定义"
**原因**: 新文件未被项目包含
**解决**: 
1. 右键 `ThunderSE` 项目 > 添加 > 现有项
2. 选择 `DeviceConfig/XmlHelper.cs`
3. 或在 `.csproj` 文件中确认包含

#### 4. C++编译错误
**原因**: 可能需要Platform Toolset
**解决**: 
1. 右键C++项目 > 属性
2. 配置属性 > 常规 > 平台工具集
3. 选择已安装的Visual Studio版本

### 运行时错误

#### 1. "找不到Device.dll/uvc.dll"
**原因**: DLL未复制到输出目录
**解决**: 
1. 确认C++项目已成功编译
2. 检查 `Debug/` 或 `Release/` 目录下是否有这些DLL
3. 手动复制到可执行文件同目录

#### 2. "连接设备失败"
**原因**: 无实际设备或驱动问题
**解决**: 
- 这是正常现象，可以测试离线模式
- 加载本地配置文件测试ISP参数

#### 3. "视频预览黑屏"
**原因**: 无摄像头或RTSP流不可达
**解决**: 
- 检查设备管理器中是否有UVC设备
- 测试RTSP地址是否可访问

---

## 功能测试清单

### 基础功能
- [ ] 程序能正常启动
- [ ] 界面显示正常（中文无乱码）
- [ ] 菜单和按钮可点击
- [ ] 窗口可以调整大小

### 离线模式
- [ ] 可以打开XML配置文件
- [ ] ISP参数可以修改
- [ ] 修改后可以保存
- [ ] 配置文件可以另存为

### 在线模式 (需设备)
- [ ] 设备插入后自动识别
- [ ] 视频预览流畅
- [ ] 可以读取设备参数
- [ ] 可以写入参数到设备
- [ ] 设备拔出后自动断开

### 视频功能
- [ ] 视频预览无明显卡顿
- [ ] 可以截图保存RAW帧
- [ ] 录影功能正常（如有）

### ISP调试
- [ ] AE/AWB/BLC/LSC等模块可打开
- [ ] 参数调整有实时反馈
- [ ] 图表显示正确（如有）

---

## 性能基准

### 正常指标
- **启动时间**: < 3秒
- **UI响应**: < 100ms
- **视频帧率**: 25-30fps (取决于设备)
- **内存占用**: 100-200MB (含视频预览)
- **长时间运行**: 内存稳定，无明显增长

### 异常指标 (需报告)
- 启动崩溃
- 操作时频繁卡顿
- 内存持续增长
- 视频严重掉帧

---

## 调试技巧

### 启用详细日志
在 `App.config` 中添加:
```xml
<system.diagnostics>
  <switches>
    <add name="DefaultSwitch" value="4" />
  </switches>
</system.diagnostics>
```

### 查看输出窗口
Visual Studio > 调试 > 窗口 > 输出
选择 "显示输出来源: 调试"

### 性能分析
1. 调试 > 性能探查器 > 性能向导
2. 选择 "CPU使用率" 或 "内存使用率"
3. 运行并分析结果

---

## 联系与支持

如有问题，请检查:
1. `OPTIMIZATION_REPORT.md` - 优化详情
2. `BUILD_CHECK.md` - 编译检查清单
3. `QWEN.md` - 项目上下文

祝编译顺利！ 🚀
