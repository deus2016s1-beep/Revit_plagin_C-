using System;
using Autodesk.Revit.DB;
using VentCalc.Core.Models;

namespace VentCalc.Revit.Services
{
    public sealed class RevitElementInfoReader
    {
        private readonly RevitParameterReader parameterReader;
        private readonly RevitConnectorReader connectorReader;

        public RevitElementInfoReader(RevitParameterReader parameterReader, RevitConnectorReader connectorReader)
        {
            this.parameterReader = parameterReader;
            this.connectorReader = connectorReader;
        }

        public VentElementInfo Read(Element element)
        {
            Document document = element.Document;
            Element typeElement = document.GetElement(element.GetTypeId());
            var familyInstance = element as FamilyInstance;

            return new VentElementInfo(
                element.Id.IntegerValue.ToString(),
                element.Category?.Name ?? "—",
                SafeRead(() => element.Name, "—"),
                SafeRead(() => typeElement?.Name, "—"),
                ReadFamilyName(familyInstance),
                ReadSystemName(element),
                ReadSystemType(element),
                ReadLevelName(document, element),
                parameterReader.Read(element),
                connectorReader.Read(element));
        }

        private string ReadSystemName(Element element)
        {
            string value = parameterReader.ReadBuiltInParameter(element, "RBS_SYSTEM_NAME_PARAM");
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return parameterReader.ReadLookupParameter(element, "Имя системы");
        }

        private string ReadSystemType(Element element)
        {
            string value = parameterReader.ReadBuiltInParameter(element, "RBS_SYSTEM_CLASSIFICATION_PARAM");
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            value = parameterReader.ReadBuiltInParameter(element, "RBS_DUCT_SYSTEM_TYPE_PARAM");
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return parameterReader.ReadLookupParameter(element, "Тип системы");
        }

        private static string ReadFamilyName(FamilyInstance familyInstance)
        {
            if (familyInstance?.Symbol?.Family != null)
            {
                return familyInstance.Symbol.Family.Name;
            }

            return "—";
        }

        private static string ReadLevelName(Document document, Element element)
        {
            if (element.LevelId == ElementId.InvalidElementId)
            {
                return "—";
            }

            Element level = document.GetElement(element.LevelId);
            return level?.Name ?? "—";
        }

        private static T SafeRead<T>(Func<T> read, T fallback)
        {
            try
            {
                T value = read();
                return value == null ? fallback : value;
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
