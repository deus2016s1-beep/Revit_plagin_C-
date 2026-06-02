using Autodesk.Revit.Attributes;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class SettingsCommand : TaskDialogCommand
    {
        protected override string CommandName => "Настройки";
    }
}
