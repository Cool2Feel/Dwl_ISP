#include <stdio.h>
#include <stdlib.h>
#include <io.h>
#include <string.h>
#include "resfont.h"



static unsigned int *resfontCache,resfontoffset,resSpcFontAddr;

int resfontInit(FILE *file,int cnt)
{
     resfontCache = (unsigned int *)malloc((cnt+1)*sizeof(RES_FONT_T));

	 memset(resfontCache,0,(cnt+1)*sizeof(RES_FONT_T));

	 resfontCache[0] = cnt;
     resfontCache[1] = (cnt+1)*sizeof(RES_FONT_T);

	 resSpcFontAddr= resfontoffset = resfontCache[1];
	
	
	 fwrite(resfontCache,resfontoffset,1,file);
     return (cnt*sizeof(RES_FONT_T));
}
int resfontEnd(FILE *file)
{
	fseek(file,0,SEEK_SET);

	fwrite(resfontCache,resfontCache[1],1,file);

	fclose(file);

	return 0;
}

int resfontUninit(void)
{
     free(resfontCache);

	 return 0;
}
extern int printout(unsigned char *buffer,unsigned short len);
int resfontAdd(FILE *file,int idx,unsigned int width,unsigned int height,unsigned char *buffer)
{
	int len;
    RES_FONT_T *res;


	if(idx>(int)resfontCache[0])
		return -1;
	res = (RES_FONT_T *)&resfontCache[(idx+1)<<1];
	res->width = width;
	res->height = height;
    fseek(file,0,SEEK_END);

	res->offset = resfontoffset;

	len = height*((width+7)/8);

	fwrite(buffer,len,1,file);

	resfontoffset+=len;

	return len;
}

int resfontGetOffset(int idx)
{
	if(idx>(int)resfontCache[0])
		return -1;

	return ((idx+1)*sizeof(RES_FONT_T));
}


RES_FONT_T *resfontGetInfo(int idx)
{
	if(idx>(int)resfontCache[0])
		return NULL;

	return ((RES_FONT_T *)&resfontCache[(idx+1)<<1]);
}