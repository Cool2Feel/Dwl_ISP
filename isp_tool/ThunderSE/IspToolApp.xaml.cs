using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using ThunderSE.Common;
using ThunderSE.Uvc;

namespace ThunderSE
{
    /// <summary>
    /// Interaction logic for IspToolApp.xaml
    /// </summary>
    public partial class IspToolApp : Application
    {
        private bool _isDevelopMode = true;
        private System.Threading.Mutex _hasInstanceStartedMutex;

        private void OnStartup(object sender, StartupEventArgs e)
        {

            try
            {
                // 初始化日志系统
                Logger.Initialize("logs", LogLevel.Debug);
                Logger.Info("========== ThunderSE Application Starting ==========");
                Logger.Info($"Application version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
                Logger.Info($".NET Framework version: {Environment.Version}");
                Logger.Info($"OS version: {Environment.OSVersion}");
                Logger.Info($"Machine name: {Environment.MachineName}");
                // 注册全局异常处理（必须在最开始）
                RegisterGlobalExceptionHandlers();
            }
            catch (Exception ex)
            {
                // 日志初始化失败，不影响正常启动
                System.Diagnostics.Debug.WriteLine($"Logger init failed: {ex.Message}");
            }

            try
            {
                _hasInstanceStartedMutex = new System.Threading.Mutex(true, "ThunderSE");
                if (!_hasInstanceStartedMutex.WaitOne(0, false))
                {
                    Logger.Warn("Application already running, shutting down.");
                    MessageBox.Show("程序已经运行！", "提示");
                    this.Shutdown();
                    return;
                }

                Logger.Info("Application instance lock acquired.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to acquire application instance lock.", ex);
            }

#if DEBUG
            foreach (var arg in e.Args)
            {
                switch (arg)
                {
                    case "/Develop":
                        IsDevelopMode = true;
                        Logger.Info("Develop mode enabled via command line.");
                        break;

                    default:
                        break;
                }
            }
#endif

            try
            {
                if (IsDevelopMode)
                {
                    StartupUri = new Uri("Ui\\MainWindow\\MainFrameForDevelop.xaml", UriKind.Relative);
                    Logger.Info("Loading MainFrameForDevelop UI.");
                }
                else
                {
                    StartupUri = new Uri("Ui\\MainWindow\\UserMode\\MainFrameForUser.xaml", UriKind.Relative);
                    Logger.Info("Loading MainFrameForUser UI.");
                }
            }
            catch (Exception ex)
            {
                Logger.Fatal("Failed to set startup URI.", ex);
                throw;
            }
        }

        public bool IsDevelopMode
        {
            get { return _isDevelopMode; }
            set { _isDevelopMode = value; }
        }


        private void OnUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // 使用详细的崩溃报告方法
            //Logger.LogCrashReport(
            //    "Unhandled Dispatcher Exception (UI Thread)",
            //    e.Exception,
            //    $"Dispatcher: {System.Windows.Threading.Dispatcher.CurrentDispatcher.Thread.ManagedThreadId}{Environment.NewLine}" +
            //    $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}{Environment.NewLine}" +
            //    $"e.Handled (before): {e.Handled}"
            //);
            // 记录当前内存状态
            LogMemoryStatus("异常发生时");

            bool isOutOfMemory = e.Exception is OutOfMemoryException;

            Logger.Fatal("===== Unhandled Exception (UI Thread) =====");
            Logger.Error($"异常类型: {e.Exception.GetType().Name}");
            Logger.Error($"异常消息: {e.Exception.Message}");
            Logger.Error($"堆栈跟踪:\n{e.Exception.StackTrace}");

            if (isOutOfMemory)
            {
                Logger.Fatal("⚠️ 检测到内存不足异常！可能的原因：");
                Logger.Fatal("1. UVC视频流未正确释放WriteableBitmap");
                Logger.Fatal("2. RAW图像缓冲区未释放");
                Logger.Fatal("3. ObservableCollection持续增长未清理");
                Logger.Fatal("4. 非托管内存（Marshal.AllocHGlobal）未释放");

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                LogMemoryStatus("GC后");
            }

#if !DEBUG
            //MessageBox.Show($"程序遇到未处理的错误：\n{e.Exception.Message}\n\n详细信息已写入日志文件。",
            //    "未处理的异常", MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Fatal($"程序遇到未处理的错误：\n{e.Exception.Message}\n\n详细信息已写入日志文件。");
            e.Handled = true; // 阻止程序崩溃
#else
            // Debug模式下让异常显示，便于调试
            Logger.Error($"程序遇到未处理的错误：\n{e.Exception.Message}\n\n详细信息已写入日志文件。");
            e.Handled = false;
#endif
        }

