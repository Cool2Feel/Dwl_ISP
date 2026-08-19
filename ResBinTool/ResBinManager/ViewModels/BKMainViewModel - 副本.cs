using ResBinManager.Core;
using ResBinManager.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ResBinManager.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        #region Fields
        private ObservableCollection<ResourceItem> _resources = null!;
        private ResourceItem? _selectedResource;
        private string _statusMessage = string.Empty;
        private bool _isLoading;
        private bool _isBuilding;
        private int _buildProgress;
        private string _buildLog = string.Empty;

        private ResBinParser? _parser;
        private DestBinParser? _destBinParser;  // 新增：DestBin.bin 解析器
        private ResHParser? _resHParser;  // 新增：RES.H 解析器
        private byte[]? _currentFileData;
        private uint _currentTableOffset;
        private string _currentFilePath = string.Empty;
        private bool _isDestBinMode = true;  // 新增：是否为 DestBin.bin 模式
        private string? _firmwareVersion = null;  // 固件版本号
        private uint _magicKey = 0;   // ✅ P3: MAGICKEY常量值（替换_firmwareSerial）
        private FirmwareBuildConfig _buildConfig;
        private FirmwareBuilder? _firmwareBuilder;

        // WAV 播放相关
        private WavPlayer? _wavPlayer;
        private WavInfo? _wavInfo;
        private float _wavVolume = 80.0f; // 默认音量 80%

        // Font 预览相关
        private FontInfo? _fontInfo;
        public byte[]? FontData { get; private set; }
        public byte[]? FontIndex { get; private set; }

        // 配置管理相关
        private FirmwareConfigData? _firmwareConfigData;
        private ObservableCollection<FirmwareConfigItem> _configItems = new();
        private bool _isConfigModified;
        private ConfigTemplateId _selectedConfigTemplate = ConfigTemplateId.Default;
        private ProjectType _selectedProjectType = ProjectType.Unknown;

        #endregion

        #region Properties

        /// <summary>
        /// 当前文件数据（用于 UI 层提取资源数据进行预览）
        /// </summary>
        public byte[]? CurrentFileData => _currentFileData;

        public ObservableCollection<ResourceItem> Resources
        {
            get => _resources;
            set { _resources = value; OnPropertyChanged(); }
        }

        public ResourceItem? SelectedResource
        {
            get => _selectedResource;
            set
            {
                _selectedResource = value;

                System.Diagnostics.Debug.WriteLine($"[VM] SelectedResource changed: ID={value?.Id}, Type={value?.Type}, Name={value?.Name}");

                // 先清空之前的数据
                WavInfo = null;
                _wavPlayer?.Stop();
                FontInfo = null;
                FontData = null;
                FontIndex = null;

                // 然后根据类型加载新的预览
                if (value?.Type == ResourceType.Wav)
                {
                    System.Diagnostics.Debug.WriteLine("[VM] Loading WAV preview");
                    LoadWavForPreview();
                }
                else if ((value?.Type == ResourceType.Binary || value?.Type == ResourceType.Font) && IsFontResource(value))
                {
                    System.Diagnostics.Debug.WriteLine($"[VM] Loading Font preview (Type={value.Type})");
                    LoadFontForPreview();
                }
                else if (value != null)
                {
                    // 对于其他资源类型（图片、二进制等），触发预览事件
                    System.Diagnostics.Debug.WriteLine($"[VM] Triggering preview for resource type: {value.Type}");
                    PreviewRequested?.Invoke(this, value);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[VM] No resource selected");
                }

                // 最后通知 UI 更新
                OnPropertyChanged();

                // 通知命令状态更新，使 Preview 按钮根据选中资源的 IsModified 状态变化
                (PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public bool IsBuilding
        {
            get => _isBuilding;
            set { _isBuilding = value; OnPropertyChanged(); }
        }

        public int BuildProgress
        {
            get => _buildProgress;
            set { _buildProgress = value; OnPropertyChanged(); }
        }

        public string BuildLog
        {
            get => _buildLog;
            set { _buildLog = value; OnPropertyChanged(); }
        }

        public FirmwareBuildConfig BuildConfig
        {
            get => _buildConfig;
            set { _buildConfig = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// WAV 音频信息
        /// </summary>
        public WavInfo? WavInfo
        {
            get => _wavInfo;
            set { _wavInfo = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// WAV 音量 (0-100)
        /// </summary>
        public float WavVolume
        {
            get => _wavVolume;
            set
            {
                _wavVolume = value;
                if (_wavPlayer != null)
                {
                    _wavPlayer.Volume = value / 100.0f;
                }
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 字体信息
        /// </summary>
        public FontInfo? FontInfo
        {
            get => _fontInfo;
            set { _fontInfo = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 是否为 DestBin.bin 模式
        /// </summary>
        public bool IsDestBinMode
        {
            get => _isDestBinMode;
            set { _isDestBinMode = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 固件版本号（仅 DestBin.bin 模式）
        /// </summary>
        public string? FirmwareVersion
        {
            get => _firmwareVersion;
            set { _firmwareVersion = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// MAGICKEY常量值（仅 DestBin.bin 模式，偏移0x10）
        /// ✅ P3: 显示SDK的MAGICKEY而非序列号
        /// </summary>
        public uint MagicKey
        {
            get => _magicKey;
            set { _magicKey = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 固件配置数据
        /// </summary>
        public FirmwareConfigData? FirmwareConfigData
        {
            get => _firmwareConfigData;
            set { _firmwareConfigData = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 配置项列表
        /// </summary>
        public ObservableCollection<FirmwareConfigItem> ConfigItems
        {
            get => _configItems;
            set { _configItems = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 配置是否已修改
        /// </summary>
        public bool IsConfigModified
        {
            get => _isConfigModified;
            set { _isConfigModified = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 当前选择的配置方案模板
        /// </summary>
        public ConfigTemplateId SelectedConfigTemplate
        {
            get => _selectedConfigTemplate;
            set
            {
                if (_selectedConfigTemplate != value)
                {
                    _selectedConfigTemplate = value;
                    ConfigTemplateManager.CurrentTemplateId = value;
                    OnPropertyChanged();

                    // 用户切换方案时自动保存设置
                    SaveUserSettings();
                }
            }
        }

        /// <summary>
        /// 当前选择的项目类型
        /// </summary>
        public ProjectType SelectedProjectType
        {
            get => _selectedProjectType;
            set
            {
                if (_selectedProjectType != value)
                {
                    _selectedProjectType = value;
                    OnPropertyChanged();
                    System.Diagnostics.Debug.WriteLine($"[VM] Project type changed to: {value}");

                    // 如果已加载配置，重新解析以应用新的映射
                    if (FirmwareConfigData != null && !string.IsNullOrEmpty(_currentFilePath))
                    {
                        ReloadConfigWithNewProjectType();
                    }
                }
            }
        }

        /// <summary>
        /// 可用的项目类型列表
        /// </summary>
        public List<ProjectType> AvailableProjectTypes
        {
            get => new List<ProjectType>
            {
                ProjectType.Unknown,
                ProjectType.JT529X,
                ProjectType.DC508J,
                ProjectType.GX_T317BV200,
                ProjectType.HM020F,
                ProjectType.MKL_CM5,
                ProjectType.MKL_DM15,
                ProjectType.JRX_JT529X,
                ProjectType.JRX_AX329X
            };
        }

        /// <summary>
        /// 可用的配置方案模板列表（向后兼容）
        /// </summary>
        public Dictionary<ConfigTemplateId, ConfigTemplate> ConfigTemplates
        {
            get => ConfigTemplateManager.AllTemplatesLegacy;
        }

        #endregion

        // Commands
        public ICommand OpenCommand { get; }
        public ICommand ReplaceCommand { get; }
        public ICommand ReplaceFontCommand { get; }  // 新增
        public ICommand RevertCommand { get; }  // 恢复原始数据命令
        public ICommand ExportCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand BuildFirmwareCommand { get; }
        public ICommand SelectElfCommand { get; }
        public ICommand SelectBinCommand { get; }  // 新增
        public ICommand SelectMakeSpiBinCommand { get; }
        public ICommand SelectOutputPathCommand { get; }
        public ICommand PlayWavCommand { get; }
        public ICommand StopWavCommand { get; }

        // 配置管理命令
        public ICommand LoadConfigCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand ResetConfigCommand { get; }
        public ICommand ExportConfigCommand { get; }

        // 映射配置管理命令
        public ICommand LoadMappingConfigCommand { get; }
        public ICommand SaveMappingConfigCommand { get; }
        public ICommand ReloadAllMappingsCommand { get; }
        public ICommand GenerateSampleMappingCommand { get; }
        public ICommand GenerateFromSourceCommand { get; }  // 新增：从源码生成配置

        public MainViewModel()
        {
            Resources = new ObservableCollection<ResourceItem>();
            _statusMessage = "Ready. Open a RES.BIN file to start.";
            _buildConfig = new FirmwareBuildConfig();

            // 设置默认输出目录为程序运行目录下的 output 子目录
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var defaultOutputDir = Path.Combine(appDir, "output");
            if (!Directory.Exists(defaultOutputDir))
            {
                Directory.CreateDirectory(defaultOutputDir);
            }
            _buildConfig.OutputPath = defaultOutputDir;

            // 尝试自动检测并设置 MakeSPIBin.exe 路径
            AutoDetectMakeSpiBin();

            OpenCommand = new RelayCommand(ExecuteOpen, CanExecuteOpen);
            ReplaceCommand = new RelayCommand(ExecuteReplace, CanExecuteReplace);
            ReplaceFontCommand = new RelayCommand(ExecuteReplaceFont, CanExecuteReplaceFont);  // 新增
            RevertCommand = new RelayCommand(ExecuteRevert, CanExecuteRevert);  // 恢复命令
            ExportCommand = new RelayCommand(ExecuteExport, CanExecuteExport);
            SaveCommand = new RelayCommand(ExecuteSave, CanExecuteSave);
            PreviewCommand = new RelayCommand(ExecutePreview, CanExecutePreview);
            BuildFirmwareCommand = new RelayCommand(ExecuteBuildFirmware, CanExecuteBuildFirmware);
            SelectElfCommand = new RelayCommand(ExecuteSelectElf);
            SelectBinCommand = new RelayCommand(ExecuteSelectBin);  // 新增
            SelectMakeSpiBinCommand = new RelayCommand(ExecuteSelectMakeSpiBin);
            SelectOutputPathCommand = new RelayCommand(ExecuteSelectOutputPath);
            PlayWavCommand = new RelayCommand(ExecutePlayWav, CanExecutePlayWav);
            StopWavCommand = new RelayCommand(ExecuteStopWav, CanExecuteStopWav);

            // 配置管理命令初始化
            LoadConfigCommand = new RelayCommand(ExecuteLoadConfig, CanExecuteLoadConfig);
            SaveConfigCommand = new RelayCommand(ExecuteSaveConfig, CanExecuteSaveConfig);
            ResetConfigCommand = new RelayCommand(ExecuteResetConfig, CanExecuteResetConfig);
            ExportConfigCommand = new RelayCommand(ExecuteExportConfig, CanExecuteExportConfig);

            // 映射配置管理命令初始化
            LoadMappingConfigCommand = new RelayCommand(ExecuteLoadMappingConfig);
            SaveMappingConfigCommand = new RelayCommand(ExecuteSaveMappingConfig, CanExecuteSaveMappingConfig);
            ReloadAllMappingsCommand = new RelayCommand(ExecuteReloadAllMappings);
            GenerateSampleMappingCommand = new RelayCommand(ExecuteGenerateSampleMapping, CanExecuteGenerateSampleMapping);
            GenerateFromSourceCommand = new RelayCommand(ExecuteGenerateFromSource);  // 新增
        }

        /// <summary>
        /// 自动检测 MakeSPIBin.exe 的位置
        /// </summary>
        private void AutoDetectMakeSpiBin()
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            System.Diagnostics.Debug.WriteLine($"[AutoDetect] AppDir: {appDir}");

            // 1. 首先检查程序目录下是否有 MakeSPIBin.exe
            var localMakeSpiBin = Path.Combine(appDir, "MakeSPIBin.exe");
            System.Diagnostics.Debug.WriteLine($"[AutoDetect] Checking: {localMakeSpiBin}");
            if (File.Exists(localMakeSpiBin))
            {
                _buildConfig.MakeSpiBinPath = localMakeSpiBin;
                System.Diagnostics.Debug.WriteLine($"[AutoDetect] Found in app dir: {localMakeSpiBin}");
                return;
            }

            // 2. 检查父目录（可能在 tools 或 ax32_platform_demo 中）
            var parentDir = Directory.GetParent(appDir)?.FullName;
            if (!string.IsNullOrEmpty(parentDir))
            {
                System.Diagnostics.Debug.WriteLine($"[AutoDetect] ParentDir: {parentDir}");

                // 检查父目录
                var parentMakeSpiBin = Path.Combine(parentDir, "MakeSPIBin.exe");
                if (File.Exists(parentMakeSpiBin))
                {
                    _buildConfig.MakeSpiBinPath = parentMakeSpiBin;
                    System.Diagnostics.Debug.WriteLine($"[AutoDetect] Found in parent: {parentMakeSpiBin}");
                    return;
                }

                // 检查 ax32_platform_demo/output
                var sdkOutputDir = Path.Combine(parentDir, "ax32_platform_demo", "output");
                var sdkMakeSpiBin = Path.Combine(sdkOutputDir, "MakeSPIBin.exe");
                System.Diagnostics.Debug.WriteLine($"[AutoDetect] Checking SDK: {sdkMakeSpiBin}");
                if (File.Exists(sdkMakeSpiBin))
                {
                    _buildConfig.MakeSpiBinPath = sdkMakeSpiBin;
                    System.Diagnostics.Debug.WriteLine($"[AutoDetect] Found in SDK: {sdkMakeSpiBin}");

                    // 复制到程序目录的 output 中
                    var destPath = Path.Combine(_buildConfig.OutputPath, "MakeSPIBin.exe");
                    try
                    {
                        File.Copy(sdkMakeSpiBin, destPath, true);
                        StatusMessage = "已自动复制 MakeSPIBin.exe 到 output 目录";
                        System.Diagnostics.Debug.WriteLine($"[AutoDetect] Copied to: {destPath}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AutoDetect] Copy failed: {ex.Message}");
                        // 如果复制失败，仍然使用原路径
                    }
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[AutoDetect] MakeSPIBin.exe not found automatically");
        }

        private bool CanExecuteOpen(object? parameter) => !IsLoading;

        /// <summary>
        /// 智能打开文件（自动检测类型）
        /// </summary>
        private void ExecuteOpen(object? parameter)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Binary files|*.bin|All files|*.*",
                Title = "Open RES.BIN or DestBin.bin File"
            };

            if (dialog.ShowDialog() == true)
            {
                LoadFileSmart(dialog.FileName);
            }
        }

        /// <summary>
        /// 智能加载文件（通过文件名自动识别 RES.BIN 或 DestBin.bin）
        /// </summary>
        private void LoadFileSmart(string filePath)
        {
            // 清理之前的状态
            CleanupPreviousLoad();

            IsLoading = true;
            _currentFilePath = filePath;

            try
            {
                // 通过文件名判断文件类型
                string fileName = Path.GetFileName(filePath).ToLower();
                bool isDestBin = false;

                // DestBin.bin 特征文件名：
                // - DestBin.bin
                // - ax329x_sdk.bin (固件输出文件)
                // - firmware.bin
                // - 包含 "dest" 或 "firmware" 关键词
                if (fileName.Contains("destbin") ||
                    fileName.Contains("ax329x_sdk") ||
                    fileName.Contains("firmware"))
                {
                    isDestBin = true;
                }

                System.Diagnostics.Debug.WriteLine($"[LoadFileSmart] File: {fileName}, Detected as DestBin: {isDestBin}");

                if (isDestBin)
                {
                    // 尝试作为 DestBin.bin 加载
                    if (!TryLoadAsDestBin(filePath))
                    {
                        // 如果 DestBin 加载失败，回退到 RES.BIN 模式
                        System.Diagnostics.Debug.WriteLine("[LoadFileSmart] DestBin load failed, falling back to RES.BIN mode");
                        LoadResBin(filePath);
                    }
                }
                else
                {
                    // 作为普通 RES.BIN 加载
                    LoadResBin(filePath);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading file: {ex.Message}";
                MessageBox.Show($"Failed to load file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                IsLoading = false;
            }
        }

        /// <summary>
        /// 清理之前加载的状态，防止资源泄漏和状态混乱
        /// </summary>
        private void CleanupPreviousLoad()
        {
            System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Cleaning up previous state...");

            // 重要：先清空选中资源，触发 UI 层清空预览面板
            if (SelectedResource != null)
            {
                System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Clearing SelectedResource to trigger preview cleanup");
                SelectedResource = null;
            }

            // 清空资源列表
            if (Resources != null && Resources.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[CleanupPreviousLoad] Clearing {Resources.Count} resources");
                Resources.Clear();
            }

            // 释放 ResBinParser
            if (_parser != null)
            {
                System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Disposing ResBinParser");
                _parser = null;
            }

            // 释放 DestBinParser
            if (_destBinParser != null)
            {
                System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Disposing DestBinParser");
                _destBinParser = null;
            }

            // 释放 RES.H Parser
            if (_resHParser != null)
            {
                System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Disposing ResHParser");
                _resHParser = null;
            }

            // 清空当前文件数据
            if (_currentFileData != null)
            {
                System.Diagnostics.Debug.WriteLine($"[CleanupPreviousLoad] Clearing file data ({_currentFileData.Length} bytes)");
                _currentFileData = null;
            }

            // 重置状态
            _currentTableOffset = 0;
            IsDestBinMode = false;
            FirmwareVersion = null;
            MagicKey = 0;  // ✅ P3: 重置MAGICKEY
            WavInfo = null;
            FontInfo = null;
            FontData = null;
            FontIndex = null;

            System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Cleanup complete");
        }

        /// <summary>
        /// 尝试作为 DestBin.bin 加载
        /// </summary>
        private bool TryLoadAsDestBin(string filePath)
        {
            string? tempFile = null;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] Loading: {filePath}");

                _destBinParser = new DestBinParser();

                if (_destBinParser.Load(filePath))
                {
                    System.Diagnostics.Debug.WriteLine("[TryLoadAsDestBin] DestBinParser.Load() succeeded");

                    // 解析 RES.H 文件（如果存在）
                    _resHParser = new ResHParser();
                    var resHPath = ResHParser.AutoFindResH(filePath);
                    if (resHPath != null && _resHParser.Parse(resHPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] RES.H parsed successfully: {resHPath}");
                        _resHParser.PrintSummary();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[TryLoadAsDestBin] RES.H not found or parse failed, continuing without it");
                        _resHParser = null;
                    }

                    // 提取 RES.BIN
                    var resBinData = _destBinParser.ExtractResBin();

                    if (resBinData != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] Extracted RES.BIN: {resBinData.Length} bytes");

                        // 保存到临时文件并用 ResBinParser 解析
                        tempFile = Path.GetTempFileName();
                        File.WriteAllBytes(tempFile, resBinData);
                        System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] Temp file: {tempFile}");

                        // 关键修改：传入 DestBin.bin 的目录作为 RES.H 搜索路径
                        string? destBinDir = Path.GetDirectoryName(filePath);
                        _parser = new ResBinParser(tempFile, destBinDir);
                        System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] Search base path for RES.H: {destBinDir}");

                        // 设置资源区基地址为 DestBin.bin 中 RES.BIN 的偏移
                        // 这样显示的偏移地址就是相对于 DestBin.bin 文件开头的绝对偏移
                        _parser.SetResourceBaseAddress(_destBinParser.ResBinOffset);

                        if (_parser.Parse())
                        {
                            System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] ResBinParser.Parse() succeeded, Resources: {_parser.Resources.Count}");

                            // 调试：检查第一个资源的数据
                            if (_parser.Resources.Count > 0)
                            {
                                var firstResource = _parser.Resources[0];
                                System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] First resource after parsing:");
                                System.Diagnostics.Debug.WriteLine($"  Name: {firstResource.Name}");
                                System.Diagnostics.Debug.WriteLine($"  Type: {firstResource.Type}");
                                System.Diagnostics.Debug.WriteLine($"  Offset: 0x{firstResource.Offset:X}");
                                System.Diagnostics.Debug.WriteLine($"  Size: {firstResource.Size}");
                                if (firstResource.Data != null && firstResource.Data.Length >= 4)
                                {
                                    System.Diagnostics.Debug.WriteLine($"  First 4 bytes: {firstResource.Data[0]:X2} {firstResource.Data[1]:X2} {firstResource.Data[2]:X2} {firstResource.Data[3]:X2}");
                                    bool isJpeg = firstResource.Data[0] == 0xFF && firstResource.Data[1] == 0xD8 && firstResource.Data[2] == 0xFF;
                                    System.Diagnostics.Debug.WriteLine($"  Is JPEG? {isJpeg}");
                                    if (!isJpeg && firstResource.Name.Contains("AUDIOPLAY"))
                                    {
                                        System.Diagnostics.Debug.WriteLine($"  ⚠️ WARNING: First resource data appears corrupted!");
                                    }
                                }
                            }

                            // 如果 RES.H 已解析，则根据 RES.H 过滤资源列表
                            if (_resHParser != null && _resHParser.IsParsed)
                            {
                                var definedIndices = _resHParser.GetAllDefinedIndices();
                                System.Diagnostics.Debug.WriteLine($"[FilterResources] RES.H defines {definedIndices.Count} resources");

                                // 创建过滤后的资源列表
                                var filteredResources = new List<ResourceItem>();
                                int filteredCount = 0;
                                int skippedCount = 0;

                                foreach (var resource in _parser.Resources)
                                {
                                    if (definedIndices.Contains((int)resource.Id))
                                    {
                                        filteredResources.Add(resource);
                                        filteredCount++;
                                    }
                                    else
                                    {
                                        skippedCount++;
                                        System.Diagnostics.Debug.WriteLine($"[FilterResources] Skipping Resource_{resource.Id} (not defined in RES.H)");
                                    }
                                }

                                Resources.Clear();
                                foreach (var resource in filteredResources)
                                {
                                    Resources.Add(resource);
                                }

                                System.Diagnostics.Debug.WriteLine($"[FilterResources] Filtered: {filteredCount} kept, {skippedCount} skipped");

                                StatusMessage = $"Loaded {Resources.Count} resources from DestBin.bin ({Path.GetFileName(filePath)}) - Filtered by RES.H";
                            }
                            else
                            {
                                // 没有 RES.H，显示所有资源
                                Resources.Clear();
                                foreach (var resource in _parser.Resources)
                                {
                                    Resources.Add(resource);
                                }

                                StatusMessage = $"Loaded {Resources.Count} resources from DestBin.bin ({Path.GetFileName(filePath)})";
                            }

                            // 保存原始数据
                            _currentFileData = resBinData;
                            _currentTableOffset = _parser.TableOffset;
                            IsDestBinMode = true;

                            // 设置版本信息
                            FirmwareVersion = _destBinParser.FirmwareVersion;
                            MagicKey = _destBinParser.MagicKey;  // ✅ P3: 设置MAGICKEY

                            // 显示结构信息
                            var structureInfo = _destBinParser.GetStructureInfo();
                            System.Diagnostics.Debug.WriteLine(structureInfo);

                            MessageBox.Show(
                                $"Successfully loaded {Resources.Count} resources from DestBin.bin!\n\n" +
                                $"File: {Path.GetFileName(filePath)}\n" +
                                $"Size: {new FileInfo(filePath).Length:N0} bytes\n\n" +
                                $"{_destBinParser.ResBinSize / 1024.0:F2} KB resources extracted.",
                                "Success",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);

                            // 清理临时文件
                            if (tempFile != null && File.Exists(tempFile))
                            {
                                File.Delete(tempFile);
                                System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] Temp file deleted: {tempFile}");
                            }

                            IsLoading = false;
                            return true;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] ResBinParser.Parse() failed: {_parser.ErrorMessage}");
                            // RES.BIN 解析失败，回退到普通模式
                            if (tempFile != null && File.Exists(tempFile))
                            {
                                File.Delete(tempFile);
                                System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] Temp file deleted (parse failed): {tempFile}");
                            }
                            return false;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] ExtractResBin() returned null: {_destBinParser.ErrorMessage}");
                        return false;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] DestBinParser.Load() failed: {_destBinParser.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] Exception: {ex.Message}\n{ex.StackTrace}");

                // 确保临时文件被清理
                if (tempFile != null && File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                        System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBin] Temp file deleted (exception): {tempFile}");
                    }
                    catch
                    {
                        // 忽略删除失败
                    }
                }

                // DestBin 加载失败，回退到普通模式
                return false;
            }
        }

        /// <summary>
        /// 判断是否可以执行恢复操作
        /// </summary>
        private bool CanExecuteRevert(object? parameter)
        {
            return SelectedResource != null &&
                   SelectedResource.IsModified &&
                   SelectedResource.OriginalData != null;
        }

        /// <summary>
        /// 执行恢复操作，将资源恢复到替换前的状态
        /// </summary>
        private void ExecuteRevert(object? parameter)
        {
            if (SelectedResource == null || _parser == null || SelectedResource.OriginalData == null)
                return;

            // 确认对话框
            var result = MessageBox.Show(
                $"Are you sure you want to revert '{SelectedResource.Name}' to its original state?\n\n" +
                $"This will undo the replacement and restore the original data.",
                "Confirm Revert",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                StatusMessage = "Revert cancelled";
                return;
            }

            StatusMessage = $"Reverting {SelectedResource.Name}...";

            try
            {
                // 使用原始数据替换当前数据
                var writer = new ResBinWriter(_currentFileData!, _currentTableOffset,
                                            _parser.GetResourceTable());

                if (writer.ReplaceResource(SelectedResource.Id, SelectedResource.OriginalData))
                {
                    _currentFileData = writer.GetData();

                    // 更新资源状态
                    var currentSelected = SelectedResource;
                    currentSelected.IsModified = false;
                    currentSelected.Size = currentSelected.OriginalSize;

                    // 清除保存的原始数据
                    currentSelected.OriginalData = null;
                    currentSelected.OriginalSize = 0;

                    // 通知 Preview 命令状态更新
                    (PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();

                    StatusMessage = $"✓ Reverted {currentSelected.Name} to original";

                    // 重要：恢复后也需要更新所有资源的 Offset
                    UpdateResourceOffsetsAfterReplace();

                    // 刷新列表显示
                    var index = Resources.IndexOf(currentSelected);
                    if (index >= 0)
                    {
                        // 暂时阻止 SelectedResource 变化
                        var tempSelected = _selectedResource;
                        _selectedResource = null;

                        Resources.RemoveAt(index);
                        Resources.Insert(index, currentSelected);

                        // 恢复选中状态
                        _selectedResource = tempSelected;
                        OnPropertyChanged(nameof(SelectedResource));
                    }

                    // 如果是图片资源，立即更新预览显示
                    if (currentSelected.Type == ResourceType.Jpeg || currentSelected.Type == ResourceType.Bitmap)
                    {
                        // 触发预览事件，让 UI 层重新加载图片
                        PreviewRequested?.Invoke(this, currentSelected);
                    }

                    MessageBox.Show(
                        $"Resource reverted successfully!\n\n" +
                        $"'{currentSelected.Name}' has been restored to its original state.",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Revert failed:\n{writer.ErrorMessage}",
                                  "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = "Revert failed";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}\n\nType: {ex.GetType().Name}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Revert error occurred";
            }
        }

        /// <summary>
        /// 替换资源后，更新所有资源的 Offset（因为文件大小可能改变）
        /// </summary>
        private void UpdateResourceOffsetsAfterReplace()
        {
            if (_parser == null || Resources.Count == 0)
                return;

            // 从解析器获取最新的资源表（现在返回的是原始引用）
            var updatedTable = _parser.GetResourceTable();

            // 更新每个 ResourceItem 的 Offset 和 Size
            for (int i = 0; i < Resources.Count && i < updatedTable.Count; i++)
            {
                var resource = Resources[i];
                var entry = updatedTable[i];

                bool offsetChanged = resource.Offset != entry.Offset;  // ✅ 使用Offset
                bool sizeChanged = resource.Size != entry.Length;

                if (offsetChanged)
                    resource.Offset = entry.Offset;  // ✅ 使用Offset
                if (sizeChanged)
                    resource.Size = entry.Length;
            }
        }


        private void LoadResBin(string filePath)
        {
            IsLoading = true;
            StatusMessage = "Parsing RES.BIN...";
            _currentFilePath = filePath;

            // 自动设置 RES.BIN 路径为当前打开的文件
            _buildConfig.ResBinPath = filePath;

            try
            {
                _parser = new ResBinParser(filePath);

                if (_parser.Parse())
                {
                    Resources.Clear();
                    foreach (var resource in _parser.Resources)
                    {
                        Resources.Add(resource);
                    }

                    // 保存原始数据用于后续修改
                    _currentFileData = _parser.FileData;
                    _currentTableOffset = _parser.TableOffset;

                    StatusMessage = $"Loaded {Resources.Count} resources from {Path.GetFileName(filePath)}";

                    MessageBox.Show(
                        $"Successfully loaded {Resources.Count} resources!\n\n" +
                        $"File: {Path.GetFileName(filePath)}\n" +
                        $"Size: {new FileInfo(filePath).Length:N0} bytes",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to parse file:\n{_parser.ErrorMessage}",
                                  "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = "Parse failed";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Error occurred";
            }
            finally
            {
                IsLoading = false;
                // 通知命令状态更新，使 BuildFirmware 按钮可以启用
                (BuildFirmwareCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private bool CanExecuteReplace(object? parameter) => SelectedResource != null && _parser != null;

        private void ExecuteReplace(object? parameter)
        {
            if (SelectedResource == null || _parser == null)
                return;

            // 检查资源是否有效（零长度或无效资源）
            if (SelectedResource.Size == 0)
            {
                MessageBox.Show(
                    $"Resource {SelectedResource.Id} ({SelectedResource.Name}) does not exist.\n\n" +
                    "This resource has zero length and cannot be replaced.\n" +
                    "It may have been removed or is not available in this platform.",
                    "Resource Not Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                StatusMessage = $"Cannot replace: {SelectedResource.Name} does not exist";
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = $"Replace Resource {SelectedResource.Id} ({SelectedResource.Name})",
                Filter = GetFilterByType(SelectedResource.Type)
            };

            if (dialog.ShowDialog() != true)
                return;

            StatusMessage = $"Replacing {SelectedResource.Name}...";

            try
            {
                // 如果是第一次修改，先保存原始数据用于恢复
                if (!SelectedResource.IsModified)
                {
                    SelectedResource.OriginalData = new byte[SelectedResource.Size];
                    Array.Copy(_currentFileData!, SelectedResource.Offset,
                              SelectedResource.OriginalData, 0, SelectedResource.Size);
                    SelectedResource.OriginalSize = SelectedResource.Size;
                }

                var newData = File.ReadAllBytes(dialog.FileName);

                // 对 WAV 资源进行特殊验证
                if (SelectedResource.Type == ResourceType.Wav)
                {
                    if (!ValidateAndConfirmWavReplacement(newData))
                    {
                        StatusMessage = "WAV replacement cancelled";
                        return;
                    }
                }
                // 对 Palette 资源进行特殊验证
                else if (SelectedResource.Type == ResourceType.Palette)
                {
                    if (!ValidateAndConfirmPaletteReplacement(newData))
                    {
                        StatusMessage = "Palette replacement cancelled";
                        return;
                    }
                }
                // 对 GameMap 资源进行特殊验证
                else if (SelectedResource.Type == ResourceType.GameMap)
                {
                    if (!ValidateAndConfirmGameMapReplacement(newData))
                    {
                        StatusMessage = "Game map replacement cancelled";
                        return;
                    }
                }
                // 对 EncodingTable 资源进行特殊验证
                else if (SelectedResource.Type == ResourceType.EncodingTable)
                {
                    if (!ValidateAndConfirmEncodingTableReplacement(newData))
                    {
                        StatusMessage = "Encoding table replacement cancelled";
                        return;
                    }
                }

                // 检查文件大小差异，弹出确认对话框
                long sizeDiff = newData.Length - (long)SelectedResource.Size;
                double sizeDiffPercent = SelectedResource.Size > 0
                    ? (double)sizeDiff / SelectedResource.Size * 100
                    : 0;

                bool needsConfirmation = sizeDiff != 0; // 任何大小差异都需要确认

                if (needsConfirmation)
                {
                    string message;
                    MessageBoxImage icon;

                    if (sizeDiff > 0)
                    {
                        // 新文件更大
                        message = $"New file is LARGER than original:\n\n" +
                                 $"Original: {SelectedResource.Size:N0} bytes ({FormatFileSize(SelectedResource.Size)})\n" +
                                 $"New:      {newData.Length:N0} bytes ({FormatFileSize((uint)newData.Length)})\n" +
                                 $"Difference: +{sizeDiff:N0} bytes (+{sizeDiffPercent:F1}%)\n\n" +
                                 $"⚠️ This will shift all subsequent resources in the file.\n" +
                                 $"The file size will increase by {FormatFileSize((uint)sizeDiff)}.\n\n" +
                                 $"Continue with replacement?";
                        icon = MessageBoxImage.Warning;
                    }
                    else
                    {
                        // 新文件更小
                        message = $"New file is SMALLER than original:\n\n" +
                                 $"Original: {SelectedResource.Size:N0} bytes ({FormatFileSize(SelectedResource.Size)})\n" +
                                 $"New:      {newData.Length:N0} bytes ({FormatFileSize((uint)newData.Length)})\n" +
                                 $"Difference: {sizeDiff:N0} bytes ({sizeDiffPercent:F1}%)\n\n" +
                                 $"✓ The remaining space will be filled with 0xFF padding.\n" +
                                 $"No other resources will be affected.\n\n" +
                                 $"Continue with replacement?";
                        icon = MessageBoxImage.Question;
                    }

                    var result = MessageBox.Show(
                        message,
                        "Confirm Replacement",
                        MessageBoxButton.YesNo,
                        icon);

                    if (result != MessageBoxResult.Yes)
                    {
                        StatusMessage = "Replace cancelled by user";
                        return;
                    }
                }

                // 执行替换
                var writer = new ResBinWriter(_currentFileData!, _currentTableOffset,
                                            _parser.GetResourceTable());

                if (writer.ReplaceResource(SelectedResource.Id, newData))
                {
                    _currentFileData = writer.GetData();

                    // 保存当前选中的资源引用，防止在 UI 刷新时丢失
                    var currentSelected = SelectedResource;
                    currentSelected.IsModified = true;
                    currentSelected.Size = (uint)newData.Length;

                    // 通知 Preview 命令状态更新
                    (PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();

                    StatusMessage = $"✓ Replaced {currentSelected.Name}";

                    // 重要：对于 DestBin 模式，立即更新 _destBinParser 的状态
                    if (IsDestBinMode && _destBinParser != null)
                    {
                        if (_destBinParser.ReplaceResBin(_currentFileData, keepSize: false))
                        {
                            System.Diagnostics.Debug.WriteLine($"[Replace] DestBinParser updated successfully");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[Replace] Failed to update DestBinParser: {_destBinParser.ErrorMessage}");
                        }
                    }

                    // 重要：替换后需要更新所有资源的 Offset，因为文件大小可能改变
                    UpdateResourceOffsetsAfterReplace();

                    // 刷新列表显示 - 使用保存的引用
                    var index = Resources.IndexOf(currentSelected);
                    if (index >= 0)
                    {
                        // 暂时阻止 SelectedResource 变化
                        var tempSelected = _selectedResource;
                        _selectedResource = null;

                        Resources.RemoveAt(index);
                        Resources.Insert(index, currentSelected);

                        // 恢复选中状态
                        _selectedResource = tempSelected;
                        OnPropertyChanged(nameof(SelectedResource));
                    }

                    // 如果是图片资源，立即更新预览显示
                    if (currentSelected.Type == ResourceType.Jpeg || currentSelected.Type == ResourceType.Bitmap)
                    {
                        // 触发预览事件，让 UI 层重新加载图片
                        PreviewRequested?.Invoke(this, currentSelected);
                    }
                    MessageBox.Show(
                        $"Resource replaced successfully!\n\n" +
                        $"Don't forget to save the modified file.",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // 资源修改标记（用于触发资源保存按钮）
                    IsConfigModified = true;

                    // 更新资源保存按钮状态
                    (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
                else
                {
                    MessageBox.Show($"Replace failed:\n{writer.ErrorMessage}",
                                  "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = "Replace failed";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}\n\nType: {ex.GetType().Name}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Error occurred";
            }
        }

        /// <summary>
        /// 验证并确认 WAV 资源替换
        /// </summary>
        private bool ValidateAndConfirmWavReplacement(byte[] newWavData)
        {
            // 1. 验证新 WAV 文件
            var validationResult = WavValidator.Validate(newWavData);

            if (!validationResult.IsValid)
            {
                MessageBox.Show(
                    $"Invalid WAV file:\n\n{validationResult.ErrorMessage}",
                    "Validation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            // 2. 如果有原始 WAV 信息，显示对比
            string comparisonText = string.Empty;
            if (WavInfo != null && validationResult.Info != null)
            {
                comparisonText = WavValidator.CompareWavInfo(WavInfo, validationResult.Info);
            }

            // 3. 构建确认消息
            var message = new StringBuilder();
            message.AppendLine("WAV Resource Replacement");
            message.AppendLine();
            message.AppendLine("New File Information:");
            message.AppendLine(validationResult.GetDisplayText());

            if (!string.IsNullOrEmpty(comparisonText))
            {
                message.AppendLine();
                message.AppendLine(comparisonText);
            }

            if (validationResult.Warnings.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Please review the warnings above.");
            }

            message.AppendLine();
            message.AppendLine("Continue with replacement?");

            // 4. 显示确认对话框
            var result = MessageBox.Show(
                message.ToString(),
                "Confirm WAV Replacement",
                MessageBoxButton.YesNo,
                validationResult.Warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// 验证并确认 Palette 资源替换
        /// </summary>
        private bool ValidateAndConfirmPaletteReplacement(byte[] newPaletteData)
        {
            // 1. 验证新 Palette 文件
            var validationResult = PaletteValidator.Validate(newPaletteData);

            if (!validationResult.IsValid)
            {
                MessageBox.Show(
                    $"Invalid Palette file:\n\n{validationResult.ErrorMessage}",
                    "Validation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            // 2. 构建确认消息
            var message = new StringBuilder();
            message.AppendLine("Palette Resource Replacement");
            message.AppendLine();
            message.AppendLine("New File Information:");
            message.AppendLine(PaletteValidator.GetDisplayText(validationResult));
            message.AppendLine();
            message.AppendLine("Continue with replacement?");

            // 3. 显示确认对话框
            var result = MessageBox.Show(
                message.ToString(),
                "Confirm Palette Replacement",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// 验证并确认 GameMap 资源替换
        /// </summary>
        private bool ValidateAndConfirmGameMapReplacement(byte[] newGameMapData)
        {
            // 1. 验证新 GameMap 文件
            var validationResult = GameMapValidator.Validate(newGameMapData);

            if (!validationResult.IsValid)
            {
                MessageBox.Show(
                    $"Invalid Game Map file:\n\n{validationResult.ErrorMessage}",
                    "Validation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            // 2. 构建确认消息
            var message = new StringBuilder();
            message.AppendLine("Game Map Resource Replacement");
            message.AppendLine();
            message.AppendLine("New File Information:");
            message.AppendLine(GameMapValidator.GetDisplayText(validationResult));
            message.AppendLine();
            message.AppendLine("Continue with replacement?");

            // 3. 显示确认对话框
            var result = MessageBox.Show(
                message.ToString(),
                "Confirm Game Map Replacement",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// 验证并确认 EncodingTable 资源替换
        /// </summary>
        private bool ValidateAndConfirmEncodingTableReplacement(byte[] newEncodingData)
        {
            // 1. 验证新 EncodingTable 文件
            var validationResult = EncodingTableValidator.Validate(newEncodingData);

            if (!validationResult.IsValid)
            {
                MessageBox.Show(
                    $"Invalid Encoding Table file:\n\n{validationResult.ErrorMessage}",
                    "Validation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            // 2. 构建确认消息
            var message = new StringBuilder();
            message.AppendLine("Encoding Table Resource Replacement");
            message.AppendLine();
            message.AppendLine("New File Information:");
            message.AppendLine(EncodingTableValidator.GetDisplayText(validationResult));
            message.AppendLine();
            message.AppendLine("Continue with replacement?");

            // 3. 显示确认对话框
            var result = MessageBox.Show(
                message.ToString(),
                "Confirm Encoding Table Replacement",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// 格式化文件大小显示
        /// </summary>
        private string FormatFileSize(uint bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            else if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F2} KB";
            else if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F2} MB";
            else
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private string GetFilterByType(ResourceType type)
        {
            return type switch
            {
                ResourceType.Jpeg => "JPEG files|*.jpg;*.jpeg|All files|*.*",
                ResourceType.Bitmap => "Bitmap files|*.bmp|All files|*.*",
                ResourceType.Wav => "WAV files|*.wav|All files|*.*",
                _ => "All files|*.*"
            };
        }

        private bool CanExecuteExport(object? parameter) => SelectedResource != null;

        private void ExecuteExport(object? parameter)
        {
            if (SelectedResource == null)
                return;

            var dialog = new SaveFileDialog
            {
                FileName = $"{SelectedResource.Name}{GetExtension(SelectedResource.Type)}",
                Filter = "All files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                if (_parser!.ExportResource(SelectedResource.Id, dialog.FileName))
                {
                    StatusMessage = $"✓ Exported {SelectedResource.Name}";
                    MessageBox.Show("Resource exported successfully!", "Success",
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Export failed:\n{_parser.ErrorMessage}",
                                  "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string GetExtension(ResourceType type)
        {
            return type switch
            {
                ResourceType.Jpeg => ".jpg",
                ResourceType.Bitmap => ".bmp",
                ResourceType.Wav => ".wav",
                ResourceType.Binary => ".bin",
                _ => ".dat"
            };
        }

        private bool CanExecuteSave(object? parameter) => Resources.Any(r => r != null && r.IsModified) || IsConfigModified;

        /// <summary>
        /// 智能保存（根据模式自动选择保存方式）
        /// </summary>
        private void ExecuteSave(object? parameter)
        {
            // 弹出选择对话框：覆盖原文件 or 另存为新文件
            var result = MessageBox.Show(
                "How would you like to save the modified file?\n\n" +
                "• (YES)Overwrite: Replace the original file (creates backup)\n" +
                "• (NO)Save As: Save as a new file with different name\n\n" +
                "Choose an option:",
                "Save Options",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                StatusMessage = "Save cancelled";
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                // 用户选择“是” = 覆盖原文件
                ExecuteOverwriteFile();
            }
            else
            {
                // 用户选择“否” = 另存为新文件
                ExecuteSaveAsNewFile();
            }
        }

        /// <summary>
        /// 覆盖原文件（创建备份后覆盖）
        /// </summary>
        private void ExecuteOverwriteFile()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                MessageBox.Show("No file is currently loaded.", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "Saving... Creating backup...";

                // 1. 创建备份
                string backupPath = _currentFilePath + ".backup";
                if (File.Exists(_currentFilePath))
                {
                    File.Copy(_currentFilePath, backupPath, true);
                    System.Diagnostics.Debug.WriteLine($"[Save] Backup created: {backupPath}");
                }

                // 2. 根据模式执行不同的保存逻辑
                if (IsDestBinMode)
                {
                    // DestBin 模式：直接保存到原文件
                    ExecuteOverwriteDestBin();
                }
                else
                {
                    // RES.BIN 模式：直接写入原文件
                    File.WriteAllBytes(_currentFilePath, _currentFileData!);

                    // 重置所有资源修改状态
                    foreach (var r in Resources)
                    {
                        if (r != null) r.IsModified = false;
                    }
                    (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();

                    StatusMessage = $"✓ Overwritten: {Path.GetFileName(_currentFilePath)}";

                    var fileInfo = new FileInfo(_currentFilePath);
                    MessageBox.Show(
                        $"File overwritten successfully!\n\n" +
                        $"File: {Path.GetFileName(_currentFilePath)}\n" +
                        $"Size: {fileInfo.Length:N0} bytes ({FormatFileSize((uint)fileInfo.Length)})\n\n" +
                        $"Backup saved as:\n{Path.GetFileName(backupPath)}\n\n" +
                        $"Next steps:\n" +
                        $"1. Copy the modified .bin file to ax32_platform_demo/resource/\n" +
                        $"2. Run GenRes.bat to regenerate RES.H if needed\n" +
                        $"3. Rebuild firmware using MakeSPIBin.exe",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed: {ex.Message}\n\nThe backup file is still available.",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Save failed";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 覆盖 DestBin.bin 文件（同时处理资源和配置修改）
        /// </summary>
        private void ExecuteOverwriteDestBin()
        {
            if (_destBinParser == null || _currentFileData == null)
            {
                MessageBox.Show("No DestBin.bin file is currently loaded.", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                StatusMessage = "Saving DestBin.bin...";

                // 1. 始终应用资源修改，确保 _destBinParser 数据与 _currentFileData 一致
                // 即使没有资源修改标志，_destBinParser 可能包含旧数据或被之前的操作污染
                System.Diagnostics.Debug.WriteLine($"[Overwrite] Applying resource changes...");
                if (_destBinParser.ReplaceResBin(_currentFileData, keepSize: false))
                {
                    System.Diagnostics.Debug.WriteLine($"[Overwrite] Resource changes applied successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Overwrite] Failed to apply resource changes: {_destBinParser.ErrorMessage}");
                    throw new InvalidOperationException($"应用资源修改失败: {_destBinParser.ErrorMessage}");
                }

                // 2. 如果有配置修改，同步配置数据到固件
                ApplyConfigChangesToDestBin();

                // 3. 保存到原文件
                if (_destBinParser.Save(_currentFilePath))
                {
                    // 保存成功后重置所有修改状态
                    IsConfigModified = false;
                    (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    foreach (var r in Resources)
                    {
                        if (r != null) r.IsModified = false;
                    }
                    (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();

                    StatusMessage = $"✓ Saved to {Path.GetFileName(_currentFilePath)}";

                    var fileInfo = new FileInfo(_currentFilePath);
                    MessageBox.Show(
                        $"DestBin.bin saved successfully!\n\n" +
                        $"File: {Path.GetFileName(_currentFilePath)}\n" +
                        $"Size: {fileInfo.Length:N0} bytes ({FormatFileSize((uint)fileInfo.Length)})\n\n" +
                        $"The firmware is ready for flashing.",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to save DestBin.bin:\n{_destBinParser.ErrorMessage}",
                                  "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 另存为新文件
        /// </summary>
        private void ExecuteSaveAsNewFile()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                MessageBox.Show("No file is currently loaded.", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                FileName = Path.GetFileNameWithoutExtension(_currentFilePath) + "_modified.bin",
                Filter = IsDestBinMode ? "BIN files|*.bin|All files|*.*" : "BIN files|*.bin|All files|*.*",
                Title = IsDestBinMode ? "Save Modified DestBin.bin As..." : "Save Modified RES.BIN As...",
                InitialDirectory = Path.GetDirectoryName(_currentFilePath)
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    IsLoading = true;
                    StatusMessage = "Saving as new file...";

                    if (IsDestBinMode)
                    {
                        // DestBin 模式：先处理资源修改，再处理配置修改，然后保存
                        if (_destBinParser != null && _currentFileData != null)
                        {
                            // 始终应用资源修改，确保 _destBinParser 数据与 _currentFileData 一致
                            // 即使没有资源修改标志，_destBinParser 可能包含旧数据或被之前的操作污染
                            System.Diagnostics.Debug.WriteLine($"[SaveAs] Applying resource changes...");
                            if (_destBinParser.ReplaceResBin(_currentFileData, keepSize: false))
                            {
                                System.Diagnostics.Debug.WriteLine($"[SaveAs] Resource changes applied successfully");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[SaveAs] Failed to apply resource changes: {_destBinParser.ErrorMessage}");
                                throw new InvalidOperationException($"应用资源修改失败: {_destBinParser.ErrorMessage}");
                            }

                            // 2. 如果有配置修改，同步配置数据到固件
                            ApplyConfigChangesToDestBin();

                            // 3. 保存到新文件
                            if (_destBinParser.Save(dialog.FileName))
                            {
                                // 保存成功后重置所有修改状态
                                IsConfigModified = false;
                                (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                                foreach (var r in Resources)
                                {
                                    if (r != null) r.IsModified = false;
                                }
                                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();

                                StatusMessage = $"✓ Saved to {Path.GetFileName(dialog.FileName)}";

                                var fileInfo = new FileInfo(dialog.FileName);
                                MessageBox.Show(
                                    $"DestBin.bin saved successfully!\n\n" +
                                    $"File: {Path.GetFileName(dialog.FileName)}\n" +
                                    $"Size: {fileInfo.Length:N0} bytes ({FormatFileSize((uint)fileInfo.Length)})\n\n" +
                                    $"The firmware is ready for flashing.",
                                    "Success",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                            }
                            else
                            {
                                MessageBox.Show($"Failed to save:\n{_destBinParser.ErrorMessage}",
                                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    }
                    else
                    {
                        // RES.BIN 模式：直接保存
                        File.WriteAllBytes(dialog.FileName, _currentFileData!);

                        // 重置所有资源修改状态
                        foreach (var r in Resources)
                        {
                            if (r != null) r.IsModified = false;
                        }
                        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();

                        StatusMessage = $"✓ Saved to {Path.GetFileName(dialog.FileName)}";

                        var fileInfo = new FileInfo(dialog.FileName);
                        MessageBox.Show(
                            "File saved successfully!\n\n" +
                            $"File: {Path.GetFileName(dialog.FileName)}\n" +
                            $"Size: {fileInfo.Length:N0} bytes ({FormatFileSize((uint)fileInfo.Length)})\n\n" +
                            "Next steps:\n" +
                            "1. Copy the modified .bin file to ax32_platform_demo/resource/\n" +
                            "2. Run GenRes.bat to regenerate RES.H if needed\n" +
                            "3. Rebuild firmware using MakeSPIBin.exe",
                            "Success",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Save failed: {ex.Message}", "Error",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = "Save failed";
                }
                finally
                {
                    IsLoading = false;
                }
            }
            else
            {
                StatusMessage = "Save cancelled";
            }
        }

        private bool CanExecutePreview(object? parameter)
        {
            return SelectedResource != null && SelectedResource.IsModified;
        }

        private void ExecutePreview(object? parameter)
        {
            if (SelectedResource == null || _parser == null || _currentFileData == null) return;

            try
            {
                // 检查是否为 WAV 资源
                if (SelectedResource.Type == ResourceType.Wav)
                {
                    LoadWavForPreview();
                }
                else
                {
                    // 其他类型使用默认预览（图片等）
                    StatusMessage = $"Previewing {SelectedResource.Name}...";
                    PreviewRequested?.Invoke(this, SelectedResource);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Preview failed:\n{ex.Message}",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region WAV Playback Handling

        /// <summary>
        /// 加载 WAV 资源进行预览和播放
        /// </summary>
        private void LoadWavForPreview()
        {
            if (SelectedResource == null || _parser == null || _currentFileData == null)
                return;

            try
            {
                // 提取 WAV 数据
                var wavData = new byte[SelectedResource.Size];
                Array.Copy(_currentFileData, SelectedResource.Offset, wavData, 0, SelectedResource.Size);

                // 解析 WAV 信息
                WavInfo = WavInfoParser.Parse(wavData);

                // 创建播放器
                if (_wavPlayer == null)
                {
                    _wavPlayer = new WavPlayer();
                    _wavPlayer.PlaybackStateChanged += OnWavPlaybackStateChanged;
                }

                _wavPlayer.Load(wavData);

                StatusMessage = $"WAV loaded: {WavInfo.FullDescription}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load WAV:\n{ex.Message}",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                WavInfo = null;
            }
        }

        private void OnWavPlaybackStateChanged(object? sender, EventArgs e)
        {
            // 当播放状态改变时，刷新命令可用性
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                (PlayWavCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StopWavCommand as RelayCommand)?.RaiseCanExecuteChanged();
            });
        }

        private void ExecutePlayWav(object? parameter)
        {
            try
            {
                if (_wavPlayer == null)
                {
                    // 如果播放器未初始化，先加载
                    LoadWavForPreview();
                }

                _wavPlayer?.Play();
                StatusMessage = "Playing WAV...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Play failed:\n{ex.Message}",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanExecutePlayWav(object? parameter)
        {
            return SelectedResource?.Type == ResourceType.Wav &&
                   _wavPlayer != null &&
                   !_wavPlayer.IsPlaying;
        }

        private void ExecuteStopWav(object? parameter)
        {
            try
            {
                _wavPlayer?.Stop();
                StatusMessage = "Playback stopped";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Stop failed:\n{ex.Message}",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanExecuteStopWav(object? parameter)
        {
            return _wavPlayer != null && (_wavPlayer.IsPlaying || _wavPlayer.IsPaused);
        }

        #endregion

        #region Font Resource Handling

        /// <summary>
        /// 判断是否为字体资源
        /// </summary>
        private bool IsFontResource(ResourceItem? resource)
        {
            // 首先检查 null
            if (resource == null)
                return false;

            // 首先通过名称判断
            if (resource.Name.IndexOf("resfont", StringComparison.OrdinalIgnoreCase) >= 0 ||
                resource.Name.IndexOf("fontidx", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Font] Resource '{resource.Name}' matched by name");
                return true;
            }

            // 如果名称不包含 font 关键词，则不认为是字体资源
            // （不再使用硬编码的 ID，因为不同项目可能不同）
            return false;
        }

        /// <summary>
        /// 安全地根据 RES.H 定义获取资源索引
        /// </summary>
        /// <param name="resourceName">RES.H 中的资源名称（如 "RES_RESFONT"）</param>
        /// <returns>资源对象，如果不存在或无效返回 null</returns>
        private ResourceItem? GetResourceByResHName(string resourceName)
        {
            if (_resHParser == null || !_resHParser.IsParsed)
            {
                System.Diagnostics.Debug.WriteLine($"[GetResourceByResHName] RES.H parser not available");
                return null;
            }

            int index = _resHParser.GetIndex(resourceName);
            if (index < 0)
            {
                System.Diagnostics.Debug.WriteLine($"[GetResourceByResHName] Resource '{resourceName}' not found in RES.H");
                return null;
            }

            if (index >= Resources.Count)
            {
                System.Diagnostics.Debug.WriteLine($"[GetResourceByResHName] Resource '{resourceName}' index {index} out of range (total: {Resources.Count})");
                return null;
            }

            var resource = Resources[index];
            if (resource.Size == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[GetResourceByResHName] Resource '{resourceName}' has zero size");
                return null;
            }

            System.Diagnostics.Debug.WriteLine($"[GetResourceByResHName] Found '{resourceName}' at index {index}: {resource.Name}, Size={resource.Size}");
            return resource;
        }

        /// <summary>
        /// 安全地获取字体资源（兼容多平台）
        /// </summary>
        /// <returns>字体资源列表（可能包含 resfont 和 resfontidx）</returns>
        private List<ResourceItem> GetFontResources()
        {
            var fontResources = new List<ResourceItem>();

            // 方法 1: 使用 RES.H 解析器（推荐）
            if (_resHParser != null && _resHParser.IsParsed)
            {
                var resfont = GetResourceByResHName("RES_RESFONT");
                if (resfont != null)
                {
                    fontResources.Add(resfont);
                    System.Diagnostics.Debug.WriteLine($"[GetFontResources] Added RES_RESFONT from RES.H");
                }

                var resfontidx = GetResourceByResHName("RES_RESFONTIDX");
                if (resfontidx != null)
                {
                    fontResources.Add(resfontidx);
                    System.Diagnostics.Debug.WriteLine($"[GetFontResources] Added RES_RESFONTIDX from RES.H");
                }

                if (fontResources.Count > 0)
                {
                    return fontResources;
                }
            }

            // 方法 2: Fallback - 通过名称匹配
            System.Diagnostics.Debug.WriteLine("[GetFontResources] Fallback to name-based matching");
            foreach (var resource in Resources)
            {
                if (resource.Name.IndexOf("resfont", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    resource.Name.IndexOf("fontidx", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (resource.Size > 0)  // 只添加有效的资源
                    {
                        fontResources.Add(resource);
                        System.Diagnostics.Debug.WriteLine($"[GetFontResources] Found by name: {resource.Name}");
                    }
                }
            }

            return fontResources;
        }

        /// <summary>
        /// 加载字体资源进行预览
        /// </summary>
        private void LoadFontForPreview()
        {
            if (SelectedResource == null || _parser == null || _currentFileData == null)
            {
                System.Diagnostics.Debug.WriteLine("[Font] LoadFontForPreview: Missing required data");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[Font] Loading font for resource: ID={SelectedResource.Id}, Name={SelectedResource.Name}");

            try
            {
                // 使用新的安全方法获取字体资源（兼容多平台）
                var fontResources = GetFontResources();

                if (fontResources.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[Font] No font resources found");
                    StatusMessage = "Font resources not found";
                    return;
                }

                // 提取 resfont 和 resfontidx
                ResourceItem? resfont = null;
                ResourceItem? resfontidx = null;

                foreach (var fontRes in fontResources)
                {
                    if (fontRes.Name.IndexOf("resfontidx", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        resfontidx = fontRes;
                    }
                    else if (fontRes.Name.IndexOf("resfont", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        resfont = fontRes;
                    }
                }

                // 至少需要一个 resfont
                if (resfont == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Font] resfont resource not found");
                    StatusMessage = "resfont resource not found";
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[Font] Found resfont: Size={resfont.Size}, Offset={resfont.Offset}");
                System.Diagnostics.Debug.WriteLine($"[Font] Found resfontidx: Size={resfontidx.Size}, Offset={resfontidx.Offset}");

                // 提取字体数据
                FontData = new byte[resfont.Size];
                Array.Copy(_currentFileData, resfont.Offset, FontData, 0, resfont.Size);

                FontIndex = new byte[resfontidx.Size];
                Array.Copy(_currentFileData, resfontidx.Offset, FontIndex, 0, resfontidx.Size);

                System.Diagnostics.Debug.WriteLine($"[Font] Extracted data: FontData.Length={FontData.Length}, FontIndex.Length={FontIndex.Length}");

                // 解析字体信息
                FontInfo = FontInfoParser.Parse(FontData, FontIndex);

                System.Diagnostics.Debug.WriteLine($"[Font] Parsed successfully: {FontInfo.DisplayName}");
                StatusMessage = $"Font loaded: {FontInfo.DisplayName}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Font] Error: {ex.Message}");
                MessageBox.Show($"Failed to load font:\n{ex.Message}",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                FontInfo = null;
                FontData = null;
                FontIndex = null;
            }
        }

        #endregion

        public event EventHandler<ResourceItem>? PreviewRequested;

        // ==================== 固件打包相关方法 ====================
        #region Firmware Build Commands
        private bool CanExecuteBuildFirmware(object? parameter)
        {
            // 基本条件：不在构建中
            if (IsBuilding) return false;

            // RES.BIN 路径可以使用当前打开的文件
            string resBinPath = !string.IsNullOrEmpty(_buildConfig.ResBinPath)
                ? _buildConfig.ResBinPath
                : _currentFilePath;

            // 必须条件：MakeSPIBin.exe、输出目录和输入文件（ELF 或 BIN）
            bool hasInputFile = (_buildConfig.InputType == FirmwareInputType.Elf && !string.IsNullOrEmpty(_buildConfig.ElfPath)) ||
                               (_buildConfig.InputType == FirmwareInputType.Bin && !string.IsNullOrEmpty(_buildConfig.BinPath));

            return !string.IsNullOrEmpty(resBinPath) &&
                   !string.IsNullOrEmpty(_buildConfig.MakeSpiBinPath) &&
                   !string.IsNullOrEmpty(_buildConfig.OutputPath) &&
                   hasInputFile;
        }

        private async void ExecuteBuildFirmware(object? parameter)
        {
            if (IsBuilding)
            {
                MessageBox.Show("Packaging is already in progress.", "Warning",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 如果 RES.BIN 路径未设置，使用当前打开的文件
            if (string.IsNullOrEmpty(_buildConfig.ResBinPath) && !string.IsNullOrEmpty(_currentFilePath))
            {
                _buildConfig.ResBinPath = _currentFilePath;
            }

            // 验证配置
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
                // 自动检测并设置输入类型（如果未明确选择）
                if (string.IsNullOrEmpty(_buildConfig.ElfPath) && string.IsNullOrEmpty(_buildConfig.BinPath))
                {
                    // 尝试自动检测可用的文件
                    var appDir = AppDomain.CurrentDomain.BaseDirectory;
                    var outputDir = _buildConfig.OutputPath;

                    // 优先检查 BIN 文件（更快）
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

                    // 如果没有找到 BIN，再检查 ELF
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

                // 检查是否有未保存的修改，如果有则使用内存中的数据
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

                var result = await _firmwareBuilder.BuildAsync();

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
                // 清理临时文件
                _firmwareBuilder?.Cleanup();

                IsBuilding = false;
                BuildProgress = 0;
            }
        }

        private void OnBuildProgressChanged(object? sender, BuildProgressEventArgs e)
        {
            // 确保在 UI 线程更新
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
                _buildConfig.InputType = FirmwareInputType.Elf;  // 自动切换到 ELF 类型

                StatusMessage = $"ELF file selected: {Path.GetFileName(dialog.FileName)}";

                // 通知命令状态更新
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
                _buildConfig.InputType = FirmwareInputType.Bin;  // 自动切换到 BIN 类型

                StatusMessage = $"BIN file selected: {Path.GetFileName(dialog.FileName)}";

                // 通知命令状态更新
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

                // 通知命令状态更新
                (BuildFirmwareCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private void ExecuteSelectOutputPath(object? parameter)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select Output Directory (must contain MakeSPIBin.exe)";
                dialog.UseDescriptionForTitle = true;

                // 如果已有路径，设置为初始目录
                if (!string.IsNullOrEmpty(_buildConfig.OutputPath) && Directory.Exists(_buildConfig.OutputPath))
                {
                    dialog.SelectedPath = _buildConfig.OutputPath;
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    // 验证目录中是否有 MakeSPIBin.exe
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
                            return;  // 用户取消选择
                        }
                    }

                    _buildConfig.OutputPath = dialog.SelectedPath;
                    StatusMessage = $"Output directory selected: {dialog.SelectedPath}";

                    // 通知命令状态更新
                    (BuildFirmwareCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 判断是否可以执行 Font 替换
        /// </summary>
        private bool CanExecuteReplaceFont(object? parameter)
        {
            return SelectedResource != null && IsFontResource(SelectedResource);
        }

        /// <summary>
        /// 执行 Font 资源替换
        /// </summary>
        private void ExecuteReplaceFont(object? parameter)
        {
            if (SelectedResource == null || _parser == null)
                return;

            // 确保选中的是字体资源
            if (!IsFontResource(SelectedResource))
            {
                MessageBox.Show("Please select a font resource first.",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 打开字体替换对话框
            var dialog = new ResBinManager.Views.FontReplaceDialog();
            dialog.SetCurrentFontInfo(FontData, FontIndex, FontInfo);
            dialog.Owner = System.Windows.Application.Current.MainWindow;

            if (dialog.ShowDialog() != true)
                return;

            // 获取新文件数据
            var newFontData = dialog.NewFontData;
            var newFontIndex = dialog.NewFontIndex;

            if (newFontData == null || newFontIndex == null)
            {
                MessageBox.Show("Invalid font data.", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            StatusMessage = "Replacing font resources...";

            try
            {
                var writer = new ResBinWriter(_currentFileData!, _currentTableOffset,
                                            _parser.GetResourceTable());

                // 替换 resfont.bin (ID 79)
                if (!writer.ReplaceResource(79, newFontData))
                {
                    throw new Exception($"Failed to replace resfont.bin: {writer.ErrorMessage}");
                }

                // 替换 resfontidx.bin (ID 80)
                if (!writer.ReplaceResource(80, newFontIndex))
                {
                    throw new Exception($"Failed to replace resfontidx.bin: {writer.ErrorMessage}");
                }

                // 更新数据
                _currentFileData = writer.GetData();

                // 更新 ViewModel 状态
                FontData = newFontData;
                FontIndex = newFontIndex;
                LoadFontForPreview(); // 重新加载预览

                // 标记两个资源为已修改
                var resfont = Resources.FirstOrDefault(r => r != null && r.Id == 79);
                var resfontidx = Resources.FirstOrDefault(r => r != null && r.Id == 80);

                if (resfont != null) resfont.IsModified = true;
                if (resfontidx != null) resfontidx.IsModified = true;

                // 通知 Preview 命令状态更新
                (PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();

                StatusMessage = "✓ Font resources replaced successfully";

                MessageBox.Show(
                    "Font resources replaced successfully!\n\n" +
                    "Both resfont.bin and resfontidx.bin have been updated.\n" +
                    "Don't forget to save the modified file.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Font replacement failed";
            }
        }

        #endregion

        // ==================== 配置管理相关方法 ====================
        #region Configuration Management Commands
        private bool CanExecuteLoadConfig(object? parameter)
        {
            return IsDestBinMode && !string.IsNullOrEmpty(_currentFilePath);
        }

        private void ExecuteLoadConfig(object? parameter)
        {
            if (!IsDestBinMode || string.IsNullOrEmpty(_currentFilePath))
            {
                MessageBox.Show("请先打开 DestBin.bin 文件", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "正在解析配置区...";

                // 使用选中的项目类型解析配置
                FirmwareConfigData = ConfigParser.ParseConfigFromDestBin(_currentFilePath, SelectedProjectType);

                if (FirmwareConfigData == null || FirmwareConfigData.ConfigAddress == 0)
                {
                    StatusMessage = "配置区解析失败";
                    MessageBox.Show($"配置区解析失败\n{FirmwareConfigData?.StatusMessage}",
                                  "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                ConfigItems.Clear();

                if (!FirmwareConfigData.IsValid)
                {
                    // 如果已经找到了 RES.H 文件，说明项目结构完整，直接加载默认配置
                    if (false)//(_resHParser != null && _resHParser.IsParsed)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LoadConfig] RES.H found, loading default config for project: {SelectedProjectType}");
                        ConfigWriter.ResetToDefaults(FirmwareConfigData);
                        IsConfigModified = true;
                        (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    }
                    else
                    {
                        // 未找到 RES.H，询问用户处理方式
                        var result = MessageBox.Show(
                            "配置区为空白或无效，请选择处理方式：\n\n" +
                            "是(Y) - 加载默认配置（基于项目类型）\n" +
                            "否(N) - 从 config.c 文件加载配置\n" +
                            "取消 - 保持空白配置",
                            "配置区空白",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            ConfigWriter.ResetToDefaults(FirmwareConfigData);
                            IsConfigModified = true;
                            (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                        }
                        else if (result == MessageBoxResult.No)
                        {
                            LoadConfigFromCFile();
                        }
                    }
                }

                var items = ConfigParser.BuildConfigItemList(FirmwareConfigData);
                foreach (var item in items)
                {
                    ConfigItems.Add(item);
                }

                //IsConfigModified = !FirmwareConfigData.IsValid;

                if (FirmwareConfigData.IsValid)
                {
                    StatusMessage = $"配置加载成功 (项目: {SelectedProjectType}, 地址: 0x{FirmwareConfigData.ConfigAddress:X})";
                }
                else if (items.Count > 0)
                {
                    StatusMessage = $"已加载默认配置 (项目: {SelectedProjectType}, 地址: 0x{FirmwareConfigData.ConfigAddress:X})，请修改后保存";
                }
                else
                {
                    StatusMessage = $"配置区空白 (项目: {SelectedProjectType}, 地址: 0x{FirmwareConfigData.ConfigAddress:X})";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "配置加载失败";
                MessageBox.Show($"配置加载失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 从 config.c 文件加载配置
        /// </summary>
        private void LoadConfigFromCFile()
        {
            if (FirmwareConfigData == null)
                return;

            var openFileDialog = new OpenFileDialog
            {
                Filter = "C源文件 (*.c)|*.c|所有文件 (*.*)|*.*",
                Title = "选择 config.c 文件",
                InitialDirectory = Path.GetDirectoryName(_currentFilePath) ?? string.Empty
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    IsLoading = true;
                    var parsedItems = ConfigCParser.ParseFromFile(openFileDialog.FileName);
                    if (parsedItems.Count > 0)
                    {
                        ConfigWriter.ResetFromCParsedItems(FirmwareConfigData, parsedItems);

                        //RefreshConfigItems();

                        IsConfigModified = true;
                        (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                        StatusMessage = $"已从 config.c 加载配置 ({parsedItems.Count} 项)，请修改后保存到固件";
                        System.Diagnostics.Debug.WriteLine($"[VM] Loaded {parsedItems.Count} config items from {openFileDialog.FileName}");
                        MessageBox.Show($"成功从 config.c 加载配置 ({parsedItems.Count} 项)\n\n您可以在界面上修改配置，然后点击保存按钮将配置写入固件。",
                                      "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("未从 config.c 文件中解析到任何配置项", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"加载 config.c 文件失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        /// <summary>
        /// 刷新配置项列表显示
        /// </summary>
        private void RefreshConfigItems()
        {
            if (FirmwareConfigData == null)
                return;

            try
            {
                ConfigItems.Clear();

                var items = ConfigParser.BuildConfigItemList(FirmwareConfigData);
                foreach (var item in items)
                {
                    ConfigItems.Add(item);
                }

                System.Diagnostics.Debug.WriteLine($"[VM] Refreshed {items.Count} config items");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VM] RefreshConfigItems failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 将界面上修改的配置项同步回 FirmwareConfigData.Flags 数组
        /// 确保保存时使用的是用户修改后的数据
        /// </summary>
        private void SyncConfigItemsToFlags()
        {
            if (FirmwareConfigData == null || ConfigItems == null)
                return;

            foreach (var item in ConfigItems)
            {
                int index = (int)item.Id;
                if (index >= 0 && index < FirmwareConfigData.Flags.Length)
                {
                    if (FirmwareConfigData.Flags[index] != item.Value)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SyncConfig] ConfigId={item.Id} ({item.Name}): 0x{FirmwareConfigData.Flags[index]:X8} -> 0x{item.Value:X8}");
                        FirmwareConfigData.Flags[index] = item.Value;
                    }
                }
            }

            FirmwareConfigData.CheckSum = FirmwareConfigData.CalculateCheckSum();
            System.Diagnostics.Debug.WriteLine($"[SyncConfig] Recalculated CheckSum: 0x{FirmwareConfigData.CheckSum:X8}");
        }

        /// <summary>
        /// 使用新的项目类型重新加载配置
        /// </summary>
        private void ReloadConfigWithNewProjectType()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
                return;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[VM] Reloading config with project type: {SelectedProjectType}");

                // 使用新的项目类型重新解析
                var newConfigData = ConfigParser.ParseConfigFromDestBin(_currentFilePath, SelectedProjectType);

                if (newConfigData == null || newConfigData.ConfigAddress == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[VM] Reload failed: invalid config data");
                    return;
                }

                // 保留原有的配置数据地址和有效性状态
                newConfigData.ConfigAddress = FirmwareConfigData?.ConfigAddress ?? newConfigData.ConfigAddress;

                // 如果原配置有效，新配置也保持有效
                if (FirmwareConfigData?.IsValid == true)
                {
                    newConfigData.IsValid = true;
                }

                FirmwareConfigData = newConfigData;

                // 重新构建配置项列表
                ConfigItems.Clear();
                var items = ConfigParser.BuildConfigItemList(FirmwareConfigData);
                foreach (var item in items)
                {
                    ConfigItems.Add(item);
                }

                StatusMessage = $"配置已重新加载 (项目: {SelectedProjectType}, 配置项: {items.Count})";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VM] Reload failed: {ex.Message}");
                StatusMessage = $"配置重新加载失败: {ex.Message}";
            }
        }

        private bool CanExecuteSaveConfig(object? parameter)
        {
            return IsConfigModified && FirmwareConfigData != null && !string.IsNullOrEmpty(_currentFilePath);
        }

        private void ExecuteSaveConfig(object? parameter)
        {
            if (FirmwareConfigData == null || string.IsNullOrEmpty(_currentFilePath))
                return;

            try
            {
                IsLoading = true;
                StatusMessage = "正在保存配置...";

                SyncConfigItemsToFlags();

                System.Diagnostics.Debug.WriteLine($"[SaveConfig] ConfigAddress=0x{FirmwareConfigData.ConfigAddress:X}, IsValid={FirmwareConfigData.IsValid}");
                System.Diagnostics.Debug.WriteLine($"[SaveConfig] CheckSum=0x{FirmwareConfigData.CheckSum:X8}");
                System.Diagnostics.Debug.WriteLine($"[SaveConfig] Language=0x{FirmwareConfigData.Flags[7]:X8}");

                string backupPath = _currentFilePath + ".config_backup-" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                if (File.Exists(_currentFilePath))
                {
                    File.Copy(_currentFilePath, backupPath, true);
                }

                byte[]? firmwareDataWithResources = null;

                if (_destBinParser != null && _currentFileData != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveConfig] Applying resource changes before saving config...");

                    if (_destBinParser.ReplaceResBin(_currentFileData, keepSize: false))
                    {
                        firmwareDataWithResources = _destBinParser.GetDestBinData();
                        System.Diagnostics.Debug.WriteLine($"[SaveConfig] Resource changes applied, firmware size: {firmwareDataWithResources?.Length}");

                        System.Diagnostics.Debug.WriteLine($"[SaveConfig] Recalculating config address after resource replacement...");
                        System.Diagnostics.Debug.WriteLine($"  Old ConfigAddress: 0x{FirmwareConfigData.ConfigAddress:X}");
                        System.Diagnostics.Debug.WriteLine($"  New ResAddress: 0x{_destBinParser.ResAddress:X}");
                        System.Diagnostics.Debug.WriteLine($"  New ResSize: 0x{_destBinParser.ResSize:X}");

                        uint newConfigAddress = _destBinParser.CalculateConfigAddress();
                        FirmwareConfigData.ConfigAddress = newConfigAddress;
                        System.Diagnostics.Debug.WriteLine($"  New ConfigAddress: 0x{newConfigAddress:X}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveConfig] Failed to apply resource changes: {_destBinParser.ErrorMessage}");
                        throw new InvalidOperationException($"应用资源修改失败: {_destBinParser.ErrorMessage}");
                    }
                }

                if (ConfigWriter.SaveConfigToDestBin(_currentFilePath, FirmwareConfigData, firmwareDataWithResources))
                {
                    IsConfigModified = false;
                    (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    StatusMessage = "✅配置保存成功";
                    System.Diagnostics.Debug.WriteLine($"[SaveConfig] Saved successfully");

                    if (_destBinParser != null)
                    {
                        byte[] savedData = File.ReadAllBytes(_currentFilePath);
                        _destBinParser.UpdateDestBinData(savedData);
                        // 从 _destBinParser 提取 RES.BIN 数据，保持 _currentFileData 语义一致
                        _currentFileData = _destBinParser.ExtractResBin();
                        System.Diagnostics.Debug.WriteLine($"[SaveConfig] Updated _currentFileData (RES.BIN: {_currentFileData?.Length ?? 0} bytes) and _destBinParser");
                    }

                    MessageBox.Show($"配置已成功保存到固件文件中。\n\n备份文件: {Path.GetFileName(backupPath)}",
                                  "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = "配置保存失败";
                    System.Diagnostics.Debug.WriteLine($"[SaveConfig] Save failed");
                    MessageBox.Show("配置保存失败。", "错误",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "配置保存异常";
                System.Diagnostics.Debug.WriteLine($"[SaveConfig] Exception: {ex.Message}");
                MessageBox.Show($"配置保存异常:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyConfigChangesToDestBin()
        {
            if (!IsConfigModified || FirmwareConfigData == null || _destBinParser == null)
                return;

            System.Diagnostics.Debug.WriteLine($"[ApplyConfig] Applying config changes...");
            SyncConfigItemsToFlags();

            byte[] firmwareData = _destBinParser.GetDestBinData();
            uint configAddress = _destBinParser.CalculateConfigAddress();
            FirmwareConfigData.ConfigAddress = configAddress;

            if (configAddress + 512 > firmwareData.Length)
            {
                System.Diagnostics.Debug.WriteLine($"[ApplyConfig] Config address 0x{configAddress:X} exceeds firmware size {firmwareData.Length}");
                throw new InvalidOperationException($"配置区地址 0x{configAddress:X} 超出固件大小 {firmwareData.Length}");
            }

            byte[] configBuffer = new byte[512];
            for (int i = 0; i < 127; i++)
            {
                byte[] flagBytes = BitConverter.GetBytes(FirmwareConfigData.Flags[i]);
                Array.Copy(flagBytes, 0, configBuffer, i * 4, 4);
            }
            uint checkSum = FirmwareConfigData.CalculateCheckSum();
            byte[] checkSumBytes = BitConverter.GetBytes(checkSum);
            Array.Copy(checkSumBytes, 0, configBuffer, 127 * 4, 4);

            Array.Copy(configBuffer, 0, firmwareData, (int)configAddress, 512);
            _destBinParser.UpdateDestBinData(firmwareData);

            System.Diagnostics.Debug.WriteLine($"[ApplyConfig] Config data written to address 0x{configAddress:X}");
        }

        private bool CanExecuteResetConfig(object? parameter)
        {
            return FirmwareConfigData != null;
        }

        private void ExecuteResetConfig(object? parameter)
        {
            if (FirmwareConfigData == null)
                return;

            var template = ConfigTemplateManager.CurrentTemplate;
            var result = MessageBox.Show(
                $"确定要恢复所有配置为默认值吗？\n\n当前方案: {template.Name}\n此操作将所有配置项恢复为出厂默认设置。",
                "确认恢复默认配置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                ConfigWriter.ResetToDefaults(FirmwareConfigData, _selectedConfigTemplate);

                ConfigItems.Clear();
                var items = ConfigParser.BuildConfigItemList(FirmwareConfigData);
                foreach (var item in items)
                {
                    ConfigItems.Add(item);
                }

                IsConfigModified = true;
                (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                StatusMessage = $"✅ 已恢复默认配置 ({template.Name})";
            }
            catch (Exception ex)
            {
                StatusMessage = "恢复默认配置失败";
                MessageBox.Show($"恢复默认配置失败:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanExecuteExportConfig(object? parameter)
        {
            return FirmwareConfigData != null && ConfigItems.Count > 0;
        }

        private void ExecuteExportConfig(object? parameter)
        {
            if (FirmwareConfigData == null || ConfigItems.Count == 0)
                return;

            var dialog = new SaveFileDialog
            {
                FileName = "firmware_config.txt",
                Filter = "Text files|*.txt|All files|*.*",
                Title = "导出配置文件"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                string configText = ConfigWriter.ExportConfigAsText(FirmwareConfigData, ConfigItems.ToList());
                File.WriteAllText(dialog.FileName, configText, Encoding.UTF8);
                StatusMessage = $"✅ 配置已导出到: {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusMessage = "导出配置失败";
                MessageBox.Show($"导出配置失败:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 映射配置管理命令

        /// <summary>
        /// 加载映射配置
        /// </summary>
        private void ExecuteLoadMappingConfig(object? parameter)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON files|*.json|All files|*.*",
                Title = "加载映射配置文件"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var mapping = ProjectConfigMapping.LoadFromJsonFile(dialog.FileName);
                if (mapping != null)
                {
                    ProjectConfigMappingDatabase.AddOrUpdateMapping(mapping);
                    StatusMessage = $"✅ 已加载映射配置: {mapping.ProjectName}";

                    // 如果当前项目类型与加载的映射匹配，重新加载配置
                    if (FirmwareConfigData != null && FirmwareConfigData.ProjectType == mapping.ProjectType)
                    {
                        LoadConfigCommand.Execute(null);
                    }
                }
                else
                {
                    StatusMessage = "❌ 加载映射配置失败";
                    MessageBox.Show("无法加载映射配置文件，请检查文件格式。",
                                  "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "加载映射配置失败";
                MessageBox.Show($"加载映射配置失败:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 是否可以保存映射配置
        /// </summary>
        private bool CanExecuteSaveMappingConfig(object? parameter)
        {
            return FirmwareConfigData != null;
        }

        /// <summary>
        /// 保存映射配置
        /// </summary>
        private void ExecuteSaveMappingConfig(object? parameter)
        {
            if (FirmwareConfigData?.Mapping == null)
                return;

            var dialog = new SaveFileDialog
            {
                FileName = $"{FirmwareConfigData.ProjectType}_mapping.json",
                Filter = "JSON files|*.json|All files|*.*",
                Title = "保存映射配置文件"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                if (FirmwareConfigData.Mapping.SaveToJsonFile(dialog.FileName))
                {
                    StatusMessage = $"✅ 映射配置已保存: {Path.GetFileName(dialog.FileName)}";
                }
                else
                {
                    StatusMessage = "❌ 保存映射配置失败";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "保存映射配置失败";
                MessageBox.Show($"保存映射配置失败:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 重新加载所有映射
        /// </summary>
        private void ExecuteReloadAllMappings(object? parameter)
        {
            try
            {
                ProjectConfigMappingDatabase.ReloadMappings();
                StatusMessage = "✅ 已重新加载所有映射配置";

                // 如果当前有配置数据，重新加载配置
                if (FirmwareConfigData != null)
                {
                    LoadConfigCommand.Execute(null);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "重新加载映射配置失败";
                MessageBox.Show($"重新加载映射配置失败:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 是否可以生成示例配置
        /// </summary>
        private bool CanExecuteGenerateSampleMapping(object? parameter)
        {
            return FirmwareConfigData != null;
        }

        /// <summary>
        /// 生成示例配置
        /// </summary>
        private void ExecuteGenerateSampleMapping(object? parameter)
        {
            if (FirmwareConfigData == null)
                return;

            var dialog = new SaveFileDialog
            {
                FileName = $"{FirmwareConfigData.ProjectType}_sample.json",
                Filter = "JSON files|*.json|All files|*.*",
                Title = "生成示例映射配置文件"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                if (ProjectMappingConfigLoader.GenerateSampleConfig(FirmwareConfigData.ProjectType, dialog.FileName))
                {
                    StatusMessage = $"✅ 示例配置已生成: {Path.GetFileName(dialog.FileName)}";
                }
                else
                {
                    StatusMessage = "❌ 生成示例配置失败";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "生成示例配置失败";
                MessageBox.Show($"生成示例配置失败:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 从源码生成配置映射文件
        /// </summary>
        private void ExecuteGenerateFromSource(object? parameter)
        {
            // 选择项目目录
            var folderDialog = new FolderBrowserDialog
            {
                Description = "选择包含 config.c 和 config.h 的项目目录"
            };

            if (folderDialog.ShowDialog() != DialogResult.OK)
                return;

            string projectPath = folderDialog.SelectedPath;

            // 查找 config.c
            string? configCPath = ConfigSourceParser.FindConfigC(projectPath);
            if (string.IsNullOrEmpty(configCPath))
            {
                MessageBox.Show("未找到 config.c 文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 查找 config.h（可选）
            string? configHPath = ConfigHParser.FindConfigH(projectPath);

            // 选择输出文件
            var saveDialog = new SaveFileDialog
            {
                FileName = $"{Path.GetFileName(projectPath)}.json",
                Filter = "JSON files|*.json|All files|*.*",
                Title = "保存配置映射文件",
                InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mappings")
            };

            if (saveDialog.ShowDialog() != true)
                return;

            try
            {
                // 解析源码
                var parser = new ConfigSourceParser();
                var parseResult = parser.Parse(configCPath, configHPath);

                if (!parseResult.Success)
                {
                    MessageBox.Show($"解析失败:\n{parseResult.ErrorMessage}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 生成 JSON
                var generator = new ConfigJsonGenerator();
                if (generator.Generate(parseResult, saveDialog.FileName))
                {
                    StatusMessage = $"✅ 配置映射已生成: {Path.GetFileName(saveDialog.FileName)}";
                    MessageBox.Show($"成功生成配置映射文件:\n{saveDialog.FileName}\n\n共提取 {parseResult.ConfigItems.Count} 个配置项",
                                  "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = "❌ 生成配置映射失败";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "生成配置映射失败";
                MessageBox.Show($"生成配置映射失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 配置项修改方法
        /// <summary>
        /// 更新配置项的值
        /// </summary>
        public void UpdateConfigItemValue(FirmwareConfigItem item, uint newValue)
        {
            if (FirmwareConfigData == null)
                return;

            if (ConfigWriter.UpdateConfigValue(FirmwareConfigData, item.Id, newValue))
            {
                item.Value = newValue;
                item.ValueDisplay = GetConfigValueDisplay(item.Id, newValue);
                IsConfigModified = true;
                (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                StatusMessage = $"已修改 {item.Name} = {item.ValueDisplay}";
            }
        }

        private string GetConfigValueDisplay(ConfigId configId, uint value)
        {
            return configId switch
            {
                ConfigId.CONFIG_ID_LANGUAGE => ConfigParser_BuildConfigItemList_GetLanguageDisplay(value),
                ConfigId.CONFIG_ID_VIDEO_RESOLUTION => ConfigParser_BuildConfigItemList_GetResolutionDisplay(value),
                ConfigId.CONFIG_ID_NETWORK_SPEED => ConfigParser_BuildConfigItemList_GetNetworkSpeedDisplay(value),
                _ => $"0x{value:X8}"
            };
        }

        private string ConfigParser_BuildConfigItemList_GetLanguageDisplay(uint value)
        {
            return value switch
            {
                0 => "中文",
                1 => "English",
                2 => "日本語",
                3 => "한국어",
                _ => $"未知({value})"
            };
        }

        private string ConfigParser_BuildConfigItemList_GetResolutionDisplay(uint value)
        {
            return value switch
            {
                0 => "1080P",
                1 => "720P",
                2 => "4K",
                _ => $"未知({value})"
            };
        }

        private string ConfigParser_BuildConfigItemList_GetNetworkSpeedDisplay(uint value)
        {
            return value switch
            {
                0 => "100Mbps",
                1 => "10Mbps",
                _ => $"未知({value})"
            };
        }

        #endregion

        /// <summary>
        /// 保存用户设置
        /// </summary>
        private void SaveUserSettings()
        {
            try
            {
                var settings = new AppSettings
                {
                    SelectedConfigTemplate = _selectedConfigTemplate,
                    LastOpenedFilePath = _currentFilePath,
                    LastOutputDirectory = _buildConfig.OutputPath
                };
                AppSettingsManager.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveUserSettings] Failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载用户设置
        /// </summary>
        private void LoadUserSettings()
        {
            try
            {
                var settings = AppSettingsManager.LoadSettings();
                if (settings != null)
                {
                    _selectedConfigTemplate = settings.SelectedConfigTemplate;
                    ConfigTemplateManager.CurrentTemplateId = _selectedConfigTemplate;

                    if (!string.IsNullOrEmpty(settings.LastOutputDirectory) && Directory.Exists(settings.LastOutputDirectory))
                    {
                        _buildConfig.OutputPath = settings.LastOutputDirectory;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadUserSettings] Failed: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 简单的 RelayCommand 实现
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        /// <summary>
        /// 手动触发 CanExecuteChanged 事件
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
