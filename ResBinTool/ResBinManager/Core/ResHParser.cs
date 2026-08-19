using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ResBinManager.Core
{
    /// <summary>
    /// RES.H 文件解析器
    /// 用于解析不同平台的资源定义文件，建立资源名称到索引的映射
    /// </summary>
    public class ResHParser
    {
        private Dictionary<string, int> _resourceMap = new();
        private string? _platformName;
        private int _totalResources;
        
        /// <summary>
        /// 获取平台名称（如果已解析）
        /// </summary>
        public string? PlatformName => _platformName;
        
        /// <summary>
        /// 获取资源总数
        /// </summary>
        public int TotalResources => _totalResources;
        
        /// <summary>
        /// 是否已成功解析
        /// </summary>
        public bool IsParsed => _resourceMap.Count > 0;
        
        /// <summary>
        /// 解析 RES.H 文件
        /// </summary>
        /// <param name="resHPath">RES.H 文件路径</param>
        /// <returns>是否解析成功</returns>
        public bool Parse(string resHPath)
        {
            try
            {
                if (!File.Exists(resHPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ResHParser] File not found: {resHPath}");
                    return false;
                }
                
                _resourceMap.Clear();
                _platformName = null;
                _totalResources = 0;
                
                var lines = File.ReadAllLines(resHPath);
                int maxIndex = -1;
                
                // 检测平台名称（从路径或注释中）
                _platformName = DetectPlatformName(resHPath, lines);
                
                // 正则表达式匹配 #define RES_XXX N
                var definePattern = new Regex(@"^\s*#\s*define\s+(RES_\w+)\s+(\d+)", RegexOptions.Compiled);
                
                foreach (var line in lines)
                {
                    var match = definePattern.Match(line);
                    if (match.Success)
                    {
                        string resourceName = match.Groups[1].Value;
                        if (int.TryParse(match.Groups[2].Value, out int index))
                        {
                            _resourceMap[resourceName] = index;
                            
                            if (index > maxIndex)
                            {
                                maxIndex = index;
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"[ResHParser] Found: {resourceName} = {index}");
                        }
                    }
                }
                
                _totalResources = maxIndex + 1;
                
                System.Diagnostics.Debug.WriteLine($"[ResHParser] Platform: {_platformName}, Total Resources: {_totalResources}, Parsed: {_resourceMap.Count} entries");
                
                return _resourceMap.Count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResHParser] Error parsing RES.H: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 根据资源名称获取索引
        /// </summary>
        /// <param name="resourceName">资源名称（如 "RES_RESFONT"）</param>
        /// <returns>资源索引，如果不存在返回 -1</returns>
        public int GetIndex(string resourceName)
        {
            if (_resourceMap.TryGetValue(resourceName, out int index))
            {
                return index;
            }
            
            System.Diagnostics.Debug.WriteLine($"[ResHParser] Resource not found: {resourceName}");
            return -1;
        }
        
        /// <summary>
        /// 检查资源是否存在
        /// </summary>
        /// <param name="resourceName">资源名称</param>
        /// <returns>是否存在</returns>
        public bool HasResource(string resourceName)
        {
            return _resourceMap.ContainsKey(resourceName);
        }
        
        /// <summary>
        /// 获取所有资源名称列表
        /// </summary>
        /// <returns>资源名称列表</returns>
        public List<string> GetAllResourceNames()
        {
            return new List<string>(_resourceMap.Keys);
        }
        
        /// <summary>
        /// 获取所有已定义的资源索引列表（排序后）
        /// </summary>
        /// <returns>排序后的索引列表</returns>
        public List<int> GetAllDefinedIndices()
        {
            var indices = _resourceMap.Values.ToList();
            indices.Sort();
            return indices;
        }
        
        /// <summary>
        /// 获取资源映射表（用于调试）
        /// </summary>
        /// <returns>资源名称到索引的字典</returns>
        public Dictionary<string, int> GetResourceMap()
        {
            return new Dictionary<string, int>(_resourceMap);
        }
        
        /// <summary>
        /// 尝试自动查找 RES.H 文件
        /// </summary>
        /// <param name="destBinPath">DestBin.bin 文件路径</param>
        /// <returns>RES.H 文件路径，如果未找到返回 null</returns>
        public static string? AutoFindResH(string destBinPath)
        {
            if (string.IsNullOrEmpty(destBinPath))
                return null;
            
            var destBinDir = Path.GetDirectoryName(destBinPath);
            if (string.IsNullOrEmpty(destBinDir))
                return null;
            
            // 策略 1: 在同一目录下查找 RES.H
            var resHInSameDir = Path.Combine(destBinDir, "RES.H");
            if (File.Exists(resHInSameDir))
            {
                System.Diagnostics.Debug.WriteLine($"[ResHParser] Found RES.H in same directory: {resHInSameDir}");
                return resHInSameDir;
            }
            
            // 策略 2: 在上级 resource 目录下查找
            var parentDir = Directory.GetParent(destBinDir);
            if (parentDir != null)
            {
                var resourceDir = Path.Combine(parentDir.FullName, "resource");
                if (Directory.Exists(resourceDir))
                {
                    var resHInResourceDir = Path.Combine(resourceDir, "RES.H");
                    if (File.Exists(resHInResourceDir))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ResHParser] Found RES.H in resource directory: {resHInResourceDir}");
                        return resHInResourceDir;
                    }
                }
            }
            
            // 策略 3: 向上遍历最多 3 层目录查找 resource/RES.H
            var currentDir = destBinDir;
            for (int i = 0; i < 3; i++)
            {
                var resourceDir = Path.Combine(currentDir, "resource");
                if (Directory.Exists(resourceDir))
                {
                    var resHPath = Path.Combine(resourceDir, "RES.H");
                    if (File.Exists(resHPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ResHParser] Found RES.H after {i + 1} level(s): {resHPath}");
                        return resHPath;
                    }
                }
                
                var parent = Directory.GetParent(currentDir);
                if (parent == null)
                    break;
                    
                currentDir = parent.FullName;
            }
            
            System.Diagnostics.Debug.WriteLine("[ResHParser] RES.H not found");
            return null;
        }
        
        /// <summary>
        /// 检测平台名称
        /// </summary>
        private string? DetectPlatformName(string resHPath, string[] lines)
        {
            // 从路径中提取平台名称
            var pathLower = resHPath.ToLower();
            if (pathLower.Contains("jt529x"))
                return "JT529X";
            if (pathLower.Contains("ax329x"))
                return "AX329X";
            if (pathLower.Contains("ax32"))
                return "AX32";
            
            // 从注释中提取平台名称
            foreach (var line in lines.Take(20)) // 只检查前 20 行
            {
                if (line.IndexOf("JT529X", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "JT529X";
                if (line.IndexOf("AX329X", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "AX329X";
            }
            
            return null;
        }
        
        /// <summary>
        /// 打印解析结果（用于调试）
        /// </summary>
        public void PrintSummary()
        {
            System.Diagnostics.Debug.WriteLine("=".PadRight(70, '='));
            System.Diagnostics.Debug.WriteLine($"RES.H Parser Summary");
            System.Diagnostics.Debug.WriteLine("=".PadRight(70, '='));
            System.Diagnostics.Debug.WriteLine($"Platform: {_platformName ?? "Unknown"}");
            System.Diagnostics.Debug.WriteLine($"Total Resources: {_totalResources}");
            System.Diagnostics.Debug.WriteLine($"Parsed Entries: {_resourceMap.Count}");
            System.Diagnostics.Debug.WriteLine("");
            
            // 打印关键资源
            var keyResources = new[] { "RES_RESFONT", "RES_RESFONTIDX", "RES_LOGO", "RES_MENU_BG" };
            foreach (var resName in keyResources)
            {
                if (_resourceMap.TryGetValue(resName, out int index))
                {
                    System.Diagnostics.Debug.WriteLine($"  {resName,-20} = {index}");
                }
            }
            
            System.Diagnostics.Debug.WriteLine("");
        }
    }
}
