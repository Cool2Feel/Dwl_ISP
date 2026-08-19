namespace ResBinManager.ViewModels
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Windows;
    using System.Windows.Forms;
    using ResBinManager.Core;
    using ResBinManager.Models;
    using MessageBox = System.Windows.MessageBox;
    using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
    using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
    using DialogResult = System.Windows.Forms.DialogResult;

    public partial class MainViewModel
    {
        private bool CanExecuteBuildFirmware(object? parameter)
        {
            if (IsBuilding) return false;

            string resBinPath = !string.IsNullOrEmpty(_buildConfig.ResBinPath)
                ? _buildConfig.ResBinPath
                : _currentFilePath;

            bool hasInputFile = (_buildConfig.InputType == FirmwareInputType.Elf && !string.IsNullOrEmpty(_buildConfig.ElfPath)) ||
                               (_buildConfig.InputType == FirmwareInputType.Bin && !string.IsNullOrEmpty(_buildConfig.BinPath));

            return !string.IsNullOrEmpty(resBinPath) &&
                   !string.IsNullOrEmpty(_buildConfig.MakeSpiBinPath) &&
                   !string.IsNullOrEmpty(_buildConfig.OutputPath) &&
                   hasInputFile;
        }

        private void ExecuteBuildFirmware(object? parameter)
        {
            if (IsBuilding)
            {
                MessageBox.Show("Packaging is already in progress.", "Warning",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_buildConfig.ResBinPath) && !string.IsNullOrEmpty(_currentFilePath))
            {
                _buildConfig.ResBinPath = _currentFilePath;
            }

            if (string.IsNullOrEmpty(_buildConfig.ResBinPath))
            {
                MessageBox.Show("Please open a RES.BIN file first or set the resource file path.",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IsBuilding = true;
            BuildProgress = 0;
            BuildLog = string.Empty;
            StatusMessage = "Starting firmware packaging...";

            try
            {
                if (string.IsNullOrEmpty(_buildConfig.ElfPath) && string.IsNullOrEmpty(_buildConfig.BinPath))
                {
                    var appDir = AppDomain.CurrentDomain.BaseDirectory;
                    var outputDir = _buildConfig.OutputPath;

                    var binCandidates = new[]
                    {
                        Path.Combine(outputDir, "ax329x_sdk.bin"),
                        Path.Combine(appDir, "..", "..", "..", "ax32_platform_demo", "output", "ax329x_sdk.bin")
                    };

                    foreach (var binPath in binCandidates)
                    {
                        var fullPath = Path.GetFullPath(binPath);
                        if (File.Exists(fullPath))
                        {
                            _buildConfig.BinPath = fullPath;
                            _buildConfig.InputType = FirmwareInputType.Bin;
                            BuildLog += $"自动检测到 BIN 文件: {Path.GetFileName(fullPath)}\n";
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(_buildConfig.BinPath))
                    {
                        var elfCandidates = new[]
                        {
                            Path.Combine(appDir, "..", "..", "..", "ax32_platform_demo", "Debug", "ax329x_sdk.elf"),
                            Path.Combine(outputDir, "ax329x_sdk.elf")
                        };

                        foreach (var elfPath in elfCandidates)
                        {
                            var fullPath = Path.GetFullPath(elfPath);
                            if (File.Exists(fullPath))
                            {
                                _buildConfig.ElfPath = fullPath;
                                _buildConfig.InputType = FirmwareInputType.Elf;
                                BuildLog += $"自动检测到 ELF 文件: {Path.GetFileName(fullPath)}\n";
                                break;
                            }
                        }
                    }
                }

                bool hasModifiedResources = Resources.Any(r => r != null && r.IsModified);
                byte[]? resBinDataToUse = null;

                if (hasModifiedResources && _currentFileData != null)
                {
                    BuildLog += "检测到未保存的修改，将使用最新的资源数据\n";
                    resBinDataToUse = _currentFileData;
                }

                BuildLog += $"输入类型: {_buildConfig.InputType}\n";
                if (_buildConfig.InputType == FirmwareInputType.Elf)
                {
                    BuildLog += $"ELF 文件: {Path.GetFileName(_buildConfig.ElfPath)}\n";
                }
                else
                {
                    BuildLog += $"BIN 文件: {Path.GetFileName(_buildConfig.BinPath)}\n";
                }

                _firmwareBuilder = new FirmwareBuilder(_buildConfig, resBinDataToUse);
                _firmwareBuilder.ProgressChanged += OnBuildProgressChanged;

                var result = _firmwareBuilder.Build();

                if (result.Success)
                {
                    var duration = result.Duration.TotalSeconds;
                    var fileSizeKB = new FileInfo(result.OutputFile).Length / 1024;

                    BuildLog += $"\n✅ 打包成功！\n";
                    BuildLog += $"输出文件: {result.OutputFile}\n";
                    BuildLog += $"文件大小: {fileSizeKB} KB\n";
                    BuildLog += $"耗时: {duration:F2} 秒\n";

                    StatusMessage = $"Firmware built successfully: {Path.GetFileName(result.OutputFile)}";

                    MessageBox.Show(
                        $"固件打包成功！\n\n" +
                        $"输出文件: {result.OutputFile}\n" +
                        $"文件大小: {fileSizeKB} KB\n" +
                        $"耗时: {duration:F2} 秒",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    BuildLog += $"\n❌ 打包失败: {result.ErrorMessage}\n";
                    StatusMessage = "Firmware build failed";

                    MessageBox.Show(
                        $"固件打包失败！\n\n{result.ErrorMessage}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                BuildLog += $"\n❌ 异常: {ex.Message}\n";
                StatusMessage = "Build error occurred";

                MessageBox.Show($"打包过程发生异常: {ex.Message}",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _firmwareBuilder?.Cleanup();
                IsBuilding = false;
                BuildProgress = 0;
            }
        }

        private void OnBuildProgressChanged(object? sender, BuildProgressEventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                BuildProgress = e.Progress;

                if (e.IsError)
                {
                    BuildLog += $"[ERROR] {e.Message}\n";
                }
                else
                {
                    BuildLog += $"{e.Message}\n";
                }
            });
        }

        private void ExecuteSelectElf(object? parameter)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "ELF files|*.elf|All files|*.*",
                Title = "Select ELF File"
            };

            if (dialog.ShowDialog() == true)
            {
                _buildConfig.ElfPath = dialog.FileName;
                _buildConfig.InputType = FirmwareInputType.Elf;
                StatusMessage = $"ELF file selected: {Path.GetFileName(dialog.FileName)}";
                (BuildFirmwareCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private void ExecuteSelectBin(object? parameter)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "BIN files|*.bin|All files|*.*",
                Title = "Select BIN File"
            };

            if (dialog.ShowDialog() == true)
            {
                _buildConfig.BinPath = dialog.FileName;
                _buildConfig.InputType = FirmwareInputType.Bin;
                StatusMessage = $"BIN file selected: {Path.GetFileName(dialog.FileName)}";
                (BuildFirmwareCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private void ExecuteSelectMakeSpiBin(object? parameter)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executable files|*.exe|All files|*.*",
                Title = "Select MakeSPIBin.exe"
            };

            if (dialog.ShowDialog() == true)
            {
                _buildConfig.MakeSpiBinPath = dialog.FileName;
                StatusMessage = $"MakeSPIBin.exe selected: {Path.GetFileName(dialog.FileName)}";
                (BuildFirmwareCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private void ExecuteSelectOutputPath(object? parameter)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select Output Directory (must contain MakeSPIBin.exe)";
#if NET6_0_OR_GREATER
                dialog.UseDescriptionForTitle = true;
#endif

                if (!string.IsNullOrEmpty(_buildConfig.OutputPath) && Directory.Exists(_buildConfig.OutputPath))
                {
                    dialog.SelectedPath = _buildConfig.OutputPath;
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var makeSpiBinPath = Path.Combine(dialog.SelectedPath, "MakeSPIBin.exe");
                    if (!File.Exists(makeSpiBinPath))
                    {
                        var result = MessageBox.Show(
                            $"警告：选择的目录中不包含 MakeSPIBin.exe！\n\n" +
                            $"目录: {dialog.SelectedPath}\n\n" +
                            $"MakeSPIBin.exe 必须在输出目录中才能正常工作。\n\n" +
                            $"是否继续？（可能会导致打包失败）",
                            "Warning",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result != MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }

                    _buildConfig.OutputPath = dialog.SelectedPath;
                    StatusMessage = $"Output directory selected: {dialog.SelectedPath}";
                    (BuildFirmwareCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }
    }
}
