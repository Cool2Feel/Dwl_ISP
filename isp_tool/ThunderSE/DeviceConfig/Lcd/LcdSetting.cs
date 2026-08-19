using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ThunderSE.DeviceConfig.Lcd
{
    
    public struct lcd_common_t
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public char[] name;
        public short screen_w;
        public short screen_h;
    };

    public struct lcd_lsawtooth_t 
    {
	    //int anti_lsawtooth[3][24];//0: all lcd  1:half lcd 2:small window
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3 * 24)]
        int[] anti_lsawtooth;//0: all lcd  1:half lcd 2:small window
    };

    public struct lcd_vde_t{
	    public int contrast;
	    public int brightness;
	    public int saturation;
    };

    public struct lcd_gamma_t{
	    public int contra_index;
	    public int gamma_red;
	    public int gamma_green;
	    public int gamma_blue;
    };

    public struct usb_lcddev_t
    {
	    public lcd_common_t lcd_common;
	    public lcd_vde_t lcd_vde;
	    public lcd_gamma_t lcd_gamma;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
	    public int[] de_ccm;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public int[] de_saj;
	    public lcd_lsawtooth_t  lcd_lsawtooth;
    };

    public enum LcdSection
    {
        LcdCommon,
        LcdVde,
        LcdGamma,
        LcdCcm,
        LcdSaj,
        LcdLsawtooth
    }


    public class LcdSetting
    {
        public Dictionary<LcdSection, LcdSettingSection> SettingSections =
            new Dictionary<LcdSection, LcdSettingSection>()
        {
            { LcdSection.LcdCommon, new LcdCommon() },
            { LcdSection.LcdVde, new LcdVde() },
            { LcdSection.LcdGamma, new LcdGamma() },
            { LcdSection.LcdCcm, new LcdCcm() },
            { LcdSection.LcdSaj, new LcdSaj() },
            { LcdSection.LcdLsawtooth, new LcdLsawtooth() },
        };
    }
}
