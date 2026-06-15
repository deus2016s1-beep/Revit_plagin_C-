using System;
using System.Collections.Generic;
using System.Linq;

namespace VentCalc.UI.Services
{
    public static class SpecGroupingService
    {
        public static IReadOnlyList<SpecGroupRow> Group(IEnumerable<SpecItemRow> items, IReadOnlyList<SpecColumnLayout>? layouts = null)
        {
            return items
                .GroupBy(CreateKey)
                .Select(ToGroupRow)
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

        private static SpecGroupRow ToGroupRow(IGrouping<SpecGroupingKey, SpecItemRow> group)
        {
            var row = new SpecGroupRow
            {
                Section = group.Key.Section,
                Group = group.Key.Group,
                Name = group.Key.Name,
                TypeMark = group.Key.TypeMark,
                Size = group.Key.Size,
                Unit = group.Key.Unit,
                Quantity = Math.Round(group.Sum(item => item.Quantity), IsCountUnit(group.Key.Unit) ? 0 : 2),
                LengthM = Math.Round(group.Sum(item => item.LengthM), 2),
                AreaM2 = Math.Round(group.Sum(item => item.AreaM2), 2),
                System = Collapse(group.Select(item => item.System)),
                Level = Collapse(group.Select(item => item.Level)),
                Material = Collapse(group.Select(item => item.Material)),
                Note = group.Key.Note,
                Status = group.Any(item => item.Status == "Error") ? "Error" : group.Any(item => item.Status == "Warning") ? "Warning" : "OK",
                Source = group.Any(item => item.Source == "ManualRule") ? "ManualRule" : "Auto",
                IsManual = group.Any(item => item.Source == "ManualRule"),
                ImagePath = SpecImageService.GetFallbackImagePath(group.Key.Group, group.Key.Name)
            };

            foreach (SpecItemRow item in group)
            {
                row.SourceItems.Add(item);
                if (item.ElementId > 0) row.ElementIds.Add(item.ElementId);
                if (!string.IsNullOrWhiteSpace(item.UniqueId)) row.UniqueIds.Add(item.UniqueId);
            }

            return row;
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
