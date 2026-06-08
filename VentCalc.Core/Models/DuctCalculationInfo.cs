using System.Collections.Generic;

namespace VentCalc.Core.Models
{
    public sealed class DuctCalculationInfo
    {
        public long ElementId { get; set; }

        public string Size { get; set; } = string.Empty;

        public double FlowM3h { get; set; }

        public double FlowM3s { get; set; }

        public double LengthM { get; set; }

        public double WidthM { get; set; }

        public double HeightM { get; set; }

        public double DiameterM { get; set; }

        public double AreaM2 { get; set; }

        public double EquivalentDiameterM { get; set; }

        public double VelocityMs { get; set; }

        public double Reynolds { get; set; }

        public double Lambda { get; set; }

        public double DynamicPressurePa { get; set; }

        public double SpecificPressureLossPaPerM { get; set; }

        public double FrictionPressureLossPa { get; set; }

        public bool IsRound { get; set; }

        public bool IsRectangular { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();
    }
}
