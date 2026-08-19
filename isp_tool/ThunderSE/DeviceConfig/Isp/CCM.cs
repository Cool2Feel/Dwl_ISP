using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using ThunderSE.Common;
using ThunderSE.DeviceConfig;

namespace ThunderSE.DeviceConfig.Isp
{
    class CCM : ProcessStep, INotifyPropertyChanged
    {
        private struct CCMParams
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
            public short[] ccm;
	        public short s41;
	        public short s42;
	        public short s43;
        }

        private short[] _ccm = new short[9];
        private short _s41;
        private short _s42;
        private short _s43;

        public CCM()
        {
            DeviceModulePos = 5;
        }

        public short[] ccm
        {
            get { return _ccm; }
            set
            {
                _ccm = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ccm"));
                }
            }
        }

        public short s41
        {
            get { return _s41; }
            set
            {
                _s41 = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("s41"));
                }
            }
        }

        public short s42
        {
            get { return _s42; }
            set
            {
                _s42 = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("s42"));
                }
            }
        }

        public short s43
        {
            get { return _s43; }
            set
            {
                _s43 = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("s43"));
                }
            }
        }

        public override void ProcessRawBuffer(ref byte[] imgBuffer)
        {
            if (imgBuffer == null || imgBuffer.Length == 0)
                throw new ArgumentException("图像缓冲区为空");

            try
            {
                int width = _commonConfig.ResolutionWidth;
                int height = _commonConfig.ResolutionHeight;
                int totalPixels = width * height;

                if (totalPixels <= 0) return;

                IntPtr[] inputPtrs = new IntPtr[3];
                IntPtr[] outputPtrs = new IntPtr[3];

                for (int i = 0; i < 3; i++)
                {
                    inputPtrs[i] = Marshal.AllocHGlobal(totalPixels * sizeof(short));
                    outputPtrs[i] = Marshal.AllocHGlobal(totalPixels * sizeof(short));
                }

                try
                {
                    int[,] matrix = new int[3, 3];
                    for (int row = 0; row < 3; row++)
                        for (int col = 0; col < 3; col++)
                            matrix[row, col] = ccm[row * 3 + col];

                    int[][] matrixJagged = new int[3][];
                    for (int i = 0; i < 3; i++)
                        matrixJagged[i] = new int[] { matrix[i, 0], matrix[i, 1], matrix[i, 2] };

                    int[] offsets = { s41, s42, s43 };

                    IspApi.CCM_Img(inputPtrs, outputPtrs, width, height, matrix, offsets);

                    byte[] outputBuffer = new byte[imgBuffer.Length];
                    int outSize = outputBuffer.Length;

                    IspApi.EncoderImgBuffer(outputPtrs, width, height, 2, outputBuffer, ref outSize);

                    if (outSize > 0 && outSize <= imgBuffer.Length)
                        Array.Copy(outputBuffer, imgBuffer, Math.Min(outSize, imgBuffer.Length));
                }
                finally
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (inputPtrs[i] != IntPtr.Zero) Marshal.FreeHGlobal(inputPtrs[i]);
                        if (outputPtrs[i] != IntPtr.Zero) Marshal.FreeHGlobal(outputPtrs[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"CCM ProcessRawBuffer异常: {ex.Message}");
                throw;
            }
        }

        public override void ProcessRgbBuffer(ref byte[] imgBuffer)
        {
            throw new NotImplementedException();
        }

        public override Dictionary<int, byte[]> ParamsDataCollection
        {
            get
            {
                CCMParams ccmParams = new CCMParams()
                {
                    ccm = ccm,
                    s41 = s41,
                    s42 = s42,
                    s43 = s43
                };

                int size = Marshal.SizeOf(ccmParams);
                byte[] arr = new byte[size];

                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(ccmParams, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                Marshal.FreeHGlobal(ptr);

                return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
            }
            set
            {
                CCMParams ccmParams = new CCMParams();

                int size = Marshal.SizeOf(ccmParams);
                IntPtr ptr = Marshal.AllocHGlobal(size);

                Marshal.Copy(value[DeviceModulePos], 0, ptr, size);

                ccmParams = (CCMParams)Marshal.PtrToStructure(ptr, ccmParams.GetType());
                Marshal.FreeHGlobal(ptr);

                ccm = ccmParams.ccm;
                s41 = ccmParams.s41;
                s42 = ccmParams.s42;
                s43 = ccmParams.s43;
            }
        }

        public override System.Xml.XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("CCM");

            XmlElement CcmNode = xmlDoc.CreateElement("ccm");
            string CcmStr = string.Join(",", ccm.Select(x => x.ToString()).ToArray());
            CcmNode.AppendChild(xmlDoc.CreateTextNode(CcmStr));
            xmlElement.AppendChild(CcmNode);

            // 添加s41、s42、s43字段的序列化
            XmlElement s41Node = xmlDoc.CreateElement("s41");
            s41Node.AppendChild(xmlDoc.CreateTextNode(s41.ToString()));
            xmlElement.AppendChild(s41Node);

            XmlElement s42Node = xmlDoc.CreateElement("s42");
            s42Node.AppendChild(xmlDoc.CreateTextNode(s42.ToString()));
            xmlElement.AppendChild(s42Node);

            XmlElement s43Node = xmlDoc.CreateElement("s43");
            s43Node.AppendChild(xmlDoc.CreateTextNode(s43.ToString()));
            xmlElement.AppendChild(s43Node);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(System.Xml.XmlElement ispToolDataNode)
        {
            var CcmNode = ispToolDataNode["CCM"];

            var tmpCcmStr = XmlHelper.GetNodeValue(CcmNode, "ccm");
            if (tmpCcmStr != null)
            {
                ccm = tmpCcmStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt16(s))
                    .ToArray();
            }

            // 添加s41、s42、s43字段的反序列化
            s41 = XmlHelper.GetNodeShort(CcmNode, "s41", 0);
            s42 = XmlHelper.GetNodeShort(CcmNode, "s42", 0);
            s43 = XmlHelper.GetNodeShort(CcmNode, "s43", 0);
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
