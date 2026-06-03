using System.Windows;
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
