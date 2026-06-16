using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace VentCalc.UI.Views
{
    public partial class ExcelExportResultWindow : Window
    {
        public ExcelExportResultWindow(string filePath)
        {
            InitializeComponent();
            FilePath = filePath;
            DataContext = this;
        }

        public string FilePath { get; }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            StartShell(FilePath);
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                StartShell(directory);
            }
        }

        private static void StartShell(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "VentCalc", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
