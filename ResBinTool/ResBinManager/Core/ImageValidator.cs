using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace ResBinManager.Core
{
    /// <summary>
    /// 图片验证结果
    /// </summary>
    public class ImageValidationResult
    {
        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 错误消息（如果无效）
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 警告消息列表
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// 图片信息
        /// </summary>
        public ImageInfo Info { get; set; }

        /// <summary>
        /// 分辨率是否匹配
        /// </summary>
        public bool ResolutionMatches { get; set; }

        /// <summary>
        /// 原始图片信息（如果提供了原始资源）
        /// </summary>
        public ImageInfo OriginalInfo { get; set; }

        /// <summary>
        /// 获取验证结果的显示文本
        /// </summary>
        public string GetDisplayText()
        {
            var sb = new StringBuilder();

            if (IsValid && Info != null)
            {
                sb.AppendLine("✅Valid image file");
                sb.AppendLine($"  Format: {Info.Format}");
                sb.AppendLine($"  Resolution: {Info.Width} x {Info.Height}");
                sb.AppendLine($"  Size: {Info.FileSize:N0} bytes");

                if (!ResolutionMatches && OriginalInfo != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠️Resolution Mismatch:");
                    sb.AppendLine($"  Original: {OriginalInfo.Width} x {OriginalInfo.Height}");
                    sb.AppendLine($"  New:      {Info.Width} x {Info.Height}");
                }

                if (Warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠️Warnings:");
                    foreach (var warning in Warnings)
                    {
                        sb.AppendLine($"  - {warning}");
                    }
                }
            }
            else
            {
                sb.AppendLine($"❌Invalid image file");
                sb.AppendLine($"  Error: {ErrorMessage}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// 图片信息
    /// </summary>
    public class ImageInfo
    {
        /// <summary>
        /// 图片宽度（像素）
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 图片高度（像素）
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 图片格式（JPEG/PNG/BMP等）
        /// </summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        public int FileSize { get; set; }

        /// <summary>
        /// 位深度
        /// </summary>
        public int BitsPerPixel { get; set; }

        /// <summary>
        /// 分辨率显示
        /// </summary>
        public string ResolutionDisplay => $"{Width} x {Height}";
    }

    /// <summary>
    /// 图片资源验证器
    /// 用于验证替换图片的分辨率是否与原图一致
    /// </summary>
    public static class ImageValidator
    {
        /// <summary>
        /// 验证图片并检查分辨率匹配
        /// </summary>
        /// <param name="newImageData">新图片数据</param>
        /// <param name="originalImageData">原始图片数据（可选，用于分辨率对比）</param>
        /// <returns>验证结果</returns>
        public static ImageValidationResult Validate(byte[] newImageData, byte[] originalImageData = null)
        {
            var result = new ImageValidationResult();

            // 1. 基本大小检查
            if (newImageData == null || newImageData.Length == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "File is empty";
                return result;
            }

            if (newImageData.Length < 10)
            {
                result.IsValid = false;
                result.ErrorMessage = $"File too small ({newImageData.Length} bytes). Not a valid image.";
                return result;
            }

            // 2. 解析新图片信息
            var newInfo = ParseImageInfo(newImageData);
            if (newInfo == null)
            {
                result.IsValid = false;
                result.ErrorMessage = "Failed to parse image file. The file may be corrupted or in an unsupported format.";
                return result;
            }

            result.Info = newInfo;
            result.IsValid = true;

            // 3. 如果提供了原始图片数据，解析并比较分辨率
            if (originalImageData != null && originalImageData.Length > 0)
            {
                var originalInfo = ParseImageInfo(originalImageData);
                if (originalInfo != null)
                {
                    result.OriginalInfo = originalInfo;
                    result.ResolutionMatches = (newInfo.Width == originalInfo.Width && 
                                                newInfo.Height == originalInfo.Height);

                    if (!result.ResolutionMatches)
                    {
                        result.Warnings.Add(
                            $"Resolution mismatch: original is {originalInfo.Width}x{originalInfo.Height}, " +
                            $"new is {newInfo.Width}x{newInfo.Height}");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 解析图片信息
        /// </summary>
        private static ImageInfo ParseImageInfo(byte[] imageData)
        {
            try
            {
                using (var ms = new MemoryStream(imageData))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    string format = DetectImageFormat(imageData);
                    int bitsPerPixel = bitmap.Format != null ? bitmap.Format.BitsPerPixel : 0;

                    return new ImageInfo
                    {
                        Width = bitmap.PixelWidth,
                        Height = bitmap.PixelHeight,
                        Format = format,
                        FileSize = imageData.Length,
                        BitsPerPixel = bitsPerPixel
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageValidator] Failed to parse image: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 检测图片格式
        /// </summary>
        private static string DetectImageFormat(byte[] imageData)
        {
            if (imageData.Length < 4)
                return "Unknown";

            // JPEG: FF D8 FF
            if (imageData[0] == 0xFF && imageData[1] == 0xD8 && imageData[2] == 0xFF)
                return "JPEG";

            // PNG: 89 50 4E 47
            if (imageData[0] == 0x89 && imageData[1] == 0x50 && 
                imageData[2] == 0x4E && imageData[3] == 0x47)
                return "PNG";

            // BMP: 42 4D
            if (imageData[0] == 0x42 && imageData[1] == 0x4D)
                return "BMP";

            // GIF: 47 49 46 38
            if (imageData[0] == 0x47 && imageData[1] == 0x49 && 
                imageData[2] == 0x46 && imageData[3] == 0x38)
                return "GIF";

            // WEBP: 52 49 46 46 ... 57 45 42 50
            if (imageData.Length >= 12 &&
                imageData[0] == 0x52 && imageData[1] == 0x49 &&
                imageData[2] == 0x46 && imageData[3] == 0x46 &&
                imageData[8] == 0x57 && imageData[9] == 0x45 &&
                imageData[10] == 0x42 && imageData[11] == 0x50)
                return "WEBP";

            return "Unknown";
        }

        /// <summary>
        /// 比较两张图片的分辨率
        /// </summary>
        public static string CompareResolution(ImageInfo original, ImageInfo? newImage)
        {
            if (original == null || newImage == null)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("Resolution Comparison:");
            sb.AppendLine($"  Original: {original.Width} x {original.Height}");
            sb.AppendLine($"  New:      {newImage.Width} x {newImage.Height}");

            if (original.Width == newImage.Width && original.Height == newImage.Height)
            {
                sb.AppendLine("  ✅Resolution matches");
            }
            else
            {
                sb.AppendLine("  ⚠️Resolution mismatch!");
                sb.AppendLine($"  Width difference:  {newImage.Width - original.Width:+#;-#;0} pixels");
                sb.AppendLine($"  Height difference: {newImage.Height - original.Height:+#;-#;0} pixels");
            }

            return sb.ToString();
        }
    }
}
