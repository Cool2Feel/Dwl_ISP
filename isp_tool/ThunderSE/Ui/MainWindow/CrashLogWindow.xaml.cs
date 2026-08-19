using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using ThunderSE.Common;

namespace ThunderSE.Ui
{
    /// <summary>
    /// 崩溃日志查看器窗口
    /// </summary>
    public partial class CrashLogWindow : Window
    {
        public CrashLogWindow()
        {
            InitializeComponent();
            LoadLatestCrashLog();
        }

        /// <summary>
        /// 加载最新的崩溃日志
        /// </summary>
        private void LoadLatestCrashLog()
        {
            try
            {
                string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                
                if (!Directory.Exists(logDirectory))
                {
                    txtLogContent.Text = "未找到日志目录。";
                    return;
                }

                // 查找所有日志文件
                var logFiles = Directory.GetFiles(logDirectory, "ThunderSE_*.log");
                
                if (logFiles.Length == 0)
                {
                    txtLogContent.Text = "未找到任何日志文件。";
                    return;
                }

                // 按修改时间排序，获取最新的
                var latestLog = new FileInfo(logFiles[0]);
                foreach (var file in logFiles)
                {
                    var fi = new FileInfo(file);
                    if (fi.LastWriteTime > latestLog.LastWriteTime)
                    {
                        latestLog = fi;
                    }
                }

                // 读取日志内容
                string content = File.ReadAllText(latestLog.FullName);
                
                // 查找崩溃报告部分
                if (content.Contains("[CRASH REPORT]"))
                {
                    // 只提取崩溃报告部分
                    int startIndex = content.LastIndexOf("[CRASH REPORT]");
                    if (startIndex >= 0)
                    {
                        // 找到前面的分隔符
                        int sectionStart = content.LastIndexOf(new string('=', 80), startIndex);
                        if (sectionStart < 0)
                            sectionStart = startIndex;

                        // 显示崩溃报告部分
                        txtLogContent.Text = content.Substring(sectionStart);
                    }
                    else
                    {
                        txtLogContent.Text = content;
                    }
                }
                else
                {
                    // 没有崩溃报告，显示整个日志
                    txtLogContent.Text = content;
                }

                // 显示文件名
                Title = $"崩溃日志查看器 - {latestLog.Name}";
            }
            catch (Exception ex)
            {
                txtLogContent.Text = $"读取日志文件时出错：\n\n{ex.Message}\n\n{ex.StackTrace}";
            }
        }

        /// <summary>
        /// 复制日志到剪贴板
        /// </summary>
        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(txtLogContent.Text);
                MessageBox.Show("日志已复制到剪贴板！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制失败：\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 打开日志目录
        /// </summary>
        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                
                if (Directory.Exists(logDirectory))
                {
                    Process.Start("explorer.exe", logDirectory);
                }
                else
                {
                    MessageBox.Show("日志目录不存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开目录失败：\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 关闭窗口
        /// </summary>
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
