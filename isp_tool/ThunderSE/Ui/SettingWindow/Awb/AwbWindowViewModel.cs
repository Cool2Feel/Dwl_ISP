using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ThunderSE.DeviceConfig.Isp;

namespace ThunderSE.Ui.SettingWindow.Awb
{
    using System.Drawing;
    using ThunderSE.Ui.SettingWindow.IspSteps;
    using WhiteBalanceStatCollection = ObservableCollection<KeyValuePair<double, double>>;

    // 新增：增益数据系列类
    public class GainDataSeries : ViewModelBase
    {
        private string _categoryName;
        private Brush _categoryColor;
        private ObservableCollection<KeyValuePair<int, int>> _dataPoints = new ObservableCollection<KeyValuePair<int, int>>();

        public string CategoryName
        {
            get { return _categoryName; }
            set
            {
                _categoryName = value;
                RaisePropertyChanged("CategoryName");
            }
        }

        public Brush CategoryColor
        {
            get { return _categoryColor; }
            set
            {
                _categoryColor = value;
                RaisePropertyChanged("CategoryColor");
            }
        }

        public ObservableCollection<KeyValuePair<int, int>> DataPoints
        {
            get { return _dataPoints; }
            set
            {
                _dataPoints = value;
                RaisePropertyChanged("DataPoints");
            }
        }
    }

    class AwbWindowViewModel : ViewModelBase
    {
        private IspStepsWindow _ispStepsWindow;

        private RelayCommand _loadRawFileCommand;

        private RelayCommand _loadChartDataFileCommand;
        private RelayCommand _saveChartDataFileCommand;

        private RelayCommand _viewIQCommand;
        private RelayCommand _updateStatTabCommand;

        private RelayCommand _viewPreviousIspStep;

        // 智能插值相关命令
        private RelayCommand _toggleSmartModeCommand;
        private RelayCommand _generateCurveFromKeyPointsCommand;
        private RelayCommand _initializeDefaultKeyPointsCommand;
        private RelayCommand _applySmartInterpolationCommand;

        private Processor _ispProcessor = null;
        private AutoWhiteBalance _awb = null;
        //private List<ObservableCollection<KeyValuePair<double, double>>> _correctionDataList;
        // 新增：按类别分组的增益数据系列集合
        private ObservableCollection<GainDataSeries> _gainDataSeriesCollection = new ObservableCollection<GainDataSeries>();

        public bool IsLoadFile = false;

        public AwbWindowViewModel(Processor ispProcessor)
        {
            _ispProcessor = ispProcessor;
            _loadRawFileCommand = new RelayCommand(LoadRawFiles);

            _loadChartDataFileCommand = new RelayCommand(LoadChartData);
            _saveChartDataFileCommand = new RelayCommand(SaveChartDataFile);

            _viewIQCommand = new RelayCommand(ViewIQ);
            _updateStatTabCommand = new RelayCommand(UpdateStatTab);

            _viewPreviousIspStep = new RelayCommand(ViewPreviousIspStep);

            // 初始化智能插值命令
            _toggleSmartModeCommand = new RelayCommand(ToggleSmartMode);
            _generateCurveFromKeyPointsCommand = new RelayCommand(GenerateCurveFromKeyPoints);
            _initializeDefaultKeyPointsCommand = new RelayCommand(InitializeDefaultKeyPoints);
            _applySmartInterpolationCommand = new RelayCommand(ApplySmartInterpolation);

            _awb = (AutoWhiteBalance)ispProcessor.AllProcessSteps[IspModule.Awb];
            _awb.SetPreviousStep(ispProcessor);
            _awb.StatisticData.Clear();
            _awb.PropertyChanged += OnDataChanged;

            // 默认启用智能模式并初始化关键点
            //if (_awb.SmartInterpolationEnabled)
            //{
            //    InitializeDefaultKeyPointsForAllCurves();
            //}
        }

        private void ViewPreviousIspStep()
        {
            if (_ispStepsWindow != null)
            {
                _ispStepsWindow.Show();
                _ispStepsWindow.Activate();
            }
            else
            {
                _ispStepsWindow = new IspStepsWindow();
                _ispStepsWindow.DataContext = new IspStepsWindowViewModel(_ispProcessor, DeviceConfig.Isp.IspModule.Awb);
                _ispStepsWindow.Show();
            }
        }

