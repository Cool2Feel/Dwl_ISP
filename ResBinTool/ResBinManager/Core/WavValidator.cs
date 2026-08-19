using System;
using System.Collections.Generic;
using System.Text;

namespace ResBinManager.Core
{
    /// <summary>
    /// WAV 文件验证结果
    /// </summary>
    public class WavValidationResult
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
        /// WAV 信息（如果解析成功）
        /// </summary>
        public WavInfo? Info { get; set; }

        /// <summary>
        /// 获取验证结果的显示文本
        /// </summary>
        public string GetDisplayText()
        {
            var sb = new StringBuilder();

            if (IsValid && Info != null)
            {
                sb.AppendLine("✓ Valid WAV file");
                sb.AppendLine($"  Format: {Info.Format}");
                sb.AppendLine($"  Sample Rate: {Info.SampleRate} Hz");
                sb.AppendLine($"  Channels: {Info.ChannelsDisplay}");
                sb.AppendLine($"  Bits: {Info.BitsPerSample}-bit");
                sb.AppendLine($"  Duration: {Info.DurationDisplay}");
                sb.AppendLine($"  Size: {Info.DataSize:N0} bytes");

                if (Warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠ Warnings:");
                    foreach (var warning in Warnings)
                    {
                        sb.AppendLine($"  - {warning}");
                    }
                }
            }
            else
            {
                sb.AppendLine($"✗ Invalid WAV file");
                sb.AppendLine($"  Error: {ErrorMessage}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// WAV 文件验证器
    /// </summary>
    public static class WavValidator
    {
        /// <summary>
        /// 验证 WAV 文件格式
        /// </summary>
        /// <param name="wavData">WAV 文件数据</param>
        /// <returns>验证结果</returns>
        public static WavValidationResult Validate(byte[] wavData)
        {
            var result = new WavValidationResult();

            // 1. 基本大小检查
            if (wavData == null || wavData.Length == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "File is empty";
                return result;
            }

            if (wavData.Length < 44)
            {
                result.IsValid = false;
                result.ErrorMessage = $"File too small ({wavData.Length} bytes). Minimum WAV size is 44 bytes.";
                return result;
            }

            // 2. 验证 RIFF 魔数
            string riff = Encoding.ASCII.GetString(wavData, 0, 4);
            if (riff != "RIFF")
            {
                result.IsValid = false;
                result.ErrorMessage = $"Invalid file format. Expected 'RIFF', got '{riff}'";
                return result;
            }

            // 3. 验证 WAVE 标识
            string wave = Encoding.ASCII.GetString(wavData, 8, 4);
            if (wave != "WAVE")
            {
                result.IsValid = false;
                result.ErrorMessage = $"Not a WAV file. Expected 'WAVE', got '{wave}'";
                return result;
            }

            // 4. 尝试解析详细信息
            try
            {
                var info = WavInfoParser.Parse(wavData);
                result.Info = info;
                result.IsValid = true;

                // 5. 添加警告（非致命问题）
                AddWarnings(info, result);
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Failed to parse WAV header: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// 比较两个 WAV 文件的参数差异
        /// </summary>
        /// <param name="oldInfo">原始 WAV 信息</param>
        /// <param name="newInfo">新 WAV 信息</param>
        /// <returns>差异描述</returns>
        public static string CompareWavInfo(WavInfo oldInfo, WavInfo newInfo)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Parameter Comparison:");
            sb.AppendLine();

            // 采样率
            if (oldInfo.SampleRate != newInfo.SampleRate)
            {
                sb.AppendLine($"⚠ Sample Rate: {oldInfo.SampleRate} Hz → {newInfo.SampleRate} Hz");
            }
            else
            {
                sb.AppendLine($"✓ Sample Rate: {oldInfo.SampleRate} Hz");
            }

            // 声道数
            if (oldInfo.Channels != newInfo.Channels)
            {
                sb.AppendLine($"⚠ Channels: {oldInfo.ChannelsDisplay} → {newInfo.ChannelsDisplay}");
            }
            else
            {
                sb.AppendLine($"✓ Channels: {oldInfo.ChannelsDisplay}");
            }

            // 位深度
            if (oldInfo.BitsPerSample != newInfo.BitsPerSample)
            {
                sb.AppendLine($"⚠ Bits: {oldInfo.BitsPerSample}-bit → {newInfo.BitsPerSample}-bit");
            }
            else
            {
                sb.AppendLine($"✓ Bits: {oldInfo.BitsPerSample}-bit");
            }

            // 时长
            double durationDiff = (newInfo.Duration - oldInfo.Duration).TotalSeconds;
            if (Math.Abs(durationDiff) > 0.1)
            {
                string sign = durationDiff > 0 ? "+" : "";
                sb.AppendLine($"ℹ Duration: {oldInfo.DurationDisplay} → {newInfo.DurationDisplay} ({sign}{durationDiff:F2}s)");
            }
            else
            {
                sb.AppendLine($"✓ Duration: {oldInfo.DurationDisplay}");
            }

            // 文件大小
            long sizeDiff = newInfo.DataSize - oldInfo.DataSize;
            if (sizeDiff != 0)
            {
                string sign = sizeDiff > 0 ? "+" : "";
                sb.AppendLine($"ℹ File Size: {oldInfo.DataSize:N0} → {newInfo.DataSize:N0} bytes ({sign}{sizeDiff:N0})");
            }
            else
            {
                sb.AppendLine($"✓ File Size: {oldInfo.DataSize:N0} bytes");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 添加警告信息
        /// </summary>
        private static void AddWarnings(WavInfo info, WavValidationResult result)
        {
            // 采样率警告
            if (info.SampleRate < 8000)
            {
                result.Warnings.Add($"Very low sample rate ({info.SampleRate} Hz). May sound poor quality.");
            }
            else if (info.SampleRate > 48000)
            {
                result.Warnings.Add($"High sample rate ({info.SampleRate} Hz). Ensure device supports it.");
            }

            // 位深度警告
            if (info.BitsPerSample == 8)
            {
                result.Warnings.Add("8-bit audio has limited dynamic range.");
            }
            else if (info.BitsPerSample > 16)
            {
                result.Warnings.Add($"{info.BitsPerSample}-bit audio may not be fully supported by all devices.");
            }

            // 声道数警告
            if (info.Channels > 2)
            {
                result.Warnings.Add($"Multi-channel audio ({info.Channels} channels). Device may downmix to stereo.");
            }

            // 时长警告
            if (info.Duration.TotalSeconds > 10)
            {
                result.Warnings.Add($"Long audio clip ({info.DurationDisplay}). Consider shortening for better performance.");
            }

            // 文件大小警告
            if (info.DataSize > 100 * 1024) // 100KB
            {
                result.Warnings.Add($"Large file size ({info.DataSize / 1024} KB). May increase firmware size significantly.");
            }
        }
    }
}
