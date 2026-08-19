using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.DeviceConfig.Lcd;

namespace ThunderSE.Ui.MainWindow.UserMode.LcdTabControl
{
    class LcdSajAreaViewModel : ViewModelBase
    {
        private LcdSaj _lcdSajSection = null;

        public LcdSajAreaViewModel(LcdSaj LcdSajSection)
        {
            _lcdSajSection = LcdSajSection;
            _lcdSajSection.PropertyChanged += OnLcdSajPropertyChange;
        }

        void OnLcdSajPropertyChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
        }

        public byte MaxSajValue
        {
            get { return 255; }
        }

        public int[] de_saj
        {
            get
            {
                return _lcdSajSection.de_saj;
            }
            set
            {
                _lcdSajSection.de_saj = value;
            }
        }
    }
}
