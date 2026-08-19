#ifndef  MAXFONT_H
#define  MAXFONT_H


typedef struct FONT_LAN_INFO_S
{
	unsigned int offset;
	unsigned int size;
}FONT_LAN_INFO_T;

typedef struct FONT_STR_INFO_S
{
	unsigned short width;
	unsigned short height;
	unsigned int   offset;
}FONT_STR_INFO_T;

int maxFontEncodeInit(FILE *file);

int maxFontEncodeAdd(FILE *file,unsigned int offset,unsigned int size);

int maxFontEncodeEnd(FILE *file);

int maxFontEncodeGetOffset(FILE *file);

int maxFontEncodeLanInit(FILE *file);

int maxFontEncodeLanAdd(FILE *file,unsigned short w,unsigned short h,unsigned int offset);

int maxFontEncodeLanEnd(FILE *file,unsigned int offset,unsigned int lcnt);

int maxFontEncodeLanData(FILE *file,FILE *temp,int size);

int maxFontEncodeTempData(FILE *temp,unsigned char *buff,int len);



int maxFontDecodeInit(FILE *file);

int maxFontDecodeEnd(FILE *file);

int maxFontDecodeGetLan(FILE *file,FONT_LAN_INFO_T *font,unsigned char lan);

int maxFontDecodeGetLanHeadSize(FILE *file,FONT_LAN_INFO_T *font);

int maxFontDecodeGetHead(FILE *file,FONT_LAN_INFO_T *font,int hsize,unsigned int *buff);

int maxFontDecodeGetStr(unsigned int *head,FONT_STR_INFO_T *str,unsigned int str_id);

int maxFontDecodeGetStrData(FILE *file,FONT_LAN_INFO_T *font,FONT_STR_INFO_T *str,unsigned char *buff);


#endif
