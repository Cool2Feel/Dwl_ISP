using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FontBuilder.Core;

namespace FontBuilder
{
    /// <summary>
    /// App 入口：注册编码提供者 + 全局异常处理
    /// 支持两种模式:
    ///   GUI 模式 (无参数): 打开主窗口
    ///   CLI 模式 (--build <font.ini>): 无头构建后退出
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

#if NET6_0_OR_GREATER
            // 注册 CodePages 编码提供者（GB2312/Shift-JIS 等）
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif

            // CLI 模式: --build <font.ini>
            if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--build", StringComparison.OrdinalIgnoreCase))
            {
                RunCliBuild(e.Args[1]);
                Shutdown(0);
                return;
            }

            // GUI 模式
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            var window = new Views.MainWindow();
            MainWindow = window;
            window.Show();
        }

        /// <summary>
        /// 无头构建模式：直接执行 FontBuildOrchestrator 并输出日志
        /// </summary>
        private void RunCliBuild(string iniPath)
        {
            // WinExe 默认无控制台，附加到父进程控制台以支持 CLI 输出
            bool hasConsole = false;
            try { hasConsole = AttachConsole(ATTACH_PARENT_PROCESS); } catch { }

            // 同时写入日志文件（与输出同目录），确保即使控制台不可用也能获取日志
            var logPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(iniPath)),
                "FontBuilder.log");
            var logLines = new System.Collections.Generic.List<string>();

            void Emit(string line)
            {
                logLines.Add(line);
                if (hasConsole)
                {
                    try { Console.WriteLine(line); } catch { }
                }
            }

            try
            {
                if (hasConsole)
                {
                    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
                }
                Emit($"FontBuilder CLI: {iniPath}");

                var orchestrator = new FontBuildOrchestrator
                {
                    Progress = new Progress<(int done, int total, string stage)>(p =>
                    {
                        Emit($"  [{p.done}/{p.total}] {p.stage}");
                    })
                };

                var result = orchestrator.Build(iniPath);

                foreach (var line in result.Log)
                    Emit(line);

                if (result.Success)
                {
                    Emit($"=== 构建成功 ===");
                    Emit($"  字符数: {result.CharCount}");
                    Emit($"  字符串数: {result.StringCount}");
                    Emit($"  语言数: {result.LanguageCount}");
                    Emit($"  耗时: {result.ElapsedMilliseconds} ms");
                }
                else if (result.Cancelled)
                {
                    Emit("=== 构建已取消 ===");
                }
                else
                {
                    Emit($"=== 构建失败: {result.Error?.Message} ===");
                }
            }
            catch (Exception ex)
            {
                Emit($"[致命错误] {ex}");
                if (hasConsole) { try { Console.Error.WriteLine($"[致命错误] {ex}"); } catch { } }
            }
            finally
            {
                try
                {
                    System.IO.File.WriteAllLines(logPath, logLines, Encoding.UTF8);
                    if (hasConsole)
                    {
                        try { Console.WriteLine($"日志已写入: {logPath}"); } catch { }
                    }
                }
                catch { }
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var ex = e.Exception;
            MessageBox.Show(
                $"发生错误：\n\n{ex.Message}\n\n{ex.StackTrace}",
                "应用程序错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"严重错误：\n\n{ex.Message}",
                    "应用程序严重错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
        }

        #region Console Attach (P/Invoke)

        private const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        #endregion
    }
}
