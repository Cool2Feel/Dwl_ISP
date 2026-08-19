#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <io.h>
#include "config.h"
#include "menu.h"




static MENU_INFO_T userMenuTable[MENU_OP_MAX];
static u8_t R_MENU_MAX;


static int menu_string_check(char *str)
{
	int flag=0,i;

	i = 0;
	while(*str)
	{
		if(flag==0)
		{
			if(((*str >= 'A')&&(*str<='Z'))|| (*str>='0' && *str<='9')||(*str == '.'))
				flag = 1;
			else
				i++;
		}
		else if(*str == ',')
		{
			*str = 0;
			break;
		}
		str++;
	}

	return i;
}
static int menu_string_int(char *str)
{
	int i=0;

	while(*str)
	{
		i = i*10+(*str-'0');
		str++;
	}

	return i;
}



int user_menuInit(char *filename)
{
    FILE *file;
	char string[128];
	int i,idx,n,step,level;

	fopen_s(&file,filename,"r");
	if(file==NULL)
	{
		printf("menu : open file <%s> fail\n");
		return -1;
	}

    level = 0;
	i = -1;
	while(1)
	{
		 string[0]=0;
         fscanf_s(file,"%s",string,128);
         if(string[0] == 0)
         {
		 	printf("parse menu fail\n");
			return -2;
         }

		 n = menu_string_check(string);
  
		 if(level == 0)
		 {
		 	 if(strcmp(&string[n],".option_id")==0)
		 	 {
			 	level = 1;
				i++;
				step = 0;
		 	 }
			 else if(strcmp(&string[n],".options")==0)
			 {
			 	level = 1;
				step = 1;
			 }
			 else if(strcmp(&string[n],".option_type")==0)
			 {
			 	level = 1;
				step = 2;
			 }
			 else if(strcmp(&string[n],".option_sub")==0)
			 {
			 	level = 1;
				step = 3;
			 }
			 else if(strcmp(&string[n],".config_id")==0)
			 {
			 	level = 1;
				step = 4;
			 }
			 else if(strcmp(&string[n],".name")==0)
			 {
			 	level = 1;
				step = 5;
			 }
			 else if(strcmp(&string[n],".icon")==0)
			 {
			 	level = 1;
				step = 6;
			 }
			 else if(strcmp(&string[n],".subname")==0)
			 {
			 	level = 1;
				step = 7;
				idx = 0;
			 }
		 }
		 else if(level==1)
		 {
		 	 if(step == 0)
		 	 {
			 	if(strcmp(&string[n],"MENU_END")==0)
					break;
				else
				{
					if((strcmp(&string[n],"0")==0))
					{
						level = 0;
						userMenuTable[i].option_id = 0;
					}
					else if(strncmp(&string[n],"MENU",4)==0)
					{
						level = 0;
						userMenuTable[i].option_id = 1;
					}	
				}
		 	 }
			 else if(step==1)
			 {
			 	if(string[n]>='0' && string[n]<='9')
			 	{
					userMenuTable[i].options = menu_string_int(&string[n]);
					level = 0;
			 	}
			 }
			 else if(step == 2)
			 {
			 	if(string[n] =='O')
			 	{
					level = 0;
			 	}
			 }
			 else if(step == 3)
			 {
			 	if(string[n] == 'M')
			 	{
					level = 0;
			 	}
			 }
			 else if(step == 4)
			 {
			 	if(string[n] == 'C')
			 	{
                     userMenuTable[i].config_id = user_configFindId(&string[n]);
					 //printf("config_id=%s\n",&string[n]);
					 level = 0;
			 	}
				else if(string[n]>='0' && string[n]<='9')
					level = 0;
			 }
			 else if(step == 5)
			 {
			 	if(string[n] == 'R')
			 	{
					userMenuTable[i].name = user_stringFindStr(&string[n]);
					//printf("name=%s\n",&string[n]);
					level = 0;
			 	}
			 }
			 else if(step == 6)
			 {
			 	if((string[n] == 'R') || (string[n]>='0' && string[n]<='9'))
			 	{
					level = 0;
			 	}
			 }
			 else if(step == 7)
			 {
			 	if(idx>=userMenuTable[i].options)
					level = 0;
			 	if(string[n] == 'R')
			 	{
			 		//printf("subname[%d]=%s\n",idx,&string[n]);
					userMenuTable[i].subname[idx++] = user_stringFindStr(&string[n]);
			 	}
			 }
		 }
	}


	R_MENU_MAX = i;

	fclose(file);

//====debug====
/*
	for(i = 0;i < R_MENU_MAX;i++)
	{
		printf("Menu id=%d,ops=%d,type=%d,sub=%d,",userMenuTable[i].option_id,userMenuTable[i].options,userMenuTable[i].option_type,userMenuTable[i].option_sub);
		printf("cfgid=%d,name=%d,icon=%d,sub_name1=%d,2=%d,3=%d,4=%d\n",userMenuTable[i].config_id,userMenuTable[i].name,userMenuTable[i].icon,userMenuTable[i].subname[0],userMenuTable[i].subname[1],userMenuTable[i].subname[2],userMenuTable[i].subname[3]);

	}
*/
//===end debug===

	return 0;
}



MENU_INFO_T *user_menuFind(int i)
{
	if(i>=R_MENU_MAX)
		return NULL;
	return &userMenuTable[i];
}









