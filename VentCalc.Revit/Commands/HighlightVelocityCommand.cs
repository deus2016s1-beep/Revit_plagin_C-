using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class HighlightVelocityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return RibbonHighlightExecutor.ExecuteVelocityMap(commandData, ref message);
        }
    }
}
