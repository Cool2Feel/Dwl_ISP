using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Threading;

namespace AnalyzeBrightest
{
    public partial class MainWindow : Window
    {
        private byte[] _rawBuffer;
        private byte[] _rgbBuffer;
        private byte[] _bayerBuffer; // 8-bit 解码后的 Bayer 原始数据
        private ushort[] _raw16Buffer; // 10-bit 原始数据（16-bit 容器），保留完整精度用于 LSC 分析
        private int _width;
        private int _height;
        private int _bayerMode; // 0=RGGB, 1=GRBG, 2=GBRG, 3=BGGR
        private List<BrightestItem> _brightestPositions;
        private double _scaleX;
        private double _scaleY;
        private double _offsetX;
        private double _offsetY;
        private string _currentFileName;
        private int _selectedItemIndex = -1; // 当前选中的列表项索引
        private CancellationTokenSource _processingCts; // 用于取消之前的处理任务

        public class BrightestItem
        {
            public int Rank { get; set; }
            public string Position { get; set; }
            public string Info { get; set; }
            public int RawX { get; set; }
            public int RawY { get; set; }
            public int Brightness { get; set; }
            public double Distance { get; set; }
            public bool IsSelected { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
            _brightestPositions = new List<BrightestItem>();
            LstBrightestPositions.ItemsSource = _brightestPositions;
            SizeChanged += OnWindowSizeChanged;
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_rgbBuffer != null)
            {
                LayoutImage();
                RedrawMarkers();
            }
        }

        private void OnLoadFileClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "RAW 文件 (*.RAW)|*.RAW|所有文件 (*.*)|*.*",
                Title = "选择 RAW 图像文件"
            };

