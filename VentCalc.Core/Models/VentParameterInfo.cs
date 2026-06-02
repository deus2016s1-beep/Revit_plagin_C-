namespace VentCalc.Core.Models
{
    public sealed class VentParameterInfo
    {
        public VentParameterInfo(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }

        public string Value { get; }
    }
}
