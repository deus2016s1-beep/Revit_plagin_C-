using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using VentCalc.Core.Models;

namespace VentCalc.Revit.Services
{
    public sealed class RevitParameterReader
    {
        private static readonly IReadOnlyList<ParameterDefinition> ParameterDefinitions = new[]
        {
            new ParameterDefinition("Расход воздуха", new[] { "RBS_DUCT_FLOW_PARAM", "RBS_SYSTEM_FLOW_PARAM" }, new[] { "Расход воздуха", "Flow", "Air Flow" }),
            new ParameterDefinition("Размер", new[] { "RBS_CALCULATED_SIZE" }, new[] { "Размер", "Size" }),
            new ParameterDefinition("Диаметр", new[] { "RBS_CURVE_DIAMETER_PARAM" }, new[] { "Диаметр", "Diameter" }),
            new ParameterDefinition("Ширина", new[] { "RBS_CURVE_WIDTH_PARAM" }, new[] { "Ширина", "Width" }),
            new ParameterDefinition("Высота", new[] { "RBS_CURVE_HEIGHT_PARAM" }, new[] { "Высота", "Height" }),
            new ParameterDefinition("Длина", new[] { "CURVE_ELEM_LENGTH" }, new[] { "Длина", "Length" }),
            new ParameterDefinition("Комментарии", new[] { "ALL_MODEL_INSTANCE_COMMENTS" }, new[] { "Комментарии", "Comments" }),
            new ParameterDefinition("ADSK_Наименование", Array.Empty<string>(), new[] { "ADSK_Наименование" }),
            new ParameterDefinition("ADSK_Размер_УголПоворота", Array.Empty<string>(), new[] { "ADSK_Размер_УголПоворота" })
        };

        public IReadOnlyList<VentParameterInfo> Read(Element element)
        {
            var result = new List<VentParameterInfo>();
            foreach (ParameterDefinition definition in ParameterDefinitions)
            {
                Parameter parameter = FindParameter(element, definition);
                string value = FormatParameterValue(parameter);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(new VentParameterInfo(definition.DisplayName, value));
                }
            }

            return result;
        }

        public string ReadBuiltInParameter(Element element, string builtInParameterName)
        {
            if (!Enum.TryParse(builtInParameterName, out BuiltInParameter builtInParameter))
            {
                return string.Empty;
            }

            return FormatParameterValue(element.get_Parameter(builtInParameter));
        }

        public string ReadLookupParameter(Element element, string parameterName)
        {
            return FormatParameterValue(element.LookupParameter(parameterName));
        }

        private static Parameter FindParameter(Element element, ParameterDefinition definition)
        {
            foreach (string builtInParameterName in definition.BuiltInParameterNames)
            {
                if (Enum.TryParse(builtInParameterName, out BuiltInParameter builtInParameter))
                {
                    Parameter parameter = element.get_Parameter(builtInParameter);
                    if (HasValue(parameter))
                    {
                        return parameter;
                    }
                }
            }

            foreach (string parameterName in definition.LookupParameterNames)
            {
                Parameter parameter = element.LookupParameter(parameterName);
                if (HasValue(parameter))
                {
                    return parameter;
                }
            }

            return null;
        }

        private static bool HasValue(Parameter parameter)
        {
            return parameter != null && parameter.HasValue;
        }

        private static string FormatParameterValue(Parameter parameter)
        {
            if (!HasValue(parameter))
            {
                return string.Empty;
            }

            string valueString = parameter.AsValueString();
            if (!string.IsNullOrWhiteSpace(valueString))
            {
                return valueString;
            }

            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return parameter.AsString() ?? string.Empty;
                case StorageType.Integer:
                    return parameter.AsInteger().ToString();
                case StorageType.Double:
                    return parameter.AsDouble().ToString("G6");
                case StorageType.ElementId:
                    return parameter.AsElementId().IntegerValue.ToString();
                default:
                    return string.Empty;
            }
        }

        private sealed class ParameterDefinition
        {
            public ParameterDefinition(string displayName, IReadOnlyList<string> builtInParameterNames, IReadOnlyList<string> lookupParameterNames)
            {
                DisplayName = displayName;
                BuiltInParameterNames = builtInParameterNames;
                LookupParameterNames = lookupParameterNames;
            }

            public string DisplayName { get; }

            public IReadOnlyList<string> BuiltInParameterNames { get; }

            public IReadOnlyList<string> LookupParameterNames { get; }
        }
    }
}
