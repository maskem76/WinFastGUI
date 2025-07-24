// ImageToIsoControl.xaml.cs
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WinFastGUI.Services;

namespace WinFastGUI.Controls
{
    public partial class ImageToIsoControl : UserControl
    {
        private readonly IsoCreationService _isoService;

        public ImageToIsoControl()
        {
            InitializeComponent();
            _isoService = new IsoCreationService();
            TemplateComboBox.SelectedIndex = 0; // Varsayılan olarak ilkini seç
        }

        private void BrowseWimButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "WIM Dosyaları (*.wim)|*.wim|Tüm Dosyalar (*.*)|*.*",
                Title = "Yedek WIM Dosyasını Seçin"
            };
            if (dialog.ShowDialog() == true)
            {
                WimPathTextBox.Text = dialog.FileName;
            }
        }

        private void SaveIsoButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "ISO Dosyası (*.iso)|*.iso",
                Title = "Oluşturulacak ISO Dosyasını Kaydedin"
            };
            if (dialog.ShowDialog() == true)
            {
                IsoPathTextBox.Text = dialog.FileName;
            }
        }

        private async void CreateIsoButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(WimPathTextBox.Text) ||
                string.IsNullOrWhiteSpace(IsoPathTextBox.Text) ||
                TemplateComboBox.SelectedItem == null)
            {
                MessageBox.Show("Lütfen tüm alanları doldurun.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetUiState(false);
            LogBox.Clear();
            ProgressBar.Value = 0;

            string wimPath = WimPathTextBox.Text;
            string isoPath = IsoPathTextBox.Text;
            string templateName = (TemplateComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() == "Windows 11 Şablonu"
                ? "MinimalISO_Template_11.zip"
                : "MinimalISO_Template_10.zip";
            
            // DÜZELTME: Yol, 'templates' alt klasörü olmadan, doğrudan 'Modules' klasörünü işaret edecek şekilde güncellendi.
            string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", templateName);

            var progressHandler = new Progress<BackupProgressReport>(report =>
            {
                switch (report.Type)
                {
                    case ReportType.Log:
                        LogBox.AppendText($"{report.Message}{Environment.NewLine}");
                        break;
                    case ReportType.Error:
                        LogBox.AppendText($"HATA: {report.Message}{Environment.NewLine}");
                        break;
                    case ReportType.Progress:
                        ProgressBar.Value = report.Percent;
                        break;
                    case ReportType.Result:
                        if (report.Success)
                        {
                            MessageBox.Show($"ISO başarıyla oluşturuldu!\n\nDosya: {report.ResultPath}", "İşlem Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        break;
                }
                LogBox.ScrollToEnd();
            });

            try
            {
                await _isoService.CreateIsoAsync(wimPath, isoPath, templatePath, progressHandler);
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
            BrowseWimButton.IsEnabled = isEnabled;
            SaveIsoButton.IsEnabled = isEnabled;
            TemplateComboBox.IsEnabled = isEnabled;
            CreateIsoButton.IsEnabled = isEnabled;
        }
    }
}
