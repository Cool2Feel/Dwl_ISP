// MakeSpycamBin.cpp : Defines the entry point for the console application.
//

#include "stdafx.h"
#include "MakeSpycamBin.h"
#include "stdlib.h"
#ifdef _DEBUG
#define new DEBUG_NEW
#undef THIS_FILE
static char THIS_FILE[] = __FILE__;
#endif

/////////////////////////////////////////////////////////////////////////////
// The one and only application object

#define MAX_FILE_NUM	2550

#ifdef _DEBUG
#define WORK_DIR		"res"
#else
#define WORK_DIR		"file"
#endif
#define MAX_DATATAB_LEN	(4 * 1024 * 1024)

CWinApp theApp;

using namespace std;

CString resHeadText = "/* !!! Do not manually modify this file in the production stage !!!\r\n * This file is automatically created by MakeResBin.exe,\r\n * and used in the production stage.*/\r\n\r\n#ifndef __RES_H_\r\n#define __RES_H_\r\n\r\n";
CString resTailText = "\r\n#endif\r\n";

CString	  T_current_path;					//程序运行的目录
CString	  T_select_file_path;				//选择的文件全路径
CString	  T_select_file_title;				//选择的文件名称,无扩展名
CString	  g_work_dir;

CFile fileRes, fileBin, fileHead;

DWORD XCHDWORD(DWORD dwXCHData);
WORD XCHWORD(WORD wXCHData);
WORD CalCRC(BYTE *l_Data, int l_Length);
DWORD AlignWithBlockPage(DWORD dwData, BOOL bBPMode);
DWORD AlignWithSecPage(DWORD dwData);
BOOL ResolveTextFile(BYTE *pDataTab, DWORD &dwUnencryptDataLen, DWORD &fileCnt);
DWORD MakeBin(void);

static void T_get_cur_path(char *bufptr)			//get exe run path
{
	char buffer[MAX_PATH+1];
	ZeroMemory(buffer,sizeof(buffer));
	::GetModuleFileName(NULL,buffer,MAX_PATH);
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
	memcpy(bufptr,buffer,strlen(buffer));
}

