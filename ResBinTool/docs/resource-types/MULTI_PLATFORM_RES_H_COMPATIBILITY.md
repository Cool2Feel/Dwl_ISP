# 多平台 RES.H 兼容处理方案

## 📋 问题分析

不同平台使用不同的 RES.H 文件，资源索引完全不同：

| 平台 | 资源总数 | RES_RESFONT | RES_RESFONTIDX | 特点 |
|------|---------|-------------|----------------|------|
| **JT529X** | 94 (0-93) | 79 | 80 | 完整功能 |
| **AX329X** | 13 (0-12) | 9 | 10 | 精简版 |

**核心问题**: 不能硬编码资源索引，必须动态适配不同平台。

---

## 🔍 当前实现的问题

### 问题 1: 依赖固定索引

```csharp
// ❌ 错误做法：硬编码索引
var resfont = Resources[79];  // 只在 JT529X 有效
var resfontidx = Resources[80];  // 只在 JT529X 有效
```

**后果**: 
- 在 AX329X 上访问索引 79/80 会越界或获取错误资源
- 无法支持新平台

### 问题 2: 假设所有平台都有相同的资源

```csharp
// ❌ 错误做法：假设资源存在
if (Resources.Count > 80)
{
    var font = Resources[79];  // AX329X 只有 13 个资源！
}
```

**后果**:
- AX329X 只有 13 个资源，访问索引 79 会崩溃

---

## 💡 解决方案

### 方案 1: 基于 RES.H 解析的动态映射（推荐）

#### 步骤 1: 创建 RES.H 解析器

```csharp
public class ResHParser
{
    private Dictionary<string, int> _resourceMap = new Dictionary<string, int>();
    
    /// <summary>
    /// 解析 RES.H 文件，建立资源名称到索引的映射
    /// </summary>
    public bool Parse(string resHPath)
    {
        if (!File.Exists(resHPath))
            return false;
        
        _resourceMap.Clear();
        
        var lines = File.ReadAllLines(resHPath);
        foreach (var line in lines)
        {
            // 匹配 #define RES_XXX N 格式
            var match = System.Text.RegularExpressions.Regex.Match(
                line, 
                @"#define\s+(RES_\w+)\s+(\d+)");
            
            if (match.Success)
            {
                string resourceName = match.Groups[1].Value;
                int index = int.Parse(match.Groups[2].Value);
                _resourceMap[resourceName] = index;
                
                System.Diagnostics.Debug.WriteLine(
                    $"[ResHParser] {resourceName} = {index}");
            }
        }
        
        System.Diagnostics.Debug.WriteLine(
            $"[ResHParser] Parsed {_resourceMap.Count} resources from {Path.GetFileName(resHPath)}");
        
        return _resourceMap.Count > 0;
    }
    
    /// <summary>
    /// 获取资源索引，如果不存在返回 -1
    /// </summary>
    public int GetResourceIndex(string resourceName)
    {
        if (_resourceMap.TryGetValue(resourceName, out int index))
            return index;
        
        System.Diagnostics.Debug.WriteLine(
            $"[ResHParser] Resource '{resourceName}' not found in RES.H");
        
        return -1;
    }
    
    /// <summary>
    /// 检查资源是否存在
    /// </summary>
    public bool HasResource(string resourceName)
    {
        return _resourceMap.ContainsKey(resourceName);
    }
    
    /// <summary>
    /// 获取所有资源名称
    /// </summary>
    public List<string> GetAllResourceNames()
    {
        return _resourceMap.Keys.ToList();
    }
}
```

#### 步骤 2: 在 MainViewModel 中集成

