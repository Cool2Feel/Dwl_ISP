using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ResBinManager.Models;

namespace ResBinManager.Core
{
    /// <summary>
    /// 固件打包进度事件参数
    /// </summary>
    public class BuildProgressEventArgs : EventArgs
    {
        public string Message { get; set; } = string.Empty;
        public int Progress { get; set; } // 0-100
        public bool IsError { get; set; }
    }

    /// <summary>
    /// 固件打包结果
    /// </summary>
    public class BuildResult
    {
        public bool Success { get; set; }
        public string OutputFile { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// 固件打包引擎 - 调用 MakeSPIBin.exe
    /// </summary>
    public class FirmwareBuilder
    {
        private readonly FirmwareBuildConfig _config;
        private byte[]? _modifiedResBinData;  // 存储修改后的 RES.BIN 数据
        private string? _tempResBinPath;      // 临时文件路径
        
        public event EventHandler<BuildProgressEventArgs>? ProgressChanged;

        public FirmwareBuilder(FirmwareBuildConfig config, byte[]? modifiedResBinData = null)
        {
            _config = config;
            _modifiedResBinData = modifiedResBinData;
        }

        /// <summary>
        /// 异步执行固件打包
        /// </summary>
        public BuildResult Build()
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new BuildResult();

            try
            {
                if (!ValidateConfig(out string validationError))
                {
                    result.Success = false;
                    result.ErrorMessage = validationError;
                    return result;
                }

                ReportProgress("开始固件打包流程...", 0);
                
                System.Diagnostics.Debug.WriteLine($"[Build] MakeSpiBinPath: {_config.MakeSpiBinPath}");
                System.Diagnostics.Debug.WriteLine($"[Build] OutputPath: {_config.OutputPath}");

                if (_config.AutoBackup)
                {
                    ReportProgress("备份原文件...", 10);
                    BackupFiles();
                }

                ReportProgress("准备输出目录...", 20);
                PrepareOutputDirectory();
                
                ReportProgress("检查 MakeSPIBin.exe...", 22);
                EnsureMakeSpiBinInOutput();

                ReportProgress("复制资源文件...", 30);
                var resBinFileName = CopyResBinToOutput();
                
                string inputFileName;
                if (_config.InputType == Models.FirmwareInputType.Elf)
                {
                    ReportProgress("复制 ELF 文件...", 35);
                    inputFileName = CopyElfToOutput();
                }
                else
                {
                    ReportProgress("复制 BIN 文件...", 35);
                    inputFileName = CopyBinToOutput();
                }

                ReportProgress($"调用 MakeSPIBin.exe 进行合并...", 50);
                bool buildSuccess;
#if !NET40
                buildSuccess = Task.Run(() => RunMakeSpiBin(inputFileName, resBinFileName)).Result;
#else
                buildSuccess = RunMakeSpiBin(inputFileName, resBinFileName);
#endif

                if (!buildSuccess)
                {
                    result.Success = false;
                    result.ErrorMessage = "MakeSPIBin.exe 执行失败，请检查输出日志";
                    return result;
                }

                ReportProgress("打包完成！", 100);

                result.Success = true;
                result.OutputFile = Path.Combine(_config.OutputPath, "DestBin.bin");
                result.Duration = stopwatch.Elapsed;

                // 自动打开输出文件夹
                if (_config.AutoOpenOutputFolder && Directory.Exists(_config.OutputPath))
                {
                    Process.Start("explorer.exe", _config.OutputPath);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"打包过程发生异常: {ex.Message}";
                ReportProgress($"错误: {ex.Message}", 0, true);
            }
            finally
            {
                stopwatch.Stop();
            }

            return result;
        }

        /// <summary>
        /// 清理临时文件
        /// </summary>
        public void Cleanup()
        {
            try
            {
                if (!string.IsNullOrEmpty(_tempResBinPath) && File.Exists(_tempResBinPath))
                {
                    File.Delete(_tempResBinPath);
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }

        /// <summary>
        /// 验证配置有效性
        /// </summary>
        private bool ValidateConfig(out string errorMessage)
        {
            errorMessage = string.Empty;

            // 根据输入类型验证相应的文件
            if (_config.InputType == Models.FirmwareInputType.Elf)
            {
                if (string.IsNullOrEmpty(_config.ElfPath))
                {
                    errorMessage = "ELF 文件路径未设置";
                    return false;
                }

                if (!File.Exists(_config.ElfPath))
                {
                    errorMessage = $"ELF 文件不存在: {_config.ElfPath}";
                    return false;
                }
            }
            else // Bin
            {
                if (string.IsNullOrEmpty(_config.BinPath))
                {
                    errorMessage = "BIN 文件路径未设置";
                    return false;
                }

                if (!File.Exists(_config.BinPath))
                {
                    errorMessage = $"BIN 文件不存在: {_config.BinPath}";
                    return false;
                }
            }

            if (string.IsNullOrEmpty(_config.ResBinPath))
            {
                errorMessage = "RES.BIN 文件路径未设置";
                return false;
            }

            if (!File.Exists(_config.ResBinPath))
            {
                errorMessage = $"RES.BIN 文件不存在: {_config.ResBinPath}";
                return false;
            }

            if (string.IsNullOrEmpty(_config.MakeSpiBinPath))
            {
                errorMessage = "MakeSPIBin.exe 路径未设置";
                return false;
            }

            if (!File.Exists(_config.MakeSpiBinPath))
            {
                errorMessage = $"MakeSPIBin.exe 不存在: {_config.MakeSpiBinPath}";
                return false;
            }

            if (string.IsNullOrEmpty(_config.OutputPath))
            {
                errorMessage = "输出目录未设置";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 备份原文件
        /// </summary>
        private void BackupFiles()
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                // 备份 DestBin.bin（如果存在）
                var destBinPath = Path.Combine(_config.OutputPath, "DestBin.bin");
                if (File.Exists(destBinPath))
                {
                    var backupPath = $"{destBinPath}.backup.{timestamp}";
                    File.Copy(destBinPath, backupPath, true);
                    ReportProgress($"已备份 DestBin.bin -> {Path.GetFileName(backupPath)}", 15);

                    // 清理旧备份，保留最近 3 个
                    CleanupOldBackups(destBinPath);
                }
            }
            catch (Exception ex)
            {
                ReportProgress($"备份警告: {ex.Message}", 15, true);
            }
        }

        private static void CleanupOldBackups(string filePath)
        {
            try
            {
                var dir = Path.GetDirectoryName(filePath)!;
                var baseName = Path.GetFileName(filePath);
                var backups = Directory.GetFiles(dir, $"{baseName}.backup.*")
                    .OrderByDescending(f => f)
                    .ToList();
                foreach (var old in backups.Skip(3))
                    File.Delete(old);
            }
            catch { /* cleanup failure is non-fatal */ }
        }

        /// <summary>
        /// 准备输出目录
        /// </summary>
        private void PrepareOutputDirectory()
        {
            if (!Directory.Exists(_config.OutputPath))
            {
                Directory.CreateDirectory(_config.OutputPath);
                ReportProgress($"创建输出目录: {_config.OutputPath}", 25);
            }
        }

        /// <summary>
        /// 确保 MakeSPIBin.exe 在输出目录中
        /// </summary>
        private void EnsureMakeSpiBinInOutput()
        {
            var makeSpiBinInOutput = Path.Combine(_config.OutputPath, "MakeSPIBin.exe");
            
            // 如果已经存在，验证是否有效
            if (File.Exists(makeSpiBinInOutput))
            {
                var fileInfo = new FileInfo(makeSpiBinInOutput);
                ReportProgress($"MakeSPIBin.exe 已就绪 ({fileInfo.Length / 1024} KB)", 25);
                return;
            }
            
            // 否则从配置路径复制
            if (!string.IsNullOrEmpty(_config.MakeSpiBinPath) && File.Exists(_config.MakeSpiBinPath))
            {
                try
                {
                    ReportProgress($"正在复制 MakeSPIBin.exe...", 23);
                    File.Copy(_config.MakeSpiBinPath, makeSpiBinInOutput, true);
                    
                    // 验证复制成功
                    if (File.Exists(makeSpiBinInOutput))
                    {
                        var fileInfo = new FileInfo(makeSpiBinInOutput);
                        ReportProgress($"已复制 MakeSPIBin.exe 到输出目录 ({fileInfo.Length / 1024} KB)", 25);
                    }
                    else
                    {
                        throw new IOException("复制后文件不存在");
                    }
                }
                catch (Exception ex)
                {
                    ReportProgress($"复制 MakeSPIBin.exe 失败: {ex.Message}", 25, true);
                    throw;
                }
            }
            else
            {
                ReportProgress($"错误: MakeSPIBin.exe 未找到!", 25, true);
                ReportProgress($"配置路径: {_config.MakeSpiBinPath ?? "(空)"}", 25, true);
                throw new FileNotFoundException("MakeSPIBin.exe not found", _config.MakeSpiBinPath);
            }
        }

        /// <summary>
        /// 复制 RES.BIN 到输出目录并重命名为 Res.bin
        /// 如果有修改后的数据，优先使用修改后的数据
        /// </summary>
        private string CopyResBinToOutput()
        {
            var resBinFileName = "Res.bin";
            var destPath = Path.Combine(_config.OutputPath, resBinFileName);
            
            try
            {
                if (_modifiedResBinData != null && _modifiedResBinData.Length > 0)
                {
                    // 使用内存中的修改后数据
                    File.WriteAllBytes(destPath, _modifiedResBinData);
                    ReportProgress($"已写入修改后的 RES.BIN ({_modifiedResBinData.Length / 1024} KB)", 40);
                }
                else
                {
                    // 使用磁盘上的文件
                    File.Copy(_config.ResBinPath, destPath, true);
                    ReportProgress($"已复制 RES.BIN 到输出目录", 40);
                }
            }
            catch (Exception ex)
            {
                ReportProgress($"RES.BIN 复制错误: {ex.Message}", 40, true);
                throw;
            }
            
            return resBinFileName;
        }

        /// <summary>
        /// 复制 ELF 文件到输出目录
        /// </summary>
        private string CopyElfToOutput()
        {
            var elfFileName = Path.GetFileName(_config.ElfPath);
            var destPath = Path.Combine(_config.OutputPath, elfFileName);
            
            try
            {
                File.Copy(_config.ElfPath, destPath, true);
                
                // 验证复制后的文件
                if (File.Exists(destPath))
                {
                    var fileSize = new FileInfo(destPath).Length;
                    ReportProgress($"已复制 ELF 文件: {elfFileName} ({fileSize / 1024} KB)", 45);
                }
                else
                {
                    ReportProgress($"警告: ELF 文件复制后不存在!", 45, true);
                }
            }
            catch (Exception ex)
            {
                ReportProgress($"ELF 复制错误: {ex.Message}", 45, true);
                throw;
            }
            
            return elfFileName;
        }

        /// <summary>
        /// 复制 BIN 文件到输出目录
        /// </summary>
        private string CopyBinToOutput()
        {
            var binFileName = Path.GetFileName(_config.BinPath);
            var destPath = Path.Combine(_config.OutputPath, binFileName);
            
            try
            {
                File.Copy(_config.BinPath, destPath, true);
                
                // 验证复制后的文件
                if (File.Exists(destPath))
                {
                    var fileSize = new FileInfo(destPath).Length;
                    ReportProgress($"已复制 BIN 文件: {binFileName} ({fileSize / 1024} KB)", 45);
                }
                else
                {
                    ReportProgress($"警告: BIN 文件复制后不存在!", 45, true);
                }
            }
            catch (Exception ex)
            {
                ReportProgress($"BIN 复制错误: {ex.Message}", 45, true);
                throw;
            }
            
            return binFileName;
        }

        /// <summary>
        /// 运行 MakeSPIBin.exe
        /// </summary>
        private bool RunMakeSpiBin(string elfFileName, string resBinFileName)
        {
            try
            {
                var elfFullPath = Path.Combine(_config.OutputPath, elfFileName);
                var resBinFullPath = Path.Combine(_config.OutputPath, resBinFileName);
                
                ReportProgress($"=" + new string('=', 50), 51);
                ReportProgress($"工作目录: {_config.OutputPath}", 52);
                ReportProgress($"ELF 文件: {elfFileName}", 53);
                ReportProgress($"  - 完整路径: {elfFullPath}", 53);
                ReportProgress($"  - 存在: {File.Exists(elfFullPath)}", 53);
                if (File.Exists(elfFullPath))
                {
                    ReportProgress($"  - 大小: {new FileInfo(elfFullPath).Length / 1024} KB", 53);
                }
                ReportProgress($"RES 文件: {resBinFileName}", 54);
                ReportProgress($"  - 完整路径: {resBinFullPath}", 54);
                ReportProgress($"  - 存在: {File.Exists(resBinFullPath)}", 54);
                if (File.Exists(resBinFullPath))
                {
                    ReportProgress($"  - 大小: {new FileInfo(resBinFullPath).Length / 1024} KB", 54);
                }
                ReportProgress($"=" + new string('=', 50), 55);
                
                var arguments = $"\"{elfFileName}\" \"{resBinFileName}\"";
                
                ReportProgress($"调用方式: MakeSPIBin.exe {arguments}", 56);

                var makeSpiBinFullPath = Path.Combine(_config.OutputPath, "MakeSPIBin.exe");
                
                if (!File.Exists(makeSpiBinFullPath))
                {
                    ReportProgress($"错误: MakeSPIBin.exe 不存在于输出目录!", 56, true);
                    ReportProgress($"期望位置: {makeSpiBinFullPath}", 56, true);
                    throw new FileNotFoundException("MakeSPIBin.exe not found in output directory", makeSpiBinFullPath);
                }
                
                ReportProgress($"MakeSPIBin.exe 路径: {makeSpiBinFullPath}", 57);
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = makeSpiBinFullPath,
                    Arguments = arguments,
                    WorkingDirectory = _config.OutputPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        ReportProgress("无法启动 MakeSPIBin.exe", 60, true);
                        return false;
                    }

                    var outputBuilder = new StringBuilder();
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            outputBuilder.AppendLine(e.Data);
                            ReportProgress($"[OUT] {e.Data}", 60 + (int)(outputBuilder.Length * 0.3));
                        }
                    };

                    var errorBuilder = new StringBuilder();
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            errorBuilder.AppendLine(e.Data);
                            ReportProgress($"[ERR] {e.Data}", 60, true);
                        }
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    var completed = process.WaitForExit(_config.BuildTimeoutMs);
                    
                    if (!completed)
                    {
                        process.Kill();
                        ReportProgress("MakeSPIBin.exe 执行超时", 90, true);
                        return false;
                    }

                    ReportProgress($"MakeSPIBin.exe 退出码: {process.ExitCode}", 95);

                    var destBinPath = Path.Combine(_config.OutputPath, "DestBin.bin");
                    if (process.ExitCode == 0 && File.Exists(destBinPath))
                    {
                        var fileSize = new FileInfo(destBinPath).Length;
                        ReportProgress($"生成 DestBin.bin ({fileSize / 1024} KB)", 98);
                        return true;
                    }
                    else
                    {
                        ReportProgress($"生成失败: {errorBuilder}", 95, true);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                ReportProgress($"执行 MakeSPIBin.exe 异常: {ex.Message}", 90, true);
                return false;
            }
        }

        /// <summary>
        /// 报告进度
        /// </summary>
        private void ReportProgress(string message, int progress, bool isError = false)
        {
            ProgressChanged?.Invoke(this, new BuildProgressEventArgs
            {
                Message = message,
                Progress = progress,
                IsError = isError
            });
        }
    }
}
