using System.Collections.Generic;

namespace VentCalc.Core.Models
{
    public sealed class LocalResistanceCalculationInfo
    {
        public long ElementId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public string FamilyName { get; set; } = string.Empty;

        public string TypeName { get; set; } = string.Empty;

        public string Size { get; set; } = string.Empty;

        public string LocalKind { get; set; } = string.Empty;

        public string PathRole { get; set; } = string.Empty;

        public string RoleReason { get; set; } = string.Empty;

        public long? PreviousDuctElementId { get; set; }

        public long? NextDuctElementId { get; set; }

        public double PreviousAreaM2 { get; set; }

        public double NextAreaM2 { get; set; }

        public double PreviousFlowM3h { get; set; }

        public double NextFlowM3h { get; set; }

        public double Zeta { get; set; }

        public double FlowM3h { get; set; }

        public double AreaM2 { get; set; }

        public double VelocityMs { get; set; }

        public double DynamicPressurePa { get; set; }

        public double LocalPressureLossPa { get; set; }

        public string Source { get; set; } = string.Empty;

        public List<string> Warnings { get; set; } = new List<string>();

        public string WarningText => string.Join("; ", Warnings);
    }
}
