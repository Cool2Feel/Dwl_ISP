using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;

namespace ThunderSE.DeviceConfig.Isp
{
    public class YGamma : ProcessStep, INotifyPropertyChanged
    {
        /*
        private struct YGammaParams
        {
            public int br_mod;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public int[] gma_num; //= new int[8];
            public int contra_num;
            public int bofst;
            public int lofst;
            public int lcpr_low;
            public int lcpr_high;
            public int lcpr_llimt;
            public int lcpr_hlimt;
            public int pad_num;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public short[] using_ygama;//= new short[512];
        };
        */

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct YGammaParams
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] gma_num;        // u8 gma_num[8]

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] yLowRate;       // u8 yLowRate[8]

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] yHighRate;      // u8 yHighRate[8]

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] rate;           // u8 rate[8]

            public byte ylowLimit;        // u8 ylowLimit
            public byte yhighLimit;       // u8 yhighLimit
            public byte fogPersent;       // u8 fogPersent
        };

        private short[] _yGammaTable = new short[]
        {
            0x0,0x8d,0xb5,0xd1,0xe8,0xfb,0x10c,0x11b,0x129,0x136,0x142,0x14d,0x157,0x161,0x16b,0x174,0x17c,0x185,0x18d,0x194,0x19c,0x1a3,0x1aa,0x1b1,0x1b8,
            0x1be,0x1c4,0x1cb,0x1d1,0x1d6,0x1dc,0x1e2,0x1e7,0x1ed,0x1f2,0x1f7,0x1fc,0x201,0x206,0x20b,0x210,0x214,0x219,0x21d,0x222,0x226,0x22b,0x22f,0x233,
            0x237,0x23b,0x240,0x244,0x247,0x24b,0x24f,0x253,0x257,0x25b,0x25e,0x262,0x266,0x269,0x26d,0x270,0x274,0x277,0x27a,0x27e,0x281,0x284,0x288,0x28b,
            0x28e,0x291,0x295,0x298,0x29b,0x29e,0x2a1,0x2a4,0x2a7,0x2aa,0x2ad,0x2b0,0x2b3,0x2b6,0x2b8,0x2bb,0x2be,0x2c1,0x2c4,0x2c7,0x2c9,0x2cc,
            0x2cf,0x2d1,0x2d4,0x2d7,0x2d9,0x2dc,0x2df,0x2e1,0x2e4,0x2e6,0x2e9,0x2eb,0x2ee,0x2f0,0x2f3,0x2f5,0x2f8,0x2fa,0x2fd,0x2ff,0x301,
            0x304,0x306,0x309,0x30b,0x30d,0x310,0x312,0x314,0x316,0x319,0x31b,0x31d,0x31f,0x322,0x324,0x326,0x328,0x32a,0x32d,0x32f,0x331,
            0x333,0x335,0x337,0x339,0x33c,0x33e,0x340,0x342,0x344,0x346,0x348,0x34a,0x34c,0x34e,0x350,0x352,0x354,0x356,0x358,0x35a,0x35c,
            0x35e,0x360,0x362,0x364,0x366,0x368,0x369,0x36b,0x36d,0x36f,0x371,0x373,0x375,0x377,0x378,0x37a,0x37c,0x37e,0x380,0x382,0x383,
            0x385,0x387,0x389,0x38b,0x38c,0x38e,0x390,0x392,0x393,0x395,0x397,0x399,0x39a,0x39c,0x39e,0x39f,0x3a1,0x3a3,0x3a5,0x3a6,0x3a8,
            0x3aa,0x3ab,0x3ad,0x3af,0x3b0,0x3b2,0x3b4,0x3b5,0x3b7,0x3b8,0x3ba,0x3bc,0x3bd,0x3bf,0x3c1,0x3c2,0x3c4,0x3c5,0x3c7,0x3c8,0x3ca,0x3cc,
            0x3cd,0x3cf,0x3d0,0x3d2,0x3d3,0x3d5,0x3d7,0x3d8,0x3da,0x3db,0x3dd,0x3de,0x3e0,0x3e1,0x3e3,0x3e4,0x3e6,0x3e7,0x3e9,0x3ea,0x3ec,0x3ed,
            0x3ef,0x3f0,0x3f2,0x3f3,0x3f4,0x3f6,0x3f7,0x3f9,0x3fa,0x3fc,0x3fd,0x3ff
        };
        private byte _pad_num = 1;

        private byte[] _gma_num = new byte[8];
        private byte[] _yLowRate = new byte[8];
        private byte[] _yHighRate = new byte[8];
        private byte[] _rate = new byte[8];
        private byte _ylowLimit;
        private byte _yhighLimit;
        private byte _fogPersent;

        public event PropertyChangedEventHandler PropertyChanged;

        public YGamma()
        {
            DeviceModulePos = 7;

            SetPreviousStepEnable(IspModule.Blc, true);
            SetPreviousStepEnable(IspModule.Lsc, true);
            SetPreviousStepEnable(IspModule.Awb, true);
        }

        #region Properties
        public short[] YGammaTable
        {
            get { return _yGammaTable; }
            set
            {
                _yGammaTable = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("YGammaTable");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public byte PadNum
        {
            get { return _pad_num; }
            set
            {
                _pad_num = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("PadNum");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public byte[] Gma_Num
        {
            get { return _gma_num; }
            set
            {
                _gma_num = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Gma_Num");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public byte[] YLowRate
        {
            get { return _yLowRate; }
            set
            {
                _yLowRate = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("YLowRate");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }
        public byte[] YHighRate
        {
            get { return _yHighRate; }
            set
            {
                _yHighRate = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("YHighRate");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public byte[] Rate
        {
            get { return _rate; }
            set
            {
                _rate = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Rate");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public byte YLowLimit
        {
            get { return _ylowLimit; }
            set
            {
                _ylowLimit = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("YLowLimit");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public byte YHighLimit
        {
            get { return _yhighLimit; }
            set
            {
                _yhighLimit = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("YHighLimit");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public byte FogPersent
        {
            get { return _fogPersent; }
            set
            {
                _fogPersent = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("FogPersent");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public string LoadYGammaTable
        {
            set
            {
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("LoadYGammaTable");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public string SaveYGammaTable
        {
            set
            {
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("SaveYGammaTable");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        #endregion

        public void LoadYGammaTableFromFile(string tableFile)
        {
            try
            {
                string fileContent = File.ReadAllText(tableFile);

                short[] yGammaTable;

                if (fileContent.StartsWith("0x"))
                {
                    //yGammaTable = fileContent.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                    //.Select(s => short.Parse(s.Substring(2).ToUpper(), System.Globalization.NumberStyles.HexNumber))
                    //.ToArray();
                    // 十六进制格式：支持 \r\n、\n、\r 作为行分隔符
                    string[] lines = fileContent.Split(
                        new string[] { ",", "\r\n", "\n", "\r" },
                        StringSplitOptions.RemoveEmptyEntries);

                    yGammaTable = lines
                        .Select(line => line.Trim())
                        .Where(line => !string.IsNullOrEmpty(line))
                        .Select(line =>
                        {
                            string hexStr = line.StartsWith("0x") || line.StartsWith("0X")
                                ? line.Substring(2)
                                : line;
                            return short.Parse(hexStr, System.Globalization.NumberStyles.HexNumber);
                        })
                        .ToArray();
                }
                else
                {
                    //yGammaTable = fileContent.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    //.Select(s => Convert.ToInt16(s))
                    //.ToArray();
                    // 十进制格式：支持逗号、空格、换行、制表符等多种分隔符
                    yGammaTable = fileContent.Split(
                        new char[] { ',', ' ', '\r', '\n', '\t', ';' },
                        StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => Convert.ToInt16(s.Trim()))
                        .ToArray();
                }

                if (yGammaTable.Length < 256)
                {
                    System.Windows.MessageBox.Show("数据格式不正确！", "", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                _yGammaTable = yGammaTable;

                PropertyChangedEventArgs args = new PropertyChangedEventArgs("YGammaTable");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
            catch (FileNotFoundException)
            {
                System.Windows.MessageBox.Show($"文件不存在：{tableFile}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            catch (FormatException)
            {
                System.Windows.MessageBox.Show("数据格式错误，无法解析为数字。", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"加载失败：{ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public void SaveYGammaTableToFile(string tableFile)
        {
            string fileContent = String.Join(",", new List<short>(_yGammaTable).ConvertAll(i => i.ToString()).ToArray());

            fileContent = fileContent.Substring(0, fileContent.Length);

            File.WriteAllText(tableFile, fileContent);
        }

        public override void ProcessRawBuffer(ref byte[] imgBuffer)
        {
            throw new NotImplementedException();
        }

        public override void ProcessRgbBuffer(ref byte[] imgBuffer)
        {
            int tmpReadPos = 0;
            IntPtr[] inBuffer = new IntPtr[3];
            for (int i = 0; i < inBuffer.Length; i++)
            {
                inBuffer[i] = Marshal.AllocHGlobal(_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
                Marshal.Copy(imgBuffer,
                    tmpReadPos, inBuffer[i], _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));

                tmpReadPos += _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short);
            }

            IntPtr[] outBuffer = new IntPtr[3];
            for (int i = 0; i < outBuffer.Length; i++)
            {
                outBuffer[i] = Marshal.AllocHGlobal(_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
                Marshal.Copy(new byte[_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short)],
                    0, outBuffer[i], _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
            }

            IspApi.YGammaImg(_commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, PadNum, YGammaTable, inBuffer, outBuffer);

            for (int i = 0; i < outBuffer.Length; i++)
            {
                Marshal.FreeHGlobal(inBuffer[i]);
            }

            tmpReadPos = 0;
            for (int i = 0; i < outBuffer.Length; i++)
            {
                Marshal.Copy(outBuffer[i], imgBuffer, tmpReadPos, _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
                tmpReadPos += _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short);
                Marshal.FreeHGlobal(outBuffer[i]);
            }
        }

        public override Dictionary<int, byte[]> ParamsDataCollection
        {
            get
            {
                YGammaParams yGammaParams = new YGammaParams()
                {
                    //br_mod = 0,
                    gma_num = Gma_Num,
                    yLowRate = YLowRate,
                    yHighRate = YHighRate,
                    rate = Rate,
                    ylowLimit = YLowLimit,
                    yhighLimit = YHighLimit,
                    fogPersent = FogPersent,
                    //contra_num = 0,
                    //bofst = 0,
                    //lofst = 0,
                    //lcpr_low = 0,
                    //lcpr_high = 0,
                    //lcpr_llimt = 0,
                    //lcpr_hlimt = 0,
                    //pad_num = PadNum,
                    //using_ygama = YGammaTable
                };

                int size = Marshal.SizeOf(yGammaParams);
                byte[] arr = new byte[size];

                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(yGammaParams, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                Marshal.FreeHGlobal(ptr);

                return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
            }
            set
            {
                YGammaParams yGammaParams = new YGammaParams();

                int size = Marshal.SizeOf(yGammaParams);
                IntPtr ptr = Marshal.AllocHGlobal(size);

                Marshal.Copy(value[DeviceModulePos], 0, ptr, size);

                yGammaParams = (YGammaParams)Marshal.PtrToStructure(ptr, yGammaParams.GetType());
                Marshal.FreeHGlobal(ptr);

                //YGammaTable = yGammaParams.using_ygama;
                //PadNum = (byte)yGammaParams.pad_num;
                Gma_Num = yGammaParams.gma_num;
                YLowRate = yGammaParams.yLowRate;
                YHighRate = yGammaParams.yHighRate;
                Rate = yGammaParams.rate;
                YLowLimit = yGammaParams.ylowLimit;
                YHighLimit = yGammaParams.yhighLimit;
                FogPersent = yGammaParams.fogPersent;
            }
        }

        public override XmlElement SerializeToXmlElement(XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("YGamma");

            XmlElement globalGammaTableNode = xmlDoc.CreateElement("Global_Gamma_Table");
            string yGammaTable = string.Join(",", YGammaTable.Select(x => x.ToString()).ToArray());
            globalGammaTableNode.AppendChild(xmlDoc.CreateTextNode(yGammaTable));
            xmlElement.AppendChild(globalGammaTableNode);

            //XmlElement padNumNode = xmlDoc.CreateElement("Pad_Num");
            //padNumNode.AppendChild(xmlDoc.CreateTextNode(PadNum.ToString()));
            //xmlElement.AppendChild(padNumNode);

            // 添加遗漏字段的序列化
            XmlElement gmaNumNode = xmlDoc.CreateElement("Gma_Num");
            string gmaNumStr = string.Join(",", Gma_Num.Select(x => x.ToString()).ToArray());
            gmaNumNode.AppendChild(xmlDoc.CreateTextNode(gmaNumStr));
            xmlElement.AppendChild(gmaNumNode);

            XmlElement yLowRateNode = xmlDoc.CreateElement("YLowRate");
            string yLowRateStr = string.Join(",", YLowRate.Select(x => x.ToString()).ToArray());
            yLowRateNode.AppendChild(xmlDoc.CreateTextNode(yLowRateStr));
            xmlElement.AppendChild(yLowRateNode);

            XmlElement yHighRateNode = xmlDoc.CreateElement("YHighRate");
            string yHighRateStr = string.Join(",", YHighRate.Select(x => x.ToString()).ToArray());
            yHighRateNode.AppendChild(xmlDoc.CreateTextNode(yHighRateStr));
            xmlElement.AppendChild(yHighRateNode);

            XmlElement rateNode = xmlDoc.CreateElement("Rate");
            string rateStr = string.Join(",", Rate.Select(x => x.ToString()).ToArray());
            rateNode.AppendChild(xmlDoc.CreateTextNode(rateStr));
            xmlElement.AppendChild(rateNode);

            XmlElement yLowLimitNode = xmlDoc.CreateElement("YLowLimit");
            yLowLimitNode.AppendChild(xmlDoc.CreateTextNode(YLowLimit.ToString()));
            xmlElement.AppendChild(yLowLimitNode);

            XmlElement yHighLimitNode = xmlDoc.CreateElement("YHighLimit");
            yHighLimitNode.AppendChild(xmlDoc.CreateTextNode(YHighLimit.ToString()));
            xmlElement.AppendChild(yHighLimitNode);

            XmlElement fogPersentNode = xmlDoc.CreateElement("FogPersent");
            fogPersentNode.AppendChild(xmlDoc.CreateTextNode(FogPersent.ToString()));
            xmlElement.AppendChild(fogPersentNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
        {
            var yGammaNode = ispToolDataNode["YGamma"];

            var tmpYGammaTableStr = XmlHelper.GetNodeValue(yGammaNode, "Global_Gamma_Table");
            if (tmpYGammaTableStr != null)
            {
                YGammaTable = tmpYGammaTableStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt16(s))
                    .ToArray();
            }

            //PadNum = Convert.ToByte(XmlHelper.GetNodeValue(yGammaNode, "Pad_Num"));

            // 添加遗漏字段的反序列化
            var tmpGmaNumStr = XmlHelper.GetNodeValue(yGammaNode, "Gma_Num");
            if (tmpGmaNumStr != null)
            {
                Gma_Num = tmpGmaNumStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }

            var tmpYLowRateStr = XmlHelper.GetNodeValue(yGammaNode, "YLowRate");
            if (tmpYLowRateStr != null)
            {
                YLowRate = tmpYLowRateStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }

            var tmpYHighRateStr = XmlHelper.GetNodeValue(yGammaNode, "YHighRate");
            if (tmpYHighRateStr != null)
            {
                YHighRate = tmpYHighRateStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }

            var tmpRateStr = XmlHelper.GetNodeValue(yGammaNode, "Rate");
            if (tmpRateStr != null)
            {
                Rate = tmpRateStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }

            YLowLimit = (byte)XmlHelper.GetNodeInt(yGammaNode, "YLowLimit", 0);
            YHighLimit = (byte)XmlHelper.GetNodeInt(yGammaNode, "YHighLimit", 0);
            FogPersent = (byte)XmlHelper.GetNodeInt(yGammaNode, "FogPersent", 0);
        }
    }
}
