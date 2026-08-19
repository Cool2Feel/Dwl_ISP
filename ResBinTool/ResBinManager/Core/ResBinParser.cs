using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ResBinManager.Core.ResourceDetection;
using ResBinManager.Models;

namespace ResBinManager.Core
{
    /// <summary>
    /// RES.BIN 文件解析引擎
    /// </summary>
    public class ResBinParser
    {
        private readonly string _filePath;
        private readonly string? _searchBasePath;  // 新增：RES.H 搜索的基础路径
        private byte[]? _fileData;
        private uint _tableOffset;
        private uint _resBinOffset = 0;  // ✅ 资源区基地址(对于DestBin模式是_resBinOffset, standalone模式是0)
        private uint _firstResAddr = 0;  // ✅ P1: 第一个资源的相对偏移(资源表结束位置)
        private List<ResInfoEntry>? _resourceTable;
        private readonly ResourceTypeDetectorOrchestrator _typeDetector = new();

        public List<ResourceItem> Resources { get; private set; }
        public string? ErrorMessage { get; private set; }
        public byte[]? FileData => _fileData;
        public uint TableOffset => _tableOffset;
        
        /// <summary>
        /// P1: 获取资源表的最大有效条目数(基于firstResAddr)
        /// </summary>
        public int MaxResourceCount => _firstResAddr > 0 ? (int)(_firstResAddr / 8) : 0;
        
        /// <summary>
        /// P1: 获取第一个资源的相对偏移(资源表结束位置)
        /// </summary>
        public uint FirstResAddr => _firstResAddr;
        
        /// <summary>
        /// 设置资源区基地址(用于DestBin模式)
        /// </summary>
        public void SetResourceBaseAddress(uint baseAddress)
        {
            _resBinOffset = baseAddress;
            System.Diagnostics.Debug.WriteLine($"[ResBinParser] Resource base address set to: 0x{_resBinOffset:X}");
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="filePath">RES.BIN 文件路径</param>
        /// <param name="searchBasePath">可选的基础搜索路径（用于查找 RES.H），默认为文件所在目录</param>
        public ResBinParser(string filePath, string? searchBasePath = null)
        {
            _filePath = filePath;
            _searchBasePath = searchBasePath ?? Path.GetDirectoryName(filePath);
            Resources = new List<ResourceItem>();
        }

        /// <summary>
        /// 解析 RES.BIN 文件（从磁盘读取）
        /// </summary>
        public bool Parse()
        {
            try
            {
                _fileData = File.ReadAllBytes(_filePath);
                return ParseFromBytes(_fileData);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Parse error: {ex.Message}\n{ex.StackTrace}";
                Logger.Error(ErrorMessage!);
                return false;
            }
        }

        /// <summary>
        /// 从内存数据解析 RES.BIN（避免临时文件 I/O，用于 DestBin 模式直接传入提取的 RES.BIN 字节）
        /// </summary>
        /// <param name="data">RES.BIN 完整字节数据</param>
        /// <returns>是否解析成功</returns>
        public bool ParseFromBytes(byte[] data)
        {
            try
            {
                _fileData = data;

                if (_fileData.Length < 1024)
                {
                    ErrorMessage = "File too small to be a valid RES.BIN";
                    return false;
                }

                // ✅ P6: RES.BIN的索引表始终从偏移0开始（对照SDK nvfs.c实现）
                _tableOffset = 0;

                System.Diagnostics.Debug.WriteLine($"[ParseFromBytes] RES.BIN table offset: 0x{_tableOffset:X} (always 0 per SDK spec), data size: {_fileData.Length} bytes");

                // 解析索引表
                if (!ParseResourceTable(_tableOffset))
                {
                    ErrorMessage = "Failed to parse resource table";
                    return false;
                }

                Logger.Info($"Parsed {_resourceTable!.Count} resources");

                // 提取资源元数据
                ExtractResourceMetadata();

                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Parse error: {ex.Message}\n{ex.StackTrace}";
                Logger.Error(ErrorMessage!);
                return false;
            }
        }


        private ResInfoEntry ReadEntry(uint offset)
        {
            var entry = new ResInfoEntry();
            entry.Offset = BitConverter.ToUInt32(_fileData!, (int)offset);
            entry.Length = BitConverter.ToUInt32(_fileData!, (int)offset + 4);
            return entry;
        }

        /// <summary>
        /// 解析完整的资源索引表
        /// P1改进: 使用firstResAddr进行边界检查，增强完整性验证
        /// </summary>
        private bool ParseResourceTable(uint tableOffset)
        {
            _resourceTable = new List<ResInfoEntry>();
            
            // ✅ P1: 读取第一个资源条目以确定firstResAddr
            if (tableOffset + 8 <= _fileData!.Length)
            {
                var firstEntry = ReadEntry(tableOffset);
                _firstResAddr = firstEntry.Offset;  // ✅ firstResAddr是第一个资源的相对偏移
                
                System.Diagnostics.Debug.WriteLine($"[ParseResourceTable] firstResAddr: 0x{_firstResAddr:X}");
                System.Diagnostics.Debug.WriteLine($"[ParseResourceTable] Max resources (theoretical): {MaxResourceCount}");
                
                // 验证firstResAddr合理性
                if (_firstResAddr == 0 || _firstResAddr > _fileData.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"[ParseResourceTable] ⚠️ Warning: firstResAddr 0x{_firstResAddr:X} seems invalid");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ParseResourceTable] ⚠️ Warning: Cannot read first entry");
                _firstResAddr = 0;
            }
            
            // ✅ P1: 动态确定最大资源数(基于firstResAddr和文件大小)
            int maxByFirstResAddr = MaxResourceCount;
            int maxByFileSize = (int)(_fileData.Length - tableOffset) / 8;
            int maxPossibleEntries = Math.Min(maxByFirstResAddr > 0 ? maxByFirstResAddr : 500, maxByFileSize);
            
            if (maxPossibleEntries <= 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ParseResourceTable] ✗ Error: No valid entries possible");
                return false;
            }
            
