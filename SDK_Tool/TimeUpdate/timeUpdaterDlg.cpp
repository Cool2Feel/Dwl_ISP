// timeUpdaterDlg.cpp : implementation file
//

#include "stdafx.h"
#include "timeUpdater.h"
#include "timeUpdaterDlg.h"
#include "spti.h"
#ifdef _DEBUG
#define new DEBUG_NEW
#undef THIS_FILE
static char THIS_FILE[] = __FILE__;
#endif

/////////////////////////////////////////////////////////////////////////////
// CAboutDlg dialog used for App About

class CAboutDlg : public CDialog
{
public:
	CAboutDlg();

// Dialog Data
	//{{AFX_DATA(CAboutDlg)
	enum { IDD = IDD_ABOUTBOX };
	//}}AFX_DATA

	// ClassWizard generated virtual function overrides
	//{{AFX_VIRTUAL(CAboutDlg)
	protected:
	virtual void DoDataExchange(CDataExchange* pDX);    // DDX/DDV support
	//}}AFX_VIRTUAL

// Implementation
protected:
	//{{AFX_MSG(CAboutDlg)
	//}}AFX_MSG
	DECLARE_MESSAGE_MAP()
};

CAboutDlg::CAboutDlg() : CDialog(CAboutDlg::IDD)
{
	//{{AFX_DATA_INIT(CAboutDlg)
	//}}AFX_DATA_INIT
}

void CAboutDlg::DoDataExchange(CDataExchange* pDX)
{
	CDialog::DoDataExchange(pDX);
	//{{AFX_DATA_MAP(CAboutDlg)
	//}}AFX_DATA_MAP
}

BEGIN_MESSAGE_MAP(CAboutDlg, CDialog)
	//{{AFX_MSG_MAP(CAboutDlg)
		// No message handlers
	//}}AFX_MSG_MAP
END_MESSAGE_MAP()

/////////////////////////////////////////////////////////////////////////////
// CTimeUpdaterDlg dialog

CTimeUpdaterDlg::CTimeUpdaterDlg(CWnd* pParent /*=NULL*/)
	: CDialog(CTimeUpdaterDlg::IDD, pParent)
{
	//{{AFX_DATA_INIT(CTimeUpdaterDlg)
	//}}AFX_DATA_INIT
	// Note that LoadIcon does not require a subsequent DestroyIcon in Win32
	m_hIcon = AfxGetApp()->LoadIcon(IDR_MAINFRAME);
}

void CTimeUpdaterDlg::DoDataExchange(CDataExchange* pDX)
{
	CDialog::DoDataExchange(pDX);
	//{{AFX_DATA_MAP(CTimeUpdaterDlg)
	DDX_Control(pDX, IDC_STATICSTATUS, m_status);
	DDX_Control(pDX, IDC_STATICTime, m_staticTime);
	//}}AFX_DATA_MAP
}

BEGIN_MESSAGE_MAP(CTimeUpdaterDlg, CDialog)
	//{{AFX_MSG_MAP(CTimeUpdaterDlg)
	ON_WM_SYSCOMMAND()
	ON_WM_PAINT()
	ON_WM_QUERYDRAGICON()
	ON_WM_TIMER()
	//}}AFX_MSG_MAP
	ON_BN_CLICKED(IDOK, &CTimeUpdaterDlg::OnBnClickedOk)
END_MESSAGE_MAP()

/////////////////////////////////////////////////////////////////////////////
// CTimeUpdaterDlg message handlers

