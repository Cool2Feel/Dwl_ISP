/****************************************************************************
**
 **                              RESOURCE 
  ** *   **             THE APPOTECH MULTIMEDIA PROCESSOR
   **** **                  RESOURCE FONT
  *** ***
 **  * **               (C) COPYRIGHT 2016 BUILDWIN 
**      **                         
         **         BuildWin SZ LTD.CO  ; VIDEO PROJECT TEAM
          **   
* File Name   : font.c
* Author      : Mark.Douglas 
* Version     : V100
* Date        : 09/22/2016
* Description : 
*               
* History     : 
* 2016-09-22  : 
*      <1>.This is created by mark,set version as v100.
*      <2>.Add basic functions & information
******************************************************************************/

#include <stdio.h>
#include <io.h>
#include <stdlib.h>
#include "dfont.h"
#include "draw.h"

#define  FONT_DATA                RES_RESFONT
#define  FONT_INDEX              RES_RESFONTIDX


typedef struct Font_Data_S
{
	INT16U width;
	INT16U height;
	INT32U offset;
}Font_Data_T;

typedef struct Font_Idx_S
{
	INT32U index;
	INT32U offset;
}Font_Idx_T;
typedef struct Font_Str_S
{
	INT16U width;
	INT16U height;
	INT16U number;
	INT16U offset;
}Font_Str_T;

typedef struct Font_Ctrl_S
{
	INT8U lanMax;
	INT8U strMax;
	INT8U curLan;
	INT8U curStr;

    INT32U curChar;
	INT32U charMax;

	INT32U curOffset;

	Font_Str_T strInfo;
	Font_Data_T charInfo;

	INT32U addrData;
	INT32U addrIndex;

	INT16U strCache[128];
    INT8U  charCache[1024];

	FILE *fdata;
	FILE *findex;
}Font_Ctrl_T;



static Font_Ctrl_T fontCtrl;

