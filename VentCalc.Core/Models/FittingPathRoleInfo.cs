using System.Collections.Generic;

namespace VentCalc.Core.Models
{
    public sealed class FittingPathRoleInfo
    {
        public long ElementId { get; set; }

        public int PathIndex { get; set; }

        public string FittingKind { get; set; } = string.Empty;

        public string PathRole { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public long? PreviousDuctElementId { get; set; }

        public long? NextDuctElementId { get; set; }

        public double PreviousAreaM2 { get; set; }

        public double NextAreaM2 { get; set; }

        public double PreviousFlowM3h { get; set; }

        public double NextFlowM3h { get; set; }

        public double? ActualAngleDeg { get; set; }

        public double? RoundedAngleDeg { get; set; }

        public bool AngleWasRounded { get; set; }

        public string AngleRoundingWarning { get; set; } = string.Empty;

        public List<string> Warnings { get; set; } = new List<string>();

        public string WarningText => string.Join("; ", Warnings);
    }
}
