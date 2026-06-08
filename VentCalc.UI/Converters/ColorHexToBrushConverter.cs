using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VentCalc.UI.Converters
{
    public sealed class ColorHexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                string? hex = value as string;
                if (string.IsNullOrWhiteSpace(hex))
                {
                    return Brushes.Transparent;
                }

                return (Brush)new BrushConverter().ConvertFromString(hex)!;
            }
            catch (Exception)
            {
                return Brushes.Transparent;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
