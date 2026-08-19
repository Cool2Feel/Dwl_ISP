using System;
using System.Collections.Generic;

namespace ResBinManager.Models
{
    /// <summary>
    /// 字符串资源ID定义（统一所有项目）
    /// </summary>
    public static class StringResourceIds
    {
        // 语言类 (0x0100-0x011F)
        public const uint R_ID_STR_LAN_ENGLISH = 0x0100;
        public const uint R_ID_STR_LAN_SCHINESE = 0x0101;
        public const uint R_ID_STR_LAN_TCHINESE = 0x0102;
        public const uint R_ID_STR_LAN_JAPANESE = 0x0103;
        public const uint R_ID_STR_LAN_GERMAN = 0x0104;
        public const uint R_ID_STR_LAN_FRECH = 0x0105;
        public const uint R_ID_STR_LAN_RUSSIAN = 0x0106;
        public const uint R_ID_STR_LAN_ITALIAN = 0x0107;
        public const uint R_ID_STR_LAN_KOERA = 0x0108;
        public const uint R_ID_STR_LAN_TAI = 0x0109;
        public const uint R_ID_STR_LAN_HEBREW = 0x000A;
        public const uint R_ID_STR_LAN_DUTCH = 0x010B;
        public const uint R_ID_STR_LAN_UKRAINIAN = 0x010C;
        public const uint R_ID_STR_LAN_SPANISH = 0x010D;
        public const uint R_ID_STR_LAN_PORTUGUESE = 0x010E;
        public const uint R_ID_STR_LAN_POLISH = 0x010F;
        public const uint R_ID_STR_LAN_CZECH = 0x0110;
        public const uint R_ID_STR_LAN_TURKEY = 0x0111;
        public const uint R_ID_STR_LAN_ROMANIAN = 0x00DA;  // DC508J/GX-T317BV200

        // 开关类 (0x0114-0x0115)
        public const uint R_ID_STR_COM_OFF = 0x0114;
        public const uint R_ID_STR_COM_ON = 0x0115;
        public const uint R_ID_STR_COM_OK = 0x0116;
        public const uint R_ID_STR_COM_CANCEL = 0x0117;
        public const uint R_ID_STR_COM_YES = 0x0118;
        public const uint R_ID_STR_COM_NO = 0x0119;

        // 灵敏度类 (0x011A-0x011C)
        public const uint R_ID_STR_COM_LOW = 0x011A;
        public const uint R_ID_STR_COM_MIDDLE = 0x011B;
        public const uint R_ID_STR_COM_HIGH = 0x011C;

        // 频率类 (0x011D-0x011E)
        public const uint R_ID_STR_COM_50HZ = 0x011D;
        public const uint R_ID_STR_COM_60HZ = 0x011E;

        // 级别类 (0x011F-0x0128)
        public const uint R_ID_STR_COM_LEVEL_0 = 0x011F;
        public const uint R_ID_STR_COM_LEVEL_1 = 0x0120;
        public const uint R_ID_STR_COM_LEVEL_2 = 0x0121;
        public const uint R_ID_STR_COM_LEVEL_3 = 0x0122;
        public const uint R_ID_STR_COM_LEVEL_4 = 0x0123;
        public const uint R_ID_STR_COM_LEVEL_5 = 0x0124;
        public const uint R_ID_STR_COM_LEVEL_6 = 0x0125;
        public const uint R_ID_STR_COM_LEVEL_7 = 0x0126;
        public const uint R_ID_STR_COM_LEVEL_8 = 0x0127;
        public const uint R_ID_STR_COM_LEVEL_9 = 0x0128;

        // 曝光补偿类 (0x0128-0x0133)
        public const uint R_ID_STR_COM_P4_0 = 0x0129;
        public const uint R_ID_STR_COM_P3_0 = 0x012A;
        public const uint R_ID_STR_COM_P2_0 = 0x012B;
        public const uint R_ID_STR_COM_P1_0 = 0x012C;
        public const uint R_ID_STR_COM_P0_0 = 0x012D;
        public const uint R_ID_STR_COM_N1_0 = 0x012E;
        public const uint R_ID_STR_COM_N2_0 = 0x012F;

