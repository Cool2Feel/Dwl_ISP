using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Common;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;

namespace ThunderSE.Ui.SettingWindow.IspSteps
{
    class IspStepsWindowViewModel : ViewModelBase
    {
        private RelayCommand<DeviceConfig.Isp.IspModule> _navigateToModuleCommand;
        private RelayCommand<bool> _setAllEnablesCommand;

        private Processor _ispProcessor = null;
        private DeviceConfig.Isp.IspModule? _currentSettingModule = null;

        private ObservableCollection<KeyValuePair<IspModule, bool>> _ispProccesStepsEnables = null;

        public IspStepsWindowViewModel(Processor processor)
        {
            _ispProcessor = processor;
            _ispProccesStepsEnables = processor.IspCommonConfig.ProcessorStepsEnables;
            _ispProccesStepsEnables.CollectionChanged += StepsEnablesCollectionChanged;

            _navigateToModuleCommand = new RelayCommand<DeviceConfig.Isp.IspModule>(NavigateToModule);
            _setAllEnablesCommand = new RelayCommand<bool>(SetAllEnables);
        }

        public IspStepsWindowViewModel(Processor processor, DeviceConfig.Isp.IspModule currentSettingModule)
        {
            _ispProcessor = processor;
            _ispProccesStepsEnables = _ispProcessor.AllProcessSteps[currentSettingModule].PreviousStepsEnables;
            _ispProccesStepsEnables.CollectionChanged += StepsEnablesCollectionChanged;
            _currentSettingModule = currentSettingModule;

            _navigateToModuleCommand = new RelayCommand<DeviceConfig.Isp.IspModule>(NavigateToModule);
            _setAllEnablesCommand = new RelayCommand<bool>(SetAllEnables);
        }

        private void StepsEnablesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RaisePropertyChanged("IsBlcSelected");
            RaisePropertyChanged("IsLscSelected");
            RaisePropertyChanged("IsDdcSelected");
            RaisePropertyChanged("IsAwbSelected");
            RaisePropertyChanged("IsCcmSelected");
            RaisePropertyChanged("IsDgainSelected");
            RaisePropertyChanged("IsYGammaSelected");
            RaisePropertyChanged("IsRgbGammaSelected");
            RaisePropertyChanged("IsChSelected");
            RaisePropertyChanged("IsVdeSelected");
            RaisePropertyChanged("IsEeSelected");
            RaisePropertyChanged("IsCfdSelected");
            RaisePropertyChanged("IsSajSelected");
        }

        public DeviceConfig.Isp.IspModule? CurrentSettingModule
        {
            get { return _currentSettingModule; }
            set
            {
                _currentSettingModule = value;
                RaisePropertyChanged("CurrentSettingModule");

                RaisePropertyChanged("IsBlcSelected");
                RaisePropertyChanged("IsLscSelected");
                RaisePropertyChanged("IsDdcSelected");
                RaisePropertyChanged("IsAwbSelected");
                RaisePropertyChanged("IsCcmSelected");
                RaisePropertyChanged("IsDgainSelected");
                RaisePropertyChanged("IsYGammaSelected");
                RaisePropertyChanged("IsRgbGammaSelected");
                RaisePropertyChanged("IsChSelected");
                RaisePropertyChanged("IsVdeSelected");
                RaisePropertyChanged("IsEeSelected");
                RaisePropertyChanged("IsCfdSelected");
                RaisePropertyChanged("IsSajSelected");
            }
        }

        public void SetStepEnable(IspModule ispStep, bool isEnable)
        {
            Logger.Debug($"[IspStepsWindowViewModel] SetStepEnable - Step: {ispStep}, Enable: {isEnable}, Module: {CurrentSettingModule}");

            if (_ispProccesStepsEnables == null)
            {
                Logger.Warn($"[IspStepsWindowViewModel] SetStepEnable - _ispProccesStepsEnables is null, cannot set {ispStep}");
                return;
            }

            int stepPos = _ispProccesStepsEnables.IndexOf(_ispProccesStepsEnables.First(item => item.Key == ispStep));

            if (stepPos >= 0)
            {
                _ispProccesStepsEnables[stepPos] = new KeyValuePair<IspModule, bool>(ispStep, isEnable);
                Logger.Debug($"[IspStepsWindowViewModel] SetStepEnable - Successfully updated {ispStep} at position {stepPos} to {isEnable}");
            }
            else
            {
                Logger.Warn($"[IspStepsWindowViewModel] SetStepEnable - Step {ispStep} not found in _ispProccesStepsEnables");
            }
        }

