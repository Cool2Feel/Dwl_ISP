# 更新日志

## v1.5.1 (2026-05-18) - Bug 修复

### 🐛 Bug 修复

#### NullReferenceException 修复
- ✅ 修复 `IsFontResource()` 方法的 null 引用异常
- ✅ 修复 `CanExecuteSave()` LINQ 表达式的 null 引用异常
- ✅ 修复 `LoadFontForPreview()` 中的 null 引用异常
- ✅ 修复 `ExecuteReplaceFont()` 中的 null 引用异常
- ✅ **修复 DataGrid UI 刷新时的 NullReferenceException**
- ✅ 添加参数 null 检查
- ✅ 将参数类型改为可空 (`ResourceItem?`)
- ✅ 在 LINQ 表达式中添加 null 检查
- ✅ 防止取消选中资源时崩溃
- ✅ 防止 Resources 集合包含 null 元素时崩溃
- ✅ **在列表修改前保存 SelectedResource 引用**
- ✅ **临时清空 _selectedResource 防止 DataGrid 访问不一致状态**

### 🔧 技术改进

- ✅ 增强防御性编程
- ✅ 改进空安全处理
- ✅ 添加详细的错误分析文档

### 📝 文档更新

- ✅ 新增 `BUGFIX_NULL_REFERENCE_EXCEPTION.md` - 详细修复说明

---

## v1.5.0 (2026-05-18) - Font 资源替换功能

### ✨ 新增功能

#### Font 双文件联合替换
- ✅ 自动检测 resfont.bin 和 resfontidx.bin
- ✅ 专用替换对话框界面
- ✅ 双文件同时选择和验证
- ✅ 原子性替换操作（要么都成功，要么都失败）

#### Font 格式验证
- ✅ resfontidx.bin 魔数检查（0x4D58）
- ✅ 文件大小验证（最小尺寸检查）
- ✅ 字符数量合理性验证（1-65535）
- ✅ 语言数量验证（1-20）
- ✅ 默认字符宽度合理性检查
- ✅ 双文件匹配性验证
- ✅ 字符元数据有效性检查

#### 参数对比显示
- ✅ 替换前显示新旧字体参数对比
- ✅ 字符数量变化提示
- ✅ 语言数量变化提示
- ✅ 文件大小差异显示
- ✅ 字符集规模对比

#### 智能警告系统
- ✅ 超大字符集警告（> 2000 字符）
- ✅ 超多语言警告（> 10 种）
- ✅ 超大文件体积警告（> 5MB）
- ✅ 异常字符尺寸警告（> 128px）
- ✅ 文件体积差异过大警告（> 50%）

#### 用户交互增强
- ✅ 实时文件选择验证
- ✅ 验证状态颜色区分（绿/红/黄）
- ✅ 当前字体信息显示
- ✅ 警告时二次确认
- ✅ 详细的成功/失败提示

### 🎨 UI 改进

#### Font 替换对话框
- ✅ 现代化对话框设计
- ✅ 清晰的分区布局
- ✅ 直观的文件浏览按钮
- ✅ 动态验证结果展示
- ✅ 参数对比表格

