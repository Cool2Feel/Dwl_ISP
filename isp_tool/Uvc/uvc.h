#ifdef UVC_EXPORTS
#define UVC_API extern "C" __declspec(dllexport)
#else
#define UVC_API extern "C" __declspec(dllimport)
#endif

typedef void(__stdcall *YuvDataCallbackFunc)(void **data);

typedef void(__stdcall *VideoDataCallbackFunc)(void *data, int dataSize, int pixelFormat, const void *user_data);

typedef void(__stdcall *PlayStateChangeCallbackFunc)(bool isPlaying);

// 新增：RAW数据回调函数类型（用于直接传递未转码的原始数据）
typedef void(__stdcall *RawDataCallbackFunc)(void *data, int dataSize, int pixelFormat, int width, int height, const void *user_data);

UVC_API void SetPlayStateChangeCallback(PlayStateChangeCallbackFunc cb);

UVC_API void SetVideoDataCallback(VideoDataCallbackFunc cb, const void *user_data);

UVC_API void SetYuvDataCallback(YuvDataCallbackFunc cb, const void *user_data);

// 新增：设置RAW数据回调
UVC_API void SetRawDataCallback(RawDataCallbackFunc cb, const void *user_data);

UVC_API int OpenInput(const char* filepath, int& videoWidth, int& videoHeight);

UVC_API int ReconfigureResolution(int& videoWidth, int& videoHeight);

UVC_API int CloseInput();

UVC_API int StartRecord(const char* out_file);

UVC_API void StopRecord();

UVC_API void CaptureOneRawFrame(const char* raw_save_path);

UVC_API void SetRawFrameMode(const int mode);

UVC_API void SetRawScaleDown(const int scale);

UVC_API void SetColScaleDown(const int scale);

// ============================================================
// 视频属性控制面板 (Proc Amp / IAMVideoProcAmp)
// 用于获取并设置 USB 摄像头的亮度/对比度/饱和度/色调/增益等图像属性
// ============================================================

// Proc Amp 属性 ID（与 DirectShow VideoProcAmpProperty 枚举值一致）
enum ProcAmpPropertyId {
    PROCAMP_BRIGHTNESS = 0,
    PROCAMP_CONTRAST = 1,
    PROCAMP_HUE = 2,
    PROCAMP_SATURATION = 3,
    PROCAMP_SHARPNESS = 4,
    PROCAMP_GAMMA = 5,
    PROCAMP_COLOR_ENABLE = 6,
    PROCAMP_WHITE_BALANCE = 7,
    PROCAMP_BACKLIGHT_COMPENSATION = 8,
    PROCAMP_GAIN = 9,
    PROCAMP_DIGITAL_MULTIPLIER = 10,
    PROCAMP_DIGITAL_MULTIPLIER_LIMIT = 11,
    PROCAMP_WHITEBALANCE_COMPONENT = 12,
    PROCAMP_POWERLINE_FREQUENCY = 13
};

// 单个属性的信息结构（C# 端使用 LayoutKind.Sequential 对应）
// long 在 Windows 下为 32 位，对应 C# 的 int
struct ProcAmpParamInfo {
    int  propertyId;   // ProcAmpPropertyId
    long minVal;       // 最小值
    long maxVal;       // 最大值
    long stepVal;      // 步进
    long defaultVal;   // 默认值
    long currentVal;   // 当前值
    long flags;        // VideoProcAmp_Flags: 0x1=Auto, 0x2=Manual
    int  supported;    // 1=设备支持该属性, 0=不支持
};

// 初始化指定设备的 Proc Amp 控制。
// deviceName 为设备描述符，例如 "video=USB Camera" 或 "USB Camera" 均可，
// 内部会自动去掉 "video=" 前缀并做包含匹配（不区分大小写）。
// 返回：支持的可调属性数量(>=0)；<0 表示失败（未找到设备/不支持/接口错误）
UVC_API int InitProcAmp(const char* deviceName);

// 释放 Proc Amp 控制资源（设备断开时调用）
UVC_API void ReleaseProcAmp();

// 获取当前已初始化的「支持」属性数量
UVC_API int GetProcAmpCount();

// 获取第 index 个（仅统计 supported 的属性）的信息，index 范围 [0, GetProcAmpCount())
// 返回：0 成功，-1 索引越界或指针为空
UVC_API int GetProcAmpInfo(int index, ProcAmpParamInfo* info);

// 设置属性值。
// autoMode: 0=手动（使用 value），1=自动（忽略 value，由摄像头自行控制）
// 返回：0 成功，<0 失败（未初始化或设备拒绝）
UVC_API int SetProcAmpValue(int propertyId, long value, int autoMode);

// 读取属性当前值（value/flags 可为 NULL）
// 返回：0 成功，<0 失败
UVC_API int GetProcAmpValue(int propertyId, long* value, long* flags);

// 获取最近一次 Proc Amp 调用的失败 HRESULT（0 表示成功/无错误）。
// 与 InitProcAmp/SetProcAmpValue/GetProcAmpValue 的 <0 返回值配合使用，
// 可区分 0x80040154(类未注册)/0x80070005(访问被拒)/0x800401F0(COM 未初始化) 等具体原因。
UVC_API int GetLastProcAmpError();

// ============================================================
// 相机控制面板 (Camera Control / IAMCameraControl)
// 用于获取并设置 USB 摄像头的平移/俯仰/变焦/曝光/光圈/对焦等属性。
// 与图像属性(VideoProcAmp)共用同一设备句柄与工作线程：
// InitProcAmp 初始化时两套控制会一并初始化，ReleaseProcAmp 一并释放。
// ============================================================

