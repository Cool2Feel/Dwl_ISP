using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.DeviceConfig.Lcd;

namespace ThunderSE.Ui.MainWindow.UserMode.LcdTabControl
{
    class LcdVdeAreaViewModel : ViewModelBase
    {
        private LcdVde _lcdVdeSection = null;

        public LcdVdeAreaViewModel(LcdVde LcdVdeSection)
        {
            _lcdVdeSection = LcdVdeSection;
            _lcdVdeSection.PropertyChanged += OnLcdVdePropertyChange;
        }

        void OnLcdVdePropertyChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
        }

        public int brightness
        {
            get { return _lcdVdeSection.brightness; }
            set { _lcdVdeSection.brightness = value; }
        }
        public int saturation
        {
            get { return _lcdVdeSection.saturation; }
            set { _lcdVdeSection.saturation = value; }
        }

        public int contrast
        {
            get { return _lcdVdeSection.contrast; }
            set { _lcdVdeSection.contrast = value; }
        }

        public int BrightnessMaxValue
        {
            get { return 127; }
        }

        public int BrightnessMinValue
        {
            get { return -128; }
        }

        public int SaturationAndContrastMaxValue
        {
            get { return 14; }
        }

        public int SaturationAndContrastMinValue
        {
            get { return 0; }
        }

    }
}
