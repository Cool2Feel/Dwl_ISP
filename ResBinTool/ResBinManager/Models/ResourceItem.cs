using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ResBinManager.Models
{
    /// <summary>
    /// 资源项数据模型
    /// </summary>
    public class ResourceItem : INotifyPropertyChanged
    {
        private uint _id;
        private string _name = string.Empty;
        private ResourceType _type;
        private uint _offset;
        private uint _baseOffset;
        private uint _size;
        private byte[]? _data;
        private bool _isModified;
        private string _originalFilePath = string.Empty;
        private byte[]? _originalData; // 保存替换前的原始数据
        private uint _originalSize; // 保存替换前的原始大小
        private int _width; // 图片宽度
        private int _height; // 图片高度

        /// <summary>
        /// 资源 ID
        /// </summary>
        public uint Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 资源名称（从 RES.H 解析）
        /// </summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 资源类型
        /// </summary>
        public ResourceType Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 在资源文件中的相对偏移地址
        /// </summary>
        public uint Offset
        {
            get => _offset;
            set { _offset = value; OnPropertyChanged(); OnPropertyChanged(nameof(OffsetDisplay)); }
        }

        /// <summary>
        /// 资源区基地址（相对于父文件，用于DestBin模式）
        /// </summary>
        public uint BaseOffset
        {
            get => _baseOffset;
            set { _baseOffset = value; OnPropertyChanged(); OnPropertyChanged(nameof(OffsetDisplay)); }
        }

        /// <summary>
        /// 资源大小（字节）
        /// </summary>
        public uint Size
        {
            get => _size;
            set { _size = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeDisplay)); }
        }

        /// <summary>
        /// 资源原始数据
        /// </summary>
        public byte[]? Data
        {
            get => _data;
            set { _data = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 是否已被修改
        /// </summary>
        public bool IsModified
        {
            get => _isModified;
            set { _isModified = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); }
        }

        /// <summary>
        /// 原始文件路径
        /// </summary>
        public string OriginalFilePath
        {
            get => _originalFilePath;
            set { _originalFilePath = value; }
        }

        /// <summary>
        /// 替换前的原始数据（用于恢复）
        /// </summary>
        public byte[]? OriginalData
        {
            get => _originalData;
            set { _originalData = value; }
        }

        /// <summary>
        /// 替换前的原始大小
        /// </summary>
        public uint OriginalSize
        {
            get => _originalSize;
            set { _originalSize = value; }
        }

        /// <summary>
        /// 图片宽度（仅图片资源有效）
        /// </summary>
        public int Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); OnPropertyChanged(nameof(ResolutionDisplay)); }
        }

        /// <summary>
        /// 图片高度（仅图片资源有效）
        /// </summary>
        public int Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); OnPropertyChanged(nameof(ResolutionDisplay)); }
        }

        #region Display Properties

        /// <summary>
        /// 显示用的大小字符串
        /// </summary>
        public string SizeDisplay => $"{Size:N0} bytes ({Size / 1024.0:F2} KB)";

        /// <summary>
        /// 显示用的偏移地址（绝对偏移= 相对偏移 + 基地址偏移）
        /// </summary>
        public string OffsetDisplay => $"0x{Offset + BaseOffset:X8}";

        /// <summary>
        /// 状态显示
        /// </summary>
        public string StatusDisplay => IsModified ? "✏Modified" : "✓Original";

        /// <summary>
        /// 分辨率显示（仅图片资源有效）
        /// </summary>
        public string ResolutionDisplay
        {
            get
            {
                if (Width > 0 && Height > 0)
                    return $"{Width} × {Height}";
                return "N/A";
            }
        }

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return $"[{Id}] {Name} ({Type}) - {SizeDisplay}";
        }
    }

    /// <summary>
    /// 资源类型枚举
    /// </summary>
    public enum ResourceType
    {
        Unknown = 0,
        Jpeg = 1,
        Bitmap = 2,
        Wav = 3,
        Binary = 4,
        Font = 5,
        Text = 6,
        Palette = 7,           // 调色板资源
        GameMap = 8,          // 游戏地图资源
        IconSelection = 9,    // 图标选择资源
        EncodingTable = 10,   // 字符编码转换表
        OsdSource = 11,       // OSD屏幕显示资源
        Png = 12,             // PNG图片 (P2新增)
        Mp3 = 13              // MP3音频 (P2新增)
    }
}
