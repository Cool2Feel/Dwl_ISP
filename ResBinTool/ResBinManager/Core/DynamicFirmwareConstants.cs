using System;
using System.Collections.Generic;

namespace ResBinManager.Core
{
    public class DynamicFirmwareConstants
    {
        private readonly Dictionary<string, uint> _stringConstants;
        private readonly uint _rIdTypeStrBase;

        public DynamicFirmwareConstants(Dictionary<string, uint> stringConstants, uint rIdTypeStrBase)
        {
            _stringConstants = stringConstants ?? new Dictionary<string, uint>();
            _rIdTypeStrBase = rIdTypeStrBase;
        }

        public uint R_ID_TYPE_STR => _rIdTypeStrBase;

        public uint GetValue(string constantName)
        {
            if (_stringConstants.TryGetValue(constantName, out uint value))
                return value;

            if (_stringConstants.TryGetValue($"R_ID_STR_{constantName}", out uint valueWithPrefix))
                return valueWithPrefix;

            if (_stringConstants.TryGetValue($"R_STR_{constantName}", out uint valueWithRStrPrefix))
                return valueWithRStrPrefix;

            return _rIdTypeStrBase;
        }

        public uint GetValueOrDefault(string constantName, uint defaultValue)
        {
            if (_stringConstants.TryGetValue(constantName, out uint value))
                return value;

            if (_stringConstants.TryGetValue($"R_ID_STR_{constantName}", out uint valueWithPrefix))
                return valueWithPrefix;

            if (_stringConstants.TryGetValue($"R_STR_{constantName}", out uint valueWithRStrPrefix))
                return valueWithRStrPrefix;

            return defaultValue;
        }

        public bool TryGetValue(string constantName, out uint value)
        {
            if (_stringConstants.TryGetValue(constantName, out value))
                return true;

            if (_stringConstants.TryGetValue($"R_ID_STR_{constantName}", out value))
                return true;

            if (_stringConstants.TryGetValue($"R_STR_{constantName}", out value))
                return true;

            value = _rIdTypeStrBase;
            return false;
        }

        public uint R_STR_COM_OFF => GetValueOrDefault("R_ID_STR_COM_OFF", FirmwareConstants.R_STR_COM_OFF);
        public uint R_STR_COM_ON => GetValueOrDefault("R_ID_STR_COM_ON", FirmwareConstants.R_STR_COM_ON);
        public uint R_STR_COM_OK => GetValueOrDefault("R_ID_STR_COM_OK", FirmwareConstants.R_STR_COM_OK);
        public uint R_STR_COM_CANCEL => GetValueOrDefault("R_ID_STR_COM_CANCEL", FirmwareConstants.R_STR_COM_CANCEL);
        public uint R_STR_COM_YES => GetValueOrDefault("R_ID_STR_COM_YES", FirmwareConstants.R_STR_COM_YES);
        public uint R_STR_COM_NO => GetValueOrDefault("R_ID_STR_COM_NO", FirmwareConstants.R_STR_COM_NO);
        public uint R_STR_COM_LOW => GetValueOrDefault("R_ID_STR_COM_LOW", FirmwareConstants.R_STR_COM_LOW);
        public uint R_STR_COM_MIDDLE => GetValueOrDefault("R_ID_STR_COM_MIDDLE", FirmwareConstants.R_STR_COM_MIDDLE);
        public uint R_STR_COM_HIGH => GetValueOrDefault("R_ID_STR_COM_HIGH", FirmwareConstants.R_STR_COM_HIGH);
        public uint R_STR_COM_50HZ => GetValueOrDefault("R_ID_STR_COM_50HZ", FirmwareConstants.R_STR_COM_50HZ);
        public uint R_STR_COM_60HZ => GetValueOrDefault("R_ID_STR_COM_60HZ", FirmwareConstants.R_STR_COM_60HZ);

        public uint R_STR_COM_LEVEL_0 => GetValueOrDefault("R_ID_STR_COM_LEVEL_0", FirmwareConstants.R_STR_COM_LEVEL_0);
        public uint R_STR_COM_LEVEL_1 => GetValueOrDefault("R_ID_STR_COM_LEVEL_1", FirmwareConstants.R_STR_COM_LEVEL_1);
        public uint R_STR_COM_LEVEL_2 => GetValueOrDefault("R_ID_STR_COM_LEVEL_2", FirmwareConstants.R_STR_COM_LEVEL_2);
        public uint R_STR_COM_LEVEL_3 => GetValueOrDefault("R_ID_STR_COM_LEVEL_3", FirmwareConstants.R_STR_COM_LEVEL_3);
        public uint R_STR_COM_LEVEL_4 => GetValueOrDefault("R_ID_STR_COM_LEVEL_4", FirmwareConstants.R_STR_COM_LEVEL_4);
        public uint R_STR_COM_LEVEL_5 => GetValueOrDefault("R_ID_STR_COM_LEVEL_5", FirmwareConstants.R_STR_COM_LEVEL_5);
        public uint R_STR_COM_LEVEL_6 => GetValueOrDefault("R_ID_STR_COM_LEVEL_6", FirmwareConstants.R_STR_COM_LEVEL_6);

