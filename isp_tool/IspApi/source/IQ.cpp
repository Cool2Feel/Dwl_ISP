#include "../include/IQ.h"
#include <iostream>
#include <algorithm>

#include "Export.h"

using namespace std;

void GetRawImg(char *input_file_path, unsigned int img_width, unsigned int img_height, int raw_bit_depth, short *out_data) {
    FILE *in_fp, *out_fp;
    unsigned short tmp;
    in_fp = fopen(input_file_path, "rb");
    if ((out_fp = fopen("./output/ref_file/bayer_data.txt", "wb")) == NULL){
        printf("Can not open the bayer_data file.");

    }

    for (unsigned int i = 0; i < img_width*img_height; i++){
        fread(&tmp, 2, 1, in_fp);
        out_data[i] = tmp << ((1 - raw_bit_depth) * 2);
        fprintf(out_fp, "%03x\n", out_data[i]);
    }
    fclose(in_fp);
    fclose(out_fp);
}

ISP_API void BlcCal(const void *img_buffer, int img_width, int img_height, int polarity_mode, short **out_data)
{
    unsigned int w = img_width;
    unsigned int h = img_height;
    unsigned int polarity = polarity_mode;
    short *raw_img = (short *)img_buffer;
    short *r_array, *gr_array, *gb_array, *b_array, *tmp_array;
    r_array = out_data[0];
    gr_array = out_data[1];
    gb_array = out_data[2];
    b_array = out_data[3];
    // R,GR,GB,B average value
    for (unsigned int i = 0; i < h; i++){
        for (unsigned int j = 0; j < w; j++){
            unsigned int tmp = (i % 2) * 2 + (j % 2);
            if (polarity == 0 || polarity == 2){
                if (tmp == 0)
                    r_array[(i / 2)*w / 2 + (j / 2)] = raw_img[i*w + j];
                if (tmp == 1)
                    gr_array[(i / 2)*w / 2 + (j / 2)] = raw_img[i*w + j];
                if (tmp == 2)
                    gb_array[(i / 2)*w / 2 + (j / 2)] = raw_img[i*w + j];
                if (tmp == 3)
                    b_array[(i / 2)*w / 2 + (j / 2)] = raw_img[i*w + j];
            }
            else if (polarity == 1 || polarity == 3){
                if (tmp == 0)
                    gr_array[(i / 2)*w / 2 + (j / 2)] = raw_img[i*w + j];
                if (tmp == 1)
                    r_array[(i / 2)*w / 2 + (j / 2)] = raw_img[i*w + j];
                if (tmp == 2)
                    b_array[(i / 2)*w / 2 + (j / 2)] = raw_img[i*w + j];
                if (tmp == 3)
                    gb_array[(i / 2)*w / 2 + (j / 2)] = raw_img[i*w + j];
            }
        }
    }
    if (polarity == 2 || polarity == 3){
        tmp_array = r_array;
        r_array = b_array;
        b_array = tmp_array;
        tmp_array = gr_array;
        gr_array = gb_array;
        gb_array = tmp_array;
    }
}

