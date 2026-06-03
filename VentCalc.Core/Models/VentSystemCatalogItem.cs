namespace VentCalc.Core.Models
{
    public sealed class VentSystemCatalogItem
    {
        public string SystemName { get; set; } = string.Empty;

        public string SystemType { get; set; } = string.Empty;

        public string Direction { get; set; } = string.Empty;

        public int ElementCount { get; set; }

        public int DuctCount { get; set; }

        public int FittingCount { get; set; }

        public int TerminalCount { get; set; }

        public int EquipmentCount { get; set; }

        public long RepresentativeElementId { get; set; }

        public int ComponentCount { get; set; } = 1;

        public string DisplayName { get; set; } = string.Empty;
    }
}
