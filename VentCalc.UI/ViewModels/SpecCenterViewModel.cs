using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using VentCalc.UI.Services;
using VentCalc.UI.Views;

namespace VentCalc.UI.ViewModels
{
    public sealed class SpecCenterViewModel : NotifyObject
    {
        private readonly Action<SpecCenterViewModel>? requestCollectVentilation;
        private readonly Action<SpecCenterViewModel, IReadOnlyList<SpecItemRow>>? requestWriteAdsk;
        private readonly Action<IEnumerable<long>>? selectElementsInRevit;
        private readonly Action<string>? showMessage;
        private readonly SpecRuleService ruleService;
        private readonly SpecCalcSettingsService settingsService = new SpecCalcSettingsService();
        private readonly SpecCalcSettings settings;
        private SpecGroupRow? selectedSpecRow;
        private SpecItemRow? selectedRawRow;
        private SpecColumnLayout? selectedAvailableColumn;
        private SpecColumnLayout? selectedActiveColumn;
        private string statusText = "Нажмите «Обновить», чтобы сформировать спецификацию.";
        private string lastExcelExportPath = "—";
        private string statusFilter = "Все";
        private string groupFilter = "Все";
        private string sectionFilter = "Все";
        private string searchText = string.Empty;
        private bool onlyProblems;
        private string selectedRuleScope = "Type";
        private string selectedExcelProfile;

        public SpecCenterViewModel(Action<SpecCenterViewModel>? requestCollectVentilation, Action<IEnumerable<long>>? selectElementsInRevit, Action<string>? showMessage, SpecRuleService? ruleService = null, Action<SpecCenterViewModel, IReadOnlyList<SpecItemRow>>? requestWriteAdsk = null)
        {
            this.requestCollectVentilation = requestCollectVentilation;
            this.requestWriteAdsk = requestWriteAdsk;
            this.selectElementsInRevit = selectElementsInRevit;
            this.showMessage = showMessage;
            this.ruleService = ruleService ?? new SpecRuleService();
            settings = settingsService.Load();
            selectedExcelProfile = settings.SelectedExcelProfile;
            foreach (SpecColumnLayout column in settings.Columns) ColumnLayouts.Add(column);
            RefreshColumnLists();

            CollectVentilationCommand = new RelayCommand(_ => CollectVentilation());
            BulkEditCommand = new RelayCommand(_ => BulkEdit(), _ => SelectedSpecRows.Count > 0 || FilteredSpecRows.Count > 0);
            ExportExcelCommand = new RelayCommand(_ => ExportExcel(), _ => SpecRows.Count > 0);
            ShowSelectedElementCommand = new RelayCommand(_ => ShowSelectedElement(), _ => SelectedSpecRows.Count > 0 || SelectedSpecRow?.ElementIds.Count > 0 || SelectedRawRow?.ElementId > 0);
            ShowProblemsCommand = new RelayCommand(_ => ShowProblems(), _ => ProblemRows.Any(row => row.ElementIds.Count > 0));
            ShowUnknownCommand = new RelayCommand(_ => ShowUnknown(), _ => RawRows.Any(row => row.IsUnrecognized && row.ElementId > 0));
            ClearSpecHighlightCommand = new RelayCommand(_ => ClearSpecHighlight());
            SaveRulesCommand = new RelayCommand(_ => SaveRules(), _ => RawRows.Count > 0);
            ResetAutoCommand = new RelayCommand(_ => ResetAuto(), _ => SelectedSpecRow != null || SelectedRawRow != null);
            ResetFiltersCommand = new RelayCommand(_ => ResetFilters());
            ShowOnlyProblemsCommand = new RelayCommand(_ => ShowOnlyProblems());
            WriteSelectedAdskCommand = new RelayCommand(_ => WriteSelectedAdsk(), _ => SelectedSpecRow != null || SelectedRawRow != null);
            WriteFilteredAdskCommand = new RelayCommand(_ => WriteFilteredAdsk(), _ => FilteredSpecRows.Count > 0);
            SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
            AddColumnCommand = new RelayCommand(_ => AddColumn(), _ => SelectedAvailableColumn != null);
            RemoveColumnCommand = new RelayCommand(_ => RemoveColumn(), _ => SelectedActiveColumn != null);
            MoveColumnUpCommand = new RelayCommand(_ => MoveColumn(-1), _ => SelectedActiveColumn != null);
            MoveColumnDownCommand = new RelayCommand(_ => MoveColumn(1), _ => SelectedActiveColumn != null);
        }

