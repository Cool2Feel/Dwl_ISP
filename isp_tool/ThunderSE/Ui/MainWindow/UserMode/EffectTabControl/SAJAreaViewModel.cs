using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig.Isp;

namespace ThunderSE.Ui.MainWindow.UserMode.EffectTabControl
{
    class SAJAreaViewModel : ViewModelBase
    {
        private Processor _ispProcessor = null;
        private SAJ _sajStep = null;

        public SAJAreaViewModel(Processor ispProcessor)
        {
            _ispProcessor = ispProcessor;

            _sajStep = (SAJ)_ispProcessor.AllProcessSteps[IspModule.Saj];
            _sajStep.PropertyChanged += OnSAJConfigChange;

            _ispProcessor.IspCommonConfig.PropertyChanged += OnCommonConfigChange;
        }

        ~SAJAreaViewModel()
        {
            _sajStep.PropertyChanged -= OnSAJConfigChange;
        }


        public byte MaxSatRateValue
        {
            get { return 16; }
        }

        public bool IsSajEnable
        {
            get { return _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Saj].Value; }
            set
            {
                _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Saj]
                    = new KeyValuePair<IspModule, bool>(IspModule.Saj, value);
            }
        }


        public int[] sat_rate
        {
            get
            {
                int[] tmpSatRate = new int[_sajStep.sat_rate.Length];
                for (int i = 0; i < _sajStep.sat_rate.Length; i++)
                {
                    tmpSatRate[i] = (byte)(MaxSatRateValue - _sajStep.sat_rate[i]);
                }

                return tmpSatRate;
            }
            set
            {
                var tmpValue = new byte[value.Length] ;
                for (int i = 0; i < tmpValue.Length; i++)
                {
                    tmpValue[i] = (byte)(MaxSatRateValue - value[i]);
                }

                _sajStep.sat_rate = tmpValue;
            }
        }

        private void OnSAJConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
        }

        private void OnCommonConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsSajEnable")
            {
                RaisePropertyChanged("IsSajEnable");
            }
        }
    }
}
