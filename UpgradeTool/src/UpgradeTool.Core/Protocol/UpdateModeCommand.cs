using UpgradeTool.Core.Abstractions;

namespace UpgradeTool.Core.Protocol;

/// <summary>
/// 进入固件升级模式命令（0xDA）。
/// 固件端 hal_usb_msc.c scsi_cmd_analysis：MscCmd.OpCode == 0xDA 时调用 cbw_update()，
/// 立即回 CSW（无数据阶段），随后关中断、复位 USB、跳转 bootloader。
/// CBW 字节 15 = OpCode 对应 CDB[0]，因此一条 CDB[0]=0xDA 即可触发，无需 SubOpCode。
/// </summary>
public static class UpdateModeCommand
{
    public const byte OpCode = 0xDA;

    /// <summary>
    /// 构建 16 字节 CDB（对齐 MPTool UFIsp 的 sizeof(ScsiCBW_Buf)=16）。
    /// 参考项目 MPTool UsbFunction.cpp: memset(ScsiCBW_Buf, 0, 16); ScsiCBW_Buf[0] = 0xDA;
    /// ReadFromScsi(fileHandle, 16, ScsiCBW_Buf, 0, NULL)。
    /// 固件通过 get_cbw 解析 CDB，CBW 中 CDB 不足部分自动补零，此处 16 字节与 MPTool 完全一致。
    /// </summary>
    public static byte[] BuildCdb()
    {
        byte[] cdb = new byte[16];
        cdb[0] = OpCode;
        return cdb;
    }

    /// <summary>
    /// 下发 0xDA 收尾复位命令并判定结果（Loader 0xCB / 应用态 0xCD 两协议共用）。
    /// 0xDA 后固件立即复位 USB，DeviceIoControl 可能返回错误 55（设备未连接）
    /// 或 31（设备未就绪），都视为命令已送达（设备正在重启并重新枚举）。
    ///
    /// 设备侧 SCSI 处理：固件 cbw_update 收到 0xDA 后回 CSW（无数据阶段），
    /// 然后关中断、复位 USB。实际固件在回 CSW 前可能已开始复位流程，
    /// 导致 SCSI 状态返回 CHECK CONDITION（0x02），Sense=NOT READY/MEDIUM NOT PRESENT。
    /// 此状态同样视为命令已送达，因为设备确实进入了复位流程。
    /// </summary>
    public static Task<ProtocolResult> SendAsync(IFlashTransport transport, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 对齐 MPTool ReadFromScsi：使用 SCSI_IOCTL_DATA_IN 方向（bmCBWFlags=0x80），
        // 而非 SCSI_IOCTL_DATA_UNSPECIFIED（bmCBWFlags=0x00）。设备固件检查
        // bmCBWFlags 方向位，方向错误时返回 CHECK CONDITION / NOT READY 拒绝命令。
        // 此处 dataLength=0 无实际数据传输，仅方向位影响固件判定。
        ScsiCommandResult result = transport.SendDataIn(BuildCdb(), 0);

        if (result.Success)
            return Task.FromResult(ProtocolResult.Ok("0xDA 已下发，设备正在复位并重新枚举。"));
        if (result.ErrorCode is 55 or 31)
            return Task.FromResult(ProtocolResult.Ok("0xDA 已下发，设备已断开（正在重启）。"));

        // 0xDA 使设备复位 USB，设备可能在回 CSW 前已开始复位，
        // 返回 SCSI CHECK CONDITION（状态 0x02）是预期行为，仍视为成功。
        // 附 Sense 数据便于诊断：如 firmware 因方向位/CBWLUN 等拒绝命令时，
        // Sense 会携带 ASC/ASCQ 指明具体原因。
        if (result.ScsiStatus == 0x02)
        {
            string senseInfo = result.Sense is { Length: > 0 }
                ? $"，Sense: {Convert.ToHexString(result.Sense)}"
                : "，无 Sense 数据";
            return Task.FromResult(ProtocolResult.Ok($"0xDA 已下发（SCSI CHECK CONDITION{senseInfo}），设备正在复位。"));
        }

        return Task.FromResult(ProtocolResult.Fail($"0xDA 下发失败: {result.DescribeError()}。请检查设备连接。"));
    }
}
