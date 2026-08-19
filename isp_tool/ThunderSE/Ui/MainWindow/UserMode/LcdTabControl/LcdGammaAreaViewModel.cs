using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.DeviceConfig.Lcd;

namespace ThunderSE.Ui.MainWindow.UserMode.LcdTabControl
{
    class LcdGammaAreaViewModel : ViewModelBase
    {
        private LcdGamma _lcdGammaSection = null;

        public LcdGammaAreaViewModel(LcdGamma LcdGammaSection)
        {
            _lcdGammaSection = LcdGammaSection;
            _lcdGammaSection.PropertyChanged += OnLcdVdePropertyChange;
        }

        void OnLcdVdePropertyChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
        }

        public int contra_index
        {
            get { return _lcdGammaSection.contra_index; }
            set { _lcdGammaSection.contra_index = value; }
        }
        public int gamma_red
        {
            get { return _lcdGammaSection.gamma_red; }
            set { _lcdGammaSection.gamma_red = value; }
        }

        public int gamma_green
        {
            get { return _lcdGammaSection.gamma_green; }
            set { _lcdGammaSection.gamma_green = value; }
        }

        public int gamma_blue
        {
            get { return _lcdGammaSection.gamma_blue; }
            set { _lcdGammaSection.gamma_blue = value; }
        }

        public int ContraIndexMaxValue
        {
            get { return 12; }
        }

        public int ContraIndexMinValue
        {
            get { return 0; }
        }

        public int GammaMaxValue
        {
            get { return 11; }
        }

        public int GammaMinValue
        {
            get { return 0; }
        }

    }
}
