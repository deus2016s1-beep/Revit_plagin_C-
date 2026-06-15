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

        public SpecCenterWindow(SpecCenterViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            this.viewModel = viewModel;
            Loaded += (_, _) => AttachViewModel(viewModel);
        }

        private void AttachViewModel(SpecCenterViewModel model)
        {
            if (model.ActiveColumns is INotifyCollectionChanged collectionChanged)
            {
                collectionChanged.CollectionChanged += (_, _) => RebuildSpecColumns();
            }

            foreach (SpecColumnLayout column in model.ColumnLayouts)
            {
                column.PropertyChanged += Column_PropertyChanged;
            }

            RebuildSpecColumns();
        }

        private void Column_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SpecColumnLayout.VisibleInMain) or nameof(SpecColumnLayout.Header) or nameof(SpecColumnLayout.Order))
            {
                RebuildSpecColumns();
            }
        }

        private void RebuildSpecColumns()
        {
            if (viewModel == null) return;
            SpecRowsGrid.Columns.Clear();
            SpecRowsGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "✓",
                Width = new DataGridLength(36),
                CellTemplate = (DataTemplate)FindResource("SelectionCheckBoxTemplate")
            });

            foreach (SpecColumnLayout column in viewModel.ActiveColumns.Where(column => column.VisibleInMain).OrderBy(column => column.Order))
            {
                SpecRowsGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = column.Header,
                    Binding = new Binding(column.FieldName) { StringFormat = column.IsNumeric ? "{0:0.##}" : null },
                    IsReadOnly = true,
                    Width = column.FieldName == "Note" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto
                });
            }
        }

        private void SpecRowsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            viewModel?.UpdateSelectedSpecRows(SpecRowsGrid.SelectedItems.OfType<SpecGroupRow>());
        }
    }
}
