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
            return ExecuteProjectVelocityMap(commandData, ref message);
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

        private static Result ExecuteProjectVelocityMap(ExternalCommandData commandData, ref string message)
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
                settingsService);

            HighlightResult Apply(HighlightRequest request)
            {
                HighlightResult result = highlightHandler.ApplyNow(uiApplication, request);
                viewModel.ApplyHighlightResult(result);
                VentCalcCenterCommand.ApplyHighlightResultToActiveWindow(result);
                return result;
            }

            if (viewModel.HighlightState.ActiveMode == HighlightMode.Velocity)
            {
                Apply(BuildClearRequest(viewModel, "Ribbon: Карта скоростей"));
                return Result.Succeeded;
            }

            if (viewModel.HighlightState.ActiveMode != HighlightMode.None)
            {
                Apply(BuildClearRequest(viewModel, "Ribbon: Карта скоростей"));
            }

            HighlightRequest request = new ProjectVelocityMapRequestBuilder().Build(uiDocument.Document, viewModel);
            if (request.RequestedElementCount == 0)
            {
                const string noVelocityMessage = "Не удалось определить скорость воздуховодов или фитингов в проекте.";
                TaskDialog.Show("VentCalc", noVelocityMessage);
                message = noVelocityMessage;
                return Result.Cancelled;
            }

            HighlightResult applyResult = Apply(request);
            if (applyResult.FailedElementCount > 0 || applyResult.Errors.Count > 0)
            {
                TaskDialog.Show("VentCalc", viewModel.StatusText);
                message = viewModel.StatusText;
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        private static HighlightRequest BuildClearRequest(VentCalcCenterViewModel viewModel, string windowSource)
        {
            return new HighlightRequest
            {
                Action = HighlightAction.Clear,
                Mode = HighlightMode.None,
                SelectElements = false,
                ShowElements = false,
                WindowSource = windowSource,
                DisplayMode = HighlightDisplayMode.Normal,
                SystemName = viewModel.SystemName,
                StatusMessage = "Подсветка VentCalc очищена."
            };
        }
    }
}
