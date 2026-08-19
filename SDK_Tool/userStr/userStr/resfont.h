#ifndef  RESFONT_H
#define  RESFONT_H



typedef struct RES_FONT_S
{
	unsigned short width;
	unsigned short height;
	unsigned int   offset;
}RES_FONT_T;




int resfontInit(FILE *file,int cnt);



int resfontEnd(FILE *file);




int resfontUninit(void);




int resfontAdd(FILE *file,int idx,unsigned int width,unsigned int height,unsigned char *buffer);





int resfontGetOffset(int idx);





RES_FONT_T *resfontGetInfo(int idx);















#endif
