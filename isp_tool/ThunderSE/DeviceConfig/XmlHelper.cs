using System.Xml;

namespace ThunderSE.DeviceConfig
{
    /// <summary>
    /// XML解析辅助�?提供安全的节点访问方�?    /// </summary>
    internal static class XmlHelper
    {
        /// <summary>
        /// 安全获取XML节点的文本�?        /// </summary>
        /// <param name="parentNode">父节�?/param>
        /// <param name="childName">子节点名�?/param>
        /// <param name="defaultValue">默认�?当节点不存在或为空时)</param>
        /// <returns>节点文本值或默认�?/returns>
        public static string GetNodeValue(XmlNode parentNode, string childName, string defaultValue = null)
        {
            var childNode = parentNode[childName];
            if (childNode == null || childNode.FirstChild == null)
            {
                return defaultValue;
            }
            // 使用InnerText更可靠,适用于各种节点类型
            return childNode.InnerText;
        }

        /// <summary>
        /// 安全获取XML节点的整数�?        /// </summary>
        public static int GetNodeInt(XmlNode parentNode, string childName, int defaultValue = 0)
        {
            var value = GetNodeValue(parentNode, childName);
            if (value == null)
            {
                return defaultValue;
            }
            return int.TryParse(value, out int result) ? result : defaultValue;
        }

        /// <summary>
        /// 安全获取XML节点的short�?        /// </summary>
        public static short GetNodeShort(XmlNode parentNode, string childName, short defaultValue = 0)
        {
            var value = GetNodeValue(parentNode, childName);
            if (value == null)
            {
                return defaultValue;
            }
            return short.TryParse(value, out short result) ? result : defaultValue;
        }

        /// <summary>
        /// 安全获取XML节点的double�?        /// </summary>
        public static double GetNodeDouble(XmlNode parentNode, string childName, double defaultValue = 0.0)
        {
            var value = GetNodeValue(parentNode, childName);
            if (value == null)
            {
                return defaultValue;
            }
            return double.TryParse(value, out double result) ? result : defaultValue;
        }

        /// <summary>
        /// 安全获取XML节点的bool�?        /// </summary>
        public static bool GetNodeBool(XmlNode parentNode, string childName, bool defaultValue = false)
        {
            var value = GetNodeValue(parentNode, childName);
            if (value == null)
            {
                return defaultValue;
            }
            int intVal;
            if (int.TryParse(value, out intVal))
            {
                return intVal != 0;
            }
            return bool.TryParse(value, out bool boolResult) ? boolResult : defaultValue;
        }

        /// <summary>
        /// 安全获取XML节点的整数数�?逗号分隔)
        /// </summary>
        public static int[] GetNodeIntArray(XmlNode parentNode, string childName)
        {
            var value = GetNodeValue(parentNode, childName);
            if (value == null)
            {
                return new int[0];
            }
            var parts = value.Split(new[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                result[i] = int.TryParse(parts[i], out int val) ? val : 0;
            }
            return result;
        }

        /// <summary>
        /// 安全获取XML节点的short数组(逗号分隔)
        /// </summary>
        public static short[] GetNodeShortArray(XmlNode parentNode, string childName)
        {
            var value = GetNodeValue(parentNode, childName);
            if (value == null)
            {
                return new short[0];
            }
            var parts = value.Split(new[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            var result = new short[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                result[i] = short.TryParse(parts[i], out short val) ? val : (short)0;
            }
            return result;
        }

        /// <summary>
        /// 安全获取XML节点的double数组(逗号分隔)
        /// </summary>
        public static double[] GetNodeDoubleArray(XmlNode parentNode, string childName)
        {
            var value = GetNodeValue(parentNode, childName);
            if (value == null)
            {
                return new double[0];
            }
            var parts = value.Split(new[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            var result = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                result[i] = double.TryParse(parts[i], out double val) ? val : 0.0;
            }
            return result;
        }
    }
}
