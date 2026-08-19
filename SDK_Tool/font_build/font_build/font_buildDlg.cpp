// font_buildDlg.cpp : 实现文件
//

#include "stdafx.h"
#include "font_build.h"
#include "font_buildDlg.h"


//#include <atlconv.h>						//ticru add for str change

#ifdef _DEBUG
#define new DEBUG_NEW
#endif



//ticru add

#define   OUTPUT_BIN	_T("\\font.bin")	//输出文件
CString	  T_current_path;					//程序运行的目录
CString	  T_select_file_path;				//选择的文件全路径
CString   T_build_msg;						//显示生成信息
CString   T_build_msg_uni;					//显示生成的UNICO信息
CString   T_build_msg_not_support;			//显示不支持的字符信息
CString   T_errmsg;							//出错信息提示
int		  T_errnum;							//出错数目
LOGFONT	  T_font_lib;						//选择的字库
HFONT	  T_font;							//字库字体
CString   T_font_w;							//查询的UNICODE宽
CString   T_font_h;							//查询的UNICODE高
CString   T_unicode_value;					//查询的UNICODE值
char	  Handle_Unicode[0xffff+1];			//unicode value :0 is not handle, 1 is need handle
int		  unicode_num = 0;					//unicode str num

UNICODESTRUCT *punicode_index = NULL;
FONTSTRUCT	*punicode_font = NULL;
UINT		CurDataStartAddress;			//unicode buf address
CFile		outputfile;


HDC		  T_hMemDC;							
HBITMAP m_hBitmap;
CBitmap m_MyBitmap; 
HBITMAP m_hBmp;
BITMAPINFOHEADER theinfo;
BITMAPINFO info2;
CStatic *m_pWnd;
HDC m_dc;
CDC m_mydc;
//end ticru add


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


// Cfont_buildDlg 对话框




Cfont_buildDlg::Cfont_buildDlg(CWnd* pParent /*=NULL*/)
	: CDialog(Cfont_buildDlg::IDD, pParent)
{
	m_hIcon = AfxGetApp()->LoadIcon(IDR_MAINFRAME);
}

void Cfont_buildDlg::DoDataExchange(CDataExchange* pDX)
{
	CDialog::DoDataExchange(pDX);
	//========ticru add===========
	DDX_Text(pDX, IDC_EDIT1, T_select_file_path);			//关连控件到变量。
	DDX_Text(pDX, IDC_EDIT4, T_build_msg);					//关连控件到变量。
	DDX_Text(pDX, IDC_EDIT2, T_font_w);						//关连控件到变量。
	DDX_Text(pDX, IDC_EDIT3, T_font_h);						//关连控件到变量。
	DDX_Text(pDX, IDC_EDIT5, T_unicode_value);				//关连控件到变量。
	
}

BEGIN_MESSAGE_MAP(Cfont_buildDlg, CDialog)
	ON_WM_SYSCOMMAND()
	ON_WM_PAINT()
	ON_WM_QUERYDRAGICON()
	//}}AFX_MSG_MAP
	ON_BN_CLICKED(IDC_BUTTON1, &Cfont_buildDlg::OnBnClickedButton1)
	ON_BN_CLICKED(IDC_BUTTON2, &Cfont_buildDlg::OnBnClickedButton2)
	ON_BN_CLICKED(IDOK, &Cfont_buildDlg::OnBnClickedOk)
	ON_EN_CHANGE(IDC_EDIT5, &Cfont_buildDlg::OnEnChangeEdit5)
END_MESSAGE_MAP()


// Cfont_buildDlg 消息处理程序

