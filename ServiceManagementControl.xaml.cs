using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using System.Management;

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "Bu uygulama sadece Windows üzerinde çalışacak.")]

namespace WinFastGUI.Controls
{
    public partial class ServiceManagementControl : UserControl
    {
        public Action<string>? LogMessageToMain { get; set; }

        private List<ServiceInfo> services = new();
        private List<string> logMessages = new();
        private string activeCategory = "Tümü";
        private string activeSearch = "";

        // Kaldırılabilir (önerilen) servis listesi
        private List<SuggestedService> suggestedServices = new List<SuggestedService>
        {
            new SuggestedService("BthServ", "Bluetooth"),
            new SuggestedService("PhoneSvc", "Telefon [Bluetooth]"),
            new SuggestedService("Spooler", "Yazıcı"),
            new SuggestedService("WiaRpc", "Tarayıcı ve Kamera [OBS]"),
            new SuggestedService("PenService", "Kalem ve Dokunmatik"),
            new SuggestedService("BDESVC", "Bitlocker Sürücü Şifreleme"),
            new SuggestedService("DusmSvc", "Tarifeli Ağlar Kota Yöneticisi"),
            new SuggestedService("iphlpsvc", "IP Yardımcısı [IPV6]"),
            new SuggestedService("icssvc", "Mobil Etkin Nokta [Miracast]"),
            new SuggestedService("WwanSvc", "Radyo ve Uçak Modu"),
            new SuggestedService("wcncsvc", "Windows Şimdi Bağlan [WPS]"),
            new SuggestedService("WlanSvc", "Wifi [Cloudflare WARP]"),
            new SuggestedService("WdiServiceHost", "Konum"),
            new SuggestedService("WFDSConMgrSvc", "Miracast [Kablosuz Ekran]"),
            new SuggestedService("FDResPub", "Akış Bağlı: Ağ üzeri veri, yazıcı paylaşımı"),
            new SuggestedService("SysMain", "Hızlı Getir-Başlat [HDD]"),
            new SuggestedService("WSearch", "Windows Search İndeksleme"),
            new SuggestedService("wlpasvc", "Hızlı Kullanıcı Değiştir [Blizzard]"),
            new SuggestedService("FontCache", "Yazı Tipi Önbelliği [HDD]"),
            new SuggestedService("wisvc", "Windows Insider"),
            new SuggestedService("WbioSrvc", "Biyometrik [Parmak izi, HelloFace]"),
            new SuggestedService("defragsvc", "Disk Birleştirme [SSD/HDD, Trim]"),
            new SuggestedService("DevicesFlowUserSvc", "Yönlendirici Yakındaki cihazlar"),
            new SuggestedService("SCardSvr", "Akıllı Kart [Çipli kart okuyucu]"),
            new SuggestedService("SessionEnv", "Kurumsal [Kiosk mod, Intune, AppLocker]"),
            new SuggestedService("pla", "Performans günlükleri [Kurumsal]"),
            new SuggestedService("BcastDVRUserService", "Oyun DVR ve Yayın [Xbox ekran kayıt]"),
            new SuggestedService("SDRSVC", "Sistem Geri Yükleme [Dosya geçmişi]"),
            new SuggestedService("MixedRealityOpenXRSvc", "Karma gerçeklik [VR]"),
            new SuggestedService("XboxGipSvc", "Xbox"),
            new SuggestedService("DoSvc", "Teslim en iyileştirme [Market, Xbox, Güncellemeler]"),
            new SuggestedService("RemoteDesktop", "Uzak masaüstü"),
            new SuggestedService("SnippingTool", "Ekran yakalama"),
            new SuggestedService("WpcMonSvc", "Ebeveyn denetimleri"),
            new SuggestedService("NaturalLanguage", "Sesli Komut [Cortana]"),
            new SuggestedService("RetailDemo", "Perakende gösteri"),
            new SuggestedService("PimIndexMaintenanceSvc", "Kişiler [Ana bilgisayarı eşitle]"),
            new SuggestedService("DiagTrack", "Telemetri"),
            new SuggestedService("TroubleShootingSvc", "Sorun giderme"),
            new SuggestedService("SensorService", "Sensörler [Laptop]"),
            new SuggestedService("TimeBrokerSvc", "Otomatik Saat Dilimi Güncelleştirici"),
            new SuggestedService("MapsBroker", "İndirilen haritalar yöneticisi"),
            new SuggestedService("WalletService", "Cüzdan"),
            new SuggestedService("NaturalAuthentication", "Otomatik oynat [Otomatik sürücü yükleme]")
        };

public ServiceManagementControl()
{
    InitializeComponent();
    Loaded += ServiceManagementControl_Loaded;
}

private async void ServiceManagementControl_Loaded(object sender, RoutedEventArgs e)
{
    await LoadServicesAsync();
    UpdateSuggestedServicesStatus();
    if (SuggestedServicesListView != null)
        SuggestedServicesListView.ItemsSource = suggestedServices;
}
        private string GetServiceDescription(string serviceName)
        {
            try
            {
                using (var mgmt = new ManagementObject($"Win32_Service.Name='{serviceName}'"))
                {
                    mgmt.Get();
                    return mgmt["Description"]?.ToString() ?? "";
                }
            }
            catch
            {
                return "";
            }
        }

