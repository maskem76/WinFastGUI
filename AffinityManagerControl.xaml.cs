using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.IO;
using System.Text.Json;
using System.Management;
using System.ComponentModel;
using System.Timers; // Bu artık kullanılmayacak, ama derleyici hatası olmaması için şimdilik bırakılabilir veya kaldırılabilir.
using Microsoft.VisualBasic;
using System.Threading.Tasks; // Task.Delay için eklendi

namespace WinFastGUI
{
    public class DeviceAffinityInfo
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public long CoreMask { get; set; }
    }
    public class DeviceAffinitySnapshot
    {
        public string DeviceName { get; set; } = "";
        public long PreviousMask { get; set; }
    }
    public class LogService
    {
        private static LogService? _instance;
        public static LogService Instance => _instance ??= new LogService(null);
        private ListBox? _listBox;
        private readonly List<string> _logMessages = new(); // Tip belirtildi
        private const string LOG_FILE = "AffinityLog.txt";
        private const int maximum = 500;
        public LogService(ListBox? listBox) => _listBox = listBox;
        public void SetListBox(ListBox listBox) => _listBox = listBox;
        public void Log(string message, string level = "Info")
        {
            string logEntry = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
            _logMessages.Add(logEntry);
            _listBox?.Dispatcher.Invoke(() =>
            {
                _listBox.Items.Add(logEntry);
                _listBox.ScrollIntoView(logEntry);
                if (_logMessages.Count > maximum) _listBox.Items.RemoveAt(0);
            });
            try
            {
                if (File.Exists(LOG_FILE) && new FileInfo(LOG_FILE).Length > 1024 * 1024)
                    File.WriteAllText(LOG_FILE, "");
                File.AppendAllText(LOG_FILE, logEntry + Environment.NewLine);
            }
            catch { }
        }
        public void Clear()
        {
            _logMessages.Clear();
            _listBox?.Dispatcher.Invoke(() => _listBox.Items.Clear());
        }
    }
    public class ProfileService
    {
        private readonly LogService _logger;
        public Dictionary<string, long> AutoAssignProfile { get; private set; } = new(); // Tip belirtildi
        public ProfileService(LogService logger)
        {
            _logger = logger;
            LoadProfile();
        }
        private void LoadProfile()
        {
            AutoAssignProfile.Clear();
            string path = "AffinityProfile.json";
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("auto_assignment", out var aa) &&
                        aa.TryGetProperty("devices", out var devicesElem))
                    {
                        foreach (var dev in devicesElem.EnumerateArray())
                        {
                            string cat = dev.GetProperty("category").GetString() ?? "Diğer";
                            int cc = dev.TryGetProperty("core_count", out var cce) ? cce.GetInt32() : 2;
                            long mask = cc > 0 ? (1L << cc) - 1 : 0;
                            AutoAssignProfile[cat] = mask;
                        }
                        _logger.Log("Profil AffinityProfile.json yüklendi.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"Profil dosyası okunamadı: {ex.Message}", "Error");
                }
            }
            // Varsayılan profil tanımlamaları ve CPU açıklamaları eklendi
            AutoAssignProfile = new()
            {
                { "PCI", 0x2 }, // CPU1 (0010)
                { "USB", 0x7 }, // CPU0, CPU1, CPU2 (0111)
                { "Ağ", 0x4 }, // CPU2 (0100)
                { "Depolama", 0x7 }, // CPU0, CPU1, CPU2 (0111)
                { "GPU", 0x8 }, // CPU3 (1000)
                { "Ses", 0x7 }, // CPU0, CPU1, CPU2 (0111)
                { "Bluetooth", 0x7 }, // CPU0, CPU1, CPU2 (0111)
                { "Görüntü", 0x10 }, // CPU4 (10000)
                { "Denetleyici", 0x7 }, // CPU0, CPU1, CPU2 (0111)
                { "Kamera", 0x1 }, // CPU0 (0001)
                { "Yazıcı", 0x1 }, // CPU0 (0001)
                { "Monitör", 0x1 }, // CPU0 (0001)
                { "Diğer", 0x7 } // CPU0, CPU1, CPU2 (0111)
            };
            _logger.Log("Varsayılan profil yüklendi.");
        }
        public void SaveProfile()
        {
            var root = new
            {
                version = "1.0",
                auto_assignment = new
                {
                    enabled = true,
                    default_core_count = 2,
                    devices = AutoAssignProfile.Select(kvp => new
                    {
                        category = kvp.Key,
                        core_count = BitCount(kvp.Value),
                        match = new { type = "*", keywords = new[] { kvp.Key.ToLower() } }
                    }).ToList()
                }
            };
            var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText("AffinityProfile.json", json);
            _logger.Log("Profil AffinityProfile.json dosyasına kaydedildi.");
        }
        public string GuessCategory(string name, string desc)
        {
            string text = (name + " " + desc).ToLower();
            if (text.Contains("usb") || text.Contains("hub") || text.Contains("composite")) return "USB";
            if (text.Contains("ethernet") || text.Contains("network") || text.Contains("lan") || text.Contains("wi-fi") || text.Contains("wireless")) return "Ağ";
            if (text.Contains("audio") || text.Contains("sound") || text.Contains("realtek")) return "Ses";
            if (text.Contains("bluetooth")) return "Bluetooth";
            if (text.Contains("storage") || text.Contains("disk") || text.Contains("ssd") || text.Contains("hdd")) return "Depolama";
            if (text.Contains("video") || text.Contains("display") || text.Contains("graphics") || text.Contains("nvidia") || text.Contains("amd") || text.Contains("intel hd")) return "Görüntü";
            if (text.Contains("pci")) return "PCI";
            if (text.Contains("controller")) return "Denetleyici";
            if (text.Contains("printer")) return "Yazıcı";
            if (text.Contains("camera") || text.Contains("webcam")) return "Kamera";
            if (text.Contains("monitor")) return "Monitör";
            return "Diğer";
        }
        public int GetDefaultCoreCountForCategory(string category)
        {
            if (AutoAssignProfile.TryGetValue(category, out long mask))
                return BitCount(mask);
            return 2;
        }
        /// <summary>
        /// Verilen long değerindeki set edilmiş (1 olan) bit sayısını döndürür.
        /// Bu, maskenin kaç CPU çekirdeğini temsil ettiğini gösterir.
        /// </summary>
        public static int BitCount(long val)
        {
            int cnt = 0;
            while (val != 0) { cnt += (int)(val & 1); val >>= 1; }
            return cnt;
        }
    }
    public class AffinityAutoAssigner
    {
        private readonly LogService _logger;
        private readonly ProfileService _profileService;
        public AffinityAutoAssigner(LogService logger, ProfileService profileService)
        {
            _logger = logger;
            _profileService = profileService;
        }
        public void AssignCores(List<DeviceAffinityInfo> devices) // Tip belirtildi
        {
            int cpuCount = Environment.ProcessorCount;
            if (cpuCount < 2)
            {
                _logger.Log("Hata: Yeterli çekirdek yok.", "Error"); // LogMessage ile değiştirildi
                return;
            }
            int nextCore = 0;
            int gpuCoreIndex = cpuCount - 1;
            var usedCores = new HashSet<int>(); // Tip belirtildi
            foreach (var device in devices)
            {
                if (device.Category == "GPU")
                {
                    device.CoreMask = (1L << gpuCoreIndex);
                    _logger.Log($"GPU otomatik atama: {device.Name} → Mask=0x{device.CoreMask:X} (CPU{gpuCoreIndex})"); // LogMessage ile değiştirildi
                    usedCores.Add(gpuCoreIndex);
                }
            }
            foreach (var device in devices)
            {
                if (device.Category == "GPU") continue;
                int coreCount = _profileService.GetDefaultCoreCountForCategory(device.Category);
                long mask = 0;
                List<int> assignedCores = new(); // Tip belirtildi
                for (int i = 0; i < coreCount; i++)
                {
                    int coreIndex;
                    do
                    {
                        coreIndex = (nextCore++) % cpuCount;
                    } while (coreIndex == gpuCoreIndex && usedCores.Contains(coreIndex));
                    mask |= (1L << coreIndex);
                    assignedCores.Add(coreIndex);
                    usedCores.Add(coreIndex);
                }
                device.CoreMask = mask;
                _logger.Log($"Otomatik atama: {device.Name} → Mask=0x{mask:X} ({string.Join(", ", assignedCores.Select(x => $"CPU{x}"))})"); // LogMessage ile değiştirildi
            }
        }
    }
    public partial class AffinityManagerControl : UserControl
    {
        private readonly List<DeviceAffinityInfo> devices = new(); // Tip belirtildi
        private readonly Stack<List<DeviceAffinitySnapshot>> undoStack = new(); // Tip belirtildi
        private readonly LogService _logger;
        private readonly ProfileService _profileService;
        private readonly AffinityAutoAssigner _autoAssigner;
        private ListCollectionView? _deviceView;
        private string lastSearch = "";
        private string lastCategory = "Tümü";
        private readonly System.Timers.Timer searchDebounceTimer = new(300); // Bu timer hala kullanılıyor, ShowNotification'daki değiştirildi.
        public AffinityManagerControl()
        {
            InitializeComponent();
            _logger = LogService.Instance;
            _logger.SetListBox(LogListBox);
            _profileService = new ProfileService(_logger);
            _autoAssigner = new AffinityAutoAssigner(_logger, _profileService);
            searchDebounceTimer.Elapsed += (s, args) => Dispatcher.Invoke(() => FilterDevices());
            LoadDevices();
            BuildCoreCheckboxGrid();
            UpdateCategories();
        }
        private async void LoadDevices()
        {
            devices.Clear();
            try
            {
                await Task.Run(() =>
                {
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString() ?? "Bilinmeyen";
                        string desc = obj["Description"]?.ToString() ?? "";
                        string cat = _profileService.GuessCategory(name, desc);
                        long mask = _profileService.AutoAssignProfile.TryGetValue(cat, out long m) ? m : 0x3;
                        devices.Add(new DeviceAffinityInfo { Name = name, Category = cat, CoreMask = mask });
                    }
                });
                _deviceView = new ListCollectionView(devices) { Filter = FilterDevice };
                DeviceTree.ItemsSource = _deviceView;
                // Dispatcher.Invoke(() => UpdateCategories()); // Bu satır kaldırıldı
                UpdateCategories(); // Doğrudan çağrıldı, çünkü zaten UI thread'deyiz.
                LogMessage($"{devices.Count} cihaz yüklendi.");
                ShowNotification($"{devices.Count} cihaz yüklendi.");
            }
            catch (ManagementException ex)
            {
                LogMessage($"WMI hatası: {ex.Message}", "Error");
                ShowNotification("Cihazlar yüklenemedi: WMI servisi çalışmıyor olabilir.", true);
            }
            catch (Exception ex)
            {
                LogMessage($"Donanımlar listelenemedi: {ex.Message}", "Error");
                ShowNotification($"Donanımlar listelenemedi: {ex.Message}", true);
            }
        }
        private void UpdateCategories()
        {
            CategoryCombo.Items.Clear();
            CategoryCombo.Items.Add(new ComboBoxItem { Content = "Tümü" });
            foreach (var cat in devices.Select(d => d.Category).Distinct().OrderBy(c => c))
                CategoryCombo.Items.Add(new ComboBoxItem { Content = cat });
            CategoryCombo.SelectedIndex = 0;
        }
        private void BuildCoreCheckboxGrid()
        {
            CoreCheckboxGrid.Children.Clear();
            int count = Environment.ProcessorCount;
            for (int i = 0; i < count; i++)
            {
                var chk = new CheckBox
                {
                    Content = $"CPU{i}",
                    Tag = i,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 12, 7)
                };
                CoreCheckboxGrid.Children.Add(chk);
            }
            LogMessage($"Çekirdek kutuları ({count}) oluşturuldu.");
        }
        private void DeviceTree_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedDevices = DeviceTree.SelectedItems.Cast<DeviceAffinityInfo>().ToList(); // Tip belirtildi
            if (selectedDevices.Count == 0)
            {
                DeviceNameText.Text = "Ad: -";
                DeviceCategoryText.Text = "Kategori: -";
                DeviceMaskText.Text = "Mask: -";
                foreach (CheckBox chk in CoreCheckboxGrid.Children) chk.IsChecked = false;
                return;
            }
            if (selectedDevices.Count == 1)
            {
                var dev = selectedDevices[0];
                DeviceNameText.Text = $"Ad: {dev.Name}";
                DeviceCategoryText.Text = $"Kategori: {dev.Category}";
                DeviceMaskText.Text = $"Mask: {MaskToCoreString(dev.CoreMask)}";
                for (int i = 0; i < CoreCheckboxGrid.Children.Count; i++)
                {
                    if (CoreCheckboxGrid.Children[i] is CheckBox chk && chk.Tag is int ix)
                        chk.IsChecked = ((dev.CoreMask >> ix) & 1) == 1;
                }
            }
            else
            {
                DeviceNameText.Text = $"Seçilen Cihazlar: {selectedDevices.Count}";
                DeviceCategoryText.Text = "Kategori: Birden fazla cihaz";
                long commonMask = selectedDevices[0].CoreMask;
                foreach (var dev in selectedDevices.Skip(1))
                    commonMask &= dev.CoreMask;
                DeviceMaskText.Text = $"Mask: {MaskToCoreString(commonMask)}";
                for (int i = 0; i < CoreCheckboxGrid.Children.Count; i++)
                {
                    if (CoreCheckboxGrid.Children[i] is CheckBox chk && chk.Tag is int ix)
                        chk.IsChecked = ((commonMask >> ix) & 1) == 1;
                }
            }
        }
        private void SaveMaskButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedDevices = DeviceTree.SelectedItems.Cast<DeviceAffinityInfo>().ToList(); // Tip belirtildi
            if (selectedDevices.Count == 0)
            {
                LogMessage("Cihaz seçili değil.", "Error");
                ShowNotification("Lütfen en az bir cihaz seçin.", true);
                return;
            }
            PushUndoSnapshot();
            long mask = 0;
            foreach (CheckBox chk in CoreCheckboxGrid.Children)
            {
                if (chk.IsChecked == true && chk.Tag is int ix)
                    mask |= (1L << ix);
            }
            foreach (var dev in selectedDevices)
            {
                dev.CoreMask = mask;
                LogMessage($"Cihaz güncellendi: {dev.Name} Mask={MaskToCoreString(mask)}");
            }
            _deviceView?.Refresh();
            ShowNotification("Çekirdek atamaları kaydedildi.");
        }
        private void PushUndoSnapshot()
        {
            undoStack.Push(devices.Select(d => new DeviceAffinitySnapshot
            { DeviceName = d.Name, PreviousMask = d.CoreMask }).ToList());
            LogMessage($"Geri alma anlık görüntüsü kaydedildi: {devices.Count} cihaz.");
        }
        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            if (undoStack.Count == 0)
            {
                LogMessage("Geri alınacak işlem yok.", "Error");
                ShowNotification("Geri alınacak işlem yok.", true);
                return;
            }
            var snap = undoStack.Pop();
            foreach (var s in snap)
            {
                var dev = devices.FirstOrDefault(x => x.Name == s.DeviceName);
                if (dev != null) dev.CoreMask = s.PreviousMask;
            }
            _deviceView?.Refresh();
            LogMessage("Son işlem geri alındı.");
            ShowNotification("Son işlem geri alındı.");
        }
        private void AssignToAllButton_Click(object sender, RoutedEventArgs e)
        {
            long mask = 0;
            foreach (CheckBox chk in CoreCheckboxGrid.Children)
                if (chk.IsChecked == true && chk.Tag is int ix)
                    mask |= (1L << ix);
            if (mask == 0)
            {
                LogMessage("En az bir çekirdek seçin.", "Error");
                ShowNotification("En az bir çekirdek seçin.", true);
                return;
            }
            PushUndoSnapshot();
            foreach (var dev in devices)
            {
                dev.CoreMask = mask;
                LogMessage($"Tümüne atama: {dev.Name} Mask={MaskToCoreString(mask)}");
            }
            _deviceView?.Refresh();
            ShowNotification("Tüm cihazlara çekirdek ataması yapıldı.");
        }
        private void AutoAssignButton_Click(object sender, RoutedEventArgs e)
        {
            PushUndoSnapshot();
            _logger.Clear(); // _logger.Clear() doğrudan çağrısı yerine LogService.Clear() çağrısı kullanılabilir, ama bu zaten tutarlı.
            _autoAssigner.AssignCores(devices);
            _deviceView?.Refresh();
            LogMessage("Tüm cihazlara otomatik mask atandı."); // _logger.Log yerine LogMessage
            ShowNotification("Tüm cihazlara otomatik çekirdek ataması yapıldı.");
        }
        // ShowNotification metodu Task.Delay kullanacak şekilde güncellendi
        private async void ShowNotification(string message, bool isError = false)
        {
            NotificationText.Text = message;
            NotificationText.Foreground = isError ? new SolidColorBrush(Color.FromRgb(229, 115, 115)) : new SolidColorBrush(Color.FromRgb(67, 160, 71));
            NotificationPanel.Visibility = Visibility.Visible;
            await Task.Delay(3000); // 3 saniye bekle
            NotificationPanel.Visibility = Visibility.Collapsed;
        }
        private string MaskToCoreString(long mask)
        {
            var cores = new List<string>(); // Tip belirtildi
            for (int i = 0; i < Environment.ProcessorCount; i++)
                if (((mask >> i) & 1) == 1)
                    cores.Add($"CPU{i}");
            return $"0x{mask:X} ({string.Join(", ", cores)})";
        }
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "JSON Files|*.json" };
            if (dlg.ShowDialog() == true)
            {
                var exportList = devices.Select(d => new DeviceAffinitySnapshot { DeviceName = d.Name, PreviousMask = d.CoreMask }).ToList();
                File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(exportList, new JsonSerializerOptions { WriteIndented = true }));
                LogMessage($"Profil {dlg.FileName} olarak dışa aktarıldı.");
                ShowNotification($"Profil {dlg.FileName} olarak dışa aktarıldı.");
            }
        }
        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON Files|*.json" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dlg.FileName);
                    var importList = JsonSerializer.Deserialize<List<DeviceAffinitySnapshot>>(json); // Tip belirtildi
                    if (importList == null)
                    {
                        LogMessage("Profil içe aktarılamadı: Geçersiz dosya.", "Error");
                        ShowNotification("Profil içe aktarılamadı: Geçersiz dosya.", true);
                        return;
                    }
                    PushUndoSnapshot();
                    foreach (var s in importList)
                    {
                        var dev = devices.FirstOrDefault(x => x.Name == s.DeviceName);
                        if (dev != null)
                        {
                            dev.CoreMask = s.PreviousMask;
                            LogMessage($"Profil içe aktarıldı: {dev.Name} Mask={MaskToCoreString(s.PreviousMask)}");
                        }
                    }
                    _deviceView?.Refresh();
                    LogMessage("Profil başarıyla içe aktarıldı.");
                    ShowNotification("Profil başarıyla içe aktarıldı.");
                }
                catch (Exception ex)
                {
                    LogMessage($"Profil yüklenemedi: {ex.Message}", "Error");
                    ShowNotification($"Profil yüklenemedi: {ex.Message}", true);
                }
            }
        }
        private void EditProfileButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var cat in _profileService.AutoAssignProfile.Keys.ToList())
            {
                string input = Interaction.InputBox($"{cat} için çekirdek sayısı giriniz (örn: 3)", "Profil Düzenle", ProfileService.BitCount(_profileService.AutoAssignProfile[cat]).ToString());
                if (int.TryParse(input, out int cnt) && cnt >= 0)
                {
                    _profileService.AutoAssignProfile[cat] = cnt > 0 ? (1L << cnt) - 1 : 0;
                    LogMessage($"Profil güncellendi: Kategori={cat}, Çekirdek Sayısı={cnt}, Mask={MaskToCoreString(_profileService.AutoAssignProfile[cat])}");
                }
                else
                {
                    LogMessage($"Hata: {cat} için geçersiz çekirdek sayısı: {input}", "Error");
                    ShowNotification($"Geçersiz çekirdek sayısı: {input}", true);
                }
            }
            _profileService.SaveProfile();
            LogMessage("Profil güncellendi ve kaydedildi.");
            ShowNotification("Profil güncellendi ve kaydedildi.");
        }
        private void CreateClassicProfileButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var cat in devices.Select(d => d.Category).Distinct())
                _profileService.AutoAssignProfile[cat] = 0x3;
            _profileService.SaveProfile();
            LogMessage("Klasik profil oluşturuldu ve kaydedildi.");
            ShowNotification("Klasik profil oluşturuldu.");
        }
        private void CreateAdvancedProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var advanced = new Dictionary<string, int> // Tip belirtildi
            {
                { "PCI", 1 }, { "USB", 3 }, { "Ağ", 1 }, { "Depolama", 3 }, { "GPU", 1 },
                { "Ses", 3 }, { "Bluetooth", 3 }, { "Görüntü", 1 }, { "Denetleyici", 3 },
                { "Kamera", 1 }, { "Yazıcı", 1 }, { "Monitör", 1 }, { "Diğer", 3 }
            };
            foreach (var cat in devices.Select(d => d.Category).Distinct())
                _profileService.AutoAssignProfile[cat] = advanced.TryGetValue(cat, out int cnt) ? (1L << cnt) - 1 : 0x3;
            _profileService.SaveProfile();
            LogMessage("Gelişmiş profil oluşturuldu ve kaydedildi.");
            ShowNotification("Gelişmiş profil oluşturuldu.");
        }
        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.Clear();
            LogMessage("Loglar temizlendi.");
            ShowNotification("Loglar temizlendi.");
        }
        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterDevices();
        }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            searchDebounceTimer.Stop();
            searchDebounceTimer.Start();
        }
        private void FilterDevices()
        {
            string category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Tümü";
            string search = SearchBox.Text;
            if (search == lastSearch && category == lastCategory) return;
            lastSearch = search;
            lastCategory = category;
            _deviceView?.Refresh();
            LogMessage($"Filtre uygulandı: Kategori={category}, Arama={search}");
        }
        private bool FilterDevice(object item)
        {
            if (item is not DeviceAffinityInfo device) return false;
            string category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Tümü";
            string search = SearchBox.Text;
            bool categoryMatch = category == "Tümü" || device.Category == category;
            bool searchMatch = string.IsNullOrEmpty(search) || device.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
            return categoryMatch && searchMatch;
        }
        private void LogMessage(string msg, string level = "Info") => _logger.Log(msg, level);
    }
}