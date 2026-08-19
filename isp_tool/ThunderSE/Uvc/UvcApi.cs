using System;
using System.Runtime.InteropServices;

namespace ThunderSE.Uvc
{
    // C++��ʹ��__stdcall,ί�б���ƥ��
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int VideoDataCallbackFunc(IntPtr videoData, int size, int pixelFormat, IntPtr user_data);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PlayStateChangeCallbackFunc(bool isPlayable);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int YuvDataCallbackFunc(IntPtr yuvData);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int RawDataCallbackFunc(IntPtr rawData, int dataSize, int pixelFormat, int width, int height, IntPtr user_data);

    public class UvcApi
    {
        const string libraryName = "uvc.dll";

        // DllImport��CallingConvention���ڷǻص�����,����Cdecl
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetVideoDataCallback(VideoDataCallbackFunc cb, IntPtr user_data);

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetYuvDataCallback(YuvDataCallbackFunc cb);

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetRawDataCallback(RawDataCallbackFunc cb, IntPtr user_data);

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetPlayStateChangeCallback(PlayStateChangeCallbackFunc cb);

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int OpenInput(IntPtr filePath, ref int videoWidth, ref int videoHeight);

        /// <summary>
        /// 打开UVC输入流（安全版本，固定字符串防止GC移动）
        /// </summary>
        public static int OpenInputSafe(string filePath, ref int videoWidth, ref int videoHeight)
        {
            IntPtr hGlobal = IntPtr.Zero;
            try
            {
                hGlobal = Marshal.StringToHGlobalAnsi(filePath);
                return OpenInput(hGlobal, ref videoWidth, ref videoHeight);
            }
            finally
            {
                if (hGlobal != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(hGlobal);
                }
            }
        }

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ReconfigureResolution(ref int videoWidth, ref int videoHeight);

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CloseInput();

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int StartRecord(
            [MarshalAs(UnmanagedType.LPStr)] string filePath);

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int StopRecord();

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CaptureOneRawFrame(
            [MarshalAs(UnmanagedType.LPStr)] string raw_save_path);

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetRawFrameMode(int raw_mode);

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetRawScaleDown(int raw_scale);

        #region Proc Amp 视频属性控制 (IAMVideoProcAmp)

        /// <summary>
        /// Proc Amp 属性 ID（与 C++ ProcAmpPropertyId 枚举值一致）
        /// </summary>
        public enum ProcAmpProperty : int
        {
            Brightness = 0,
            Contrast = 1,
            Hue = 2,
            Saturation = 3,
            Sharpness = 4,
            Gamma = 5,
            ColorEnable = 6,
            WhiteBalance = 7,
            BacklightCompensation = 8,
            Gain = 9,
            DigitalMultiplier = 10,
            DigitalMultiplierLimit = 11,
            WhiteBalanceComponent = 12,
            PowerlineFrequency = 13
        }

        /// <summary>
        /// 单个 Proc Amp 属性的信息（结构体布局需与 uvc.h 的 ProcAmpParamInfo 完全一致）
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ProcAmpParamInfo
        {
            public int PropertyId;   // ProcAmpProperty
            public int MinVal;       // 最小值
            public int MaxVal;       // 最大值
            public int StepVal;      // 步进
            public int DefaultVal;   // 默认值
            public int CurrentVal;   // 当前值
            public int Flags;        // VideoProcAmp_Flags: 0x1=Auto, 0x2=Manual
            public int Supported;    // 1=设备支持, 0=不支持
        }

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int InitProcAmp(
            [MarshalAs(UnmanagedType.LPStr)] string deviceName);

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ReleaseProcAmp();

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetProcAmpCount();

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetProcAmpInfo(int index, ref ProcAmpParamInfo info);

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetProcAmpValue(int propertyId, int value, int autoMode);

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetProcAmpValue(int propertyId, ref int value, ref int flags);

