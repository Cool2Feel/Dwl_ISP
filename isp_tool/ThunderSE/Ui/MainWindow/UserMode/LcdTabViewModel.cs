using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.DeviceConfig.Lcd;

namespace ThunderSE.Ui.MainWindow.UserMode
{
    class LcdTabViewModel : ViewModelBase
    {
        public LcdTabViewModel(LcdSetting lcdSetting)
        {
            LcdSetting = lcdSetting;
        }

        public LcdSetting LcdSetting
        {
            get;
            set;
        }
    }
}
