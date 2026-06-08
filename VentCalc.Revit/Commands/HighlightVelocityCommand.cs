using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VentCalc.Revit.ExternalEvents;
using VentCalc.UI.Services;
using VentCalc.UI.ViewModels;
using VentCalc.UI.Views;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class HighlightVelocityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var settingsService = new VentCalcSettingsService();
            VentCalcCenterViewModel? viewModel = null;
            var highlightHandler = new ApplyHighlightExternalEventHandler(
                null,
                result => viewModel?.ApplyHighlightResult(result));
            ExternalEvent highlightExternalEvent = ExternalEvent.Create(highlightHandler);
            highlightHandler.Initialize(highlightExternalEvent);
            viewModel = new VentCalcCenterViewModel(
                (_, _) => throw new System.InvalidOperationException("Загрузка системы доступна в окне VentCalc Center."),
                null,
                null,
                text => TaskDialog.Show("VentCalc", text),
                settingsService,
                null,
                (vm, request) => highlightHandler.Request(request));
            var window = new VelocityHighlightWindow(viewModel);
            window.Closed += (_, _) => highlightExternalEvent.Dispose();
            window.Show();
            return Result.Succeeded;
        }
    }
}
