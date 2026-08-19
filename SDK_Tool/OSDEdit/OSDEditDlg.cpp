// OSDEditDlg.cpp : 实现文件
//

#include "stdafx.h"
#include "OSDEdit.h"
#include "OSDEditDlg.h"
#include <process.h>

#ifdef _DEBUG
#define new DEBUG_NEW
#endif

#define MAX_PIC_NUM  2000
#define CUSTOM_PALETTE	"[CustomPalette]"
#define UM_WM_OSD_ANALYSIS_OK (WM_USER + 101)

DWORD BGRA2ABGR(DWORD BGRA)
{
	DWORD ABGR;
	ABGR = (BGRA>>8)|((BGRA&0xff)<<24);
	return ABGR;

}
DWORD ARGBA2RGBA(DWORD ARGB)
{
	/*
	DWORD RGBA;
	RGBA = ((ARGB&0x00ffffff)<<8)|((ARGB&0xff000000)>>24);
	return RGBA;
	*/
	DWORD ABGR;
	ABGR = (ARGB&0xff00ff00)|((ARGB&0xff0000)>>16)|((ARGB&0xff)<<16)|0xff000000;
	return ABGR;
}
BOOL CheckIndex(BYTE Data,BYTE *pData,DWORD Len)
{
	while(Len--)
	{
		if(Data == *pData )
		{
			return FALSE;
		}
		pData++;
	}
	return TRUE;
}
DWORD CheckPalette(DWORD Data,DWORD *pData,DWORD Len)
{
	DWORD i;
	for(i=0;i<Len;i++)
	{
		if(Data == *pData )
		{
			break;
		}
		pData++;
	}
	return i;
}

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


// COSDEditDlg 对话框




COSDEditDlg::COSDEditDlg(CWnd* pParent /*=NULL*/)
	: CDialog(COSDEditDlg::IDD, pParent)
	, T_inputFile_path(_T(""))
	, picFileCnt(0)
	, picHorizontalPos(0)
	, picVerticalPos(0)
	, T_ouputFileDir(_T(""))
	, T_current_path(_T(""))
	, m_PaletteResultSrcCnt(0)
	, curPosColor(0)
	, m_PaletteCustomModifyFlag(FALSE)
{
	m_hIcon = AfxGetApp()->LoadIcon(IDR_MAINFRAME);
	curInClient.x = 0;
	curInClient.y = 0;
}

void COSDEditDlg::DoDataExchange(CDataExchange* pDX)
{
	CDialog::DoDataExchange(pDX);
	DDX_Text(pDX, IDC_EDIT_DIR, T_inputFile_path);
	DDX_Control(pDX, IDC_LIST_PIC, m_PicListBox);
	DDX_Control(pDX, IDC_LIST_PIC_PALETTE, m_listPicPalette);
	DDX_Control(pDX, IDC_EDIT_PIC_ALPHA, m_EditPicPaletteAlpha);
	DDX_Control(pDX, IDC_LIST_PALETTE, m_ListCustomPalette);
	DDX_Text(pDX, IDC_EDIT_OUTPUT_DIR, T_ouputFileDir);
	DDX_Control(pDX, IDC_PROGRESS_OSD, m_progressOsd);
}

BEGIN_MESSAGE_MAP(COSDEditDlg, CDialog)
	ON_WM_SYSCOMMAND()
	ON_WM_PAINT()
	ON_WM_QUERYDRAGICON()
	//}}AFX_MSG_MAP
	ON_BN_CLICKED(IDC_BUT_DIR, &COSDEditDlg::OnBnClickedButDir)
	ON_LBN_SELCHANGE(IDC_LIST_PIC, &COSDEditDlg::OnLbnSelchangeListPic)
//	ON_STN_CLICKED(IDC_PIC_PREVIEW, &COSDEditDlg::OnStnClickedPicPreview)
	ON_WM_LBUTTONDOWN()
	ON_LBN_SELCHANGE(IDC_LIST_PIC_PALETTE, &COSDEditDlg::OnLbnSelchangeListPicPalette)
	ON_BN_CLICKED(IDC_BUTTON_ALLPIC_MODIFY, &COSDEditDlg::OnBnClickedButtonAllpicModify)
	ON_BN_CLICKED(IDC_BUTTON_PIC_MODIFY, &COSDEditDlg::OnBnClickedButtonPicModify)
	ON_BN_CLICKED(IDC_BUTTON_CUSTOM_ADD, &COSDEditDlg::OnBnClickedButtonCustomAdd)
	ON_BN_CLICKED(IDC_BUTTON_CUSTOM_MODIFY, &COSDEditDlg::OnBnClickedButtonCustomModify)
	ON_LBN_SELCHANGE(IDC_LIST_PALETTE, &COSDEditDlg::OnLbnSelchangeListPalette)
	ON_BN_CLICKED(IDC_BUTTON_GEN, &COSDEditDlg::OnBnClickedButtonGen)
	ON_WM_CTLCOLOR()
	ON_BN_CLICKED(IDC_BUTTON_OUTDIR, &COSDEditDlg::OnBnClickedButtonOutdir)
	ON_WM_CLOSE()
	ON_BN_CLICKED(IDC_BUTTON_CUSTOM_DEL, &COSDEditDlg::OnBnClickedButtonCustomDel)
	ON_MESSAGE(UM_WM_OSD_ANALYSIS_OK, OnUserOSDAnalysisOKMsg)
END_MESSAGE_MAP()


// COSDEditDlg 消息处理程序

BOOL COSDEditDlg::OnInitDialog()
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
	m_EditPicPaletteAlpha.SetLimitText(2);
	((CEdit*)GetDlgItem(IDC_EDIT_CUSTOM_R))->SetLimitText(2);
	((CEdit*)GetDlgItem(IDC_EDIT_CUSTOM_G))->SetLimitText(2);
	((CEdit*)GetDlgItem(IDC_EDIT_CUSTOM_B))->SetLimitText(2);
	((CEdit*)GetDlgItem(IDC_EDIT_CUSTOM_ALPHA))->SetLimitText(2);
	((CEdit*)GetDlgItem(IDC_EDIT_CUSTOM_COMMENT))->SetLimitText(24);
	GetDlgItem(IDC_BUTTON_CUSTOM_MODIFY)->EnableWindow(FALSE);
	GetDlgItem(IDC_BUTTON_PIC_MODIFY)->EnableWindow(FALSE);
	GetDlgItem(IDC_BUTTON_ALLPIC_MODIFY)->EnableWindow(FALSE);
	//hbmp = NULL;
	curClickedValid = FALSE;
	listCurSel = -1;
	m_PaletteCustomCnt = 0;
	m_PaletteResultCnt = 0;
	m_PaletteResultSrcCnt = 0;
	memset(m_PaletteCustom,0,sizeof(m_PaletteCustom));
	m_PaletteCustomModifyFlag = FALSE;
	m_progressOsd.ShowWindow(FALSE);
	m_progressOsd.SetRange(0,100);
	m_progressOsd.SetPos(0);
	m_progressOsd.SetStep(10);

	//获取当前应用程序路径
	TCHAR temp[1024];
	GetCurrentDirectory(MAX_PATH,temp);
	T_inputFile_path.Format(_T(""),temp);
	T_ouputFileDir.Format(_T("%s"),temp);
	T_current_path.Format(_T("%s"),temp);

	if(loadConfig() == TRUE) {
		CString strTmp;
		DWORD i;
		for(i=0;i<m_PaletteCustomCnt;i++) {
			strTmp.Format(_T(" %03d: %02X %02X %02X-------%02X-------%s "),255-i,m_PaletteCustom[i]&0x000000ff,(m_PaletteCustom[i]&0x0000ff00)>>8,
								(m_PaletteCustom[i]&0x00ff0000)>>16,(m_PaletteCustom[i]&0xff000000)>>24,m_PaletteCustomComment[i]);
			m_ListCustomPalette.AddString(strTmp);
		}
		if(m_PaletteCustomCnt != 0) {
			m_ListCustomPalette.SetCurSel(0);
			DrawColor(IDC_STATIC_CUSTOM_COLOR,m_PaletteCustom[0]&0xffffff);
		}
		GetDlgItem(IDC_BUTTON_CUSTOM_MODIFY)->EnableWindow(TRUE);
	}
	OnLbnSelchangeListPalette();

	UpdateData(FALSE);
	//UpdatePicListFromDirChanged();

	return TRUE;  // 除非将焦点设置到控件，否则返回 TRUE
}

void COSDEditDlg::OnSysCommand(UINT nID, LPARAM lParam)
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

void COSDEditDlg::OnPaint()
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
		//在STATIC控件中显示位图
		//if(hbmp != NULL){
			//显示原始图片
			//ShowSrcBmp();            
		//}
		if(hbmp.m_hObject) {
			ShowSrcBmp();         
		}
		if(curClickedValid) {
			DrawColor(IDC_STATIC_PREVIEW_COLOR,m_PaletteResult[curBmpColorIndex]&0xffffff);
			DrawColor(IDC_STATIC_PALETTE_COLOR,m_PaletteResult[m_listPicPalette.GetCurSel()]&0xffffff);
		}
		if(m_ListCustomPalette.GetCount()>0 && m_ListCustomPalette.GetCurSel() < m_ListCustomPalette.GetCount()) {
			DrawColor(IDC_STATIC_CUSTOM_COLOR,m_PaletteCustom[m_ListCustomPalette.GetCurSel()]&0xffffff);
		}
	}
	
}

//当用户拖动最小化窗口时系统调用此函数取得光标
//显示。
HCURSOR COSDEditDlg::OnQueryDragIcon()
{
	return static_cast<HCURSOR>(m_hIcon);
}

int CALLBACK BrowseCallbackProc(HWND hwnd,UINT uMsg,LPARAM lParam,LPARAM lpData)  
{
#if 1
	if(uMsg == BFFM_INITIALIZED)
	{  
		SendMessage(hwnd, BFFM_SETSELECTION, TRUE, lpData);
	}
#else
	switch(uMsg) 
	{
	case BFFM_INITIALIZED: 
		{
			// WParam is TRUE since you are passing a path.
			// It would be FALSE if you were passing a pidl.
			TCHAR szDir[MAX_PATH]={0};
			GetCurrentDirectory(sizeof(szDir)/sizeof(TCHAR), szDir);
			SendMessage(hwnd, BFFM_SETSELECTION, TRUE, (LPARAM)szDir);
		}
		break;
	}
#endif
	return 0;  
}

