using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;

namespace ThunderSE.DeviceConfig.Isp
{
    class AE : ProcessStep, INotifyPropertyChanged
    {
        private EXP _expAdapt = new EXP();
        private HGRM _hgrmAdapt = new HGRM();

        public AE()
        {
            DeviceModulePos = 0;
        }

        void OnExpAdaptPropertyChange(object sender, PropertyChangedEventArgs e)
        {
            HasChangedParams = true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ExpAdapt." + e.PropertyName));
        }

        void OnHgrmAdaptPropertyChange(object sender, PropertyChangedEventArgs e)
        {
            HasChangedParams = true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("HgrmAdapt." + e.PropertyName));
        }

        private struct AEParams
        {
            public _EXP exp_adapt;
            public _HGRM hgrm_adapt;
        };

        public EXP ExpAdapt
        {
            get { return _expAdapt; }
            set
            {
                _expAdapt = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("ExpAdapt");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }

        public HGRM HgrmAdapt
        {
            get { return _hgrmAdapt; }
            set
            {
                _hgrmAdapt = value;

                HasChangedParams = true;
                PropertyChangedEventArgs args = new PropertyChangedEventArgs("HgrmAdapt");
                if (PropertyChanged != null)
                    PropertyChanged(this, args);
            }
        }


        public override void ProcessRawBuffer(ref byte[] imgBuffer)
        {
            throw new NotImplementedException();
        }

        public override void ProcessRgbBuffer(ref byte[] imgBuffer)
        {
            throw new NotImplementedException();
        }

        public override Dictionary<int, byte[]> ParamsDataCollection
        {
            get
            {
                AEParams aeParam = new AEParams()
                {
                    exp_adapt = new _EXP(ExpAdapt),
                    hgrm_adapt = new _HGRM(HgrmAdapt)
                };

                int size = Marshal.SizeOf(aeParam);
                byte[] arr = new byte[size];

                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(aeParam, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                Marshal.FreeHGlobal(ptr);

                return new Dictionary<int, byte[]>() { { DeviceModulePos, arr } };
            }
            set
            {
                AEParams aeParam = new AEParams();

                int size = Marshal.SizeOf(aeParam);
                IntPtr ptr = Marshal.AllocHGlobal(size);

                Marshal.Copy(value[DeviceModulePos], 0, ptr, size);

                aeParam = (AEParams)Marshal.PtrToStructure(ptr, aeParam.GetType());
                Marshal.FreeHGlobal(ptr);

                ExpAdapt = new EXP(aeParam.exp_adapt);
                ExpAdapt.PropertyChanged += OnExpAdaptPropertyChange;

                HgrmAdapt = new HGRM(aeParam.hgrm_adapt);
                HgrmAdapt.PropertyChanged += OnHgrmAdaptPropertyChange;
            }
        }

        public override System.Xml.XmlElement SerializeToXmlElement(System.Xml.XmlDocument xmlDoc)
        {
            var xmlElement = xmlDoc.CreateElement("AE");

            XmlElement expTagNode = xmlDoc.CreateElement("ExpAdapt.exp_tag");
            string expTagStr = string.Join(",", ExpAdapt.exp_tag.Select(x => x.ToString()).ToArray());
            expTagNode.AppendChild(xmlDoc.CreateTextNode(expTagStr));
            xmlElement.AppendChild(expTagNode);

            XmlElement expAdjNode = xmlDoc.CreateElement("ExpAdapt.exp_adj");
            expAdjNode.AppendChild(xmlDoc.CreateTextNode(ExpAdapt.exp_adj.ToString()));
            xmlElement.AppendChild(expAdjNode);

            XmlElement darkWeightNode = xmlDoc.CreateElement("ExpAdapt.dark_weight");
            darkWeightNode.AppendChild(xmlDoc.CreateTextNode(ExpAdapt.dark_weight.ToString()));
            xmlElement.AppendChild(darkWeightNode);

            XmlElement lightWeightNode = xmlDoc.CreateElement("ExpAdapt.light_weight");
            lightWeightNode.AppendChild(xmlDoc.CreateTextNode(ExpAdapt.light_weight.ToString()));
            xmlElement.AppendChild(lightWeightNode);

            XmlElement expMinNode = xmlDoc.CreateElement("ExpAdapt.exp_min");
            expMinNode.AppendChild(xmlDoc.CreateTextNode(ExpAdapt.exp_min.ToString()));
            xmlElement.AppendChild(expMinNode);

            XmlElement gainMaxNode = xmlDoc.CreateElement("ExpAdapt.gain_max");
            gainMaxNode.AppendChild(xmlDoc.CreateTextNode(ExpAdapt.gain_max.ToString()));
            xmlElement.AppendChild(gainMaxNode);

            XmlElement expNumsNode = xmlDoc.CreateElement("ExpAdapt.exp_nums");
            expNumsNode.AppendChild(xmlDoc.CreateTextNode(ExpAdapt.exp_nums.ToString()));
            xmlElement.AppendChild(expNumsNode);

            // HgrmAdapt fields

            XmlElement aeWinX0Node = xmlDoc.CreateElement("HgrmAdapt.ae_win_x0");
            aeWinX0Node.AppendChild(xmlDoc.CreateTextNode(HgrmAdapt.ae_win_x0.ToString()));
            xmlElement.AppendChild(aeWinX0Node);

            XmlElement aeWinX1Node = xmlDoc.CreateElement("HgrmAdapt.ae_win_x1");
            aeWinX1Node.AppendChild(xmlDoc.CreateTextNode(HgrmAdapt.ae_win_x1.ToString()));
            xmlElement.AppendChild(aeWinX1Node);

            XmlElement aeWinX2Node = xmlDoc.CreateElement("HgrmAdapt.ae_win_x2");
            aeWinX2Node.AppendChild(xmlDoc.CreateTextNode(HgrmAdapt.ae_win_x2.ToString()));
            xmlElement.AppendChild(aeWinX2Node);

            XmlElement aeWinX3Node = xmlDoc.CreateElement("HgrmAdapt.ae_win_x3");
            aeWinX3Node.AppendChild(xmlDoc.CreateTextNode(HgrmAdapt.ae_win_x3.ToString()));
            xmlElement.AppendChild(aeWinX3Node);

            XmlElement aeWinY0Node = xmlDoc.CreateElement("HgrmAdapt.ae_win_y0");
            aeWinY0Node.AppendChild(xmlDoc.CreateTextNode(HgrmAdapt.ae_win_y0.ToString()));
            xmlElement.AppendChild(aeWinY0Node);

            XmlElement aeWinY1Node = xmlDoc.CreateElement("HgrmAdapt.ae_win_y1");
            aeWinY1Node.AppendChild(xmlDoc.CreateTextNode(HgrmAdapt.ae_win_y1.ToString()));
            xmlElement.AppendChild(aeWinY1Node);

            XmlElement aeWinY2Node = xmlDoc.CreateElement("HgrmAdapt.ae_win_y2");
            aeWinY2Node.AppendChild(xmlDoc.CreateTextNode(HgrmAdapt.ae_win_y2.ToString()));
            xmlElement.AppendChild(aeWinY2Node);

            XmlElement aeWinY3Node = xmlDoc.CreateElement("HgrmAdapt.ae_win_y3");
            aeWinY3Node.AppendChild(xmlDoc.CreateTextNode(HgrmAdapt.ae_win_y3.ToString()));
            xmlElement.AppendChild(aeWinY3Node);

            XmlElement weight07Node = xmlDoc.CreateElement("HgrmAdapt.weight_0_7");
            weight07Node.AppendChild(xmlDoc.CreateTextNode(HgrmAdapt.weight_0_7.ToString()));
            xmlElement.AppendChild(weight07Node);

            XmlElement weight815Node = xmlDoc.CreateElement("HgrmAdapt.weight_8_15");
            weight815Node.AppendChild(xmlDoc.CreateTextNode(HgrmAdapt.weight_8_15.ToString()));
            xmlElement.AppendChild(weight815Node);

            XmlElement weight1623Node = xmlDoc.CreateElement("HgrmAdapt.weight_16_23");
            weight1623Node.AppendChild(xmlDoc.CreateTextNode(HgrmAdapt.weight_16_23.ToString()));
            xmlElement.AppendChild(weight1623Node);

            XmlElement weight24Node = xmlDoc.CreateElement("HgrmAdapt.weight_24");
            weight24Node.AppendChild(xmlDoc.CreateTextNode(HgrmAdapt.weight_24.ToString()));
            xmlElement.AppendChild(weight24Node);

            return xmlElement;
        }

        public override void DeserializeFromXmlElement(System.Xml.XmlElement ispToolDataNode)
        {
            var AENode = ispToolDataNode["AE"];

            var tmpExpTagStr = XmlHelper.GetNodeValue(AENode, "ExpAdapt.exp_tag");
            if (tmpExpTagStr != null)
            {
                ExpAdapt.exp_tag = tmpExpTagStr.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToByte(s))
                    .ToArray();
            }

            ExpAdapt.ylog_cal_fnum = XmlHelper.GetNodeInt(AENode, "ExpAdapt.ylog_cal_fnum", 0);
            ExpAdapt.exp_adj = (byte)XmlHelper.GetNodeInt(AENode, "ExpAdapt.exp_adj", 0);
            ExpAdapt.dark_weight = (byte)XmlHelper.GetNodeInt(AENode, "ExpAdapt.dark_weight", 0);
            ExpAdapt.light_weight = (byte)XmlHelper.GetNodeInt(AENode, "ExpAdapt.light_weight", 0);
            ExpAdapt.exp_min = (byte)XmlHelper.GetNodeInt(AENode, "ExpAdapt.exp_min", 0);
            ExpAdapt.gain_max = XmlHelper.GetNodeInt(AENode, "ExpAdapt.gain_max", 0);
            ExpAdapt.exp_nums = XmlHelper.GetNodeInt(AENode, "ExpAdapt.exp_nums", 0);

            // HgrmAdapt fields
            HgrmAdapt.ae_win_x0 = XmlHelper.GetNodeShort(AENode, "HgrmAdapt.ae_win_x0", 0);
            HgrmAdapt.ae_win_x1 = XmlHelper.GetNodeShort(AENode, "HgrmAdapt.ae_win_x1", 0);
            HgrmAdapt.ae_win_x2 = XmlHelper.GetNodeShort(AENode, "HgrmAdapt.ae_win_x2", 0);
            HgrmAdapt.ae_win_x3 = XmlHelper.GetNodeShort(AENode, "HgrmAdapt.ae_win_x3", 0);
            HgrmAdapt.ae_win_y0 = XmlHelper.GetNodeShort(AENode, "HgrmAdapt.ae_win_y0", 0);
            HgrmAdapt.ae_win_y1 = XmlHelper.GetNodeShort(AENode, "HgrmAdapt.ae_win_y1", 0);
            HgrmAdapt.ae_win_y2 = XmlHelper.GetNodeShort(AENode, "HgrmAdapt.ae_win_y2", 0);
            HgrmAdapt.ae_win_y3 = XmlHelper.GetNodeShort(AENode, "HgrmAdapt.ae_win_y3", 0);
            HgrmAdapt.weight_0_7 = XmlHelper.GetNodeInt(AENode, "HgrmAdapt.weight_0_7", 0);
            HgrmAdapt.weight_8_15 = XmlHelper.GetNodeInt(AENode, "HgrmAdapt.weight_8_15", 0);
            HgrmAdapt.weight_16_23 = XmlHelper.GetNodeInt(AENode, "HgrmAdapt.weight_16_23", 0);
            HgrmAdapt.weight_24 = XmlHelper.GetNodeInt(AENode, "HgrmAdapt.weight_24", 0);


        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
