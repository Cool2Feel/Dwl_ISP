using System;
using System.Collections.Generic;
using System.IO;

namespace ResBinManager.Core
{
    /// <summary>
    /// RES.BIN 文件写入引擎
    /// </summary>
    public class ResBinWriter
    {
        private byte[] _fileData;
        private uint _tableOffset;
        private List<ResInfoEntry> _resourceTable;

        public string? ErrorMessage { get; private set; }

        public ResBinWriter(byte[] fileData, uint tableOffset, List<ResInfoEntry> resourceTable)
        {
            _fileData = new byte[fileData.Length];
            Array.Copy(fileData, _fileData, fileData.Length);
            _tableOffset = tableOffset;

            // 深拷贝资源表（ResInfoEntry是值类型，List<T>.AddRange会创建副本）
            _resourceTable = new List<ResInfoEntry>(resourceTable.Count);
            _resourceTable.AddRange(resourceTable);
        }

        /// <summary>
        /// 替换指定资源
        /// </summary>
        public bool ReplaceResource(uint resourceId, byte[] newData)
        {
            try
            {
                if (resourceId >= _resourceTable.Count)
                {
                    ErrorMessage = $"Invalid resource ID: {resourceId}";
                    return false;
                }

                var oldEntry = _resourceTable[(int)resourceId];
                uint oldOffset = oldEntry.Offset;  // ✅ 使用Offset而非Address
                uint oldSize = oldEntry.Length;
                uint newSize = (uint)newData.Length;

                Logger.Info($"Replacing resource {resourceId}:");
                Logger.Info($"  Old: offset=0x{oldOffset:X8}, size={oldSize}");
                Logger.Info($"  New: size={newSize}");

                if (newSize < oldSize)
                {
                    // 情况 A: 新文件更小 - 前移后续数据并收缩文件
                    return ReplaceCompact(resourceId, newData, oldOffset, oldSize, newSize);
                }
                else if (newSize == oldSize)
                {
                    // 情况 B: 大小相等 - 直接覆盖
                    return ReplaceInPlace(resourceId, newData, oldOffset, newSize);
                }
                else
                {
                    // 情况 C: 新文件更大 - 后移后续数据并扩展文件
                    return ReplaceWithShift(resourceId, newData, oldOffset, oldSize, newSize);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Replace failed: {ex.Message}";
                Logger.Error(ErrorMessage!);
                return false;
            }
        }

        /// <summary>
        /// 原地替换（新文件 = 原文件，大小不变时直接覆盖）
        /// </summary>
        private bool ReplaceInPlace(uint resourceId, byte[] newData, uint offset, uint size)
        {
            // 保存原始数据用于事务回滚
            byte[] originalData = new byte[_fileData.Length];
            Array.Copy(_fileData, originalData, _fileData.Length);

            // 保存原始资源表用于事务回滚
            List<ResInfoEntry> originalResourceTable = new List<ResInfoEntry>(_resourceTable.Count);
            originalResourceTable.AddRange(_resourceTable);

            try
            {
                // 1. 写入新数据
                Array.Copy(newData, 0, _fileData, offset, size);

                // 2. 更新索引表中的长度字段
                UpdateEntryLength(resourceId, size);

                // 确保文件内资源表与内存表一致
                RewriteAllTableEntries();

                Logger.Info($"  ✓ Replaced in-place (same size)");
                return true;
            }
            catch (Exception ex)
            {
                // 事务回滚：恢复原始数据
                Array.Copy(originalData, _fileData, originalData.Length);

                // 事务回滚：恢复原始资源表
                _resourceTable.Clear();
                _resourceTable.AddRange(originalResourceTable);

                ErrorMessage = $"In-place replace failed: {ex.Message}";
                Logger.Error(ErrorMessage!);
                return false;
            }
        }

        /// <summary>
        /// 紧缩替换（新文件 < 原文件）：前移后续数据并收缩文件
        /// </summary>
        private bool ReplaceCompact(uint resourceId, byte[] newData, uint offset,
                                   uint oldSize, uint newSize)
        {
            uint delta = oldSize - newSize;
            uint dataEnd = offset + oldSize;
            uint originalLength = (uint)_fileData.Length;

            Logger.Info($"  ↓ Compact: shift {delta} bytes backward");

            // 保存原始数据用于事务回滚
            byte[] originalData = new byte[_fileData.Length];
            Array.Copy(_fileData, originalData, _fileData.Length);

            List<ResInfoEntry> originalResourceTable = new List<ResInfoEntry>(_resourceTable.Count);
            originalResourceTable.AddRange(_resourceTable);

            try
            {
                // 1. 写入新数据
                Array.Copy(newData, 0, _fileData, offset, newSize);

                // 2. 前移后续数据（目标在源之前，从前往后复制）
                uint moveStart = dataEnd;
                uint moveLength = originalLength - moveStart;

                if (moveLength > 0)
                {
                    Buffer.BlockCopy(_fileData, (int)moveStart, _fileData, (int)(offset + newSize), (int)moveLength);
                }

                // 3. 收缩文件
                Array.Resize(ref _fileData, (int)(originalLength - delta));

                // 验证数据移动后所有受影响资源的完整性
                ValidateAfterDataMove(dataEnd, delta, isExpanding: false);

                // 4. 修正当前资源之后所有资源的 Offset（减去 delta）
                uint currentEnd = offset + newSize;
                for (uint i = resourceId + 1; i < _resourceTable.Count; i++)
                {
                    var entry = _resourceTable[(int)i];

                    if (entry.Offset >= currentEnd)
                    {
                        if (entry.Offset < delta)
                        {
                            System.Diagnostics.Debug.WriteLine($"WARNING: Resource {i} offset {entry.Offset} < delta {delta}, would underflow");
                            break;
                        }

                        uint newOffset = entry.Offset - delta;

                        if (newOffset >= _fileData.Length)
                        {
                            System.Diagnostics.Debug.WriteLine($"WARNING: Resource {i} new offset 0x{newOffset:X} >= file size {_fileData.Length}");
                            break;
                        }

                        // 更新文件中的资源表
                        uint tblOffset = _tableOffset + i * 8;
                        var addrBytes = BitConverter.GetBytes(newOffset);
                        Array.Copy(addrBytes, 0, _fileData, tblOffset, 4);

                        // 更新内存中的表
                        entry.Offset = newOffset;
                        _resourceTable[(int)i] = entry;
                    }
                    else if (entry.Offset == 0)
                    {
                        break;
                    }
                }

                // 5. 更新当前资源的长度
                UpdateEntryLength(resourceId, newSize);

                // 确保文件内资源表与内存表一致（数据移位可能覆写了文件中的表区域）
                RewriteAllTableEntries();

                Logger.Info($"  ✓ Replaced with compact (smaller size, new file length: {_fileData.Length})");
                return true;
            }
            catch (Exception ex)
            {
                // 事务回滚：恢复原始数据
                Array.Resize(ref _fileData, originalData.Length);
                Array.Copy(originalData, _fileData, originalData.Length);

                // 事务回滚：恢复原始资源表
                _resourceTable.Clear();
                _resourceTable.AddRange(originalResourceTable);

                ErrorMessage = $"Compact replace failed: {ex.Message}";
                Logger.Error(ErrorMessage!);
                return false;
            }
        }

        /// <summary>
        /// 移位替换（新文件 > 原文件）
        /// </summary>
        private bool ReplaceWithShift(uint resourceId, byte[] newData, uint oldOffset,
                                     uint oldSize, uint newSize)
        {
            uint delta = newSize - oldSize;
            uint dataEnd = oldOffset + oldSize;

            Logger.Warning($"  Larger file, need to shift {delta} bytes");

            // ✅ P7: 保存原始长度，避免Array.Resize后计算混乱
            uint originalLength = (uint)_fileData.Length;

            // 保存原始数据用于事务回滚
            byte[] originalData = new byte[_fileData.Length];
            Array.Copy(_fileData, originalData, _fileData.Length);

            // 保存原始资源表用于事务回滚（ResInfoEntry是值类型，AddRange会创建副本）
            List<ResInfoEntry> originalResourceTable = new List<ResInfoEntry>(_resourceTable.Count);
            originalResourceTable.AddRange(_resourceTable);

            try
            {
                // 1. 扩展数组以容纳新增数据
                uint requiredSize = originalLength + delta;
                Array.Resize(ref _fileData, (int)requiredSize);

                // 2. 移动后续数据（从后往前复制，避免覆盖）
                uint moveStart = dataEnd;
                uint moveLength = originalLength - moveStart;  // ✅ 清晰：原始长度 - 移动起始位置

                if (moveLength > 0)
                {
                    // 使用 Buffer.BlockCopy 替代逐字节循环，利用原生 memmove 实现高效内存拷贝
                    // 对于同一数组内重叠区域的拷贝（delta > 0 时 dest > src），
                    // BlockCopy 内部处理方向选择，保证数据完整性
                    Buffer.BlockCopy(_fileData, (int)moveStart, _fileData, (int)(moveStart + delta), (int)moveLength);

                    // 验证数据移动后所有受影响资源的完整性
                    ValidateAfterDataMove(dataEnd, delta, isExpanding: true);
                }

                // 3. 写入新数据
                Array.Copy(newData, 0, _fileData, oldOffset, newSize);

                // 4. 更新所有后续资源的地址
                UpdateSubsequentAddresses(resourceId, delta);

                // 5. 更新当前资源的长度
                UpdateEntryLength(resourceId, newSize);

                // 确保文件内资源表与内存表一致（数据移位可能覆写了文件中的表区域）
                RewriteAllTableEntries();

                Logger.Info($"  ✓ Replaced with shift (larger size)");
                return true;
            }
            catch (Exception ex)
            {
                // 事务回滚：恢复原始数据
                Array.Resize(ref _fileData, originalData.Length);
                Array.Copy(originalData, _fileData, originalData.Length);

                // 事务回滚：恢复原始资源表
                _resourceTable.Clear();
                _resourceTable.AddRange(originalResourceTable);

                ErrorMessage = $"Shift replace failed: {ex.Message}";
                Logger.Error(ErrorMessage!);
                return false;
            }
        }

        /// <summary>
        /// 更新索引表中条目的长度字段
        /// </summary>
        private void UpdateEntryLength(uint resourceId, uint newLength)
        {
            uint offset = _tableOffset + resourceId * 8 + 4; // +4 是 length 字段偏移

            // 验证：确保不会覆盖资源数据
            if (offset >= _fileData.Length)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR: UpdateEntryLength - offset 0x{offset:X} >= file length {_fileData.Length}");
                return;
            }

            var lengthBytes = BitConverter.GetBytes(newLength);
            Array.Copy(lengthBytes, 0, _fileData, offset, 4);

            // 更新内存中的表
            var entry = _resourceTable[(int)resourceId];
            entry.Length = newLength;
            _resourceTable[(int)resourceId] = entry;
        }

