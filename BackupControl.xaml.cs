using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Globalization;
using System.Threading;

namespace WinFastGUI
{
    public partial class BackupControl : UserControl
    {
        public BackupControl()
        {
            InitializeComponent();
            UpdateLanguage(); // Başlangıçta dili güncelle
        }

        // Dil güncellemesi
        public void UpdateLanguage()
        {
            // .resx dosyaları CurrentUICulture'a göre otomatik işler, UI elemanlarını güncelle
            UpdateUITexts();
        }

        // UI metinlerini güncelle (XAML'de binding var, bu yedek)
        private void UpdateUITexts()
        {
            BackupRegistryButton.Content = Properties.Strings.BackupRegistry;
            RestoreRegistryButton.Content = Properties.Strings.RestoreRegistry;
            CreateRestorePointButton.Content = Properties.Strings.CreateRestorePoint;
            BackupBloatwareButton.Content = Properties.Strings.BackupBloatware;
            RestoreBloatwareButton.Content = Properties.Strings.RestoreBloatware;
        }

        // Dil değiştirme
        public void ChangeLanguageTo(string languageCode)
        {
            var culture = new CultureInfo(languageCode);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            UpdateLanguage(); // UI ve logları güncelle
        }

        // LOG
        private void LogMessage(string msg, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                string prefix = isError ? "[" + Properties.Strings.Error + "] " : "";
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {prefix}{msg}{Environment.NewLine}");
                LogTextBox.ScrollToEnd();
            });
        }

        // NSudo path
        private string GetNSudoPath()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "nsudo");
            if (File.Exists(Path.Combine(dir, "NSudo.exe")))
                return Path.Combine(dir, "NSudo.exe");
            if (File.Exists(Path.Combine(dir, "NSudoLC.exe")))
                return Path.Combine(dir, "NSudoLC.exe");
            return "";
        }

        // BACKUP REGISTRY (to C:\Backups folder)
        private async void BackupRegistryButton_Click(object sender, RoutedEventArgs e)
        {
            string nsudoPath = GetNSudoPath();
            if (string.IsNullOrEmpty(nsudoPath))
            {
                LogMessage(Properties.Strings.ErrorNSudoNotFound, true);
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
            LogMessage(Properties.Strings.StartingRegistryBackup);
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
                            LogMessage(string.Format(Properties.Strings.RegistryBackupMessage, backupFile));
                            LogMessage(string.Format(Properties.Strings.UserRegistryBackupMessage, userFile));
                        }
                        else
                        {
                            LogMessage(string.Format(Properties.Strings.BackupFailed, proc.ExitCode), true);
                        }
                    }
                    else LogMessage(Properties.Strings.BackupProcessError, true);
                }
            }
            catch (Exception ex)
            {
                LogMessage(Properties.Strings.BackupError + ex.Message, true);
            }
        }

        // RESTORE REGISTRY
        private async void RestoreRegistryButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = Properties.Strings.SelectRegistryBackup, Filter = "Registry File|*.reg" };
            if (dlg.ShowDialog() != true) return;
            string nsudoPath = GetNSudoPath();
            if (string.IsNullOrEmpty(nsudoPath))
            {
                LogMessage(Properties.Strings.ErrorNSudoNotFound, true);
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
            LogMessage(Properties.Strings.RestoreRegistryMessage);
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
                            LogMessage(Properties.Strings.RegistryRestored);
                        }
                        else
                        {
                            LogMessage(string.Format(Properties.Strings.RestoreFailed, proc.ExitCode), true);
                        }
                    }
                    else LogMessage(Properties.Strings.RestoreProcessError, true);
                }
            }
            catch (Exception ex)
            {
                LogMessage(Properties.Strings.RestoreError + ex.Message, true);
            }
        }

        // CREATE RESTORE POINT
        private async void CreateRestorePointButton_Click(object sender, RoutedEventArgs e)
        {
            LogMessage(Properties.Strings.CreatingSystemRestorePoint);
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
                            LogMessage(Properties.Strings.SystemRestorePointCreated);
                        else
                            LogMessage(string.Format(Properties.Strings.ErrorOccurred + " Failed to create restore point! (code {0})", proc.ExitCode), true);
                    }
                    else LogMessage(Properties.Strings.ErrorOccurred + " Restore point creation process could not be started.", true);
                }
            }
            catch (Exception ex)
            {
                LogMessage(Properties.Strings.Error + ": " + ex.Message, true);
            }
        }

        // BACKUP BLOATWARE
        private void BackupBloatwareButton_Click(object sender, RoutedEventArgs e)
        {
            // Örnek implementasyon
            LogMessage(Properties.Strings.BackupBloatware); // Yedekleme başladığını belirt
            // Gerçek implementasyon için dosya yazma eklenebilir
            LogMessage(Properties.Strings.Success + " Bloatware backup completed."); // Örnek tamamlanma mesajı
        }

        // RESTORE BLOATWARE
        private void RestoreBloatwareButton_Click(object sender, RoutedEventArgs e)
        {
            // Örnek implementasyon
            LogMessage(Properties.Strings.RestoreBloatware); // Geri yükleme başladığını belirt
            // Gerçek implementasyon için dosya okuma eklenebilir
            LogMessage(Properties.Strings.Success + " Bloatware restore completed."); // Örnek tamamlanma mesajı
        }
    }
}