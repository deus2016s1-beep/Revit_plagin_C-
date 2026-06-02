using System;
using Autodesk.Revit.UI;

namespace VentCalc.Revit
{
    public sealed class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                RibbonBuilder.Build(application);
                return Result.Succeeded;
            }
            catch (Exception exception)
            {
                TaskDialog.Show("VentCalc — ошибка запуска", exception.ToString());
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
