// procamp.cpp : USB 摄像头视频属性控制 (Proc Amp) 实现
//
// 说明：本项目 stdafx.h 定义了 WIN32_LEAN_AND_MEAN，windows.h 不会包含 ole/objbase/
// dshow 等头文件，且 objbase.h/dshow.h 在本工程配置下无法完整编译。因此这里与工程
// 一贯风格一致，自包含地手动声明所需的最小 SetupAPI 与 Kernel Streaming(KS) 结构，
// 仅依赖 windows.h + setupapi.lib。（本文件已在 Uvc.vcxproj 中单独开启 /utf-8 编译。）
//
// 背景：某些精简版 Windows（如 IoT Enterprise LTSC）会裁剪 DirectShow 组件——
// CLSID_SystemDeviceEnum(devenum.dll) 的 COM 类不可用，MF 设备枚举(MFEnumDeviceSources)
// 也未导出。此时「CoCreateInstance + CreateClassEnumerator」的经典枚举路径必然失败
// （0x80040154/0x80040111），但摄像头在 PnP / KS 层完全正常（KSCATEGORY_VIDEO_CAMERA）。
// 因此这里改走底层 KS 属性通道：
//   1) SetupAPI 枚举 KSCATEGORY_VIDEO_CAMERA 设备接口，按 FriendlyName 匹配设备；
//   2) CreateFile 打开设备句柄；
//   3) 通过 IOCTL_KS_PROPERTY 发送 BASICSUPPORT/SET，在 filter 级别读写
//      KSPROPSETID_VIDCAP_VIDEOPROCAMP 的各属性（亮度/对比度/增益等，属性 ID 与
//      uvc.h ProcAmpPropertyId 一一对应）。
// 该通道不依赖 DirectShow/MF，任何 Windows 版本均可使用。
// 注意：部分 UVC 驱动在 IoT LTSC 上不支持 KSPROPERTY_TYPE_GET（返回 122 缓冲区不足），
// 因此当前值由内存缓存跟踪，写操作直接用 SET。
//
// 线程模型：所有设备句柄/缓存只在一条工作线程上被访问；导出的 C 函数把一次"命令"
// 写入全局槽、通知工作线程执行并等待结果，天然串行化，避免回调线程与 UI 线程并发。

#include "stdafx.h"
#include "uvc.h"

#include <string>
#include <vector>
#include <algorithm>
#include <wchar.h>
#include <stdio.h>
#include <string.h>
#include <stdarg.h>

#include <windows.h>

#pragma comment(lib, "setupapi.lib")
#pragma comment(lib, "ole32.lib")

// ===================== 手动声明最小 SetupAPI 依赖 =====================

#define DIGCF_PRESENT          0x00000002
#define DIGCF_DEVICEINTERFACE  0x00000010
#define SPDRP_DEVICEDESC       0x00000000
#define SPDRP_FRIENDLYNAME     0x0000000C

typedef struct _SP_DEVINFO_DATA {
    DWORD      cbSize;
    GUID       ClassGuid;
    DWORD      DevInst;
    ULONG_PTR  Reserved;
} SP_DEVINFO_DATA, *PSP_DEVINFO_DATA;

typedef struct _SP_DEVICE_INTERFACE_DATA {
    DWORD      cbSize;
    GUID       InterfaceClassGuid;
    DWORD      Flags;
    ULONG_PTR  Reserved;
} SP_DEVICE_INTERFACE_DATA, *PSP_DEVICE_INTERFACE_DATA;

typedef struct _SP_DEVICE_INTERFACE_DETAIL_DATA_W {
    DWORD cbSize;
    WCHAR DevicePath[ANYSIZE_ARRAY];
} SP_DEVICE_INTERFACE_DETAIL_DATA_W, *PSP_DEVICE_INTERFACE_DETAIL_DATA_W;

extern "C" HANDLE __stdcall SetupDiGetClassDevsW(const GUID* ClassGuid, PCWSTR Enumerator,
                                                 HWND hwndParent, DWORD Flags);
extern "C" BOOL __stdcall SetupDiEnumDeviceInterfaces(HANDLE DeviceInfoSet,
                                                      PSP_DEVINFO_DATA DeviceInfoData,
                                                      const GUID* InterfaceClassGuid,
                                                      DWORD MemberIndex,
                                                      PSP_DEVICE_INTERFACE_DATA DeviceInterfaceData);
extern "C" BOOL __stdcall SetupDiEnumDeviceInfo(HANDLE DeviceInfoSet,
                                                DWORD MemberIndex,
                                                PSP_DEVINFO_DATA DeviceInfoData);
extern "C" BOOL __stdcall SetupDiGetDeviceInterfaceDetailW(HANDLE DeviceInfoSet,
                                                           PSP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
                                                           PSP_DEVICE_INTERFACE_DETAIL_DATA_W DeviceInterfaceDetailData,
                                                           DWORD DeviceInterfaceDetailDataSize,
                                                           PDWORD RequiredSize,
                                                           PSP_DEVINFO_DATA DeviceInfoData);
extern "C" BOOL __stdcall SetupDiGetDeviceRegistryPropertyW(HANDLE DeviceInfoSet,
                                                            PSP_DEVINFO_DATA DeviceInfoData,
                                                            DWORD Property,
                                                            PDWORD PropertyRegDataType,
                                                            PBYTE PropertyBuffer,
                                                            DWORD PropertyBufferSize,
                                                            PDWORD RequiredSize);
extern "C" BOOL __stdcall SetupDiDestroyDeviceInfoList(HANDLE DeviceInfoSet);

// ===================== 手动声明最小 Kernel Streaming(KS) 依赖 =====================

// 原定义: #define IOCTL_KS_PROPERTY 0x002F0003UL (METHOD_NEITHER)
// 本文件仍以 METHOD_NEITHER (0x002F0003) 为主通道；部分精简驱动仅支持
// METHOD_BUFFERED (0x002F0000)，由 KsIoctlDirect 失败时自动降级重试：
//   CTL_CODE(FILE_DEVICE_KS, 0x00, METHOD_BUFFERED, FILE_ANY_ACCESS)
//   = (0x2F << 16) | (0 << 14) | (0x00 << 2) | 0x0 = 0x002F0000
#define IOCTL_KS_PROPERTY 0x002F0003UL

// 部分精简驱动仅支持 METHOD_BUFFERED（0x002F0000）而非 METHOD_NEITHER（0x002F0003）
#define IOCTL_KS_PROPERTY_BUFFERED 0x002F0000UL

#define KSPROPERTY_TYPE_GET         0x00000001L
#define KSPROPERTY_TYPE_SET         0x00000002L
#define KSPROPERTY_TYPE_BASICSUPPORT 0x00000200L
#define KSPROPERTY_TYPE_GETRANGE    0x00010000L
#define KSPROPERTY_TYPE_TOPOLOGY    0x00000100L

// KSPROPERTY_MEMBERSHEADER.MembersFlags（与 ks.h 一致）
#define KSPROPERTY_MEMBER_RANGES    0x00000001L
#define KSPROPERTY_MEMBER_VALUES    0x00000002L

#define KSPROPERTY_TOPOLOGY_NODES   1

// VideoProcAmp 能力标志（与 ksmedia.h KSPROPERTY_VIDEOPROCAMP_FLAGS_* 一致）
#define KSPROPERTY_VIDEOPROCAMP_FLAGS_AUTO    0x0001L
#define KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL  0x0002L

typedef struct {
    GUID  Set;
    ULONG Id;
    ULONG Flags;
} KSPROPERTY;

typedef struct {
    KSPROPERTY Property;
    LONG  Value;
    ULONG Flags;
    ULONG Capabilities;
} KSPROPERTY_VIDEOPROCAMP_S;

// KSPROPERTY_TYPE_BASICSUPPORT 响应结构
// 实际大小 40 字节（KSIDENTIFIER = GUID + ULONG + ULONG = 24 字节）
typedef struct {
    ULONG AccessFlags;        // 4, offset 0
    ULONG DescriptionSize;    // 4, offset 4
    GUID  Set;                // 16, offset 8
    ULONG Id;                 // 4, offset 24
    ULONG Flags;              // 4, offset 28
    ULONG MembersListCount;   // 4, offset 32
    ULONG Reserved;           // 4, offset 36
} KSPROPERTY_DESCRIPTION;    // 40 bytes total

typedef struct {
    ULONG MembersFlags;
    ULONG MembersSize;
    ULONG MembersCount;
    ULONG Reserved;
} KSPROPERTY_MEMBERSHEADER;

// KSPROPERTY_TYPE_BASICSUPPORT 返回的成员范围数据。
// 严格按 ks.h / ksmedia.h：范围型成员是 KSPROPERTY_STEPPING_LONG（16 字节）：
//   SteppingDelta(4) + Reserved(4) + Bounds{ SignedMinimum(4), SignedMaximum(4) }
// 部分驱动只返回 KSPROPERTY_BOUNDS_LONG（8 字节，无 SteppingDelta）。
typedef struct {
    LONG  SignedMinimum;
    LONG  SignedMaximum;
} KSPROPERTY_BOUNDS_LONG;

typedef struct {
    ULONG SteppingDelta;
    ULONG Reserved;
    KSPROPERTY_BOUNDS_LONG Bounds;
} KSPROPERTY_STEPPING_LONG;

// 节点级属性请求（KSP_NODE，附加 NodeId）
// 严格按 ks.h：{ KSPROPERTY Property; ULONG NodeId; ULONG Reserved; } 共 32 字节。
// Reserved 字段必不可少——缺它会令请求长度不匹配，驱动返回
// ERROR_INVALID_FUNCTION / ERROR_INVALID_PARAMETER，节点级 GET/SET/GETRANGE 全失败。
typedef struct {
    KSPROPERTY Property;
    ULONG      NodeId;
    ULONG      Reserved;
} KSP_NODE;

// 节点级 VideoProcAmp GET/SET 结构（KSPROPERTY_VIDEOPROCAMP_NODE_S）
typedef struct {
    KSP_NODE NodeProperty;
    LONG     Value;
    ULONG    Flags;
    ULONG    Capabilities;
} KSPROPERTY_VIDEOPROCAMP_NODE_S;

// ===================== 所需 GUID / 常量 =====================

static const GUID KSCATEGORY_VIDEO_CAMERA = { 0xE5323777, 0xF976, 0x4F5B, { 0x9B,0x55,0xB9,0x46,0x99,0xC4,0x6E,0x44 } };
static const GUID PROPSETID_VIDCAP_VIDEOPROCAMP = { 0xC6E13360, 0x30AC, 0x11D0, { 0xA1,0x8C,0x00,0xA0,0xC9,0x11,0x89,0x56 } };
// 相机控制属性集（Camera Control）：平移/俯仰/变焦/曝光/光圈/对焦等（AMCap Camera Control 面板）
static const GUID PROPSETID_VIDCAP_CAMERACONTROL = { 0xC6E13370, 0x30AC, 0x11D0, { 0xA1,0x8C,0x00,0xA0,0xC9,0x11,0x89,0x56 } };
static const GUID KSPROPSETID_Topology = { 0x720D4AC0, 0x7533, 0x11D0, { 0xA5,0xD6,0x28,0xDB,0x04,0xC1,0x00,0x00 } };
static const GUID KSNODETYPE_VIDEO_PROCESSING = { 0xD76E9640, 0x38FD, 0x11D0, { 0xA1,0x62,0x00,0xA0,0xC9,0x22,0x31,0x96 } };
// 非标准处理节点 GUID（部分精简驱动使用自定义节点类型）
// 日志中 node[3] = {941C7AC0-C559-11D0-8A2B-00A0C9255AC1}
static const GUID CUSTOM_NODE_VIDEO_PROCESSING = { 0x941C7AC0, 0xC559, 0x11D0, { 0x8A,0x2B,0x00,0xA0,0xC9,0x25,0x5A,0xC1 } };

// ===================== DirectShow/COM 探测所需 GUID =====================
// AMCap 等工具经 DirectShow 的 IAMVideoProcAmp 读写视频属性。为复刻该通道，
// 手动声明其枚举所需的最小 GUID/接口（工程不含 objbase.h/dshow.h，见文件头注释）。

static const GUID CLSID_SystemDeviceEnum        = { 0x62BE5D10, 0x60EB, 0x11D0, { 0xBD,0x3B,0x00,0xA0,0xC9,0x11,0xCE,0x86 } };
static const GUID CLSID_VideoInputDeviceCategory = { 0x860BB310, 0x5D01, 0x11D0, { 0xBD,0x3B,0x00,0xA0,0xC9,0x11,0xCE,0x86 } };
static const GUID IID_ICreateDevEnum            = { 0x29840822, 0x5B84, 0x11D0, { 0xBD,0x3B,0x00,0xA0,0xC9,0x11,0xCE,0x86 } };
static const GUID IID_IBaseFilter               = { 0x56A86895, 0x0AD4, 0x11CE, { 0xB0,0x3A,0x00,0x20,0xAF,0x0B,0xA7,0x70 } };
static const GUID IID_IAMVideoProcAmp           = { 0xC6E13360, 0x30AC, 0x11D0, { 0xA1,0x8C,0x00,0xA0,0xC9,0x11,0x89,0x56 } };
// IAMCameraControl（strmif.h）：相机控制高层接口，AMCap Camera Control 面板底层
static const GUID IID_IAMCameraControl          = { 0xC6E13370, 0x30AC, 0x11D0, { 0xA1,0x8C,0x00,0xA0,0xC9,0x11,0x89,0x56 } };
static const GUID IID_IPropertyBag              = { 0x55272A00, 0x42CB, 0x11CE, { 0x81,0x35,0x00,0xAA,0x00,0x4B,0xB8,0x51 } };
// IKsPropertySet（strmif.h）：DirectShow 访问 KS 属性集的标准接口（VideoProcAmp 属性页底层）
static const GUID IID_IKsPropertySet            = { 0x31EFAC30, 0x515C, 0x11D0, { 0xA9,0xAA,0x00,0xAA,0x00,0x61,0xBE,0x93 } };
// IKsControl（ksproxy.h / DirectShow DDK）：微软推荐的属性集传递接口，AMCap 属性页底层
static const GUID IID_IKsControl                = { 0x28F54685, 0x06FD, 0x11D2, { 0xB2,0x7A,0x00,0xA0,0xC9,0x22,0x31,0x96 } };

// ===================== 分辨率枚举所需 GUID（IAMStreamConfig） =====================
// IAMStreamConfig（strmif.h）：枚举设备支持格式的标准接口（GetStreamCaps），
// 通常由视频捕获输出 pin 实现；FORMAT_VideoInfo(2) 为 VIDEOINFOHEADER(2) 格式 GUID
static const GUID IID_IAMStreamConfig           = { 0xC6E13340, 0x30AC, 0x11D0, { 0xA1,0x8C,0x00,0xA0,0xC9,0x11,0x89,0x56 } };
static const GUID IID_IEnumPins                 = { 0xCC669960, 0x7B37, 0x11D0, { 0xB0,0xEF,0x00,0xAA,0x00,0x6C,0x0A,0x0C } };
static const GUID IID_IPin                      = { 0x56A86891, 0x0AD4, 0x11CE, { 0xB0,0x3A,0x00,0x20,0xAF,0x0B,0xA7,0x70 } };
static const GUID FORMAT_VideoInfo              = { 0x05589F80, 0xC356, 0x11CE, { 0xBF,0x01,0x00,0xAA,0x00,0x55,0x59,0x5A } };
static const GUID FORMAT_VideoInfo2             = { 0xF72A76A0, 0xEB0A, 0x11D0, { 0xAC,0xE4,0x00,0x00,0xC0,0xCC,0x16,0xBA } };

// ===================== capture pin 识别（AMCap 同款） =====================
// 通过 IKsPropertySet::Get(KSPROPSETID_Pin, KSPROPERTY_PIN_CATEGORY) 查询 pin 类别，
// 仅 PIN_CATEGORY_CAPTURE 的 pin 才持有标准 IAMStreamConfig（非 capture pin 的
// 占位实现布局非标准，直接 QI/调用会导致栈破坏/RTC #0/写越界）。
static const GUID KSPROPSETID_Pin               = { 0x60AFD4D4, 0x201C, 0x11D3, { 0xAD,0x6F,0x00,0xA0,0xC9,0x2B,0x8D,0x27 } };
static const GUID PIN_CATEGORY_CAPTURE           = { 0xFB6C4281, 0x0353, 0x11D1, { 0x90,0x5F,0x00,0xC0,0x4F,0xC2,0xBB,0x4F } };
#define KSPROPERTY_PIN_CATEGORY 0x10

// ===================== 手动声明最小 DirectShow/COM 依赖 =====================
// 本工程不包含 objbase.h/dshow.h（见文件头），照旧手动声明最小 vtable。
// 方法顺序必须与官方接口完全一致，仅声明实际调用的方法。
// ole32.lib 已链接；读取 FriendlyName 需要 oleaut32.lib（SysFreeString）。

#pragma comment(lib, "oleaut32.lib")

#define COINIT_MULTITHREADED       0x0
#define COINIT_APARTMENTTHREADED   0x2
#define CLSCTX_INPROC_SERVER       0x1
#define CLSCTX_LOCAL_SERVER        0x4
#define CLSCTX_ALL                 0x17
#define VT_BSTR                    8

extern "C" HRESULT __stdcall CoInitializeEx(void* pvReserved, DWORD dwCoInit);
extern "C" void    __stdcall CoUninitialize(void);
extern "C" HRESULT __stdcall CoCreateInstance(const GUID* rclsid, void* pUnkOuter,
                                              DWORD dwClsContext, const GUID* riid, void** ppv);
extern "C" void    __stdcall SysFreeString(wchar_t* bstr);
// IMoniker::BindToObject 属于 OLE：仅 CoInitializeEx 不够（实测 BindToObject 返回 S_FALSE），
// 需 OleInitialize 初始化 OLE 环境；CreateBindCtx 提供标准绑定上下文。
extern "C" HRESULT __stdcall OleInitialize(void* pvReserved);
extern "C" void    __stdcall OleUninitialize(void);
extern "C" HRESULT __stdcall CreateBindCtx(DWORD reserved, void** ppbc);
// AM_MEDIA_TYPE 的 pbFormat / pSCC 由 GetStreamCaps 用 CoTaskMemAlloc 分配，须 CoTaskMemFree 释放
extern "C" void    __stdcall CoTaskMemFree(void* pv);

// 最小 VARIANT：只读 vt 与 bstrVal；union 留足 16 字节，避免对方写入 DECIMAL 等大成员时越界
typedef struct tagVARIANT {
    USHORT vt;
    USHORT wReserved1;
    USHORT wReserved2;
    USHORT wReserved3;
    union {
        wchar_t* bstrVal;   // offset 8
        void*    pv;
        BYTE     raw[16];
    };
} VARIANT;

// IUnknown（通用基类）
typedef struct IUnknownVtbl IUnknownVtbl;
typedef struct IUnknown { IUnknownVtbl* lpVtbl; } IUnknown;
struct IUnknownVtbl {
    HRESULT (__stdcall* QueryInterface)(IUnknown*, const GUID*, void**);
    ULONG   (__stdcall* AddRef)(IUnknown*);
    ULONG   (__stdcall* Release)(IUnknown*);
};

// ICreateDevEnum：CreateClassEnumerator 枚举设备类别
typedef struct IEnumMoniker IEnumMoniker;
typedef struct ICreateDevEnumVtbl ICreateDevEnumVtbl;
typedef struct ICreateDevEnum { ICreateDevEnumVtbl* lpVtbl; } ICreateDevEnum;
struct ICreateDevEnumVtbl {
    HRESULT (__stdcall* QueryInterface)(ICreateDevEnum*, const GUID*, void**);
    ULONG   (__stdcall* AddRef)(ICreateDevEnum*);
    ULONG   (__stdcall* Release)(ICreateDevEnum*);
    HRESULT (__stdcall* CreateClassEnumerator)(ICreateDevEnum*, const GUID* clsidDeviceClass,
                                               IEnumMoniker** ppEnumMoniker, DWORD dwFlags);
};

// IEnumMoniker：设备 moniker 枚举
typedef struct IMoniker IMoniker;
typedef struct IEnumMonikerVtbl IEnumMonikerVtbl;
typedef struct IEnumMoniker { IEnumMonikerVtbl* lpVtbl; } IEnumMoniker;
struct IEnumMonikerVtbl {
    HRESULT (__stdcall* QueryInterface)(IEnumMoniker*, const GUID*, void**);
    ULONG   (__stdcall* AddRef)(IEnumMoniker*);
    ULONG   (__stdcall* Release)(IEnumMoniker*);
    HRESULT (__stdcall* Next)(IEnumMoniker*, ULONG celt, IMoniker** rgelt, ULONG* pceltFetched);
    HRESULT (__stdcall* Skip)(IEnumMoniker*, ULONG celt);
    HRESULT (__stdcall* Reset)(IEnumMoniker*);
    HRESULT (__stdcall* Clone)(IEnumMoniker*, IEnumMoniker** ppenum);
};

// IMoniker 继承链为 IUnknown → IPersist → IPersistStream（objidl.h:
// interface IMoniker : IPersistStream），vtable 顺序必须为：
//   [0-2] IUnknown: QI / AddRef / Release
//   [3]   IPersist::GetClassID
//   [4-7] IPersistStream: IsDirty / Load / Save / GetSizeMax
//   [8]   IMoniker::BindToObject
//   [9]   IMoniker::BindToStorage
// 注意：不得跳过 IPersistStream 的 4 个方法——否则 BindToObject 会落到真
// vtable 的 Enum 槽、BindToStorage 落到 IsEqual 槽，造成错位：BindToObject
// 返回 S_FALSE、BindToStorage 访问违例（与历史日志现象吻合）。
typedef struct IBindCtx IBindCtx;
typedef struct IMonikerVtbl IMonikerVtbl;
typedef struct IMoniker { IMonikerVtbl* lpVtbl; } IMoniker;
struct IMonikerVtbl {
    HRESULT (__stdcall* QueryInterface)(IMoniker*, const GUID*, void**);
    ULONG   (__stdcall* AddRef)(IMoniker*);
    ULONG   (__stdcall* Release)(IMoniker*);
    // IPersist
    HRESULT (__stdcall* GetClassID)(IMoniker*, void* pClassID);
    // IPersistStream
    HRESULT (__stdcall* IsDirty)(IMoniker*);
    HRESULT (__stdcall* Load)(IMoniker*, void* pStm);
    HRESULT (__stdcall* Save)(IMoniker*, void* pStm, int fClearDirty);
    HRESULT (__stdcall* GetSizeMax)(IMoniker*, void* pcbSize);
    // IMoniker
    HRESULT (__stdcall* BindToObject)(IMoniker*, IBindCtx* pbc, IMoniker* pmkToLeft,
                                      const GUID* riidResult, void** ppvResult);
    HRESULT (__stdcall* BindToStorage)(IMoniker*, IBindCtx* pbc, IMoniker* pmkToLeft,
                                       const GUID* riid, void** ppvObj);
};

// IPropertyBag：读取设备 FriendlyName
typedef struct IPropertyBagVtbl IPropertyBagVtbl;
typedef struct IPropertyBag { IPropertyBagVtbl* lpVtbl; } IPropertyBag;
struct IPropertyBagVtbl {
    HRESULT (__stdcall* QueryInterface)(IPropertyBag*, const GUID*, void**);
    ULONG   (__stdcall* AddRef)(IPropertyBag*);
    ULONG   (__stdcall* Release)(IPropertyBag*);
    HRESULT (__stdcall* Read)(IPropertyBag*, const wchar_t* pszPropName, VARIANT* pVar, void* pErrorLog);
    HRESULT (__stdcall* Write)(IPropertyBag*, const wchar_t*, VARIANT*);
};

// IBaseFilter：完整 vtable（前 3 槽 IUnknown + IPersist::GetClassID + 媒体控制 +
// 同步源 + EnumPins/FindPin）。现有 IAM 属性探测只用到 QI（前 3 槽）；
// 分辨率枚举需要 EnumPins（槽 10）遍历 pin 以找到实现 IAMStreamConfig 的输出 pin。
typedef struct IBaseFilterVtbl IBaseFilterVtbl;
typedef struct IBaseFilter { IBaseFilterVtbl* lpVtbl; } IBaseFilter;
struct IBaseFilterVtbl {
    HRESULT (__stdcall* QueryInterface)(IBaseFilter*, const GUID*, void**);
    ULONG   (__stdcall* AddRef)(IBaseFilter*);
    ULONG   (__stdcall* Release)(IBaseFilter*);
    HRESULT (__stdcall* GetClassID)(IBaseFilter*, void* pClassID);
    HRESULT (__stdcall* Stop)(IBaseFilter*);
    HRESULT (__stdcall* Pause)(IBaseFilter*);
    HRESULT (__stdcall* Run)(IBaseFilter*, long long tStart);
    HRESULT (__stdcall* GetState)(IBaseFilter*, DWORD dwMilliSecsTimeout, void* pState);
    HRESULT (__stdcall* SetSyncSource)(IBaseFilter*, void* pClock);
    HRESULT (__stdcall* GetSyncSource)(IBaseFilter*, void** ppClock);
    HRESULT (__stdcall* EnumPins)(IBaseFilter*, void** ppEnum);
    HRESULT (__stdcall* FindPin)(IBaseFilter*, const wchar_t* pId, void** ppPin);
};

// IEnumPins：遍历过滤器 pin（Next 槽 3）
typedef struct IPin IPin;
typedef struct IEnumPinsVtbl IEnumPinsVtbl;
typedef struct IEnumPins { IEnumPinsVtbl* lpVtbl; } IEnumPins;
struct IEnumPinsVtbl {
    HRESULT (__stdcall* QueryInterface)(IEnumPins*, const GUID*, void**);
    ULONG   (__stdcall* AddRef)(IEnumPins*);
    ULONG   (__stdcall* Release)(IEnumPins*);
    HRESULT (__stdcall* Next)(IEnumPins*, ULONG cPins, IPin** ppPins, ULONG* pcFetched);
    HRESULT (__stdcall* Skip)(IEnumPins*, ULONG cPins);
    HRESULT (__stdcall* Reset)(IEnumPins*);
    HRESULT (__stdcall* Clone)(IEnumPins*, IEnumPins** ppEnum);
};