BOOL Cfont_buildDlg::OnInitDialog()
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

	//===============================ticru add==================================
	//set defaut fong lib
	memset(&T_font_lib,0,sizeof(LOGFONT));
	T_font_lib.lfHeight = -21;
	T_font_lib.lfWidth = 0;
	T_font_lib.lfEscapement = 0;
	T_font_lib.lfOrientation = 0;
	T_font_lib.lfWeight = 400;
	T_font_lib.lfItalic = 0;
	T_font_lib.lfUnderline = 0;
	T_font_lib.lfStrikeOut = 0;
	T_font_lib.lfCharSet = 0;
	T_font_lib.lfOutPrecision = 3;
	T_font_lib.lfClipPrecision = 2;
	T_font_lib.lfQuality = 1;
	T_font_lib.lfPitchAndFamily = 34;
	T_font_lib.lfFaceName[0] = 'A';
	T_font_lib.lfFaceName[1] = 'r';
	T_font_lib.lfFaceName[2] = 'i';
	T_font_lib.lfFaceName[3] = 'a';
	T_font_lib.lfFaceName[4] = 'l';
	T_font_lib.lfFaceName[5] = ' ';
	T_font_lib.lfFaceName[6] = 'U';
	T_font_lib.lfFaceName[7] = 'n';
	T_font_lib.lfFaceName[8] = 'i';
	T_font_lib.lfFaceName[9] = 'c';
	T_font_lib.lfFaceName[10] = 'o';
	T_font_lib.lfFaceName[11] = 'd';
	T_font_lib.lfFaceName[12] = 'e';
	T_font_lib.lfFaceName[13] = ' ';
	T_font_lib.lfFaceName[14] = 'M';
	T_font_lib.lfFaceName[15] = 'S';

	//creat font
	T_font=CreateFont(T_font_lib.lfHeight,T_font_lib.lfWidth,T_font_lib.lfEscapement,T_font_lib.lfOrientation,T_font_lib.lfWeight,T_font_lib.lfItalic,T_font_lib.lfUnderline,T_font_lib.lfStrikeOut,
							T_font_lib.lfCharSet,T_font_lib.lfOutPrecision,
							T_font_lib.lfClipPrecision,T_font_lib.lfQuality,
							T_font_lib.lfPitchAndFamily,T_font_lib.lfFaceName);


	//获取当前应用程序路径
//	USES_CONVERSION;
//	T2A();
	WCHAR temp[1024];
	GetCurrentDirectory(MAX_PATH,temp);
	T_current_path.Format(_T("%s"),temp);
	T_select_file_path.Format(_T("%s\\test.txt"),temp);
	T_build_msg = _T("");
	T_build_msg_uni = _T("");
	T_build_msg_not_support = _T("");
	T_font_w = _T("");
	T_font_h = _T("");
	T_unicode_value = _T("31");
	CEdit *pEdit = (CEdit*)GetDlgItem(IDC_EDIT5);		//set unicode value length is 4  (0000~ffff)
	if( NULL != pEdit)
	pEdit->LimitText(4);

	//clear err msg
	T_errmsg = _T("");
	T_errnum = 0;

	//clear unicode handle
	memset(Handle_Unicode,0,sizeof(Handle_Unicode)/sizeof(Handle_Unicode[0]));


	T_hMemDC    = ::CreateCompatibleDC(NULL);
    m_hBitmap   = ::CreateBitmap(320, 240, 1, 1, NULL);
	theinfo.biSize = sizeof(BITMAPINFOHEADER);
	theinfo.biWidth = 320;
	theinfo.biHeight = 240;
	theinfo.biPlanes = 1;
	theinfo.biCompression = 0;
	theinfo.biSizeImage = (320*240)/8;//size;
	theinfo.biXPelsPerMeter = 0;
	theinfo.biYPelsPerMeter = 0;
	theinfo.biClrUsed =0;
	theinfo.biClrImportant = 0;	
	theinfo.biBitCount=1;
	info2.bmiHeader=theinfo;

	m_MyBitmap.LoadBitmap(IDB_BITMAP1);
	m_hBmp = (HBITMAP)(m_MyBitmap);
	//=====for show unicode info====
	m_pWnd=(CStatic*)GetDlgItem(IDC_STATIC_BITMAP);
	m_dc=::GetWindowDC(this->GetSafeHwnd());
	m_mydc.Attach(m_dc);


	::SelectObject(T_hMemDC, m_hBitmap);

	UpdateData(FALSE);
	//================================end ticru add=============================

	return TRUE;  // 除非将焦点设置到控件，否则返回 TRUE
}

void Cfont_buildDlg::OnSysCommand(UINT nID, LPARAM lParam)
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

void Cfont_buildDlg::OnPaint()
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
HCURSOR Cfont_buildDlg::OnQueryDragIcon()
{
	return static_cast<HCURSOR>(m_hIcon);
}


void Cfont_buildDlg::OnBnClickedButton1()
{
	// TODO: 在此添加控件通知处理程序代码

	CFileDialog FileDialog(TRUE,_T("NULL"),NULL,OFN_HIDEREADONLY|OFN_OVERWRITEPROMPT,_T("*.*(*.*)|*.*"));		//任意文件

	if (FileDialog.DoModal() == IDOK)
	{
		T_select_file_path = FileDialog.GetPathName();	//取得打开的文件路径
	}
/*
	CString ttt;
	ttt.Format(T_select_file_path);
	MessageBox(ttt);
*/
	UpdateData(FALSE);
}

