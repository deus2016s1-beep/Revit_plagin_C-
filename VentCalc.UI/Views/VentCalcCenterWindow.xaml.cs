using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VentCalc.Core.Models;
using VentCalc.UI.ViewModels;

namespace VentCalc.UI.Views
{
    public partial class VentCalcCenterWindow : Window
    {
        private bool isLoadedOnce;

        public VentCalcCenterWindow(VentCalcCenterViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(VentCalcCenterViewModel.StatusText) ||
                DataContext is not VentCalcCenterViewModel { StatusText: "Система загружена." })
            {
                return;
            }

            Dispatcher.BeginInvoke(SelectDefaultTabAfterLoad, DispatcherPriority.Background);
        }

        private void SelectDefaultTabAfterLoad()
        {
            string targetHeader = DataContext is VentCalcCenterViewModel viewModel
                ? viewModel.Settings.UiDefaultTabAfterLoad
                : "Расчёт";

            foreach (TabItem tab in MainTabs.Items.OfType<TabItem>())
            {
                if (tab.Header?.ToString() == targetHeader)
                {
                    MainTabs.SelectedItem = tab;
                    return;
                }
            }
        }

        private void LocalResistancesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not VentCalcCenterViewModel viewModel || sender is not DataGrid grid)
            {
                return;
            }

            viewModel.SetSelectedLocalResistanceRows(grid.SelectedItems.OfType<LocalResistanceCalculationInfo>());
        }

        private void OpenProjectZetaCatalog_Click(object sender, RoutedEventArgs e)
        {
            var window = new ProjectZetaCatalogWindow
            {
                Owner = this,
                DataContext = DataContext
            };
            window.ShowDialog();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (isLoadedOnce || DataContext is not VentCalcCenterViewModel viewModel)
            {
                return;
            }

            isLoadedOnce = true;
            viewModel.LoadSelectedSystemCommand.Execute(null);
        }
    }
}
