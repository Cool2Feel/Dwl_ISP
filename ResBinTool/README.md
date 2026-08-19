# Tools 文件夹结构说明

本文件夹包含 ResBinManager 工具相关的脚本、文档和辅助文件。

## 📁 目录结构

```
tools/
├── ResBinManager/           # 主应用程序（C# WPF）
├── scripts/                 # Python 和 PowerShell 脚本
│   ├── analysis/           # 分析脚本
│   ├── detection/          # 检测脚本
│   └── testing/            # 测试脚本
├── docs/                   # 文档
│   ├── ax329x/            # AX329X 平台相关文档
│   ├── destbin/           # DestBin 格式相关文档
│   ├── resource-types/    # 资源类型相关文档
│   ├── guides/            # 使用指南
│   └── resbin-manager/    # ResBinManager 详细文档（59个）
├── ResBinManager.sln       # Visual Studio 解决方案文件
└── RunResBinManager.bat    # 快速启动脚本
```

---

## 📜 Scripts 文件夹

### `scripts/analysis/` - 分析脚本

用于分析 DestBin.bin、RES.BIN 和资源结构的 Python 脚本。

**主要脚本**：
- `AnalyzeAX329X_DestBin.py` - 分析 AX329X DestBin.bin 文件结构
- `AnalyzeAX329X_FontResources.py` - 分析 AX329X 字体资源
- `AnalyzeAX329X_res_bin.py` - 分析 AX329X RES.BIN 文件
- `AnalyzeDestBin_AX329X.py` - DestBin 综合分析
- `ListAX329X_AllResources.py` - 列出 AX329X 所有资源
- `AnalyzeMP3Font.py` - 分析 MP3 字体资源
- `CompareDestBinFiles.py` - 比较多个 DestBin 文件
- `analyze_destbin.py` - 通用 DestBin 分析
- `analyze_destbin_detailed.py` - 详细 DestBin 分析
- `analyze_relative_offsets.py` - 分析相对偏移
- `check_file_integrity.py` - 检查文件完整性
- `check_first_resource.py` - 检查第一个资源
- `verify_program_code_size.py` - 验证程序代码大小

**使用方法**：
```bash
python scripts/analysis/AnalyzeAX329X_DestBin.py <DestBin.bin路径>
```

---

### `scripts/detection/` - 检测脚本

用于检测 RES.BIN 位置、资源表结构和字体条目的脚本。

**主要脚本**：
- `FindResBinInDestBin.py` - 在 DestBin.bin 中查找 RES.BIN
- `FindResBinOffset.py` - 查找 RES.BIN 偏移位置
- `CheckAX329X_FontEntries.py` - 检查 AX329X 字体条目

**使用方法**：
```bash
python scripts/detection/FindResBinInDestBin.py <DestBin.bin路径>
```

---

### `scripts/testing/` - 测试脚本

用于测试各种功能的脚本（Python 和 PowerShell）。

**主要脚本**：
- `TestFontDetection.ps1` - 测试字体检测功能
- `TestMP3FontDetection.py` - 测试 MP3 字体检测
- `TestNewResourceTypes.ps1` - 测试新资源类型
- `TestResHParser.py` - 测试 RES.H 解析器
- `test_save_reload.ps1` - 测试保存和重载功能
- `test_save_reload_simple.py` - 简化版保存重载测试

**使用方法**：
```powershell
# PowerShell 脚本
.\scripts\testing\TestFontDetection.ps1

# Python 脚本
python scripts/testing/TestMP3FontDetection.py
```

---

## 📚 Docs 文件夹

### `docs/resbin-manager/` - ResBinManager 详细文档 ⭐ 新增

**包含 59 个技术文档**，按主题分为 7 个分类：

#### 1. `analysis/` (8个文件)
项目分析、阶段报告、变更日志
- CHANGELOG.md - 完整变更日志
- PROJECT_SUMMARY.md - 项目总体总结
- PHASE1/PHASE2_COMPLETION_REPORT.md - 阶段完成报告

#### 2. `bugfixes/` (3个文件)
Bug 修复记录和问题诊断
- BUGFIX_NULL_REFERENCE_EXCEPTION.md - 空引用异常修复
- PREVIEW_CLEANUP_OPTIMIZATION.md - 预览面板清空优化

#### 3. `build/` (5个文件)
构建流程、编译问题、ELF 分析
- BUILD_GUIDE.md - 构建指南
- ELF_ANALYSIS.md - ELF 文件格式分析
- FIRMWARE_BUILD_GUIDE.md - 固件构建指南

#### 4. `destbin/` (15个文件) ⭐ 核心
DestBin 格式解析、偏移量修复、兼容性
- DESTBIN_PARSER_IMPLEMENTATION.md - 解析器实现
- DESTBIN_LOAD_FAILURE_DIAGNOSIS.md - 加载失败诊断
- AX329X_DESTBIN_COMPATIBILITY_FIX.md - AX329X 兼容性修复
- FINAL_OFFSET_FIX.md - 最终偏移量修复

#### 5. `features/` (24个文件) ⭐ 核心
功能特性、资源替换、预览优化
- **资源替换**: RESOURCE_REPLACE_SIZE_HANDLING.md, SAVE_OPERATION_OPTIMIZATION.md
- **预览功能**: PREVIEW_BUTTON_FUNCTION_SPEC.md, IMAGE_PREVIEW_AUTO_UPDATE.md
- **Font 资源**: FONT_*.md (5个文档)
- **WAV 资源**: WAV_*.md (6个文档)
- **其他功能**: RES_H_FILTER_FEATURE.md, FIRMWARE_VERSION_DISPLAY.md

