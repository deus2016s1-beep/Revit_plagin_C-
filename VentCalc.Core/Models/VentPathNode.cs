namespace VentCalc.Core.Models
{
    public sealed class VentPathNode
    {
        public VentPathNode(
            string elementId,
            string categoryName,
            string typeName,
            string familyName,
            string size,
            string flowM3h,
            double ductLengthMm)
        {
            ElementId = elementId;
            CategoryName = categoryName;
            TypeName = typeName;
            FamilyName = familyName;
            Size = size;
            FlowM3h = flowM3h;
            DuctLengthMm = ductLengthMm;
        }

        public string ElementId { get; }

        public string CategoryName { get; }

        public string TypeName { get; }

        public string FamilyName { get; }

        public string Size { get; }

        public string FlowM3h { get; }

        public double DuctLengthMm { get; }
    }
}
