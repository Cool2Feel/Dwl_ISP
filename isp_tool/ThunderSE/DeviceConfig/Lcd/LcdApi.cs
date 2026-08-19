using System.Runtime.InteropServices;

namespace ThunderSE.DeviceConfig.Lcd
{
    class LcdApi
    {
        [DllImport("LcdApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void AntiSawtooth8(int sensorWidth, int lcdWidth, int strength, byte[] coef);

        [DllImport("LcdApi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern void AntiSawtooth4(int sensorHeight, int lcdHeight, int strength, byte[] coef);
    }
}
