#pragma once

#include "USBDeviceMonitor.h"

#include <map>
#include <vector>
#include <string>
#include <SetupAPI.h>

// 新增视频设备枚举所需头文件
#include <strmif.h>
#include <initguid.h>


#include "Data/AX32XXDevice.h"

// 新增DirectShow枚举所需GUID定义
DEFINE_GUID(CLSID_SystemDeviceEnum, 0x62be5d10, 0x60eb, 0x11d0, 0xbd, 0x3b, 0x00, 0xa0, 0xc9, 0x11, 0xce, 0x86);
DEFINE_GUID(CLSID_VideoInputDeviceCategory, 0x860bb310, 0x5d01, 0x11d0, 0xbd, 0x3b, 0x00, 0xa0, 0xc9, 0x11, 0xce, 0x86);
DEFINE_GUID(IID_ICreateDevEnum, 0x29840822, 0x5b84, 0x11d0, 0xbd, 0x3b, 0x00, 0xa0, 0xc9, 0x11, 0xce, 0x86);
DEFINE_GUID(GUID_VIDEO_CAPTURE, 0x65E87780, 0x9F72, 0x11D0, 0xB3, 0xB3, 0x00, 0xA0, 0xC9, 0x22, 0x31, 0x96);

// 摄像头信息结构体
struct CameraInfo
{
    std::string friendlyName;  // 设备友好名称
    std::string symbolicLink;  // 符号链接
    std::wstring instanceId;   // 设备实例ID
};

class DeviceManager
{
public:

    static DeviceManager& GetInstance();

    void Initialize();
    void Uninitialize();

    void AddSupportedHardwareId(const wchar_t* deviceModel, const wchar_t* hardwareId);
    void RemoveSupportedHardwareId(const wchar_t* deviceModel);

    void AddDevChangeNotifyFunc(std::function<void(int,const wchar_t*, const wchar_t*, const wchar_t*)> deviceChangeNotifyFunc);
    void RemoveDevChangeNotifyFunc();

    // 新增：宽字符转UTF8字符串
    std::string WideToString(const wchar_t* wstr);
    // 新增：枚举所有视频输入设备（摄像头）
    int ListVideoDevices(std::vector<CameraInfo>& cameraList);
    // 新增：刷新摄像头列表并输出日志
    void RefreshCameraList();

    void ScanDevice();

    AX32XXDevice* GetDevice(const wchar_t* devLocation);

private:
    DeviceManager();
    ~DeviceManager();

    AX32XXDevice* _AddDevice(const wchar_t* devSymbolicLink, SP_DEVINFO_DATA devInfoData);
    void _RemoveDevice(const wchar_t* devSymbolicLink);

    bool _IsSupportedDevice(SP_DEVINFO_DATA devInfoData);
    bool _IsSupportedDevice(SP_DEVINFO_DATA devInfoData, HDEVINFO deviceInfoSet);
    std::wstring _GetDeviceHardwareId(SP_DEVINFO_DATA devInfoData);
    std::wstring _GetDeviceHardwareId(SP_DEVINFO_DATA devInfoData, HDEVINFO deviceInfoSet);
    SP_DEVINFO_DATA _GetDeviceInfo(const wchar_t* devSymbolicLink);
    SP_DEVINFO_DATA _GetDeviceInfo(const wchar_t* devSymbolicLink, HDEVINFO deviceInfoSet);
    void _OnUsbEvent(DeviceEvent event, const wchar_t* devSymbolicLink);

    // 新增：从设备实例ID获取符号链接
    std::string GetSymbolicLinkFromInstanceId(const std::wstring& instanceId);

    HDEVINFO m_DeviceInfoSet;
    USBDeviceMonitor m_usbDevMonitor;

    std::map<std::wstring, AX32XXDevice*> m_Ax32xxDevMap;
    std::function<void(int event,
        const wchar_t* devLocation, const wchar_t* devModel, const wchar_t* uvcInterfaceName)> m_deviceChangeNotifyFunc;

    static std::map<std::wstring, std::wstring> m_SupportedHardwareSpecificsMap;
    // 新增：存储上次枚举的摄像头列表（用于对比更新）
    std::vector<CameraInfo> m_lastCameras;
};


// 链接库（也可在工程属性中配置）
#pragma comment(lib, "setupapi.lib")
#pragma comment(lib, "ole32.lib")
