using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;
using ThunderSE.Common;

namespace ThunderSE.DeviceConfig.Isp
{
    public enum LscMode
    {
        Y,
        Rgb
    }

    public class LensShading : ProcessStep, INotifyPropertyChanged
    {
        private const int _blockSizeX = 32;//64
        private const int _blockSizeY = 32;

        private short[] _correctionData;
        private int _currentExpectedSize = 0;
        //private const int lscDataLine = ((720 / 2 + _blockSizeY - 1) / _blockSizeY + 1);

        //private short[] correctionData = new short[4 * ((1280 / 2 + _blockSizeX - 1) / _blockSizeX + 1) * lscDataLine];//572 //4*((1280/2+ blocksizeX-1)/ blocksizeX+1)* 1scDataLine

        //【新增】缓存最后一次处理生成的RGB图像指针，供IQ计算复用，避免二次Demosaic
        //private IntPtr[] _cachedRgbPlanes = null;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 重写基类方法，订阅 CommonConfig 的变化事件
        /// </summary>
        public override void SetCommonConfig(CommonConfig config)
        {
            // 取消旧订阅
            if (_commonConfig != null)
            {
                _commonConfig.PropertyChanged -= OnCommonConfigPropertyChanged;
            }

            _commonConfig = config;

            // 订阅新配置的变化
            if (_commonConfig != null)
            {
                _commonConfig.PropertyChanged += OnCommonConfigPropertyChanged;
            }
        }

