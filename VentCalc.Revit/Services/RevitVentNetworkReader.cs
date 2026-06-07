using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using VentCalc.Core.Models;

namespace VentCalc.Revit.Services
{
    public sealed class RevitVentNetworkReader
    {
        private static readonly HashSet<BuiltInCategory> SupportedCategories = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_MechanicalEquipment
        };

        private readonly RevitElementInfoReader elementInfoReader;

        public RevitVentNetworkReader(RevitElementInfoReader elementInfoReader)
        {
            this.elementInfoReader = elementInfoReader;
        }

        public VentNetworkInfo Read(Document document, ElementId selectedElementId)
        {
            var visitedElementIds = new HashSet<long>();
            var queuedElementIds = new HashSet<long>();
            var queue = new Queue<ElementId>();
            var connections = new List<VentNetworkConnection>();
            var connectionKeys = new HashSet<string>();

            queue.Enqueue(selectedElementId);
            queuedElementIds.Add(selectedElementId.Value);

            while (queue.Count > 0)
            {
                ElementId currentElementId = queue.Dequeue();
                if (!visitedElementIds.Add(currentElementId.Value))
                {
                    continue;
                }

                Element? currentElement = document.GetElement(currentElementId);
                if (!IsSupportedVentilationElement(currentElement))
                {
                    continue;
                }

                foreach (ConnectorLink connectorLink in ReadConnectorLinks(currentElement))
                {
                    Element? neighborElement = connectorLink.NeighborElement;
                    if (!IsSupportedVentilationElement(neighborElement) || neighborElement.Id.Value == currentElement.Id.Value)
                    {
                        continue;
                    }

                    AddConnectionIfMissing(connections, connectionKeys, connectorLink);

                    if (!visitedElementIds.Contains(neighborElement.Id.Value) && queuedElementIds.Add(neighborElement.Id.Value))
                    {
                        queue.Enqueue(neighborElement.Id);
                    }
                }
            }

            var elements = visitedElementIds
                .Select(id => document.GetElement(new ElementId(id)))
                .Where(IsSupportedVentilationElement)
                .Cast<Element>()
                .OrderBy(element => element.Id.Value)
                .ToList();

            var nodes = elements
                .Select(element => CreateNode(element, connections))
                .ToList();

            return new VentNetworkInfo(
                selectedElementId.Value.ToString(),
                nodes,
                connections.OrderBy(connection => connection.FromElementId).ThenBy(connection => connection.ToElementId).ToList(),
                elements.Count(element => IsCategory(element, BuiltInCategory.OST_DuctCurves)),
                elements.Count(element => IsCategory(element, BuiltInCategory.OST_DuctFitting)),
                elements.Count(element => IsCategory(element, BuiltInCategory.OST_DuctAccessory)),
                elements.Count(element => IsCategory(element, BuiltInCategory.OST_DuctTerminal)),
                elements.Count(element => IsCategory(element, BuiltInCategory.OST_MechanicalEquipment)),
                elements.Sum(CountOpenConnectors),
                elements.Count(element => IsCategory(element, BuiltInCategory.OST_MechanicalEquipment)),
                elements.Count(element => IsCategory(element, BuiltInCategory.OST_DuctTerminal)));
        }

