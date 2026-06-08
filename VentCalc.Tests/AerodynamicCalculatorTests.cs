using Microsoft.VisualStudio.TestTools.UnitTesting;
using VentCalc.Core.Models;
using VentCalc.Core.Services;

namespace VentCalc.Tests
{
    [TestClass]
    public sealed class AerodynamicCalculatorTests
    {
        [TestMethod]
        public void CalculateDuct_RectangularDuct_ComputesRealisticVelocity()
        {
            var duct = new DuctGeometryData
            {
                ElementId = 1,
                Size = "600x300",
                FlowM3h = 3600,
                LengthM = 10,
                WidthM = 0.6,
                HeightM = 0.3,
                IsRectangular = true
            };

            DuctCalculationInfo result = new AerodynamicCalculator().CalculateDuct(duct, new AerodynamicSettings());

            Assert.AreEqual(1.0, result.FlowM3s, 0.0001);
            Assert.AreEqual(0.18, result.AreaM2, 0.0001);
            Assert.AreEqual(0.4, result.EquivalentDiameterM, 0.0001);
            Assert.AreEqual(5.56, result.VelocityMs, 0.01);
            Assert.IsTrue(result.Reynolds > 0);
            Assert.IsTrue(result.Lambda > 0);
            Assert.IsTrue(result.FrictionPressureLossPa > 0);
        }
    }
}
