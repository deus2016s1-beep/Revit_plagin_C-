using System.Windows;
using VentCalc.UI.ViewModels;

namespace VentCalc.UI.Views
{
    public partial class PathHighlightWindow : Window
    {
        public PathHighlightWindow(VentCalcCenterViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