        /// <summary>
        /// 更新后续所有资源的地址偏移
        /// </summary>
        private void UpdateSubsequentAddresses(uint resourceId, uint delta)
        {
            uint currentEnd = _resourceTable[(int)resourceId].Offset +   // ✅ 使用Offset
                            _resourceTable[(int)resourceId].Length;

            for (uint i = resourceId + 1; i < _resourceTable.Count; i++)
            {
                var entry = _resourceTable[(int)i];

                if (entry.Offset >= currentEnd)  // ✅ 使用Offset
                {
                    // 验证地址是否合理（必须在文件范围内）
                    if (entry.Offset >= _fileData.Length)  // ✅ 使用Offset
                    {
                        System.Diagnostics.Debug.WriteLine($"WARNING: Skipping resource {i} - Invalid offset 0x{entry.Offset:X} (>= file size {_fileData.Length})");
                        break;  // 遇到无效地址，停止更新
                    }

                    // 更新偏移量
                    uint newOffset = entry.Offset + delta;  // ✅ 使用Offset

                    // 验证新偏移量是否合理
                    if (newOffset >= _fileData.Length)
                    {
                        System.Diagnostics.Debug.WriteLine($"WARNING: Skipping resource {i} - New offset 0x{newOffset:X} would exceed file size {_fileData.Length}");
                        break;  // 新地址超出范围，停止更新
                    }

                    uint offset = _tableOffset + i * 8;  // 偏移量字段在偏移 0

                    var addrBytes = BitConverter.GetBytes(newOffset);
                    Array.Copy(addrBytes, 0, _fileData, offset, 4);

                    // 更新内存表
                    entry.Offset = newOffset;  // ✅ 使用Offset
                    _resourceTable[(int)i] = entry;
                }
                else if (entry.Offset == 0)  // ✅ 使用Offset
                {
                    // 遇到空条目，停止
                    break;
                }
            }
        }

