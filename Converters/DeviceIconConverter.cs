using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace WinFastGUI.Converters
{
    public class DeviceIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string category = (value as string)?.ToLower() ?? "diğer";
            string iconPath = category switch
            {
                "pci" => "Images/pci_icon.png",
                "usb" => "Images/usb_icon.png",
                "ağ" => "Images/network_icon.png",
                "depolama" => "Images/storage_icon.png",
                "gpu" => "Images/display_icon.png",
                "ses" => "Images/audio_icon.png",
                "bluetooth" => "Images/bluetooth_icon.png",
                "görüntü" => "Images/display_icon.png",
                "denetleyici" => "Images/controller_icon.png",
                "kamera" => "Images/camera_icon.png",
                "yazıcı" => "Images/printer_icon.png",
                "monitör" => "Images/monitor_icon.png",
                _ => "Images/default_icon.png"
            };
            try
            {
                return new BitmapImage(new Uri($"pack://application:,,,/{iconPath}", UriKind.Absolute));
            }
            catch
            {
                return new BitmapImage(new Uri("pack://application:,,,/Images/default_icon.png", UriKind.Absolute));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}