void COSDEditDlg::OnBnClickedButDir()
{
	// TODO: 在此添加控件通知处理程序代码
#if 0
	//打开文件
	CFileDialog FileDialog(TRUE,_T("NULL"),NULL,OFN_HIDEREADONLY|OFN_OVERWRITEPROMPT,_T("*.*(*.*)|*.*"));		//任意文件

	if (FileDialog.DoModal() == IDOK)
	{
		T_inputFile_path = FileDialog.GetPathName();	//取得打开的文件路径
	}
#else
	BROWSEINFO bi;

	TCHAR Buffer[MAX_PATH];

	//初始化入口参数bi开始
	bi.hwndOwner = this->m_hWnd; 
	bi.pidlRoot =NULL;//初始化制定的root目录很不容易，
	bi.pszDisplayName = Buffer;//此参数如为NULL则不能显示对话框
	bi.lpszTitle = _T("请选择目录");
	//bi.ulFlags = BIF_BROWSEINCLUDEFILES;//包括文件
	bi.ulFlags = BIF_RETURNONLYFSDIRS |BIF_EDITBOX;//BIF_EDITBOX;//包括文件
	bi.lpfn = NULL;
	bi.iImage=IDR_MAINFRAME;
	if(T_inputFile_path.GetLength()) {
		bi.lParam = (long)(T_inputFile_path.GetBuffer(T_inputFile_path.GetLength()));
		bi.lpfn = BrowseCallbackProc;
	}
	else {	
		//bi.lParam = NULL;
		//bi.lpfn = NULL;
		bi.lParam = (long)(T_current_path.GetBuffer(T_current_path.GetLength()));
		bi.lpfn = BrowseCallbackProc;
	}
	//初始化入口参数bi结束
	LPITEMIDLIST pIDList = SHBrowseForFolder(&bi);//调用显示选择对话框
	if(pIDList)
	{
		SHGetPathFromIDList(pIDList, Buffer);
		//取得文件夹路径到Buffer里
		T_inputFile_path = Buffer;//将文件夹路径保存在一个CString对象里
	}
	CoTaskMemFree(pIDList);
#endif
	
	UpdateData(FALSE);

	//更新图片文件列表
	UpdatePicListFromDirChanged();

	//(HANDLE)_beginthreadex(
	//	NULL,	// Security attributes
//		0,	    // stack
	//	UpdatePicListFromDirChanged,   	// Thread proc
	//	this,	// Thread param
	//	0,              	// creation mode
	//	NULL);	// Thread ID

}

BOOL COSDEditDlg::UpdatePicListFromDirChanged(void)
{
	CString strTmp,strFileTitle,strFileName;
	DWORD fileCnt =  0;	//file number
	strTmp.Format(_T("%s\\*.bmp"),T_inputFile_path);
	m_PicListBox.ResetContent();
	CFileFind finder;
	BOOL bWorking = finder.FindFile(strTmp);	
	while (bWorking)
	{
		bWorking = finder.FindNextFile();
		strFileTitle = finder.GetFileTitle();
		strFileName = finder.GetFileName();

		//HANDLE hFile;
		CFile fileTmp;
		CFileException ex;	
		BITMAPFILEHEADER bmpfheader;
		BITMAPINFOHEADER bmpiheader;
		//DWORD nBytesRead;

		strTmp.Format(_T("%s\\%s"),T_inputFile_path,strFileName);

		if ( !fileTmp.Open(strTmp, CFile::modeRead|CFile::typeBinary, &ex)) {
			strTmp.Format(_T("open file %s fail"),strTmp);
			AfxMessageBox(strTmp);
			continue;
		}
		fileTmp.Read(&bmpfheader,sizeof(BITMAPFILEHEADER));
		fileTmp.Read(&bmpiheader,sizeof(BITMAPINFOHEADER));
		fileTmp.Close();
		//ReadFile(hFile,&bmpfheader,sizeof(BITMAPFILEHEADER),&nBytesRead,NULL);
		//ReadFile(hFile,&bmpiheader,sizeof(BITMAPINFOHEADER),&nBytesRead,NULL);
		//CloseHandle(hFile);

		if(bmpiheader.biBitCount!= 8) {
			strTmp.Format(_T("%s is not a 8bit BMP file"),strFileName);
			AfxMessageBox(strTmp);
			continue;
		}

		//if(finder.IsDots() || finder.IsDirectory())	//忽略目录路径
		//	continue;
		m_PicListBox.AddString(strFileTitle);
		fileCnt++;
		if(fileCnt > MAX_PIC_NUM) {
			strTmp.Format(_T("The maximum number of pictures is limit to %d"), MAX_PIC_NUM);
			AfxMessageBox(strTmp);
			return 1;
		}

	}
	picFileCnt = fileCnt;
		m_listPicPalette.ResetContent();
		hbmp.Detach();
		Invalidate();
		//设置水平位置显示信息
		strTmp.Format(_T(""));
		GetDlgItem(IDC_HORIZONTAL)->SetWindowText(strTmp);
		//设置垂直位置显示信息
		GetDlgItem(IDC_VERTICAL)->SetWindowText(strTmp);
		//设置颜色值显示
		GetDlgItem(IDC_STATIC_PREVIEW_RGB)->SetWindowText(strTmp);
		GetDlgItem(IDC_EDIT_PIC_R)->SetWindowText(strTmp);
		GetDlgItem(IDC_EDIT_PIC_G)->SetWindowText(strTmp);
		GetDlgItem(IDC_EDIT_PIC_B)->SetWindowText(strTmp);
		GetDlgItem(IDC_EDIT_PIC_ALPHA)->SetWindowText(strTmp);
		GetDlgItem(IDC_BUTTON_PIC_MODIFY)->EnableWindow(FALSE);
		GetDlgItem(IDC_BUTTON_ALLPIC_MODIFY)->EnableWindow(FALSE);
		curClickedValid = FALSE;
		listCurSel = -1;
		m_PaletteResultCnt = 0;
		m_PaletteResultSrcCnt = 0;
		memset(m_PaletteResult,0,sizeof(m_PaletteResult));


	//AnalysisOsdData();
	(HANDLE)_beginthreadex(
		NULL,	// Security attributes
		0,	    // stack
		AnalysisOsdData,   	// Thread proc
		this,	// Thread param
		0,              	// creation mode
		NULL);	// Thread ID


#if 0
	AddPaletteList();
	if(picFileCnt) {
		m_PicListBox.SetCurSel(0);
		OnLbnSelchangeListPic();
	}
	else {
		hbmp.Detach();
		Invalidate();
		//设置水平位置显示信息
		strTmp.Format(_T(""));
		GetDlgItem(IDC_HORIZONTAL)->SetWindowText(strTmp);
		//设置垂直位置显示信息
		GetDlgItem(IDC_VERTICAL)->SetWindowText(strTmp);
		//设置颜色值显示
		GetDlgItem(IDC_STATIC_PREVIEW_RGB)->SetWindowText(strTmp);
		GetDlgItem(IDC_EDIT_PIC_R)->SetWindowText(strTmp);
		GetDlgItem(IDC_EDIT_PIC_G)->SetWindowText(strTmp);
		GetDlgItem(IDC_EDIT_PIC_B)->SetWindowText(strTmp);
		GetDlgItem(IDC_EDIT_PIC_ALPHA)->SetWindowText(strTmp);
		GetDlgItem(IDC_BUTTON_PIC_MODIFY)->EnableWindow(FALSE);
		GetDlgItem(IDC_BUTTON_ALLPIC_MODIFY)->EnableWindow(FALSE);
	}
	UpdateData(FALSE);
	::SendMessage(GetSafeHwnd(),UM_WM_OSD_ANALYSIS_OK,0L,0L);
#endif
	return 0;
}

void COSDEditDlg::OnLbnSelchangeListPic()
{
	// TODO: 在此添加控件通知处理程序代码
	CFileException ex;	
	CString strTmp,strFilePath;
	if(listCurSel == m_PicListBox.GetCurSel())
		return;
	listCurSel = m_PicListBox.GetCurSel();
	m_PicListBox.GetText(m_PicListBox.GetCurSel(),strTmp);
	strFilePath.Format(_T("%s\\%s.bmp"),T_inputFile_path,strTmp);
	loadBMPFile(strFilePath);
	if(picFile.m_hFile != CFile::hFileNull) {
		picFile.Close();
	}
	if ( !picFile.Open(strFilePath, CFile::modeRead|CFile::typeBinary, &ex)) {
		strTmp.Format(_T("open file %s fail"),strFilePath);
		AfxMessageBox(strTmp);
	}
	//获取BMP HEADER
	//picFile.Seek(sizeof(BITMAPFILEHEADER),CFile::begin);
	picFile.Read(&m_bmpFileHeader,sizeof(m_bmpFileHeader));
	picFile.Read(&m_bmpInfoHeader,sizeof(m_bmpInfoHeader));
	//获取BMP Palette
	UINT size;
	size = (m_bmpFileHeader.bfOffBits-m_bmpInfoHeader.biSize - 0x0E > sizeof(m_bmpPalette)) ? sizeof(m_bmpPalette) : m_bmpFileHeader.bfOffBits-m_bmpInfoHeader.biSize - 0x0E;
	picFile.Read(m_bmpPalette,size);
	//Invalidate();
	CRect lRect;
	CStatic * pStatic=(CStatic*)GetDlgItem(IDC_PIC_PREVIEW);
	pStatic->GetWindowRect(&lRect); 
	ScreenToClient(&lRect);   
	this->InvalidateRect(&lRect,TRUE);
	if(buttonDownIndicate(curPointInPic) == FALSE) {
		CStatic *pStatic=(CStatic*)GetDlgItem(IDC_PIC_PREVIEW);
		//获取Static控件的大小范围
		pStatic->GetWindowRect(&lRect); 
		ScreenToClient(&lRect);
			
		curPointInPic.x = (lRect.left + lRect.right) / 2;
		curPointInPic.y = (lRect.top + lRect.bottom) / 2;
		buttonDownIndicate(curPointInPic);
	}
	picFile.Close();
	//strTmp2.Format(_T("selchange:%d,%s"),m_PicListBox.GetCurSel(),strTmp);
	//AfxMessageBox(strTmp2);
	//loadBMPFile();
}

void COSDEditDlg::loadBMPFile(CString bmpPath)
{
	HBITMAP hbitmap;
	//装载资源*.bmp
	hbitmap=(HBITMAP)::LoadImage (::AfxGetInstanceHandle(),bmpPath,IMAGE_BITMAP,0,0,LR_LOADFROMFILE|LR_CREATEDIBSECTION);
	//NEW资源(调用一次重新拷贝一次)
	//if (hbmp != NULL)
	//{
		//delete hbmp;
		//DeleteObject(hbmp);
		//hbmp = NULL;
	//}
	//创建位图
	//hbmp = CBitmap::FromHandle(hbitmap);
	//this->Invalidate();
	if(hbmp.m_hObject) {
		hbmp.Detach();
	}
	hbmp.Attach(hbitmap);
	//获取图片内容    
	hbmp.GetBitmap(&bmp);
	//ShowSrcBmp();  
}