// Camera Control 属性 ID（与 DirectShow CameraControlProperty 枚举值一致）
enum CameraControlPropertyId {
    CAMERA_PAN = 0,
    CAMERA_TILT = 1,
    CAMERA_ROLL = 2,
    CAMERA_ZOOM = 3,
    CAMERA_EXPOSURE = 4,
    CAMERA_IRIS = 5,
    CAMERA_FOCUS = 6,
    CAMERA_SCANMODE = 7,
    CAMERA_PRIVACY = 8,
    CAMERA_PANTILT = 9,
    CAMERA_PAN_RELATIVE = 10,
    CAMERA_TILT_RELATIVE = 11,
    CAMERA_ROLL_RELATIVE = 12,
    CAMERA_ZOOM_RELATIVE = 13,
    CAMERA_EXPOSURE_RELATIVE = 14,
    CAMERA_IRIS_RELATIVE = 15,
    CAMERA_FOCUS_RELATIVE = 16,
    CAMERA_PANTILT_RELATIVE = 17,
    CAMERA_FOCAL_LENGTH = 18,
    CAMERA_AUTO_EXPOSURE_PRIORITY = 19
};

// 相机控制属性信息结构与 ProcAmpParamInfo 布局完全一致，可复用该结构。
// 统计口径与 ProcAmp 相同（仅统计 supported 的属性）。

// 获取当前已初始化的「支持」相机控制属性数量
UVC_API int GetCameraControlCount();

// 获取第 index 个（仅统计 supported 的属性）相机控制属性信息
UVC_API int GetCameraControlInfo(int index, ProcAmpParamInfo* info);

// 设置相机控制属性值。
// autoMode: 0=手动（使用 value），1=自动（忽略 value，由摄像头自行控制）
// 返回：0 成功，<0 失败（未初始化或设备拒绝）
UVC_API int SetCameraControlValue(int propertyId, long value, int autoMode);

// 读取相机控制属性当前值（value/flags 可为 NULL）
// 返回：0 成功，<0 失败
UVC_API int GetCameraControlValue(int propertyId, long* value, long* flags);

// 获取最近一次 Camera Control 调用的失败 HRESULT（0 表示成功/无错误）
UVC_API int GetLastCameraControlError();

// ============================================================
// 分辨率枚举 (IAMStreamConfig / GetStreamCaps)
// 枚举 UVC 设备支持的所有视频格式（分辨率 + 像素格式 FOURCC）
// ============================================================

// 单个支持格式（C# 端使用 LayoutKind.Sequential 对应）
// pixelFormat：像素格式 FOURCC 码（YUY2=0x32595559 / MJPG=0x47504A4D / NV12=...），
// 0 表示未压缩 RGB（BI_RGB）；显示时可用 ASCII 还原为 4 字符码。
// fps：由 VIDEOINFOHEADER.AvgTimePerFrame 换算的帧率（fps），0 = 未知。
struct VideoFormatInfo {
    int width;        // 分辨率宽
    int height;       // 分辨率高
    int pixelFormat;  // 像素格式 FOURCC
    double fps;       // 帧率（fps），0 = 未知
};

// 枚举指定设备的视频格式（DirectShow IAMStreamConfig 探测，工作线程执行，内部 SEH）。
// 注意：部分 usbvideo 仿真层在"未连接"状态下 GetStreamCaps/GetNumberOfCapabilities
// 不可安全调用（实测会写坏调用者栈帧甚至返回地址），实现采用安全通道
// （GetFormat → ConnectionMediaType）返回设备当前格式（通常 1 条，含分辨率/像素
// 格式/帧率 fps）。需要全量支持列表的设备需在建图连接后再枚举。
// deviceName 为设备描述符（UTF-8 编码，同 InitProcAmp，支持 "video=" 前缀与包含匹配）。
// outBuf 接收格式列表；bufLen 为 outBuf 容量（格式个数，上限 64）。
// 返回：>=0 实际格式数量；-1 参数非法/设备未找到/探测失败；-3 等待超时；-4 等待失败
UVC_API int EnumVideoFormats(const char* deviceName, VideoFormatInfo* outBuf, int bufLen);

// 关闭工作线程并释放 COM 环境（进程退出前调用，DllDetach 已自动调用，幂等）
UVC_API void ShutdownProcAmpCom();

//ʹ�÷���:
//1.����:
//int StartCamera()
//{
//    SetPlayStateChangeCallback(...);
//    SetVideoDataCallback(OnVideoData, ...);
//
//    int width, height;
//
//    //filepathΪ�豸rtsp·����Ĭ����rtsp://192.168.1.1:7070/webcam    
//    OpenInput(filepath, width, height);
//
//    //OpenInput����֮��Ӧ����������Ƶչʾ���ڵĴ�С
//}
//
//void OnVideoData(void *videoData, int dataSize, const void *user_data)
//{
//    // �˺����ᱻ�����̵߳��ã����videoData��������ݷ�����UI�߳̽�һ��������
//    // videoData�����֡��ʽΪrgb24������д��bitmap����ʾ
//    // user_dataΪSetVideoDataCallback�����������ָ��
//}
//
//2.¼��:
//�ڻص�PlayStateChangeCallbackFunc������֮����isPlayingΪtrueʱ��
//����StartRecord����(StartRecord�Ĳ���out_fileΪ��Ƶ�����·������d:\capture.mp4)��
//
//3.�ط�:
//�벥�Ų���һ�£�����OpenInput����filepath��¼�ƺõ���Ƶ·��һ�¼���(��d:\capture.mp4)��