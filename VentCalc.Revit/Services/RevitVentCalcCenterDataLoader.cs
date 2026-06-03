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
        private static readonly HashSet<BuiltInCategory> SupportedCategories = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_MechanicalEquipment
        };

        public VentCalcCenterData Load(UIDocument uiDocument, AerodynamicSettings settings)
        {
            try
            {
                var selectionReader = new RevitSelectionReader();
                if (!selectionReader.TryGetSingleSelectedVentElement(uiDocument, out Element? element, out string? errorMessage))
                {
                    string message = ToCenterSelectionMessage(errorMessage);
                    return CreateWarningData(uiDocument, message);
                }

                return LoadElement(uiDocument, element, settings);
            }
            catch (Exception exception)
            {
                return CreateExceptionData(uiDocument, exception);
            }
        }

        public VentCalcCenterData Load(UIDocument uiDocument, AerodynamicSettings settings, ElementId selectedElementId)
        {
            try
            {
                Element? element = uiDocument.Document.GetElement(selectedElementId);
                if (!IsSupportedVentilationElement(element))
                {
                    return CreateWarningData(uiDocument, "Выбранный элемент не относится к вентиляционной системе.");
                }

                return LoadElement(uiDocument, element!, settings);
            }
            catch (Exception exception)
            {
                return CreateExceptionData(uiDocument, exception);
            }
        }

        private VentCalcCenterData LoadElement(UIDocument uiDocument, Element element, AerodynamicSettings settings)
        {
            try
            {
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

                VentCalcCenterData data = CreateBaseData(uiDocument);
                data.Success = true;
                data.SelectedElementInfo = elementInfo;
                data.NetworkInfo = networkInfo;
                data.PathSummary = pathSummary;
                data.AerodynamicSummary = aerodynamicSummary;
                data.ReportText = reportText;
                return data;
            }
            catch (Exception exception)
            {
                return CreateExceptionData(uiDocument, exception);
            }
        }

        private static VentCalcCenterData CreateWarningData(UIDocument uiDocument, string message)
        {
            VentCalcCenterData data = CreateBaseData(uiDocument);
            data.Success = false;
            data.IsUserSelectionWarning = true;
            data.ErrorMessage = message;
            data.ReportText = message;
            data.Warnings = new List<string> { message };
            return data;
        }

        private static VentCalcCenterData CreateExceptionData(UIDocument uiDocument, Exception exception)
        {
            ErrorReporter.Report(uiDocument.Application, "Ошибка загрузки данных VentCalc Center", exception);
            VentCalcCenterData data = CreateBaseData(uiDocument);
            data.Success = false;
            data.ErrorMessage = exception.Message;
            data.ReportText = exception.ToString();
            data.Warnings = new List<string> { "Ошибка при чтении вентиляционной сети. Окно оставлено открытым; смотрите лог VentCalc." };
            return data;
        }

        private static VentCalcCenterData CreateBaseData(UIDocument uiDocument)
        {
            return new VentCalcCenterData
            {
                RevitVersion = uiDocument.Application.Application.VersionNumber,
                RevitFilePath = string.IsNullOrWhiteSpace(uiDocument.Document.PathName) ? uiDocument.Document.Title : uiDocument.Document.PathName,
                LoadedAt = DateTime.Now
            };
        }

        private static string ToCenterSelectionMessage(string? selectionReaderMessage)
        {
            if (string.Equals(selectionReaderMessage, "Выберите элемент воздуховодной системы.", StringComparison.Ordinal))
            {
                return "Выбранный элемент не относится к вентиляционной системе.";
            }

            return "Выберите один элемент воздуховодной системы в Revit.";
        }

        private static bool IsSupportedVentilationElement(Element? element)
        {
            if (element?.Category == null)
            {
                return false;
            }

            var category = (BuiltInCategory)element.Category.Id.Value;
            return SupportedCategories.Contains(category);
        }
    }
}
