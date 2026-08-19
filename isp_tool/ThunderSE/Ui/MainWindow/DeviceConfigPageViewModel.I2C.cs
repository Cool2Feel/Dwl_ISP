using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ThunderSE.Common;
using ThunderSE.Device;
using ThunderSE.DeviceConfig;

namespace ThunderSE.Ui.MainWindow
{
    public partial class DeviceConfigPageViewModel
    {
        private int _iicdataNum = 0;
        public int IICDataInfor
        {
            get { return _iicdataNum; }
            set
            {
                _iicdataNum = value;
                RaisePropertyChanged("IICDataInfor");
            }
        }

        private ushort _iicAddrNum = 0;
        public ushort IICAddrNum
        {
            get { return _iicAddrNum; }
            set
            {
                if (value > 0xFFFF)
                {
                    throw new ArgumentOutOfRangeException("寄存器地址范围: 0x0000 - 0xFFFF (16位)");
                }
                _iicAddrNum = value;
                RaisePropertyChanged("IICAddrNum");
                RaisePropertyChanged("IICAddrNumHex");
                RaisePropertyChanged("IICAddrNumBinary");
            }
        }

        private ushort _iicDataNum = 0;
        public ushort IICDataNum
        {
            get { return _iicDataNum; }
            set
            {
                if (value > 0xFFFF)
                {
                    throw new ArgumentOutOfRangeException("数据范围: 0x0000 - 0xFFFF (16位)");
                }
                _iicDataNum = value;
                RaisePropertyChanged("IICDataNum");
                RaisePropertyChanged("IICDataNumHex");
                RaisePropertyChanged("IICDataNumBinary");
            }
        }

        public string IICAddrNumHex
        {
            get { return "0x" + _iicAddrNum.ToString("X4"); }
        }

        public string IICDataNumHex
        {
            get { return "0x" + _iicDataNum.ToString("X4"); }
        }

        public string IICAddrNumBinary
        {
            get { return Convert.ToString(_iicAddrNum, 2).PadLeft(16, '0'); }
        }

        public string IICDataNumBinary
        {
            get { return Convert.ToString(_iicDataNum, 2).PadLeft(16, '0'); }
        }

        private ObservableCollection<IICRegisterItem> _iicRegisterList = new ObservableCollection<IICRegisterItem>();
        public ObservableCollection<IICRegisterItem> IICRegisterList
        {
            get { return _iicRegisterList; }
        }

        public void AddIICRegisterItem()
        {
            int newIndex = _iicRegisterList.Count + 1;
            var item = new IICRegisterItem { Index = newIndex };
            _iicRegisterList.Add(item);
            RaisePropertyChanged("IICRegisterList");
            RaisePropertyChanged("TotalRegisterCount");
        }

        public void RemoveIICRegisterItem(IICRegisterItem item)
        {
            if (_iicRegisterList.Contains(item))
            {
                _iicRegisterList.Remove(item);
                ReindexIICRegisterList();
                RaisePropertyChanged("IICRegisterList");
                RaisePropertyChanged("TotalRegisterCount");
            }
        }

        public void ClearIICRegisterList()
        {
            _iicRegisterList.Clear();
            RaisePropertyChanged("IICRegisterList");
            RaisePropertyChanged("TotalRegisterCount");
        }

        private void ReindexIICRegisterList()
        {
            for (int i = 0; i < _iicRegisterList.Count; i++)
            {
                _iicRegisterList[i].Index = i + 1;
            }
        }

        public int TotalRegisterCount
        {
            get { return _iicRegisterList.Count; }
        }

        public string AllRegistersSummary
        {
            get
            {
                if (_iicRegisterList.Count == 0) return "(空)";
                var summary = _iicRegisterList.Select(r => $"{r.AddressHex},{r.DataHex}");
                return string.Join("; ", summary);
            }
        }

        private string _addrWidthMode = "TwoByte";
        public string AddrWidthMode
        {
            get
            {
                byte key = (byte)((IICInfor >> 4) & 0x0F);
                if (key == 1)
                    _addrWidthMode = "SingleByte";
                else if (key == 2)
                    _addrWidthMode = "TwoByte";
                else
                    _addrWidthMode = "SingleByte";

                return _addrWidthMode;
            }
        }

        private string _dataWidthMode = "SingleByte";
        public string DataWidthMode
        {
            get
            {
                byte key = (byte)(IICInfor & 0x0F);
                if (key == 1)
                    _dataWidthMode = "SingleByte";
                else if (key == 2)
                    _dataWidthMode = "TwoByte";
                else
                    _dataWidthMode = "SingleByte";

                Console.WriteLine("_dataWidthMode :" + _dataWidthMode + " key:" + key + " IICInfor:" + IICInfor);
                return _dataWidthMode;
            }
        }

