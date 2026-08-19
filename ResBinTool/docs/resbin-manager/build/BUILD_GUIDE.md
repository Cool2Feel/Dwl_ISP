# 编译和运行指南

## 📋 系统要求

### 必需软件

1. **.NET 6.0 SDK** 或更高版本
   - 下载地址: https://dotnet.microsoft.com/download
   - 验证安装: `dotnet --version`

2. **操作系统**: Windows 10/11 (WPF 仅支持 Windows)

3. **IDE** (可选，推荐):
   - Visual Studio 2022 (Community 版免费)
   - VS Code + C# 扩展
   - JetBrains Rider

---

## 🚀 快速启动（3 步）

### 方法 1: 使用批处理脚本（最简单）

```batch
cd d:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\tools
RunResBinManager.bat
```

脚本会自动完成所有步骤！

### 方法 2: 命令行方式

```batch
# 进入项目目录
cd d:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\tools\ResBinManager

# 还原依赖
dotnet restore

# 编译
dotnet build

# 运行
dotnet run
```

### 方法 3: Visual Studio

1. 双击 `ResBinManager.sln`
2. 等待解决方案加载
3. 按 `F5` 或点击 "启动" 按钮

---

## 🔨 详细编译步骤

### Step 1: 验证环境

```bash
# 检查 .NET 版本
dotnet --version
# 应显示: 6.0.x 或更高

# 检查 SDK 列表
dotnet --list-sdks
```

如果未安装，请访问 https://dotnet.microsoft.com/download 下载并安装。

### Step 2: 还原依赖

```bash
cd tools/ResBinManager
dotnet restore
```

预期输出:
```
正在确定要还原的项目…
已还原 D:\...\ResBinManager.csproj (用时 XX ms)。
```

### Step 3: 编译项目

```bash
# Debug 模式（包含调试信息）
dotnet build

# Release 模式（优化性能）
dotnet build --configuration Release
```

预期输出:
```
生成成功。
    0 个警告
    0 个错误
```

### Step 4: 运行应用

```bash
# 默认 Debug 模式
dotnet run

# 或指定 Release 模式
dotnet run --configuration Release
```

应用窗口应该立即打开！

---

## 📦 发布为独立应用

### 方式 1: 框架依赖（需要目标机器安装 .NET）

```bash
dotnet publish -c Release -o ./publish
```

**输出目录**: `ResBinManager/publish/`  
**大小**: ~5 MB  
**要求**: 目标机器需安装 .NET 6.0 Runtime

### 方式 2: 自包含（无需安装 .NET）

```bash
# Windows x64
dotnet publish -c Release -r win-x64 --self-contained -o ./publish-win-x64

# Windows x86
dotnet publish -c Release -r win-x86 --self-contained -o ./publish-win-x86
```

**输出目录**: `ResBinManager/publish-win-x64/`  
**大小**: ~60-80 MB  
**要求**: 无，可直接运行

**运行发布的版本**:
```bash
cd publish-win-x64
.\ResBinManager.exe
```

### 方式 3: 单文件发布

```bash
dotnet publish -c Release -r win-x64 --self-contained ^
    --no-embed-symbols ^
    -p:PublishSingleFile=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o ./publish-single
```

**优点**: 单个 exe 文件，便于分发  
**缺点**: 首次启动稍慢（解压）

---

## 🐛 故障排除

### 问题 1: "dotnet" 不是内部或外部命令

**原因**: .NET SDK 未安装或未添加到 PATH

**解决**:
1. 安装 .NET 6.0 SDK: https://dotnet.microsoft.com/download
2. 重启命令行窗口
3. 验证: `dotnet --version`

### 问题 2: 还原失败 - NuGet 连接超时

**原因**: 网络问题或代理设置

**解决**:
```bash
# 清除 NuGet 缓存
dotnet nuget locals all --clear

# 重试还原
dotnet restore

# 或使用国内镜像
dotnet nuget add source https://nuget.cdn.azure.cn/v3/index.json -n AzureCN
```

### 问题 3: 编译错误 - 找不到类型或命名空间

**原因**: 依赖未正确还原

