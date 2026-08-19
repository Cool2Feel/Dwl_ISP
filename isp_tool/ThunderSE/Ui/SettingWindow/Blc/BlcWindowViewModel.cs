using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Model;
using RelayCommand = ThunderSE.Model.RelayCommand;

namespace ThunderSE.Ui.SettingWindow.Blc
{
    class BlcWindowViewModel : ViewModelBase, ICleanup
    {
        private enum CorrectionForm
        {
            Median,
            Average
        }

        private Dictionary<string, int> _medianValues = new Dictionary<string, int>();
        private Dictionary<string, int> _avgValues = new Dictionary<string, int>();

        private string _rawFile = "";
        private CorrectionForm _correctionForm = CorrectionForm.Median;

        private AsyncRelayCommand _openRawFileCommand;
        private RelayCommand _applyCorrectionCommand;

        private Processor _ispProcessor;
        private BlackLevel _blackLevelData;

        private Dictionary<BlackLevelPixelType, short[]> _blackLevelDataArrays;
        private byte[] _nativeRawFileBuffer;

        private bool _isCleanedUp = false;

        public BlcWindowViewModel(Processor ispProcessor)
        {
            _ispProcessor = ispProcessor;
            _blackLevelData = (BlackLevel)ispProcessor.AllProcessSteps[IspModule.Blc];

            _openRawFileCommand = new AsyncRelayCommand(OpenRawFileAndCalcBlackLevel);
            _applyCorrectionCommand = new RelayCommand(ApplyCorrection);

            var arrayLength = _ispProcessor.IspCommonConfig.ResolutionWidth * _ispProcessor.IspCommonConfig.ResolutionHeight / 4;
            _blackLevelDataArrays = new Dictionary<BlackLevelPixelType, short[]>() {
                { BlackLevelPixelType.R, new short[arrayLength] } ,
                { BlackLevelPixelType.Gr, new short[arrayLength] } ,
                { BlackLevelPixelType.Gb, new short[arrayLength] } ,
                { BlackLevelPixelType.B, new short[arrayLength] } ,
            };

            foreach (var item in _blackLevelDataArrays)
            {
                Array.Clear(item.Value, 0, item.Value.Length);
            }
        }

        public AsyncRelayCommand OpenRawFileCommand
        {
            get { return _openRawFileCommand; }
            set { _openRawFileCommand = value; }
        }

        public RelayCommand ApplyCorrectionCommand
        {
            get { return _applyCorrectionCommand; }
            set { _applyCorrectionCommand = value; }
        }

        public string RawFile
        {
            get { return _rawFile; }
            private set
            {
                _rawFile = value;
                RaisePropertyChanged("RawFile");
            }
        }


        #region 像素分布数据

        /* * 原有 WPF 绑定数据绑定机制，直接绑定到图表会产生问题（每次更新都会重新绑定整个数组）
        public Dictionary<int, int> RPixelData
        {
            get
            {
                var pixelDictionary = new Dictionary<int, int>();
                foreach (var group in _blackLevelDataArrays[BlackLevelPixelType.R].GroupBy(i => i))
                {
                    pixelDictionary[group.Key] = group.Count();
                }

                return pixelDictionary;
            }
        }

        public Dictionary<int, int> GRPixelData
        {
            get
            {
                var pixelDictionary = new Dictionary<int, int>();
                foreach (var group in _blackLevelDataArrays[BlackLevelPixelType.Gr].GroupBy(i => i))
                {
                    pixelDictionary[group.Key] = group.Count();
                }

                return pixelDictionary;
            }
        }

        public Dictionary<int, int> GBPixelData
        {
            get
            {
                var pixelDictionary = new Dictionary<int, int>();
                foreach (var group in _blackLevelDataArrays[BlackLevelPixelType.Gb].GroupBy(i => i))
                {
                    pixelDictionary[group.Key] = group.Count();
                }

                return pixelDictionary;
            }
        }
        public Dictionary<int, int> BPixelData
        {
            get
            {
                var pixelDictionary = new Dictionary<int, int>();
                foreach (var group in _blackLevelDataArrays[BlackLevelPixelType.B].GroupBy(i => i))
                {
                    pixelDictionary[group.Key] = group.Count();
                }

                return pixelDictionary;
            }
        }
        */
        private Dictionary<int, int> _rPixelData;
        private Dictionary<int, int> _grPixelData;
        private Dictionary<int, int> _gbPixelData;
        private Dictionary<int, int> _bPixelData;

