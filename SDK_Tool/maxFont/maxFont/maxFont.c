#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <io.h>
#include "maxFont.h"
#define  MAXFONT_TAG    "MAX0"

static unsigned int *maxFontCache,maxFontSize,maxFontIdx;
//----------------------------------max font encode-----------------------------------------------
int maxFontEncodeInit(FILE *file)
{
	unsigned char *buff;
	int i;

    maxFontCache = (unsigned int *)malloc(1024);

	buff = (unsigned char *)maxFontCache;
	for(i=0;i<256;i++)
       maxFontCache[i] = 0;

	buff[0] = 'M';
	buff[1] = 'A';
	buff[2] = 'X';
    buff[3] = 0;

    maxFontCache[1] = 1024;
	maxFontCache[255] = 0x55aa0000;
    maxFontSize = 1024;
    maxFontIdx  = 0;
	fwrite(buff,1024,1,file);

    return 0;
}
int maxFontEncodeAdd(FILE *file,unsigned int offset,unsigned int size)
{
    maxFontIdx++;
    maxFontCache[maxFontIdx*2] = offset;
    maxFontCache[maxFontIdx*2+1] = size;
	maxFontSize+= size;

	return maxFontIdx;
}
int maxFontEncodeEnd(FILE *file)
{
    unsigned char *buff;
	unsigned int size;

	fseek(file,0,SEEK_END);
	size = ftell(file);
    maxFontCache[1] = size;
	buff = (unsigned char *)maxFontCache;
    buff[3] = maxFontIdx;
    fseek(file,0,SEEK_SET);
	size = ftell(file);
	fwrite(buff,1024,1,file);

	free(maxFontCache);
	return 0;
}
int maxFontEncodeGetOffset(FILE *file)
{
	return ftell(file);
}

int maxFontEncodeLanInit(FILE *file)
{
	unsigned int hsize,offset;

	fseek(file,0,SEEK_END);

	offset = ftell(file);

	hsize = 4;

	fwrite(&hsize,4,1,file);

	return offset;
}
int maxFontEncodeLanAdd(FILE *file,unsigned short w,unsigned short h,unsigned int offset)
{
	unsigned int size;

	size = (h<<16)|w;

	fwrite(&size,4,1,file);
	fwrite(&offset,4,1,file);

	return 0;
}
int maxFontEncodeLanEnd(FILE *file,unsigned int offset,unsigned int lcnt)
{
	unsigned int hsize;

	hsize = lcnt*8+4;

	fseek(file,offset,SEEK_SET);
	fwrite(&hsize,4,1,file);

	return hsize;
}
int maxFontEncodeLanData(FILE *file,FILE *temp,int size)
{
	unsigned char *buffer;
    unsigned int len;

	buffer = (unsigned char *)malloc(4096);
	len = 0;
	fseek(temp,0,SEEK_END);
	len = ftell(temp);
	fseek(temp,0,SEEK_SET);
	fseek(file,0,SEEK_END);
	while(1)
	{
        if(size>=4096)
			len = 4096;
		else
			len = size;
        fread(buffer,len,1,temp);
		fwrite(buffer,len,1,file);
		size-=len;

		if(size<=0)
		   break;
	}
    return 0;
}
int maxFontEncodeTempData(FILE *temp,unsigned char *buff,int len)
{
	fwrite(buff,len,1,temp);

	return ftell(temp);
}

//-------------------------------------------max font decode---------------------------------
int maxFontDecodeInit(FILE *file)
{
	unsigned char *buff;

    maxFontCache = (unsigned int *)malloc(1024);

	buff = (unsigned char *)maxFontCache;
	fread(maxFontCache,1024,1,file);

	if(buff[0]!='M' || buff[1] != 'A' || buff[2] != 'X')
		return -1;

    return buff[3];
}
int maxFontDecodeEnd(FILE *file)
{
	free(maxFontCache);

	return 0;
}
int maxFontDecodeGetLan(FILE *file,FONT_LAN_INFO_T *font,unsigned char lan)
{
	unsigned char *buff;
    buff = (unsigned char *)maxFontCache;
 
	if(lan>=buff[3])
		return -1;
	lan+=1;
	font->offset = maxFontCache[lan*2];
	font->size   = maxFontCache[lan*2+1];

	return 0;
} 
int maxFontDecodeGetLanHeadSize(FILE *file,FONT_LAN_INFO_T *font)
{
	unsigned int temp;

	fseek(file,font->offset,SEEK_SET);
	fread(&temp,4,1,file);

	return temp;
}
int maxFontDecodeGetHead(FILE *file,FONT_LAN_INFO_T *font,int hsize,unsigned int *buff)
{
    unsigned int temp;

	if(hsize<=0)
	{
        fseek(file,font->offset,SEEK_SET);
	    fread(&temp,4,1,file);
	}
	else
	{
		temp = hsize;
		fseek(file,font->offset,SEEK_SET);
	}

    fread(buff,temp,1,file);

	return ((temp-4)>>3);
}
int maxFontDecodeGetStr(unsigned int *head,FONT_STR_INFO_T *str,unsigned int str_id)
{
    unsigned int temp;

	temp = *head-4;
    if(str_id>=(temp>>3))
		return -1;

	temp = *(head+1+str_id*2);
	str->width = temp&0xffff;
	str->height= temp>>16;
	temp = *(head+1+str_id*2+1);
	str->offset= temp+*head;

	return 0;
}
int maxFontDecodeGetStrData(FILE *file,FONT_LAN_INFO_T *font,FONT_STR_INFO_T *str,unsigned char *buff)
{
    unsigned int offset,size;

	offset = font->offset+str->offset;
	size   = ((str->width+7)/8)*str->height;

	fseek(file,offset,SEEK_SET);
	fread(buff,size,1,file);

	return 0;
}