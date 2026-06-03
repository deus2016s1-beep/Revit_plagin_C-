using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VentCalc.Core.Models;
using VentCalc.Core.Services;
using VentCalc.UI.ViewModels;

namespace VentCalc.Revit.Services
{
    public sealed class RevitVentCalcCenterDataLoader
    {
        public VentCalcCenterData Load(UIDocument uiDocument, AerodynamicSettings settings)
        {
            var selectionReader = new RevitSelectionReader();
            if (!selectionReader.TryGetSingleSelectedVentElement(uiDocument, out Element? element, out string? errorMessage))
            {
                throw new InvalidOperationException(errorMessage ?? "Выберите ровно один элемент воздуховодной системы.");
            }

            var parameterReader = new RevitParameterReader();
            var connectorReader = new RevitConnectorReader();
            var elementInfoReader = new RevitElementInfoReader(parameterReader, connectorReader);
            var networkReader = new RevitVentNetworkReader(elementInfoReader);
            var pathDataReader = new RevitVentPathDataReader(elementInfoReader);
            var pathFinder = new VentPathFinder();
            var ductGeometryReader = new RevitDuctGeometryReader();
            var localResistanceDataReader = new RevitLocalResistanceDataReader();
            var aerodynamicCalculator = new AerodynamicCalculator();

            VentElementInfo elementInfo = elementInfoReader.Read(element);
            VentNetworkInfo networkInfo = pathDataReader.Enrich(uiDocument.Document, networkReader.Read(uiDocument.Document, element.Id));
            VentPathSummary pathSummary = pathFinder.FindPaths(networkInfo);
            IReadOnlyDictionary<long, DuctGeometryData> ductDataByElementId = ductGeometryReader.ReadDucts(uiDocument.Document, networkInfo);
            IReadOnlyDictionary<long, LocalResistanceElementData> localDataByElementId = localResistanceDataReader.ReadElements(uiDocument.Document, networkInfo);
            AerodynamicCalculationSummary aerodynamicSummary = aerodynamicCalculator.CalculatePaths(
                pathSummary.Paths,
                ductDataByElementId,
                localDataByElementId,
                settings);
            string reportText = elementInfo.ToReportText()
                + System.Environment.NewLine
                + networkInfo.ToReportText(pathSummary)
                + System.Environment.NewLine
                + pathSummary.ToReportText()
                + System.Environment.NewLine
                + aerodynamicSummary.ToReportText();

            return new VentCalcCenterData
            {
                SelectedElementInfo = elementInfo,
                NetworkInfo = networkInfo,
                PathSummary = pathSummary,
                AerodynamicSummary = aerodynamicSummary,
                ReportText = reportText
            };
        }
    }
}
