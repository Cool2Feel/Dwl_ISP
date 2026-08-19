using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ThunderSE.Device
{
    public enum DeviceEvent
    {
        Arrival,
        RemoveComplete
    };

    // C++端使用__stdcall,委托必须匹配
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void DeviceChangeHandler(
        DeviceEvent eventType,
        [MarshalAs(UnmanagedType.LPWStr)] string location,
        [MarshalAs(UnmanagedType.LPWStr)] string model,
        [MarshalAs(UnmanagedType.LPWStr)] string uvcInterface);

    class DeviceApi
    {
        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Initialize();

        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void UnInitialize();

        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void RegDeviceChangeCallback(DeviceChangeHandler handler);

        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void UnRegDeviceChangeCallback();

        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool WriteAx327XIspProperty([MarshalAs(UnmanagedType.LPWStr)] string location,
            int parameter,
            byte[] dataBuffer,
            int dataSize);

        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ReadAx327XIspProperty([MarshalAs(UnmanagedType.LPWStr)] string location,
            int parameter,
            byte[] dataBuffer,
            int dataSize);

        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool WriteAx327XSensorProperty([MarshalAs(UnmanagedType.LPWStr)] string location,
            int parameter,
            byte[] dataBuffer,
            int dataSize);

        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ReadAx327XSensorProperty([MarshalAs(UnmanagedType.LPWStr)] string location,
            int parameter,
            byte[] dataBuffer,
            int dataSize);

        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ReadAx327XLcdProperty([MarshalAs(UnmanagedType.LPWStr)] string location,
            byte[] dataBuffer);

        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool WriteAx327XLcdProperty([MarshalAs(UnmanagedType.LPWStr)] string location,
            int parameter,
            byte[] dataBuffer,
            int dataSize);

        // rawFilePath是输出参数(C++端会写入文件路径)
        // C++端是char*(ANSI),所以CharSet必须用Ansi
        [DllImport("Device.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool Ax327XCutRaw(
            [MarshalAs(UnmanagedType.LPWStr)] string location,  // location是宽字符
            StringBuilder rawFilePath);  // 输出缓冲区,接收ANSI字符串

        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ScanDevice();


        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool WriteAx327XSensorMode([MarshalAs(UnmanagedType.LPWStr)] string location,
            int parameter,
            byte[] dataBuffer,
            int dataSize);

        // ============================================================
        // USB设备软件复位（模拟重新插拔）
        // ============================================================

        /// <summary>
        /// 通过软件方式复位USB设备（模拟重新插拔）
        /// 带自定义等待时间
        /// </summary>
        /// <param name="deviceSymbolicLink">设备符号链接</param>
        /// <param name="waitDisconnectMs">断开等待时间（毫秒），默认2000ms</param>
        /// <param name="waitConnectMs">连接等待时间（毫秒），默认3000ms</param>
        /// <returns>是否成功</returns>
        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool SoftwareResetUsbDeviceEx(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceSymbolicLink,
            int waitDisconnectMs = 2000,
            int waitConnectMs = 3000);

        /// <summary>
        /// 通过软件方式复位USB设备（模拟重新插拔）
        /// 使用默认等待时间
        /// </summary>
        /// <param name="deviceSymbolicLink">设备符号链接</param>
        /// <returns>是否成功</returns>
        [DllImport("Device.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool SoftwareResetUsbDeviceSimple(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceSymbolicLink);
    }
}
