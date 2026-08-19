# WAV 音频播放功能 - 快速测试指南

## 🚀 5分钟快速体验

### 步骤 1: 启动程序

```bash
cd tools/ResBinManager
dotnet run
```

或者直接运行编译好的可执行文件：
```bash
.\bin\Debug\net6.0-windows\ResBinManager.exe
```

### 步骤 2: 打开 RES.BIN 文件

1. 点击工具栏的 **"Open"** 按钮（或按 Ctrl+O）
2. 浏览到 `ax32_platform_demo/resource` 目录
3. 选择 `RES.BIN` 文件
4. 点击 **"打开"**

### 步骤 3: 找到 WAV 资源

在资源列表中查找类型为 **WAV** 的资源，例如：
- 滚动列表查看 "Type" 列
- 或者查找名称包含 "SOUND"、"MUSIC"、"BEEP" 等的资源

常见的 WAV 资源 ID：
- ID 1-10: 系统音效
- ID 11-20: UI 交互音效
- ID 21-30: 游戏音效

### 步骤 4: 试听音频

1. **单击** 任意 WAV 资源
2. 右侧会自动显示 **WAV 控制面板**
3. 查看音频信息：
   ```
   🎵 Audio Information
   Duration:    X.XXs
   Sample Rate: XXXX Hz
   Channels:    Mono/Stereo
   Format:      XX-bit
   ```
4. 点击 **"▶ Play"** 按钮开始播放
5. 拖动 **音量滑块** 调节音量
6. 点击 **"⏹ Stop"** 按钮停止播放

### 步骤 5: 切换资源

尝试以下操作：
1. 选中另一个 WAV 资源 → 面板自动更新
2. 选中 JPEG/BMP 资源 → 面板自动隐藏，显示图片预览
3. 再次选中 WAV 资源 → 面板重新显示

---

## 🎯 测试要点

### ✅ 正常功能测试

| 测试项 | 预期结果 |
|--------|---------|
| 选中 WAV 资源 | 控制面板自动显示 |
| 点击 Play | 音频开始播放 |
| 调节音量 | 音量实时变化 |
| 点击 Stop | 播放立即停止 |
| 切换到非 WAV | 面板自动隐藏 |
| 关闭窗口 | 无异常，资源正确释放 |

### ⚠️ 边界情况测试

| 测试场景 | 预期行为 |
|---------|---------|
| 无效 WAV 文件 | 显示错误提示，不崩溃 |
| 超大 WAV 文件 | 正常加载和播放 |
| 超小 WAV 文件 (< 1KB) | 正常解析或提示格式错误 |
| 连续快速切换资源 | 播放器正确释放旧资源 |
| 播放中关闭窗口 | 安全退出，无内存泄漏 |

---

## 🔍 调试技巧

### 查看详细信息

如果遇到问题，可以：

1. **检查输出窗口**（Visual Studio）
   - 查看是否有异常信息
   - 确认 NAudio 库正确加载

2. **验证 WAV 文件格式**
   ```csharp
   // 在代码中添加断点
   var isValid = WavInfoParser.IsValidWav(wavData);
   if (!isValid)
   {
       // 检查 wavData 的前 44 字节
       Console.WriteLine(BitConverter.ToString(wavData, 0, 44));
   }
   ```

3. **测试独立 WAV 文件**
   ```csharp
   // 创建临时测试程序
   byte[] data = File.ReadAllBytes("test.wav");
   var info = WavInfoParser.Parse(data);
   Console.WriteLine(info.FullDescription);
   ```

### 常见问题

**Q: 为什么没有声音？**
- 检查系统音量是否开启
- 确认应用程序音量滑块不是 0%
- 验证默认音频输出设备

**Q: 播放时卡顿？**
- 检查 WAV 文件大小（建议 < 1MB）
- 确认采样率在合理范围（8k-48k Hz）
- 关闭其他占用音频设备的程序

**Q: 某些 WAV 无法播放？**
- 确认是 PCM 格式（非压缩）
- 检查位深是否为 8/16/24/32-bit
- 验证文件头是否完整

---

## 📸 界面截图说明

### WAV 控制面板位置

```
┌─────────────────────────────────────────────┐
│  ResBinManager v1.2                          │
├──────────┬──────────┬───────────────────────┤
│          │          │                       │
│ 资源列表  │ 图片预览  │  WAV 控制面板         │ ← 这里！
│          │          │  (选中 WAV 时显示)     │
│          │          │                       │
│ ID Type  │ [Image]  │ 🎵 Audio Info         │
│ 1  JPEG  │          │ Duration: 2.35s       │
│ 2  WAV   │ ← 点击这里│ Sample Rate: 16000Hz │
│ 3  BMP   │          │ Channels: Mono        │
│ ...      │          │ Format: 16-bit        │
│          │          │                       │
│          │          │ [▶ Play] [⏹ Stop]    │
│          │          │                       │
│          │          │ 🔊 Volume: [====] 80%│
└──────────┴──────────┴───────────────────────┘
```

---

## 🎓 进阶使用

### 替换 WAV 资源

1. 选中要替换的 WAV 资源
2. 点击 **"Replace"** 按钮
3. 选择新的 WAV 文件
4. 确认替换（注意文件大小变化）
5. 点击 **"Save"** 保存修改

### 导出 WAV 资源

1. 选中 WAV 资源
2. 点击 **"Export"** 按钮
3. 选择保存路径
4. 在其他音频编辑器中处理

### 批量处理（需扩展）

未来版本可能支持：
- 批量导出所有 WAV 资源
- 批量替换多个音效
- 自动生成资源清单

---

## 📞 获取帮助

如果遇到问题：

1. **查看文档**
   - [WAV_FEATURE_GUIDE.md](WAV_FEATURE_GUIDE.md) - 完整使用指南
   - [IMPLEMENTATION_SUMMARY.md](IMPLEMENTATION_SUMMARY.md) - 技术实现细节

2. **检查日志**
   - 查看控制台输出
   - 检查异常信息

3. **联系开发团队**
   - 提供详细的错误描述
   - 附上相关的 RES.BIN 文件（如果可能）
   - 说明操作步骤和预期结果

---

**祝您使用愉快！** 🎵✨