void COSDEditDlg::ShowSrcBmp(void)
{
	//将pStatic指向要显示的地方
	CStatic *pStatic = NULL;
	//根据ID获取Static控件
	pStatic=(CStatic*)GetDlgItem(IDC_PIC_PREVIEW);
	////////////////////////////////////////////////
	/**这一步相当重要，否则无法实现自绘*****/
	////////////////////////////////////////////////
	pStatic->ModifyStyle(0,BS_OWNERDRAW);
	//创建DC
	CClientDC dc(pStatic);

	//获取图片内容    
	//hbmp.GetBitmap(&bmp);
	CDC dcMem;

	//创建兼容DC
	dcMem.CreateCompatibleDC(&dc);
	CBitmap *pOldBitmap=(CBitmap*)dcMem.SelectObject(&hbmp);
	CRect lRect;

	//获取Static控件的大小范围
	pStatic->GetClientRect(&lRect);    
	//在Static控件上显示位图
	//判断是否需要调整到适合画布
	//在Static控件上显示位图
	//dc.StretchBlt(lRect.left ,lRect.top ,lRect.Width(),lRect.Height(),
	//	&dcMem,0 ,0,bmp.bmWidth,bmp.bmHeight,SRCCOPY);
	if(bmp.bmHeight*lRect.Width()/bmp.bmWidth < lRect.Height()) {
		dc.StretchBlt(lRect.left ,lRect.top+(lRect.Height()-bmp.bmHeight*lRect.Width()/bmp.bmWidth)/2 ,lRect.Width(),bmp.bmHeight*lRect.Width()/bmp.bmWidth,
				&dcMem,0 ,0,bmp.bmWidth,bmp.bmHeight,SRCCOPY);
	}
	else {
		dc.StretchBlt(lRect.left + (lRect.Width()-bmp.bmWidth*lRect.Height()/bmp.bmHeight)/2 ,lRect.top ,bmp.bmWidth*lRect.Height()/bmp.bmHeight,lRect.Height(),
			&dcMem,0 ,0,bmp.bmWidth,bmp.bmHeight,SRCCOPY);
	}
	if(curClickedValid) {
		//curClickedValid = FALSE;

		CPen pen;
		pen.CreatePen (PS_SOLID,2,RGB(255,0,0));
		//将画笔选入
		dc.SelectObject (&pen);
		//dc.SelectStockObject(NULL_BRUSH);
		dc.SelectObject(GetStockObject(NULL_BRUSH));
		dc.Ellipse(CRect(curInClient.x-3,curInClient.y-3,curInClient.x+3,curInClient.y+3));
		pen.DeleteObject();
		pen.CreatePen (PS_SOLID,2,RGB(0,255,255));
		dc.SelectObject (&pen);
		dc.Ellipse(CRect(curInClient.x-6,curInClient.y-6,curInClient.x+6,curInClient.y+6));
		//dc.SelectObject(&pen2);
		
		//dc.SelectObject(oldPen);
		//dc.MoveTo(curInClient.x,curInClient.y);
		//dc.LineTo(curInClient.x+10,curInClient.y+10);

		//CString strTmp;
		//strTmp.Format(_T("PIC:,%d,%d,%d,%d,REC:%d/%d,%d/%d"),curPosInRect.x,curPosInRect.y,curInClient.x,curInClient.y,curPosInPic.x,bmp.bmWidth,curPosInPic.y,bmp.bmHeight);
		//AfxMessageBox(strTmp);

	}

	dcMem.SelectObject(pOldBitmap);
	DeleteObject(&hbmp);               //删除位图
	dcMem.DeleteDC();                      //删除后台DC
}

//void COSDEditDlg::OnStnClickedPicPreview()
//{
//	// TODO: 在此添加控件通知处理程序代码
//	CString strTmp;
//	strTmp.Format(_T("Clicked picture"));
//	AfxMessageBox(strTmp);
//}
BOOL COSDEditDlg::buttonDownIndicate(CPoint point)
{
	if(hbmp.m_hObject) {
		LONG x0,y0,width,height;
		
		//根据ID获取Static控件
		CStatic *pStatic=(CStatic*)GetDlgItem(IDC_PIC_PREVIEW);
		CRect lRect;
		//获取Static控件的大小范围
		pStatic->GetWindowRect(&lRect); 
		ScreenToClient(&lRect);

		if(bmp.bmHeight*lRect.Width()/bmp.bmWidth < lRect.Height()) {
			x0 = lRect.left;
			y0 = lRect.top+(lRect.Height()-bmp.bmHeight*lRect.Width()/bmp.bmWidth)/2;
			width = lRect.Width();
			height = bmp.bmHeight*lRect.Width()/bmp.bmWidth;
			curPosInRect.x = point.x-x0;
			curPosInRect.y = point.y-y0;
			curPosInPic.x = curPosInRect.x * bmp.bmWidth / lRect.Width();
			curPosInPic.y = curPosInRect.y * bmp.bmWidth / lRect.Width();
		}
		else {
			x0 = lRect.left + (lRect.Width()-bmp.bmWidth*lRect.Height()/bmp.bmHeight)/2;
			y0 = lRect.top;
			width = bmp.bmWidth*lRect.Height()/bmp.bmHeight;
			height = lRect.Height();
			curPosInRect.x = point.x-x0;
			curPosInRect.y = point.y-y0;
			curPosInPic.x = curPosInRect.x * bmp.bmHeight / lRect.Height();
			curPosInPic.y = curPosInRect.y * bmp.bmHeight / lRect.Height();
		}

		if(curPosInRect.x>0 && curPosInRect.x<width && curPosInRect.y>0 &&  curPosInRect.y<height) {	//鼠标单击位置在图片范围内
			curInClient.x = point.x - lRect.left;
			curInClient.y = point.y - lRect.top;

			curClickedValid = TRUE;
			curPointInPic = point;
			//获取当前点颜色
			DWORD offset;
			DWORD color;
#if 0
			if(m_bmpInfoHeader.biHeight > 0) {
				if(m_bmpInfoHeader.biWidth > 0) {
					offset = m_bmpInfoHeader.biWidth * (m_bmpInfoHeader.biHeight -1 - curPosInPic.y) + curPosInPic.x;
				}
				else {
					offset = (-m_bmpInfoHeader.biWidth) * (m_bmpInfoHeader.biHeight -1 - curPosInPic.y) + (-m_bmpInfoHeader.biWidth) - curPosInPic.x;
				}
			}
			else {
				if(m_bmpInfoHeader.biWidth > 0) {
					offset = m_bmpInfoHeader.biWidth * curPosInPic.y + curPosInPic.x;
				}
				else {
					offset = (-m_bmpInfoHeader.biWidth) * curPosInPic.y + (-m_bmpInfoHeader.biWidth) - curPosInPic.x;
				}
			}
			picFile.Seek(m_bmpFileHeader.bfOffBits+offset,CFile::begin);
			picFile.Read(&curBmpColorIndex,1);
			color = m_bmpPalette[curBmpColorIndex];
			CString strTmp;
			strTmp.Format(_T("R:0x%02x,G:0x%02x,B:0x%02x"),(color&0xff0000)>>16,(color&0x00ff00)>>8,(color&0x0000ff)>>0);
			GetDlgItem(IDC_STATIC_PREVIEW_RGB)->SetWindowText(strTmp);

			//设置水平位置显示信息
			strTmp.Format(_T("H:%d/%d"),curPosInPic.x+1,m_bmpInfoHeader.biWidth);
			GetDlgItem(IDC_HORIZONTAL)->SetWindowText(strTmp);

			//设置垂直位置显示信息
			strTmp.Format(_T("V:%d/%d"),curPosInPic.y+1,m_bmpInfoHeader.biHeight);
			GetDlgItem(IDC_VERTICAL)->SetWindowText(strTmp);
#endif
#if 1
			offset = m_bmpInfoHeader.biWidth * curPosInPic.y + curPosInPic.x;
			OSD_SOURCE_INF dataSource;
			CString strPathTmp;
			strPathTmp.Format(_T("%s\\OSD_source.tmp"),T_current_path);
			fileOsdBin.Open(strPathTmp, CFile::modeRead|CFile::typeBinary);
			fileOsdBin.Seek(listCurSel*sizeof(OSD_SOURCE_INF),CFile::begin);
			fileOsdBin.Read(&dataSource,sizeof(OSD_SOURCE_INF));
			fileOsdBin.Seek(dataSource.dataoffset+offset,CFile::begin);
			fileOsdBin.Read(&curBmpColorIndex,1);
			color = m_PaletteResult[curBmpColorIndex];
			fileOsdBin.Close();
			//更新图片调色板列表框
			m_listPicPalette.SetCurSel(curBmpColorIndex);
			OnLbnSelchangeListPicPalette();
			GetDlgItem(IDC_BUTTON_PIC_MODIFY)->EnableWindow(TRUE);
			GetDlgItem(IDC_BUTTON_ALLPIC_MODIFY)->EnableWindow(TRUE);
			curPosColor = color;

			CString strTmp;
			strTmp.Format(_T("R:0x%02x,G:0x%02x,B:0x%02x,A:0x%02x"),(color&0x0000ff)>>0,(color&0x00ff00)>>8,(color&0xff0000)>>16,(color&0xff000000)>>24);
			GetDlgItem(IDC_STATIC_PREVIEW_RGB)->SetWindowText(strTmp);

			//设置水平位置显示信息
			strTmp.Format(_T("H:%d/%d"),curPosInPic.x+1,m_bmpInfoHeader.biWidth);
			GetDlgItem(IDC_HORIZONTAL)->SetWindowText(strTmp);

			//设置垂直位置显示信息
			strTmp.Format(_T("V:%d/%d"),curPosInPic.y+1,m_bmpInfoHeader.biHeight);
			GetDlgItem(IDC_VERTICAL)->SetWindowText(strTmp);
#endif
			//pStatic=(CStatic*)GetDlgItem(IDC_STATIC_PREVIEW_COLOR);
			//pStatic->SetBkColor(RGB((color&0xff0000)>>16,(color&0x00ff00)>>8,(color&0x0000ff)>>0));

			//CString strTmp;
			//strTmp.Format(_T("color:%08x"),color);
			//AfxMessageBox(strTmp);
			//Invalidate();
			CPoint topLeft,botRight;
			topLeft = lRect.TopLeft();
			botRight = lRect.BottomRight();
			lRect.SetRect(topLeft.x-10,topLeft.y-10,botRight.x+10,botRight.y+10);
			this->InvalidateRect(&lRect,TRUE);

			DrawColor(IDC_STATIC_PREVIEW_COLOR,m_PaletteResult[curBmpColorIndex]&0xffffff);
			return TRUE;
		}
		else {
			return FALSE;
		}
	}
	return FALSE;
}
void COSDEditDlg::OnLButtonDown(UINT nFlags, CPoint point)
{
	// TODO: 在此添加消息处理程序代码和/或调用默认值

	CDialog::OnLButtonDown(nFlags, point);
	buttonDownIndicate(point);
	
}

