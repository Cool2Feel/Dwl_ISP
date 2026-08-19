using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThunderSE.DeviceConfig;
using ThunderSE.DeviceConfig.Isp;

namespace ThunderSE.Ui.MainWindow.UserMode
{
    class CommonTabViewModel : ViewModelBase
    {
        private CommonConfig _commonConfig = null;

        public CommonTabViewModel(CommonConfig commonConfig)
        {
            _commonConfig = commonConfig;
            _commonConfig.PropertyChanged += OnPropertyChanged;
        }

        void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(e.PropertyName);
        }

        public string Name
        {
            get { return _commonConfig.Name; }
            set { _commonConfig.Name = value; }
        }

        public int Id
        {
            get { return _commonConfig.Id; }
            set { _commonConfig.Id = value; }
        }

        public int ResolutionWidth
        {
            get { return _commonConfig.ResolutionWidth; }
            set { _commonConfig.ResolutionWidth = value; }
        }

        public int ResolutionHeight
        {
            get { return _commonConfig.ResolutionHeight; }
            set { _commonConfig.ResolutionHeight = value; }
        }

        public int IsPclkFirEn
        {
            get { return _commonConfig.IsPclkFirEn; }
            set { _commonConfig.IsPclkFirEn = value; }
        }

        public bool IsPclkInvEn
        {
            get { return _commonConfig.IsPclkInvEn; }
            set { _commonConfig.IsPclkInvEn = value; }
        }

        public byte PclkFirClass
        {
            get { return _commonConfig.PclkFirClass; }
            set { _commonConfig.PclkFirClass = value; }
        }

        public byte Fps
        {
            get { return _commonConfig.Fps; }
            set { _commonConfig.Fps = value; }
        }

        public byte CsiTun
        {
            get { return _commonConfig.CsiTun; }
            set { _commonConfig.CsiTun = value; }
        }
    }
}
