using System.Diagnostics;
using System.IO;

namespace TimeUpdater.Services
{
    /// <summary>
    /// Logging utility for the TimeUpdater application.
    /// Outputs to both Debug/Trace listeners and a rolling log file.
    /// </summary>
    internal static class Logger
    {
        private static readonly string LogFilePath;
        private static readonly object LockObj = new();

        static Logger()
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TimeUpdater",
                "Logs");
            Directory.CreateDirectory(logDir);
            LogFilePath = Path.Combine(logDir, $"TimeUpdater_{DateTime.Now:yyyyMMdd}.log");

            // Set up trace listener for file output
            Trace.AutoFlush = true;
        }

        /// <summary>
        /// Writes an informational log message.
        /// </summary>
        public static void Info(string message)
        {
            WriteLog("INFO", message);
        }

        /// <summary>
        /// Writes a warning log message.
        /// </summary>
        public static void Warn(string message)
        {
            WriteLog("WARN", message);
        }

        /// <summary>
        /// Writes an error log message.
        /// </summary>
        public static void Error(string message)
        {
            WriteLog("ERROR", message);
        }

        /// <summary>
        /// Writes a formatted informational log message.
        /// </summary>
        public static void Info(string format, params object?[] args)
        {
            Info(string.Format(format, args));
        }

        /// <summary>
        /// Writes a formatted warning log message.
        /// </summary>
        public static void Warn(string format, params object?[] args)
        {
            Warn(string.Format(format, args));
        }

        /// <summary>
        /// Writes a formatted error log message.
        /// </summary>
        public static void Error(string format, params object?[] args)
        {
            Error(string.Format(format, args));
        }

        /// <summary>
        /// Writes a hex dump of a byte array for debugging purposes.
        /// </summary>
        public static void HexDump(string label, byte[] data, int maxBytes = 64)
        {
            int len = Math.Min(data.Length, maxBytes);
            var hex = new System.Text.StringBuilder(len * 3);
            for (int i = 0; i < len; i++)
            {
                hex.AppendFormat("{0:X2} ", data[i]);
            }
            if (data.Length > maxBytes)
                hex.Append("...");
            Info($"{label} ({data.Length} bytes): [{hex.ToString().Trim()}]");
        }

        private static void WriteLog(string level, string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string logLine = $"[{timestamp}] [{level}] {message}";

            // Output to Debug/Trace (visible in DebugView, VS Output Window)
            Trace.WriteLine(logLine);

            // Also write to file
            lock (LockObj)
            {
                try
                {
                    File.AppendAllText(LogFilePath, logLine + Environment.NewLine);
                }
                catch
                {
                    // Silently ignore file write failures
                }
            }
        }
    }
}