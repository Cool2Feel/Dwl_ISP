// Device.cpp : 定义 DLL 应用程序的导出函数。
//

#include "Export.h"


// 这是导出变量的一个示例
ISP_API int nDevice = 0;

// 这是导出函数的一个示例。
ISP_API int fnDevice(void)
{
	return 42;
}

//// 这是已导出类的构造函数。
//// 有关类定义的信息，请参阅 Device.h
//CDevice::CDevice()
//{
//	return;
//}
