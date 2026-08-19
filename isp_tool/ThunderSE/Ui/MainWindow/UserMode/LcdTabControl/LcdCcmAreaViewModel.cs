using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.DeviceConfig.Lcd;

namespace ThunderSE.Ui.MainWindow.UserMode.LcdTabControl
{
    class LcdCcmAreaViewModel : ViewModelBase
    {
        private LcdCcm _lcdCcmSection = null;
        private Dictionary<string, int[]> _presetCcmData = new Dictionary<string, int[]>()
        {
            {"R", new int[] { 0x110, 0x08, -0x18, 0x00, 0x100, 0x00, 0x00, 0x00, 0x100 } },
            {"G", new int[] { 0x100, 0x00, 0x00, -0x08, 0x110, -0x08, 0x00, 0x00, 0x100 } },
            {"B", new int[] { 0x100, 0x00, 0x00, 0x00, 0x100, 0x00, -0x18, 0x08, 0x110 } },
            {"Y", new int[] { 0x110, 0x08, -0x18, -0x08, 0x110, -0x08, 0x00, 0x00, 0x100 } },
            {"C", new int[] { 0x100, 0x00, 0x00, -0x08, 0x110, -0x08, -0x18, 0x08, 0x110 } },
            {"M", new int[] { 0x110, 0x08, -0x18, 0x00, 0x100, 0x00, -0x18, 0x08, 0x110 } }
        };

        public LcdCcmAreaViewModel(LcdCcm LcdCcmSection)
        {
            _lcdCcmSection = LcdCcmSection;
            _lcdCcmSection.PropertyChanged += OnLcdCcmPropertyChange;
        }

        void OnLcdCcmPropertyChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
        }

        public int[] de_ccm
        {
            get
            {
                return _lcdCcmSection.de_ccm;
            }
            set
            {
                _lcdCcmSection.de_ccm = value;
            }
        }

        public int MaxValue0to8
        {
            get { return 511; }
        }

        public int MinValue0to8
        {
            get { return -512; }
        }

        public int MaxValue9to11
        {
            get { return 15; }
        }

        public int MinValue9to11
        {
            get { return -16; }
        }

        public void SetPresetCcmData(string dataType)
        {
            var tmpArray = _lcdCcmSection.de_ccm;
            for (int i = 0; i < _presetCcmData[dataType].Length; i++)
            {
                tmpArray[i] = _presetCcmData[dataType][i];
            }

            _lcdCcmSection.de_ccm = tmpArray;
        }
    }
}