// IPin：最小 vtable（仅用到 QueryDirection 槽 9 判断输出 pin；PINDIR_OUTPUT=1）
typedef struct IPinVtbl IPinVtbl;
typedef struct IPin { IPinVtbl* lpVtbl; } IPin;
struct IPinVtbl {
    HRESULT (__stdcall* QueryInterface)(IPin*, const GUID*, void**);
    ULONG   (__stdcall* AddRef)(IPin*);
    ULONG   (__stdcall* Release)(IPin*);
    HRESULT (__stdcall* Connect)(IPin*, IPin*, void* pMediaType);
    HRESULT (__stdcall* ReceiveConnection)(IPin*, IPin*, void* pMediaType);
    HRESULT (__stdcall* Disconnect)(IPin*);
    HRESULT (__stdcall* ConnectedTo)(IPin*, IPin** ppPin);
    HRESULT (__stdcall* ConnectionMediaType)(IPin*, void* pMediaType);
    HRESULT (__stdcall* QueryPinInfo)(IPin*, void* pInfo);
    HRESULT (__stdcall* QueryDirection)(IPin*, int* pPinDir);
};

// IAMStreamConfig：枚举支持格式（GetStreamCaps 槽 6）
typedef struct IAMStreamConfigVtbl IAMStreamConfigVtbl;
typedef struct IAMStreamConfig { IAMStreamConfigVtbl* lpVtbl; } IAMStreamConfig;
struct IAMStreamConfigVtbl {
    HRESULT (__stdcall* QueryInterface)(IAMStreamConfig*, const GUID*, void**);
    ULONG   (__stdcall* AddRef)(IAMStreamConfig*);
    ULONG   (__stdcall* Release)(IAMStreamConfig*);
    HRESULT (__stdcall* SetFormat)(IAMStreamConfig*, void* pMediaType);
    HRESULT (__stdcall* GetFormat)(IAMStreamConfig*, void** ppMediaType);
    HRESULT (__stdcall* GetNumberOfCapabilities)(IAMStreamConfig*, int* piCount, int* piSize);
    // GetStreamCaps 标准签名 4 参数（含 cbSCC）。此前"3 参数"猜测与栈破坏均源于
    // 误用了非 capture pin 的占位实现；锁定 PIN_CATEGORY_CAPTURE pin 后为标准实现。
    HRESULT (__stdcall* GetStreamCaps)(IAMStreamConfig*, int iIndex, void** ppMediaType,
                                       BYTE** ppSCC, DWORD_PTR cbSCC);
};

// AM_MEDIA_TYPE（只读需要的字段；pbFormat 由 GetStreamCaps 分配）
typedef struct tagAM_MEDIA_TYPE {
    GUID   majortype;                 // 0
    GUID   subtype;                   // 16（MEDIASUBTYPE_*，Data1 即 FOURCC）
    BOOL   bFixedSizeSamples;         // 32
    BOOL   bTemporalCompression;      // 36
    ULONG  lSampleSize;               // 40
    GUID   formattype;                // 44
    void*  pUnk;                      // 56
    ULONG  cbFormat;                  // 60
    BYTE*  pbFormat;                  // 64
} AM_MEDIA_TYPE;

// VIDEOINFOHEADER 内 bmiHeader 偏移：VideoInfo 格式 48，VideoInfo2 格式 72
// （VIDEOINFOHEADER2 = VIH(48) + 6 个 DWORD 扩展字段 = 48+24 = 72：
// dwInterlaceFlags/dwCopyProtectFlags/dwPictAspectRatioX/dwPictAspectRatioY/
// dwControlFlags/dwReserved2，共 6 个，非 7 个）
// BITMAPINFOHEADER：biSize@0 biWidth@4 biHeight@8 biCompression@16（FOURCC）
#define VIH_OFFSET_VIDEOINFO  48
#define VIH_OFFSET_VIDEOINFO2 72

// IAMVideoProcAmp：与 AMCap 一致的高层视频属性接口（属性 0=Brightness … 9=Gain）
typedef struct IAMVideoProcAmpVtbl IAMVideoProcAmpVtbl;
typedef struct IAMVideoProcAmp { IAMVideoProcAmpVtbl* lpVtbl; } IAMVideoProcAmp;
struct IAMVideoProcAmpVtbl {
    HRESULT (__stdcall* QueryInterface)(IAMVideoProcAmp*, const GUID*, void**);
    ULONG   (__stdcall* AddRef)(IAMVideoProcAmp*);
    ULONG   (__stdcall* Release)(IAMVideoProcAmp*);
    HRESULT (__stdcall* GetRange)(IAMVideoProcAmp*, long Property, long* pMin, long* pMax,
                                  long* pSteppingDelta, long* pDefault, long* pCapsFlags);
    HRESULT (__stdcall* Set)(IAMVideoProcAmp*, long Property, long lValue, long Flags);
    HRESULT (__stdcall* Get)(IAMVideoProcAmp*, long Property, long* lValue, long* Flags);
};

// IAMCameraControl：与 AMCap 一致的相机控制高层接口（属性 0=Pan … 19=AutoExposurePriority）。
// vtable 布局与 IAMVideoProcAmp 完全一致（GetRange/Set/Get），仅 IID 与语义不同。
typedef struct IAMCameraControlVtbl IAMCameraControlVtbl;
typedef struct IAMCameraControl { IAMCameraControlVtbl* lpVtbl; } IAMCameraControl;
struct IAMCameraControlVtbl {
    HRESULT (__stdcall* QueryInterface)(IAMCameraControl*, const GUID*, void**);
    ULONG   (__stdcall* AddRef)(IAMCameraControl*);
    ULONG   (__stdcall* Release)(IAMCameraControl*);
    HRESULT (__stdcall* GetRange)(IAMCameraControl*, long Property, long* pMin, long* pMax,
                                  long* pSteppingDelta, long* pDefault, long* pCapsFlags);
    HRESULT (__stdcall* Set)(IAMCameraControl*, long Property, long lValue, long Flags);
    HRESULT (__stdcall* Get)(IAMCameraControl*, long Property, long* lValue, long* Flags);
};

// IKsPropertySet：DirectShow 访问 KS 属性集的标准接口，VideoProcAmp 属性页（AMCap 同款）底层用它。
// 对本设备实测 IAMVideoProcAmp QI 返回 E_NOINTERFACE，但 filter 一定实现 IKsPropertySet。
// 注意：vtable 槽 5 标准实现即 GetRange（本接口仅 Set/Get/GetRange 三方法，
// 无 QuerySupported 成员；审查项"槽 5 误声明为 GetRange"不成立）。
typedef struct IKsPropertySetVtbl IKsPropertySetVtbl;
typedef struct IKsPropertySet { IKsPropertySetVtbl* lpVtbl; } IKsPropertySet;
struct IKsPropertySetVtbl {
    HRESULT (__stdcall* QueryInterface)(IKsPropertySet*, const GUID*, void**);
    ULONG   (__stdcall* AddRef)(IKsPropertySet*);
    ULONG   (__stdcall* Release)(IKsPropertySet*);
    HRESULT (__stdcall* Set)(IKsPropertySet*, const GUID* PropSet, ULONG Id, void* InstanceData,
                             ULONG InstanceLength, void* PropertyData, ULONG DataLength);
    HRESULT (__stdcall* Get)(IKsPropertySet*, const GUID* PropSet, ULONG Id, void* InstanceData,
                             ULONG InstanceLength, void* PropertyData, ULONG DataLength, ULONG* BytesReturned);
    HRESULT (__stdcall* GetRange)(IKsPropertySet*, const GUID* PropSet, ULONG Id, void* InstanceData,
                                  ULONG InstanceLength, void* Min, ULONG MinLength,
                                  void* Max, ULONG MaxLength, void* SteppingDelta, ULONG SteppingDeltaLength);
};

// IKsControl：微软推荐的在 WDM 驱动与用户态之间传递属性集的接口（ksproxy 的 filter/pin 均实现）。
// AMCap 的 VideoProcAmp 属性页底层实际走的是 IKsControl::KsProperty（DirectShow DDK 文档推荐）。
// KsProperty 与 IOCTL_KS_PROPERTY 一一对应：Property/PropertyLength 是请求头（KSPROPERTY，
// 或 KSPROPERTY_VIDEOPROCAMP_S 整体），PropertyData/DataLength 是数据缓冲区。
typedef struct IKsControlVtbl IKsControlVtbl;
typedef struct IKsControl { IKsControlVtbl* lpVtbl; } IKsControl;
struct IKsControlVtbl {
    HRESULT (__stdcall* QueryInterface)(IKsControl*, const GUID*, void**);
    ULONG   (__stdcall* AddRef)(IKsControl*);
    ULONG   (__stdcall* Release)(IKsControl*);
    HRESULT (__stdcall* KsProperty)(IKsControl*, KSPROPERTY* Property, ULONG PropertyLength,
                                    void* PropertyData, ULONG DataLength, ULONG* BytesReturned);
    HRESULT (__stdcall* KsMethod)(IKsControl*, void* Method, ULONG MethodLength,
                                  void* MethodData, ULONG DataLength, ULONG* BytesReturned);
    HRESULT (__stdcall* KsEvent)(IKsControl*, void* Event, ULONG EventLength,
                                 void* EventData, ULONG DataLength, ULONG* BytesReturned);
};

// 调试日志：仅 Debug（_DEBUG）构建输出到 procamp_debug.txt；Release 下编译为空操作
// 宏（零调用开销、参数不求值），不产生任何日志文件写入。
#ifdef _DEBUG
// 临时调试日志前向声明（供 KsIoctlDirect/QueryNodeRange 等使用）
static void DbgLog(const char* fmt, ...);
#else
#define DbgLog(...) ((void)0)
#endif

// 属性枚举上限（见 uvc.h ProcAmpPropertyId / CameraControlPropertyId，
// 值与 KSPROPERTY_VIDEOPROCAMP_* / KSPROPERTY_CAMERACONTROL_* 一致）
static const int PROCAMP_MAX_ID = 13;       // VideoProcAmp: PROCAMP_POWERLINE_FREQUENCY
static const int CAMERACONTROL_MAX_ID = 19; // CameraControl: CAMERA_AUTO_EXPOSURE_PRIORITY

// ===================== 工作线程 =====================
//
// 所有设备句柄与缓存只在工作线程中被访问；导出的 C 函数把一次"命令"写入全局槽，
// 通知工作线程执行，等待完成后再返回，从而天然串行化并发访问。

enum ProcAmpOp { OP_INIT = 0, OP_RELEASE = 1, OP_GETCOUNT = 2, OP_GETINFO = 3, OP_SET = 4, OP_GET = 5, OP_ENUMFORMATS = 6 };

struct Cmd {
    int            op;
    const char*   dev;        // OP_INIT / OP_ENUMFORMATS：设备名（UTF-8，指向 g_devNameBuf）
    int           idx;        // OP_GETINFO
    int           setIdx;     // OP_GETCOUNT/GETINFO/SET/GET：0=VideoProcAmp 1=CameraControl
    int           pid;        // OP_SET / OP_GET
    long          val;        // OP_SET
    int           autoMode;   // OP_SET
    ProcAmpParamInfo* info;   // OP_GETINFO：输出（指向 g_cmd.outInfo，工作线程侧缓冲）
    long*         oVal;       // OP_GET：输出（指向 g_cmd.outVal，工作线程侧缓冲）
    long*         oFlags;     // OP_GET：输出（指向 g_cmd.outFlags，工作线程侧缓冲）
    VideoFormatInfo* fmtBuf;  // OP_ENUMFORMATS：输出（指向 g_cmd.outFormats，工作线程侧缓冲）
    int           fmtCap;     // OP_ENUMFORMATS：输出缓冲容量（格式个数上限）
    int           result;     // 输出
    volatile LONG gen;        // 命令代次（提交时递增）：区分"当前命令"与"超时后残留执行"
    // 工作线程侧结果缓冲：即便调用方因超时提前返回，工作线程也绝不会写调用方内存
    // （调用方仅在等待成功后自行拷贝，杜绝悬垂指针/释放后写入）。
    ProcAmpParamInfo outInfo;
    long          outVal;
    long          outFlags;
    VideoFormatInfo outFormats[64]; // OP_ENUMFORMATS 结果缓冲（分辨率枚举上限 64 种）
};

static Cmd             g_cmd;
static volatile LONG   g_cmdGen = 0; // 命令代次计数器（提交处递增，写入 g_cmd.gen）
static HANDLE          g_hReq = NULL;   // 请求就绪（自动复位）
static HANDLE          g_hDone = NULL;  // 处理完成（自动复位）
static HANDLE          g_hThread = NULL;
static volatile LONG   g_run = 1;

// 命令提交并发控制（见导出接口注释）：串行化"写命令槽→等待结果"整段，
// 避免 g_cmd 被多调用线程踩踏、以及多个调用方同时等待同一自动复位事件永久阻塞。
// 仅调用方线程获取该锁；工作线程绝不获取（否则与等待它的调用者形成死锁）。
static SRWLOCK         g_cmdLock = SRWLOCK_INIT;

// 调试日志互斥：DbgLog 可由调用方线程（EnsureWorkerThread/WaitCommandDone）
// 与工作线程并发调用，锁内串行化日志写入，避免行交错。
static SRWLOCK         g_logLock = SRWLOCK_INIT;

// 等待命令完成的有界超时（毫秒）：防止工作线程异常/驱动挂起导致调用方永久卡死。
// 正常操作远低于该上限，仅病态场景触发后返回负错误码。
static const DWORD     kInitTimeoutMs = 60000; // OP_INIT 探测步骤多，给更长预算
static const DWORD     kOpTimeoutMs   = 20000; // 其余命令

// OP_INIT 设备名的自有拷贝：调用方（C# P/Invoke）字符串仅在调用期间有效，
// 即使调用方超时提前返回，工作线程也始终读本缓冲，杜绝悬垂指针。
// 1024 字节足以容纳标准 USB 设备路径（含 "video=" 前缀，典型 < 256 字节）。
static const int kDevNameBufSize = 1024;
static char            g_devNameBuf[kDevNameBufSize];

// 单个属性的缓存条目
struct ProcAmpEntry {
    int  id;
    long minVal;
    long maxVal;
    long stepVal;
    long defaultVal;
    long currentVal;
    long flags;        // 能力标志：0x1=Auto, 0x2=Manual
    ULONG accessFlags; // BASICSUPPORT 返回的 AccessFlags（GET/SET/GETRANGE 等）
    bool supported;
};

// 仅由工作线程访问的状态
static HANDLE                     g_devHandle = INVALID_HANDLE_VALUE; // 打开的 UVC 设备句柄
static HRESULT                    g_lastKsHr = S_OK;      // KsIoctlDirect 最近一次失败的 HRESULT（用于诊断）
// 跨线程访问（工作线程写、调用方线程经 GetLast*Error 读）：volatile + Interlocked 保证原子读写
static volatile LONG                  g_lastHr = S_OK;   // 最近一次 VideoProcAmp 调用的失败 HRESULT
static volatile LONG                  g_lastCcHr = S_OK; // 最近一次 CameraControl 调用的失败 HRESULT
static IAMVideoProcAmp*           g_pIam = nullptr;  // IAMVideoProcAmp 探测成功后的实例（图像属性，仅工作线程访问）
static IAMCameraControl*          g_pCam = nullptr;  // IAMCameraControl 探测成功后的实例（相机控制，仅工作线程访问）
static IKsPropertySet*            g_pKsPropSet = nullptr; // IKsPropertySet 探测成功后的实例（两集合共享，仅工作线程访问）
static IKsControl*                g_pKsControl = nullptr; // IKsControl 探测成功后的实例（两集合共享，仅工作线程访问）
static bool                       g_comInitedByUs = false; // 本线程已调 OleInitialize（DoCleanup 需 OleUninitialize）

// ===================== 控制集合状态 =====================
// 一台设备同时暴露两套属性：图像属性(VideoProcAmp)与相机控制(CameraControl)。
// 两者 KS 属性结构布局一致（KSPROPERTY + LONG Value + ULONG Flags + ULONG Capabilities），
// 仅属性集 GUID 不同；DirectShow 侧对应 IAMVideoProcAmp / IAMCameraControl（vtable 亦一致）。
// 这正是 AMCap 两个面板（Image Controls / Camera Control）的底层数据模型。
enum ControlSetId {
    SET_VIDEOPROCAMP = 0,  // 图像属性：亮度/对比度/色相/饱和度/锐度/伽马/白平衡等
    SET_CAMERACONTROL = 1, // 相机控制：平移/俯仰/变焦/曝光/光圈/对焦等
    SET_COUNT = 2
};

struct ControlSetState {
    const GUID* propSet;               // PROPSETID_VIDCAP_*（该集合的属性集 GUID）
    int         maxId;                 // 属性 ID 枚举上限（含）
    const char* name;                  // 日志/调试名
    std::vector<ProcAmpEntry> entries; // 属性缓存（仅工作线程访问）
    ULONG puNodeId;                    // 视频处理节点(PU) ID，-1 表示未找到
    ULONG numNodes;                    // 拓扑节点总数
    bool  hasRealPu;                   // 是否存在真实 PU 节点
    bool  writeOk;                     // 探测到可用写通道（KS/DirectShow 任一）
};

static ControlSetState g_vpSet = { &PROPSETID_VIDCAP_VIDEOPROCAMP, PROCAMP_MAX_ID, "VideoProcAmp",
                                   {}, (ULONG)-1, 0, false, false };
static ControlSetState g_ccSet = { &PROPSETID_VIDCAP_CAMERACONTROL, CAMERACONTROL_MAX_ID, "CameraControl",
                                   {}, (ULONG)-1, 0, false, false };

// 按集合 ID 取状态
static ControlSetState& GetSet(int setIdx)
{
    return setIdx == SET_CAMERACONTROL ? g_ccSet : g_vpSet;
}

// 记录最近一次失败的 HRESULT（按集合分账，供 GetLastProcAmpError/GetLastCameraControlError）
static void SetSetLastHr(int setIdx, HRESULT hr)
{
    if (setIdx == SET_CAMERACONTROL)
        InterlockedExchange(&g_lastCcHr, (LONG)hr);
    else
        InterlockedExchange(&g_lastHr, (LONG)hr);
}

// 不区分大小写的包含匹配：friendlyName 是否包含 deviceName
static bool NameMatch(const std::wstring& friendlyName, const std::wstring& deviceName)
{
    if (deviceName.empty()) return false;
    std::wstring a = friendlyName;
    std::wstring b = deviceName;
    std::transform(a.begin(), a.end(), a.begin(), ::towlower);
    std::transform(b.begin(), b.end(), b.begin(), ::towlower);
    return a.find(b) != std::wstring::npos;
}

static void DoCleanup()
{
    g_vpSet.entries.clear();
    g_vpSet.puNodeId = (ULONG)-1;
    g_vpSet.numNodes = 0;
    g_vpSet.hasRealPu = false;
    g_vpSet.writeOk = false;
    g_ccSet.entries.clear();
    g_ccSet.puNodeId = (ULONG)-1;
    g_ccSet.numNodes = 0;
    g_ccSet.hasRealPu = false;
    g_ccSet.writeOk = false;
    if (g_pIam) {
        g_pIam->lpVtbl->Release(g_pIam);
        g_pIam = nullptr;
    }
    if (g_pCam) {
        g_pCam->lpVtbl->Release(g_pCam);
        g_pCam = nullptr;
    }
    if (g_pKsPropSet) {
        g_pKsPropSet->lpVtbl->Release(g_pKsPropSet);
        g_pKsPropSet = nullptr;
    }
    if (g_pKsControl) {
        g_pKsControl->lpVtbl->Release(g_pKsControl);
        g_pKsControl = nullptr;
    }
    if (g_comInitedByUs) {
        OleUninitialize();
        g_comInitedByUs = false;
    }
    if (g_devHandle != INVALID_HANDLE_VALUE) {
        CloseHandle(g_devHandle);
        g_devHandle = INVALID_HANDLE_VALUE;
    }
}

// 发送一个 KS 属性请求，仅通过 DeviceIoControl（不经过 IKsControl）。
// 先尝试 METHOD_NEITHER（标准 IOCTL），失败时自动降级为 METHOD_BUFFERED。
static bool KsIoctlDirect(HANDLE h, const void* propReq, DWORD reqSize,
                           void* outBuf, DWORD outSize, DWORD* bytesRet)
{
    if (DeviceIoControl(h, IOCTL_KS_PROPERTY,
                        const_cast<void*>(propReq), reqSize,
                        outBuf, outSize, bytesRet, NULL))
        return true;

    // 降级：尝试 METHOD_BUFFERED（部分精简驱动不支持 METHOD_NEITHER）
    DWORD firstErr = GetLastError();
    if (DeviceIoControl(h, IOCTL_KS_PROPERTY_BUFFERED,
                        const_cast<void*>(propReq), reqSize,
                        outBuf, outSize, bytesRet, NULL)) {
        DbgLog("  KsIoctl: METHOD_BUFFERED OK (METHOD_NEITHER err=%u)", firstErr);
        return true;
    }
    DWORD finalErr = GetLastError();
    DbgLog("  KsIoctl: BOTH METHOD_NEITHER and METHOD_BUFFERED failed (first=%u final=%u)", firstErr, finalErr);
    g_lastKsHr = HRESULT_FROM_WIN32(finalErr);
    return false;
}

// 发送一个 KS 属性请求，仅通过 DeviceIoControl（不经过 IKsControl）。
static bool KsIoctl(HANDLE h, const void* propReq, DWORD reqSize,
                    void* outBuf, DWORD outSize, DWORD* bytesRet)
{
    return KsIoctlDirect(h, propReq, reqSize, outBuf, outSize, bytesRet);
}

// 解析 BASICSUPPORT/GETRANGE 响应，提取范围信息。
// 成功返回 true（要求 Max > Min），并填充 rangeMin/rangeMax/step/defaultVal。
// 成员数据严格按 ks.h 解析：MembersFlags 带 KSPROPERTY_MEMBER_RANGES 时，
// 每个成员是 KSPROPERTY_STEPPING_LONG（16B，SteppingDelta+Reserved+Bounds）
// 或精简的 KSPROPERTY_BOUNDS_LONG（8B，仅 Bounds）。驱动会在 MembersSize 里
// 标明每个成员的大小，务必用它计算步长，不要假设固定 16 字节。
static bool ParseRangeResponse(const BYTE* buf, DWORD rb,
                               LONG& rangeMin, LONG& rangeMax,
                               LONG& step, LONG& defaultVal)
{
    if (rb < sizeof(KSPROPERTY_DESCRIPTION)) return false;
    KSPROPERTY_DESCRIPTION* desc = (KSPROPERTY_DESCRIPTION*)buf;
    if (desc->MembersListCount == 0 || desc->DescriptionSize > rb) return false;

    LONG bestMin = 0, bestMax = 0, bestStep = 1;
    bool hasRange = false;
    // 首个 VALUES 成员值：UVC 约定该单值即属性默认值（或当前值），
    // 与 RANGES 同响应出现时优先用作默认值（真实默认 > 中值兜底）。
    LONG valuesDefault = 0;
    bool hasValuesDefault = false;

    ULONG off = sizeof(KSPROPERTY_DESCRIPTION);
    for (ULONG mi = 0; mi < desc->MembersListCount && off + sizeof(KSPROPERTY_MEMBERSHEADER) <= rb; mi++) {
        KSPROPERTY_MEMBERSHEADER* mh = (KSPROPERTY_MEMBERSHEADER*)(buf + off);
        off += sizeof(KSPROPERTY_MEMBERSHEADER);
        if (mh->MembersCount == 0) continue;
        ULONG elem = mh->MembersSize; // 每个成员的数据大小（驱动填写，如 16）
        if (elem == 0) elem = sizeof(KSPROPERTY_STEPPING_LONG);
        if (elem > rb - off) break;

        for (ULONG ri = 0; ri < mh->MembersCount && off + elem <= rb; ri++) {
            if (mh->MembersFlags & KSPROPERTY_MEMBER_RANGES) {
                LONG mn = 0, mx = 0, st = 1;
                if (elem >= sizeof(KSPROPERTY_STEPPING_LONG)) {
                    KSPROPERTY_STEPPING_LONG* sl = (KSPROPERTY_STEPPING_LONG*)(buf + off);
                    mn = sl->Bounds.SignedMinimum;
                    mx = sl->Bounds.SignedMaximum;
                    st = sl->SteppingDelta;
                } else if (elem >= sizeof(KSPROPERTY_BOUNDS_LONG)) {
                    KSPROPERTY_BOUNDS_LONG* b = (KSPROPERTY_BOUNDS_LONG*)(buf + off);
                    mn = b->SignedMinimum;
                    mx = b->SignedMaximum;
                }
                DbgLog("    Range[%u]: Min=%ld Max=%ld Step=%ld", ri, mn, mx, st);
                if (mx > mn && !hasRange) { // 取第一个有效范围（保持既有行为）
                    bestMin = mn;
                    bestMax = mx;
                    bestStep = st > 0 ? st : 1;
                    hasRange = true;
                }
            } else if (mh->MembersFlags & KSPROPERTY_MEMBER_VALUES) {
                // VALUES 成员：单值（如 4 字节），无范围信息。
                // UVC 约定该值为属性默认值（或当前值）：与 RANGES 同响应出现时
                // 优先用作默认值；单独出现时无范围可用，仅记录供诊断。
                LONG v = (elem >= 4) ? *(LONG*)(buf + off) : 0;
                if (!hasValuesDefault) { hasValuesDefault = true; valuesDefault = v; }
                DbgLog("    Value[%u]: %ld (默认值候选)", ri, v);
            }
            off += elem;
        }
    }

    if (!hasRange) return false;

    rangeMin = bestMin;
    rangeMax = bestMax;
    step = bestStep;
    if (hasValuesDefault && valuesDefault >= bestMin && valuesDefault <= bestMax) {
        defaultVal = valuesDefault; // 驱动下发的真实默认值
        DbgLog("    defaultVal=%ld (VALUES 成员真实默认值)", defaultVal);
    } else {
        defaultVal = (bestMin + bestMax) / 2 + 1; // 范围型成员不带默认值，取中值兜底（避免重置默认跳到最大值）
        DbgLog("    defaultVal=%ld (中值兜底)", defaultVal);
    }
    return true;
}