unsigned int WINAPI COSDEditDlg::AnalysisOsdData(void *p)
{
	COSDEditDlg *pDlg = (COSDEditDlg *)p; 
	CFileException ex;	
	CString strTmp,strFilePath;

	DWORD iBmpCnt;
	BITMAPFILEHEADER bmpFileHeader;
	BITMAPINFO bmpInfoHeader;
	DWORD dwPicDataOff  = 0;
	DWORD dwPicDataLen  = 0;

	if(pDlg->picFileCnt == 0) {
		pDlg->curClickedValid = FALSE;
		pDlg->listCurSel = -1;
		pDlg->m_PaletteResultCnt = 0;
		pDlg->m_PaletteResultSrcCnt = 0;
		memset(pDlg->m_PaletteResult,0,sizeof(pDlg->m_PaletteResult));
		return 1;
	}
	pDlg->m_progressOsd.SetPos(0);
	pDlg->m_progressOsd.ShowWindow(TRUE);


	BMP_INF    *BmpArray = new BMP_INF[MAX_PIC_NUM];
	OSD_SOURCE_INF *bmpIndexArray = new OSD_SOURCE_INF[MAX_PIC_NUM];

	if(NULL == BmpArray)
	{
			return 3;
	}

	if(NULL == bmpIndexArray)
	{
			if(NULL != BmpArray)
			{
				delete []BmpArray; BmpArray = NULL;
			}
			return 4;
	}

	memset(bmpIndexArray, 0, MAX_PIC_NUM*sizeof(OSD_SOURCE_INF));
	memset(BmpArray, 0, MAX_PIC_NUM*sizeof(BMP_INF));

	//建立临时文件
	CString strPathTmp;
	strPathTmp.Format(_T("%s\\OSD_source.tmp"),pDlg->T_current_path);
	pDlg->fileOsdBin.Open(strPathTmp, CFile::modeCreate|CFile::typeBinary|CFile::modeReadWrite);
	pDlg->fileOsdBin.Write(bmpIndexArray, pDlg->picFileCnt*sizeof(OSD_SOURCE_INF));
	
	//strTmp.Format(_T("picFileCnt=%d"),pDlg->picFileCnt);
	//AfxMessageBox(strTmp);

	pDlg->m_progressOsd.SetPos(10);
	for(iBmpCnt=0;iBmpCnt<pDlg->picFileCnt;iBmpCnt++) {
		pDlg->m_PicListBox.GetText(iBmpCnt,strTmp);
		strFilePath.Format(_T("%s\\%s.bmp"),pDlg->T_inputFile_path,strTmp);
		if(pDlg->picFile.m_hFile != CFile::hFileNull) {
			pDlg->picFile.Close();
		}
		if ( !pDlg->picFile.Open(strFilePath, CFile::modeRead|CFile::typeBinary, &ex)) {
			strTmp.Format(_T("open file %s fail"),strFilePath);
			AfxMessageBox(strTmp);
		}
		
		pDlg->picFile.Read(&bmpFileHeader,sizeof(bmpFileHeader));
		pDlg->picFile.Read(&bmpInfoHeader,sizeof(bmpInfoHeader));

		BmpArray[iBmpCnt].BmpNum = iBmpCnt;
		pDlg->picFile.Seek(0x36, CFile::begin);
		pDlg->picFile.Read(&(BmpArray[iBmpCnt].Palette), sizeof(BmpArray[iBmpCnt].Palette));

		dwPicDataOff = bmpFileHeader.bfOffBits;
		dwPicDataLen  = bmpInfoHeader.bmiHeader.biHeight * bmpInfoHeader.bmiHeader.biWidth * bmpInfoHeader.bmiHeader.biBitCount/8;
		//==test==
		/*
		int len=(int)dwPicDataOff;
		strTmp.Format(_T("iBmpCnt=%d,bmpw=%d,len=%d\n"),bmpInfoHeader.bmiHeader.biWidth,bmpInfoHeader.bmiHeader.biHeight,len);
		AfxMessageBox(strTmp);
		*/
		//==end test==
		unsigned int pic_align_w;
		pic_align_w=(bmpInfoHeader.bmiHeader.biWidth+0x3)& ~0x3;	// 4bytes align

		unsigned int temp_picdatalen=pic_align_w*bmpInfoHeader.bmiHeader.biHeight* bmpInfoHeader.bmiHeader.biBitCount/8;
		unsigned char *ptempPicData = new unsigned char[temp_picdatalen];
		unsigned char *pPicData = new unsigned char[dwPicDataLen];
		unsigned char *pBmpIndex;
		pDlg->picFile.Seek(dwPicDataOff, CFile::begin);
		pDlg->picFile.Read(ptempPicData, temp_picdatalen);	//读取图片数据

		//==cpy real pic w h data==
		DWORD j;
		for(j=0;j<bmpInfoHeader.bmiHeader.biHeight;j++)
		{
			memcpy(pPicData+j*bmpInfoHeader.bmiHeader.biWidth,ptempPicData+j*pic_align_w,bmpInfoHeader.bmiHeader.biWidth);
		}

		if(NULL != ptempPicData)
		{
			delete []ptempPicData; 
			ptempPicData = NULL;
		}
		//==end cpy real pic w h data==


		pBmpIndex = pPicData;
		pDlg->picFile.Close();
		
		DWORD i;
		BmpArray[iBmpCnt].IndexLen = dwPicDataLen;
		// 将不同的bmp数据统计出来
		BmpArray[iBmpCnt].RecordIndex[0] = *pBmpIndex;
		pBmpIndex++;
		for(i=1,BmpArray[iBmpCnt].RecordIndexLen=1;i<dwPicDataLen;i++,pBmpIndex++)
		{
			if(CheckIndex(*pBmpIndex,&BmpArray[iBmpCnt].RecordIndex[0],
				BmpArray[iBmpCnt].RecordIndexLen))
			{
				BmpArray[iBmpCnt].RecordIndex[BmpArray[iBmpCnt].RecordIndexLen] = *pBmpIndex;
				BmpArray[iBmpCnt].RecordIndexLen++;
			}
		}
		if(256 == BmpArray[iBmpCnt].RecordIndexLen)
		{
			strTmp.Format(_T("ERROR! The number of palette limit to 256"));
			AfxMessageBox(strTmp);
			pDlg->fileOsdBin.Close();
			pDlg->m_progressOsd.ShowWindow(FALSE);

			if(NULL != BmpArray)
			{
				delete []BmpArray; BmpArray = NULL;
			}
			if(NULL != bmpIndexArray)
			{
				delete []bmpIndexArray; bmpIndexArray = NULL;
			}

			return 3;
		}

		bmpIndexArray[iBmpCnt+1].xwidth  = bmpInfoHeader.bmiHeader.biWidth;
		bmpIndexArray[iBmpCnt+1].yheight = bmpInfoHeader.bmiHeader.biHeight;
		bmpIndexArray[iBmpCnt+1].dataoffset = dwPicDataLen;
		
		// bmp数据与实际显示是倒过来的，要转过来
		unsigned char *pNewData = new unsigned char[dwPicDataLen];
		for (LONG i=0; i<bmpIndexArray[iBmpCnt+1].yheight; i++)
		{
			memcpy(pNewData+i*bmpIndexArray[iBmpCnt+1].xwidth, pPicData+(bmpIndexArray[iBmpCnt+1].yheight-i-1)*bmpIndexArray[iBmpCnt+1].xwidth, bmpIndexArray[iBmpCnt+1].xwidth);
		}
		pDlg->fileOsdBin.Write(pNewData, dwPicDataLen);
		pDlg->fileOsdBin.Flush();

		delete []pPicData; pPicData = NULL;
		delete []pNewData; pNewData = NULL;
		pDlg->m_progressOsd.SetPos(10+70*iBmpCnt/pDlg->picFileCnt);
	}

	pDlg->m_progressOsd.SetPos(80);
	// 在 OSD_source.bin 文件的开始写上图片索引
	pDlg->fileOsdBin.SeekToBegin();
	OSD_SOURCE_INF* pNewBmpIndex = new OSD_SOURCE_INF[pDlg->picFileCnt+1];
	if(NULL == pNewBmpIndex)
	{
		strTmp.Format(_T("mem err"));
		AfxMessageBox(strTmp);
	}
	memset(pNewBmpIndex, 0, (pDlg->picFileCnt+1)*sizeof(OSD_SOURCE_INF));
	pNewBmpIndex[0].dataoffset = 0;
	bmpIndexArray[0].dataoffset = pDlg->picFileCnt*sizeof(OSD_SOURCE_INF);
	for (DWORD j=1; j<=pDlg->picFileCnt; j++)
	{
		pNewBmpIndex[j].xwidth  = bmpIndexArray[j].xwidth;
		pNewBmpIndex[j].yheight = bmpIndexArray[j].yheight;
		pNewBmpIndex[j].dataoffset = pNewBmpIndex[j-1].dataoffset+bmpIndexArray[j-1].dataoffset;
		bmpIndexArray[j-1].dataoffset = pNewBmpIndex[j].dataoffset;
	}
	pDlg->fileOsdBin.Write(&pNewBmpIndex[1], pDlg->picFileCnt*sizeof(OSD_SOURCE_INF));
	delete []pNewBmpIndex; pNewBmpIndex = NULL;


	//处理调色板的数据
	memset(pDlg->m_PaletteResult, 0, 256*4);
	DWORD i,j,n;
	DWORD PaletteTemp;
	pDlg->m_PaletteResultCnt = 0 ;
	for(i=0;i<pDlg->picFileCnt;i++)
	{	

		for(j=0;j<BmpArray[i].RecordIndexLen;j++)
		{
			if(pDlg->m_PaletteResultCnt >= 256)
			{
				break;
			}
			PaletteTemp =BmpArray[i].Palette[BmpArray[i].RecordIndex[j]];
			PaletteTemp = ARGBA2RGBA(PaletteTemp);
			DWORD PaletteTempNum;
			PaletteTempNum = CheckPalette(PaletteTemp,pDlg->m_PaletteResult,pDlg->m_PaletteResultCnt);
			if(pDlg->m_PaletteResultCnt == PaletteTempNum)
			{
				pDlg->m_PaletteResult[pDlg->m_PaletteResultCnt] = PaletteTemp;
				BmpArray[i].NewIndex[j] = (BYTE)pDlg->m_PaletteResultCnt;
				pDlg->m_PaletteResultCnt++;
				pDlg->m_PaletteResultSrcCnt++;
			}
			else
			{
				BmpArray[i].NewIndex[j] = (BYTE)PaletteTempNum;
			}
		}

		if(pDlg->m_PaletteResultCnt >= 256)
		{
			strTmp.Format(_T("ERROR! The number of palette limit to 256"));
			AfxMessageBox(strTmp);
			pDlg->fileOsdBin.Close();
			pDlg->m_progressOsd.ShowWindow(FALSE);
			if(NULL != BmpArray)
			{
				delete []BmpArray; BmpArray = NULL;
			}
			if(NULL != bmpIndexArray)
			{
				delete []bmpIndexArray; bmpIndexArray = NULL;
			}
			return 3;
		}



		pDlg->fileOsdBin.Seek(bmpIndexArray[i].dataoffset, CFile::begin);
		unsigned char *pBmpIndex1 = new unsigned char[BmpArray[i].IndexLen];
		if(NULL == pBmpIndex1)
		{
			strTmp.Format(_T("pBmpIndex1 mem err"));
			AfxMessageBox(strTmp);
		}
		unsigned char *pBmpIndex2;
		pDlg->fileOsdBin.Read(pBmpIndex1, BmpArray[i].IndexLen);
		pBmpIndex2 = pBmpIndex1;
		for(j=0;j<BmpArray[i].IndexLen;j++,pBmpIndex2++)
		{
			for(n=0;n<BmpArray[i].RecordIndexLen;n++)
			{
				if(*pBmpIndex2==BmpArray[i].RecordIndex[n])
				{
					*pBmpIndex2= BmpArray[i].NewIndex[n];
					break;
				}

			}
		}


		pDlg->fileOsdBin.Seek(bmpIndexArray[i].dataoffset , CFile::begin);
		pDlg->fileOsdBin.Write(pBmpIndex1, BmpArray[i].IndexLen);
		delete []pBmpIndex1; pBmpIndex1 = NULL;
		pDlg->m_progressOsd.SetPos(80+10*iBmpCnt/pDlg->picFileCnt);

	}


	pDlg->fileOsdBin.Flush();
	pDlg->fileOsdBin.Close();
	::SendMessage(pDlg->GetSafeHwnd(),UM_WM_OSD_ANALYSIS_OK,0L,0L);
	pDlg->m_progressOsd.ShowWindow(FALSE);
	pDlg->m_progressOsd.SetPos(100);

	if(NULL != BmpArray)
	{
		delete []BmpArray; BmpArray = NULL;
	}
	if(NULL != bmpIndexArray)
	{
		delete []bmpIndexArray; bmpIndexArray = NULL;
	}

	return 0;
}
DWORD COSDEditDlg::AddPaletteList(void)
{
	DWORD i;
	CString strTmp;
	m_listPicPalette.ResetContent();
	for(i=0;i<m_PaletteResultCnt;i++) {
		if(((m_PaletteResult[i]&0xff000000)>>24) != 0xff){	//修改了Alpha参数
			strTmp.Format(_T(" %03d: %02X %02X %02X----------%02X--------* "),i,m_PaletteResult[i]&0x000000ff,(m_PaletteResult[i]&0x0000ff00)>>8,(m_PaletteResult[i]&0x00ff0000)>>16,(m_PaletteResult[i]&0xff000000)>>24);
		}
		else {
			strTmp.Format(_T(" %03d: %02X %02X %02X----------%02X "),i,m_PaletteResult[i]&0x000000ff,(m_PaletteResult[i]&0x0000ff00)>>8,(m_PaletteResult[i]&0x00ff0000)>>16,(m_PaletteResult[i]&0xff000000)>>24);
		}
		m_listPicPalette.AddString(strTmp);
	}
	UpdateData(FALSE);
	return 0;
}
void COSDEditDlg::OnLbnSelchangeListPicPalette()
{
	// TODO: 在此添加控件通知处理程序代码
	CString strTmp;
	DWORD sel;
	sel = m_listPicPalette.GetCurSel();
	//R color
	strTmp.Format(_T("%02X"),m_PaletteResult[sel]&0x000000ff);
	GetDlgItem(IDC_EDIT_PIC_R)->SetWindowText(strTmp);
	//G color
	strTmp.Format(_T("%02X"),(m_PaletteResult[sel]&0x0000ff00)>>8);
	GetDlgItem(IDC_EDIT_PIC_G)->SetWindowText(strTmp);
	//B color
	strTmp.Format(_T("%02X"),(m_PaletteResult[sel]&0x00ff0000)>>16);
	GetDlgItem(IDC_EDIT_PIC_B)->SetWindowText(strTmp);
	//ALPHA color
	strTmp.Format(_T("%02X"),(m_PaletteResult[sel]&0xff000000)>>24);
	GetDlgItem(IDC_EDIT_PIC_ALPHA)->SetWindowText(strTmp);

	
	if(curPosColor != m_PaletteResult[sel]) {
		GetDlgItem(IDC_BUTTON_PIC_MODIFY)->EnableWindow(FALSE);
	}
	else {
		GetDlgItem(IDC_BUTTON_PIC_MODIFY)->EnableWindow(TRUE);
	}
	UpdateData(FALSE);
	DrawColor(IDC_STATIC_PALETTE_COLOR,m_PaletteResult[sel]&0xffffff);
}
BOOL COSDEditDlg::PicColorValidChkInOsdBin(DWORD colorIndex)
{
	BOOL result = FALSE;
	OSD_SOURCE_INF dataSource;
	DWORD i;
	CString strPathTmp;
	strPathTmp.Format(_T("%s\\OSD_source.tmp"),T_current_path);
	fileOsdBin.Open(strPathTmp, CFile::modeReadWrite|CFile::typeBinary);
	DWORD fileSel;
	for(fileSel = 0;fileSel < picFileCnt;fileSel++) {
		fileOsdBin.Seek(fileSel*sizeof(OSD_SOURCE_INF),CFile::begin);
		fileOsdBin.Read(&dataSource,sizeof(OSD_SOURCE_INF));
		fileOsdBin.Seek(dataSource.dataoffset,CFile::begin);

		unsigned char *pBmpIndex1 = new unsigned char[dataSource.xwidth * dataSource.yheight];
		unsigned char *pBmpIndex2;
		fileOsdBin.Read(pBmpIndex1, dataSource.xwidth * dataSource.yheight);
		pBmpIndex2 = pBmpIndex1;
		for(i=0;i<(DWORD)(dataSource.xwidth * dataSource.yheight);i++,pBmpIndex2++)
		{
			if(*pBmpIndex2==colorIndex){
				result = TRUE;
				break;
			}
		}
		delete []pBmpIndex1; pBmpIndex1 = NULL;
	}
	fileOsdBin.Close();
	return result;
}
void COSDEditDlg::PicPaletteModifyOsdBin(int fileSel,DWORD colorIndexSrc,DWORD colorIndexDst)
{
	//更新OSD_source.tmp文件
	OSD_SOURCE_INF dataSource;
	DWORD i;
	int fileBeginSel,fileEndSel;
	CString strPathTmp;
	strPathTmp.Format(_T("%s\\OSD_source.tmp"),T_current_path);
	fileOsdBin.Open(strPathTmp, CFile::modeReadWrite|CFile::typeBinary);
	if(fileSel < MAX_PIC_NUM) {
		fileBeginSel = fileSel;
		fileEndSel = fileSel;
	}
	else {
		fileBeginSel = 0;
		fileEndSel = picFileCnt-1;
	}
	for(fileSel = fileBeginSel;fileSel <= fileEndSel;fileSel++) {
		fileOsdBin.Seek(fileSel*sizeof(OSD_SOURCE_INF),CFile::begin);
		fileOsdBin.Read(&dataSource,sizeof(OSD_SOURCE_INF));
		fileOsdBin.Seek(dataSource.dataoffset,CFile::begin);

		unsigned char *pBmpIndex1 = new unsigned char[dataSource.xwidth * dataSource.yheight];
		unsigned char *pBmpIndex2;
		fileOsdBin.Read(pBmpIndex1, dataSource.xwidth * dataSource.yheight);
		pBmpIndex2 = pBmpIndex1;
		for(i=0;i<(DWORD)(dataSource.xwidth * dataSource.yheight);i++,pBmpIndex2++)
		{
			if(*pBmpIndex2==colorIndexSrc)
				*pBmpIndex2 = (unsigned char)colorIndexDst;
		}
		fileOsdBin.Seek(dataSource.dataoffset,CFile::begin);
		fileOsdBin.Write(pBmpIndex1, dataSource.xwidth * dataSource.yheight);
		delete []pBmpIndex1; pBmpIndex1 = NULL;
	}
	fileOsdBin.Close();
}