ISP_API void BlcImg(const void* img_buffer, short *correction_val, int polarity_mode, int image_width, int image_height, short *blc_img)
{
    short *raw_img = (short*)img_buffer;
    int blackl_r, blackl_gr, blackl_gb, blackl_b;
    int range_val;
    unsigned int w = image_width;
    unsigned int h = image_height;
    int data_adj[4];
    unsigned int polarity = polarity_mode;

    blackl_r = correction_val[0];
    blackl_gr = correction_val[1];
    blackl_gb = correction_val[2];
    blackl_b = correction_val[3];

    range_val = 1024;

    blackl_r = (blackl_r >= range_val / 2) ? blackl_r - range_val : blackl_r;
    blackl_gr = (blackl_gr >= range_val / 2) ? blackl_gr - range_val : blackl_gr;
    blackl_gb = (blackl_gb >= range_val / 2) ? blackl_gb - range_val : blackl_gb;
    blackl_b = (blackl_b >= range_val / 2) ? blackl_b - range_val : blackl_b;

    for (unsigned int i = 0; i < h; i++) {
        for (unsigned int j = 0; j < w; j++) {
            data_adj[0] = raw_img[i*w + j] + blackl_r;
            data_adj[1] = raw_img[i*w + j] + blackl_gr;
            data_adj[2] = raw_img[i*w + j] + blackl_gb;
            data_adj[3] = raw_img[i*w + j] + blackl_b;

            switch (polarity) {
            case 0:		// 0: RG/GB;
                if ((i & 1) == 0 && (j & 1) == 0){
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[0], 0, HIGH_VAL_10BIT);
                }
                else if ((i & 1) == 0 && (j & 1) == 1){
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[1], 0, HIGH_VAL_10BIT);
                }
                else if ((i & 1) == 1 && (j & 1) == 0){
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[2], 0, HIGH_VAL_10BIT);
                }
                else{
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[3], 0, HIGH_VAL_10BIT);
                }
                break;
            case 1:		//  1: GR/BG;
                if ((i & 1) == 0 && (j & 1) == 0){
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[1], 0, HIGH_VAL_10BIT);
                }
                else if ((i & 1) == 0 && (j & 1) == 1){
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[0], 0, HIGH_VAL_10BIT);
                }
                else if ((i & 1) == 1 && (j & 1) == 0){
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[3], 0, HIGH_VAL_10BIT);
                }
                else{
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[2], 0, HIGH_VAL_10BIT);
                }
                break;
            case 2:		//  2: BG/GR;
                if ((i & 1) == 0 && (j & 1) == 0){
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[3], 0, HIGH_VAL_10BIT);
                }
                else if ((i & 1) == 0 && (j & 1) == 1){
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[2], 0, HIGH_VAL_10BIT);
                }
                else if ((i & 1) == 1 && (j & 1) == 0){
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[1], 0, HIGH_VAL_10BIT);
                }
                else{
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[0], 0, HIGH_VAL_10BIT);
                }
                break;
            case 3:		//  3: GB/RG;
                if ((i & 1) == 0 && (j & 1) == 0){
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[2], 0, HIGH_VAL_10BIT);
                }
                else if ((i & 1) == 0 && (j & 1) == 1){
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[3], 0, HIGH_VAL_10BIT);
                }
                else if ((i & 1) == 1 && (j & 1) == 0){
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[0], 0, HIGH_VAL_10BIT);
                }
                else{
                    blc_img[i*w + j] = CLIP_PIXEL(data_adj[1], 0, HIGH_VAL_10BIT);
                }
                break;
            default:
                printf("Unknown BLC error!");
                return;
                break;
            }
        }
    }
}
ISP_API void LscCal(const void *img_buffer, int img_width, int img_height, int block_size_x, int block_size_y, int lsc_mode,
    int polarity, unsigned int *lsc_table, int ref_x, int ref_y){
    // Calculate
    int w = img_width;
    int h = img_height;
    short *raw_img = (short *)img_buffer;
    int block_h = (h / 2 + block_size_y - 1) / block_size_y + 1;
    int block_w = (w / 2 + block_size_x - 1) / block_size_x + 1;
    int lsc_table_size = 4 * block_h*block_w;
	unsigned int block_y = 0;
	unsigned int block_x = 0;
	double val_th = 50;
	int	   tmp_case = 0;
    //lsc_table = (unsigned int *)malloc(sizeof(unsigned int)*lsc_table_size);
    // cal y
    if (lsc_mode == 0){
        Pix *y_array;
        double tmp_array[289];
		double y_max = 0;
		double mean_val = 0;
		double y_tmp = 0;
		
		int    tmp = 0;	
        y_array = (short *)malloc(sizeof(short)*w*h);
		// get Y image
        for (unsigned int i = 0; i < h; i = i + 2){
            for (unsigned int j = 0; j < w; j = j + 2){
                if (polarity == 0)
                    y_array[i*w + j] = (raw_img[i*w + j] * 77 + (raw_img[i*w + (j + 1)] + raw_img[(i + 1)*w + j]) / 2 * 150 + raw_img[(i + 1)*w + (j + 1)] * 29) / 256;
                else if (polarity == 1)
                    y_array[i*w + j] = (raw_img[i*w + (j + 1)] * 77 + (raw_img[i*w + j] + raw_img[(i + 1)*w + (j + 1)]) / 2 * 150 + raw_img[(i + 1)*w + j] * 29) / 256;
                else if (polarity == 2)
                    y_array[i*w + j] = (raw_img[(i + 1)*w + (j + 1)] * 77 + (raw_img[i*w + (j + 1)] + raw_img[(i + 1)*w + j]) / 2 * 150 + raw_img[i*w + j] * 29) / 256;
                else if (polarity == 3)
                    y_array[i*w + j] = (raw_img[(i + 1)*w + j] * 77 + (raw_img[i*w + j] + raw_img[(i + 1)*w + (j + 1)]) / 2 * 150 + raw_img[i*w + (j + 1)] * 29) / 256;
                else
                    printf("polarity configure error!");
                // copy other three points
                y_array[i*w + (j + 1)] = y_array[i*w + j];
                y_array[(i + 1)*w + j] = y_array[i*w + j];
                y_array[(i + 1)*w + (j + 1)] = y_array[i*w + j];
            }
        }
        // get ref_val       
        for (unsigned int i = 0; i < 17; i++)
            for (unsigned int j = 0; j < 17; j++)
                tmp_array[i * 17 + j] = (double)(y_array[(ref_y - 8 + i)*w + (ref_x - 8 + j)]);

        for (unsigned int i = 0; i < 288; i++){
            for (unsigned int j = 0; j < 288 - i; j++){
                if (tmp_array[j] > tmp_array[j + 1]){
                    int tmp = tmp_array[j];
                    tmp_array[j] = tmp_array[j + 1];
                    tmp_array[j + 1] = tmp;
                }
            }
        }
        mean_val = tmp_array[144];
        for (unsigned int i = 0; i < 289; i++)
            y_max = y_max + (abs(tmp_array[i] - mean_val) < val_th ? tmp_array[i] : mean_val);
        y_max = y_max / 289;
        // get web_val
        for (unsigned int i = 0; i < h; i++){
			for (unsigned int j = 0; j < w; j++){
				block_y = (i + 1) / (block_size_y * 2);		// block_size_y:64
				block_x = (j + 1) / (block_size_x * 2);		// block_size_x:128				
				if (h % (block_size_y * 2) != 0 && i == h - 1)
					block_y = block_y + 1;
				if (w % (block_size_x * 2) != 0 && j == w - 1)
					block_x = block_x + 1;
				if (i == 0 && j == 0)
					tmp_case = 0;
				else if (i == 0 && j == w - 1)
					tmp_case = 1;
				else if (i == h - 1 && j == 0)
					tmp_case = 2;
				else if (i == h - 1 && j == w - 1)
					tmp_case = 3;
				else if (i == 0 && j % (block_size_x * 2) == 0)
					tmp_case = 4;
				else if (j == 0 && i % (block_size_y * 2) == 0)
					tmp_case = 5;
				else if (i == h - 1 && j % (block_size_x * 2) == 0)
					tmp_case = 6;
				else if (j == w - 1 && i % (block_size_y * 2) == 0)
					tmp_case = 7;
				else if (i % (block_size_y * 2) == 0 && j % (block_size_x * 2) == 0)
					tmp_case = 8;
				else
					tmp_case = -1;
				if (tmp_case != -1){
					for (unsigned int n = 0; n < 9; n++){
						for (unsigned int m = 0; m < 9; m++){
							switch (tmp_case){
							case 0:
								tmp_array[n * 9 + m] = (double)(y_array[(i + n)*w + (j + m)]);
								break;
							case 1:
								tmp_array[n * 9 + m] = (double)(y_array[(i + n)*w + (j - m)]);
								break;
							case 2:
								tmp_array[n * 9 + m] = (double)(y_array[(i - n)*w + (j + m)]);
								break;
							case 3:
								tmp_array[n * 9 + m] = (double)(y_array[(i - n)*w + (j - m)]);
								break;
							case 4:
								tmp_array[n * 9 + m] = (double)(y_array[(i + n)*w + (j - 4 + m)]);
								break;
							case 5:
								tmp_array[n * 9 + m] = (double)(y_array[(i - 4 + n)*w + (j + m)]);
								break;
							case 6:
								tmp_array[n * 9 + m] = (double)(y_array[(i - n)*w + (j - 4 + m)]);
								break;
							case 7:
								tmp_array[n * 9 + m] = (double)(y_array[(i - 4 + n)*w + (j - m)]);
								break;
							case 8:
								tmp_array[n * 9 + m] = (double)(y_array[(i - 4 + n)*w + (j - 4 + m)]);
								break;
							default:
								printf("LSC cal block error!\n");
								break;
							}
						}
					}
					for (unsigned int n = 0; n < 80; n++){
						for (unsigned int m = 0; m < 80 - n; m++){
							if (tmp_array[m] > tmp_array[m + 1]){
								tmp = tmp_array[m];
								tmp_array[m] = tmp_array[m + 1];
								tmp_array[m + 1] = tmp;
							}
						}
					}
					mean_val = tmp_array[40];
					for (unsigned int n = 0; n < 81; n++)
						y_tmp = y_tmp + (abs(tmp_array[n] - mean_val) < val_th ? tmp_array[n] : mean_val);
					y_tmp = y_tmp / 81;
					for (unsigned int k = 0; k < 4; k++)
						lsc_table[block_y*block_w + block_x + k *block_h*block_w] = CLIP_PIXEL((unsigned int)(y_max / y_tmp * 256), 0, HIGH_VAL_10BIT);
					y_tmp = 0;
				}
            }
        }
        if (y_array != NULL){
            free(y_array);
            y_array = NULL;
        }
    }
    // cal bayer
    if (lsc_mode == 1){       
        double block_array[4][81];
        double mean_val[4];
		double tmp;
		int	   bformat;
		Pix	   *tmp_array[4];
		double mid_val[4] = { 0.0 };
        for (unsigned int i = 0; i < 4; i++){
            tmp_array[i] = (Pix *)malloc(sizeof(Pix)*w / 2 * h / 2);
            memset(tmp_array[i], 0, sizeof(Pix)*w / 2 * h / 2);
        }
        // partition
        for (unsigned int i = 0; i < h; i++){
            for (unsigned int j = 0; j < w; j++){
				bformat = (i % 2) * 2 + (j % 2);
				tmp_array[bformat][(i / 2)*w / 2 + (j / 2)] = raw_img[i*w + j];
            }
        }
        // middle block
        for (unsigned int i = 0; i < 9; i++){
            for (unsigned int j = 0; j < 9; j++){
                for (unsigned int k = 0; k < 4; k++)
                    block_array[k][i * 9 + j] = (double)(tmp_array[k][(ref_y / 2 - 4 + i)*w / 2 + (ref_x / 2 - 4 + j)]);
            }
        }
        // sort
        for (unsigned int k = 0; k < 4; k++){
            for (unsigned int i = 0; i < 80; i++){
                for (unsigned int j = 0; j < 80 - i; j++){
                    if (block_array[k][j] > block_array[k][j + 1]){
                        tmp = block_array[k][j];
                        block_array[k][j] = block_array[k][j + 1];
                        block_array[k][j + 1] = tmp;
                    }
                }
            }
        }
        // get mean value, replace bad points
        for (unsigned int i = 0; i < 4; i++)
            mean_val[i] = block_array[i][40];       
        for (unsigned int i = 0; i < 9; i++){
            for (unsigned int j = 0; j < 9; j++){
                for (unsigned int k = 0; k < 4; k++){
                    mid_val[k] = mid_val[k] + (abs(block_array[k][i * 9 + j] - mean_val[k]) < val_th ?  block_array[k][i * 9 + j] : mean_val[k]);
                }
            }
        }
        for (unsigned int i = 0; i < 4; i++)
            mid_val[i] = mid_val[i] / 81;
        // other block, 9 cases: 4 points, 4 lines, inside
        for (unsigned int i = 0; i < h / 2; i++){
            for (unsigned int j = 0; j < w / 2; j++){
                block_y = (i + 1) / block_size_y;
                block_x = (j + 1) / block_size_x;
                if ((h / 2) % block_size_y != 0 && i == h / 2 - 1)
                    block_y = block_y + 1;
                if ((w / 2) % block_size_x != 0 && j == w / 2 - 1)
                    block_x = block_x + 1;
                if (i == 0 && j == 0)
                    tmp_case = 0;
                else if (i == 0 && j == w / 2 - 1)
                    tmp_case = 1;
                else if (j == 0 && i == h / 2 - 1)
                    tmp_case = 2;
                else if (i == h / 2 - 1 && j == w / 2 - 1)
                    tmp_case = 3;
                else if (i == 0 && j%block_size_x == 0)
                    tmp_case = 4;
                else if (j == 0 && i%block_size_y == 0)
                    tmp_case = 5;
                else if (i == h / 2 - 1 && j%block_size_x == 0)
                    tmp_case = 6;
                else if (j == w / 2 - 1 && i%block_size_y == 0)
                    tmp_case = 7;
                else if (i%block_size_y == 0 && j%block_size_x == 0)
                    tmp_case = 8;
                else
                    tmp_case = -1;
                if (tmp_case != -1){
                    // 5x5 block
                    for (unsigned int n = 0; n < 5; n++){
                        for (unsigned int m = 0; m < 5; m++){
                            for (unsigned int k = 0; k < 4; k++){
                                switch (tmp_case){
                                case 0:
                                    block_array[k][n * 5 + m] = (double)tmp_array[k][(i + n)*w / 2 + (j + m)];
                                    break;
                                case 1:
                                    block_array[k][n * 5 + m] = (double)tmp_array[k][(i + n)*w / 2 + (j - m)];
                                    break;
                                case 2:
                                    block_array[k][n * 5 + m] = (double)tmp_array[k][(i - n)*w / 2 + (j + m)];
                                    break;
                                case 3:
                                    block_array[k][n * 5 + m] = (double)tmp_array[k][(i - n)*w / 2 + (j - m)];
                                    break;
                                case 4:
                                    block_array[k][n * 5 + m] = (double)tmp_array[k][(i + n)*w / 2 + (j - 2 + m)];
                                    break;
                                case 5:
                                    block_array[k][n * 5 + m] = (double)tmp_array[k][(i - 2 + n)*w / 2 + (j + m)];
                                    break;
                                case 6:
                                    block_array[k][n * 5 + m] = (double)tmp_array[k][(i - n)*w / 2 + (j - 2 + m)];
                                    break;
                                case 7:
                                    block_array[k][n * 5 + m] = (double)tmp_array[k][(i - 2 + n)*w / 2 + (j - m)];
                                    break;
                                case 8:
                                    block_array[k][n * 5 + m] = (double)tmp_array[k][(i - 2 + n)*w / 2 + (j - 2 + m)];
                                    break;
                                default:
                                    printf("LSC cal block error!\n");
                                    break;
                                }
                            }
                        }
                    }

                    // get mean value, replace bad points
                    for (unsigned int k = 0; k < 4; k++)
                        mean_val[k] = block_array[k][12];
                    double tmp_val[4] = { 0.0 };
                    for (unsigned int n = 0; n < 5; n++){
                        for (unsigned int m = 0; m < 5; m++){
                            for (unsigned int k = 0; k < 4; k++){
                                tmp_val[k] = tmp_val[k] + (abs(block_array[k][n * 5 + m] - mean_val[k]) < val_th ? block_array[k][n * 5 + m] : mean_val[k]);
                            }
                        }
                    }
                    for (unsigned int k = 0; k < 4; k++)
                        tmp_val[k] = tmp_val[k] / 25;
                    for (unsigned int k = 0; k < 4; k++)
                        lsc_table[block_y*block_w + block_x + k *block_h*block_w] = CLIP_PIXEL((unsigned int)(mid_val[k] / tmp_val[k] * 256), 0, HIGH_VAL_10BIT);
                }
            }
        }
        for (unsigned int i = 0; i < 4; i++){
            if (tmp_array[i] != NULL){
                free(tmp_array[i]);
                tmp_array[i] = NULL;
            }
        }
    }
}
ISP_API void LscIQ(short **img_buffer, int img_width, int img_height, lsc_cs_iq_result* colorShadingIQ, lsc_ls_iq_result* lensShadingIQ){
    int w = img_width;
    int h = img_height;
    short **rgb_img = (short **)img_buffer;
    // LSC IQ
    // color shading
    double top_left[3][25], top_right[3][25], bottom_left[3][25], bottom_right[3][25], mid[3][25];
    double mean_top_left[3], mean_top_right[3], mean_bottom_left[3], mean_bottom_right[3], mean_mid[3];
    double sum_top_left[3] = { 0.0 }, sum_top_right[3] = { 0.0 }, sum_bottom_left[3] = { 0.0 }, sum_bottom_right[3] = { 0.0 }, sum_mid[3] = { 0.0 };
    double r_tl = 0.0, r_tr = 0.0, r_mid = 0.0, r_bl = 0.0, r_br = 0.0;
    double g_tl = 0.0, g_tr = 0.0, g_mid = 0.0, g_bl = 0.0, g_br = 0.0;
    double b_tl = 0.0, b_tr = 0.0, b_mid = 0.0, b_bl = 0.0, b_br = 0.0;
    double rg_tl_rate = 0, rg_tr_rate = 0.0, rg_bl_rate = 0.0, rg_br_rate = 0.0;
    double bg_tl_rate = 0, bg_tr_rate = 0.0, bg_bl_rate = 0.0, bg_br_rate = 0.0;
	int	   val_th = 10;

	for (unsigned int i = 0; i < h; i++){
		for (unsigned int j = 0; j < w; j++){
			int tmp_case = 0;
			if (i == 2 && j == 2)
				tmp_case = 0;
			else if (i == 2 && j == w - 3)
				tmp_case = 1;
			else if (i == h - 3 && j == 2)
				tmp_case = 2;
			else if (i == h - 3 && j == w - 3)
				tmp_case = 3;
			else if (i == h / 2 - 1 && j == w / 2 - 1)
				tmp_case = 4;
			else
				tmp_case = -1;
			if (tmp_case != -1){
				for (unsigned int n = 0; n < 5; n++){
					for (unsigned int m = 0; m < 5; m++){
						for (unsigned int k = 0; k < 3; k++){
							switch (tmp_case){
							case 0:
								top_left[k][n * 5 + m] = (double)rgb_img[k][(i + 2 - n)*w + (j + 2 - m)];
								break;
							case 1:
								top_right[k][n * 5 + m] = (double)rgb_img[k][(i + 2 - n)*w + (j + 2 - m)];
								break;
							case 2:
								bottom_left[k][n * 5 + m] = (double)rgb_img[k][(i + 2 - n)*w + (j + 2 - m)];
								break;
							case 3:
								bottom_right[k][n * 5 + m] = (double)rgb_img[k][(i + 2 - n)*w + (j + 2 - m)];
								break;
							case 4:
								mid[k][n * 5 + m] = (double)rgb_img[k][(i + 2 - n)*w + (j + 2 - m)];
								break;
							}
						}
					}
				}
			}
		}
	}
    for (unsigned int k = 0; k < 3; k++){
        for (unsigned int n = 0; n < 24; n++){
            for (unsigned int m = 0; m < 24 - n; m++){
                int tmp = 0;
                if (top_left[k][m] > top_left[k][m + 1]){
                    tmp = top_left[k][m];
                    top_left[k][m] = top_left[k][m + 1];
                    top_left[k][m + 1] = tmp;
                }
                if (top_right[k][m] > top_right[k][m + 1]){
                    tmp = top_right[k][m];
                    top_right[k][m] = top_right[k][m + 1];
                    top_right[k][m + 1] = tmp;
                }
                if (bottom_left[k][m] > bottom_left[k][m + 1]){
                    tmp = bottom_left[k][m];
                    bottom_left[k][m] = bottom_left[k][m + 1];
                    bottom_left[k][m + 1] = tmp;
                }
                if (bottom_right[k][m] > bottom_right[k][m + 1]){
                    tmp = bottom_right[k][m];
                    bottom_right[k][m] = bottom_right[k][m + 1];
                    bottom_right[k][m + 1] = tmp;
                }
                if (mid[k][m] > mid[k][m + 1]){
                    tmp = mid[k][m];
                    mid[k][m] = mid[k][m + 1];
                    mid[k][m + 1] = tmp;
                }
            }
        }
    }
    for (unsigned int k = 0; k < 3; k++){
        mean_top_left[k] = top_left[k][12];
        mean_top_right[k] = top_right[k][12];
        mean_bottom_left[k] = bottom_left[k][12];
        mean_bottom_right[k] = bottom_right[k][12];
        mean_mid[k] = mid[k][12];
    }
    for (unsigned int k = 0; k < 3; k++){
        for (unsigned int n = 0; n < 25; n++){
			sum_top_left[k] = sum_top_left[k] + (abs(top_left[k][n] - mean_top_left[k]) < val_th ? top_left[k][n] : mean_top_left[k]);
			sum_top_right[k] = sum_top_right[k] + (abs(top_right[k][n] - mean_top_right[k]) < val_th ? top_right[k][n] : mean_top_right[k]);
			sum_bottom_left[k] = sum_bottom_left[k] + (abs(bottom_left[k][n] - mean_bottom_left[k]) < val_th ? bottom_left[k][n] : mean_bottom_left[k]);
			sum_bottom_right[k] = sum_bottom_right[k] + (abs(bottom_right[k][n] - mean_bottom_right[k]) < val_th ? bottom_right[k][n] : mean_bottom_right[k]);
			sum_mid[k] = sum_mid[k] + (abs(mid[k][n] - mean_mid[k]) < val_th ? mid[k][n] : mean_mid[k]);
        }
    }
    r_tl  = sum_top_left[0]		 / 25;
    r_tr  = sum_top_right[0]	 / 25;
    r_bl  = sum_bottom_left[0]	 / 25;
    r_br  = sum_bottom_right[0]  / 25;
    r_mid = sum_mid[0]			 / 25;
    g_tl  = sum_top_left[1]		 / 25;
    g_tr  = sum_top_right[1]	 / 25;
    g_bl  = sum_bottom_left[1]	 / 25;
    g_br  = sum_bottom_right[1]	 / 25;
    g_mid = sum_mid[1]			 / 25;
    b_tl  = sum_top_left[2]		 / 25;
    b_tr  = sum_top_right[2]	 / 25;
    b_bl  = sum_bottom_left[2]   / 25;
    b_br  = sum_bottom_right[2]  / 25;
    b_mid = sum_mid[2]			 / 25;
    rg_tl_rate = (r_tl / g_tl - r_mid / g_mid) / (r_mid / g_mid) * 100.0;
    rg_tr_rate = (r_tr / g_tr - r_mid / g_mid) / (r_mid / g_mid) * 100.0;
    rg_bl_rate = (r_bl / g_bl - r_mid / g_mid) / (r_mid / g_mid) * 100.0;
    rg_br_rate = (r_br / g_br - r_mid / g_mid) / (r_mid / g_mid) * 100.0;
    bg_tl_rate = (b_tl / g_tl - b_mid / g_mid) / (b_mid / g_mid) * 100.0;
    bg_tr_rate = (b_tr / g_tr - b_mid / g_mid) / (b_mid / g_mid) * 100.0;
    bg_bl_rate = (b_bl / g_bl - b_mid / g_mid) / (b_mid / g_mid) * 100.0;
    bg_br_rate = (b_br / g_br - b_mid / g_mid) / (b_mid / g_mid) * 100.0;

    colorShadingIQ->cr_tl = (r_tl / g_tl) / (r_mid / g_mid);
    colorShadingIQ->cr_tr = (r_tr / g_tr) / (r_mid / g_mid);
    colorShadingIQ->cr_bl = (r_bl / g_bl) / (r_mid / g_mid);
    colorShadingIQ->cr_br = (r_br / g_br) / (r_mid / g_mid);
    colorShadingIQ->cb_tl = (b_tl / g_tl) / (b_mid / g_mid);
    colorShadingIQ->cb_tr = (b_tr / g_tr) / (b_mid / g_mid);
    colorShadingIQ->cb_bl = (b_bl / g_bl) / (b_mid / g_mid);
    colorShadingIQ->cb_br = (b_br / g_br) / (b_mid / g_mid);
    colorShadingIQ->rg_tl_rate = rg_tl_rate;
    colorShadingIQ->rg_tr_rate = rg_tr_rate;
    colorShadingIQ->rg_bl_rate = rg_bl_rate;
    colorShadingIQ->rg_br_rate = rg_br_rate;
    colorShadingIQ->bg_tl_rate = bg_tl_rate;
    colorShadingIQ->bg_tr_rate = bg_tr_rate;
    colorShadingIQ->bg_bl_rate = bg_bl_rate;
    colorShadingIQ->bg_br_rate = bg_br_rate;
    //printf("IQ:\n");
    //printf("------------------------------\n");
    //printf("Color shading:\n");
    //printf("R/G RATIO_TL = %4f\n", cr_tl);
    //printf("R/G RATIO_TL = %4f%%\n", rg_tl_rate);
    //printf("R/G RATIO_TR = %4f\n", cr_tr);
    //printf("R/G RATIO_TR = %4f%%\n", rg_tr_rate);
    //printf("R/G RATIO_BL = %4f\n", cr_bl);
    //printf("R/G RATIO_BL = %4f%%\n", rg_bl_rate);
    //printf("R/G RATIO_BR = %4f\n", cr_br);
    //printf("R/G RATIO_BR = %4f%%\n", rg_br_rate);
    //printf("B/G RATIO_TL = %4f\n", cb_tl);
    //printf("B/G RATIO_TL = %4f%%\n", bg_tl_rate);
    //printf("B/G RATIO_TR = %4f\n", cb_tr);
    //printf("B/G RATIO_TR = %4f%%\n", bg_tr_rate);
    //printf("B/G RATIO_BL = %4f\n", cb_bl);
    //printf("B/G RATIO_BL = %4f%%\n", bg_bl_rate);
    //printf("B/G RATIO_BR = %4f\n", cb_br);
    //printf("B/G RATIO_BR = %4f%%\n", bg_br_rate);
    //if ((cr_tl >= 0.85 && cr_tl <= 1.20)
    //    && (cr_tr >= 0.85 && cr_tr <= 1.20)
    //    && (cr_bl >= 0.85 && cr_bl <= 1.20)
    //    && (cr_br >= 0.85 && cr_br <= 1.20)
    //    && (cb_tl >= 0.85 && cb_tl <= 1.20)
    //    && (cb_tr >= 0.85 && cb_tr <= 1.20)
    //    && (cb_bl >= 0.85 && cb_bl <= 1.20)
    //    && (cb_br >= 0.85 && cb_br <= 1.20))
    //{
    //    colorShadingIQ = true;
    //    printf("Color shading is perfect!\n");
    //}
    //else
    //{
    //    colorShadingIQ = false;
    //    printf("Color shading needs correction!\n");
    //}
    //printf("------------------------------\n");
    double y_tl, y_tr, y_bl, y_br, y_mid;
    y_tl = 77 * r_tl + 150 * g_tl + 29 * b_tl;
    y_tr = 77 * r_tr + 150 * g_tr + 29 * b_tr;
    y_bl = 77 * r_bl + 150 * g_bl + 29 * b_bl;
    y_br = 77 * r_br + 150 * g_br + 29 * b_br;
    y_mid = 77 * r_mid + 150 * g_mid + 29 * b_mid;

    lensShadingIQ->ly_tl = y_tl / y_mid;
    lensShadingIQ->ly_tr = y_tr / y_mid;
    lensShadingIQ->ly_bl = y_bl / y_mid;
    lensShadingIQ->ly_br = y_br / y_mid;
    lensShadingIQ->y_tl_rate = (y_tl - y_mid) / y_mid * 100.0;
    lensShadingIQ->y_tr_rate = (y_tr - y_mid) / y_mid * 100.0;
    lensShadingIQ->y_bl_rate = (y_bl - y_mid) / y_mid * 100.0;
    lensShadingIQ->y_br_rate = (y_br - y_mid) / y_mid * 100.0;
    //printf("Lens shading:\n");
    //printf("Y RATIO_TL = %4f\n", ly_tl);
    //printf("Y RATIO_TL = %4f%%\n", y_tl_rate);
    //printf("Y RATIO_TR = %4f\n", ly_tr);
    //printf("Y RATIO_TL = %4f%%\n", y_tr_rate);
    //printf("Y RATIO_BL = %4f\n", ly_bl);
    //printf("Y RATIO_TL = %4f%%\n", y_bl_rate);
    //printf("Y RATIO_BR = %4f\n", ly_br);
    //printf("Y RATIO_TL = %4f%%\n", y_br_rate);
    //if ((ly_tl >= 0.80 && ly_tl <= 1.10)
    //    && (ly_tr >= 0.80 && ly_tr <= 1.10)
    //    && (ly_bl >= 0.80 && ly_bl <= 1.10)
    //    && (ly_br >= 0.80 && ly_br <= 1.10))
    //{
    //    lensShadingIQ = true;
    //    printf("Lens shading is perfect!\n");
    //}
    //else
    //{
    //    lensShadingIQ = false;
    //    printf("Lens shading needs correction!\n");
    //}
    //printf("------------------------------\n");
}
ISP_API void LscImg(void *raw_img, int image_width, int image_height, int block_size_x,
    int block_size_y, unsigned int* lsc_weight, void *lsc_img)
{
    Pix* rawimg = (Pix*)raw_img;
    Pix* lscimg = (Pix*)lsc_img;
    unsigned int w = image_width;
    unsigned int h = image_height;
    unsigned int block_h = (h / 2 + block_size_y - 1) / block_size_y + 1;
    unsigned int block_w = (w / 2 + block_size_x - 1) / block_size_x + 1;
    unsigned int xs = 0;
    unsigned int ys = 0;
    unsigned int s = 0;				// indicates location
    unsigned int block_y = 0;
    unsigned int block_x = 0;
    unsigned int weight_y = 0;
    unsigned int weight_x = 0;
    unsigned int tmp1, tmp2, tmp3, tmp4;
    unsigned t = 0;
    for (unsigned int i = 0; i < h; i++) {
        for (unsigned int j = 0; j < w; j++){
            xs = j % 2;
            ys = i % 2;
            s = ys * 2 + xs;
            block_y = (i / 2) / block_size_y;
            block_x = (j / 2) / block_size_x;
            weight_y = (i / 2) % block_size_y;
            weight_x = (j / 2) % block_size_x;
            tmp1 = lsc_weight[block_h*block_w*s + block_y*block_w + block_x] * (block_size_x - weight_x)*(block_size_y - weight_y);
            tmp2 = lsc_weight[block_h*block_w*s + (block_y + 1)*block_w + block_x] * weight_y * (block_size_x - weight_x);
            tmp3 = lsc_weight[block_h*block_w*s + block_y*block_w + (block_x + 1)] * (block_size_y - weight_y) * weight_x;
            tmp4 = lsc_weight[block_h*block_w*s + (block_y + 1)*block_w + block_x + 1] * weight_y * weight_x;
            t = (tmp1 + tmp2 + tmp3 + tmp4) / block_size_y / block_size_x;
            lscimg[i*w + j] = CLIP_PIXEL(t * rawimg[i*w + j] / 256, 0, HIGH_VAL_10BIT);
        }
    }
}

