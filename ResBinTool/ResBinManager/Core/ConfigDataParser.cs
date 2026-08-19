using System;
using System.IO;
using ResBinManager.Models;

namespace ResBinManager.Core
{
    public class ConfigDataParser
    {
        private const int CONFIG_STRUCT_SIZE = (127 + 1) * 4;

        private byte[]? _firmwareData;
        private string? _filePath;
        
        public string? ErrorMessage { get; private set; }
        public bool IsLoaded { get; private set; }
        
        public uint ConfigAddress { get; private set; }
        public bool ConfigExists { get; private set; }

        public ConfigDataParser()
        {
            IsLoaded = false;
            ConfigExists = false;
            ConfigAddress = 0;
        }

        public bool LoadFromDestBin(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    ErrorMessage = $"File not found: {filePath}";
                    return false;
                }

                _firmwareData = File.ReadAllBytes(filePath);
                _filePath = filePath;
                IsLoaded = true;

                return ParseConfigAddressFromDestBin();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Load error: {ex.Message}";
                _firmwareData = null;
                IsLoaded = false;
                return false;
            }
        }

        public bool LoadFromResBin(string filePath, uint configAddressOffset)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    ErrorMessage = $"File not found: {filePath}";
                    return false;
                }

                _firmwareData = File.ReadAllBytes(filePath);
                _filePath = filePath;
                IsLoaded = true;
                ConfigAddress = configAddressOffset;

                return CheckConfigExists();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Load error: {ex.Message}";
                _firmwareData = null;
                IsLoaded = false;
                return false;
            }
        }

        public bool LoadFromData(byte[] data, uint configAddress)
        {
            try
            {
                if (data == null || data.Length == 0)
                {
                    ErrorMessage = "Data is null or empty";
                    return false;
                }

                _firmwareData = data;
                IsLoaded = true;
                ConfigAddress = configAddress;

                return CheckConfigExists();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Load error: {ex.Message}";
                _firmwareData = null;
                IsLoaded = false;
                return false;
            }
        }

        private bool ParseConfigAddressFromDestBin()
        {
            if (_firmwareData == null || _firmwareData.Length < 32)
            {
                ErrorMessage = "Firmware data too small";
                return false;
            }

            try
            {
                uint magic = BitConverter.ToUInt32(_firmwareData, 4);
                if (magic != 0x52444C42)
                {
                    ErrorMessage = "Invalid DestBin header (BLDR signature not found)";
                    return false;
                }

                byte bootSectorNum = _firmwareData[9];
                uint flashParamOffset = (uint)(bootSectorNum << 4);

                uint resSectorNum = BitConverter.ToUInt32(_firmwareData, (int)(flashParamOffset + 0x08));
                uint resBinOffset = resSectorNum << 9;

                uint resSizeSectors = BitConverter.ToUInt32(_firmwareData, (int)(flashParamOffset + 0x0C));
                uint resBinSize = resSizeSectors << 9;

                uint addr = resBinOffset + resBinSize;
                if ((addr & 0xFFF) != 0)
                {
                    addr = (addr & 0xFFFFF000) + 0x1000;
                }

                ConfigAddress = addr;
                System.Diagnostics.Debug.WriteLine($"[ConfigDataParser] Config address calculated: 0x{ConfigAddress:X}");

                return CheckConfigExists();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Parse error: {ex.Message}";
                return false;
            }
        }

        private bool CheckConfigExists()
        {
            if (_firmwareData == null)
                return false;

            if (ConfigAddress + CONFIG_STRUCT_SIZE > _firmwareData.Length)
            {
                ConfigExists = false;
                System.Diagnostics.Debug.WriteLine($"[ConfigDataParser] Config area beyond file size (0x{ConfigAddress:X} + {CONFIG_STRUCT_SIZE} > {_firmwareData.Length})");
                return true;
            }

            uint checkSumStored = BitConverter.ToUInt32(_firmwareData, (int)(ConfigAddress + 127 * 4));
            
            if (checkSumStored != 0 && checkSumStored != 0xFFFFFFFF)
            {
                ConfigExists = true;
                System.Diagnostics.Debug.WriteLine($"[ConfigDataParser] Config data exists at 0x{ConfigAddress:X} (checksum: 0x{checkSumStored:X8})");
            }
            else
            {
                ConfigExists = false;
                System.Diagnostics.Debug.WriteLine($"[ConfigDataParser] Config data not found or invalid at 0x{ConfigAddress:X}");
            }

            return true;
        }

        public bool ParseConfig(ConfigManager configManager)
        {
            if (!IsLoaded || _firmwareData == null)
            {
                ErrorMessage = "Firmware data not loaded";
                return false;
            }

            if (!ConfigExists)
            {
                System.Diagnostics.Debug.WriteLine("[ConfigDataParser] Config data not found, initializing defaults");
                configManager.InitializeDefaults();
                return true;
            }

            try
            {
                byte[] configData = new byte[CONFIG_STRUCT_SIZE];
                Array.Copy(_firmwareData, (int)ConfigAddress, configData, 0, CONFIG_STRUCT_SIZE);

                bool isValid = configManager.Deserialize(configData);
                configManager.GetConfigAddress(0, 0);
                
                if (isValid)
                {
                    System.Diagnostics.Debug.WriteLine("[ConfigDataParser] Config data parsed successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ConfigDataParser] Config checksum validation failed, initializing defaults");
                    configManager.InitializeDefaults();
                }

                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Parse config error: {ex.Message}";
                return false;
            }
        }

        public byte[]? ExtractConfigRawData()
        {
            if (!IsLoaded || _firmwareData == null || !ConfigExists)
            {
                return null;
            }

            try
            {
                byte[] configData = new byte[CONFIG_STRUCT_SIZE];
                Array.Copy(_firmwareData, (int)ConfigAddress, configData, 0, CONFIG_STRUCT_SIZE);
                return configData;
            }
            catch
            {
                return null;
            }
        }

        public string GetConfigInfo()
        {
            if (!IsLoaded)
                return "Not loaded";

            return $@"Config Data Parser Info:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Config Address:    0x{ConfigAddress:X8} ({ConfigAddress:N0} bytes)
Config Exists:     {(ConfigExists ? "Yes" : "No")}
Config Size:       {CONFIG_STRUCT_SIZE} bytes (127 flags + 1 checksum)
Firmware Size:     {(_firmwareData?.Length ?? 0):N0} bytes
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";
        }
    }
}