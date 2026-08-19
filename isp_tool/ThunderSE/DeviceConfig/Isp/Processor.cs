using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Serialization;
using ThunderSE.Common;

namespace ThunderSE.DeviceConfig.Isp
{
    public enum IspModule
    {
        AE,
        Blc,
        Lsc,
        Ddc,
        Awb,
        Ccm,
        Dgain,
        YGamma,
        RgbGamma,
        Ch,
        Vde,
        Ee,
        Cfd,
        Saj,
        GainLevel,
        GammaTable
    }

    public class Processor
    {
        private CommonConfig _ispCommonconfig = new CommonConfig();
        public CommonConfig IspCommonConfig
        {
            get { return _ispCommonconfig; }
            set
            {
                _ispCommonconfig = value;
                foreach (var item in AllProcessSteps)
                {
                    item.Value.SetCommonConfig(IspCommonConfig);
                }
            }
        }

        public Dictionary<IspModule, ProcessStep> AllProcessSteps = new Dictionary<IspModule, ProcessStep>
        {
            { IspModule.AE, new AE() },
            { IspModule.Blc, new BlackLevel() },
            { IspModule.Lsc, new LensShading() },
            { IspModule.Ddc, new DDC() },
            { IspModule.Awb, new AutoWhiteBalance()},
            { IspModule.Ccm, new CCM() },
            { IspModule.YGamma, new YGamma() },
            { IspModule.Ch, new CH() },
            { IspModule.Vde, new VDE() },
            { IspModule.Ee, new EE() },
            { IspModule.Saj, new SAJ() },
            { IspModule.GainLevel, new GainLevel() },
            { IspModule.GammaTable, new GammaTable() }
        };

        public Dictionary<IspModule, ProcessStep> RawFileProcessSteps;
        public Dictionary<IspModule, ProcessStep> RgbFileProcessSteps;

        public string ConfigName
        {
            get;
            set;
        }

        public bool TryGetProcessStep<T>(IspModule module, out T step) where T : ProcessStep
        {
            if (AllProcessSteps.TryGetValue(module, out var found) && found is T typed)
            {
                step = typed;
                return true;
            }
            step = null;
            return false;
        }

        public Processor()
        {
            try
            {
                foreach (var item in AllProcessSteps)
                {
                    item.Value.SetCommonConfig(IspCommonConfig);
                }

                RawFileProcessSteps = new Dictionary<IspModule, ProcessStep>
                {
                    { IspModule.Blc, AllProcessSteps[IspModule.Blc] },
                    { IspModule.Lsc, AllProcessSteps[IspModule.Lsc] },
                    { IspModule.Awb, AllProcessSteps[IspModule.Awb] },
                    { IspModule.Ccm, AllProcessSteps[IspModule.Ccm] }
                };

                RgbFileProcessSteps = new Dictionary<IspModule, ProcessStep>
                {
                    { IspModule.YGamma, AllProcessSteps[IspModule.YGamma] },
                    { IspModule.GammaTable, AllProcessSteps[IspModule.GammaTable] }
                };

                Logger.Debug("Processor initialized with ISP modules.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize Processor.", ex);
                throw;
            }
        }

