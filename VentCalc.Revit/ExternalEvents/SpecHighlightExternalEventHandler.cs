using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VentCalc.Revit.ExternalEvents
{
    public sealed class SpecHighlightExternalEventHandler : IExternalEventHandler
    {
        private readonly Action? focusWindow;
        private ExternalEvent? externalEvent;
        private List<long> pendingElementIds = new List<long>();
        private bool clearOnly;

        public SpecHighlightExternalEventHandler(Action? focusWindow = null)
        {
            this.focusWindow = focusWindow;
        }

        public string GetName() => "SpecCalc independent selection highlight";

        public void Initialize(ExternalEvent createdExternalEvent)
        {
            externalEvent = createdExternalEvent;
        }

        public void Request(IEnumerable<long> elementIds)
        {
            pendingElementIds = elementIds.Distinct().ToList();
            clearOnly = pendingElementIds.Count == 0;
            externalEvent?.Raise();
        }

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument? uiDocument = app.ActiveUIDocument;
                if (uiDocument == null)
                {
                    return;
                }

                if (clearOnly)
                {
                    uiDocument.Selection.SetElementIds(Array.Empty<ElementId>());
                    return;
                }

                List<ElementId> ids = pendingElementIds.Select(id => new ElementId(id)).ToList();
                uiDocument.Selection.SetElementIds(ids);
                TryShowElements(uiDocument, ids);
            }
            finally
            {
                focusWindow?.Invoke();
            }
        }

        private static void TryShowElements(UIDocument uiDocument, ICollection<ElementId> elementIds)
        {
            try
            {
                if (elementIds.Count == 1)
                {
                    uiDocument.ShowElements(elementIds.First());
                }
                else if (elementIds.Count > 1)
                {
                    uiDocument.ShowElements(elementIds);
                }
            }
            catch (Exception)
            {
                // Selection/highlight must remain safe even when zooming is unavailable.
            }
        }
    }
}
