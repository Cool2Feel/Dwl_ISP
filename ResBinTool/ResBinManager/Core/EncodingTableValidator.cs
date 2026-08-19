using System;

namespace ResBinManager.Core
{
    /// <summary>
    /// 字符编码表资源验证结果
    /// </summary>
    public class EncodingTableValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Info { get; set; }
    }

    /// <summary>
    /// AX329x 字符编码转换表验证器
    /// oem2uni936.bin, uni2oem936.bin (约85KB)
    /// </summary>
    public static class EncodingTableValidator
    {
        private const int EXPECTED_SIZE_MIN = 80000;  // 80KB
        private const int EXPECTED_SIZE_MAX = 90000;  // 90KB

        /// <summary>
        /// 验证字符编码表资源
        /// </summary>
        /// <param name="encodingData">编码表数据</param>
        /// <returns>验证结果</returns>
        public static EncodingTableValidationResult Validate(byte[] encodingData)
        {
            var result = new EncodingTableValidationResult();

            // 1. 检查文件大小是否在合理范围内
            if (encodingData.Length < EXPECTED_SIZE_MIN || encodingData.Length > EXPECTED_SIZE_MAX)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Invalid encoding table size: {encodingData.Length} bytes (expected {EXPECTED_SIZE_MIN}-{EXPECTED_SIZE_MAX})";
                return result;
            }

            // 2. 检查数据合理性（编码表通常包含成对的映射关系）
            // OEM到Unicode的转换表通常是 2字节 -> 2字节 的映射
            int nonZeroMappings = 0;

            // 检查前几个映射条目是否合理
            int checkCount = Math.Min(100, encodingData.Length / 4);
            for (int i = 0; i < checkCount; i++)
            {
                int offset = i * 4;
                if (offset + 3 >= encodingData.Length)
                    break;

                ushort source = BitConverter.ToUInt16(encodingData, offset);
                ushort target = BitConverter.ToUInt16(encodingData, offset + 2);

                if (source != 0 || target != 0)
                {
                    nonZeroMappings++;
                }
            }

            // 如果所有映射都是零，可能不是有效的编码表
            if (nonZeroMappings == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "Encoding table contains all zero mappings, likely invalid";
                return result;
            }

            result.IsValid = true;
            result.Info = $"Valid encoding table: {encodingData.Length} bytes, ~{encodingData.Length / 4} mappings, {nonZeroMappings} non-zero in first 100";
            return result;
        }

        /// <summary>
        /// 获取字符编码表的显示文本
        /// </summary>
        public static string GetDisplayText(EncodingTableValidationResult result)
        {
            if (!result.IsValid)
            {
                return $"❌ Invalid Encoding Table\n{result.ErrorMessage}";
            }

            return $"✓ Valid Encoding Table\n{result.Info}";
        }
    }
}
