using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using VentCalc.Core.Models;

namespace VentCalc.Revit.Services
{
    public sealed class RevitLocalResistanceDataReader
    {
        public IReadOnlyDictionary<long, LocalResistanceElementData> ReadElements(Document document, VentNetworkInfo networkInfo)
        {
            var result = new Dictionary<long, LocalResistanceElementData>();
            foreach (VentNetworkNode node in networkInfo.Elements)
            {
                if (node.CategoryKey == BuiltInCategory.OST_DuctCurves.ToString())
                {
                    continue;
                }

                if (!long.TryParse(node.ElementId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long elementIdValue))
                {
                    continue;
                }

                Element? element = document.GetElement(new ElementId(elementIdValue));
                LocalResistanceElementData data = ReadElement(node, element);
                result[data.ElementId] = data;
            }

            return result;
        }

        private static LocalResistanceElementData ReadElement(VentNetworkNode node, Element? element)
        {
            long elementId = long.TryParse(node.ElementId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : 0;
            var data = new LocalResistanceElementData
            {
                ElementId = elementId,
                CategoryKey = node.CategoryKey,
                CategoryName = node.CategoryName,
                FamilyName = node.FamilyName,
                TypeName = node.TypeName,
                Name = node.Name,
                Size = node.Size,
                Comments = element == null ? string.Empty : ReadComments(element)
            };

            return data;
        }

        private static string ReadComments(Element element)
        {
            return ReadParameterString(element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS))
                ?? ReadParameterString(element.LookupParameter("Комментарии"))
                ?? ReadParameterString(element.LookupParameter("Comments"))
                ?? string.Empty;
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
    }
}
