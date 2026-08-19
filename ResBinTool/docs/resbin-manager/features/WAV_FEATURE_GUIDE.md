# WAV 音频预览功能使用指南

## 功能概述

ResBinManager 工具现已支持 WAV 音频资源的实时预览和播放功能。当您选择 RES.BIN 文件中的 WAV 资源时，系统会自动解析音频信息并提供播放控制。

## 主要特性

### 1. 自动音频信息解析
- **采样率**: 显示音频的采样频率（如 8000 Hz, 16000 Hz）
- **声道数**: 单声道 (Mono) 或立体声 (Stereo)
- **位深**: 音频位数（8-bit, 16-bit, 24-bit, 32-bit）
- **时长**: 自动计算并显示音频播放时长

### 2. 播放控制
- **播放/暂停**: 点击 "▶ Play" 按钮开始播放音频
- **停止**: 点击 "⏹ Stop" 按钮停止播放
- **音量调节**: 通过滑块调整音量（0% - 100%）

### 3. 智能界面切换
- 当选中 WAV 资源时，自动显示音频控制面板
- 当选中其他类型资源时，自动隐藏音频控制面板

## 使用步骤

### 步骤 1: 打开 RES.BIN 文件
1. 点击工具栏的 **"Open"** 按钮
2. 选择包含 WAV 资源的 RES.BIN 文件
3. 等待文件加载完成

### 步骤 2: 选择 WAV 资源
在资源列表中找到类型为 **WAV** 的资源项，例如：
- `SOUND_CLICK.WAV` - 点击音效
- `SOUND_ALERT.WAV` - 警告音效
- `MUSIC_BG.WAV` - 背景音乐

### 步骤 3: 查看音频信息
选中 WAV 资源后，右侧面板会自动显示：

```
🎵 Audio Information
┌─────────────────────────┐
│ Duration:    2.35s      │
│ Sample Rate: 16000 Hz   │
│ Channels:    Mono       │
│ Format:      16-bit     │
└─────────────────────────┘
```

### 步骤 4: 播放音频
1. 点击 **"▶ Play"** 按钮开始播放
2. 使用音量滑块调整音量
3. 点击 **"⏹ Stop"** 按钮停止播放

### 步骤 5: 替换音频（可选）
如果需要替换 WAV 资源：
1. 点击 **"Replace"** 按钮
2. 选择新的 WAV 文件
3. 确认替换操作
4. 点击 **"Save"** 保存修改后的 RES.BIN 文件

## 技术实现细节

### 依赖库
- **NAudio 2.1.0**: .NET 音频处理库，提供 WAV 解码和播放功能

### 核心组件

#### 1. WavInfoParser.cs
负责解析 WAV 文件头，提取音频参数：
```csharp
// 解析 WAV 数据
var wavInfo = WavInfoParser.Parse(wavData);
Console.WriteLine($"Sample Rate: {wavInfo.SampleRate} Hz");
Console.WriteLine($"Duration: {wavInfo.DurationDisplay}");
```

#### 2. WavPlayer.cs
提供音频播放功能：
```csharp
// 创建播放器
var player = new WavPlayer();
player.Load(wavData);
player.Volume = 0.8f; // 80% 音量
player.Play();
```

#### 3. MainViewModel.cs
集成 WAV 播放到 MVVM 架构：
- `WavInfo` 属性：绑定音频信息显示
- `WavVolume` 属性：绑定音量控制
- `PlayWavCommand` / `StopWavCommand`: 播放控制命令

## 支持的 WAV 格式

| 参数 | 支持范围 |
|------|---------|
| 采样率 | 8000 Hz - 192000 Hz |
| 声道数 | 1 - 8 声道 |
| 位深 | 8-bit, 16-bit, 24-bit, 32-bit |
| 格式 | PCM ( uncompressed ) |

## 常见问题

### Q1: 为什么有些 WAV 文件无法播放？
**A**: 确保 WAV 文件是标准的 PCM 格式。以下格式可能不受支持：
- 压缩格式（如 ADPCM, MP3-in-WAV）
- 非标准采样率（< 8000 Hz 或 > 192000 Hz）
- 损坏的文件头

### Q2: 播放时没有声音？
**A**: 检查以下几点：
1. 系统音量是否已开启
2. 应用程序音量滑块是否设置为 0%
3. 默认音频输出设备是否正确

### Q3: 如何验证 WAV 文件的有效性？
**A**: 可以使用以下方法：
```csharp
bool isValid = WavInfoParser.IsValidWav(wavData);
if (!isValid)
{
    MessageBox.Show("Invalid WAV file format");
}
```

### Q4: 替换 WAV 资源后文件大小变化很大怎么办？
**A**: 系统会提示您确认替换操作。如果新文件比原文件大很多，会导致后续资源的地址偏移。建议：
1. 尽量使用大小相近的替代文件
2. 替换后重新生成固件以确保地址正确

## 性能优化建议

1. **内存管理**: WAV 播放器会在加载新资源时自动释放旧资源
2. **延迟加载**: 仅在选中 WAV 资源时才解析和加载音频数据
3. **事件驱动**: 使用事件通知机制更新 UI，避免频繁轮询

## 下一步计划

当前版本实现了基础的 WAV 播放功能。未来版本可能会添加：
- [ ] 波形可视化显示
- [ ] 音频频谱分析
- [ ] 播放进度条和跳转功能
- [ ] 批量导出/导入 WAV 资源
- [ ] 音频格式转换工具

## 故障排除

如果遇到 WAV 播放问题，请检查：

1. **编译错误**: 确保 NAudio NuGet 包已正确安装
   ```bash
   dotnet restore
   ```

2. **运行时错误**: 查看输出窗口的异常信息

3. **UI 不更新**: 确认 XAML 中的数据绑定路径正确

## 代码示例

### 手动测试 WAV 解析
```csharp
// 读取 WAV 文件
byte[] wavData = File.ReadAllBytes("test.wav");

// 解析信息
var info = WavInfoParser.Parse(wavData);
Console.WriteLine(info.FullDescription);
// 输出: "16000Hz, 16-bit, Mono, 2.35s"

// 验证有效性
if (WavInfoParser.IsValidWav(wavData))
{
    Console.WriteLine("Valid WAV file");
}
```

### 在代码中播放 WAV
```csharp
using (var player = new WavPlayer())
{
    player.Load(wavData);
    player.Volume = 0.5f; // 50% 音量
    player.Play();
    
    // 等待播放完成
    Thread.Sleep((int)player.Duration.TotalMilliseconds);
}
```

## 总结

WAV 音频预览功能为 RES.BIN 资源管理提供了直观的音频试听体验，使开发者能够快速验证和调试嵌入式系统中的音效资源。结合现有的图片预览和固件打包功能，ResBinManager 已成为 AX329x SDK 开发的完整资源管理解决方案。

---

**版本**: v1.2.0  
**更新日期**: 2026-05-18  
**作者**: AX329x SDK Team
