using GalaSoft.MvvmLight;
using ThunderSE.DeviceConfig;

namespace ThunderSE.Ui.MainWindow
{
    class ExpGainWindowViewModel : ViewModelBase
    {
        public ExpGainWindowViewModel(Config deviceConfig)
        {
            DeviceConfig = deviceConfig;
        }

        public Config DeviceConfig
        {
            get;
            set;
        }

        public int ExpGain
        {
            get { return DeviceConfig.IspProcessor.IspCommonConfig.ExpGain; }
            set { DeviceConfig.IspProcessor.IspCommonConfig.ExpGain = value; }
        }

        //public int GainMax
        //{
        //    get { return DeviceConfig.IspProcessor.IspCommonConfig.GainMax; }
        //    set { DeviceConfig.IspProcessor.IspCommonConfig.GainMax = value; }
        //}
        public int TurnGain
        {
            get { return DeviceConfig.IspProcessor.IspCommonConfig.TurnGain; }
            set { DeviceConfig.IspProcessor.IspCommonConfig.TurnGain = value; }
        }

        public int TurnExp
        {
            get { return DeviceConfig.IspProcessor.IspCommonConfig.TurnExp; }
            set { DeviceConfig.IspProcessor.IspCommonConfig.TurnExp = value; }
        }
    }
}