```csharp
public class MainViewModel : INotifyPropertyChanged
{
    private ResHParser _resHParser = new ResHParser();
    
    /// <summary>
    /// 加载 RES.H 文件（自动检测平台）
    /// </summary>
    private void LoadResH(string destBinPath)
    {
        // 尝试在 DestBin.bin 同目录查找 RES.H
        string? destBinDir = Path.GetDirectoryName(destBinPath);
        
        if (destBinDir != null)
        {
            // 优先查找上级目录的 resource 文件夹
            string? parentDir = Directory.GetParent(destBinDir)?.FullName;
            string resHPath = Path.Combine(parentDir ?? destBinDir, "resource", "RES.H");
            
            if (File.Exists(resHPath))
            {
                if (_resHParser.Parse(resHPath))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[VM] Loaded RES.H from: {resHPath}");
                    
                    // 检测平台
                    DetectPlatform();
                    return;
                }
            }
        }
        
        System.Diagnostics.Debug.WriteLine("[VM] RES.H not found or failed to parse");
    }
    
    /// <summary>
    /// 检测平台并调整UI
    /// </summary>
    private void DetectPlatform()
    {
        int totalResources = _resHParser.GetAllResourceNames().Count;
        
        if (totalResources <= 20)
        {
            PlatformName = "AX329X (Lite)";
            System.Diagnostics.Debug.WriteLine("[VM] Detected AX329X platform (lite version)");
        }
        else
        {
            PlatformName = "JT529X (Full)";
            System.Diagnostics.Debug.WriteLine("[VM] Detected JT529X platform (full version)");
        }
    }
    
    /// <summary>
    /// 安全地获取字体资源
    /// </summary>
    private ResourceItem? GetFontResource(string resourceName)
    {
        int index = _resHParser.GetResourceIndex(resourceName);
        
        if (index < 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[VM] {resourceName} not defined in RES.H");
            return null;
        }
        
        if (index >= Resources.Count)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[VM] {resourceName} index {index} out of range (total: {Resources.Count})");
            return null;
        }
        
        var resource = Resources[index];
        
        // 验证资源有效性
        if (resource.Size == 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[VM] {resourceName} exists but has zero size");
            return null;
        }
        
        return resource;
    }
    
    /// <summary>
    /// 获取 RES_RESFONT 资源
    /// </summary>
    public ResourceItem? GetResFont()
    {
        return GetFontResource("RES_RESFONT");
    }
    
    /// <summary>
    /// 获取 RES_RESFONTIDX 资源
    /// </summary>
    public ResourceItem? GetResFontIdx()
    {
        return GetFontResource("RES_RESFONTIDX");
    }
}
```

#### 步骤 3: 在 UI 中使用

```csharp
// ✅ 正确做法：动态获取资源
private void OnShowFontPreview()
{
    var resfont = ViewModel.GetResFont();
    var resfontidx = ViewModel.GetResFontIdx();
    
    if (resfont != null)
    {
        // 显示 resfont 预览
        ShowFontPreview(resfont);
    }
    else
    {
        MessageBox.Show("RES_RESFONT not available on this platform.");
    }
    
    if (resfontidx != null)
    {
        // 显示 resfontidx 预览
        ShowFontPreview(resfontidx);
    }
    else
    {
        MessageBox.Show("RES_RESFONTIDX not available on this platform.");
    }
}
```

---

### 方案 2: 基于资源名称的自动匹配

如果不解析 RES.H，可以通过资源名称模式匹配：

```csharp
public class ResourceNameMatcher
{
    /// <summary>
    /// 根据名称模式查找资源
    /// </summary>
    public static ResourceItem? FindByPattern(
        ObservableCollection<ResourceItem> resources, 
        string pattern)
    {
        // 首先尝试精确匹配
        var exactMatch = resources.FirstOrDefault(r => 
            r.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase));
        
        if (exactMatch != null && exactMatch.Size > 0)
            return exactMatch;
        
        // 然后尝试模糊匹配
        var fuzzyMatch = resources.FirstOrDefault(r => 
            r.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase) &&
            r.Size > 0);
        
        return fuzzyMatch;
    }
    
    /// <summary>
    /// 查找字体资源
    /// </summary>
    public static ResourceItem? FindFontResource(
        ObservableCollection<ResourceItem> resources)
    {
        // 优先级 1: 通过名称匹配
        var namedFont = FindByPattern(resources, "RESFONT");
        if (namedFont != null)
            return namedFont;
        
        // 优先级 2: 通过类型检测
        var detectedFont = resources.FirstOrDefault(r => 
            r.Type == ResourceType.Font && r.Size > 0);
        
        return detectedFont;
    }
}
```

---

### 方案 3: 平台配置文件（最灵活）

为每个平台创建 JSON 配置文件：

