using System.Windows;
using System.Windows.Threading;
using UpgradeTool.Core.Utilities;

namespace UpgradeTool.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private LogFileWriter? _crashLog;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常兜底：UI 线程 / 后台任务 / 应用域的任何未处理异常都先记录到日志，
        // 避免产线烧录场景下进程静默终止而无任何排查痕迹。
        _crashLog = new LogFileWriter(fileNamePrefix: "crash");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("UI 线程未处理异常", e.Exception);
        MessageBox.Show(
            $"发生未处理的界面异常：\n{e.Exception.Message}\n\n详细信息已写入日志文件。",
            "UpgradeTool 错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        // 标记已处理，阻止 WPF 默认崩溃对话框并保留应用继续运行（烧录会话不受影响）
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("后台任务未观察异常", e.Exception);
        // 标记已观察，避免未观察任务异常触发进程终止
        e.SetObserved();
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogCrash("应用程序域未处理异常（进程即将退出）", ex);
    }

    private void LogCrash(string title, Exception? ex)
    {
        try
        {
            _crashLog?.Write($"[{title}] {(ex?.ToString() ?? "未知异常")}");
            _crashLog?.Flush();
        }
        catch
        {
            // 日志写入本身失败时静默忽略，避免掩盖原始异常
        }
    }
}
