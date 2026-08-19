using ResBinManager.Models;
using System;
using System.IO;
#if NET40
using Newtonsoft.Json;
#else
using System.Text.Json;
#endif

namespace ResBinManager.Core
{
    public static class AppSettingsManager
    {
        private static readonly string SettingsFileName = "appsettings.json";
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ResBinManager",
            SettingsFileName);

        public static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
#if NET40
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
#else
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
#endif
                    return settings ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] Load failed: {ex.Message}");
            }
            return new AppSettings();
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                var settingsDir = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(settingsDir) && !Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }

#if NET40
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
#else
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
#endif
                File.WriteAllText(SettingsFilePath, json);
                System.Diagnostics.Debug.WriteLine($"[AppSettings] Saved to: {SettingsFilePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] Save failed: {ex.Message}");
            }
        }
    }

    public class AppSettings
    {
        public ConfigTemplateId SelectedConfigTemplate { get; set; } = ConfigTemplateId.Default;
        public string LastOpenedFilePath { get; set; }
        public string LastOutputDirectory { get; set; }
        public DateTime LastSaveTime { get; set; } = DateTime.Now;
    }
}
