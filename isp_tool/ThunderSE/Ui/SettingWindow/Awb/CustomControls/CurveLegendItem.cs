using System.Windows.Media;
using ThunderSE.DeviceConfig.Isp;

namespace ThunderSE.Ui.SettingWindow.Awb.CustomControls
{
    /// <summary>
    /// 曲线图例项数据模型
    /// 用于绑定到图例面板的每个曲线项
    /// </summary>
    public class CurveLegendItem
    {
        /// <summary>曲线索引 (0-3)</summary>
        public int Index { get; set; }

        /// <summary>边界曲线类型</summary>
        public AwbBoundaryCurveDefinitions.BoundaryCurveType BoundaryType { get; set; }

        /// <summary>显示颜色（SolidBrush用于XAML绑定）</summary>
        public SolidColorBrush Color { get; set; }

        /// <summary>图标 + 短标签 (如 "⬆️ OUT↑")</summary>
        public string IconAndLabel { get; set; }

        /// <summary>中文名称</summary>
        public string ChineseName { get; set; }

        /// <summary>英文名称</summary>
        public string EnglishName { get; set; }

        /// <summary>功能描述</summary>
        public string Description { get; set; }

        /// <summary>内存位置标识</summary>
        public string MemoryLocation { get; set; }

        /// <summary>权重系数名称</summary>
        public string WeightCoefficient { get; set; }
    }
}