void COSDEditDlg::OnBnClickedButtonAllpicModify()
{
	// TODO: 在此添加控件通知处理程序代码
	CString strTmp;
	DWORD alpha;
	m_EditPicPaletteAlpha.GetWindowText(strTmp);
	DWORD len,i;
	len= strTmp.GetLength();
	if(len == 0) {
		strTmp.Format(_T("请输入Alpha值"));
		AfxMessageBox(strTmp);
		return;
	}
	alpha = 0;
	for(i=0;i<len;i++) {
		alpha = alpha<<4;
		if(strTmp[i]>='0' && strTmp[i]<='9')
			alpha += strTmp[i]-'0';
		else if(strTmp[i]>='A' && strTmp[i]<='F')
			alpha += strTmp[i]-'A'+0x0a;
		else if(strTmp[i]>='a' && strTmp[i]<='f')
			alpha += strTmp[i]-'a'+0x0a;
		else {
			strTmp.Format(_T("Alpha值输入错误字符"));
			AfxMessageBox(strTmp);
			return;
		}
	}
	if(alpha > 255) {
		strTmp.Format(_T("Alpha值输入错误字符"));
		AfxMessageBox(strTmp);
		return;
	}
	DWORD sel,selSrc,selDst;
	sel = m_listPicPalette.GetCurSel();
	if(m_PaletteResult[sel] == ((m_PaletteResult[sel]&0x00ffffff)|(alpha<<24))) {	//alpha值未修改
		return;
	}
	for(i=0;i<m_PaletteResultCnt;i++) {		//判断该颜色是否已经存在调色板中
		if(m_PaletteResult[i] == ((m_PaletteResult[sel]&0x00ffffff)|(alpha<<24)))
			break;
	}
	if(i<m_PaletteResultCnt) {	//该颜色已经存在调色板中，删除重复的颜色
		if(i<sel) {	//删除sel位置颜色
			selSrc = sel;
			selDst = i;
		}
		else {		//删除i位置颜色
			selSrc = i;
			selDst = sel;
		}
		m_PaletteResult[selDst] = (m_PaletteResult[sel]&0x00ffffff)|(alpha<<24);
		PicPaletteModifyOsdBin(MAX_PIC_NUM,selSrc,selDst);	//修改osd_source.tmp文件
		for(i=selSrc;i<m_PaletteResultCnt-1;i++) {
			m_PaletteResult[i] = m_PaletteResult[i+1];
			PicPaletteModifyOsdBin(MAX_PIC_NUM,i+1,i);	//修改osd_source.tmp文件
		}
		m_PaletteResultCnt--;
		sel = selDst;
	}	
	else {
		m_PaletteResult[sel] = (m_PaletteResult[sel]&0x00ffffff)|(alpha<<24);
	}
	AddPaletteList();
	m_listPicPalette.SetCurSel(sel);
	
	UpdateData(FALSE);
	DrawColor(IDC_STATIC_PALETTE_COLOR,m_PaletteResult[sel]&0xffffff);
}