        // 时间类 (0x0134-0x0138)
        public const uint R_ID_STR_TIM_1MIN = 0x0134;
        public const uint R_ID_STR_TIM_2MIN = 0x0135;
        public const uint R_ID_STR_TIM_3MIN = 0x0136;
        public const uint R_ID_STR_TIM_5MIN = 0x0137;
        public const uint R_ID_STR_TIM_10MIN = 0x0138;

        // 视频分辨率类 (0x0180-0x018F)
        public const uint R_ID_STR_RES_240P = 0x0180;
        public const uint R_ID_STR_RES_480P = 0x0181;
        public const uint R_ID_STR_RES_480FHD = 0x0182;
        public const uint R_ID_STR_RES_720P = 0x0183;
        public const uint R_ID_STR_RES_1024P = 0x0184;
        public const uint R_ID_STR_RES_1080P = 0x0185;
        public const uint R_ID_STR_RES_1080FHD = 0x0186;
        public const uint R_ID_STR_RES_1440P = 0x0187;
        public const uint R_ID_STR_RES_2160P = 0x0188;   // JT529X/DC508J/GX-T317BV200
        public const uint R_ID_STR_RES_3024P = 0x0189;
        public const uint R_ID_STR_RES_720P_SHORT = 0x018A;
        public const uint R_ID_STR_RES_1080P_SHORT = 0x018B;
        public const uint R_ID_STR_RES_1440P_SHORT = 0x018C;  // JT529X/DC508J/GX-T317BV200
        public const uint R_ID_STR_RES_2160P_SHORT = 0x018D;  // JT529X/DC508J/GX-T317BV200

        // 拍照分辨率类 (0x0190-0x01A0)
        public const uint R_ID_STR_RES_100M = 0x0192;   // JT529X/DC508J/GX-T317BV200
        public const uint R_ID_STR_RES_56M = 0x0193;    // JT529X/DC508J/GX-T317BV200
        public const uint R_ID_STR_RES_48M = 0x0194;
        public const uint R_ID_STR_RES_40M = 0x0195;
        public const uint R_ID_STR_RES_24M = 0x0196;
        public const uint R_ID_STR_RES_20M = 0x0197;
        public const uint R_ID_STR_RES_18M = 0x0198;
        public const uint R_ID_STR_RES_16M = 0x0199;
        public const uint R_ID_STR_RES_12M = 0x019A;
        public const uint R_ID_STR_RES_10M = 0x019B;
        public const uint R_ID_STR_RES_8M = 0x019C;
        public const uint R_ID_STR_RES_5M = 0x019D;
        public const uint R_ID_STR_RES_3M = 0x019E;
        public const uint R_ID_STR_RES_2M = 0x019F;
        public const uint R_ID_STR_RES_1M = 0x01A0;
        public const uint R_ID_STR_RES_4M = 0x00D9;     // GX-T317BV200
    }

    /// <summary>
    /// 项目配置特征定义
    /// </summary>
    public class ProjectConfigFeatures
    {
        public ProjectType ProjectType { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        // 字符串资源特征
        public bool Has2160P { get; set; }                    // 是否支持2160P
        public bool Has2160PShort { get; set; }               // 是否支持2160P_SHORT
        public bool Has1440PShort { get; set; }               // 是否支持1440P_SHORT
        public bool Has100M { get; set; }                     // 是否支持100M拍照
        public bool Has56M { get; set; }                      // 是否支持56M拍照
        public bool Has4M { get; set; }                       // 是否支持4M拍照（GX-T317BV200独有）
        public bool HasRomanian { get; set; }                 // 是否支持罗马尼亚语
        public bool HasPhotoPuzzle { get; set; }              // 是否支持拼图功能
        public bool HasPhotoEdit { get; set; }                // 是否支持照片编辑
        public bool HasVideoStandard { get; set; }            // 是否支持视频标准
        public bool HasVideoFrame { get; set; }               // 是否支持视频帧
        public bool HasPhotoDetection { get; set; }           // 是否支持照片检测
        public bool HasPhotoScore { get; set; }               // 是否支持照片评分
        public bool HasTipsBatFull { get; set; }              // 是否有电池满提示
        public bool HasVideoRecSpeed { get; set; }            // 是否有录像速度选项
        public bool HasTipsIntSdc { get; set; }               // 是否有内部SD卡提示（HM020F独有）
        public bool HasTipsSdcMov { get; set; }               // 是否有SD卡移动提示（HM020F独有）
        public bool HasWaiting { get; set; }                  // 是否有等待提示（MKL_DM15/JRX_AX329X独有）
        public bool HasUsbPhoto { get; set; }                 // 是否有USB拍照（MKL_DM15独有）
        public bool HasAssist { get; set; }                   // 是否有辅助功能（MKL_DM15独有）
        // 语言数量
        public int LanguageCount { get; set; }

        // 字符串资源总数
        public int StringResourceCount { get; set; }

        // 配置项总数
        public int ConfigItemCount { get; set; }
    }

