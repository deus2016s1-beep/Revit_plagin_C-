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
        private static readonly HashSet<string> KnownFields = new HashSet<string>(CreateDefaultColumns().Select(column => column.FieldName), StringComparer.Ordinal);
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
                TryMoveBadSettingsFile();
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
            bool hadStoredColumns = settings.Columns.Count > 0;
            settings.Columns = settings.Columns
                .Where(column => column != null && KnownFields.Contains(column.FieldName))
                .GroupBy(column => column.FieldName)
                .Select(group => group.First())
                .ToList();

            IReadOnlyList<SpecColumnLayout> defaults = CreateDefaultColumns();
            foreach (SpecColumnLayout column in defaults)
            {
                if (!settings.Columns.Any(existing => existing.FieldName == column.FieldName)) settings.Columns.Add(column);
            }

            foreach (SpecColumnLayout column in settings.Columns)
            {
                SpecColumnLayout defaultColumn = defaults.First(defaultItem => defaultItem.FieldName == column.FieldName);
                if (string.IsNullOrWhiteSpace(column.Header)) column.Header = defaultColumn.Header;
                if (column.Order <= 0) column.Order = defaultColumn.Order;
                if (string.IsNullOrWhiteSpace(column.Format)) column.Format = defaultColumn.Format;
                column.IsNumeric = defaultColumn.IsNumeric;
            }

            if (!settings.Columns.Any(column => column.VisibleInMain))
            {
                foreach (SpecColumnLayout column in defaults.Where(column => column.VisibleInMain))
                {
                    SpecColumnLayout target = settings.Columns.First(existing => existing.FieldName == column.FieldName);
                    target.VisibleInMain = true;
                }
            }

            if (!settings.Columns.Any(column => column.VisibleInExcel))
            {
                foreach (SpecColumnLayout column in defaults.Where(column => column.VisibleInExcel))
                {
                    SpecColumnLayout target = settings.Columns.First(existing => existing.FieldName == column.FieldName);
                    target.VisibleInExcel = true;
                }
            }

            settings.Columns = settings.Columns.OrderBy(column => column.Order).ToList();
            if (string.IsNullOrWhiteSpace(settings.SelectedExcelProfile)) settings.SelectedExcelProfile = "Рабочий Excel";
            if (!VisibleExportProfiles.Contains(settings.SelectedExcelProfile)) settings.SelectedExcelProfile = "Рабочий Excel";
            if (string.IsNullOrWhiteSpace(settings.DuctQuantityMode)) settings.DuctQuantityMode = "Площадь, м²";
            if (settings.DuctQuantityMode != "Площадь, м²" && settings.DuctQuantityMode != "Длина, м" && settings.DuctQuantityMode != "Площадь и длина") settings.DuctQuantityMode = "Площадь, м²";
            settings.ProfileColumns ??= new Dictionary<string, List<SpecColumnLayout>>();
            EnsureProfileColumns(settings, hadStoredColumns);
            settings.NameRules ??= new List<SpecNameRule>();
            EnsureNameRules(settings);
        }

        public static IReadOnlyList<string> VisibleExportProfiles { get; } = new[] { "Рабочий Excel", "Визуальная спецификация", "Ведомость А3" };

        public static List<SpecColumnLayout> CreateDefaultColumnsForProfile(string profile)
        {
            IReadOnlyList<SpecColumnLayout> defaults = CreateDefaultColumns();
            string[] fields = profile == "Визуальная спецификация"
                ? new[] { "Name", "Size", "Unit", "Quantity", "ImagePath" }
                : profile == "Ведомость А3"
                    ? new[] { "Name", "TypeMark", "Unit", "Quantity", "Note" }
                    : new[] { "Name", "Size", "Unit", "Quantity", "LengthM", "AreaM2" };
            var result = new List<SpecColumnLayout>();
            for (int i = 0; i < fields.Length; i++)
            {
                SpecColumnLayout source = defaults.First(column => column.FieldName == fields[i]);
                SpecColumnLayout clone = CloneColumn(source);
                clone.Order = i + 1;
                clone.VisibleInMain = clone.FieldName != "ImagePath";
                clone.VisibleInExcel = true;
                result.Add(clone);
            }
            foreach (SpecColumnLayout source in defaults.Where(column => !fields.Contains(column.FieldName)))
            {
                SpecColumnLayout clone = CloneColumn(source);
                clone.VisibleInMain = false;
                clone.VisibleInExcel = false;
                result.Add(clone);
            }
            return result.OrderBy(column => column.Order).ToList();
        }

        private static void EnsureProfileColumns(SpecCalcSettings settings, bool useLegacyColumns)
        {
            foreach (string profile in VisibleExportProfiles)
            {
                List<SpecColumnLayout> seed = settings.ProfileColumns.TryGetValue(profile, out List<SpecColumnLayout>? existing) && existing.Count > 0
                    ? existing
                    : useLegacyColumns && profile == settings.SelectedExcelProfile && settings.Columns.Count > 0 ? settings.Columns.Select(CloneColumn).ToList() : CreateDefaultColumnsForProfile(profile);
                settings.ProfileColumns[profile] = NormalizeColumns(seed);
            }
            settings.Columns = settings.ProfileColumns[settings.SelectedExcelProfile].Select(CloneColumn).ToList();
        }

        private static List<SpecColumnLayout> NormalizeColumns(IEnumerable<SpecColumnLayout> columns)
        {
            var temp = new SpecCalcSettings { Columns = columns.Select(CloneColumn).ToList(), SelectedExcelProfile = "Рабочий Excel" };
            temp.NameRules = new List<SpecNameRule>();
            if (temp.Columns.Count == 0) temp.Columns = CreateDefaultColumnsForProfile("Рабочий Excel");
            temp.Columns = temp.Columns
                .Where(column => column != null && KnownFields.Contains(column.FieldName))
                .GroupBy(column => column.FieldName)
                .Select(group => group.First())
                .ToList();
            foreach (SpecColumnLayout column in CreateDefaultColumns())
            {
                if (!temp.Columns.Any(existing => existing.FieldName == column.FieldName))
                {
                    SpecColumnLayout clone = CloneColumn(column);
                    clone.VisibleInMain = false;
                    clone.VisibleInExcel = false;
                    temp.Columns.Add(clone);
                }
            }
            foreach (SpecColumnLayout column in temp.Columns)
            {
                SpecColumnLayout defaultColumn = CreateDefaultColumns().First(item => item.FieldName == column.FieldName);
                if (string.IsNullOrWhiteSpace(column.Header)) column.Header = defaultColumn.Header;
                if (column.Order <= 0) column.Order = defaultColumn.Order;
                if (string.IsNullOrWhiteSpace(column.Format)) column.Format = defaultColumn.Format;
                column.IsNumeric = defaultColumn.IsNumeric;
            }
            return temp.Columns.OrderBy(column => column.Order).ToList();
        }

        public static SpecColumnLayout CloneColumn(SpecColumnLayout column)
        {
            return new SpecColumnLayout
            {
                FieldName = column.FieldName,
                Header = column.Header,
                Format = column.Format,
                IsNumeric = column.IsNumeric,
                VisibleInMain = column.VisibleInMain,
                VisibleInExcel = column.VisibleInExcel,
                GroupBy = column.GroupBy,
                Sum = column.Sum,
                Order = column.Order
            };
        }

        public static IReadOnlyList<SpecNameRule> CreateDefaultNameRules()
        {
            return new[]
            {
                Rule("Воздуховоды", "Воздуховод {форма} из оцинкованной стали", "{размер}", string.Empty),
                Rule("Гибкие воздуховоды", "Гибкий воздуховод", "{размер}", "м"),
                Rule("Фасонные части", "{тип}", "{размер_чистый}", "шт"),
                Rule("Врезки", "Врезка", "{размер_чистый}", "шт"),
                Rule("Отводы", "Отвод {угол}°", "{размер_чистый}", "шт"),
                Rule("Переходы", "Переход", "{размер_чистый}", "шт"),
                Rule("Тройники", "Тройник", "{размер_чистый}", "шт"),
                Rule("Заглушки", "Заглушка", "{размер_чистый}", "шт"),
                Rule("Воздухораспределители", "Решётка вентиляционная", "{размер}", "шт"),
                Rule("Клапаны", "Клапан", "{размер}", "шт"),
                Rule("Зонты", "Зонт вытяжной", "{размер}", "шт"),
                Rule("Оборудование", "{тип}", "{размер}", "шт")
            };
        }

        private static void EnsureNameRules(SpecCalcSettings settings)
        {
            foreach (SpecNameRule rule in CreateDefaultNameRules())
            {
                if (!settings.NameRules.Any(existing => string.Equals(existing.Group, rule.Group, StringComparison.OrdinalIgnoreCase))) settings.NameRules.Add(rule);
            }
        }

        private static SpecNameRule Rule(string group, string nameTemplate, string sizeTemplate, string unit)
        {
            return new SpecNameRule { Group = group, NameTemplate = nameTemplate, SizeTemplate = sizeTemplate, Unit = unit, Enabled = true };
        }

        private void TryMoveBadSettingsFile()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                string badPath = Path.Combine(Path.GetDirectoryName(SettingsPath) ?? string.Empty, "speccalc_settings.bad.json");
                if (File.Exists(badPath)) File.Delete(badPath);
                File.Move(SettingsPath, badPath);
            }
            catch (Exception)
            {
                // Ignore settings recovery failures and continue with defaults.
            }
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
