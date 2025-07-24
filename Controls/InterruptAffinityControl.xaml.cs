using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.ComponentModel;

namespace WinFastGUI.Controls
{
    public partial class InterruptAffinityToolControl : UserControl
    {
        public class IrqInfo : INotifyPropertyChanged
        {
            public string? DeviceName { get; set; } = ""; // Nullable yapıldı
            public string? PnpDeviceId { get; set; } = ""; // Nullable yapıldı
            private string? _currentAffinity = "Bilinmiyor"; // Nullable yapıldı
            public string? CurrentAffinity
            {
                get => _currentAffinity;
                set { _currentAffinity = value; OnPropertyChanged(nameof(CurrentAffinity)); }
            }
            private string? _newAffinity = "-"; // Nullable yapıldı
            public string? NewAffinity
            {
                get => _newAffinity;
                set { _newAffinity = value; OnPropertyChanged(nameof(NewAffinity)); }
            }
            private string? _originalAffinity = ""; // Nullable yapıldı
            public string? OriginalAffinity
            {
                get => _originalAffinity;
                set { _originalAffinity = value; OnPropertyChanged(nameof(OriginalAffinity)); }
            }
            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string name) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private class HeavyDeviceRule
        {
            public string? Keyword { get; set; } = ""; // Nullable yapıldı
            public int[] TargetCpus { get; set; } = Array.Empty<int>();
            public string? DisplayName { get; set; } = ""; // Nullable yapıldı
        }

        private readonly List<HeavyDeviceRule> heavyDeviceRules = new List<HeavyDeviceRule>
        {
            new HeavyDeviceRule { Keyword = "nvidia", TargetCpus = new[] { 4, 5 }, DisplayName = "Ekran Kartı (NVIDIA)" },
            new HeavyDeviceRule { Keyword = "geforce", TargetCpus = new[] { 4, 5 }, DisplayName = "Ekran Kartı (NVIDIA)" },
            new HeavyDeviceRule { Keyword = "radeon", TargetCpus = new[] { 2, 3 }, DisplayName = "Ekran Kartı (AMD)" },
            new HeavyDeviceRule { Keyword = "ethernet", TargetCpus = new[] { 6, 7 }, DisplayName = "Ethernet" },
            new HeavyDeviceRule { Keyword = "realtek", TargetCpus = new[] { 6, 7 }, DisplayName = "Ağ Kartı" },
            new HeavyDeviceRule { Keyword = "intel", TargetCpus = new[] { 8, 9 }, DisplayName = "Ağ Kartı" },
            new HeavyDeviceRule { Keyword = "nvme", TargetCpus = new[] { 10, 11 }, DisplayName = "NVMe SSD" },
        };

private readonly string backupPath = Path.Combine(AppContext.BaseDirectory, "backups");
private List<IrqInfo> originalDeviceList = new List<IrqInfo>();
private List<IrqInfo>? cachedDeviceList = null; // Nullable yapıldı
private DateTime lastCacheUpdate = DateTime.MinValue;

        public InterruptAffinityToolControl()
        {
            InitializeComponent();
            this.Loaded += OnControlLoaded;
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= OnControlLoaded;
            HandleAutomaticElevation();
        }