ISP_API void AWBCal(const void *img_buffer, int img_width, int img_height, int polarity,
    unsigned int *x, unsigned int *y, unsigned int *width, unsigned int *height, int &bgain, int &rgain){
    // awb calculation
    unsigned int h = img_height;
    unsigned int w = img_width;
    short *raw_img = (short *)img_buffer;
    bool flag = 0;
    unsigned int count = 0;
    unsigned int sum_r = 0, sum_g = 0, sum_b = 0;
    for (unsigned int k = 0; k < 6; k++){
        if (width[k] == 0)
            break;
        else {
            for (unsigned int i = 0; i < h; i++){
                for (unsigned int j = 0; j < w; j++){
                    if (i >= y[k] && i < (y[k] + height[k])
                        && j >= x[k] && j < (x[k] + width[k])){
                        count = k + 1;
                        unsigned int tmp = (i % 2) * 2 + (j % 2);
                        if (polarity == 0 || polarity == 2){
                            if (tmp == 0)
                                sum_r = sum_r + raw_img[i*w + j];
                            if (tmp == 1 || tmp == 2)
                                sum_g = sum_g + raw_img[i*w + j];
                            if (tmp == 3)
                                sum_b = sum_b + raw_img[i*w + j];
                        }
                        else {
                            if (tmp == 0 || tmp == 3)
                                sum_g = sum_g + raw_img[i*w + j];
                            if (tmp == 1)
                                sum_r = sum_r + raw_img[i*w + j];
                            if (tmp == 2)
                                sum_b = sum_b + raw_img[i*w + j];
                        }
                    }
                }
            }
        }
    }
    unsigned int num = 0;
    for (unsigned int i = 0; i < count; i++){
        num = num + height[i] * width[i];
    }
    double avg_r = (double)(sum_r) / (double)(num / 4);
    double avg_g = (double)(sum_g) / (double)(num / 2);
    double avg_b = (double)(sum_b) / (double)(num / 4);
    if (polarity == 2 || polarity == 3){
        double tmp_avg;
        tmp_avg = avg_r;
        avg_r = avg_b;
        avg_b = tmp_avg;
    }
    rgain = CLIP_PIXEL(int(avg_g / avg_r * 256), 0, HIGH_VAL_10BIT);
    bgain = CLIP_PIXEL(int(avg_g / avg_b * 256), 0, HIGH_VAL_10BIT);
    printf("r_gain = %d\n", rgain);
    printf("b_gain = %d\n", bgain);
    printf("end AWBCal\n");
}

ISP_API void AWB_IQ(short **img_buffer, int img_width, int img_height, int polarity,
    unsigned int *x, unsigned int *y, unsigned int *width, unsigned int *height, double* rg_iq, double* bg_iq){
    // awb IQ
    unsigned int h = img_height;
    unsigned int w = img_width;
    short **rgb_img = (short **)img_buffer;
    bool flag = 0;
    unsigned int count = 0;
    unsigned int sum_r = 0, sum_g = 0, sum_b = 0;
    for (unsigned int k = 0; k < 6; k++){
        if (width[k] == 0)
            break;
        else {
            for (unsigned int i = 0; i < h; i++){
                for (unsigned int j = 0; j < w; j++){
                    if (i >= y[k] && i < (y[k] + height[k])
                        && j >= x[k] && j < (x[k] + width[k])){
                        count = k + 1;
                        sum_r = sum_r + rgb_img[0][i*w + j];
                        sum_g = sum_g + rgb_img[1][i*w + j];
                        sum_b = sum_b + rgb_img[2][i*w + j];
                    }
                }
            }
        }
    }
    unsigned int num = 0;
    for (unsigned int i = 0; i < count; i++){
        num = num + height[i] * width[i];
    }
    double avg_r = (double)(sum_r) / (double)(num);
    double avg_g = (double)(sum_g) / (double)(num);
    double avg_b = (double)(sum_b) / (double)(num);
    *rg_iq = avg_g / avg_r;
    *bg_iq = avg_g / avg_b;
    printf("IQ:\n");
    printf("r_gain = %f\n", *rg_iq);
    printf("b_gain = %f\n", *bg_iq);
    if (*rg_iq > 0.92 && *rg_iq < 1.08 && *bg_iq > 0.92 && *bg_iq < 1.08)
    {
        printf("AWB is perfect!\n");
    }
    else
    {
        printf("AWB needs correction!\n");
    }
}
ISP_API void AWBStatistic(void *raw_img, int polarity_mode, int w, int h, int seg_mode, unsigned char * awb_stat_tab, int weight_in, int weight_out, int rg_start,
    int rgmin, int rgmax, int ymin, int ymax, int *wp_output)
{
    Pix* img = (Pix*)raw_img;

    unsigned int segs = 1 << seg_mode;
    unsigned int r_chanel_of_polar[4] = { 0, 1, 3, 2 };
    unsigned int gr_chanel_of_polar[4] = { 1, 0, 2, 3 };
    unsigned int chanel_num_r;
    unsigned int chanel_num_b;
    unsigned int chanel_num_gr;
    unsigned int chanel_num_gb;
    if (polarity_mode < 4){
        chanel_num_r = r_chanel_of_polar[polarity_mode];
        chanel_num_b = 3 - chanel_num_r;
        chanel_num_gr = gr_chanel_of_polar[polarity_mode];
        chanel_num_gb = 3 - chanel_num_gr;
    }
    else{
        printf("Unknown AWB error!");
        return;
    }
    unsigned char r, g, b, y;
    char cb, cr;
    unsigned int rgain, weight, segk;
    unsigned int bound_out_low, bound_out_high, bound_in_low, bound_in_high;
    unsigned int bgain_out_low, bgain_out_high, bgain_in_low, bgain_in_high;
    if (rgmax > (rg_start + 496)){
        rgmax = (rg_start + 496);
    }
    if (rgmin < rg_start){
        rgmin = rg_start;
    }
    for (unsigned int n = 0; n < h; n += 2){
        for (unsigned int m = 0; m < w; m += 2){
            r = img[(n + chanel_num_r / 2)*w + (m + (chanel_num_r % 2))] >> (BAYER_BIT - 8);
            g = (img[(n + chanel_num_gr / 2)*w + (m + (chanel_num_gr % 2))] +
                img[(n + chanel_num_gb / 2)*w + (m + (chanel_num_gb % 2))]) >> (BAYER_BIT - 7);
            b = img[(n + chanel_num_b / 2)*w + (m + (chanel_num_b % 2))] >> (BAYER_BIT - 8);
            y = (r * 77 + g * 150 + b * 29) / 256;
            if ((y >= ymin) && (y <= ymax)){
                segk = y >> (8 - seg_mode);
                weight = 0;

                if (r == 0){
                    rgain = HIGH_VAL_10BIT;
                }
                else{
                    rgain = CLIP_PIXEL(g * 256 / r, 0, HIGH_VAL_10BIT);
                }
                if ((rgain >= rgmin) && (rgain <= rgmax)){
                    int rgain_num = (rgain - rg_start) / 16;
                    int rgain_mod = (rgain - rg_start) % 16;
                    if (rgain_num < 31){
                        bgain_out_high = (awb_stat_tab[rgain_num] * (16 - rgain_mod) + awb_stat_tab[rgain_num + 1] * rgain_mod) / 4;
                        bgain_in_high = (awb_stat_tab[32 + rgain_num] * (16 - rgain_mod) + awb_stat_tab[32 + rgain_num + 1] * rgain_mod) / 4;
                        bgain_in_low = (awb_stat_tab[2 * 32 + rgain_num] * (16 - rgain_mod) + awb_stat_tab[2 * 32 + rgain_num + 1] * rgain_mod) / 4;
                        bgain_out_low = (awb_stat_tab[3 * 32 + rgain_num] * (16 - rgain_mod) + awb_stat_tab[3 * 32 + rgain_num + 1] * rgain_mod) / 4;
                    }
                    else{
                        bgain_out_high = awb_stat_tab[31] * 4;
                        bgain_in_high = awb_stat_tab[63] * 4;
                        bgain_in_low = awb_stat_tab[95] * 4;
                        bgain_out_low = awb_stat_tab[127] * 4;
                    }
                    bound_out_low = bgain_out_low*b / 256;
                    bound_out_high = bgain_out_high*b / 256;
                    if ((g >= bound_out_low) && (g <= bound_out_high)){
                        weight = weight_out + 1;
                    }
                    bound_in_low = bgain_in_low*b / 256;
                    bound_in_high = bgain_in_high*b / 256;
                    if ((g >= bound_in_low) && (g <= bound_in_high)){
                        weight = weight_in + 1;
                    }
                }

                wp_output[segk * 4] += weight;
                wp_output[segk * 4 + 1] += r*weight;
                wp_output[segk * 4 + 2] += g*weight;
                wp_output[segk * 4 + 3] += b*weight;
            }

        }
    }
    // TODO:这部分输出要迁移到整体运算过程中
    //FILE *fp;
    //fp = fopen("./output/ref_file/isp_wp_stat_out.ref", "wb");
    //for (unsigned int i = 0; i < 8; i++){
    //    fprintf(fp, "%08x\n", wp_output[i * 4]);
    //    fprintf(fp, "%08x\n", wp_output[i * 4 + 1]);
    //    fprintf(fp, "%08x\n", wp_output[i * 4 + 2]);
    //    fprintf(fp, "%08x\n", wp_output[i * 4 + 3]);
    //}
    //fprintf(fp, "\n");
    //fclose(fp);
}

ISP_API void AWBStatistic_Yuv(void *raw_img, int polarity_mode, int w, int h, int seg_mode, int ymin, int ymax,
    int* awb_cb_th, int* awb_cr_th, int* awb_cbcr_th, int awb_ycbcr_th, int *wp_output)
{
    Pix *img = (Pix*)raw_img;
    unsigned int segs = 1 << seg_mode;
    unsigned int r_chanel_of_polar[4] = { 0, 1, 3, 2 };
    unsigned int gr_chanel_of_polar[4] = { 1, 0, 2, 3 };
    unsigned int chanel_num_r;
    unsigned int chanel_num_b;
    unsigned int chanel_num_gr;
    unsigned int chanel_num_gb;
    if (polarity_mode < 4){
        chanel_num_r = r_chanel_of_polar[polarity_mode];
        chanel_num_b = 3 - chanel_num_r;
        chanel_num_gr = gr_chanel_of_polar[polarity_mode];
        chanel_num_gb = 3 - chanel_num_gr;
    }
    else{
        printf("Unknown AWB error!");
        return;
    }
    unsigned char r, g, b, y;
    char cb, cr;
    unsigned int rgain, weight, segk;

    for (unsigned int n = 0; n < h; n += 2){
        for (unsigned int m = 0; m < w; m += 2){
            r = img[(n + chanel_num_r / 2)*w + (m + (chanel_num_r % 2))] >> (BAYER_BIT - 8);
            g = (img[(n + chanel_num_gr / 2)*w + (m + (chanel_num_gr % 2))] +
                img[(n + chanel_num_gb / 2)*w + (m + (chanel_num_gb % 2))]) >> (BAYER_BIT - 7);
            b = img[(n + chanel_num_b / 2)*w + (m + (chanel_num_b % 2))] >> (BAYER_BIT - 8);
            y = (r * 77 + g * 150 + b * 29) / 256;
            if ((y >= ymin) && (y <= ymax)){
                segk = y >> (8 - seg_mode);
                weight = 0;

                cb = (-r * 43 - g * 85 + b * 128) / 256;
                cr = (r * 128 - g * 107 - b * 21) / 256;
                if ((abs(cb) < awb_cb_th[segk]) && (abs(cr) < awb_cr_th[segk])){
                    if ((abs(cb) + abs(cr) < awb_cbcr_th[segk]) && (y > abs(cb) + abs(cr) + awb_ycbcr_th)){
                        weight = 1;
                    }
                }
                wp_output[segk * 4] += weight;
                wp_output[segk * 4 + 1] += r*weight;
                wp_output[segk * 4 + 2] += g*weight;
                wp_output[segk * 4 + 3] += b*weight;
            }

        }
    }
    // TODO:这部分输出要迁移到整体运算过程中
    //FILE *fp;
    //fp = fopen("./output/ref_file/isp_wp_stat_out.ref", "wb");
    //for (unsigned int i = 0; i < 8; i++){
    //    fprintf(fp, "%08x\n", wp_output[i * 4]);
    //    fprintf(fp, "%08x\n", wp_output[i * 4 + 1]);
    //    fprintf(fp, "%08x\n", wp_output[i * 4 + 2]);
    //    fprintf(fp, "%08x\n", wp_output[i * 4 + 3]);
    //}
    //fprintf(fp, "\n");
    //fclose(fp);
}

