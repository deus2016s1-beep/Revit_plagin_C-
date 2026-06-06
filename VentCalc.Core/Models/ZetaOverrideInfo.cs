namespace VentCalc.Core.Models
{
    public sealed class ZetaOverrideInfo
    {
        public string SystemName { get; set; } = string.Empty;

        public long ElementId { get; set; }

        public string PathRole { get; set; } = string.Empty;

        public long? PreviousDuctElementId { get; set; }

        public long? NextDuctElementId { get; set; }

        public double Zeta { get; set; }

        public string OverrideKey { get; set; } = string.Empty;
    }
}
