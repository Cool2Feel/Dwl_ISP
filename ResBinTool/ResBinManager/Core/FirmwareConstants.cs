using System.Collections.Generic;

namespace ResBinManager.Core
{
    /// <summary>
    /// 固件字符串常量定义
    /// 基于 max.h: #define R_ID_TYPE_STR 0x81000000
    /// 所有 R_ID_STR_* 枚举值 = R_ID_TYPE_STR + 枚举偏移
    /// 
    /// 这些值在所有项目中统一一致，不同项目仅 configName 可能不同，
    /// 有的项目有某个配置项，有的没有。
    /// </summary>
    public static class FirmwareConstants
    {
        /// <summary>
        /// R_ID_TYPE_STR 基址常量 (来自 max.h)
        /// </summary>
        public const uint R_ID_TYPE_STR = 0x81000000;

        // ============================================================
        // 语言选项值 (R_ID_STR_LAN_ENGLISH + offset)
        // 对应 user_str.h 中 R_ID_STR_LAN_* 枚举
        // ============================================================
        public const uint R_STR_LAN_ENGLISH    = R_ID_TYPE_STR + 0x00; // 0x81000000
        public const uint R_STR_LAN_SCHINESE   = R_ID_TYPE_STR + 0x01; // 0x81000001
        public const uint R_STR_LAN_TCHINESE   = R_ID_TYPE_STR + 0x02; // 0x81000002
        public const uint R_STR_LAN_JAPANESE   = R_ID_TYPE_STR + 0x03; // 0x81000003
        public const uint R_STR_LAN_GERMAN     = R_ID_TYPE_STR + 0x04; // 0x81000004
        public const uint R_STR_LAN_FRECH      = R_ID_TYPE_STR + 0x05; // 0x81000005
        public const uint R_STR_LAN_RUSSIAN    = R_ID_TYPE_STR + 0x06; // 0x81000006
        public const uint R_STR_LAN_ITALIAN    = R_ID_TYPE_STR + 0x07; // 0x81000007
        public const uint R_STR_LAN_KOERA      = R_ID_TYPE_STR + 0x08; // 0x81000008
        public const uint R_STR_LAN_TAI        = R_ID_TYPE_STR + 0x09; // 0x81000009
        public const uint R_STR_LAN_HEBREW     = R_ID_TYPE_STR + 0x0A; // 0x8100000A
        public const uint R_STR_LAN_DUTCH      = R_ID_TYPE_STR + 0x0B; // 0x8100000B
        public const uint R_STR_LAN_UKRAINIAN  = R_ID_TYPE_STR + 0x0C; // 0x8100000C
        public const uint R_STR_LAN_SPANISH    = R_ID_TYPE_STR + 0x0D; // 0x8100000D
        public const uint R_STR_LAN_PORTUGUESE = R_ID_TYPE_STR + 0x0E; // 0x8100000E
        public const uint R_STR_LAN_POLISH     = R_ID_TYPE_STR + 0x0F; // 0x8100000F
        public const uint R_STR_LAN_CZECH      = R_ID_TYPE_STR + 0x10; // 0x81000010
        public const uint R_STR_LAN_TURKEY     = R_ID_TYPE_STR + 0x11; // 0x81000011
        public const uint R_STR_LAN_ROMANIAN   = R_ID_TYPE_STR + 0xDA; // 0x810000DA (DC508J/GX-T317BV200)

        // ============================================================
        // 通用开关/选项值
        // 对应 user_str.h 中 R_ID_STR_COM_* 枚举
        // ============================================================
        public const uint R_STR_COM_OFF        = R_ID_TYPE_STR + 0x14; // 0x81000014
        public const uint R_STR_COM_ON         = R_ID_TYPE_STR + 0x15; // 0x81000015
        public const uint R_STR_COM_OK         = R_ID_TYPE_STR + 0x16; // 0x81000016
        public const uint R_STR_COM_CANCEL     = R_ID_TYPE_STR + 0x17; // 0x81000017
        public const uint R_STR_COM_YES        = R_ID_TYPE_STR + 0x18; // 0x81000018
        public const uint R_STR_COM_NO         = R_ID_TYPE_STR + 0x19; // 0x81000019
        public const uint R_STR_COM_LOW        = R_ID_TYPE_STR + 0x1A; // 0x8100001A
        public const uint R_STR_COM_MIDDLE     = R_ID_TYPE_STR + 0x1B; // 0x8100001B
        public const uint R_STR_COM_HIGH       = R_ID_TYPE_STR + 0x1C; // 0x8100001C
        public const uint R_STR_COM_50HZ       = R_ID_TYPE_STR + 0x1D; // 0x8100001D
        public const uint R_STR_COM_60HZ       = R_ID_TYPE_STR + 0x1E; // 0x8100001E

