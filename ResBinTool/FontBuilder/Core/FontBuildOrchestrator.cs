using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FontBuilder.Models;

namespace FontBuilder.Core
{
    /// <summary>
    /// 字体构建编排器：串联解析→收集→渲染→写入全流程
    /// 等价于 fontSrc.exe + userStr.exe 的完整执行
    ///
    /// 流程:
    ///   1. 解析 font.ini    → FontBuildConfig
    ///   2. 解析 fontSelect.txt → FontSelectConfig
    ///   3. 加载各语言字符串   (FontSrcTxtParser)
    ///   4. 收集去重字符       (CharCollector)
    ///   5. 渲染字符位图       (GlyphRenderer)
    ///   6. 写入输出文件:
    ///      - font.bin / resfont.bin / resfontidx.bin
    ///      - font.tab / user_str.c / user_str.h
    /// </summary>
    public sealed class FontBuildOrchestrator
    {
        /// <summary>
        /// 进度报告: (已完成数, 总数, 阶段描述)
        /// </summary>
        public IProgress<(int done, int total, string stage)> Progress { get; set; }

        /// <summary>
        /// 同步构建（阻塞调用线程）
        /// </summary>
        /// <param name="iniPath">font.ini 绝对路径</param>
        /// <param name="cancellationToken">取消令牌</param>
        public BuildResult Build(string iniPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(iniPath))
                throw new ArgumentNullException(nameof(iniPath));
            if (!File.Exists(iniPath))
                throw new FileNotFoundException($"font.ini not found: {iniPath}");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var log = new List<string>();

            void Log(string msg)
            {
                log.Add(msg);
            }

