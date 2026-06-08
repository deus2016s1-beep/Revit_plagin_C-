namespace VentCalc.Core.Models
{
    public sealed class VentIssueInfo
    {
        public string Severity { get; set; } = string.Empty;

        public string ElementId { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string Recommendation { get; set; } = string.Empty;
    }
}
