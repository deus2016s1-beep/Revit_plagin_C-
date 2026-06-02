using Autodesk.Revit.Attributes;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class AboutCommand : TaskDialogCommand
    {
        protected override string CommandName => "О программе";
    }
}
