# RES.BIN 资源管理器 - 使用示例

## 示例 1: 替换开机 Logo

### 场景
UI 设计师提供了新的开机 Logo 图片 `new_power_on.jpg`，需要替换到固件中。

### 步骤

#### 方法 A: 使用资源管理工具（推荐）

1. **启动工具**
   ```bash
   cd tools
   RunResBinManager.bat
   ```

2. **打开 RES.BIN**
   - 点击 📂 Open 按钮
   - 选择 `ax32_platform_demo/resource/RES.BIN`
   - 等待解析完成（约 1-2 秒）

3. **找到开机 Logo 资源**
   - 在列表中查找 ID=78 或 Name=RES_POWER_ON
   - 点击选中该行

4. **预览原图**
   - 右侧面板显示当前 Logo 预览
   - 确认这是要替换的资源

5. **替换资源**
   - 点击 🔄 Replace 按钮
   - 选择 `new_power_on.jpg`
   - 如果文件大小差异较大，会弹出警告
   - 点击 Yes 确认继续

6. **查看新预览**
   - 右侧面板更新为新 Logo 预览
   - 确认效果满意

7. **保存修改**
   - 点击 💿 Save 按钮
   - 保存为 `RES_modified.bin`
   - 程序自动创建备份 `RES_modified.bin.backup`

8. **重新打包固件**
   ```batch
   cd ax32_platform_demo\resource
   copy RES_modified.bin RES.BIN
   cd ..\output
   make.bat
   ```

9. **烧录测试**
   - 烧录生成的 DestBin.bin
   - 重启设备验证新 Logo

#### 方法 B: 直接替换源文件后重新打包

```batch
cd ax32_platform_demo\resource\resTable
copy D:\design\new_power_on.jpg power_on.jpg
cd ..
GenRes.bat
cd ..\output
make.bat
```

---

## 示例 2: 批量替换多个图标

### 场景
需要同时更新开机 Logo、关机 Logo 和 USB 充电图标。

### 步骤

1. **准备文件**
   ```
   D:\new_icons\
   ├── power_on.jpg      (新开机 Logo)
   ├── power_off.jpg     (新关机 Logo)
   └── usbbat_100.jpg    (新充电图标)
   ```

2. **启动工具并打开 RES.BIN**

3. **依次替换每个资源**

   **替换开机 Logo (ID=78)**:
   - 选中 RES_POWER_ON
   - 点击 Replace → 选择 power_on.jpg
   
   **替换关机 Logo (ID=77)**:
   - 选中 RES_POWER_OFF
   - 点击 Replace → 选择 power_off.jpg
   
   **替换充电图标 (ID=86)**:
   - 选中 RES_USBBAT_100
   - 点击 Replace → 选择 usbbat_100.jpg

4. **检查修改状态**
   - Status 列显示 "✏ Modified" 表示已修改
   - 确认三个资源都已修改

5. **保存**
   - 点击 Save
   - 保存为 `RES_batch_update.bin`

6. **重新打包并测试**

---

## 示例 3: 导出资源进行检查

### 场景
设备显示异常，怀疑某个资源文件损坏，需要导出检查。

### 步骤

1. **打开 RES.BIN**

2. **找到可疑资源**
   - 例如：RES_MAIN_BK (ID=44) 主菜单背景

3. **导出资源**
   - 选中该资源
   - 点击 💾 Export
   - 保存为 `main_bk_exported.jpg`

4. **用图片查看器打开**
   - 检查图片是否完整
   - 对比设计原稿

5. **如果发现问题**
   - 准备正确的图片文件
   - 使用 Replace 功能替换
   - 保存并重新打包

---

## 示例 4: 调试资源偏移问题

### 场景
替换一个大尺寸图片后，后续资源显示异常。

### 诊断步骤

1. **记录原始信息**
   - 打开原始 RES.BIN
   - 记录要替换资源的 Offset 和 Size
   - 例如：ID=78, Offset=0x1A000, Size=41267

2. **执行替换**
   - 替换为新的大文件（例如 80KB）
   - 观察控制台输出：
     ```
     Replacing resource 78:
       Old: offset=0x0001A000, size=41267
       New: size=81920
       ⚠ Larger file, need to shift 40653 bytes
         Updated resource 79: 0x00024113 → 0x0002DFB6
         Updated resource 80: 0x0002DFB6 → 0x00037E59
         ...
     ```

3. **检查后续资源**
   - 滚动列表查看 ID>78 的资源
   - 确认它们的 Offset 都已更新

4. **验证完整性**
   - 逐个预览后续资源
   - 确认都能正常显示

5. **如果发现问题**
   - 恢复备份文件
   - 尝试使用更小的图片或
   - 使用方法 A（重新打包）代替直接修改

---

## 示例 5: 命令行批量操作（高级）

### 创建批处理脚本

创建 `batch_replace.bat`:

```batch
@echo off
REM 批量替换资源脚本

set RES_BIN=ax32_platform_demo\resource\RES.BIN
set OUTPUT=ax32_platform_demo\resource\RES_updated.bin

echo Starting batch replacement...

REM 使用 ResBinManager 的命令行模式（需扩展实现）
ResBinManager.exe --input %RES_BIN% ^
    --replace 78=D:\icons\power_on.jpg ^
    --replace 77=D:\icons\power_off.jpg ^
    --replace 86=D:\icons\usbbat_100.jpg ^
    --output %OUTPUT%

if %ERRORLEVEL% EQU 0 (
    echo Batch replacement successful!
    echo Output: %OUTPUT%
) else (
    echo Batch replacement failed!
)

pause
```

---

## 常见问题解答

### Q1: 替换后预览正常，但设备上显示异常？

**A**: 可能原因：
1. 图片格式不兼容（例如 Progressive JPEG）
2. 图片尺寸超出 LCD 分辨率
3. 颜色空间问题（CMYK vs RGB）

**解决**:
- 使用标准 Baseline JPEG
- 确保尺寸不超过 LCD 分辨率
- 转换为 sRGB 颜色空间

### Q2: 如何知道哪个 ID 对应哪个资源？

**A**: 
- 工具会自动从 RES.H 读取名称映射
- 如果没有 RES.H，会显示 Resource_0, Resource_1 等
- 可以对照 RES.H 文件手动查找

### Q3: 替换音频文件（WAV）需要注意什么？

**A**:
- 保持相同的采样率和位深
- 当前 SDK 使用 16kHz, 16-bit mono
- 文件大小变化会影响播放时长

### Q4: 可以同时替换多少个资源？

**A**:
- 理论上无限制
- 建议每次不超过 10 个，便于调试
- 大量替换建议使用重新打包方法

### Q5: 修改后的文件比原文件大很多，有问题吗？

**A**:
- 只要 SPI Flash 有足够空间就没问题
- 注意 Boot Sector 中的资源区大小可能需要更新
- 建议使用 MakeSPIBin.exe 重新合并时会自动处理

---

## 最佳实践

1. ✅ **始终备份**: 修改前备份原始 RES.BIN
2. ✅ **小步迭代**: 一次修改 1-2 个资源，逐步测试
3. ✅ **保持格式**: 使用相同的文件格式和编码
4. ✅ **验证预览**: 替换后立即预览确认
5. ✅ **文档记录**: 记录每次修改的内容和原因
6. ✅ **版本控制**: 将 RES.BIN 纳入 Git 管理
7. ✅ **团队沟通**: UI 变更通知团队成员

---

**提示**: 对于生产环境，建议使用方法 A（重新打包）而非直接修改二进制文件，以确保最大的安全性和兼容性。