        public uint R_STR_COM_P4_0 => GetValueOrDefault("R_ID_STR_COM_P4_0", FirmwareConstants.R_STR_COM_P4_0);
        public uint R_STR_COM_P3_0 => GetValueOrDefault("R_ID_STR_COM_P3_0", FirmwareConstants.R_STR_COM_P3_0);
        public uint R_STR_COM_P2_0 => GetValueOrDefault("R_ID_STR_COM_P2_0", FirmwareConstants.R_STR_COM_P2_0);
        public uint R_STR_COM_P1_0 => GetValueOrDefault("R_ID_STR_COM_P1_0", FirmwareConstants.R_STR_COM_P1_0);
        public uint R_STR_COM_P0_0 => GetValueOrDefault("R_ID_STR_COM_P0_0", FirmwareConstants.R_STR_COM_P0_0);
        public uint R_STR_COM_N1_0 => GetValueOrDefault("R_ID_STR_COM_N1_0", FirmwareConstants.R_STR_COM_N1_0);
        public uint R_STR_COM_N2_0 => GetValueOrDefault("R_ID_STR_COM_N2_0", FirmwareConstants.R_STR_COM_N2_0);
        public uint R_STR_COM_N3_0 => GetValueOrDefault("R_ID_STR_COM_N3_0", FirmwareConstants.R_STR_COM_N3_0);

        public uint R_STR_LAN_ENGLISH => GetValueOrDefault("R_ID_STR_LAN_ENGLISH", FirmwareConstants.R_STR_LAN_ENGLISH);
        public uint R_STR_LAN_SCHINESE => GetValueOrDefault("R_ID_STR_LAN_SCHINESE", FirmwareConstants.R_STR_LAN_SCHINESE);
        public uint R_STR_LAN_TCHINESE => GetValueOrDefault("R_ID_STR_LAN_TCHINESE", FirmwareConstants.R_STR_LAN_TCHINESE);
        public uint R_STR_LAN_JAPANESE => GetValueOrDefault("R_ID_STR_LAN_JAPANESE", FirmwareConstants.R_STR_LAN_JAPANESE);
        public uint R_STR_LAN_GERMAN => GetValueOrDefault("R_ID_STR_LAN_GERMAN", FirmwareConstants.R_STR_LAN_GERMAN);
        public uint R_STR_LAN_FRECH => GetValueOrDefault("R_ID_STR_LAN_FRECH", FirmwareConstants.R_STR_LAN_FRECH);
        public uint R_STR_LAN_RUSSIAN => GetValueOrDefault("R_ID_STR_LAN_RUSSIAN", FirmwareConstants.R_STR_LAN_RUSSIAN);

        public uint R_STR_ISP_AUTO => GetValueOrDefault("R_ID_STR_ISP_AUTO", FirmwareConstants.R_STR_ISP_AUTO);
        public uint R_STR_ISP_SUNLIGHT => GetValueOrDefault("R_ID_STR_ISP_SUNLIGHT", FirmwareConstants.R_STR_ISP_SUNLIGHT);
        public uint R_STR_ISP_CLOUDY => GetValueOrDefault("R_ID_STR_ISP_CLOUDY", FirmwareConstants.R_STR_ISP_CLOUDY);
        public uint R_STR_ISP_TUNGSTEN => GetValueOrDefault("R_ID_STR_ISP_TUNGSTEN", FirmwareConstants.R_STR_ISP_TUNGSTEN);
        public uint R_STR_ISP_FLUORESCENT => GetValueOrDefault("R_ID_STR_ISP_FLUORESCENT", FirmwareConstants.R_STR_ISP_FLUORESCENT);
        public uint R_STR_ISP_RETRO => GetValueOrDefault("R_ID_STR_ISP_RETRO", FirmwareConstants.R_STR_ISP_RETRO);

        public uint R_STR_TIM_1MIN => GetValueOrDefault("R_ID_STR_TIM_1MIN", FirmwareConstants.R_STR_TIM_1MIN);
        public uint R_STR_TIM_2MIN => GetValueOrDefault("R_ID_STR_TIM_2MIN", FirmwareConstants.R_STR_TIM_2MIN);
        public uint R_STR_TIM_3MIN => GetValueOrDefault("R_ID_STR_TIM_3MIN", FirmwareConstants.R_STR_TIM_3MIN);
        public uint R_STR_TIM_5MIN => GetValueOrDefault("R_ID_STR_TIM_5MIN", FirmwareConstants.R_STR_TIM_5MIN);
        public uint R_STR_TIM_10MIN => GetValueOrDefault("R_ID_STR_TIM_10MIN", FirmwareConstants.R_STR_TIM_10MIN);

        public uint R_STR_SET_PRINT_DOT => GetValueOrDefault("R_ID_STR_SET_PRINT_DOT", FirmwareConstants.R_STR_SET_PRINT_DOT);
        public uint R_STR_SET_PRINT_GRAY => GetValueOrDefault("R_ID_STR_SET_PRINT_GRAY", FirmwareConstants.R_STR_SET_PRINT_GRAY);

        public uint R_STR_TIP_NEAR => GetValueOrDefault("R_ID_STR_TIP_NEAR", FirmwareConstants.R_STR_TIP_NEAR);
        public uint R_STR_TIP_MIDDLE => GetValueOrDefault("R_ID_STR_TIP_MIDDLE", FirmwareConstants.R_STR_TIP_MIDDLE);
        public uint R_STR_TIP_FAR => GetValueOrDefault("R_ID_STR_TIP_FAR", FirmwareConstants.R_STR_TIP_FAR);
    }
}