ISP_API void AWB_Gain_Soft_Cal(int *wp_input, int awb_seg_mode, int* r_gain, int* b_gain, int* g_gain)
{
    unsigned int rgain = 0, bgain = 0, k_weight, k_weight_all = 0, segs = 1 << awb_seg_mode;
    unsigned int seg_k_weight[8] = { 24, 32, 36, 36, 36, 36, 36, 24 };
    for (unsigned int i = 0; i < segs; i++){
        if (wp_input[i * 4] < (2048 * 8 / segs)){
            k_weight = 0;
        }
        else{
            k_weight = seg_k_weight[i];
            rgain += (LONG_64)(wp_input[i * 4 + 2])*k_weight * 256 / wp_input[i * 4 + 1];
            bgain += (LONG_64)(wp_input[i * 4 + 2])*k_weight * 256 / wp_input[i * 4 + 3];
        }
        k_weight_all += k_weight;
    }
    //k_weight_all =0;
    if (k_weight_all == 0){
        *r_gain = 256;
        *b_gain = 256;
    }
    else{
        *b_gain = CLIP_PIXEL(bgain / k_weight_all, 0, 1023);
        *r_gain = CLIP_PIXEL(rgain / k_weight_all, 0, 1023);
    }
    *g_gain = 256;
}

ISP_API void AWBImg(void *awb_in_img, int polarity_mode, int image_width, int image_height, int* gain_values,
    int awb_de_high_red_class, int awb_de_high_blue_class, int awb_de_high_red_rate, int awb_de_high_blue_rate,
    void *awb_out_img) {
    short* in_img = (short*)awb_in_img;
    short* out_img = (short*)awb_out_img;

    int r_gain = gain_values[0];
    int g_gain = gain_values[1];
    int b_gain = gain_values[2];

    unsigned int r_chanel_of_polar[4] = { 0, 1, 3, 2 };
    unsigned int chanel_num_r;
    unsigned int chanel_num_b;
    if (polarity_mode < 4){
        chanel_num_r = r_chanel_of_polar[polarity_mode];
        chanel_num_b = 3 - chanel_num_r;
    }
    else{
        printf("Unknown AWB error!");

    }
    unsigned int chanel_num, gain, rate;
    unsigned int awb_de_high_red_th = HIGH_VAL_10BIT - (1 << (6 + awb_de_high_red_class));
    unsigned int awb_de_high_blue_th = HIGH_VAL_10BIT - (1 << (6 + awb_de_high_blue_class));
    for (unsigned int n = 0; n < image_height; n++){
        for (unsigned int m = 0; m < image_width; m++){
            chanel_num = 2 * (n % 2) + (m % 2);
            if (chanel_num == chanel_num_b){  //
                gain = b_gain;
                if ((b_gain < 256) && (awb_de_high_red_class > 0) && (in_img[n*image_width + m] >(short)awb_de_high_red_th)){
                    rate = (HIGH_VAL_10BIT - in_img[n*image_width + m]) * 256 + (in_img[n*image_width + m] - awb_de_high_red_th)
                        * awb_de_high_red_rate;
                    rate = rate >> (8 + awb_de_high_red_class);
                    rate = CLIP_PIXEL(rate, 0, HIGH_VAL_8BIT);
                    gain = (b_gain * rate + 256 * (256 - rate)) >> 8;
                }
            }
            else if (chanel_num == chanel_num_r){
                gain = r_gain;
                if ((r_gain < 256) && (awb_de_high_blue_class > 0) && (in_img[n*image_width + m] > (short)awb_de_high_blue_th)){
                    rate = (HIGH_VAL_10BIT - in_img[n*image_width + m]) * 256 + (in_img[n*image_width + m] - awb_de_high_blue_th)
                        * awb_de_high_blue_rate;
                    rate = rate >> (8 + awb_de_high_blue_class);
                    rate = CLIP_PIXEL(rate, 0, HIGH_VAL_8BIT);
                    gain = (r_gain * rate + 256 * (256 - rate)) >> 8;
                }
            }
            else{
                gain = g_gain;
            }
            out_img[n*image_width + m] = CLIP_PIXEL(in_img[n*image_width + m] * gain / 256, 0, HIGH_VAL_10BIT);
        }
    }
}

int DemosaicGainG(int *matrixgbgr, unsigned int y, unsigned int x, unsigned int size_matrix){
    int dh, dh_tmp1, dh_tmp2, dh_tmp3;
    int dv, dv_tmp1, dv_tmp2, dv_tmp3;
    int wh, wv, gh1, gv1, gh2, gv2, val;
    int delta_h, delta_v;
    dh_tmp1 = abs(matrixgbgr[(y - 1)*size_matrix + (x - 1)] - matrixgbgr[(y - 1)*size_matrix + (x + 1)]);
    dh_tmp2 = abs(matrixgbgr[y*size_matrix + (x - 1)] - matrixgbgr[y*size_matrix + (x + 1)]);
    dh_tmp3 = abs(matrixgbgr[(y + 1)*size_matrix + (x - 1)] - matrixgbgr[(y + 1)*size_matrix + (x + 1)]);
    dh = (2 * dh_tmp2 + dh_tmp1 + dh_tmp3) / 4;
    dv_tmp1 = abs(matrixgbgr[(y - 1)*size_matrix + (x - 1)] - matrixgbgr[(y + 1)*size_matrix + (x - 1)]);
    dv_tmp2 = abs(matrixgbgr[(y - 1)*size_matrix + x] - matrixgbgr[(y + 1)*size_matrix + x]);
    dv_tmp3 = abs(matrixgbgr[(y - 1)*size_matrix + (x + 1)] - matrixgbgr[(y + 1)*size_matrix + (x + 1)]);
    dv = (2 * dv_tmp2 + dv_tmp1 + dv_tmp3) / 4;
    if (dv > 4 * dh){
        wh = 8;    wv = 0;
    }
    else if (dv > 3 * dh){
        wh = 7;    wv = 1;
    }
    else if (dv > 2 * dh){
        wh = 6;    wv = 2;
    }
    else if (dv > dh){
        wh = 5;    wv = 3;
    }
    else if (dv == dh){
        wh = 4;    wv = 4;
    }
    else if (4 * dv < dh){
        wh = 0;     wv = 8;
    }
    else if (3 * dv < dh){
        wh = 1;     wv = 7;
    }
    else if (2 * dv < dh){
        wh = 2;     wv = 6;
    }
    else if (dv < dh){
        wh = 3;     wv = 5;
    }
    gh1 = (matrixgbgr[y*size_matrix + (x - 1)] + matrixgbgr[y*size_matrix + (x + 1)]) / 2;
    gv1 = (matrixgbgr[(y - 1)*size_matrix + x] + matrixgbgr[(y + 1)*size_matrix + x]) / 2;
    delta_h = (2 * matrixgbgr[y*size_matrix + x] - matrixgbgr[y*size_matrix + (x - 2)] - matrixgbgr[y*size_matrix + (x + 2)]) / 4;
    delta_v = (2 * matrixgbgr[y*size_matrix + x] - matrixgbgr[(y - 2)*size_matrix + x] - matrixgbgr[(y + 2)*size_matrix + x]) / 4;
    gh2 = gh1 + delta_h;
    gv2 = gv1 + delta_v;
    val = (wh*gh2 + wv*gv2) / 8;
    val = CLIP_PIXEL(val, 0, HIGH_VAL_10BIT);
    return val;
}
ISP_API void DemosaicImg(void *rawimg, int polarity, int image_width, int image_height, short **demosaic_img){
    unsigned int raw_img_type = polarity;
    unsigned int w = image_width;
    unsigned int h = image_height;
    unsigned int b_size = 4;
    unsigned int w_b = w + 2 * b_size;
    unsigned int h_b = h + 2 * b_size;
    Pix *raw_img = (Pix *)rawimg;
    Pix *extend_raw;
    Pix *Edemosaic_img[3];
    extend_raw = (Pix *)malloc(sizeof(Pix)*w_b*h_b);
    for (int i = 0; i < 3; i++) {
        Edemosaic_img[i] = (Pix*)malloc(sizeof(unsigned short)*w_b*h_b);
        memset(Edemosaic_img[i], 0, w_b*h_b);
    }
    for (unsigned int i = 0; i < h; i++) {
        memcpy(extend_raw + (i + b_size)*w_b + b_size, raw_img + i*w, sizeof(Pix)*w);
    }
    for (unsigned int i = b_size; i < h_b - b_size; i++) {
        // left
        extend_raw[i*w_b + 0] = extend_raw[i*w_b + b_size];
        extend_raw[i*w_b + 2] = extend_raw[i*w_b + b_size];
        extend_raw[i*w_b + 1] = extend_raw[i*w_b + b_size + 1];
        extend_raw[i*w_b + 3] = extend_raw[i*w_b + b_size + 1];
        // right
        extend_raw[i*w_b + w + b_size + 0] = extend_raw[i*w_b + w + b_size - 2];
        extend_raw[i*w_b + w + b_size + 2] = extend_raw[i*w_b + w + b_size - 2];
        extend_raw[i*w_b + w + b_size + 1] = extend_raw[i*w_b + w + b_size - 1];
        extend_raw[i*w_b + w + b_size + 3] = extend_raw[i*w_b + w + b_size - 1];
    }
    for (unsigned int j = 0; j < w_b; j++) {
        // top
        extend_raw[0 * w_b + j] = extend_raw[b_size*w_b + j];
        extend_raw[2 * w_b + j] = extend_raw[b_size*w_b + j];
        extend_raw[1 * w_b + j] = extend_raw[(b_size + 1)*w_b + j];
        extend_raw[3 * w_b + j] = extend_raw[(b_size + 1)*w_b + j];
        // bottom
        extend_raw[(h + b_size)*w_b + j] = extend_raw[(h + b_size - 2)*w_b + j];
        extend_raw[(h + b_size + 2)*w_b + j] = extend_raw[(h + b_size - 2)*w_b + j];
        extend_raw[(h + b_size + 1)*w_b + j] = extend_raw[(h + b_size - 1)*w_b + j];
        extend_raw[(h + b_size + 3)*w_b + j] = extend_raw[(h + b_size - 1)*w_b + j];
    }

    for (unsigned int im = b_size; im < h_b - b_size; im++){
        for (unsigned int jm = b_size; jm < w_b - b_size; jm++){
            int matrix_seven[49];
            unsigned int msize1 = 7;
            for (unsigned int io = 0; io < msize1; io++){
                for (unsigned int jo = 0; jo < msize1; jo++){
                    matrix_seven[io*msize1 + jo] = extend_raw[(im + io - 3)*w_b + (jm + jo - 3)];
                }
            }
            int B44_rg, G44_rg, G33_rg, G35_rg, G53_rg, G55_rg, dggr, R44_rg, tmp;
            B44_rg = matrix_seven[3 * msize1 + 3];
            G44_rg = DemosaicGainG(matrix_seven, 3, 3, msize1);
            G33_rg = DemosaicGainG(matrix_seven, 2, 2, msize1);
            G35_rg = DemosaicGainG(matrix_seven, 2, 4, msize1);
            G53_rg = DemosaicGainG(matrix_seven, 4, 2, msize1);
            G55_rg = DemosaicGainG(matrix_seven, 4, 4, msize1);
            dggr = (G33_rg - matrix_seven[2 * msize1 + 2] + G35_rg - matrix_seven[2 * msize1 + 4] + G53_rg - matrix_seven[4 * msize1 + 2] + G55_rg - matrix_seven[4 * msize1 + 4]) / 4;
            tmp = G44_rg - dggr;
            R44_rg = CLIP_PIXEL(tmp, 0, HIGH_VAL_10BIT);

            int matrix_nine[81];
            unsigned int msize2 = 9;
            for (unsigned int io = 0; io < msize2; io++){
                for (unsigned int jo = 0; jo < msize2; jo++){
                    matrix_nine[io*msize2 + jo] = extend_raw[(im + io - 4)*w_b + (jm + jo - 4)];
                }
            }
            int G55_gr, G34_gr, G36_gr, G43_gr, G45_gr, G47_gr, G54_gr;
            int G56_gr, G63_gr, G65_gr, G67_gr, G74_gr, G76_gr;
            int	dgr, dgb, dggb, R45_gr, R65_gr, R55_gr, B54_gr, B56_gr, B55_gr;
            G55_gr = matrix_nine[4 * msize2 + 4];
            G34_gr = DemosaicGainG(matrix_nine, 2, 3, msize2);
            G36_gr = DemosaicGainG(matrix_nine, 2, 5, msize2);
            G43_gr = DemosaicGainG(matrix_nine, 3, 2, msize2);
            G45_gr = DemosaicGainG(matrix_nine, 3, 4, msize2);
            G47_gr = DemosaicGainG(matrix_nine, 3, 6, msize2);
            G54_gr = DemosaicGainG(matrix_nine, 4, 3, msize2);
            G56_gr = DemosaicGainG(matrix_nine, 4, 5, msize2);
            G63_gr = DemosaicGainG(matrix_nine, 5, 2, msize2);
            G65_gr = DemosaicGainG(matrix_nine, 5, 4, msize2);
            G67_gr = DemosaicGainG(matrix_nine, 5, 6, msize2);
            G74_gr = DemosaicGainG(matrix_nine, 6, 3, msize2);
            G76_gr = DemosaicGainG(matrix_nine, 6, 5, msize2);
            dgr = (G34_gr - matrix_nine[2 * msize2 + 3] + G36_gr - matrix_nine[2 * msize2 + 5] + G54_gr - matrix_nine[4 * msize2 + 3] + G56_gr - matrix_nine[4 * msize2 + 5]) / 4;
            tmp = G45_gr - dgr;
            R45_gr = CLIP_PIXEL(tmp, 0, HIGH_VAL_10BIT);
            dgr = (G54_gr - matrix_nine[4 * msize2 + 3] + G56_gr - matrix_nine[4 * msize2 + 5] + G74_gr - matrix_nine[6 * msize2 + 3] + G76_gr - matrix_nine[6 * msize2 + 5]) / 4;
            tmp = G65_gr - dgr;
            R65_gr = CLIP_PIXEL(tmp, 0, HIGH_VAL_10BIT);
            dggr = (G45_gr - R45_gr + G54_gr - matrix_nine[4 * msize2 + 3] + G56_gr - matrix_nine[4 * msize2 + 5] + G65_gr - R65_gr) / 4;
            tmp = G55_gr - dggr;
            R55_gr = CLIP_PIXEL(tmp, 0, HIGH_VAL_10BIT);
            dgb = (G43_gr - matrix_nine[3 * msize2 + 2] + G45_gr - matrix_nine[3 * msize2 + 4] + G63_gr - matrix_nine[5 * msize2 + 2] + G65_gr - matrix_nine[5 * msize2 + 4]) / 4;
            tmp = G54_gr - dgb;
            B54_gr = CLIP_PIXEL(tmp, 0, HIGH_VAL_10BIT);
            dgb = (G45_gr - matrix_nine[3 * msize2 + 4] + G47_gr - matrix_nine[3 * msize2 + 6] + G65_gr - matrix_nine[5 * msize2 + 4] + G67_gr - matrix_nine[5 * msize2 + 6]) / 4;
            tmp = G56_gr - dgb;
            B56_gr = CLIP_PIXEL(tmp, 0, HIGH_VAL_10BIT);
            dggb = (G45_gr - matrix_nine[3 * msize2 + 4] + G54_gr - B54_gr + G56_gr - B56_gr + G65_gr - matrix_nine[5 * msize2 + 4]) / 4;
            tmp = G55_gr - dggb;
            B55_gr = CLIP_PIXEL(tmp, 0, HIGH_VAL_10BIT);
            unsigned int xl = (jm - b_size) % 2;
            unsigned int yl = (im - b_size) % 2;
            unsigned int sp = xl + 2 * yl;
            if (raw_img_type == 0 || raw_img_type == 2){
                switch (sp){
                case 0:
                    Edemosaic_img[0][im*w_b + jm] = B44_rg;
                    Edemosaic_img[1][im*w_b + jm] = G44_rg;
                    Edemosaic_img[2][im*w_b + jm] = R44_rg;
                    break;
                case 1:
                    Edemosaic_img[0][im*w_b + jm] = R55_gr;
                    Edemosaic_img[1][im*w_b + jm] = G55_gr;
                    Edemosaic_img[2][im*w_b + jm] = B55_gr;
                    break;
                case 2:
                    Edemosaic_img[0][im*w_b + jm] = B55_gr;
                    Edemosaic_img[1][im*w_b + jm] = G55_gr;
                    Edemosaic_img[2][im*w_b + jm] = R55_gr;
                    break;
                case 3:
                    Edemosaic_img[0][im*w_b + jm] = R44_rg;
                    Edemosaic_img[1][im*w_b + jm] = G44_rg;
                    Edemosaic_img[2][im*w_b + jm] = B44_rg;
                    break;
                default:
                    printf("Unknown demosaic error!");

                    break;
                }
            }
            else if (raw_img_type == 1 || raw_img_type == 3){
                switch (sp){
                case 0:
                    Edemosaic_img[0][im*w_b + jm] = R55_gr;
                    Edemosaic_img[1][im*w_b + jm] = G55_gr;
                    Edemosaic_img[2][im*w_b + jm] = B55_gr;
                    break;
                case 1:
                    Edemosaic_img[0][im*w_b + jm] = B44_rg;
                    Edemosaic_img[1][im*w_b + jm] = G44_rg;
                    Edemosaic_img[2][im*w_b + jm] = R44_rg;
                    break;
                case 2:
                    Edemosaic_img[0][im*w_b + jm] = R44_rg;
                    Edemosaic_img[1][im*w_b + jm] = G44_rg;
                    Edemosaic_img[2][im*w_b + jm] = B44_rg;
                    break;
                case 3:
                    Edemosaic_img[0][im*w_b + jm] = B55_gr;
                    Edemosaic_img[1][im*w_b + jm] = G55_gr;
                    Edemosaic_img[2][im*w_b + jm] = R55_gr;
                    break;
                default:
                    printf("Unknown demosaic error!");

                    break;
                }
            }
        }
    }
    if (raw_img_type == 2 || raw_img_type == 3) {
        for (unsigned int i = 0; i < h; i++) {
            memcpy(demosaic_img[0] + i*w, Edemosaic_img[2] + (i + b_size)*w_b + b_size, sizeof(Pix)*w);
            memcpy(demosaic_img[1] + i*w, Edemosaic_img[1] + (i + b_size)*w_b + b_size, sizeof(Pix)*w);
            memcpy(demosaic_img[2] + i*w, Edemosaic_img[0] + (i + b_size)*w_b + b_size, sizeof(Pix)*w);
        }
    }
    else {
        for (unsigned int i = 0; i < 3; i++)
        {
            for (unsigned int j = 0; j < h; j++)
            {
                memcpy(demosaic_img[i] + j*w, Edemosaic_img[i] + (j + b_size)*w_b + b_size, sizeof(Pix)*w);
            }
        }
    }
    free(extend_raw);
    for (unsigned int i = 0; i < 3; i++) {
        free(Edemosaic_img[i]);
    }
}

