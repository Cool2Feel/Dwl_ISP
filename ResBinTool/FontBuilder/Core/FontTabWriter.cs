using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FontBuilder.Core
{
    /// <summary>
    /// font.tab 写入器：字符串 ID 名称列表
    /// 格式示例:
    ///   STR_LAN_ENGLISH,
    ///   STR_LAN_SCHINESE,
    ///   STR_COM_OFF,
    ///   ...
    /// </summary>
    public static class FontTabWriter
    {
        /// <summary>
        /// 写入 font.tab
        /// </summary>
        /// <param name="stringIdNames">字符串 ID 名称列表（不含 STR_ 前缀也行，按需补齐）</param>
        /// <param name="outputPath">输出路径</param>
        public static void Write(List<string> stringIdNames, string outputPath)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < stringIdNames.Count; i++)
            {
                sb.Append(stringIdNames[i]);
                sb.AppendLine(",");
            }
            File.WriteAllText(outputPath, sb.ToString(), Encoding.ASCII);
        }

        /// <summary>
        /// 从字符串 ID 名称列表生成默认的 font.tab 内容
        /// 若用户提供的字符串顺序与 user_str.h 中 R_ID_STR_* 枚举一致，直接使用
        /// </summary>
        public static string BuildContent(List<string> stringIdNames)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < stringIdNames.Count; i++)
            {
                sb.Append(stringIdNames[i]);
                if (i < stringIdNames.Count - 1) sb.Append(",\n");
            }
            return sb.ToString();
        }
    }
}
