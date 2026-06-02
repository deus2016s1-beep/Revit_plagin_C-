using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public abstract class TaskDialogCommand : IExternalCommand
    {
        protected abstract string CommandName { get; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            TaskDialog.Show("VentCalc", CommandName);
            return Result.Succeeded;
        }
    }
}