double gamma(double x){
    return x > 0.04045 ? pow((x + 0.055) / 1.055, 2.4) : x / 12.92;
}

/**
 * @brief Gamma校正（sRGB标准）
 * @param linear 线性值 [0, 1]
 * @return 校正后的gamma值 [0, 1]
 */
double sRGB_GammaCorrection(double linear) {
    if (linear <= 0.04045) {
        return linear / 12.92;
    }
    else {
        return pow((linear + 0.055) / 1.055, 2.4);
    }
}
/**
 * @brief RGB转XYZ色彩空间转换（使用sRGB矩阵）
 * @param r, g, b 输入RGB值 [0, 1023]
 * @param x, y, z 输出XYZ值
 */
void RGB_to_XYZ(int r, int g, int b, double& x, double& y, double& z) {
    // 归一化到[0, 1]
    double rn = (double)r / HIGH_VAL_10BIT;
    double gn = (double)g / HIGH_VAL_10BIT;
    double bn = (double)b / HIGH_VAL_10BIT;

    // Gamma校正
    double rg = sRGB_GammaCorrection(rn);
    double gg = sRGB_GammaCorrection(gn);
    double bg = sRGB_GammaCorrection(bn);

    // sRGB → XYZ 转换矩阵（D65光源）
    x = 0.4124564 * rg + 0.3575761 * gg + 0.1804375 * bg;
    y = 0.2126729 * rg + 0.7151522 * gg + 0.0721750 * bg;
    z = 0.0193339 * rg + 0.1191920 * gg + 0.9503041 * bg;

    // 归一化到[0, 100]范围
    x *= 100.0;
    y *= 100.0;
    z *= 100.0;
}

/**
 * @brief XYZ转Lab色彩空间转换
 * @param x, y, z XYZ值
 * @param xn, yn, zn 参考白点XYZ值
 * @param l, a, b 输出Lab值
 */
void XYZ_to_Lab(double x, double y, double z,
    double xn, double yn, double zn,
    double& l, double& a, double& bb) {
    // f(t) 函数定义
    auto f = [](double t) -> double {
        const double delta = 6.0 / 29.0;
        if (t > pow(delta, 3)) {
            return cbrt(t);
        }
        else {
            return t / (3 * pow(delta, 2)) + 4.0 / 29.0;
        }
        };

    // 计算L*, a*, b*
    l = 116.0 * f(y / yn) - 16.0;
    a = 500.0 * (f(x / xn) - f(y / yn));
    bb = 200.0 * (f(y / yn) - f(z / zn));
}

/**
 * @brief 计算两个颜色之间的Delta E（简化版CIE76）
 * @param l1, a1, b1 颜色1的Lab值
 * @param l2, a2, b2 颜色2的Lab值
 * @return Delta E值
 */
double Calculate_Delta_E(double l1, double a1, double b1,
    double l2, double a2, double b2) {
    double dl = l1 - l2;
    double da = a1 - a2;
    double db = b1 - b2;
    return sqrt(dl * dl + da * da + db * db);
}

/**
 * @brief 计算Delta Eab（忽略亮度分量）
 * @param a1, b1 颜色1的ab分量
 * @param a2, b2 颜色2的ab分量
 * @return Delta Eab值
 */
double Calculate_Delta_Eab(double a1, double b1, double a2, double b2) {
    double da = a1 - a2;
    double db = b1 - b2;
    return sqrt(da * da + db * db);
}

double XYZ2LAB(double x){
    double out_val;
    if (x > pow(6.0 / 29.0, 3.0))
        out_val = pow(x, 1.0 / 3.0);
    else
        out_val = 1.0 / 3.0 * pow((29.0 / 6.0), 2.0) * x + 4.0 / 29.0;
    return out_val;
}

const double L_Ideal[24] = {
    37.26, 65.96, 50.59, 43.19, 55.66, 71.23,
    60.54, 40.52, 50.36, 30.57, 71.98, 71.79,
    29.59, 55.52, 41.84, 81.70, 50.39, 51.08,
    95.37, 80.98, 66.25, 51.24, 35.38, 20.52
};
const double a_Ideal[24] = {
    12.75, 13.54, -1.58, -16.05, 11.22, -31.83,
    31.37, 15.50, 45.39, 23.49, -26.83, 15.03,
    26.88, -41.03, 56.41, -1.25, 49.69, -23.74,
    -0.64, -0.03, -0.10, -0.05, -0.12, 0.35
};
const double b_Ideal[24] = {
    14.85, 17.20, -21.29, 21.95, -25.04, 1.48,
    58.34, -42.49, 14.49, -22.34, 58.56, 67.04,
    -52.69, 34.93, 28.65, 79.40, -15.70, -26.27,
    2.58, 0.27, 0.06, 0.66, -0.14, -0.20
};

const double L_D65[24] = {
    39.22, 83.82, 67.61, 54.13, 75.82, 89.84,
    75.78, 51.95, 66.13, 36.38, 91.57, 89.40,
    31.55, 68.56, 56.31, 96.60, 66.32, 65.07,
    99.99, 93.45, 80.95, 57.71, 25.88, 6.36
};
const double a_D65[24] = {
    13.90, 14.02, -0.86, -32.40, 14.39, -29.10,
    21.45, 36.25, 50.61, 36.26, -35.57, -6.44,
    57.13, -61.13, 71.72, -18.57, 66.47, -5.67,
    -0.05, -3.86, -5.70, -5.70, -3.74, 0.09
};
const double b_D65[24] = {
    29.09, 21.01, -35.52, 41.57, -31.58, -2.38,
    79.09, -75.31, 33.13, -32.64, 62.59, 85.35,
    -87.45, 52.31, 62.46, 80.34, -20.92, -49.07,
    -0.02, -0.14, 1.22, 1.89, -0.94, 0.26
};


