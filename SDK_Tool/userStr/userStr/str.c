#include <stdio.h>
#include <string.h>

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
    sprintf(str2,"%s",str);
    return 0;
}

int stridcheck(char *str)
{

	if(str[0] =='R' && str[1] == '_' && str[2] == 'I' && str[3] == 'D' && str[4] == '_')
		return 1;
	return 0;
}




int stringChange(char *str1,char *str2)
{
    char *src;

	src = str1;
	if(*src == 0)
		return -1;
	else if(*src!='O')
		return 0;
	while(*src!=0)
	{
		if(*src == 'N')
		{
			src++;
			break;
		}
		src++;
	}

	sprintf(str2,"%s",src);
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
char *string2hdefine2(char *str)
{
	static char string[32],*tar;

	tar = string;
	while(*str)
	{
		if(*str == '.')
			break;
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