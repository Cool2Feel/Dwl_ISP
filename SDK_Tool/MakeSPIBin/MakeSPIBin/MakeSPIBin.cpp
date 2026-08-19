// MakeSPIBin.cpp : 定义控制台应用程序的入口点。
//

#include "stdafx.h"
#include "MakeSPIBin.h"
#include "stdlib.h"

#ifdef _DEBUG
#define new DEBUG_NEW
#endif


// 唯一的应用程序对象

CWinApp theApp;

using namespace std;

#define BOOTSECT_SIZE	512

CString	  T_current_path;					//程序运行的目录
CString	  T_select_file_path;				//选择的文件全路径

/*
CFile fileResBin,fileCodeBin,fileAX208Code,fileDestBin;
//const TCHAR *pszBootsectFileName =  _T("Bootsect.bin");

const TCHAR *pszResFileName = _T("Res.bin");

const TCHAR *pszCodeFileName = _T("Tiga.bin");

const TCHAR *pszAX208CodeFileName = _T("app208.cod");
*/

int  arg_num;
CFile fileDestBin , file[10];
TCHAR *psFileName[10];


 
static void T_get_cur_path(char *bufptr)			//get exe run path
{
	char buffer[MAX_PATH+1];
	memset(buffer,0,MAX_PATH+1);
	::GetModuleFileName(NULL,buffer,MAX_PATH);
//	printf("get path= %s\n",buffer);
	for(int i=MAX_PATH-1;i>=0;i--)
	{
		if(*(buffer+i)!='\\')
		{
			*(buffer+i)=0;
		}
		else
		{
			*(buffer+i)=0;
			break;
		}
	}
	//strcpy(bufptr,buffer);
//	printf("handle path= %s\n",buffer);
	memcpy(bufptr,buffer,strlen(buffer));
}


DWORD XCHDWORD(DWORD dwXCHData)
{
	DWORD dwXCHDataTmp;
	((BYTE *)(&dwXCHDataTmp))[0] = ((BYTE *)(&dwXCHData))[3];
	((BYTE *)(&dwXCHDataTmp))[1] = ((BYTE *)(&dwXCHData))[2];
	((BYTE *)(&dwXCHDataTmp))[2] = ((BYTE *)(&dwXCHData))[1];
	((BYTE *)(&dwXCHDataTmp))[3] = ((BYTE *)(&dwXCHData))[0];
	return dwXCHDataTmp;
}
WORD XCHWORD(WORD wXCHData)
{
	WORD wXCHDataTmp;
	((BYTE *)(&wXCHDataTmp))[0] = ((BYTE *)(&wXCHData))[1];
	((BYTE *)(&wXCHDataTmp))[1] = ((BYTE *)(&wXCHData))[0];
	return wXCHDataTmp;
}

DWORD AlignWithBlockPage(DWORD dwData, BOOL bBPMode)
{
	BYTE byCnt = 8;		// PageSize = 256 = (1 << 8);
	if (bBPMode)
	{
		byCnt = 12;		// BlockSize = 4K = (1 << 12);
	} 

	if (dwData & ((1 << byCnt) - 1))
	{
		dwData = ((dwData >> byCnt) + 1) << byCnt;
	}
	return dwData;
}
DWORD AlignWithSecPage(DWORD dwData)
{
	BYTE byCnt = 9;		// SectorSize = 512 = (1 << 9);

	if (dwData & ((1 << byCnt) - 1))
	{
		dwData = ((dwData >> byCnt) + 1) << byCnt;
	}
	return dwData;
}

WORD CalCRC(BYTE *l_Data, int l_Length)
{
	BYTE i;
	unsigned int crc=0xffff;

	while(l_Length--!=0)
	{
		for(i=0x01; i!=0; i*=2) 
		{
			if((crc&0x8000)!=0) 
			{
				crc*=2;
				crc^=0x1021;
			}
			else
				crc*=2;

			if((*l_Data&i)!=0) 
				crc^=0x1021; 
		}
		l_Data++;
	}

	WORD crc_result = 0;
	WORD crc_temp = (WORD)crc;
	int j;
	for(j = 15; j >= 0; j--)
	{
		if(((crc_temp >> j) & 1) == 1)
		{
			crc_result |= (1 << (15 - j));
		}
	}
	return (WORD)crc_result;
}

