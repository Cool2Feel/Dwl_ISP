using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig.Isp;

namespace ThunderSE.Ui.MainWindow.UserMode.EffectTabControl
{
    class DDCAreaViewModel : ViewModelBase
    {
        private Processor _ispProcessor = null;
        private DDC _ddcStep = null;

        public DDCAreaViewModel(Processor ispProcessor)
        {
            _ispProcessor = ispProcessor;

            _ddcStep = (DDC)_ispProcessor.AllProcessSteps[IspModule.Ddc];
            _ddcStep.PropertyChanged += OnDDCConfigChange;

            _ispProcessor.IspCommonConfig.PropertyChanged += OnCommonConfigChange;
        }

        public bool IsDdcEnable
        {
            get { return _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Ddc].Value; }
            set
            {
                _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Ddc]
                    = new KeyValuePair<IspModule, bool>(IspModule.Ddc, value);
            }
        }

        public int MaxDdcClassValue
        {
            get { return 7; }
        }

        public int MaxIndxAdaptValue
        {
            get { return 14; }
        }

        //public int ddc_class
        //{
        //    get { return _ddcStep.ddc_class; }
        //    set
        //    {
        //        _ddcStep.ddc_class = value;
        //    }
        //}


        public int[] indx_adapt
        {
            get 
            {
                int[] tmpIndxAdapt = new int[_ddcStep.indx_adapt.Length];
                for (int i = 0; i < _ddcStep.indx_adapt.Length; i++)
                {
                    tmpIndxAdapt[i] = _ddcStep.indx_adapt[i] + 7;
                }

                return tmpIndxAdapt; 
            }
            set
            {
                var tmpValue = new int[_ddcStep.indx_adapt.Length];
                for (int i = 0; i < tmpValue.Length; i++)
                {
                    tmpValue[i] = value[i] - 7;
                }
                _ddcStep.indx_adapt = tmpValue;
            }
        }

        private void OnDDCConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
        }

        private void OnCommonConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsDdcEnable")
            {
                RaisePropertyChanged("IsDdcEnable");
            }
        }
    }
}