        // ============================================================
        // 等级值 LEVEL_0 ~ LEVEL_9
        // ============================================================
        public const uint R_STR_COM_LEVEL_0    = R_ID_TYPE_STR + 0x1F; // 0x8100001F
        public const uint R_STR_COM_LEVEL_1    = R_ID_TYPE_STR + 0x20; // 0x81000020
        public const uint R_STR_COM_LEVEL_2    = R_ID_TYPE_STR + 0x21; // 0x81000021
        public const uint R_STR_COM_LEVEL_3    = R_ID_TYPE_STR + 0x22; // 0x81000022
        public const uint R_STR_COM_LEVEL_4    = R_ID_TYPE_STR + 0x23; // 0x81000023
        public const uint R_STR_COM_LEVEL_5    = R_ID_TYPE_STR + 0x24; // 0x81000024
        public const uint R_STR_COM_LEVEL_6    = R_ID_TYPE_STR + 0x25; // 0x81000025
        public const uint R_STR_COM_LEVEL_7    = R_ID_TYPE_STR + 0x26; // 0x81000026
        public const uint R_STR_COM_LEVEL_8    = R_ID_TYPE_STR + 0x27; // 0x81000027
        public const uint R_STR_COM_LEVEL_9    = R_ID_TYPE_STR + 0x28; // 0x81000028

        // ============================================================
        // 曝光补偿值 (与HM020F user_str.c一致)
        // ============================================================
        public const uint R_STR_COM_P4_0       = R_ID_TYPE_STR + 0x29; // 0x81000029
        public const uint R_STR_COM_P3_0       = R_ID_TYPE_STR + 0x2A; // 0x8100002A
        public const uint R_STR_COM_P2_0       = R_ID_TYPE_STR + 0x2B; // 0x8100002B
        public const uint R_STR_COM_P1_0       = R_ID_TYPE_STR + 0x2C; // 0x8100002C
        public const uint R_STR_COM_P0_0       = R_ID_TYPE_STR + 0x2D; // 0x8100002D
        public const uint R_STR_COM_N1_0       = R_ID_TYPE_STR + 0x2E; // 0x8100002E
        public const uint R_STR_COM_N2_0       = R_ID_TYPE_STR + 0x2F; // 0x8100002F
        public const uint R_STR_COM_N3_0       = R_ID_TYPE_STR + 0x30; // 0x81000030

        // ============================================================
        // 附加选项值 (与HM020F user_str.c一致)
        // ============================================================
        public const uint R_STR_COM_ALWAYSON   = R_ID_TYPE_STR + 0x30; // 0x81000030 (常亮)
        public const uint R_STR_COM_ECONOMIC   = R_ID_TYPE_STR + 0x31; // 0x81000031 (经济)
        public const uint R_STR_COM_NORMAL     = R_ID_TYPE_STR + 0x32; // 0x81000032 (标准)
        public const uint R_STR_COM_FINE       = R_ID_TYPE_STR + 0x33; // 0x81000033 (精细)

        // ============================================================
        // 时间值 (与HM020F user_str.c一致)
        // ============================================================
        public const uint R_STR_TIM_1MIN       = R_ID_TYPE_STR + 0x34; // 0x81000034
        public const uint R_STR_TIM_2MIN       = R_ID_TYPE_STR + 0x35; // 0x81000035
        public const uint R_STR_TIM_3MIN       = R_ID_TYPE_STR + 0x36; // 0x81000036
        public const uint R_STR_TIM_5MIN       = R_ID_TYPE_STR + 0x37; // 0x81000037
        public const uint R_STR_TIM_10MIN      = R_ID_TYPE_STR + 0x38; // 0x81000038
        public const uint R_STR_TIM_2SEC       = R_ID_TYPE_STR + 0x39; // 0x81000039
        public const uint R_STR_TIM_3SEC       = R_ID_TYPE_STR + 0x3A; // 0x8100003A
        public const uint R_STR_TIM_5SEC       = R_ID_TYPE_STR + 0x3B; // 0x8100003B
        public const uint R_STR_TIM_10SEC      = R_ID_TYPE_STR + 0x3C; // 0x8100003C
        public const uint R_STR_TIM_30SEC      = R_ID_TYPE_STR + 0x3D; // 0x8100003D

        // ============================================================
        // 拍照张数值 (与HM020F user_str.c一致)
        // ============================================================
        public const uint R_STR_PHOTO_NUM_3    = R_ID_TYPE_STR + 0x3E; // 0x8100003E (3张)
        public const uint R_STR_PHOTO_NUM_5    = R_ID_TYPE_STR + 0x3F; // 0x8100003F (5张)

