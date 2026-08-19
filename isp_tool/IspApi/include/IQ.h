#ifndef __IQ_H__
#define __IQ_H__
#include <malloc.h>
#include <stdio.h>
#include <string>
#include <math.h>

#define USE_CV  0
#define OUTPUT_BLOCK_IMG   USE_CV&0
#define SHOW_BLOCK_IMG     USE_CV&0
#define ONE_FRAME_SIM
#define LONG_64 __int64
#define DEBUG_PRINT	1
#define BAYER_BIT 10
#define MAX_F_BIT(x)     ((1<<(x)) - 1)
#define HIGH_VAL_12BIT   MAX_F_BIT(12)
#define HIGH_VAL_10BIT   MAX_F_BIT(10)
#define HIGH_VAL_8BIT    MAX_F_BIT(8)
#define UNROUND(x) ((unsigned int)(x + 0.5))
#define CLIP_PIXEL(val, low, high) (((val) < (low)) ? (low) : (((val) >= (high)) ? (high) : (val)))

#define LSC_CAL_Y	0		// calculation of Y
#define LSC_CAL_RGB 1		// calculation of RGB

// D65标准光源XYZ三刺激值（归一化到100）
#define D65_XN 95.047
#define D65_YN 100.0
#define D65_ZN 108.883

// 色卡数量（24色标准色卡）
#define COLOR_PATCH_COUNT 24

// CCM模块错误码定义
#define CCM_SUCCESS              0    // 成功
#define CCM_ERR_NULL_POINTER     -1   // 空指针输入
#define CCM_ERR_INVALID_PARAM    -2   // 参数超出范围
#define CCM_ERR_MEMORY_ALLOC     -3   // 内存分配失败
#define CCM_ERR_NO_CONVERGENCE   -4   // 搜索未收敛（最优解仍超阈值）
#define CCM_ERR_FILE_NOT_FOUND   -5   // 文件不存在

typedef short Pix;
typedef unsigned char YUVPix;

struct awb_rect{
	unsigned int x[6];
	unsigned int y[6];
	unsigned int height[6];
	unsigned int width[6];
};
struct ccm_iq{
	unsigned int x[24];
	unsigned int y[24];
	unsigned int height[24];
	unsigned int width[24];
};
struct gray_iq{
	unsigned int x[13];
	unsigned int y[13];
	unsigned int height[13];
	unsigned int width[13];
};

struct lsc_cs_iq_result{
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
}; 

struct lsc_ls_iq_result{
	double ly_tl;
	double ly_tr;
	double ly_bl;
	double ly_br;
    double y_tl_rate;
    double y_tr_rate;
    double y_bl_rate;
    double y_br_rate;
};

struct iq_config{
	// common
	unsigned int	image_width;
	unsigned int	image_height;
	unsigned int	raw_bit_depth;  //2'h0: 8bit  2'h1: 10bit    2'h2: 12bit   2'h3: Reserved
	unsigned int	polarity_mode;  // 0:RG
	unsigned int	low_bit_mode;
	// enable bits
	bool			blc_en;
	bool			lsc_en;
	bool			awb_en;
	bool			ccm_en;
	bool			ygamma_en;
	// cal enable bits
	bool			blc_cal_en;
	bool			lsc_cal_en;
	bool			awb_cal_en;
    bool			ccm_cal_en;
	// iq enable bits
	bool			lsc_iq_en;
	bool			awb_iq_en;
	bool			ccm_iq_en;
	bool			ygamma_iq_en;
	// isp & iq module config
	// blc
	int				blackl_r;
	int				blackl_gr;
	int				blackl_gb;
	int				blackl_b;
	// lsc
	int				lsc_mode;			// 0:cal Y; 1:cal RGB
	unsigned int	lsc_weight[572];
	unsigned int    lens_en;
	unsigned int    lens_rect_w;
	unsigned int	lens_rect_h;
	unsigned int	colors_en;
	unsigned int    colors_rect_w;
	unsigned int    colors_rect_h;
	// awb statistic
	awb_rect		awb_cal[1];
	awb_rect		awb_iq[1];
	unsigned int	awb_seg_mode;
	unsigned int	awb_weight_in;
	unsigned int	awb_weight_out;
	unsigned int	awb_rg_start;
	unsigned int	awb_rgain_min;
	unsigned int	awb_rgain_max;
	unsigned int	awb_ymin;
	unsigned int	awb_ymax;
	unsigned int	awb_yuv_mod_en;
	int	awb_cb_th[8];
	int	awb_cr_th[8];
	int	awb_cbcr_th[8];
	int	awb_ycbcr_th;

    // 这个东西只需要bgain
	unsigned char	awb_stat_tab[128];
	// awb
	int	r_gain;
	int	g_gain;
	int	b_gain;
	unsigned int 	awb_de_high_red_class;
	unsigned int 	awb_de_high_blue_class;
	unsigned int	awb_de_high_red_rate;
	unsigned int	awb_de_high_blue_rate;
	// ccm
	ccm_iq			ccm_rect[1];
	unsigned int	ccm_par_c[3][3];
	unsigned int	ccm_par_s[3];
	// ygamma
	gray_iq			gray_rect[1];
	unsigned int	global_gamma_table[256];
	unsigned char   pad_num;

	// file path and name
	char			input_file_path[300];
	char			input_file_name[300];
	char			output_file_path[300];
	char			output_file_name[300];
	// output data
	int	wp_output[4 * 8 * 4];//(wpcnt0,wprsum0,wgsum0,wbsum0,wpcnt1,...wbsum7)
};
struct iq_image_buff{
	Pix *raw_img;
	Pix *blc_img;
	Pix *lsc_img;
	Pix *awb_img;
	Pix *demosaic_img[3];
	Pix *ccm_img[3];
	Pix *ygamma_img[3];
};

void IqConfig(iq_config *iq_cfg, iq_image_buff *iq_img_buff, bool isp_en, bool isp_cal_en);
void IspProcess(iq_config *iq_cfg, iq_image_buff *iq_img_buff);
void IspCal(iq_config *iq_cfg, iq_image_buff *iq_img_buff);
void IqCal(iq_config *iq_cfg, iq_image_buff *iq_img_buff);
void get_awb_stat_tab(unsigned char *table, int awb_rg_start, int fa, int fb, int fc, int bound_in_width, int bound_out_width);
void image_write(char * img_name,const char * picture_format,void *pm);
void image_show(char * img_name, void *pm);
void image_write_show(char * img_in_name,const char * out_put_suffix,void *pm);
void ShowRgbImg(Pix **rgb_buff, unsigned int width, unsigned int height, char * img_in_name,const char * out_put_suffix, unsigned int bit_shift);
void PrintRGBImg(Pix **rgb_img, unsigned int w, unsigned int h, char *file_name, unsigned int bit_depth);
void AllocImgBuff(iq_image_buff *iq_img_buff, iq_config *iq_cfg);
void FreeImgBuff(iq_image_buff *iq_img_buff, iq_config *iq_cfg);
void ShowRgbImg(Pix **rgb_buff, unsigned int width, unsigned int height, char * img_in_name, const char * out_put_suffix, unsigned int bit_shift);

#endif
