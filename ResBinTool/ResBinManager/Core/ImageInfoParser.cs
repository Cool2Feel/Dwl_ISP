using System;
using System.IO;
using System.Windows.Media.Imaging;
using ResBinManager.Models;

namespace ResBinManager.Core
{
    public static class ImageInfoParser
    {
        public struct ImageDimensions
        {
            public int Width;
            public int Height;
            public bool Valid;

            public ImageDimensions(int width, int height)
            {
                Width = width;
                Height = height;
                Valid = width > 0 && height > 0;
            }

            public static ImageDimensions Invalid => new ImageDimensions(0, 0);
        }

        public static ImageDimensions ParseImageDimensions(byte[] data, ResourceType type = ResourceType.Jpeg)
        {
            if (data == null || data.Length < 10)
                return ImageDimensions.Invalid;

            if (!ValidateMagicBytes(data, type))
            {
                System.Diagnostics.Debug.WriteLine($"[ImageInfoParser] Magic byte validation failed for type {type}, skipping image parse");
                return ImageDimensions.Invalid;
            }

            try
            {
                using (var ms = new MemoryStream(data))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    System.Diagnostics.Debug.WriteLine($"[ImageInfoParser] Image parsed successfully: {bitmap.PixelWidth}x{bitmap.PixelHeight}");
                    return new ImageDimensions(bitmap.PixelWidth, bitmap.PixelHeight);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageInfoParser] Failed to parse image: {ex.Message}");
                return ImageDimensions.Invalid;
            }
        }

        private static bool ValidateMagicBytes(byte[] data, ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Jpeg:
                    if (data.Length < 3) return false;
                    return data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

                case ResourceType.Png:
                    if (data.Length < 8) return false;
                    return data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                           data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

                case ResourceType.Bitmap:
                    if (data.Length < 2) return false;
                    return data[0] == (byte)'B' && data[1] == (byte)'M';

                default:
                    return true;
            }
        }
    }
}