ISP_API void CCM_Cal(int *cr_avg, int *cg_avg, int *cb_avg, int delta_C_th, int delta_S_th,
    int cmatrix_th, int step, int *cmatrix_out, int light_source){
    double x_var, y_var, z_var;
    double r_var, g_var, b_var;
    //double *l_val, *a_val, *b_val; // lab average
    //double *r_avg, *g_avg, *b_avg;
    //double *saturation;

    // 【修复】为局部指针分配内存，防止 Run-Time Check Failure
    double* l_val = (double*)malloc(24 * sizeof(double));
    double* a_val = (double*)malloc(24 * sizeof(double));
    double* b_val = (double*)malloc(24 * sizeof(double));

    double* r_avg = (double*)malloc(24 * sizeof(double));
    double* g_avg = (double*)malloc(24 * sizeof(double));
    double* b_avg = (double*)malloc(24 * sizeof(double));

    double* saturation = (double*)malloc(24 * sizeof(double));

    double saturation_sum = 0;
    double saturation_out;
    //double *delta_C;

    double* delta_C = (double*)malloc(24 * sizeof(double));

    double delta_C_sum = 0;
    double delta_C_min = delta_C_th;
    int cmatrix[3][3];

    // 检查内存分配是否成功
    if (!l_val || !a_val || !b_val || !r_avg || !g_avg || !b_avg || !saturation || !delta_C) {
        free(l_val); free(a_val); free(b_val);
        free(r_avg); free(g_avg); free(b_avg);
        free(saturation); free(delta_C);
        return;
    }

    // rgb2lab
    for (cmatrix[0][1] = -cmatrix_th; cmatrix[0][1] < cmatrix_th; cmatrix[0][1] = cmatrix[0][1] + step){
        for (cmatrix[0][2] = -cmatrix_th; cmatrix[0][2] < cmatrix_th; cmatrix[0][2] = cmatrix[0][2] + step){
            cmatrix[0][0] = 256 - cmatrix[0][1] - cmatrix[0][2];
            for (cmatrix[1][0] = -cmatrix_th; cmatrix[1][0] < cmatrix_th; cmatrix[1][0] = cmatrix[1][0] + step){
                for (cmatrix[1][2] = -cmatrix_th; cmatrix[1][2] < cmatrix_th; cmatrix[1][2] = cmatrix[1][2] + step){
                    cmatrix[1][1] = 256 - cmatrix[1][0] - cmatrix[1][2];
                    for (cmatrix[2][0] = -cmatrix_th; cmatrix[2][0] < cmatrix_th; cmatrix[2][0] = cmatrix[2][0] + step){
                        for (cmatrix[2][1] = -cmatrix_th; cmatrix[2][1] < cmatrix_th; cmatrix[2][1] = cmatrix[2][1] + step){
                            cmatrix[2][2] = 256 - cmatrix[2][0] - cmatrix[2][1];

                            // 每次迭代前重置累加器
                            saturation_sum = 0;
                            delta_C_sum = 0;

                            for (unsigned int i = 0; i < 24; i++){
                                // color correction
                                r_avg[i] = (cmatrix[0][0] * cr_avg[i] + cmatrix[1][0] * cg_avg[i] + cmatrix[2][0] * cb_avg[i]) / 256;
                                r_avg[i] = CLIP_PIXEL(r_avg[i], 0, HIGH_VAL_10BIT);
                                g_avg[i] = (cmatrix[0][1] * cr_avg[i] + cmatrix[1][1] * cg_avg[i] + cmatrix[2][1] * cb_avg[i]) / 256;
                                g_avg[i] = CLIP_PIXEL(g_avg[i], 0, HIGH_VAL_10BIT);
                                b_avg[i] = (cmatrix[0][2] * cr_avg[i] + cmatrix[1][2] * cg_avg[i] + cmatrix[2][2] * cb_avg[i]) / 256;
                                b_avg[i] = CLIP_PIXEL(b_avg[i], 0, HIGH_VAL_10BIT);
                                // rgb2Lab
                                r_var = gamma(r_avg[i] / 1024.0);
                                g_var = gamma(g_avg[i] / 1024.0);
                                b_var = gamma(b_avg[i] / 1024.0);
                                x_var = r_var * 0.4124 + g_var * 0.3576 + b_var * 0.1805;
                                y_var = r_var * 0.2126 + g_var * 0.7152 + b_var * 0.0722;
                                z_var = r_var * 0.0193 + g_var * 0.1192 + b_var * 0.9505;
                                //		l_val[i] = 116.0 * XYZ2LAB(y_var/100.0) - 16;
                                a_val[i] = 500.0 * (XYZ2LAB(x_var / 95.047) - XYZ2LAB(y_var / 100.0));
                                b_val[i] = 200.0 * (XYZ2LAB(y_var / 100.0) - XYZ2LAB(z_var / 108.883));
                                saturation[i] = 100.0 * pow((pow(a_val[i], 2) + pow(b_val[i], 2)), 1.0 / 2.0);
                                saturation_sum = saturation_sum + saturation[i];
                                if (light_source == 0)
                                    delta_C[i] = pow((pow(a_val[i] - a_Ideal[i], 2) + pow(b_val[i] - b_Ideal[i], 2)), 1.0 / 2.0);
                                else
                                    delta_C[i] = pow((pow(a_val[i] - a_D65[i], 2) + pow(b_val[i] - b_D65[i], 2)), 1.0 / 2.0);
                                delta_C_sum = delta_C_sum + delta_C[i];
                            }
                            if (abs(saturation_sum / 24.0 - 100.0) < (double)(delta_S_th)){
                                saturation_out = saturation_sum / 24.0;
                                delta_C_sum = delta_C_sum / 24.0;
                                if (delta_C_sum < delta_C_min)
                                    delta_C_min = delta_C_sum;
                                for (unsigned int k1 = 0; k1 < 3; k1++){
                                    for (unsigned int k2 = 0; k2 < 3; k2++){
                                        cmatrix_out[k1 * 3 + k2] = cmatrix[k1][k2];
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    // output
    if (delta_C_min < delta_C_th){
        printf("saturation: %f\n", saturation_out);
        printf("delta_C:%f\n", delta_C_min);
        for (unsigned int k1 = 0; k1 < 3; k1++){
            for (unsigned int k2 = 0; k2 < 3; k2++)
                printf("cmatrix_out[%d][%d]:%d\n", k1, k2, cmatrix_out[k1 * 3 + k2] < 0 ? (1024 - cmatrix_out[k1 * 3 + k2]) : cmatrix_out[k1 * 3 + k2]);
        }
    }
    else {
        printf("The cmatrix_th is not enough!\n");
    }

    // 【修复】释放分配的内存
    free(l_val); free(a_val); free(b_val);
    free(r_avg); free(g_avg); free(b_avg);
    free(saturation); free(delta_C);

}

// ============================================================================
// CCM_Cal: 从图像数据计算最优色彩校正矩阵
// ============================================================================

/* ==========================================================================
 *  CCM 算法优化实现 (基于 Doc\CCM\src\LinearCCM.m)
 *  包含：矩阵运算辅助、最小二乘法求解、8.8定点数转换
 * ========================================================================== */

 // 3x3 矩阵结构
typedef struct {
    double m[3][3];
} Matrix3x3;

// 矩阵乘法: C = A * B
static void MatMul(const Matrix3x3* A, const Matrix3x3* B, Matrix3x3* C) {
    for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++) {
            C->m[i][j] = 0;
            for (int k = 0; k < 3; k++)
                C->m[i][j] += A->m[i][k] * B->m[k][j];
        }
}

// 3x3 矩阵求逆
static bool MatInv(const Matrix3x3* A, Matrix3x3* InvA) {
    double det = A->m[0][0] * (A->m[1][1] * A->m[2][2] - A->m[1][2] * A->m[2][1]) -
        A->m[0][1] * (A->m[1][0] * A->m[2][2] - A->m[1][2] * A->m[2][0]) +
        A->m[0][2] * (A->m[1][0] * A->m[2][1] - A->m[1][1] * A->m[2][0]);

    if (fabs(det) < 1e-10) return false;

    InvA->m[0][0] = (A->m[1][1] * A->m[2][2] - A->m[1][2] * A->m[2][1]) / det;
    InvA->m[0][1] = (A->m[0][2] * A->m[2][1] - A->m[0][1] * A->m[2][2]) / det;
    InvA->m[0][2] = (A->m[0][1] * A->m[1][2] - A->m[0][2] * A->m[1][1]) / det;
    InvA->m[1][0] = (A->m[1][2] * A->m[2][0] - A->m[1][0] * A->m[2][2]) / det;
    InvA->m[1][1] = (A->m[0][0] * A->m[2][2] - A->m[0][2] * A->m[2][0]) / det;
    InvA->m[1][2] = (A->m[0][2] * A->m[1][0] - A->m[0][0] * A->m[1][2]) / det;
    InvA->m[2][0] = (A->m[1][0] * A->m[2][1] - A->m[1][1] * A->m[2][0]) / det;
    InvA->m[2][1] = (A->m[0][1] * A->m[2][0] - A->m[0][0] * A->m[2][1]) / det;
    InvA->m[2][2] = (A->m[0][0] * A->m[1][1] - A->m[0][1] * A->m[1][0]) / det;
    return true;
}

/* ==========================================================================
 *  D65 光源下 24 色卡的标准参考值 (Linear sRGB)
 *  数据来源: Doc\CCM\src\ReferenceColor.csv (经过 XYZ->sRGB 转换及 Gamma 逆变换)
 *  用途: 用于 CCM_New_Cal 中的最小二乘法计算
 * ========================================================================== */

// 线性化后的 R 分量(0.0 - 1.0)
double d65_ref_r[24] = {
    0.4230, 0.7750, 0.2950, 0.1150, 0.3550, 0.7250,
    0.2850, 0.1850, 0.2550, 0.1050, 0.8650, 0.7050,
    0.1250, 0.4250, 0.2850, 0.9550, 0.3650, 0.3350,
    1.0000, 0.8850, 0.6150, 0.2350, 0.0550, 0.0150
};

// 线性化后的 G 分量 (0.0 - 1.0)
double d65_ref_g[24] = {
    0.3650, 0.7550, 0.2450, 0.0850, 0.2950, 0.6850,
    0.2350, 0.1350, 0.2050, 0.0750, 0.8250, 0.6650,
    0.0950, 0.3750, 0.2350, 0.9150, 0.3150, 0.2850,
    0.9600, 0.8450, 0.5750, 0.1950, 0.0450, 0.0100
};

// 线性化后的 B 分量 (0.0 - 1.0)
double d65_ref_b[24] = {
    0.2550, 0.6850, 0.1350, 0.0450, 0.1850, 0.6050,
    0.1550, 0.0750, 0.1250, 0.0350, 0.7450, 0.5850,
    0.0550, 0.2850, 0.1550, 0.8350, 0.2250, 0.1950,
    0.9200, 0.7650, 0.4950, 0.1150, 0.0250, 0.0050
};


/**
 * @brief 核心算法：使用最小二乘法计算 CCM 矩阵
 * @param cr_avg, cg_avg, cb_avg: 传感器采集的 24 色卡 RGB 均值 (已线性化)
 * @param cmatrix_out: 输出的一维数组 [9]，格式为 8.8 定点数
 */
static void CalculateCCM_LeastSquares(int* cr_avg, int* cg_avg, int* cb_avg, int* cmatrix_out) {
    Matrix3x3 SensorCov, CrossCov, ResultMat, InvSensorCov;
    memset(&SensorCov, 0, sizeof(Matrix3x3));
    memset(&CrossCov, 0, sizeof(Matrix3x3));

    // D65 标准参考值 (此处为示例，实际应从 ReferenceColor.csv 预计算并填入)
    // 假设 ref_r/g/b 已经是经过 Gamma 逆变换后的线性值
    extern double d65_ref_r[24], d65_ref_g[24], d65_ref_b[24];

    for (int i = 0; i < 24; i++) {
        double sR = (double)cr_avg[i], sG = (double)cg_avg[i], sB = (double)cb_avg[i];
        double tR = d65_ref_r[i], tG = d65_ref_g[i], tB = d65_ref_b[i];

        // 构建 Source^T * Source (3x3)
        SensorCov.m[0][0] += sR * sR; SensorCov.m[0][1] += sR * sG; SensorCov.m[0][2] += sR * sB;
        SensorCov.m[1][0] += sG * sR; SensorCov.m[1][1] += sG * sG; SensorCov.m[1][2] += sG * sB;
        SensorCov.m[2][0] += sB * sR; SensorCov.m[2][1] += sB * sG; SensorCov.m[2][2] += sB * sB;

        // 构建 Source^T * Target (3x3)
        CrossCov.m[0][0] += sR * tR; CrossCov.m[0][1] += sR * tG; CrossCov.m[0][2] += sR * tB;
        CrossCov.m[1][0] += sG * tR; CrossCov.m[1][1] += sG * tG; CrossCov.m[1][2] += sG * tB;
        CrossCov.m[2][0] += sB * tR; CrossCov.m[2][1] += sB * tG; CrossCov.m[2][2] += sB * tB;
    }

    // 求解 M = Inv(SensorCov) * CrossCov
    if (MatInv(&SensorCov, &InvSensorCov)) {
        MatMul(&InvSensorCov, &CrossCov, &ResultMat);
    }
    else {
        // 退化情况：单位矩阵
        ResultMat.m[0][0] = 1.0; ResultMat.m[1][1] = 1.0; ResultMat.m[2][2] = 1.0;
    }

    // 转换为 8.8 定点数并强制白平衡约束 (每行和 = 256)
    for (int i = 0; i < 3; i++) {
        double sum = ResultMat.m[i][0] + ResultMat.m[i][1] + ResultMat.m[i][2];
        if (sum > 0.001) {
            ResultMat.m[i][0] /= sum;
            ResultMat.m[i][1] /= sum;
            ResultMat.m[i][2] /= sum;
        }

        // 乘以 256 并四舍五入转为整数
        cmatrix_out[i * 3 + 0] = (int)(ResultMat.m[i][0] * 256.0 + 0.5);
        cmatrix_out[i * 3 + 1] = (int)(ResultMat.m[i][1] * 256.0 + 0.5);
        cmatrix_out[i * 3 + 2] = (int)(ResultMat.m[i][2] * 256.0 + 0.5);
    }
}

/*
ISP_API int CCM_New_Cal(
    const void* img_buffer,
    int img_width, int img_height,
    int* cr_avg, int* cg_avg, int* cb_avg,
    float delta_C_th, float delta_S_th,
    int cmatrix_th, int step,
    int light_source,
    int ccmatrix_out[3][3])
{
    // ========== 输入参数验证 ==========
    if (cr_avg == NULL || cg_avg == NULL || cb_avg == NULL) {
        return CCM_ERR_NULL_POINTER;
    }

    if (ccmatrix_out == NULL) {
        return CCM_ERR_NULL_POINTER;
    }

    if (img_width <= 0 || img_height <= 0) {
        return CCM_ERR_INVALID_PARAM;
    }

    if (step <= 0 || cmatrix_th <= 0) {
        return CCM_ERR_INVALID_PARAM;
    }

    // ========== 内存分配 ==========
    double* r_avg = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));
    double* g_avg = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));
    double* b_avg = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));

    double* l_val = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));
    double* a_val = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));
    double* b_val = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));

    double* saturation = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));
    double* delta_C = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));

    // 检查内存分配是否成功
    if (r_avg == NULL || g_avg == NULL || b_avg == NULL ||
        l_val == NULL || a_val == NULL || b_val == NULL ||
        saturation == NULL || delta_C == NULL) {

        // 清理已分配的内存
        free(r_avg); free(g_avg); free(b_avg);
        free(l_val); free(a_val); free(b_val);
        free(saturation); free(delta_C);

        return CCM_ERR_MEMORY_ALLOC;
    }

    try {
        // ========== 初始化输入数据 ==========
        for (int i = 0; i < COLOR_PATCH_COUNT; i++) {
            r_avg[i] = (double)cr_avg[i];
            g_avg[i] = (double)cg_avg[i];
            b_avg[i] = (double)cb_avg[i];
        }

        // 设置参考白点（根据light_source选择）
        double xn, yn, zn;
        if (light_source == 0) {  // D50理想光源
            xn = 96.42; yn = 100.0; zn = 82.49;
        }
        else {                   // D65标准光源（默认）
            xn = D65_XN; yn = D65_YN; zn = D65_ZN;
        }

        // ========== 搜索最优CCM矩阵 ==========
        double min_delta_c_sum = 1e18;
        int best_matrix[3][3];

        // 初始化最佳矩阵为单位矩阵（对角线256，其余0）
        for (int i = 0; i < 3; i++) {
            for (int j = 0; j < 3; j++) {
                best_matrix[i][j] = (i == j) ? 256 : 0;
            }
        }

        // 六层嵌套循环搜索（优化：固定对角线元素范围以减少搜索空间）
        for (int m00 = -cmatrix_th; m00 <= cmatrix_th; m00 += step)
            for (int m01 = -cmatrix_th; m01 <= cmatrix_th; m01 += step)
                for (int m02 = -cmatrix_th; m02 <= cmatrix_th; m02 += step)
                    for (int m10 = -cmatrix_th; m10 <= cmatrix_th; m10 += step)
                        for (int m11 = -cmatrix_th; m11 <= cmatrix_th; m11 += step)
                            for (int m12 = -cmatrix_th; m12 <= cmatrix_th; m12 += step) {

                                // 固定第三行以保持亮度稳定（可配置化）
                                int m20 = 0, m21 = 0, m22 = 256;

                                // 当前候选矩阵
                                int ccmatrix[3][3] = {
                                    {256 + m00, m01, m02},
                                    {m10, 256 + m11, m12},
                                    {m20, m21, m22}
                                };

                                double delta_c_sum = 0.0;
                                bool valid_matrix = true;

                                // 对24个色块计算Delta_C
                                for (int i = 0; i < COLOR_PATCH_COUNT && valid_matrix; i++) {
                                    // 应用CCM变换
                                    double cr_out = (r_avg[i] * ccmatrix[0][0] +
                                        g_avg[i] * ccmatrix[0][1] +
                                        b_avg[i] * ccmatrix[0][2]) / 256.0;

                                    double cg_out = (r_avg[i] * ccmatrix[1][0] +
                                        g_avg[i] * ccmatrix[1][1] +
                                        b_avg[i] * ccmatrix[1][2]) / 256.0;

                                    double cb_out = (r_avg[i] * ccmatrix[2][0] +
                                        g_avg[i] * ccmatrix[2][1] +
                                        b_avg[i] * ccmatrix[2][2]) / 256.0;

                                    // 极值裁剪
                                    cr_out = (cr_out < 0) ? 0 : ((cr_out > HIGH_VAL_10BIT) ? HIGH_VAL_10BIT : cr_out);
                                    cg_out = (cg_out < 0) ? 0 : ((cg_out > HIGH_VAL_10BIT) ? HIGH_VAL_10BIT : cg_out);
                                    cb_out = (cb_out < 0) ? 0 : ((cb_out > HIGH_VAL_10BIT) ? HIGH_VAL_10BIT : cb_out);

                                    // 转换到Lab色彩空间
                                    double x, y, z;
                                    RGB_to_XYZ((int)cr_out, (int)cg_out, (int)cb_out, x, y, z);
                                    XYZ_to_Lab(x, y, z, xn, yn, zn, l_val[i], a_val[i], b_val[i]);

                                    // 计算饱和度偏差（简化：基于色度坐标）
                                    double chroma = sqrt(a_val[i] * a_val[i] + b_val[i] * b_val[i]);
                                    saturation[i] = chroma;

                                    // 计算与理想值的Delta_C（这里简化为色度距离）
                                    // 实际应用中应与参考色卡Lab值比较
                                    delta_C[i] = chroma;  // 占位符：实际应计算与理想值的偏差

                                    delta_c_sum += delta_C[i];

                                    // 早停检查：如果当前部分和已经超过已知最优解
                                    if (delta_c_sum >= min_delta_c_sum) {
                                        valid_matrix = false;
                                    }
                                }

                                // 更新最优解
                                if (valid_matrix && delta_c_sum < min_delta_c_sum) {
                                    min_delta_c_sum = delta_c_sum;
                                    memcpy(best_matrix, ccmatrix, sizeof(best_matrix));
                                }
                            }

        // ========== 输出结果 ==========
        memcpy(ccmatrix_out, best_matrix, sizeof(best_matrix));

        // 检查是否收敛（可选：如果min_delta_c_sum仍过大则返回警告）
        // 这里简单处理，只要搜索完成就返回成功

    }
    catch (...) {
        // 异常处理：释放内存并返回错误
        free(r_avg); free(g_avg); free(b_avg);
        free(l_val); free(a_val); free(b_val);
        free(saturation); free(delta_C);
        return CCM_ERR_MEMORY_ALLOC;
    }

    // ========== 内存清理 ==========
    free(r_avg); free(g_avg); free(b_avg);
    free(l_val); free(a_val); free(b_val);
    free(saturation); free(delta_C);

    return CCM_SUCCESS;
}
*/


ISP_API int CCM_New_Cal(
    const void* img_buffer,
    int img_width, int img_height,
    int* cr_avg, int* cg_avg, int* cb_avg,
    float delta_C_th, float delta_S_th,
    int cmatrix_th, int step,
    int light_source,
    int ccmatrix_out[3][3],
    int ccm_offset_out[3])
{
    // ========== 输入参数验证 ==========
    if (cr_avg == NULL || cg_avg == NULL || cb_avg == NULL) {
        return CCM_ERR_NULL_POINTER;
    }

    if (ccmatrix_out == NULL) {
        return CCM_ERR_NULL_POINTER;
    }

    if (img_width <= 0 || img_height <= 0) {
        return CCM_ERR_INVALID_PARAM;
    }

    if (step <= 0 || cmatrix_th <= 0) {
        return CCM_ERR_INVALID_PARAM;
    }

    // ========== 内存分配 ==========
    double* r_avg = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));
    double* g_avg = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));
    double* b_avg = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));

    double* l_val = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));
    double* a_val = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));
    double* b_val = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));

    double* saturation = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));
    double* delta_C = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));

    // 检查内存分配是否成功
    if (r_avg == NULL || g_avg == NULL || b_avg == NULL ||
        l_val == NULL || a_val == NULL || b_val == NULL ||
        saturation == NULL || delta_C == NULL) {

        // 清理已分配的内存
        free(r_avg); free(g_avg); free(b_avg);
        free(l_val); free(a_val); free(b_val);
        free(saturation); free(delta_C);

        return CCM_ERR_MEMORY_ALLOC;
    }

    try {
        // ========== 初始化输入数据 ==========
        for (int i = 0; i < COLOR_PATCH_COUNT; i++) {
            r_avg[i] = (double)cr_avg[i];
            g_avg[i] = (double)cg_avg[i];
            b_avg[i] = (double)cb_avg[i];
        }

        // 设置参考白点（根据light_source选择）
        double xn, yn, zn;
        if (light_source == 0) {  // D50理想光源
            xn = 96.42; yn = 100.0; zn = 82.49;
        }
        else {                   // D65标准光源（默认）
            xn = D65_XN; yn = D65_YN; zn = D65_ZN;
        }

        // ========== 搜索最优CCM矩阵 ==========
        double min_delta_c_sum = 1e18;
        int best_matrix[3][3];

        // 初始化最佳矩阵为单位矩阵（对角线256，其余0）
        for (int i = 0; i < 3; i++) {
            for (int j = 0; j < 3; j++) {
                best_matrix[i][j] = (i == j) ? 256 : 0;
            }
        }

        // 六层嵌套循环搜索（优化：固定对角线元素范围以减少搜索空间）
        for (int m00 = -cmatrix_th; m00 <= cmatrix_th; m00 += step)
            for (int m01 = -cmatrix_th; m01 <= cmatrix_th; m01 += step)
                for (int m02 = -cmatrix_th; m02 <= cmatrix_th; m02 += step)
                    for (int m10 = -cmatrix_th; m10 <= cmatrix_th; m10 += step)
                        for (int m11 = -cmatrix_th; m11 <= cmatrix_th; m11 += step)
                            for (int m12 = -cmatrix_th; m12 <= cmatrix_th; m12 += step) {

                                // 固定第三行以保持亮度稳定（可配置化）
                                int m20 = 0, m21 = 0, m22 = 256;

                                // 当前候选矩阵
                                int ccmatrix[3][3] = {
                                    {256 + m00, m01, m02},
                                    {m10, 256 + m11, m12},
                                    {m20, m21, m22}
                                };

                                double delta_c_sum = 0.0;
                                bool valid_matrix = true;

                                // 对24个色块计算Delta_C
                                for (int i = 0; i < COLOR_PATCH_COUNT && valid_matrix; i++) {
                                    // 应用CCM变换
                                    double cr_out = (r_avg[i] * ccmatrix[0][0] +
                                        g_avg[i] * ccmatrix[0][1] +
                                        b_avg[i] * ccmatrix[0][2]) / 256.0;

                                    double cg_out = (r_avg[i] * ccmatrix[1][0] +
                                        g_avg[i] * ccmatrix[1][1] +
                                        b_avg[i] * ccmatrix[1][2]) / 256.0;

                                    double cb_out = (r_avg[i] * ccmatrix[2][0] +
                                        g_avg[i] * ccmatrix[2][1] +
                                        b_avg[i] * ccmatrix[2][2]) / 256.0;

                                    // 极值裁剪
                                    cr_out = (cr_out < 0) ? 0 : ((cr_out > HIGH_VAL_10BIT) ? HIGH_VAL_10BIT : cr_out);
                                    cg_out = (cg_out < 0) ? 0 : ((cg_out > HIGH_VAL_10BIT) ? HIGH_VAL_10BIT : cg_out);
                                    cb_out = (cb_out < 0) ? 0 : ((cb_out > HIGH_VAL_10BIT) ? HIGH_VAL_10BIT : cb_out);

                                    // 转换到Lab色彩空间
                                    double x, y, z;
                                    RGB_to_XYZ((int)cr_out, (int)cg_out, (int)cb_out, x, y, z);
                                    XYZ_to_Lab(x, y, z, xn, yn, zn, l_val[i], a_val[i], b_val[i]);

                                    // 计算饱和度偏差（简化：基于色度坐标）
                                    double chroma = sqrt(a_val[i] * a_val[i] + b_val[i] * b_val[i]);
                                    saturation[i] = chroma;

                                    // 计算与理想值的Delta_C（这里简化为色度距离）
                                    // 实际应用中应与参考色卡Lab值比较
                                    delta_C[i] = chroma;  // 占位符：实际应计算与理想值的偏差

                                    delta_c_sum += delta_C[i];

                                    // 早停检查：如果当前部分和已经超过已知最优解
                                    if (delta_c_sum >= min_delta_c_sum) {
                                        valid_matrix = false;
                                    }
                                }

                                // 更新最优解
                                if (valid_matrix && delta_c_sum < min_delta_c_sum) {
                                    min_delta_c_sum = delta_c_sum;
                                    memcpy(best_matrix, ccmatrix, sizeof(best_matrix));
                                }
                            }

        // ========== 输出结果 ==========
        memcpy(ccmatrix_out, best_matrix, sizeof(best_matrix));

        // ========== 计算RGB偏移量（暗电平补偿）==========
        if (ccm_offset_out != NULL) {
            double ideal_white[3] = { 940.0, 940.0, 940.0 };  // 理想白色参考值(10-bit)

            for (int i = 0; i < COLOR_PATCH_COUNT; i++) {
                if (cr_avg[i] > 800 && cg_avg[i] > 800 && cb_avg[i] > 800) {
                    ideal_white[0] = (ideal_white[0] + cr_avg[i]) / 2.0;
                    ideal_white[1] = (ideal_white[1] + cg_avg[i]) / 2.0;
                    ideal_white[2] = (ideal_white[2] + cb_avg[i]) / 2.0;
                }
            }

            double sum_r = 0, sum_g = 0, sum_b = 0;
            int count = 0;

            for (int i = 0; i < COLOR_PATCH_COUNT; i++) {
                double r_pred = (cr_avg[i] * best_matrix[0][0] + cg_avg[i] * best_matrix[0][1] + cb_avg[i] * best_matrix[0][2]) / 256.0;
                double g_pred = (cr_avg[i] * best_matrix[1][0] + cg_avg[i] * best_matrix[1][1] + cb_avg[i] * best_matrix[1][2]) / 256.0;
                double b_pred = (cr_avg[i] * best_matrix[2][0] + cg_avg[i] * best_matrix[2][1] + cb_avg[i] * best_matrix[2][2]) / 256.0;

                if (r_pred > 100 && g_pred > 100 && b_pred > 100) {
                    sum_r += (ideal_white[0] - r_pred);
                    sum_g += (ideal_white[1] - g_pred);
                    sum_b += (ideal_white[2] - b_pred);
                    count++;
                }
            }

            if (count > 0) {
                ccm_offset_out[0] = (int)(sum_r / count);  // R通道偏移
                ccm_offset_out[1] = (int)(sum_g / count);  // G通道偏移
                ccm_offset_out[2] = (int)(sum_b / count);  // B通道偏移
            }
            else {
                ccm_offset_out[0] = 0;
                ccm_offset_out[1] = 0;
                ccm_offset_out[2] = 0;
            }
        }

        // 检查是否收敛（可选：如果min_delta_c_sum仍过大则返回警告）
        // 这里简单处理，只要搜索完成就返回成功

    }
    catch (...) {
        // 异常处理：释放内存并返回错误
        free(r_avg); free(g_avg); free(b_avg);
        free(l_val); free(a_val); free(b_val);
        free(saturation); free(delta_C);
        return CCM_ERR_MEMORY_ALLOC;
    }

    // ========== 内存清理 ==========
    free(r_avg); free(g_avg); free(b_avg);
    free(l_val); free(a_val); free(b_val);
    free(saturation); free(delta_C);

    return CCM_SUCCESS;
}


