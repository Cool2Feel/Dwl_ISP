using System;
using System.IO;

namespace ResBinManager.Core
{
    public class ConfigDataWriter
    {
        private const int CONFIG_STRUCT_SIZE = (127 + 1) * 4;
        private const int FLASH_SECTOR_SIZE = 4096;

        private byte[]? _firmwareData;
        private string? _filePath;
        private uint _configAddress;

        public string? ErrorMessage { get; private set; }

        public ConfigDataWriter()
        {
            _configAddress = 0;
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

                if (!ParseConfigAddressFromDestBin())
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Load error: {ex.Message}";
                _firmwareData = null;
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
                _configAddress = configAddress;

                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Load error: {ex.Message}";
                _firmwareData = null;
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
                    ErrorMessage = "Invalid DestBin header";
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

                _configAddress = addr;
                System.Diagnostics.Debug.WriteLine($"[ConfigDataWriter] Config address: 0x{_configAddress:X}");

                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Parse error: {ex.Message}";
                return false;
            }
        }

        public bool WriteConfig(ConfigManager configManager)
        {
            if (_firmwareData == null)
            {
                ErrorMessage = "Firmware data not loaded";
                return false;
            }

            try
            {
                byte[] configData = configManager.Serialize();

                if (_configAddress + CONFIG_STRUCT_SIZE > _firmwareData.Length)
                {
                    int newSize = (int)(_configAddress + CONFIG_STRUCT_SIZE);
                    if ((newSize & 0xFFF) != 0)
                    {
                        newSize = (int)((uint)newSize & 0xFFFFF000) + 0x1000;
                    }

                    Array.Resize(ref _firmwareData, newSize);
                    System.Diagnostics.Debug.WriteLine($"[ConfigDataWriter] Extended firmware size to {_firmwareData.Length} bytes");
                }

                Array.Copy(configData, 0, _firmwareData, (int)_configAddress, CONFIG_STRUCT_SIZE);

                System.Diagnostics.Debug.WriteLine($"[ConfigDataWriter] Config written to 0x{_configAddress:X}");
                System.Diagnostics.Debug.WriteLine($"[ConfigDataWriter] Checksum: 0x{configManager.CalculateCheckSum():X8}");

                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Write error: {ex.Message}";
                return false;
            }
        }

        public bool WriteConfigDirect(byte[] configData)
        {
            if (_firmwareData == null)
            {
                ErrorMessage = "Firmware data not loaded";
                return false;
            }

            if (configData == null || configData.Length < CONFIG_STRUCT_SIZE)
            {
                ErrorMessage = "Invalid config data size";
                return false;
            }

            try
            {
                if (_configAddress + CONFIG_STRUCT_SIZE > _firmwareData.Length)
                {
                    int newSize = (int)(_configAddress + CONFIG_STRUCT_SIZE);
                    if ((newSize & 0xFFF) != 0)
                    {
                        newSize = (int)((uint)newSize & 0xFFFFF000) + 0x1000;
                    }

                    Array.Resize(ref _firmwareData, newSize);
                    System.Diagnostics.Debug.WriteLine($"[ConfigDataWriter] Extended firmware size to {_firmwareData.Length} bytes");
                }

                Array.Copy(configData, 0, _firmwareData, (int)_configAddress, CONFIG_STRUCT_SIZE);

                uint checkSum = BitConverter.ToUInt32(configData, 127 * 4);
                System.Diagnostics.Debug.WriteLine($"[ConfigDataWriter] Config written to 0x{_configAddress:X}");
                System.Diagnostics.Debug.WriteLine($"[ConfigDataWriter] Checksum: 0x{checkSum:X8}");

                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Write error: {ex.Message}";
                return false;
            }
        }

        public bool Save(string outputPath)
        {
            if (_firmwareData == null)
            {
                ErrorMessage = "Firmware data not loaded";
                return false;
            }

            try
            {
                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(outputPath, _firmwareData);

                System.Diagnostics.Debug.WriteLine($"[ConfigDataWriter] Saved to: {outputPath} ({_firmwareData.Length} bytes)");
                ErrorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Save error: {ex.Message}";
                return false;
            }
        }

        public byte[]? GetModifiedData()
        {
            return _firmwareData;
        }

        public uint ConfigAddress => _configAddress;

        public int FirmwareSize => _firmwareData?.Length ?? 0;
    }
}