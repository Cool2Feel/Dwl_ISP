using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Model;
using ThunderSE.Ui.SettingWindow.IspSteps;
using RelayCommand = ThunderSE.Model.RelayCommand;

namespace ThunderSE.Ui.SettingWindow.Lsc
{
    class LscWindowViewModel : ViewModelBase
    {
        private IspStepsWindow _ispStepsWindow = null;

        private Processor _ispProcessor = null;
        private LensShading _lensShading = null;

        private RelayCommand _loadRawFileCommand;
        private RelayCommand<int[]> _calcLscWeightCommand;

        private RelayCommand _viewIQCommand;

        private RelayCommand _viewPreviousIspStep;

        // 新增异步命令
        private AsyncRelayCommand _loadRawFileAsyncCommand;
        private AsyncRelayCommand<int[]> _calcLscWeightAsyncCommand;
        private AsyncRelayCommand _viewIQAsyncCommand;

        // 自动描点相关
        private RelayCommand _autoDetectBrightestCommand;
        private bool _autoDotEnabled = true;
        public Action AutoDetectBrightestAction { get; set; }

        private byte[] _originRawFileBuffer;
        private byte[] _processedRawFileBuffer;

        public LscWindowViewModel(Processor ispProcessor)
        {
            SelectedLscMode = 1;

            _ispProcessor = ispProcessor;
            _lensShading = (LensShading)ispProcessor.AllProcessSteps[IspModule.Lsc];
            _lensShading.SetPreviousStep(ispProcessor);

            _loadRawFileCommand = new RelayCommand(LoadRawFile);
            _loadRawFileAsyncCommand = new AsyncRelayCommand(LoadRawFileAsync);

            _calcLscWeightCommand = new RelayCommand<int[]>(CalcWeight);
            _calcLscWeightAsyncCommand = new AsyncRelayCommand<int[]>(CalcWeightAsync);

            _viewIQCommand = new RelayCommand(ViewIQ);
            _viewIQAsyncCommand = new AsyncRelayCommand(ViewIQAsync);

            _viewPreviousIspStep = new RelayCommand(ViewPreviousIspStep);

            _autoDetectBrightestCommand = new RelayCommand(AutoDetectBrightest);

            _lensShading.PropertyChanged += LscConfigsChange;
        }

        public byte[] ProcessedRawFileBuffer
        {
            get { return _processedRawFileBuffer; }
            set 
            {
                _processedRawFileBuffer = value;
                RaisePropertyChanged("ProcessedRawFileBuffer");
                RaisePropertyChanged("HasProcessedRawFile");
            }
        }

        public CommonConfig IspCommonConfig
        {
            get { return _ispProcessor.IspCommonConfig; }
        }

        public bool HasProcessedRawFile
        {
            get { return _processedRawFileBuffer != null; }
        }

        public byte[] OriginRawFileBuffer
        {
            get { return _originRawFileBuffer; }
            set 
            { 
                _originRawFileBuffer = value;
                RaisePropertyChanged("OriginRawFileBuffer");
                RaisePropertyChanged("HasLoadedRawFile");
            }
        }
        public bool HasLoadedRawFile
        {
            get { return _originRawFileBuffer != null; }
        }

        void LscConfigsChange(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "CorrectionData" && _originRawFileBuffer != null)
            {
                // 清除缓存，确保使用最新的参数处理图像
                RawBufferToBitmapImageConverter.ClearCache();

                _processedRawFileBuffer = new byte[_originRawFileBuffer.Length];
                Buffer.BlockCopy(_originRawFileBuffer, 0, _processedRawFileBuffer, 0, _originRawFileBuffer.Length);
                _ispProcessor.ProcessRawFile(ref _processedRawFileBuffer, IspModule.Lsc);

                RaisePropertyChanged("ProcessedRawFileBuffer");
                RaisePropertyChanged("HasProcessedRawFile");
            }
        }

        public AsyncRelayCommand LoadRawFileCommand
        {
            //get { return _loadRawFileCommand; }
            get
            {
                return _loadRawFileAsyncCommand;
            }
        }
        public RelayCommand<int[]> CalcLscWeightCommand
        {
            get { return _calcLscWeightCommand; }
        }

        public RelayCommand ViewPreviousIspStepCommand
        {
            get { return _viewPreviousIspStep; }
        }

