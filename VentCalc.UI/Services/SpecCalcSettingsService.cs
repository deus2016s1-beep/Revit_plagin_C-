using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VentCalc.UI.Services
{
    public sealed class SpecCalcSettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        public string SettingsPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VentCalc", "speccalc_settings.json");

        public SpecCalcSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    SpecCalcSettings? settings = JsonSerializer.Deserialize<SpecCalcSettings>(File.ReadAllText(SettingsPath));
                    if (settings != null)
                    {
                        EnsureDefaults(settings);
                        return settings;
                    }
                }
            }
            catch (Exception)
            {
            }

            var defaults = new SpecCalcSettings();
            EnsureDefaults(defaults);
            return defaults;
        }

        public void Save(SpecCalcSettings settings)
        {
            EnsureDefaults(settings);
            string? directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }

        public static void EnsureDefaults(SpecCalcSettings settings)
        {
            if (settings.Columns == null) settings.Columns = new List<SpecColumnLayout>();
            foreach (SpecColumnLayout column in CreateDefaultColumns())
            {
                if (!settings.Columns.Any(existing => existing.FieldName == column.FieldName)) settings.Columns.Add(column);
            }
            settings.Columns = settings.Columns.OrderBy(column => column.Order).ToList();
            if (string.IsNullOrWhiteSpace(settings.SelectedExcelProfile)) settings.SelectedExcelProfile = "Проектная спецификация";
        }

        public static IReadOnlyList<SpecColumnLayout> CreateDefaultColumns()
        {
            return new[]
            {
                Column("Section", "Раздел", 1, true, true),
                Column("Group", "Группа", 2, true, true),
                Column("Name", "Наименование", 3, true, true),
                Column("TypeMark", "Тип / марка", 4, true, true),
                Column("Size", "Размер", 5, true, true),
                Column("Unit", "Ед. изм.", 6, true, true),
                Column("Quantity", "Кол-во", 7, true, true, true, "0.##"),
                Column("LengthM", "Длина, м", 8, true, true, true, "0.00"),
                Column("AreaM2", "Площадь, м²", 9, true, true, true, "0.00"),
                Column("System", "Система", 10, false, false),
                Column("Level", "Уровень", 11, false, false),
                Column("Material", "Материал", 12, false, false),
                Column("Note", "Примечание", 13, true, true),
                Column("ImagePath", "Изображение", 14, false, false)
            };
        }

        private static SpecColumnLayout Column(string field, string header, int order, bool main, bool excel, bool numeric = false, string format = "")
        {
            return new SpecColumnLayout { FieldName = field, Header = header, Order = order, VisibleInMain = main, VisibleInExcel = excel, GroupBy = !numeric, Sum = numeric, IsNumeric = numeric, Format = format };
        }
    }
}
