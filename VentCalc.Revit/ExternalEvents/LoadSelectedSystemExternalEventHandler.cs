using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VentCalc.Revit.Services;
using VentCalc.UI.ViewModels;

namespace VentCalc.Revit.ExternalEvents
{
    public sealed class LoadSelectedSystemExternalEventHandler : IExternalEventHandler
    {
        private readonly RevitVentCalcCenterDataLoader dataLoader;
        private readonly string launchLogPath;
        private readonly Action? focusWindow;
        private VentCalcCenterViewModel? pendingViewModel;
        private LoadSelectedSystemRequestMode pendingMode;
        private ExternalEvent? externalEvent;

        public LoadSelectedSystemExternalEventHandler(RevitVentCalcCenterDataLoader dataLoader, string launchLogPath, Action? focusWindow = null)
        {
            this.dataLoader = dataLoader;
            this.launchLogPath = launchLogPath;
            this.focusWindow = focusWindow;
        }

        public void Initialize(ExternalEvent createdExternalEvent)
        {
            externalEvent = createdExternalEvent;
        }

        public void Request(VentCalcCenterViewModel viewModel, LoadSelectedSystemRequestMode mode)
        {
            pendingViewModel = viewModel;
            pendingMode = mode;
            ErrorReporter.WriteTrace(launchLogPath, $"LoadSelectedSystem external event requested: {mode}");
            externalEvent?.Raise();
        }

        public void Execute(UIApplication app)
        {
            VentCalcCenterViewModel? viewModel = pendingViewModel;
            if (viewModel == null)
            {
                return;
            }

            try
            {
                ErrorReporter.WriteTrace(launchLogPath, $"LoadSelectedSystem external event started: {pendingMode}");
                UIDocument? uiDocument = app.ActiveUIDocument;
                VentCalcCenterData data;
                if (uiDocument == null)
                {
                    data = new VentCalcCenterData
                    {
                        Success = false,
                        IsUserSelectionWarning = true,
                        ErrorMessage = "Откройте документ Revit и выберите элемент воздуховодной системы.",
                        ReportText = "Откройте документ Revit и выберите элемент воздуховодной системы."
                    };
                }
                else if (pendingMode == LoadSelectedSystemRequestMode.LastLoadedElement && viewModel.LastLoadedElementId.HasValue)
                {
                    data = dataLoader.Load(uiDocument, viewModel.Settings.ToAerodynamicSettings(), new ElementId(viewModel.LastLoadedElementId.Value));
                }
                else if (pendingMode == LoadSelectedSystemRequestMode.SystemCatalog && viewModel.SelectedCatalogSystem != null)
                {
                    data = dataLoader.LoadSystem(uiDocument, viewModel.Settings.ToAerodynamicSettings(), viewModel.SelectedCatalogSystem);
                }
                else if (pendingMode == LoadSelectedSystemRequestMode.SystemCatalog)
                {
                    data = new VentCalcCenterData
                    {
                        Success = false,
                        IsUserSelectionWarning = true,
                        ErrorMessage = "Выберите систему в списке VentCalc Center.",
                        ReportText = "Выберите систему в списке VentCalc Center."
                    };
                    data.SystemCatalog.AddRange(dataLoader.ReadSystemCatalog(uiDocument));
                }
                else
                {
                    data = dataLoader.Load(uiDocument, viewModel.Settings.ToAerodynamicSettings());
                }

                InvokeOnUiThread(viewModel, () =>
                {
                    viewModel.CompleteLoad(data);
                    focusWindow?.Invoke();
                });
                ErrorReporter.WriteTrace(launchLogPath, "LoadSelectedSystem external event completed");
            }
            catch (Exception exception)
            {
                ErrorReporter.Report(app, "Ошибка ExternalEvent загрузки VentCalc Center", exception, launchLogPath);
                InvokeOnUiThread(viewModel, () =>
                {
                    viewModel.FailLoad(exception);
                    focusWindow?.Invoke();
                });
            }
        }

        public string GetName()
        {
            return "VentCalc Load Selected System";
        }

        private static void InvokeOnUiThread(VentCalcCenterViewModel viewModel, Action action)
        {
            System.Windows.Application? application = System.Windows.Application.Current;
            if (application?.Dispatcher != null && !application.Dispatcher.CheckAccess())
            {
                application.Dispatcher.Invoke(action);
                return;
            }

            action();
        }
    }
}
