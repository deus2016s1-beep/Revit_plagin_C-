using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VentCalc.Revit.Services;
using VentCalc.UI.Services;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ClearHighlightsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument == null)
            {
                TaskDialog.Show("VentCalc", "Откройте документ Revit перед очисткой подсветки.");
                return Result.Cancelled;
            }

            var errors = new List<string>();
            int selectionCountBefore = uiDocument.Selection.GetElementIds().Count;
            int restored;
            using (var transaction = new Transaction(uiDocument.Document, "VentCalc: очистить подсветку"))
            {
                transaction.Start();
                restored = HighlightStateStore.RestoreAll(uiDocument.Document, errors);
                HighlightStateStore.SetActiveMode(uiDocument.Document, HighlightMode.None);
                transaction.Commit();
            }

            int selectionCountAfter = uiDocument.Selection.GetElementIds().Count;
            string details = errors.Count == 0
                ? $"Подсветка VentCalc снята: восстановлено {restored} элементов."
                : $"Подсветка VentCalc снята: восстановлено {restored} элементов; ошибок {errors.Count}.";
            VentCalcCenterCommand.ApplyHighlightResultToActiveWindow(new HighlightResult
            {
                ActiveMode = HighlightMode.None,
                RequestedElementCount = restored + errors.Count,
                RestoredElementCount = restored,
                FailedElementCount = errors.Count,
                SnapshotCount = HighlightStateStore.SnapshotCount,
                SelectionElementCountBefore = selectionCountBefore,
                SelectionElementCountAfter = selectionCountAfter,
                SelectionChangedByVentCalc = false,
                WindowSource = "Ribbon",
                ActiveDisplayMode = HighlightDisplayMode.Normal,
                LastApplySucceeded = true,
                LastClearSucceeded = errors.Count == 0,
                Message = details,
                Errors = errors
            });
            TaskDialog.Show("VentCalc", details);
            return errors.Count == 0 ? Result.Succeeded : Result.Failed;
        }
    }
}
