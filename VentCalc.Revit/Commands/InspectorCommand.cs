using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VentCalc.Core.Models;
using VentCalc.Revit.Services;
using VentCalc.UI;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class InspectorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument == null)
            {
                TaskDialog.Show("VentCalc", "Откройте документ Revit и выберите элемент воздуховодной системы.");
                return Result.Cancelled;
            }

            var selectionReader = new RevitSelectionReader();
            if (!selectionReader.TryGetSingleSelectedVentElement(uiDocument, out Element? element, out string? errorMessage))
            {
                TaskDialog.Show("VentCalc", errorMessage);
                return Result.Cancelled;
            }

            var parameterReader = new RevitParameterReader();
            var connectorReader = new RevitConnectorReader();
            var elementInfoReader = new RevitElementInfoReader(parameterReader, connectorReader);
            var networkReader = new RevitVentNetworkReader(elementInfoReader);

            VentElementInfo elementInfo = elementInfoReader.Read(element);
            VentNetworkInfo networkInfo = networkReader.Read(uiDocument.Document, element.Id);
            string reportText = elementInfo.ToReportText() + System.Environment.NewLine + networkInfo.ToReportText();
            var window = new InspectorResultWindow(reportText);
            window.ShowDialog();

            return Result.Succeeded;
        }
    }
}
