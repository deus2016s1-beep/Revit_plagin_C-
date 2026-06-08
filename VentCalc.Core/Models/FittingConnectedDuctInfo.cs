namespace VentCalc.Core.Models
{
    public sealed class FittingConnectedDuctInfo
    {
        public long ElementId { get; set; }

        public double DirectionX { get; set; }

        public double DirectionY { get; set; }

        public double DirectionZ { get; set; }

        public bool HasDirection { get; set; }
    }
}