BOOL CTimeUpdaterDlg::OnInitDialog()
{
	CDialog::OnInitDialog();

	// Add "About..." menu item to system menu.

	// IDM_ABOUTBOX must be in the system command range.
	ASSERT((IDM_ABOUTBOX & 0xFFF0) == IDM_ABOUTBOX);
	ASSERT(IDM_ABOUTBOX < 0xF000);

	CMenu* pSysMenu = GetSystemMenu(FALSE);
	if (pSysMenu != NULL)
	{
		CString strAboutMenu;
		strAboutMenu.LoadString(IDS_ABOUTBOX);
		if (!strAboutMenu.IsEmpty())
		{
			pSysMenu->AppendMenu(MF_SEPARATOR);
			pSysMenu->AppendMenu(MF_STRING, IDM_ABOUTBOX, strAboutMenu);
		}
	}

	// Set the icon for this dialog.  The framework does this automatically
	//  when the application's main window is not a dialog
	SetIcon(m_hIcon, TRUE);			// Set big icon
	SetIcon(m_hIcon, FALSE);		// Set small icon
	
	// TODO: Add extra initialization here
	DisplayTime();
	m_timer=SetTimer(1,1000,0);
	UpdateDeviceTime();
	return TRUE;  // return TRUE  unless you set the focus to a control
}

void CTimeUpdaterDlg::OnSysCommand(UINT nID, LPARAM lParam)
{
	if ((nID & 0xFFF0) == IDM_ABOUTBOX)
	{
		CAboutDlg dlgAbout;
		dlgAbout.DoModal();
	}
	else
	{
		CDialog::OnSysCommand(nID, lParam);
	}
}

// If you add a minimize button to your dialog, you will need the code below
//  to draw the icon.  For MFC applications using the document/view model,
//  this is automatically done for you by the framework.

void CTimeUpdaterDlg::OnPaint() 
{
	if (IsIconic())
	{
		CPaintDC dc(this); // device context for painting

		SendMessage(WM_ICONERASEBKGND, (WPARAM) dc.GetSafeHdc(), 0);

		// Center icon in client rectangle
		int cxIcon = GetSystemMetrics(SM_CXICON);
		int cyIcon = GetSystemMetrics(SM_CYICON);
		CRect rect;
		GetClientRect(&rect);
		int x = (rect.Width() - cxIcon + 1) / 2;
		int y = (rect.Height() - cyIcon + 1) / 2;

		// Draw the icon
		dc.DrawIcon(x, y, m_hIcon);
	}
	else
	{
		CDialog::OnPaint();
	}
}

// The system calls this to obtain the cursor to display while the user drags
//  the minimized window.
HCURSOR CTimeUpdaterDlg::OnQueryDragIcon()
{
	return (HCURSOR) m_hIcon;
}

void CTimeUpdaterDlg::OnTimer(UINT nIDEvent) 
{
	// TODO: Add your message handler code here and/or call default
	if(nIDEvent==m_timer)
	{
		DisplayTime();
	}
	CDialog::OnTimer(nIDEvent);
}

LRESULT CTimeUpdaterDlg::WindowProc(UINT message, WPARAM wParam, LPARAM lParam) 
{
	// TODO: Add your specialized code here and/or call the base class
	if (message == WM_DEVICECHANGE)
	{
		UpdateDeviceTime();
		return true;
	}
	return CDialog::WindowProc(message, wParam, lParam);
}

void CTimeUpdaterDlg::DisplayTime()
{
	//COleDateTime nowTime = COleDateTime::GetCurrentTime();
	SYSTEMTIME sysTime;
	GetLocalTime(&sysTime);
	CString strTime;
	strTime.Format("%04i-%02i-%02i %02i:%02i:%02i",
				   sysTime.wYear,
				   sysTime.wMonth,
				   sysTime.wDay,
				   sysTime.wHour,
				   sysTime.wMinute,
				   sysTime.wSecond);
	m_staticTime.SetWindowText(strTime);
}

