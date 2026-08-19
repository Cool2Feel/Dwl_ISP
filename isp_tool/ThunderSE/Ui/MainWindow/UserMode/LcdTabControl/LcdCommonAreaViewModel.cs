using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.DeviceConfig.Lcd;

namespace ThunderSE.Ui.MainWindow.UserMode.LcdTabControl
{
    class LcdCommonAreaViewModel : ViewModelBase
    {
        private LcdCommon _lcdCommonSection = null;

        public LcdCommonAreaViewModel(LcdCommon LcdCommonSection)
        {
            _lcdCommonSection = LcdCommonSection;
            _lcdCommonSection.PropertyChanged += OnLcdCommonPropertyChange;
        }

        void OnLcdCommonPropertyChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
        }

        public string name
        {
            get { return _lcdCommonSection.name; }
        }

        public int screen_w
        {
            get { return _lcdCommonSection.screen_w; }
        }

        public int screen_h
        {
            get { return _lcdCommonSection.screen_h; }
        }
    }
}
