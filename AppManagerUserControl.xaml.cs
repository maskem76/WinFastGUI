using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinFastGUI.Properties;

namespace WinFastGUI
{
    public partial class AppManagerUserControl : UserControl
    {
        private readonly Dictionary<string, string> categoryTranslations = new Dictionary<string, string>
        {
            { "Tools", "Araçlar" },
            { "Communication", "İletişim" },
            { "Games", "Oyun" },
            { "Browsers", "Tarayıcılar" },
            { "Multimedia", "Multimedya" },
            { "Graphics", "Grafik" },
            { "Download Managers", "İndirme Yöneticileri" },
            { "Office", "Ofis" },
            { "Developer Tools", "Geliştirici Araçları" },
            { "Design", "Tasarım" },
            { "Remote Access", "Uzaktan Erişim" },
            { "System Tools", "Sistem Araçları" },
            { "Network", "Ağ" },
            { "Security", "Güvenlik" },
            { "All", "Tümü" }
        };

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

            SetDefaultLanguage();

            InstallSelectedButton.Click += InstallSelectedButton_Click;
            RefreshUninstallListButton.Click += RefreshUninstallListButton_Click;
            UninstallSelectedButton.Click += UninstallSelectedButton_Click;
            HuntInatciProgramButton.Click += HuntInatciProgramButton_Click;
            RestoreInatciProgramButton.Click += RestoreInatciProgramButton_Click;
            CategoryCombo.SelectionChanged += CategoryCombo_SelectionChanged;
            ManualInatciProgramTextBox.GotFocus += ManualInatciProgramTextBox_GotFocus;
            ManualInatciProgramTextBox.LostFocus += ManualInatciProgramTextBox_LostFocus;
            ManualInatciProgramTextBox.Tag = ManualInatciProgramTextBox.Tag ?? "Veya buraya program adı girin...";
            ManualInatciProgramTextBox.Text = ManualInatciProgramTextBox.Tag?.ToString() ?? string.Empty;

            UpdateLanguage();
            LoadWingetApps();
            LoadInstallerFiles();
            LoadUninstallableApps();
            LoadInatciPrograms();
            LoadCategories();
        }

        private void SetDefaultLanguage()
        {
            string? language = Properties.Settings.Default.Language;
            if (string.IsNullOrEmpty(language))
            {
                language = "tr-TR";
            }
            ChangeLanguageTo(language!);
        }

        private void ChangeLanguageTo(string culture)
        {
            Properties.Settings.Default.Language = culture;
            Properties.Settings.Default.Save();

            System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(culture);
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(culture);

            UpdateLanguage();
        }

        public void ChangeLanguage()
        {
            string? currentCulture = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;
            string newCulture = currentCulture == "tr" ? "en-US" : "tr-TR";
            ChangeLanguageTo(newCulture);
        }

