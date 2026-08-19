using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ResBinManager.Core
{
    public static class Logger
    {
        private static readonly string LogDirectory = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static bool _isInitialized;
        private static readonly object _lockObj = new object();

        public static void Initialize()
        {
            lock (_lockObj)
            {
                if (_isInitialized) return;

                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }

                Trace.Listeners.Add(new FileTraceListener());
                _isInitialized = true;

                Info("Logger initialized");
            }
        }

        public static void Info(string message)
        {
            WriteLog("[INFO]", message);
        }

        public static void Warning(string message)
        {
            WriteLog("[WARNING]", message);
        }

        public static void Error(string message)
        {
            WriteLog("[ERROR]", message);
        }

        public static void Error(string message, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine(message);
            sb.AppendLine($"类型: {ex.GetType().FullName}");
            sb.AppendLine($"消息: {ex.Message}");
            sb.AppendLine($"堆栈: {ex.StackTrace}");

            if (ex.InnerException != null)
            {
                sb.AppendLine();
                sb.AppendLine("--- 内部异常 ---");
                sb.AppendLine($"类型: {ex.InnerException.GetType().FullName}");
                sb.AppendLine($"消息: {ex.InnerException.Message}");
                sb.AppendLine($"堆栈: {ex.InnerException.StackTrace}");
            }

            WriteLog("[ERROR]", sb.ToString());
        }

        public static void Debug(string message)
        {
            WriteLog("[DEBUG]", message);
        }

        private static void WriteLog(string level, string message)
        {
            try
            {
                var fileName = $"log_{DateTime.Now:yyyyMMdd}.txt";
                var filePath = Path.Combine(LogDirectory, fileName);
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {level} {message}\n";

                lock (_lockObj)
                {
                    File.AppendAllText(filePath, logEntry, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        private class FileTraceListener : TraceListener
        {
            public override void Write(string? message)
            {
                if (message == null)
                    return;
                WriteLog("[DEBUG]", message);
            }

            public override void WriteLine(string? message)
            {
                if (message == null)
                    return;
                WriteLog("[DEBUG]", message);
            }
        }
    }
}