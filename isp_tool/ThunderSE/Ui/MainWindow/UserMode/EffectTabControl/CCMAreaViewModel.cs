using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig.Isp;

namespace ThunderSE.Ui.MainWindow.UserMode.EffectTabControl
{
    class CCMAreaViewModel : ViewModelBase
    {
        private Processor _ispProcessor = null;
        private CCM _ccmStep = null;

        private Dictionary<string, short[]> _presetCcmData = new Dictionary<string, short[]>()
        {
            {"R", new short[] { 0x110, 0x08, -0x18, 0x00, 0x100, 0x00, 0x00, 0x00, 0x100 } },
            {"G", new short[] { 0x100, 0x00, 0x00, -0x08, 0x110, -0x08, 0x00, 0x00, 0x100 } },
            {"B", new short[] { 0x100, 0x00, 0x00, 0x00, 0x100, 0x00, -0x18, 0x08, 0x110 } },
            {"Y", new short[] { 0x110, 0x08, -0x18, -0x08, 0x110, -0x08, 0x00, 0x00, 0x100 } },
            {"C", new short[] { 0x100, 0x00, 0x00, -0x08, 0x110, -0x08, -0x18, 0x08, 0x110 } },
            {"M", new short[] { 0x110, 0x08, -0x18, 0x00, 0x100, 0x00, -0x18, 0x08, 0x110 } }
        };

        public CCMAreaViewModel(Processor ispProcessor)
        {
            _ispProcessor = ispProcessor;

            _ccmStep = (CCM)_ispProcessor.AllProcessSteps[IspModule.Ccm];
            _ccmStep.PropertyChanged += OnCCMConfigChange;

            _ispProcessor.IspCommonConfig.PropertyChanged += OnCommonConfigChange;
        }

        public bool IsCcmEnable
        {
            get { return _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Ccm].Value; }
            set
            {
                _ispProcessor.IspCommonConfig.ProcessorStepsEnables[(int)IspModule.Ccm]
                    = new KeyValuePair<IspModule, bool>(IspModule.Ccm, value);
            }
        }

        public int MinCcmValue
        {
            get { return -512; }
        }

        public int MaxCcmValue
        {
            get { return 511; }
        }

        public int[] ccm
        {
            get
            {
                return _ccmStep.ccm.Select(x => (int)x).ToArray();
            }
            set
            {
                _ccmStep.ccm = value.Select(x => (short)x).ToArray();
            }
        }

        public void SetPresetCcmData(string dataType)
        {
            _ccmStep.ccm = _presetCcmData[dataType];
        }

        private void OnCCMConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
        }

        private void OnCommonConfigChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsCcmEnable")
            {
                RaisePropertyChanged("IsCcmEnable");
            }
        }
    }
}
