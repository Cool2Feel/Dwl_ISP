#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <io.h>
#include <string.h>
typedef struct font_info_s
{
	unsigned char width;
	unsigned char heigth;
	unsigned short length;
	unsigned int  addr;
}font_info_t;
void pixelset(unsigned char *table,int x,int y,int width)
{
	unsigned char *line,i,j;

	i = x/8;
	j = x%8;
	line = width*y+table;

    line[i]|= 0x80>>j;
}
void stringupcase(char *str)
{
	char value;

	while(1)
	{
		value = *str;
		if(value == '\0')
			break;
		if(value >= 'a' && value<='z')
			value -= 'a'-'A';
		*str++ = value;
	}
}
#if 1
int main(int argc,char *argv[])
{
   FILE *fin,*fout,*fouth,*fdraw;
   unsigned int temp32,i,j,umax,idx,strlen;
   font_info_t font;
   unsigned char doxmat[512],edoxmat[512],temp8,mask;
   char inputfilename[128],fonttype[32],string[64];

    
   printf("welcome to max font program\nplease input fonttype:");
   scanf("%s",fonttype);
   printf("please input font file name:");
   scanf("%s",inputfilename);

   printf("start...\n");

   sprintf(string,"ascii_%s.h",fonttype);
   unlink(string);
   fouth=fopen(string,"w");

   sprintf(string,"ascii_%s.c",fonttype);
   unlink(string);
   fout= fopen(string,"w");

   sprintf(string,"ascii_%s_draw.c",fonttype);
   unlink(string);
   fdraw=fopen(string,"w");
  // fin = fopen("font.bin","r");
   fin = fopen(inputfilename,"rb");
   
   if(fin==NULL || fout==NULL)
   {
	   printf("open file fail\n");
	   goto END_MAIN;
   }
   sprintf(string,"ASCII_%s_H",fonttype);
   stringupcase(string);
   fprintf(fouth,"#ifndef  %s\n   #define  %s\n\n\n",string,string);
   fprintf(fdraw,"#include \"ascii_%s.h\"\n\n\n\n",fonttype);
   fprintf(fdraw,"//-------------------------ascii table----------------------\n");
   fprintf(fdraw,"const unsigned char *ascii_%s_table[]=\n{\n",fonttype);
   fread(&umax,4,1,fin);
   idx = 0;
   while(1)
   {
       fread(&temp32,4,1,fin);
	   if(temp32>127)
		   break;
	   fprintf(fout,"const unsigned char ascii_%s_%02d[]= // '%c'\n{\n",fonttype,temp32,(char)temp32);
	   fprintf(fouth,"extern const unsigned char ascii_%s_%02d[]; // '%c'\n",fonttype,temp32,(char)temp32);
	   fprintf(fdraw,"   ascii_%s_%02d,// '%c'\n",fonttype,temp32,(char)temp32);
//---font info
	   fread(&temp32,4,1,fin);
	   temp32+=4;
       fseek(fin,temp32,SEEK_SET);
//---font doxmat
       fread(&font,8,1,fin);
	   fseek(fin,font.addr+4,SEEK_SET);
	   fread(doxmat,font.length,1,fin);


	   fprintf(fout,"   0x%02x,0x%02x,\n   ",font.width,font.heigth);
//-------------------------------------------------------------------
       for(i=0;i<512;i++)
           edoxmat[i] = 0;
	   j = 0;
	   for(i=0;i<font.length;i++)
	   {
           temp8 = doxmat[i];
		   if(i%font.width==0 && i!=0)
               j++;
           for(mask=0;mask<8;mask++)
		   {
			   if(temp8&(1<<mask))
			   {
                  pixelset(edoxmat,i%font.width,j*8+mask,(font.width+7)/8);
			   }
		   }

	   }

	   font.length = ((font.width+7)/8)*font.heigth;
	   for(i=0;i<font.length;i++)
	   {
           if((i%16) ==0 && i!=0)
			   fprintf(fout,"\n   ");
		   fprintf(fout,"0x%02x,",edoxmat[i]);
		   
	   }

	   fprintf(fout,"\n};\n");
	   idx++;
	   if(idx==umax)
		   break;
	   fseek(fin,idx*8+4,SEEK_SET);

   }


   fprintf(fouth,"#endif // #ifndef %s\n",string);
   fclose(fin);
   fclose(fouth);
   fclose(fout);

   fprintf(fdraw,"};\n\n\n");

   fprintf(fdraw,"const unsigned char *ascii_%s_get(char c,unsigned char *width,unsigned char *heigth)\n",fonttype);
   fprintf(fdraw,"{\n");
   fprintf(fdraw,"   const unsigned char *table;\n");
   fprintf(fdraw,"   unsigned char index;\n");
   fprintf(fdraw,"   if(c<32 || c == 34 || c>126)\n      return ((void *)0);\n");
   fprintf(fdraw,"   if(c<34)\n      index = c-32;\n   else\n      index = c-33;\n");
   fprintf(fdraw,"   table = ascii_%s_table[index];\n",fonttype);
   fprintf(fdraw,"   if(width)\n      *width = table[0];\n   if(heigth)\n      *heigth = table[1];\n");
   fprintf(fdraw,"   return &table[2];\n}\n");
   
   fclose(fdraw);

   printf("ok\n");
END_MAIN:
	system("pause");
	return 0;
}
#else
const unsigned char ascii_37[]= // '%'
{
   0x13,0x20,
   0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
   0x00,0x00,0x00,0x00,0x0c,0x02,0x00,0x12,0x04,0x00,0x21,0x04,0x00,0x21,0x08,0x00,
   0x21,0x08,0x00,0x21,0x10,0x00,0x12,0x20,0x00,0x0c,0x20,0x00,0x00,0x43,0x00,0x00,
   0x44,0x80,0x00,0x88,0x40,0x01,0x08,0x40,0x01,0x08,0x40,0x02,0x08,0x40,0x02,0x04,
   0x80,0x04,0x03,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
   0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
};
int main(int argc,char *argv[])
{
	unsigned char i,j,w,h,*table,temp,mask;
	FILE *file;

	table = ascii_37;
    
	w = table[0];
	h = table[1];

	unlink("temp.txt");
	file = fopen("temp.txt","w");

	table+=2;

	for(i=0;i<h;i++)
	{
         for(j=0;j<(w+7)/8;j++)
		 {
             temp = *table++;
			 for(mask=0;mask<8;mask++)
			 {
				 if(temp&(0x80>>mask))
				 {
					 printf("*");
					 fprintf(file,"%c",(char)223);
				 }
				 else
				 {
					 printf(" ");
					 fprintf(file,"  ");
				 }
			 }
		 }
		 printf("\n");
		 fprintf(file,"\n");
	}
	fclose(file);
    system("pause");
	return 0;  
}
#endif