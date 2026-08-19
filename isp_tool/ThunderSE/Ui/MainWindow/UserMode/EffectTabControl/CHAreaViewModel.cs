using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig.Isp;

namespace ThunderSE.Ui.MainWindow.UserMode.EffectTabControl
{
    class CHAreaViewModel : ViewModelBase
    {
        private Processor _ispProcessor = null;
        private CH _chStep = null;

        public CHAreaViewModel(Processor ispProcessor)
        {
            _ispProcessor = ispProcessor;

            _chStep = (CH)_ispProcessor.AllProcessSteps[IspModule.Ch];
            _chStep.PropertyChanged += OnCHConfigChange;

            _ispProcessor.IspCommonConfig.PropertyChanged += OnCommonConfigChange;
        }

        public bool IsChEnable
        {
            get { return _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Ch].Value; }
            set
            {
                _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Ch]
                    = new KeyValuePair<IspModule, bool>(IspModule.Ch, value);
            }
        }

        public int MaxRateValue
        {
            get { return 31; }
        }

        public int r_rate_r
        {
            get
            {
                return _chStep.r_rate[0];
            }
            set
            {
                _chStep.SetRRate(0, value);
            }
        }

        public int r_rate_g
        {
            get
            {
                return _chStep.g_rate[1];
            }
            set
            {
                _chStep.SetGRate(1, value);
            }
        }

        public int r_rate_b
        {
            get
            {
                return _chStep.b_rate[2];
            }
            set
            {
                _chStep.SetBRate(2, value);
            }
        }

        public int r_rate_y
        {
            get
            {
                return _chStep.r_rate[3];
            }
            set
            {
                _chStep.SetRRate(3, value);
                _chStep.SetGRate(3, value);
            }
        }

        public int r_rate_c
        {
            get
            {
                return _chStep.g_rate[4];
            }
            set
            {
                _chStep.SetGRate(4, value);
                _chStep.SetBRate(4, value);
            }
        }

        public int r_rate_m
        {
            get
            {
                return _chStep.r_rate[5];
            }
            set
            {
                _chStep.SetRRate(5, value);
                _chStep.SetBRate(5, value);
            }
        }


        public int[] enhance
        {
            get { return _chStep.enhence; }
            set
            {
                _chStep.enhence = value;
            }
        }

        private void OnCHConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "r_rate":
                    RaisePropertyChanged("r_rate_r");
                    RaisePropertyChanged("r_rate_y");
                    RaisePropertyChanged("r_rate_m");
                    break;
                     
                case "g_rate":
                    RaisePropertyChanged("r_rate_g");
                    RaisePropertyChanged("r_rate_y");
                    RaisePropertyChanged("r_rate_c");
                    break;

                case "b_rate":
                    RaisePropertyChanged("r_rate_b");
                    RaisePropertyChanged("r_rate_c");
                    RaisePropertyChanged("r_rate_m");
                    break;

                default:
                    RaisePropertyChanged(e.PropertyName);
                    break;
            }
        }

        private void OnCommonConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsChEnable")
            {
                RaisePropertyChanged("IsChEnable");
            }
        }
    }
}
