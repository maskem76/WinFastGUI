using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;

namespace WinFastGUI.Controls
{
    public partial class BloatwareCleanerUserControl : UserControl
    {
        public Action<string>? LogMessageToMain { get; set; }

        private List<BloatwareItem> _bloatwareList = new();
        private HashSet<string> _selectedApps = new();

        public class BloatwareItem
        {
            public string Name { get; set; } = string.Empty;
            public bool Recommended { get; set; }
            public string? Description { get; set; }
        }

        public BloatwareCleanerUserControl()
        {
            InitializeComponent();
            LoadBloatwareList();
            SelectAllButton.Click += (s, e) => SelectAllItems();
            SelectRecommendedButton.Click += (s, e) => SelectRecommendedItems();
            ClearSelectionButton.Click += (s, e) => ClearSelection();
            RemoveButton.Click += RemoveSelectedBloatware_Click;
            RemoveAllButton.Click += RemoveAllBloatware_Click;
            RestoreButton.Click += RestoreBloatware_Click;
        }

        private void LoadBloatwareList()
        {
            try
            {
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "BloatwareList.json");
                if (!File.Exists(jsonPath))
                    jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "Modules", "BloatwareList.json");

                if (File.Exists(jsonPath))
                {
                    string json = File.ReadAllText(jsonPath);
                    _bloatwareList = JsonSerializer.Deserialize<List<BloatwareItem>>(json) ?? new List<BloatwareItem>();
                    DisplayBloatwareItems();
                }
                else
                {
                    BloatwareLogTextBox.AppendText($"Hata: BloatwareList.json bulunamadı. Yol: {jsonPath}\n");
                }
            }
            catch (Exception ex)
            {
                BloatwareLogTextBox.AppendText($"Bloatware listesi yüklenirken hata oluştu: {ex.Message}\n");
            }
        }

        private void DisplayBloatwareItems()
        {
            BloatwareListPanel.Children.Clear();
            foreach (var item in _bloatwareList)
            {
                var cb = new CheckBox
                {
                    Content = new TextBlock
                    {
                        Text = item.Name + (item.Recommended ? " (Güvenli)" : ""),
                        ToolTip = item.Description,
                        Foreground = item.Recommended ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.White,
                        FontWeight = item.Recommended ? FontWeights.SemiBold : FontWeights.Normal
                    },
                    Margin = new Thickness(6, 3, 6, 3),
                    Tag = item
                };
                cb.Checked += (s, e) => _selectedApps.Add(item.Name);
                cb.Unchecked += (s, e) => _selectedApps.Remove(item.Name);

                BloatwareListPanel.Children.Add(cb);
            }
        }

        private void SelectAllItems()
        {
            foreach (var cb in BloatwareListPanel.Children.OfType<CheckBox>())
            {
                cb.IsChecked = true;
            }
        }

        private void SelectRecommendedItems()
        {
            foreach (var cb in BloatwareListPanel.Children.OfType<CheckBox>())
            {
                var item = cb.Tag as BloatwareItem;
                cb.IsChecked = item != null && item.Recommended;
            }
        }

        private void ClearSelection()
        {
            foreach (var cb in BloatwareListPanel.Children.OfType<CheckBox>())
            {
                cb.IsChecked = false;
            }
        }

        private async void RemoveSelectedBloatware_Click(object sender, RoutedEventArgs e)
        {
            var selected = BloatwareListPanel.Children.OfType<CheckBox>()
                .Where(cb => cb.IsChecked == true)
                .Select(cb => cb.Tag as BloatwareItem)
                .Where(x => x != null)
                .ToList();

            if (!selected.Any())
            {
                BloatwareLogTextBox.AppendText("Uyarı: Kaldırmak için en az bir uygulama seçin.\n");
                return;
            }

            if (MessageBox.Show($"{selected.Count} uygulama kaldırılacak, devam edilsin mi?", "Onay", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            foreach (var app in selected)
            {
                string log = await RunPowerShellDebloatWithNSudo(app!.Name, "Remove-BloatwareApp");
                BloatwareLogTextBox.AppendText(log + "\n");
                LogMessageToMain?.Invoke(log);
            }
            BloatwareLogTextBox.AppendText("Kaldırma işlemleri tamamlandı.\n");
        }

        private async void RemoveAllBloatware_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Tüm bloatware uygulamalarını kaldırmak istiyor musunuz?", "Onay", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            string log = await RunPowerShellDebloatWithNSudo(null, "Remove-AllBloatware");
            BloatwareLogTextBox.AppendText(log + "\n");
            LogMessageToMain?.Invoke(log);
            BloatwareLogTextBox.AppendText("Tüm bloatware kaldırma işlemi tamamlandı.\n");
        }

        private async void RestoreBloatware_Click(object sender, RoutedEventArgs e)
        {
            var selected = BloatwareListPanel.Children.OfType<CheckBox>()
                .Where(cb => cb.IsChecked == true)
                .Select(cb => cb.Tag as BloatwareItem)
                .Where(x => x != null)
                .ToList();

            if (!selected.Any())
            {
                BloatwareLogTextBox.AppendText("Uyarı: Geri almak için en az bir uygulama seçin.\n");
                return;
            }

            if (MessageBox.Show($"{selected.Count} uygulama geri alınacak, devam edilsin mi?", "Onay", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            foreach (var app in selected)
            {
                string log = await RunPowerShellDebloatWithNSudo(app!.Name, "Restore-BloatwareApp");
                BloatwareLogTextBox.AppendText(log + "\n");
                LogMessageToMain?.Invoke(log);
            }
            BloatwareLogTextBox.AppendText("Geri alma işlemleri tamamlandı.\n");
        }

        private async Task<string> RunPowerShellDebloatWithNSudo(string? appName, string commandName)
        {
            // Doğru NSudo yolu:
            string nsudoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "nsudo", "NSudo.exe");
            string modulePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "DebloatCore.psm1");

            if (!File.Exists(modulePath) || !File.Exists(nsudoPath))
                return $"Hata: DebloatCore.psm1 veya NSudo.exe bulunamadı. Yol: {modulePath}, {nsudoPath}";

            string script = $"Import-Module '{modulePath}'; ";
            if (!string.IsNullOrEmpty(appName))
                script += $"{commandName} -AppName '{appName}'";
            else
                script += commandName;

            var psi = new ProcessStartInfo
            {
                FileName = nsudoPath,
                Arguments = $"-U:S -P:E -M:S -UseCurrentConsole powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (var process = new Process { StartInfo = psi })
                {
                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                        return string.IsNullOrEmpty(output) ? "İşlem başarılı." : output;
                    else
                        return $"Hata: {error} (Exit Code: {process.ExitCode})";
                }
            }
            catch (Exception ex)
            {
                return $"Hata: {ex.Message}";
            }
        }
    }
}