/*******************************************************************************
* Function Name  : fontInit
* Description    :  font initial
* Input          : 
* Output         : none                                            
* Return         : int : 
*******************************************************************************/
int dfontInit(FILE *fdata,FILE *findex)
{
	int addr;

	fontCtrl.curStr = 0xff;
	fontCtrl.curLan = 0xff;
	fontCtrl.strMax = 0;
	fontCtrl.curChar= 0xffffffff;

	fontCtrl.fdata = fdata;
	fontCtrl.findex = findex;

	fread(&fontCtrl.charMax,4,1,fontCtrl.fdata);

	fread(&addr,4,1,fontCtrl.findex);

	if((addr&0x00ffffff) != 0x0058414d)
	{
		printf("font : initial fail.index error\n");
		return -2;
	}
    fontCtrl.lanMax = (addr>>24)&0xff;
	printf("font : initial ok.max lan =%d,max char %d\n",fontCtrl.lanMax,fontCtrl.charMax);
    return 0;	
}
/*******************************************************************************
* Function Name  : fontSetLanguage
* Description    :  set current lanaguage
* Input          : INT8U num : lan index 
* Output         : none                                            
* Return         : int : 
*******************************************************************************/
int fontSetLanguage(INT8U num)
{
	Font_Idx_T index;
	
	if(num>=fontCtrl.lanMax)
		return -1;

	if(num == fontCtrl.curLan)
		return 0;

	fseek(fontCtrl.findex,(num+1)*8,0);
	fread(&index,8,1,fontCtrl.findex);

	if(num!=(index.index&0xff))
		return -1;
	fontCtrl.curOffset = index.offset;
	fontCtrl.curLan = num;
	fontCtrl.strMax = (index.index>>8)&0xff;
// all cache is invliad	
	fontCtrl.curStr = 0xff;
	fontCtrl.curChar= 0xffffffff;
    printf("font : language = %d,str max = %d\n",num,fontCtrl.strMax);
	return fontCtrl.curLan;
}
/*******************************************************************************
* Function Name  : fontGetCharData
* Description    :  get char data
* Input          : INT8U num : char offset 
                      INT8U *buffer : dest buffer
* Output         : none                                            
* Return         : int : 
*******************************************************************************/
static int fontGetCharData(INT32U num,INT8U *buffer)
{
	if(num>=fontCtrl.charMax)
		return -1;

	if(num != fontCtrl.curChar)
	{
		fseek(fontCtrl.fdata,(num+1)*8,0);
		fread(&fontCtrl.charInfo,8,1,fontCtrl.fdata);
		//nv_read(fontCtrl.addrData+(num+1)*8,&fontCtrl.charInfo,8);
	}
	fseek(fontCtrl.fdata,fontCtrl.charInfo.offset,0);
	fread(buffer,fontCtrl.charInfo.height*((fontCtrl.charInfo.width+7)>>3),1,fontCtrl.fdata);
   // nv_read(fontCtrl.addrData+fontCtrl.charInfo.offset,buffer,fontCtrl.charInfo.height*((fontCtrl.charInfo.width+7)>>3)); // read data

   // uart_PrintfBuf(buffer,fontCtrl.charInfo.height*((fontCtrl.charInfo.width+7)>>3));
	return 0;
}
/*******************************************************************************
* Function Name  : fontGetStringInfo
* Description    :  get string info 
* Input          : INT8U num : string index
                      INT16U *width:string width
                      INT16U *height:string height
* Output         : none                                            
* Return         : int : 
*******************************************************************************/
int fontGetStringInfo(INT8U num,INT16U *width,INT16U *height)
{
	if(num>=fontCtrl.strMax)
		return -1;
	if(num!=fontCtrl.curStr)
	{
		fseek(fontCtrl.findex,fontCtrl.curOffset+(num+1)*8,0);
		fread(&fontCtrl.strInfo,8,1,fontCtrl.findex);
		//nv_read(fontCtrl.addrIndex+fontCtrl.curOffset+(num+1)*8,&fontCtrl.strInfo,8);
	}

	if(width)
		*width = fontCtrl.strInfo.width;
	if(height)
		*height = fontCtrl.strInfo.height;

	return 0;
}
/*******************************************************************************
* Function Name  : fontGetString
* Description    :  get string data 
* Input          : INT8U num : string index
                      INT16U *buffer: dest buffer
* Output         : none                                            
* Return         : int : 
*******************************************************************************/
static int fontGetString(INT8U num,INT16U *buffer)
{
	if(num>=fontCtrl.strMax)
		return -1;
	if(num!=fontCtrl.curStr)
	{
		fseek(fontCtrl.findex,fontCtrl.curOffset+(num+1)*8,0);
		fread(&fontCtrl.strInfo,8,1,fontCtrl.findex);
		//nv_read(fontCtrl.addrIndex+fontCtrl.curOffset+(num+1)*8,&fontCtrl.strInfo,8);
	}
    fseek(fontCtrl.findex,fontCtrl.curOffset+fontCtrl.strInfo.offset,0);
	fread(buffer,fontCtrl.strInfo.number<<1,1,fontCtrl.findex);
//	nv_read(fontCtrl.addrIndex+fontCtrl.curOffset+fontCtrl.strInfo.offset,buffer,fontCtrl.strInfo.number<<1);

	return fontCtrl.strInfo.number;
}
/*******************************************************************************
* Function Name  : fontDrawChar
* Description    :  draw char 
* Input          : INT8U *dest : destination buffer
                      INT8U *charsrc :char buffer
                      INT16S x    : x position
                      INT16S y    : y position
                      INT16U char_w: char width
                      INT16U char_h: char height
                      INT16U width : buffer size
                      INT8U color : color
* Output         : none                                            
* Return         : int : 
*******************************************************************************/
static int fontDrawChar(color_t *dest,INT8U *charsrc,INT16S x,INT16S y,INT16U char_w,INT16U char_h,INT16U dest_w,color_t color)
{
	INT8U i,j,temp,mask;
	color_t *line;

	if(charsrc == NULL || dest == NULL)
		return -1;
		
	line = dest+(x)+((y*dest_w));

	for(i=0;i<char_h;i++)
	{
	    for(j=0;j<(char_w+7)/8;j++)
		{
	         temp = *charsrc++;
			 for(mask=0;mask<8;mask++)
			 {
				 if(temp&(0x80>>mask))
				 {
					 *line++ = color;
				 }
				 else
				 {
					 line++;
				 }
			 }
		 }
		 y++;
		 line =dest+(x)+((y*dest_w));
	}
	return 0;  
}
int fontCharDraw(color_t *dest,INT16U num,INT16S x,INT16S y,INT16U width,color_t color)
{
    fontGetCharData(num,fontCtrl.charCache);

	draw_main_ext(fontCtrl.charCache,fontCtrl.charInfo.width,fontCtrl.charInfo.height);

	printf("draw <%d> ,%d,%d,0x%x,len = %d\n",num,fontCtrl.charInfo.width,fontCtrl.charInfo.height,fontCtrl.charInfo.offset,fontCtrl.charInfo.height*((fontCtrl.charInfo.width+7)>>3));
	printout(fontCtrl.charCache,fontCtrl.charInfo.height,fontCtrl.charInfo.offset,fontCtrl.charInfo.height*((fontCtrl.charInfo.width+7)>>3));
//    fontDrawChar(dest,fontCtrl.charCache,x,y,,width,color);

	return 0;
}
/*******************************************************************************
* Function Name  : fontDrawString
* Description    :  draw string 
* Input          : INT8U *dest : destination buffer
                      INT8U num  : string index
                      INT16S x    : x position
                      INT16S y    : y position
                      INT16U width : buffer size
                      INT8U color : color
* Output         : none                                            
* Return         : int : 
*******************************************************************************/
int fontDrawString(color_t *dest,INT8U num,INT16S x,INT16S y,INT16U width,color_t color)
{
	INT8U charcnt,i;	
	INT16S sx;

	if(fontGetString(num,fontCtrl.strCache)<0)
		return -1;
	charcnt = fontCtrl.strInfo.number;
    if(charcnt>128)
		charcnt = 128;

    sx = x;
	for(i=0;i<charcnt;i++)
	{
		if(fontGetCharData(fontCtrl.strCache[i],fontCtrl.charCache)<0)
		{
			sx+=fontCtrl.charInfo.width;  // last char width
			continue;
		}
		if((sx+fontCtrl.charInfo.width)>width)
			break;
		fontDrawChar(dest,fontCtrl.charCache,sx,y,fontCtrl.charInfo.width,fontCtrl.charInfo.height,width,color);

		sx+=fontCtrl.charInfo.width+2;  // last char width
	}

	return 0;
}








