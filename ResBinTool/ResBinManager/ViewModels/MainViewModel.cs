using ResBinManager.Core;
using ResBinManager.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ResBinManager.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel
    /// </summary>
    public partial class MainViewModel : INotifyPropertyChanged
    {
        #region Fields
        private ObservableCollection<ResourceItem> _resources = null!;
        private ResourceItem _selectedResource;
        private string _statusMessage = string.Empty;
        private bool _isLoading;
        private bool _isBuilding;
        private int _buildProgress;
        private string _buildLog = string.Empty;

        private ResBinParser _parser;
        private DestBinParser _destBinParser;  // 新增：DestBin.bin 解析器
        private ResHParser _resHParser;  // 新增：RES.H 解析器
        private byte[] _currentFileData;
        private uint _currentTableOffset;
        private string _currentFilePath = string.Empty;
        private bool _currentFileExists;
        private bool _isDestBinMode = true;  // 新增：是否为 DestBin.bin 模式
        private string _firmwareVersion = "";  // 固件版本号
        private uint _magicKey = 0;   // ✅ P3: MAGICKEY常量值（替换_firmwareSerial）
        private string _buildTime = "";  // 固件编译时间
        private Views.TimeSyncWindow? _timeSyncWindow;
        private FirmwareBuildConfig _buildConfig;
        private FirmwareBuilder _firmwareBuilder;

        // WAV 播放相关
        private WavPlayer _wavPlayer;
        private WavInfo _wavInfo;
        private float _wavVolume = 80.0f; // 默认音量 80%

        // Font 预览相关
        private FontInfo _fontInfo;
        public byte[] FontData { get; private set; }
        public byte[] FontIndex { get; private set; }
        public byte[] FontBinData { get; private set; }

        // Palette 预览相关
        private PaletteInfo _paletteInfo;
        public byte[] PaletteData { get; private set; }

        // OSD 预览相关
        private OsdInfo _osdInfo;
        public byte[] OsdData { get; private set; }
        private string _osdOriginalIconDirectory;

        // Text 预览相关
        private string _textContent = string.Empty;

        // 配置管理相关
        private FirmwareConfigData _firmwareConfigData;
        private ObservableCollection<FirmwareConfigItem> _configItems = new();
        private bool _isConfigModified;
        private ConfigTemplateId _selectedConfigTemplate = ConfigTemplateId.Default;
        private ProjectType _selectedProjectType = ProjectType.Unknown;
        private string _loadedXmlFilePath = string.Empty;

        #endregion

        #region Properties

        /// <summary>
        /// 当前文件数据（用于 UI 层提取资源数据进行预览）
        /// </summary>
        public byte[]? CurrentFileData => _currentFileData;

        public ResHParser? ResHParser => _resHParser;

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
                PaletteInfo = null;
                PaletteData = null;
                OsdInfo = null;
                OsdData = null;
                _osdOriginalIconDirectory = null;
                TextContent = string.Empty;

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
                else if (value?.Type == ResourceType.Palette)
                {
                    System.Diagnostics.Debug.WriteLine("[VM] Loading Palette preview");
                    LoadPaletteForPreview();
                }
                else if (value?.Type == ResourceType.OsdSource)
                {
                    System.Diagnostics.Debug.WriteLine("[VM] Loading OSD preview");
                    LoadOsdForPreview();
                }
                else if (value?.Type == ResourceType.Text)
                {
                    System.Diagnostics.Debug.WriteLine("[VM] Loading Text preview");
                    LoadTextForPreview();
                }
                else if (value != null)
                {
                    // 对于其他资源类型（图片、二进制等），触发预览事件
                    var typeLabel = value.Type.ToString();
                    var resolution = value.Width > 0 && value.Height > 0 ? $", {value.Width}×{value.Height}" : "";
                    StatusMessage = $"{typeLabel} loaded: {value.Name} — {value.SizeDisplay} {resolution}";
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
                (ReplaceCommand as RelayCommand)?.RaiseCanExecuteChanged();   // 新增
                (RevertCommand as RelayCommand)?.RaiseCanExecuteChanged();    // 新增
                (ExportCommand as RelayCommand)?.RaiseCanExecuteChanged();    // 新增
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
            set
            {
                _isLoading = value;
                OnPropertyChanged();
                (LoadXmlConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RefreshConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
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

        private ObservableCollection<FontLanguageItem> _fontLanguages = new();
        public ObservableCollection<FontLanguageItem> FontLanguages
        {
            get => _fontLanguages;
            set { _fontLanguages = value; OnPropertyChanged(); }
        }

        private int _selectedFontLanguageIndex;
        public int SelectedFontLanguageIndex
        {
            get => _selectedFontLanguageIndex;
            set
            {
                _selectedFontLanguageIndex = value;
                OnPropertyChanged();
                LoadFontStringsForLanguage(value);
            }
        }

        private ObservableCollection<FontStringItem> _fontStrings = new();
        public ObservableCollection<FontStringItem> FontStrings
        {
            get => _fontStrings;
            set { _fontStrings = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 调色板信息
        /// </summary>
        public PaletteInfo? PaletteInfo
        {
            get => _paletteInfo;
            set { _paletteInfo = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// OSD 屏幕显示资源信息
        /// </summary>
        public OsdInfo? OsdInfo
        {
            get => _osdInfo;
            set { _osdInfo = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Text 资源内容
        /// </summary>
        public string TextContent
        {
            get => _textContent;
            set { _textContent = value; OnPropertyChanged(); }
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
        /// 固件编译时间（仅 DestBin.bin 模式，从程序代码段中提取）
        /// </summary>
        public string? BuildTime
        {
            get => _buildTime;
            set { _buildTime = value; OnPropertyChanged(); }
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

        private bool _showDisabledItems = false;
        public bool ShowDisabledItems
        {
            get => _showDisabledItems;
            set
            {
                _showDisabledItems = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 配置是否已修改
        /// </summary>
        public bool IsConfigModified
        {
            get => _isConfigModified;
            set
            {
                _isConfigModified = value;
                OnPropertyChanged();
                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();  // 新增
            }
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

        #region Commands
        public ICommand ShowTimeSyncCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand ReplaceCommand { get; }
        public ICommand ReplaceFontCommand { get; }  // 新增
        public ICommand RevertCommand { get; }  // 恢复原始数据命令
        public ICommand ExportCommand { get; }
        public ICommand ExportOsdIconsCommand { get; }
        public ICommand ReplaceOsdIconCommand { get; }
        public ICommand ApplyTextEditCommand { get; }  // 新增：应用文本编辑修改
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

        // XML配置加载命令
        public ICommand LoadXmlConfigCommand { get; }
        public ICommand RefreshConfigCommand { get; }

        #endregion

        public MainViewModel()
        {
            Resources = new ObservableCollection<ResourceItem>();
            _statusMessage = "Ready. Open a DEST.BIN file to start.";
            _buildConfig = new FirmwareBuildConfig();

            //// 设置默认输出目录为程序运行目录下的 output 子目录
            //var appDir = AppDomain.CurrentDomain.BaseDirectory;
            //var defaultOutputDir = Path.Combine(appDir, "output");
            //if (!Directory.Exists(defaultOutputDir))
            //{
            //    Directory.CreateDirectory(defaultOutputDir);
            //}
            //_buildConfig.OutputPath = defaultOutputDir;

            // 尝试自动检测并设置 MakeSPIBin.exe 路径
            //AutoDetectMakeSpiBin();

            OpenCommand = new RelayCommand(ExecuteOpen, CanExecuteOpen);
            ShowTimeSyncCommand = new RelayCommand(ExecuteShowTimeSync);
            ReplaceCommand = new RelayCommand(ExecuteReplace, CanExecuteReplace);
            ReplaceFontCommand = new RelayCommand(ExecuteReplaceFont, CanExecuteReplaceFont);  // 新增
            RevertCommand = new RelayCommand(ExecuteRevert, CanExecuteRevert);  // 恢复命令
            ExportCommand = new RelayCommand(ExecuteExport, CanExecuteExport);
            ExportOsdIconsCommand = new RelayCommand(ExecuteExportOsdIcons, CanExecuteExportOsdIcons);
            ReplaceOsdIconCommand = new RelayCommand(ExecuteReplaceOsdIcon, CanExecuteReplaceOsdIcon);
            ApplyTextEditCommand = new RelayCommand(ExecuteApplyTextEdit, CanExecuteApplyTextEdit);  // 新增
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
            GenerateFromSourceCommand = new RelayCommand(ExecuteGenerateFromSource, CanExecuteGenerateFromSource);  // 新增

            // XML配置加载命令初始化
            LoadXmlConfigCommand = new RelayCommand(_ => ExecuteLoadXmlConfig(), CanExecuteLoadXmlConfig);
            RefreshConfigCommand = new RelayCommand(_ => ExecuteRefreshConfig(), _ => CanExecuteRefreshConfig());
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

        #region OpenCommand
        private bool CanExecuteOpen(object? parameter) => !IsLoading;

        /// <summary>
        /// 智能打开文件（自动检测类型）
        /// </summary>
        private async void ExecuteOpen(object? parameter)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Binary files|*.bin|All files|*.*",
                Title = "Open RES.BIN or DestBin.bin File"
            };

            if (dialog.ShowDialog() == true)
            {
                await LoadFileSmartAsync(dialog.FileName);
            }
        }

        /// <summary>
        /// 智能加载文件（通过文件名自动识别 RES.BIN 或 DestBin.bin）
        /// 异步执行：文件读取和解析在后台线程，UI 更新在主线程
        /// </summary>
        private async Task LoadFileSmartAsync(string filePath)
        {
            // 清理之前的状态
            CleanupPreviousLoad();

            IsLoading = true;
            _currentFilePath = filePath;
            _currentFileExists = true;
            StatusMessage = "Loading file...";

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

                System.Diagnostics.Debug.WriteLine($"[LoadFileSmartAsync] File: {fileName}, Detected as DestBin: {isDestBin}");

                if (isDestBin)
                {
                    // 尝试作为 DestBin.bin 加载
                    if (!await TryLoadAsDestBinAsync(filePath))
                    {
                        // 如果 DestBin 加载失败，回退到 RES.BIN 模式
                        System.Diagnostics.Debug.WriteLine("[LoadFileSmartAsync] DestBin load failed, falling back to RES.BIN mode");
                        await LoadResBinAsync(filePath);
                    }
                }
                else
                {
                    // 作为普通 RES.BIN 加载
                    await LoadResBinAsync(filePath);
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
                _destBinParser.Dispose();
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

            // 清理配置数据
            if (FirmwareConfigData != null)
            {
                System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Clearing FirmwareConfigData");
                FirmwareConfigData = null;
            }

            // 清空配置项列表
            if (ConfigItems != null && ConfigItems.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[CleanupPreviousLoad] Clearing {ConfigItems.Count} config items");
                ConfigItems.Clear();
            }

            // 重置配置选项缓存（包括动态常量）
            System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Clearing ConfigOptionsCache");
            ConfigOptionsCache.Clear();

            // 重置状态
            _currentTableOffset = 0;
            IsDestBinMode = false;
            FirmwareVersion = null;
            MagicKey = 0;
            BuildTime = null;
            WavInfo = null;
            FontInfo = null;
            FontData = null;
            FontIndex = null;
            IsConfigModified = false;
            _currentFilePath = string.Empty;
            _currentFileExists = false;
            _loadedXmlFilePath = string.Empty;
            StatusMessage = string.Empty;

            System.Diagnostics.Debug.WriteLine("[CleanupPreviousLoad] Cleanup complete");
        }

        /// <summary>
        /// 尝试作为 DestBin.bin 异步加载
        /// 所有文件 I/O 和解析在后台线程执行，UI 更新在主线程
        /// 使用 ParseFromBytes 直接从内存解析，消除临时文件 I/O
        /// </summary>
        private async Task<bool> TryLoadAsDestBinAsync(string filePath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBinAsync] Loading: {filePath}");

                // 在后台线程执行所有文件读取和解析
                var result = await Task.Run(() =>
                {
                    var destBinParser = new DestBinParser();

                    if (!destBinParser.Load(filePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBinAsync] DestBinParser.Load() failed: {destBinParser.ErrorMessage}");
                        return (Success: false, DestBinParser: destBinParser, ResHParser: (ResHParser?)null,
                                Parser: (ResBinParser?)null, ResBinData: (byte[]?)null, FilteredResources: (List<ResourceItem>?)null);
                    }

                    System.Diagnostics.Debug.WriteLine("[TryLoadAsDestBinAsync] DestBinParser.Load() succeeded");

                    // 解析 RES.H 文件（如果存在）
                    ResHParser? resHParser = null;
                    var resHPath = ResHParser.AutoFindResH(filePath);
                    if (resHPath != null)
                    {
                        resHParser = new ResHParser();
                        if (resHParser.Parse(resHPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBinAsync] RES.H parsed successfully: {resHPath}");
                            resHParser.PrintSummary();
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[TryLoadAsDestBinAsync] RES.H parse failed, continuing without it");
                            resHParser = null;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[TryLoadAsDestBinAsync] RES.H not found, continuing without it");
                    }

                    // 提取 RES.BIN
                    var resBinData = destBinParser.ExtractResBin();
                    if (resBinData == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBinAsync] ExtractResBin() returned null: {destBinParser.ErrorMessage}");
                        return (Success: false, DestBinParser: destBinParser, ResHParser: resHParser,
                                Parser: (ResBinParser?)null, ResBinData: (byte[]?)null, FilteredResources: (List<ResourceItem>?)null);
                    }

                    System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBinAsync] Extracted RES.BIN: {resBinData.Length} bytes");

                    // 直接从内存解析 RES.BIN（无需临时文件）
                    string? destBinDir = Path.GetDirectoryName(filePath);
                    var parser = new ResBinParser(filePath, destBinDir);
                    System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBinAsync] Search base path for RES.H: {destBinDir}");

                    // 设置资源区基地址为 DestBin.bin 中 RES.BIN 的偏移
                    parser.SetResourceBaseAddress(destBinParser.ResBinOffset);

                    if (!parser.ParseFromBytes(resBinData))
                    {
                        System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBinAsync] ParseFromBytes() failed: {parser.ErrorMessage}");
                        return (Success: false, DestBinParser: destBinParser, ResHParser: resHParser,
                                Parser: parser, ResBinData: resBinData, FilteredResources: (List<ResourceItem>?)null);
                    }

                    System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBinAsync] ParseFromBytes() succeeded, Resources: {parser.Resources.Count}");

                    // 调试：检查第一个资源的数据
                    if (parser.Resources.Count > 0)
                    {
                        var firstResource = parser.Resources[0];
                        System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBinAsync] First resource: Name={firstResource.Name}, Type={firstResource.Type}, Offset=0x{firstResource.Offset:X}, Size={firstResource.Size}");
                    }

                    // 如果 RES.H 已解析，则根据 RES.H 过滤资源列表
                    List<ResourceItem> filteredResources;
                    if (resHParser != null && resHParser.IsParsed)
                    {
                        var definedIndices = new HashSet<int>(resHParser.GetAllDefinedIndices());
                        System.Diagnostics.Debug.WriteLine($"[FilterResources] RES.H defines {definedIndices.Count} resources");

                        filteredResources = new List<ResourceItem>();
                        int skippedCount = 0;
                        foreach (var resource in parser.Resources)
                        {
                            if (definedIndices.Contains((int)resource.Id))
                            {
                                filteredResources.Add(resource);
                            }
                            else
                            {
                                skippedCount++;
                            }
                        }
                        System.Diagnostics.Debug.WriteLine($"[FilterResources] Filtered: {filteredResources.Count} kept, {skippedCount} skipped");
                    }
                    else
                    {
                        filteredResources = parser.Resources.ToList();
                    }

                    return (Success: true, DestBinParser: destBinParser, ResHParser: resHParser,
                            Parser: parser, ResBinData: resBinData, FilteredResources: filteredResources);
                });

                // === UI 线程：更新绑定属性 ===

                _destBinParser = result.DestBinParser;
                _resHParser = result.ResHParser;
                _parser = result.Parser;

                if (!result.Success || result.Parser == null || result.FilteredResources == null || result.ResBinData == null)
                {
                    return false;
                }

                // 保存原始数据
                _currentFileData = result.ResBinData;
                _currentTableOffset = result.Parser.TableOffset;
                IsDestBinMode = true;

                // 设置版本信息
                FirmwareVersion = result.DestBinParser.FirmwareVersion;
                MagicKey = result.DestBinParser.MagicKey;
                BuildTime = result.DestBinParser.BuildTime;

                // 批量更新：一次性赋值新的 ObservableCollection（单次 PropertyChanged 通知）
                bool filteredByResH = result.ResHParser != null && result.ResHParser.IsParsed;
                Resources = new ObservableCollection<ResourceItem>(result.FilteredResources);

                string filterSuffix = filteredByResH ? " - Filtered by RES.H" : "";
                StatusMessage = $"Loaded {Resources.Count} resources from DestBin.bin ({Path.GetFileName(filePath)}){filterSuffix}";

                // 显示结构信息
                var structureInfo = result.DestBinParser.GetStructureInfo();
                System.Diagnostics.Debug.WriteLine(structureInfo);

                long fileSize = new FileInfo(filePath).Length;
                MessageBox.Show(
                    $"Successfully loaded {Resources.Count} resources from DestBin.bin!\n\n" +
                    $"File: {Path.GetFileName(filePath)}\n" +
                    $"Size: {fileSize:N0} bytes\n\n" +
                    $"{result.DestBinParser.ResBinSize / 1024.0:F2} KB resources extracted.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                IsLoading = false;
                (BuildFirmwareCommand as RelayCommand)?.RaiseCanExecuteChanged();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TryLoadAsDestBinAsync] Exception: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        #endregion

        #region ReplaceCommand
        /// <summary>
        /// 替换资源后，更新所有资源的 Offset（因为文件大小可能改变）
        /// </summary>
        private void UpdateResourceOffsetsAfterReplace()
        {
            if (_parser == null || Resources.Count == 0)
                return;

            // 从解析器获取最新的资源表（现在返回的是原始引用）
            var updatedTable = _parser.GetResourceTable();

            // 通过 ResourceItem.Id 与资源表索引匹配，避免顺序不一致导致错误更新
            foreach (var resource in Resources)
            {
                if (resource.Id < updatedTable.Count)
                {
                    var entry = updatedTable[(int)resource.Id];

                    bool offsetChanged = resource.Offset != entry.Offset;
                    bool sizeChanged = resource.Size != entry.Length;

                    if (offsetChanged)
                        resource.Offset = entry.Offset;
                    if (sizeChanged)
                        resource.Size = entry.Length;
                }
            }
        }

        private async Task LoadResBinAsync(string filePath)
        {
            IsLoading = true;
            StatusMessage = "Parsing RES.BIN...";
            _currentFilePath = filePath;
            _currentFileExists = true;

            // 自动设置 RES.BIN 路径为当前打开的文件
            _buildConfig.ResBinPath = filePath;

            try
            {
                // 在后台线程执行文件读取和解析
                var result = await Task.Run(() =>
                {
                    var parser = new ResBinParser(filePath);
                    bool success = parser.Parse();
                    return (Success: success, Parser: parser);
                });

                _parser = result.Parser;

                if (result.Success)
                {
                    // 保存原始数据用于后续修改
                    _currentFileData = result.Parser.FileData;
                    _currentTableOffset = result.Parser.TableOffset;

                    // 批量更新：一次性赋值新的 ObservableCollection（单次 PropertyChanged 通知）
                    Resources = new ObservableCollection<ResourceItem>(result.Parser.Resources);

                    StatusMessage = $"Loaded {Resources.Count} resources from {Path.GetFileName(filePath)}";

                    long fileSize = new FileInfo(filePath).Length;
                    MessageBox.Show(
                        $"Successfully loaded {Resources.Count} resources!\n\n" +
                        $"File: {Path.GetFileName(filePath)}\n" +
                        $"Size: {fileSize:N0} bytes",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to parse file:\n{result.Parser.ErrorMessage}",
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

        #endregion

        #region SaveCommand
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

                // 1. 创建备份（带时间戳）
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupPath = $"{_currentFilePath}.backup.{timestamp}";
                if (File.Exists(_currentFilePath))
                {
                    File.Copy(_currentFilePath, backupPath, true);

                    // 清理旧备份，保留最近 3 个
                    try
                    {
                        var dir = Path.GetDirectoryName(_currentFilePath)!;
                        var baseName = Path.GetFileName(_currentFilePath);
                        var backups = Directory.GetFiles(dir, $"{baseName}.backup.*")
                            .OrderByDescending(f => f)
                            .ToList();
                        foreach (var old in backups.Skip(3))
                            File.Delete(old);
                    }
                    catch { /* cleanup failure is non-fatal */ }
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

            object? snapshot = null;
            try
            {
                StatusMessage = "Saving DestBin.bin...";

                // Save snapshot for rollback
                snapshot = _destBinParser.CreateSnapshot();

                // 1. 始终应用资源修改
                if (!_destBinParser.ReplaceResBin(_currentFileData, keepSize: false))
                    throw new InvalidOperationException($"应用资源修改失败: {_destBinParser.ErrorMessage}");

                // 2. 如果有配置修改，同步配置数据到固件
                ApplyConfigChangesToDestBin();

                // 3. 保存到原文件
                if (!_destBinParser.Save(_currentFilePath))
                    throw new InvalidOperationException($"保存 DestBin.bin 失败: {_destBinParser.ErrorMessage}");

                // Committed - no rollback needed
                snapshot = null;

                // 保存成功后重置所有修改状态
                IsConfigModified = false;
                (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                foreach (var r in Resources)
                {
                    if (r != null) r.IsModified = false;
                }
                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();

                StatusMessage = $"✓ Saved to {Path.GetFileName(_currentFilePath)}";

                if (!string.IsNullOrEmpty(_loadedXmlFilePath) && FirmwareConfigData.XmlParsedItems != null)
                {
                    try
                    {
                        SyncConfigItemsToXmlParsed();

                        string xmlBackupPath = _loadedXmlFilePath + ".backup-" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        if (File.Exists(_loadedXmlFilePath))
                        {
                            File.Copy(_loadedXmlFilePath, xmlBackupPath, true);
                        }

                        ConfigXmlParser.SaveXmlToFile(_loadedXmlFilePath, FirmwareConfigData.XmlParsedItems);
                        StatusMessage += " (XML 已同步)";
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExecuteSaveConfig] XML sync failed: {ex.Message}");
                        StatusMessage += $" (XML 同步失败: {ex.Message})";
                    }
                }

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
            catch (Exception ex)
            {
                if (snapshot != null)
                    _destBinParser.RestoreSnapshot(snapshot);

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
                            object? snapshot = null;
                            try
                            {
                                // Save snapshot for rollback
                                snapshot = _destBinParser.CreateSnapshot();

                                // 1. 始终应用资源修改
                                if (!_destBinParser.ReplaceResBin(_currentFileData, keepSize: false))
                                    throw new InvalidOperationException($"应用资源修改失败: {_destBinParser.ErrorMessage}");

                                // 2. 如果有配置修改，同步配置数据到固件
                                ApplyConfigChangesToDestBin();

                                // 3. 保存到新文件
                                if (!_destBinParser.Save(dialog.FileName))
                                    throw new InvalidOperationException($"保存失败: {_destBinParser.ErrorMessage}");

                                // Committed - no rollback needed
                                snapshot = null;

                                // 保存成功后重置所有修改状态
                                IsConfigModified = false;
                                (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                                foreach (var r in Resources)
                                {
                                    if (r != null) r.IsModified = false;
                                }
                                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();

                                StatusMessage = $"✓ Saved to {Path.GetFileName(dialog.FileName)}";

                                if (!string.IsNullOrEmpty(_loadedXmlFilePath) && FirmwareConfigData.XmlParsedItems != null)
                                {
                                    try
                                    {
                                        SyncConfigItemsToXmlParsed();

                                        string xmlBackupPath = _loadedXmlFilePath + ".backup-" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                                        if (File.Exists(_loadedXmlFilePath))
                                        {
                                            File.Copy(_loadedXmlFilePath, xmlBackupPath, true);
                                        }

                                        ConfigXmlParser.SaveXmlToFile(_loadedXmlFilePath, FirmwareConfigData.XmlParsedItems);
                                        StatusMessage += " (XML 已同步)";
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[ExecuteSaveConfig] XML sync failed: {ex.Message}");
                                        StatusMessage += $" (XML 同步失败: {ex.Message})";
                                    }
                                }

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
                            catch (Exception ex)
                            {
                                if (snapshot != null)
                                    _destBinParser.RestoreSnapshot(snapshot);

                                MessageBox.Show($"Save failed: {ex.Message}",
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

        #endregion

        private bool CanExecutePreview(object? parameter)
        {
            return SelectedResource != null && SelectedResource.IsModified;
        }

        private void ExecuteShowTimeSync(object? parameter)
        {
            // 已打开则激活，避免多实例
            if (_timeSyncWindow != null && _timeSyncWindow.IsLoaded)
            {
                _timeSyncWindow.Activate();
                return;
            }

            _timeSyncWindow = new Views.TimeSyncWindow();
            _timeSyncWindow.Owner = Application.Current?.MainWindow;

            // 窗口关闭时清理引用（ViewModel.Dispose 由 Window.Closed 事件处理）
            _timeSyncWindow.Closed += (s, e) => _timeSyncWindow = null;

            // 使用 Show() 而非 ShowDialog()，允许后台自动同步时操作主窗口
            _timeSyncWindow.Show();
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
        private async void LoadWavForPreview()
        {
            if (SelectedResource == null || _parser == null || _currentFileData == null)
                return;

            try
            {
                StatusMessage = "Loading WAV...";

                // 在后台线程提取并解析 WAV 数据，避免阻塞 UI
                var offset = SelectedResource.Offset;
                var size = SelectedResource.Size;
                var wavData = await Task.Run(() =>
                {
                    var data = new byte[size];
                    Array.Copy(_currentFileData, offset, data, 0, size);
                    return data;
                });

                // 解析 WAV 信息（UI 线程）
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
        /// <remarks>
        /// 字体资源有两种类型，通常相邻存放：
        /// - RES_RESFONT (ID 14): resfont.bin，前4字节是字符数量（小端序，100-50000）
        /// - RES_RESFONTIDX (ID 15): resfontidx.bin，前2字节是魔数 0x584D ("MX")
        /// 
        /// 判断优先级：
        /// 1. 名称匹配（resfont / fontidx）
        /// 2. 魔数检测（0x584D 或字符数量范围）
        /// </remarks>
        private bool IsFontResource(ResourceItem? resource)
        {
            if (resource == null)
                return false;

            // 方法1: 通过名称判断（优先级最高）
            bool nameMatchesFont = resource.Name.IndexOf("resfont", StringComparison.OrdinalIgnoreCase) >= 0;
            bool nameMatchesFontIdx = resource.Name.IndexOf("fontidx", StringComparison.OrdinalIgnoreCase) >= 0;

            if (nameMatchesFont || nameMatchesFontIdx)
            {
                System.Diagnostics.Debug.WriteLine($"[Font] Resource '{resource.Name}' matched by name");
                return true;
            }

            // 方法2: 魔数检测 + 相邻存储配对检测（精确匹配）
            byte[]? data = resource.Data;
            if (data == null || data.Length == 0)
            {
                if (_currentFileData != null && resource.Offset + resource.Size <= _currentFileData.Length)
                {
                    data = new byte[resource.Size];
                    Array.Copy(_currentFileData, (int)resource.Offset, data, 0, (int)resource.Size);
                }
            }

            if (data != null && data.Length >= 4)
            {
                try
                {
                    ushort magic = BitConverter.ToUInt16(data, 0);
                    if (magic == 0x584D)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Font] Resource '{resource.Name}' has font index magic (MX): 0x{magic:X4}");

                        int currentIdx = Resources.IndexOf(resource);
                        if (currentIdx >= 0)
                        {
                            bool hasAdjacentFontData = CheckAdjacentResourceForCharCount(currentIdx, -1) ||
                                                        CheckAdjacentResourceForCharCount(currentIdx, 1);
                            if (hasAdjacentFontData)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Font] Resource '{resource.Name}' matched by magic + adjacent font data");
                                return true;
                            }
                        }
                    }
                    else
                    {
                        uint charCount = BitConverter.ToUInt32(data, 0);
                        if (charCount >= 100 && charCount <= 50000)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Font] Resource '{resource.Name}' has valid char count: {charCount}");

                            int currentIdx = Resources.IndexOf(resource);
                            if (currentIdx >= 0)
                            {
                                bool hasAdjacentFontIdx = CheckAdjacentResourceForMagic(currentIdx, -1) ||
                                                           CheckAdjacentResourceForMagic(currentIdx, 1);
                                if (hasAdjacentFontIdx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Font] Resource '{resource.Name}' matched by char count + adjacent font index");
                                    return true;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Font] Error checking magic: {ex.Message}");
                }
            }



            return false;
        }

        /// <summary>
        /// 判断是否是 resfont.bin（字体数据文件，存储字符位图）
        /// 特征：首4字节是 charCount，值在 100~50000 之间
        /// </summary>
        private bool IsFontDataResource(ResourceItem? resource)
        {
            if (resource == null)
                return false;

            if (resource.Name.IndexOf("resfont", StringComparison.OrdinalIgnoreCase) >= 0 &&
                resource.Name.IndexOf("fontidx", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return true;
            }

            byte[]? data = resource.Data;
            if (data == null || data.Length == 0)
            {
                if (_currentFileData != null && resource.Offset + resource.Size <= _currentFileData.Length)
                {
                    data = new byte[resource.Size];
                    Array.Copy(_currentFileData, (int)resource.Offset, data, 0, (int)resource.Size);
                }
            }

            if (data != null && data.Length >= 4)
            {
                uint charCount = BitConverter.ToUInt32(data, 0);
                if (charCount >= 100 && charCount <= 50000)
                {
                    int currentIdx = Resources.IndexOf(resource);
                    if (currentIdx >= 0)
                    {
                        bool hasAdjacentFontIdx = CheckAdjacentResourceForMagic(currentIdx, -1) ||
                                                   CheckAdjacentResourceForMagic(currentIdx, 1);
                        if (hasAdjacentFontIdx)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 判断是否是 resfontidx.bin（字体索引文件，存储字符串）
        /// 特征：首2字节是 magic=0x584D
        /// </summary>
        private bool IsFontIndexResource(ResourceItem? resource)
        {
            if (resource == null)
                return false;

            if (resource.Name.IndexOf("fontidx", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            byte[]? data = resource.Data;
            if (data == null || data.Length == 0)
            {
                if (_currentFileData != null && resource.Offset + resource.Size <= _currentFileData.Length)
                {
                    data = new byte[resource.Size];
                    Array.Copy(_currentFileData, (int)resource.Offset, data, 0, (int)resource.Size);
                }
            }

            if (data != null && data.Length >= 4)
            {
                ushort magic = BitConverter.ToUInt16(data, 0);
                if (magic == 0x584D)
                {
                    int currentIdx = Resources.IndexOf(resource);
                    if (currentIdx >= 0)
                    {
                        bool hasAdjacentFontData = CheckAdjacentResourceForCharCount(currentIdx, -1) ||
                                                    CheckAdjacentResourceForCharCount(currentIdx, 1);
                        if (hasAdjacentFontData)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public bool CheckAdjacentResourceForMagic(int currentIdx, int offset)
        {
            int adjacentIdx = currentIdx + offset;
            if (adjacentIdx < 0 || adjacentIdx >= Resources.Count)
                return false;

            var adjacentResource = Resources[adjacentIdx];
            if (adjacentResource.Size <= 0)
                return false;

            byte[]? data = adjacentResource.Data;
            if (data == null || data.Length == 0)
            {
                if (_currentFileData != null && adjacentResource.Offset + adjacentResource.Size <= _currentFileData.Length)
                {
                    data = new byte[adjacentResource.Size];
                    Array.Copy(_currentFileData, (int)adjacentResource.Offset, data, 0, (int)adjacentResource.Size);
                }
            }

            if (data != null && data.Length >= 2)
            {
                ushort magic = BitConverter.ToUInt16(data, 0);
                if (magic == 0x584D)
                {
                    System.Diagnostics.Debug.WriteLine($"[Font] Adjacent resource '{adjacentResource.Name}' has font index magic");
                    return true;
                }
            }

            return false;
        }

        public bool CheckAdjacentResourceForCharCount(int currentIdx, int offset)
        {
            int adjacentIdx = currentIdx + offset;
            if (adjacentIdx < 0 || adjacentIdx >= Resources.Count)
                return false;

            var adjacentResource = Resources[adjacentIdx];
            if (adjacentResource.Size <= 0)
                return false;

            byte[]? data = adjacentResource.Data;
            if (data == null || data.Length == 0)
            {
                if (_currentFileData != null && adjacentResource.Offset + adjacentResource.Size <= _currentFileData.Length)
                {
                    data = new byte[adjacentResource.Size];
                    Array.Copy(_currentFileData, (int)adjacentResource.Offset, data, 0, (int)adjacentResource.Size);
                }
            }

            if (data != null && data.Length >= 4)
            {
                uint charCount = BitConverter.ToUInt32(data, 0);
                if (charCount >= 100 && charCount <= 50000)
                {
                    System.Diagnostics.Debug.WriteLine($"[Font] Adjacent resource '{adjacentResource.Name}' has valid char count: {charCount}");
                    return true;
                }
            }

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

            foreach (var resource in Resources)
            {
                if (resource.Size <= 0)
                    continue;

                if (resource.Name.IndexOf("resfont", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    resource.Name.IndexOf("fontidx", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    fontResources.Add(resource);
                    System.Diagnostics.Debug.WriteLine($"[GetFontResources] Found by name: {resource.Name}");
                }
                else if (IsFontResourceByMagic(resource))
                {
                    fontResources.Add(resource);
                    System.Diagnostics.Debug.WriteLine($"[GetFontResources] Found by magic: {resource.Name}");
                }
            }

            return fontResources;
        }

        private bool IsFontResourceByMagic(ResourceItem resource)
        {
            byte[]? data = resource.Data;
            if (data == null || data.Length == 0)
            {
                if (_currentFileData != null && resource.Offset + resource.Size <= _currentFileData.Length)
                {
                    data = new byte[resource.Size];
                    Array.Copy(_currentFileData, (int)resource.Offset, data, 0, (int)resource.Size);
                }
            }

            if (data != null && data.Length >= 4)
            {
                ushort magic = BitConverter.ToUInt16(data, 0);
                if (magic == 0x584D)
                {
                    int currentIdx = Resources.IndexOf(resource);
                    if (currentIdx >= 0)
                    {
                        bool hasAdjacentFontData = CheckAdjacentResourceForCharCount(currentIdx, -1) ||
                                                    CheckAdjacentResourceForCharCount(currentIdx, 1);
                        if (hasAdjacentFontData)
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    uint charCount = BitConverter.ToUInt32(data, 0);
                    if (charCount >= 100 && charCount <= 70000)
                    {
                        int currentIdx = Resources.IndexOf(resource);
                        if (currentIdx >= 0)
                        {
                            bool hasAdjacentFontIdx = CheckAdjacentResourceForMagic(currentIdx, -1) ||
                                                       CheckAdjacentResourceForMagic(currentIdx, 1);
                            if (hasAdjacentFontIdx)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 加载字体资源进行预览（异步）
        /// </summary>
        private async void LoadFontForPreview()
        {
            if (SelectedResource == null || _parser == null || _currentFileData == null)
            {
                System.Diagnostics.Debug.WriteLine("[Font] LoadFontForPreview: Missing required data");
                return;
            }

            StatusMessage = "Loading font...";

            System.Diagnostics.Debug.WriteLine($"[Font] Loading font for resource: ID={SelectedResource.Id}, Name={SelectedResource.Name}");

            try
            {
                var fontResources = GetFontResources();

                if (fontResources.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[Font] No font resources found");
                    StatusMessage = "Font resources not found";
                    return;
                }

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

                if (resfont == null || resfontidx == null)
                {
                    foreach (var fontRes in fontResources)
                    {
                        byte[]? data = fontRes.Data;
                        if (data == null || data.Length == 0)
                        {
                            if (_currentFileData != null && fontRes.Offset + fontRes.Size <= _currentFileData.Length)
                            {
                                data = new byte[fontRes.Size];
                                Array.Copy(_currentFileData, (int)fontRes.Offset, data, 0, (int)fontRes.Size);
                            }
                        }

                        if (data != null && data.Length >= 4)
                        {
                            ushort magic = BitConverter.ToUInt16(data, 0);
                            if (magic == 0x584D && resfontidx == null)
                            {
                                resfontidx = fontRes;
                                System.Diagnostics.Debug.WriteLine($"[Font] Identified resfontidx by magic: {fontRes.Name}");
                            }
                            else if (resfont == null)
                            {
                                uint charCount = BitConverter.ToUInt32(data, 0);
                                if (charCount >= 100 && charCount <= 60000)
                                {
                                    resfont = fontRes;
                                    System.Diagnostics.Debug.WriteLine($"[Font] Identified resfont by char count: {fontRes.Name}");
                                }
                            }
                        }
                    }
                }

                if (resfont == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Font] resfont resource not found");
                    StatusMessage = "resfont resource not found";
                    return;
                }

                if (resfontidx == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Font] resfontidx resource not found");
                    StatusMessage = "resfontidx resource not found";
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[Font] Found resfont: Size={resfont.Size}, Offset={resfont.Offset}");
                System.Diagnostics.Debug.WriteLine($"[Font] Found resfontidx: Size={resfontidx.Size}, Offset={resfontidx.Offset}");

                // 异步提取字体数据
                FontData = await Task.Run(() =>
                {
                    byte[] data = new byte[resfont.Size];
                    Array.Copy(_currentFileData, resfont.Offset, data, 0, resfont.Size);
                    return data;
                });

                // 异步提取索引数据
                FontIndex = await Task.Run(() =>
                {
                    byte[] data = new byte[resfontidx.Size];
                    Array.Copy(_currentFileData, resfontidx.Offset, data, 0, resfontidx.Size);
                    return data;
                });

                System.Diagnostics.Debug.WriteLine($"[Font] Extracted data: FontData.Length={FontData.Length}, FontIndex.Length={FontIndex.Length}");

                // 尝试从文件系统加载 font.bin (charCode→index 映射表)
                FontBinData = null;
                if (!string.IsNullOrEmpty(_currentFilePath))
                {
                    try
                    {
                        string? dir = System.IO.Path.GetDirectoryName(_currentFilePath);
                        while (dir != null)
                        {
                            string fontBinPath = System.IO.Path.Combine(dir, "font.bin");
                            if (System.IO.File.Exists(fontBinPath))
                            {
                                FontBinData = await Task.Run(() => System.IO.File.ReadAllBytes(fontBinPath));
                                System.Diagnostics.Debug.WriteLine($"[Font] Loaded font.bin: {fontBinPath} ({FontBinData.Length} bytes)");
                                break;
                            }
                            // 向上查找，最多3层
                            string parent = System.IO.Path.GetDirectoryName(dir)!;
                            if (parent == null || parent == dir) break;
                            dir = parent;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Font] Warning: Could not load font.bin: {ex.Message}");
                    }
                }

                if (FontBinData == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Font] Warning: font.bin not found, string decoding may be incorrect");
                }

                // 异步解析字体信息（带 font.bin 映射）
                FontInfo = await Task.Run(() => FontInfoParser.Parse(FontData, FontIndex, FontBinData));

                // 更新语言列表
                UpdateFontLanguages();

                System.Diagnostics.Debug.WriteLine($"[Font] Parsed successfully: {FontInfo.DisplayName}");
                StatusMessage = $"Font loaded: {FontInfo.DisplayName}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Font] Error: {ex.Message}");
                MessageBox.Show($"Failed to load font:\n{ex.Message}",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                FontInfo = null;
            }
        }

        private void UpdateFontLanguages()
        {
            FontLanguages.Clear();

            if (FontInfo == null)
                return;

            var languageNames = new string[]
            {
                "English", "简体中文", "繁体中文", "日本語", "русский",
                "한국어", "Italiano", "Nederlands", "Deutsch", "Polski",
                "Español", "ไทย", "Français", "Português", "Čeština",
                "Magyar", "Română", "Türkçe"
            };

            for (int i = 0; i < FontInfo.LanguageCount; i++)
            {
                string name = i < languageNames.Length ? languageNames[i] : $"Language {i}";
                FontLanguages.Add(new FontLanguageItem { Index = i, DisplayName = name });
            }

            SelectedFontLanguageIndex = 0;
        }

        private void LoadFontStringsForLanguage(int languageIndex)
        {
            FontStrings.Clear();

            if (FontInfo == null || FontIndex == null || FontIndex.Length == 0)
                return;

            if (languageIndex < 0 || languageIndex >= FontInfo.LanguageCount)
            {
                System.Diagnostics.Debug.WriteLine($"[Font] Invalid language index: {languageIndex}");
                return;
            }

            if (languageIndex >= FontInfo.Languages.Count)
            {
                System.Diagnostics.Debug.WriteLine($"[Font] Language index {languageIndex} out of range (Languages.Count={FontInfo.Languages.Count})");
                return;
            }

            try
            {
                var langInfo = FontInfo.Languages[languageIndex];
                int strBlockOff = (int)langInfo.StringBlockOffset;
                int stringCount = langInfo.StringCount;
                int strEntrySize = 8;

                System.Diagnostics.Debug.WriteLine($"[Font] Loading lang[{languageIndex}]: strBlockOff={strBlockOff}, stringCount={stringCount}");

                if (strBlockOff + 8 > FontIndex.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"[Font] String block offset {strBlockOff} out of range");
                    return;
                }

                // 字符串条目从 strBlockOff + 8 开始 (跳过8字节块头)
                int strEntryStart = strBlockOff + 8;

                // 优先使用预构建的 charIndex→charCode 映射（避免重复解析 font.bin）
                bool useIndexMap = FontInfo.CharCodeIndexMap != null && FontInfo.CharCodeIndexMap.Length > 0;

                for (int i = 0; i < stringCount; i++)
                {
                    int entryOffset = strEntryStart + i * strEntrySize;

                    if (entryOffset + strEntrySize > FontIndex.Length)
                        break;

                    var strInfo = new StringInfo
                    {
                        Width = BitConverter.ToUInt16(FontIndex, entryOffset),
                        Height = BitConverter.ToUInt16(FontIndex, entryOffset + 2),
                        Number = BitConverter.ToUInt16(FontIndex, entryOffset + 4)
                    };

                    // relOffset 是相对于 strBlockOff 的偏移
                    ushort relOffset = BitConverter.ToUInt16(FontIndex, entryOffset + 6);
                    strInfo.DataOffset = strBlockOff + relOffset;

                    if (strInfo.Number > 1000 || strInfo.DataOffset >= FontIndex.Length)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Font] Skip invalid string #{i}: Number={strInfo.Number}, DataOffset={strInfo.DataOffset}");
                        continue;
                    }

                    // 解码字符串：优先用映射表，否则用 font.bin 逐条解析
                    // 注意：没有 font.bin 时不要传入 FontData(resfont.bin)，两种文件结构不同，
                    // 传入会造成误解析。FontBinData 为 null 时 DecodeString 会自动降级为 hex 显示。
                    string content;
                    if (useIndexMap)
                    {
                        content = StringDecoder.DecodeString(FontIndex, FontInfo.CharCodeIndexMap, strInfo, (int)FontInfo.CharCount);
                    }
                    else
                    {
                        content = StringDecoder.DecodeString(FontIndex, FontBinData, strInfo);
                    }

                    FontStrings.Add(new FontStringItem
                    {
                        Index = i,
                        Width = strInfo.Width,
                        Height = strInfo.Height,
                        CharCount = strInfo.Number,
                        Content = content,
                        StringInfos = strInfo,
                        DisplayText = $"[{i:D3}] {content}"
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Font] Failed to load strings for language {languageIndex}: {ex.Message}");
            }
        }

        private void LoadPaletteForPreview()
        {
            if (SelectedResource == null || _parser == null || _currentFileData == null)
            {
                System.Diagnostics.Debug.WriteLine("[Palette] LoadPaletteForPreview: Missing required data");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[Palette] Loading palette for resource: ID={SelectedResource.Id}, Name={SelectedResource.Name}");

            try
            {
                PaletteData = new byte[SelectedResource.Size];
                Array.Copy(_currentFileData, SelectedResource.Offset, PaletteData, 0, SelectedResource.Size);

                PaletteInfo = PaletteParser.ParsePaletteInfo(PaletteData);

                System.Diagnostics.Debug.WriteLine($"[Palette] Parsed successfully: {PaletteInfo.DisplayName}");
                StatusMessage = $"Palette loaded: {PaletteInfo.DisplayName}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Palette] Error: {ex.Message}");
                MessageBox.Show($"Failed to load palette:\n{ex.Message}",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                PaletteInfo = null;
                PaletteData = null;
            }
        }

        private void LoadOsdForPreview()
        {
            if (SelectedResource == null || _currentFileData == null)
            {
                System.Diagnostics.Debug.WriteLine("[OSD] LoadOsdForPreview: Missing required data");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[OSD] Loading OSD for resource: ID={SelectedResource.Id}, Name={SelectedResource.Name}");

            try
            {
                OsdData = new byte[SelectedResource.Size];
                Array.Copy(_currentFileData, SelectedResource.Offset, OsdData, 0, SelectedResource.Size);

                byte[]? paletteData = FindPaletteResourceData();

                string? originalIconDirectory = null;
                if (!string.IsNullOrEmpty(_currentFilePath))
                {
                    string firmwareDir = Path.GetDirectoryName(_currentFilePath);
                    if (firmwareDir != null)
                    {
                        string iconSrcDir = Path.Combine(firmwareDir, "resource", "icon", "iconSrc");
                        if (Directory.Exists(iconSrcDir))
                        {
                            originalIconDirectory = iconSrcDir;
                        }
                    }
                }

                _osdOriginalIconDirectory = originalIconDirectory;
                OsdInfo = OsdSourceParser.ParseOsdInfo(OsdData, paletteData, originalIconDirectory);

                System.Diagnostics.Debug.WriteLine($"[OSD] Parsed successfully: {OsdInfo.DisplayName}");
                StatusMessage = $"OSD loaded: {OsdInfo.DisplayName}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OSD] Error: {ex.Message}");
                MessageBox.Show($"Failed to load OSD:\n{ex.Message}",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                OsdInfo = null;
                OsdData = null;
            }
        }

        private void LoadTextForPreview()
        {
            if (SelectedResource == null || _currentFileData == null)
            {
                System.Diagnostics.Debug.WriteLine("[Text] LoadTextForPreview: Missing required data");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[Text] Loading Text for resource: ID={SelectedResource.Id}, Name={SelectedResource.Name}");

            try
            {
                byte[] textData = new byte[SelectedResource.Size];
                Array.Copy(_currentFileData, SelectedResource.Offset, textData, 0, SelectedResource.Size);

                string textContent = System.Text.Encoding.UTF8.GetString(textData);

                textContent = textContent.TrimEnd('\0', '\r', '\n');

                TextContent = textContent;

                System.Diagnostics.Debug.WriteLine($"[Text] Loaded successfully: {textContent.Length} characters");
                StatusMessage = $"Text loaded: {textContent.Length} characters";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Text] Error: {ex.Message}");
                TextContent = string.Empty;
            }
        }

        private byte[]? FindPaletteResourceData()
        {
            if (Resources == null)
                return null;

            foreach (var res in Resources)
            {
                if (res.Type == ResourceType.Palette && res.Data != null)
                {
                    return res.Data;
                }
            }

            foreach (var res in Resources)
            {
                if (res.Name.IndexOf("PALETTE", StringComparison.OrdinalIgnoreCase) >= 0 && res.Data != null)
                {
                    return res.Data;
                }
            }

            return null;
        }

        #endregion

        public event EventHandler<ResourceItem>? PreviewRequested;

        // ==================== 配置管理相关方法 ====================
        // === Configuration Management moved to MainViewModel.Config.cs ===

        private void ApplyConfigChangesToDestBin()
        {
            if (!IsConfigModified || FirmwareConfigData == null || _destBinParser == null)
                return;

            System.Diagnostics.Debug.WriteLine($"[ApplyConfig] Applying config changes...");

            // 备份 Flags 用于 SyncConfigItemsToFlags() 后可能发生的回滚
            uint[] originalFlags = (uint[])FirmwareConfigData.Flags.Clone();
            uint originalCheckSum = FirmwareConfigData.CheckSum;

            try
            {
                SyncConfigItemsToFlags();

                byte[] firmwareData = _destBinParser.GetDestBinData();
                uint configAddress = _destBinParser.CalculateConfigAddress();
                FirmwareConfigData.ConfigAddress = configAddress;

                int flagsCount = ConfigParser.SDK_CONFIG_ID_MAX;
                int configSize = flagsCount * 4 + 4;

                if (configAddress + configSize > firmwareData.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"[ApplyConfig] Config address 0x{configAddress:X} exceeds firmware size {firmwareData.Length}");
                    throw new InvalidOperationException($"配置区地址 0x{configAddress:X} 超出固件大小 {firmwareData.Length}");
                }

                byte[] configBuffer = new byte[configSize];
                for (int i = 0; i < flagsCount; i++)
                {
                    byte[] flagBytes = BitConverter.GetBytes(FirmwareConfigData.Flags[i]);
                    Array.Copy(flagBytes, 0, configBuffer, i * 4, 4);
                }
                uint checkSum = FirmwareConfigData.CalculateCheckSum();
                byte[] checkSumBytes = BitConverter.GetBytes(checkSum);
                Array.Copy(checkSumBytes, 0, configBuffer, flagsCount * 4, 4);

                Array.Copy(configBuffer, 0, firmwareData, (int)configAddress, configSize);
                _destBinParser.UpdateDestBinData(firmwareData);

                System.Diagnostics.Debug.WriteLine($"[ApplyConfig] Config data written to address 0x{configAddress:X}");

                IsConfigModified = false;
            }
            catch
            {
                // 回滚：恢复 SyncConfigItemsToFlags 对 Flags 的修改
                Array.Copy(originalFlags, FirmwareConfigData.Flags, originalFlags.Length);
                FirmwareConfigData.CheckSum = originalCheckSum;
                System.Diagnostics.Debug.WriteLine($"[ApplyConfig] Rolled back Flags after sync failure");
                throw;
            }
        }

        // CanExecuteResetConfig, ExecuteResetConfig, CanExecuteExportConfig, ExecuteExportConfig moved to Config.cs

        #region 用户设置管理
        /// <summary>
        /// 查找 customer.h 文件
        /// </summary>
        private string? FindCustomerH(string projectPath)
        {
            string[] searchPaths = new[]
            {
                Path.Combine(projectPath, "inc", "customer.h"),
                Path.Combine(projectPath, "customer.h"),
                Path.Combine(projectPath, "ax32_platform_demo", "inc", "customer.h"),
                Path.Combine(projectPath, "ax32_platform_demo", "customer.h"),
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            try
            {
                var files = Directory.GetFiles(projectPath, "customer.h", SearchOption.AllDirectories);
                if (files.Length > 0)
                    return files[0];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FindCustomerH] Error searching customer.h: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 查找 version.h 文件
        /// </summary>
        private string? FindVersionH(string projectPath)
        {
            string[] searchPaths = new[]
            {
                Path.Combine(projectPath, "version.h"),
                Path.Combine(projectPath, "inc", "version.h"),
                Path.Combine(projectPath, "ax32_platform_demo", "version.h"),
                Path.Combine(projectPath, "ax32_platform_demo", "inc", "version.h"),
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            try
            {
                var files = Directory.GetFiles(projectPath, "version.h", SearchOption.AllDirectories);
                if (files.Length > 0)
                    return files[0];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FindVersionH] Error searching version.h: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 查找 RES.H 文件（资源定义头文件）
        /// </summary>
        private string? FindResH(string projectPath)
        {
            string[] searchPaths = new[]
            {
                Path.Combine(projectPath, "resource", "RES.H"),
                Path.Combine(projectPath, "RES.H"),
                Path.Combine(projectPath, "ax32_platform_demo", "resource", "RES.H"),
                Path.Combine(projectPath, "ax32_platform_demo", "RES.H"),
                Path.Combine(projectPath, "inc", "RES.H"),
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            try
            {
                var files = Directory.GetFiles(projectPath, "RES.H", SearchOption.AllDirectories);
                if (files.Length > 0)
                    return files[0];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FindResH] Error searching RES.H: {ex.Message}");
            }

            return null;
        }

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

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }

    #region 辅助类
    public class FontLanguageItem
    {
        public int Index { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }

    public class FontStringItem
    {
        public int Index { get; set; }
        public ushort Width { get; set; }
        public ushort Height { get; set; }
        public ushort CharCount { get; set; }
        public string Content { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
        public StringInfo? StringInfos { get; set; }
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

    #endregion
}


