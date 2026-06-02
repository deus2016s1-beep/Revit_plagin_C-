using Autodesk.Revit.Attributes;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class HighlightVelocityCommand : TaskDialogCommand
    {
        protected override string CommandName => "Скорости";
    }
}
