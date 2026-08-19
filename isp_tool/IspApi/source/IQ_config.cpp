#include "../include/IQ.h"
#include <math.h>
#include "Export.h"

#if  USE_CV
#include "OpenCv/highgui.h"
using namespace cv;  
#endif

using namespace std;

char debug_output_dir[100] = "./output/ref_file/"; 
char output_dir[200] = "./output/out_img/"; 
char img_show_name[200];

void IqConfig(iq_config *iq_cfg, iq_image_buff *iq_img_buff, bool isp_en, bool isp_cal_en) {
	// raw input
	strcpy(iq_cfg->input_file_path, "./RAW/");
	strcpy(iq_cfg->input_file_name, "1409_LSC1.RAW");  //1004-白天-九洲大道-晴天1.raw 1034_BL1.RAW
	strcat(iq_cfg->input_file_path, iq_cfg->input_file_name);
	// common
	iq_cfg->image_width = 1280;
	iq_cfg->image_height = 720;
	iq_cfg->raw_bit_depth = 1;
	iq_cfg->polarity_mode	= 2;
	// enable
	iq_cfg->blc_en = 1;
	iq_cfg->lsc_en = 1;
	iq_cfg->awb_en = 1;
	iq_cfg->ccm_en = 1;
	iq_cfg->ygamma_en = 1;
	// Cal enable
	iq_cfg->blc_cal_en		= 1; 
	iq_cfg->lsc_cal_en		= 1; 
	iq_cfg->lsc_mode		= 1;
	iq_cfg->awb_cal_en		= 1; 
	
	// IQ enable
	iq_cfg->lsc_iq_en		= 1;   //2'h0:lsc  2'h1:awb  2'h2:ccm  2'h4:ygamma
	iq_cfg->awb_iq_en		= 1;
	iq_cfg->ccm_iq_en		= 1;
	iq_cfg->ygamma_iq_en	= 1;

	
	// module config
	// blc
	iq_cfg->blackl_r = 0;
	iq_cfg->blackl_gr = 0;
	iq_cfg->blackl_gb = 0;
	iq_cfg->blackl_b = 0;
	// lsc 
	FILE *fp = NULL;
	if (iq_cfg->image_height == 720){
		fp = fopen("./input/outweight32x64_720.txt","rt");		
	}
	if (fp == NULL){
		printf("Can not find LSC table\n");
	}
	else {
		unsigned int i = 0;
		while (!feof(fp)){
			fscanf(fp, "%d", iq_cfg->lsc_weight + i);
			i++;
		}
		fclose(fp);
	}
	// awb statistic
	iq_cfg->awb_seg_mode = 3;
	iq_cfg->awb_weight_in = 7;
	iq_cfg->awb_weight_out = 3;
	iq_cfg->awb_rg_start = 170;
	iq_cfg->awb_rgain_min = 170;
	iq_cfg->awb_rgain_max = 440;
	iq_cfg->awb_ymin = 0x10;
	iq_cfg->awb_ymax = 0xd0;
	get_awb_stat_tab(iq_cfg->awb_stat_tab, iq_cfg->awb_rg_start, 199, 3336, 951, 50, 120);

	iq_cfg->awb_yuv_mod_en = 0;
	iq_cfg->awb_cb_th[0] = 0x8;
	iq_cfg->awb_cb_th[1] = 0x10;
	iq_cfg->awb_cb_th[2] = 0x18;
	iq_cfg->awb_cb_th[3] = 0x20;
	iq_cfg->awb_cb_th[4] = 0x28;
	iq_cfg->awb_cb_th[5] = 0x30;
	iq_cfg->awb_cb_th[6] = 0x30;
	iq_cfg->awb_cb_th[7] = 0x30;

	iq_cfg->awb_cr_th[0] = 0x8;
	iq_cfg->awb_cr_th[1] = 0x10;
	iq_cfg->awb_cr_th[2] = 0x18;
	iq_cfg->awb_cr_th[3] = 0x20;
	iq_cfg->awb_cr_th[4] = 0x28;
	iq_cfg->awb_cr_th[5] = 0x30;
	iq_cfg->awb_cr_th[6] = 0x30;
	iq_cfg->awb_cr_th[7] = 0x30;

	iq_cfg->awb_cbcr_th[0] = 0xc;
	iq_cfg->awb_cbcr_th[1] = 0x18;
	iq_cfg->awb_cbcr_th[2] = 0x24;
	iq_cfg->awb_cbcr_th[3] = 0x30;
	iq_cfg->awb_cbcr_th[4] = 0x3c;
	iq_cfg->awb_cbcr_th[5] = 0x48;
	iq_cfg->awb_cbcr_th[6] = 0x48;
	iq_cfg->awb_cbcr_th[7] = 0x48;
	iq_cfg->awb_ycbcr_th = 0x0a;
	//awb
	iq_cfg->awb_de_high_red_class = 3;// 0~3
	iq_cfg->awb_de_high_blue_class = 3;// 0~3
	iq_cfg->awb_de_high_red_rate = 0;// 0~255
	iq_cfg->awb_de_high_blue_rate = 0;// 0~255
	// awb cal_IQ
	// rectangles should be less than 6.
	for (unsigned int i = 0; i < 6; i++){
	 	iq_cfg->awb_cal->x[i] = 0;
	 	iq_cfg->awb_cal->y[i] = 0;
	 	iq_cfg->awb_cal->height[i] = 0;
	 	iq_cfg->awb_cal->width[i] = 0;
	}
	// input rectangles location & size
	iq_cfg->awb_cal->x[0] = 866;
	iq_cfg->awb_cal->y[0] = 553;
	iq_cfg->awb_cal->height[0] = 44;
	iq_cfg->awb_cal->width[0] = 50;
	iq_cfg->awb_cal->x[1] = 710;
	iq_cfg->awb_cal->y[1] = 553;
	iq_cfg->awb_cal->height[1] = 44;
	iq_cfg->awb_cal->width[1] = 50;
	// ccm
	iq_cfg->ccm_par_c[0][0] = 0x38 * 4;
	iq_cfg->ccm_par_c[0][1] = 0x0 * 4;
	iq_cfg->ccm_par_c[0][2] = 0x4 * 4;
	iq_cfg->ccm_par_c[1][0] = 0x4 * 4;
	iq_cfg->ccm_par_c[1][1] = 0x44 * 4;
	iq_cfg->ccm_par_c[1][2] = (1024 - 4 * 4);
	iq_cfg->ccm_par_c[2][0] = 0x4 * 4;
	iq_cfg->ccm_par_c[2][1] = (1024 - 4 * 4);
	iq_cfg->ccm_par_c[2][2] = 0x40 * 4;
	iq_cfg->ccm_par_s[0] = 0x0;
	iq_cfg->ccm_par_s[1] = 0x0;
	iq_cfg->ccm_par_s[2] = 0x0;
	// ygamma
	iq_cfg->pad_num = 1;
	if (1){
		fp = fopen("./input/ygamma.txt", "wt");
		for (unsigned int i = 0; i < (1 << 8); i++){
			unsigned int tmp = UNROUND(pow(((double)(i) / (1 << 8)), (1 / 2.8))*(1 << 10));
			fprintf(fp, "%x\n", tmp);
		}
		fclose(fp);
	}
	fp = fopen("./input/ygamma.txt", "rt");
	if (fp == NULL){
		printf("Can not find gamma table.\n");
	}
	for (unsigned int i = 0; i < (1 << 8); i++){
		fscanf(fp, "%x\n", iq_cfg->global_gamma_table + i);
	}
	fclose(fp);
	memset(iq_cfg->wp_output, 0, 4 * 8 * 4);
}
void get_awb_stat_tab(unsigned char *table, int awb_rg_start, int fa, int fb, int fc, int bound_in_width, int bound_out_width){
	int fbr, fbr_b, fbr_c, rgain, i, j;
	if (fa > 512){
		fa = fa - 1024;
	}
	if (fb > 2048){
		fb = fb - 4096;
	}
	for (i = 0; i < 32; i++){
		rgain = awb_rg_start + i * 16;
		fbr_b = fa*rgain / 256 + fb;
		fbr_c = fbr_b*rgain / 256 + fc;
		fbr = CLIP_PIXEL(fbr_c, bound_out_width, 1023 - bound_out_width);
		table[i] = (fbr + bound_out_width) / 4;
		table[32 + i] = (fbr + bound_in_width) / 4;
		table[2 * 32 + i] = (fbr - bound_in_width) / 4;
		table[3 * 32 + i] = (fbr - bound_out_width) / 4;
	}
	for (i = 0; i < 4; i++){
		for (j = 0; j < 32; j++){
			//printf("%d ", table[i*32+j]);
		}
		//printf("\n");
	}
}
void image_write(char * img_name,const char * picture_format,void *pm){
#if OUTPUT_BLOCK_IMG
	char image_write_name[200] = "";
	strcat(image_write_name, output_dir);
	strcat(image_write_name, img_name);
	strcat(image_write_name, picture_format);
	imwrite(image_write_name,*((Mat*)pm));
#endif
}
void image_show(char * img_name, void *pm){
#if SHOW_BLOCK_IMG
	imshow(img_name, *((Mat*)pm));  
	waitKey();
#endif
}
void image_write_show(char * img_in_name,const char * out_put_suffix,void *pm){
	strcpy(img_show_name, img_in_name);
	strcat(img_show_name, out_put_suffix);
	image_write(img_show_name,".bmp",pm);
	image_show(img_show_name,pm);
}
void ShowRgbImg(Pix **rgb_buff, unsigned int width, unsigned int height, char * img_in_name,const char * out_put_suffix, unsigned int bit_shift) {
#if USE_CV
	Mat M(height, width,CV_8UC3, Scalar(0,0,255));
	unsigned char *p = M.data;
	for (unsigned int i = 0; i < height; i++) {
		for (unsigned int j = 0; j < width; j++) {
			*p++ = (unsigned char)(rgb_buff[2][i*width+j]>>bit_shift);
			*p++ = (unsigned char)(rgb_buff[1][i*width+j]>>bit_shift);
			*p++ = (unsigned char)(rgb_buff[0][i*width+j]>>bit_shift);
		}
	}
	image_write_show(img_in_name,out_put_suffix,&M);
#endif
}
void PrintRGBImg(Pix **rgb_img, unsigned int w, unsigned int h, char *file_name, unsigned int bit_depth) {
#if DEBUG_PRINT
	FILE *fp;
	char print_file_name[200] = "";
	unsigned int mask = (bit_depth == 8) ? 0xff : (bit_depth == 10) ? 0x3ff : (bit_depth == 12) ? 0xfff : 0xffffffff;
	strcat(print_file_name, debug_output_dir);
	strcat(print_file_name, file_name);

	fp = fopen(print_file_name,"wt");
	for (unsigned int j = 0; j < w*h; j++) {
		if(bit_depth == 8)
			fprintf(fp, "%02x %02x %02x\n", rgb_img[0][j]&mask,rgb_img[1][j]&mask,rgb_img[2][j]&mask);
		else
			fprintf(fp, "%03x %03x %03x\n", rgb_img[0][j]&mask,rgb_img[1][j]&mask,rgb_img[2][j]&mask);
	}
	fclose(fp);
#endif
}
void AllocImgBuff(iq_image_buff *iq_img_buff, iq_config *iq_cfg) {
	iq_img_buff->raw_img = (Pix *)malloc(sizeof(Pix)*iq_cfg->image_width*iq_cfg->image_height);
	iq_img_buff->blc_img = (Pix *)malloc(sizeof(Pix)*iq_cfg->image_width*iq_cfg->image_height);
	iq_img_buff->lsc_img = (Pix *)malloc(sizeof(Pix)*iq_cfg->image_width*iq_cfg->image_height);
	iq_img_buff->awb_img = (Pix *)malloc(sizeof(Pix)*iq_cfg->image_width*iq_cfg->image_height);
	for (unsigned int i = 0; i < 3; i++){
		iq_img_buff->demosaic_img[i] = (Pix*)malloc(sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);
		memset(iq_img_buff->demosaic_img[i], 0, sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);
	}
	for (unsigned int i = 0; i < 3; i++){
		iq_img_buff->ccm_img[i] = (Pix*)malloc(sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);
		memset(iq_img_buff->ccm_img[i], 0, sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);
	}
	for (unsigned int i = 0; i < 3; i++){
		iq_img_buff->ygamma_img[i] = (Pix*)malloc(sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);
		memset(iq_img_buff->ygamma_img[i], 0, sizeof(unsigned short)*iq_cfg->image_width*iq_cfg->image_height);
	}
}
void FreeImgBuff(iq_image_buff *iq_img_buff, iq_config *iq_cfg){
	if (iq_img_buff->raw_img != NULL){
		free(iq_img_buff->raw_img);
		iq_img_buff->raw_img = NULL;
	}
	if (iq_img_buff->blc_img != NULL){
		free(iq_img_buff->blc_img);
		iq_img_buff->blc_img = NULL;
	}
	if (iq_img_buff->lsc_img != NULL){
		free(iq_img_buff->lsc_img);
		iq_img_buff->lsc_img = NULL;
	}
	if (iq_img_buff->awb_img != NULL){
		free(iq_img_buff->awb_img);
		iq_img_buff->awb_img = NULL;
	}
	for (unsigned int i = 0; i < 3; i++){
		if (iq_img_buff->demosaic_img[i] != NULL){
			free(iq_img_buff->demosaic_img[i]);
			iq_img_buff->demosaic_img[i] = NULL;
		}
	}
	for (unsigned int i = 0; i < 3; i++){
		if (iq_img_buff->ccm_img[i] != NULL){
			free(iq_img_buff->ccm_img[i]);
			iq_img_buff->ccm_img[i] = NULL;
		}
	}
	for (unsigned int i = 0; i < 3; i++){
		if (iq_img_buff->ygamma_img[i] != NULL){
			free(iq_img_buff->ygamma_img[i]);
			iq_img_buff->ygamma_img[i] = NULL;
		}
	}
}
typedef struct                       /**** BMP file header structure ****/  
{  
    unsigned int   bfSize;           /* Size of file */  
    unsigned short bfReserved1;      /* Reserved */  
    unsigned short bfReserved2;      /* ... */  
    unsigned int   bfOffBits;        /* Offset to bitmap data */  
} BITMAPFILEHEADER;
typedef struct                       /**** BMP file info structure ****/  
{  
    unsigned int   biSize;           /* Size of info header */  
    int            biWidth;          /* Width of image */  
    int            biHeight;         /* Height of image */  
    unsigned short biPlanes;         /* Number of color planes */  
    unsigned short biBitCount;       /* Number of bits per pixel */  
    unsigned int   biCompression;    /* Type of compression to use */  
    unsigned int   biSizeImage;      /* Size of image data */  
    int            biXPelsPerMeter;  /* X pixels per meter */  
    int            biYPelsPerMeter;  /* Y pixels per meter */  
    unsigned int   biClrUsed;        /* Number of colors used */  
    unsigned int   biClrImportant;   /* Number of important colors */  
} BITMAPINFOHEADER;
void RGBToBmp(unsigned char *fileBuff,short **rgbbuf,unsigned int width,unsigned int height,int bit_shift)  
{  
    BITMAPFILEHEADER bfh;  
    BITMAPINFOHEADER bih;  
    /* Magic number for file. It does not fit in the header structure due to alignment requirements, so put it outside */  
    unsigned short bfType=0x4d42;             
    bfh.bfReserved1 = 0;  
    bfh.bfReserved2 = 0;  
    bfh.bfSize = 2+sizeof(BITMAPFILEHEADER) + sizeof(BITMAPINFOHEADER)+width*height*3;  
    bfh.bfOffBits = 0x36;  
  
    bih.biSize = sizeof(BITMAPINFOHEADER);  
    bih.biWidth = width;  
    bih.biHeight = -(int)height; //改为负值，声明为 top - down BMP
    bih.biPlanes = 1;  
    bih.biBitCount = 24;  
    bih.biCompression = 0;  
    bih.biSizeImage = 0;  
    bih.biXPelsPerMeter = 5000;  
    bih.biYPelsPerMeter = 5000;  
    bih.biClrUsed = 0;  
    bih.biClrImportant = 0;  
  
    memcpy(fileBuff,&bfType,sizeof(bfType));  
	fileBuff+=sizeof(bfType);
    memcpy(fileBuff,&bfh,sizeof(bfh)); 
	fileBuff+=sizeof(bfh);
    memcpy(fileBuff,&bih,sizeof(bih)); 
	fileBuff+=sizeof(bih);
    //memcpy(fileBuff,rgbbuf,width*height*3);  
    for (unsigned int i = 0; i < height; i++){
		for (unsigned int j = 0; j < width; j++) {
            *fileBuff++ = (unsigned char)(rgbbuf[2][i*width + j] >> bit_shift);
            *fileBuff++ = (unsigned char)(rgbbuf[1][i*width + j] >> bit_shift);
            *fileBuff++ = (unsigned char)(rgbbuf[0][i*width + j] >> bit_shift);
		}
	}

}
#if 1
ISP_API bool EncoderImgBuffer(short **rgb_buff, unsigned int width, unsigned int height, int bit_shift, void* out_buffer, int &buffer_size)
{
	if(buffer_size<width*height*3+54)
	{
		buffer_size=width*height*3+54;
		return false;
	}
	RGBToBmp((unsigned char *)out_buffer,rgb_buff,width,height,bit_shift);
	return true;
}
#else
ISP_API bool EncoderImgBuffer(short **rgb_buff, unsigned int width, unsigned int height, int bit_shift, void* out_buffer, int &buffer_size) {
	Mat M(height, width, CV_8UC3, Scalar(0, 0, 255));
	unsigned char *p = M.data;
	printf("c1,%d,%d\n",width,height);
	//return true;
	for (unsigned int i = 0; i < height; i++) {
		for (unsigned int j = 0; j < width; j++) {
            *p++ = (unsigned char)(rgb_buff[2][i*width + j] >> bit_shift);
            *p++ = (unsigned char)(rgb_buff[1][i*width + j] >> bit_shift);
            *p++ = (unsigned char)(rgb_buff[0][i*width + j] >> bit_shift);
		}
	}
	printf("c2\n");
	std::vector<uchar> buffer;
	imencode(".bmp", M, buffer);
	printf("c3\n");
	if (buffer_size < buffer.size())
	{
		printf("c4\n");
		buffer_size = buffer.size();
		printf("c7 %d\n",buffer_size);
		return false;
	}
	printf("c5\n");
	memcpy(out_buffer, buffer.data(), buffer.size());
	printf("c6\n");
	return true;
}
#endif


