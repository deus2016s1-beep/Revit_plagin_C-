using System;
using System.Windows;
using Autodesk.Revit.UI;
using VentCalc.Revit.Services;
using VentCalc.UI.Services;
using VentCalc.UI.ViewModels;

namespace VentCalc.Revit.ExternalEvents
{
    public sealed class SpecCollectExternalEventHandler : IExternalEventHandler
    {
        private readonly SpecCollectorService collectorService = new SpecCollectorService();
        private readonly Action? focusWindow;
        private ExternalEvent? externalEvent;
        private SpecCenterViewModel? pendingViewModel;

        public SpecCollectExternalEventHandler(Action? focusWindow = null)
        {
            this.focusWindow = focusWindow;
        }

        public string GetName() => "SpecCalc ventilation collector";

        public void Initialize(ExternalEvent createdExternalEvent)
        {
            externalEvent = createdExternalEvent;
        }

        public void Request(SpecCenterViewModel viewModel)
        {
            pendingViewModel = viewModel;
            externalEvent?.Raise();
        }

        public void Execute(UIApplication app)
        {
            SpecCenterViewModel? viewModel = pendingViewModel;
            pendingViewModel = null;
            if (viewModel == null)
            {
                return;
            }

            try
            {
                UIDocument? uiDocument = app.ActiveUIDocument;
                if (uiDocument?.Document == null)
                {
                    Dispatch(() => viewModel.FailCollect("Откройте документ Revit для сбора спецификации."));
                    return;
                }

                var rows = collectorService.Collect(uiDocument.Document);
                Dispatch(() =>
                {
                    viewModel.ApplyCollectedRows(rows);
                    focusWindow?.Invoke();
                });
            }
            catch (Exception exception)
            {
                Dispatch(() => viewModel.FailCollect($"Не удалось собрать спецификацию вентиляции: {exception.Message}"));
            }
        }

        private static void Dispatch(Action action)
        {
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(action);
                return;
            }

            action();
        }
    }
}
