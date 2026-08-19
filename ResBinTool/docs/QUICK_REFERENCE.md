# Tools 文件夹快速参考

## 🚀 快速启动

```bash
# 运行 ResBinManager
.\RunResBinManager.bat

# 或打开 Visual Studio 解决方案
ResBinManager.sln
```

---

## 📂 目录速查

| 需要... | 去这里 | 示例 |
|---------|--------|------|
| **分析 DestBin** | `scripts/analysis/` | `python AnalyzeAX329X_DestBin.py file.bin` |
| **查找 RES.BIN** | `scripts/detection/` | `python FindResBinInDestBin.py file.bin` |
| **测试功能** | `scripts/testing/` | `.\TestFontDetection.ps1` |
| **AX329X 文档** | `docs/ax329x/` | 查看平台特定分析 |
| **DestBin 文档** | `docs/destbin/` | 查看格式说明 |
| **资源类型** | `docs/resource-types/` | 查看检测逻辑 |

---

## 🔍 常用命令

### 分析 AX329X DestBin
```bash
cd scripts/analysis
python AnalyzeAX329X_DestBin.py D:\path\to\DestBin.bin
```

### 查找 RES.BIN 位置
```bash
cd scripts/detection
python FindResBinInDestBin.py D:\path\to\DestBin.bin
```

### 测试字体检测
```powershell
cd scripts/testing
.\TestFontDetection.ps1
```

### 查看所有文档
```bash
# AX329X 相关
ls docs/ax329x/*.md

# DestBin 相关
ls docs/destbin/*.md

# 资源类型相关
ls docs/resource-types/*.md
```

---

## 📖 重要文档

- **README.md** - 完整的工具说明和目录结构
- **ORGANIZATION_SUMMARY.md** - 整理总结和统计信息
- **docs/guides/Tools_README.md** - 原始工具说明

---

## 💡 提示

- 所有 Python 脚本都支持 `-h` 或 `--help` 参数查看使用方法
- PowerShell 脚本可能需要设置执行策略：`Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`
- 建议将 `tools` 文件夹添加到系统 PATH，方便全局访问

---

**最后更新**：2026-05-20