        public Dictionary<int, int> RPixelData => _rPixelData;
        public Dictionary<int, int> GRPixelData => _grPixelData;
        public Dictionary<int, int> GBPixelData => _gbPixelData;
        public Dictionary<int, int> BPixelData => _bPixelData;

        private void UpdatePixelData()
        {
            _rPixelData = BuildPixelData(BlackLevelPixelType.R);
            _grPixelData = BuildPixelData(BlackLevelPixelType.Gr);
            _gbPixelData = BuildPixelData(BlackLevelPixelType.Gb);
            _bPixelData = BuildPixelData(BlackLevelPixelType.B);

            RaisePropertyChanged("RPixelData");
            RaisePropertyChanged("GRPixelData");
            RaisePropertyChanged("GBPixelData");
            RaisePropertyChanged("BPixelData");
        }

        private Dictionary<int, int> BuildPixelData(BlackLevelPixelType type)
        {
            var pixelDictionary = new Dictionary<int, int>();
            foreach (var group in _blackLevelDataArrays[type].GroupBy(i => i))
            {
                pixelDictionary[group.Key] = group.Count();
            }
            return pixelDictionary;
        }


        #endregion

        #region 平均值
        public int AvgBlackLevelR
        {
            get
            {
                int val = 0;
                _avgValues.TryGetValue("AvgBlackLevelR", out val);
                return val;
            }
        }

        public int AvgBlackLevelGR
        {
            get
            {
                int val = 0;
                _avgValues.TryGetValue("AvgBlackLevelGR", out val);
                return val;
            }
        }

        public int AvgBlackLevelGB
        {
            get
            {
                int val = 0;
                _avgValues.TryGetValue("AvgBlackLevelGB", out val);
                return val;
            }
        }

        public int AvgBlackLevelB
        {
            get
            {
                int val = 0;
                _avgValues.TryGetValue("AvgBlackLevelB", out val);
                return val;
            }
        }
        #endregion

        #region 中值
        public int MedianBlackLevelR
        {
            get
            {
                int val = 0;
                _medianValues.TryGetValue("MedianBlackLevelR", out val);
                return val;
            }
        }

        public int MedianBlackLevelGR
        {
            get
            {
                int val = 0;
                _medianValues.TryGetValue("MedianBlackLevelGR", out val);
                return val;
            }
        }

        public int MedianBlackLevelGB
        {
            get
            {
                int val = 0;
                _medianValues.TryGetValue("MedianBlackLevelGB", out val);
                return val;
            }
        }

        public int MedianBlackLevelB
        {
            get
            {
                int val = 0;
                _medianValues.TryGetValue("MedianBlackLevelB", out val);
                return val;
            }
        }
        #endregion


        public int SelectedCorrection
        {
            get { return (int)_correctionForm; }
            set
            {
                _correctionForm = (CorrectionForm)value;
            }
        }
        private async Task OpenRawFileAndCalcBlackLevel()
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Raw文件(*.raw) | *.raw";
            if (!(bool)openFileDialog.ShowDialog())
            {
                return;
            }

            RawFile = openFileDialog.FileName;

            _nativeRawFileBuffer = File.ReadAllBytes(openFileDialog.FileName);
            await Task.Run(() =>
            {
                CalBlackLevelData(_nativeRawFileBuffer);
            });
            //CalBlackLevelData(_nativeRawFileBuffer);

            _medianValues["MedianBlackLevelR"] = GetMedianPixelValue(_blackLevelDataArrays[BlackLevelPixelType.R]);
            _medianValues["MedianBlackLevelGR"] = GetMedianPixelValue(_blackLevelDataArrays[BlackLevelPixelType.Gr]);
            _medianValues["MedianBlackLevelGB"] = GetMedianPixelValue(_blackLevelDataArrays[BlackLevelPixelType.Gb]);
            _medianValues["MedianBlackLevelB"] = GetMedianPixelValue(_blackLevelDataArrays[BlackLevelPixelType.B]);

            _avgValues["AvgBlackLevelR"] = (int)_blackLevelDataArrays[BlackLevelPixelType.R].Average((short item) => { return (int)item; });
            _avgValues["AvgBlackLevelGR"] = (int)_blackLevelDataArrays[BlackLevelPixelType.Gr].Average((short item) => { return (int)item; });
            _avgValues["AvgBlackLevelGB"] = (int)_blackLevelDataArrays[BlackLevelPixelType.Gb].Average((short item) => { return (int)item; });
            _avgValues["AvgBlackLevelB"] = (int)_blackLevelDataArrays[BlackLevelPixelType.B].Average((short item) => { return (int)item; });