void Cfont_buildDlg::OnBnClickedButton2()
{
	// TODO: 在此添加控件通知处理程序代码

	CFontDialog MyFontDialog(&T_font_lib);
	if(MyFontDialog.DoModal()==IDOK)
	{
		MyFontDialog.GetCurrentFont(&T_font_lib);
		if(T_font!=NULL)
		{
			::DeleteObject(T_font);
			T_font=NULL;
		}
		T_font=CreateFont(T_font_lib.lfHeight,T_font_lib.lfWidth,T_font_lib.lfEscapement,T_font_lib.lfOrientation,T_font_lib.lfWeight,T_font_lib.lfItalic,T_font_lib.lfUnderline,T_font_lib.lfStrikeOut,
							T_font_lib.lfCharSet,T_font_lib.lfOutPrecision,
							T_font_lib.lfClipPrecision,T_font_lib.lfQuality,
							T_font_lib.lfPitchAndFamily,T_font_lib.lfFaceName);

	}

	OnEnChangeEdit5();			//show font info
}


void Cfont_buildDlg::DrawToMemory(TCHAR uni,UNICODESTRUCT *pindex,FONTSTRUCT *pfont,int done_i)
{
	char backcol[9600];
	memset(backcol,0,9600);
	SetDIBits(T_hMemDC,m_hBitmap,0,240,backcol,&info2,DIB_RGB_COLORS);

	RECT CurRect;
	memset(&CurRect,0,sizeof(RECT));
	CurRect.bottom=100;
	CurRect.right=100;

	CPoint m_WordPoint;
	m_WordPoint.x=0;
	m_WordPoint.y=0;

	GCP_RESULTS gcp;
	memset(&gcp,0,sizeof(GCP_RESULTS));
	WCHAR   glyphs[12]; 
	memset(glyphs,0,sizeof(WCHAR)*12); 
	gcp.lStructSize   =   sizeof(GCP_RESULTSW); 
	gcp.lpOutString   =   NULL; 
	gcp.lpDx   =   NULL; 
	gcp.lpCaretPos   =   NULL; 
	gcp.lpOrder   =   NULL; 
	gcp.lpGlyphs   =   glyphs; 
	CString str;
	

	if(38 != uni)
	{
		str.Format(_T("%s"),&uni);
		gcp.nGlyphs   =   2; 
		DWORD SS=GetCharacterPlacementW(T_hMemDC,str,str.GetLength(),0, &gcp,GetFontLanguageInfo(T_hMemDC)); 
		m_WordPoint.x=SS & 0x0000FFFF;
		m_WordPoint.y=::DrawText(T_hMemDC,&uni,1,&CurRect,DT_LEFT);
	}
	else
	{
		str.Format(_T("&&"));
		gcp.nGlyphs   =   1; 
		DWORD SS=GetCharacterPlacementW(T_hMemDC,str,2,0, &gcp,GetFontLanguageInfo(T_hMemDC)); 
		m_WordPoint.x=SS & 0x0000FFFF;
		m_WordPoint.y=::DrawText(T_hMemDC,str,2,&CurRect,DT_LEFT);
	}

	
	DWORD databuf_w = m_WordPoint.x;
	DWORD databuf_h = m_WordPoint.y;
	if(databuf_h%8!=0)
		databuf_h=(databuf_h/8+1)*8;

	//======save info======
	pindex->unicode = uni;
	pindex->fontaddress = unicode_num*sizeof(UNICODESTRUCT) + done_i*sizeof(FONTSTRUCT);

	if(m_WordPoint.x >= 255 || m_WordPoint.y >= 255)
	{
		CString temp;
		temp.Format(_T("0x%x,x or y too big err!"),uni);
		T_errmsg = T_errmsg + temp;
		T_errnum++;
		MessageBox(T_errmsg);
	}
	pfont->x = databuf_w;
	pfont->y = databuf_h;
	pfont->length = (databuf_w*databuf_h/8);
	pfont->data_baseadd = CurDataStartAddress;

	//===test
//	CString ttt;
//	ttt.Format(_T("SS = %d,pfont->x=%d,pfont->y = %d,pfont->length=%d"),SS,pfont->x,pfont->y,m_WordPoint.x*m_WordPoint.y/8);
//	MessageBox(ttt);
	//===test


	//====draw buf====
	m_pWnd->SetBitmap(m_hBmp);
	char Value=0,icount=0;
	CPoint point;
	COLORREF col=RGB(0,0,0);

	unsigned char *databuf=new unsigned char[databuf_w*databuf_h/8];
	if(NULL == databuf)
	{
		return;
	}
	memset(databuf,0,databuf_w*databuf_h/8);

	unsigned char tmpbyte=0;
	int bytecount=0;
	int x,y,k;
	for( k=0;k<databuf_h/8;k++)
	{
		for( x=0;x<databuf_w;x++)
		{
			for( y=k*8;y<(k+1)*8;y++)
			{
					COLORREF tmp=GetPixel(T_hMemDC,x,y);
					if(tmp==RGB(0,0,0))
					{
						if(y < m_WordPoint.y)		// 字体内容
						{
							tmpbyte |=1<<icount;
						}
						else						// 超出字体高度的不画
						{
							tmpbyte |=0<<icount;
						}
					}
					icount++;
					if(icount==8)
					{
						databuf[bytecount]=tmpbyte;
						icount=0;
						tmpbyte=0;
						bytecount++;
					}
			}
		}
	}

	outputfile.Write(databuf,databuf_w*databuf_h/8);
	CurDataStartAddress+=databuf_w*databuf_h/8;

	if(NULL != databuf)
	{
		delete [] databuf;
	}

}

