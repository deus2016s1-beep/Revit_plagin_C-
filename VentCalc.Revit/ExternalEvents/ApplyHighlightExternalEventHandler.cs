using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VentCalc.Revit.Services;
using VentCalc.UI.Services;

namespace VentCalc.Revit.ExternalEvents
{
    public sealed class ApplyHighlightExternalEventHandler : IExternalEventHandler
    {
        private readonly string? launchLogPath;
        private readonly Action<HighlightResult>? onCompleted;
        private ExternalEvent? externalEvent;
        private HighlightRequest? pendingRequest;

        public ApplyHighlightExternalEventHandler(string? launchLogPath, Action<HighlightResult>? onCompleted = null)
        {
            this.launchLogPath = launchLogPath;
            this.onCompleted = onCompleted;
        }

        public string GetName() => "VentCalc Apply Highlight";

        public void Initialize(ExternalEvent createdExternalEvent)
        {
            externalEvent = createdExternalEvent;
        }

        public void Request(HighlightRequest request)
        {
            pendingRequest = request;
            externalEvent?.Raise();
        }

        public void Execute(UIApplication app)
        {
            HighlightRequest? request = pendingRequest;
            pendingRequest = null;
            if (request == null)
            {
                return;
            }

            HighlightResult result;
            try
            {
                UIDocument? uiDocument = app.ActiveUIDocument;
                if (uiDocument == null)
                {
                    result = Failed(request, "Откройте документ Revit перед подсветкой.");
                    Complete(result);
                    return;
                }

                result = request.Action == HighlightAction.Clear
                    ? ClearHighlight(uiDocument, request)
                    : ApplyHighlight(uiDocument, request);
            }
            catch (Exception exception)
            {
                ErrorReporter.WriteTrace(launchLogPath, $"Highlight failed: {exception}");
                result = Failed(request, exception.Message);
            }

            Complete(result);
        }

        private HighlightResult ApplyHighlight(UIDocument uiDocument, HighlightRequest request)
        {
            Document document = uiDocument.Document;
            View view = uiDocument.ActiveView;
            var result = new HighlightResult
            {
                ActiveMode = request.Mode,
                ActiveViewId = view.Id.Value,
                RequestedElementCount = request.RequestedElementCount,
                Message = request.StatusMessage
            };

            var highlightedIds = new List<ElementId>();

            using (var transaction = new Transaction(document, "VentCalc: подсветка"))
            {
                transaction.Start();
                result.RestoredElementCount = HighlightStateStore.RestoreAll(document, result.Errors);

                ElementId solidFillPatternId = FindSolidFillPatternId(document);
                foreach (HighlightElementGroup group in request.Groups)
                {
                    OverrideGraphicSettings overrides = BuildOverrides(group, solidFillPatternId);
                    foreach (long rawElementId in group.ElementIds.Distinct())
                    {
                        ElementId elementId = new ElementId(rawElementId);
                        Element? element = document.GetElement(elementId);
                        if (element == null)
                        {
                            result.SkippedElementCount++;
                            continue;
                        }

                        try
                        {
                            HighlightStateStore.SaveIfMissing(document, view, elementId);
                            view.SetElementOverrides(elementId, overrides);
                            highlightedIds.Add(elementId);
                            result.HighlightedElementCount++;
                        }
                        catch (Exception exception)
                        {
                            result.FailedElementCount++;
                            result.Errors.Add($"ElementId {rawElementId}: {exception.Message}");
                        }
                    }
                }

                transaction.Commit();
            }

            result.SnapshotCount = HighlightStateStore.SnapshotCount;
            if (request.SelectElements)
            {
                uiDocument.Selection.SetElementIds(highlightedIds.Distinct().ToList());
            }

            if (request.ShowElements && highlightedIds.Count > 0)
            {
                uiDocument.ShowElements(highlightedIds.Distinct().ToList());
            }

            return result;
        }

        private HighlightResult ClearHighlight(UIDocument uiDocument, HighlightRequest request)
        {
            Document document = uiDocument.Document;
            View view = uiDocument.ActiveView;
            int snapshotCountBeforeClear = HighlightStateStore.GetSnapshotsForDocument(document).Count;
            var result = new HighlightResult
            {
                ActiveMode = HighlightMode.None,
                ActiveViewId = view.Id.Value,
                RequestedElementCount = snapshotCountBeforeClear,
                Message = request.StatusMessage
            };

            using (var transaction = new Transaction(document, "VentCalc: очистить подсветку"))
            {
                transaction.Start();
                result.RestoredElementCount = HighlightStateStore.RestoreAll(document, result.Errors);
                transaction.Commit();
            }

            if (request.SelectElements)
            {
                uiDocument.Selection.SetElementIds(Array.Empty<ElementId>());
            }

            result.FailedElementCount = result.Errors.Count;
            result.SnapshotCount = HighlightStateStore.SnapshotCount;
            return result;
        }

        private static OverrideGraphicSettings BuildOverrides(HighlightElementGroup group, ElementId solidFillPatternId)
        {
            Autodesk.Revit.DB.Color color = ParseRevitColor(group.ColorHex);
            var overrides = new OverrideGraphicSettings();
            overrides.SetProjectionLineColor(color);
            overrides.SetProjectionLineWeight(Math.Max(1, Math.Min(16, group.LineWeight)));
            overrides.SetSurfaceTransparency(Math.Max(0, Math.Min(90, group.Transparency)));
            if (solidFillPatternId != ElementId.InvalidElementId)
            {
                overrides.SetSurfaceForegroundPatternId(solidFillPatternId);
                overrides.SetSurfaceForegroundPatternColor(color);
            }

            return overrides;
        }

        private static Autodesk.Revit.DB.Color ParseRevitColor(string colorHex)
        {
            string hex = (colorHex ?? string.Empty).Trim().TrimStart('#');
            if (hex.Length == 6
                && byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
                && byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
                && byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                return new Autodesk.Revit.DB.Color(r, g, b);
            }

            return new Autodesk.Revit.DB.Color(255, 0, 0);
        }

        private static ElementId FindSolidFillPatternId(Document document)
        {
            FillPatternElement? fillPattern = new FilteredElementCollector(document)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(element => element.GetFillPattern().IsSolidFill);
            return fillPattern?.Id ?? ElementId.InvalidElementId;
        }

        private static HighlightResult Failed(HighlightRequest request, string message)
        {
            return new HighlightResult
            {
                ActiveMode = request.Mode,
                RequestedElementCount = request.RequestedElementCount,
                FailedElementCount = request.RequestedElementCount == 0 ? 1 : request.RequestedElementCount,
                Message = message,
                Errors = new List<string> { message },
                SnapshotCount = HighlightStateStore.SnapshotCount
            };
        }

        private void Complete(HighlightResult result)
        {
            onCompleted?.Invoke(result);
        }
    }
}