        /// <summary>
        /// 验证数据移动后所有受影响资源的完整性
        /// </summary>
        private void ValidateAfterDataMove(uint dataEnd, uint delta, bool isExpanding)
        {
            for (int i = 0; i < _resourceTable.Count; i++)
            {
                var entry = _resourceTable[i];
                if (entry.Offset < dataEnd || entry.Offset == 0)
                    continue;

                uint newOffset = isExpanding ? entry.Offset + delta : entry.Offset - delta;

                if (newOffset + 4 > _fileData.Length)
                {
                    Logger.Warning($"Resource {i} new offset 0x{newOffset:X} exceeds file size {_fileData.Length}");
                    continue;
                }

                var header = new byte[4];
                Array.Copy(_fileData, (int)newOffset, header, 0, 4);

                bool isValid =
                    // JPEG: FF D8 FF
                    (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) ||
                    // PNG: 89 50 4E 47
                    (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) ||
                    // BMP: 42 4D
                    (header[0] == 0x42 && header[1] == 0x4D) ||
                    // WAV: 52 49 46 46
                    (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46) ||
                    // 其他有效格式（非全零、非全0xFF）
                    !(header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x00 && header[3] == 0x00) &&
                    !(header[0] == 0xFF && header[1] == 0xFF && header[2] == 0xFF && header[3] == 0xFF);

                if (!isValid)
                {
                    Logger.Warning($"Resource {i} at offset 0x{newOffset:X} may be corrupted after data move!");
                }
            }
        }

