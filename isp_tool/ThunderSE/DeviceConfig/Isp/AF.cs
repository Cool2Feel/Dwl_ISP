using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using static ThunderSE.DeviceConfig.Isp.SAJ;

namespace ThunderSE.DeviceConfig.Isp
{
    public class AF : ProcessStep, INotifyPropertyChanged
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct AfParams
        {
            public int af_x0;
            public int af_x1;
            public int af_y0;
            public int af_y1;
            public byte af_frame_interval;

        };

        public int _af_x0;
        public int _af_x1;
        public int _af_y0;
        public int _af_y1;

        public byte _af_frame_interval;

        public event PropertyChangedEventHandler PropertyChanged;

        public AF()
        {
            DeviceModulePos = 14;
        }

        public int Af_x0
        {
            get { return _af_x0; }
            set
            {
                _af_x0 = value;
                 HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Af_x0");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int Af_x1
        {
            get { return _af_x1; }
            set
            {
                _af_x1 = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Af_x1");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int Af_y0
        {
            get { return _af_y0; }
            set
            {
                _af_y0 = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Af_y0");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public int Af_y1
        {
            get { return _af_y1; }
            set
            {
                _af_y1 = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Af_y1");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }


        public byte Af_frame_interval
        {
            get { return _af_frame_interval; }
            set
            {
                _af_frame_interval = value;
                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("Af_frame_interval");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public override Dictionary<int, byte[]> ParamsDataCollection 
        { 
            get
            {
                AfParams afParams = new AfParams()
                {
                    af_x0 = _af_x0,
                    af_x1 = _af_x1,
                    af_y0 = _af_y0,
                    af_y1 = _af_y1,
                    af_frame_interval = _af_frame_interval
                };

                int size = Marshal.SizeOf(afParams);
                byte[] arr = new byte[size];

                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(afParams, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                Marshal.FreeHGlobal(ptr);

                return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
            }
            set
            {
                AfParams afParams = new AfParams();

                int size = Marshal.SizeOf(afParams);
                IntPtr ptr = Marshal.AllocHGlobal(size);

                Marshal.Copy(value[DeviceModulePos], 0, ptr, size);

                afParams = (AfParams)Marshal.PtrToStructure(ptr, afParams.GetType());
                Marshal.FreeHGlobal(ptr);
                _af_x0 = afParams.af_x0;
                _af_x1 = afParams.af_x1;
                _af_y0 = afParams.af_y0;
                _af_y1 = afParams.af_y1;
                _af_frame_interval = afParams.af_frame_interval;
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
            throw new NotImplementedException();
        }

        public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
        {
            throw new NotImplementedException();
        }
    }
}