        public bool GetStepEnable(IspModule ispStep)
        {
            if (_ispProccesStepsEnables == null)
            {
                return false;
            }

            int previousStepPos = _ispProccesStepsEnables.IndexOf(_ispProccesStepsEnables.First(item => item.Key == ispStep));

            if (previousStepPos >= 0)
            {
                return _ispProccesStepsEnables[previousStepPos].Value;
            }
            return false;
        }

        public char GetModuleActualStatus(IspModule ispStep)
        {
            if (_ispProcessor == null || _ispProcessor.IspCommonConfig == null)
            {
                return (char)0x00;
            }

            bool isSelected = GetStepEnable(ispStep);

            if (isSelected == false)
            {
                return (char)0x00;
            }

            if (_ispProcessor.IspCommonConfig.ProcessorStepsEnablesActualValueMap.TryGetValue(ispStep, out char actualValue))
            {
                if (actualValue == 0)
                {
                    actualValue = (char)0x01;
                }
                return actualValue;
            }

            return (char)0x00;
        }

        #region UI Bindings
        public bool IsBlcSelected
        {
            get { return GetStepEnable(IspModule.Blc); }
            set { SetStepEnable(IspModule.Blc, value); RaisePropertyChanged("BlcStatus"); }
        }

        public char BlcStatus
        {
            get
            {
                if (IsBlcSelected)
                {
                    return GetModuleActualStatus(IspModule.Blc);
                }
                return (char)0x00;
            }
        }

        public bool IsLscSelected
        {
            get { return GetStepEnable(IspModule.Lsc); }
            set { SetStepEnable(IspModule.Lsc, value); RaisePropertyChanged("LscStatus"); }
        }

        public char LscStatus
        {
            get
            {
                if (IsLscSelected)
                {
                    return GetModuleActualStatus(IspModule.Lsc);
                }
                return (char)0x00;
            }
        }

        public bool IsDdcSelected
        {
            get { return GetStepEnable(IspModule.Ddc); }
            set { SetStepEnable(IspModule.Ddc, value); RaisePropertyChanged("DdcStatus"); }
        }

        public char DdcStatus
        {
            get
            {
                if (IsDdcSelected)
                    return GetModuleActualStatus(IspModule.Ddc);
                return (char)0x00;
            }
        }

        public bool IsAwbSelected
        {
            get { return GetStepEnable(IspModule.Awb); }
            set { SetStepEnable(IspModule.Awb, value); RaisePropertyChanged("AwbStatus"); }
        }

        public char AwbStatus
        {
            get
            {
                if (IsAwbSelected)
                    return GetModuleActualStatus(IspModule.Awb);
                return (char)0x00;
            }
        }

        public bool IsCcmSelected
        {
            get
            {
                try
                {
                    _ispProccesStepsEnables.First(item => item.Key == IspModule.Ccm);
                }
                catch
                {
                    return false;
                }
                return GetStepEnable(IspModule.Ccm);
            }
            set { SetStepEnable(IspModule.Ccm, value); RaisePropertyChanged("CcmStatus"); }
        }

        public char CcmStatus
        {
            get
            {
                if (IsCcmSelected)
                    return GetModuleActualStatus(IspModule.Ccm);
                return (char)0x00;
            }
        }

        public bool IsDgainSelected
        {
            get
            {
                try
                {
                    _ispProccesStepsEnables.First(item => item.Key == IspModule.Dgain);
                }
                catch
                {
                    return false;
                }
                return GetStepEnable(IspModule.Dgain);
            }
            set { SetStepEnable(IspModule.Dgain, value); RaisePropertyChanged("DgainStatus"); }
        }

        public char DgainStatus
        {
            get
            {
                if (IsDgainSelected)
                    return GetModuleActualStatus(IspModule.Dgain);
                return (char)0x00;
            }
        }

        public bool IsYGammaSelected
        {
            get { return GetStepEnable(IspModule.YGamma); }
            set { SetStepEnable(IspModule.YGamma, value); RaisePropertyChanged("YGammaStatus"); }
        }

        public char YGammaStatus
        {
            get
            {
                if (IsYGammaSelected)
                    return GetModuleActualStatus(IspModule.YGamma);
                return (char)0x00;
            }
        }

        public bool IsRgbGammaSelected
        {
            get
            {
                try
                {
                    _ispProccesStepsEnables.First(item => item.Key == IspModule.RgbGamma);
                }
                catch
                {
                    return false;
                }
                return GetStepEnable(IspModule.RgbGamma);
            }
            set { SetStepEnable(IspModule.RgbGamma, value); RaisePropertyChanged("RgbGammaStatus"); }
        }

        public char RgbGammaStatus
        {
            get
            {
                if (IsRgbGammaSelected)
                    return GetModuleActualStatus(IspModule.RgbGamma);
                return (char)0x00;
            }
        }

