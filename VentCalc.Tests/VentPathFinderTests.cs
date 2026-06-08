using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VentCalc.Core.Models;
using VentCalc.Core.Services;

namespace VentCalc.Tests
{
    [TestClass]
    public sealed class VentPathFinderTests
    {
        [TestMethod]
        public void FindPaths_SupplyNetworkWithCaps_UsesOpenEndAndTerminalsOnly()
        {
            var network = new VentNetworkInfo(
                "8602891",
                new[]
                {
                    Node("8602891", "OST_DuctCurves", "Воздуховоды", "Магистраль", "Воздуховод", "", "Приточный воздух", openConnectorCount: 1, ductLengthMm: 1000),
                    Node("8604856", "OST_DuctCurves", "Воздуховоды", "Участок", "Воздуховод", "", "Приточный воздух", ductLengthMm: 1000),
                    Node("8602871", "OST_DuctFitting", "Соединительные детали воздуховодов", "Тройник", "Тройник", "", "Приточный воздух"),
                    Node("8603000", "OST_DuctCurves", "Воздуховоды", "Ветка", "Воздуховод", "", "Приточный воздух", ductLengthMm: 500),
                    Node("8603001", "OST_DuctCurves", "Воздуховоды", "Ветка", "Воздуховод", "", "Приточный воздух", ductLengthMm: 500),
                    Node("8600549", "OST_DuctTerminal", "Воздухораспределители", "Решётка", "Решётка", "", "Приточный воздух"),
                    Node("8600876", "OST_DuctTerminal", "Воздухораспределители", "Решётка", "Решётка", "", "Приточный воздух"),
                    Node("8600950", "OST_DuctTerminal", "Воздухораспределители", "Решётка", "Решётка", "", "Приточный воздух"),
                    Node("8600951", "OST_DuctTerminal", "Воздухораспределители", "Решётка", "Решётка", "", "Приточный воздух"),
                    Node("8601362", "OST_DuctFitting", "Соединительные детали воздуховодов", "Заглушка", "ADSK_Заглушка_Прямоугольный", "ADSK_Заглушка_Прямоугольный", "Приточный воздух", openConnectorCount: 1),
                    Node("8602238", "OST_DuctFitting", "Соединительные детали воздуховодов", "Заглушка", "ADSK_Заглушка_Прямоугольный", "ADSK_Заглушка_Прямоугольный", "Приточный воздух", openConnectorCount: 1)
                },
                new[]
                {
                    Connection("8602891", "8604856"),
                    Connection("8604856", "8602871"),
                    Connection("8602871", "8603000"),
                    Connection("8602871", "8603001"),
                    Connection("8603000", "8600549"),
                    Connection("8603000", "8600876"),
                    Connection("8603001", "8600950"),
                    Connection("8603001", "8600951"),
                    Connection("8604856", "8601362"),
                    Connection("8602871", "8602238")
                },
                ductCount: 4,
                fittingCount: 3,
                accessoryCount: 0,
                terminalCount: 4,
                equipmentCount: 0,
                openConnectorCount: 3,
                startCandidateCount: 0,
                endCandidateCount: 0);

            VentPathSummary summary = new VentPathFinder().FindPaths(network);

            Assert.AreEqual("Supply / Приточная система", summary.Direction);
            Assert.AreEqual("8602891", summary.StartElementIds.Single());
            CollectionAssert.AreEquivalent(new[] { "8600549", "8600876", "8600950", "8600951" }, summary.EndElementIds.ToArray());
            Assert.AreEqual(4, summary.Paths.Count);
            CollectionAssert.AreEquivalent(new[] { "8601362", "8602238" }, summary.IgnoredCapDetails.Select(detail => detail.Split('|')[0].Trim()).ToArray());
            Assert.IsTrue(summary.Warnings.Any(warning => warning.Contains("Оборудование не найдено")));
            Assert.IsTrue(summary.Paths.All(path => path.StartElementId == "8602891"));
            Assert.IsTrue(summary.Paths.All(path => path.DuctCount + path.FittingCount + path.AccessoryCount + path.TerminalCount + path.EquipmentCount <= path.TotalElementCount));
        }

        private static VentNetworkNode Node(
            string id,
            string categoryKey,
            string categoryName,
            string name,
            string typeName,
            string familyName,
            string systemType,
            int openConnectorCount = 0,
            double ductLengthMm = 0)
        {
            return new VentNetworkNode(
                id,
                categoryName,
                categoryKey,
                name,
                typeName,
                familyName,
                "П 1",
                systemType,
                "100 м³/ч",
                "—",
                "—",
                2,
                openConnectorCount,
                ductLengthMm,
                Enumerable.Empty<string>());
        }

        private static VentNetworkConnection Connection(string fromElementId, string toElementId)
        {
            return new VentNetworkConnection(fromElementId, toElementId, 1, 1, "Connected");
        }
    }
}
