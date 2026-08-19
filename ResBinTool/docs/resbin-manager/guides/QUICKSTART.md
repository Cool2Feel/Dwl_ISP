# 🚀 RES.BIN Resource Manager - 5分钟快速入门

## 前置要求

✅ **已安装**: .NET 6.0 SDK 或更高版本  
✅ **操作系统**: Windows 10/11  
✅ **文件准备**: `ax32_platform_demo/resource/RES.BIN`

---

## Step 1: 启动工具（30秒）

### 方法 A: 使用批处理脚本（推荐）

```batch
cd d:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\tools
RunResBinManager.bat
```

脚本会自动：
- 检查 .NET 是否安装
- 还原依赖包
- 编译项目
- 启动应用

### 方法 B: 手动运行

```batch
cd ResBinManager
dotnet run
```

---

## Step 2: 打开 RES.BIN（1分钟）

1. 点击左上角 **📂 Open** 按钮
2. 浏览到 `ax32_platform_demo/resource/RES.BIN`
3. 点击 "打开"
4. 等待 1-2 秒，资源列表加载完成

**成功标志**: 
- 左侧显示 94 个资源
- 状态栏显示 "Loaded 94 resources from RES.BIN"
- 弹出成功提示框

---

## Step 3: 浏览资源（1分钟）

### 查看列表

左侧表格显示所有资源：
- **ID**: 资源编号 (0-93)
- **Name**: 资源名称（如 RES_POWER_ON）
- **Type**: 类型 (Jpeg/Bitmap/Wav/Binary)
- **Offset**: 文件偏移地址
- **Size**: 文件大小
- **Status**: ✓ Original（原始）或 ✏ Modified（已修改）

### 预览图片

1. 点击任意资源行（例如 ID=78 RES_POWER_ON）
2. 右侧面板自动显示：
   - 图片预览（如果是 JPEG/BMP）
   - 资源属性详情
3. 双击资源行也可触发预览

---

## Step 4: 替换资源（2分钟）

### 示例：替换开机 Logo

1. **选中资源**
   - 在列表中找到 `RES_POWER_ON` (ID=78)
   - 单击选中

2. **点击替换**
   - 点击工具栏 **🔄 Replace** 按钮

3. **选择新文件**
   - 浏览到您的新图片文件（例如 `D:\new_logo.jpg`）
   - 点击 "打开"

4. **确认操作**
   - 如果新文件比原文件大很多，会弹出警告
   - 点击 "Yes" 继续

5. **查看结果**
   - 右侧预览更新为新图片
   - Status 列变为 "✏ Modified"（橙色）
   - 弹出成功提示

---

## Step 5: 保存修改（30秒）

1. 点击工具栏 **💿 Save** 按钮
2. 选择保存位置（建议保存到 resource 目录）
3. 文件名建议：`RES_modified.bin`
4. 点击 "保存"
5. 程序自动创建备份文件 `.backup`

**成功标志**:
- 状态栏显示 "✓ Saved to ..."
- 弹出成功提示框，包含后续步骤说明

---

## Step 6: 重新打包固件（外部步骤）

### 方法 A: 替换后重新打包

```batch
cd ax32_platform_demo\resource
copy RES_modified.bin RES.BIN
cd ..\output
make.bat
```

### 方法 B: 使用内置固件打包功能 ⭐ 推荐

1. **切换到打包面板**
   - 点击工具栏的 **⚙️ Config** 按钮

2. **配置参数**（首次需要）
   - ELF File: 选择 `ax32_platform_demo/Debug/ax329x_sdk.elf`
   - MakeSPIBin.exe: 选择 `ax32_platform_demo/output/MakeSPIBin.exe`
   - Output Directory: 自动推断为 `ax32_platform_demo/output/`

3. **开始打包**
   - 点击 **🔨 Build Firmware** 按钮
   - 等待进度条完成
   - 查看日志和结果提示

4. **获取 DestBin.bin**
   - 打包成功后，文件位于输出目录
   - 文件夹会自动打开（如果启用了该选项）

---

## Step 7: 烧录测试

1. 获取生成的 `DestBin.bin`
2. 使用编程器或 USB 升级功能烧录
3. 重启设备
4. 验证新 Logo 显示正常

---

## ✅ 完成！

您已成功完成第一次资源替换！

---

## 🎯 下一步

### 学习更多功能

- 📖 阅读 [完整文档](README.md)
- 💡 查看 [使用示例](USAGE_EXAMPLES.md)
- 🔧 探索高级功能

### 常见操作

| 操作 | 按钮 | 快捷键 |
|------|------|--------|
| 打开文件 | 📂 Open | Ctrl+O |
| 替换资源 | 🔄 Replace | Ctrl+R |
| 导出资源 | 💾 Export | Ctrl+E |
| 保存修改 | 💿 Save | Ctrl+S |
| 预览资源 | 👁 Preview | Space |

### 实用技巧

💡 **批量替换**: 依次选中多个资源进行替换，最后统一保存

💡 **对比效果**: 导出原资源和新资源，用图片查看器对比

💡 **撤销操作**: 如果替换错误，关闭程序重新打开原始 RES.BIN

💡 **安全检查**: 替换后务必预览确认，再保存

---

## ❓ 遇到问题？

### 问题 1: 无法启动程序

**症状**: 提示 ".NET SDK not found"

**解决**: 
```bash
# 检查是否安装
dotnet --version

# 如果未安装，下载并安装
# https://dotnet.microsoft.com/download
```

### 问题 2: 无法解析 RES.BIN

**症状**: "Cannot detect resource table offset"

**解决**:
- 确认文件路径正确
- 确认文件未损坏
- 尝试其他 RES.BIN 文件

### 问题 3: 图片预览失败

**症状**: 预览区域空白

**解决**:
- 确认资源是图片格式（JPEG/BMP）
- 尝试导出后用系统图片查看器打开
- 检查文件格式是否标准

### 需要帮助？

- 📧 联系开发团队
- 📝 查看完整文档
- 🔍 搜索常见问题

---

## 🎉 恭喜！

您现在已经掌握了 RES.BIN Resource Manager 的基本用法！

开始自定义您的 AX329x 设备界面吧！ 🚀

---

**提示**: 建议将此快速入门指南加入书签，方便随时查阅。
