#include <stdio.h>
#include "../include/IQ.h"
#if  USE_CV
#include <opencv2/opencv.hpp>
using namespace std;
using namespace cv;
#endif
int main()
{
	iq_config cfg;
	iq_config *iq_cfg = &cfg;
	iq_image_buff buff;
	iq_image_buff *iq_img_buff = &buff;
	bool iq_en = 1;
	bool isp_cal_en = 1;
	IqConfig(iq_cfg, iq_img_buff, iq_en, isp_cal_en);
	AllocImgBuff(iq_img_buff, iq_cfg);
	IspProcess(iq_cfg, iq_img_buff);
	return 0;
}

