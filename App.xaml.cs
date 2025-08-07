using System;
using System.IO;
using System.Windows;
using System.Diagnostics; // Process ve WindowsIdentity için gerekli (loglama için)
using System.Security.Principal; // WindowsIdentity için gerekli (loglama için)
using System.Text;

namespace WinFastGUI
{
    public partial class App : Application
    {
        public App()
        {
			 Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // Uygulama başlangıç logu
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string temp = Path.GetTempPath();
                string logText = $"[APP_CTOR_START] {DateTime.Now} Args: {string.Join(";", Environment.GetCommandLineArgs())}\n";

                // Şu anki kullanıcı kimliğini de loga ekleyelim, debug için faydalı.
                try
                {
                    string currentIdentity = WindowsIdentity.GetCurrent().Name;
                    logText += $"[CURRENT_USER] {currentIdentity}\n";
                }
                catch (Exception ex)
                {
                    logText += $"[CURRENT_USER_ERROR] {ex.Message}\n";
                }

                File.AppendAllText(Path.Combine(desktop, "WinFast_startup.log"), logText);
                File.AppendAllText(Path.Combine(temp, "WinFast_startup.log"), logText);
            }
            catch { /* Loglama hatası olursa uygulamanın çökmesini engelle. */ }

            // GLOBAL EXCEPTION HANDLERS (Aynı kalacak)
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    string temp = Path.GetTempPath();
                    Exception ex = (Exception)e.ExceptionObject;
                    string logText = $"[CRASH: AppDomain] {DateTime.Now}\n{ex}\n";
                    File.AppendAllText(Path.Combine(desktop, "WinFast_crash.log"), logText);
                    File.AppendAllText(Path.Combine(temp, "WinFast_crash.log"), logText);
                }
                catch { }
                if (Application.Current != null) Application.Current.Shutdown(); else Environment.Exit(1);
            };

            this.DispatcherUnhandledException += (s, e) =>
            {
                try
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    string temp = Path.GetTempPath();
                    string logText = $"[CRASH: Dispatcher] {DateTime.Now}\n{e.Exception}\n";
                    File.AppendAllText(Path.Combine(desktop, "WinFast_crash.log"), logText);
                    File.AppendAllText(Path.Combine(temp, "WinFast_crash.log"), logText);
                }
                catch { }
                if (Application.Current != null) Application.Current.Shutdown(); else Environment.Exit(1);
            };
        }

        // Uygulamanın başlangıç noktası. MainWindow'u açacak.
        // Bu metot, App.xaml'deki Startup="Application_Startup" ile eşleşir.
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                var wnd = new MainWindow(); // MainWindow projenizde olduğundan emin olun.
                wnd.Show();
                File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "WinFast_window_opened.log"), $"MainWindow açıldı: {DateTime.Now} (Yetki: {WindowsIdentity.GetCurrent().Name})\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "WinFast_window_crash.log"), $"MainWindow açılırken kritik hata: {DateTime.Now}\n{ex}\n");
                MessageBox.Show($"Uygulama başlatılırken veya MainWindow açılırken kritik bir hata oluştu:\n{ex.Message}", "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);

                if (Application.Current != null) Application.Current.Shutdown(); else Environment.Exit(1);
            }
        }
    }
}
