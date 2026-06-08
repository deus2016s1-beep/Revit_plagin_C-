using Autodesk.Revit.Attributes;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class LocalResistanceCommand : TaskDialogCommand
    {
        protected override string CommandName => "МС";
    }
}