#### 替换按钮优化
- ✅ 绿色主题按钮 (#4CAF50)
- ✅ 圆角设计 (CornerRadius=5)
- ✅ 悬停/按下效果
- ✅ 居中对齐布局
- ✅ 大字号显示 (13pt)

### 🔧 技术改进

- ✅ 新增 `FontValidator` 类 - 专用验证器
- ✅ 新增 `FontValidationResult` 类 - 验证结果封装
- ✅ 新增 `FontReplaceDialog` 窗口 - 替换对话框
- ✅ 新增 `ReplaceFontCommand` 命令
- ✅ 新增 `ExecuteReplaceFont()` 方法
- ✅ 增强字体解析错误处理
- ✅ 完善空引用安全检查

### 📝 文档更新

- ✅ 新增 `FONT_REPLACE_IMPLEMENTATION_SUMMARY.md` - 实现总结
- ✅ 更新 `WAV_FONT_REPLACE_ANALYSIS.md` - 分析文档
- ✅ 更新 `CHANGELOG.md` - 版本记录

### 📊 代码统计

- 新增文件: 3 个 (FontValidator.cs, FontReplaceDialog.xaml, FontReplaceDialog.xaml.cs)
- 修改文件: 2 个 (MainViewModel.cs, MainWindow.xaml)
- 新增代码: ~747 行
- 验证检查项: 7 项
- 警告类型: 5 种

---

## v1.4.0 (2026-05-18) - WAV 格式验证功能

### ✨ 新增功能

#### WAV 格式验证
- ✅ 完整的 RIFF/WAVE 魔数检查
- ✅ 文件大小验证（最小 44 字节）
- ✅ 音频格式解析（PCM/其他）
- ✅ 采样率范围检查（8kHz - 192kHz）
- ✅ 声道数验证（1-8）
- ✅ 位深度支持检查（8/16/24/32-bit）
- ✅ Data chunk 查找和验证

#### 参数对比显示
- ✅ 替换前显示新旧文件参数对比
- ✅ 采样率变化提示
- ✅ 声道数变化提示
- ✅ 位深度变化提示
- ✅ 时长差异显示
- ✅ 文件大小差异显示

#### 智能警告系统
- ✅ 极低/高采样率警告
- ✅ 8-bit 低动态范围警告
- ✅ >16-bit 兼容性警告
- ✅ 多声道混音警告
- ✅ 长音频性能警告
- ✅ 大文件体积警告

#### 用户交互增强
- ✅ 验证失败时阻止替换
- ✅ 详细的确认对话框
- ✅ 警告时使用黄色图标
- ✅ 可取消替换操作

### 📝 文档更新

- ✅ 新增 `WAV_VALIDATION_TEST_GUIDE.md` - 测试指南
- ✅ 新增 `WAV_VALIDATION_IMPLEMENTATION_SUMMARY.md` - 实现总结
- ✅ 更新 `WAV_FONT_REPLACE_ANALYSIS.md` - 分析文档

### 🔧 技术改进

- ✅ 新增 `WavValidator` 类 - 专用验证器
- ✅ 新增 `WavValidationResult` 类 - 验证结果封装
- ✅ 增强 `ExecuteReplace()` 方法 - 集成 WAV 验证
- ✅ 新增 `ValidateAndConfirmWavReplacement()` 方法

### 📊 代码统计

- 新增文件: 1 个 (WavValidator.cs)
- 修改文件: 1 个 (MainViewModel.cs)
- 新增代码: ~315 行
- 验证检查项: 8 项
- 警告类型: 7 种

---

## v1.3.0 (2026-05-18) - Font 字符网格预览功能

### ✨ 新增功能

#### Font 字体解析
- ✅ 支持 AX329x 自定义点阵字体格式
- ✅ 解析 resfont.bin 和 resfontidx.bin 双文件
- ✅ 提取字符元数据（宽度、高度、偏移）
- ✅ 支持多语言索引系统
- ✅ 16字节对齐处理

#### 字符网格预览
- ✅ 可视化显示前 200 个字符
- ✅ 黑白位图渲染
- ✅ WrapPanel 自动换行布局
- ✅ ToolTip 显示字符详情
- ✅ 滚动查看器支持

#### 交互控制
- ✅ 缩放功能（40% - 300%）
- ✅ 网格线显示/隐藏
- ✅ 实时刷新显示
- ✅ 智能面板切换

### 📝 文档更新

- ✅ 新增 `PHASE2_COMPLETION_REPORT.md` - Phase 2 完成报告

### 🎨 UI 改进

- ✅ 右侧面板添加 Font 控制区域（默认隐藏）
- ✅ 字体信息显示 GroupBox
- ✅ 字符网格预览容器（400px 高度）
- ✅ 缩放控制按钮组
- ✅ 网格线开关复选框
- ✅ 选中字体资源时自动显示控制面板

### 🔧 技术实现

#### 新增文件
- `Core/FontInfoParser.cs` - 字体文件解析器（293行）
- `Controls/FontPreviewControl.cs` - 字体预览控件（252行）
- `PHASE2_COMPLETION_REPORT.md` - Phase 2 完成报告

#### 修改文件
- `ViewModels/MainViewModel.cs` - 添加字体加载逻辑
- `Views/MainWindow.xaml` - 添加 Font 控制面板 UI
- `Views/MainWindow.xaml.cs` - 添加字体预览控制逻辑

#### 核心类
```csharp
// FontInfoParser.cs
public static class FontInfoParser
{
    public static FontInfo Parse(byte[] fontData, byte[] fontIndex);
    public static byte[] ExtractCharBitmap(byte[] fontData, CharInfo charInfo);
    public static bool[,] BitmapToPixels(byte[] bitmap, int width, int height);
}

public class FontInfo
{
    public uint CharCount { get; set; }
    public byte LanguageCount { get; set; }
    public List<CharInfo> Characters { get; set; }
}

// FontPreviewControl.cs
public class FontPreviewControl : UserControl
{
    public double ZoomLevel { get; set; }
    public bool ShowGrid { get; set; }
    
    public void LoadFont(byte[] fontData, byte[] fontIndex);
    public void ClearDisplay();
}
```

### 📊 代码统计

- 新增代码行数: ~545 行
- 新增文件数: 3 个
- 修改文件数: 3 个
- 总代码行数: ~3,500 行
- 总文件数: 22 个

### 🐛 Bug 修复

- ✅ 修复 FontPreviewControl 可空性警告
- ✅ 修复反射访问私有字段的问题
- ✅ 修复位图 MSB 优先解析错误

### ⚠️ Breaking Changes

- 无（完全向后兼容）

---

## v1.2.0 (2026-05-18) - WAV 音频预览功能

### ✨ 新增功能

#### WAV 音频播放
- ✅ 集成 NAudio 2.1.0 音频库
- ✅ 实现 WAV 资源实时播放功能
- ✅ 支持 PCM 格式（8/16/24/32-bit）
- ✅ 自动解析音频参数（采样率、声道数、位深、时长）

#### 音频信息展示
- ✅ 显示音频详细信息面板
- ✅ 格式化显示时长、采样率、声道等信息
- ✅ 智能界面切换（仅对 WAV 资源显示控制面板）

#### 播放控制
- ✅ Play/Stop 播放控制按钮
- ✅ 音量调节滑块（0% - 100%）
- ✅ 播放状态实时更新
- ✅ 命令可用性动态管理

#### 内存管理
- ✅ 自动加载/卸载 WAV 数据
- ✅ 播放器资源自动释放
- ✅ 窗口关闭时清理所有音频资源

### 📝 文档更新

- ✅ 新增 `WAV_FEATURE_GUIDE.md` - WAV 功能完整使用指南
- ✅ 更新 `CHANGELOG.md` - 记录 v1.2.0 变更

### 🎨 UI 改进

- ✅ 右侧面板添加 WAV 控制区域（默认隐藏）
- ✅ 音频信息显示框（GroupBox）
- ✅ 播放控制按钮组
- ✅ 音量调节滑块和百分比显示
- ✅ 选中 WAV 资源时自动显示控制面板

### 🔧 技术实现

#### 新增文件
- `Core/WavInfoParser.cs` - WAV 文件头解析器（191行）
- `Core/WavPlayer.cs` - WAV 播放器封装（197行）
- `WAV_FEATURE_GUIDE.md` - WAV 功能使用指南

#### 修改文件
- `ResBinManager.csproj` - 添加 NAudio NuGet 包引用
- `ViewModels/MainViewModel.cs` - 添加 WAV 播放逻辑和属性
- `Views/MainWindow.xaml` - 添加 WAV 控制面板 UI
- `Views/MainWindow.xaml.cs` - 添加面板可见性控制逻辑

#### 核心类
```csharp
// WavInfoParser.cs
public static class WavInfoParser
{
    public static WavInfo Parse(byte[] wavData);
    public static bool IsValidWav(byte[] wavData);
}

public class WavInfo
{
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
    public TimeSpan Duration { get; set; }
    public string FullDescription { get; }
}

// WavPlayer.cs
public class WavPlayer : IDisposable
{
    public event EventHandler PlaybackStateChanged;
    public bool IsPlaying { get; }
    public float Volume { get; set; }
    
    public void Load(byte[] wavData);
    public void Play();
    public void Stop();
    public void Pause();
}
```

### 📊 代码统计

- 新增代码行数: ~450 行
- 新增文件数: 3 个
- 修改文件数: 4 个
- 总代码行数: ~2,950 行
- 总文件数: 20 个

### 🐛 Bug 修复

- ✅ 修复 RelayCommand 缺少 RaiseCanExecuteChanged 方法的问题
- ✅ 修复 Application 引用不明确的问题（System.Windows vs Forms）
- ✅ 修复资源提取方法调用错误

### ⚠️ Breaking Changes

- 无（完全向后兼容）

---

## v1.1.1 (2026-05-18) - 编译问题修复

### 🐛 Bug 修复

#### OpenFolderDialog 兼容性问题
- ✅ 修复 .NET 6.0 中 `OpenFolderDialog` 不可用的问题
- ✅ 替换为 `FolderBrowserDialog`（Windows Forms）
- ✅ 在 `.csproj` 中添加 `<UseWindowsForms>true</UseWindowsForms>`
- ✅ 添加 `using System.Windows.Forms;`

#### 结构体修改问题
- ✅ 修复 `ResBinWriter.cs` 中直接修改列表结构体元素的问题
- ✅ 采用“获取副本 → 修改 → 赋值回去”的正确模式
- ✅ 修复 `UpdateResourceLength()` 方法
- ✅ 修复 `UpdateSubsequentAddresses()` 方法

#### 可空性警告
- ✅ 消除 `_resources` 字段的 CS8618 警告
- ✅ 消除 `_statusMessage` 字段的 CS8618 警告
- ✅ 使用 `null!` 和 `string.Empty` 初始化

### 📝 文档更新

- ✅ 新增 `BUILD_FIXES.md` - 详细的编译问题修复记录

### 📊 代码统计

- 修改文件数: 3 个
- 新增文档: 1 个
- 编译状态: ✅ 成功（仅剩 EOL 警告）

---

## v1.1.0 (2026-05-18) - 固件打包功能

### ✨ 新增功能

#### 固件打包集成
- ✅ 集成 MakeSPIBin.exe 调用逻辑
- ✅ 实现一键固件打包流程（ELF + RES.BIN → DestBin.bin）
- ✅ 添加固件打包配置面板（可切换显示/隐藏）
- ✅ 支持异步打包，不阻塞 UI 界面

#### 可视化配置
- ✅ ELF 文件选择器（自动推断输出目录）
- ✅ MakeSPIBin.exe 路径选择器
- ✅ 输出目录选择器
- ✅ RES.BIN 路径自动填充（使用当前打开的文件）

#### 进度监控
- ✅ 实时进度条显示（0-100%）
- ✅ 详细日志输出窗口（Consolas 字体）
- ✅ 捕获并显示标准输出和错误输出
- ✅ 各阶段进度提示（备份、复制、合并等）

#### 安全保护
- ✅ 打包前自动备份原 DestBin.bin
- ✅ 使用时间戳命名避免覆盖（DestBin.bin.backup_YYYYMMDD_HHMMSS）
- ✅ 验证所有输入文件存在性
- ✅ 超时保护机制（60秒）
- ✅ 退出码检查确保执行成功

#### 结果反馈
- ✅ 显示生成文件大小（KB）
- ✅ 显示打包耗时（秒）
- ✅ 自动打开输出文件夹（可选）
- ✅ 详细的成功/失败对话框
- ✅ 状态栏实时更新

### 📝 文档更新

- ✅ 新增 `FIRMWARE_BUILD_GUIDE.md` - 完整的固件打包使用指南
- ✅ 更新 `README.md` - 添加固件打包功能说明
- ✅ 更新 `QUICKSTART.md` - 添加内置打包功能的快速入门
- ✅ 更新 `PROJECT_SUMMARY.md` - 反映新增功能和文件统计

### 🎨 UI 改进

- ✅ 工具栏添加 **🔨 Build Firmware** 按钮
- ✅ 工具栏添加 **⚙️ Config** 切换按钮（ToggleButton）
- ✅ 右侧面板支持预览/配置切换
- ✅ 固件打包配置面板包含：
  - 4个路径配置项（带 Browse 按钮）
  - 2个选项复选框
  - 进度条
  - 日志输出框
  - 说明信息框

### 🔧 技术实现

#### 新增文件
- `Models/FirmwareBuildConfig.cs` - 固件打包配置模型
- `Core/FirmwareBuilder.cs` - 固件打包引擎（325行）
- `FIRMWARE_BUILD_GUIDE.md` - 固件打包使用指南

#### 修改文件
- `ViewModels/MainViewModel.cs` - 添加固件打包相关命令和方法
- `Views/MainWindow.xaml` - 添加固件打包 UI 组件
- `Views/MainWindow.xaml.cs` - 添加面板切换逻辑
- `README.md` - 更新功能列表和注意事项
- `QUICKSTART.md` - 更新打包步骤
- `PROJECT_SUMMARY.md` - 更新项目统计

#### 核心类
```csharp
// FirmwareBuilder.cs
public class FirmwareBuilder
{
    public event EventHandler<BuildProgressEventArgs> ProgressChanged;
    public async Task<BuildResult> BuildAsync();
}

// BuildProgressEventArgs
public class BuildProgressEventArgs : EventArgs
{
    public string Message { get; set; }
    public int Progress { get; set; } // 0-100
    public bool IsError { get; set; }
}

// BuildResult
public class BuildResult
{
    public bool Success { get; set; }
    public string OutputFile { get; set; }
    public string ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
}
```

### 📊 代码统计

- 新增代码行数: ~700 行
- 新增文件数: 3 个
- 修改文件数: 6 个
- 总代码行数: ~2,500 行
- 总文件数: 17 个

### 🐛 已知问题

- 无

### ⚠️  breaking Changes

- 无（完全向后兼容）

---

## v1.0.0 (2026-05-18) - 初始版本

### ✨ 核心功能

- ✅ RES.BIN 文件解析（94个资源）
- ✅ 资源列表浏览（DataGrid）
- ✅ JPEG/BMP 图片预览
- ✅ 资源替换（原地覆盖/数据移位）
- ✅ 资源导出
- ✅ 保存修改（自动备份）
- ✅ RES.H 自动解析

### 📝 文档

- ✅ README.md - 完整使用文档
- ✅ USAGE_EXAMPLES.md - 使用示例
- ✅ QUICKSTART.md - 快速入门
- ✅ BUILD_GUIDE.md - 编译运行指南
- ✅ PROJECT_SUMMARY.md - 项目总结

### 🎨 UI

- ✅ 现代化 WPF 界面
- ✅ 工具栏布局
- ✅ 资源列表 + 预览面板
- ✅ 状态栏显示

---

## 版本说明

### 版本号规则

采用语义化版本控制（Semantic Versioning）：`MAJOR.MINOR.PATCH`

- **MAJOR**: 不兼容的 API 变更
- **MINOR**: 向后兼容的功能新增
- **PATCH**: 向后兼容的问题修正

### 图标说明

- ✨ 新增功能
- 🐛 Bug 修复
- 📝 文档更新
- 🎨 UI 改进
- 🔧 技术实现
- ⚠️ Breaking Changes
- ✅ 完成项