            try
            {
                // ---------- 阶段 1: 解析 font.ini ----------
                Log($"[1/6] 解析 font.ini: {iniPath}");
                Progress?.Report((1, 6, "解析 font.ini"));
                var config = FontIniParser.Parse(iniPath);

                string baseDir = Path.GetDirectoryName(Path.GetFullPath(iniPath)) ?? string.Empty;

                // 补充 font.ini 未指定的输出路径（与 font.ini 同目录）
                if (string.IsNullOrEmpty(config.FontSelectPath) || !Path.IsPathRooted(config.FontSelectPath))
                    config.FontSelectPath = Path.Combine(baseDir, "fontSelect.txt");

                config.FontBinPath = ResolveOrDefault(config.FontBinPath, baseDir, "font.bin");
                config.ResFontBinPath = ResolveOrDefault(config.ResFontBinPath, baseDir, "resfont.bin");
                config.ResFontIdxPath = ResolveOrDefault(config.ResFontIdxPath, baseDir, "resfontidx.bin");

                Log($"  语言数: {config.Languages.Count}");
                foreach (var lang in config.Languages)
                    Log($"    [{lang.Index}] {lang.Name}: {Path.GetFileName(lang.FilePath)}");

                cancellationToken.ThrowIfCancellationRequested();

                // ---------- 阶段 2: 解析 fontSelect.txt ----------
                Log("[2/6] 解析 fontSelect.txt");
                Progress?.Report((2, 6, "解析 fontSelect.txt"));
                var fontConfig = FontSelectConfig.Parse(config.FontSelectPath);
                Log($"  字体: {fontConfig}");

                cancellationToken.ThrowIfCancellationRequested();

                // ---------- 阶段 3: 加载语言字符串 ----------
                Log("[3/6] 加载语言字符串");
                Progress?.Report((3, 6, "加载语言字符串"));
                FontSrcTxtParser.LoadLanguageStrings(config);

                int totalStrings = 0;
                foreach (var lang in config.Languages)
                {
                    Log($"  {lang.Name}: {lang.Strings.Count} 条字符串");
                    totalStrings += lang.Strings.Count;
                }
                Log($"  合计: {totalStrings} 条字符串");

                // 校验各语言字符串数一致
                int expectedCount = config.Languages[0].Strings.Count;
                foreach (var lang in config.Languages)
                {
                    if (lang.Strings.Count != expectedCount)
                        Log($"  [警告] {lang.Name} 字符串数 {lang.Strings.Count} != {expectedCount}");
                }
                Log($"  预期字符串 ID 数: {StringIdNames.Count}");
                if (expectedCount != StringIdNames.Count)
                    Log($"  [警告] 字符串数 {expectedCount} != StringIdNames.Count {StringIdNames.Count}");

                cancellationToken.ThrowIfCancellationRequested();

                // ---------- 阶段 4: 收集去重字符 ----------
                Log("[4/6] 收集去重字符");
                Progress?.Report((4, 6, "收集字符"));
                var collectResult = CharCollector.Collect(config);
                Log($"  唯一字符数: {collectResult.CharCodes.Count}");

                cancellationToken.ThrowIfCancellationRequested();

                // ---------- 阶段 5: 渲染字符位图 ----------
                Log("[5/6] 渲染字符位图");
                List<CharGlyph> glyphs;
                using (var renderer = new GlyphRenderer(fontConfig))
                {
                    var glyphProgress = new Progress<(int done, int total, uint current)>(p =>
                    {
                        Progress?.Report((p.done, p.total, $"渲染字符 {p.done}/{p.total} (0x{p.current:X4})"));
                    });
                    glyphs = renderer.RenderAll(collectResult.CharCodes, glyphProgress);
                }
                Log($"  已渲染: {glyphs.Count} 个字形");

                cancellationToken.ThrowIfCancellationRequested();

                // ---------- 阶段 6: 写入输出文件 ----------
                Log("[6/6] 写入输出文件");
                Progress?.Report((0, 6, "写入输出文件"));

                // 6a. font.bin
                Log($"  写入 font.bin: {config.FontBinPath}");
                FontBinWriter.Write(glyphs, config.FontBinPath);
                Progress?.Report((1, 6, "font.bin 已写入"));

                // 6b. resfont.bin
                Log($"  写入 resfont.bin: {config.ResFontBinPath}");
                ResFontBinWriter.Write(glyphs, config.ResFontBinPath);
                Progress?.Report((2, 6, "resfont.bin 已写入"));

                // 6c. resfontidx.bin
                Log($"  写入 resfontidx.bin: {config.ResFontIdxPath}");
                ResFontIdxWriter.Write(config, collectResult, glyphs, config.ResFontIdxPath);
                Progress?.Report((3, 6, "resfontidx.bin 已写入"));

                // 6d. font.tab
                var stringIdNames = new List<string>(StringIdNames.All);
                Log($"  写入 font.tab: {config.FontTabPath}");
                FontTabWriter.Write(stringIdNames, config.FontTabPath);
                Progress?.Report((4, 6, "font.tab 已写入"));

                // 6e. user_str.c
                Log($"  写入 user_str.c: {config.UserStrCPath}");
                UserStrGenerator.WriteSource(stringIdNames, config, config.UserStrCPath);
                Progress?.Report((5, 6, "user_str.c 已写入"));

                // 6f. user_str.h
                Log($"  写入 user_str.h: {config.UserStrHPath}");
                UserStrGenerator.WriteHeader(stringIdNames, config, config.UserStrHPath);
                Progress?.Report((6, 6, "user_str.h 已写入"));

                stopwatch.Stop();
                Log($"构建完成，耗时 {stopwatch.ElapsedMilliseconds} ms");

                return new BuildResult
                {
                    Success = true,
                    Config = config,
                    CharCount = collectResult.CharCodes.Count,
                    StringCount = expectedCount,
                    LanguageCount = config.Languages.Count,
                    Glyphs = glyphs,
                    CollectResult = collectResult,
                    Log = log,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                Log("构建已取消");
                return new BuildResult
                {
                    Success = false,
                    Cancelled = true,
                    Log = log,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Log($"[错误] {ex.Message}");
                Log(ex.StackTrace ?? "");
                return new BuildResult
                {
                    Success = false,
                    Error = ex,
                    Log = log,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }
        }

        /// <summary>
        /// 异步构建
        /// </summary>
        public Task<BuildResult> BuildAsync(string iniPath, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Build(iniPath, cancellationToken), cancellationToken);
        }

        private static string ResolveOrDefault(string path, string baseDir, string defaultName)
        {
            if (string.IsNullOrEmpty(path) || path == $".\\{defaultName}" || path == $"./{defaultName}")
                return Path.Combine(baseDir, defaultName);
            if (Path.IsPathRooted(path)) return path;
            return Path.GetFullPath(Path.Combine(baseDir, path));
        }

        /// <summary>
        /// 构建结果
        /// </summary>
        public sealed class BuildResult
        {
            public bool Success { get; set; }
            public bool Cancelled { get; set; }
            public Exception Error { get; set; }
            public FontBuildConfig Config { get; set; }
            public List<CharGlyph> Glyphs { get; set; }
            public CharCollector.CollectResult CollectResult { get; set; }
            public int CharCount { get; set; }
            public int StringCount { get; set; }
            public int LanguageCount { get; set; }
            public List<string> Log { get; set; } = new();
            public long ElapsedMilliseconds { get; set; }
        }
    }
}
