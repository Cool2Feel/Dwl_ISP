#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <io.h>
#include <time.h>
#include "config.h"
#include "menu.h"


/*******************************************************************************
* Function Name  : dma_memcpy
* Description    : copy data from src to dst
* Input          : *dst: destination pointer
*                  *src: source pointer
*                  cnt :length
* Output         : None
* Return         : None
*******************************************************************************/
int my_str_cmp(const void* src, const void* dst, int cnt)
{
    const char *s = (const char *)src;
	const char *d = (const char *)dst;
	int pos=0;
	int cmp_cnt = cnt;
    while(1)
    {
    	if((*d) == (*(s+pos)))
    	{
			cmp_cnt--;
			if(0 == cmp_cnt)
			{
				return pos+1;
			}
			d++;
    	}
		else
		{
			cmp_cnt = cnt;
			d = (const char *)dst;
		}

		if((0 == *d) ||(0 == *(s+pos)))
		{
			return 0;
		}
		pos++;
    }
    return -1;
}


int main(int argc,char *argv[])
{
    int ret,i;
//	MENU_INFO_T *menu;

#if (1 == TDEBUG)
   for(i=1;i<argc;i++)
   {
	   printf("file[%d]->%s\n",i,argv[i]);
   }
#endif

    ret = user_stringInit(argv[1]);//("user_str.h");

//    user_config_init(argv[2]);//("config.bin");//

	user_configInit(argv[3]);//("config.c");//

	userConfigLoad(argv[5]);//("DestBin.bin");//   

	user_setting_init(argv[4]);//("setting.txt");//

	userConfigSave(argv[5],argv[6]);//("DestBin.bin","res.bin");//

    return 0;

}




















