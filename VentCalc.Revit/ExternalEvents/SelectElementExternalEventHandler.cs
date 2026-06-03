using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VentCalc.Revit.Services;

namespace VentCalc.Revit.ExternalEvents
{
    public sealed class SelectElementExternalEventHandler : IExternalEventHandler
    {
        private readonly string launchLogPath;
        private ExternalEvent? externalEvent;
        private long? pendingElementId;

        public SelectElementExternalEventHandler(string launchLogPath)
        {
            this.launchLogPath = launchLogPath;
        }

        public void Initialize(ExternalEvent createdExternalEvent)
        {
            externalEvent = createdExternalEvent;
        }

        public void Request(long elementId)
        {
            pendingElementId = elementId;
            ErrorReporter.WriteTrace(launchLogPath, $"SelectElement external event requested: {elementId}");
            externalEvent?.Raise();
        }

        public void Execute(UIApplication app)
        {
            if (!pendingElementId.HasValue || app.ActiveUIDocument == null)
            {
                return;
            }

            try
            {
                UIDocument uiDocument = app.ActiveUIDocument;
                var revitElementId = new ElementId(pendingElementId.Value);
                uiDocument.Selection.SetElementIds(new List<ElementId> { revitElementId });
                uiDocument.ShowElements(revitElementId);
                ErrorReporter.WriteTrace(launchLogPath, $"SelectElement external event completed: {pendingElementId.Value}");
            }
            catch (Exception exception)
            {
                ErrorReporter.Report(app, "Ошибка выбора элемента VentCalc Center", exception, launchLogPath);
            }
        }

        public string GetName()
        {
            return "VentCalc Select Element";
        }
    }
}
