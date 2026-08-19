// LcdApi.cpp : 定义 DLL 应用程序的导出函数。
//

#include "stdafx.h"
#include "LcdApi.h"

#include <cmath>
#include <fstream>

// 这是导出变量的一个示例
LCDAPI_API int nLcdApi=0;

// 这是导出函数的一个示例。
LCDAPI_API int fnLcdApi(void)
{
	return 42;
}

// 这是已导出类的构造函数。
// 有关类定义的信息，请参阅 LcdApi.h
CLcdApi::CLcdApi()
{
	return;
}


#define ROUND(x) ((int)(x + 0.5))	
//void get_cubic_coeff8x4(unsigned char *w_table, int in_size, int out_size, int strength){ // 宽度
//	FILE *fid_scaler_table;
//	fid_scaler_table = fopen("./output/tmp_file/scaler_table8x4.txt", "w+");
//
//	AntiSawtooth4(in_size, out_size, strength, w_table);
//	for (unsigned int i = 0; i < 8; i++){
//		fprintf(fid_scaler_table, "%02x%02x%02x%02x\n", w_table[i * 4 + 3],
//			w_table[i * 4 + 2], w_table[i * 4 + 1], w_table[i * 4 + 0]);
//	}
//	fclose(fid_scaler_table);
//}
//void get_cubic_coeff8x8(unsigned char *w_table, int in_size, int out_size, int strength){ // 高度
//	FILE *fid_scaler_table;
//	fid_scaler_table = fopen("./output/tmp_file/scaler_table8x8.txt", "w+");
//
//	AntiSawtooth8(in_size, out_size, strength, w_table);
//	for (unsigned int i = 0; i < 8; i++){
//		for (unsigned int j = 0; j < 8; j++){
//			fprintf(fid_scaler_table, "%02x", w_table[i * 8 + j]);
//		}
//		fprintf(fid_scaler_table, "\n");
//
//	}
//	fclose(fid_scaler_table);
//}

double GetCoef(double x) {
	double f;
	double pi = 3.1415926;
	f = (sin(pi*x)* sin(pi*x / 3) + 0.0001) / ((pi*pi*x*x / 3) + 0.0001);
	return f;
}

extern "C" LCDAPI_API void AntiSawtooth8(int sensorWidth, int lcdWidth, int strength, unsigned char *coef){
	double s_rate, c_rate;
	int i, j;
	double p;
	double c[8];
	int d[8];
	int sum_d;
	double s_level[8] = { 2.75, 2.50, 2.25, 2.00, 1.75, 1.50, 1.25, 1.0 };


	s_rate = (double)lcdWidth / (double)sensorWidth;

	for (i = 0; i < 8; i++) {
		int idx;
		p = i*0.125;
		if (s_rate < 1) {
			c[0] = s_rate*GetCoef(s_level[strength] * s_rate*(3 + p));
			c[1] = s_rate*GetCoef(s_level[strength] * s_rate*(2 + p));
			c[2] = s_rate*GetCoef(s_level[strength] * s_rate*(1 + p));
			c[3] = s_rate*GetCoef(s_level[strength] * s_rate*(p));
			c[4] = s_rate*GetCoef(s_level[strength] * s_rate*(1 - p));
			c[5] = s_rate*GetCoef(s_level[strength] * s_rate*(2 - p));
			c[6] = s_rate*GetCoef(s_level[strength] * s_rate*(3 - p));
			c[7] = s_rate*GetCoef(s_level[strength] * s_rate*(4 - p));
		}
		else{
			c[0] = GetCoef((3 + p));
			c[1] = GetCoef((2 + p));
			c[2] = GetCoef((1 + p));
			c[3] = GetCoef((p));
			c[4] = GetCoef((1 - p));
			c[5] = GetCoef((2 - p));
			c[6] = GetCoef((3 - p));
			c[7] = GetCoef((4 - p));
		}

		c_rate = 1 / (c[0] + c[1] + c[2] + c[3] + c[4] + c[5] + c[6] + c[7]);

		d[0] = (int)ROUND(c[0] * 64 * c_rate);
		d[1] = (int)ROUND(c[1] * 64 * c_rate);
		d[2] = (int)ROUND(c[2] * 64 * c_rate);
		d[3] = (int)ROUND(c[3] * 64 * c_rate);
		d[4] = (int)ROUND(c[4] * 64 * c_rate);
		d[5] = (int)ROUND(c[5] * 64 * c_rate);
		d[6] = (int)ROUND(c[6] * 64 * c_rate);
		d[7] = (int)ROUND(c[7] * 64 * c_rate);
		sum_d = d[0] + d[1] + d[2] + d[3] + d[4] + d[5] + d[6] + d[7];
		sum_d = 64 - sum_d;
		idx = (i < 4) ? 3 : 4;
		d[idx] += sum_d;

		for (j = 0; j < 8; j++){
			coef[i * 8 + j] = d[j];
		}
	}

#ifdef _DEBUG
	FILE *fid_scaler_table;
	fid_scaler_table = fopen("./scaler_table8x8.txt", "w+");
	
	for (unsigned int i = 0; i < 8; i++){
		for (unsigned int j = 0; j < 8; j++){
			fprintf(fid_scaler_table, "%02x", coef[i * 8 + j]);
		}
		fprintf(fid_scaler_table, "\n");
	}
	fclose(fid_scaler_table);
#endif
}

extern "C" LCDAPI_API void AntiSawtooth4(int sensorHeight, int lcdHeight, int strength, unsigned char *coef){
	double s_rate, c_rate;
	int i, j;
	double p;
	double c[4];
	int d[4];
	int sum_d;
	double s_level[8] = { 2.75, 2.50, 2.25, 2.00, 1.75, 1.50, 1.25, 1.0 };


	s_rate = (double)lcdHeight / (double)sensorHeight;

	for (i = 0; i < 8; i++) {
		int idx;
		p = i*0.125;
		if (s_rate < 1) {
			c[0] = s_rate*GetCoef(s_level[strength] * s_rate*(1 + p));
			c[1] = s_rate*GetCoef(s_level[strength] * s_rate*(p));
			c[2] = s_rate*GetCoef(s_level[strength] * s_rate*(1 - p));
			c[3] = s_rate*GetCoef(s_level[strength] * s_rate*(2 - p));
		}
		else{
			c[0] = GetCoef((1 + p));
			c[1] = GetCoef((p));
			c[2] = GetCoef((1 - p));
			c[3] = GetCoef((2 - p));
		}

		c_rate = 1 / (c[0] + c[1] + c[2] + c[3]);

		d[0] = (int)ROUND(c[0] * 64 * c_rate);
		d[1] = (int)ROUND(c[1] * 64 * c_rate);
		d[2] = (int)ROUND(c[2] * 64 * c_rate);
		d[3] = (int)ROUND(c[3] * 64 * c_rate);
		sum_d = d[0] + d[1] + d[2] + d[3];
		sum_d = 64 - sum_d;
		idx = (i < 4) ? 1 : 2;
		d[idx] += sum_d;

		for (j = 0; j < 4; j++){
			coef[i * 4 + j] = d[j];
		}
	}

#ifdef _DEBUG
	FILE *fid_scaler_table;
	fid_scaler_table = fopen("./scaler_table8x4.txt", "w+");
	
	for (unsigned int i = 0; i < 8; i++){
		fprintf(fid_scaler_table, "%02x%02x%02x%02x\n", coef[i * 4 + 3],
			coef[i * 4 + 2], coef[i * 4 + 1], coef[i * 4 + 0]);
	}
	fclose(fid_scaler_table);

#endif
}
