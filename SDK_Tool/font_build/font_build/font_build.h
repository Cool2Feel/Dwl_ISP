// font_build.h : PROJECT_NAME 应用程序的主头文件
//

#pragma once

#ifndef __AFXWIN_H__
	#error "在包含此文件之前包含“stdafx.h”以生成 PCH 文件"
#endif

#include "resource.h"		// 主符号


// Cfont_buildApp:
// 有关此类的实现，请参阅 font_build.cpp
//

class Cfont_buildApp : public CWinApp
{
public:
	Cfont_buildApp();

// 重写
	public:
	virtual BOOL InitInstance();

// 实现

	DECLARE_MESSAGE_MAP()
};

extern Cfont_buildApp theApp;