/**
 * UsbDeviceReset.h - USB设备软件复位接口
 *
 * 功能：通过Windows SetupAPI禁用并重新启用USB设备，
 *       实现软件方式的设备"重新插拔"效果
 */

#pragma once

#include <windows.h>
#include <string>

// 设备信息结构
struct UsbDeviceInfo {
    wchar_t SymbolicLink[512];  // 设备符号链接
    wchar_t VendorId[8];        // 供应商ID（例如：046D）
    wchar_t ProductId[8];       // 产品ID（例如：082D）
    wchar_t SerialNumber[64];   // 序列号（可选）
};

/**
 * 通过软件方式复位USB设备（模拟重新插拔）
 *
 * 流程：
 *   1. 解析设备符号链接
 *   2. 查找设备实例句柄
 *   3. 禁用设备
 *   4. 等待设备完全断开（默认2秒）
 *   5. 启用设备
 *   6. 等待设备重新枚举完成（默认3秒）
 *
 * 注意：
 *   - 需要管理员权限
 *   - 设备会短暂不可用（约5秒）
 *   - 会触发系统设备变更通知（Device Arrival/Remove）
 *
 * @param deviceSymbolicLink 设备符号链接
 *                           例如：L"\\\\?\\USB#VID_1234&PID_5678#ABC123#{GUID}"
 * @param waitDisconnectMs 断开等待时间（毫秒），默认2000ms
 * @param waitConnectMs 连接等待时间（毫秒），默认3000ms
 * @return 是否成功
 *
 * 使用示例：
 *   SoftwareResetUsbDeviceEx(L"\\\\?\\USB#VID_1234&PID_5678#ABC123#{...}");
 */
extern "C" __declspec(dllexport) bool SoftwareResetUsbDeviceEx(
    const wchar_t* deviceSymbolicLink,
    int waitDisconnectMs,
    int waitConnectMs
);

/**
 * 简化版：使用默认等待时间的USB设备复位
 *
 * @param deviceSymbolicLink 设备符号链接
 * @return 是否成功
 */
extern "C" __declspec(dllexport) bool SoftwareResetUsbDeviceSimple(
    const wchar_t* deviceSymbolicLink
);
