using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VentCalc.Core.Models;
using VentCalc.Core.Services;
using VentCalc.UI.ViewModels;

namespace VentCalc.Revit.Services
{
    public sealed class RevitVentCalcCenterDataLoader
    {
        private static readonly HashSet<BuiltInCategory> SupportedCategories = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_MechanicalEquipment
        };

        public IReadOnlyList<VentSystemCatalogItem> ReadSystemCatalog(UIDocument uiDocument)
        {
            return new RevitSystemCatalogService().Read(uiDocument.Document);
        }

        public VentCalcCenterData Load(UIDocument uiDocument, AerodynamicSettings settings)
        {
            try
            {
                var selectionReader = new RevitSelectionReader();
                if (!selectionReader.TryGetSingleSelectedVentElement(uiDocument, out Element? element, out string? errorMessage))
                {
                    string message = ToCenterSelectionMessage(errorMessage);
                    return CreateWarningData(uiDocument, message);
                }

                VentCalcCenterData data = LoadElement(uiDocument, element, settings);
                data.LoadMode = "selectedElement";
                return data;
            }
            catch (Exception exception)
            {
                return CreateExceptionData(uiDocument, exception);
            }
        }

        public VentCalcCenterData Load(UIDocument uiDocument, AerodynamicSettings settings, ElementId selectedElementId)
        {
            try
            {
                Element? element = uiDocument.Document.GetElement(selectedElementId);
                if (!IsSupportedVentilationElement(element))
                {
                    return CreateWarningData(uiDocument, "Выбранный элемент не относится к вентиляционной системе.");
                }

                VentCalcCenterData data = LoadElement(uiDocument, element!, settings);
                data.LoadMode = "selectedElement";
                return data;
            }
            catch (Exception exception)
            {
                return CreateExceptionData(uiDocument, exception);
            }
        }


        public VentCalcCenterData LoadSystem(UIDocument uiDocument, AerodynamicSettings settings, VentSystemCatalogItem catalogItem)
        {
            try
            {
                IReadOnlyList<ElementId> systemElementIds = new RevitSystemCatalogService().FindSystemElementIds(uiDocument.Document, catalogItem.SystemName, catalogItem.SystemType);
                if (systemElementIds.Count == 0)
                {
                    return CreateWarningData(uiDocument, $"Система {catalogItem.SystemName} не найдена в проекте.");
                }

                var loadedComponents = new List<VentCalcCenterData>();
                var visited = new HashSet<long>();
                foreach (ElementId elementId in systemElementIds)
                {
                    if (visited.Contains(elementId.Value))
                    {
                        continue;
                    }

                    Element? element = uiDocument.Document.GetElement(elementId);
                    if (!IsSupportedVentilationElement(element))
                    {
                        continue;
                    }

                    VentCalcCenterData component = LoadElement(uiDocument, element!, settings, catalogItem.SystemName, catalogItem.SystemType);
                    foreach (VentNetworkNode node in component.NetworkInfo?.Elements ?? Array.Empty<VentNetworkNode>())
                    {
                        if (long.TryParse(node.ElementId, out long nodeId))
                        {
                            visited.Add(nodeId);
                        }
                    }

                    loadedComponents.Add(component);
                }

                VentCalcCenterData selected = loadedComponents
                    .Where(component => component.NetworkInfo != null)
                    .OrderByDescending(component => component.NetworkInfo!.Elements.Count)
                    .FirstOrDefault() ?? CreateWarningData(uiDocument, $"Не удалось прочитать компоненты системы {catalogItem.SystemName}.");

                selected.LoadMode = "systemName";
                selected.SelectedSystemName = catalogItem.SystemName;
                selected.SelectedSystemType = catalogItem.SystemType;
                selected.SystemComponentCount = Math.Max(loadedComponents.Count, 1);
                if (loadedComponents.Count > 1)
                {
                    selected.Warnings.Add($"Система {catalogItem.SystemName} содержит несколько несвязанных компонентов ({loadedComponents.Count}). Загружен самый большой компонент.");
                }

                return selected;
            }
            catch (Exception exception)
            {
                return CreateExceptionData(uiDocument, exception);
            }
        }

        private VentCalcCenterData LoadElement(UIDocument uiDocument, Element element, AerodynamicSettings settings, string? filterSystemName = null, string? filterSystemType = null)
        {
            try
            {
                var parameterReader = new RevitParameterReader();
                var connectorReader = new RevitConnectorReader();
                var elementInfoReader = new RevitElementInfoReader(parameterReader, connectorReader);
                var networkReader = new RevitVentNetworkReader(elementInfoReader);
                var pathDataReader = new RevitVentPathDataReader(elementInfoReader);
                var pathFinder = new VentPathFinder();
                var ductGeometryReader = new RevitDuctGeometryReader();
                var localResistanceDataReader = new RevitLocalResistanceDataReader();
                var aerodynamicCalculator = new AerodynamicCalculator();

                VentElementInfo elementInfo = elementInfoReader.Read(element);
                VentNetworkInfo networkInfo = pathDataReader.Enrich(uiDocument.Document, networkReader.Read(uiDocument.Document, element.Id));
                if (!string.IsNullOrWhiteSpace(filterSystemName))
                {
                    networkInfo = FilterNetworkToSystem(networkInfo, filterSystemName!, filterSystemType ?? string.Empty);
                }

                VentPathSummary pathSummary = pathFinder.FindPaths(networkInfo);
                IReadOnlyDictionary<long, DuctGeometryData> ductDataByElementId = ductGeometryReader.ReadDucts(uiDocument.Document, networkInfo);
                IReadOnlyDictionary<long, LocalResistanceElementData> localDataByElementId = localResistanceDataReader.ReadElements(uiDocument.Document, networkInfo);
                AerodynamicCalculationSummary aerodynamicSummary = aerodynamicCalculator.CalculatePaths(
                    pathSummary.Paths,
                    ductDataByElementId,
                    localDataByElementId,
                    settings);
                string reportText = elementInfo.ToReportText()
                    + Environment.NewLine
                    + networkInfo.ToReportText(pathSummary)
                    + Environment.NewLine
                    + pathSummary.ToReportText()
                    + Environment.NewLine
                    + aerodynamicSummary.ToReportText();

                VentCalcCenterData data = CreateBaseData(uiDocument);
                data.Success = true;
                data.SelectedElementInfo = elementInfo;
                data.NetworkInfo = networkInfo;
                data.PathSummary = pathSummary;
                data.AerodynamicSummary = aerodynamicSummary;
                data.ReportText = reportText;
                AddMixedSystemWarning(data);
                return data;
            }
            catch (Exception exception)
            {
                return CreateExceptionData(uiDocument, exception);
            }
        }

