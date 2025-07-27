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
            public string? DeviceName { get; set; } = ""; // Made nullable
            public string? PnpDeviceId { get; set; } = ""; // Made nullable
            private string? _currentAffinity = "Unknown"; // Made nullable
            public string? CurrentAffinity
            {
                get => _currentAffinity;
                set { _currentAffinity = value; OnPropertyChanged(nameof(CurrentAffinity)); }
            }
            private string? _newAffinity = "-"; // Made nullable
            public string? NewAffinity
            {
                get => _newAffinity;
                set { _newAffinity = value; OnPropertyChanged(nameof(NewAffinity)); }
            }
            private string? _originalAffinity = ""; // Made nullable
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
            public string? Keyword { get; set; } = ""; // Made nullable
            public int[] TargetCpus { get; set; } = Array.Empty<int>();
            public string? DisplayName { get; set; } = ""; // Made nullable
        }

        private readonly List<HeavyDeviceRule> heavyDeviceRules = new List<HeavyDeviceRule>
        {
            new HeavyDeviceRule { Keyword = "nvidia", TargetCpus = new[] { 4, 5 }, DisplayName = "Graphics Card (NVIDIA)" },
            new HeavyDeviceRule { Keyword = "geforce", TargetCpus = new[] { 4, 5 }, DisplayName = "Graphics Card (NVIDIA)" },
            new HeavyDeviceRule { Keyword = "radeon", TargetCpus = new[] { 2, 3 }, DisplayName = "Graphics Card (AMD)" },
            new HeavyDeviceRule { Keyword = "ethernet", TargetCpus = new[] { 6, 7 }, DisplayName = "Ethernet" },
            new HeavyDeviceRule { Keyword = "realtek", TargetCpus = new[] { 6, 7 }, DisplayName = "Network Card" },
            new HeavyDeviceRule { Keyword = "intel", TargetCpus = new[] { 8, 9 }, DisplayName = "Network Card" },
            new HeavyDeviceRule { Keyword = "nvme", TargetCpus = new[] { 10, 11 }, DisplayName = "NVMe SSD" },
        };

        private readonly string backupPath = Path.Combine(AppContext.BaseDirectory, "backups");
        private List<IrqInfo> originalDeviceList = new List<IrqInfo>();
        private List<IrqInfo>? cachedDeviceList = null; // Made nullable
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
            string? userName = identity.Name; // Made nullable

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
                    string? nsudoPath = Path.Combine(AppContext.BaseDirectory, "Modules", "nsudo", "NSudo.exe"); // Made nullable
                    if (string.IsNullOrEmpty(nsudoPath) || !File.Exists(nsudoPath))
                    {
                        MessageBox.Show($"NSudo.exe not found at the specified path!\n{nsudoPath ?? "Unknown path"}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        Application.Current.Shutdown();
                        return;
                    }

                    string? appPath = Process.GetCurrentProcess().MainModule?.FileName; // Made nullable
                    if (string.IsNullOrEmpty(appPath))
                    {
                        MessageBox.Show("Application path could not be retrieved. Program is closing.", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        Application.Current.Shutdown();
                        return;
                    }

                    // Null-safe assignment
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = nsudoPath ?? throw new InvalidOperationException("NSudo path is null"),
                        Arguments = $"-U:T -P:E -M:S \"{appPath ?? throw new InvalidOperationException("Application path is null")}\"",
                        UseShellExecute = true
                    };

                    Process.Start(startInfo);
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error during automatic elevation with NSudo:\n{ex.Message}", "Elevation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown();
                }
            }
        }

        private async void LoadDeviceList(bool forceRefresh = false)
        {
            if (!forceRefresh && cachedDeviceList != null && (DateTime.Now - lastCacheUpdate).TotalMinutes < 5)
            {
                IrqDataGrid.ItemsSource = cachedDeviceList;
                IrqLogText.Text = "Device list loaded from cache.";
                return;
            }

            IrqLogText.Text = "Loading devices...";
            IrqDataGrid.ItemsSource = null;

            try
            {
                var deviceList = await Task.Run(() =>
                {
                    var devices = new List<IrqInfo>();
                    var pnpSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity");

                    foreach (ManagementObject dev in pnpSearcher.Get())
                    {
                        string? deviceName = dev["Caption"] as string; // Made nullable
                        string? pnpDeviceId = dev["DeviceID"] as string; // Made nullable

                        if (string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(pnpDeviceId))
                            continue;

                        bool shouldSkip = pnpDeviceId.StartsWith("ACPI\\PNP0") || pnpDeviceId.StartsWith("ROOT\\") ||
                                         deviceName.ToLower().Contains("microsoft") || deviceName.ToLower().Contains("system") ||
                                         deviceName.ToLower().Contains("volume") || deviceName.ToLower().Contains("resources");

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
                    IrqLogText.Text = $"Total {cachedDeviceList.Count} devices listed. Use the buttons for assignment.";
                }
                else
                {
                    IrqLogText.Text = "No devices found to list.";
                }
            }
            catch (Exception ex)
            {
                IrqLogText.Text = $"An error occurred while listing devices: {ex.Message}";
                LogError("LoadDeviceList error", ex);
                Debug.WriteLine($"LoadDeviceList Error: {ex}");
            }
        }

        private void AutoAssignIrqButton_Click(object sender, RoutedEventArgs e)
        {
            if (IrqDataGrid.ItemsSource == null)
            {
                IrqLogText.Text = "Error: Device list could not be loaded.";
                return;
            }
            var irqList = IrqDataGrid.ItemsSource as List<IrqInfo>;
            if (irqList == null)
            {
                IrqLogText.Text = "Error: Device list is invalid.";
                return;
            }

            int cpuCount = Environment.ProcessorCount;
            if (cpuCount == 0)
            {
                IrqLogText.Text = "Error: No processor cores found in the system.";
                return;
            }

            var usedCpus = new HashSet<int>();
            var heavyDeviceIndexes = new HashSet<int>();

            for (int i = 0; i < irqList.Count; i++)
            {
                var irq = irqList[i];
                string? nameLower = irq.DeviceName?.ToLower(); // Null check

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
            IrqLogText.Text = "Multiple cores assigned to heavy devices, remaining cores to others. Suggested load: CPU 4-5 (70%), CPU 6-7 (30%). Press 'Apply Changes' to apply.";
        }

        private void ResetPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (IrqDataGrid.ItemsSource == null)
            {
                IrqLogText.Text = "Error: Device list could not be loaded.";
                return;
            }
            var irqList = IrqDataGrid.ItemsSource as List<IrqInfo>;
            if (irqList == null)
            {
                IrqLogText.Text = "Error: Device list is invalid.";
                return;
            }
            foreach (var device in irqList)
            {
                device.NewAffinity = "-";
            }
            IrqDataGrid.Items.Refresh();
            IrqLogText.Text = "Preview cleared. Devices returned to initial state.";
        }

        private void BackupSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (IrqDataGrid.ItemsSource == null)
            {
                IrqLogText.Text = "Error: Device list could not be loaded.";
                return;
            }
            var irqList = IrqDataGrid.ItemsSource as List<IrqInfo>;
            if (irqList == null)
            {
                IrqLogText.Text = "Error: Device list is invalid.";
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

                // Validation
                var readBackData = System.Text.Json.JsonSerializer.Deserialize<BackupData>(json);
                if (readBackData == null || readBackData.Devices == null || readBackData.Devices.Count != irqList.Count)
                {
                    IrqLogText.Text = "Backup file could not be validated, please try again.";
                    LogError("Backup validation error");
                    return;
                }

                IrqLogText.Text = $"Settings backed up to {backupFile}.";
            }
            catch (Exception ex)
            {
                IrqLogText.Text = $"Backup error: {ex.Message}";
                LogError("Backup error", ex);
            }
        }

        private void RestoreSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(backupPath))
            {
                IrqLogText.Text = "Backup directory not found.";
                return;
            }
            string[] backupFiles = Directory.GetFiles(backupPath, "affinity_backup_*.json");
            if (backupFiles.Length == 0)
            {
                IrqLogText.Text = "No backup file found.";
                return;
            }

            if (IrqDataGrid.ItemsSource == null)
            {
                IrqLogText.Text = "Error: Device list could not be loaded.";
                return;
            }
            var irqList = IrqDataGrid.ItemsSource as List<IrqInfo>;
            if (irqList == null)
            {
                IrqLogText.Text = "Error: Device list is invalid.";
                return;
            }

            string latestBackup = backupFiles.OrderByDescending(f => File.GetLastWriteTime(f)).First();
            try
            {
                string json = File.ReadAllText(latestBackup);
                var backupData = System.Text.Json.JsonSerializer.Deserialize<BackupData>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (backupData == null || backupData.Devices == null)
                {
                    IrqLogText.Text = "Backup data is invalid or missing.";
                    return;
                }

                foreach (var device in irqList)
                {
                    var backupDevice = backupData.Devices.FirstOrDefault(d => d.PnpDeviceId == device.PnpDeviceId);
                    if (backupDevice != null)
                    {
                        device.NewAffinity = backupDevice.OriginalAffinity != "Default" ? backupDevice.OriginalAffinity : "-";
                    }
                }
                IrqDataGrid.Items.Refresh();
                IrqLogText.Text = $"Settings restored from {latestBackup}. Press 'Apply Changes' to apply.";
            }
            catch (Exception ex)
            {
                IrqLogText.Text = $"Restore error: {ex.Message}";
                LogError("Restore error", ex);
            }
        }

        private void UndoChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (IrqDataGrid.ItemsSource == null)
            {
                IrqLogText.Text = "Error: Device list could not be loaded.";
                return;
            }
            var irqList = IrqDataGrid.ItemsSource as List<IrqInfo>;
            if (irqList == null)
            {
                IrqLogText.Text = "Error: Device list is invalid.";
                return;
            }
            foreach (var device in irqList)
            {
                var original = originalDeviceList.FirstOrDefault(d => d.PnpDeviceId == device.PnpDeviceId);
                if (original != null)
                {
                    device.NewAffinity = original.OriginalAffinity != "Default" ? original.OriginalAffinity : "-";
                }
            }
            IrqDataGrid.Items.Refresh();
            IrqLogText.Text = "Changes undone. Returned to original affinity state.";
        }

        private async void ApplyIrqAffinityButton_Click(object sender, RoutedEventArgs e)
        {
            if (IrqDataGrid.ItemsSource == null)
            {
                IrqLogText.Text = "Error: Device list could not be loaded.";
                return;
            }
            var irqList = IrqDataGrid.ItemsSource as List<IrqInfo>;
            if (irqList == null)
            {
                IrqLogText.Text = "Error: Device list is invalid.";
                return;
            }

            var itemsToApply = irqList.Where(irq => irq.NewAffinity != "-" && irq.NewAffinity?.StartsWith("CPU") == true).ToList();
            if (!itemsToApply.Any())
            {
                IrqLogText.Text = "No changes to apply.";
                return;
            }

            if (MessageBox.Show("Are you sure you want to apply the changes? This operation may affect your system.", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
            {
                IrqLogText.Text = "Changes cancelled.";
                return;
            }

            IrqLogText.Text = "Applying changes, please wait...";
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
                            continue; // Skip if PnpDeviceId is null
                        }

                        // Get CPU number and create mask
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
                            string? fullErrorDetails = $"DETAILED ERROR ({irq.DeviceName}):\n{ex}";
                            IrqLogText.Text = $"Error: Settings could not be applied for {irq.DeviceName}.";
                            MessageBox.Show(fullErrorDetails ?? "Error details could not be retrieved", "Registry Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            LogError("ApplyAffinity error", ex);
                        });
                        break;
                    }
                }
            });

            ProgressBar.Visibility = Visibility.Collapsed;
            IrqLogText.Text = $"Process completed. Affinity settings applied for {appliedCount} devices. You MAY NEED TO RESTART your system for changes to take effect.";
            if (appliedCount > 0)
            {
                MessageBox.Show("Settings applied.\nIt is recommended to restart your computer for the changes to take full effect.", "Applied", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadDeviceList(true); // Refresh cache
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
            return cores.Any() ? string.Join(", ", cores) : "None";
        }

        private string ReadCurrentAffinity(string? pnpDeviceId) // Made nullable
        {
            try
            {
                if (string.IsNullOrEmpty(pnpDeviceId)) return "Default";
                string registryPolicyPath = $"SYSTEM\\CurrentControlSet\\Enum\\{pnpDeviceId}\\Device Parameters\\Interrupt Management\\Affinity Policy";
                using (RegistryKey? affinityKey = Registry.LocalMachine.OpenSubKey(registryPolicyPath)) // Made nullable
                {
                    if (affinityKey == null) return "Default";
                    object? policyValue = affinityKey.GetValue("DevicePolicy"); // Made nullable
                    if (policyValue is int policy && policy == 0x00000001)
                    {
                        object? overrideValue = affinityKey.GetValue("AssignmentSetOverride"); // Made nullable
                        if (overrideValue is long mask)
                        {
                            return MaskToCoreString(mask);
                        }
                        else if (overrideValue is byte[] byteMask && byteMask.Length == 8)
                        {
                            return MaskToCoreString(BitConverter.ToInt64(byteMask, 0));
                        }
                    }
                    return "Default";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Affinity read error ({pnpDeviceId}): {ex.Message}");
                LogError($"Affinity read error ({pnpDeviceId})", ex);
                return "Error";
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

        // BackupData class definition
        private class BackupData
        {
            public string? Timestamp { get; set; } // Made nullable
            public List<DeviceBackup>? Devices { get; set; } // Made nullable
        }

        private class DeviceBackup
        {
            public string? PnpDeviceId { get; set; } // Made nullable
            public string? OriginalAffinity { get; set; } // Made nullable
        }
    }
}