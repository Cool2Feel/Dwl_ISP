#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <io.h>
#include "config.h"


#define  USER_STR_MAX        CONFIG_MAX

typedef struct USER_STR_S
{
	char str_name[50];
}USER_STR_T;


static USER_STR_T userStrTable[USER_STR_MAX];
static USER_STR_T userCfgTable[USER_STR_MAX];
static USER_STR_T userLanTable[USER_STR_MAX];
static u16_t R_STR_MAX,R_CFG_MAX,R_LAN_MAX;

static int str_id_check(char *string)
{
	int cnt =0;
	while(*string)
	{
		if(*string == ' ' || *string == ',' || *string == '=')
		{
			return cnt;
		}
		string++;
		cnt++;
	}
}

static int str_lan_check(char *string)
{
	int i,j;

	i=0;
    for(j=0;string[j]!=0;j++)
    {
		if(string[j]=='_')
			i = j+1;
    }
   
	return i; 
}

int user_stringInit(char *filename)
{
	FILE *file;
	char *string_buf;
	int i;
	int file_size;
	int pos;
	int ret;

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

	string_buf = (char *)malloc(file_size+1);
	if(string_buf==NULL)
	{
		printf("string_buf malloc fail\n");
		fclose(file);
		return -1;
	}
	memset(string_buf,0,sizeof(file_size+1));
//--------------load str table-------------------------------------	
//	printf("file_size=0x%x\n",file_size);
//	fscanf_s(file,"%s",string_buf,file_size);
    fread(string_buf,file_size,1,file);
	
	if(string_buf[0] == 0)
	{
		printf("load string table fail\n");
		free(string_buf);
		fclose(file);
		return -2;
	}
	//printf("string_buf=%s\n",string_buf);
	ret = my_str_cmp(string_buf,"enum",4);
	if(ret <=0)
	{
		 printf("can't find str\n");
		 free(string_buf);
		 fclose(file);
		 return -3;
	}
	pos = ret-4;

#if (1 == TDEBUG)
	printf("pos=0x%x\n",pos);
	printf("string_buf=%c\n",*(string_buf+pos));
	printf("string_buf=%c\n",*(string_buf+pos+1));
	printf("string_buf=%c\n",*(string_buf+pos+2));
	printf("string_buf=%c\n",*(string_buf+pos+3));
#endif

    i = 0;
	while(1)
	{
		 ret = my_str_cmp(string_buf+pos,"R_ID_STR_",9);
		 if(ret > 0)
		 {
		 	int len;
		 	pos += (ret-9);
			len = str_id_check(string_buf+pos);
			memset(userStrTable[i].str_name,0,sizeof(userStrTable[i++].str_name));
		 	memcpy(userStrTable[i++].str_name,string_buf+pos,len);
			//printf("rid_name=%s,len=0x%x\n",userStrTable[i-1].str_name,len);
			pos += len;
		 }
		 else
		 {
		 	break;
		 }
	}
    R_STR_MAX = i;
    userStrTable[i].str_name[0] = 0;
//----------------load configure table--------------------------------	

    i = 0;
	while(1)
	{
		 ret = my_str_cmp(string_buf+pos,"CFG_",4);
		 if(ret > 0)
		 {
		 	int len;
		 	pos += (ret-4);
			len = str_id_check(string_buf+pos);
			memset(userCfgTable[i].str_name,0,sizeof(userCfgTable[i++].str_name));
		 	memcpy(userCfgTable[i++].str_name,string_buf+pos,len);
			#if (1 == TDEBUG)
			printf("cfg_name=%s,len=0x%x\n",userCfgTable[i-1].str_name,len);
			#endif
			pos += len;
		 }
		 else
		 {
		 	break;
		 }
	}
	R_CFG_MAX = i;
	userCfgTable[i].str_name[0] = 0;
//----------------load language table--------------------------------	

    i = 0;
	while(1)
	{

		 ret = my_str_cmp(string_buf+pos,"LANUAGE_",8);
		 if(ret > 0)
		 {
		 	int len;
		 	pos += (ret-8);
			len = str_id_check(string_buf+pos);
			memset(userLanTable[i].str_name,0,sizeof(userLanTable[i++].str_name));
		 	memcpy(userLanTable[i++].str_name,string_buf+pos,len);
			#if (1 == TDEBUG)
			printf("lan_name=%s,len=0x%x\n",userLanTable[i-1].str_name,len);
			#endif
			pos += len;
		 }
		 else
		 {
		 	break;
		 }

	}
	R_LAN_MAX = i;
    userLanTable[i].str_name[0] = 0;
	free(string_buf);
	fclose(file);

	return 0;

}

int user_stringFindStr(char *str)
{
	int i;

	for(i=0;i<R_STR_MAX;i++)
	{
		if(strcmp(userStrTable[i].str_name,str)==0)
			return i;
	}

	return -1;
}


int user_stringFindCfg(char *str)
{
	int i;

	for(i=0;i<R_CFG_MAX;i++)
	{
		if(strcmp(userCfgTable[i].str_name,str)==0)
			return i;
	}

	return -1;
}

int user_stringFindLan(char *str)
{
	int i,n,m;

	n = str_lan_check(str);

	for(i=0;i<R_LAN_MAX;i++)
	{
		m = str_lan_check(userCfgTable[i].str_name);
		if(strcmp(&userCfgTable[i].str_name[m],&str[n])==0)
			return i;
	}

	return -1;
}





