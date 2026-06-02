using Autodesk.Revit.Attributes;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CalculateCommand : TaskDialogCommand
    {
        protected override string CommandName => "Расчёт пока не реализован";
    }
}
