#include <stdio.h>
#include <stdlib.h>
#include <io.h>
#include <string.h>
#include <windows.h>
const char iconTable_C[]=
{"/****************************************************************************\n\
       ***             ***                      MAXLIB-GRAPHC                  \n\
      ** **           ** **                                                    \n\
     **   **         **   **            THE MAXLIB FOR IMAGE SHOW PROCESS      \n\
    **     **       **     **                                                  \n\
   **       **     **       **              MAX ROURCE ICON MANAGEMENT         \n\
  **         **   **         **                                                \n\
 **           ** **           **              (C) COPYRIGHT 2016 MAX           \n\
**             ***             **                                              \n\
                                                                               \n\
* File Name   : user_icon.c                                                    \n\
* Author      : Mark.Douglas                                                   \n\
* Version     : V0100                                                          \n\
* Date        : 05/25/2016                                                     \n\
* Description : This file for maxlib resource icon managemant                  \n\
*                                                                              \n\
* History     :                                                                \n\
* 2016-05-25  :                                                                \n\
*      <1>.This is created by mark,set version as v0103.                       \n\
*      <2>.Add basic functions.                                                \n\
******************************************************************************/\n"
};
const char icontable_I[]=
{
	"#include \"../application.h\""
};

static unsigned char tColor[256],tColorCnt;

unsigned int string2pixel(char *string)
{
	unsigned int s,d,i;
	char c;

	string[10] = 0;

	s = 0;
	for(i=0;i<8;i++)
	{
		s<<=4;
		c = string[2+i];
        if(c>='0' && c<='9')
			s +=c-'0';
		else if(c>='a' && c<='f')
			s +=10+c-'a';
		else if(c>='A' && c<='F')
			s +=10+c-'A';
	}
    d = s;
//	s = atoi(string);

//	d = ((s&0xf8000000)>>11)| ((s&0x000000f8)<<8)|  ((s&0x0000fc00)>>5)| ((s&0x00f80000)>>19);

	return d;
}

int tColorLoader(char *filename)
{
    FILE *file;

	char string[16];
	unsigned int pixel,i,idx;

	tColorCnt = 0;
    fopen_s(&file,filename,"r");
	if(file == NULL)
	{
		printf("open file fail.%s\n",filename);
		return -1;
	}

    while(1)
	{
		 string[0] = 0;
		 fread(string,1,1,file);
		 if(string[0] == '{')
			 break;
		 else if(string[0] == 0)
		 {
			 printf("find palate fail\n");
			 return -1;
		 }
	}
    idx =0;
	i  = 0;
    while(1)
	{
		string[0] = 0;
		fread(string,1,1,file);
		if(string[0] == '}' || string[0] == 0)
			break;
		else if(string[0] == 0x0d || string[0] == 0x0a)
			continue;
		fread(&string[1],10,1,file);
        pixel  = string2pixel(string);

		if(pixel&0xff000000)
		{

		}
		else
		{
             tColor[tColorCnt++] = idx;
		}
		idx++;
		if(idx>=(256-16))
			break;
	}

	fclose(file);
	return tColorCnt;
}
int tColorCheck(int pixel)
{
	int i;

	for(i=0;i<tColorCnt;i++)
	{
		if(pixel == tColor[i])
			return pixel;
	}
	return -1;
}
char *string2filename(char *str)
{
	char name[128],*tar,*src;

	tar = name;
    src = str;
	while(*src)
	{
		if(*src == '/' || *src == '\\')
			tar = name;
		else
			*tar++ = *src;
		src++;
	}
	*tar = 0;
	strcpy_s(str,128,name);

	return str;
}
int stringHandler(char *str,char *str2)
{
	int i;
	char c,*src;

    src = str;
	if(*str == 0)
		return -1;

	for(i=0;i<128;i++)
	{
		c = *src;
		if(c == '\0')
			break;
		if(c == ',' || c<=' ' || c>(char )127)
		{
			*src = 0;
			break;
		}
		src++;
	}
    sprintf_s(str2,128,"%s",str);
    return 0;
}