        private void LoadWingetApps()
        {
            try
            {
                if (InstallLogTextBox != null)
                    InstallLogTextBox.AppendText($"[INFO] winget.json yolu: {_wingetJsonPath}\n");

                if (File.Exists(_wingetJsonPath))
                {
                    string jsonContent = File.ReadAllText(_wingetJsonPath);
                    var root = JsonSerializer.Deserialize<Root>(jsonContent);
                    _wingetApps = root?.Applications ?? new List<WingetApp>();

                    // Kategorileri Türkçe olarak bırak, çeviri dil değişiminde yapılacak
                    if (InstallLogTextBox != null)
                        InstallLogTextBox.AppendText($"[INFO] {_wingetJsonPath} okundu, {_wingetApps.Count} uygulama bulundu.\n");
                }
                else
                {
                    if (InstallLogTextBox != null)
                        InstallLogTextBox.AppendText($"[ERROR] {_wingetJsonPath} bulunamadı.\n");
                    MessageBox.Show($"winget.json bulunamadı: {_wingetJsonPath}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    LoadCategories();
                    UpdateInstallableAppsList(null);
                });
            }
            catch (Exception ex)
            {
                if (InstallLogTextBox != null)
                    InstallLogTextBox.AppendText($"[ERROR] winget.json okunurken hata: {ex.Message}\n");
                MessageBox.Show($"winget.json okunurken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadInstallerFiles()
        {
            try
            {
                if (InstallLogTextBox != null)
                    InstallLogTextBox.AppendText($"[INFO] Installers klasörü taranıyor...\n");
                string installPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Installers");
                if (Directory.Exists(installPath))
                {
                    _installerFiles = Directory.GetFiles(installPath, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => f.EndsWith(".exe") || f.EndsWith(".msi"))
                        .Select(Path.GetFileName)
                        .ToList() ?? new List<string?>();
                    if (InstallLogTextBox != null)
                        InstallLogTextBox.AppendText($"[INFO] {installPath} tarandı, {_installerFiles.Count} dosya bulundu.\n");
                }
                else
                {
                    if (InstallLogTextBox != null)
                        InstallLogTextBox.AppendText($"[ERROR] {installPath} klasörü bulunamadı.\n");
                    MessageBox.Show($"Installers klasörü bulunamadı: {installPath}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                Application.Current.Dispatcher.Invoke(() => UpdateInstallableAppsList(null));
            }
            catch (Exception ex)
            {
                if (InstallLogTextBox != null)
                    InstallLogTextBox.AppendText($"[ERROR] Installers klasörü taranırken hata: {ex.Message}\n");
                MessageBox.Show($"Installers klasörü taranırken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateInstallableAppsList(string? selectedTranslatedCategory = null)
        {
            if (InstallAppsListBox == null) return;
            InstallAppsListBox.Items.Clear();

            if (InstallLogTextBox != null)
                InstallLogTextBox.AppendText($"[DEBUG] Seçilen kategori: {selectedTranslatedCategory}\n");

            string currentLang = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;

            // "Tümü" seçeneği için özel kontrol
            if (selectedTranslatedCategory == (currentLang == "tr" ? "Tümü" : "All") || string.IsNullOrEmpty(selectedTranslatedCategory))
            {
                foreach (var app in _wingetApps)
                {
                    string displayCategory = currentLang == "tr" ? app.Category : categoryTranslations.FirstOrDefault(x => x.Value == app.Category).Key ?? app.Category;
                    InstallAppsListBox.Items.Add($"[winget] {app.AppName} ({app.PackageId}) - Kategori: {displayCategory}");
                }
                if (InstallLogTextBox != null)
                    InstallLogTextBox.AppendText($"[DEBUG] 'Tümü' için {_wingetApps.Count} uygulama eklendi.\n");
                return;
            }

            // Seçili kategori için filtreleme
            string originalCategory = currentLang == "tr" ? selectedTranslatedCategory! : categoryTranslations.FirstOrDefault(x => x.Value == selectedTranslatedCategory!).Key ?? selectedTranslatedCategory!;

            var filteredApps = _wingetApps.Where(app => app.Category == originalCategory).ToList();
            foreach (var app in filteredApps)
            {
                string displayCategory = currentLang == "tr" ? app.Category : categoryTranslations.FirstOrDefault(x => x.Value == app.Category).Key ?? app.Category;
                InstallAppsListBox.Items.Add($"[winget] {app.AppName} ({app.PackageId}) - Kategori: {displayCategory}");
            }
            if (InstallLogTextBox != null)
                InstallLogTextBox.AppendText($"[DEBUG] '{selectedTranslatedCategory}' için {filteredApps.Count} uygulama eklendi.\n");
        }

        private void LoadUninstallableApps()
        {
            if (UninstallAppsListBox == null || UninstallLogTextBox == null) return;
            UninstallAppsListBox.Items.Clear();
            try
            {
                if (UninstallLogTextBox != null)
                    UninstallLogTextBox.AppendText($"[INFO] winget list çalıştırılıyor, yol: {_wingetPath}\n");
                if (!File.Exists(_wingetPath))
                {
                    if (UninstallLogTextBox != null)
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
                    if (UninstallLogTextBox != null)
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
                        if (UninstallLogTextBox != null)
                            UninstallLogTextBox.AppendText("[ERROR] UninstallStatusLabel null.\n");
                    }
                    if (UninstallLogTextBox != null)
                        UninstallLogTextBox.AppendText("[INFO] Kurulu uygulamalar yenilendi.\n");
                }
                else
                {
                    if (UninstallLogTextBox != null)
                        UninstallLogTextBox.AppendText($"[ERROR] winget list hatası: {error}\n");
                    MessageBox.Show($"winget list hatası: {error}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                if (UninstallLogTextBox != null)
                    UninstallLogTextBox.AppendText($"[ERROR] Kurulu uygulamalar alınırken hata: {ex.Message}\n");
                MessageBox.Show($"Kurulu uygulamalar alınırken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadInatciPrograms()
        {
            if (InatciProgramComboBox == null) return;
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
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (CategoryCombo == null)
                {
                    InstallLogTextBox?.AppendText("[ERROR] CategoryCombo null.\n");
                    return;
                }
                CategoryCombo.Items.Clear();

                string currentLang = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;

                // "Tümü" ekle
                CategoryCombo.Items.Add(currentLang == "tr" ? "Tümü" : "All");
                InstallLogTextBox?.AppendText($"[DEBUG] 'Tümü' eklendi: {(currentLang == "tr" ? "Tümü" : "All")}\n");

                var categories = _wingetApps
                    .Select(app => app.Category)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();

                InstallLogTextBox?.AppendText($"[DEBUG] Ham kategoriler: {string.Join(", ", categories)}\n");

                foreach (var category in categories)
                {
                    string displayName = currentLang == "tr" ? category : categoryTranslations.FirstOrDefault(x => x.Value == category).Key ?? category;
                    CategoryCombo.Items.Add(displayName);
                    InstallLogTextBox?.AppendText($"[DEBUG] Kategori eklendi: {displayName}\n");
                }

                CategoryCombo.SelectedIndex = 0;
                InstallLogTextBox?.AppendText($"[DEBUG] Toplam {CategoryCombo.Items.Count} öğe eklendi.\n");
            });
        }

        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryCombo == null || CategoryCombo.SelectedItem == null) return;
            string? selectedCategory = CategoryCombo.SelectedItem.ToString();
            if (InstallLogTextBox != null)
                InstallLogTextBox.AppendText($"[DEBUG] Seçilen kategori: {selectedCategory}\n");
            UpdateInstallableAppsList(selectedCategory ?? "All");
        }

        private void InstallSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (InstallStatusLabel != null)
            {
                InstallStatusLabel.Content = "Kuruluyor...";
                InstallStatusLabel.Foreground = Brushes.Orange;
            }
            else if (InstallLogTextBox != null)
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
                        if (InstallLogTextBox != null)
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
                            if (InstallLogTextBox != null)
                                InstallLogTextBox.AppendText($"[ERROR] {packageId} kurulumu başlatılamadı.\n");
                            MessageBox.Show($"Kurulum başlatılamadı: {packageId}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                            continue;
                        }
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        if (InstallLogTextBox != null)
                            InstallLogTextBox.AppendText(process.ExitCode == 0
                                ? $"[SUCCESS] {packageId} kuruldu.\n{output}\n"
                                : $"[ERROR] {packageId} kurulumu başarısız: {error}\n");
                    }
                    else if (item.StartsWith("[installer]"))
                    {
                        string fileName = item.Replace("[installer] ", "");
                        string filePath = Path.Combine(_modulesPath, fileName);
                        if (InstallLogTextBox != null)
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
                            if (InstallLogTextBox != null)
                                InstallLogTextBox.AppendText($"[ERROR] {fileName} kurulumu başlatılamadı.\n");
                            MessageBox.Show($"Kurulum başlatılamadı: {fileName}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                            continue;
                        }
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        if (InstallLogTextBox != null)
                            InstallLogTextBox.AppendText(process.ExitCode == 0
                                ? $"[SUCCESS] {fileName} kuruldu.\n{output}\n"
                                : $"[ERROR] {fileName} kurulumu başarısız: {error}\n");
                    }
                }
                catch (Exception ex)
                {
                    if (InstallLogTextBox != null)
                        InstallLogTextBox.AppendText($"[ERROR] {item} kurulurken hata: {ex.Message}\n");
                    MessageBox.Show($"Kurulum hatası ({item}): {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            if (InstallStatusLabel != null)
            {
                InstallStatusLabel.Content = "Tamamlandı";
                InstallStatusLabel.Foreground = Brushes.Green;
            }
            else if (InstallLogTextBox != null)
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
            else if (UninstallLogTextBox != null)
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
                        if (UninstallLogTextBox != null)
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
                            if (UninstallLogTextBox != null)
                                UninstallLogTextBox.AppendText($"[ERROR] {packageId} kaldırma başlatılamadı.\n");
                            MessageBox.Show($"Kaldırma başlatılamadı: {packageId}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                            continue;
                        }
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        if (UninstallLogTextBox != null)
                            UninstallLogTextBox.AppendText(process.ExitCode == 0
                                ? $"[SUCCESS] {packageId} kaldırıldı.\n{output}\n"
                                : $"[ERROR] {packageId} kaldırılamadı: {error}\n");
                    }
                }
                catch (Exception ex)
                {
                    if (UninstallLogTextBox != null)
                        UninstallLogTextBox.AppendText($"[ERROR] {item} kaldırılırken hata: {ex.Message}\n");
                    MessageBox.Show($"Kaldırma hatası ({item}): {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            if (UninstallStatusLabel != null)
            {
                UninstallStatusLabel.Content = "Tamamlandı";
                UninstallStatusLabel.Foreground = Brushes.Green;
            }
            else if (UninstallLogTextBox != null)
            {
                UninstallLogTextBox.AppendText("[ERROR] UninstallStatusLabel null.\n");
            }
        }

        private void HuntInatciProgramButton_Click(object sender, RoutedEventArgs e)
        {
            if (InatciStatusLabel != null)
            {
                InatciStatusLabel.Content = "Avlanıyor...";
                InatciStatusLabel.Foreground = Brushes.Orange;
            }
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (InatciLogTextBox != null)
                {
                    InatciLogTextBox.AppendText("[INFO] İnatçı kaldırma başlatıldı.\n");
                }
                else
                {
                    MessageBox.Show("İnatcı log text box'ı null.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            try
            {
                RemoveInatciProgram("Yandex");
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
            Console.WriteLine($"[INFO] {programName} kaldırılacak.");
        }

        private void RestoreInatciProgramButton_Click(object sender, RoutedEventArgs e)
        {
            if (InatciStatusLabel != null)
            {
                InatciStatusLabel.Content = "Geri yükleniyor...";
                InatciStatusLabel.Foreground = Brushes.Orange;
            }
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
        }

        private void ManualInatciProgramTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (ManualInatciProgramTextBox != null && ManualInatciProgramTextBox.Text == ManualInatciProgramTextBox.Tag?.ToString())
            {
                ManualInatciProgramTextBox.Text = "";
                ManualInatciProgramTextBox.Foreground = Brushes.White;
            }
        }

        private void ManualInatciProgramTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (ManualInatciProgramTextBox != null && string.IsNullOrWhiteSpace(ManualInatciProgramTextBox.Text))
            {
                ManualInatciProgramTextBox.Text = ManualInatciProgramTextBox.Tag?.ToString() ?? string.Empty;
                ManualInatciProgramTextBox.Foreground = Brushes.Gray;
            }
        }

        public void UpdateLanguage()
        {
            string? currentLang = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;
            var tabInstaller = AppManagerTabs.Items[0] as TabItem;
            var tabRemover = AppManagerTabs.Items[1] as TabItem;
            var tabHunter = AppManagerTabs.Items[2] as TabItem;

            if (tabInstaller != null && tabInstaller.Header is Border installerBorder && installerBorder.Child is TextBlock installerText)
            {
                installerText.Text = currentLang == "tr" ? (Properties.Strings.ApplicationInstaller ?? "Uygulama Yöneticisi") : (Properties.Strings.ApplicationInstaller ?? "Application Manager");
            }
            if (tabRemover != null && tabRemover.Header is Border removerBorder && removerBorder.Child is TextBlock removerText)
            {
                removerText.Text = currentLang == "tr" ? (Properties.Strings.ApplicationRemover ?? "Uygulama Kaldırıcı") : (Properties.Strings.ApplicationRemover ?? "Application Remover");
            }
            if (tabHunter != null && tabHunter.Header is Border hunterTabBorder && hunterTabBorder.Child is TextBlock hunterTabText)
            {
                hunterTabText.Text = currentLang == "tr" ? (Properties.Strings.StubbornProgramHunter ?? "İnatçı Program Avcısı") : (Properties.Strings.StubbornProgramHunter ?? "Stubborn Program Hunter");
            }

            if (InstallableAppsText != null)
                InstallableAppsText.Text = currentLang == "tr" ? (Properties.Strings.InstallableApps ?? "Yüklenebilir Uygulamalar:") : (Properties.Strings.InstallableApps ?? "Installable Applications:");

            if (InstalledAppsText != null)
                InstalledAppsText.Text = currentLang == "tr" ? (Properties.Strings.InstalledApps ?? "Kurulu Uygulamalar:") : (Properties.Strings.InstalledApps ?? "Installed Applications:");

            if (InstallSelectedButton != null)
                InstallSelectedButton.Content = currentLang == "tr" ? (Properties.Strings.InstallSelected ?? "Seçilenleri Yükle") : (Properties.Strings.InstallSelected ?? "Install Selected");

            if (RefreshUninstallListButton != null)
                RefreshUninstallListButton.Content = currentLang == "tr" ? (Properties.Strings.RefreshList ?? "Listeyi Yenile") : (Properties.Strings.RefreshList ?? "Refresh List");

            if (UninstallSelectedButton != null)
                UninstallSelectedButton.Content = currentLang == "tr" ? (Properties.Strings.UninstallSelected ?? "Seçilenleri Kaldır") : (Properties.Strings.UninstallSelected ?? "Uninstall Selected");

            if (SelectOrEnterProgramText != null)
                SelectOrEnterProgramText.Text = currentLang == "tr" ? (Properties.Strings.SelectOrEnterProgram ?? "Hazır Programı Seç / Manuel Gir:") : (Properties.Strings.SelectOrEnterProgram ?? "Select Program / Enter Manually:");

            if (HuntInatciProgramButton != null)
                HuntInatciProgramButton.Content = currentLang == "tr" ? (Properties.Strings.HuntStubborn ?? "İnatçı Programı Kaldır") : (Properties.Strings.HuntStubborn ?? "Remove Stubborn");

            if (RestoreInatciProgramButton != null)
                RestoreInatciProgramButton.Content = currentLang == "tr" ? (Properties.Strings.RestoreStubborn ?? "Avlananı Geri Yükle") : (Properties.Strings.RestoreStubborn ?? "Restore Removed");

            if (ManualInatciProgramTextBox != null)
            {
                ManualInatciProgramTextBox.Tag = currentLang == "tr" ? (Properties.Strings.OrEnterProgram ?? "Veya buraya program adı girin...") : (Properties.Strings.OrEnterProgram ?? "Or enter program name...");
                if (ManualInatciProgramTextBox.Text == (currentLang == "tr" ? "Veya buraya program adı girin..." : "Or enter program name..."))
                {
                    ManualInatciProgramTextBox.Text = currentLang == "tr" ? (Properties.Strings.OrEnterProgram ?? "Veya buraya program adı girin...") : (Properties.Strings.OrEnterProgram ?? "Or enter program name...");
                }
            }
        }

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return default;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i) as DependencyObject;
                if (child is T typedChild)
                {
                    return typedChild;
                }
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return default;
        }
    }
}