            if (dlg.ShowDialog() == true)
            {
                LoadRawFile(dlg.FileName);
            }
        }

        private void LoadRawFile(string filepath)
        {
            try
            {
                if (!File.Exists(filepath))
                {
                    MessageBox.Show($"文件不存在: {filepath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 先取消之前的处理任务，防止并发访问共享字段
                _processingCts?.Cancel();
                _processingCts?.Dispose();
                _processingCts = new CancellationTokenSource();
                var token = _processingCts.Token;

                // 读取文件并设置共享字段（在取消旧任务之后）
                _rawBuffer = File.ReadAllBytes(filepath);
                _currentFileName = System.IO.Path.GetFileName(filepath);

                // 尝试自动检测分辨率
                _width = 1920;
                _height = 1080;
                int expected = _width * _height * 2;

                if (_rawBuffer.Length != expected)
                {
                    if (_rawBuffer.Length == 1280 * 720 * 2)
                    {
                        _width = 1280;
                        _height = 720;
                    }
                    else if (_rawBuffer.Length == 3840 * 2160 * 2)
                    {
                        _width = 3840;
                        _height = 2160;
                    }
                    else if (_rawBuffer.Length == 2592 * 1944 * 2)
                    {
                        _width = 2592;
                        _height = 1944;
                    }
                    else
                    {
                        MessageBox.Show($"文件大小不匹配: 期望 {expected}, 实际 {_rawBuffer.Length}",
                            "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                TxtStatus.Text = $"正在处理: {_currentFileName}...";
                
                // 异步执行图像处理，避免 UI 线程阻塞
                _ = ProcessImageAsync(token);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "加载失败";
            }
        }

        private async Task ProcessImageAsync(CancellationToken cancellationToken)
        {
            try
            {
                // 阶段1：解码 10-bit RAW 数据
                Dispatcher.Invoke(() => TxtStatus.Text = "正在解码 10-bit RAW 数据...");
                await Task.Run(() => { DecodeRaw10To16Bit(); }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                // 阶段2：解码 RAW10 → 8-bit
                Dispatcher.Invoke(() => TxtStatus.Text = "正在转换为 8-bit 格式...");
                int pixelCount = _width * _height;
                byte[] pixels = new byte[pixelCount];
                int minVal = 255, maxVal = 0;

                await Task.Run(() =>
                {
                    // 检查是 packed 还是 unpacked 格式
                    int expectedPacked = (_width * _height * 10 + 7) / 8;
                    int expectedUnpacked = _width * _height * 2;

                    if (_rawBuffer.Length == expectedPacked)
                    {
                        // Packed RAW10: 每 4 像素占 5 字节
                        DecodeRaw10Packed(_rawBuffer, pixels, _width, _height);
                    }
                    else if (_rawBuffer.Length == expectedUnpacked)
                    {
                        // Unpacked RAW10: 每像素占 2 字节
                        for (int i = 0; i < pixelCount; i++)
                        {
                            int idx = i * 2;
                            int val = (_rawBuffer[idx] | (_rawBuffer[idx + 1] << 8)) >> 2;
                            pixels[i] = (byte)val;
                        }
                    }
                    else
                    {
                        throw new Exception($"无法识别的 RAW 格式: 文件大小 {_rawBuffer.Length}");
                    }

                    // 计算像素范围
                    for (int i = 0; i < pixelCount; i++)
                    {
                        if (pixels[i] < minVal) minVal = pixels[i];
                        if (pixels[i] > maxVal) maxVal = pixels[i];
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                }, cancellationToken);

                // 保存 8-bit Bayer 数据供鼠标悬停时使用
                _bayerBuffer = pixels;

                // 更新 UI 信息
                Dispatcher.Invoke(() =>
                {
                    TxtResolution.Text = $"分辨率: {_width}x{_height}";
                    TxtFileSize.Text = $"文件大小: {_rawBuffer.Length:N0} 字节";
                    TxtPixelRange.Text = $"像素范围: {minVal} - {maxVal}";
                    TxtImageInfo.Text = $"{_currentFileName} | {_width}x{_height} | Bayer: {GetBayerName()}";
                });

                cancellationToken.ThrowIfCancellationRequested();

                // 阶段3：Demosaic: RAW → RGB
                Dispatcher.Invoke(() => TxtStatus.Text = "正在执行去马赛克处理...");
                byte[] rgbBuffer = await Task.Run(() =>
                {
                    Demosaic(pixels, _width, _height, _bayerMode, out byte[] rgb);
                    cancellationToken.ThrowIfCancellationRequested();
                    return rgb;
                }, cancellationToken);
                _rgbBuffer = rgbBuffer;

                // 阶段4：显示 RGB 图像
                Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = "正在渲染图像...";
                    DisplayImage(_rgbBuffer, _width, _height);
                });

                cancellationToken.ThrowIfCancellationRequested();

                // 阶段5：检测最亮位置并标注
                Dispatcher.Invoke(() => TxtStatus.Text = "正在分析最亮区域...");
                await Task.Run(() => DetectAndMarkBrightest(pixels, _width, _height, _bayerMode), cancellationToken);

                // 完成
                Dispatcher.Invoke(() => TxtStatus.Text = $"已加载: {_currentFileName}");
            }
            catch (OperationCanceledException)
            {
                // 任务被取消，静默处理
                Dispatcher.Invoke(() => TxtStatus.Text = "处理已取消");
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"处理图像失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    TxtStatus.Text = "处理失败";
                });
            }
        }

        private void DecodeRaw10Packed(byte[] raw, byte[] pixels, int width, int height)
        {
            int pixelCount = width * height;
            for (int i = 0; i < pixelCount; i += 4)
            {
                int byteIdx = (i / 4) * 5;
                if (byteIdx + 4 >= raw.Length) break;

                // 每个像素的低 8 位
                int p0 = raw[byteIdx + 0];
                int p1 = raw[byteIdx + 1];
                int p2 = raw[byteIdx + 2];
                int p3 = raw[byteIdx + 3];

                // 第 5 字节包含 4 个像素的高 2 位
                int highBits = raw[byteIdx + 4];
                int h0 = (highBits >> 0) & 0x03;
                int h1 = (highBits >> 2) & 0x03;
                int h2 = (highBits >> 4) & 0x03;
                int h3 = (highBits >> 6) & 0x03;

                // 组合成 10 位值，然后右移 2 位得到 8 位
                pixels[i + 0] = (byte)(((p0 | (h0 << 8)) >> 2) & 0xFF);
                if (i + 1 < pixelCount) pixels[i + 1] = (byte)(((p1 | (h1 << 8)) >> 2) & 0xFF);
                if (i + 2 < pixelCount) pixels[i + 2] = (byte)(((p2 | (h2 << 8)) >> 2) & 0xFF);
                if (i + 3 < pixelCount) pixels[i + 3] = (byte)(((p3 | (h3 << 8)) >> 2) & 0xFF);
            }
        }

        /// <summary>
        /// 解码 RAW10 数据到 16-bit 容器，保留完整 10-bit 精度用于 LSC 分析
        /// 支持 packed 和 unpacked 两种格式
        /// </summary>
        private void DecodeRaw10To16Bit()
        {
            int pixelCount = _width * _height;
            _raw16Buffer = new ushort[pixelCount];

            int expectedPacked = (_width * _height * 10 + 7) / 8;
            int expectedUnpacked = _width * _height * 2;

            if (_rawBuffer.Length == expectedPacked)
            {
                // Packed RAW10: 每 4 像素占 5 字节
                for (int i = 0; i < pixelCount; i += 4)
                {
                    int byteIdx = (i / 4) * 5;
                    if (byteIdx + 4 >= _rawBuffer.Length) break;

                    int p0 = _rawBuffer[byteIdx + 0];
                    int p1 = _rawBuffer[byteIdx + 1];
                    int p2 = _rawBuffer[byteIdx + 2];
                    int p3 = _rawBuffer[byteIdx + 3];

                    int highBits = _rawBuffer[byteIdx + 4];
                    int h0 = (highBits >> 0) & 0x03;
                    int h1 = (highBits >> 2) & 0x03;
                    int h2 = (highBits >> 4) & 0x03;
                    int h3 = (highBits >> 6) & 0x03;

                    // 组合成 10 位值，保留完整精度
                    _raw16Buffer[i + 0] = (ushort)(p0 | (h0 << 8));
                    if (i + 1 < pixelCount) _raw16Buffer[i + 1] = (ushort)(p1 | (h1 << 8));
                    if (i + 2 < pixelCount) _raw16Buffer[i + 2] = (ushort)(p2 | (h2 << 8));
                    if (i + 3 < pixelCount) _raw16Buffer[i + 3] = (ushort)(p3 | (h3 << 8));
                }
            }
            else if (_rawBuffer.Length == expectedUnpacked)
            {
                // Unpacked RAW10: 每像素占 2 字节
                for (int i = 0; i < pixelCount; i++)
                {
                    int idx = i * 2;
                    _raw16Buffer[i] = (ushort)(_rawBuffer[idx] | (_rawBuffer[idx + 1] << 8));
                }
            }
        }

        /// <summary>
        /// 快速梯度感知 G 通道插值
        /// </summary>
        private int FastInterpolateG(int left, int right, int top, int bottom)
        {
            int gradH = Math.Abs(left - right);
            int gradV = Math.Abs(top - bottom);

            if (gradH < gradV)
                return (left + right) >> 1;
            else if (gradV < gradH)
                return (top + bottom) >> 1;
            else
                return (left + right + top + bottom) >> 2;
        }

        /// <summary>
        /// 将 RAW Bayer 数据转换为 RGB24 格式
        /// 使用快速梯度感知 G 通道插值，2x2 宏块遍历优化性能
        /// </summary>
        private unsafe void Demosaic(byte[] raw, int width, int height, int bayerMode, out byte[] rgb)
        {
            int pixelCount = width * height;
            rgb = new byte[pixelCount * 3];

            fixed (byte* pBayer = raw)
            fixed (byte* pRgb = rgb)
            {
                int rgbStride = width * 3;

                // 以 2x2 宏块为单位遍历，减少函数调用
                int blockHeight = height - 1;
                int blockWidth = width - 1;

                for (int by = 0; by < blockHeight; by += 2)
                {
                    for (int bx = 0; bx < blockWidth; bx += 2)
                    {
                        // 预计算邻居索引
                        int x0 = bx > 0 ? bx - 1 : 0;
                        int x2 = bx + 1;
                        int x3 = bx + 2 < width ? bx + 2 : width - 1;
                        int y0 = by > 0 ? by - 1 : 0;
                        int y2 = by + 1;
                        int y3 = by + 2 < height ? by + 2 : height - 1;

                        // 读取 4x3 窗口内的所有像素
                        int p00 = pBayer[y0 * width + x0];
                        int p01 = pBayer[y0 * width + bx];
                        int p02 = pBayer[y0 * width + x2];
                        int p03 = pBayer[y0 * width + x3];

                        int p10 = pBayer[by * width + x0];
                        int p11 = pBayer[by * width + bx];
                        int p12 = pBayer[by * width + x2];
                        int p13 = pBayer[by * width + x3];

                        int p20 = pBayer[y2 * width + x0];
                        int p21 = pBayer[y2 * width + bx];
                        int p22 = pBayer[y2 * width + x2];
                        int p23 = pBayer[y2 * width + x3];

                        int p30 = pBayer[y3 * width + x0];
                        int p31 = pBayer[y3 * width + bx];
                        int p32 = pBayer[y3 * width + x2];
                        int p33 = pBayer[y3 * width + x3];

                        int r00, g00, b00, r01, g01, b01, r10, g10, b10, r11, g11, b11;

                        bool isEvenRow = (by & 1) == 0;

                        switch (bayerMode)
                        {
                            case 0: // RGRG
                                if (isEvenRow)
                                {
                                    r00 = p11;
                                    g00 = FastInterpolateG(p10, p12, p01, p21);
                                    b00 = (p01 + p21) >> 1;
                                    g01 = p12;
                                    r01 = (p11 + p13) >> 1;
                                    b01 = (p02 + p22) >> 1;
                                    g10 = p21;
                                    r10 = (p11 + p31) >> 1;
                                    b10 = (p20 + p22) >> 1;
                                    r11 = p22;
                                    g11 = FastInterpolateG(p21, p23, p12, p32);
                                    b11 = (p12 + p32) >> 1;
                                }
                                else
                                {
                                    r00 = p11;
                                    g00 = FastInterpolateG(p10, p12, p01, p21);
                                    b00 = (p01 + p21) >> 1;
                                    g01 = p12;
                                    r01 = (p11 + p13) >> 1;
                                    b01 = (p02 + p22) >> 1;
                                    g10 = p21;
                                    r10 = (p11 + p31) >> 1;
                                    b10 = (p20 + p22) >> 1;
                                    r11 = p22;
                                    g11 = FastInterpolateG(p21, p23, p12, p32);
                                    b11 = (p12 + p32) >> 1;
                                }
                                break;

                            case 1: // GRGR
                                if (isEvenRow)
                                {
                                    g00 = p11;
                                    r00 = (p01 + p21) >> 1;
                                    b00 = (p00 + p02 + p20 + p22) >> 2;
                                    r01 = p12;
                                    g01 = FastInterpolateG(p11, p13, p02, p22);
                                    b01 = (p01 + p03 + p21 + p23) >> 2;
                                    r10 = p21;
                                    g10 = FastInterpolateG(p20, p22, p11, p31);
                                    b10 = (p20 + p22) >> 1;
                                    g11 = p22;
                                    r11 = (p12 + p32) >> 1;
                                    b11 = (p21 + p23) >> 1;
                                }
                                else
                                {
                                    g00 = p11;
                                    r00 = (p01 + p21) >> 1;
                                    b00 = (p00 + p02 + p20 + p22) >> 2;
                                    r01 = p12;
                                    g01 = FastInterpolateG(p11, p13, p02, p22);
                                    b01 = (p01 + p03 + p21 + p23) >> 2;
                                    r10 = p21;
                                    g10 = FastInterpolateG(p20, p22, p11, p31);
                                    b10 = (p20 + p22) >> 1;
                                    g11 = p22;
                                    r11 = (p12 + p32) >> 1;
                                    b11 = (p21 + p23) >> 1;
                                }
                                break;

                            case 2: // BGBG
                                if (isEvenRow)
                                {
                                    b00 = p11;
                                    g00 = FastInterpolateG(p10, p12, p01, p21);
                                    r00 = (p00 + p02 + p20 + p22) >> 2;
                                    g01 = p12;
                                    b01 = (p11 + p13) >> 1;
                                    r01 = (p01 + p21) >> 1;
                                    g10 = p21;
                                    b10 = (p11 + p31) >> 1;
                                    r10 = (p20 + p22) >> 1;
                                    b11 = p22;
                                    g11 = FastInterpolateG(p21, p23, p12, p32);
                                    r11 = (p11 + p13 + p31 + p33) >> 2;
                                }
                                else
                                {
                                    b00 = p11;
                                    g00 = FastInterpolateG(p10, p12, p01, p21);
                                    r00 = (p00 + p02 + p20 + p22) >> 2;
                                    g01 = p12;
                                    b01 = (p11 + p13) >> 1;
                                    r01 = (p01 + p21) >> 1;
                                    g10 = p21;
                                    b10 = (p11 + p31) >> 1;
                                    r10 = (p20 + p22) >> 1;
                                    b11 = p22;
                                    g11 = FastInterpolateG(p21, p23, p12, p32);
                                    r11 = (p11 + p13 + p31 + p33) >> 2;
                                }
                                break;

                            case 3: // GBGB
                                if (isEvenRow)
                                {
                                    g00 = p11;
                                    b00 = (p01 + p21) >> 1;
                                    r00 = (p00 + p02 + p20 + p22) >> 2;
                                    b01 = p12;
                                    g01 = FastInterpolateG(p11, p13, p02, p22);
                                    r01 = (p01 + p03 + p21 + p23) >> 2;
                                    b10 = p21;
                                    g10 = FastInterpolateG(p20, p22, p11, p31);
                                    r10 = (p20 + p22) >> 1;
                                    g11 = p22;
                                    b11 = (p12 + p32) >> 1;
                                    r11 = (p21 + p23) >> 1;
                                }
                                else
                                {
                                    g00 = p11;
                                    b00 = (p01 + p21) >> 1;
                                    r00 = (p00 + p02 + p20 + p22) >> 2;
                                    b01 = p12;
                                    g01 = FastInterpolateG(p11, p13, p02, p22);
                                    r01 = (p01 + p03 + p21 + p23) >> 2;
                                    b10 = p21;
                                    g10 = FastInterpolateG(p20, p22, p11, p31);
                                    r10 = (p20 + p22) >> 1;
                                    g11 = p22;
                                    b11 = (p12 + p32) >> 1;
                                    r11 = (p21 + p23) >> 1;
                                }
                                break;

                            default:
                                r00 = p11;
                                g00 = FastInterpolateG(p10, p12, p01, p21);
                                b00 = (p01 + p21) >> 1;
                                g01 = p12;
                                r01 = (p11 + p13) >> 1;
                                b01 = (p02 + p22) >> 1;
                                g10 = p21;
                                r10 = (p11 + p31) >> 1;
                                b10 = (p20 + p22) >> 1;
                                r11 = p22;
                                g11 = FastInterpolateG(p21, p23, p12, p32);
                                b11 = (p12 + p32) >> 1;
                                break;
                        }

                        // 写入 RGB 数据 (BGR 顺序匹配 PixelFormats.Bgr24)
                        int row0 = by * rgbStride;
                        int row1 = (by + 1) * rgbStride;
                        int col0 = bx * 3;
                        int col1 = col0 + 3;

                        pRgb[row0 + col0] = (byte)b00;
                        pRgb[row0 + col0 + 1] = (byte)g00;
                        pRgb[row0 + col0 + 2] = (byte)r00;

                        pRgb[row0 + col1] = (byte)b01;
                        pRgb[row0 + col1 + 1] = (byte)g01;
                        pRgb[row0 + col1 + 2] = (byte)r01;

                        pRgb[row1 + col0] = (byte)b10;
                        pRgb[row1 + col0 + 1] = (byte)g10;
                        pRgb[row1 + col0 + 2] = (byte)r10;

                        pRgb[row1 + col1] = (byte)b11;
                        pRgb[row1 + col1 + 1] = (byte)g11;
                        pRgb[row1 + col1 + 2] = (byte)r11;
                    }
                }

                // 处理边界行（最后一行，如果高度为奇数）
                if ((height & 1) != 0)
                {
                    int y = height - 1;
                    int rowOffset = y * rgbStride;
                    for (int x = 0; x < width; x++)
                    {
                        int r, g, b;
                        GetRgbFromBayer(pBayer, width, height, x, y, out r, out g, out b, bayerMode);
                        int idx = rowOffset + x * 3;
                        pRgb[idx] = (byte)b;
                        pRgb[idx + 1] = (byte)g;
                        pRgb[idx + 2] = (byte)r;
                    }
                }

                // 处理边界列（最后一列，如果宽度为奇数）
                if ((width & 1) != 0)
                {
                    int x = width - 1;
                    for (int y = 0; y < height - 1; y += 2)
                    {
                        int r, g, b;
                        GetRgbFromBayer(pBayer, width, height, x, y, out r, out g, out b, bayerMode);
                        int idx = (y * rgbStride) + x * 3;
                        pRgb[idx] = (byte)b;
                        pRgb[idx + 1] = (byte)g;
                        pRgb[idx + 2] = (byte)r;

                        GetRgbFromBayer(pBayer, width, height, x, y + 1, out r, out g, out b, bayerMode);
                        idx = ((y + 1) * rgbStride) + x * 3;
                        pRgb[idx] = (byte)b;
                        pRgb[idx + 1] = (byte)g;
                        pRgb[idx + 2] = (byte)r;
                    }
                }
            }
        }

        /// <summary>
        /// 从 Bayer 数据获取单个像素的 RGB 值（用于边界处理）
        /// </summary>
        private unsafe void GetRgbFromBayer(byte* pBayer, int width, int height, int x, int y,
            out int r, out int g, out int b, int bayerMode)
        {
            int x0 = x > 0 ? x - 1 : 0;
            int x2 = x < width - 1 ? x + 1 : width - 1;
            int y0 = y > 0 ? y - 1 : 0;
            int y2 = y < height - 1 ? y + 1 : height - 1;

            int p00 = pBayer[y0 * width + x0];
            int p01 = pBayer[y0 * width + x];
            int p02 = pBayer[y0 * width + x2];
            int p10 = pBayer[y * width + x0];
            int p11 = pBayer[y * width + x];
            int p12 = pBayer[y * width + x2];
            int p20 = pBayer[y2 * width + x0];
            int p21 = pBayer[y2 * width + x];
            int p22 = pBayer[y2 * width + x2];

            bool isEvenRow = (y & 1) == 0;
            bool isEvenCol = (x & 1) == 0;

            switch (bayerMode)
            {
                case 0: // RGRG
                    if (isEvenRow && isEvenCol) { r = p11; g = FastInterpolateG(p10, p12, p01, p21); b = (p01 + p21) >> 1; }
                    else if (isEvenRow) { g = p11; r = (p10 + p12) >> 1; b = (p01 + p21) >> 1; }
                    else if (isEvenCol) { g = p11; r = (p10 + p12) >> 1; b = (p01 + p21) >> 1; }
                    else { r = p11; g = FastInterpolateG(p10, p12, p01, p21); b = (p01 + p21) >> 1; }
                    break;
                case 1: // GRGR
                    if (isEvenRow && isEvenCol) { g = p11; r = (p01 + p21) >> 1; b = (p00 + p02 + p20 + p22) >> 2; }
                    else if (isEvenRow) { r = p11; g = FastInterpolateG(p10, p12, p01, p21); b = (p01 + p21) >> 1; }
                    else if (isEvenCol) { r = p11; g = FastInterpolateG(p10, p12, p01, p21); b = (p01 + p21) >> 1; }
                    else { g = p11; r = (p01 + p21) >> 1; b = (p00 + p02 + p20 + p22) >> 2; }
                    break;
                case 2: // BGBG
                    if (isEvenRow && isEvenCol) { b = p11; g = FastInterpolateG(p10, p12, p01, p21); r = (p00 + p02 + p20 + p22) >> 2; }
                    else if (isEvenRow) { g = p11; b = (p10 + p12) >> 1; r = (p01 + p21) >> 1; }
                    else if (isEvenCol) { g = p11; b = (p10 + p12) >> 1; r = (p01 + p21) >> 1; }
                    else { b = p11; g = FastInterpolateG(p10, p12, p01, p21); r = (p00 + p02 + p20 + p22) >> 2; }
                    break;
                case 3: // GBGB
                    if (isEvenRow && isEvenCol) { g = p11; b = (p01 + p21) >> 1; r = (p00 + p02 + p20 + p22) >> 2; }
                    else if (isEvenRow) { b = p11; g = FastInterpolateG(p10, p12, p01, p21); r = (p01 + p21) >> 1; }
                    else if (isEvenCol) { b = p11; g = FastInterpolateG(p10, p12, p01, p21); r = (p01 + p21) >> 1; }
                    else { g = p11; b = (p01 + p21) >> 1; r = (p00 + p02 + p20 + p22) >> 2; }
                    break;
                default:
                    r = p11; g = FastInterpolateG(p10, p12, p01, p21); b = (p01 + p21) >> 1;
                    break;
            }
        }

        private void DisplayImage(byte[] rgb, int width, int height)
        {
            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
            bmp.WritePixels(new Int32Rect(0, 0, width, height), rgb, width * 3, 0);
            bmp.Freeze();

            ImgDisplay.Source = bmp;
            LayoutImage();
        }

        private void LayoutImage()
        {
            double canvasWidth = ImgCanvas.ActualWidth;
            double canvasHeight = ImgCanvas.ActualHeight;

            if (canvasWidth > 0 && canvasHeight > 0 && ImgDisplay.Source != null)
            {
                double scale = Math.Min(canvasWidth / _width, canvasHeight / _height);
                _scaleX = scale;
                _scaleY = scale;

                _offsetX = (canvasWidth - _width * scale) / 2;
                _offsetY = (canvasHeight - _height * scale) / 2;

                ImgDisplay.Width = _width * scale;
                ImgDisplay.Height = _height * scale;
                Canvas.SetLeft(ImgDisplay, _offsetX);
                Canvas.SetTop(ImgDisplay, _offsetY);
            }
        }

        /// <summary>
        /// 量产级 LSC 标定最亮点检测算法
        /// 优化1：基于 2x2 Bayer 宏块的层级结构，物理意义明确
        /// 优化2：构建原始 10-bit 数据的积分图，支持 O(1) 任意区域均值查询
        /// 优化3：Two-Pass 连通域算法，纯线性扫描，无动态内存分配
        /// 优化4：几何重心替代亮度加权质心，更符合 LSC 光学中心物理模型
        /// </summary>
        private void DetectAndMarkBrightest(byte[] pixels, int width, int height, int bayerMode)
        {
            // 使用 16-bit 原始数据进行 LSC 分析（保留完整 10-bit 精度）
            if (_raw16Buffer == null)
            {
                MessageBox.Show("16-bit RAW 数据未初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // ===== 读取用户配置参数 =====
            int macroblockFilterSize = 4; // 宏块均值滤波半径（4 表示 4x4 宏块 = 8x8 像素）
            double plateauThresholdRatio = 0.98; // 高原区阈值（最大值的 98%）
            double centerWeightAlpha = 1.0; // 亮度权重
            double distanceWeightBeta = 0.01; // 距离惩罚权重


            Dispatcher.Invoke(() => {
                if (int.TryParse(TxtFilterRadius?.Text, out int filterVal) && filterVal > 0)
                    macroblockFilterSize = filterVal;
                if (double.TryParse(TxtThresholdRatio?.Text, out double thVal) && thVal > 0 && thVal <= 1.0)
                    plateauThresholdRatio = thVal;
            });

            // ===== 层级 1：构建 2x2 Bayer 宏块亮度图 =====
            // 物理意义：每个宏块包含 R, G, G, B 四个像素，求和后抵消通道差异
            // 对于奇数分辨率，向下取整（最后一行/列无法构成完整 2x2 宏块，对 LSC 标定影响可忽略）
            int macroblockCols = width / 2;
            int macroblockRows = height / 2;
            int macroblockCount = macroblockRows * macroblockCols;

            // 构建宏块亮度图和积分图
            int[] macroblockSum = ArrayPool<int>.Shared.Rent(macroblockCount);
            long[] integral = ArrayPool<long>.Shared.Rent((macroblockRows + 1) * (macroblockCols + 1));

            try
            {
                // Step 1: 计算 2x2 宏块亮度（直接求和，代表物理光照强度）
                int rawMax = 0;
                for (int my = 0; my < macroblockRows; my++)
                {
                    for (int mx = 0; mx < macroblockCols; mx++)
                    {
                        int py = my * 2;
                        int px = mx * 2;

                        // 读取 2x2 宏块的 4 个像素（10-bit 原始值）
                        int p00 = _raw16Buffer[py * width + px];
                        int p01 = _raw16Buffer[py * width + px + 1];
                        int p10 = _raw16Buffer[(py + 1) * width + px];
                        int p11 = _raw16Buffer[(py + 1) * width + px + 1];

                        int sum = p00 + p01 + p10 + p11;
                        macroblockSum[my * macroblockCols + mx] = sum;

                        if (sum > rawMax) rawMax = sum;
                    }
                }

                // Step 2: 构建宏块积分图（支持 O(1) 任意区域均值查询）
                int iStride = macroblockCols + 1;
                Array.Clear(integral, 0, integral.Length);

                for (int my = 0; my < macroblockRows; my++)
                {
                    long rowSum = 0;
                    int iRow = (my + 1) * iStride;
                    int iPrevRow = my * iStride;
                    int mRow = my * macroblockCols;

                    for (int mx = 0; mx < macroblockCols; mx++)
                    {
                        rowSum += macroblockSum[mRow + mx];
                        integral[iRow + mx + 1] = integral[iPrevRow + mx + 1] + rowSum;
                    }
                }

                // 提前释放 macroblockSum：积分图构建完成后不再需要
                ArrayPool<int>.Shared.Return(macroblockSum);
                macroblockSum = null; // 标记已释放，防止 finally 重复归还

                // ===== 层级2：在宏块图上进行 N×N 均值滤波 =====
                // 计算滤波后的块图尺寸
                int blockCols = macroblockCols / macroblockFilterSize;
                int blockRows = macroblockRows / macroblockFilterSize;
                int blockCount = blockRows * blockCols;

                int[] blockSum = ArrayPool<int>.Shared.Rent(blockCount);

                try
                {
                    // Step 3: 使用积分图快速计算 N×N 宏块区域的均值
                    int maxBlockSum = 0;
                    int maxBlockX = 0, maxBlockY = 0;

                    for (int by = 0; by < blockRows; by++)
                    {
                        for (int bx = 0; bx < blockCols; bx++)
                        {
                            // 宏块坐标范围
                            int mStartX = bx * macroblockFilterSize;
                            int mStartY = by * macroblockFilterSize;
                            int mEndX = mStartX + macroblockFilterSize;
                            int mEndY = mStartY + macroblockFilterSize;

                            // 使用积分图 O(1) 计算区域和
                            int sum = (int)(integral[mEndY * iStride + mEndX]
                                          - integral[mStartY * iStride + mEndX]
                                          - integral[mEndY * iStride + mStartX]
                                          + integral[mStartY * iStride + mStartX]);

                            blockSum[by * blockCols + bx] = sum;

                            if (sum > maxBlockSum)
                            {
                                maxBlockSum = sum;
                                maxBlockX = bx;
                                maxBlockY = by;
                            }
                        }
                    }

                    // Step 4: 阈值分割 - 框定中心高原区
                    int threshold = (int)(maxBlockSum * plateauThresholdRatio);

                    // Step 5: Two-Pass 连通域分析（替代 BFS，无动态内存分配）
                    int[] labels = ArrayPool<int>.Shared.Rent(blockCount);
                    int[] parent = ArrayPool<int>.Shared.Rent(blockCount); // 并查集

                    try
                    {
                        // 初始化并查集
                        for (int i = 0; i < blockCount; i++)
                        {
                            parent[i] = i;
                        }

                        // Pass 1: 标记临时标签，建立等价关系
                        int nextLabel = 1;
                        for (int by = 0; by < blockRows; by++)
                        {
                            for (int bx = 0; bx < blockCols; bx++)
                            {
                                int idx = by * blockCols + bx;
                                if (blockSum[idx] < threshold) continue;

                                // 检查 4 个邻居（8-连通）：左、左上、上、右上
                                int leftLabel = (bx > 0 && blockSum[idx - 1] >= threshold) ? labels[idx - 1] : 0;
                                int topLeftLabel = (bx > 0 && by > 0 && blockSum[idx - blockCols - 1] >= threshold) ? labels[idx - blockCols - 1] : 0;
                                int topLabel = (by > 0 && blockSum[idx - blockCols] >= threshold) ? labels[idx - blockCols] : 0;
                                int topRightLabel = (bx < blockCols - 1 && by > 0 && blockSum[idx - blockCols + 1] >= threshold) ? labels[idx - blockCols + 1] : 0;

                                // 找到最小的非零标签
                                int minLabel = 0;
                                if (leftLabel > 0) minLabel = leftLabel;
                                if (topLeftLabel > 0 && (minLabel == 0 || topLeftLabel < minLabel)) minLabel = topLeftLabel;
                                if (topLabel > 0 && (minLabel == 0 || topLabel < minLabel)) minLabel = topLabel;
                                if (topRightLabel > 0 && (minLabel == 0 || topRightLabel < minLabel)) minLabel = topRightLabel;

                                if (minLabel == 0)
                                {
                                    // 新标签
                                    labels[idx] = nextLabel++;
                                }
                                else
                                {
                                    labels[idx] = minLabel;
                                    // 合并所有等价标签
                                    if (leftLabel > 0 && leftLabel != minLabel) Union(parent, minLabel, leftLabel);
                                    if (topLeftLabel > 0 && topLeftLabel != minLabel) Union(parent, minLabel, topLeftLabel);
                                    if (topLabel > 0 && topLabel != minLabel) Union(parent, minLabel, topLabel);
                                    if (topRightLabel > 0 && topRightLabel != minLabel) Union(parent, minLabel, topRightLabel);
                                }
                            }
                        }

                        // Pass 2: 解析并查集，统计每个连通域的面积和质心
                        int componentCount = nextLabel - 1;
                        int[] componentArea = new int[componentCount + 1];
                        long[] componentSumX = new long[componentCount + 1];
                        long[] componentSumY = new long[componentCount + 1];

                        for (int by = 0; by < blockRows; by++)
                        {
                            for (int bx = 0; bx < blockCols; bx++)
                            {
                                int idx = by * blockCols + bx;
                                if (labels[idx] == 0) continue;

                                int root = Find(parent, labels[idx]);
                                componentArea[root]++;
                                componentSumX[root] += bx;
                                componentSumY[root] += by;
                            }
                        }

                        // 找到面积最大的连通域
                        int largestComponentLabel = 0;
                        int largestArea = 0;
                        for (int i = 1; i <= componentCount; i++)
                        {
                            if (componentArea[i] > largestArea)
                            {
                                largestArea = componentArea[i];
                                largestComponentLabel = i;
                            }
                        }

                        // 计算最大连通域的几何重心（粗定位）
                        double centroidBlockX, centroidBlockY;
                        if (largestComponentLabel > 0 && largestArea > 0)
                        {
                            centroidBlockX = (double)componentSumX[largestComponentLabel] / largestArea;
                            centroidBlockY = (double)componentSumY[largestComponentLabel] / largestArea;
                        }
                        else
                        {
                            // 降级：使用单个最亮块
                            centroidBlockX = maxBlockX;
                            centroidBlockY = maxBlockY;
                        }

                        // Step 6.5: 灰度重心法亚像素细化
                        // 以几何重心为中心，取 3x3 滤波块范围的窗口，用原始宏块亮度加权细化
                        {
                            int refineRadius = Math.Max(1, macroblockFilterSize); // 细化窗口半径（滤波块单位）
                            int refineCenterMX = (int)(centroidBlockX * macroblockFilterSize); // 转换为宏块坐标
                            int refineCenterMY = (int)(centroidBlockY * macroblockFilterSize);

                            long weightedSumX = 0, weightedSumY = 0, totalWeight = 0;
                            int refineMinMX = Math.Max(0, refineCenterMX - refineRadius);
                            int refineMaxMX = Math.Min(macroblockCols - 1, refineCenterMX + refineRadius);
                            int refineMinMY = Math.Max(0, refineCenterMY - refineRadius);
                            int refineMaxMY = Math.Min(macroblockRows - 1, refineCenterMY + refineRadius);

                            // 计算背景基线（窗口边缘的平均亮度）
                            long edgeSum = 0;
                            int edgeCount = 0;
                            for (int my = refineMinMY; my <= refineMaxMY; my++)
                            {
                                for (int mx = refineMinMX; mx <= refineMaxMX; mx++)
                                {
                                    if (mx == refineMinMX || mx == refineMaxMX || my == refineMinMY || my == refineMaxMY)
                                    {
                                        edgeSum += integral[(my + 1) * iStride + (mx + 1)]
                                                 - integral[my * iStride + (mx + 1)]
                                                 - integral[(my + 1) * iStride + mx]
                                                 + integral[my * iStride + mx];
                                        edgeCount++;
                                    }
                                }
                            }
                            long backgroundBaseline = edgeCount > 0 ? edgeSum / edgeCount : 0;

                            // 灰度重心法：权重 = 亮度 - 背景基线
                            for (int my = refineMinMY; my <= refineMaxMY; my++)
                            {
                                for (int mx = refineMinMX; mx <= refineMaxMX; mx++)
                                {
                                    int macroVal = (int)(integral[(my + 1) * iStride + (mx + 1)]
                                                       - integral[my * iStride + (mx + 1)]
                                                       - integral[(my + 1) * iStride + mx]
                                                       + integral[my * iStride + mx]);
                                    long weight = macroVal - backgroundBaseline;
                                    if (weight > 0)
                                    {
                                        weightedSumX += weight * mx;
                                        weightedSumY += weight * my;
                                        totalWeight += weight;
                                    }
                                }
                            }

                            if (totalWeight > 0)
                            {
                                // 灰度重心（宏块坐标）转换为滤波块坐标
                                double refinedMX = (double)weightedSumX / totalWeight;
                                double refinedMY = (double)weightedSumY / totalWeight;
                                // 混合：70% 灰度重心 + 30% 几何重心（保持稳定性）
                                double refinedBlockX = refinedMX / macroblockFilterSize;
                                double refinedBlockY = refinedMY / macroblockFilterSize;
                                centroidBlockX = refinedBlockX * 0.7 + centroidBlockX * 0.3;
                                centroidBlockY = refinedBlockY * 0.7 + centroidBlockY * 0.3;
                            }
                        }

                        // 转换为像素坐标（宏块中心）
                        double centroidPixelX = (centroidBlockX + 0.5) * macroblockFilterSize * 2;
                        double centroidPixelY = (centroidBlockY + 0.5) * macroblockFilterSize * 2;

                        // 图像几何中心（块坐标）
                        double centerBlockX = (blockCols - 1) / 2.0;
                        double centerBlockY = (blockRows - 1) / 2.0;

                        // Step 6: 计算偏心度（用于警告，不修正）
                        double distToCenter = Math.Sqrt(
                            Math.Pow(centroidBlockX - centerBlockX, 2) +
                            Math.Pow(centroidBlockY - centerBlockY, 2)
                        );

                        double finalScore = maxBlockSum * centerWeightAlpha - distToCenter * distanceWeightBeta * maxBlockSum;

                        // 计算偏心度百分比（相对于图像对角线）
                        double imageDiagonal = Math.Sqrt(Math.Pow(blockCols, 2) + Math.Pow(blockRows, 2));
                        double eccentricityPercent = (distToCenter / imageDiagonal) * 100;

                        // 当偏心度超过阈值时，显示警告（不修正质心）
                        double maxAllowedEccentricity = 15.0; // 15% 阈值
                        if (eccentricityPercent > maxAllowedEccentricity)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                TxtStatus.Text = $"⚠️ 警告：检测到光学中心严重偏离，偏心度 {eccentricityPercent:F1}%，请检查标定光源或镜头装配！";
                                TxtStatus.Foreground = System.Windows.Media.Brushes.Red;
                            });
                        }
                        else
                        {
                            Dispatcher.Invoke(() =>
                            {
                                TxtStatus.Foreground = System.Windows.Media.Brushes.Black;
                            });
                        }

                        // Step 7: 生成 Top 10 列表
                        _brightestPositions.Clear();

                        // 第 1 项：连通域几何重心（光学中心）
                        _brightestPositions.Add(new BrightestItem
                        {
                            Rank = 1,
                            Position = $"RAW({centroidPixelX:F1}, {centroidPixelY:F1})",
                            Info = $"Sum={maxBlockSum}, 距中心={distToCenter:F1}, Score={finalScore:F0}",
                            RawX = (int)Math.Round(centroidPixelX),
                            RawY = (int)Math.Round(centroidPixelY),
                            Brightness = maxBlockSum,
                            Distance = distToCenter,
                            IsSelected = false
                        });

                        // 第 2-10 项：全图搜索（按块均值排序，NMS 去重）
                        // 计算每个块的均值（亮度密度），而非总和
                        var allBlocks = new List<(int x, int y, double mean)>();
                        int macroblockArea = macroblockFilterSize * macroblockFilterSize;
                        for (int by = 0; by < blockRows; by++)
                        {
                            for (int bx = 0; bx < blockCols; bx++)
                            {
                                int idx = by * blockCols + bx;
                                double mean = (double)blockSum[idx] / macroblockArea;
                                allBlocks.Add((bx, by, mean));
                            }
                        }

                        // 按均值降序排序
                        allBlocks.Sort((a, b) => b.mean.CompareTo(a.mean));

                        // NMS（非极大值抑制）：抑制距离过近的块
                        double nmsDistance = macroblockFilterSize * 0.5; // 抑制半径（滤波块单位）
                        var selectedBlocks = new List<(int x, int y, double mean)>();
                        
                        foreach (var block in allBlocks)
                        {
                            // 跳过已选中的 Top 1 中心点附近
                            if (Math.Abs(block.x - centroidBlockX) < 0.5 && Math.Abs(block.y - centroidBlockY) < 0.5)
                                continue;

                            // 检查是否与已选块距离过近
                            bool suppressed = false;
                            foreach (var selected in selectedBlocks)
                            {
                                double dist = Math.Sqrt(Math.Pow(block.x - selected.x, 2) + Math.Pow(block.y - selected.y, 2));
                                if (dist < nmsDistance)
                                {
                                    suppressed = true;
                                    break;
                                }
                            }

                            if (!suppressed)
                            {
                                selectedBlocks.Add(block);
                                if (selectedBlocks.Count >= 9) // 只需要 9 个（加上 Top 1 共 10 个）
                                    break;
                            }
                        }

                        // 生成 Top 2-10 列表
                        for (int i = 0; i < selectedBlocks.Count; i++)
                        {
                            var block = selectedBlocks[i];
                            double pixelX = (block.x + 0.5) * macroblockFilterSize * 2;
                            double pixelY = (block.y + 0.5) * macroblockFilterSize * 2;
                            double dist = Math.Sqrt(Math.Pow(block.x - centerBlockX, 2) + Math.Pow(block.y - centerBlockY, 2));

                            _brightestPositions.Add(new BrightestItem
                            {
                                Rank = i + 2,
                                Position = $"RAW({pixelX:F1}, {pixelY:F1})",
                                Info = $"Mean={block.mean:F1}, 距中心={dist:F1}",
                                RawX = (int)Math.Round(pixelX),
                                RawY = (int)Math.Round(pixelY),
                                Brightness = (int)(block.mean * macroblockArea),
                                Distance = dist,
                                IsSelected = false
                            });
                        }


                        Dispatcher.Invoke(() =>
                        {
                            LstBrightestPositions.Items.Refresh();

                            // 更新 UI 信息
                            TxtYRange.Text = $"RAW 亮度范围: 0 - {rawMax} (10-bit)";
                            // 在图像上标注
                            RedrawMarkers();
                        });

                    }
                    finally
                    {
                        ArrayPool<int>.Shared.Return(labels);
                        ArrayPool<int>.Shared.Return(parent);
                    }
                }
                finally
                {
                    ArrayPool<int>.Shared.Return(blockSum);
                }
            }
            finally
            {
                // macroblockSum 可能已被提前释放（积分图构建完成后）
                if (macroblockSum != null)
                {
                    ArrayPool<int>.Shared.Return(macroblockSum);
                }
                ArrayPool<long>.Shared.Return(integral);
            }
        }

        /// <summary>
        /// 并查集：查找根节点（带路径压缩）
        /// </summary>
        private int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]]; // 路径压缩
                x = parent[x];
            }
            return x;
        }

        /// <summary>
        /// 并查集：合并两个集合
        /// </summary>
        private void Union(int[] parent, int x, int y)
        {
            int rootX = Find(parent, x);
            int rootY = Find(parent, y);
            if (rootX != rootY)
            {
                parent[rootX] = rootY;
            }
        }

        /// <summary>
        /// 检测最亮区域并标注 Top 10（旧版本，保留用于对比）
        /// </summary>
        private void DetectAndMarkBrightestLegacy(byte[] pixels, int width, int height, int bayerMode)
        {
            // 修复问题2：边界遗漏 - 使用向上取整处理奇数宽高
            int halfW = (width + 1) / 2;
            int halfH = (height + 1) / 2;
            // 修正：中心块坐标应为 (halfW - 1) / 2，而非 halfW / 2
            int centerBlockX = (halfW - 1) / 2;
            int centerBlockY = (halfH - 1) / 2;

            // 安全边距（块单位），避免搜索到图像边缘
            int marginBlock = 4; // 8 像素边距

            // ===== 读取用户配置参数 =====
            bool isGlobalSearch = CmbSearchMode?.SelectedIndex == 1;
            int userNmsRadius = 4;
            double userThresholdRatio = 0.95;
            
            if (int.TryParse(TxtFilterRadius?.Text, out int nmsVal) && nmsVal > 0)
                userNmsRadius = nmsVal;
            if (double.TryParse(TxtThresholdRatio?.Text, out double thVal) && thVal > 0 && thVal <= 1.0)
                userThresholdRatio = thVal;

            // ===== 内存优化：使用 ArrayPool 复用数组，减少 GC 压力 =====
            int yBlockSize = halfW * halfH;
            int integralSize = (halfH + 1) * (halfW + 1);

            byte[] yBlockMapOrig = ArrayPool<byte>.Shared.Rent(yBlockSize);
            byte[] yBlockMap = yBlockMapOrig;
            byte[] yFiltered = ArrayPool<byte>.Shared.Rent(yBlockSize);
            // 修复Bug4：积分图使用int而非long，1080p下最大值132M远小于int.MaxValue(2.1B)
            int[] integral = ArrayPool<int>.Shared.Rent(integralSize);

            try
            {
                // ===== Step 1: 计算 Y 亮度图 (2x2 block 平均) =====
                int yMin = 255, yMax = 0;

                for (int by = 0; by < height; by += 2)
                {
                    int yRow = (by / 2) * halfW;
                    for (int bx = 0; bx < width; bx += 2)
                    {
                        // 修复问题6：边界使用镜像填充而非重复边缘像素
                        int idx00 = by * width + bx;
                        // 镜像：超出边界时反射到内侧像素
                        int bx1 = (bx + 1 < width) ? bx + 1 : bx - 1;
                        int by1 = (by + 1 < height) ? by + 1 : by - 1;
                        int idx01 = by * width + bx1;
                        int idx10 = by1 * width + bx;
                        int idx11 = by1 * width + bx1;

                        int p00 = pixels[idx00];
                        int p01 = pixels[idx01];
                        int p10 = pixels[idx10];
                        int p11 = pixels[idx11];

                        int r, g, b;
                        switch (bayerMode)
                        {
                            case 0: // RGRG: p00=R, p01=G, p10=G, p11=B
                                r = p00;
                                g = (p01 + p10) >> 1;
                                b = p11;
                                break;
                            case 1: // GRGR: p00=G, p01=R, p10=B, p11=G
                                r = p01;
                                g = (p00 + p11) >> 1;
                                b = p10;
                                break;
                            case 2: // BGBG: p00=B, p01=G, p10=G, p11=R
                                r = p11;
                                g = (p01 + p10) >> 1;
                                b = p00;
                                break;
                            case 3: // GBGB: p00=G, p01=B, p10=R, p11=G
                                r = p10;
                                g = (p00 + p11) >> 1;
                                b = p01;
                                break;
                            default:
                                r = p00;
                                g = (p01 + p10) >> 1;
                                b = p11;
                                break;
                        }

                        int yVal = (r * 77 + g * 150 + b * 29) >> 8;
                        yBlockMap[yRow + bx / 2] = (byte)yVal;
                        if (yVal < yMin) yMin = yVal;
                        if (yVal > yMax) yMax = yVal;
                    }
                }

                TxtYRange.Text = $"Y 亮度范围: {yMin} - {yMax}";

                // ===== Step 2: 严格 3x3 中值滤波 (消除热像素/坏点干扰) =====
                // 修复Bug1：边界处只对有效数据排序，避免脏数据污染
                int[] sortArr = new int[9];
                for (int y = 0; y < halfH; y++)
                {
                    for (int x = 0; x < halfW; x++)
                    {
                        int cnt = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int ny = y + dy;
                            if (ny < 0 || ny >= halfH) continue;
                            int rowBase = ny * halfW;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx;
                                if (nx < 0 || nx >= halfW) continue;
                                sortArr[cnt++] = yBlockMap[rowBase + nx];
                            }
                        }
                        // 只对有效数据排序，避免脏数据污染
                        SortNetworkPartial(sortArr, cnt);
                        // 取中值
                        if (cnt == 9)
                            yFiltered[y * halfW + x] = (byte)sortArr[4];
                        else if ((cnt & 1) == 1)
                            yFiltered[y * halfW + x] = (byte)sortArr[cnt / 2];
                        else
                            yFiltered[y * halfW + x] = (byte)((sortArr[cnt / 2 - 1] + sortArr[cnt / 2]) >> 1);
                    }
                }
                yBlockMap = yFiltered;

                // ===== Step 3: Otsu 自适应阈值 + 饱和像素统计 =====
                // 修复问题2：使用 Otsu 方法计算最优阈值，取代简单的 yMax * ratio
                int[] histogram = new int[256];
                int searchAreaSize = (halfW - marginBlock * 2) * (halfH - marginBlock * 2);
                int saturatedCount = 0;
                int satThreshold = 250;
                
                // 构建亮度直方图
                for (int y = marginBlock; y < halfH - marginBlock; y++)
                {
                    int rowBase = y * halfW;
                    for (int x = marginBlock; x < halfW - marginBlock; x++)
                    {
                        int val = yBlockMap[rowBase + x];
                        histogram[val]++;
                        if (val >= satThreshold) saturatedCount++;
                    }
                }
                
                // Otsu 方法：寻找使类间方差最大的阈值
                int totalPixels = searchAreaSize;
                double sumAll = 0;
                for (int i = 0; i < 256; i++) sumAll += i * histogram[i];
                
                double sumB = 0;
                int wB = 0;
                double maxVariance = 0;
                int otsuThreshold = yMax / 2; // 默认值
                
                for (int t = 0; t < 256; t++)
                {
                    wB += histogram[t];
                    if (wB == 0) continue;
                    
                    int wF = totalPixels - wB;
                    if (wF == 0) break;
                    
                    sumB += t * histogram[t];
                    double mB = sumB / wB;
                    double mF = (sumAll - sumB) / wF;
                    
                    double variance = (double)wB * wF * (mB - mF) * (mB - mF);
                    if (variance > maxVariance)
                    {
                        maxVariance = variance;
                        otsuThreshold = t;
                    }
                }
                
                // 修复问题2：结合用户阈值系数和 Otsu 结果
                // 如果饱和像素占比 > 5%，降低阈值
                double satRatio = (searchAreaSize > 0) ? (double)saturatedCount / searchAreaSize : 0;
                double adjustedRatio = (satRatio > 0.05) ? userThresholdRatio * 0.95 : userThresholdRatio;
                int adaptiveThreshold = Math.Max(otsuThreshold, (int)(yMax * adjustedRatio));

                // ===== Step 4: 构建积分图 =====
                // 积分图第0行和第0列必须初始化为0
                int iStride = halfW + 1;
                // 第0行清零
                for (int x = 0; x < iStride; x++)
                {
                    integral[x] = 0;
                }
                for (int y = 0; y < halfH; y++)
                {
                    int rowSum = 0;
                    int iRow = (y + 1) * iStride;
                    int iPrevRow = y * iStride;
                    int yRow = y * halfW;
                    // 每行第0列清零
                    integral[iRow] = 0;
                    for (int x = 0; x < halfW; x++)
                    {
                        rowSum += yBlockMap[yRow + x];
                        integral[iRow + x + 1] = integral[iPrevRow + x + 1] + rowSum;
                    }
                }

                // ===== Step 5: 搜索区域 =====
                // 搜索范围：从安全边距到图像边界
                int cStartX = marginBlock;
                int cEndX = halfW - marginBlock;
                int cStartY = marginBlock;
                int cEndY = halfH - marginBlock;

                // ===== Step 6: 多尺度渐进搜索 =====
                // 粗搜：16x16 block, 中搜：8x8 block, 精搜：4x4 block
                int[] scaleBlocks = { 16, 8, 4 };
                int[] scaleSteps = { 4, 2, 1 };

                int searchCX = centerBlockX;
                int searchCY = centerBlockY;
                int searchRadius = Math.Max((cEndX - cStartX) / 2, (cEndY - cStartY) / 2);

                for (int scale = 0; scale < 3; scale++)
                {
                    int _blockSize = scaleBlocks[scale];
                    int step = scaleSteps[scale];

                    // 计算搜索范围
                    int sStartX = Math.Max(cStartX, searchCX - searchRadius);
                    int sEndX = Math.Min(cEndX - _blockSize, searchCX + searchRadius);
                    int sStartY = Math.Max(cStartY, searchCY - searchRadius);
                    int sEndY = Math.Min(cEndY - _blockSize, searchCY + searchRadius);

                    // 找最大亮度块，更新搜索中心
                    int maxSum = int.MinValue;
                    int bestX = searchCX, bestY = searchCY;

                    for (int cy = sStartY; cy <= sEndY; cy += step)
                    {
                        int cy0 = cy * iStride;
                        int cy1 = (cy + _blockSize) * iStride;
                        for (int cx = sStartX; cx <= sEndX; cx += step)
                        {
                            int sum = integral[cy1 + cx + _blockSize]
                                    - integral[cy0 + cx + _blockSize]
                                    - integral[cy1 + cx]
                                    + integral[cy0 + cx];
                            if (sum > maxSum)
                            {
                                maxSum = sum;
                                bestX = cx + _blockSize / 2;
                                bestY = cy + _blockSize / 2;
                            }
                        }
                    }
                    searchCX = bestX;
                    searchCY = bestY;
                    searchRadius = _blockSize * 2;
                }

                // ===== Step 7: 收集 Top 10 最亮位置（使用积分图快速搜索） =====
                // 修复问题1：支持全局搜索模式
                // 修复问题2：移除 fallback 逻辑，统一使用质心法
                // 修复问题3：质心计算转移到 Block 域，避免重复 RGB → Y 转换
                // 修复问题4：使用 double 除法保留亚像素精度
                int nmsRadius = userNmsRadius;
                
                // 根据搜索模式确定搜索范围
                int regionStartX, regionEndX, regionStartY, regionEndY;
                
                if (isGlobalSearch)
                {
                    // 全局搜索：覆盖整个有效区域
                    regionStartX = cStartX;
                    regionEndX = cEndX;
                    regionStartY = cStartY;
                    regionEndY = cEndY;
                }
                else
                {
                    // 局部搜索：以 Step 6 的结果为中心
                    int searchRange = 32; // 块单位
                    regionStartX = Math.Max(cStartX, searchCX - searchRange);
                    regionEndX = Math.Min(cEndX, searchCX + searchRange);
                    regionStartY = Math.Max(cStartY, searchCY - searchRange);
                    regionEndY = Math.Min(cEndY, searchCY + searchRange);
                }

                // ===== Phase 1: 收集所有超过阈值的候选块 =====
                // 修复NMS执行顺序问题：先收集所有候选，排序后再应用NMS
                // 这样可以确保最亮的块优先被选中，避免因遍历顺序导致漏检
                var allCandidates = new List<(int bx, int by, int avgBrightness, long distSq)>();
                int blockSize = 4;
                
                for (int by = regionStartY; by < regionEndY - blockSize; by += blockSize)
                {
                    for (int bx = regionStartX; bx < regionEndX - blockSize; bx += blockSize)
                    {
                        // 使用积分图快速计算块的和
                        int sum = integral[(by + blockSize) * iStride + bx + blockSize]
                                - integral[by * iStride + bx + blockSize]
                                - integral[(by + blockSize) * iStride + bx]
                                + integral[by * iStride + bx];
                        
                        int avgBrightness = sum / (blockSize * blockSize);
                        
                        // 使用自适应阈值过滤低亮度候选
                        if (avgBrightness < adaptiveThreshold) continue;
                        
                        int centerX = bx + blockSize / 2;
                        int centerY = by + blockSize / 2;
                        long distSq = (centerX - centerBlockX) * (long)(centerX - centerBlockX) +
                                     (centerY - centerBlockY) * (long)(centerY - centerBlockY);
                        
                        allCandidates.Add((bx, by, avgBrightness, distSq));
                    }
                }
                
                // 按亮度降序排序，亮度相同则按距离升序
                allCandidates.Sort((a, b) => 
                {
                    int cmp = b.avgBrightness.CompareTo(a.avgBrightness);
                    return cmp != 0 ? cmp : a.distSq.CompareTo(b.distSq);
                });

                // ===== Phase 2: 应用 NMS 并生成 Top 10（使用 Block 域质心） =====
                // 修复问题4：使用 double 类型保留亚像素精度
                var top10 = new List<(double pixelX, double pixelY, int brightness, long distSq, int blockX, int blockY)>(10);
                
                foreach (var candidate in allCandidates)
                {
                    if (top10.Count >= 10) break; // 已收集足够候选
                    
                    int centerX = candidate.bx + blockSize / 2;
                    int centerY = candidate.by + blockSize / 2;
                    
                    // NMS 检查：是否与已选中的块太近
                    bool suppressed = false;
                    foreach (var t in top10)
                    {
                        int dx = Math.Abs(centerX - t.blockX);
                        int dy = Math.Abs(centerY - t.blockY);
                        if (dx <= nmsRadius && dy <= nmsRadius)
                        {
                            suppressed = true;
                            break;
                        }
                    }
                    if (suppressed) continue;

                    // 修复问题3：在 Block 域计算质心，避免重复 RGB → Y 转换
                    // 使用 yBlockMap（已滤波）而非 _rgbBuffer，性能提升 75%
                    double sumX = 0, sumY = 0, sumWeight = 0;
                    int blockEndX = Math.Min(candidate.bx + blockSize, halfW);
                    int blockEndY = Math.Min(candidate.by + blockSize, halfH);
                    
                    for (int by = candidate.by; by < blockEndY; by++)
                    {
                        int rowBase = by * halfW;
                        for (int bx = candidate.bx; bx < blockEndX; bx++)
                        {
                            int yVal = yBlockMap[rowBase + bx];
                            // 块坐标转像素坐标（每个块对应 2x2 像素，取块中心）
                            double pixelX = bx * 2 + 0.5;
                            double pixelY = by * 2 + 0.5;
                            
                            sumX += pixelX * yVal;
                            sumY += pixelY * yVal;
                            sumWeight += yVal;
                        }
                    }
                    
                    // 修复问题4：使用 double 除法，保留亚像素精度
                    double finalPixelX = sumWeight > 0 ? sumX / sumWeight : (candidate.bx * 2 + blockSize);
                    double finalPixelY = sumWeight > 0 ? sumY / sumWeight : (candidate.by * 2 + blockSize);
                    int finalBrightness = candidate.avgBrightness;

                    // 添加到 top10 列表（已按亮度排序，直接追加即可）
                    top10.Add((finalPixelX, finalPixelY, finalBrightness, candidate.distSq, centerX, centerY));
                }

                // 更新 UI 列表
                _brightestPositions.Clear();
                for (int rank = 0; rank < top10.Count && rank < 10; rank++)
                {
                    var item = top10[rank];
                    double dist = Math.Round(Math.Sqrt(item.distSq), 1);

                    _brightestPositions.Add(new BrightestItem
                    {
                        Rank = rank + 1,
                        Position = $"RAW({item.pixelX:F1}, {item.pixelY:F1})", // 显示 1 位小数
                        Info = $"Y={item.brightness}, 距中心={dist}",
                        RawX = (int)Math.Round(item.pixelX), // 用于标注时取整
                        RawY = (int)Math.Round(item.pixelY),
                        Brightness = item.brightness,
                        Distance = dist,
                        IsSelected = false
                    });
                }

                LstBrightestPositions.Items.Refresh();

                // 在图像上标注
                RedrawMarkers();
            }
            finally
            {
                // 归还数组到池，减少 GC 压力
                // 注意：yBlockMap 在 Step 2 后指向 yFiltered，必须归还原始数组 yBlockMapOrig
                ArrayPool<byte>.Shared.Return(yBlockMapOrig);
                ArrayPool<byte>.Shared.Return(yFiltered);
                ArrayPool<int>.Shared.Return(integral);
            }
        }

        /// <summary>
        /// 9 元素排序网络（Bose-Nelson 最优序列）
        /// </summary>
        private void SortNetwork(int[] a)
        {
            SortPair(a, 0, 1); SortPair(a, 3, 4); SortPair(a, 6, 7);
            SortPair(a, 1, 2); SortPair(a, 4, 5); SortPair(a, 7, 8);
            SortPair(a, 0, 1); SortPair(a, 3, 4); SortPair(a, 6, 7);
            SortPair(a, 0, 3); SortPair(a, 3, 6); SortPair(a, 0, 3);
            SortPair(a, 1, 4); SortPair(a, 4, 7); SortPair(a, 1, 4);
            SortPair(a, 2, 5); SortPair(a, 5, 8); SortPair(a, 2, 5);
            SortPair(a, 1, 3); SortPair(a, 5, 7);
            SortPair(a, 2, 6); SortPair(a, 4, 8); SortPair(a, 2, 4);
            SortPair(a, 2, 3); SortPair(a, 5, 6);
        }

        /// <summary>
        /// 部分排序网络：只对前 n 个元素排序，避免边界脏数据污染
        /// 使用插入排序（边界处 n 通常 < 9，插入排序更高效）
        /// </summary>
        private void SortNetworkPartial(int[] a, int n)
        {
            for (int i = 1; i < n; i++)
            {
                int key = a[i];
                int j = i - 1;
                while (j >= 0 && a[j] > key)
                {
                    a[j + 1] = a[j];
                    j--;
                }
                a[j + 1] = key;
            }
        }

        /// <summary>
        /// 排序网络 compare-swap 原语
        /// </summary>
        private void SortPair(int[] a, int i, int j)
        {
            if (a[i] > a[j])
            {
                int tmp = a[i];
                a[i] = a[j];
                a[j] = tmp;
            }
        }

        /// <summary>
        /// 获取单个像素的亮度值（用于逐像素精细扫描）
        /// 根据 Bayer 模式还原 R/G/B 分量，计算加权亮度 Y = (77*R + 150*G + 29*B) >> 8
        /// </summary>
        private int GetPixelBrightness(byte[] pixels, int width, int height, int x, int y, int bayerMode)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return 0;

            int idx = y * width + x;
            int val = pixels[idx];

            // 判断当前像素在 Bayer 模式中的通道类型
            // 返回: 0=R, 1=G, 2=B
            int channelType = GetBayerChannelType(x, y, bayerMode);

            int r, g, b;

            if (channelType == 0) // R 像素
            {
                r = val;
                // G 通道：取上下左右四个邻居的平均
                g = GetNeighborAverage(pixels, width, height, x, y, bayerMode, 1);
                // B 通道：取对角四个邻居的平均
                b = GetDiagonalAverage(pixels, width, height, x, y, bayerMode, 2);
            }
            else if (channelType == 2) // B 像素
            {
                b = val;
                // G 通道：取上下左右四个邻居的平均
                g = GetNeighborAverage(pixels, width, height, x, y, bayerMode, 1);
                // R 通道：取对角四个邻居的平均
                r = GetDiagonalAverage(pixels, width, height, x, y, bayerMode, 0);
            }
            else // G 像素
            {
                g = val;
                // 判断 G 像素的水平方向是 R 还是 B
                bool horizontalIsR = IsGHorizontalR(x, y, bayerMode);
                if (horizontalIsR)
                {
                    // 水平方向是 R，垂直方向是 B
                    r = GetHorizontalAverage(pixels, width, height, x, y, bayerMode, 0);
                    b = GetVerticalAverage(pixels, width, height, x, y, bayerMode, 2);
                }
                else
                {
                    // 水平方向是 B，垂直方向是 R
                    b = GetHorizontalAverage(pixels, width, height, x, y, bayerMode, 2);
                    r = GetVerticalAverage(pixels, width, height, x, y, bayerMode, 0);
                }
            }

            // 计算加权亮度 Y
            return (r * 77 + g * 150 + b * 29) >> 8;
        }

        /// <summary>
        /// 判断像素在 Bayer 模式中的通道类型
        /// </summary>
        private int GetBayerChannelType(int x, int y, int bayerMode)
        {
            int xOdd = x & 1;
            switch (bayerMode)
            {
                case 0: // RGRG: 每行都是 R G R G...
                    return xOdd == 0 ? 0 : 1;
                case 1: // GRGR: 每行都是 G R G R...
                    return xOdd == 0 ? 1 : 0;
                case 2: // BGBG: 每行都是 B G B G...
                    return xOdd == 0 ? 2 : 1;
                case 3: // GBGB: 每行都是 G B G B...
                    return xOdd == 0 ? 1 : 2;
                default:
                    return 1;
            }
        }

        /// <summary>
        /// 判断 G 像素的水平方向是否是 R
        /// </summary>
        private bool IsGHorizontalR(int x, int y, int bayerMode)
        {
            int xOdd = x & 1;
            switch (bayerMode)
            {
                case 0: // RGRG: R G R G... → G 在奇数位，左右都是 R
                    return xOdd == 1;
                case 1: // GRGR: G R G R... → G 在偶数位，左右都是 R
                    return xOdd == 0;
                case 2: // BGBG: B G B G... → G 在奇数位，左右都是 B
                    return false;
                case 3: // GBGB: G B G B... → G 在偶数位，左右都是 B
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 获取上下左右四个邻居中指定通道的平均值
        /// </summary>
        private int GetNeighborAverage(byte[] pixels, int width, int height, int x, int y, int bayerMode, int targetType)
        {
            int sum = 0, count = 0;
            if (x > 0 && GetBayerChannelType(x - 1, y, bayerMode) == targetType) { sum += pixels[y * width + x - 1]; count++; }
            if (x < width - 1 && GetBayerChannelType(x + 1, y, bayerMode) == targetType) { sum += pixels[y * width + x + 1]; count++; }
            if (y > 0 && GetBayerChannelType(x, y - 1, bayerMode) == targetType) { sum += pixels[(y - 1) * width + x]; count++; }
            if (y < height - 1 && GetBayerChannelType(x, y + 1, bayerMode) == targetType) { sum += pixels[(y + 1) * width + x]; count++; }
            return count > 0 ? sum / count : 128;
        }

        /// <summary>
        /// 获取对角四个邻居中指定通道的平均值
        /// </summary>
        private int GetDiagonalAverage(byte[] pixels, int width, int height, int x, int y, int bayerMode, int targetType)
        {
            int sum = 0, count = 0;
            if (x > 0 && y > 0 && GetBayerChannelType(x - 1, y - 1, bayerMode) == targetType) { sum += pixels[(y - 1) * width + x - 1]; count++; }
            if (x < width - 1 && y > 0 && GetBayerChannelType(x + 1, y - 1, bayerMode) == targetType) { sum += pixels[(y - 1) * width + x + 1]; count++; }
            if (x > 0 && y < height - 1 && GetBayerChannelType(x - 1, y + 1, bayerMode) == targetType) { sum += pixels[(y + 1) * width + x - 1]; count++; }
            if (x < width - 1 && y < height - 1 && GetBayerChannelType(x + 1, y + 1, bayerMode) == targetType) { sum += pixels[(y + 1) * width + x + 1]; count++; }
            return count > 0 ? sum / count : 128;
        }

        /// <summary>
        /// 获取水平方向邻居中指定通道的平均值
        /// </summary>
        private int GetHorizontalAverage(byte[] pixels, int width, int height, int x, int y, int bayerMode, int targetType)
        {
            int sum = 0, count = 0;
            if (x > 0 && GetBayerChannelType(x - 1, y, bayerMode) == targetType) { sum += pixels[y * width + x - 1]; count++; }
            if (x < width - 1 && GetBayerChannelType(x + 1, y, bayerMode) == targetType) { sum += pixels[y * width + x + 1]; count++; }
            return count > 0 ? sum / count : 128;
        }

        /// <summary>
        /// 获取垂直方向邻居中指定通道的平均值
        /// </summary>
        private int GetVerticalAverage(byte[] pixels, int width, int height, int x, int y, int bayerMode, int targetType)
        {
            int sum = 0, count = 0;
            if (y > 0 && GetBayerChannelType(x, y - 1, bayerMode) == targetType) { sum += pixels[(y - 1) * width + x]; count++; }
            if (y < height - 1 && GetBayerChannelType(x, y + 1, bayerMode) == targetType) { sum += pixels[(y + 1) * width + x]; count++; }
            return count > 0 ? sum / count : 128;
        }

        /// <summary>
        /// 获取 2x2 block 的亮度值
        /// </summary>
        private int GetBlockBrightness(byte[] pixels, int width, int height, int x, int y, int bayerMode)
        {
            if (x < 0 || x >= width - 1 || y < 0 || y >= height - 1) return 0;

            int idx00 = y * width + x;
            int idx01 = idx00 + 1;
            int idx10 = idx00 + width;
            int idx11 = idx10 + 1;

            int p00 = pixels[idx00];
            int p01 = pixels[idx01];
            int p10 = pixels[idx10];
            int p11 = pixels[idx11];

            int r, g, b;
            switch (bayerMode)
            {
                case 0: r = p00; g = (p01 + p10) >> 1; b = p11; break;
                case 1: r = p01; g = (p00 + p11) >> 1; b = p10; break;
                case 2: r = p11; g = (p01 + p10) >> 1; b = p00; break;
                case 3: r = p10; g = (p00 + p11) >> 1; b = p01; break;
                default: r = p00; g = (p01 + p10) >> 1; b = p11; break;
            }

            return (r * 77 + g * 150 + b * 29) >> 8;
        }

        private void RedrawMarkers()
        {
            // 清除旧的标记
            var toRemove = new List<UIElement>();
            foreach (UIElement child in ImgCanvas.Children)
            {
                if (child is Ellipse || (child is TextBlock tb && tb.Tag != null && tb.Tag.ToString() == "marker") ||
                    (child is Line ln && ln.Tag != null && ln.Tag.ToString() == "crosshair"))
                {
                    toRemove.Add(child);
                }
            }
            foreach (var el in toRemove)
            {
                ImgCanvas.Children.Remove(el);
            }

            if (_brightestPositions == null || _scaleX <= 0) return;

            // 找到离中心最近的项（Distance 最小）
            int closestRank = -1;
            double minDistance = double.MaxValue;
            foreach (var item in _brightestPositions)
            {
                if (item.Distance < minDistance)
                {
                    minDistance = item.Distance;
                    closestRank = item.Rank;
                }
            }

            foreach (var item in _brightestPositions)
            {
                int rawX = item.RawX;
                int rawY = item.RawY;

                // 转换为 Canvas 坐标
                double canvasX = _offsetX + rawX * _scaleX;
                double canvasY = _offsetY + rawY * _scaleY;

                bool isSelected = item.IsSelected;
                bool isClosest = item.Rank == closestRank;

                // 创建标记圆点
                var ellipse = new Ellipse
                {
                    Width = isSelected ? 16 : 12,
                    Height = isSelected ? 16 : 12,
                    Stroke = isSelected ? Brushes.LimeGreen : (isClosest ? Brushes.Red : Brushes.Yellow),
                    StrokeThickness = isSelected ? 3 : 2,
                    Fill = isSelected
                        ? new SolidColorBrush(Color.FromArgb(100, 0, 255, 0))
                        : (isClosest
                            ? new SolidColorBrush(Color.FromArgb(128, 255, 0, 0))
                            : new SolidColorBrush(Color.FromArgb(128, 255, 255, 0)))
                };

                Canvas.SetLeft(ellipse, canvasX - (isSelected ? 8 : 6));
                Canvas.SetTop(ellipse, canvasY - (isSelected ? 8 : 6));
                ImgCanvas.Children.Add(ellipse);

                // 选中项添加十字准星
                if (isSelected)
                {
                    double crossSize = 20;
                    var lineH = new Line
                    {
                        X1 = canvasX - crossSize,
                        Y1 = canvasY,
                        X2 = canvasX + crossSize,
                        Y2 = canvasY,
                        Stroke = Brushes.LimeGreen,
                        StrokeThickness = 1.5,
                        Tag = "crosshair"
                    };
                    var lineV = new Line
                    {
                        X1 = canvasX,
                        Y1 = canvasY - crossSize,
                        X2 = canvasX,
                        Y2 = canvasY + crossSize,
                        Stroke = Brushes.LimeGreen,
                        StrokeThickness = 1.5,
                        Tag = "crosshair"
                    };
                    ImgCanvas.Children.Add(lineH);
                    ImgCanvas.Children.Add(lineV);
                }

                // 添加序号标签
                var label = new TextBlock
                {
                    Text = isSelected ? $"#{item.Rank} ★" : $"#{item.Rank}",
                    Foreground = isSelected ? Brushes.LimeGreen : (isClosest ? Brushes.Red : Brushes.Yellow),
                    FontWeight = FontWeights.Bold,
                    FontSize = isSelected ? 12 : 10,
                    Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                    Tag = "marker"
                };

                Canvas.SetLeft(label, canvasX + (isSelected ? 12 : 8));
                Canvas.SetTop(label, canvasY - 8);
                ImgCanvas.Children.Add(label);
            }
        }

        /// <summary>
        /// 列表项点击事件：选中对应位置并在图像上高亮显示
        /// </summary>
        private void OnBrightestItemClick(object sender, MouseButtonEventArgs e)
        {
            if (_brightestPositions == null || _rgbBuffer == null) return;

            var border = sender as Border;
            if (border?.DataContext is not BrightestItem clickedItem) return;

            // 更新选中状态
            foreach (var item in _brightestPositions)
            {
                item.IsSelected = (item.Rank == clickedItem.Rank);
            }

            _selectedItemIndex = clickedItem.Rank - 1;

            // 刷新列表显示
            LstBrightestPositions.Items.Refresh();

            // 重绘标记
            RedrawMarkers();

            // 更新鼠标位置显示为选中项信息
            TxtMousePos.Text = $"选中 #{clickedItem.Rank}: RAW({clickedItem.RawX}, {clickedItem.RawY}) | " +
                              $"Y={clickedItem.Brightness}, 距中心={clickedItem.Distance}";
        }

        private void OnBayerModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbBayerMode == null) return;
            _bayerMode = CmbBayerMode.SelectedIndex;

            if (_rawBuffer != null)
            {
                _processingCts = new CancellationTokenSource();
                var token = _processingCts.Token;
                //ProcessImage();
                // 异步执行图像处理，避免 UI 线程阻塞
                _ = ProcessImageAsync(token);
            }
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (_raw16Buffer == null) return;

            var pos = e.GetPosition(ImgCanvas);

            // 转换为图像坐标
            int imgX = (int)((pos.X - _offsetX) / _scaleX);
            int imgY = (int)((pos.Y - _offsetY) / _scaleY);

            if (imgX >= 0 && imgX < _width && imgY >= 0 && imgY < _height)
            {
                // 从 10-bit RAW 数据获取像素值（与算法精度一致）
                int rawIdx = imgY * _width + imgX;
                int rawVal = _raw16Buffer[rawIdx];
                
                // 判断当前像素在 Bayer 模式中的通道类型
                int channelType = GetBayerChannelType(imgX, imgY, _bayerMode);
                string channelName = channelType == 0 ? "R" : (channelType == 2 ? "B" : "G");
                
                // 显示 10-bit RAW 值和通道信息（简化显示，移除 RGB 插值）
                TxtMousePos.Text = $"鼠标: ({imgX}, {imgY}) | RAW={rawVal} ({channelName})";
            }
            else
            {
                TxtMousePos.Text = "";
            }
        }

        private string GetBayerName()
        {
            switch (_bayerMode)
            {
                case 0: return "RGRG";
                case 1: return "GRGR";
                case 2: return "BGBG";
                case 3: return "GBGB";
                default: return "Unknown";
            }
        }
    }
}