void Cfont_buildDlg::DrawAndShowUnicode(TCHAR uni)
{
	char backcol[9600];
	memset(backcol,0,9600);
	
	SetDIBits(T_hMemDC,m_hBitmap,0,240,backcol,&info2,DIB_RGB_COLORS);

	
	RECT CurRect;
	memset(&CurRect,0,sizeof(RECT));
	CurRect.left = 0;
	CurRect.top = 0;
	CurRect.bottom=100;
	CurRect.right=100;
	LPRECT tempRect;
	

	CPoint m_WordPoint;
	m_WordPoint.x=0;
	m_WordPoint.y=0;

	GCP_RESULTS gcp;
	memset(&gcp,0,sizeof(GCP_RESULTS));
	WCHAR   glyphs[12]; 
	memset(glyphs,0,sizeof(WCHAR)*12); 

	gcp.lStructSize   =   sizeof(GCP_RESULTSW); 
	gcp.lpOutString   =   NULL; 
	gcp.lpDx   =   NULL; 
	gcp.lpCaretPos   =   NULL; 
	gcp.lpOrder   =   NULL; 
	gcp.lpGlyphs   =   glyphs; 

	CString str;
	if(38 != uni)
	{
		str.Format(_T("%s"),&uni);
		gcp.nGlyphs   =   2; 
	}
	else
	{
		str.Format(_T("&&"));
		gcp.nGlyphs   =   1; 
	}
	
//	DWORD SS=GetCharacterPlacementW(T_hMemDC,str,str.GetLength(),0, &gcp,GetFontLanguageInfo(T_hMemDC)); 
	DWORD SS=GetCharacterPlacementW(T_hMemDC,str,2,0, &gcp,GetFontLanguageInfo(T_hMemDC)); 
	m_WordPoint.x=SS & 0x0000FFFF;
//	m_WordPoint.y=::DrawText(T_hMemDC,&uni,1,&CurRect,DT_LEFT);
	m_WordPoint.y=::DrawText(T_hMemDC,str,2,&CurRect,DT_LEFT);

	//===test
	/*
	CString ttt;
	ttt.Format(_T("SS = %d,m_WordPoint.x=%d,m_WordPoint.y = %d,m_WordPoint.length=%d"),SS,m_WordPoint.x,m_WordPoint.y,m_WordPoint.x*m_WordPoint.y/8);
	MessageBox(ttt);
	*/
	//===test

	//=====get font show item x,y,w,h======
	CRect rectBtn;
	CWnd * pWnd_pos = GetDlgItem(IDC_STATIC_BITMAP);    // 得到控件的指针
	int window_bar_x = 4;								// 窗口内部起点x
	int window_bar_y = 30;								// 窗口内部起点y

	if ((NULL != pWnd_pos) && IsWindow(pWnd_pos->GetSafeHwnd()))
	{
		pWnd_pos->GetWindowRect(&rectBtn);					// 得到的是在屏幕坐标系下的RECT
		ScreenToClient(rectBtn);
		
/*
		CString ttt;
		ttt.Format(_T("rectBtn left = %d,rectBtn top = %d,rectBtn w = %d ,rectBtn h = %d"),rectBtn.left,rectBtn.top,rectBtn.Width(),rectBtn.Height());
		MessageBox(ttt);
*/
	}
	//====end get font show item x,y,w,h=====

	//=========draw =======
	m_pWnd->SetBitmap(m_hBmp);
	char Value=0,icount=0;
	CPoint point;
	COLORREF col=RGB(0,0,0);

	int x,y;
	//=====clean show======
	for(y = 0;y < rectBtn.Height()-2;y ++)
	{
		for(x = 0;x <rectBtn.Width()-2; x++)
		{
			point.x = x+rectBtn.left+window_bar_x;
			point.y = y+rectBtn.top+window_bar_y;
			m_mydc.SetPixel(point,RGB(236,233,216));
		}
	}
	//====end clean show====


		for( x=0;x<m_WordPoint.x;x++)
		{
			for( y=0;y<m_WordPoint.y;y++)
			{

				COLORREF tmp=GetPixel(T_hMemDC,x,y);
				if(tmp==RGB(0,0,0))
				{

					if((rectBtn.Width() > m_WordPoint.x) && (rectBtn.Height() > m_WordPoint.y))
					{
						point.x=(rectBtn.Width()-m_WordPoint.x)/2+rectBtn.left+x+window_bar_x;
						point.y=(rectBtn.Height()-m_WordPoint.y)/2+rectBtn.top+y+window_bar_y;
						m_mydc.SetPixel(point,col);
					}
					else if((rectBtn.Width() > m_WordPoint.x))
					{
						point.x=(rectBtn.Width()-m_WordPoint.x)/2+rectBtn.left+x+window_bar_x;
						point.y=rectBtn.top+y+window_bar_y;
						m_mydc.SetPixel(point,col);
					}
					else if((rectBtn.Height() > m_WordPoint.y))
					{
						point.x=rectBtn.left+x+window_bar_x;
						point.y=(rectBtn.Height()-m_WordPoint.y)/2+rectBtn.top+y+window_bar_y;
						m_mydc.SetPixel(point,col);
					}
					else
					{
						point.x=rectBtn.left+x+window_bar_x;
						point.y=rectBtn.top+y+window_bar_y;
						m_mydc.SetPixel(point,col);
					}
				}
			}
		}

	T_font_w.Format(_T("%d"),m_WordPoint.x);
	T_font_h.Format(_T("%d"),m_WordPoint.y);

	UpdateData(false);

}

