#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using WinFastGUI.Controls;
using WinFastGUI.Properties;

namespace WinFastGUI
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, UserControl> _controlCache = new();
        private string _currentControlKey = "Home";

        public MainWindow()
        {
            InitializeComponent();
            LoadSavedLanguage();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SetInitialLanguage();
            ShowControl(_currentControlKey);
        }

        private void LoadSavedLanguage()
        {
            string savedCulture = Properties.Settings.Default.Language;
            Console.WriteLine($"Settings'ten okunan dil: {savedCulture ?? "null"}");
            if (string.IsNullOrEmpty(savedCulture))
            {
                Properties.Settings.Default.Language = "tr-TR";
                Properties.Settings.Default.Save();
                Console.WriteLine("Varsayılan dil tr-TR olarak ayarlandı ve kaydedildi");
            }
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(Properties.Settings.Default.Language);
            Console.WriteLine($"Geçerli dil: {Thread.CurrentThread.CurrentUICulture.Name}");
            RefreshUI();
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded || LanguageComboBox.SelectedItem is not ComboBoxItem selectedItem || selectedItem.Tag is not string culture)
            {
                Console.WriteLine("Geçersiz seçim veya yüklenmedi");
                return;
            }

            string currentCulture = Thread.CurrentThread.CurrentUICulture.Name;
            if (currentCulture == culture)
            {
                Console.WriteLine($"Dil zaten {culture}");
                UpdateLanguageComboBox(culture);
                return;
            }

            if (MessageBox.Show("Dil değişikliği için yeniden başlatma gereklidir. Devam edilsin mi?", "Dil Değişikliği", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Console.WriteLine($"Yeni dil seçildi: {culture}");
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                Properties.Settings.Default.Language = culture;
                Properties.Settings.Default.Save();
                string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinFastGUI", "user.config");
                if (File.Exists(settingsPath))
                {
                    string configContent = File.ReadAllText(settingsPath);
                    Console.WriteLine($"Ayar dosyası içeriği (ilk 100 karakter): {configContent.Substring(0, Math.Min(100, configContent.Length))}");
                }
                else
                {
                    Console.WriteLine($"Ayar dosyası bulunamadı: {settingsPath}");
                }
                Console.WriteLine($"Dil kaydedildi: {Properties.Settings.Default.Language}");
                UpdateLanguageComboBox(culture);
                RefreshUI();
                ForceRestart();
            }
            else
            {
                Console.WriteLine("Dil değişikliği iptal edildi");
                UpdateLanguageComboBox(currentCulture);
            }
        }

        private void UpdateLanguageComboBox(string currentCulture)
        {
            Console.WriteLine($"Menü güncelleniyor, yeni dil: {currentCulture}");
            Dispatcher.Invoke(() =>
            {
                int targetIndex = currentCulture.StartsWith("en") ? 1 : 0;
                Console.WriteLine($"Hedef indeks: {targetIndex}, Mevcut indeks: {LanguageComboBox.SelectedIndex}, Öğe sayısı: {LanguageComboBox.Items.Count}");
                if (LanguageComboBox.Items.Count > targetIndex && LanguageComboBox.SelectedIndex != targetIndex)
                {
                    LanguageComboBox.SelectedIndex = targetIndex;
                    Console.WriteLine($"Menü güncellendi, yeni seçili indeks: {LanguageComboBox.SelectedIndex}");
                }
                else
                {
                    Console.WriteLine("Menü güncellenemedi: İndeks zaten doğru veya öğe sayısı yetersiz");
                }
            });
        }

        private void ForceRestart()
        {
            string? appPath = Process.GetCurrentProcess().MainModule?.FileName;
            if (appPath != null)
            {
                Console.WriteLine($"Yeniden başlatılıyor: {appPath} ile dil {Properties.Settings.Default.Language}");
                Process.Start(new ProcessStartInfo { FileName = appPath, UseShellExecute = true });
                Application.Current.Shutdown();
            }
            else
            {
                Console.WriteLine("Uygulama yolu null");
            }
        }

        private void SetInitialLanguage()
        {
            string currentCulture = Thread.CurrentThread.CurrentUICulture.Name;
            Console.WriteLine($"Başlangıç dili: {currentCulture}");
            UpdateLanguageComboBox(currentCulture);
            RefreshUI();
        }

