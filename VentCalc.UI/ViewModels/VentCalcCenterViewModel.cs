using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VentCalc.Core.Models;
using VentCalc.UI.Services;

namespace VentCalc.UI.ViewModels
{
    public sealed class VentCalcCenterViewModel : NotifyObject
    {
        private readonly Func<VentCalcCenterData> loadSelectedSystem;
        private readonly Action<long>? selectElementInRevit;
        private readonly Action<string>? showMessage;
        private readonly Action<Exception>? reportException;
        private readonly VentCalcSettingsService settingsService;
        private NetworkElementRow? selectedNetworkElement;
        private PathRow? selectedPath;
        private string selectedElementId = "—";
        private string systemName = "—";
        private string systemType = "—";
        private string direction = "—";
        private string directionReason = "—";
        private string statusText = "Выберите элемент вентиляционной системы в Revit и нажмите «Загрузить выбранную систему».";
        private PathCalculationInfo? criticalPath;

        public VentCalcCenterViewModel(
            Func<VentCalcCenterData> loadSelectedSystem,
            Action<long>? selectElementInRevit,
            Action<string>? showMessage,
            VentCalcSettingsService settingsService,
            Action<Exception>? reportException = null)
        {
            this.loadSelectedSystem = loadSelectedSystem;
            this.selectElementInRevit = selectElementInRevit;
            this.showMessage = showMessage;
            this.reportException = reportException;
            this.settingsService = settingsService;
            Settings = settingsService.Load();
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
            }

            LoadSelectedSystemCommand = new RelayCommand(_ => LoadSelectedSystem());
            RefreshCommand = new RelayCommand(_ => LoadSelectedSystem());
            SelectElementInRevitCommand = new RelayCommand(_ => SelectElementInRevit(), _ => SelectedNetworkElement != null);
            SelectStartElementInRevitCommand = new RelayCommand(_ => SelectStartElementInRevit(), _ => StartElementIds.Count > 0);
            SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
            ResetSettingsCommand = new RelayCommand(_ => ResetSettings());
            ApplyVelocityHighlightCommand = new RelayCommand(_ => ShowStub("Подсветка скоростей будет добавлена на следующем этапе."));
            ResetVelocityHighlightCommand = new RelayCommand(_ => ShowStub("Сброс подсветки скоростей будет подключён к Revit OverrideGraphicSettings на следующем этапе."));
            StubCommand = new RelayCommand(parameter => ShowStub(parameter?.ToString() ?? "Функция будет добавлена позже."));
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

        public ObservableCollection<VentIssueInfo> Issues { get; } = new ObservableCollection<VentIssueInfo>();

