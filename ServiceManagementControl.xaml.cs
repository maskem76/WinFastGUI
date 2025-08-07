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

        private List<SuggestedService> suggestedServices = new List<SuggestedService>
        {
            new SuggestedService("BthServ", WinFastGUI.Properties.Strings.Bluetooth),
            new SuggestedService("PhoneSvc", WinFastGUI.Properties.Strings.PhoneBluetooth),
            new SuggestedService("Spooler", WinFastGUI.Properties.Strings.Printer),
            new SuggestedService("WiaRpc", WinFastGUI.Properties.Strings.ScannerCamera),
            new SuggestedService("PenService", WinFastGUI.Properties.Strings.PenTouch),
            new SuggestedService("BDESVC", WinFastGUI.Properties.Strings.BitLocker),
            new SuggestedService("DusmSvc", WinFastGUI.Properties.Strings.MeteredNetworks),
            new SuggestedService("iphlpsvc", WinFastGUI.Properties.Strings.IPHelper),
            new SuggestedService("icssvc", WinFastGUI.Properties.Strings.HotspotMiracast),
            new SuggestedService("WwanSvc", WinFastGUI.Properties.Strings.RadioAirplaneMode),
            new SuggestedService("wcncsvc", WinFastGUI.Properties.Strings.WindowsConnectNow),
            new SuggestedService("WlanSvc", WinFastGUI.Properties.Strings.WifiWARP),
            new SuggestedService("WdiServiceHost", WinFastGUI.Properties.Strings.Location),
            new SuggestedService("WFDSConMgrSvc", WinFastGUI.Properties.Strings.MiracastWireless),
            new SuggestedService("FDResPub", WinFastGUI.Properties.Strings.StreamingNetwork),
            new SuggestedService("SysMain", WinFastGUI.Properties.Strings.FastStartup),
            new SuggestedService("WSearch", WinFastGUI.Properties.Strings.WindowsSearch),
            new SuggestedService("wlpasvc", WinFastGUI.Properties.Strings.FastUserSwitch),
            new SuggestedService("FontCache", WinFastGUI.Properties.Strings.FontCache),
            new SuggestedService("wisvc", WinFastGUI.Properties.Strings.WindowsInsider),
            new SuggestedService("WbioSrvc", WinFastGUI.Properties.Strings.Biometric),
            new SuggestedService("defragsvc", WinFastGUI.Properties.Strings.DiskDefragment),
            new SuggestedService("DevicesFlowUserSvc", WinFastGUI.Properties.Strings.NearbyDevices),
            new SuggestedService("SCardSvr", WinFastGUI.Properties.Strings.SmartCard),
            new SuggestedService("SessionEnv", WinFastGUI.Properties.Strings.Enterprise),
            new SuggestedService("pla", WinFastGUI.Properties.Strings.PerformanceLogs),
            new SuggestedService("BcastDVRUserService", WinFastGUI.Properties.Strings.GameDVR),
            new SuggestedService("SDRSVC", WinFastGUI.Properties.Strings.SystemRestore),
            new SuggestedService("MixedRealityOpenXRSvc", WinFastGUI.Properties.Strings.MixedReality),
            new SuggestedService("XboxGipSvc", WinFastGUI.Properties.Strings.Xbox),
            new SuggestedService("DoSvc", WinFastGUI.Properties.Strings.DeliveryOptimization),
            new SuggestedService("RemoteDesktop", WinFastGUI.Properties.Strings.RemoteDesktop),
            new SuggestedService("SnippingTool", WinFastGUI.Properties.Strings.SnippingTool),
            new SuggestedService("WpcMonSvc", WinFastGUI.Properties.Strings.ParentControl),
            new SuggestedService("NaturalLanguage", WinFastGUI.Properties.Strings.VoiceCommand),
            new SuggestedService("RetailDemo", WinFastGUI.Properties.Strings.RetailDemo),
            new SuggestedService("PimIndexMaintenanceSvc", WinFastGUI.Properties.Strings.ContactsSync),
            new SuggestedService("DiagTrack", WinFastGUI.Properties.Strings.Telemetry),
            new SuggestedService("TroubleShootingSvc", WinFastGUI.Properties.Strings.Troubleshooting),
            new SuggestedService("SensorService", WinFastGUI.Properties.Strings.Sensors),
            new SuggestedService("TimeBrokerSvc", WinFastGUI.Properties.Strings.TimeZoneUpdater),
            new SuggestedService("MapsBroker", WinFastGUI.Properties.Strings.MapsManager),
            new SuggestedService("WalletService", WinFastGUI.Properties.Strings.Wallet),
            new SuggestedService("NaturalAuthentication", WinFastGUI.Properties.Strings.AutoPlay)
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
            UpdateLanguage();
        }

        public void UpdateLanguage()
        {
            RefreshButton.Content = WinFastGUI.Properties.Strings.Refresh;
            ClearLogButton.Content = WinFastGUI.Properties.Strings.ClearLog;
            StartServiceButton.Content = WinFastGUI.Properties.Strings.Start;
            StopServiceButton.Content = WinFastGUI.Properties.Strings.Stop;
            StopSelectedSuggestedButton.Content = WinFastGUI.Properties.Strings.StopSelectedServices;
            ServiceNameText.Text = WinFastGUI.Properties.Strings.ServiceName + ": " + WinFastGUI.Properties.Strings.NotSelected;
            ServiceDescriptionText.Text = WinFastGUI.Properties.Strings.Description + ":";
            ServiceStatusText.Text = WinFastGUI.Properties.Strings.Status + ":";
            ServiceStartTypeText.Text = WinFastGUI.Properties.Strings.StartType + ":";
            ServiceDependenciesText.Text = WinFastGUI.Properties.Strings.Dependencies + ":";
            ServiceCpuUsageText.Text = WinFastGUI.Properties.Strings.CpuUsage + ":";
            
            if (SearchBox.Text == "" || SearchBox.Text == WinFastGUI.Properties.Strings.SearchServices)
                SearchBox.Text = WinFastGUI.Properties.Strings.SearchServices;

            CategoryCombo.Items.Clear();
            CategoryCombo.Items.Add(new ComboBoxItem { Content = WinFastGUI.Properties.Strings.All });
            CategoryCombo.Items.Add(new ComboBoxItem { Content = WinFastGUI.Properties.Strings.Connection });
            CategoryCombo.Items.Add(new ComboBoxItem { Content = WinFastGUI.Properties.Strings.Device });
            CategoryCombo.Items.Add(new ComboBoxItem { Content = WinFastGUI.Properties.Strings.Security });
            CategoryCombo.Items.Add(new ComboBoxItem { Content = WinFastGUI.Properties.Strings.System });
            CategoryCombo.Items.Add(new ComboBoxItem { Content = WinFastGUI.Properties.Strings.Media });
            CategoryCombo.Items.Add(new ComboBoxItem { Content = WinFastGUI.Properties.Strings.Other });
            if (CategoryCombo.Items.Count > 0) CategoryCombo.SelectedIndex = 0;

            StartTypeCombo.Items.Clear();
            StartTypeCombo.Items.Add(new ComboBoxItem { Content = WinFastGUI.Properties.Strings.Automatic });
            StartTypeCombo.Items.Add(new ComboBoxItem { Content = WinFastGUI.Properties.Strings.Manual });
            StartTypeCombo.Items.Add(new ComboBoxItem { Content = WinFastGUI.Properties.Strings.Disabled });
            if (StartTypeCombo.Items.Count > 0) StartTypeCombo.SelectedIndex = 0;

            var gridView = ServiceGridView as GridView;
            if (gridView != null && gridView.Columns.Count == 5)
            {
                gridView.Columns[0].Header = WinFastGUI.Properties.Strings.ServiceName;
                gridView.Columns[1].Header = WinFastGUI.Properties.Strings.Status;
                gridView.Columns[2].Header = WinFastGUI.Properties.Strings.StartType;
                gridView.Columns[3].Header = WinFastGUI.Properties.Strings.Category;
                gridView.Columns[4].Header = WinFastGUI.Properties.Strings.CpuUsage;
            }

            var groupBox = this.FindName("SuggestedServicesGroupBox") as GroupBox;
            if (groupBox != null) groupBox.Header = WinFastGUI.Properties.Strings.SuggestedStoppableServices;
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
                MessageBox.Show(WinFastGUI.Properties.Strings.ServicesLoadError + $": {ex.Message}");
            }
        }

        private void ApplyFilter()
        {
            IEnumerable<ServiceInfo> filtered = services;
            if (activeCategory != "Tümü")
                filtered = filtered.Where(s => s.Category == activeCategory);
            if (!string.IsNullOrWhiteSpace(activeSearch))
                filtered = filtered.Where(s => s.DisplayName.Contains(activeSearch, StringComparison.OrdinalIgnoreCase));
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
            if (SearchBox.Text == WinFastGUI.Properties.Strings.SearchServices)
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
                ServiceNameText.Text = WinFastGUI.Properties.Strings.ServiceName + $": {info.DisplayName}";
                ServiceDescriptionText.Text = WinFastGUI.Properties.Strings.Description + $": {info.Description}";
                ServiceStatusText.Text = WinFastGUI.Properties.Strings.Status + $": {info.Status}";
                ServiceStartTypeText.Text = WinFastGUI.Properties.Strings.StartType + $": {info.StartType}";
                ServiceDependenciesText.Text = WinFastGUI.Properties.Strings.Dependencies + $": {info.Dependencies}";
                ServiceCpuUsageText.Text = WinFastGUI.Properties.Strings.CpuUsage + $": {info.CpuUsage:F2}%";
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
                        LogMessageToMain?.Invoke(string.Format(WinFastGUI.Properties.Strings.ServiceStopped, s.Display));
                    }
                    else if (svc != null && svc.Status == ServiceControllerStatus.Stopped)
                    {
                        LogMessageToMain?.Invoke(string.Format(WinFastGUI.Properties.Strings.ServiceAlreadyStopped, s.Display));
                    }
                    else
                    {
                        LogMessageToMain?.Invoke(string.Format(WinFastGUI.Properties.Strings.ServiceNotFound, s.Display));
                    }
                }
                catch (Exception ex)
                {
                    LogMessageToMain?.Invoke(string.Format(WinFastGUI.Properties.Strings.ServiceStopFailed, s.Display) + ex.Message);
                }
            }
            await LoadServicesAsync();
            UpdateSuggestedServicesStatus();
        }

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