using System;
using System.Globalization;
using System.Windows.Data;

namespace WinFastGUI.Converters
{
    public class LogLevelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string logEntry = value?.ToString() ?? "";
            return logEntry.Contains("[Error]") ? "Error" : "Info";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}