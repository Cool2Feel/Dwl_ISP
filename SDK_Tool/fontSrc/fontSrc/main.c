
#include <stdio.h>
#include <stdlib.h>
#include <io.h>
#include <string.h>

#ifndef _WIN32_WINNT            // Specifies that the minimum required platform is Windows Vista.
#define _WIN32_WINNT 0x0600     // Change this to the appropriate value to target other versions of Windows.
#endif

#define WIN32_LEAN_AND_MEAN             // Exclude rarely-used stuff from Windows headers




int main(int argc,char *argv[])
{
    char filename[128],temp8,*mem;
	FILE *cfg,*tar,*src;
	int ret,idx,size;

	if(argc>2)
		fopen_s(&cfg,argv[1],"r");
	else
		fopen_s(&cfg,"font.ini","r");

	if(cfg==NULL)
	{
		printf("open cfg file fail.\n");
		goto ERR_END;
	}

	while(1)
	{
		filename[0] = 0;
		fscanf_s(cfg,"%s",&filename,128);
		if(filename[0] == 0)
			break;
        if(strcmp(filename,"-f")==0)
			break;
	}

	if(argc>=3)
	{
		_unlink(argv[2]);
		fopen_s(&tar,argv[2],"w");
	}
	else
	{
		_unlink("fontSrc.txt");
		fopen_s(&tar,"fontSrc.txt","w");
	}
	filename[0] = 0xef;
	filename[1] = 0xbb;
	filename[2] = 0xbf;
    fwrite(filename,3,1,tar);

    if(tar==NULL)
	{
		printf("creat font srcouce fail.\n");
		fclose(cfg);
		goto ERR_END;
	}
    mem = (char *)malloc(4096);
    while(1)
	{
        filename[0] = 0;
		fscanf_s(cfg,"%s",&filename,128);
		if(filename[0] == 0)
			break;
		if(filename[0] == '#')
			continue;

        fopen_s(&src,filename,"r");
        
		while(1)
		{
            fread(&temp8,1,1,src);
			if(temp8 == '"')
				break;
		}
		fseek(src,-1,SEEK_CUR);

//		fread(&temp8,1,1,src);
//	    if(temp8<0)
//		   fseek(src,3,SEEK_SET);
//	    else
//		   fseek(src,0,SEEK_SET);

        idx = ftell(src);
        while(1)
		{
           fread(mem,4096,1,src);
		   size = ftell(src)-idx;
		   idx = ftell(src);
		   if(size==0)
			   break;
		   fwrite(mem,size,1,tar);
		} 
		fclose(src);
	}
	fclose(cfg);
	fclose(tar);
	return 0;
ERR_END:
//    Sleep(1);
	return -1;
}