        public bool IsChSelected
        {
            get
            {
                try
                {
                    _ispProccesStepsEnables.First(item => item.Key == IspModule.Ch);
                }
                catch
                {
                    return false;
                }
                return GetStepEnable(IspModule.Ch);
            }
            set { SetStepEnable(IspModule.Ch, value); RaisePropertyChanged("ChStatus"); }
        }

        public char ChStatus
        {
            get
            {
                if (IsChSelected)
                    return GetModuleActualStatus(IspModule.Ch);
                return (char)0x00;
            }
        }

        public bool IsVdeSelected
        {
            get
            {
                try
                {
                    _ispProccesStepsEnables.First(item => item.Key == IspModule.Vde);
                }
                catch
                {
                    return false;
                }
                return GetStepEnable(IspModule.Vde);
            }
            set { SetStepEnable(IspModule.Vde, value); RaisePropertyChanged("VdeStatus"); }
        }

        public char VdeStatus
        {
            get
            {
                if (IsVdeSelected)
                    return GetModuleActualStatus(IspModule.Vde);
                return (char)0x00;
            }
        }

        public bool IsEeSelected
        {
            get
            {
                try
                {
                    _ispProccesStepsEnables.First(item => item.Key == IspModule.Ee);
                }
                catch
                {
                    return false;
                }
                return GetStepEnable(IspModule.Ee);
            }
            set { SetStepEnable(IspModule.Ee, value); RaisePropertyChanged("EeStatus"); }
        }

        public char EeStatus
        {
            get
            {
                if (IsEeSelected)
                    return GetModuleActualStatus(IspModule.Ee);
                return (char)0x00;
            }
        }

        public bool IsCfdSelected
        {
            get
            {
                try
                {
                    _ispProccesStepsEnables.First(item => item.Key == IspModule.Cfd);
                }
                catch
                {
                    return false;
                }
                return GetStepEnable(IspModule.Cfd);
            }
            set { SetStepEnable(IspModule.Cfd, value); RaisePropertyChanged("CfdStatus"); }
        }

        public char CfdStatus
        {
            get
            {
                if (IsCfdSelected)
                    return GetModuleActualStatus(IspModule.Cfd);
                return (char)0x00;
            }
        }

        public bool IsSajSelected
        {
            get
            {
                try
                {
                    _ispProccesStepsEnables.First(item => item.Key == IspModule.Saj);
                }
                catch
                {
                    return false;
                }
                return GetStepEnable(IspModule.Saj);
            }
            set { SetStepEnable(IspModule.Saj, value); RaisePropertyChanged("SajStatus"); }
        }

        public char SajStatus
        {
            get
            {
                if (IsSajSelected)
                    return GetModuleActualStatus(IspModule.Saj);
                return (char)0x00;
            }
        }

        #endregion

        public RelayCommand<DeviceConfig.Isp.IspModule> NavigateToModuleCommand
        {
            get { return _navigateToModuleCommand; }
        }

        public RelayCommand<bool> SetAllEnablesCommand
        {
            get { return _setAllEnablesCommand; }
        }

        private void NavigateToModule(DeviceConfig.Isp.IspModule module)
        {
            CurrentSettingModule = module;
        }

        private async void SetAllEnables(bool isEnable)
        {
            _ = Task.Run(() =>
            {
                foreach (IspModule item in Enum.GetValues(typeof(IspModule)))
                {
                    if (item == IspModule.AE)
                    {
                        continue;
                    }
                    if (GetStepEnable(item) == isEnable)
                    {
                        continue;
                    }
                    if (item == IspModule.Blc) IsBlcSelected = isEnable;
                    else if (item == IspModule.Lsc) IsLscSelected = isEnable;
                    else if (item == IspModule.Ddc) IsDdcSelected = isEnable;
                    else if (item == IspModule.Awb) IsAwbSelected = isEnable;
                    else if (item == IspModule.Ccm) IsCcmSelected = isEnable;
                    else if (item == IspModule.Dgain) IsDgainSelected = isEnable;
                    else if (item == IspModule.YGamma) IsYGammaSelected = isEnable;
                    else if (item == IspModule.RgbGamma) IsRgbGammaSelected = isEnable;
                    else if (item == IspModule.Ch) IsChSelected = isEnable;
                    else if (item == IspModule.Vde) IsVdeSelected = isEnable;
                    else if (item == IspModule.Ee) IsEeSelected = isEnable;
                    else if (item == IspModule.Cfd) IsCfdSelected = isEnable;
                    else if (item == IspModule.Saj) IsSajSelected = isEnable;

                    Thread.Sleep(100);
                }
            });
        }
    }
}