/*

int main(int argc,char *argv[])
{
   FILE *in,*outc,*outh;
   char name[128],idname[128];
   int flag;

   unlink("str_table.c");
   unlink("str_table.h");

   fopen_s(&in,"font.txt","r");

   fopen_s(&outc,"str_table.c","w");
   fopen_s(&outh,"str_table.h","w");

   if(in==NULL || outc == NULL || outh == NULL)
	   goto END;
   fprintf(outh,"enum\n{\n");
   fprintf(outc,"R_STRING_T User_String_Table[R_STR_MAX&0xffff] = \n{\n");
   sprintf(idname,"R_ID_");
   flag = 0;
   while(1)
   {
	   name[0] = 0;
       fscanf(in,"%s",name);
       if(stringHandler(name,&idname[5])<0)
		   break;
	   
       fprintf(outh,"   %s",idname);
       if(flag==0)
	   {
		     fprintf(outh," = R_ID_TYPE_STR,\n",idname);
			 flag = 1;
	   }
	   else 
		   fprintf(outh,",\n");
	   fprintf(outc,"   {%s,  (void *)0,0,0,%s},\n",idname,name);
   }

   fprintf(outc,"\n};\n");
   fprintf(outh,"   \nR_STR_MAX\n};\n");
END:
   if(in)
	   fclose(in);
   if(outh)
	   fclose(outh);
   if(outc)
	   fclose(outc);
	system("pause");
}

*/
int stringChange(char *str1,char *str2)
{
    char *src;
	int flag;

	src = str1;
	if(*src == 0)
		return -1;
	else if(*src!='O')
		return 0;
	if(strncmp(src,"OSD_",4)==0)
		src+=4;
	if(*src == 'I')
		flag = 1;
	else
		flag = 0;

	while(*src!=0 && flag)
	{
		if(*src == 'N')
		{
			src++;
			break;
		}
		src++;

	}

	sprintf_s(str2,128,"%s",src);
	return 1;
}
int string2int(char *str)
{
	int value;

	value = 0;
	while(*str)
	{
		if(*str>='0' && *str<='9')
		{
			value = value*10+*str-'0';
		}
		str++;
	}

	return value;
}
char *string2hdefine(char *str)
{
	static char string[32],*tar;

	tar = string;
	while(*str)
	{
		if(*str == '.')
			*tar = '_';
		else if(*str>='a' && *str<='z')
			*tar = *str-('a'-'A');
		else
			*tar = *str;
		str++;
		tar++;
	}
	*tar = 0;

	return string;
}
int memcmplen(char *str1,char *str2,int len)
{
	int i;
	for(i=0;i<len;i++)
	{
		if(str1==NULL)
			return -1;
		if(str2==NULL)
			return 1;
		if(*str1>*str2)
			return 1;
		if(*str1<*str2)
			return -1;
		str1++;
		str2++;
	}
	return 0;
}
typedef struct
{
	unsigned long xwidth;
	unsigned long yheight;
	unsigned long dataoffset;
} osdsource_info_t;


int osdGetIconInfo(FILE *file,int icon,osdsource_info_t *cinfo,unsigned int tcolor)
{
	int pixel,i;
	unsigned char *mem,color;



    fseek(file,icon*sizeof(osdsource_info_t),0);
	fread(cinfo,sizeof(osdsource_info_t),1,file);

	if((tcolor&0xff000000) == 0)
		return 0;

	fseek(file,cinfo->dataoffset,0);

	mem = (unsigned char *)malloc(cinfo->xwidth*cinfo->yheight+1024);
	//printf("test0\n");
	fread(mem,cinfo->xwidth*cinfo->yheight,1,file);
	//printf("test1\n");
    color = tcolor&0xff;
	if(color == 0xff)
		color = mem[0];
	for(i=0;i<(cinfo->xwidth*cinfo->yheight);i++)
	{
		pixel = tColorCheck(mem[i]);
		if(pixel>=0)
			break;
     //   if(mem[i] == color)
	//		break;
	}
	//if(i>=(cinfo->xwidth*cinfo->yheight))
	if(pixel<0)
	    color = mem[0];
	else
		color = pixel&0xff;
	free(mem);
	//printf("test2\n");
	
	
    pixel = 0xff000000|color;
	return pixel;
}

