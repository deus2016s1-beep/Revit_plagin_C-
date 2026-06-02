using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using VentCalc.Core.Models;

namespace VentCalc.Revit.Services
{
    public sealed class RevitConnectorReader
    {
        public IReadOnlyList<VentConnectorInfo> Read(Element element)
        {
            ConnectorManager connectorManager = GetConnectorManager(element);
            if (connectorManager == null)
            {
                return Array.Empty<VentConnectorInfo>();
            }

            var result = new List<VentConnectorInfo>();
            int number = 1;
            foreach (Connector connector in connectorManager.Connectors)
            {
                ConnectorSet allRefs = SafeRead(() => connector.AllRefs, null);
                var connectedElementIds = ReadConnectedElementIds(element, allRefs);

                result.Add(new VentConnectorInfo(
                    number,
                    SafeRead(() => connector.ConnectorType.ToString(), "—"),
                    SafeRead(() => connector.Domain.ToString(), "—"),
                    SafeRead(() => connector.Direction.ToString(), "—"),
                    SafeRead(() => connector.Shape.ToString(), "—"),
                    FormatConnectorLength(SafeReadNullable(() => connector.Radius)),
                    FormatConnectorLength(SafeReadNullable(() => connector.Width)),
                    FormatConnectorLength(SafeReadNullable(() => connector.Height)),
                    SafeRead(() => connector.IsConnected, false),
                    CountConnectorRefs(allRefs),
                    connectedElementIds));

                number++;
            }

            return result;
        }

        private static ConnectorManager GetConnectorManager(Element element)
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

        private static IReadOnlyList<string> ReadConnectedElementIds(Element owner, ConnectorSet allRefs)
        {
            if (allRefs == null)
            {
                return Array.Empty<string>();
            }

            var ids = new SortedSet<string>();
            foreach (Connector referencedConnector in allRefs)
            {
                Element referencedOwner = SafeRead(() => referencedConnector.Owner, null);
                if (referencedOwner != null && referencedOwner.Id.IntegerValue != owner.Id.IntegerValue)
                {
                    ids.Add(referencedOwner.Id.IntegerValue.ToString());
                }
            }

            return ids.ToList();
        }

        private static int CountConnectorRefs(ConnectorSet allRefs)
        {
            if (allRefs == null)
            {
                return 0;
            }

            int count = 0;
            foreach (Connector connector in allRefs)
            {
                count++;
            }

            return count;
        }

        private static string FormatConnectorLength(double? valueInFeet)
        {
            if (!valueInFeet.HasValue)
            {
                return "—";
            }

            double millimeters = valueInFeet.Value * 304.8;
            return $"{millimeters:0.###} мм";
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

        private static double? SafeReadNullable(Func<double> read)
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
    }
}
