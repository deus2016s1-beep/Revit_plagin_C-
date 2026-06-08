using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VentCalc.Core.Models
{
    public sealed class VentNetworkNode
    {
        public VentNetworkNode(
            string elementId,
            string categoryName,
            string categoryKey,
            string name,
            string typeName,
            string familyName,
            string systemName,
            string systemType,
            string flowM3h,
            string size,
            string levelName,
            int connectorCount,
            int openConnectorCount,
            double ductLengthMm,
            IEnumerable<string> connectedElementIds)
        {
            ElementId = elementId;
            CategoryName = categoryName;
            CategoryKey = categoryKey;
            Name = name;
            TypeName = typeName;
            FamilyName = familyName;
            SystemName = systemName;
            SystemType = systemType;
            FlowM3h = flowM3h;
            Size = size;
            LevelName = levelName;
            ConnectorCount = connectorCount;
            OpenConnectorCount = openConnectorCount;
            DuctLengthMm = ductLengthMm;
            ConnectedElementIds = new ReadOnlyCollection<string>(connectedElementIds.ToList());
            Role = VentNodeRole.Unknown;
            PathRoleReason = string.Empty;
        }

        public string ElementId { get; }

        public string CategoryName { get; }

        public string CategoryKey { get; }

        public string Name { get; }

        public string TypeName { get; }

        public string FamilyName { get; }

        public string SystemName { get; }

        public string SystemType { get; }

        public string FlowM3h { get; }

        public string Size { get; }

        public string LevelName { get; }

        public int ConnectorCount { get; }

        public int OpenConnectorCount { get; }

        public double DuctLengthMm { get; }

        public IReadOnlyList<string> ConnectedElementIds { get; }

        public VentNodeRole Role { get; set; }

        public bool IsStartCandidate { get; set; }

        public bool IsEndCandidate { get; set; }

        public bool IsIgnoredForPathSearch { get; set; }

        public string PathRoleReason { get; set; }
    }
}