**jt529x_config.json**:
```json
{
  "platform": "JT529X",
  "description": "Full feature platform",
  "resources": {
    "RES_RESFONT": 79,
    "RES_RESFONTIDX": 80,
    "RES_PALETTE": 60,
    "RES_OSD_SOURCE": 59
  },
  "features": {
    "has_games": true,
    "has_video": true,
    "has_photo": true
  }
}
```

**ax329x_config.json**:
```json
{
  "platform": "AX329X",
  "description": "Lite platform",
  "resources": {
    "RES_RESFONT": 9,
    "RES_RESFONTIDX": 10,
    "RES_PALETTE": 8,
    "RES_OSD_SOURCE": 7
  },
  "features": {
    "has_games": false,
    "has_video": false,
    "has_photo": false
  }
}
```

**加载配置**:
```csharp
public class PlatformConfig
{
    private Dictionary<string, int> _resourceMap = new();
    
    public void LoadConfig(string platform)
    {
        string configPath = $"configs/{platform.ToLower()}_config.json";
        
        if (File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<PlatformConfigData>(json);
            
            if (config?.Resources != null)
            {
                _resourceMap = config.Resources;
                System.Diagnostics.Debug.WriteLine(
                    $"Loaded config for {platform}: {_resourceMap.Count} resources");
            }
        }
    }
    
    public int GetResourceIndex(string name)
    {
        return _resourceMap.TryGetValue(name, out int index) ? index : -1;
    }
}
```

---

## 🎯 推荐的实施方案

### 阶段 1: 立即实施（基础兼容）

1. **添加 RES.H 解析器**
   - 解析当前平台的 RES.H
   - 建立名称到索引的映射

2. **修改资源访问方式**
   - 不再使用硬编码索引
   - 通过名称动态查找

3. **添加有效性检查**
   - 检查索引是否越界
   - 检查资源大小是否为 0

### 阶段 2: 中期优化（增强鲁棒性）

4. **实现资源名称匹配**
   - 支持模糊匹配
   -  fallback 到类型检测

5. **添加平台检测**
   - 自动识别 JT529X vs AX329X
   - 调整 UI 显示

6. **错误处理改进**
   - 友好的错误提示
   - 详细的调试日志

### 阶段 3: 长期完善（可扩展架构）

7. **平台配置文件**
   - JSON 格式配置
   - 易于添加新平台

8. **资源验证框架**
   - 自动验证资源完整性
   - 检测损坏的资源表

9. **多平台测试套件**
   - 自动化测试不同平台
   - 确保兼容性

---

## 📝 实施示例代码

