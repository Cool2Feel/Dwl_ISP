using ResBinManager.Core;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace ResBinManager.Models
{
    public class OsdIconPreviewItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public uint DataOffset { get; set; }
        public int PixelCount { get; set; }
        public int DataSize { get; set; }
        public byte[]? IconData { get; set; }
        public byte[]? RawIndexData { get; set; }
        public string ResolutionDisplay => $"{Width} × {Height}";
        public string DataOffsetDisplay => $"0x{DataOffset:X4} ({DataOffset})";
        public string PixelCountDisplay => $"{PixelCount} px ({DataSize} bytes)";
        public string TooltipDisplay => $"[{Index}] {Name}\n{ResolutionDisplay} | Offset: 0x{DataOffset:X4} | {PixelCount}px";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class OsdInfo : INotifyPropertyChanged
    {
        private OsdIconPreviewItem? _selectedIcon;

        public int IconCount { get; set; }
        public int HeaderSize { get; set; }
        public int TotalPixels { get; set; }
        public int TotalSize { get; set; }
        public List<OsdIconPreviewItem> Icons { get; set; } = new List<OsdIconPreviewItem>();

        public OsdIconPreviewItem? SelectedIcon
        {
            get => _selectedIcon;
            set
            {
                _selectedIcon = value;
                OnPropertyChanged(nameof(SelectedIcon));
            }
        }

        public string DisplayName => $"OSD Source: {IconCount} icons";
        public string StatsDisplay
        {
            get
            {
                if (Icons.Count == 0)
                    return $"Header: {HeaderSize} bytes, {TotalPixels} total pixels, {TotalSize} bytes total";

                int minW = Icons.Min(i => i.Width);
                int maxW = Icons.Max(i => i.Width);
                int minH = Icons.Min(i => i.Height);
                int maxH = Icons.Max(i => i.Height);
                int avgPx = Icons.Count > 0 ? TotalPixels / Icons.Count : 0;

                return $"Header: {HeaderSize}B | {TotalPixels}px total | Size: {minW}×{minH}~{maxW}×{maxH} | Avg: {avgPx}px/icon | {TotalSize}B total";
            }
        }

        public int GetIconIndexByName(string name)
        {
            var icon = Icons.FirstOrDefault(i => i.Name.Equals(name));
            return icon != null ? icon.Index : -1;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}