void user_fscanf(FILE *file,char *buffer,int len)
{
	int i,j;
	char tchar;

	for(i=0,j=0;j<len;i++)
	{
		tchar = 0;
		fread(&tchar,1,1,file);
        if(tchar==0)
			return ;
		if(tchar == ' ' || tchar == '\n')
		{
			if(j==0)
				continue;
			break;
		}
		buffer[j++] = tchar;

	}
    buffer[j] = 0;
   
}
int main(int argc,char *argv[])
{
	FILE *in,*outc,*outh,*osd,*paltte;
	osdsource_info_t icon;
	char sname[256],dname[256],strv[128],flag;
	int ret,value,index,i;
    unsigned int tcolor;

//	while(1);


//	printf("open %s,%x\n\n",argv[1],(int)in);
//	fread(sname,120,1,in);
//	printf("%s\n",sname);
	if(argc>2)
	    fopen_s(&osd,argv[2],"rb");
	else
	    fopen_s(&osd,"OSD_source.bin","r");

	if(osd == NULL)
		return -1;
	if(argc>3)
	{
        _unlink(argv[3]);
		fopen_s(&outc,argv[3],"w");
	}
	else
	{
        _unlink("user_icon.c");
	    fopen_s(&outc,"user_icon.c","w");
	}
	if(outc==NULL)
		return -1;
	fprintf(outc,"%s\n",iconTable_C);
	fprintf(outc,"%s\n",icontable_I);

	if(argc>4)
	{
         _unlink(argv[4]);
		 fopen_s(&outh,argv[4],"w");
		 fprintf(outc,"#include \"%s\"\n",string2filename(argv[4]));
	}
	else
	{
	     _unlink("user_icon.h");
	     fopen_s(&outh,"user_icon.h","w");
         fprintf(outc,"#include \"user_icon.h\"\n");
	}
	if(outh == NULL)
		return -1;
    fprintf(outh,"%s\n",iconTable_C);

    if(argc>4)
	{
		fprintf(outh,"#ifndef %s \n   #define %s\n\n\n\n",string2hdefine(string2filename(argv[4])),string2hdefine(string2filename(argv[4])));
	}
	else
	{
        fprintf(outh,"#ifndef %s \n   #define %s\n\n\n\n",string2hdefine("max_user_icon.h"),string2hdefine("max_user_icon.h"));
	}
	if(argc>3)
        ret = tColorLoader(argv[5]);
	else
		ret = tColorLoader("palette.txt");
	printf("loader palette : res = %d\n",ret);

	_unlink("r_palette.h");
	fopen_s(&paltte,"r_palette.h","w");

    fprintf(paltte,"%s\n",iconTable_C);
	fprintf(paltte,"#ifndef %s \n   #define %s\n\n\n\n",string2hdefine("r_palette.h"),string2hdefine("r_palette.h"));
//	fseek(in,0x150,SEEK_SET);
	if(argc>1)
        fopen_s(&in,argv[1],"r");
	else
	    fopen_s(&in,"OSD_source.h","r");
	if(in==NULL)
		return -1;
	i = 0;
	while(1)
	{
         sname[i] = 0;
         fread(&sname[i],1,1,in);
		 if(sname[i] == 0)
			 return -1;
		 else if(sname[i] == ' ' || sname[i] == '\n')
			 sname[i] = 0;
		 else
		 {
			 i++;
			 continue;
		 }
		 i = 0;
	//	 fscanf_s(in,"%s",sname,128);

	//	 if(sname[0] == 0)
	//		 return -1;
         if(strcmp(sname,"information")==0)
			 break;
		 
	}
    
	fprintf(outc,"R_ICON_T User_Icon_Table[R_ICON_MAX&0xffff]=\n{\n");
	fprintf(outh,"enum\n{\n");
    sprintf_s(dname,128,"R_ID_ICON_");
    flag = 0;
    value = 0;
	tcolor = 0xff0000ff;
	while(1)
	{
		sname[0] = 0;
	//	fscanf_s(in,"%s",sname,128);
		user_fscanf(in,sname,128);
	//	printf("%s\n",sname);
        ret = stringChange(sname,&dname[10]);
		if(ret<0)
			break;
		else if(ret == 0)
		{
			if(sname[0] == 'P')
			{
				fprintf(paltte,"#define  %-40s",sname);
				user_fscanf(in,sname,128);
              //  fscanf_s(in,"%s",sname,128);
				fprintf(paltte,"%s\n",sname);
			}
			continue;
		}
		fprintf(outh,"   %s",dname);
		if(flag == 0)
		{
			fprintf(outh,"=R_ID_TYPE_ICON, \n");
			flag = 1;
		}
		else
			fprintf(outh,",\n");
		if(memcmplen(&dname[10],"MENU",4)==0 || memcmplen(&dname[10],"MT",2)==0 || memcmplen(&dname[10],"BUTTON",6)==0)
			tcolor |= 0xff000000;
		else
			tcolor &= ~(0xff000000);
      //  fscanf_s(in,"%s",strv,4);
		user_fscanf(in,strv,4);		
        value = string2int(strv);
        index = value;
        if(index == 0x1e)
		{
			index = index;
		}
		value = osdGetIconInfo(osd,value,&icon,tcolor);	
		if(value!=0)
			tcolor = value;
//		if(memcmplen(&dname[10],"MENU",4)==0 || memcmplen(&dname[10],"MT",2)==0 || memcmplen(&dname[10],"BUTTON",6)==0)
//			value = 0xff000000|(value&0xff);
		//else
		//	value = 0;
		fprintf(outc,"   {%-40s,\t(void *)0,0,%3d,%3d,0x%08x,0x%08x},\n",dname,icon.xwidth,icon.yheight,value,index);//icon.dataoffset);
	}

	fprintf(outh,"\n  R_ICON_MAX\n};\n");
    fprintf(outh,"extern R_ICON_T User_Icon_Table[];\n\n");
	fprintf(outh,"\n\n\n#endif\n\n");
	
	fprintf(outc,"};\n");
	fprintf(paltte,"\n\n\n#endif\n\n");
	
	fclose(paltte);
	fclose(osd);
	fclose(in);
	fclose(outc);
	fclose(outh);
    
	printf("build icon ok!\n");
	Sleep(3000);
//	system("pause");
	return 0;
}