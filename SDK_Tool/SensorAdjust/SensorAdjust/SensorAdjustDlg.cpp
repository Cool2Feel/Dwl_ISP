// SensorAdjustDlg.cpp : 实现文件
//

#include "stdafx.h"
#include "SensorAdjust.h"
#include "SensorAdjustDlg.h"
#include "spti.h"

#ifdef _DEBUG
#define new DEBUG_NEW
#endif

//=======ticru add ======
CString str_addr;
CString str_value;
CString str_first;
CString str_mid;

CString str_debug;			//for test

// 用于应用程序“关于”菜单项的 CAboutDlg 对话框

class CAboutDlg : public CDialog
{
public:
	CAboutDlg();

// 对话框数据
	enum { IDD = IDD_ABOUTBOX };

	protected:
	virtual void DoDataExchange(CDataExchange* pDX);    // DDX/DDV 支持

// 实现
protected:
	DECLARE_MESSAGE_MAP()
};

CAboutDlg::CAboutDlg() : CDialog(CAboutDlg::IDD)
{
}

void CAboutDlg::DoDataExchange(CDataExchange* pDX)
{
	CDialog::DoDataExchange(pDX);
}

BEGIN_MESSAGE_MAP(CAboutDlg, CDialog)
END_MESSAGE_MAP()


// CSensorAdjustDlg 对话框




CSensorAdjustDlg::CSensorAdjustDlg(CWnd* pParent /*=NULL*/)
	: CDialog(CSensorAdjustDlg::IDD, pParent)
{
	m_hIcon = AfxGetApp()->LoadIcon(IDR_MAINFRAME);
}

void CSensorAdjustDlg::DoDataExchange(CDataExchange* pDX)
{
	CDialog::DoDataExchange(pDX);

	DDX_Control(pDX, IDC_EDIT3, m_connectStatus);
	DDX_Text(pDX, IDC_EDIT1, str_addr);
	DDX_Text(pDX, IDC_EDIT2, str_value);

	DDX_Control(pDX, IDC_CHECK1, addr_16bit);
	DDX_Control(pDX, IDC_CHECK2, value_16bit);
	DDX_Control(pDX, IDC_LIST1, listbox);
}

BEGIN_MESSAGE_MAP(CSensorAdjustDlg, CDialog)
	ON_WM_SYSCOMMAND()
	ON_WM_PAINT()
	ON_WM_QUERYDRAGICON()
	//}}AFX_MSG_MAP
	ON_BN_CLICKED(IDC_BUTTON1, &CSensorAdjustDlg::OnBnClickedButton1)
	ON_EN_CHANGE(IDC_EDIT1, &CSensorAdjustDlg::OnEnChangeAddr)
	ON_EN_CHANGE(IDC_EDIT2, &CSensorAdjustDlg::OnEnChangeValue)
	ON_BN_CLICKED(IDC_BUTTON2, &CSensorAdjustDlg::OnBnClickedButton2)
	ON_BN_CLICKED(IDC_BUTTON3, &CSensorAdjustDlg::OnBnClickedButton3)
	ON_BN_CLICKED(IDC_CHECK1, &CSensorAdjustDlg::OnBnClicked_addr)
	ON_BN_CLICKED(IDC_CHECK2, &CSensorAdjustDlg::OnBnClicked_value)
	ON_LBN_SELCHANGE(IDC_LIST1, &CSensorAdjustDlg::OnLbnSelchange)
	ON_BN_CLICKED(IDC_BUTTON4, &CSensorAdjustDlg::OnBnClickedButton4)
	ON_BN_CLICKED(IDC_BUTTON5, &CSensorAdjustDlg::OnBnClicked_Save)
END_MESSAGE_MAP()


// CSensorAdjustDlg 消息处理程序

