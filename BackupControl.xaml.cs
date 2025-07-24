using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WinFastGUI
{
    public partial class BackupControl : UserControl
    {
        public BackupControl()
        {
            InitializeComponent();
        }

        // LOG
        private void LogMessage(string msg, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                string prefix = isError ? "[HATA] " : "";
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {prefix}{msg}{Environment.NewLine}");
                LogTextBox.ScrollToEnd();
            });
        }

        // NSudo yolu
        private string GetNSudoPath()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "nsudo");
            if (File.Exists(Path.Combine(dir, "NSudo.exe")))
                return Path.Combine(dir, "NSudo.exe");
            if (File.Exists(Path.Combine(dir, "NSudoLC.exe")))
                return Path.Combine(dir, "NSudoLC.exe");
            return "";
        }

        // 1) REGISTRY YEDEKLE (C:\Backups klasörüne)
        private async void BackupRegistryButton_Click(object sender, RoutedEventArgs e)
        {
            string nsudoPath = GetNSudoPath();
            if (string.IsNullOrEmpty(nsudoPath))
            {
                LogMessage("NSudo bulunamadı! Lütfen Modules\\nsudo klasörüne NSudo.exe veya NSudoLC.exe ekleyin.", true);
                return;
            }
            string backupDir = @"C:\Backups";
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
            string fileName = $"RegistryBackup_{DateTime.Now:yyyyMMdd_HHmmss}.reg";
            string backupFile = Path.Combine(backupDir, fileName);
            string userFile = Path.Combine(backupDir, fileName.Replace(".reg", "_CU.reg"));

            string psScript = $"reg export HKLM \"{backupFile}\" /y; reg export HKCU \"{userFile}\" /y";
            var psi = new ProcessStartInfo
            {
                FileName = nsudoPath,
                Arguments = $"-U:T -P:E -Wait powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            LogMessage("Kayıt defteri yedeği başlatılıyor...");
            try
            {
                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        string output = await proc.StandardOutput.ReadToEndAsync();
                        string error = await proc.StandardError.ReadToEndAsync();
                        await proc.WaitForExitAsync();
                        if (!string.IsNullOrWhiteSpace(output)) LogMessage(output);
                        if (!string.IsNullOrWhiteSpace(error)) LogMessage(error, true);
                        if (proc.ExitCode == 0)
                        {
                            LogMessage($"Kayıt defteri yedeği: {backupFile}");
                            LogMessage($"Kullanıcı kayıt defteri yedeği: {userFile}");
                        }
                        else
                        {
                            LogMessage($"Yedekleme işleminde hata! (kod {proc.ExitCode})", true);
                        }
                    }
                    else LogMessage("Hata: Yedekleme süreci başlatılamadı.", true);
                }
            }
            catch (Exception ex)
            {
                LogMessage("Yedekleme sırasında hata oluştu: " + ex.Message, true);
            }
        }

        // 2) REGISTRY GERİ YÜKLE
        private async void RestoreRegistryButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Registry Yedek Dosyasını Seç (.reg)", Filter = "Registry Dosyası|*.reg" };
            if (dlg.ShowDialog() != true) return;
            string nsudoPath = GetNSudoPath();
            if (string.IsNullOrEmpty(nsudoPath))
            {
                LogMessage("NSudo bulunamadı! Lütfen Modules\\nsudo klasörüne NSudo.exe veya NSudoLC.exe ekleyin.", true);
                return;
            }
            string filePath = dlg.FileName;
            string psScript = $"reg import \"{filePath}\"";
            var psi = new ProcessStartInfo
            {
                FileName = nsudoPath,
                Arguments = $"-U:T -P:E -Wait powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            LogMessage("Kayıt defteri geri yüklemesi başlatılıyor...");
            try
            {
                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        string output = await proc.StandardOutput.ReadToEndAsync();
                        string error = await proc.StandardError.ReadToEndAsync();
                        await proc.WaitForExitAsync();
                        if (!string.IsNullOrWhiteSpace(output)) LogMessage(output);
                        if (!string.IsNullOrWhiteSpace(error)) LogMessage(error, true);
                        if (proc.ExitCode == 0)
                        {
                            LogMessage("Kayıt defteri geri yüklendi.");
                        }
                        else
                        {
                            LogMessage($"Geri yüklemede hata! (kod {proc.ExitCode})", true);
                        }
                    }
                    else LogMessage("Hata: Geri yükleme süreci başlatılamadı.", true);
                }
            }
            catch (Exception ex)
            {
                LogMessage("Geri yükleme sırasında hata oluştu: " + ex.Message, true);
            }
        }

        // 3) SİSTEM GERİ YÜKLEME NOKTASI OLUŞTUR
        private async void CreateRestorePointButton_Click(object sender, RoutedEventArgs e)
        {
            LogMessage("Sistem geri yükleme noktası oluşturuluyor...");
            string psScript = @"Checkpoint-Computer -Description 'WinFastGUI' -RestorePointType 'MODIFY_SETTINGS'";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            try
            {
                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        string output = await proc.StandardOutput.ReadToEndAsync();
                        string error = await proc.StandardError.ReadToEndAsync();
                        await proc.WaitForExitAsync();
                        if (!string.IsNullOrWhiteSpace(output)) LogMessage(output);
                        if (!string.IsNullOrWhiteSpace(error)) LogMessage(error, true);
                        if (proc.ExitCode == 0)
                            LogMessage("Sistem geri yükleme noktası oluşturuldu.");
                        else
                            LogMessage($"Geri yükleme noktası oluşturulamadı! (kod {proc.ExitCode})", true);
                    }
                    else LogMessage("Hata: Geri yükleme noktası oluşturma süreci başlatılamadı.", true);
                }
            }
            catch (Exception ex)
            {
                LogMessage("Hata: " + ex.Message, true);
            }
        }

        // 4) BLOATWARE YEDEKLE (örnek, masaüstüne json atar)
        private void BackupBloatwareButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedApps = new List<string> { "Microsoft.3DBuilder", "Microsoft.BingWeather" };
            string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BloatwareBackup.json");
            File.WriteAllText(filePath, JsonSerializer.Serialize(selectedApps));
            LogMessage("Bloatware yedeği masaüstüne kaydedildi: " + filePath);
        }

        // 5) BLOATWARE GERİ YÜKLE (örnek)
        private void RestoreBloatwareButton_Click(object sender, RoutedEventArgs e)
        {
            string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BloatwareBackup.json");
            if (!File.Exists(filePath))
            {
                LogMessage("Yedek dosyası bulunamadı: " + filePath, true);
                return;
            }
            var appList = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(filePath));
            if (appList != null)
                LogMessage("Bloatware yedeği geri yüklendi. (Liste: " + string.Join(", ", appList) + ")");
            else
                LogMessage("Hata: Bloatware listesi yüklenemedi.", true);
        }

        // VSS Snapshot silme işlevi örneği (panelde uygun yerde kullan)
        public async Task<bool> DeleteAllVssSnapshots()
        {
            // Tüm C: snapshotlarını siler
            var psi = new ProcessStartInfo
            {
                FileName = GetNSudoPath(),
                Arguments = $"-U:T -P:E -Wait cmd.exe /c \"vssadmin delete shadows /for=C: /all /quiet\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            try
            {
                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        string output = await proc.StandardOutput.ReadToEndAsync();
                        string error = await proc.StandardError.ReadToEndAsync();
                        await proc.WaitForExitAsync();
                        if (!string.IsNullOrWhiteSpace(output)) LogMessage(output);
                        if (!string.IsNullOrWhiteSpace(error)) LogMessage(error, true);
                        if (proc.ExitCode == 0)
                        {
                            LogMessage("Tüm VSS snapshot'lar silindi.");
                            return true;
                        }
                        else
                        {
                            LogMessage($"Snapshot silinemedi! (kod {proc.ExitCode})", true);
                            return false;
                        }
                    }
                    else
                    {
                        LogMessage("VSS silme işlemi başlatılamadı.", true);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("VSS silme sırasında hata oluştu: " + ex.Message, true);
                return false;
            }
        }
    }
}
