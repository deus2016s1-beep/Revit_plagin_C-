using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class PathHighlightCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return RibbonHighlightExecutor.ExecuteCriticalPath(commandData, ref message);
        }
    }
}
