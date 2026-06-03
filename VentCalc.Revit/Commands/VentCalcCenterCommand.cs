using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VentCalc.Revit.Services;
using VentCalc.UI.Services;
using VentCalc.UI.ViewModels;
using VentCalc.UI.Views;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class VentCalcCenterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument == null)
            {
                TaskDialog.Show("VentCalc", "Откройте документ Revit и выберите элемент воздуховодной системы.");
                return Result.Cancelled;
            }

            var settingsService = new VentCalcSettingsService();
            var loader = new RevitVentCalcCenterDataLoader();
            VentCalcCenterViewModel? viewModel = null;
            viewModel = new VentCalcCenterViewModel(
                () => loader.Load(uiDocument, viewModel?.Settings.ToAerodynamicSettings() ?? settingsService.Load().ToAerodynamicSettings()),
                elementId => SelectElement(uiDocument, elementId),
                text => TaskDialog.Show("VentCalc", text),
                settingsService);

            var window = new VentCalcCenterWindow(viewModel);
            window.ShowDialog();
            return Result.Succeeded;
        }

        private static void SelectElement(UIDocument uiDocument, long elementId)
        {
            var revitElementId = new ElementId(elementId);
            uiDocument.Selection.SetElementIds(new List<ElementId> { revitElementId });
            uiDocument.ShowElements(revitElementId);
        }
    }
}
