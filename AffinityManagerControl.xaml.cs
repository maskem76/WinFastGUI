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
using Microsoft.VisualBasic; // Yalnızca Interaction.InputBox için kalacak
using System.Threading.Tasks;
using System.Globalization;
using System.Threading;
using WinFastGUI.Properties;

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
        private readonly List<string> _logMessages = new();
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
        public Dictionary<string, long> AutoAssignProfile { get; private set; } = new();

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
                            string cat = dev.GetProperty("category").GetString() ?? "Other";
                            int cc = dev.TryGetProperty("core_count", out var cce) ? cce.GetInt32() : 2;
                            long mask = cc > 0 ? (1L << cc) - 1 : 0;
                            AutoAssignProfile[cat] = mask;
                        }
                        _logger.Log(WinFastGUI.Properties.Strings.ProfileLoadedFromAffinityProfileJson);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(string.Format(WinFastGUI.Properties.Strings.ProfileFileCouldNotBeRead, ex.Message), "Error");
                }
            }
            AutoAssignProfile = new()
            {
                { "PCI", 0x2 },
                { "USB", 0x7 },
                { "Network", 0x4 },
                { "Storage", 0x7 },
                { "GPU", 0x8 },
                { "Audio", 0x7 },
                { "Bluetooth", 0x7 },
                { "Display", 0x10 },
                { "Controller", 0x7 },
                { "Camera", 0x1 },
                { "Printer", 0x1 },
                { "Monitor", 0x1 },
                { "Other", 0x7 }
            };
            _logger.Log(WinFastGUI.Properties.Strings.DefaultProfileLoaded);
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
            _logger.Log(WinFastGUI.Properties.Strings.ProfileSavedToAffinityProfileJson);
        }

        public string GuessCategory(string name, string desc)
        {
            string text = (name + " " + desc).ToLower();
            if (text.Contains("usb") || text.Contains("hub") || text.Contains("composite")) return "USB";
            if (text.Contains("ethernet") || text.Contains("network") || text.Contains("lan") || text.Contains("wi-fi") || text.Contains("wireless")) return "Network";
            if (text.Contains("audio") || text.Contains("sound") || text.Contains("realtek")) return "Audio";
            if (text.Contains("bluetooth")) return "Bluetooth";
            if (text.Contains("storage") || text.Contains("disk") || text.Contains("ssd") || text.Contains("hdd")) return "Storage";
            if (text.Contains("video") || text.Contains("display") || text.Contains("graphics") || text.Contains("nvidia") || text.Contains("amd") || text.Contains("intel hd")) return "Display";
            if (text.Contains("pci")) return "PCI";
            if (text.Contains("controller")) return "Controller";
            if (text.Contains("printer")) return "Printer";
            if (text.Contains("camera") || text.Contains("webcam")) return "Camera";
            if (text.Contains("monitor")) return "Monitor";
            return "Other";
        }

        public int GetDefaultCoreCountForCategory(string category)
        {
            if (AutoAssignProfile.TryGetValue(category, out long mask))
                return BitCount(mask);
            return 2;
        }

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

        public void AssignCores(List<DeviceAffinityInfo> devices)
        {
            int cpuCount = Environment.ProcessorCount;
            if (cpuCount < 2)
            {
                _logger.Log(WinFastGUI.Properties.Strings.ErrorInsufficientCores, "Error");
                return;
            }
            int nextCore = 0;
            int gpuCoreIndex = cpuCount - 1;
            var usedCores = new HashSet<int>();
            foreach (var device in devices)
            {
                if (device.Category == "GPU")
                {
                    device.CoreMask = (1L << gpuCoreIndex);
                    _logger.Log(string.Format(WinFastGUI.Properties.Strings.GPUAutoAssignment, device.Name, device.CoreMask.ToString("X"), gpuCoreIndex));
                    usedCores.Add(gpuCoreIndex);
                }
            }
            foreach (var device in devices)
            {
                if (device.Category == "GPU") continue;
                int coreCount = _profileService.GetDefaultCoreCountForCategory(device.Category);
                long mask = 0;
                List<int> assignedCores = new();
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
                _logger.Log(string.Format(WinFastGUI.Properties.Strings.AutoAssignment, device.Name, mask.ToString("X"), string.Join(", ", assignedCores.Select(x => $"CPU{x}"))));
            }
        }
    }

    public partial class AffinityManagerControl : UserControl
    {
        private readonly List<DeviceAffinityInfo> devices = new();
        private readonly Stack<List<DeviceAffinitySnapshot>> undoStack = new();
        private readonly LogService _logger;
        private readonly ProfileService _profileService;
        private readonly AffinityAutoAssigner _autoAssigner;
        private ListCollectionView? _deviceView;
        private string lastSearch = "";
        private string lastCategory = "All";
        private readonly System.Timers.Timer searchDebounceTimer = new(300);

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

            // Dil güncellemesi MainWindow'dan gelecek
            UpdateLanguage();
        }

        // Dil güncellemesi (MainWindow'dan çağrılacak)
        public void UpdateLanguage()
        {
            string currentCulture = Thread.CurrentThread.CurrentUICulture.Name;
            LogMessage($"Kontrol dili güncellendi: {currentCulture}"); // Hata ayıklama
            SaveMaskButton.Content = WinFastGUI.Properties.Strings.SaveMask;
            UndoButton.Content = WinFastGUI.Properties.Strings.Undo;
            AssignToAllButton.Content = WinFastGUI.Properties.Strings.AssignToAll;
            AutoAssignButton.Content = WinFastGUI.Properties.Strings.AutoAssign;
            ExportButton.Content = WinFastGUI.Properties.Strings.Export;
            ImportButton.Content = WinFastGUI.Properties.Strings.Import;
            EditProfileButton.Content = WinFastGUI.Properties.Strings.EditProfile;
            CreateClassicProfileButton.Content = WinFastGUI.Properties.Strings.CreateClassicProfile;
            CreateAdvancedProfileButton.Content = WinFastGUI.Properties.Strings.CreateAdvancedProfile;
            ClearLogButton.Content = WinFastGUI.Properties.Strings.ClearLog;

            // CategoryCombo'yu güncelle
            CategoryCombo.Items.Clear();
            CategoryCombo.Items.Add(new ComboBoxItem { Content = WinFastGUI.Properties.Strings.All });

            // Statik metinleri güncelle
            if (DeviceNameText != null) DeviceNameText.Text = WinFastGUI.Properties.Strings.NamePlaceholder;
            if (DeviceCategoryText != null) DeviceCategoryText.Text = WinFastGUI.Properties.Strings.CategoryPlaceholder;
            if (DeviceMaskText != null) DeviceMaskText.Text = WinFastGUI.Properties.Strings.MaskPlaceholder;
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
                        string name = obj["Name"]?.ToString() ?? "Unknown";
                        string desc = obj["Description"]?.ToString() ?? "";
                        string cat = _profileService.GuessCategory(name, desc);
                        long mask = _profileService.AutoAssignProfile.TryGetValue(cat, out long m) ? m : 0x3;
                        devices.Add(new DeviceAffinityInfo { Name = name, Category = cat, CoreMask = mask });
                    }
                });
                _deviceView = new ListCollectionView(devices) { Filter = FilterDevice };
                DeviceTree.ItemsSource = _deviceView;
                UpdateCategories();
                LogMessage(string.Format(WinFastGUI.Properties.Strings.DevicesLoaded, devices.Count));
                ShowNotification(string.Format(WinFastGUI.Properties.Strings.DevicesLoaded, devices.Count));
            }
            catch (ManagementException ex)
            {
                LogMessage(string.Format(WinFastGUI.Properties.Strings.WMIError, ex.Message), "Error");
                ShowNotification(WinFastGUI.Properties.Strings.DevicesCouldNotBeLoadedWMI, true);
            }
            catch (Exception ex)
            {
                LogMessage(string.Format(WinFastGUI.Properties.Strings.FailedToListHardware, ex.Message), "Error");
                ShowNotification(string.Format(WinFastGUI.Properties.Strings.FailedToListHardware, ex.Message), true);
            }
        }

        private void UpdateCategories()
        {
            CategoryCombo.Items.Clear();
            CategoryCombo.Items.Add(new ComboBoxItem { Content = WinFastGUI.Properties.Strings.All }); // Dil desteği
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
            LogMessage(string.Format(WinFastGUI.Properties.Strings.CoreCheckboxesCreated, count));
        }

        private void DeviceTree_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedDevices = DeviceTree.SelectedItems.Cast<DeviceAffinityInfo>().ToList();
            if (selectedDevices.Count == 0)
            {
                DeviceNameText.Text = WinFastGUI.Properties.Strings.NamePlaceholder;
                DeviceCategoryText.Text = WinFastGUI.Properties.Strings.CategoryPlaceholder;
                DeviceMaskText.Text = WinFastGUI.Properties.Strings.MaskPlaceholder;
                foreach (CheckBox chk in CoreCheckboxGrid.Children) chk.IsChecked = false;
                return;
            }
            if (selectedDevices.Count == 1)
            {
                var dev = selectedDevices[0];
                DeviceNameText.Text = $"{WinFastGUI.Properties.Strings.Name}: {dev.Name}";
                DeviceCategoryText.Text = $"{WinFastGUI.Properties.Strings.Category}: {dev.Category}";
                DeviceMaskText.Text = $"{WinFastGUI.Properties.Strings.Mask}: {MaskToCoreString(dev.CoreMask)}";
                for (int i = 0; i < CoreCheckboxGrid.Children.Count; i++)
                {
                    if (CoreCheckboxGrid.Children[i] is CheckBox chk && chk.Tag is int ix)
                        chk.IsChecked = ((dev.CoreMask >> ix) & 1) == 1;
                }
            }
            else
            {
                DeviceNameText.Text = $"{WinFastGUI.Properties.Strings.SelectedDevices} {selectedDevices.Count}";
                DeviceCategoryText.Text = WinFastGUI.Properties.Strings.CategoryMultipleDevices;
                long commonMask = selectedDevices[0].CoreMask;
                foreach (var dev in selectedDevices.Skip(1))
                    commonMask &= dev.CoreMask;
                DeviceMaskText.Text = $"{WinFastGUI.Properties.Strings.Mask}: {MaskToCoreString(commonMask)}";
                for (int i = 0; i < CoreCheckboxGrid.Children.Count; i++)
                {
                    if (CoreCheckboxGrid.Children[i] is CheckBox chk && chk.Tag is int ix)
                        chk.IsChecked = ((commonMask >> ix) & 1) == 1;
                }
            }
        }

        private void SaveMaskButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedDevices = DeviceTree.SelectedItems.Cast<DeviceAffinityInfo>().ToList();
            if (selectedDevices.Count == 0)
            {
                LogMessage(WinFastGUI.Properties.Strings.NoDeviceSelected, "Error");
                ShowNotification(WinFastGUI.Properties.Strings.PleaseSelectAtLeastOneDevice, true);
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
                LogMessage(string.Format(WinFastGUI.Properties.Strings.DeviceUpdated, dev.Name, MaskToCoreString(mask)));
            }
            _deviceView?.Refresh();
            ShowNotification(WinFastGUI.Properties.Strings.CoreAssignmentsSaved);
        }

        private void PushUndoSnapshot()
        {
            undoStack.Push(devices.Select(d => new DeviceAffinitySnapshot
            { DeviceName = d.Name, PreviousMask = d.CoreMask }).ToList());
            LogMessage(string.Format(WinFastGUI.Properties.Strings.UndoSnapshotSaved, devices.Count));
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            if (undoStack.Count == 0)
            {
                LogMessage(WinFastGUI.Properties.Strings.NoActionToUndo, "Error");
                ShowNotification(WinFastGUI.Properties.Strings.NoActionToUndo, true);
                return;
            }
            var snap = undoStack.Pop();
            foreach (var s in snap)
            {
                var dev = devices.FirstOrDefault(x => x.Name == s.DeviceName);
                if (dev != null) dev.CoreMask = s.PreviousMask;
            }
            _deviceView?.Refresh();
            LogMessage(WinFastGUI.Properties.Strings.LastActionUndone);
            ShowNotification(WinFastGUI.Properties.Strings.LastActionUndone);
        }

        private void AssignToAllButton_Click(object sender, RoutedEventArgs e)
        {
            long mask = 0;
            foreach (CheckBox chk in CoreCheckboxGrid.Children)
                if (chk.IsChecked == true && chk.Tag is int ix)
                    mask |= (1L << ix);
            if (mask == 0)
            {
                LogMessage(WinFastGUI.Properties.Strings.SelectAtLeastOneCore, "Error");
                ShowNotification(WinFastGUI.Properties.Strings.SelectAtLeastOneCore, true);
                return;
            }
            PushUndoSnapshot();
            foreach (var dev in devices)
            {
                dev.CoreMask = mask;
                LogMessage(string.Format(WinFastGUI.Properties.Strings.AssignedToAll, dev.Name, MaskToCoreString(mask)));
            }
            _deviceView?.Refresh();
            ShowNotification(WinFastGUI.Properties.Strings.CoreAssignmentsAppliedToAll);
        }

        private void AutoAssignButton_Click(object sender, RoutedEventArgs e)
        {
            PushUndoSnapshot();
            _logger.Clear();
            _autoAssigner.AssignCores(devices);
            _deviceView?.Refresh();
            LogMessage(WinFastGUI.Properties.Strings.AutoMaskAssignedToAllDevices);
            ShowNotification(WinFastGUI.Properties.Strings.AutoCoreAssignmentAppliedToAll);
        }

        private async void ShowNotification(string message, bool isError = false)
        {
            NotificationText.Text = message;
            NotificationText.Foreground = isError ? new SolidColorBrush(Color.FromRgb(229, 115, 115)) : new SolidColorBrush(Color.FromRgb(67, 160, 71));
            NotificationPanel.Visibility = Visibility.Visible;
            await Task.Delay(3000);
            NotificationPanel.Visibility = Visibility.Collapsed;
        }

        private string MaskToCoreString(long mask)
        {
            var cores = new List<string>();
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
                LogMessage(string.Format(WinFastGUI.Properties.Strings.ProfileExportedTo, dlg.FileName));
                ShowNotification(string.Format(WinFastGUI.Properties.Strings.ProfileExportedTo, dlg.FileName));
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
                    var importList = JsonSerializer.Deserialize<List<DeviceAffinitySnapshot>>(json);
                    if (importList == null)
                    {
                        LogMessage(WinFastGUI.Properties.Strings.ProfileImportFailedInvalidFile, "Error");
                        ShowNotification(WinFastGUI.Properties.Strings.ProfileImportFailedInvalidFile, true);
                        return;
                    }
                    PushUndoSnapshot();
                    foreach (var s in importList)
                    {
                        var dev = devices.FirstOrDefault(x => x.Name == s.DeviceName);
                        if (dev != null)
                        {
                            dev.CoreMask = s.PreviousMask;
                            LogMessage(string.Format(WinFastGUI.Properties.Strings.ProfileImported, dev.Name, MaskToCoreString(s.PreviousMask)));
                        }
                    }
                    _deviceView?.Refresh();
                    LogMessage(WinFastGUI.Properties.Strings.ProfileImportedSuccessfully);
                    ShowNotification(WinFastGUI.Properties.Strings.ProfileImportedSuccessfully);
                }
                catch (Exception ex)
                {
                    LogMessage(string.Format(WinFastGUI.Properties.Strings.ProfileLoadFailed, ex.Message), "Error");
                    ShowNotification(string.Format(WinFastGUI.Properties.Strings.ProfileLoadFailed, ex.Message), true);
                }
            }
        }

        private void EditProfileButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var cat in _profileService.AutoAssignProfile.Keys.ToList())
            {
                string input = Interaction.InputBox(string.Format(WinFastGUI.Properties.Strings.EnterCoreCountFor, cat), WinFastGUI.Properties.Strings.EditProfile, ProfileService.BitCount(_profileService.AutoAssignProfile[cat]).ToString());
                if (int.TryParse(input, out int cnt) && cnt >= 0)
                {
                    _profileService.AutoAssignProfile[cat] = cnt > 0 ? (1L << cnt) - 1 : 0;
                    LogMessage(string.Format(WinFastGUI.Properties.Strings.ProfileUpdated, cat, cnt, MaskToCoreString(_profileService.AutoAssignProfile[cat])));
                }
                else
                {
                    LogMessage(string.Format(WinFastGUI.Properties.Strings.ErrorInvalidCoreCountFor, cat, input), "Error");
                    ShowNotification(string.Format(WinFastGUI.Properties.Strings.InvalidCoreCount, input), true);
                }
            }
            _profileService.SaveProfile();
            LogMessage(WinFastGUI.Properties.Strings.ProfileUpdatedAndSaved);
            ShowNotification(WinFastGUI.Properties.Strings.ProfileUpdatedAndSaved);
        }

        private void CreateClassicProfileButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var cat in devices.Select(d => d.Category).Distinct())
                _profileService.AutoAssignProfile[cat] = 0x3;
            _profileService.SaveProfile();
            LogMessage(WinFastGUI.Properties.Strings.ClassicProfileCreatedAndSaved);
            ShowNotification(WinFastGUI.Properties.Strings.ClassicProfileCreated);
        }

        private void CreateAdvancedProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var advanced = new Dictionary<string, int>
            {
                { "PCI", 1 }, { "USB", 3 }, { "Network", 1 }, { "Storage", 3 }, { "GPU", 1 },
                { "Audio", 3 }, { "Bluetooth", 3 }, { "Display", 1 }, { "Controller", 3 },
                { "Camera", 1 }, { "Printer", 1 }, { "Monitor", 1 }, { "Other", 3 }
            };
            foreach (var cat in devices.Select(d => d.Category).Distinct())
                _profileService.AutoAssignProfile[cat] = advanced.TryGetValue(cat, out int cnt) ? (1L << cnt) - 1 : 0x3;
            _profileService.SaveProfile();
            LogMessage(WinFastGUI.Properties.Strings.AdvancedProfileCreatedAndSaved);
            ShowNotification(WinFastGUI.Properties.Strings.AdvancedProfileCreated);
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.Clear();
            LogMessage(WinFastGUI.Properties.Strings.LogsCleared);
            ShowNotification(WinFastGUI.Properties.Strings.LogsCleared);
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
            string category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? WinFastGUI.Properties.Strings.All; // Dil desteği
            string search = SearchBox.Text;
            if (search == lastSearch && category == lastCategory) return;
            lastSearch = search;
            lastCategory = category;
            _deviceView?.Refresh();
            LogMessage(string.Format(WinFastGUI.Properties.Strings.FilterApplied, category, search));
        }

        private bool FilterDevice(object item)
        {
            if (item is not DeviceAffinityInfo device) return false;
            string category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? WinFastGUI.Properties.Strings.All; // Dil desteği
            string search = SearchBox.Text;
            bool categoryMatch = category == WinFastGUI.Properties.Strings.All || device.Category == category;
            bool searchMatch = string.IsNullOrEmpty(search) || device.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
            return categoryMatch && searchMatch;
        }

        private void LogMessage(string msg, string level = "Info") => _logger.Log(msg, level);
    } // AffinityManagerControl sınıfının kapanış süslü ayrağı
} // Namespace'in kapanış süslü ayrağı