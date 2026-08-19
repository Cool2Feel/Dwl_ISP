using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ResBinManager.Core;
using ResBinManager.Models;

namespace ResBinManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

#if NET6_0_OR_GREATER
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif

            // 初始化日志系统
            Logger.Initialize();

            // 注册全局异常处理器
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // 初始化配置项注册表
            ConfigItemRegistry.Initialize();

            Logger.Info("应用程序启动");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info($"应用程序退出，退出码: {e.ApplicationExitCode}");
            base.OnExit(e);
        }

        /// <summary>
        /// 处理 UI 线程未捕获的异常
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var ex = e.Exception;
            
            Logger.Error("UI线程异常", ex);
            
            var result = MessageBox.Show(
                $"发生了一个错误：\n\n{ex.Message}\n\n" +
                $"详细信息已保存到 logs 目录\n\n" +
                "是否继续运行？（否将退出程序）",
                "应用程序错误",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (result == MessageBoxResult.No)
            {
                Shutdown(1);
            }

            e.Handled = true;
        }

        /// <summary>
        /// 处理非 UI 线程未捕获的异常
        /// </summary>
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.Error("非UI线程异常", ex);

                MessageBox.Show(
                    $"发生了一个严重错误：\n\n{ex.Message}\n\n" +
                    $"详细信息已保存到 logs 目录\n\n" +
                    "程序即将退出。",
                    "应用程序严重错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 处理异步任务未观察到的异常
        /// </summary>
        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            var ex = e.Exception;
            
            Logger.Error("异步任务异常", ex);
            
            // 异步任务异常通常不需要显示对话框，只记录日志
            e.SetObserved();
        }
    }
}
