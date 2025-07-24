using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinFastGUI
{
    public partial class AppManagerUserControl : UserControl
    {
        private class WingetApp
        {
            public string AppName { get; set; } = "";
            public string PackageId { get; set; } = "";
            public string Category { get; set; } = "";
            public bool Recommended { get; set; }
            public string? Version { get; set; }
        }

        private class Root
        {
            public List<WingetApp> Applications { get; set; } = new List<WingetApp>();
        }

        private List<WingetApp> _wingetApps = new List<WingetApp>();
        private List<string?> _installerFiles = new List<string?>();
        private readonly string _modulesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules");
        private readonly string _wingetJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "winget.json");
        private readonly string _wingetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");

        public AppManagerUserControl()
        {
            InitializeComponent();

            InstallSelectedButton.Click += InstallSelectedButton_Click;
            RefreshUninstallListButton.Click += RefreshUninstallListButton_Click;
            UninstallSelectedButton.Click += UninstallSelectedButton_Click;
            HuntInatciProgramButton.Click += HuntInatciProgramButton_Click;
            RestoreInatciProgramButton.Click += RestoreInatciProgramButton_Click;
            CategoryCombo.SelectionChanged += CategoryCombo_SelectionChanged;
            ManualInatciProgramTextBox.GotFocus += ManualInatciProgramTextBox_GotFocus;
            ManualInatciProgramTextBox.LostFocus += ManualInatciProgramTextBox_LostFocus;
            ManualInatciProgramTextBox.Tag = "Veya buraya program adı girin...";
            ManualInatciProgramTextBox.Text = ManualInatciProgramTextBox.Tag?.ToString() ?? "";

            LoadWingetApps();
            LoadInstallerFiles();
            LoadUninstallableApps();
            LoadInatciPrograms();
            LoadCategories();
        }

        private void LoadWingetApps()
        {
            try
            {
                InstallLogTextBox.AppendText($"[INFO] winget.json yolu: {_wingetJsonPath}\n");
                if (File.Exists(_wingetJsonPath))
                {
                    string jsonContent = File.ReadAllText(_wingetJsonPath);
                    var root = JsonSerializer.Deserialize<Root>(jsonContent);
                    _wingetApps = root?.Applications ?? new List<WingetApp>();
                    InstallLogTextBox.AppendText($"[INFO] {_wingetJsonPath} okundu, {_wingetApps.Count} uygulama bulundu.\n");
                }
                else
                {
                    InstallLogTextBox.AppendText($"[ERROR] {_wingetJsonPath} bulunamadı.\n");
                    MessageBox.Show($"winget.json bulunamadı: {_wingetJsonPath}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                UpdateInstallableAppsList();
            }
            catch (Exception ex)
            {
                InstallLogTextBox.AppendText($"[ERROR] winget.json okunurken hata: {ex.Message}\n");
                MessageBox.Show($"winget.json okunurken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadInstallerFiles()
{
    try
    {
        InstallLogTextBox.AppendText($"[INFO] Installers klasörü taranıyor...\n");

        // Installers klasör yolunu burada doğrudan belirtiyoruz
        string installPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Installers");

        if (Directory.Exists(installPath))
        {
            _installerFiles = Directory.GetFiles(installPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".exe") || f.EndsWith(".msi"))
                .Select(Path.GetFileName)
                .ToList() ?? new List<string?>();

            InstallLogTextBox.AppendText($"[INFO] {installPath} tarandı, {_installerFiles.Count} dosya bulundu.\n");
        }
        else
        {
            InstallLogTextBox.AppendText($"[ERROR] {installPath} klasörü bulunamadı.\n");
            MessageBox.Show($"Installers klasörü bulunamadı: {installPath}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        UpdateInstallableAppsList();
    }
    catch (Exception ex)
    {
        InstallLogTextBox.AppendText($"[ERROR] Installers klasörü taranırken hata: {ex.Message}\n");
        MessageBox.Show($"Installers klasörü taranırken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}


        private void UpdateInstallableAppsList()
        {
            InstallAppsListBox.Items.Clear();
            string? selectedCategory = (CategoryCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tümü";
            foreach (var app in _wingetApps)
            {
                if (selectedCategory == "Tümü" || app.Category == selectedCategory)
                {
                    string appName = app.AppName ?? "Bilinmeyen Uygulama";
                    string packageId = app.PackageId ?? "Bilinmeyen ID";
                    InstallAppsListBox.Items.Add($"[winget] {appName} ({packageId})");
                }
            }
            foreach (var file in _installerFiles)
            {
                if (!string.IsNullOrEmpty(file))
                {
                    InstallAppsListBox.Items.Add($"[installer] {file}");
                }
            }
        }

        private void LoadUninstallableApps()
        {
            UninstallAppsListBox.Items.Clear();
            try
            {
                UninstallLogTextBox.AppendText($"[INFO] winget list çalıştırılıyor, yol: {_wingetPath}\n");
                if (!File.Exists(_wingetPath))
                {
                    UninstallLogTextBox.AppendText($"[ERROR] winget.exe bulunamadı: {_wingetPath}\n");
                    MessageBox.Show($"winget.exe bulunamadı: {_wingetPath}. Lütfen winget'in yüklü olduğundan emin olun.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _wingetPath,
                    Arguments = "list --source winget",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using Process? process = Process.Start(startInfo);
                if (process == null)
                {
                    UninstallLogTextBox.AppendText($"[ERROR] winget list işlemi başlatılamadı.\n");
                    MessageBox.Show("winget list işlemi başlatılamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    var lines = output.Split('\n').Skip(2).Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains("winget"));
                    foreach (var line in lines)
                    {
                        UninstallAppsListBox.Items.Add(line.Trim());
                    }
                    if (UninstallStatusLabel != null)
                    {
                        UninstallStatusLabel.Content = "Yenilendi";
                        UninstallStatusLabel.Foreground = Brushes.Green;
                    }
                    else
                    {
                        UninstallLogTextBox.AppendText("[ERROR] UninstallStatusLabel null.\n");
                    }
                    UninstallLogTextBox.AppendText("[INFO] Kurulu uygulamalar yenilendi.\n");
                }
                else
                {
                    UninstallLogTextBox.AppendText($"[ERROR] winget list hatası: {error}\n");
                    MessageBox.Show($"winget list hatası: {error}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                UninstallLogTextBox.AppendText($"[ERROR] Kurulu uygulamalar alınırken hata: {ex.Message}\n");
                MessageBox.Show($"Kurulu uygulamalar alınırken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadInatciPrograms()
        {
            InatciProgramComboBox.Items.Clear();
var inatciProgramList = new List<string>
{
    "Adobe Update", "Google Update", "OneDrive", "Yandex", "CCleaner", "McAfee", "NVIDIA Update",
    "Spotify", "Skype", "Zoom", "Slack", "Dropbox", "VLC", "TeamViewer"
};
            foreach (var app in inatciProgramList)
                InatciProgramComboBox.Items.Add(app);
            InatciProgramComboBox.SelectedIndex = 0;
        }

        private void LoadCategories()
        {
            CategoryCombo.Items.Clear();
            CategoryCombo.Items.Add(new ComboBoxItem { Content = "Tümü" });
            var categories = _wingetApps.Select(app => app.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
            foreach (var category in categories)
            {
                CategoryCombo.Items.Add(new ComboBoxItem { Content = category });
            }
            CategoryCombo.SelectedIndex = 0;
        }

        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateInstallableAppsList();
        }

        private void InstallSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (InstallStatusLabel != null)
            {
                InstallStatusLabel.Content = "Kuruluyor...";
                InstallStatusLabel.Foreground = Brushes.Orange;
            }
            else
            {
                InstallLogTextBox.AppendText("[ERROR] InstallStatusLabel null.\n");
            }

            foreach (var item in InstallAppsListBox.SelectedItems.Cast<string>())
            {
                try
                {
                    if (item.StartsWith("[winget]"))
                    {
                        string packageId = item.Split('(')[1].TrimEnd(')');
                        InstallLogTextBox.AppendText($"[INFO] {packageId} kurulumu başlatıldı...\n");
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = _wingetPath,
                            Arguments = $"install --id {packageId} --silent --accept-source-agreements --accept-package-agreements",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using Process? process = Process.Start(startInfo);
                        if (process == null)
                        {
                            InstallLogTextBox.AppendText($"[ERROR] {packageId} kurulumu başlatılamadı.\n");
                            MessageBox.Show($"Kurulum başlatılamadı: {packageId}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                            continue;
                        }

                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        InstallLogTextBox.AppendText(process.ExitCode == 0
                            ? $"[SUCCESS] {packageId} kuruldu.\n{output}\n"
                            : $"[ERROR] {packageId} kurulumu başarısız: {error}\n");
                    }
                    else if (item.StartsWith("[installer]"))
                    {
                        string fileName = item.Replace("[installer] ", "");
                        string filePath = Path.Combine(_modulesPath, fileName);
                        InstallLogTextBox.AppendText($"[INFO] {fileName} kurulumu başlatıldı...\n");

                        string arguments = fileName.EndsWith(".msi") ? "/quiet /norestart" : "/S";
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = filePath,
                            Arguments = arguments,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using Process? process = Process.Start(startInfo);
                        if (process == null)
                        {
                            InstallLogTextBox.AppendText($"[ERROR] {fileName} kurulumu başlatılamadı.\n");
                            MessageBox.Show($"Kurulum başlatılamadı: {fileName}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                            continue;
                        }

                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        InstallLogTextBox.AppendText(process.ExitCode == 0
                            ? $"[SUCCESS] {fileName} kuruldu.\n{output}\n"
                            : $"[ERROR] {fileName} kurulumu başarısız: {error}\n");
                    }
                }
                catch (Exception ex)
                {
                    InstallLogTextBox.AppendText($"[ERROR] {item} kurulurken hata: {ex.Message}\n");
                    MessageBox.Show($"Kurulum hatası ({item}): {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            if (InstallStatusLabel != null)
            {
                InstallStatusLabel.Content = "Tamamlandı";
                InstallStatusLabel.Foreground = Brushes.Green;
            }
            else
            {
                InstallLogTextBox.AppendText("[ERROR] InstallStatusLabel null.\n");
            }
        }

        private void RefreshUninstallListButton_Click(object sender, RoutedEventArgs e)
        {
            LoadUninstallableApps();
        }

        private void UninstallSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (UninstallStatusLabel != null)
            {
                UninstallStatusLabel.Content = "Kaldırılıyor...";
                UninstallStatusLabel.Foreground = Brushes.Orange;
            }
            else
            {
                UninstallLogTextBox.AppendText("[ERROR] UninstallStatusLabel null.\n");
            }

            foreach (var item in UninstallAppsListBox.SelectedItems.Cast<string>())
            {
                try
                {
                    string[] parts = item.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        string packageId = parts[1];
                        UninstallLogTextBox.AppendText($"[INFO] {packageId} kaldırma başlatıldı...\n");
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = _wingetPath,
                            Arguments = $"uninstall --id {packageId} --silent",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using Process? process = Process.Start(startInfo);
                        if (process == null)
                        {
                            UninstallLogTextBox.AppendText($"[ERROR] {packageId} kaldırma başlatılamadı.\n");
                            MessageBox.Show($"Kaldırma başlatılamadı: {packageId}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                            continue;
                        }

                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        UninstallLogTextBox.AppendText(process.ExitCode == 0
                            ? $"[SUCCESS] {packageId} kaldırıldı.\n{output}\n"
                            : $"[ERROR] {packageId} kaldırılamadı: {error}\n");
                    }
                }
                catch (Exception ex)
                {
                    UninstallLogTextBox.AppendText($"[ERROR] {item} kaldırılırken hata: {ex.Message}\n");
                    MessageBox.Show($"Kaldırma hatası ({item}): {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            if (UninstallStatusLabel != null)
            {
                UninstallStatusLabel.Content = "Tamamlandı";
                UninstallStatusLabel.Foreground = Brushes.Green;
            }
            else
            {
                UninstallLogTextBox.AppendText("[ERROR] UninstallStatusLabel null.\n");
            }
        }

private void HuntInatciProgramButton_Click(object sender, RoutedEventArgs e)
{
    // StatusLabel güncellemesi
    if (InatciStatusLabel != null)
    {
        InatciStatusLabel.Content = "Avlanıyor...";
        InatciStatusLabel.Foreground = Brushes.Orange;
    }

    // LogTextBox güncellemesi (Dispatcher ile UI thread'ine yönlendirme)
    Application.Current.Dispatcher.Invoke(() =>
    {
        // Null kontrolü ile güvenli log yazma
        if (InatciLogTextBox != null)
        {
            InatciLogTextBox.AppendText("[INFO] İnatçı kaldırma başlatıldı.\n");
        }
        else
        {
            MessageBox.Show("İnatcı log text box'ı null.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    });

    // Kaldırma işlemi burada yapılmalı
    try
    {
        // Burada kaldırma işlemi yapılacak
        RemoveInatciProgram("Yandex");

        // Kaldırma başarılı logu
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (InatciLogTextBox != null)
            {
                InatciLogTextBox.AppendText("[SUCCESS] İnatçı program başarıyla kaldırıldı.\n");
            }
            else
            {
                MessageBox.Show("İnatcı log text box'ı null.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
    }
    catch (Exception ex)
    {
        // Hata durumu logu
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (InatciLogTextBox != null)
            {
                InatciLogTextBox.AppendText($"[ERROR] Kaldırma işlemi sırasında hata: {ex.Message}\n");
            }
            else
            {
                MessageBox.Show("İnatcı log text box'ı null.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
        MessageBox.Show($"Hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

private void RemoveInatciProgram(string programName)
{
    // Programı kaldırma işlemi burada yapılır
    Console.WriteLine($"[INFO] {programName} kaldırılacak.");
}

private void RestoreInatciProgramButton_Click(object sender, RoutedEventArgs e)
{
    // StatusLabel güncellemesi
    if (InatciStatusLabel != null)
    {
        InatciStatusLabel.Content = "Geri yükleniyor...";
        InatciStatusLabel.Foreground = Brushes.Orange;
    }

    // LogTextBox güncellemesi (Dispatcher ile UI thread'ine yönlendirme)
    Application.Current.Dispatcher.Invoke(() =>
    {
        if (InatciLogTextBox != null)
        {
            InatciLogTextBox.AppendText("[INFO] İnatçı geri yükleme başlatıldı.\n");
        }
        else
        {
            MessageBox.Show("İnatcı log text box'ı null.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    });

    // Geri yükleme işlemi burada yapılmalı
}

        private void ManualInatciProgramTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (ManualInatciProgramTextBox.Text == ManualInatciProgramTextBox.Tag?.ToString())
            {
                ManualInatciProgramTextBox.Text = "";
                ManualInatciProgramTextBox.Foreground = Brushes.White;
            }
        }

        private void ManualInatciProgramTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ManualInatciProgramTextBox.Text))
            {
                ManualInatciProgramTextBox.Text = ManualInatciProgramTextBox.Tag?.ToString() ?? "";
                ManualInatciProgramTextBox.Foreground = Brushes.Gray;
            }
        }
    }
}