using System;
using System.IO;
using System.Text.Json;
using VentCalc.UI.ViewModels;

namespace VentCalc.UI.Services
{
    public sealed class VentCalcSettingsService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions { WriteIndented = true };

        public string SettingsPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VentCalc",
            "settings.json");

        public VentCalcSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return VentCalcSettings.CreateDefault();
                }

                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<VentCalcSettings>(json) ?? VentCalcSettings.CreateDefault();
            }
            catch (Exception)
            {
                return VentCalcSettings.CreateDefault();
            }
        }

        public void Save(VentCalcSettings settings)
        {
            string? directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, SerializerOptions));
        }
    }
}
