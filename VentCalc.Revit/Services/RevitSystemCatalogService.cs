using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using VentCalc.Core.Models;

namespace VentCalc.Revit.Services
{
    public sealed class RevitSystemCatalogService
    {
        private static readonly BuiltInCategory[] SupportedCategories =
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_MechanicalEquipment
        };

        public IReadOnlyList<VentSystemCatalogItem> Read(Document document)
        {
            var parameterReader = new RevitParameterReader();
            var rows = new List<SystemElementRow>();
            foreach (BuiltInCategory category in SupportedCategories)
            {
                foreach (Element element in new FilteredElementCollector(document).OfCategory(category).WhereElementIsNotElementType())
                {
                    string systemName = ReadSystemName(parameterReader, element);
                    string systemType = ReadSystemType(parameterReader, element);
                    if (string.IsNullOrWhiteSpace(systemName) || systemName == "—")
                    {
                        continue;
                    }

                    rows.Add(new SystemElementRow(element.Id.Value, category, systemName.Trim(), string.IsNullOrWhiteSpace(systemType) ? "—" : systemType.Trim()));
                }
            }

            return rows
                .GroupBy(row => new { row.SystemName, row.SystemType })
                .Select(group => CreateItem(group.Key.SystemName, group.Key.SystemType, group.ToList()))
                .OrderBy(item => item.SystemName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.SystemType, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<ElementId> FindSystemElementIds(Document document, string systemName, string systemType)
        {
            var parameterReader = new RevitParameterReader();
            var result = new List<ElementId>();
            foreach (BuiltInCategory category in SupportedCategories)
            {
                foreach (Element element in new FilteredElementCollector(document).OfCategory(category).WhereElementIsNotElementType())
                {
                    if (SameSystem(ReadSystemName(parameterReader, element), systemName)
                        && SameSystem(ReadSystemType(parameterReader, element), systemType))
                    {
                        result.Add(element.Id);
                    }
                }
            }

            return result;
        }

        private static VentSystemCatalogItem CreateItem(string systemName, string systemType, IReadOnlyList<SystemElementRow> rows)
        {
            var item = new VentSystemCatalogItem
            {
                SystemName = systemName,
                SystemType = systemType,
                Direction = DetermineDirection(systemName, systemType),
                ElementCount = rows.Count,
                DuctCount = rows.Count(row => row.Category == BuiltInCategory.OST_DuctCurves),
                FittingCount = rows.Count(row => row.Category == BuiltInCategory.OST_DuctFitting),
                TerminalCount = rows.Count(row => row.Category == BuiltInCategory.OST_DuctTerminal),
                EquipmentCount = rows.Count(row => row.Category == BuiltInCategory.OST_MechanicalEquipment),
                RepresentativeElementId = rows.First().ElementId
            };
            item.DisplayName = $"{item.SystemName} — {item.SystemType} — {item.ElementCount} элементов";
            return item;
        }

        public static string DetermineDirection(string systemName, string systemType)
        {
            if (ContainsSupply(systemType))
            {
                return "Supply";
            }

            if (ContainsExhaust(systemType))
            {
                return "Exhaust";
            }

            if (ContainsSupply(systemName) || StartsWithSystemPrefix(systemName, "П", "P"))
            {
                return "Supply";
            }

            if (ContainsExhaust(systemName) || StartsWithSystemPrefix(systemName, "В", "V"))
            {
                return "Exhaust";
            }

            return "Unknown";
        }

        private static bool ContainsSupply(string text)
        {
            return ContainsAny(text, "SupplyAir", "Supply Air", "Приточный воздух", "приточная", "приток", "supply");
        }

        private static bool ContainsExhaust(string text)
        {
            return ContainsAny(text, "ExhaustAir", "Exhaust Air", "ReturnAir", "Return Air", "Отработанный воздух", "Вытяжной воздух", "вытяжная", "вытяжка", "exhaust", "return");
        }

        private static bool ContainsAny(string text, params string[] patterns)
        {
            return patterns.Any(pattern => (text ?? string.Empty).IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool StartsWithSystemPrefix(string systemName, params string[] prefixes)
        {
            string trimmed = (systemName ?? string.Empty).TrimStart();
            return prefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static string ReadSystemName(RevitParameterReader parameterReader, Element element)
        {
            string value = parameterReader.ReadBuiltInParameter(element, "RBS_SYSTEM_NAME_PARAM");
            return !string.IsNullOrWhiteSpace(value) ? value : parameterReader.ReadLookupParameter(element, "Имя системы");
        }

        private static string ReadSystemType(RevitParameterReader parameterReader, Element element)
        {
            string value = parameterReader.ReadBuiltInParameter(element, "RBS_SYSTEM_CLASSIFICATION_PARAM");
            if (!string.IsNullOrWhiteSpace(value)) return value;
            value = parameterReader.ReadBuiltInParameter(element, "RBS_DUCT_SYSTEM_TYPE_PARAM");
            return !string.IsNullOrWhiteSpace(value) ? value : parameterReader.ReadLookupParameter(element, "Тип системы");
        }

        private static bool SameSystem(string actual, string expected)
        {
            return string.Equals((actual ?? string.Empty).Trim(), (expected ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(actual) && string.IsNullOrWhiteSpace(expected));
        }

        private sealed record SystemElementRow(long ElementId, BuiltInCategory Category, string SystemName, string SystemType);
    }
}
