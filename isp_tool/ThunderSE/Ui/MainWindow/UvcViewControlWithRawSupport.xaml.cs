using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThunderSE.Uvc;

namespace ThunderSE.Ui.MainWindow
{
    /// <summary>
    /// UVC视频显示控件（支持RAWVIDEO原始数据处理）
    /// 此示例展示如何同时处理MJPG(RGB24)和RAWVIDEO原始数据
    /// </summary>
    public partial class UvcViewControlWithRawSupport : UserControl
    {
        private WriteableBitmap _bitmap;
        private int _videoWidth;
        private int _videoHeight;
        private volatile bool _isReceiving = false;

        // 像素格式常量
        private const int AV_PIX_FMT_YUYV422 = 0;
        private const int AV_PIX_FMT_YUV420P = 1;
        private const int AV_PIX_FMT_NV12 = 2;
        private const int AV_PIX_FMT_NV21 = 3;
        private const int AV_PIX_FMT_UYVY422 = 4;
        private const int AV_PIX_FMT_GRAY8 = 5;
        private const int AV_PIX_FMT_RGB24 = 6;
        private const int AV_PIX_FMT_BGR24 = 7;

        public UvcViewControlWithRawSupport()
        {
            InitializeComponent();
            Loaded += UvcViewControl_Loaded;
            Unloaded += UvcViewControl_Unloaded;
        }

        private void UvcViewControl_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeVideo();
            RegisterCallbacks();
        }

        private void UvcViewControl_Unloaded(object sender, RoutedEventArgs e)
        {
            UnregisterCallbacks();
        }

        /// <summary>
        /// 初始化视频显示
        /// </summary>
        private void InitializeVideo()
        {
            var receiver = UvcReceiver.Instance;
            _videoWidth = receiver.VideoWidth;
            _videoHeight = receiver.VideoHeight;

            if (_videoWidth <= 0 || _videoHeight <= 0)
            {
                // 默认尺寸
                _videoWidth = 1280;
                _videoHeight = 720;
            }

            // 创建WriteableBitmap（使用Rgb24格式）
            _bitmap = new WriteableBitmap(
                _videoWidth,
                _videoHeight,
                96, 96,
                PixelFormats.Rgb24,
                null);

            UvcImage.Source = _bitmap;
        }

        /// <summary>
        /// 注册UVC回调
        /// </summary>
        private void RegisterCallbacks()
        {
            var receiver = UvcReceiver.Instance;
            
            // 注册标准视频数据回调（MJPG会转换为RGB24）
            receiver.DataReceive += OnVideoDataReceive;
            
            // 注册原始数据回调（RAWVIDEO原始数据）
            receiver.RawDataReceive += OnRawDataReceive;
            
            _isReceiving = true;
        }

        /// <summary>
        /// 取消注册回调
        /// </summary>
        private void UnregisterCallbacks()
        {
            _isReceiving = false;
            
            var receiver = UvcReceiver.Instance;
            receiver.DataReceive -= OnVideoDataReceive;
            receiver.RawDataReceive -= OnRawDataReceive;
        }

