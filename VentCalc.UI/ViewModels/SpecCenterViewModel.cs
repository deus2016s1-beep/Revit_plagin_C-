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
        private SpecItemRow? selectedVentilationRow;
        private SpecItemRow? selectedProblemRow;
        private string statusText = "Нажмите «Собрать вентиляцию», чтобы сформировать спецификацию.";
        private string lastExcelExportPath = "—";
        private string statusFilter = "Все";
        private string groupFilter = "Все";
        private string searchText = string.Empty;
        private bool onlyProblems;
        private string selectedRuleScope = "Type";

        public SpecCenterViewModel(
            Action<SpecCenterViewModel>? requestCollectVentilation,
            Action<IEnumerable<long>>? selectElementsInRevit,
            Action<string>? showMessage,
            SpecRuleService? ruleService = null,
            Action<SpecCenterViewModel, IReadOnlyList<SpecItemRow>>? requestWriteAdsk = null)
        {
            this.requestCollectVentilation = requestCollectVentilation;
            this.requestWriteAdsk = requestWriteAdsk;
            this.selectElementsInRevit = selectElementsInRevit;
            this.showMessage = showMessage;
            this.ruleService = ruleService ?? new SpecRuleService();

            CollectVentilationCommand = new RelayCommand(_ => CollectVentilation());
            ExportExcelCommand = new RelayCommand(_ => ExportExcel(), _ => VentilationRows.Count > 0);
            ShowSelectedElementCommand = new RelayCommand(_ => ShowSelectedElement(), _ => CurrentSelectedRow?.ElementId > 0);
            ShowProblemsCommand = new RelayCommand(_ => ShowProblems(), _ => ProblemRows.Any(row => row.ElementId > 0));
            ShowUnknownCommand = new RelayCommand(_ => ShowUnknown(), _ => VentilationRows.Any(row => row.IsUnrecognized && row.ElementId > 0));
            ClearSpecHighlightCommand = new RelayCommand(_ => ClearSpecHighlight());
            SaveRulesCommand = new RelayCommand(_ => SaveRules(), _ => VentilationRows.Count > 0);
            ResetAutoCommand = new RelayCommand(_ => ResetAuto(), _ => CurrentSelectedRow != null);
            ResetFiltersCommand = new RelayCommand(_ => ResetFilters());
            ShowOnlyProblemsCommand = new RelayCommand(_ => ShowOnlyProblems());
            WriteSelectedAdskCommand = new RelayCommand(_ => WriteSelectedAdsk(), _ => CurrentSelectedRow != null);
            WriteFilteredAdskCommand = new RelayCommand(_ => WriteFilteredAdsk(), _ => FilteredVentilationRows.Count > 0);
        }

        public ObservableCollection<SpecItemRow> VentilationRows { get; } = new ObservableCollection<SpecItemRow>();
        public ObservableCollection<SpecItemRow> FilteredVentilationRows { get; } = new ObservableCollection<SpecItemRow>();
        public ObservableCollection<SpecItemRow> ProblemRows { get; } = new ObservableCollection<SpecItemRow>();

        public IReadOnlyList<string> StatusFilterOptions { get; } = new[] { "Все", "OK", "Warning", "Error" };
        public IReadOnlyList<string> GroupFilterOptions { get; } = new[] { "Все", "Воздуховоды", "Гибкие воздуховоды", "Фасонные части", "Арматура / клапаны", "Воздухораспределители", "Оборудование", "Зонты", "Неопознано" };
        public IReadOnlyList<string> RuleScopeOptions { get; } = new[] { "Element", "Type", "Family" };

        public SpecItemRow? SelectedVentilationRow
        {
            get => selectedVentilationRow;
            set => SetProperty(ref selectedVentilationRow, value);
        }

        public SpecItemRow? SelectedProblemRow
        {
            get => selectedProblemRow;
            set => SetProperty(ref selectedProblemRow, value);
        }

        private SpecItemRow? CurrentSelectedRow => SelectedProblemRow ?? SelectedVentilationRow;

        public string StatusText
        {
            get => statusText;
            private set => SetProperty(ref statusText, value);
        }

        public string LastExcelExportPath
        {
            get => lastExcelExportPath;
            private set => SetProperty(ref lastExcelExportPath, value);
        }

        public string StatusFilter
        {
            get => statusFilter;
            set { if (SetProperty(ref statusFilter, value)) RefreshFilters(); }
        }

        public string GroupFilter
        {
            get => groupFilter;
            set { if (SetProperty(ref groupFilter, value)) RefreshFilters(); }
        }

        public string SearchText
        {
            get => searchText;
            set { if (SetProperty(ref searchText, value)) RefreshFilters(); }
        }

        public string SelectedRuleScope
        {
            get => selectedRuleScope;
            set => SetProperty(ref selectedRuleScope, string.IsNullOrWhiteSpace(value) ? "Type" : value);
        }

        public int TotalCount => VentilationRows.Count;
        public int OkCount => VentilationRows.Count(row => row.Status == "OK");
        public int WarningCount => VentilationRows.Count(row => row.Status == "Warning");
        public int ErrorCount => VentilationRows.Count(row => row.Status == "Error");
        public int UnknownCount => VentilationRows.Count(row => row.IsUnrecognized || row.Group == "Неопознано");
        public int MissingAdskNameCount => VentilationRows.Count(row => !row.HasAdskName);
        public int MissingSizeCount => VentilationRows.Count(row => row.MissingSize);
        public int MissingSystemCount => VentilationRows.Count(row => row.MissingSystem);
        public string RulesPath => ruleService.RulesPath;

        public ICommand CollectVentilationCommand { get; }
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

        public void ApplyCollectedRows(IReadOnlyList<SpecItemRow> rows)
        {
            VentilationRows.Clear();
            foreach (SpecItemRow row in rows)
            {
                VentilationRows.Add(row);
            }

            RefreshFilters();
            SelectedVentilationRow = FilteredVentilationRows.FirstOrDefault();
            StatusText = $"Собрано элементов вентиляции: {VentilationRows.Count}. OK: {OkCount}; Warning: {WarningCount}; Error: {ErrorCount}.";
            NotifyCounts();
        }

        public void ApplyAdskWriteResult(string message, IReadOnlyList<SpecItemRow>? refreshedRows)
        {
            if (refreshedRows != null)
            {
                ApplyCollectedRows(refreshedRows);
            }

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

        private void ExportExcel()
        {
            try
            {
                if ((WarningCount > 0 || ErrorCount > 0)
                    && MessageBox.Show($"В спецификации есть проблемы: Warning = {WarningCount}, Error = {ErrorCount}. Экспортировать?", "SpecCalc", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }

                SpecExcelExportResult result = SpecExcelExporter.Export(VentilationRows);
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
            if (CurrentSelectedRow?.ElementId > 0)
            {
                selectElementsInRevit?.Invoke(new[] { CurrentSelectedRow.ElementId });
            }
        }

        private void ShowProblems()
        {
            SelectRows(ProblemRows, "Проблемные элементы не найдены.", "Выделено проблемных элементов");
        }

        private void ShowUnknown()
        {
            SelectRows(VentilationRows.Where(row => row.IsUnrecognized || row.Group == "Неопознано"), "Неопознанные элементы не найдены.", "Выделено неопознанных элементов");
        }

        private void ClearSpecHighlight()
        {
            selectElementsInRevit?.Invoke(Array.Empty<long>());
            StatusText = "Подсветка/выделение SpecCalc снято.";
        }

        private void SelectRows(IEnumerable<SpecItemRow> rows, string emptyMessage, string successPrefix)
        {
            long[] ids = rows.Where(row => row.ElementId > 0).Select(row => row.ElementId).Distinct().ToArray();
            if (ids.Length == 0)
            {
                showMessage?.Invoke(emptyMessage);
                return;
            }

            selectElementsInRevit?.Invoke(ids);
            StatusText = $"{successPrefix}: {ids.Length}.";
        }

        private void SaveRules()
        {
            List<SpecItemRow> rowsToSave = VentilationRows.Where(row => row.IsManualEdited).ToList();
            if (rowsToSave.Count == 0 && CurrentSelectedRow != null)
            {
                rowsToSave.Add(CurrentSelectedRow);
            }

            if (rowsToSave.Count == 0)
            {
                showMessage?.Invoke("Нет строк для сохранения правил.");
                return;
            }

            try
            {
                ruleService.SaveRules(rowsToSave, SelectedRuleScope);
                StatusText = $"Правила сохранены: {rowsToSave.Count}. Область: {SelectedRuleScope}. Файл: {ruleService.RulesPath}";
            }
            catch (Exception exception)
            {
                StatusText = exception.Message;
                showMessage?.Invoke(exception.Message);
            }
        }

        private void ResetAuto()
        {
            if (CurrentSelectedRow == null)
            {
                return;
            }

            bool removed = ruleService.RemoveRule(CurrentSelectedRow, SelectedRuleScope);
            StatusText = removed
                ? "Ручное правило выбранной области удалено. Выполняется пересборка."
                : "Для выбранной строки не найдено сохранённое ручное правило в выбранной области. Выполняется пересборка.";
            requestCollectVentilation?.Invoke(this);
        }

        private void WriteSelectedAdsk()
        {
            if (CurrentSelectedRow == null)
            {
                return;
            }

            WriteAdskRows(new[] { CurrentSelectedRow }, "Будут изменены ADSK-параметры выбранной строки. Продолжить?");
        }

        private void WriteFilteredAdsk()
        {
            WriteAdskRows(FilteredVentilationRows.ToList(), "Будут изменены ADSK-параметры выбранных/отфильтрованных элементов. Продолжить?");
        }

        private void WriteAdskRows(IReadOnlyList<SpecItemRow> rows, string confirmation)
        {
            if (rows.Count == 0)
            {
                showMessage?.Invoke("Нет строк для записи ADSK.");
                return;
            }

            if (MessageBox.Show(confirmation, "SpecCalc", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            requestWriteAdsk?.Invoke(this, rows);
        }

        private void ShowOnlyProblems()
        {
            onlyProblems = true;
            RefreshFilters();
        }

        private void ResetFilters()
        {
            onlyProblems = false;
            StatusFilter = "Все";
            GroupFilter = "Все";
            SearchText = string.Empty;
            RefreshFilters();
        }

        private void RefreshFilters()
        {
            IEnumerable<SpecItemRow> rows = VentilationRows;
            if (onlyProblems)
            {
                rows = rows.Where(IsProblemRow);
            }

            if (!string.IsNullOrWhiteSpace(StatusFilter) && StatusFilter != "Все")
            {
                rows = rows.Where(row => row.Status == StatusFilter);
            }

            if (!string.IsNullOrWhiteSpace(GroupFilter) && GroupFilter != "Все")
            {
                rows = rows.Where(row => row.Group == GroupFilter);
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string query = SearchText.Trim();
                rows = rows.Where(row => Contains(row.Name, query)
                    || Contains(row.TypeMark, query)
                    || Contains(row.Size, query)
                    || Contains(row.System, query)
                    || Contains(row.Level, query)
                    || Contains(row.Note, query));
            }

            Replace(FilteredVentilationRows, rows.ToList());
            Replace(ProblemRows, VentilationRows.Where(IsProblemRow).ToList());
            NotifyCounts();
        }

        private static bool IsProblemRow(SpecItemRow row)
        {
            return row.Status is "Warning" or "Error" || row.IsUnrecognized || row.MissingSize || row.MissingSystem || !row.HasAdskName;
        }

        private static bool Contains(string value, string query)
        {
            return value?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void Replace(ObservableCollection<SpecItemRow> target, IReadOnlyList<SpecItemRow> rows)
        {
            target.Clear();
            foreach (SpecItemRow row in rows)
            {
                target.Add(row);
            }
        }

        private void NotifyCounts()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(OkCount));
            OnPropertyChanged(nameof(WarningCount));
            OnPropertyChanged(nameof(ErrorCount));
            OnPropertyChanged(nameof(UnknownCount));
            OnPropertyChanged(nameof(MissingAdskNameCount));
            OnPropertyChanged(nameof(MissingSizeCount));
            OnPropertyChanged(nameof(MissingSystemCount));
        }
    }
}