    /// <summary>
    /// 项目配置特征数据库
    /// </summary>
    public static class ProjectFeatureDatabase
    {
        private static Dictionary<ProjectType, ProjectConfigFeatures> _features;

        public static Dictionary<ProjectType, ProjectConfigFeatures> GetAllFeatures()
        {
            if (_features == null)
            {
                InitializeFeatures();
            }
            return _features!;
        }

        public static ProjectConfigFeatures GetFeatures(ProjectType projectType)
        {
            if (_features == null)
            {
                InitializeFeatures();
            }
            return _features![projectType];
        }

        private static void InitializeFeatures()
        {
            _features = new Dictionary<ProjectType, ProjectConfigFeatures>();

            // JT529X项目特征
            _features[ProjectType.JT529X] = new ProjectConfigFeatures
            {
                ProjectType = ProjectType.JT529X,
                ProjectName = "JT529X",
                Description = "JT529X项目配置",
                Has2160P = true,
                Has2160PShort = true,
                Has1440PShort = true,
                Has100M = true,
                Has56M = true,
                Has4M = false,
                HasRomanian = false,
                HasPhotoPuzzle = true,
                HasPhotoEdit = true,
                HasVideoStandard = true,
                HasVideoFrame = true,
                HasPhotoDetection = true,
                HasPhotoScore = true,
                HasTipsBatFull = true,
                HasVideoRecSpeed = false,
                HasTipsIntSdc = false,
                HasTipsSdcMov = false,
                LanguageCount = 16,
                StringResourceCount = 274,
                ConfigItemCount = 274
            };

            // DC508J项目特征
            _features[ProjectType.DC508J] = new ProjectConfigFeatures
            {
                ProjectType = ProjectType.DC508J,
                ProjectName = "DC508J",
                Description = "DC508J项目配置",
                Has2160P = true,
                Has2160PShort = true,
                Has1440PShort = true,
                Has100M = true,
                Has56M = true,
                Has4M = false,
                HasRomanian = true,
                HasPhotoPuzzle = true,
                HasPhotoEdit = true,
                HasVideoStandard = true,
                HasVideoFrame = true,
                HasPhotoDetection = true,
                HasPhotoScore = true,
                HasTipsBatFull = true,
                HasVideoRecSpeed = true,
                HasTipsIntSdc = false,
                HasTipsSdcMov = false,
                LanguageCount = 17,
                StringResourceCount = 284,
                ConfigItemCount = 284
            };

            // GX-T317BV200项目特征
            _features[ProjectType.GX_T317BV200] = new ProjectConfigFeatures
            {
                ProjectType = ProjectType.GX_T317BV200,
                ProjectName = "GX-T317BV200",
                Description = "GX-T317BV200项目配置",
                Has2160P = false,
                Has2160PShort = false,
                Has1440PShort = false,
                Has100M = false,
                Has56M = false,
                Has4M = false,
                HasRomanian = false,
                HasPhotoPuzzle = false,
                HasPhotoEdit = false,
                HasVideoStandard = false,
                HasVideoFrame = false,
                HasPhotoDetection = false,
                HasPhotoScore = false,
                HasTipsBatFull = false,
                HasVideoRecSpeed = false,
                HasTipsIntSdc = false,
                HasTipsSdcMov = false,
                LanguageCount = 16,
                StringResourceCount = 268,
                ConfigItemCount = 268
            };

            // HM020F项目特征
            _features[ProjectType.HM020F] = new ProjectConfigFeatures
            {
                ProjectType = ProjectType.HM020F,
                ProjectName = "HM020F",
                Description = "HM020F项目配置",
                Has2160P = false,
                Has2160PShort = false,
                Has1440PShort = false,
                Has100M = false,
                Has56M = false,
                Has4M = false,
                HasRomanian = false,
                HasPhotoPuzzle = false,
                HasPhotoEdit = false,
                HasVideoStandard = false,
                HasVideoFrame = false,
                HasPhotoDetection = false,
                HasPhotoScore = false,
                HasTipsBatFull = false,
                HasVideoRecSpeed = false,
                HasTipsIntSdc = true,
                HasTipsSdcMov = true,
                LanguageCount = 15,
                StringResourceCount = 271,
                ConfigItemCount = 271
            };

            // MKL_CM5项目特征
            _features[ProjectType.MKL_CM5] = new ProjectConfigFeatures
            {
                ProjectType = ProjectType.MKL_CM5,
                ProjectName = "MKL_CM5",
                Description = "MKL_CM5项目配置",
                Has2160P = false,
                Has2160PShort = false,
                Has1440PShort = false,
                Has100M = false,
                Has56M = false,
                Has4M = false,
                HasRomanian = false,
                HasPhotoPuzzle = false,
                HasPhotoEdit = false,
                HasVideoStandard = false,
                HasVideoFrame = false,
                HasPhotoDetection = false,
                HasPhotoScore = false,
                HasTipsBatFull = false,
                HasVideoRecSpeed = false,
                HasTipsIntSdc = false,
                HasTipsSdcMov = false,
                LanguageCount = 15,
                StringResourceCount = 265,
                ConfigItemCount = 265
            };

            // MKL_DM15项目特征
            _features[ProjectType.MKL_DM15] = new ProjectConfigFeatures
            {
                ProjectType = ProjectType.MKL_DM15,
                ProjectName = "MKL_DM15",
                Description = "MKL_DM15项目配置（基于MKL-DM15_SVN2032）",
                Has2160P = false,
                Has2160PShort = false,
                Has1440PShort = false,
                Has100M = false,
                Has56M = false,
                Has4M = false,
                HasRomanian = false,
                HasPhotoPuzzle = false,
                HasPhotoEdit = false,
                HasVideoStandard = false,
                HasVideoFrame = false,
                HasPhotoDetection = false,
                HasPhotoScore = false,
                HasTipsBatFull = false,
                HasVideoRecSpeed = false,
                HasTipsIntSdc = false,
                HasTipsSdcMov = false,
                HasWaiting = true,
                HasUsbPhoto = true,
                HasAssist = true,
                LanguageCount = 13,
                StringResourceCount = 203,
                ConfigItemCount = 203
            };

            // JRX_SDK JT529X项目特征
            _features[ProjectType.JRX_JT529X] = new ProjectConfigFeatures
            {
                ProjectType = ProjectType.JRX_JT529X,
                ProjectName = "JRX_JT529X",
                Description = "JRX_SDK JT529X项目配置",
                Has2160P = true,
                Has2160PShort = true,
                Has1440PShort = true,
                Has100M = true,
                Has56M = true,
                Has4M = false,
                HasRomanian = false,
                HasPhotoPuzzle = true,
                HasPhotoEdit = true,
                HasVideoStandard = true,
                HasVideoFrame = true,
                HasPhotoDetection = true,
                HasPhotoScore = true,
                HasTipsBatFull = true,
                HasVideoRecSpeed = true,
                HasTipsIntSdc = false,
                HasTipsSdcMov = false,
                LanguageCount = 15,
                StringResourceCount = 274,
                ConfigItemCount = 274
            };

            // JRX_SDK AX329X项目特征
            _features[ProjectType.JRX_AX329X] = new ProjectConfigFeatures
            {
                ProjectType = ProjectType.JRX_AX329X,
                ProjectName = "JRX_AX329X",
                Description = "JRX_SDK AX329X项目配置",
                Has2160P = false,
                Has2160PShort = false,
                Has1440PShort = false,
                Has100M = false,
                Has56M = false,
                Has4M = false,
                HasRomanian = false,
                HasPhotoPuzzle = false,
                HasPhotoEdit = false,
                HasVideoStandard = false,
                HasVideoFrame = false,
                HasPhotoDetection = false,
                HasPhotoScore = false,
                HasTipsBatFull = false,
                HasVideoRecSpeed = false,
                HasTipsIntSdc = false,
                HasTipsSdcMov = false,
                HasWaiting = true,
                LanguageCount = 18,
                StringResourceCount = 199,
                ConfigItemCount = 199
            };
        }
    }
}
