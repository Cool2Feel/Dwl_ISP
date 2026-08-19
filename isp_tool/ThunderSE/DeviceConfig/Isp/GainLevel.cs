using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;

namespace ThunderSE.DeviceConfig.Isp
{
    public class GainLevel : ProcessStep, INotifyPropertyChanged
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct GainLevelParams
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public int[] gainLevel;
        }

        private int[] _gainLevel = new int[8];
        public event PropertyChangedEventHandler PropertyChanged;

        public GainLevel()
        {
            DeviceModulePos = 14;
        }


        public int[] Gain_Level
        {
            get { return _gainLevel; }
            set
            {
                _gainLevel = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("Gain_Level"));
                }
            }
        }

        public override Dictionary<int, byte[]> ParamsDataCollection
        {
            get
            {
                GainLevelParams levelParams = new GainLevelParams()
                {
                    gainLevel = Gain_Level
                };

                int size = Marshal.SizeOf(levelParams);
                byte[] arr = new byte[size];

                IntPtr ptr = IntPtr.Zero;
                try
                {
                    ptr = Marshal.AllocHGlobal(size);
                    Marshal.StructureToPtr(levelParams, ptr, false);
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
                if (value.ContainsKey(DeviceModulePos))
                {
                    byte[] arr = value[DeviceModulePos];
                    GainLevelParams levelParams = new GainLevelParams();

                    int size = Marshal.SizeOf(levelParams);
                    if (arr.Length != size)
                        throw new ArgumentException($"Invalid byte array size for GainLevelParams. Expected {size} bytes.");

                    IntPtr ptr = IntPtr.Zero;
                    try
                    {
                        ptr = Marshal.AllocHGlobal(size);
                        Marshal.Copy(arr, 0, ptr, size);
                        levelParams = (GainLevelParams)Marshal.PtrToStructure(ptr, typeof(GainLevelParams));
                    }
                    finally
                    {
                        if (ptr != IntPtr.Zero)
                            Marshal.FreeHGlobal(ptr);
                    }

                    Gain_Level = levelParams.gainLevel;
                    //Logger.Debug($"[GainLevel] Gain Level Values: [{string.Join(", ", Gain_Level)}]");
                }
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
            var xmlElement = xmlDoc.CreateElement("GainLevel");

            XmlElement gainLevelNode = xmlDoc.CreateElement("Gain_Level");
            string gainLevelStr = string.Join(",", Gain_Level.Select(x => x.ToString()).ToArray());
            gainLevelNode.AppendChild(xmlDoc.CreateTextNode(gainLevelStr));
            xmlElement.AppendChild(gainLevelNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
        {
            var gainLevelNode = ispToolDataNode["GainLevel"];

            var tmpGainLevelStr = XmlHelper.GetNodeValue(gainLevelNode, "Gain_Level");
            if (tmpGainLevelStr != null)
            {
                Gain_Level = tmpGainLevelStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();
            }
        }
    }
}
