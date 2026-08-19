using System.Runtime.InteropServices;

namespace UpgradeTool.Core.Interop;

/// <summary>
/// Win32 原生互操作：IOCTL_STORAGE_QUERY_PROPERTY / STORAGE_DEVICE_DESCRIPTOR。
/// 用于读取磁盘设备描述符（BusType、厂商、产品），识别方式参考 TimeUpdate 的 OpenTheDrv。
/// </summary>
internal static class StoragePropertyNative
{
    internal const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

    internal const uint StorageDeviceProperty = 0;
    internal const uint PropertyStandardQuery = 0;

    /// <summary>STORAGE_BUS_TYPE（见 ntddstor.h）。</summary>
    internal enum StorageBusType : int
    {
        BusTypeUnknown = 0x00,
        BusTypeScsi = 0x01,
        BusTypeAtapi = 0x02,
        BusTypeAta = 0x03,
        BusType1394 = 0x04,
        BusTypeSsa = 0x05,
        BusTypeFibre = 0x06,
        BusTypeUsb = 0x07,
        BusTypeRAID = 0x08,
        BusTypeiScsi = 0x09,
        BusTypeSas = 0x0A,
        BusTypeSata = 0x0B,
        BusTypeSd = 0x0C,
        BusTypeMmc = 0x0D,
        BusTypeMax = 0x0E,
        BusTypeVirtual = 0x0F,
        BusTypeFileBackedVirtual = 0x10,
    }

    /// <summary>STORAGE_PROPERTY_QUERY（PropertyId / QueryType 双 DWORD，AdditionalParameters 至少 1 字节）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct StoragePropertyQuery
    {
        public uint PropertyId;
        public uint QueryType;
        public byte AdditionalParameters;
    }

    /// <summary>
    /// STORAGE_DEVICE_DESCRIPTOR（变长：头部后跟原始属性区，字符串以偏移量引用）。
    /// 字段布局与 C 头文件一致（BYTE/BOOLEAN 均为 1 字节，DWORD 4 字节对齐，无插入填充）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct StorageDeviceDescriptor
    {
        public uint Version;
        public uint Size;
        public byte DeviceType;
        public byte DeviceTypeModifier;
        [MarshalAs(UnmanagedType.U1)] public bool RemovableMedia;
        [MarshalAs(UnmanagedType.U1)] public bool CommandQueueing;
        public uint VendorIdOffset;
        public uint ProductIdOffset;
        public uint ProductRevisionOffset;
        public uint SerialNumberOffset;
        public StorageBusType BusType;
        public uint RawPropertiesLength;
        public byte RawDeviceProperties;
    }
}
