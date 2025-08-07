using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WinFastGUI.Controls
{
    public class BoolToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return Brushes.LightGreen;
            return Brushes.IndianRed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}