void Cfont_buildDlg::OnBnClickedOk()
{
	// TODO: 在此添加控件通知处理程序代码
  int fontbin_flag = 0;							// 0: not font.bin file ,1 :font.bin file 
  CFile checkfile;
//=======check open file is or not font.bin========
  if(checkfile.Open(T_select_file_path,CFile::modeRead))				//open file
  {

	  fontbin_flag = 1;													//first guess it's is font.bin file
	  checkfile.Seek(0,CFile::begin);
	  checkfile.Read(&unicode_num,4);
		
	  if((unicode_num > 0xffff) || (0 == unicode_num))
	  {
			fontbin_flag = 0;
	  }
	  else
	  {
			int ret_one,ret_two;
			punicode_index = new UNICODESTRUCT[unicode_num];									
			punicode_font = new FONTSTRUCT[unicode_num];
			ret_one = checkfile.Read(punicode_index,unicode_num * sizeof(UNICODESTRUCT));
			ret_two = checkfile.Read(punicode_font,unicode_num * sizeof(FONTSTRUCT));


			if((ret_one != unicode_num * sizeof(UNICODESTRUCT) ) || (ret_two != unicode_num * sizeof(UNICODESTRUCT) ) || (ret_one != ret_two))
			{
				fontbin_flag = 0;
			}
			else
			{
				int i;
				for(i = 0;i < unicode_num;i ++)
				{
					if((punicode_index[i].unicode > 0xffff) || (punicode_index[i].fontaddress != (unicode_num * sizeof(UNICODESTRUCT)+i*sizeof(FONTSTRUCT))))
					{
						fontbin_flag = 0;
						break;
					}
				}
			}
		}
  }
  checkfile.Close();


//=======end check open file ======================

  if(0 == fontbin_flag)			//string file 
  {

		// open string file
		  CStdioFile selectfile;
		  if(NULL == selectfile.Open(T_select_file_path,CFile::modeRead))							
		  {
				CString ttt;
				ttt.Format(_T("Open the string file err!"));
				MessageBox(ttt);
			   return;
		  }

		//============ read string =========
		  int not_support_num = 0;
		  int line_num = 0;
		  T_build_msg.Empty();						//clear msg
		  T_build_msg_uni.Empty();
		  T_build_msg_not_support.Empty();
		  while(1)
		  {
				CString tempstr;
				if(selectfile.ReadString(tempstr))
				{
					int pos;
					CString ttt;
		//			pos = tempstr.Find('=');
		//			if(-1 != pos)												//find the char
					{
						int startpos,endpos;
		//				startpos = tempstr.Find('"',pos);
						startpos = tempstr.Find('"');
						if(-1 != startpos)
						{
							endpos = tempstr.Find('"',startpos+1);				// after startpos
							if(-1 != endpos)									// get string ok
							{
								CString get_str;
								CString get_str_uni;
								//========handle txt string========
								char *szBuf = NULL;
								WCHAR *ptch = NULL;
								int str_len = endpos - startpos;
								szBuf = new char[str_len];

								if(NULL != szBuf)
								{
									int i=0;
									memset(szBuf,0,str_len);
									for ( i = 0; i < str_len-1; i++)			// 取得要处理的字符串
									{
										szBuf[i] = (char)tempstr.GetAt(startpos+1+i);
									}
									szBuf[i] = '\0';								//结束符。否则会在末尾产生乱码。

									int nLen = MultiByteToWideChar(CP_ACP, 0, szBuf, -1, NULL, 0);		//获得需要的宽字符字节数
									if(nLen > 0)
									{
										ptch = new WCHAR[nLen];
										memset(ptch, '\0', nLen);
										MultiByteToWideChar(CP_UTF8, 0, szBuf, -1, ptch, nLen);			//设置编码类型 utf8
									}

									//========set show build msg=======

									line_num++;
									CString cs_msg;
									get_str.Format(_T("%d(String:%s"), line_num,ptch);
									get_str_uni.Format(_T("%d(Unico:"),line_num);
									for(i = 0;i < nLen;i++)
									{
										if(0 == i)
										{
											cs_msg.Format(_T("0x%x"),ptch[i]);
										}
										else
										{
											cs_msg.Format(_T(",0x%x"),ptch[i]);
										}
										if(0 == ptch[i])
										{
											break;		//line end
										}
										get_str_uni = get_str_uni + cs_msg;

										//======get unicode=======
										if((0 <= ptch[i]) && (ptch[i] < 0xffff))
										{
											Handle_Unicode[ptch[i]] = 1;										//get the handle unicode
										}
										else
										{
											CString errmsg;
											errmsg.Format(_T("unicode value err:%x\r\n"),ptch[i]);				//
											T_errmsg = T_errmsg + errmsg;
											T_errnum++;
										}
										//======end get unicode===

									}
									cs_msg.Format(_T(")\r\n"));
									get_str = get_str + cs_msg;
									get_str_uni = get_str_uni + cs_msg;
									//=======end set show build msg=====

									if(NULL != ptch)
									 delete [] ptch;
									 ptch = NULL;
									 
									if(NULL != szBuf)
									 delete []szBuf;
									 szBuf = NULL;
								}
								//=======end handle txt string=======
								T_build_msg = T_build_msg + get_str;
								//T_build_msg_uni = T_build_msg_uni + get_str_uni;

							}
						}
					}

				}
				else
				{
					//finish
					break;
				}
		  }
		  selectfile.Close();
		//============end read string ========

//======add china test======
/*
//		Item114=4E00-9FBF
		int n;
		for(n = 0x4E00; n<= 0x9fbf; n++)
		{
			Handle_Unicode[n] = 1;
		}
*/
//======end test=====


		  //==========handle unicode =========
		  {
				CString outputpath;
				int i;
				unicode_num = 0;
				Handle_Unicode[0x25a1] = 1;						//add spac unicode char "口" ,SDK need it 
				for(i = 0;i < sizeof(Handle_Unicode)/sizeof(Handle_Unicode[0]);i++)				// get the unicode char num
				{
					if(1 == Handle_Unicode[i])
					{
						unicode_num++;
					}
				}

				outputpath = T_current_path + OUTPUT_BIN;
				if(outputfile.Open(outputpath,CFile::modeCreate|CFile::modeWrite))				//create output file
				{
					int i;
					int havedone_i = 0;					//处理完成
					punicode_index = new UNICODESTRUCT[unicode_num];									
					punicode_font = new FONTSTRUCT[unicode_num];
					if(NULL != punicode_index)
					{
						memset(punicode_index,0,unicode_num * sizeof(UNICODESTRUCT));
					}
					else
					{
						CString ttt;
						ttt.Format(_T("punicode_index mem not enough!"));
						MessageBox(ttt);
					}
					if(NULL != punicode_font)
					{
						memset(punicode_font,0,unicode_num * sizeof(FONTSTRUCT));
					}
					else
					{
						CString ttt;
						ttt.Format(_T("punicode_font mem not enough!"));
						MessageBox(ttt);
					}


					outputfile.Write(&unicode_num,4);		//write all unicode num
					outputfile.Seek(4 + unicode_num * sizeof(UNICODESTRUCT) + unicode_num * sizeof(FONTSTRUCT),CFile::begin);			//seek to the char buffer

					::SelectObject(T_hMemDC,T_font);
					CurDataStartAddress = unicode_num * sizeof(UNICODESTRUCT) + unicode_num * sizeof(FONTSTRUCT);
					for(i = 0;i< sizeof(Handle_Unicode)/sizeof(Handle_Unicode[0]);i++)
					{
						if(1 == Handle_Unicode[i])			//draw unicode buffer
						{
							DrawToMemory(i,&punicode_index[havedone_i],&punicode_font[havedone_i],havedone_i);							//draw and write char buffer
							havedone_i++;
						}
					}

					//write unicode
					outputfile.Seek(4 ,CFile::begin);											
					outputfile.Write(punicode_index,unicode_num * sizeof(UNICODESTRUCT));
					outputfile.Write(punicode_font,unicode_num * sizeof(FONTSTRUCT));

		//			CString ttt;
		//			ttt.Format(_T("index size = %d,font size = %d,havedone_i = %d"),unicode_num * sizeof(UNICODESTRUCT),unicode_num * sizeof(FONTSTRUCT),havedone_i);
		//			MessageBox(ttt);

					//====check not support char=====
					CString temp;

					temp.Format(_T("\r\n===font not support unicode char===\r\n"));
					T_build_msg_not_support = T_build_msg_not_support + temp;
					for(i = 0;i < havedone_i;i++)
					{
						if(0 == punicode_font[i].length)
						{
							not_support_num++;
							temp.Format(_T("unicode:0x%x\r\n"),punicode_index[i].unicode);
							T_build_msg_not_support = T_build_msg_not_support + temp;

						}
					}
					temp.Format(_T("not support num:%d\r\n"),not_support_num);
					T_build_msg_not_support = T_build_msg_not_support + temp;


					if(NULL != punicode_index)
					{
						delete [] punicode_index;
					}
					if(NULL != punicode_font)
					{
						delete [] punicode_font;
					}

					outputfile.Close();
				}
				else
				{
					CString ttt;
					ttt.Format(_T("create output file err"));
					MessageBox(ttt);
				}

		  }
		  //==========end handle unicode======

			/*
				CString ttt;
				ttt.Format(_T("lfHeight=%d,lfWidth=%d,lfEscapement=%d,lfOrientation=%d,lfWeight=%d,lfItalic=%d,lfUnderline=%d,lfStrikeOut=%d,lfCharSet=%d,lfOutPrecision=%d,lfClipPrecision=%d,lfQuality=%d,lfPitchAndFamily=%d,lfFaceName=%s"),
							T_font_lib.lfHeight,T_font_lib.lfWidth,T_font_lib.lfEscapement,T_font_lib.lfOrientation,T_font_lib.lfWeight,T_font_lib.lfItalic,
							T_font_lib.lfUnderline,T_font_lib.lfStrikeOut,T_font_lib.lfCharSet,T_font_lib.lfOutPrecision,T_font_lib.lfClipPrecision,
							T_font_lib.lfQuality,T_font_lib.lfPitchAndFamily,T_font_lib.lfFaceName);
				MessageBox(ttt);
			*/

		  //==========handle show msg=========
			T_build_msg = T_build_msg + T_build_msg_uni;
			T_build_msg = T_build_msg + T_build_msg_not_support;
			T_errmsg.Format(_T("\r\n======Build success======\r\nErr:%d"),T_errnum);
			T_build_msg = T_build_msg + T_errmsg;

			if(0 != not_support_num)
			{
				AfxMessageBox(_T("some unicodes are not support ,please select other font!"));
			}
  }
  else							//font.bin file 
  {
		  //==========handle unicode =========
			int not_support_num = 0;
			UINT databuf_len = 0;
	  		CString outputpath;
			outputpath = T_current_path + OUTPUT_BIN;

			if(outputfile.Open(outputpath,CFile::modeReadWrite))				//create output file
			{
				int i,ret;
				ret = outputfile.Seek(4 + unicode_num * sizeof(UNICODESTRUCT) + unicode_num * sizeof(FONTSTRUCT),CFile::begin);			//seek to the char buffer
				//=====read font buffer to memory=======
				for(i = 0;i < unicode_num;i++)
				{
					databuf_len += punicode_font[i].length;
				}

				unsigned char *databuf=new unsigned char[databuf_len];
				if(NULL == databuf)
				{
					CString ttt;
					ttt.Format(_T("not enough memory err !!"));
					MessageBox(ttt);
					return;
				}
				memset(databuf,0,databuf_len);
				ret = outputfile.Read(databuf,databuf_len);
				if(ret != databuf_len)
				{
					CString ttt;
					ttt.Format(_T("read font.bin file err !!"));
					MessageBox(ttt);
				}
				//=====end read font buffer to memory===

				outputfile.Seek(4 + unicode_num * sizeof(UNICODESTRUCT) + unicode_num * sizeof(FONTSTRUCT),CFile::begin);			//seek to the char buffer
				::SelectObject(T_hMemDC,T_font);
				CurDataStartAddress = unicode_num * sizeof(UNICODESTRUCT) + unicode_num * sizeof(FONTSTRUCT);
				unsigned char *temp_databuf = databuf;
				for(i = 0;i< unicode_num;i++)
				{
					if(0 == punicode_font[i].length)			//draw unicode buffer,add to output file
					{
						DrawToMemory(punicode_index[i].unicode,&punicode_index[i],&punicode_font[i],i);							//draw and write char buffer
					}
					else										//writeback to output file 
					{
						outputfile.Write(temp_databuf,punicode_font[i].length);
						temp_databuf += punicode_font[i].length;	//point to next font buffer
						punicode_font[i].data_baseadd = CurDataStartAddress;
						CurDataStartAddress += punicode_font[i].length;
					}
				}

				//write unicode
				outputfile.Seek(0,CFile::begin);	//seek to start
				outputfile.Write(&unicode_num,4);	//write all unicode num
				outputfile.Write(punicode_index,unicode_num * sizeof(UNICODESTRUCT));
				outputfile.Write(punicode_font,unicode_num * sizeof(FONTSTRUCT));


				//====check not support char=====
				CString temp;
				T_build_msg.Empty();
				T_build_msg_uni.Empty();
				T_build_msg_not_support.Empty();

				temp.Format(_T("\r\n===font not support unicode char===\r\n"));
				T_build_msg_not_support = T_build_msg_not_support + temp;
				for(i = 0;i < unicode_num;i++)
				{
					if(0 == punicode_font[i].length)
					{
							not_support_num++;
							temp.Format(_T("unicode:0x%x\r\n"),punicode_index[i].unicode);
							T_build_msg_not_support = T_build_msg_not_support + temp;

					}
				}
				temp.Format(_T("not support num:%d\r\n"),not_support_num);
				T_build_msg_not_support = T_build_msg_not_support + temp;

				if(NULL != databuf)
				{
					delete [] databuf;
				}

				outputfile.Close();
			}
			else
			{
					CString ttt;
					ttt.Format(_T("create output file err"));
					MessageBox(ttt);
			}

		  //==========end handle unicode======

			/*
				CString ttt;
				ttt.Format(_T("lfHeight=%d,lfWidth=%d,lfEscapement=%d,lfOrientation=%d,lfWeight=%d,lfItalic=%d,lfUnderline=%d,lfStrikeOut=%d,lfCharSet=%d,lfOutPrecision=%d,lfClipPrecision=%d,lfQuality=%d,lfPitchAndFamily=%d,lfFaceName=%s"),
							T_font_lib.lfHeight,T_font_lib.lfWidth,T_font_lib.lfEscapement,T_font_lib.lfOrientation,T_font_lib.lfWeight,T_font_lib.lfItalic,
							T_font_lib.lfUnderline,T_font_lib.lfStrikeOut,T_font_lib.lfCharSet,T_font_lib.lfOutPrecision,T_font_lib.lfClipPrecision,
							T_font_lib.lfQuality,T_font_lib.lfPitchAndFamily,T_font_lib.lfFaceName);
				MessageBox(ttt);
			*/

		  //==========handle show msg=========
			T_build_msg = T_build_msg + T_build_msg_uni;
			T_build_msg = T_build_msg + T_build_msg_not_support;
			T_errmsg.Format(_T("\r\n======Build success======\r\nErr:%d"),T_errnum);
			T_build_msg = T_build_msg + T_errmsg;

			if(0 != not_support_num)
			{
				AfxMessageBox(_T("some unicodes are not support ,please select other font!"));
			}
  }

	UpdateData(FALSE);
	
}

