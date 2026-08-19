using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Ui.SettingWindow.Awb.CustomControls;

namespace ThunderSE.Ui.SettingWindow.Awb
{
    /// <summary>
    /// CurveLegendPanel.xaml 的交互逻辑
    /// AWB增益边界曲线图例面板 - 显示4条曲线的专业标注信息
    /// </summary>
    public partial class CurveLegendPanel : UserControl
    {
        public CurveLegendPanel()
        {
            InitializeComponent();
            Loaded += CurveLegendPanel_Loaded;
        }

        private void CurveLegendPanel_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeCurveLegends();
        }

        /// <summary>
        /// 初始化4条边界曲线的图例数据
        /// 确保每条曲线与标签的一一对应关系清晰可见
        /// </summary>
        private void InitializeCurveLegends()
        {
            var legendItems = new List<CurveLegendItem>();

            // 按照层级顺序（从外到内，从上到下）添加4条曲线
            var curveTypes = new[]
            {
                AwbBoundaryCurveDefinitions.BoundaryCurveType.OutHighBoundary,
                AwbBoundaryCurveDefinitions.BoundaryCurveType.InHighBoundary,
                AwbBoundaryCurveDefinitions.BoundaryCurveType.InLowBoundary,
                AwbBoundaryCurveDefinitions.BoundaryCurveType.OutLowBoundary
            };

            foreach (var curveType in curveTypes)
            {
                legendItems.Add(new CurveLegendItem
                {
                    Index = (int)curveType,
                    BoundaryType = curveType,
                    Color = new SolidColorBrush(AwbBoundaryCurveDefinitions.GetRecommendedColor(curveType)),
                    IconAndLabel = $"{AwbBoundaryCurveDefinitions.GetIconSymbol(curveType)} {AwbBoundaryCurveDefinitions.GetShortLabel(curveType)}",
                    ChineseName = AwbBoundaryCurveDefinitions.GetChineseName(curveType),
                    EnglishName = AwbBoundaryCurveDefinitions.GetEnglishName(curveType),
                    Description = AwbBoundaryCurveDefinitions.GetDescription(curveType),
                    MemoryLocation = $"stat_tab[{AwbBoundaryCurveDefinitions.GetStatTabOffset(curveType)}..{AwbBoundaryCurveDefinitions.GetStatTabEndOffset(curveType) - 1}]",
                    WeightCoefficient = AwbBoundaryCurveDefinitions.GetWeightCoefficientName(curveType)
                });
            }

            CurveLegendList.ItemsSource = legendItems;
        }
    }

}
