using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace WinFastGUI.Converters
{
    public class MaskToCpuTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long mask)
            {
                List<string> cores = new();
                for (int i = 0; i < Environment.ProcessorCount; i++)
                    if (((mask >> i) & 1) == 1)
                        cores.Add($"CPU{i}");
                return "Atanan çekirdekler: " + (cores.Count > 0 ? string.Join(", ", cores) : "YOK");
            }
            return "Atanan çekirdekler: YOK";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}