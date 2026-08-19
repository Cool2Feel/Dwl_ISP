/**
 * UsbDeviceReset.cpp - USB设备软件复位（模拟插拔）
 *
 * 功能：通过Windows SetupAPI禁用并重新启用USB设备，
 *       实现软件方式的设备"重新插拔"效果
 *
 * 原理：
 *   1. 通过设备符号链接获取设备信息
 *   2. 使用 SetupAPI 禁用设备
 *   3. 等待设备完全断开
 *   4. 重新启用设备
 *   5. 等待设备重新枚举完成
 *
 * 优势：
 *   - 无需物理插拔
 *   - 自动触发系统重新枚举
 *   - 适用于所有USB设备类型
 *
 * 注意：
 *   - 需要管理员权限
 *   - 设备会短暂不可用（约2-3秒）
 */

#include "Misc\stdafx.h"
#include "UsbDeviceReset.h"
#include <setupapi.h>
#include <cfgmgr32.h>
#include <devguid.h>
#include <regstr.h>
#include <initguid.h>
#include <usbiodef.h>
#include <locale.h>
#include <comdef.h>

#pragma comment(lib, "setupapi.lib")
#pragma comment(lib, "cfgmgr32.lib")

// 调试输出宏
#define DBG_PRINT(fmt, ...) printf("[USB-Reset] " fmt "\n", __VA_ARGS__)

/**
 * 从设备符号链接解析设备信息
 *
 * @param deviceSymbolicLink 设备符号链接（例如：\\?\USB#VID_XXXX&PID_YYYY#...）
 * @param outDevInfo 输出的设备信息结构
 * @return 是否成功
 */
static bool ParseDeviceLink(const std::wstring& deviceSymbolicLink, UsbDeviceInfo& outDevInfo)
{
    if (deviceSymbolicLink.empty()) {
        DBG_PRINT("ERROR: Empty device link");
        return false;
    }

    // 复制链接到输出结构
    wcsncpy_s(outDevInfo.SymbolicLink, 
              deviceSymbolicLink.c_str(), 
              _TRUNCATE);

    // 从符号链接中提取VID和PID
    // 格式：\\?\USB#VID_XXXX&PID_YYYY#...
    std::wstring link = deviceSymbolicLink;
    
    size_t vidPos = link.find(L"VID_");
    if (vidPos != std::wstring::npos) {
        wcsncpy_s(outDevInfo.VendorId, 
                  link.substr(vidPos + 4, 4).c_str(), 
                  _TRUNCATE);
    }

    size_t pidPos = link.find(L"PID_");
    if (pidPos != std::wstring::npos) {
        wcsncpy_s(outDevInfo.ProductId, 
                  link.substr(pidPos + 4, 4).c_str(), 
                  _TRUNCATE);
    }

    //DBG_PRINT("Parsed device: VID_%s PID_%s", 
    //          CT2A(outDevInfo.VendorId), 
    //          CT2A(outDevInfo.ProductId));

    return true;
}

/**
 * 通过 SetupAPI 查找设备的 DEVINST 句柄
 *
 * @param deviceSymbolicLink 设备符号链接
 * @param outDevInst 输出的设备实例句柄
 * @return 是否成功
 */
