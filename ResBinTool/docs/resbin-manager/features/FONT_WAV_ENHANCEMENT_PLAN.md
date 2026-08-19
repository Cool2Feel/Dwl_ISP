# Font 和 WAV 资源预览与替换功能完善方案

## 📋 目录

1. [现状分析](#现状分析)
2. [Font 资源深度分析](#font-资源深度分析)
3. [WAV 资源深度分析](#wav-资源深度分析)
4. [功能完善方案](#功能完善方案)
5. [技术实现细节](#技术实现细节)
6. [UI/UX 设计](#uiux-设计)
7. [实施路线图](#实施路线图)

---

## 现状分析

### 当前实现状态

#### ✅ 已实现功能

**JPEG/BMP 图片**:
- ✅ 实时预览（BitmapImage）
- ✅ 自适应缩放显示
- ✅ 双击快速预览
- ✅ 替换验证（格式检查）

**WAV 音频**:
- ⚠️ 仅显示基本信息（大小、估算时长）
- ❌ 无波形可视化
- ❌ 无播放功能
- ❌ 无频谱分析

**Font 字体**:
- ⚠️ 归类为 Binary 类型
- ❌ 无预览功能
- ❌ 无字符映射展示
- ❌ 无字体信息解析

**Binary 资源**:
- ⚠️ 仅显示元数据（ID、名称、大小、偏移）
- ❌ 无十六进制查看器
- ❌ 无内容分析

### 实际资源分布

根据 `resTable` 目录分析：

```
总资源数: 94 个

图片类 (JPEG/BMP): ~60 个 (64%)
  - power_on.jpg, power_off.jpg
  - frame*.jpg, gamemenu_*.bmp
  - icon_*.bmp, playback_frame*.jpg/bmp

音频类 (WAV): ~8 个 (8.5%)
  - game_block_knock.wav (3.3KB)
  - game_plane_audio.wav (7.0KB)
  - music_key_sound.wav (1.9KB)
  - music_photo_focus.wav (2.6KB)
  - music_photo_time.wav (2.0KB)
  - music_power_off.wav (57.1KB)
  - music_power_on.wav (50.6KB)
  - music_take_photo.wav (13.0KB)

字体类 (BIN): ~4 个 (4.3%)
  - MP3font.bin (982.8KB) ← 最大资源
  - resfont.bin (82.5KB)
  - resfontidx.bin (75.0KB)
  - OSD_source.bin (91.7KB)

编码表类 (BIN): ~3 个 (3.2%)
  - oem2uni936.bin (85.1KB)
  - uni2oem936.bin (85.1KB)
  - str_version.bin (0.0KB)

游戏数据类 (BIN): ~7 个 (7.4%)
  - game_*_map.bin
  - game_*_icon.bin
  - palette.bin
  - mainmenu_sel.bin
  - video_sel.bin

其他: ~12 个 (12.8%)
```

**关键发现**:
- Font 和 WAV 虽然数量不多，但 **MP3font.bin 占用了 982.8KB**（总资源的 23%）
- WAV 文件包含重要的 UI 音效，需要听觉验证
- Font 文件是中文显示的核心，需要字符映射验证

---

## Font 资源深度分析

### Font 文件格式确认

根据代码分析 [`font.c`](file://d:/dwl/work/2026/JT/JX_SDK/JT529X/firmware/ax32_platform_demo/font.c)，AX329x 使用**自定义点阵字体格式**：

#### 字体数据结构

```c
// font.c 中的定义
typedef struct Font_Data_S {
    INT16U width;      // 字符宽度（像素）
    INT16U height;     // 字符高度（像素）
    INT32U offset;     // 位图数据偏移
} Font_Data_T;

typedef struct Font_Idx_S {
    INT32U index;      // Unicode 索引
    INT32U offset;     // 字符数据偏移
} Font_Idx_T;

typedef struct Font_Str_S {
    INT16U width;      // 字符串总宽度
    INT16U height;     // 字符串高度
    INT16U number;     // 字符数量
    INT16U offset;     // 字符串偏移
} Font_Str_T;
```

#### 文件组成

**resfont.bin** (82.5KB):
- 包含所有字符的点阵位图数据
- 每个字符是一个单色位图（1 bit per pixel）
- 按 Unicode 编码组织

**resfontidx.bin** (75.0KB):
- 字符索引表
- 映射 Unicode → 位图偏移
- 支持快速查找

**MP3font.bin** (982.8KB):
- MP3 界面专用的大字库
- 包含中英文字符
- 可能采用不同的编码方式

### 字体渲染原理

从 [`font.c:368-400`](file://d:/dwl/work/2026/JT/JX_SDK/JT529X/firmware/ax32_platform_demo/font.c#L368-L400) 可以看出：

```c
int fontGetCharData(INT32U unicode, INT8U *charCache)
{
    // 1. 从索引表查找字符
    // 2. 读取位图数据到缓存
    // 3. 返回位图指针
    
    if(fontCtrl.strCache[num] & 0x8000) {
        // 特殊字符（可能是拼音标注）
        if(width) *width = fontCtrl.charInfo.width;
        if(height) *height = (fontCtrl.charInfo.height >> 2) + 1;
    } else {
        // 普通字符
        if(width) *width = fontCtrl.charInfo.width;
        if(height) *height = fontCtrl.charInfo.height;
    }
    return charCache;
}
```

**关键点**:
- 字体是**单色位图**（黑白）
- 每个字符有固定的宽度和高度
- 支持特殊标记（如拼音标注）
- 使用 NVFS 系统从 Flash 加载

### Font 预览方案设计

#### 方案 1: 字符网格预览（推荐）

```
┌─────────────────────────────────────┐
│  Font Preview - resfont.bin         │
├─────────────────────────────────────┤
│  Size: 82.5 KB | Chars: ~2000       │
│  Width: 16px | Height: 16px         │
├─────────────────────────────────────┤
│  0123456789ABCDEF                   │
│  !@#$%^&*()_+-=[]                   │
│  abcdefghijklmnop                   │
│  qrstuvwxyz{}|;':                   │
│  你好世界欢迎使用AX329x             │
│  测试字体显示效果正常               │
│  ...                                │
├─────────────────────────────────────┤
│  [Zoom: 100%] [Show Grid] [Export]  │
└─────────────────────────────────────┘
```

**实现步骤**:
1. 解析字体文件头，获取字符尺寸
2. 提取前 100-200 个常用字符
3. 将每个字符的位图转换为 WPF BitmapSource
4. 在 Grid 中排列显示
5. 支持缩放和网格线显示

#### 方案 2: 十六进制查看器 + 位图预览

```
┌─────────────────────────────────────┐
│  Hex Viewer + Bitmap Preview        │
├──────────┬──────────────────────────┤
│ Offset   │ 00 01 02 03 04 05 ...    │
├──────────┼──────────────────────────┤
│ 0x000000 │ FF FF FF 00 00 FF ...    │ ← 十六进制
│ 0x000010 │ FF 00 00 FF FF 00 ...    │
│ ...      │                          │
├──────────┴──────────────────────────┤
│  Preview (16x16 pixels):            │
│  ██░░░░░░░░░░░░██                   │ ← 位图预览
│  ██░░████████░░██                   │
│  ██░░████████░░██                   │
│  ...                                │
└─────────────────────────────────────┘
```

**优势**:
- 可以看到原始二进制数据
- 适合调试和分析字体格式
- 可以手动修改特定字符

#### 方案 3: 字符搜索和预览

```
┌─────────────────────────────────────┐
│  Character Search                   │
├─────────────────────────────────────┤
│  Search: [你好          ] 🔍        │
├─────────────────────────────────────┤
│  Found: U+4F60 (你)                 │
│  Width: 16px, Height: 16px          │
├─────────────────────────────────────┤
│  ░░░░░░████████░░░░                 │
│  ░░░░████████████░░                 │
│  ░░██████░░░░██████░░               │
│  ░░██████░░░░██████░░               │
│  ░░░░████████████░░                 │
│  ░░░░░░████████░░░░                 │
│  ░░░░████████████░░                 │
│  ░░██████░░░░██████░░               │
│  ░░██████░░░░██████░░               │
│  ░░░░████████████░░                 │
│  ░░░░░░████████░░░░                 │
│  ░░░░░░░░░░░░░░░░░░                 │
│  ░░░░░░░░░░░░░░░░░░                 │
│  ░░░░░░░░░░░░░░░░░░                 │
│  ░░░░░░░░░░░░░░░░░░                 │
│  ░░░░░░░░░░░░░░░░░░                 │
└─────────────────────────────────────┘
```

**功能**:
- 输入 Unicode 或字符本身
- 显示对应的位图
- 支持放大查看细节

### Font 替换方案设计

#### 替换流程

```
1. 用户选择要替换的字体资源（如 MP3font.bin）
2. 点击 "Replace" 按钮
3. 选择新的字体文件
   ├─ 选项 A: 直接替换为 .bin 文件（保持格式一致）
   └─ 选项 B: 从 TTF 生成点阵字体（需要转换工具）
4. 验证新字体文件
   ├─ 检查文件大小是否合理
   ├─ 检查文件头格式
   └─ 抽样验证字符位图
5. 执行替换
6. 预览新字体效果
7. 保存并重新打包固件
```

#### 验证要点

**文件大小检查**:
```csharp
if (newSize > oldSize * 2) {
    // 警告：新文件过大
    MessageBox.Show("New font file is too large!");
}
```

**格式检查**:
```csharp
// 检查是否为有效的点阵字体
bool IsValidBitmapFont(byte[] data) {
    // 检查文件头魔数（如果有）
    // 检查字符数量是否合理
    // 检查位图数据完整性
    return true;
}
```

**字符抽样**:
```csharp
// 抽样检查几个常用字符
char[] sampleChars = { 'A', 'a', '0', '你', '好' };
foreach (char c in sampleChars) {
    var bitmap = ExtractCharBitmap(data, c);
    if (bitmap == null || IsEmpty(bitmap)) {
        return false; // 字符缺失
    }
}
```

---

## WAV 资源深度分析

### WAV 文件格式

WAV 是标准的音频文件格式，结构如下：

```
WAV File Structure:
┌──────────────────────┐
│ RIFF Header          │ 12 bytes
│  - "RIFF"            │
│  - File size         │
│  - "WAVE"            │
├──────────────────────┤
│ fmt Chunk            │ 16-40 bytes
│  - Audio format      │ (1=PCM)
│  - Num channels      │ (1=Mono, 2=Stereo)
│  - Sample rate       │ (8000/16000/44100 Hz)
│  - Bits per sample   │ (8/16/24 bits)
├──────────────────────┤
│ data Chunk           │ Variable
│  - "data"            │
│  - Data size         │
│  - Raw audio samples │
└──────────────────────┘
```

### AX329x 中的 WAV 使用情况

从代码分析，WAV 文件用于：

1. **UI 音效**:
   - `music_key_sound.wav` (1.9KB) - 按键音
   - `music_photo_focus.wav` (2.6KB) - 对焦音
   - `music_take_photo.wav` (13.0KB) - 拍照音

2. **系统提示音**:
   - `music_power_on.wav` (50.6KB) - 开机音乐
   - `music_power_off.wav` (57.1KB) - 关机音乐

3. **游戏音效**:
   - `game_block_knock.wav` (3.3KB) - 方块碰撞
   - `game_plane_audio.wav` (7.0KB) - 飞机游戏音效

**推测参数**:
- 采样率: 16kHz 或 8kHz（嵌入式系统常用）
- 位深: 16-bit PCM
- 声道: Mono（单声道）
- 压缩: 无压缩（PCM）

### WAV 预览方案设计

#### 方案 1: 波形可视化 + 播放（推荐）

```
┌─────────────────────────────────────┐
│  WAV Preview - music_power_on.wav   │
├─────────────────────────────────────┤
│  Duration: 3.2s | Size: 50.6 KB     │
│  Format: 16kHz, 16-bit, Mono        │
├─────────────────────────────────────┤
│                                     │
│  ▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄   │ ← 波形图
│  ████████████████████████████████   │
│  ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀   │
│                                     │
├─────────────────────────────────────┤
│  [◀▶ Play] [⏹ Stop] [🔊 Volume]    │
│  [00:00 / 00:03.2]                  │
└─────────────────────────────────────┘
```

**实现技术**:
- **NAudio** 库（.NET 音频处理）
- 解析 WAV 文件头获取参数
- 提取 PCM 数据绘制波形
- 使用 `WaveOut` 播放音频

#### 方案 2: 频谱分析 + 播放

```
┌─────────────────────────────────────┐
│  Spectrum Analyzer                  │
├─────────────────────────────────────┤
│                                     │
│  █                                  │
│  ██                                 │
│  ███       ██                       │ ← 频谱柱状图
│  █████    ████                      │
│  ██████  ██████                     │
│  ██████████████                     │
│  0Hz   1k   2k   4k   8k  16k      │
├─────────────────────────────────────┤
│  [Play] [Pause] [Loop]              │
└─────────────────────────────────────┘
```

**优势**:
- 可以看到频率分布
- 适合分析音频质量
- 视觉效果更好

#### 方案 3: 详细信息 + 简单播放

```
┌─────────────────────────────────────┐
│  Audio Information                  │
├─────────────────────────────────────┤
│  File: music_power_on.wav           │
│  Size: 50.6 KB                      │
│  Duration: 3.2 seconds              │
│  Sample Rate: 16000 Hz              │
│  Channels: 1 (Mono)                 │
│  Bits: 16                           │
│  Format: PCM                        │
├─────────────────────────────────────┤
│  [▶ Play] [⏹ Stop]                  │
└─────────────────────────────────────┘
```

**最简单实现**:
- 使用 `System.Media.SoundPlayer`
- 无需第三方库
- 功能有限但够用

### WAV 替换方案设计

#### 替换流程

```
1. 用户选择 WAV 资源
2. 点击 "Replace"
3. 选择新的 .wav 文件
4. 验证新文件
   ├─ 检查文件格式（必须是 WAV）
   ├─ 检查参数兼容性
   │   ├─ 采样率是否匹配
   │   ├─ 位深是否匹配
   │   └─ 声道数是否匹配
   └─ 检查文件大小（避免过大）
5. 播放预览（可选）
6. 执行替换
7. 保存并重新打包
```

#### 验证要点

**格式检查**:
```csharp
bool IsValidWav(byte[] data) {
    if (data.Length < 44) return false;
    
    // 检查 RIFF 标志
    if (Encoding.ASCII.GetString(data, 0, 4) != "RIFF")
        return false;
    
    // 检查 WAVE 标志
    if (Encoding.ASCII.GetString(data, 8, 4) != "WAVE")
        return false;
    
    return true;
}
```

**参数提取**:
```csharp
class WavInfo {
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
    public TimeSpan Duration { get; set; }
}

WavInfo ParseWavHeader(byte[] data) {
    var info = new WavInfo();
    
    // 读取 fmt chunk
    info.SampleRate = BitConverter.ToInt32(data, 24);
    info.Channels = BitConverter.ToInt16(data, 22);
    info.BitsPerSample = BitConverter.ToInt16(data, 34);
    
    // 计算时长
    int dataSize = BitConverter.ToInt32(data, 40);
    int bytesPerSecond = info.SampleRate * info.Channels * (info.BitsPerSample / 8);
    info.Duration = TimeSpan.FromSeconds((double)dataSize / bytesPerSecond);
    
    return info;
}
```

**兼容性检查**:
```csharp
void ValidateCompatibility(WavInfo original, WavInfo replacement) {
    var warnings = new List<string>();
    
    if (original.SampleRate != replacement.SampleRate) {
        warnings.Add($"Sample rate mismatch: {original.SampleRate} vs {replacement.SampleRate}");
    }
    
    if (original.Channels != replacement.Channels) {
        warnings.Add($"Channel count mismatch: {original.Channels} vs {replacement.Channels}");
    }
    
    if (warnings.Count > 0) {
        MessageBox.Show(string.Join("\n", warnings), 
                       "Compatibility Warning", 
                       MessageBoxButton.OK, 
                       MessageBoxImage.Warning);
    }
}
```

---

## 功能完善方案

### 优先级排序

#### P0 - 高优先级（必须实现）

1. **WAV 播放功能**
   - 基本播放/停止
   - 音量控制
   - 进度显示

2. **WAV 信息显示**
   - 采样率、位深、声道数
   - 时长计算
   - 文件大小

3. **Font 字符网格预览**
   - 显示前 100 个字符
   - 支持缩放
   - 显示字符 Unicode 编码

#### P1 - 中优先级（应该实现）

4. **WAV 波形可视化**
   - 简单波形图
   - 峰值显示
   - 时间轴

5. **Font 字符搜索**
   - 按 Unicode 搜索
   - 按字符搜索
   - 高亮显示

6. **WAV 格式验证**
   - 自动检测参数
   - 兼容性警告
   - 错误提示

#### P2 - 低优先级（可以后续实现）

7. **WAV 频谱分析**
   - FFT 变换
   - 频谱柱状图
   - 实时更新

8. **Font 十六进制查看器**
   - 原始数据显示
   - 位图同步预览
   - 编辑功能

9. **Font 导出功能**
   - 导出为 PNG 字符集
   - 导出为文本报告
   - 导出为 TTF（复杂）

### 技术选型

#### WAV 处理

**方案 A: NAudio（推荐）**
```xml
<!-- NuGet 包 -->
<PackageReference Include="NAudio" Version="2.1.0" />
```

**优势**:
- ✅ 功能强大，成熟稳定
- ✅ 支持 WAV 解析、播放、录制
- ✅ 提供波形绘制组件
- ✅ 纯 .NET 实现，无需 native DLL

**劣势**:
- ❌ 增加依赖（~500KB）
- ❌ 学习曲线较陡

**方案 B: System.Media.SoundPlayer**
```csharp
using System.Media;

var player = new SoundPlayer("test.wav");
player.Play();
```

**优势**:
- ✅ 无需额外依赖
- ✅ 简单易用
- ✅ 内置于 .NET Framework

**劣势**:
- ❌ 功能有限（只能播放）
- ❌ 无法获取波形数据
- ❌ 无法控制音量

**方案 C: Windows Media Player COM**
```csharp
// 添加 COM 引用: WMPLib
var player = new WMPLib.WindowsMediaPlayer();
player.URL = "test.wav";
player.controls.play();
```

**优势**:
- ✅ 功能完整
- ✅ 支持多种格式

**劣势**:
- ❌ 需要 Windows Media Player
- ❌ COM 互操作复杂
- ❌ 不适合嵌入式工具

**推荐**: **NAudio**，因为我们需要波形可视化和详细参数解析。

#### Font 处理

**方案 A: 自定义解析器（推荐）**
```csharp
class FontParser {
    public FontInfo Parse(byte[] data) { ... }
    public Bitmap RenderChar(int unicode) { ... }
}
```

**优势**:
- ✅ 完全控制解析逻辑
- ✅ 可以根据实际格式定制
- ✅ 无外部依赖

**劣势**:
- ❌ 需要逆向工程字体格式
- ❌ 开发工作量大

**方案 B: FreeTypeSharp**
```xml
<PackageReference Include="FreeTypeSharp" Version="1.1.3" />
```

**优势**:
- ✅ 成熟的字体渲染引擎
- ✅ 支持多种字体格式

**劣势**:
- ❌ 主要针对 TTF/OTF
- ❌ 不适用于自定义点阵字体
- ❌ Native DLL 依赖

**方案 C: 位图直接渲染**
```csharp
// 直接从二进制数据创建 Bitmap
Bitmap CreateCharBitmap(byte[] bitmadata, int width, int height) {
    var bmp = new Bitmap(width, height);
    for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
            int bitIndex = y * width + x;
            byte bit = bitmapData[bitIndex / 8] & (0x80 >> (bitIndex % 8));
            bmp.SetPixel(x, y, bit != 0 ? Color.Black : Color.White);
        }
    }
    return bmp;
}
```

**优势**:
- ✅ 简单直接
- ✅ 适合点阵字体
- ✅ 无依赖

**劣势**:
- ❌ 需要了解具体格式
- ❌ 不支持矢量字体

**推荐**: **方案 A + C 结合**，先解析字体格式，再渲染位图。

---

## 技术实现细节

### 1. WAV 播放功能实现

#### 步骤 1: 添加 NAudio 依赖

```xml
<!-- ResBinManager.csproj -->
<ItemGroup>
    <PackageReference Include="NAudio" Version="2.1.0" />
</ItemGroup>
```

#### 步骤 2: 创建 WAV 播放器类

```csharp
// Core/WavPlayer.cs
using NAudio.Wave;
using System;
using System.IO;

namespace ResBinManager.Core
{
    public class WavPlayer : IDisposable
    {
        private WaveOutEvent? _waveOut;
        private WaveFileReader? _reader;
        private bool _disposed;

        public event EventHandler<PlaybackStoppedEventArgs>? PlaybackStopped;

        public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;
        public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

        public void Load(byte[] wavData)
        {
            Stop();
            
            var stream = new MemoryStream(wavData);
            _reader = new WaveFileReader(stream);
            
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_reader);
            _waveOut.PlaybackStopped += OnPlaybackStopped;
        }

        public void Play()
        {
            if (_waveOut != null && _reader != null)
            {
                _reader.Position = 0;
                _waveOut.Play();
            }
        }

        public void Pause()
        {
            _waveOut?.Pause();
        }

        public void Stop()
        {
            _waveOut?.Stop();
        }

        public void SetVolume(float volume)
        {
            if (_waveOut != null)
            {
                _waveOut.Volume = Math.Clamp(volume, 0f, 1f);
            }
        }

        private void OnPlaybackStopped(object? sender, PlaybackStoppedEventArgs e)
        {
            PlaybackStopped?.Invoke(this, e);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _waveOut?.Dispose();
                _reader?.Dispose();
                _disposed = true;
            }
        }
    }
}
```

#### 步骤 3: 解析 WAV 信息

```csharp
// Core/WavInfoParser.cs
using System;
using System.IO;
using System.Text;

namespace ResBinManager.Core
{
    public class WavInfo
    {
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitsPerSample { get; set; }
        public int DataSize { get; set; }
        public TimeSpan Duration { get; set; }
        public string Format { get; set; } = "PCM";
        
        public string Description => 
            $"{SampleRate}Hz, {BitsPerSample}-bit, {(Channels == 1 ? "Mono" : "Stereo")}";
    }

    public static class WavInfoParser
    {
        public static WavInfo Parse(byte[] wavData)
        {
            if (wavData.Length < 44)
                throw new InvalidDataException("WAV file too small");

            // 验证 RIFF 标志
            if (Encoding.ASCII.GetString(wavData, 0, 4) != "RIFF")
                throw new InvalidDataException("Invalid WAV file: missing RIFF header");

            if (Encoding.ASCII.GetString(wavData, 8, 4) != "WAVE")
                throw new InvalidDataException("Invalid WAV file: missing WAVE marker");

            var info = new WavInfo();

            // 读取 fmt chunk
            info.Channels = BitConverter.ToInt16(wavData, 22);
            info.SampleRate = BitConverter.ToInt32(wavData, 24);
            info.BitsPerSample = BitConverter.ToInt16(wavData, 34);

            // 读取 data chunk 大小
            info.DataSize = BitConverter.ToInt32(wavData, 40);

            // 计算时长
            int bytesPerSecond = info.SampleRate * info.Channels * (info.BitsPerSample / 8);
            if (bytesPerSecond > 0)
            {
                info.Duration = TimeSpan.FromSeconds((double)info.DataSize / bytesPerSecond);
            }

            return info;
        }
    }
}
```

#### 步骤 4: 绘制波形图

```csharp
// Core/WaveformRenderer.cs
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ResBinManager.Core
{
    public static class WaveformRenderer
    {
        public static Geometry DrawWaveform(byte[] wavData, double width, double height)
        {
            var info = WavInfoParser.Parse(wavData);
            
            // 提取 PCM 数据（跳过 44 字节头部）
            var pcmData = new byte[wavData.Length - 44];
            Array.Copy(wavData, 44, pcmData, 0, pcmData.Length);

            // 降采样用于显示（最多 1000 个点）
            int sampleCount = Math.Min(1000, pcmData.Length / (info.BitsPerSample / 8));
            var points = new List<Point>();
            
            double step = (double)pcmData.Length / sampleCount;
            for (int i = 0; i < sampleCount; i++)
            {
                int index = (int)(i * step);
                
                // 读取 16-bit 样本
                short sample = BitConverter.ToInt16(pcmData, index);
                
                // 归一化到 -1.0 ~ 1.0
                double normalized = sample / 32768.0;
                
                // 映射到绘图区域
                double x = (double)i / sampleCount * width;
                double y = height / 2 - normalized * height / 2;
                
                points.Add(new Point(x, y));
            }

            // 创建折线几何
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(points[0], false, false);
                foreach (var point in points)
                {
                    context.LineTo(point, true, false);
                }
            }
            geometry.Freeze();

            return geometry;
        }
    }
}
```

### 2. Font 预览功能实现

#### 步骤 1: 字体解析器

```csharp
// Core/FontParser.cs
using System;
using System.Collections.Generic;
using System.IO;

namespace ResBinManager.Core
{
    public class FontInfo
    {
        public int CharWidth { get; set; }
        public int CharHeight { get; set; }
        public int CharCount { get; set; }
        public List<int> UnicodeList { get; set; } = new();
        public Dictionary<int, byte[]> CharBitmaps { get; set; } = new();
    }

    public class FontParser
    {
        public FontInfo Parse(byte[] fontData, byte[]? indexData = null)
        {
            var info = new FontInfo();

            // TODO: 根据实际字体格式解析
            // 这里假设一个简单的点阵字体格式
            
            // 1. 尝试从文件头读取字符尺寸
            // 2. 解析索引表获取字符列表
            // 3. 提取每个字符的位图数据

            // 临时实现：假设固定 16x16 点阵
            info.CharWidth = 16;
            info.CharHeight = 16;
            
            // 估算字符数量（每个字符 32 字节 = 16*16/8）
            info.CharCount = fontData.Length / 32;

            // 提取前 100 个字符用于预览
            int previewCount = Math.Min(100, info.CharCount);
            for (int i = 0; i < previewCount; i++)
            {
                int offset = i * 32;
                if (offset + 32 <= fontData.Length)
                {
                    var bitmap = new byte[32];
                    Array.Copy(fontData, offset, bitmap, 0, 32);
                    info.CharBitmaps[i] = bitmap;
                    info.UnicodeList.Add(i + 0x20); // 假设从 ASCII 32 开始
                }
            }

            return info;
        }

        public byte[]? GetCharBitmap(FontInfo font, int unicode)
        {
            if (font.CharBitmaps.TryGetValue(unicode, out var bitmap))
            {
                return bitmap;
            }
            return null;
        }
    }
}
```

#### 步骤 2: 位图渲染器

```csharp
// Core/FontBitmapRenderer.cs
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ResBinManager.Core
{
    public static class FontBitmapRenderer
    {
        public static BitmapSource RenderChar(byte[] bitmapData, int width, int height, int scale = 1)
        {
            int scaledWidth = width * scale;
            int scaledHeight = height * scale;
            
            var pixels = new byte[scaledWidth * scaledHeight * 4]; // BGRA

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int bitIndex = y * width + x;
                    int byteIndex = bitIndex / 8;
                    int bitOffset = bitIndex % 8;
                    
                    byte bit = (byteIndex < bitmapData.Length) 
                        ? (byte)(bitmapData[byteIndex] & (0x80 >> bitOffset))
                        : (byte)0;

                    Color color = bit != 0 ? Colors.Black : Colors.White;

                    // 填充缩放后的像素
                    for (int sy = 0; sy < scale; sy++)
                    {
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int px = x * scale + sx;
                            int py = y * scale + sy;
                            int pixelIndex = (py * scaledWidth + px) * 4;

                            pixels[pixelIndex] = color.B;
                            pixels[pixelIndex + 1] = color.G;
                            pixels[pixelIndex + 2] = color.R;
                            pixels[pixelIndex + 3] = color.A;
                        }
                    }
                }
            }

            var bitmap = new WriteableBitmap(scaledWidth, scaledHeight, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, scaledWidth, scaledHeight), pixels, scaledWidth * 4, 0);
            bitmap.Freeze();

            return bitmap;
        }
    }
}
```

---

## UI/UX 设计

### MainWindow.xaml 更新

#### 添加 WAV 控制面板

```xml
<!-- 在 PreviewPanel 中添加 -->
<StackPanel x:Name="WavControlPanel" Visibility="Collapsed">
    <!-- WAV 信息 -->
    <GroupBox Header="Audio Information" Margin="0,0,0,10">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <TextBlock Text="Duration:" Grid.Row="0" Grid.Column="0" Margin="5"/>
            <TextBlock Text="{Binding WavInfo.DurationDisplay}" Grid.Row="0" Grid.Column="1" Margin="5"/>

            <TextBlock Text="Sample Rate:" Grid.Row="1" Grid.Column="0" Margin="5"/>
            <TextBlock Text="{Binding WavInfo.SampleRateDisplay}" Grid.Row="1" Grid.Column="1" Margin="5"/>

            <TextBlock Text="Channels:" Grid.Row="2" Grid.Column="0" Margin="5"/>
            <TextBlock Text="{Binding WavInfo.ChannelsDisplay}" Grid.Row="2" Grid.Column="1" Margin="5"/>

            <TextBlock Text="Format:" Grid.Row="3" Grid.Column="0" Margin="5"/>
            <TextBlock Text="{Binding WavInfo.FormatDisplay}" Grid.Row="3" Grid.Column="1" Margin="5"/>
        </Grid>
    </GroupBox>

    <!-- 波形图 -->
    <Border Height="100" Background="#F0F0F0" Margin="0,0,0,10"
            BorderBrush="#CCCCCC" BorderThickness="1">
        <Path x:Name="WaveformPath" Stroke="#2196F3" StrokeThickness="1.5"
              HorizontalAlignment="Stretch" VerticalAlignment="Center"/>
    </Border>

    <!-- 播放控制 -->
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
        <Button Content="▶ Play" Command="{Binding PlayWavCommand}" Width="80" Margin="5"/>
        <Button Content="⏹ Stop" Command="{Binding StopWavCommand}" Width="80" Margin="5"/>
        <Slider Width="150" Minimum="0" Maximum="1" Value="{Binding WavVolume}" 
                TickFrequency="0.1" Margin="5"/>
    </StackPanel>
</StackPanel>
```

#### 添加 Font 预览面板

```xml
<!-- 在 PreviewPanel 中添加 -->
<StackPanel x:Name="FontPreviewPanel" Visibility="Collapsed">
    <!-- Font 信息 -->
    <GroupBox Header="Font Information" Margin="0,0,0,10">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <TextBlock Text="Characters:" Grid.Row="0" Grid.Column="0" Margin="5"/>
            <TextBlock Text="{Binding FontInfo.CharCountDisplay}" Grid.Row="0" Grid.Column="1" Margin="5"/>

            <TextBlock Text="Char Size:" Grid.Row="1" Grid.Column="0" Margin="5"/>
            <TextBlock Text="{Binding FontInfo.CharSizeDisplay}" Grid.Row="1" Grid.Column="1" Margin="5"/>

            <TextBlock Text="File Size:" Grid.Row="2" Grid.Column="0" Margin="5"/>
            <TextBlock Text="{Binding SelectedResource.SizeDisplay}" Grid.Row="2" Grid.Column="1" Margin="5"/>
        </Grid>
    </GroupBox>

    <!-- 字符网格 -->
    <GroupBox Header="Character Grid" Margin="0,0,0,10">
        <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto"
                     Height="300">
            <ItemsControl ItemsSource="{Binding FontPreviewChars}">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <WrapPanel ItemWidth="40" ItemHeight="40"/>
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border BorderBrush="#CCCCCC" BorderThickness="1" Margin="2">
                            <StackPanel>
                                <Image Source="{Binding Bitmap}" Width="32" Height="32" 
                                       Stretch="Uniform"/>
                                <TextBlock Text="{Binding UnicodeDisplay}" 
                                          FontSize="8" HorizontalAlignment="Center"/>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </GroupBox>

    <!-- 搜索框 -->
    <StackPanel Orientation="Horizontal" Margin="0,5,0,0">
        <TextBox Width="150" Text="{Binding FontSearchText, UpdateSourceTrigger=PropertyChanged}"
                Margin="5"/>
        <Button Content="🔍 Search" Command="{Binding SearchFontCharCommand}" Margin="5"/>
    </StackPanel>
</StackPanel>
```

### ViewModel 更新

```csharp
// ViewModels/MainViewModel.cs

// 添加属性
private WavPlayer? _wavPlayer;
private WavInfo? _wavInfo;
private FontInfo? _fontInfo;
private ObservableCollection<FontCharPreview> _fontPreviewChars;

public WavInfo? WavInfo
{
    get => _wavInfo;
    set { _wavInfo = value; OnPropertyChanged(); }
}

public FontInfo? FontInfo
{
    get => _fontInfo;
    set { _fontInfo = value; OnPropertyChanged(); }
}

public ObservableCollection<FontCharPreview> FontPreviewChars
{
    get => _fontPreviewChars;
    set { _fontPreviewChars = value; OnPropertyChanged(); }
}

public ICommand PlayWavCommand { get; }
public ICommand StopWavCommand { get; }
public ICommand SearchFontCharCommand { get; }

// 在构造函数中初始化
public MainViewModel()
{
    
    PlayWavCommand = new RelayCommand(ExecutePlayWav, CanExecutePlayWav);
    StopWavCommand = new RelayCommand(ExecuteStopWav);
    SearchFontCharCommand = new RelayCommand(ExecuteSearchFontChar);
    
    FontPreviewChars = new ObservableCollection<FontCharPreview>();
}

// WAV 播放命令
private void ExecutePlayWav(object? parameter)
{
    if (SelectedResource?.Type == ResourceType.Wav && SelectedResource.Data != null)
    {
        if (_wavPlayer == null)
        {
            _wavPlayer = new WavPlayer();
        }
        
        _wavPlayer.Load(SelectedResource.Data);
        _wavPlayer.Play();
    }
}

private void ExecuteStopWav(object? parameter)
{
    _wavPlayer?.Stop();
}

// Font 搜索命令
private void ExecuteSearchFontChar(object? parameter)
{
    // TODO: 实现字符搜索逻辑
}

// 更新预览方法
private void OnPreviewRequested(object? sender, ResourceItem resource)
{
    // 隐藏所有特殊面板
    WavControlPanel.Visibility = Visibility.Collapsed;
    FontPreviewPanel.Visibility = Visibility.Collapsed;
    
    switch (resource.Type)
    {
        case ResourceType.Jpeg:
        case ResourceType.Bitmap:
            ShowImagePreview(resource.Data);
            break;
        
        case ResourceType.Wav:
            ShowWavPreview(resource);
            break;
        
        case ResourceType.Font:
            ShowFontPreview(resource);
            break;
        
        default:
            ShowBinaryInfo(resource);
            break;
    }
}

private void ShowWavPreview(ResourceItem resource)
{
    try
    {
        // 解析 WAV 信息
        WavInfo = WavInfoParser.Parse(resource.Data!);
        
        // 绘制波形
        var waveformGeometry = WaveformRenderer.DrawWaveform(
            resource.Data!, 
            WaveformCanvas.ActualWidth, 
            WaveformCanvas.ActualHeight);
        WaveformPath.Data = waveformGeometry;
        
        // 显示控制面板
        WavControlPanel.Visibility = Visibility.Visible;
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Failed to parse WAV: {ex.Message}", "Error",
                       MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

private void ShowFontPreview(ResourceItem resource)
{
    try
    {
        var parser = new FontParser();
        FontInfo = parser.Parse(resource.Data!);
        
        // 生成字符预览
        FontPreviewChars.Clear();
        foreach (var kvp in FontInfo.CharBitmaps.Take(100))
        {
            var bitmap = FontBitmapRenderer.RenderChar(
                kvp.Value, 
                FontInfo.CharWidth, 
                FontInfo.CharHeight,
                scale: 2); // 2x 缩放
            
            FontPreviewChars.Add(new FontCharPreview
            {
                Bitmap = bitmap,
                Unicode = FontInfo.UnicodeList[kvp.Key],
                UnicodeDisplay = $"U+{FontInfo.UnicodeList[kvp.Key]:X4}"
            });
        }
        
        // 显示预览面板
        FontPreviewPanel.Visibility = Visibility.Visible;
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Failed to parse font: {ex.Message}", "Error",
                       MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
```

---

## 实施路线图

### Phase 1: WAV 基础功能（1-2 天）

**Day 1**:
- [ ] 添加 NAudio NuGet 包
- [ ] 实现 `WavPlayer` 类
- [ ] 实现 `WavInfoParser` 类
- [ ] 添加播放/停止按钮

**Day 2**:
- [ ] 实现 WAV 信息显示
- [ ] 集成到 MainViewModel
- [ ] 测试基本播放功能
- [ ] 添加音量控制

### Phase 2: WAV 波形可视化（1-2 天）

**Day 3**:
- [ ] 实现 `WaveformRenderer` 类
- [ ] 添加波形图 UI 组件
- [ ] 优化降采样算法

**Day 4**:
- [ ] 添加时间轴显示
- [ ] 实现播放进度同步
- [ ] 性能优化

### Phase 3: Font 基础预览（2-3 天）

**Day 5**:
- [ ] 研究实际字体格式
- [ ] 实现 `FontParser` 类
- [ ] 实现 `FontBitmapRenderer` 类

**Day 6**:
- [ ] 创建字符网格 UI
- [ ] 实现字符渲染
- [ ] 添加缩放功能

**Day 7**:
- [ ] 添加字符搜索功能
- [ ] 显示 Unicode 编码
- [ ] 测试常用字符

### Phase 4: 高级功能（2-3 天）

**Day 8-9**:
- [ ] WAV 频谱分析（可选）
- [ ] Font 十六进制查看器（可选）
- [ ] 批量导出功能

**Day 10**:
- [ ] 综合测试
- [ ] Bug 修复
- [ ] 性能优化
- [ ] 文档更新

### 总计：8-10 个工作日

---

## 总结

### 核心价值

1. **WAV 预览**: 听觉验证 UI 音效，确保音质和时长符合预期
2. **Font 预览**: 视觉验证字体显示，确保字符完整性和清晰度
3. **提升效率**: 减少烧录测试次数，加快开发迭代
4. **降低风险**: 提前发现问题，避免固件刷写失败

### 技术亮点

- ✅ 使用 NAudio 实现专业级音频处理
- ✅ 自定义字体解析器适配嵌入式格式
- ✅ 实时波形可视化和字符网格预览
- ✅ 完整的格式验证和兼容性检查

### 下一步行动

1. **立即开始**: Phase 1 - WAV 基础功能
2. **优先完成**: WAV 播放和信息显示
3. **逐步完善**: Font 预览和高级功能
4. **持续优化**: 性能和用户体验

---

**文档版本**: v1.0  
**创建日期**: 2026-05-18  
**作者**: AX329x SDK Team