#### 6. `guides/` (2个文件)
快速入门和使用示例
- QUICKSTART.md - 快速开始指南
- USAGE_EXAMPLES.md - 使用示例

#### 7. `testing/` (2个文件)
功能测试方法和步骤
- BIN_FILE_QUICK_TEST.md - BIN 文件快速测试
- REVERT_TEST_GUIDE.md - 回退功能测试指南

**完整索引**：查看 [docs/resbin-manager/README.md](docs/resbin-manager/README.md)

**整理报告**：查看 [docs/RESBIN_MANAGER_DOCS_ORGANIZATION.md](docs/RESBIN_MANAGER_DOCS_ORGANIZATION.md)

---

### `docs/ax329x/` - AX329X 平台文档

AX329X 平台的分析和配置文档。

**文档列表**：
- `AX329X_DESTBIN_RESBIN_LOCATION.md` - AX329X DestBin 和 RES.BIN 位置说明
- `AX329X_FONT_RESOURCE_ISSUE_ANALYSIS.md` - AX329X 字体资源问题分析
- `AX329X_RESOURCE_ARCHITECTURE_ANALYSIS.md` - AX329X 资源架构分析
- `DESTBIN_AX329X_ANALYSIS.md` - AX329X DestBin 格式深度分析

---

### `docs/destbin/` - DestBin 格式文档

DestBin.bin 文件格式、解析和修复相关文档。

**文档列表**：
- `DESTBIN_FIX_TESTING_GUIDE.md` - DestBin 修复测试指南
- `DESTBIN_PARSE_ISSUE_DIAGNOSIS.md` - DestBin 解析问题诊断
- `FONT_RESOURCE_FIX_REPORT.md` - 字体资源修复报告
- `RES_H_PARSER_INTEGRATION_COMPLETE.md` - RES.H 解析器集成完成报告
- `PREVIEW_CLEANUP_OPTIMIZATION.md` - 预览面板清空优化说明
- `AX329X_DESTBIN_COMPATIBILITY_FIX.md` - AX329X DestBin 兼容性修复
- `RES_H_FILTER_FEATURE.md` - RES.H 资源过滤功能说明

---

### `docs/resource-types/` - 资源类型文档

各种资源类型的检测、识别和处理文档。

**文档列表**：
- `MP3FONT_RESOURCE_ANALYSIS.md` - MP3 字体资源分析
- `MULTI_PLATFORM_RES_H_COMPATIBILITY.md` - 多平台 RES.H 兼容性说明
- `RESOURCE_DETECTION_CHEATSHEET.md` - 资源检测速查表
- `RESOURCE_DETECTION_DECISION_TREE.md` - 资源检测决策树
- `RESOURCE_DETECTION_LOGIC_ANALYSIS.md` - 资源检测逻辑分析
- `RESOURCE_TYPE_EXTENSION_SUMMARY.md` - 资源类型扩展总结
- `RESOURCE_TYPE_QUICK_REFERENCE.md` - 资源类型快速参考

---

### `docs/guides/` - 使用指南

通用使用指南和说明文档。

**文档列表**：
- `Tools_README.md` - 工具总览和使用说明

---

## 🚀 快速开始

### 1. 运行 ResBinManager

**方式 1**：双击批处理文件
```batch
RunResBinManager.bat
```

**方式 2**：使用 Visual Studio
```
打开 ResBinManager.sln
按 F5 运行
```

### 2. 分析 DestBin.bin 文件

```bash
# 分析 AX329X DestBin.bin
python scripts/analysis/AnalyzeAX329X_DestBin.py path/to/DestBin.bin

# 查找 RES.BIN 位置
python scripts/detection/FindResBinInDestBin.py path/to/DestBin.bin
```

### 3. 测试功能

```powershell
# 测试字体检测
.\scripts\testing\TestFontDetection.ps1

# 测试 RES.H 解析器
python scripts/testing/TestResHParser.py
```

---

## 📝 文件命名规范

### Python 脚本
- **分析类**：`Analyze*.py` 或 `analyze_*.py`
- **检测类**：`Find*.py` 或 `Check*.py`
- **测试类**：`Test*.py` 或 `test_*.py`

### PowerShell 脚本
- **测试类**：`Test*.ps1` 或 `test_*.ps1`

### Markdown 文档
- **分析报告**：`*_ANALYSIS.md`
- **修复报告**：`*_FIX.md` 或 `*_REPORT.md`
- **指南**：`*_GUIDE.md`
- **参考**：`*_REFERENCE.md` 或 `*_CHEATSHEET.md`

---

## 🔍 查找特定内容

### 查找 AX329X 相关文档
```bash
ls docs/ax329x/
```

### 查找 DestBin 分析脚本
```bash
ls scripts/analysis/ | Select-String "destbin"
```

### 查找资源检测文档
```bash
ls docs/resource-types/ | Select-String "detection"
```

---

## 💡 最佳实践

1. **新增脚本**：根据功能放入对应的 `scripts/` 子文件夹
2. **新增文档**：根据主题放入对应的 `docs/` 子文件夹
3. **保持命名一致**：遵循现有的命名规范
4. **更新索引**：如果添加了重要功能，更新相关文档

---

## 📞 联系方式

如有问题或建议，请联系开发团队。

---

**最后更新**：2026-05-20