        // ============================================================
        // 打印机选项值 (503项目特有)
        // ============================================================
        public const uint R_STR_SET_PRINT_DENSITY = R_ID_TYPE_STR + 0x47; // 0x81000047
        public const uint R_STR_SET_PRINT_MODE    = R_ID_TYPE_STR + 0x48; // 0x81000048
        public const uint R_STR_SET_PRINT_GRAY    = R_ID_TYPE_STR + 0x54; // 0x81000054 (灰度打印)
        public const uint R_STR_SET_PRINT_DOT     = R_ID_TYPE_STR + 0x55; // 0x81000055 (点阵打印)

        // ============================================================
        // 提示信息值 (503项目特有)
        // ============================================================
        public const uint R_STR_TIP_NEAR         = R_ID_TYPE_STR + 0xC5; // 0x810000C5 (近)
        public const uint R_STR_TIP_MIDDLE       = R_ID_TYPE_STR + 0xC6; // 0x810000C6 (中)
        public const uint R_STR_TIP_FAR          = R_ID_TYPE_STR + 0xC7; // 0x810000C7 (远)

        // ============================================================
        // 视频分辨率值 (与HM020F user_str.c一致)
        // ============================================================
        public const uint R_STR_RES_240P        = R_ID_TYPE_STR + 0x80; // 0x81000080
        public const uint R_STR_RES_480P        = R_ID_TYPE_STR + 0x81; // 0x81000081
        public const uint R_STR_RES_480FHD      = R_ID_TYPE_STR + 0x82; // 0x81000082
        public const uint R_STR_RES_720P        = R_ID_TYPE_STR + 0x83; // 0x81000083
        public const uint R_STR_RES_1024P       = R_ID_TYPE_STR + 0x84; // 0x81000084
        public const uint R_STR_RES_1080P       = R_ID_TYPE_STR + 0x85; // 0x81000085
        public const uint R_STR_RES_1080FHD     = R_ID_TYPE_STR + 0x86; // 0x81000086
        public const uint R_STR_RES_720HD       = R_ID_TYPE_STR + 0x66; // 0x81000066 (MKL-DM15)
        public const uint R_STR_RES_1440P       = R_ID_TYPE_STR + 0x87; // 0x81000087
        public const uint R_STR_RES_2160P       = R_ID_TYPE_STR + 0x88; // 0x81000088
        public const uint R_STR_RES_3024P       = R_ID_TYPE_STR + 0x89; // 0x81000089
        public const uint R_STR_RES_720P_SHORT  = R_ID_TYPE_STR + 0x8A; // 0x8100008A
        public const uint R_STR_RES_1080P_SHORT = R_ID_TYPE_STR + 0x8B; // 0x8100008B
        public const uint R_STR_RES_1440P_SHORT = R_ID_TYPE_STR + 0x8C; // 0x8100008C (JT529X/DC508J/GX-T317BV200)
        public const uint R_STR_RES_2160P_SHORT = R_ID_TYPE_STR + 0x8D; // 0x8100008D

        // ============================================================
        // 拍照分辨率值 (与HM020F user_str.c一致)
        // ============================================================
        public const uint R_STR_RES_QVGA        = R_ID_TYPE_STR + 0x8C; // 0x8100008C
        public const uint R_STR_RES_VGA         = R_ID_TYPE_STR + 0x8D; // 0x8100008D
        public const uint R_STR_RES_HD          = R_ID_TYPE_STR + 0x8E; // 0x8100008E
        public const uint R_STR_RES_FHD         = R_ID_TYPE_STR + 0x8F; // 0x8100008F
        public const uint R_STR_RES_48M         = R_ID_TYPE_STR + 0x90; // 0x81000090
        public const uint R_STR_RES_40M         = R_ID_TYPE_STR + 0x91; // 0x81000091
        public const uint R_STR_RES_24M         = R_ID_TYPE_STR + 0x92; // 0x81000092
        public const uint R_STR_RES_20M         = R_ID_TYPE_STR + 0x93; // 0x81000093
        public const uint R_STR_RES_18M         = R_ID_TYPE_STR + 0x94; // 0x81000094
        public const uint R_STR_RES_16M         = R_ID_TYPE_STR + 0x95; // 0x81000095
        public const uint R_STR_RES_12M         = R_ID_TYPE_STR + 0x96; // 0x81000096
        public const uint R_STR_RES_10M         = R_ID_TYPE_STR + 0x97; // 0x81000097
        public const uint R_STR_RES_8M          = R_ID_TYPE_STR + 0x98; // 0x81000098
        public const uint R_STR_RES_5M          = R_ID_TYPE_STR + 0x99; // 0x81000099
        public const uint R_STR_RES_4M          = R_ID_TYPE_STR + 0xD9; // 0x810000D9 (GX-T317BV200)
        public const uint R_STR_RES_3M          = R_ID_TYPE_STR + 0x9A; // 0x8100009A
        public const uint R_STR_RES_2M          = R_ID_TYPE_STR + 0x9B; // 0x8100009B
        public const uint R_STR_RES_1M          = R_ID_TYPE_STR + 0x9C; // 0x8100009C

