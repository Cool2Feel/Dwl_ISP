# Tools 文件夹整理总结

## 📅 整理日期
2026-05-20

## 🎯 整理目标
对 `tools` 文件夹中的文件进行合理归类，方便后续的分析记录和查找。

---

## 📁 新的目录结构

### 1. **scripts/** - 脚本文件夹

#### `scripts/analysis/` (14个文件)
分析 DestBin.bin、RES.BIN 和资源结构的 Python 脚本。

**文件列表**：
- AnalyzeAX329X_DestBin.py
- AnalyzeAX329X_FontResources.py
- AnalyzeAX329X_res_bin.py
- AnalyzeDestBin_AX329X.py
- ListAX329X_AllResources.py
- AnalyzeMP3Font.py
- CompareDestBinFiles.py
- analyze_destbin.py
- analyze_destbin_detailed.py
- analyze_relative_offsets.py
- check_file_integrity.py
- check_first_resource.py
- verify_program_code_size.py

#### `scripts/detection/` (3个文件)
检测 RES.BIN 位置、资源表结构和字体条目的脚本。

**文件列表**：
- FindResBinInDestBin.py
- FindResBinOffset.py
- CheckAX329X_FontEntries.py

#### `scripts/testing/` (6个文件)
测试各种功能的脚本（Python 和 PowerShell）。

**文件列表**：
- TestFontDetection.ps1
- TestMP3FontDetection.py
- TestNewResourceTypes.ps1
- TestResHParser.py
- test_save_reload.ps1
- test_save_reload_simple.py

---

### 2. **docs/** - 文档文件夹

#### `docs/ax329x/` (4个文件)
AX329X 平台的分析和配置文档。

**文件列表**：
- AX329X_DESTBIN_RESBIN_LOCATION.md
- AX329X_FONT_RESOURCE_ISSUE_ANALYSIS.md
- AX329X_RESOURCE_ARCHITECTURE_ANALYSIS.md
- DESTBIN_AX329X_ANALYSIS.md

#### `docs/destbin/` (4个文件)
DestBin.bin 文件格式、解析和修复相关文档。

**文件列表**：
- DESTBIN_FIX_TESTING_GUIDE.md
- DESTBIN_PARSE_ISSUE_DIAGNOSIS.md
- FONT_RESOURCE_FIX_REPORT.md
- RES_H_PARSER_INTEGRATION_COMPLETE.md

#### `docs/resource-types/` (7个文件)
各种资源类型的检测、识别和处理文档。

**文件列表**：
- MP3FONT_RESOURCE_ANALYSIS.md
- MULTI_PLATFORM_RES_H_COMPATIBILITY.md
- RESOURCE_DETECTION_CHEATSHEET.md
- RESOURCE_DETECTION_DECISION_TREE.md
- RESOURCE_DETECTION_LOGIC_ANALYSIS.md
- RESOURCE_TYPE_EXTENSION_SUMMARY.md
- RESOURCE_TYPE_QUICK_REFERENCE.md

#### `docs/guides/` (1个文件)
通用使用指南和说明文档。

**文件列表**：
- Tools_README.md

---

### 3. **根目录保留文件**

以下文件保留在根目录，便于快速访问：

- `README.md` - 新创建的总览文档
- `ResBinManager.sln` - Visual Studio 解决方案文件
- `RunResBinManager.bat` - 快速启动脚本
- `ResBinManager/` - 主应用程序文件夹

---

## 📊 统计信息

| 类别 | 数量 | 说明 |
|------|------|------|
| Python 脚本 | 21 | 分析和检测脚本 |
| PowerShell 脚本 | 3 | 测试脚本 |
| Markdown 文档 | 16 | 分析和参考文档 |
| **总计** | **40** | **已整理的文件** |

---

## 🔍 查找指南

### 按功能查找

**需要分析 DestBin.bin？**
```bash
cd scripts/analysis
python AnalyzeAX329X_DestBin.py <文件路径>
```

**需要查找 RES.BIN 位置？**
```bash
cd scripts/detection
python FindResBinInDestBin.py <文件路径>
```

**需要测试功能？**
```bash
cd scripts/testing
.\TestFontDetection.ps1
```

### 按平台查找

**AX329X 相关文档**
```bash
cd docs/ax329x
ls *.md
```

**DestBin 格式文档**
```bash
cd docs/destbin
ls *.md
```

**资源类型文档**
```bash
cd docs/resource-types
ls *.md
```

---

## 💡 使用建议

### 1. 日常使用
- **运行工具**：双击 `RunResBinManager.bat`
- **查看文档**：阅读 `README.md` 了解整体结构
- **执行脚本**：进入对应的 `scripts/` 子文件夹

### 2. 开发调试
- **分析问题**：使用 `scripts/analysis/` 中的脚本
- **检测结构**：使用 `scripts/detection/` 中的脚本
- **验证功能**：使用 `scripts/testing/` 中的脚本

### 3. 学习参考
- **AX329X 平台**：查看 `docs/ax329x/`
- **DestBin 格式**：查看 `docs/destbin/`
- **资源类型**：查看 `docs/resource-types/`

---

## ✅ 整理效果

### 整理前
```
tools/
├── 40+ 个混杂的文件
├── .py 文件和 .md 文件混在一起
└── 难以快速定位所需内容
```

### 整理后
```
tools/
├── README.md                    # 清晰的导航文档
├── scripts/                     # 所有脚本
│   ├── analysis/               # 14个分析脚本
│   ├── detection/              # 3个检测脚本
│   └── testing/                # 6个测试脚本
├── docs/                       # 所有文档
│   ├── ax329x/                 # 4个AX329X文档
│   ├── destbin/                # 4个DestBin文档
│   ├── resource-types/         # 7个资源类型文档
│   └── guides/                 # 1个指南
└── ResBinManager/              # 主应用程序
```

---

## 🎉 优势

1. **结构清晰**：按功能和主题分类，一目了然
2. **易于查找**：明确的目录命名，快速定位
3. **便于维护**：新增文件有明确的存放位置
4. **文档完善**：README.md 提供完整的使用指南
5. **向后兼容**：保留了所有原有文件，只是重新组织

---

## 📝 后续建议

1. **定期清理**：删除过时的脚本和文档
2. **更新索引**：添加重要新功能时更新 README.md
3. **版本控制**：考虑将 docs/ 文件夹纳入 Git 管理
4. **自动化**：可以编写脚本自动整理新添加的文件

---

**整理完成时间**：2026-05-20  
**整理人员**：AI Assistant  
**审核状态**：✅ 已完成