void Cfont_buildDlg::OnEnChangeEdit5()
{
	// TODO:  如果该控件是 RICHEDIT 控件，则它将不会
	// 发送该通知，除非重写 CDialog::OnInitDialog()
	// 函数并调用 CRichEditCtrl().SetEventMask()，
	// 同时将 ENM_CHANGE 标志“或”运算到掩码中。

	// TODO:  在此添加控件通知处理程序代码
	int i;
	UpdateData(true);
	int length=T_unicode_value.GetLength();
	if(length>4)
	{
		AfxMessageBox(_T("Range:0000-FFFF"));
		T_unicode_value=T_unicode_value.Left(4);
		UpdateData(false);
		return;
	}
	T_unicode_value.MakeLower();
	int value=0;
	int icount=0;
	for(i=length-1;i>=0;i--)
	{
		TCHAR tmp=T_unicode_value.GetAt(i);
		int  tmpvalue=0;
		if((tmp>'f')||(tmp<0))
		{
			AfxMessageBox(_T("Range:0000-FFFF"));
			T_unicode_value=T_unicode_value.Left(i);
			UpdateData(false);
		}
		else
		{
			if(tmp>='a')
			{
				tmpvalue=(tmp-'a')+10;
			}
			else
				tmpvalue=_wtoi(&tmp);
		}
		value+=tmpvalue<<(icount*4);
		icount++;
	}

	::SelectObject(T_hMemDC,T_font);
	DrawAndShowUnicode(value);

}