        public ObservableCollection<SpecItemRow> RawRows { get; } = new ObservableCollection<SpecItemRow>();
        public ObservableCollection<SpecGroupRow> SpecRows { get; } = new ObservableCollection<SpecGroupRow>();
        public ObservableCollection<SpecGroupRow> FilteredSpecRows { get; } = new ObservableCollection<SpecGroupRow>();
        public ObservableCollection<SpecGroupRow> SelectedSpecRows { get; } = new ObservableCollection<SpecGroupRow>();
        public ObservableCollection<SpecGroupRow> ProblemRows { get; } = new ObservableCollection<SpecGroupRow>();
        public ObservableCollection<SpecColumnLayout> ColumnLayouts { get; } = new ObservableCollection<SpecColumnLayout>();
        public ObservableCollection<SpecColumnLayout> AvailableColumns { get; } = new ObservableCollection<SpecColumnLayout>();
        public ObservableCollection<SpecColumnLayout> ActiveColumns { get; } = new ObservableCollection<SpecColumnLayout>();

        public IReadOnlyList<string> StatusFilterOptions { get; } = new[] { "Все", "OK", "Warning", "Error" };
        public IReadOnlyList<string> GroupFilterOptions { get; } = new[] { "Все", "Воздуховоды", "Гибкие воздуховоды", "Фасонные части", "Арматура / клапаны", "Воздухораспределители", "Оборудование", "Зонты", "Неопознано" };
        public IReadOnlyList<string> SectionFilterOptions { get; } = new[] { "Все", "Вентиляция" };
        public IReadOnlyList<string> RuleScopeOptions { get; } = new[] { "Element", "Type", "Family", "Group" };
        public IReadOnlyList<string> ExcelProfiles { get; } = new[] { "Проектная спецификация", "Визуальная спецификация", "Монтажная ведомость", "Закупка" };
        public IReadOnlyList<string> FormatOptions { get; } = new[] { "Текст", "Целое число", "0.00", "0.000", "м", "м²", "шт", "Изображение" };

        public SpecGroupRow? SelectedSpecRow { get => selectedSpecRow; set => SetProperty(ref selectedSpecRow, value); }
        public SpecItemRow? SelectedRawRow { get => selectedRawRow; set => SetProperty(ref selectedRawRow, value); }
        public SpecColumnLayout? SelectedAvailableColumn { get => selectedAvailableColumn; set => SetProperty(ref selectedAvailableColumn, value); }
        public SpecColumnLayout? SelectedActiveColumn { get => selectedActiveColumn; set => SetProperty(ref selectedActiveColumn, value); }
        public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
        public string LastExcelExportPath { get => lastExcelExportPath; private set => SetProperty(ref lastExcelExportPath, value); }
        public string RulesPath => ruleService.RulesPath;
        public string SettingsPath => settingsService.SettingsPath;
        public string StatusFilter { get => statusFilter; set { if (SetProperty(ref statusFilter, value)) RefreshFilters(); } }
        public string GroupFilter { get => groupFilter; set { if (SetProperty(ref groupFilter, value)) RefreshFilters(); } }
        public string SectionFilter { get => sectionFilter; set { if (SetProperty(ref sectionFilter, value)) RefreshFilters(); } }
        public string SearchText { get => searchText; set { if (SetProperty(ref searchText, value)) RefreshFilters(); } }
        public string SelectedRuleScope { get => selectedRuleScope; set => SetProperty(ref selectedRuleScope, string.IsNullOrWhiteSpace(value) ? "Type" : value); }
        public string SelectedExcelProfile { get => selectedExcelProfile; set { if (SetProperty(ref selectedExcelProfile, value)) { settings.SelectedExcelProfile = value; SaveSettings(); } } }

        public int TotalCount => RawRows.Count;
        public int GroupCount => SpecRows.Count;
        public int OkCount => RawRows.Count(row => row.Status == "OK");
        public int WarningCount => RawRows.Count(row => row.Status == "Warning");
        public int ErrorCount => RawRows.Count(row => row.Status == "Error");
        public int UnknownCount => RawRows.Count(row => row.IsUnrecognized || row.Group == "Неопознано");
        public int MissingAdskNameCount => RawRows.Count(row => !row.HasAdskName);
        public int MissingSizeCount => RawRows.Count(row => row.MissingSize);
        public int MissingSystemCount => RawRows.Count(row => row.MissingSystem);

