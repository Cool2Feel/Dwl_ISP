using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig;
using ThunderSE.DeviceConfig.Isp;

namespace ThunderSE.Ui.MainWindow.UserMode
{
    class EffectTabViewModel : ViewModelBase
    {
        public EffectTabViewModel(Processor ispProcessor)
        {
            IspProcessor = ispProcessor;
        }

        public Processor IspProcessor
        {
            get;
            set;
        }
    }
}
