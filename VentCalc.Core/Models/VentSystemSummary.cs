namespace VentCalc.Core.Models
{
    public sealed class VentSystemSummary
    {
        public string SystemName { get; set; } = string.Empty;

        public string SystemType { get; set; } = string.Empty;

        public string Direction { get; set; } = string.Empty;

        public int ElementCount { get; set; }

        public int DuctCount { get; set; }

        public int FittingCount { get; set; }

        public int TerminalCount { get; set; }

        public int EquipmentCount { get; set; }
    }
}
