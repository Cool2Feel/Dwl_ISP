using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig.Isp;

namespace ThunderSE.Ui.MainWindow.UserMode.EffectTabControl
{
    class VDEAreaViewModel : ViewModelBase
    {
        private Processor _ispProcessor = null;
        private VDE _vdeStep = null;

        public VDEAreaViewModel(Processor ispProcessor)
        {
            _ispProcessor = ispProcessor;

            _vdeStep = (VDE)_ispProcessor.AllProcessSteps[IspModule.Vde];
            _vdeStep.PropertyChanged += OnVDEConfigChange;

            _ispProcessor.IspCommonConfig.PropertyChanged += OnCommonConfigChange;
        }


        public int MaxDataValue
        {
            get { return 255; }
        }

        public int MaxSatRateValue
        {
            get { return 32; }
        }

        public bool IsVdeEnable
        {
            get { return _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Vde].Value; }
            set 
            {
                _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Vde]
                    = new KeyValuePair<IspModule, bool>(IspModule.Vde, value);
            }
        }

        public int[] sat_rate
        {
            get 
            {
                return _vdeStep.sat_rate;
            }
            set
            {
                _vdeStep.sat_rate = value;
            }
        }

        public int contra
        {
            get
            {
                return _vdeStep.contra;
            }
            set
            {
                _vdeStep.contra = value;
            }
        }

        public int bright_k
        {
            get
            {
                return _vdeStep.bright_k;
            }
            set
            {
                _vdeStep.bright_k = value;
            }
        }

        public int bright_oft
        {
            get
            {
                return _vdeStep.bright_oft;
            }
            set
            {
                _vdeStep.bright_oft = value;
            }
        }

        public int hue
        {
            get
            {
                return _vdeStep.hue;
            }
            set
            {
                _vdeStep.hue = value;
            }
        }

        private void OnVDEConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
        }

        private void OnCommonConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsVdeEnable")
            {
                RaisePropertyChanged("IsVdeEnable");
            }
        }
    }
}
