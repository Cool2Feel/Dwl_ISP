#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <io.h>
#include "maxFont.h"
#include "utf_unicode.h"
#include "draw.h"


#define  INPUT_FILE   "Font.bin"
#define OUTPUT_FILE  "maxFont.bin"
#define TEMP_FILE    "font.tmp"
#define FONT_HEADER  "Font.h"

typedef struct font_info_s
{
	unsigned char width;
	unsigned char heigth;
	unsigned short length;
	unsigned int  addr;
}font_info_t;


unsigned int *fontCache,fontCnt;

int fontInit(FILE *file)
{
   
	fread(&fontCnt,4,1,file);

    fontCache = (unsigned int *)malloc(fontCnt*8);

	fread(fontCache,fontCnt*8,1,file);

	return fontCnt;
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
int fontGetData(FILE *file,unsigned char *buff,font_info_t *info)
{
	fseek(file,info->addr+4,SEEK_SET);
	fread(buff,info->length,1,file);

	return 0;
}
void fontPixelXchange(unsigned char *table,int x,int y,int width)
{
	unsigned char *line,i,j;

	i = x/8;
	j = x%8;
	line = width*y+table+i;

    *line |= 0x80>>j;
}
int fontWordXchange(FILE *file,font_info_t *info,unsigned char *buffer,int width,int sx)
{
    unsigned char dcache[1024],i,j,mask,temp8;

    fontGetData(file,dcache,info);
    j=0;
    for(i=0;i<info->length;i++)
	{
           temp8 = dcache[i];
		   if(i%info->width==0 && i!=0)
               j++;
           for(mask=0;mask<8;mask++)
		   {
			   if(temp8&(1<<mask))
			   {
                  fontPixelXchange(buffer,i%(info->width)+sx,j*8+mask,(width+7)/8);
			   }
		   }
		   
	}

	return 0;
}
int fontStringXchange(FILE *file,font_info_t *info[],int len,unsigned int *width,unsigned int *height,unsigned char *buff)
{
	int i;
	unsigned int w,h,x;
    
	w = 0;
	h = 0;
    for(i=0;i<len;i++)
	{
		w += info[i]->width+2;
		if(info[i]->heigth > h)
			h = info[i]->heigth;
	}

    x = 0;
	for(i=0;i<len;i++)
	{
          fontWordXchange(file,info[i],buff,w,x);
		  x+=info[i]->width+2;
	}

	*width = w;
	*height= h;
	return 0;
}
int fontUnicodeGet(FILE *txt)
{
	unsigned char temp8,utf8[4],*des;
	int len,i;

	len = 0;

	des = utf8;

    if(fread(&temp8,1,1,txt)<=0)
		return 0;
	if((temp8&0xf0) == 0xe0)
		len = 2;
	else if((temp8&0xe0) == 0xc0)
		len = 1;
	else 
		len = 0;
	i = len;
	*des++ = temp8;
	while(i)
	{
       if(fread(&temp8,1,1,txt)<=0)
		  return 0;
	   *des++ = temp8;
	   i--;
	}

    return enc_utf8_to_unicode_one(utf8,NULL,len+1);

//	return (len+1);
}
int strcnt = 0;
int fontStringGet(FILE *file,font_info_t **info,FILE *txt)
{
	static font_info_t infopool[128],*cinfo;
	int i,sflag,len;
    unsigned int unicode[128],temp32;
	
    if(strcnt == 49)
	{
		strcnt = 49;
	}
	i = 0;
	sflag = 0;
    while(1)
	{
         temp32 = fontUnicodeGet(txt);
		 if(temp32<0x80)
		 {
			 if(temp32 == (unsigned char)'"')
				 continue;
			 else if(temp32 == (unsigned char)'\n')
				 break;
			 else if(temp32 == 0)
				 break;

		 }
		 if(temp32>0x599 && temp32<0x700)
			 sflag = 1;
         unicode[i++] = temp32; 
	}
    len = i;
	if(len ==0)
		return 0;
	if(sflag)
	{
		for(i=0;i<len/2;i++)
		{
			temp32 = unicode[i];
            unicode[i] = unicode[len-1-i];
            unicode[len-1-i] = temp32;
		}
	}

	for(i=0;i<len;i++)
	{
		cinfo = fontGetInfo(file,unicode[i]);
		if(cinfo!=NULL)
		{
			infopool[i].addr = cinfo->addr;
			infopool[i].heigth=cinfo->heigth;
			infopool[i].length=cinfo->length;
			infopool[i].width =cinfo->width;

			*info = &infopool[i];
			info++;
		}
		else
			return -1;
	}

	return i;	
}

int fontLanuage(FILE *file,FILE *font,FILE *temp,FILE *txt)
{
	unsigned char output[4096];
	int len,idx,i;
    font_info_t *infotable[128];
    unsigned int loffset,lsize,swidth,sheight,soffset;
	int ret;

	loffset = maxFontEncodeLanInit(font);
	lsize   = 4;
	swidth  = 0;
	sheight = 0;
	soffset = 0;
	idx =0;
	ret = 0;
	strcnt = 0;
    while(1)
	{
        for(i=0;i<4096;i++)
			output[i] = 0;
		strcnt++;
		len = fontStringGet(file,infotable,txt);
		if(len<0)
		{
			printf("-FONT ERROR");
			ret = -1;
			break;
		}
		else if(len == 0)
			break;
        fontStringXchange(file,infotable,len,&swidth,&sheight,output);
		//draw_main_ext(output,swidth,sheight);
		//draw_main_return();
		len = ((swidth+7)/8)*sheight;     
		maxFontEncodeTempData(temp,output,len);  
		maxFontEncodeLanAdd(font,swidth,sheight,soffset);
        soffset+=len;
		idx++;
	}
    maxFontEncodeLanData(font,temp,soffset);
	lsize = maxFontEncodeLanEnd(font,loffset,idx);
	lsize += soffset;
	maxFontEncodeAdd(font,loffset,lsize);

	return ret;
}
char *filenameUpcase(char *name)
{
	static char filename[128];
	int i;

	for(i=0;i<128;i++)
	{
        if(name[i] == 0 || name[i] == '.')
			break;
		if(name[i]>='a' && name[i]<='z')
			filename[i] = name[i]-('z'-'Z');
		else
			filename[i] = name[i];
	}
	filename[i] = 0;
	return filename;
}

int main_encode(void)
{
     FILE *font,*maxfont,*ftemp,*header,*file;
	 unsigned char lanidx,temp8;
	 int handle,len,k,errcnt,okcnt;
	 struct _finddata_t FileInfo;
	 char fontname[64];

     _unlink((const char *)OUTPUT_FILE);
	 _unlink((const char *)FONT_HEADER);
	 _unlink((const char *)TEMP_FILE);

	 //fopen_s(&font,(const char *)INPUT_FILE,(const char *)"r");
	 fopen_s(&maxfont,(const char *)OUTPUT_FILE,(const char *)"wb+");	
	 fopen_s(&header,(const char *)FONT_HEADER,(const char *)"w+");

	 errcnt = 0;
	 okcnt  = 0;
	 lanidx = 0;
	 fprintf(header,"#ifndef  FONT_H\n   #define  FONT_H\n\n\n\n");
	 maxFontEncodeInit(maxfont);
	// fontInit(font);
     //draw_main_start();
     k = handle=_findfirst("*.txt",&FileInfo);
	 while(k>=0)
	 {
		 fprintf(header,"#define  LANUAGE_%s        %d     // %s\n",filenameUpcase(FileInfo.name),lanidx++,FileInfo.name);
		 printf("LOG >> encode : %s.",FileInfo.name);
		 fopen_s(&file,FileInfo.name,"r");
		 strcpy(fontname,FileInfo.name);
		 len = strlen(FileInfo.name);
		 fontname[len-1] = 'n';
		 fontname[len-2] = 'i';
		 fontname[len-3] = 'b';
		 fopen_s(&font,(const char *)fontname,(const char *)"rb");
		 
		 if(file == NULL || font == NULL)
		 {
			 errcnt++;
			 k = _findnext(handle,&FileInfo);
			 printf("-FAIL,can not open this file\n");
			 if(font)
				 fclose(font);
			 if(file)
				 fclose(file);
			 continue;
		 }
		 else
			 printf("-PROCESS.");
		 fontInit(font);
		 fread(&temp8,1,1,file);
		 if(temp8>0x80)
			 fseek(file,4,SEEK_SET);
		 else
			 fseek(file,0,SEEK_SET);
         _unlink(TEMP_FILE);
		 fopen_s(&ftemp,TEMP_FILE,"wb+");

         if(fontLanuage(font,maxfont,ftemp,file)==0)
		 {
			 okcnt++;
            printf("-OK.\n");
		 }
		 else
		 {
		    errcnt++;
		    printf("-FAIL.\n");
		 }
         fclose(ftemp);
		 fclose(file);
         fontEnd(font);
		 fclose(font);
         k = _findnext(handle,&FileInfo);
		 
	 }
	 _findclose(handle);
     _unlink(TEMP_FILE);
	 fprintf(header,"\n\n\n\n#endif // end of #ifndef FONT_H\n");
     maxFontEncodeEnd(maxfont);
	// fontEnd(font);
	// fclose(font);
	 fclose(maxfont);
	 fclose(header);
    // draw_main_end();
     printf("LOG>>encode end.success = %d,error = %d\n",okcnt,errcnt);
	 return 0;
}
int main_decode(void)
{
    FILE *file;
	unsigned char lmax,smax,idx,strdata[2048],lan;
	unsigned int *head;
	FONT_LAN_INFO_T font;
	FONT_STR_INFO_T str;

	lan = 0;
    head = NULL;
	fopen_s(&file,OUTPUT_FILE,"rb");
	lmax = maxFontDecodeInit(file);
	printf("log >> find lanuage count : %d\n",lmax);
    draw_main_start();
DECODE_START:
    if(lmax<=lan)
        goto DECODE_END;
	maxFontDecodeGetLan(file,&font,lan++);
	smax = maxFontDecodeGetLanHeadSize(file,&font);
	if(head!=NULL)
		free(head);
	head = (unsigned int *)malloc(smax);
	smax = maxFontDecodeGetHead(file,&font,smax,head);
	printf("log >> len[%] find string count : %d\n",lan,smax);
	

	idx = 0;
	while(1)
	{
        if(maxFontDecodeGetStr(head,&str,idx++)<0)
			break;
        maxFontDecodeGetStrData(file,&font,&str,strdata);
		draw_main_ext(strdata,str.width,str.height);
		draw_main_return();
		goto DECODE_START;
	}
DECODE_END:
	draw_main_end();
	maxFontDecodeEnd(file);
	fclose(file);
	free(head);
	printf("log >> draw string end : %d\n",idx-1);
    return 0;

}
int main(int argc,char *argv[])
{
	char mode[16],sel;

	sel = 0;
	if(argc<2)
	{
		printf("max font function select.\n");
		printf("help:\n");
		printf("     -enc   : encode to maxfont\n");
		printf("     -dec   : decode to rawdata\n");
		scanf("%s",mode);
        if(strcmp("-enc",mode)==0)
			sel = 1;
		else if(strcmp("-dec",mode)==0)
			sel = 2;
		else
			sel = 0;
	}
	else
	{
		if(strcmp("-enc",argv[1])==0)
			sel = 1;
		else if(strcmp("-dec",argv[1])==0)
			sel = 2;
		else
			sel = 0;
	}

	if(sel == 1)
	{
		printf("log >> encode start\n");
        main_encode();
        printf("log >> encode end\n");
	}
	else if(sel == 2)
	{
		printf("log >> encode start\n");
        main_decode();
		printf("log >> decode end\n");
	}
	else
	{
        printf("log >> no function selected.\n");
	}
    
	 system("pause");

	 return 0;
}