using System;
using System.Collections.Generic;
using System.Drawing;
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
                    AssignPreviewImagePath(document, element, row);
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

        private static void AssignPreviewImagePath(Document document, Element element, SpecItemRow row)
        {
            try
            {
                ElementId typeId = element.GetTypeId();
                if (typeId == ElementId.InvalidElementId) return;
                Element? type = document.GetElement(typeId);
                if (type == null) return;
                Image? preview = TryGetPreviewImage(type);
                try
                {
                    if (preview == null || preview.Width <= 0 || preview.Height <= 0) return;
                    string cacheKey = $"{typeId.Value}_{row.FamilyName}_{row.TypeName}";
                    string path = SpecImageService.SavePreviewImage(cacheKey, preview);
                    if (!string.IsNullOrWhiteSpace(path)) row.ImagePath = path;
                }
                finally
                {
                    preview?.Dispose();
                }
            }
            catch (Exception)
            {
                // Preview images are optional for SpecCalc. Fallback icons are used if Revit preview is unavailable.
            }
        }

        private static Image? TryGetPreviewImage(Element type)
        {
            try
            {
                return type is ElementType elementType ? elementType.GetPreviewImage(new System.Drawing.Size(128, 128)) : null;
            }
            catch (Exception)
            {
                return null;
            }
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
                item.TypeId = element.GetTypeId() == ElementId.InvalidElementId ? 0 : element.GetTypeId().Value;
                item.InternalName = element.Name ?? string.Empty;
                item.Category = categoryName;
                item.FamilyName = familyName;
                item.TypeName = typeName;
                item.TypeMark = CleanTypeMark(FirstNonEmpty(ReadParameterAsString(element, "ADSK_Марка"), ReadParameterAsString(type, "ADSK_Марка"), typeName, "—"));
                item.Size = ReadSize(element, type, allText);
                item.System = ReadSystemName(element);
                item.Level = ReadLevelName(document, element);
                item.Section = "Вентиляция";
                item.Material = FirstNonEmpty(ReadParameterAsString(element, "ADSK_Материал"), ReadParameterAsString(type, "ADSK_Материал"));
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
                        item.Name = AppendSize("Гибкий воздуховод", item.Size);
                        item.Unit = "м";
                        item.LengthM = Math.Round(ReadLengthM(element), 3);
                        item.Quantity = item.LengthM;
                    });
                    break;
                case BuiltInCategory.OST_DuctFitting:
                    ClassifyFitting(row, allText);
                    break;
                case BuiltInCategory.OST_DuctAccessory:
                    row.ApplySystemValues(item =>
                    {
                        item.Group = "Арматура / клапаны";
                        item.Name = ContainsAny(allText, "шумоглуш", "silencer") ? "Шумоглушитель"
                            : ContainsAny(allText, "гибк", "flex") ? "Гибкая вставка"
                            : ContainsAny(allText, "клапан", "damper", "valve") ? "Клапан"
                            : "Арматура / клапан";
                        item.Unit = "шт";
                        item.Quantity = 1;
                    });
                    break;
                case BuiltInCategory.OST_DuctTerminal:
                    ClassifyTerminal(element, type, row, allText);
                    break;
                case BuiltInCategory.OST_MechanicalEquipment:
                    row.ApplySystemValues(item =>
                    {
                        item.Group = ContainsAny(allText, "зонт", "hood") ? "Зонты" : "Оборудование";
                        item.Size = ReadEquipmentSize(element, type, item.Size);
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
                item.Unit = "м²";
                item.LengthM = Math.Round(lengthM, 3);
                item.TypeMark = "Оцинкованная сталь";
                if (diameterM > 0)
                {
                    item.Name = "Воздуховоды круглые из оцинкованной стали";
                    item.AreaM2 = Math.Round(Math.PI * diameterM * lengthM, 3);
                    item.Quantity = item.AreaM2;
                }
                else if (widthM > 0 && heightM > 0)
                {
                    item.Name = "Воздуховоды прямоугольные из оцинкованной стали";
                    item.AreaM2 = Math.Round(2 * (widthM + heightM) * lengthM, 3);
                    item.Quantity = item.AreaM2;
                }
                else
                {
                    item.Name = "Воздуховоды из оцинкованной стали";
                    item.Unit = "м";
                    item.Quantity = item.LengthM;
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
                item.Name = ContainsAny(allText, "заглуш", "cap", "plug") ? "Заглушка"
                    : ContainsAny(allText, "отвод", "elbow") ? $"Отвод {ReadAngleText(allText)}".Trim()
                    : ContainsAny(allText, "переход", "transition") ? "Переход"
                    : ContainsAny(allText, "тройник", "tee") ? "Тройник"
                    : ContainsAny(allText, "врез", "tap") ? "Врезка"
                    : "Неопознанная фасонная часть";
            });
        }

        private static void ClassifyTerminal(Element element, Element? type, SpecItemRow row, string allText)
        {
            row.ApplySystemValues(item =>
            {
                item.Group = ContainsAny(allText, "зонт", "hood") ? "Зонты" : "Воздухораспределители";
                item.Unit = "шт";
                item.Quantity = 1;
                item.Source = "FamilyName";
                item.Size = ReadTerminalSize(element, type, item.Size, allText);
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
            string adskSize = ReadParameterAsString(element, "ADSK_Размер");

            bool anyAdsk = false;
            row.ApplySystemValues(item =>
            {
                item.AdskName = adskName;
                item.AdskMark = adskMark;
                item.AdskSize = adskSize;
                if (!string.IsNullOrWhiteSpace(adskName) && CanUseAdskName(item, adskName)) { item.Name = adskName; anyAdsk = true; }
                item.HasAdskName = !string.IsNullOrWhiteSpace(adskName);
                if (!string.IsNullOrWhiteSpace(adskMark)) { item.TypeMark = CleanTypeMark(adskMark); anyAdsk = true; }
                else if (!string.IsNullOrWhiteSpace(adskCode)) { item.TypeMark = CleanTypeMark(adskCode); anyAdsk = true; }
                if (!string.IsNullOrWhiteSpace(adskUnit) && item.Group != "Воздуховоды" && item.Group != "Фасонные части") { item.Unit = adskUnit; anyAdsk = true; }
                if (TryParseDouble(adskQuantity, out double quantity) && quantity > 0 && item.Group != "Воздуховоды") { item.Quantity = Math.Round(quantity, 3); anyAdsk = true; }
                if (!string.IsNullOrWhiteSpace(adskNote)) { item.Note = adskNote; anyAdsk = true; }
                if (anyAdsk) item.Source = "ADSK";
            });
        }

        private static void ApplyRule(SpecItemRow row, IReadOnlyDictionary<string, SpecRule> rules)
        {
            if (!SpecRuleService.TryGetRule(row, rules, out SpecRule? rule) || rule == null)
            {
                return;
            }

            row.ApplySystemValues(item =>
            {
                if (!string.IsNullOrWhiteSpace(rule.Section)) item.Section = rule.Section;
                if (!string.IsNullOrWhiteSpace(rule.Group)) item.Group = rule.Group;
                if (!string.IsNullOrWhiteSpace(rule.Name)) item.Name = rule.Name;
                if (!string.IsNullOrWhiteSpace(rule.TypeMark)) item.TypeMark = rule.TypeMark;
                if (!string.IsNullOrWhiteSpace(rule.Size)) item.Size = rule.Size;
                if (!string.IsNullOrWhiteSpace(rule.Unit)) item.Unit = rule.Unit;
                if (!string.IsNullOrWhiteSpace(rule.Note)) item.Note = rule.Note;
                item.Source = "ManualRule";
            });
        }

        private static void FinalizeStatus(SpecItemRow row)
        {
            var problems = new List<string>();
            var recommendations = new List<string>();
            if (string.IsNullOrWhiteSpace(row.Name) || row.Name == "Неопознанный элемент") problems.Add("не найдено нормальное наименование");
            if (string.IsNullOrWhiteSpace(row.Name) || row.Name == "Неопознанный элемент") recommendations.Add("Заполните ADSK_Наименование или задайте ручное правило");
            row.MissingSize = row.Size == "—" && row.Group is "Воздуховоды" or "Гибкие воздуховоды" or "Фасонные части" or "Зонты" or "Воздухораспределители";
            if (row.MissingSize) { problems.Add("не найден размер"); recommendations.Add("Проверьте семейство: не найден размер"); }
            if (row.Group == "Воздуховоды" && row.LengthM <= 0) { problems.Add("не найдена длина воздуховода"); recommendations.Add("Проверьте параметр длины воздуховода"); }
            row.MissingSystem = row.System == "—";
            if (row.MissingSystem) { problems.Add("элемент не подключён к системе"); recommendations.Add("Элемент не подключён к системе"); }
            row.IsUnrecognized = row.Source == "Unknown" || row.Group == "Неопознано";
            if (row.IsUnrecognized) { problems.Add("неопознанный элемент"); recommendations.Add("Проверьте категорию семейства"); }
            if (!row.HasAdskName) { problems.Add("пустой ADSK_Наименование"); recommendations.Add("Заполните ADSK_Наименование или задайте ручное правило"); }

            row.ApplySystemValues(item =>
            {
                if (problems.Count == 0)
                {
                    item.Status = "OK";
                    item.Recommendation = string.Empty;
                }
                else
                {
                    item.Status = problems.Any(problem => problem.Contains("неопознан", StringComparison.OrdinalIgnoreCase)) ? "Error" : "Warning";
                    string suffix = string.Join("; ", problems.Distinct());
                    item.Note = string.IsNullOrWhiteSpace(item.Note) ? suffix : $"{item.Note}; {suffix}";
                    item.Recommendation = string.Join("; ", recommendations.Distinct());
                }
            });
        }

        private static string ReadSize(Element element, Element? type, string allText)
        {
            double diameterM = ReadDoubleParameterM(element, type, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM, "Диаметр", "Diameter");
            if (diameterM > 0) return $"Ø{Math.Round(diameterM * 1000):0}";

            double widthM = ReadDoubleParameterM(element, type, BuiltInParameter.RBS_CURVE_WIDTH_PARAM, "Ширина", "Width");
            double heightM = ReadDoubleParameterM(element, type, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, "Высота", "Height");
            if (widthM > 0 && heightM > 0) return $"{Math.Round(widthM * 1000):0}×{Math.Round(heightM * 1000):0}";

            string size = FirstNonEmpty(ReadParameterAsString(element, "Размер"), ReadParameterAsString(type, "Размер"), ReadParameterAsString(element, "ADSK_Размер"), ReadParameterAsString(type, "ADSK_Размер"), ReadParameterAsString(element, "Size"), ReadParameterAsString(type, "Size"));
            if (!string.IsNullOrWhiteSpace(size)) return NormalizeSize(size);
            string fromNames = TryExtractSize(allText);
            return string.IsNullOrWhiteSpace(fromNames) ? "—" : fromNames;
        }


        private static string ReadEquipmentSize(Element element, Element? type, string currentSize)
        {
            string three = ReadThreeDimensionalSize(element, type, new[] { "ADSK_Размер_Длина", "Длина", "Length" }, new[] { "ADSK_Размер_Ширина", "Ширина", "Width" }, new[] { "ADSK_Размер_Высота", "Высота", "Height" });
            if (!string.IsNullOrWhiteSpace(three)) return three;
            if (!string.IsNullOrWhiteSpace(currentSize) && currentSize != "—") return NormalizeSize(currentSize);
            string fromNames = TryExtractSize($"{type?.Name} {element.Name}");
            return string.IsNullOrWhiteSpace(fromNames) ? "—" : fromNames;
        }

        private static string ReadTerminalSize(Element element, Element? type, string currentSize, string allText)
        {
            if (!string.IsNullOrWhiteSpace(currentSize) && currentSize != "—") return NormalizeSize(currentSize);
            double width = ReadNamedLengthMm(element, type, new[] { "ADSK_Размер_Ширина", "Ширина", "A", "Width" });
            double height = ReadNamedLengthMm(element, type, new[] { "ADSK_Размер_Высота", "Высота", "B", "Height" });
            if (width > 0 && height > 0) return $"{Math.Round(width):0}×{Math.Round(height):0}";
            string fromNames = TryExtractSize(allText);
            return string.IsNullOrWhiteSpace(fromNames) ? "—" : fromNames;
        }

        private static string ReadThreeDimensionalSize(Element element, Element? type, string[] lengthNames, string[] widthNames, string[] heightNames)
        {
            double length = ReadNamedLengthMm(element, type, lengthNames);
            double width = ReadNamedLengthMm(element, type, widthNames);
            double height = ReadNamedLengthMm(element, type, heightNames);
            return length > 0 && width > 0 && height > 0
                ? $"{Math.Round(length):0}×{Math.Round(width):0}×{Math.Round(height):0}"
                : string.Empty;
        }

        private static double ReadNamedLengthMm(Element element, Element? type, IEnumerable<string> names)
        {
            foreach (string name in names)
            {
                Parameter? parameter = element.LookupParameter(name) ?? type?.LookupParameter(name);
                if (parameter == null) continue;
                if (parameter.StorageType == StorageType.Double)
                {
                    return UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Millimeters);
                }

                if (TryParseDouble(ParameterToString(parameter), out double parsed) && parsed > 0)
                {
                    return parsed > 20 ? parsed : parsed * 1000;
                }
            }

            return 0;
        }

        private static bool CanUseAdskName(SpecItemRow item, string adskName)
        {
            if (item.Group == "Фасонные части" && ContainsAny(adskName, "оцинков", "воздуховод")) return false;
            if (item.Group == "Воздуховоды" && ContainsAny(adskName, "ADSK_", "врезк", "тройник", "переход")) return false;
            return true;
        }

        private static string CleanTypeMark(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "—") return "—";
            if (ContainsAny(value, "ADSK_Оцинковка", "Оцинковка_", "ГОСТ 14918")) return "Оцинкованная сталь";
            return value.Trim();
        }

        private static string NormalizeSize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "—"
                : value.Trim().Replace('х', '×').Replace('Х', '×').Replace('x', '×').Replace('X', '×').Replace('*', '×');
        }

        private static string TryExtractSize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var match = System.Text.RegularExpressions.Regex.Match(text, @"(?i)(?:Ø|ø|ф|d)?\s*(\d{2,4})\s*[xх×*]\s*(\d{2,4})(?:\s*[xх×*]\s*(\d{2,4}))?");
            if (match.Success)
            {
                return match.Groups[3].Success
                    ? $"{match.Groups[1].Value}×{match.Groups[2].Value}×{match.Groups[3].Value}"
                    : $"{match.Groups[1].Value}×{match.Groups[2].Value}";
            }

            match = System.Text.RegularExpressions.Regex.Match(text, @"(?i)(?:Ø|ø|ф|d)\s*(\d{2,4})");
            return match.Success ? $"Ø{match.Groups[1].Value}" : string.Empty;
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

        private static string AppendSize(string name, string size)
        {
            return string.IsNullOrWhiteSpace(size) || size == "—" ? name : $"{name} {size}";
        }

        private static string ReadAngleText(string text)
        {
            int[] angles = { 15, 30, 45, 60, 90 };
            foreach (int angle in angles)
            {
                if (text.Contains(angle.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
                {
                    return $"{angle}°";
                }
            }

            return string.Empty;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
        }

        private static string ReadSafe(string value) => value ?? string.Empty;
    }
}
