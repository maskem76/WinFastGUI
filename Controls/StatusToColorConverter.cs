using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WinFastGUI.Controls
{
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status && status == "Running")
                return Brushes.LightGreen;
            return Brushes.LightCoral;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}