HANDLE CTimeUpdaterDlg::OpenTheDrv(int phDrv)
{
	HANDLE theHandle=INVALID_HANDLE_VALUE;
	CString csDeviceName;
	CString csPhysical;
	
	csDeviceName.Format("\\\\.\\PHYSICALDRIVE%i",phDrv);
	theHandle = ::CreateFile(csDeviceName, GENERIC_READ|GENERIC_WRITE, FILE_SHARE_READ|FILE_SHARE_WRITE, NULL, OPEN_EXISTING,
						FILE_ATTRIBUTE_NORMAL, NULL);
	if ((theHandle == INVALID_HANDLE_VALUE)||(theHandle == NULL))
	{
		return INVALID_HANDLE_VALUE;
	}
	PSTORAGE_DEVICE_DESCRIPTOR pDevDesc;
	pDevDesc = (PSTORAGE_DEVICE_DESCRIPTOR)new BYTE[sizeof(STORAGE_DEVICE_DESCRIPTOR) + 512 - 1];
	pDevDesc->Size = sizeof(STORAGE_DEVICE_DESCRIPTOR) + 512 - 1;
	
	if (GetDisksProperty(theHandle, pDevDesc))
	{
		if (pDevDesc->BusType == BusTypeUsb)
		{
			CString str;
			char *p= (char*)pDevDesc;
			str=p+pDevDesc->VendorIdOffset;
			

			//MessageBox(str);

			if ((str=="Buildwin") ||(str=="AX3231MP")||(str=="Generic"))
				str+=&p[pDevDesc->VendorIdOffset+9];
			str.MakeLower();
			if(str.IsEmpty())
			{
				::CloseHandle(theHandle);
				delete pDevDesc;
				return NULL;
			}
			if((str.Find(_T("buildwin minidv")) != -1) ||(str.Find(_T("ax3231mptool")) != -1)||(str.Find(_T("buildwinmedia-player")) != -1)||(str.Find(_T("generic")) != -1))
			{
				delete pDevDesc;
				pDevDesc=NULL;
				//return theHandle;
			}
			else
			{
				::CloseHandle(theHandle);
				delete pDevDesc;
				return NULL;
			}
			//str=(pDevDesc->ProductIdOffset ? &p[pDevDesc->ProductIdOffset]:"(NULL)");					
		}
		else
		{
			::CloseHandle(theHandle);
			delete pDevDesc;
			return NULL;
		}
	}
	else
	{
		::CloseHandle(theHandle);
		delete pDevDesc;
		return NULL;
	}
	if(pDevDesc==NULL)
		delete pDevDesc;
	
	return theHandle;
}
BOOL CTimeUpdaterDlg::GetDisksProperty(HANDLE hDevice, PSTORAGE_DEVICE_DESCRIPTOR pDevDesc)
{
	STORAGE_PROPERTY_QUERY	Query;	
	DWORD dwOutBytes;				
	BOOL bResult;					

	Query.PropertyId = StorageDeviceProperty;
	Query.QueryType = PropertyStandardQuery;

	bResult = ::DeviceIoControl(hDevice,			
			IOCTL_STORAGE_QUERY_PROPERTY,			
			&Query, sizeof(STORAGE_PROPERTY_QUERY),	
			pDevDesc, pDevDesc->Size,				
			&dwOutBytes,							
			(LPOVERLAPPED)NULL);					
//	if(!bResult)
//		TRACE("GetDisksPropertyResult:%i\n",GetLastError());
	return bResult;
}

