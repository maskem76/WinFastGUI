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
using System.Globalization;
using System.Threading;

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

        private readonly Dictionary<string, string> categoryTranslations = new Dictionary<string, string>
        {
            { "SystemPerformance", "System Performance" },
            { "BackgroundApps", "Background Apps" },
            { "ExplorerSettings", "Explorer Settings" },
            { "VisualEffects", "Visual Effects" },
            { "SearchSettings", "Search Settings" },
            { "AdvancedNetworkTweaks", "Advanced Network Tweaks" },
            { "GameMode", "Game Mode" },
            { "MMCSS_Profiles", "MMCSS Profiles" },
            { "TelemetryAndPrivacy", "Telemetry and Privacy" },
            { "WindowsDefender", "Windows Defender" },
            { "CoreIsolation", "Core Isolation" },
            { "WindowsUpdates", "Windows Updates" },
            { "Updates_CompleteDisable", "Updates Complete Disable" },
            { "MicrosoftEdge", "Microsoft Edge" },
            { "Input_Optimizations", "Input Optimizations" },
            { "MPO_Optimization", "MPO Optimization" },
            { "AutomaticOptimization", "Automatic Optimization" },
            { "GeneralCleanup", "General Cleanup" },
            { "CleanRegistry", "Clean Registry" },
            { "Network", "Network" },
            { "CS2Recommendations", "CS2 Recommendations" },
            { "EventLogging", "Event Logging" },
            { "MemoryOptimizations", "Memory Optimizations" },
            { "StorageOptimizations", "Storage Optimizations" },
            { "GpuOptimizations", "GPU Optimizations" },
            { "VirtualMemory", "Virtual Memory" },
            { "SpecificDevices", "Specific Devices" }
        };

        public OptimizationManagerControl()
        {
            InitializeComponent();
            WriteLog(Properties.Strings.OptimizationManagerStarted, "INFO");
            LoadOptimizations();
            InitializeButtons();

            logWatcher = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            logWatcher.Tick += LogWatcher_Tick!;
            logWatcher.Start();
        }

        public void UpdateLanguage()
        {
            SelectAllButton!.Content = Properties.Strings.SelectAll;
            SelectSafeButton!.Content = Properties.Strings.SelectSafeOptions;
            ClearSelectionButton!.Content = Properties.Strings.ClearSelection;
            ImportNvidiaNibButton!.Content = Properties.Strings.ImportNvidiaNib;
            OpenPowerProfileButton!.Content = Properties.Strings.OpenPowerProfile;
            OpenCoreAssignmentButton!.Content = Properties.Strings.OpenCoreAssignment;
            RunTweaksButton!.Content = Properties.Strings.RunSelectedTweaks;
            ApplyButton!.Content = Properties.Strings.Apply;
            CancelButton!.Content = Properties.Strings.Cancel;
            StatusLabel!.Content = Properties.Strings.StatusReady;

            foreach (var cb in checkboxes)
            {
                if (cb.Tag is OptimizationItem item)
                {
                    cb.Content = $"{TranslateText(item.Key, "Title")} ({TranslateCategory(item.Category)})";
                }
            }
        }

        public void ChangeLanguageTo(string languageCode)
        {
            var culture = new CultureInfo(languageCode);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            UpdateLanguage();
        }

        private string TranslateCategory(string category)
        {
            return categoryTranslations.TryGetValue(category, out var translation) ? translation : category;
        }

        private string TranslateText(string key, string field = "Title")
        {
            var currentLang = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;
            var item = OptimizationItems.FirstOrDefault(i => i.Key == key);
            if (item == null) return key;

            return currentLang switch
            {
                "en" => field switch
                {
                    "Title" => item.TitleEn ?? item.Title,
                    "Description" => item.DescriptionEn ?? item.Description,
                    "Warning" => item.WarningEn ?? item.Warning,
                    _ => key
                },
                _ => field switch
                {
                    "Title" => item.Title,
                    "Description" => item.Description,
                    "Warning" => item.Warning,
                    _ => key
                }
            };
        }

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
                        MessageBox.Show(Properties.Strings.OptimizationCompleted + lastLineRead, Properties.Strings.Information, MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (IOException ex)
            {
                WriteLog(string.Format(Properties.Strings.LogFileReadError, ex.Message), "ERROR");
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
                WriteLog(string.Format(Properties.Strings.LoadingOptimizationList, OptimizationListPath), "INFO");
                if (!File.Exists(OptimizationListPath)) { ShowError(string.Format(Properties.Strings.OptimizationFileNotFound, OptimizationListPath)); return; }
                var json = File.ReadAllText(OptimizationListPath);
                OptimizationItems = JsonSerializer.Deserialize<List<OptimizationItem>>(json) ?? new();
                WriteLog(string.Format(Properties.Strings.OptimizationsLoaded, OptimizationItems.Count), "INFO");

                OptimizationOptionsPanel?.Children.Clear();
                checkboxes.Clear();
                foreach (var item in OptimizationItems)
                {
                    var cb = new CheckBox
                    {
                        Content = $"{TranslateText(item.Key, "Title")} ({TranslateCategory(item.Category)})",
                        Tag = item,
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize = 14,
                        Foreground = TranslateText(item.Key, "Warning").ToLower().Contains("kritik") ? Brushes.Red : Brushes.LightGray,
                        Margin = new Thickness(5),
                        ToolTip = $"{TranslateText(item.Key, "Description")}\nWarning: {TranslateText(item.Key, "Warning")}"
                    };
                    checkboxes.Add(cb);
                    OptimizationOptionsPanel?.Children.Add(cb);
                }
            }
            catch (Exception ex)
            {
                ShowError(string.Format(Properties.Strings.OptimizationLoadError, ex.Message));
            }
        }

        private void ShowError(string message)
        {
            WriteLog(message, "ERROR");
            MessageBox.Show(message, Properties.Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (selected.Count == 0) { ShowError(Properties.Strings.SelectAtLeastOneOptimization); return; }
            if (!File.Exists(ExecutorScriptPath)) { ShowError(Properties.Strings.ExecutorNotFound); return; }

            StatusLabel!.Content = Properties.Strings.StatusOptimizing;
            foreach (var item in selected)
            {
                var cmdKey = item!.Key;
                WriteLog(string.Format(Properties.Strings.RunningCommand, cmdKey), "INFO");
                await Task.Run(() =>
                {
                    if (!File.Exists(NSudoPath)) { WriteLog(Properties.Strings.NsudoNotFound, "ERROR"); return; }
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
                            WriteLog(string.Format(Properties.Strings.CommandCompleted, cmdKey, outp), "SUCCESS");
                        }
                        else
                        {
                            WriteLog(string.Format(Properties.Strings.CommandFailed, cmdKey, err, outp), "ERROR");
                        }
                    });
                });
                await Task.Delay(250);
            }
            StatusLabel!.Content = Properties.Strings.StatusCompleted;
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            WriteLog(Properties.Strings.CancelClicked, "INFO");
            Window.GetWindow(this)?.Close();
        }

        private void ImportNvidiaNibButton_Click(object? sender, RoutedEventArgs e)
        {
            WriteLog(Properties.Strings.NvidiaNibClicked, "INFO");
            var dlg = new OpenFileDialog
            {
                Filter = "Nvidia Profile (*.nip;*.txt)|*.nip;*.txt|Tüm Dosyalar|*.*",
                Title = Properties.Strings.NvidiaProfileSelect
            };
            if (dlg.ShowDialog() == true)
            {
                var sel = dlg.FileName;
                var exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Modules\nvidiaProfileInspector.exe");
                if (!File.Exists(exe)) { ShowError(Properties.Strings.NvidiaInspectorNotFound); return; }
                var psi = new ProcessStartInfo(exe, $"/import \"{sel}\"") { UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                WriteLog(Properties.Strings.NvidiaProfileImported, "SUCCESS");
            }
        }

        private void OpenPowerProfileButton_Click(object? sender, RoutedEventArgs e)
        {
            WriteLog(Properties.Strings.PowerProfileClicked, "INFO");
            var dlg = new OpenFileDialog { Filter = "Power Profile (*.pow)|*.pow|Tüm Dosyalar|*.*", Title = Properties.Strings.PowerProfileSelect };
            if (dlg.ShowDialog() == true)
            {
                var sel = dlg.FileName;
                var psi = new ProcessStartInfo("powercfg.exe", $"/import \"{sel}\"") { UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                WriteLog(Properties.Strings.PowerProfileLoaded, "SUCCESS");
            }
        }

        private async void OpenCoreAssignmentButton_Click(object? sender, RoutedEventArgs e)
        {
            WriteLog(Properties.Strings.CoreAssignmentClicked, "INFO");
            var dlg = new OpenFileDialog { Filter = "Application (*.exe)|*.exe", Title = Properties.Strings.AppForCoreAssignment };
            if (dlg.ShowDialog() != true) return;
            if (!int.TryParse(Microsoft.VisualBasic.Interaction.InputBox(Properties.Strings.EnterMask, "", "3"), out int mask)) { ShowError(Properties.Strings.InvalidMask); return; }

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
                Dispatcher.Invoke(() => WriteLog(proc != null && proc.ExitCode == 0 ? Properties.Strings.CoreAssignmentSuccess : string.Format(Properties.Strings.CoreAssignmentError, err), proc != null && proc.ExitCode == 0 ? "SUCCESS" : "ERROR"));
            });
        }

        public class OptimizationItem
        {
            public string Key { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty; // Türkçe
            public string TitleEn { get; set; } = string.Empty; // İngilizce
            public string Description { get; set; } = string.Empty; // Türkçe
            public string DescriptionEn { get; set; } = string.Empty; // İngilizce
            public string Warning { get; set; } = string.Empty; // Türkçe
            public string WarningEn { get; set; } = string.Empty; // İngilizce
        }
    }
}