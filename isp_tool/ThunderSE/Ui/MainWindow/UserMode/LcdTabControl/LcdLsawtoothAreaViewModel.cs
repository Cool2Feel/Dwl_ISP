using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.DeviceConfig.Lcd;

namespace ThunderSE.Ui.MainWindow.UserMode.LcdTabControl
{
    class LcdLsawtoothViewModel : ViewModelBase
    {
        private LcdLsawtooth _lcdLsawtoothSection = null;
        private int _smooth_level;

        public LcdLsawtoothViewModel(LcdLsawtooth LcdLsawtoothSection)
        {
            _smooth_level = 0;

            _lcdLsawtoothSection = LcdLsawtoothSection;
            _lcdLsawtoothSection.PropertyChanged += OnLcdLsawtoothPropertyChange;
        }

        void OnLcdLsawtoothPropertyChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
        }

        public int smooth_level
        {
            get { return _lcdLsawtoothSection.smooth_level; }
            set 
            {
                _smooth_level = value;
                _lcdLsawtoothSection.smooth_level = value;
            }
        }

        public int MaxSmoothLevel
        {
            get { return 7; }
        }
    }
}
