using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
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
            var viewModel = new VentCalcCenterViewModel(
                (_, _) => throw new System.InvalidOperationException("Загрузка системы доступна в окне VentCalc Center."),
                null,
                text => TaskDialog.Show("VentCalc", text),
                settingsService);
            var window = new VelocityHighlightWindow(viewModel);
            window.ShowDialog();
            return Result.Succeeded;
        }
    }
}
