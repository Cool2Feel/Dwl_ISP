# Tools 目录说明

本目录包含 AX329x SDK 的各种开发工具和实用程序。

## 📁 工具列表

### 1. ResBinManager - RES.BIN 资源管理器 ⭐

**路径**: `tools/ResBinManager/`

**功能**: 交互式 RES.BIN 资源管理工具（WPF 应用）

**主要特性**:
- ✅ 可视化浏览所有 94 个资源
- ✅ JPEG/BMP 图片实时预览
- ✅ 安全替换单个资源
- ✅ 导出任意资源为文件
- ✅ 自动解析 RES.H 获取资源名称
- ✅ 智能处理文件大小变化
- ✅ 自动备份机制

**使用方法**:
```bash
# Windows 快速启动
cd tools
RunResBinManager.bat

# 或手动编译运行
cd ResBinManager
dotnet run
```

**详细文档**: 
- [README.md](ResBinManager/README.md) - 完整使用说明
- [USAGE_EXAMPLES.md](ResBinManager/USAGE_EXAMPLES.md) - 实际使用示例

---

## 🔧 其他工具（预留位置）

### MakeResBin.exe
**位置**: `ax32_platform_demo/resource/MakeResBin.exe`

**功能**: RES.BIN 打包工具（命令行）

**用途**: 将 resTable 目录中的文件打包成 RES.BIN

### MakeSPIBin.exe
**位置**: `ax32_platform_demo/output/MakeSPIBin.exe`

**功能**: SPI Flash 镜像合并工具

**用途**: 将 ELF 固件和 RES.BIN 合并成 DestBin.bin

---

## 📖 工作流程

### 标准资源更新流程

```
设计师提供新图片
    ↓
方法 A: 重新打包（推荐）
├─ 替换 resTable/ 中的源文件
├─ 运行 GenRes.bat
├─ 生成新的 RES.BIN 和 RES.H
└─ 继续固件编译

方法 B: 直接修改（快速）
├─ 使用 ResBinManager 打开 RES.BIN
├─ 替换指定资源
├─ 保存为 RES_modified.bin
├─ 复制到 resource/ 目录
└─ 继续固件编译
    ↓
运行 MakeSPIBin.exe
    ↓
生成 DestBin.bin
    ↓
烧录到设备测试
```

### 工具选择指南

| 场景 | 推荐工具 | 原因 |
|------|---------|------|
| 日常开发 | ResBinManager | 可视化、安全、快速验证 |
| 批量更新 | GenRes.bat + MakeResBin.exe | 自动化、可脚本化 |
| 生产构建 | GenRes.bat + MakeResBin.exe | 标准化流程 |
| 紧急修复 | ResBinManager | 无需重新编译整个项目 |
| 调试分析 | ResBinManager | 导出资源进行检查 |

---

## 🚀 快速开始

### 首次使用 ResBinManager

1. **安装 .NET SDK**
   ```bash
   # 下载并安装 .NET 6.0 SDK
   # https://dotnet.microsoft.com/download
   ```

2. **运行工具**
   ```bash
   cd tools
   RunResBinManager.bat
   ```

3. **打开 RES.BIN**
   - 点击 Open 按钮
   - 选择 `ax32_platform_demo/resource/RES.BIN`
   - 查看资源列表

4. **尝试替换**
   - 选中一个资源（例如 RES_POWER_ON）
   - 点击 Replace
   - 选择新图片
   - 预览效果

5. **保存测试**
   - 点击 Save
   - 保存为新文件
   - 按照提示重新打包固件

---

## 📝 开发说明

### 添加新工具

如需添加工具到此目录：

1. 创建工具子目录
2. 编写工具代码
3. 创建 README.md 说明文档
4. 更新本文件（Tools_README.md）
5. 如有需要，创建快速启动脚本

### 工具开发规范

- ✅ 提供清晰的 README 文档
- ✅ 包含使用示例
- ✅ 支持命令行参数（如适用）
- ✅ 提供错误提示和帮助信息
- ✅ 考虑跨平台兼容性

---

## 🔗 相关文档

- [AX329x SDK 资源组织与使用实战指南](../AX329x_SDK资源组织与使用实战指南.md)
- [RES.BIN 文件格式分析](../Config_Storage_System_Technical_Analysis.md)

---

## 💬 支持与反馈

如有问题或建议，请联系 AX329x SDK 开发团队。

---

**最后更新**: 2026-05-18  
**维护者**: AX329x SDK Team
