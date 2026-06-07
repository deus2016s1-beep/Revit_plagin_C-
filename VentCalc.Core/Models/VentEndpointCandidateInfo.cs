namespace VentCalc.Core.Models
{
    public sealed class VentEndpointCandidateInfo
    {
        public string ElementId { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string FamilyName { get; set; } = string.Empty;

        public string TypeName { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public int ConnectorCount { get; set; }

        public int ConnectedHvacConnectorCount { get; set; }

        public int GraphDegree { get; set; }

        public string Reason { get; set; } = string.Empty;
    }
}
