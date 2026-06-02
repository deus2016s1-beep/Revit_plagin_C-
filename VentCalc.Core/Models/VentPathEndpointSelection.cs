using System.Collections.Generic;

namespace VentCalc.Core.Models
{
    public sealed class VentPathEndpointSelection
    {
        public string SystemDirection { get; set; } = "Unknown";

        public string DirectionReason { get; set; } = string.Empty;

        public List<long> StartElementIds { get; set; } = new List<long>();

        public List<long> EndElementIds { get; set; } = new List<long>();

        public List<long> IgnoredCapElementIds { get; set; } = new List<long>();

        public List<string> Warnings { get; set; } = new List<string>();
    }
}
