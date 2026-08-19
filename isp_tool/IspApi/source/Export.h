// 下列 ifdef 块是创建使从 DLL 导出更简单的
// 宏的标准方法。此 DLL 中的所有文件都是用命令行上定义的 DEVICE_EXPORTS
// 符号编译的。在使用此 DLL 的
// 任何其他项目上不应定义此符号。这样，源文件中包含此文件的任何其他项目都会将
// ISP_API 函数视为是从 DLL 导入的，而此 DLL 则将用此宏定义的
// 符号视为是被导出的。

#ifdef ISP_API_DLL
#ifdef ISP_API_EXPORTS
#define ISP_API extern "C" __declspec(dllexport)
#else
#define ISP_API __declspec(dllimport)
#endif
#else
#define ISP_API
#endif


#include "../include/IQ.h"
//// 此类是从 Device.dll 导出的
//class ISP_API CDevice {
//public:
//	CDevice(void);
//	// TODO:  在此添加您的方法。
//};
//
//extern ISP_API int nDevice;

ISP_API int fnDevice(void);

ISP_API void BlcCal(const void *img_buffer, int img_width, int img_height, int polarity_mode, short **out_data);
ISP_API void BlcImg(const void* img_buffer, short *correction_val, int polarity_mode, int image_width, int image_height, 
    short *blc_img);

ISP_API void LscCal(const void *img_buffer, int img_width, int img_height, int block_size_x, int block_size_y,
    int lsc_mode, int polarity, unsigned int *lsc_table, int ref_x, int ref_y);

ISP_API void LscImg(void *raw_img, int image_width, int image_height, int block_size_x,
    int block_size_y, unsigned int* lsc_weight, void *lsc_img);

ISP_API void LscIQ(short **img_buffer, int img_width, int img_height, lsc_cs_iq_result* colorShadingIQ, lsc_ls_iq_result* lensShadingIQ);

ISP_API void AWBCal(const void *img_buffer, int img_width, int img_height, int polarity,
	unsigned int *x, unsigned int *y, unsigned int *width, unsigned int *height, int &bgain, int &rgain);

ISP_API void AWBStatistic(void *raw_img, int polarity_mode, int w, int h, int seg_mode, 
    unsigned char * awb_stat_tab, int weight_in, int weight_out, int rg_start,
    int rgmin, int rgmax, int ymin, int ymax, int *wp_output);

ISP_API void AWBStatistic_Yuv(void *raw_img, int polarity_mode, int w, int h, int seg_mode, int ymin, int ymax,
    int* awb_cb_th, int* awb_cr_th, int* awb_cbcr_th, int awb_ycbcr_th, int *wp_output);

ISP_API void AWB_Gain_Soft_Cal(int *wp_input, int awb_seg_mode, int* r_gain, int* b_gain, int* g_gain);

ISP_API void AWBImg(void *awb_in_img, int polarity_mode, int image_width, int image_height, int* gain_values,
    int awb_de_high_red_class, int awb_de_high_blue_class, int awb_de_high_red_rate, int awb_de_high_blue_rate,
    void *awb_out_img);

ISP_API void AWB_IQ(short **img_buffer, int img_width, int img_height, int polarity, 
    unsigned int *x, unsigned int *y, unsigned int *width, unsigned int *height, double* rg_iq, double* bg_iq);

ISP_API void CCM_Cal(int *cr_avg, int *cg_avg, int *cb_avg, int delta_C_th, int delta_S_th,
    int cmatrix_th, int step, int *cmatrix_out, int light_source);

ISP_API void Rgb2Lab_CCM_IQ(int *r_avg, int *g_avg, int *b_avg);

//ISP_API int CCM_New_Cal(
//    const void* img_buffer,
//    int img_width, int img_height,
//    int* cr_avg, int* cg_avg, int* cb_avg,
//    float delta_C_th, float delta_S_th,
//    int cmatrix_th, int step,
//    int light_source,
//    int ccmatrix_out[3][3]);

ISP_API int CCM_New_Cal(
    const void* img_buffer,
    int img_width, int img_height,
    int* cr_avg, int* cg_avg, int* cb_avg,
    float delta_C_th, float delta_S_th,
    int cmatrix_th, int step,
    int light_source,
    int ccmatrix_out[3][3],
    int ccm_offset_out[3]);

ISP_API int CCM_Img(
    short** input_img,
    short** output_img,
    int image_width, int image_height,
    int ccm_matrix[3][3],
    int ccm_offset[3]);

ISP_API int CCM_IQ(
    const int* r_avg, const int* g_avg, const int* b_avg,
    float* delta_e_out, float* delta_eab_out,
    float* per_patch_delta);

ISP_API void YGammaImg(int w, int h, int pad_num, unsigned int* global_gamma_table, short **input_img, short **output_img);
ISP_API void YGAMMA_IQ(double *gr_avg, double *gg_avg, double *gb_avg, int num, double* diff_l, int* count, double *l_var, double *delta_l,
    double* y_max, double *y_avg, double* out_gama);

ISP_API void DemosaicImg(void *rawimg, int polarity, int image_width, int image_height, short **demosaic_img);

ISP_API bool EncoderImgBuffer(short **rgb_buff, unsigned int width, unsigned int height, int bit_shift, void* out_buffer, int &buffer_size);
