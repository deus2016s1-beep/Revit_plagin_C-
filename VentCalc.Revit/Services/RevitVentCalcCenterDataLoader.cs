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
            try
            {
                var selectionReader = new RevitSelectionReader();
                if (!selectionReader.TryGetSingleSelectedVentElement(uiDocument, out Element? element, out string? errorMessage))
                {
                    string message = ToCenterSelectionMessage(errorMessage);
                    return new VentCalcCenterData
                    {
                        Success = false,
                        IsUserSelectionWarning = true,
                        ErrorMessage = message,
                        ReportText = message,
                        Warnings = new List<string> { message }
                    };
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
                    + Environment.NewLine
                    + networkInfo.ToReportText(pathSummary)
                    + Environment.NewLine
                    + pathSummary.ToReportText()
                    + Environment.NewLine
                    + aerodynamicSummary.ToReportText();

                return new VentCalcCenterData
                {
                    Success = true,
                    SelectedElementInfo = elementInfo,
                    NetworkInfo = networkInfo,
                    PathSummary = pathSummary,
                    AerodynamicSummary = aerodynamicSummary,
                    ReportText = reportText
                };
            }
            catch (Exception exception)
            {
                ErrorReporter.Report(uiDocument.Application, "Ошибка загрузки данных VentCalc Center", exception);
                return new VentCalcCenterData
                {
                    Success = false,
                    ErrorMessage = exception.Message,
                    ReportText = exception.ToString(),
                    Warnings = new List<string> { "Ошибка при чтении вентиляционной сети. Окно оставлено открытым; смотрите лог VentCalc." }
                };
            }
        }

        private static string ToCenterSelectionMessage(string? selectionReaderMessage)
        {
            if (string.Equals(selectionReaderMessage, "Выберите элемент воздуховодной системы.", StringComparison.Ordinal))
            {
                return "Выбранный элемент не относится к вентиляционной системе.";
            }

            return "Выберите элемент воздуховодной системы и нажмите 'Загрузить выбранную систему'.";
        }
    }
}
