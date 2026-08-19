/****************************************************************************
       ***             ***                      MAXLIB-GRAPHC                  
      ** **           ** **                                                    
     **   **         **   **            THE MAXLIB FOR IMAGE SHOW PROCESS      
    **     **       **     **                                                  
   **       **     **       **              MAX ROURCE ICON MANAGEMENT         
  **         **   **         **                                                
 **           ** **           **              (C) COPYRIGHT 2016 MAX           
**             ***             **                                              
                                                                               
* File Name   : user_str.c                                                     
* Author      : Mark.Douglas                                                   
* Version     : V0100                                                          
* Date        : 05/25/2016                                                     
* Description : This file for maxlib resource str managemant                   
*                                                                              
* History     :                                                                
* 2016-05-25  :                                                                
*      <1>.This is created by mark,set version as v0103.                       
*      <2>.Add basic functions.                                                
******************************************************************************/

#include "../application.h"
#include "user_str.h"

R_STRING_T User_String_Table[R_STR_MAX&0xffff] = 
{
   {R_ID_STR_LAN_ENGLISH            ,  (void *)0,0,0,0x00000000},
   {R_ID_STR_LAN_SCHINESE           ,  (void *)0,0,0,0x00000001},
   {R_ID_STR_LAN_TCHINESE           ,  (void *)0,0,0,0x00000002},
   {R_ID_STR_LAN_JAPANESE           ,  (void *)0,0,0,0x00000003},
   {R_ID_STR_LAN_GERMAN             ,  (void *)0,0,0,0x00000004},
   {R_ID_STR_LAN_FRECH              ,  (void *)0,0,0,0x00000005},
   {R_ID_STR_LAN_RUSSIAN            ,  (void *)0,0,0,0x00000006},
   {R_ID_STR_LAN_ITALIAN            ,  (void *)0,0,0,0x00000007},
   {R_ID_STR_LAN_KOERA              ,  (void *)0,0,0,0x00000008},
   {R_ID_STR_LAN_TAI                ,  (void *)0,0,0,0x00000009},
   {R_ID_STR_LAN_HEBREW             ,  (void *)0,0,0,0x0000000a},
   {R_ID_STR_LAN_DUTCH              ,  (void *)0,0,0,0x0000000b},
   {R_ID_STR_LAN_UKRAINIAN          ,  (void *)0,0,0,0x0000000c},
   {R_ID_STR_LAN_SPANISH            ,  (void *)0,0,0,0x0000000d},
   {R_ID_STR_LAN_PORTUGUESE         ,  (void *)0,0,0,0x0000000e},
   {R_ID_STR_LAN_POLISH             ,  (void *)0,0,0,0x0000000f},
   {R_ID_STR_LAN_CZECH              ,  (void *)0,0,0,0x00000010},
   {R_ID_STR_LAN_TURKEY             ,  (void *)0,0,0,0x00000011},
   {R_ID_STR_UPDATE_START           ,  (void *)0,0,0,0x00000012},
   {R_ID_STR_UPDATE_END             ,  (void *)0,0,0,0x00000013},
   {R_ID_STR_COM_OFF                ,  (void *)0,0,0,0x00000014},
   {R_ID_STR_COM_ON                 ,  (void *)0,0,0,0x00000015},
   {R_ID_STR_COM_OK                 ,  (void *)0,0,0,0x00000016},
   {R_ID_STR_COM_CANCEL             ,  (void *)0,0,0,0x00000017},
   {R_ID_STR_COM_YES                ,  (void *)0,0,0,0x00000018},
   {R_ID_STR_COM_NO                 ,  (void *)0,0,0,0x00000019},
   {R_ID_STR_COM_LOW                ,  (void *)0,0,0,0x0000001a},
   {R_ID_STR_COM_MIDDLE             ,  (void *)0,0,0,0x0000001b},
   {R_ID_STR_COM_HIGH               ,  (void *)0,0,0,0x0000001c},
   {R_ID_STR_COM_WAITING            ,  (void *)0,0,0,0x0000001d},
   {R_ID_STR_COM_50HZ               ,  (void *)0,0,0,0x0000001e},
   {R_ID_STR_COM_60HZ               ,  (void *)0,0,0,0x0000001f},
   {R_ID_STR_COM_P2_0               ,  (void *)0,0,0,0x00000020},
   {R_ID_STR_COM_P1_0               ,  (void *)0,0,0,0x00000021},
   {R_ID_STR_COM_P0_0               ,  (void *)0,0,0,0x00000022},
   {R_ID_STR_COM_N1_0               ,  (void *)0,0,0,0x00000023},
   {R_ID_STR_COM_N2_0               ,  (void *)0,0,0,0x00000024},
   {R_ID_STR_COM_ALWAYSON           ,  (void *)0,0,0,0x00000025},
   {R_ID_STR_TIM_1MIN               ,  (void *)0,0,0,0x00000026},
   {R_ID_STR_TIM_3MIN               ,  (void *)0,0,0,0x00000027},
   {R_ID_STR_TIM_5MIN               ,  (void *)0,0,0,0x00000028},
   {R_ID_STR_TIM_10MIN              ,  (void *)0,0,0,0x00000029},
   {R_ID_STR_TIM_2SEC               ,  (void *)0,0,0,0x0000002a},
   {R_ID_STR_TIM_5SEC               ,  (void *)0,0,0,0x0000002b},
   {R_ID_STR_SET_DATETIME           ,  (void *)0,0,0,0x0000002c},
   {R_ID_STR_SET_AUTOOFF            ,  (void *)0,0,0,0x0000002d},
   {R_ID_STR_SET_LANGUAGE           ,  (void *)0,0,0,0x0000002e},
   {R_ID_STR_SET_SCREENOFF          ,  (void *)0,0,0,0x0000002f},
   {R_ID_STR_SET_VIDEOROTATE        ,  (void *)0,0,0,0x00000030},
   {R_ID_STR_SET_RESET              ,  (void *)0,0,0,0x00000031},
   {R_ID_STR_SET_FORMAT             ,  (void *)0,0,0,0x00000032},
   {R_ID_STR_SET_VERSION            ,  (void *)0,0,0,0x00000033},
   {R_ID_STR_SET_RESOLUTION         ,  (void *)0,0,0,0x00000034},
   {R_ID_STR_SET_LOOPRECORD         ,  (void *)0,0,0,0x00000035},
   {R_ID_STR_SET_MOTIONDET          ,  (void *)0,0,0,0x00000036},
   {R_ID_STR_SET_AUDIOREC           ,  (void *)0,0,0,0x00000037},
   {R_ID_STR_SET_TIMESTRAMP         ,  (void *)0,0,0,0x00000038},
   {R_ID_STR_SET_FILLIGHT           ,  (void *)0,0,0,0x00000039},
   {R_ID_STR_SET_GSENSOR            ,  (void *)0,0,0,0x0000003a},
   {R_ID_STR_SET_PARKMODE           ,  (void *)0,0,0,0x0000003b},
   {R_ID_STR_SET_FASTVIEW           ,  (void *)0,0,0,0x0000003c},
   {R_ID_STR_SET_DELETECUR          ,  (void *)0,0,0,0x0000003d},
   {R_ID_STR_SET_DELETEALL          ,  (void *)0,0,0,0x0000003e},
   {R_ID_STR_SET_LOCKCUR            ,  (void *)0,0,0,0x0000003f},
   {R_ID_STR_SET_LOCKALL            ,  (void *)0,0,0,0x00000040},
   {R_ID_STR_SET_UNLOCKCUR          ,  (void *)0,0,0,0x00000041},
   {R_ID_STR_SET_UNLOCKALL          ,  (void *)0,0,0,0x00000042},
   {R_ID_STR_SET_LOCK               ,  (void *)0,0,0,0x00000043},
   {R_ID_STR_SET_DELETE             ,  (void *)0,0,0,0x00000044},
   {R_ID_STR_SET_PLAYMODE           ,  (void *)0,0,0,0x00000045},
   {R_ID_STR_SET_REPEATALL          ,  (void *)0,0,0,0x00000046},
   {R_ID_STR_SET_REPEATRAD          ,  (void *)0,0,0,0x00000047},
   {R_ID_STR_SET_REPEATSIG          ,  (void *)0,0,0,0x00000048},
   {R_ID_STR_SET_AUDIOPLAY          ,  (void *)0,0,0,0x00000049},
   {R_ID_STR_SET_VOLUME             ,  (void *)0,0,0,0x0000004a},
   {R_ID_STR_SET_THUMBNAIL          ,  (void *)0,0,0,0x0000004b},
   {R_ID_STR_SET_SETTING            ,  (void *)0,0,0,0x0000004c},
   {R_ID_STR_SET_VIDEO              ,  (void *)0,0,0,0x0000004d},
   {R_ID_STR_SET_PHOTO              ,  (void *)0,0,0,0x0000004e},
   {R_ID_STR_SET_PLAY               ,  (void *)0,0,0,0x0000004f},
   {R_ID_STR_SET_MUSIC              ,  (void *)0,0,0,0x00000050},
   {R_ID_STR_SET_AUDIOMODE          ,  (void *)0,0,0,0x00000051},
   {R_ID_STR_SET_BEEPSOUND          ,  (void *)0,0,0,0x00000052},
   {R_ID_STR_SET_FREQUENCY          ,  (void *)0,0,0,0x00000053},
   {R_ID_STR_RES_QVGA               ,  (void *)0,0,0,0x00000054},
   {R_ID_STR_RES_VGA                ,  (void *)0,0,0,0x00000055},
   {R_ID_STR_RES_480P               ,  (void *)0,0,0,0x00000056},
   {R_ID_STR_RES_720P               ,  (void *)0,0,0,0x00000057},
   {R_ID_STR_RES_1024P              ,  (void *)0,0,0,0x00000058},
   {R_ID_STR_RES_1080P              ,  (void *)0,0,0,0x00000059},
   {R_ID_STR_RES_1440P              ,  (void *)0,0,0,0x0000005a},
   {R_ID_STR_RES_3024P              ,  (void *)0,0,0,0x0000005b},
   {R_ID_STR_RES_12M                ,  (void *)0,0,0,0x0000005c},
   {R_ID_STR_RES_10M                ,  (void *)0,0,0,0x0000005d},
   {R_ID_STR_RES_8M                 ,  (void *)0,0,0,0x0000005e},
   {R_ID_STR_RES_5M                 ,  (void *)0,0,0,0x0000005f},
   {R_ID_STR_RES_3M                 ,  (void *)0,0,0,0x00000060},
   {R_ID_STR_SDC_NULL               ,  (void *)0,0,0,0x00000061},
   {R_ID_STR_SDC_FULL               ,  (void *)0,0,0,0x00000062},
   {R_ID_STR_SDC_ERROR              ,  (void *)0,0,0,0x00000063},
   {R_ID_STR_FIL_NULL               ,  (void *)0,0,0,0x00000064},
   {R_ID_STR_FIL_LOCKED             ,  (void *)0,0,0,0x00000065},
   {R_ID_STR_FMT_ING                ,  (void *)0,0,0,0x00000066},
   {R_ID_STR_FMT_SUCCESS            ,  (void *)0,0,0,0x00000067},
   {R_ID_STR_FMT_FAIL               ,  (void *)0,0,0,0x00000068},
   {R_ID_STR_PWR_LOW                ,  (void *)0,0,0,0x00000069},
   {R_ID_STR_ISP_WHITEBL            ,  (void *)0,0,0,0x0000006a},
   {R_ID_STR_ISP_ISO                ,  (void *)0,0,0,0x0000006b},
   {R_ID_STR_ISP_ANTISHANK          ,  (void *)0,0,0,0x0000006c},
   {R_ID_STR_ISP_SOFT               ,  (void *)0,0,0,0x0000006d},
   {R_ID_STR_ISP_CLOUDY             ,  (void *)0,0,0,0x0000006e},
   {R_ID_STR_ISP_TUNGSTEN           ,  (void *)0,0,0,0x0000006f},
   {R_ID_STR_ISP_FLUORESCENT        ,  (void *)0,0,0,0x00000070},
   {R_ID_STR_ISP_BLACKWHITE         ,  (void *)0,0,0,0x00000071},
   {R_ID_STR_ISP_SEPIA              ,  (void *)0,0,0,0x00000072},
   {R_ID_STR_ISP_ISO100             ,  (void *)0,0,0,0x00000073},
   {R_ID_STR_ISP_ISO200             ,  (void *)0,0,0,0x00000074},
   {R_ID_STR_ISP_ISO400             ,  (void *)0,0,0,0x00000075},
   {R_ID_STR_ISP_WDR                ,  (void *)0,0,0,0x00000076},
   {R_ID_STR_ISP_EXPOSURE           ,  (void *)0,0,0,0x00000077},
};

