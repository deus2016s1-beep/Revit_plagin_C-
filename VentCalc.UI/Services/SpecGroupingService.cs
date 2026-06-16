using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace VentCalc.UI.Services
{
    public static class SpecGroupingService
    {
        public static IReadOnlyList<SpecGroupRow> Group(IEnumerable<SpecItemRow> items, IReadOnlyList<SpecColumnLayout>? layouts = null, SpecCalcSettings? settings = null)
        {
            SpecCalcSettings effectiveSettings = settings ?? new SpecCalcSettings { Columns = layouts?.ToList() ?? SpecCalcSettingsService.CreateDefaultColumns().ToList() };
            SpecCalcSettingsService.EnsureDefaults(effectiveSettings);
            return items
                .GroupBy(CreateKey)
                .Select(group => ToGroupRow(group, effectiveSettings))
                .OrderBy(row => row.Section)
                .ThenBy(row => row.Group)
                .ThenBy(row => row.Name)
                .ThenBy(row => row.Size)
                .ToList();
        }

        private static SpecGroupingKey CreateKey(SpecItemRow item)
        {
            bool duct = item.Group == "Воздуховоды" || item.Group == "Гибкие воздуховоды";
            return new SpecGroupingKey(
                item.Section,
                item.Group,
                item.Name,
                duct ? string.Empty : CleanTypeMark(item.TypeMark),
                item.Size,
                item.Unit,
                item.Note);
        }

        private static SpecGroupRow ToGroupRow(IGrouping<SpecGroupingKey, SpecItemRow> group, SpecCalcSettings settings)
        {
            string size = group.Key.Size;
            string name = group.Key.Name;
            string unit = group.Key.Unit;
            string note = group.Key.Note;
            double quantity = group.Sum(item => item.Quantity);
            double length = Math.Round(group.Sum(item => item.LengthM), 2);
            double area = Math.Round(group.Sum(item => item.AreaM2), 2);

            ApplyDuctQuantityMode(settings.DuctQuantityMode, group.Key.Group, ref unit, ref quantity, length, area);

            var row = new SpecGroupRow
            {
                Section = group.Key.Section,
                Group = group.Key.Group,
                Name = name,
                TypeMark = group.Key.TypeMark,
                Size = size,
                Unit = unit,
                Quantity = Math.Round(quantity, IsCountUnit(unit) ? 0 : 2),
                LengthM = length,
                AreaM2 = area,
                System = Collapse(group.Select(item => item.System)),
                Level = Collapse(group.Select(item => item.Level)),
                Material = Collapse(group.Select(item => item.Material)),
                Code = Collapse(group.Select(item => item.AdskCode)),
                Note = note,
                Status = group.Any(item => item.Status == "Error") ? "Error" : group.Any(item => item.Status == "Warning") ? "Warning" : "OK",
                Source = group.Any(item => item.Source == "ManualRule") ? "ManualRule" : "Auto",
                IsManual = group.Any(item => item.Source == "ManualRule"),
                ImagePath = SafeGetImagePath(group)
            };

            foreach (SpecItemRow item in group)
            {
                row.SourceItems.Add(item);
                if (item.ElementId > 0) row.ElementIds.Add(item.ElementId);
                if (!string.IsNullOrWhiteSpace(item.UniqueId)) row.UniqueIds.Add(item.UniqueId);
            }

            return row;
        }

        private static void ApplyDuctQuantityMode(string mode, string group, ref string unit, ref double quantity, double length, double area)
        {
            if (group != "Воздуховоды") return;
            if (mode == "Длина, м")
            {
                unit = "м";
                quantity = length;
            }
            else
            {
                unit = "м²";
                quantity = area;
            }
        }

        private static void ApplyNameRule(SpecCalcSettings settings, string group, SpecItemRow? representative, ref string name, ref string size, ref string unit, ref string note)
        {
            if (representative?.Source == "ManualRule") return;
            SpecNameRule? rule = FindRule(settings, group, name);
            if (rule == null || !rule.Enabled) return;
            string cleanSize = NormalizeCompositeSize(size);
            string newName = ApplyTemplate(rule.NameTemplate, representative, name, cleanSize);
            string newSize = ApplyTemplate(rule.SizeTemplate, representative, name, cleanSize);
            string newUnit = ApplyTemplate(rule.Unit, representative, name, cleanSize);
            string newNote = ApplyTemplate(rule.NoteTemplate, representative, name, cleanSize);
            if (!string.IsNullOrWhiteSpace(newName)) name = StripSizeFromName(newName, cleanSize);
            if (!string.IsNullOrWhiteSpace(newSize)) size = NormalizeCompositeSize(newSize);
            if (!string.IsNullOrWhiteSpace(newUnit)) unit = newUnit;
            if (!string.IsNullOrWhiteSpace(newNote)) note = newNote;
        }

        private static SpecNameRule? FindRule(SpecCalcSettings settings, string group, string name)
        {
            string specific = name.Contains("Отвод", StringComparison.OrdinalIgnoreCase) ? "Отводы"
                : name.Contains("Переход", StringComparison.OrdinalIgnoreCase) ? "Переходы"
                : name.Contains("Тройник", StringComparison.OrdinalIgnoreCase) ? "Тройники"
                : name.Contains("Врез", StringComparison.OrdinalIgnoreCase) ? "Врезки"
                : name.Contains("Заглуш", StringComparison.OrdinalIgnoreCase) ? "Заглушки"
                : name.Contains("Клапан", StringComparison.OrdinalIgnoreCase) ? "Клапаны"
                : group;
            return settings.NameRules.FirstOrDefault(rule => string.Equals(rule.Group, specific, StringComparison.OrdinalIgnoreCase))
                ?? settings.NameRules.FirstOrDefault(rule => string.Equals(rule.Group, group, StringComparison.OrdinalIgnoreCase));
        }

        private static string ApplyTemplate(string template, SpecItemRow? item, string name, string size)
        {
            if (string.IsNullOrWhiteSpace(template)) return string.Empty;
            string shape = size.StartsWith("Ø", StringComparison.OrdinalIgnoreCase) ? "круглый" : size.Contains('×') ? "прямоугольный" : string.Empty;
            string angle = Regex.Match(name, @"(15|30|45|60|90)").Value;
            string type = item?.TypeMark == "—" ? string.Empty : item?.TypeMark ?? string.Empty;
            return template
                .Replace("{форма}", shape)
                .Replace("{размер}", size)
                .Replace("{размер_чистый}", NormalizeCompositeSize(size))
                .Replace("{материал}", item?.Material ?? string.Empty)
                .Replace("{угол}", angle)
                .Replace("{тип}", string.IsNullOrWhiteSpace(type) ? name : type)
                .Replace("{марка}", type)
                .Replace("{система}", item?.System ?? string.Empty)
                .Replace("{уровень}", item?.Level ?? string.Empty)
                .Trim();
        }

        public static string NormalizeCompositeSize(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "—") return value;
            string normalized = value.Trim().Replace('х', '×').Replace('Х', '×').Replace('x', '×').Replace('X', '×').Replace('*', '×');
            string[] parts = Regex.Split(normalized, @"\s*(?:-|/|\\)\s*").Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim()).ToArray();
            if (parts.Length <= 1) return normalized;
            string[] distinct = parts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return distinct.Length == 1 ? distinct[0] : string.Join(" / ", distinct.Take(3));
        }

        public static string StripSizeFromName(string name, string size)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(size) || size == "—") return name;
            string result = name.Trim();
            foreach (string token in new[] { size, size.Replace("Ø", "ø"), size.Replace("Ø", "ф"), size.Replace('×', 'x'), size.Replace('×', 'х') }.Distinct())
            {
                if (string.IsNullOrWhiteSpace(token)) continue;
                result = Regex.Replace(result, @"\s*[,;/-]?\s*" + Regex.Escape(token) + @"\s*$", string.Empty, RegexOptions.IgnoreCase);
            }
            result = Regex.Replace(result, @"\s*[,;/-]?\s*(?:Ø|ø|ф|DN|D)?\s*\d{2,4}(?:\s*[xх×*]\s*\d{2,4}){0,2}\s*$", string.Empty, RegexOptions.IgnoreCase);
            return result.Trim();
        }

        private static string SafeGetImagePath(IGrouping<SpecGroupingKey, SpecItemRow> group)
        {
            try
            {
                string preview = group.Select(item => item.ImagePath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path)) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(preview)) return preview;
                return SpecImageService.GetFallbackImagePath(group.Key.Group, group.Key.Name, group.Key.Size, group.Key.TypeMark);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static bool IsCountUnit(string unit) => string.Equals(unit, "шт", StringComparison.OrdinalIgnoreCase);

        private static string Collapse(IEnumerable<string> values)
        {
            string[] distinct = values.Where(value => !string.IsNullOrWhiteSpace(value) && value != "—").Distinct().Take(3).ToArray();
            return distinct.Length == 0 ? "—" : string.Join(", ", distinct);
        }

        private static string CleanTypeMark(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "—") return string.Empty;
            return value.Contains("ADSK_Оцинковка", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Оцинковка_", StringComparison.OrdinalIgnoreCase)
                || value.Contains("ГОСТ 14918", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : value.Trim();
        }

        private readonly record struct SpecGroupingKey(string Section, string Group, string Name, string TypeMark, string Size, string Unit, string Note);
    }
}
