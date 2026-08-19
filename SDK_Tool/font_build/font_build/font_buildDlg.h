// font_buildDlg.h : 头文件
//

#pragma once

typedef  struct
{
	UINT unicode;							//unicode值
	UINT fontaddress;						//对应字模的起始地址
}UNICODESTRUCT;

typedef struct 
{
      unsigned char  x;						//字模宽
	  unsigned char  y;						//字模高
	  unsigned short  length;				//字模点阵数据长度
	  UINT  data_baseadd;					//字模点阵数据起始地址
}FONTSTRUCT;


// Cfont_buildDlg 对话框
class Cfont_buildDlg : public CDialog
{
// 构造
public:
	Cfont_buildDlg(CWnd* pParent = NULL);	// 标准构造函数

// 对话框数据
	enum { IDD = IDD_FONT_BUILD_DIALOG };

	protected:
	virtual void DoDataExchange(CDataExchange* pDX);	// DDX/DDV 支持


// 实现
protected:
	HICON m_hIcon;

	// 生成的消息映射函数
	virtual BOOL OnInitDialog();
	afx_msg void OnSysCommand(UINT nID, LPARAM lParam);
	afx_msg void OnPaint();
	afx_msg HCURSOR OnQueryDragIcon();
	DECLARE_MESSAGE_MAP()
public:
	afx_msg void OnBnClickedButton1();
	afx_msg void OnBnClickedButton2();
	afx_msg void OnBnClickedOk();

	//===ticru add====
	afx_msg void DrawToMemory(TCHAR uni,UNICODESTRUCT *pindex,FONTSTRUCT *pfont,int done_i);			//for build font.bin
	afx_msg void DrawAndShowUnicode(TCHAR uni);																	//for show unicode info
	afx_msg void OnEnChangeEdit5();
};