        private void HandleAutomaticElevation()
{
    WindowsIdentity identity = WindowsIdentity.GetCurrent();
    string? userName = identity.Name; // Nullable yapıldı

    bool isElevated = !string.IsNullOrEmpty(userName) && (userName.Equals("NT AUTHORITY\\SYSTEM", StringComparison.OrdinalIgnoreCase) ||
                      userName.Equals("NT SERVICE\\TrustedInstaller", StringComparison.OrdinalIgnoreCase));

    if (isElevated)
    {
        MainContentGrid.Visibility = Visibility.Visible;
        LoadDeviceList();
    }
    else
    {
        try
        {
            string? nsudoPath = Path.Combine(AppContext.BaseDirectory, "Modules", "nsudo", "NSudo.exe"); // Nullable yapıldı
            if (string.IsNullOrEmpty(nsudoPath) || !File.Exists(nsudoPath))
            {
                MessageBox.Show($"NSudo.exe belirtilen yolda bulunamadı!\n{nsudoPath ?? "Bilinmeyen yol"}", "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            string? appPath = Process.GetCurrentProcess().MainModule?.FileName; // Nullable yapıldı
            if (string.IsNullOrEmpty(appPath))
            {
                MessageBox.Show("Uygulama yolu alınamadı. Program kapanıyor.", "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            // Null-safe atama
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = nsudoPath ?? throw new InvalidOperationException("NSudo yolu null"),
                Arguments = $"-U:T -P:E -M:S \"{appPath ?? throw new InvalidOperationException("Uygulama yolu null")}\"",
                UseShellExecute = true
            };

            Process.Start(startInfo);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"NSudo ile otomatik yetki yükseltme sırasında hata oluştu:\n{ex.Message}", "Yükseltme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }
}

        private async void LoadDeviceList(bool forceRefresh = false)
        {
            if (!forceRefresh && cachedDeviceList != null && (DateTime.Now - lastCacheUpdate).TotalMinutes < 5)
            {
                IrqDataGrid.ItemsSource = cachedDeviceList;
                IrqLogText.Text = "Önbellekten donanım listesi yüklendi.";
                return;
            }

            IrqLogText.Text = "Donanımlar yükleniyor...";
            IrqDataGrid.ItemsSource = null;

            try
            {
                var deviceList = await Task.Run(() =>
                {
                    var devices = new List<IrqInfo>();
                    var pnpSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity");

                    foreach (ManagementObject dev in pnpSearcher.Get())
                    {
                        string? deviceName = dev["Caption"] as string; // Nullable yapıldı
                        string? pnpDeviceId = dev["DeviceID"] as string; // Nullable yapıldı

                        if (string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(pnpDeviceId))
                            continue;

                        bool shouldSkip = pnpDeviceId.StartsWith("ACPI\\PNP0") || pnpDeviceId.StartsWith("ROOT\\") ||
                                         deviceName.ToLower().Contains("microsoft") || deviceName.ToLower().Contains("sistem") ||
                                         deviceName.ToLower().Contains("birim") || deviceName.ToLower().Contains("kaynakları");

                        if (shouldSkip) continue;

                        var irqInfo = new IrqInfo
                        {
                            DeviceName = deviceName,
                            PnpDeviceId = pnpDeviceId,
                            CurrentAffinity = ReadCurrentAffinity(pnpDeviceId),
                            OriginalAffinity = ReadCurrentAffinity(pnpDeviceId)
                        };
                        devices.Add(irqInfo);
                    }
                    return devices;
                });

                cachedDeviceList = deviceList.ToList();
                lastCacheUpdate = DateTime.Now;
                originalDeviceList = cachedDeviceList.Select(d => new IrqInfo
                {
                    DeviceName = d.DeviceName,
                    PnpDeviceId = d.PnpDeviceId,
                    CurrentAffinity = d.CurrentAffinity,
                    OriginalAffinity = d.OriginalAffinity
                }).ToList();

                if (cachedDeviceList.Any())
                {
                    IrqDataGrid.ItemsSource = cachedDeviceList;
                    IrqLogText.Text = $"Toplam {cachedDeviceList.Count} donanım listelendi. Atama için butonları kullanın.";
                }
                else
                {
                    IrqLogText.Text = "Listelenecek donanım bulunamadı.";
                }
            }
            catch (Exception ex)
            {
                IrqLogText.Text = $"Donanımlar listelenirken bir hata oluştu: {ex.Message}";
                LogError("LoadDeviceList hatası", ex);
                Debug.WriteLine($"LoadDeviceList Hata: {ex}");
            }
        }

        private void AutoAssignIrqButton_Click(object sender, RoutedEventArgs e)
        {
            if (IrqDataGrid.ItemsSource == null)
            {
                IrqLogText.Text = "Hata: Cihaz listesi yüklenemedi.";
                return;
            }
            var irqList = IrqDataGrid.ItemsSource as List<IrqInfo>;
            if (irqList == null)
            {
                IrqLogText.Text = "Hata: Cihaz listesi geçersiz.";
                return;
            }

            int cpuCount = Environment.ProcessorCount;
            if (cpuCount == 0)
            {
                IrqLogText.Text = "Hata: Sistemde işlemci çekirdeği bulunamadı.";
                return;
            }

            var usedCpus = new HashSet<int>();
            var heavyDeviceIndexes = new HashSet<int>();

            for (int i = 0; i < irqList.Count; i++)
            {
                var irq = irqList[i];
                string? nameLower = irq.DeviceName?.ToLower(); // Nullable kontrolü

                foreach (var rule in heavyDeviceRules)
                {
                    if (!string.IsNullOrEmpty(nameLower) && !string.IsNullOrEmpty(rule.Keyword) && nameLower.Contains(rule.Keyword))
                    {
                        long mask = 0;
                        var assignedCpus = new List<string>();
                        foreach (var cpu in rule.TargetCpus)
                        {
                            if (cpu < cpuCount)
                            {
                                mask |= (1L << cpu);
                                assignedCpus.Add($"CPU{cpu + 1}");
                            }
                        }
                        irq.NewAffinity = string.Join(", ", assignedCpus);
                        heavyDeviceIndexes.Add(i);
                        break;
                    }
                }
            }

            var availableSystemCores = Enumerable.Range(0, cpuCount).Where(c => !usedCpus.Contains(c)).ToList();
            if (!availableSystemCores.Any())
            {
                availableSystemCores = Enumerable.Range(0, cpuCount).ToList();
            }

            int systemCpuIndex = 0;
            for (int i = 0; i < irqList.Count; i++)
            {
                if (heavyDeviceIndexes.Contains(i)) continue;

                int coreToAssign = availableSystemCores[systemCpuIndex % availableSystemCores.Count];
                irqList[i].NewAffinity = $"CPU{coreToAssign + 1}";
                systemCpuIndex++;
            }

            IrqDataGrid.Items.Refresh();
            IrqLogText.Text = "Ağır cihazlara çoklu çekirdek, diğerlerine kalan çekirdekler atandı. Önerilen yük: CPU 4-5 (%70), CPU 6-7 (%30). Uygulamak için 'Değişiklikleri Uygula'ya basın.";
        }

        private void ResetPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (IrqDataGrid.ItemsSource == null)
            {
                IrqLogText.Text = "Hata: Cihaz listesi yüklenemedi.";
                return;
            }
            var irqList = IrqDataGrid.ItemsSource as List<IrqInfo>;
            if (irqList == null)
            {
                IrqLogText.Text = "Hata: Cihaz listesi geçersiz.";
                return;
            }
            foreach (var device in irqList)
            {
                device.NewAffinity = "-";
            }
            IrqDataGrid.Items.Refresh();
            IrqLogText.Text = "Önizleme temizlendi. Cihazlar başlangıç durumuna döndürüldü.";
        }

        private void BackupSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (IrqDataGrid.ItemsSource == null)
            {
                IrqLogText.Text = "Hata: Cihaz listesi yüklenemedi.";
                return;
            }
            var irqList = IrqDataGrid.ItemsSource as List<IrqInfo>;
            if (irqList == null)
            {
                IrqLogText.Text = "Hata: Cihaz listesi geçersiz.";
                return;
            }

            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFile = Path.Combine(backupPath, $"affinity_backup_{timestamp}.json");

                var backupData = new
                {
                    Timestamp = timestamp,
                    Devices = irqList.Select(d => new
                    {
                        PnpDeviceId = d.PnpDeviceId,
                        OriginalAffinity = d.OriginalAffinity
                    }).ToList()
                };

                string directory = Path.GetDirectoryName(backupFile) ?? "";
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = System.Text.Json.JsonSerializer.Serialize(backupData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(backupFile, json);

                // Doğrulama
                var readBackData = System.Text.Json.JsonSerializer.Deserialize<BackupData>(json);
                if (readBackData == null || readBackData.Devices == null || readBackData.Devices.Count != irqList.Count)
                {
                    IrqLogText.Text = "Yedekleme dosyası doğrulanamadı, lütfen tekrar deneyin.";
                    LogError("Yedekleme doğrulama hatası");
                    return;
                }

                IrqLogText.Text = $"Ayarlar {backupFile} yoluna yedeklendi.";
            }
            catch (Exception ex)
            {
                IrqLogText.Text = $"Yedekleme hatası: {ex.Message}";
                LogError("Backup hatası", ex);
            }
        }

        private void RestoreSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(backupPath))
            {
                IrqLogText.Text = "Yedekleme dizini bulunamadı.";
                return;
            }
            string[] backupFiles = Directory.GetFiles(backupPath, "affinity_backup_*.json");
            if (backupFiles.Length == 0)
            {
                IrqLogText.Text = "Yedekleme dosyası bulunamadı.";
                return;
            }

            if (IrqDataGrid.ItemsSource == null)
            {
                IrqLogText.Text = "Hata: Cihaz listesi yüklenemedi.";
                return;
            }
            var irqList = IrqDataGrid.ItemsSource as List<IrqInfo>;
            if (irqList == null)
            {
                IrqLogText.Text = "Hata: Cihaz listesi geçersiz.";
                return;
            }

            string latestBackup = backupFiles.OrderByDescending(f => File.GetLastWriteTime(f)).First();
            try
            {
                string json = File.ReadAllText(latestBackup);
                var backupData = System.Text.Json.JsonSerializer.Deserialize<BackupData>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (backupData == null || backupData.Devices == null)
                {
                    IrqLogText.Text = "Yedekleme verisi geçersiz veya eksik.";
                    return;
                }

                foreach (var device in irqList)
                {
                    var backupDevice = backupData.Devices.FirstOrDefault(d => d.PnpDeviceId == device.PnpDeviceId);
                    if (backupDevice != null)
                    {
                        device.NewAffinity = backupDevice.OriginalAffinity != "Varsayılan" ? backupDevice.OriginalAffinity : "-";
                    }
                }
                IrqDataGrid.Items.Refresh();
                IrqLogText.Text = $"Ayarlar {latestBackup} yolundan geri yüklendi. Uygulamak için 'Değişiklikleri Uygula'ya basın.";
            }
            catch (Exception ex)
            {
                IrqLogText.Text = $"Geri yükleme hatası: {ex.Message}";
                LogError("Restore hatası", ex);
            }
        }

        private void UndoChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (IrqDataGrid.ItemsSource == null)
            {
                IrqLogText.Text = "Hata: Cihaz listesi yüklenemedi.";
                return;
            }
            var irqList = IrqDataGrid.ItemsSource as List<IrqInfo>;
            if (irqList == null)
            {
                IrqLogText.Text = "Hata: Cihaz listesi geçersiz.";
                return;
            }
            foreach (var device in irqList)
            {
                var original = originalDeviceList.FirstOrDefault(d => d.PnpDeviceId == device.PnpDeviceId);
                if (original != null)
                {
                    device.NewAffinity = original.OriginalAffinity != "Varsayılan" ? original.OriginalAffinity : "-";
                }
            }
            IrqDataGrid.Items.Refresh();
            IrqLogText.Text = "Değişiklikler geri alındı. Özgün afinite durumuna dönüldü.";
        }