        /// <summary>
        /// 获取最近一次 Proc Amp 调用的失败 HRESULT（0 表示成功/无错误）。
        /// 与 InitProcAmp/SetProcAmpValue/GetProcAmpValue 的 &lt;0 返回值配合使用，用于定位具体失败原因。
        /// </summary>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetLastProcAmpError();

        #endregion

        #region Camera Control 相机控制 (IAMCameraControl)

        /// <summary>
        /// Camera Control 属性 ID（与 C++ CameraControlPropertyId 枚举值一致）
        /// </summary>
        public enum CameraControlProperty : int
        {
            Pan = 0,
            Tilt = 1,
            Roll = 2,
            Zoom = 3,
            Exposure = 4,
            Iris = 5,
            Focus = 6,
            ScanMode = 7,
            Privacy = 8,
            PanTilt = 9,
            PanRelative = 10,
            TiltRelative = 11,
            RollRelative = 12,
            ZoomRelative = 13,
            ExposureRelative = 14,
            IrisRelative = 15,
            FocusRelative = 16,
            PanTiltRelative = 17,
            FocalLength = 18,
            AutoExposurePriority = 19
        }

        // 相机控制属性信息结构复用 ProcAmpParamInfo（布局完全一致）。
        // 原生层 InitProcAmp 初始化时会一并初始化相机控制，ReleaseProcAmp 一并释放，
        // 因此相机控制没有独立的 Init/Release 接口，直接读取/设置即可。

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetCameraControlCount();

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetCameraControlInfo(int index, ref ProcAmpParamInfo info);

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetCameraControlValue(int propertyId, int value, int autoMode);

        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetCameraControlValue(int propertyId, ref int value, ref int flags);

        /// <summary>
        /// 获取最近一次 Camera Control 调用的失败 HRESULT（0 表示成功/无错误）。
        /// </summary>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetLastCameraControlError();

        #endregion

        #region 分辨率枚举 (IAMStreamConfig)

        /// <summary>
        /// 单个支持的视频格式（与 uvc.h 的 VideoFormatInfo 布局一致）。
        /// PixelFormat 为像素格式 FOURCC 码（0=未压缩 RGB）。
        /// Fps 由 VIDEOINFOHEADER.AvgTimePerFrame 换算（0=未知）。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct VideoFormatInfo
        {
            public int Width;
            public int Height;
            public int PixelFormat;
            public double Fps;

            /// <summary>显示用名称，如 "1280x720 30fps (YUY2)"</summary>
            public override string ToString()
            {
                string fps = Fps > 0 ? $" {Fps:0.##}fps" : "";
                return $"{Width}x{Height}{fps} ({FourCcName(PixelFormat)})";
            }

            private static string FourCcName(int v)
            {
                if (v == 0) return "RGB";
                var bytes = BitConverter.GetBytes(v);
                var s = new string(new[]
                {
                    (char)bytes[0], (char)bytes[1], (char)bytes[2], (char)bytes[3]
                });
                return string.IsNullOrWhiteSpace(s) ? $"0x{v:X8}" : s;
            }
        }

        /// <summary>
        /// 枚举指定设备的视频格式（原生层经 DirectShow IAMStreamConfig 安全通道
        /// GetFormat/ConnectionMediaType 探测，工作线程执行）。当前实现返回设备
        /// 当前格式（通常 1 条，含分辨率/像素格式/帧率）；全量支持列表需设备在
        /// 建图连接状态下方可枚举（GetStreamCaps 对部分仿真层未连接时不可安全调用）。
        /// formats 传 64 项固定数组。
        /// 返回：>=0 实际格式数量；-1 设备/探测失败，-3 超时，-4 等待失败。
        /// 注意：deviceName 为 UTF-8 字节序列（MarshalAs(LPStr) 在系统 ANSI 代码页下
        /// 编组，非 ASCII 设备名（如中文）可能无法正确匹配，建议使用 ASCII 设备名）。
        /// </summary>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int EnumVideoFormats(
            [MarshalAs(UnmanagedType.LPStr)] string deviceName,
            [In, Out] VideoFormatInfo[] formats,
            int bufLen);

        #endregion
    }
}
