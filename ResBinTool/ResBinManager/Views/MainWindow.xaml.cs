using ResBinManager.Controls;
using ResBinManager.Core;
using ResBinManager.Models;
using ResBinManager.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ResBinManager.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel? ViewModel => DataContext as MainViewModel;

        public MainWindow()
        {
            InitializeComponent();

            if (ViewModel != null)
            {
                ViewModel.PreviewRequested += OnPreviewRequested;
                ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            // 设置窗口标题包含版本信息
            Title = $"DestBin Resource Manager v1.0.1.6";

            // 默认显示预览面板，隐藏打包面板
            // 确保两个面板不会同时可见
            BuildConfigPanel.Visibility = Visibility.Collapsed;
            PreviewPanel.Visibility = Visibility.Visible;
            WavControlPanel.Visibility = Visibility.Collapsed;
            FontControlPanel.Visibility = Visibility.Collapsed;

            // 初始化修改计数徽章
            UpdateModifiedCountBadge();
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 资源集合变化时刷新修改计数徽章
            if (e.PropertyName == nameof(MainViewModel.Resources))
            {
                UpdateModifiedCountBadge();
            }

            if (e.PropertyName == nameof(MainViewModel.SelectedResource))
            {
                // 当选中资源改变时，更新控制面板可见性
                // 注意：预览加载已由 ViewModel 的 SelectedResource setter 处理
                var resource = ViewModel?.SelectedResource;
                var resourceType = resource?.Type;

                System.Diagnostics.Debug.WriteLine($"[UI] SelectedResource changed: Type={resourceType}");

                // 如果没有选中资源，清空所有预览面板
                if (resource == null)
                {
                    System.Diagnostics.Debug.WriteLine("[UI] No resource selected, clearing all preview panels");
                    WavControlPanel.Visibility = Visibility.Collapsed;
                    FontControlPanel.Visibility = Visibility.Collapsed;
                    PaletteControlPanel.Visibility = Visibility.Collapsed;
                    OsdControlPanel.Visibility = Visibility.Collapsed;
                    TextControlPanel.Visibility = Visibility.Collapsed;
                    ImagePreviewBorder.Visibility = Visibility.Collapsed;
                    ActionButtonsPanel.Visibility = Visibility.Collapsed;
                    ClearPreview();
                    return;
                }

                if (RightTabControl.SelectedIndex == 1)
                    RightTabControl.SelectedIndex = 0;

                if (resourceType == Models.ResourceType.Wav)
                {
                    System.Diagnostics.Debug.WriteLine("[UI] Showing WAV panel");
                    WavControlPanel.Visibility = Visibility.Visible;
                    FontControlPanel.Visibility = Visibility.Collapsed;
                    PaletteControlPanel.Visibility = Visibility.Collapsed;
                    OsdControlPanel.Visibility = Visibility.Collapsed;
                    TextControlPanel.Visibility = Visibility.Collapsed;
                    ImagePreviewBorder.Visibility = Visibility.Collapsed;
                    ActionButtonsPanel.Visibility = Visibility.Visible;
                }
                else if ((resourceType == Models.ResourceType.Binary || resourceType == Models.ResourceType.Font) && IsFontResource(resource))
                {
                    System.Diagnostics.Debug.WriteLine($"[UI] Showing Font panel (Type={resourceType})");
                    WavControlPanel.Visibility = Visibility.Collapsed;
                    FontControlPanel.Visibility = Visibility.Visible;
                    PaletteControlPanel.Visibility = Visibility.Collapsed;
                    OsdControlPanel.Visibility = Visibility.Collapsed;
                    TextControlPanel.Visibility = Visibility.Collapsed;
                    ImagePreviewBorder.Visibility = Visibility.Collapsed;
                    ActionButtonsPanel.Visibility = Visibility.Collapsed;
                    // LoadFontPreview 已由 ViewModel 触发
                }
                else if (resourceType == Models.ResourceType.Jpeg || resourceType == Models.ResourceType.Bitmap)
                {
                    System.Diagnostics.Debug.WriteLine("[UI] Showing Image preview");
                    WavControlPanel.Visibility = Visibility.Collapsed;
                    FontControlPanel.Visibility = Visibility.Collapsed;
                    PaletteControlPanel.Visibility = Visibility.Collapsed;
                    OsdControlPanel.Visibility = Visibility.Collapsed;
                    TextControlPanel.Visibility = Visibility.Collapsed;
                    ImagePreviewBorder.Visibility = Visibility.Visible;
                    ActionButtonsPanel.Visibility = Visibility.Visible;
                }
                else if (resourceType == Models.ResourceType.Palette)
                {
                    System.Diagnostics.Debug.WriteLine("[UI] Showing Palette panel");
                    WavControlPanel.Visibility = Visibility.Collapsed;
                    FontControlPanel.Visibility = Visibility.Collapsed;
                    PaletteControlPanel.Visibility = Visibility.Visible;
                    OsdControlPanel.Visibility = Visibility.Collapsed;
                    TextControlPanel.Visibility = Visibility.Collapsed;
                    ImagePreviewBorder.Visibility = Visibility.Collapsed;
                    ActionButtonsPanel.Visibility = Visibility.Visible;
                }
                else if (resourceType == Models.ResourceType.OsdSource)
                {
                    System.Diagnostics.Debug.WriteLine("[UI] Showing OSD panel");
                    WavControlPanel.Visibility = Visibility.Collapsed;
                    FontControlPanel.Visibility = Visibility.Collapsed;
                    PaletteControlPanel.Visibility = Visibility.Collapsed;
                    OsdControlPanel.Visibility = Visibility.Visible;
                    TextControlPanel.Visibility = Visibility.Collapsed;
                    ImagePreviewBorder.Visibility = Visibility.Collapsed;
                    ActionButtonsPanel.Visibility = Visibility.Visible;
                }
                else if (resourceType == Models.ResourceType.Text)
                {
                    System.Diagnostics.Debug.WriteLine("[UI] Showing Text panel");
                    WavControlPanel.Visibility = Visibility.Collapsed;
                    FontControlPanel.Visibility = Visibility.Collapsed;
                    PaletteControlPanel.Visibility = Visibility.Collapsed;
                    OsdControlPanel.Visibility = Visibility.Collapsed;
                    TextControlPanel.Visibility = Visibility.Visible;
                    ImagePreviewBorder.Visibility = Visibility.Collapsed;
                    ActionButtonsPanel.Visibility = Visibility.Visible;
                }
                else if (resourceType == Models.ResourceType.Binary ||
                         resourceType == Models.ResourceType.GameMap ||
                         resourceType == Models.ResourceType.EncodingTable ||
                         resourceType == Models.ResourceType.IconSelection)
                {
                    System.Diagnostics.Debug.WriteLine($"[UI] Showing Binary preview for {resourceType}");
                    WavControlPanel.Visibility = Visibility.Collapsed;
                    FontControlPanel.Visibility = Visibility.Collapsed;
                    PaletteControlPanel.Visibility = Visibility.Collapsed;
                    OsdControlPanel.Visibility = Visibility.Collapsed;
                    TextControlPanel.Visibility = Visibility.Collapsed;
                    ImagePreviewBorder.Visibility = Visibility.Visible;
                    ActionButtonsPanel.Visibility = Visibility.Visible;

                    // 显示默认的二进制文件图标
                    ShowDefaultBinaryIcon();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[UI] Showing default preview");
                    if (resourceType == Models.ResourceType.Font)
                        ShowDefaultBinaryIcon();
                    WavControlPanel.Visibility = Visibility.Collapsed;
                    FontControlPanel.Visibility = Visibility.Collapsed;
                    OsdControlPanel.Visibility = Visibility.Collapsed;
                    TextControlPanel.Visibility = Visibility.Collapsed;
                    ImagePreviewBorder.Visibility = Visibility.Visible;
                    ActionButtonsPanel.Visibility = Visibility.Visible;
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.FontInfo))
            {
                // 字体信息加载完成后，刷新预览
                if (ViewModel?.FontInfo != null)
                {
                    LoadFontPreview();
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.ConfigItems))
            {
                RightTabControl.SelectedIndex = 1;
            }
        }

        /// <summary>
        /// 判断是否为字体资源
        /// </summary>
        private bool IsFontResource(ResourceItem? resource)
        {
            if (resource == null) return false;

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
                if (ViewModel?.CurrentFileData != null && resource.Offset + resource.Size <= ViewModel?.CurrentFileData.Length)
                {
                    data = new byte[resource.Size];
                    Array.Copy(ViewModel.CurrentFileData, (int)resource.Offset, data, 0, (int)resource.Size);
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

                        int currentIdx = ViewModel.Resources.IndexOf(resource);
                        if (currentIdx >= 0)
                        {
                            bool hasAdjacentFontData = ViewModel.CheckAdjacentResourceForCharCount(currentIdx, -1) ||
                                                        ViewModel.CheckAdjacentResourceForCharCount(currentIdx, 1);
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
                        if (charCount >= 100 && charCount <= 60000)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Font] Resource '{resource.Name}' has valid char count: {charCount}");

                            int currentIdx = ViewModel.Resources.IndexOf(resource);
                            if (currentIdx >= 0)
                            {
                                bool hasAdjacentFontIdx = ViewModel.CheckAdjacentResourceForMagic(currentIdx, -1) ||
                                                           ViewModel.CheckAdjacentResourceForMagic(currentIdx, 1);
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




            System.Diagnostics.Debug.WriteLine($"[UI] Resource '{resource.Name}' (ID={resource.Id}) is font: {false}");
            return false;
        }

        /// <summary>
        /// 加载字体预览控件
        /// </summary>
        private void LoadFontPreview()
        {
            if (ViewModel?.FontInfo == null)
            {
                System.Diagnostics.Debug.WriteLine("[UI] LoadFontPreview: FontInfo is null");
                return;
            }

            if (ViewModel.FontData == null || ViewModel.FontIndex == null)
            {
                System.Diagnostics.Debug.WriteLine("[UI] LoadFontPreview: FontData or FontIndex is null");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[UI] Loading font preview: {ViewModel.FontInfo.DisplayName}");

            try
            {
                // 创建或更新字体预览控件（传入 font.bin 以构建 charCode 映射）
                var fontPreview = new FontPreviewControl();
                fontPreview.LoadFont(ViewModel.FontData, ViewModel.FontIndex, ViewModel.FontBinData);
                fontPreview.CharSelected += FontPreview_CharSelected;

                FontPreviewContainer.Content = fontPreview;
                System.Diagnostics.Debug.WriteLine("[UI] Font preview loaded successfully");
                
                UpdateFontStats();
                PopulateCharRangeComboBox();
                UpdateFontBinStatus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UI] Font preview error: {ex.Message}");
                MessageBox.Show($"Failed to load font preview:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 更新 font.bin 加载状态提示
        /// </summary>
        private void UpdateFontBinStatus()
        {
            if (ViewModel?.FontInfo != null && ViewModel.FontInfo.CharCodeMap.Count > 0)
            {
                FontStatsText.Text = $"Chars: {ViewModel.FontInfo.Characters.Count} | " +
                                    $"Langs: {ViewModel.FontInfo.LanguageCount} | " +
                                    $"Strings: {ViewModel.FontInfo.Languages.FirstOrDefault()?.StringCount ?? 0} | " +
                                    $"CharCodeMap: {ViewModel.FontInfo.CharCodeMap.Count} entries";
                FontStatsText.Visibility = Visibility.Visible;
            }
            else
            {
                FontStatsText.Text = $"Chars: {ViewModel.FontInfo.Characters.Count} | " +
                                    $"Langs: {ViewModel.FontInfo.LanguageCount} | " +
                                    $"Strings: {ViewModel.FontInfo.Languages.FirstOrDefault()?.StringCount ?? 0} | " +
                                    "⚠ font.bin not loaded, charCode search disabled";
                FontStatsText.Visibility = Visibility.Visible;
            }
        }

        private void UpdateFontStats()
        {
            if (ViewModel.FontInfo != null)
            {
                FontStatsText.Text = $"Chars: {ViewModel.FontInfo.Characters.Count} | " +
                                    $"Langs: {ViewModel.FontInfo.LanguageCount} | " +
                                    $"Strings: {ViewModel.FontInfo.Languages.FirstOrDefault()?.StringCount ?? 0}";
            }
            else
            {
                FontStatsText.Text = string.Empty;
            }
        }

        private void CharSearchBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (FontPreviewContainer.Content is FontPreviewControl fontPreview)
            {
                var searchBox = sender as TextBox;
                if (searchBox != null)
                {
                    string searchText = searchBox.Text.Trim();
                    if (searchText == searchBox.Tag?.ToString() || string.IsNullOrEmpty(searchText))
                        return;

                    // 尝试按 unicode 十六进制搜索（如 "0x4E00" 或 "4E00"）
                    if (searchText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                        searchText.StartsWith("0X"))
                    {
                        if (uint.TryParse(searchText.Substring(2),
                            System.Globalization.NumberStyles.HexNumber, null, out uint charCode))
                        {
                            if (string.IsNullOrEmpty(searchText) || searchText == searchBox.Tag?.ToString())
                                return;

                            if (fontPreview.FontInfo?.CharCodeMap == null ||
                                fontPreview.FontInfo.CharCodeMap.Count == 0)
                            {
                                FontStatsText.Text = "⚠ font.bin 未加载，无法按 unicode 搜索字符";
                                return;
                            }

                            if (fontPreview.LocateCharByCode(charCode))
                            {
                                FontStatsText.Text = $"Located char 0x{charCode:X4}";
                                return;
                            }
                            else
                            {
                                FontStatsText.Text = $"Char 0x{charCode:X4} not found";
                            }
                        }
                    }
                    // 尝试按纯数字（序号）搜索
                    else if (int.TryParse(searchText, out int charIndex))
                    {
                        fontPreview.SetCharRange(charIndex, charIndex + 200);
                        FontStatsText.Text = $"Range: {charIndex} - {charIndex + 200}";
                    }
                    // 尝试按实际字符搜索（取第一个字符的 unicode）
                    else if (searchText.Length > 0)
                    {
                        if (fontPreview.FontInfo?.CharCodeMap == null ||
                            fontPreview.FontInfo.CharCodeMap.Count == 0)
                        {
                            FontStatsText.Text = "⚠ font.bin 未加载，无法按字符搜索，请使用序号搜索";
                            return;
                        }

                        uint charCode = (uint)searchText[0];
                        if (fontPreview.LocateCharByCode(charCode))
                        {
                            FontStatsText.Text = $"Located char '{searchText[0]}' (0x{charCode:X4})";
                        }
                        else
                        {
                            FontStatsText.Text = $"Char '{searchText[0]}' not found in font";
                        }
                    }
                }
            }
        }

        private void CharSearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var searchBox = sender as TextBox;
            if (searchBox != null && searchBox.Text == searchBox.Tag?.ToString())
            {
                searchBox.Text = string.Empty;
                searchBox.Foreground = Brushes.Black;
            }
        }

        private void CharSearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var searchBox = sender as TextBox;
            if (searchBox != null && string.IsNullOrEmpty(searchBox.Text))
            {
                searchBox.Text = searchBox.Tag?.ToString();
                searchBox.Foreground = Brushes.Gray;
            }
        }

        private void StringListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as ListBox;
            if (listBox != null && listBox.SelectedItem is FontStringItem item)
            {
                System.Diagnostics.Debug.WriteLine($"[UI] Selected string: {item.DisplayText}");

                // 在字符详情面板展示字符串的 charIndex 序列
                CharDetailGroupBox.Visibility = Visibility.Visible;
                CharDetailsGrid.Visibility = Visibility.Visible;
                CharIndexText.Text = item.Index.ToString();
                CharSizeText.Text = $"{item.Width} x {item.Height}";

                if (item.StringInfos != null)
                {
                    CharOffsetText.Text = $"0x{item.StringInfos.DataOffset:X6}";
                    BuildCharIndexSeqDetails(item.StringInfos);
                }
                else
                {
                    CharOffsetText.Text = string.Empty;
                    CharIndexSeqText.Text = string.Empty;
                    CharGlyphSizesText.Text = string.Empty;
                }

                // 尝试渲染字符串的合成位图
                if (ViewModel?.FontData != null && ViewModel.FontInfo != null &&
                    item.StringInfos != null && item.StringInfos.CharIndices.Length > 0)
                {
                    try
                    {
                        var pixels = FontInfoParser.ComposeStringPixels(
                            ViewModel.FontData, ViewModel.FontInfo, item.StringInfos);

                        if (pixels != null)
                        {
                            var bitmap = Controls.FontPreviewControl.PixelsToBitmapSource(
                                pixels, pixels.GetLength(1), pixels.GetLength(0));

                            System.Diagnostics.Debug.WriteLine($"[UI] Composed string bitmap: {pixels.GetLength(1)}x{pixels.GetLength(0)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UI] String composition failed: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 构建选中字符串的 charIndex 序列与字形尺寸详情
        /// </summary>
        private void BuildCharIndexSeqDetails(Core.StringInfo strInfo)
        {
            var characters = ViewModel?.FontInfo?.Characters;
            var charIndices = strInfo.CharIndices;

            if (characters == null || charIndices.Length == 0)
            {
                CharIndexSeqText.Text = "(empty)";
                CharGlyphSizesText.Text = string.Empty;
                return;
            }

            var seqSb = new StringBuilder();
            var sizeSb = new StringBuilder();

            for (int i = 0; i < charIndices.Length; i++)
            {
                ushort idx = charIndices[i];
                seqSb.Append($"0x{idx:X4} ");

                if (idx < characters.Count)
                {
                    var ci = characters[idx];
                    sizeSb.Append($"[0x{idx:X4}:{ci.Width}x{ci.Height}] ");
                }
                else
                {
                    sizeSb.Append($"[0x{idx:X4}:?x?] ");
                }
            }

            CharIndexSeqText.Text = seqSb.ToString().TrimEnd();
            CharGlyphSizesText.Text = sizeSb.ToString().TrimEnd();
        }

        private void FontPreview_CharSelected(object? sender, FontPreviewControl.CharSelectionEventArgs e)
        {
            CharDetailGroupBox.Visibility = Visibility.Visible;
            CharDetailsGrid.Visibility = Visibility.Visible;
            CharIndexText.Text = e.Index.ToString();
            CharSizeText.Text = $"{e.CharInfo.Width} x {e.CharInfo.Height}";
            CharOffsetText.Text = $"0x{e.CharInfo.Offset:X8}";
            // 单字符详情不显示字符串序列
            CharIndexSeqText.Text = string.Empty;
            CharGlyphSizesText.Text = string.Empty;

            System.Diagnostics.Debug.WriteLine($"[UI] Selected char #{e.Index}: {e.CharInfo.Width}x{e.CharInfo.Height} at offset 0x{e.CharInfo.Offset:X8}");
        }

        private void OnPreviewRequested(object? sender, ResourceItem resource)
        {
            if (resource == null)
            {
                ClearPreview();
                return;
            }

            try
            {
                // 根据类型显示预览
                switch (resource.Type)
                {
                    case ResourceType.Jpeg:
                    case ResourceType.Bitmap:
                        // 从 ViewModel 获取最新的文件数据来显示图片
                        if (ViewModel != null && ViewModel.CurrentFileData != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Preview] Extracting image data: Offset=0x{resource.Offset:X8}, Size={resource.Size}");

                            // 验证偏移量和大小是否合理
                            if (resource.Offset + resource.Size > ViewModel.CurrentFileData.Length)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Preview] ERROR: Offset + Size exceeds file length!");
                                System.Diagnostics.Debug.WriteLine($"[Preview]   File length: {ViewModel.CurrentFileData.Length}");
                                System.Diagnostics.Debug.WriteLine($"[Preview]   Offset: 0x{resource.Offset:X8} ({resource.Offset})");
                                System.Diagnostics.Debug.WriteLine($"[Preview]   Size: {resource.Size}");
                                System.Diagnostics.Debug.WriteLine($"[Preview]   End position: 0x{resource.Offset + resource.Size:X8} ({resource.Offset + resource.Size})");
                                MessageBox.Show(
                                    $"Invalid resource offset or size!\n\n" +
                                    $"File length: {ViewModel.CurrentFileData.Length}\n" +
                                    $"Resource offset: 0x{resource.Offset:X8}\n" +
                                    $"Resource size: {resource.Size}\n\n" +
                                    $"This usually happens after replacement without proper offset synchronization.",
                                    "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                                ClearPreview();
                                return;
                            }

                            var imageData = new byte[resource.Size];
                            Array.Copy(ViewModel.CurrentFileData, resource.Offset, imageData, 0, resource.Size);

                            // 输出前几个字节用于调试
                            if (imageData.Length >= 4)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Preview] First 4 bytes: {imageData[0]:X2} {imageData[1]:X2} {imageData[2]:X2} {imageData[3]:X2}");
                            }

                            ShowImagePreview(imageData);
                        }
                        else if (resource.Data != null)
                        {
                            // Fallback: 使用资源自带的 Data
                            ShowImagePreview(resource.Data);
                        }
                        else
                        {
                            ClearPreview();
                        }
                        break;

                    default:
                        // 其他类型由 ViewModel 和 OnViewModelPropertyChanged 处理
                        ClearPreview();
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Preview failed: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                ClearPreview();
            }
        }

        private void ShowImagePreview(byte[] imageData)
        {
            try
            {
                // 使用 BitmapDecoder 来更可靠地解码图片
                BitmapImage bitmap = null;

                using (var ms = new MemoryStream(imageData))
                {
                    bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // 必须在 BeginInit 和 EndInit 之间设置
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze(); // 使位图可在 UI 线程外访问
                }

                // 在 using 块之外设置 Source，因为 Freeze 后可以在任何线程访问
                PreviewImage.Source = bitmap;

                System.Diagnostics.Debug.WriteLine($"[Preview] Image loaded successfully, Size: {imageData.Length} bytes");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Preview] Failed to load image: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[Preview] Stack trace: {ex.StackTrace}");

                MessageBox.Show($"Failed to load image: {ex.Message}\n\nThis may be due to an unsupported image format or corrupted data.",
                              "Warning",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                ClearPreview();
            }
        }

        private void ShowWaveformInfo(ResourceItem resource)
        {
            ClearPreview();

            // 显示 WAV 文件信息
            var infoText = $"WAV Audio File\n" +
                          $"Size: {resource.SizeDisplay}\n" +
                          $"Duration: ~{(resource.Size / 32000.0):F1} seconds\n" +
                          $"(assuming 16kHz, 16-bit mono)";

            // 可以在这里添加更复杂的波形可视化
            MessageBox.Show(infoText, "WAV Resource Info",
                          MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowBinaryInfo(ResourceItem resource)
        {
            ClearPreview();

            var infoText = $"Binary Resource\n" +
                          $"Type: {resource.Type}\n" +
                          $"Size: {resource.SizeDisplay}\n" +
                          $"Offset: {resource.OffsetDisplay}";

            MessageBox.Show(infoText, "Binary Resource Info",
                          MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ClearPreview()
        {
            PreviewImage.Source = null;
        }

        /// <summary>
        /// OsdIcon 点击事件处理，设置选中状态
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OsdIcon_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Border border && border.DataContext is Models.OsdIconPreviewItem item)
            {
                if (ViewModel != null && ViewModel.OsdInfo != null)
                {
                    foreach (var icon in ViewModel.OsdInfo.Icons)
                    {
                        icon.IsSelected = false;
                    }
                    item.IsSelected = true;
                    ViewModel.OsdInfo.SelectedIcon = item;
                }
            }
        }

        /// <summary>
        /// 显示默认的二进制文件图标
        /// </summary>
        private void ShowDefaultBinaryIcon()
        {
            try
            {
                // 创建一个简单的蓝色文档图标
                var drawingVisual = new DrawingVisual();
                using (var drawingContext = drawingVisual.RenderOpen())
                {
                    // 背景 - 浅灰色
                    var backgroundBrush = new SolidColorBrush(Color.FromRgb(240, 240, 240));
                    drawingContext.DrawRectangle(backgroundBrush, null, new Rect(0, 0, 100, 100));

                    // 文档形状 - 白色
                    var docBrush = new SolidColorBrush(Colors.White);
                    var docPen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 100)), 2);
                    drawingContext.DrawRectangle(docBrush, docPen, new Rect(20, 15, 60, 70));

                    // "BIN" 文字
                    var textBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215));
                    var formattedText = new FormattedText(
                        "BIN",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Arial"),
                        24,
                        textBrush,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);

                    double x = (100 - formattedText.Width) / 2;
                    double y = (100 - formattedText.Height) / 2;
                    drawingContext.DrawText(formattedText, new Point(x, y));
                }

                // 转换为 BitmapSource
                var renderTarget = new RenderTargetBitmap(
                    100, 100, 96, 96, PixelFormats.Pbgra32);
                renderTarget.Render(drawingVisual);
                renderTarget.Freeze();

                PreviewImage.Source = renderTarget;
                System.Diagnostics.Debug.WriteLine("[Preview] Default binary icon displayed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Preview] Failed to load default icon: {ex.Message}");
                ClearPreview();
            }
        }

        /// <summary>
        /// 切换到固件打包面板
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleBuildPanel_Checked(object sender, RoutedEventArgs e)
        {
            // 显示固件打包面板，隐藏预览面板
            SwitchPanel(showBuildPanel: true);
        }

        /// <summary>
        /// 切换回资源预览面板
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleBuildPanel_Unchecked(object sender, RoutedEventArgs e)
        {
            // 显示预览面板，隐藏固件打包面板
            SwitchPanel(showBuildPanel: false);
        }

        /// <summary>
        /// 安全地切换面板显示，确保任何时候只有一个面板可见
        /// </summary>
        /// <param name="showBuildPanel">true=显示打包面板，false=显示预览面板</param>
        private void SwitchPanel(bool showBuildPanel)
        {
            // 使用 Dispatcher 确保 UI 更新顺序正确，避免重叠
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (showBuildPanel)
                {
                    // 先隐藏预览面板，再显示打包面板
                    PreviewPanel.Visibility = Visibility.Collapsed;
                    BuildConfigPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    // 先隐藏打包面板，再显示预览面板
                    BuildConfigPanel.Visibility = Visibility.Collapsed;
                    PreviewPanel.Visibility = Visibility.Visible;
                }
            });
        }

        // ==================== Font 预览控制方法 ====================

        private double _currentZoom = 1.0;

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _currentZoom = Math.Min(_currentZoom + 0.2, 3.0);
            UpdateZoomLevel();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _currentZoom = Math.Max(_currentZoom - 0.2, 0.4);
            UpdateZoomLevel();
        }

        private void ShowGrid_Checked(object sender, RoutedEventArgs e)
        {
            if (FontPreviewContainer.Content is FontPreviewControl fontPreview)
            {
                fontPreview.ShowGrid = true;
            }
        }

        private void ShowGrid_Unchecked(object sender, RoutedEventArgs e)
        {
            if (FontPreviewContainer.Content is FontPreviewControl fontPreview)
            {
                fontPreview.ShowGrid = false;
            }
        }

        private void PopulateCharRangeComboBox()
        {
            CharRangeComboBox.Items.Clear();
            
            int totalChars = ViewModel.FontInfo?.Characters.Count ?? 0;
            int chunkSize = 200;
            
            CharRangeComboBox.Items.Add(new ComboBoxItem { Content = "All" });
            
            for (int i = 0; i < totalChars; i += chunkSize)
            {
                int end = Math.Min(i + chunkSize, totalChars);
                CharRangeComboBox.Items.Add(new ComboBoxItem { Content = $"{i}-{end}" });
            }
            
            if (CharRangeComboBox.Items.Count > 0)
            {
                CharRangeComboBox.SelectedIndex = 0;
            }
        }

        private void CharRangeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontPreviewContainer.Content is FontPreviewControl fontPreview)
            {
                var comboBox = sender as ComboBox;
                if (comboBox != null && comboBox.SelectedItem is ComboBoxItem item)
                {
                    string content = item.Content.ToString();
                    int totalChars = ViewModel.FontInfo?.Characters.Count ?? 0;
                    
                    if (content == "All")
                    {
                        fontPreview.SetCharRange(0, totalChars);
                        CurrentRangeText.Text = $"0 - {totalChars}";
                    }
                    else
                    {
                        string[] parts = content.Split('-');
                        if (parts.Length == 2 && 
                            int.TryParse(parts[0], out int start) && 
                            int.TryParse(parts[1], out int end))
                        {
                            fontPreview.SetCharRange(start, end);
                            CurrentRangeText.Text = $"{start} - {end}";
                        }
                    }
                }
            }
        }

        private void UpdateZoomLevel()
        {
            ZoomLevelText.Text = $"{(int)(_currentZoom * 100)}%";

            if (FontPreviewContainer.Content is FontPreviewControl fontPreview)
            {
                fontPreview.ZoomLevel = _currentZoom;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // 清理资源
            if (ViewModel != null)
            {
                ViewModel.PreviewRequested -= OnPreviewRequested;
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
        }

        private void ConfigTemplateComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // 配置模板选择变化时的处理已在 ViewModel 中通过绑定完成
        }

        private void ConfigComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // 配置项选择变化时的处理已在 ViewModel 中通过绑定完成
            if (ViewModel != null)
            {
                ViewModel.IsConfigModified = true;
            }
        }

        #region 资源列表搜索过滤

        /// <summary>
        /// 资源列表实时过滤回调
        /// </summary>
        private void ResourcesViewSource_Filter(object sender, FilterEventArgs e)
        {
            if (e.Item is ResourceItem resource)
            {
                var searchText = SearchBox?.Text?.Trim();
                if (string.IsNullOrEmpty(searchText))
                {
                    e.Accepted = true;
                }
                else
                {
                    e.Accepted = resource.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                             || resource.Id.ToString().Contains(searchText)
                             || resource.Type.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            else
            {
                e.Accepted = false;
            }
        }

        /// <summary>
        /// 搜索框文本变化时刷新过滤视图
        /// </summary>
        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (Resources["ResourcesViewSource"] is CollectionViewSource cvs)
            {
                cvs.View.Refresh();
            }
        }

        /// <summary>
        /// 更新已修改资源计数徽章
        /// </summary>
        private void UpdateModifiedCountBadge()
        {
            var resources = ViewModel?.Resources;
            if (resources == null || resources.Count == 0)
            {
                ModifiedCountBadge.Visibility = Visibility.Collapsed;
                return;
            }

            int modifiedCount = resources.Count(r => r.IsModified);
            if (modifiedCount > 0)
            {
                ModifiedCountText.Text = $"{modifiedCount} 已修改";
                ModifiedCountBadge.Visibility = Visibility.Visible;
            }
            else
            {
                ModifiedCountBadge.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

    }
}
