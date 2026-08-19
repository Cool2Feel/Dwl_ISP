using System;
using System.Runtime.InteropServices;

namespace UpgradeTool.Core.Interop
{
    /// <summary>
    /// UVC 扩展单元命令所需的 COM 互操作声明。
    /// 设计对齐 MPTool 的 <c>Cuvc_dev_if</c>：通过 KS 拓扑信息（DirectShow 视频输入设备分类下的 IKsTopologyInfo）
    /// 找到 KSNODETYPE_DEV_SPECIFIC 扩展节点，再通过 IKsControl.KsProperty 下发 XU SET 命令，
    /// 触发相机进入升级（Loader）模式。这与 MPTool 的 uvc_dev.SearchNode() + uvc_send_updata_cmd() 语义完全一致。
    /// </summary>
    internal static class UvcInterop
    {
        // ---- 关键 GUID（与 MPTool uvc_dev_if.cpp 严格一致）----

        /// <summary>UVC 扩展单元 GUID，MPTool 中定义为 BD_Guid。</summary>
        public static readonly Guid BdGuid =
            new Guid(0x9e9590a3, 0xfe3f, 0x4a82, 0x8c, 0xe8, 0xf7, 0xb0, 0x43, 0xf6, 0x43, 0x67);

        /// <summary>KSNODETYPE_DEV_SPECIFIC —— 扩展单元（XU）节点类型。</summary>
        public static readonly Guid KsNodeTypeDevSpecific =
            new Guid(0xa19df336, 0xb3a4, 0x4cf7, 0xa7, 0x70, 0x33, 0xb5, 0x88, 0x66, 0x77, 0x9a);

        public static readonly Guid ClsIdSystemDeviceEnum =
            new Guid(0x62be5d10, 0x60eb, 0x11d0, 0xbd, 0x3b, 0x00, 0xa0, 0xc9, 0x11, 0xce, 0x86);

        public static readonly Guid ClsIdVideoInputDeviceCategory =
            new Guid(0x860bb310, 0x5d01, 0x11d0, 0xbd, 0x3b, 0x00, 0xa0, 0xc9, 0x11, 0xce, 0x86);

        public static readonly Guid IIdIBaseFilter =
            new Guid(0x796951dc, 0x5aee, 0x11ce, 0xbd, 0x0e, 0x00, 0xaa, 0x00, 0x68, 0x6f, 0x13);

        public static readonly Guid IIdIPropertyBag =
            new Guid(0x55272a00, 0x42cb, 0x11ce, 0x81, 0x35, 0x00, 0xaa, 0x00, 0x4b, 0xb8, 0x51);

        public static readonly Guid IIdIKsTopologyInfo =
            new Guid(0xa2e30750, 0x6c3d, 0x11d0, 0xbd, 0x4e, 0x00, 0xa0, 0xc9, 0x11, 0xce, 0x86);

        public static readonly Guid IIdIKsControl =
            new Guid(0x28f54685, 0x06fd, 0x11d2, 0xb2, 0x7a, 0x00, 0xa0, 0xc9, 0x22, 0x31, 0x96);

        public static readonly Guid IIdIUnknown =
            new Guid(0x00000000, 0x0000, 0x0000, 0xc0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);

        // ---- KSPROPERTY / KSP_NODE ----

        /// <summary>KSP_NODE：KSPROPERTY(Set/GUID + Id + Flags) + NodeId + Reserved，共 32 字节。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct KspNode
        {
            public Guid Set;
            public uint Id;
            public uint Flags;
            public uint NodeId;
            public uint Reserved;
        }

        // ---- P/Invoke ----

        [DllImport("ole32.dll", PreserveSig = true)]
        public static extern int CoCreateInstance(
            ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out ICreateDevEnum ppv);

        [DllImport("ole32.dll", PreserveSig = true)]
        public static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll", PreserveSig = true)]
        public static extern void CoUninitialize();

        [DllImport("ole32.dll", PreserveSig = true)]
        public static extern int QueryInterface(IntPtr pUnk, ref Guid iid, out IntPtr ppv);

        public const uint ClsctxAll = 0x17; // CLSCTX_INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER
        public const uint CoinitApartmentthreaded = 0x2; // COINIT_APARTMENTTHREADED
    }

    // ---- COM 接口（仅声明实际调用到的 vtable 槽位，保持 ABI 对齐）----

    [ComImport, Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid pType, out IEnumMoniker ppEnumMoniker, uint dwFlags);
    }

    [ComImport, Guid("00000102-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumMoniker
    {
        [PreserveSig]
        int Next(uint celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IMoniker[] rgelt, out uint pceltFetched);

        [PreserveSig]
        int Skip(uint celt);

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int Clone(out IEnumMoniker ppenum);
    }

    [ComImport, Guid("0000000F-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMoniker
    {
        // vtable 槽位 3 / 4（IUnknown 之后）
        [PreserveSig]
        int BindToObject(IntPtr pbc, IntPtr pmkToLeft, ref Guid riidResult, out IBaseFilter ppvResult);

        [PreserveSig]
        int BindToStorage(IntPtr pbc, IntPtr pmkToLeft, ref Guid riid, out IPropertyBag ppv);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyBag
    {
        [PreserveSig]
        int Read([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [In, Out] ref object pVar, IntPtr pErrorLog);

        [PreserveSig]
        int Write([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [In] ref object pVar);
    }

    /// <summary>仅作为 BindToObject 的输出类型占位，不声明方法。</summary>
    [ComImport, Guid("796951DC-5AEE-11CE-BD0E-00AA00686F13"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IBaseFilter
    {
    }

    [ComImport, Guid("A2E30750-6C3D-11D0-BD4E-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IKsTopologyInfo
    {
        [PreserveSig]
        int GetNumNodes(out uint pdwNumNodes);

        [PreserveSig]
        int GetNodeType(uint dwNode, out Guid pNodeType);

        [PreserveSig]
        int GetNumConnections(out uint pdwNumConnections);

        [PreserveSig]
        int GetConnectionInfo(uint dwConnectionIndex, IntPtr pConnection);

        [PreserveSig]
        int GetNodeName(uint dwNode, IntPtr pwchNodeName, ref uint pdwLength);

        [PreserveSig]
        int GetNodeTypeByName(IntPtr pwchNodeName, uint dwLength, out Guid pNodeType);

        [PreserveSig]
        int CreateNodeInstance(uint dwNode, ref Guid riid, out IntPtr ppInterface);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IKsControl
    {
        [PreserveSig]
        int KsProperty(IntPtr property, uint propertyLength, IntPtr propertyData, uint dataLength, out uint bytesReturned);

        [PreserveSig]
        int KsMethod(IntPtr method, uint methodLength, IntPtr methodData, uint dataLength, out uint bytesReturned);

        [PreserveSig]
        int KsEvent(IntPtr eventObj, uint eventLength, IntPtr eventData, uint dataLength, out uint bytesReturned);
    }
}