BOOL CSensorAdjustDlg::OnInitDialog()
{
	CDialog::OnInitDialog();

	// 将“关于...”菜单项添加到系统菜单中。

	// IDM_ABOUTBOX 必须在系统命令范围内。
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

	// 设置此对话框的图标。当应用程序主窗口不是对话框时，框架将自动
	//  执行此操作
	SetIcon(m_hIcon, TRUE);			// 设置大图标
	SetIcon(m_hIcon, FALSE);		// 设置小图标

	// TODO: 在此添加额外的初始化代码
	m_hDevHandle	=	NULL;
	m_connectStatus.SetWindowText(_T("Device disconnected"));
	addr_16bit.SetCheck(0);
	value_16bit.SetCheck(0);
	str_first.Format(_T("addr: 0x"));
	str_mid.Format(_T("         value: 0x"));
	CString data;
	data.Format(_T("00"));
	str_addr.Format(_T("00"));
	str_value.Format(_T("00"));

	CString temp;
	temp+=str_first;
	temp+=data;
	temp+=str_mid;
	temp+=data;
	listbox.AddString(temp);
	listbox.SetSel(0,1);

	UpdateData(false);

	return TRUE;  // 除非将焦点设置到控件，否则返回 TRUE
}

void CSensorAdjustDlg::OnSysCommand(UINT nID, LPARAM lParam)
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

// 如果向对话框添加最小化按钮，则需要下面的代码
//  来绘制该图标。对于使用文档/视图模型的 MFC 应用程序，
//  这将由框架自动完成。

void CSensorAdjustDlg::OnPaint()
{
	if (IsIconic())
	{
		CPaintDC dc(this); // 用于绘制的设备上下文

		SendMessage(WM_ICONERASEBKGND, reinterpret_cast<WPARAM>(dc.GetSafeHdc()), 0);

		// 使图标在工作区矩形中居中
		int cxIcon = GetSystemMetrics(SM_CXICON);
		int cyIcon = GetSystemMetrics(SM_CYICON);
		CRect rect;
		GetClientRect(&rect);
		int x = (rect.Width() - cxIcon + 1) / 2;
		int y = (rect.Height() - cyIcon + 1) / 2;

		// 绘制图标
		dc.DrawIcon(x, y, m_hIcon);
	}
	else
	{
		CDialog::OnPaint();
	}
}

//当用户拖动最小化窗口时系统调用此函数取得光标
//显示。
HCURSOR CSensorAdjustDlg::OnQueryDragIcon()
{
	return static_cast<HCURSOR>(m_hIcon);
}


//========device=========
BOOL CSensorAdjustDlg::GetDisksProperty(HANDLE hDevice, PSTORAGE_DEVICE_DESCRIPTOR pDevDesc)
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
	if(!bResult)
		TRACE("GetDisksPropertyResult:%i\n",GetLastError());
	return bResult;
}


