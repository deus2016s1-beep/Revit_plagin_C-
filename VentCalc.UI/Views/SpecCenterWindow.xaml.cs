using System.Windows;
using VentCalc.UI.ViewModels;

namespace VentCalc.UI.Views
{
    public partial class SpecCenterWindow : Window
    {
        public SpecCenterWindow(SpecCenterViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
