using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VentCalc.UI.Services;
using VentCalc.UI.ViewModels;

namespace VentCalc.UI.Views
{
    public partial class SpecCenterWindow : Window
    {
        private SpecCenterViewModel? viewModel;
        private bool isAttached;

        public SpecCenterWindow(SpecCenterViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            this.viewModel = viewModel;
            Loaded += (_, _) => SafeAttachViewModel(viewModel);
            Dispatcher.UnhandledException += (_, args) =>
            {
                args.Handled = true;
                viewModel.ReportUiError(args.Exception);
                MessageBox.Show(args.Exception.Message, "SpecCalc", MessageBoxButton.OK, MessageBoxImage.Warning);
            };
        }

        private void SafeAttachViewModel(SpecCenterViewModel model)
        {
            try
            {
                AttachViewModel(model);
            }
            catch (Exception exception)
            {
                model.ReportUiError(exception);
                MessageBox.Show(exception.Message, "SpecCalc", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AttachViewModel(SpecCenterViewModel model)
        {
            if (isAttached) return;
            isAttached = true;
            if (model.ActiveColumns is INotifyCollectionChanged collectionChanged)
            {
                collectionChanged.CollectionChanged += (_, _) => SafeRebuildSpecColumns();
            }

            foreach (SpecColumnLayout column in model.ColumnLayouts)
            {
                column.PropertyChanged += Column_PropertyChanged;
            }

            SafeRebuildSpecColumns();
        }

        private void Column_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SpecColumnLayout.VisibleInMain) or nameof(SpecColumnLayout.Header) or nameof(SpecColumnLayout.Order) or nameof(SpecColumnLayout.Format))
            {
                SafeRebuildSpecColumns();
            }
        }

        private void SafeRebuildSpecColumns()
        {
            try
            {
                RebuildSpecColumns();
            }
            catch (Exception exception)
            {
                viewModel?.ReportUiError(exception);
                SpecRowsGrid.Columns.Clear();
                PreviewGrid.Columns.Clear();
            }
        }

        private void RebuildSpecColumns()
        {
            if (viewModel == null) return;
            SpecRowsGrid.Columns.Clear();
                PreviewGrid.Columns.Clear();
            DataTemplate? selectionTemplate = TryFindResource("SelectionCheckBoxTemplate") as DataTemplate;
            SpecRowsGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "✓",
                Width = new DataGridLength(36),
                CellTemplate = selectionTemplate
            });

            foreach (SpecColumnLayout activeColumn in viewModel.ActiveColumns)
            {
                activeColumn.PropertyChanged -= Column_PropertyChanged;
                activeColumn.PropertyChanged += Column_PropertyChanged;
            }

            foreach (SpecColumnLayout column in viewModel.ActiveColumns.Where(column => column.VisibleInMain && !string.IsNullOrWhiteSpace(column.FieldName) && column.FieldName != "ImagePath").OrderBy(column => column.Order))
            {
                var gridColumn = CreateTextColumn(column);
                SpecRowsGrid.Columns.Add(gridColumn);
                PreviewGrid.Columns.Add(CreateTextColumn(column));
            }
        }


        private static DataGridTextColumn CreateTextColumn(SpecColumnLayout column)
        {
            return new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new Binding(column.FieldName) { StringFormat = column.IsNumeric ? "{0:0.##}" : null },
                IsReadOnly = true,
                Width = column.FieldName == "Name" || column.FieldName == "Note" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto
            };
        }

        private void SpecRowsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                viewModel?.UpdateSelectedSpecRows(SpecRowsGrid.SelectedItems.OfType<SpecGroupRow>());
            }
            catch (Exception exception)
            {
                viewModel?.ReportUiError(exception);
            }
        }
    }
}
