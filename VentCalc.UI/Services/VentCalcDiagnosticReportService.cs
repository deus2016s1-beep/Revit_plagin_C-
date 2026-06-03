using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using VentCalc.Core.Models;
using VentCalc.UI.ViewModels;

namespace VentCalc.UI.Services
{
    public sealed class VentCalcDiagnosticReportResult
    {
        public string TxtPath { get; set; } = string.Empty;

        public string JsonPath { get; set; } = string.Empty;

        public string PreviewText { get; set; } = string.Empty;
    }

    public static class VentCalcDiagnosticReportService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        public static string GetReportsDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VentCalc",
                "reports");
        }

        public static VentCalcDiagnosticReportResult Generate(VentCalcCenterViewModel viewModel)
        {
            Directory.CreateDirectory(GetReportsDirectory());
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string basePath = Path.Combine(GetReportsDirectory(), $"ventcalc_report_{timestamp}");
            string txtPath = basePath + ".txt";
            string jsonPath = basePath + ".json";
            DateTime createdAt = DateTime.Now;
            ReportDiagnostics diagnostics = BuildDiagnostics(viewModel);
            string text = BuildTextReport(viewModel, createdAt, diagnostics);
            File.WriteAllText(txtPath, text, Encoding.UTF8);
            File.WriteAllText(jsonPath, BuildJsonReport(viewModel, createdAt, diagnostics), new UTF8Encoding(false));

            return new VentCalcDiagnosticReportResult
            {
                TxtPath = txtPath,
                JsonPath = jsonPath,
                PreviewText = text.Length > 6000 ? text.Substring(0, 6000) + Environment.NewLine + "..." : text
            };
        }

        private static ReportDiagnostics BuildDiagnostics(VentCalcCenterViewModel viewModel)
        {
            List<LocalResistanceApplication> localApplications = GetLocalApplications(viewModel).ToList();
            List<DuctApplication> ductApplications = GetDuctApplications(viewModel).ToList();
            return new ReportDiagnostics
            {
                LocalApplications = localApplications,
                DuctApplications = ductApplications,
                UniqueRecommendedZetaElements = BuildUniqueLocalElements(localApplications, "Рекомендовано"),
                UniqueCommentZetaElements = BuildUniqueLocalElements(localApplications, "Комментарии"),
                MissingZetaElements = BuildUniqueLocalElements(localApplications, "Не найдено"),
                TopLocalResistanceContribution = localApplications.OrderByDescending(item => item.Local.LocalPressureLossPa).FirstOrDefault(),
                TopFrictionContribution = ductApplications.OrderByDescending(item => item.Duct.FrictionPressureLossPa).FirstOrDefault(),
                MaxVelocityDuct = ductApplications.OrderByDescending(item => item.Duct.VelocityMs).FirstOrDefault(),
                MinVelocityDuct = ductApplications.Where(item => item.Duct.VelocityMs > 0).OrderBy(item => item.Duct.VelocityMs).FirstOrDefault()
            };
        }

        private static string BuildTextReport(VentCalcCenterViewModel viewModel, DateTime createdAt, ReportDiagnostics diagnostics)
        {
            var builder = new StringBuilder();
            PathCalculationInfo? criticalPath = viewModel.CriticalPath;
            IReadOnlyList<string> systemPairs = GetSystemPairs(viewModel).ToList();

            builder.AppendLine("VentCalc v2.0 — отчёт для проверки");
            builder.AppendLine(new string('=', 60));
            builder.AppendLine();
            builder.AppendLine("1. Общая информация");
            builder.AppendLine($"Дата/время: {createdAt:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine("Версия VentCalc: 2.0");
            builder.AppendLine($"Версия Revit: {viewModel.RevitVersion}");
            builder.AppendLine($"Файл Revit: {viewModel.RevitFilePath}");
            builder.AppendLine($"Выбранный ElementId: {viewModel.SelectedElementId}");
            builder.AppendLine($"Категория: {viewModel.SelectedElementInfo?.CategoryName ?? "—"}");
            builder.AppendLine($"Имя: {viewModel.SelectedElementInfo?.Name ?? "—"}");
            builder.AppendLine($"Тип: {viewModel.SelectedElementInfo?.TypeName ?? "—"}");
            builder.AppendLine($"Семейство: {viewModel.SelectedElementInfo?.FamilyName ?? "—"}");
            builder.AppendLine();

            builder.AppendLine("2. Загруженная система");
            builder.AppendLine($"SystemName: {viewModel.SystemName}");
            builder.AppendLine($"SystemType: {viewModel.SystemType}");
            builder.AppendLine($"Direction: {viewModel.Direction}");
            builder.AppendLine($"Всего элементов: {viewModel.TotalElements}");
            builder.AppendLine($"Воздуховодов: {viewModel.DuctCount}");
            builder.AppendLine($"Фитингов: {viewModel.FittingCount}");
            builder.AppendLine($"Арматуры: {viewModel.AccessoryCount}");
            builder.AppendLine($"Терминалов: {viewModel.TerminalCount}");
            builder.AppendLine($"Оборудования: {viewModel.EquipmentCount}");
            builder.AppendLine($"Открытых коннекторов: {viewModel.OpenConnectorCount}");
            builder.AppendLine($"Связей: {viewModel.ConnectionCount}");
            builder.AppendLine($"Стартовых точек: {viewModel.TraceStartCount}");
            builder.AppendLine($"Конечных точек: {viewModel.TraceEndCount}");
            builder.AppendLine($"Игнорируемых заглушек: {viewModel.IgnoredCapCount}");
            builder.AppendLine(systemPairs.Count <= 1
                ? $"Сеть содержит одну систему: {systemPairs.FirstOrDefault() ?? viewModel.SystemName}"
                : "WARNING: В найденной сети обнаружены элементы разных систем: " + string.Join("; ", systemPairs));
            builder.AppendLine();

            builder.AppendLine("3. Настройки расчёта");
            builder.AppendLine($"Плотность, кг/м³: {viewModel.Settings.AirDensityKgM3:0.###}");
            builder.AppendLine($"Вязкость, Па·с: {viewModel.Settings.AirDynamicViscosityPaS:0.########E+0}");
            builder.AppendLine($"Шероховатость, мм: {viewModel.Settings.RoughnessMm:0.###}");
            builder.AppendLine($"Шероховатость, м: {viewModel.Settings.RoughnessM:0.########}");
            builder.AppendLine($"Запас давления, %: {viewModel.Settings.PressureReservePercent:0.###}");
            builder.AppendLine($"Min/Max/Critical скорость, м/с: {viewModel.Settings.MinVelocityMs:0.###} / {viewModel.Settings.MaxVelocityMs:0.###} / {viewModel.Settings.CriticalVelocityMs:0.###}");
            builder.AppendLine();

            builder.AppendLine("4. Диагностики");
            foreach (VentIssueInfo issue in viewModel.Issues)
            {
                builder.AppendLine($"[{issue.Severity}] ElementId={issue.ElementId}; {issue.Message}; Рекомендация: {issue.Recommendation}");
            }
            builder.AppendLine();

            builder.AppendLine("5. Трассы");
            foreach (PathRow path in viewModel.Paths)
            {
                double totalWithReserve = (path.Calculation?.TotalPressureLossPa ?? 0) * (1.0 + viewModel.Settings.PressureReservePercent / 100.0);
                builder.AppendLine($"Трасса №{path.PathIndex}: Start={path.StartElementId}; End={path.EndElementId}; Элементов={path.TotalElementCount}; Воздуховодов={path.DuctCount}; Фитингов={path.FittingCount}; Длина={path.TotalDuctLengthM:0.###} м; Расход={path.FlowM3h}; Трение={(path.Calculation?.TotalFrictionPressureLossPa ?? 0):0.###} Па; МС={(path.Calculation?.TotalLocalPressureLossPa ?? 0):0.###} Па; Итого={path.TotalPressureLossPa:0.###} Па; Итого с запасом={totalWithReserve:0.###} Па");
                builder.AppendLine("  Цепочка: " + string.Join(" → ", path.ElementIds));
            }
            builder.AppendLine();

            builder.AppendLine("6. Критическая трасса");
            if (criticalPath == null)
            {
                builder.AppendLine("Критическая трасса не определена.");
            }
            else
            {
                builder.AppendLine($"Номер: {criticalPath.PathIndex}");
                builder.AppendLine("Почему выбрана: максимальные предварительные потери давления среди найденных трасс.");
                builder.AppendLine($"Трение: {criticalPath.TotalFrictionPressureLossPa:0.###} Па");
                builder.AppendLine($"МС: {criticalPath.TotalLocalPressureLossPa:0.###} Па");
                builder.AppendLine($"Итого: {criticalPath.TotalPressureLossPa:0.###} Па");
                builder.AppendLine($"Итого с запасом: {criticalPath.TotalPressureLossPa * (1.0 + viewModel.Settings.PressureReservePercent / 100.0):0.###} Па");
            }
            builder.AppendLine();

            builder.AppendLine("7. Воздуховоды критической трассы");
            foreach (DuctCalculationInfo duct in criticalPath?.Ducts ?? Enumerable.Empty<DuctCalculationInfo>())
            {
                builder.AppendLine($"{duct.ElementId}; {duct.Size}; Q={duct.FlowM3h:0.###} м³/ч; L={duct.LengthM:0.###} м; A={duct.AreaM2:0.####} м²; Dэкв={duct.EquivalentDiameterM:0.####} м; V={duct.VelocityMs:0.###} м/с; Re={duct.Reynolds:0.#}; λ={duct.Lambda:0.####}; Pv={duct.DynamicPressurePa:0.###} Па; R={duct.SpecificPressureLossPaPerM:0.###} Па/м; R·l={duct.FrictionPressureLossPa:0.###} Па");
            }
            builder.AppendLine();

            builder.AppendLine("8. Местные сопротивления критической трассы");
            foreach (LocalResistanceCalculationInfo local in criticalPath?.LocalResistances ?? Enumerable.Empty<LocalResistanceCalculationInfo>())
            {
                builder.AppendLine($"{local.ElementId}; {local.TypeName}; {local.FamilyName}; {local.Size}; ζ={local.Zeta:0.###}; источник={local.Source}; V={local.VelocityMs:0.###} м/с; Pv={local.DynamicPressurePa:0.###} Па; Z={local.LocalPressureLossPa:0.###} Па; warnings={local.WarningText}");
            }
            builder.AppendLine();

            builder.AppendLine("9. Проверочные значения");
            AppendTopLocalContribution(builder, diagnostics.TopLocalResistanceContribution);
            AppendTopFrictionContribution(builder, diagnostics.TopFrictionContribution);
            builder.AppendLine($"Самая большая скорость: {(diagnostics.MaxVelocityDuct?.Duct.VelocityMs ?? 0):0.###} м/с (ElementId {diagnostics.MaxVelocityDuct?.Duct.ElementId.ToString(CultureInfo.InvariantCulture) ?? "—"}, трасса №{diagnostics.MaxVelocityDuct?.PathIndex.ToString(CultureInfo.InvariantCulture) ?? "—"})");
            builder.AppendLine($"Самая маленькая скорость: {(diagnostics.MinVelocityDuct?.Duct.VelocityMs ?? 0):0.###} м/с (ElementId {diagnostics.MinVelocityDuct?.Duct.ElementId.ToString(CultureInfo.InvariantCulture) ?? "—"}, трасса №{diagnostics.MinVelocityDuct?.PathIndex.ToString(CultureInfo.InvariantCulture) ?? "—"})");
            AppendUniqueLocalGroup(builder, "Уникальные элементы с ζ из рекомендации", diagnostics.UniqueRecommendedZetaElements);
            builder.AppendLine($"Количество применений ζ из рекомендации по трассам: {diagnostics.LocalApplications.Count(item => item.Local.Source == "Рекомендовано")}");
            AppendUniqueLocalGroup(builder, "Уникальные элементы с ζ из комментария", diagnostics.UniqueCommentZetaElements);
            builder.AppendLine($"Количество применений ζ из комментария по трассам: {diagnostics.LocalApplications.Count(item => item.Local.Source == "Комментарии")}");
            AppendUniqueLocalGroup(builder, "Элементы без ζ", diagnostics.MissingZetaElements);
            builder.AppendLine($"Количество элементов без ζ: {diagnostics.MissingZetaElements.Count}");

            return builder.ToString();
        }

        private static string BuildJsonReport(VentCalcCenterViewModel viewModel, DateTime createdAt, ReportDiagnostics diagnostics)
        {
            PathCalculationInfo? criticalPath = viewModel.CriticalPath;
            var payload = new
            {
                app = new
                {
                    name = "VentCalc",
                    version = "2.0",
                    createdAt = createdAt.ToString("O", CultureInfo.InvariantCulture),
                    revitVersion = viewModel.RevitVersion,
                    revitFilePath = viewModel.RevitFilePath
                },
                selectedElement = new
                {
                    elementId = ToLongOrNull(viewModel.SelectedElementInfo?.ElementId),
                    categoryName = viewModel.SelectedElementInfo?.CategoryName,
                    name = viewModel.SelectedElementInfo?.Name,
                    typeName = viewModel.SelectedElementInfo?.TypeName,
                    familyName = viewModel.SelectedElementInfo?.FamilyName
                },
                system = new
                {
                    systemName = viewModel.SystemName,
                    systemType = viewModel.SystemType,
                    direction = viewModel.Direction,
                    totalElements = viewModel.TotalElements,
                    ductCount = viewModel.DuctCount,
                    fittingCount = viewModel.FittingCount,
                    accessoryCount = viewModel.AccessoryCount,
                    terminalCount = viewModel.TerminalCount,
                    equipmentCount = viewModel.EquipmentCount,
                    openConnectorCount = viewModel.OpenConnectorCount,
                    connectionCount = viewModel.ConnectionCount,
                    startCount = viewModel.TraceStartCount,
                    endCount = viewModel.TraceEndCount,
                    ignoredCapCount = viewModel.IgnoredCapCount,
                    systemPairs = GetSystemPairs(viewModel)
                },
                settings = new
                {
                    airDensityKgM3 = viewModel.Settings.AirDensityKgM3,
                    airDynamicViscosityPaS = viewModel.Settings.AirDynamicViscosityPaS,
                    roughnessMm = viewModel.Settings.RoughnessMm,
                    roughnessM = viewModel.Settings.RoughnessM,
                    pressureReservePercent = viewModel.Settings.PressureReservePercent,
                    minVelocityMs = viewModel.Settings.MinVelocityMs,
                    maxVelocityMs = viewModel.Settings.MaxVelocityMs,
                    criticalVelocityMs = viewModel.Settings.CriticalVelocityMs
                },
                issues = viewModel.Issues.Select(issue => new
                {
                    issue.Severity,
                    elementId = ToLongOrNull(issue.ElementId),
                    issue.Category,
                    issue.Message,
                    issue.Recommendation
                }),
                paths = viewModel.Paths.Select(path => new
                {
                    pathIndex = path.PathIndex,
                    startElementId = ToLongOrNull(path.StartElementId),
                    endElementId = ToLongOrNull(path.EndElementId),
                    totalElementCount = path.TotalElementCount,
                    ductCount = path.DuctCount,
                    fittingCount = path.FittingCount,
                    terminalCount = path.TerminalCount,
                    totalDuctLengthM = path.TotalDuctLengthM,
                    flowM3h = ParseFlowM3h(path.FlowM3h),
                    flowText = path.FlowM3h,
                    frictionPressureLossPa = path.Calculation?.TotalFrictionPressureLossPa ?? 0,
                    localPressureLossPa = path.Calculation?.TotalLocalPressureLossPa ?? 0,
                    totalPressureLossPa = path.TotalPressureLossPa,
                    totalPressureLossWithReservePa = path.TotalPressureLossPa * (1.0 + viewModel.Settings.PressureReservePercent / 100.0),
                    elementIds = path.ElementIds.Select(ToLongOrNull).Where(id => id.HasValue).Select(id => id!.Value).ToList()
                }),
                criticalPath = criticalPath == null ? null : new
                {
                    pathIndex = criticalPath.PathIndex,
                    startElementId = criticalPath.StartElementId,
                    endElementId = criticalPath.EndElementId,
                    frictionPressureLossPa = criticalPath.TotalFrictionPressureLossPa,
                    localPressureLossPa = criticalPath.TotalLocalPressureLossPa,
                    totalPressureLossPa = criticalPath.TotalPressureLossPa,
                    totalPressureLossWithReservePa = criticalPath.TotalPressureLossPa * (1.0 + viewModel.Settings.PressureReservePercent / 100.0),
                    elementIds = criticalPath.ElementIds
                },
                criticalPathDucts = criticalPath?.Ducts ?? Enumerable.Empty<DuctCalculationInfo>(),
                criticalPathLocalResistances = criticalPath?.LocalResistances ?? Enumerable.Empty<LocalResistanceCalculationInfo>(),
                allDucts = GetAllDucts(viewModel),
                allLocalResistances = GetAllLocalResistances(viewModel),
                uniqueRecommendedZetaElements = diagnostics.UniqueRecommendedZetaElements,
                uniqueCommentZetaElements = diagnostics.UniqueCommentZetaElements,
                missingZetaElements = diagnostics.MissingZetaElements,
                localResistanceApplicationsCount = diagnostics.LocalApplications.Count,
                recommendedZetaApplicationsCount = diagnostics.LocalApplications.Count(item => item.Local.Source == "Рекомендовано"),
                commentZetaApplicationsCount = diagnostics.LocalApplications.Count(item => item.Local.Source == "Комментарии"),
                missingZetaApplicationsCount = diagnostics.LocalApplications.Count(item => item.Local.Source == "Не найдено"),
                topLocalResistanceContribution = ToJsonTopLocal(diagnostics.TopLocalResistanceContribution),
                topFrictionContribution = ToJsonTopFriction(diagnostics.TopFrictionContribution)
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private static IEnumerable<DuctCalculationInfo> GetAllDucts(VentCalcCenterViewModel viewModel)
        {
            return viewModel.AerodynamicSummary?.Paths.SelectMany(path => path.Ducts) ?? Enumerable.Empty<DuctCalculationInfo>();
        }

        private static IEnumerable<LocalResistanceCalculationInfo> GetAllLocalResistances(VentCalcCenterViewModel viewModel)
        {
            return viewModel.AerodynamicSummary?.Paths.SelectMany(path => path.LocalResistances) ?? Enumerable.Empty<LocalResistanceCalculationInfo>();
        }

        private static IEnumerable<LocalResistanceApplication> GetLocalApplications(VentCalcCenterViewModel viewModel)
        {
            return viewModel.AerodynamicSummary?.Paths
                .SelectMany(path => path.LocalResistances.Select(local => new LocalResistanceApplication(path.PathIndex, local)))
                ?? Enumerable.Empty<LocalResistanceApplication>();
        }

        private static IEnumerable<DuctApplication> GetDuctApplications(VentCalcCenterViewModel viewModel)
        {
            return viewModel.AerodynamicSummary?.Paths
                .SelectMany(path => path.Ducts.Select(duct => new DuctApplication(path.PathIndex, duct)))
                ?? Enumerable.Empty<DuctApplication>();
        }

        private static IReadOnlyList<UniqueLocalResistanceElement> BuildUniqueLocalElements(IEnumerable<LocalResistanceApplication> applications, string source)
        {
            return applications
                .Where(item => string.Equals(item.Local.Source, source, StringComparison.Ordinal))
                .GroupBy(item => item.Local.ElementId)
                .Select(group =>
                {
                    LocalResistanceCalculationInfo first = group.First().Local;
                    return new UniqueLocalResistanceElement
                    {
                        ElementId = first.ElementId,
                        TypeName = first.TypeName,
                        FamilyName = first.FamilyName,
                        Size = first.Size,
                        Zeta = first.Zeta,
                        Source = first.Source,
                        ApplicationsCount = group.Count(),
                        MaxLocalPressureLossPa = group.Max(item => item.Local.LocalPressureLossPa),
                        PathIndexes = group.Select(item => item.PathIndex).Distinct().OrderBy(index => index).ToList()
                    };
                })
                .OrderBy(item => item.ElementId)
                .ToList();
        }

        private static void AppendUniqueLocalGroup(StringBuilder builder, string title, IReadOnlyList<UniqueLocalResistanceElement> elements)
        {
            builder.AppendLine($"{title}: {elements.Count}");
            if (elements.Count == 0)
            {
                builder.AppendLine("  —");
                return;
            }

            foreach (UniqueLocalResistanceElement element in elements)
            {
                builder.AppendLine($"  {element.ElementId}; тип={element.TypeName}; семейство={element.FamilyName}; ζ={element.Zeta:0.###}; применений={element.ApplicationsCount}; трассы={string.Join(", ", element.PathIndexes)}");
            }
        }

        private static void AppendTopLocalContribution(StringBuilder builder, LocalResistanceApplication? topLocal)
        {
            if (topLocal == null)
            {
                builder.AppendLine("Самый большой вклад по МС: —");
                return;
            }

            builder.AppendLine($"Самый большой вклад по МС: ElementId {topLocal.Local.ElementId}; тип={topLocal.Local.TypeName}; семейство={topLocal.Local.FamilyName}; ζ={topLocal.Local.Zeta:0.###}; Z={topLocal.Local.LocalPressureLossPa:0.###} Па; трасса №{topLocal.PathIndex}");
        }

        private static void AppendTopFrictionContribution(StringBuilder builder, DuctApplication? topFriction)
        {
            if (topFriction == null)
            {
                builder.AppendLine("Самый большой вклад по трению: —");
                return;
            }

            builder.AppendLine($"Самый большой вклад по трению: ElementId {topFriction.Duct.ElementId}; размер={topFriction.Duct.Size}; R·l={topFriction.Duct.FrictionPressureLossPa:0.###} Па; трасса №{topFriction.PathIndex}");
        }

        private static object? ToJsonTopLocal(LocalResistanceApplication? topLocal)
        {
            return topLocal == null
                ? null
                : new
                {
                    pathIndex = topLocal.PathIndex,
                    elementId = topLocal.Local.ElementId,
                    typeName = topLocal.Local.TypeName,
                    familyName = topLocal.Local.FamilyName,
                    zeta = topLocal.Local.Zeta,
                    source = topLocal.Local.Source,
                    dynamicPressurePa = topLocal.Local.DynamicPressurePa,
                    localPressureLossPa = topLocal.Local.LocalPressureLossPa
                };
        }

        private static object? ToJsonTopFriction(DuctApplication? topFriction)
        {
            return topFriction == null
                ? null
                : new
                {
                    pathIndex = topFriction.PathIndex,
                    elementId = topFriction.Duct.ElementId,
                    size = topFriction.Duct.Size,
                    specificPressureLossPaPerM = topFriction.Duct.SpecificPressureLossPaPerM,
                    frictionPressureLossPa = topFriction.Duct.FrictionPressureLossPa
                };
        }

        private static long? ToLongOrNull(string? value)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : null;
        }

        private static double ParseFlowM3h(string? flowText)
        {
            if (string.IsNullOrWhiteSpace(flowText))
            {
                return 0;
            }

            string numericText = new string(flowText
                .Replace(',', '.')
                .TakeWhile(character => char.IsDigit(character) || character == '.' || character == '-' || character == '+')
                .ToArray());
            return double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
        }

        private static IEnumerable<string> GetSystemPairs(VentCalcCenterViewModel viewModel)
        {
            return viewModel.NetworkInfo?.Elements
                .Select(node => $"{node.SystemName} / {node.SystemType}")
                .Where(value => !string.IsNullOrWhiteSpace(value) && value != "— / —")
                .Distinct()
                .OrderBy(value => value, StringComparer.Ordinal)
                ?? Enumerable.Empty<string>();
        }

        private sealed class ReportDiagnostics
        {
            public IReadOnlyList<LocalResistanceApplication> LocalApplications { get; set; } = Array.Empty<LocalResistanceApplication>();

            public IReadOnlyList<DuctApplication> DuctApplications { get; set; } = Array.Empty<DuctApplication>();

            public IReadOnlyList<UniqueLocalResistanceElement> UniqueRecommendedZetaElements { get; set; } = Array.Empty<UniqueLocalResistanceElement>();

            public IReadOnlyList<UniqueLocalResistanceElement> UniqueCommentZetaElements { get; set; } = Array.Empty<UniqueLocalResistanceElement>();

            public IReadOnlyList<UniqueLocalResistanceElement> MissingZetaElements { get; set; } = Array.Empty<UniqueLocalResistanceElement>();

            public LocalResistanceApplication? TopLocalResistanceContribution { get; set; }

            public DuctApplication? TopFrictionContribution { get; set; }

            public DuctApplication? MaxVelocityDuct { get; set; }

            public DuctApplication? MinVelocityDuct { get; set; }
        }

        private sealed class LocalResistanceApplication
        {
            public LocalResistanceApplication(int pathIndex, LocalResistanceCalculationInfo local)
            {
                PathIndex = pathIndex;
                Local = local;
            }

            public int PathIndex { get; }

            public LocalResistanceCalculationInfo Local { get; }
        }

        private sealed class DuctApplication
        {
            public DuctApplication(int pathIndex, DuctCalculationInfo duct)
            {
                PathIndex = pathIndex;
                Duct = duct;
            }

            public int PathIndex { get; }

            public DuctCalculationInfo Duct { get; }
        }

        private sealed class UniqueLocalResistanceElement
        {
            public long ElementId { get; set; }

            public string TypeName { get; set; } = string.Empty;

            public string FamilyName { get; set; } = string.Empty;

            public string Size { get; set; } = string.Empty;

            public double Zeta { get; set; }

            public string Source { get; set; } = string.Empty;

            public int ApplicationsCount { get; set; }

            public double MaxLocalPressureLossPa { get; set; }

            public List<int> PathIndexes { get; set; } = new List<int>();
        }
    }
}
