using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VentCalc.Revit.ExternalEvents;
using VentCalc.Revit.Services;
using VentCalc.UI.Services;
using VentCalc.UI.ViewModels;

namespace VentCalc.Revit.Commands
{
    internal static class RibbonHighlightExecutor
    {
        private const string NeedSystemMessage = "Выберите элемент вентиляционной системы или сначала рассчитайте систему в VentCalc.";

        public static Result ExecuteCriticalPath(ExternalCommandData commandData, ref string message)
        {
            return Execute(commandData, ref message, vm => vm.HighlightCriticalPathFromRibbon());
        }

        public static Result ExecuteVelocityMap(ExternalCommandData commandData, ref string message)
        {
            return Execute(commandData, ref message, vm => vm.ApplyVelocityHighlightFromRibbon());
        }

        private static Result Execute(ExternalCommandData commandData, ref string message, Action<VentCalcCenterViewModel> action)
        {
            UIApplication uiApplication = commandData.Application;
            UIDocument? uiDocument = uiApplication.ActiveUIDocument;
            if (uiDocument == null)
            {
                TaskDialog.Show("VentCalc", NeedSystemMessage);
                return Result.Cancelled;
            }

            var settingsService = new VentCalcSettingsService();
            settingsService.Load();
            var highlightHandler = new ApplyHighlightExternalEventHandler(null);
            var viewModel = new VentCalcCenterViewModel(
                (_, _) => { },
                null,
                null,
                text => TaskDialog.Show("VentCalc", text),
                settingsService,
                null,
                (vm, request) =>
                {
                    HighlightResult result = highlightHandler.ApplyNow(uiApplication, request);
                    vm.ApplyHighlightResult(result);
                    VentCalcCenterCommand.ApplyHighlightResultToActiveWindow(result);
                });

            if (VentCalcSessionState.CurrentData == null)
            {
                VentCalcCenterData data = new RevitVentCalcCenterDataLoader().Load(uiDocument, viewModel.Settings.ToAerodynamicSettings());
                viewModel.CompleteLoad(data);
                if (!data.Success)
                {
                    TaskDialog.Show("VentCalc", NeedSystemMessage);
                    message = NeedSystemMessage;
                    return Result.Cancelled;
                }
            }

            action(viewModel);
            TaskDialog.Show("VentCalc", viewModel.StatusText);
            return Result.Succeeded;
        }
    }
}
