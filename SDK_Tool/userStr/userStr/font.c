#include <stdio.h>
#include <stdlib.h>
#include <io.h>
#include <string.h>
#include "font.h"



unsigned int *fontCache,fontCnt;

int fontInit(FILE *file)
{
   
	fread(&fontCnt,4,1,file);

    fontCache = (unsigned int *)malloc((fontCnt+MAX_SPC_FONT)*8);

	fread(fontCache,fontCnt*8,1,file);

	return fontCnt+MAX_SPC_FONT;
}
int fontEnd(FILE *file)
{
	free(fontCache);

	return 0;
}
int fontGetIndex(unsigned int unicode)
{
	unsigned int i;

    for(i=0;i<fontCnt;i++)
	{
		if(fontCache[i*2] == unicode)
			return i;
	}

	return -1;
}
font_info_t *fontGetInfo(FILE *file,unsigned int unicode)
{
     static font_info_t info;
	 int temp32;


     temp32 = fontGetIndex(unicode);
	 if(temp32<0)
		 return NULL;

    temp32 = fontCache[temp32*2+1]+4;
    fseek(file,temp32,SEEK_SET);
	fread(&info,8,1,file);

	return &info;
}
font_info_t *fontGetInfoByindex(FILE *file,unsigned int index)
{
    static font_info_t info;
	if(index>fontCnt)
		return NULL;
    index = fontCache[index*2+1]+4;
    fseek(file,index,SEEK_SET);
	fread(&info,8,1,file);

	return &info;
}

int fontGetFontCnt()
{
	return fontCnt;
}

int fontGetData(FILE *file,unsigned char *buff,font_info_t *info)
{
	fseek(file,info->addr+4,SEEK_SET);
	fread(buff,info->length,1,file);

	return 0;
}
