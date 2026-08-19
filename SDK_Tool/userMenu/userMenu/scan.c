#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <io.h>
#include "config.h"
#include "menu.h"

static char Cache[4096];
static int sindex,scount;

int scanRead(FILE *file)
{
	int point;

    point = ftell(file);
	fread(Cache,4096,1,file);

    scount = ftell(file)-point;

	return scount;
}

int scanInit(FILE *file)
{

	sindex = 0;
	scount = 0;

	return scanRead(file);
}

char scanGet(FILE *file)
{
	if(scount)
      return Cache[sindex++];
	else
	{
        if(scanInit(file))
		{
			sindex = 0;
            return Cache[sindex++];
		}
		else
			return 0;
	}
}
int diffter(char c)
{
	if((c>='0' && c<='9') ||(c>='a'&&c<='z')||(c>='A'&&c<='Z')||c=='_')
		return 1;
	else
		return 0;
}

int scanString(char *str,FILE *file)
{
    char c;
    int i,f;

	i=0;
	f=0;

	while(1)
	{
		c = scanGet(file);
		if(c==0)
			return -1;
		if(f==0)
		{
			if(diffter(c))
				f = 1;
		}
		else if(f==1)
		{
			if(diffter(c)==0)
			{
				str[i++] = 0;
				return i;
			}
            
		}

        if(f)
			str[i++] = c;
	}

	return 0;

}