static bool FindDeviceInstance(const std::wstring& deviceSymbolicLink, DEVINST& outDevInst)
{
    HDEVINFO hDevInfo = INVALID_HANDLE_VALUE;
    SP_DEVINFO_DATA deviceInfoData;
    BOOL bSuccess = FALSE;

    // 获取所有USB设备的信息
    hDevInfo = SetupDiGetClassDevs(NULL, L"USB", NULL, DIGCF_PRESENT | DIGCF_ALLCLASSES);
    
    if (hDevInfo == INVALID_HANDLE_VALUE) {
        DBG_PRINT("ERROR: SetupDiGetClassDevs failed, error: %lu", GetLastError());
        return false;
    }

    deviceInfoData.cbSize = sizeof(SP_DEVINFO_DATA);

    // 遍历所有USB设备
    for (DWORD i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, &deviceInfoData); i++) {
        WCHAR deviceId[MAX_PATH];
        DWORD requiredSize = 0;

        // 获取设备ID
        if (!SetupDiGetDeviceInstanceId(hDevInfo, &deviceInfoData, 
                                        deviceId, MAX_PATH, &requiredSize)) {
            continue;
        }

        // 检查是否匹配目标设备
        // 设备符号链接包含设备ID的信息
        if (deviceSymbolicLink.find(deviceId) != std::wstring::npos) {
            outDevInst = deviceInfoData.DevInst;
            DBG_PRINT("Found device instance: %ls (DEVINST: 0x%08X)", 
                      deviceId, outDevInst);
            
            SetupDiDestroyDeviceInfoList(hDevInfo);
            return true;
        }
    }

    SetupDiDestroyDeviceInfoList(hDevInfo);
    
    DBG_PRINT("ERROR: Device instance not found for: %ls", 
              deviceSymbolicLink.c_str());
    return false;
}

/**
 * 禁用设备实例
 *
 * @param devInst 设备实例句柄
 * @return 是否成功
 */
static bool DisableDeviceInstance(DEVINST devInst)
{
    HDEVINFO hDevInfo = INVALID_HANDLE_VALUE;
    SP_DEVINFO_DATA deviceInfoData;
    SP_PROPCHANGE_PARAMS propChangeParams;
    BOOL bSuccess = FALSE;

    // 获取设备信息集（通过DEVINST）
    hDevInfo = SetupDiGetClassDevs(NULL, NULL, NULL, DIGCF_ALLCLASSES | DIGCF_PRESENT);
    if (hDevInfo == INVALID_HANDLE_VALUE) {
        DBG_PRINT("ERROR: SetupDiGetClassDevs failed, error: %lu", GetLastError());
        return false;
    }

    deviceInfoData.cbSize = sizeof(SP_DEVINFO_DATA);

    // 查找匹配的DeviceInfoData
    BOOL bFound = FALSE;
    for (DWORD i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, &deviceInfoData); i++) {
        if (deviceInfoData.DevInst == devInst) {
            bFound = TRUE;
            break;
        }
    }

    if (!bFound) {
        DBG_PRINT("ERROR: DeviceInfoData not found for DEVINST: 0x%08X", devInst);
        SetupDiDestroyDeviceInfoList(hDevInfo);
        return false;
    }

    // 设置禁用参数
    propChangeParams.ClassInstallHeader.cbSize = sizeof(SP_CLASSINSTALL_HEADER);
    propChangeParams.ClassInstallHeader.InstallFunction = DIF_PROPERTYCHANGE;
    propChangeParams.Scope = DICS_FLAG_GLOBAL;
    propChangeParams.StateChange = DICS_DISABLE;
    propChangeParams.HwProfile = 0;

    // 安装参数
    if (!SetupDiSetClassInstallParams(hDevInfo, &deviceInfoData, 
                                      (SP_CLASSINSTALL_HEADER*)&propChangeParams, 
                                      sizeof(propChangeParams))) {
        DBG_PRINT("ERROR: SetupDiSetClassInstallParams failed, error: %lu", GetLastError());
        SetupDiDestroyDeviceInfoList(hDevInfo);
        return false;
    }

    // 执行禁用操作
    if (!SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, hDevInfo, &deviceInfoData)) {
        DBG_PRINT("ERROR: SetupDiCallClassInstaller(DICS_DISABLE) failed, error: %lu", 
                  GetLastError());
        SetupDiDestroyDeviceInfoList(hDevInfo);
        return false;
    }

    DBG_PRINT("Device disabled successfully (DEVINST: 0x%08X)", devInst);

    SetupDiDestroyDeviceInfoList(hDevInfo);
    return true;
}

