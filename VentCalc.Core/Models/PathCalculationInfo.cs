using System.Collections.Generic;

namespace VentCalc.Core.Models
{
    public sealed class PathCalculationInfo
    {
        public int PathIndex { get; set; }

        public long StartElementId { get; set; }

        public long EndElementId { get; set; }

        public List<long> ElementIds { get; set; } = new List<long>();

        public List<DuctCalculationInfo> Ducts { get; set; } = new List<DuctCalculationInfo>();

        public List<LocalResistanceCalculationInfo> LocalResistances { get; set; } = new List<LocalResistanceCalculationInfo>();

        public double TotalDuctLengthM { get; set; }

        public double MaxVelocityMs { get; set; }

        public double MinVelocityMs { get; set; }

        public double TotalFrictionPressureLossPa { get; set; }

        public double TotalLocalPressureLossPa { get; set; }

        public double TotalPressureLossPa { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();
    }
}
