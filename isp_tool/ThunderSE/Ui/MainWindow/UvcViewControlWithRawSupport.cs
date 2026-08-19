using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThunderSE.Uvc;

namespace ThunderSE.Ui.MainWindow
{
    /// <summary>
    /// UVC视频显示控件（支持RAWVIDEO原始数据）
    /// 自动处理MJPG(RGB24)和RAWVIDEO(YUV/RGB等原始格式)
    /// </summary>
    public class UvcViewControlWithRawSupport : Image
    {
        private WriteableBitmap _bitmap;
        private int _videoWidth;
        private int _videoHeight;
        private volatile bool _isReceiving = false;

        // FFmpeg像素格式常量（与C++端AVPixelFormat对应）
        public const int AV_PIX_FMT_YUYV422 = 0;       // 16位YUV422 (Y0 U Y1 V)
        public const int AV_PIX_FMT_YUV420P = 1;       // 12位YUV420 (平面)
        public const int AV_PIX_FMT_NV12 = 2;          // 12位YUV420 (Y后交错的UV)
        public const int AV_PIX_FMT_NV21 = 3;          // 12位YUV420 (Y后交错的VU)
        public const int AV_PIX_FMT_UYVY422 = 4;       // 16位YUV422 (U Y0 V Y1)
        public const int AV_PIX_FMT_GRAY8 = 5;         // 8位灰度
        public const int AV_PIX_FMT_RGB24 = 6;         // 24位RGB
        public const int AV_PIX_FMT_BGR24 = 7;         // 24位BGR

        /// <summary>
        /// 初始化视频控件
        /// </summary>
        public void Initialize(int width, int height)
        {
            _videoWidth = width;
            _videoHeight = height;

            // 创建WriteableBitmap用于RGB24显示
            _bitmap = new WriteableBitmap(
                width, height,
                96, 96,
                PixelFormats.Rgb24,
                null);

            this.Source = _bitmap;
        }

        /// <summary>
        /// 注册UVC回调
        /// </summary>
        public void StartReceiving()
        {
            var receiver = UvcReceiver.Instance;
            receiver.DataReceive += OnVideoDataReceive;
            receiver.RawDataReceive += OnRawDataReceive;
            _isReceiving = true;
        }

        /// <summary>
        /// 停止接收
        /// </summary>
        public void StopReceiving()
        {
            _isReceiving = false;
            var receiver = UvcReceiver.Instance;
            receiver.DataReceive -= OnVideoDataReceive;
            receiver.RawDataReceive -= OnRawDataReceive;
        }

