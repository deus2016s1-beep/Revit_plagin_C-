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
    public sealed class SpecCenterCommand : IExternalCommand
    {
        private static SpecCenterWindow? activeWindow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApplication = commandData.Application;
            try
            {
                if (activeWindow?.IsVisible == true)
                {
                    ActivateWindow(activeWindow);
                    return Result.Succeeded;
                }

                var collectHandler = new SpecCollectExternalEventHandler(() => ActivateWindow(activeWindow));
                ExternalEvent collectExternalEvent = ExternalEvent.Create(collectHandler);
                collectHandler.Initialize(collectExternalEvent);

                var selectHandler = new SelectElementExternalEventHandler(ErrorReporter.CreateLaunchLog(), () => ActivateWindow(activeWindow));
                ExternalEvent selectExternalEvent = ExternalEvent.Create(selectHandler);
                selectHandler.Initialize(selectExternalEvent);

                SpecCenterViewModel viewModel = new SpecCenterViewModel(
                    vm => collectHandler.Request(vm),
                    ids => selectHandler.Request(ids),
                    text => TaskDialog.Show("SpecCalc", text),
                    new SpecRuleService());

                var window = new SpecCenterWindow(viewModel);
                AssignRevitOwner(window, uiApplication);
                window.Closed += (_, _) =>
                {
                    activeWindow = null;
                    collectExternalEvent.Dispose();
                    selectExternalEvent.Dispose();
                };
                activeWindow = window;
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception exception)
            {
                message = exception.Message;
                TaskDialog.Show("SpecCalc", exception.Message);
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
                // Owner assignment is optional.
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
