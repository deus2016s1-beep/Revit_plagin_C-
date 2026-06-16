using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VentCalc.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class PressureLossMapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return RibbonHighlightExecutor.ExecutePressureLossMap(commandData, ref message);
        }
    }
}
