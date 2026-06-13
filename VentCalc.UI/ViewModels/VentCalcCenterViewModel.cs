using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using VentCalc.Core.Models;
using VentCalc.UI.Services;

namespace VentCalc.UI.ViewModels
{
    public sealed class VentCalcCenterViewModel : NotifyObject
    {
        private readonly Action<VentCalcCenterViewModel, VentCalcLoadRequestMode> requestLoadSelectedSystem;
        private readonly Action<IEnumerable<long>>? selectElementsInRevit;
        private readonly Action<VentCalcCenterViewModel, IReadOnlyList<LocalResistanceCalculationInfo>, ZetaOverrideRequestMode>? writeZetaToRevitComments;
        private readonly Action<VentCalcCenterViewModel, HighlightRequest>? requestHighlight;
        private readonly Action<string>? showMessage;
        private readonly Action<Exception>? reportException;
        private readonly VentCalcSettingsService settingsService;
        private NetworkElementRow? selectedNetworkElement;
        private DuctCalculationInfo? selectedPathDuct;
        private LocalResistanceCalculationInfo? selectedPathLocalResistance;
        private VentIssueInfo? selectedIssue;
        private PathRow? selectedPath;
        private VentSystemSummary? selectedSystemSummary;
        private VentSystemCatalogItem? selectedCatalogSystem;
        private CalculationSectionInfo? selectedPathSection;
        private ProjectZetaCatalogRow? selectedProjectZetaCatalogRow;
        private bool isSynchronizingManualZeta;
        private string localResistanceScopeMode = "CurrentPath";
        private string selectedSettingsSection = "Воздух и расчёт";
        private string calculationViewMode = "Основные данные";
        private string issuesFilter = "Требуют внимания";
        private bool showAllProjectZetaCatalogRoles;
        private string selectedElementId = "—";
        private string systemName = "—";
        private string systemType = "—";
        private string direction = "—";
        private string directionReason = "—";
        private string statusText = "Выберите элемент вентиляционной системы в Revit и нажмите «Загрузить выбранную систему».";
        private string lastReportTxtPath = "—";
        private string lastReportJsonPath = "—";
        private string reportPreviewText = "Отчёт для проверки ещё не сформирован.";
        private string lastActionMessage = "Действий пока не было.";
        private string revitVersion = "—";
        private string revitFilePath = "—";
        private PathCalculationInfo? criticalPath;
        private HighlightDisplayMode highlightDisplayMode = VentCalc.UI.Services.HighlightDisplayMode.Normal;

        public VentCalcCenterViewModel(
            Action<VentCalcCenterViewModel, VentCalcLoadRequestMode> requestLoadSelectedSystem,
            Action<IEnumerable<long>>? selectElementsInRevit,
            Action<VentCalcCenterViewModel, IReadOnlyList<LocalResistanceCalculationInfo>, ZetaOverrideRequestMode>? writeZetaToRevitComments,
            Action<string>? showMessage,
            VentCalcSettingsService settingsService,
            Action<Exception>? reportException = null,
            Action<VentCalcCenterViewModel, HighlightRequest>? requestHighlight = null)
        {
            this.requestLoadSelectedSystem = requestLoadSelectedSystem;
            this.selectElementsInRevit = selectElementsInRevit;
            this.writeZetaToRevitComments = writeZetaToRevitComments;
            this.showMessage = showMessage;
            this.reportException = reportException;
            this.requestHighlight = requestHighlight;
            this.settingsService = settingsService;
            Settings = settingsService.Load();
            LogAction("VentCalc Center открыт; настройки загружены.");
            if (!string.IsNullOrWhiteSpace(settingsService.LastWarning))
            {
                StatusText = settingsService.LastWarning;
                Issues.Add(new VentIssueInfo
                {
                    Severity = "Warning",
                    Category = "Настройки",
                    Message = settingsService.LastWarning,
                    Recommendation = "Проверьте файл %APPDATA%\\VentCalc\\settings.json; повреждённый файл переименован."
                });
                RefreshFilteredIssues();
            }

            LoadSelectedSystemCommand = new RelayCommand(_ => RequestLoadSelectedSystem(VentCalcLoadRequestMode.SelectedElement));
            LoadCatalogSystemCommand = new RelayCommand(_ => RequestLoadSelectedSystem(VentCalcLoadRequestMode.SystemCatalog));
            RefreshCommand = new RelayCommand(_ => RequestLoadSelectedSystem(VentCalcLoadRequestMode.LastLoadedElement));
            SelectElementInRevitCommand = new RelayCommand(_ => SelectElementInRevit(), _ => CurrentSelectedElementId.HasValue);
            SelectStartElementInRevitCommand = new RelayCommand(_ => SelectStartElementInRevit(), _ => StartElementIds.Count > 0);
            SelectSelectedPathInRevitCommand = new RelayCommand(_ => SelectSelectedPathInRevit(), _ => SelectedPath != null);
            SelectCriticalPathInRevitCommand = new RelayCommand(_ => SelectCriticalPathInRevit(), _ => CriticalPath != null);
            SelectAllPathsThroughElementInRevitCommand = new RelayCommand(_ => SelectAllPathsThroughElementInRevit(), _ => PathsThroughSelectedElement.Count > 0);
            SelectLoadedPathThroughElementInRevitCommand = new RelayCommand(_ => SelectLoadedPathThroughElementInRevit(), _ => PathsThroughSelectedElement.Count > 0);
            SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
            ResetSettingsCommand = new RelayCommand(_ => ResetSettings());
            ResetSettingsSectionCommand = new RelayCommand(parameter => ResetSettingsSection(parameter?.ToString() ?? SelectedSettingsSection));
            ApplyVelocityHighlightCommand = new RelayCommand(_ => ApplyVelocityHighlight(), _ => AerodynamicSummary != null);
            ResetVelocityHighlightCommand = new RelayCommand(_ => ClearHighlight());
            PickLowVelocityColorCommand = new RelayCommand(_ => PickColor(Settings.LowVelocityColorHex, value => Settings.LowVelocityColorHex = value));
            PickNormalVelocityColorCommand = new RelayCommand(_ => PickColor(Settings.NormalVelocityColorHex, value => Settings.NormalVelocityColorHex = value));
            PickHighVelocityColorCommand = new RelayCommand(_ => PickColor(Settings.HighVelocityColorHex, value => Settings.HighVelocityColorHex = value));
            PickCriticalVelocityColorCommand = new RelayCommand(_ => PickColor(Settings.CriticalVelocityColorHex, value => Settings.CriticalVelocityColorHex = value));
            PickSelectedPathColorCommand = new RelayCommand(_ => PickColor(Settings.SelectedPathColorHex, value => Settings.SelectedPathColorHex = value));
            PickCriticalPathColorCommand = new RelayCommand(_ => PickColor(Settings.CriticalPathColorHex, value => Settings.CriticalPathColorHex = value));
            PickIssueColorCommand = new RelayCommand(_ => PickColor(Settings.IssueColorHex, value => Settings.IssueColorHex = value));
            ResetHighlightColorsCommand = new RelayCommand(_ => ResetHighlightColors());
            HighlightSelectedPathCommand = new RelayCommand(_ => HighlightSelectedPath(), _ => SelectedPath != null);
            HighlightCriticalPathCommand = new RelayCommand(_ => HighlightCriticalPath(), _ => CriticalPath != null);
            HighlightIssuesCommand = new RelayCommand(_ => HighlightIssues(), _ => GetIssueElementIds().Count > 0);
            ClearHighlightCommand = new RelayCommand(_ => ClearHighlight());
            PreviousPathCommand = new RelayCommand(_ => SelectRelativePath(-1), _ => Paths.Count > 0);
            NextPathCommand = new RelayCommand(_ => SelectRelativePath(1), _ => Paths.Count > 0);
            LoadPathsThroughSelectedElementCommand = new RelayCommand(_ => RequestLoadSelectedSystem(VentCalcLoadRequestMode.SelectedElement));
            GenerateVerificationReportCommand = new RelayCommand(_ => GenerateVerificationReport(), _ => NetworkInfo != null || !string.IsNullOrWhiteSpace(ReportText));
            SaveZetaOverridesCommand = new RelayCommand(_ => SaveManualZetaOverrides(), _ => GetChangedLocalResistanceRows().Count > 0);
            ResetSelectedZetaCommand = new RelayCommand(_ => ClearSelectedManualZeta(), _ => GetSelectedLocalResistanceRows().Count > 0);
            ResetToAutoCommand = new RelayCommand(_ => ResetSelectedZetaToAuto(), _ => GetSelectedLocalResistanceRows().Count > 0);
            ResetProjectToAutoCommand = new RelayCommand(_ => ResetProjectToAuto());
            SaveProjectZetaCatalogCommand = new RelayCommand(_ => SaveProjectZetaCatalog());
            ResetSelectedCatalogRoleCommand = new RelayCommand(_ => ResetSelectedCatalogRoleToAuto(), _ => SelectedProjectZetaCatalogRow != null);
            ResetWholeCatalogCommand = new RelayCommand(_ => ResetWholeCatalogToAuto());
            RecalculateManualZetaCommand = new RelayCommand(_ => RecalculateWithManualZeta(), _ => AerodynamicSummary != null);
            AcceptRecommendedZetaCommand = new RelayCommand(_ => AcceptRecommendedZeta(), _ => SelectedLocalResistanceRows.Count > 0 || SelectedPathLocalResistance != null);
            WriteZetaToCommentsCommand = new RelayCommand(_ => SaveManualZetaOverrides(), _ => GetChangedLocalResistanceRows().Count > 0);
            OpenReportsFolderCommand = new RelayCommand(_ => OpenReportsFolder());
            StubCommand = new RelayCommand(parameter => ShowStub(parameter?.ToString() ?? "Функция будет добавлена позже."));
            if (VentCalcSessionState.CurrentData != null)
            {
                ApplyData(VentCalcSessionState.CurrentData);
                RestoreHighlightState(VentCalcSessionState.LastHighlightState);
            }
        }

        public VentElementInfo? SelectedElementInfo { get; private set; }

        public VentNetworkInfo? NetworkInfo { get; private set; }

        public VentPathSummary? PathSummary { get; private set; }

        public AerodynamicCalculationSummary? AerodynamicSummary { get; private set; }

        public VentCalcSettings Settings { get; private set; }

        public ObservableCollection<NetworkElementRow> NetworkElements { get; } = new ObservableCollection<NetworkElementRow>();

        public ObservableCollection<PathRow> Paths { get; } = new ObservableCollection<PathRow>();

        public ObservableCollection<DuctCalculationInfo> SelectedPathDucts { get; } = new ObservableCollection<DuctCalculationInfo>();

        public ObservableCollection<LocalResistanceCalculationInfo> SelectedPathLocalResistances { get; } = new ObservableCollection<LocalResistanceCalculationInfo>();

        public ObservableCollection<LocalResistanceCalculationInfo> DisplayedLocalResistances { get; } = new ObservableCollection<LocalResistanceCalculationInfo>();

        public ObservableCollection<LocalResistanceCalculationInfo> SelectedLocalResistanceRows { get; } = new ObservableCollection<LocalResistanceCalculationInfo>();

        public ObservableCollection<ProjectZetaCatalogRow> ProjectZetaCatalogRows { get; } = new ObservableCollection<ProjectZetaCatalogRow>();

        public IEnumerable<ProjectZetaCatalogRow> VisibleProjectZetaCatalogRows => ShowAllProjectZetaCatalogRoles
            ? ProjectZetaCatalogRows
            : ProjectZetaCatalogRows.Where(row => row.ApplicationCount > 0 || row.ProjectZeta.HasValue);

        public bool ShowAllProjectZetaCatalogRoles
        {
            get => showAllProjectZetaCatalogRoles;
            set
            {
                if (SetProperty(ref showAllProjectZetaCatalogRoles, value))
                {
                    OnPropertyChanged(nameof(VisibleProjectZetaCatalogRows));
                }
            }
        }

        public string LocalResistanceScopeMode
        {
            get => localResistanceScopeMode;
            set
            {
                if (SetProperty(ref localResistanceScopeMode, string.IsNullOrWhiteSpace(value) ? "CurrentPath" : value))
                {
                    RefreshDisplayedLocalResistances();
                }
            }
        }

        public HighlightDisplayMode HighlightDisplayMode
        {
            get => highlightDisplayMode;
            set
            {
                if (SetProperty(ref highlightDisplayMode, value))
                {
                    OnPropertyChanged(nameof(IsNormalHighlightMode));
                    OnPropertyChanged(nameof(IsFocusHighlightMode));
                }
            }
        }

        public bool IsNormalHighlightMode
        {
            get => HighlightDisplayMode == VentCalc.UI.Services.HighlightDisplayMode.Normal;
            set
            {
                if (value)
                {
                    HighlightDisplayMode = VentCalc.UI.Services.HighlightDisplayMode.Normal;
                }
            }
        }

        public bool IsFocusHighlightMode
        {
            get => HighlightDisplayMode == VentCalc.UI.Services.HighlightDisplayMode.Focus;
            set
            {
                if (value)
                {
                    HighlightDisplayMode = VentCalc.UI.Services.HighlightDisplayMode.Focus;
                }
            }
        }

