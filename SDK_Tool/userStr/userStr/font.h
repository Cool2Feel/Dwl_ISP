#ifndef  FONT_H
#define  FONT_H




#define MAX_SPC_FONT  800//200


typedef struct font_info_s
{
	unsigned char width;
	unsigned char heigth;
	unsigned short length;
	unsigned int  addr;
}font_info_t;



int fontInit(FILE *file);

int fontEnd(FILE *file);

int fontGetIndex(unsigned int unicode);

font_info_t *fontGetInfo(FILE *file,unsigned int unicode);


int fontGetData(FILE *file,unsigned char *buff,font_info_t *info);


font_info_t *fontGetInfoByindex(FILE *file,unsigned int index);















#endif