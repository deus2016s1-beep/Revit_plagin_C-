using System;
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
            CreateTab(application);

            RibbonPanel ventilationPanel = application.CreateRibbonPanel(TabName, VentilationPanelName);
            RibbonPanel servicePanel = application.CreateRibbonPanel(TabName, ServicePanelName);

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            AddButton<CalculateCommand>(ventilationPanel, "VentCalcCalculate", "Расчёт", assemblyPath);
            AddButton<InspectorCommand>(ventilationPanel, "VentCalcInspector", "Инспектор", assemblyPath);
            AddButton<LocalResistanceCommand>(ventilationPanel, "VentCalcLocalResistance", "МС", assemblyPath);
            AddButton<HighlightCriticalPathCommand>(ventilationPanel, "VentCalcCriticalPath", "Трасса", assemblyPath);
            AddButton<HighlightVelocityCommand>(ventilationPanel, "VentCalcVelocity", "Скорости", assemblyPath);
            AddButton<SettingsCommand>(ventilationPanel, "VentCalcSettings", "Настройки", assemblyPath);

            AddButton<ClearHighlightsCommand>(servicePanel, "VentCalcClearHighlights", "Очистить", assemblyPath);
            AddButton<AboutCommand>(servicePanel, "VentCalcAbout", "О программе", assemblyPath);
        }

        private static void CreateTab(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Revit throws when the tab already exists, for example after another VentCalc module created it.
            }
        }

        private static void AddButton<TCommand>(RibbonPanel panel, string internalName, string displayName, string assemblyPath)
        {
            var pushButtonData = new PushButtonData(
                internalName,
                displayName,
                assemblyPath,
                typeof(TCommand).FullName);

            panel.AddItem(pushButtonData);
        }
    }
}
