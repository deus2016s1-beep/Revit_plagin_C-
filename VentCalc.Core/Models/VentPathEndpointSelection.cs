using System.Collections.Generic;

namespace VentCalc.Core.Models
{
    public sealed class VentPathEndpointSelection
    {
        public string SystemDirection { get; set; } = "Unknown";

        public string DirectionReason { get; set; } = string.Empty;

        public bool FallbackUsed { get; set; }

        public List<long> StartElementIds { get; set; } = new List<long>();

        public List<long> EndElementIds { get; set; } = new List<long>();

        public List<long> IgnoredCapElementIds { get; set; } = new List<long>();

        public List<VentEndpointCandidateInfo> StartCandidates { get; set; } = new List<VentEndpointCandidateInfo>();

        public List<VentEndpointCandidateInfo> EndCandidates { get; set; } = new List<VentEndpointCandidateInfo>();

        public List<VentEndpointCandidateInfo> RejectedCandidates { get; set; } = new List<VentEndpointCandidateInfo>();

        public List<VentEndpointCandidateInfo> OpenEndCandidates { get; set; } = new List<VentEndpointCandidateInfo>();

        public List<VentEndpointCandidateInfo> HoodCandidates { get; set; } = new List<VentEndpointCandidateInfo>();

        public List<VentEndpointCandidateInfo> FanCandidates { get; set; } = new List<VentEndpointCandidateInfo>();

        public List<string> Warnings { get; set; } = new List<string>();
    }
}
