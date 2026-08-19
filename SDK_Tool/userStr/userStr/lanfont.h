#ifndef  LANFONT_H
#define  LANFONT_H


typedef struct LAN_FONT_S
{
	unsigned short width;
	unsigned short height;	
	unsigned short number;
    unsigned short offset;
}LAN_FONT_T;



#define  LAN_CHAR_INV_W         0


int lanfontInit(FILE *file,int strcnt);


int lanfontEnd(FILE *file);


int lanfontLanInit(FILE *file,int idx);



int lanfontLanEnd(FILE *file,int idx);



int lanfontStrAdd(FILE *file,int width,int height,int number,unsigned short *table);









#endif