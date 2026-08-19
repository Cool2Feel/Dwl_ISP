#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <io.h>
#include <time.h>
#include "config.h"
#include "menu.h"

static int str_cfgid_check(char *string)
{
	int cnt =0;
	while(*string)
	{
		if(*string == ' ' || *string == ':')
		{
			return cnt;
		}
		string++;
		cnt++;
	}
}

static int str_setting_check(char *string)
{
	int cnt =0;
	while(*string)
	{
		if(*string == ' ' || *string == '\r' || *string == '\n'|| *string == '#')
		{
			return cnt;
		}
		string++;
		cnt++;
	}
}

static char* str_cut_unuser(char *string)
{
	int cnt =0;
	while(*string)
	{
		if(*string == ' ')
		{
			string++;
			cnt++;
		}
		else
		{
			return cnt;
		}
	}
}



int user_setting_init(char *filename)
{

	MENU_INFO_T *menu;
	FILE *file;
	char *string_buf;
	int file_size;
	int pos=0;
	int ret;
	char cfgid_str[64];
	char setting_str[64];
	char temp[64];
	int idx;
	
	fopen_s(&file,filename,"r");
	if(file== NULL)
	{
		printf("string : find file <%s> fail\n",filename);
		return -1;
	}
	
	fseek(file,0,SEEK_END);
	file_size = ftell(file);
	fseek(file,0,SEEK_SET);
	
	if(file_size <= 0)
	{
		printf("string : size err\n");
		return -1;
	}
	//printf("file_size=0x%x\n",file_size);
	string_buf = (char *)malloc(file_size+1);
	if(string_buf==NULL)
	{
		printf("string_buf malloc fail\n");
		fclose(file);
		return -1;
	}
	memset(string_buf,0,sizeof(file_size+1));
	fread(string_buf,file_size,1,file);
	
	if(string_buf[0] == 0)
	{
		printf("load string table fail\n");
		free(string_buf);
		fclose(file);
		return -2;
	}
	//printf("string_buf=%s\n",string_buf);
	pos=0;

    while(1)
	{

		ret = my_str_cmp(string_buf+pos,"CONFIG",6);
		if(ret > 0)	// find CONFIG_ID_
		{
		 	int len;
			char *str;
		 	pos += (ret-6);
			//==get cfgid str===
			len = str_cfgid_check(string_buf+pos);
			memset(cfgid_str,0,sizeof(cfgid_str));
		 	memcpy(cfgid_str,string_buf+pos,len);
			//printf("cfgid_str=%s,len=0x%x\n",cfgid_str,len);
			pos += len;
			//==get seting str===
			ret = my_str_cmp(string_buf+pos,":",1);
			if(ret > 0)
			{
				pos += ret;
				len = str_cut_unuser(string_buf+pos);
				pos += len;
				len = str_setting_check(string_buf+pos);
				memset(setting_str,0,sizeof(setting_str));
				memcpy(setting_str,string_buf+pos,len);
				pos += len;
				#if (1 == TDEBUG)
				//printf("setting_str=%s,len=0x%x\n",setting_str,len);
				#endif
			}
			else
			{
				break;
			}
		}
		else
		{
			break;
		}
		

		if(strcmp(cfgid_str,"CONFIG_ID_YEAR")==0)
		{
			u32_t temp;
			temp = setting_str[0]-'0';
			temp = temp*10+setting_str[1]-'0';
			temp = temp*10+setting_str[2]-'0';
			temp = temp*10+setting_str[3]-'0';
			#if (1 == TDEBUG)
			printf("year=%d\n",temp);
			#endif
			userConfigSetValue(user_configFindId("CONFIG_ID_YEAR"),temp);
		}
		else if(strcmp(cfgid_str,"CONFIG_ID_MONTH")==0)
		{
			u32_t temp;
			temp = setting_str[0]-'0';
			temp = temp*10+setting_str[1]-'0';
			if(temp > 12 || (0 == temp))
			{
				temp = 1;
			}
			#if (1 == TDEBUG)
			printf("mon=%d\n",temp);
			#endif
			userConfigSetValue(user_configFindId("CONFIG_ID_MONTH"),temp);
		}
		else if(strcmp(cfgid_str,"CONFIG_ID_MDAY")==0)
		{
			u32_t temp;
			temp = setting_str[0]-'0';
			temp = temp*10+setting_str[1]-'0';
			if(temp > 31 || (0 == temp))
			{
				temp = 1;
			}
			#if (1 == TDEBUG)
			printf("day=%d\n",temp);
			#endif
			userConfigSetValue(user_configFindId("CONFIG_ID_MDAY"),temp);
		}
		else if(strcmp(cfgid_str,"CONFIG_ID_HOUR")==0)
		{
			u32_t temp;
			temp = setting_str[0]-'0';
			temp = temp*10+setting_str[1]-'0';
			if(temp >= 24)
			{
				temp = 0;
			}
			#if (1 == TDEBUG)
			printf("hour=%d\n",temp);
			#endif
			userConfigSetValue(user_configFindId("CONFIG_ID_HOUR"),temp);
		}
		else if(strcmp(cfgid_str,"CONFIG_ID_MIN")==0)
		{
			u32_t temp;
			temp = setting_str[0]-'0';
			temp = temp*10+setting_str[1]-'0';
			if(temp >=60)
			{
				temp = 0;
			}
			#if (1 == TDEBUG)
			printf("min=%d\n",temp);
			#endif
			userConfigSetValue(user_configFindId("CONFIG_ID_MIN"),temp);
		}
		else if(strcmp(cfgid_str,"CONFIG_ID_SEC")==0)
		{
			u32_t temp;
			temp = setting_str[0]-'0';
			temp = temp*10+setting_str[1]-'0';
			if(temp >=60)
			{
				temp = 0;
			}
			#if (1 == TDEBUG)
			printf("sec=%d\n",temp);
			#endif
			userConfigSetValue(user_configFindId("CONFIG_ID_SEC"),temp);
		}
		else
		{
			idx = user_stringFindCfg(setting_str);
			if(-1 == idx)
			{
			 	printf("SDK not find this setting!!\n");
			}
			else
			{
				userConfigSetValue(user_configFindId(cfgid_str),idx+R_ID_TYPE_STR);
			}
			#if (1 == TDEBUG)
			printf("setting_str=%s,idx=0x%x\n",setting_str,idx+R_ID_TYPE_STR);
			#endif
		}
	}

	free(string_buf);
	fclose(file);

	return 0;
}


