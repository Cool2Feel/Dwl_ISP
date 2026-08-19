namespace UpgradeTool.Core.Devices
{
    /// <summary>
    /// UVC 设备升级命令下发接口。
    /// 对齐 MPTool 的 Cuvc_dev_if：在 WM_TIMER 中周期性查找 UVC 扩展单元节点；
    /// 找到后通过扩展单元（XU）下发升级触发命令，使相机进入 Loader/升级模式。
    /// </summary>
    public interface IUvcUpdater
    {
        /// <summary>
        /// 枚举视频输入设备，查找 UVC 扩展单元（KSNODETYPE_DEV_SPECIFIC）节点。
        /// 返回节点索引；未找到任何 UVC 扩展节点时返回 -1。
        /// </summary>
        int FindExtensionNode();

        /// <summary>
        /// 通过 UVC 扩展单元下发升级命令（XU SET：BD_Guid / 属性 0x4 / 2 字节全 0）。
        /// 成功返回 true。
        /// </summary>
        bool SendUpdateCommand(int nodeIndex);
    }
}
