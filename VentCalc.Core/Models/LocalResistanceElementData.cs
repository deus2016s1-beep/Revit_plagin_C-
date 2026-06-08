using System.Collections.Generic;

namespace VentCalc.Core.Models
{
    public sealed class LocalResistanceElementData
    {
        public long ElementId { get; set; }

        public string CategoryKey { get; set; } = string.Empty;

        public string CategoryName { get; set; } = string.Empty;

        public string FamilyName { get; set; } = string.Empty;

        public string TypeName { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Size { get; set; } = string.Empty;

        public string Comments { get; set; } = string.Empty;

        public string SystemName { get; set; } = string.Empty;

        public string SystemType { get; set; } = string.Empty;

        public List<ZetaOverrideInfo> ZetaOverrides { get; set; } = new List<ZetaOverrideInfo>();

        public List<ProjectZetaCatalogItem> ProjectZetaCatalog { get; set; } = new List<ProjectZetaCatalogItem>();

        public List<FittingConnectedDuctInfo> ConnectedDucts { get; set; } = new List<FittingConnectedDuctInfo>();

        public List<string> Warnings { get; set; } = new List<string>();
    }
}