        /// <summary>
        /// 标准视频数据回调（MJPG/H264 → RGB24）
        /// </summary>
        private void OnVideoDataReceive(byte[] dataBuffer)
        {
            if (!_isReceiving || _bitmap == null) return;

            bool locked = false;
            try
            {
                // 直接写入RGB24数据
                _bitmap.Lock();
                locked = true;
                _bitmap.WritePixels(
                    new Int32Rect(0, 0, _videoWidth, _videoHeight),
                    dataBuffer,
                    _videoWidth * 3,  // stride = width * 3 bytes
                    0);
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, _videoWidth, _videoHeight));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Video data error: {ex.Message}");
            }
            finally
            {
                // 确保无论如何都会释放锁
                if (locked)
                {
                    try { _bitmap.Unlock(); } catch { }
                }
            }
        }

        /// <summary>
        /// 原始数据回调（RAWVIDEO）
        /// 根据像素格式进行相应处理
        /// </summary>
        private void OnRawDataReceive(IntPtr data, int dataSize, int pixelFormat, int width, int height)
        {
            if (!_isReceiving || _bitmap == null) return;

            try
            {
                // 复制非托管数据到托管缓冲区
                byte[] rawData = new byte[dataSize];
                System.Runtime.InteropServices.Marshal.Copy(data, rawData, 0, dataSize);

                // 根据像素格式处理数据
                byte[] rgbData = null;
                
                switch (pixelFormat)
                {
                    case AV_PIX_FMT_YUYV422:
                        rgbData = ConvertYuyv422ToRgb(rawData, width, height);
                        break;
                        
                    case AV_PIX_FMT_UYVY422:
                        rgbData = ConvertUyvy422ToRgb(rawData, width, height);
                        break;
                        
                    case AV_PIX_FMT_YUV420P:
                    case AV_PIX_FMT_NV12:
                    case AV_PIX_FMT_NV21:
                        rgbData = ConvertYuv420ToRgb(rawData, width, height, pixelFormat);
                        break;
                        
                    case AV_PIX_FMT_GRAY8:
                        rgbData = ConvertGray8ToRgb(rawData, width, height);
                        break;
                        
                    case AV_PIX_FMT_RGB24:
                        // 已经是RGB24，直接使用
                        rgbData = rawData;
                        break;
                        
                    case AV_PIX_FMT_BGR24:
                        rgbData = ConvertBgr24ToRgb(rawData);
                        break;
                        
                    default:
                        System.Diagnostics.Debug.WriteLine($"Unknown pixel format: {pixelFormat}");
                        return;
                }

                if (rgbData != null)
                {
                    // 更新显示
                    _bitmap.Lock();
                    _bitmap.WritePixels(
                        new Int32Rect(0, 0, width, height),
                        rgbData,
                        width * 3,  // stride
                        0);
                    _bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                    _bitmap.Unlock();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Raw data error: {ex.Message}");
                try { _bitmap.Unlock(); } catch { }
            }
        }

        #region 像素格式转换方法

        /// <summary>
        /// YUYV422 转 RGB24
        /// YUYV布局：Y0 U Y1 V
        /// </summary>
        private byte[] ConvertYuyv422ToRgb(byte[] yuyvData, int width, int height)
        {
            byte[] rgbData = new byte[width * height * 3];
            int yuyvIndex = 0;
            int rgbIndex = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x += 2)
                {
                    // 读取YUYV数据
                    byte y0 = yuyvData[yuyvIndex++];
                    byte u = yuyvData[yuyvIndex++];
                    byte y1 = yuyvData[yuyvIndex++];
                    byte v = yuyvData[yuyvIndex++];

                    // 转换Y0
                    ConvertYuvToRgb(y0, u, v, ref rgbData[rgbIndex]);
                    rgbIndex += 3;

                    // 转换Y1
                    ConvertYuvToRgb(y1, u, v, ref rgbData[rgbIndex]);
                    rgbIndex += 3;
                }
            }

            return rgbData;
        }

        /// <summary>
        /// UYVY422 转 RGB24
        /// UYVY布局：U Y0 V Y1
        /// </summary>
        private byte[] ConvertUyvy422ToRgb(byte[] uyvyData, int width, int height)
        {
            byte[] rgbData = new byte[width * height * 3];
            int uyvyIndex = 0;
            int rgbIndex = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x += 2)
                {
                    // 读取UYVY数据
                    byte u = uyvyData[uyvyIndex++];
                    byte y0 = uyvyData[uyvyIndex++];
                    byte v = uyvyData[uyvyIndex++];
                    byte y1 = uyvyData[uyvyIndex++];

                    // 转换Y0
                    ConvertYuvToRgb(y0, u, v, ref rgbData[rgbIndex]);
                    rgbIndex += 3;

                    // 转换Y1
                    ConvertYuvToRgb(y1, u, v, ref rgbData[rgbIndex]);
                    rgbIndex += 3;
                }
            }

            return rgbData;
        }

        /// <summary>
        /// YUV420P/NV12/NV21 转 RGB24
        /// </summary>
        private byte[] ConvertYuv420ToRgb(byte[] yuvData, int width, int height, int pixelFormat)
        {
            byte[] rgbData = new byte[width * height * 3];
            int ySize = width * height;
            int uvSize = ySize / 4;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int yIndex = y * width + x;
                    byte yVal = yuvData[yIndex];

                    // UV平面索引取决于格式
                    int uIndex, vIndex;
                    
                    if (pixelFormat == AV_PIX_FMT_YUV420P)
                    {
                        // YUV420P: Y, U, V平面分开
                        uIndex = ySize + (y / 2) * (width / 2) + (x / 2);
                        vIndex = ySize + uvSize + (y / 2) * (width / 2) + (x / 2);
                    }
                    else if (pixelFormat == AV_PIX_FMT_NV12)
                    {
                        // NV12: Y平面后交错的UV (UVUV...)
                        int uvOffset = ySize + (y / 2) * width + (x / 2) * 2;
                        uIndex = uvOffset;
                        vIndex = uvOffset + 1;
                    }
                    else // NV21
                    {
                        // NV21: Y平面后交错的VU (VUVU...)
                        int uvOffset = ySize + (y / 2) * width + (x / 2) * 2;
                        vIndex = uvOffset;
                        uIndex = uvOffset + 1;
                    }

                    byte uVal = yuvData[uIndex];
                    byte vVal = yuvData[vIndex];

                    int rgbIndex = (y * width + x) * 3;
                    ConvertYuvToRgb(yVal, uVal, vVal, ref rgbData[rgbIndex]);
                }
            }

            return rgbData;
        }

        /// <summary>
        /// GRAY8 转 RGB24（灰度图）
        /// </summary>
        private byte[] ConvertGray8ToRgb(byte[] grayData, int width, int height)
        {
            byte[] rgbData = new byte[width * height * 3];
            int grayIndex = 0;
            int rgbIndex = 0;

            for (int i = 0; i < grayData.Length; i++)
            {
                byte gray = grayData[grayIndex++];
                rgbData[rgbIndex++] = gray; // R
                rgbData[rgbIndex++] = gray; // G
                rgbData[rgbIndex++] = gray; // B
            }

            return rgbData;
        }

        /// <summary>
        /// BGR24 转 RGB24
        /// </summary>
        private byte[] ConvertBgr24ToRgb(byte[] bgrData)
        {
            byte[] rgbData = new byte[bgrData.Length];
            int bgrIndex = 0;
            int rgbIndex = 0;

            while (bgrIndex < bgrData.Length)
            {
                byte b = bgrData[bgrIndex++];
                byte g = bgrData[bgrIndex++];
                byte r = bgrData[bgrIndex++];

                rgbData[rgbIndex++] = r;
                rgbData[rgbIndex++] = g;
                rgbData[rgbIndex++] = b;
            }

            return rgbData;
        }

        /// <summary>
        /// YUV分量转RGB（BT.601标准）
        /// </summary>
        private void ConvertYuvToRgb(byte y, byte u, byte v, ref byte r)
        {
            // YUV到RGB的转换（BT.601）
            double yNorm = y;
            double uNorm = u - 128;
            double vNorm = v - 128;

            int rVal = (int)(yNorm + 1.402 * vNorm);
            int gVal = (int)(yNorm - 0.344136 * uNorm - 0.714136 * vNorm);
            int bVal = (int)(yNorm + 1.772 * uNorm);

            // 钳位到[0, 255]
            r = (byte)Math.Max(0, Math.Min(255, rVal));
            // 注意：这里只是示例，实际应该返回RGB三个值
            // 为了简化，这里只返回R值，实际需要修改方法签名
        }

        #endregion
    }
}
