using System;
using System.IO;
using System.Runtime.InteropServices;  // ✅ P4: Marshal和GCHandle
using System.Text;

namespace ResBinManager.Core
{
    /// <summary>
    /// DestBin.bin 固件文件解析和修改引擎
    /// 支持直接提取、替换 RES.BIN 资源段，无需重新编译和打包
    /// </summary>
    public class DestBinParser : IDisposable
    {
        // DestBin.bin 结构常量
        private const uint PROGRAM_CODE_SIZE = 0x9DC00;  // 程序代码段大小 (646,144 bytes) - 标准值
        private const string BLDR_SIGNATURE = "BLDR";     // Bootloader 签名
        private const int SYSTEM_FLAY_SIZE = 512;         // 配置区大小 (127个配置项 ×4 + 1个校验和 ×4)

        private byte[]? _destBinData;
        private string? _filePath;

        // RES.BIN 相关信息
        private byte[]? _resBinData;
        private uint _resBinOffset;
        private int _resBinSize;

        private bool _disposed = false;

        public string? ErrorMessage { get; private set; }
        public bool IsLoaded { get; private set; }

        /// <summary>
        /// DestBin.bin 总大小
        /// </summary>
        public int TotalSize => _destBinData?.Length ?? 0;

        /// <summary>
        /// 程序代码段大小
        /// </summary>
        public uint ProgramCodeSize => PROGRAM_CODE_SIZE;

        /// <summary>
        /// RES.BIN 在 DestBin.bin 中的偏移量
        /// </summary>
        public uint ResBinOffset => _resBinOffset;

        /// <summary>
        /// 获取完整的 DestBin.bin 数据
        /// </summary>
        /// <returns>DestBin.bin 数据，未加载返回 null</returns>
        public byte[]? GetDestBinData()
        {
            if (!IsLoaded || _destBinData == null)
            {
                ErrorMessage = "DestBin.bin not loaded";
                return null;
            }

            var result = new byte[_destBinData.Length];
            Array.Copy(_destBinData, result, _destBinData.Length);
            return result;
        }

        public void UpdateDestBinData(byte[] newData)
        {
            if (newData == null)
                return;

            _destBinData = newData;
            System.Diagnostics.Debug.WriteLine($"[DestBinParser] DestBin data updated, new size: {_destBinData.Length}");
        }

        /// <summary>
        /// RES.BIN 的大小
        /// </summary>
        public int ResBinSize => _resBinSize;

        /// <summary>
        /// 资源区起始地址（逻辑地址，用于计算配置区地址）
        /// </summary>
        public uint ResAddress => _resBinOffset;

        /// <summary>
        /// 资源区大小（字节，用于计算配置区地址）
        /// </summary>
        public uint ResSize => (uint)_resBinSize;

        /// <summary>
        /// 计算配置区地址（4KB对齐）
        /// </summary>
        /// <returns>配置区起始地址</returns>
        public uint CalculateConfigAddress()
        {
            const uint alignment = 0x1000;
            uint configAddress = _resBinOffset + (uint)_resBinSize;
            configAddress = (configAddress + alignment - 1) & ~(alignment - 1);
            return configAddress;
        }

        /// <summary>
        /// 尾部填充大小
        /// </summary>
        public int TailPaddingSize => _destBinData != null
            ? _destBinData.Length - (int)(_resBinOffset + _resBinSize)
            : 0;

        /// <summary>
        /// 固件版本号（从文件头解析，格式: 0x00MMmmbb）
        /// </summary>
        public string? FirmwareVersion { get; private set; }

        /// <summary>
        /// MAGICKEY常量值（从偏移0x10读取）
        /// </summary>
        public uint MagicKey { get; private set; }

        /// <summary>
        /// 固件编译时间（从程序代码段中的 "build time:%s" 字符串附近提取）
        /// 对应 SDK: uart_Printf("[V%d.%d]build time:%s\n", VERSION_MAIN, VERSION_SUB, VERSION_TIME)
        /// 二进制中格式: "build time:%s\n\0" 后紧跟 "YYYY/MM/DD HH:MM:SS\0"
        /// </summary>
        public string? BuildTime { get; private set; }

