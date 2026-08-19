using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using UpgradeTool.Core.Interop;

namespace UpgradeTool.Core.Devices
{
    /// <summary>
    /// UVC 设备升级命令下发实现，对齐 MPTool 的 Cuvc_dev_if。
    /// <para>
    /// MPTool 在 WM_TIMER（每 300ms 一次）中以 wait_cnt 节流轮询：先 <c>SearchNode()</c> 枚举视频输入设备、
    /// 查找 KSNODETYPE_DEV_SPECIFIC 扩展节点；找到后 <c>uvc_send_updata_cmd()</c> 通过扩展单元下发 XU SET 命令
    /// （BD_Guid / 属性 0x4 / 2 字节全 0），使相机进入升级（Loader）模式。
    /// </para>
    /// <para>本实现以 DirectShow 视频输入设备分类下的 IKsTopologyInfo 完成节点发现与命令下发（与 MPTool SearchNode 同一条 KS 路径），
    /// 在独立 STA 线程上执行 COM，避免污染 DeviceWatcher 的 MTA 轮询线程；任何异常均被吞掉并记录，绝不中断热插拔轮询。</para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class UvcDeviceUpdater : IUvcUpdater, IDisposable
    {
        /// <summary>XU 属性 ID（MPTool 中 0x4）。</summary>
        public const uint UpdatePropertyId = 0x4;

        /// <summary>KsProperty Flags：KSPROPERTY_TYPE_SET | KSPROPERTY_TYPE_TOPOLOGY。</summary>
        private const uint KsPropertyTypeSetTopology = 0x10000002;

        /// <summary>下发数据长度（MPTool 中为 2 字节全 0）。</summary>
        public const int UpdateCommandLength = 2;

        private readonly Action<string>? _log;
        private bool _disposed;

        public UvcDeviceUpdater(Action<string>? log = null)
        {
            _log = log;
        }

        public int FindExtensionNode()
        {
            return RunSta(FindExtensionNodeCore);
        }

        public bool SendUpdateCommand(int nodeIndex)
        {
            if (nodeIndex < 0)
                return false;
            return RunSta(() => SendUpdateCommandCore(nodeIndex));
        }

        // ----------------------------------------------------------------

        private T RunSta<T>(Func<T> work)
        {
            T result = default!;
            var thread = new Thread(() =>
            {
                int hr = UvcInterop.CoInitializeEx(IntPtr.Zero, UvcInterop.CoinitApartmentthreaded);
                try
                {
                    result = work();
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[UVC] 操作时发生异常：{ex.Message}");
                    result = default!;
                }
                finally
                {
                    if (hr >= 0)
                        UvcInterop.CoUninitialize();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }

        private int FindExtensionNodeCore()
        {
            if (!TryCreateVideoEnum(out _, out var enumMon) || enumMon == null)
                return -1;

            var monikers = new IMoniker[1];
            try
            {
                while (enumMon.Next(1, monikers, out uint fetched) == 0 && fetched == 1)
                {
                    var mon = monikers[0];
                    if (mon == null)
                        continue;
                    try
                    {
                        int node = FindNodeOnDevice(mon);
                        if (node >= 0)
                            return node;
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"[UVC] 枚举设备查找节点失败：{ex.Message}");
                    }
                    finally
                    {
                        if (mon != null)
                            Marshal.ReleaseComObject(mon);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumMon);
            }

            return -1;
        }

        private int FindNodeOnDevice(IMoniker mon)
        {
            var iidBaseFilter = UvcInterop.IIdIBaseFilter;
            var iidKsTopo = UvcInterop.IIdIKsTopologyInfo;
            mon.BindToObject(IntPtr.Zero, IntPtr.Zero, ref iidBaseFilter, out IBaseFilter pcap);
            if (pcap == null)
                return -1;

            IntPtr unk = IntPtr.Zero;
            IKsTopologyInfo? ksTopo = null;
            IntPtr ksTopoPtr = IntPtr.Zero;
            try
            {
                unk = Marshal.GetIUnknownForObject(pcap);
                if (UvcInterop.QueryInterface(unk, ref iidKsTopo, out ksTopoPtr) != 0)
                    return -1;

                ksTopo = (IKsTopologyInfo)Marshal.GetObjectForIUnknown(ksTopoPtr);
                if (ksTopo.GetNumNodes(out uint numNodes) != 0)
                    return -1;

                for (uint n = 0; n < numNodes; n++)
                {
                    if (ksTopo.GetNodeType(n, out Guid nodeType) == 0 &&
                        nodeType == UvcInterop.KsNodeTypeDevSpecific)
                    {
                        return (int)n;
                    }
                }

                return -1;
            }
            finally
            {
                if (ksTopo != null)
                    Marshal.ReleaseComObject(ksTopo);
                if (ksTopoPtr != IntPtr.Zero)
                    Marshal.Release(ksTopoPtr);
                if (unk != IntPtr.Zero)
                    Marshal.Release(unk);
                Marshal.ReleaseComObject(pcap);
            }
        }

        private bool SendUpdateCommandCore(int nodeIndex)
        {
            if (!TryCreateVideoEnum(out _, out var enumMon) || enumMon == null)
                return false;

            IMoniker? mon = null;
            IBaseFilter? pcap = null;
            IKsTopologyInfo? ksTopo = null;
            IntPtr ksTopoPtr = IntPtr.Zero;
            IntPtr nodeInstPtr = IntPtr.Zero;
            IKsControl? ksCtrl = null;
            IntPtr ksCtrlPtr = IntPtr.Zero;
            GCHandle kspHandle = default;
            GCHandle dataHandle = default;
            var iidBaseFilter = UvcInterop.IIdIBaseFilter;
            var iidKsTopo = UvcInterop.IIdIKsTopologyInfo;
            var iidIUnknown = UvcInterop.IIdIUnknown;
            var iidKsCtrl = UvcInterop.IIdIKsControl;
            try
            {
                var monikers = new IMoniker[1];
                if (enumMon.Next(1, monikers, out uint fetched) != 0 || fetched != 1 || monikers[0] == null)
                    return false;
                mon = monikers[0];

                mon.BindToObject(IntPtr.Zero, IntPtr.Zero, ref iidBaseFilter, out pcap);
                if (pcap == null)
                    return false;

                IntPtr unk = Marshal.GetIUnknownForObject(pcap);
                if (UvcInterop.QueryInterface(unk, ref iidKsTopo, out ksTopoPtr) != 0)
                    return false;
                ksTopo = (IKsTopologyInfo)Marshal.GetObjectForIUnknown(ksTopoPtr);

                if (ksTopo.CreateNodeInstance((uint)nodeIndex, ref iidIUnknown, out nodeInstPtr) != 0)
                    return false;
                if (UvcInterop.QueryInterface(nodeInstPtr, ref iidKsCtrl, out ksCtrlPtr) != 0)
                    return false;
                ksCtrl = (IKsControl)Marshal.GetObjectForIUnknown(ksCtrlPtr);

                var ksp = new UvcInterop.KspNode
                {
                    Set = UvcInterop.BdGuid,
                    Id = UpdatePropertyId,
                    Flags = KsPropertyTypeSetTopology,
                    NodeId = (uint)nodeIndex,
                    Reserved = 0,
                };
                kspHandle = GCHandle.Alloc(ksp, GCHandleType.Pinned);

                var data = new byte[UpdateCommandLength];
                dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);

                int hr = ksCtrl.KsProperty(
                    kspHandle.AddrOfPinnedObject(),
                    (uint)Marshal.SizeOf<UvcInterop.KspNode>(),
                    dataHandle.AddrOfPinnedObject(),
                    (uint)data.Length,
                    out uint bytesReturned);
                return hr == 0;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[UVC] 下发升级命令失败：{ex.Message}");
                return false;
            }
            finally
            {
                if (kspHandle.IsAllocated)
                    kspHandle.Free();
                if (dataHandle.IsAllocated)
                    dataHandle.Free();
                if (ksCtrl != null)
                    Marshal.ReleaseComObject(ksCtrl);
                if (ksCtrlPtr != IntPtr.Zero)
                    Marshal.Release(ksCtrlPtr);
                if (nodeInstPtr != IntPtr.Zero)
                    Marshal.Release(nodeInstPtr);
                if (ksTopo != null)
                    Marshal.ReleaseComObject(ksTopo);
                if (ksTopoPtr != IntPtr.Zero)
                    Marshal.Release(ksTopoPtr);
                if (pcap != null)
                    Marshal.ReleaseComObject(pcap);
                if (mon != null)
                    Marshal.ReleaseComObject(mon);
                if (enumMon != null)
                    Marshal.ReleaseComObject(enumMon);
            }
        }

        private static bool TryCreateVideoEnum(out ICreateDevEnum? devEnum, out IEnumMoniker? enumMon)
        {
            devEnum = null;
            enumMon = null;
            try
            {
                var iidCreateDevEnum = typeof(ICreateDevEnum).GUID;
                var clsIdSystemDeviceEnum = UvcInterop.ClsIdSystemDeviceEnum;
                var clsIdVideoInput = UvcInterop.ClsIdVideoInputDeviceCategory;
                if (UvcInterop.CoCreateInstance(
                        ref clsIdSystemDeviceEnum, IntPtr.Zero,
                        UvcInterop.ClsctxAll, ref iidCreateDevEnum, out devEnum) != 0)
                {
                    return false;
                }

                if (devEnum == null)
                    return false;

                if (devEnum.CreateClassEnumerator(ref clsIdVideoInput, out enumMon, 0) != 0)
                    return false;

                return enumMon != null;
            }
            catch
            {
                if (enumMon != null)
                    Marshal.ReleaseComObject(enumMon);
                if (devEnum != null)
                    Marshal.ReleaseComObject(devEnum);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
        }
    }
}
