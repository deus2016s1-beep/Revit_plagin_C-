using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using VentCalc.UI.ViewModels;

namespace VentCalc.UI.Services
{
    public sealed class VentCalcSettingsService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions { WriteIndented = true };

        private VentCalcSettings? cachedSettings;

        public string SettingsPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VentCalc",
            "settings.json");

        public string LastWarning { get; private set; } = string.Empty;

        public VentCalcSettings Load()
        {
            if (cachedSettings != null)
            {
                return cachedSettings;
            }

            LastWarning = string.Empty;
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    cachedSettings = VentCalcSettings.CreateDefault();
                    Save(cachedSettings);
                    return cachedSettings;
                }

                string json = File.ReadAllText(SettingsPath);
                cachedSettings = JsonSerializer.Deserialize<VentCalcSettings>(json) ?? VentCalcSettings.CreateDefault();
                return cachedSettings;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception.ToString());
                LastWarning = "Файл настроек повреждён, использованы настройки по умолчанию.";
                TryRenameCorruptedSettings();
                cachedSettings = VentCalcSettings.CreateDefault();
                return cachedSettings;
            }
        }

        public void Save(VentCalcSettings settings)
        {
            try
            {
                string? directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, SerializerOptions));
                cachedSettings = settings;
                LastWarning = string.Empty;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception.ToString());
                LastWarning = $"Не удалось сохранить настройки: {exception.Message}";
            }
        }

        private void TryRenameCorruptedSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return;
                }

                string? directory = Path.GetDirectoryName(SettingsPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return;
                }

                string corruptedPath = Path.Combine(directory, $"settings_corrupted_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.Move(SettingsPath, corruptedPath);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception.ToString());
            }
        }
    }
}