        private VentNetworkNode CreateNode(Element element, IReadOnlyList<VentNetworkConnection> connections)
        {
            VentElementInfo elementInfo = elementInfoReader.Read(element);
            var connectedElementIds = connections
                .Where(connection => connection.FromElementId == elementInfo.ElementId || connection.ToElementId == elementInfo.ElementId)
                .Select(connection => connection.FromElementId == elementInfo.ElementId ? connection.ToElementId : connection.FromElementId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            return new VentNetworkNode(
                elementInfo.ElementId,
                elementInfo.CategoryName,
                ReadCategoryKey(element),
                elementInfo.Name,
                elementInfo.TypeName,
                elementInfo.FamilyName,
                elementInfo.SystemName,
                elementInfo.SystemType,
                GetParameterValue(elementInfo, "Расход воздуха"),
                ReadSize(elementInfo),
                elementInfo.LevelName,
                elementInfo.Connectors.Count,
                CountOpenConnectors(element),
                0,
                connectedElementIds);
        }

        private static string ReadCategoryKey(Element element)
        {
            return element.Category == null ? "—" : ((BuiltInCategory)element.Category.Id.Value).ToString();
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

        private static IReadOnlyList<ConnectorLink> ReadConnectorLinks(Element element)
        {
            ConnectorManager? connectorManager = GetConnectorManager(element);
            if (connectorManager == null)
            {
                return Array.Empty<ConnectorLink>();
            }

            var links = new List<ConnectorLink>();
            int connectorIndex = 1;
            foreach (Connector connector in connectorManager.Connectors)
            {
                ConnectorSet? allRefs = SafeReadReference(() => connector.AllRefs);
                if (allRefs != null)
                {
                    foreach (Connector referencedConnector in allRefs)
                    {
                        Element? neighborElement = SafeReadReference(() => referencedConnector.Owner);
                        if (neighborElement == null)
                        {
                            continue;
                        }

                        links.Add(new ConnectorLink(
                            element,
                            neighborElement,
                            connectorIndex,
                            FindConnectorIndex(neighborElement, referencedConnector),
                            SafeRead(() => connector.IsConnected, false) ? "Connected" : "AllRefs"));
                    }
                }

                connectorIndex++;
            }

            return links;
        }

        private static int CountOpenConnectors(Element element)
        {
            ConnectorManager? connectorManager = GetConnectorManager(element);
            if (connectorManager == null)
            {
                return 0;
            }

            int openConnectorCount = 0;
            foreach (Connector connector in connectorManager.Connectors)
            {
                if (!HasRealVentNeighbor(element, connector))
                {
                    openConnectorCount++;
                }
            }

            return openConnectorCount;
        }

        private static bool HasRealVentNeighbor(Element currentElement, Connector connector)
        {
            ConnectorSet? allRefs = SafeReadReference(() => connector.AllRefs);
            if (allRefs == null)
            {
                return false;
            }

            foreach (Connector referencedConnector in allRefs)
            {
                Element? owner = SafeReadReference(() => referencedConnector.Owner);
                if (owner == null)
                {
                    continue;
                }

                if (owner.Id.Value == currentElement.Id.Value)
                {
                    continue;
                }

                if (!IsSupportedVentilationElement(owner))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static void AddConnectionIfMissing(
            ICollection<VentNetworkConnection> connections,
            ISet<string> connectionKeys,
            ConnectorLink connectorLink)
        {
            long fromId = connectorLink.OwnerElement.Id.Value;
            long toId = connectorLink.NeighborElement.Id.Value;
            int fromConnectorIndex = connectorLink.OwnerConnectorIndex;
            int toConnectorIndex = connectorLink.NeighborConnectorIndex;

            if (toId < fromId)
            {
                (fromId, toId) = (toId, fromId);
                (fromConnectorIndex, toConnectorIndex) = (toConnectorIndex, fromConnectorIndex);
            }

            string connectionKey = $"{fromId}:{fromConnectorIndex}->{toId}:{toConnectorIndex}";
            if (!connectionKeys.Add(connectionKey))
            {
                return;
            }

            connections.Add(new VentNetworkConnection(
                fromId.ToString(),
                toId.ToString(),
                fromConnectorIndex,
                toConnectorIndex,
                connectorLink.ConnectionKind));
        }

        private static int FindConnectorIndex(Element ownerElement, Connector targetConnector)
        {
            ConnectorManager? connectorManager = GetConnectorManager(ownerElement);
            if (connectorManager == null)
            {
                return 0;
            }

            int index = 1;
            foreach (Connector connector in connectorManager.Connectors)
            {
                if (ReferenceEquals(connector, targetConnector))
                {
                    return index;
                }

                index++;
            }

            return 0;
        }

        private static ConnectorManager? GetConnectorManager(Element element)
        {
            var mepCurve = element as MEPCurve;
            if (mepCurve != null)
            {
                return mepCurve.ConnectorManager;
            }

            var familyInstance = element as FamilyInstance;
            if (familyInstance?.MEPModel != null)
            {
                return familyInstance.MEPModel.ConnectorManager;
            }

            return null;
        }

        private static bool IsSupportedVentilationElement(Element? element)
        {
            if (element?.Category == null)
            {
                return false;
            }

            var category = (BuiltInCategory)element.Category.Id.Value;
            return SupportedCategories.Contains(category);
        }

        private static bool IsCategory(Element element, BuiltInCategory category)
        {
            return element.Category != null && (BuiltInCategory)element.Category.Id.Value == category;
        }

        private static T SafeRead<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (Exception)
            {
                return fallback;
            }
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

        private sealed class ConnectorLink
        {
            public ConnectorLink(
                Element ownerElement,
                Element neighborElement,
                int ownerConnectorIndex,
                int neighborConnectorIndex,
                string connectionKind)
            {
                OwnerElement = ownerElement;
                NeighborElement = neighborElement;
                OwnerConnectorIndex = ownerConnectorIndex;
                NeighborConnectorIndex = neighborConnectorIndex;
                ConnectionKind = connectionKind;
            }

            public Element OwnerElement { get; }

            public Element NeighborElement { get; }

            public int OwnerConnectorIndex { get; }

            public int NeighborConnectorIndex { get; }

            public string ConnectionKind { get; }
        }
    }
}
