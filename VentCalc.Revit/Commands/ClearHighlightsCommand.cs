using Autodesk.Revit.Attributes;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ClearHighlightsCommand : TaskDialogCommand
    {
        protected override string CommandName => "Очистить";
    }
}
