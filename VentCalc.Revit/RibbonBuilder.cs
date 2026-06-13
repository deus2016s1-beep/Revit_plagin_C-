using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
using VentCalc.Revit.Commands;

namespace VentCalc.Revit
{
    public static class RibbonBuilder
    {
        private const string TabName = "VentCalc";
        private const string VentilationPanelName = "Вентиляция";

        public static void Build(UIControlledApplication application)
        {
            CreateTabIfMissing(application);

            RibbonPanel ventilationPanel = GetOrCreatePanel(application, VentilationPanelName);
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            AddButtonIfMissing<VentCalcCenterCommand>(ventilationPanel, "VentCalcCenter", "VentCalc", assemblyPath);
            AddButtonIfMissing<PathHighlightCommand>(ventilationPanel, "VentCalcPaths", "Критическая трасса", assemblyPath);
            AddButtonIfMissing<HighlightVelocityCommand>(ventilationPanel, "VentCalcVelocity", "Карта скоростей", assemblyPath);
            AddButtonIfMissing<SettingsCommand>(ventilationPanel, "VentCalcSettings", "Настройки", assemblyPath);
        }

        private static void CreateTabIfMissing(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // The tab already exists. Reuse it instead of failing Revit startup.
            }
        }

        private static RibbonPanel GetOrCreatePanel(UIControlledApplication application, string panelName)
        {
            RibbonPanel existingPanel = application
                .GetRibbonPanels(TabName)
                .FirstOrDefault(panel => string.Equals(panel.Name, panelName, StringComparison.Ordinal));

            return existingPanel ?? application.CreateRibbonPanel(TabName, panelName);
        }

        private static void AddButtonIfMissing<TCommand>(RibbonPanel panel, string internalName, string displayName, string assemblyPath)
        {
            bool buttonExists = panel
                .GetItems()
                .Any(item => string.Equals(item.Name, internalName, StringComparison.Ordinal));

            if (buttonExists)
            {
                return;
            }

            var pushButtonData = new PushButtonData(
                internalName,
                displayName,
                assemblyPath,
                typeof(TCommand).FullName);

            panel.AddItem(pushButtonData);
        }
    }
}
