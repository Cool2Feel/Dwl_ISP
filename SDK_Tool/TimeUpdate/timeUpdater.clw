; CLW file contains information for the MFC ClassWizard

[General Info]
Version=1
LastClass=CTimeUpdaterDlg
LastTemplate=CDialog
NewFileInclude1=#include "stdafx.h"
NewFileInclude2=#include "timeUpdater.h"

ClassCount=3
Class1=CTimeUpdaterApp
Class2=CTimeUpdaterDlg
Class3=CAboutDlg

ResourceCount=3
Resource1=IDD_ABOUTBOX (English (U.S.))
Resource2=IDR_MAINFRAME
Resource3=IDD_TIMEUPDATER_DIALOG (English (U.S.))

[CLS:CTimeUpdaterApp]
Type=0
HeaderFile=timeUpdater.h
ImplementationFile=timeUpdater.cpp
Filter=N

[CLS:CTimeUpdaterDlg]
Type=0
HeaderFile=timeUpdaterDlg.h
ImplementationFile=timeUpdaterDlg.cpp
Filter=D
BaseClass=CDialog
VirtualFilter=dWC
LastObject=IDC_DATETIMEPICKER

[CLS:CAboutDlg]
Type=0
HeaderFile=timeUpdaterDlg.h
ImplementationFile=timeUpdaterDlg.cpp
Filter=D

[DLG:IDD_TIMEUPDATER_DIALOG (English (U.S.))]
Type=1
Class=CTimeUpdaterDlg
ControlCount=4
Control1=IDOK,button,1342242817
Control2=IDC_STATICTime,static,1342312961
Control3=IDC_STATICSTATUS,static,1342312961
Control4=IDC_STATIC,button,1342177287

[DLG:IDD_ABOUTBOX (English (U.S.))]
Type=1
Class=CAboutDlg
ControlCount=4
Control1=IDC_STATIC,static,1342177283
Control2=IDC_STATIC,static,1342308480
Control3=IDC_STATIC,static,1342308352
Control4=IDOK,button,1342373889

