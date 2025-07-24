using System;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WinFastGUI.Services; // DİKKAT: Sadece kullanıyoruz, tipleri burada tanımlamayacağız

namespace WinFastGUI.Controls
{
    public partial class ImageBackupControl : UserControl
    {
        private readonly VssBackupService _backupService;

        public ImageBackupControl()
        {
            InitializeComponent();
            _backupService = new VssBackupService();
            TargetPathBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "System_Backups");
        }

        private void BrowseTargetBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Klasör Seç"
            };
            if (dialog.ShowDialog() == true)
            {
                string? directoryPath = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(directoryPath))
                {
                    TargetPathBox.Text = directoryPath;
                }
            }
        }

        private async void TakeImageBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!IsRunningAsAdmin())
            {
                MessageBox.Show("Bu işlem için uygulama Yönetici olarak çalıştırılmalıdır.", "Yetki Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (string.IsNullOrWhiteSpace(TargetPathBox.Text))
            {
                MessageBox.Show("Lütfen geçerli bir hedef klasör seçin.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetUiState(false);
            LogBox.Clear();
            ProgressBar.Value = 0;

            string backupDir = TargetPathBox.Text;
            string imageType = (ImageTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "WIM";
            string mountLetter = "X:";

            var progressHandler = new Progress<BackupProgressReport>(report =>
            {
                switch (report.Type)
                {
                    case ReportType.Log:
                        LogBox.AppendText($"{DateTime.Now:HH:mm:ss} - {report.Message}{Environment.NewLine}");
                        LogBox.ScrollToEnd();
                        break;
                    case ReportType.Progress:
                        ProgressBar.Value = report.Percent;
                        break;
                    case ReportType.Error:
                        LogBox.AppendText($"{DateTime.Now:HH:mm:ss} - HATA: {report.Message}{Environment.NewLine}");
                        LogBox.ScrollToEnd();
                        break;
                    case ReportType.Result:
                        if (report.Success)
                        {
                            MessageBox.Show($"Yedekleme başarıyla tamamlandı!\n\nDosya: {report.ResultPath}", "İşlem Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        break;
                }
            });

            try
            {
                await _backupService.CreateBackupWithDosdevWimlibAsync(backupDir, imageType, mountLetter, progressHandler);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kritik bir hata oluştu:\n\n{ex.Message}", "İşlem Başarısız", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetUiState(true);
            }
        }

        private void SetUiState(bool isEnabled)
        {
            TakeImageBtn.IsEnabled = isEnabled;
            BrowseTargetBtn.IsEnabled = isEnabled;
            ImageTypeCombo.IsEnabled = isEnabled;
        }

        private bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