        // ============================================================
        // ISP/白平衡值 (与HM020F user_str.c一致)
        // ============================================================
        public const uint R_STR_ISP_WHITEBL     = R_ID_TYPE_STR + 0xA7; // 0x810000A7 (白炽灯)
        public const uint R_STR_ISP_ISO         = R_ID_TYPE_STR + 0xA8; // 0x810000A8
        public const uint R_STR_ISP_ANTISHANK   = R_ID_TYPE_STR + 0xA9; // 0x810000A9
        public const uint R_STR_ISP_AUTO        = R_ID_TYPE_STR + 0xAA; // 0x810000AA (自动)
        public const uint R_STR_ISP_SOFT        = R_ID_TYPE_STR + 0xAB; // 0x810000AB
        public const uint R_STR_ISP_STRONG      = R_ID_TYPE_STR + 0xAC; // 0x810000AC
        public const uint R_STR_ISP_SUNLIGHT    = R_ID_TYPE_STR + 0xAD; // 0x810000AD (晴天)
        public const uint R_STR_ISP_CLOUDY      = R_ID_TYPE_STR + 0xAE; // 0x810000AE (阴天)
        public const uint R_STR_ISP_TUNGSTEN    = R_ID_TYPE_STR + 0xAF; // 0x810000AF (办公室/钨丝灯)
        public const uint R_STR_ISP_FLUORESCENT = R_ID_TYPE_STR + 0xB0; // 0x810000B0 (荧光灯)
        public const uint R_STR_ISP_BLACKWHITE  = R_ID_TYPE_STR + 0xB1; // 0x810000B1 (黑白)
        public const uint R_STR_ISP_SEPIA       = R_ID_TYPE_STR + 0xB2; // 0x810000B2 (复古)
        public const uint R_STR_ISP_RETRO       = R_ID_TYPE_STR + 0xB2; // 0x810000B2 (复古，与SEPIA相同)
        public const uint R_STR_ISP_ISO100      = R_ID_TYPE_STR + 0xB3; // 0x810000B3 (ISO100)
        public const uint R_STR_ISP_ISO200      = R_ID_TYPE_STR + 0xB4; // 0x810000B4 (ISO200)
        public const uint R_STR_ISP_ISO400      = R_ID_TYPE_STR + 0xB5; // 0x810000B5 (ISO400)
        public const uint R_STR_ISP_WDR         = R_ID_TYPE_STR + 0xB6; // 0x810000B6 (WDR)
        public const uint R_STR_ISP_EXPOSURE    = R_ID_TYPE_STR + 0xB7; // 0x810000B7 (曝光)

        // ============================================================
        // 旧格式转换工具
        // 旧格式: 0x01XX (XX = R_ID_STR_* 枚举偏移)
        // 新格式: 0x81000000 + XX = R_ID_TYPE_STR + XX
        // 
        // 注意：仅适用于 R_ID_STR_* 枚举值，不适用于原始数值
        // 例如 CONFIG_ID_VOLUME = 10 (原始数值，不需要转换)
        // ============================================================

        /// <summary>
        /// 判断值是否为旧的 0x01XX 格式
        /// </summary>
        public static bool IsLegacyFormat(uint value)
        {
            return value >= 0x0100 && value <= 0x01FF;
        }

        /// <summary>
        /// 判断值是否已经是正确的 0x81XXXXXX 格式
        /// </summary>
        public static bool IsCorrectFormat(uint value)
        {
            return (value & 0xFF000000) == R_ID_TYPE_STR;
        }

        /// <summary>
        /// 将旧的 0x01XX 格式转换为正确的 0x810000XX 格式
        /// 如果已经是正确格式则直接返回
        /// 如果不是旧格式也不是正确格式，返回原值
        /// </summary>
        public static uint ConvertLegacyValue(uint value)
        {
            if (IsCorrectFormat(value))
                return value;
            if (IsLegacyFormat(value))
                return R_ID_TYPE_STR + (value & 0xFF);
            return value; // 原始数值（如音量=10）或未知格式，保持不变
        }

        /// <summary>
        /// 创建动态固件常量管理器
        /// 根据 user_str.h 解析的字符串常量动态生成常量值
        /// </summary>
        public static DynamicFirmwareConstants CreateDynamic(Dictionary<string, uint> stringConstants, uint rIdTypeStrBase = 0)
        {
            return new DynamicFirmwareConstants(
                stringConstants ?? new Dictionary<string, uint>(),
                rIdTypeStrBase == 0 ? R_ID_TYPE_STR : rIdTypeStrBase
            );
        }
    }
}
