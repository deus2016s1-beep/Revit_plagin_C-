using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VentCalc.UI
{
    public sealed class InspectorResultWindow : Window
    {
        public InspectorResultWindow(string reportText)
        {
            Title = "VentCalc — Инспектор выбранного элемента";
            Width = 820;
            Height = 680;
            MinWidth = 640;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var textBox = new TextBox
            {
                Text = reportText,
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Margin = new Thickness(12)
            };

            Content = textBox;
        }
    }
}