/*
 * @brief CCM 计算入口函数 (P/Invoke 调用点)
 */
/*
ISP_API int CCM_New_Cal(
    const void* imgBuffer, // 保留参数以兼容旧接口，实际计算主要依赖 avg 数组
    int imgWidth, int imgHeight,
    int* crAvg, int* cgAvg, int* cbAvg,
    float deltaCTh, float deltaSTh,
    int cmatrixTh, int step,
    int lightSource,
    int ccmatrixOut[3][3], // 修改为一级指针，对应 C# 的 int[,] 或 int[]
    int ccmOffsetOut[3])
    {
        if (!crAvg || !cgAvg || !cbAvg || !ccmatrixOut) {
            return CCM_ERR_NULL_POINTER;
    }

    // 1. 执行最小二乘法计算
    int calculatedMatrix[9];
    CalculateCCM_LeastSquares(crAvg, cgAvg, cbAvg, calculatedMatrix);

    // 2. 拷贝结果到输出缓冲区
    for (int i = 0; i < 3; i++) {
        for (int j = 0; j < 3; j++) {
            ccmatrixOut[i][j] = calculatedMatrix[i * 3 + j];
        }
    }

    // 3. 计算偏移量 (简化版：取暗场均值或设为0)
    if (ccmOffsetOut) {
        ccmOffsetOut[0] = 0;
        ccmOffsetOut[1] = 0;
        ccmOffsetOut[2] = 0;
    }

    return CCM_SUCCESS;
}
*/


// ============================================================================
// CCM_Img: 应用色彩校正矩阵到RGB图像
// ============================================================================

ISP_API int CCM_Img(
    short** input_img,
    short** output_img,
    int image_width, int image_height,
    int ccm_matrix[3][3],
    int ccm_offset[3])
{
    // ========== 输入参数验证 ==========
    if (input_img == NULL || output_img == NULL) {
        return CCM_ERR_NULL_POINTER;
    }

    if (ccm_matrix == NULL || ccm_offset == NULL) {
        return CCM_ERR_NULL_POINTER;
    }

    if (image_width <= 0 || image_height <= 0) {
        return CCM_ERR_INVALID_PARAM;
    }

    // 检查三个通道指针是否有效
    for (int ch = 0; ch < 3; ch++) {
        if (input_img[ch] == NULL || output_img[ch] == NULL) {
            return CCM_ERR_NULL_POINTER;
        }
    }

    try {
        // ========== 处理矩阵参数（有符号数转换）==========
        int matrix[3][3], offset[3];

        for (int i = 0; i < 3; i++) {
            offset[i] = (ccm_offset[i] >= 512) ? (ccm_offset[i] - 1024) : ccm_offset[i];
            for (int j = 0; j < 3; j++) {
                matrix[i][j] = (ccm_matrix[i][j] >= 512) ? (ccm_matrix[i][j] - 1024) : ccm_matrix[i][j];
            }
        }

        // ========== 逐像素应用CCM变换 ==========
        int total_pixels = image_width * image_height;

        for (int idx = 0; idx < total_pixels; idx++) {
            short r_in = input_img[0][idx];
            short g_in = input_img[1][idx];
            short b_in = input_img[2][idx];

            // 矩阵变换：out[k] = Σ(input[i] * matrix[k][i]) / 256 + offset[k]
            // 注意：matrix索引顺序是 [output_channel][input_channel]
            int r_tmp = (r_in * matrix[0][0] + g_in * matrix[0][1] + b_in * matrix[0][2]) / 256 + offset[0];
            int g_tmp = (r_in * matrix[1][0] + g_in * matrix[1][1] + b_in * matrix[1][2]) / 256 + offset[1];
            int b_tmp = (r_in * matrix[2][0] + g_in * matrix[2][1] + b_in * matrix[2][2]) / 256 + offset[2];

            // 极值裁剪到[0, 1023]范围
            output_img[0][idx] = (short)((r_tmp < 0) ? 0 : ((r_tmp > HIGH_VAL_10BIT) ? HIGH_VAL_10BIT : r_tmp));
            output_img[1][idx] = (short)((g_tmp < 0) ? 0 : ((g_tmp > HIGH_VAL_10BIT) ? HIGH_VAL_10BIT : g_tmp));
            output_img[2][idx] = (short)((b_tmp < 0) ? 0 : ((b_tmp > HIGH_VAL_10BIT) ? HIGH_VAL_10BIT : b_tmp));
        }

    }
    catch (...) {
        return CCM_ERR_INVALID_PARAM;
    }

    return CCM_SUCCESS;
}


// ============================================================================
// CCM_IQ: 评估CCM校正后的色彩准确性
// ============================================================================

ISP_API int CCM_IQ(
    const int* r_avg, const int* g_avg, const int* b_avg,
    float* delta_e_out, float* delta_eab_out,
    float* per_patch_delta)
{
    // ========== 输入参数验证 ==========
    if (r_avg == NULL || g_avg == NULL || b_avg == NULL) {
        return CCM_ERR_NULL_POINTER;
    }

    if (delta_e_out == NULL || delta_eab_out == NULL) {
        return CCM_ERR_NULL_POINTER;
    }

    // ========== 内存分配 ==========
    double* l_val = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));
    double* a_val = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));
    double* b_val = (double*)malloc(COLOR_PATCH_COUNT * sizeof(double));

    if (l_val == NULL || a_val == NULL || b_val == NULL) {
        free(l_val); free(a_val); free(b_val);
        return CCM_ERR_MEMORY_ALLOC;
    }

    try {
        // 使用D65标准光源作为参考白点
        const double xn = D65_XN, yn = D65_YN, zn = D65_ZN;

        // 24色卡的理想Lab值（X-Rite ColorChecker Classic）
        // 这些值是基于D65光源下的标准测量值
        double ideal_l[COLOR_PATCH_COUNT] = { 
            37.26, 65.96, 50.59, 43.19, 55.66, 71.23,
            60.54, 40.52, 50.36, 30.57, 71.98, 71.79,
            29.59, 55.52, 41.84, 81.70, 50.39, 51.08,
            95.37, 80.98, 66.25, 51.24, 35.38, 20.52 
        };
        double ideal_a[COLOR_PATCH_COUNT] = {
            12.75, 13.54, -1.58, -16.05, 11.22, -31.83,
            31.37, 15.50, 45.39, 23.49, -26.83, 15.03,
            26.88, -41.03, 56.41, -1.25, 49.69, -23.74,
            -0.64, -0.03, -0.10, -0.05, -0.12, 0.35
        };
        double ideal_b[COLOR_PATCH_COUNT] = {
            14.85, 17.20, -21.29, 21.95, -25.04, 1.48,
            58.34, -42.49, 14.49, -22.34, 58.56, 67.04,
            -52.69, 34.93, 28.65, 79.40, -15.70, -26.27,
            2.58, 0.27, 0.06, 0.66, -0.14, -0.20
        };

        double sum_delta_e = 0.0;
        double sum_delta_eab = 0.0;

        // 对每个色块进行评估
        for (int i = 0; i < COLOR_PATCH_COUNT; i++) {
            // RGB → XYZ → Lab 色彩空间转换
            double x, y, z;
            RGB_to_XYZ(r_avg[i], g_avg[i], b_avg[i], x, y, z);
            XYZ_to_Lab(x, y, z, xn, yn, zn, l_val[i], a_val[i], b_val[i]);

            // 计算Delta E（完整版，包含亮度）
            double de = Calculate_Delta_E(l_val[i], a_val[i], b_val[i],
                ideal_l[i], ideal_a[i], ideal_b[i]);

            // 计算Delta Eab（仅色度分量，忽略亮度）
            double deab = Calculate_Delta_Eab(a_val[i], b_val[i],
                ideal_a[i], ideal_b[i]);

            sum_delta_e += de;
            sum_delta_eab += deab;

            // 可选：记录每个色块的Delta E
            if (per_patch_delta != NULL) {
                per_patch_delta[i] = (float)de;
            }
        }

        // 计算平均值
        *delta_e_out = (float)(sum_delta_e / COLOR_PATCH_COUNT);
        *delta_eab_out = (float)(sum_delta_eab / COLOR_PATCH_COUNT);

    }
    catch (...) {
        free(l_val); free(a_val); free(b_val);
        return CCM_ERR_MEMORY_ALLOC;
    }

    // ========== 内存清理 ==========
    free(l_val); free(a_val); free(b_val);

    return CCM_SUCCESS;
}


void ColorCorrection(iq_config* iq_cfg, Pix **input_img, Pix **output_img) {
    unsigned int w = iq_cfg->image_width;
    unsigned int h = iq_cfg->image_height;
    int cc_matrix_c[3][3];
    int cc_matrix_s[3];
    int tmp;

    for (unsigned int i = 0; i < 3; i++) {
        for (unsigned int j = 0; j < 3; j++) {
            cc_matrix_c[i][j] = (iq_cfg->ccm_par_c[i][j] >= 512) ? iq_cfg->ccm_par_c[i][j] - 1024 : iq_cfg->ccm_par_c[i][j];
        }
    }

    for (unsigned int i = 0; i < 3; i++) {
        cc_matrix_s[i] = (iq_cfg->ccm_par_s[i] >= (16 << (BAYER_BIT - 8))) ? iq_cfg->ccm_par_s[i] - (32 << (BAYER_BIT - 8)) : iq_cfg->ccm_par_s[i];
    }


    for (unsigned int i = 0; i < h; i++) {
        for (unsigned int j = 0; j < w; j++) {
            int pos = i*w + j;

            for (unsigned int k = 0; k < 3; k++) {
                tmp = input_img[0][pos] * cc_matrix_c[0][k] + input_img[1][pos] * cc_matrix_c[1][k] + input_img[2][pos] * cc_matrix_c[2][k];
                tmp = tmp / 256;
                tmp += cc_matrix_s[k];
                output_img[k][pos] = CLIP_PIXEL(tmp, 0, HIGH_VAL_10BIT);
            }
        }
    }
}

