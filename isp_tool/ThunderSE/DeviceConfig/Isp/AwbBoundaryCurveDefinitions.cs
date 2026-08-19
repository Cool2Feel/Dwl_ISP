using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ThunderSE.DeviceConfig.Isp
{
    /// <summary>
    /// AWB (自动白平衡) 模块 - 4条增益边界曲线的层级定义
    /// 
    /// 核心概念:
    /// AWB系统使用4条贝塞尔曲线(或样条插值曲线)来定义R通道增益(RGain)到B通道期望增益(BGain)的映射关系。
    /// 这4条曲线并非代表"不同光源"，而是定义了**白点检测的4个判定边界层**:
    /// 
    /// 层级结构 (从外到内):
    /// ┌─────────────────────────────────────────────┐
    //│  Curve[0]: OUT_HIGH - 外层上边界(最宽松)      │  ← 权重 = weight_out + 1
    //│  Curve[1]: IN_HIGH  - 内层上边界(较严格)       │  ← 权重 = weight_in + 1
    //│  ────────── 有效白点判定区域 ────────────     │
    //│  Curve[2]: IN_LOW   - 内层下边界(较严格)       │  ← 权重 = weight_in + 1
    //│  Curve[3]: OUT_LOW  - 外层下边界(最宽松)      │  ← 权重 = weight_out + 1
    /// └─────────────────────────────────────────────┘
    /// 
    /// 物理含义:
    /// 当统计某个像素的G值落在 [in_low, in_high] 范围内时，认为该像素是"有效白点"，
    /// 并赋予较高的权重参与最终的RGB增益计算。
    /// 落在 [out_low, out_high] 但不在 [in_low, in_high] 的像素给予较低权重。
    /// 完全超出 [out_low, out_high] 的像素被排除。
    /// 
    /// 内存映射:
    /// Curve[i] → awb_stat_tab[i*32 .. i*32+31]  (i=0,1,2,3)
    /// 总计: 4条曲线 × 32点/曲线 = 128字节
    /// 
    /// @author AI Assistant
    /// @version 1.0 (2026-04-27)
    /// </summary>
    public static class AwbBoundaryCurveDefinitions
    {
        #region ===== 枚举定义: 曲线边界类型 =====

        /// <summary>
        /// AWB增益边界曲线的类型枚举
        /// </summary>
        public enum BoundaryCurveType
        {
            /// <summary>外层上边界 (OUT_HIGH) - 最宽松的上限，用于初步筛选</summary>
            OutHighBoundary = 0,

            /// <summary>内层上边界 (IN_HIGH) - 严格的上限，用于精确白点判定</summary>
            InHighBoundary = 1,

            /// <summary>内层下边界 (IN_LOW) - 严格的下限，用于精确白点判定</summary>
            InLowBoundary = 2,

            /// <summary>外层下边界 (OUT_LOW) - 最宽松的下限，用于初步筛选</summary>
            OutLowBoundary = 3
        }

        #endregion

        #region ===== 常量定义: 曲线标识信息 =====

        /// <summary>
        /// 边界曲线总数（固定为4）
        /// </summary>
        public const int TOTAL_BOUNDARY_CURVES = 4;

        /// <summary>
        /// 每条曲线的采样点数（固定为32）
        /// </summary>
        public const int POINTS_PER_CURVE = 32;

        /// <summary>
        /// 总数据大小（字节数）
        /// </summary>
        public const int TOTAL_STAT_TAB_SIZE = 128; // 4 × 32

        #endregion

        #region ===== 标签定义: 专业命名与描述 =====

        /// <summary>
        /// 获取曲线类型的英文专业名称
        /// </summary>
        public static string GetEnglishName(BoundaryCurveType curveType)
        {
            switch (curveType)
            {
                case BoundaryCurveType.OutHighBoundary: return "Outer-High Boundary (Out-High)";
                case BoundaryCurveType.InHighBoundary: return "Inner-High Boundary (In-High)";
                case BoundaryCurveType.InLowBoundary: return "Inner-Low Boundary (In-Low)";
                case BoundaryCurveType.OutLowBoundary: return "Outer-Low Boundary (Out-Low)";
                default: return "Unknown Boundary";
            }
        }

        /// <summary>
        /// 获取曲线类型的中文名称（通俗易懂）
        /// </summary>
        public static string GetChineseName(BoundaryCurveType curveType)
        {
            switch (curveType)
            {
                case BoundaryCurveType.OutHighBoundary: return "外层上边界 (宽松上限)";
                case BoundaryCurveType.InHighBoundary: return "内层上边界 (严格上限)";
                case BoundaryCurveType.InLowBoundary: return "内层下边界 (严格下限)";
                case BoundaryCurveType.OutLowBoundary: return "外层下边界 (宽松下限)";
                default: return "未知边界";
            }
        }

        /// <summary>
        /// 获取曲线类型的简短标识符（用于UI紧凑显示）
        /// </summary>
        public static string GetShortLabel(BoundaryCurveType curveType)
        {
            switch (curveType)
            {
                case BoundaryCurveType.OutHighBoundary: return "OUT↑";
                case BoundaryCurveType.InHighBoundary: return "IN↑";
                case BoundaryCurveType.InLowBoundary: return "IN↓";
                case BoundaryCurveType.OutLowBoundary: return "OUT↓";
                default: return "??";
            }
        }

        /// <summary>
        /// 获取曲线类型的物理功能描述
        /// </summary>
        public static string GetDescription(BoundaryCurveType curveType)
        {
            switch (curveType)
            {
                case BoundaryCurveType.OutHighBoundary:
                    return "外层上边界: 定义BGain的最大允许值(宽松)。超出此值的G通道像素赋予低权重(weight_out+1)。用于排除极端高光或异常色彩区域。";
                    
                case BoundaryCurveType.InHighBoundary:
                    return "内层上边界: 定义BGain的理想上限(严格)。在此范围内的G通道像素被视为高质量白点，赋予高权重(weight_in+1)，主导最终增益计算。";
                    
                case BoundaryCurveType.InLowBoundary:
                    return "内层下边界: 定义BGain的理想下限(严格)。低于此值的G通道像素被视为欠饱和白点，赋予高权重(weight_in+1)，影响暗部校正。";
                    
                case BoundaryCurveType.OutLowBoundary:
                    return "外层下边界: 定义BGain的最小允许值(宽松)。低于此值的G通道像素赋予低权重(weight_out+1)。用于排除暗部噪声或死黑区域。";
                    
                default:
                    return "未定义的边界类型";
            }
        }

        /// <summary>
        /// 获取曲线在awb_stat_tab中的内存偏移量（起始索引）
        /// </summary>
        public static int GetStatTabOffset(BoundaryCurveType curveType)
        {
            return (int)curveType * POINTS_PER_CURVE;
            // OutHigh → 0, InHigh → 32, InLow → 64, OutLow → 96
        }

        /// <summary>
        /// 获取曲线在awb_stat_tab中的内存结束索引（不包含）
        /// </summary>
        public static int GetStatTabEndOffset(BoundaryCurveType curveType)
        {
            return ((int)curveType + 1) * POINTS_PER_CURVE;
            // OutHigh → 32, InHigh → 64, InLow → 96, OutLow → 128
        }

        #endregion

        #region ===== 视觉标识: 颜色方案 =====

        /// <summary>
        /// 获取曲线在图表中的推荐显示颜色（符合色觉心理学）
        /// 
        /// 配色原则:
        /// - 外层曲线(Curve 0,3): 使用冷色调(蓝/紫系)，表示"宽松/外围"
        /// - 内层曲线(Curve 1,2): 使用暖色调(红/橙系)，表示"严格/核心"
        /// - 符合"外松内紧"的直觉认知
        /// </summary>
        public static Color GetRecommendedColor(BoundaryCurveType curveType)
        {
            switch (curveType)
            {
                // 外层边界: 冷色调（蓝紫色系）- 表示宽松、外围、次要
                case BoundaryCurveType.OutHighBoundary:
                    return Color.FromRgb(65, 105, 225);  // Royal Blue 皇家蓝
                    // #4169E1
                
                case BoundaryCurveType.OutLowBoundary:
                    return Color.FromRgb(156, 39, 176);   // Purple 紫色
                    // #9C27B0
                    
                // 内层边界: 暖色调（红橙色系）- 表示严格、核心、主要
                case BoundaryCurveType.InHighBoundary:
                    return Color.FromRgb(220, 53, 69);    // Red 红色
                    // #DC3545
                    
                case BoundaryCurveType.InLowBoundary:
                    return Color.FromRgb(255, 152, 0);    // Orange 橙色
                    // #FF9800
                    
                default:
                    return Colors.Gray;
            }
        }

        /// <summary>
        /// 获取曲线的图标Unicode字符（用于UI标签前缀）
        /// </summary>
        public static string GetIconSymbol(BoundaryCurveType curveType)
        {
            switch (curveType)
            {
                case BoundaryCurveType.OutHighBoundary: return "⬆️";  // 外层向上箭头
                case BoundaryCurveType.InHighBoundary: return "⬆️";   // 内层向上箭头
                case BoundaryCurveType.InLowBoundary: return "⬇️";   // 内层向下箭头
                case BoundaryCurveType.OutLowBoundary: return "⬇️";  // 外层向下箭头
                default: return "?";
            }
        }

        /// <summary>
        /// 获取曲线的权重系数名称（对应AWB算法中的weight_out和weight_in）
        /// </summary>
        public static string GetWeightCoefficientName(BoundaryCurveType curveType)
        {
            switch (curveType)
            {
                case BoundaryCurveType.OutHighBoundary:
                case BoundaryCurveType.OutLowBoundary:
                    return "weight_out";  // 外层使用外部权重参数
                    
                case BoundaryCurveType.InHighBoundary:
                case BoundaryCurveType.InLowBoundary:
                    return "weight_in";   // 内层使用内部权重参数
                    
                default:
                    return "unknown_weight";
            }
        }

        #endregion

        #region ===== 层级关系验证 =====

        /// <summary>
        /// 检查两条曲线是否符合"外层比内层更宽松"的约束
        /// 
        /// 规则: OUT_HIGH ≥ IN_HIGH 且 OUT_LOW ≤ IN_LOW
        /// </summary>
        public static bool ValidateHierarchyOrder(
            BoundaryCurveType outerCurve, 
            BoundaryCurveType innerCurve,
            bool isUpperBoundary)
        {
            if (isUpperBoundary)
            {
                // 上边界: outer应该 >= inner（outer数值更大）
                return (int)outerCurve <= (int)innerCurve;
            }
            else
            {
                // 下边界: outer应该 <= inner（outer数值更小）
                return (int)outerCurve >= (int)innerCurve;
            }
        }

        /// <summary>
        /// 获取完整的层级结构说明文本（用于文档生成）
        /// </summary>
        public static string GetFullHierarchyDescription()
        {
            return @"
╔══════════════════════════════════════════════════════════════╗
║          AWB 增益边界曲线 4层架构 (4-Tier Boundary Architecture)         ║
╠══════════════════════════════════════════════════════════════╣
║                                                               ║
║  Layer 0 (最外层)                                              ║
║  ┌─────────────────────────────────────────────────────────┐   ║
║  │  Curve[0]: OUT_HIGH (外层上边界)                       │   ║
║  │  📍 内存位置: awb_stat_tab[0..31]                      │   ║
║  │  🎨 显示颜色: Royal Blue (#4169E1)                     │   ║
║  │  ⚖️ 宽松度: 最宽 (±20% vs 内层)                        │   ║
║  │  🔢 功能: 排除极端高光/异常色彩                         │   ║
║  │  🎯 权重: weight_out + 1 (最低优先级)                  │   ║
║  └─────────────────────────────────────────────────────────┘   ║
║                                                               ║
║  Layer 1 (内层-上)                                            ║
║  ┌─────────────────────────────────────────────────────────┐   ║
║  │  Curve[1]: IN_HIGH (内层上边界)                        │   ║
║  │  📍 内存位置: awb_stat_tab[32..63]                     │   ║
║  │  🎨 显示颜色: Red (#DC3545)                            │   ║
║  │  ⚖️ 宽松度: 较窄 (基准参考)                              │   ║
║  │  🔢 功能: 高质量白点的精确上界                          │   ║
║  │  🎯 权重: weight_in + 1 (高优先级，主导计算)           │   ║
║  └─────────────────────────────────────────────────────────┘   ║
║                                                               ║
║  ═════════════ 有效白点判定区域 (Valid White Point Zone) ════ ║
║  ══════════ 此区域内的像素被视为可靠的白点，参与增益计算 ════ ║
║                                                               ║
║  Layer 2 (内层-下)                                            ║
║  ┌─────────────────────────────────────────────────────────┐   ║
║  │  Curve[2]: IN_LOW (内层下边界)                         │   ║
║  │  │  📍 内存位置: awb_stat_tab[64..95]                   │   ║
║  │  │  🎨 显示颜色: Orange (#FF9800)                      │   ║
║  │  │  ⚖️ 宽松度: 较窄 (基准参考)                             │   ║
║  │  │  🔢 功能: 欠饱和白点的精确下限                         │   ║
║  │  │  🎯 权重: weight_in + 1 (高优先级，影响暗部校正)       │   ║
║  │  └─────────────────────────────────────────────────────────┘   ║
║                                                               ║
║  Layer 3 (最外层)                                              ║
║  ┌─────────────────────────────────────────────────────────┐   ║
║  │  Curve[3]: OUT_LOW (外层下边界)                       │   ║
║  │  📍 内存位置: awb_stat_tab[96..127]                     │   ║
║  │  🎨 显示颜色: Purple (#9C27B0)                       │   ║
║  │  ⚖️ 宽松度: 最宽 (±20% vs 内层)                        │   ║
║  │  🔢 功能: 排除暗部噪声/死黑区域                         │   ║
║  │  🎯 权重: weight_out + 1 (最低优先级)                  │   ║
║  └─────────────────────────────────────────────────────────┘   ║
║                                                               ║
╚══════════════════════════════════════════════════════════════╝
";
        }

        #endregion

        #region ===== 工厂方法: 创建带标注的曲线数据 =====

        // 用于存储集合元数据的ConditionalWeakTable
        private static readonly ConditionalWeakTable<ObservableCollection<KeyValuePair<double, double>>,
            Dictionary<string, object>> _boundaryTagStorage =
            new ConditionalWeakTable<ObservableCollection<KeyValuePair<double, double>>,
                Dictionary<string, object>>();

        /// <summary>
        /// 为ObservableCollection添加边界曲线类型元数据（扩展方法）
        /// 使用方式: statisticData.SetBoundaryTag(BoundaryCurveType.OutHighBoundary);
        /// </summary>
        public static void SetBoundaryTag(
            this ObservableCollection<KeyValuePair<double, double>> collection,
            BoundaryCurveType boundaryType)
        {
            // 通过AttachedProperties附加元数据（WPF标准做法）
            if (collection != null)
            {
                // 存储到Tag属性中（运行时可读取）
                //object currentTag = collection.Tag;
                
                var tagDict = new Dictionary<string, object>();

                tagDict["BoundaryType"] = boundaryType;
                tagDict["DisplayName"] = GetChineseName(boundaryType);
                tagDict["ShortLabel"] = GetShortLabel(boundaryType);
                tagDict["Description"] = GetDescription(boundaryType);
                tagDict["Color"] = GetRecommendedColor(boundaryType);
                tagDict["Icon"] = GetIconSymbol(boundaryType);
                tagDict["WeightName"] = GetWeightCoefficientName(boundaryType);
                tagDict["StatTabOffset"] = GetStatTabOffset(boundaryType);
                tagDict["StatTabEnd"] = GetStatTabEndOffset(boundaryType);

                //collection.Tag = tagDict;
                // 使用ConditionalWeakTable存储每个集合的元数据
                var collectionMetadata = _boundaryTagStorage.GetOrCreateValue(collection);
                collectionMetadata.Clear();

                foreach (var kvp in tagDict)
                {
                    collectionMetadata[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// 从集合中提取边界曲线类型（如果已设置）
        /// </summary>
        public static BoundaryCurveType? GetBoundaryTag(
            this System.Collections.IList collection)
        {
            //var dict = collection?.Tag as System.Collections.Generic.Dictionary<string, object>;

            //if (dict != null && dict.TryGetValue("BoundaryType", out var typeObj))
            //{
            //    return (BoundaryCurveType)typeObj;
            //}
            //return null;
            if (collection is ObservableCollection<KeyValuePair<double, double>> obsCollection &&
                _boundaryTagStorage.TryGetValue(obsCollection, out var metadata))
            {
                if (metadata.TryGetValue("BoundaryType", out var typeObj))
                {
                    return (BoundaryCurveType)typeObj;
                }
            }
            return null;
        }

        #endregion
    }
}