HANDLE CSensorAdjustDlg::OpenTheDrv(char drv)
{
	HANDLE theHandle=INVALID_HANDLE_VALUE;
	CString csDeviceName;
	CString csPhysical;
	
	
	csDeviceName.Format(_T("\\\\.\\PHYSICALDRIVE%i"),drv);
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


void CSensorAdjustDlg::OnBnClickedButton1()
{
	// TODO: 在此添加控件通知处理程序代码
	if (m_hDevHandle != NULL)
	{
		CloseHandle(m_hDevHandle);
		m_hDevHandle = NULL;
	}
	for(char driver=0;driver<127;driver++)
	{
		HANDLE tmp=OpenTheDrv(driver);
		if (tmp!=NULL && tmp!=INVALID_HANDLE_VALUE)
		{
			m_hDevHandle=tmp;
			m_connectStatus.SetWindowText(_T("Device connected"));
//			RefreshRegs();
			return;
		}
	}
	m_connectStatus.SetWindowText(_T("Device disconnected"));
}

void CSensorAdjustDlg::OnEnChangeAddr()
{
	// TODO:  如果该控件是 RICHEDIT 控件，则它将不会
	// 发送该通知，除非重写 CDialog::OnInitDialog()
	// 函数并调用 CRichEditCtrl().SetEventMask()，
	// 同时将 ENM_CHANGE 标志“或”运算到掩码中。

	// TODO:  在此添加控件通知处理程序代码

	int i;
	UpdateData(true);

	int length=str_addr.GetLength();
	if(0 == addr_16bit.GetCheck())
	{
		if(length>2)
		{
			AfxMessageBox(_T("Range:00-FF"));
			str_addr=str_addr.Left(2);
			UpdateData(false);
			return;
		}
	}
	else
	{
		if(length>4)
		{
			AfxMessageBox(_T("Range:0000-FFFF"));
			str_addr=str_addr.Left(4);
			UpdateData(false);
			return;
		}
	}

	for(i=length-1;i>=0;i--)
	{
		TCHAR tmp=str_addr.GetAt(i);
//		if((tmp>'f')||(tmp<'0'))
		if(((tmp >= '0')&&( tmp <= '9')) || ((tmp >= 'A') && (tmp <= 'F')))
		{

		}
		else
		{
			AfxMessageBox(_T("Range:0-F"));
			str_addr=str_addr.Left(i);
			UpdateData(false);
		}
	}
	UpdateData(false);


}

void CSensorAdjustDlg::OnEnChangeValue()
{
	// TODO:  如果该控件是 RICHEDIT 控件，则它将不会
	// 发送该通知，除非重写 CDialog::OnInitDialog()
	// 函数并调用 CRichEditCtrl().SetEventMask()，
	// 同时将 ENM_CHANGE 标志“或”运算到掩码中。

	// TODO:  在此添加控件通知处理程序代码
	int i;
	UpdateData(true);
	int length=str_value.GetLength();
	if(0 == value_16bit.GetCheck())
	{
		if(length>2)
		{
			AfxMessageBox(_T("Range:00-FF"));
			str_value=str_value.Left(2);
			UpdateData(false);
			return;
		}
	}
	else
	{
		if(length>4)
		{
			AfxMessageBox(_T("Range:0000-FFFF"));
			str_value=str_value.Left(4);
			UpdateData(false);
			return;
		}
	}

	for(i=length-1;i>=0;i--)
	{
		TCHAR tmp=str_value.GetAt(i);
//		if((tmp>'9')||(tmp<'0'))
		if(((tmp >= '0')&&( tmp <= '9')) || ((tmp >= 'A') && (tmp <= 'F')))
		{
			
		}
		else
		{
			AfxMessageBox(_T("Range:0-F"));
			str_value=str_value.Left(i);
			UpdateData(false);
		}
	}

	UpdateData(false);
}


BOOL CSensorAdjustDlg::ReadFromScsi(
								int    cdbLen,
								void  *cdb,
								int    dataLen,
								BYTE  *data//char  *data
								)	//发命令从Scsi读出
{
	
	BOOL	status;
	ULONG	length = 0,
			returned = 0;
	BYTE *tmp = (BYTE*)cdb;
	tmp[0]=0xcb;
	SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER sptdwb;
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
    status = DeviceIoControl(m_hDevHandle,
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


BOOL CSensorAdjustDlg::WriteToScsi(	int    cdbLen,
								void  *cdb,
								int    dataLen,
								BYTE  *data//char  *data
								)	//发命令向Scsi写入
{
	BOOL	status;
	ULONG	length = 0,
			returned = 0;
	BYTE *tmp = (BYTE*)cdb;
	tmp[0]=0xcb;
	SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER sptdwb;
    sptdwb.sptd.Length = sizeof(SCSI_PASS_THROUGH_DIRECT);
    sptdwb.sptd.PathId = 0;
    sptdwb.sptd.TargetId = 1;
    sptdwb.sptd.Lun = 0;
    sptdwb.sptd.CdbLength = cdbLen;
    sptdwb.sptd.SenseInfoLength = 26;	//24;
    sptdwb.sptd.DataIn = SCSI_IOCTL_DATA_OUT;	//写数据
    sptdwb.sptd.DataTransferLength = dataLen;//sectorSize;
    sptdwb.sptd.TimeOutValue = 200;
    sptdwb.sptd.DataBuffer = data;
    sptdwb.sptd.SenseInfoOffset =
       offsetof(SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER,ucSenseBuf);
	memcpy(sptdwb.sptd.Cdb, cdb, cdbLen);
    length = sizeof(SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER);
    status = DeviceIoControl(m_hDevHandle,
                             IOCTL_SCSI_PASS_THROUGH_DIRECT,
                             &sptdwb,
                             length,
                             &sptdwb,
                             length,
                             &returned,
                             FALSE);
	if(status==0)
		TRACE("Write error:%i\n",GetLastError());
	return status;
}

BOOL CSensorAdjustDlg::WriteReg(UINT regAddr, UINT RegValue)
{
	BYTE cdb[16];
	memset(cdb,0,16);
	cdb[1]	=	0xf1;
	cdb[2]	=	(BYTE)((regAddr>>24)&0xFF);
	cdb[3]	=	(BYTE)((regAddr>>16)&0xFF);
	cdb[4]	=	(BYTE)((regAddr>>8)&0xFF);
	cdb[5]	=	(BYTE)(regAddr&0xFF);
	cdb[6]	=	(BYTE)((RegValue>>24)&0xFF);
	cdb[7]	=	(BYTE)((RegValue>>16)&0xFF);
	cdb[8]	=	(BYTE)((RegValue>>8)&0xFF);
	cdb[9]	=	(BYTE)(RegValue&0xFF);
	return WriteToScsi(16,cdb,0,0);
}

BOOL CSensorAdjustDlg::ReadReg(UINT regAddr, UINT *RegValue)
{
	static BYTE cdb[16];
	static BYTE data[16];
	memset(data,0,16);
	memset(cdb,0,16);
	cdb[1]	=	0xf2;
	cdb[2]	=	(BYTE)((regAddr>>24)&0xFF);
	cdb[3]	=	(BYTE)((regAddr>>16)&0xFF);
	cdb[4]	=	(BYTE)((regAddr>>8)&0xFF);
	cdb[5]	=	(BYTE)(regAddr&0xFF);
	
	BOOL bRet = ReadFromScsi(16,cdb,16,data);
	if(bRet)
	{
		if((data[0]==0xcb)&&(data[1]==0xf2))
		{
			*RegValue =(data[2]<<24);
			*RegValue+=(data[3]<<16);
			*RegValue+=(data[4]<<8);
			*RegValue+=(data[5]);
			return TRUE;
		}
		else
			return FALSE;
	}
	else
		return FALSE;
}

UINT CSensorAdjustDlg::hexStr2Value(CString str)
{
	UINT value=0;
	int iLen = str.GetLength();
	for(int i=0;((i<str.GetLength()) && (i< iLen)); i++)
	{
		char c =str[i];
		UINT tmpV=(UINT)-1;
		if( (c >='0') && (c<='9') )
		{
			tmpV=c-'0';
		}
		else if( (c >='A') && (c<='F') )
		{
			tmpV=c-'A' + 10;

		}
		if ( tmpV == (UINT)-1 )
			return (UINT)-1;
		value<<=4;
		value+=tmpV;
	}
	return value;
}


void CSensorAdjustDlg::OnBnClickedButton2()			//read
{
	// TODO: 在此添加控件通知处理程序代码
	UINT u32_addr;
	UINT u32_value;
	if(str_addr.IsEmpty())
	{
		AfxMessageBox(_T("addr is empty!"));
		return;
	}

	u32_addr = hexStr2Value(str_addr);
	

	//======debug======
//	str_debug.Format(_T("listbox.GetCount() = 0x%x,listbox.GetSelCount()=0x%x"),listbox.GetCount(),listbox.GetSelCount());
//	AfxMessageBox(str_debug);
	//=================
	if(1 == listbox.GetSelCount())
	{
		if(ReadReg(u32_addr,&u32_value))
		{
			if(0 == value_16bit.GetCheck())
			{
				str_value.Format(_T("%02x"),u32_value);				//show read value
			}
			else
			{
				str_value.Format(_T("%04x"),u32_value);				//show read value
			}
			str_value.MakeUpper();
			//======handle listbox======
			int i;
			int cur_sel = listbox.GetCurSel();
			int pos_addr,pos_value;
			CString strtmp;

			if(cur_sel == -1)
				return;

			listbox.GetText(cur_sel, strtmp);

			//======debug======
			//	str_debug.Format(_T("listbox.GetCount() = 0x%x"),listbox.GetCount());
			//	AfxMessageBox(str_debug);
			//=================

			pos_addr = strtmp.Find(_T("addr: 0x"));
			if(0 == addr_16bit.GetCheck())
			{
				strtmp.Delete(pos_addr+8,2);
				strtmp.Insert(pos_addr+8,str_addr);
			}
			else
			{
				strtmp.Delete(pos_addr+8,4);
				strtmp.Insert(pos_addr+8,str_addr);
			}

			pos_value = strtmp.Find(_T("value: 0x"));
			if(0 == value_16bit.GetCheck())
			{
				strtmp.Delete(pos_value+9,2);
				strtmp.Insert(pos_value+9,str_value);
			}
			else
			{
				strtmp.Delete(pos_value+9,4);
				strtmp.Insert(pos_value+9,str_value);
			}

			listbox.DeleteString(cur_sel);
			listbox.InsertString(cur_sel,strtmp);
			listbox.SetSel(cur_sel,1);
		}
		else
		{
			AfxMessageBox(_T("Read Fail.Can't find device"));
			OnBnClickedButton1();			//check connect
		}
	}
	else if(listbox.GetSelCount() > 1)
	{
		CString str_tmp_addr,str_tmp_value,strtmp;
		int pos_addr,pos_value;
		int i;
		for(i = 0;i < listbox.GetCount();i++)
		{
			if(listbox.GetSel(i))
			{
				//====get addr and value===
				listbox.GetText(i,strtmp);
				pos_addr = strtmp.Find(_T("addr: 0x"));
				if(0 == addr_16bit.GetCheck())
				{
					str_tmp_addr = strtmp.Mid(pos_addr+8,2);
				}
				else
				{
					str_tmp_addr = strtmp.Mid(pos_addr+8,4);
				}

				pos_value = strtmp.Find(_T("value: 0x"));
				if(0 == value_16bit.GetCheck())
				{
					str_tmp_value = strtmp.Mid(pos_value+9,2);
				}
				else
				{
					str_tmp_value = strtmp.Mid(pos_value+9,4);
				}
				
				u32_addr = hexStr2Value(str_tmp_addr);
				u32_value = hexStr2Value(str_tmp_value);

				//====set addr and value===
				if(ReadReg(u32_addr,&u32_value))
				{
					if(0 == value_16bit.GetCheck())
					{
						str_tmp_value.Format(_T("%02x"),u32_value);				//show read value
					}
					else
					{
						str_tmp_value.Format(_T("%04x"),u32_value);				//show read value
					}
					str_tmp_value.MakeUpper();
					//======handle listbox======

					if(0 == value_16bit.GetCheck())
					{
						strtmp.Delete(pos_value+9,2);
						strtmp.Insert(pos_value+9,str_tmp_value);
					}
					else
					{
						strtmp.Delete(pos_value+9,4);
						strtmp.Insert(pos_value+9,str_tmp_value);
					}

					listbox.DeleteString(i);
					listbox.InsertString(i,strtmp);
					listbox.SetSel(i,1);
				}
				else
				{
					AfxMessageBox(_T("Read Fail.Can't find device"));
					OnBnClickedButton1();			//check connect
				}

			}
		}
	}
	else
	{

	}
	UpdateData(false);
}

void CSensorAdjustDlg::OnBnClickedButton3()			//write
{
	// TODO: 在此添加控件通知处理程序代码
	UINT u32_addr;
	UINT u32_value;

	if(str_addr.IsEmpty() || str_value.IsEmpty())
	{
		AfxMessageBox(_T("addr or value is empty!"));
		return;
	}

	if(1 == listbox.GetSelCount())
	{
		u32_addr = hexStr2Value(str_addr);
		u32_value = hexStr2Value(str_value);
		if(WriteReg(u32_addr,u32_value))
		{
			if(0 == addr_16bit.GetCheck())
			{
				str_addr.Format(_T("%02x"),u32_addr);				//show read value
			}
			else
			{
				str_addr.Format(_T("%04x"),u32_addr);				//show read value
			}
			str_addr.MakeUpper();
			if(0 == value_16bit.GetCheck())
			{
				str_value.Format(_T("%02x"),u32_value);				//show read value
			}
			else
			{
				str_value.Format(_T("%04x"),u32_value);				//show read value
			}
			str_value.MakeUpper();

			//======handle listbox======
			int i;
			int cur_sel = listbox.GetCurSel();
			int pos_addr,pos_value;
			CString strtmp;

			if(cur_sel == -1)
				return;

			listbox.GetText(cur_sel, strtmp);

			pos_addr = strtmp.Find(_T("addr: 0x"));
			if(0 == addr_16bit.GetCheck())
			{
				strtmp.Delete(pos_addr+8,2);
				strtmp.Insert(pos_addr+8,str_addr);
			}
			else
			{
				strtmp.Delete(pos_addr+8,4);
				strtmp.Insert(pos_addr+8,str_addr);
			}

			pos_value = strtmp.Find(_T("value: 0x"));
			if(0 == value_16bit.GetCheck())
			{
				strtmp.Delete(pos_value+9,2);
				strtmp.Insert(pos_value+9,str_value);
			}
			else
			{
				strtmp.Delete(pos_value+9,4);
				strtmp.Insert(pos_value+9,str_value);
			}

			listbox.DeleteString(cur_sel);
			listbox.InsertString(cur_sel,strtmp);
			listbox.SetSel(cur_sel,1);
		}
		else
		{
			AfxMessageBox(_T("Write Fail.Can't find device"));
			OnBnClickedButton1();			//check connect
		}
	}
	else if(listbox.GetSelCount()>1)
	{
		CString str_tmp_addr,str_tmp_value,strtmp;
		int pos_addr,pos_value;
		int i;
		for(i = 0;i < listbox.GetCount();i++)
		{
			if(listbox.GetSel(i))
			{
				//====get addr and value===
				listbox.GetText(i,strtmp);
				pos_addr = strtmp.Find(_T("addr: 0x"));
				if(0 == addr_16bit.GetCheck())
				{
					str_tmp_addr = strtmp.Mid(pos_addr+8,2);
				}
				else
				{
					str_tmp_addr = strtmp.Mid(pos_addr+8,4);
				}

				pos_value = strtmp.Find(_T("value: 0x"));
				if(0 == value_16bit.GetCheck())
				{
					str_tmp_value = strtmp.Mid(pos_value+9,2);
				}
				else
				{
					str_tmp_value = strtmp.Mid(pos_value+9,4);
				}

				u32_addr = hexStr2Value(str_tmp_addr);
				u32_value = hexStr2Value(str_tmp_value);

				//====set addr and value===
				if(WriteReg(u32_addr,u32_value))
				{
					if(0 == addr_16bit.GetCheck())
					{
						str_addr.Format(_T("%02x"),u32_addr);				//show read value
					}
					else
					{
						str_addr.Format(_T("%04x"),u32_addr);				//show read value
					}
					str_addr.MakeUpper();
					if(0 == value_16bit.GetCheck())
					{
						str_value.Format(_T("%02x"),u32_value);				//show read value
					}
					else
					{
						str_value.Format(_T("%04x"),u32_value);				//show read value
					}
					str_value.MakeUpper();
					//======handle listbox======
					if(0 == addr_16bit.GetCheck())
					{
						strtmp.Delete(pos_addr+8,2);
						strtmp.Insert(pos_addr+8,str_addr);
					}
					else
					{
						strtmp.Delete(pos_addr+8,4);
						strtmp.Insert(pos_addr+8,str_addr);
					}

					if(0 == value_16bit.GetCheck())
					{
						strtmp.Delete(pos_value+9,2);
						strtmp.Insert(pos_value+9,str_value);
					}
					else
					{
						strtmp.Delete(pos_value+9,4);
						strtmp.Insert(pos_value+9,str_value);
					}

					listbox.DeleteString(i);
					listbox.InsertString(i,strtmp);
					listbox.SetSel(i,1);
				}
				else
				{
					AfxMessageBox(_T("Read Fail.Can't find device"));
					OnBnClickedButton1();			//check connect
				}

			}
		}
	}
	else
	{

	}
	UpdateData(false);
}

void CSensorAdjustDlg::OnBnClicked_addr()					// 16bit ,8 bit change
{
	// TODO: 在此添加控件通知处理程序代码

	if(0 == addr_16bit.GetCheck())
	{
		str_addr=str_addr.Right(2);

	}
	else
	{
		str_addr.Insert(0,_T("00"));
	}

	//======handle listbox======
	int i;
	int cur_sel = listbox.GetCurSel();
	//======debug======
//	str_debug.Format(_T("listbox.GetCount() = 0x%x"),listbox.GetCount());
//	AfxMessageBox(str_debug);
	//=================

	for(i = 0;i < listbox.GetCount();i++)
	{
		int pos_addr;
		CString strtmp;
		listbox.GetText(i, strtmp);
		pos_addr = strtmp.Find(_T("addr: 0x"));
		if(0 == addr_16bit.GetCheck())
		{
			strtmp.Delete(pos_addr+8,2);
		}
		else
		{
			strtmp.Insert(pos_addr+8,_T("00"));
		}
		listbox.DeleteString(i);
		listbox.InsertString(i,strtmp);
	}

	listbox.SetSel(cur_sel,1);
	UpdateData(false);
}

void CSensorAdjustDlg::OnBnClicked_value()					// 16bit ,8 bit change
{
	// TODO: 在此添加控件通知处理程序代码

	if(0 == value_16bit.GetCheck())
	{
		str_value=str_value.Right(2);
	}
	else
	{
		str_value.Insert(0,_T("00"));
	}

	//======handle listbox======
	int i;
	int cur_sel = listbox.GetCurSel();
	for(i = 0;i < listbox.GetCount();i++)
	{
		int pos_value;
		CString strtmp;
		listbox.GetText(i, strtmp);
		pos_value = strtmp.Find(_T("value: 0x"));
		if(0 == value_16bit.GetCheck())
		{
			strtmp.Delete(pos_value+9,2);
		}
		else
		{
			strtmp.Insert(pos_value+9,_T("00"));
		}
		listbox.DeleteString(i);
		listbox.InsertString(i,strtmp);
	}

	listbox.SetSel(cur_sel,1);
	UpdateData(false);
}

void CSensorAdjustDlg::OnLbnSelchange()
{
	// TODO: 在此添加控件通知处理程序代码

	int pos_addr,pos_value;
	int iSel=listbox.GetCurSel();
	//======debug======
//	str_debug.Format(_T("iSel = 0x%x"),iSel);
//	AfxMessageBox(str_debug);
	//=================
	if(iSel == -1)
		return;

	listbox.SetSel(iSel,1);

	CString strtmp;
	listbox.GetText(iSel, strtmp);

	pos_addr = strtmp.Find(_T("addr: 0x"));
	if(addr_16bit.GetCheck())
	{
		str_addr = strtmp.Mid(pos_addr+8,4);
	}
	else
	{
		str_addr = strtmp.Mid(pos_addr+8,2);
	}

	pos_value = strtmp.Find(_T("value: 0x"));
	if(value_16bit.GetCheck())
	{
		str_value = strtmp.Mid(pos_value+9,4);
	}
	else
	{
		str_value = strtmp.Mid(pos_value+9,2);
	}

	UpdateData(false);
}

void CSensorAdjustDlg::OnBnClickedButton4()
{
	// TODO: 在此添加控件通知处理程序代码

	CString addstr;
	addstr.Empty();
	if(str_addr.IsEmpty())
	{
		if(addr_16bit.GetCheck())
		{
			str_addr.Format(_T("0000"));
		}
		else
		{
			str_addr.Format(_T("00"));
		}
	}

	if(1 == str_addr.GetLength())
	{
		if(addr_16bit.GetCheck())
		{
			str_addr.Insert(0,_T("000"));
		}
		else
		{
			str_addr.Insert(0,_T("0"));
		}
	}
	else if(2 == str_addr.GetLength())
	{
		if(addr_16bit.GetCheck())
		{
			str_addr.Insert(0,_T("00"));
		}
	}
	else if(3 == str_addr.GetLength())
	{
		if(addr_16bit.GetCheck())
		{
			str_addr.Insert(0,_T("0"));
		}
	}

	if(str_value.IsEmpty())
	{
		if(value_16bit.GetCheck())
		{
			str_value.Format(_T("0000"));
		}
		else
		{
			str_value.Format(_T("00"));
		}
	}

	if(1 == str_value.GetLength())
	{
		if(value_16bit.GetCheck())
		{
			str_value.Insert(0,_T("000"));
		}
		else
		{
			str_value.Insert(0,_T("0"));
		}
	}
	else if(2 == str_value.GetLength())
	{
		if(value_16bit.GetCheck())
		{
			str_value.Insert(0,_T("00"));
		}
	}
	else if(3 == str_value.GetLength())
	{
		if(value_16bit.GetCheck())
		{
			str_value.Insert(0,_T("0"));
		}
	}

	addstr+=str_first;
	addstr+=str_addr;
	addstr+=str_mid;
	addstr+=str_value;
//	listbox.AddString(addstr);
	listbox.InsertString(listbox.GetCount(),addstr);

	//===========set addstring select==========
	int i;
	for(i = 0;i < listbox.GetCount();i++)
	{
		listbox.SetSel(i,0);
	}
	listbox.SetSel(listbox.GetCount()-1,1);
	//===========end set addstring select=====
	UpdateData(false);
}

void CSensorAdjustDlg::OnBnClicked_Save()
{
	// TODO: 在此添加控件通知处理程序代码
	CFileDialog fileDlg(FALSE, _T("txt"), _T("setting"),OFN_FILEMUSTEXIST| OFN_HIDEREADONLY, _T("txt Files (*.txt)|*.txt|All Files (*.*)|*.*||"), this);

	if (fileDlg.DoModal()==IDOK)
	{
		int index;
		CString fileName=fileDlg.GetPathName();
		CFile cf;
		if (!cf.Open(fileName,CFile::modeReadWrite|CFile::modeCreate ))
		{
			AfxMessageBox(_T("Create the txt file Fail."));
			return;
		}
		int iRegs	=	listbox.GetCount();
		for(index=0 ; index < iRegs ; index++)
		{
			CString str_src,str_dst;
			listbox.GetText(index,str_src);
			str_dst.Format(_T("%s\r\n"),str_src);
			cf.Write(str_dst,str_dst.GetLength()*sizeof(TCHAR));
		}
		cf.Close();
	}
}