        private void OnDataChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "RGainStart")
            {
                RaisePropertyChanged("RGainEnd");
            }
            else
                RaisePropertyChanged(e.PropertyName);
        }

        public RelayCommand LoadRawFileCommand
        {
            get { return _loadRawFileCommand; }
        }
        public RelayCommand LoadChartDataFileCommand
        {
            get { return _loadChartDataFileCommand; }
        }

        public RelayCommand SaveChartDataFileCommand
        {
            get { return _saveChartDataFileCommand; }
        }

        public RelayCommand ViewIQCommand
        {
            get { return _viewIQCommand; }
        }

        public RelayCommand UpdateStatTabCommand
        {
            get { return _updateStatTabCommand; }
        }

        public RelayCommand ViewPreviousIspStepCommand
        {
            get { return _viewPreviousIspStep; }
        }

        #region Awb参数绑定属性
        public int Awb_De_High_Red_Class
        {
            get { return _awb.Awb_De_High_Red_Class; }
            set
            {
                _awb.Awb_De_High_Red_Class = value;
                RaisePropertyChanged("Awb_De_High_Red_Class");
            }
        }

        public int Awb_De_High_Blue_Class
        {
            get { return _awb.Awb_De_High_Blue_Class; }
            set
            {
                _awb.Awb_De_High_Blue_Class = value;
                RaisePropertyChanged("Awb_De_High_Blue_Class");
            }
        }

        public int Awb_De_High_Red_Rate
        {
            get { return _awb.Awb_De_High_Red_Rate; }
            set
            {
                _awb.Awb_De_High_Red_Rate = value;
                RaisePropertyChanged("Awb_De_High_Red_Rate");
            }
        }

        public int Awb_De_High_Blue_Rate
        {
            get { return _awb.Awb_De_High_Blue_Rate; }
            set
            {
                _awb.Awb_De_High_Blue_Rate = value;
                RaisePropertyChanged("Awb_De_High_Blue_Rate");
            }
        }

        public int Seg_Mode
        {
            get { return _awb.Seg_Mode; }
            set
            {
                _awb.Seg_Mode = value;
                RaisePropertyChanged("Seg_Mode");
            }
        }

        public int Awb_Weight_In
        {
            get { return _awb.Awb_Weight_In; }
            set
            {
                _awb.Awb_Weight_In = value;
                RaisePropertyChanged("Awb_Weight_In");
            }
        }

        public int Awb_Weight_Out
        {
            get { return _awb.Awb_Weight_Out; }
            set
            {
                _awb.Awb_Weight_Out = value;
                RaisePropertyChanged("Awb_Weight_Out");
            }
        }

        public int RGainStart
        {
            get 
            {
                var value = _awb.RGainStart;
                // 确保返回值有效
                if (value < 0 || value > 1024)
                    return 170; // 默认值
                return value;
            }
            set { _awb.RGainStart = value; }
        }

        public int RGainEnd
        {
            get 
            {
                //return _awb.RGainStart + 16 * 31; 
                var value = _awb.RGainStart + 16 * 31;
                // 确保返回值在合理范围内
                if (value < 0 || value > 1024)
                    return 170 + 16 * 31; // 默认值
                return value;
            }
        }

        public ObservableCollection<WhiteBalanceStatCollection> StatisticData
        {
            get { return _awb.StatisticData; }
            set { _awb.StatisticData = value; }
        }

        public Dictionary<string, KeyValuePair<int, int>> GainData
        {
            get { return _awb.GainData; }
            set { _awb.GainData = value; }
        }

        public int RGainMin
        {
            get 
            {
                var value = _awb.RGainMin;
                if (value < 0 || value > 1024)
                    return 170; // 默认值
                return value;
            }
            set { _awb.RGainMin = value; }
        }

        public int RGainMax
        {
            get 
            {
                var value = _awb.RGainMax;
                if (value < 0 || value > 1024)
                    return 170; // 默认值
                return value;
            }
            set { _awb.RGainMax = value; }
        }

        public int Awb_Ymin
        {
            get { return _awb.Awb_YMin; }
            set { _awb.Awb_YMin = value; }
        }

        public int Awb_Ymax
        {
            get { return _awb.Awb_YMax; }
            set { _awb.Awb_YMax = value; }
        }

        public ObservableCollection<GainDataSeries> GainDataSeriesCollection
        {
            get { return _gainDataSeriesCollection; }
            set
            {
                _gainDataSeriesCollection = value;
                RaisePropertyChanged("GainDataSeriesCollection");
            }
        }

        #endregion

        private void LoadRawFiles()
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Multiselect = true;
            openFileDialog.Filter = "Raw文件(*.raw) | *.raw";
            if (!(bool)openFileDialog.ShowDialog())
            {
                return;
            }

            ColorblockPickingWindow colorblockPickingWindow = new ColorblockPickingWindow(_ispProcessor, openFileDialog.FileNames);
            var correctionData = new Dictionary<string, KeyValuePair<int, int>>();
            colorblockPickingWindow.DataContext = correctionData;
            colorblockPickingWindow.ShowDialog();

            _awb.GainData = correctionData.Concat(
                _awb.GainData.Where(dataItem => !correctionData.Keys.Contains(dataItem.Key)))
                .ToDictionary(x => x.Key, x => x.Value);

        }

        private void LoadChartData()
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Multiselect = true;
            openFileDialog.Filter = "Awb作图数据(*.ispawb) | *.ispawb";
            if (!(bool)openFileDialog.ShowDialog())
            {
                return;
            }
            IsLoadFile = true;
            _awb.LoadChartDataFile(openFileDialog.FileName);
        }

        private void SaveChartDataFile()
        {
            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
            saveFileDialog.CheckFileExists = false;
            saveFileDialog.Filter = "Awb作图数据(*.ispawb) | *.ispawb";
            if (!(bool)saveFileDialog.ShowDialog())
            {
                return;
            }
            IsLoadFile = true;

            _awb.SaveChartDataFile(saveFileDialog.FileName);
        }

        private void ViewIQ()
        {
            AwbIQWindow IQWindow = new AwbIQWindow(_ispProcessor);
            IQWindow.Show();
        }

        private void UpdateStatTab()
        {
            _awb.UpdateAwbStatTab();
        }

        // ==================== 智能插值相关属性 ====================

        public bool SmartInterpolationEnabled
        {
            get { return _awb.SmartInterpolationEnabled; }
            set { _awb.SmartInterpolationEnabled = value; RaisePropertyChanged("SmartInterpolationEnabled"); }
        }

        public int KeyPointCountPerCurve
        {
            get { return _awb.KeyPointCountPerCurve; }
            set { _awb.KeyPointCountPerCurve = value; RaisePropertyChanged("KeyPointCountPerCurve"); }
        }

        public ObservableCollection<ObservableCollection<KeyValuePair<double, double>>> KeyPointData
        {
            get { return _awb.KeyPointData; }
        }

        #region
        private System.Windows.Media.Brush _currentModeBackgroundColor = System.Windows.Media.Brushes.LightBlue;
        private System.Windows.Media.Brush _currentModeTextColor = System.Windows.Media.Brushes.White;

        public System.Windows.Media.Brush CurrentModeBackgroundColor
        {
            get { return _currentModeBackgroundColor; }
            set
            {
                if (_currentModeBackgroundColor != value)
                {
                    _currentModeBackgroundColor = value;
                    RaisePropertyChanged("CurrentModeBackgroundColor");
                }
            }
        }

        public System.Windows.Media.Brush CurrentModeTextColor
        {
            get { return _currentModeTextColor; }
            set
            {
                if (_currentModeTextColor != value)
                {
                    _currentModeTextColor = value;
                    RaisePropertyChanged("CurrentModeTextColor");
                }
            }
        }
        #endregion
        // ==================== 智能插值命令 ====================

        public RelayCommand ToggleSmartModeCommand
        {
            get { return _toggleSmartModeCommand; }
        }

        public RelayCommand GenerateCurveFromKeyPointsCommand
        {
            get { return _generateCurveFromKeyPointsCommand; }
        }

        public RelayCommand InitializeDefaultKeyPointsCommand
        {
            get { return _initializeDefaultKeyPointsCommand; }
        }

        public RelayCommand ApplySmartInterpolationCommand
        {
            get { return _applySmartInterpolationCommand; }
        }

        // ==================== 智能插值实现方法 ====================

        private void ToggleSmartMode()
        {
            SmartInterpolationEnabled = !SmartInterpolationEnabled;
            if (SmartInterpolationEnabled)
            {
                InitializeDefaultKeyPointsForAllCurves();
            }
        }

        private void GenerateCurveFromKeyPoints()
        {
            _awb.GenerateFullCurveFromKeyPoints();
        }

        private void InitializeDefaultKeyPoints()
        {
            InitializeDefaultKeyPointsForAllCurves();
        }

        private void ApplySmartInterpolation()
        {
            _awb.SmartUpdateAwbStatTab();
        }

        private void InitializeDefaultKeyPointsForAllCurves()
        {
            for (int i = 0; i < 4; i++)
            {
                _awb.InitializeDefaultKeyPoints(i);
            }
            RaisePropertyChanged("KeyPointData");
        }

    }
}