        public ICommand CollectVentilationCommand { get; }
        public ICommand BulkEditCommand { get; }
        public ICommand ExportExcelCommand { get; }
        public ICommand ShowSelectedElementCommand { get; }
        public ICommand ShowProblemsCommand { get; }
        public ICommand ShowUnknownCommand { get; }
        public ICommand ClearSpecHighlightCommand { get; }
        public ICommand SaveRulesCommand { get; }
        public ICommand ResetAutoCommand { get; }
        public ICommand ResetFiltersCommand { get; }
        public ICommand ShowOnlyProblemsCommand { get; }
        public ICommand WriteSelectedAdskCommand { get; }
        public ICommand WriteFilteredAdskCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand AddColumnCommand { get; }
        public ICommand RemoveColumnCommand { get; }
        public ICommand MoveColumnUpCommand { get; }
        public ICommand MoveColumnDownCommand { get; }

        public void ApplyCollectedRows(IReadOnlyList<SpecItemRow> rows)
        {
            Replace(RawRows, rows.ToList());
            Replace(SpecRows, SpecGroupingService.Group(RawRows, ColumnLayouts).ToList());
            RefreshFilters();
            SelectedSpecRow = FilteredSpecRows.FirstOrDefault();
            StatusText = $"Собрано элементов: {RawRows.Count}. Строк спецификации: {SpecRows.Count}. OK: {OkCount}; Warning: {WarningCount}; Error: {ErrorCount}.";
            NotifyCounts();
        }

        public void ApplyAdskWriteResult(string message, IReadOnlyList<SpecItemRow>? refreshedRows)
        {
            if (refreshedRows != null) ApplyCollectedRows(refreshedRows);
            StatusText = message;
        }

        public void FailCollect(string message)
        {
            StatusText = message;
            showMessage?.Invoke(message);
        }

        private void CollectVentilation()
        {
            StatusText = "Сбор спецификации вентиляции...";
            requestCollectVentilation?.Invoke(this);
        }

        private void BulkEdit()
        {
            IReadOnlyList<SpecGroupRow> rows = SelectedSpecRows.Count > 0 ? SelectedSpecRows.ToList() : SelectedSpecRow != null ? new[] { SelectedSpecRow } : FilteredSpecRows.ToList();
            var window = new BulkEditSpecRuleWindow(rows, SelectedRuleScope);
            if (window.ShowDialog() != true) return;
            List<SpecItemRow> rawRows = ExpandGroups(window.TargetFiltered ? FilteredSpecRows : rows).ToList();
            foreach (SpecItemRow row in rawRows)
            {
                row.ApplySystemValues(item =>
                {
                    if (!string.IsNullOrWhiteSpace(window.SectionValue)) item.Section = window.SectionValue;
                    if (!string.IsNullOrWhiteSpace(window.GroupValue)) item.Group = window.GroupValue;
                    if (!string.IsNullOrWhiteSpace(window.NameValue)) item.Name = window.NameValue;
                    if (!string.IsNullOrWhiteSpace(window.TypeMarkValue)) item.TypeMark = window.TypeMarkValue;
                    if (!string.IsNullOrWhiteSpace(window.SizeValue)) item.Size = window.SizeValue;
                    if (!string.IsNullOrWhiteSpace(window.UnitValue)) item.Unit = window.UnitValue;
                    if (!string.IsNullOrWhiteSpace(window.NoteValue)) item.Note = window.NoteValue;
                    item.Source = "ManualRule";
                });
            }
            ruleService.SaveRules(rawRows, window.RuleScope);
            Replace(SpecRows, SpecGroupingService.Group(RawRows, ColumnLayouts).ToList());
            RefreshFilters();
            StatusText = $"Массовое правило сохранено: {rawRows.Count} элементов. Файл: {ruleService.RulesPath}";
        }

