using System;
using System.IO;
using System.Text;

namespace ResBinManager.Core
{
    /// <summary>
    /// WAV 音频文件信息
    /// </summary>
    public class WavInfo
    {
        /// <summary>
        /// 采样率 (Hz)
        /// </summary>
        public int SampleRate { get; set; }

        /// <summary>
        /// 声道数 (1=Mono, 2=Stereo)
        /// </summary>
        public int Channels { get; set; }

        /// <summary>
        /// 位深 (bits)
        /// </summary>
        public int BitsPerSample { get; set; }

        /// <summary>
        /// 数据大小 (bytes)
        /// </summary>
        public int DataSize { get; set; }

        /// <summary>
        /// 音频时长
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// 音频格式
        /// </summary>
        public string Format { get; set; } = "PCM";

        /// <summary>
        /// 显示用的时长字符串
        /// </summary>
        public string DurationDisplay => Duration.TotalSeconds < 60 
            ? $"{Duration.TotalSeconds:F2}s" 
            : $"{(int)Duration.TotalMinutes}:{Duration.Seconds:D2}";

        /// <summary>
        /// 显示用的采样率字符串
        /// </summary>
        public string SampleRateDisplay => $"{SampleRate} Hz";

        /// <summary>
        /// 显示用的声道字符串
        /// </summary>
        public string ChannelsDisplay => Channels == 1 ? "Mono" : "Stereo";

        /// <summary>
        /// 显示用的格式描述
        /// </summary>
        public string FormatDisplay => $"{SampleRate}Hz, {BitsPerSample}-bit, {ChannelsDisplay}";

        /// <summary>
        /// 完整的描述信息
        /// </summary>
        public string FullDescription => $"{FormatDisplay}, {DurationDisplay}";
    }

    /// <summary>
    /// WAV 文件信息解析器
    /// </summary>
    public static class WavInfoParser
    {
        /// <summary>
        /// 解析 WAV 文件头，提取音频信息
        /// </summary>
        /// <param name="wavData">WAV 文件的二进制数据</param>
        /// <returns>WAV 信息对象</returns>
        /// <exception cref="InvalidDataException">当文件格式无效时抛出</exception>
        public static WavInfo Parse(byte[] wavData)
        {
            if (wavData == null || wavData.Length < 44)
                throw new InvalidDataException("WAV file too small or invalid");

            // 验证 RIFF 标志
            string riff = Encoding.ASCII.GetString(wavData, 0, 4);
            if (riff != "RIFF")
                throw new InvalidDataException($"Invalid WAV file: expected 'RIFF', got '{riff}'");

            // 验证 WAVE 标志
            string wave = Encoding.ASCII.GetString(wavData, 8, 4);
            if (wave != "WAVE")
                throw new InvalidDataException($"Invalid WAV file: expected 'WAVE', got '{wave}'");

            var info = new WavInfo();

            try
            {
                // 读取 fmt chunk 信息 (offset 12-35)
                // Audio format (2 bytes): 1 = PCM
                short audioFormat = BitConverter.ToInt16(wavData, 20);
                info.Format = audioFormat == 1 ? "PCM" : $"Format {audioFormat}";

                // Number of channels (2 bytes)
                info.Channels = BitConverter.ToInt16(wavData, 22);

                // Sample rate (4 bytes)
                info.SampleRate = BitConverter.ToInt32(wavData, 24);

                // Byte rate (4 bytes) - can be used for validation
                int byteRate = BitConverter.ToInt32(wavData, 28);

                // Block align (2 bytes)
                short blockAlign = BitConverter.ToInt16(wavData, 32);

                // Bits per sample (2 bytes)
                info.BitsPerSample = BitConverter.ToInt16(wavData, 34);

                // 查找 data chunk
                int dataChunkOffset = FindDataChunk(wavData);
                if (dataChunkOffset < 0)
                    throw new InvalidDataException("Cannot find 'data' chunk in WAV file");

                // 读取 data chunk 大小
                info.DataSize = BitConverter.ToInt32(wavData, dataChunkOffset + 4);

                // 计算时长
                if (info.SampleRate > 0 && info.Channels > 0 && info.BitsPerSample > 0)
                {
                    int bytesPerSecond = info.SampleRate * info.Channels * (info.BitsPerSample / 8);
                    if (bytesPerSecond > 0)
                    {
                        info.Duration = TimeSpan.FromSeconds((double)info.DataSize / bytesPerSecond);
                    }
                }

                // 验证合理性
                if (info.SampleRate < 8000 || info.SampleRate > 192000)
                    throw new InvalidDataException($"Unusual sample rate: {info.SampleRate} Hz");

                if (info.Channels < 1 || info.Channels > 8)
                    throw new InvalidDataException($"Invalid channel count: {info.Channels}");

                if (info.BitsPerSample != 8 && info.BitsPerSample != 16 && 
                    info.BitsPerSample != 24 && info.BitsPerSample != 32)
                    throw new InvalidDataException($"Unsupported bits per sample: {info.BitsPerSample}");
            }
            catch (Exception ex) when (!(ex is InvalidDataException))
            {
                throw new InvalidDataException($"Failed to parse WAV header: {ex.Message}", ex);
            }

            return info;
        }

        /// <summary>
        /// 查找 data chunk 的偏移位置
        /// </summary>
        private static int FindDataChunk(byte[] wavData)
        {
            // 从 offset 12 开始搜索 "data" 标志
            for (int i = 12; i < wavData.Length - 8; i += 2)
            {
                if (wavData[i] == 'd' && wavData[i + 1] == 'a' &&
                    wavData[i + 2] == 't' && wavData[i + 3] == 'a')
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 验证 WAV 文件是否有效
        /// </summary>
        public static bool IsValidWav(byte[] wavData)
        {
            try
            {
                Parse(wavData);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
