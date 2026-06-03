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
            string text = BuildTextReport(viewModel, DateTime.Now);
            File.WriteAllText(txtPath, text, Encoding.UTF8);
            File.WriteAllText(jsonPath, BuildJsonReport(viewModel, DateTime.Now), Encoding.UTF8);

            return new VentCalcDiagnosticReportResult
            {
                TxtPath = txtPath,
                JsonPath = jsonPath,
                PreviewText = text.Length > 6000 ? text.Substring(0, 6000) + Environment.NewLine + "..." : text
            };
        }

        private static string BuildTextReport(VentCalcCenterViewModel viewModel, DateTime createdAt)
        {
            var builder = new StringBuilder();
            PathCalculationInfo? criticalPath = viewModel.CriticalPath;
            IReadOnlyList<DuctCalculationInfo> allDucts = GetAllDucts(viewModel).ToList();
            IReadOnlyList<LocalResistanceCalculationInfo> allLocalResistances = GetAllLocalResistances(viewModel).ToList();
            DuctCalculationInfo? maxVelocityDuct = allDucts.OrderByDescending(duct => duct.VelocityMs).FirstOrDefault();
            DuctCalculationInfo? minVelocityDuct = allDucts.Where(duct => duct.VelocityMs > 0).OrderBy(duct => duct.VelocityMs).FirstOrDefault();
            DuctCalculationInfo? maxFrictionDuct = allDucts.OrderByDescending(duct => duct.FrictionPressureLossPa).FirstOrDefault();
            LocalResistanceCalculationInfo? maxLocal = allLocalResistances.OrderByDescending(local => local.LocalPressureLossPa).FirstOrDefault();
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
            builder.AppendLine($"Самая большая скорость: {(maxVelocityDuct?.VelocityMs ?? 0):0.###} м/с (ElementId {maxVelocityDuct?.ElementId.ToString(CultureInfo.InvariantCulture) ?? "—"})");
            builder.AppendLine($"Самая маленькая скорость: {(minVelocityDuct?.VelocityMs ?? 0):0.###} м/с (ElementId {minVelocityDuct?.ElementId.ToString(CultureInfo.InvariantCulture) ?? "—"})");
            builder.AppendLine($"Самый большой вклад по МС: {(maxLocal?.LocalPressureLossPa ?? 0):0.###} Па (ElementId {maxLocal?.ElementId.ToString(CultureInfo.InvariantCulture) ?? "—"})");
            builder.AppendLine($"Самый большой вклад по трению: {(maxFrictionDuct?.FrictionPressureLossPa ?? 0):0.###} Па (ElementId {maxFrictionDuct?.ElementId.ToString(CultureInfo.InvariantCulture) ?? "—"})");
            builder.AppendLine("Элементы без ζ: " + string.Join(", ", allLocalResistances.Where(local => local.Source == "Не найдено").Select(local => local.ElementId)));
            builder.AppendLine("Элементы с ζ из рекомендации: " + string.Join(", ", allLocalResistances.Where(local => local.Source == "Рекомендовано").Select(local => local.ElementId)));
            builder.AppendLine("Элементы с ζ из комментария: " + string.Join(", ", allLocalResistances.Where(local => local.Source == "Комментарии").Select(local => local.ElementId)));

            return builder.ToString();
        }

        private static string BuildJsonReport(VentCalcCenterViewModel viewModel, DateTime createdAt)
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
                    elementId = viewModel.SelectedElementInfo?.ElementId,
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
                    issue.ElementId,
                    issue.Category,
                    issue.Message,
                    issue.Recommendation
                }),
                paths = viewModel.Paths.Select(path => new
                {
                    pathIndex = path.PathIndex,
                    startElementId = path.StartElementId,
                    endElementId = path.EndElementId,
                    totalElementCount = path.TotalElementCount,
                    ductCount = path.DuctCount,
                    fittingCount = path.FittingCount,
                    terminalCount = path.TerminalCount,
                    totalDuctLengthM = path.TotalDuctLengthM,
                    flowM3h = path.FlowM3h,
                    frictionPressureLossPa = path.Calculation?.TotalFrictionPressureLossPa ?? 0,
                    localPressureLossPa = path.Calculation?.TotalLocalPressureLossPa ?? 0,
                    totalPressureLossPa = path.TotalPressureLossPa,
                    totalPressureLossWithReservePa = path.TotalPressureLossPa * (1.0 + viewModel.Settings.PressureReservePercent / 100.0),
                    elementIds = path.ElementIds
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
                allLocalResistances = GetAllLocalResistances(viewModel)
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

        private static IEnumerable<string> GetSystemPairs(VentCalcCenterViewModel viewModel)
        {
            return viewModel.NetworkInfo?.Elements
                .Select(node => $"{node.SystemName} / {node.SystemType}")
                .Where(value => !string.IsNullOrWhiteSpace(value) && value != "— / —")
                .Distinct()
                .OrderBy(value => value, StringComparer.Ordinal)
                ?? Enumerable.Empty<string>();
        }
    }
}
