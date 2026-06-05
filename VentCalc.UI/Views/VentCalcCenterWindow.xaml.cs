using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        }


        private void LocalResistancesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not VentCalcCenterViewModel viewModel || sender is not DataGrid grid)
            {
                return;
            }

            viewModel.SetSelectedLocalResistanceRows(grid.SelectedItems.OfType<LocalResistanceCalculationInfo>());
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
