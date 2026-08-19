namespace UpgradeTool.Core.Protocol;

/// <summary>
/// 协议层固件下载选项（对齐 MPTool 刷写行为）。
/// </summary>
public sealed record FlashDownloadOptions(
    /// <summary>整片擦除：擦除区域从固件所需区域扩展到全片（对齐 MPTool mptool.ini ERASEALL=1）。</summary>
    bool EraseAll = false,

    /// <summary>
    /// 容量 pattern 测试：在地址 0 写 0x5a×512、容量中点写 0xa5×512 并回读比对（对齐 MPTool CheckCapacity，识别灰片）。
    /// 测试前先擦两端扇区，回读先地址 0 再容量中点（MPTool 顺序）；且守卫与 MPTool 一致：仅当容量中点 &gt; 固件长度
    /// 时才运行（否则中点落在固件区内，测试无意义）。
    /// 仅在 <see cref="EraseAll"/> 为 true 时生效——该测试会在容量中点擦写扇区，对局部重刷会破坏固件区外的
    /// 资源/参数分区，故默认关闭并限定到整片擦除场景。
    /// </summary>
    bool RunCapacityPatternTest = false);
