using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using VentCalc.UI.Services;

namespace VentCalc.Revit.Services
{
    public sealed class SpecCollectorService
    {
        private static readonly BuiltInCategory[] VentilationCategories = new[]
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_MechanicalEquipment
        };

        private readonly SpecRuleService ruleService;

        public SpecCollectorService(SpecRuleService? ruleService = null)
        {
            this.ruleService = ruleService ?? new SpecRuleService();
        }

        public IReadOnlyList<SpecItemRow> Collect(Document document)
        {
            Dictionary<string, SpecRule> rules = ruleService.LoadRules();
            var rows = new List<SpecItemRow>();
            foreach (BuiltInCategory category in VentilationCategories)
            {
                IEnumerable<Element> elements;
                try
                {
                    elements = new FilteredElementCollector(document)
                        .OfCategory(category)
                        .WhereElementIsNotElementType()
                        .ToElements();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (Element element in elements)
                {
                    SpecItemRow row = BuildRow(document, element, category);
                    ApplyAdskOverrides(element, row);
                    ApplyRule(row, rules);
                    FinalizeStatus(row);
                    rows.Add(row);
                }
            }

            return rows
                .OrderBy(row => row.Group)
                .ThenBy(row => row.Name)
                .ThenBy(row => row.TypeMark)
                .ThenBy(row => row.Size)
                .ThenBy(row => row.ElementId)
                .ToList();
        }

        private static SpecItemRow BuildRow(Document document, Element element, BuiltInCategory category)
        {
            Element? type = document.GetElement(element.GetTypeId());
            string familyName = ReadFamilyName(element, type);
            string typeName = type?.Name ?? element.Name ?? string.Empty;
            string allText = $"{familyName} {typeName} {element.Name}".ToLowerInvariant();
            string categoryName = element.Category?.Name ?? category.ToString();

            var row = new SpecItemRow();
            row.ApplySystemValues(item =>
            {
                item.ElementId = element.Id.Value;
                item.UniqueId = element.UniqueId;
                item.InternalName = element.Name ?? string.Empty;
                item.Category = categoryName;
                item.FamilyName = familyName;
                item.TypeName = typeName;
                item.TypeMark = FirstNonEmpty(ReadParameterAsString(element, "ADSK_Марка"), ReadParameterAsString(type, "ADSK_Марка"), typeName, "—");
                item.Size = ReadSize(element, type);
                item.System = ReadSystemName(element);
                item.Level = ReadLevelName(document, element);
                item.Source = "Category";
            });

            switch (category)
            {
                case BuiltInCategory.OST_DuctCurves:
                    ClassifyDuct(element, type, row);
                    break;
                case BuiltInCategory.OST_FlexDuctCurves:
                    row.ApplySystemValues(item =>
                    {
                        item.Group = "Гибкие воздуховоды";
                        item.Name = "Гибкий воздуховод";
                        item.Unit = "м";
                        item.Quantity = Math.Round(ReadLengthM(element), 3);
                    });
                    break;
                case BuiltInCategory.OST_DuctFitting:
                    ClassifyFitting(row, allText);
                    break;
                case BuiltInCategory.OST_DuctAccessory:
                    row.ApplySystemValues(item =>
                    {
                        item.Group = "Арматура / клапаны";
                        item.Name = ContainsAny(allText, "клапан", "damper", "valve") ? "Клапан" : "Арматура / клапан";
                        item.Unit = "шт";
                        item.Quantity = 1;
                    });
                    break;
                case BuiltInCategory.OST_DuctTerminal:
                    ClassifyTerminal(row, allText);
                    break;
                case BuiltInCategory.OST_MechanicalEquipment:
                    row.ApplySystemValues(item =>
                    {
                        item.Group = ContainsAny(allText, "зонт", "hood") ? "Зонты" : "Оборудование";
                        item.Name = ContainsAny(allText, "зонт", "hood") ? "Зонт вытяжной" : ContainsAny(allText, "вентил", "fan") ? "Вентилятор" : "Оборудование вентиляционное";
                        item.Unit = "шт";
                        item.Quantity = 1;
                    });
                    break;
                default:
                    row.ApplySystemValues(item =>
                    {
                        item.Group = "Неопознано";
                        item.Name = "Неопознанный элемент";
                        item.Unit = "шт";
                        item.Quantity = 1;
                        item.Source = "Unknown";
                    });
                    break;
            }

            return row;
        }

        private static void ClassifyDuct(Element element, Element? type, SpecItemRow row)
        {
            double lengthM = ReadLengthM(element);
            double diameterM = ReadDoubleParameterM(element, type, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM, "Диаметр", "Diameter");
            double widthM = ReadDoubleParameterM(element, type, BuiltInParameter.RBS_CURVE_WIDTH_PARAM, "Ширина", "Width");
            double heightM = ReadDoubleParameterM(element, type, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, "Высота", "Height");
            row.ApplySystemValues(item =>
            {
                item.Group = "Воздуховоды";
                if (diameterM > 0)
                {
                    item.Name = "Воздуховод круглый";
                    item.Unit = "м²";
                    item.Quantity = Math.Round(Math.PI * diameterM * lengthM, 3);
                }
                else if (widthM > 0 && heightM > 0)
                {
                    item.Name = "Воздуховод прямоугольный";
                    item.Unit = "м²";
                    item.Quantity = Math.Round(2 * (widthM + heightM) * lengthM, 3);
                }
                else
                {
                    item.Name = "Воздуховод";
                    item.Unit = "м";
                    item.Quantity = Math.Round(lengthM, 3);
                }
            });
        }

        private static void ClassifyFitting(SpecItemRow row, string allText)
        {
            row.ApplySystemValues(item =>
            {
                item.Group = "Фасонные части";
                item.Unit = "шт";
                item.Quantity = 1;
                item.Source = ContainsAny(allText, "отвод", "elbow", "переход", "transition", "тройник", "tee", "врез", "tap") ? "FamilyName" : "Category";
                item.Name = ContainsAny(allText, "отвод", "elbow") ? "Отвод"
                    : ContainsAny(allText, "переход", "transition") ? "Переход"
                    : ContainsAny(allText, "тройник", "tee") ? "Тройник"
                    : ContainsAny(allText, "врез", "tap") ? "Врезка"
                    : "Фасонная часть";
            });
        }

        private static void ClassifyTerminal(SpecItemRow row, string allText)
        {
            row.ApplySystemValues(item =>
            {
                item.Group = ContainsAny(allText, "зонт", "hood") ? "Зонты" : "Воздухораспределители";
                item.Unit = "шт";
                item.Quantity = 1;
                item.Source = "FamilyName";
                item.Name = ContainsAny(allText, "зонт", "hood") ? "Зонт вытяжной"
                    : ContainsAny(allText, "дифф", "diffuser") ? "Диффузор"
                    : "Решётка вентиляционная";
            });
        }

        private static void ApplyAdskOverrides(Element element, SpecItemRow row)
        {
            string adskName = ReadParameterAsString(element, "ADSK_Наименование");
            string adskMark = ReadParameterAsString(element, "ADSK_Марка");
            string adskCode = ReadParameterAsString(element, "ADSK_Код изделия");
            string adskUnit = ReadParameterAsString(element, "ADSK_Единица измерения");
            string adskQuantity = ReadParameterAsString(element, "ADSK_Количество");
            string adskNote = ReadParameterAsString(element, "ADSK_Примечание");

            bool anyAdsk = false;
            row.ApplySystemValues(item =>
            {
                if (!string.IsNullOrWhiteSpace(adskName)) { item.Name = adskName; anyAdsk = true; }
                if (!string.IsNullOrWhiteSpace(adskMark)) { item.TypeMark = adskMark; anyAdsk = true; }
                else if (!string.IsNullOrWhiteSpace(adskCode)) { item.TypeMark = adskCode; anyAdsk = true; }
                if (!string.IsNullOrWhiteSpace(adskUnit)) { item.Unit = adskUnit; anyAdsk = true; }
                if (TryParseDouble(adskQuantity, out double quantity) && quantity > 0) { item.Quantity = Math.Round(quantity, 3); anyAdsk = true; }
                if (!string.IsNullOrWhiteSpace(adskNote)) { item.Note = adskNote; anyAdsk = true; }
                if (anyAdsk) item.Source = "ADSK";
            });
        }

        private static void ApplyRule(SpecItemRow row, IReadOnlyDictionary<string, SpecRule> rules)
        {
            if (!rules.TryGetValue(row.RuleKey.ToStorageKey(), out SpecRule? rule))
            {
                return;
            }

            row.ApplySystemValues(item =>
            {
                if (!string.IsNullOrWhiteSpace(rule.Group)) item.Group = rule.Group;
                if (!string.IsNullOrWhiteSpace(rule.Name)) item.Name = rule.Name;
                if (!string.IsNullOrWhiteSpace(rule.TypeMark)) item.TypeMark = rule.TypeMark;
                if (!string.IsNullOrWhiteSpace(rule.Unit)) item.Unit = rule.Unit;
                if (!string.IsNullOrWhiteSpace(rule.Note)) item.Note = rule.Note;
                item.Source = "ManualRule";
            });
        }

        private static void FinalizeStatus(SpecItemRow row)
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(row.Name) || row.Name == "Неопознанный элемент") problems.Add("не найдено нормальное наименование");
            if (row.Size == "—" && row.Group is "Воздуховоды" or "Гибкие воздуховоды" or "Фасонные части") problems.Add("не найден размер");
            if (row.System == "—") problems.Add("элемент не подключён к системе");
            if (row.Source == "Unknown" || row.Group == "Неопознано") problems.Add("неопознанный элемент");
            if (string.IsNullOrWhiteSpace(ReadSafe(row.Note)) && row.Source != "ADSK") problems.Add("пустые ADSK-параметры");

