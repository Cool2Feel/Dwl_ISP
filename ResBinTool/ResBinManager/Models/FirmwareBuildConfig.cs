using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ResBinManager.Models
{
    /// <summary>
    /// 固件打包输入文件类型
    /// </summary>
    public enum FirmwareInputType
    {
        Elf,    // ELF 文件
        Bin     // BIN 文件
    }

    /// <summary>
    /// 固件打包配置模型
    /// </summary>
    public class FirmwareBuildConfig : INotifyPropertyChanged
    {
        private string _elfPath = string.Empty;
        private string _binPath = string.Empty;  // 新增：BIN 文件路径
        private FirmwareInputType _inputType = FirmwareInputType.Elf;  // 新增：输入类型
        private string _resBinPath = string.Empty;
        private string _outputPath = string.Empty;
        private string _makeSpiBinPath = string.Empty;
        private bool _autoBackup = true;
        private bool _autoOpenOutputFolder = true;
        private int _buildTimeoutMs = 60000;

        /// <summary>
        /// ELF 文件路径
        /// </summary>
        public string ElfPath
        {
            get => _elfPath;
            set { _elfPath = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// BIN 文件路径
        /// </summary>
        public string BinPath
        {
            get => _binPath;
            set { _binPath = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 输入文件类型（ELF 或 BIN）
        /// </summary>
        public FirmwareInputType InputType
        {
            get => _inputType;
            set { _inputType = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// RES.BIN 文件路径
        /// </summary>
        public string ResBinPath
        {
            get => _resBinPath;
            set { _resBinPath = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 输出目录路径
        /// </summary>
        public string OutputPath
        {
            get => _outputPath;
            set { _outputPath = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// MakeSPIBin.exe 工具路径
        /// </summary>
        public string MakeSpiBinPath
        {
            get => _makeSpiBinPath;
            set { _makeSpiBinPath = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 是否自动备份原文件
        /// </summary>
        public bool AutoBackup
        {
            get => _autoBackup;
            set { _autoBackup = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 完成后是否自动打开输出文件夹
        /// </summary>
        public bool AutoOpenOutputFolder
        {
            get => _autoOpenOutputFolder;
            set { _autoOpenOutputFolder = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Build 超时时间（毫秒），默认 60000
        /// </summary>
        public int BuildTimeoutMs
        {
            get => _buildTimeoutMs;
            set { _buildTimeoutMs = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