        /// <summary>
        /// 标准视频数据回调（MJPG/H264已转换为RGB24）
        /// </summary>
        private void OnVideoDataReceive(byte[] dataBuffer)
        {
            if (!_isReceiving || _bitmap == null) return;

            try
            {
                // 直接写入RGB24数据
                _bitmap.Lock();
                _bitmap.WritePixels(
                    new Int32Rect(0, 0, _videoWidth, _videoHeight),
                    dataBuffer,
                    _videoWidth * 3,  // stride = width * 3 bytes
                    0);
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, _videoWidth, _videoHeight));
                _bitmap.Unlock();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MJPG] Video data error: {ex.Message}");
                try { _bitmap.Unlock(); } catch { }
            }
        }

        /// <summary>
        /// 原始数据回调（RAWVIDEO）
        /// 根据像素格式自动转换到RGB24显示
        /// </summary>
        private void OnRawDataReceive(IntPtr data, int dataSize, int pixelFormat, int width, int height)
        {
            if (!_isReceiving || _bitmap == null) return;

            try
            {
                // 复制非托管数据到托管缓冲区
                byte[] rawData = new byte[dataSize];
                System.Runtime.InteropServices.Marshal.Copy(data, rawData, 0, dataSize);

                // 根据像素格式转换为RGB24
                byte[] rgbData = ConvertToRgb(rawData, width, height, pixelFormat);

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
                System.Diagnostics.Debug.WriteLine($"[RAW] Data error: {ex.Message}");
                try { _bitmap.Unlock(); } catch { }
            }
        }

        /// <summary>
        /// 将各种像素格式转换为RGB24
        /// </summary>
        private byte[] ConvertToRgb(byte[] data, int width, int height, int pixelFormat)
        {
            switch (pixelFormat)
            {
                case AV_PIX_FMT_YUYV422:
                    return ConvertYuyv422ToRgb(data, width, height);

                case AV_PIX_FMT_UYVY422:
                    return ConvertUyvy422ToRgb(data, width, height);

                case AV_PIX_FMT_YUV420P:
                case AV_PIX_FMT_NV12:
                case AV_PIX_FMT_NV21:
                    return ConvertYuv420ToRgb(data, width, height, pixelFormat);

                case AV_PIX_FMT_GRAY8:
                    return ConvertGray8ToRgb(data, width, height);

                case AV_PIX_FMT_RGB24:
                    return data;  // 已经是RGB24

                case AV_PIX_FMT_BGR24:
                    return ConvertBgr24ToRgb(data);

                default:
                    System.Diagnostics.Debug.WriteLine($"[RAW] Unsupported format: {pixelFormat}");
                    return null;
            }
        }

        #region 像素格式转换实现

        /// <summary>
        /// YUYV422 → RGB24
        /// 布局：[Y0][U][Y1][V] 每4字节包含2个像素
        /// </summary>
        private byte[] ConvertYuyv422ToRgb(byte[] yuyvData, int width, int height)
        {
            byte[] rgbData = new byte[width * height * 3];
            int srcIdx = 0;
            int dstIdx = 0;

            for (int i = 0; i < width * height; i += 2)
            {
                byte y0 = yuyvData[srcIdx++];
                byte u = yuyvData[srcIdx++];
                byte y1 = yuyvData[srcIdx++];
                byte v = yuyvData[srcIdx++];

                // Y0 → RGB
                YuvToRgb(y0, u, v, rgbData, dstIdx);
                dstIdx += 3;

                // Y1 → RGB
                YuvToRgb(y1, u, v, rgbData, dstIdx);
                dstIdx += 3;
            }

            return rgbData;
        }

        /// <summary>
        /// UYVY422 → RGB24
        /// 布局：[U][Y0][V][Y1] 每4字节包含2个像素
        /// </summary>
        private byte[] ConvertUyvy422ToRgb(byte[] uyvyData, int width, int height)
        {
            byte[] rgbData = new byte[width * height * 3];
            int srcIdx = 0;
            int dstIdx = 0;

            for (int i = 0; i < width * height; i += 2)
            {
                byte u = uyvyData[srcIdx++];
                byte y0 = uyvyData[srcIdx++];
                byte v = uyvyData[srcIdx++];
                byte y1 = uyvyData[srcIdx++];

                YuvToRgb(y0, u, v, rgbData, dstIdx);
                dstIdx += 3;

                YuvToRgb(y1, u, v, rgbData, dstIdx);
                dstIdx += 3;
            }

            return rgbData;
        }

        /// <summary>
        /// YUV420 (YUV420P/NV12/NV21) → RGB24
        /// </summary>
        private byte[] ConvertYuv420ToRgb(byte[] yuvData, int width, int height, int pixelFormat)
        {
            byte[] rgbData = new byte[width * height * 3];
            int ySize = width * height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Y分量
                    int yIdx = y * width + x;
                    byte yVal = yuvData[yIdx];

                    // UV分量（根据格式不同）
                    int uIdx, vIdx;

                    if (pixelFormat == AV_PIX_FMT_YUV420P)
                    {
                        // YUV420P: 三个独立平面
                        uIdx = ySize + (y / 2) * (width / 2) + (x / 2);
                        vIdx = ySize + (ySize / 4) + (y / 2) * (width / 2) + (x / 2);
                    }
                    else if (pixelFormat == AV_PIX_FMT_NV12)
                    {
                        // NV12: Y后交错UV
                        int uvOffset = ySize + (y / 2) * width + (x / 2) * 2;
                        uIdx = uvOffset;
                        vIdx = uvOffset + 1;
                    }
                    else // NV21
                    {
                        // NV21: Y后交错VU
                        int uvOffset = ySize + (y / 2) * width + (x / 2) * 2;
                        vIdx = uvOffset;
                        uIdx = uvOffset + 1;
                    }

                    byte uVal = yuvData[uIdx];
                    byte vVal = yuvData[vIdx];

                    int rgbIdx = (y * width + x) * 3;
                    YuvToRgb(yVal, uVal, vVal, rgbData, rgbIdx);
                }
            }

            return rgbData;
        }

        /// <summary>
        /// GRAY8 → RGB24 (灰度图扩展为RGB)
        /// </summary>
        private byte[] ConvertGray8ToRgb(byte[] grayData, int width, int height)
        {
            byte[] rgbData = new byte[width * height * 3];
            int srcIdx = 0;
            int dstIdx = 0;

            foreach (byte gray in grayData)
            {
                rgbData[dstIdx++] = gray;  // R
                rgbData[dstIdx++] = gray;  // G
                rgbData[dstIdx++] = gray;  // B
            }

            return rgbData;
        }

        /// <summary>
        /// BGR24 → RGB24 (字节序交换)
        /// </summary>
        private byte[] ConvertBgr24ToRgb(byte[] bgrData)
        {
            byte[] rgbData = new byte[bgrData.Length];
            int srcIdx = 0;
            int dstIdx = 0;

            while (srcIdx < bgrData.Length)
            {
                byte b = bgrData[srcIdx++];
                byte g = bgrData[srcIdx++];
                byte r = bgrData[srcIdx++];

                rgbData[dstIdx++] = r;
                rgbData[dstIdx++] = g;
                rgbData[dstIdx++] = b;
            }

            return rgbData;
        }

        /// <summary>
        /// YUV → RGB 单像素转换 (BT.601标准)
        /// </summary>
        private void YuvToRgb(byte y, byte u, byte v, byte[] rgb, int offset)
        {
            // YUV到RGB转换 (BT.601)
            double c = y - 16;
            double d = u - 128;
            double e = v - 128;

            int r = Clamp((int)(1.164 * c + 1.596 * e));
            int g = Clamp((int)(1.164 * c - 0.392 * d - 0.813 * e));
            int b = Clamp((int)(1.164 * c + 2.017 * d));

            rgb[offset] = (byte)r;
            rgb[offset + 1] = (byte)g;
            rgb[offset + 2] = (byte)b;
        }

        /// <summary>
        /// 钳位函数 [0, 255]
        /// </summary>
        private int Clamp(int value)
        {
            return Math.Max(0, Math.Min(255, value));
        }

        #endregion
    }
}
