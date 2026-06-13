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
            return Execute(commandData, ref message, vm => vm.ToggleCriticalPathFromRibbon());
        }

        public static Result ExecuteVelocityMap(ExternalCommandData commandData, ref string message)
        {
            return Execute(commandData, ref message, vm => vm.ToggleVelocityHighlightFromRibbon());
        }

        public static Result ExecutePressureLossMap(ExternalCommandData commandData, ref string message)
        {
            return Execute(commandData, ref message, vm => vm.ApplyPressureLossMapFromRibbon());
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
            if (viewModel.HighlightState.FailedElementCount > 0 || viewModel.HighlightState.Errors.Count > 0)
            {
                TaskDialog.Show("VentCalc", viewModel.StatusText);
                message = viewModel.StatusText;
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }
}
