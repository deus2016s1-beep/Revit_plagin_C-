using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using VentCalc.UI.Services;

namespace VentCalc.Revit.Services
{
    internal sealed class HighlightSnapshot
    {
        public HighlightSnapshot(string documentKey, ElementId viewId, ElementId elementId, OverrideGraphicSettings overrides)
        {
            DocumentKey = documentKey;
            ViewId = viewId;
            ElementId = elementId;
            Overrides = overrides;
        }

        public string DocumentKey { get; }

        public ElementId ViewId { get; }

        public ElementId ElementId { get; }

        public OverrideGraphicSettings Overrides { get; }
    }

    internal static class HighlightStateStore
    {
        private static readonly Dictionary<string, HighlightSnapshot> Snapshots = new Dictionary<string, HighlightSnapshot>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HighlightMode> ActiveModes = new Dictionary<string, HighlightMode>(StringComparer.Ordinal);

        public static int SnapshotCount => Snapshots.Count;

        public static string GetDocumentKey(Document document)
        {
            string path = string.IsNullOrWhiteSpace(document.PathName) ? document.Title : document.PathName;
            return $"{document.GetHashCode():X8}|{path}";
        }

        public static HighlightMode GetActiveMode(Document document)
        {
            return ActiveModes.TryGetValue(GetDocumentKey(document), out HighlightMode mode) ? mode : HighlightMode.None;
        }

        public static void SetActiveMode(Document document, HighlightMode mode)
        {
            string documentKey = GetDocumentKey(document);
            if (mode == HighlightMode.None)
            {
                ActiveModes.Remove(documentKey);
                return;
            }

            ActiveModes[documentKey] = mode;
        }

        public static bool SaveIfMissing(Document document, View view, ElementId elementId)
        {
            string documentKey = GetDocumentKey(document);
            string key = BuildKey(documentKey, view.Id, elementId);
            if (Snapshots.ContainsKey(key))
            {
                return false;
            }

            Snapshots[key] = new HighlightSnapshot(documentKey, view.Id, elementId, view.GetElementOverrides(elementId));
            return true;
        }

        public static IReadOnlyList<HighlightSnapshot> GetSnapshotsForDocument(Document document)
        {
            string documentKey = GetDocumentKey(document);
            return Snapshots.Values
                .Where(snapshot => string.Equals(snapshot.DocumentKey, documentKey, StringComparison.Ordinal))
                .ToList();
        }

        public static void Remove(Document document, HighlightSnapshot snapshot)
        {
            string key = BuildKey(GetDocumentKey(document), snapshot.ViewId, snapshot.ElementId);
            Snapshots.Remove(key);
        }

        public static int RestoreAll(Document document, ICollection<string> errors)
        {
            IReadOnlyList<HighlightSnapshot> snapshots = GetSnapshotsForDocument(document);
            int restored = 0;
            foreach (HighlightSnapshot snapshot in snapshots)
            {
                try
                {
                    View? snapshotView = document.GetElement(snapshot.ViewId) as View;
                    if (snapshotView == null)
                    {
                        errors.Add($"Вид {snapshot.ViewId.Value} недоступен для восстановления подсветки.");
                        continue;
                    }

                    snapshotView.SetElementOverrides(snapshot.ElementId, snapshot.Overrides);
                    Remove(document, snapshot);
                    restored++;
                }
                catch (Exception exception)
                {
                    errors.Add($"ElementId {snapshot.ElementId.Value}: {exception.Message}");
                }
            }

            return restored;
        }

        private static string BuildKey(string documentKey, ElementId viewId, ElementId elementId)
        {
            return $"{documentKey}|{viewId.Value}|{elementId.Value}";
        }
    }
}
