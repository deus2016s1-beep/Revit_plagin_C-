using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using VentCalc.Core.Models;

namespace VentCalc.Revit.Services
{
    public sealed class RevitDuctGeometryReader
    {
        public IReadOnlyDictionary<long, DuctGeometryData> ReadDucts(Document document, VentNetworkInfo networkInfo)
        {
            var result = new Dictionary<long, DuctGeometryData>();
            foreach (VentNetworkNode node in networkInfo.Elements.Where(node => node.CategoryKey == BuiltInCategory.OST_DuctCurves.ToString()))
            {
                if (!long.TryParse(node.ElementId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long elementIdValue))
                {
                    continue;
                }

                Element? element = document.GetElement(new ElementId(elementIdValue));
                DuctGeometryData? duct = element == null ? null : ReadDuct(element);
                if (duct != null)
                {
                    result[duct.ElementId] = duct;
                }
            }

            return result;
        }

        public DuctGeometryData? ReadDuct(Element element)
        {
            if (element.Category == null || (BuiltInCategory)element.Category.Id.Value != BuiltInCategory.OST_DuctCurves)
            {
                return null;
            }

            var data = new DuctGeometryData
            {
                ElementId = element.Id.Value,
                Size = ReadString(element, "RBS_CALCULATED_SIZE") ?? ReadStringByName(element, "Размер") ?? string.Empty,
                LengthM = ReadLengthInMeters(element, BuiltInParameter.CURVE_ELEM_LENGTH),
                FlowM3h = ReadFlowM3h(element),
                DiameterM = ReadLengthInMeters(element, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM),
                WidthM = ReadLengthInMeters(element, BuiltInParameter.RBS_CURVE_WIDTH_PARAM),
                HeightM = ReadLengthInMeters(element, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)
            };

            data.IsRound = data.DiameterM > 0;
            data.IsRectangular = data.WidthM > 0 && data.HeightM > 0;

            if (data.LengthM <= 0)
            {
                data.Warnings.Add("Не удалось прочитать длину воздуховода.");
            }

            if (data.FlowM3h <= 0)
            {
                data.Warnings.Add("Не удалось прочитать расход воздуха в м³/ч.");
            }

            if (!data.IsRound && !data.IsRectangular)
            {
                data.Warnings.Add("Не удалось прочитать диаметр или ширину/высоту воздуховода.");
            }

            return data;
        }

        private static double ReadLengthInMeters(Element element, BuiltInParameter builtInParameter)
        {
            Parameter? parameter = element.get_Parameter(builtInParameter);
            if (parameter == null || !parameter.HasValue || parameter.StorageType != StorageType.Double)
            {
                return 0;
            }

            return SafeConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Meters);
        }

        private static double ReadFlowM3h(Element element)
        {
            Parameter? parameter = element.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM)
                ?? element.LookupParameter("Расход воздуха")
                ?? element.LookupParameter("Flow")
                ?? element.LookupParameter("Air Flow");
            if (parameter == null || !parameter.HasValue)
            {
                return 0;
            }

            if (parameter.StorageType == StorageType.Double)
            {
                double converted = SafeConvertFromInternalUnits(parameter.AsDouble(), CubicMetersPerHourUnitTypeId());
                if (converted > 0)
                {
                    return converted;
                }
            }

            string? valueString = parameter.AsValueString();
            if (!string.IsNullOrWhiteSpace(valueString))
            {
                return ParseFirstNumber(valueString);
            }

            return parameter.StorageType == StorageType.Double ? parameter.AsDouble() : 0;
        }

        private static string? ReadString(Element element, string builtInParameterName)
        {
            if (!Enum.TryParse(builtInParameterName, out BuiltInParameter builtInParameter))
            {
                return null;
            }

            Parameter? parameter = element.get_Parameter(builtInParameter);
            return ReadParameterString(parameter);
        }

        private static string? ReadStringByName(Element element, string parameterName)
        {
            return ReadParameterString(element.LookupParameter(parameterName));
        }

        private static string? ReadParameterString(Parameter? parameter)
        {
            if (parameter == null || !parameter.HasValue)
            {
                return null;
            }

            string? valueString = parameter.AsValueString();
            if (!string.IsNullOrWhiteSpace(valueString))
            {
                return valueString;
            }

            return parameter.StorageType == StorageType.String ? parameter.AsString() : null;
        }

        private static double SafeConvertFromInternalUnits(double value, ForgeTypeId unitTypeId)
        {
            try
            {
                return UnitUtils.ConvertFromInternalUnits(value, unitTypeId);
            }
            catch (Exception)
            {
                return value;
            }
        }

        private static ForgeTypeId CubicMetersPerHourUnitTypeId()
        {
            try
            {
                object? value = typeof(UnitTypeId).GetProperty("CubicMetersPerHour")?.GetValue(null);
                if (value is ForgeTypeId unitTypeId)
                {
                    return unitTypeId;
                }
            }
            catch (Exception)
            {
                // Fall back to the known Forge unit id used by Revit's UnitUtils.
            }

            return new ForgeTypeId("autodesk.unit.unit:cubicMetersPerHour-1.0.0");
        }

        private static double ParseFirstNumber(string value)
        {
            Match match = Regex.Match(value.Replace(',', '.'), @"[-+]?\d+(\.\d+)?");
            return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : 0;
        }
    }
}
