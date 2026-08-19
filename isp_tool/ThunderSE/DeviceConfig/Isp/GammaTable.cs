using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;

namespace ThunderSE.DeviceConfig.Isp
{
    public class GammaTable : ProcessStep, INotifyPropertyChanged
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct YGammaParams
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public byte[] gamma_table;

        };

        private byte[] _yGammaTable = new byte[]
        {
            0x0, 0x3, 0x6, 0x9, 0xC, 0xF, 0x12, 0x15, 0x18, 0x1B, 0x1E, 0x21, 0x23, 0x26, 0x29, 0x2B, 0x2E, 0x31, 0x33, 0x36, 0x38, 0x3B, 0x3D, 0x40, 0x42, 0x44, 0x47, 0x49, 0x4B, 0x4E, 0x50, 0x52,
            0x54, 0x56, 0x59, 0x5B, 0x5D, 0x5F, 0x61, 0x63, 0x65, 0x67, 0x69, 0x6B, 0x6D, 0x6E, 0x70, 0x72, 0x74, 0x76, 0x77, 0x79, 0x7B, 0x7C, 0x7E, 0x80, 0x81, 0x83, 0x85, 0x86, 0x88, 0x89, 0x8B, 0x8C,
            0x8E, 0x8F, 0x90, 0x92, 0x93, 0x95, 0x96, 0x97, 0x99, 0x9A, 0x9B, 0x9C, 0x9E, 0x9F, 0xA0, 0xA1, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xAB, 0xAC, 0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2,
            0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xB9, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBE, 0xBF, 0xC0, 0xC1, 0xC1, 0xC2, 0xC3, 0xC3, 0xC4, 0xC5, 0xC5, 0xC6, 0xC7, 0xC7, 0xC8, 0xC9, 0xC9, 0xCA, 0xCA,
            0xCB, 0xCC, 0xCC, 0xCD, 0xCD, 0xCE, 0xCE, 0xCF, 0xCF, 0xD0, 0xD0, 0xD1, 0xD1, 0xD2, 0xD2, 0xD3, 0xD3, 0xD4, 0xD4, 0xD4, 0xD5, 0xD5, 0xD6, 0xD6, 0xD7, 0xD7, 0xD7, 0xD8, 0xD8, 0xD9, 0xD9, 0xD9,
            0xDA, 0xDA, 0xDA, 0xDB, 0xDB, 0xDB, 0xDC, 0xDC, 0xDC, 0xDD, 0xDD, 0xDD, 0xDE, 0xDE, 0xDE, 0xDF, 0xDF, 0xDF, 0xE0, 0xE0, 0xE0, 0xE1, 0xE1, 0xE1, 0xE2, 0xE2, 0xE2, 0xE2, 0xE3, 0xE3, 0xE3, 0xE4,
            0xE4, 0xE4, 0xE5, 0xE5, 0xE5, 0xE6, 0xE6, 0xE6, 0xE6, 0xE7, 0xE7, 0xE7, 0xE8, 0xE8, 0xE8, 0xE9, 0xE9, 0xE9, 0xEA, 0xEA, 0xEA, 0xEB, 0xEB, 0xEB, 0xEC, 0xEC, 0xED, 0xED, 0xED, 0xEE, 0xEE, 0xEE,
            0xEF, 0xEF, 0xF0, 0xF0, 0xF0, 0xF1, 0xF1, 0xF2, 0xF2, 0xF3, 0xF3, 0xF4, 0xF4, 0xF5, 0xF5, 0xF6, 0xF6, 0xF7, 0xF7, 0xF8, 0xF8, 0xF9, 0xF9, 0xFA, 0xFB, 0xFB, 0xFC, 0xFC, 0xFD, 0xFE, 0xFE, 0xFF
        };

        public event PropertyChangedEventHandler PropertyChanged;


        public GammaTable()
        {
            DeviceModulePos = 17;
        }

        public byte[] Y_Gamma_Table
        {
            get { return _yGammaTable; }
            set
            {
                _yGammaTable = value;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Y_Gamma_Table");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public string Y_Gamma_Table_String
        {
            set
            {
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Y_Gamma_Table");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }


        public override Dictionary<int, byte[]> ParamsDataCollection
        {
            get
            {
                YGammaParams yGammaParams = new YGammaParams()
                {
                    gamma_table = _yGammaTable,
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
                Y_Gamma_Table = yGammaParams.gamma_table;
            }
        }


        public override void ProcessRawBuffer(ref byte[] imgBuffer)
        {
            throw new NotImplementedException();
        }

        public override void ProcessRgbBuffer(ref byte[] imgBuffer)
        {
            throw new NotImplementedException();
        }

        public override XmlElement SerializeToXmlElement(XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("GammaTable");

            XmlElement gammaTableNode = xmlDoc.CreateElement("Y_Gamma_Table");
            string gammaTableStr = string.Join(",", Y_Gamma_Table.Select(x => x.ToString()).ToArray());
            gammaTableNode.AppendChild(xmlDoc.CreateTextNode(gammaTableStr));
            xmlElement.AppendChild(gammaTableNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
        {
            var gammaTableNode = ispToolDataNode["GammaTable"];

            var tmpGammaTableStr = XmlHelper.GetNodeValue(gammaTableNode, "Y_Gamma_Table");
            if (tmpGammaTableStr != null)
            {
                Y_Gamma_Table = tmpGammaTableStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }
        }
    }
}
