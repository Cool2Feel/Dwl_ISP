using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using FontBuilder.Core;
using FontBuilder.Models;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace FontBuilder.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel
    /// 三栏布局: 左配置 / 中源文本 / 右预览+日志
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        #region Fields

        private string _fontIniPath = string.Empty;
        private string _fontSelectPath = string.Empty;
        private string _outputDirectory = string.Empty;
        private string _statusMessage = "请加载 font.ini";
        private bool _isBuilding;
        private bool _isLoaded;
        private int _buildProgress;
        private string _progressStage = string.Empty;
        private string _logText = string.Empty;
        private DataTable _stringTable;
        private FontBuildConfig _config;
        private CancellationTokenSource _cts;

        // 预览
        private int _previewCharIndex;
        private string _previewCharInfo = string.Empty;

        // 构建结果
        private int _charCount;
        private int _stringCount;
        private int _languageCount;
        private string _elapsedText = string.Empty;

        #endregion

        #region Properties

        /// <summary>font.ini 路径</summary>
        public string FontIniPath
        {
            get => _fontIniPath;
            set { _fontIniPath = value; OnPropertyChanged(); }
        }

        /// <summary>fontSelect.txt 路径</summary>
        public string FontSelectPath
        {
            get => _fontSelectPath;
            set { _fontSelectPath = value; OnPropertyChanged(); }
        }

        /// <summary>输出目录</summary>
        public string OutputDirectory
        {
            get => _outputDirectory;
            set { _outputDirectory = value; OnPropertyChanged(); }
        }

        /// <summary>状态消息</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        /// <summary>是否正在构建</summary>
        public bool IsBuilding
        {
            get => _isBuilding;
            set { _isBuilding = value; OnPropertyChanged(); }
        }

        /// <summary>是否已加载</summary>
        public bool IsLoaded
        {
            get => _isLoaded;
            set { _isLoaded = value; OnPropertyChanged(); }
        }

        /// <summary>构建进度 (0-100)</summary>
        public int BuildProgress
        {
            get => _buildProgress;
            set { _buildProgress = value; OnPropertyChanged(); }
        }

        /// <summary>进度阶段描述</summary>
        public string ProgressStage
        {
            get => _progressStage;
            set { _progressStage = value; OnPropertyChanged(); }
        }

        /// <summary>日志文本</summary>
        public string LogText
        {
            get => _logText;
            set { _logText = value; OnPropertyChanged(); }
        }

        /// <summary>源文本表格（列=语言，行=字符串索引）</summary>
        public DataTable StringTable
        {
            get => _stringTable;
            set { _stringTable = value; OnPropertyChanged(); }
        }

        /// <summary>字符数</summary>
        public int CharCount
        {
            get => _charCount;
            set { _charCount = value; OnPropertyChanged(); }
        }

        /// <summary>字符串数</summary>
        public int StringCount
        {
            get => _stringCount;
            set { _stringCount = value; OnPropertyChanged(); }
        }

        /// <summary>语言数</summary>
        public int LanguageCount
        {
            get => _languageCount;
            set { _languageCount = value; OnPropertyChanged(); }
        }

        /// <summary>耗时</summary>
        public string ElapsedText
        {
            get => _elapsedText;
            set { _elapsedText = value; OnPropertyChanged(); }
        }

        /// <summary>预览字符索引</summary>
        public int PreviewCharIndex
        {
            get => _previewCharIndex;
            set { _previewCharIndex = value; OnPropertyChanged(); UpdatePreviewInfo(); }
        }

        /// <summary>预览字符信息</summary>
        public string PreviewCharInfo
        {
            get => _previewCharInfo;
            set { _previewCharInfo = value; OnPropertyChanged(); }
        }

        /// <summary>已渲染字形列表（用于预览）</summary>
        public System.Collections.Generic.List<CharGlyph> Glyphs { get; private set; }

        /// <summary>构建结果</summary>
        public FontBuildOrchestrator.BuildResult LastResult { get; private set; }

        #endregion

        #region Commands

        public ICommand BrowseIniCommand { get; }
        public ICommand LoadCommand { get; }
        public ICommand BuildCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand BrowseOutputDirCommand { get; }

        #endregion

        public MainViewModel()
        {
            BrowseIniCommand = new RelayCommand(_ => BrowseIni());
            LoadCommand = new RelayCommand(_ => LoadConfig(), _ => !string.IsNullOrEmpty(FontIniPath));
            BuildCommand = new RelayCommand(_ => BuildAsync(), _ => IsLoaded && !IsBuilding);
            CancelCommand = new RelayCommand(_ => CancelBuild(), _ => IsBuilding);
            BrowseOutputDirCommand = new RelayCommand(_ => BrowseOutputDir());
        }

        #region Load

        private void BrowseIni()
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择 font.ini",
                Filter = "font.ini|font.ini|INI 文件|*.ini|所有文件|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true)
            {
                FontIniPath = dlg.FileName;
                LoadConfig();
            }
        }

        private void BrowseOutputDir()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择输出目录",
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputDirectory = dlg.SelectedPath;
            }
        }

        /// <summary>
        /// 加载 font.ini 与 fontSelect.txt，预览源文本
        /// </summary>
        public void LoadConfig()
        {
            if (string.IsNullOrEmpty(FontIniPath) || !File.Exists(FontIniPath))
            {
                StatusMessage = "font.ini 不存在";
                return;
            }

            try
            {
                _config = FontIniParser.Parse(FontIniPath);

                string baseDir = Path.GetDirectoryName(Path.GetFullPath(FontIniPath)) ?? string.Empty;
                OutputDirectory = baseDir;
                FontSelectPath = Path.Combine(baseDir, "fontSelect.txt");

                // 加载字符串
                FontSrcTxtParser.LoadLanguageStrings(_config);

                // 构建源文本表格
                BuildStringTable(_config);

                LanguageCount = _config.Languages.Count;
                StringCount = _config.Languages.Count > 0 ? _config.Languages[0].Strings.Count : 0;
                StatusMessage = $"已加载 {LanguageCount} 语言，{StringCount} 条字符串";
                IsLoaded = true;
                AppendLog($"加载完成: {_config.Languages.Count} 语言 x {StringCount} 字符串");
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
                AppendLog($"[错误] {ex}");
            }
        }

        private void BuildStringTable(FontBuildConfig config)
        {
            var dt = new DataTable();
            dt.Columns.Add("Index", typeof(int));
            dt.Columns.Add("ID", typeof(string));
            foreach (var lang in config.Languages)
                dt.Columns.Add(lang.Name, typeof(string));

            int strCount = config.Languages.Count > 0 ? config.Languages[0].Strings.Count : 0;
            for (int i = 0; i < strCount; i++)
            {
                var row = dt.NewRow();
                row["Index"] = i;
                row["ID"] = i < StringIdNames.Count ? StringIdNames.All[i] : $"STR_{i}";
                foreach (var lang in config.Languages)
                {
                    if (i < lang.Strings.Count)
                        row[lang.Name] = lang.Strings[i];
                }
                dt.Rows.Add(row);
            }
            StringTable = dt;
        }

        #endregion

        #region Build

        private async void BuildAsync()
        {
            if (_config == null)
            {
                StatusMessage = "请先加载 font.ini";
                return;
            }

            _cts = new CancellationTokenSource();
            IsBuilding = true;
            BuildProgress = 0;
            LogText = string.Empty;
            AppendLog("开始构建...");

            try
            {
                var orchestrator = new FontBuildOrchestrator
                {
                    Progress = new Progress<(int done, int total, string stage)>(p =>
                    {
                        BuildProgress = p.total > 0 ? (p.done * 100 / p.total) : 0;
                        ProgressStage = p.stage;
                        StatusMessage = p.stage;
                    })
                };

                LastResult = await Task.Run(() => orchestrator.Build(FontIniPath, _cts.Token));

                foreach (var line in LastResult.Log)
                    AppendLog(line);

                if (LastResult.Success)
                {
                    Glyphs = LastResult.Glyphs;
                    CharCount = LastResult.CharCount;
                    StringCount = LastResult.StringCount;
                    LanguageCount = LastResult.LanguageCount;
                    ElapsedText = $"{LastResult.ElapsedMilliseconds} ms";
                    StatusMessage = $"构建完成: {CharCount} 字符, {StringCount} 字符串, {LanguageCount} 语言";
                    AppendLog(StatusMessage);
                }
                else if (LastResult.Cancelled)
                {
                    StatusMessage = "构建已取消";
                }
                else
                {
                    StatusMessage = $"构建失败: {LastResult.Error?.Message}";
                    AppendLog($"[错误] {LastResult.Error}");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"构建异常: {ex.Message}";
                AppendLog($"[异常] {ex}");
            }
            finally
            {
                IsBuilding = false;
                BuildProgress = 0;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void CancelBuild()
        {
            _cts?.Cancel();
            StatusMessage = "正在取消...";
        }

        #endregion

        #region Preview

        private void UpdatePreviewInfo()
        {
            if (Glyphs == null || PreviewCharIndex < 0 || PreviewCharIndex >= Glyphs.Count)
            {
                PreviewCharInfo = string.Empty;
                return;
            }
            var g = Glyphs[PreviewCharIndex];
            PreviewCharInfo = $"U+{g.CharCode:X4} '{g.DisplayText}'  {g.Width}x{g.Height}px  offset={g.BitmapOffset}";
        }

        #endregion

        #region Helpers

        private void AppendLog(string msg)
        {
            LogText += $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}";
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        #endregion
    }
}