**解决**:
```bash
# 清理并重新构建
dotnet clean
dotnet restore
dotnet build
```

### 问题 4: 运行时错误 - 无法加载 DLL

**原因**: WPF 组件缺失

**解决**:
- 确认使用 Windows 系统
- 确认安装了 .NET Desktop Runtime
- 重新安装 .NET 6.0 SDK

### 问题 5: 中文乱码

**原因**: 控制台编码问题

**解决**:
```bash
# 设置 UTF-8 编码
chcp 65001
dotnet run
```

或在 PowerShell 中:
```powershell
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
dotnet run
```

---

## ⚙️ 高级配置

### 修改目标框架

编辑 `ResBinManager.csproj`:

```xml
<!-- 改为 .NET 7.0 -->
<TargetFramework>net7.0-windows</TargetFramework>

<!-- 或 .NET 8.0 -->
<TargetFramework>net8.0-windows</TargetFramework>
```

### 启用 nullable 检查

已在项目中启用:
```xml
<Nullable>enable</Nullable>
```

### 自定义版本号

编辑 `ResBinManager.csproj`:

```xml
<Version>1.0.1</Version>
<FileVersion>1.0.1.0</FileVersion>
<AssemblyVersion>1.0.1.0</AssemblyVersion>
```

---

## 🧪 测试建议

### 功能测试清单

- [ ] 打开有效的 RES.BIN 文件
- [ ] 浏览资源列表
- [ ] 预览 JPEG 图片
- [ ] 预览 BMP 图片
- [ ] 替换小尺寸资源
- [ ] 替换大尺寸资源
- [ ] 导出资源到文件
- [ ] 保存修改后的文件
- [ ] 验证备份文件创建
- [ ] 测试错误处理（打开无效文件）

### 性能测试

```bash
# 测量启动时间
Measure-Command { dotnet run }

# 监控内存使用
# 任务管理器 → 详细信息 → ResBinManager.exe
```

---

## 📊 编译选项说明

| 选项 | 说明 | 推荐值 |
|------|------|--------|
| Debug | 包含调试符号，便于调试 | 开发时使用 |
| Release | 优化代码，性能更好 | 发布时使用 |
| Any CPU | 兼容 x86 和 x64 | ✅ 推荐 |
| x64 | 仅 64 位系统 | 高性能需求 |
| x86 | 仅 32 位系统 | 兼容性需求 |

---

## 🔄 持续集成（可选）

### GitHub Actions 示例

创建 `.github/workflows/build.yml`:

```yaml
name: Build

on: [push, pull_request]

jobs:
  build:
    runs-on: windows-latest
    
    steps:
    - uses: actions/checkout@v2
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v1
      with:
        dotnet-version: 6.0.x
    
    - name: Restore dependencies
      run: dotnet restore tools/ResBinManager
    
    - name: Build
      run: dotnet build tools/ResBinManager --no-restore
    
    - name: Test
      run: dotnet test tools/ResBinManager --no-build
```

---

## 📝 开发提示

### 调试技巧

1. **断点调试**
   ```bash
   # 在 Visual Studio 中按 F9 设置断点
   # 按 F5 启动调试
   # 按 F10/F11 单步执行
   ```

2. **查看日志**
   - 控制台输出实时日志
   - 检查异常信息

3. **热重载** (.NET 6+)
   ```bash
   dotnet watch run
   ```
   修改代码后自动重启应用

### 代码规范

- ✅ 遵循 C# 命名约定
- ✅ 添加 XML 文档注释
- ✅ 使用 async/await 进行异步操作
- ✅ 实现 IDisposable 管理资源

---

## 🎯 下一步

编译成功后：

1. 📖 阅读 [快速入门指南](QUICKSTART.md)
2. 💡 查看 [使用示例](USAGE_EXAMPLES.md)
3. 🔧 开始使用工具替换资源！

---

## 📞 需要帮助？

- 📧 Email: sdk-support@ax329x.com
- 📚 文档: [README.md](README.md)
- 🐛 问题报告: GitHub Issues

---

**祝您编译顺利！** 🎉