        public RelayCommand ViewIQCommand
        {
            get { return _viewIQCommand; }
        }

        public int SelectedLscMode
        {
            get;
            set;
        }

        public bool AutoDotEnabled
        {
            get { return _autoDotEnabled; }
            set
            {
                _autoDotEnabled = value;
                RaisePropertyChanged("AutoDotEnabled");
            }
        }

        public RelayCommand AutoDetectBrightestCommand
        {
            get { return _autoDetectBrightestCommand; }
        }

        private void AutoDetectBrightest()
        {
            // 通过 Action 委托调用 View 层的实现
            AutoDetectBrightestAction?.Invoke();
        }

        private void LoadRawFile()
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Raw文件(*.raw) | *.raw";
            if (!(bool)openFileDialog.ShowDialog())
            {
                return;
            }
            //Console.WriteLine(openFileDialog.FileName);

            // 1. 读取文件后，先推断或询问分辨率
            //int detectedWidth = DetectWidthFromFileName(openFileDialog.FileName); // 示例

            //// 2. 更新配置核心
            //if (_ispProcessor.IspCommonConfig.ResolutionWidth != detectedWidth)
            //{
            //    _ispProcessor.UpdateResolution(detectedWidth, detectedHeight); // 调用C++接口更新
            //    AllocImgBuff(); // 重新分配内存
            //}
            try
            {
                OriginRawFileBuffer = File.ReadAllBytes(openFileDialog.FileName);
                RaisePropertyChanged("OriginRawFileBuffer");
            }
            catch (Exception ex) { 
                Console.WriteLine(ex.ToString());
            }
        }

        private void CalcWeight(int[] parameters)
        {
            _lensShading.CalWeight(_originRawFileBuffer, (LscMode)SelectedLscMode, parameters[0], parameters[1]);
        }

        // 异步加载原始图像
        private async Task LoadRawFileAsync()
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Raw文件(*.raw) | *.raw";
            if (!(bool)openFileDialog.ShowDialog())
            {
                return;
            }

            try
            {
                // 使用Task.Run在后台线程读取文件
                OriginRawFileBuffer = await Task.Run(() => File.ReadAllBytes(openFileDialog.FileName));
                RaisePropertyChanged("OriginRawFileBuffer");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                // 在UI线程显示错误信息
                Application.Current.Dispatcher.Invoke(() => {
                    MessageBox.Show($"加载文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        // 异步计算LSC权重
        private async Task CalcWeightAsync(int[] parameters)
        {
            try
            {
                // 使用Task.Run在后台线程计算权重
                await Task.Run(() => {
                    _lensShading.CalWeight(_originRawFileBuffer, (LscMode)SelectedLscMode, parameters[0], parameters[1]);
                });

                // 在UI线程显示完成信息
                Application.Current.Dispatcher.Invoke(() => {
                    MessageBox.Show("LSC权重计算完成！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                // 在UI线程显示错误信息
                Application.Current.Dispatcher.Invoke(() => {
                    MessageBox.Show($"计算LSC权重失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        // 异步查看IQ
        private async Task ViewIQAsync()
        {
            try
            {
                // 在后台线程处理图像数据
                byte[] processedBuffer = null;
                await Task.Run(() => {
                    processedBuffer = new byte[_originRawFileBuffer.Length];
                    Buffer.BlockCopy(_originRawFileBuffer, 0, processedBuffer, 0, _originRawFileBuffer.Length);
                    _ispProcessor.ProcessRawFile(ref processedBuffer, IspModule.Lsc);
                });

                // 在UI线程显示IQ窗口
                Application.Current.Dispatcher.Invoke(() => {
                    LscIQWindow IQWindow = new LscIQWindow(_lensShading, processedBuffer);
                    IQWindow.Show();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                // 在UI线程显示错误信息
                Application.Current.Dispatcher.Invoke(() => {
                    MessageBox.Show($"处理图像失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private void ViewIQ()
        {
            //这里已经有处理过的raw了，直接拿来用就行
            LscIQWindow IQWindow = new LscIQWindow(_lensShading, ProcessedRawFileBuffer);
            IQWindow.Show();
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
                _ispStepsWindow.DataContext = new IspStepsWindowViewModel(_ispProcessor, DeviceConfig.Isp.IspModule.Lsc);
                _ispStepsWindow.Show();
            }
        }
    }
}
