using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace WinFastGUI.Controls
{
    public partial class DismPlusImageControl : UserControl
    {
        public DismPlusImageControl()
        {
            InitializeComponent();
        }

        private void OpenDismPlusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dismppPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "Dism++x64.exe");
                if (System.IO.File.Exists(dismppPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dismppPath,
                        UseShellExecute = true
                    });
                    LogBox.Text = "Dism++ başarıyla başlatıldı.";
                }
                else
                {
                    LogBox.Text = "Dism++ dosyası bulunamadı: " + dismppPath;
                }
            }
            catch (System.Exception ex)
            {
                LogBox.Text = "Hata: " + ex.Message;
            }
        }
    }
}