        public string AddrModeDescription
        {
            get
            {
                return _addrWidthMode == "SingleByte"
                    ? "单字节地址模式 (8位, 0x00-0xFF)"
                    : "双字节地址模式 (16位, 0x0000-0xFFFF)";
            }
        }

        public string DataModeDescription
        {
            get
            {
                return _dataWidthMode == "SingleByte"
                    ? "单字节数据模式 (8位, 0x00-0xFF)"
                    : "双字节数据模式 (16位, 0x0000-0xFFFF)";
            }
        }

        /// <summary>
        /// 写入IIC配置到设备，参考SensorAdjust WriteReg的模式：检查底层API返回值，失败时记录错误。
        /// </summary>
        public bool WriteIICConfigsToDevice(byte[] data)
        {
            try
            {
                int IICDeviceModulePos = 16;
                int sentPos = 0;
                while (sentPos < data.Length)
                {
                    var dataToSend = data.Skip(sentPos).Take(512).ToArray();

                    if (dataToSend.Length > 0)
                    {
                        var dataHex = string.Join(" ", dataToSend.Select(b => b.ToString("X2")));
                        Logger.Debug($"[Write] IIC Send - Pos: {sentPos}, Len: {dataToSend.Length}, Data: {dataHex}");
                    }
                    int parameter = 0;
                    parameter = sentPos << 8 | (IICDeviceModulePos * Config.IspBitWidth);

                    Logger.Info($"[Write] IIC - Key: {IICDeviceModulePos}, Pos: {sentPos}, Len: {dataToSend.Length}, Param: 0x{parameter:X}");

                    lock (DeviceConfig.DeviceWriteLock)
                    {
                        bool success = DeviceApi.WriteAx327XIspProperty(DeviceConfig.DeviceLocation, parameter, dataToSend, sizeof(byte) * dataToSend.Length);
                        if (!success)
                        {
                            Logger.Error($"[Write] IIC Write FAILED - Pos: {sentPos}, Len: {dataToSend.Length}, DeviceApi returned false");
                            return false;
                        }
                    }
                    Logger.Info($"[Write] IIC Write OK - Pos: {sentPos}, Len: {dataToSend.Length}");
                    sentPos += dataToSend.Length;
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error writing all modules to device: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 从设备读取IIC配置，参考SensorAdjust ReadReg的模式：
        /// 1. 记录发送前数据(用于对比验证)
        /// 2. 检查底层API返回值，失败时返回null(区别于成功但空数据)
        /// 3. 记录接收后数据(用于调试分析)
        /// </summary>
        public byte[] ReadIICConfigFromDevice(byte[] data)
        {
            try
            {
                int IICDeviceModulePos = 16;
                int sentPos = 0;
                var resultBuffer = new List<byte>();

                while (sentPos < data.Length)
                {
                    var dataToRead = data.Skip(sentPos).Take(512).ToArray();

                    // 记录发送前的数据(作为读取请求的地址参数)
                    var sentHex = string.Join(" ", dataToRead.Select(b => b.ToString("X2")));
                    Logger.Debug($"[Read] IIC Send(Addr) - Pos: {sentPos}, Len: {dataToRead.Length}, Data: {sentHex}");

                    int parameter = 0;
                    parameter = sentPos << 8 | (IICDeviceModulePos * Config.IspBitWidth);
                    Logger.Info($"[Read] IIC - Key: {IICDeviceModulePos}, Pos: {sentPos}, Len: {dataToRead.Length}, Param: 0x{parameter:X}");

                    bool success;
                    lock (DeviceConfig.DeviceWriteLock)
                    {
                        success = DeviceApi.ReadAx327XIspProperty(DeviceConfig.DeviceLocation, parameter, dataToRead, sizeof(byte) * dataToRead.Length);
                    }

                    if (!success)
                    {
                        // 参考SensorAdjust: 读取失败时记录错误并返回null
                        Logger.Error($"[Read] IIC Read FAILED - Pos: {sentPos}, DeviceApi returned false. Sent: {sentHex}");
                        return null;
                    }

                    // 记录接收到的数据(读取结果)
                    var recvHex = string.Join(" ", dataToRead.Select(b => b.ToString("X2")));
                    Logger.Info($"[Read] IIC Recv(Data) - Pos: {sentPos}, Sent: {sentHex} -> Recv: {recvHex}");

                    resultBuffer.AddRange(dataToRead);
                    sentPos += dataToRead.Length;
                }
                return resultBuffer.ToArray();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error reading all modules from device: {ex.Message}", ex);
                return null;
            }
        }
    }
}