        public ProjectZetaCatalogRow? SelectedProjectZetaCatalogRow
        {
            get => selectedProjectZetaCatalogRow;
            set
            {
                if (SetProperty(ref selectedProjectZetaCatalogRow, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ObservableCollection<ZetaWriteActionInfo> ZetaWriteActions { get; } = new ObservableCollection<ZetaWriteActionInfo>();

        public ObservableCollection<ZetaRecalculationActionInfo> ZetaRecalculationActions { get; } = new ObservableCollection<ZetaRecalculationActionInfo>();

        public ObservableCollection<VentIssueInfo> Issues { get; } = new ObservableCollection<VentIssueInfo>();

        public ObservableCollection<VentIssueInfo> FilteredIssues { get; } = new ObservableCollection<VentIssueInfo>();

        public HighlightStateInfo HighlightState { get; } = new HighlightStateInfo();

        public int VelocityBelowMinCount => HighlightState.VelocityGroups.BelowMin;

        public int VelocityNormalCount => HighlightState.VelocityGroups.Normal;

        public int VelocityAboveMaxCount => HighlightState.VelocityGroups.AboveMax;

        public int VelocityCriticalCount => HighlightState.VelocityGroups.Critical;

        public int VelocityNotCalculatedCount => HighlightState.VelocityGroups.NotCalculated;

        public string SelectedSettingsSection
        {
            get => selectedSettingsSection;
            set => SetProperty(ref selectedSettingsSection, string.IsNullOrWhiteSpace(value) ? "Воздух и расчёт" : value);
        }

        public string CalculationViewMode
        {
            get => calculationViewMode;
            set
            {
                if (SetProperty(ref calculationViewMode, string.IsNullOrWhiteSpace(value) ? "Основные данные" : value))
                {
                    OnPropertyChanged(nameof(IsCalculationMainView));
                    OnPropertyChanged(nameof(IsCalculationEngineeringView));
                }
            }
        }

        public bool IsCalculationMainView => string.Equals(CalculationViewMode, "Основные данные", StringComparison.Ordinal);

        public bool IsCalculationEngineeringView => string.Equals(CalculationViewMode, "Инженерные данные", StringComparison.Ordinal);

        public string IssuesFilter
        {
            get => issuesFilter;
            set
            {
                if (SetProperty(ref issuesFilter, string.IsNullOrWhiteSpace(value) ? "Требуют внимания" : value))
                {
                    RefreshFilteredIssues();
                }
            }
        }

        public int ErrorIssueCount => Issues.Count(issue => IsSeverity(issue, "Error"));

        public int WarningIssueCount => Issues.Count(issue => IsSeverity(issue, "Warning"));

        public int InfoIssueCount => Issues.Count(issue => !IsSeverity(issue, "Error") && !IsSeverity(issue, "Warning"));

        public double SelectedPathFlowM3h => SelectedPath?.FlowM3hNumeric ?? 0;

        public int SelectedPathSectionCount => SelectedPathSections.Count;

        public string HighlightStatusText => HighlightState.ActiveMode == HighlightMode.None
            ? "Подсветка не активна."
            : $"Активна подсветка {HighlightState.ActiveMode}: элементов {HighlightState.HighlightedElementCount}.";

        public ObservableCollection<string> StartCandidateDetails { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> EndCandidateDetails { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> IgnoredCapDetails { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> PathWarnings { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> StartElementIds { get; } = new ObservableCollection<string>();

        public ObservableCollection<VentSystemSummary> SystemSummaries { get; } = new ObservableCollection<VentSystemSummary>();

        public ObservableCollection<VentSystemCatalogItem> SystemCatalog { get; } = new ObservableCollection<VentSystemCatalogItem>();

        public ObservableCollection<CalculationSectionInfo> SelectedPathSections { get; } = new ObservableCollection<CalculationSectionInfo>();

        public ObservableCollection<PathRow> PathsThroughSelectedElement { get; } = new ObservableCollection<PathRow>();

        public string SelectedElementId
        {
            get => selectedElementId;
            private set
            {
                if (SetProperty(ref selectedElementId, string.IsNullOrWhiteSpace(value) ? "—" : value))
                {
                    OnPropertyChanged(nameof(SelectedElementDisplay));
                }
            }
        }

        public string SelectedElementDisplay => $"ElementId: {SelectedElementId}";

        public string SystemName
        {
            get => systemName;
            private set => SetProperty(ref systemName, value);
        }

        public string SystemType
        {
            get => systemType;
            private set => SetProperty(ref systemType, value);
        }

        public string Direction
        {
            get => direction;
            private set => SetProperty(ref direction, value);
        }

        public string DirectionReason
        {
            get => directionReason;
            private set => SetProperty(ref directionReason, value);
        }

        public string StatusText
        {
            get => statusText;
            private set => SetProperty(ref statusText, value);
        }

        public int TotalElements => NetworkInfo?.Elements.Count ?? 0;

        public int DuctCount => NetworkInfo?.DuctCount ?? 0;

        public int FittingCount => NetworkInfo?.FittingCount ?? 0;

        public int AccessoryCount => NetworkInfo?.AccessoryCount ?? 0;

        public int TerminalCount => NetworkInfo?.TerminalCount ?? 0;

        public int EquipmentCount => NetworkInfo?.EquipmentCount ?? 0;

        public int OpenConnectorCount => NetworkInfo?.OpenConnectorCount ?? 0;

        public int ConnectionCount => NetworkInfo?.Connections.Count ?? 0;

        public int TraceStartCount => PathSummary?.StartElementIds.Count ?? 0;

        public int TraceEndCount => PathSummary?.EndElementIds.Count ?? 0;

        public int IgnoredCapCount => PathSummary?.IgnoredCapDetails.Count ?? 0;

        public int PathCount => PathSummary?.Paths.Count ?? 0;

        public PathCalculationInfo? CriticalPath
        {
            get => criticalPath;
            private set => SetProperty(ref criticalPath, value);
        }

        public string CriticalPathText => CriticalPath == null
            ? "Критическая трасса пока не определена."
            : string.IsNullOrWhiteSpace(NearCriticalPathIndexes)
                ? $"Критическая трасса предварительно: №{CriticalPath.PathIndex}, итого {CriticalPath.TotalPressureLossPa:0.###} Па."
                : $"Критическая трасса: №{CriticalPath.PathIndex}; почти критические: {NearCriticalPathIndexes}; итого {CriticalPath.TotalPressureLossPa:0.###} Па.";

        public string NearCriticalPathIndexes => string.Join(", ", Paths.Where(path => path.IsNearCritical && !path.IsCritical).Select(path => path.PathIndex));

        public string LoadedSystemDisplay => SystemName == "—" ? "Система не загружена" : $"{SystemName} | {SystemType} | {Direction}";

        public int CriticalPathIndex => CriticalPath?.PathIndex ?? 0;

        public string CriticalPathDisplay => CriticalPath == null
            ? "—"
            : string.IsNullOrWhiteSpace(NearCriticalPathIndexes)
                ? CriticalPath.PathIndex.ToString(CultureInfo.InvariantCulture)
                : $"{CriticalPath.PathIndex}; почти: {NearCriticalPathIndexes}";

        public double CriticalPathTotalPressureLossPa => CriticalPath?.TotalPressureLossPa ?? 0;

        public double CriticalPathTotalWithReservePa => CriticalPathTotalPressureLossPa * (1.0 + Settings.PressureReservePercent / 100.0);

        public long? LastLoadedElementId { get; private set; }

        public string RevitVersion
        {
            get => revitVersion;
            private set => SetProperty(ref revitVersion, value);
        }

        public string RevitFilePath
        {
            get => revitFilePath;
            private set => SetProperty(ref revitFilePath, value);
        }

        public double SelectedPathFrictionPressureLossPa => SelectedPath?.Calculation?.TotalFrictionPressureLossPa ?? 0;

        public double SelectedPathLocalPressureLossPa => SelectedPath?.Calculation?.TotalLocalPressureLossPa ?? 0;

        public double SelectedPathTotalPressureLossPa => SelectedPath?.Calculation?.TotalPressureLossPa ?? 0;

        public double SelectedPathTotalWithReservePa => SelectedPathTotalPressureLossPa * (1.0 + Settings.PressureReservePercent / 100.0);

        public string LoadModeDisplay { get; private set; } = "—";

        public int SystemComponentCount { get; private set; }

        public string TraceDirectionSummary => $"Направление: {Direction} / {DirectionReason}";

        public string TraceStartSummary => StartElementIds.Count == 0 ? "Старт: —" : $"Старт: {StartElementIds.First()}";

        public string TraceEndSummary => $"Концов: {TraceEndCount} терминалов/конечных точек";

        public string TraceCapsSummary => $"Заглушек проигнорировано: {IgnoredCapCount}";

        public string TraceWarningsSummary => PathWarnings.Count == 0 ? "Предупреждения: —" : $"Предупреждения: {string.Join("; ", PathWarnings)}";

        public VentSystemSummary? SelectedSystemSummary
        {
            get => selectedSystemSummary;
            set => SetProperty(ref selectedSystemSummary, value);
        }

        public VentSystemCatalogItem? SelectedCatalogSystem
        {
            get => selectedCatalogSystem;
            set
            {
                if (SetProperty(ref selectedCatalogSystem, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public NetworkElementRow? SelectedNetworkElement
        {
            get => selectedNetworkElement;
            set
            {
                if (SetProperty(ref selectedNetworkElement, value))
                {
                    ClearTableSelectionsExcept(nameof(SelectedNetworkElement));
                    UpdatePathsThroughCurrentElement();
                    NotifySelectionChanged();
                }
            }
        }

        public DuctCalculationInfo? SelectedPathDuct
        {
            get => selectedPathDuct;
            set
            {
                if (SetProperty(ref selectedPathDuct, value))
                {
                    ClearTableSelectionsExcept(nameof(SelectedPathDuct));
                    UpdatePathsThroughCurrentElement();
                    NotifySelectionChanged();
                }
            }
        }

        public CalculationSectionInfo? SelectedPathSection
        {
            get => selectedPathSection;
            set
            {
                if (SetProperty(ref selectedPathSection, value))
                {
                    ClearTableSelectionsExcept(nameof(SelectedPathSection));
                    UpdatePathsThroughCurrentElement();
                    NotifySelectionChanged();
                }
            }
        }

        public LocalResistanceCalculationInfo? SelectedPathLocalResistance
        {
            get => selectedPathLocalResistance;
            set
            {
                if (SetProperty(ref selectedPathLocalResistance, value))
                {
                    ClearTableSelectionsExcept(nameof(SelectedPathLocalResistance));
                    UpdatePathsThroughCurrentElement();
                    NotifySelectionChanged();
                }
            }
        }

        public VentIssueInfo? SelectedIssue
        {
            get => selectedIssue;
            set
            {
                if (SetProperty(ref selectedIssue, value))
                {
                    ClearTableSelectionsExcept(nameof(SelectedIssue));
                    UpdatePathsThroughCurrentElement();
                    NotifySelectionChanged();
                }
            }
        }

        public PathRow? SelectedPath
        {
            get => selectedPath;
            set
            {
                if (SetProperty(ref selectedPath, value))
                {
                    UpdateSelectedPathDetails();
                    OnPropertyChanged(nameof(SelectedPathChain));
                }
            }
        }

        public string SelectedPathChain => SelectedPath == null ? "—" : string.Join(" → ", SelectedPath.ElementIds);

        public string ReportText { get; private set; } = "Экспорт будет добавлен после стабилизации расчёта.";

        public string LastReportTxtPath
        {
            get => lastReportTxtPath;
            private set => SetProperty(ref lastReportTxtPath, value);
        }

        public string LastReportJsonPath
        {
            get => lastReportJsonPath;
            private set => SetProperty(ref lastReportJsonPath, value);
        }

        public string ReportPreviewText
        {
            get => reportPreviewText;
            private set => SetProperty(ref reportPreviewText, value);
        }

        public string LastActionMessage
        {
            get => lastActionMessage;
            private set
            {
                if (SetProperty(ref lastActionMessage, value))
                {
                    OnPropertyChanged(nameof(LastActionDisplay));
                }
            }
        }

        public string LastActionDisplay => $"Последнее действие: {LastActionMessage}";

        public ICommand LoadSelectedSystemCommand { get; }

        public ICommand LoadCatalogSystemCommand { get; }

        public ICommand RefreshCommand { get; }

        public ICommand SelectElementInRevitCommand { get; }

        public ICommand SelectStartElementInRevitCommand { get; }

        public ICommand SelectSelectedPathInRevitCommand { get; }

        public ICommand SelectCriticalPathInRevitCommand { get; }

        public ICommand SelectAllPathsThroughElementInRevitCommand { get; private set; } = null!;

        public ICommand SelectLoadedPathThroughElementInRevitCommand { get; private set; } = null!;

        public ICommand SaveSettingsCommand { get; }

        public ICommand ResetSettingsCommand { get; }

        public ICommand ResetSettingsSectionCommand { get; }

        public ICommand ApplyVelocityHighlightCommand { get; }

        public ICommand ResetVelocityHighlightCommand { get; }

        public ICommand PickLowVelocityColorCommand { get; }

        public ICommand PickNormalVelocityColorCommand { get; }

        public ICommand PickHighVelocityColorCommand { get; }

        public ICommand PickCriticalVelocityColorCommand { get; }

        public ICommand PickSelectedPathColorCommand { get; }

        public ICommand PickCriticalPathColorCommand { get; }

        public ICommand PickIssueColorCommand { get; }

        public ICommand ResetHighlightColorsCommand { get; }

        public ICommand HighlightSelectedPathCommand { get; }

        public ICommand HighlightCriticalPathCommand { get; }

        public ICommand HighlightIssuesCommand { get; }

        public ICommand ClearHighlightCommand { get; }

        public ICommand PreviousPathCommand { get; }

        public ICommand NextPathCommand { get; }

        public ICommand LoadPathsThroughSelectedElementCommand { get; }

        public ICommand GenerateVerificationReportCommand { get; }

        public ICommand SaveZetaOverridesCommand { get; }

        public ICommand ResetSelectedZetaCommand { get; }

        public ICommand ResetToAutoCommand { get; }

        public ICommand ResetProjectToAutoCommand { get; }

        public ICommand SaveProjectZetaCatalogCommand { get; }

        public ICommand ResetSelectedCatalogRoleCommand { get; }

        public ICommand ResetWholeCatalogCommand { get; }

        public ICommand RecalculateManualZetaCommand { get; }

        public ICommand AcceptRecommendedZetaCommand { get; }

        public ICommand WriteZetaToCommentsCommand { get; }

        public ICommand OpenReportsFolderCommand { get; }

        public ICommand StubCommand { get; }

        private void RequestLoadSelectedSystem(VentCalcLoadRequestMode mode)
        {
            try
            {
                StatusText = mode == VentCalcLoadRequestMode.LastLoadedElement && LastLoadedElementId.HasValue
                    ? "Обновление последней загруженной системы..."
                    : mode == VentCalcLoadRequestMode.SystemCatalog && SelectedCatalogSystem == null
                        ? "Ожидание Revit: чтение списка систем."
                        : mode == VentCalcLoadRequestMode.SystemCatalog
                            ? "Ожидание Revit: загрузка системы из списка."
                            : "Ожидание Revit: выберите один элемент воздуховодной системы в Revit.";
                requestLoadSelectedSystem(this, mode);
            }
            catch (Exception exception)
            {
                FailLoad(exception);
            }
        }

        public void CompleteLoad(VentCalcCenterData data)
        {
            ApplyData(data);
            StatusText = data.Success
                ? "Система загружена."
                : (string.IsNullOrWhiteSpace(data.ErrorMessage) ? "Выберите один элемент воздуховодной системы в Revit." : data.ErrorMessage);
            LogAction(data.Success ? $"Загрузка системы выполнена: {SystemName}, трасс {PathCount}." : $"Загрузка системы не выполнена: {StatusText}");
        }

        public void FailLoad(Exception exception)
        {
            StatusText = exception.Message;
            Issues.Add(new VentIssueInfo
            {
                Severity = "Error",
                Category = "VentCalc Center",
                Message = exception.Message,
                Recommendation = "Смотрите лог VentCalc; Revit не должен завершаться аварийно."
            });
            reportException?.Invoke(exception);
            showMessage?.Invoke(exception.ToString());
        }

        private void ApplyData(VentCalcCenterData data)
        {
            string settingsWarning = settingsService.LastWarning;
            SelectedElementInfo = data.SelectedElementInfo;
            NetworkInfo = data.NetworkInfo;
            PathSummary = data.PathSummary;
            AerodynamicSummary = data.AerodynamicSummary;
            ReportText = data.ReportText;
            RevitVersion = data.RevitVersion;
            RevitFilePath = data.RevitFilePath;
            SelectedElementId = data.SelectedElementInfo?.ElementId ?? data.NetworkInfo?.SelectedElementId ?? "—";
            SystemName = data.SelectedElementInfo?.SystemName ?? data.NetworkInfo?.Elements.FirstOrDefault()?.SystemName ?? "—";
            SystemType = data.PathSummary?.SystemType ?? data.SelectedElementInfo?.SystemType ?? "—";
            Direction = data.PathSummary?.Direction ?? "—";
            DirectionReason = data.PathSummary?.DirectionReason ?? "—";
            CriticalPath = data.AerodynamicSummary?.CriticalPathByTotalPressure;
            LoadModeDisplay = string.IsNullOrWhiteSpace(data.LoadMode) ? "—" : data.LoadMode;
            SystemComponentCount = data.SystemComponentCount;
            if (data.Success && long.TryParse(data.SelectedElementInfo?.ElementId ?? data.NetworkInfo?.SelectedElementId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long loadedElementId))
            {
                LastLoadedElementId = loadedElementId;
            }

            Replace(NetworkElements, BuildNetworkRows(data));
            Replace(Paths, BuildPathRows(data.PathSummary, data.AerodynamicSummary));
            Replace(SystemSummaries, BuildSystemSummaries(data));
            SelectedSystemSummary = SystemSummaries.FirstOrDefault();
            Replace(SystemCatalog, data.SystemCatalog);
            SynchronizeSelectedCatalogSystem(data);
            Replace(Issues, SortIssues(BuildIssues(data)));
            RefreshFilteredIssues();
            if (!string.IsNullOrWhiteSpace(settingsWarning))
            {
                Issues.Insert(0, new VentIssueInfo
                {
                    Severity = "Warning",
                    Category = "Настройки",
                    Message = settingsWarning,
                    Recommendation = "Проверьте файл %APPDATA%\\VentCalc\\settings.json; повреждённый файл переименован."
                });
                RefreshFilteredIssues();
            }
            Replace(StartCandidateDetails, data.PathSummary?.StartCandidateDetails ?? Array.Empty<string>());
            Replace(EndCandidateDetails, data.PathSummary?.EndCandidateDetails ?? Array.Empty<string>());
            Replace(IgnoredCapDetails, data.PathSummary?.IgnoredCapDetails ?? Array.Empty<string>());
            Replace(PathWarnings, data.PathSummary?.Warnings ?? Array.Empty<string>());
            Replace(StartElementIds, data.PathSummary?.StartElementIds ?? Array.Empty<string>());
            SelectedPath = Paths.FirstOrDefault(path => path.PathIndex == CriticalPath?.PathIndex) ?? Paths.FirstOrDefault();

            OnPropertyChanged(nameof(TotalElements));
            OnPropertyChanged(nameof(DuctCount));
            OnPropertyChanged(nameof(FittingCount));
            OnPropertyChanged(nameof(AccessoryCount));
            OnPropertyChanged(nameof(TerminalCount));
            OnPropertyChanged(nameof(EquipmentCount));
            OnPropertyChanged(nameof(OpenConnectorCount));
            OnPropertyChanged(nameof(ConnectionCount));
            OnPropertyChanged(nameof(TraceStartCount));
            OnPropertyChanged(nameof(TraceEndCount));
            OnPropertyChanged(nameof(IgnoredCapCount));
            OnPropertyChanged(nameof(PathCount));
            OnPropertyChanged(nameof(CriticalPathText));
            OnPropertyChanged(nameof(LoadedSystemDisplay));
            OnPropertyChanged(nameof(CriticalPathIndex));
            OnPropertyChanged(nameof(CriticalPathDisplay));
            RefreshFilteredIssues();
            OnPropertyChanged(nameof(CriticalPathTotalPressureLossPa));
            OnPropertyChanged(nameof(CriticalPathTotalWithReservePa));
            OnPropertyChanged(nameof(LastLoadedElementId));
            OnPropertyChanged(nameof(SelectedElementDisplay));
            OnPropertyChanged(nameof(SelectedPathFrictionPressureLossPa));
            OnPropertyChanged(nameof(SelectedPathLocalPressureLossPa));
            OnPropertyChanged(nameof(SelectedPathTotalPressureLossPa));
            OnPropertyChanged(nameof(SelectedPathTotalWithReservePa));
            OnPropertyChanged(nameof(SelectedPathFlowM3h));
            OnPropertyChanged(nameof(SelectedPathSectionCount));
            OnPropertyChanged(nameof(ReportText));
            BuildProjectZetaCatalogRows();
            RefreshDisplayedLocalResistances();
            OnPropertyChanged(nameof(LocalResistanceRecognitionSummary));
            OnPropertyChanged(nameof(LoadModeDisplay));
            OnPropertyChanged(nameof(SystemComponentCount));
            OnPropertyChanged(nameof(TraceDirectionSummary));
            OnPropertyChanged(nameof(TraceStartSummary));
            OnPropertyChanged(nameof(TraceEndSummary));
            OnPropertyChanged(nameof(TraceCapsSummary));
            OnPropertyChanged(nameof(TraceWarningsSummary));
            VentCalcSessionState.StoreData(data);
            CommandManager.InvalidateRequerySuggested();
        }

        private static IReadOnlyList<VentSystemSummary> BuildSystemSummaries(VentCalcCenterData data)
        {
            if (data.NetworkInfo == null)
            {
                return Array.Empty<VentSystemSummary>();
            }

            return new[]
            {
                new VentSystemSummary
                {
                    SystemName = data.SelectedElementInfo?.SystemName ?? data.NetworkInfo.Elements.FirstOrDefault()?.SystemName ?? "—",
                    SystemType = data.PathSummary?.SystemType ?? data.SelectedElementInfo?.SystemType ?? "—",
                    Direction = data.PathSummary?.Direction ?? "—",
                    ElementCount = data.NetworkInfo.Elements.Count,
                    DuctCount = data.NetworkInfo.DuctCount,
                    FittingCount = data.NetworkInfo.FittingCount,
                    TerminalCount = data.NetworkInfo.TerminalCount,
                    EquipmentCount = data.NetworkInfo.EquipmentCount
                }
            };
        }

        private static IReadOnlyList<NetworkElementRow> BuildNetworkRows(VentCalcCenterData data)
        {
            if (data.NetworkInfo == null)
            {
                return Array.Empty<NetworkElementRow>();
            }

            return data.NetworkInfo.Elements
                .Select(node => new NetworkElementRow
                {
                    ElementId = node.ElementId,
                    Category = node.CategoryName,
                    Role = node.Role.ToString(),
                    Size = node.Size,
                    FlowM3h = node.FlowM3h,
                    LengthM = node.DuctLengthMm / 1000.0,
                    SystemName = node.SystemName,
                    ConnectedElementIds = string.Join(", ", node.ConnectedElementIds),
                    Warnings = BuildNodeWarning(node, data.PathSummary)
                })
                .OrderBy(row => row.ElementId, StringComparer.Ordinal)
                .ToList();
        }

        private static string BuildNodeWarning(VentNetworkNode node, VentPathSummary? pathSummary)
        {
            var warnings = new List<string>();
            if (node.OpenConnectorCount > 0)
            {
                warnings.Add($"Открытых коннекторов: {node.OpenConnectorCount}");
            }

            if (node.IsIgnoredForPathSearch || pathSummary?.IgnoredCapDetails.Any(detail => detail.StartsWith(node.ElementId, StringComparison.Ordinal)) == true)
            {
                warnings.Add("Заглушка игнорируется при трассировке");
            }

            if (!string.IsNullOrWhiteSpace(node.PathRoleReason))
            {
                warnings.Add(node.PathRoleReason);
            }

            return string.Join("; ", warnings);
        }

        private static IReadOnlyList<PathRow> BuildPathRows(VentPathSummary? pathSummary, AerodynamicCalculationSummary? aerodynamicSummary)
        {
            if (pathSummary == null)
            {
                return Array.Empty<PathRow>();
            }

            Dictionary<int, PathCalculationInfo> calculations = aerodynamicSummary?.Paths.ToDictionary(path => path.PathIndex) ?? new Dictionary<int, PathCalculationInfo>();
            PathCalculationInfo? critical = aerodynamicSummary?.CriticalPathByTotalPressure;
            double criticalLoss = critical?.TotalPressureLossPa ?? 0;
            return pathSummary.Paths
                .Select(path =>
                {
                    calculations.TryGetValue(path.PathIndex, out PathCalculationInfo? calculation);
                    return new PathRow(path, calculation, critical?.PathIndex, criticalLoss);
                })
                .ToList();
        }

        private void SynchronizeSelectedCatalogSystem(VentCalcCenterData data)
        {
            string loadedSystemName = SystemName == "—" ? string.Empty : SystemName;
            string loadedSystemType = SystemType == "—" ? string.Empty : SystemType;

            VentSystemCatalogItem? matching = SystemCatalog.FirstOrDefault(item =>
                string.Equals(item.SystemName, loadedSystemName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.SystemType, loadedSystemType, StringComparison.OrdinalIgnoreCase));

            if (matching == null && !string.IsNullOrWhiteSpace(loadedSystemName))
            {
                matching = SystemCatalog.FirstOrDefault(item =>
                    string.Equals(item.SystemName, loadedSystemName, StringComparison.OrdinalIgnoreCase));
            }

            if (matching == null && !string.IsNullOrWhiteSpace(loadedSystemName))
            {
                matching = new VentSystemCatalogItem
                {
                    SystemName = loadedSystemName,
                    SystemType = loadedSystemType,
                    Direction = Direction == "—" ? string.Empty : Direction,
                    ElementCount = NetworkInfo?.Elements.Count ?? 0,
                    DuctCount = NetworkInfo?.DuctCount ?? 0,
                    FittingCount = NetworkInfo?.FittingCount ?? 0,
                    TerminalCount = NetworkInfo?.TerminalCount ?? 0,
                    EquipmentCount = NetworkInfo?.EquipmentCount ?? 0
                };
                matching.DisplayName = string.IsNullOrWhiteSpace(loadedSystemType)
                    ? $"{loadedSystemName} — {matching.ElementCount} элементов"
                    : $"{loadedSystemName} — {loadedSystemType} — {matching.ElementCount} элементов";
                SystemCatalog.Add(matching);
                data.Warnings.Add($"Загруженная система {matching.DisplayName} отсутствовала в каталоге систем и временно добавлена в список.");
            }

            SelectedCatalogSystem = matching ?? SystemCatalog.FirstOrDefault();
            data.SelectedSystemName = SelectedCatalogSystem?.SystemName ?? loadedSystemName;
            data.SelectedSystemType = SelectedCatalogSystem?.SystemType ?? loadedSystemType;
        }

        private IReadOnlyList<VentIssueInfo> BuildIssues(VentCalcCenterData data)
        {
            var result = new List<VentIssueInfo>();
            foreach (string warning in data.Warnings)
            {
                result.Add(new VentIssueInfo
                {
                    Severity = "Warning",
                    Category = "VentCalc Center",
                    Message = warning,
                    Recommendation = "Исправьте выбор или модель и нажмите 'Загрузить выбранную систему'."
                });
            }

            if (!data.Success && !string.IsNullOrWhiteSpace(data.ErrorMessage))
            {
                result.Add(new VentIssueInfo
                {
                    Severity = data.IsUserSelectionWarning ? "Warning" : "Error",
                    Category = data.IsUserSelectionWarning ? "Выбор" : "Ошибка чтения сети",
                    Message = data.ErrorMessage,
                    Recommendation = data.IsUserSelectionWarning
                        ? "Выберите один элемент воздуховодной системы и нажмите 'Загрузить выбранную систему'."
                        : "Откройте лог ошибки и передайте его разработчику."
                });
            }

            if (data.NetworkInfo != null)
            {
                var systemPairs = data.NetworkInfo.Elements
                    .Select(node => new { node.SystemName, node.SystemType })
                    .Where(item => !string.IsNullOrWhiteSpace(item.SystemName) && item.SystemName != "—")
                    .Distinct()
                    .ToList();
                if (systemPairs.Count > 1)
                {
                    result.Add(new VentIssueInfo
                    {
                        Severity = "Warning",
                        Category = "Системы",
                        Message = "В найденной сети обнаружены элементы разных систем. Проверьте соединения и классификацию системы.",
                        Recommendation = string.Join("; ", systemPairs.Select(item => $"{item.SystemName} / {item.SystemType}"))
                    });
                }

                foreach (VentNetworkNode node in data.NetworkInfo.Elements.Where(node => node.OpenConnectorCount > 0))
                {
                    result.Add(new VentIssueInfo
                    {
                        Severity = node.Role == VentNodeRole.OpenEndCandidate ? "Info" : "Warning",
                        ElementId = node.ElementId,
                        Category = "Открытые коннекторы",
                        Message = $"Открытых коннекторов: {node.OpenConnectorCount}.",
                        Recommendation = node.Role == VentNodeRole.OpenEndCandidate
                            ? "Допустимый открытый магистральный конец используется как кандидат трассировки."
                            : "Проверьте подключение элемента или наличие заглушки."
                    });
                }

                if (data.NetworkInfo.EquipmentCount == 0)
                {
                    result.Add(new VentIssueInfo
                    {
                        Severity = "Warning",
                        ElementId = data.NetworkInfo.SelectedElementId,
                        Category = "Оборудование",
                        Message = "Оборудование не найдено; используется открытый магистральный конец.",
                        Recommendation = "Проверьте, должна ли система быть подключена к установке/вентилятору."
                    });
                }
            }

            if (data.PathSummary != null)
            {
                foreach (string cap in data.PathSummary.IgnoredCapDetails)
                {
                    result.Add(new VentIssueInfo
                    {
                        Severity = "Info",
                        ElementId = ExtractElementId(cap),
                        Category = "Заглушки",
                        Message = cap,
                        Recommendation = "Заглушка показана в сети, но не используется как старт/конец трассы."
                    });
                }
            }

            foreach (DuctCalculationInfo duct in data.AerodynamicSummary?.Paths.SelectMany(path => path.Ducts) ?? Enumerable.Empty<DuctCalculationInfo>())
            {
                if (duct.FlowM3h <= 0)
                {
                    AddDuctIssue(result, duct.ElementId, "Воздуховоды без расхода", "Расход воздуха не найден или равен 0.", "Проверьте системный расход и параметры воздуховода.");
                }

                if (duct.AreaM2 <= 0 || duct.EquivalentDiameterM <= 0)
                {
                    AddDuctIssue(result, duct.ElementId, "Воздуховоды без размера", "Не найден корректный размер воздуховода.", "Проверьте диаметр или ширину/высоту.");
                }

                if (duct.LengthM < 0.02)
                {
                    AddDuctIssue(result, duct.ElementId, "Короткие воздуховоды", "Длина меньше 0.02 м.", "Проверьте корректность геометрии участка.");
                }

                if (duct.VelocityMs > Settings.CriticalVelocityMs)
                {
                    AddDuctIssue(result, duct.ElementId, "Скорости", $"Критическая скорость {duct.VelocityMs:0.###} м/с.", "Проверьте расход и размер участка.", "Error");
                }
                else if (duct.VelocityMs > Settings.MaxVelocityMs)
                {
                    AddDuctIssue(result, duct.ElementId, "Скорости", $"Высокая скорость {duct.VelocityMs:0.###} м/с.", "Рассмотрите увеличение сечения.", "Warning");
                }
                else if (duct.VelocityMs > 0 && duct.VelocityMs < Settings.MinVelocityMs)
                {
                    AddDuctIssue(result, duct.ElementId, "Скорости", $"Низкая скорость {duct.VelocityMs:0.###} м/с.", "Проверьте целесообразность выбранного сечения.", "Info");
                }
            }

            var unknownLocalResistanceGroups = (data.AerodynamicSummary?.Paths ?? Enumerable.Empty<PathCalculationInfo>())
                .SelectMany(path => path.LocalResistances
                    .Where(IsUnknownLocalResistanceRole)
                    .Select(local => new { path.PathIndex, Local = local }))
                .GroupBy(item => item.Local.ElementId)
                .OrderBy(group => group.Key);

            foreach (var group in unknownLocalResistanceGroups)
            {
                List<int> pathIndexes = group.Select(item => (int)item.PathIndex).Distinct().OrderBy(index => index).ToList();
                LocalResistanceCalculationInfo first = group.First().Local;
                result.Add(new VentIssueInfo
                {
                    Severity = "Warning",
                    ElementId = group.Key.ToString(CultureInfo.InvariantCulture),
                    Category = "Местные сопротивления",
                    Message = $"Роль фитинга не определена в {group.Count()} применениях трасс. PathRole: {first.PathRole}.",
                    Recommendation = $"Проверьте роль фитинга или заполните ζ в комментарии, например z=0.35. Трассы: {string.Join(", ", pathIndexes)}."
                });
            }

            return result;
        }

        private static bool IsUnknownLocalResistanceRole(LocalResistanceCalculationInfo local)
        {
            return local.Source == "Не найдено"
                || local.Source == "Не определено"
                || string.Equals(local.PathRole, "Unknown", StringComparison.OrdinalIgnoreCase)
                || local.PathRole?.EndsWith("Unknown", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static IReadOnlyList<VentIssueInfo> SortIssues(IEnumerable<VentIssueInfo> issues)
        {
            return issues
                .OrderBy(issue => issue.Severity == "Error" ? 0 : issue.Severity == "Warning" ? 1 : 2)
                .ThenBy(issue => issue.Category, StringComparer.Ordinal)
                .ThenBy(issue => issue.ElementId, StringComparer.Ordinal)
                .ToList();
        }

        private void RefreshFilteredIssues()
        {
            IEnumerable<VentIssueInfo> filtered = IssuesFilter switch
            {
                "Все" => Issues,
                "Ошибки" => Issues.Where(issue => IsSeverity(issue, "Error")),
                "Предупреждения" => Issues.Where(issue => IsSeverity(issue, "Warning")),
                "Сведения" => Issues.Where(issue => !IsSeverity(issue, "Error") && !IsSeverity(issue, "Warning")),
                _ => Issues.Where(issue => IsSeverity(issue, "Error") || IsSeverity(issue, "Warning"))
            };

            Replace(FilteredIssues, filtered);
            OnPropertyChanged(nameof(ErrorIssueCount));
            OnPropertyChanged(nameof(WarningIssueCount));
            OnPropertyChanged(nameof(InfoIssueCount));
        }

        private static bool IsSeverity(VentIssueInfo issue, string severity)
        {
            return string.Equals(issue.Severity, severity, StringComparison.OrdinalIgnoreCase);
        }

        private static void AddDuctIssue(List<VentIssueInfo> issues, long elementId, string category, string message, string recommendation, string severity = "Warning")
        {
            issues.Add(new VentIssueInfo
            {
                Severity = severity,
                ElementId = elementId.ToString(CultureInfo.InvariantCulture),
                Category = category,
                Message = message,
                Recommendation = recommendation
            });
        }

        private void UpdateSelectedPathDetails()
        {
            selectedPathDuct = null;
            selectedPathLocalResistance = null;
            UnsubscribeLocalResistanceRows();
            SelectedLocalResistanceRows.Clear();
            selectedPathSection = null;
            OnPropertyChanged(nameof(SelectedPathDuct));
            OnPropertyChanged(nameof(SelectedPathLocalResistance));
            OnPropertyChanged(nameof(SelectedPathSection));
            NotifySelectionChanged();
            Replace(SelectedPathDucts, SelectedPath?.Calculation?.Ducts ?? Enumerable.Empty<DuctCalculationInfo>());
            Replace(SelectedPathSections, SelectedPath?.Calculation?.Sections ?? Enumerable.Empty<CalculationSectionInfo>());
            OnPropertyChanged(nameof(SelectedPathSectionCount));
            Replace(SelectedPathLocalResistances, SelectedPath?.Calculation?.LocalResistances ?? Enumerable.Empty<LocalResistanceCalculationInfo>());
            SubscribeLocalResistanceRows();
            RefreshDisplayedLocalResistances();

            UpdatePathsThroughCurrentElement();
            OnPropertyChanged(nameof(SelectedPathChain));
            OnPropertyChanged(nameof(SelectedPathFrictionPressureLossPa));
            OnPropertyChanged(nameof(SelectedPathLocalPressureLossPa));
            OnPropertyChanged(nameof(SelectedPathTotalPressureLossPa));
            OnPropertyChanged(nameof(SelectedPathTotalWithReservePa));
            OnPropertyChanged(nameof(SelectedPathFlowM3h));
        }

        private void SubscribeLocalResistanceRows()
        {
            foreach (LocalResistanceCalculationInfo local in SelectedPathLocalResistances)
            {
                local.PropertyChanged -= LocalResistance_PropertyChanged;
                local.PropertyChanged += LocalResistance_PropertyChanged;
            }
        }

        private void UnsubscribeLocalResistanceRows()
        {
            foreach (LocalResistanceCalculationInfo local in SelectedPathLocalResistances)
            {
                local.PropertyChanged -= LocalResistance_PropertyChanged;
            }
        }

        private void LocalResistance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not LocalResistanceCalculationInfo local)
            {
                return;
            }

            if (e.PropertyName == nameof(LocalResistanceCalculationInfo.ManualZeta)
                || e.PropertyName == nameof(LocalResistanceCalculationInfo.ManualZetaText)
                || e.PropertyName == nameof(LocalResistanceCalculationInfo.EffectiveZeta)
                || e.PropertyName == nameof(LocalResistanceCalculationInfo.ZetaSource)
                || e.PropertyName == nameof(LocalResistanceCalculationInfo.LocalPressureLossPa)
                || e.PropertyName == nameof(LocalResistanceCalculationInfo.ValidationMessage))
            {
                OnPropertyChanged(nameof(SelectedPathLocalPressureLossPa));
                OnPropertyChanged(nameof(SelectedPathTotalPressureLossPa));
                OnPropertyChanged(nameof(SelectedPathTotalWithReservePa));
                NotifyLocalResistanceUiChanged();
                CommandManager.InvalidateRequerySuggested();

                if ((e.PropertyName == nameof(LocalResistanceCalculationInfo.ManualZeta)
                    || e.PropertyName == nameof(LocalResistanceCalculationInfo.ManualZetaText))
                    && string.IsNullOrWhiteSpace(local.ValidationMessage)
                    && !isSynchronizingManualZeta)
                {
                    SynchronizeManualZetaApplications(local);
                    RecalculateWithManualZeta();
                    string zetaText = local.ManualZeta.HasValue
                        ? local.ManualZeta.GetValueOrDefault().ToString("0.###", CultureInfo.InvariantCulture)
                        : "Auto";
                    LogAction($"ManualZeta изменён: ElementId {local.ElementId}, ζ={zetaText}. Расчёт обновлён автоматически.");
                }
            }
        }

        private void SynchronizeManualZetaApplications(LocalResistanceCalculationInfo changedRow)
        {
            if (AerodynamicSummary == null)
            {
                return;
            }

            isSynchronizingManualZeta = true;
            try
            {
                IEnumerable<LocalResistanceCalculationInfo> matchingRows = AerodynamicSummary.Paths
                    .SelectMany(path => path.LocalResistances)
                    .Where(row => ReferenceEquals(row, changedRow)
                        || (changedRow.PathDependent
                            ? string.Equals(row.OverrideKey, changedRow.OverrideKey, StringComparison.Ordinal)
                            : row.ElementId == changedRow.ElementId && !row.PathDependent));

                foreach (LocalResistanceCalculationInfo row in matchingRows)
                {
                    if (ReferenceEquals(row, changedRow))
                    {
                        continue;
                    }

                    row.ManualZeta = changedRow.ManualZeta;
                    if (!changedRow.ManualZeta.HasValue)
                    {
                        row.EffectiveZeta = row.OriginalEffectiveZeta;
                        row.Zeta = row.OriginalEffectiveZeta;
                        row.Source = string.IsNullOrWhiteSpace(row.OriginalZetaSource) ? row.Source : row.OriginalZetaSource;
                        row.ZetaSource = row.Source;
                        row.LocalPressureLossPa = row.EffectiveZeta * row.DynamicPressurePa;
                    }
                }
            }
            finally
            {
                isSynchronizingManualZeta = false;
            }
        }

        private long? CurrentSelectedElementId
        {
            get
            {
                if (SelectedNetworkElement != null && TryParseElementId(SelectedNetworkElement.ElementId, out long networkElementId))
                {
                    return networkElementId;
                }

                if (SelectedPathDuct != null)
                {
                    return SelectedPathDuct.ElementId;
                }

                if (SelectedPathLocalResistance != null)
                {
                    return SelectedPathLocalResistance.ElementId;
                }

                if (SelectedPathSection?.ElementIds.Count > 0)
                {
                    return SelectedPathSection.ElementIds.First();
                }

                if (SelectedIssue != null && TryParseElementId(SelectedIssue.ElementId, out long issueElementId))
                {
                    return issueElementId;
                }

                if (TryParseElementId(SelectedElementId, out long loadedElementId))
                {
                    return loadedElementId;
                }

                return null;
            }
        }

        private void SelectElementInRevit()
        {
            long? elementId = CurrentSelectedElementId;
            if (!elementId.HasValue)
            {
                StatusText = "Выберите строку с ElementId.";
                return;
            }

            SelectElementsInRevit(new[] { elementId.GetValueOrDefault() }, "Элемент выделен в Revit.");
        }

        private void SelectStartElementInRevit()
        {
            string? firstStart = StartElementIds.FirstOrDefault();
            if (firstStart != null && TryParseElementId(firstStart, out long elementId))
            {
                SelectElementsInRevit(new[] { elementId }, "Стартовый элемент выделен в Revit.");
            }
        }

        private void SelectSelectedPathInRevit()
        {
            if (SelectedPath == null)
            {
                StatusText = "Выберите трассу.";
                return;
            }

            SelectPathElementIds(SelectedPath.ElementIds, $"Трасса №{SelectedPath.PathIndex} выделена в Revit.");
        }

        private void SelectCriticalPathInRevit()
        {
            if (CriticalPath == null)
            {
                StatusText = "Критическая трасса пока не определена.";
                return;
            }

            SelectElementsInRevit(CriticalPath.ElementIds, $"Критическая трасса №{CriticalPath.PathIndex} выделена в Revit.");
        }

        private void SelectAllPathsThroughElementInRevit()
        {
            if (PathsThroughSelectedElement.Count == 0)
            {
                StatusText = "Через выбранный элемент трассы не найдены.";
                return;
            }

            List<long> ids = PathsThroughSelectedElement
                .SelectMany(path => path.ElementIds)
                .Where(value => TryParseElementId(value, out _))
                .Select(value => long.Parse(value, CultureInfo.InvariantCulture))
                .Distinct()
                .ToList();
            SelectElementsInRevit(ids, "Все трассы через выбранный элемент выделены в Revit.");
        }

        private void SelectLoadedPathThroughElementInRevit()
        {
            PathRow? path = PathsThroughSelectedElement
                .OrderByDescending(row => row.TotalPressureLossPa)
                .ThenByDescending(row => row.FlowM3hNumeric)
                .FirstOrDefault();
            if (path == null)
            {
                StatusText = "Через выбранный элемент трассы не найдены.";
                return;
            }

            SelectPathElementIds(path.ElementIds, $"Самая нагруженная трасса через элемент: №{path.PathIndex} выделена в Revit.");
        }

        private void SelectPathElementIds(IEnumerable<string> elementIds, string successMessage)
        {
            List<long> parsedIds = elementIds
                .Where(value => TryParseElementId(value, out _))
                .Select(value => long.Parse(value, CultureInfo.InvariantCulture))
                .ToList();
            SelectElementsInRevit(parsedIds, successMessage);
        }

        private void SelectElementsInRevit(IEnumerable<long> elementIds, string successMessage)
        {
            List<long> ids = elementIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                StatusText = "Выберите строку с ElementId.";
                return;
            }

            selectElementsInRevit?.Invoke(ids);
            StatusText = successMessage;
        }

        private void ClearTableSelectionsExcept(string propertyName)
        {
            if (propertyName != nameof(SelectedNetworkElement) && selectedNetworkElement != null)
            {
                selectedNetworkElement = null;
                OnPropertyChanged(nameof(SelectedNetworkElement));
            }

            if (propertyName != nameof(SelectedPathDuct) && selectedPathDuct != null)
            {
                selectedPathDuct = null;
                OnPropertyChanged(nameof(SelectedPathDuct));
            }

            if (propertyName != nameof(SelectedPathLocalResistance) && selectedPathLocalResistance != null)
            {
                selectedPathLocalResistance = null;
                OnPropertyChanged(nameof(SelectedPathLocalResistance));
            }

            if (propertyName != nameof(SelectedPathSection) && selectedPathSection != null)
            {
                selectedPathSection = null;
                OnPropertyChanged(nameof(SelectedPathSection));
            }

            if (propertyName != nameof(SelectedIssue) && selectedIssue != null)
            {
                selectedIssue = null;
                OnPropertyChanged(nameof(SelectedIssue));
            }
        }

        private void UpdatePathsThroughCurrentElement()
        {
            long? elementId = CurrentSelectedElementId;
            if (!elementId.HasValue)
            {
                Replace(PathsThroughSelectedElement, Array.Empty<PathRow>());
                OnPropertyChanged(nameof(PathsThroughElementSummary));
                OnPropertyChanged(nameof(PathsThroughElementMaxLossPa));
                OnPropertyChanged(nameof(PathsThroughElementMaxFlowM3h));
                return;
            }

            long selectedElementId = elementId.GetValueOrDefault();
            IReadOnlyList<PathRow> rows = Paths
                .Where(path => path.ElementIds.Any(id => TryParseElementId(id, out long parsed) && parsed == selectedElementId))
                .ToList();
            Replace(PathsThroughSelectedElement, rows);
            OnPropertyChanged(nameof(PathsThroughElementSummary));
            OnPropertyChanged(nameof(PathsThroughElementMaxLossPa));
            OnPropertyChanged(nameof(PathsThroughElementMaxFlowM3h));
        }

        public string PathsThroughElementSummary
        {
            get
            {
                long? elementId = CurrentSelectedElementId;
                if (!elementId.HasValue)
                {
                    return "Выберите элемент, воздуховод, участок, МС или диагностику с ElementId.";
                }

                return PathsThroughSelectedElement.Count == 0
                    ? $"ElementId {elementId}: через элемент трассы не найдены."
                    : $"ElementId {elementId}: трасс через элемент — {PathsThroughSelectedElement.Count}; номера: {string.Join(", ", PathsThroughSelectedElement.Select(path => path.PathIndex))}.";
            }
        }

        public double PathsThroughElementMaxLossPa => PathsThroughSelectedElement.Count == 0 ? 0 : PathsThroughSelectedElement.Max(path => path.TotalPressureLossPa);

        public double PathsThroughElementMaxFlowM3h => PathsThroughSelectedElement.Count == 0 ? 0 : PathsThroughSelectedElement.Max(path => path.FlowM3hNumeric);

        public string LocalResistanceRecognitionSummary
        {
            get
            {
                IReadOnlyList<LocalResistanceCalculationInfo> locals = AerodynamicSummary?.Paths.SelectMany(path => path.LocalResistances).ToList() ?? new List<LocalResistanceCalculationInfo>();
                int elbowCount = locals.Count(local => local.PathRole.StartsWith("Elbow", StringComparison.OrdinalIgnoreCase));
                int teePassCount = locals.Count(local => local.PathRole == "TeePass");
                int teeBranchCount = locals.Count(local => local.PathRole == "TeeBranch");
                int transitionNarrowingCount = locals.Count(local => local.PathRole == "TransitionNarrowing");
                int transitionExpansionCount = locals.Count(local => local.PathRole == "TransitionExpansion");
                int tapCount = locals.Count(local => local.PathRole == "TapBranch");
                int grilleCount = locals.Count(local => local.PathRole == "Grille");
                int unknownCount = locals.Count(local => string.Equals(local.PathRole, "Unknown", StringComparison.OrdinalIgnoreCase) || local.PathRole.EndsWith("Unknown", StringComparison.OrdinalIgnoreCase));
                return $"Отводы: {elbowCount}; Тройники проход: {teePassCount}; Тройники ответвление: {teeBranchCount}; Переходы сужение: {transitionNarrowingCount}; Переходы расширение: {transitionExpansionCount}; Врезки: {tapCount}; Решётки: {grilleCount}; Не определено: {unknownCount}";
            }
        }

        public int LocalResistanceSummaryCount => DisplayedLocalResistances.Count;

        public double LocalResistanceSummaryLossPa => DisplayedLocalResistances.Sum(local => local.LocalPressureLossPa);

        public int LocalResistanceManualCount => DisplayedLocalResistances.Count(local => local.ManualZeta.HasValue);

        public int LocalResistanceErrorCount => DisplayedLocalResistances.Count(local => !string.IsNullOrWhiteSpace(local.ValidationMessage) || local.ZetaSource == "Не определено" || local.Warnings.Any(w => w.IndexOf("не определ", StringComparison.OrdinalIgnoreCase) >= 0));

        public string LocalResistanceTrackSummary => SelectedPath == null
            ? "Трасса не выбрана"
            : $"Текущая трасса №{SelectedPath.PathIndex} · {SelectedPath.CriticalStatus}";

        public string LocalResistanceHealthText => LocalResistanceErrorCount == 0
            ? "Все коэффициенты определены"
            : $"Для {LocalResistanceErrorCount} элементов требуется проверка коэффициента ζ";

        private void NotifyLocalResistanceUiChanged()
        {
            OnPropertyChanged(nameof(LocalResistanceSummaryCount));
            OnPropertyChanged(nameof(LocalResistanceSummaryLossPa));
            OnPropertyChanged(nameof(LocalResistanceManualCount));
            OnPropertyChanged(nameof(LocalResistanceErrorCount));
            OnPropertyChanged(nameof(LocalResistanceTrackSummary));
            OnPropertyChanged(nameof(LocalResistanceHealthText));
        }

        private void RefreshDisplayedLocalResistances()
        {
            IEnumerable<LocalResistanceCalculationInfo> rows = LocalResistanceScopeMode == "WholeSystem"
                ? AerodynamicSummary?.Paths.SelectMany(path => path.LocalResistances) ?? Enumerable.Empty<LocalResistanceCalculationInfo>()
                : SelectedPath?.Calculation?.LocalResistances ?? Enumerable.Empty<LocalResistanceCalculationInfo>();

            List<LocalResistanceCalculationInfo> displayRows = rows.ToList();
            for (int index = 0; index < displayRows.Count; index++)
            {
                displayRows[index].DisplayNumber = index + 1;
                displayRows[index].PropertyChanged -= LocalResistance_PropertyChanged;
                displayRows[index].PropertyChanged += LocalResistance_PropertyChanged;
            }

            Replace(DisplayedLocalResistances, displayRows);
            NotifyLocalResistanceUiChanged();
        }

        private void NotifySelectionChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }

        private static bool TryParseElementId(string? value, out long elementId)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out elementId);
        }

        private void PickColor(string currentColorHex, Action<string> setColor)
        {
            try
            {
                using var dialog = new Forms.ColorDialog
                {
                    FullOpen = true,
                    Color = ParseDrawingColor(currentColorHex)
                };

                if (dialog.ShowDialog() == Forms.DialogResult.OK)
                {
                    setColor($"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
                    settingsService.Save(Settings);
                    StatusText = "Цвет подсветки сохранён.";
                }
            }
            catch (Exception exception)
            {
                StatusText = $"Не удалось открыть выбор цвета: {exception.Message}";
            }
        }

        private static Drawing.Color ParseDrawingColor(string colorHex)
        {
            try
            {
                return Drawing.ColorTranslator.FromHtml(colorHex);
            }
            catch (Exception)
            {
                return Drawing.Color.White;
            }
        }

        public void ApplyHighlightResult(HighlightResult result)
        {
            HighlightState.ActiveMode = result.ActiveMode;
            HighlightState.ActiveViewId = result.ActiveViewId;
            HighlightState.RequestedElementCount = result.RequestedElementCount;
            HighlightState.HighlightedElementCount = result.HighlightedElementCount;
            HighlightState.SkippedElementCount = result.SkippedElementCount;
            HighlightState.FailedElementCount = result.FailedElementCount;
            HighlightState.RestoredElementCount = result.RestoredElementCount;
            HighlightState.SnapshotCount = result.SnapshotCount;
            HighlightState.SelectionChangedByVentCalc = result.SelectionChangedByVentCalc;
            HighlightState.SelectionElementCountBefore = result.SelectionElementCountBefore;
            HighlightState.SelectionElementCountAfter = result.SelectionElementCountAfter;
            HighlightState.ShowElementsUsed = result.ShowElementsUsed;
            HighlightState.WindowSource = result.WindowSource;
            HighlightState.ActiveDisplayMode = result.ActiveDisplayMode;
            HighlightState.SystemNameAtApply = result.SystemNameAtApply;
            HighlightState.PathIndexAtApply = result.PathIndexAtApply;
            HighlightState.IsCriticalPath = result.IsCriticalPath;
            HighlightState.HighlightedPathElementCount = result.HighlightedPathElementCount;
            HighlightState.DimmedSystemElementCount = result.DimmedSystemElementCount;
            HighlightState.StartElementId = result.StartElementId;
            HighlightState.EndElementId = result.EndElementId;
            HighlightState.LastApplySucceeded = result.LastApplySucceeded;
            HighlightState.LastClearSucceeded = result.LastClearSucceeded;
            HighlightState.HighlightApplySucceeded = result.ActiveMode == HighlightMode.None || result.ApplySucceeded;
            HighlightState.HighlightClearSucceeded = result.ClearSucceeded;
            HighlightState.OriginalOverridesRestored = result.OriginalOverridesRestored;
            HighlightState.Errors = result.Errors.ToList();
            VentCalcSessionState.StoreHighlight(HighlightState);
            NotifyHighlightStateChanged();

            string message = string.IsNullOrWhiteSpace(result.Message)
                ? result.ActiveMode == HighlightMode.None
                    ? $"Подсветка VentCalc очищена: элементов {result.RestoredElementCount}."
                    : $"Подсветка VentCalc применена: элементов {result.HighlightedElementCount}."
                : result.Message;
            StatusText = result.FailedElementCount == 0
                ? message
                : $"{message} Ошибок: {result.FailedElementCount}.";
            LogAction(StatusText);
        }

        private void HighlightSelectedPath()
        {
            if (SelectedPath == null)
            {
                StatusText = "Выберите трассу.";
                return;
            }

            List<long> ids = ParseElementIds(SelectedPath.ElementIds).ToList();
            if (ids.Count == 0)
            {
                StatusText = "В выбранной трассе нет элементов для подсветки.";
                return;
            }

            RequestHighlight(BuildPathHighlightRequest(SelectedPath, false, "PathHighlightWindow"));
        }

        private void HighlightCriticalPath(string windowSource = "PathHighlightWindow")
        {
            if (CriticalPath == null)
            {
                StatusText = "Критическая трасса пока не определена.";
                return;
            }

            List<long> ids = CriticalPath.ElementIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                StatusText = "В критической трассе нет элементов для подсветки.";
                return;
            }

            RequestHighlight(BuildPathHighlightRequest(CriticalPath, true, windowSource));
        }

        private HighlightRequest BuildPathHighlightRequest(PathRow path, bool isCritical, string windowSource)
        {
            List<long> pathIds = ParseElementIds(path.ElementIds).Distinct().ToList();
            long? startId = TryParseElementId(path.StartElementId, out long parsedStart) ? parsedStart : pathIds.FirstOrDefault();
            long? endId = TryParseElementId(path.EndElementId, out long parsedEnd) ? parsedEnd : pathIds.LastOrDefault();
            return BuildPathHighlightRequestCore(
                path.PathIndex,
                path.TotalPressureLossPa,
                pathIds,
                startId,
                endId,
                isCritical,
                windowSource);
        }

        private HighlightRequest BuildPathHighlightRequest(PathCalculationInfo path, bool isCritical, string windowSource)
        {
            List<long> pathIds = path.ElementIds.Distinct().ToList();
            return BuildPathHighlightRequestCore(
                path.PathIndex,
                path.TotalPressureLossPa,
                pathIds,
                path.StartElementId,
                path.EndElementId,
                isCritical,
                windowSource);
        }

        private HighlightRequest BuildPathHighlightRequestCore(int pathIndex, double totalPressureLossPa, List<long> pathIds, long? startId, long? endId, bool isCritical, string windowSource)
        {
            var groups = new List<HighlightElementGroup>();
            bool focus = HighlightDisplayMode == VentCalc.UI.Services.HighlightDisplayMode.Focus;
            List<long> dimmedIds = focus ? GetLoadedSystemElementIds().Except(pathIds).ToList() : new List<long>();
            if (focus)
            {
                groups.Add(CreateHighlightGroup("Система вне трассы", "#9E9E9E", dimmedIds, 1, 82));
                groups.Add(CreateHighlightGroup(isCritical ? "Критическая трасса" : "Выбранная трасса", isCritical ? Settings.CriticalPathColorHex : Settings.SelectedPathColorHex, pathIds, 8, 10));
                if (startId.HasValue && startId.Value > 0)
                {
                    groups.Add(CreateHighlightGroup("Начало трассы", "#00C853", new[] { startId.Value }, 9, 0));
                }

                if (endId.HasValue && endId.Value > 0)
                {
                    groups.Add(CreateHighlightGroup("Конец трассы", "#D50000", new[] { endId.Value }, 9, 0));
                }
            }
            else
            {
                groups.Add(CreateHighlightGroup(isCritical ? "Критическая трасса" : "Выбранная трасса", isCritical ? Settings.CriticalPathColorHex : Settings.SelectedPathColorHex, pathIds, isCritical ? 9 : 8, isCritical ? 20 : 25));
            }

            string startEnd = $"старт {startId?.ToString(CultureInfo.InvariantCulture) ?? "—"}, конец {endId?.ToString(CultureInfo.InvariantCulture) ?? "—"}";
            return new HighlightRequest
            {
                Action = HighlightAction.Apply,
                Mode = isCritical ? HighlightMode.CriticalPath : HighlightMode.SelectedPath,
                SelectElements = false,
                ShowElements = Settings.ZoomToElementOnShow,
                WindowSource = windowSource,
                DisplayMode = HighlightDisplayMode,
                SystemName = SystemName,
                PathIndex = pathIndex,
                IsCriticalPath = isCritical,
                HighlightedPathElementCount = pathIds.Count,
                DimmedSystemElementCount = dimmedIds.Count,
                StartElementId = startId,
                EndElementId = endId,
                StatusMessage = isCritical
                    ? $"Показана критическая трасса №{pathIndex}, потери {totalPressureLossPa:0.###} Па."
                    : $"Подсвечена трасса №{pathIndex}: элементов {pathIds.Count}.",
                Groups = groups.Where(group => group.ElementIds.Count > 0).ToList()
            };
        }

        private IEnumerable<long> GetLoadedSystemElementIds()
        {
            return NetworkInfo?.Elements
                .Select(node => TryParseElementId(node.ElementId, out long id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                ?? Enumerable.Empty<long>();
        }

        private void SelectRelativePath(int offset)
        {
            if (Paths.Count == 0)
            {
                StatusText = "Трассы ещё не загружены.";
                return;
            }

            int currentIndex = SelectedPath == null ? 0 : Paths.IndexOf(SelectedPath);
            int nextIndex = currentIndex < 0 ? 0 : (currentIndex + offset + Paths.Count) % Paths.Count;
            SelectedPath = Paths[nextIndex];
        }

        private void RestoreHighlightState(HighlightStateInfo state)
        {
            HighlightState.ActiveMode = state.ActiveMode;
            HighlightState.ActiveViewId = state.ActiveViewId;
            HighlightState.RequestedElementCount = state.RequestedElementCount;
            HighlightState.HighlightedElementCount = state.HighlightedElementCount;
            HighlightState.SkippedElementCount = state.SkippedElementCount;
            HighlightState.FailedElementCount = state.FailedElementCount;
            HighlightState.RestoredElementCount = state.RestoredElementCount;
            HighlightState.SnapshotCount = state.SnapshotCount;
            HighlightState.SelectionChangedByVentCalc = state.SelectionChangedByVentCalc;
            HighlightState.SelectionElementCountBefore = state.SelectionElementCountBefore;
            HighlightState.SelectionElementCountAfter = state.SelectionElementCountAfter;
            HighlightState.ShowElementsUsed = state.ShowElementsUsed;
            HighlightState.WindowSource = state.WindowSource;
            HighlightState.ActiveDisplayMode = state.ActiveDisplayMode;
            HighlightState.SystemNameAtApply = state.SystemNameAtApply;
            HighlightState.PathIndexAtApply = state.PathIndexAtApply;
            HighlightState.IsCriticalPath = state.IsCriticalPath;
            HighlightState.HighlightedPathElementCount = state.HighlightedPathElementCount;
            HighlightState.DimmedSystemElementCount = state.DimmedSystemElementCount;
            HighlightState.StartElementId = state.StartElementId;
            HighlightState.EndElementId = state.EndElementId;
            HighlightState.LastApplySucceeded = state.LastApplySucceeded;
            HighlightState.LastClearSucceeded = state.LastClearSucceeded;
            HighlightState.HighlightApplySucceeded = state.HighlightApplySucceeded;
            HighlightState.HighlightClearSucceeded = state.HighlightClearSucceeded;
            HighlightState.OriginalOverridesRestored = state.OriginalOverridesRestored;
            HighlightState.Errors = state.Errors.ToList();
            NotifyHighlightStateChanged();
        }

        private void ApplyVelocityHighlight(string windowSource = "VelocityHighlightWindow")
        {
            if (AerodynamicSummary == null)
            {
                StatusText = "Сначала загрузите систему.";
                return;
            }

            Dictionary<long, DuctCalculationInfo> ducts = AerodynamicSummary.Paths
                .SelectMany(path => path.Ducts)
                .Where(duct => duct.ElementId > 0)
                .GroupBy(duct => duct.ElementId)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(duct => duct.VelocityMs).First());

            var belowMin = new List<long>();
            var normal = new List<long>();
            var aboveMax = new List<long>();
            var critical = new List<long>();
            int notCalculated = 0;
            foreach (DuctCalculationInfo duct in ducts.Values)
            {
                if (duct.FlowM3h <= 0 || duct.AreaM2 <= 0 || duct.VelocityMs <= 0 || double.IsNaN(duct.VelocityMs) || double.IsInfinity(duct.VelocityMs))
                {
                    notCalculated++;
                    continue;
                }

                if (duct.VelocityMs < Settings.MinVelocityMs)
                {
                    belowMin.Add(duct.ElementId);
                }
                else if (duct.VelocityMs <= Settings.MaxVelocityMs)
                {
                    normal.Add(duct.ElementId);
                }
                else if (duct.VelocityMs < Settings.CriticalVelocityMs)
                {
                    aboveMax.Add(duct.ElementId);
                }
                else
                {
                    critical.Add(duct.ElementId);
                }
            }

            HighlightState.VelocityGroups.BelowMin = belowMin.Count;
            HighlightState.VelocityGroups.Normal = normal.Count;
            HighlightState.VelocityGroups.AboveMax = aboveMax.Count;
            HighlightState.VelocityGroups.Critical = critical.Count;
            HighlightState.VelocityGroups.NotCalculated = notCalculated;
            NotifyHighlightStateChanged();

            var groups = new List<HighlightElementGroup>
            {
                CreateHighlightGroup($"Ниже {Settings.MinVelocityMs:0.###} м/с", Settings.LowVelocityColorHex, belowMin, 6, 35),
                CreateHighlightGroup($"От {Settings.MinVelocityMs:0.###} до {Settings.MaxVelocityMs:0.###} м/с", Settings.NormalVelocityColorHex, normal, 5, 45),
                CreateHighlightGroup($"Выше {Settings.MaxVelocityMs:0.###} м/с", Settings.HighVelocityColorHex, aboveMax, 7, 30),
                CreateHighlightGroup($"От {Settings.CriticalVelocityMs:0.###} м/с", Settings.CriticalVelocityColorHex, critical, 9, 15)
            };

            RequestHighlight(new HighlightRequest
            {
                Action = HighlightAction.Apply,
                Mode = HighlightMode.Velocity,
                SelectElements = false,
                ShowElements = false,
                WindowSource = windowSource,
                DisplayMode = VentCalc.UI.Services.HighlightDisplayMode.Normal,
                SystemName = SystemName,
                StatusMessage = $"Карта скоростей применена: воздуховодов {belowMin.Count + normal.Count + aboveMax.Count + critical.Count}.",
                Groups = groups.Where(group => group.ElementIds.Count > 0).ToList()
            });
        }

        private void HighlightIssues()
        {
            List<long> ids = GetIssueElementIds();
            if (ids.Count == 0)
            {
                StatusText = "Нет проблемных элементов для подсветки.";
                return;
            }

            HighlightState.IssueElementCount = ids.Count;
            NotifyHighlightStateChanged();
            RequestHighlight(new HighlightRequest
            {
                Action = HighlightAction.Apply,
                Mode = HighlightMode.Issues,
                SelectElements = false,
                ShowElements = Settings.ZoomToElementOnShow,
                WindowSource = "VentCalcCenter",
                DisplayMode = VentCalc.UI.Services.HighlightDisplayMode.Normal,
                SystemName = SystemName,
                StatusMessage = $"Подсвечено проблемных элементов: {ids.Count}.",
                Groups = new List<HighlightElementGroup>
                {
                    new HighlightElementGroup
                    {
                        Name = "Проблемы",
                        ColorHex = Settings.IssueColorHex,
                        LineWeight = 9,
                        Transparency = 10,
                        ElementIds = ids
                    }
                }
            });
        }

        private void ClearHighlight()
        {
            RequestHighlight(new HighlightRequest
            {
                Action = HighlightAction.Clear,
                Mode = HighlightMode.None,
                SelectElements = false,
                ShowElements = false,
                WindowSource = "VentCalc",
                DisplayMode = HighlightDisplayMode,
                SystemName = SystemName,
                StatusMessage = "Подсветка VentCalc очищена."
            });
        }

        private void ResetHighlightColors()
        {
            Settings.LowVelocityColorHex = "#2196F3";
            Settings.NormalVelocityColorHex = "#4CAF50";
            Settings.HighVelocityColorHex = "#FF9800";
            Settings.CriticalVelocityColorHex = "#F44336";
            Settings.SelectedPathColorHex = "#00BCD4";
            Settings.CriticalPathColorHex = "#E91E63";
            Settings.IssueColorHex = "#D50000";
            settingsService.Save(Settings);
            StatusText = "Стандартные цвета подсветки восстановлены.";
        }

        private void RequestHighlight(HighlightRequest request)
        {
            if (request.Action == HighlightAction.Apply && request.RequestedElementCount == 0)
            {
                StatusText = "Нет элементов для подсветки.";
                return;
            }

            if (requestHighlight == null)
            {
                StatusText = "Подсветка доступна из окна VentCalc Center, открытого из Revit.";
                showMessage?.Invoke(StatusText);
                return;
            }

            StatusText = request.Action == HighlightAction.Clear
                ? "Ожидание Revit: очистка подсветки."
                : "Ожидание Revit: применение подсветки.";
            requestHighlight(this, request);
        }

        private static HighlightElementGroup CreateHighlightGroup(string name, string colorHex, IEnumerable<long> elementIds, int lineWeight, int transparency)
        {
            return new HighlightElementGroup
            {
                Name = name,
                ColorHex = colorHex,
                LineWeight = lineWeight,
                Transparency = transparency,
                ElementIds = elementIds.Distinct().ToList()
            };
        }

        private List<long> GetIssueElementIds()
        {
            var ids = new HashSet<long>();
            foreach (VentIssueInfo issue in Issues)
            {
                if (TryParseElementId(issue.ElementId, out long issueElementId))
                {
                    ids.Add(issueElementId);
                }
            }

            if (AerodynamicSummary != null)
            {
                foreach (DuctCalculationInfo duct in AerodynamicSummary.Paths.SelectMany(path => path.Ducts))
                {
                    if (duct.ElementId <= 0)
                    {
                        continue;
                    }

                    bool invalidVelocity = duct.FlowM3h <= 0 || duct.AreaM2 <= 0 || duct.VelocityMs <= 0 || double.IsNaN(duct.VelocityMs) || double.IsInfinity(duct.VelocityMs);
                    if (invalidVelocity || duct.VelocityMs >= Settings.CriticalVelocityMs)
                    {
                        ids.Add(duct.ElementId);
                    }
                }

                foreach (LocalResistanceCalculationInfo local in AerodynamicSummary.Paths.SelectMany(path => path.LocalResistances))
                {
                    bool unknown = string.Equals(local.PathRole, "Unknown", StringComparison.OrdinalIgnoreCase) || local.PathRole.EndsWith("Unknown", StringComparison.OrdinalIgnoreCase);
                    bool missingZeta = string.Equals(local.ZetaSource, "Не определено", StringComparison.OrdinalIgnoreCase) || (local.EffectiveZeta <= 0 && local.AutoZeta <= 0 && !local.ManualZeta.HasValue);
                    if ((unknown || missingZeta) && local.ElementId > 0)
                    {
                        ids.Add(local.ElementId);
                    }
                }
            }

            return ids.OrderBy(id => id).ToList();
        }

        private static IEnumerable<long> ParseElementIds(IEnumerable<string> elementIds)
        {
            foreach (string value in elementIds)
            {
                if (TryParseElementId(value, out long elementId))
                {
                    yield return elementId;
                }
            }
        }

        private void NotifyHighlightStateChanged()
        {
            OnPropertyChanged(nameof(HighlightState));
            OnPropertyChanged(nameof(VelocityBelowMinCount));
            OnPropertyChanged(nameof(VelocityNormalCount));
            OnPropertyChanged(nameof(VelocityAboveMaxCount));
            OnPropertyChanged(nameof(VelocityCriticalCount));
            OnPropertyChanged(nameof(VelocityNotCalculatedCount));
            OnPropertyChanged(nameof(HighlightStatusText));
        }

        private void AcceptRecommendedZeta()
        {
            IReadOnlyList<LocalResistanceCalculationInfo> rows = GetSelectedLocalResistanceRows();
            if (rows.Count == 0)
            {
                StatusText = "Выберите строки МС.";
                return;
            }

            int updated = 0;
            foreach (LocalResistanceCalculationInfo local in rows.Where(local => local.AutoZeta > 0 || local.ZetaSource != "Не определено"))
            {
                local.ManualZeta = local.AutoZeta;
                local.ZetaSource = "Вручную";
                updated++;
            }

            StatusText = updated == 0
                ? "Для выбранных МС нет рекомендованных ζ."
                : $"Рекомендованные ζ приняты вручную для {updated} строк. Нажмите «Пересчитать с ручными ζ».";
            LogAction(StatusText);
        }

        private void RecalculateWithManualZeta()
        {
            if (AerodynamicSummary == null)
            {
                StatusText = "Сначала загрузите систему.";
                return;
            }

            int? previousCriticalPathIndex = CriticalPath?.PathIndex;
            double previousCriticalPressurePa = CriticalPath?.TotalPressureLossPa ?? 0;
            int changedRowsCount = AerodynamicSummary.Paths.SelectMany(path => path.LocalResistances).Count(local => local.ManualZeta.HasValue);

            foreach (PathCalculationInfo path in AerodynamicSummary.Paths)
            {
                foreach (LocalResistanceCalculationInfo local in path.LocalResistances)
                {
                    if (local.ManualZeta.HasValue)
                    {
                        double manualZeta = local.ManualZeta.GetValueOrDefault();
                        local.EffectiveZeta = manualZeta;
                        local.Zeta = manualZeta;
                        local.Source = "Вручную";
                        local.ZetaSource = "Вручную";
                    }
                    else
                    {
                        local.EffectiveZeta = local.OriginalEffectiveZeta;
                        local.Zeta = local.OriginalEffectiveZeta;
                        local.Source = string.IsNullOrWhiteSpace(local.OriginalZetaSource) ? local.Source : local.OriginalZetaSource;
                        local.ZetaSource = local.Source;
                    }

                    local.LocalPressureLossPa = local.EffectiveZeta * local.DynamicPressurePa;
                }

                path.TotalLocalPressureLossPa = path.LocalResistances.Sum(local => local.LocalPressureLossPa);
                path.TotalPressureLossPa = path.TotalFrictionPressureLossPa + path.TotalLocalPressureLossPa;
            }

            CriticalPath = AerodynamicSummary.CriticalPathByTotalPressure;
            PathRow? previouslySelected = SelectedPath;
            Replace(Paths, BuildPathRows(PathSummary, AerodynamicSummary));
            SelectedPath = previouslySelected == null
                ? Paths.FirstOrDefault(path => path.PathIndex == CriticalPath?.PathIndex) ?? Paths.FirstOrDefault()
                : Paths.FirstOrDefault(path => path.PathIndex == previouslySelected.PathIndex) ?? Paths.FirstOrDefault();
            Replace(Issues, SortIssues(BuildIssues(new VentCalcCenterData
            {
                Success = true,
                NetworkInfo = NetworkInfo,
                PathSummary = PathSummary,
                AerodynamicSummary = AerodynamicSummary
            })));
            RefreshFilteredIssues();

            OnPropertyChanged(nameof(CriticalPathText));
            OnPropertyChanged(nameof(NearCriticalPathIndexes));
            OnPropertyChanged(nameof(CriticalPathIndex));
            OnPropertyChanged(nameof(CriticalPathDisplay));
            OnPropertyChanged(nameof(CriticalPathTotalPressureLossPa));
            OnPropertyChanged(nameof(CriticalPathTotalWithReservePa));
            OnPropertyChanged(nameof(SelectedPathLocalPressureLossPa));
            OnPropertyChanged(nameof(SelectedPathTotalPressureLossPa));
            OnPropertyChanged(nameof(SelectedPathTotalWithReservePa));
            RefreshDisplayedLocalResistances();
            OnPropertyChanged(nameof(LocalResistanceRecognitionSummary));
            var action = new ZetaRecalculationActionInfo
            {
                Timestamp = DateTime.Now,
                ChangedRowsCount = changedRowsCount,
                PreviousCriticalPathIndex = previousCriticalPathIndex,
                NewCriticalPathIndex = CriticalPath?.PathIndex,
                PreviousCriticalPressurePa = previousCriticalPressurePa,
                NewCriticalPressurePa = CriticalPath?.TotalPressureLossPa ?? 0
            };
            ZetaRecalculationActions.Add(action);
            StatusText = $"Пересчёт выполнен: ручных ζ {changedRowsCount}, критическая трасса {action.NewCriticalPathIndex}, итог {action.NewCriticalPressurePa:0.###} Па.";
            LogAction(StatusText);
        }


        private void BuildProjectZetaCatalogRows()
        {
            string[] roles = new[]
            {
                "Elbow15", "Elbow30", "Elbow45", "Elbow60", "Elbow90",
                "TeePass", "TeeBranch", "CrossPass", "CrossBranch",
                "TransitionNarrowing", "TransitionExpansion", "TapBranch",
                "Grille", "Hood", "Damper", "FireDamper", "BackdraftDamper"
            };
            var locals = AerodynamicSummary?.Paths.SelectMany(path => path.LocalResistances).ToList() ?? new List<LocalResistanceCalculationInfo>();
            Replace(ProjectZetaCatalogRows, roles.Select(role =>
            {
                LocalResistanceCalculationInfo? first = locals.FirstOrDefault(local => local.PathRole == role);
                double autoZeta = first?.AutoZeta ?? 0;
                double? projectZeta = first?.ProjectCatalogZeta;
                return new ProjectZetaCatalogRow(role, autoZeta, projectZeta, locals.Count(local => local.PathRole == role), locals.Where(local => local.PathRole == role).Select(local => local.ElementId).Distinct().Count());
            }));
            OnPropertyChanged(nameof(VisibleProjectZetaCatalogRows));
        }

        private void SaveManualZetaOverrides()
        {
            IReadOnlyList<LocalResistanceCalculationInfo> rows = GetChangedLocalResistanceRows();
            if (rows.Count == 0)
            {
                StatusText = "Нет изменённых ручных ζ для сохранения.";
                return;
            }

            writeZetaToRevitComments?.Invoke(this, rows, ZetaOverrideRequestMode.SaveOverrides);
            int dataStorageCount = rows.Count(row => row.PathDependent);
            int commentCount = rows.Count - dataStorageCount;
            StatusText = $"Ожидание Revit: сохранение переопределений ζ. Комментарии: {commentCount}; DataStorage: {dataStorageCount}.";
        }

        private void ClearSelectedManualZeta()
        {
            IReadOnlyList<LocalResistanceCalculationInfo> rows = GetSelectedLocalResistanceRows();
            if (rows.Count == 0)
            {
                StatusText = "Выберите строки МС.";
                return;
            }

            var affectedRows = ExpandMatchingLocalResistanceRows(rows).ToList();
            foreach (LocalResistanceCalculationInfo local in affectedRows)
            {
                local.ManualZeta = null;
                local.EffectiveZeta = local.OriginalEffectiveZeta;
                local.Zeta = local.OriginalEffectiveZeta;
                local.Source = string.IsNullOrWhiteSpace(local.OriginalZetaSource) ? local.Source : local.OriginalZetaSource;
                local.ZetaSource = local.Source;
                local.LocalPressureLossPa = local.EffectiveZeta * local.DynamicPressurePa;
            }

            RecalculateWithManualZeta();
            StatusText = $"Очищены несохранённые ручные ζ для выбранных применений: {affectedRows.Count}.";
            LogAction(StatusText);
        }

        private void ResetSelectedZetaToAuto()
        {
            IReadOnlyList<LocalResistanceCalculationInfo> rows = GetSelectedLocalResistanceRows();
            if (rows.Count == 0)
            {
                StatusText = "Выберите строки МС.";
                return;
            }

            writeZetaToRevitComments?.Invoke(this, rows, ZetaOverrideRequestMode.ResetToAuto);
            StatusText = $"Ожидание Revit: возврат к Auto для выбранных строк: {rows.Count}.";
        }

        private void ResetProjectToAuto()
        {
            showMessage?.Invoke("Будут удалены все VentCalc-переопределения ζ и все фрагменты z=/ζ=/zeta= из комментариев поддерживаемых фитингов проекта. Остальной текст комментариев сохранится.");
            writeZetaToRevitComments?.Invoke(this, Array.Empty<LocalResistanceCalculationInfo>(), ZetaOverrideRequestMode.ResetProjectToAuto);
            StatusText = "Ожидание Revit: возврат всего проекта к Auto.";
        }

        private void SaveProjectZetaCatalog()
        {
            IReadOnlyList<ProjectZetaCatalogRow> invalidRows = ProjectZetaCatalogRows.Where(row => !string.IsNullOrWhiteSpace(row.ValidationMessage)).ToList();
            if (invalidRows.Count > 0)
            {
                StatusText = $"Каталог ζ проекта содержит некорректные значения: {invalidRows.Count}.";
                return;
            }

            writeZetaToRevitComments?.Invoke(this, Array.Empty<LocalResistanceCalculationInfo>(), ZetaOverrideRequestMode.SaveProjectCatalog);
            StatusText = "Ожидание Revit: сохранение каталога ζ проекта.";
        }

        private void ResetSelectedCatalogRoleToAuto()
        {
            if (SelectedProjectZetaCatalogRow == null)
            {
                StatusText = "Выберите роль каталога ζ проекта.";
                return;
            }

            SelectedProjectZetaCatalogRow.ProjectZetaText = string.Empty;
            SaveProjectZetaCatalog();
        }

        private void ResetWholeCatalogToAuto()
        {
            foreach (ProjectZetaCatalogRow row in ProjectZetaCatalogRows)
            {
                row.ProjectZetaText = string.Empty;
            }

            SaveProjectZetaCatalog();
        }

        public void SetSelectedLocalResistanceRows(IEnumerable<LocalResistanceCalculationInfo> rows)
        {
            Replace(SelectedLocalResistanceRows, rows);
            CommandManager.InvalidateRequerySuggested();
        }

        private IEnumerable<LocalResistanceCalculationInfo> ExpandMatchingLocalResistanceRows(IEnumerable<LocalResistanceCalculationInfo> sourceRows)
        {
            if (AerodynamicSummary == null)
            {
                return sourceRows;
            }

            var keys = sourceRows
                .Select(row => row.PathDependent ? row.OverrideKey : row.ElementId.ToString(CultureInfo.InvariantCulture))
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.Ordinal);

            return AerodynamicSummary.Paths
                .SelectMany(path => path.LocalResistances)
                .Where(row => keys.Contains(row.PathDependent ? row.OverrideKey : row.ElementId.ToString(CultureInfo.InvariantCulture)));
        }

        private IReadOnlyList<LocalResistanceCalculationInfo> GetChangedLocalResistanceRows()
        {
            return AerodynamicSummary?.Paths
                .SelectMany(path => path.LocalResistances)
                .Where(local => local.ManualZeta.HasValue && Math.Abs(local.ManualZeta.GetValueOrDefault() - local.OriginalEffectiveZeta) > 0.0001)
                .GroupBy(local => local.PathDependent ? local.OverrideKey : local.ElementId.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList() ?? new List<LocalResistanceCalculationInfo>();
        }

        private IReadOnlyList<LocalResistanceCalculationInfo> GetSelectedLocalResistanceRows()
        {
            if (SelectedLocalResistanceRows.Count > 0)
            {
                return SelectedLocalResistanceRows.ToList();
            }

            return SelectedPathLocalResistance == null
                ? Array.Empty<LocalResistanceCalculationInfo>()
                : new[] { SelectedPathLocalResistance };
        }

        public void CompleteZetaCommentWrite(IReadOnlyCollection<ZetaWriteActionInfo> actions, string message)
        {
            foreach (ZetaWriteActionInfo action in actions)
            {
                ZetaWriteActions.Add(action);
                VentCalcActionLogService.Append($"Сохранение ζ: ElementId={action.ElementId}; ζ={action.RequestedZeta:0.###}; storage={action.OverrideStorageType}; key='{action.OverrideKey}'; ok={action.WriteSucceeded}; verified={action.VerifiedAfterCommit}; actual='{action.ActualCommentAfterCommit}'; error={action.ErrorMessage}");
            }

            foreach (LocalResistanceCalculationInfo local in AerodynamicSummary?.Paths.SelectMany(path => path.LocalResistances) ?? Enumerable.Empty<LocalResistanceCalculationInfo>())
            {
                ZetaWriteActionInfo? action = actions.LastOrDefault(item => item.PathDependent
                    ? string.Equals(item.OverrideKey, local.OverrideKey, StringComparison.Ordinal)
                    : item.ElementId == local.ElementId);
                if (action == null)
                {
                    continue;
                }

                local.WasWrittenToRevitComment = action.WriteSucceeded && action.OverrideStorageType == "Comment";
                local.WasSavedToVentCalcStorage = action.WriteSucceeded && action.OverrideStorageType == "DataStorage";
                local.LastWriteError = action.ErrorMessage;
                local.ManualZeta = null;
                if (action.OverrideStorageType.Contains("Reset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (action.WriteSucceeded)
                {
                    local.EffectiveZeta = action.RequestedZeta;
                    local.Zeta = action.RequestedZeta;
                    local.ZetaSource = action.OverrideStorageType == "DataStorage" ? "Переопределение VentCalc" : "Комментарии";
                    local.OverrideStorageType = action.OverrideStorageType;
                    local.OriginalEffectiveZeta = local.EffectiveZeta;
                    local.OriginalZetaSource = local.ZetaSource;
                    local.LocalPressureLossPa = local.EffectiveZeta * local.DynamicPressurePa;
                    action.EffectiveZetaAfterReload = local.EffectiveZeta;
                    action.SourceAfterReload = local.ZetaSource;
                }
            }

            StatusText = message;
            LogAction(message);
        }

        private void SaveSettings()
        {
            if (!ValidateVelocitySettings() || !ValidateHighlightColorSettings())
            {
                return;
            }

            settingsService.Save(Settings);
            NotifySettingsChanged();
            StatusText = string.IsNullOrWhiteSpace(settingsService.LastWarning) ? "Настройки сохранены." : settingsService.LastWarning;
        }

        private void ResetSettings()
        {
            Forms.DialogResult result = Forms.MessageBox.Show(
                "Будут восстановлены стандартные настройки VentCalc. Расчёт текущей системы будет обновлён. Продолжить?",
                "VentCalc",
                Forms.MessageBoxButtons.YesNo,
                Forms.MessageBoxIcon.Question);

            if (result != Forms.DialogResult.Yes)
            {
                return;
            }

            Settings = VentCalcSettings.CreateDefault();
            settingsService.Save(Settings);
            OnPropertyChanged(nameof(Settings));
            NotifySettingsChanged();
            RecalculateLoadedSystemIfAvailable();
            StatusText = "Настройки VentCalc восстановлены.";
            LogAction(StatusText);
        }

        private void ResetSettingsSection(string section)
        {
            VentCalcSettings defaults = VentCalcSettings.CreateDefault();
            switch (section)
            {
                case "Скорости":
                    Settings.MinVelocityMs = defaults.MinVelocityMs;
                    Settings.MaxVelocityMs = defaults.MaxVelocityMs;
                    Settings.CriticalVelocityMs = defaults.CriticalVelocityMs;
                    break;
                case "Подсветка":
                    Settings.LowVelocityColorHex = defaults.LowVelocityColorHex;
                    Settings.NormalVelocityColorHex = defaults.NormalVelocityColorHex;
                    Settings.HighVelocityColorHex = defaults.HighVelocityColorHex;
                    Settings.CriticalVelocityColorHex = defaults.CriticalVelocityColorHex;
                    Settings.CriticalPathColorHex = defaults.CriticalPathColorHex;
                    Settings.IssueColorHex = defaults.IssueColorHex;
                    Settings.ZoomToElementOnShow = defaults.ZoomToElementOnShow;
                    break;
                case "Интерфейс":
                    Settings.UiDefaultTabAfterLoad = defaults.UiDefaultTabAfterLoad;
                    Settings.RememberWindowPlacement = defaults.RememberWindowPlacement;
                    Settings.ConfirmBulkZetaChanges = defaults.ConfirmBulkZetaChanges;
                    break;
                case "Трассировка":
                    StatusText = "В этом разделе пока нет подключённых пользовательских настроек.";
                    return;
                default:
                    Settings.AirDensityKgM3 = defaults.AirDensityKgM3;
                    Settings.AirDynamicViscosityPaS = defaults.AirDynamicViscosityPaS;
                    Settings.RoughnessMm = defaults.RoughnessMm;
                    Settings.PressureReservePercent = defaults.PressureReservePercent;
                    break;
            }

            if (!ValidateVelocitySettings() || !ValidateHighlightColorSettings())
            {
                return;
            }

            settingsService.Save(Settings);
            NotifySettingsChanged();
            RecalculateLoadedSystemIfAvailable();
            StatusText = "Раздел настроек восстановлен.";
            LogAction(StatusText);
        }

        private bool ValidateVelocitySettings()
        {
            if (Settings.MinVelocityMs < Settings.MaxVelocityMs && Settings.MaxVelocityMs < Settings.CriticalVelocityMs)
            {
                return true;
            }

            const string message = "Проверьте пороги скоростей: минимальная должна быть меньше максимальной, а максимальная меньше критической.";
            StatusText = message;
            showMessage?.Invoke(message);
            return false;
        }

        private bool ValidateHighlightColorSettings()
        {
            string[] values =
            {
                Settings.LowVelocityColorHex,
                Settings.NormalVelocityColorHex,
                Settings.HighVelocityColorHex,
                Settings.CriticalVelocityColorHex,
                Settings.CriticalPathColorHex,
                Settings.IssueColorHex
            };

            if (values.All(IsValidColorHex))
            {
                return true;
            }

            const string message = "Проверьте цвета подсветки: используйте HEX в формате #RRGGBB.";
            StatusText = message;
            showMessage?.Invoke(message);
            return false;
        }

        private static bool IsValidColorHex(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string hex = value.Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal))
            {
                hex = hex.Substring(1);
            }

            return hex.Length == 6 && hex.All(Uri.IsHexDigit);
        }

        public void HighlightCriticalPathFromRibbon()
        {
            HighlightCriticalPath("Ribbon: Критическая трасса");
        }

        public void ApplyVelocityHighlightFromRibbon()
        {
            ApplyVelocityHighlight("Ribbon: Карта скоростей");
        }

        private void NotifySettingsChanged()
        {
            OnPropertyChanged(nameof(SelectedPathTotalWithReservePa));
            OnPropertyChanged(nameof(CriticalPathTotalWithReservePa));
        }

        private void RecalculateLoadedSystemIfAvailable()
        {
            if (LastLoadedElementId.HasValue)
            {
                RequestLoadSelectedSystem(VentCalcLoadRequestMode.LastLoadedElement);
            }
        }


        private void GenerateVerificationReport()
        {
            try
            {
                VentCalcDiagnosticReportResult result = VentCalcDiagnosticReportService.Generate(this);
                LastReportTxtPath = result.TxtPath;
                LastReportJsonPath = result.JsonPath;
                ReportPreviewText = result.PreviewText;
                StatusText = $"Отчёт для проверки сохранён: {result.TxtPath}";
                try
                {
                    System.Windows.Clipboard.SetText(result.TxtPath);
                }
                catch (Exception)
                {
                    // Clipboard is optional; report generation must not fail because of it.
                }
            }
            catch (Exception exception)
            {
                StatusText = $"Не удалось сформировать отчёт: {exception.Message}";
                reportException?.Invoke(exception);
            }
        }

        private void OpenReportsFolder()
        {
            try
            {
                string reportsDirectory = VentCalcDiagnosticReportService.GetReportsDirectory();
                Directory.CreateDirectory(reportsDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = reportsDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                StatusText = $"Не удалось открыть папку отчётов: {exception.Message}";
                reportException?.Invoke(exception);
            }
        }

        private void ShowStub(string message)
        {
            StatusText = message;
            showMessage?.Invoke(message);
        }

        private void LogAction(string message)
        {
            LastActionMessage = message;
            VentCalcActionLogService.Append(message);
        }

        private static string ExtractElementId(string value)
        {
            int separatorIndex = value.IndexOf('|');
            return separatorIndex >= 0 ? value.Substring(0, separatorIndex).Trim() : value.Trim();
        }

        private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
        {
            collection.Clear();
            foreach (T item in items)
            {
                collection.Add(item);
            }
        }
    }

    public enum ZetaOverrideRequestMode
    {
        SaveOverrides,
        ResetToAuto,
        ResetProjectToAuto,
        SaveProjectCatalog
    }

    public enum ZetaVerificationMode
    {
        WriteExpectedZeta,
        RemoveZetaToken,
        RemoveDataStorageOverride,
        ClearProjectCatalogValue
    }

    public enum VentCalcLoadRequestMode
    {
        SelectedElement,
        LastLoadedElement,
        SystemCatalog
    }

    public sealed class VentCalcCenterData
    {
        public VentElementInfo? SelectedElementInfo { get; set; }

        public VentNetworkInfo? NetworkInfo { get; set; }

        public VentPathSummary? PathSummary { get; set; }

        public AerodynamicCalculationSummary? AerodynamicSummary { get; set; }

        public string ReportText { get; set; } = string.Empty;

        public string RevitVersion { get; set; } = "—";

        public string RevitFilePath { get; set; } = "—";

        public DateTime LoadedAt { get; set; } = DateTime.Now;

        public bool Success { get; set; }

        public bool IsUserSelectionWarning { get; set; }

        public string ErrorMessage { get; set; } = string.Empty;

        public List<string> Warnings { get; set; } = new List<string>();

        public List<VentSystemCatalogItem> SystemCatalog { get; set; } = new List<VentSystemCatalogItem>();

        public string LoadMode { get; set; } = string.Empty;

        public string SelectedSystemName { get; set; } = string.Empty;

        public string SelectedSystemType { get; set; } = string.Empty;

        public int SystemComponentCount { get; set; } = 1;
    }

    public sealed class NetworkElementRow
    {
        public string ElementId { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public string Size { get; set; } = string.Empty;

        public string FlowM3h { get; set; } = string.Empty;

        public double LengthM { get; set; }

        public string SystemName { get; set; } = string.Empty;

        public string ConnectedElementIds { get; set; } = string.Empty;

        public string Warnings { get; set; } = string.Empty;
    }

    public sealed class PathRow
    {
        public const double NearCriticalTolerancePa = 1.0;
        public const double NearCriticalTolerancePercent = 2.0;

        public PathRow(VentPathInfo path, PathCalculationInfo? calculation, int? criticalPathIndex, double criticalPressureLossPa)
        {
            Path = path;
            Calculation = calculation;
            IsCritical = criticalPathIndex.HasValue && path.PathIndex == criticalPathIndex.Value;
            PressureLossDeltaFromCriticalPa = Math.Max(0, criticalPressureLossPa - TotalPressureLossPa);
            PressureLossDeltaFromCriticalPercent = criticalPressureLossPa > 0 ? PressureLossDeltaFromCriticalPa / criticalPressureLossPa * 100.0 : 0;
            IsNearCritical = !IsCritical
                && criticalPressureLossPa > 0
                && (PressureLossDeltaFromCriticalPa <= NearCriticalTolerancePa || PressureLossDeltaFromCriticalPercent <= NearCriticalTolerancePercent);
        }

        public VentPathInfo Path { get; }

        public PathCalculationInfo? Calculation { get; }

        public int PathIndex => Path.PathIndex;

        public string StartElementId => Path.StartElementId;

        public string EndElementId => Path.EndElementId;

        public int TotalElementCount => Path.TotalElementCount;

        public int DuctCount => Path.DuctCount;

        public int FittingCount => Path.FittingCount;

        public int TerminalCount => Path.TerminalCount;

        public double TotalDuctLengthM => Path.TotalDuctLengthM;

        public string FlowM3h => Path.FlowRangeM3h;

        public double FlowM3hNumeric => TryParseFlow(Path.MaxFlowM3h);

        public double TotalPressureLossPa => Calculation?.TotalPressureLossPa ?? 0;

        public bool IsCritical { get; }

        public bool IsNearCritical { get; }

        public double PressureLossDeltaFromCriticalPa { get; }

        public double PressureLossDeltaFromCriticalPercent { get; }

        public string CriticalStatus => IsCritical ? "Критическая" : IsNearCritical ? "Почти критическая" : "Обычная";


        public IReadOnlyList<string> ElementIds => Path.ElementIds;

        private static double TryParseFlow(string value)
        {
            string number = new string((value ?? string.Empty).Replace(',', '.').Where(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray());
            return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;
        }
    }

    public sealed class ProjectZetaCatalogRow : NotifyObject
    {
        private double? projectZeta;
        private string projectZetaText = string.Empty;
        private bool isDirty;
        private string validationMessage = string.Empty;

        public ProjectZetaCatalogRow(string pathRole, double autoZeta, double? projectZeta, int applicationCount, int systemCount)
        {
            PathRole = pathRole;
            AutoZeta = autoZeta;
            this.projectZeta = projectZeta;
            projectZetaText = projectZeta.HasValue ? projectZeta.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
            ApplicationCount = applicationCount;
            SystemCount = systemCount;
        }

        public string PathRole { get; }

        public string LocalizedRole => PathRole switch
        {
            "Elbow15" => "Отвод 15°",
            "Elbow30" => "Отвод 30°",
            "Elbow45" => "Отвод 45°",
            "Elbow60" => "Отвод 60°",
            "Elbow90" => "Отвод 90°",
            "TeePass" => "Тройник — проход",
            "TeeBranch" => "Тройник — ответвление",
            "CrossPass" => "Крестовина — проход",
            "CrossBranch" => "Крестовина — ответвление",
            "TransitionNarrowing" => "Переход — сужение",
            "TransitionExpansion" => "Переход — расширение",
            "TapBranch" => "Врезка",
            "Grille" => "Решётка",
            "Hood" => "Зонт",
            "Damper" => "Клапан",
            "FireDamper" => "Противопожарный клапан",
            "BackdraftDamper" => "Обратный клапан",
            _ => PathRole
        };

        public double AutoZeta { get; }

        public double? ProjectZeta
        {
            get => projectZeta;
            set
            {
                if (SetProperty(ref projectZeta, value))
                {
                    projectZetaText = value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
                    IsDirty = true;
                    OnPropertyChanged(nameof(ProjectZetaText));
                    OnPropertyChanged(nameof(EffectiveProjectZeta));
                    OnPropertyChanged(nameof(Status));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string ProjectZetaText
        {
            get => projectZetaText;
            set
            {
                string normalized = value ?? string.Empty;
                if (!SetProperty(ref projectZetaText, normalized))
                {
                    return;
                }

                IsDirty = true;
                CommandManager.InvalidateRequerySuggested();
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    projectZeta = null;
                    ValidationMessage = string.Empty;
                    OnPropertyChanged(nameof(ProjectZeta));
                    OnPropertyChanged(nameof(EffectiveProjectZeta));
                    OnPropertyChanged(nameof(Status));
                    return;
                }

                if (!double.TryParse(normalized.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || parsed < 0)
                {
                    ValidationMessage = "Некорректное значение ζ";
                    return;
                }

                projectZeta = parsed;
                ValidationMessage = string.Empty;
                OnPropertyChanged(nameof(ProjectZeta));
                OnPropertyChanged(nameof(EffectiveProjectZeta));
                OnPropertyChanged(nameof(Status));
            }
        }

        public bool IsDirty
        {
            get => isDirty;
            set => SetProperty(ref isDirty, value);
        }

        public string ValidationMessage
        {
            get => validationMessage;
            set
            {
                if (SetProperty(ref validationMessage, value))
                {
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public double EffectiveProjectZeta => ProjectZeta ?? AutoZeta;

        public int ApplicationCount { get; }

        public int SystemCount { get; }

        public string Status => string.IsNullOrWhiteSpace(ValidationMessage)
            ? ProjectZeta.HasValue ? "Каталог проекта" : "Auto"
            : ValidationMessage;
    }

    public sealed class ZetaWriteActionInfo
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public long ElementId { get; set; }
        public int PathIndex { get; set; }
        public string OldComment { get; set; } = string.Empty;
        public string NewComment { get; set; } = string.Empty;
        public double RequestedZeta { get; set; }
        public bool ParameterFound { get; set; }
        public string ParameterName { get; set; } = string.Empty;
        public bool ParameterIsReadOnly { get; set; }
        public string StorageType { get; set; } = string.Empty;
        public string OverrideStorageType { get; set; } = string.Empty;
        public ZetaVerificationMode VerificationMode { get; set; } = ZetaVerificationMode.WriteExpectedZeta;
        public bool ZetaTokenExistsAfterCommit { get; set; }
        public string OverrideKey { get; set; } = string.Empty;
        public bool PathDependent { get; set; }
        public bool WriteSucceeded { get; set; }
        public bool VerifiedAfterCommit { get; set; }
        public string ActualCommentAfterCommit { get; set; } = string.Empty;
        public double EffectiveZetaAfterReload { get; set; }
        public string SourceAfterReload { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public sealed class ZetaRecalculationActionInfo
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public int ChangedRowsCount { get; set; }
        public int? PreviousCriticalPathIndex { get; set; }
        public int? NewCriticalPathIndex { get; set; }
        public double PreviousCriticalPressurePa { get; set; }
        public double NewCriticalPressurePa { get; set; }
    }

    public sealed class VentCalcSettings : NotifyObject
    {
        private double airDensityKgM3 = 1.2;
        private double airDynamicViscosityPaS = 0.0000181;
        private double roughnessMm = 0.1;
        private double pressureReservePercent;
        private double minVelocityMs = 1;
        private double maxVelocityMs = 8;
        private double criticalVelocityMs = 12;
        private string lowVelocityColorHex = "#2196F3";
        private string normalVelocityColorHex = "#4CAF50";
        private string highVelocityColorHex = "#FF9800";
        private string criticalVelocityColorHex = "#F44336";
        private string selectedPathColorHex = "#00BCD4";
        private string criticalPathColorHex = "#E91E63";
        private string issueColorHex = "#D50000";
        private int settingsSchemaVersion = 1;
        private string uiDefaultTabAfterLoad = "Расчёт";
        private bool rememberWindowPlacement;
        private bool zoomToElementOnShow = true;
        private bool confirmBulkZetaChanges = true;

        public double AirDensityKgM3
        {
            get => airDensityKgM3;
            set => SetProperty(ref airDensityKgM3, value);
        }

        public double AirDynamicViscosityPaS
        {
            get => airDynamicViscosityPaS;
            set
            {
                if (SetProperty(ref airDynamicViscosityPaS, value))
                {
                    OnPropertyChanged(nameof(AirDynamicViscosityText));
                }
            }
        }

        public string AirDynamicViscosityText
        {
            get => AirDynamicViscosityPaS.ToString("0.########E+0", CultureInfo.InvariantCulture);
            set
            {
                if (TryParseDouble(value, out double parsed))
                {
                    AirDynamicViscosityPaS = parsed;
                    OnPropertyChanged(nameof(AirDynamicViscosityText));
                }
            }
        }

        public double RoughnessMm
        {
            get => roughnessMm;
            set => SetProperty(ref roughnessMm, value);
        }

        public double RoughnessM
        {
            get => RoughnessMm / 1000.0;
            set => RoughnessMm = value * 1000.0;
        }

        public double PressureReservePercent
        {
            get => pressureReservePercent;
            set => SetProperty(ref pressureReservePercent, value);
        }

        public double ReservePercent
        {
            get => PressureReservePercent;
            set => PressureReservePercent = value;
        }

        public double MinVelocityMs
        {
            get => minVelocityMs;
            set => SetProperty(ref minVelocityMs, value);
        }

        public double MaxVelocityMs
        {
            get => maxVelocityMs;
            set => SetProperty(ref maxVelocityMs, value);
        }

        public double CriticalVelocityMs
        {
            get => criticalVelocityMs;
            set => SetProperty(ref criticalVelocityMs, value);
        }

        public string LowVelocityColorHex
        {
            get => lowVelocityColorHex;
            set => SetProperty(ref lowVelocityColorHex, NormalizeColorHex(value, "#2196F3"));
        }

        public string NormalVelocityColorHex
        {
            get => normalVelocityColorHex;
            set => SetProperty(ref normalVelocityColorHex, NormalizeColorHex(value, "#4CAF50"));
        }

        public string HighVelocityColorHex
        {
            get => highVelocityColorHex;
            set => SetProperty(ref highVelocityColorHex, NormalizeColorHex(value, "#FF9800"));
        }

        public string CriticalVelocityColorHex
        {
            get => criticalVelocityColorHex;
            set => SetProperty(ref criticalVelocityColorHex, NormalizeColorHex(value, "#F44336"));
        }

        public string SelectedPathColorHex
        {
            get => selectedPathColorHex;
            set => SetProperty(ref selectedPathColorHex, NormalizeColorHex(value, "#00BCD4"));
        }

        public string CriticalPathColorHex
        {
            get => criticalPathColorHex;
            set => SetProperty(ref criticalPathColorHex, NormalizeColorHex(value, "#E91E63"));
        }

        public string IssueColorHex
        {
            get => issueColorHex;
            set => SetProperty(ref issueColorHex, NormalizeColorHex(value, "#D50000"));
        }

        public int SettingsSchemaVersion
        {
            get => settingsSchemaVersion;
            set => SetProperty(ref settingsSchemaVersion, value <= 0 ? 1 : value);
        }

        public string UiDefaultTabAfterLoad
        {
            get => uiDefaultTabAfterLoad;
            set => SetProperty(ref uiDefaultTabAfterLoad, NormalizeDefaultTab(value));
        }

        public bool RememberWindowPlacement
        {
            get => rememberWindowPlacement;
            set => SetProperty(ref rememberWindowPlacement, value);
        }

        public bool ZoomToElementOnShow
        {
            get => zoomToElementOnShow;
            set => SetProperty(ref zoomToElementOnShow, value);
        }

        public bool ConfirmBulkZetaChanges
        {
            get => confirmBulkZetaChanges;
            set => SetProperty(ref confirmBulkZetaChanges, value);
        }

        public static VentCalcSettings CreateDefault()
        {
            return new VentCalcSettings();
        }

        private static bool TryParseDouble(string value, out double parsed)
        {
            return double.TryParse(value?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
        }

        private static string NormalizeDefaultTab(string? value)
        {
            return value == "Местные сопротивления" || value == "Проверки" ? value : "Расчёт";
        }

        private static string NormalizeColorHex(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string trimmed = value.Trim();
            return trimmed.StartsWith("#", StringComparison.Ordinal) ? trimmed : $"#{trimmed}";
        }

        public AerodynamicSettings ToAerodynamicSettings()
        {
            return new AerodynamicSettings
            {
                AirDensityKgM3 = AirDensityKgM3,
                AirDynamicViscosityPaS = AirDynamicViscosityPaS,
                RoughnessM = RoughnessM
            };
        }
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> execute;
        private readonly Predicate<object?>? canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter)
        {
            return canExecute?.Invoke(parameter) ?? true;
        }

        public void Execute(object? parameter)
        {
            execute(parameter);
        }
    }

    public abstract class NotifyObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
