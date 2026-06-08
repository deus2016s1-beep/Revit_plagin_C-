namespace VentCalc.Core.Models
{
    public sealed class AerodynamicSettings
    {
        public double AirDensityKgM3 { get; set; } = 1.2;

        public double AirDynamicViscosityPaS { get; set; } = 0.0000181;

        public double RoughnessM { get; set; } = 0.0001;
    }
}
