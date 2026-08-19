/****************************************************************************
**
 **                              CONFIGURE
  ** *   **             THE APPOTECH MULTIMEDIA PROCESSOR
   **** **                  CONFIGURE
  *** ***
 **  * **               (C) COPYRIGHT 2016 BUILDWIN 
**      **                         
         **         BuildWin SZ LTD.CO  ; VIDEO PROJECT TEAM
          **   
* File Name   : config.c
* Author      : Mark.Douglas 
* Version     : V100
* Date        : 09/22/2016
* Description : This file is image encode file
*               
* History     : 
* 2016-09-22  : 
*      <1>.This is created by mark,set version as v100.
*      <2>.Add basic functions & information
******************************************************************************/
#include "config.h"
#include <stdio.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <io.h>


extern int scanInit(FILE *file);
extern int scanString(char *str,FILE *file);

static CONFIG_INDEX_T  ConfigNodePool[CONFIG_MAX];
static SYSTEM_FLAY    userConfigTable;
//char userConfigName[256];
static int cfgOffset;

#if 0
static int cfg_string_check(char *str)
{
	int flag=0,i;

	i = 0;
	while(*str)
	{
		if(flag==0)
		{
			if((*str == 'C')|| (*str>='0' && *str<='9'))
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
static int cfg_string_int(char *str)
{
	int i=0;

	while(*str)
	{
		i = i*10+(*str-'0');
		str++;
	}

	return i;
}


int user_configInit(char *filename)
{
	FILE *file;
    char string[128];
	int i,n,level,idx,id;

	fopen_s(&file,filename,"r");
	if(file==NULL)
	{
		printf("configure : open file <%s> fail\n",filename);
		return -1;
	}

    i = 0;
	id = 0;
	level = 0;
	scanInit(file);
	while(1)
	{
		 if(scanString(string,file)<0)
		 	break;

		 n = cfg_string_check(string);
		 if(level == 0)
		 {
			 if(strcmp(&string[n],"CONFIG_ID_MAX")==0)
			 {
				 #if (1 == TDEBUG)
				 printf("CONFIG_ID_MAX end\n");
				 #endif
			 	 break;
			 }
			 else if(strncmp(&string[n],"CONFIG_ID_",10)==0)
			 {
			 	 level = 1;
				 idx = 1;
				 memset(ConfigNodePool[i].item_id,0,sizeof(ConfigNodePool[i].item_id));
				 strcpy_s(ConfigNodePool[i].item_id,64,&string[n]);
				 #if (1 == TDEBUG)
				 printf("%s\n",ConfigNodePool[i].item_id);
				 #endif
			 }
		 }
		 else
		 {
		 	  if(idx == 1)
		 	  {
			  	  if(string[n]>='0' && string[n]<='9')
				  	idx++;
		 	  }
			  else if(idx == 2)
			  {
			  	  if(string[n]>='0' && string[n]<='9')
			  	  {
				  	   ConfigNodePool[i].item_rev = string[n]-'0';
					   ConfigNodePool[i].item_idx = id;
					   id+=ConfigNodePool[i].item_rev;
					   idx++;
			  	  }
			  }
			  else if(idx==3)
			  {
			  	  if(string[n]>='0' && string[n]<='9')
			  	  {
				  	   ConfigNodePool[i].item_default= cfg_string_int(&string[n]);
					   level = 0;
					   i++;
			  	  }
				  else if(string[n]=='C')
				  {
				  	   ConfigNodePool[i].item_default = user_stringFindCfg(&string[n]);
					   #if (1 == TDEBUG)
					   printf("config : [%d] %s = %d\n",ConfigNodePool[i].item_idx,&string[n],ConfigNodePool[i].item_default);
					   #endif
					   level = 0;
					   i++;
				  }
			  }
		 }		 
	}

	fclose(file);
    memset((void *)&userConfigTable,0,sizeof(SYSTEM_FLAY));
	
//====debug====
#if (1 == TDEBUG)
/*
	printf("CONFIG_ID_MAX=%d\n",CONFIG_ID_MAX);
	for(i = 0;i < CONFIG_ID_MAX;i++)
	{
		printf("item_id=%s,%d,%d\n",ConfigNodePool[i].item_id,ConfigNodePool[i].item_rev,ConfigNodePool[i].item_default);
	}
*/
#endif
//===end debug===
	return 0;
	
}
#else

static int str_cfg_len(char *string)
{
	int cnt =0;
	while(*string)
	{
		if(*string == ' ' || *string == ')'||*string == ',' || *string == ':')
		{
			return cnt;
		}
		string++;
		cnt++;
	}
}

static int str_cfg_space_len(char *string)
{
	int cnt =0;
	while(*string)
	{
		if(*string == ' '||*string == ',')
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

static int cfg_string_int(char *str)
{
	int i=0;

	while(*str)
	{
		i = i*10+(*str-'0');
		str++;
	}

	return i;
}

int user_configInit(char *filename)
{
	FILE *file;
	char *string_buf;
	int file_size;
	int pos;
	int ret;
	char setting_str[64];
	char temp[64];
	int idx;
	int i;

	
	fopen_s(&file,filename,"r");
	if(file== NULL)
	{
		printf("confige : find file <%s> fail\n",filename);
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
	#if (1 == TDEBUG)
	printf("file_size=0x%x\n",file_size);
	#endif
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
	pos = 0;
	idx = 0;
	
	while(1)
	{
		 ret = my_str_cmp(string_buf+pos,"CONFIG_ID_",10);
		 if(ret > 0) // find CONFIG_ID_
		 {
			 int len;
			 char *str;
			 pos += (ret-10);
			 //==get cfgid str===
			 len = str_cfg_len(string_buf+pos);
			 memset(ConfigNodePool[idx].item_id,0,sizeof(ConfigNodePool[idx].item_id));
			 memcpy(ConfigNodePool[idx].item_id,string_buf+pos,len);
			 #if (1 == TDEBUG)
			 printf("%s:",ConfigNodePool[idx].item_id,len);
			 #endif
			 pos += len;
			 //==get seting str===
			 len = str_cfg_space_len(string_buf+pos);
			 pos += len;
			 len = str_cfg_len(string_buf+pos);
			 memset(setting_str,0,sizeof(setting_str));
			 memcpy(setting_str,string_buf+pos,len);
			 #if (1 == TDEBUG)
			 printf("%s\n",setting_str,len);
			 #endif
			 //==get value==
			 if((setting_str[0]>='0') && (setting_str[0]<='9'))
			 {
				ConfigNodePool[idx].item_value = cfg_string_int(setting_str);
			 }
			 else
			 {
			 	int str_idx = user_stringFindStr(setting_str);
			 	if(-1 == str_idx)
			 	{
			 		printf("confige value err!!\n");
					idx = 0;
					break;
			 	}
				ConfigNodePool[idx].item_value = str_idx + R_ID_TYPE_STR;
			 }

			 idx ++;
		 }
		 else
		 {
			 break;
		 }
	}

	free(string_buf);
	fclose(file);

	memset(&userConfigTable,0,sizeof(userConfigTable));
	printf("CONFIG_ID count=%d\n",idx);
	for(i = 0;i < idx;i++)
	{
		userConfigSetValue(i,ConfigNodePool[i].item_value);
		#if (1 == TDEBUG)
		printf("cfg_id=%s,val=0x%x\n",ConfigNodePool[i].item_id,ConfigNodePool[i].item_value);
		#endif
	}

	return 0;
}
#endif


int user_configFindId(char *str)
{
	int i;
	for(i=0;i<CONFIG_MAX;i++)
	{
		if(strcmp(ConfigNodePool[i].item_id,str)==0)
			return i;
	}
	return -1;
}

int destin_bin_cfg_find(FILE *file)
{
	int mem=0,addr,size;

	fseek(file,4,SEEK_SET);
	fread(&mem,4,1,file);
	if(mem!=0x52444c42)
		return -1;
	mem = 0;
	fseek(file,9,SEEK_SET);
	fread(&mem,1,1,file);
	mem<<=4;
    fseek(file,mem+8,SEEK_SET);
	fread(&addr,4,1,file);
	fseek(file,mem+12,SEEK_SET);
	fread(&size,4,1,file);

	addr = (addr+size)<<9;
	
	if(addr&0xfff)
		addr = (addr&0xfffff000)+0x1000;  // 4096 algin

#if (1 == TDEBUG)
	printf("find cfg address:0x%x\n",addr);
#endif
	return addr;

}


int destin_bin_cfg_load(char *filename,u32_t buff,u32_t size)
{
    FILE *file;
//	int addr;

	fopen_s(&file,filename,"rb");
	if(file==NULL)
	{
		printf("cfg load : open file <%s> fail\n",filename);
		return -1;
	}
		
	cfgOffset = destin_bin_cfg_find(file);
	if(cfgOffset<0)
	{
		printf("find configure table fail\n");
		fclose(file);
		return -1;
	}

	fseek(file,cfgOffset,SEEK_SET);
	fread((void *)buff,size,1,file);
	fclose(file);

	return 0;
}
/*
int destin_bin_cfg_save(char *filename,char *newfile,u32_t buff,u32_t size)
{
    FILE *file,*file2;
	int addr,i,ret;
	char *mem;

    ret = 0;
	fopen_s(&file,filename,"rb");	
	fopen_s(&file2,"temp.bin","wb");
	if(file==NULL || file2==NULL)
	{
		printf("cfg save : open file <%s> fail\n",filename);
		ret = -1;
		goto SAVE_END;
	}		
	cfgOffset = destin_bin_cfg_find(file);
	if(cfgOffset<0)
	{
		printf("find configure table fail\n");
		ret = -1;
		goto SAVE_END;
	}
	mem = (char *)malloc(4096);
	if(mem==NULL)
	{
		printf("malloc fail\n");
		ret = -1;
		goto SAVE_END;
	}
	i=0;
	fseek(file,0,SEEK_SET);
	while(i!=cfgOffset)
	{
		fread(mem,4096,1,file);
		fwrite(mem,4096,1,file2);
		i+=4096;
	}
	fwrite((void *)buff,size,1,file2);
	fwrite((void *)buff,size,1,file2);

    size = 4096-size*2;
	memset(mem,0,4096);
	fwrite(mem,size,1,file2);

	if(file2)
		fclose(file2);
	if(file)
		fclose(file);
	unlink(newfile);
	rename(filename,newfile);
	rename("temp.bin",filename);
	if(mem)
		free(mem);
SAVE_END:
	return ret;	
}
*/

int makeispbinandcfg(char *destbin,char *resbin,u32_t buff,u32_t size)
{
    FILE *dest=NULL,*res=NULL,*target=NULL;
	int addr,i,ret,ressize,binsize;
	char *mem;

    ret = 0;
	fopen_s(&dest,destbin,"rb");	
	fopen_s(&target,"temp.bin","wb");
	fopen_s(&res,resbin,"rb");
	if(dest==NULL || target==NULL || res == NULL)
	{
        printf("makeispbincfg open file fail\n");
		return -1;		
	}
	mem = (char *)malloc(4096);
	if(mem == NULL)
	{
		printf("makeispbinandcfg :malloc fail\n");
		return -1;
	}
    fseek(res,0,SEEK_END);
	ressize = ftell(res);
	fseek(res,0,SEEK_SET);
	if(ressize&0x1ff)
	{
		ressize = (ressize&(~0x1ff))+0x200;
	}
	//printf("ressize = %x\n",ressize);
	fread(mem,4096,1,dest);
//---------write exe bin
    i = mem[9];
	i<<=4;
	binsize = *((int *)&mem[i+8]);
	*((int *)&mem[i+8+4]) = ressize>>9;
	binsize<<=9;
	binsize-=4096;
	fwrite(mem,4096,1,target);
	while(binsize>0)
	{				
		if(binsize <= 4096)
			memset(mem,0,4096);
		fread(mem,4096,1,dest);
		binsize-=4096;
		if(binsize<0)
		{
			binsize+=4096;
			if(binsize&0x1ff)
		        binsize = (binsize&(~0x1ff))+0x200;
            fwrite(mem,binsize,1,target);
			break;
		}
		fwrite(mem,4096,1,target);
	}
//----------write res
	while(ressize>0)
	{
		if(ressize <= 4096)
			memset(mem,0,4096);
		fread(mem,4096,1,res);
		ressize-=4096;
		if(ressize<0)
		{
            ressize+=4096;
			if(ressize&0x1ff)
		        ressize = (ressize&(~0x1ff))+0x200;
            fwrite(mem,ressize,1,target);
			break;
		}
		fwrite(mem,4096,1,target);		
	}
    memset(mem,0,4096);
	ressize = ftell(target);
	if(ressize&0xfff)
	{
		ressize = 0x1000-(ressize&0xfff);
		fwrite(mem,ressize,1,target);

	}
//---------write config
	memset(mem,0,4096);
	memcpy(mem,buff,size);
	fwrite(mem,4096,1,target);

	fclose(res);
	fclose(target);
	fclose(dest);
    free(mem);

	
	rename(destbin,"Destbin_backup.bin");
	rename("temp.bin",destbin);
    _unlink("Destbin_backup.bin");

	return 0;

}


/*
static s32_t user_config_read(u32_t buff,u32_t len)
{
    FILE *file;

	fopen_s(&file,userConfigName,"r");
	if(file == NULL)
		return -1;
	fread((void*)buff,256,1,file);
	fclose(file);
	return len;
}

static s32_t user_config_write(u32_t buff,u32_t len)
{
     FILE *file;
     
	 fopen_s(&file,userConfigName,"w");
	 if(file==NULL)
	 	return -1;
	 fwrite((void*)buff,len,1,file);
	 fclose(file);
	 return len;
}
s32_t user_config_init(char *filename)
{	
	strcpy(userConfigName,filename);
    return -1;
}
*/
/*******************************************************************************
* Function Name  : userCofnigCrcCal
* Description    : count crc value
* Input          : none
* Output         : None
* Return         : u32_t : crc value
*******************************************************************************/
static u32_t userCofnigCrcCal(void)
{
    u32_t chkSum,i;
    
    chkSum = 0;
    for(i=0;i<sizeof(userConfigTable.flag)/sizeof(userConfigTable.flag[0]);i++)
        chkSum +=userConfigTable.flag[i];
    return chkSum;
}

/*******************************************************************************
* Function Name  : userConfigLoad
* Description    : load user configure
* Input          : none
* Output         : None
* Return         : s32_t 
*                    0 : success
*******************************************************************************/
s32_t userConfigLoad(char *filename)
{
	return destin_bin_cfg_load(filename,(u32_t)&userConfigTable,sizeof(userConfigTable));
}
/*******************************************************************************
* Function Name  : userConfigSave
* Description    : save user configure value to spi flash
* Input          : none
* Output         : None
* Return         : s32_t 
*                    0 : always
*******************************************************************************/
s32_t userConfigSave(char *filename,char *newname)
{
    userConfigTable.CheckSum = userCofnigCrcCal();
	makeispbinandcfg(filename,newname,(u32_t)&userConfigTable,sizeof(userConfigTable));
//	user_config_write((u32_t)&userConfigTable,sizeof(userConfigTable));
    return 0;
}


/*******************************************************************************
* Function Name  : userConfigSetValue
* Description    : set configure value
* Input          : u8_t configId : configure id
*                  u32_t value   : configure value
* Output         : None
* Return         : none
*******************************************************************************/
void userConfigSetValue(int configId,u32_t value)
{
	if((configId<sizeof(userConfigTable.flag)/sizeof(userConfigTable.flag[0]))&&(configId>=0))
    userConfigTable.flag[configId] = value;
}


