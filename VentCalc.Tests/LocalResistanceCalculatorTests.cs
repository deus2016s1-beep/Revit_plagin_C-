using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VentCalc.Core.Models;
using VentCalc.Core.Services;

namespace VentCalc.Tests
{
    [TestClass]
    public sealed class LocalResistanceCalculatorTests
    {
        [TestMethod]
        public void CalculatePath_CommentZeta_AddsLocalPressureLossToTotal()
        {
            var path = new VentPathInfo(
                1,
                "100",
                "300",
                new[] { "100", "200", "300" },
                new[]
                {
                    PathNode("100", "OST_DuctCurves", "Воздуховоды", "600x300"),
                    PathNode("200", "OST_DuctFitting", "Соединительные детали воздуховодов", "Отвод 90"),
                    PathNode("300", "OST_DuctTerminal", "Воздухораспределители", "Решетка")
                },
                ductCount: 1,
                fittingCount: 1,
                accessoryCount: 0,
                terminalCount: 1,
                equipmentCount: 0,
                totalElementCount: 3,
                totalDuctLengthMm: 2000,
                maxFlowM3h: "3600",
                pathKind: "Supply");
            var ductData = new Dictionary<long, DuctGeometryData>
            {
                [100] = new DuctGeometryData
                {
                    ElementId = 100,
                    Size = "600x300",
                    FlowM3h = 3600,
                    LengthM = 2,
                    WidthM = 0.6,
                    HeightM = 0.3,
                    IsRectangular = true
                }
            };
            var localData = new Dictionary<long, LocalResistanceElementData>
            {
                [200] = new LocalResistanceElementData
                {
                    ElementId = 200,
                    CategoryKey = "OST_DuctFitting",
                    CategoryName = "Соединительные детали воздуховодов",
                    TypeName = "Отвод 90",
                    Comments = "ζ = 1,2"
                },
                [300] = new LocalResistanceElementData
                {
                    ElementId = 300,
                    CategoryKey = "OST_DuctTerminal",
                    CategoryName = "Воздухораспределители",
                    TypeName = "Решетка"
                }
            };

            PathCalculationInfo result = new AerodynamicCalculator().CalculatePath(path, ductData, localData, new AerodynamicSettings());

            LocalResistanceCalculationInfo elbow = result.LocalResistances.Single(local => local.ElementId == 200);
            LocalResistanceCalculationInfo grille = result.LocalResistances.Single(local => local.ElementId == 300);
            Assert.AreEqual("Комментарии", elbow.Source);
            Assert.AreEqual(1.2, elbow.Zeta, 0.0001);
            Assert.AreEqual(2.0, grille.Zeta, 0.0001);
            Assert.AreEqual("Рекомендовано", grille.Source);
            Assert.IsTrue(result.TotalLocalPressureLossPa > 0);
            Assert.AreEqual(result.TotalFrictionPressureLossPa + result.TotalLocalPressureLossPa, result.TotalPressureLossPa, 0.0001);
        }

        private static VentPathNode PathNode(string elementId, string categoryKey, string categoryName, string typeName)
        {
            return new VentPathNode(elementId, categoryName, categoryKey, typeName, typeName, "600x300", "3600", 1000);
        }
    }
}