BOOL  MakeBin(void)
{

	TCHAR temp[1024];
	ZeroMemory(temp,sizeof(temp));
	T_get_cur_path(temp);
	T_current_path.Format(_T("%s"),temp);
	

	CString strTmp;
	ULONGLONG fileSize = 0;
	CString strTemp = _T("");
	BYTE  *pUnencryptData;
	DWORD dwUnencryptDataLen=0;
	BYTE  ax208_exit_flag = 0;

	UINT openFlags = CFile::modeCreate|CFile::typeBinary|CFile::modeReadWrite;
	strTmp.Format("%s\\DestBin.bin",T_current_path);
	
	if ( !fileDestBin.Open(strTmp, openFlags))	//建立DestBin.bin文件
	{
		strTmp.Format(_T("Couldn't open/create DestBin.bin"));
		printf(strTemp);
		//system("pause");
//		printf("Couldn't open/create DestBin.bin");
		return FALSE;
	}

	//get connect file size
	for(int i = 1;i < arg_num; i++)
	{
		T_select_file_path.Format(_T("%s\\%s"),T_current_path,psFileName[i]);
		if(!file[i].Open(T_select_file_path, CFile::modeNoTruncate | CFile::modeRead |  CFile::shareDenyNone))
		{
			fileDestBin.Close();
			for( int j = 1; j <= i;j++)
			{
				file[j].Close();
			}
			strTemp.Format(_T("%s"),psFileName[i]);
			strTemp += _T(" file open fail\n");
			printf(strTemp);
			//system("pause");
	//		printf("code file length error\n");
			return FALSE;
		}

		fileSize = file[i].GetLength();
		dwUnencryptDataLen += (DWORD)fileSize;
		if(i != (arg_num-1))	// last file align with blockpage,not secpage
		{
			dwUnencryptDataLen = AlignWithSecPage(dwUnencryptDataLen);
		}
	}

	dwUnencryptDataLen = AlignWithBlockPage(dwUnencryptDataLen,TRUE);
	dwUnencryptDataLen += 0x1000;// 为菜单参数预留4k 空间

	pUnencryptData = new BYTE[(DWORD)dwUnencryptDataLen];	//分配文件合并的空间
	if(NULL != pUnencryptData)
	{
		DWORD dwResPos = 0;//last file must be res.bin 
		ZeroMemory(pUnencryptData,dwUnencryptDataLen);
		dwUnencryptDataLen = 0;

		for(int i = 1;i < arg_num; i++)	// 读文件数据
		{
			fileSize = file[i].GetLength();
			file[i].Read(pUnencryptData+dwUnencryptDataLen,(DWORD)fileSize);
			dwResPos = dwUnencryptDataLen;
			dwUnencryptDataLen += (DWORD)fileSize;
			if(i != (arg_num-1))	// last file align with blockpage,not secpage
			{
				dwUnencryptDataLen = AlignWithSecPage(dwUnencryptDataLen);
			}
		}


		dwUnencryptDataLen = AlignWithBlockPage(dwUnencryptDataLen,TRUE);
		dwUnencryptDataLen += 0x1000;// 为菜单参数预留4k 空间

			//modify bootsect
		if(*((BYTE *)(pUnencryptData  + 9)))
		{
			int iTableAdr = *((BYTE *)(pUnencryptData  + 9))* 0x10;
			*((DWORD *)(pUnencryptData  + iTableAdr + 0x08)) = AlignWithSecPage(dwResPos)>>9 ;//扇区序号		//last file must be res.bin 
			*((DWORD *)(pUnencryptData  + iTableAdr + 0x0c)) = AlignWithSecPage((DWORD)fileSize)>>9;  //扇区数
		}

		fileDestBin.Write(pUnencryptData,dwUnencryptDataLen);
	}


	fileDestBin.Close();

	//system("pause");
	delete []pUnencryptData;

	return TRUE;
}

int _tmain(int argc, TCHAR* argv[], TCHAR* envp[])
{
	int nRetCode = 0;

	// 初始化 MFC 并在失败时显示错误
	if (!AfxWinInit(::GetModuleHandle(NULL), NULL, ::GetCommandLine(), 0))
	{
		// TODO: 更改错误代码以符合您的需要
		_tprintf(_T("错误: MFC 初始化失败\n"));
		nRetCode = 1;
	}
	else
	{
		Sleep(1000);								//wait 1s for fix the make destbin.bin fail problem!
		// TODO: 在此处为应用程序的行为编写代码。

		CString strTmp;
		ZeroMemory(psFileName,sizeof(psFileName));

		if((argc <= 1) || (argc >= 10))
		{
			printf("arg err!");
		}
		else
		{
			arg_num = argc;
			for(int i = 0;i < argc;i++)
			{
				psFileName[i] = (TCHAR*)argv[i];
			/*
				if(NULL != psFileName[i])
				{
					strTmp.Format(_T("%s\n"),psFileName[i]);
					printf(strTmp);
				}
			*/
			}


			if (MakeBin())
				printf("Make destbin.bin success.");
			else
				printf("Make destbin.bin fail.\n");
		}
	}

	return nRetCode;
}