/**
 * 启用设备实例
 *
 * @param devInst 设备实例句柄
 * @return 是否成功
 */
static bool EnableDeviceInstance(DEVINST devInst)
{
    HDEVINFO hDevInfo = INVALID_HANDLE_VALUE;
    SP_DEVINFO_DATA deviceInfoData;
    SP_PROPCHANGE_PARAMS propChangeParams;

    // 获取设备信息集
    hDevInfo = SetupDiGetClassDevs(NULL, NULL, NULL, DIGCF_ALLCLASSES | DIGCF_PRESENT);
    if (hDevInfo == INVALID_HANDLE_VALUE) {
        DBG_PRINT("ERROR: SetupDiGetClassDevs failed, error: %lu", GetLastError());
        return false;
    }

    deviceInfoData.cbSize = sizeof(SP_DEVINFO_DATA);

    // 查找匹配的DeviceInfoData
    BOOL bFound = FALSE;
    for (DWORD i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, &deviceInfoData); i++) {
        if (deviceInfoData.DevInst == devInst) {
            bFound = TRUE;
            break;
        }
    }

    if (!bFound) {
        DBG_PRINT("ERROR: DeviceInfoData not found for DEVINST: 0x%08X", devInst);
        SetupDiDestroyDeviceInfoList(hDevInfo);
        return false;
    }

    // 设置启用参数
    propChangeParams.ClassInstallHeader.cbSize = sizeof(SP_CLASSINSTALL_HEADER);
    propChangeParams.ClassInstallHeader.InstallFunction = DIF_PROPERTYCHANGE;
    propChangeParams.Scope = DICS_FLAG_GLOBAL;
    propChangeParams.StateChange = DICS_ENABLE;
    propChangeParams.HwProfile = 0;

    // 安装参数
    if (!SetupDiSetClassInstallParams(hDevInfo, &deviceInfoData, 
                                      (SP_CLASSINSTALL_HEADER*)&propChangeParams, 
                                      sizeof(propChangeParams))) {
        DBG_PRINT("ERROR: SetupDiSetClassInstallParams failed, error: %lu", GetLastError());
        SetupDiDestroyDeviceInfoList(hDevInfo);
        return false;
    }

    // 执行启用操作
    if (!SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, hDevInfo, &deviceInfoData)) {
        DBG_PRINT("ERROR: SetupDiCallClassInstaller(DICS_ENABLE) failed, error: %lu", 
                  GetLastError());
        SetupDiDestroyDeviceInfoList(hDevInfo);
        return false;
    }

    DBG_PRINT("Device enabled successfully (DEVINST: 0x%08X)", devInst);

    SetupDiDestroyDeviceInfoList(hDevInfo);
    return true;
}

/**
 * 获取设备当前状态
 *
 * @param devInst 设备实例句柄
 * @param outState 输出状态
 * @return 是否成功
 */
static bool GetDeviceState(DEVINST devInst, DWORD& outState)
{
    CONFIGRET cr = CM_Get_DevNode_Status(&outState, NULL, devInst, 0);
    
    if (cr == CR_SUCCESS) {
        return true;
    }

    DBG_PRINT("ERROR: CM_Get_DevNode_Status failed, error: 0x%08X", cr);
    return false;
}

// ============================================================================
// 公开API实现
// ============================================================================

/**
 * 通过软件方式复位USB设备（模拟重新插拔）
 *
 * 流程：
 *   1. 解析设备符号链接
 *   2. 查找设备实例句柄
 *   3. 禁用设备
 *   4. 等待设备完全断开（2秒）
 *   5. 启用设备
 *   6. 等待设备重新枚举完成（3秒）
 *
 * @param deviceSymbolicLink 设备符号链接
 * @param waitDisconnectMs 断开等待时间（毫秒）
 * @param waitConnectMs 连接等待时间（毫秒）
 * @return 是否成功
 */
