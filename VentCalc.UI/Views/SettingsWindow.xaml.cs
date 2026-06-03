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
    }
}
