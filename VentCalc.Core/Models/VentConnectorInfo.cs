using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VentCalc.Core.Models
{
    public sealed class VentConnectorInfo
    {
        public VentConnectorInfo(
            int number,
            string connectorType,
            string domain,
            string direction,
            string shape,
            string radius,
            string width,
            string height,
            bool isConnected,
            int allRefsCount,
            IEnumerable<string> connectedElementIds)
        {
            Number = number;
            ConnectorType = connectorType;
            Domain = domain;
            Direction = direction;
            Shape = shape;
            Radius = radius;
            Width = width;
            Height = height;
            IsConnected = isConnected;
            AllRefsCount = allRefsCount;
            ConnectedElementIds = new ReadOnlyCollection<string>(connectedElementIds.ToList());
        }

        public int Number { get; }

        public string ConnectorType { get; }

        public string Domain { get; }

        public string Direction { get; }

        public string Shape { get; }

        public string Radius { get; }

        public string Width { get; }

        public string Height { get; }

        public bool IsConnected { get; }

        public int AllRefsCount { get; }

        public IReadOnlyList<string> ConnectedElementIds { get; }
    }
}
