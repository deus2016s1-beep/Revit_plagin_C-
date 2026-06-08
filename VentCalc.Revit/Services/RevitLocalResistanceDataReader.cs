using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using VentCalc.Core.Models;

namespace VentCalc.Revit.Services
{
    public sealed class RevitLocalResistanceDataReader
    {
        public IReadOnlyDictionary<long, LocalResistanceElementData> ReadElements(Document document, VentNetworkInfo networkInfo)
        {
            var overridesByElementId = RevitZetaOverrideStorage.ReadOverrides(document)
                .GroupBy(item => item.ElementId)
                .ToDictionary(group => group.Key, group => group.ToList());
            IReadOnlyList<ProjectZetaCatalogItem> projectCatalog = RevitZetaOverrideStorage.ReadProjectCatalog(document);
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
                if (overridesByElementId.TryGetValue(data.ElementId, out List<ZetaOverrideInfo>? overrides))
                {
                    data.ZetaOverrides.AddRange(overrides);
                }
                data.ProjectZetaCatalog.AddRange(projectCatalog);
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
                Comments = element == null ? string.Empty : ReadComments(element),
                SystemName = node.SystemName,
                SystemType = node.SystemType
            };

            if (element != null)
            {
                data.ConnectedDucts.AddRange(ReadConnectedDucts(element));
            }

            return data;
        }

        private static IReadOnlyList<FittingConnectedDuctInfo> ReadConnectedDucts(Element element)
        {
            ConnectorManager? connectorManager = GetConnectorManager(element);
            if (connectorManager == null)
            {
                return Array.Empty<FittingConnectedDuctInfo>();
            }

            var result = new List<FittingConnectedDuctInfo>();
            foreach (Connector connector in connectorManager.Connectors)
            {
                ConnectorSet? refs = SafeReadReference(() => connector.AllRefs);
                if (refs == null)
                {
                    continue;
                }

                foreach (Connector referencedConnector in refs)
                {
                    Element? owner = SafeReadReference(() => referencedConnector.Owner);
                    if (owner == null || owner.Id.Value == element.Id.Value || !IsDuct(owner))
                    {
                        continue;
                    }

                    if (result.Any(item => item.ElementId == owner.Id.Value))
                    {
                        continue;
                    }

                    XYZ? direction = ReadConnectorDirection(connector);
                    result.Add(new FittingConnectedDuctInfo
                    {
                        ElementId = owner.Id.Value,
                        DirectionX = direction?.X ?? 0,
                        DirectionY = direction?.Y ?? 0,
                        DirectionZ = direction?.Z ?? 0,
                        HasDirection = direction != null
                    });
                }
            }

            return result;
        }

        private static XYZ? ReadConnectorDirection(Connector connector)
        {
            try
            {
                XYZ basis = connector.CoordinateSystem.BasisZ;
                return basis.Normalize();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static ConnectorManager? GetConnectorManager(Element element)
        {
            if (element is MEPCurve mepCurve)
            {
                return mepCurve.ConnectorManager;
            }

            return element is FamilyInstance familyInstance ? familyInstance.MEPModel?.ConnectorManager : null;
        }

        private static bool IsDuct(Element element)
        {
            return element.Category != null && (BuiltInCategory)element.Category.Id.Value == BuiltInCategory.OST_DuctCurves;
        }

        private static T? SafeReadReference<T>(Func<T> read)
            where T : class
        {
            try
            {
                return read();
            }
            catch (Exception)
            {
                return null;
            }
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
