// stdafx.h : include file for standard system include files,
//  or project specific include files that are used frequently, but
//      are changed infrequently
//

#if !defined(AFX_STDAFX_H__B2902944_32C9_48DE_B441_D216DA1009CD__INCLUDED_)
#define AFX_STDAFX_H__B2902944_32C9_48DE_B441_D216DA1009CD__INCLUDED_
//#define WINVER 0x0500
#define WINVER 0x0602
#define _WIN32_WINNT 0x0602
#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000

#define VC_EXTRALEAN		// Exclude rarely-used stuff from Windows headers

#include <afxwin.h>         // MFC core and standard components
#include <afxext.h>         // MFC extensions
#include <afxdisp.h>        // MFC Automation classes
#include <afxdtctl.h>		// MFC support for Internet Explorer 4 Common Controls
#ifndef _AFX_NO_AFXCMN_SUPPORT
#include <afxcmn.h>			// MFC support for Windows Common Controls
#endif // _AFX_NO_AFXCMN_SUPPORT

#include <dbt.h>
#ifndef ULONG_PTR
#define ULONG_PTR unsigned long*
#endif
#ifndef DWORD_PTR
#define DWORD_PTR DWORD*
#endif
#include "devioctl.h"
#include "ntdddisk.h"
#include "ntddscsi.h"
//{{AFX_INSERT_LOCATION}}
// Microsoft Visual C++ will insert additional declarations immediately before the previous line.

#endif // !defined(AFX_STDAFX_H__B2902944_32C9_48DE_B441_D216DA1009CD__INCLUDED_)
