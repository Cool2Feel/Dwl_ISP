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
    class AEAreaViewModel : ViewModelBase
    {
        private Processor _ispProcessor = null;
        private AE _aeStep = null;

        public AEAreaViewModel(Processor ispProcessor)
        {
            _ispProcessor = ispProcessor;

            _aeStep = (AE)_ispProcessor.AllProcessSteps[IspModule.AE];
            _aeStep.PropertyChanged += OnAEConfigChange;
        }

        ~AEAreaViewModel()
        {
            _aeStep.PropertyChanged -= OnAEConfigChange;
        }


        public int MaxExpTagValue
        {
            get { return 255; }
        }

        public byte[] ExpTag
        {
            get 
            {
                return ExpAdapt.exp_tag; 
            }
            set 
            {
                ExpAdapt.exp_tag = value; 
            }
        }


        public EXP ExpAdapt
        {
            get { return _aeStep.ExpAdapt; }
            set { _aeStep.ExpAdapt = value; }
        }

        public HGRM HgrmAdapt
        {
            get { return _aeStep.HgrmAdapt; }
            set { _aeStep.HgrmAdapt = value; }
        }

        private void OnAEConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ExpAdapt.exp_tag" || e.PropertyName == "ExpAdapt")
            {
                RaisePropertyChanged("ExpTag");
            }
            else
            {
                RaisePropertyChanged(e.PropertyName);
            }
        }
    }
}