        /// <summary>
        /// 将内存中的资源表全部重新写入文件，确保文件内表与内存表一致
        /// 在数据移位操作（Compact/WithShift）后，文件内的表区域可能被覆写，
        /// 此方法修复所有条目的偏移和长度字段
        /// </summary>
        private void RewriteAllTableEntries()
        {
            for (int i = 0; i < _resourceTable.Count; i++)
            {
                uint entryOffset = _tableOffset + (uint)i * 8;
                if (entryOffset + 8 > _fileData.Length)
                    break;
                BitConverter.GetBytes(_resourceTable[i].Offset).CopyTo(_fileData, (int)entryOffset);
                BitConverter.GetBytes(_resourceTable[i].Length).CopyTo(_fileData, (int)entryOffset + 4);
            }
        }

        /// <summary>
        /// 保存修改后的文件
        /// </summary>
        public bool Save(string outputPath)
        {
            try
            {
                // 创建备份（使用时间戳命名，避免覆盖之前的备份）
                string backupPath = outputPath + ".backup-" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                if (File.Exists(outputPath))
                {
                    File.Copy(outputPath, backupPath, true);
                    Logger.Info($"Backup created: {backupPath}");
                }

                File.WriteAllBytes(outputPath, _fileData);
                Logger.Info($"Saved to: {outputPath}");
                Logger.Info($"File size: {_fileData.Length} bytes");
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Save failed: {ex.Message}";
                Logger.Error(ErrorMessage!);
                return false;
            }
        }

        /// <summary>
        /// 获取修改后的数据
        /// </summary>
        public byte[] GetData()
        {
            var copy = new byte[_fileData.Length];
            Array.Copy(_fileData, copy, _fileData.Length);
            return copy;
        }

        /// <summary>
        /// 获取更新后的资源表（用于同步回 ResBinParser）
        /// </summary>
        public List<ResInfoEntry> GetResourceTable()
        {
            return _resourceTable;
        }
    }
}
