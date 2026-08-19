using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace ThunderSE.DeviceConfig.Lcd
{
    class LcdLsawtooth : LcdSettingSection, INotifyPropertyChanged
    {
        private int _smooth_level = 0;
        private int[] _anti_lsawtooth = new int[3 * 24];

        private int _sensorWidth = 0;
        private int _sensorHeight = 0;

        private int _lcdWidth = 0;
        private int _lcdHeight = 0;

        private List<int[]> _presetAntiLsawtoothData = new List<int[]>();

        public int SensorWidth
        {
            get { return _sensorWidth; }
            set { _sensorWidth = value; }
        }

        public int SensorHeight
        {
            get { return _sensorHeight; }
            set { _sensorHeight = value; }
        }

        public int LcdWidth
        {
            get { return _lcdWidth; }
            set { _lcdWidth = value; }
        }

        public int LcdHeight
        {
            get { return _lcdHeight; }
            set { _lcdHeight = value; }
        }

        public void RefreshAntiLsawtoothPresetData()
        {
            _presetAntiLsawtoothData.Clear();
            for (int i = 0; i < 8; i++)
			{
                byte[] tmp8x8Table = new byte[64];
                byte[] tmp8x4Table = new byte[32];
                LcdApi.AntiSawtooth8(_sensorWidth, _lcdWidth, i, tmp8x8Table);
                LcdApi.AntiSawtooth4(_sensorHeight, _lcdHeight, i, tmp8x4Table);

                var tmpAntiLsawtooth = (int[])anti_lsawtooth.Clone();
                Buffer.BlockCopy(tmp8x8Table, 0, tmpAntiLsawtooth, 0, tmp8x8Table.Length);
                Buffer.BlockCopy(tmp8x4Table, 0, tmpAntiLsawtooth, tmp8x8Table.Length, tmp8x4Table.Length);

                Buffer.BlockCopy(tmpAntiLsawtooth, 0, tmpAntiLsawtooth, 24 * sizeof(int), 24 * sizeof(int));
                Buffer.BlockCopy(tmpAntiLsawtooth, 0, tmpAntiLsawtooth, 24 * sizeof(int) + 24 * sizeof(int), 24 * sizeof(int));

                _presetAntiLsawtoothData.Add(tmpAntiLsawtooth);
			}
        }

        public int[] anti_lsawtooth
        {
            get
            {
                return _anti_lsawtooth;
            }
            set
            {
                _anti_lsawtooth = value;
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("anti_lsawtooth"));
                }

                for (int i = 0; i < _presetAntiLsawtoothData.Count; i++)
                {

                    if (Enumerable.SequenceEqual(_anti_lsawtooth, _presetAntiLsawtoothData[i]))
                    {
                        _smooth_level = i;
                        if (PropertyChanged != null)
                        {
                            PropertyChanged(this, new PropertyChangedEventArgs("smooth_level"));
                        }
                        break;
                    }
                }
            }
        }

        public int smooth_level
        {
            get
            {
                return _smooth_level;
            }
            set
            {
                _smooth_level = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("smooth_level"));
                }

                _anti_lsawtooth = _presetAntiLsawtoothData[value];
                HasChangedParams = true;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("anti_lsawtooth"));
                }
            }
        }

        public override byte[] ParamsData
        {
            get
            {

                byte[] result = new byte[anti_lsawtooth.Length * sizeof(int)];
                Buffer.BlockCopy(anti_lsawtooth, 0, result, 0, result.Length);

                return result;
            }
            set
            {
                var tmpArray = new int[value.Length / sizeof(int)];
                Buffer.BlockCopy(value, 0, tmpArray, 0, value.Length);
                anti_lsawtooth = tmpArray;
            }
        }

        public override XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("LcdLsawtooth");

            XmlElement antiLsawtoothNode = xmlDoc.CreateElement("smooth_level");
            antiLsawtoothNode.AppendChild(xmlDoc.CreateTextNode(smooth_level.ToString()));
            xmlElement.AppendChild(antiLsawtoothNode);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(XmlElement LcdNode)
        {
            var LcdLsawtoothNode = LcdNode["LcdLsawtooth"];

            smooth_level = XmlHelper.GetNodeInt(LcdLsawtoothNode, "smooth_level");
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