        private async Task LoadServicesAsync()
        {
            services.Clear();
            try
            {
                var controllers = await Task.Run(() => ServiceController.GetServices());
                foreach (var sc in controllers)
                {
                    services.Add(new ServiceInfo
                    {
                        ServiceName = sc.ServiceName,
                        DisplayName = sc.DisplayName,
                        Status = sc.Status.ToString(),
                        StartType = "Bilinmiyor",
                        Category = "Diğer",
                        Description = GetServiceDescription(sc.ServiceName),
                        Dependencies = "",
                        Note = "",
                        CpuUsage = 0
                    });
                }
                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hizmetler yüklenemedi: {ex.Message}");
            }
        }

private void ApplyFilter()
{
    IEnumerable<ServiceInfo> filtered = services;
    if (activeCategory != "Tümü")
        filtered = filtered.Where(s => s.Category == activeCategory);
    if (!string.IsNullOrWhiteSpace(activeSearch))
        filtered = filtered.Where(s => s.DisplayName.Contains(activeSearch, StringComparison.OrdinalIgnoreCase));
    
    // NULL KONTROLÜ!
    if (ServiceListView != null)
        Dispatcher.Invoke(() => ServiceListView.ItemsSource = filtered.ToList());
}

        private void UpdateSuggestedServicesStatus()
        {
            var allServices = ServiceController.GetServices();
            foreach (var s in suggestedServices)
            {
                var svc = allServices.FirstOrDefault(x => x.ServiceName.Equals(s.ServiceName, StringComparison.OrdinalIgnoreCase));
                s.IsRunning = svc != null && svc.Status == ServiceControllerStatus.Running;
            }
            SuggestedServicesListView.ItemsSource = null;
            SuggestedServicesListView.ItemsSource = suggestedServices;
        }

        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryCombo.SelectedItem is ComboBoxItem ci)
                activeCategory = ci.Content.ToString() ?? "Tümü";
            ApplyFilter();
        }
        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Hizmet ara...")
                SearchBox.Text = "";
        }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            activeSearch = SearchBox.Text?.Trim() ?? "";
            ApplyFilter();
        }
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadServicesAsync();
            UpdateSuggestedServicesStatus();
        }
        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            logMessages.Clear();
        }
        private void ServiceListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServiceListView.SelectedItem is ServiceInfo info)
            {
                ServiceNameText.Text = $"Ad: {info.DisplayName}";
                ServiceDescriptionText.Text = $"Açıklama: {info.Description}";
                ServiceStatusText.Text = $"Durum: {info.Status}";
                ServiceStartTypeText.Text = $"Başlatma Türü: {info.StartType}";
                ServiceDependenciesText.Text = $"Bağımlılıklar: {info.Dependencies}";
                ServiceCpuUsageText.Text = $"CPU Kullanımı: {info.CpuUsage:F2}%";
                foreach (ComboBoxItem item in StartTypeCombo.Items)
                {
                    if (item.Content.ToString() == info.StartType)
                        StartTypeCombo.SelectedItem = item;
                }
            }
        }
        private void StartTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void StartServiceButton_Click(object sender, RoutedEventArgs e) { }
        private void StopServiceButton_Click(object sender, RoutedEventArgs e) { }

        private async void StopSelectedSuggestedButton_Click(object sender, RoutedEventArgs e)
        {
            var toStop = suggestedServices.Where(s => s.IsSelected).ToList();
            foreach (var s in toStop)
            {
                try
                {
                    var svc = ServiceController.GetServices().FirstOrDefault(x => x.ServiceName.Equals(s.ServiceName, StringComparison.OrdinalIgnoreCase));
                    if (svc != null && svc.Status != ServiceControllerStatus.Stopped)
                    {
                        svc.Stop();
                        svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                        LogMessageToMain?.Invoke($"{s.Display} servisi durduruldu.");
                    }
                    else if (svc != null && svc.Status == ServiceControllerStatus.Stopped)
                    {
                        LogMessageToMain?.Invoke($"{s.Display} servisi zaten kapalı.");
                    }
                    else
                    {
                        LogMessageToMain?.Invoke($"{s.Display} servisi bulunamadı.");
                    }
                }
                catch (Exception ex)
                {
                    LogMessageToMain?.Invoke($"{s.Display} servisi durdurulamadı: {ex.Message}");
                }
            }
            await LoadServicesAsync();
            UpdateSuggestedServicesStatus();
        }

        // MODELLER:
        public class ServiceInfo
        {
            public string ServiceName { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string Status { get; set; } = "";
            public string StartType { get; set; } = "";
            public string Description { get; set; } = "";
            public string Category { get; set; } = "";
            public string Dependencies { get; set; } = "";
            public string Note { get; set; } = "";
            public double CpuUsage { get; set; } = 0.0;
        }

        public class SuggestedService
        {
            public string ServiceName { get; set; }
            public string Display { get; set; }
            public bool IsSelected { get; set; }
            public bool IsRunning { get; set; }
            public SuggestedService(string name, string display) { ServiceName = name; Display = display; }
        }
    }
}