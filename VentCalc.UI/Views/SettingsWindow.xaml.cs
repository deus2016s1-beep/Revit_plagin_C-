using System.Windows;
using VentCalc.UI.ViewModels;

namespace VentCalc.UI.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow(VentCalcCenterViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is VentCalcCenterViewModel viewModel && !viewModel.SaveSettingsFromWindow())
            {
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