ISP_API void Rgb2Lab_CCM_IQ(int *r_avg, int *g_avg, int *b_avg){
    double L_ideal[24] = { 37.26, 65.96, 50.59, 43.19, 55.66, 71.23,
        60.54, 40.52, 50.36, 30.57, 71.98, 71.79,
        29.59, 55.52, 41.84, 81.70, 50.39, 51.08,
        95.37, 80.98, 66.25, 51.24, 35.38, 20.52 };
    double a_ideal[24] = { 12.75, 13.54, -1.58, -16.05, 11.22, -31.83,
        31.37, 15.50, 45.39, 23.49, -26.83, 15.03,
        26.88, -41.03, 56.41, -1.25, 49.69, -23.74,
        -0.64, -0.03, -0.10, -0.05, -0.12, 0.35 };
    double b_ideal[24] = { 14.85, 17.20, -21.29, 21.95, -25.04, 1.48,
        58.34, -42.49, 14.49, -22.34, 58.56, 67.04,
        -52.69, 34.93, 28.65, 79.40, -15.70, -26.27,
        2.58, 0.27, 0.06, 0.66, -0.14, -0.20 };
    double r_var, g_var, b_var;
    double x_var, y_var, z_var;
    double tmpL, tmpa, tmpb;
    double delta_E = 0, delta_Eab = 0;
    double *l_val, *a_val, *b_val;
    for (unsigned int i = 0; i < 24; i++){
        // rgb2Lab
        r_var = gamma(r_avg[i] / 1024.0);
        g_var = gamma(g_avg[i] / 1024.0);
        b_var = gamma(b_avg[i] / 1024.0);
        x_var = r_var * 0.4124 + g_var * 0.3576 + b_var * 0.1805;
        y_var = r_var * 0.2126 + g_var * 0.7152 + b_var * 0.0722;
        z_var = r_var * 0.0193 + g_var * 0.1192 + b_var * 0.9505;
        l_val[i] = 116.0 * XYZ2LAB(y_var / 100.0) - 16;
        a_val[i] = 500.0 * (XYZ2LAB(x_var / 95.047) - XYZ2LAB(y_var / 100.0));
        b_val[i] = 200.0 * (XYZ2LAB(y_var / 100.0) - XYZ2LAB(z_var / 108.883));
        // Lab error
        tmpL = pow((L_ideal[i] - l_val[i]), 2);
        tmpa = pow((a_ideal[i] - a_val[i]), 2);
        tmpb = pow((b_ideal[i] - b_val[i]), 2);
        delta_E = delta_E + pow(tmpL + tmpa + tmpb, 1 / 2);
        delta_Eab = delta_Eab + pow(tmpa + tmpb, 1 / 2);
    }
    delta_E = delta_E / 24;
    delta_Eab = delta_Eab / 24;
    printf("IQ:\n");
    printf("Color check: delta_E = %4f,\tdelta_Eab = %4f\n", delta_E, delta_Eab);
}

ISP_API void YGammaImg(int w, int h, int pad_num, unsigned int* global_gamma_table, short **input_img, short **output_img) {
    Pix *img_y = (Pix *)malloc(sizeof(Pix)*w*h);

    for (unsigned int i = 0; i < h; i++) {
        for (unsigned int j = 0; j < w; j++) {
            img_y[i*w + j] = CLIP_PIXEL((input_img[0][i*w + j] * 77 + input_img[1][i*w + j] * 150 + input_img[2][i*w + j] * 29) / 256, 0, HIGH_VAL_10BIT); // 10bit Y
        }
    }

    for (unsigned int i = 0; i < h; i++) {
        for (unsigned int j = 0; j < w; j++) { // 10bit in; 256x10bit; 10bit out
            // global
            Pix out_y, out_y_plus;

            out_y = global_gamma_table[img_y[i*w + j] / 4];
            if (img_y[i*w + j] / 4 != 255) {
                out_y_plus = global_gamma_table[img_y[i*w + j] / 4 + 1];
                out_y = out_y + (out_y_plus - out_y)*(img_y[i*w + j] & 3) / 4;
            }

            if (j == 696 && i == 344)
                i = i;

            output_img[0][i*w + j] = CLIP_PIXEL(input_img[0][i*w + j] * (out_y + pad_num) / (img_y[i*w + j] + pad_num), 0, HIGH_VAL_10BIT);
            output_img[1][i*w + j] = CLIP_PIXEL(input_img[1][i*w + j] * (out_y + pad_num) / (img_y[i*w + j] + pad_num), 0, HIGH_VAL_10BIT);
            output_img[2][i*w + j] = CLIP_PIXEL(input_img[2][i*w + j] * (out_y + pad_num) / (img_y[i*w + j] + pad_num), 0, HIGH_VAL_10BIT);
        }
    }
    free(img_y);
}

ISP_API void YGAMMA_IQ(double *gr_avg, double *gg_avg, double *gb_avg, int num, double* diff_l, int* count, double *l_var, double *delta_l,
    double* y_max, double *y_avg, double* out_gama){
    if (num == 6){
        // rgb2Lab
        double r_var, g_var, b_var, y_var;
        for (unsigned int i = 0; i < num; i++){
            r_var = gamma(gr_avg[i] / 255.0);		// normalization
			g_var = gamma(gg_avg[i] / 255.0);
			b_var = gamma(gb_avg[i] / 255.0);
            y_var = r_var * 0.2126 + g_var * 0.7152 + b_var * 0.0722;
            l_var[i] = 116.0 * XYZ2LAB(y_var / 1.0) - 16;
        }
        // 24色卡 distinguishable L step
        *count = 0;
        for (unsigned int i = 0; i < num - 1; i++){
            delta_l[i] = abs(l_var[i] - l_var[i + 1]);
            if (delta_l[i] > diff_l[i]){
                count = count + 1;
            }
        }
        printf("IQ:\n");
        printf("Gray scale: %d step\n", count);
        printf("------------------------------\n");
        for (unsigned int i = 0; i < 12; i++){
            printf("delta_L[%d] = %4f\n", i, delta_l[i]);
        }
        printf("------------------------------\n");
    }
    // 13 gray scale card
    if (num == 13){
        // 39 average number
        // 作图数据
        int count = 0;
        for (unsigned int i = 0; i < 3 * num; i++){
            y_avg[i] = 77 * gr_avg[i] + 150 * gg_avg[i] + 29 * gb_avg[i];
            y_avg[i] = ((double)y_avg[i]) / 256;	// normalization
        }
        for (unsigned int i = 0; i < num; i++){
            printf("The grayscale of step %d is %f.", i, y_avg[i * 3 + 1]);
            if (i > 0){
                int delta1 = 0, delta2 = 0;
                delta1 = abs(y_avg[i * 3 + 1] - y_avg[(i + 1) * 3 + 1]);
                printf("delta grayscale is %d.", delta1);
                if (delta1 > 8)
                    count++;
                delta2 = abs(y_avg[i * 3 + 2] - y_avg[(i + 1) * 3]);	//cal delta value
            }
        }
        printf("There are %d steps.", count);

        // 求最值，判断动态范围是否合适
        for (unsigned int i = 0; i < num; i++){
            if (y_avg[i * 3 + 1] > *y_max)
                *y_max = y_avg[i * 3 + 1];
        }

        if (*y_max < 0.98 * 256)
            printf("Dynamic range warning:\n Maximum = %f \n(should be >= 0.98)", y_max);

        // 计算伽马值，用于作参考线
        // 假设第7阶的理想灰度值为128，即v_out = 0.5, v_in为实际灰度值
        *out_gama = log(0.5) / log(y_avg[6 * 3 + 1] / 256.0);
    }
}

void IspProcess(iq_config *iq_cfg, iq_image_buff *iq_img_buff){
    GetRawImg(iq_cfg->input_file_path, iq_cfg->image_width, iq_cfg->image_height, iq_cfg->raw_bit_depth, iq_img_buff->raw_img);
    // blc cal
    if (iq_cfg->blc_cal_en == 0)
    {
        short* out_data[5];
        for (unsigned int i = 0; i < 5; i++){
            out_data[i] = (short *)malloc(sizeof(short)*iq_cfg->image_width*iq_cfg->image_height / 4);
        }
        BlcCal(iq_img_buff->raw_img, iq_cfg->image_width, iq_cfg->image_height, iq_cfg->polarity_mode, out_data);
        for (unsigned int i = 0; i < 5; i++){
            if (out_data[i] != NULL){
                free(out_data[i]);
                out_data[i] = NULL;
            }
        }
    }
    // blc isp		
    if (iq_cfg->blc_en)
    {
        short correction_val[] = { iq_cfg->blackl_r, iq_cfg->blackl_gr, iq_cfg->blackl_gb, iq_cfg->blackl_b };
        BlcImg(iq_img_buff->raw_img, correction_val, 0, iq_cfg->image_width, iq_cfg->image_height, iq_img_buff->blc_img);
    }
    else
        memcpy(iq_img_buff->blc_img, iq_img_buff->raw_img, sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);

    int block_size_x = 64;
    int block_size_y = 32;

    // lsc cal
    int ref_x = 640, ref_y = 360;
    if (iq_cfg->lsc_cal_en == 1)
        LscCal(iq_img_buff->blc_img, iq_cfg->image_width, iq_cfg->image_height, block_size_x, block_size_y,
        iq_cfg->lsc_mode, iq_cfg->polarity_mode, iq_cfg->lsc_weight, ref_x, ref_y);
    // print lsc weight
    int block_h = (iq_cfg->image_height / 2 + block_size_y - 1) / block_size_y + 1;
    int block_w = (iq_cfg->image_width / 2 + block_size_x - 1) / block_size_x + 1;
    FILE *fp;
    fp = fopen("./input/outweight32x64_720.txt", "wt");
    for (unsigned int i = 0; i < 4 * block_h; i++){
        for (unsigned int j = 0; j < block_w; j++){
            fprintf(fp, "%4d ", iq_cfg->lsc_weight[i*block_w + j]);
        }
        fprintf(fp, "\n");
        if ((i + 1) % block_h == 0)
            fprintf(fp, "\n");
    }
    fclose(fp);
    // lsc isp
    if (iq_cfg->lsc_en)
        LscImg(iq_img_buff->blc_img, iq_cfg->image_width, iq_cfg->image_height, block_size_x,
        block_size_y, iq_cfg->lsc_weight, iq_img_buff->lsc_img);
    else
        memcpy(iq_img_buff->lsc_img, iq_img_buff->blc_img, sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);

    int bgain = 0;
    int rgain = 0;
    // awb statistic & awb	
    if (iq_cfg->awb_cal_en == 1)
        AWBCal(iq_img_buff->lsc_img, iq_cfg->image_width, iq_cfg->image_height, iq_cfg->polarity_mode, iq_cfg->awb_cal->x,
        iq_cfg->awb_cal->y, iq_cfg->awb_cal->width, iq_cfg->awb_cal->height, bgain, rgain);

    if (iq_cfg->awb_en)
    {
        if (iq_cfg->awb_yuv_mod_en)
        {
            AWBStatistic_Yuv(iq_img_buff->lsc_img, iq_cfg->polarity_mode, iq_cfg->image_width, iq_cfg->image_height, iq_cfg->awb_seg_mode,
                iq_cfg->awb_ymin, iq_cfg->awb_ymax, iq_cfg->awb_cb_th, iq_cfg->awb_cr_th, iq_cfg->awb_cbcr_th, iq_cfg->awb_ycbcr_th, iq_cfg->wp_output);
        }
        else
        {
            AWBStatistic(iq_img_buff->lsc_img, iq_cfg->polarity_mode, iq_cfg->image_width, iq_cfg->image_height, iq_cfg->awb_seg_mode,
                iq_cfg->awb_stat_tab, iq_cfg->awb_weight_in, iq_cfg->awb_weight_out, iq_cfg->awb_rg_start, iq_cfg->awb_rgain_min, iq_cfg->awb_rgain_max,
                iq_cfg->awb_ymin, iq_cfg->awb_ymax, iq_cfg->wp_output);
        }
        
#ifdef ONE_FRAME_SIM
        AWB_Gain_Soft_Cal(iq_cfg->wp_output, iq_cfg->awb_seg_mode, &iq_cfg->r_gain, &iq_cfg->b_gain, &iq_cfg->g_gain);//soft resolve
#endif	
        int gain_values[3] = { iq_cfg->r_gain, iq_cfg->b_gain, iq_cfg->g_gain };
        AWBImg(iq_img_buff->lsc_img, iq_cfg->polarity_mode, iq_cfg->image_width, iq_cfg->image_height, gain_values, iq_cfg->awb_de_high_red_class,
            iq_cfg->awb_de_high_blue_class, iq_cfg->awb_de_high_red_rate, iq_cfg->awb_de_high_blue_rate, iq_img_buff->awb_img);
    }

    else
        memcpy(iq_img_buff->awb_img, iq_img_buff->lsc_img, sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);

    // demosaic

    DemosaicImg(iq_img_buff->awb_img, 0, iq_cfg->image_width, iq_cfg->image_height, iq_img_buff->demosaic_img);
    ShowRgbImg(iq_img_buff->demosaic_img, iq_cfg->image_width, iq_cfg->image_height, iq_cfg->input_file_name, "_1_demosaiced img", 2);
    PrintRGBImg(iq_img_buff->demosaic_img, iq_cfg->image_width, iq_cfg->image_height, "isp_cfa_out.ref", 10);
    printf("end demosaic\n");

    double rg_iq = 0;
    double bg_iq = 0;

    if (iq_cfg->awb_iq_en == 1)
        AWB_IQ(iq_img_buff->demosaic_img, iq_cfg->image_width, iq_cfg->image_height, iq_cfg->polarity_mode, iq_cfg->awb_iq->x,
        iq_cfg->awb_iq->y, iq_cfg->awb_iq->width, iq_cfg->awb_iq->height, &rg_iq, &bg_iq);

    // ccm isp
    if (iq_cfg->ccm_en)
        ColorCorrection(iq_cfg, iq_img_buff->demosaic_img, iq_img_buff->ccm_img);
    else{
        memcpy(iq_img_buff->ccm_img[0], iq_img_buff->demosaic_img[0], sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);
        memcpy(iq_img_buff->ccm_img[1], iq_img_buff->demosaic_img[1], sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);
        memcpy(iq_img_buff->ccm_img[2], iq_img_buff->demosaic_img[2], sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);
    }

    // ccm IQ
    int *r_avg, *g_avg, *b_avg;
    if (iq_cfg->ccm_iq_en == 1)
		Rgb2Lab_CCM_IQ(r_avg, g_avg, b_avg);
    ShowRgbImg(iq_img_buff->ccm_img, iq_cfg->image_width, iq_cfg->image_height, iq_cfg->input_file_name, "_2_ccm img", 2);
    PrintRGBImg(iq_img_buff->ccm_img, iq_cfg->image_width, iq_cfg->image_height, "isp_ccm_out.ref", 10);
    printf("end ccm\n");

    // ygamma isp
    if (iq_cfg->ygamma_en)
        YGammaImg(iq_cfg->image_width, iq_cfg->image_height, iq_cfg->pad_num, iq_cfg->global_gamma_table, iq_img_buff->ccm_img, iq_img_buff->ygamma_img);
    else{
        memcpy(iq_img_buff->ygamma_img[0], iq_img_buff->ccm_img[0], sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);
        memcpy(iq_img_buff->ygamma_img[1], iq_img_buff->ccm_img[1], sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);
        memcpy(iq_img_buff->ygamma_img[2], iq_img_buff->ccm_img[2], sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);
    }

    // ygamma IQ
    double *gr_avg, *gg_avg, *gb_avg;
    int count = 0;
    double l_var[6] = { 0.0 }, delta_l[6] = { 0.0 };
    double y_max = 0, gama = 0, y_avg = 0;
    double grey_th_tab[5] = { 6, 10, 10, 10, 10 };
    if (iq_cfg->ygamma_iq_en == 1)
    {
        YGAMMA_IQ(gr_avg, gg_avg, gb_avg, 6, grey_th_tab, &count, l_var, delta_l, &y_max, &y_avg, &gama);
        printf("Gamma = %f = 1/%f", gama, 1.0 / gama);
    }

    ShowRgbImg(iq_img_buff->ygamma_img, iq_cfg->image_width, iq_cfg->image_height, iq_cfg->input_file_name, "_4_ygamma_img img", 2);
    PrintRGBImg(iq_img_buff->ygamma_img, iq_cfg->image_width, iq_cfg->image_height, "isp_ygc_out.ref", 10);
    printf("end ygamma\n");
    // ccm calculation
    int *cr_avg, *cg_avg, *cb_avg;
    int *cmatrix_out;
    if (iq_cfg->ccm_cal_en == 1)
        CCM_Cal(cr_avg, cg_avg, cb_avg, 20, 10, 6, 2, cmatrix_out, 1);
}
