using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using VentCalc.UI.Services;
using VentCalc.UI.Views;

namespace VentCalc.UI.ViewModels
{
    public sealed class SpecCenterViewModel : NotifyObject
    {
        private readonly Action<SpecCenterViewModel>? requestCollectVentilation;
        private readonly Action<IEnumerable<long>>? selectElementsInRevit;
        private readonly Action<string>? showMessage;
        private readonly SpecRuleService ruleService;
        private SpecItemRow? selectedVentilationRow;
        private string statusText = "Нажмите «Собрать вентиляцию», чтобы сформировать спецификацию.";
        private string lastExcelExportPath = "—";

        public SpecCenterViewModel(
            Action<SpecCenterViewModel>? requestCollectVentilation,
            Action<IEnumerable<long>>? selectElementsInRevit,
            Action<string>? showMessage,
            SpecRuleService? ruleService = null)
        {
            this.requestCollectVentilation = requestCollectVentilation;
            this.selectElementsInRevit = selectElementsInRevit;
            this.showMessage = showMessage;
            this.ruleService = ruleService ?? new SpecRuleService();

            CollectVentilationCommand = new RelayCommand(_ => CollectVentilation());
            ExportExcelCommand = new RelayCommand(_ => ExportExcel(), _ => VentilationRows.Count > 0);
            ShowSelectedElementCommand = new RelayCommand(_ => ShowSelectedElement(), _ => SelectedVentilationRow?.ElementId > 0);
            ShowProblemsCommand = new RelayCommand(_ => ShowProblems(), _ => VentilationRows.Any(row => row.Status != "OK" && row.ElementId > 0));
            SaveRulesCommand = new RelayCommand(_ => SaveRules(), _ => VentilationRows.Count > 0);
            ResetAutoCommand = new RelayCommand(_ => ResetAuto(), _ => SelectedVentilationRow != null);
        }

        public ObservableCollection<SpecItemRow> VentilationRows { get; } = new ObservableCollection<SpecItemRow>();

        public SpecItemRow? SelectedVentilationRow
        {
            get => selectedVentilationRow;
            set => SetProperty(ref selectedVentilationRow, value);
        }

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

        public int TotalCount => VentilationRows.Count;
        public int WarningCount => VentilationRows.Count(row => row.Status == "Warning");
        public int ErrorCount => VentilationRows.Count(row => row.Status == "Error");
        public string RulesPath => ruleService.RulesPath;

        public ICommand CollectVentilationCommand { get; }
        public ICommand ExportExcelCommand { get; }
        public ICommand ShowSelectedElementCommand { get; }
        public ICommand ShowProblemsCommand { get; }
        public ICommand SaveRulesCommand { get; }
        public ICommand ResetAutoCommand { get; }

        public void ApplyCollectedRows(IReadOnlyList<SpecItemRow> rows)
        {
            VentilationRows.Clear();
            foreach (SpecItemRow row in rows)
            {
                VentilationRows.Add(row);
            }

            SelectedVentilationRow = VentilationRows.FirstOrDefault();
            StatusText = $"Собрано элементов вентиляции: {VentilationRows.Count}. Предупреждений: {WarningCount}; ошибок: {ErrorCount}.";
            NotifyCounts();
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
            if (SelectedVentilationRow?.ElementId > 0)
            {
                selectElementsInRevit?.Invoke(new[] { SelectedVentilationRow.ElementId });
            }
        }

        private void ShowProblems()
        {
            long[] ids = VentilationRows
                .Where(row => row.Status != "OK" && row.ElementId > 0)
                .Select(row => row.ElementId)
                .Distinct()
                .ToArray();
            if (ids.Length == 0)
            {
                showMessage?.Invoke("Проблемные элементы не найдены.");
                return;
            }

            selectElementsInRevit?.Invoke(ids);
            StatusText = $"Выделено проблемных элементов: {ids.Length}.";
        }

        private void SaveRules()
        {
            List<SpecItemRow> rowsToSave = VentilationRows.Where(row => row.IsManualEdited).ToList();
            if (rowsToSave.Count == 0 && SelectedVentilationRow != null)
            {
                rowsToSave.Add(SelectedVentilationRow);
            }

            if (rowsToSave.Count == 0)
            {
                showMessage?.Invoke("Нет строк для сохранения правил.");
                return;
            }

            try
            {
                ruleService.SaveRules(rowsToSave);
                StatusText = $"Правила сохранены: {rowsToSave.Count}. Файл: {ruleService.RulesPath}";
            }
            catch (Exception exception)
            {
                StatusText = exception.Message;
                showMessage?.Invoke(exception.Message);
            }
        }

        private void ResetAuto()
        {
            if (SelectedVentilationRow == null)
            {
                return;
            }

            bool removed = ruleService.RemoveRule(SelectedVentilationRow);
            StatusText = removed
                ? "Ручное правило выбранного типа удалено. Выполняется пересборка."
                : "Для выбранной строки не найдено сохранённое ручное правило. Выполняется пересборка.";
            requestCollectVentilation?.Invoke(this);
        }

        private void NotifyCounts()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(WarningCount));
            OnPropertyChanged(nameof(ErrorCount));
        }
    }
}