private void RefreshUI()
{
    Console.WriteLine($"UI yenileniyor, dil: {Thread.CurrentThread.CurrentUICulture.Name}");
    this.Title = Properties.Strings.WinfastOptimizasyonAraciV131;
    HomeButton.Content = Properties.Strings.Home;
    SystemCleanButton.Content = Properties.Strings.SystemCleanup;
    AppManagerButton.Content = Properties.Strings.AppManagement;
    BackupButton.Content = Properties.Strings.Backup;
    ImageBackupButton.Content = Properties.Strings.ImageCreator;
    OptimizationManagerButton.Content = Properties.Strings.OptimizationManager;
    AffinityManagerButton.Content = Properties.Strings.AffinityManager;
    UpdateManagerButton.Content = Properties.Strings.UpdateManager;
    BloatwareButton.Content = Properties.Strings.Bloatware;
    ServiceManagementButton.Content = Properties.Strings.ServiceManagement;
    IrqToolButton.Content = Properties.Strings.IrqTool;

    // AppManagerUserControl için dil güncellemesi
    if (MainContentArea.Content is AppManagerUserControl appManager)
    {
        appManager.UpdateLanguage();
    }
}

private void ShowControl(string controlKey)
{
    _currentControlKey = controlKey;
    if (!_controlCache.ContainsKey(controlKey))
    {
        UserControl? newControl = controlKey switch
        {
            "Home" => new HomeControl(),
            "SystemClean" => new SystemCleanUserControl(),
            "AppManager" => new AppManagerUserControl(),
            "Backup" => new BackupControl(),
            "ImageBackup" => new DismPlusPlusControl(),
            "OptimizationManager" => new OptimizationManagerControl(),
            "AffinityManager" => new AffinityManagerControl(),
            "UpdateManager" => new UpdateManagerControl(),
            "Bloatware" => new BloatwareCleanerUserControl(),
            "ServiceManagement" => new ServiceManagementControl(),
            "IrqTool" => new InterruptAffinityToolControl(),
            _ => new HomeControl()
        };
        if (newControl != null) _controlCache[controlKey] = newControl;
    }
    MainContentArea.Content = _controlCache.GetValueOrDefault(controlKey);
    HeaderTextBlock.Text = controlKey switch
    {
        "Home" => Properties.Strings.Home,
        "SystemClean" => Properties.Strings.SystemCleanup,
        "AppManager" => Properties.Strings.AppManagement,
        "Backup" => Properties.Strings.Backup,
        "ImageBackup" => Properties.Strings.ImageCreator,
        "OptimizationManager" => Properties.Strings.OptimizationManager,
        "AffinityManager" => Properties.Strings.AffinityManager,
        "UpdateManager" => Properties.Strings.UpdateManager,
        "Bloatware" => Properties.Strings.Bloatware,
        "ServiceManagement" => Properties.Strings.ServiceManagement,
        "IrqTool" => Properties.Strings.IrqTool,
        _ => Properties.Strings.Home
    };
    Console.WriteLine($"Kontrol: {controlKey}, Başlık: {HeaderTextBlock.Text}");

    // Dil güncellemesi
    if (MainContentArea.Content is AppManagerUserControl appManager)
    {
        appManager.UpdateLanguage();
    }
}

        // --- BUTON CLICK OLAYLARI ---
        private void HomeButton_Click(object sender, RoutedEventArgs e) => ShowControl("Home");
        private void SystemCleanButton_Click(object sender, RoutedEventArgs e) => ShowControl("SystemClean");
        private void AppManagerButton_Click(object sender, RoutedEventArgs e) => ShowControl("AppManager");
        private void BackupButton_Click(object sender, RoutedEventArgs e) => ShowControl("Backup");
        private void ImageBackupButton_Click(object sender, RoutedEventArgs e) => ShowControl("ImageBackup");
        private void OptimizationManagerButton_Click(object sender, RoutedEventArgs e) => ShowControl("OptimizationManager");
        private void AffinityManagerButton_Click(object sender, RoutedEventArgs e) => ShowControl("AffinityManager");
        private void UpdateManagerButton_Click(object sender, RoutedEventArgs e) => ShowControl("UpdateManager");
        private void BloatwareButton_Click(object sender, RoutedEventArgs e) => ShowControl("Bloatware");
        private void ServiceManagementButton_Click(object sender, RoutedEventArgs e) => ShowControl("ServiceManagement");
        private void IrqToolButton_Click(object sender, RoutedEventArgs e) => ShowControl("IrqTool");

        public void LogMessageToMainWindow(string message)
        {
            if (LogTextBox != null)
            {
                Dispatcher.Invoke(() =>
                {
                    LogTextBox.Text = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}{LogTextBox.Text}";
                });
            }
        }
    }
}