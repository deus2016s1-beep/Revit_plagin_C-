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
            string name,
            string typeName,
            string familyName,
            string systemName,
            string systemType,
            string flowM3h,
            string size,
            string levelName,
            int connectorCount,
            IEnumerable<string> connectedElementIds)
        {
            ElementId = elementId;
            CategoryName = categoryName;
            Name = name;
            TypeName = typeName;
            FamilyName = familyName;
            SystemName = systemName;
            SystemType = systemType;
            FlowM3h = flowM3h;
            Size = size;
            LevelName = levelName;
            ConnectorCount = connectorCount;
            ConnectedElementIds = new ReadOnlyCollection<string>(connectedElementIds.ToList());
        }

        public string ElementId { get; }

        public string CategoryName { get; }

        public string Name { get; }

        public string TypeName { get; }

        public string FamilyName { get; }

        public string SystemName { get; }

        public string SystemType { get; }

        public string FlowM3h { get; }

        public string Size { get; }

        public string LevelName { get; }

        public int ConnectorCount { get; }

        public IReadOnlyList<string> ConnectedElementIds { get; }
    }
}
