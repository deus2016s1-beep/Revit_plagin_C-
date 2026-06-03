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
            string launchLogPath = ErrorReporter.CreateLaunchLog();
            UIApplication uiApplication = commandData.Application;
            try
            {
                ErrorReporter.WriteTrace(launchLogPath, "Command started");
                UIDocument? uiDocument = uiApplication.ActiveUIDocument;
                if (uiDocument == null)
                {
                    TaskDialog.Show("VentCalc", "Откройте документ Revit и выберите элемент воздуховодной системы.");
                    ErrorReporter.WriteTrace(launchLogPath, "Command cancelled: ActiveUIDocument is null");
                    return Result.Cancelled;
                }

                var settingsService = new VentCalcSettingsService();
                settingsService.Load();
                ErrorReporter.WriteTrace(launchLogPath, "Settings loaded");

                var loader = new RevitVentCalcCenterDataLoader();
                VentCalcCenterViewModel? viewModel = null;
                viewModel = new VentCalcCenterViewModel(
                    () => loader.Load(uiDocument, viewModel?.Settings.ToAerodynamicSettings() ?? settingsService.Load().ToAerodynamicSettings()),
                    elementId => SelectElement(uiDocument, elementId),
                    text => TaskDialog.Show("VentCalc", text),
                    settingsService,
                    exception => ErrorReporter.Report(uiApplication, "Ошибка ViewModel VentCalc Center", exception, launchLogPath));
                ErrorReporter.WriteTrace(launchLogPath, "ViewModel created");

                var window = new VentCalcCenterWindow(viewModel);
                window.Dispatcher.UnhandledException += (_, args) =>
                {
                    ErrorReporter.Report(uiApplication, "Ошибка WPF окна VentCalc Center", args.Exception, launchLogPath);
                    args.Handled = true;
                };
                ErrorReporter.WriteTrace(launchLogPath, "Window created");

                ErrorReporter.WriteTrace(launchLogPath, "ShowDialog started");
                window.ShowDialog();
                ErrorReporter.WriteTrace(launchLogPath, "ShowDialog closed");
                return Result.Succeeded;
            }
            catch (Exception exception)
            {
                ErrorReporter.WriteTrace(launchLogPath, "Command failed");
                ErrorReporter.Report(uiApplication, "Ошибка запуска VentCalc Center", exception, launchLogPath);
                message = exception.Message;
                return Result.Failed;
            }
        }

        private static void SelectElement(UIDocument uiDocument, long elementId)
        {
            var revitElementId = new ElementId(elementId);
            uiDocument.Selection.SetElementIds(new List<ElementId> { revitElementId });
            uiDocument.ShowElements(revitElementId);
        }
    }
}