void COSDEditDlg::OnBnClickedButtonPicModify()
{
	// TODO: 在此添加控件通知处理程序代码
	CString strTmp;
	DWORD alpha;
	DWORD sel,/*selSrc,*/selDst;
	m_EditPicPaletteAlpha.GetWindowText(strTmp);
	DWORD len,i;
	len= strTmp.GetLength();
	if(len == 0) {
		strTmp.Format(_T("请输入Alpha值"));
		AfxMessageBox(strTmp);
		return;
	}
	alpha = 0;
	for(i=0;i<len;i++) {
		alpha = alpha<<4;
		if(strTmp[i]>='0' && strTmp[i]<='9')
			alpha += strTmp[i]-'0';
		else if(strTmp[i]>='A' && strTmp[i]<='F')
			alpha += strTmp[i]-'A'+0x0a;
		else if(strTmp[i]>='a' && strTmp[i]<='f')
			alpha += strTmp[i]-'a'+0x0a;
		else {
			strTmp.Format(_T("Alpha值输入错误字符"));
			AfxMessageBox(strTmp);
			return;
		}
	}
	if(alpha > 255) {
		strTmp.Format(_T("Alpha值输入错误字符"));
		AfxMessageBox(strTmp);
		return;
	}
	sel = m_listPicPalette.GetCurSel();
	if(m_PaletteResult[sel] == ((m_PaletteResult[sel]&0x00ffffff)|(alpha<<24))) {	//alpha值未修改
		return;
	}
	for(i=0;i<m_PaletteResultCnt;i++) {		//判断该颜色是否已经存在调色板中
		if(m_PaletteResult[i] == ((m_PaletteResult[sel]&0x00ffffff)|(alpha<<24)))
			break;
	}
	if(i<m_PaletteResultCnt) {	//该颜色已经存在调色板中
		selDst = i;
		m_PaletteResult[selDst] = (m_PaletteResult[sel]&0x00ffffff)|(alpha<<24);
		PicPaletteModifyOsdBin(listCurSel,sel,selDst);	//修改osd_source.tmp文件
		if(PicColorValidChkInOsdBin(sel) == FALSE) {	//该颜色未再使用了，删除
			for(i=sel;i<m_PaletteResultCnt-1;i++) {
				m_PaletteResult[i] = m_PaletteResult[i+1];
				PicPaletteModifyOsdBin(MAX_PIC_NUM,i+1,i);	//修改osd_source.tmp文件
			}
			m_PaletteResultCnt--;
		}
	}	
	else {	//该颜色没有存在调色板中，新建一个颜色
		if(m_PaletteCustomCnt + m_PaletteResultCnt >= 256) {
			strTmp.Format(_T("调色板颜色数量超出限制，最大颜色数为256"));
			AfxMessageBox(strTmp);
			return;
		}
		selDst = m_PaletteResultCnt;
		m_PaletteResult[selDst] = (m_PaletteResult[sel]&0x00ffffff)|(alpha<<24);
		PicPaletteModifyOsdBin(listCurSel,sel,selDst);
		m_PaletteResultCnt++;
	}
	
	sel = selDst;
	AddPaletteList();
	m_listPicPalette.SetCurSel(sel);
	
	if(curPosColor != m_PaletteResult[sel]) {
		GetDlgItem(IDC_BUTTON_PIC_MODIFY)->EnableWindow(FALSE);
	}
	else {
		GetDlgItem(IDC_BUTTON_PIC_MODIFY)->EnableWindow(TRUE);
	}

	DrawColor(IDC_STATIC_PALETTE_COLOR,m_PaletteResult[sel]&0xffffff);

	UpdateData(FALSE);
}

void COSDEditDlg::OnBnClickedButtonPicDel()
{
	// TODO: 在此添加控件通知处理程序代码
	CString strTmp;
	DWORD alpha;
	DWORD sel,/*selSrc,*/selDst;
	DWORD i;
	alpha = 0xff;
	sel = m_listPicPalette.GetCurSel();
	if(m_PaletteResult[sel] == ((m_PaletteResult[sel]&0x00ffffff)|(alpha<<24))) {	//alpha值未修改
		return;
	}
	for(i=0;i<m_PaletteResultCnt;i++) {		//判断该颜色是否已经存在调色板中
		if(m_PaletteResult[i] == ((m_PaletteResult[sel]&0x00ffffff)|(alpha<<24)))
			break;
	}
	if(i<m_PaletteResultCnt) {	//该颜色已经存在调色板中
		selDst = i;
		m_PaletteResult[selDst] = (m_PaletteResult[sel]&0x00ffffff)|(alpha<<24);
		PicPaletteModifyOsdBin(listCurSel,sel,selDst);	//修改osd_source.tmp文件
		if(PicColorValidChkInOsdBin(sel) == FALSE) {	//该颜色未再使用了，删除
			for(i=sel;i<m_PaletteResultCnt-1;i++) {
				m_PaletteResult[i] = m_PaletteResult[i+1];
				PicPaletteModifyOsdBin(MAX_PIC_NUM,i+1,i);	//修改osd_source.tmp文件
			}
			m_PaletteResultCnt--;
		}
	}	
	else {	//该颜色没有存在调色板中，新建一个颜色
		if(m_PaletteCustomCnt + m_PaletteResultCnt >= 256) {
			strTmp.Format(_T("调色板颜色数量超出限制，最大颜色数为256"));
			AfxMessageBox(strTmp);
			return;
		}
		selDst = m_PaletteResultCnt;
		m_PaletteResult[selDst] = (m_PaletteResult[sel]&0x00ffffff)|(alpha<<24);
		PicPaletteModifyOsdBin(listCurSel,sel,selDst);
		m_PaletteResultCnt++;
	}

	sel = selDst;
	AddPaletteList();
	m_listPicPalette.SetCurSel(sel);

	if(curPosColor != m_PaletteResult[sel]) {
		GetDlgItem(IDC_BUTTON_PIC_MODIFY)->EnableWindow(FALSE);
	}
	else {
		GetDlgItem(IDC_BUTTON_PIC_MODIFY)->EnableWindow(TRUE);
	}

	DrawColor(IDC_STATIC_PALETTE_COLOR,m_PaletteResult[sel]&0xffffff);

	UpdateData(FALSE);
}

BOOL COSDEditDlg::GetCustomPalette(DWORD &red,DWORD &green,DWORD &blue,DWORD &alpha,CString &comment)
{
	CString strTmp;
	DWORD len,i;
	red = 0; green = 0; blue = 0; alpha = 0;
	//获取R值
	((CEdit*)GetDlgItem(IDC_EDIT_CUSTOM_R))->GetWindowText(strTmp);
	len= strTmp.GetLength();
	if(len == 0) {
		strTmp.Format(_T("请输入Red值"));
		AfxMessageBox(strTmp);
		return FALSE;
	}
	for(i=0;i<len;i++) {
		red = red<<4;
		if(strTmp[i]>='0' && strTmp[i]<='9')
			red += strTmp[i]-'0';
		else if(strTmp[i]>='A' && strTmp[i]<='F')
			red += strTmp[i]-'A'+0x0a;
		else if(strTmp[i]>='a' && strTmp[i]<='f')
			red += strTmp[i]-'a'+0x0a;
		else {
			strTmp.Format(_T("Red值输入错误字符"));
			AfxMessageBox(strTmp);
			return FALSE;
		}
	}
	if(red > 255) {
		strTmp.Format(_T("Red值输入错误字符"));
		AfxMessageBox(strTmp);
		return FALSE;
	}
	//获取G值
	((CEdit*)GetDlgItem(IDC_EDIT_CUSTOM_G))->GetWindowText(strTmp);
	len= strTmp.GetLength();
	if(len == 0) {
		strTmp.Format(_T("请输入Green值"));
		AfxMessageBox(strTmp);
		return FALSE;
	}
	for(i=0;i<len;i++) {
		green = green<<4;
		if(strTmp[i]>='0' && strTmp[i]<='9')
			green += strTmp[i]-'0';
		else if(strTmp[i]>='A' && strTmp[i]<='F')
			green += strTmp[i]-'A'+0x0a;
		else if(strTmp[i]>='a' && strTmp[i]<='f')
			green += strTmp[i]-'a'+0x0a;
		else {
			strTmp.Format(_T("Green值输入错误字符"));
			AfxMessageBox(strTmp);
			return FALSE;
		}
	}
	if(green > 255) {
		strTmp.Format(_T("Green值输入错误字符"));
		AfxMessageBox(strTmp);
		return FALSE;
	}
	//获取B值
	((CEdit*)GetDlgItem(IDC_EDIT_CUSTOM_B))->GetWindowText(strTmp);
	len= strTmp.GetLength();
	if(len == 0) {
		strTmp.Format(_T("请输入Blue值"));
		AfxMessageBox(strTmp);
		return FALSE;
	}
	for(i=0;i<len;i++) {
		blue = blue<<4;
		if(strTmp[i]>='0' && strTmp[i]<='9')
			blue += strTmp[i]-'0';
		else if(strTmp[i]>='A' && strTmp[i]<='F')
			blue += strTmp[i]-'A'+0x0a;
		else if(strTmp[i]>='a' && strTmp[i]<='f')
			blue += strTmp[i]-'a'+0x0a;
		else {
			strTmp.Format(_T("Blue值输入错误字符"));
			AfxMessageBox(strTmp);
			return FALSE;
		}
	}
	if(blue > 255) {
		strTmp.Format(_T("Blue值输入错误字符"));
		AfxMessageBox(strTmp);
		return FALSE;
	}
	//获取ALPHA值
	((CEdit*)GetDlgItem(IDC_EDIT_CUSTOM_ALPHA))->GetWindowText(strTmp);
	len= strTmp.GetLength();
	if(len == 0) {
		strTmp.Format(_T("请输入Alpha值"));
		AfxMessageBox(strTmp);
		return FALSE;
	}
	for(i=0;i<len;i++) {
		alpha = alpha<<4;
		if(strTmp[i]>='0' && strTmp[i]<='9')
			alpha += strTmp[i]-'0';
		else if(strTmp[i]>='A' && strTmp[i]<='F')
			alpha += strTmp[i]-'A'+0x0a;
		else if(strTmp[i]>='a' && strTmp[i]<='f')
			alpha += strTmp[i]-'a'+0x0a;
		else {
			strTmp.Format(_T("Alpha值输入错误字符"));
			AfxMessageBox(strTmp);
			return FALSE;
		}
	}
	if(alpha > 255) {
		strTmp.Format(_T("Alpha值输入错误字符"));
		AfxMessageBox(strTmp);
		return FALSE;
	}
	//获取Comment字符串
	((CEdit*)GetDlgItem(IDC_EDIT_CUSTOM_COMMENT))->GetWindowText(comment);
	if(comment.GetLength() == 0) {
		strTmp.Format(_T("颜色注释为空，请输入颜色注释"));
		AfxMessageBox(strTmp);
		return FALSE;
	}
	for(i=0;i<(DWORD)comment.GetLength();i++) {
		TCHAR x= comment.GetAt(i);
		if((x >= 'a'&&x<='z') || (x >= 'A'&&x<='Z') || (x >= '0'&&x<='9')|| x == '_') {

		}
		else {
			strTmp.Format(_T("颜色注释输入错误字符"));
			AfxMessageBox(strTmp);
			return FALSE;
		}
	}
	return TRUE;
}
void COSDEditDlg::OnBnClickedButtonCustomDel()
{
	// TODO: 在此添加控件通知处理程序代码
	CString strTmp;
	DWORD sel;
	DWORD i;
	if( m_PaletteCustomCnt == 0) {
		GetDlgItem(IDC_BUTTON_CUSTOM_MODIFY)->EnableWindow(FALSE);
		return;
	}
	sel = m_ListCustomPalette.GetCurSel();
	m_PaletteCustom[sel] = 0;
	m_PaletteCustomComment[sel].Format(_T(""));
	for(i=sel+1;i<m_PaletteCustomCnt;i++) {
		m_PaletteCustom[i-1] = m_PaletteCustom[i];
		m_PaletteCustomComment[i-1] = m_PaletteCustomComment[i];
	}
	m_PaletteCustomCnt--;
	m_ListCustomPalette.ResetContent();
	for(i=0;i<m_PaletteCustomCnt;i++) {
		strTmp.Format(_T(" %03d: %02X %02X %02X-------%02X-------%s "),255-i,m_PaletteCustom[i]&0x000000ff,(m_PaletteCustom[i]&0x0000ff00)>>8,
							(m_PaletteCustom[i]&0x00ff0000)>>16,(m_PaletteCustom[i]&0xff000000)>>24,m_PaletteCustomComment[i]);
		m_ListCustomPalette.AddString(strTmp);
		m_ListCustomPalette.SetCurSel(m_PaletteCustomCnt);
	}
	if(m_PaletteCustomCnt != 0) {
		if(sel >= m_PaletteCustomCnt)
			sel = m_PaletteCustomCnt - 1;
		m_ListCustomPalette.SetCurSel(sel);
		DrawColor(IDC_STATIC_CUSTOM_COLOR,m_PaletteCustom[sel]&0xffffff);
		OnLbnSelchangeListPalette();
	}
	else {
		GetDlgItem(IDC_BUTTON_CUSTOM_MODIFY)->EnableWindow(FALSE);
	}
	UpdateData(FALSE);	
	m_PaletteCustomModifyFlag = TRUE;
}