        /// <summary>
        /// Flash参数结构（从flash_param解析）
        /// ✅ P4: 新增完整的flash_param解析
        /// </summary>
        public FlashParam? FlashParamInfo { get; private set; }

        /// <summary>
        /// 固件序列号或构建ID（从文件头解析）
        /// ⚠️ 已废弃: SDK中偏移0x10是MAGICKEY常量，不是序列号
        /// </summary>
        [Obsolete("Use MagicKey property instead. FirmwareSerial is deprecated.")]
        public string? FirmwareSerial { get; private set; }

        public DestBinParser()
        {
            IsLoaded = false;
            _resBinOffset = PROGRAM_CODE_SIZE;  // 默认使用已知偏移
        }

        /// <summary>
        /// 加载 DestBin.bin 文件
        /// </summary>
        /// <param name="filePath">DestBin.bin 文件路径</param>
        /// <returns>是否加载成功</returns>
        public bool Load(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    ErrorMessage = $"File not found: {filePath}";
                    return false;
                }

                // 读取文件
                _destBinData = File.ReadAllBytes(filePath);
                _filePath = filePath;

                // 验证文件大小
                if (_destBinData.Length < PROGRAM_CODE_SIZE + 1024)
                {
                    ErrorMessage = $"File too small to be a valid DestBin.bin (size: {_destBinData.Length})";
                    _destBinData = null;
                    return false;
                }

                // 验证文件头签名
                if (!ValidateHeader())
                {
                    ErrorMessage = "Invalid DestBin.bin header (missing BLDR signature)";
                    _destBinData = null;
                    return false;
                }

                // 解析版本信息
                ParseVersionInfo();

                // ✅ 使用启动扇区解析替代硬编码偏移(模拟SDK的nv_init)
                if (!ParseBootSector())
                {
                    ErrorMessage = "Failed to parse boot sector information";
                    _destBinData = null;
                    return false;
                }

                // 提取 RES.BIN（使用 ParseBootSector 解析的大小）
                _resBinData = new byte[_resBinSize];
                Array.Copy(_destBinData, _resBinOffset, _resBinData, 0, _resBinSize);

                // 验证 RES.BIN 有效性
                if (!ValidateResBin())
                {
                    ErrorMessage = "Extracted RES.BIN data is invalid";
                    _destBinData = null;
                    _resBinData = null;
                    return false;
                }

                IsLoaded = true;
                ErrorMessage = null;

                // 解析固件编译时间
                ParseBuildTime();

