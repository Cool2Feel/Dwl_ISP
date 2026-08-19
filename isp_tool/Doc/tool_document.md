此文档用于答题介绍使用ISP工具过物理的步骤：

注意事项：

1. 使用ISP工具前，SDK打开USB调试：

#define CMOS\_USB\_ONLINE\_DBG 1

最后产品的软件，这个宏必须定义为0.

1. USB设备定义为UVC和MSC复合设备：

#define USB\_DEVICE\_TYPE (USB\_DEVICE\_UVC|USB\_DEVICE\_MSC)

3、截RAW数据时，是通过USB传到PC保存的，USB带宽限制，在ISP调试时，720P的sensor帧率配置10帧，1080P的sensor帧率配置4帧。

运行ThunderSE.exe

可以看到以下界面：

![](data:image/png;base64...)

1、设备开机默认进入MJPG模式，在这个模式下配置Exp Gain值一般102400，sensor帧率低时可以小一些。（RAW模式下，ISP不工作，任何ISP配置不起作用）

![](data:image/png;base64...)![](data:image/png;base64...)

2、要截RAW图时，选择RAW模式，点击截RAW，RAW数据保存在工具TestRaw文件夹下

BLC过物理：

* 用盖子将摄像头完全盖住，必须保证没有光进入到sensor；
* 点击UI右上角的“展开UVCView”或“弹出UVCView窗口”，再点击得到窗口的右下角的“截RAW”。截raw成功的话会弹出成功及raw的位置；

![](data:image/png;base64...)

* 点击并选择刚刚所截的raw图，工具会计算出两种blc值，选一种将其填写到sdk中，或者直接点击应用ISP工具会将对应的参数写入到板子上，并立即生效。

![](data:image/png;base64...)

![](data:image/png;base64...)

LSC过物理：（辉度机）

* 选择辉度机的A光源，将辉度机调节至level8.0—level11.0之间，保证图像中心位置的的亮度值在230左右（如果图像偏暗优先增大Exp\_gain，偏亮则优先减小辉度机的level级别），截raw；
* 点击lsc中的“使用图示进行设置”，加载刚刚截取的raw图，选择lsc模式为Y时，矫正亮度不均匀，选择RGB时，可以矫正亮度不均匀和颜色问题，如果画面颜色有问题，选择RGB，选择图像的最亮区域，计算lsc。

![](data:image/png;base64...)

![](data:image/png;base64...)

* 将其填写到sdk中，或者直接点击应用ISP工具会将对应的参数写入到板子上，并立即生效。

notice :

1. you must make sure the lens fits the DNP light box perfectlt .

notice：

1. the 24 color card must occupy 80% in the picture .
2. the 20 ,21 ,22 shuold not over exposure .
3. if you are in the darkroom , you must make sure the color temperature purity & clean .

AWB过物理：（24色卡、色温箱）

* 24色卡的图像居于屏幕正中并尽可能沾满屏幕，选择色温箱的D65光源，将色温箱档位调节至最大档位的1/3-1之间，保证24色卡的图像右下角最白的那块区域的Y值不超过220（过暗优先调节调大色温箱档位或exp\_gain，最后在调节GainMax），截2张该色温下的raw图；
* TL84,CWF,A光源同上；
* 框选白平衡参数计算区域（选择信噪比大的区域，所以最暗的那一块不选）；

![](data:image/png;base64...)

* 框选完所有的raw之后点击确定，并依次得到如下图

![](data:image/png;base64...)![](data:image/png;base64...)

* 输出参数将其填写到sdk中，或者直接点击应用ISP工具会将对应的参数写入到板子上，并立即生效。
