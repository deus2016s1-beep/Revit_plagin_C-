using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VentCalc.Revit.ExternalEvents;
using VentCalc.Revit.Services;
using VentCalc.UI.Services;
using VentCalc.UI.ViewModels;
using VentCalc.UI.Views;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class PathHighlightCommand : IExternalCommand
    {
        private static PathHighlightWindow? activeWindow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string launchLogPath = ErrorReporter.CreateLaunchLog();
            UIApplication uiApplication = commandData.Application;
            try
            {
                if (activeWindow?.IsVisible == true)
                {
                    ActivateWindow(activeWindow);
                    return Result.Succeeded;
                }

                var settingsService = new VentCalcSettingsService();
                var loader = new RevitVentCalcCenterDataLoader();
                var loadHandler = new LoadSelectedSystemExternalEventHandler(loader, launchLogPath, () => ActivateWindow(activeWindow));
                ExternalEvent loadExternalEvent = ExternalEvent.Create(loadHandler);
                loadHandler.Initialize(loadExternalEvent);

                VentCalcCenterViewModel? viewModel = null;
                var highlightHandler = new ApplyHighlightExternalEventHandler(
                    launchLogPath,
                    result => activeWindow?.Dispatcher.BeginInvoke(new Action(() => viewModel?.ApplyHighlightResult(result))));
                ExternalEvent highlightExternalEvent = ExternalEvent.Create(highlightHandler);
                highlightHandler.Initialize(highlightExternalEvent);

                viewModel = new VentCalcCenterViewModel(
                    (vm, mode) => loadHandler.Request(
                        vm,
                        mode == VentCalcLoadRequestMode.SystemCatalog
                            ? LoadSelectedSystemRequestMode.SystemCatalog
                            : mode == VentCalcLoadRequestMode.LastLoadedElement
                                ? LoadSelectedSystemRequestMode.LastLoadedElement
                                : LoadSelectedSystemRequestMode.SelectedElement),
                    null,
                    null,
                    text => TaskDialog.Show("VentCalc", text),
                    settingsService,
                    exception => ErrorReporter.Report(uiApplication, "Ошибка окна подсветки трасс", exception, launchLogPath),
                    (vm, request) => highlightHandler.Request(request));

                var window = new PathHighlightWindow(viewModel);
                AssignRevitOwner(window, uiApplication);
                window.Closed += (_, _) =>
                {
                    activeWindow = null;
                    loadExternalEvent.Dispose();
                    highlightExternalEvent.Dispose();
                };
                activeWindow = window;
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception exception)
            {
                ErrorReporter.Report(uiApplication, "Ошибка запуска подсветки трасс", exception, launchLogPath);
                message = exception.Message;
                return Result.Failed;
            }
        }

        private static void AssignRevitOwner(Window window, UIApplication uiApplication)
        {
            IntPtr ownerHandle = uiApplication.MainWindowHandle;
            if (ownerHandle == IntPtr.Zero)
            {
                ownerHandle = Process.GetCurrentProcess().MainWindowHandle;
            }

            if (ownerHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(window).Owner = ownerHandle;
            }
        }

        private static void ActivateWindow(Window? window)
        {
            if (window == null)
            {
                return;
            }

            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }

                window.Activate();
            }));
        }
    }
}