            System.Diagnostics.Debug.WriteLine($"[ParseResourceTable] Scanning up to {maxPossibleEntries} entries...");
            
            for (int i = 0; i < maxPossibleEntries; i++)
            {
                uint offset = tableOffset + (uint)(i * 8);
                
                if (offset + 8 > _fileData!.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"[ParseResourceTable] Entry {i}: Offset 0x{offset:X} exceeds file size");
                    break;
                }

                var entry = ReadEntry(offset);
                
                // ✅ P1: 遇到空条目则停止
                if (entry.Offset == 0 && entry.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[ParseResourceTable] Entry {i}: Empty entry, stopping");
                    break;
                }
                
                // ✅ P1: 验证相对偏移不超出文件范围
                if (entry.Offset >= _fileData.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"[ParseResourceTable] ⚠️ Entry {i}: Offset 0x{entry.Offset:X} >= file size {_fileData.Length}, stopping");
                    break;
                }
                
                // ✅ P1: 验证长度合理性
                if (entry.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[ParseResourceTable] ⚠️ Entry {i}: Zero length, skipping");
                    continue;  // 跳过而不是停止
                }
                
                if (entry.Length > 30 * 1024 * 1024)  // 30MB上限
                {
                    System.Diagnostics.Debug.WriteLine($"[ParseResourceTable] ⚠️ Entry {i}: Length {entry.Length} too large, skipping");
                    continue;
                }

