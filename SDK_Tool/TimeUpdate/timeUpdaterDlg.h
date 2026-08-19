// timeUpdaterDlg.h : header file
//

#if !defined(AFX_TIMEUPDATERDLG_H__2527293B_A447_4E57_B9AB_6C4DEA81BBEA__INCLUDED_)
#define AFX_TIMEUPDATERDLG_H__2527293B_A447_4E57_B9AB_6C4DEA81BBEA__INCLUDED_

#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000

/////////////////////////////////////////////////////////////////////////////
// CTimeUpdaterDlg dialog

class CTimeUpdaterDlg : public CDialog
{
// Construction
public:
	CTimeUpdaterDlg(CWnd* pParent = NULL);	// standard constructor

// Dialog Data
	//{{AFX_DATA(CTimeUpdaterDlg)
	enum { IDD = IDD_TIMEUPDATER_DIALOG };
	CStatic	m_status;
	CStatic	m_staticTime;
	//}}AFX_DATA

	// ClassWizard generated virtual function overrides
	//{{AFX_VIRTUAL(CTimeUpdaterDlg)
	protected:
	virtual void DoDataExchange(CDataExchange* pDX);	// DDX/DDV support
	virtual LRESULT WindowProc(UINT message, WPARAM wParam, LPARAM lParam);
	//}}AFX_VIRTUAL

// Implementation
protected:
	HICON m_hIcon;

	// Generated message map functions
	//{{AFX_MSG(CTimeUpdaterDlg)
	virtual BOOL OnInitDialog();
	afx_msg void OnSysCommand(UINT nID, LPARAM lParam);
	afx_msg void OnPaint();
	afx_msg HCURSOR OnQueryDragIcon();
	afx_msg void OnTimer(UINT nIDEvent);
	//}}AFX_MSG
	DECLARE_MESSAGE_MAP()
private:
	void UpdateDeviceTime();
	DWORD dateTime2Sec();
	HANDLE OpenTheDrv(int phDrv);
	BOOL GetDisksProperty(HANDLE hDevice, PSTORAGE_DEVICE_DESCRIPTOR pDevDesc);
	BOOL ReadFromScsi(HANDLE devHandle,int    cdbLen,void  *cdb,int    dataLen,BYTE  *data);
	void DisplayTime();
	BYTE cdb16[16];
	UINT m_timer;
public:
	afx_msg void OnBnClickedOk();
};

//{{AFX_INSERT_LOCATION}}
// Microsoft Visual C++ will insert additional declarations immediately before the previous line.

#endif // !defined(AFX_TIMEUPDATERDLG_H__2527293B_A447_4E57_B9AB_6C4DEA81BBEA__INCLUDED_)