// 调试日志：写到"当前工作目录/procamp_debug.txt"（原硬编码 D:\... 路径在部署机上不可用）。
// 超过 8MB 自动截断重建，避免无界增长占满磁盘。
// 线程安全：DbgLog 可能在调用方线程（EnsureWorkerThread/WaitCommandDone）与工作线程
// 并发调用，用互斥锁串行化"检查大小→打开→写入→关闭"整段，避免两个 FILE* 追加写交错。
// 路径只计算一次并缓存：进程运行期间 CWD 不变，省去每次日志的 GetCurrentDirectoryW 系统调用。
// 仅 _DEBUG（Debug 配置）编译本函数体；Release 下由上方宏替换为空操作。
#ifdef _DEBUG
static void DbgLog(const char* fmt, ...)
{
    static wchar_t s_logPath[MAX_PATH];
    static volatile LONG s_pathReady = 0; // 缓存标记（Interlocked 原子读写）
    // 用 InterlockedCompareExchange 原子读路径就绪标记，避免首次并发时
    // 多线程同时进入初始化块（即使无害，也应避免重复计算路径）。
    if (!InterlockedCompareExchange(&s_pathReady, 0, 0)) {
        wchar_t path[MAX_PATH];
        DWORD n = GetCurrentDirectoryW(MAX_PATH, path);
        if (n == 0 || n >= MAX_PATH) return;
        const wchar_t kName[] = L"\\procamp_debug.txt";
        // 计算拼接后总字符数（含终止符），防止 wcscat_s 溢出
        size_t pathLen = wcslen(path);
        size_t nameLen = (sizeof(kName) / sizeof(kName[0])) - 1; // 不含终止符
        if (pathLen + nameLen >= MAX_PATH) return;
        wcscat_s(path, MAX_PATH, kName);
        wcscpy_s(s_logPath, MAX_PATH, path);
        InterlockedExchange(&s_pathReady, 1);
    }

    AcquireSRWLockExclusive(&g_logLock);

    // 日志大小上限：超过则重建文件
    WIN32_FILE_ATTRIBUTE_DATA fad;
    if (GetFileAttributesExW(s_logPath, GetFileExInfoStandard, &fad) &&
        fad.nFileSizeHigh == 0 && fad.nFileSizeLow > 8u * 1024u * 1024u) {
        HANDLE h = CreateFileW(s_logPath, GENERIC_WRITE, FILE_SHARE_READ, nullptr,
                               CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (h != INVALID_HANDLE_VALUE) CloseHandle(h);
    }

    FILE* f = nullptr;
    if (_wfopen_s(&f, s_logPath, L"a") == 0) {
        va_list ap; va_start(ap, fmt);
        vfprintf(f, fmt, ap); va_end(ap);
        fputs("\n", f);
        fclose(f);
    }

    ReleaseSRWLockExclusive(&g_logLock);
}
#endif // _DEBUG

// ===================== IAMVideoProcAmp 探测通道 =====================
//
// 背景：标准 KS 值操作（filter/节点 SET/GET）被部分 UVC 驱动以 ERROR_INVALID_FUNCTION
// 拒绝（本设备已确证），而 AMCap 等工具经 DirectShow 的 IAMVideoProcAmp 高层接口
// 读写成功。这里枚举 DirectShow 视频输入设备、按 FriendlyName 匹配、绑定捕获过滤器、
// 取 IAMVideoProcAmp 并做 GetRange/Get/Set 探测：
//   - Set 成功 → 写通道可用，保留接口实例（g_pIam）供 DoSet/DoGet 回退；
//   - Set 失败 / 接口不可用 → 维持只读。
// 注意：精简版 Windows（IoT LTSC）可能裁剪 devenum.dll，CoCreateInstance 返回
// 0x80040154（类未注册），日志会明确记录，回退只读。
// 阶段标记：记录 IAM 探测崩溃发生的调用点（仅工作线程访问）
static int g_iamStage = 0;

// 大小写不敏感的包含匹配（raw 宽字符版，供 SEH 保护函数内使用，避免 C++ 对象）
static bool RawNameMatch(const wchar_t* friendly, const wchar_t* deviceName)
{
    if (!friendly || !deviceName || !*deviceName) return false;
    size_t flen = wcslen(friendly);
    size_t dlen = wcslen(deviceName);
    if (dlen == 0 || flen < dlen) return false;
    for (size_t i = 0; i + dlen <= flen; i++)
        if (_wcsnicmp(friendly + i, deviceName, dlen) == 0) return true;
    return false;
}

// 纯 C 风格探测主体，整体包在 SEH(结构化异常)里：即使 devenum.dll 内部访问违例，
// 也只记录崩溃点并返回失败（保持只读），不让整个进程崩溃。
// 注意：_EHsc 下 __try 函数内不能有带析构的 C++ 对象，故友好名/匹配全用 raw 缓冲区。
// 返回：1 = 写通道可用（*ppIam / *ppCam / *ppKs / *ppKsControl 已赋值）；0 = 探测失败；-1 = SEH 捕获到崩溃。
static int IamProbeRaw(const wchar_t* devName, IAMVideoProcAmp** ppIam, IAMCameraControl** ppCam,
                        IKsPropertySet** ppKs, IKsControl** ppKsControl)
{
    HRESULT hr = S_OK;
    bool ok = false;
    ICreateDevEnum* pEnumFactory = nullptr;
    IEnumMoniker* pEnum = nullptr;
    IMoniker* pMoniker = nullptr;
    IPropertyBag* pBag = nullptr;
    IUnknown* pFilter = nullptr;
    IAMVideoProcAmp* pIam = nullptr;
    IAMCameraControl* pCam = nullptr;
    IKsPropertySet* pKs = nullptr;
    IKsControl* pKsControl = nullptr;
    // 全部 POD 局部变量在函数顶部初始化/声明（__except 处理器也要访问它们：
    // SEH 崩溃跳转不会执行 __try 内的局部初始化，未初始化访问是未定义行为）
    VARIANT var;
    ZeroMemory(&var, sizeof(var));
    void* pbc = nullptr; // 绑定上下文：__try 内创建，__except 处理器中释放
    wchar_t fb[512] = {0};
    ULONG fetched = 0;
    BOOL nameOk = FALSE;
    DWORD monIndex = 0;

    __try {
        g_iamStage = 1;
        hr = CoCreateInstance(&CLSID_SystemDeviceEnum, nullptr, CLSCTX_ALL,
                              &IID_ICreateDevEnum, (void**)&pEnumFactory);
        DbgLog("IAM probe: CoCreateInstance(SystemDeviceEnum) hr=0x%08lX", (unsigned long)hr);
        if (FAILED(hr) || !pEnumFactory) goto done;

        g_iamStage = 2;
        hr = pEnumFactory->lpVtbl->CreateClassEnumerator(pEnumFactory,
                                                         &CLSID_VideoInputDeviceCategory, &pEnum, 0);
        pEnumFactory->lpVtbl->Release(pEnumFactory);
        pEnumFactory = nullptr;
        DbgLog("IAM probe: CreateClassEnumerator(VideoInputDevice) hr=0x%08lX", (unsigned long)hr);
        if (FAILED(hr) || !pEnum) goto done;

        while (true) {
            g_iamStage = 3;
            pMoniker = nullptr;
            fetched = 0;
            if (pEnum->lpVtbl->Next(pEnum, 1, &pMoniker, &fetched) != S_OK || !pMoniker) break;
            monIndex++;

            // 读取 DirectShow 设备 FriendlyName 用于匹配。
            // 注意：本系统 devenum 的 BindToStorage 会访问违例（见前次 stage=4 日志），
            // 因此把这一整段包在独立的 __try/__except 里：崩溃则放弃设备名匹配，
            // 仍继续探测该设备（单摄像头系统下即为目标设备）。
            nameOk = FALSE;
            fb[0] = 0;
            __try {
                ZeroMemory(&var, sizeof(var));
                g_iamStage = 4;
                pBag = nullptr;
                if (pMoniker->lpVtbl->BindToStorage(pMoniker, nullptr, nullptr, &IID_IPropertyBag, (void**)&pBag) == S_OK && pBag) {
                    hr = pBag->lpVtbl->Read(pBag, L"FriendlyName", &var, nullptr);
                    DbgLog("IAM probe: Read(FriendlyName) hr=0x%08lX vt=%u", (unsigned long)hr, var.vt);
                    if (SUCCEEDED(hr) && var.vt == VT_BSTR && var.bstrVal) {
                        wcsncpy_s(fb, 512, var.bstrVal, _TRUNCATE);
                        fb[511] = 0;
                        nameOk = TRUE;
                    }
                    if (var.bstrVal) SysFreeString(var.bstrVal);
                    pBag->lpVtbl->Release(pBag);
                }
            }
            __except (EXCEPTION_EXECUTE_HANDLER) {
                DbgLog("IAM probe: BindToStorage/Read 访问违例，跳过设备名匹配，继续探测");
                // SEH 跳转不执行正常路径的释放代码：释放崩溃前已获取的 pBag/BSTR，
                // 避免 COM 对象与 BSTR 泄漏（崩溃点之后的对象状态不可信，仅释已持有的引用）
                if (pBag) { pBag->lpVtbl->Release(pBag); pBag = nullptr; }
                if (var.bstrVal) { SysFreeString(var.bstrVal); var.bstrVal = nullptr; }
            }
            DbgLog("IAM probe: device #%u name=%ls", monIndex, fb);

            // 读不到名字时不匹配（防止漏掉唯一的目标设备）；读得到才按名字过滤
            if (nameOk && devName && *devName && !RawNameMatch(fb, devName)) {
                pMoniker->lpVtbl->Release(pMoniker);
                pMoniker = nullptr;
                continue;
            }

            // 绑定捕获过滤器：BindToObject 以 IID_IBaseFilter 生成过滤器对象，
            // 其 vtable 前 3 项即 IUnknown，直接用 QueryInterface 取 IAMVideoProcAmp
            g_iamStage = 5;
            pFilter = nullptr;
            // 标准绑定上下文（OLE）：CreateBindCtx 失败时退回 nullptr（pbc 已在函数顶部声明，
            // 便于外层 __except 崩溃路径释放）
            CreateBindCtx(0, &pbc);
            hr = pMoniker->lpVtbl->BindToObject(pMoniker, (IBindCtx*)pbc, nullptr, &IID_IBaseFilter, (void**)&pFilter);
            if (pbc) ((IUnknown*)pbc)->lpVtbl->Release((IUnknown*)pbc);
            pMoniker->lpVtbl->Release(pMoniker);
            pMoniker = nullptr;
            DbgLog("IAM probe: BindToObject(IBaseFilter) hr=0x%08lX", (unsigned long)hr);
            // 仅当 S_OK 且返回有效 filter 才继续：S_FALSE/其他一律跳过，
            // 避免用无效/半初始化的 IBaseFilter 指针调用 QueryInterface 触发访问违例。
            if (hr != S_OK || !pFilter) continue; // 换下一个设备

            // QI 两个接口：IAMVideoProcAmp（部分设备支持）与 IKsPropertySet（DirectShow 标准 KS 属性通道）
            g_iamStage = 6;
            pIam = nullptr;
            {
                __try {
                    hr = pFilter->lpVtbl->QueryInterface(pFilter, &IID_IAMVideoProcAmp, (void**)&pIam);
                } __except (EXCEPTION_EXECUTE_HANDLER) {
                    DbgLog("IAM probe: QueryInterface(IAMVideoProcAmp) 访问违例");
                    hr = E_FAIL;
                }
            }
            if (FAILED(hr) || !pIam)
                DbgLog("IAM probe: QueryInterface(IAMVideoProcAmp) hr=0x%08lX（无此接口，改试 IKsPropertySet）", (unsigned long)hr);
            else
                DbgLog("IAM probe: QueryInterface(IAMVideoProcAmp) hr=0x%08lX", (unsigned long)hr);

            g_iamStage = 8;
            pKs = nullptr;
            {
                __try {
                    hr = pFilter->lpVtbl->QueryInterface(pFilter, &IID_IKsPropertySet, (void**)&pKs);
                } __except (EXCEPTION_EXECUTE_HANDLER) {
                    DbgLog("IAM probe: QueryInterface(IKsPropertySet) 访问违例");
                    hr = E_FAIL;
                }
            }
            DbgLog("IAM probe: QueryInterface(IKsPropertySet) hr=0x%08lX", (unsigned long)hr);

            // QI IKsControl（AMCap 属性页底层，微软推荐的属性集通道）
            g_iamStage = 10;
            pKsControl = nullptr;
            {
                __try {
                    hr = pFilter->lpVtbl->QueryInterface(pFilter, &IID_IKsControl, (void**)&pKsControl);
                } __except (EXCEPTION_EXECUTE_HANDLER) {
                    DbgLog("IAM probe: QueryInterface(IKsControl) 访问违例");
                    hr = E_FAIL;
                }
            }
            DbgLog("IAM probe: QueryInterface(IKsControl) hr=0x%08lX", (unsigned long)hr);

            // QI IAMCameraControl（AMCap Camera Control 面板底层接口，与 IAMVideoProcAmp 同布局）
            g_iamStage = 12;
            pCam = nullptr;
            {
                __try {
                    hr = pFilter->lpVtbl->QueryInterface(pFilter, &IID_IAMCameraControl, (void**)&pCam);
                } __except (EXCEPTION_EXECUTE_HANDLER) {
                    DbgLog("IAM probe: QueryInterface(IAMCameraControl) 访问违例");
                    hr = E_FAIL;
                }
            }
            if (FAILED(hr) || !pCam)
                DbgLog("IAM probe: QueryInterface(IAMCameraControl) hr=0x%08lX（无此接口）", (unsigned long)hr);
            else
                DbgLog("IAM probe: QueryInterface(IAMCameraControl) hr=0x%08lX", (unsigned long)hr);

            pFilter->lpVtbl->Release(pFilter);
            pFilter = nullptr;

            if (!pIam && !pCam && !pKs && !pKsControl) continue; // 四个接口都没有 → 换下一个设备

            // ---- 优先 IAMVideoProcAmp 探测（图像属性，AMCap 同款）----
            // 不在探测阶段做 Set（避免修改摄像头属性产生副作用）：
            // GetRange 成功即视为 IAM 通道可用，写入能力由 FinalizeSetWritability
            // 据 AccessFlags 判定，DoSet 在用户调节时实际写入。
            bool haveIamWrite = false;
            if (pIam) {
                g_iamStage = 7;
                long mn = 0, mx = 0, st = 0, dv = 0, caps = 0;
                hr = pIam->lpVtbl->GetRange(pIam, 0, &mn, &mx, &st, &dv, &caps);
                DbgLog("IAM probe: GetRange(Brightness=0) hr=0x%08lX min=%ld max=%ld step=%ld default=%ld caps=0x%lX",
                       (unsigned long)hr, mn, mx, st, dv, (unsigned long)caps);
                if (SUCCEEDED(hr)) {
                    long cur = 0, curFlags = 0;
                    hr = pIam->lpVtbl->Get(pIam, 0, &cur, &curFlags);
                    if (SUCCEEDED(hr))
                        DbgLog("IAM probe: Get(Brightness) OK value=%ld flags=0x%lX", cur, (unsigned long)curFlags);
                    else
                        DbgLog("IAM probe: Get(Brightness) FAIL hr=0x%08lX", (unsigned long)hr);
                    haveIamWrite = true;
                } else {
                    DbgLog("IAM probe: GetRange(Brightness) FAIL hr=0x%08lX → 释放 IAMVideoProcAmp", (unsigned long)hr);
                    pIam->lpVtbl->Release(pIam);
                    pIam = nullptr;
                }
            }

            // ---- IAMCameraControl 探测（相机控制，AMCap Camera Control 面板同款）----
            // 同 IAMVideoProcAmp：GetRange 成功即视为通道可用，不在探测阶段做 Set。
            if (pCam) {
                g_iamStage = 13;
                long mn = 0, mx = 0, st = 0, dv = 0, caps = 0;
                hr = pCam->lpVtbl->GetRange(pCam, 0, &mn, &mx, &st, &dv, &caps);
                DbgLog("IAM probe: CameraControl GetRange(Pan=0) hr=0x%08lX min=%ld max=%ld step=%ld default=%ld caps=0x%lX",
                       (unsigned long)hr, mn, mx, st, dv, (unsigned long)caps);
                if (SUCCEEDED(hr)) {
                    long cur = 0, curFlags = 0;
                    hr = pCam->lpVtbl->Get(pCam, 0, &cur, &curFlags);
                    if (SUCCEEDED(hr))
                        DbgLog("IAM probe: CameraControl Get(Pan) OK value=%ld flags=0x%lX", cur, (unsigned long)curFlags);
                    else
                        DbgLog("IAM probe: CameraControl Get(Pan) FAIL hr=0x%08lX", (unsigned long)hr);
                    haveIamWrite = true;
                } else {
                    DbgLog("IAM probe: CameraControl GetRange(Pan) FAIL hr=0x%08lX → 释放 IAMCameraControl", (unsigned long)hr);
                    pCam->lpVtbl->Release(pCam);
                    pCam = nullptr;
                }
            }

            // 任一 IAM 写通道可用即保留对应接口（done 标签统一赋值）；不再需要 IKs 探测
            if (haveIamWrite) {
                ok = true;
                if (pKs) { pKs->lpVtbl->Release(pKs); pKs = nullptr; }
                if (pKsControl) { pKsControl->lpVtbl->Release(pKsControl); pKsControl = nullptr; }
                break;
            }

            // ---- IKsPropertySet 探测 ----
            // 历史：4B long 与 36B KSPROPERTY_VIDEOPROCAMP_S 均返回 122 ERROR_INSUFFICIENT_BUFFER
            // （连 Set 也返回 122，说明并非输出缓冲区太短，而是 ksproxy 对 PropertyData 的
            // 格式/大小另有要求）。本次按 MSDN 建议先做"NULL/0 查询所需大小"，再对 Get/Set
            // 做多种 DataLength 扫描，定位驱动真正接受的缓冲区格式。
            if (pKs) {
                g_iamStage = 9;

                // A) MSDN 推荐：Get(NULL, 0) 查询所需缓冲区大小
                {
                    ULONG reqBytes = 0;
                    HRESULT hrq = pKs->lpVtbl->Get(pKs, &PROPSETID_VIDCAP_VIDEOPROCAMP, 0, nullptr, 0,
                                                   nullptr, 0, &reqBytes);
                    DbgLog("IAM probe: IKs Get(NULL,0) size query hr=0x%08lX required=%lu",
                           (unsigned long)hrq, reqBytes);
                }

                // B) Get 长度扫描（缓冲区首部预填 KSPROPERTY 头，按组合缓冲模式尝试）
                {
                    static const ULONG sizes[] = { 12, 24, 36, 40, 48, 64, 128, 256 };
                    for (int ti = 0; ti < (int)(sizeof(sizes) / sizeof(sizes[0])); ti++) {
                        BYTE buf[256];
                        ZeroMemory(buf, sizeof(buf));
                        KSPROPERTY_VIDEOPROCAMP_S* ks = (KSPROPERTY_VIDEOPROCAMP_S*)buf;
                        ks->Property.Set = PROPSETID_VIDCAP_VIDEOPROCAMP;
                        ks->Property.Id  = 0; // Brightness
                        ks->Property.Flags = KSPROPERTY_TYPE_GET;
                        ks->Value = 0;
                        ks->Flags = KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
                        ks->Capabilities = 0;
                        ULONG cb = 0;
                        HRESULT hrx = pKs->lpVtbl->Get(pKs, &PROPSETID_VIDCAP_VIDEOPROCAMP, 0, nullptr, 0,
                                                       buf, sizes[ti], &cb);
                        DbgLog("IAM probe: IKs Get(len=%lu) hr=0x%08lX val@24=%ld bytes=%lu raw0-7=%02X %02X %02X %02X %02X %02X %02X %02X",
                               (unsigned long)sizes[ti], (unsigned long)hrx,
                               ((KSPROPERTY_VIDEOPROCAMP_S*)buf)->Value, cb,
                               buf[0], buf[1], buf[2], buf[3], buf[4], buf[5], buf[6], buf[7]);
                        if (SUCCEEDED(hrx)) break;
                    }
                }

                // C) Set 长度扫描（原值回写 128，无副作用），先 filter 级再节点级
                {
                    static const ULONG sizes[] = { 36, 40, 64, 128, 256 };
                    HRESULT setHr = E_FAIL;
                    for (int si = 0; si < (int)(sizeof(sizes) / sizeof(sizes[0])); si++) {
                        BYTE buf[256];
                        ZeroMemory(buf, sizeof(buf));
                        KSPROPERTY_VIDEOPROCAMP_S* ss = (KSPROPERTY_VIDEOPROCAMP_S*)buf;
                        ss->Property.Set = PROPSETID_VIDCAP_VIDEOPROCAMP;
                        ss->Property.Id  = 0;
                        ss->Property.Flags = KSPROPERTY_TYPE_SET;
                        ss->Value = 128;
                        ss->Flags = KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
                        ss->Capabilities = 0;
                        HRESULT hrx = pKs->lpVtbl->Set(pKs, &PROPSETID_VIDCAP_VIDEOPROCAMP, 0, nullptr, 0,
                                                       buf, sizes[si]);
                        DbgLog("IAM probe: IKs Set(len=%lu, value=128, filter) hr=0x%08lX",
                               (unsigned long)sizes[si], (unsigned long)hrx);
                        if (SUCCEEDED(hrx)) { setHr = hrx; break; }
                    }
                    if (FAILED(setHr)) {
                        // 节点级重试（InstanceData=&nodeId，同样做长度扫描）
                        // 优先使用由 IKsPropertySet 拿到的 topology node（若能获取），否则回退到常见 nodeId=1
                        ULONG discoveredNode = (ULONG)-1;
                        {
                            BYTE topoBuf[4096] = {0};
                            ULONG topoBytes = 0;
                            HRESULT topHr = pKs->lpVtbl->Get(pKs, &KSPROPSETID_Topology, KSPROPERTY_TOPOLOGY_NODES,
                                                            nullptr, 0, topoBuf, sizeof(topoBuf), &topoBytes);
                            if (SUCCEEDED(topHr) && topoBytes >= 8) {
                                ULONG numNodes = *(ULONG*)(topoBuf + 4);
                                if (topoBytes >= 8 + numNodes * (ULONG)sizeof(GUID)) {
                                    GUID* nodes = (GUID*)(topoBuf + 8);
                                    for (ULONG ni = 0; ni < numNodes; ni++) {
                                        if (IsEqualGUID(nodes[ni], KSNODETYPE_VIDEO_PROCESSING)) {
                                            discoveredNode = ni; break;
                                        }
                                    }
                                    if (discoveredNode == (ULONG)-1) {
                                        for (ULONG ni = 0; ni < numNodes; ni++) {
                                            if (IsEqualGUID(nodes[ni], CUSTOM_NODE_VIDEO_PROCESSING)) { discoveredNode = ni; break; }
                                        }
                                    }
                                    if (discoveredNode != (ULONG)-1) {
                                        DbgLog("IAM probe: IKs topology discovered nodeId=%lu (numNodes=%lu)", discoveredNode, numNodes);
                                    }
                                }
                            }
                        }
                        static const ULONG nodeSizes[] = { 36, 64, 128, 256 };
                        for (int si = 0; si < (int)(sizeof(nodeSizes) / sizeof(nodeSizes[0])); si++) {
                            BYTE buf[256];
                            ZeroMemory(buf, sizeof(buf));
                            KSPROPERTY_VIDEOPROCAMP_S* ss = (KSPROPERTY_VIDEOPROCAMP_S*)buf;
                            ss->Property.Set = PROPSETID_VIDCAP_VIDEOPROCAMP;
                            ss->Property.Id  = 0;
                            ss->Property.Flags = KSPROPERTY_TYPE_SET;
                            ss->Value = 128;
                            ss->Flags = KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
                            ss->Capabilities = 0;
                            // 构建候选节点列表：优先 discoveredNode，再尝试常见 nodeId=1
                            ULONG candidates[2]; int candCount = 0;
                            if (discoveredNode != (ULONG)-1) candidates[candCount++] = discoveredNode;
                            candidates[candCount++] = 1;
                            for (int ci = 0; ci < candCount; ci++) {
                                ULONG nodeId = candidates[ci];
                                HRESULT hrx = pKs->lpVtbl->Set(pKs, &PROPSETID_VIDCAP_VIDEOPROCAMP, 0,
                                                               &nodeId, sizeof(nodeId), buf, nodeSizes[si]);
                                DbgLog("IAM probe: IKs Set(len=%lu, value=128, node=%lu) hr=0x%08lX",
                                       (unsigned long)nodeSizes[si], (unsigned long)nodeId, (unsigned long)hrx);
                                if (SUCCEEDED(hrx)) { setHr = hrx; break; }
                            }
                            if (SUCCEEDED(setHr)) break;
                        }
                    }
                    if (SUCCEEDED(setHr)) {
                        DbgLog("IAM probe: IKs Set OK → IKsPropertySet 写通道可用");
                        ok = true;
                        *ppKs = pKs; // 保留实例供 DoSet/DoGet 回退；DoCleanup 释放
                        if (pKsControl) { pKsControl->lpVtbl->Release(pKsControl); pKsControl = nullptr; }
                        break;
                    }
                }
                // 探测失败也保留 pKs：用户调节（streaming 后）时可能成功，DoSet/DoGet 延迟重试
            }

            // ---- IKsControl 探测（AMCap 属性页底层通道）----
            // KsProperty 与 IOCTL_KS_PROPERTY 一一对应，Property 指向 KSPROPERTY_VIDEOPROCAMP_S
            // 的头部（含完整结构），PropertyData 为同一缓冲区——即"组合缓冲区"模式。
            if (pKsControl) {
                g_iamStage = 11;

                // GET（组合缓冲：Property 指向结构头，长度给整个结构）
                KSPROPERTY_VIDEOPROCAMP_S ks;
                ZeroMemory(&ks, sizeof(ks));
                ks.Property.Set = PROPSETID_VIDCAP_VIDEOPROCAMP;
                ks.Property.Id  = 0;
                ks.Property.Flags = KSPROPERTY_TYPE_GET;
                ks.Value = 0;
                ks.Flags = KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
                ks.Capabilities = 0;
                ULONG bytes = 0;
                hr = pKsControl->lpVtbl->KsProperty(pKsControl, &ks.Property, sizeof(ks),
                                                     &ks, sizeof(ks), &bytes);
                DbgLog("IAM probe: IKsControl GET hr=0x%08lX value=%ld flags=0x%lX bytes=%lu",
                       (unsigned long)hr, ks.Value, (unsigned long)ks.Flags, bytes);

                // 变体：PropertyLength 只给 KSPROPERTY 头(24)，数据用独立缓冲区
                if (FAILED(hr)) {
                    KSPROPERTY_VIDEOPROCAMP_S ks2;
                    ZeroMemory(&ks2, sizeof(ks2));
                    ks2.Property.Set = PROPSETID_VIDCAP_VIDEOPROCAMP;
                    ks2.Property.Id  = 0;
                    ks2.Property.Flags = KSPROPERTY_TYPE_GET;
                    ULONG cb2 = 0;
                    HRESULT hr2 = pKsControl->lpVtbl->KsProperty(pKsControl, &ks2.Property, sizeof(KSPROPERTY),
                                                                 &ks2, sizeof(ks2), &cb2);
                    DbgLog("IAM probe: IKsControl GET(header-only) hr=0x%08lX value=%ld bytes=%lu",
                           (unsigned long)hr2, ks2.Value, cb2);
                    if (SUCCEEDED(hr2)) { hr = hr2; ks = ks2; bytes = cb2; }
                }

                // SET（原值回写 128，无副作用）
                KSPROPERTY_VIDEOPROCAMP_S ss;
                ZeroMemory(&ss, sizeof(ss));
                ss.Property.Set = PROPSETID_VIDCAP_VIDEOPROCAMP;
                ss.Property.Id  = 0;
                ss.Property.Flags = KSPROPERTY_TYPE_SET;
                ss.Value = 128;
                ss.Flags = KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
                ss.Capabilities = 0;
                HRESULT setHr = pKsControl->lpVtbl->KsProperty(pKsControl, &ss.Property, sizeof(ss),
                                                               &ss, sizeof(ss), &bytes);
                DbgLog("IAM probe: IKsControl SET(value=128) hr=0x%08lX", (unsigned long)setHr);
                if (FAILED(setHr)) {
                    // 变体：PropertyLength 只给头
                    KSPROPERTY_VIDEOPROCAMP_S ss2 = ss;
                    setHr = pKsControl->lpVtbl->KsProperty(pKsControl, &ss2.Property, sizeof(KSPROPERTY),
                                                           &ss2, sizeof(ss2), &bytes);
                    DbgLog("IAM probe: IKsControl SET(header-only) hr=0x%08lX", (unsigned long)setHr);
                }

                // 如果普通 SET 都失败且可以访问 IKsPropertySet，则尝试节点级 KsProperty（包含 NodeId 的节点结构）
                if (FAILED(setHr) && pKs) {
                    ULONG discoveredNode = (ULONG)-1;
                    BYTE topoBuf[4096] = {0}; ULONG topoBytes = 0;
                    HRESULT topHr = pKs->lpVtbl->Get(pKs, &KSPROPSETID_Topology, KSPROPERTY_TOPOLOGY_NODES,
                                                    nullptr, 0, topoBuf, sizeof(topoBuf), &topoBytes);
                    if (SUCCEEDED(topHr) && topoBytes >= 8) {
                        ULONG numNodes = *(ULONG*)(topoBuf + 4);
                        if (topoBytes >= 8 + numNodes * (ULONG)sizeof(GUID)) {
                            GUID* nodes = (GUID*)(topoBuf + 8);
                            for (ULONG ni = 0; ni < numNodes; ni++) {
                                if (IsEqualGUID(nodes[ni], KSNODETYPE_VIDEO_PROCESSING)) { discoveredNode = ni; break; }
                            }
                            if (discoveredNode == (ULONG)-1) {
                                for (ULONG ni = 0; ni < numNodes; ni++) {
                                    if (IsEqualGUID(nodes[ni], CUSTOM_NODE_VIDEO_PROCESSING)) { discoveredNode = ni; break; }
                                }
                            }
                            if (discoveredNode != (ULONG)-1) DbgLog("IAM probe: IKsControl topology discovered nodeId=%lu (numNodes=%lu)", discoveredNode, numNodes);
                        }
                    }

                    // 构造节点级结构并尝试节点级 KsProperty
                    if (discoveredNode != (ULONG)-1) {
                        KSPROPERTY_VIDEOPROCAMP_NODE_S nfull;
                        ZeroMemory(&nfull, sizeof(nfull));
                        nfull.NodeProperty.Property.Set = PROPSETID_VIDCAP_VIDEOPROCAMP;
                        nfull.NodeProperty.Property.Id = 0;
                        nfull.NodeProperty.Property.Flags = KSPROPERTY_TYPE_SET | KSPROPERTY_TYPE_TOPOLOGY;
                        nfull.NodeProperty.NodeId = discoveredNode;
                        nfull.NodeProperty.Reserved = 0;
                        nfull.Value = 128;
                        nfull.Flags = KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
                        nfull.Capabilities = 0;
                        ULONG nb = 0;
                        HRESULT hr2 = pKsControl->lpVtbl->KsProperty(pKsControl, (KSPROPERTY*)&nfull.NodeProperty, sizeof(nfull.NodeProperty),
                                                                      &nfull, sizeof(nfull), &nb);
                        DbgLog("IAM probe: IKsControl node-level SET node=%lu hr=0x%08lX bytes=%lu", discoveredNode, (unsigned long)hr2, nb);
                        if (SUCCEEDED(hr2)) setHr = hr2;
                    }

                    // 若仍然失败，兜底尝试常见 nodeId=1
                    if (FAILED(setHr)) {
                        KSPROPERTY_VIDEOPROCAMP_NODE_S nfull2;
                        ZeroMemory(&nfull2, sizeof(nfull2));
                        nfull2.NodeProperty.Property.Set = PROPSETID_VIDCAP_VIDEOPROCAMP;
                        nfull2.NodeProperty.Property.Id = 0;
                        nfull2.NodeProperty.Property.Flags = KSPROPERTY_TYPE_SET | KSPROPERTY_TYPE_TOPOLOGY;
                        nfull2.NodeProperty.NodeId = 1;
                        nfull2.NodeProperty.Reserved = 0;
                        nfull2.Value = 128;
                        nfull2.Flags = KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
                        nfull2.Capabilities = 0;
                        ULONG nb2 = 0;
                        HRESULT hr3 = pKsControl->lpVtbl->KsProperty(pKsControl, (KSPROPERTY*)&nfull2.NodeProperty, sizeof(nfull2.NodeProperty),
                                                                      &nfull2, sizeof(nfull2), &nb2);
                        DbgLog("IAM probe: IKsControl node-level SET node=1 hr=0x%08lX bytes=%lu", (unsigned long)hr3, nb2);
                        if (SUCCEEDED(hr3)) setHr = hr3;
                    }
                }

                if (SUCCEEDED(setHr)) {
                    DbgLog("IAM probe: IKsControl SET OK → IKsControl 写通道可用");
                    ok = true;
                    *ppKsControl = pKsControl; // 保留实例供 DoSet/DoGet 回退；DoCleanup 释放
                    break;
                }
                // 探测失败也保留 pKsControl：用户调节（streaming 后）时可能成功，DoSet/DoGet 延迟重试
            }
            break;
        }
        if (pEnum) { pEnum->lpVtbl->Release(pEnum); pEnum = nullptr; }
    done:
        // 无论探测是否成功，把已获得的接口全部保留给 DoSet/DoGet 延迟重试
        // （用户实际调节时 streaming 可能已激活，写操作可能成功）。DoCleanup 统一释放。
        if (pIam) { *ppIam = pIam; pIam = nullptr; }
        if (pCam) { *ppCam = pCam; pCam = nullptr; }
        if (pKs) { *ppKs = pKs; pKs = nullptr; }
        if (pKsControl) { *ppKsControl = pKsControl; pKsControl = nullptr; }
        return ok ? 1 : 0;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        DbgLog("IAM probe: SEH 捕获访问违例 code=0x%08X stage=%d（devenum/KS 组件异常，回退只读）",
               (unsigned)GetExceptionCode(), g_iamStage);
        // SEH 跳转不执行 done 标签的释放代码：释放崩溃前已获取的临时接口，
        // 避免 COM 对象泄漏。pIam/pCam/pKs/pKsControl 有意保留——探测期间崩
        // 溃时其状态不可信，统一由 DoCleanup 释放（本函数外）。
        if (pEnumFactory) { pEnumFactory->lpVtbl->Release(pEnumFactory); pEnumFactory = nullptr; }
        if (pEnum)        { pEnum->lpVtbl->Release(pEnum); pEnum = nullptr; }
        if (pMoniker)     { pMoniker->lpVtbl->Release(pMoniker); pMoniker = nullptr; }
        if (pBag)         { pBag->lpVtbl->Release(pBag); pBag = nullptr; }
        if (pFilter)      { pFilter->lpVtbl->Release(pFilter); pFilter = nullptr; }
        if (pbc)          { ((IUnknown*)pbc)->lpVtbl->Release((IUnknown*)pbc); pbc = nullptr; }
        if (var.bstrVal)  { SysFreeString(var.bstrVal); var.bstrVal = nullptr; }
        return -1;
    }
}

// ===================== 分辨率枚举 (IAMStreamConfig) =====================
//
// 与 IamProbeRaw 同架构：devenum 枚举视频输入设备 → 按 FriendlyName 匹配 → 绑定捕获
// 过滤器 → EnumPins 遍历输出 pin → QI IAMStreamConfig → GetStreamCaps 循环枚举
// 全部支持格式（分辨率 + 像素格式 FOURCC）。整体 SEH 保护（本设备 devenum 已知访问
// 违例），结果按分辨率升序、去重后写入 outBuf。纯 POD，__try 内无 C++ 析构对象。
// 返回：>=0 格式数量；-1 探测失败/SEH 捕获。
//
// 实测（usbvideo 仿真层）：GetStreamCaps 调用会写越界破坏调用者栈帧（cap#0 调用后
// 调用方局部 pCfg 被覆盖为 0x438），且返回缓冲非 CoTaskMem 内存（释放即堆损坏）。
// 因此枚举循环必须放入独立函数帧（越界写只损坏本帧局部，返回即丢弃），每轮调用前
// 重新 QI 接口，所有环节逐段 SEH 保护，缓冲一律不释放。

// 解析单个 AM_MEDIA_TYPE 为 VideoFormatInfo（去重 + 按宽*高升序插入 outBuf）。
// 解析段独立 SEH：pmt 可能被仿真层写坏，读 pbFormat 可能访问违例。
// 返回更新后的格式数量。
static int ParseMediaType(AM_MEDIA_TYPE* pmt, VideoFormatInfo* outBuf, int count, int outCap)
{
    if (!pmt || count >= outCap) return count;
    // 实测仿真层可能返回非法指针（如 0x1/0x438）：明显非法的低地址直接跳过
    if ((ULONG_PTR)pmt < 0x10000) {
        DbgLog("EnumFormats: pmt 非法指针 0x%p，跳过", (void*)pmt);
        return count;
    }
    __try {
        if (pmt->pbFormat && pmt->cbFormat >= 40) {
            int bmiOff = 0;
            if (memcmp(&pmt->formattype, &FORMAT_VideoInfo, sizeof(GUID)) == 0)
                bmiOff = VIH_OFFSET_VIDEOINFO;
            else if (memcmp(&pmt->formattype, &FORMAT_VideoInfo2, sizeof(GUID)) == 0)
                bmiOff = VIH_OFFSET_VIDEOINFO2;
            if (bmiOff > 0 && pmt->cbFormat >= (ULONG)(bmiOff + 40)) {
                BYTE* bmi = pmt->pbFormat + bmiOff;
                LONG w = *(LONG*)(bmi + 4);  // biWidth
                LONG h = *(LONG*)(bmi + 8);  // biHeight
                DWORD comp = *(DWORD*)(bmi + 16); // biCompression（FOURCC）
                DWORD fourcc = comp ? comp : pmt->subtype.Data1;
                // 帧率：VIDEOINFOHEADER 与 VIDEOINFOHEADER2 中 AvgTimePerFrame 均位于
                // bmiHeader 之前 8 字节（pbFormat + bmiOff - 8），单位 100ns。
                // 换算 fps = 10^7 / AvgTimePerFrame（0/负值 → 0 = 未知）
                double fps = 0.0;
                if (pmt->cbFormat >= (ULONG)(bmiOff + 8)) {
                    LONGLONG atpf = *(LONGLONG*)(pmt->pbFormat + bmiOff - 8);
                    if (atpf > 0)
                        fps = 10000000.0 / (double)atpf;
                }
                DbgLog("EnumFormats: %dx%d fourcc=0x%08X fps=%.2f bmiOff=%d cb=%u",
                       (int)w, (int)h, (unsigned)fourcc, fps, bmiOff, (unsigned)pmt->cbFormat);
                if (w > 0 && h > 0) {
                    bool dup = false;
                    for (int k = 0; k < count; k++) {
                        if (outBuf[k].width == (int)w && outBuf[k].height == (int)h &&
                            outBuf[k].pixelFormat == (int)fourcc) { dup = true; break; }
                    }
                    if (!dup) {
                        // 插入排序：按宽*高升序
                        int ins = count;
                        while (ins > 0 &&
                               (long)outBuf[ins - 1].width * outBuf[ins - 1].height > (long)w * h)
                            ins--;
                        if (ins < count)
                            memmove(&outBuf[ins + 1], &outBuf[ins],
                                    (count - ins) * sizeof(VideoFormatInfo));
                        outBuf[ins].width = (int)w;
                        outBuf[ins].height = (int)h;
                        outBuf[ins].pixelFormat = (int)fourcc;
                        outBuf[ins].fps = fps;
                        count++;
                    }
                }
            }
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        DbgLog("EnumFormats: 解析异常 code=0x%08X，跳过", (unsigned)GetExceptionCode());
    }
    return count;
}

// ============ IAMStreamConfig 枚举已禁用（实测不可安全调用） ============
// 本设备（usbvideo 仿真层）IAMStreamConfig 在未连接状态下：
//   - GetNumberOfCapabilities：返回 10/128，但写坏调用者帧（outCap 变 0），
//     且返回地址被覆盖 → 函数返回后跳 0x00000001 执行 → 0xC0000005。
//   - GetStreamCaps：同源写坏（此前 3 参数/4 参数/独立帧/SEH/不释放全试过，
//     循环变量 i→1920、count→64、pmt→0x1 等，最终波及返回地址）。
// SEH 无法防御返回地址破坏，故不再调用这两个方法。
// 唯一实证可用：GetFormat（1 参数，返回当前格式）与
// ConnectionMediaType（1 参数，调用方栈缓冲）。完整支持列表在本仿真层
// 未连接状态下无法通过 IAMStreamConfig 获得（参考工具在已连接状态下可用）。

// 安全通道：获取设备当前格式（≤ 1 条）。
// 路径 1：IPin::ConnectionMediaType（调用方提供 72B 栈缓冲，无分配、无释放，最安全）
// 路径 2：IAMStreamConfig::GetFormat（1 参数，实现分配缓冲，不释放）
// 两路径独立 SEH；返回解析到的格式数量（0 或 1）。
#pragma runtime_checks("s", off)
static __declspec(safebuffers) int TryGetCurrentFormat(IPin* pPin, IAMStreamConfig* pCfg,
                                                       VideoFormatInfo* outBuf, int outCap)
{
    if (outCap <= 0) return 0;
    int count = 0;

    // 路径 1：ConnectionMediaType（输出 AM_MEDIA_TYPE 结构到调用方栈缓冲）
    if (pPin) {
        __try {
            AM_MEDIA_TYPE mt;
            ZeroMemory(&mt, sizeof(mt));
            if (SUCCEEDED(pPin->lpVtbl->ConnectionMediaType(pPin, &mt))) {
                count = ParseMediaType(&mt, outBuf, count, outCap);
                if (count > 0) {
                    DbgLog("EnumFormats: 经 ConnectionMediaType 获得当前格式");
                    return count;
                }
            }
        }
        __except (EXCEPTION_EXECUTE_HANDLER) {
            DbgLog("EnumFormats: ConnectionMediaType 异常 code=0x%08X，尝试 GetFormat",
                   (unsigned)GetExceptionCode());
        }
    }

    // 路径 2：GetFormat（实现分配 AM_MEDIA_TYPE，调用方只读，不释放）
    if (pCfg) {
        __try {
            AM_MEDIA_TYPE* pmt = nullptr;
            if (SUCCEEDED(pCfg->lpVtbl->GetFormat(pCfg, (void**)&pmt)) && pmt) {
                count = ParseMediaType(pmt, outBuf, count, outCap);
                if (count > 0) {
                    DbgLog("EnumFormats: 经 GetFormat 获得当前格式");
                    return count;
                }
            }
        }
        __except (EXCEPTION_EXECUTE_HANDLER) {
            DbgLog("EnumFormats: GetFormat 异常 code=0x%08X", (unsigned)GetExceptionCode());
        }
    }

    return count;
}

// 该函数帧可能被 GetStreamCaps 仿真层的栈写越界波及（实测 pCfg 局部槽被覆盖），
// 关闭本函数的栈金丝雀（RTC #2）检查：写坏的只是局部槽，返回地址安全，且用后
// 即弃（不 Release），不影响正确性。RTC pragma 已由上方 EnumCapsLoop 统一开启。
static __declspec(safebuffers) int EnumFormatsRaw(const wchar_t* devName, VideoFormatInfo* outBuf, int outCap)
{
    HRESULT hr = S_OK;
    int count = 0;
    // 全部局部在函数顶部初始化（__except 处理器也要访问；SEH 跳转不执行 __try 内初始化）
    ICreateDevEnum* pEnumFactory = nullptr;
    IEnumMoniker* pEnum = nullptr;
    IMoniker* pMoniker = nullptr;
    IPropertyBag* pBag = nullptr;
    IUnknown* pFilter = nullptr;
    void* pbc = nullptr;
    VARIANT var; ZeroMemory(&var, sizeof(var));
    wchar_t fb[512] = {0};
    ULONG fetched = 0;
    BOOL nameOk = FALSE;
    DWORD monIndex = 0;
    IEnumPins* pPins = nullptr;
    IPin* pPin = nullptr;
    IAMStreamConfig* pCfg = nullptr;
    IKsPropertySet* pKsTmp = nullptr;
    // 回退 pin：类别查询失败（无 capture 识别）时，取第一个 QI IAMStreamConfig 成功的
    // 输出 pin 走安全通道（GetFormat/ConnectionMediaType），仅 1 参数简单调用
    IPin* pPinSafe = nullptr;
    IAMStreamConfig* pCfgSafe = nullptr;

    __try {
        g_iamStage = 1;
        hr = CoCreateInstance(&CLSID_SystemDeviceEnum, nullptr, CLSCTX_ALL,
                              &IID_ICreateDevEnum, (void**)&pEnumFactory);
        if (FAILED(hr) || !pEnumFactory) goto done;

        g_iamStage = 2;
        hr = pEnumFactory->lpVtbl->CreateClassEnumerator(pEnumFactory,
                                                         &CLSID_VideoInputDeviceCategory, &pEnum, 0);
        if (FAILED(hr) || !pEnum) goto done;

        while (true) {
            g_iamStage = 3;
            pMoniker = nullptr;
            fetched = 0;
            if (pEnum->lpVtbl->Next(pEnum, 1, &pMoniker, &fetched) != S_OK || !pMoniker) break;
            monIndex++;

            // FriendlyName 读取与匹配（与 IAM 属性探测同款；BindToStorage 可能访问违例）
            nameOk = FALSE;
            fb[0] = 0;
            __try {
                ZeroMemory(&var, sizeof(var));
                g_iamStage = 4;
                pBag = nullptr;
                if (pMoniker->lpVtbl->BindToStorage(pMoniker, nullptr, nullptr,
                                                    &IID_IPropertyBag, (void**)&pBag) == S_OK && pBag) {
                    hr = pBag->lpVtbl->Read(pBag, L"FriendlyName", &var, nullptr);
                    if (SUCCEEDED(hr) && var.vt == VT_BSTR && var.bstrVal) {
                        wcsncpy_s(fb, 512, var.bstrVal, _TRUNCATE);
                        fb[511] = 0;
                        nameOk = TRUE;
                    }
                    if (var.bstrVal) { SysFreeString(var.bstrVal); var.bstrVal = nullptr; }
                    pBag->lpVtbl->Release(pBag); pBag = nullptr;
                }
            }
            __except (EXCEPTION_EXECUTE_HANDLER) {
                DbgLog("EnumFormats: FriendlyName 访问违例，跳过匹配继续探测");
                if (pBag) { pBag->lpVtbl->Release(pBag); pBag = nullptr; }
                if (var.bstrVal) { SysFreeString(var.bstrVal); var.bstrVal = nullptr; }
            }
            if (nameOk && devName && *devName && !RawNameMatch(fb, devName)) {
                pMoniker->lpVtbl->Release(pMoniker);
                pMoniker = nullptr;
                continue;
            }

            // 绑定捕获过滤器
            g_iamStage = 5;
            pFilter = nullptr;
            CreateBindCtx(0, &pbc);
            hr = pMoniker->lpVtbl->BindToObject(pMoniker, (IBindCtx*)pbc, nullptr,
                                                &IID_IBaseFilter, (void**)&pFilter);
            if (pbc) { ((IUnknown*)pbc)->lpVtbl->Release((IUnknown*)pbc); pbc = nullptr; }
            pMoniker->lpVtbl->Release(pMoniker);
            pMoniker = nullptr;
            if (hr != S_OK || !pFilter) continue;

            // 枚举 pin：在输出 pin（PINDIR_OUTPUT=1）上找 IAMStreamConfig
            g_iamStage = 6;
            pPins = nullptr;
            hr = ((IBaseFilter*)pFilter)->lpVtbl->EnumPins((IBaseFilter*)pFilter, (void**)&pPins);
            if (FAILED(hr) || !pPins) {
                pFilter->lpVtbl->Release(pFilter); pFilter = nullptr;
                continue;
            }
            pCfg = nullptr;
            for (;;) {
                pPin = nullptr;
                fetched = 0;
                if (pPins->lpVtbl->Next(pPins, 1, &pPin, &fetched) != S_OK || !pPin) break;
                int dir = 0;
                if (SUCCEEDED(pPin->lpVtbl->QueryDirection(pPin, &dir)) && dir == 1) {
                    // 仅识别 PIN_CATEGORY_CAPTURE 的 capture pin（AMCap 同款）：
                    // 非 capture pin 的 IAMStreamConfig 为占位实现（布局非标准），
                    // 直接调用会导致 RTC #0/栈写越界/返回地址破坏
                    BOOL isCapture = FALSE;
                    HRESULT hrCat = E_FAIL;
                    __try {
                        GUID cat;
                        DWORD cbRet = 0;
                        hrCat = pPin->lpVtbl->QueryInterface(pPin, &IID_IKsPropertySet, (void**)&pKsTmp);
                        if (hrCat == S_OK && pKsTmp) {
                            ZeroMemory(&cat, sizeof(cat));
                            hrCat = pKsTmp->lpVtbl->Get(pKsTmp, &KSPROPSETID_Pin, KSPROPERTY_PIN_CATEGORY,
                                                        nullptr, 0, &cat, sizeof(cat), &cbRet);
                            if (SUCCEEDED(hrCat) && cbRet == sizeof(cat) &&
                                memcmp(&cat, &PIN_CATEGORY_CAPTURE, sizeof(GUID)) == 0) {
                                isCapture = TRUE;
                            }
                            pKsTmp->lpVtbl->Release(pKsTmp);
                            pKsTmp = nullptr;
                        }
                    }
                    __except (EXCEPTION_EXECUTE_HANDLER) {
                        DbgLog("EnumFormats: pin 类别查询异常 code=0x%08X",
                               (unsigned)GetExceptionCode());
                        hrCat = E_FAIL;
                    }
                    DbgLog("EnumFormats: 输出 pin #%u isCapture=%d catHr=0x%08lX",
                           (unsigned)fetched, isCapture ? 1 : 0, (unsigned long)hrCat);
                    if (isCapture) {
                        pCfg = nullptr;
                        hr = pPin->lpVtbl->QueryInterface(pPin, &IID_IAMStreamConfig, (void**)&pCfg);
                        if (SUCCEEDED(hr) && pCfg) break; // 找到 capture pin 配置接口，保留 pPin
                    }
                    else {
                        // 回退候选：记录第一个 QI IAMStreamConfig 成功的输出 pin
                        // （不 Release，用后即弃策略；泄漏 1 个引用可接受）
                        if (!pCfgSafe) {
                            pCfgSafe = nullptr;
                            hr = pPin->lpVtbl->QueryInterface(pPin, &IID_IAMStreamConfig, (void**)&pCfgSafe);
                            if (SUCCEEDED(hr) && pCfgSafe) {
                                pPinSafe = pPin;
                                DbgLog("EnumFormats: pin #%u 记为回退通道", (unsigned)fetched);
                                continue; // 保留 pPinSafe，不释放
                            }
                        }
                    }
                }
                pPin->lpVtbl->Release(pPin);
                pPin = nullptr;
            }
            pPins->lpVtbl->Release(pPins); pPins = nullptr;

            // 仅安全通道（实证可用）：GetFormat/ConnectionMediaType 取当前格式。
            // GetNumberOfCapabilities/GetStreamCaps 在本仿真层未连接状态下
            // 会写坏调用者帧（含返回地址，跳 0x1 崩溃），一律不调用。
            if (pCfgSafe) {
                g_iamStage = 8;
                count += TryGetCurrentFormat(pPinSafe, pCfgSafe, outBuf + count, outCap - count);
            }
            if (count == 0)
                DbgLog("EnumFormats: 设备 #%u 未能枚举任何格式", monIndex);

            // 正常路径：本设备唯一可安全调用的 IAMStreamConfig 方法为 GetFormat
            // （1 参数，实证不写坏调用者帧），调用后局部槽可信，按序释放引用
            // （pPins 已在上方释放），保持引用计数平衡、无累积泄漏。
            // 注意：异常路径（外层 __except）不释放——仿真层可能已写坏局部槽，
            // Release 垃圾指针会二次崩溃，交由进程退出回收。
            if (pCfgSafe) { pCfgSafe->lpVtbl->Release(pCfgSafe); pCfgSafe = nullptr; }
            if (pPinSafe) { pPinSafe->lpVtbl->Release(pPinSafe); pPinSafe = nullptr; }
            if (pCfg)     { pCfg->lpVtbl->Release(pCfg);     pCfg = nullptr; }
            if (pPin)     { pPin->lpVtbl->Release(pPin);     pPin = nullptr; }
            if (pFilter)  { pFilter->lpVtbl->Release(pFilter); pFilter = nullptr; }
            break; // 目标设备已处理完毕
        }
    done:
        if (pEnum) { pEnum->lpVtbl->Release(pEnum); pEnum = nullptr; }
        if (pEnumFactory) { pEnumFactory->lpVtbl->Release(pEnumFactory); pEnumFactory = nullptr; }
        DbgLog("EnumFormats: 完成，共 %d 种格式", count);
        return count;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        DbgLog("EnumFormats: SEH 捕获访问违例 code=0x%08X stage=%d",
               (unsigned)GetExceptionCode(), g_iamStage);
        // 局部槽可能已被仿真层写坏，Release 垃圾指针会二次崩溃：
        // 一律不释放，全部交由进程退出回收（泄漏量级极小）
        return -1;
    }
}
#pragma runtime_checks("s", restore)

// 分辨率枚举入口（工作线程执行）：UTF-8 设备名 → 宽字符（剥 "video=" 前缀，
// 与 DoInit 同款校验），再交 EnumFormatsRaw 完成 COM 探测。
// 返回：>=0 格式数量；-1 参数非法/设备名无效/探测失败。
static int DoEnumFormats(const char* deviceName, VideoFormatInfo* outBuf, int outCap)
{
    if (!deviceName || !outBuf || outCap <= 0) return -1;

    std::wstring devNameW;
    int wlen = MultiByteToWideChar(CP_UTF8, 0, deviceName, -1, nullptr, 0);
    if (wlen <= 0) {
        DbgLog("EnumFormats: 设备名非法/无法解码（wlen=%d），拒绝枚举", wlen);
        return -1;
    }
    {
        std::vector<wchar_t> buf(wlen);
        MultiByteToWideChar(CP_UTF8, 0, deviceName, -1, buf.data(), wlen);
        devNameW = buf.data();
    }
    const std::wstring prefix = L"video=";
    if (devNameW.size() >= prefix.size() &&
        _wcsnicmp(devNameW.c_str(), prefix.c_str(), prefix.size()) == 0) {
        devNameW = devNameW.substr(prefix.size());
    }
    if (devNameW.empty()) {
        DbgLog("EnumFormats: 设备名为空，拒绝枚举");
        return -1;
    }

    return EnumFormatsRaw(devNameW.c_str(), outBuf, outCap);
}

// 探测两套 DirectShow 控制接口（AMCap 同款通道）：
// 图像属性 IAMVideoProcAmp + 相机控制 IAMCameraControl + 共享的 IKsPropertySet/IKsControl。
static bool ProbeIamControls(const std::wstring& devName)
{
// IMoniker::BindToObject 属于 OLE，必须用 OleInitialize（内部完成 STA COM 初始化）——
    // 仅 CoInitializeEx 时 BindToObject 实测返回 S_FALSE，拿不到过滤器。
    // 审查项 R7：STA 线程无消息泵。本线程所有 COM 调用均为"同线程直接调用"
    // （不经跨线程封送），不依赖消息泵；若未来接入需要消息泵的组件（如
    // FilterGraph 建图连接），需改用 MsgWaitForMultipleObjects 补消息循环。
    HRESULT hr = OleInitialize(nullptr);
    if (hr == RPC_E_CHANGED_MODE) {
        DbgLog("IAM probe: 线程 COM 模式冲突，跳过探测");
        return false;
    }
    if (FAILED(hr)) {
        DbgLog("IAM probe: OleInitialize FAIL hr=0x%08lX", (unsigned long)hr);
        return false;
    }
    g_comInitedByUs = true;

    IAMVideoProcAmp* pIam = nullptr;
    IAMCameraControl* pCam = nullptr;
    IKsPropertySet* pKs = nullptr;
    IKsControl* pKsControl = nullptr;
    int rc = IamProbeRaw(devName.c_str(), &pIam, &pCam, &pKs, &pKsControl);
    // 无论探测是否成功，只要拿到了接口就保留，供 DoSet/DoGet 在用户调节
    // （预览/streaming 可能已激活）时延迟重试；DoCleanup 统一释放并 OleUninitialize。
    g_pIam = pIam;
    g_pCam = pCam;
    g_pKsPropSet = pKs;
    g_pKsControl = pKsControl;
    if (g_pIam || g_pCam || g_pKsPropSet || g_pKsControl)
        return true;
    if (rc == -1)
        DbgLog("DoInit: IAM 探测因访问违例中止 → 属性维持只读");
    if (g_comInitedByUs) {
        // 探测失败且未保留实例，释放 OLE
        OleUninitialize();
        g_comInitedByUs = false;
    }
    return false;
}

// ===================== USB / UVC 描述符解析（诊断用） =====================
// 用于确认设备固件是否真正实现了 VideoProcAmp（Processing Unit）的写入能力。
// 方法：枚举 USB hub → 按 VID/PID 匹配设备 → 读取配置描述符 → 解析 VideoControl
// 接口中的 Processing Unit 描述符，输出 bmControls 位图（每个控制的 GET/SET 位）。

// USB 描述符类型
#define USB_DESCRIPTOR_TYPE_DEVICE        0x01
#define USB_DESCRIPTOR_TYPE_CONFIGURATION 0x02
#define USB_DESCRIPTOR_TYPE_INTERFACE     0x04
#define USB_DESCRIPTOR_TYPE_ENDPOINT      0x05

// USB Video Class（UVC）
#define USB_CC_VIDEO            0x0E   // bInterfaceClass = 视频类
#define USB_VSC_VIDEO_CONTROL   0x01   // VideoControl 接口子类
#define USB_VSC_VIDEO_STREAMING 0x02   // VideoStreaming 接口子类

// UVC 类接口描述符（bDescriptorType = 0x24, CS_INTERFACE）
#define USB_CS_INTERFACE_DESCRIPTOR_TYPE 0x24
#define VC_HEADER           0x01
#define VC_INPUT_TERMINAL   0x02
#define VC_OUTPUT_TERMINAL  0x03
#define VC_SELECTOR_UNIT    0x04
#define VC_PROCESSING_UNIT  0x05
#define VC_EXTENSION_UNIT   0x06

// usbioctl.h IOCTL（FILE_DEVICE_USB=0x22, METHOD_BUFFERED, FILE_ANY_ACCESS）
#define IOCTL_USB_GET_NODE_INFORMATION                0x00220010UL // func 4
#define IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX  0x0022001CUL // func 7
#define IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION 0x00220040UL // func 16

// 精简 USB 结构（与 usb.h/usbioctl.h 一致，WIN32_LEAN_AND_MEAN 下需自包含）
typedef struct {
    UCHAR  bLength;
    UCHAR  bDescriptorType;
    USHORT bcdUSB;
    UCHAR  bDeviceClass;
    UCHAR  bDeviceSubClass;
    UCHAR  bDeviceProtocol;
    UCHAR  bMaxPacketSize0;
    USHORT idVendor;
    USHORT idProduct;
    USHORT bcdDevice;
    UCHAR  iManufacturer;
    UCHAR  iProduct;
    UCHAR  iSerialNumber;
    UCHAR  bNumConfigurations;
} UVC_USB_DEVICE_DESCRIPTOR;   // 18 字节

typedef struct {
    ULONG ConnectionIndex;
    struct {
        UCHAR  bmRequest;
        UCHAR  bDescriptorType;
        USHORT wIndex;
        USHORT wLength;
    } Data;
} UVC_USB_DESCRIPTOR_REQUEST;  // 10 字节

// USB_NODE_CONNECTION_INFORMATION_EX（前部，PipeList 可变部分忽略）
typedef struct {
    ULONG ConnectionIndex;
    UVC_USB_DEVICE_DESCRIPTOR DeviceDescriptor;
    UCHAR CurrentConfigurationValue;
    UCHAR Speed;
    BOOLEAN DeviceIsHub;
    USHORT DeviceAddress;
    ULONG NumberOfOpenPipes;
} UVC_USB_NODE_CONNECTION_INFORMATION_EX;

// 从 KS 设备路径提取 VID/PID（路径形如 \\?\usb#vid_XXXX&pid_YYYY#...）
static bool ParseUsbVidPid(const wchar_t* path, USHORT& vid, USHORT& pid)
{
    if (!path) return false;
    const wchar_t* v = wcsstr(path, L"vid_");
    const wchar_t* p = wcsstr(path, L"pid_");
    if (!v || !p) return false;
    vid = (USHORT)wcstoul(v + 4, nullptr, 16);
    pid = (USHORT)wcstoul(p + 4, nullptr, 16);
    return true;
}

// 输出 PU 的 bmControls 位图：每个控制占 2 位（bit0=GET, bit1=SET），
// 控制 i 的 GET/SET 位位于第 i*2 / i*2+1 位（位图按字节小端排列）。
static void DumpUvcBmControls(const BYTE* bm, UCHAR ctrlSize)
{
    static const struct { int ctrl; const char* name; } table[] = {
        { 0, "Brightness 亮度" },
        { 1, "Contrast 对比度" },
        { 2, "Hue 色调" },
        { 3, "Saturation 饱和度" },
        { 4, "Sharpness 锐度" },
        { 5, "Gamma 伽马" },
        { 6, "WhiteBalance 白平衡" },
        { 7, "BacklightComp 背光补偿" },
        { 8, "Gain 增益" },
        { 9, "PowerLineFreq 电源频率" },
    };
    int bitCount = (int)ctrlSize * 8;
    for (size_t i = 0; i < sizeof(table) / sizeof(table[0]); i++) {
        int bit = table[i].ctrl * 2;
        int get = (bit < bitCount) ? ((bm[bit / 8] >> (bit % 8)) & 1) : 0;
        int set = (bit + 1 < bitCount) ? ((bm[(bit + 1) / 8] >> ((bit + 1) % 8)) & 1) : 0;
        DbgLog("UVC-PU:   [%s] GET=%d SET=%d%s", table[i].name, get, set,
               set ? "  <-- 固件声明支持写入" : "");
    }
    char hex[256] = { 0 }; int off = 0;
    for (UCHAR i = 0; i < ctrlSize && i < 32; i++) off += sprintf(hex + off, "%02X ", bm[i]);
    DbgLog("UVC-PU:   bmControls hex: %s", hex);
}

// 在配置描述符中查找 VideoControl 接口的 Processing Unit 描述符。
// 全部用字节偏移访问，避免对齐问题。
static void ParseUvcConfigForPu(const BYTE* p, DWORD len)
{
    if (len < 9) { DbgLog("UVC-PU: 配置描述符过短 len=%u", len); return; }
    USHORT total = (USHORT)(p[2] | (p[3] << 8));  // wTotalLength
    UCHAR numIf = p[4];
    if (total > len) total = (USHORT)len;
    DbgLog("UVC-PU: 配置描述符 wTotalLength=%u bNumInterfaces=%u", total, numIf);

    bool inVC = false;
    const BYTE* q = p;
    const BYTE* end = p + total;
    while (q + 2 <= end) {
        UCHAR dl = q[0], dt = q[1];
        if (dl < 2) break;
        if (dt == USB_DESCRIPTOR_TYPE_INTERFACE) {
            // 接口描述符: bInterfaceClass @5, bInterfaceSubClass @6
            inVC = (q[5] == USB_CC_VIDEO && q[6] == USB_VSC_VIDEO_CONTROL);
        } else if (dt == USB_CS_INTERFACE_DESCRIPTOR_TYPE && inVC) {
            UCHAR sub = q[2];
            if (sub == VC_PROCESSING_UNIT) {
                // PU 布局: bLength(0) type(1) sub(2) bUnitID(3) bSourceID(4)
                //         wMaxMultiplier(5-6) bControlSize(7) bmControls(8..)
                UCHAR ctrlSize = q[7];
                DbgLog("UVC-PU: 找到 Processing Unit (bUnitID=%u bSourceID=%u bControlSize=%u)",
                       q[3], q[4], ctrlSize);
                DumpUvcBmControls(q + 8, ctrlSize);
                return;
            }
        }
        q += dl;
    }
    DbgLog("UVC-PU: 配置中未找到 Processing Unit 描述符 (inVC=%d)", inVC ? 1 : 0);
}

// 枚举 USB hub，按 VID/PID 匹配目标摄像头，读取配置描述符并解析 PU bmControls。
static void DumpUvcProcessingUnit(const wchar_t* ksDevicePath)
{
    USHORT vid = 0, pid = 0;
    if (!ParseUsbVidPid(ksDevicePath, vid, pid)) {
        DbgLog("UVC-PU: 设备路径不含 vid_/pid_，跳过 USB 描述符解析");
        return;
    }
    DbgLog("=== UVC Processing Unit 能力解析 (VID=%04X PID=%04X) ===", vid, pid);

    static const GUID GUID_DEVINTERFACE_USB_HUB = { 0xF18A0E88, 0xC30C, 0x11D0, { 0x88,0x15,0x00,0xA0,0xC9,0x06,0xBB,0xB8 } };
    HANDLE hdi = SetupDiGetClassDevsW(&GUID_DEVINTERFACE_USB_HUB, nullptr, nullptr,
                                      DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
    if (hdi == INVALID_HANDLE_VALUE) {
        DbgLog("UVC-PU: SetupDiGetClassDevs(USB_HUB) FAIL err=%u", GetLastError());
        return;
    }

    bool found = false;
    DWORD hubCount = 0;
    BYTE cfgBuf[4096] = { 0 };
    SP_DEVICE_INTERFACE_DATA did;
    did.cbSize = sizeof(did);
    for (DWORD idx = 0; !found && SetupDiEnumDeviceInterfaces(hdi, nullptr, &GUID_DEVINTERFACE_USB_HUB, idx, &did); idx++) {
        std::vector<BYTE> detailBuf(2048);
        PSP_DEVICE_INTERFACE_DETAIL_DATA_W detail = (PSP_DEVICE_INTERFACE_DETAIL_DATA_W)detailBuf.data();
        detail->cbSize = 6;
        if (!SetupDiGetDeviceInterfaceDetailW(hdi, &did, detail, 2048, nullptr, nullptr)) {
            DbgLog("UVC-PU: hub[%lu] GetDetail FAIL err=%u", idx, GetLastError());
            continue;
        }
        hubCount++;

        HANDLE hHub = CreateFileW(detail->DevicePath, GENERIC_WRITE,
                                  FILE_SHARE_READ | FILE_SHARE_WRITE,
                                  nullptr, OPEN_EXISTING, 0, nullptr);
        if (hHub == INVALID_HANDLE_VALUE) {
            DbgLog("UVC-PU: hub[%lu] 打开失败 err=%u", idx, GetLastError());
            continue;
        }

        // hub 端口数：USB_NODE_INFORMATION 输出，NumberOfPorts 位于偏移 6
        BYTE nodeBuf[256] = { 0 };
        DWORD rb = 0;
        UCHAR numPorts = 0;
        if (!DeviceIoControl(hHub, IOCTL_USB_GET_NODE_INFORMATION, nullptr, 0,
                             nodeBuf, sizeof(nodeBuf), &rb, nullptr)) {
            DbgLog("UVC-PU: hub[%lu] 查询端口数 FAIL err=%u", idx, GetLastError());
            CloseHandle(hHub);
            continue;
        }
        numPorts = nodeBuf[6];
        DbgLog("UVC-PU: hub[%lu] 端口数=%u", idx, numPorts);
        if (numPorts == 0) { CloseHandle(hHub); continue; }

        for (UCHAR port = 1; port <= numPorts && !found; port++) {
            BYTE connBuf[1024] = { 0 };
            UVC_USB_NODE_CONNECTION_INFORMATION_EX* conn = (UVC_USB_NODE_CONNECTION_INFORMATION_EX*)connBuf;
            conn->ConnectionIndex = port;
            rb = 0;
            if (!DeviceIoControl(hHub, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX,
                                 connBuf, sizeof(connBuf), connBuf, sizeof(connBuf), &rb, nullptr)) {
                DbgLog("UVC-PU: hub[%lu] port %u 连接查询 FAIL err=%u", idx, port, GetLastError());
                continue;
            }
            DbgLog("UVC-PU: hub[%lu] port %u VID=%04X PID=%04X bcdDev=%04X", idx, port,
                   conn->DeviceDescriptor.idVendor, conn->DeviceDescriptor.idProduct,
                   conn->DeviceDescriptor.bcdDevice);
            if (conn->DeviceDescriptor.idVendor != vid || conn->DeviceDescriptor.idProduct != pid)
                continue;

            found = true;
            DbgLog("UVC-PU: hub[%lu] 端口 %u 找到目标设备 (bcdDevice=%04X)", idx, port, conn->DeviceDescriptor.bcdDevice);

            // 读取配置描述符
            UVC_USB_DESCRIPTOR_REQUEST* req = (UVC_USB_DESCRIPTOR_REQUEST*)cfgBuf;
            req->ConnectionIndex = port;
            req->Data.bmRequest = 0x80;                 // 方向 IN
            req->Data.bDescriptorType = USB_DESCRIPTOR_TYPE_CONFIGURATION;
            req->Data.wIndex = 0;
            req->Data.wLength = sizeof(cfgBuf);
            rb = 0;
            if (DeviceIoControl(hHub, IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION,
                                cfgBuf, sizeof(cfgBuf), cfgBuf, sizeof(cfgBuf), &rb, nullptr)) {
                if (rb > sizeof(UVC_USB_DESCRIPTOR_REQUEST))
                    ParseUvcConfigForPu(cfgBuf + sizeof(UVC_USB_DESCRIPTOR_REQUEST),
                                        rb - (DWORD)sizeof(UVC_USB_DESCRIPTOR_REQUEST));
            } else {
                DbgLog("UVC-PU: 读取配置描述符 FAIL err=%u", GetLastError());
            }
        }
        CloseHandle(hHub);
    }
    SetupDiDestroyDeviceInfoList(hdi);

    DbgLog("UVC-PU: 共枚举 %lu 个 hub", hubCount);
    if (!found)
        DbgLog("UVC-PU: 未在 USB hub 中找到 VID=%04X PID=%04X", vid, pid);
}

// 在 filter 级别用 BASICSUPPORT 查询指定属性的范围信息。
// 成功返回 true 并填充 rangeMin/rangeMax/step/defaultVal。
// nodeId：视频处理节点(PU) 的 NodeId，>=0 时会在 BASICSUPPORT 范围无效时
//         尝试节点级 GETRANGE 获取真实范围（UVC 的真实范围/默认值在 PU 节点上）。
static bool QueryNodeRange(HANDLE h, const GUID& set, ULONG id, ULONG nodeId,
                           LONG& rangeMin, LONG& rangeMax,
                           LONG& step, LONG& defaultVal)
{
    KSP_NODE req;
    req.Property.Set = set;
    req.Property.Id  = id;
    req.Property.Flags = KSPROPERTY_TYPE_GETRANGE | KSPROPERTY_TYPE_TOPOLOGY;
    req.NodeId = nodeId;
    req.Reserved = 0;

    // 用大缓冲区接收，KSPROPERTY_TYPE_GETRANGE 返回的是标准 KSPROPERTY_DESCRIPTION
    // 结构（Header + MembersHeader + MembersRange），与 BASICSUPPORT 一致，需按
    // KSPROPERTY_DESCRIPTION 布局解析，不能用 KSPROPERTY_VIDEOPROCAMP_NODE_RANGE 直接接收。
    BYTE buf[512] = {0};
    DWORD rb = 0;
    if (!KsIoctl(h, &req, sizeof(req), buf, sizeof(buf), &rb)) {
        DbgLog("  NodeGETRANGE id=%u FAIL err=%u hr=0x%08lX", id, GetLastError(), (unsigned long)g_lastKsHr);
        return false;
    }
    DbgLog("  NodeGETRANGE id=%u rb=%u", id, rb);
    {
        char hex[512] = {0}; int off = 0;
        for (DWORD i = 0; i < ((rb < 72) ? rb : 72); i++) off += sprintf(hex + off, "%02X ", buf[i]);
        DbgLog("    raw hex[0..%u]: %s", (rb < 72) ? rb-1 : 71, hex);
    }
    return ParseRangeResponse(buf, rb, rangeMin, rangeMax, step, defaultVal);
}

// Filter 级 GETRANGE（不带拓扑标志）。
// 部分驱动在 BASICSUPPORT 返回无效范围（Min=1, Max=0）时，
// 通过直接发送 KSPROPERTY_TYPE_GETRANGE 可获取有效范围数据。
static bool QueryFilterRange(HANDLE h, const GUID& set, ULONG id,
                             LONG& rangeMin, LONG& rangeMax,
                             LONG& step, LONG& defaultVal)
{
    KSPROPERTY req;
    req.Set = set;
    req.Id  = id;
    req.Flags = KSPROPERTY_TYPE_GETRANGE;

    BYTE buf[512] = {0};
    DWORD rb = 0;
    if (!KsIoctl(h, &req, sizeof(req), buf, sizeof(buf), &rb)) {
        DbgLog("  FilterGETRANGE id=%u FAIL err=%u hr=0x%08lX", id, GetLastError(), (unsigned long)g_lastKsHr);
        return false;
    }
    DbgLog("  FilterGETRANGE id=%u rb=%u", id, rb);
    {
        char hex[512] = {0}; int off = 0;
        for (DWORD i = 0; i < ((rb < 72) ? rb : 72); i++) off += sprintf(hex + off, "%02X ", buf[i]);
        DbgLog("    raw hex[0..%u]: %s", (rb < 72) ? rb-1 : 71, hex);
    }
    return ParseRangeResponse(buf, rb, rangeMin, rangeMax, step, defaultVal);
}

// 枚举拓扑节点，找到视频处理节点(PU) 的 NodeId。找不到返回 false。
// 响应布局（实测）：[0..3]=总字节数, [4..7]=节点数, [8..]=GUID 数组。
// 部分驱动没有标准的 KSNODETYPE_VIDEO_PROCESSING 节点（用自定义 GUID），
// 此时逐个节点试多个属性 ID 的 GETRANGE，能返回有效范围的即 PU。
// 探测的属性 ID 按集合区分：VideoProcAmp 试亮度/对比度等，CameraControl 试曝光/平移等。
static bool FindProcessingNode(HANDLE h, ControlSetState& st)
{
    KSPROPERTY req;
    req.Set = KSPROPSETID_Topology;
    req.Id  = KSPROPERTY_TOPOLOGY_NODES;
    req.Flags = KSPROPERTY_TYPE_GET;

    BYTE buf[4096] = {0};
    DWORD rb = 0;
    if (!KsIoctl(h, &req, sizeof(req), buf, sizeof(buf), &rb) || rb < 8) {
        DbgLog("FindProcessingNode(%s) FAIL err=%u rb=%u", st.name, GetLastError(), rb);
        return false;
    }
    ULONG numNodes = *(ULONG*)(buf + 4);
    st.numNodes = numNodes; // 供 DoSet 遍历全部候选节点
    DbgLog("FindProcessingNode(%s): %lu nodes rb=%u", st.name, numNodes, rb);
    char hex[4096] = {0}; int off = 0;
    for (DWORD i = 0; i < rb && i < 96; i++) off += sprintf(hex + off, "%02X ", buf[i]);
    DbgLog("  topology raw hex[0..%u]: %s", rb < 96 ? rb - 1 : 95, hex);
    if (rb < 8 + numNodes * (ULONG)sizeof(GUID)) {
        DbgLog("  topology buffer too small, need %u", 8 + numNodes * (ULONG)sizeof(GUID));
        return false;
    }

    // 1) 先找标准 PU 节点类型（KSNODETYPE_VIDEO_PROCESSING）。
    //    注意：CUSTOM_NODE_VIDEO_PROCESSING(=DEV_SPECIFIC) 不是真实 PU——
    //    无 PU 节点的设备其 VideoProcAmp 由 usbvideo.sys 仿真，SET 必然失败，仅只读。
    st.hasRealPu = false;
    bool hasDevSpecific = false;
    GUID* nodes = (GUID*)(buf + 8);
    for (ULONG i = 0; i < numNodes; i++) {
        char g[64]; sprintf(g, "{%08lX-%04X-%04X-%02X%02X-%02X%02X%02X%02X%02X%02X}",
            nodes[i].Data1, nodes[i].Data2, nodes[i].Data3,
            nodes[i].Data4[0], nodes[i].Data4[1], nodes[i].Data4[2], nodes[i].Data4[3],
            nodes[i].Data4[4], nodes[i].Data4[5], nodes[i].Data4[6], nodes[i].Data4[7]);
        if (i < 80) DbgLog("  node[%lu] %s", i, g);
        if (IsEqualGUID(nodes[i], KSNODETYPE_VIDEO_PROCESSING)) {
            st.puNodeId = i;
            st.hasRealPu = true;
            DbgLog("FindProcessingNode(%s): real PU nodeId=%lu (VIDEO_PROCESSING)", st.name, i);
            return true;
        }
        if (IsEqualGUID(nodes[i], CUSTOM_NODE_VIDEO_PROCESSING)) {
            hasDevSpecific = true;
            DbgLog("  node[%lu] is DEV_SPECIFIC (non-standard, NOT a real PU — device likely emulates VideoProcAmp)", i);
        }
    }

    // 拓扑含 DEV_SPECIFIC 且无标准 PU → 判定为仿真设备（usbvideo.sys 合成 VideoProcAmp），
    // 其节点级操作必然 ERROR_INVALID_FUNCTION，跳过探测以免无谓 IOCTL。
    if (hasDevSpecific) {
        DbgLog("FindProcessingNode(%s): 含 DEV_SPECIFIC 且无标准 PU → 仿真设备，跳过节点探测", st.name);
        return false;
    }

    // 2) 兜底：逐个节点试多个属性 ID 的节点级 GETRANGE，能返回有效范围的即 PU。
    //    部分驱动在第一个属性上不返回有效范围，但其它属性可以。
    static const ULONG vpProbeIds[] = { 0, 1, 3, 9, 4, 7, 5, 8 };
    // 0=brightness, 1=contrast, 3=saturation, 9=gain, 4=sharpness,
    // 7=white_balance, 5=gamma, 8=backlight_compensation
    static const ULONG ccProbeIds[] = { 4, 0, 3, 1, 6, 5, 7, 8 };
    // 4=exposure, 0=pan, 3=zoom, 1=tilt, 6=focus, 5=iris, 7=scanmode, 8=privacy
    const ULONG* probeIds = (st.propSet == &PROPSETID_VIDCAP_CAMERACONTROL) ? ccProbeIds : vpProbeIds;
    int probeCount = (st.propSet == &PROPSETID_VIDCAP_CAMERACONTROL)
                         ? (int)(sizeof(ccProbeIds) / sizeof(ccProbeIds[0]))
                         : (int)(sizeof(vpProbeIds) / sizeof(vpProbeIds[0]));
    for (ULONG i = 0; i < numNodes; i++) {
        for (int pi = 0; pi < probeCount; pi++) {
            LONG mn = 0, mx = 0, stp = 0, dv = 0;
            DbgLog("  Probe node[%lu] id=%u", i, probeIds[pi]);
            if (QueryNodeRange(h, *st.propSet, probeIds[pi], i, mn, mx, stp, dv)) {
                st.puNodeId = i;
                st.hasRealPu = true;
                DbgLog("FindProcessingNode(%s): PU nodeId=%lu (probe id=%u ok, range %ld-%ld)",
                       st.name, i, probeIds[pi], mn, mx);
                return true;
            }
        }
    }
    return false;
}

static bool QueryPropertyRange(HANDLE h, ControlSetState& st, ULONG id,
                               LONG& rangeMin, LONG& rangeMax,
                               LONG& step, LONG& defaultVal,
                               ULONG* outAccessFlags = nullptr)
{
    KSPROPERTY req;
    req.Set = *st.propSet;
    req.Id  = id;
    req.Flags = KSPROPERTY_TYPE_BASICSUPPORT;
    BYTE buf[512] = {0};
    DWORD rb = 0;
    bool ok = KsIoctl(h, &req, sizeof(req), buf, sizeof(buf), &rb);
    DbgLog("QueryPropertyRange(%s) id=%u ok=%d rb=%u err=%u", st.name, id, ok, rb, ok ? 0 : GetLastError());
    if (!ok || rb < 40) {
        if (ok) {
            char hex[256] = {0}; int off = 0;
            for (DWORD i = 0; i < rb && i < 64; i++) off += sprintf(hex + off, "%02X ", buf[i]);
            DbgLog("  BASICSUPPORT <40 bytes=%u hex: %s", rb, hex);
        }
        // Step 2: BASICSUPPORT 完全失败，尝试 Filter 级 GETRANGE 作为最后手段
        DbgLog("  BASICSUPPORT failed, trying Filter GETRANGE");
        if (QueryFilterRange(h, *st.propSet, id, rangeMin, rangeMax, step, defaultVal)) {
            DbgLog("  Filter GETRANGE ok: %ld-%ld step %ld default %ld", rangeMin, rangeMax, step, defaultVal);
            if (outAccessFlags) *outAccessFlags = 0; // 未知，标记为 0
            return true;
        }
        return false;
    }

    KSPROPERTY_DESCRIPTION* desc = (KSPROPERTY_DESCRIPTION*)buf;
    DbgLog("  AccessFlags=0x%lX DescriptionSize=%lu MembersListCount=%lu", desc->AccessFlags, desc->DescriptionSize, desc->MembersListCount);
    if (outAccessFlags) *outAccessFlags = desc->AccessFlags;
    {
        char hex[512] = {0}; int off = 0;
        for (ULONG i = 0; i < ((rb < 72) ? rb : 72); i++) off += sprintf(hex + off, "%02X ", buf[i]);
        DbgLog("  raw hex[0..%u]: %s", (rb < 72) ? rb-1 : 71, hex);
    }

    // Step 1: 尝试从 BASICSUPPORT 响应中解析有效范围
    if (ParseRangeResponse(buf, rb, rangeMin, rangeMax, step, defaultVal)) {
        DbgLog("  BASICSUPPORT range ok: %ld-%ld step %ld default %ld", rangeMin, rangeMax, step, defaultVal);
        return true;
    }

    // BASICSUPPORT 返回了无效范围（如 Min=1, Max=0），尝试降级方案

    // Step 2: 尝试 Filter 级 GETRANGE（不带拓扑标志）
    DbgLog("  BASICSUPPORT range invalid, trying Filter GETRANGE");
    if (QueryFilterRange(h, *st.propSet, id, rangeMin, rangeMax, step, defaultVal)) {
        DbgLog("  Filter GETRANGE ok: %ld-%ld step %ld default %ld", rangeMin, rangeMax, step, defaultVal);
        return true;
    }

    // Step 3: 尝试节点级 GETRANGE
    if (st.puNodeId != (ULONG)-1 && QueryNodeRange(h, *st.propSet, id, st.puNodeId, rangeMin, rangeMax, step, defaultVal)) {
        DbgLog("  Node GETRANGE ok: %ld-%ld step %ld default %ld", rangeMin, rangeMax, step, defaultVal);
        return true;
    }

    // Step 4: 所有范围查询均失败，使用 0-255 兜底，保证属性仍可被 UI 操作
    DbgLog("  All range queries failed, using fallback 0-255");
    rangeMin = 0;
    rangeMax = 255;
    step = 1;
    defaultVal = 0;
    return true;
}

// 部分驱动（如 GENERAL - UVC）拒绝标准 KSPROPERTY_TYPE_GET，但 filter 级
// GETRANGE 会返回"当前值"（VALUES 成员，单值）。此函数尝试该通道读取当前值。
static bool QueryFilterCurrentValue(HANDLE h, const GUID& set, ULONG id, LONG& value)
{
    KSPROPERTY req;
    req.Set = set;
    req.Id  = id;
    req.Flags = KSPROPERTY_TYPE_GETRANGE;

    BYTE buf[512] = {0};
    DWORD rb = 0;
    if (!KsIoctl(h, &req, sizeof(req), buf, sizeof(buf), &rb)) {
        DbgLog("  FilterGETRANGE(cur) id=%u FAIL err=%u hr=0x%08lX", id, GetLastError(), (unsigned long)g_lastKsHr);
        return false;
    }
    DbgLog("  FilterGETRANGE(cur) id=%u rb=%u", id, rb);
    if (rb < sizeof(KSPROPERTY_DESCRIPTION) + sizeof(KSPROPERTY_MEMBERSHEADER) + 4) return false;
    KSPROPERTY_DESCRIPTION* desc = (KSPROPERTY_DESCRIPTION*)buf;
    if (desc->MembersListCount == 0) return false;
    ULONG off = sizeof(KSPROPERTY_DESCRIPTION);
    KSPROPERTY_MEMBERSHEADER* mh = (KSPROPERTY_MEMBERSHEADER*)(buf + off);
    off += sizeof(KSPROPERTY_MEMBERSHEADER);
    if (mh->MembersCount == 0) return false;
    // MembersFlags 校验：仅"枚举值列表"（KSPROPERTY_MEMBER_VALUES）时，
    // MembersHeader 之后的字节才是属性值数组；范围型/步进型（RANGES 等）
    // 其后是 KSPROPERTY_BOUNDS_LONG/STEPPING_LONG 结构，取首 LONG 当"当前值"
    // 会读到范围边界/步进量（标准驱动上显示错误的当前值）。
    if (!(mh->MembersFlags & KSPROPERTY_MEMBER_VALUES)) {
        DbgLog("  FilterGETRANGE(cur) id=%u 非枚举值列表 flags=0x%08lX，不可当当前值",
               id, mh->MembersFlags);
        return false;
    }
    ULONG elem = mh->MembersSize ? mh->MembersSize : 4;
    if (elem < 4 || off + elem > rb) return false;
    value = *(LONG*)(buf + off);
    DbgLog("  FilterGETRANGE(cur) id=%u value=%ld", id, value);
    return true;
}

// 读取属性当前值（filter 级别 KSPROPERTY_TYPE_GET）。
// 部分 UVC 驱动对 GET 返回 122 ERROR_INSUFFICIENT_BUFFER，按返回的所需大小重试。
// 成功返回 true 并填充 value/flags。
static bool ReadCurrentValue(HANDLE h, ControlSetState& st, ULONG id, LONG& value, ULONG& flags)
{
    // 尝试 Filter 级 GET（KSPROPERTY 24B 输入）
    {
        KSPROPERTY req;
        req.Set = *st.propSet;
        req.Id  = id;
        req.Flags = KSPROPERTY_TYPE_GET;

        BYTE buf[64];
        for (int attempt = 0; attempt < 3; attempt++) {
            DWORD rb = 0;
            if (KsIoctl(h, &req, sizeof(req), buf, sizeof(buf), &rb)) {
                // 至少需返回 Value(24)+Flags(28) 为止的 32 字节，否则 buf 后部是未初始化栈数据
                if (rb >= 32) {
                    KSPROPERTY_VIDEOPROCAMP_S* s = (KSPROPERTY_VIDEOPROCAMP_S*)buf;
                    value = s->Value;
                    flags = s->Flags;
                    DbgLog("  FilterGET(id=24B) id=%u value=%ld flags=0x%lX", id, value, flags);
                    return true;
                }
                DbgLog("  FilterGET(id=24B) id=%u rb=%u 过短，丢弃", id, rb);
                return false;
            }
            DWORD err = GetLastError();
            DbgLog("  FilterGET(id=24B) id=%u attempt=%d FAIL err=%u hr=0x%08lX",
                   id, attempt, err, (unsigned long)g_lastKsHr);
            if (err != ERROR_INSUFFICIENT_BUFFER)
                break;
            Sleep(5);
        }
    }

    // 尝试 Filter 级 GET（完整 KSPROPERTY_VIDEOPROCAMP_S 36B 输入，部分驱动要求完整结构）
    {
        KSPROPERTY_VIDEOPROCAMP_S fg;
        ZeroMemory(&fg, sizeof(fg));
        fg.Property.Set = *st.propSet;
        fg.Property.Id  = id;
        fg.Property.Flags = KSPROPERTY_TYPE_GET;
        fg.Value = 0;
        fg.Flags = KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
        fg.Capabilities = 0;

        BYTE buf[64];
        DWORD rb = 0;
        if (KsIoctl(h, &fg, sizeof(fg), buf, sizeof(buf), &rb)) {
            if (rb >= 32) { // 同上前置校验：至少 Value+Flags 长度的数据
                KSPROPERTY_VIDEOPROCAMP_S* s = (KSPROPERTY_VIDEOPROCAMP_S*)buf;
                value = s->Value;
                flags = s->Flags;
                DbgLog("  FilterGET(full) id=%u value=%ld flags=0x%lX", id, value, flags);
                return true;
            }
            DbgLog("  FilterGET(full) id=%u rb=%u 过短，丢弃", id, rb);
        } else {
            DbgLog("  FilterGET(full) id=%u FAIL err=%u hr=0x%08lX", id, GetLastError(), (unsigned long)g_lastKsHr);
        }
    }

    // Filter 级 GET 失败，尝试节点级 GET（部分驱动仅支持节点级操作）。
    // 仿真设备（无真实 PU）节点级操作必失败，跳过以减少无效 IOCTL。
    if (st.hasRealPu && st.puNodeId != (ULONG)-1) {
        // 尝试 1: KSP_NODE 作为输入（标准节点级请求）
        {
            KSP_NODE nreq;
            nreq.Property.Set = *st.propSet;
            nreq.Property.Id  = id;
            nreq.Property.Flags = KSPROPERTY_TYPE_GET | KSPROPERTY_TYPE_TOPOLOGY;
            nreq.NodeId = st.puNodeId;
            nreq.Reserved = 0;

            KSPROPERTY_VIDEOPROCAMP_NODE_S nresp;
            ZeroMemory(&nresp, sizeof(nresp));
            DWORD rb = 0;
            // 节点响应中 Value 位于偏移 32、Flags 位于偏移 36，需至少 40 字节才有效
            if (KsIoctl(h, &nreq, sizeof(nreq), &nresp, sizeof(nresp), &rb) && rb >= 40) {
                value = nresp.Value;
                flags = nresp.Flags;
                DbgLog("  NodeGET id=%u value=%ld flags=0x%lX", id, value, flags);
                return true;
            }
            DbgLog("  NodeGET id=%u FAIL err=%u hr=0x%08lX", id, GetLastError(), (unsigned long)g_lastKsHr);
        }

        // 尝试 2: 完整 KSPROPERTY_VIDEOPROCAMP_NODE_S 作为输入（部分驱动要求完整结构）
        {
            KSPROPERTY_VIDEOPROCAMP_NODE_S nfull;
            ZeroMemory(&nfull, sizeof(nfull));
            nfull.NodeProperty.Property.Set = *st.propSet;
            nfull.NodeProperty.Property.Id  = id;
            nfull.NodeProperty.Property.Flags = KSPROPERTY_TYPE_GET | KSPROPERTY_TYPE_TOPOLOGY;
            nfull.NodeProperty.NodeId = st.puNodeId;
            nfull.NodeProperty.Reserved = 0;

            KSPROPERTY_VIDEOPROCAMP_NODE_S nresp;
            ZeroMemory(&nresp, sizeof(nresp));
            DWORD rb = 0;
            if (KsIoctl(h, &nfull, sizeof(nfull), &nresp, sizeof(nresp), &rb) && rb >= 40) {
                value = nresp.Value;
                flags = nresp.Flags;
                DbgLog("  NodeGET(full-struct) id=%u value=%ld flags=0x%lX", id, value, flags);
                return true;
            }
            DbgLog("  NodeGET(full-struct) id=%u FAIL err=%u hr=0x%08lX", id, GetLastError(), (unsigned long)g_lastKsHr);
        }
    }

    // Filter 级 GETRANGE 兜底：此驱动拒绝标准 GET，但 GETRANGE 返回"当前值"
    {
        LONG v = 0;
        if (QueryFilterCurrentValue(h, *st.propSet, id, v)) {
            value = v;
            flags = KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
            return true;
        }
    }

    return false;
}

// SET 诊断自测需要调用 DoSet（其定义位于本文件后部），此处前向声明
static int DoSet(int setIdx, int propertyId, long value, int autoMode);
// DoInit 中的节点级 SET 探测需要调用 TryNodeSet（定义位于本文件后部），此处前向声明
static bool TryNodeSet(HANDLE h, ULONG nodeId, const GUID& set, ULONG propertyId, LONG value, ULONG flags, LONG& outValue);

// ===================== 单集合初始化 =====================
// 初始化一个控制集合（VideoProcAmp / CameraControl）：
// 节点发现 → 属性枚举(BASICSUPPORT/GETRANGE) → 当前值读取 → KS SET 探测。
// 不涉及 DirectShow（IAM/IKs）通道——该通道在 DoInit 中由 ProbeIamControls 统一探测。
static void InitControlSet(ControlSetState& st)
{
    DbgLog("=== Init %s 开始 ===", st.name);
    st.puNodeId = (ULONG)-1;
    st.numNodes = 0;
    st.hasRealPu = false;
    st.writeOk = false;

    // 找到视频处理节点(PU)，用于节点级 GETRANGE/GET/SET 获取真实范围/默认值
    FindProcessingNode(g_devHandle, st);

    // 枚举所有属性，用 BASICSUPPORT 获取范围，内存缓存当前值
    for (int id = 0; id <= st.maxId; id++) {
        ProcAmpEntry e;
        e.id = id;
        e.supported = false;
        e.minVal = e.maxVal = e.stepVal = e.defaultVal = e.currentVal = 0;
        e.flags = 0;

        // 用 BASICSUPPORT 查询属性范围
        LONG rangeMin = 0, rangeMax = 0, step = 0, defaultVal = 0;
        ULONG accessFlags = 0;
        if (QueryPropertyRange(g_devHandle, st, (ULONG)id, rangeMin, rangeMax, step, defaultVal, &accessFlags)) {
            e.minVal = rangeMin;
            e.maxVal = rangeMax;
            e.stepVal = step;
            e.defaultVal = defaultVal;
            e.accessFlags = accessFlags;
            e.currentVal = defaultVal;  // 初始值 = 默认值
            e.flags = KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;

            // 尝试读取真实当前值（部分驱动 GET 不可用则保持默认值）。
            // 注意：对 usbvideo.sys 仿真层（无真实 PU，本设备），Filter GET 一律返回 122，
            // ReadCurrentValue 最终回落到的 GETRANGE(cur) 通道读回的 VALUE 是驱动占位值
            // （实测各属性读回值恰等于范围中值：亮度 128/白平衡 255/饱和度 16…），
            // 并非硬件实时值，仅可作为参考显示，绝不能当作默认值写入。
            LONG cur = 0; ULONG curFlags = 0;
            bool gotValue = false;

            // 优先 IAM 通道：VideoProcAmp 用 IAMVideoProcAmp，CameraControl 用 IAMCameraControl
            // IAMVideoProcAmp 属性枚举只到 Gain(9)，10-13 是 UVC 扩展，无 IAM 对应
            if (st.propSet == &PROPSETID_VIDCAP_VIDEOPROCAMP && g_pIam && id <= 9) {
                long v = 0, f = 0;
                if (SUCCEEDED(g_pIam->lpVtbl->Get(g_pIam, id, &v, &f))) {
                    cur = v; curFlags = (ULONG)f;
                    gotValue = true;
                    DbgLog("%s id=%u IAM GET current=%ld flags=0x%lX（真实值）", st.name, id, cur, curFlags);
                }
            } else if (st.propSet == &PROPSETID_VIDCAP_CAMERACONTROL && g_pCam) {
                long v = 0, f = 0;
                if (SUCCEEDED(g_pCam->lpVtbl->Get(g_pCam, id, &v, &f))) {
                    cur = v; curFlags = (ULONG)f;
                    gotValue = true;
                    DbgLog("%s id=%u IAM GET current=%ld flags=0x%lX（真实值）", st.name, id, cur, curFlags);
                }
            }

            if (!gotValue) {
                if (ReadCurrentValue(g_devHandle, st, (ULONG)id, cur, curFlags)) {
                    gotValue = true;
                    DbgLog("%s id=%u GET current=%ld flags=0x%lX（仿真驱动占位值，仅参考）", st.name, id, cur, curFlags);
                }
            }

            if (gotValue) {
                e.currentVal = cur;
                // 默认值保持 ParseRangeResponse 的结果（中值或 VALUES 候选，随后由下方
                // IAM GetRange 真实默认覆盖），不再用 KS GETRANGE(cur) 占位值覆盖
                // （否则“重置默认”会跳到一个无依据的值）。
                if (curFlags & KSPROPERTY_VIDEOPROCAMP_FLAGS_AUTO)
                    e.flags |= KSPROPERTY_VIDEOPROCAMP_FLAGS_AUTO;
            }

            // 真实范围与默认值：IAM GetRange 的 min/max/step/pDefault 来自 UVC 描述符
            // （bMinValue/bMaxValue/bStepSize/bDefaultValue），是驱动下发的权威值，
            // 优于 KS BASICSUPPORT 解析值——尤其当 KS 回退到 0-255 兜底时，用真实范围
            // 修正缓存，保证 UI 滑块范围与 DoSet 钳制都以驱动真实值为准。
            // 仅更新缓存供“恢复默认”/界面显示使用，不向设备写入任何内容。
            {
                long dv = 0, mn2 = 0, mx2 = 0, st2 = 0, caps2 = 0;
                bool haveRealRange = false;
                if (st.propSet == &PROPSETID_VIDCAP_VIDEOPROCAMP && g_pIam && id <= 9) {
                    if (SUCCEEDED(g_pIam->lpVtbl->GetRange(g_pIam, id, &mn2, &mx2, &st2, &dv, &caps2)))
                        haveRealRange = true;
                } else if (st.propSet == &PROPSETID_VIDCAP_CAMERACONTROL && g_pCam) {
                    if (SUCCEEDED(g_pCam->lpVtbl->GetRange(g_pCam, id, &mn2, &mx2, &st2, &dv, &caps2)))
                        haveRealRange = true;
                }
                if (haveRealRange && mn2 < mx2) {
                    e.minVal = mn2;
                    e.maxVal = mx2;
                    if (st2 > 0) e.stepVal = st2;
                    if (dv >= mn2 && dv <= mx2) {
                        if (dv != e.defaultVal)
                            DbgLog("%s id=%u IAM GetRange 真实默认=%ld（替代原默认 %ld）", st.name, id, dv, e.defaultVal);
                        e.defaultVal = dv;
                    } else {
                        DbgLog("%s id=%u IAM GetRange 默认=%ld 越界[%ld,%ld]，保留原默认 %ld",
                               st.name, id, dv, mn2, mx2, e.defaultVal);
                    }
                } else if (haveRealRange) {
                    DbgLog("%s id=%u IAM GetRange 范围无效 [%ld,%ld]，保留 KS 范围与默认",
                           st.name, id, mn2, mx2);
                }
            }

            e.supported = true;
        }

        st.entries.push_back(e);
    }

    // 若 IAM 已可用，跳过 KS SET 探测，直接标记可写（AMCap 优先使用 IAM 通道）
    {
        bool iamAvailable = false;
        if (st.propSet == &PROPSETID_VIDCAP_VIDEOPROCAMP && g_pIam) iamAvailable = true;
        if (st.propSet == &PROPSETID_VIDCAP_CAMERACONTROL && g_pCam) iamAvailable = true;
        if (iamAvailable) {
            DbgLog("InitControlSet(%s): IAM 可用，跳过 KS SET 探测", st.name);
            st.writeOk = true;
            return;
        }
    }

    // [诊断] Filter 级 SET 探测：对 id=0（VideoProcAmp=亮度 / CameraControl=Pan）原值回写，
    // 直接验证 Filter 级 SET 是否可用。放在只读判定之前执行——AccessFlags=0x3 只是驱动描述
    // 声称的支持，实际以探测为准。原值回写无副作用。writeOk=任一通道（Filter/节点级）SET 可用。
    bool writeOk = false;
    {
        ProcAmpEntry* probe = nullptr;
        for (auto& it : st.entries) if (it.id == 0 && it.supported) { probe = &it; break; }
        if (probe) {
            DbgLog("=== %s Filter SET probe start: id=0 value=%ld auto=0 (原值回写) ===", st.name, probe->currentVal);
            LONG val = probe->currentVal;

            // 变体扫描：Flags 取值(0/MANUAL)、请求结构(带 Capabilities/仅 KSP+LONG)、
            // 输出方式(同结构/NULL)。找出驱动真正接受的 SET 格式。
            // 只试 0 与 MANUAL(0x2)，不试 AUTO(0x1) 以免意外切换自动模式。
            bool anyOk = false;
            static const ULONG flagVariants[] = { 0, KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL };
            for (int fi = 0; fi < (int)(sizeof(flagVariants) / sizeof(flagVariants[0])); fi++) {
                KSPROPERTY_VIDEOPROCAMP_S s;
                ZeroMemory(&s, sizeof(s));
                s.Property.Set = *st.propSet;
                s.Property.Id  = 0;
                s.Property.Flags = KSPROPERTY_TYPE_SET;
                s.Value = val;
                s.Flags = flagVariants[fi];
                s.Capabilities = 0;
                DWORD bytes = 0;
                KSPROPERTY_VIDEOPROCAMP_S resp = s;
                if (KsIoctl(g_devHandle, &s, sizeof(s), &resp, sizeof(resp), &bytes)) {
                    DbgLog("=== %s Filter SET probe OK flags=0x%lX value=%ld ===", st.name, flagVariants[fi], resp.Value);
                    writeOk = true; anyOk = true;
                    break;
                }
                DbgLog("=== %s Filter SET probe flags=0x%lX FAIL err=%u hr=0x%08lX ===",
                       st.name, flagVariants[fi], GetLastError(), (unsigned long)g_lastKsHr);
            }

            if (!anyOk) {
                // 变体：KSPROPERTY(24B) + LONG value(4B) 输入，无 Flags/Capabilities
                struct { KSPROPERTY p; LONG v; } ksv;
                ZeroMemory(&ksv, sizeof(ksv));
                ksv.p.Set = *st.propSet;
                ksv.p.Id  = 0;
                ksv.p.Flags = KSPROPERTY_TYPE_SET;
                ksv.v = val;
                DWORD bytes = 0;
                LONG out = 0;
                if (KsIoctl(g_devHandle, &ksv, sizeof(ksv), &out, sizeof(out), &bytes)) {
                    DbgLog("=== %s Filter SET probe OK (KSP+LONG) value=%ld ===", st.name, out);
                    writeOk = true; anyOk = true;
                } else {
                    DbgLog("=== %s Filter SET probe (KSP+LONG) FAIL err=%u hr=0x%08lX ===",
                           st.name, GetLastError(), (unsigned long)g_lastKsHr);
                }
            }

            if (!anyOk) {
                // 变体：输出缓冲区 NULL（SET 可能不返回数据）
                KSPROPERTY_VIDEOPROCAMP_S s;
                ZeroMemory(&s, sizeof(s));
                s.Property.Set = *st.propSet;
                s.Property.Id  = 0;
                s.Property.Flags = KSPROPERTY_TYPE_SET;
                s.Value = val;
                s.Flags = KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
                s.Capabilities = 0;
                DWORD bytes = 0;
                if (KsIoctl(g_devHandle, &s, sizeof(s), nullptr, 0, &bytes)) {
                    DbgLog("=== %s Filter SET probe OK (NULL out) ===", st.name);
                    writeOk = true;
                } else {
                    DbgLog("=== %s Filter SET probe (NULL out) FAIL err=%u hr=0x%08lX ===",
                           st.name, GetLastError(), (unsigned long)g_lastKsHr);
                }
            }
        }
    }

    // Filter 级 SET 失败时，主动对所有拓扑节点执行节点级 SET 探测。
    // 任一节点（可能是 DEV_SPECIFIC 等非标准节点）成功即视为可写，
    // 并将该节点记录为工作节点（st.puNodeId），后续 DoSet 会优先命中它。
    if (!writeOk) {
        ProcAmpEntry* probe = nullptr;
        for (auto& it : st.entries) if (it.id == 0 && it.supported) { probe = &it; break; }
        if (probe) {
            ULONG nodeLimit = st.numNodes > 0 ? st.numNodes : 4;
            for (ULONG n = 0; n < nodeLimit; n++) {
                LONG outVal = 0;
                DbgLog("=== %s Node SET probe node %lu ... ===", st.name, n);
                if (TryNodeSet(g_devHandle, n, *st.propSet, 0, probe->currentVal,
                               KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL, outVal)) {
                    DbgLog("=== %s Node SET probe OK node=%lu value=%ld ===", st.name, n, outVal);
                    st.puNodeId = n;
                    st.hasRealPu = true;
                    writeOk = true;
                    break;
                }
                DbgLog("=== %s Node SET probe node %lu FAIL err=%u hr=0x%08lX ===",
                       st.name, n, GetLastError(), (unsigned long)g_lastKsHr);
            }
        }
    }

    st.writeOk = writeOk;
}

// 可写判定：以初始化时的实测 SET 探测为准（writeOk）。驱动 BASICSUPPORT 的
// AccessFlags 声明不足为凭——本设备仿真层声明支持 SET(0x2) 但实际拒绝一切写
// （SetCameraControl 焦距=30 全通道失败 code -2 即因此）。实测探测全失败时确认
// 设备不可写，清掉 AUTO/MANUAL 能力位，C# 端据此禁用调节 UI（滑块/自动复选框）。
// 注意：只影响 UI 能力展示；DoSet 在用户实际调节时仍会在所有通道重试——
// streaming 激活后驱动可能接受写（AMCap 属性页同样依赖 streaming），不丢失写通道。
static void FinalizeSetWritability(ControlSetState& st)
{
    bool accessSaysSet = false;
    for (auto& it : st.entries)
        if (it.supported && (it.accessFlags & KSPROPERTY_TYPE_SET)) { accessSaysSet = true; break; }
    if (st.writeOk) {
        DbgLog("DoInit: %s 属性可写（探测 writeOk=1，通道=%s）",
               st.name,
               st.hasRealPu ? "含真实 PU/工作节点"
                            : (st.propSet == &PROPSETID_VIDCAP_CAMERACONTROL
                                 ? (g_pCam ? "IAMCameraControl"
                                           : (g_pKsPropSet ? "IKsPropertySet"
                                                           : (g_pKsControl ? "IKsControl" : "Filter 级")))
                                 : (g_pIam ? "IAMVideoProcAmp"
                                           : (g_pKsPropSet ? "IKsPropertySet"
                                                           : (g_pKsControl ? "IKsControl" : "Filter 级")))));
    } else {
        DbgLog("DoInit: %s 实测写探测全部失败（BASICSUPPORT 声明 SET=%d 不足为凭）→ 属性只读，UI 禁用滑块",
               st.name, accessSaysSet ? 1 : 0);
        // 只清 MANUAL 位（禁用滑块）；保留 AUTO 模式位——驱动返回的 AUTO 位表示
        // 该属性当前处于自动模式（如自动曝光），应继续在 UI 显示自动复选框状态，
        // 否则用户看不到设备的自动控制状态。AUTO 位后续由 DoGet 刷新保持。
        for (auto& it : st.entries)
            if (it.supported) it.flags &= ~(ULONG)KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
    }
}

static int DoInit(const char* deviceName)
{
    InterlockedExchange(&g_lastHr, S_OK);
    InterlockedExchange(&g_lastCcHr, S_OK);
    DoCleanup();

    if (!deviceName) return -1;

    // 设备名：去掉 "video=" 前缀，转宽字符
    std::wstring devNameW;
    int wlen = MultiByteToWideChar(CP_UTF8, 0, deviceName, -1, nullptr, 0);
    if (wlen <= 0) {
        // 非法 UTF-8 序列（MultiByteToWideChar 失败）或空字符串：拒绝初始化，
        // 避免空名跳过名称过滤而匹配到第一台枚举设备（初始化错设备）。
        DbgLog("DoInit: 设备名非法/无法解码（wlen=%d），拒绝初始化", wlen);
        return -1;
    }
    {
        std::vector<wchar_t> buf(wlen);
        MultiByteToWideChar(CP_UTF8, 0, deviceName, -1, buf.data(), wlen);
        devNameW = buf.data();
    }
    const std::wstring prefix = L"video=";
    if (devNameW.size() >= prefix.size() &&
        _wcsnicmp(devNameW.c_str(), prefix.c_str(), prefix.size()) == 0) {
        devNameW = devNameW.substr(prefix.size());
    }
    if (devNameW.empty()) {
        DbgLog("DoInit: 设备名为空，拒绝初始化（避免匹配到错误设备）");
        return -1;
    }

    // 枚举 KSCATEGORY_VIDEO_CAMERA 设备接口
    HANDLE hdi = SetupDiGetClassDevsW(&KSCATEGORY_VIDEO_CAMERA, nullptr, nullptr,
                                      DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
    if (hdi == INVALID_HANDLE_VALUE) {
        InterlockedExchange(&g_lastHr, HRESULT_FROM_WIN32(GetLastError()));
        DbgLog("DoInit: SetupDiGetClassDevsW FAIL err=%u", GetLastError());
        return -2;
    }

    bool found = false;
    SP_DEVICE_INTERFACE_DATA did;
    did.cbSize = sizeof(did);
    for (DWORD idx = 0; SetupDiEnumDeviceInterfaces(hdi, nullptr, &KSCATEGORY_VIDEO_CAMERA, idx, &did); idx++) {
        DbgLog("DoInit: enumerate idx=%lu", idx);
        // 1) 获取接口路径（设备路径）。
        //    注意：此 API 的 cbSize 必须填 6（SP_DEVICE_INTERFACE_DETAIL_DATA_W 的
        //    非对齐大小），填 sizeof()=8 或传入非 NULL 的 DeviceInfoData 都会返回
        //    1784 ERROR_INVALID_USER_BUFFER。设备信息改用 SetupDiEnumDeviceInfo 获取。
        std::vector<BYTE> detailBuf(2048);
        PSP_DEVICE_INTERFACE_DETAIL_DATA_W detail = (PSP_DEVICE_INTERFACE_DETAIL_DATA_W)detailBuf.data();
        detail->cbSize = 6;
        if (!SetupDiGetDeviceInterfaceDetailW(hdi, &did, detail, 2048, nullptr, nullptr))
            continue;

        // 2) 获取该接口对应的设备实例名称用于匹配。
        //    注意：接口枚举索引（SetupDiEnumDeviceInterfaces 的 idx）与设备实例
        //    索引（SetupDiEnumDeviceInfo 的 MemberIndex）在"多摄像头/一设备多接口"
        //    场景下并不对齐（接口序号 ≠ 实例序号），用 idx 直接对齐可能读到别的
        //    设备实例名称 → 错配。改为遍历全部实例，按名称匹配（名称才是依据）。
        SP_DEVINFO_DATA devInfo;
        bool instanceMatched = false;
        for (DWORD idx2 = 0; ; idx2++) {
            devInfo.cbSize = sizeof(devInfo);
            if (!SetupDiEnumDeviceInfo(hdi, idx2, &devInfo)) break;
            // 设备名：优先 FriendlyName，回退 DeviceDesc
            WCHAR nameBuf[256];
            DWORD nameBytes = 0;
            BOOL ok = SetupDiGetDeviceRegistryPropertyW(hdi, &devInfo, SPDRP_FRIENDLYNAME,
                                                        nullptr, (PBYTE)nameBuf, sizeof(nameBuf), &nameBytes);
            if (!ok || nameBytes == 0)
                ok = SetupDiGetDeviceRegistryPropertyW(hdi, &devInfo, SPDRP_DEVICEDESC,
                                                       nullptr, (PBYTE)nameBuf, sizeof(nameBuf), &nameBytes);
            if (!ok) continue;
            std::wstring friendly(nameBuf);
            DbgLog("DoInit: instance[%lu]=%ls", idx2, friendly.c_str());
            if (devNameW.empty() || NameMatch(friendly, devNameW)) { instanceMatched = true; break; }
        }
        if (!instanceMatched) continue;

        // 4) 打开设备（该摄像头偶发 995 ERROR_OPERATION_ABORTED，重试几次）
        HANDLE h = INVALID_HANDLE_VALUE;
        for (int attempt = 0; attempt < 4 && h == INVALID_HANDLE_VALUE; attempt++) {
            h = CreateFileW(detail->DevicePath,
                            GENERIC_READ | GENERIC_WRITE,
                            FILE_SHARE_READ | FILE_SHARE_WRITE,
                            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
            DbgLog("DoInit: CreateFileW attempt=%d h=%p err=%u", attempt, (void*)h, h == INVALID_HANDLE_VALUE ? GetLastError() : 0);
            if (h == INVALID_HANDLE_VALUE) Sleep(100);
        }
        if (h == INVALID_HANDLE_VALUE) continue;
        g_devHandle = h;
        found = true;
#ifdef _DEBUG
        // 诊断：读取 UVC Processing Unit 的 bmControls 位图，确认固件写入能力。
        // 纯诊断开销（USB 全树 IOCTL 遍历），仅 Debug 构建执行；Release 跳过。
        DumpUvcProcessingUnit(detail->DevicePath);
#endif
        break;
    }
    SetupDiDestroyDeviceInfoList(hdi);

    if (!found || g_devHandle == INVALID_HANDLE_VALUE) {
        InterlockedExchange(&g_lastHr, HRESULT_FROM_WIN32(ERROR_NOT_FOUND));
        DoCleanup();
        return -4;
    }

    // 优先使用 DirectShow 通道（AMCap 同款）：先探测 IAM/IKs 接口，
    // 再初始化两套控制集合。InitControlSet 中若 IAM 已可用则跳过 KS SET 探测。
    DbgLog("=== DirectShow (IAM/IKsPropertySet/IKsControl) probe start (优先 IAM 通道) ===");
    ProbeIamControls(devNameW);
    if (g_pIam || g_pCam || g_pKsPropSet || g_pKsControl)
        DbgLog("DoInit: DirectShow 接口已获取（保留供 DoSet/DoGet 使用）");
    else
        DbgLog("DoInit: DirectShow 接口均不可用 → 维持 KS 路径");

    // 初始化两套控制集合：图像属性(VideoProcAmp) + 相机控制(CameraControl)
    InitControlSet(g_vpSet);
    InitControlSet(g_ccSet);

    // 两套集合各自做最终可写判定（只读时清空 flags）
    FinalizeSetWritability(g_vpSet);
    FinalizeSetWritability(g_ccSet);

    // 保持原语义：返回 VideoProcAmp（图像属性）的支持数量
    int count = 0;
    for (auto& e : g_vpSet.entries) if (e.supported) count++;
    return count;
}

static int DoRelease()
{
    DoCleanup();
    return 0;
}

static int DoGetCount(int setIdx)
{
    ControlSetState& st = GetSet(setIdx);
    int count = 0;
    for (auto& e : st.entries) if (e.supported) count++;
    return count;
}

static int DoGetInfo(int setIdx, int index, ProcAmpParamInfo* info)
{
    if (!info) return -1;
    ControlSetState& st = GetSet(setIdx);

    int count = 0;
    for (auto& e : st.entries) {
        if (!e.supported) continue;
        if (count == index) {
            info->propertyId  = e.id;
            info->minVal      = e.minVal;
            info->maxVal      = e.maxVal;
            info->stepVal     = e.stepVal;
            info->defaultVal  = e.defaultVal;
            info->currentVal  = e.currentVal;
            info->flags       = e.flags;
            info->supported   = 1;
            return 0;
        }
        count++;
    }
    return -1;
}

// 尝试对指定节点发送节点级 SET，返回是否成功。
// 依次尝试：同一缓冲区 → 独立缓冲区 → NULL 输出，适配不同驱动实现。
static bool TryNodeSet(HANDLE h, ULONG nodeId, const GUID& set, ULONG propertyId,
                        LONG value, ULONG flags, LONG& outValue)
{
    KSPROPERTY_VIDEOPROCAMP_NODE_S ns;
    ZeroMemory(&ns, sizeof(ns));
    ns.NodeProperty.Property.Set = set;
    ns.NodeProperty.Property.Id  = (ULONG)propertyId;
    ns.NodeProperty.Property.Flags = KSPROPERTY_TYPE_SET | KSPROPERTY_TYPE_TOPOLOGY;
    ns.NodeProperty.NodeId = nodeId;
    ns.Value = value;
    ns.Flags = flags;
    ns.Capabilities = 0;

    // 方式 A: 输入输出使用同一缓冲区（标准方式）
    {
        KSPROPERTY_VIDEOPROCAMP_NODE_S resp = ns;
        DWORD bytes = 0;
        if (KsIoctl(h, &ns, sizeof(ns), &resp, sizeof(resp), &bytes)) {
            outValue = resp.Value;
            return true;
        }
    }

    // 方式 B: 输入输出缓冲区分离（部分驱动要求独立缓冲区）
    {
        KSPROPERTY_VIDEOPROCAMP_NODE_S resp;
        ZeroMemory(&resp, sizeof(resp));
        DWORD bytes = 0;
        if (KsIoctl(h, &ns, sizeof(ns), &resp, sizeof(resp), &bytes)) {
            outValue = resp.Value;
            return true;
        }
    }

    // 方式 C: 输出缓冲区置 NULL（SET 操作可能不返回数据）
    {
        DWORD bytes = 0;
        if (KsIoctl(h, &ns, sizeof(ns), nullptr, 0, &bytes)) {
            outValue = value;
            return true;
        }
    }

    // 方式 D: 大输出缓冲区（部分驱动可能写入额外数据）
    {
        BYTE bigOut[1024];
        ZeroMemory(bigOut, sizeof(bigOut));
        DWORD bytes = 0;
        // 节点结构 Value 位于偏移 32，需至少 36 字节才有效，否则继续下一方式
        if (KsIoctl(h, &ns, sizeof(ns), bigOut, sizeof(bigOut), &bytes) && bytes >= 36) {
            KSPROPERTY_VIDEOPROCAMP_NODE_S* np = (KSPROPERTY_VIDEOPROCAMP_NODE_S*)bigOut;
            outValue = np->Value;
            return true;
        }
    }

    // 方式 E: 仅 4 字节值输出（部分驱动 SET 只回写值本身）
    {
        LONG outVal = 0;
        DWORD bytes = 0;
        if (KsIoctl(h, &ns, sizeof(ns), &outVal, sizeof(outVal), &bytes)) {
            outValue = outVal;
            return true;
        }
    }

    return false;
}

// SET 成功后的统一收尾（审查项 R9/R11）：
// - R9：写成功即证明写通道可用 → 置 st.writeOk（运行时重估：streaming 激活后
//   驱动可能恢复写能力，UI 滑块可随 flags 恢复解锁，不再依赖初始化一次性探测）
// - R11：KS 通道的 SET 返回值可能不可靠（驱动不回写/回写垃圾/截断回包），
//   readBack=true 时用 GET 读回权威当前值；读回失败才保留 SET 返回值
static void OnSetSucceeded(ControlSetState& st, ProcAmpEntry* entry, int propertyId,
                           LONG setValue, ULONG setFlags, bool readBack)
{
    st.writeOk = true;
    if (!entry) return;
    entry->flags = (long)setFlags;
    entry->currentVal = setValue;
    if (readBack) {
        LONG v = 0; ULONG f = 0;
        if (ReadCurrentValue(g_devHandle, st, (ULONG)propertyId, v, f)) {
            entry->currentVal = v;
            entry->flags = (long)f;
        }
    }
}

static int DoSet(int setIdx, int propertyId, long value, int autoMode)
{
    if (g_devHandle == INVALID_HANDLE_VALUE) return -1;

    ControlSetState& st = GetSet(setIdx);
    const GUID& set = *st.propSet;

    // 查找缓存条目
    ProcAmpEntry* entry = nullptr;
    for (auto& e : st.entries)
        if (e.id == propertyId && e.supported) { entry = &e; break; }

    LONG setValue = (LONG)(autoMode ? (entry ? entry->currentVal : value) : value);
    ULONG setFlags = autoMode ? KSPROPERTY_VIDEOPROCAMP_FLAGS_AUTO : KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
    DWORD lastErr = 0;

    // 钳制到属性范围，避免越界值被发往驱动（UVC 语义要求值在 [Min,Max] 内）。
    // 无论 autoMode 如何，有 entry 时始终钳制 setValue（autoMode 时 currentVal 也应
    // 在范围内；若 entry 为 null 则无法钳制，直接使用原始值）。
    if (entry) {
        if (setValue < entry->minVal) setValue = entry->minVal;
        if (setValue > entry->maxVal) setValue = entry->maxVal;
    }

    // IAM 写通道（AMCap 同款）：VideoProcAmp 用 IAMVideoProcAmp，CameraControl 用 IAMCameraControl。
    // 本设备 KS SET 通道已确证失败，直接复用 AMCap 同款通道，避免无效 IOCTL 与延迟。
    // IAM 失败时再回退 KS 尝试（设备状态变化后可能恢复）。
    // 注意：DirectShow VideoProcAmpProperty 枚举只到 Gain(9)，10-13 是 UVC 扩展，无 IAM 对应；
    // IAMCameraControl 的 CameraControlProperty 枚举到 19，全部可用。
    if (setIdx == SET_VIDEOPROCAMP && g_pIam && propertyId <= 9) {
        HRESULT hr = g_pIam->lpVtbl->Set(g_pIam, propertyId, setValue, setFlags);
        if (SUCCEEDED(hr)) {
            DbgLog("Set%s id=%u value=%ld auto=%d OK (IAMVideoProcAmp)", st.name, propertyId, setValue, autoMode);
            OnSetSucceeded(st, entry, propertyId, setValue, setFlags, false); // IAM 返回值可靠，无需回读
            SetSetLastHr(setIdx, S_OK);
            return 0;
        }
        DbgLog("Set%s id=%u value=%ld auto=%d IAMVideoProcAmp FAIL hr=0x%08lX（回退 KS）",
               st.name, propertyId, setValue, autoMode, (unsigned long)hr);
        SetSetLastHr(setIdx, hr);
    } else if (setIdx == SET_CAMERACONTROL && g_pCam) {
        HRESULT hr = g_pCam->lpVtbl->Set(g_pCam, propertyId, setValue, setFlags);
        if (SUCCEEDED(hr)) {
            DbgLog("Set%s id=%u value=%ld auto=%d OK (IAMCameraControl)", st.name, propertyId, setValue, autoMode);
            OnSetSucceeded(st, entry, propertyId, setValue, setFlags, false); // IAM 返回值可靠，无需回读
            SetSetLastHr(setIdx, S_OK);
            return 0;
        }
        DbgLog("Set%s id=%u value=%ld auto=%d IAMCameraControl FAIL hr=0x%08lX（回退 KS）",
               st.name, propertyId, setValue, autoMode, (unsigned long)hr);
        SetSetLastHr(setIdx, hr);
    }

    // IKsPropertySet 写通道（DirectShow 标准 KS 属性通道，VideoProcAmp/CameraControl 属性页底层）。
    // 无标准 PU 的设备 filter 不暴露 IAM 接口，但通常实现 IKsPropertySet。
    // PropertyData 必须传完整 KSPROPERTY_VIDEOPROCAMP_S（见 IamProbeRaw 注释）。
    if (g_pKsPropSet) {
        KSPROPERTY_VIDEOPROCAMP_S ks;
        ZeroMemory(&ks, sizeof(ks));
        ks.Property.Set = set;
        ks.Property.Id  = (ULONG)propertyId;
        ks.Property.Flags = KSPROPERTY_TYPE_SET;
        ks.Value = setValue;
        ks.Flags = setFlags;
        ks.Capabilities = 0;
        HRESULT hr = g_pKsPropSet->lpVtbl->Set(g_pKsPropSet, &set,
                                               (ULONG)propertyId, nullptr, 0,
                                               &ks, sizeof(ks));
        if (FAILED(hr)) {
            // 如果已知 PU 节点则优先用其 NodeId，再尝试常见的 nodeId=1 作为兜底。
            if (st.puNodeId != (ULONG)-1) {
                ULONG nodeId = st.puNodeId;
                hr = g_pKsPropSet->lpVtbl->Set(g_pKsPropSet, &set,
                                               (ULONG)propertyId, &nodeId, sizeof(nodeId),
                                               &ks, sizeof(ks));
            }
            if (FAILED(hr)) {
                ULONG nodeId = 1; // 常见 PU 节点
                hr = g_pKsPropSet->lpVtbl->Set(g_pKsPropSet, &set,
                                               (ULONG)propertyId, &nodeId, sizeof(nodeId),
                                               &ks, sizeof(ks));
            }
        }
        if (SUCCEEDED(hr)) {
            DbgLog("Set%s id=%u value=%ld auto=%d OK (IKsPropertySet)", st.name, propertyId, setValue, autoMode);
            if (entry) { entry->currentVal = setValue; entry->flags = setFlags; }
            SetSetLastHr(setIdx, S_OK);
            return 0;
        }
        DbgLog("Set%s id=%u value=%ld auto=%d IKsPropertySet FAIL hr=0x%08lX（回退 KS）",
               st.name, propertyId, setValue, autoMode, (unsigned long)hr);
        SetSetLastHr(setIdx, hr);
    }

    // IKsControl 写通道（AMCap 属性页底层接口，微软推荐；IKsPropertySet 失败后的补充）
    if (g_pKsControl) {
        KSPROPERTY_VIDEOPROCAMP_S ks;
        ZeroMemory(&ks, sizeof(ks));
        ks.Property.Set = set;
        ks.Property.Id  = (ULONG)propertyId;
        ks.Property.Flags = KSPROPERTY_TYPE_SET;
        ks.Value = setValue;
        ks.Flags = setFlags;
        ks.Capabilities = 0;
        ULONG bytes = 0;
        HRESULT hr = g_pKsControl->lpVtbl->KsProperty(g_pKsControl, &ks.Property, sizeof(ks),
                                                      &ks, sizeof(ks), &bytes);
        if (FAILED(hr)) {
            // 变体：PropertyLength 只给头
            hr = g_pKsControl->lpVtbl->KsProperty(g_pKsControl, &ks.Property, sizeof(KSPROPERTY),
                                                  &ks, sizeof(ks), &bytes);
        }
        // 变体：若设备存在真实 PU 节点，尝试节点级 KsProperty（包含 NodeId 的请求头 + 节点结构）
        if (FAILED(hr) && st.puNodeId != (ULONG)-1) {
            KSPROPERTY_VIDEOPROCAMP_NODE_S nfull;
            ZeroMemory(&nfull, sizeof(nfull));
            nfull.NodeProperty.Property.Set = set;
            nfull.NodeProperty.Property.Id  = (ULONG)propertyId;
            nfull.NodeProperty.Property.Flags = KSPROPERTY_TYPE_SET | KSPROPERTY_TYPE_TOPOLOGY;
            nfull.NodeProperty.NodeId = st.puNodeId;
            nfull.NodeProperty.Reserved = 0;
            nfull.Value = setValue;
            nfull.Flags = setFlags;
            nfull.Capabilities = 0;
            ULONG nb = 0;
            HRESULT hr2 = g_pKsControl->lpVtbl->KsProperty(g_pKsControl, (KSPROPERTY*)&nfull.NodeProperty, sizeof(nfull.NodeProperty),
                                                          &nfull, sizeof(nfull), &nb);
            DbgLog("Set%s id=%u IKsControl node-level attempt node=%lu hr=0x%08lX bytes=%lu", st.name, propertyId, st.puNodeId, (unsigned long)hr2, nb);
            if (SUCCEEDED(hr2)) hr = hr2;
        }
        if (SUCCEEDED(hr)) {
            DbgLog("Set%s id=%u value=%ld auto=%d OK (IKsControl)", st.name, propertyId, setValue, autoMode);
            OnSetSucceeded(st, entry, propertyId, setValue, setFlags, true); // KS 返回值可能不可靠，GET 回读
            SetSetLastHr(setIdx, S_OK);
            return 0;
        }
        DbgLog("Set%s id=%u value=%ld auto=%d IKsControl FAIL hr=0x%08lX（回退 KS）",
               st.name, propertyId, setValue, autoMode, (unsigned long)hr);
        SetSetLastHr(setIdx, hr);
    }

    // 检查 AccessFlags：若 BASICSUPPORT 明确报告不支持 SET，则提前警告
    if (entry && (entry->accessFlags & KSPROPERTY_TYPE_SET) == 0) {
        DbgLog("Set%s id=%u WARN: AccessFlags=0x%lX missing SET(0x2)", st.name, propertyId, entry->accessFlags);
    }

    // 尝试 Filter 级 SET
    {
        KSPROPERTY_VIDEOPROCAMP_S s;
        ZeroMemory(&s, sizeof(s));
        s.Property.Set = set;
        s.Property.Id  = (ULONG)propertyId;
        s.Property.Flags = KSPROPERTY_TYPE_SET;
        s.Value = setValue;
        s.Flags = setFlags;
        s.Capabilities = 0;

        // 方式 A: 同一缓冲区（标准方式）
        {
            KSPROPERTY_VIDEOPROCAMP_S resp = s;
            DWORD bytes = 0;
            if (KsIoctl(g_devHandle, &s, sizeof(s), &resp, sizeof(resp), &bytes)) {
                DbgLog("Set%s id=%u value=%ld auto=%d OK (filter)", st.name, propertyId, resp.Value, autoMode);
                OnSetSucceeded(st, entry, propertyId, resp.Value, setFlags, true); // GET 回读权威值
                return 0;
            }
            lastErr = GetLastError();
        }

        // 方式 B: 输出缓冲区为 NULL（部分驱动 SET 不返回数据）
        {
            DWORD bytes = 0;
            if (KsIoctl(g_devHandle, &s, sizeof(s), nullptr, 0, &bytes)) {
                DbgLog("Set%s id=%u value=%ld auto=%d OK (filter NULL out)", st.name, propertyId, s.Value, autoMode);
                OnSetSucceeded(st, entry, propertyId, s.Value, setFlags, true); // GET 回读权威值
                return 0;
            }
            lastErr = GetLastError();
        }

        // 方式 C: 大输出缓冲区（部分驱动可能写入额外数据）
        {
            BYTE bigOut[1024];
            ZeroMemory(bigOut, sizeof(bigOut));
            DWORD bytes = 0;
            // 结构 Value 位于偏移 24，需至少 28 字节才有效
            if (KsIoctl(g_devHandle, &s, sizeof(s), bigOut, sizeof(bigOut), &bytes) && bytes >= 28) {
                KSPROPERTY_VIDEOPROCAMP_S* p = (KSPROPERTY_VIDEOPROCAMP_S*)bigOut;
                DbgLog("Set%s id=%u value=%ld auto=%d OK (filter bigOut)", st.name, propertyId, p->Value, autoMode);
                if (entry) { entry->currentVal = p->Value; entry->flags = setFlags; }
                return 0;
            }
            lastErr = GetLastError();
        }

        // 方式 D: 仅 4 字节值输出（部分驱动 SET 只回写值本身）
        {
            LONG outVal = 0;
            DWORD bytes = 0;
            if (KsIoctl(g_devHandle, &s, sizeof(s), &outVal, sizeof(outVal), &bytes)) {
                DbgLog("Set%s id=%u value=%ld auto=%d OK (filter valOut=%ld)", st.name, propertyId, outVal, autoMode, outVal);
                if (entry) { entry->currentVal = outVal; entry->flags = setFlags; }
                return 0;
            }
            lastErr = GetLastError();
        }

        DbgLog("Set%s id=%u value=%ld auto=%d filter all FAIL err=%u hr=0x%08lX",
               st.name, propertyId, s.Value, autoMode, lastErr, (unsigned long)g_lastKsHr);
    }

    // 仿真设备（无真实 PU）节点级 SET 必失败，跳过以减少无效 IOCTL
    if (st.hasRealPu) {
        // 收集待尝试的候选节点 ID：先试已知节点，再试全部枚举节点。
        // 审查项 R15：候选上限 16 个，PU 拓扑节点 >16 时靠前的兜底节点
        // （0..3）仍会被尝试，漏试仅影响极端拓扑，保持上限防栈膨胀。
        ULONG candidateNodes[16];
        int numCandidates = 0;
        if (st.puNodeId != (ULONG)-1) {
            candidateNodes[numCandidates++] = st.puNodeId;
        }
        // 补充候选节点 0..st.numNodes-1（找不到拓扑时兜底到 0~3），避免与已知节点重复
        ULONG nodeLimit = st.numNodes > 0 ? st.numNodes : 4;
        for (ULONG n = 0; n < nodeLimit && numCandidates < 16; n++) {
            bool dup = false;
            for (int i = 0; i < numCandidates; i++) {
                if (candidateNodes[i] == n) { dup = true; break; }
            }
            if (!dup) candidateNodes[numCandidates++] = n;
        }

        // 遍历所有候选节点尝试节点级 SET
        for (int ci = 0; ci < numCandidates; ci++) {
            ULONG nid = candidateNodes[ci];
            LONG outVal = 0;
            if (TryNodeSet(g_devHandle, nid, set, (ULONG)propertyId, setValue, setFlags, outVal)) {
                DbgLog("Set%s id=%u value=%ld auto=%d OK (node %lu)", st.name, propertyId, outVal, autoMode, nid);
                // 发现工作节点，更新 st.puNodeId 供后续使用
                if (st.puNodeId != nid) {
                    st.puNodeId = nid;
                    DbgLog("  Discovered working node: %lu", nid);
                }
                OnSetSucceeded(st, entry, propertyId, outVal, setFlags, true); // GET 回读权威值
                return 0;
            }
            DbgLog("Set%s id=%u node=%lu FAIL err=%u hr=0x%08lX", st.name, propertyId, nid, GetLastError(), (unsigned long)g_lastKsHr);
        }
    }

    // 所有方式均失败
    bool hasIam = (setIdx == SET_CAMERACONTROL) ? (g_pCam != nullptr) : (g_pIam != nullptr);
    if (!st.hasRealPu && !hasIam)
        DbgLog("Set%s id=%u value=%ld auto=%d UNSUPPORTED (无真实 PU 节点，仅只读)", st.name, propertyId, setValue, autoMode);
    else
        DbgLog("Set%s id=%u value=%ld auto=%d UNSUPPORTED (KS+IAM 均失败)", st.name, propertyId, setValue, autoMode);
    // 全链失败但 GetLastError 可能为 0（如最后一步是 IAM/IKs COM 失败）：兜底为
    // 通用失败码，避免 GetLastProcAmpError 返回 0（"无错误"）与失败返回 -2 矛盾。
    if (lastErr == 0) lastErr = ERROR_GEN_FAILURE;
    SetSetLastHr(setIdx, HRESULT_FROM_WIN32(lastErr));
    return -2;
}

static int DoGet(int setIdx, int propertyId, long* value, long* flags)
{
    if (g_devHandle == INVALID_HANDLE_VALUE) return -1;

    ControlSetState& st = GetSet(setIdx);
    const GUID& set = *st.propSet;

    // 从内存缓存返回，不向硬件发送 GET（部分 UVC 驱动不支持）。
    // IAM / IKsPropertySet 通道可用时，先读真实硬件当前值刷新缓存。
    for (auto& e : st.entries) {
        if (e.id == propertyId && e.supported) {
            if (setIdx == SET_VIDEOPROCAMP && g_pIam && propertyId <= 9) {
                long v = 0, f = 0;
                HRESULT hr = g_pIam->lpVtbl->Get(g_pIam, propertyId, &v, &f);
                if (SUCCEEDED(hr)) {
                    e.currentVal = v;
                    // 只读组保持 flags 只有 AUTO 模式位：能力位以初始化实测判定为准，
                    // 驱动返回的 flags 是"当前模式位"，若整值覆盖会复活 MANUAL（滑块误
                    // 可用）；保留 AUTO 位用于自动复选框显示（如自动曝光状态）。
                    if (st.writeOk) e.flags = f;
                    else e.flags = f & KSPROPERTY_VIDEOPROCAMP_FLAGS_AUTO;
                    DbgLog("Get%s id=%u IAM value=%ld flags=0x%lX", st.name, propertyId, v, (unsigned long)f);
                }
            } else if (setIdx == SET_CAMERACONTROL && g_pCam) {
                long v = 0, f = 0;
                HRESULT hr = g_pCam->lpVtbl->Get(g_pCam, propertyId, &v, &f);
                if (SUCCEEDED(hr)) {
                    e.currentVal = v;
                    if (st.writeOk) e.flags = f;
                    else e.flags = f & KSPROPERTY_VIDEOPROCAMP_FLAGS_AUTO;
                    DbgLog("Get%s id=%u IAM value=%ld flags=0x%lX", st.name, propertyId, v, (unsigned long)f);
                }
            } else if (g_pKsPropSet || g_pKsControl) {
                // 优先 IKsPropertySet::Get，失败再用 IKsControl::KsProperty（AMCap 属性页通道）
                bool refreshed = false;
                if (g_pKsPropSet) {
                    KSPROPERTY_VIDEOPROCAMP_S ks;
                    ZeroMemory(&ks, sizeof(ks));
                    ks.Property.Set = set;
                    ks.Property.Id  = (ULONG)propertyId;
                    ks.Property.Flags = KSPROPERTY_TYPE_GET;
                    ks.Value = 0;
                    ks.Flags = KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
                    ks.Capabilities = 0;
                    ULONG bytes = 0;
                    HRESULT hr = g_pKsPropSet->lpVtbl->Get(g_pKsPropSet, &set,
                                                           (ULONG)propertyId, nullptr, 0,
                                                           &ks, sizeof(ks), &bytes);
                    if (SUCCEEDED(hr)) {
                        refreshed = true;
                        e.currentVal = ks.Value;
                        if (st.writeOk) e.flags = ks.Flags;
                        else e.flags = ks.Flags & KSPROPERTY_VIDEOPROCAMP_FLAGS_AUTO;
                        DbgLog("Get%s id=%u IKs value=%ld flags=0x%lX", st.name, propertyId, ks.Value, (unsigned long)ks.Flags);
                    }
                }
                if (!refreshed && g_pKsControl) {
                    KSPROPERTY_VIDEOPROCAMP_S ks;
                    ZeroMemory(&ks, sizeof(ks));
                    ks.Property.Set = set;
                    ks.Property.Id  = (ULONG)propertyId;
                    ks.Property.Flags = KSPROPERTY_TYPE_GET;
                    ks.Value = 0;
                    ks.Flags = KSPROPERTY_VIDEOPROCAMP_FLAGS_MANUAL;
                    ks.Capabilities = 0;
                    ULONG bytes = 0;
                    HRESULT hr = g_pKsControl->lpVtbl->KsProperty(g_pKsControl, &ks.Property, sizeof(ks),
                                                                  &ks, sizeof(ks), &bytes);
                    if (FAILED(hr))
                        hr = g_pKsControl->lpVtbl->KsProperty(g_pKsControl, &ks.Property, sizeof(KSPROPERTY),
                                                              &ks, sizeof(ks), &bytes);
                    if (SUCCEEDED(hr)) {
                        e.currentVal = ks.Value;
                        if (st.writeOk) e.flags = ks.Flags;
                        else e.flags = ks.Flags & KSPROPERTY_VIDEOPROCAMP_FLAGS_AUTO;
                        DbgLog("Get%s id=%u IKsControl value=%ld flags=0x%lX", st.name, propertyId, ks.Value, (unsigned long)ks.Flags);
                    }
                }
            }
            if (value) *value = e.currentVal;
            if (flags) *flags = e.flags;
            return 0;
        }
    }
    return -1;
}

// 在工作线程上执行一条命令。整体包 SEH：任何 KS/COM/驱动访问违例都不会拖垮进程，
// 统一记录并返回错误码 -3（与 Do* 的常规负返回区分）。
// 注意：__try 作用域内不得声明带析构的 C++ 自动对象（本函数仅用 POD 局部变量）。
static int SafeDispatch()
{
    int rc = 0;
    __try {
        switch (g_cmd.op) {
            case OP_INIT:     rc = DoInit(g_cmd.dev);     break;
            case OP_RELEASE:  rc = DoRelease();           break;
            case OP_GETCOUNT: rc = DoGetCount(g_cmd.setIdx);          break;
            case OP_GETINFO:  rc = DoGetInfo(g_cmd.setIdx, g_cmd.idx, g_cmd.info); break;
            case OP_SET:      rc = DoSet(g_cmd.setIdx, g_cmd.pid, g_cmd.val, g_cmd.autoMode); break;
            case OP_GET:      rc = DoGet(g_cmd.setIdx, g_cmd.pid, g_cmd.oVal, g_cmd.oFlags); break;
            case OP_ENUMFORMATS: rc = DoEnumFormats(g_cmd.dev, g_cmd.fmtBuf, g_cmd.fmtCap); break;
            default:          rc = -1;                    break;
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        DbgLog("Worker: op=%d 执行时访问违例 code=0x%08X（已捕获，命令返回错误）",
               g_cmd.op, (unsigned)GetExceptionCode());
        // 初始化中途崩溃可能残留半初始化状态：清空以便下次干净重试。
        // 崩溃后 COM 指针槽可能已被写坏，DoCleanup 内 Release/CloseHandle 可能
        // 再次崩溃——__except 内二次异常会直接终止进程，必须再包一层 SEH 兜底。
        if (g_cmd.op == OP_INIT) {
            __try {
                DoCleanup();
            }
            __except (EXCEPTION_EXECUTE_HANDLER) {
                DbgLog("Worker: DoCleanup 二次异常 code=0x%08X（已忽略，交由进程退出回收）",
                       (unsigned)GetExceptionCode());
            }
        }
        rc = -3;
    }
    return rc;
}

// 工作线程主循环：循环处理命令。
static DWORD WINAPI WorkerThread(LPVOID)
{
    for (;;) {
        WaitForSingleObject(g_hReq, INFINITE);
        if (!g_run) break;
        // 快照命令代次：若本命令执行期间调用方已超时返回、命令槽被新命令覆盖，
        // 完成时不得 Set g_hDone——否则新命令的等待者会读到本命令（旧命令）的
        // result（命令结果错配竞态）。新命令完成时由其自身代次校验后 Set。
        LONG myGen = g_cmd.gen;
        g_cmd.result = SafeDispatch();
        if (myGen == g_cmd.gen)
            SetEvent(g_hDone);
    }
    // 线程退出前自清理：即使 DllMain 因加载器锁不再等待，COM/句柄也在本线程
    // 释放（OleUninitialize 必须与 OleInitialize 同线程）。幂等，可安全重复调用。
    DoCleanup();
    return 0;
}

// 确保工作线程与事件已创建。必须持有 g_cmdLock 时调用，避免多线程重复创建
// （TOCTOU）。返回 false 表示创建失败（调用方返回错误码，不做等待）。
static bool EnsureWorkerThread()
{
    if (g_hThread != NULL) {
        // 工作线程若已退出（Shutdown 等待成功后由调用方清理了句柄则不会走到这里；
        // 此处覆盖的是线程异常死亡、或 Shutdown 超时后线程完成自清理退出的场景），
        // 句柄残留：清理并重建。注意：仅在确认线程已退出时才重建——若线程仍卡在
        // 驱动 IOCTL/COM 中，重建会让新旧两个线程同时访问全局槽与设备句柄，绝不重建。
        if (WaitForSingleObject(g_hThread, 0) == WAIT_OBJECT_0) {
            DbgLog("EnsureWorkerThread: 检测到工作线程已退出，重建线程");
            CloseHandle(g_hThread);   g_hThread = NULL;
            if (g_hReq)  { CloseHandle(g_hReq);  g_hReq = NULL; }
            if (g_hDone) { CloseHandle(g_hDone); g_hDone = NULL; }
        } else {
            return true;
        }
    }
    g_hReq  = CreateEvent(nullptr, FALSE, FALSE, nullptr);
    g_hDone = CreateEvent(nullptr, FALSE, FALSE, nullptr);
    g_hThread = CreateThread(nullptr, 0, WorkerThread, nullptr, 0, nullptr);
    if (g_hThread != NULL) {
        InterlockedExchange(&g_run, 1); // 若此前执行过 Shutdown，复位运行标志以便重建
        return true;
    }
    // 创建失败：清理已创建的事件，避免悬挂半初始化状态
    if (g_hReq)  { CloseHandle(g_hReq);  g_hReq = NULL; }
    if (g_hDone) { CloseHandle(g_hDone); g_hDone = NULL; }
    return false;
}

// 唤醒工作线程并等待本次命令完成（必须持有 g_cmdLock 时调用）。
// 返回命令结果，或负错误码：-3=超时，-4=等待失败。
static int WaitCommandDone(DWORD timeoutMs)
{
    // 清掉可能残留的完成信号：若上一命令因调用方超时提前返回，工作线程随后
    // 完成时会 SetEvent(g_hDone) 留下 signaled 残留——不清除会导致下一命令的
    // 等待瞬间返回并读到上一命令的 result（假成功/假失败，命令语义错乱）。
    ResetEvent(g_hDone);
    SetEvent(g_hReq);
    DWORD wr = WaitForSingleObject(g_hDone, timeoutMs);
    if (wr == WAIT_OBJECT_0) return g_cmd.result;
    if (wr == WAIT_TIMEOUT) {
        DbgLog("WaitCommandDone: 等待超时 (%lu ms)，命令可能仍在工作线程执行（将被后续命令丢弃）", timeoutMs);
        return -3;
    }
    DbgLog("WaitCommandDone: 等待失败 wr=%lu err=%u", wr, GetLastError());
    return -4;
}

// 进程卸载时由 DllMain 调用：通知并等待工作线程退出，释放句柄。
// 有意不获取 g_cmdLock：DllMain 持加载器锁，若再等待用户线程的锁，而该线程又
// 在等待工作线程（工作线程可能因 LoadLibrary 需要加载器锁），会形成死锁链。
// 这里只做有界等待——工作线程退出前会自清理（见 WorkerThread），超时说明其
// 卡在驱动 IOCTL/COM 中，进程卸载时由系统回收，不强制关闭句柄。
UVC_API void ShutdownProcAmpCom()
{
    if (g_hThread == NULL) return;
    InterlockedExchange(&g_run, 0);
    SetEvent(g_hReq);
    // 等待 3s：正常情况线程在完成当前命令后退出。超时（线程卡在驱动 IOCTL/COM）
    // 时不强杀：线程从 IOCTL 返回后自清理退出（WorkerThread 末尾 DoCleanup）。
    // 审查项 R12：超时后紧跟的新命令会因无人消费而等到 kOpTimeoutMs（一次性），
    // 线程退出后 EnsureWorkerThread 检测到线程已死会自动重建（自愈）。
    if (WaitForSingleObject(g_hThread, 3000) == WAIT_OBJECT_0) {
        CloseHandle(g_hThread);   g_hThread = NULL;
        if (g_hReq)  { CloseHandle(g_hReq);  g_hReq = NULL; }
        if (g_hDone) { CloseHandle(g_hDone); g_hDone = NULL; }
    }
}

// ===================== 导出接口（调用方线程 → 工作线程） =====================
//
// 并发模型：所有"写命令槽→等待结果"均持有 g_cmdLock，多调用线程天然串行化，
// 避免 g_cmd 被踩踏、以及多个调用方同时等待同一自动复位事件造成永久阻塞。
// 等待均带超时（kInitTimeoutMs/kOpTimeoutMs）：工作线程异常/驱动挂起时调用方
// 返回负错误码而不是永久卡死。OP_GETINFO/OP_GET 的结果先写入工作线程侧缓冲
// （g_cmd.out*），调用方仅在等待成功后才拷贝到自己的内存，杜绝超时提前返回后
// 工作线程向已释放内存写入。
//
// 返回码约定（与历史兼容）：>=0 成功；<0 失败。新增：-3=命令超时/SEH 捕获，
// -4=等待失败，-5=工作线程创建失败。

// ---- 宏：减少 VideoProcAmp / CameraControl 两套导出接口的代码重复 ----
// 四个核心接口（GetCount / GetInfo / SetValue / GetValue）在图像属性与相机控制
// 两套集合上仅 setIdx 不同，用宏展开消除重复函数体。

#define DEFINE_CONTROL_GET_COUNT(FuncName, SetIdx) \
UVC_API int FuncName() { \
    AcquireSRWLockExclusive(&g_cmdLock); \
    int rc = -5; \
    if (EnsureWorkerThread()) { \
        g_cmd.op = OP_GETCOUNT; g_cmd.setIdx = SetIdx; \
        g_cmd.gen = InterlockedIncrement(&g_cmdGen); \
        rc = WaitCommandDone(kOpTimeoutMs); \
    } \
    ReleaseSRWLockExclusive(&g_cmdLock); \
    return rc; \
}

#define DEFINE_CONTROL_GET_INFO(FuncName, SetIdx) \
UVC_API int FuncName(int index, ProcAmpParamInfo* info) { \
    if (!info) return -1; \
    AcquireSRWLockExclusive(&g_cmdLock); \
    int rc = -5; \
    if (EnsureWorkerThread()) { \
        g_cmd.op = OP_GETINFO; g_cmd.setIdx = SetIdx; g_cmd.idx = index; \
        g_cmd.gen = InterlockedIncrement(&g_cmdGen); \
        g_cmd.info = &g_cmd.outInfo; \
        rc = WaitCommandDone(kOpTimeoutMs); \
        if (rc == 0) *info = g_cmd.outInfo; \
    } \
    ReleaseSRWLockExclusive(&g_cmdLock); \
    return rc; \
}

#define DEFINE_CONTROL_SET_VALUE(FuncName, SetIdx) \
UVC_API int FuncName(int propertyId, long value, int autoMode) { \
    AcquireSRWLockExclusive(&g_cmdLock); \
    int rc = -5; \
    if (EnsureWorkerThread()) { \
        g_cmd.op = OP_SET; g_cmd.setIdx = SetIdx; \
        g_cmd.pid = propertyId; g_cmd.val = value; g_cmd.autoMode = autoMode; \
        g_cmd.gen = InterlockedIncrement(&g_cmdGen); \
        rc = WaitCommandDone(kOpTimeoutMs); \
    } \
    ReleaseSRWLockExclusive(&g_cmdLock); \
    return rc; \
}

#define DEFINE_CONTROL_GET_VALUE(FuncName, SetIdx) \
UVC_API int FuncName(int propertyId, long* value, long* flags) { \
    AcquireSRWLockExclusive(&g_cmdLock); \
    int rc = -5; \
    if (EnsureWorkerThread()) { \
        g_cmd.op = OP_GET; g_cmd.setIdx = SetIdx; \
        g_cmd.pid = propertyId; g_cmd.oVal = &g_cmd.outVal; g_cmd.oFlags = &g_cmd.outFlags; \
        g_cmd.gen = InterlockedIncrement(&g_cmdGen); \
        rc = WaitCommandDone(kOpTimeoutMs); \
        if (rc == 0) { \
            if (value) *value = g_cmd.outVal; \
            if (flags) *flags = g_cmd.outFlags; \
        } \
    } \
    ReleaseSRWLockExclusive(&g_cmdLock); \
    return rc; \
}

// 宏展开：生成 VideoProcAmp 与 CameraControl 两套接口
DEFINE_CONTROL_GET_COUNT(GetProcAmpCount, SET_VIDEOPROCAMP)
DEFINE_CONTROL_GET_INFO(GetProcAmpInfo, SET_VIDEOPROCAMP)
DEFINE_CONTROL_SET_VALUE(SetProcAmpValue, SET_VIDEOPROCAMP)
DEFINE_CONTROL_GET_VALUE(GetProcAmpValue, SET_VIDEOPROCAMP)

DEFINE_CONTROL_GET_COUNT(GetCameraControlCount, SET_CAMERACONTROL)
DEFINE_CONTROL_GET_INFO(GetCameraControlInfo, SET_CAMERACONTROL)
DEFINE_CONTROL_SET_VALUE(SetCameraControlValue, SET_CAMERACONTROL)
DEFINE_CONTROL_GET_VALUE(GetCameraControlValue, SET_CAMERACONTROL)

// ===================== Init / Release / Shutdown（唯一接口，无法用宏生成） =====================

UVC_API int InitProcAmp(const char* deviceName)
{
    if (!deviceName) return -1;
    AcquireSRWLockExclusive(&g_cmdLock);
    int rc = -5;
    if (EnsureWorkerThread()) {
        // 拷贝到自有缓冲：即使调用方超时提前返回，工作线程也读本缓冲而非调用方内存
        // 设备名超出缓冲会被截断（strncpy_s _TRUNCATE 静默截断）：匹配失败前先告警
        if (strncpy_s(g_devNameBuf, kDevNameBufSize, deviceName, _TRUNCATE) == STRUNCATE)
            DbgLog("InitProcAmp: 设备名被截断到 %u 字节，后续匹配可能失败",
                   (unsigned)kDevNameBufSize - 1);
        g_cmd.op = OP_INIT; g_cmd.dev = g_devNameBuf;
        g_cmd.gen = InterlockedIncrement(&g_cmdGen); // 命令代次：区分超时残留与当前命令
        rc = WaitCommandDone(kInitTimeoutMs);
    }
    ReleaseSRWLockExclusive(&g_cmdLock);
    return rc;
}

UVC_API void ReleaseProcAmp()
{
    AcquireSRWLockExclusive(&g_cmdLock);
    if (EnsureWorkerThread()) {
        g_cmd.op = OP_RELEASE;
        g_cmd.gen = InterlockedIncrement(&g_cmdGen);
        WaitCommandDone(kOpTimeoutMs);
    }
    ReleaseSRWLockExclusive(&g_cmdLock);
}

// 获取最近一次 Proc Amp 调用的失败 HRESULT（供 C# 端诊断，0 表示成功/无错误）。
UVC_API int GetLastProcAmpError()
{
    // 原子读：工作线程经 InterlockedExchange 写入
    return (int)InterlockedCompareExchange(&g_lastHr, 0, 0);
}

// ===================== Camera Control 导出接口（相机控制面板，AMCap 同款） =====================
// 与图像属性共用同一设备句柄/工作线程：InitProcAmp 初始化时两套控制一并初始化，
// ReleaseProcAmp 一并释放。以下接口仅针对 CameraControl 集合。
// 注意：GetCount/GetInfo/SetValue/GetValue 已由上方宏展开生成，此处仅保留
// GetLastCameraControlError（因读取的全局变量 g_lastCcHr 不同）。

// 获取最近一次 Camera Control 调用的失败 HRESULT（供 C# 端诊断，0 表示成功/无错误）。
UVC_API int GetLastCameraControlError()
{
    return (int)InterlockedCompareExchange(&g_lastCcHr, 0, 0);
}

// 枚举指定设备支持的所有视频格式（分辨率 + 像素格式 FOURCC），经 DirectShow
// IAMStreamConfig::GetStreamCaps（与属性通道同源的 COM 探测，工作线程执行，
// SEH 保护）。deviceName 同 InitProcAmp（支持 "video=" 前缀与包含匹配）。
// 返回：>=0 实际格式数量（已按分辨率升序、去重）；-1 参数非法/设备未找到/探测失败；
// -3 等待超时；-4 等待失败。
UVC_API int EnumVideoFormats(const char* deviceName, VideoFormatInfo* outBuf, int bufLen)
{
    if (!deviceName || !outBuf || bufLen <= 0) return -1;
    AcquireSRWLockExclusive(&g_cmdLock);
    int rc = -5;
    if (EnsureWorkerThread()) {
        if (strncpy_s(g_devNameBuf, kDevNameBufSize, deviceName, _TRUNCATE) == STRUNCATE)
            DbgLog("EnumVideoFormats: 设备名被截断到 %u 字节，后续匹配可能失败",
                   (unsigned)kDevNameBufSize - 1);
        g_devNameBuf[kDevNameBufSize - 1] = 0;
        g_cmd.op = OP_ENUMFORMATS; g_cmd.dev = g_devNameBuf;
        g_cmd.fmtBuf = g_cmd.outFormats;
        g_cmd.fmtCap = (bufLen > (int)(sizeof(g_cmd.outFormats) / sizeof(g_cmd.outFormats[0])))
                       ? (int)(sizeof(g_cmd.outFormats) / sizeof(g_cmd.outFormats[0])) : bufLen;
        g_cmd.gen = InterlockedIncrement(&g_cmdGen);
        rc = WaitCommandDone(kOpTimeoutMs);
        if (rc > 0) {
            int n = (rc < g_cmd.fmtCap) ? rc : g_cmd.fmtCap;
            memcpy(outBuf, g_cmd.outFormats, n * sizeof(VideoFormatInfo));
        }
    }
    ReleaseSRWLockExclusive(&g_cmdLock);
    return rc;
}
