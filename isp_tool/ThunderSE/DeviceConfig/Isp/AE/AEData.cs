using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ThunderSE.DeviceConfig.Isp
{
    public struct _EXP
    {
        public _EXP(EXP expClassObj)
        {
            //ylog_cal_fnum = expClassObj.ylog_cal_fnum;
            exp_tag = expClassObj.exp_tag;
            //exp_ext_mod = expClassObj.exp_ext_mod;
            exp_adj = expClassObj.exp_adj;
            dark_weight = expClassObj.dark_weight;
            light_weight = expClassObj.light_weight;
            exp_min = expClassObj.exp_min;
            gain_max = expClassObj.gain_max;
            exp_nums = expClassObj.exp_nums;
            //gain_max_save = expClassObj.gain_max_save;
        }
        //public int ylog_cal_fnum;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] exp_tag;
        //public byte exp_ext_mod;
        public byte exp_adj;
        public byte dark_weight;
        public byte light_weight;
        public byte exp_min;
        public int gain_max;
        public int exp_nums;
        //public int gain_max_save;
    };

    public struct _HGRM
    {
        public _HGRM(HGRM hgrmClassObject)
        {
            //allow_miss_dots = hgrmClassObject.allow_miss_dots;
            ae_win_x0 = hgrmClassObject.ae_win_x0;
            ae_win_x1 = hgrmClassObject.ae_win_x1;
            ae_win_x2 = hgrmClassObject.ae_win_x2;
            ae_win_x3 = hgrmClassObject.ae_win_x3;
            ae_win_y0 = hgrmClassObject.ae_win_y0;
            ae_win_y1 = hgrmClassObject.ae_win_y1;
            ae_win_y2 = hgrmClassObject.ae_win_y2;
            ae_win_y3 = hgrmClassObject.ae_win_y3;
            weight_0_7 = hgrmClassObject.weight_0_7;
            weight_8_15 = hgrmClassObject.weight_8_15;
            weight_16_23 = hgrmClassObject.weight_16_23;
            weight_24 = hgrmClassObject.weight_24;
            //hgrm_centre_weight = hgrmClassObject.hgrm_centre_weight;
            //hgrm_gray_weight = hgrmClassObject.hgrm_gray_weight;
        }
        //public int allow_miss_dots;
        public short ae_win_x0;
        public short ae_win_x1;
        public short ae_win_x2;
        public short ae_win_x3;
        public short ae_win_y0;
        public short ae_win_y1;
        public short ae_win_y2;
        public short ae_win_y3;
        public int weight_0_7;
        public int weight_8_15;
        public int weight_16_23;
        public int weight_24;
        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        //public int[] hgrm_centre_weight;
        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        //public int[] hgrm_gray_weight;
    };

    public class EXP : INotifyPropertyChanged
    {
        private int _ylog_cal_fnum;
        private byte[] _exp_tag = new byte[8];
        private int _exp_ext_mod;
        private byte _exp_adj;
        private byte _dark_weight;
        private byte _light_weight;
        private byte _exp_min;
        private int _gain_max;
        private int _exp_nums;
        private int _gain_max_save;

        public EXP()
        {

        }

        public EXP(_EXP expStructObj)
        {
            //ylog_cal_fnum = expStructObj.ylog_cal_fnum;
            exp_tag = expStructObj.exp_tag;
            //exp_ext_mod = expStructObj.exp_ext_mod;
            exp_adj = expStructObj.exp_adj;
            dark_weight = expStructObj.dark_weight;
            light_weight = expStructObj.light_weight;
            exp_min = expStructObj.exp_min;
            gain_max = expStructObj.gain_max;
            exp_nums = expStructObj.exp_nums;
            //gain_max_save = expStructObj.gain_max_save;
        }
        public int ylog_cal_fnum 
        {
            get { return _ylog_cal_fnum; } 
            set
            {
                _ylog_cal_fnum = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ylog_cal_fnum"));
                }
            }
        }
        public byte[] exp_tag
        {
            get { return _exp_tag; }
            set
            {
                _exp_tag = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("exp_tag"));
                }
            }
        }
        public int exp_ext_mod
        {
            get { return _exp_ext_mod; }
            set
            {
                _exp_ext_mod = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("exp_ext_mod"));
                }
            }
        }

        public byte exp_adj
        {
            get { return _exp_adj; }
            set
            {
                _exp_adj = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("exp_adj"));
                }
            }
        }
        public byte dark_weight
        {
            get { return _dark_weight; }
            set
            {
                _dark_weight = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("dark_weight"));
                }
            }
        }
        public byte light_weight
        {
            get { return _light_weight; }
            set
            {
                _light_weight = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("light_weight"));
                }
            }
        }

        public byte exp_min
        {
            get { return _exp_min; }
            set
            {
                _exp_min = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("exp_min"));
                }
            }
        }
        public int gain_max
        {
            get { return _gain_max; }
            set
            {
                _gain_max = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("gain_max"));
                }
            }
        }

        public int exp_nums
        {
            get { return _exp_nums; }
            set
            {
                _exp_nums = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("exp_nums"));
                }
            }
        }

        public int gain_max_save
        {
            get { return _gain_max_save; }
            set
            {
                _gain_max_save = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("gain_max_save"));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    };

    public class HGRM : INotifyPropertyChanged
    {
        private int _allow_miss_dots;
        private short _ae_win_x0;
        private short _ae_win_x1;
        private short _ae_win_x2;
        private short _ae_win_x3;
        private short _ae_win_y0;
        private short _ae_win_y1;
        private short _ae_win_y2;
        private short _ae_win_y3;
        private int _weight_0_7;
        private int _weight_8_15;
        private int _weight_16_23;
        private int _weight_24;
        private int[] _hgrm_centre_weight = new int[8];
        private int[] _hgrm_gray_weight = new int[8];

        public HGRM()
        {

        }

        public HGRM(_HGRM hgrmStructObj)
        {
            //allow_miss_dots = hgrmStructObj.allow_miss_dots;
            ae_win_x0 = hgrmStructObj.ae_win_x0;
            ae_win_x1 = hgrmStructObj.ae_win_x1;
            ae_win_x2 = hgrmStructObj.ae_win_x2;
            ae_win_x3 = hgrmStructObj.ae_win_x3;
            ae_win_y0 = hgrmStructObj.ae_win_y0;
            ae_win_y1 = hgrmStructObj.ae_win_y1;
            ae_win_y2 = hgrmStructObj.ae_win_y2;
            ae_win_y3 = hgrmStructObj.ae_win_y3;
            weight_0_7 = hgrmStructObj.weight_0_7;
            weight_8_15 = hgrmStructObj.weight_8_15;
            weight_16_23 = hgrmStructObj.weight_16_23;
            weight_24 = hgrmStructObj.weight_24;
            //hgrm_centre_weight = hgrmStructObj.hgrm_centre_weight;
            //hgrm_gray_weight = hgrmStructObj.hgrm_gray_weight;
        }

        public int allow_miss_dots
        {
            get { return _allow_miss_dots; }
            set
            {
                _allow_miss_dots = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("allow_miss_dots"));
                }
            }
        }
        public short ae_win_x0
        {
            get { return _ae_win_x0; }
            set
            {
                _ae_win_x0 = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ae_win_x0"));
                }
            }
        }
        public short ae_win_x1
        {
            get { return _ae_win_x1; }
            set
            {
                _ae_win_x1 = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ae_win_x1"));
                }
            }
        }
        public short ae_win_x2
        {
            get { return _ae_win_x2; }
            set
            {
                _ae_win_x2 = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ae_win_x2"));
                }
            }
        }
        public short ae_win_x3
        {
            get { return _ae_win_x3; }
            set
            {
                _ae_win_x3 = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ae_win_x3"));
                }
            }
        }
        public short ae_win_y0
        {
            get { return _ae_win_y0; }
            set
            {
                _ae_win_y0 = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ae_win_y0"));
                }
            }
        }
        public short ae_win_y1
        {
            get { return _ae_win_y1; }
            set
            {
                _ae_win_y1 = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ae_win_y1"));
                }
            }
        }
        public short ae_win_y2
        {
            get { return _ae_win_y2; }
            set
            {
                _ae_win_y2 = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ae_win_y2"));
                }
            }
        }
        public short ae_win_y3
        {
            get { return _ae_win_y3; }
            set
            {
                _ae_win_y3 = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("ae_win_y3"));
                }
            }
        }
        public int weight_0_7
        {
            get { return _weight_0_7; }
            set
            {
                _weight_0_7 = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("weight_0_7"));
                }
            }
        }
        public int weight_8_15
        {
            get { return _weight_8_15; }
            set
            {
                _weight_8_15 = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("weight_8_15"));
                }
            }
        }
        public int weight_16_23
        {
            get { return _weight_16_23; }
            set
            {
                _weight_16_23 = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("weight_16_23"));
                }
            }
        }
        public int weight_24
        {
            get { return _weight_24; }
            set
            {
                _weight_24 = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("weight_24"));
                }
            }
        }
        public int[] hgrm_centre_weight
        {
            get { return _hgrm_centre_weight; }
            set
            {
                _hgrm_centre_weight = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("hgrm_centre_weight"));
                }
            }
        }
        public int[] hgrm_gray_weight
        {
            get { return _hgrm_gray_weight; }
            set
            {
                _hgrm_gray_weight = value;
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs("hgrm_gray_weight"));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    };
}