#include <stdio.h>
#include <stdlib.h>
#include <io.h>
#include <string.h>
#include "lanfont.h"

#define  LAN_HEADER_SIZE     (512/16)


static LAN_FONT_T *lanfont;
static unsigned short itable[4096];
static unsigned char lantable[4096]; 
static unsigned int *lanCache,lanCnt,lanOffset,strCnt,strOffset,strIdx;  //

int lanfontInit(FILE *file,int strcnt)
{
	int i;

    lanCache = (unsigned int *)lantable;
	lanCnt = 0;

	for(i=0;i<512;i++)
		lantable[i] = 0;

	lantable[0] = 'M';
//	lantable[1] = 'A';
	lantable[1] = 'X';
	lantable[2] = LAN_CHAR_INV_W;
    lantable[3] = 0;
    lantable[510] = 0x55;
	lantable[511] = 0xaa;

    fwrite(lantable,512,1,file);

	strCnt = strcnt;

    lanfont = (LAN_FONT_T *)malloc(sizeof(LAN_FONT_T)*(1+strCnt));

	lanOffset = 512;

	return 0;
}

int lanfontEnd(FILE *file)
{
	fseek(file,0,SEEK_SET);

    lantable[3] = lanCnt;

	lanCache[1] = lanOffset; 

	fwrite(lantable,512,1,file);

	free(lanfont);

	return 0;
}


int lanfontLanInit(FILE *file,int idx)
{
    if(idx>=LAN_HEADER_SIZE)
		return -1;
    lanCache[idx*2+2+0] = (strCnt<<8)|idx;
	lanCache[idx*2+2+1] = lanOffset;

	memset(lanfont,0,sizeof(LAN_FONT_T)*(1+strCnt));
	
	lanfont->width = idx;
	lanfont->height= 0;
	fwrite(lanfont,sizeof(LAN_FONT_T)*(1+strCnt),1,file);

    lanOffset+=sizeof(LAN_FONT_T)*(1+strCnt);
    strOffset =sizeof(LAN_FONT_T)*(1+strCnt);

	strIdx = 0;

	if((idx+1)>(int)lanCnt)
		lanCnt = idx+1;

	return 0;
} 

int lanfontLanEnd(FILE *file,int idx)
{
    lanfont->height = strOffset;
	fseek(file,lanCache[idx*2+2+1],SEEK_SET);

	fwrite(lanfont,sizeof(LAN_FONT_T)*(1+strCnt),1,file);

	fseek(file,0,SEEK_END);

	return 0;
}

int lanfontStrAdd(FILE *file,int width,int height,int number,unsigned short *table)
{
	LAN_FONT_T *string;

    fseek(file,0,SEEK_END);

    strIdx++;

	string = lanfont+strIdx;
	string->width =width;
	string->height=height;
	string->offset = strOffset;
	string->number = number;

	fwrite(table,number<<1,1,file);

	strOffset += number<<1;
    lanOffset += number<<1;
	return 0;
}






