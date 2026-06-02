using System.Collections.Generic;

namespace VentCalc.Core.Models
{
    public sealed class DuctGeometryData
    {
        public long ElementId { get; set; }

        public string Size { get; set; } = string.Empty;

        public double FlowM3h { get; set; }

        public double LengthM { get; set; }

        public double WidthM { get; set; }

        public double HeightM { get; set; }

        public double DiameterM { get; set; }

        public bool IsRound { get; set; }

        public bool IsRectangular { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();
    }
}
