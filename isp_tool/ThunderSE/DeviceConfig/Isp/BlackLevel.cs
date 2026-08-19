using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Serialization;
using ThunderSE.DeviceConfig;
using ThunderSE.Common;

namespace ThunderSE.DeviceConfig.Isp
{
    public enum BlackLevelPixelType
    {
        R,
        Gr,
        Gb,
        B
    }

    public class BlackLevel : ProcessStep, INotifyPropertyChanged
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlcParams
        {
            public short blkl_r;
            public short blkl_gr;
            public short blkl_gb;
            public short blkl_b;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] blk_rate;   // u8 blk_rate[8]
        };

        private short[] _correctValuesArray = new short[4];

        private byte[] _blk_rate = new byte[8];

        public event PropertyChangedEventHandler PropertyChanged;

        public BlackLevel()
        {
            DeviceModulePos = 1;
        }

        public short R
        {
            get { return _correctValuesArray[(int)BlackLevelPixelType.R]; }
            set
            {
                SetCorrectValue(BlackLevelPixelType.R, value);
                // _correctValuesArray[(int)BlackLevelPixelType.R] = value;

                // HasChangedParams = true;
                // PropertyChangedEventArgs args = new PropertyChangedEventArgs("CorrectValuesArray");
                // if (PropertyChanged != null)
                //     PropertyChanged(this, args);
            }
        }

        public short Gr
        {
            get { return _correctValuesArray[(int)BlackLevelPixelType.Gr]; }
            set
            {
                SetCorrectValue(BlackLevelPixelType.Gr, value);
                // _correctValuesArray[(int)BlackLevelPixelType.Gr] = value;

                // HasChangedParams = true;
                // PropertyChangedEventArgs args = new PropertyChangedEventArgs("CorrectValuesArray");
                // if (PropertyChanged != null)
                //     PropertyChanged(this, args);
            }
        }

        public short B
        {
            get { return _correctValuesArray[(int)BlackLevelPixelType.B]; }
            set
            {
                SetCorrectValue(BlackLevelPixelType.B, value);
                // _correctValuesArray[(int)BlackLevelPixelType.B] = value;

                // HasChangedParams = true;
                // PropertyChangedEventArgs args = new PropertyChangedEventArgs("CorrectValuesArray");
                // if (PropertyChanged != null)
                //     PropertyChanged(this, args);
            }
        }

        public short Gb
        {
            get { return _correctValuesArray[(int)BlackLevelPixelType.Gb]; }
            set
            {
                SetCorrectValue(BlackLevelPixelType.Gb, value);
                // _correctValuesArray[(int)BlackLevelPixelType.Gb] = value;

                // HasChangedParams = true;
                // PropertyChangedEventArgs args = new PropertyChangedEventArgs("CorrectValuesArray");
                // if (PropertyChanged != null)
                //     PropertyChanged(this, args);
            }
        }

        public byte[] Blk_Rate
        {
            get { return _blk_rate; }
            set
            {
                _blk_rate = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("Blk_Rate"));
                }
            }
        }

        private void SetCorrectValue(BlackLevelPixelType pixelType, short value,
        [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            _correctValuesArray[(int)pixelType] = value;
            HasChangedParams = true;

            // 通知具体改变的属性 (R/Gr/Gb/B)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            
            // 同时通知数组属性 (兼容绑定到 CorrectValuesArray 的场景)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CorrectValuesArray"));
        }

        public short[] CorrectValuesArray
        {
            get { return (short[])_correctValuesArray.Clone(); }
        }

        public void ApplyBlackLevelCorrection(short[] correctValues, bool isMinus = true)
        {
            //short[] outputBuffer = new short[_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight];
            //Array.Clear(outputBuffer, 0, outputBuffer.Length);

            //byte[] outputByteBuffer = new byte[Buffer.ByteLength(outputBuffer)];
            if (correctValues == null || correctValues.Length != 4)
                throw new ArgumentException("校正值数组必须包含4个元素");

            _correctValuesArray = correctValues;
            if (isMinus)
            {
                //_correctValuesArray = correctValues.Select(x => x = (short)-x).ToArray();

                // 正确写法：直接取负
                _correctValuesArray = new short[4];
                for (int i = 0; i < 4; i++)
                {
                    _correctValuesArray[i] = (short)-correctValues[i];
                }
            }
            else
            {
                // 创建副本，避免外部修改影响内部状态
                _correctValuesArray = (short[])correctValues.Clone();
            }

            HasChangedParams = true;
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("CorrectValuesArray"));
        }

        public override void ProcessRawBuffer(ref byte[] imgBuffer)
        {
            try
            {
                if (imgBuffer == null)
                    throw new ArgumentNullException(nameof(imgBuffer));
                if (_commonConfig == null)
                    throw new InvalidOperationException("CommonConfig not initialized");

                Logger.Debug($"[BLC] Processing - Buffer: {imgBuffer.Length} bytes, Resolution: {_commonConfig.ResolutionWidth}x{_commonConfig.ResolutionHeight}, Bayer: {_commonConfig.Bayer}");
                Logger.Debug($"[BLC] Correction values: R={R}, Gr={Gr}, Gb={Gb}, B={B}");

                int pixelCount = _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight;
                short[] outputBuffer = new short[pixelCount];

                Logger.Debug($"[BLC] Calling IspApi.BlcImg with {pixelCount} pixels");

                IspApi.BlcImg(imgBuffer, _correctValuesArray, (int)_commonConfig.Bayer,
                    _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, outputBuffer);

                Logger.Debug($"[BLC] BlcImg completed, output: {outputBuffer.Length} shorts");

                // 直接转换，避免中间变量
                imgBuffer = new byte[pixelCount * sizeof(short)];
                Buffer.BlockCopy(outputBuffer, 0, imgBuffer, 0, imgBuffer.Length);

                Logger.Debug($"[BLC] Processing completed, output buffer: {imgBuffer.Length} bytes");
            }
            catch (Exception ex)
            {
                Logger.Error("[BLC] ProcessRawBuffer failed.", ex);
                throw;
            }
        }

        public void CalBlackLevelData(byte[] nativeRawFileBuffer, Dictionary<BlackLevelPixelType, short[]> blackLevelDataArrays)
        {
            if (nativeRawFileBuffer == null)
                throw new ArgumentNullException(nameof(nativeRawFileBuffer));
            if (blackLevelDataArrays == null)
                throw new ArgumentNullException(nameof(blackLevelDataArrays));
            if (_commonConfig == null)
                throw new InvalidOperationException("CommonConfig not initialized");

            IntPtr[] ptrArray = null;
            try
            {
                ptrArray = new IntPtr[5];//4 / 5 ? (预留一个额外的指针位置，以防未来扩展或特殊情况)
                var arrayLength = _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight / 4;

                for (int i = 0; i < ptrArray.Length; i++)
                {
                    ptrArray[i] = Marshal.AllocHGlobal(arrayLength * sizeof(short));
                    // 无需零初始化，BlcCal 会覆盖所有数据
                }

                IspApi.BlcCal(nativeRawFileBuffer, _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight,
                    (int)_commonConfig.Bayer, ptrArray);

                Marshal.Copy(ptrArray[(int)BlackLevelPixelType.R], blackLevelDataArrays[BlackLevelPixelType.R], 0, arrayLength);
                Marshal.Copy(ptrArray[(int)BlackLevelPixelType.Gr], blackLevelDataArrays[BlackLevelPixelType.Gr], 0, arrayLength);
                Marshal.Copy(ptrArray[(int)BlackLevelPixelType.Gb], blackLevelDataArrays[BlackLevelPixelType.Gb], 0, arrayLength);
                Marshal.Copy(ptrArray[(int)BlackLevelPixelType.B], blackLevelDataArrays[BlackLevelPixelType.B], 0, arrayLength);

            }
            finally
            {
                if (ptrArray != null)
                    for (int i = 0; i < ptrArray.Length; i++)
                    {
                        Marshal.FreeHGlobal(ptrArray[i]);
                    }
            }
        }

        public override Dictionary<int, byte[]> ParamsDataCollection
        {
            get
            {
                BlcParams blcParams = new BlcParams()
                {
                    blkl_r = R,
                    blkl_gr = Gr,
                    blkl_gb = Gb,
                    blkl_b = B,
                    blk_rate = Blk_Rate
                };

                int size = Marshal.SizeOf(blcParams);
                byte[] arr = new byte[size];

                //IntPtr ptr = Marshal.AllocHGlobal(size);
                //Marshal.StructureToPtr(blcParams, ptr, true);
                //Marshal.Copy(ptr, arr, 0, size);
                //Marshal.FreeHGlobal(ptr);
                IntPtr ptr = IntPtr.Zero;
                try
                {
                    ptr = Marshal.AllocHGlobal(size);
                    Marshal.StructureToPtr(blcParams, ptr, false);
                    Marshal.Copy(ptr, arr, 0, size);
                }
                finally
                {
                    if (ptr != IntPtr.Zero)
                        Marshal.FreeHGlobal(ptr);
                }

                return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
            }
            set
            {
                //BlcParams blcParams = new BlcParams();

                //int size = Marshal.SizeOf(blcParams);
                //IntPtr ptr = Marshal.AllocHGlobal(size);

                //Marshal.Copy(value[DeviceModulePos], 0, ptr, size);

                //blcParams = (BlcParams)Marshal.PtrToStructure(ptr, blcParams.GetType());
                //Marshal.FreeHGlobal(ptr);

                //R = (short)blcParams.blkl_r;
                //Gr = (short)blcParams.blkl_gr;
                //Gb = (short)blcParams.blkl_gb;
                //B = (short)blcParams.blkl_b;

                if (value == null || !value.ContainsKey(DeviceModulePos))
                    throw new ArgumentException("ParamsDataCollection 数据缺失");

                byte[] data = value[DeviceModulePos];
                int expectedSize = Marshal.SizeOf(typeof(BlcParams));

                if (data.Length != expectedSize)
                    throw new ArgumentException($"数据尺寸不匹配: 期望 {expectedSize}，实际 {data.Length}");

                IntPtr ptr = IntPtr.Zero;
                try
                {
                    ptr = Marshal.AllocHGlobal(expectedSize);
                    Marshal.Copy(data, 0, ptr, expectedSize);
                    BlcParams blcParams = (BlcParams)Marshal.PtrToStructure(ptr, typeof(BlcParams));

                    R = blcParams.blkl_r;
                    Gr = blcParams.blkl_gr;
                    Gb = blcParams.blkl_gb;
                    B = blcParams.blkl_b;
                    Blk_Rate = blcParams.blk_rate;
                }
                finally
                {
                    if (ptr != IntPtr.Zero)
                        Marshal.FreeHGlobal(ptr);
                }
            }
        }

        public override XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("Blc");

            XmlElement blcRNode = xmlDoc.CreateElement("BlcR");
            blcRNode.AppendChild(xmlDoc.CreateTextNode(R.ToString()));
            xmlElement.AppendChild(blcRNode);

            XmlElement blcGrNode = xmlDoc.CreateElement("BlcGr");
            blcGrNode.AppendChild(xmlDoc.CreateTextNode(Gr.ToString()));
            xmlElement.AppendChild(blcGrNode);

            XmlElement blcGbNode = xmlDoc.CreateElement("BlcGb");
            blcGbNode.AppendChild(xmlDoc.CreateTextNode(Gb.ToString()));
            xmlElement.AppendChild(blcGbNode);

            XmlElement blcBNode = xmlDoc.CreateElement("BlcB");
            blcBNode.AppendChild(xmlDoc.CreateTextNode(B.ToString()));
            xmlElement.AppendChild(blcBNode);

            // 添加Blk_Rate数组的序列化
            XmlElement blkRateNode = xmlDoc.CreateElement("Blk_Rate");
            string blkRateStr = string.Join(",", Blk_Rate.Select(x => x.ToString()).ToArray());
            blkRateNode.AppendChild(xmlDoc.CreateTextNode(blkRateStr));
            xmlElement.AppendChild(blkRateNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
        {
            if (ispToolDataNode == null)
                return;

            var blcNode = ispToolDataNode["Blc"];
            if (blcNode == null)
                return;

            R = XmlHelper.GetNodeShort(blcNode, "BlcR", 0);
            Gr = XmlHelper.GetNodeShort(blcNode, "BlcGr", 0);
            Gb = XmlHelper.GetNodeShort(blcNode, "BlcGb", 0);
            B = XmlHelper.GetNodeShort(blcNode, "BlcB", 0);

            // 添加Blk_Rate数组的反序列化
            var tmpBlkRateStr = XmlHelper.GetNodeValue(blcNode, "Blk_Rate");
            if (tmpBlkRateStr != null)
            {
                Blk_Rate = tmpBlkRateStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }
        }

        public override void ProcessRgbBuffer(ref byte[] imgBuffer)
        {
            throw new NotImplementedException();
        }
    }
}