                _resourceTable.Add(entry);
            }

            System.Diagnostics.Debug.WriteLine($"[ParseResourceTable] ✓ Parsed {_resourceTable.Count} resources successfully");
            return _resourceTable.Count > 0;
        }

        /// <summary>
        /// 重新解析资源表（不重新读取文件）
        /// 用于在 ResBinWriter 修改文件后同步更新内存中的资源表
        /// </summary>
        public bool ReParseResourceTable()
        {
            System.Diagnostics.Debug.WriteLine($"[ReParseResourceTable] Checking state...");
            System.Diagnostics.Debug.WriteLine($"  _fileData is null: {_fileData == null}");
            System.Diagnostics.Debug.WriteLine($"  _fileData length: {_fileData?.Length ?? 0}");
            System.Diagnostics.Debug.WriteLine($"  _tableOffset: 0x{_tableOffset:X}");
            System.Diagnostics.Debug.WriteLine($"  _filePath: {_filePath}");
            
            if (_fileData == null || _tableOffset == 0)
            {
                ErrorMessage = "File not loaded or table offset not detected";
                System.Diagnostics.Debug.WriteLine($"[ReParseResourceTable] ERROR: {ErrorMessage}");
                return false;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[ReParseResourceTable] Starting re-parse...");
                System.Diagnostics.Debug.WriteLine($"  File size: {_fileData.Length}");
                System.Diagnostics.Debug.WriteLine($"  Table offset: 0x{_tableOffset:X}");
                
                // 验证资源表起始位置是否有效
                if (_tableOffset + 8 > _fileData.Length)
                {
                    ErrorMessage = $"Table offset 0x{_tableOffset:X} exceeds file size {_fileData.Length}";
                    System.Diagnostics.Debug.WriteLine($"[ReParseResourceTable] ERROR: {ErrorMessage}");
                    return false;
                }
                
                var firstEntry = ReadEntry(_tableOffset);
                System.Diagnostics.Debug.WriteLine($"[ReParseResourceTable] First entry: offset=0x{firstEntry.Offset:X}, len={firstEntry.Length}");
                
                if (firstEntry.Offset == 0 && firstEntry.Length == 0)
                {
                    ErrorMessage = "First resource entry is empty (0, 0)";
                    System.Diagnostics.Debug.WriteLine($"[ReParseResourceTable] ERROR: {ErrorMessage}");
                    return false;
                }
                
                // 重新解析资源表
                if (!ParseResourceTable(_tableOffset))
                {
                    ErrorMessage = "Failed to re-parse resource table";
                    System.Diagnostics.Debug.WriteLine($"[ReParseResourceTable] ERROR: {ErrorMessage}");
                    return false;
                }

                Logger.Info($"Re-parsed {_resourceTable!.Count} resources");
                System.Diagnostics.Debug.WriteLine($"[ReParseResourceTable] SUCCESS: Parsed {_resourceTable.Count} resources");
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Re-parse error: {ex.Message}\n{ex.StackTrace}";
                Logger.Error(ErrorMessage!);
                System.Diagnostics.Debug.WriteLine($"[ReParseResourceTable] EXCEPTION: {ErrorMessage}");
                return false;
            }
        }

        /// <summary>
        /// 提取资源元数据并构建 ResourceItem 列表
        /// P1改进: 完善调试日志输出，显示更多诊断信息
        /// P6改进: RES.H名称推断前先验证资源数量一致性
        /// </summary>
        private void ExtractResourceMetadata()
        {
            Resources.Clear();

            // 加载 RES.H 中的资源名称映射
            var nameMap = LoadResourceNamesFromHeader();
            
            // 检查 RES.H 定义的资源数量与实际解析出的资源数量是否一致
            int actualResourceCount = _resourceTable!.Count;
            int resHDefinedCount = nameMap.Count;
            bool isResHCountMatch = actualResourceCount == resHDefinedCount;
            
            System.Diagnostics.Debug.WriteLine($"[ExtractResourceMetadata] Starting extraction for {actualResourceCount} resources");
            System.Diagnostics.Debug.WriteLine($"[ExtractResourceMetadata] RES.H defines {resHDefinedCount} resources");
            System.Diagnostics.Debug.WriteLine($"[ExtractResourceMetadata] Resource count match: {isResHCountMatch}");
            System.Diagnostics.Debug.WriteLine($"[ExtractResourceMetadata] Resource base address: 0x{_resBinOffset:X}");
            System.Diagnostics.Debug.WriteLine($"[ExtractResourceMetadata] firstResAddr: 0x{_firstResAddr:X}, MaxResourceCount: {MaxResourceCount}");

            if (!isResHCountMatch)
            {
                System.Diagnostics.Debug.WriteLine($"[ExtractResourceMetadata] ⚠️ RES.H count ({resHDefinedCount}) != actual count ({actualResourceCount}), will NOT use RES.H names for type detection");
            }

            int successCount = 0;
            int failCount = 0;

            for (int i = 0; i < _resourceTable!.Count; i++)
            {
                var entry = _resourceTable[i];
                
                // 提取实际数据
                byte[]? data = null;
                                    
                // 计算资源的绝对地址(对应SDK: nvInfo.lastRes.address + nvInfo.resAddress)
                uint absoluteAddress = entry.GetAbsoluteAddress(_resBinOffset);
                // 数据读取使用相对偏移（因为_fileData是独立的RES.BIN文件）
                uint readOffset = entry.Offset;
                                    
                if (readOffset + entry.Length <= _fileData!.Length)
                {
                    data = new byte[entry.Length];
                    Array.Copy(_fileData, readOffset, data, 0, entry.Length);
                    successCount++;
                    
                    // 详细的调试日志（仅前3个和最后一个资源）
                    if (i < 3 || i == _resourceTable.Count - 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExtractResourceMetadata] Resource[{i}]:");
                        System.Diagnostics.Debug.WriteLine($"  Relative offset: 0x{entry.Offset:X}");
                        System.Diagnostics.Debug.WriteLine($"  Absolute address (in DestBin): 0x{absoluteAddress:X}");
                        System.Diagnostics.Debug.WriteLine($"  Read offset (in RES.BIN): 0x{readOffset:X}");
                        System.Diagnostics.Debug.WriteLine($"  Length: {entry.Length} bytes");
                        System.Diagnostics.Debug.WriteLine($"  Data range: [0x{readOffset:X}, 0x{readOffset + entry.Length - 1:X}]");
                        
                        if (data.Length >= 4)
                        {
                            System.Diagnostics.Debug.WriteLine($"  First 4 bytes: {data[0]:X2} {data[1]:X2} {data[2]:X2} {data[3]:X2}");
                            bool isJpeg = data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;
                            bool isBmp = data[0] == 'B' && data[1] == 'M';
                            bool isWav = data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F';
                            System.Diagnostics.Debug.WriteLine($"  Format hints: JPEG={isJpeg}, BMP={isBmp}, WAV={isWav}");
                        }
                    }
                }
                else
                {
                    failCount++;
                    System.Diagnostics.Debug.WriteLine($"[ExtractResourceMetadata] ✗ Resource[{i}]: Read offset 0x{readOffset:X} + Length {entry.Length} exceeds file size {_fileData.Length}");
                }

                // ✅ P6: 只有当 RES.H 资源数量与实际数量一致时，才使用 RES.H 名称
                // 否则完全不使用 RES.H 的任何信息
                string name = isResHCountMatch && nameMap.ContainsKey((uint)i) 
                    ? nameMap[(uint)i] 
                    : $"Resource_{i}";

                // ✅ P6: 只有当 RES.H 资源数量与实际数量一致时，才使用 RES.H 名称推断类型
                // 否则完全依赖魔数和大小特征
                // 读取后一个资源（i+1）的前2字节，用于字体配对检测
                // 顺序固定: resfont.bin 在前，resfontidx.bin 在后
                // 如果 i+1 有 0x584D 魔数 → 当前 i 是 resfont.bin
                byte[]? adjacentData = null;
                if (_resourceTable != null && _fileData != null && i + 1 < _resourceTable.Count)
                {
                    var nextEntry = _resourceTable[i + 1];
                    if (nextEntry.Offset + 2 <= _fileData.Length)
                    {
                        adjacentData = new byte[2];
                        Array.Copy(_fileData, nextEntry.Offset, adjacentData, 0, 2);
                    }
                }

                var type = isResHCountMatch
                    ? _typeDetector.DetectByName(name, data, entry.Length, i, adjacentData)
                    : _typeDetector.DetectByMagic(data, entry.Length);

                System.Diagnostics.Debug.WriteLine($"[ExtractResourceMetadata] Resource[{i}] Name: {name}, Type: {type}, Size: {entry.Length} bytes");

                var item = new ResourceItem
                {
                    Id = (uint)i,
                    Name = name,
                    Type = type,
                    Offset = entry.Offset,
                    BaseOffset = _resBinOffset,
                    Size = entry.Length,
                    Data = data,
                    IsModified = false
                };

                if ((type == ResourceType.Jpeg || type == ResourceType.Bitmap || type == ResourceType.Png) && data != null)
                {
                    var dimensions = ImageInfoParser.ParseImageDimensions(data, type);
                    if (dimensions.Valid)
                    {
                        item.Width = dimensions.Width;
                        item.Height = dimensions.Height;
                        System.Diagnostics.Debug.WriteLine($"[ExtractResourceMetadata] Image {i} ({name}) resolution: {dimensions.Width}x{dimensions.Height}");
                    }
                }

                Resources.Add(item);
            }
            
            System.Diagnostics.Debug.WriteLine($"[ExtractResourceMetadata] ✓ Extraction complete: {successCount} succeeded, {failCount} failed");
        }

        /// <summary>
        /// 从 RES.H 文件加载资源名称映射
        /// </summary>
        private Dictionary<uint, string> LoadResourceNamesFromHeader()
        {
            var map = new Dictionary<uint, string>();
            
            // 确定搜索基础路径
            string basePath = _searchBasePath ?? Path.GetDirectoryName(_filePath)!;
            
            if (string.IsNullOrEmpty(basePath))
            {
                Logger.Info("Search base path is null, using file directory");
                basePath = Path.GetDirectoryName(_filePath) ?? "";
            }
            
            // 构建搜索路径列表（按优先级排序）
            var headerPaths = new List<string>
            {
                // 优先级 1: 基于搜索路径
                Path.Combine(basePath, "RES.H"),
                Path.Combine(basePath, "..", "RES.H"),
                Path.Combine(basePath, "..", "..", "ax32_platform_demo", "resource", "RES.H"),
                
                // 优先级 2: 基于文件路径（回退）
                Path.Combine(Path.GetDirectoryName(_filePath)!, "RES.H"),
            };

            // 去重并规范化路径
            headerPaths = headerPaths.Select(p => Path.GetFullPath(p)).Distinct().ToList();

            foreach (var headerPath in headerPaths)
            {
                if (File.Exists(headerPath))
                {
                    Logger.Info($"Found RES.H at: {headerPath}");
                    return ParseResHFile(headerPath);
                }
            }

            Logger.Info("RES.H not found, using default names");
            return map;
        }

        private Dictionary<uint, string> ParseResHFile(string headerPath)
        {
            var map = new Dictionary<uint, string>();
            var lines = File.ReadAllLines(headerPath);

            foreach (var line in lines)
            {
                // 匹配: #define RES_POWER_ON  78
                if (line.Trim().StartsWith("#define"))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        string name = parts[1];
                        if (uint.TryParse(parts[2], out uint id))
                        {
                            map[id] = name;
                        }
                    }
                }
            }

            Logger.Info($"Loaded {map.Count} resource names from RES.H");
            return map;
        }

        /// <summary>
        /// 导出单个资源到文件
        /// </summary>
        public bool ExportResource(uint resourceId, string outputPath)
        {
            if (resourceId >= Resources.Count)
                return false;

            var resource = Resources[(int)resourceId];
            
            try
            {
                if (resource.Type == ResourceType.OsdSource)
                {
                    return ExportOsdSource(resource, outputPath);
                }
                else if (resource.Type == ResourceType.Palette)
                {
                    return ExportPalette(resource, outputPath);
                }
                else
                {
                    File.WriteAllBytes(outputPath, resource.Data!);
                    return true;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Export failed: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 导出OSD资源（解码为BMP图标）
        /// </summary>
        private bool ExportOsdSource(ResourceItem resource, string outputPath)
        {
            string outputDir = Path.GetDirectoryName(outputPath) ?? ".";
            string baseName = Path.GetFileNameWithoutExtension(outputPath);
            string iconsDir = Path.Combine(outputDir, $"{baseName}_icons");

            byte[]? paletteData = FindPaletteResourceData();
            
            if (paletteData == null)
            {
                ErrorMessage = "Palette resource not found, cannot decode OSD icons";
                return false;
            }

            try
            {
                string[]? iconNames = SelectIconNames(resource.Data!, resource.Name);
                
                OsdSourceParser.ExportOsdIcons(resource.Data!, paletteData, iconsDir, iconNames);
                
                File.WriteAllBytes(outputPath, resource.Data!);
                
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"OSD export failed: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 导出调色板资源（同时导出原始bin和预览BMP）
        /// </summary>
        private bool ExportPalette(ResourceItem resource, string outputPath)
        {
            try
            {
                File.WriteAllBytes(outputPath, resource.Data!);
                
                string bmpPath = Path.ChangeExtension(outputPath, ".bmp");
                PaletteParser.ExportPaletteAsImage(resource.Data!, bmpPath);
                
                string txtPath = Path.ChangeExtension(outputPath, ".txt");
                PaletteParser.ExportPaletteAsText(resource.Data!, txtPath);
                
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Palette export failed: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 在资源列表中查找调色板资源数据
        /// </summary>
        private byte[]? FindPaletteResourceData()
        {
            foreach (var res in Resources)
            {
                if (res.Type == ResourceType.Palette && res.Data != null)
                {
                    return res.Data;
                }
            }
            
            foreach (var res in Resources)
            {
                if (res.Name.IndexOf("PALETTE", StringComparison.OrdinalIgnoreCase) >= 0 && res.Data != null)
                {
                    return res.Data;
                }
            }
            
            return null;
        }

        /// <summary>
        /// 根据OSD数据源选择合适的图标名称
        /// </summary>
        private string[]? SelectIconNames(byte[] osdData, string resourceName)
        {
            int iconCount = OsdSourceParser.DetectIconCount(osdData);
            if (iconCount == 0)
                return null;

            string[] hm020fNames = OsdSourceParser.GetHm020fIconNames();
            
            if (iconCount == hm020fNames.Length)
            {
                return hm020fNames;
            }

            return null;
        }

        /// <summary>
        /// 获取资源表副本
        /// </summary>
        public List<ResInfoEntry> GetResourceTable()
        {
            if (_resourceTable == null)
                return new List<ResInfoEntry>();
            
            // 返回副本而非原始引用，避免并发修改导致的数据不一致
            var copy = new List<ResInfoEntry>(_resourceTable.Count);
            copy.AddRange(_resourceTable);
            return copy;
        }

        /// <summary>
        /// 更新资源表（用于同步 ResBinWriter 的修改）
        /// </summary>
        /// <param name="newTable">新的资源表</param>
        /// <param name="newFileData">新的文件数据（可选，用于同步更新内部 FileData）</param>
        public void UpdateResourceTable(List<ResInfoEntry> newTable, byte[]? newFileData = null)
        {
            _resourceTable = newTable;
            
            if (newFileData != null)
            {
                _fileData = newFileData;
            }
            
            if (_resourceTable != null && _resourceTable.Count > 0)
            {
                _firstResAddr = _resourceTable[0].Offset;
            }
            else
            {
                _firstResAddr = 0;
            }
        }
    }
}
