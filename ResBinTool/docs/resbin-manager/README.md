# ResBinManager 文档索引

本文件夹包含 ResBinManager 工具的所有技术文档，按主题分类整理。

## 📁 目录结构

```
docs/resbin-manager/
├── analysis/          # 项目分析和总结报告
├── bugfixes/         # Bug 修复记录
├── build/            # 构建和编译相关文档
├── destbin/          # DestBin.bin 格式和处理
├── features/         # 功能特性说明
├── guides/           # 使用指南
└── testing/          # 测试指南
```

---

## 📊 文档统计

| 分类 | 文件数量 | 说明 |
|------|---------|------|
| **analysis** | 8 | 项目分析、阶段报告、变更日志 |
| **bugfixes** | 3 | Bug 修复记录和问题诊断 |
| **build** | 5 | 构建指南、编译问题、ELF 分析 |
| **destbin** | 15 | DestBin 格式解析、偏移量修复、兼容性 |
| **features** | 24 | 功能特性、资源替换、预览优化 |
| **guides** | 2 | 快速开始、使用示例 |
| **testing** | 2 | 功能测试指南 |
| **总计** | **59** | 完整的技术文档集 |

---

## 📖 各分类详细说明

### 1. `analysis/` - 项目分析和总结

**用途**：项目整体分析、阶段性总结、变更记录

**文件列表**：
- **CHANGELOG.md** - 完整的变更日志
- **FEATURE_DEMO.md** - 功能演示说明
- **FILE_MANIFEST.md** - 文件清单和项目结构
- **IMPLEMENTATION_SUMMARY.md** - 实现总结报告
- **PHASE1_COMPLETION_REPORT.md** - 第一阶段完成报告
- **PHASE2_COMPLETION_REPORT.md** - 第二阶段完成报告
- **PHASE2_SUMMARY.md** - 第二阶段总结
- **PROJECT_SUMMARY.md** - 项目总体总结

**何时查看**：
- 了解项目整体进展
- 查看历史变更记录
- 回顾开发阶段成果

---

### 2. `bugfixes/` - Bug 修复记录

**用途**：记录重要 Bug 的发现、分析和修复过程

**文件列表**：
- **BUGFIX_NULL_REFERENCE_EXCEPTION.md** - 空引用异常修复
- **FIRST_RESOURCE_TYPE_DETECTION_DEBUG.md** - 首个资源类型检测调试
- **PREVIEW_CLEANUP_OPTIMIZATION.md** - 预览面板清空优化

**何时查看**：
- 遇到类似问题时参考解决方案
- 了解已知问题和修复方法
- 学习调试技巧

---

### 3. `build/` - 构建和编译

**用途**：构建流程、编译问题、ELF 文件分析

**文件列表**：
- **BUILD_FIXES.md** - 构建问题修复
- **BUILD_GUIDE.md** - 构建指南
- **BUILD_PANEL_VISIBILITY.md** - 构建面板可见性
- **ELF_ANALYSIS.md** - ELF 文件格式分析
- **FIRMWARE_BUILD_GUIDE.md** - 固件构建指南

**何时查看**：
- 需要编译项目时
- 遇到构建错误时
- 分析 ELF 文件结构时

---

### 4. `destbin/` - DestBin.bin 格式和处理

**用途**：DestBin.bin 文件格式解析、偏移量检测、兼容性问题

**文件列表**：
- **AX329X_DESTBIN_COMPATIBILITY_FIX.md** - AX329X 平台兼容性修复
- **DESTBIN_DATA_CORRUPTION_FIX.md** - 数据损坏修复
- **DESTBIN_DIRECT_REPLACE_ANALYSIS.md** - 直接替换分析
- **DESTBIN_INTEGRATION_COMPLETE.md** - DestBin 集成完成报告
- **DESTBIN_LOAD_FAILURE_DIAGNOSIS.md** - 加载失败诊断
- **DESTBIN_OFFSET_FIX.md** - 偏移量修复
- **DESTBIN_PARSER_IMPLEMENTATION.md** - 解析器实现
- **DESTBIN_RESOURCE_NAME_FIX.md** - 资源名称修复
- **DESTBIN_SAVE_RELOAD_ISSUE_ANALYSIS.md** - 保存重载问题分析
- **DESTBIN_STRUCTURE_VERIFICATION.md** - 结构验证
- **DESTBIN_STRUCTURE_VISUALIZATION.md** - 结构可视化
- **DESTBIN_UI_INTEGRATION_COMPLETE.md** - UI 集成完成
- **FILENAME_DETECTION_UPDATE.md** - 文件名检测更新
- **FINAL_OFFSET_FIX.md** - 最终偏移量修复
- **OFFSET_SYNC_FIX.md** - 偏移量同步修复

**何时查看**：
- 处理 DestBin.bin 文件时
- 遇到偏移量问题时
- 需要理解 DestBin 格式时
- 跨平台兼容性问题

**重要性**：⭐⭐⭐⭐⭐ 核心功能文档

---

### 5. `features/` - 功能特性

**用途**：详细的功能说明、实现细节、使用场景

**文件列表**：

