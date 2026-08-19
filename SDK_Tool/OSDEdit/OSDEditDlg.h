// OSDEditDlg.h : 头文件
//

#pragma once
#include "afxwin.h"
#include "afxcmn.h"

typedef struct  
{
	LONG xwidth;
	LONG yheight;
	DWORD dataoffset;
}OSD_SOURCE_INF;

typedef struct  
{
	DWORD Palette[256];			//调色板内容
	BYTE  RecordIndex[256];		//使用到的颜色的索引
	BYTE  NewIndex[256];		//
	DWORD BmpNum;
	DWORD IndexLen;				//图片数据长度
	DWORD RecordIndexLen;		//使用到的颜色的索引的数量

}BMP_INF;

// COSDEditDlg 对话框
class COSDEditDlg : public CDialog
{
// 构造
public:
	COSDEditDlg(CWnd* pParent = NULL);	// 标准构造函数

// 对话框数据
	enum { IDD = IDD_OSDEDIT_DIALOG };

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
	afx_msg void OnBnClickedButDir();
	CString T_inputFile_path;
	// picture file count
	DWORD picFileCnt;
	BOOL UpdatePicListFromDirChanged(void);
	//static unsigned int WINAPI UpdatePicListFromDirChanged(void * p);
	CListBox m_PicListBox;
	afx_msg void OnLbnSelchangeListPic();
	void loadBMPFile(CString bmpPath);
	CBitmap hbmp;
	void ShowSrcBmp(void);
	//DWORD AnalysisOsdData(void);
	static unsigned int WINAPI AnalysisOsdData(void * p);
	DWORD AddPaletteList(void);
	DWORD picHorizontalPos;
	DWORD picVerticalPos;
//	afx_msg void OnStnClickedPicPreview();
	afx_msg void OnLButtonDown(UINT nFlags, CPoint point);
	afx_msg LRESULT OnUserOSDAnalysisOKMsg(WPARAM wParam,LPARAM lParam);
	BITMAP bmp;
	POINT curPosInPic;
	POINT curPosInRect;
	POINT curInClient;
	CPoint curPointInPic;
	DWORD curPosColor;

	BOOL curClickedValid;
	BYTE curBmpColorIndex;

	int listCurSel;
	
	BITMAPFILEHEADER m_bmpFileHeader;
	BITMAPINFOHEADER m_bmpInfoHeader;
	DWORD m_bmpPalette[256];
	DWORD m_PaletteResult[256];
	DWORD m_PaletteResultCnt;
	DWORD m_PaletteResultSrcCnt;
	DWORD m_PaletteCustom[256];
	CString m_PaletteCustomComment[256];
	DWORD m_PaletteCustomCnt;
	BOOL m_PaletteCustomModifyFlag;
	CFile picFile;
	CFile fileOsdBin;
	CFile filePaletteTxt;
	CListBox m_listPicPalette;
	afx_msg void OnLbnSelchangeListPicPalette();
	afx_msg void OnBnClickedButtonAllpicModify();
	CEdit m_EditPicPaletteAlpha;
	afx_msg void OnBnClickedButtonPicModify();
	afx_msg void OnBnClickedButtonCustomAdd();
	afx_msg void OnBnClickedButtonCustomModify();
	BOOL GetCustomPalette(DWORD &red,DWORD &green,DWORD &blue,DWORD &alpha,CString &comment);
	CListBox m_ListCustomPalette;
	afx_msg void OnLbnSelchangeListPalette();
	afx_msg void OnBnClickedButtonGen();
	afx_msg HBRUSH OnCtlColor(CDC* pDC, CWnd* pWnd, UINT nCtlColor);
	void DrawColor(int nStaticID,COLORREF color);
	afx_msg void OnBnClickedButtonOutdir();
	CString T_ouputFileDir;
	afx_msg void OnClose();
	CString T_current_path;
	BOOL buttonDownIndicate(CPoint point);
	afx_msg void OnBnClickedButtonCustomDel();
	afx_msg void OnBnClickedButtonPicDel();
	void PicPaletteModifyOsdBin(int fileSel,DWORD colorIndexSrc,DWORD colorIndexDst);
	BOOL PicColorValidChkInOsdBin(DWORD colorIndex);
	BOOL bmemcmp(void *dst, void *src, DWORD byte_len);
	BOOL saveConfig(void);
	BOOL loadConfig(void);
	CProgressCtrl m_progressOsd;
};
