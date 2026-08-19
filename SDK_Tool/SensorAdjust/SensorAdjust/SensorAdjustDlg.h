// SensorAdjustDlg.h : 头文件
//

#pragma once
#include "afxwin.h"


// CSensorAdjustDlg 对话框
class CSensorAdjustDlg : public CDialog
{
// 构造
public:
	CSensorAdjustDlg(CWnd* pParent = NULL);	// 标准构造函数

// 对话框数据
	enum { IDD = IDD_SENSORADJUST_DIALOG };

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
	afx_msg void OnEnChangeAddr();
	afx_msg void OnEnChangeValue();

public:
	CButton addr_16bit;
	CButton value_16bit;
	CEdit m_connectStatus;
	CListBox listbox;
	HANDLE m_hDevHandle;						//device
	BOOL GetDisksProperty(HANDLE hDevice, PSTORAGE_DEVICE_DESCRIPTOR pDevDesc);
	HANDLE OpenTheDrv(char drv);

	BOOL ReadFromScsi(
								int    cdbLen,
								void  *cdb,
								int    dataLen,
								BYTE  *data//char  *data
								);
	BOOL WriteToScsi(	int    cdbLen,
								void  *cdb,
								int    dataLen,
								BYTE  *data//char  *data
								);
	BOOL ReadReg(UINT regAddr,UINT *RegValue);
	BOOL WriteReg(UINT regAddr,UINT RegValue);
	UINT hexStr2Value(CString str);

	afx_msg void OnBnClickedButton2();
	afx_msg void OnBnClickedButton3();
	afx_msg void OnBnClicked_addr();
	afx_msg void OnBnClicked_value();
	
	afx_msg void OnLbnSelchange();
	afx_msg void OnBnClickedButton4();
	afx_msg void OnBnClicked_Save();
};
