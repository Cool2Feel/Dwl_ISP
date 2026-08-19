using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace ThunderSE.DeviceConfig.Isp
{
    // CCM模块错误码定义（与Export.h保持一致）
    public static class CcmErrorCode
    {
        public const int CCM_SUCCESS = 0;           // 成功
        public const int CCM_ERR_NULL_POINTER = -1;   // 空指针输�?
        public const int CCM_ERR_INVALID_PARAM = -2;  // 参数超出范围
        public const int CCM_ERR_MEMORY_ALLOC = -3;   // 内存分配失败
        public const int CCM_ERR_NO_CONVERGENCE = -4; // 搜索未收�?
        public const int CCM_ERR_FILE_NOT_FOUND = -5;  // 文件不存�?
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ColorShadingIQResult
    {
        double cr_tl;
        double cr_tr;
        double cr_bl;
        double cr_br;
        double cb_tl;
        double cb_tr;
        double cb_bl;
        double cb_br;
        double rg_tl_rate;
        double rg_tr_rate;
        double rg_bl_rate;
        double rg_br_rate;
        double bg_tl_rate;
        double bg_tr_rate;
        double bg_bl_rate;
        double bg_br_rate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LensShadingIQResult
    {
        double ly_tl;
        double ly_tr;
        double ly_bl;
        double ly_br;
        double y_tl_rate;
        double y_tr_rate;
        double y_bl_rate;
        double y_br_rate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CcmIQResult
    {
        public float DeltaE;       // 平均Delta E值（包含亮度分量�?
        public float DeltaEab;     // 平均Delta Eab值（仅色度分量）
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
        public float[] PerPatchDelta; // 每个色块的Delta E值[24]
    }


    class IspApi
    {
        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DemosaicImg(byte[] rawimg, int polarity, int image_width, int image_height, IntPtr[] demosaic_img);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void EncoderImgBuffer(IntPtr[] demosaic_img,
            int image_width, int image_height, int bit_shift, byte[] out_buffer, ref int buffer_size);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void BlcCal(byte[] imgBuffer, int imgWidth, int imgHeight, int polarity, IntPtr[] outData);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void BlcImg(byte[] imgBuffer, short[] correctionValues, int polarity,
            int imgWidth, int imgHeight, short[] outImg);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void LscCal(byte[] imgBuffer, int imgWidth, int imgHeight, int block_size_x, int block_size_y,
            int lsc_mode, int polarity, int[] outData, int pointX, int pointY);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void LscImg(byte[] imgBuffer, int imgWidth, int imgHeight, int block_size_x, int block_size_y, 
            int[] lsc_weight, short[] outImg);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void LscIQ(IntPtr[] demosaic_img, int imgWidth, int imgHeight, ref ColorShadingIQResult colorShadingIQ,
            ref LensShadingIQResult lenShadingIQ);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void AWBCal(byte[] img_buffer, int img_width, int img_height, 
            int polarity, int[] x, int[] y, int[] width, int[] height, ref int bgain, ref int rgain);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void AWBImg(byte[] awb_in_img, int polarity_mode, int image_width, int image_height, int[] gain_values,
            int awb_de_high_red_class, int awb_de_high_blue_class, int awb_de_high_red_rate, int awb_de_high_blue_rate,
            short[] outImg);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void AWBStatistic(byte[] raw_img, int polarity_mode, int w, int h, int seg_mode, byte[] awb_stat_tab,
            int weight_in, int weight_out, int rg_start, int rgmin, int rgmax, int ymin, int ymax, int[] wp_output);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void AWBStatistic_Yuv(byte[] raw_img, int polarity_mode, int w, int h, int seg_mode, int ymin, int ymax,
            int[] awb_cb_th, int[] awb_cr_th, int[] awb_cbcr_th, int awb_ycbcr_th, int[] wp_output);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void AWB_Gain_Soft_Cal(int[] wp_input, int awb_seg_mode, ref int r_gain, ref int b_gain, ref int g_gain);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void AWB_IQ(IntPtr[] img_buffer, int img_width, int img_height,
            int polarity, int[] x, int[] y, int[] width, int[] height, ref double rg_iq, ref double bg_iq);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void YGammaImg(int w, int h, int pad_num, short[] global_gamma_table, IntPtr[] input_img, IntPtr[] output_img);

        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void YGAMMA_IQ(double[] gr_avg, double[] gg_avg, double[] gb_avg, int num, double[] diff_l, ref int count,
            double[] l_var, double[] delta_l, ref double y_max, double[] y_avg, ref double out_gamma);


        // ====================================================================
        // CCM 相关函数（新增标准化接口 - 与BLC/LSC模块风格一致）
        // ====================================================================
        /*
        /// <summary>
        /// 从图像数据计算最优色彩校正矩�?
        /// </summary>
        /// <param name="imgBuffer">输入图像缓冲�?/param>
        /// <param name="imgWidth">图像宽度</param>
        /// <param name="imgHeight">图像高度</param>
        /// <param name="crAvg">R通道24色卡平均值[24]</param>
        /// <param name="cgAvg">G通道24色卡平均值[24]</param>
        /// <param name="cbAvg">B通道24色卡平均值[24]</param>
        /// <param name="deltaCTh">可接受的Delta_C阈值（建议20.0�?/param>
        /// <param name="deltaSTh">饱和度偏差阈值（建议10.0�?/param>
        /// <param name="cmatrixTh">矩阵元素搜索范围（建�?，即±6�?/param>
        /// <param name="step">搜索步长（建�?�?/param>
        /// <param name="lightSource">光源类型�?=D50理想光源, 1=D65标准光源�?/param>
        /// <param name="ccmatrixOut">输出3×3色彩校正矩阵[3][3]�?.8定点数，基准256�?/param>
        /// <returns>成功返回0，失败返回负错误�?/returns>
        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CCM_New_Cal(
            IntPtr imgBuffer,
            int imgWidth, int imgHeight,
            int[] crAvg, int[] cgAvg, int[] cbAvg,
            float deltaCTh, float deltaSTh,
            int cmatrixTh, int step,
            int lightSource,
            [Out] int[,] ccmatrixOut
        );
        */

        //void CCM_Cal(int *cr_avg, int *cg_avg, int *cb_avg, int delta_C_th, int delta_S_th,
        //int cmatrix_th, int step, int** cmatrix_out, int light_source);
        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CCM_Cal(
            int[] crAvg, int[] cgAvg, int[] cbAvg,
            float deltaCTh, float deltaSTh,
            int cmatrixTh, int step,
            [Out] int[,] ccmatrixOut,
            int lightSource
        );

        /// <summary>
        /// 从图像数据计算最优色彩校正矩�?
        /// </summary>
        /// <param name="imgBuffer">输入图像缓冲�?/param>
        /// <param name="imgWidth">图像宽度</param>
        /// <param name="imgHeight">图像高度</param>
        /// <param name="crAvg">R通道24色卡平均值[24]</param>
        /// <param name="cgAvg">G通道24色卡平均值[24]</param>
        /// <param name="cbAvg">B通道24色卡平均值[24]</param>
        /// <param name="deltaCTh">可接受的Delta_C阈值（建议20.0�?/param>
        /// <param name="deltaSTh">饱和度偏差阈值（建议10.0�?/param>
        /// <param name="cmatrixTh">矩阵元素搜索范围（建�?，即±6�?/param>
        /// <param name="step">搜索步长（建�?�?/param>
        /// <param name="lightSource">光源类型�?=D50理想光源, 1=D65标准光源�?/param>
        /// <param name="ccmatrixOut">输出3×3色彩校正矩阵[3][3]�?.8定点数，基准256�?/param>
        /// <param name="ccmOffsetOut">输出RGB三通道偏移量[3]（用于暗电平补偿�?/param>
        /// <returns>成功返回0，失败返回负错误�?/returns>
        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CCM_New_Cal(
            IntPtr imgBuffer,
            int imgWidth, int imgHeight,
            int[] crAvg, int[] cgAvg, int[] cbAvg,
            float deltaCTh, float deltaSTh,
            int cmatrixTh, int step,
            int lightSource,
            [Out] int[,] ccmatrixOut,
            [Out] int[] ccmOffsetOut
        );


        /// <summary>
        /// 应用色彩校正矩阵到RGB图像
        /// </summary>
        /// <param name="inputImg">输入RGB图像[3]（R/G/B三通道指针数组�?/param>
        /// <param name="outputImg">输出RGB图像[3]（需预先分配内存�?/param>
        /// <param name="imageWidth">图像宽度</param>
        /// <param name="imageHeight">图像高度</param>
        /// <param name="ccmMatrix">3×3色彩校正矩阵[3][3]�?.8定点数，基准256�?/param>
        /// <param name="ccmOffset">RGB三通道偏移量[3]</param>
        /// <returns>成功返回0，失败返回负错误�?/returns>
        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CCM_Img(
            IntPtr[] inputImg,
            IntPtr[] outputImg,
            int imageWidth, int imageHeight,
            int[,] ccmMatrix,
            int[] ccmOffset
        );

        /// <summary>
        /// 评估CCM校正后的色彩准确�?
        /// </summary>
        /// <param name="rAvg">R通道24色块校正后平均值[24]�?0-bit�?/param>
        /// <param name="gAvg">G通道24色块校正后平均值[24]�?0-bit�?/param>
        /// <param name="bAvg">B通道24色块校正后平均值[24]�?0-bit�?/param>
        /// <param name="deltaEOut">输出：平均Delta E�?/param>
        /// <param name="deltaEabOut">输出：平均Delta Eab值（忽略亮度分量�?/param>
        /// <param name="perPatchDelta">输出：每个色块的Delta E值[24]（可选，传IntPtr.Zero则不计算�?/param>
        /// <returns>成功返回0，失败返回负错误�?/returns>
        [DllImport("IspApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CCM_IQ(
            int[] rAvg, int[] gAvg, int[] bAvg,
            out float deltaEOut,
            out float deltaEabOut,
            [In, Out] float[] perPatchDelta
        );
    }
}