            row.ApplySystemValues(item =>
            {
                if (problems.Count == 0)
                {
                    item.Status = "OK";
                }
                else
                {
                    item.Status = problems.Any(problem => problem.Contains("неопознан", StringComparison.OrdinalIgnoreCase)) ? "Error" : "Warning";
                    string suffix = string.Join("; ", problems.Distinct());
                    item.Note = string.IsNullOrWhiteSpace(item.Note) ? suffix : $"{item.Note}; {suffix}";
                }
            });
        }

        private static string ReadSize(Element element, Element? type)
        {
            double diameterM = ReadDoubleParameterM(element, type, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM, "Диаметр", "Diameter");
            if (diameterM > 0) return $"Ø{Math.Round(diameterM * 1000):0}";

            double widthM = ReadDoubleParameterM(element, type, BuiltInParameter.RBS_CURVE_WIDTH_PARAM, "Ширина", "Width");
            double heightM = ReadDoubleParameterM(element, type, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, "Высота", "Height");
            if (widthM > 0 && heightM > 0) return $"{Math.Round(widthM * 1000):0}×{Math.Round(heightM * 1000):0}";

            string size = FirstNonEmpty(ReadParameterAsString(element, "Размер"), ReadParameterAsString(type, "Размер"), ReadParameterAsString(element, "Size"), ReadParameterAsString(type, "Size"));
            return string.IsNullOrWhiteSpace(size) ? "—" : size;
        }

        private static string ReadSystemName(Element element)
        {
            return FirstNonEmpty(
                ReadParameterAsString(element, BuiltInParameter.RBS_SYSTEM_NAME_PARAM),
                ReadParameterAsString(element, "Имя системы"),
                ReadParameterAsString(element, "System Name"),
                "—");
        }

        private static string ReadLevelName(Document document, Element element)
        {
            ElementId levelId = element.LevelId;
            if (levelId != ElementId.InvalidElementId && document.GetElement(levelId) is Level level)
            {
                return level.Name;
            }

            return FirstNonEmpty(ReadParameterAsString(element, BuiltInParameter.FAMILY_LEVEL_PARAM), ReadParameterAsString(element, "Уровень"), "—");
        }

        private static string ReadFamilyName(Element element, Element? type)
        {
            if (element is FamilyInstance familyInstance)
            {
                return familyInstance.Symbol?.FamilyName ?? string.Empty;
            }

            return FirstNonEmpty(ReadParameterAsString(type, BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM), type?.Name, element.Category?.Name, string.Empty);
        }

        private static double ReadLengthM(Element element)
        {
            Parameter? parameter = element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            if (parameter != null && parameter.StorageType == StorageType.Double)
            {
                return UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Meters);
            }

            return 0;
        }

        private static double ReadDoubleParameterM(Element element, Element? type, BuiltInParameter builtInParameter, params string[] names)
        {
            Parameter? parameter = element.get_Parameter(builtInParameter) ?? type?.get_Parameter(builtInParameter);
            if (parameter != null && parameter.StorageType == StorageType.Double)
            {
                return UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Meters);
            }

            foreach (string name in names)
            {
                string value = FirstNonEmpty(ReadParameterAsString(element, name), ReadParameterAsString(type, name));
                if (TryParseDouble(value, out double parsed) && parsed > 0)
                {
                    return parsed > 20 ? parsed / 1000.0 : parsed;
                }
            }

            return 0;
        }

        private static string ReadParameterAsString(Element? element, BuiltInParameter builtInParameter)
        {
            if (element == null) return string.Empty;
            Parameter? parameter = element.get_Parameter(builtInParameter);
            return ParameterToString(parameter);
        }

        private static string ReadParameterAsString(Element? element, string name)
        {
            if (element == null || string.IsNullOrWhiteSpace(name)) return string.Empty;
            Parameter? parameter = element.LookupParameter(name);
            return ParameterToString(parameter);
        }

        private static string ParameterToString(Parameter? parameter)
        {
            if (parameter == null) return string.Empty;
            string value = parameter.StorageType == StorageType.String ? parameter.AsString() : string.Empty;
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            value = parameter.AsValueString();
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static bool TryParseDouble(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                || double.TryParse(value?.Replace(',', '.') ?? string.Empty, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static bool ContainsAny(string text, params string[] tokens)
        {
            return tokens.Any(token => text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
        }

        private static string ReadSafe(string value) => value ?? string.Empty;
    }
}