### ResHParser.cs（完整实现）

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ResBinManager.Core
{
    public class ResHParser
    {
        private Dictionary<string, int> _resourceMap = new Dictionary<string, int>();
        private string? _platformName;
        
        public int ResourceCount => _resourceMap.Count;
        public string? PlatformName => _platformName;
        
        /// <summary>
        /// 解析 RES.H 文件
        /// </summary>
        public bool Parse(string resHPath)
        {
            if (!File.Exists(resHPath))
            {
                System.Diagnostics.Debug.WriteLine($"[ResHParser] File not found: {resHPath}");
                return false;
            }
            
            _resourceMap.Clear();
            
            try
            {
                var lines = File.ReadAllLines(resHPath);
                int parsedCount = 0;
                
                foreach (var line in lines)
                {
                    // 匹配 #define RES_XXX N 格式
                    var match = Regex.Match(line, @"#define\s+(RES_\w+)\s+(\d+)");
                    
                    if (match.Success)
                    {
                        string resourceName = match.Groups[1].Value;
                        int index = int.Parse(match.Groups[2].Value);
                        _resourceMap[resourceName] = index;
                        parsedCount++;
                    }
                }
                
                // 检测平台
                DetectPlatform(parsedCount);
                
                System.Diagnostics.Debug.WriteLine(
                    $"[ResHParser] ✓ Parsed {parsedCount} resources from {Path.GetFileName(resHPath)}");
                System.Diagnostics.Debug.WriteLine(
                    $"[ResHParser] Platform: {_platformName}");
                
                return parsedCount > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ResHParser] ✗ Error parsing RES.H: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 根据资源数量检测平台
        /// </summary>
        private void DetectPlatform(int resourceCount)
        {
            if (resourceCount <= 20)
            {
                _platformName = "AX329X";
            }
            else if (resourceCount >= 90)
            {
                _platformName = "JT529X";
            }
            else
            {
                _platformName = $"Unknown ({resourceCount} resources)";
            }
        }
        
        /// <summary>
        /// 获取资源索引
        /// </summary>
        public int GetIndex(string resourceName)
        {
            if (_resourceMap.TryGetValue(resourceName, out int index))
                return index;
            
            System.Diagnostics.Debug.WriteLine(
                $"[ResHParser] ⚠ Resource '{resourceName}' not found");
            
            return -1;
        }
        
        /// <summary>
        /// 检查资源是否存在
        /// </summary>
        public bool Exists(string resourceName)
        {
            return _resourceMap.ContainsKey(resourceName);
        }
        
        /// <summary>
        /// 获取所有资源名称
        /// </summary>
        public List<string> GetAllNames()
        {
            return _resourceMap.Keys.OrderBy(k => _resourceMap[k]).ToList();
        }
        
        /// <summary>
        /// 打印资源映射（用于调试）
        /// </summary>
        public void PrintMapping()
        {
            System.Diagnostics.Debug.WriteLine($"\n[ResHParser] Resource Mapping ({_platformName}):");
            System.Diagnostics.Debug.WriteLine(new string('-', 50));
            
            foreach (var kvp in _resourceMap.OrderBy(k => k.Value))
            {
                System.Diagnostics.Debug.WriteLine($"  {kvp.Key,-30} = {kvp.Value}");
            }
            
            System.Diagnostics.Debug.WriteLine(new string('-', 50));
        }
    }
}
```

### 在 MainViewModel 中使用

```csharp
public class MainViewModel : INotifyPropertyChanged
{
    private ResHParser _resHParser = new ResHParser();
    
    private async Task<bool> TryLoadAsDestBin(string filePath)
    {
        // ... 现有代码 ...
        
        if (_destBinParser.Load(filePath))
        {
            var resBinData = _destBinParser.ExtractResBin();
            
            if (resBinData != null)
            {
                // 加载 RES.H
                string? destBinDir = Path.GetDirectoryName(filePath);
                if (destBinDir != null)
                {
                    string? parentDir = Directory.GetParent(destBinDir)?.FullName;
                    string resHPath = Path.Combine(parentDir ?? destBinDir, "resource", "RES.H");
                    
                    if (File.Exists(resHPath))
                    {
                        _resHParser.Parse(resHPath);
                        _resHParser.PrintMapping();  // 调试输出
                    }
                }
                
                // ... 继续解析 RES.BIN ...
            }
        }
        
        // ...
    }
    
    /// <summary>
    /// 安全地替换资源
    /// </summary>
    private void ExecuteReplace(object? parameter)
    {
        if (SelectedResource == null || _parser == null)
            return;
        
        // 检查资源是否有效
        if (SelectedResource.Size == 0)
        {
            MessageBox.Show(
                $"Resource {SelectedResource.Id} ({SelectedResource.Name}) does not exist.\n\n" +
                "This resource may not be available on this platform.",
                "Resource Not Available",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        
        // 继续替换逻辑...
    }
}
```

---

## ✅ 总结

### 核心原则

1. **不要硬编码索引** - 不同平台索引不同
2. **解析 RES.H** - 动态获取资源映射
3. **验证有效性** - 检查索引范围和资源大小
4. **友好提示** - 资源不存在时给出清晰说明

### 关键改进

- ✅ 支持任意数量的平台
- ✅ 自动检测平台类型
- ✅ 防止越界访问
- ✅ 清晰的错误提示
- ✅ 易于扩展和维护

### 下一步

1. 实现 `ResHParser` 类
2. 在 `MainViewModel` 中集成
3. 修改所有硬编码的资源访问
4. 测试 JT529X 和 AX329X 两个平台

---

**文档创建时间**: 2026年  
**适用平台**: JT529X, AX329X, 及未来所有平台  
**兼容性**: ✅ 完全兼容不同 RES.H 格式
