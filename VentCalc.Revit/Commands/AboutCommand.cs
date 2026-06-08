using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class AboutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            TaskDialog.Show(
                "VentCalc",
                "VentCalc v2.0\n\nЕдиный рабочий центр для инспекции вентиляционных сетей, трасс и предварительного аэродинамического расчёта в Revit 2025.");
            return Result.Succeeded;
        }
    }
}