        public ObservableCollection<string> StartCandidateDetails { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> EndCandidateDetails { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> IgnoredCapDetails { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> PathWarnings { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> StartElementIds { get; } = new ObservableCollection<string>();

        public string SelectedElementId
        {
            get => selectedElementId;
            private set => SetProperty(ref selectedElementId, value);
        }

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
            : $"Критическая трасса предварительно: №{CriticalPath.PathIndex}, итого {CriticalPath.TotalPressureLossPa:0.###} Па.";

        public NetworkElementRow? SelectedNetworkElement
        {
            get => selectedNetworkElement;
            set
            {
                if (SetProperty(ref selectedNetworkElement, value))
                {
                    CommandManager.InvalidateRequerySuggested();
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
                }
            }
        }

        public string SelectedPathChain => SelectedPath == null ? "—" : string.Join(" → ", SelectedPath.ElementIds);

        public string ReportText { get; private set; } = "Экспорт будет добавлен после стабилизации расчёта.";

        public ICommand LoadSelectedSystemCommand { get; }

        public ICommand RefreshCommand { get; }

        public ICommand SelectElementInRevitCommand { get; }

        public ICommand SelectStartElementInRevitCommand { get; }

        public ICommand SaveSettingsCommand { get; }

        public ICommand ResetSettingsCommand { get; }

        public ICommand ApplyVelocityHighlightCommand { get; }

        public ICommand ResetVelocityHighlightCommand { get; }

        public ICommand StubCommand { get; }

        private void LoadSelectedSystem()
        {
            try
            {
                VentCalcCenterData data = loadSelectedSystem();
                ApplyData(data);
                StatusText = data.Success
                    ? "Система загружена."
                    : (string.IsNullOrWhiteSpace(data.ErrorMessage) ? "Выберите элемент воздуховодной системы и нажмите 'Загрузить выбранную систему'." : data.ErrorMessage);
            }
            catch (Exception exception)
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
        }

        private void ApplyData(VentCalcCenterData data)
        {
            string settingsWarning = settingsService.LastWarning;
            SelectedElementInfo = data.SelectedElementInfo;
            NetworkInfo = data.NetworkInfo;
            PathSummary = data.PathSummary;
            AerodynamicSummary = data.AerodynamicSummary;
            ReportText = data.ReportText;
            SelectedElementId = data.SelectedElementInfo?.ElementId ?? data.NetworkInfo?.SelectedElementId ?? "—";
            SystemName = data.SelectedElementInfo?.SystemName ?? data.NetworkInfo?.Elements.FirstOrDefault()?.SystemName ?? "—";
            SystemType = data.PathSummary?.SystemType ?? data.SelectedElementInfo?.SystemType ?? "—";
            Direction = data.PathSummary?.Direction ?? "—";
            DirectionReason = data.PathSummary?.DirectionReason ?? "—";
            CriticalPath = data.AerodynamicSummary?.CriticalPathByTotalPressure;

            Replace(NetworkElements, BuildNetworkRows(data));
            Replace(Paths, BuildPathRows(data));
            Replace(Issues, BuildIssues(data));
            if (!string.IsNullOrWhiteSpace(settingsWarning))
            {
                Issues.Insert(0, new VentIssueInfo
                {
                    Severity = "Warning",
                    Category = "Настройки",
                    Message = settingsWarning,
                    Recommendation = "Проверьте файл %APPDATA%\\VentCalc\\settings.json; повреждённый файл переименован."
                });
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
            OnPropertyChanged(nameof(ReportText));
            CommandManager.InvalidateRequerySuggested();
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

        private static IReadOnlyList<PathRow> BuildPathRows(VentCalcCenterData data)
        {
            if (data.PathSummary == null)
            {
                return Array.Empty<PathRow>();
            }

            Dictionary<int, PathCalculationInfo> calculations = data.AerodynamicSummary?.Paths.ToDictionary(path => path.PathIndex) ?? new Dictionary<int, PathCalculationInfo>();
            return data.PathSummary.Paths
                .Select(path =>
                {
                    calculations.TryGetValue(path.PathIndex, out PathCalculationInfo? calculation);
                    return new PathRow(path, calculation);
                })
                .ToList();
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

            foreach (LocalResistanceCalculationInfo local in data.AerodynamicSummary?.Paths.SelectMany(path => path.LocalResistances) ?? Enumerable.Empty<LocalResistanceCalculationInfo>())
            {
                if (local.Source == "Не найдено")
                {
                    result.Add(new VentIssueInfo
                    {
                        Severity = "Warning",
                        ElementId = local.ElementId.ToString(CultureInfo.InvariantCulture),
                        Category = "Местные сопротивления",
                        Message = "Фитинг без понятного типа МС; требуется проверка ζ.",
                        Recommendation = "Заполните ζ в комментарии, например z=0.35."
                    });
                }
            }

            return result;
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
            Replace(SelectedPathDucts, SelectedPath?.Calculation?.Ducts ?? Enumerable.Empty<DuctCalculationInfo>());
            Replace(SelectedPathLocalResistances, SelectedPath?.Calculation?.LocalResistances ?? Enumerable.Empty<LocalResistanceCalculationInfo>());
            OnPropertyChanged(nameof(SelectedPathChain));
        }

        private void SelectElementInRevit()
        {
            if (SelectedNetworkElement == null || !long.TryParse(SelectedNetworkElement.ElementId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long elementId))
            {
                return;
            }

            selectElementInRevit?.Invoke(elementId);
        }

        private void SelectStartElementInRevit()
        {
            string? firstStart = StartElementIds.FirstOrDefault();
            if (firstStart != null && long.TryParse(firstStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out long elementId))
            {
                selectElementInRevit?.Invoke(elementId);
            }
        }

        private void SaveSettings()
        {
            settingsService.Save(Settings);
            StatusText = string.IsNullOrWhiteSpace(settingsService.LastWarning) ? "Настройки сохранены." : settingsService.LastWarning;
        }

        private void ResetSettings()
        {
            Settings = VentCalcSettings.CreateDefault();
            OnPropertyChanged(nameof(Settings));
            StatusText = "Настройки сброшены по умолчанию.";
        }

        private void ShowStub(string message)
        {
            StatusText = message;
            showMessage?.Invoke(message);
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

    public sealed class VentCalcCenterData
    {
        public VentElementInfo? SelectedElementInfo { get; set; }

        public VentNetworkInfo? NetworkInfo { get; set; }

        public VentPathSummary? PathSummary { get; set; }

        public AerodynamicCalculationSummary? AerodynamicSummary { get; set; }

        public string ReportText { get; set; } = string.Empty;

        public bool Success { get; set; }

        public bool IsUserSelectionWarning { get; set; }

        public string ErrorMessage { get; set; } = string.Empty;

        public List<string> Warnings { get; set; } = new List<string>();
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
        public PathRow(VentPathInfo path, PathCalculationInfo? calculation)
        {
            Path = path;
            Calculation = calculation;
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

        public string FlowM3h => Path.MaxFlowM3h;

        public double TotalPressureLossPa => Calculation?.TotalPressureLossPa ?? 0;

        public IReadOnlyList<string> ElementIds => Path.ElementIds;
    }

    public sealed class VentCalcSettings : NotifyObject
    {
        private double airDensityKgM3 = 1.2;
        private double airDynamicViscosityPaS = 0.0000181;
        private double roughnessM = 0.0001;
        private double reservePercent;
        private double minVelocityMs = 1;
        private double maxVelocityMs = 8;
        private double criticalVelocityMs = 12;

        public double AirDensityKgM3
        {
            get => airDensityKgM3;
            set => SetProperty(ref airDensityKgM3, value);
        }

        public double AirDynamicViscosityPaS
        {
            get => airDynamicViscosityPaS;
            set => SetProperty(ref airDynamicViscosityPaS, value);
        }

        public double RoughnessM
        {
            get => roughnessM;
            set => SetProperty(ref roughnessM, value);
        }

        public double ReservePercent
        {
            get => reservePercent;
            set => SetProperty(ref reservePercent, value);
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

        public static VentCalcSettings CreateDefault()
        {
            return new VentCalcSettings();
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
