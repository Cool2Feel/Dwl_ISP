using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig.Isp;

namespace ThunderSE.Ui.MainWindow.UserMode.EffectTabControl
{
    class EEAreaViewModel : ViewModelBase
    {
        private Processor _ispProcessor = null;
        private EE _eeStep = null;

        public EEAreaViewModel(Processor ispProcessor)
        {
            _ispProcessor = ispProcessor;

            _eeStep = (EE)_ispProcessor.AllProcessSteps[IspModule.Ee];
            _eeStep.PropertyChanged += OnEEConfigChange;

            _ispProcessor.IspCommonConfig.PropertyChanged += OnCommonConfigChange;
        }

        public bool IsEeEnable
        {
            get { return _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Ee].Value; }
            set
            {
                _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Ee]
                    = new KeyValuePair<IspModule, bool>(IspModule.Ee, value);
            }
        }

        public int MaxEEClassValue
        {
            get { return 15; }
        }

        public byte ee_class
        {
            get { return _eeStep.ee_class; }
            set
            {
                _eeStep.ee_class = value;
            }
        }

        private void OnEEConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
        }

        private void OnCommonConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsEeEnable")
            {
                RaisePropertyChanged("IsEeEnable");
            }
        }
    }
}
