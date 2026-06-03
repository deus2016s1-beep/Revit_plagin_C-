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
    public sealed class VentCalcCenterCommand : IExternalCommand
    {
        private static VentCalcCenterWindow? activeWindow;

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

                if (activeWindow?.IsVisible == true)
                {
                    ActivateWindow(activeWindow);
                    ErrorReporter.WriteTrace(launchLogPath, "Existing VentCalc Center activated");
                    return Result.Succeeded;
                }

                var settingsService = new VentCalcSettingsService();
                settingsService.Load();
                ErrorReporter.WriteTrace(launchLogPath, "Settings loaded");

                var loader = new RevitVentCalcCenterDataLoader();
                var loadHandler = new LoadSelectedSystemExternalEventHandler(loader, launchLogPath, () => ActivateWindow(activeWindow));
                ExternalEvent loadExternalEvent = ExternalEvent.Create(loadHandler);
                loadHandler.Initialize(loadExternalEvent);
                var selectHandler = new SelectElementExternalEventHandler(launchLogPath, () => ActivateWindow(activeWindow));
                ExternalEvent selectExternalEvent = ExternalEvent.Create(selectHandler);
                selectHandler.Initialize(selectExternalEvent);

                var viewModel = new VentCalcCenterViewModel(
                    (vm, refreshLast) => loadHandler.Request(
                        vm,
                        refreshLast ? LoadSelectedSystemRequestMode.LastLoadedElement : LoadSelectedSystemRequestMode.SelectedElement),
                    elementIds => selectHandler.Request(elementIds),
                    text => TaskDialog.Show("VentCalc", text),
                    settingsService,
                    exception => ErrorReporter.Report(uiApplication, "Ошибка ViewModel VentCalc Center", exception, launchLogPath));
                ErrorReporter.WriteTrace(launchLogPath, "ViewModel created");

                var window = new VentCalcCenterWindow(viewModel);
                AssignRevitOwner(window, uiApplication);
                window.Dispatcher.UnhandledException += (_, args) =>
                {
                    ErrorReporter.Report(uiApplication, "Ошибка WPF окна VentCalc Center", args.Exception, launchLogPath);
                    args.Handled = true;
                };
                window.Closed += (_, _) =>
                {
                    activeWindow = null;
                    loadExternalEvent.Dispose();
                    selectExternalEvent.Dispose();
                    ErrorReporter.WriteTrace(launchLogPath, "VentCalc Center window closed");
                };
                activeWindow = window;
                ErrorReporter.WriteTrace(launchLogPath, "Window created");

                ErrorReporter.WriteTrace(launchLogPath, "Show modeless started");
                window.Show();
                ErrorReporter.WriteTrace(launchLogPath, "Show modeless returned");
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

        private static void AssignRevitOwner(Window window, UIApplication uiApplication)
        {
            try
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
            catch (Exception)
            {
                // Owner assignment is UX-only; VentCalc Center must still open if Revit does not expose a handle.
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
                window.Topmost = true;
                window.Topmost = false;
                window.Activate();
            }));
        }

    }
}