        /// <summary>
        /// 记录当前内存使用状态
        /// </summary>
        private void LogMemoryStatus(string context)
        {
            try
            {
                long totalMemory = GC.GetTotalMemory(false);
                Process currentProcess = Process.GetCurrentProcess();

                Logger.Info($"[内存监控] {context}:");
                Logger.Info($"  - GC托管内存: {totalMemory / 1024 / 1024:F2} MB");
                Logger.Info($"  - 进程工作集: {currentProcess.WorkingSet64 / 1024 / 1024:F2} MB");
                Logger.Info($"  - 进程私有内存: {currentProcess.PrivateMemorySize64 / 1024 / 1024:F2} MB");
                Logger.Info($"  - GC代数0: {GC.CollectionCount(0)}");
                Logger.Info($"  - GC代数1: {GC.CollectionCount(1)}");
                Logger.Info($"  - GC代数2: {GC.CollectionCount(2)}");

                currentProcess.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error($"记录内存状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 注册全局异常处理器，捕获所有未处理的异常
        /// </summary>
        private void RegisterGlobalExceptionHandlers()
        {
            // 1. UI线程未处理的异常（已在XAML中绑定）
            this.DispatcherUnhandledException += OnUnhandledException;

            // 2. 非UI线程未处理的异常（AppDomain级别）
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Exception ex = e.ExceptionObject as Exception;

                if (ex != null)
                {
                    // 使用详细的崩溃报告方法
                    //Logger.LogCrashReport(
                    //    "Unhandled Exception (Non-UI Thread) - Application Terminating",
                    //    ex,
                    //    $"IsTerminating: {e.IsTerminating}{Environment.NewLine}" +
                    //    $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}"
                    //);
                    Logger.Fatal("===== Unhandled Exception (Non-UI Thread) =====");
                    Logger.Error(ex.ToString());
                }
                else
                {
                    Logger.Fatal("===== Unhandled Exception (Non-UI Thread) =====");
                    Logger.Fatal($"ExceptionObject: {e.ExceptionObject}");
                    Logger.Fatal($"IsTerminating: {e.IsTerminating}");
                }

                Logger.Fatal("Application will likely crash after this.");

                // 注意：这里无法阻止程序崩溃，但可以记录崩溃前的最后日志
                if (e.IsTerminating)
                {
                    // 程序即将终止，显示提示
                    try
                    {
                        //MessageBox.Show(
                        //    "程序遇到严重错误，即将关闭。\n\n详细信息已写入日志文件，请将日志文件发送给开发人员以帮助修复问题。",
                        //    "致命错误",
                        //    MessageBoxButton.OK,
                        //    MessageBoxImage.Error);
                        Logger.Error("程序遇到严重错误，即将关闭。");
                    }
                    catch { /* 如果连MessageBox都失败，忽略 */ }
                }
            };

            // 3. Task未观察到的异常（.NET 4.0+）
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Logger.Fatal("===== Unobserved Task Exception =====");
                Logger.Fatal($"Exception type: {e.Exception.GetType().FullName}");
                Logger.Fatal($"Message: {e.Exception.Message}");
                Logger.Fatal($"Stack trace:\n{e.Exception.StackTrace}");

                // 标记为已观察，防止进程崩溃
                e.SetObserved();
            };

            // 4. 进程退出事件
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                Logger.Info("===== Process Exit Event =====");
                Logger.Info($"Exit code: {Environment.ExitCode}");
                Logger.Info("Application process is exiting.");

                // 确保日志刷新到磁盘
                try
                {
                    Logger.Cleanup();
                }
                catch { /* 忽略最后的清理错误 */ }
            };

            Logger.Info("Global exception handlers registered.");
        }

        private void OnExit(object sender, ExitEventArgs e)
        {
            try
            {
                Logger.Info("Application exiting, cleaning up...");
                UvcReceiver.Instance.Disconnect();
                Logger.Info("UVC connection disconnected.");
            }
            catch (Exception ex)
            {
                Logger.Error("Error during application exit.", ex);
            }
            finally
            {
                Logger.Cleanup();
                Environment.Exit(0); // 强制结束进程，忽略所有后台线程
                base.OnExit(e);
            }
        }

    }
}
