using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThunderSE.DeviceConfig;
using ThunderSE.DeviceConfig.Isp;

namespace ThunderSE.Ui.SettingWindow.Lsc
{
    /// <summary>
    /// RAW缓冲区到BitmapImage的转换器
    /// 负责将RAW图像数据解码并渲染为WPF可显示的BitmapImage
    /// </summary>
    public class RawBufferToBitmapImageConverter : IValueConverter, IDisposable
    {
        /// <summary>
        /// 处理器通用配置，用于获取分辨率和Bayer模式
        /// 需在Window初始化时设置
        /// </summary>
        public CommonConfig ProcessorCommonConfig { get; set; }

        // 使用局部MemoryManager,每次Convert后自动释放
        private static ImageProcessingCache _imageCache = new ImageProcessingCache();

        /// <summary>
        /// 将RAW Buffer转换为BitmapImage
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var rawImgBuffer = value as byte[];
            if (rawImgBuffer == null || ProcessorCommonConfig == null)
            {
                return null;
            }

            // 生成缓存键
            //string cacheKey = _imageCache.GetCacheKey(rawImgBuffer,
            //    ProcessorCommonConfig.ResolutionWidth,
            //    ProcessorCommonConfig.ResolutionHeight,
            //    (int)ProcessorCommonConfig.Bayer);

            //// 尝试从缓存取
            //if (_imageCache.TryGetCachedImage(cacheKey, out byte[] cachedBuffer))
            //{
            //    return CreateBitmapImageFromBuffer(cachedBuffer);
            //}

            // 每次Convert创建新的MemoryManager,确保内存在方法结束时释放
            using (var localMemoryManager = new MemoryManager())
            {
                IntPtr[] ptrArray = new IntPtr[3];
                int width = ProcessorCommonConfig.ResolutionWidth;
                int height = ProcessorCommonConfig.ResolutionHeight;
                int bufferSize = width * height * sizeof(short);

                try
                {
                    // 分配并初始化非托管内存
                    for (int i = 0; i < ptrArray.Length; i++)
                    {
                        ptrArray[i] = localMemoryManager.AllocateMemory(bufferSize);
                        Marshal.Copy(new byte[bufferSize], 0, ptrArray[i], bufferSize);
                    }

                    // 测量处理时间
                    var stopwatch = Stopwatch.StartNew();

                    // 调用ISP API进行Demosaic和编码
                    IspApi.DemosaicImg(rawImgBuffer, (int)ProcessorCommonConfig.Bayer, width, height, ptrArray);

                    int size = 0;
                    IspApi.EncoderImgBuffer(ptrArray, width, height, 2, null, ref size);

                    byte[] buffer = new byte[size];
                    IspApi.EncoderImgBuffer(ptrArray, width, height, 2, buffer, ref size);

                    stopwatch.Stop();
                    Debug.WriteLine($"[LSC] 图像处理耗时: {stopwatch.ElapsedMilliseconds}ms");

                    // 添加到缓存
                    //_imageCache.AddToCache(cacheKey, buffer);

                    // 创建BitmapImage
                    var image = CreateBitmapImageFromBuffer(buffer);
                    return image;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LSC] 图片转换失败: {ex.Message}");
                    return null;
                }
                // using块结束,localMemoryManager.Dispose()自动释放所有分配的内存
            }
        }

        /// <summary>
        /// 从字节数组创建BitmapImage
        /// </summary>
        private BitmapImage CreateBitmapImageFromBuffer(byte[] buffer)
        {
            var image = new BitmapImage();
            using (var memStream = new MemoryStream(buffer))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = memStream;
                image.EndInit();
                image.Freeze(); // 冻结使图片可以在其他线程使用
            }
            // 创建垂直翻转的变换 (ScaleY = -1)
            //var flipTransform = new ScaleTransform(1, -1);

            // 使用 TransformedBitmap 创建新图
            //var flippedImage = new TransformedBitmap(image, flipTransform);

            // 别忘了将新图也 Freeze，这样它依然可以跨线程使用
            //flippedImage.Freeze();

            //return flippedImage;
            return image;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public static void ClearCache()
        {
            _imageCache.ClearCache();
        }

        public void Dispose()
        {
            // 清理静态缓存(注意:会影响所有实例)
            // _imageCache.ClearCache();  // 注释掉,避免影响其他窗口实例
        }
    }
}
