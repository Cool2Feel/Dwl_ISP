using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Xml;

namespace ThunderSE.DeviceConfig.Isp
{
    public abstract class ProcessStep
    {
        public ProcessStep()
        {
            HasChangedParams = false;
        }

        public int DeviceModulePos = -1;

        public bool HasChangedParams
        {
            get;
            set;
        }

        private ObservableCollection<KeyValuePair<IspModule, bool>>  _previousStepsEnables = new ObservableCollection<KeyValuePair<IspModule, bool>>()
        { 
            new KeyValuePair<IspModule, bool>(IspModule.Blc, false),
            new KeyValuePair<IspModule, bool>(IspModule.Lsc, false),
            new KeyValuePair<IspModule, bool>(IspModule.Awb, false),
            new KeyValuePair<IspModule, bool>(IspModule.YGamma, false)
        };
        protected CommonConfig _commonConfig = null;


        //TODO: �����Ҫ��Ϊprivate,Ҫ��getset̫�鷳��
        public ObservableCollection<KeyValuePair<IspModule, bool>> PreviousStepsEnables
        {
            get { return _previousStepsEnables; }
        }
       
        public void SetPreviousStepEnable(IspModule previousStep, bool isEnable)
        {
            int previousStepPos =
                PreviousStepsEnables.IndexOf(PreviousStepsEnables.First(item => item.Key == previousStep));

            PreviousStepsEnables[previousStepPos] = new KeyValuePair<IspModule,bool>(previousStep, isEnable);
        }

        public virtual void SetCommonConfig(CommonConfig config)
        {
            _commonConfig = config;
        }

        abstract public void ProcessRawBuffer(ref byte[] imgBuffer);

        abstract public void ProcessRgbBuffer(ref byte[] imgBuffer);

        abstract public Dictionary<int, byte[]> ParamsDataCollection
        {
            get;
            set;
        }

        abstract public XmlElement SerializeToXmlElement(XmlDocument xmlDoc);

        abstract public void DeserializeFromXmlElement(XmlElement ispToolDataNode);
    }
}
