using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ResBinManager.Core;

namespace ResBinManager.Controls
{
    /// <summary>
    /// 字体字符网格预览控件（带位图缓存 + unicode 搜索支持）
    /// </summary>
    public class FontPreviewControl : UserControl
    {
        private Grid _mainGrid = null!;
        private ScrollViewer _scrollViewer = null!;
        private WrapPanel _charPanel = null!;
        private TextBlock _infoText = null!;

        private FontInfo? _fontInfo;
        private byte[]? _fontData;
        private double _zoomLevel = 1.0;
        private bool _showGrid = true;
        private int _charStartIndex = 0;
        private int _charEndIndex = 200;

        /// <summary>
        /// 位图缓存：key = "w_h_offset", value = frozen BitmapSource
        /// </summary>
        private readonly ConcurrentDictionary<string, BitmapSource> _bitmapCache = new();

        /// <summary>
        /// 缩放级别属性
        /// </summary>
        public static readonly DependencyProperty ZoomLevelProperty =
            DependencyProperty.Register("ZoomLevel", typeof(double), typeof(FontPreviewControl),
                new PropertyMetadata(1.0, OnZoomLevelChanged));

        public double ZoomLevel
        {
            get => (double)GetValue(ZoomLevelProperty);
            set => SetValue(ZoomLevelProperty, value);
        }

        private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (FontPreviewControl)d;
            control._zoomLevel = (double)e.NewValue;
            control.RefreshDisplay();
        }

        /// <summary>
        /// 显示网格线属性
        /// </summary>
        public static readonly DependencyProperty ShowGridProperty =
            DependencyProperty.Register("ShowGrid", typeof(bool), typeof(FontPreviewControl),
                new PropertyMetadata(true, OnShowGridChanged));

        public bool ShowGrid
        {
            get => (bool)GetValue(ShowGridProperty);
            set => SetValue(ShowGridProperty, value);
        }

