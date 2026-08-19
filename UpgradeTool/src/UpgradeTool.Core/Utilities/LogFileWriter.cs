using System.Globalization;
using System.IO;

namespace UpgradeTool.Core.Utilities;

/// <summary>
/// 日志文件写入器，将日志消息同步写入磁盘文件。
/// 可在应用级持久使用（fileNamePrefix="dc503j"），也可在每次刷写会话中创建（fileNamePrefix="flash"）。
/// 写入路径：{LogDirectory}/{fileNamePrefix}_{yyyyMMdd_HHmmss}_{唯一ID}.log
///
/// 特性：
///   - 文件创建失败时优雅降级（回退到临时目录，不抛异常中断主流程）
///   - 缓冲写入 + 后台定时刷盘，避免每条日志都触发 IO
///   - Dispose 时正确写入结束标记（已修复旧版先置 _disposed=true 导致标记丢失的 Bug）
/// </summary>
public sealed class LogFileWriter : IDisposable
{
    private StreamWriter? _writer;
    private readonly Timer? _flushTimer;
    private readonly object _sync = new();
    private bool _disposed;

    /// <summary>默认日志目录（工具同级 logs/）。</summary>
    public static readonly string DefaultLogDirectory = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "logs");

    /// <summary>后台刷盘间隔（毫秒）。</summary>
    private const int FlushIntervalMs = 1000;

    /// <summary>
    /// 创建日志文件写入器。
    /// </summary>
    /// <param name="logDirectory">日志文件保存目录（默认 logs/）。</param>
    /// <param name="fileNamePrefix">文件名前缀（默认 "flash"）。</param>
    /// <param name="maxAgeHours">最大保留时长（小时），超出此时间的旧日志文件将被自动清理。</param>
    public LogFileWriter(
        string? logDirectory = null,
        string fileNamePrefix = "flash",
        int maxAgeHours = 72)
    {
        // 尝试创建日志文件；失败则回退到系统临时目录；再失败则禁用文件日志
        string primaryDir = logDirectory ?? DefaultLogDirectory;
        if (!TryCreateWriter(primaryDir, fileNamePrefix))
        {
            string fallbackDir = Path.Combine(Path.GetTempPath(), "MP_Logs");
            if (!TryCreateWriter(fallbackDir, fileNamePrefix))
            {
                // 文件日志完全不可用，写操作静默丢弃
                IsFileLoggingActive = false;
                FilePath = string.Empty;
                return;
            }
        }

        IsFileLoggingActive = true;
        WriteHeader();
        _flushTimer = new Timer(static state => ((LogFileWriter)state!).Flush(), this, FlushIntervalMs, FlushIntervalMs);
        CleanupOldLogs(Path.GetDirectoryName(FilePath)!, fileNamePrefix, maxAgeHours);
    }

    /// <summary>获取当前日志文件的完整路径（文件日志不可用时为 ""）。</summary>
    public string FilePath { get; private set; } = string.Empty;

    /// <summary>文件日志是否可用。</summary>
    public bool IsFileLoggingActive { get; private set; }

    /// <summary>
    /// 尝试在指定目录创建日志文件。失败时不抛异常。
    /// </summary>
    private bool TryCreateWriter(string dir, string prefix)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string unique = Guid.NewGuid().ToString("N")[..6];
            string filePath = Path.Combine(dir, $"{prefix}_{timestamp}_{unique}.log");
            _writer = new StreamWriter(filePath, append: false, encoding: System.Text.Encoding.UTF8)
            {
                // 不自动刷盘，由后台定时器批量刷盘以提高性能
                AutoFlush = false
            };
            FilePath = filePath;
            return true;
        }
        catch
        {
            // 目录创建失败或文件打开失败（如无写权限），不抛异常
            return false;
        }
    }

    /// <summary>
    /// 写入一条带时间戳的日志消息。
    /// 文件日志不可用时静默丢弃。
    /// </summary>
    public void Write(string message)
    {
        var w = _writer;
        if (w == null)
            return;

        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        lock (_sync)
        {
            if (!_disposed)
                w.WriteLine(line);
        }
    }

    /// <summary>写入日志并换行（同 Write）。</summary>
    public void WriteLine(string message) => Write(message);

    /// <summary>
    /// 创建一个 <see cref="Action{string}"/> 委托，可直接传给协议层作为 log 回调。
    /// </summary>
    public Action<string> CreateLogger() => Write;

    /// <summary>
    /// 创建组合日志委托：同时写入此文件 + 调用原有回调。
    /// </summary>
    public Action<string> CombineWith(Action<string>? existing)
    {
        if (existing == null)
            return Write;
        return msg =>
        {
            Write(msg);
            existing(msg);
        };
    }

    /// <summary>写入文件头（启动时间、OS 版本、文件路径）。</summary>
    private void WriteHeader()
    {
        var w = _writer;
        if (w == null)
            return;

        lock (_sync)
        {
            w.WriteLine("=".PadRight(60, '='));
            w.WriteLine("MP 固件刷写工具 - 日志");
            w.WriteLine($"启动时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            w.WriteLine($"日志文件: {FilePath}");
            w.WriteLine($"OS: {Environment.OSVersion}");
            w.WriteLine("=".PadRight(60, '='));
        }
    }

    /// <summary>强制刷盘（定时器或 Dispose 时调用）。</summary>
    public void Flush()
    {
        var w = _writer;
        if (w == null)
            return;

        lock (_sync)
        {
            if (!_disposed)
            {
                try { w.Flush(); }
                catch { /* 刷盘失败不影响主流程 */ }
            }
        }
    }

    /// <summary>清理指定目录下超过保留时长的旧日志文件（异步执行，不阻塞）。</summary>
    private static void CleanupOldLogs(string dir, string prefix, int maxAgeHours)
    {
        if (maxAgeHours <= 0)
            return;

        Task.Run(() =>
        {
            try
            {
                DateTime cutoff = DateTime.Now.AddHours(-maxAgeHours);
                foreach (string file in Directory.GetFiles(dir, $"{prefix}_*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                            File.Delete(file);
                    }
                    catch
                    {
                        // 单个文件清理失败不影响其他文件
                    }
                }
            }
            catch
            {
                // 清理失败不影响主流程
            }
        });
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            // 先写结束标记（必须在 _disposed 置位前写入，否则 Write 的 !_disposed 检查会阻止写入）
            try
            {
                var w = _writer;
                if (w != null)
                {
                    w.WriteLine("=".PadRight(60, '='));
                    w.WriteLine($"日志结束: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    w.WriteLine("=".PadRight(60, '='));
                }
            }
            catch
            {
                // 写结束标记失败不影响主流程
            }

            _disposed = true;

            try { _flushTimer?.Dispose(); }
            catch { /* 定时器销毁失败忽略 */ }

            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch
            {
                // 文件关闭失败不影响主流程
            }
        }
    }
}