static void T_get_cur_name(char *bufptr)			//get exe name
{
	char buffer[MAX_PATH+1];
	int i;
	ZeroMemory(buffer,sizeof(buffer));
	::GetModuleFileName(NULL,buffer,MAX_PATH);
	for(i=MAX_PATH-1;i>=0;i--)
	{
		if(*(buffer+i)=='\\')
		{
			break;
		}
	}
	//strcpy(bufptr,buffer);
	memcpy(bufptr,buffer+i+1,MAX_PATH-i);
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
		byCnt = 16;		// BlockSize = 64K = (1 << 16);
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

DWORD inline judgeValidChar(char cTabTemp,ifstream &fin)
{
	if ('/' == cTabTemp)
	{
		cTabTemp = fin.get();
		if('/'==cTabTemp) {
			while ('\n' != fin.get())
			{
				if (fin.eof())
				{
					return 3;
				}
				if (fin.fail())
				{
					//AfxMessageBox(_T("file\\DataTab.txt file data format error"));
					return 1;
				}
			}
			return 0;
		}
		else if('*'==cTabTemp) {
			while ('*' != fin.get() || '/' != fin.get())
			{
				if (fin.eof())
				{
					return 3;
				}
				if (fin.fail())
				{
					//AfxMessageBox(_T("file\\DataTab.txt file data format error"));
					return 1;
				}
			}
			return 0;
		}
		else {
			return 1;
		}
	}
	else 
		return 0;
}

BOOL ResolveTextFile(BYTE *pDataTab, DWORD &dwUnencryptDataLen, DWORD &fileCnt)
{
	//resolve DataTab.txt file
	TCHAR temp[256];
	DWORD dwDataTabCnt;
	DWORD dwDataTabItemLenCnt;	
	char  cTabTemp;
	DWORD dwTabTemp;
	char cflag;		//u8,u16,u32标记
	CString strTmp;
	DWORD judgeResult;
	DWORD search_cnt=0;
	
	dwDataTabCnt = dwUnencryptDataLen;
	setlocale(LC_ALL,"Chinese-simplified");//设置中文环境
	//locale::global(locale(""));//将全局区域设为操作系统默认区域
	ifstream fin(T_select_file_path);			//注意不能支持中文路径！！！！！！！！！！！！！
	//locale::global(locale("C"));//还原全局区域设定
	if (!fin.is_open())
	{
		return FALSE;
	}
SEARCH_START:
	search_cnt++;
	dwDataTabItemLenCnt = 0;
	do{
		cTabTemp=fin.get();
		judgeResult = judgeValidChar(cTabTemp,fin);
		if(judgeResult != 0)
			goto SEARCH_FINISH; 
		if (fin.fail() || fin.eof())
		{
			//AfxMessageBox(_T("file\\DataTab.txt file data format error"));
			if(search_cnt <=1)
			{ 
				fin.close(); 
				return FALSE; 
			}
			goto SEARCH_FINISH;
		}
	}while (('u' != cTabTemp)&&('U'!= cTabTemp));
	fin >> dwTabTemp;
	//printf("1:u%d\n",dwTabTemp);
	switch(dwTabTemp)
	{
	case 8  : cflag = 0; break;
	case 16 : cflag = 1; break;
	case 32 : cflag = 2; break;
	default : { fin.close(); return FALSE; }

	}
	do 	//去掉空格或注释等符号
	{
		cTabTemp=fin.get();
		judgeResult = judgeValidChar(cTabTemp,fin);
		if(judgeResult != 0)
		{
			fin.close();
			return FALSE;
		}
		if (fin.fail() || fin.eof())
		{
			//AfxMessageBox(_T("file\\DataTab.txt file data format error"));
			fin.close();
			return FALSE;
		}
	}while(' ' == cTabTemp || '\n' == cTabTemp || '\t' == cTabTemp || '\r' == cTabTemp);
	DWORD i=0;
	ZeroMemory(temp,sizeof(temp));
	while((cTabTemp>='a'&&cTabTemp<='z')||(cTabTemp>='A'&&cTabTemp<='Z')||(cTabTemp>='0'&&cTabTemp<='9')||(cTabTemp=='_')){
		temp[i++]=cTabTemp;
		cTabTemp=fin.get();
	}
	if(i==0) {
		fin.close();
		return FALSE;
	}
	while(i--) {	//将小写字母转换成大写字母
		if(temp[i]>='a'&&temp[i]<='z')
			temp[i]=temp[i]-('a'-'A');
	}


	
	do{
		cTabTemp = fin.get();
		judgeResult = judgeValidChar(cTabTemp,fin);
		if(judgeResult != 0)
		{
			fin.close();
			return FALSE;
		}
		if (fin.fail() || fin.eof())
		{
			//AfxMessageBox(_T("file\\DataTab.txt file data format error"));
			fin.close();
			return FALSE;
		}
		if(cTabTemp == '}' || cTabTemp == ',') {
			fin.close();
			return FALSE;
		}
	}while ('{' != cTabTemp);
	fin >> dec;
	while(TRUE) {
		if (fin >> dwTabTemp)
		{
			if ((0==dwTabTemp) && (cTabTemp=fin.get(),('x'==cTabTemp)||('X'==cTabTemp)))
			{
				//fin >> hex;
				if (fin.setf(ios_base::hex, ios_base::basefield) & ios_base::hex)
				{
					//The previous format flags has always been set as hex format
					//AfxMessageBox(_T("file\\DataTab.txt file data format error"));
					fin.close();
					return FALSE;
				}			
			} 
			else
			{
				//save table data
				if( 0 == cflag )
				{
					pDataTab[dwDataTabCnt++] = dwTabTemp & 0xff;
					dwDataTabItemLenCnt++;
				}
				if( 1 == cflag )
				{
					pDataTab[dwDataTabCnt++] = (dwTabTemp >> 8) & 0xff;
					dwDataTabItemLenCnt++;
					pDataTab[dwDataTabCnt++] = dwTabTemp & 0xff;
					dwDataTabItemLenCnt++;
				}
				if( 2 == cflag )
				{
					pDataTab[dwDataTabCnt++] = (dwTabTemp >> 24) & 0xff;
					dwDataTabItemLenCnt++;
					pDataTab[dwDataTabCnt++] = (dwTabTemp >> 16) & 0xff;
					dwDataTabItemLenCnt++;
					pDataTab[dwDataTabCnt++] = (dwTabTemp >> 8) & 0xff;
					dwDataTabItemLenCnt++;
					pDataTab[dwDataTabCnt++] = dwTabTemp & 0xff;
					dwDataTabItemLenCnt++;
				}
				if (dwDataTabItemLenCnt > MAX_DATATAB_LEN)
				{
					//AfxMessageBox(_T("file\\DataTab.txt file data format error"));
					fin.close();
					return FALSE;
				}
			}			
		}
		else
		{
			if (fin.eof())
			{
				break;
			}
			else if (fin.fail())
			{
				fin.clear();
				fin >> dec;
				cTabTemp = fin.get();
				if ('}' == cTabTemp)
				{
					//成功找到一个数组，写头文件
					CString strName;
					strName.Format(_T("%s_%s"), T_select_file_title.MakeUpper(),temp);
					strTmp.Format(_T("#define         RES_%-20s\t%i\r\n"), strName, fileCnt);
					fileHead.Write((LPCTSTR)strTmp, strTmp.GetLength());	

					*((DWORD *)(pDataTab+fileCnt*8)) = dwUnencryptDataLen;
					*((DWORD *)(pDataTab+fileCnt*8+4)) = (DWORD)dwDataTabItemLenCnt;
					dwUnencryptDataLen=(DWORD)dwDataTabCnt;
					fileCnt++;
					goto SEARCH_START;
				}
				else if ('/' == cTabTemp)
				{
					cTabTemp = fin.get();
					if('/'==cTabTemp) {
						while ('\n' != fin.get())
						{
							if (fin.eof())
							{
								goto SEARCH_FINISH;
							}
							if (fin.fail())
							{
								//AfxMessageBox(_T("file\\DataTab.txt file data format error"));
								fin.close();
								return FALSE;
							}
						}
						continue;
					}
					else if('*'==cTabTemp) {
						while ('*' != fin.get() || '/' != fin.get())
						{
							if (fin.eof())
							{
								goto SEARCH_FINISH;
							}
							if (fin.fail())
							{
								//AfxMessageBox(_T("file\\DataTab.txt file data format error"));
								fin.close();
								return FALSE;
							}
						}
						continue;
					}
					else {
						fin.close();
						return FALSE;
					}
				}
			} 
			else
			{
				//AfxMessageBox(_T("file\\DataTab.txt file data format error"));
				fin.close();
				return FALSE;
			}
		}
	}
SEARCH_FINISH:
	fin.close();
	return TRUE;
}

DWORD  MakeBin(void)
{
	TCHAR temp[1024];
	CString exeFileName;
	ZeroMemory(temp,sizeof(temp));
	T_get_cur_path(temp);	//获取当前运行目录
	T_current_path.Format(_T("%s"),temp);
	T_get_cur_name(temp);	//获取当前运行程序名称
	exeFileName.Format(_T("%s"),temp);
	
	BYTE  *pUnencryptData;
	DWORD dwUnencryptDataLen=0;

	CFileFind finder;
	CString strFileTitle,strFileName,strTmp;
	
	DWORD fileCnt =  0;	//file number
	ULONGLONG fileSize = 0;	//file size
	strTmp.Format("%s\\%s\\*.*",T_current_path,g_work_dir);
	BOOL bWorking = finder.FindFile(strTmp);	
	while (bWorking)
	{
		bWorking = finder.FindNextFile();
		strFileName = finder.GetFileName();
		if(finder.IsDots() || finder.IsDirectory())	//忽略目录路径
			continue;
		if(strFileName.MakeLower() == "res.h" || strFileName.MakeLower() == "res.bin" || strFileName.MakeLower() == exeFileName.MakeLower())
			continue;
		fileCnt++;
		fileSize += finder.GetLength();
		if(fileCnt > MAX_FILE_NUM) {
			//strTmp.Format(_T("The maximum number of file is limit to %d"), MAX_FILE_NUM);
			//AfxMessageBox(strTmp);
			printf("The maximum number of files is limit to %d\n",MAX_FILE_NUM);
			return 1;
		}
	}
	if(fileCnt == 0) {
		printf("Can't find files,please confirm the directory is right.\n");
		return 2;
	}
	pUnencryptData = new BYTE[(DWORD)fileSize+8*fileCnt];	//分配文件合并的空间

	CFileException ex;	
	UINT openFlags = CFile::modeCreate|CFile::modeWrite;
	strTmp.Format("%s\\RES.H",T_current_path);
	if ( !fileHead.Open(strTmp, openFlags, &ex))	//建立RES.H文件
	{
		delete []pUnencryptData;
		TCHAR szError[1024];
		ex.GetErrorMessage(szError, 1024);
		//strTmp.Format(_T("Couldn't open/create res.h, Error: %s"), szError);
		//AfxMessageBox(strTmp) ;
		printf("Couldn't open/create res.h\n");
		return 2;
	}
	
	//写头文件
	fileHead.Write((LPCTSTR)resHeadText, resHeadText.GetLength());	

	openFlags = CFile::modeCreate|CFile::typeBinary|CFile::modeReadWrite;
	strTmp.Format("%s\\RES.BIN",T_current_path);
	if ( !fileBin.Open(strTmp, openFlags, &ex))	//建立RES.BIN文件
	{
		delete []pUnencryptData;
		TCHAR szError[1024];
		ex.GetErrorMessage(szError, 1024);
		//strTmp.Format(_T("Couldn't open/create res.bin, Error: %s"), szError);
		//AfxMessageBox(strTmp) ;
		printf("Couldn't open/create res.bin\n");
		strTmp.Format("%s\\%s\\RES.H",T_current_path,g_work_dir);
		fileHead.Close();
		fileHead.Remove(strTmp);
		return 2;
	}
	
	dwUnencryptDataLen = fileCnt*8;
	fileCnt = 0;
	fileSize = 0;
	strTmp.Format("%s\\%s\\*.*",T_current_path,g_work_dir);
	bWorking = finder.FindFile(strTmp);	
	while(bWorking) {
		bWorking = finder.FindNextFile();
		if(finder.IsDots() || finder.IsDirectory())	//忽略目录路径
			continue;
		strFileName = finder.GetFileName();
		strFileTitle = finder.GetFileTitle();
		if(strFileName.MakeLower() == "res.h" || strFileName.MakeLower() == "res.bin" || strFileName.MakeLower() == exeFileName.MakeLower())
			continue;
		if(strFileName.Right(4).MakeLower() == ".txt" || strFileName.Right(2).MakeLower() == ".c")	//需要将数组结构转换成二进制文件
		{

			T_select_file_path.Format("%s\\%s\\%s",T_current_path,g_work_dir,strFileName);
			T_select_file_title.Format("%s",strFileTitle);
			if(FALSE==ResolveTextFile(pUnencryptData,dwUnencryptDataLen,fileCnt)) {
				printf("Couldn't deal with resource file:%s\n",strFileName);
				fileBin.Close();
				fileHead.Close();
				strTmp.Format("%s\\%s\\RES.BIN",T_current_path,g_work_dir);
				fileBin.Remove(strTmp);
				strTmp.Format("%s\\%s\\RES.H",T_current_path,g_work_dir);
				fileHead.Remove(strTmp);
				delete []pUnencryptData;
				return 2;
			}
		}
		else {	//直接复制二进制文件
			strTmp.Format(_T("#define         RES_%-20s\t%i\r\n"), strFileTitle.MakeUpper(), fileCnt);
			fileHead.Write((LPCTSTR)strTmp, strTmp.GetLength());	//写头文件

			fileSize = finder.GetLength();
			*((DWORD *)(pUnencryptData+fileCnt*8)) = dwUnencryptDataLen;
			*((DWORD *)(pUnencryptData+fileCnt*8+4)) = (DWORD)fileSize;
			T_select_file_path.Format("%s\\%s\\%s",T_current_path,g_work_dir,strFileName);
			T_select_file_title.Format("%s",strFileTitle);
			if ( !fileRes.Open(T_select_file_path, CFile::modeRead|CFile::typeBinary, &ex))
			{
				TCHAR szError[1024];
				ex.GetErrorMessage(szError, 1024);
				//strTmp.Format(_T("Couldn't open resource file:%s ! ERROR: %s"), strFileName,szError);
				//AfxMessageBox(strTmp) ;
				printf("Couldn't open resource file:%s\n",strFileName);
				fileHead.Close();
				fileBin.Close();
				strTmp.Format("%s\\%s\\RES.BIN",T_current_path,g_work_dir);
				fileBin.Remove(strTmp);
				strTmp.Format("%s\\%s\\RES.H",T_current_path,g_work_dir);
				fileHead.Remove(strTmp);
				delete []pUnencryptData;
				return 2;
			}
			fileRes.Read(pUnencryptData+dwUnencryptDataLen,(DWORD)fileSize);
			dwUnencryptDataLen+=(DWORD)fileSize;
			fileRes.Close();
			fileCnt++;
		}
	}
	fileBin.Write(pUnencryptData,dwUnencryptDataLen);
	fileHead.Write((LPCTSTR)resTailText, resTailText.GetLength());	
	fileBin.Close();
	fileHead.Close();
	delete []pUnencryptData;
	
	
	return 0;
}


int _tmain(int argc, TCHAR* argv[], TCHAR* envp[])
{
	int nRetCode = 0;

	// initialize MFC and print and error on failure
	if (!AfxWinInit(::GetModuleHandle(NULL), NULL, ::GetCommandLine(), 0))
	{
		// TODO: change error code to suit your needs
		cerr << _T("Fatal Error: MFC initialization failed") << endl;
		nRetCode = 1;
	}
	
	if(argc < 3) {
		printf("too few arguments for the program.\n");
	}

	if(argv[1][0] != '-' || (argv[1][1] != 'd' && argv[1][1] != 'D')) {
		printf("arguments is error.\n");
	}

	g_work_dir.Format("%s",argv[2]);

	if (MakeBin() == 0)
		printf("Make res.bin and res.h success.\nsource files is from %s\n",g_work_dir);
	else
		printf("Make res.bin and res.h fail.\n");
	return nRetCode;
}


