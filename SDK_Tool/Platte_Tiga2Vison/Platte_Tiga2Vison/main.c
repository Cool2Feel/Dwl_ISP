#include <stdio.h>
#include <string.h>
#include <io.h>
#include <stdlib.h>

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

//	s = atoi(string);

	d = ((s&0xf8000000)>>11)| ((s&0x000000f8)<<8)|  ((s&0x0000fc00)>>5)| ((s&0x00f80000)>>19);

	return d;
}

int main(int argc,char *argv[])
{
	FILE *in,*out;
	char string[16];
	unsigned int pixel;


	if(argc>=2)
		fopen_s(&in,argv[1],"r");
	else
		fopen_s(&in,"palette.txt","r");
	if(in == NULL)
	{
		printf("open platette.txt fail\n");
	//	sleep(1);
		return -1;
	}

	if(argc>=3)
	{
		unlink(argv[2]);
		fopen_s(&out,argv[2],"wb");
	}
	else
	{
		unlink("palette.bin");
		fopen_s(&out,"palette.bin","wb");
	}

	if(out == NULL)
	{
		fclose(in);
		printf("open plaette.bin fail \n");
	//	sleep(1);
		return -2;
	}

    while(1)
	{
		 string[0] = 0;
		 fread(string,1,1,in);
		 if(string[0] == '{')
			 break;
		 else if(string[0] == 0)
			 break;
	}
	if(string[0] == 0)
	{
		printf("can not find palette table\n");
		fclose(in);
		fclose(out);
		return -3;
	}

    while(1)
	{
		string[0] = 0;
		fread(string,1,1,in);
		if(string[0] == '}' || string[0] == 0)
			break;
		else if(string[0] == 0x0d || string[0] == 0x0a)
			continue;
		fread(&string[1],10,1,in);
        pixel  = string2pixel(string);

		fwrite(&pixel,4,1,out);
	}

	fclose(in);
	fclose(out);

	printf("palette chage sucess!\n");
//	sleep(1);
    return 0;
} 