        private void ExportExcel()
        {
            try
            {
                if ((WarningCount > 0 || ErrorCount > 0) && MessageBox.Show($"В спецификации есть проблемы: Warning = {WarningCount}, Error = {ErrorCount}. Экспортировать?", "SpecCalc", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                SpecExcelExportResult result = SpecExcelExporter.Export(FilteredSpecRows.Count > 0 ? FilteredSpecRows : SpecRows, SelectedExcelProfile, ColumnLayouts);
                LastExcelExportPath = result.Path;
                StatusText = $"Excel спецификации создан: {result.Path}";
                new ExcelExportResultWindow(result.Path).ShowDialog();
            }
            catch (Exception exception)
            {
                StatusText = exception.Message;
                showMessage?.Invoke(exception.Message);
            }
        }

        private void ShowSelectedElement()
        {
            if (SelectedSpecRows.Count > 0) selectElementsInRevit?.Invoke(SelectedSpecRows.SelectMany(row => row.ElementIds).Distinct());
            else if (SelectedSpecRow?.ElementIds.Count > 0) selectElementsInRevit?.Invoke(SelectedSpecRow.ElementIds);
            else if (SelectedRawRow?.ElementId > 0) selectElementsInRevit?.Invoke(new[] { SelectedRawRow.ElementId });
        }

        private void ShowProblems() => SelectRows(ProblemRows.SelectMany(row => row.ElementIds), "Проблемные элементы не найдены.", "Выделено проблемных элементов");
        private void ShowUnknown() => SelectRows(RawRows.Where(row => row.IsUnrecognized || row.Group == "Неопознано").Select(row => row.ElementId), "Неопознанные элементы не найдены.", "Выделено неопознанных элементов");
        private void ClearSpecHighlight() { selectElementsInRevit?.Invoke(Array.Empty<long>()); StatusText = "Подсветка/выделение SpecCalc снято."; }

        private void SelectRows(IEnumerable<long> elementIds, string emptyMessage, string successPrefix)
        {
            long[] ids = elementIds.Where(id => id > 0).Distinct().ToArray();
            if (ids.Length == 0) { showMessage?.Invoke(emptyMessage); return; }
            selectElementsInRevit?.Invoke(ids);
            StatusText = $"{successPrefix}: {ids.Length}.";
        }

        private void SaveRules()
        {
            List<SpecItemRow> rowsToSave = RawRows.Where(row => row.IsManualEdited).ToList();
            if (rowsToSave.Count == 0) rowsToSave = SelectedSpecRow != null ? SelectedSpecRow.SourceItems.ToList() : SelectedRawRow != null ? new List<SpecItemRow> { SelectedRawRow } : new List<SpecItemRow>();
            if (rowsToSave.Count == 0) { showMessage?.Invoke("Нет строк для сохранения правил."); return; }
            ruleService.SaveRules(rowsToSave, SelectedRuleScope);
            Replace(SpecRows, SpecGroupingService.Group(RawRows, ColumnLayouts).ToList());
            RefreshFilters();
            StatusText = $"Правила сохранены: {rowsToSave.Count}. Область: {SelectedRuleScope}. Файл: {ruleService.RulesPath}";
        }

        private void ResetAuto()
        {
            SpecItemRow? row = SelectedSpecRow?.SourceItems.FirstOrDefault() ?? SelectedRawRow;
            if (row == null) return;
            ruleService.RemoveRule(row, SelectedRuleScope);
            StatusText = "Ручное правило удалено. Выполняется пересборка.";
            requestCollectVentilation?.Invoke(this);
        }

        private void WriteSelectedAdsk()
        {
            List<SpecItemRow> rows = SelectedSpecRows.Count > 0 ? ExpandGroups(SelectedSpecRows).ToList() : SelectedSpecRow != null ? SelectedSpecRow.SourceItems.ToList() : SelectedRawRow != null ? new List<SpecItemRow> { SelectedRawRow } : new List<SpecItemRow>();
            WriteAdskRows(rows, "Будут изменены ADSK-параметры выбранных строк. Продолжить?");
        }

        private void WriteFilteredAdsk() => WriteAdskRows(ExpandGroups(FilteredSpecRows).ToList(), "Будут изменены ADSK-параметры отфильтрованных строк. Продолжить?");

        private void WriteAdskRows(IReadOnlyList<SpecItemRow> rows, string confirmation)
        {
            if (rows.Count == 0) { showMessage?.Invoke("Нет строк для записи ADSK."); return; }
            if (MessageBox.Show(confirmation, "SpecCalc", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            requestWriteAdsk?.Invoke(this, rows);
        }

        private void ShowOnlyProblems() { onlyProblems = true; RefreshFilters(); }
        private void ResetFilters() { onlyProblems = false; SectionFilter = "Все"; StatusFilter = "Все"; GroupFilter = "Все"; SearchText = string.Empty; RefreshFilters(); }
        private void SaveSettings() { settings.Columns = ColumnLayouts.OrderBy(column => column.Order).ToList(); settings.SelectedExcelProfile = SelectedExcelProfile; settingsService.Save(settings); RefreshColumnLists(); StatusText = $"Настройки SpecCalc сохранены: {settingsService.SettingsPath}"; }

        public void UpdateSelectedSpecRows(IEnumerable<SpecGroupRow> rows)
        {
            Replace(SelectedSpecRows, rows.ToList());
        }

        private void AddColumn()
        {
            if (SelectedAvailableColumn == null) return;
            SelectedAvailableColumn.VisibleInMain = true;
            SelectedAvailableColumn.VisibleInExcel = true;
            SelectedAvailableColumn.Order = ActiveColumns.Count == 0 ? 1 : ActiveColumns.Max(column => column.Order) + 1;
            SaveSettings();
        }

        private void RemoveColumn()
        {
            if (SelectedActiveColumn == null) return;
            SelectedActiveColumn.VisibleInMain = false;
            SelectedActiveColumn.VisibleInExcel = false;
            SaveSettings();
        }

        private void MoveColumn(int delta)
        {
            if (SelectedActiveColumn == null) return;
            List<SpecColumnLayout> active = ActiveColumns.OrderBy(column => column.Order).ToList();
            int index = active.IndexOf(SelectedActiveColumn);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= active.Count) return;
            (active[index].Order, active[target].Order) = (active[target].Order, active[index].Order);
            SaveSettings();
            SelectedActiveColumn = active[target];
        }

        private void RefreshColumnLists()
        {
            Replace(ActiveColumns, ColumnLayouts.Where(column => column.VisibleInMain || column.VisibleInExcel).OrderBy(column => column.Order).ToList());
            Replace(AvailableColumns, ColumnLayouts.Where(column => !column.VisibleInMain && !column.VisibleInExcel).OrderBy(column => column.Order).ToList());
        }

        private void RefreshFilters()
        {
            IEnumerable<SpecGroupRow> rows = SpecRows;
            if (onlyProblems) rows = rows.Where(IsProblemRow);
            if (SectionFilter != "Все") rows = rows.Where(row => row.Section == SectionFilter);
            if (StatusFilter != "Все") rows = rows.Where(row => row.Status == StatusFilter);
            if (GroupFilter != "Все") rows = rows.Where(row => row.Group == GroupFilter);
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string query = SearchText.Trim();
                rows = rows.Where(row => Contains(row.Name, query) || Contains(row.TypeMark, query) || Contains(row.Size, query) || Contains(row.System, query) || Contains(row.Level, query) || Contains(row.Note, query));
            }
            Replace(FilteredSpecRows, rows.ToList());
            Replace(ProblemRows, SpecRows.Where(IsProblemRow).ToList());
            NotifyCounts();
        }

        private static bool IsProblemRow(SpecGroupRow row) => row.Status is "Warning" or "Error" || row.SourceItems.Any(item => item.IsUnrecognized || item.MissingSize || item.MissingSystem || !item.HasAdskName);
        private static bool Contains(string value, string query) => value?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        private static IEnumerable<SpecItemRow> ExpandGroups(IEnumerable<SpecGroupRow> groups) => groups.SelectMany(group => group.SourceItems).Distinct();
        private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> rows) { target.Clear(); foreach (T row in rows) target.Add(row); }
        private void NotifyCounts()
        {
            OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(GroupCount)); OnPropertyChanged(nameof(OkCount)); OnPropertyChanged(nameof(WarningCount)); OnPropertyChanged(nameof(ErrorCount)); OnPropertyChanged(nameof(UnknownCount)); OnPropertyChanged(nameof(MissingAdskNameCount)); OnPropertyChanged(nameof(MissingSizeCount)); OnPropertyChanged(nameof(MissingSystemCount));
        }
    }
}
