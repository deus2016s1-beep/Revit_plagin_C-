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
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

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
                MissingZetaElements = BuildMissingLocalElements(localApplications),
                UnknownFittingElements = BuildUnknownFittingElements(localApplications),
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
            SelfCheckInfo selfCheck = BuildSelfCheck(viewModel, diagnostics);

            builder.AppendLine("VentCalc v2.0 — отчёт для проверки");
            builder.AppendLine(new string('=', 60));
            builder.AppendLine();
            builder.AppendLine("САМОПРОВЕРКА VENTCALC");
            builder.AppendLine($"Статус: {selfCheck.Status.ToUpperInvariant()}");
            builder.AppendLine($"Система: {viewModel.SystemName}");
            builder.AppendLine($"Трасс: {viewModel.PathCount}");
            builder.AppendLine($"Неопределённых МС: {selfCheck.UnknownFittingCount}");
            builder.AppendLine($"МС без ζ: {selfCheck.MissingZetaCount}");
            builder.AppendLine($"Зонтов без ζ: {selfCheck.HoodsWithoutZetaCount}");
            builder.AppendLine($"Ошибок согласованности: {selfCheck.StateConsistencyErrorCount}");
            builder.AppendLine($"Итог критической трассы: {selfCheck.CriticalPressureLossPa:0.###} Па");
            foreach (string error in selfCheck.Errors)
            {
                builder.AppendLine($"ERROR: {error}");
            }
            foreach (string warning in selfCheck.Warnings)
            {
                builder.AppendLine($"WARNING: {warning}");
            }
            builder.AppendLine();
            builder.AppendLine("ПОДСВЕТКА");
            builder.AppendLine($"Режим: {viewModel.HighlightState.ActiveMode}");
            builder.AppendLine($"Вид: {viewModel.HighlightState.ActiveViewId}");
            builder.AppendLine($"Запрошено: {viewModel.HighlightState.RequestedElementCount}");
            builder.AppendLine($"Подсвечено: {viewModel.HighlightState.HighlightedElementCount}");
            builder.AppendLine($"Пропущено: {viewModel.HighlightState.SkippedElementCount}");
            builder.AppendLine($"Ошибок: {viewModel.HighlightState.FailedElementCount}");
            builder.AppendLine($"Восстановлено: {viewModel.HighlightState.RestoredElementCount}");
            builder.AppendLine();
            builder.AppendLine("1. Общая информация");
            builder.AppendLine($"Дата/время: {createdAt:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine("Версия VentCalc: 2.0");
            builder.AppendLine($"Версия Revit: {viewModel.RevitVersion}");
            builder.AppendLine($"Файл Revit: {viewModel.RevitFilePath}");
            builder.AppendLine($"Выбранный ElementId: {viewModel.SelectedElementId}");
            builder.AppendLine($"Режим загрузки: {viewModel.LoadModeDisplay}");
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
            builder.AppendLine($"Компонентов системы: {viewModel.SystemComponentCount}");
            builder.AppendLine(systemPairs.Count <= 1
                ? $"Сеть содержит одну систему: {systemPairs.FirstOrDefault() ?? viewModel.SystemName}"
                : "WARNING: В найденной сети обнаружены элементы разных систем: " + string.Join("; ", systemPairs));
            AppendEndpointSelection(builder, viewModel.PathSummary);
            builder.AppendLine();

            builder.AppendLine("3. Настройки расчёта");
            builder.AppendLine($"Плотность, кг/м³: {viewModel.Settings.AirDensityKgM3:0.###}");
            builder.AppendLine($"Вязкость, Па·с: {viewModel.Settings.AirDynamicViscosityPaS:0.########E+0}");
            builder.AppendLine($"Шероховатость, мм: {viewModel.Settings.RoughnessMm:0.###}");
            builder.AppendLine($"Шероховатость, м: {viewModel.Settings.RoughnessM:0.########}");
            builder.AppendLine($"Запас давления, %: {viewModel.Settings.PressureReservePercent:0.###}");
            builder.AppendLine($"Min/Max/Critical скорость, м/с: {viewModel.Settings.MinVelocityMs:0.###} / {viewModel.Settings.MaxVelocityMs:0.###} / {viewModel.Settings.CriticalVelocityMs:0.###}");
            builder.AppendLine();
            builder.AppendLine("3а. Состояние редактора ζ");
            IReadOnlyList<LocalResistanceCalculationInfo> allLocalRows = GetAllLocalResistances(viewModel).ToList();
            builder.AppendLine($"Последнее действие: {viewModel.LastActionMessage}");
            builder.AppendLine($"Лог действий: {VentCalcActionLogService.GetLogPath()}");
            builder.AppendLine($"Всего строк МС: {allLocalRows.Count}; выбранных строк: {viewModel.SelectedLocalResistanceRows.Count}");
            builder.AppendLine($"Строк с ручными ζ: {allLocalRows.Count(row => row.ManualZeta.HasValue)}; из комментариев: {allLocalRows.Count(row => row.ZetaSource == "Комментарии")}; рекомендованных: {allLocalRows.Count(row => row.ZetaSource == "Рекомендовано" || row.ZetaSource == "Автоматически")}; без ζ: {allLocalRows.Count(row => row.ZetaSource == "Не определено")}");
            IReadOnlyList<string> stateConsistencyErrors = GetStateConsistencyErrors(allLocalRows);
            builder.AppendLine($"Сохранено переопределений: {viewModel.ZetaWriteActions.Count(action => action.WriteSucceeded)}; ошибок сохранения: {viewModel.ZetaWriteActions.Count(action => !action.WriteSucceeded)}; ошибок согласованности: {stateConsistencyErrors.Count}");
            foreach (string error in stateConsistencyErrors)
            {
                builder.AppendLine($"  ERROR: {error}");
            }
            builder.AppendLine("Каталог ζ проекта:");
            foreach (ProjectZetaCatalogRow row in viewModel.ProjectZetaCatalogRows)
            {
                builder.AppendLine($"  {row.PathRole}: Auto={row.AutoZeta:0.###}; Project={(row.ProjectZeta.HasValue ? row.ProjectZeta.Value.ToString("0.###", CultureInfo.InvariantCulture) : "—")}; Used={row.EffectiveProjectZeta:0.###}; Applications={row.ApplicationCount}; Systems={row.SystemCount}; Status={row.Status}");
            }
            builder.AppendLine("Возврат к Auto / массовые действия:");
            foreach (ZetaWriteActionInfo action in viewModel.ZetaWriteActions.Where(action => action.OverrideStorageType.Contains("Reset", StringComparison.OrdinalIgnoreCase) || action.OverrideStorageType == "ProjectCatalog").TakeLast(20))
            {
                builder.AppendLine($"  {action.Timestamp:yyyy-MM-dd HH:mm:ss}; ElementId={action.ElementId}; storage={action.OverrideStorageType}; verification={action.VerificationMode}; zetaTokenAfter={action.ZetaTokenExistsAfterCommit}; key={action.OverrideKey}; ok={action.WriteSucceeded}; error={action.ErrorMessage}; old='{action.OldComment}'; new='{action.NewComment}'");
            }
            builder.AppendLine("Последние внутренние действия:");
            foreach (string logEntry in VentCalcActionLogService.ReadLastEntries(20))
            {
                builder.AppendLine($"  {logEntry}");
            }
            builder.AppendLine("Строки с ручными ζ:");
            foreach (LocalResistanceCalculationInfo row in allLocalRows.Where(row => row.ManualZeta.HasValue))
            {
                builder.AppendLine($"  ElementId={row.ElementId}; трасса={row.PathIndex}; manualζ={row.ManualZeta:0.###}; effectiveζ={row.EffectiveZeta:0.###}; Z={row.LocalPressureLossPa:0.###} Па");
            }
            builder.AppendLine("Последние действия записи ζ:");
            foreach (ZetaWriteActionInfo action in viewModel.ZetaWriteActions.TakeLast(20))
            {
                builder.AppendLine($"  {action.Timestamp:yyyy-MM-dd HH:mm:ss}; ElementId={action.ElementId}; ζ={action.RequestedZeta:0.###}; ok={action.WriteSucceeded}; verified={action.VerifiedAfterCommit}; parameter={action.ParameterName}; error={action.ErrorMessage}; old='{action.OldComment}'; new='{action.NewComment}'; actual='{action.ActualCommentAfterCommit}'");
            }
            builder.AppendLine("Ошибки записи ζ:");
            foreach (ZetaWriteActionInfo action in viewModel.ZetaWriteActions.Where(action => !action.WriteSucceeded && !string.IsNullOrWhiteSpace(action.ErrorMessage)).TakeLast(20))
            {
                builder.AppendLine($"  ElementId={action.ElementId}; {action.ErrorMessage}");
            }
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
                builder.AppendLine($"Трасса №{path.PathIndex}: статус={path.CriticalStatus}; Δкрит={path.PressureLossDeltaFromCriticalPa:0.###} Па / {path.PressureLossDeltaFromCriticalPercent:0.##}%; Start={path.StartElementId}; End={path.EndElementId}; Элементов={path.TotalElementCount}; Воздуховодов={path.DuctCount}; Фитингов={path.FittingCount}; Длина={path.TotalDuctLengthM:0.###} м; Расход={path.FlowM3h}; Трение={(path.Calculation?.TotalFrictionPressureLossPa ?? 0):0.###} Па; МС={(path.Calculation?.TotalLocalPressureLossPa ?? 0):0.###} Па; Итого={path.TotalPressureLossPa:0.###} Па; Итого с запасом={totalWithReserve:0.###} Па");
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
            IReadOnlyList<PathRow> nearCriticalRows = viewModel.Paths.Where(path => path.IsNearCritical && !path.IsCritical).ToList();
            builder.AppendLine($"Почти критические трассы: {(nearCriticalRows.Count == 0 ? "—" : string.Join(", ", nearCriticalRows.Select(path => path.PathIndex)))}");
            builder.AppendLine();

            builder.AppendLine("7. Расчётные участки критической трассы");
            foreach (CalculationSectionInfo section in criticalPath?.Sections ?? Enumerable.Empty<CalculationSectionInfo>())
            {
                builder.AppendLine($"Участок {section.SectionIndex}; elements={section.ElementIdsText}; Start={section.StartElementId}; End={section.EndElementId}; размер={section.Size}; Q={section.FlowM3h:0.###} м³/ч; L={section.TotalLengthM:0.###} м; V={section.VelocityMs:0.###} м/с; R·l={section.FrictionPressureLossPa:0.###} Па; split={section.SplitReason}; warnings={section.WarningText}");
            }
            builder.AppendLine();

            builder.AppendLine("7а. Воздуховоды критической трассы");
            foreach (DuctCalculationInfo duct in criticalPath?.Ducts ?? Enumerable.Empty<DuctCalculationInfo>())
            {
                builder.AppendLine($"{duct.ElementId}; {duct.Size}; Q={duct.FlowM3h:0.###} м³/ч; L={duct.LengthM:0.###} м; A={duct.AreaM2:0.####} м²; Dэкв={duct.EquivalentDiameterM:0.####} м; V={duct.VelocityMs:0.###} м/с; Re={duct.Reynolds:0.#}; λ={duct.Lambda:0.####}; Pv={duct.DynamicPressurePa:0.###} Па; R={duct.SpecificPressureLossPaPerM:0.###} Па/м; R·l={duct.FrictionPressureLossPa:0.###} Па");
            }
            builder.AppendLine();

            builder.AppendLine("8. Местные сопротивления критической трассы");
            foreach (LocalResistanceCalculationInfo local in criticalPath?.LocalResistances ?? Enumerable.Empty<LocalResistanceCalculationInfo>())
            {
                builder.AppendLine($"{local.ElementId}; {local.TypeName}; {local.FamilyName}; {local.Size}; role={local.PathRole}; pathDependent={local.PathDependent}; storage={local.OverrideStorageType}; overrideKey={local.OverrideKey}; reason={local.RoleReason}; actualAngle={local.ActualAngleDeg:0.#}; roundedAngle={local.RoundedAngleDeg:0.#}; angleRounded={local.AngleWasRounded}; angleWarning={local.AngleRoundingWarning}; prev={local.PreviousDuctElementId}; next={local.NextDuctElementId}; autoζ={local.AutoZeta:0.###}; manualζ={(local.ManualZeta.HasValue ? local.ManualZeta.Value.ToString("0.###", CultureInfo.InvariantCulture) : "—")}; effectiveζ={local.EffectiveZeta:0.###}; источник={local.ZetaSource}; zetaComment={local.ZetaComment}; written={local.WasWrittenToRevitComment}; savedStorage={local.WasSavedToVentCalcStorage}; V={local.VelocityMs:0.###} м/с; Pv={local.DynamicPressurePa:0.###} Па; Z={local.LocalPressureLossPa:0.###} Па; warnings={local.WarningText}");
            }
            builder.AppendLine();

            builder.AppendLine("9. Проверочные значения");
            AppendTopLocalContribution(builder, diagnostics.TopLocalResistanceContribution);
            AppendTopFrictionContribution(builder, diagnostics.TopFrictionContribution);
            builder.AppendLine($"Самая большая скорость: {(diagnostics.MaxVelocityDuct?.Duct.VelocityMs ?? 0):0.###} м/с (ElementId {diagnostics.MaxVelocityDuct?.Duct.ElementId.ToString(CultureInfo.InvariantCulture) ?? "—"}, трасса №{diagnostics.MaxVelocityDuct?.PathIndex.ToString(CultureInfo.InvariantCulture) ?? "—"})");
            builder.AppendLine($"Самая маленькая скорость: {(diagnostics.MinVelocityDuct?.Duct.VelocityMs ?? 0):0.###} м/с (ElementId {diagnostics.MinVelocityDuct?.Duct.ElementId.ToString(CultureInfo.InvariantCulture) ?? "—"}, трасса №{diagnostics.MinVelocityDuct?.PathIndex.ToString(CultureInfo.InvariantCulture) ?? "—"})");
            AppendUniqueLocalGroup(builder, "Уникальные элементы с ζ из рекомендации", diagnostics.UniqueRecommendedZetaElements);
            int recommendedApplicationCount = diagnostics.LocalApplications.Count(item => item.Local.Source == "Рекомендовано");
            int commentApplicationCount = diagnostics.LocalApplications.Count(item => item.Local.Source == "Комментарии");
            int teePassCount = diagnostics.LocalApplications.Count(item => item.Local.PathRole == "TeePass");
            int teeBranchCount = diagnostics.LocalApplications.Count(item => item.Local.PathRole == "TeeBranch");
            int transitionNarrowingCount = diagnostics.LocalApplications.Count(item => item.Local.PathRole == "TransitionNarrowing");
            int transitionExpansionCount = diagnostics.LocalApplications.Count(item => item.Local.PathRole == "TransitionExpansion");
            int unknownFittingCount = diagnostics.LocalApplications.Count(item => string.Equals(item.Local.PathRole, "Unknown", StringComparison.OrdinalIgnoreCase) || item.Local.PathRole.EndsWith("Unknown", StringComparison.OrdinalIgnoreCase));
            builder.AppendLine($"Количество применений ζ из рекомендации по трассам: {recommendedApplicationCount}");
            AppendUniqueLocalGroup(builder, "Уникальные элементы с ζ из комментария", diagnostics.UniqueCommentZetaElements);
            builder.AppendLine($"Количество применений ζ из комментария по трассам: {commentApplicationCount}");
            AppendUniqueLocalGroup(builder, "Элементы без ζ", diagnostics.MissingZetaElements);
            builder.AppendLine($"Количество элементов без ζ: {diagnostics.MissingZetaElements.Count}");
            AppendUnknownFittingGroup(builder, diagnostics.UnknownFittingElements);
            builder.AppendLine($"TeePass: {teePassCount}; TeeBranch: {teeBranchCount}; TransitionNarrowing: {transitionNarrowingCount}; TransitionExpansion: {transitionExpansionCount}; Unknown фитингов: {unknownFittingCount}");
            builder.AppendLine($"Коротких воздуховодов, присоединённых к участкам: {GetAllSections(viewModel).Count(section => section.ContainsShortDucts)}; выделено отдельным участком: {GetAllSections(viewModel).Count(section => section.ContainsShortDucts && section.ElementIds.Count == 1)}");

            return builder.ToString();
        }

        private static void AppendEndpointSelection(StringBuilder builder, VentPathSummary? pathSummary)
        {
            if (pathSummary == null)
            {
                builder.AppendLine("Endpoint selection: —");
                return;
            }

            VentPathEndpointSelection selection = pathSummary.EndpointSelection;
            builder.AppendLine("Endpoint selection:");
            builder.AppendLine($"  detectedDirection: {pathSummary.Direction}");
            builder.AppendLine($"  directionReason: {pathSummary.DirectionReason}");
            builder.AppendLine($"  fallbackUsed: {selection.FallbackUsed}");
            builder.AppendLine($"  terminalElementStartCount: {selection.TerminalElementStartCount}");
            builder.AppendLine($"  connectorLevelStartCount: {selection.ConnectorLevelStartCount}");
            builder.AppendLine($"  detailedConnectorStartCount: {selection.DetailedConnectorStartCount}");
            builder.AppendLine($"  fallbackConnectorStartCount: {selection.FallbackConnectorStartCount}");
            builder.AppendLine($"  duplicateConnectorStartsRemoved: {selection.DuplicateConnectorStartsRemoved}");
            builder.AppendLine($"  finalConnectorLevelStartCount: {selection.ConnectorLevelStartCount}");
            builder.AppendLine($"  duplicatePathsRemoved: {selection.DuplicatePathsRemoved}");
            builder.AppendLine($"  pathsBuiltCount: {selection.PathsBuiltCount}");
            builder.AppendLine($"  connectorStartsWithoutPathCount: {selection.ConnectorStartsWithoutPathCount}");
            AppendEndpointCandidates(builder, "  startCandidates", selection.StartCandidates);
            AppendEndpointCandidates(builder, "  endCandidates", selection.EndCandidates);
            AppendEndpointCandidates(builder, "  hoodCandidates", selection.HoodCandidates);
            AppendEndpointCandidates(builder, "  fanCandidates", selection.FanCandidates);
            AppendEndpointCandidates(builder, "  openEndCandidates", selection.OpenEndCandidates);
            AppendEndpointCandidates(builder, "  rejectedCandidates", selection.RejectedCandidates);
        }

        private static void AppendEndpointCandidates(StringBuilder builder, string title, IReadOnlyList<VentEndpointCandidateInfo> candidates)
        {
            builder.AppendLine($"{title}: {candidates.Count}");
            foreach (VentEndpointCandidateInfo candidate in candidates)
            {
                builder.AppendLine($"    ElementId={candidate.ElementId}; category={candidate.Category}; family={candidate.FamilyName}; type={candidate.TypeName}; role={candidate.Role}; connectors={candidate.ConnectorCount}; connected={candidate.ConnectedHvacConnectorCount}; degree={candidate.GraphDegree}; reason={candidate.Reason}");
            }
        }

        private static string BuildJsonReport(VentCalcCenterViewModel viewModel, DateTime createdAt, ReportDiagnostics diagnostics)
        {
            PathCalculationInfo? criticalPath = viewModel.CriticalPath;
            SelfCheckInfo selfCheck = BuildSelfCheck(viewModel, diagnostics);
            var payload = new
            {
                selfCheck = selfCheck,
                buildInfo = new
                {
                    version = "2.0-preview",
                    commit = "unknown",
                    localResistanceUiVersion = "preview-1",
                    reportSchemaVersion = 2
                },
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
                    loadMode = viewModel.LoadModeDisplay,
                    selectedSystemName = viewModel.SelectedCatalogSystem?.SystemName ?? viewModel.SystemName,
                    selectedSystemType = viewModel.SelectedCatalogSystem?.SystemType ?? viewModel.SystemType,
                    componentCount = viewModel.SystemComponentCount,
                    systemPairs = GetSystemPairs(viewModel)
                },
                endpointSelection = viewModel.PathSummary == null ? null : new
                {
                    detectedDirection = viewModel.PathSummary.Direction,
                    directionReason = viewModel.PathSummary.DirectionReason,
                    startCandidates = viewModel.PathSummary.EndpointSelection.StartCandidates,
                    endCandidates = viewModel.PathSummary.EndpointSelection.EndCandidates,
                    rejectedCandidates = viewModel.PathSummary.EndpointSelection.RejectedCandidates,
                    openEndCandidates = viewModel.PathSummary.EndpointSelection.OpenEndCandidates,
                    hoodCandidates = viewModel.PathSummary.EndpointSelection.HoodCandidates,
                    fanCandidates = viewModel.PathSummary.EndpointSelection.FanCandidates,
                    fallbackUsed = viewModel.PathSummary.EndpointSelection.FallbackUsed,
                    terminalElementStartCount = viewModel.PathSummary.EndpointSelection.TerminalElementStartCount,
                    connectorLevelStartCount = viewModel.PathSummary.EndpointSelection.ConnectorLevelStartCount,
                    detailedConnectorStartCount = viewModel.PathSummary.EndpointSelection.DetailedConnectorStartCount,
                    fallbackConnectorStartCount = viewModel.PathSummary.EndpointSelection.FallbackConnectorStartCount,
                    duplicateConnectorStartsRemoved = viewModel.PathSummary.EndpointSelection.DuplicateConnectorStartsRemoved,
                    finalConnectorLevelStartCount = viewModel.PathSummary.EndpointSelection.ConnectorLevelStartCount,
                    duplicatePathsRemoved = viewModel.PathSummary.EndpointSelection.DuplicatePathsRemoved,
                    pathsBuiltCount = viewModel.PathSummary.EndpointSelection.PathsBuiltCount,
                    connectorStartsWithoutPathCount = viewModel.PathSummary.EndpointSelection.ConnectorStartsWithoutPathCount,
                    connectorStarts = viewModel.PathSummary.EndpointSelection.ConnectorStarts,
                    noPathReason = viewModel.PathSummary.NoPathReason,
                    warnings = viewModel.PathSummary.Warnings
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
                    criticalVelocityMs = viewModel.Settings.CriticalVelocityMs,
                    lowVelocityColorHex = viewModel.Settings.LowVelocityColorHex,
                    normalVelocityColorHex = viewModel.Settings.NormalVelocityColorHex,
                    highVelocityColorHex = viewModel.Settings.HighVelocityColorHex,
                    criticalVelocityColorHex = viewModel.Settings.CriticalVelocityColorHex,
                    selectedPathColorHex = viewModel.Settings.SelectedPathColorHex,
                    criticalPathColorHex = viewModel.Settings.CriticalPathColorHex,
                    issueColorHex = viewModel.Settings.IssueColorHex,
                    lowPressureLossColorHex = viewModel.Settings.LowPressureLossColorHex,
                    mediumPressureLossColorHex = viewModel.Settings.MediumPressureLossColorHex,
                    highPressureLossColorHex = viewModel.Settings.HighPressureLossColorHex,
                    maxPressureLossColorHex = viewModel.Settings.MaxPressureLossColorHex,
                    settingsSchemaVersion = viewModel.Settings.SettingsSchemaVersion,
                    selectedSettingsSection = viewModel.SelectedSettingsSection,
                    uiDefaultTabAfterLoad = viewModel.Settings.UiDefaultTabAfterLoad,
                    rememberWindowPlacement = viewModel.Settings.RememberWindowPlacement,
                    zoomToElementOnShow = viewModel.Settings.ZoomToElementOnShow,
                    confirmBulkZetaChanges = viewModel.Settings.ConfirmBulkZetaChanges
                },
                highlighting = new
                {
                    lastRibbonCommand = viewModel.HighlightState.WindowSource,
                    activeMode = viewModel.HighlightState.ActiveMode.ToString(),
                    windowSource = viewModel.HighlightState.WindowSource,
                    activeDisplayMode = viewModel.HighlightState.ActiveDisplayMode.ToString(),
                    systemNameAtApply = viewModel.HighlightState.SystemNameAtApply,
                    pathIndexAtApply = viewModel.HighlightState.PathIndexAtApply,
                    isCriticalPath = viewModel.HighlightState.IsCriticalPath,
                    highlightedPathElementCount = viewModel.HighlightState.HighlightedPathElementCount,
                    dimmedSystemElementCount = viewModel.HighlightState.DimmedSystemElementCount,
                    startElementId = viewModel.HighlightState.StartElementId,
                    endElementId = viewModel.HighlightState.EndElementId,
                    lastApplySucceeded = viewModel.HighlightState.LastApplySucceeded,
                    lastClearSucceeded = viewModel.HighlightState.LastClearSucceeded,
                    originalOverridesRestored = viewModel.HighlightState.OriginalOverridesRestored,
                    activeViewId = viewModel.HighlightState.ActiveViewId,
                    requestedElementCount = viewModel.HighlightState.RequestedElementCount,
                    highlightedElementCount = viewModel.HighlightState.HighlightedElementCount,
                    skippedElementCount = viewModel.HighlightState.SkippedElementCount,
                    failedElementCount = viewModel.HighlightState.FailedElementCount,
                    restoredElementCount = viewModel.HighlightState.RestoredElementCount,
                    snapshotCount = viewModel.HighlightState.SnapshotCount,
                    selectionChangedByVentCalc = viewModel.HighlightState.SelectionChangedByVentCalc,
                    selectionElementCountBefore = viewModel.HighlightState.SelectionElementCountBefore,
                    selectionElementCountAfter = viewModel.HighlightState.SelectionElementCountAfter,
                    showElementsUsed = viewModel.HighlightState.ShowElementsUsed,
                    velocityGroups = new
                    {
                        belowMin = viewModel.HighlightState.VelocityGroups.BelowMin,
                        normal = viewModel.HighlightState.VelocityGroups.Normal,
                        aboveMax = viewModel.HighlightState.VelocityGroups.AboveMax,
                        critical = viewModel.HighlightState.VelocityGroups.Critical,
                        notCalculated = viewModel.HighlightState.VelocityGroups.NotCalculated
                    },
                    pressureLossGroups = new
                    {
                        low = viewModel.HighlightState.PressureLossGroups.Low,
                        medium = viewModel.HighlightState.PressureLossGroups.Medium,
                        high = viewModel.HighlightState.PressureLossGroups.High,
                        maximum = viewModel.HighlightState.PressureLossGroups.Maximum,
                        zeroOrSkipped = viewModel.HighlightState.PressureLossGroups.ZeroOrSkipped
                    },
                    maxElementPressureLossPa = viewModel.HighlightState.MaxElementPressureLossPa,
                    issueElementCount = viewModel.HighlightState.IssueElementCount,
                    errors = viewModel.HighlightState.Errors
                },
                uiState = new
                {
                    selectedTab = "—",
                    selectedSystemName = viewModel.SelectedCatalogSystem?.SystemName,
                    loadedSystemName = viewModel.SystemName,
                    selectedElementId = ToLongOrNull(viewModel.SelectedElementId),
                    selectedPathIndex = viewModel.SelectedPath?.PathIndex,
                    selectedLocalResistanceElementIds = viewModel.SelectedLocalResistanceRows.Select(row => row.ElementId).ToList(),
                    manualZetaRowsCount = GetAllLocalResistances(viewModel).Count(row => row.ManualZeta.HasValue),
                    savedOverrideCount = viewModel.ZetaWriteActions.Count(action => action.WriteSucceeded),
                    failedOverrideCount = viewModel.ZetaWriteActions.Count(action => !action.WriteSucceeded),
                    lastActionMessage = viewModel.LastActionMessage,
                    actionLogPath = VentCalcActionLogService.GetLogPath(),
                    lastInternalLogEntries = VentCalcActionLogService.ReadLastEntries(50)
                },
                zetaEditorState = new
                {
                    totalRows = GetAllLocalResistances(viewModel).Count(),
                    editableManualZetaRows = GetAllLocalResistances(viewModel).Count(row => row.CanEditManualZeta),
                    rowsWithSessionManualZeta = GetAllLocalResistances(viewModel).Count(row => row.ManualZeta.HasValue),
                    rowsWithPathOverrides = GetAllLocalResistances(viewModel).Count(row => row.ZetaSource == "Переопределение VentCalc" || row.ZetaSource == "Переопределение трассы"),
                    rowsWithCommentZeta = GetAllLocalResistances(viewModel).Count(row => row.ZetaSource == "Комментарии" || row.ZetaSource == "Комментарии элемента"),
                    rowsWithProjectCatalogZeta = GetAllLocalResistances(viewModel).Count(row => row.ZetaSource == "Каталог проекта"),
                    rowsWithFamilyTypeZeta = GetAllLocalResistances(viewModel).Count(row => row.ZetaSource == "Семейство/тип"),
                    rowsWithAutoZeta = GetAllLocalResistances(viewModel).Count(row => row.ZetaSource == "Auto" || row.ZetaSource == "Автоматически" || row.ZetaSource == "Рекомендовано"),
                    rowsWithMissingZeta = GetAllLocalResistances(viewModel).Count(row => row.ZetaSource == "Не определено"),
                    canWriteSelectedRows = viewModel.SelectedLocalResistanceRows.Any(row => row.CanWriteComment),
                    selectedRowsCount = viewModel.SelectedLocalResistanceRows.Count
                },
                projectZetaCatalog = viewModel.ProjectZetaCatalogRows.Select(row => new
                {
                    pathRole = row.PathRole,
                    autoZeta = row.AutoZeta,
                    projectZeta = row.ProjectZeta,
                    effectiveProjectZeta = row.EffectiveProjectZeta,
                    applicationCount = row.ApplicationCount,
                    systemCount = row.SystemCount,
                    status = row.Status
                }),
                projectZetaCatalogActions = viewModel.ZetaWriteActions.Where(action => action.OverrideStorageType == "ProjectCatalog"),
                bulkZetaActions = Array.Empty<object>(),
                resetToAutoActions = viewModel.ZetaWriteActions.Where(action => action.OverrideStorageType == "CommentReset" || action.OverrideStorageType == "DataStorageReset" || action.OverrideStorageType == "ProjectReset"),
                resetProjectActions = viewModel.ZetaWriteActions.Where(action => action.OverrideStorageType == "ProjectReset"),
                sourceCounts = GetAllLocalResistances(viewModel).GroupBy(row => row.ZetaSource).ToDictionary(group => group.Key, group => group.Count()),
                affectedSystems = viewModel.NetworkInfo?.Elements.Select(element => element.SystemName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToList() ?? new List<string>(),
                affectedElementsCount = GetAllLocalResistances(viewModel).Select(row => row.ElementId).Distinct().Count(),
                lastInternalLogEntries = VentCalcActionLogService.ReadLastEntries(50),
                zetaWriteActions = viewModel.ZetaWriteActions.Select(action => new
                {
                    timestamp = action.Timestamp,
                    elementId = action.ElementId,
                    pathIndex = action.PathIndex,
                    oldComment = action.OldComment,
                    newComment = action.NewComment,
                    requestedZeta = action.RequestedZeta,
                    storageType = action.OverrideStorageType,
                    verificationMode = action.VerificationMode.ToString(),
                    zetaTokenExistsAfterCommit = action.ZetaTokenExistsAfterCommit,
                    overrideKey = action.OverrideKey,
                    pathDependent = action.PathDependent,
                    parameterFound = action.ParameterFound,
                    parameterName = action.ParameterName,
                    parameterIsReadOnly = action.ParameterIsReadOnly,
                    parameterStorageType = action.StorageType,
                    writeSucceeded = action.WriteSucceeded,
                    verifiedAfterCommit = action.VerifiedAfterCommit,
                    actualCommentAfterCommit = action.ActualCommentAfterCommit,
                    effectiveZetaAfterReload = action.EffectiveZetaAfterReload,
                    sourceAfterReload = action.SourceAfterReload,
                    errorMessage = action.ErrorMessage
                }),
                zetaRecalculationActions = viewModel.ZetaRecalculationActions.Select(action => new
                {
                    timestamp = action.Timestamp,
                    changedRowsCount = action.ChangedRowsCount,
                    previousCriticalPathIndex = action.PreviousCriticalPathIndex,
                    newCriticalPathIndex = action.NewCriticalPathIndex,
                    previousCriticalPressurePa = action.PreviousCriticalPressurePa,
                    newCriticalPressurePa = action.NewCriticalPressurePa
                }),
                localResistanceRows = GetLocalApplications(viewModel).Select(item => new
                {
                    elementId = item.Local.ElementId,
                    pathIndex = item.PathIndex,
                    pathRole = item.Local.PathRole,
                    autoZeta = item.Local.AutoZeta,
                    manualZeta = item.Local.ManualZeta,
                    effectiveZeta = item.Local.EffectiveZeta,
                    zetaSource = item.Local.ZetaSource,
                    zetaComment = item.Local.ZetaComment,
                    actualAngleDeg = item.Local.ActualAngleDeg,
                    roundedAngleDeg = item.Local.RoundedAngleDeg,
                    angleWasRounded = item.Local.AngleWasRounded,
                    angleRoundingWarning = item.Local.AngleRoundingWarning,
                    storageType = item.Local.OverrideStorageType,
                    overrideKey = item.Local.OverrideKey,
                    pathDependent = item.Local.PathDependent,
                    canEditManualZeta = item.Local.CanEditManualZeta,
                    canWriteComment = item.Local.CanWriteComment,
                    wasWrittenToRevitComment = item.Local.WasWrittenToRevitComment,
                    lastWriteError = item.Local.LastWriteError,
                    effectiveZetaAfterReload = item.Local.EffectiveZeta,
                    sourceAfterReload = item.Local.ZetaSource,
                    validationMessage = item.Local.ValidationMessage
                }),
                stateConsistencyErrors = GetStateConsistencyErrors(GetAllLocalResistances(viewModel).ToList()),
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
                    startConnectorKey = path.Path.StartConnectorKey,
                    connectedStartElementId = ToLongOrNull(path.Path.ConnectedStartElementId),
                    startFlowM3h = ParseFlowM3h(path.Path.StartFlowM3h),
                    endFlowM3h = ParseFlowM3h(path.Path.EndFlowM3h),
                    maxFlowM3h = ParseFlowM3h(path.Path.MaxFlowM3h),
                    flowRangeText = path.Path.FlowRangeM3h,
                    frictionPressureLossPa = path.Calculation?.TotalFrictionPressureLossPa ?? 0,
                    localPressureLossPa = path.Calculation?.TotalLocalPressureLossPa ?? 0,
                    totalPressureLossPa = path.TotalPressureLossPa,
                    totalPressureLossWithReservePa = path.TotalPressureLossPa * (1.0 + viewModel.Settings.PressureReservePercent / 100.0),
                    isCritical = path.IsCritical,
                    isNearCritical = path.IsNearCritical,
                    criticalStatus = path.CriticalStatus,
                    pressureLossDeltaFromCriticalPa = path.PressureLossDeltaFromCriticalPa,
                    pressureLossDeltaFromCriticalPercent = path.PressureLossDeltaFromCriticalPercent,
                    elementIds = ToElementIdList(path.ElementIds),
                    sections = path.Calculation?.Sections ?? Enumerable.Empty<CalculationSectionInfo>()
                }),
                nearCriticalPaths = viewModel.Paths.Where(path => path.IsNearCritical).Select(path => new
                {
                    pathIndex = path.PathIndex,
                    totalPressureLossPa = path.TotalPressureLossPa,
                    pressureLossDeltaFromCriticalPa = path.PressureLossDeltaFromCriticalPa,
                    pressureLossDeltaFromCriticalPercent = path.PressureLossDeltaFromCriticalPercent,
                    elementIds = ToElementIdList(path.ElementIds)
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
                criticalPathSections = criticalPath?.Sections ?? Enumerable.Empty<CalculationSectionInfo>(),
                sectionSplitReasons = GetAllSections(viewModel).Select(section => new { section.PathIndex, section.SectionIndex, section.SplitReason }).ToList(),
                shortDuctWarnings = GetAllSections(viewModel).Where(section => section.ContainsShortDucts || section.Warnings.Any(w => w.IndexOf("Корот", StringComparison.OrdinalIgnoreCase) >= 0)).Select(section => new { section.PathIndex, section.SectionIndex, section.ElementIds, section.Warnings }).ToList(),
                criticalPathDucts = criticalPath?.Ducts ?? Enumerable.Empty<DuctCalculationInfo>(),
                criticalPathLocalResistances = criticalPath?.LocalResistances ?? Enumerable.Empty<LocalResistanceCalculationInfo>(),
                allDucts = GetAllDucts(viewModel),
                sections = GetAllSections(viewModel),
                allLocalResistances = GetAllLocalResistances(viewModel),
                uniqueRecommendedZetaElements = diagnostics.UniqueRecommendedZetaElements,
                uniqueCommentZetaElements = diagnostics.UniqueCommentZetaElements,
                missingZetaElements = diagnostics.MissingZetaElements,
                unknownFittingElements = diagnostics.UnknownFittingElements,
                localResistanceApplicationsCount = diagnostics.LocalApplications.Count,
                recommendedZetaApplicationsCount = diagnostics.LocalApplications.Count(item => item.Local.Source == "Рекомендовано"),
                commentZetaApplicationsCount = diagnostics.LocalApplications.Count(item => item.Local.Source == "Комментарии"),
                missingZetaApplicationsCount = diagnostics.LocalApplications.Count(item => HasMissingZeta(item.Local)),
                teePassCount = diagnostics.LocalApplications.Count(item => item.Local.PathRole == "TeePass"),
                teeBranchCount = diagnostics.LocalApplications.Count(item => item.Local.PathRole == "TeeBranch"),
                transitionNarrowingCount = diagnostics.LocalApplications.Count(item => item.Local.PathRole == "TransitionNarrowing"),
                transitionExpansionCount = diagnostics.LocalApplications.Count(item => item.Local.PathRole == "TransitionExpansion"),
                unknownFittingCount = diagnostics.LocalApplications.Where(item => IsUnknownLocalResistanceRole(item.Local)).Select(item => item.Local.ElementId).Distinct().Count(),
                hoodsWithoutZetaCount = diagnostics.LocalApplications.Where(item => string.Equals(item.Local.PathRole, "Hood", StringComparison.OrdinalIgnoreCase) && HasMissingZeta(item.Local)).Select(item => item.Local.ElementId).Distinct().Count(),
                shortDuctsAttachedCount = GetAllSections(viewModel).Count(section => section.ContainsShortDucts),
                shortDuctsStandaloneCount = GetAllSections(viewModel).Count(section => section.ContainsShortDucts && section.ElementIds.Count == 1),
                topLocalResistanceContribution = ToJsonTopLocal(diagnostics.TopLocalResistanceContribution),
                topFrictionContribution = ToJsonTopFriction(diagnostics.TopFrictionContribution)
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }


        private static SelfCheckInfo BuildSelfCheck(VentCalcCenterViewModel viewModel, ReportDiagnostics diagnostics)
        {
            IReadOnlyList<LocalResistanceCalculationInfo> allLocals = GetAllLocalResistances(viewModel).ToList();
            IReadOnlyList<string> consistencyErrors = GetStateConsistencyErrors(allLocals);
            bool calculationHasNaN = HasInvalidNumber(viewModel, double.IsNaN);
            bool calculationHasInfinity = HasInvalidNumber(viewModel, double.IsInfinity);
            int unknownCount = diagnostics.LocalApplications.Where(item => IsUnknownLocalResistanceRole(item.Local)).Select(item => item.Local.ElementId).Distinct().Count();
            int missingZetaCount = diagnostics.LocalApplications.Where(item => HasMissingZeta(item.Local)).Select(item => item.Local.ElementId).Distinct().Count();
            int hoodsWithoutZetaCount = diagnostics.LocalApplications.Where(item => string.Equals(item.Local.PathRole, "Hood", StringComparison.OrdinalIgnoreCase) && HasMissingZeta(item.Local)).Select(item => item.Local.ElementId).Distinct().Count();
            bool resetVerified = viewModel.ZetaWriteActions
                .Where(action => action.OverrideStorageType.Contains("Reset", StringComparison.OrdinalIgnoreCase))
                .All(action => action.VerifiedAfterCommit || action.WriteSucceeded);
            bool catalogWriteSucceeded = viewModel.ZetaWriteActions
                .Where(action => action.OverrideStorageType == "ProjectCatalog")
                .All(action => action.WriteSucceeded);

            var warnings = new List<string>();
            var errors = new List<string>();
            if (unknownCount > 0) warnings.Add($"Неопределённых фитингов: {unknownCount}.");
            if (missingZetaCount > 0) warnings.Add($"МС без ζ: {missingZetaCount}.");
            AddEndpointTopologyMessages(viewModel, warnings, errors);
            if (consistencyErrors.Count > 0) errors.AddRange(consistencyErrors);
            if (!viewModel.Paths.Any()) errors.Add(string.IsNullOrWhiteSpace(viewModel.PathSummary?.NoPathReason) ? "Трассы не построены." : viewModel.PathSummary.NoPathReason);
            if (viewModel.CriticalPath == null) errors.Add("Критическая трасса не найдена.");
            if (calculationHasNaN) errors.Add("В расчёте есть NaN.");
            if (calculationHasInfinity) errors.Add("В расчёте есть Infinity.");
            if (viewModel.HighlightState.FailedElementCount > 0) errors.Add($"Ошибок подсветки: {viewModel.HighlightState.FailedElementCount}.");

            string status = errors.Count > 0 ? "Failed" : warnings.Count > 0 ? "Warning" : "Passed";
            return new SelfCheckInfo
            {
                Status = status,
                SystemLoaded = viewModel.NetworkInfo != null,
                PathsBuilt = viewModel.Paths.Any(),
                CriticalPathFound = viewModel.CriticalPath != null,
                UnknownFittingCount = unknownCount,
                MissingZetaCount = missingZetaCount,
                HoodsWithoutZetaCount = hoodsWithoutZetaCount,
                StateConsistencyErrorCount = consistencyErrors.Count,
                ResetToAutoVerified = resetVerified,
                ProjectCatalogReadSucceeded = viewModel.ProjectZetaCatalogRows.Count > 0,
                ProjectCatalogWriteSucceeded = catalogWriteSucceeded,
                PathIndependentValuesConsistent = !consistencyErrors.Any(error => error.StartsWith("Path-independent", StringComparison.OrdinalIgnoreCase)),
                PathDependentValuesConsistent = !consistencyErrors.Any(error => error.StartsWith("Path-dependent", StringComparison.OrdinalIgnoreCase)),
                CalculationHasNaN = calculationHasNaN,
                CalculationHasInfinity = calculationHasInfinity,
                CriticalPressureLossPa = viewModel.CriticalPath?.TotalPressureLossPa ?? 0,
                HighlightApplySucceeded = viewModel.HighlightState.HighlightApplySucceeded,
                HighlightClearSucceeded = viewModel.HighlightState.HighlightClearSucceeded,
                OriginalOverridesRestored = viewModel.HighlightState.OriginalOverridesRestored,
                HighlightFailedElementCount = viewModel.HighlightState.FailedElementCount,
                Warnings = warnings,
                Errors = errors
            };
        }

        private static void AddEndpointTopologyMessages(VentCalcCenterViewModel viewModel, ICollection<string> warnings, ICollection<string> errors)
        {
            VentPathEndpointSelection? selection = viewModel.PathSummary?.EndpointSelection;
            if (selection == null)
            {
                return;
            }

            int connectedStartSideConnectorCount = selection.StartCandidates.Sum(candidate => candidate.ConnectedHvacConnectorCount);
            if (selection.ConnectorLevelStartCount > connectedStartSideConnectorCount && connectedStartSideConnectorCount > 0)
            {
                errors.Add($"connectorLevelStartCount={selection.ConnectorLevelStartCount} больше суммы connectedHvacConnectorCount стартовых элементов={connectedStartSideConnectorCount}.");
            }

            if (selection.DuplicatePathsRemoved > 0)
            {
                warnings.Add($"Удалено дублей трасс: {selection.DuplicatePathsRemoved}.");
            }

            if (selection.ConnectorStartsWithoutPathCount > 0)
            {
                warnings.Add($"Connector-level стартов без трассы: {selection.ConnectorStartsWithoutPathCount}.");
            }

            if (selection.ConnectorStarts.Count > 0 && selection.PathsBuiltCount == 0)
            {
                errors.Add("Connector-level старты найдены, но трассы от них не построены.");
            }

            foreach (VentConnectorEndpointStartInfo start in selection.ConnectorStarts.Where(start => !start.PathFound && string.IsNullOrWhiteSpace(start.RejectionReason)))
            {
                warnings.Add($"Connector-level старт {start.LogicalKey} не имеет трассы и причины отклонения.");
            }

            foreach (PathRow path in viewModel.Paths)
            {
                string? first = path.ElementIds.FirstOrDefault();
                if (first != null && path.ElementIds.Skip(1).Any(id => id == first))
                {
                    errors.Add($"Трасса {path.PathIndex} повторно посещает стартовое терминальное оборудование {first}.");
                }
            }
        }

        private static bool HasInvalidNumber(VentCalcCenterViewModel viewModel, Func<double, bool> predicate)
        {
            return viewModel.AerodynamicSummary?.Paths.Any(path =>
                predicate(path.TotalPressureLossPa)
                || predicate(path.TotalFrictionPressureLossPa)
                || predicate(path.TotalLocalPressureLossPa)
                || path.Ducts.Any(duct => predicate(duct.VelocityMs) || predicate(duct.FrictionPressureLossPa))
                || path.LocalResistances.Any(local => predicate(local.EffectiveZeta) || predicate(local.LocalPressureLossPa))) == true;
        }

        private static IEnumerable<DuctCalculationInfo> GetAllDucts(VentCalcCenterViewModel viewModel)
        {
            return viewModel.AerodynamicSummary?.Paths.SelectMany(path => path.Ducts) ?? Enumerable.Empty<DuctCalculationInfo>();
        }

        private static IEnumerable<CalculationSectionInfo> GetAllSections(VentCalcCenterViewModel viewModel)
        {
            return viewModel.AerodynamicSummary?.Paths.SelectMany(path => path.Sections) ?? Enumerable.Empty<CalculationSectionInfo>();
        }

        private static IReadOnlyList<string> GetStateConsistencyErrors(IReadOnlyList<LocalResistanceCalculationInfo> localRows)
        {
            var errors = new List<string>();
            errors.AddRange(localRows
                .Where(row => !row.PathDependent)
                .GroupBy(row => row.ElementId)
                .Where(group => group.Select(row => Math.Round(row.EffectiveZeta, 4)).Distinct().Count() > 1)
                .Select(group => $"Path-independent ElementId {group.Key} имеет разные EffectiveZeta: {string.Join(", ", group.Select(row => $"path {row.PathIndex}: {row.EffectiveZeta:0.###}"))}"));

            errors.AddRange(localRows
                .Where(row => row.PathDependent && !string.IsNullOrWhiteSpace(row.OverrideKey))
                .GroupBy(row => row.OverrideKey, StringComparer.Ordinal)
                .Where(group => group.Select(row => Math.Round(row.EffectiveZeta, 4)).Distinct().Count() > 1)
                .Select(group => $"Path-dependent overrideKey {group.Key} имеет разные EffectiveZeta: {string.Join(", ", group.Select(row => $"path {row.PathIndex}: {row.EffectiveZeta:0.###}"))}"));

            return errors;
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

        private static IReadOnlyList<UniqueLocalResistanceElement> BuildMissingLocalElements(IEnumerable<LocalResistanceApplication> applications)
        {
            return BuildUniqueLocalElements(applications.Where(item => HasMissingZeta(item.Local)), null);
        }

        private static IReadOnlyList<UniqueLocalResistanceElement> BuildUniqueLocalElements(IEnumerable<LocalResistanceApplication> applications, string? source)
        {
            return applications
                .Where(item => source == null || string.Equals(item.Local.Source, source, StringComparison.Ordinal))
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

        private static IReadOnlyList<UnknownFittingElement> BuildUnknownFittingElements(IEnumerable<LocalResistanceApplication> applications)
        {
            return applications
                .Where(item => IsUnknownLocalResistanceRole(item.Local))
                .GroupBy(item => item.Local.ElementId)
                .Select(group =>
                {
                    LocalResistanceCalculationInfo first = group.First().Local;
                    return new UnknownFittingElement
                    {
                        ElementId = first.ElementId,
                        FamilyName = first.FamilyName,
                        TypeName = first.TypeName,
                        ApplicationCount = group.Count(),
                        PathIndexes = group.Select(item => item.PathIndex).Distinct().OrderBy(index => index).ToList(),
                        WarningText = string.IsNullOrWhiteSpace(first.WarningText)
                            ? "Роль фитинга не определена; ζ не применяется автоматически."
                            : first.WarningText
                    };
                })
                .OrderBy(item => item.ElementId)
                .ToList();
        }

        private static bool IsUnknownLocalResistanceRole(LocalResistanceCalculationInfo local)
        {
            return string.Equals(local.PathRole, "Unknown", StringComparison.OrdinalIgnoreCase)
                || local.PathRole?.EndsWith("Unknown", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static bool HasMissingZeta(LocalResistanceCalculationInfo local)
        {
            return !IsUnknownLocalResistanceRole(local)
                && !string.Equals(local.PathRole, "Cap", StringComparison.OrdinalIgnoreCase)
                && (local.ZetaSource == "Не определено"
                    || local.Source == "Не найдено"
                    || local.Source == "Не определено"
                    || (local.EffectiveZeta == 0 && local.AutoZeta == 0));
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

        private static void AppendUnknownFittingGroup(StringBuilder builder, IReadOnlyList<UnknownFittingElement> elements)
        {
            builder.AppendLine($"Уникальные фитинги с Unknown ролью: {elements.Count}");
            if (elements.Count == 0)
            {
                builder.AppendLine("  —");
                return;
            }

            foreach (UnknownFittingElement element in elements)
            {
                builder.AppendLine($"  {element.ElementId}; тип={element.TypeName}; семейство={element.FamilyName}; применений={element.ApplicationCount}; трассы={string.Join(", ", element.PathIndexes)}; предупреждение={element.WarningText}");
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
                    autoZeta = topLocal.Local.AutoZeta,
                    manualZeta = topLocal.Local.ManualZeta,
                    effectiveZeta = topLocal.Local.EffectiveZeta,
                    zeta = topLocal.Local.Zeta,
                    zetaSource = topLocal.Local.ZetaSource,
                    zetaComment = topLocal.Local.ZetaComment,
                    wasWrittenToRevitComment = topLocal.Local.WasWrittenToRevitComment,
                    source = topLocal.Local.Source,
                    dynamicPressurePa = topLocal.Local.DynamicPressurePa,
                    localPressureLossPa = topLocal.Local.LocalPressureLossPa,
                    pathRole = topLocal.Local.PathRole,
                    roleReason = topLocal.Local.RoleReason
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

        private static List<long> ToElementIdList(IEnumerable<string> values)
        {
            return values
                .Select(ToLongOrNull)
                .Where(id => id.HasValue)
                .Select(id => id.GetValueOrDefault())
                .ToList();
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

        private sealed class SelfCheckInfo
        {
            public string Status { get; set; } = "Failed";
            public bool SystemLoaded { get; set; }
            public bool PathsBuilt { get; set; }
            public bool CriticalPathFound { get; set; }
            public int UnknownFittingCount { get; set; }
            public int MissingZetaCount { get; set; }
            public int HoodsWithoutZetaCount { get; set; }
            public int StateConsistencyErrorCount { get; set; }
            public bool ResetToAutoVerified { get; set; }
            public bool ProjectCatalogReadSucceeded { get; set; }
            public bool ProjectCatalogWriteSucceeded { get; set; }
            public bool PathIndependentValuesConsistent { get; set; }
            public bool PathDependentValuesConsistent { get; set; }
            public bool CalculationHasNaN { get; set; }
            public bool CalculationHasInfinity { get; set; }
            public double CriticalPressureLossPa { get; set; }
            public bool HighlightApplySucceeded { get; set; } = true;
            public bool HighlightClearSucceeded { get; set; } = true;
            public bool OriginalOverridesRestored { get; set; } = true;
            public int HighlightFailedElementCount { get; set; }
            public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
        }

        private sealed class ReportDiagnostics
        {
            public IReadOnlyList<LocalResistanceApplication> LocalApplications { get; set; } = Array.Empty<LocalResistanceApplication>();

            public IReadOnlyList<DuctApplication> DuctApplications { get; set; } = Array.Empty<DuctApplication>();

            public IReadOnlyList<UniqueLocalResistanceElement> UniqueRecommendedZetaElements { get; set; } = Array.Empty<UniqueLocalResistanceElement>();

            public IReadOnlyList<UniqueLocalResistanceElement> UniqueCommentZetaElements { get; set; } = Array.Empty<UniqueLocalResistanceElement>();

            public IReadOnlyList<UniqueLocalResistanceElement> MissingZetaElements { get; set; } = Array.Empty<UniqueLocalResistanceElement>();

            public IReadOnlyList<UnknownFittingElement> UnknownFittingElements { get; set; } = Array.Empty<UnknownFittingElement>();

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


        private sealed class UnknownFittingElement
        {
            public long ElementId { get; set; }

            public string FamilyName { get; set; } = string.Empty;

            public string TypeName { get; set; } = string.Empty;

            public int ApplicationCount { get; set; }

            public List<int> PathIndexes { get; set; } = new List<int>();

            public string WarningText { get; set; } = string.Empty;
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