bool SoftwareResetUsbDeviceEx(const wchar_t* deviceSymbolicLink, 
                                     int waitDisconnectMs, 
                                     int waitConnectMs)
{
    if (!deviceSymbolicLink || wcslen(deviceSymbolicLink) == 0) {
        DBG_PRINT("ERROR: Invalid device link");
        return false;
    }

    DBG_PRINT("========================================");
    DBG_PRINT("Starting USB device software reset...");
    //DBG_PRINT("Device: %ls", CT2A(deviceSymbolicLink));
    DBG_PRINT("========================================");

    // 步骤1: 解析设备信息
    UsbDeviceInfo devInfo = { 0 };
    if (!ParseDeviceLink(deviceSymbolicLink, devInfo)) {
        DBG_PRINT("ERROR: Failed to parse device link");
        return false;
    }

    // 步骤2: 查找设备实例句柄
    DEVINST devInst = 0;
    if (!FindDeviceInstance(deviceSymbolicLink, devInst)) {
        DBG_PRINT("ERROR: Failed to find device instance");
        return false;
    }

    // 步骤3: 检查设备当前状态
    DWORD devState = 0;
    if (GetDeviceState(devInst, devState)) {
        DBG_PRINT("Current device state: 0x%08X", devState);
        
        // 检查设备是否已禁用
        if (devState & DN_HAS_PROBLEM) {
            DBG_PRINT("WARN: Device already has problems!");
        }
    }

    // 步骤4: 禁用设备
    DBG_PRINT("----------------------------------------");
    DBG_PRINT("Step 1: Disabling device...");
    if (!DisableDeviceInstance(devInst)) {
        DBG_PRINT("ERROR: Failed to disable device");
        return false;
    }

    // 步骤5: 等待设备完全断开
    DBG_PRINT("----------------------------------------");
    DBG_PRINT("Step 2: Waiting %d ms for device disconnect...", waitDisconnectMs);
    Sleep(waitDisconnectMs);

    // 验证设备已断开
    DWORD stateAfterDisable = 0;
    if (GetDeviceState(devInst, stateAfterDisable)) {
        DBG_PRINT("Device state after disable: 0x%08X", stateAfterDisable);
    }

    // 步骤6: 重新启用设备
    DBG_PRINT("----------------------------------------");
    DBG_PRINT("Step 3: Re-enabling device...");
    if (!EnableDeviceInstance(devInst)) {
        DBG_PRINT("ERROR: Failed to re-enable device");
        return false;
    }

    // 步骤7: 等待设备重新枚举完成
    DBG_PRINT("----------------------------------------");
    DBG_PRINT("Step 4: Waiting %d ms for device reconnect...", waitConnectMs);
    Sleep(waitConnectMs);

    // 验证设备已重新启用
    DWORD stateAfterEnable = 0;
    if (GetDeviceState(devInst, stateAfterEnable)) {
        DBG_PRINT("Device state after enable: 0x%08X", stateAfterEnable);
        
        // 检查设备是否正常
        if (stateAfterEnable & DN_HAS_PROBLEM) {
            DBG_PRINT("WARN: Device has problems after reset! State: 0x%08X", 
                      stateAfterEnable);
        } else {
            DBG_PRINT("Device reset successfully!");
        }
    }

    DBG_PRINT("========================================");
    DBG_PRINT("USB device software reset completed!");
    DBG_PRINT("========================================");

    return true;
}

/**
 * 简化版：使用默认等待时间的USB设备复位
 *
 * @param deviceSymbolicLink 设备符号链接
 * @return 是否成功
 */
bool SoftwareResetUsbDeviceSimple(const wchar_t* deviceSymbolicLink)
{
    // 默认等待时间：断开2秒，连接3秒
    return SoftwareResetUsbDeviceEx(deviceSymbolicLink, 2000, 3000);
}