        /// <summary>
        /// 处理 CommonConfig 属性变化事件
        /// </summary>
        private void OnCommonConfigPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CommonConfig.ResolutionHeight) || 
                e.PropertyName == nameof(CommonConfig.ResolutionWidth))
            {
                // 分辨率变化时，标记需要重新初始化 CorrectionData
                _currentExpectedSize = 0;  // 强制下次访问时重新分配
                //PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CorrectionData"));
            }
        }

        public LensShading()
        {
            DeviceModulePos = 2;
            //for (int i = 0; i < _correctionData.Length; i++)
            //{
            //    _correctionData[i] = 256;
            //}

            //SetPreviousStepEnable(IspModule.Blc, true);
        }

        public void SetPreviousStep(Processor ispProcessor)
        {
            ObservableCollection<KeyValuePair<IspModule, bool>> _ispProccesStepsEnables = ispProcessor.IspCommonConfig.ProcessorStepsEnables;
            if (_ispProccesStepsEnables == null)
            {
                return;
            }

            int previousStepPos = _ispProccesStepsEnables.IndexOf(_ispProccesStepsEnables.First(item => item.Key == IspModule.Blc));

            if (previousStepPos >= 0)
            {
                bool set = _ispProccesStepsEnables[previousStepPos].Value;
                SetPreviousStepEnable(IspModule.Blc, set);
            }
        }

        /// <summary>
        /// 根据当前配置的分辨率，动态计算LSC网格所需的真实节点数
        /// </summary>
        private int CalculateRequiredLscSize()
        {
            if (_commonConfig == null || _commonConfig.ResolutionWidth <= 0) return 0;

            // 数学公式必须与 C++ 底层 LscCal 中的 block_h/block_w 计算保持绝对一致
            int blockH = (_commonConfig.ResolutionHeight / 2 + _blockSizeY - 1) / _blockSizeY + 1;
            int blockW = (_commonConfig.ResolutionWidth / 2 + _blockSizeX - 1) / _blockSizeX + 1;
             return 4 * blockH * blockW; // 4 个 Bayer 通道
        }

        private void EnsureCorrectionDataInitialized()
        {
            int requiredSize = CalculateRequiredLscSize();
            if (requiredSize == 0) return;

            if (_correctionData == null || _correctionData.Length != requiredSize)
            {
                _currentExpectedSize = requiredSize;
                _correctionData = new short[requiredSize];
                //Console.WriteLine("Lsc correction data length: " + requiredSize);
                for (int i = 0; i < requiredSize; i++) _correctionData[i] = 256; // 默认1.0倍增益
                //PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CorrectionData"));
            }
        }

        public short[] CorrectionData
        {
            get { EnsureCorrectionDataInitialized(); return _correctionData; }
            set
            {
                _correctionData = value;

                HasChangedParams = true;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CorrectionData"));
                //PropertyChangedEventArgs args = new PropertyChangedEventArgs("CorrectionData");
                //if (PropertyChanged != null)
                //    PropertyChanged(this, args);
            }
        }

        public override void ProcessRawBuffer(ref byte[] imgBuffer)
        {
            try
            {
                if (imgBuffer == null)
                    throw new ArgumentNullException(nameof(imgBuffer));
                if (_commonConfig == null)
                    throw new InvalidOperationException("CommonConfig not initialized");

                Logger.Debug($"[LSC] Processing - Buffer: {imgBuffer.Length} bytes, Resolution: {_commonConfig.ResolutionWidth}x{_commonConfig.ResolutionHeight}, Block: {_blockSizeX}x{_blockSizeY}");

                int pixelCount = _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight;
                short[] outputBuffer = new short[pixelCount];
                Array.Clear(outputBuffer, 0, outputBuffer.Length);

                var lscWeightBuffer = CorrectionData.Select(x => Convert.ToInt32(x)).ToArray();
                Logger.Debug($"[LSC] Weight data size: {lscWeightBuffer.Length}");

                IspApi.LscImg(imgBuffer, _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, _blockSizeX, _blockSizeY,
                        lscWeightBuffer, outputBuffer);
                
                Logger.Debug($"[LSC] LscImg completed");

                byte[] outputByteBuffer = new byte[Buffer.ByteLength(outputBuffer)];
                Buffer.BlockCopy(outputBuffer, 0, outputByteBuffer, 0, outputByteBuffer.Length);

                imgBuffer = outputByteBuffer;
                Logger.Debug($"[LSC] Processing completed, output buffer: {imgBuffer.Length} bytes");
            }
            catch (Exception ex)
            {
                Logger.Error("[LSC] ProcessRawBuffer failed.", ex);
                throw;
            }
        }

        public void CalWeight(byte[] rawFileBuffer, LscMode lscMode, int pointX, int pointY)
        {
            EnsureCorrectionDataInitialized();

            //var lscWeightBuffer = new int[CorrectionData.Length];
            var lscWeightBuffer = new int[_currentExpectedSize];

            //GCHandle pinnedRaw = GCHandle.Alloc(rawFileBuffer, GCHandleType.Pinned);
            try
            {
                IspApi.LscCal(rawFileBuffer, _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight,
                    _blockSizeX, _blockSizeY, (int)lscMode, (int)_commonConfig.Bayer, lscWeightBuffer, pointX, pointY);
            }
            catch (Exception ex)
            {
                Console.WriteLine("LscCal exception: " + ex.Message);
                return;
            }
            finally
            {
                //pinnedRaw.Free();
            }
            //_correctionData = lscWeightBuffer.Select(x => Convert.ToInt16(x)).ToArray();
            //_correctionData = lscWeightBuffer.Select(x => (short)Math.Clamp(x, 0, short.MaxValue)).ToArray();
            // 【修复3】安全截断，使用兼容旧框架的写法，防止底层算出的异常大值导致 short 溢出变成负数
            CorrectionData = lscWeightBuffer.Select(x => (short)Math.Max(0, Math.Min(x, short.MaxValue))).ToArray();

            //PropertyChangedEventArgs args = new PropertyChangedEventArgs("CorrectionData");
            //if (PropertyChanged != null)
            //    PropertyChanged(this, args);
        }

        public void CalcIQ(byte[] fileBuffer, ref ColorShadingIQResult colorShadingIQResult, ref LensShadingIQResult lensShadingIQResult)
        {
            using (var memoryManager = new MemoryManager())
            {
                IntPtr[] ptrArray = new IntPtr[3];
                for (int i = 0; i < ptrArray.Length; i++)
                {
                    ptrArray[i] = memoryManager.AllocateMemory(_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
                    Marshal.Copy(new byte[_commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short)],
                        0, ptrArray[i], _commonConfig.ResolutionWidth * _commonConfig.ResolutionHeight * sizeof(short));
                }

                IspApi.DemosaicImg(fileBuffer, (int)_commonConfig.Bayer, _commonConfig.ResolutionWidth,
                    _commonConfig.ResolutionHeight, ptrArray);

                IspApi.LscIQ(ptrArray, _commonConfig.ResolutionWidth, _commonConfig.ResolutionHeight, ref colorShadingIQResult, ref lensShadingIQResult);

                //for (int i = 0; i < ptrArray.Length; i++)
                //{
                //    Marshal.FreeHGlobal(ptrArray[i]);
                //}
            }
        }

        public override Dictionary<int, byte[]> ParamsDataCollection
        {
            get
            {
                //byte[] arr = new byte[CorrectionData.Length * sizeof(short)];
                //Buffer.BlockCopy(CorrectionData, 0, arr, 0, arr.Length * sizeof(byte));
                int byteCount = CorrectionData.Length * sizeof(short);
                byte[] arr = new byte[byteCount];
                Buffer.BlockCopy(CorrectionData, 0, arr, 0, byteCount);  // 明确的字节数

                return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
            }
            set
            {
                var tmpData = new short[CorrectionData.Length];
                Buffer.BlockCopy(value[DeviceModulePos], 0, tmpData, 0, tmpData.Length * sizeof(short));

                CorrectionData = tmpData;
            }
        }

        public override XmlElement SerializeToXmlElement(XmlDocument xmlDoc)
        {
            EnsureCorrectionDataInitialized();
            var xmlElement = xmlDoc.CreateElement("Lsc");

            XmlElement lscWeight = xmlDoc.CreateElement("Lsc_Weight");
            string lscWeightStr = string.Join(",", CorrectionData.Select(x => x.ToString()).ToArray());
            lscWeight.AppendChild(xmlDoc.CreateTextNode(lscWeightStr));
            xmlElement.AppendChild(lscWeight);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(XmlElement ispToolDataNode)
        {
            var lscNode = ispToolDataNode["Lsc"];

            var tmpLscWeightStr = lscNode["Lsc_Weight"].FirstChild.Value;
            CorrectionData = tmpLscWeightStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Convert.ToInt16(s))
                .ToArray();
        }

        public override void ProcessRgbBuffer(ref byte[] imgBuffer)
        {
            throw new NotImplementedException();
        }
    }
}
