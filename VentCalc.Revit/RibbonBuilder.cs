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
        private const string ServicePanelName = "Сервис";

        public static void Build(UIControlledApplication application)
        {
            CreateTabIfMissing(application);

            RibbonPanel ventilationPanel = GetOrCreatePanel(application, VentilationPanelName);
            RibbonPanel servicePanel = GetOrCreatePanel(application, ServicePanelName);

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            AddButtonIfMissing<CalculateCommand>(ventilationPanel, "VentCalcCalculate", "Расчёт", assemblyPath);
            AddButtonIfMissing<InspectorCommand>(ventilationPanel, "VentCalcInspector", "Инспектор", assemblyPath);
            AddButtonIfMissing<LocalResistanceCommand>(ventilationPanel, "VentCalcLocalResistance", "МС", assemblyPath);
            AddButtonIfMissing<HighlightCriticalPathCommand>(ventilationPanel, "VentCalcCriticalPath", "Трасса", assemblyPath);
            AddButtonIfMissing<HighlightVelocityCommand>(ventilationPanel, "VentCalcVelocity", "Скорости", assemblyPath);
            AddButtonIfMissing<SettingsCommand>(ventilationPanel, "VentCalcSettings", "Настройки", assemblyPath);

            AddButtonIfMissing<ClearHighlightsCommand>(servicePanel, "VentCalcClearHighlights", "Очистить", assemblyPath);
            AddButtonIfMissing<AboutCommand>(servicePanel, "VentCalcAbout", "О программе", assemblyPath);
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
