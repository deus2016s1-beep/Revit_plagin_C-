using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.UI;
using VentCalc.Revit.Services;
using VentCalc.UI.Services;
using VentCalc.UI.ViewModels;

namespace VentCalc.Revit.ExternalEvents
{
    public sealed class SpecWriteAdskExternalEventHandler : IExternalEventHandler
    {
        private readonly SpecAdskWriterService writerService = new SpecAdskWriterService();
        private readonly SpecCollectorService collectorService = new SpecCollectorService();
        private readonly Action? focusWindow;
        private ExternalEvent? externalEvent;
        private SpecCenterViewModel? pendingViewModel;
        private IReadOnlyList<SpecItemRow> pendingRows = Array.Empty<SpecItemRow>();

        public SpecWriteAdskExternalEventHandler(Action? focusWindow = null)
        {
            this.focusWindow = focusWindow;
        }

        public string GetName() => "SpecCalc ADSK writer";

        public void Initialize(ExternalEvent createdExternalEvent)
        {
            externalEvent = createdExternalEvent;
        }

        public void Request(SpecCenterViewModel viewModel, IReadOnlyList<SpecItemRow> rows)
        {
            pendingViewModel = viewModel;
            pendingRows = rows;
            externalEvent?.Raise();
        }

        public void Execute(UIApplication app)
        {
            SpecCenterViewModel? viewModel = pendingViewModel;
            IReadOnlyList<SpecItemRow> rows = pendingRows;
            pendingViewModel = null;
            pendingRows = Array.Empty<SpecItemRow>();
            if (viewModel == null)
            {
                return;
            }

            try
            {
                UIDocument? uiDocument = app.ActiveUIDocument;
                if (uiDocument?.Document == null)
                {
                    Dispatch(() => viewModel.ApplyAdskWriteResult("Откройте документ Revit для записи ADSK.", null));
                    return;
                }

                SpecAdskWriteResult result = writerService.Write(uiDocument.Document, rows);
                IReadOnlyList<SpecItemRow> refreshedRows = collectorService.Collect(uiDocument.Document);
                Dispatch(() =>
                {
                    viewModel.ApplyAdskWriteResult(result.Summary, refreshedRows);
                    focusWindow?.Invoke();
                });
            }
            catch (Exception exception)
            {
                Dispatch(() => viewModel.ApplyAdskWriteResult($"Не удалось записать ADSK: {exception.Message}", null));
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