       private async void ApplyIrqAffinityButton_Click(object sender, RoutedEventArgs e)
{
    if (IrqDataGrid.ItemsSource == null)
    {
        IrqLogText.Text = "Hata: Cihaz listesi yüklenemedi.";
        return;
    }
    var irqList = IrqDataGrid.ItemsSource as List<IrqInfo>;
    if (irqList == null)
    {
        IrqLogText.Text = "Hata: Cihaz listesi geçersiz.";
        return;
    }

    var itemsToApply = irqList.Where(irq => irq.NewAffinity != "-" && irq.NewAffinity?.StartsWith("CPU") == true).ToList();
    if (!itemsToApply.Any())
    {
        IrqLogText.Text = "Uygulanacak bir değişiklik bulunamadı.";
        return;
    }

    if (MessageBox.Show("Değişiklikleri uygulamak istediğinizden emin misiniz? Bu işlem sisteminizi etkileyebilir.", "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
    {
        IrqLogText.Text = "Değişiklikler iptal edildi.";
        return;
    }

    IrqLogText.Text = "Değişiklikler uygulanıyor, lütfen bekleyin...";
    ProgressBar.Visibility = Visibility.Visible;
    ProgressBar.Maximum = itemsToApply.Count;
    ProgressBar.Value = 0;

    int appliedCount = 0;
    await Task.Run(() =>
    {
        foreach (var irq in itemsToApply)
        {
            try
            {
                if (irq.PnpDeviceId == null)
                {
                    continue; // PnpDeviceId null ise atla
                }

                // CPU numarasını al ve maske oluştur
                var cpuNumbers = irq.NewAffinity?.Split(',').Select(c => int.Parse(c.Replace("CPU", "")) - 1).ToArray() ?? new int[0];
                long affinityMask = 0;
                foreach (var cpu in cpuNumbers)
                {
                    if (cpu >= 0 && cpu < Environment.ProcessorCount)
                    {
                        affinityMask |= (1L << cpu);
                    }
                }

                string registryPolicyPath = $"SYSTEM\\CurrentControlSet\\Enum\\{irq.PnpDeviceId}\\Device Parameters\\Interrupt Management\\Affinity Policy";
                using (RegistryKey? affinityKey = Registry.LocalMachine.CreateSubKey(registryPolicyPath))
                {
                    if (affinityKey != null)
                    {
                        affinityKey.SetValue("DevicePolicy", 0x00000001, RegistryValueKind.DWord);
                        affinityKey.SetValue("AssignmentSetOverride", affinityMask, RegistryValueKind.QWord);
                        appliedCount++;
                        Dispatcher.Invoke(() => ProgressBar.Value++);
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    string? fullErrorDetails = $"DETAYLI HATA ({irq.DeviceName}):\n{ex}";
                    IrqLogText.Text = $"Hata: {irq.DeviceName} için ayar yapılamadı.";
                    MessageBox.Show(fullErrorDetails ?? "Hata detayları alınamadı", "Kayıt Defteri Yazma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                    LogError("ApplyAffinity hatası", ex);
                });
                break;
            }
        }
    });

    ProgressBar.Visibility = Visibility.Collapsed;
    IrqLogText.Text = $"İşlem tamamlandı. {appliedCount} cihaz için afinite ayarı yapıldı. Değişikliklerin etkili olması için sistemi YENİDEN BAŞLATMANIZ GEREKEBİLİR.";
    if (appliedCount > 0)
    {
        MessageBox.Show("Ayarlar uygulandı.\nDeğişikliklerin tam olarak etkili olması için bilgisayarınızı yeniden başlatmanız önerilir.", "Uygulandı", MessageBoxButton.OK, MessageBoxImage.Information);
        LoadDeviceList(true); // Önbelleği yenile
    }
}

        private string MaskToCoreString(long mask)
        {
            var cores = new List<string>();
            int processorCount = Environment.ProcessorCount;
            if (processorCount == 0) return $"0x{mask:X}";

            for (int i = 0; i < processorCount; i++)
                if (((mask >> i) & 1) == 1)
                    cores.Add($"CPU{i + 1}");
            return cores.Any() ? string.Join(", ", cores) : "Yok";
        }

        private string ReadCurrentAffinity(string? pnpDeviceId) // Nullable yapıldı
        {
            try
            {
                if (string.IsNullOrEmpty(pnpDeviceId)) return "Varsayılan";
                string registryPolicyPath = $"SYSTEM\\CurrentControlSet\\Enum\\{pnpDeviceId}\\Device Parameters\\Interrupt Management\\Affinity Policy";
                using (RegistryKey? affinityKey = Registry.LocalMachine.OpenSubKey(registryPolicyPath)) // Nullable yapıldı
                {
                    if (affinityKey == null) return "Varsayılan";
                    object? policyValue = affinityKey.GetValue("DevicePolicy"); // Nullable yapıldı
                    if (policyValue is int policy && policy == 0x00000001)
                    {
                        object? overrideValue = affinityKey.GetValue("AssignmentSetOverride"); // Nullable yapıldı
                        if (overrideValue is long mask)
                        {
                            return MaskToCoreString(mask);
                        }
                        else if (overrideValue is byte[] byteMask && byteMask.Length == 8)
                        {
                            return MaskToCoreString(BitConverter.ToInt64(byteMask, 0));
                        }
                    }
                    return "Varsayılan";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Affinite okuma hatası ({pnpDeviceId}): {ex.Message}");
                LogError($"Affinite okuma hatası ({pnpDeviceId})", ex);
                return "Hata";
            }
        }

private void LogError(string message, Exception? ex = null)
{
    string logPath = Path.Combine(AppContext.BaseDirectory, "logs", "error.log");
    string? directory = Path.GetDirectoryName(logPath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
        Directory.CreateDirectory(directory);
    }
    string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}" + (ex != null ? $"\n{ex}" : "") + "\n";
    File.AppendAllText(logPath, logEntry);
}

        // BackupData sınıfı tanımlaması
        private class BackupData
        {
            public string? Timestamp { get; set; } // Nullable yapıldı
            public List<DeviceBackup>? Devices { get; set; } // Nullable yapıldı
        }

        private class DeviceBackup
        {
            public string? PnpDeviceId { get; set; } // Nullable yapıldı
            public string? OriginalAffinity { get; set; } // Nullable yapıldı
        }
    }
}