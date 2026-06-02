using Microsoft.VisualStudio.TestTools.UnitTesting;
using VentCalc.Core;

namespace VentCalc.Tests
{
    [TestClass]
    public sealed class CalculationEngineTests
    {
        [TestMethod]
        public void Version_ReturnsVentCalcTwo()
        {
            var engine = new CalculationEngine();

            Assert.AreEqual("2.0", engine.Version);
        }
    }
}