        private static VentNetworkInfo FilterNetworkToSystem(VentNetworkInfo networkInfo, string systemName, string systemType)
        {
            List<VentNetworkNode> nodes = networkInfo.Elements
                .Where(node => SameSystem(node.SystemName, systemName) && (string.IsNullOrWhiteSpace(systemType) || SameSystem(node.SystemType, systemType)))
                .ToList();
            if (nodes.Count == 0)
            {
                return networkInfo;
            }

            var ids = new HashSet<string>(nodes.Select(node => node.ElementId), StringComparer.Ordinal);
            List<VentNetworkConnection> connections = networkInfo.Connections
                .Where(connection => ids.Contains(connection.FromElementId) && ids.Contains(connection.ToElementId))
                .ToList();

            return new VentNetworkInfo(
                networkInfo.SelectedElementId,
                nodes,
                connections,
                nodes.Count(node => node.CategoryKey == "OST_DuctCurves"),
                nodes.Count(node => node.CategoryKey == "OST_DuctFitting"),
                nodes.Count(node => node.CategoryKey == "OST_DuctAccessory"),
                nodes.Count(node => node.CategoryKey == "OST_DuctTerminal"),
                nodes.Count(node => node.CategoryKey == "OST_MechanicalEquipment"),
                nodes.Sum(node => node.OpenConnectorCount),
                nodes.Count(node => node.IsStartCandidate),
                nodes.Count(node => node.IsEndCandidate));
        }

        private static bool SameSystem(string actual, string expected)
        {
            return string.Equals((actual ?? string.Empty).Trim(), (expected ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static void AddMixedSystemWarning(VentCalcCenterData data)
        {
            var pairs = data.NetworkInfo?.Elements
                .Where(node => !string.IsNullOrWhiteSpace(node.SystemName) && node.SystemName != "—")
                .Select(node => $"{node.SystemName} / {node.SystemType}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            if (pairs.Count > 1)
            {
                data.Warnings.Add("В найденной сети обнаружены элементы разных систем: " + string.Join("; ", pairs));
            }
        }

        private static VentCalcCenterData CreateWarningData(UIDocument uiDocument, string message)
        {
            VentCalcCenterData data = CreateBaseData(uiDocument);
            data.Success = false;
            data.IsUserSelectionWarning = true;
            data.ErrorMessage = message;
            data.ReportText = message;
            data.Warnings = new List<string> { message };
            return data;
        }

        private static VentCalcCenterData CreateExceptionData(UIDocument uiDocument, Exception exception)
        {
            ErrorReporter.Report(uiDocument.Application, "Ошибка загрузки данных VentCalc Center", exception);
            VentCalcCenterData data = CreateBaseData(uiDocument);
            data.Success = false;
            data.ErrorMessage = exception.Message;
            data.ReportText = exception.ToString();
            data.Warnings = new List<string> { "Ошибка при чтении вентиляционной сети. Окно оставлено открытым; смотрите лог VentCalc." };
            return data;
        }

        private static VentCalcCenterData CreateBaseData(UIDocument uiDocument)
        {
            var data = new VentCalcCenterData
            {
                RevitVersion = uiDocument.Application.Application.VersionNumber,
                RevitFilePath = string.IsNullOrWhiteSpace(uiDocument.Document.PathName) ? uiDocument.Document.Title : uiDocument.Document.PathName,
                LoadedAt = DateTime.Now
            };
            try
            {
                data.SystemCatalog.AddRange(new RevitSystemCatalogService().Read(uiDocument.Document));
            }
            catch (Exception)
            {
                data.Warnings.Add("Не удалось прочитать список систем проекта.");
            }

            return data;
        }

        private static string ToCenterSelectionMessage(string? selectionReaderMessage)
        {
            if (string.Equals(selectionReaderMessage, "Выберите элемент воздуховодной системы.", StringComparison.Ordinal))
            {
                return "Выбранный элемент не относится к вентиляционной системе.";
            }

            return "Выберите один элемент воздуховодной системы в Revit.";
        }

        private static bool IsSupportedVentilationElement(Element? element)
        {
            if (element?.Category == null)
            {
                return false;
            }

            var category = (BuiltInCategory)element.Category.Id.Value;
            return SupportedCategories.Contains(category);
        }
    }
}
