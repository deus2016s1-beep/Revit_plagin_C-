using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VentCalc.Revit.Services;

namespace VentCalc.Revit.ExternalEvents
{
    public sealed class SelectElementExternalEventHandler : IExternalEventHandler
    {
        private readonly string launchLogPath;
        private readonly Action? focusWindow;
        private ExternalEvent? externalEvent;
        private List<long> pendingElementIds = new List<long>();

        public SelectElementExternalEventHandler(string launchLogPath, Action? focusWindow = null)
        {
            this.launchLogPath = launchLogPath;
            this.focusWindow = focusWindow;
        }

        public void Initialize(ExternalEvent createdExternalEvent)
        {
            externalEvent = createdExternalEvent;
        }

        public void Request(long elementId)
        {
            Request(new[] { elementId });
        }

        public void Request(IEnumerable<long> elementIds)
        {
            pendingElementIds = elementIds.Distinct().ToList();
            ErrorReporter.WriteTrace(launchLogPath, $"SelectElement external event requested: {string.Join(", ", pendingElementIds)}");
            externalEvent?.Raise();
        }

        public void Execute(UIApplication app)
        {
            if (pendingElementIds.Count == 0 || app.ActiveUIDocument == null)
            {
                focusWindow?.Invoke();
                return;
            }

            try
            {
                UIDocument uiDocument = app.ActiveUIDocument;
                List<ElementId> revitElementIds = pendingElementIds.Select(id => new ElementId(id)).ToList();
                uiDocument.Selection.SetElementIds(revitElementIds);
                TryShowElements(uiDocument, revitElementIds);
                ErrorReporter.WriteTrace(launchLogPath, $"SelectElement external event completed: {string.Join(", ", pendingElementIds)}");
            }
            catch (Exception exception)
            {
                ErrorReporter.Report(app, "Ошибка выбора элемента VentCalc Center", exception, launchLogPath);
            }
            finally
            {
                focusWindow?.Invoke();
            }
        }

        public string GetName()
        {
            return "VentCalc Select Element";
        }

        private static void TryShowElements(UIDocument uiDocument, ICollection<ElementId> elementIds)
        {
            try
            {
                if (elementIds.Count == 1)
                {
                    uiDocument.ShowElements(elementIds.First());
                }
                else
                {
                    uiDocument.ShowElements(elementIds);
                }
            }
            catch (Exception)
            {
                // Selection is the primary action; zooming is optional and may fail for hidden/unloaded elements.
            }
        }
    }
}