void COSDEditDlg::OnBnClickedButtonCustomAdd()
{
	// TODO: 在此添加控件通知处理程序代码
	CString strTmp;
	DWORD red,green,blue,alpha;
	if(GetCustomPalette(red,green,blue,alpha,strTmp) == FALSE)
	{
		return;
	}
	if(m_PaletteCustomCnt + m_PaletteResultCnt >= 256) {
		strTmp.Format(_T("调色板颜色数量超出限制，最大颜色数为256"));
		AfxMessageBox(strTmp);
		return;
	}
	//判断颜色注释有效性
	DWORD i;
	for(i=0;i<m_PaletteCustomCnt;i++) {
		if(m_PaletteCustom[i] == ((alpha&0xff)<<24) + ((blue&0xff)<<16) + ((green&0xff)<<8) + (red&0xff)) {
			strTmp.Format(_T("颜色出现重复，请修改颜色R、G、B、Alpha"));
			AfxMessageBox(strTmp);
			return;
		}
	}
	for(i=0;i<m_PaletteCustomCnt;i++) {
		if(m_PaletteCustomComment[i] == strTmp) {
			strTmp.Format(_T("颜色注释出现重复，请修改颜色注释"));
			AfxMessageBox(strTmp);
			return;
		}
	}
	
	m_PaletteCustom[m_PaletteCustomCnt] = ((alpha&0xff)<<24) + ((blue&0xff)<<16) + ((green&0xff)<<8) + (red&0xff);
	m_PaletteCustomComment[m_PaletteCustomCnt] = strTmp;
	strTmp.Format(_T(" %03d: %02X %02X %02X-------%02X-------%s "),255-m_PaletteCustomCnt,red,green,blue,alpha,m_PaletteCustomComment[m_PaletteCustomCnt]);
	m_ListCustomPalette.AddString(strTmp);
	m_ListCustomPalette.SetCurSel(m_PaletteCustomCnt);
	DrawColor(IDC_STATIC_CUSTOM_COLOR,m_PaletteCustom[m_PaletteCustomCnt]&0xffffff);
	m_PaletteCustomCnt++;
	GetDlgItem(IDC_BUTTON_CUSTOM_MODIFY)->EnableWindow(TRUE);
	UpdateData(FALSE);
	m_PaletteCustomModifyFlag = TRUE;
}

void COSDEditDlg::OnBnClickedButtonCustomModify()
{
	// TODO: 在此添加控件通知处理程序代码
	CString strTmp;
	DWORD red,green,blue,alpha;
	if(GetCustomPalette(red,green,blue,alpha,strTmp) == FALSE) {
		return;
	}
	DWORD sel;
	sel = m_ListCustomPalette.GetCurSel();
	//判断颜色注释有效性
	DWORD i;
	for(i=0;i<m_PaletteCustomCnt;i++) {
		if(i == sel)
			continue;
		if(m_PaletteCustom[i] == ((alpha&0xff)<<24) + ((blue&0xff)<<16) + ((green&0xff)<<8) + (red&0xff)) {
			strTmp.Format(_T("颜色出现重复，请修改颜色R、G、B、Alpha"));
			AfxMessageBox(strTmp);
			return;
		}
	}
	for(i=0;i<m_PaletteCustomCnt;i++) {
		if(i == sel)
			continue;
		if(m_PaletteCustomComment[i] == strTmp) {
			strTmp.Format(_T("颜色注释出现重复，请修改颜色注释"));
			AfxMessageBox(strTmp);
			return;
		}
	}
	
	m_PaletteCustom[sel] = ((alpha&0xff)<<24) + ((blue&0xff)<<16) + ((green&0xff)<<8) + (red&0xff);
	m_PaletteCustomComment[sel] = strTmp;
	m_ListCustomPalette.DeleteString(sel);
	strTmp.Format(_T(" %03d: %02X %02X %02X-------%02X-------%s "),255-sel,red,green,blue,alpha,m_PaletteCustomComment[sel]);
	m_ListCustomPalette.InsertString(sel,strTmp);
	m_ListCustomPalette.SetCurSel(sel);

	DrawColor(IDC_STATIC_CUSTOM_COLOR,m_PaletteCustom[sel]&0xffffff);

	UpdateData(FALSE);
	m_PaletteCustomModifyFlag = TRUE;
}

void COSDEditDlg::OnLbnSelchangeListPalette()
{
	// TODO: 在此添加控件通知处理程序代码
	CString strTmp;
	DWORD sel;
	sel = m_ListCustomPalette.GetCurSel();
	//R color
	strTmp.Format(_T("%02X"),m_PaletteCustom[sel]&0x000000ff);
	GetDlgItem(IDC_EDIT_CUSTOM_R)->SetWindowText(strTmp);
	//G color
	strTmp.Format(_T("%02X"),(m_PaletteCustom[sel]&0x0000ff00)>>8);
	GetDlgItem(IDC_EDIT_CUSTOM_G)->SetWindowText(strTmp);
	//B color
	strTmp.Format(_T("%02X"),(m_PaletteCustom[sel]&0x00ff0000)>>16);
	GetDlgItem(IDC_EDIT_CUSTOM_B)->SetWindowText(strTmp);
	//ALPHA color
	strTmp.Format(_T("%02X"),(m_PaletteCustom[sel]&0xff000000)>>24);
	GetDlgItem(IDC_EDIT_CUSTOM_ALPHA)->SetWindowText(strTmp);

	//Comment
	GetDlgItem(IDC_EDIT_CUSTOM_COMMENT)->SetWindowText(m_PaletteCustomComment[sel]);

	DrawColor(IDC_STATIC_CUSTOM_COLOR,m_PaletteCustom[sel]&0xffffff);

	UpdateData(FALSE);
}

void COSDEditDlg::OnBnClickedButtonGen()
{
	// TODO: 在此添加控件通知处理程序代码
	CString strTmp;
	CString strNote;
	CString strPath;
	LONG i,j;
	//创建OSD_source.bin文件
	if(m_PaletteResultSrcCnt > 0) {
		strPath.Format(_T("%s\\OSD_source.bin"),T_ouputFileDir);
		CString strPathTmp;
		strPathTmp.Format(_T("%s\\OSD_source.tmp"),T_current_path);
		if(CopyFile(strPathTmp,strPath,FALSE) == FALSE) {
			strTmp.Format(_T("OSD_source.bin创建失败"));
			AfxMessageBox(strTmp);
			return;
		}
		strNote.Format(_T("OSD_source.bin"));
	}

	//创建palette.txt调色板文件
	if(m_PaletteResultCnt + m_PaletteCustomCnt > 0) {
		FILE  *fpout;
		//char *pCharTmp;
		strPath.Format(_T("%s\\palette.txt"),T_ouputFileDir);
		USES_CONVERSION;
		fpout = fopen(T2A(strPath),"wb"); 
		fprintf(fpout,"u32 Tab[256] = \r\n{\r\n");

		for(i= 1;i<=(LONG)m_PaletteResultCnt;i++)
		{
			fprintf(fpout,"0x");
			fprintf(fpout,"%08x",m_PaletteResult[i-1]);
			if(i!=256)
				fprintf(fpout,"%c",',');
			else 
			{
				fprintf(fpout,"\r\n");
				fprintf(fpout,"};");

			}

			if(((i%8)==0)&&(i!=0))
				fprintf(fpout,"\r\n");
		}
		for(;i<=(LONG)(256-m_PaletteCustomCnt);i++)
		{
			fprintf(fpout,"0x00000000");
			if(i!=256)
				fprintf(fpout,"%c",',');
			else 
			{
				fprintf(fpout,"\r\n");
				fprintf(fpout,"};");

			}

			if(((i%8)==0)&&(i!=0))
				fprintf(fpout,"\r\n");
		}
		j=1;
		for(;i<=256;i++,j++)
		{
			fprintf(fpout,"0x");
			fprintf(fpout,"%08x",m_PaletteCustom[m_PaletteCustomCnt-j]);
			if(i!=256)
				fprintf(fpout,"%c",',');
			else 
			{
				fprintf(fpout,"\r\n");
				fprintf(fpout,"};");

			}

			if(((i%8)==0)&&(i!=0))
				fprintf(fpout,"\r\n");
		}
		fclose(fpout);
		if(strNote.GetLength())
			strNote+="、";
		strNote+="palette.txt";
	}
	//创建OSD_source.h文件
	if(m_PaletteResultSrcCnt > 0 || m_PaletteCustomCnt >0) {
		char *pCharTmp;
		CString HeadText = _T("/* !!! Do not manually modify this file in the production stage !!!\r\n * This file is automatically created by OSDEdit.exe,\r\n * and used in the production stage.*/\r\n\r\n#ifndef __OSD_SOURCE_H\r\n#define __OSD_SOURCE_H\r\n\r\n");
		CString TailText = _T("\r\n\r\n#endif\r\n");
		CFile fileOsdHead;
		CString strListName;
		strPath.Format(_T("%s\\OSD_source.h"),T_ouputFileDir);
		fileOsdHead.Open(strPath, CFile::modeCreate|CFile::modeWrite);
		USES_CONVERSION;
		pCharTmp = T2A(HeadText);
		fileOsdHead.Write(pCharTmp,strlen(pCharTmp));
		//写入自定义颜色顺序
		strTmp.Format(_T("//Custom color index information\r\n"));
		pCharTmp = T2A(strTmp);
		fileOsdHead.Write(pCharTmp,strlen(pCharTmp));
		for(i=0;i<(LONG)m_PaletteCustomCnt;i++) {
			if(m_PaletteCustomComment[i].GetLength()) {
				strTmp.Format(_T("#define       PALETTE_%-32s\t0x%02x\r\n"), m_PaletteCustomComment[i].MakeUpper(), 255-i);
				pCharTmp = T2A(strTmp);
				fileOsdHead.Write(pCharTmp,strlen(pCharTmp));
			}
		}
		strTmp.Format(_T("\r\n//OSD Icon index information\r\n"));
		pCharTmp = T2A(strTmp);
		fileOsdHead.Write(pCharTmp,strlen(pCharTmp));
		//写入OSD图标顺序
		for(i=0;i<m_PicListBox.GetCount();i++) {
			m_PicListBox.GetText(i,strListName);
			strTmp.Format(_T("#define       OSD_%-32s\t%i\r\n"), strListName.MakeUpper(), i);
			pCharTmp = T2A(strTmp);
			fileOsdHead.Write(pCharTmp,strlen(pCharTmp));
		}
		pCharTmp = T2A(TailText);
		fileOsdHead.Write(pCharTmp,strlen(pCharTmp));
		if(strNote.GetLength())
			strNote+="、";
		strNote+="OSD_source.h";
	}
	if(strNote.GetLength()) {
		if(m_PaletteCustomModifyFlag) {
			saveConfig();
			m_PaletteCustomModifyFlag = FALSE;
		}
		strNote+="生成成功";
		AfxMessageBox(strNote);
	}
	else {
		strNote+="生成失败";
		AfxMessageBox(strNote);
	}
}

