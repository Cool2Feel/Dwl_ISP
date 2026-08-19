using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace ThunderSE.Common
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
        Fatal = 4
    }

    /// <summary>
    /// 轻量级日志记录器
    /// 支持文件日志和Debug输出，线程安全，异步写入
    /// </summary>
    public static class Logger
    {
        #region 私有字段

        private static readonly object _lockObject = new object(); 
        private static readonly object _crashReportLock = new object();
        private static volatile bool _initialized = false;
        private static volatile bool _disposed = false;
        private static StreamWriter _logWriter;
        private static string _logDirectory;
        private static string _currentLogFile;
        private static LogLevel _minLogLevel = LogLevel.Debug;
        private static string _currentDate;

        #endregion

        #region 公共属性

        /// <summary>
        /// 最小日志级别
        /// </summary>
        public static LogLevel MinLogLevel
        {
            get => _minLogLevel;
            set => _minLogLevel = value;
        }

        /// <summary>
        /// 日志目录路径
        /// </summary>
        public static string LogDirectory => _logDirectory;

        /// <summary>
        /// 当前日志文件路径
        /// </summary>
        public static string CurrentLogFile => _currentLogFile;

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化日志系统
        /// </summary>
        /// <param name="logDirectory">日志目录（相对或绝对路径）</param>
        /// <param name="minLevel">最小日志级别</param>
        public static void Initialize(string logDirectory = "logs", LogLevel minLevel = LogLevel.Debug)
        {
            if (_initialized)
            {
                Info("Logger already initialized, reinitializing...");
                Cleanup();
            }

            try
            {
                // 解析日志目录路径
                if (!Path.IsPathRooted(logDirectory))
                {
                    // 相对路径，基于应用程序基目录
                    _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logDirectory);
                }
                else
                {
                    _logDirectory = logDirectory;
                }

                // 创建日志目录
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                _minLogLevel = minLevel;

                // 创建今天的日志文件
                RotateLogFile();
                // ✅ 关键修复：确保 _logWriter 不为 null 才标记成功
                if (_logWriter == null)
                {
                    throw new InvalidOperationException("Failed to create log writer");
                }
                _initialized = true;

                Info($"Logger initialized. Log directory: {_logDirectory}");
                Info($"Minimum log level: {minLevel}");
            }
            catch (Exception ex)
            {
                // 初始化失败，重置状态
                _initialized = false;
                _logWriter = null;
                // 初始化失败，尝试输出到Debug
                System.Diagnostics.Debug.WriteLine($"Logger initialization failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 轮转日志文件（按日期）
        /// </summary>
        private static void RotateLogFile()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");

            // 如果是同一天且已经初始化，不重复创建
            if (_currentDate == today && _logWriter != null)
                return;

            lock (_lockObject)
            {
                // 双重检查
                if (_currentDate == today && _logWriter != null)
                    return;

                // 关闭旧的日志文件
                if (_logWriter != null)
                {
                    try
                    {
                        _logWriter.Flush();
                        _logWriter.Dispose();
                    }
                    catch { /* 忽略关闭错误 */ }
                    _logWriter = null;
                }

                // 创建新的日志文件
                _currentDate = today;
                string fileName = $"ThunderSE_{today}.log";
                _currentLogFile = Path.Combine(_logDirectory, fileName);

                try
                {
                    // 使用UTF-8编码，追加模式
                    _logWriter = new StreamWriter(_currentLogFile, true, Encoding.UTF8);
                    _logWriter.AutoFlush = false; // 手动控制刷新，提高性能
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to create log file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public static void Cleanup()
        {
            if (_disposed)
                return;

            lock (_lockObject)
            {
                if (_disposed)
                    return;

                try
                {
                    if (_logWriter != null)
                    {
                        _logWriter.Flush();
                        _logWriter.Dispose();
                        _logWriter = null;
                    }
                }
                catch { /* 忽略清理错误 */ }

                _disposed = true;
            }
        }

        #endregion

        #region 公共日志方法

        /// <summary>
        /// 记录Debug级别日志
        /// </summary>
        public static void Debug(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            WriteLog(LogLevel.Debug, message, memberName, filePath);
        }

        /// <summary>
        /// 记录Info级别日志
        /// </summary>
        public static void Info(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            WriteLog(LogLevel.Info, message, memberName, filePath);
        }

        /// <summary>
        /// 记录Warn级别日志
        /// </summary>
        public static void Warn(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            WriteLog(LogLevel.Warn, message, memberName, filePath);
        }

        /// <summary>
        /// 记录Error级别日志
        /// </summary>
        public static void Error(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            WriteLog(LogLevel.Error, message, memberName, filePath);
        }

        /// <summary>
        /// 记录Error级别日志（带异常）
        /// </summary>
        public static void Error(string message, Exception ex, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            string fullMessage = $"{message}{Environment.NewLine}Exception: {ex}";
            WriteLog(LogLevel.Error, fullMessage, memberName, filePath);
        }

        /// <summary>
        /// 记录Fatal级别日志
        /// </summary>
        public static void Fatal(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            WriteLog(LogLevel.Fatal, message, memberName, filePath);
        }

        /// <summary>
        /// 记录Fatal级别日志（带异常）
        /// </summary>
        public static void Fatal(string message, Exception ex, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            string fullMessage = $"{message}{Environment.NewLine}Exception: {ex}";
            WriteLog(LogLevel.Fatal, fullMessage, memberName, filePath);
        }

        /// <summary>
        /// 记录崩溃详情（包含完整的系统信息和堆栈跟踪）
        /// 专用于闪退场景，记录尽可能多的诊断信息
        /// </summary>
        /// <param name="title">崩溃标题</param>
        /// <param name="ex">异常对象</param>
        /// <param name="additionalInfo">附加信息</param>
        public static void LogCrashReport(string title, Exception ex, string additionalInfo = null)
        {
            try
            {
                // 确保日志已初始化
                if (!_initialized)
                {
                    Initialize("logs", LogLevel.Debug);
                }

                // 写入崩溃报告分隔符
                string separator = new string('=', 80);
                lock (_crashReportLock)
                {
                    if (_logWriter != null && !_disposed)
                    {
                        _logWriter.WriteLine(separator);
                        _logWriter.WriteLine($"[CRASH REPORT] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                        _logWriter.WriteLine(separator);
                        _logWriter.Flush();
                    }
                }

                // 记录系统信息
                Fatal($"[CRASH] {title}");
                Fatal($"[SYSTEM] OS: {Environment.OSVersion}");
                Fatal($"[SYSTEM] .NET Version: {Environment.Version}");
                Fatal($"[SYSTEM] 64-bit OS: {Environment.Is64BitOperatingSystem}");
                Fatal($"[SYSTEM] 64-bit Process: {Environment.Is64BitProcess}");
                Fatal($"[SYSTEM] Processor Count: {Environment.ProcessorCount}");
                Fatal($"[SYSTEM] Working Set: {Environment.WorkingSet / 1024 / 1024} MB");
                Fatal($"[SYSTEM] Machine Name: {Environment.MachineName}");
                Fatal($"[SYSTEM] User: {Environment.UserName}");
                Fatal($"[SYSTEM] Current Directory: {Environment.CurrentDirectory}");
                Fatal($"[SYSTEM] Base Directory: {AppDomain.CurrentDomain.BaseDirectory}");

                // 记录异常详情
                Fatal($"[EXCEPTION] Type: {ex?.GetType().FullName}");
                Fatal($"[EXCEPTION] Message: {ex?.Message}");
                Fatal($"[EXCEPTION] Source: {ex?.Source}");
                Fatal($"[EXCEPTION] TargetSite: {ex?.TargetSite}");
                Fatal($"[EXCEPTION] HResult: 0x{(ex?.HResult ?? 0):X8}");
                
                // 记录完整堆栈
                Fatal($"[STACK TRACE]\n{ex?.StackTrace}");

                // 记录内部异常链
                Exception innerEx = ex?.InnerException;
                int innerLevel = 1;
                while (innerEx != null)
                {
                    Fatal($"[INNER EXCEPTION {innerLevel}] Type: {innerEx.GetType().FullName}");
                    Fatal($"[INNER EXCEPTION {innerLevel}] Message: {innerEx.Message}");
                    Fatal($"[INNER EXCEPTION {innerLevel}] Stack Trace:\n{innerEx.StackTrace}");
                    innerEx = innerEx.InnerException;
                    innerLevel++;
                }

                // 记录附加信息
                if (!string.IsNullOrEmpty(additionalInfo))
                {
                    Fatal($"[ADDITIONAL INFO]\n{additionalInfo}");
                }

                // 记录加载的程序集
                Fatal("[LOADED ASSEMBLIES]");
                try
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Fatal($"  - {assembly.GetName().Name} v{assembly.GetName().Version} ({assembly.Location})");
                    }
                }
                catch (Exception asmEx)
                {
                    Warn($"Failed to enumerate assemblies: {asmEx.Message}");
                }

                // 写入结束分隔符
                lock (_crashReportLock)
                {
                    if (_logWriter != null && !_disposed)
                    {
                        _logWriter.WriteLine(new string('=', 80));
                        _logWriter.WriteLine();
                        _logWriter.Flush();
                    }
                }
            }
            catch (Exception logEx)
            {
                // 如果日志记录本身失败，输出到Debug
                System.Diagnostics.Debug.WriteLine($"CRASH REPORT FAILED: {logEx.Message}");
            }
        }

        #endregion

        #region 私有日志写入

        /// <summary>
        /// 写入日志（核心方法）
        /// </summary>
        private static void WriteLog(LogLevel level, string message, string memberName, string filePath)
        {
            // 检查日志级别
            if (level < _minLogLevel)
                return;

            // 确保已初始化
            if (!_initialized)
            {
                // 尝试自动初始化
                try
                {
                    Initialize();
                }
                catch
                {
                    // 自动初始化失败，仅输出到Debug
                    System.Diagnostics.Debug.WriteLine($"[LOG] {level}: {message}");
                    return;
                }
            }

            // 检查是否需要轮转日志文件
            RotateLogFile();

            // 构建日志消息
            string logEntry = FormatLogEntry(level, message, memberName, filePath);

            // 异步写入文件
            WriteToFileAsync(logEntry);

            // 同时输出到Debug窗口（便于调试）
#if DEBUG
            System.Diagnostics.Debug.WriteLine(logEntry);
#endif
        }

        /// <summary>
        /// 格式化日志条目
        /// 格式：[时间戳] [级别] [线程ID] [类名.方法] - 消息
        /// </summary>
        private static string FormatLogEntry(LogLevel level, string message, string memberName, string filePath)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelStr = level.ToString().ToUpper();
            int threadId = Thread.CurrentThread.ManagedThreadId;

            // 提取类名和方法名
            string className = ExtractClassName(filePath, memberName);

            return $"[{timestamp}] [{levelStr,-5}] [T{threadId:00}] [{className}] - {message}";
        }

        /// <summary>
        /// 从文件路径提取类名
        /// </summary>
        private static string ExtractClassName(string filePath, string memberName)
        {
            if (string.IsNullOrEmpty(filePath))
                return memberName ?? "Unknown";

            try
            {
                // 从完整路径提取文件名（不含扩展名）
                string fileName = Path.GetFileNameWithoutExtension(filePath);

                // 如果有方法名，拼接
                if (!string.IsNullOrEmpty(memberName))
                {
                    return $"{fileName}.{memberName}";
                }

                return fileName;
            }
            catch
            {
                return memberName ?? "Unknown";
            }
        }

        /// <summary>
        /// 异步写入文件（使用Task避免阻塞调用线程）
        /// </summary>
        private static async void WriteToFileAsync(string logEntry)
        {
            try
            {
                lock (_lockObject)
                {
                    if (_logWriter != null && !_disposed)
                    {
                        _logWriter.WriteLine(logEntry);
                        _logWriter.Flush(); // 立即刷新，确保日志写入
                    }
                }
            }
            catch (Exception ex)
            {
                // 写入失败，输出到Debug
                System.Diagnostics.Debug.WriteLine($"Failed to write log: {ex.Message}");
            }
        }

        #endregion

        #region 日志清理工具

        /// <summary>
        /// 清理指定天数之前的日志文件
        /// </summary>
        /// <param name="daysToKeep">保留天数</param>
        /// <returns>删除的文件数</returns>
        public static int CleanOldLogs(int daysToKeep = 30)
        {
            if (string.IsNullOrEmpty(_logDirectory) || !Directory.Exists(_logDirectory))
                return 0;

            try
            {
                DateTime cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                int deletedCount = 0;

                var logFiles = Directory.GetFiles(_logDirectory, "ThunderSE_*.log");

                foreach (var file in logFiles)
                {
                    try
                    {
                        // 从文件名提取日期
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        string datePart = fileName.Replace("ThunderSE_", "");

                        if (DateTime.TryParse(datePart, out DateTime fileDate))
                        {
                            if (fileDate < cutoffDate)
                            {
                                File.Delete(file);
                                deletedCount++;
                                Info($"Deleted old log file: {fileName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Warn($"Failed to delete old log file {file}: {ex.Message}");
                    }
                }

                return deletedCount;
            }
            catch (Exception ex)
            {
                Error($"Failed to clean old logs: {ex.Message}");
                return 0;
            }
        }

        #endregion
    }
}