BOOL CTimeUpdaterDlg::ReadFromScsi(HANDLE devHandle,
								int    cdbLen,
								void  *cdb,
								int    dataLen,
								BYTE  *data//char  *data
								)	//发命令从Scsi读出
{
	SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER sptdwb;
	BOOL	status;
	ULONG	length = 0,
			returned = 0;
	BYTE *tmp = (BYTE*)cdb;
	tmp[0]=0xcb;
    sptdwb.sptd.Length = sizeof(SCSI_PASS_THROUGH_DIRECT);
    sptdwb.sptd.PathId = 0;
    sptdwb.sptd.TargetId = 1;
    sptdwb.sptd.Lun = 0;
    sptdwb.sptd.CdbLength = cdbLen;		//CDB命令的长度
    sptdwb.sptd.SenseInfoLength = 26;	//24;
    sptdwb.sptd.DataIn = SCSI_IOCTL_DATA_IN;	//读数据
    sptdwb.sptd.DataTransferLength = dataLen;//sectorSize;	//读取数据的长度
    sptdwb.sptd.TimeOutValue = 200;		//响应超时时间
    sptdwb.sptd.DataBuffer = data;		//读取的数据的存放指针
    sptdwb.sptd.SenseInfoOffset =
       offsetof(SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER, ucSenseBuf);
	memcpy(sptdwb.sptd.Cdb, cdb, cdbLen);
    length = sizeof(SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER);
    status = DeviceIoControl(devHandle,
                             IOCTL_SCSI_PASS_THROUGH_DIRECT,
                             &sptdwb,
                             length,
                             &sptdwb,
                             length,
                             &returned,
                             FALSE);
	if(status==0)
	{
		TRACE("ReadError:%i\n",GetLastError());
	}
	return status;
}
#define YEAR 2000
typedef struct
{
	 int year;
	 int month;
	 int day;
	 int hour;
	 int min;
	 int sec;
} date;

/*储存12个月的天数*/ 
const int days[12]={31,28,31,30,31,30,31,31,30,31,30,31}; 

/*判断是否为闰年*/
int isLeapYear(int year)  
{ 
	if(((year%4==0)&&(year%100!=0))||(year%400==0)) 
	{
		return 1; 
	}
		return 0; 
}
DWORD CTimeUpdaterDlg::dateTime2Sec()
{
	long sum=0; 
	int i;
	date d;
	
	SYSTEMTIME nowTime;
	GetLocalTime(&nowTime);

	d.year  =	nowTime.wYear;
	d.month =	nowTime.wMonth;
    d.day   =	nowTime.wDay;
	d.hour  =	nowTime.wHour;
	d.min   =	nowTime.wMinute;
	d.sec   =	nowTime.wSecond;
	//累计以往各年的天数
	for(i=YEAR;i<d.year;i++) 
	{ 
		sum+=365; 
		if(isLeapYear(i)) 
		{//闰年多一天
			sum+=1; 
		}
	}
	//累计当年以往各月的天数
	for(i=0;i<d.month-1;i++)
	{
		sum+=days[i];
	}
	if(d.month>2)
	{
		if(isLeapYear(d.year)) 
		{//闰年多一天
			sum+=1; 
		}
	}
	//累计当年当月的天数
	sum+=d.day-1; 
	//转换成秒
	sum=sum*24*60*60; 

	//加当天的小时，分钟，秒
	sum+=d.hour*60*60+d.min*60+d.sec;
	//返回总秒数
	return sum; 
}

void CTimeUpdaterDlg::UpdateDeviceTime()
{
	for(int i=1;i<127;i++)
	{
		HANDLE tmp = OpenTheDrv(i);
		if ((INVALID_HANDLE_VALUE != tmp) && ((NULL != tmp)))
		{
			m_status.SetWindowText("device online...");
			DWORD secCount = dateTime2Sec();
			memset(cdb16,0,16);
			cdb16[0]=0xcb;
			cdb16[1]=0xf0;
			cdb16[4]=(BYTE)(secCount>>24);
			cdb16[5]=(BYTE)(secCount>>16);
			cdb16[6]=(BYTE)(secCount>>8);
			cdb16[7]=(BYTE)(secCount>>0);
			BOOL ret = ReadFromScsi(tmp,16,cdb16,0,NULL);
			if(ret)
				m_status.SetWindowText("Update device's time successful.");
			else
				m_status.SetWindowText("Update device's time fail.");
			::CloseHandle(tmp);
			return;
		}
	}
	m_status.SetWindowText("No device online...");
}


void CTimeUpdaterDlg::OnBnClickedOk()
{
	// TODO: 在此添加控件通知处理程序代码
	OnOK();
}