            RaisePropertyChanged("AvgBlackLevelR");
            RaisePropertyChanged("AvgBlackLevelGR");
            RaisePropertyChanged("AvgBlackLevelGB");
            RaisePropertyChanged("AvgBlackLevelB");

            RaisePropertyChanged("MedianBlackLevelR");
            RaisePropertyChanged("MedianBlackLevelGR");
            RaisePropertyChanged("MedianBlackLevelGB");
            RaisePropertyChanged("MedianBlackLevelB");
        }

        private void CalBlackLevelData(byte[] rawFileBuf)
        {
            _blackLevelData.CalBlackLevelData(rawFileBuf, _blackLevelDataArrays);

            //RaisePropertyChanged("RPixelData");
            //RaisePropertyChanged("GRPixelData");
            //RaisePropertyChanged("GbPixelData");
            //RaisePropertyChanged("BPixelData");
            UpdatePixelData();
        }

        private void ApplyCorrection()
        {
            if (_nativeRawFileBuffer == null)
            {
                MessageBox.Show("请先加载 RAW 文件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            switch (_correctionForm)
            {
                case CorrectionForm.Median:
                    {
                        short[] correctionValueArray = { (short)MedianBlackLevelR, (short)MedianBlackLevelGR,
                              (short)MedianBlackLevelGB, (short)MedianBlackLevelB };
                        _blackLevelData.ApplyBlackLevelCorrection(correctionValueArray);
                    }

                    break;
                case CorrectionForm.Average:
                    {
                        short[] correctionValueArray = { (short)AvgBlackLevelR, (short)AvgBlackLevelGR,
                              (short)AvgBlackLevelGB,(short) AvgBlackLevelB };
                        _blackLevelData.ApplyBlackLevelCorrection(correctionValueArray);
                    }
                    break;
                default:
                    break;
            }

            byte[] correctingRawBuffer = new byte[_nativeRawFileBuffer.Length];
            Buffer.BlockCopy(_nativeRawFileBuffer, 0, correctingRawBuffer, 0, _nativeRawFileBuffer.Length);
            Task.Run(() =>
            {
                _blackLevelData.ProcessRawBuffer(ref correctingRawBuffer);

                // 在 UI 线程更新图表
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CalBlackLevelData(correctingRawBuffer);
                });
            });
        }

        private short GetMedianPixelValue(IEnumerable<short> PixelValueArray)
        {
            // Create a copy of the input, and sort the copy
            short[] temp = PixelValueArray.ToArray();
            Array.Sort(temp);

            int count = temp.Length;
            if (count == 0)
            {
                throw new InvalidOperationException("Empty collection");
            }
            else if (count % 2 == 0)
            {
                // count is even, average two middle elements
                short a = temp[count / 2 - 1];
                short b = temp[count / 2];
                return (short)((a + b) / 2);
            }
            else
            {
                // count is odd, return the middle element
                return temp[count / 2];
            }

        }

        private short GetMedianPixelValue(short[] pixelValueArray)
        {
            // 扩展直方图范围: 支持 [-512, 1023] 覆盖可能的异常值
            const int minVal = -512;
            const int maxVal = 1023;
            const int range = maxVal - minVal + 1;
            
            int[] histogram = new int[range];
            int validCount = 0;

            foreach (short val in pixelValueArray)
            {
                if (val >= minVal && val <= maxVal)
                {
                    histogram[val - minVal]++;  // 偏移索引
                    validCount++;
                }
            }

            if (validCount == 0)
                return 0;  // 无有效数据

            int medianIndex1 = (validCount - 1) / 2;
            int medianIndex2 = validCount / 2;
            int currentIndex = -1;
            int medianVal1 = 0, medianVal2 = 0;

            for (int i = 0; i < range; i++)
            {
                currentIndex += histogram[i];

                if (medianVal1 == 0 && currentIndex >= medianIndex1)
                    medianVal1 = i + minVal;  // 还原实际值

                if (currentIndex >= medianIndex2)
                {
                    medianVal2 = i + minVal;
                    break;
                }
            }

            return (short)((medianVal1 + medianVal2) / 2);
        }

        public override void Cleanup()
        {
            if (_isCleanedUp) return;

            _ispProcessor = null;
            _blackLevelData = null;
            _blackLevelDataArrays = null;
            _nativeRawFileBuffer = null;
            _medianValues.Clear();
            _avgValues.Clear();

            _isCleanedUp = true;
        }
    }
}
