/****************************************************************************
**
 **                              CONFIGURE
  ** *   **             THE APPOTECH MULTIMEDIA PROCESSOR
   **** **                      CONFIGURE
  *** ***
 **  * **               (C) COPYRIGHT 2016 BUILDWIN 
**      **                         
         **         BuildWin SZ LTD.CO  ; VIDEO PROJECT TEAM
          **   
* File Name   : config.h
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
#ifndef  CONFIG_H
#define  CONFIG_H


#define TDEBUG	1 // 0: not debug , 1: debug

typedef unsigned char u8_t;
typedef char          s8_t;
typedef unsigned short u16_t;
typedef short         s16_t;
typedef unsigned int  u32_t;
typedef int           s32_t;


typedef u8_t CONFIG_ITEM_T;

#define  R_ID_TYPE_STR         0x81000000		// come from SDK
#define  CONFIG_MAX            512

typedef struct CONFIG_SYS_S
{
    u8_t powerkeyflag;
    u8_t gsensorflag;
    u8_t parklineflag;
    u8_t mdflag;
}CONFIG_SYS_T;

typedef struct CONFIG_USER_S
{
	u32_t flag[127];
	u32_t CheckSum;
}SYSTEM_FLAY;


typedef struct CONFIG_INDEX_S
{
    char item_id[64];
	u32_t item_value;
}CONFIG_INDEX_T;



int user_configInit(char *filename);
int user_configFindId(char *str);
/*******************************************************************************
* Function Name  : userConfigLoad
* Description    : load user configure
* Input          : none
* Output         : None
* Return         : s32_t 
*                    0 : success
*                    -1: fail
*******************************************************************************/
extern s32_t userConfigLoad(char *filename);
/*******************************************************************************
* Function Name  : userConfigSave
* Description    : save user configure value to spi flash
* Input          : none
* Output         : None
* Return         : s32_t 
*                    0 : always
*******************************************************************************/
extern s32_t userConfigSave(char *filename,char *newname);

/*******************************************************************************
* Function Name  : userConfigInitial
* Description    : initial user configure value
* Input          : none
* Output         : None
* Return         : s32_t 
*                      0 : always
*******************************************************************************/
extern s32_t userConfigInitial(void);
/*******************************************************************************
* Function Name  : userConfigSetValue
* Description    : set configure value
* Input          : u8_t configId : configure id
*                  u32_t value   : configure value
* Output         : None
* Return         : none
*******************************************************************************/
extern void userConfigSetValue(u8_t configId,u32_t value);


//======api====
extern int my_str_cmp(const void* src, const void* dst, int cnt);

#endif