        // Process RAW file, optionally processing previous steps before finalStep
        public void ProcessRawFile(ref byte[] rawFileBuffer, IspModule finalPrcessSteps, bool useFinalStep = true)
        {
            try
            {
                Logger.Debug($"Processing RAW file, final step: {finalPrcessSteps}, useFinalStep: {useFinalStep}");

                foreach (var step in RawFileProcessSteps[finalPrcessSteps].PreviousStepsEnables)
                {
                    if (step.Value == true)
                    {
                        Logger.Debug($"Processing dependent step: {step.Key}");
                        RawFileProcessSteps[step.Key].ProcessRawBuffer(ref rawFileBuffer);
                    }
                }

                if (useFinalStep)
                {
                    Logger.Debug($"Processing final step: {finalPrcessSteps}");
                    RawFileProcessSteps[finalPrcessSteps].ProcessRawBuffer(ref rawFileBuffer);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ProcessRawFile failed for module: {finalPrcessSteps}", ex);
                throw;
            }
        }

        public void ProcessRGBFile(ref byte[] RgbFileBuffer, IspModule finalPrcessSteps = IspModule.YGamma)
        {
            try
            {
                Logger.Debug($"Processing RGB file, module: {finalPrcessSteps}");
                RgbFileProcessSteps[finalPrcessSteps].ProcessRgbBuffer(ref RgbFileBuffer);
            }
            catch (Exception ex)
            {
                Logger.Error($"ProcessRGBFile failed for module: {finalPrcessSteps}", ex);
                throw;
            }
        }

        public BitmapImage GenerateBitmapUsingRaw(byte[] rawImgBuffer, IspModule processorStep, bool useFinalStep = true)
        {
            int _imgHeight = IspCommonConfig.ResolutionHeight;
            int _imgWidth = IspCommonConfig.ResolutionWidth;

            try
            {
                Logger.Debug($"Generating bitmap, processor step: {processorStep}");
                ProcessRawFile(ref rawImgBuffer, processorStep, useFinalStep);

                IntPtr[] ptrArray = new IntPtr[3];
                for (int i = 0; i < ptrArray.Length; i++)
                {
                    ptrArray[i] = Marshal.AllocHGlobal(_imgWidth * _imgHeight * sizeof(short));
                    Marshal.Copy(new byte[_imgWidth * _imgHeight * sizeof(short)], 0, ptrArray[i], _imgWidth * _imgHeight * sizeof(short));
                }

                IspApi.DemosaicImg(rawImgBuffer, (int)IspCommonConfig.Bayer, _imgWidth, _imgHeight, ptrArray);
                int size = 0;
                IspApi.EncoderImgBuffer(ptrArray, _imgWidth, _imgHeight, 2, null, ref size);
                byte[] buffer = new byte[size];
                IspApi.EncoderImgBuffer(ptrArray, _imgWidth, _imgHeight, 2, buffer, ref size);

                for (int i = 0; i < ptrArray.Length; i++)
                {
                    Marshal.FreeHGlobal(ptrArray[i]);
                }

                var image = new BitmapImage();
                using (MemoryStream memStream = new MemoryStream(buffer))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = memStream;
                    image.EndInit();
                    image.Freeze();
                }

                // 创建垂直翻转的变换 (ScaleY = -1)
                //var flipTransform = new ScaleTransform(1, -1);

                //// 使用 TransformedBitmap 创建新图
                //var flippedImage = new TransformedBitmap(image, flipTransform);

                //// 别忘了将新图也 Freeze，这样它依然可以跨线程使用
                //flippedImage.Freeze();

                Logger.Debug($"Bitmap generated successfully for step: {processorStep}");
                //return flippedImage;

                return image;
            }
            catch (Exception ex)
            {
                Logger.Error($"GenerateBitmapUsingRaw failed for step: {processorStep}", ex);
                throw;
            }
        }

        public BitmapImage GenerateBitmapUsingRgb(byte[] rgbImgBuffer, IspModule processorStep = IspModule.YGamma)
        {
            int _imgHeight = IspCommonConfig.ResolutionHeight;
            int _imgWidth = IspCommonConfig.ResolutionWidth;

            //ProcessRGBFile(ref rgbImgBuffer, processorStep);

            int tmpReadPos = 0;
            IntPtr[] inBuffer = new IntPtr[3];
            for (int i = 0; i < inBuffer.Length; i++)
            {
                inBuffer[i] = Marshal.AllocHGlobal(_imgWidth * _imgHeight * sizeof(short));
                Marshal.Copy(rgbImgBuffer,
                    tmpReadPos, inBuffer[i], _imgWidth * _imgHeight * sizeof(short));

                tmpReadPos += _imgWidth * _imgHeight * sizeof(short);
            }

            //TODO:release版本这里有问题,想法子fix吧
            int size = 0;
            IspApi.EncoderImgBuffer(inBuffer, _imgWidth, _imgHeight, 0, null, ref size);
            byte[] buffer = new byte[size];
            IspApi.EncoderImgBuffer(inBuffer, _imgWidth, _imgHeight, 0, buffer, ref size);

            for (int i = 0; i < inBuffer.Length; i++)
            {
                Marshal.FreeHGlobal(inBuffer[i]);
            }

            var image = new BitmapImage();
            using (MemoryStream memStream = new MemoryStream(buffer))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = memStream;
                image.EndInit();
            }

            return image;
        }

        public void WriteToFile(XmlWriter writer)
        {
            try
            {
                Logger.Debug($"Writing processor config to file: {ConfigName}");

                XmlSerializer serializer = new XmlSerializer(typeof(Processor));
                serializer.Serialize(writer, this);

                Logger.Debug($"Processor config written successfully.");
            }
            catch (Exception ex)
            {
                Logger.Error("WriteToFile failed.", ex);
                throw;
            }
        }

        public void ReadFromFile(XmlReader reader)
        {
            try
            {
                Logger.Debug($"Reading processor config from file.");

                XmlSerializer serializer = new XmlSerializer(typeof(Processor));
                Processor loadedProcessor = (Processor)serializer.Deserialize(reader);

                // Update this instance with loaded data
                IspCommonConfig = loadedProcessor.IspCommonConfig;
                ConfigName = loadedProcessor.ConfigName;

                // Update all process steps with loaded configuration
                foreach (var step in AllProcessSteps)
                {
                    if (loadedProcessor.AllProcessSteps.ContainsKey(step.Key))
                    {
                        //step.Value.CopyFrom(loadedProcessor.AllProcessSteps[step.Key]);
                        step.Value.SetCommonConfig(IspCommonConfig);
                    }
                }

                Logger.Debug($"Processor config read successfully.");
            }
            catch (Exception ex)
            {
                Logger.Error("ReadFromFile failed.", ex);
                throw;
            }
        }
    }
}
