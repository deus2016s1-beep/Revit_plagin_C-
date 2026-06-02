using Autodesk.Revit.Attributes;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class HighlightCriticalPathCommand : TaskDialogCommand
    {
        protected override string CommandName => "Трасса";
    }
}