                System.Diagnostics.Debug.WriteLine(
                    $"[DestBinParser] Loaded successfully:\n" +
                    $"  Total Size: {_destBinData.Length} bytes ({_destBinData.Length / 1024.0:F2} KB)\n" +
                    $"  Program Code: {PROGRAM_CODE_SIZE} bytes ({PROGRAM_CODE_SIZE / 1024.0:F2} KB)\n" +
                    $"  RES.BIN Offset: 0x{_resBinOffset:X}\n" +
                    $"  RES.BIN Size: {_resBinSize} bytes ({_resBinSize / 1024.0:F2} KB)\n" +
                    $"  Tail Padding: {TailPaddingSize} bytes\n" +
                    $"  Build Time: {BuildTime ?? "N/A"}");

                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Load error: {ex.Message}";
                _destBinData = null;
                _resBinData = null;
                IsLoaded = false;
                return false;
            }
        }

        /// <summary>
        /// 验证 DestBin.bin 文件头
        /// </summary>
        private bool ValidateHeader()
        {
            if (_destBinData == null || _destBinData.Length < 12)
                return false;

            // 检查 BLDR 签名 (偏移 0x0004-0x0007)
            var signature = System.Text.Encoding.ASCII.GetString(_destBinData, 4, 4);
            return signature == BLDR_SIGNATURE;
        }

        /// <summary>
        /// 解析固件版本信息
        /// 对应 SDK: BLDRX32.S 第57行和boot_config.h第59/61行
        /// 偏移0x08: BLDR_VER (0x00020000)
        /// 偏移0x10: MAGICKEY (0x01234567)
        /// </summary>
        private void ParseVersionInfo()
        {
            if (_destBinData == null || _destBinData.Length < 20)
                return;

            try
            {
                // ===== 偏移 0x08-0x0B: BLDR_VER 固件版本 =====
                // SDK定义: #define BLDR_VER 0x00020000 (小端序)
                // 格式: 0x00MMmmbb (Major.Minor.Build)
                uint bldrVerRaw = BitConverter.ToUInt32(_destBinData, 8);

                // 解析版本号格式: 0x00MMmmbb
                byte major = (byte)((bldrVerRaw >> 16) & 0xFF);   // MM
                byte minor = (byte)((bldrVerRaw >> 8) & 0xFF);    // mm
                byte build = (byte)(bldrVerRaw & 0xFF);           // bb

                if (major > 0 || minor > 0 || build > 0)
                {
                    FirmwareVersion = $"v{major}.{minor}.{build} (0x{bldrVerRaw:X8})";
                    System.Diagnostics.Debug.WriteLine($"[ParseVersionInfo] BLDR_VER: {FirmwareVersion}");
                }
                else
                {
                    FirmwareVersion = $"0x{bldrVerRaw:X8}";
                    System.Diagnostics.Debug.WriteLine($"[ParseVersionInfo] BLDR_VER (raw): {FirmwareVersion}");
                }

                // ===== 偏移 0x10-0x13: MAGICKEY 常量 =====
                // SDK定义: #define MAGICKEY 0x01234567 (小端序)
                // 注意: 这不是ASCII序列号，而是固定的魔数常量！
                MagicKey = BitConverter.ToUInt32(_destBinData, 16);

                if (MagicKey == 0x01234567)
                {
                    System.Diagnostics.Debug.WriteLine($"[ParseVersionInfo] MAGICKEY: 0x{MagicKey:X8} ✓ (标准值)");
                }
                else if (MagicKey == 0x67452301)
                {
                    System.Diagnostics.Debug.WriteLine($"[ParseVersionInfo] MAGICKEY: 0x{MagicKey:X8} ⚠ (大端序变体)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ParseVersionInfo] MAGICKEY: 0x{MagicKey:X8} ❓ (非标准值)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParseVersionInfo] Error: {ex.Message}");
                FirmwareVersion = "Unknown";
                MagicKey = 0;
            }
        }

        /// <summary>
        /// 解析固件编译时间
        /// SDK源码: uart_Printf("[V%d.%d]build time:%s\n", VERSION_MAIN, VERSION_SUB, VERSION_TIME)
        /// 二进制中: 格式字符串 "build time:%s\n\0" 后紧跟 VERSION_TIME 字符串 "YYYY/MM/DD HH:MM:SS\0"
        /// 两者在 .rodata 段中通常相邻存储
        /// </summary>
        private void ParseBuildTime()
        {
            BuildTime = null;

            if (_destBinData == null || _destBinData.Length < 1024)
                return;

            try
            {
                // 搜索范围: 程序代码段 (0 ~ RES.BIN偏移)
                int searchEnd = (int)Math.Min(_destBinData.Length, _resBinOffset);

                // 1. 搜索 "build time:%s" 的 ASCII 字节序列
                byte[] searchPattern = Encoding.ASCII.GetBytes("build time:%s");
                int patternIndex = IndexOfBytes(_destBinData, searchPattern, 0, searchEnd);

                if (patternIndex >= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[ParseBuildTime] Found 'build time:%s' at offset 0x{patternIndex:X}");

                    // 从模式末尾开始，跳过格式字符串的剩余部分（\n\0），找到下一个字符串
                    int pos = patternIndex + searchPattern.Length;

                    // 跳过直到当前字符串的 null 终止符
                    while (pos < searchEnd && _destBinData[pos] != 0x00)
                        pos++;

                    // 跳过 null 终止符
                    pos++;

                    // 跳过可能的连续 null 填充
                    while (pos < searchEnd && _destBinData[pos] == 0x00)
                        pos++;

                    // 读取下一个 null 终止字符串（应为 VERSION_TIME）
                    if (pos < searchEnd)
                    {
                        int stringStart = pos;
                        while (pos < _destBinData.Length && _destBinData[pos] != 0x00)
                            pos++;

                        string timestamp = Encoding.ASCII.GetString(_destBinData, stringStart, pos - stringStart);

                        if (IsValidTimestamp(timestamp))
                        {
                            BuildTime = timestamp;
                            System.Diagnostics.Debug.WriteLine($"[ParseBuildTime] Build time (adjacent): {BuildTime}");
                            return;
                        }

                        System.Diagnostics.Debug.WriteLine($"[ParseBuildTime] Adjacent string is not a valid timestamp: '{timestamp}'");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ParseBuildTime] Pattern 'build time:%s' not found, searching for timestamp pattern");
                }

                // 2. 回退策略: 在程序代码段中搜索时间戳模式 "YYYY/MM/DD HH:MM:SS"
                BuildTime = SearchForTimestampPattern(_destBinData, 0, searchEnd);

                if (BuildTime != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ParseBuildTime] Build time (pattern search): {BuildTime}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ParseBuildTime] Build time not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParseBuildTime] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 在字节数组中搜索指定模式（Boyer-Moore 简化版）
        /// </summary>
        private static int IndexOfBytes(byte[] haystack, byte[] needle, int startIndex, int endIndex)
        {
            int needleLen = needle.Length;
            if (needleLen == 0 || startIndex >= endIndex)
                return -1;

            int searchLimit = endIndex - needleLen;
            for (int i = startIndex; i <= searchLimit; i++)
            {
                bool match = true;
                for (int j = 0; j < needleLen; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 验证字符串是否符合时间戳格式: YYYY/MM/DD HH:MM:SS
        /// </summary>
        private static bool IsValidTimestamp(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 19)
                return false;

            // 格式: YYYY/MM/DD HH:MM:SS
            // 检查分隔符位置
            if (s[4] != '/' || s[7] != '/' || s[10] != ' ' || s[13] != ':' || s[16] != ':')
                return false;

            // 检查数字部分
            for (int i = 0; i < 19; i++)
            {
                if (i == 4 || i == 7 || i == 10 || i == 13 || i == 16)
                    continue;
                if (!char.IsDigit(s[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 在二进制数据中搜索时间戳模式 "YYYY/MM/DD HH:MM:SS"
        /// </summary>
        private static string? SearchForTimestampPattern(byte[] data, int startIndex, int endIndex)
        {
            // 时间戳格式: "YYYY/MM/DD HH:MM:SS" = 19 字节
            // 特征: 数字 数字 数字 数字 / 数字 数字 / 数字 数字 空格 数字 数字 : 数字 数字 : 数字 数字
            if (endIndex - startIndex < 19)
                return null;

            int searchLimit = endIndex - 19;
            for (int i = startIndex; i <= searchLimit; i++)
            {
                // 快速检查: 第5字节必须是 '/' (0x2F)
                if (data[i + 4] != 0x2F)
                    continue;

                // 检查第8字节也是 '/'
                if (data[i + 7] != 0x2F)
                    continue;

                // 检查第11字节是空格 (0x20)
                if (data[i + 10] != 0x20)
                    continue;

                // 检查第14和17字节是 ':'
                if (data[i + 13] != 0x3A || data[i + 16] != 0x3A)
                    continue;

                // 检查其余位置都是数字 (0x30-0x39)
                bool allDigits = true;
                for (int j = 0; j < 19; j++)
                {
                    if (j == 4 || j == 7 || j == 10 || j == 13 || j == 16)
                        continue;
                    if (data[i + j] < 0x30 || data[i + j] > 0x39)
                    {
                        allDigits = false;
                        break;
                    }
                }

                if (allDigits)
                {
                    return Encoding.ASCII.GetString(data, i, 19);
                }
            }

            return null;
        }

        /// <summary>
        /// 解析DestBin.bin的启动扇区信息(模拟SDK的nv_init)
        /// 对应 nvfs.c 第95-119行的逻辑
        /// ✅ P4: 新增flash_param完整解析
        /// </summary>
        private bool ParseBootSector()
        {
            if (_destBinData == null || _destBinData.Length < 32)
                return false;

            try
            {
                // ===== 1. 解析启动扇区头部 (BootSectorHeader) =====
                // 偏移0x00-0x0B
                uint bldrVer = BitConverter.ToUInt32(_destBinData, 0);
                uint magic = BitConverter.ToUInt32(_destBinData, 4);
                byte checkSum = _destBinData[8];
                byte bootSectorNum = _destBinData[9];
                byte bootFlagByte = _destBinData[10];
                byte reserved = _destBinData[11];

                if (magic != 0x52444C42) // "BLDR"
                {
                    System.Diagnostics.Debug.WriteLine("[ParseBootSector] Invalid magic number");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"[ParseBootSector] BLDR_VER: 0x{bldrVer:X8}");
                System.Diagnostics.Debug.WriteLine($"[ParseBootSector] Boot sector num: {bootSectorNum}");

                // ===== 2. 计算flash_param位置 =====
                uint flashParamOffset = (uint)(bootSectorNum << 4); // ×16

                if (flashParamOffset + Marshal.SizeOf<FlashParam>() > _destBinData.Length)
                {
                    System.Diagnostics.Debug.WriteLine("[ParseBootSector] Flash param data out of range");
                    return false;
                }

                // ===== 3. 解析flash_param结构 =====
                byte[] flashParamBytes = new byte[Marshal.SizeOf<FlashParam>()];
                Array.Copy(_destBinData, flashParamOffset, flashParamBytes, 0, flashParamBytes.Length);

                GCHandle handle = GCHandle.Alloc(flashParamBytes, GCHandleType.Pinned);
                try
                {
                    FlashParamInfo = (FlashParam)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(FlashParam))!;

                    System.Diagnostics.Debug.WriteLine($"[ParseBootSector] FlashParam parsed successfully:");
                    System.Diagnostics.Debug.WriteLine($"  TextStart: 0x{FlashParamInfo.Value.TextStart:X8}");
                    System.Diagnostics.Debug.WriteLine($"  TextSec: {FlashParamInfo.Value.TextSec}");
                    System.Diagnostics.Debug.WriteLine($"  TextLen: {FlashParamInfo.Value.TextLen} bytes");
                    System.Diagnostics.Debug.WriteLine($"  MagicKey: 0x{FlashParamInfo.Value.MagicKey:X8}");
                    System.Diagnostics.Debug.WriteLine($"  SpiDmaShift: 0x{FlashParamInfo.Value.SpiDmaShift:X8}");
                }
                finally
                {
                    handle.Free();
                }

                // ===== 4. 读取资源区信息 (在flash_param偏移处的+0x08和+0x0C) =====
                // 注意: 这两个字段位于boot_sector的特定偏移，不在flash_param结构中
                uint resSectorNum = BitConverter.ToUInt32(_destBinData, (int)(flashParamOffset + 0x08));
                _resBinOffset = resSectorNum << 9; // ×512转换为字节地址

                uint resSizeSectors = BitConverter.ToUInt32(_destBinData, (int)(flashParamOffset + 0x0C));
                _resBinSize = (int)(resSizeSectors << 9); // ×512转换为字节大小

                System.Diagnostics.Debug.WriteLine($"[ParseBootSector] RES.BIN offset: 0x{_resBinOffset:X} ({_resBinOffset} bytes)");
                System.Diagnostics.Debug.WriteLine($"[ParseBootSector] RES.BIN size: {_resBinSize} bytes ({_resBinSize / 1024.0:F2} KB)");

                // ===== 5. 验证解析结果合理性 =====
                if (_resBinOffset + _resBinSize > _destBinData.Length)
                {
                    System.Diagnostics.Debug.WriteLine("[ParseBootSector] RES.BIN exceeds file size");
                    return false;
                }

                if (_resBinSize < 1024) // 至少应该有1KB的资源
                {
                    System.Diagnostics.Debug.WriteLine("[ParseBootSector] RES.BIN size too small");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParseBootSector] Exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 验证提取的 RES.BIN 数据是否有效（轻量级检查，不做完整解析）
        /// 仅验证索引表头部结构的合理性，完整解析由调用方在需要时执行
        /// </summary>
        private bool ValidateResBin()
        {
            if (_resBinData == null || _resBinData.Length < 1024)
                return false;

            // 读取第一个资源条目（8字节: offset + length）
            uint firstOffset = BitConverter.ToUInt32(_resBinData, 0);
            uint firstLength = BitConverter.ToUInt32(_resBinData, 4);

            // 空条目表示无效
            if (firstOffset == 0 && firstLength == 0)
            {
                System.Diagnostics.Debug.WriteLine("[ValidateResBin] First entry is empty (0, 0)");
                return false;
            }

            // 第一个资源的偏移即为资源表结束位置（firstResAddr），必须 > 0 且在文件范围内
            if (firstOffset == 0 || firstOffset >= _resBinData.Length)
            {
                System.Diagnostics.Debug.WriteLine($"[ValidateResBin] Invalid firstOffset: 0x{firstOffset:X} (file size: {_resBinData.Length})");
                return false;
            }

            // 验证偏移 8 字节对齐（每个条目 8 字节）
            if (firstOffset % 8 != 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ValidateResBin] firstOffset 0x{firstOffset:X} not 8-byte aligned");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"[ValidateResBin] Valid: firstOffset=0x{firstOffset:X}, firstLength={firstLength}, estimated resources={firstOffset / 8}");
            return true;
        }

        /// <summary>
        /// 提取 RES.BIN 数据
        /// </summary>
        /// <returns>RES.BIN 数据，失败返回 null</returns>
        public byte[]? ExtractResBin()
        {
            if (!IsLoaded || _resBinData == null)
            {
                ErrorMessage = "DestBin.bin not loaded or RES.BIN not extracted";
                return null;
            }

            var result = new byte[_resBinData.Length];
            Array.Copy(_resBinData, result, _resBinData.Length);

            System.Diagnostics.Debug.WriteLine($"[DestBinParser] Extracted RES.BIN: {result.Length} bytes");
            return result;
        }

        /// <summary>
        /// 替换 RES.BIN 数据
        /// </summary>
        /// <param name="newResBinData">新的 RES.BIN 数据</param>
        /// <param name="keepSize">是否保持原始大小（如果为 true，会自动填充或截断）</param>
        /// <returns>是否替换成功</returns>
        public bool ReplaceResBin(byte[] newResBinData, bool keepSize = true)
        {
            if (!IsLoaded || _destBinData == null)
            {
                ErrorMessage = "DestBin.bin not loaded";
                return false;
            }

            if (newResBinData == null || newResBinData.Length == 0)
            {
                ErrorMessage = "New RES.BIN data is empty";
                return false;
            }

            try
            {
                byte[] dataToWrite = newResBinData;

                if (keepSize)
                {
                    // 保持原始大小
                    if (newResBinData.Length != _resBinSize)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[DestBinParser] RES.BIN size mismatch: new={newResBinData.Length}, original={_resBinSize}");

                        if (newResBinData.Length < _resBinSize)
                        {
                            // 新数据较小，用 0xFF 填充（Flash 未编程状态）
                            dataToWrite = new byte[_resBinSize];
                            Array.Copy(newResBinData, dataToWrite, newResBinData.Length);
                            // 填充 0xFF
                            for (int i = newResBinData.Length; i < _resBinSize; i++)
                            {
                                dataToWrite[i] = 0xFF;
                            }
                            System.Diagnostics.Debug.WriteLine($"[DestBinParser] Padded with 0xFF to maintain size");
                        }
                        else
                        {
                            // 新数据较大，截断（警告用户）
                            dataToWrite = new byte[_resBinSize];
                            Array.Copy(newResBinData, dataToWrite, _resBinSize);
                            System.Diagnostics.Debug.WriteLine($"[DestBinParser] WARNING: Truncated RES.BIN to maintain size");

                            ErrorMessage = $"Warning: RES.BIN was truncated from {newResBinData.Length} to {_resBinSize} bytes";
                        }
                    }

                    // 直接覆盖 RES.BIN 区域（保持原始大小的情况）
                    Array.Copy(dataToWrite, 0, _destBinData, (int)_resBinOffset, dataToWrite.Length);
                    _resBinData = dataToWrite;

                    System.Diagnostics.Debug.WriteLine($"[DestBinParser] RES.BIN replaced successfully (size unchanged)");
                    ErrorMessage = null;
                    return true;
                }
                else
                {
                    // 不保持大小，允许改变（需要调整尾部填充）
                    // 总是重新计算文件大小，确保配置区有足够空间
                    System.Diagnostics.Debug.WriteLine(
                        $"[DestBinParser] RES.BIN size: {newResBinData.Length} (current: {_resBinSize})");

                    // 重新计算 DestBin.bin 大小
                    // 1. 计算资源结束地址
                    uint resourceEndAddress = _resBinOffset + (uint)newResBinData.Length;
                    
                    // 2. 配置区地址需要向上对齐到 4KB 边界（与固件 nv_configAddr 算法一致）
                    //    固件算法: if(addr&0xfff) addr = (addr&0xfffff000)+0x1000;
                    uint newConfigAddress = resourceEndAddress;
                    if ((newConfigAddress & 0xFFF) != 0)
                    {
                        newConfigAddress = (newConfigAddress & 0xFFFFF000) + 0x1000;
                    }
                    
                    // 3. 文件大小 = 对齐后的配置区地址 + 配置区大小 + 对齐填充
                    int newSize = (int)newConfigAddress + SYSTEM_FLAY_SIZE;

                    // 确保 4KB 对齐
                    int paddingNeeded = (4096 - (newSize % 4096)) % 4096;
                    newSize += paddingNeeded;

                    System.Diagnostics.Debug.WriteLine($"[DestBinParser] New total size: {newSize} bytes (padding: {paddingNeeded}, resBinOffset: 0x{_resBinOffset:X})");
                    System.Diagnostics.Debug.WriteLine($"[DestBinParser] Config address: 0x{newConfigAddress:X} (resource end: 0x{resourceEndAddress:X})");

                    // 创建新的 DestBin.bin 数据
                    var newData = new byte[newSize];

                    // 复制程序代码段和其他前面的数据（到 _resBinOffset）
                    Array.Copy(_destBinData, 0, newData, 0, (int)_resBinOffset);

                    // 复制新的 RES.BIN
                    Array.Copy(newResBinData, 0, newData, (int)_resBinOffset, newResBinData.Length);

                    // 计算原配置区位置（用于复制原有配置数据）
                    uint oldConfigAddress = _resBinOffset + (uint)_resBinSize;
                    if ((oldConfigAddress & 0xFFF) != 0)
                    {
                        oldConfigAddress = (oldConfigAddress & 0xFFFFF000) + 0x1000;
                    }

                    // 如果原文件中有配置区数据，复制到新位置
                    if (oldConfigAddress + SYSTEM_FLAY_SIZE <= _destBinData.Length)
                    {
                        // 计算配置区在新文件中的位置
                        uint configOffsetInNewData = newConfigAddress;
                        
                        if (configOffsetInNewData + SYSTEM_FLAY_SIZE <= newSize)
                        {
                            // 复制原配置区数据到新位置
                            Array.Copy(_destBinData, (int)oldConfigAddress, 
                                      newData, (int)configOffsetInNewData, SYSTEM_FLAY_SIZE);
                            
                            System.Diagnostics.Debug.WriteLine($"[DestBinParser] Preserved config data: old address 0x{oldConfigAddress:X} -> new address 0x{newConfigAddress:X}");
                        }
                    }

                    // 其余部分填充 0xFF（Flash 未编程状态）
                    // 注意：配置区空间已经由 new byte[newSize] 初始化为 0，
                    // 有配置数据的位置会被 Array.Copy 覆盖，没有数据的位置保持为 0
                    for (int i = (int)_resBinOffset + newResBinData.Length; i < newSize; i++)
                    {
                        // 跳过配置区（已处理，填充为 0）
                        if (i >= (int)newConfigAddress && i < (int)newConfigAddress + SYSTEM_FLAY_SIZE)
                            continue;
                        
                        newData[i] = 0xFF;
                    }

                    _destBinData = newData;
                    _resBinData = newResBinData;
                    _resBinSize = newResBinData.Length;

                    // 更新启动扇区中的 flash_param 结构（res_size_sectors）
                    UpdateFlashParamResSize(_resBinSize);

                    System.Diagnostics.Debug.WriteLine($"[DestBinParser] RES.BIN replaced successfully (size changed)");
                    ErrorMessage = null;
                    return true;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Replace error: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 更新启动扇区中的 flash_param 结构（res_size_sectors）
        /// </summary>
        /// <param name="newResSize">新的 RES.BIN 大小（字节）</param>
        private void UpdateFlashParamResSize(int newResSize)
        {
            if (_destBinData == null)
                return;

            // 计算新的 res_size_sectors（向上取整）
            uint resSizeSectors = (uint)((newResSize + 511) / 512);

            // 找到 flash_param 的位置
            // flash_param 位于 bootSectorNum × 16 的偏移处
            // 我们需要找到 bootSectorNum
            uint bootSectorNum = 0;
            if (_destBinData.Length > 9)
            {
                bootSectorNum = _destBinData[9];
            }

            uint flashParamOffset = bootSectorNum << 4; // ×16

            // 更新 res_size_sectors（偏移 0x0C）
            uint resSizeOffset = flashParamOffset + 0x0C;
            if (resSizeOffset + 4 <= _destBinData.Length)
            {
                byte[] resSizeBytes = BitConverter.GetBytes(resSizeSectors);
                Array.Copy(resSizeBytes, 0, _destBinData, (int)resSizeOffset, 4);
                
                System.Diagnostics.Debug.WriteLine($"[DestBinParser] Updated flash_param res_size_sectors: {resSizeSectors} (0x{resSizeSectors:X})");
            }
        }

        /// <summary>
        /// 创建当前内部状态的快照，用于事务回滚
        /// </summary>
        public object? CreateSnapshot()
        {
            if (_destBinData == null && _resBinData == null)
                return null;

            return new DestBinSnapshot
            {
                DestBinData = _destBinData != null ? (byte[])_destBinData.Clone() : null,
                ResBinData = _resBinData != null ? (byte[])_resBinData.Clone() : null,
                ResBinSize = _resBinSize,
                ErrorMessage = ErrorMessage
            };
        }

        /// <summary>
        /// 从快照恢复内部状态
        /// </summary>
        public void RestoreSnapshot(object? snapshot)
        {
            if (snapshot is not DestBinSnapshot state)
                return;

            _destBinData = state.DestBinData;
            _resBinData = state.ResBinData;
            _resBinSize = state.ResBinSize;
            ErrorMessage = state.ErrorMessage;
        }

        private class DestBinSnapshot
        {
            public byte[]? DestBinData { get; set; }
            public byte[]? ResBinData { get; set; }
            public int ResBinSize { get; set; }
            public string? ErrorMessage { get; set; }
        }

        /// <summary>
        /// 保存修改后的 DestBin.bin 文件
        /// </summary>
        /// <param name="outputPath">输出文件路径</param>
        /// <returns>是否保存成功</returns>
        public bool Save(string outputPath)
        {
            if (!IsLoaded || _destBinData == null)
            {
                ErrorMessage = "DestBin.bin not loaded";
                return false;
            }

            try
            {
                // 确保输出目录存在
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 写入文件
                File.WriteAllBytes(outputPath, _destBinData);

                System.Diagnostics.Debug.WriteLine($"[DestBinParser] Saved to: {outputPath} ({_destBinData.Length} bytes)");
                ErrorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Save error: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 获取 DestBin.bin 结构信息
        /// </summary>
        public string GetStructureInfo()
        {
            if (!IsLoaded || _destBinData == null)
                return "Not loaded";

            return $@"DestBin.bin Structure:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Total Size:       {_destBinData.Length,12:N0} bytes ({_destBinData.Length / 1024.0:F2} KB)
Program Code:     {PROGRAM_CODE_SIZE,12:N0} bytes ({PROGRAM_CODE_SIZE / 1024.0:F2} KB) [0x000000 - 0x{(PROGRAM_CODE_SIZE - 1):X6}]
RES.BIN Offset:   {_resBinOffset,12:X6} ({_resBinOffset,12:N0} bytes)
RES.BIN Size:     {_resBinSize,12:N0} bytes ({_resBinSize / 1024.0:F2} KB)
Tail Padding:     {TailPaddingSize,12:N0} bytes ({TailPaddingSize / 1024.0:F2} KB)
Alignment:        {(TotalSize % 4096 == 0 ? "✓ 4KB aligned" : "✗ Not aligned")}
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _destBinData = null;
                _resBinData = null;
                IsLoaded = false;
                ErrorMessage = null;
            }

            _disposed = true;
        }

        ~DestBinParser()
        {
            Dispose(false);
        }
    }
}
