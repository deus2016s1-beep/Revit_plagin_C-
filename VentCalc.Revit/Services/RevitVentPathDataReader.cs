using System;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using VentCalc.Core.Models;

namespace VentCalc.Revit.Services
{
    public sealed class RevitVentPathDataReader
    {
        private readonly RevitElementInfoReader elementInfoReader;

        public RevitVentPathDataReader(RevitElementInfoReader elementInfoReader)
        {
            this.elementInfoReader = elementInfoReader;
        }

        public VentNetworkInfo Enrich(Document document, VentNetworkInfo networkInfo)
        {
            var enrichedNodes = networkInfo.Elements
                .Select(node => EnrichNode(document, node))
                .ToList();

            return new VentNetworkInfo(
                networkInfo.SelectedElementId,
                enrichedNodes,
                networkInfo.Connections,
                networkInfo.DuctCount,
                networkInfo.FittingCount,
                networkInfo.AccessoryCount,
                networkInfo.TerminalCount,
                networkInfo.EquipmentCount,
                networkInfo.OpenConnectorCount,
                networkInfo.StartCandidateCount,
                networkInfo.EndCandidateCount);
        }

        private VentNetworkNode EnrichNode(Document document, VentNetworkNode node)
        {
            if (!long.TryParse(node.ElementId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long elementIdValue))
            {
                return node;
            }

            Element? element = document.GetElement(new ElementId(elementIdValue));
            if (element == null)
            {
                return node;
            }

            VentElementInfo elementInfo = elementInfoReader.Read(element);

            return new VentNetworkNode(
                node.ElementId,
                elementInfo.CategoryName,
                node.CategoryKey,
                elementInfo.Name,
                elementInfo.TypeName,
                elementInfo.FamilyName,
                elementInfo.SystemName,
                elementInfo.SystemType,
                GetParameterValue(elementInfo, "Расход воздуха"),
                ReadSize(elementInfo),
                elementInfo.LevelName,
                elementInfo.Connectors.Count,
                node.OpenConnectorCount,
                ReadDuctLengthMm(element),
                node.ConnectedElementIds);
        }

        private static double ReadDuctLengthMm(Element element)
        {
            Parameter? parameter = element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            if (parameter == null || !parameter.HasValue || parameter.StorageType != StorageType.Double)
            {
                return 0;
            }

            return parameter.AsDouble() * 304.8;
        }

        private static string GetParameterValue(VentElementInfo elementInfo, string parameterName)
        {
            return elementInfo.Parameters.FirstOrDefault(parameter => parameter.Name == parameterName)?.Value ?? "—";
        }

        private static string ReadSize(VentElementInfo elementInfo)
        {
            string size = GetParameterValue(elementInfo, "Размер");
            if (size != "—")
            {
                return size;
            }

            string diameter = GetParameterValue(elementInfo, "Диаметр");
            if (diameter != "—")
            {
                return diameter;
            }

            string width = GetParameterValue(elementInfo, "Ширина");
            string height = GetParameterValue(elementInfo, "Высота");
            if (width != "—" || height != "—")
            {
                return $"{width} x {height}";
            }

            return "—";
        }
    }
}
