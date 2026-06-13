using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VentCalc.Core.Models
{
    public sealed class CalculationSectionInfo
    {
        public int SectionIndex { get; set; }

        public int PathIndex { get; set; }

        public List<long> ElementIds { get; set; } = new List<long>();

        public long StartElementId { get; set; }

        public long EndElementId { get; set; }

        public string Shape { get; set; } = string.Empty;

        public string Size { get; set; } = string.Empty;

        public double FlowM3h { get; set; }

        public double TotalLengthM { get; set; }

        public double AreaM2 { get; set; }

        public double EquivalentDiameterM { get; set; }

        public double VelocityMs { get; set; }

        public double Reynolds { get; set; }

        public double Lambda { get; set; }

        public double DynamicPressurePa { get; set; }

        public double SpecificPressureLossPaPerM { get; set; }

        public double FrictionPressureLossPa { get; set; }

        public string SplitReason { get; set; } = string.Empty;

        public string SplitReasonShort
        {
            get
            {
                if (SplitReason.Contains("Начало", System.StringComparison.OrdinalIgnoreCase)) return "Старт";
                bool size = SplitReason.Contains("размер", System.StringComparison.OrdinalIgnoreCase);
                bool flow = SplitReason.Contains("расход", System.StringComparison.OrdinalIgnoreCase);
                if (SplitReason.Contains("Короткий", System.StringComparison.OrdinalIgnoreCase)) return "Короткий участок присоединён";
                if (size && flow) return "Размер + расход";
                if (size) return "Размер";
                if (flow) return "Расход";
                return SplitReason;
            }
        }

        public bool ContainsShortDucts { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();

        public string ElementIdsText => ElementIds.Count == 0
            ? string.Empty
            : string.Join(", ", ElementIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));

        public string StartEndDisplay => string.Create(CultureInfo.InvariantCulture, $"{StartElementId}–{EndElementId}");

        public string WarningText => string.Join("; ", Warnings);
    }
}