        private static void OnShowGridChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (FontPreviewControl)d;
            control._showGrid = (bool)e.NewValue;
            control.RefreshDisplay();
        }

        public event EventHandler<CharSelectionEventArgs>? CharSelected;

        public FontPreviewControl()
        {
            InitializeComponents();
        }

        public class CharSelectionEventArgs : EventArgs
        {
            public int Index { get; set; }
            public CharInfo CharInfo { get; set; } = null!;
        }

        private void InitializeComponents()
        {
            // 主布局
            _mainGrid = new Grid();
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 信息文本
            _infoText = new TextBlock
            {
                Margin = new Thickness(5),
                FontSize = 12,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_infoText, 0);
            _mainGrid.Children.Add(_infoText);

            // 滚动查看器
            _scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.White
            };

            // 字符面板 - 使用 WrapPanel 实现自动换行
            _charPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemWidth = 40,
                ItemHeight = 40,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            _scrollViewer.Content = _charPanel;
            Grid.SetRow(_scrollViewer, 1);
            _mainGrid.Children.Add(_scrollViewer);

            Content = _mainGrid;
        }

        /// <summary>
        /// 加载字体数据
        /// </summary>
        public void LoadFont(byte[] fontData, byte[] fontIndex)
        {
            try
            {
                _fontInfo = FontInfoParser.Parse(fontData, fontIndex);
                _fontData = fontData;
                _bitmapCache.Clear();

                UpdateInfoText();
                RenderCharacters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load font:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ClearDisplay();
            }
        }

        /// <summary>
        /// 加载字体数据（带 font.bin 映射）
        /// </summary>
        public void LoadFont(byte[] fontData, byte[] fontIndex, byte[]? fontBinData)
        {
            try
            {
                _fontInfo = FontInfoParser.Parse(fontData, fontIndex, fontBinData);
                _fontData = fontData;
                _bitmapCache.Clear();

                UpdateInfoText();
                RenderCharacters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load font:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ClearDisplay();
            }
        }

        /// <summary>
        /// 清空显示
        /// </summary>
        public void ClearDisplay()
        {
            _fontInfo = null;
            _fontData = null;
            _bitmapCache.Clear();
            _charPanel.Children.Clear();
            _infoText.Text = "No font loaded";
        }

        /// <summary>
        /// 获取当前 FontInfo（供外部访问 CharCodeMap 等）
        /// </summary>
        public FontInfo? FontInfo => _fontInfo;

        private void UpdateInfoText()
        {
            if (_fontInfo == null)
            {
                _infoText.Text = "No font loaded";
                return;
            }

            _infoText.Text = $"Font: {_fontInfo.DisplayName} | " +
                           $"Strings: {_fontInfo.Languages.FirstOrDefault()?.StringCount ?? 0} | " +
                           $"Zoom: {(int)(_zoomLevel * 100)}% | " +
                           $"Range: {_charStartIndex}-{_charEndIndex}";
        }

        private void RenderCharacters()
        {
            if (_fontInfo == null || _fontData == null)
                return;

            _charPanel.Children.Clear();

            int start = Math.Max(0, _charStartIndex);
            int end = Math.Min(_fontInfo.Characters.Count, _charEndIndex);

            for (int i = start; i < end; i++)
            {
                var charInfo = _fontInfo.Characters[i];
                var border = CreateCharBorder(charInfo, i);
                _charPanel.Children.Add(border);
            }
        }

        public void SetCharRange(int start, int end)
        {
            _charStartIndex = start;
            _charEndIndex = end;
            RenderCharacters();
            UpdateInfoText();
        }

        /// <summary>
        /// 通过 unicode 码点搜索字符并定位到该字符附近
        /// </summary>
        /// <param name="charCode">unicode 码点</param>
        /// <returns>找到的字符索引，-1 表示未找到</returns>
        public int FindCharByCode(uint charCode)
        {
            if (_fontInfo == null || _fontInfo.CharCodeMap.Count == 0)
                return -1;

            if (_fontInfo.CharCodeMap.TryGetValue(charCode, out var charInfo))
            {
                return charInfo.Index;
            }

            return -1;
        }

        /// <summary>
        /// 通过 unicode 码点搜索并定位到该字符附近
        /// </summary>
        /// <param name="charCode">unicode 码点</param>
        /// <param name="rangeSize">定位后显示的字符范围（前后数量）</param>
        /// <returns>是否找到</returns>
        public bool LocateCharByCode(uint charCode, int rangeSize = 200)
        {
            int idx = FindCharByCode(charCode);
            if (idx < 0)
                return false;

            int start = Math.Max(0, idx - rangeSize / 4);
            int end = Math.Min(_fontInfo?.Characters.Count ?? 0, idx + rangeSize * 3 / 4);
            if (end - start < rangeSize)
            {
                if (start == 0)
                    end = Math.Min(_fontInfo?.Characters.Count ?? 0, rangeSize);
                else
                    start = Math.Max(0, end - rangeSize);
            }

            SetCharRange(start, end);
            return true;
        }

        private Border CreateCharBorder(CharInfo charInfo, int index)
        {
            var border = new Border
            {
                Width = 30 * _zoomLevel,
                Height = 30 * _zoomLevel,
                Margin = new Thickness(2),
                BorderThickness = _showGrid ? new Thickness(1) : new Thickness(0),
                BorderBrush = Brushes.LightGray,
                Background = Brushes.White,
                ToolTip = BuildCharToolTip(charInfo, index),
                Cursor = Cursors.Hand
            };

            border.MouseLeftButtonDown += (s, e) =>
            {
                // 重置所有选中边框
                foreach (Border child in _charPanel.Children)
                {
                    child.BorderBrush = Brushes.LightGray;
                    child.BorderThickness = new Thickness(1);
                }

                border.BorderBrush = Brushes.Blue;
                border.BorderThickness = new Thickness(2);

                CharSelected?.Invoke(this, new CharSelectionEventArgs
                {
                    Index = index,
                    CharInfo = charInfo
                });
            };

            border.MouseEnter += (s, e) =>
            {
                if (border.BorderBrush != Brushes.Blue)
                {
                    border.BorderBrush = Brushes.DarkGray;
                }
            };

            border.MouseLeave += (s, e) =>
            {
                if (border.BorderBrush != Brushes.Blue)
                {
                    border.BorderBrush = Brushes.LightGray;
                }
            };

            try
            {
                var image = GetOrCreateCharImage(charInfo);
                border.Child = image;
            }
            catch
            {
                var textBlock = new TextBlock
                {
                    Text = "?",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Red,
                    FontSize = 16 * _zoomLevel
                };
                border.Child = textBlock;
            }

            return border;
        }

        /// <summary>
        /// 生成字符 hover 提示信息
        /// </summary>
        private string BuildCharToolTip(CharInfo charInfo, int index)
        {
            string info = $"Char #{index}\n" +
                         $"Size: {charInfo.Width}x{charInfo.Height}\n" +
                         $"Offset: 0x{charInfo.Offset:X8}";

            if (charInfo.HasCharCode)
            {
                info += $"\nCharCode: 0x{charInfo.CharCode:X4}";
                info += $"\nGlyph: {charInfo.GetDisplayChar()}";
            }

            return info;
        }

        /// <summary>
        /// 获取或创建字符位图（使用缓存）
        /// </summary>
        private Image GetOrCreateCharImage(CharInfo charInfo)
        {
            string cacheKey = $"{charInfo.Width}_{charInfo.Height}_{charInfo.Offset}";

            if (!_bitmapCache.TryGetValue(cacheKey, out var bitmap))
            {
                var rawBitmap = FontInfoParser.ExtractCharBitmap(_fontData!, charInfo);
                var pixels = FontInfoParser.BitmapToPixels(rawBitmap, charInfo.Width, charInfo.Height);

                bitmap = CreateWriteableBitmap(pixels, charInfo.Width, charInfo.Height);
                _bitmapCache[cacheKey] = bitmap;
            }

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            return image;
        }

        /// <summary>
        /// 将像素数组转换为可冻结的 WriteableBitmap
        /// </summary>
        private static BitmapSource CreateWriteableBitmap(bool[,] pixels, int width, int height)
        {
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

            int[] pixelData = new int[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // 前景色为黑色，背景色为白色
                    byte value = pixels[y, x] ? (byte)0 : (byte)255;
                    pixelData[y * width + x] = (255 << 24) | (value << 16) | (value << 8) | value;
                }
            }

            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixelData, width * 4, 0);
            bitmap.Freeze();

            // 返回已冻结的副本，确保线程安全
            return bitmap;
        }

        /// <summary>
        /// 将像素数组转换为 WPF ImageSource（用于字符串预览等）
        /// </summary>
        public static BitmapSource PixelsToBitmapSource(bool[,] pixels, int width, int height)
        {
            return CreateWriteableBitmap(pixels, width, height);
        }

        private void RefreshDisplay()
        {
            UpdateInfoText();
            if (_fontInfo != null)
            {
                // 缩放/网格切换时只重建 UI 容器，位图从缓存中取
                RenderCharacters();
            }
        }
    }
}