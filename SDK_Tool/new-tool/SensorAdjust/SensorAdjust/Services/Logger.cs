using System.Diagnostics;
using System.IO;

namespace SensorAdjust.Services
{
    internal static class Logger
    {
        private static readonly string LogFilePath;
        private static readonly object LockObj = new();

        static Logger()
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SensorAdjust",
                "Logs");
            Directory.CreateDirectory(logDir);
            LogFilePath = Path.Combine(logDir, $"SensorAdjust_{DateTime.Now:yyyyMMdd}.log");
            Trace.AutoFlush = true;
        }

        public static void Info(string message) => WriteLog("INFO", message);
        public static void Warn(string message) => WriteLog("WARN", message);
        public static void Error(string message) => WriteLog("ERROR", message);

        public static void Info(string format, params object?[] args) => Info(string.Format(format, args));
        public static void Warn(string format, params object?[] args) => Warn(string.Format(format, args));
        public static void Error(string format, params object?[] args) => Error(string.Format(format, args));

        private static void WriteLog(string level, string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string logLine = $"[{timestamp}] [{level}] {message}";
            Trace.WriteLine(logLine);
            lock (LockObj)
            {
                try { File.AppendAllText(LogFilePath, logLine + Environment.NewLine); }
                catch { }
            }
        }
    }
}