#### 资源替换相关
- **BINARY_RESOURCE_DEFAULT_ICON.md** - 二进制资源默认图标
- **BIN_FILE_PACKING_FEATURE.md** - BIN 文件打包功能
- **REPLACE_SIZE_CONFIRMATION.md** - 替换大小确认
- **RESOURCE_REPLACE_SIZE_HANDLING.md** - 资源替换大小处理
- **RESOURCE_REPLACE_SIZE_HANDLING_DETAILED.md** - 详细的大小处理说明
- **SAVE_OPERATION_OPTIMIZATION.md** - 保存操作优化
- **SMART_FILE_OPERATIONS_INTEGRATION.md** - 智能文件操作集成

#### 预览功能
- **IMAGE_PREVIEW_AUTO_UPDATE.md** - 图片预览自动更新
- **PREVIEW_BUTTON_FUNCTION_SPEC.md** - 预览按钮功能规范
- **PREVIEW_BUTTON_OPTIMIZATION.md** - 预览按钮优化

#### Font 和 WAV 资源
- **FONT_DEBUG_GUIDE.md** - Font 调试指南
- **FONT_QUICK_TEST.md** - Font 快速测试
- **FONT_REPLACE_IMPLEMENTATION_SUMMARY.md** - Font 替换实现总结
- **FONT_REPLACE_QUICK_TEST.md** - Font 替换快速测试
- **FONT_WAV_ENHANCEMENT_PLAN.md** - Font/WAV 增强计划
- **WAV_FEATURE_GUIDE.md** - WAV 功能指南
- **WAV_FONT_REPLACE_ANALYSIS.md** - WAV/Font 替换分析
- **WAV_QUICK_TEST.md** - WAV 快速测试
- **WAV_VALIDATION_IMPLEMENTATION_SUMMARY.md** - WAV 验证实现总结
- **WAV_VALIDATION_QUICK_TEST.md** - WAV 验证快速测试
- **WAV_VALIDATION_TEST_GUIDE.md** - WAV 验证测试指南

#### 其他功能
- **FIRMWARE_VERSION_DISPLAY.md** - 固件版本显示
- **RES_H_FILTER_FEATURE.md** - RES.H 过滤功能
- **REVERT_FEATURE_SPEC.md** - 回退功能规范

**何时查看**：
- 使用特定功能时
- 需要了解功能实现细节时
- 开发新功能时参考

**重要性**：⭐⭐⭐⭐⭐ 核心功能文档

---

### 6. `guides/` - 使用指南

**用途**：快速入门和使用示例

**文件列表**：
- **QUICKSTART.md** - 快速开始指南
- **USAGE_EXAMPLES.md** - 使用示例

**何时查看**：
- 首次使用 ResBinManager
- 需要快速了解基本操作
- 查找常用操作示例

**重要性**：⭐⭐⭐⭐ 新手必读

---

### 7. `testing/` - 测试指南

**用途**：功能测试方法和步骤

**文件列表**：
- **BIN_FILE_QUICK_TEST.md** - BIN 文件快速测试
- **REVERT_TEST_GUIDE.md** - 回退功能测试指南

**何时查看**：
- 验证功能是否正常
- 回归测试
- 新功能测试

---

## 🔍 快速查找指南

### 按主题查找

| 我想了解... | 查看这里 |
|------------|---------|
| **如何开始使用** | `guides/QUICKSTART.md` |
| **DestBin.bin 格式** | `destbin/` 文件夹（15个文档） |
| **资源替换功能** | `features/RESOURCE_REPLACE_*.md` |
| **Font 资源处理** | `features/FONT_*.md` |
| **WAV 资源处理** | `features/WAV_*.md` |
| **预览功能** | `features/PREVIEW_*.md`, `features/IMAGE_PREVIEW_*.md` |
| **构建项目** | `build/BUILD_GUIDE.md` |
| **Bug 修复** | `bugfixes/` 文件夹 |
| **项目历史** | `analysis/CHANGELOG.md` |
| **测试方法** | `testing/` 文件夹 |

### 按问题类型查找

| 遇到的问题 | 查看这里 |
|-----------|---------|
| DestBin 加载失败 | `destbin/DESTBIN_LOAD_FAILURE_DIAGNOSIS.md` |
| 资源数据损坏 | `destbin/DESTBIN_DATA_CORRUPTION_FIX.md` |
| 偏移量错误 | `destbin/FINAL_OFFSET_FIX.md`, `destbin/OFFSET_SYNC_FIX.md` |
| 空引用异常 | `bugfixes/BUGFIX_NULL_REFERENCE_EXCEPTION.md` |
| 预览未清空 | `bugfixes/PREVIEW_CLEANUP_OPTIMIZATION.md` |
| 编译错误 | `build/BUILD_FIXES.md` |

---

## 📝 文档维护建议

### 新增文档时
1. **确定分类**：根据文档主题选择合适的子文件夹
2. **命名规范**：使用大写字母和下划线，如 `FEATURE_NAME.md`
3. **更新索引**：在本文件中添加新文档的条目
4. **交叉引用**：在相关文档中添加链接

### 定期清理
1. **合并重复**：检查是否有内容重复的文档
2. **归档过时**：将过时的文档标记为已废弃
3. **更新链接**：确保所有内部链接有效

---

## 🔗 相关文档

- **主 README**：[ResBinManager/README.md](../../ResBinManager/README.md)
- **Tools 总览**：[tools/README.md](../README.md)
- **快速参考**：[tools/QUICK_REFERENCE.md](../QUICK_REFERENCE.md)

---

## 📅 最后更新

**更新日期**：2026-05-20  
**文档总数**：59 个 Markdown 文件  
**整理状态**：✅ 已完成
