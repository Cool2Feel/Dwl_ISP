# WAV 格式验证 - 快速测试

## 🚀 快速开始

### 1. 编译项目

```powershell
cd d:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\tools\ResBinManager
dotnet build
```

**预期输出**:
```
ResBinManager net6.0-windows 成功，出现 1 警告
在 X.X 秒内生成 成功，出现 3 警告
```

### 2. 运行程序

```powershell
.\bin\Debug\net6.0-windows\ResBinManager.exe
```

或者在 Visual Studio 中按 `F5` 启动调试。

---

## 🧪 测试场景

### 场景 1: 替换有效 WAV 文件

**步骤**:
1. 打开包含 WAV 资源的 RES.BIN 文件
2. 在左侧列表中找到 WAV 资源（Type=Wav）
3. 选中该资源
4. 点击 "🔄 Replace" 按钮
5. 选择一个有效的 .wav 文件
6. 查看验证对话框

**预期结果**:
- ✅ 显示 "✓ Valid WAV file"
- ✅ 显示详细的音频参数
- ✅ 如果有原始 WAV，显示参数对比
- ✅ 可以点击 "Yes" 继续或 "No" 取消

### 场景 2: 尝试替换非 WAV 文件

**步骤**:
1. 选中 WAV 资源
2. 点击 "Replace"
3. 选择一个 .mp3、.txt 或其他非 WAV 文件

**预期结果**:
- ❌ 显示错误对话框
- ❌ 消息: "Invalid WAV file: Invalid file format..."
- ❌ 标题: "Validation Failed"
- ❌ 替换被阻止

### 场景 3: 替换不同参数的 WAV

**准备**:
- 原始 WAV: 44.1kHz, 16-bit, Mono
- 新 WAV: 22.05kHz, 8-bit, Stereo

**步骤**:
1. 执行替换
2. 观察验证对话框

**预期结果**:
- ⚠️ 显示黄色警告图标
- ⚠️ 参数对比显示变化:
  ```
  ⚠ Sample Rate: 44100 Hz → 22050 Hz
  ⚠ Channels: Mono → Stereo
  ⚠ Bits: 16-bit → 8-bit
  ```
- ⚠️ 可能有警告: "8-bit audio has limited dynamic range."

---

## 🔍 调试技巧

### 查看调试输出

在 Visual Studio 中：
1. 菜单栏: View → Output
2. 下拉选择: "Debug"
3. 运行程序并执行替换

**预期看到**:
```
[VM] SelectedResource changed: ID=5, Type=Wav, Name=RES_BEEP
[VM] Loading WAV preview
[WAV] Parsing wav file...
[WAV] SampleRate: 44100, Channels: 1, Bits: 16
```

### 检查验证逻辑

如果想查看验证细节，可以临时添加调试输出：

在 `WavValidator.cs` 的 `Validate()` 方法中添加：

```csharp
System.Diagnostics.Debug.WriteLine($"[WavValidator] Validating WAV file...");
System.Diagnostics.Debug.WriteLine($"[WavValidator] Size: {wavData.Length} bytes");
System.Diagnostics.Debug.WriteLine($"[WavValidator] RIFF: {riff}");
System.Diagnostics.Debug.WriteLine($"[WavValidator] WAVE: {wave}");
```

---

## ✅ 验证清单

完成以下检查确保功能正常：

- [ ] 程序能正常编译
- [ ] 程序能正常启动
- [ ] 能加载包含 WAV 的 RES.BIN
- [ ] WAV 资源能被正确识别
- [ ] 点击 Replace 能打开文件对话框
- [ ] 选择有效 WAV 文件能显示验证对话框
- [ ] 验证对话框显示正确的音频参数
- [ ] 选择无效文件能被拒绝
- [ ] 参数对比功能正常工作
- [ ] 警告系统正常触发
- [ ] 可以点击 Yes 完成替换
- [ ] 可以点击 No 取消替换
- [ ] 替换后预览能更新

---

## 🐛 常见问题

### Q1: 编译失败，提示找不到 List<>

**解决**: 确保 `WavValidator.cs` 顶部有：
```csharp
using System.Collections.Generic;
```

### Q2: 验证对话框不显示

**检查**:
1. 选中的资源 Type 是否为 `ResourceType.Wav`
2. 是否在 `ExecuteReplace()` 中调用了验证
3. 查看调试输出是否有异常

### Q3: 所有 WAV 文件都被拒绝

**可能原因**:
1. 文件确实不是 WAV 格式
2. 文件已损坏
3. 文件大小 < 44 字节

**解决**: 用音频播放器确认文件是否正常

### Q4: 没有显示参数对比

**原因**: 原始 WAV 信息未加载

**解决**: 
1. 确保之前已经选中过该 WAV 资源
2. 检查 `WavInfo` 属性是否为 null
3. 重新加载 RES.BIN 文件

---

## 📊 性能测试

### 测试大文件验证速度

准备一个 1MB 的 WAV 文件，执行替换。

**预期**:
- 验证时间 < 100ms
- UI 无明显卡顿
- 验证结果准确

### 测试批量替换

连续替换 5 个不同的 WAV 文件。

**预期**:
- 每次验证都独立进行
- 无内存泄漏
- 状态正确重置

---

## 🎯 成功标准

当以下所有条件满足时，认为功能实现成功：

✅ **功能性**
- 能有效区分 WAV 和非 WAV 文件
- 能正确解析 WAV 头部信息
- 能检测常见的格式错误
- 能显示详细的参数信息

✅ **用户体验**
- 验证对话框清晰易懂
- 参数对比直观明了
- 警告信息有帮助性
- 操作流程顺畅

✅ **可靠性**
- 不会误判有效文件
- 不会漏判无效文件
- 异常情况有适当处理
- 无崩溃或卡死

✅ **代码质量**
- 代码结构清晰
- 注释完整
- 易于维护和扩展
- 符合项目规范

---

## 📝 反馈和改进

如果发现问题或有改进建议，请记录：

1. **问题描述**: 
2. **复现步骤**: 
3. **预期行为**: 
4. **实际行为**: 
5. **截图/日志**: 

---

**祝测试顺利！** 🎉

如有问题，请参考：
- [WAV_VALIDATION_TEST_GUIDE.md](./WAV_VALIDATION_TEST_GUIDE.md) - 详细测试指南
- [WAV_VALIDATION_IMPLEMENTATION_SUMMARY.md](./WAV_VALIDATION_IMPLEMENTATION_SUMMARY.md) - 实现总结
- [WAV_FONT_REPLACE_ANALYSIS.md](./WAV_FONT_REPLACE_ANALYSIS.md) - 完整分析文档
