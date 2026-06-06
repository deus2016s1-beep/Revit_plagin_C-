using System.Windows;

namespace VentCalc.UI.Views
{
    public partial class ProjectZetaCatalogWindow : Window
    {
        public ProjectZetaCatalogWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
