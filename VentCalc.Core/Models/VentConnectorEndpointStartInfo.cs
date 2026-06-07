namespace VentCalc.Core.Models
{
    public sealed class VentConnectorEndpointStartInfo
    {
        public long ElementId { get; set; }

        public string ConnectorKey { get; set; } = string.Empty;

        public string ConnectorOrigin { get; set; } = string.Empty;

        public string ConnectorDirection { get; set; } = string.Empty;

        public long ConnectedElementId { get; set; }

        public string ConnectedElementCategory { get; set; } = string.Empty;

        public string SystemName { get; set; } = string.Empty;

        public string EndpointRole { get; set; } = string.Empty;

        public double FlowM3h { get; set; }

        public bool PathFound { get; set; }

        public int? PathIndex { get; set; }

        public string RejectionReason { get; set; } = string.Empty;

        public string LogicalKey => $"{ElementId}|{ConnectorKey}|{ConnectedElementId}";
    }
}
