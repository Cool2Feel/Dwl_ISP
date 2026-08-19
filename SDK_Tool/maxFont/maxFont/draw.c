#include <stdio.h>
#include <stdlib.h>
#include <io.h>

#define CHAR_INTER_VAL      2
#define  DRAW_WIDTH    320
#define  DRAW_HEIGHT   240
extern unsigned char *ascii_draw_get(char c,unsigned char *width,unsigned char *heigth);

static unsigned short *drawBuffer = NULL;
static unsigned short drawX,drawY;
static unsigned short  charWidth,charHeight;
static FILE *drawFile;
int draw_main_start(void)
{
	int i;
    drawBuffer = (unsigned short *)malloc(DRAW_WIDTH*DRAW_HEIGHT*2);
    drawX = 8;
    drawY = 8;
    _unlink("font_buff_rawdata.bin");
	fopen_s(&drawFile,"font_buff_rawdata.bin","w");
    if(drawFile == NULL)
	{
		printf("open draw file end.\n");
	}
	for(i=0;i<DRAW_WIDTH*DRAW_HEIGHT;i++)
       drawBuffer[i] = 0;
	return 0;
}
int draw_main_end(void)
{
    fwrite(drawBuffer,DRAW_WIDTH*DRAW_HEIGHT*2,1,drawFile);
	fclose(drawFile);
	free(drawBuffer);

	return 0;
}
int ascii_draw_char_ext(unsigned short *obuff,unsigned char *table,int x,int y,unsigned short width,unsigned short color)
{
	unsigned char i,j,temp,mask;
	unsigned short *line;

	if(table == ((void *)0))
		return -1;
		
	line = obuff+(x)+((y*width));

	for(i=0;i<charHeight;i++)
	{
     for(j=0;j<(charWidth+7)/8;j++)
		 {
       temp = *table++;
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
		 line =obuff+(x)+((y*width));
	}

	return ((charHeight<<8)|charWidth);  
}
int draw_main_ext(unsigned char *table,unsigned short width,unsigned short heigth)
{
	int x,y;

	if(table == NULL)
	{
		 drawX=drawX-12;
		 return 0;
	}

	if(drawY>(DRAW_HEIGHT-32))
		return 0;
	

	charWidth = width;
	charHeight= heigth;
 //   drawX=drawX-width+2;
    x = drawX;
	y = drawY;
	printf("draw position : %d,%d,(%d,%d)\n",width,heigth,x,y);

    ascii_draw_char_ext(drawBuffer,table,x,y,DRAW_WIDTH,0xffff);

    
	
	return 0;
}
int draw_main_return(void)
{
	if(drawX<(DRAW_WIDTH>>1))
		drawX += DRAW_WIDTH>>1;
	else
	{
		drawX=8;
        drawY+=32;
	}

	return 0;
}