HBRUSH COSDEditDlg::OnCtlColor(CDC* pDC, CWnd* pWnd, UINT nCtlColor)
{
	HBRUSH hbr = CDialog::OnCtlColor(pDC, pWnd, nCtlColor);

	// TODO:  在此更改 DC 的任何属性
#if 0
	if(pWnd-> GetDlgCtrlID()==IDC_STATIC_CUSTOM_COLOR) 
	{ 
		pDC->SetBkColor(0xffff);
	} 
	if(pWnd-> GetDlgCtrlID()==IDC_STATIC_PALETTE_COLOR) 
	{ 
		pDC->SetBkColor(0xffff);
	} 
	if(pWnd-> GetDlgCtrlID()==IDC_STATIC_PREVIEW_COLOR) 
	{ 
		pDC->SetBkColor(0xffff);
		//pDC->SetBkColor(m_PaletteResult[curBmpColorIndex]&0xffffff);
	} 
#endif	

	
	// TODO:  如果默认的不是所需画笔，则返回另一个画笔
	
	return hbr;
}

void COSDEditDlg::DrawColor(int nStaticID,COLORREF color)
{
	//将pStatic指向要显示的地方
	CStatic *pStatic = NULL;
	//根据ID获取Static控件
	pStatic=(CStatic*)GetDlgItem(nStaticID);
	////////////////////////////////////////////////
	/**这一步相当重要，否则无法实现自绘*****/
	////////////////////////////////////////////////
	pStatic->ModifyStyle(0,BS_OWNERDRAW);
	//创建DC
	CClientDC dc(pStatic);

	CRect lRect;
	pStatic->GetClientRect(&lRect);
	CBrush *pBrush = new CBrush(color);
	//CBrush *pBrush = new CBrush(0xff);
	dc.FillRect(&lRect,pBrush);
	delete pBrush;
}

void COSDEditDlg::OnBnClickedButtonOutdir()
{
	// TODO: 在此添加控件通知处理程序代码
	BROWSEINFO bi;

	TCHAR Buffer[MAX_PATH];

	//初始化入口参数bi开始
	bi.hwndOwner = this->m_hWnd; 
	bi.pidlRoot =NULL;//初始化制定的root目录很不容易，
	bi.pszDisplayName = Buffer;//此参数如为NULL则不能显示对话框
	bi.lpszTitle = _T("请选择目录");
	//bi.ulFlags = BIF_BROWSEINCLUDEFILES;//包括文件
	bi.ulFlags = BIF_RETURNONLYFSDIRS |BIF_EDITBOX;//BIF_EDITBOX;//包括文件
	bi.lpfn = NULL;
	bi.iImage=IDR_MAINFRAME;
	if(T_ouputFileDir.GetLength()) {
		bi.lParam = (long)(T_ouputFileDir.GetBuffer(T_ouputFileDir.GetLength()));
		bi.lpfn = BrowseCallbackProc;
	}
	else {
		bi.lParam = NULL;
		bi.lpfn = NULL;
	}
	//初始化入口参数bi结束
	LPITEMIDLIST pIDList = SHBrowseForFolder(&bi);//调用显示选择对话框
	if(pIDList)
	{
		SHGetPathFromIDList(pIDList, Buffer);
		//取得文件夹路径到Buffer里
		T_ouputFileDir = Buffer;//将文件夹路径保存在一个CString对象里
	}
	CoTaskMemFree(pIDList);

	UpdateData(FALSE);
}

void COSDEditDlg::OnClose()
{
	// TODO: 在此添加消息处理程序代码和/或调用默认值
	CString strPathTmp;
	strPathTmp.Format(_T("%s\\OSD_source.tmp"),T_current_path);
	//if(fileOsdBin.m_hFile != CFile::hFileNull) {
	//	fileOsdBin.Close();
	//}
	if(GetFileAttributes(strPathTmp)!=0xFFFFFFFF)
	{
		DeleteFile(strPathTmp);
	}
	//fileOsdBin.Remove(strPathTmp);
	CDialog::OnClose();
}
BOOL COSDEditDlg::bmemcmp(void *dst, void *src, DWORD byte_len)
{
    DWORD i = 0;
    char *dst_u8 = (char*)dst;
    char *src_u8 = (char*)src;
    for(i=0; i<byte_len;)
    {
       if(dst_u8[i] != src_u8[i]) 
       {
          return 0;
       }   
       i++;     
    }
    return 1;
}

BOOL COSDEditDlg::saveConfig(void)
{
	CString strFilePath;
	CString strTmp;
	CFile configFile;
	CFileException ex;	
	char *pCharTmp;
	if(m_PaletteCustomCnt == 0)
		return FALSE;
	strFilePath.Format(_T("%s\\OSDEditConfig.ini"),T_current_path);
	if(!configFile.Open(strFilePath, CFile::modeCreate|CFile::modeWrite, &ex)) {
		return FALSE;
	}
	USES_CONVERSION;
	pCharTmp = T2A(_T(CUSTOM_PALETTE));
	configFile.Write(pCharTmp,strlen(pCharTmp));
	for(DWORD i=0;i<m_PaletteCustomCnt;i++) {
		strTmp.Format(_T("\n%-32s\t=0x%08x"),m_PaletteCustomComment[i].MakeUpper(),m_PaletteCustom[i]);
		pCharTmp = T2A(strTmp);
		configFile.Write(pCharTmp,strlen(pCharTmp));
	}
	configFile.Close();
	return TRUE;	
}

BOOL COSDEditDlg::loadConfig(void)
{
	DWORD len;
	CString strFilePath;
	CString strTmp;
	CFile configFile;
	CFileException ex;	
	strFilePath.Format(_T("%s\\OSDEditConfig.ini"),T_current_path);
	if(!configFile.Open(strFilePath, CFile::modeRead, &ex)) {
		return FALSE;
	}
	len = (DWORD)configFile.GetLength();
	char *buf = new char[len+1];
	configFile.Read(buf,len);
	buf[len] = 0;
	DWORD i,j;
	DWORD val;
	for(i=0;i<len-sizeof(CUSTOM_PALETTE)+1;i++) {
		if(bmemcmp(buf+i,CUSTOM_PALETTE,sizeof(CUSTOM_PALETTE)-1) == 1) {
			break;
		}
	}
	if(i>=len-sizeof(CUSTOM_PALETTE)+1) {
		return FALSE;
	}
	i+=sizeof(CUSTOM_PALETTE)-1;
	while(i<len) {
		//get comment
		if(' ' == buf[i] || '\n' == buf[i] || '\t' == buf[i] || '\r' == buf[i]) {		//忽略空格或换行等符号
			i++;
			continue;
		}
		m_PaletteCustomComment[m_PaletteCustomCnt].Format(_T(""));
		while((buf[i] >= '0' && buf[i] <= '9') || (buf[i]>='a' && buf[i]<='z') || (buf[i]>='A' && buf[i]<='Z') || buf[i]=='_') {
			strTmp.Format(_T("%c"),buf[i]);
			m_PaletteCustomComment[m_PaletteCustomCnt]+=strTmp;
			i++;
			if(i >= len) {
				break;
			}
		}
		//获取颜色值
		while(i<len && (' ' == buf[i] || '\n' == buf[i] || '\t' == buf[i] || '\r' == buf[i])) {		//忽略空格或换行等符号
			i++;
		}
		if(i<len && buf[i] == '=') {
			i++;
			while(i<len && (' ' == buf[i] || '\n' == buf[i] || '\t' == buf[i] || '\r' == buf[i])) {		//忽略空格或换行等符号
				i++;
			}
			if(i<len && buf[i]=='0' && (buf[i+1] == 'x'||buf[i+1] == 'X')) {
				i+=2;
				val = 0;
				if(i+8<=len) {
					for(j=0;j<8;j++) {
						if(buf[i+j] >= 'a'&&buf[i+j]<='z') {
							val<<=4;
							val += buf[i+j]-'a'+0x0a;
						}
						else if(buf[i+j] >= 'A'&&buf[i+j]<='Z') {
							val<<=4;
							val += buf[i+j]-'A'+0x0a;
						}
						else if(buf[i+j] >= '0'&&buf[i+j]<='9')  {
							val<<=4;
							val += buf[i+j]-'0';
						}
						else
							break;
					}
					if(j>=8) {	//数据有效
						m_PaletteCustom[m_PaletteCustomCnt] = val;
						m_PaletteCustomCnt++;
					}
					else{
						break;
					}
				}
				i+=8;
			}
		}
	}
	delete buf;
	configFile.Close();
	if(m_PaletteCustomCnt)
		return TRUE;
	else 
		return FALSE;	
}
LRESULT COSDEditDlg::OnUserOSDAnalysisOKMsg(WPARAM wParam,LPARAM lParam)
{
	CString strTmp;
	AddPaletteList();
	if(picFileCnt) {
		m_PicListBox.SetCurSel(0);
		OnLbnSelchangeListPic();
	}
	else {
		hbmp.Detach();
		Invalidate();
		//设置水平位置显示信息
		strTmp.Format(_T(""));
		GetDlgItem(IDC_HORIZONTAL)->SetWindowText(strTmp);
		//设置垂直位置显示信息
		GetDlgItem(IDC_VERTICAL)->SetWindowText(strTmp);
		//设置颜色值显示
		GetDlgItem(IDC_STATIC_PREVIEW_RGB)->SetWindowText(strTmp);
		GetDlgItem(IDC_EDIT_PIC_R)->SetWindowText(strTmp);
		GetDlgItem(IDC_EDIT_PIC_G)->SetWindowText(strTmp);
		GetDlgItem(IDC_EDIT_PIC_B)->SetWindowText(strTmp);
		GetDlgItem(IDC_EDIT_PIC_ALPHA)->SetWindowText(strTmp);
		GetDlgItem(IDC_BUTTON_PIC_MODIFY)->EnableWindow(FALSE);
		GetDlgItem(IDC_BUTTON_ALLPIC_MODIFY)->EnableWindow(FALSE);
	}
	UpdateData(FALSE);
	//AfxMessageBox(_T("自定义消息"));
	return 0;
}