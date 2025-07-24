using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WinFastGUI.Controls
{
    public partial class OptimizationManagerControl : UserControl
    {
        private List<OptimizationItem> OptimizationItems { get; set; } = new();
        private readonly string OptimizationListPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data\OptimizationList.json");
        private readonly string ModulePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Modules\OptimizationTweaks.psm1");
        private readonly string NSudoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Modules\nsudo\NSudoLC.exe");
        private readonly string ExecutorScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Modules\Executor.ps1");
        private readonly string logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "PS1_CALIŞTI.txt");

        private DispatcherTimer? logWatcher;
        private string lastLogLine = string.Empty;
        private readonly List<CheckBox> checkboxes = new();

        public OptimizationManagerControl()
        {
            InitializeComponent();
            WriteLog("OptimizationManagerControl başlatıldı.", "INFO");
            LoadOptimizations();
            InitializeButtons();

            logWatcher = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            logWatcher.Tick += LogWatcher_Tick!;
            logWatcher.Start();
        }

        // Null güvenli event handler
        private void LogWatcher_Tick(object? sender, EventArgs e)
        {
            if (!File.Exists(logFilePath)) return;
            try
            {
                var lines = File.ReadAllLines(logFilePath);
                var lastLineRead = lines.LastOrDefault();
                if (!string.IsNullOrEmpty(lastLineRead) && lastLineRead != lastLogLine)
                {
                    lastLogLine = lastLineRead!;
                    WriteLog(lastLineRead, lastLineRead.Contains("TAMAMLANDI:") ? "SUCCESS" : "INFO");
                    if (lastLineRead.Contains("TAMAMLANDI:"))
                        MessageBox.Show("Optimizasyon tamamlandı: " + lastLineRead, "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (IOException ex)
            {
                WriteLog($"Log dosyası okunamadı: {ex.Message}", "ERROR");
            }
        }

        private void InitializeButtons()
        {
            SelectAllButton!.Click += SelectAllButton_Click!;
            SelectSafeButton!.Click += SelectSafeButton_Click!;
            ClearSelectionButton!.Click += ClearSelectionButton_Click!;
            RunTweaksButton!.Click += StartSelectedButton_Click!;
            ApplyButton!.Click += StartSelectedButton_Click!;
            CancelButton!.Click += CancelButton_Click!;
            ImportNvidiaNibButton!.Click += ImportNvidiaNibButton_Click!;
            OpenPowerProfileButton!.Click += OpenPowerProfileButton_Click!;
            OpenCoreAssignmentButton!.Click += OpenCoreAssignmentButton_Click!;
        }

        private void WriteLog(string message, string level = "INFO")
        {
            var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
            Dispatcher.Invoke(() =>
            {
                LogTextBox?.AppendText(logLine + Environment.NewLine);
                LogTextBox?.ScrollToEnd();
            });
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinFastGUI.log"), logLine + Environment.NewLine);
            }
            catch { }
            Debug.WriteLine(logLine);
        }

        private void LoadOptimizations()
        {
            try
            {
                WriteLog($"OptimizationList.json yükleniyor: {OptimizationListPath}", "INFO");
                if (!File.Exists(OptimizationListPath)) { ShowError($"Optimizasyon dosyası bulunamadı: {OptimizationListPath}"); return; }
                var json = File.ReadAllText(OptimizationListPath);
                OptimizationItems = JsonSerializer.Deserialize<List<OptimizationItem>>(json) ?? new();
                WriteLog($"{OptimizationItems.Count} optimizasyon ayarı yüklendi.", "INFO");

                OptimizationOptionsPanel?.Children.Clear();
                checkboxes.Clear();
                foreach (var item in OptimizationItems)
                {
                    var cb = new CheckBox
                    {
                        Content = $"{item.Title} ({item.Category})",
                        Tag = item,
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize = 14,
                        Foreground = item.Warning.ToLower().Contains("kritik") ? Brushes.Red : Brushes.LightGray,
                        Margin = new Thickness(5)
                    };
                    checkboxes.Add(cb);
                    OptimizationOptionsPanel?.Children.Add(cb);
                }
            }
            catch (Exception ex)
            {
                ShowError($"Optimizasyon ayarları yüklenirken hata oluştu: {ex.Message}");
            }
        }

        private void ShowError(string message)
        {
            WriteLog(message, "ERROR");
            MessageBox.Show(message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void SelectAllButton_Click(object? sender, RoutedEventArgs e) => SetAllCheckboxes(true);
        private void SelectSafeButton_Click(object? sender, RoutedEventArgs e) => SetAllCheckboxes(item => !item.Warning.ToLower().Contains("kritik"));
        private void ClearSelectionButton_Click(object? sender, RoutedEventArgs e) => SetAllCheckboxes(false);

        private void SetAllCheckboxes(bool value)
        {
            foreach (var cb in checkboxes) cb.IsChecked = value;
        }

        private void SetAllCheckboxes(Func<OptimizationItem, bool> pred)
        {
            foreach (var cb in checkboxes)
                cb.IsChecked = cb.Tag is OptimizationItem itm && pred(itm);
        }

        private async void StartSelectedButton_Click(object? sender, RoutedEventArgs e)
        {
            var selected = checkboxes.Where(cb => cb.IsChecked == true).Select(cb => cb.Tag as OptimizationItem).Where(i => i != null).ToList();
            if (selected.Count == 0) { ShowError("Lütfen en az bir optimizasyon seçin."); return; }
            if (!File.Exists(ExecutorScriptPath)) { ShowError("Executor.ps1 bulunamadı!"); return; }

            StatusLabel!.Content = "🟡 Optimizasyon Başlatıldı...";
            foreach (var item in selected)
            {
                var cmdKey = item!.Key;
                WriteLog($"{cmdKey} komutu çalıştırılıyor...", "INFO");
                await Task.Run(() =>
                {
                    if (!File.Exists(NSudoPath)) { WriteLog("NSudoLC.exe bulunamadı.", "ERROR"); return; }
                    var psi = new ProcessStartInfo
                    {
                        FileName = NSudoPath,
                        Arguments =
                            "/U:T /P:E /M:S /UseCurrentConsole " +
                            $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{ExecutorScriptPath}\" -OptimizationCommand \"{cmdKey}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = Path.GetDirectoryName(NSudoPath)
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit();
                    var outp = proc?.StandardOutput.ReadToEnd().Trim();
                    var err = proc?.StandardError.ReadToEnd().Trim();
                    Dispatcher.Invoke(() =>
                    {
                        if (proc != null && proc.ExitCode == 0 && string.IsNullOrEmpty(err))
                        {
                            WriteLog($"{cmdKey} tamamlandı: {outp}", "SUCCESS");
                        }
                        else
                        {
                            WriteLog($"{cmdKey} hatayla tamamlandı: {err} (Çıktı: {outp})", "ERROR");
                        }
                    });
                });
                await Task.Delay(250);
            }
            StatusLabel!.Content = "🟩 Tüm işlemler tamamlandı.";
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            WriteLog("İptal butonuna tıklandı.", "INFO");
            Window.GetWindow(this)?.Close();
        }

        private void ImportNvidiaNibButton_Click(object? sender, RoutedEventArgs e)
        {
            WriteLog("Nvidia NIB Aktar butonuna tıklandı.", "INFO");
            var dlg = new OpenFileDialog
            {
                Filter = "Nvidia Profile (*.nip;*.txt)|*.nip;*.txt|Tüm Dosyalar|*.*",
                Title = "Nvidia Profilini Seç"
            };
            if (dlg.ShowDialog() == true)
            {
                var sel = dlg.FileName;
                var exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Modules\nvidiaProfileInspector.exe");
                if (!File.Exists(exe)) { ShowError("nvidiaProfileInspector.exe bulunamadı!"); return; }
                var psi = new ProcessStartInfo(exe, $"/import \"{sel}\"") { UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                WriteLog("Nvidia profili başarıyla içe aktarıldı.", "SUCCESS");
            }
        }

        private void OpenPowerProfileButton_Click(object? sender, RoutedEventArgs e)
        {
            WriteLog("Güç Profilini Yükle butonuna tıklandı.", "INFO");
            var dlg = new OpenFileDialog { Filter = "Güç Profili (*.pow)|*.pow|Tüm Dosyalar|*.*", Title = "Güç Profilini Seç" };
            if (dlg.ShowDialog() == true)
            {
                var sel = dlg.FileName;
                var psi = new ProcessStartInfo("powercfg.exe", $"/import \"{sel}\"") { UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                WriteLog("Güç profili başarıyla yüklendi.", "SUCCESS");
            }
        }

        private async void OpenCoreAssignmentButton_Click(object? sender, RoutedEventArgs e)
        {
            WriteLog("Çekirdek Atama butonuna tıklandı.", "INFO");
            var dlg = new OpenFileDialog { Filter = "Uygulama (*.exe)|*.exe", Title = "Çekirdek Ataması Yapılacak Uygulama" };
            if (dlg.ShowDialog() != true) return;
            if (!int.TryParse(Microsoft.VisualBasic.Interaction.InputBox("Çekirdek maskesi:", "", "3"), out int mask)) { ShowError("Geçersiz maskesi."); return; }

            string psScript = $"$p = Start-Process -FilePath '{dlg.FileName}' -PassThru; Start-Sleep 2; $p.ProcessorAffinity = {mask}";
            await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = NSudoPath,
                    Arguments =
                        "/U:S /P:E /M:S /UseCurrentConsole " +
                        $"powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
                var err = proc?.StandardError.ReadToEnd();
                Dispatcher.Invoke(() => WriteLog(proc != null && proc.ExitCode == 0 ? "Çekirdek ataması yapıldı." : $"Çekirdek ataması hatası: {err}", proc != null && proc.ExitCode == 0 ? "SUCCESS" : "ERROR"));
            });
        }

        public class OptimizationItem
        {
            public string Key { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Warning { get; set; } = string.Empty;
        }
    }
}
