using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
                Width = new DataGridLength(35),
                MinWidth = 35,
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
                Width = GetColumnWidth(column),
                MinWidth = GetColumnMinWidth(column)
            };
        }


        private static DataGridLength GetColumnWidth(SpecColumnLayout column)
        {
            return column.FieldName == "Name" || column.FieldName == "Note"
                ? new DataGridLength(1, DataGridLengthUnitType.Star)
                : new DataGridLength(GetColumnMinWidth(column));
        }

        private static double GetColumnMinWidth(SpecColumnLayout column)
        {
            return column.FieldName switch
            {
                "Name" => 350,
                "Size" => 110,
                "Unit" => 70,
                "Quantity" => 80,
                "LengthM" => 80,
                "AreaM2" => 90,
                "ImagePath" => 110,
                _ => 90
            };
        }

        private void ColumnList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || sender is not ListBox listBox) return;
            if (e.OriginalSource is not DependencyObject source || FindAncestor<ListBoxItem>(source) is not ListBoxItem item) return;
            if (item.DataContext is SpecColumnLayout column) DragDrop.DoDragDrop(listBox, column, DragDropEffects.Move);
        }

        private void ColumnList_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(SpecColumnLayout)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void ActiveColumnsList_Drop(object sender, DragEventArgs e)
        {
            if (viewModel == null || !e.Data.GetDataPresent(typeof(SpecColumnLayout))) return;
            var moved = e.Data.GetData(typeof(SpecColumnLayout)) as SpecColumnLayout;
            var target = e.OriginalSource is DependencyObject source ? FindAncestor<ListBoxItem>(source)?.DataContext as SpecColumnLayout : null;
            if (moved == null) return;
            if (viewModel.AvailableColumns.Contains(moved)) viewModel.ActivateColumn(moved);
            else viewModel.MoveColumnBefore(moved, target);
        }

        private void AvailableColumnsList_Drop(object sender, DragEventArgs e)
        {
            if (viewModel == null || !e.Data.GetDataPresent(typeof(SpecColumnLayout))) return;
            var moved = e.Data.GetData(typeof(SpecColumnLayout)) as SpecColumnLayout;
            if (moved != null && viewModel.ActiveColumns.Contains(moved)) viewModel.DeactivateColumn(moved);
        }

        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
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
